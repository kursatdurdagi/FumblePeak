using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using GPUInstancerPro.CrowdAnimations;
using EnemyMasses.VFX;
using Arawn.EnemyMasses.Runtime.Navigation;

namespace Arawn.EnemyMasses.Runtime.Civil
{
    /// <summary>
    /// Handles civil NPC navigation, activities, and lifecycle management.
    /// Integrates with the existing EnemyMasses crowd system.
    /// </summary>
    public class CivilNavigationController
    {
        private readonly EnemyMassesCrowdController _crowdController;
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        
        // Civil agent states - indexed by agent index
        private readonly Dictionary<int, CivilAgentState> _civilStates = new Dictionary<int, CivilAgentState>();
        
        // Object pools for props
        private readonly Dictionary<GameObject, Queue<GameObject>> _propPools = new Dictionary<GameObject, Queue<GameObject>>();
        
        // Update throttling
        private int _updateCursor;
        private int _activityUpdateCursor;
        private float _lastDistantUpdateTime;
        
        // Cached waypoint manager reference
        private CivilWaypointManager _waypointManager;
        
        // Social interaction manager
        private CivilSocialInteractionManager _socialInteractionManager;

        // Shared VFX spawner for social interaction effects
        private readonly VFXSpawner _socialVfxSpawner = new VFXSpawner();
        
        // Material property for fading
        private static readonly int AlphaPropertyId = Shader.PropertyToID("_Alpha");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_BaseColor");
        
        /// <summary>
        /// Creates a new civil navigation controller.
        /// </summary>
        public CivilNavigationController(
            EnemyMassesCrowdController crowdController,
            List<CrowdAgent> agents,
            List<EnemyCrowd> enemyCrowds)
        {
            _crowdController = crowdController;
            _agents = agents;
            _enemyCrowds = enemyCrowds;
        }
        
        /// <summary>
        /// Initializes the civil navigation system.
        /// </summary>
        public void Initialize()
        {
            // Try to get waypoint manager - may not be available yet during startup
            TryGetWaypointManager();
            
            // Initialize social interaction manager
            _socialInteractionManager = new CivilSocialInteractionManager(this, _agents, _enemyCrowds);
        }
        
        /// <summary>
        /// Attempts to get the waypoint manager reference if not already set.
        /// </summary>
        private void TryGetWaypointManager()
        {
            if (_waypointManager != null)
                return;
            
            _waypointManager = CivilWaypointManager.Instance;
            
            if (_waypointManager == null)
            {
                _waypointManager = Object.FindFirstObjectByType<CivilWaypointManager>();
            }
        }
        
        /// <summary>
        /// Updates all civil agents. Call from main update loop.
        /// </summary>
        public void Update(float deltaTime)
        {
            // Try to get waypoint manager if we don't have it yet
            if (_waypointManager == null)
            {
                TryGetWaypointManager();
                
                if (_waypointManager == null)
                {
                    return;
                }
            }
            
            if (!_waypointManager.IsInitialized)
            {
                return;
            }
            
            // Process auto-spawning from spawn waypoints
            ProcessAutoSpawning();
            
            int agentCount = _agents.Count;
            if (agentCount == 0)
                return;
            
            // Track max updates per frame from the first civil-enabled faction
            int maxDecisions = 100;
            int maxActivityUpdates = 50;
            float distantUpdateInterval = 0.5f;
            
            // Find settings from first civil-enabled faction
            foreach (var crowd in _enemyCrowds)
            {
                if (crowd != null &&
                    crowd.civilSettings != null &&
                    crowd.civilSettings.enableCivilMode &&
                    SupportsCivilNavigationBackend(crowd))
                {
                    maxDecisions = crowd.civilSettings.maxDecisionsPerFrame;
                    maxActivityUpdates = crowd.civilSettings.maxActivityUpdatesPerFrame;
                    distantUpdateInterval = crowd.civilSettings.distantUpdateInterval;
                    break;
                }
            }
            
            int decisionsThisFrame = 0;
            int activityUpdatesThisFrame = 0;
            float currentTime = Time.time;
            
            // Process agents in round-robin fashion
            for (int i = 0; i < agentCount && decisionsThisFrame < maxDecisions; i++)
            {
                int agentIndex = (_updateCursor + i) % agentCount;
                CrowdAgent agent = _agents[agentIndex];
                
                if (agent == null || agent.isDead)
                    continue;
                
                // Check if this agent has civil mode enabled
                EnemyCrowd crowd = GetAgentCrowd(agent);
                if (crowd == null || crowd.civilSettings == null || !crowd.civilSettings.enableCivilMode)
                    continue;

                if (!SupportsCivilNavigationBackend(crowd))
                    continue;
                
                // Get or create civil state
                if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                {
                    state = InitializeCivilState(agent, crowd);
                    _civilStates[agentIndex] = state;
                }
                
                // Update lifetime
                state.lifetime += deltaTime;
                
                // Check if agent needs update (distant agents update less frequently)
                bool isDistant = IsAgentDistant(agent);
                if (isDistant && (currentTime - state.lastUpdateTime) < distantUpdateInterval)
                    continue;
                
                state.lastUpdateTime = currentTime;
                
                // Update based on current state
                switch (state.navigationState)
                {
                    case CivilNavigationState.Idle:
                        HandleIdleState(agent, state, crowd);
                        decisionsThisFrame++;
                        break;
                        
                    case CivilNavigationState.Walking:
                        HandleWalkingState(agent, state, crowd, deltaTime);
                        // Also process fade-in if spawned from frustum (walking while fading in)
                        if (state.isFading && state.fadeDirection > 0)
                        {
                            ProcessFadeOnly(agent, state, crowd, deltaTime);
                        }
                        break;
                        
                    case CivilNavigationState.Waiting:
                        HandleWaitingState(agent, state, crowd, currentTime);
                        break;
                        
                    case CivilNavigationState.Exploring:
                        HandleExploringState(agent, state, crowd, currentTime);
                        decisionsThisFrame++;
                        break;
                        
                    case CivilNavigationState.PerformingActivity:
                        if (activityUpdatesThisFrame < maxActivityUpdates)
                        {
                            HandleActivityState(agent, state, crowd, currentTime, deltaTime);
                            activityUpdatesThisFrame++;
                        }
                        break;
                        
                    case CivilNavigationState.Leaning:
                        HandleLeanState(agent, state, crowd, currentTime);
                        break;

                    case CivilNavigationState.Sitting:
                        HandleSitState(agent, state, crowd, currentTime);
                        break;

                    case CivilNavigationState.SocialInteraction:
                        // Handled by social interaction manager
                        break;
                        
                    case CivilNavigationState.LookingAround:
                        HandleLookAroundState(agent, state, crowd, currentTime);
                        break;
                        
                    case CivilNavigationState.FadingIn:
                    case CivilNavigationState.FadingOut:
                        HandleFadeState(agent, state, crowd, deltaTime);
                        break;
                }
            }
            
            _updateCursor = (_updateCursor + maxDecisions) % Mathf.Max(1, agentCount);
            
            // Update social interactions
            _socialInteractionManager?.Update(deltaTime);
        }
        
        /// <summary>
        /// Initializes a civil agent at a spawn waypoint.
        /// </summary>
        public void SpawnCivilAgent(CrowdAgent agent, CivilWaypointNode spawnWaypoint)
        {
            SpawnCivilAgent(agent, spawnWaypoint, null);
        }
        
        /// <summary>
        /// Initializes a civil agent at a spawn waypoint from a specific spawn entry.
        /// </summary>
        /// <param name="agent">The agent to initialize.</param>
        /// <param name="spawnWaypoint">The spawn waypoint.</param>
        /// <param name="spawnEntry">The spawn entry that triggered this spawn (for tracking).</param>
        public void SpawnCivilAgent(CrowdAgent agent, CivilWaypointNode spawnWaypoint, CivilSpawnEntry spawnEntry)
        {
            EnemyCrowd crowd = GetAgentCrowd(agent);
            if (crowd == null || crowd.civilSettings == null)
                return;

            if (!SupportsCivilNavigationBackend(crowd))
            {
                Debug.LogWarning($"[CivilNavigation] Skipping civil spawn for faction '{crowd.name}' because it requires Unity NavMesh navigation.");
                return;
            }
            
            CivilAgentState state = InitializeCivilState(agent, crowd);
            state.previousWaypoint = spawnWaypoint;
            state.spawnWaypoint = spawnWaypoint;
            state.spawnEntry = spawnEntry;
            state.fadeProgress = 0f;
            state.fadeDirection = 1;
            state.isFading = true;
            
            // Set initial position
            Vector3 spawnPos = spawnWaypoint.GetSpawnPosition();
            if (agent.navAgent != null)
            {
                agent.navAgent.Warp(spawnPos);
            }
            
            // Apply initial fade (invisible)
            ApplyFadeToAgent(agent, 0f);
            
            // Check if this is a Camera Frustum spawn - if so, start walking immediately
            // This prevents the "crowd suddenly starts walking" effect when camera turns
            bool isFrustumSpawn = spawnWaypoint.spawnMode == CivilSpawnMode.CameraFrustumEdge;
            
            if (isFrustumSpawn)
            {
                // Start walking immediately while fading in
                // Agent is invisible but already moving, will fade in while walking
                state.navigationState = CivilNavigationState.Walking;
                state.spawnedOutsideFrustum = true;
                
                // Immediately select and navigate to a waypoint
                SelectAndNavigateToWaypoint(agent, state, crowd, spawnWaypoint);
            }
            else
            {
                // Normal spawn - wait for fade-in to complete before moving
                state.navigationState = CivilNavigationState.FadingIn;
                state.spawnedOutsideFrustum = false;
            }
            
            // Notify spawn waypoint (with entry tracking)
            spawnWaypoint.OnAgentSpawned(spawnEntry);
            
            _civilStates[agent.index] = state;
        }
        
        /// <summary>
        /// Initializes civil state for an agent at their current position (no waypoint required).
        /// Used when converting a combat agent to civil mode at runtime.
        /// </summary>
        public void InitializeCivilAgentAtCurrentPosition(CrowdAgent agent)
        {
            if (agent == null) return;
            
            // Get agent's crowd for settings
            EnemyCrowd crowd = GetAgentCrowd(agent);
            if (crowd == null || !SupportsCivilNavigationBackend(crowd))
            {
                if (crowd != null)
                    Debug.LogWarning($"[CivilNavigation] Cannot initialize civil mode for faction '{crowd.name}' because it requires Unity NavMesh navigation.");
                return;
            }
            
            // Create new civil state
            CivilAgentState state = new CivilAgentState
            {
                navigationState = CivilNavigationState.Idle,
                lifetime = 0f,
                lastUpdateTime = Time.time,
                currentTarget = null,
                previousWaypoint = null,
                spawnWaypoint = null,
                spawnEntry = null,
                fadeProgress = 1f, // Fully visible
                fadeDirection = 0,
                isFading = false,
                spawnedOutsideFrustum = false
            };
            
            // Apply movement style from crowd settings
            if (crowd != null && crowd.civilSettings != null)
            {
                state.currentMovementStyle = crowd.civilSettings.movementStyle;
            }
            
            agent.civilStateIndex = agent.index;
            _civilStates[agent.index] = state;
            
            Debug.Log($"[CivilNavigation] Initialized civil state for agent {agent.index} at current position");
        }
        
