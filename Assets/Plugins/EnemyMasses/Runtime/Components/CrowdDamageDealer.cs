using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Arawn.EnemyMasses.Runtime.Factions;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// How targets are selected within the damage radius.
    /// </summary>
    public enum CrowdDamageTargetMode
    {
        /// <summary>Damage all agents found within the radius.</summary>
        AllInRadius,
        /// <summary>Damage only the single closest agent within the radius.</summary>
        ClosestOnly,
    }

    /// <summary>
    /// How factions are filtered when selecting damage targets.
    /// </summary>
    public enum CrowdDamageFactionFilter
    {
        /// <summary>Damage all factions regardless of relationship.</summary>
        AllFactions,
        /// <summary>Damage only agents hostile to this object (requires ExternalFactionIdentifier).</summary>
        HostileOnly,
        /// <summary>Damage only the listed factions (whitelist).</summary>
        OnlyListedFactions,
        /// <summary>Damage all factions except the listed ones (blacklist).</summary>
        ExcludeListedFactions,
    }

    /// <summary>
    /// Designer-friendly component for dealing damage to EnemyCrowd agents.
    /// 
    /// Attach to any GameObject (player weapon, trap, spell origin, environmental hazard, etc.)
    /// and call <see cref="DealDamage"/> from Animation Events, UnityEvents, or code.
    /// 
    /// Supports radius-based AoE, faction filtering, critical hits, cooldowns,
    /// and rich UnityEvent callbacks for VFX/audio/UI hooks.
    /// </summary>w
    [AddComponentMenu("EnemyMasses/Crowd Damage Dealer")]
    public class CrowdDamageDealer : MonoBehaviour
    {
        #region ── Damage Settings ──

        [Tooltip("Base damage amount applied to each target.")]
        public float baseDamage = 10f;

        [Tooltip("Random variance applied to base damage. Final = baseDamage ± Random(0, variance).")]
        [Min(0f)]
        public float damageVariance = 0f;

        [Tooltip("Treat as player attack (applies the global Player Damage Multiplier from EnemyMasses settings).")]
        public bool isPlayerAttack = true;

        [Tooltip("Allow agents to block this damage via their block chance.")]
        public bool allowBlock = true;

        [Tooltip("Force kill the agent regardless of remaining health.")]
        public bool forceKill = false;

        #endregion

        #region ── Critical Hits ──

        [Tooltip("Chance to land a critical hit (0 = never, 1 = always).")]
        [Range(0f, 1f)]
        public float criticalChance = 0f;

        [Tooltip("Damage multiplier applied on a critical hit.")]
        [Min(1f)]
        public float criticalMultiplier = 2f;

        #endregion

        #region ── Targeting ──

        [Tooltip("How targets are selected within the damage radius.")]
        public CrowdDamageTargetMode targetMode = CrowdDamageTargetMode.AllInRadius;

        [Tooltip("Radius for finding agents to damage.")]
        [Min(0.1f)]
        public float damageRadius = 3f;

        [Tooltip("Origin point for the damage area. If not assigned, uses this Transform.")]
        public Transform damageOrigin;

        [Tooltip("Maximum number of agents to damage per call. 0 = unlimited.")]
        [Min(0)]
        public int maxTargets = 0;

        #endregion

        #region ── Faction Filtering ──

        [Tooltip("How to filter which factions are valid damage targets.")]
        public CrowdDamageFactionFilter factionFilter = CrowdDamageFactionFilter.HostileOnly;

        [Tooltip("EnemyCrowd factions to include or exclude, depending on filter mode.")]
        public List<EnemyCrowd> factionList = new();

        #endregion

        #region ── Cooldown ──

        [Tooltip("Minimum seconds between damage calls. 0 = no cooldown.")]
        [Min(0f)]
        public float cooldown = 0f;

        #endregion

        #region ── Debug ──

        [Tooltip("Show damage radius gizmo in Scene view.")]
        public bool showGizmos = true;

        [Tooltip("Gizmo color for the damage radius.")]
        public Color gizmoColor = new(1f, 0.25f, 0f, 0.15f);

        #endregion

        #region ── Events ──

        [Tooltip("Fired when DealDamage is called and processing begins.")]
        public UnityEvent OnDamageBegin = new();

        [Tooltip("Fired after all damage processing completes.")]
        public UnityEvent OnDamageEnd = new();

        [Tooltip("Fired for each agent that receives damage. Parameter: agent index.")]
        public UnityEvent<int> OnAgentDamaged = new();

        [Tooltip("Fired when a damaged agent is killed. Parameter: agent index.")]
        public UnityEvent<int> OnAgentKilled = new();

        [Tooltip("Fired when a critical hit occurs.")]
        public UnityEvent OnCriticalHit = new();

        [Tooltip("Fired after all agents are processed. Parameter: total number of agents damaged.")]
        public UnityEvent<int> OnTotalAgentsDamaged = new();

        [Tooltip("Fired after all agents are processed. Parameter: total damage dealt.")]
        public UnityEvent<float> OnTotalDamageDealt = new();

        [Tooltip("Fired when no valid targets are found within the radius.")]
        public UnityEvent OnNoTargetsFound = new();

        #endregion

        #region ── Private State ──

        private float _lastDamageTime = float.NegativeInfinity;
        private ExternalFactionIdentifier _factionIdentifier;
        private HashSet<EnemyCrowd> _factionSet;

        // Editor tab tracking
        [HideInInspector] public int editorTab;

        #endregion

        #region ── Properties ──

        /// <summary>World-space origin for the damage area.</summary>
        public Vector3 Origin => damageOrigin != null ? damageOrigin.position : transform.position;

        /// <summary>Whether a cooldown is active preventing damage.</summary>
        public bool IsOnCooldown => cooldown > 0f && Time.time - _lastDamageTime < cooldown;

        /// <summary>Seconds remaining on cooldown (0 if off cooldown).</summary>
        public float CooldownRemaining => IsOnCooldown
            ? Mathf.Max(0f, cooldown - (Time.time - _lastDamageTime))
            : 0f;

        #endregion

        #region ── Unity Lifecycle ──

        private void Awake()
        {
            _factionIdentifier = GetComponentInParent<ExternalFactionIdentifier>();
            RebuildFactionSet();
        }

        private void OnValidate()
        {
            RebuildFactionSet();
        }

        #endregion

        #region ── Public API ──

        /// <summary>
        /// Deal damage to agents near the configured origin point.
        /// Call from Animation Events, UnityEvents, or code.
        /// </summary>
        public void DealDamage()
        {
            DealDamageAtPosition(Origin);
        }

        /// <summary>
        /// Deal damage to agents within radius of a specific world position.
        /// Useful for projectile impacts, spell target locations, etc.
        /// </summary>
        public void DealDamageAtPosition(Vector3 position)
        {
            if (IsOnCooldown) return;

            var controller = EnemyMassesCrowdController.Instance;
            if (controller == null)
            {
                Debug.LogWarning($"[CrowdDamageDealer] {name}: EnemyMassesCrowdController.Instance is null.", this);
                return;
            }

            _lastDamageTime = Time.time;
            OnDamageBegin.Invoke();

            // Gather agents in radius
            var agents = controller.GetCrowdAgentsInRadius(position, damageRadius);

            // Filter dead agents and apply faction filter
            FilterAgents(agents);

            // Apply target mode
            if (targetMode == CrowdDamageTargetMode.ClosestOnly && agents.Count > 1)
            {
                var closest = FindClosestAgent(agents, position);
                agents.Clear();
                if (closest != null) agents.Add(closest);
            }

            // Apply max targets cap (keep closest N)
            if (maxTargets > 0 && agents.Count > maxTargets)
            {
                SortByDistance(agents, position);
                agents.RemoveRange(maxTargets, agents.Count - maxTargets);
            }

            if (agents.Count == 0)
            {
                OnNoTargetsFound.Invoke();
                OnDamageEnd.Invoke();
                return;
            }

            // Apply damage to each target
            int totalDamaged = 0;
            float totalDamage = 0f;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead) continue;

                float damage = CalculateDamage(out bool isCritical);
                Vector3 attackDirection = (agent.WorldPosition - position).normalized;
                if (attackDirection.sqrMagnitude < 0.001f)
                    attackDirection = transform.forward;

                bool success = controller.TryApplyDamage(
                    agent.index,
                    damage,
                    allowBlock,
                    forceKill,
                    attackerForward: attackDirection,
                    attackerUp: Vector3.up,
                    attackerPosition: position,
                    attackerAgentIndex: -1,
                    isPlayerAttack: isPlayerAttack
                );

                if (success)
                {
                    totalDamaged++;
                    totalDamage += damage;
                    OnAgentDamaged.Invoke(agent.index);

                    if (isCritical) OnCriticalHit.Invoke();
                    if (agent.isDead) OnAgentKilled.Invoke(agent.index);
                }
            }

            if (totalDamaged > 0)
            {
                OnTotalAgentsDamaged.Invoke(totalDamaged);
                OnTotalDamageDealt.Invoke(totalDamage);
            }
            else
            {
                OnNoTargetsFound.Invoke();
            }

            OnDamageEnd.Invoke();
        }

        /// <summary>
        /// Deal damage to a specific agent by its index.
        /// Useful when you already know the target (e.g. from a raycast or selection system).
        /// </summary>
        public void DealDamageToAgentIndex(int agentIndex)
        {
            if (IsOnCooldown) return;

            var controller = EnemyMassesCrowdController.Instance;
            if (controller == null) return;

            var agent = controller.GetAgent(agentIndex);
            if (agent == null || agent.isDead)
            {
                OnNoTargetsFound.Invoke();
                return;
            }

            // Faction check
            if (!PassesFactionFilter(agent))
            {
                OnNoTargetsFound.Invoke();
                return;
            }

            _lastDamageTime = Time.time;
            OnDamageBegin.Invoke();

            float damage = CalculateDamage(out bool isCritical);
            Vector3 attackDirection = (agent.WorldPosition - Origin).normalized;
            if (attackDirection.sqrMagnitude < 0.001f)
                attackDirection = transform.forward;

            bool success = controller.TryApplyDamage(
                agentIndex,
                damage,
                allowBlock,
                forceKill,
                attackerForward: attackDirection,
                attackerUp: Vector3.up,
                attackerPosition: Origin,
                attackerAgentIndex: -1,
                isPlayerAttack: isPlayerAttack
            );

            if (success)
            {
                OnAgentDamaged.Invoke(agentIndex);
                OnTotalAgentsDamaged.Invoke(1);
                OnTotalDamageDealt.Invoke(damage);

                if (isCritical) OnCriticalHit.Invoke();
                if (agent.isDead) OnAgentKilled.Invoke(agentIndex);
            }
            else
            {
                OnNoTargetsFound.Invoke();
            }

            OnDamageEnd.Invoke();
        }

        /// <summary>
        /// Kill all agents within the damage radius that pass the faction filter.
        /// Bypasses damage calculation — instant death.
        /// </summary>
        public void KillAllInRadius()
        {
            if (IsOnCooldown) return;

            var controller = EnemyMassesCrowdController.Instance;
            if (controller == null) return;

            _lastDamageTime = Time.time;
            OnDamageBegin.Invoke();

            var agents = controller.GetCrowdAgentsInRadius(Origin, damageRadius);
            FilterAgents(agents);

            int killed = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead) continue;

                if (controller.KillAgent(agent.index))
                {
                    killed++;
                    OnAgentKilled.Invoke(agent.index);
                }
            }

            if (killed > 0)
                OnTotalAgentsDamaged.Invoke(killed);
            else
                OnNoTargetsFound.Invoke();

            OnDamageEnd.Invoke();
        }

        /// <summary>
        /// Set the base damage at runtime. Useful for wiring from UnityEvents.
        /// </summary>
        public void SetBaseDamage(float damage) => baseDamage = Mathf.Max(0f, damage);

        /// <summary>
        /// Set the damage radius at runtime. Useful for wiring from UnityEvents.
        /// </summary>
        public void SetRadius(float radius) => damageRadius = Mathf.Max(0.1f, radius);

        /// <summary>
        /// Reset the cooldown, allowing DealDamage to be called immediately.
        /// </summary>
        public void ResetCooldown() => _lastDamageTime = float.NegativeInfinity;

        #endregion

        #region ── Internals ──

        private float CalculateDamage(out bool isCritical)
        {
            float damage = baseDamage;

            if (damageVariance > 0f)
                damage += Random.Range(-damageVariance, damageVariance);

            isCritical = criticalChance > 0f && Random.value <= criticalChance;
            if (isCritical)
                damage *= criticalMultiplier;

            return Mathf.Max(0f, damage);
        }

        private void FilterAgents(List<CrowdAgent> agents)
        {
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead)
                {
                    agents.RemoveAt(i);
                    continue;
                }

                if (!PassesFactionFilter(agent))
                {
                    agents.RemoveAt(i);
                }
            }
        }

        private bool PassesFactionFilter(CrowdAgent agent)
        {
            switch (factionFilter)
            {
                case CrowdDamageFactionFilter.AllFactions:
                    return true;

                case CrowdDamageFactionFilter.HostileOnly:
                    return IsHostileToUs(agent);

                case CrowdDamageFactionFilter.OnlyListedFactions:
                    if (_factionSet == null || _factionSet.Count == 0) return false;
                    return agent.enemyCrowd != null && _factionSet.Contains(agent.enemyCrowd);

                case CrowdDamageFactionFilter.ExcludeListedFactions:
                    if (_factionSet == null || _factionSet.Count == 0) return true;
                    return agent.enemyCrowd == null || !_factionSet.Contains(agent.enemyCrowd);

                default:
                    return true;
            }
        }

        private bool IsHostileToUs(CrowdAgent agent)
        {
            // If we have an ExternalFactionIdentifier, use it for relationship checking
            if (_factionIdentifier != null)
            {
                var relationship = _factionIdentifier.GetRelationshipFrom(agent);
                return relationship.IsHostile();
            }

            // Fallback: use the global player relationship
            return FactionQuery.GetPlayerRelationship(agent).IsHostile();
        }

        private void RebuildFactionSet()
        {
            _factionSet ??= new HashSet<EnemyCrowd>();
            _factionSet.Clear();

            if (factionList != null)
            {
                for (int i = 0; i < factionList.Count; i++)
                {
                    if (factionList[i] != null)
                        _factionSet.Add(factionList[i]);
                }
            }
        }

        private static CrowdAgent FindClosestAgent(List<CrowdAgent> agents, Vector3 position)
        {
            CrowdAgent closest = null;
            float closestSqr = float.MaxValue;

            for (int i = 0; i < agents.Count; i++)
            {
                float sqr = (agents[i].WorldPosition - position).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest = agents[i];
                }
            }

            return closest;
        }

        private static void SortByDistance(List<CrowdAgent> agents, Vector3 position)
        {
            agents.Sort((a, b) =>
                (a.WorldPosition - position).sqrMagnitude
                    .CompareTo((b.WorldPosition - position).sqrMagnitude));
        }

        #endregion

        #region ── Gizmos ──

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            Vector3 origin = damageOrigin != null ? damageOrigin.position : transform.position;

            // Filled sphere
            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(origin, damageRadius);

            // Wire outline
            Color wireColor = gizmoColor;
            wireColor.a = Mathf.Clamp01(wireColor.a * 4f);
            Gizmos.color = wireColor;
            Gizmos.DrawWireSphere(origin, damageRadius);
        }
#endif

        #endregion
    }
}
