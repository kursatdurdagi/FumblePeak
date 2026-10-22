using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages distance-based workflow transitions for crowd agents.
    /// Switches between BoneTracker (close), MecanimReader (medium), and ComputeAnimator (distant) based on camera distance.
    /// </summary>
    public class EnemyMassesHybridWorkflowSystem
    {
        private readonly PerformanceSettings _settings;
        private readonly AnimationSettings _animationSettings;
        private readonly List<CrowdAgent> _agents;
        private readonly Camera _camera;
        
        private readonly List<int> _closeRangeAgents = new List<int>();
        private readonly List<int> _mediumRangeAgents = new List<int>();
        private readonly List<int> _distantRangeAgents = new List<int>();
        private readonly List<WorkflowCandidate> _closeRangeCandidates = new List<WorkflowCandidate>();
        private readonly List<WorkflowCandidate> _mediumRangeCandidates = new List<WorkflowCandidate>();
        
        private int _currentCloseRangeCount;
        private int _currentMediumRangeCount;
        
        private float _hybridCloseDistanceSqr;
        private float _hybridMediumDistanceSqr;
        private float _lastHybridUpdateTime;
        
        private const float LOCOMOTION_EXIT_THRESHOLD = 0.02f;

        public event System.Action<CrowdAgent, HybridWorkflowType, HybridWorkflowType> OnWorkflowChanged;

        /// <summary>
        /// Creates a new hybrid workflow system.
        /// </summary>
        public EnemyMassesHybridWorkflowSystem(
            PerformanceSettings settings,
            AnimationSettings animationSettings,
            List<CrowdAgent> agents,
            Camera camera)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _animationSettings = animationSettings ?? throw new System.ArgumentNullException(nameof(animationSettings));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
            _camera = camera;
        }

        /// <summary>
        /// Initializes cached distance values.
        /// </summary>
        public void Initialize()
        {
            _hybridCloseDistanceSqr = _settings.hybridCloseDistance * _settings.hybridCloseDistance;
            _hybridMediumDistanceSqr = _settings.hybridMediumDistance * _settings.hybridMediumDistance;
            _lastHybridUpdateTime = Time.time;
        }

        /// <summary>
        /// Updates workflow assignments based on agent distance from camera.
        /// </summary>
        public void UpdateHybridWorkflows(
            System.Func<CrowdAgent, bool> shouldUseRagdollCallback,
            System.Action<CrowdAgent> forceBoneTrackerCallback,
            System.Action<CrowdAgent, bool> deactivateRagdollCallback,
            System.Func<CrowdAgent, float> calculateNormalizedSpeedCallback,
            System.Action<CrowdAgent, float, float> startLocomotionBlendCallback)
        {
            if (!_settings.enableHybridWorkflows || _camera == null)
                return;

            if (Time.time - _lastHybridUpdateTime < _settings.hybridUpdateInterval)
                return;

            _closeRangeAgents.Clear();
            _mediumRangeAgents.Clear();
            _distantRangeAgents.Clear();
            _closeRangeCandidates.Clear();
            _mediumRangeCandidates.Clear();
            _currentCloseRangeCount = 0;
            _currentMediumRangeCount = 0;

            Vector3 cameraPos = _camera.transform.position;

            // First pass: collect all candidates and current workflow counts
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.navigationAgent == null)
                    continue;

                if (agent.isDead)
                {
                    if (shouldUseRagdollCallback != null && shouldUseRagdollCallback(agent))
                    {
                        forceBoneTrackerCallback?.Invoke(agent);
                        FlushPendingGpuAnimationCalls(agent);
                    }
                    continue;
                }

                Vector3 agentPos = agent.WorldPosition;
                float sqrDistance = (agentPos - cameraPos).sqrMagnitude;
                bool isVisible = agent.isVisible;

                // Count current workflow usage
                if (agent.currentWorkflow == _settings.closeRangeWorkflow && _settings.closeRangeWorkflow != HybridWorkflowType.ComputeAnimator)
                    _currentCloseRangeCount++;
                else if (agent.currentWorkflow == _settings.mediumRangeWorkflow && _settings.mediumRangeWorkflow != HybridWorkflowType.ComputeAnimator)
                    _currentMediumRangeCount++;

                // Collect candidates for each range
                if (isVisible && sqrDistance <= _hybridCloseDistanceSqr)
                {
                    Vector3 screenPos = Vector3.zero;
                    bool inCenterScreen = IsAgentInCenterScreen(agentPos, out screenPos);
                    float priorityScore = CalculatePriorityScore(agent, sqrDistance, inCenterScreen);

                    _closeRangeCandidates.Add(new WorkflowCandidate
                    {
                        agentIndex = i,
                        sqrDistance = sqrDistance,
                        agent = agent,
                        screenPosition = screenPos,
                        isInCenterScreen = inCenterScreen,
                        priorityScore = priorityScore
                    });
                }
                else if (isVisible && sqrDistance <= _hybridMediumDistanceSqr)
                {
                    Vector3 screenPos = Vector3.zero;
                    bool inCenterScreen = IsAgentInCenterScreen(agentPos, out screenPos);
                    float priorityScore = CalculatePriorityScore(agent, sqrDistance, inCenterScreen);

                    _mediumRangeCandidates.Add(new WorkflowCandidate
                    {
                        agentIndex = i,
                        sqrDistance = sqrDistance,
                        agent = agent,
                        screenPosition = screenPos,
                        isInCenterScreen = inCenterScreen,
                        priorityScore = priorityScore
                    });
                }
                else
                {
                    // Distant/invisible agents always use distant workflow
                    _distantRangeAgents.Add(i);
                    AssignWorkflowToAgent(agent, _settings.distantRangeWorkflow, deactivateRagdollCallback, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);
                }
            }

            // Sort candidates by priority score (lower score = higher priority)
            _closeRangeCandidates.Sort((a, b) => a.priorityScore.CompareTo(b.priorityScore));
            _mediumRangeCandidates.Sort((a, b) => a.priorityScore.CompareTo(b.priorityScore));

            // Assign close range workflows with limits
            AssignWorkflowsWithLimits(_closeRangeCandidates, _settings.closeRangeWorkflow, _settings.maxCloseRangeAgents, _closeRangeAgents, deactivateRagdollCallback, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);

            // Assign medium range workflows with limits
            AssignWorkflowsWithLimits(_mediumRangeCandidates, _settings.mediumRangeWorkflow, _settings.maxMediumRangeAgents, _mediumRangeAgents, deactivateRagdollCallback, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);

            _lastHybridUpdateTime = Time.time;
        }

        #region Workflow Assignment

        private void AssignWorkflowsWithLimits(
            List<WorkflowCandidate> candidates,
            HybridWorkflowType workflowType,
            int maxAgents,
            List<int> assignedList,
            System.Action<CrowdAgent, bool> deactivateRagdollCallback,
            System.Func<CrowdAgent, float> calculateNormalizedSpeedCallback,
            System.Action<CrowdAgent, float, float> startLocomotionBlendCallback)
        {
            int assigned = 0;
            bool hasLimit = maxAgents > 0 && workflowType != HybridWorkflowType.ComputeAnimator;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                // Check if we've hit the limit for non-Compute Animator workflows
                if (hasLimit && assigned >= maxAgents)
                {
                    // Remaining candidates fall back to distant workflow
                    AssignWorkflowToAgent(candidate.agent, _settings.distantRangeWorkflow, deactivateRagdollCallback, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);
                    _distantRangeAgents.Add(candidate.agentIndex);
                }
                else
                {
                    // Assign the target workflow
                    AssignWorkflowToAgent(candidate.agent, workflowType, deactivateRagdollCallback, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);
                    assignedList.Add(candidate.agentIndex);

                    if (hasLimit)
                        assigned++;
                }
            }
        }

        /// <summary>
        /// Forces an agent to use Bone Tracker workflow immediately (used for ragdoll activation).
        /// </summary>
        public void ForceBoneTrackerWorkflow(
            CrowdAgent agent,
            System.Action<CrowdAgent, bool> deactivateRagdollCallback,
            System.Func<CrowdAgent, float> calculateNormalizedSpeedCallback,
            System.Action<CrowdAgent, float, float> startLocomotionBlendCallback)
        {
            if (agent == null)
                return;

            // Debug log removed for production build
            
            AssignWorkflowToAgent(agent, HybridWorkflowType.BoneTracker, deactivateRagdollCallback, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);
            
            // Debug log removed for production build
        }

        private void AssignWorkflowToAgent(
            CrowdAgent agent,
            HybridWorkflowType targetWorkflow,
            System.Action<CrowdAgent, bool> deactivateRagdollCallback,
            System.Func<CrowdAgent, float> calculateNormalizedSpeedCallback,
            System.Action<CrowdAgent, float, float> startLocomotionBlendCallback)
        {
            if (agent == null)
                return;

            if (agent.currentWorkflow == targetWorkflow)
            {
                // Debug log removed for production build
                return;
            }

            HybridWorkflowType previousWorkflow = agent.currentWorkflow;
            // Debug log removed for production build

            // Flush pending GPU calls before switching workflows
            if (DoesWorkflowSupportGpuAnimations(previousWorkflow) && !DoesWorkflowSupportGpuAnimations(targetWorkflow))
            {
                FlushPendingGpuAnimationCalls(agent);
            }

            ApplyHybridWorkflow(agent, targetWorkflow);
            agent.currentWorkflow = targetWorkflow;

            if (!agent.isDead)
            {
                if (targetWorkflow != HybridWorkflowType.BoneTracker)
                    deactivateRagdollCallback?.Invoke(agent, true);

                agent.lastAliveWorkflow = targetWorkflow;
            }

            // Synchronize animation state when switching workflows
            SynchronizeAnimationStateAfterWorkflowChange(agent, previousWorkflow, targetWorkflow, calculateNormalizedSpeedCallback, startLocomotionBlendCallback);

            OnWorkflowChanged?.Invoke(agent, previousWorkflow, targetWorkflow);
        }

        private void ApplyHybridWorkflow(CrowdAgent agent, HybridWorkflowType workflowType)
        {
            if (agent?.crowdInstance == null)
            {
                Debug.LogWarning($"[ApplyHybridWorkflow] Agent has null crowdInstance, cannot apply workflow");
                return;
            }

            int previousWorkflowId = agent.crowdInstance.AnimatorWorkflowID;

            switch (workflowType)
            {
                case HybridWorkflowType.BoneTracker:
                    // Setting AnimatorWorkflowID triggers OnInstancingStatusModified which calls
                    // SetupCrowdInstanceForSkinning, which handles LoadBoneTransforms and SetAllBonesRead
                    agent.crowdInstance.AnimatorWorkflowID = GPUIAWBoneTracker.WORKFLOW_ID;
                    if (agent.mecanimAnimator != null)
                    {
                        agent.mecanimAnimator.enabled = true;
                        // CRITICAL: Ensure animator updates even when off-screen
                        // Bone Tracker reads bone transforms which requires animator to actually run
                        agent.mecanimAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                    // Debug log removed for production build
                    break;

                case HybridWorkflowType.MecanimReader:
                    agent.crowdInstance.AnimatorWorkflowID = GPUIAWMecanimReader.WORKFLOW_ID;
                    if (agent.crowdInstance.gameObject != null && !GPUICrowdSkinningSystem.IsAnimatorWorkflowBoneDataProvider(GPUIAWMecanimReader.WORKFLOW_ID))
                        AnimatorUtility.OptimizeTransformHierarchy(agent.crowdInstance.gameObject, null);
                    if (agent.mecanimAnimator != null)
                        agent.mecanimAnimator.enabled = true;
                    break;

                case HybridWorkflowType.ComputeAnimator:
                    agent.crowdInstance.AnimatorWorkflowID = GPUIAWComputeAnimator.WORKFLOW_ID;
                    if (agent.mecanimAnimator != null)
                        agent.mecanimAnimator.enabled = false;
                    break;
            }
        }

        #endregion

        #region Animation Synchronization

        private void SynchronizeAnimationStateAfterWorkflowChange(
            CrowdAgent agent,
            HybridWorkflowType previousWorkflow,
            HybridWorkflowType newWorkflow,
            System.Func<CrowdAgent, float> calculateNormalizedSpeedCallback,
            System.Action<CrowdAgent, float, float> startLocomotionBlendCallback)
        {
            if (agent?.mecanimAnimator == null)
                return;

            float currentNormalizedSpeed = calculateNormalizedSpeedCallback != null ? calculateNormalizedSpeedCallback(agent) : 0f;

            // Switching TO Mecanim-based workflow
            bool newWorkflowUsesMemcanim = newWorkflow == HybridWorkflowType.MecanimReader || newWorkflow == HybridWorkflowType.BoneTracker;
            bool previousWorkflowUsedGpu = DoesWorkflowSupportGpuAnimations(previousWorkflow);

            if (newWorkflowUsesMemcanim)
            {
                agent.mecanimAnimator.SetFloat("Speed", currentNormalizedSpeed);

                if (previousWorkflowUsedGpu)
                {
                    agent.cachedBlendWeight = -1f;

                    if (agent.currentState == CrowdAnimationState.Locomotion && currentNormalizedSpeed > LOCOMOTION_EXIT_THRESHOLD)
                    {
                        agent.mecanimAnimator.SetFloat("Speed", currentNormalizedSpeed);
                    }
                    else if (agent.currentState == CrowdAnimationState.Idle || currentNormalizedSpeed <= LOCOMOTION_EXIT_THRESHOLD)
                    {
                        agent.mecanimAnimator.SetFloat("Speed", 0f);
                    }
                }
            }

            // Switching FROM Mecanim TO GPU workflow
            bool previousWorkflowUsedMecanim = previousWorkflow == HybridWorkflowType.MecanimReader || previousWorkflow == HybridWorkflowType.BoneTracker;
            bool newWorkflowUsesGpu = DoesWorkflowSupportGpuAnimations(newWorkflow);

            if (previousWorkflowUsedMecanim && newWorkflowUsesGpu)
            {
                agent.cachedBlendWeight = -1f;

                if (agent.currentState == CrowdAnimationState.Locomotion && currentNormalizedSpeed > 0f)
                {
                    startLocomotionBlendCallback?.Invoke(agent, currentNormalizedSpeed, 0.2f);
                }
                else if (agent.currentState == CrowdAnimationState.Idle)
                {
                    if (IsGpuCrowdReady(agent) && _animationSettings.idleClip != null && newWorkflowUsesGpu &&
                        newWorkflow != HybridWorkflowType.BoneTracker && newWorkflow != HybridWorkflowType.MecanimReader)
                    {
                        int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                        if (newWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                        {
                            // Realign instance workflow to ComputeAnimator if possible
                            if (agent.crowdInstance != null)
                            {
                                agent.crowdInstance.AnimatorWorkflowID = GPUIAWComputeAnimator.WORKFLOW_ID;
                                actualWorkflowId = agent.crowdInstance.AnimatorWorkflowID;
                            }

                            if (newWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                            {
                                Debug.LogError($"[EnemyMasses] GPU hybrid idle blocked: agent={agent.index} currentWorkflow={agent.currentWorkflow} newWorkflow={newWorkflow} actualWorkflowId={actualWorkflowId}");
                                return;
                            }
                        }

                        agent.crowdInstance.StartAnimation(_animationSettings.idleClip, -1f, 1f, 0.2f, null, true);
                    }
                }
            }
        }

        private void FlushPendingGpuAnimationCalls(CrowdAgent agent)
        {
            if (agent?.crowdInstance == null)
                return;

            agent.cachedBlendWeight = -1f;
            agent.idleAnimationWasGpu = false;
            agent.lastIdleAnimationHash = -1;

            // Don't overwrite flight animation states — the flight system manages its own transitions
            if (!agent.isFlying && agent.currentState != CrowdAnimationState.FlightTakeOff &&
                agent.currentState != CrowdAnimationState.Flying && agent.currentState != CrowdAnimationState.FlightLanding)
            {
                agent.currentState = CrowdAnimationState.Idle;
            }
        }

        #endregion

        #region Priority Calculation

        private bool IsAgentInCenterScreen(Vector3 worldPosition, out Vector3 screenPosition)
        {
            screenPosition = Vector3.zero;

            if (_camera == null)
                return false;

            screenPosition = _camera.WorldToScreenPoint(worldPosition);

            // Check if the point is within screen bounds
            if (screenPosition.z <= 0 || screenPosition.x < 0 || screenPosition.x > Screen.width ||
                screenPosition.y < 0 || screenPosition.y > Screen.height)
                return false;

            if (!_settings.enableCenterScreenPriority)
                return false;

            // Calculate center screen bounds
            float screenCenterX = Screen.width * 0.5f;
            float screenCenterY = Screen.height * 0.5f;
            float centerWidth = Screen.width * _settings.centerScreenWidth * 0.5f;
            float centerHeight = Screen.height * _settings.centerScreenHeight * 0.5f;

            // Check if agent is within center screen area
            bool inCenterX = Mathf.Abs(screenPosition.x - screenCenterX) <= centerWidth;
            bool inCenterY = Mathf.Abs(screenPosition.y - screenCenterY) <= centerHeight;

            return inCenterX && inCenterY;
        }

        private float CalculatePriorityScore(CrowdAgent agent, float sqrDistance, bool isInCenterScreen)
        {
            float priorityScore = sqrDistance;

            if (_settings.enableCenterScreenPriority && isInCenterScreen)
            {
                priorityScore /= _settings.centerScreenPriorityBoost;
            }

            return priorityScore;
        }

        #endregion

        #region Helper Methods

        private static bool DoesWorkflowSupportGpuAnimations(HybridWorkflowType workflowType)
        {
            // Only ComputeAnimator has an internal GPU animator system
            return workflowType != HybridWorkflowType.BoneTracker &&
                   workflowType != HybridWorkflowType.MecanimReader;
        }

        private static bool IsGpuCrowdReady(CrowdAgent agent)
        {
            if (agent?.crowdInstance == null)
                return false;

            var prefabComponent = agent.crowdInstance.PrefabComponent;
            if (prefabComponent == null || !agent.crowdInstance.IsInstanced)
                return false;

            if (!prefabComponent.IsInstanced || prefabComponent.renderKey == 0)
                return false;
            
            // Validate buffer bounds to prevent "bufferIndex out of bounds" errors
            if (!IsBufferIndexValid(prefabComponent.renderKey, prefabComponent.bufferIndex))
                return false;
            
            // Validate crowd data exists to prevent "Can not find Crowd data for renderer" errors
            if (!GPUICrowdSkinningSystem.IsActive)
                return false;
            if (!GPUICrowdSkinningSystem.TryGetCrowdRenderSource(prefabComponent.renderKey, out _))
                return false;

            return agent.crowdInstance.AnimatorWorkflow != null;
        }

        /// <summary>
        /// Validates that the buffer index is within the current buffer size for the given render key.
        /// </summary>
        private static bool IsBufferIndexValid(int renderKey, int bufferIndex)
        {
            if (bufferIndex < 0)
                return false;
                
            if (!GPUIRenderingSystem.IsActive)
                return false;
                
            if (!GPUIRenderingSystem.TryGetRenderSource(renderKey, out var renderSource))
                return false;
                
            return bufferIndex < renderSource.bufferSize;
        }

        #endregion
    }
}