        /// <summary>
        /// Removes civil state for an agent (call when agent dies/despawns).
        /// </summary>
        public void RemoveCivilState(int agentIndex)
        {
            if (_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
            {
                // Clean up any active props
                CleanupActivityProps(state);
                
                // Clean up corner smoothing
                if (state.isCornerSmoothing)
                {
                    // Find the agent to restore NavMesh settings
                    CrowdAgent agent = agentIndex < _agents.Count ? _agents[agentIndex] : null;
                    CancelCornerSmoothing(agent, state);
                }

                // Release any lean spot reservation
                if (state.navigationState == CivilNavigationState.Leaning && state.currentTarget != null)
                {
                    state.currentTarget.ReleaseLeanSpot(agentIndex);
                }

                if (state.navigationState == CivilNavigationState.Sitting && state.currentTarget != null)
                {
                    state.currentTarget.ReleaseSitSpot(agentIndex);
                }

                if (state.navigationState == CivilNavigationState.Sitting && state.currentTarget != null)
                {
                    state.currentTarget.ReleaseSitSpot(agentIndex);
                }
                
                // Unregister from current waypoint
                if (state.currentTarget != null)
                {
                    state.currentTarget.UnregisterAgent(agentIndex);
                }
                
                // Notify spawn waypoint that this agent has despawned
                if (state.spawnWaypoint != null)
                {
                    state.spawnWaypoint.OnAgentDespawned(state.spawnEntry);
                }
                
                _civilStates.Remove(agentIndex);
            }
        }
        
        /// <summary>
        /// Gets the civil state for an agent.
        /// </summary>
        public CivilAgentState GetCivilState(int agentIndex)
        {
            _civilStates.TryGetValue(agentIndex, out CivilAgentState state);
            return state;
        }
        
        /// <summary>
        /// Checks if an agent is in civil mode.
        /// </summary>
        public bool IsAgentCivil(int agentIndex)
        {
            return _civilStates.ContainsKey(agentIndex);
        }
        
        #region Auto-Spawning
        
        private float _lastAutoSpawnCheck;
        private const float AUTO_SPAWN_CHECK_INTERVAL = 0.5f;
        
        /// <summary>
        /// Processes automatic spawning from spawn waypoints with configured spawn entries.
        /// </summary>
        private void ProcessAutoSpawning()
        {
            float currentTime = Time.time;
            
            // Rate limit spawn checks
            if (currentTime - _lastAutoSpawnCheck < AUTO_SPAWN_CHECK_INTERVAL)
                return;
            
            _lastAutoSpawnCheck = currentTime;
            
            // Get all spawn waypoints
            var spawnWaypoints = _waypointManager.GetWaypointsByType(CivilWaypointType.Spawn);
            
            foreach (var waypoint in spawnWaypoints)
            {
                if (!waypoint.IsValid)
                {
                    Debug.LogWarning($"[CivilNavigation] Spawn waypoint '{waypoint.name}' is not valid (not on NavMesh?)");
                    continue;
                }
                
                // Get ready spawn entries for this waypoint
                var readyEntries = waypoint.GetReadySpawnEntries();
                
                foreach (var entry in readyEntries)
                {
                    // Spawn the configured count
                    for (int i = 0; i < entry.spawnCount; i++)
                    {
                        bool spawned = TrySpawnFromEntry(waypoint, entry);
                        if (!spawned)
                        {
                            Debug.LogWarning($"[CivilNavigation] Failed to spawn from entry (faction: {entry.factionIndex}) at waypoint '{waypoint.name}'");
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Attempts to spawn an agent from a specific spawn entry.
        /// </summary>
        /// <param name="waypoint">The spawn waypoint.</param>
        /// <param name="entry">The spawn entry configuration.</param>
        /// <returns>True if an agent was spawned, false otherwise.</returns>
        private bool TrySpawnFromEntry(CivilWaypointNode waypoint, CivilSpawnEntry entry)
        {
            // Determine which faction to spawn
            EnemyCrowd targetCrowd = null;
            int targetFactionIndex = entry.factionIndex;
            
            if (entry.factionIndex < 0)
            {
                // Any civil-enabled faction - pick one randomly
                var civilFactions = new List<(EnemyCrowd crowd, int index)>();
                for (int i = 0; i < _enemyCrowds.Count; i++)
                {
                    var crowd = _enemyCrowds[i];
                    if (crowd != null && 
                        crowd.civilSettings != null && 
                        crowd.civilSettings.enableCivilMode &&
                        SupportsCivilNavigationBackend(crowd))
                    {
                        civilFactions.Add((crowd, i));
                    }
                }
                
                if (civilFactions.Count == 0)
                {
                    Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: No civil-enabled factions available");
                    return false;
                }
                
                int randomIndex = Random.Range(0, civilFactions.Count);
                targetCrowd = civilFactions[randomIndex].crowd;
                targetFactionIndex = civilFactions[randomIndex].index;
            }
            else
            {
                // Specific faction
                if (entry.factionIndex >= 0 && entry.factionIndex < _enemyCrowds.Count)
                {
                    targetCrowd = _enemyCrowds[entry.factionIndex];
                }
                else
                {
                    Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: Invalid faction index {entry.factionIndex}, only {_enemyCrowds.Count} crowds available");
                    return false;
                }
            }
            
            if (targetCrowd == null)
            {
                Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: Target crowd is null for faction index {targetFactionIndex}");
                return false;
            }
            
            if (targetCrowd.civilSettings == null)
            {
                Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: Faction '{targetCrowd.name}' has no civilSettings");
                return false;
            }
            
            if (!targetCrowd.civilSettings.enableCivilMode)
            {
                Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: Faction '{targetCrowd.name}' does not have enableCivilMode set to true");
                return false;
            }

            if (!SupportsCivilNavigationBackend(targetCrowd))
            {
                Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: Faction '{targetCrowd.name}' uses {targetCrowd.navigationType} navigation. Civil mode currently requires Unity NavMesh.");
                return false;
            }
            
            // Check if crowd can spawn more (has room in pool)
            if (!CanSpawnMoreForCrowd(targetCrowd))
            {
                Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: Cannot spawn more for crowd '{targetCrowd.name}'");
                return false;
            }
            
            // Spawn the agent via the crowd controller
            Vector3 spawnPos = waypoint.GetSpawnPosition();
            
            CrowdAgent newAgent = SpawnAgentFromCrowd(targetCrowd, targetFactionIndex, spawnPos);
            
            if (newAgent != null)
            {
                // Initialize civil state with spawn tracking
                SpawnCivilAgent(newAgent, waypoint, entry);
                return true;
            }
            
            Debug.LogWarning($"[CivilNavigation] TrySpawnFromEntry: SpawnAgentFromCrowd returned null for '{targetCrowd.name}'");
            return false;
        }
        
        /// <summary>
        /// Checks if more agents can be spawned for a given crowd.
        /// </summary>
        private bool CanSpawnMoreForCrowd(EnemyCrowd crowd)
        {
            // Basic check - can add more sophisticated limits here
            return crowd != null;
        }
        
        /// <summary>
        /// Counts civil agents belonging to a specific crowd.
        /// </summary>
        private int CountCivilAgentsForCrowd(EnemyCrowd crowd)
        {
            int count = 0;
            foreach (var agent in _agents)
            {
                if (agent != null && !agent.isDead && GetAgentCrowd(agent) == crowd)
                {
                    if (_civilStates.ContainsKey(agent.index))
                        count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// Spawns an agent from a specific crowd. Override this if using custom spawn logic.
        /// </summary>
        protected virtual CrowdAgent SpawnAgentFromCrowd(EnemyCrowd crowd, int factionIndex, Vector3 position)
        {
            // This should integrate with EnemyMassesCrowdController's spawning system
            var controller = EnemyMassesCrowdController.Instance;
            if (controller == null)
            {
                Debug.LogError("[CivilNavigation] SpawnAgentFromCrowd: EnemyMassesCrowdController.Instance is null!");
                return null;
            }
            
            // SpawnEnemyCrowd returns a list of indices, spawn 1 agent
            var spawnedIndices = controller.SpawnEnemyCrowd(factionIndex, position, Quaternion.identity, 1);
            if (spawnedIndices == null)
            {
                Debug.LogWarning($"[CivilNavigation] SpawnAgentFromCrowd: SpawnEnemyCrowd returned null for faction {factionIndex}");
                return null;
            }
            
            if (spawnedIndices.Count == 0)
            {
                Debug.LogWarning($"[CivilNavigation] SpawnAgentFromCrowd: SpawnEnemyCrowd returned empty list for faction {factionIndex}");
                return null;
            }
            
            // Get the CrowdAgent for the spawned index
            var agent = controller.GetAgent(spawnedIndices[0]);
            if (agent == null)
            {
                Debug.LogWarning($"[CivilNavigation] SpawnAgentFromCrowd: GetAgent({spawnedIndices[0]}) returned null");
            }
            return agent;
        }
        
        #endregion
        
        #region State Handlers
        
        private CivilAgentState InitializeCivilState(CrowdAgent agent, EnemyCrowd crowd)
        {
            var state = new CivilAgentState
            {
                navigationState = CivilNavigationState.Idle,
                currentMovementStyle = crowd.civilSettings.movementStyle,
                fadeProgress = 1f,
                activeProps = new List<GameObject>(),
                originalWorkflow = agent.currentWorkflow,
                // Initialize crowd flow state
                congestionSpeedMult = 1f,
                baseSpeed = agent.navAgent != null ? agent.navAgent.speed : 3.5f
            };
            
            // Apply movement style
            ApplyMovementStyle(agent, state, crowd.civilSettings);
            
            return state;
        }
        
        private void HandleIdleState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd)
        {
            // Find nearest waypoint to start navigation
            CivilWaypointNode nearest = _waypointManager.FindNearestWaypoint(
                agent.transform.position,
                null,
                agent.factionIndex
            );
            
            if (nearest == null)
            {
                Debug.LogWarning($"[CivilNavigation] HandleIdleState: No waypoint found for agent {agent.index}");
                return;
            }
            
            // Select next waypoint
            SelectAndNavigateToWaypoint(agent, state, crowd, nearest);
        }
        
        private void HandleWalkingState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float deltaTime)
        {
            if (state.currentTarget == null)
            {
                state.navigationState = CivilNavigationState.Idle;
                return;
            }
            
            var settings = crowd.civilSettings;
            float currentTime = Time.time;
            
            // Handle corner smoothing FIRST if active (before crowd flow can interfere)
            if (state.isCornerSmoothing)
            {
                if (UpdateCornerSmoothing(agent, state, crowd, deltaTime))
                {
                    return; // Still navigating the corner arc - skip all other updates
                }
                // Corner smoothing complete, continue with normal walking
            }
            
            // Ensure agent has a valid destination (for seamless waypoint transitions)
            // This handles cases where ContinueToNextWaypoint deferred setting the destination
            if (agent.navAgent != null && agent.navAgent.isOnNavMesh && !state.isCornerSmoothing)
            {
                // If agent has no path or is stopped unexpectedly, set destination
                if (!agent.navAgent.hasPath || agent.navAgent.isStopped)
                {
                    agent.navAgent.isStopped = false;
                    agent.navAgent.SetDestination(state.targetPosition);
                }
            }
            
            // Update crowd flow (density, congestion, avoidance) - only when NOT corner smoothing
            if (settings.enableCrowdFlow && !state.isCornerSmoothing)
            {
                UpdateCrowdFlow(agent, state, settings, deltaTime, currentTime);
            }
            
            // Update path divergence
            if (settings.pathDivergence > 0f)
            {
                UpdatePathDivergence(agent, state, settings, deltaTime);
            }
            
            // Check for look-around stop (only with high divergence and low probability)
            if (settings.enableLookAroundStops && settings.pathDivergence > 0.3f && !state.isLookingAround)
            {
                // Chance scales with divergence
                float lookChance = settings.lookAroundChance * settings.pathDivergence * deltaTime;
                if (Random.value < lookChance)
                {
                    StartLookAround(agent, state, settings);
                    return;
                }
            }
            
            // Check if approaching a corner that needs smoothing
            // Only attempt if we haven't already failed this frame (prevents repeated failed attempts)
            if (!state.isCornerSmoothing && settings.enableCornerSmoothing && !state.cornerSmoothingFailed)
            {
                if (TryStartCornerSmoothing(agent, state, crowd))
                {
                    return; // Started corner smoothing
                }
            }
            
            // Check if arrived at waypoint
            // Use target position (which includes channel offset) not waypoint center
            float distanceToTarget = Vector3.Distance(agent.transform.position, state.targetPosition);
            float arrivalRadius = state.currentTarget.arrivalRadius;
            
            if (distanceToTarget <= arrivalRadius)
            {
                OnArrivedAtWaypoint(agent, state, crowd);
            }
        }
        
        private void HandleLookAroundState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float currentTime)
        {
            // Check if look around is complete
            if (currentTime - state.lookAroundStartTime >= state.lookAroundDuration)
            {
                EndLookAround(agent, state, crowd);
                return;
            }
            
            // Gradually rotate to look direction
            if (agent.transform != null)
            {
                Quaternion targetRot = Quaternion.LookRotation(state.lookAroundDirection, Vector3.up);
                agent.transform.rotation = Quaternion.Slerp(
                    agent.transform.rotation,
                    targetRot,
                    Time.deltaTime * 2f);
            }
        }
        
        private void StartLookAround(CrowdAgent agent, CivilAgentState state, CivilBehaviorSettings settings)
        {
            state.isLookingAround = true;
            state.lookAroundStartTime = Time.time;
            state.lookAroundDuration = Random.Range(settings.lookAroundDuration.x, settings.lookAroundDuration.y);
            
            // Pick a random direction to look
            float angle = Random.Range(-120f, 120f);
            state.lookAroundDirection = Quaternion.Euler(0, angle, 0) * agent.transform.forward;
            
            // Stop the agent
            StopAgent(agent);
            state.navigationState = CivilNavigationState.LookingAround;
        }
        
        private void EndLookAround(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd)
        {
            state.isLookingAround = false;
            state.navigationState = CivilNavigationState.Walking;
            
            // Resume navigation with divergence
            Vector3 targetWithDivergence = state.targetPosition + state.currentDivergenceOffset;
            SetAgentDestination(agent, targetWithDivergence);
            ApplyMovementStyle(agent, state, crowd.civilSettings);
        }
        
        private void HandleWaitingState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float currentTime)
        {
            // Check if wait time is over
            if (currentTime - state.arrivalTime >= state.waitDuration)
            {
                // Select next waypoint
                SelectAndNavigateToWaypoint(agent, state, crowd, state.currentTarget);
            }
        }
        
        private void HandleExploringState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float currentTime)
        {
            if (state.currentTarget == null || state.currentTarget.waypointType != CivilWaypointType.Explore)
            {
                state.navigationState = CivilNavigationState.Idle;
                return;
            }
            
            // Check if exploration time is over
            Vector2 exploreTime = state.currentTarget.exploreTimeOverride.sqrMagnitude > 0 
                ? state.currentTarget.exploreTimeOverride 
                : crowd.civilSettings.exploreTime;
            
            float explorationDuration = Random.Range(exploreTime.x, exploreTime.y);
            
            if (currentTime - state.exploreStartTime >= explorationDuration)
            {
                // Done exploring, move to next waypoint
                SelectAndNavigateToWaypoint(agent, state, crowd, state.currentTarget);
                return;
            }
            
            // Check if need new explore target
            if (currentTime >= state.nextExploreRetargetTime)
            {
                Vector3 newTarget = state.currentTarget.GetExplorePosition();
                SetAgentDestination(agent, newTarget);
                
                Vector2 retargetInterval = crowd.civilSettings.exploreRetargetInterval;
                state.nextExploreRetargetTime = currentTime + Random.Range(retargetInterval.x, retargetInterval.y);
            }
        }
        
        private void HandleActivityState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float currentTime, float deltaTime)
        {
            if (state.currentActivity == null)
            {
                EndActivity(agent, state, crowd);
                return;
            }
            
            float activityProgress = (currentTime - state.activityStartTime) / state.activityDuration;
            
            // Fire progress event
            state.currentActivity.FireProgress(agent.index, activityProgress);
            
            // Update prop visibility based on timing
            UpdateActivityProps(state, activityProgress);
            
            // Check if activity is complete
            if (activityProgress >= 1f)
            {
                EndActivity(agent, state, crowd);
            }
        }
        
        private void HandleFadeState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float deltaTime)
        {
            float fadeSpeed = 1f / Mathf.Max(0.1f, crowd.civilSettings.fadeTransitionDuration);
            state.fadeProgress += state.fadeDirection * fadeSpeed * deltaTime;
            state.fadeProgress = Mathf.Clamp01(state.fadeProgress);
            
            ApplyFadeToAgent(agent, state.fadeProgress);
            
            // Check if fade is complete
            if ((state.fadeDirection > 0 && state.fadeProgress >= 1f) ||
                (state.fadeDirection < 0 && state.fadeProgress <= 0f))
            {
                state.isFading = false;
                
                if (state.fadeDirection > 0)
                {
                    // Fade in complete - start navigation from spawn waypoint
                    // (unless already walking from frustum spawn)
                    if (state.navigationState == CivilNavigationState.FadingIn)
                    {
                        state.navigationState = CivilNavigationState.Idle;
                        
                        // Use the spawn waypoint as the starting point for navigation
                        // This ensures agents navigate properly even if they spawned far from the waypoint
                        if (state.spawnWaypoint != null)
                        {
                            SelectAndNavigateToWaypoint(agent, state, crowd, state.spawnWaypoint);
                        }
                    }
                    // else: agent was already walking (frustum spawn), just clear the fading flag
                }
                else
                {
                    // Fade out complete - despawn
                    DespawnAgent(agent, state);
                }
            }
        }
        
        /// <summary>
        /// Processes only the fade effect without changing navigation state.
        /// Used for agents that are walking while still fading in (Camera Frustum spawns).
        /// </summary>
        private void ProcessFadeOnly(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float deltaTime)
        {
            float fadeSpeed = 1f / Mathf.Max(0.1f, crowd.civilSettings.fadeTransitionDuration);
            state.fadeProgress += state.fadeDirection * fadeSpeed * deltaTime;
            state.fadeProgress = Mathf.Clamp01(state.fadeProgress);
            
            ApplyFadeToAgent(agent, state.fadeProgress);
            
            // Check if fade-in is complete
            if (state.fadeDirection > 0 && state.fadeProgress >= 1f)
            {
                state.isFading = false;
                // Navigation state remains unchanged - agent continues walking
            }
        }
        
        #endregion
        
        #region Navigation
        
        private void SelectAndNavigateToWaypoint(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, CivilWaypointNode fromWaypoint)
        {
            // Unregister from previous waypoint
            if (state.currentTarget != null)
            {
                state.currentTarget.UnregisterAgent(agent.index);
            }
            
            var settings = crowd.civilSettings;
            
            // Use FreeRoam mode if enabled
            if (settings.navigationMode == CivilNavigationMode.FreeRoam)
            {
                SelectFreeRoamWaypoint(agent, state, crowd, fromWaypoint);
                return;
            }
            
            // FollowConnections mode (traditional)
            // Check despawn chance
            if (state.lifetime >= settings.minLifetimeBeforeDespawn &&
                Random.value < settings.despawnChance &&
                _waypointManager.DespawnWaypoints.Count > 0)
            {
                // Try to find a despawn waypoint
                CivilWaypointNode despawnWaypoint = _waypointManager.FindNearestWaypoint(
                    agent.transform.position,
                    CivilWaypointType.Despawn,
                    agent.factionIndex
                );
                
                if (despawnWaypoint != null)
                {
                    NavigateToWaypoint(agent, state, crowd, despawnWaypoint, fromWaypoint);
                    return;
                }
            }
            
            // Select next waypoint from connections
            CivilWaypointNode nextWaypoint = fromWaypoint.SelectNextWaypoint(
                settings.selectionMode,
                state.previousWaypoint
            );
            
            if (nextWaypoint == null)
            {
                // No valid connections - stay idle
                state.navigationState = CivilNavigationState.Idle;
                return;
            }
            
            NavigateToWaypoint(agent, state, crowd, nextWaypoint, fromWaypoint);
        }
        
        /// <summary>
        /// Selects next waypoint in FreeRoam mode - ignores connections, uses NavMesh pathfinding.
        /// </summary>
        private void SelectFreeRoamWaypoint(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, CivilWaypointNode fromWaypoint)
        {
            var settings = crowd.civilSettings;
            Vector3 agentPos = agent.transform.position;
            
            // Initialize recent waypoints list if needed
            if (state.recentWaypoints == null)
            {
                state.recentWaypoints = new List<CivilWaypointNode>();
            }
            
            // Add current waypoint to recent list (if valid)
            if (fromWaypoint != null && !state.recentWaypoints.Contains(fromWaypoint))
            {
                state.recentWaypoints.Add(fromWaypoint);
                // Trim to memory size
                while (state.recentWaypoints.Count > settings.freeRoamRecentMemory)
                {
                    state.recentWaypoints.RemoveAt(0);
                }
            }
            
            // Check for despawn after minimum lifetime
            bool shouldDespawn = state.lifetime >= settings.minLifetimeBeforeDespawn &&
                                 Random.value < settings.freeRoamDespawnBias &&
                                 _waypointManager.DespawnWaypoints.Count > 0;
            
            CivilWaypointNode selectedWaypoint = null;
            
            if (shouldDespawn)
            {
                // Find nearest despawn waypoint
                selectedWaypoint = _waypointManager.FindNearestWaypoint(
                    agentPos, CivilWaypointType.Despawn, agent.factionIndex);
            }
            
            if (selectedWaypoint == null)
            {
                // Collect all valid waypoints (excluding recent ones)
                List<CivilWaypointNode> candidates = new List<CivilWaypointNode>();
                
                // Add Path waypoints
                foreach (var wp in _waypointManager.PathWaypoints)
                {
                    if (wp != null && wp != fromWaypoint && !state.recentWaypoints.Contains(wp))
                    {
                        if (settings.freeRoamMaxDistance <= 0 || 
                            Vector3.Distance(agentPos, wp.transform.position) <= settings.freeRoamMaxDistance)
                        {
                            candidates.Add(wp);
                        }
                    }
                }
                
                // Add Explore waypoints
                foreach (var wp in _waypointManager.ExploreWaypoints)
                {
                    if (wp != null && wp != fromWaypoint && !state.recentWaypoints.Contains(wp))
                    {
                        if (settings.freeRoamMaxDistance <= 0 || 
                            Vector3.Distance(agentPos, wp.transform.position) <= settings.freeRoamMaxDistance)
                        {
                            candidates.Add(wp);
                        }
                    }
                }
                
                // Add Activity waypoints
                foreach (var wp in _waypointManager.ActivityWaypoints)
                {
                    if (wp != null && wp != fromWaypoint && !state.recentWaypoints.Contains(wp))
                    {
                        if (settings.freeRoamMaxDistance <= 0 || 
                            Vector3.Distance(agentPos, wp.transform.position) <= settings.freeRoamMaxDistance)
                        {
                            candidates.Add(wp);
                        }
                    }
                }
                
                // Always include Despawn waypoints as valid destinations
                foreach (var wp in _waypointManager.DespawnWaypoints)
                {
                    if (wp != null && wp != fromWaypoint)
                    {
                        if (settings.freeRoamMaxDistance <= 0 || 
                            Vector3.Distance(agentPos, wp.transform.position) <= settings.freeRoamMaxDistance)
                        {
                            candidates.Add(wp);
                        }
                    }
                }
                
                if (candidates.Count == 0)
                {
                    // No candidates - try to include recent waypoints if we have no choice
                    foreach (var wp in _waypointManager.PathWaypoints)
                    {
                        if (wp != null && wp != fromWaypoint)
                            candidates.Add(wp);
                    }
                }
                
                if (candidates.Count > 0)
                {
                    if (settings.freeRoamPreferCloser)
                    {
                        // Sort by distance and pick from closest few
                        candidates.Sort((a, b) => 
                            Vector3.Distance(agentPos, a.transform.position)
                            .CompareTo(Vector3.Distance(agentPos, b.transform.position)));
                        
                        // Pick randomly from the closest third
                        int range = Mathf.Max(1, candidates.Count / 3);
                        selectedWaypoint = candidates[Random.Range(0, range)];
                    }
                    else
                    {
                        // Pure random selection
                        selectedWaypoint = candidates[Random.Range(0, candidates.Count)];
                    }
                }
            }
            
            if (selectedWaypoint == null)
            {
                // Truly no waypoints available - stay idle
                state.navigationState = CivilNavigationState.Idle;
                return;
            }
            
            // Navigate to selected waypoint using NavMesh pathfinding
            NavigateToWaypointFreeRoam(agent, state, crowd, selectedWaypoint, fromWaypoint);
        }
        
        /// <summary>
        /// Navigates to waypoint in FreeRoam mode - direct NavMesh pathfinding, no channel width.
        /// </summary>
        private void NavigateToWaypointFreeRoam(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd,
                                                 CivilWaypointNode targetWaypoint, CivilWaypointNode fromWaypoint)
        {
            state.previousWaypoint = state.currentTarget;
            state.currentTarget = targetWaypoint;
            // In FreeRoam mode, navigate directly to waypoint position (no channel offset)
            state.targetPosition = targetWaypoint.NavMeshPosition;
            state.navigationState = CivilNavigationState.Walking;
            
            // Register at new waypoint
            targetWaypoint.RegisterAgent(agent.index);
            
            // Apply movement style speed
            ApplyMovementStyle(agent, state, crowd.civilSettings);
            
            // Set NavMesh destination - let NavMesh handle the pathfinding
            SetAgentDestination(agent, state.targetPosition);
        }
        
        /// <summary>
        /// Seamlessly continues to next waypoint without stopping.
        /// Used for Path waypoints to avoid the brief pause caused by SetDestination.
        /// </summary>
        private void ContinueToNextWaypoint(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, CivilWaypointNode fromWaypoint)
        {
            // Unregister from current waypoint
            if (state.currentTarget != null)
            {
                state.currentTarget.UnregisterAgent(agent.index);
            }
            
            var settings = crowd.civilSettings;
            CivilWaypointNode nextWaypoint = null;
            
            // FreeRoam mode: use free waypoint selection
            if (settings.navigationMode == CivilNavigationMode.FreeRoam)
            {
                SelectFreeRoamWaypoint(agent, state, crowd, fromWaypoint);
                return;
            }
            
            // FollowConnections mode: select next waypoint from connections
            nextWaypoint = fromWaypoint.SelectNextWaypoint(
                settings.selectionMode,
                state.previousWaypoint
            );
            
            if (nextWaypoint == null)
            {
                // No valid connections - stay idle
                state.navigationState = CivilNavigationState.Idle;
                return;
            }
            
            // Update state WITHOUT calling SetDestination immediately
            state.previousWaypoint = state.currentTarget;
            state.currentTarget = nextWaypoint;
            state.targetPosition = nextWaypoint.GetChannelPosition(fromWaypoint);
            state.navigationState = CivilNavigationState.Walking;
            
            // Register at new waypoint
            nextWaypoint.RegisterAgent(agent.index);
            
            // DON'T call ApplyMovementStyle - keep current speed to avoid stuttering
            // DON'T call SetDestination yet - let the agent continue moving
            
            // Only set destination if agent is significantly off-course or nearly stopped
            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                Vector3 agentVelocity = agent.navAgent.velocity;
                Vector3 toTarget = (state.targetPosition - agent.transform.position).normalized;
                toTarget.y = 0;
                agentVelocity.y = 0;
                
                // Check if agent is moving roughly toward the new target
                float alignment = Vector3.Dot(agentVelocity.normalized, toTarget);
                
                // If agent is nearly stopped OR heading wrong direction, set new destination
                if (agentVelocity.sqrMagnitude < 0.1f || alignment < 0.3f)
                {
                    agent.navAgent.SetDestination(state.targetPosition);
                }
                // Otherwise, let agent continue - NavMesh will naturally path toward current destination
                // and we'll update destination on next frame's HandleWalkingState if needed
            }
        }
        
        private void NavigateToWaypoint(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, 
                                        CivilWaypointNode targetWaypoint, CivilWaypointNode fromWaypoint)
        {
            state.previousWaypoint = state.currentTarget;
            state.currentTarget = targetWaypoint;
            state.targetPosition = targetWaypoint.GetChannelPosition(fromWaypoint);
            state.navigationState = CivilNavigationState.Walking;
            
            // Register at new waypoint
            targetWaypoint.RegisterAgent(agent.index);
            
            // Apply movement style speed
            ApplyMovementStyle(agent, state, crowd.civilSettings);
            
            // Set NavMesh destination
            SetAgentDestination(agent, state.targetPosition);
        }
        
        private void OnArrivedAtWaypoint(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd)
        {
            CivilWaypointNode waypoint = state.currentTarget;
            float currentTime = Time.time;
            
            // Reset corner smoothing failed flag for the next waypoint
            state.cornerSmoothingFailed = false;
            
            switch (waypoint.waypointType)
            {
                case CivilWaypointType.Path:
                    // For Path waypoints, continue immediately to next waypoint by default
                    // Only wait if pathWaitTime.y > 0.1 AND both min/max are set
                    float minWait = crowd.civilSettings.pathWaitTime.x;
                    float maxWait = crowd.civilSettings.pathWaitTime.y;
                    if (maxWait > 0.1f && minWait >= 0f)
                    {
                        // User explicitly wants some waiting at path waypoints
                        state.navigationState = CivilNavigationState.Waiting;
                        state.arrivalTime = currentTime;
                        state.waitDuration = Random.Range(minWait, maxWait);
                        StopAgent(agent);
                    }
                    else
                    {
                        // Continue immediately without stopping - use seamless transition
                        ContinueToNextWaypoint(agent, state, crowd, waypoint);
                    }
                    break;
                    
                case CivilWaypointType.Explore:
                    // Start exploring
                    state.navigationState = CivilNavigationState.Exploring;
                    state.exploreStartTime = currentTime;
                    state.nextExploreRetargetTime = currentTime; // Immediately pick first target
                    break;
                    
                case CivilWaypointType.Despawn:
                    // Start fade out
                    state.navigationState = CivilNavigationState.FadingOut;
                    state.fadeDirection = -1;
                    state.isFading = true;
                    StopAgent(agent);
                    break;
                    
                case CivilWaypointType.Activity:
                    // Check if should perform activity
                    bool canActivity = waypoint.CanStartActivity &&
                                      Random.value < crowd.civilSettings.activityChance &&
                                      (currentTime - state.lastActivityEndTime) >= crowd.civilSettings.activityCooldown;
                    
                    if (canActivity && waypoint.availableActivities.Count > 0)
                    {
                        StartActivity(agent, state, crowd, waypoint);
                    }
                    else
                    {
                        // Just wait and continue
                        state.navigationState = CivilNavigationState.Waiting;
                        state.arrivalTime = currentTime;
                        state.waitDuration = Random.Range(crowd.civilSettings.pathWaitTime.x, crowd.civilSettings.pathWaitTime.y);
                        StopAgent(agent);
                    }
                    break;

                case CivilWaypointType.Lean:
                    if (!TryStartLean(agent, state, crowd, waypoint))
                    {
                        waypoint.UnregisterAgent(agent.index);
                        SelectAndNavigateToWaypoint(agent, state, crowd, waypoint);
                    }
                    break;

                case CivilWaypointType.Sit:
                    if (!TryStartSit(agent, state, crowd, waypoint))
                    {
                        waypoint.UnregisterAgent(agent.index);
                        SelectAndNavigateToWaypoint(agent, state, crowd, waypoint);
                    }
                    break;
                    
                default:
                    // Continue to next waypoint
                    SelectAndNavigateToWaypoint(agent, state, crowd, waypoint);
                    break;
            }
        }
        
        #region Corner Smoothing
        
        /// <summary>
        /// Checks if the agent is approaching a corner that needs smoothing.
        /// </summary>
        private bool TryStartCornerSmoothing(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd)
        {
            var settings = crowd.civilSettings;
            
            if (!settings.enableCornerSmoothing)
                return false;
            
            // Need current target and it must have connections to look ahead
            if (state.currentTarget == null)
                return false;
            
            // Get the next waypoint after current target
            CivilWaypointNode nextWaypoint = state.currentTarget.SelectNextWaypoint(
                settings.selectionMode, 
                state.previousWaypoint);
            
            if (nextWaypoint == null)
                return false;
            
            // Calculate the angle at the corner
            Vector3 currentPos = agent.transform.position;
            Vector3 cornerPos = state.currentTarget.GetChannelPosition(state.previousWaypoint);
            Vector3 nextPos = nextWaypoint.GetChannelPosition(state.currentTarget);
            
            Vector3 toCorner = (cornerPos - currentPos).normalized;
            Vector3 fromCorner = (nextPos - cornerPos).normalized;
            
            // Handle cases where positions might be too close
            if (toCorner.sqrMagnitude < 0.001f || fromCorner.sqrMagnitude < 0.001f)
                return false;
            
            // Flatten to 2D for angle calculation
            toCorner.y = 0;
            fromCorner.y = 0;
            toCorner.Normalize();
            fromCorner.Normalize();
            
            // Calculate turning angle (0 = straight, 180 = U-turn)
            float dotProduct = Vector3.Dot(toCorner, fromCorner);
            float angle = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;
            
            // Only smooth if angle exceeds threshold (higher angle = sharper turn)
            if (angle < settings.cornerSmoothingAngleThreshold)
                return false;
            
            // Check distance to corner - start smoothing when within approach radius
            float distanceToCorner = Vector3.Distance(currentPos, cornerPos);
            // Use a larger approach radius to start smoothing earlier (2x the radius + approach factor)
            float approachRadius = settings.cornerSmoothingRadius * (2f + settings.cornerApproachFactor);
            
            if (distanceToCorner > approachRadius)
                return false;
            
            // If we're very close to the corner (within arrival radius), skip corner smoothing
            // Let the agent arrive normally to prevent getting stuck
            if (distanceToCorner < state.currentTarget.arrivalRadius * 1.2f)
            {
                state.cornerSmoothingFailed = true; // Don't try again for this waypoint
                return false;
            }
            
            // Calculate the arc points for smooth corner
            state.cornerArcPoints = CalculateCornerArc(
                currentPos, cornerPos, nextPos, 
                settings.cornerSmoothingRadius, 
                settings.cornerSmoothingSegments);
            
            if (state.cornerArcPoints == null || state.cornerArcPoints.Length == 0)
            {
                state.cornerSmoothingFailed = true;
                return false;
            }
            
            // Start corner smoothing
            state.isCornerSmoothing = true;
            state.currentArcIndex = 0;
            state.arcProgress = 0f;
            state.cornerWaypoint = state.currentTarget;
            state.cornerStartPosition = currentPos;
            state.cornerEndPosition = state.cornerArcPoints[state.cornerArcPoints.Length - 1];
            state.postCornerWaypoint = nextWaypoint;
            
            // Capture the CURRENT actual speed (including congestion) for consistent arc movement
            // This prevents speed jumps when entering/exiting corner smoothing
            float currentSpeed = agent.navAgent != null ? agent.navAgent.speed : state.baseSpeed;
            if (currentSpeed <= 0.1f) currentSpeed = state.baseSpeed > 0 ? state.baseSpeed : 3.5f;
            
            // Store speed in arcVelocity magnitude for use during UpdateCornerSmoothing
            Vector3 toFirstPoint = (state.cornerArcPoints[0] - currentPos).normalized;
            state.arcVelocity = toFirstPoint * currentSpeed;
            
            // Disable NavMesh auto-updates so we can control position smoothly
            if (agent.navAgent != null)
            {
                agent.navAgent.updatePosition = false;
                agent.navAgent.updateRotation = false;
                agent.navAgent.isStopped = true;
            }
            
            return true;
        }
        
        /// <summary>
        /// Calculates arc points for smoothing a corner between three positions.
        /// Uses a quadratic Bezier curve with outward-pushed control point for wider arcs.
        /// </summary>
        private Vector3[] CalculateCornerArc(Vector3 startPos, Vector3 cornerPos, Vector3 endPos, 
                                              float radius, int segments)
        {
            // Calculate direction vectors
            Vector3 toCorner = cornerPos - startPos;
            Vector3 fromCorner = endPos - cornerPos;
            
            float distToCorner = toCorner.magnitude;
            float distFromCorner = fromCorner.magnitude;
            
            // Don't smooth if we're too close to the corner already
            if (distToCorner < 0.3f)
                return null;
            
            // Use a larger effective radius for wider arcs (minimum 2m for visible curves)
            // But don't let it be larger than the distance to corner
            float effectiveRadius = Mathf.Max(radius, 2f);
            effectiveRadius = Mathf.Min(effectiveRadius, distToCorner * 0.8f, distFromCorner * 0.5f);
            
            if (effectiveRadius < 0.5f)
                return null;
            
            // Normalize direction vectors
            Vector3 toCornerDir = toCorner.normalized;
            Vector3 fromCornerDir = fromCorner.normalized;
            
            // Arc START is the agent's current position (not calculated from corner)
            // This prevents the agent from needing to backtrack
            Vector3 arcStart = startPos;
            
            // Calculate arc end point - extend along the exit path
            float exitDistance = effectiveRadius * 2f;
            exitDistance = Mathf.Min(exitDistance, distFromCorner * 0.8f); // Don't overshoot the destination
            Vector3 arcEnd = cornerPos + fromCornerDir * exitDistance;
            
            // Calculate the outward direction (perpendicular to the corner bisector)
            // This pushes the control point AWAY from the corner for a wider arc
            Vector3 cornerBisector = ((-toCornerDir) + fromCornerDir).normalized;
            
            // If vectors are nearly opposite (U-turn), use perpendicular
            if (cornerBisector.sqrMagnitude < 0.01f)
            {
                cornerBisector = Vector3.Cross(Vector3.up, toCornerDir).normalized;
            }
            
            // Push the control point outward from the corner - more for tighter angles
            float dotProduct = Vector3.Dot(toCornerDir, fromCornerDir);
            float angleSharpness = 1f - (dotProduct + 1f) * 0.5f; // 0 = straight, 1 = U-turn
            float outwardPush = effectiveRadius * (0.6f + angleSharpness * 0.6f); // More push for sharper turns
            Vector3 controlPoint = cornerPos + cornerBisector * outwardPush;
            
            // Validate arc start/end positions on NavMesh
            if (!IsPositionOnNavMesh(arcStart, out arcStart) || !IsPositionOnNavMesh(arcEnd, out arcEnd))
                return null;
            
            // Validate control point on NavMesh (or fall back to corner)
            if (!IsPositionOnNavMesh(controlPoint, out controlPoint))
            {
                // If outward control point is invalid, use a point between corner and outward
                controlPoint = Vector3.Lerp(cornerPos, controlPoint, 0.5f);
                if (!IsPositionOnNavMesh(controlPoint, out controlPoint))
                {
                    controlPoint = cornerPos; // Fall back to original corner
                }
            }
            
            // Generate Bezier curve points - more segments for smoother curve
            int actualSegments = Mathf.Max(segments, 8); // Minimum 8 segments
            Vector3[] arcPoints = new Vector3[actualSegments + 1];
            
            // Generate the Bezier curve
            for (int i = 0; i <= actualSegments; i++)
            {
                float t = (float)i / actualSegments;
                
                // Quadratic Bezier: B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2
                float oneMinusT = 1f - t;
                Vector3 point = oneMinusT * oneMinusT * arcStart + 
                               2f * oneMinusT * t * controlPoint + 
                               t * t * arcEnd;
                
                // Validate each point on NavMesh
                if (IsPositionOnNavMesh(point, out Vector3 validPoint))
                {
                    arcPoints[i] = validPoint;
                }
                else
                {
                    // If any point is invalid, fall back to simple interpolation
                    arcPoints[i] = Vector3.Lerp(arcStart, arcEnd, t);
                    IsPositionOnNavMesh(arcPoints[i], out arcPoints[i]);
                }
            }
            
            return arcPoints;
        }
        
        /// <summary>
        /// Validates a position is on the NavMesh and returns the closest valid position.
        /// </summary>
        private bool IsPositionOnNavMesh(Vector3 position, out Vector3 validPosition)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                validPosition = hit.position;
                return true;
            }
            validPosition = position;
            return false;
        }
        
