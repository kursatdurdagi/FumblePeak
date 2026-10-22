using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Tracks EnemyCrowd agents across multiple controllers and provides efficient
    /// range queries and damage dealing. This is the foundation for any system
    /// that needs to interact with EnemyCrowd agents (skills, melee combat, etc.)
    /// </summary>
    public class CrowdAgentTracker : MonoBehaviour
    {
        #region Static Instance
        
        /// <summary>
        /// Singleton-like access to the first active CrowdAgentTracker in the scene.
        /// Returns null if none exists.
        /// </summary>
        public static CrowdAgentTracker Instance { get; private set; }
        
        #endregion
        
        #region Serialized Fields
        
        [Header("Controller References")]
        [Tooltip("Optional: Manually assign crowd controllers. If empty, all controllers will be auto-found.")]
        [SerializeField] protected List<EnemyMassesCrowdController> crowdControllers = new List<EnemyMassesCrowdController>();
        
        [Tooltip("Auto-find new crowd controllers spawned at runtime")]
        [SerializeField] protected bool autoFindNewControllers = true;
        
        [Tooltip("How often to check for new crowd controllers (seconds)")]
        [SerializeField] protected float controllerScanInterval = 1f;
        
        [Header("Cache Settings")]
        [Tooltip("How often to update enemy position cache (seconds)")]
        [SerializeField] protected float cacheUpdateRate = 0.1f;
        
        [Tooltip("Maximum number of agents to track (0 = unlimited)")]
        [SerializeField] protected int maxTrackedAgents = 0;
        
        [Tooltip("Only cache ALIVE agents (recommended - dead agents don't need tracking)")]
        [SerializeField] protected bool onlyCacheAliveAgents = true;
        
        [Header("Damage Settings")]
        [Tooltip("Global damage multiplier")]
        [SerializeField] protected float damageMultiplier = 1f;
        
        [Tooltip("Invincibility time after being hit")]
        [SerializeField] protected float invincibilityDuration = 0.5f;
        
        [Header("Debug")]
        [Tooltip("Enable verbose debug logging")]
        [SerializeField] protected bool enableDebugLogs = false;
        
        [Tooltip("Log damage events")]
        [SerializeField] protected bool logDamageEvents = false;
        
        [Tooltip("Log cache updates")]
        [SerializeField] protected bool logCacheUpdates = false;
        
        [Tooltip("Log periodic status every N seconds (0 = disabled)")]
        [SerializeField] protected float periodicStatusInterval = 0f;
        
        #endregion
        
        #region Private Fields
        
        protected List<CachedAgentData> cachedAgents = new List<CachedAgentData>();
        protected Dictionary<(EnemyMassesCrowdController controller, int index), float> agentInvincibilityTimers = 
            new Dictionary<(EnemyMassesCrowdController, int), float>();
        
        protected float lastCacheUpdate = -999f;
        protected float lastControllerScan = -999f;
        protected float lastPeriodicStatusTime = -999f;
        
        #endregion
        
        #region Properties
        
        /// <summary>Returns the first crowd controller (for backwards compatibility)</summary>
        public EnemyMassesCrowdController CrowdController => crowdControllers.Count > 0 ? crowdControllers[0] : null;
        
        /// <summary>Returns all active crowd controllers</summary>
        public IReadOnlyList<EnemyMassesCrowdController> CrowdControllers => crowdControllers;
        
        /// <summary>Returns cached agent data</summary>
        public IReadOnlyList<CachedAgentData> CachedAgents => cachedAgents;
        
        /// <summary>Global damage multiplier</summary>
        public float DamageMultiplier { get => damageMultiplier; set => damageMultiplier = Mathf.Max(0, value); }
        
        /// <summary>Number of agents currently cached</summary>
        public int CachedAgentCount => cachedAgents.Count;
        
        /// <summary>Number of alive agents in cache</summary>
        public int AliveAgentCount
        {
            get
            {
                int count = 0;
                foreach (var agent in cachedAgents)
                {
                    if (agent.isAlive) count++;
                }
                return count;
            }
        }
        
        #endregion
        
        #region Events
        
        /// <summary>Invoked when an enemy is damaged</summary>
        public System.Action<EnemyMassesCrowdController, int, float> OnAgentDamaged;
        
        /// <summary>Invoked when an enemy is killed</summary>
        public System.Action<EnemyMassesCrowdController, int> OnAgentKilled;
        
        #endregion
        
        #region Unity Lifecycle
        
        protected virtual void Awake()
        {
            // Set static instance (first one wins)
            if (Instance == null)
            {
                Instance = this;
            }
            
            RefreshCrowdControllers();
        }
        
        protected virtual void OnDestroy()
        {
            // Clear static instance if this was it
            if (Instance == this)
            {
                Instance = null;
            }
        }
        
        protected virtual void Update()
        {
            // Periodically scan for new crowd controllers
            if (autoFindNewControllers && Time.time - lastControllerScan >= controllerScanInterval)
            {
                RefreshCrowdControllers();
                lastControllerScan = Time.time;
            }
            
            // Update enemy position cache
            if (Time.time - lastCacheUpdate >= cacheUpdateRate)
            {
                UpdateCache();
                lastCacheUpdate = Time.time;
            }
            
            // Periodic status log
            if (periodicStatusInterval > 0 && Time.time - lastPeriodicStatusTime >= periodicStatusInterval)
            {
                LogPeriodicStatus();
                lastPeriodicStatusTime = Time.time;
            }
            
            // Update invincibility timers
            UpdateInvincibilityTimers();
        }
        
        #endregion
        
        #region Controller Management
        
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
                Debug.Log($"[CrowdAgentTracker] Removed {removedCount} null/destroyed controllers");
            }
            
            // Find all controllers in scene
            var allControllers = FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            
            foreach (var controller in allControllers)
            {
                if (!crowdControllers.Contains(controller))
                {
                    crowdControllers.Add(controller);
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[CrowdAgentTracker] <color=green>Found NEW crowd controller:</color> {controller.name}");
                    }
                }
            }
            
            if (enableDebugLogs && crowdControllers.Count != previousCount)
            {
                Debug.Log($"[CrowdAgentTracker] Total crowd controllers: {crowdControllers.Count}");
            }
        }
        
        /// <summary>
        /// Get the number of crowd controllers
        /// </summary>
        public int GetCrowdControllerCount()
        {
            return crowdControllers.Count;
        }
        
        #endregion
        
        #region Cache Management
        
        /// <summary>
        /// Update the agent position cache from all crowd controllers
        /// </summary>
        protected virtual void UpdateCache()
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
                    
                    bool isAlive = !controller.IsAgentDead(i);
                    
                    // Skip dead agents if enabled
                    if (onlyCacheAliveAgents && !isAlive)
                    {
                        controllerSkippedDead++;
                        skippedDead++;
                        continue;
                    }
                    
                    cachedAgents.Add(new CachedAgentData
                    {
                        controller = controller,
                        index = i,
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
                    Debug.LogWarning($"[CrowdAgentTracker] {controller.name}: Skipped {skippedNull} null agents");
                }
            }
            
            // Check if we hit the max limit
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
                    Debug.LogWarning($"[CrowdAgentTracker] <color=orange>MAX TRACKED AGENTS LIMIT HIT!</color> Tracking {totalCached}/{actualAlive} alive agents.");
                }
            }
            
            if (logCacheUpdates)
            {
                string skippedInfo = onlyCacheAliveAgents ? $", skipped {skippedDead} dead" : "";
                // Debug log removed for production build
            }
        }
        
        protected virtual void UpdateInvincibilityTimers()
        {
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
        
        protected virtual void LogPeriodicStatus()
        {
            int aliveCount = 0;
            int deadCount = 0;
            
            foreach (var cached in cachedAgents)
            {
                if (cached.isAlive)
                    aliveCount++;
                else
                    deadCount++;
            }
            
            Debug.Log($"[CrowdAgentTracker] STATUS - Controllers: {crowdControllers.Count}, Cached: {cachedAgents.Count} (alive: {aliveCount}, dead: {deadCount})");
        }
        
        #endregion
        
        #region Range Queries
        
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
        /// Get total enemy count in range
        /// </summary>
        public int GetEnemyCountInRange(Vector3 position, float range)
        {
            return GetAgentsInRange(position, range).Count;
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
        
        #endregion
        
        #region Agent Position Helpers
        
        /// <summary>
        /// Get agent position from cache or controller (searches all controllers)
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
            
            return agent.WorldPosition;
        }
        
        /// <summary>
        /// Check if agent is alive (searches cache first, then controllers)
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
        /// Check if agent is alive on a specific controller
        /// </summary>
        public bool IsAgentAlive(EnemyMassesCrowdController controller, int agentIndex)
        {
            if (controller == null) return false;
            return !controller.IsAgentDead(agentIndex);
        }
        
        #endregion
        
        #region Damage Dealing
        
        /// <summary>
        /// Deal damage using an AgentReference (recommended for multi-crowd support)
        /// </summary>
        public void DealDamageToAgent(AgentReference agent, float damage)
        {
            if (!agent.IsValid) return;
            DealDamageToAgent(agent.controller, agent.index, damage);
        }
        
        /// <summary>
        /// Deal damage to an EnemyCrowd agent on a specific controller
        /// </summary>
        public void DealDamageToAgent(EnemyMassesCrowdController controller, int agentIndex, float damage)
        {
            if (controller == null)
            {
                if (logDamageEvents) Debug.LogWarning($"[CrowdAgentTracker] DealDamageToAgent called with null controller!");
                return;
            }
            
            var key = (controller, agentIndex);
            
            // Check invincibility
            if (agentInvincibilityTimers.TryGetValue(key, out float timer))
            {
                if (Time.time < timer)
                {
                    if (logDamageEvents) Debug.Log($"[CrowdAgentTracker] Agent {agentIndex} is invincible");
                    return;
                }
            }
            
            // Apply damage multiplier
            float finalDamage = damage * damageMultiplier;
            
            if (logDamageEvents)
            {
                Debug.Log($"[CrowdAgentTracker] Applying {finalDamage:F1} damage to agent {agentIndex} on {controller.name}");
            }
            
            // Apply damage
            bool killed = ApplyDamageToAgent(controller, agentIndex, finalDamage);
            
            // Set invincibility
            agentInvincibilityTimers[key] = Time.time + invincibilityDuration;
            
            // Fire events
            OnAgentDamaged?.Invoke(controller, agentIndex, finalDamage);
            
            if (killed)
            {
                OnAgentKilled?.Invoke(controller, agentIndex);
            }
        }
        
        /// <summary>
        /// Deal damage to all agents within range
        /// </summary>
        public int DealDamageToAgentsInRange(Vector3 position, float range, float damage)
        {
            var agents = GetAgentsInRangeWithController(position, range);
            
            foreach (var (controller, index) in agents)
            {
                DealDamageToAgent(controller, index, damage);
            }
            
            return agents.Count;
        }
        
        /// <summary>
        /// Apply damage to an agent using the controller's damage system
        /// </summary>
        protected virtual bool ApplyDamageToAgent(EnemyMassesCrowdController controller, int agentIndex, float damage)
        {
            if (controller == null) return false;
            
            // Use the controller's damage system
            bool result = controller.TryApplyDamage(agentIndex, damage, allowBlock: false);
            
            if (logDamageEvents)
            {
                Debug.Log($"[CrowdAgentTracker] TryApplyDamage result: {result}");
            }
            
            return result;
        }
        
        #endregion
        
        #region Debug Methods
        
        /// <summary>
        /// Dump current state to console for debugging
        /// </summary>
        [ContextMenu("Debug: Dump State")]
        public virtual void DebugDumpState()
        {
            Debug.Log("========== CrowdAgentTracker State ==========");
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
                Debug.Log($"  [{i}] {ctrl.name}: {agentCount} agents, {aliveCount} alive");
            }
            
            Debug.Log($"\nCached Agents: {cachedAgents.Count}");
            int cachedAlive = 0;
            foreach (var cached in cachedAgents)
            {
                if (cached.isAlive) cachedAlive++;
            }
            Debug.Log($"  Alive in cache: {cachedAlive}");
            
            Debug.Log("==============================================");
        }
        
        /// <summary>
        /// Force refresh controllers and cache immediately
        /// </summary>
        [ContextMenu("Debug: Force Refresh")]
        public void DebugForceRefresh()
        {
            Debug.Log("[CrowdAgentTracker] Force refreshing...");
            RefreshCrowdControllers();
            UpdateCache();
            DebugDumpState();
        }
        
        /// <summary>
        /// Test damage on agents in range
        /// </summary>
        [ContextMenu("Debug: Test Damage (10 dmg at origin, 20m range)")]
        public void DebugTestDamage()
        {
            Debug.Log("[CrowdAgentTracker] Testing damage at origin...");
            int count = DealDamageToAgentsInRange(Vector3.zero, 20f, 10f);
            Debug.Log($"[CrowdAgentTracker] Damaged {count} agents");
        }
        
        #endregion
    }
}
