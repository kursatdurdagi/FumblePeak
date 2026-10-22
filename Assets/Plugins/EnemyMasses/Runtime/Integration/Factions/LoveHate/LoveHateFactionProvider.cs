using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Optional Love/Hate bridge provider.
    /// Uses reflection to avoid compile-time coupling to PixelCrushers assemblies.
    /// </summary>
    public sealed class LoveHateFactionProvider :
        ILoveHateFactionAdapter,
        IFactionRelationshipEditor,
        ILoveHateFactionSettingsReceiver
    {
        private const string FactionManagerTypeName = "PixelCrushers.LoveHate.FactionManager";
        private const string FactionMemberTypeName = "PixelCrushers.LoveHate.FactionMember";

        private EnemyMassesFactionProvider _fallbackProvider;

        private readonly Dictionary<EnemyCrowd, int> _enemyCrowdToFactionId = new();
        private readonly Dictionary<int, EnemyCrowd> _factionIdToEnemyCrowd = new();

        private Type _factionManagerType;
        private Type _factionMemberType;
        private PropertyInfo _managerInstanceProperty;
        private MethodInfo _managerGetAffinityMethod;
        private MethodInfo _managerSetPersonalAffinityMethod;
        private MethodInfo _managerModifyPersonalAffinityMethod;
        private FieldInfo _memberFactionIdField;
        private FieldInfo _memberFactionManagerField;
        private MethodInfo _memberRegisterMethod;
        private MethodInfo _memberUnregisterMethod;
        private MethodInfo _memberGetAffinityMethod;

        private bool _hasMappings;

        public string ProviderId => "lovehate";
        public string DisplayName => "Love/Hate (EnemyMasses Bridge)";
        public int Priority => 150;
        public bool IsAvailable
        {
            get
            {
                CacheReflectionMembers();
                return _factionManagerType != null && _factionMemberType != null && _hasMappings;
            }
        }

        public float AllyThreshold { get; set; } = 25f;
        public float EnemyThreshold { get; set; } = -50f;

        public void ApplySettings(LoveHateIntegrationSettings settings)
        {
            if (settings == null)
                return;

            AllyThreshold = settings.allyThreshold;
            EnemyThreshold = settings.enemyThreshold;

            _enemyCrowdToFactionId.Clear();
            _factionIdToEnemyCrowd.Clear();

            if (settings.factionMappings != null)
            {
                for (int i = 0; i < settings.factionMappings.Count; i++)
                {
                    LoveHateFactionMapping mapping = settings.factionMappings[i];
                    if (mapping?.enemyCrowd == null || mapping.loveHateFactionId < 0)
                        continue;

                    MapFactionToLoveHate(mapping.enemyCrowd, mapping.loveHateFactionId);
                }
            }

            _hasMappings = _enemyCrowdToFactionId.Count > 0;
        }

        public void Initialize()
        {
            _fallbackProvider = FactionSystemManager.Instance.GetBuiltInProvider();
            _fallbackProvider.Initialize();
            CacheReflectionMembers();
        }

        public void Shutdown()
        {
            _enemyCrowdToFactionId.Clear();
            _factionIdToEnemyCrowd.Clear();
            _hasMappings = false;
        }

        public RelationshipType GetRelationship(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            if (sourceAgent == null || targetAgent == null)
                return RelationshipType.Neutral;

            if (sourceAgent.index == targetAgent.index)
                return RelationshipType.Self;

            if (TryGetAffinity(sourceAgent, targetAgent, out float affinity))
                return ConvertAffinityToRelationship(affinity);

            return GetFallback().GetRelationship(sourceAgent, targetAgent);
        }

        public RelationshipType GetRelationship(int sourceAgentIndex, int targetAgentIndex)
        {
            EnemyMassesCrowdController controller = EnemyMassesCrowdController.Instance;
            if (controller == null)
                return RelationshipType.Neutral;

            CrowdAgent source = controller.GetAgent(sourceAgentIndex);
            CrowdAgent target = controller.GetAgent(targetAgentIndex);
            return GetRelationship(source, target);
        }

        public RelationshipType GetRelationshipToExternal(CrowdAgent sourceAgent, GameObject targetObject)
        {
            if (sourceAgent == null || targetObject == null)
                return RelationshipType.Neutral;

            if (TryGetSourceFactionId(sourceAgent, out int sourceFactionId) &&
                TryGetTargetFactionMemberId(targetObject, out int targetFactionId) &&
                TryGetAffinity(sourceFactionId, targetFactionId, out float affinity))
            {
                return ConvertAffinityToRelationship(affinity);
            }

            return GetFallback().GetRelationshipToExternal(sourceAgent, targetObject);
        }

        public RelationshipType GetFactionRelationship(EnemyCrowd sourceFaction, EnemyCrowd targetFaction)
        {
            if (sourceFaction == null || targetFaction == null)
                return RelationshipType.Neutral;

            if (sourceFaction == targetFaction)
                return RelationshipType.Self;

            if (TryGetSourceFactionId(sourceFaction, out int sourceFactionId) &&
                TryGetSourceFactionId(targetFaction, out int targetFactionId) &&
                TryGetAffinity(sourceFactionId, targetFactionId, out float affinity))
            {
                return ConvertAffinityToRelationship(affinity);
            }

            return GetFallback().GetFactionRelationship(sourceFaction, targetFaction);
        }

        public RelationshipType GetFactionRelationship(int sourceFactionIndex, int targetFactionIndex)
        {
            EnemyMassesCrowdController controller = EnemyMassesCrowdController.Instance;
            if (controller == null)
                return RelationshipType.Neutral;

            EnemyCrowd sourceFaction = controller.GetEnemyCrowdAtIndex(sourceFactionIndex);
            EnemyCrowd targetFaction = controller.GetEnemyCrowdAtIndex(targetFactionIndex);
            return GetFactionRelationship(sourceFaction, targetFaction);
        }

        public RelationshipType GetPlayerRelationship(CrowdAgent sourceAgent)
        {
            return GetFallback().GetPlayerRelationship(sourceAgent);
        }

        public RelationshipType GetPlayerRelationship(EnemyCrowd sourceFaction)
        {
            return GetFallback().GetPlayerRelationship(sourceFaction);
        }

        public bool ShouldAttack(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return GetRelationship(sourceAgent, targetAgent).IsHostile();
        }

        public bool ShouldSupport(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return GetRelationship(sourceAgent, targetAgent).IsFriendly();
        }

        public int GetOwnerPlayerId(CrowdAgent agent)
        {
            return GetFallback().GetOwnerPlayerId(agent);
        }

        public void SetOwnerPlayerId(CrowdAgent agent, int playerId)
        {
            GetFallback().SetOwnerPlayerId(agent, playerId);
        }

        public bool HaveSameOwner(CrowdAgent agentA, CrowdAgent agentB)
        {
            return GetFallback().HaveSameOwner(agentA, agentB);
        }

        public IEnumerable<CrowdAgent> GetAgentsByOwner(int playerId)
        {
            return GetFallback().GetAgentsByOwner(playerId);
        }

        public IEnumerable<CrowdAgent> GetAgentsByRelationship(CrowdAgent sourceAgent, RelationshipFlags relationshipFlags)
        {
            EnemyMassesCrowdController controller = EnemyMassesCrowdController.Instance;
            if (controller == null || sourceAgent == null)
                yield break;

            List<CrowdAgent> agents = controller.GetAgents();
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent candidate = agents[i];
                if (candidate == null || candidate == sourceAgent)
                    continue;

                RelationshipType relationship = GetRelationship(sourceAgent, candidate);
                if (relationship.MatchesFlags(relationshipFlags))
                    yield return candidate;
            }
        }

        public RelationshipType ConvertAffinityToRelationship(float affinity)
        {
            if (affinity >= AllyThreshold)
                return RelationshipType.Ally;

            if (affinity <= EnemyThreshold)
                return RelationshipType.Enemy;

            return RelationshipType.Neutral;
        }

        public void RegisterAgentWithLoveHate(CrowdAgent agent, int factionId)
        {
            if (agent?.transform == null)
                return;

            CacheReflectionMembers();
            if (_factionMemberType == null || _memberFactionIdField == null)
                return;

            Component member = agent.transform.GetComponent(_factionMemberType);
            if (member == null)
                member = agent.transform.gameObject.AddComponent(_factionMemberType);

            _memberFactionIdField.SetValue(member, factionId);

            if (_memberFactionManagerField != null)
            {
                object manager = GetLoveHateManager();
                if (manager != null)
                    _memberFactionManagerField.SetValue(member, manager);
            }

            _memberRegisterMethod?.Invoke(member, null);
        }

        public void UnregisterAgentFromLoveHate(CrowdAgent agent)
        {
            if (agent?.transform == null)
                return;

            CacheReflectionMembers();
            if (_factionMemberType == null)
                return;

            Component member = agent.transform.GetComponent(_factionMemberType);
            if (member == null)
                return;

            _memberUnregisterMethod?.Invoke(member, null);
        }

        public void ReportDeed(CrowdAgent actorAgent, CrowdAgent targetAgent, string deedTag, float impact)
        {
            CacheReflectionMembers();
            if (_managerModifyPersonalAffinityMethod == null)
                return;

            object manager = GetLoveHateManager();
            if (manager == null)
                return;

            if (!TryGetSourceFactionId(actorAgent, out int actorFactionId))
                return;

            if (!TryGetSourceFactionId(targetAgent, out int targetFactionId))
                return;

            _managerModifyPersonalAffinityMethod.Invoke(manager, new object[] { actorFactionId, targetFactionId, impact });
        }

        public int GetLoveHateFactionId(EnemyCrowd crowd)
        {
            if (crowd == null)
                return -1;

            return _enemyCrowdToFactionId.TryGetValue(crowd, out int factionId) ? factionId : -1;
        }

        public void MapFactionToLoveHate(EnemyCrowd crowd, int loveHateFactionId)
        {
            if (crowd == null || loveHateFactionId < 0)
                return;

            _enemyCrowdToFactionId[crowd] = loveHateFactionId;
            _factionIdToEnemyCrowd[loveHateFactionId] = crowd;
            _hasMappings = _enemyCrowdToFactionId.Count > 0;
        }

        public bool SetFactionRelationship(string sourceFactionName, string targetFactionName, RelationshipType relationship, bool bidirectional = false)
        {
            if (!TryGetSourceFactionId(sourceFactionName, out int sourceId))
                return GetFallback().SetFactionRelationship(sourceFactionName, targetFactionName, relationship, bidirectional);

            if (!TryGetSourceFactionId(targetFactionName, out int targetId))
                return GetFallback().SetFactionRelationship(sourceFactionName, targetFactionName, relationship, bidirectional);

            if (!TrySetPersonalAffinity(sourceId, targetId, ConvertRelationshipToAffinity(relationship)))
                return false;

            if (bidirectional && !TrySetPersonalAffinity(targetId, sourceId, ConvertRelationshipToAffinity(relationship)))
                return false;

            FactionSystemManager.Instance.NotifyRelationshipsChanged();
            return true;
        }

        public bool SetFactionDefaultRelationship(string factionName, RelationshipType relationship)
        {
            return GetFallback().SetFactionDefaultRelationship(factionName, relationship);
        }

        public bool SetFactionPlayerRelationship(string factionName, RelationshipType relationship)
        {
            return GetFallback().SetFactionPlayerRelationship(factionName, relationship);
        }

        public bool SetSameFactionDifferentOwnerRelationship(string factionName, RelationshipType relationship)
        {
            return GetFallback().SetSameFactionDifferentOwnerRelationship(factionName, relationship);
        }

        public bool RemoveFactionRelationship(string sourceFactionName, string targetFactionName, bool bidirectional = false)
        {
            bool removed = SetFactionRelationship(sourceFactionName, targetFactionName, RelationshipType.Neutral, bidirectional);
            if (!removed)
                removed = GetFallback().RemoveFactionRelationship(sourceFactionName, targetFactionName, bidirectional);

            return removed;
        }

        private EnemyMassesFactionProvider GetFallback()
        {
            if (_fallbackProvider == null)
            {
                _fallbackProvider = FactionSystemManager.Instance.GetBuiltInProvider();
                _fallbackProvider.Initialize();
            }

            return _fallbackProvider;
        }

        private void CacheReflectionMembers()
        {
            if (_factionManagerType != null && _factionMemberType != null)
                return;

            _factionManagerType = FindType(FactionManagerTypeName);
            _factionMemberType = FindType(FactionMemberTypeName);

            if (_factionManagerType == null || _factionMemberType == null)
                return;

            _managerInstanceProperty = _factionManagerType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            _managerGetAffinityMethod = _factionManagerType.GetMethod("GetAffinity", new[] { typeof(int), typeof(int) });
            _managerSetPersonalAffinityMethod = _factionManagerType.GetMethod("SetPersonalAffinity", new[] { typeof(int), typeof(int), typeof(float) });
            _managerModifyPersonalAffinityMethod = _factionManagerType.GetMethod("ModifyPersonalAffinity", new[] { typeof(int), typeof(int), typeof(float) });

            _memberFactionIdField = _factionMemberType.GetField("factionID", BindingFlags.Public | BindingFlags.Instance);
            _memberFactionManagerField = _factionMemberType.GetField("factionManager", BindingFlags.Public | BindingFlags.Instance);
            _memberRegisterMethod = _factionMemberType.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Instance);
            _memberUnregisterMethod = _factionMemberType.GetMethod("Unregister", BindingFlags.NonPublic | BindingFlags.Instance);
            _memberGetAffinityMethod = _factionMemberType.GetMethod("GetAffinity", new[] { _factionMemberType });
        }

        private bool TryGetAffinity(CrowdAgent sourceAgent, CrowdAgent targetAgent, out float affinity)
        {
            affinity = 0f;

            // Fast path: mapped faction ids.
            if (TryGetSourceFactionId(sourceAgent, out int sourceFactionId) &&
                TryGetSourceFactionId(targetAgent, out int targetFactionId) &&
                TryGetAffinity(sourceFactionId, targetFactionId, out affinity))
            {
                return true;
            }

            // Optional path: direct FactionMember components.
            CacheReflectionMembers();
            if (_memberGetAffinityMethod == null || _factionMemberType == null)
                return false;

            Component sourceMember = sourceAgent.transform?.GetComponent(_factionMemberType);
            Component targetMember = targetAgent.transform?.GetComponent(_factionMemberType);
            if (sourceMember == null || targetMember == null)
                return false;

            object result = _memberGetAffinityMethod.Invoke(sourceMember, new object[] { targetMember });
            affinity = result is float floatValue ? floatValue : 0f;
            return true;
        }

        private bool TryGetAffinity(int sourceFactionId, int targetFactionId, out float affinity)
        {
            affinity = 0f;
            CacheReflectionMembers();

            object manager = GetLoveHateManager();
            if (manager == null || _managerGetAffinityMethod == null)
                return false;

            object result = _managerGetAffinityMethod.Invoke(manager, new object[] { sourceFactionId, targetFactionId });
            affinity = result is float floatValue ? floatValue : 0f;
            return true;
        }

        private bool TrySetPersonalAffinity(int sourceFactionId, int targetFactionId, float affinity)
        {
            CacheReflectionMembers();
            object manager = GetLoveHateManager();
            if (manager == null || _managerSetPersonalAffinityMethod == null)
                return false;

            _managerSetPersonalAffinityMethod.Invoke(manager, new object[] { sourceFactionId, targetFactionId, affinity });
            return true;
        }

        private object GetLoveHateManager()
        {
            CacheReflectionMembers();
            return _managerInstanceProperty?.GetValue(null);
        }

        private bool TryGetSourceFactionId(CrowdAgent agent, out int factionId)
        {
            factionId = -1;
            if (agent?.enemyCrowd == null)
                return false;

            return TryGetSourceFactionId(agent.enemyCrowd, out factionId);
        }

        private bool TryGetSourceFactionId(EnemyCrowd crowd, out int factionId)
        {
            factionId = -1;
            if (crowd == null)
                return false;

            if (_enemyCrowdToFactionId.TryGetValue(crowd, out int mappedId))
            {
                factionId = mappedId;
                return true;
            }

            return false;
        }

        private bool TryGetSourceFactionId(string factionName, out int factionId)
        {
            factionId = -1;
            if (string.IsNullOrWhiteSpace(factionName))
                return false;

            EnemyCrowd crowd = GetFallback().GetFactionByName(factionName);
            return TryGetSourceFactionId(crowd, out factionId);
        }

        private bool TryGetTargetFactionMemberId(GameObject targetObject, out int factionId)
        {
            factionId = -1;
            CacheReflectionMembers();

            if (_factionMemberType == null || _memberFactionIdField == null || targetObject == null)
                return false;

            Component member = targetObject.GetComponent(_factionMemberType);
            if (member == null)
                return false;

            object value = _memberFactionIdField.GetValue(member);
            if (value is int id)
            {
                factionId = id;
                return true;
            }

            return false;
        }

        private float ConvertRelationshipToAffinity(RelationshipType relationship)
        {
            return relationship switch
            {
                RelationshipType.Self => 100f,
                RelationshipType.Ally => Mathf.Max(AllyThreshold, 40f),
                RelationshipType.Enemy => Mathf.Min(EnemyThreshold, -40f),
                _ => 0f
            };
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }

    internal static class LoveHateFactionProviderBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterProvider()
        {
            FactionSystemManager manager = FactionSystemManager.Instance;
            if (manager.IsProviderRegistered("lovehate"))
                return;

            manager.RegisterProvider(new LoveHateFactionProvider());
        }
    }
}
