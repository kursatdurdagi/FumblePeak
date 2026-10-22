using UnityEngine;
using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    // Note: AgentReference and CachedAgentData are now defined in AgentTypes.cs
    // They exist in both Arawn.EnemyMasses.Runtime and Arawn.EnemyMasses.Runtime.Skills
    // for backwards compatibility.
    
    /// <summary>
    /// Manages all player skills and handles damage dealing to EnemyCrowd agents.
    /// Attach this to the player GameObject.
    /// </summary>
    public class PlayerSkillManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Optional: Manually assign crowd controllers. If empty, all controllers will be auto-found.")]
        [SerializeField] private List<EnemyMassesCrowdController> crowdControllers = new List<EnemyMassesCrowdController>();
        
        [Tooltip("Transform used as skill origin point (defaults to this transform)")]
        [SerializeField] private Transform skillOrigin;
        
        [Tooltip("Auto-find new crowd controllers spawned at runtime")]
        [SerializeField] private bool autoFindNewControllers = true;
        
        [Tooltip("How often to check for new crowd controllers (seconds)")]
        [SerializeField] private float controllerScanInterval = 1f;
        
        [Header("Settings")]
        [Tooltip("How often to update enemy position cache (seconds)")]
        [SerializeField] private float enemyCacheUpdateRate = 0.1f;
        
        [Tooltip("Maximum number of agents to track for targeting (0 = unlimited)")]
        [SerializeField] private int maxTrackedAgents = 0;
        
        [Tooltip("Only cache ALIVE agents (recommended - dead agents don't need tracking)")]
        [SerializeField] private bool onlyCacheAliveAgents = true;
        
        [Header("Damage Settings")]
        [Tooltip("Global damage multiplier")]
        [SerializeField] private float damageMultiplier = 1f;
        
        [Tooltip("Invincibility time after being hit")]
        [SerializeField] private float invincibilityDuration = 0.5f;
        
        [Header("Effect Limits")]
        [Tooltip("Maximum VFX instances spawned per skill activation (0 = unlimited)")]
        [SerializeField] private int globalMaxVFXPerActivation = 20;
        
        [Tooltip("Maximum hit sounds played per skill activation (0 = unlimited)")]
        [SerializeField] private int globalMaxHitSoundsPerActivation = 5;
        
        // Public accessors for effect limits
        public int GlobalMaxVFXPerActivation => globalMaxVFXPerActivation;
        public int GlobalMaxHitSoundsPerActivation => globalMaxHitSoundsPerActivation;
        
        [Header("Debug")]
        [Tooltip("Enable verbose debug logging for troubleshooting")]
        [SerializeField] private bool enableDebugLogs = false;
        
        [Tooltip("Log damage events")]
        [SerializeField] private bool logDamageEvents = false;
        
        [Tooltip("Log cache updates (warning: very verbose)")]
        [SerializeField] private bool logCacheUpdates = false;
        
        [Tooltip("Log periodic status every N seconds (0 = disabled)")]
        [SerializeField] private float periodicStatusInterval = 5f;
        
        private float lastPeriodicStatusTime = -999f;
        
        [Header("Events")]
        [Tooltip("Invoked when an enemy is damaged")]
        public System.Action<int, float, BaseSkill> OnEnemyDamaged;
        
        [Tooltip("Invoked when an enemy is killed")]
        public System.Action<int, BaseSkill> OnEnemyKilled;
        
        [Tooltip("Invoked when a skill levels up")]
        public System.Action<BaseSkill> OnSkillLevelUp;
        
        // Cached components and state
        private List<BaseSkill> activeSkills = new List<BaseSkill>();
        private Dictionary<(EnemyMassesCrowdController controller, int index), float> agentInvincibilityTimers = 
            new Dictionary<(EnemyMassesCrowdController, int), float>();
        private float lastControllerScan = -999f;
        
        // Enemy position cache for efficient range queries
        private struct CachedAgentData
        {
            public EnemyMassesCrowdController controller;
            public int index;
            public Vector3 position;
            public bool isAlive;
        }
        private List<CachedAgentData> cachedAgents = new List<CachedAgentData>();
        private float lastCacheUpdate = -999f;
        
        // Public accessors
        /// <summary>Returns the first crowd controller (for backwards compatibility)</summary>
        public EnemyMassesCrowdController CrowdController => crowdControllers.Count > 0 ? crowdControllers[0] : null;
        /// <summary>Returns all active crowd controllers</summary>
        public IReadOnlyList<EnemyMassesCrowdController> CrowdControllers => crowdControllers;
        public Transform SkillOrigin => skillOrigin != null ? skillOrigin : transform;
        public Vector3 PlayerPosition => SkillOrigin.position;
        public IReadOnlyList<BaseSkill> ActiveSkills => activeSkills;
        public float DamageMultiplier { get => damageMultiplier; set => damageMultiplier = Mathf.Max(0, value); }
        
        private void Awake()
        {
            if (skillOrigin == null)
            {
                skillOrigin = transform;
            }
            
            // Find all crowd controllers if none assigned
            RefreshCrowdControllers();
        }
        
        /// <summary>
        /// Refresh the list of crowd controllers. Call this after spawning new crowds.
        /// </summary>
        public void RefreshCrowdControllers()
        {
            int previousCount = crowdControllers.Count;
            
            // Remove any null/destroyed controllers
            int removedCount = crowdControllers.RemoveAll(c => c == null);
            if (removedCount > 0 && enableDebugLogs)
            {
                Debug.Log($"[PlayerSkillManager] Removed {removedCount} null/destroyed controllers");
            }
            
            // Find all controllers in scene
            var allControllers = FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            
            foreach (var controller in allControllers)
            {
                if (!crowdControllers.Contains(controller))
                {
                    crowdControllers.Add(controller);
                    Debug.Log($"[PlayerSkillManager] <color=green>Found NEW crowd controller:</color> {controller.name} (InstanceID: {controller.GetInstanceID()})");
                }
            }
            
            if (enableDebugLogs && crowdControllers.Count != previousCount)
            {
                Debug.Log($"[PlayerSkillManager] Total crowd controllers: {crowdControllers.Count}");
                for (int i = 0; i < crowdControllers.Count; i++)
                {
                    var ctrl = crowdControllers[i];
                    Debug.Log($"  [{i}] {ctrl.name} - AgentCount: {ctrl.GetAgentCount()}, InstanceID: {ctrl.GetInstanceID()}");
                }
            }
        }
        
        private void Start()
        {
            // Find all skills on this GameObject and children (including disabled ones)
            var skills = GetComponentsInChildren<BaseSkill>(true);
            foreach (var skill in skills)
            {
                RegisterSkill(skill);
            }
        }
        
        private void Update()
        {
            // Periodically scan for new crowd controllers
            if (autoFindNewControllers && Time.time - lastControllerScan >= controllerScanInterval)
            {
                RefreshCrowdControllers();
                lastControllerScan = Time.time;
            }
            
            // Update enemy position cache
            if (Time.time - lastCacheUpdate >= enemyCacheUpdateRate)
            {
                UpdateEnemyCache();
                lastCacheUpdate = Time.time;
            }
            
            // Periodic status log (always enabled when interval > 0)
            if (periodicStatusInterval > 0 && Time.time - lastPeriodicStatusTime >= periodicStatusInterval)
            {
                LogPeriodicStatus();
                lastPeriodicStatusTime = Time.time;
            }
            
            // Update invincibility timers
            UpdateInvincibilityTimers();
            
            // Update all active skills (only if their GameObject is active)
            foreach (var skill in activeSkills)
            {
                if (skill != null && skill.IsActive && skill.gameObject.activeInHierarchy)
                {
                    skill.UpdateSkill();
                }
            }
        }
        
        private void LogPeriodicStatus()
        {
            int aliveCount = 0;
            int deadCount = 0;
            int zeroPositionCount = 0;
            var controllerCounts = new Dictionary<string, (int alive, int dead, int total, int zeroPos)>();
            
            foreach (var cached in cachedAgents)
            {
                string name = cached.controller != null ? cached.controller.name : "NULL";
                if (!controllerCounts.ContainsKey(name))
                    controllerCounts[name] = (0, 0, 0, 0);
                
                var current = controllerCounts[name];
                bool isZeroPos = cached.position.sqrMagnitude < 0.01f;
                
                if (cached.isAlive)
                {
                    aliveCount++;
                    if (isZeroPos) zeroPositionCount++;
                    controllerCounts[name] = (current.alive + 1, current.dead, current.total + 1, current.zeroPos + (isZeroPos ? 1 : 0));
                }
                else
                {
                    deadCount++;
                    controllerCounts[name] = (current.alive, current.dead + 1, current.total + 1, current.zeroPos);
                }
            }
            
            // Also check ACTUAL agent counts from controllers (not just cache)
            var actualCounts = new Dictionary<string, (int alive, int dead, int total)>();
            foreach (var ctrl in crowdControllers)
            {
                if (ctrl == null) continue;
                int ctrlAlive = 0, ctrlDead = 0;
                int count = ctrl.GetAgentCount();
                for (int i = 0; i < count; i++)
                {
                    if (ctrl.IsAgentDead(i))
                        ctrlDead++;
                    else
                        ctrlAlive++;
                }
                actualCounts[ctrl.name] = (ctrlAlive, ctrlDead, count);
            }
            
            // Check how many enemies are in range of player
            int inRange = GetAgentsInRange(PlayerPosition, 20f).Count;
            
            // Debug log removed for production build
            
            foreach (var kvp in controllerCounts)
            {
                string actualInfo = actualCounts.TryGetValue(kvp.Key, out var actual) 
                    ? $" | ACTUAL: {actual.total} (alive: {actual.alive}, dead: {actual.dead})" 
                    : "";
                string zeroPosWarn = kvp.Value.zeroPos > 0 ? $" <color=orange>({kvp.Value.zeroPos} at origin!)</color>" : "";
                // Debug log removed for production
            }
            
            // Check for mismatch (critical: alive count should match when caching alive only)
            foreach (var kvp in actualCounts)
            {
                bool hasInCache = controllerCounts.ContainsKey(kvp.Key);
                
                if (!hasInCache)
                {
                    // Controller not in cache at all
                    if (onlyCacheAliveAgents && kvp.Value.alive == 0)
                    {
                        // Expected: all dead, we skip dead agents - no warning needed
                        // Debug log removed for production build
                    }
                    else
                    {
                        Debug.LogWarning($"  <color=red>MISSING: {kvp.Key} has {kvp.Value.alive} alive agents but NONE are in cache!</color>");
                    }
                }
                else 
                {
                    var cached = controllerCounts[kvp.Key];
                    // When caching alive only, the ALIVE counts should match (not total)
                    // Small differences are normal - agents can die between cache updates
                    // Only warn if cache has significantly FEWER than actual (means agents are being missed)
                    if (onlyCacheAliveAgents)
                    {
                        int diff = cached.alive - kvp.Value.alive;
                        // Only warn if cache has much fewer agents than actual (potential tracking issue)
                        if (diff < -20)
                        {
                            Debug.LogWarning($"  <color=red>CACHE BEHIND: {kvp.Key} has {kvp.Value.alive} alive agents but cache only tracking {cached.alive}! Consider increasing cache update rate.</color>");
                        }
                        // Positive diff (cache > actual) is normal - agents died since last update
                        // Don't spam logs for expected behavior
                    }
                    else if (cached.total != kvp.Value.total && Mathf.Abs(cached.total - kvp.Value.total) > 20)
                    {
                        Debug.LogWarning($"  <color=orange>MISMATCH: {kvp.Key} has {kvp.Value.total} agents but cache only has {cached.total}</color>");
                    }
                }
            }
            
            // Note: Having alive agents but none in range is normal gameplay - they may be far away
            if (aliveCount > 0 && inRange == 0)
            {
                // Skip position sampling when no agents in range
            }
        }
        
        /// <summary>
        /// Register a skill with the manager
        /// </summary>
        public void RegisterSkill(BaseSkill skill)
        {
            if (skill == null || activeSkills.Contains(skill)) return;
            
            skill.Initialize(this, SkillOrigin);
            activeSkills.Add(skill);
        }
        
        /// <summary>
        /// Unregister a skill from the manager
        /// </summary>
        public void UnregisterSkill(BaseSkill skill)
        {
            activeSkills.Remove(skill);
        }
        
        /// <summary>
        /// Add a new skill by instantiating a prefab
        /// </summary>
        public BaseSkill AddSkill(BaseSkill skillPrefab)
        {
            var skill = Instantiate(skillPrefab, transform);
            RegisterSkill(skill);
            return skill;
        }
        
        /// <summary>
        /// Enable a skill by name (activates its GameObject)
        /// </summary>
        public bool EnableSkill(string skillName)
        {
            foreach (var skill in activeSkills)
            {
                if (skill != null && skill.SkillName == skillName)
                {
                    skill.gameObject.SetActive(true);
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Disable a skill by name (deactivates its GameObject)
        /// </summary>
        public bool DisableSkill(string skillName)
        {
            foreach (var skill in activeSkills)
            {
                if (skill != null && skill.SkillName == skillName)
                {
                    skill.gameObject.SetActive(false);
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Enable a skill by type (activates its GameObject)
        /// </summary>
        public bool EnableSkill<T>() where T : BaseSkill
        {
            var skill = GetSkill<T>();
            if (skill != null)
            {
                skill.gameObject.SetActive(true);
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Disable a skill by type (deactivates its GameObject)
        /// </summary>
        public bool DisableSkill<T>() where T : BaseSkill
        {
            var skill = GetSkill<T>();
            if (skill != null)
            {
                skill.gameObject.SetActive(false);
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Check if a skill is currently enabled (GameObject active)
        /// </summary>
        public bool IsSkillEnabled(string skillName)
        {
            foreach (var skill in activeSkills)
            {
                if (skill != null && skill.SkillName == skillName)
                {
                    return skill.gameObject.activeInHierarchy;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Get all registered skills (including disabled ones)
        /// </summary>
        public List<BaseSkill> GetAllSkills()
        {
            return new List<BaseSkill>(activeSkills);
        }
        
        /// <summary>
        /// Get only enabled skills
        /// </summary>
        public List<BaseSkill> GetEnabledSkills()
        {
            var enabled = new List<BaseSkill>();
            foreach (var skill in activeSkills)
            {
                if (skill != null && skill.gameObject.activeInHierarchy)
                {
                    enabled.Add(skill);
                }
            }
            return enabled;
        }
        
        /// <summary>
        /// Get a skill by type
        /// </summary>
        public T GetSkill<T>() where T : BaseSkill
        {
            foreach (var skill in activeSkills)
            {
                if (skill is T typedSkill)
                {
                    return typedSkill;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Get a skill by name
        /// </summary>
        public BaseSkill GetSkill(string skillName)
        {
            foreach (var skill in activeSkills)
            {
                if (skill != null && skill.SkillName == skillName)
                {
                    return skill;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Level up a skill
        /// </summary>
        public void LevelUpSkill(BaseSkill skill)
        {
            if (skill == null || !activeSkills.Contains(skill)) return;
            
            skill.LevelUp();
            OnSkillLevelUp?.Invoke(skill);
        }
        
        /// <summary>
        /// Deal damage to an EnemyCrowd agent (by cached index). Requires the agent to be cached.
        /// </summary>
        public void DealDamageToAgent(int agentIndex, float damage, BaseSkill sourceSkill = null)
        {
            if (logDamageEvents)
            {
                Debug.Log($"[PlayerSkillManager] <color=yellow>DealDamageToAgent(index={agentIndex})</color> damage={damage:F1}, skill={sourceSkill?.SkillName ?? "null"}");
                Debug.Log($"  CachedAgents count: {cachedAgents.Count}, Controllers count: {crowdControllers.Count}");
            }

            foreach (var cached in cachedAgents)
            {
                if (cached.index != agentIndex || !cached.isAlive) continue;

                if (logDamageEvents)
                {
                    Debug.Log($"  Found in cache: Controller={cached.controller.name}, index={agentIndex}, pos={cached.position}");
                }

                DealDamageToAgent(cached.controller, agentIndex, damage, sourceSkill);
            }
        }
        
        /// <summary>
        /// Deal damage using an AgentReference (recommended for multi-crowd support)
        /// </summary>
        public void DealDamageToAgent(AgentReference agent, float damage, BaseSkill sourceSkill = null)
        {
            if (!agent.IsValid) return;
            DealDamageToAgent(agent.controller, agent.index, damage, sourceSkill);
        }
        
        /// <summary>
        /// Deal damage to an EnemyCrowd agent on a specific controller
        /// </summary>
        public void DealDamageToAgent(EnemyMassesCrowdController controller, int agentIndex, float damage, BaseSkill sourceSkill = null)
        {
            if (controller == null)
            {
                if (logDamageEvents) Debug.LogWarning($"[PlayerSkillManager] DealDamageToAgent called with null controller!");
                return;
            }
            
            var key = (controller, agentIndex);
            
            // Check invincibility
            if (agentInvincibilityTimers.TryGetValue(key, out float timer))
            {
                if (Time.time < timer)
                {
                    if (logDamageEvents) Debug.Log($"  Agent {agentIndex} on {controller.name} is invincible (remaining: {timer - Time.time:F2}s)");
                    return; // Still invincible
                }
            }
            
            // Apply damage multiplier
            float finalDamage = damage * damageMultiplier;
            
            if (logDamageEvents)
            {
                bool isAlreadyDead = controller.IsAgentDead(agentIndex);
                Debug.Log($"  <color=cyan>Applying {finalDamage:F1} damage</color> to agent {agentIndex} on {controller.name} (alreadyDead={isAlreadyDead})");
            }
            
            // Apply damage to the agent
            bool killed = ApplyDamageToAgent(controller, agentIndex, finalDamage);
            
            // Set invincibility
            agentInvincibilityTimers[key] = Time.time + invincibilityDuration;
            
            // Fire events
            OnEnemyDamaged?.Invoke(agentIndex, finalDamage, sourceSkill);
            
            if (killed)
            {
                if (logDamageEvents) Debug.Log($"  <color=red>Agent {agentIndex} on {controller.name} KILLED!</color>");
                OnEnemyKilled?.Invoke(agentIndex, sourceSkill);
                agentInvincibilityTimers.Remove(key);
            }
        }
        
        /// <summary>
        /// Apply damage to an agent - uses EnemyMassesCrowdController's TryApplyDamage
        /// </summary>
        protected virtual bool ApplyDamageToAgent(EnemyMassesCrowdController controller, int agentIndex, float damage)
        {
            if (controller == null) return false;
            
            // Use the controller's damage system which handles health, death, effects etc.
            bool result = controller.TryApplyDamage(agentIndex, damage, allowBlock: false, isPlayerAttack: true);
            
            if (logDamageEvents)
            {
                Debug.Log($"    TryApplyDamage result: {result} (controller={controller.name}, agent={agentIndex}, damage={damage:F1})");
            }
            
            return result;
        }

        #region Knockback / NavMesh Impulse

        /// <summary>
        /// Apply a fake-physics knockback impulse to an agent using the NavMesh offset system.
        /// This pushes agents without requiring rigidbodies - they stay on the NavMesh while visually moving.
        /// </summary>
        /// <param name="agentIndex">Agent index in the cache</param>
        /// <param name="direction">Direction of the knockback (will be normalized)</param>
        /// <param name="pushForce">Horizontal push strength (m/s)</param>
        /// <param name="liftForce">Vertical lift strength (m/s), default 0 for ground-only push</param>
        /// <param name="duration">How long the impulse runs (default 0.5s)</param>
        /// <param name="gravity">Downward gravity during impulse (default 25)</param>
        /// <param name="drag">Horizontal damping (default 4)</param>
        /// <param name="overrideActive">If false, won't override an existing impulse</param>
        public void ApplyKnockback(int agentIndex, Vector3 direction, float pushForce, float liftForce = 0f, 
            float duration = 0.5f, float gravity = 25f, float drag = 4f, bool overrideActive = true)
        {
            foreach (var cached in cachedAgents)
            {
                if (cached.index != agentIndex || !cached.isAlive) continue;
                ApplyKnockback(cached.controller, agentIndex, direction, pushForce, liftForce, duration, gravity, drag, overrideActive);
                return;
            }
        }

        /// <summary>
        /// Apply a fake-physics knockback impulse to an agent using AgentReference.
        /// </summary>
        public void ApplyKnockback(AgentReference agent, Vector3 direction, float pushForce, float liftForce = 0f,
            float duration = 0.5f, float gravity = 25f, float drag = 4f, bool overrideActive = true)
        {
            if (!agent.IsValid) return;
            ApplyKnockback(agent.controller, agent.index, direction, pushForce, liftForce, duration, gravity, drag, overrideActive);
        }

        /// <summary>
        /// Apply a fake-physics knockback impulse to an agent on a specific controller.
        /// </summary>
        public void ApplyKnockback(EnemyMassesCrowdController controller, int agentIndex, Vector3 direction, float pushForce,
            float liftForce = 0f, float duration = 0.5f, float gravity = 25f, float drag = 4f, bool overrideActive = true)
        {
            if (controller == null) return;
            
            var agent = controller.GetAgent(agentIndex);
            if (agent == null || agent.isDead) return;
            
            float now = Time.time;
            
            // Check if we should override existing impulse
            if (!overrideActive && agent.hasNavmeshOffsetImpulse && now < agent.navmeshOffsetEndTime)
                return;
            
            // Normalize direction and compute velocity
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (horizontal == Vector3.zero)
                horizontal = Vector3.forward;
            
            Vector3 velocity = horizontal * pushForce;
            velocity.y += liftForce;
            
            // Get agent's current position for ground reference
            var navigationAgent = agent.navigationAgent;
            bool hasNav = navigationAgent != null;
            Vector3 agentPos = hasNav ? agent.WorldPosition : (agent.transform != null ? agent.transform.position : Vector3.zero);

            bool handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                agent,
                velocity,
                duration,
                drag,
                gravity,
                pushForce,
                liftForce,
                controller);

            if (!handledByAstar)
            {
                // Apply the fake-physics impulse
                agent.hasNavmeshOffsetImpulse = true;
                agent.navmeshOffset = Vector3.zero;
                agent.navmeshOffsetVelocity = velocity;
                agent.navmeshOffsetGroundPosition = hasNav ? navigationAgent.Position : agentPos;
                agent.navmeshOffsetGravity = gravity;
                agent.navmeshOffsetDrag = drag;
                agent.navmeshOffsetDuration = duration;
                agent.navmeshOffsetEndTime = now + duration;
                
                // Store impulse values for animation system
                agent.lastNavmeshImpulsePush = pushForce;
                agent.lastNavmeshImpulseLift = liftForce;
                agent.navmeshImpulseAnimationPlayed = false;
                
                // Mark lift impulse for get-up flow (threshold from InstructionCrowdNavmeshImpulse)
                const float LiftGetUpThreshold = 1.5f;
                agent.liftImpulseActive = liftForce >= LiftGetUpThreshold;
            }
            
            // Lock attacks during knockback
            float lockEnd = now + duration + 0.5f;
            agent.impulseAttackLockEndTime = Mathf.Max(agent.impulseAttackLockEndTime, lockEnd);
            
            if (logDamageEvents)
            {
                Debug.Log($"[PlayerSkillManager] Applied knockback to agent {agentIndex}: vel={velocity}, duration={duration}, gravity={gravity}, drag={drag}");
            }
        }

        /// <summary>
        /// Apply knockback to all agents within a radius, pushing them away from the center.
        /// </summary>
        /// <param name="center">Center point of the knockback (agents are pushed away from here)</param>
        /// <param name="radius">Radius to affect agents</param>
        /// <param name="pushForce">Horizontal push strength (m/s)</param>
        /// <param name="liftForce">Vertical lift strength (m/s)</param>
        /// <param name="duration">How long the impulse runs</param>
        /// <param name="gravity">Downward gravity during impulse</param>
        /// <param name="drag">Horizontal damping</param>
        /// <param name="overrideActive">If false, won't override existing impulses</param>
        /// <returns>Number of agents affected</returns>
        public int ApplyKnockbackInRadius(Vector3 center, float radius, float pushForce, float liftForce = 0f,
            float duration = 0.5f, float gravity = 25f, float drag = 4f, bool overrideActive = true)
        {
            var agents = GetAgentsInRangeWithController(center, radius);
            int affected = 0;
            
            foreach (var (controller, index) in agents)
            {
                Vector3 agentPos = GetAgentPosition(controller, index);
                Vector3 direction = (agentPos - center).normalized;
                if (direction == Vector3.zero) direction = Vector3.forward;
                
                ApplyKnockback(controller, index, direction, pushForce, liftForce, duration, gravity, drag, overrideActive);
                affected++;
            }
            
            return affected;
        }

        #endregion

        /// <summary>
        /// Update the enemy position cache from ALL crowd controllers
        /// </summary>
        private void UpdateEnemyCache()
        {
            cachedAgents.Clear();
            
            int totalCached = 0;
            int aliveCount = 0;
            int skippedDead = 0;
            var perControllerStats = new Dictionary<string, (int total, int alive, int skipped)>();
            
            // Determine effective max (0 means unlimited)
            int effectiveMax = maxTrackedAgents <= 0 ? int.MaxValue : maxTrackedAgents;
            
            // Iterate through all crowd controllers
            foreach (var controller in crowdControllers)
            {
                if (controller == null) continue;
                
                int agentCount = controller.GetAgentCount();
                int controllerAlive = 0;
                int controllerTotal = 0;
                int controllerSkippedDead = 0;
                int skippedNull = 0;
                
                for (int i = 0; i < agentCount && totalCached < effectiveMax; i++)
                {
                    var agent = controller.GetAgent(i);
                    if (agent == null)
                    {
                        skippedNull++;
                        continue;
                    }
                    
                    // Note: agent.index might not always match i - we use i for consistency
                    // because that's what the controller expects for damage/queries
                    bool isAlive = !controller.IsAgentDead(i);
                    
                    // OPTIMIZATION: Skip dead agents if enabled (saves cache slots for alive agents)
                    if (onlyCacheAliveAgents && !isAlive)
                    {
                        controllerSkippedDead++;
                        skippedDead++;
                        continue;
                    }
                    
                    cachedAgents.Add(new CachedAgentData
                    {
                        controller = controller,
                        index = i,  // Use loop index, not agent.index
                        position = GetAgentPosition(controller, i),
                        isAlive = isAlive
                    });
                    totalCached++;
                    controllerTotal++;
                    if (isAlive)
                    {
                        aliveCount++;
                        controllerAlive++;
                    }
                }
                
                perControllerStats[controller.name] = (controllerTotal, controllerAlive, controllerSkippedDead);
                
                if (logCacheUpdates && skippedNull > 0)
                {
                    Debug.LogWarning($"[PlayerSkillManager] {controller.name}: Skipped {skippedNull} null agents");
                }
            }
            
            // Check if we hit the max limit (only relevant if max > 0)
            if (maxTrackedAgents > 0 && totalCached >= maxTrackedAgents)
            {
                int actualAlive = 0;
                foreach (var ctrl in crowdControllers)
                {
                    if (ctrl == null) continue;
                    for (int i = 0; i < ctrl.GetAgentCount(); i++)
                    {
                        if (!ctrl.IsAgentDead(i)) actualAlive++;
                    }
                }
                if (actualAlive > totalCached)
                {
                    Debug.LogWarning($"[PlayerSkillManager] <color=orange>MAX TRACKED AGENTS LIMIT HIT!</color> Tracking {totalCached}/{actualAlive} alive agents. Increase 'Max Tracked Agents' or set to 0 (unlimited)!");
                }
            }
            
            if (logCacheUpdates)
            {
                string skippedInfo = onlyCacheAliveAgents ? $", skipped {skippedDead} dead" : "";
                Debug.Log($"[PlayerSkillManager] Cache updated: {totalCached} agents ({aliveCount} alive{skippedInfo}) from {crowdControllers.Count} controllers");
                foreach (var kvp in perControllerStats)
                {
                    string skipInfo = onlyCacheAliveAgents ? $", {kvp.Value.skipped} dead skipped" : "";
                    Debug.Log($"  {kvp.Key}: {kvp.Value.total} cached, {kvp.Value.alive} alive{skipInfo}");
                }
            }
        }
        
        private void UpdateInvincibilityTimers()
        {
            // Remove expired timers
            var expiredKeys = new List<(EnemyMassesCrowdController, int)>();
            foreach (var kvp in agentInvincibilityTimers)
            {
                if (Time.time >= kvp.Value || kvp.Key.controller == null)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            foreach (var key in expiredKeys)
            {
                agentInvincibilityTimers.Remove(key);
            }
        }
        
        /// <summary>
        /// Get all agents within range of a position (returns indices only - for backwards compatibility)
        /// </summary>
        public List<int> GetAgentsInRange(Vector3 position, float range)
        {
            var result = new List<int>();
            float rangeSqr = range * range;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr <= rangeSqr)
                {
                    result.Add(agent.index);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get all agents within range as AgentReferences (recommended for multi-crowd support)
        /// </summary>
        public List<AgentReference> GetAgentReferencesInRange(Vector3 position, float range)
        {
            var result = new List<AgentReference>();
            float rangeSqr = range * range;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr <= rangeSqr)
                {
                    result.Add(new AgentReference(agent.controller, agent.index, agent.position));
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get all agents within range with their controller references (recommended for multi-crowd support)
        /// </summary>
        public List<(EnemyMassesCrowdController controller, int index)> GetAgentsInRangeWithController(Vector3 position, float range)
        {
            var result = new List<(EnemyMassesCrowdController, int)>();
            float rangeSqr = range * range;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr <= rangeSqr)
                {
                    result.Add((agent.controller, agent.index));
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get all agents within range with their distances
        /// </summary>
        public List<(int index, float distance)> GetAgentsInRangeWithDistance(Vector3 position, float range)
        {
            var result = new List<(int, float)>();
            float rangeSqr = range * range;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr <= rangeSqr)
                {
                    result.Add((agent.index, Mathf.Sqrt(distSqr)));
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get all agents within range with controller references and distances (recommended)
        /// </summary>
        public List<(EnemyMassesCrowdController controller, int index, float distance)> GetAgentsInRangeWithControllerAndDistance(Vector3 position, float range)
        {
            var result = new List<(EnemyMassesCrowdController, int, float)>();
            float rangeSqr = range * range;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr <= rangeSqr)
                {
                    result.Add((agent.controller, agent.index, Mathf.Sqrt(distSqr)));
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get the closest agent to a position (returns index only - for backwards compatibility)
        /// </summary>
        public int GetClosestAgent(Vector3 position, float maxRange)
        {
            int closest = -1;
            float closestDistSqr = maxRange * maxRange;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closest = agent.index;
                }
            }
            
            return closest;
        }
        
        /// <summary>
        /// Get the closest agent with controller reference (recommended for multi-crowd support)
        /// </summary>
        public (EnemyMassesCrowdController controller, int index) GetClosestAgentWithController(Vector3 position, float maxRange)
        {
            EnemyMassesCrowdController closestController = null;
            int closestIndex = -1;
            float closestDistSqr = maxRange * maxRange;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closestController = agent.controller;
                    closestIndex = agent.index;
                }
            }
            
            return (closestController, closestIndex);
        }
        
        /// <summary>
        /// Get the closest agent as AgentReference (recommended for multi-crowd support)
        /// </summary>
        public AgentReference GetClosestAgentReference(Vector3 position, float maxRange)
        {
            AgentReference closest = default;
            float closestDistSqr = maxRange * maxRange;
            
            foreach (var agent in cachedAgents)
            {
                if (!agent.isAlive) continue;
                
                float distSqr = (agent.position - position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closest = new AgentReference(agent.controller, agent.index, agent.position);
                }
            }
            
            return closest;
        }
        
        /// <summary>
        /// Get random agents within range
        /// </summary>
        public List<int> GetRandomAgentsInRange(Vector3 position, float range, int count)
        {
            var inRange = GetAgentsInRange(position, range);
            
            if (inRange.Count <= count)
            {
                return inRange;
            }
            
            // Fisher-Yates shuffle and take first 'count'
            for (int i = inRange.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (inRange[i], inRange[j]) = (inRange[j], inRange[i]);
            }
            
            return inRange.GetRange(0, count);
        }
        
        /// <summary>
        /// Get agent position from transform (searches all controllers)
        /// </summary>
        public Vector3 GetAgentPosition(int agentIndex)
        {
            // Search cache first
            foreach (var cached in cachedAgents)
            {
                if (cached.index == agentIndex)
                {
                    return cached.position;
                }
            }
            
            // Fallback to first controller
            if (crowdControllers.Count > 0)
            {
                return GetAgentPosition(crowdControllers[0], agentIndex);
            }
            
            return Vector3.zero;
        }
        
        /// <summary>
        /// Get agent position from a specific controller
        /// </summary>
        public Vector3 GetAgentPosition(EnemyMassesCrowdController controller, int agentIndex)
        {
            if (controller == null) return Vector3.zero;
            
            var agent = controller.GetAgent(agentIndex);
            if (agent == null) return Vector3.zero;
            if (agent.navigationAgent != null) return agent.WorldPosition;
            if (agent.transform != null) return agent.transform.position;

            return Vector3.zero;
        }
        
        /// <summary>
        /// Check if agent is alive (searches all controllers)
        /// </summary>
        public bool IsAgentAlive(int agentIndex)
        {
            foreach (var cached in cachedAgents)
            {
                if (cached.index == agentIndex)
                {
                    return cached.isAlive;
                }
            }
            
            // Fallback to first controller
            if (crowdControllers.Count > 0)
            {
                return !crowdControllers[0].IsAgentDead(agentIndex);
            }
            
            return false;
        }
        
        /// <summary>
        /// Get total enemy count in range
        /// </summary>
        public int GetEnemyCountInRange(Vector3 position, float range)
        {
            return GetAgentsInRange(position, range).Count;
        }
        
        /// <summary>
        /// Deal damage to all agents within range (uses controller references for multi-crowd support)
        /// </summary>
        public int DealDamageToAgentsInRange(Vector3 position, float range, float damage, BaseSkill sourceSkill = null)
        {
            var agents = GetAgentsInRangeWithController(position, range);
            
            foreach (var (controller, index) in agents)
            {
                DealDamageToAgent(controller, index, damage, sourceSkill);
            }
            
            return agents.Count;
        }
        
        /// <summary>
        /// Get the total number of alive agents across all crowd controllers
        /// </summary>
        public int GetTotalAliveAgentCount()
        {
            int count = 0;
            foreach (var cached in cachedAgents)
            {
                if (cached.isAlive) count++;
            }
            return count;
        }
        
        /// <summary>
        /// Get the number of crowd controllers currently tracked
        /// </summary>
        public int GetCrowdControllerCount()
        {
            return crowdControllers.Count;
        }
        
        #region Debug Methods
        
        /// <summary>
        /// Dump current state to console for debugging
        /// </summary>
        [ContextMenu("Debug: Dump State")]
        public void DebugDumpState()
        {
            Debug.Log("========== PlayerSkillManager State ==========");
            Debug.Log($"Controllers: {crowdControllers.Count}");
            
            for (int i = 0; i < crowdControllers.Count; i++)
            {
                var ctrl = crowdControllers[i];
                if (ctrl == null)
                {
                    Debug.Log($"  [{i}] NULL");
                    continue;
                }
                
                int agentCount = ctrl.GetAgentCount();
                int aliveCount = 0;
                for (int j = 0; j < agentCount; j++)
                {
                    if (!ctrl.IsAgentDead(j)) aliveCount++;
                }
                Debug.Log($"  [{i}] {ctrl.name}: {agentCount} agents, {aliveCount} alive (InstanceID: {ctrl.GetInstanceID()})");
            }
            
            Debug.Log($"\nCached Agents: {cachedAgents.Count}");
            int cachedAlive = 0;
            var perController = new Dictionary<string, int>();
            foreach (var cached in cachedAgents)
            {
                if (cached.isAlive) cachedAlive++;
                string key = cached.controller != null ? cached.controller.name : "NULL";
                if (!perController.ContainsKey(key)) perController[key] = 0;
                perController[key]++;
            }
            Debug.Log($"  Alive in cache: {cachedAlive}");
            foreach (var kvp in perController)
            {
                Debug.Log($"  {kvp.Key}: {kvp.Value} cached");
            }
            
            Debug.Log($"\nActive Skills: {activeSkills.Count}");
            foreach (var skill in activeSkills)
            {
                if (skill == null) continue;
                Debug.Log($"  {skill.SkillName} (enabled={skill.gameObject.activeInHierarchy}, level={skill.CurrentLevel})");
            }
            
            Debug.Log("==============================================");
        }
        
        /// <summary>
        /// Force refresh controllers and cache immediately
        /// </summary>
        [ContextMenu("Debug: Force Refresh")]
        public void DebugForceRefresh()
        {
            Debug.Log("[PlayerSkillManager] Force refreshing...");
            RefreshCrowdControllers();
            UpdateEnemyCache();
            DebugDumpState();
        }
        
        /// <summary>
        /// Test damage on all agents of first controller
        /// </summary>
        [ContextMenu("Debug: Test Damage (10 dmg to all in 20m range)")]
        public void DebugTestDamage()
        {
            Debug.Log("[PlayerSkillManager] Testing damage...");
            int count = DealDamageToAgentsInRange(PlayerPosition, 20f, 10f, null);
            Debug.Log($"[PlayerSkillManager] Damaged {count} agents");
        }
        
        #endregion
    }
}
