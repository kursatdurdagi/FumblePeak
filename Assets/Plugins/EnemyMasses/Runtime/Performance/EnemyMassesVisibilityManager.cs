using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages agent visibility detection, frustum culling, and priority categorization.
    /// Categorizes agents into high/medium/low priority groups based on camera proximity and visibility.
    /// </summary>
    public class EnemyMassesVisibilityManager
    {
        private readonly PerformanceSettings _settings;
        private readonly List<CrowdAgent> _agents;
        
        private readonly List<int> _visibleAgentIndices = new List<int>();
        private readonly List<int> _offscreenAgentIndices = new List<int>();
        private readonly List<int> _priorityAgentIndices = new List<int>();
        private readonly HashSet<int> _priorityAgentSet = new HashSet<int>();
        
        private readonly List<int> _highPriorityAgents = new List<int>();
        private readonly List<int> _mediumPriorityAgents = new List<int>();
        private readonly List<int> _lowPriorityAgents = new List<int>();
        
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private bool _frustumPlanesValid = false;
        
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        
        private float _lastVisibilityUpdateTime;
        private float _lastFrustumUpdateTime;
        private float _lastHighPriorityUpdateTime;
        private float _lastMediumPriorityUpdateTime;
        private float _lastLowPriorityUpdateTime;
        private float _lastFailsafeUpdateTime;
        
        private int _visibilityUpdateStartIndex;
        private int _highPriorityUpdateIndex;
        private int _mediumPriorityUpdateIndex;
        private int _lowPriorityUpdateIndex;
        private int _failsafeUpdateIndex;
        
        private float _priorityMaxDistanceSqr;
        private float _highPriorityDistanceSqr;
        private float _mediumPriorityDistanceSqr;

        /// <summary>
        /// Gets the list of visible agent indices.
        /// </summary>
        public IReadOnlyList<int> VisibleAgentIndices => _visibleAgentIndices;

        /// <summary>
        /// Gets the list of offscreen agent indices.
        /// </summary>
        public IReadOnlyList<int> OffscreenAgentIndices => _offscreenAgentIndices;

        /// <summary>
        /// Gets the list of priority agent indices (visible and close to camera).
        /// </summary>
        public IReadOnlyList<int> PriorityAgentIndices => _priorityAgentIndices;

        /// <summary>
        /// Gets the set of priority agent indices for fast lookups.
        /// </summary>
        public HashSet<int> PriorityAgentSet => _priorityAgentSet;

        /// <summary>
        /// Creates a new visibility manager.
        /// </summary>
        public EnemyMassesVisibilityManager(PerformanceSettings settings, List<CrowdAgent> agents)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
        }

        /// <summary>
        /// Initializes cached distance values.
        /// </summary>
        public void Initialize()
        {
            _priorityMaxDistanceSqr = _settings.priorityMaxDistance * _settings.priorityMaxDistance;
            _highPriorityDistanceSqr = _settings.highPriorityDistance * _settings.highPriorityDistance;
            _mediumPriorityDistanceSqr = _settings.mediumPriorityDistance * _settings.mediumPriorityDistance;
            
            _lastVisibilityUpdateTime = Time.time;
            _lastFrustumUpdateTime = Time.time;
            _lastHighPriorityUpdateTime = Time.time;
            _lastMediumPriorityUpdateTime = Time.time;
            _lastLowPriorityUpdateTime = Time.time;
            _lastFailsafeUpdateTime = Time.time;
        }

        /// <summary>
        /// Updates frustum planes if camera has moved.
        /// </summary>
        public void UpdateFrustumPlanes(Camera camera)
        {
            if (camera == null)
            {
                _frustumPlanesValid = false;
                return;
            }

            if (Time.time - _lastFrustumUpdateTime < _settings.frustumUpdateInterval)
                return;

            Transform cameraTransform = camera.transform;
            bool cameraHasMoved = Vector3.SqrMagnitude(cameraTransform.position - _lastCameraPosition) > 0.01f ||
                                Quaternion.Angle(cameraTransform.rotation, _lastCameraRotation) > 1f;

            if (cameraHasMoved || !_frustumPlanesValid)
            {
                Matrix4x4 worldToClip = camera.projectionMatrix * camera.worldToCameraMatrix;
                GeometryUtility.CalculateFrustumPlanes(worldToClip, _frustumPlanes);
                _lastCameraPosition = cameraTransform.position;
                _lastCameraRotation = cameraTransform.rotation;
                _frustumPlanesValid = true;
            }

            _lastFrustumUpdateTime = Time.time;
        }

        /// <summary>
        /// Updates visibility lists for all agents.
        /// </summary>
        public void UpdateVisibilityLists(int agentCount, bool useVisibility, Camera camera, System.Action<CrowdAgent> syncAgentCallback, System.Action<CrowdAgent> checkProgressCallback, bool shouldUseLargeCrowdOptimizations)
        {
            if (_settings.enablePriorityBasedVisibility)
            {
                UpdateVisibilityListsPriorityBased(agentCount, useVisibility, camera, syncAgentCallback, checkProgressCallback, shouldUseLargeCrowdOptimizations);
            }
            else
            {
                UpdateVisibilityListsLegacy(agentCount, useVisibility, camera, syncAgentCallback, checkProgressCallback);
            }
        }

        #region Legacy Visibility System

        private void UpdateVisibilityListsLegacy(int agentCount, bool useVisibility, Camera camera, System.Action<CrowdAgent> syncAgentCallback, System.Action<CrowdAgent> checkProgressCallback)
        {
            bool shouldUpdateVisibility = Time.time - _lastVisibilityUpdateTime >= _settings.visibilityUpdateInterval;
            if (!shouldUpdateVisibility && _visibilityUpdateStartIndex == 0)
                return;

            if (_visibilityUpdateStartIndex == 0)
            {
                _visibleAgentIndices.Clear();
                _offscreenAgentIndices.Clear();
                _priorityAgentIndices.Clear();
                _priorityAgentSet.Clear();
                _lastVisibilityUpdateTime = Time.time;
            }

            Transform cameraTransform = camera != null ? camera.transform : null;
            Vector3 cameraPos = cameraTransform != null ? cameraTransform.position : Vector3.zero;

            int agentsToProcessThisFrame = Mathf.Max(1, Mathf.CeilToInt(agentCount * _settings.visibilityUpdateFraction));
            int endIndex = Mathf.Min(_visibilityUpdateStartIndex + agentsToProcessThisFrame, agentCount);

            for (int i = _visibilityUpdateStartIndex; i < endIndex; i++)
            {
                ProcessAgentVisibility(i, useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);
            }

            _visibilityUpdateStartIndex = endIndex;
            if (_visibilityUpdateStartIndex >= agentCount)
            {
                _visibilityUpdateStartIndex = 0;
            }
        }

        #endregion

        #region Priority-Based Visibility System

        private void UpdateVisibilityListsPriorityBased(int agentCount, bool useVisibility, Camera camera, System.Action<CrowdAgent> syncAgentCallback, System.Action<CrowdAgent> checkProgressCallback, bool shouldUseLargeCrowdOptimizations)
        {
            Transform cameraTransform = camera != null ? camera.transform : null;
            Vector3 cameraPos = cameraTransform != null ? cameraTransform.position : Vector3.zero;

            // For large crowds, use simpler less frequent categorization
            float categorizationInterval = shouldUseLargeCrowdOptimizations 
                ? _settings.frustumUpdateInterval * 3f 
                : _settings.frustumUpdateInterval;

            // Categorize agents into priority groups
            if (Time.time - _lastVisibilityUpdateTime >= categorizationInterval)
            {
                if (shouldUseLargeCrowdOptimizations)
                {
                    CategorizeAgentsByPriorityFast(agentCount, useVisibility, cameraTransform, cameraPos);
                }
                else
                {
                    CategorizeAgentsByPriority(agentCount, useVisibility, cameraTransform, cameraPos);
                }
                _lastVisibilityUpdateTime = Time.time;
            }

            // Update high priority agents frequently
            if (Time.time - _lastHighPriorityUpdateTime >= _settings.highPriorityUpdateInterval)
            {
                UpdateAgentGroup(_highPriorityAgents, _settings.highPriorityUpdateFraction, ref _highPriorityUpdateIndex,
                               useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);
                _lastHighPriorityUpdateTime = Time.time;
            }

            // Update medium priority agents moderately
            if (Time.time - _lastMediumPriorityUpdateTime >= _settings.mediumPriorityUpdateInterval)
            {
                UpdateAgentGroup(_mediumPriorityAgents, _settings.mediumPriorityUpdateFraction, ref _mediumPriorityUpdateIndex,
                               useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);
                _lastMediumPriorityUpdateTime = Time.time;
            }

            // Update low priority agents infrequently
            if (Time.time - _lastLowPriorityUpdateTime >= _settings.lowPriorityUpdateInterval)
            {
                UpdateAgentGroup(_lowPriorityAgents, _settings.lowPriorityUpdateFraction, ref _lowPriorityUpdateIndex,
                               useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);
                _lastLowPriorityUpdateTime = Time.time;
            }

            // FAILSAFE: Ensure all agents get processed regularly
            if (_settings.enablePriorityFailsafe && Time.time - _lastFailsafeUpdateTime >= _settings.failsafeUpdateInterval)
            {
                ProcessFailsafeAgents(agentCount, useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);
                _lastFailsafeUpdateTime = Time.time;
            }
        }

        private void CategorizeAgentsByPriority(int agentCount, bool useVisibility, Transform cameraTransform, Vector3 cameraPos)
        {
            int previousHighCount = _highPriorityAgents.Count;
            int previousMediumCount = _mediumPriorityAgents.Count;
            int previousLowCount = _lowPriorityAgents.Count;

            _highPriorityAgents.Clear();
            _mediumPriorityAgents.Clear();
            _lowPriorityAgents.Clear();
            _visibleAgentIndices.Clear();
            _offscreenAgentIndices.Clear();
            _priorityAgentIndices.Clear();
            _priorityAgentSet.Clear();

            for (int i = 0; i < agentCount; i++)
            {
                var agent = _agents[i];
                if (agent?.navigationAgent == null)
                    continue;

                Vector3 agentPos = agent.navigationAgent.Position;
                float sqrDistanceToCamera = cameraTransform != null ? (agentPos - cameraPos).sqrMagnitude : float.MaxValue;
                bool visible = !useVisibility || IsAgentVisible(agent);

                // Balanced categorization to prevent gridlock
                if (visible && sqrDistanceToCamera <= _highPriorityDistanceSqr)
                {
                    _highPriorityAgents.Add(i);
                }
                else if (visible && sqrDistanceToCamera <= _mediumPriorityDistanceSqr)
                {
                    _mediumPriorityAgents.Add(i);
                }
                else if (visible)
                {
                    // Visible but distant - still medium priority to avoid gridlock
                    _mediumPriorityAgents.Add(i);
                }
                else
                {
                    // Only truly invisible/offscreen agents go to low priority
                    _lowPriorityAgents.Add(i);
                }
            }

            // Debug logging
            if (_settings.debugPriorityUpdates && (previousHighCount != _highPriorityAgents.Count ||
                previousMediumCount != _mediumPriorityAgents.Count ||
                previousLowCount != _lowPriorityAgents.Count))
            {
                Debug.Log($"Priority Update: High={_highPriorityAgents.Count} (was {previousHighCount}), " +
                         $"Medium={_mediumPriorityAgents.Count} (was {previousMediumCount}), " +
                         $"Low={_lowPriorityAgents.Count} (was {previousLowCount}) @ {Time.time:F2}s");
            }
        }

        private void CategorizeAgentsByPriorityFast(int agentCount, bool useVisibility, Transform cameraTransform, Vector3 cameraPos)
        {
            // Fast categorization for large crowds - simpler logic, less precision
            _highPriorityAgents.Clear();
            _mediumPriorityAgents.Clear();
            _lowPriorityAgents.Clear();
            _visibleAgentIndices.Clear();
            _offscreenAgentIndices.Clear();
            _priorityAgentIndices.Clear();
            _priorityAgentSet.Clear();

            // Process only a subset of agents per frame
            int agentsToProcess = Mathf.Min(agentCount, 1000);
            int step = Mathf.Max(1, agentCount / agentsToProcess);

            for (int i = 0; i < agentCount; i += step)
            {
                var agent = _agents[i];
                if (agent?.navigationAgent == null)
                    continue;

                Vector3 agentPos = agent.navigationAgent.Position;
                float sqrDistanceToCamera = cameraTransform != null ? (agentPos - cameraPos).sqrMagnitude : float.MaxValue;

                // Simplified visibility check - just use distance
                bool visible = sqrDistanceToCamera <= _mediumPriorityDistanceSqr * 2f;

                if (visible && sqrDistanceToCamera <= _highPriorityDistanceSqr)
                {
                    _highPriorityAgents.Add(i);
                }
                else if (visible)
                {
                    _mediumPriorityAgents.Add(i);
                }
                else
                {
                    _lowPriorityAgents.Add(i);
                }
            }

            // Fill visibility lists
            _visibleAgentIndices.AddRange(_highPriorityAgents);
            _visibleAgentIndices.AddRange(_mediumPriorityAgents);
            _offscreenAgentIndices.AddRange(_lowPriorityAgents);
            _priorityAgentIndices.AddRange(_highPriorityAgents);

            // Update HashSet for fast lookups
            _priorityAgentSet.Clear();
            foreach (int agentIndex in _priorityAgentIndices)
            {
                _priorityAgentSet.Add(agentIndex);
            }
        }

        private void UpdateAgentGroup(List<int> agentGroup, float updateFraction, ref int updateIndex,
                                    bool useVisibility, Transform cameraTransform, Vector3 cameraPos,
                                    System.Action<CrowdAgent> syncAgentCallback, System.Action<CrowdAgent> checkProgressCallback)
        {
            if (agentGroup.Count == 0)
                return;

            int agentsToProcess = Mathf.Max(1, Mathf.CeilToInt(agentGroup.Count * updateFraction));
            int processed = 0;

            while (processed < agentsToProcess && agentGroup.Count > 0)
            {
                if (updateIndex >= agentGroup.Count)
                    updateIndex = 0;

                int agentIndex = agentGroup[updateIndex];
                ProcessAgentVisibility(agentIndex, useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);

                updateIndex++;
                processed++;
            }
        }

        private void ProcessFailsafeAgents(int agentCount, bool useVisibility, Transform cameraTransform, Vector3 cameraPos,
                                         System.Action<CrowdAgent> syncAgentCallback, System.Action<CrowdAgent> checkProgressCallback)
        {
            // Process a small fraction of all agents to ensure no one gets starved
            int agentsToProcess = Mathf.Max(1, Mathf.CeilToInt(agentCount * 0.1f));

            for (int i = 0; i < agentsToProcess; i++)
            {
                if (_failsafeUpdateIndex >= agentCount)
                    _failsafeUpdateIndex = 0;

                ProcessAgentVisibility(_failsafeUpdateIndex, useVisibility, cameraTransform, cameraPos, syncAgentCallback, checkProgressCallback);
                _failsafeUpdateIndex++;
            }

            if (_settings.debugPriorityUpdates)
            {
                Debug.Log($"Failsafe processed {agentsToProcess} agents starting from index {_failsafeUpdateIndex - agentsToProcess}");
            }
        }

        #endregion

        #region Agent Visibility Processing

        private void ProcessAgentVisibility(int agentIndex, bool useVisibility, Transform cameraTransform, Vector3 cameraPos,
                                          System.Action<CrowdAgent> syncAgentCallback, System.Action<CrowdAgent> checkProgressCallback)
        {
            var agent = _agents[agentIndex];
            if (agent == null || agent.navigationAgent == null)
                return;

            bool visible = !useVisibility || IsAgentVisible(agent);
            
            // Detect visibility transition: invisible -> visible
            // This is used to trigger animation refresh for agents that spawned outside frustum
            // and may have stale animation data from GPU buffer reuse
            if (visible && !agent.wasVisible && !agent.isDead)
            {
                agent.needsAnimationRefresh = true;
            }
            
            agent.wasVisible = agent.isVisible;
            agent.isVisible = visible;

            // Check if agent is within priority distance
            bool withinPriorityDistance = false;
            if (cameraTransform != null && _settings.priorityMaxDistance > 0f)
            {
                float sqrDistanceToCamera = (agent.navigationAgent.Position - cameraPos).sqrMagnitude;
                withinPriorityDistance = sqrDistanceToCamera <= _priorityMaxDistanceSqr;
            }

            bool isPriorityAgent = visible && withinPriorityDistance;

            if (visible)
            {
                _visibleAgentIndices.Add(agentIndex);
                syncAgentCallback?.Invoke(agent);
            }
            else
            {
                _offscreenAgentIndices.Add(agentIndex);
            }

            if (isPriorityAgent)
            {
                if (!_priorityAgentSet.Contains(agentIndex))
                {
                    _priorityAgentIndices.Add(agentIndex);
                    _priorityAgentSet.Add(agentIndex);
                }
            }
            else
            {
                if (_priorityAgentSet.Contains(agentIndex))
                {
                    _priorityAgentIndices.Remove(agentIndex);
                    _priorityAgentSet.Remove(agentIndex);
                }
            }

            checkProgressCallback?.Invoke(agent);
        }

        /// <summary>
        /// Builds a prioritized list for animation updates.
        /// Priority order: priority agents, visible agents, offscreen agents.
        /// </summary>
        public List<int> BuildAnimationPriorityList()
        {
            var animationPriorityList = new List<int>();
            var refreshPriorityList = new List<int>();

            // First priority: agents that are visible and within priority distance
            for (int i = 0; i < _priorityAgentIndices.Count; i++)
            {
                int agentIndex = _priorityAgentIndices[i];
                if (NeedsAnimationRefresh(agentIndex))
                    refreshPriorityList.Add(agentIndex);
                else
                    animationPriorityList.Add(agentIndex);
            }
            
            // Second priority: remaining visible agents
            for (int i = 0; i < _visibleAgentIndices.Count; i++)
            {
                int agentIndex = _visibleAgentIndices[i];
                if (_priorityAgentSet.Contains(agentIndex))
                    continue;

                if (NeedsAnimationRefresh(agentIndex))
                    refreshPriorityList.Add(agentIndex);
                else
                    animationPriorityList.Add(agentIndex);
            }
            
            // Last priority: offscreen agents
            for (int i = 0; i < _offscreenAgentIndices.Count; i++)
            {
                int agentIndex = _offscreenAgentIndices[i];
                if (NeedsAnimationRefresh(agentIndex))
                    refreshPriorityList.Add(agentIndex);
                else
                    animationPriorityList.Add(agentIndex);
            }

            if (refreshPriorityList.Count > 0)
            {
                refreshPriorityList.AddRange(animationPriorityList);
                return refreshPriorityList;
            }

            return animationPriorityList;
        }

        /// <summary>
        /// Builds separate animation update lists to allow cursor-based rotation within visible agents.
        /// </summary>
        public void BuildAnimationUpdateLists(
            List<int> refreshIndices,
            List<int> priorityVisibleIndices,
            List<int> visibleIndices,
            List<int> offscreenIndices)
        {
            if (refreshIndices == null || priorityVisibleIndices == null || visibleIndices == null || offscreenIndices == null)
                return;

            refreshIndices.Clear();
            priorityVisibleIndices.Clear();
            visibleIndices.Clear();
            offscreenIndices.Clear();

            for (int i = 0; i < _priorityAgentIndices.Count; i++)
            {
                int agentIndex = _priorityAgentIndices[i];
                if (NeedsAnimationRefresh(agentIndex))
                    refreshIndices.Add(agentIndex);
                else
                    priorityVisibleIndices.Add(agentIndex);
            }

            for (int i = 0; i < _visibleAgentIndices.Count; i++)
            {
                int agentIndex = _visibleAgentIndices[i];
                if (_priorityAgentSet.Contains(agentIndex))
                    continue;

                if (NeedsAnimationRefresh(agentIndex))
                    refreshIndices.Add(agentIndex);
                else
                    visibleIndices.Add(agentIndex);
            }

            for (int i = 0; i < _offscreenAgentIndices.Count; i++)
            {
                int agentIndex = _offscreenAgentIndices[i];
                if (NeedsAnimationRefresh(agentIndex))
                    refreshIndices.Add(agentIndex);
                else
                    offscreenIndices.Add(agentIndex);
            }
        }

        private bool IsAgentVisible(CrowdAgent agent)
        {
            if (agent != null && agent.isFogHidden)
                return false;
            if (!_frustumPlanesValid)
                return true;

            var navAgent = agent.navigationAgent;
            if (navAgent == null)
                return false;

            Transform transform = navAgent.Transform;
            float radius = Mathf.Max(navAgent.Radius, 0.1f);
            float height = Mathf.Max(navAgent.Height, 0.1f);
            Bounds bounds = new Bounds(navAgent.Position + Vector3.up * height * 0.5f, new Vector3(radius * 2f, height, radius * 2f));
            return GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds);
        }

        private bool NeedsAnimationRefresh(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;

            var agent = _agents[agentIndex];
            return agent != null && agent.needsAnimationRefresh;
        }

        #endregion
        
        #region Phase 2: Settings Helpers (EnemyCrowd as single source of truth)
        
        // Note: Visibility manager doesn't need per-agent settings currently
        // as performance settings are typically global. This region is here
        // for consistency and future extensibility if needed.
        
        #endregion
    }
}