        /// <summary>
        /// Updates corner smoothing navigation using smooth position interpolation.
        /// Uses constant speed movement for natural walking around corners.
        /// </summary>
        private bool UpdateCornerSmoothing(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float deltaTime)
        {
            if (!state.isCornerSmoothing || state.cornerArcPoints == null || state.cornerArcPoints.Length == 0)
                return false;
            
            // Use the speed captured when corner smoothing started (stored in arcVelocity magnitude)
            // This ensures consistent speed throughout the arc - no speed jumps
            float speed = state.arcVelocity.magnitude;
            if (speed <= 0.1f)
            {
                // Fallback if arcVelocity wasn't set properly
                speed = agent.navAgent != null ? agent.navAgent.speed : state.baseSpeed;
                if (speed <= 0.1f) speed = 3.5f;
            }
            
            // Calculate how far we can move this frame
            float moveDistance = speed * deltaTime;
            Vector3 currentPos = agent.transform.position;
            
            // Move along the arc path, potentially passing through multiple points per frame
            while (moveDistance > 0.01f && state.currentArcIndex < state.cornerArcPoints.Length)
            {
                Vector3 targetPoint = state.cornerArcPoints[state.currentArcIndex];
                Vector3 toTarget = targetPoint - currentPos;
                toTarget.y = 0; // Keep horizontal
                float distToTarget = toTarget.magnitude;
                
                if (distToTarget <= moveDistance)
                {
                    // We can reach this point, move to it and continue to next
                    currentPos = targetPoint;
                    moveDistance -= distToTarget;
                    state.currentArcIndex++;
                }
                else
                {
                    // Move toward the target point
                    currentPos += toTarget.normalized * moveDistance;
                    moveDistance = 0;
                }
            }
            
            // Check if we've completed the arc
            if (state.currentArcIndex >= state.cornerArcPoints.Length)
            {
                CompleteCornerSmoothing(agent, state, crowd);
                return false;
            }
            
            // Validate final position on NavMesh
            if (NavMesh.SamplePosition(currentPos, out NavMeshHit hit, 1f, NavMesh.AllAreas))
            {
                currentPos = hit.position;
            }
            
            // Apply position
            agent.transform.position = currentPos;
            
            // Sync NavAgent position
            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                agent.navAgent.nextPosition = currentPos;
            }
            
