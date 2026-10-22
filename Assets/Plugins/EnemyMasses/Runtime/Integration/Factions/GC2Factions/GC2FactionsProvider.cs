using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Optional Ninjutsu GC2 Factions bridge provider.
    /// Uses reflection to avoid compile-time coupling and keep EnemyMasses modular.
    /// </summary>
    public sealed class GC2FactionsProvider :
        IGC2FactionAdapter,
        IFactionRelationshipEditor,
        IGC2FactionSettingsReceiver
    {
        private const string MemberTypeName = "NinjutsuGames.Runtime.Factions.Member";
        private const string FactionTypeName = "NinjutsuGames.Runtime.Factions.Faction";
        private const string FactionStanceTypeName = "NinjutsuGames.Runtime.Factions.FactionStance";
        private const string FactionsRepositoryTypeName = "NinjutsuGames.Runtime.Factions.FactionsRepository";

        private EnemyMassesFactionProvider _fallbackProvider;

        private readonly Dictionary<EnemyCrowd, UnityEngine.Object> _enemyCrowdToFactionAsset = new();
        private readonly Dictionary<EnemyCrowd, string> _enemyCrowdToFactionId = new();
        private readonly Dictionary<string, RelationshipType> _stanceToRelationship = new(StringComparer.OrdinalIgnoreCase)
        {
            { "enemy", RelationshipType.Enemy },
            { "hostile", RelationshipType.Enemy },
            { "neutral", RelationshipType.Neutral },
            { "friendly", RelationshipType.Ally },
            { "ally", RelationshipType.Ally },
            { "self", RelationshipType.Self },
        };

        private Type _memberType;
        private Type _factionType;
        private Type _factionStanceType;
        private Type _factionsRepositoryType;

        private MethodInfo _memberHighestStatusToMemberMethod;
        private MethodInfo _memberHighestStatusToFactionMethod;
        private MethodInfo _memberJoinFactionMethod;
        private MethodInfo _memberLeaveFactionMethod;
        private PropertyInfo _memberFactionsProperty;

        private MethodInfo _factionGetStatusTowardsMethod;
        private MethodInfo _factionSetRelationshipStatusMethod;
        private PropertyInfo _factionNameProperty;
        private PropertyInfo _factionUniqueIdProperty;

        private PropertyInfo _stanceKeyProperty;

        private PropertyInfo _repositoryGetProperty;
        private PropertyInfo _repositoryFactionsProperty;
        private MethodInfo _globalFactionsGetByStringMethod;

        private bool _hasMappings;

        public string ProviderId => "gc2factions";
        public string DisplayName => "GC2 Factions (EnemyMasses Bridge)";
        public int Priority => 140;
        public bool IsAvailable
        {
            get
            {
                CacheReflectionMembers();
                return _memberType != null && _factionType != null && _hasMappings;
            }
        }

        public void ApplySettings(GC2FactionIntegrationSettings settings)
        {
            if (settings == null)
                return;

            _stanceToRelationship.Clear();
            if (settings.stanceMappings != null && settings.stanceMappings.Count > 0)
            {
                for (int i = 0; i < settings.stanceMappings.Count; i++)
                {
                    StanceMapping mapping = settings.stanceMappings[i];
                    if (mapping == null || string.IsNullOrWhiteSpace(mapping.stanceKey))
                        continue;

                    _stanceToRelationship[mapping.stanceKey] = mapping.relationship;
                }
            }
            else
            {
                _stanceToRelationship["enemy"] = RelationshipType.Enemy;
                _stanceToRelationship["hostile"] = RelationshipType.Enemy;
                _stanceToRelationship["neutral"] = RelationshipType.Neutral;
                _stanceToRelationship["friendly"] = RelationshipType.Ally;
                _stanceToRelationship["ally"] = RelationshipType.Ally;
                _stanceToRelationship["self"] = RelationshipType.Self;
            }

            _enemyCrowdToFactionAsset.Clear();
            _enemyCrowdToFactionId.Clear();

            if (settings.factionMappings != null)
            {
                for (int i = 0; i < settings.factionMappings.Count; i++)
                {
                    GC2FactionMapping mapping = settings.factionMappings[i];
                    if (mapping?.enemyCrowd == null)
                        continue;

                    if (mapping.gc2FactionAsset != null)
                        MapFactionToGC2(mapping.enemyCrowd, mapping.gc2FactionAsset);

                    if (!string.IsNullOrWhiteSpace(mapping.gc2FactionId))
                        _enemyCrowdToFactionId[mapping.enemyCrowd] = mapping.gc2FactionId;
                }
            }

            _hasMappings = _enemyCrowdToFactionAsset.Count > 0 || _enemyCrowdToFactionId.Count > 0;
        }

        public void Initialize()
        {
            _fallbackProvider = FactionSystemManager.Instance.GetBuiltInProvider();
            _fallbackProvider.Initialize();
            CacheReflectionMembers();
        }

        public void Shutdown()
        {
            _enemyCrowdToFactionAsset.Clear();
            _enemyCrowdToFactionId.Clear();
            _hasMappings = false;
        }

        public RelationshipType GetRelationship(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            if (sourceAgent == null || targetAgent == null)
                return RelationshipType.Neutral;

            if (sourceAgent.index == targetAgent.index)
                return RelationshipType.Self;

            if (TryGetStanceFromMembers(sourceAgent, targetAgent, out string stanceKey))
                return ConvertStanceToRelationship(stanceKey);

            if (TryGetStanceFromMappedFactions(sourceAgent.enemyCrowd, targetAgent.enemyCrowd, out stanceKey))
                return ConvertStanceToRelationship(stanceKey);

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

            CacheReflectionMembers();
            if (_memberType != null && _memberHighestStatusToFactionMethod != null)
            {
                Component targetMember = targetObject.GetComponent(_memberType);
                object sourceFaction = ResolveMappedFaction(sourceAgent.enemyCrowd);
                if (targetMember != null && sourceFaction != null)
                {
                    object stance = _memberHighestStatusToFactionMethod.Invoke(targetMember, new[] { sourceFaction });
                    string stanceKey = ExtractStanceKey(stance);
                    if (!string.IsNullOrWhiteSpace(stanceKey))
                        return ConvertStanceToRelationship(stanceKey);
                }
            }

            return GetFallback().GetRelationshipToExternal(sourceAgent, targetObject);
        }

        public RelationshipType GetFactionRelationship(EnemyCrowd sourceFaction, EnemyCrowd targetFaction)
        {
            if (sourceFaction == null || targetFaction == null)
                return RelationshipType.Neutral;

            if (sourceFaction == targetFaction)
                return RelationshipType.Self;

            if (TryGetStanceFromMappedFactions(sourceFaction, targetFaction, out string stanceKey))
                return ConvertStanceToRelationship(stanceKey);

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

        public RelationshipType ConvertStanceToRelationship(string stanceKey)
        {
            if (string.IsNullOrWhiteSpace(stanceKey))
                return RelationshipType.Neutral;

            if (_stanceToRelationship.TryGetValue(stanceKey, out RelationshipType mapped))
                return mapped;

            string lowered = stanceKey.ToLowerInvariant();
            if (lowered.Contains("enemy") || lowered.Contains("hostile"))
                return RelationshipType.Enemy;

            if (lowered.Contains("ally") || lowered.Contains("friendly"))
                return RelationshipType.Ally;

            if (lowered.Contains("self"))
                return RelationshipType.Self;

            return RelationshipType.Neutral;
        }

        public UnityEngine.Object GetGC2Faction(EnemyCrowd crowd)
        {
            if (crowd == null)
                return null;

            return _enemyCrowdToFactionAsset.TryGetValue(crowd, out UnityEngine.Object gc2Faction) ? gc2Faction : null;
        }

        public string GetGC2FactionId(EnemyCrowd crowd)
        {
            if (crowd == null)
                return string.Empty;

            return _enemyCrowdToFactionId.TryGetValue(crowd, out string gc2FactionId) ? gc2FactionId : string.Empty;
        }

        public void MapFactionToGC2(EnemyCrowd crowd, UnityEngine.Object gc2Faction)
        {
            if (crowd == null || gc2Faction == null)
                return;

            _enemyCrowdToFactionAsset[crowd] = gc2Faction;
            string factionId = ExtractFactionId(gc2Faction);
            if (!string.IsNullOrWhiteSpace(factionId))
                _enemyCrowdToFactionId[crowd] = factionId;

            _hasMappings = _enemyCrowdToFactionAsset.Count > 0 || _enemyCrowdToFactionId.Count > 0;
        }

        public void RegisterAgentWithGC2(CrowdAgent agent, string gc2FactionId)
        {
            if (agent?.transform == null || string.IsNullOrWhiteSpace(gc2FactionId))
                return;

            CacheReflectionMembers();
            if (_memberType == null || _memberJoinFactionMethod == null)
                return;

            Component member = agent.transform.GetComponent(_memberType);
            if (member == null)
                member = agent.transform.gameObject.AddComponent(_memberType);

            object faction = ResolveFactionById(gc2FactionId);
            if (faction == null)
                return;

            _memberJoinFactionMethod.Invoke(member, new object[] { faction, 0, true });
        }

        public void UnregisterAgentFromGC2(CrowdAgent agent)
        {
            if (agent?.transform == null)
                return;

            CacheReflectionMembers();
            if (_memberType == null || _memberLeaveFactionMethod == null || _memberFactionsProperty == null)
                return;

            Component member = agent.transform.GetComponent(_memberType);
            if (member == null)
                return;

            object factionsValue = _memberFactionsProperty.GetValue(member);
            if (factionsValue is not IList factions)
                return;

            object[] factionSnapshot = new object[factions.Count];
            factions.CopyTo(factionSnapshot, 0);

            for (int i = 0; i < factionSnapshot.Length; i++)
            {
                object faction = factionSnapshot[i];
                if (faction == null)
                    continue;

                _memberLeaveFactionMethod.Invoke(member, new[] { faction });
            }
        }

        public void ModifyReputation(EnemyCrowd fromCrowd, EnemyCrowd toCrowd, int reputationChange)
        {
            if (fromCrowd == null || toCrowd == null || reputationChange == 0)
                return;

            if (reputationChange > 0)
            {
                SetFactionRelationship(fromCrowd.factionName, toCrowd.factionName, RelationshipType.Ally);
            }
            else
            {
                SetFactionRelationship(fromCrowd.factionName, toCrowd.factionName, RelationshipType.Enemy);
            }
        }

        public int GetReputation(EnemyCrowd fromCrowd, EnemyCrowd toCrowd)
        {
            // GC2 Factions stance is key-based, not directly exposed as a single integer reputation per faction pair.
            return 0;
        }

        public string GetStance(EnemyCrowd fromCrowd, EnemyCrowd toCrowd)
        {
            return TryGetStanceFromMappedFactions(fromCrowd, toCrowd, out string stanceKey)
                ? stanceKey
                : string.Empty;
        }

        public bool SetFactionRelationship(string sourceFactionName, string targetFactionName, RelationshipType relationship, bool bidirectional = false)
        {
            EnemyCrowd sourceCrowd = GetFallback().GetFactionByName(sourceFactionName);
            EnemyCrowd targetCrowd = GetFallback().GetFactionByName(targetFactionName);
            if (sourceCrowd == null || targetCrowd == null)
                return GetFallback().SetFactionRelationship(sourceFactionName, targetFactionName, relationship, bidirectional);

            bool setForward = TrySetRelationshipBetweenMappedFactions(sourceCrowd, targetCrowd, relationship);
            if (!setForward)
                return GetFallback().SetFactionRelationship(sourceFactionName, targetFactionName, relationship, bidirectional);

            if (bidirectional && !TrySetRelationshipBetweenMappedFactions(targetCrowd, sourceCrowd, relationship))
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
            // GC2 Factions relationships are explicit stance keys. "Neutral" is the practical reset default.
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
            if (_memberType != null && _factionType != null)
                return;

            _memberType = FindType(MemberTypeName);
            _factionType = FindType(FactionTypeName);
            _factionStanceType = FindType(FactionStanceTypeName);
            _factionsRepositoryType = FindType(FactionsRepositoryTypeName);

            if (_memberType == null || _factionType == null)
                return;

            _memberHighestStatusToMemberMethod = _memberType.GetMethod("HighestStatusToMember", new[] { _memberType });
            _memberHighestStatusToFactionMethod = _memberType.GetMethod("HighestStatusToFaction", new[] { _factionType });
            _memberJoinFactionMethod = _memberType.GetMethod("JoinFaction", new[] { _factionType, typeof(int), typeof(bool) });
            _memberLeaveFactionMethod = _memberType.GetMethod("LeaveFaction", new[] { _factionType });
            _memberFactionsProperty = _memberType.GetProperty("Factions", BindingFlags.Public | BindingFlags.Instance);

            _factionGetStatusTowardsMethod = _factionType.GetMethod("GetStatusTowards", new[] { _factionType });
            _factionSetRelationshipStatusMethod = _factionType.GetMethod("SetRelationshipStatus", new[] { _factionType, typeof(string), typeof(bool) });
            _factionNameProperty = _factionType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            _factionUniqueIdProperty = _factionType.GetProperty("UniqueID", BindingFlags.Public | BindingFlags.Instance);

            if (_factionStanceType != null)
            {
                _stanceKeyProperty = _factionStanceType.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
            }

            if (_factionsRepositoryType != null)
            {
                _repositoryGetProperty = _factionsRepositoryType.GetProperty("Get", BindingFlags.Public | BindingFlags.Static);
                _repositoryFactionsProperty = _factionsRepositoryType.GetProperty("Factions", BindingFlags.Public | BindingFlags.Instance);
            }
        }

        private bool TryGetStanceFromMembers(CrowdAgent sourceAgent, CrowdAgent targetAgent, out string stanceKey)
        {
            stanceKey = string.Empty;
            CacheReflectionMembers();
            if (_memberType == null || _memberHighestStatusToMemberMethod == null)
                return false;

            Component sourceMember = sourceAgent?.transform?.GetComponent(_memberType);
            Component targetMember = targetAgent?.transform?.GetComponent(_memberType);
            if (sourceMember == null || targetMember == null)
                return false;

            object stance = _memberHighestStatusToMemberMethod.Invoke(sourceMember, new object[] { targetMember });
            stanceKey = ExtractStanceKey(stance);
            return !string.IsNullOrWhiteSpace(stanceKey);
        }

        private bool TryGetStanceFromMappedFactions(EnemyCrowd sourceCrowd, EnemyCrowd targetCrowd, out string stanceKey)
        {
            stanceKey = string.Empty;
            CacheReflectionMembers();
            if (_factionGetStatusTowardsMethod == null)
                return false;

            object sourceFaction = ResolveMappedFaction(sourceCrowd);
            object targetFaction = ResolveMappedFaction(targetCrowd);
            if (sourceFaction == null || targetFaction == null)
                return false;

            object stance = _factionGetStatusTowardsMethod.Invoke(sourceFaction, new[] { targetFaction });
            stanceKey = stance as string ?? string.Empty;
            return !string.IsNullOrWhiteSpace(stanceKey);
        }

        private bool TrySetRelationshipBetweenMappedFactions(EnemyCrowd sourceCrowd, EnemyCrowd targetCrowd, RelationshipType relationship)
        {
            CacheReflectionMembers();
            if (_factionSetRelationshipStatusMethod == null)
                return false;

            object sourceFaction = ResolveMappedFaction(sourceCrowd);
            object targetFaction = ResolveMappedFaction(targetCrowd);
            if (sourceFaction == null || targetFaction == null)
                return false;

            string stance = ConvertRelationshipToStance(relationship);
            _factionSetRelationshipStatusMethod.Invoke(sourceFaction, new object[] { targetFaction, stance, true });
            return true;
        }

        private object ResolveMappedFaction(EnemyCrowd crowd)
        {
            if (crowd == null)
                return null;

            if (_enemyCrowdToFactionAsset.TryGetValue(crowd, out UnityEngine.Object mappedAsset) && mappedAsset != null)
                return mappedAsset;

            if (_enemyCrowdToFactionId.TryGetValue(crowd, out string factionId) && !string.IsNullOrWhiteSpace(factionId))
                return ResolveFactionById(factionId);

            return null;
        }

        private object ResolveFactionById(string factionIdOrName)
        {
            CacheReflectionMembers();
            if (_factionType == null || string.IsNullOrWhiteSpace(factionIdOrName))
                return null;

            // 1) Try repository GlobalFactions.GetByString(name)
            object repository = _repositoryGetProperty?.GetValue(null);
            if (repository != null && _repositoryFactionsProperty != null)
            {
                object globalFactions = _repositoryFactionsProperty.GetValue(repository);
                if (globalFactions != null)
                {
                    _globalFactionsGetByStringMethod ??= globalFactions.GetType().GetMethod("GetByString", new[] { typeof(string) });
                    if (_globalFactionsGetByStringMethod != null)
                    {
                        object faction = _globalFactionsGetByStringMethod.Invoke(globalFactions, new object[] { factionIdOrName });
                        if (faction != null)
                            return faction;
                    }
                }
            }

            // 2) Fallback: compare loaded faction assets by extracted id/name.
            UnityEngine.Object[] factions = Resources.FindObjectsOfTypeAll(_factionType);
            for (int i = 0; i < factions.Length; i++)
            {
                UnityEngine.Object faction = factions[i];
                if (faction == null)
                    continue;

                string factionId = ExtractFactionId(faction);
                if (!string.IsNullOrWhiteSpace(factionId) &&
                    string.Equals(factionId, factionIdOrName, StringComparison.OrdinalIgnoreCase))
                {
                    return faction;
                }
            }

            return null;
        }

        private string ExtractFactionId(UnityEngine.Object factionObject)
        {
            if (factionObject == null)
                return string.Empty;

            CacheReflectionMembers();
            if (_factionUniqueIdProperty != null)
            {
                object uniqueId = _factionUniqueIdProperty.GetValue(factionObject);
                if (uniqueId != null)
                {
                    PropertyInfo stringProperty = uniqueId.GetType().GetProperty("String", BindingFlags.Public | BindingFlags.Instance);
                    if (stringProperty != null)
                    {
                        object value = stringProperty.GetValue(uniqueId);
                        if (value is string s && !string.IsNullOrWhiteSpace(s))
                            return s;
                    }

                    string uniqueIdString = uniqueId.ToString();
                    if (!string.IsNullOrWhiteSpace(uniqueIdString))
                        return uniqueIdString;
                }
            }

            if (_factionNameProperty != null)
            {
                object nameValue = _factionNameProperty.GetValue(factionObject);
                if (nameValue is string name && !string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return factionObject.name;
        }

        private string ExtractStanceKey(object stance)
        {
            if (stance == null)
                return string.Empty;

            if (stance is string directKey)
                return directKey;

            if (_stanceKeyProperty != null && _factionStanceType != null && _factionStanceType.IsInstanceOfType(stance))
            {
                object key = _stanceKeyProperty.GetValue(stance);
                return key as string ?? string.Empty;
            }

            PropertyInfo keyProperty = stance.GetType().GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
            if (keyProperty != null)
            {
                object key = keyProperty.GetValue(stance);
                if (key is string keyString)
                    return keyString;
            }

            return string.Empty;
        }

        private string ConvertRelationshipToStance(RelationshipType relationship)
        {
            foreach (KeyValuePair<string, RelationshipType> pair in _stanceToRelationship)
            {
                if (pair.Value == relationship)
                    return pair.Key;
            }

            return relationship switch
            {
                RelationshipType.Enemy => "enemy",
                RelationshipType.Ally => "friendly",
                RelationshipType.Self => "self",
                _ => "neutral"
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

    internal static class GC2FactionsProviderBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterProvider()
        {
            FactionSystemManager manager = FactionSystemManager.Instance;
            if (manager.IsProviderRegistered("gc2factions"))
                return;

            manager.RegisterProvider(new GC2FactionsProvider());
        }
    }
}
