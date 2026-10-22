using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime.Navigation.Climbing
{
    /// <summary>
    /// Handles custom traversal of NavMeshLinks for climbing.
    /// This is a CENTRAL CONTROLLER - only ONE is needed in the scene.
    /// 
    /// This component intercepts off-mesh link traversal and provides:
    /// - Custom movement along the climb path
    /// - Animation integration via NavMeshLinkCrowdAnimationHandler (optional add-on)
    /// - Multiple agents traversing simultaneously
    /// - Proper state management without disabling NavMeshAgent
    /// 
    /// Animation clips are sourced from each agent's EnemyCrowd.climbingAnimationConfig.
    /// Add NavMeshLinkCrowdAnimationHandler component for GPU Instancer Pro animation support.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Climbing/NavMesh Link Traversal Handler")]
    public class NavMeshLinkTraversalHandler : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Reference to the crowd controller for animation integration")]
        [SerializeField] private EnemyMassesCrowdController crowdController;
        
        [Header("Traversal Settings")]
        [Tooltip("Whether to auto-complete link traversal (true) or require manual completion (false)")]
        [SerializeField] private bool autoCompleteTraversal = true;
        
        [Tooltip("Speed multiplier for traversal time (higher = faster climbing)")]
        [SerializeField] [Range(0.5f, 3f)] private float speedMultiplier = 1f;
        
        [Header("Horde Mode (Proximity Detection)")]
        [Tooltip("Enable proximity-based climb detection. This bypasses Unity's link occupancy system to allow true simultaneous climbing (World War Z zombie horde style).")]
        [SerializeField] private bool useProximityDetection = true;
        
        [Tooltip("Distance from link start point at which agents will begin climbing (bypasses Unity's link queue)")]
        [SerializeField] [Range(0.5f, 5f)] private float proximityDetectionRadius = 1.5f;
        
        [Tooltip("How often to check for agents near climb links (seconds). Lower = more responsive but higher CPU cost.")]
        [SerializeField] [Range(0.05f, 0.5f)] private float proximityCheckInterval = 0.1f;
        
        [Tooltip("Cooldown after completing a climb before an agent can start another proximity-based climb. Prevents yo-yo climbing.")]
        [SerializeField] [Range(0.5f, 5f)] private float climbCooldown = 2f;

        [Header("A* Proximity Fallback")]
        [Tooltip("If true, allow A* agents to start climbing when very close to the link start even if the path check fails.")]
        [SerializeField] private bool allowAStarProximityFallback = true;

        [Tooltip("Minimum dot product between agent desired velocity and link direction required for A* fallback climb.")]
        [SerializeField] [Range(-1f, 1f)] private float aStarDirectionDotThreshold = 0.1f;
        
        private float _lastProximityCheck = 0f;
        /// <summary>Tracks when each agent last finished a climb (for cooldown)</summary>
        private Dictionary<int, float> _agentClimbCooldowns = new Dictionary<int, float>();
        
        [Header("Animation (Mecanim Fallback)")]
        [Tooltip("Use Mecanim animator parameters from EnemyCrowd.climbingAnimationConfig for agents without GPUICrowdAnimator")]
        [SerializeField] private bool useMecanimFallback = true;
        
        [Header("Debug")]
        [SerializeField] private bool debugMode = false;
        
        // Track agents on links
        private Dictionary<int, AgentLinkTraversalState> _agentStates = new Dictionary<int, AgentLinkTraversalState>();
        private List<NavMeshLinkClimbable> _registeredClimbables = new List<NavMeshLinkClimbable>();
        
        /// <summary>State for an agent traversing a link</summary>
        private class AgentLinkTraversalState
        {
            public CrowdAgent agent;
            public INavigationAgent navigationAgent;
            public NavMeshLinkClimbable climbable;
            public NavMeshLink link;
            public Vector3 startPos;
            public Vector3 endPos;
            public float startTime;
            public float duration;
            public bool ascending;
            public bool completed;
            
            // Horde mode: position offset and speed variation
            public Vector3 positionOffset;
            public float speedMultiplier;
            /// <summary>Actual duration accounting for speed variation</summary>
            public float ActualDuration => speedMultiplier > 0 ? duration / speedMultiplier : duration;
            
            /// <summary>True if this climb was started via proximity detection (horde mode) rather than normal off-mesh link</summary>
            public bool startedViaProximity;
            /// <summary>Original NavMeshAgent.enabled state before we disabled it for horde climbing</summary>
            public bool wasNavMeshAgentEnabled;
            /// <summary>Original destination the agent was heading to before climbing (to restore after climb)</summary>
            public Vector3 originalDestination;
            /// <summary>Whether agent had a valid path before climbing</summary>
            public bool hadPathBeforeClimb;
            /// <summary>Original obstacle avoidance type before we disabled it for climbing</summary>
            public ObstacleAvoidanceType originalAvoidanceType;
            /// <summary>Original avoidance priority before we set it to lowest for climbing</summary>
            public int originalAvoidancePriority;

            /// <summary>Original stop state for non-NavMesh agents</summary>
            public bool wasStopped;
            /// <summary>Original UpdatePosition state for non-NavMesh agents</summary>
            public bool wasUpdatePosition;
            /// <summary>Original UpdateRotation state for non-NavMesh agents</summary>
            public bool wasUpdateRotation;
        }
        
        // Track which agents we've configured
        private HashSet<int> _configuredAgents = new HashSet<int>();
        
        private void Awake()
        {
            if (crowdController == null)
            {
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            }
        }
        
        private void Start()
        {
            // Find all NavMeshLinkClimbables in the scene
            _registeredClimbables.AddRange(FindObjectsByType<NavMeshLinkClimbable>(FindObjectsSortMode.None));
        }
        
        private void Update()
        {
            if (crowdController == null) return;
            
            int agentCount = crowdController.GetAgentCount();
            if (agentCount == 0) return;

            bool sawNonNavMeshAgents = false;
            
            // Check each agent for off-mesh link status
            for (int i = 0; i < agentCount; i++)
            {
                var agent = crowdController.GetAgent(i);
                if (agent == null || agent.isDead) continue;
                if (agent.navigationAgent == null || !agent.navigationAgent.Enabled) continue;

                var navMeshAgent = GetUnityNavMeshAgent(agent);
                if (navMeshAgent == null)
                {
                    sawNonNavMeshAgents = true;
                }
                else
                {
                    if (!navMeshAgent.isOnNavMesh) continue;
                }
                
                // Skip agents already climbing
                if (_agentStates.ContainsKey(agent.index)) continue;
                
                if (navMeshAgent != null)
                {
                    // CRITICAL: Disable auto-traverse so we can handle link traversal manually
                    // This allows us to control climb speed and play animations
                    ConfigureAgentForManualTraversal(agent);
                    
                    ProcessAgentLink(agent);
                }
            }
            
            // HORDE MODE: Proximity-based detection to bypass Unity's link occupancy blocking
            bool allowUnityNavMeshProximity = useProximityDetection;
            bool allowNonNavMeshProximity = sawNonNavMeshAgents;
            if ((allowUnityNavMeshProximity || allowNonNavMeshProximity) && Time.time - _lastProximityCheck >= proximityCheckInterval)
            {
                _lastProximityCheck = Time.time;
                CheckProximityBasedClimbing(allowUnityNavMeshProximity, allowNonNavMeshProximity);
            }
            
            // Update traversing agents
            UpdateTraversingAgents();
        }
        
        /// <summary>
        /// HORDE MODE: Check for agents near climb link start points and start their climb immediately.
        /// This bypasses Unity's NavMesh link occupancy system which normally prevents multiple agents on one link.
        /// Note: With overlapping links, this is often not needed as each agent gets their own link.
        /// </summary>
        private void CheckProximityBasedClimbing(bool allowUnityNavMeshAgents, bool allowNonNavMeshAgents)
        {
            int agentCount = crowdController.GetAgentCount();
            if (debugMode)
            {
                Debug.Log($"[CLIMB-PROX] Proximity check: agents={agentCount}, allowUnity={allowUnityNavMeshAgents}, allowNonUnity={allowNonNavMeshAgents}, climbables={_registeredClimbables.Count}");
            }
            
            foreach (var climbable in _registeredClimbables)
            {
                if (climbable == null) continue;
                // Proximity detection works with any climbable, but is most useful when links are scarce
                
                var links = climbable.GetClimbLinks();
                if (links == null || links.Count == 0) continue;
                
                foreach (var link in links)
                {
                    if (link == null) continue;
                    
                    // Get link start position in world space
                    Vector3 linkStart = link.start;
                    Vector3 linkEnd = link.end;
                    
                    // Check all agents for proximity to this link's start
                    for (int i = 0; i < agentCount; i++)
                    {
                        var agent = crowdController.GetAgent(i);
                        if (agent == null || agent.isDead) continue;
                        if (agent.navigationAgent == null) continue;

                        var unityNavMeshAgent = GetUnityNavMeshAgent(agent);
                        bool isUnityNavMeshAgent = unityNavMeshAgent != null;
                        if (isUnityNavMeshAgent && !allowUnityNavMeshAgents) continue;
                        if (!isUnityNavMeshAgent && !allowNonNavMeshAgents) continue;
                        
                        // Skip agents already climbing
                        if (_agentStates.ContainsKey(agent.index)) continue;
                        
                        // Skip agents already on an off-mesh link (let normal processing handle them)
                        if (isUnityNavMeshAgent && unityNavMeshAgent.isOnOffMeshLink) continue;
                        
                        // Skip agents on cooldown (just finished climbing - prevents yo-yo effect)
                        if (_agentClimbCooldowns.TryGetValue(agent.index, out float cooldownEnd) && Time.time < cooldownEnd)
                            continue;
                        
                        Vector3 agentPos = agent.WorldPosition;
                        float distToStart = Vector3.Distance(agentPos, linkStart);
                        float distToEnd = Vector3.Distance(agentPos, linkEnd);
                        
                        // Check if agent is near the START of the link (within proximity radius)
                        // and closer to start than end (so they climb in the right direction)
                        if (distToStart < proximityDetectionRadius && distToStart < distToEnd)
                        {
                            if (!IsLikelyMovingThroughLink(agent, linkStart, linkEnd))
                            {
                                bool allowFallback = !isUnityNavMeshAgent && allowAStarProximityFallback && IsAStarFallbackAllowed(agent, linkStart, linkEnd);
                                if (allowFallback)
                                {
                                    if (debugMode)
                                    {
                                        Debug.Log($"[CLIMB-PROX] A* agent {agent.index} using fallback climb. distStart={distToStart:F2} distEnd={distToEnd:F2}");
                                    }
                                    StartHordeClimb(agent, climbable, link, linkStart, linkEnd);
                                    continue;
                                }

                                if (debugMode && !isUnityNavMeshAgent)
                                {
                                    Debug.Log($"[CLIMB-PROX] A* agent {agent.index} near START but path check failed. distStart={distToStart:F2} distEnd={distToEnd:F2} dest={agent.navigationAgent?.Destination}");
                                }
                                continue;
                            }
                            
                            // Start horde climb!
                            StartHordeClimb(agent, climbable, link, linkStart, linkEnd);
                        }
                        // Also check if agent is near END (for climbing down)
                        else if (distToEnd < proximityDetectionRadius && distToEnd < distToStart)
                        {
                            if (!IsLikelyMovingThroughLink(agent, linkEnd, linkStart))
                            {
                                bool allowFallback = !isUnityNavMeshAgent && allowAStarProximityFallback && IsAStarFallbackAllowed(agent, linkEnd, linkStart);
                                if (allowFallback)
                                {
                                    if (debugMode)
                                    {
                                        Debug.Log($"[CLIMB-PROX] A* agent {agent.index} using fallback climb (reverse). distStart={distToStart:F2} distEnd={distToEnd:F2}");
                                    }
                                    StartHordeClimb(agent, climbable, link, linkEnd, linkStart);
                                    continue;
                                }

                                if (debugMode && !isUnityNavMeshAgent)
                                {
                                    Debug.Log($"[CLIMB-PROX] A* agent {agent.index} near END but path check failed. distStart={distToStart:F2} distEnd={distToEnd:F2} dest={agent.navigationAgent?.Destination}");
                                }
                                continue;
                            }
                            
                            // Start horde climb (descending)
                            StartHordeClimb(agent, climbable, link, linkEnd, linkStart);
                        }
                        else if (debugMode && !isUnityNavMeshAgent && (distToStart < proximityDetectionRadius || distToEnd < proximityDetectionRadius))
                        {
                            Debug.Log($"[CLIMB-PROX] A* agent {agent.index} near link but orientation mismatch. distStart={distToStart:F2} distEnd={distToEnd:F2}");
                        }
                    }
                }
            }
        }

        private static NavMeshAgent GetUnityNavMeshAgent(CrowdAgent agent)
        {
            return agent?.navigationAgent?.GetUnderlyingAgent<NavMeshAgent>();
        }

        private static NavMeshAgent GetUnityNavMeshAgent(INavigationAgent navigationAgent)
        {
            return navigationAgent?.GetUnderlyingAgent<NavMeshAgent>();
        }

        /// <summary>
        /// Heuristic check to see if the agent is likely to traverse the link.
        /// For NavMesh agents, uses path corners. For A* agents, compares destination proximity.
        /// </summary>
        private bool IsLikelyMovingThroughLink(CrowdAgent agent, Vector3 linkStart, Vector3 linkEnd)
        {
            var navMeshAgent = GetUnityNavMeshAgent(agent);
            if (navMeshAgent != null && navMeshAgent.hasPath && navMeshAgent.path.corners.Length > 1)
            {
                foreach (var corner in navMeshAgent.path.corners)
                {
                    if (Vector3.Distance(corner, linkEnd) < proximityDetectionRadius * 3f)
                    {
                        return true;
                    }
                }
                return false;
            }

            var navAgent = agent.navigationAgent;
            if (navAgent == null) return false;
            if (!navAgent.HasPath && !navAgent.PathPending) return false;

            Vector3 destination = navAgent.Destination;
            if (float.IsNaN(destination.x) || float.IsNaN(destination.y) || float.IsNaN(destination.z))
                return false;

            float distToEnd = Vector3.Distance(destination, linkEnd);
            float distToStart = Vector3.Distance(destination, linkStart);
            if (debugMode && navMeshAgent == null)
            {
                Debug.Log($"[CLIMB-PROX] A* agent {agent.index} path check: distToEnd={distToEnd:F2} distToStart={distToStart:F2} dest={destination}");
            }
            return distToEnd < distToStart;
        }

        private bool IsAStarFallbackAllowed(CrowdAgent agent, Vector3 linkStart, Vector3 linkEnd)
        {
            if (agent?.navigationAgent == null) return false;
            var desired = agent.navigationAgent.DesiredVelocity;
            if (desired.sqrMagnitude <= 0.0001f) return false;

            var linkDir = (linkEnd - linkStart).normalized;
            float dot = Vector3.Dot(desired.normalized, linkDir);
            if (debugMode)
            {
                Debug.Log($"[CLIMB-PROX] A* fallback check agent {agent.index}: dot={dot:F2} threshold={aStarDirectionDotThreshold:F2} desired={desired}");
            }
            return dot >= aStarDirectionDotThreshold;
        }
        
        /// <summary>
        /// Start a horde-mode climb for an agent detected via proximity.
        /// This bypasses Unity's NavMesh link system entirely.
        /// </summary>
        private void StartHordeClimb(CrowdAgent agent, NavMeshLinkClimbable climbable, NavMeshLinkClimbable.ClimbLinkInfo linkInfo, Vector3 startPos, Vector3 endPos)
        {
            bool ascending = endPos.y > startPos.y;
            float duration = climbable.TraversalTime / speedMultiplier;
            
            // Register with climbable FIRST - this calculates position offsets for horde mode
            var navMeshLink = linkInfo != null ? linkInfo.navMeshLink : null;
            climbable.RegisterTraversingAgent(agent.index, navMeshLink, startPos, endPos, ascending);
            
            // Get the traversal data which includes position offset and speed variation
            var traversalData = climbable.GetTraversalData(agent.index);
            
            Vector3 actualStartPos = traversalData != null ? traversalData.startPos : startPos;
            Vector3 actualEndPos = traversalData != null ? traversalData.endPos : endPos;
            float hordeSpeedMultiplier = traversalData != null ? traversalData.speedMultiplier : 1f;
            Vector3 posOffset = traversalData != null ? traversalData.positionOffset : Vector3.zero;
            
            var navAgent = agent.navigationAgent;
            var navMeshAgent = GetUnityNavMeshAgent(navAgent);
            bool wasEnabled = navMeshAgent != null ? navMeshAgent.enabled : navAgent != null && navAgent.Enabled;
            Vector3 originalDest = navAgent != null ? navAgent.Destination : Vector3.zero;
            bool hadPath = navAgent != null && (navAgent.HasPath || navAgent.PathPending);
            var originalAvoidance = navMeshAgent != null ? navMeshAgent.obstacleAvoidanceType : ObstacleAvoidanceType.NoObstacleAvoidance;
            int originalPriority = navMeshAgent != null ? navMeshAgent.avoidancePriority : 0;
            bool wasStopped = navAgent != null && navAgent.IsStopped;
            bool wasUpdatePosition = navAgent != null && navAgent.UpdatePosition;
            bool wasUpdateRotation = navAgent != null && navAgent.UpdateRotation;

            if (navAgent != null)
            {
                navAgent.IsStopped = true;
                if (navMeshAgent == null)
                {
                    // For A* agents, fully take over transform updates during climb
                    navAgent.UpdatePosition = false;
                    navAgent.UpdateRotation = false;
                }
            }

            if (navMeshAgent != null)
            {
                // Make climbing agent "invisible" to other agents' avoidance
                navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                navMeshAgent.avoidancePriority = 99; // Lowest priority = others ignore me
            }
            
            var state = new AgentLinkTraversalState
            {
                agent = agent,
                navigationAgent = navAgent,
                climbable = climbable,
                link = navMeshLink,
                startPos = actualStartPos,
                endPos = actualEndPos,
                startTime = Time.time,
                duration = duration,
                ascending = ascending,
                completed = false,
                positionOffset = posOffset,
                speedMultiplier = hordeSpeedMultiplier,
                startedViaProximity = true,
                wasNavMeshAgentEnabled = wasEnabled,
                originalDestination = originalDest,
                hadPathBeforeClimb = hadPath,
                originalAvoidanceType = originalAvoidance,
                originalAvoidancePriority = originalPriority,
                wasStopped = wasStopped,
                wasUpdatePosition = wasUpdatePosition,
                wasUpdateRotation = wasUpdateRotation
            };
            
            _agentStates[agent.index] = state;
            agent.isClimbing = true;
            
            // Snap agent to climb start position immediately
            agent.transform.position = actualStartPos;
            
            SetClimbAnimation(agent, ascending);
            
            if (debugMode)
            {
                Debug.Log($"[HORDE-CLIMB] Agent {agent.index} started HORDE climb {(ascending ? "UP" : "DOWN")} " +
                    $"via proximity detection. offset={posOffset}, speed={hordeSpeedMultiplier:F2}x, start={actualStartPos}, end={actualEndPos}");
            }
        }
        
        /// <summary>
        /// Configure an agent's NavMeshAgent to allow manual off-mesh link traversal.
        /// This is required for us to control climb speed and play animations.
        /// </summary>
        private void ConfigureAgentForManualTraversal(CrowdAgent agent)
        {
            // Only configure once per agent
            if (_configuredAgents.Contains(agent.index)) return;
            
            var navAgent = GetUnityNavMeshAgent(agent);
            if (navAgent != null)
            {
                // CRITICAL: Disable auto-traverse so Unity doesn't instantly teleport agents across links
                // With this false, agents will wait on the link start until we call CompleteOffMeshLink()
                navAgent.autoTraverseOffMeshLink = false;
                
                _configuredAgents.Add(agent.index);
            }
        }
        
        /// <summary>
        /// Process an agent's off-mesh link status.
        /// </summary>
        private void ProcessAgentLink(CrowdAgent agent)
        {
            var navAgent = GetUnityNavMeshAgent(agent);
            if (navAgent == null)
                return;
            
            // Check if agent is on an off-mesh link
            if (navAgent.isOnOffMeshLink)
            {
                // Is this a new link traversal?
                if (!_agentStates.ContainsKey(agent.index))
                {
                    StartLinkTraversal(agent);
                }
            }
            else
            {
                // Agent is not on link - clean up if they were
                if (_agentStates.ContainsKey(agent.index))
                {
                    CompleteLinkTraversal(agent.index);
                }
            }
        }
        
        /// <summary>
        /// Start traversing a NavMeshLink.
        /// </summary>
        private void StartLinkTraversal(CrowdAgent agent)
        {
            var navAgent = GetUnityNavMeshAgent(agent);
            if (navAgent == null)
                return;
            var linkData = navAgent.currentOffMeshLinkData;
            
            // Find which climbable owns this link
            NavMeshLinkClimbable climbable = null;
            NavMeshLink matchedLink = null;
            
            foreach (var c in _registeredClimbables)
            {
                var link = c.FindLinkForAgent(linkData);
                if (link != null)
                {
                    climbable = c;
                    matchedLink = link;
                    break;
                }
            }
            
            if (climbable == null)
            {
                // DIAGNOSTIC: Log why we couldn't find a matching climbable
                Debug.LogWarning($"[CLIMB-DEBUG] Agent {agent.index} on link but NO CLIMBABLE FOUND! " +
                    $"linkStart={linkData.startPos}, linkEnd={linkData.endPos}, " +
                    $"registeredClimbables={_registeredClimbables.Count}. " +
                    $"Check that NavMeshLinkClimbable is registered and link positions match.");
                
                // Log each climbable's link positions for comparison
                foreach (var c in _registeredClimbables)
                {
                    var links = c.GetNavMeshLinks();
                    if (links != null && links.Count > 0)
                    {
                        var firstLink = links[0];
                        if (firstLink != null)
                        {
                            // Use link's transform, not climbable's transform!
                            Vector3 cStart = firstLink.transform.TransformPoint(firstLink.startPoint);
                            Vector3 cEnd = firstLink.transform.TransformPoint(firstLink.endPoint);
                            float startDist = Vector3.Distance(linkData.startPos, cStart);
                            float endDist = Vector3.Distance(linkData.endPos, cEnd);
                            Debug.LogWarning($"[CLIMB-DEBUG]   Climbable '{c.name}' link[0]: start={cStart}, end={cEnd}, " +
                                $"startDist={startDist:F2}, endDist={endDist:F2}");
                        }
                    }
                }
                
                // Not one of our climbing links - let Unity handle it normally
                if (autoCompleteTraversal)
                {
                    navAgent.CompleteOffMeshLink();
                }
                return;
            }
            
            // Determine climb direction based on start/end positions
            bool ascending = linkData.endPos.y > linkData.startPos.y;
            
            // Calculate traversal time based on climbable settings and speed multiplier
            float duration = climbable.TraversalTime / speedMultiplier;
            
            // Register with climbable FIRST - this calculates position offsets for horde mode
            climbable.RegisterTraversingAgent(agent.index, matchedLink, linkData.startPos, linkData.endPos, ascending);
            
            // Get the traversal data which includes position offset and speed variation for horde mode
            var traversalData = climbable.GetTraversalData(agent.index);
            
            // Use offset positions if horde mode provided them
            Vector3 actualStartPos = traversalData != null ? traversalData.startPos : linkData.startPos;
            Vector3 actualEndPos = traversalData != null ? traversalData.endPos : linkData.endPos;
            float hordeSpeedMultiplier = traversalData != null ? traversalData.speedMultiplier : 1f;
            Vector3 posOffset = traversalData != null ? traversalData.positionOffset : Vector3.zero;
            
            // Make climbing agent "invisible" to other agents' avoidance:
            // 1. Disable this agent's own avoidance behavior
            // 2. Set priority to 99 (lowest) so OTHER agents don't try to avoid this one
            var originalAvoidance = navAgent.obstacleAvoidanceType;
            int originalPriority = navAgent.avoidancePriority;
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            navAgent.avoidancePriority = 99; // Lowest priority = others ignore me
            
            // Create traversal state
            var navigationAgent = agent.navigationAgent;
            var state = new AgentLinkTraversalState
            {
                agent = agent,
                navigationAgent = navigationAgent,
                climbable = climbable,
                link = matchedLink,
                startPos = actualStartPos,
                endPos = actualEndPos,
                startTime = Time.time,
                duration = duration,
                ascending = ascending,
                completed = false,
                positionOffset = posOffset,
                speedMultiplier = hordeSpeedMultiplier,
                startedViaProximity = false,
                wasNavMeshAgentEnabled = true,
                originalDestination = navAgent.destination,
                hadPathBeforeClimb = navAgent.hasPath,
                originalAvoidanceType = originalAvoidance,
                originalAvoidancePriority = originalPriority,
                wasStopped = navigationAgent != null && navigationAgent.IsStopped,
                wasUpdatePosition = navigationAgent != null && navigationAgent.UpdatePosition,
                wasUpdateRotation = navigationAgent != null && navigationAgent.UpdateRotation
            };
            
            _agentStates[agent.index] = state;
            
            // Mark agent as climbing - this stops locomotion animation from overriding climb animation
            agent.isClimbing = true;
            
            // Set climb animation
            SetClimbAnimation(agent, ascending);
        }
        
        /// <summary>
        /// Update all traversing agents.
        /// </summary>
        private void UpdateTraversingAgents()
        {
            List<int> completedAgents = null;
            
            foreach (var kvp in _agentStates)
            {
                var state = kvp.Value;
                if (state.completed) continue;
                
                var agent = state.agent;
                if (agent == null || agent.isDead || state.navigationAgent == null)
                {
                    // Agent died or was destroyed - mark for cleanup
                    completedAgents ??= new List<int>();
                    completedAgents.Add(kvp.Key);
                    continue;
                }
                
                // Calculate progress using ActualDuration which accounts for horde speed variation
                float elapsed = Time.time - state.startTime;
                float progress = Mathf.Clamp01(elapsed / state.ActualDuration);
                
                // Get styled position from climbable (applies climb style: Machine, Human, or Custom)
                float styledProgress = state.climbable.EvaluateClimbProgress(progress, agent.index);
                
                // Validate styledProgress to prevent NaN positions
                if (float.IsNaN(styledProgress) || float.IsInfinity(styledProgress))
                {
                    Debug.LogWarning($"[NavMeshLinkTraversalHandler] Invalid styledProgress ({styledProgress}) for agent {agent.index}, using raw progress {progress}");
                    styledProgress = progress;
                }
                
                Vector3 currentPos = Vector3.Lerp(state.startPos, state.endPos, styledProgress);
                
                // Validate position before applying
                if (float.IsNaN(currentPos.x) || float.IsNaN(currentPos.y) || float.IsNaN(currentPos.z))
                {
                    Debug.LogError($"[NavMeshLinkTraversalHandler] NaN position calculated for agent {agent.index}. " +
                        $"startPos={state.startPos}, endPos={state.endPos}, progress={progress}, styledProgress={styledProgress}");
                    continue; // Skip this frame to avoid corrupting agent position
                }
                
                // Move agent (NavMeshAgent position is still tracked but we override it)
                agent.transform.position = currentPos;
                
                // Make agent face the wall - determine which side they're climbing from
                // by checking which side of the wall the start position is on
                Vector3 wallForward = state.climbable.transform.forward;
                Vector3 wallCenter = state.climbable.transform.position;
                Vector3 startToWall = state.startPos - wallCenter;
                
                // Dot product: positive = start is on forward side, negative = start is on back side
                // Agent should face INTO the wall, so flip direction based on which side they started
                float side = Vector3.Dot(startToWall, wallForward);
                Vector3 faceDirection = side > 0 ? -wallForward : wallForward;
                agent.transform.rotation = Quaternion.LookRotation(faceDirection, Vector3.up);
                
                // Check if traversal is complete
                if (progress >= 1f)
                {
                    completedAgents ??= new List<int>();
                    completedAgents.Add(kvp.Key);
                    
                    // For normal (non-proximity) climbs, complete the off-mesh link
                    // CompleteOffMeshLink() handles positioning and path continuation automatically
                    var navMeshAgent = GetUnityNavMeshAgent(state.navigationAgent);
                    if (!state.startedViaProximity && navMeshAgent != null && navMeshAgent.isOnOffMeshLink)
                    {
                        navMeshAgent.CompleteOffMeshLink();
                        // No Warp needed - Unity's NavMesh system handles positioning after CompleteOffMeshLink()
                    }
                }
            }
            
            // Clean up completed agents
            if (completedAgents != null)
            {
                foreach (var agentIndex in completedAgents)
                {
                    CompleteLinkTraversal(agentIndex);
                }
            }
        }
        
        /// <summary>
        /// Complete and clean up a link traversal.
        /// </summary>
        private void CompleteLinkTraversal(int agentIndex)
        {
            if (_agentStates.TryGetValue(agentIndex, out var state))
            {
                state.completed = true;
                state.climbable?.UnregisterTraversingAgent(agentIndex);
                
                // Reset animation to idle/walk
                if (state.agent != null && !state.agent.isDead)
                {
                    // Clear climbing flag - allows locomotion animation to resume
                    state.agent.isClimbing = false;
                    ResetAgentAnimation(state.agent);
                    
                    // Restore obstacle avoidance so agent avoids other agents again
                    var navMeshAgent = GetUnityNavMeshAgent(state.navigationAgent);
                    if (navMeshAgent != null)
                    {
                        navMeshAgent.obstacleAvoidanceType = state.originalAvoidanceType;
                        navMeshAgent.avoidancePriority = state.originalAvoidancePriority;
                    }
                    
                    // For proximity-detected agents, re-enable agent and warp to end
                    if (state.startedViaProximity && state.navigationAgent != null)
                    {
                        state.navigationAgent.IsStopped = state.wasStopped;
                        state.navigationAgent.UpdatePosition = state.wasUpdatePosition;
                        state.navigationAgent.UpdateRotation = state.wasUpdateRotation;
                        state.navigationAgent.Warp(state.endPos);
                        
                        // Restore original destination so agent continues to their goal
                        if (!state.wasStopped && state.hadPathBeforeClimb && state.originalDestination != Vector3.zero)
                        {
                            state.navigationAgent.SetDestination(state.originalDestination);
                        }
                        
                        // Set cooldown to prevent immediate re-climbing (yo-yo effect)
                        _agentClimbCooldowns[agentIndex] = Time.time + climbCooldown;
                        
                        if (debugMode)
                        {
                            Debug.Log($"[HORDE-CLIMB] Agent {agentIndex} completed proximity climb, warped to {state.endPos}, restored destination {state.originalDestination}");
                        }
                    }
                }
                
                _agentStates.Remove(agentIndex);
            }
        }
        
        /// <summary>
        /// Get the climb animation config from the agent's EnemyCrowd.
        /// </summary>
        private ClimbingAnimationConfig GetClimbConfig(CrowdAgent agent)
        {
            if (agent.enemyCrowd != null)
            {
                return agent.enemyCrowd.climbingAnimationConfig;
            }
            return null;
        }
        
        /// <summary>
        /// Set the climb animation for an agent using Mecanim parameters from EnemyCrowd config.
        /// Only used when EnemyCrowd.animatorWorkflow is MecanimReader or BoneTracker.
        /// For GPU Instancer Pro ComputeAnimator workflow, use NavMeshLinkCrowdAnimationHandler.
        /// </summary>
        private void SetClimbAnimation(CrowdAgent agent, bool ascending)
        {
            if (!useMecanimFallback) return;
            
            // Only use Mecanim for MecanimReader/BoneTracker workflows
            var workflow = agent.enemyCrowd?.animatorWorkflow ?? AnimatorWorkflowOverride.ComputeAnimator;
            bool isMecanimWorkflow = (workflow == AnimatorWorkflowOverride.MecanimReader || 
                                      workflow == AnimatorWorkflowOverride.BoneTracker);
            
            if (!isMecanimWorkflow)
            {
                // ComputeAnimator workflow - NavMeshLinkCrowdAnimationHandler handles this
                return;
            }
            
            var config = GetClimbConfig(agent);
            if (config == null) return;
            
            var animator = agent.transform.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // Set climbing bool parameter
                if (!string.IsNullOrEmpty(config.climbingBoolParameter))
                {
                    animator.SetBool(config.climbingBoolParameter, true);
                }
                
                // Set climb speed/direction parameter
                if (!string.IsNullOrEmpty(config.climbSpeedParameter))
                {
                    animator.SetFloat(config.climbSpeedParameter, ascending ? 1f : -1f);
                }
                
                if (debugMode)
                {
                    Debug.Log($"[NavMeshLinkTraversalHandler] Agent {agent.index} Mecanim: {config.climbingBoolParameter}=true, {config.climbSpeedParameter}={(ascending ? 1f : -1f)}");
                }
            }
        }
        
        /// <summary>
        /// Reset agent animation after climbing using Mecanim parameters.
        /// Only used when EnemyCrowd.animatorWorkflow is MecanimReader or BoneTracker.
        /// </summary>
        private void ResetAgentAnimation(CrowdAgent agent)
        {
            if (!useMecanimFallback) return;
            
            // Only use Mecanim for MecanimReader/BoneTracker workflows
            var workflow = agent.enemyCrowd?.animatorWorkflow ?? AnimatorWorkflowOverride.ComputeAnimator;
            bool isMecanimWorkflow = (workflow == AnimatorWorkflowOverride.MecanimReader || 
                                      workflow == AnimatorWorkflowOverride.BoneTracker);
            
            if (!isMecanimWorkflow) return;
            
            var config = GetClimbConfig(agent);
            
            var animator = agent.transform.GetComponentInChildren<Animator>();
            if (animator != null && config != null)
            {
                if (!string.IsNullOrEmpty(config.climbingBoolParameter))
                {
                    animator.SetBool(config.climbingBoolParameter, false);
                }
            }
        }
        
        /// <summary>
        /// Register a new climbable at runtime.
        /// </summary>
        public void RegisterClimbable(NavMeshLinkClimbable climbable)
        {
            if (!_registeredClimbables.Contains(climbable))
            {
                _registeredClimbables.Add(climbable);
            }
        }
        
        /// <summary>
        /// Unregister a climbable at runtime.
        /// </summary>
        public void UnregisterClimbable(NavMeshLinkClimbable climbable)
        {
            _registeredClimbables.Remove(climbable);
        }
        
        /// <summary>
        /// Check if an agent is currently traversing any climb link.
        /// </summary>
        public bool IsAgentClimbing(int agentIndex)
        {
            return _agentStates.ContainsKey(agentIndex);
        }
        
        /// <summary>
        /// Get the climb progress (0-1) for an agent.
        /// </summary>
        public float GetAgentClimbProgress(int agentIndex)
        {
            if (_agentStates.TryGetValue(agentIndex, out var state))
            {
                float elapsed = Time.time - state.startTime;
                return Mathf.Clamp01(elapsed / state.ActualDuration);
            }
            return 0f;
        }
        
        /// <summary>
        /// Get the climb duration for an agent (in seconds), accounting for horde speed variation.
        /// </summary>
        public float GetAgentClimbDuration(int agentIndex)
        {
            if (_agentStates.TryGetValue(agentIndex, out var state))
            {
                return state.ActualDuration;
            }
            return 0f;
        }
        
        /// <summary>
        /// Get the climbable the agent is currently traversing.
        /// </summary>
        public NavMeshLinkClimbable GetAgentClimbable(int agentIndex)
        {
            if (_agentStates.TryGetValue(agentIndex, out var state))
            {
                return state.climbable;
            }
            return null;
        }
        
        /// <summary>
        /// Get whether agent is climbing up (true) or down (false).
        /// </summary>
        public bool IsAgentAscending(int agentIndex)
        {
            if (_agentStates.TryGetValue(agentIndex, out var state))
            {
                return state.ascending;
            }
            return true;
        }
        
        /// <summary>
        /// Force complete an agent's climb traversal.
        /// </summary>
        public void ForceCompleteTraversal(int agentIndex)
        {
            if (_agentStates.TryGetValue(agentIndex, out var state))
            {
                var agent = state.agent;
                var navMeshAgent = GetUnityNavMeshAgent(state.navigationAgent);
                if (agent != null && navMeshAgent != null && navMeshAgent.isOnOffMeshLink)
                {
                    navMeshAgent.CompleteOffMeshLink();
                    navMeshAgent.Warp(state.endPos);
                }
                else if (state.navigationAgent != null)
                {
                    state.navigationAgent.Warp(state.endPos);
                }
                CompleteLinkTraversal(agentIndex);
            }
        }
        
        /// <summary>
        /// Get count of agents currently climbing.
        /// </summary>
        public int GetClimbingAgentCount()
        {
            return _agentStates.Count;
        }
    }
}
