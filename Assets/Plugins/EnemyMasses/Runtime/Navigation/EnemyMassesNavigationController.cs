using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using Arawn.EnemyMasses.Runtime.Navigation;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages navigation agent movement, destination assignment, and throttled updates.
    /// Handles formation-based destinations with spatial batching and priority-based processing.
    /// Supports both Unity NavMesh and A* Pathfinding through the INavigationAgent abstraction.
    /// </summary>
    public class EnemyMassesNavigationController
    {
        public static bool NavmeshImpulseDebug = false;
        private readonly NavigationSettings _settings;
        private readonly PerformanceSettings _performanceSettings;
        private readonly CombatSettings _combatSettings;
        private readonly EnemyMassesFormationSystem _formationSystem;
        
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        private int _lastKnownAgentCount;
        private readonly List<int> _destinationWorkIndices = new List<int>();
        private readonly List<int> _priorityAgentIndices;
        private readonly List<int> _visibleAgentIndices;
        private readonly List<int> _offscreenAgentIndices;
        private readonly HashSet<int> _priorityAgentSet;
        
        private Vector3 _currentTarget;
        private Vector3 _previousTarget;
        private Vector3 _pendingDestination;
        private bool _hasPendingDestination;
        private bool _destinationWorkQueueDirty;
        private int _destinationWorkCursor;
        private int _destinationGeneration;
        private float _lastDestinationCompletionTime;
        private float _lastTargetChangeTime;
        
        private int _successfulDestinationCount;
        private int _fallbackDestinationCount;
        private int _destinationFailureCount;
        private int _consecutiveHighFailureFrames;
        
        private float _lastAttackTargetSetTime;
        private bool _attackTargetsRecentlySet;
        private int _attackTargetUpdateCursor; // Rotating cursor for continuous updates
        
        // Cached faction combat state (updated once per frame for performance)
        private readonly Dictionary<int, bool> _factionCombatStateCache = new Dictionary<int, bool>();
        private int _factionCombatStateCacheFrame = -1;
        
        private const float DESTINATION_CHECK_COOLDOWN = 2f;
        private const float FAILURE_RATE_THRESHOLD = 0.5f; // 50% failure triggers adaptive throttling
        private const int MIN_ADAPTIVE_QUOTA = 100; // Minimum quota when NavMesh is overwhelmed
        private const float ATTACK_TARGET_UPDATE_INTERVAL = 0.2f; // Update each agent every 0.2s for smooth tracking
        
        /// <summary>
        /// Callback to check if an agent is in civil mode.
        /// Civil agents are excluded from formation/combat navigation.
        /// </summary>
        public System.Func<int, bool> IsAgentCivilCallback { get; set; }

        public event System.Action<Vector3> OnDestinationChanged;
        public event System.Action OnDestinationWorkCompleted;

        /// <summary>
        /// Gets the current destination generation number.
        /// </summary>
        public int DestinationGeneration => _destinationGeneration;

        /// <summary>
        /// Gets the current target position.
        /// </summary>
        public Vector3 CurrentTarget => _currentTarget;

        /// <summary>
        /// Gets whether there is pending destination work.
        /// </summary>
        public bool HasPendingDestination => _hasPendingDestination;

        /// <summary>
        /// Creates a new navigation controller.
        /// </summary>
        public EnemyMassesNavigationController(
            NavigationSettings settings,
            PerformanceSettings performanceSettings,
            CombatSettings combatSettings,
            EnemyMassesFormationSystem formationSystem,
            List<CrowdAgent> agents,
            List<EnemyCrowd> enemyCrowds,
            List<int> priorityAgentIndices,
            List<int> visibleAgentIndices,
            List<int> offscreenAgentIndices,
            HashSet<int> priorityAgentSet)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _performanceSettings = performanceSettings ?? throw new System.ArgumentNullException(nameof(performanceSettings));
            _combatSettings = combatSettings ?? throw new System.ArgumentNullException(nameof(combatSettings));
            _formationSystem = formationSystem ?? throw new System.ArgumentNullException(nameof(formationSystem));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
            _enemyCrowds = enemyCrowds ?? throw new System.ArgumentNullException(nameof(enemyCrowds));
            _priorityAgentIndices = priorityAgentIndices ?? throw new System.ArgumentNullException(nameof(priorityAgentIndices));
            _visibleAgentIndices = visibleAgentIndices ?? throw new System.ArgumentNullException(nameof(visibleAgentIndices));
            _offscreenAgentIndices = offscreenAgentIndices ?? throw new System.ArgumentNullException(nameof(offscreenAgentIndices));
            _priorityAgentSet = priorityAgentSet ?? throw new System.ArgumentNullException(nameof(priorityAgentSet));
        }

        /// <summary>
        /// Initializes the navigation system with a starting target.
        /// </summary>
        public void Initialize(Vector3 startingTarget)
        {
            _currentTarget = startingTarget;
            _previousTarget = startingTarget;
            _pendingDestination = startingTarget;
            _hasPendingDestination = false;
            _destinationWorkQueueDirty = false;
            _destinationGeneration = 0;
            _lastDestinationCompletionTime = Time.time;
            _lastKnownAgentCount = _agents.Count; // Initialize with current count
        }

        /// <summary>
        /// Sets a new target destination for all agents.
        /// </summary>
        public void SetTarget(Vector3 target)
        {
            _currentTarget = target;
        }

        /// <summary>
        /// Notifies the navigation controller that attack targets were set.
        /// Triggers warmup delay for GPU Instancer initialization.
        /// </summary>
        public void NotifyAttackTargetsSet()
        {
            _lastAttackTargetSetTime = Time.time;
            _attackTargetsRecentlySet = true;
        }

        /// <summary>
        /// Updates NavMesh destinations for agents with attack targets.
        /// Uses a rotating cursor to continuously update all agents for smooth target tracking.
        /// </summary>
        public void UpdateAttackTargets(int maxUpdatesPerFrame = 50)
        {
            // Apply warmup delay if attack targets were recently set
            // This is critical for complex models (40k+ tris) with GPU Instancer
            if (_attackTargetsRecentlySet)
            {
                float timeSinceSet = Time.time - _lastAttackTargetSetTime;
                float warmupDelay = _agents.Count > 300 ? 0.25f : 0.1f;
                
                // Extended warmup (500ms) for newly spawned agents with complex models
                warmupDelay = Mathf.Max(warmupDelay, 0.5f);
                
                if (timeSinceSet < warmupDelay)
                {
                    return; // Still warming up, don't update destinations yet
                }
                
                // Warmup complete - reset cursor to process all agents
                _attackTargetsRecentlySet = false;
                _attackTargetUpdateCursor = 0;
            }
            
            int updated = 0;
            int agentsWithTargets = 0;
            int skippedNoTarget = 0;
            int skippedCantControl = 0;
            int skippedCombatOverride = 0;
            int skippedNotChasing = 0;
            
            // Use rotating cursor to continuously update all agents with attack targets
            // Process up to maxUpdatesPerFrame agents per frame, spreading work over multiple frames
            int startCursor = _attackTargetUpdateCursor;
            int processed = 0;
            
            // Process a chunk of agents starting from the cursor
            for (int i = 0; i < maxUpdatesPerFrame && processed < _agents.Count; i++)
            {
                // Wrap cursor if needed
                if (_attackTargetUpdateCursor >= _agents.Count)
                    _attackTargetUpdateCursor = 0;
                
                var agent = _agents[_attackTargetUpdateCursor];
                _attackTargetUpdateCursor++;
                processed++;
                
                // Skip agents without attack targets
                if (agent == null || agent.navigationAgent == null || !agent.hasAttackTargetPosition || agent.isDead)
                {
                    skippedNoTarget++;
                    continue;
                }

                agentsWithTargets++;

                // Update position from transform if available
                if (agent.attackTargetTransform != null)
                {
                    agent.attackTargetPosition = agent.attackTargetTransform.position;
                }

                // Always update destination for agents with attack targets
                // This ensures smooth tracking of moving targets
                if (!CanControlNavigationAgent(agent.navigationAgent))
                {
                    skippedCantControl++;
                    continue;
                }

                // HoldPosition stance: Don't chase targets, stay at hold position
                // BUT: If agent has an active manual move command, let them execute it
                // (the hold position will be updated when they reach the destination)
                if (agent.combatStance == CombatStance.HoldPosition && agent.hasHoldPositionSet)
                {
                    // If agent has a manual command to move somewhere else, let them move there
                    // This allows players to reposition HoldPosition agents
                    if (agent.hasManualCommand && agent.navigationState == AgentNavigationState.ManualCommand)
                    {
                        // Player issued a move command - let them execute it
                        // Skip hold position enforcement while moving to new position
                        continue;
                    }
                    
                    skippedCantControl++; // Count as cannot control for stats
                    // Ensure agent stays at hold position
                    if (agent.navigationAgent.IsOnNavMesh)
                    {
                        float distFromHoldPos = Vector3.Distance(agent.WorldPosition, agent.holdPositionLocation);
                        if (distFromHoldPos > 1f)
                        {
                            // Agent drifted, return to hold position
                            if (agent.index < 3)
                            {
                                //Debug.Log($"[HOLD DEBUG] Agent {agent.index}: DRIFTED {distFromHoldPos:F1}m! " +
                                //                 $"Current: {agent.navigationAgent.Transform.position}, HoldPos: {agent.holdPositionLocation}, " +
                                //                 $"HasPath: {agent.navigationAgent.HasPath}, Destination: {agent.navigationAgent.Destination}");
                            }
                            agent.navigationAgent.SetDestination(agent.holdPositionLocation);
                        }
                        else
                        {
                            if (agent.navigationAgent.HasPath || agent.navigationAgent.PathPending)
                                agent.navigationAgent.ResetPath();
                        }
                    }
                    continue;
                }

                if (agent.navigationState == AgentNavigationState.Formation && agent.hasAttackTargetPosition)
                {
                    agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                }

                if (agent.navigationState == AgentNavigationState.CombatOverride)
                {
                    skippedCombatOverride++;
                    continue; // Combat positioning owns navigation now
                }
                
                // BattleLine positioning mode: Combat system handles all navigation
                // Skip standard navigation to prevent conflicting destinations
                if (agent.enemyCrowd != null && agent.enemyCrowd.positioningMode == CombatPositioningMode.BattleLine)
                {
                    skippedCombatOverride++; // Count in same category
                    continue;
                }

                if (agent.navigationState != AgentNavigationState.ChasingTarget)
                {
                    skippedNotChasing++;
                    // DEBUG: Log why agent isn't chasing
                    if (agent.index < 5 && Time.frameCount % 300 == 0)
                    {
                        // Debug log removed to reduce noise
                    }
                    continue;
                }

                // Resume the NavMeshAgent in case combat or other systems stopped it
                ResumeNavigationAgent(agent.navigationAgent);

                // Calculate destination with proper stopping distance for melee agents
                Vector3 targetDestination = agent.attackTargetPosition;
                Vector3 agentPos = agent.WorldPosition;
                Vector3 dirToTarget = (targetDestination - agentPos);
                float distToTarget = dirToTarget.magnitude;
                
                // Check if formation reformation should be applied
                bool useReformation = ShouldUseFormationReformation(agent, distToTarget);
                
                // Reformation decision debug logging removed
                
                if (useReformation)
                {
                    // Use formation reformation for distant agents
                    targetDestination = CalculateFormationReformationPosition(agent, agent.attackTargetPosition, agent.index);
                }
                
                // For melee agents, offset destination so they stop at attack distance
                if (agent.enemyCrowd != null && 
                    (agent.enemyCrowd.combatRangeType == CombatRangeType.Melee || 
                     (agent.enemyCrowd.combatRangeType == CombatRangeType.Hybrid && agent.activeHybridMode == CombatRangeType.Melee)))
                {
                    // Get the ideal stopping distance (middle of attack range)
                    float idealStopDistance = (agent.enemyCrowd.meleeMinDistance + agent.enemyCrowd.meleeMaxDistance) * 0.5f;
                    
                    // Set NavMeshAgent stopping distance to match attack range
                    agent.navigationAgent.StoppingDistance = idealStopDistance;
                    
                    // If already close enough, don't set destination (prevents walking through target)
                    // Only apply this check when NOT in reformation mode (reformation keeps agents moving)
                    if (!useReformation && distToTarget <= idealStopDistance + 0.1f)
                    {
                        // Already in range - stop movement but keep facing target
                        agent.navigationAgent.IsStopped = true;
                        continue;
                    }
                    
                    // Calculate position that will put agent at ideal attack distance
                    // Only offset if not using reformation (reformation handles positioning)
                    if (!useReformation)
                    {
                        Vector3 dirNormalized = dirToTarget.normalized;
                        targetDestination = agent.attackTargetPosition - dirNormalized * idealStopDistance;
                    }
                }

                // Throttle backends that are sensitive to frequent destination updates
                bool shouldThrottleUpdates = agent.navigationAgent.Capabilities.ShouldThrottleFrequentDestinationUpdates;
                if (shouldThrottleUpdates)
                {
                    float updateInterval = agent.enemyCrowd != null
                        ? agent.enemyCrowd.navTargetUpdateInterval
                        : _combatSettings.navTargetUpdateInterval;

                    bool hasValidCurrentDest = !(float.IsNaN(agent.navigationAgent.Destination.x) || float.IsInfinity(agent.navigationAgent.Destination.x));
                    bool targetMoved = !hasValidCurrentDest ||
                                       (agent.navigationAgent.Destination - targetDestination).sqrMagnitude > 0.25f;

                    if (!targetMoved && agent.navigationAgent.PathPending)
                        continue;

                    if (!targetMoved && updateInterval > 0f && (Time.time - agent.lastAttackTargetUpdateTime) < updateInterval)
                        continue;
                }

                agent.navigationAgent.SetDestination(targetDestination);
                agent.lastAttackTargetUpdateTime = Time.time;
                agent.lastDestination = targetDestination;
                agent.hasArrived = false; // Keep chasing
                updated++;
            }
        }

        /// <summary>
        /// Updates destination work queue and processes pending assignments.
        /// Call this every frame from the main controller.
        /// </summary>
        public void UpdateDestinations(bool useVisibilityPriority, SpatialGrid spatialGrid)
        {
            // Check if target changed
            bool destinationChanged = _currentTarget != _previousTarget;
            
            // Check if agents were just spawned (agent count increased)
            bool agentsJustSpawned = _agents.Count > _lastKnownAgentCount;
            if (agentsJustSpawned)
            {
                _lastKnownAgentCount = _agents.Count;
            }
            
            if (destinationChanged)
            {
                _destinationGeneration++;
                _pendingDestination = _currentTarget;
                _hasPendingDestination = true;
                _destinationWorkQueueDirty = true;
                _previousTarget = _currentTarget;
                _lastTargetChangeTime = Time.time;
                ResetAgentsForNewDestination();
                OnDestinationChanged?.Invoke(_currentTarget);
            }

            if (_agents.Count == 0)
            {
                CompleteDestinationWork();
                return;
            }

            if (!_hasPendingDestination)
            {
                // Self-healing: detect Formation agents without paths (e.g., after returning
                // from combat). RequestFormationReassignment() handles the explicit trigger,
                // but this catches edge cases where the combat system returns agents to Formation
                // without access to the navigation controller.
                // GUARD: Only run if a real destination has been assigned at least once.
                // Without this, freshly spawned agents (navState=Formation, no path) would be
                // force-sent to _pendingDestination even when the spawner had "Auto Set Destination"
                // disabled — they should stay at their spawn position until the player commands them.
                if (_destinationGeneration > 0)
                {
                    CheckForMissingDestinations();
                }
                return;
            }

            // Add warmup delay after target change to let NavMesh and GPU Instancer settle
            // Longer delay for large crowds and complex models (55k tris + blendshapes)
            float warmupDelay = _agents.Count > 300 ? 0.25f : 0.1f; // 250ms for 300+ agents
            
            // Extra warmup if agents were just spawned - GPU Instancer needs time to initialize
            if (agentsJustSpawned)
                warmupDelay = Mathf.Max(warmupDelay, 0.5f); // 500ms after spawn for complex models
            
            if (Time.time - _lastTargetChangeTime < warmupDelay)
                return;

            // Build work queue if dirty
            if (_destinationWorkQueueDirty)
                BuildDestinationWorkQueue(useVisibilityPriority);

            // Process destination assignments
            int remaining = Mathf.Max(0, _destinationWorkIndices.Count - _destinationWorkCursor);
            if (remaining > 0)
            {
                int baseQuota = _settings.throttleDestinationUpdates 
                    ? Mathf.Min(_settings.maxDestinationUpdatesPerFrame, remaining) 
                    : remaining;

                if (useVisibilityPriority && _settings.throttleDestinationUpdates && _priorityAgentIndices.Count > 0)
                    baseQuota = Mathf.Min(baseQuota + _settings.priorityDestinationBonus, remaining);

                // Apply adaptive throttling if NavMesh pathfinding is overwhelmed
                baseQuota = ApplyAdaptiveThrottling(baseQuota);

                ProcessDestinationWork(baseQuota, spatialGrid);
            }
            else if (_destinationWorkIndices.Count == 0)
            {
                CompleteDestinationWork();
            }
        }

        /// <summary>
        /// Reduces processing quota when NavMesh pathfinding is overwhelmed.
        /// </summary>
        private int ApplyAdaptiveThrottling(int requestedQuota)
        {
            // Calculate failure rate from last frame
            int totalProcessed = _successfulDestinationCount + _fallbackDestinationCount + _destinationFailureCount;
            if (totalProcessed == 0)
                return requestedQuota;

            float failureRate = (float)_destinationFailureCount / totalProcessed;

            // If failure rate is high, NavMesh pathfinding queue is saturated
            if (failureRate > FAILURE_RATE_THRESHOLD)
            {
                _consecutiveHighFailureFrames++;
                
                // Reduce quota more gradually - only reduce by 20% per consecutive failure frame
                int reduction = Mathf.FloorToInt(requestedQuota * 0.2f * _consecutiveHighFailureFrames);
                reduction = Mathf.Min(reduction, requestedQuota - MIN_ADAPTIVE_QUOTA);
                int adaptedQuota = Mathf.Max(MIN_ADAPTIVE_QUOTA, requestedQuota - reduction);
                
                if (_consecutiveHighFailureFrames == 1)
                {
                    Debug.LogWarning($"[Navigation] NavMesh pathfinding overwhelmed! " +
                                   $"Failure rate: {failureRate:P0}. Reducing quota from {requestedQuota} to {adaptedQuota}.");
                }
                
                return adaptedQuota;
            }
            else
            {
                // Reset counter when failure rate is acceptable
                if (_consecutiveHighFailureFrames > 0)
                {
                    Debug.Log($"[Navigation] NavMesh pathfinding recovered. Restoring normal quota.");
                    _consecutiveHighFailureFrames = 0;
                }
                
                return requestedQuota;
            }
        }

        /// <summary>
        /// Checks for agents missing destinations and triggers reassignment if needed.
        /// </summary>
        public void CheckForMissingDestinations()
        {
            // Don't check immediately after destination work completion
            float timeSinceCompletion = Time.time - _lastDestinationCompletionTime;
            if (timeSinceCompletion < DESTINATION_CHECK_COOLDOWN)
                return;

            // Only count agents that are actually in Formation state — agents in ManualCommand,
            // ChasingTarget, CombatOverride etc. have their own navigation and shouldn't count.
            int formationAgentsWithoutPath = 0;
            int totalFormationAgents = 0;

            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null) continue;
                if (!ShouldReceiveFormationDestination(agent)) continue;

                totalFormationAgents++;

                bool hasValidPath = agent.navigationAgent.HasPath ||
                                    agent.navigationAgent.PathPending ||
                                    agent.navigationAgent.Velocity.sqrMagnitude > 0.01f ||
                                    agent.navigationAgent.RemainingDistance > 0.1f;

                if (!hasValidPath)
                    formationAgentsWithoutPath++;
            }

            // If ANY Formation agents don't have valid paths, trigger reassignment.
            // This handles agents returning from combat that need new formation positions.
            if (formationAgentsWithoutPath > 0 && totalFormationAgents > 0)
            {
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[FormationDebug] CheckForMissingDestinations — {formationAgentsWithoutPath}/{totalFormationAgents} " +
                              $"Formation agents without paths. Triggering reassignment.");
                }

                _hasPendingDestination = true;
                _destinationWorkQueueDirty = true;
                _destinationGeneration++;
                for (int i = 0; i < _agents.Count; i++)
                {
                    var agent = _agents[i];
                    if (agent != null)
                        agent.destinationGeneration = _destinationGeneration - 1;
                }
            }
        }

        /// <summary>
        /// Checks if all agents have arrived at their destination.
        /// </summary>
        public bool AllAgentsAtDestination()
        {
            if (_agents.Count == 0)
                return false;

            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent?.navigationAgent == null)
                    return false;

                if (agent.destinationGeneration != _destinationGeneration)
                    return false;

                if (agent.hasArrived)
                    continue;

                var navAgent = agent.navigationAgent;
                if (navAgent.PathPending)
                    return false;

                if (!navAgent.HasPath)
                    return false;

                if (navAgent.RemainingDistance > navAgent.StoppingDistance + _settings.stuckProximitySlack)
                    return false;
            }

            return true;
        }

        #region Work Queue Management

        private void BuildDestinationWorkQueue(bool useVisibility)
        {
            _destinationWorkIndices.Clear();
            var addedAgents = new HashSet<int>();

            bool TryAddAgent(int agentIndex)
            {
                if (agentIndex < 0 || agentIndex >= _agents.Count)
                    return false;

                var agent = _agents[agentIndex];
                if (!ShouldReceiveFormationDestination(agent))
                    return false;

                if (!addedAgents.Add(agentIndex))
                    return false;

                _destinationWorkIndices.Add(agentIndex);
                return true;
            }

            // Priority-based or all-agents approach
            if (_performanceSettings.prioritizeVisibleAgents && useVisibility && _performanceSettings.enablePriorityBasedVisibility)
            {
                // Add priority agents first
                for (int i = 0; i < _priorityAgentIndices.Count; i++)
                    TryAddAgent(_priorityAgentIndices[i]);

                // Add visible agents
                for (int i = 0; i < _visibleAgentIndices.Count; i++)
                {
                    int agentIndex = _visibleAgentIndices[i];
                    if (!_priorityAgentSet.Contains(agentIndex))
                        TryAddAgent(agentIndex);
                }

                // Add offscreen agents
                for (int i = 0; i < _offscreenAgentIndices.Count; i++)
                    TryAddAgent(_offscreenAgentIndices[i]);

                // Failsafe: Ensure all agents are included
                for (int i = 0; i < _agents.Count; i++)
                    TryAddAgent(i);
            }
            else
            {
                // Most reliable: Add all agents
                for (int i = 0; i < _agents.Count; i++)
                    TryAddAgent(i);
            }

            _destinationWorkCursor = 0;
            _destinationWorkQueueDirty = false;
        }

        private void ResetAgentsForNewDestination()
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent != null)
                {
                    agent.destinationGeneration = _destinationGeneration - 1;
                    agent.hasArrived = false;
                    agent.destinationRetries = 0; // Reset retry count for new destination
                }
            }
        }

        private void CompleteDestinationWork()
        {
            _hasPendingDestination = false;
            _destinationWorkQueueDirty = false;
            _destinationWorkCursor = 0;
            _destinationWorkIndices.Clear();
            _lastDestinationCompletionTime = Time.time;
            _consecutiveHighFailureFrames = 0; // Reset adaptive throttling on completion

            OnDestinationWorkCompleted?.Invoke();
        }

        /// <summary>
        /// Called by external systems (CrowdController, CombatSystem) when agents have returned
        /// to Formation state and need new formation positions. Without this, agents returning
        /// from combat would stand idle because _hasPendingDestination is false after work completion.
        /// GUARD: Only triggers if a real destination has been assigned at least once
        /// (_destinationGeneration > 0). Without this, freshly spawned agents that were never
        /// commanded would be force-sent to _pendingDestination when any agent returns from
        /// combat — causing the entire crowd to walk to a position the player never set.
        /// </summary>
        public void RequestFormationReassignment()
        {
            if (_destinationGeneration > 0)
            {
                _hasPendingDestination = true;
                _destinationWorkQueueDirty = true;
            }
        }

        #endregion

        #region Destination Processing

        private void ProcessDestinationWork(int quota, SpatialGrid spatialGrid)
        {
            if (quota <= 0)
                return;

            // Reset counters at start of processing batch
            _successfulDestinationCount = 0;
            _fallbackDestinationCount = 0;
            _destinationFailureCount = 0;

            int initialCount = _destinationWorkIndices.Count;

            // Use spatial batching if enabled and initialized
            if (_performanceSettings.enableSpatialGrid && _performanceSettings.enableSpatialBatching && spatialGrid != null)
            {
                ProcessDestinationWorkSpatially(quota, spatialGrid);
            }
            else
            {
                ProcessDestinationWorkSequentially(quota);
            }

            // Only complete when cursor reaches the current count (including newly added failed agents)
            // This ensures failed agents added back to the queue get processed
            if (_destinationWorkCursor >= _destinationWorkIndices.Count)
                CompleteDestinationWork();
        }

        private void ProcessDestinationWorkSequentially(int quota)
        {
            int total = _destinationWorkIndices.Count;
            int processed = 0;
            int formationIndex = 0; // Track position within the formation
            List<int> failedAgents = null;

            while (_destinationWorkCursor < total && processed < quota)
            {
                int agentIndex = _destinationWorkIndices[_destinationWorkCursor++];
                var agent = _agents[agentIndex];
                if (!ShouldReceiveFormationDestination(agent))
                    continue;

                // DEBUG: Log when HoldPosition agents reach this point (they shouldn't!)
                if (agent.combatStance == CombatStance.HoldPosition && agent.index < 3)
                {
                    Debug.LogWarning($"[HOLD DEBUG] Agent {agent.index}: ProcessDestinationWorkSequentially - About to set formation destination! " +
                                     $"navState={agent.navigationState}, hasHoldPositionSet={agent.hasHoldPositionSet}");
                }

                // TEMPORARY DEBUG: Log when patrol agents get formation destination override
                if (agent.spawnGroupId >= 0 && agent.index < 5)
                {
                    Debug.LogWarning($"[PATROL OVERRIDE] Agent {agentIndex} (spawnGroup={agent.spawnGroupId}) " +
                        $"is receiving FORMATION destination override! navState={agent.navigationState} " +
                        $"currentDest={agent.navigationAgent?.Destination:F2} pendingDest={_pendingDestination:F2}");
                }

                var navAgent = agent?.navigationAgent;
                if (navAgent == null)
                    continue;

                // Use formation index (position within work queue) and work queue count for proper formation positions
                // This ensures formations stay coherent when agents die or are removed
                Vector3 target = _formationSystem.CalculateFormationPosition(
                    formationIndex,
                    total,
                    _pendingDestination,
                    _settings.destinationSpread,
                    navAgent,
                    agent.formationType);
                formationIndex++;

                // Skip agents that already have valid navigation for the current destination
                // generation. When RequestFormationReassignment() or CheckForMissingDestinations()
                // triggers a recalculation, agents already walking to or near their correct
                // positions should NOT be reassigned — otherwise their positions shift and
                // cause "double placement" (two agents converging on the same spot).
                if (agent.destinationGeneration == _destinationGeneration)
                {
                    bool hasActiveNavigation = navAgent.HasPath || navAgent.PathPending ||
                        navAgent.Velocity.sqrMagnitude > 0.01f || navAgent.RemainingDistance > 0.1f;
                    bool isNearLastDestination = agent.lastDestination != Vector3.zero &&
                        (agent.lastDestination - navAgent.Transform.position).sqrMagnitude < 1f;
                    if (hasActiveNavigation || isNearLastDestination)
                    {
                        processed++;
                        continue;
                    }
                }

                if (!CanControlNavigationAgent(navAgent))
                    continue;

                ResumeNavigationAgent(navAgent);

                bool destinationSet = navAgent.SetDestination(target);
                bool usedFallback = false;

                if (!destinationSet)
                {
                    Vector3 fallbackTarget = _formationSystem.GetRandomSampledPosition(
                        navAgent.Transform.position,
                        _settings.destinationSpread,
                        navAgent);
                    destinationSet = navAgent.SetDestination(fallbackTarget);
                    if (destinationSet)
                    {
                        target = fallbackTarget;
                        usedFallback = true;
                    }
                }

                if (!destinationSet)
                {
                    // Increment retry count
                    agent.destinationRetries++;

                    // If agent has failed too many times, force-warp to formation position
                    const int MAX_RETRIES = 3;
                    if (agent.destinationRetries >= MAX_RETRIES)
                    {
                        // Force warp: disable NavMeshAgent, move transform, re-enable
                        navAgent.Enabled = false;
                        navAgent.Transform.position = target;
                        navAgent.Enabled = true;
                        
                        // Mark as successful so it doesn't retry
                        UpdateAgentDestination(agent, navAgent, target);
                        agent.destinationRetries = 0;
                        _fallbackDestinationCount++;
                        
                        Debug.LogWarning($"Agent {agentIndex} failed {MAX_RETRIES} destination attempts. Force-warped to formation position.");
                    }
                    else
                    {
                        // Mark for retry
                        _destinationFailureCount++;
                        agent.destinationGeneration = _destinationGeneration - 1;
                        if (failedAgents == null)
                            failedAgents = new List<int>();
                        failedAgents.Add(agentIndex);
                    }
                    
                    processed++;
                    continue;
                }

                // Success - reset retry count
                // Note: Don't validate path immediately - Unity needs time to queue it
                // The pathPending timeout system will catch truly stuck agents
                agent.destinationRetries = 0;

                if (usedFallback)
                    _fallbackDestinationCount++;
                else
                    _successfulDestinationCount++;

                UpdateAgentDestination(agent, navAgent, target);
                processed++;
            }

            if (failedAgents != null && failedAgents.Count > 0)
                _destinationWorkIndices.AddRange(failedAgents);
        }

        private void ProcessDestinationWorkSpatially(int quota, SpatialGrid spatialGrid)
        {
            var cellProcessCount = new Dictionary<Vector2Int, int>();
            int totalProcessed = 0;
            int formationIndex = 0; // Track position within the formation
            int formationTotalCount = _destinationWorkIndices.Count; // Total agents in formation
            List<int> deferredAgents = null;
            List<int> failedAgents = null;

            int effectiveCellLimit = CalculateAdaptiveCellLimit(spatialGrid);

            while (_destinationWorkCursor < _destinationWorkIndices.Count && totalProcessed < quota)
            {
                int agentIndex = _destinationWorkIndices[_destinationWorkCursor];
                var agent = _agents[agentIndex];

                if (!ShouldReceiveFormationDestination(agent) || agent?.navigationAgent == null)
                {
                    _destinationWorkCursor++;
                    continue;
                }

                // DEBUG: Log when HoldPosition agents reach this point (they shouldn't!)
                if (agent.combatStance == CombatStance.HoldPosition && agent.index < 3)
                {
                    Debug.LogWarning($"[HOLD DEBUG] Agent {agent.index}: ProcessDestinationWorkSpatially - About to set formation destination! " +
                                     $"navState={agent.navigationState}, hasHoldPositionSet={agent.hasHoldPositionSet}");
                }

                Vector3 agentPos = agent.WorldPosition;
                Vector2Int gridPos = WorldToGrid(agentPos, _performanceSettings.gridCellSize);

                if (!cellProcessCount.TryGetValue(gridPos, out int cellCount))
                    cellCount = 0;

                if (cellCount >= effectiveCellLimit)
                {
                    if (deferredAgents == null)
                        deferredAgents = new List<int>();
                    deferredAgents.Add(agentIndex);
                    _destinationWorkCursor++;
                    formationIndex++; // Still increment to maintain formation position for deferred agent
                    continue;
                }

                _destinationWorkCursor++;
                cellProcessCount[gridPos] = cellCount + 1;
                totalProcessed++;

                var navAgent = agent.navigationAgent;
                // Use formation index (position within work queue) and work queue count for proper formation positions
                // This ensures formations stay coherent when agents die or are removed
                Vector3 target = _formationSystem.CalculateFormationPosition(
                    formationIndex,
                    formationTotalCount,
                    _pendingDestination,
                    _settings.destinationSpread,
                    navAgent,
                    agent.formationType);
                formationIndex++;

                // Skip agents that already have valid navigation for the current destination
                // generation. Prevents double-placement when reassignment triggers while
                // most agents are already correctly positioned.
                if (agent.destinationGeneration == _destinationGeneration)
                {
                    bool hasActiveNavigation = navAgent.HasPath || navAgent.PathPending ||
                        navAgent.Velocity.sqrMagnitude > 0.01f || navAgent.RemainingDistance > 0.1f;
                    bool isNearLastDestination = agent.lastDestination != Vector3.zero &&
                        (agent.lastDestination - navAgent.Transform.position).sqrMagnitude < 1f;
                    if (hasActiveNavigation || isNearLastDestination)
                        continue;
                }

                if (!CanControlNavigationAgent(navAgent))
                    continue;

                ResumeNavigationAgent(navAgent);

                bool destinationSet = navAgent.SetDestination(target);
                bool usedFallback = false;

                if (!destinationSet)
                {
                    Vector3 fallbackTarget = _formationSystem.GetRandomSampledPosition(
                        navAgent.Transform.position,
                        _settings.destinationSpread,
                        navAgent);
                    destinationSet = navAgent.SetDestination(fallbackTarget);
                    if (destinationSet)
                    {
                        target = fallbackTarget;
                        usedFallback = true;
                    }
                }

                if (!destinationSet)
                {
                    // Increment retry count
                    agent.destinationRetries++;

                    // If agent has failed too many times, force-warp to formation position
                    const int MAX_RETRIES = 3;
                    if (agent.destinationRetries >= MAX_RETRIES)
                    {
                        // Force warp: disable NavMeshAgent, move transform, re-enable
                        navAgent.Enabled = false;
                        navAgent.Transform.position = target;
                        navAgent.Enabled = true;
                        
                        // Mark as successful so it doesn't retry
                        UpdateAgentDestination(agent, navAgent, target);
                        agent.destinationRetries = 0;
                        _fallbackDestinationCount++;
                        
                        Debug.LogWarning($"Agent {agentIndex} failed {MAX_RETRIES} destination attempts. Force-warped to formation position.");
                    }
                    else
                    {
                        // Mark for retry
                        _destinationFailureCount++;
                        agent.destinationGeneration = _destinationGeneration - 1;
                        if (failedAgents == null)
                            failedAgents = new List<int>();
                        failedAgents.Add(agentIndex);
                    }
                    continue;
                }

                // Success - reset retry count
                // Note: Don't validate path immediately - Unity needs time to queue it
                // The pathPending timeout system will catch truly stuck agents
                agent.destinationRetries = 0;

                if (usedFallback)
                    _fallbackDestinationCount++;
                else
                    _successfulDestinationCount++;

                UpdateAgentDestination(agent, navAgent, target);
            }

            if (deferredAgents != null && deferredAgents.Count > 0)
                _destinationWorkIndices.AddRange(deferredAgents);

            if (failedAgents != null && failedAgents.Count > 0)
                _destinationWorkIndices.AddRange(failedAgents);
        }

        private void UpdateAgentDestination(CrowdAgent agent, INavigationAgent navAgent, Vector3 target)
        {
            agent.lastDestination = target;
            float remainingDistance = navAgent.RemainingDistance;
            if (float.IsNaN(remainingDistance) || float.IsInfinity(remainingDistance))
                remainingDistance = Vector3.Distance(agent.WorldPosition, target);
            agent.lastKnownDistance = remainingDistance;
            agent.lastProgressTime = Time.time;
            agent.remainingRetries = _settings.stuckRetryAttempts;
            agent.nextRetryTime = Time.time + _settings.stuckRetryInterval;
            agent.hasArrived = false;
            agent.destinationGeneration = _destinationGeneration;
            
            // Track pathPending start time for timeout detection
            if (navAgent.PathPending)
                agent.pathPendingStartTime = Time.time;
            else
                agent.pathPendingStartTime = 0f;
        }

        private int CalculateAdaptiveCellLimit(SpatialGrid spatialGrid)
        {
            int baseCellLimit = _performanceSettings.maxAgentsPerCellPerFrame;

            // Auto-increase for large crowds
            int totalAgents = _agents.Count;
            if (totalAgents >= 3000)
            {
                int estimatedCellsWithAgents = Mathf.Max(1, totalAgents / 50);
                int minCellLimit = Mathf.Max(baseCellLimit, totalAgents / (estimatedCellsWithAgents * 5));
                baseCellLimit = Mathf.Max(baseCellLimit, minCellLimit);

                if (baseCellLimit > _performanceSettings.maxAgentsPerCellPerFrame * 2)
                {
                    Debug.LogWarning($"Large crowd detected ({totalAgents} agents). " +
                                   $"Auto-increasing maxAgentsPerCellPerFrame from {_performanceSettings.maxAgentsPerCellPerFrame} to {baseCellLimit}.");
                }
            }

            if (!_performanceSettings.enableAdaptiveScaling)
                return baseCellLimit;

            // Scale based on crowd size
            float crowdSizeMultiplier = 1f;
            if (totalAgents >= 5000)
                crowdSizeMultiplier = 1.5f;
            else if (totalAgents >= 3000)
                crowdSizeMultiplier = 1.3f;
            else if (totalAgents >= 1000)
                crowdSizeMultiplier = 1.1f;

            // Burst mode: Scale up for large backlog
            float burstMultiplier = 1f;
            if (_performanceSettings.enableBurstMode && _destinationWorkIndices.Count > 0)
            {
                int remaining = _destinationWorkIndices.Count - _destinationWorkCursor;
                float backlogRatio = (float)remaining / totalAgents;

                if (backlogRatio > 0.8f)
                    burstMultiplier = 3f;
                else if (backlogRatio > 0.5f)
                    burstMultiplier = 2f;
                else if (backlogRatio > 0.2f)
                    burstMultiplier = 1.5f;
            }

            int adaptiveLimit = Mathf.RoundToInt(baseCellLimit * crowdSizeMultiplier * burstMultiplier);
            return Mathf.Clamp(adaptiveLimit, 5, 50);
        }

        #endregion

        #region Helper Methods

        private static bool IsAgentActiveForNavigation(CrowdAgent agent)
        {
            if (agent == null || agent.navigationAgent == null)
                return false;

            if (agent.isDead || agent.ragdollActivated)
                return false;

            // Allow agents that aren't on NavMesh yet - Unity places them over a few frames
            // SetDestination will handle the agent not being on NavMesh properly
            return agent.navigationAgent.Enabled &&
                   agent.navigationAgent.Transform != null &&
                   agent.navigationAgent.Transform.gameObject.activeInHierarchy;
        }

        private bool ShouldReceiveFormationDestination(CrowdAgent agent)
        {
            // Skip civil agents - they use their own navigation system
            if (IsAgentCivilCallback != null && IsAgentCivilCallback(agent.index))
                return false;

            // Hold-position agents should never receive formation redistribution destinations.
            // Their movement is controlled via ManualCommand/hold-position logic.
            if (agent.combatStance == CombatStance.HoldPosition)
                return false;
            
            bool isActive = IsAgentActiveForNavigation(agent);
            bool isFormationState = agent.navigationState == AgentNavigationState.Formation;
            bool result = isActive && isFormationState;
            
            // Debug: Log when HoldPosition agents unexpectedly qualify for formation destination
            if (result && agent.combatStance == CombatStance.HoldPosition && agent.index < 3)
            {
                Debug.LogError($"[HOLD DEBUG] Agent {agent.index}: ShouldReceiveFormationDestination=TRUE despite HoldPosition stance! " +
                               $"navState={agent.navigationState}, hasHoldPositionSet={agent.hasHoldPositionSet}");
            }
            
            return result;
        }

        private static bool CanControlNavigationAgent(INavigationAgent navAgent)
        {
            if (navAgent == null || !navAgent.Enabled)
                return false;

            if (navAgent.Capabilities.RequiresSurfaceValidationForControl)
                return navAgent.IsOnNavMesh;

            return true;
        }

        private static void ResumeNavigationAgent(INavigationAgent navAgent)
        {
            if (!CanControlNavigationAgent(navAgent))
                return;

            if (navAgent.IsStopped)
                navAgent.IsStopped = false;
        }

        private static Vector2Int WorldToGrid(Vector3 worldPos, float cellSize)
        {
            float inverseCellSize = 1f / cellSize;
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x * inverseCellSize),
                Mathf.FloorToInt(worldPos.z * inverseCellSize)
            );
        }

        #endregion

        #region Movement Synchronization

        /// <summary>
        /// Synchronizes agent transforms with NavMeshAgent positions.
        /// Must be called every frame to manually update agent positions from NavMesh.
        /// Required because updatePosition=false for GPU Instancer Pro compatibility.
        /// PERFORMANCE OPTIMIZATION: Offscreen agents use time-based throttling to reduce CPU load.
        /// </summary>
        /// <param name="activeAgentIndices">List of agent indices to sync</param>
        /// <param name="rotationLerp">Lerp factor (0-1) for rotation smoothing</param>
        /// <param name="offscreenSyncInterval">Time interval for offscreen agent updates (0 = always update)</param>
        public void SyncAgentMovement(List<int> activeAgentIndices, float rotationLerp = 0.15f, float offscreenSyncInterval = 0.25f)
        {
            float currentTime = Time.time;
            float dt = Time.deltaTime;
            
            foreach (int agentIndex in activeAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= _agents.Count)
                    continue;

                CrowdAgent agent = _agents[agentIndex];
                INavigationAgent navAgent = agent?.navigationAgent;

                bool hasImpulse = agent?.hasNavmeshOffsetImpulse == true;
                bool hasAstarKnockback = agent?.hasAstarKnockbackImpulse == true;
                bool ragdollDeath = agent != null && agent.isDead && agent.enemyCrowd != null && agent.enemyCrowd.enableRagdollOnDeath;
                bool ragdollActive = agent != null && (agent.ragdollActivated || agent.ragdollPending);

                // ── Crash-from-sky descent for dead flying agents ──
                // When a flying agent is killed and crash-on-death is enabled, the combat system
                // sets isCrashing = true with initial velocities. We process the physics here
                // (gravity, forward momentum, tumble rotation) and push the result to GPUI.
                // This runs BEFORE the combat-animation skip so crashing corpses aren't frozen.
                if (agent.isCrashing)
                {
                    Transform crashTransform = agent.transform;
                    if (crashTransform == null)
                    {
                        agent.isCrashing = false;
                    }
                    else
                    {
                        EnemyCrowd crashCrowd = agent.enemyCrowd;
                        float gravity     = crashCrowd != null ? crashCrowd.flightCrashGravity     : 12f;
                        float tumbleSpeed = crashCrowd != null ? crashCrowd.flightCrashTumbleSpeed : 120f;
                        float spiralSpeed = crashCrowd != null ? crashCrowd.flightCrashSpiralSpeed : 45f;

                        // Gravity acceleration
                        agent.crashVerticalVelocity += gravity * dt;

                        // Decrement flight height tracking (used for ground check)
                        agent.flightCurrentHeight -= agent.crashVerticalVelocity * dt;

                        // Forward momentum with exponential decay
                        float forwardStep = agent.crashForwardSpeed * dt;
                        agent.crashForwardSpeed *= Mathf.Max(0f, 1f - 2f * dt);

                        // Compose new position
                        Vector3 crashPos = crashTransform.position;
                        crashPos += agent.crashForwardDirection * forwardStep;
                        crashPos.y -= agent.crashVerticalVelocity * dt;

                        // Nose-down tumble (capped at 80° to prevent full inversion)
                        agent.crashTumbleAngle = Mathf.Min(agent.crashTumbleAngle + tumbleSpeed * dt, 80f);

                        // Build rotation: base facing → pitch down → spiral roll
                        Vector3 fwd = agent.crashForwardDirection;
                        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                        Quaternion baseRot = Quaternion.LookRotation(fwd, Vector3.up);
                        float elapsed = Time.time - agent.deathTime;
                        Quaternion tumble  = Quaternion.Euler(agent.crashTumbleAngle, 0f, spiralSpeed * elapsed);
                        crashTransform.rotation = baseRot * tumble;

                        // Ground check — crash ends when visual Y reaches ground level
                        if (agent.flightCurrentHeight <= 0f || crashPos.y <= agent.crashGroundY)
                        {
                            crashPos.y = agent.crashGroundY;
                            agent.flightCurrentHeight = 0f;
                            agent.isCrashing = false;
                        }

                        crashTransform.position = crashPos;

                        // Push updated transform to GPU Instancer
                        if (agent.prefabComponent != null && agent.prefabComponent.registeredManager != null)
                        {
                            agent.prefabComponent.registeredManager.UpdateTransformData(agent.prefabComponent);
                        }
                    }

                    continue;
                }

                // Move-based knockback path: apply via INavigationAgent.Move and skip manual transform sync.
                if (navAgent != null &&
                    navAgent.Capabilities.SupportsMoveBasedKnockback &&
                    hasAstarKnockback)
                {
                    UpdateAstarKnockback(agent, navAgent, currentTime, dt);
                    continue;
                }

                // Safety: clear stuck impulses for backends that do not support navmesh offset impulses
                // after the impulse window has expired.
                // This prevents long-lived sync cost if a knockback never settles (e.g., due to external overrides).
                if (hasImpulse && navAgent != null && !navAgent.Capabilities.SupportsNavmeshOffsetImpulse)
                {
                    if (currentTime - agent.navmeshOffsetEndTime > 0.5f)
                    {
                        agent.hasNavmeshOffsetImpulse = false;
                        agent.navmeshOffset = Vector3.zero;
                        agent.navmeshOffsetVelocity = Vector3.zero;
                        agent.navmeshOffsetDuration = 0f;
                        hasImpulse = false;
                    }
                }

                // Play optional impulse reaction clip once per impulse (alive agents only)
                if (hasImpulse && agent != null && !agent.isDead && !agent.navmeshImpulseAnimationPlayed)
                {
                    EnemyMassesImpulseAnimationHelper.PlayImpulseAnimation(agent, agent.lastNavmeshImpulsePush, agent.lastNavmeshImpulseLift);
                    agent.navmeshImpulseAnimationPlayed = true;
                }

                // For fake-physics impulses, ensure the navigation agent stays enabled/active (but never re-enable for ragdolling dead agents)
                if (hasImpulse && navAgent != null && !ragdollDeath && !ragdollActive)
                {
                    if (!navAgent.Enabled)
                    {
                        navAgent.Enabled = true;
                        if (navAgent.IsOnNavMesh)
                            navAgent.IsStopped = true;
                    }
                }

                // Backends without navmesh offset support typically update their own transforms.
                // Skip the expensive sync unless we need to apply the navmesh offset impulse
                // or the nav system isn't updating positions.
                if (navAgent != null &&
                    !navAgent.Capabilities.SupportsNavmeshOffsetImpulse &&
                    !hasImpulse &&
                    navAgent.UpdatePosition)
                {
                    continue;
                }

                if (navAgent == null || !navAgent.Enabled)
                {
                    // Dead agents with active impulse (knockback) still need gravity integration
                    // even when nav is disabled (ragdoll death, component cleanup, etc.).
                    // Without this, agents killed mid-air float forever.
                    if (hasImpulse && agent.isDead && agent.transform != null)
                    {
                        ApplyDeadAgentImpulseGravity(agent, dt, currentTime);
                    }
                    continue;
                }

                // Skip full nav sync during combat animations, but still allow navmesh offset impulses
                bool inCombatAnim = agent.combatState == CrowdCombatState.Attacking ||
                                     agent.combatState == CrowdCombatState.Blocking ||
                                     agent.combatState == CrowdCombatState.Hit ||
                                     agent.combatState == CrowdCombatState.Dead ||
                                     agent.isDead;
                hasImpulse = agent.hasNavmeshOffsetImpulse;
                if (inCombatAnim && !hasImpulse)
                {
                    continue;
                }

                // PERFORMANCE: Time-based throttling for offscreen agents
                // Visible agents always sync, offscreen agents only sync periodically
                // Agents with active impulse (knockback) MUST sync every frame so gravity
                // integrates correctly — throttling them causes near-zero effective gravity
                // because dt stays at Time.deltaTime while updates are skipped.
                bool forceSync = agent.isVisible || hasImpulse;
                if (!forceSync && offscreenSyncInterval > 0f)
                {
                    if (currentTime - agent.lastNavSyncTime < offscreenSyncInterval)
                        continue; // Skip this offscreen agent - not yet time to sync
                }

                Transform transform = agent.transform;
                bool wasAirborne = agent.isAirborne;

                // Apply optional visual navmesh offset impulse (parabolic push with gravity)
                Vector3 visualOffset = Vector3.zero;
                
                // For agents with impulse, use the stored ground position to prevent accumulation
                // For normal agents, use navAgent.nextPosition as usual
                Vector3 basePosition;
                if (agent.hasNavmeshOffsetImpulse)
                {
                    // Use stored ground position - this prevents position accumulation bug
                    basePosition = agent.navmeshOffsetGroundPosition;
                    
                    float gravity = agent.navmeshOffsetGravity > 0f ? agent.navmeshOffsetGravity : 25f;
                    float drag = Mathf.Max(0f, agent.navmeshOffsetDrag);
                    float duration = agent.navmeshOffsetDuration > 0f ? agent.navmeshOffsetDuration : (agent.navmeshOffsetEndTime - currentTime);
                    float timeRemaining = agent.navmeshOffsetEndTime - currentTime;
                    
                    bool debugAirPhysics = agent.enemyCrowd != null && agent.enemyCrowd.enableAirborneDebugLogs;
                    Vector3 velBefore = agent.navmeshOffsetVelocity;
                    Vector3 offsetBefore = agent.navmeshOffset;

                    // Integrate velocity with gravity (always apply, even after expired - this is what pulls the agent down)
                    agent.navmeshOffsetVelocity.y -= gravity * dt;

                    // Horizontal damping (only while duration is active)
                    if (timeRemaining > 0f)
                    {
                        agent.navmeshOffsetVelocity.x = Mathf.Lerp(agent.navmeshOffsetVelocity.x, 0f, drag * dt);
                        agent.navmeshOffsetVelocity.z = Mathf.Lerp(agent.navmeshOffsetVelocity.z, 0f, drag * dt);
                    }

                    // Apply displacement - vertical always uses full velocity, horizontal fades over duration
                    float fade = (duration > 0f && timeRemaining > 0f) ? Mathf.Clamp01(timeRemaining / duration) : 0f;
                    float horizontalStepX = agent.navmeshOffsetVelocity.x * fade * dt;
                    float horizontalStepZ = agent.navmeshOffsetVelocity.z * fade * dt;
                    float verticalStep = agent.navmeshOffsetVelocity.y * dt;
                    
                    // Horizontal movement updates the ground position so NavMeshAgent capsule follows
                    // Only Y goes into the visual offset (the "lift" above ground)
                    agent.navmeshOffsetGroundPosition.x += horizontalStepX;
                    agent.navmeshOffsetGroundPosition.z += horizontalStepZ;
                    agent.navmeshOffset.y += verticalStep;

                    // Clamp to ground plane to avoid sinking below NavMesh
                    bool hitGround = false;
                    if (agent.navmeshOffset.y < 0f)
                    {
                        agent.navmeshOffset.y = 0f;
                        if (agent.navmeshOffsetVelocity.y < 0f)
                        {
                            agent.navmeshOffsetVelocity.y = 0f;
                            hitGround = true;
                        }
                    }

                    visualOffset = agent.navmeshOffset;

                    bool expired = currentTime >= agent.navmeshOffsetEndTime;
                    if (expired && timeRemaining <= 0f)
                    {
                        // Stop horizontal push when expired, but keep gravity pulling down
                        agent.navmeshOffsetVelocity.x = 0f;
                        agent.navmeshOffsetVelocity.z = 0f;
                    }

                    bool grounded = visualOffset.y <= 0.001f && agent.navmeshOffsetVelocity.y <= 0f;
                    bool settled = grounded && Mathf.Abs(agent.navmeshOffsetVelocity.x) < 0.01f && Mathf.Abs(agent.navmeshOffsetVelocity.z) < 0.01f;
                    
                    if (debugAirPhysics)
                    {
                        Debug.Log($"[NavmeshImpulse] agent {agentIndex} dt={dt:F4} gravity={gravity} velBefore={velBefore} velAfter={agent.navmeshOffsetVelocity} offsetBefore={offsetBefore} offsetAfter={agent.navmeshOffset} fade={fade:F2} expired={expired} grounded={grounded} settled={settled} hitGround={hitGround}");
                    }
                    
                    if (settled)
                    {
                        if (NavmeshImpulseDebug)
                        {
                            Debug.Log($"[NavmeshImpulse] settle agent {agentIndex} offset={visualOffset} vel={agent.navmeshOffsetVelocity}");
                        }
                        bool pendingNavDisable = agent.disableNavMeshAfterImpulse;
                        agent.hasNavmeshOffsetImpulse = false;
                        agent.navmeshOffset = Vector3.zero;
                        agent.navmeshOffsetVelocity = Vector3.zero;
                        agent.navmeshOffsetDuration = 0f;

                        // pendingNavDisable intentionally unused for non-ragdoll deaths; nav stays enabled.
                    }
                }
                else
                {
                    // No impulse active - use normal position from navigation agent
                    basePosition = inCombatAnim ? transform.position : navAgent.Position;
                }

                bool debugAir = agent.enemyCrowd != null && agent.enemyCrowd.enableAirborneDebugLogs;
                bool airborneNow = agent.hasNavmeshOffsetImpulse && visualOffset.y > 0.05f;
                if (airborneNow)
                {
                    if (!wasAirborne && debugAir)
                    {
                        Debug.Log($"[Airborne] Agent {agentIndex} entering falling; offset={visualOffset} vel={agent.navmeshOffsetVelocity} endTime={agent.navmeshOffsetEndTime:F2} now={currentTime:F2}");
                    }
                    agent.isAirborne = true;
                    agent.landingRequested = false;
                }
                else if (wasAirborne)
                {
                    if (debugAir)
                    {
                        Debug.Log($"[Airborne] Agent {agentIndex} hit ground; landing requested. vel={agent.navmeshOffsetVelocity} now={currentTime:F2}");
                    }
                    agent.isAirborne = false;
                    agent.landingRequested = true;
                    if (agent.liftImpulseActive)
                    {
                        agent.liftGetUpPending = true; // defer get-up until after landing clip
                    }
                    agent.landingClipEndTime = 0f;
                }
                else
                {
                    agent.isAirborne = false;
                }

                // Keep navigation agent at ground level for pathfinding; only apply visual offset to transform
                Vector3 finalPosition = basePosition + visualOffset;

                // ── Flight height offset ──
                // Legacy flying archetypes use flightCurrentHeight as a visual Y offset above ground.
                // Flight3D already owns its altitude directly in its navigation adapter, so applying
                // flightCurrentHeight here would double-stack altitude during impulse fallback.
                bool useLegacyFlightHeightOffset = navAgent.NavigationType != NavigationType.Flight3D;
                if (useLegacyFlightHeightOffset && (agent.isFlying || agent.flightCurrentHeight > 0.01f))
                {
                    float target = agent.flightTargetHeight;
                    EnemyCrowd flightCrowd = agent.enemyCrowd;
                    float speed;
                    if (agent.flightCurrentHeight < target)
                        speed = flightCrowd != null ? flightCrowd.flightAscendSpeed : 5f;
                    else
                        speed = flightCrowd != null ? flightCrowd.flightDescendSpeed : 8f;

                    agent.flightCurrentHeight = Mathf.MoveTowards(agent.flightCurrentHeight, target, speed * dt);
                    finalPosition.y += agent.flightCurrentHeight;

                    // ── Flight idle bobbing (buoyancy) ──
                    // Apply a subtle sine-wave Y offset when the agent has reached cruising altitude.
                    // Phase is derived from agent index so each agent bobs independently.
                    if (flightCrowd != null && flightCrowd.flightBobAmplitude > 0f &&
                        Mathf.Approximately(agent.flightCurrentHeight, target) && target > 0.01f)
                    {
                        float phase = agent.index * 2.3917f; // prime-ish multiplier for varied phase
                        finalPosition.y += Mathf.Sin(Time.time * flightCrowd.flightBobFrequency * 6.2831853f + phase)
                                           * flightCrowd.flightBobAmplitude;
                    }

                    // Force GPU Instancer to read updated transform while height is changing
                    if (agent.prefabComponent != null && agent.prefabComponent.registeredManager != null)
                    {
                        agent.prefabComponent.registeredManager.UpdateTransformData(agent.prefabComponent);
                    }
                }

                transform.position = finalPosition;
                
                // Navigation agent stays at ground level - don't include visual offset in pathfinding position.
                // For move-based backends, avoid expensive Teleport calls by not writing Position every frame.
                if (navAgent.Capabilities.SupportsNavmeshOffsetImpulse)
                {
                    navAgent.Position = basePosition;
                }
                
                // Force GPU Instancer to read updated transform when impulse is active
                if (agent.hasNavmeshOffsetImpulse && agent.prefabComponent != null && agent.prefabComponent.registeredManager != null)
                {
                    // Only force a GPUI transform push when we are manually driving positions.
                    if (navAgent.Capabilities.SupportsNavmeshOffsetImpulse || !navAgent.UpdatePosition)
                    {
                        agent.prefabComponent.registeredManager.UpdateTransformData(agent.prefabComponent);
                    }
                    
                    bool debugPos = agent.enemyCrowd != null && agent.enemyCrowd.enableAirborneDebugLogs;
                    if (debugPos)
                    {
                        Debug.Log($"[Position] agent {agentIndex} base={basePosition:F3} offset={visualOffset:F3} final={finalPosition:F3}");
                    }
                }

                // Rotate agent to face movement direction (only when not locked in combat anim)
                if (!inCombatAnim)
                {
                    Vector3 velocity = navAgent.Velocity;
                    if (velocity.sqrMagnitude > 0.0001f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationLerp);
                    }
                }

                // Update last sync time for throttling
                agent.lastNavSyncTime = currentTime;
            }
        }

        /// <summary>
        /// Stripped-down gravity-only path for dead agents whose nav agent is disabled
        /// but still have an active navmesh offset impulse (knockback).
        /// Integrates gravity so the corpse falls to the ground instead of floating.
        /// </summary>
        private static void ApplyDeadAgentImpulseGravity(CrowdAgent agent, float dt, float currentTime)
        {
            float gravity = agent.navmeshOffsetGravity > 0f ? agent.navmeshOffsetGravity : 25f;
            float drag = Mathf.Max(0f, agent.navmeshOffsetDrag);
            float duration = agent.navmeshOffsetDuration > 0f ? agent.navmeshOffsetDuration : (agent.navmeshOffsetEndTime - currentTime);
            float timeRemaining = agent.navmeshOffsetEndTime - currentTime;

            // Integrate gravity
            agent.navmeshOffsetVelocity.y -= gravity * dt;

            // Horizontal damping
            if (timeRemaining > 0f)
            {
                agent.navmeshOffsetVelocity.x = Mathf.Lerp(agent.navmeshOffsetVelocity.x, 0f, drag * dt);
                agent.navmeshOffsetVelocity.z = Mathf.Lerp(agent.navmeshOffsetVelocity.z, 0f, drag * dt);
            }

            float fade = (duration > 0f && timeRemaining > 0f) ? Mathf.Clamp01(timeRemaining / duration) : 0f;
            agent.navmeshOffsetGroundPosition.x += agent.navmeshOffsetVelocity.x * fade * dt;
            agent.navmeshOffsetGroundPosition.z += agent.navmeshOffsetVelocity.z * fade * dt;
            agent.navmeshOffset.y += agent.navmeshOffsetVelocity.y * dt;

            // Clamp to ground
            if (agent.navmeshOffset.y < 0f)
            {
                agent.navmeshOffset.y = 0f;
                if (agent.navmeshOffsetVelocity.y < 0f)
                {
                    agent.navmeshOffsetVelocity.y = 0f;
                }
            }

            // Stop horizontal push when expired
            if (currentTime >= agent.navmeshOffsetEndTime)
            {
                agent.navmeshOffsetVelocity.x = 0f;
                agent.navmeshOffsetVelocity.z = 0f;
            }

            // Check if settled
            bool grounded = agent.navmeshOffset.y <= 0.001f && agent.navmeshOffsetVelocity.y <= 0f;
            bool settled = grounded &&
                Mathf.Abs(agent.navmeshOffsetVelocity.x) < 0.01f &&
                Mathf.Abs(agent.navmeshOffsetVelocity.z) < 0.01f;

            // Apply position to transform (nav agent is disabled, so we drive the transform directly)
            Vector3 finalPosition = agent.navmeshOffsetGroundPosition + agent.navmeshOffset;
            agent.transform.position = finalPosition;

            // Force GPU Instancer update
            if (agent.prefabComponent != null && agent.prefabComponent.registeredManager != null)
                agent.prefabComponent.registeredManager.UpdateTransformData(agent.prefabComponent);

            if (settled)
            {
                agent.hasNavmeshOffsetImpulse = false;
                agent.navmeshOffset = Vector3.zero;
                agent.navmeshOffsetVelocity = Vector3.zero;
                agent.navmeshOffsetDuration = 0f;
                agent.isAirborne = false;
            }
            else
            {
                agent.isAirborne = agent.navmeshOffset.y > 0.05f;
            }
        }

        #endregion

        private static void UpdateAstarKnockback(CrowdAgent agent, INavigationAgent navAgent, float currentTime, float dt)
        {
            if (agent == null || navAgent == null)
                return;

            if (agent.isDead || !navAgent.Enabled)
            {
                ResetMoveBasedKnockback(agent, navAgent, restoreNavigationStop: false);
                return;
            }

            if (!navAgent.IsStopped)
                navAgent.IsStopped = true;

            bool horizontalActive = currentTime < agent.astarKnockbackEndTime;

            Vector3 velocity = agent.astarKnockbackVelocity;
            velocity.y = 0f;

            if (agent.astarKnockbackDrag > 0f)
                velocity = Vector3.Lerp(velocity, Vector3.zero, agent.astarKnockbackDrag * dt);

            if (horizontalActive)
            {
                Vector3 delta = velocity * dt;
                if (delta.sqrMagnitude > 0.000001f)
                    navAgent.Move(delta);
            }

            agent.astarKnockbackVelocity = velocity;

            // Visual-only vertical lift for move-based knockback backends (does not affect nav position).
            float previousVisualOffsetY = agent.astarVisualOffsetY;
            bool verticalActive = Mathf.Abs(agent.astarKnockbackVerticalVelocity) > 0.001f || agent.astarVisualOffsetY > 0.001f;
            if (verticalActive)
            {
                float gravity = agent.astarKnockbackGravity > 0f ? agent.astarKnockbackGravity : 25f;
                agent.astarKnockbackVerticalVelocity -= gravity * dt;
                agent.astarVisualOffsetY += agent.astarKnockbackVerticalVelocity * dt;

                if (agent.astarVisualOffsetY <= 0f)
                {
                    agent.astarVisualOffsetY = 0f;
                    if (agent.astarKnockbackVerticalVelocity < 0f)
                        agent.astarKnockbackVerticalVelocity = 0f;
                }
            }

            // For backends where the controller owns transform sync (for example FlowField),
            // apply the visual position directly while the knockback impulse is active.
            if (!navAgent.UpdatePosition || verticalActive)
            {
                var t = agent.transform;
                if (t != null)
                {
                    Vector3 basePos = navAgent.Position;
                    if (navAgent.NavigationType == NavigationType.Flight3D && previousVisualOffsetY > 0f)
                    {
                        // Flight3D.Position reads/writes transform directly.
                        // Remove previously applied visual lift before adding the new frame's offset,
                        // otherwise vertical knockback accumulates into runaway altitude.
                        basePos.y -= previousVisualOffsetY;
                    }
                    t.position = basePos + Vector3.up * agent.astarVisualOffsetY;
                }
            }

            if (!horizontalActive && agent.astarVisualOffsetY <= 0.001f)
            {
                ResetMoveBasedKnockback(agent, navAgent, restoreNavigationStop: true);
            }
        }

        private static void ResetMoveBasedKnockback(CrowdAgent agent, INavigationAgent navAgent, bool restoreNavigationStop)
        {
            if (agent == null)
                return;

            if (restoreNavigationStop &&
                navAgent != null &&
                navAgent.Enabled &&
                agent.moveBasedKnockbackForcedStop)
            {
                navAgent.IsStopped = agent.moveBasedKnockbackWasStoppedBeforeImpulse;
            }

            agent.hasAstarKnockbackImpulse = false;
            agent.astarKnockbackVelocity = Vector3.zero;
            agent.astarKnockbackVerticalVelocity = 0f;
            agent.astarVisualOffsetY = 0f;
            agent.astarKnockbackEndTime = 0f;
            agent.moveBasedKnockbackForcedStop = false;
            agent.moveBasedKnockbackWasStoppedBeforeImpulse = false;
        }

        #region Formation Reformation

        /// <summary>
        /// Determines if formation reformation should be used for this agent.
        /// Returns true if:
        /// - Formation reformation is enabled for the faction
        /// - Agent is beyond the formation dissolve distance
        /// - Agent is not already in close combat (not attacking, not in combat override)
        /// - Agent does NOT have an attack target (combat has priority over reformation)
        /// </summary>
        private bool ShouldUseFormationReformation(CrowdAgent agent, float distanceToTarget)
        {
            if (agent == null)
                return false;
            
            // COMBAT PRIORITY: If agent has an attack target, never use reformation
            // This ensures agents pursuing targets go directly to them, not to formation positions
            // Validate that hasAttackTargetPosition is backed by a real target — the damage handler
            // can set this flag for directional animation without assigning an actual target.
            bool hasRealTarget = (agent.hasAttackTargetPosition && 
                (agent.attackTargetAgentIndex >= 0 || agent.attackTargetTransform != null)) || 
                agent.attackTargetAgentIndex >= 0;
            
            if (hasRealTarget)
            {
                return false;
            }
            
            // Clear phantom hasAttackTargetPosition if it was set without a valid target
            if (agent.hasAttackTargetPosition && !hasRealTarget)
            {
                agent.hasAttackTargetPosition = false;
            }
            
            // Check if reformation is enabled for this agent's faction
            EnemyCrowd faction = agent.enemyCrowd;
            bool reformationEnabled = faction != null ? faction.enableFormationReformation : _combatSettings.enableFormationReformation;
            
            if (!reformationEnabled)
                return false;
            
            // If agent is in close combat state, don't use reformation
            if (agent.combatState == CrowdCombatState.Attacking || 
                agent.combatState == CrowdCombatState.Blocking ||
                agent.combatState == CrowdCombatState.Hit)
                return false;
            
            // Get dissolve distance - if closer than this, no reformation
            float dissolveDistance = GetFormationDissolveDistance(agent);
            if (distanceToTarget <= dissolveDistance)
                return false;
            
            // Check if combat is already engaged (any agents from this faction attacking)
            // If combat is engaged, only use reformation if agent is REALLY far (beyond reformation distance)
            bool combatEngaged = IsFactionInCombat(agent.enemyCrowdIndex);
            float reformationDistance = GetReformationDistance(agent);
            
            if (combatEngaged)
            {
                // Combat is happening - only very distant agents reform, closer ones keep pushing toward target
                // This prevents agents from standing around when combat is active
                return distanceToTarget >= reformationDistance;
            }
            
            // No combat yet - use reformation for agents beyond dissolve distance
            return true;
        }
        
        /// <summary>
        /// Checks if any agent in the specified faction is currently engaged in combat.
        /// Uses per-frame caching to avoid expensive iteration when called multiple times per frame.
        /// </summary>
        private bool IsFactionInCombat(int factionIndex)
        {
            if (factionIndex < 0)
                return false;
            
            // Check cache first (invalidate each frame)
            int currentFrame = Time.frameCount;
            if (_factionCombatStateCacheFrame != currentFrame)
            {
                _factionCombatStateCache.Clear();
                _factionCombatStateCacheFrame = currentFrame;
            }
            
            if (_factionCombatStateCache.TryGetValue(factionIndex, out bool cachedResult))
                return cachedResult;
            
            // Calculate and cache the result
            bool isInCombat = false;
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.enemyCrowdIndex != factionIndex)
                    continue;
                
                // Check if this agent is in an active combat state
                if (agent.combatState == CrowdCombatState.Attacking ||
                    agent.combatState == CrowdCombatState.Blocking ||
                    agent.combatState == CrowdCombatState.Hit ||
                    agent.combatState == CrowdCombatState.Approach)
                {
                    isInCombat = true;
                    break;
                }
            }
            
            _factionCombatStateCache[factionIndex] = isInCombat;
            return isInCombat;
        }

        /// <summary>
        /// Calculates whether agent should move in formation or directly to target based on distance.
        /// When far from target (beyond reformationDistance), agents maintain formation.
        /// When close (within formationDissolveDistance), agents move directly to target.
        /// </summary>
        private Vector3 CalculateFormationReformationPosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex)
        {
            if (agent?.navigationAgent == null)
                return targetPosition;

            Vector3 agentPosition = agent.WorldPosition;
            float distanceToTarget = Vector3.Distance(agentPosition, targetPosition);

            // Track distance change to determine if target is retreating or approaching
            float deltaTime = Time.deltaTime;
            if (deltaTime > 0.001f)
            {
                agent.targetDistanceChangeRate = (distanceToTarget - agent.lastTargetDistance) / deltaTime;
                agent.lastTargetDistance = distanceToTarget;
            }

            // Agent is close - dissolve formation, move directly to target
            // Phase 2: Use agent-specific dissolve distance
            if (distanceToTarget <= GetFormationDissolveDistance(agent))
            {
                return targetPosition;
            }

            // CRITICAL: Only form up if target is RETREATING (distance increasing)
            // If target is approaching (distance decreasing), agents should engage directly
            bool targetRetreating = agent.targetDistanceChangeRate > 0.5f; // Target moving away at > 0.5m/s

            // Agent is far - maintain formation while chasing, BUT only if target is retreating
            // Phase 2: Use agent-specific reformation distance
            if (distanceToTarget >= GetReformationDistance(agent) && targetRetreating)
            {
                // Get faction to determine formation type
                EnemyCrowd faction = null;
                if (agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
                {
                    faction = _enemyCrowds[agent.enemyCrowdIndex];
                }

                // Count agents in this faction that are chasing the same target
                // ONLY count agents that are NOT currently attacking (in formation mode)
                // FIX: Count formation index properly by iterating through qualifying agents
                int factionAgentCount = 0;
                int agentIndexInFormation = -1; // Will be set when we find this agent
                bool foundThisAgent = false;
                
                for (int i = 0; i < _agents.Count; i++)
                {
                    CrowdAgent otherAgent = _agents[i];
                    if (otherAgent == null || otherAgent.isDead || otherAgent.enemyCrowdIndex != agent.enemyCrowdIndex)
                        continue;

                    if (!otherAgent.hasAttackTargetPosition)
                        continue;

                    // Skip agents that are currently attacking - they're in combat, not formation
                    if (otherAgent.combatState == CrowdCombatState.Attacking)
                        continue;

                    // Check if chasing the same target (within 5 units)
                    if (Vector3.Distance(otherAgent.attackTargetPosition, targetPosition) < 5f)
                    {
                        // If this is the agent we're calculating for, record its formation index
                        if (i == agentIndex)
                        {
                            agentIndexInFormation = factionAgentCount;
                            foundThisAgent = true;
                        }
                        factionAgentCount++;
                    }
                }
                
                // Safety check: if we didn't find this agent in the formation, use index 0
                if (!foundThisAgent)
                    agentIndexInFormation = 0;

                // Calculate formation position around the target
                FormationType formationType = faction != null && faction.formationTypeOverride != FormationType.Random 
                    ? faction.formationTypeOverride 
                    : FormationType.Circle; // Default to circle for combat formations



                Vector3 formationPosition = _formationSystem.CalculateFormationPosition(
                    agentIndexInFormation,
                    Mathf.Max(1, factionAgentCount),
                    targetPosition,
                    _combatSettings.reformationSize,
                    formationType,
                    _combatSettings.reformationSpacing,
                    _combatSettings.reformationThickness,
                    agent.navigationAgent
                );

                return formationPosition;
            }

            // In transition zone - blend between formation and direct approach
            // BUT only if target is retreating
            if (!targetRetreating)
            {
                return targetPosition; // Target approaching, engage directly
            }

            // Phase 2: Use agent-specific distances for blending
            float blendFactor = Mathf.InverseLerp(
                GetFormationDissolveDistance(agent),
                GetReformationDistance(agent),
                distanceToTarget
            );

            if (blendFactor > 0.01f)
            {
                // Get faction formation
                EnemyCrowd faction = null;
                if (agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
                {
                    faction = _enemyCrowds[agent.enemyCrowdIndex];
                }

                // FIX: Count formation index properly by iterating through qualifying agents
                int factionAgentCount = 0;
                int agentIndexInFormation = -1; // Will be set when we find this agent
                bool foundThisAgent = false;
                
                for (int i = 0; i < _agents.Count; i++)
                {
                    CrowdAgent otherAgent = _agents[i];
                    if (otherAgent == null || otherAgent.isDead || otherAgent.enemyCrowdIndex != agent.enemyCrowdIndex)
                        continue;

                    if (!otherAgent.hasAttackTargetPosition)
                        continue;

                    // Skip agents that are currently attacking
                    if (otherAgent.combatState == CrowdCombatState.Attacking)
                        continue;

                    if (Vector3.Distance(otherAgent.attackTargetPosition, targetPosition) < 5f)
                    {
                        // If this is the agent we're calculating for, record its formation index
                        if (i == agentIndex)
                        {
                            agentIndexInFormation = factionAgentCount;
                            foundThisAgent = true;
                        }
                        factionAgentCount++;
                    }
                }
                
                // Safety check: if we didn't find this agent in the formation, use index 0
                if (!foundThisAgent)
                    agentIndexInFormation = 0;

                FormationType formationType = faction != null && faction.formationTypeOverride != FormationType.Random 
                    ? faction.formationTypeOverride 
                    : FormationType.Circle;

                // Phase 2: Use agent-specific reformation settings
                Vector3 formationPosition = _formationSystem.CalculateFormationPosition(
                    agentIndexInFormation,
                    Mathf.Max(1, factionAgentCount),
                    targetPosition,
                    GetReformationSize(agent),
                    formationType,
                    GetReformationSpacing(agent),
                    GetReformationThickness(agent),
                    agent.navigationAgent
                );

                // Blend between formation position and direct target
                return Vector3.Lerp(targetPosition, formationPosition, blendFactor);
            }

            return targetPosition;
        }

        #endregion
        
        #region Phase 2: Settings Helpers (EnemyCrowd as single source of truth)
        
        /// <summary>
        /// Gets destination spread for an agent (prefers agent's EnemyCrowd settings, falls back to global).
        /// </summary>
        private float GetDestinationSpread(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.destinationSpread : _settings.destinationSpread;
        }
        
        /// <summary>
        /// Gets throttle setting for an agent.
        /// </summary>
        private bool GetThrottleDestinationUpdates(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.throttleDestinationUpdates : _settings.throttleDestinationUpdates;
        }
        
        /// <summary>
        /// Gets max destination updates per frame for an agent.
        /// </summary>
        private int GetMaxDestinationUpdatesPerFrame(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.maxDestinationUpdatesPerFrame : _settings.maxDestinationUpdatesPerFrame;
        }
        
        private float GetReformationDistance(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.reformationDistance : _combatSettings.reformationDistance;
        }
        
        private float GetFormationDissolveDistance(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.formationDissolveDistance : _combatSettings.formationDissolveDistance;
        }
        
        private float GetReformationSize(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.reformationSize : _combatSettings.reformationSize;
        }
        
        private float GetReformationSpacing(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.reformationSpacing : _combatSettings.reformationSpacing;
        }
        
        private float GetReformationThickness(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.reformationThickness : _combatSettings.reformationThickness;
        }
        
        #endregion
    }
}
