using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using Arawn.EnemyMasses.Runtime.Navigation;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Detects and recovers agents that are stuck and unable to reach their destination.
    /// Implements retry logic, teleportation, and progress tracking.
    /// </summary>
    public class EnemyMassesStuckRecoverySystem
    {
        private readonly NavigationSettings _settings;
        private readonly EnemyMassesFormationSystem _formationSystem;
        
        private int _destinationGeneration;
        private bool _enableArrivalClusterStop;

        public event System.Action<CrowdAgent> OnAgentStuck;
        public event System.Action<CrowdAgent> OnAgentTeleported;
        public event System.Action<CrowdAgent> OnAgentArrived;

        /// <summary>
        /// Creates a new stuck recovery system.
        /// </summary>
        public EnemyMassesStuckRecoverySystem(
            NavigationSettings settings,
            EnemyMassesFormationSystem formationSystem)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _formationSystem = formationSystem ?? throw new System.ArgumentNullException(nameof(formationSystem));
        }

        /// <summary>
        /// Sets the current destination generation for validation.
        /// </summary>
        public void SetDestinationGeneration(int generation)
        {
            _destinationGeneration = generation;
        }

        /// <summary>
        /// Sets whether arrival cluster stop is enabled.
        /// </summary>
        public void SetArrivalClusterStop(bool enabled)
        {
            _enableArrivalClusterStop = enabled;
        }

        /// <summary>
        /// Checks if an agent is making progress toward its destination and recovers if stuck.
        /// </summary>
        /// <param name="agent">Agent to check</param>
        public void CheckAgentProgress(CrowdAgent agent)
        {
            if (!_settings.enableStuckRecovery || agent == null || agent.navigationAgent == null)
                return;

            if (agent.isDead || agent.ragdollActivated)
                return;

            if (agent.hasArrived)
                return;
            
            // Flight3D agents use continuous steering without a NavMesh — they cannot
            // get stuck in the traditional sense (path failure, obstacle blockage).
            // Their arrival slowdown near ground-level destinations produces very low
            // speeds that the stuck detector misinterprets as zero progress, and teleporting
            // them warps to ground level (Y=0), destroying their altitude.
            if (agent.isFlying)
                return;
            
            // Skip agents that are traversing a NavMeshLink (climbing) - they're intentionally off NavMesh
            if (agent.navigationAgent.IsOnOffMeshLink)
            {
                agent.lastProgressTime = Time.time;
                agent.pathPendingStartTime = 0f;
                return;
            }
            
            // Skip agents in combat animation states - they intentionally stop to attack/block/take hits
            // Detecting them as "stuck" would cause teleportation during combat
            if (agent.combatState == CrowdCombatState.Attacking ||
                agent.combatState == CrowdCombatState.Blocking ||
                agent.combatState == CrowdCombatState.Hit)
            {
                // Reset progress tracking so they don't get flagged as stuck when returning to Idle
                agent.lastProgressTime = Time.time;
                return;
            }
            
            // Skip agents actively chasing/engaging enemies or rushing to help allies.
            // These agents are navigating toward combat targets — their movement may
            // appear stalled (e.g., offscreen sync throttling, path recalculation) but
            // they are NOT stuck. Teleporting them would warp them to the enemy position.
            if (agent.navigationState == AgentNavigationState.ChasingTarget ||
                agent.navigationState == AgentNavigationState.CombatOverride ||
                agent.isRushing ||
                agent.isChasing)
            {
                agent.lastProgressTime = Time.time;
                return;
            }

            var navAgent = agent.navigationAgent;
            if (!CanControlNavigationAgent(navAgent))
                return;

            // Some backends can be intentionally stopped during combat/stance changes.
            // Avoid treating that as "stuck" to prevent recovery loops.
            if (navAgent.Capabilities.ShouldThrottleFrequentDestinationUpdates &&
                navAgent.IsStopped &&
                agent.navigationState != AgentNavigationState.Formation)
            {
                agent.lastProgressTime = Time.time;
                agent.pathPendingStartTime = 0f;
                return;
            }

            // Handle pathPending timeout - Unity sometimes gets stuck computing paths
            if (navAgent.PathPending)
            {
                // Track how long path has been pending
                if (agent.pathPendingStartTime <= 0f)
                {
                    agent.pathPendingStartTime = Time.time;
                    return;
                }
                
                // If path pending for more than 5 seconds, force retry
                // Use longer timeout for large crowds to account for NavMesh queue saturation
                const float PATH_PENDING_TIMEOUT = 5f;
                if (Time.time - agent.pathPendingStartTime > PATH_PENDING_TIMEOUT)
                {
                    Debug.Log($"Agent pathPending timeout ({PATH_PENDING_TIMEOUT}s)! Forcing path reset.");
                    
                    // Force INavigationAgent reset to clear stuck pathPending state
                    navAgent.ResetPath();
                    navAgent.Enabled = false;
                    navAgent.Enabled = true;
                    
                    // Mark for re-processing
                    agent.destinationGeneration--;
                    agent.pathPendingStartTime = 0f;
                }
                
                return;
            }
            
            // Clear pathPending timer when not pending
            agent.pathPendingStartTime = 0f;

            // Check if destination changed
            float remainingDistance = navAgent.RemainingDistance;
            if (float.IsNaN(remainingDistance) || float.IsInfinity(remainingDistance))
            {
                // A* can report invalid distances while paths are recalculating; skip recovery.
                agent.lastProgressTime = Time.time;
                return;
            }
            bool destinationChanged = (agent.lastDestination - navAgent.Destination).sqrMagnitude > 0.01f;
            
            if (destinationChanged)
            {
                ResetProgressTracking(agent, navAgent, remainingDistance);
                return;
            }

            if (!navAgent.HasPath)
                return;

            // Initialize distance tracking
            if (float.IsPositiveInfinity(agent.lastKnownDistance))
            {
                agent.lastKnownDistance = remainingDistance;
                agent.lastProgressTime = Time.time;
                agent.hasArrived = false;
                return;
            }

            // Check if arrived at destination
            if (remainingDistance <= navAgent.StoppingDistance + _settings.stuckProximitySlack)
            {
                agent.lastKnownDistance = remainingDistance;
                agent.lastProgressTime = Time.time;
                agent.remainingRetries = _settings.stuckRetryAttempts;
                agent.nextRetryTime = Time.time + _settings.stuckRetryInterval;
                MarkAgentArrived(agent);
                return;
            }

            // Check if agent made progress
            float progressDelta = agent.lastKnownDistance - remainingDistance;
            if (progressDelta > _settings.stuckProximitySlack)
            {
                agent.lastKnownDistance = remainingDistance;
                agent.lastProgressTime = Time.time;
                agent.remainingRetries = _settings.stuckRetryAttempts;
                agent.nextRetryTime = Time.time + _settings.stuckRetryInterval;
                return;
            }

            // Check if agent is moving
            float speed = navAgent.Velocity.magnitude;
            bool isMoving = speed > _settings.stuckVelocityThreshold;
            if (progressDelta > 0f)
                agent.lastKnownDistance = remainingDistance;

            if (isMoving)
            {
                // Agent is moving - check if heading toward destination
                Vector3 toDestination = agent.lastDestination - agent.WorldPosition;
                if (toDestination.sqrMagnitude <= 0.01f)
                {
                    agent.lastProgressTime = Time.time;
                    return;
                }

                Vector3 velocityDir = navAgent.Velocity.normalized;
                Vector3 destinationDir = toDestination.normalized;
                
                // Allow some deviation (up to ~105 degrees)
                if (Vector3.Dot(velocityDir, destinationDir) > -0.25f)
                {
                    agent.lastProgressTime = Time.time;
                    return;
                }
            }

            // Agent appears stuck - check timeout
            if (Time.time - agent.lastProgressTime < _settings.stuckTimeout)
                return;

            // Agent is stuck! Attempt recovery
            RecoverStuckAgent(agent, navAgent);
        }

        #region Recovery Methods

        private void RecoverStuckAgent(CrowdAgent agent, INavigationAgent navAgent)
        {
            Vector3 agentPos = navAgent?.Transform != null ? navAgent.Transform.position : agent.WorldPosition;
            float stuckDuration = Time.time - agent.lastProgressTime;
            Debug.Log($"[StuckRecovery] Agent {agent.index} STUCK! " +
                $"pos={agentPos:F2}, dest={agent.lastDestination:F2}, " +
                $"dist={navAgent?.RemainingDistance:F2}, visible={agent.isVisible}, " +
                $"navState={agent.navigationState}, combatState={agent.combatState}, " +
                $"isChasing={agent.isChasing}, isRushing={agent.isRushing}, " +
                $"wasAlerted={agent.wasAlertedByAlly}, isAggroed={agent.isAggroed}, " +
                $"retries={agent.remainingRetries}, stuckFor={stuckDuration:F1}s, " +
                $"speed={navAgent?.Velocity.magnitude:F2}, onNavMesh={agent.IsOnNavMesh}");

            OnAgentStuck?.Invoke(agent);

            bool shouldTeleport = _settings.teleportStuckAgents || 
                                (_settings.teleportAfterRetries && agent.remainingRetries <= 0);

            if (shouldTeleport)
            {
                TeleportAgentToDestination(agent, navAgent);
                return;
            }

            if (agent.remainingRetries > 0)
            {
                if (Time.time >= agent.nextRetryTime)
                {
                    // Don't retry if the agent is stopped by arrival cluster
                    if (_enableArrivalClusterStop && agent.hasArrived)
                    {
                        agent.nextRetryTime = Time.time + _settings.stuckRetryInterval;
                        return;
                    }

                    RetryWithNewDestination(agent, navAgent);
                }
                else
                {
                    agent.nextRetryTime = Mathf.Max(agent.nextRetryTime, Time.time + _settings.stuckRetryInterval * 0.25f);
                }
                return;
            }

            // Out of retries - stop and mark arrived
            StopNavigationAgent(navAgent, true);
            agent.lastDestination = navAgent.Transform != null ? navAgent.Transform.position : agent.WorldPosition;
            agent.lastKnownDistance = 0f;
            MarkAgentArrived(agent);
        }

        private void TeleportAgentToDestination(CrowdAgent agent, INavigationAgent navAgent)
        {
            Vector3 fromPosition = navAgent?.Transform != null ? navAgent.Transform.position : agent.WorldPosition;
            Vector3 targetPosition = agent.lastDestination;
            
            // For flying agents, preserve their current altitude instead of snapping
            // to the ground-level NavMesh position returned by SamplePosition.
            if (agent.isFlying)
            {
                targetPosition = new Vector3(targetPosition.x, fromPosition.y, targetPosition.z);
            }
            else
            {
                // Ensure position is on navigation surface
                if (navAgent != null && navAgent.SamplePosition(agent.lastDestination, out Vector3 sampled, 1f))
                    targetPosition = sampled;
            }

            float teleportDistance = Vector3.Distance(fromPosition, targetPosition);
            Debug.LogWarning($"[StuckRecovery] TELEPORTING Agent {agent.index}: " +
                $"from={fromPosition:F2} to={targetPosition:F2} (dist={teleportDistance:F1}m), " +
                $"visible={agent.isVisible}, navState={agent.navigationState}, " +
                $"combatState={agent.combatState}, isChasing={agent.isChasing}, " +
                $"isRushing={agent.isRushing}, wasAlerted={agent.wasAlertedByAlly}, " +
                $"isAggroed={agent.isAggroed}, onNavMesh={agent.IsOnNavMesh}");

            navAgent.Warp(targetPosition);
            navAgent.Position = targetPosition;
            
            MarkAgentArrived(agent);
            OnAgentTeleported?.Invoke(agent);
        }

        private void RetryWithNewDestination(CrowdAgent agent, INavigationAgent navAgent)
        {
            Vector3 retryAnchor = agent.lastDestination;
            if (retryAnchor == Vector3.zero)
                retryAnchor = navAgent.Transform != null ? navAgent.Transform.position : agent.WorldPosition;

            Vector3 retryTarget = GetRandomNavigationPosition(navAgent, retryAnchor, _settings.destinationSpread);
            
            ResumeNavigationAgent(navAgent);
            SetNavDestination(navAgent, retryTarget);
            
            agent.lastDestination = retryTarget;
            if (CanControlNavigationAgent(navAgent))
                agent.lastKnownDistance = navAgent.RemainingDistance;
            
            agent.lastProgressTime = Time.time;
            agent.remainingRetries--;
            agent.nextRetryTime = Time.time + _settings.stuckRetryInterval;
            agent.hasArrived = false;
        }

        private void ResetProgressTracking(CrowdAgent agent, INavigationAgent navAgent, float remainingDistance)
        {
            agent.lastDestination = navAgent.Destination;
            agent.lastKnownDistance = remainingDistance;
            agent.lastProgressTime = Time.time;
            agent.remainingRetries = _settings.stuckRetryAttempts;
            agent.nextRetryTime = Time.time + _settings.stuckRetryInterval;
            agent.hasArrived = false;
            agent.destinationGeneration = _destinationGeneration;
        }

        private void MarkAgentArrived(CrowdAgent agent)
        {
            if (agent == null)
                return;

            // Only mark as arrived if belongs to current destination generation
            if (agent.destinationGeneration != _destinationGeneration)
                return;

            if (!agent.hasArrived)
            {
                agent.hasArrived = true;
                OnAgentArrived?.Invoke(agent);
            }
        }

        #endregion

        #region Navigation Agent Helper Methods

        private static bool CanControlNavigationAgent(INavigationAgent navAgent)
        {
            if (navAgent == null || !navAgent.Enabled)
                return false;

            if (navAgent.Capabilities.RequiresSurfaceValidationForControl)
                return navAgent.IsOnNavMesh;

            return true;
        }

        private static void StopNavigationAgent(INavigationAgent navAgent, bool resetPath = false)
        {
            if (navAgent == null || !navAgent.Enabled)
                return;

            if (navAgent.IsOnNavMesh)
            {
                navAgent.IsStopped = true;
                if (resetPath)
                    navAgent.ResetPath();
            }

            navAgent.Velocity = Vector3.zero;
        }

        private static void ResumeNavigationAgent(INavigationAgent navAgent)
        {
            if (!CanControlNavigationAgent(navAgent))
                return;

            if (navAgent.IsStopped)
                navAgent.IsStopped = false;
        }

        private static Vector3 GetRandomNavigationPosition(INavigationAgent navAgent, Vector3 origin, float range)
        {
            if (navAgent == null)
                return origin;

            for (int i = 0; i < 30; i++)
            {
                Vector3 randomPoint = origin + Random.insideUnitSphere * range;
                randomPoint.y = origin.y;

                if (navAgent.SamplePosition(randomPoint, out Vector3 sampled, range))
                    return sampled;
            }

            // Fallback: try original origin
            if (navAgent.SamplePosition(origin, out Vector3 originSampled, range))
                return originSampled;

            return origin;
        }

        private static void SetNavDestination(INavigationAgent navAgent, Vector3 destination)
        {
            if (!CanControlNavigationAgent(navAgent))
                return;

            navAgent.SetDestination(destination);
        }

        #endregion
    }
}