            // Smooth rotation toward movement direction (look at next arc point)
            if (state.currentArcIndex < state.cornerArcPoints.Length)
            {
                Vector3 lookTarget = state.cornerArcPoints[state.currentArcIndex];
                Vector3 lookDir = lookTarget - currentPos;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(lookDir.normalized);
                    agent.transform.rotation = Quaternion.Slerp(
                        agent.transform.rotation,
                        targetRotation,
                        deltaTime * 8f);
                }
            }
            
            // Track arc progress for animation blend purposes
            state.arcProgress = (float)state.currentArcIndex / state.cornerArcPoints.Length;
            
            return true;
        }
        
        /// <summary>
        /// Calculates the approximate total length of the arc path.
        /// </summary>
        private float CalculateArcLength(Vector3[] arcPoints)
        {
            if (arcPoints == null || arcPoints.Length < 2)
                return 0f;
            
            float length = 0f;
            for (int i = 1; i < arcPoints.Length; i++)
            {
                length += Vector3.Distance(arcPoints[i - 1], arcPoints[i]);
            }
            return length;
        }
        
        /// <summary>
        /// Samples a position along the arc using normalized progress (0-1).
        /// </summary>
        private Vector3 SampleArcPosition(Vector3[] arcPoints, float progress)
        {
            if (arcPoints == null || arcPoints.Length == 0)
                return Vector3.zero;
            
            if (arcPoints.Length == 1)
                return arcPoints[0];
            
            // Map progress to segment
            float scaledProgress = progress * (arcPoints.Length - 1);
            int segmentIndex = Mathf.FloorToInt(scaledProgress);
            float segmentProgress = scaledProgress - segmentIndex;
            
            // Clamp to valid range
            segmentIndex = Mathf.Clamp(segmentIndex, 0, arcPoints.Length - 2);
            
            // Lerp between segment points
            return Vector3.Lerp(arcPoints[segmentIndex], arcPoints[segmentIndex + 1], segmentProgress);
        }
        
        /// <summary>
        /// Completes corner smoothing and sets up navigation to the next waypoint.
        /// </summary>
        private void CompleteCornerSmoothing(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd)
        {
            // Capture the speed we were using during corner smoothing
            float arcSpeed = state.arcVelocity.magnitude;
            
            // Unregister from corner waypoint
            if (state.cornerWaypoint != null)
            {
                state.cornerWaypoint.UnregisterAgent(agent.index);
            }
            
            // Clear corner smoothing state
            state.isCornerSmoothing = false;
            state.cornerArcPoints = null;
            state.currentArcIndex = 0;
            state.arcProgress = 0f;
            state.arcVelocity = Vector3.zero;
            
            // Set up navigation to the post-corner waypoint BEFORE re-enabling NavMesh
            // This ensures NavMesh continues in the same direction without recalculating
            if (state.postCornerWaypoint != null)
            {
                // Update state to target post-corner waypoint
                state.previousWaypoint = state.cornerWaypoint;
                state.currentTarget = state.postCornerWaypoint;
                state.targetPosition = state.postCornerWaypoint.GetChannelPosition(state.cornerWaypoint);
                
                // Register at new waypoint
                state.postCornerWaypoint.RegisterAgent(agent.index);
            }
            
            // Re-enable NavMesh agent control
            if (agent.navAgent != null)
            {
                // Set speed to match what we were using during corner smoothing
                // This prevents sudden speed changes when NavMesh takes over
                if (arcSpeed > 0.1f)
                {
                    agent.navAgent.speed = arcSpeed;
                }
                
                // Sync the NavAgent's position to current transform (no warp, smooth handoff)
                agent.navAgent.nextPosition = agent.transform.position;
                
                // Re-enable NavMesh control
                agent.navAgent.updatePosition = true;
                agent.navAgent.updateRotation = true;
                agent.navAgent.isStopped = false;
                
                // Now set the destination - NavMesh will continue from current position
                if (state.postCornerWaypoint != null)
                {
                    SetAgentDestination(agent, state.targetPosition);
                }
            }
            
            // Clear corner waypoint references
            state.cornerWaypoint = null;
            state.postCornerWaypoint = null;
        }
        
        /// <summary>
        /// Cancels corner smoothing and reverts to normal navigation.
        /// </summary>
        private void CancelCornerSmoothing(CrowdAgent agent, CivilAgentState state)
        {
            // Re-enable NavMesh agent control
            if (agent != null && agent.navAgent != null)
            {
                agent.navAgent.updatePosition = true;
                agent.navAgent.updateRotation = true;
                agent.navAgent.isStopped = false;
            }
            
            state.isCornerSmoothing = false;
            state.cornerArcPoints = null;
            state.currentArcIndex = 0;
            state.arcProgress = 0f;
            state.arcVelocity = Vector3.zero;
            state.cornerWaypoint = null;
            state.postCornerWaypoint = null;
        }
        
        #endregion
        
        #region Crowd Flow & Congestion
        
        // Spatial hash for efficient neighbor lookups
        private Dictionary<int, List<int>> _spatialHash = new Dictionary<int, List<int>>();
        private float _spatialCellSize = 4f;
        private float _lastSpatialHashUpdate;
        private const float SPATIAL_HASH_UPDATE_INTERVAL = 0.2f;
        
        /// <summary>
        /// Updates crowd flow calculations including density, congestion, and avoidance.
        /// </summary>
        private void UpdateCrowdFlow(CrowdAgent agent, CivilAgentState state, CivilBehaviorSettings settings, float deltaTime, float currentTime)
        {
            // Initialize base speed if needed
            if (state.baseSpeed <= 0 && agent.navAgent != null)
            {
                state.baseSpeed = agent.navAgent.speed;
            }
            
            // Rate-limit density checks
            if (currentTime - state.lastDensityCheckTime < 0.1f)
            {
                // Still apply existing speed modifications
                ApplyCongestionSpeed(agent, state, settings);
                return;
            }
            state.lastDensityCheckTime = currentTime;
            
            // Update spatial hash periodically (shared across all agents)
            if (currentTime - _lastSpatialHashUpdate > SPATIAL_HASH_UPDATE_INTERVAL)
            {
                UpdateSpatialHash();
                _lastSpatialHashUpdate = currentTime;
            }
            
            // Calculate local density
            Vector3 agentPos = agent.transform.position;
            List<CrowdAgent> nearbyAgents = GetNearbyAgents(agentPos, settings.densityCheckRadius, agent.index);
            state.localDensity = nearbyAgents.Count;
            
            // Calculate congestion speed multiplier
            float congestionFactor = Mathf.Clamp01((float)state.localDensity / settings.congestionThreshold);
            state.congestionSpeedMult = Mathf.Lerp(1f, settings.minCongestionSpeedMult, congestionFactor);
            
            // Calculate crowd avoidance offset
            if (settings.crowdAvoidanceStrength > 0 && nearbyAgents.Count > 0)
            {
                Vector3 avoidance = CalculateCrowdAvoidance(agent, state, nearbyAgents, settings);
                state.crowdAvoidanceOffset = Vector3.Lerp(state.crowdAvoidanceOffset, avoidance, deltaTime * 3f);
            }
            else
            {
                state.crowdAvoidanceOffset = Vector3.Lerp(state.crowdAvoidanceOffset, Vector3.zero, deltaTime * 2f);
            }
            
            // Apply lane formation (passing on preferred side)
            if (settings.enableLaneFormation && nearbyAgents.Count > 0)
            {
                ApplyLaneFormation(agent, state, nearbyAgents, settings, deltaTime);
            }
            
            // Apply speed modifications
            ApplyCongestionSpeed(agent, state, settings);
            
            // Apply avoidance offset if significant
            if (state.crowdAvoidanceOffset.sqrMagnitude > 0.01f && !state.isCornerSmoothing)
            {
                ApplyCrowdAvoidanceOffset(agent, state, settings);
            }
        }
        
        /// <summary>
        /// Updates the spatial hash grid for efficient neighbor lookups.
        /// </summary>
        private void UpdateSpatialHash()
        {
            _spatialHash.Clear();
            
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.transform == null)
                    continue;
                
                // Check if this agent is in civil mode
                if (!_civilStates.ContainsKey(i))
                    continue;
                
                Vector3 pos = agent.transform.position;
                int hash = GetSpatialHash(pos);
                
                if (!_spatialHash.TryGetValue(hash, out List<int> cell))
                {
                    cell = new List<int>();
                    _spatialHash[hash] = cell;
                }
                cell.Add(i);
            }
        }
        
        /// <summary>
        /// Gets the spatial hash key for a position.
        /// </summary>
        private int GetSpatialHash(Vector3 pos)
        {
            int x = Mathf.FloorToInt(pos.x / _spatialCellSize);
            int z = Mathf.FloorToInt(pos.z / _spatialCellSize);
            return x * 73856093 ^ z * 19349663; // Prime number hashing
        }
        
        /// <summary>
        /// Gets all nearby agents within a radius using spatial hashing.
        /// </summary>
        private List<CrowdAgent> GetNearbyAgents(Vector3 position, float radius, int excludeIndex)
        {
            List<CrowdAgent> result = new List<CrowdAgent>();
            float radiusSq = radius * radius;
            
            // Check neighboring cells
            int cellRadius = Mathf.CeilToInt(radius / _spatialCellSize);
            int centerX = Mathf.FloorToInt(position.x / _spatialCellSize);
            int centerZ = Mathf.FloorToInt(position.z / _spatialCellSize);
            
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    int hash = (centerX + dx) * 73856093 ^ (centerZ + dz) * 19349663;
                    
                    if (_spatialHash.TryGetValue(hash, out List<int> cell))
                    {
                        foreach (int agentIndex in cell)
                        {
                            if (agentIndex == excludeIndex)
                                continue;
                            
                            CrowdAgent other = _agents[agentIndex];
                            if (other == null || other.isDead || other.transform == null)
                                continue;
                            
                            float distSq = (other.transform.position - position).sqrMagnitude;
                            if (distSq <= radiusSq)
                            {
                                result.Add(other);
                            }
                        }
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Counts agents near a specific position (for corner congestion detection).
        /// </summary>
        private int CountAgentsNearPosition(Vector3 position, float radius, int excludeIndex)
        {
            int count = 0;
            float radiusSq = radius * radius;
            
            // Check neighboring cells
            int cellRadius = Mathf.CeilToInt(radius / _spatialCellSize);
            int centerX = Mathf.FloorToInt(position.x / _spatialCellSize);
            int centerZ = Mathf.FloorToInt(position.z / _spatialCellSize);
            
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    int hash = (centerX + dx) * 73856093 ^ (centerZ + dz) * 19349663;
                    
                    if (_spatialHash.TryGetValue(hash, out List<int> cell))
                    {
                        foreach (int agentIndex in cell)
                        {
                            if (agentIndex == excludeIndex)
                                continue;
                            
                            CrowdAgent other = _agents[agentIndex];
                            if (other == null || other.isDead || other.transform == null)
                                continue;
                            
                            // Also count agents that are corner smoothing at this location
                            if (_civilStates.TryGetValue(agentIndex, out CivilAgentState otherState))
                            {
                                // If other agent is corner smoothing, check their corner position too
                                if (otherState.isCornerSmoothing && otherState.cornerWaypoint != null)
                                {
                                    float distSq = (otherState.cornerStartPosition - position).sqrMagnitude;
                                    if (distSq <= radiusSq)
                                    {
                                        count++;
                                        continue;
                                    }
                                }
                            }
                            
                            float distSq2 = (other.transform.position - position).sqrMagnitude;
                            if (distSq2 <= radiusSq)
                            {
                                count++;
                            }
                        }
                    }
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// Finds an alternative waypoint to avoid a congested corner.
        /// Looks for other connections from the previous waypoint that lead to less congested areas.
        /// </summary>
        private CivilWaypointNode FindAlternativeWaypoint(CrowdAgent agent, CivilAgentState state, 
                                                           EnemyCrowd crowd, CivilWaypointNode congestedWaypoint)
        {
            var settings = crowd.civilSettings;
            
            // Get the previous waypoint to check its other connections
            CivilWaypointNode previousWaypoint = state.previousWaypoint;
            if (previousWaypoint == null)
                return null;
            
            // Get all connections from the previous waypoint
            var waypointConnections = previousWaypoint.connections;
            if (waypointConnections == null || waypointConnections.Count <= 1)
                return null;
            
            CivilWaypointNode bestAlternative = null;
            int lowestCongestion = int.MaxValue;
            
            foreach (var connection in waypointConnections)
            {
                var waypoint = connection.target;
                if (waypoint == null)
                    continue;
                
                // Skip the congested waypoint
                if (waypoint == congestedWaypoint)
                    continue;
                
                // Skip if it's the waypoint we came from
                if (waypoint == state.previousWaypoint)
                    continue;
                
                // Check congestion at this alternative
                Vector3 altCornerPos = waypoint.GetChannelPosition(previousWaypoint);
                int congestion = CountAgentsNearPosition(altCornerPos, settings.cornerSmoothingRadius * 2f, agent.index);
                
                // Also check if this waypoint eventually leads somewhere useful
                // (not a dead end that would trap the agent)
                if (waypoint.connections == null || waypoint.connections.Count == 0)
                    continue;
                
                if (congestion < lowestCongestion)
                {
                    lowestCongestion = congestion;
                    bestAlternative = waypoint;
                }
            }
            
            // Only use alternative if it's significantly less congested
            if (bestAlternative != null && lowestCongestion < settings.congestionThreshold - 1)
            {
                return bestAlternative;
            }
            
            return null;
        }
        
        /// <summary>
        /// Calculates a steering vector to avoid nearby agents.
        /// Uses a combination of separation and velocity alignment (Reynolds flocking).
        /// </summary>
        private Vector3 CalculateCrowdAvoidance(CrowdAgent agent, CivilAgentState state, 
                                                  List<CrowdAgent> nearbyAgents, CivilBehaviorSettings settings)
        {
            Vector3 separation = Vector3.zero;
            Vector3 agentPos = agent.transform.position;
            Vector3 agentVelocity = agent.navAgent != null ? agent.navAgent.velocity : agent.transform.forward;
            
            int count = 0;
            foreach (CrowdAgent other in nearbyAgents)
            {
                Vector3 otherPos = other.transform.position;
                Vector3 diff = agentPos - otherPos;
                float dist = diff.magnitude;
                
                if (dist < 0.01f)
                    continue;
                
                // Weight by inverse distance (closer = stronger avoidance)
                float weight = 1f - (dist / settings.densityCheckRadius);
                weight = weight * weight; // Quadratic falloff
                
                // Check if other agent is moving toward us (more urgent avoidance)
                Vector3 otherVelocity = other.navAgent != null ? other.navAgent.velocity : Vector3.zero;
                float approachFactor = Vector3.Dot(otherVelocity.normalized, -diff.normalized);
                if (approachFactor > 0)
                {
                    weight *= (1f + approachFactor); // Increase urgency for approaching agents
                }
                
                separation += diff.normalized * weight;
                count++;
            }
            
            if (count > 0)
            {
                separation /= count;
                separation *= settings.crowdAvoidanceStrength;
                
                // Project onto ground plane
                separation.y = 0;
            }
            
            return separation;
        }
        
        /// <summary>
        /// Applies lane formation behavior - agents naturally pass on preferred side.
        /// </summary>
        private void ApplyLaneFormation(CrowdAgent agent, CivilAgentState state, 
                                         List<CrowdAgent> nearbyAgents, CivilBehaviorSettings settings, float deltaTime)
        {
            Vector3 agentPos = agent.transform.position;
            Vector3 agentForward = agent.navAgent != null ? agent.navAgent.velocity.normalized : agent.transform.forward;
            
            if (agentForward.sqrMagnitude < 0.01f)
                return;
            
            Vector3 laneOffset = Vector3.zero;
            int opposingCount = 0;
            
            foreach (CrowdAgent other in nearbyAgents)
            {
                Vector3 otherVelocity = other.navAgent != null ? other.navAgent.velocity : Vector3.zero;
                
                // Check if moving in roughly opposite direction
                float dot = Vector3.Dot(agentForward, otherVelocity.normalized);
                if (dot < -0.3f) // Opposing movement
                {
                    // Calculate lateral offset to pass on preferred side
                    Vector3 toOther = other.transform.position - agentPos;
                    Vector3 right = Vector3.Cross(Vector3.up, agentForward).normalized;
                    
                    float lateralDist = Vector3.Dot(toOther, right);
                    
                    // Determine if we should move right or left based on preference
                    float targetSide = settings.lanePreference == LaneSide.Right ? 1f : -1f;
                    
                    // If other agent is on our preferred side, nudge away
                    if (lateralDist * targetSide > 0)
                    {
                        // Other is on our preferred side, move to other side
                        laneOffset -= right * targetSide * 0.5f;
                    }
                    else
                    {
                        // Other is on opposite side, stay on our preferred side
                        laneOffset += right * targetSide * 0.3f;
                    }
                    
                    opposingCount++;
                }
            }
            
            if (opposingCount > 0)
            {
                laneOffset /= opposingCount;
                state.crowdAvoidanceOffset += laneOffset * deltaTime * 2f;
            }
        }
        
        /// <summary>
        /// Applies congestion-based speed modifications to the agent.
        /// </summary>
        private void ApplyCongestionSpeed(CrowdAgent agent, CivilAgentState state, CivilBehaviorSettings settings)
        {
            if (agent.navAgent == null || !agent.navAgent.isOnNavMesh)
                return;
            
            // Calculate target speed
            float targetSpeed = state.baseSpeed * state.congestionSpeedMult;
            
            // Apply path divergence speed variation too
            if (settings.pathDivergence > 0)
            {
                float divergenceSpeedMod = 1f + (Mathf.PerlinNoise(state.divergenceSeed, Time.time * 0.5f) - 0.5f) * 0.2f * settings.pathDivergence;
                targetSpeed *= divergenceSpeedMod;
            }
            
            // Smoothly interpolate speed
            agent.navAgent.speed = Mathf.Lerp(agent.navAgent.speed, targetSpeed, Time.deltaTime * 3f);
        }
        
        /// <summary>
        /// Applies crowd avoidance offset to the agent's destination.
        /// </summary>
        private void ApplyCrowdAvoidanceOffset(CrowdAgent agent, CivilAgentState state, CivilBehaviorSettings settings)
        {
            if (agent.navAgent == null || !agent.navAgent.isOnNavMesh)
                return;
            
            // Don't modify destination during corner smoothing
            if (state.isCornerSmoothing)
                return;
            
            // Calculate offset destination
            Vector3 baseDestination = state.targetPosition + state.currentDivergenceOffset;
            Vector3 avoidedDestination = baseDestination + state.crowdAvoidanceOffset;
            
            // Validate on NavMesh
            if (IsPositionOnNavMesh(avoidedDestination, out Vector3 validDestination))
            {
                // Only update if significantly different from current destination
                if (Vector3.Distance(agent.navAgent.destination, validDestination) > 0.3f)
                {
                    agent.navAgent.SetDestination(validDestination);
                }
            }
        }
        
        #endregion
        
        #endregion
        
        #region Activities
        
        private void StartActivity(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, CivilWaypointNode waypoint)
        {
            CivilActivityDefinition activity = waypoint.SelectActivity();
            if (activity == null)
            {
                state.navigationState = CivilNavigationState.Waiting;
                return;
            }
            
            state.currentActivity = activity;
            state.navigationState = CivilNavigationState.PerformingActivity;
            state.activityStartTime = Time.time;
            state.activityDuration = Random.Range(activity.minDuration, activity.maxDuration);
            
            waypoint.OnActivityStarted();
            
            // Position agent at activity position
            if (waypoint.activityPosition != null)
            {
                agent.transform.position = waypoint.activityPosition.position;
                if (waypoint.activityFacing != null)
                {
                    agent.transform.rotation = Quaternion.LookRotation(waypoint.activityFacing.forward);
                }
            }
            
            StopAgent(agent);
            
            // Switch to BoneTracker workflow for activities with props
            if (activity.propAttachments != null && activity.propAttachments.Length > 0)
            {
                state.originalWorkflow = agent.currentWorkflow;
                SwitchAgentWorkflow(agent, HybridWorkflowType.BoneTracker);
                
                // Spawn props
                SpawnActivityProps(agent, state, activity);
            }
            
            // Play activity animation
            if (activity.animationClip != null)
            {
                PlayActivityAnimation(agent, activity);
            }
            
            // Fire event
            activity.FireStarted(agent.index);
        }
        
        private void EndActivity(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd)
        {
            if (state.currentActivity != null)
            {
                state.currentActivity.FireEnded(agent.index);
                
                // Notify waypoint
                if (state.currentTarget != null)
                {
                    state.currentTarget.OnActivityEnded();
                }
            }
            
            // Clean up props
            CleanupActivityProps(state);
            
            // Restore original workflow
            if (state.originalWorkflow != agent.currentWorkflow)
            {
                SwitchAgentWorkflow(agent, state.originalWorkflow);
            }
            
            state.currentActivity = null;
            state.lastActivityEndTime = Time.time;
            
            // Continue navigation
            if (state.currentTarget != null)
            {
                SelectAndNavigateToWaypoint(agent, state, crowd, state.currentTarget);
            }
            else
            {
                state.navigationState = CivilNavigationState.Idle;
            }
        }
        
        private void SpawnActivityProps(CrowdAgent agent, CivilAgentState state, CivilActivityDefinition activity)
        {
            foreach (var propConfig in activity.propAttachments)
            {
                if (propConfig.propPrefab == null)
                    continue;
                
                // Get or create prop from pool
                GameObject prop = GetPropFromPool(propConfig.propPrefab);
                
                // Find bone
                Transform bone = FindBone(agent, propConfig.boneName, propConfig.bonePath);
                if (bone == null)
                {
                    Debug.LogWarning($"[CivilNavigation] Could not find bone '{propConfig.boneName}' on agent.", agent.transform);
                    ReturnPropToPool(propConfig.propPrefab, prop);
                    continue;
                }
                
                // Attach to bone
                prop.transform.SetParent(bone, false);
                prop.transform.localPosition = propConfig.positionOffset;
                prop.transform.localRotation = Quaternion.Euler(propConfig.rotationOffset);
                prop.transform.localScale = propConfig.scale;
                
                // Initially hidden if has show delay
                prop.SetActive(propConfig.showDelay <= 0f);
                
                state.activeProps.Add(prop);
            }
        }
        
        private void UpdateActivityProps(CivilAgentState state, float normalizedProgress)
        {
            if (state.currentActivity?.propAttachments == null || state.activeProps == null)
                return;
            
            for (int i = 0; i < state.activeProps.Count && i < state.currentActivity.propAttachments.Length; i++)
            {
                var propConfig = state.currentActivity.propAttachments[i];
                var prop = state.activeProps[i];
                
                if (prop == null)
                    continue;
                
                bool shouldShow = normalizedProgress >= propConfig.showDelay && normalizedProgress < propConfig.hideTime;
                
                if (prop.activeSelf != shouldShow)
                {
                    prop.SetActive(shouldShow);
                }
            }
        }
        
        private void CleanupActivityProps(CivilAgentState state)
        {
            if (state.activeProps == null)
                return;
            
            foreach (var prop in state.activeProps)
            {
                if (prop != null)
                {
                    prop.transform.SetParent(null);
                    prop.SetActive(false);
                    
                    // Return to pool - need to find original prefab
                    // For now, just deactivate
                }
            }
            
            state.activeProps.Clear();
        }
        
        private GameObject GetPropFromPool(GameObject prefab)
        {
            if (!_propPools.TryGetValue(prefab, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                _propPools[prefab] = pool;
            }
            
            if (pool.Count > 0)
            {
                return pool.Dequeue();
            }
            
            return Object.Instantiate(prefab);
        }
        
        private void ReturnPropToPool(GameObject prefab, GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(null);
            
            if (!_propPools.TryGetValue(prefab, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                _propPools[prefab] = pool;
            }
            
            pool.Enqueue(instance);
        }
        
        private Transform FindBone(CrowdAgent agent, string boneName, string bonePath)
        {
            if (!string.IsNullOrEmpty(bonePath))
            {
                return agent.transform.Find(bonePath);
            }
            
            if (!string.IsNullOrEmpty(boneName))
            {
                return FindBoneRecursive(agent.transform, boneName);
            }
            
            return null;
        }
        
        private Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (parent.name == boneName)
                return parent;
            
            foreach (Transform child in parent)
            {
                Transform result = FindBoneRecursive(child, boneName);
                if (result != null)
                    return result;
            }
            
            return null;
        }
        
        private void PlayActivityAnimation(CrowdAgent agent, CivilActivityDefinition activity)
        {
            if (activity.animationClip == null)
                return;

            // ComputeAnimator: play clip directly on GPUI
            if (agent.currentWorkflow == HybridWorkflowType.ComputeAnimator && agent.crowdInstance != null)
            {
                // Loop for the activity duration; civil controller ends the activity separately.
                agent.crowdInstance.StartAnimation(activity.animationClip, -1f, 1f, 0.25f, null, true);
                return;
            }

            // BoneTracker / MecanimReader: best-effort via Mecanim.
            // NOTE: activity.animationClip.name must match a state in the Animator Controller.
            if (agent.mecanimAnimator != null)
            {
                int stateHash = Animator.StringToHash(activity.animationClip.name);
                if (agent.mecanimAnimator.HasState(0, stateHash))
                {
                    agent.mecanimAnimator.CrossFade(stateHash, 0.25f, 0, 0f);
                }
            }
        }
        
        #endregion

        #region Lean

        /// <summary>
        /// Attempts to start a lean interaction at the waypoint.
        /// </summary>
        private bool TryStartLean(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, CivilWaypointNode waypoint)
        {
            if (waypoint == null)
                return false;

            if (!waypoint.TryReserveLeanSpot(agent.index, out CivilLeanSpot spot, out int spotIndex))
                return false;

            var socialSettings = crowd?.civilSettings?.socialSettings;
            SocialInteractionDefinition interaction = null;

            if (socialSettings?.interactions != null && socialSettings.interactions.Count > 0)
            {
                if (!string.IsNullOrEmpty(spot.interactionId))
                {
                    interaction = socialSettings.interactions.Find(i => i.interactionId == spot.interactionId);
                }

                interaction ??= socialSettings.interactions[0];
            }

            if (interaction == null)
            {
                waypoint.ReleaseLeanSpot(agent.index);
                return false;
            }

            Vector3 targetPos = spot.marker != null ? spot.marker.position : waypoint.transform.position;
            Quaternion targetRot = spot.marker != null ? spot.marker.rotation : waypoint.transform.rotation;

            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                agent.navAgent.Warp(targetPos);
            }

            agent.transform.SetPositionAndRotation(targetPos, targetRot);
            StopAgent(agent);

            state.navigationState = CivilNavigationState.Leaning;
            state.currentLeanInteraction = interaction;
            state.currentLeanSpotIndex = spotIndex;
            state.arrivalTime = Time.time;
            state.waitDuration = spot.duration > 0f ? spot.duration : 10f;
            state.leanEndTime = Time.time + state.waitDuration;
            state.originalWorkflow = agent.currentWorkflow;

            SwitchAgentWorkflow(agent, interaction.GetRequiredWorkflow());
            PlaySocialEffect(interaction.startEffect, agent);
            PlaySocialAnimationClip(agent, interaction.initiatorAnimation);

            return true;
        }

        /// <summary>
        /// Updates a leaning agent, releasing the spot when done.
        /// </summary>
        private void HandleLeanState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float currentTime)
        {
            var waypoint = state.currentTarget;
            var spot = waypoint?.GetLeanSpot(state.currentLeanSpotIndex);

            if (waypoint == null || spot == null)
            {
                state.navigationState = CivilNavigationState.Idle;
                return;
            }

            // Keep the agent aligned to the marker during the lean.
            if (spot.marker != null)
            {
                agent.transform.position = spot.marker.position;
                Quaternion targetRot = spot.marker.rotation;
                agent.transform.rotation = Quaternion.RotateTowards(agent.transform.rotation, targetRot, 720f * Time.deltaTime);
            }

            if (currentTime >= state.leanEndTime)
            {
                if (state.currentLeanInteraction?.outroAnimation?.clip != null)
                {
                    PlaySocialAnimationClip(agent, state.currentLeanInteraction.outroAnimation);
                }

                PlaySocialEffect(state.currentLeanInteraction?.endEffect, agent);

                waypoint.ReleaseLeanSpot(agent.index);

                waypoint.UnregisterAgent(agent.index);

                if (state.originalWorkflow != agent.currentWorkflow)
                {
                    SwitchAgentWorkflow(agent, state.originalWorkflow);
                }

                state.currentLeanInteraction = null;
                state.currentLeanSpotIndex = -1;

                SelectAndNavigateToWaypoint(agent, state, crowd, waypoint);
            }
        }

        /// <summary>
        /// Attempts to start a sit interaction at the waypoint.
        /// </summary>
        private bool TryStartSit(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, CivilWaypointNode waypoint)
        {
            if (waypoint == null)
                return false;

            if (!waypoint.TryReserveSitSpot(agent.index, out CivilSitSpot spot, out int spotIndex))
                return false;

            var socialSettings = crowd?.civilSettings?.socialSettings;
            SocialInteractionDefinition interaction = null;

            if (socialSettings?.interactions != null && socialSettings.interactions.Count > 0)
            {
                if (!string.IsNullOrEmpty(spot.interactionId))
                {
                    interaction = socialSettings.interactions.Find(i => i.interactionId == spot.interactionId);
                }

                interaction ??= socialSettings.interactions[0];
            }

            if (interaction == null)
            {
                waypoint.ReleaseSitSpot(agent.index);
                return false;
            }

            Vector3 targetPos = spot.marker != null ? spot.marker.position : waypoint.transform.position;
            Quaternion targetRot = spot.marker != null ? spot.marker.rotation : waypoint.transform.rotation;

            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                agent.navAgent.Warp(targetPos);
            }

            agent.transform.SetPositionAndRotation(targetPos, targetRot);
            StopAgent(agent);

            state.navigationState = CivilNavigationState.Sitting;
            state.currentSitInteraction = interaction;
            state.currentSitSpotIndex = spotIndex;
            state.arrivalTime = Time.time;
            state.waitDuration = spot.duration > 0f ? spot.duration : 10f;
            state.sitEndTime = Time.time + state.waitDuration;
            state.originalWorkflow = agent.currentWorkflow;

            SwitchAgentWorkflow(agent, interaction.GetRequiredWorkflow());
            PlaySocialEffect(interaction.startEffect, agent);
            PlaySocialAnimationClip(agent, interaction.initiatorAnimation);

            return true;
        }

        /// <summary>
        /// Updates a sitting agent, releasing the spot when done.
        /// </summary>
        private void HandleSitState(CrowdAgent agent, CivilAgentState state, EnemyCrowd crowd, float currentTime)
        {
            var waypoint = state.currentTarget;
            var spot = waypoint?.GetSitSpot(state.currentSitSpotIndex);

            if (waypoint == null || spot == null)
            {
                state.navigationState = CivilNavigationState.Idle;
                return;
            }

            if (spot.marker != null)
            {
                agent.transform.position = spot.marker.position;
                Quaternion targetRot = spot.marker.rotation;
                agent.transform.rotation = Quaternion.RotateTowards(agent.transform.rotation, targetRot, 720f * Time.deltaTime);
            }

            if (currentTime >= state.sitEndTime)
            {
                if (state.currentSitInteraction?.outroAnimation?.clip != null)
                {
                    PlaySocialAnimationClip(agent, state.currentSitInteraction.outroAnimation);
                }

                PlaySocialEffect(state.currentSitInteraction?.endEffect, agent);

                waypoint.ReleaseSitSpot(agent.index);
                waypoint.UnregisterAgent(agent.index);

                if (state.originalWorkflow != agent.currentWorkflow)
                {
                    SwitchAgentWorkflow(agent, state.originalWorkflow);
                }

                state.currentSitInteraction = null;
                state.currentSitSpotIndex = -1;

                SelectAndNavigateToWaypoint(agent, state, crowd, waypoint);
            }
        }

        /// <summary>
        /// Plays a social animation clip on an agent, supporting both Animator and GPUI workflows.
        /// </summary>
        private void PlaySocialAnimationClip(CrowdAgent agent, SocialAnimationClip animConfig)
        {
            if (animConfig?.clip == null)
                return;

            // Animator-driven workflows
            var animator = agent.crowdInstance?.GetComponent<Animator>();
            if (animator != null)
            {
                if (animConfig.animationLayer >= 0)
                {
                    animator.CrossFade(animConfig.clip.name, animConfig.crossFadeDuration, animConfig.animationLayer);
                }
                else
                {
                    animator.CrossFade(animConfig.clip.name, animConfig.crossFadeDuration);
                }
                animator.speed = animConfig.speedMultiplier;
            }

            // ComputeAnimator workflow
            if (agent.currentWorkflow == HybridWorkflowType.ComputeAnimator && agent.crowdInstance != null)
            {
                agent.crowdInstance.StartAnimation(animConfig.clip, -1f, animConfig.speedMultiplier,
                    animConfig.crossFadeDuration, animConfig.loop, true);
            }
        }

        #endregion
        
        #region Utilities
        
        private EnemyCrowd GetAgentCrowd(CrowdAgent agent)
        {
            if (agent.factionIndex >= 0 && agent.factionIndex < _enemyCrowds.Count)
            {
                return _enemyCrowds[agent.factionIndex];
            }
            return null;
        }

        private static bool SupportsCivilNavigationBackend(EnemyCrowd crowd)
        {
            if (crowd == null)
                return false;

            return crowd.navigationType == NavigationType.UnityNavMesh;
        }
        
        private void SetAgentDestination(CrowdAgent agent, Vector3 destination)
        {
            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                agent.navAgent.isStopped = false;
                agent.navAgent.SetDestination(destination);
            }
        }
        
        private void StopAgent(CrowdAgent agent)
        {
            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                agent.navAgent.isStopped = true;
                agent.navAgent.ResetPath();
            }
        }
        
        #region Path Divergence
        
        /// <summary>
        /// Updates path divergence for more natural movement.
        /// </summary>
        private void UpdatePathDivergence(CrowdAgent agent, CivilAgentState state, CivilBehaviorSettings settings, float deltaTime)
        {
            float currentTime = Time.time;
            
            // Initialize divergence seed if needed
            if (state.divergenceSeed == 0f)
            {
                state.divergenceSeed = agent.index * 0.123f + Random.value * 100f;
            }
            
            // Check if we need a new divergence target
            if (currentTime >= state.nextDivergenceTime)
            {
                // Calculate new divergence offset using Perlin noise for smooth variation
                float noiseX = Mathf.PerlinNoise(state.divergenceSeed + currentTime * settings.divergenceFrequency, 0f);
                float noiseZ = Mathf.PerlinNoise(0f, state.divergenceSeed + currentTime * settings.divergenceFrequency);
                
                // Map noise from 0-1 to -1 to 1
                noiseX = (noiseX - 0.5f) * 2f;
                noiseZ = (noiseZ - 0.5f) * 2f;
                
                // Calculate offset perpendicular to current movement direction
                Vector3 forward = agent.navAgent != null ? agent.navAgent.velocity.normalized : agent.transform.forward;
                if (forward.sqrMagnitude < 0.01f)
                    forward = agent.transform.forward;
                
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                
                // Apply divergence scaled by settings
                float maxOffset = settings.maxDivergenceOffset * settings.pathDivergence;
                state.targetDivergenceOffset = right * noiseX * maxOffset + forward * noiseZ * maxOffset * 0.5f;
                
                // Schedule next divergence update
                float interval = 1f / Mathf.Max(0.1f, settings.divergenceFrequency);
                state.nextDivergenceTime = currentTime + interval * Random.Range(0.8f, 1.2f);
            }
            
            // Smoothly interpolate to target offset
            float smoothing = settings.divergenceSmoothness * deltaTime;
            state.currentDivergenceOffset = Vector3.Lerp(
                state.currentDivergenceOffset, 
                state.targetDivergenceOffset, 
                Mathf.Clamp01(smoothing));
            
            // Apply divergence to current destination if we have one
            if (state.currentTarget != null && agent.navAgent != null && agent.navAgent.isOnNavMesh && agent.navAgent.hasPath)
            {
                Vector3 baseDestination = state.targetPosition;
                Vector3 divergedDestination = baseDestination + state.currentDivergenceOffset;
                
                // Ensure diverged position is on NavMesh
                if (NavMesh.SamplePosition(divergedDestination, out NavMeshHit hit, settings.maxDivergenceOffset, NavMesh.AllAreas))
                {
                    // Only update if significantly different to avoid constant recalculation
                    float distToNewDest = Vector3.Distance(agent.navAgent.destination, hit.position);
                    if (distToNewDest > 0.5f)
                    {
                        agent.navAgent.SetDestination(hit.position);
                    }
                }
            }
        }
        
        /// <summary>
        /// Initializes divergence state for a new agent.
        /// </summary>
        private void InitializeDivergenceState(CivilAgentState state, int agentIndex)
        {
            state.divergenceSeed = agentIndex * 0.123f + Random.value * 100f;
            state.currentDivergenceOffset = Vector3.zero;
            state.targetDivergenceOffset = Vector3.zero;
            state.nextDivergenceTime = 0f;
        }
        
        #endregion
        
        private void ApplyMovementStyle(CrowdAgent agent, CivilAgentState state, CivilBehaviorSettings settings)
        {
            if (agent.navAgent == null)
                return;
            
            CivilMovementStyle style = state.currentMovementStyle;
            
            // Varied style picks random each time
            if (settings.movementStyle == CivilMovementStyle.Varied)
            {
                int random = Random.Range(0, 3);
                style = random switch
                {
                    0 => CivilMovementStyle.Walk,
                    1 => CivilMovementStyle.Stroll,
                    _ => CivilMovementStyle.Hurried
                };
                state.currentMovementStyle = style;
            }
            
            float speedMultiplier = style switch
            {
                CivilMovementStyle.Stroll => settings.strollSpeedMultiplier,
                CivilMovementStyle.Hurried => settings.hurriedSpeedMultiplier,
                _ => settings.walkSpeedMultiplier
            };
            
            // Apply to NavMeshAgent
            EnemyCrowd crowd = GetAgentCrowd(agent);
            float baseSpeed = crowd != null ? crowd.GetModifiedSpeed(crowd.agentBaseSpeed) : 3.5f;
            agent.navAgent.speed = baseSpeed * speedMultiplier;
        }
        
        private void ApplyFadeToAgent(CrowdAgent agent, float alpha)
        {
            // Apply fade through GPU Instancer material properties
            if (agent.crowdInstance != null)
            {
                // Try to set alpha through material property block
                var mpb = new MaterialPropertyBlock();
                
                // Get current renderer
                var renderer = agent.crowdInstance.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(mpb);
                    
                    // Try different alpha property names
                    mpb.SetFloat(AlphaPropertyId, alpha);
                    
                    // Also try setting color alpha
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(ColorPropertyId))
                    {
                        Color color = renderer.sharedMaterial.GetColor(ColorPropertyId);
                        color.a = alpha;
                        mpb.SetColor(ColorPropertyId, color);
                    }
                    
                    renderer.SetPropertyBlock(mpb);
                }
            }
        }
        
        private void DespawnAgent(CrowdAgent agent, CivilAgentState state)
        {
            // Unregister from waypoint
            if (state.currentTarget != null)
            {
                state.currentTarget.UnregisterAgent(agent.index);
            }
            
            // Remove civil state
            _civilStates.Remove(agent.index);
            
            // Use the crowd controller's despawn/kill mechanism
            // This properly handles GPUI cleanup
            _crowdController.KillAgent(agent.index, useRagdoll: false);
        }
        
        private void SwitchAgentWorkflow(CrowdAgent agent, HybridWorkflowType targetWorkflow)
        {
            if (agent.crowdInstance == null)
                return;
            
            int workflowId = targetWorkflow switch
            {
                HybridWorkflowType.BoneTracker => GPUIAWBoneTracker.WORKFLOW_ID,
                HybridWorkflowType.MecanimReader => GPUIAWMecanimReader.WORKFLOW_ID,
                _ => GPUIAWComputeAnimator.WORKFLOW_ID
            };
            
            agent.crowdInstance.AnimatorWorkflowID = workflowId;
            agent.currentWorkflow = targetWorkflow;
        }
        
        private bool IsAgentDistant(CrowdAgent agent)
        {
            // Use camera distance or player distance check
            // For now, simple distance from main camera
            if (Camera.main == null)
                return false;
            
            float distSq = (agent.transform.position - Camera.main.transform.position).sqrMagnitude;
            return distSq > 2500f; // 50m squared
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Forces an agent to navigate to a specific waypoint.
        /// </summary>
        public void ForceNavigateToWaypoint(int agentIndex, CivilWaypointNode targetWaypoint)
        {
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return;
            
            CrowdAgent agent = _agents[agentIndex];
            EnemyCrowd crowd = GetAgentCrowd(agent);
            
            if (crowd != null)
            {
                NavigateToWaypoint(agent, state, crowd, targetWaypoint, state.currentTarget);
            }
        }
        
        /// <summary>
        /// Forces an agent to start an activity.
        /// </summary>
        public void ForceStartActivity(int agentIndex, CivilActivityDefinition activity)
        {
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return;
            
            CrowdAgent agent = _agents[agentIndex];
            EnemyCrowd crowd = GetAgentCrowd(agent);
            
            if (crowd != null && state.currentTarget != null)
            {
                state.currentActivity = activity;
                state.navigationState = CivilNavigationState.PerformingActivity;
                state.activityStartTime = Time.time;
                state.activityDuration = Random.Range(activity.minDuration, activity.maxDuration);
                
                StopAgent(agent);
                
                if (activity.propAttachments != null && activity.propAttachments.Length > 0)
                {
                    state.originalWorkflow = agent.currentWorkflow;
                    SwitchAgentWorkflow(agent, HybridWorkflowType.BoneTracker);
                    SpawnActivityProps(agent, state, activity);
                }
                
                if (activity.animationClip != null)
                {
                    PlayActivityAnimation(agent, activity);
                }
                
                activity.FireStarted(agent.index);
            }
        }
        
        /// <summary>
        /// Forces an agent to despawn with fade.
        /// </summary>
        public void ForceDespawn(int agentIndex)
        {
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return;
            
            CrowdAgent agent = _agents[agentIndex];
            
            // End any current activity
            if (state.navigationState == CivilNavigationState.PerformingActivity)
            {
                CleanupActivityProps(state);
            }

            if (state.navigationState == CivilNavigationState.Leaning && state.currentTarget != null)
            {
                state.currentTarget.ReleaseLeanSpot(agentIndex);
            }

            if (state.navigationState == CivilNavigationState.Sitting && state.currentTarget != null)
            {
                state.currentTarget.ReleaseSitSpot(agentIndex);
            }
            
            state.navigationState = CivilNavigationState.FadingOut;
            state.fadeDirection = -1;
            state.isFading = true;
            StopAgent(agent);
        }
        
        /// <summary>
        /// Gets statistics about civil agents.
        /// </summary>
        public CivilAgentStats GetStats()
        {
            var stats = new CivilAgentStats();
            
            foreach (var kvp in _civilStates)
            {
                stats.totalCivilAgents++;
                
                switch (kvp.Value.navigationState)
                {
                    case CivilNavigationState.Walking:
                        stats.walkingAgents++;
                        break;
                    case CivilNavigationState.Exploring:
                        stats.exploringAgents++;
                        break;
                    case CivilNavigationState.PerformingActivity:
                        stats.performingActivityAgents++;
                        break;
                    case CivilNavigationState.Leaning:
                        stats.leaningAgents++;
                        break;
                    case CivilNavigationState.Sitting:
                        stats.sittingAgents++;
                        break;
                    case CivilNavigationState.SocialInteraction:
                        stats.socialInteractionAgents++;
                        break;
                    case CivilNavigationState.LookingAround:
                        stats.lookingAroundAgents++;
                        break;
                    case CivilNavigationState.FadingIn:
                    case CivilNavigationState.FadingOut:
                        stats.fadingAgents++;
                        break;
                }
            }
            
            return stats;
        }
        
        /// <summary>
        /// Gets the animation blend value for a civil agent based on actual velocity.
        /// Returns null if not a civil agent.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to check.</param>
        /// <returns>Animation blend value (0=idle, 0.5=walk, 1=run) or null if not civil.</returns>
        public float? GetCivilAnimationBlend(int agentIndex)
        {
            // Check if this is a civil agent
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return null;
            
            // Get the agent
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return null;
            
            CrowdAgent agent = _agents[agentIndex];
            if (agent == null)
                return null;
            
            // If agent is in certain states, return idle
            if (state.navigationState == CivilNavigationState.SocialInteraction ||
                state.navigationState == CivilNavigationState.LookingAround ||
                state.navigationState == CivilNavigationState.Leaning ||
                state.navigationState == CivilNavigationState.Sitting ||
                state.navigationState == CivilNavigationState.PerformingActivity ||
                state.navigationState == CivilNavigationState.Waiting ||
                state.navigationState == CivilNavigationState.Idle)
            {
                return 0f;
            }
            
            // During corner smoothing, return blend based on movement style directly
            // This bypasses the velocity check since NavAgent is stopped during corner smoothing
            if (state.isCornerSmoothing)
            {
                // Return the appropriate walk/run blend based on movement style
                // 0.5 = walk, higher values = faster/run
                return state.currentMovementStyle switch
                {
                    CivilMovementStyle.Stroll => 0.45f,   // Slower walk
                    CivilMovementStyle.Hurried => 0.75f,  // Fast walk / jog
                    _ => 0.5f  // Normal walk
                };
            }
            
            // For normal walking, use actual velocity
            float actualSpeed = 0f;
            if (agent.navAgent != null && agent.navAgent.isOnNavMesh)
            {
                actualSpeed = agent.navAgent.velocity.magnitude;
            }
            
            // If not moving, return idle
            if (actualSpeed < 0.1f)
                return 0f;
            
            // Get reference speeds for blend calculation
            EnemyCrowd crowd = GetAgentCrowd(agent);
            if (crowd == null)
                return 0.5f; // Default to walk if no crowd
            
            float agentSpeed = agent.navAgent != null ? agent.navAgent.speed : 3.5f;
            
            // Calculate blend based on ratio of actual speed to target speed
            // If moving at full speed, return the appropriate blend for movement style
            float speedRatio = actualSpeed / Mathf.Max(agentSpeed, 0.1f);
            
            if (speedRatio < 0.1f)
                return 0f; // Idle
            
            // Map speed ratio to blend value
            // speedRatio ~1.0 means moving at full assigned speed
            float targetBlend = state.currentMovementStyle switch
            {
                CivilMovementStyle.Stroll => 0.45f,
                CivilMovementStyle.Hurried => 0.75f,
                _ => 0.5f
            };
            
            // Scale blend by how fast they're actually moving vs their target
            return Mathf.Lerp(0.1f, targetBlend, Mathf.Clamp01(speedRatio));
        }
        
        /// <summary>
        /// Checks if a civil agent is currently corner smoothing.
        /// Used by the animation controller to skip isStopped checks during corner smoothing.
        /// </summary>
        public bool IsCivilAgentCornerSmoothing(int agentIndex)
        {
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return false;
            
            return state.isCornerSmoothing;
        }
        
        #endregion
        
        #region Social Interaction Integration
        
        /// <summary>
        /// Sets an agent into social interaction state.
        /// Called by CivilSocialInteractionManager.
        /// </summary>
        public void SetAgentSocialInteractionState(int agentIndex, ActiveSocialInteraction interaction)
        {
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return;
            
            state.navigationState = CivilNavigationState.SocialInteraction;
            state.socialState ??= new AgentSocialState();
            state.socialState.isInInteraction = true;
            state.socialState.currentInteraction = interaction;
            
            // Stop the agent
            if (agentIndex >= 0 && agentIndex < _agents.Count)
            {
                var agent = _agents[agentIndex];
                if (agent != null)
                    StopAgent(agent);
            }
        }
        
        /// <summary>
        /// Ends a social interaction for an agent and resumes navigation.
        /// Called by CivilSocialInteractionManager.
        /// </summary>
        public void EndAgentSocialInteraction(int agentIndex)
        {
            if (!_civilStates.TryGetValue(agentIndex, out CivilAgentState state))
                return;
            
            if (state.socialState != null)
            {
                state.socialState.isInInteraction = false;
                state.socialState.currentInteraction = null;
            }
            
            // Resume navigation
            if (state.currentTarget != null)
            {
                state.navigationState = CivilNavigationState.Walking;
                
                // Resume moving to target
                if (agentIndex >= 0 && agentIndex < _agents.Count)
                {
                    var agent = _agents[agentIndex];
                    var crowd = GetAgentCrowd(agent);
                    if (agent != null && crowd != null)
                    {
                        Vector3 targetWithDivergence = state.targetPosition + state.currentDivergenceOffset;
                        SetAgentDestination(agent, targetWithDivergence);
                        ApplyMovementStyle(agent, state, crowd.civilSettings);
                    }
                }
            }
            else
            {
                state.navigationState = CivilNavigationState.Idle;
            }
        }

        internal void PlaySocialEffect(SocialInteractionEffect effect, int agentIndex)
        {
            if (effect == null || !effect.enabled)
                return;

            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;

            var agent = _agents[agentIndex];
            PlaySocialEffect(effect, agent);
        }

        internal void PlaySocialEffect(SocialInteractionEffect effect, CrowdAgent agent)
        {
            if (effect == null || !effect.enabled || agent == null)
                return;

            Vector3 position = agent.transform.position + agent.transform.TransformVector(effect.vfxOffset);
            Quaternion rotation = agent.transform.rotation;

            if (effect.vfx != null && effect.vfx.IsValid)
            {
                float duration = effect.vfxDuration > 0f ? effect.vfxDuration : -1f;
                _socialVfxSpawner.SpawnEffect(effect.vfx, position, rotation, duration);
            }

            if (effect.sound != null)
            {
                CrowdAudioManager.Instance?.PlaySound(
                    effect.sound,
                    position,
                    effect.soundVolume,
                    effect.soundPitchVariation,
                    effect.soundSpatialBlend);
            }
        }
        
        /// <summary>
        /// Gets the social interaction manager for external access.
        /// </summary>
        public CivilSocialInteractionManager SocialInteractionManager => _socialInteractionManager;
        
        #endregion
    }
    
    /// <summary>
    /// Statistics about civil agents.
    /// </summary>
    public struct CivilAgentStats
    {
        public int totalCivilAgents;
        public int walkingAgents;
        public int exploringAgents;
        public int performingActivityAgents;
        public int leaningAgents;
        public int sittingAgents;
        public int socialInteractionAgents;
        public int lookingAroundAgents;
        public int fadingAgents;
    }
}
