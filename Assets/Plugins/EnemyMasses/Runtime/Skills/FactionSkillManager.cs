using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    #region ═══════════════════════════════════════════════════════════════
    // SKILL EVENT DATA STRUCTURES
    #endregion ════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Data passed when a skill is cast (OnSkillCast event).
    /// </summary>
    public struct SkillCastEventData
    {
        /// <summary>The agent that cast the skill.</summary>
        public CrowdAgent Caster;
        
        /// <summary>The skill that was cast.</summary>
        public AgentSkill Skill;
        
        /// <summary>The skill slot used.</summary>
        public AgentSkillSlot Slot;
        
        /// <summary>The intended primary target (may be null for self-targeted skills).</summary>
        public CrowdAgent PrimaryTarget;
        
        /// <summary>All targets for this skill.</summary>
        public CrowdAgent[] AllTargets;
        
        /// <summary>World position where skill was aimed/cast.</summary>
        public Vector3 TargetPosition;
        
        /// <summary>Whether this cast was a critical hit.</summary>
        public bool IsCritical;
        
        /// <summary>Time when the skill was cast.</summary>
        public float CastTime;
    }
    
    /// <summary>
    /// Data passed when a skill impacts/hits targets (OnSkillImpact event).
    /// This fires when skill effects are actually applied (immediately for non-projectile, on hit for projectiles).
    /// </summary>
    public struct SkillImpactEventData
    {
        /// <summary>The agent that cast the skill.</summary>
        public CrowdAgent Caster;
        
        /// <summary>The skill that impacted.</summary>
        public AgentSkill Skill;
        
        /// <summary>The target that was hit. For AOE skills, this event fires once per target.</summary>
        public CrowdAgent Target;
        
        /// <summary>World position where the impact occurred.</summary>
        public Vector3 ImpactPosition;
        
        /// <summary>Damage dealt (0 if skill doesn't deal damage).</summary>
        public float DamageDealt;
        
        /// <summary>Healing applied (0 if skill doesn't heal).</summary>
        public float HealingApplied;
        
        /// <summary>Whether this impact was a critical hit.</summary>
        public bool IsCritical;
        
        /// <summary>Whether this was from a projectile hit (vs immediate effect).</summary>
        public bool IsProjectileHit;
        
        /// <summary>Time when the impact occurred.</summary>
        public float ImpactTime;
    }
    
    /// <summary>
    /// Data passed when a skill finishes (OnSkillFinish event).
    /// Fires after OnFinishActions have executed; for skills with a Duration, this is delayed accordingly.
    /// Use this to clean up indicators, particle systems, or any visual effects spawned on cast.
    /// </summary>
    public struct SkillFinishEventData
    {
        /// <summary>The agent that cast the skill.</summary>
        public CrowdAgent Caster;
        
        /// <summary>The skill that finished.</summary>
        public AgentSkill Skill;
        
        /// <summary>The skill slot used.</summary>
        public AgentSkillSlot Slot;
        
        /// <summary>World position where skill was aimed/cast (same as SkillCastEventData.TargetPosition).</summary>
        public Vector3 TargetPosition;
        
        /// <summary>All targets that were affected.</summary>
        public CrowdAgent[] AllTargets;
        
        /// <summary>True if the skill was interrupted/cancelled rather than completing naturally.</summary>
        public bool WasCancelled;
        
        /// <summary>Time when the skill finished.</summary>
        public float FinishTime;
    }
    
    /// <summary>
    /// Manages skill execution for a faction/crowd.
    /// Attach this to the same GameObject as EnemyMassesCrowdController to enable skills.
    /// 
    /// This manager handles:
    /// - Per-agent skill runtime states
    /// - Per-faction shared cooldowns
    /// - Public API for manual skill usage
    /// - Cleanup of stale agent states
    /// 
    /// Note: Primary skill execution during combat is handled by EnemyMassesCombatSystem.
    /// This manager provides the public API for:
    /// - Forcing specific skills on agents
    /// - Querying cooldown status
    /// - Manual control of skill execution
    /// </summary>
    [RequireComponent(typeof(EnemyMassesCrowdController))]
    public class FactionSkillManager : MonoBehaviour
    {
        #region ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        #endregion ════════════════════════════════════════════════════════════
        
        [Header("═══ SKILL MANAGER SETTINGS ═══")]
        
        [Tooltip("Enable debug visualization of skill usage")]
        public bool debugMode = false;
        
        [Tooltip("Range to search for potential targets when forcing skills")]
        [Range(10f, 200f)]
        public float targetSearchRadius = 50f;
        
        #region ═══════════════════════════════════════════════════════════════
        // SKILL EVENTS
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Fired when any agent casts a skill.
        /// Subscribe to react to skill usage (e.g., play UI effects, log combat, trigger abilities).
        /// </summary>
        public event Action<SkillCastEventData> OnSkillCast;
        
        /// <summary>
        /// Fired when a skill effect impacts a target.
        /// For non-projectile skills, fires immediately after cast.
        /// For projectile skills, fires when the projectile hits.
        /// May fire multiple times for AOE skills (once per target).
        /// </summary>
        public event Action<SkillImpactEventData> OnSkillImpact;
        
        /// <summary>
        /// Static event for global skill cast notifications.
        /// Use when you need to listen to skill casts without a reference to FactionSkillManager.
        /// Parameters: (FactionSkillManager source, SkillCastEventData data)
        /// </summary>
        public static event Action<FactionSkillManager, SkillCastEventData> OnAnySkillCast;
        
        /// <summary>
        /// Static event for global skill impact notifications.
        /// Use when you need to listen to skill impacts without a reference to FactionSkillManager.
        /// Parameters: (FactionSkillManager source, SkillImpactEventData data)
        /// </summary>
        public static event Action<FactionSkillManager, SkillImpactEventData> OnAnySkillImpact;
        
        /// <summary>
        /// Fired when a skill finishes (after OnFinishActions execute).
        /// For skills with a Duration, this fires after that duration elapses.
        /// Subscribe to clean up indicators, particle systems, or other visuals spawned on cast.
        /// </summary>
        public event Action<SkillFinishEventData> OnSkillFinish;
        
        /// <summary>
        /// Static event for global skill finish notifications.
        /// Use when you need to listen to skill completions without a reference to FactionSkillManager.
        /// Parameters: (FactionSkillManager source, SkillFinishEventData data)
        /// </summary>
        public static event Action<FactionSkillManager, SkillFinishEventData> OnAnySkillFinish;
        
        // Singleton instance for easy access (set in Awake)
        private static FactionSkillManager _instance;
        
        /// <summary>
        /// Gets the active FactionSkillManager instance (if any).
        /// </summary>
        public static FactionSkillManager Instance => _instance;
        
        #region ═══════════════════════════════════════════════════════════════
        // RUNTIME STATE
        #endregion ════════════════════════════════════════════════════════════
        
        private EnemyMassesCrowdController _controller;
        private Dictionary<EnemyCrowd, SkillSystemConfig> _factionConfigs = new Dictionary<EnemyCrowd, SkillSystemConfig>();
        
        // Per-agent runtime states (keyed by agent instance ID)
        // Note: Combat system also maintains its own copy for performance
        private Dictionary<int, SkillRuntimeState> _agentStates = new Dictionary<int, SkillRuntimeState>();
        
        // Per-faction shared cooldowns (keyed by faction ID)
        // Note: Combat system also maintains its own copy for performance
        private Dictionary<string, FactionSkillCooldowns> _factionCooldowns = new Dictionary<string, FactionSkillCooldowns>();
        
        // Per-EnemyCrowd shared cooldowns (keyed by EnemyCrowd asset)
        // Note: Combat system also maintains its own copy for performance
        private Dictionary<EnemyCrowd, FactionSkillCooldowns> _enemyCrowdCooldowns = new Dictionary<EnemyCrowd, FactionSkillCooldowns>();
        
        // Cached target list for reuse
        private List<CrowdAgent> _potentialTargets = new List<CrowdAgent>();
        private List<CrowdAgent> _skillTargets = new List<CrowdAgent>();
        
        // Cleanup tracking
        private float _lastCleanupTime;
        private const float CLEANUP_INTERVAL = 10f;
        
        #region ═══════════════════════════════════════════════════════════════
        // UNITY LIFECYCLE
        #endregion ════════════════════════════════════════════════════════════
        
        private void Awake()
        {
            _controller = GetComponent<EnemyMassesCrowdController>();
            _instance = this;
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
        
        private void Start()
        {
            // Build faction configs from all EnemyCrowds
            if (_controller != null && _controller.EnemyCrowds != null)
            {
                foreach (var crowd in _controller.EnemyCrowds)
                {
                    if (crowd != null)
                    {
                        _factionConfigs[crowd] = crowd.skillSystemConfig;
                        
                        // Initialize faction cooldowns using factionName
                        if (!string.IsNullOrEmpty(crowd.factionName))
                        {
                            _factionCooldowns[crowd.factionName] = new FactionSkillCooldowns();
                        }

                        // Initialize per-crowd cooldowns
                        if (!_enemyCrowdCooldowns.ContainsKey(crowd))
                        {
                            _enemyCrowdCooldowns[crowd] = new FactionSkillCooldowns();
                        }
                    }
                }
            }
        }
        
        private void Update()
        {
            if (_controller == null)
                return;
            
            float deltaTime = Time.deltaTime;
            
            // Update all faction cooldowns (for public API queries)
            foreach (var cooldowns in _factionCooldowns.Values)
            {
                cooldowns.Update(deltaTime);
            }

            // Update per-crowd cooldowns (for public API queries)
            foreach (var cooldowns in _enemyCrowdCooldowns.Values)
            {
                cooldowns.Update(deltaTime);
            }
            
            // Update per-agent cooldowns (for public API queries)
            foreach (var state in _agentStates.Values)
            {
                state.Update(deltaTime);
            }
            
            // Periodic cleanup of stale agent states
            if (Time.time - _lastCleanupTime >= CLEANUP_INTERVAL)
            {
                _lastCleanupTime = Time.time;
                CleanupStaleStates();
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // TARGET GATHERING
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Gathers potential targets (enemies and allies) near an agent.
        /// </summary>
        private void GatherPotentialTargets(CrowdAgent agent)
        {
            _potentialTargets.Clear();
            
            if (agent == null || agent.transform == null) return;
            
            Vector3 agentPos = agent.transform.position;
            
            // Get all agents from the controller
            var allAgents = _controller.GetAgents();
            if (allAgents == null) return;
            
            foreach (var otherAgent in allAgents)
            {
                if (otherAgent == null || otherAgent == agent || otherAgent.isDead) continue;
                if (otherAgent.transform == null) continue;
                
                float dist = Vector3.Distance(agentPos, otherAgent.transform.position);
                if (dist <= targetSearchRadius)
                {
                    _potentialTargets.Add(otherAgent);
                }
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Manually trigger a specific skill for an agent.
        /// Useful for player-controlled units or scripted sequences.
        /// </summary>
        /// <param name="targetAgentIndex">When >= 0, forces this specific agent as the target
        /// instead of using automatic target selection. Used by the skill bar to
        /// respect the player's clicked target.</param>
        public bool ForceUseSkill(CrowdAgent agent, int skillSlotIndex, Vector3? groundTarget = null, int targetAgentIndex = -1)
        {
            if (agent == null || agent.enemyCrowd == null) return false;
            
            if (!_factionConfigs.TryGetValue(agent.enemyCrowd, out var skillConfig))
                return false;
            
            if (skillSlotIndex < 0 || skillSlotIndex >= skillConfig.skillSlots.Count) return false;
            
            var slot = skillConfig.skillSlots[skillSlotIndex];
            if (slot?.skill == null) return false;
            
            // Get agent state
            int agentId = agent.index;
            if (!_agentStates.TryGetValue(agentId, out var agentState))
            {
                agentState = new SkillRuntimeState();
                _agentStates[agentId] = agentState;
            }
            
            // Get faction cooldowns using factionName
            string factionName = agent.enemyCrowd?.factionName ?? "";
            _factionCooldowns.TryGetValue(factionName, out var factionCooldowns);
            _enemyCrowdCooldowns.TryGetValue(agent.enemyCrowd, out var enemyCrowdCooldowns);
            
            // Check cooldown
            if (SkillExecutor.IsSkillOnCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, skillConfig.defaultCooldownMode))
            {
                if (debugMode)
                {
                    float remaining = SkillExecutor.GetRemainingCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, skillConfig.defaultCooldownMode);
                    Debug.Log($"[FactionSkillManager] Skill {slot.skill.SkillName} on cooldown: {remaining:F1}s remaining");
                }
                return false;
            }
            
            // Check resource cost (mana, energy, etc.)
            if (slot.skill.HasResourceCost && !SkillResourceManager.Instance.HasResource(agent, slot.skill))
            {
                if (debugMode)
                {
                    float current = SkillResourceManager.Instance.GetCurrentResource(agent, slot.skill.ResourceName);
                    float cost = slot.skill.GetScaledResourceCost(agent);
                    Debug.Log($"[FactionSkillManager] Not enough {slot.skill.ResourceName} for {slot.skill.SkillName}: " +
                             $"has {current:F0}, needs {cost:F0}");
                }
                return false;
            }
            
            // Find targets
            GatherPotentialTargets(agent);
            _skillTargets.Clear();
            
            // If a specific target agent was requested, try to use it directly
            if (targetAgentIndex >= 0)
            {
                var allAgents = _controller.GetAgents();
                if (targetAgentIndex < allAgents.Count)
                {
                    var targetAgent = allAgents[targetAgentIndex];
                    if (targetAgent != null && !targetAgent.isDead)
                    {
                        _skillTargets.Add(targetAgent);
                    }
                }
            }
            
            // Fall back to automatic target selection if no explicit target or it was invalid
            if (_skillTargets.Count == 0)
            {
                _skillTargets.AddRange(SkillExecutor.FindTargets(slot.skill, agent, _potentialTargets, groundTarget));
            }
            
            // GroundTargetedAOE can fire on empty ground (no enemies in radius).
            // All other targeting types require at least one target.
            if (_skillTargets.Count == 0 && slot.skill.TargetingType != SkillTargetingType.GroundTargetedAOE)
            {
                if (debugMode)
                {
                    Debug.Log($"[FactionSkillManager] No valid targets for skill {slot.skill.SkillName}");
                }
                return false;
            }
            
            // Execute — routes through INetworkSkillAuthority in multiplayer
            return SkillExecutor.RequestSkillExecution(
                slot,
                skillSlotIndex,
                agent,
                _skillTargets,
                _controller,
                agentState,
                factionCooldowns,
                enemyCrowdCooldowns,
                skillConfig.defaultCooldownMode,
                groundTarget,
                agent.ownerPlayerId
            );
        }
        
        /// <summary>
        /// Gets the remaining cooldown for a skill slot for a specific agent.
        /// </summary>
        public float GetCooldownRemaining(CrowdAgent agent, int skillSlotIndex)
        {
            if (agent == null || agent.enemyCrowd == null) return 0f;
            
            if (!_factionConfigs.TryGetValue(agent.enemyCrowd, out var skillConfig))
                return 0f;
            
            if (skillSlotIndex < 0 || skillSlotIndex >= skillConfig.skillSlots.Count) return 0f;
            
            var slot = skillConfig.skillSlots[skillSlotIndex];
            if (slot?.skill == null) return 0f;
            
            int agentId = agent.index;
            _agentStates.TryGetValue(agentId, out var agentState);
            
            string factionName = agent.enemyCrowd?.factionName ?? "";
            _factionCooldowns.TryGetValue(factionName, out var factionCooldowns);
            _enemyCrowdCooldowns.TryGetValue(agent.enemyCrowd, out var enemyCrowdCooldowns);
            
            return SkillExecutor.GetRemainingCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, skillConfig.defaultCooldownMode);
        }
        
        /// <summary>
        /// Checks if a skill is ready to use for a specific agent.
        /// </summary>
        public bool IsSkillReady(CrowdAgent agent, int skillSlotIndex)
        {
            return GetCooldownRemaining(agent, skillSlotIndex) <= 0f;
        }
        
        /// <summary>
        /// Resets all cooldowns (both per-agent and faction-wide).
        /// </summary>
        public void ResetAllCooldowns()
        {
            foreach (var cooldowns in _factionCooldowns.Values)
            {
                cooldowns.ResetAll();
            }
            foreach (var cooldowns in _enemyCrowdCooldowns.Values)
            {
                cooldowns.ResetAll();
            }
            foreach (var state in _agentStates.Values)
            {
                state.ResetAll();
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // MANUAL CONTROL API
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Queues a skill for manual execution on next attack opportunity.
        /// Use this for player-controlled units when you want the skill to execute
        /// when the agent's next attack would normally happen.
        /// </summary>
        /// <param name="agent">The agent to queue the skill for</param>
        /// <param name="skillSlotIndex">Index of the skill slot (0-based)</param>
        /// <param name="targetAgentIndex">Optional specific target agent index (-1 for auto-targeting)</param>
        /// <returns>True if skill was successfully queued</returns>
        public bool QueueSkill(CrowdAgent agent, int skillSlotIndex, int targetAgentIndex = -1)
        {
            if (agent == null || agent.enemyCrowd == null)
                return false;
            
            if (!_factionConfigs.TryGetValue(agent.enemyCrowd, out var skillConfig))
                return false;
            
            if (skillSlotIndex < 0 || skillSlotIndex >= skillConfig.skillSlots.Count)
                return false;
            
            var slot = skillConfig.skillSlots[skillSlotIndex];
            if (slot?.skill == null || !slot.enabled)
                return false;
            
            // Queue the skill
            agent.queuedSkillSlot = skillSlotIndex;
            agent.queuedSkillTargetIndex = targetAgentIndex;
            
            if (debugMode)
                Debug.Log($"[FactionSkillManager] QueueSkill — agent[{agent.index}] queued '{slot.skill.SkillName}' slot={skillSlotIndex} target={targetAgentIndex}");
            
            return true;
        }
        
        /// <summary>
        /// Cancels any queued skill for the agent.
        /// </summary>
        public void CancelQueuedSkill(CrowdAgent agent)
        {
            if (agent == null) return;
            agent.queuedSkillSlot = -1;
            agent.queuedSkillTargetIndex = -1;
            agent.queuedSkillDamageableTarget = null;
        }
        
        /// <summary>
        /// Gets the currently queued skill slot index for an agent.
        /// Returns -1 if no skill is queued.
        /// </summary>
        public int GetQueuedSkillSlot(CrowdAgent agent)
        {
            return agent?.queuedSkillSlot ?? -1;
        }
        
        /// <summary>
        /// Sets an agent to use manual skill control (Hybrid mode required on faction).
        /// </summary>
        /// <param name="agent">The agent to set manual control for</param>
        /// <param name="useManual">True to enable manual control, false for AI control</param>
        public void SetAgentManualControl(CrowdAgent agent, bool useManual)
        {
            if (agent == null) return;
            
            // Check if faction supports hybrid mode
            if (agent.enemyCrowd?.skillSystemConfig != null)
            {
                var config = agent.enemyCrowd.skillSystemConfig;
                if (config.controlMode != SkillControlMode.Hybrid && useManual)
                {
                    if (debugMode)
                        Debug.LogWarning($"[FactionSkillManager] Cannot set manual control on agent {agent.index}: " +
                                       $"Faction control mode is '{config.controlMode}', not Hybrid.");
                    return;
                }
            }
            
            agent.useManualSkillControl = useManual;
            
            // Clear any queued skill when switching modes
            if (!useManual)
            {
                agent.queuedSkillSlot = -1;
                agent.queuedSkillTargetIndex = -1;
                agent.queuedSkillDamageableTarget = null;
            }
            
            if (debugMode)
                Debug.Log($"[FactionSkillManager] Agent {agent.index} manual skill control: {useManual}");
        }
        
        /// <summary>
        /// Checks if an agent is using manual skill control.
        /// </summary>
        public bool IsAgentUsingManualControl(CrowdAgent agent)
        {
            if (agent == null || agent.enemyCrowd == null) return false;
            
            var config = agent.enemyCrowd.skillSystemConfig;
            if (config == null) return false;
            
            switch (config.controlMode)
            {
                case SkillControlMode.AIControlled:
                    return false;
                case SkillControlMode.ManualOnly:
                    return true;
                case SkillControlMode.Hybrid:
                    return agent.useManualSkillControl;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Gets all skill slot information for a faction (for UI display).
        /// </summary>
        public List<SkillSlotInfo> GetFactionSkillSlots(EnemyCrowd faction)
        {
            var result = new List<SkillSlotInfo>();
            
            if (faction?.skillSystemConfig == null) return result;
            var config = faction.skillSystemConfig;
            
            for (int i = 0; i < config.skillSlots.Count; i++)
            {
                var slot = config.skillSlots[i];
                if (slot?.skill == null) continue;
                
                result.Add(new SkillSlotInfo
                {
                    slotIndex = i,
                    skillName = slot.skill.SkillName,
                    skillId = slot.skill.SkillId,
                    enabled = slot.enabled,
                    icon = slot.skill.Icon,
                    cooldown = slot.skill.Cooldown * slot.cooldownMultiplier
                });
            }
            
            return result;
        }
        
        /// <summary>
        /// Information about a skill slot for UI display.
        /// </summary>
        public struct SkillSlotInfo
        {
            public int slotIndex;
            public string skillName;
            public string skillId;
            public bool enabled;
            public Sprite icon;
            public float cooldown;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // CLEANUP
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Cleans up state for agents that no longer exist.
        /// Call this periodically if agents are frequently destroyed.
        /// </summary>
        public void CleanupStaleStates()
        {
            var staleIds = new List<int>();
            var allAgents = _controller?.GetAgents();
            
            foreach (var kvp in _agentStates)
            {
                bool found = false;
                if (allAgents != null)
                {
                    foreach (var agent in allAgents)
                    {
                        if (agent != null && agent.index == kvp.Key)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                
                if (!found)
                {
                    staleIds.Add(kvp.Key);
                }
            }
            
            foreach (var id in staleIds)
            {
                _agentStates.Remove(id);
            }
            
            if (debugMode && staleIds.Count > 0)
            {
                Debug.Log($"[FactionSkillManager] Cleaned up {staleIds.Count} stale agent states");
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // EVENT INVOCATION (Called by SkillExecutor)
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Raises the OnSkillCast event. Called internally by SkillExecutor.
        /// </summary>
        internal void RaiseSkillCast(SkillCastEventData data)
        {
            OnSkillCast?.Invoke(data);
            OnAnySkillCast?.Invoke(this, data);
            
            if (debugMode)
            {
                Debug.Log($"[FactionSkillManager] Skill Cast: {data.Skill?.SkillName ?? "null"} by agent {data.Caster?.index ?? -1}" +
                         $" -> {data.AllTargets?.Length ?? 0} targets{(data.IsCritical ? " (CRIT!)" : "")}");
            }
        }
        
        /// <summary>
        /// Raises the OnSkillImpact event. Called internally by SkillExecutor.
        /// </summary>
        internal void RaiseSkillImpact(SkillImpactEventData data)
        {
            OnSkillImpact?.Invoke(data);
            OnAnySkillImpact?.Invoke(this, data);
            
            if (debugMode)
            {
                string effectInfo = "";
                if (data.DamageDealt > 0) effectInfo += $" dmg={data.DamageDealt:F1}";
                if (data.HealingApplied > 0) effectInfo += $" heal={data.HealingApplied:F1}";
                
                Debug.Log($"[FactionSkillManager] Skill Impact: {data.Skill?.SkillName ?? "null"} hit agent {data.Target?.index ?? -1}" +
                         $"{effectInfo}{(data.IsCritical ? " (CRIT!)" : "")}{(data.IsProjectileHit ? " [Projectile]" : "")}");
            }
        }
        
        /// <summary>
        /// Static method to raise skill cast from SkillExecutor when no manager reference is available.
        /// Finds the singleton and invokes events if available.
        /// </summary>
        internal static void RaiseSkillCastStatic(SkillCastEventData data)
        {
            _instance?.RaiseSkillCast(data);
        }
        
        /// <summary>
        /// Static method to raise skill impact from SkillExecutor when no manager reference is available.
        /// Finds the singleton and invokes events if available.
        /// </summary>
        internal static void RaiseSkillImpactStatic(SkillImpactEventData data)
        {
            _instance?.RaiseSkillImpact(data);
        }
        
        /// <summary>
        /// Raises the OnSkillFinish event. Called internally by SkillExecutor.
        /// </summary>
        internal void RaiseSkillFinish(SkillFinishEventData data)
        {
            OnSkillFinish?.Invoke(data);
            OnAnySkillFinish?.Invoke(this, data);
            
            if (debugMode)
            {
                Debug.Log($"[FactionSkillManager] Skill Finish: {data.Skill?.SkillName ?? "null"} by agent {data.Caster?.index ?? -1}" +
                         $" -> {data.AllTargets?.Length ?? 0} targets{(data.WasCancelled ? " (CANCELLED)" : "")}");
            }
        }
        
        /// <summary>
        /// Static method to raise skill finish from SkillExecutor when no manager reference is available.
        /// Finds the singleton and invokes events if available.
        /// </summary>
        internal static void RaiseSkillFinishStatic(SkillFinishEventData data)
        {
            _instance?.RaiseSkillFinish(data);
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // DEBUG VISUALIZATION
        #endregion ════════════════════════════════════════════════════════════
        
        private void OnDrawGizmosSelected()
        {
            if (!debugMode || _controller == null) return;
            
            var agents = _controller.GetAgents();
            if (agents == null) return;
            
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            
            // Draw target search radius around some agents
            int count = 0;
            foreach (var agent in agents)
            {
                if (agent != null && agent.transform != null && count < 5) // Only draw for first 5 agents
                {
                    Gizmos.DrawWireSphere(agent.transform.position, targetSearchRadius);
                    count++;
                }
            }
        }
    }
}
