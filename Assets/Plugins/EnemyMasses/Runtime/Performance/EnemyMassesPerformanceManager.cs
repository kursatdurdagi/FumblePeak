using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro.CrowdAnimations;
using GPUInstancerPro.PrefabModule;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages performance optimizations including spatial grid updates, anti-gridlock collision avoidance, and dead agent cleanup.
    /// </summary>
    public class EnemyMassesPerformanceManager
    {
        private readonly PerformanceSettings _settings;
        private readonly CombatSettings _combatSettings;
        private readonly List<CrowdAgent> _agents;
        private readonly SpatialGrid _spatialGrid;

        private readonly List<CrowdAgent> _deadAgents = new();
        private readonly HashSet<int> _deadAgentIndices = new();
        private readonly HashSet<int> _crowdedAgents = new();
        private readonly Dictionary<int, float> _originalAvoidancePriorities = new();
        private readonly Dictionary<int, float> _originalRadiuses = new();

        private float _lastSpatialUpdateTime;
        private float _lastCollisionUpdateTime;
        private float _lastDeadAgentCleanupTime;
        private int _deadAgentCleanupCursor;

        private bool _spatialGridInitialized;

        private const float SPATIAL_UPDATE_INTERVAL = 0.2f;
        private const float DEAD_AGENT_CLEANUP_INTERVAL = 0.5f;
        private const float SINK_FAILSAFE_TIMEOUT = 10f; // seconds

        /// <summary>
        /// Creates a new performance manager.
        /// </summary>
        public EnemyMassesPerformanceManager(
            PerformanceSettings settings,
            CombatSettings combatSettings,
            List<CrowdAgent> agents,
            SpatialGrid spatialGrid)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _combatSettings = combatSettings ?? throw new System.ArgumentNullException(nameof(combatSettings));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
            _spatialGrid = spatialGrid;
        }

        /// <summary>
        /// Initializes the performance manager.
        /// </summary>
        public void Initialize()
        {
            _spatialGridInitialized = _spatialGrid != null && _settings.enableSpatialGrid;
        }

        /// <summary>
        /// Updates performance systems including spatial grid, anti-gridlock, and dead agent cleanup.
        /// </summary>
        public void UpdatePerformanceSystems(
            System.Func<CrowdAgent, bool> isAgentActiveForNavigationCallback)
        {
            float currentTime = Time.time;

            // Update spatial grid periodically
            if (_settings.enableSpatialGrid && currentTime - _lastSpatialUpdateTime >= SPATIAL_UPDATE_INTERVAL)
            {
                UpdateSpatialGrid(isAgentActiveForNavigationCallback);
                _lastSpatialUpdateTime = currentTime;
            }

            // Update anti-gridlock collision avoidance
            if (_settings.enableAntiGridlock && currentTime - _lastCollisionUpdateTime >= _settings.collisionUpdateInterval)
            {
                UpdateCollisionAvoidance(isAgentActiveForNavigationCallback);
                _lastCollisionUpdateTime = currentTime;
            }

            // Process dead agent cleanup
            if (_combatSettings.deadAgentCleanupMode != DeadAgentCleanupMode.None)
            {
                ProcessDeadAgentCleanup();
            }
        }

        #region Spatial Grid Management

        private void UpdateSpatialGrid(System.Func<CrowdAgent, bool> isAgentActiveForNavigationCallback)
        {
            if (!_spatialGridInitialized || _spatialGrid == null)
                return;

            _spatialGrid.Clear();

            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (isAgentActiveForNavigationCallback != null && isAgentActiveForNavigationCallback(agent))
                {
                    if (TryGetNavigationPosition(agent, out Vector3 navigationPosition))
                        _spatialGrid.AddAgent(i, navigationPosition);
                }
            }
        }

        /// <summary>
        /// Queries nearby agents within a radius using the spatial grid.
        /// </summary>
        public List<int> GetAgentsInRadius(Vector3 center, float radius)
        {
            if (_settings.enableSpatialGrid && _spatialGridInitialized && _spatialGrid != null)
            {
                return _spatialGrid.GetAgentsInRadius(center, radius);
            }

            return new List<int>();
        }

        /// <summary>
        /// Clears the spatial grid.
        /// </summary>
        public void ClearSpatialGrid()
        {
            if (_spatialGrid != null)
                _spatialGrid.Clear();
        }

        #endregion

        #region Anti-Gridlock System

        private void UpdateCollisionAvoidance(System.Func<CrowdAgent, bool> isAgentActiveForNavigationCallback)
        {
            if (!_settings.enableAntiGridlock)
                return;

            int aliveAgents = GetAliveAgentCount();
            if (aliveAgents <= 1)
            {
                if (_crowdedAgents.Count > 0)
                {
                    foreach (int agentIndex in _crowdedAgents)
                    {
                        if (agentIndex < 0 || agentIndex >= _agents.Count)
                            continue;

                        var agent = _agents[agentIndex];
                        if (agent != null)
                            RestoreNormalCollision(agent, agentIndex);
                    }
                    _crowdedAgents.Clear();
                }
                return;
            }

            // Clear previous crowded agents state
            _crowdedAgents.Clear();

            // Detect crowded areas and adjust collision settings
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (isAgentActiveForNavigationCallback != null && !isAgentActiveForNavigationCallback(agent))
                {
                    _originalAvoidancePriorities.Remove(i);
                    _originalRadiuses.Remove(i);
                    continue;
                }

                var navigationAgent = agent.navigationAgent;
                if (navigationAgent == null ||
                    !navigationAgent.Capabilities.SupportsPerAgentAvoidanceTuning ||
                    navigationAgent.Transform == null)
                    continue;

                Vector3 agentPos = navigationAgent.Transform.position;
                int nearbyAgentCount = 0;

                // Count nearby agents using spatial grid if available, otherwise linear search
                if (_settings.enableSpatialGrid && _spatialGridInitialized && _spatialGrid != null)
                {
                    var nearbyIndices = _spatialGrid.GetAgentsInRadius(agentPos, _settings.gridlockDetectionRadius);
                    for (int j = 0; j < nearbyIndices.Count; j++)
                    {
                        int otherIndex = nearbyIndices[j];
                        if (otherIndex == i)
                            continue;

                        var otherAgent = _agents[otherIndex];
                        if (isAgentActiveForNavigationCallback != null && !isAgentActiveForNavigationCallback(otherAgent))
                            continue;

                        nearbyAgentCount++;
                    }
                }
                else
                {
                    // Linear search fallback
                    for (int j = 0; j < _agents.Count; j++)
                    {
                        if (i == j) continue;
                        var otherAgent = _agents[j];
                        if (isAgentActiveForNavigationCallback != null && !isAgentActiveForNavigationCallback(otherAgent))
                            continue;

                        if (!TryGetNavigationPosition(otherAgent, out Vector3 otherPosition))
                            continue;

                        float sqrDistance = (agentPos - otherPosition).sqrMagnitude;
                        if (sqrDistance <= _settings.gridlockDetectionRadius * _settings.gridlockDetectionRadius)
                        {
                            nearbyAgentCount++;
                        }
                    }
                }

                // Adjust collision settings based on crowding
                bool isCrowded = nearbyAgentCount >= _settings.gridlockAgentThreshold;

                if (isCrowded && !_crowdedAgents.Contains(i))
                {
                    // Enable reduced collision for crowded agent
                    ApplyReducedCollision(agent, i);
                    _crowdedAgents.Add(i);
                }
                else if (!isCrowded && _crowdedAgents.Contains(i))
                {
                    // Restore normal collision for no longer crowded agent
                    RestoreNormalCollision(agent, i);
                    _crowdedAgents.Remove(i);
                }
            }
        }

        private void ApplyReducedCollision(CrowdAgent agent, int agentIndex)
        {
            var navigationAgent = agent?.navigationAgent;
            if (navigationAgent == null)
                return;

            if (!EnemyMassesNavigationBackendHooks.TryGetPerAgentAvoidanceTuning(
                navigationAgent,
                out float currentPriority,
                out float currentRadius))
            {
                return;
            }

            // Store original values if not already stored
            if (!_originalAvoidancePriorities.ContainsKey(agentIndex))
            {
                _originalAvoidancePriorities[agentIndex] = currentPriority;
                _originalRadiuses[agentIndex] = currentRadius;
            }

            // Reduce collision avoidance
            float reducedPriority = Mathf.RoundToInt(_originalAvoidancePriorities[agentIndex] * _settings.crowdedAreaCollisionMultiplier);
            float reducedRadius = _originalRadiuses[agentIndex] * _settings.crowdedAreaCollisionMultiplier;

            // Lower priority agents can pass through higher priority ones
            if (reducedPriority < 10f)
                reducedPriority = 10f;

            EnemyMassesNavigationBackendHooks.ApplyPerAgentAvoidanceTuning(
                navigationAgent,
                reducedPriority,
                reducedRadius);
        }

        private void RestoreNormalCollision(CrowdAgent agent, int agentIndex)
        {
            var navigationAgent = agent?.navigationAgent;
            if (navigationAgent == null)
                return;

            // Restore original collision settings
            if (!_originalAvoidancePriorities.TryGetValue(agentIndex, out float originalPriority) ||
                !_originalRadiuses.TryGetValue(agentIndex, out float originalRadius))
            {
                return;
            }

            EnemyMassesNavigationBackendHooks.ApplyPerAgentAvoidanceTuning(
                navigationAgent,
                originalPriority,
                originalRadius);
        }

        /// <summary>
        /// Restores collision settings for all agents.
        /// </summary>
        public void RestoreAllCollisionSettings()
        {
            foreach (int agentIndex in _crowdedAgents)
            {
                if (agentIndex >= 0 && agentIndex < _agents.Count)
                {
                    var agent = _agents[agentIndex];
                    if (agent != null)
                        RestoreNormalCollision(agent, agentIndex);
                }
            }

            _crowdedAgents.Clear();
            _originalAvoidancePriorities.Clear();
            _originalRadiuses.Clear();
        }

        #endregion

        #region Dead Agent Cleanup

        /// <summary>
        /// Registers a dead agent for cleanup processing.
        /// </summary>
        public void RegisterDeadAgent(CrowdAgent agent)
        {
            if (agent == null)
                return;

            if (!_deadAgents.Contains(agent))
                _deadAgents.Add(agent);

            if (_combatSettings.optimizeDeadAgentLists && agent.index >= 0)
                _deadAgentIndices.Add(agent.index);
        }

        /// <summary>
        /// Checks if an agent is marked as dead.
        /// </summary>
        public bool IsAgentDead(int agentIndex)
        {
            return _combatSettings.optimizeDeadAgentLists && _deadAgentIndices.Contains(agentIndex);
        }

        /// <summary>
        /// Process dead agents for cleanup based on configured mode.
        /// </summary>
        private void ProcessDeadAgentCleanup()
        {
            if (_combatSettings.deadAgentCleanupMode == DeadAgentCleanupMode.None || _deadAgents.Count == 0)
                return;

            float currentTime = Time.time;

            // Throttle cleanup processing
            if (_combatSettings.batchDeadAgentCleanup && currentTime - _lastDeadAgentCleanupTime < DEAD_AGENT_CLEANUP_INTERVAL)
                return;

            _lastDeadAgentCleanupTime = currentTime;

            int processed = 0;
            int maxPerFrame = _combatSettings.batchDeadAgentCleanup ? _combatSettings.maxDeadAgentCleanupPerFrame : _deadAgents.Count;

            // Process from cursor position
            while (_deadAgentCleanupCursor < _deadAgents.Count && processed < maxPerFrame)
            {
                CrowdAgent agent = _deadAgents[_deadAgentCleanupCursor];

                if (agent != null && !agent.pendingCleanup)
                {
                    float timeSinceDeath = currentTime - agent.deathTime;
                    float cleanupTimeout = _combatSettings.deadAgentCleanupDelay + SINK_FAILSAFE_TIMEOUT;
                    bool forceCleanup = timeSinceDeath >= cleanupTimeout;

                    // If we plan to disable nav after an impulse, keep the agent alive a short grace window
                    // so a post-death impulse can be scheduled. Once the impulse is active, defer until it settles
                    // unless the absolute force cleanup timer has expired.
                    if (!forceCleanup && _combatSettings.disableComponentsOnDeath && agent.disableNavMeshAfterImpulse)
                    {
                        float timeSinceDeathGrace = currentTime - agent.deathTime;
                        if (agent.hasNavmeshOffsetImpulse || timeSinceDeathGrace < 5f)
                        {
                            _deadAgentCleanupCursor++;
                            continue;
                        }
                    }

                    // Defer cleanup while a navmesh fake-physics impulse is active so the NavMeshAgent/GameObject
                    // stays active until the impulse settles and can disable nav if needed, unless the force cleanup timer fires.
                    if (!forceCleanup && agent.hasNavmeshOffsetImpulse)
                    {
                        _deadAgentCleanupCursor++;
                        continue;
                    }

                    // Don't cleanup agents that are still sinking or waiting to sink - wait for sinking to complete.
                    // If ragdoll never activates, allow cleanup once the scheduled sink time has passed to avoid a permanent block.
                    if (!forceCleanup && agent.ragdollSinkPending)
                    {
                        bool sinkOverdue = currentTime >= agent.ragdollSinkScheduleTime + SINK_FAILSAFE_TIMEOUT;
                        if ((currentTime >= agent.ragdollSinkScheduleTime && !agent.ragdollActivated) || sinkOverdue)
                        {
                            agent.ragdollSinkPending = false; // ragdoll never fired or overdue; fall back to normal cleanup
                        }
                        else
                        {
                            _deadAgentCleanupCursor++;
                            continue;
                        }
                    }

                    // Failsafe: if sinking takes too long, allow cleanup to continue
                    if (!forceCleanup && agent.isSinking)
                    {
                        bool sinkTimedOut = currentTime - agent.sinkStartTime > SINK_FAILSAFE_TIMEOUT;
                        if (!sinkTimedOut)
                        {
                            _deadAgentCleanupCursor++;
                            continue;
                        }
                        agent.isSinking = false;
                    }

                    if (forceCleanup)
                    {
                        agent.ragdollSinkPending = false;
                        agent.isSinking = false;
                    }
                    
                    if (timeSinceDeath >= _combatSettings.deadAgentCleanupDelay)
                    {
                        CleanupDeadAgent(agent);
                        processed++;
                    }
                }

                _deadAgentCleanupCursor++;
            }

            // Reset cursor when we've processed all dead agents
            if (_deadAgentCleanupCursor >= _deadAgents.Count)
            {
                // Remove cleaned up agents from the list
                _deadAgents.RemoveAll(a => a == null || a.pendingCleanup);
                _deadAgentCleanupCursor = 0;
            }
        }

        private void CleanupDeadAgent(CrowdAgent agent)
        {
            if (agent == null || agent.pendingCleanup)
                return;

            agent.pendingCleanup = true;

            // A* agents are destroyed instead of pooled/disabled to avoid long-lived
            // deactivated pooled objects causing persistent frame spikes.
            if (ShouldForceDestroyForAstar(agent))
            {
                DestroyDeadAgent(agent);
            }
            else
            {
                switch (_combatSettings.deadAgentCleanupMode)
                {
                    case DeadAgentCleanupMode.Disable:
                        DisableDeadAgent(agent);
                        break;

                    case DeadAgentCleanupMode.Destroy:
                        DestroyDeadAgent(agent);
                        break;

                    case DeadAgentCleanupMode.Pool:
                        // Pooling requires external management - just disable for now
                        DisableDeadAgent(agent);
                        break;
                }
            }

            // Remove from dead agent indices
            if (agent.index >= 0)
                _deadAgentIndices.Remove(agent.index);
        }

        private static bool ShouldForceDestroyForAstar(CrowdAgent agent)
        {
            if (agent?.navigationAgent == null)
                return false;

            return agent.navigationAgent.Capabilities.ShouldThrottleFrequentDestinationUpdates;
        }

        private void DisableDeadAgent(CrowdAgent agent)
        {
            if (agent?.crowdInstance == null)
                return;

            GameObject agentObject = agent.crowdInstance.gameObject;
            
            // Move agent far away before disabling to prevent visual glitches
            // if GPU Instancer reuses the buffer index before transform data is updated
            if (agentObject != null)
            {
                agentObject.transform.position = new Vector3(0, -1000, 0);
            }
            
            // Remove from GPU Instancer BEFORE disabling GameObject
            // Use safe helper to prevent errors during shutdown
            if (agent.prefabComponent != null)
            {
                // Force transform update with the far-away position
                if (agent.prefabComponent.registeredManager != null)
                {
                    agent.prefabComponent.registeredManager.UpdateTransformData(agent.prefabComponent);
                }
                EnemyMassesGPUIHelper.SafeRemovePrefabInstance(agent.prefabComponent);
                agent.prefabComponent = null;
            }
            
            if (agentObject != null && agentObject.activeSelf)
            {
                agentObject.SetActive(false);
            }
        }

        private void DestroyDeadAgent(CrowdAgent agent)
        {
            if (agent == null)
                return;

            // Remove from GPU Instancer first - use safe helper
            EnemyMassesGPUIHelper.SafeRemovePrefabInstance(agent.prefabComponent);
            agent.prefabComponent = null;

            // Destroy the GameObject (fallback to navigation/legacy refs if crowdInstance is null)
            GameObject agentObject = null;
            if (agent.crowdInstance != null)
                agentObject = agent.crowdInstance.gameObject;
            else if (agent.navigationAgent != null && agent.navigationAgent.Transform != null)
                agentObject = agent.navigationAgent.Transform.gameObject;
            else if (agent.transform != null)
                agentObject = agent.transform.gameObject;

            if (agentObject != null)
            {
                Object.Destroy(agentObject);
            }

            // Clear references to help GC
            agent.crowdInstance = null;
            agent.navigationAgent = null;
            agent.mecanimAnimator = null;
            agent.ragdollRigidbodies = null;
            agent.ragdollColliders = null;
        }

        /// <summary>
        /// Clears all dead agent tracking.
        /// </summary>
        public void ClearDeadAgents()
        {
            _deadAgents.Clear();
            _deadAgentIndices.Clear();
            _deadAgentCleanupCursor = 0;
        }

        /// <summary>
        /// Removes a specific agent from the dead agents list.
        /// Used when respawning/pooling agents.
        /// </summary>
        public void RemoveFromDeadAgents(CrowdAgent agent)
        {
            if (agent == null)
                return;

            int index = _deadAgents.IndexOf(agent);
            if (index >= 0)
            {
                _deadAgents.RemoveAt(index);
                _deadAgentIndices.Remove(agent.index);

                // Adjust cursor if it was past the removed index
                if (_deadAgentCleanupCursor > index)
                    _deadAgentCleanupCursor--;

                // Ensure cursor is still valid
                if (_deadAgentCleanupCursor >= _deadAgents.Count)
                    _deadAgentCleanupCursor = 0;
            }
        }

        /// <summary>
        /// Disables components on dead agents to save CPU.
        /// </summary>
        public void DisableDeadAgentComponents(CrowdAgent agent, System.Func<CrowdAgent, bool> shouldUseRagdollCallback)
        {
            if (agent == null)
                return;

            bool shouldUseRagdoll = shouldUseRagdollCallback != null && shouldUseRagdollCallback(agent);
            agent.disableNavMeshAfterImpulse = false;

            // Debug log removed for production build

            // Apply backend-specific death navigation handling (currently NavMesh specific).
            bool disableNavigation = shouldUseRagdoll && agent.isDead;
            if (EnemyMassesNavigationBackendHooks.ApplyDeathNavigationState(agent.navigationAgent, disableNavigation))
            {
                if (!disableNavigation)
                    agent.disableNavMeshAfterImpulse = false;
            }

            // DO NOT disable Animator immediately - for MecanimReader workflow, we need to let the
            // death animation play fully before disabling. The MecanimReader reads the Animator state
            // each frame and sends it to the GPU. We mark the agent for delayed animator disable,
            // which will happen after the sink duration + 0.5 seconds.
            // 
            // The delayed disable is handled in UpdateDeadAgentAnimatorDisable() which should be
            // called each frame from the crowd controller.
            if (!shouldUseRagdoll && agent.mecanimAnimator != null && agent.mecanimAnimator.enabled)
            {
                // Mark for delayed disable - wait until sink duration + 0.5s after death
                agent.pendingAnimatorDisable = true;
                float sinkDuration = _combatSettings != null ? _combatSettings.deathSinkDuration : 2f;
                agent.animatorDisableTime = Time.time + sinkDuration + 0.5f;
            }
        }
        
        /// <summary>
        /// Updates dead agents that need their Animator disabled after delay.
        /// Call this each frame from the crowd controller.
        /// Disables Animator only after sink duration + 0.5s has passed.
        /// </summary>
        public void UpdateDeadAgentAnimatorDisable()
        {
            float currentTime = Time.time;
            for (int i = 0; i < _deadAgents.Count; i++)
            {
                var agent = _deadAgents[i];
                if (agent != null && agent.pendingAnimatorDisable && currentTime >= agent.animatorDisableTime)
                {
                    agent.pendingAnimatorDisable = false;
                    if (agent.mecanimAnimator != null && agent.mecanimAnimator.enabled)
                    {
                        agent.mecanimAnimator.enabled = false;
                    }
                }
            }
        }

        #endregion

        #region Helper Methods

        private static bool TryGetNavigationPosition(CrowdAgent agent, out Vector3 position)
        {
            position = Vector3.zero;
            if (agent == null)
                return false;

            if (agent.navigationAgent != null && agent.navigationAgent.Transform != null)
            {
                position = agent.navigationAgent.Transform.position;
                return true;
            }

            if (agent.crowdInstance != null)
            {
                position = agent.crowdInstance.transform.position;
                return true;
            }

            if (agent.transform != null)
            {
                position = agent.transform.position;
                return true;
            }

            return false;
        }

        private int GetAliveAgentCount()
        {
            int count = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                if (!_agents[i].isDead)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Gets the count of dead agents.
        /// </summary>
        public int GetDeadAgentCount()
        {
            return _deadAgents.Count;
        }

        /// <summary>
        /// Gets the count of agents in crowded areas.
        /// </summary>
        public int GetCrowdedAgentCount()
        {
            return _crowdedAgents.Count;
        }

        #endregion
    }
}
