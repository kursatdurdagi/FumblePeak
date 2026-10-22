using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages locomotion and animation state for crowd agents.
    /// Handles GPU-based animations (ComputeAnimator) and Mecanim-based animations (BoneTracker/MecanimReader).
    /// </summary>
    public class EnemyMassesAnimationController
    {
        private readonly AnimationSettings _settings;
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        
        private bool _needsReset;
        private HashSet<int> _loggedMissingAnimatorStates = new HashSet<int>();
        private HashSet<int> _loggedMissingAnimatorParameters = new HashSet<int>();
        private HashSet<int> _validatedAnimatorParameters = new HashSet<int>();
        private readonly Dictionary<int, int> _boneTrackerDebugLastFrame = new Dictionary<int, int>();
        
        // Cached values for performance
        private string _cachedLocomotionStateName;
        private int _cachedLocomotionStateHash;
        
        private const float LOCOMOTION_ENTER_THRESHOLD = 0.05f;
        private const float LOCOMOTION_EXIT_THRESHOLD = 0.02f;
        private const bool ENABLE_BONE_TRACKER_DEBUG = true;
        
        /// <summary>
        /// Optional callback to get animation blend weight override for civil agents.
        /// Returns a value 0-1 where 0=idle, 0.5=walk, 1.0=run, or null to use default calculation.
        /// </summary>
        public System.Func<int, float?> GetCivilAnimationBlendCallback { get; set; }
        
        /// <summary>
        /// Optional callback to check if a civil agent is currently corner smoothing.
        /// When true, the animation system should skip isStopped checks since NavAgent is manually controlled.
        /// </summary>
        public System.Func<int, bool> IsCivilAgentCornerSmoothingCallback { get; set; }

        /// <summary>
        /// Creates a new animation controller.
        /// </summary>
        public EnemyMassesAnimationController(
            AnimationSettings settings,
            List<CrowdAgent> agents,
            List<EnemyCrowd> enemyCrowds)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
            _enemyCrowds = enemyCrowds ?? throw new System.ArgumentNullException(nameof(enemyCrowds));
        }

        /// <summary>
        /// Calculates normalized speed (0-1) for an agent based on navigation agent velocity.
        /// Uses the INavigationAgent abstraction to support both Unity NavMesh and A* Pathfinding.
        /// For civil agents, uses the civil animation blend callback to get proper walk/run blend.
        /// </summary>
        public float CalculateNormalizedSpeed(CrowdAgent agent)
        {
            if (agent == null || agent.navigationAgent == null)
                return 0f;
            
            var navAgent = agent.navigationAgent;
            
            // Check for civil agent override first
            if (GetCivilAnimationBlendCallback != null)
            {
                float? civilBlend = GetCivilAnimationBlendCallback(agent.index);
                if (civilBlend.HasValue)
                {
                    // Update smoothed speed for consistency but return the civil blend value
                    if (navAgent.IsOnNavMesh)
                    {
                        float civilTargetSpeed = navAgent.DesiredVelocity.magnitude;
                        if (civilTargetSpeed <= 0f || float.IsNaN(civilTargetSpeed))
                            civilTargetSpeed = navAgent.Velocity.magnitude;
                        float civilSpeedSmoothing = GetSpeedSmoothing(agent);
                        float delta = Mathf.Max(Time.deltaTime, 0f);
                        float civilSmoothingFactor = civilSpeedSmoothing > 0f ? 1f - Mathf.Exp(-civilSpeedSmoothing * delta) : 1f;
                        agent.smoothedSpeed = Mathf.Lerp(agent.smoothedSpeed, civilTargetSpeed, Mathf.Clamp01(civilSmoothingFactor));
                    }
                    return civilBlend.Value;
                }
            }

            // Must be on navigation surface to access velocity properties
            if (!navAgent.IsOnNavMesh)
                return 0f;
            
            float targetSpeed = navAgent.DesiredVelocity.magnitude;
            if (targetSpeed <= 0f || float.IsNaN(targetSpeed))
                targetSpeed = navAgent.Velocity.magnitude;

            // Phase 2: Use agent-specific speed smoothing
            float smoothingFactor = 1f;
            float speedSmoothing = GetSpeedSmoothing(agent);
            float smoothingFactorApplied = 1f;
            if (speedSmoothing > 0f)
            {
                float delta = Mathf.Max(Time.deltaTime, 0f);
                smoothingFactor = 1f - Mathf.Exp(-speedSmoothing * delta);
                smoothingFactorApplied = smoothingFactor;
            }
            agent.smoothedSpeed = Mathf.Lerp(agent.smoothedSpeed, targetSpeed, Mathf.Clamp01(smoothingFactor));

            if (navAgent.Speed <= Mathf.Epsilon)
                return 0f;

            float normalized = Mathf.Clamp01(agent.smoothedSpeed / navAgent.Speed);
            
            return normalized;
        }

        /// <summary>
        /// Handles animation state transitions and updates for an agent.
        /// </summary>
        public void HandleAnimation(CrowdAgent agent, float normalizedSpeed, bool enableCombatSystem)
        {
            if (agent == null || agent.crowdInstance == null)
            {
                //Debug.LogWarning("[Animation] Agent or crowdInstance is null!");
                return;
            }

            // Always skip dead agents - prevents locomotion overwriting death animation
            if (agent.isDead)
                return;

            // Skip locomotion updates while flying - flight animation is handled by the controller's flight system
            // IMPORTANT: This MUST be checked BEFORE needsAnimationRefresh so that ForceAnimationRefresh
            // cannot overwrite flight states (FlightTakeOff / FlightLanding) to Idle, which would prevent
            // UpdateFlightAnimations from ever detecting the transition and clearing isFlying.
            if (agent.isFlying || agent.currentState == CrowdAnimationState.FlightTakeOff ||
                agent.currentState == CrowdAnimationState.Flying || agent.currentState == CrowdAnimationState.FlightLanding)
                return;
            
            // Skip locomotion updates while climbing - climb animation is handled by NavMeshLinkTraversalHandler
            if (agent.isClimbing)
                return;

            // Handle visibility transition: force animation refresh when agent becomes visible
            // This fixes stale animation data from GPU buffer reuse (e.g., agents spawned outside frustum
            // showing dead/wrong animations when they become visible)
            if (agent.needsAnimationRefresh)
            {
                agent.needsAnimationRefresh = false;
                ForceAnimationRefresh(agent, normalizedSpeed);
            }

            // Allow airborne handling to run during impulse reactions so landing/get-up can blend
            if (agent.impulseAnimationActive)
            {
                bool playingImpulse = Time.time < agent.impulseAnimationEndTime;
                bool readyBlendOut = !agent.impulseBlendOutTriggered && Time.time >= agent.impulseAnimationBlendOutTime;
                if (agent.isAirborne || agent.landingRequested)
                {
                    if (HandleAirborneAnimations(agent))
                    {
                        return;
                    }
                }

                // Start blending out of the impulse before it ends
                if (readyBlendOut)
                {
                    agent.impulseBlendOutTriggered = true;
                    agent.impulseAnimationActive = false;
                    agent.cachedBlendWeight = -1f;

                    float syntheticSpeed = agent.hasNavmeshOffsetImpulse ? Mathf.Max(normalizedSpeed, 0.7f) : normalizedSpeed;
                    bool wantsLocomotionBlendOut = syntheticSpeed > LOCOMOTION_ENTER_THRESHOLD;
                    if (wantsLocomotionBlendOut)
                    {
                        StartLocomotionBlend(agent, syntheticSpeed, 0.2f);
                        agent.currentState = CrowdAnimationState.Locomotion;
                        agent.hasArrived = false;
                    }
                    else
                    {
                        EnsureIdleAnimation(agent, 0.2f);
                        agent.currentState = CrowdAnimationState.Idle;
                        agent.hasArrived = true;
                    }
                    return;
                }

                if (playingImpulse)
                {
                    return;
                }

                agent.impulseAnimationActive = false;
                agent.cachedBlendWeight = -1f;
                float syntheticSpeedEnd = agent.hasNavmeshOffsetImpulse ? Mathf.Max(normalizedSpeed, 0.7f) : normalizedSpeed;
                bool wantsLocomotion = syntheticSpeedEnd > LOCOMOTION_ENTER_THRESHOLD;
                if (wantsLocomotion)
                {
                    StartLocomotionBlend(agent, syntheticSpeedEnd, 0.2f); // crossfade back into locomotion immediately
                    agent.currentState = CrowdAnimationState.Locomotion;
                    agent.hasArrived = false;
                }
                else
                {
                    EnsureIdleAnimation(agent, 0.2f);
                    agent.currentState = CrowdAnimationState.Idle;
                    agent.hasArrived = true;
                }
                return;
            }

            // Skip when ragdoll is active (combat system)
            if (enableCombatSystem && agent.ragdollActivated)
                return;

            // Airborne state has priority over locomotion/combat blends
            if (HandleAirborneAnimations(agent))
                return;

            // Skill cast lock priority: while a cast movement lock is active, do not let
            // locomotion/idle updates override the currently playing skill animation.
            if (agent.skillMovementLockEndTime > 0f)
            {
                if (Time.time < agent.skillMovementLockEndTime)
                {
                    if (agent.mecanimAnimator != null)
                    {
                        // Keep locomotion blend parameters at idle while cast lock is active.
                        UpdateMecanimLocomotionParameters(agent, 0f, true);
                    }
                    return;
                }

                // Clear expired lock timestamp.
                agent.skillMovementLockEndTime = 0f;
            }

            // Skip certain combat states entirely
            if (enableCombatSystem)
            {
                switch (agent.combatState)
                {
                    case CrowdCombatState.Attacking:
                    case CrowdCombatState.Blocking:
                    case CrowdCombatState.Hit:
                    case CrowdCombatState.Dead:
                        return;
                }
            }

            // Check if current workflow supports GPU animations
            bool workflowSupportsGpuAnimations = DoesWorkflowSupportGpuAnimations(agent.currentWorkflow);
            bool useGpuAnimations = _settings.controlAnimationsWithGPUI && workflowSupportsGpuAnimations && IsGpuCrowdReady(agent);

            // Safety: MecanimReader/BoneTracker should never reach GPU path
            // This catches workflow desync issues and auto-corrects
            if (useGpuAnimations)
            {
                int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                if (agent.currentWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                {
                    // Auto-correct the workflow mismatch instead of just blocking
                    if (actualWorkflowId == GPUIAWBoneTracker.WORKFLOW_ID)
                        agent.currentWorkflow = HybridWorkflowType.BoneTracker;
                    else if (actualWorkflowId == GPUIAWMecanimReader.WORKFLOW_ID)
                        agent.currentWorkflow = HybridWorkflowType.MecanimReader;
                    
                    // Only log once per agent
                    if (!agent.workflowMismatchWarned)
                    {
                        agent.workflowMismatchWarned = true;
                        Debug.LogWarning($"[EnemyMasses] Workflow mismatch auto-corrected: agent={agent.index} was {agent.currentWorkflow}, GPU workflow={actualWorkflowId}. Using Mecanim animations.");
                    }
                    useGpuAnimations = false;
                }
            }

            if (!useGpuAnimations)
            {
                bool hasReachedDest = HasReachedDestination(agent);
                bool forceIdle = hasReachedDest || agent.hasArrived;
                
                // Mirror GPU state handling so Mecanim (BoneTracker/MecanimReader) transitions correctly.
                CrowdAnimationState nextStateMecanim;
                if (forceIdle)
                    nextStateMecanim = CrowdAnimationState.Idle;
                else if (agent.currentState == CrowdAnimationState.Locomotion)
                    nextStateMecanim = normalizedSpeed > LOCOMOTION_EXIT_THRESHOLD ? CrowdAnimationState.Locomotion : CrowdAnimationState.Idle;
                else
                    nextStateMecanim = normalizedSpeed >= LOCOMOTION_ENTER_THRESHOLD ? CrowdAnimationState.Locomotion : CrowdAnimationState.Idle;

                bool stateChangedMecanim = nextStateMecanim != agent.currentState || _needsReset;

                if (stateChangedMecanim)
                {
                    agent.currentState = nextStateMecanim;
                    agent.cachedBlendWeight = -1f;

                    switch (nextStateMecanim)
                    {
                        case CrowdAnimationState.Idle:
                            UpdateMecanimLocomotionParameters(agent, 0f, true);
                            agent.hasArrived = true;
                            break;
                        case CrowdAnimationState.Locomotion:
                            StartLocomotionBlend(agent, normalizedSpeed, 0.35f);
                            agent.hasArrived = false;
                            break;
                    }

                    return;
                }

                if (nextStateMecanim == CrowdAnimationState.Locomotion)
                {
                    agent.hasArrived = false;
                    StartLocomotionBlend(agent, normalizedSpeed, 0f);
                }
                else
                {
                    UpdateMecanimLocomotionParameters(agent, 0f, true);
                    agent.hasArrived = true;
                }

                return;
            }

            var navAgent = agent.navigationAgent;
            
            float stopDistance = navAgent != null ? navAgent.StoppingDistance + 0.05f : 0.05f;
            bool reachedDestination = navAgent != null && navAgent.IsOnNavMesh && navAgent.HasPath && !navAgent.PathPending && navAgent.RemainingDistance <= stopDistance;
            
            // Also consider agent as stopped if:
            // 1. Navigation agent IsStopped is true (BUT NOT if corner smoothing - we manually control NavAgent then)
            // 2. Velocity is near zero (covers BattleLine agents who have their path reset)
            // 3. Agent's smoothedSpeed is near zero (animation system already thinks they're stopped)
            if (!reachedDestination && navAgent != null && navAgent.IsOnNavMesh)
            {
                // Check if this agent is corner smoothing - if so, skip isStopped check
                // because during corner smoothing we set isStopped=true to take manual control
                bool isCornerSmoothing = IsCivilAgentCornerSmoothingCallback?.Invoke(agent.index) ?? false;
                
                bool isStopped = navAgent.IsStopped && !isCornerSmoothing; // Ignore isStopped during corner smoothing
                bool pathPlanned = navAgent.HasPath || navAgent.PathPending;
                bool hasLowVelocity = navAgent.Velocity.sqrMagnitude < 0.01f; // Less than 0.1 m/s
                bool hasLowSmoothedSpeed = agent.smoothedSpeed < 0.05f;

                // Treat as arrived only when explicitly stopped with no outstanding travel, or when we have no path and movement is negligible.
                if (isStopped)
                {
                    // If we still have a path and are beyond stop distance, do not consider arrived (auto-resume happens in navigation).
                    if (!(pathPlanned && !navAgent.PathPending && navAgent.RemainingDistance > stopDistance))
                    {
                        reachedDestination = true;
                    }
                }
                else if (!pathPlanned && !isCornerSmoothing && hasLowVelocity && hasLowSmoothedSpeed)
                {
                    reachedDestination = true;
                }
            }

            CrowdAnimationState nextState;
            if (reachedDestination)
                nextState = CrowdAnimationState.Idle;
            else if (agent.currentState == CrowdAnimationState.Locomotion)
                nextState = normalizedSpeed > LOCOMOTION_EXIT_THRESHOLD ? CrowdAnimationState.Locomotion : CrowdAnimationState.Idle;
            else
                nextState = normalizedSpeed >= LOCOMOTION_ENTER_THRESHOLD ? CrowdAnimationState.Locomotion : CrowdAnimationState.Idle;

            bool stateChanged = nextState != agent.currentState || _needsReset;

            if (stateChanged)
            {
                agent.currentState = nextState;
                agent.cachedBlendWeight = -1f;

                switch (nextState)
                {
                    case CrowdAnimationState.Idle:
                        // Additional safety check to prevent GPU calls on non-GPU workflows
                        if (useGpuAnimations && agent.currentWorkflow != HybridWorkflowType.BoneTracker &&
                            agent.currentWorkflow != HybridWorkflowType.MecanimReader)
                        {
                            // Double-check GPU readiness right before API call to prevent race conditions
                            if (IsGpuCrowdReady(agent))
                            {
                                var clips = GetLocomotionClipsForAgent(agent);
                                if (clips.idle != null)
                                {
                                    // Safety check: Ensure AnimatorWorkflow is valid (prevents errors on play mode exit)
                                    if (agent.crowdInstance.AnimatorWorkflow == null)
                                        return;
                                        
                                    agent.crowdInstance.StartAnimation(clips.idle, -1f, 1f, 0.5f, null, true);
                                }
                            }
                            else if (agent.mecanimAnimator != null)
                            {
                                // Fallback to Mecanim if GPU became invalid
                                UpdateMecanimLocomotionParameters(agent, 0f, true);
                            }
                        }
                        else if (agent.mecanimAnimator != null)
                        {
                            UpdateMecanimLocomotionParameters(agent, 0f, true);
                        }
                        agent.hasArrived = true;
                        break;
                    case CrowdAnimationState.Locomotion:
                        StartLocomotionBlend(agent, normalizedSpeed, 0.35f);
                        agent.hasArrived = false;
                        break;
                }

                return;
            }

            if (nextState == CrowdAnimationState.Locomotion)
            {
                agent.hasArrived = false;
                StartLocomotionBlend(agent, normalizedSpeed, 0f);
            }
        }

        private bool HandleAirborneAnimations(CrowdAgent agent)
        {
            if (agent == null)
                return false;

            float now = Time.time;
            var (fallClip, landingClip) = GetAirborneClipsForAgent(agent);
            AnimationClip getUpClip = GetLiftGetUpClipForAgent(agent);
            bool debugAir = agent.enemyCrowd != null && agent.enemyCrowd.enableAirborneDebugLogs;

            // Keep playing landing or get-up clip until it finishes
            if (agent.currentState == CrowdAnimationState.Landing)
            {
                if (agent.landingClipEndTime > now)
                    return true;

                agent.currentState = CrowdAnimationState.Idle;
                agent.landingClipEndTime = 0f;
                if (debugAir)
                    Debug.Log($"[Airborne] Landing clip finished for agent {agent.index}");
            }

            // Start landing clip when returning to ground
            if (agent.landingRequested)
            {
                agent.landingRequested = false;
                if (landingClip != null)
                {
                    if (debugAir)
                        Debug.Log($"[Airborne] Playing landing clip for agent {agent.index}: {landingClip.name}");
                    StartAirborneAnimation(agent, landingClip, false, 0.1f, debugAir);
                    agent.currentState = CrowdAnimationState.Landing;
                    float duration = Mathf.Max(landingClip.length, 0.1f);
                    agent.landingClipEndTime = now + duration;
                    if (agent.liftGetUpPending)
                        agent.impulseAttackLockEndTime = Mathf.Max(agent.impulseAttackLockEndTime, agent.landingClipEndTime);
                    return true;
                }
                else if (debugAir)
                {
                    Debug.Log($"[Airborne] Landing requested but no landing clip for agent {agent.index}");
                }
            }

            // Falling overrides locomotion/combat until grounded
            if (agent.isAirborne)
            {
                if (fallClip != null)
                {
                    if (agent.currentState != CrowdAnimationState.Falling)
                    {
                        if (debugAir)
                            Debug.Log($"[Airborne] Playing falling clip for agent {agent.index}: {fallClip.name}");
                        StartAirborneAnimation(agent, fallClip, true, 0.12f, debugAir);
                        agent.cachedBlendWeight = -1f;
                        agent.currentState = CrowdAnimationState.Falling;
                    }
                    return true;
                }
                else
                {
                    if (debugAir)
                        Debug.Log($"[Airborne] Agent {agent.index} airborne but no falling clip assigned");
                    agent.currentState = CrowdAnimationState.Falling;
                }

                return true;
            }
            else if (agent.currentState == CrowdAnimationState.Falling)
            {
                // Reset to idle when leaving falling without a landing clip
                agent.currentState = CrowdAnimationState.Idle;
            }

            // Play lift get-up after landing if pending
            if (agent.liftGetUpPending)
            {
                agent.liftGetUpPending = false;
                agent.liftImpulseActive = false;

                if (getUpClip != null)
                {
                    if (debugAir)
                        Debug.Log($"[Airborne] Playing lift get-up clip for agent {agent.index}: {getUpClip.name}");
                    StartAirborneAnimation(agent, getUpClip, false, 0.1f, debugAir);
                    agent.currentState = CrowdAnimationState.Landing;
                    float duration = Mathf.Max(getUpClip.length, 0.1f);
                    agent.landingClipEndTime = now + duration;
                    agent.impulseAttackLockEndTime = Mathf.Max(agent.impulseAttackLockEndTime, agent.landingClipEndTime);
                    return true;
                }
            }

            return false;
        }

        private void StartAirborneAnimation(CrowdAgent agent, AnimationClip clip, bool loop, float transitionTime, bool debugAir)
        {
            if (agent == null || clip == null)
                return;

            bool useGpuAnimations = _settings.controlAnimationsWithGPUI &&
                                    DoesWorkflowSupportGpuAnimations(agent.currentWorkflow) &&
                                    IsGpuCrowdReady(agent);

            // Safety: ensure the GPU workflow is active before issuing GPU calls
            if (useGpuAnimations)
            {
                int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                if (agent.currentWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                {
                    if (debugAir)
                        Debug.Log($"[Airborne] GPU path blocked for agent {agent.index}: currentWorkflow={agent.currentWorkflow} actualWorkflow={actualWorkflowId}");
                    useGpuAnimations = false;
                }
            }

            if (useGpuAnimations)
            {
                agent.cachedBlendWeight = -1f;
                agent.idleAnimationWasGpu = false;
                agent.lastIdleAnimationHash = -1;
                agent.crowdInstance.StartAnimation(clip, -1f, 1f, transitionTime, null, loop);
                if (debugAir)
                    Debug.Log($"[Airborne] GPU StartAnimation agent {agent.index} clip={clip.name} loop={loop} transition={transitionTime}");
                return;
            }

            Animator animator = agent.mecanimAnimator;
            if (animator == null)
            {
                if (debugAir)
                    Debug.Log($"[Airborne] No Mecanim animator for agent {agent.index}; cannot play {clip.name}");
                return;
            }

            int stateHash = Animator.StringToHash(clip.name);
            if (stateHash == 0)
                return;

            if (animator.HasState(0, stateHash))
            {
                animator.CrossFade(stateHash, transitionTime, 0, 0f);
                agent.lastIdleAnimationHash = stateHash;
                if (debugAir)
                    Debug.Log($"[Airborne] Mecanim CrossFade agent {agent.index} clip={clip.name} transition={transitionTime}");
            }
            else
            {
                WarnMissingAnimatorState(animator, clip.name, stateHash);
                if (debugAir)
                    Debug.Log($"[Airborne] Animator missing state for clip {clip.name} on agent {agent.index}");
            }
        }

        /// <summary>
        /// Starts or updates locomotion blend animation (walk/run).
        /// </summary>
        public void StartLocomotionBlend(CrowdAgent agent, float normalizedSpeed, float transitionTime)
        {
            var clips = GetLocomotionClipsForAgent(agent);

            if (clips.walk == null && clips.run == null)
                return;

            ResetIdleAnimationCache(agent, false);

            float clampedWeight = Mathf.Clamp01(normalizedSpeed);
            if (agent.cachedBlendWeight >= 0f && Mathf.Abs(agent.cachedBlendWeight - clampedWeight) < _settings.minBlendUpdateDelta)
                return;

            agent.cachedBlendWeight = clampedWeight;

            // Check if current workflow supports GPU animations
            bool workflowSupportsGpuAnimations = DoesWorkflowSupportGpuAnimations(agent.currentWorkflow);
            bool useGpuAnimations = workflowSupportsGpuAnimations && IsGpuCrowdReady(agent);

            // Additional safety check to prevent GPU calls on BoneTracker/MecanimReader
            if (useGpuAnimations && (agent.currentWorkflow == HybridWorkflowType.BoneTracker ||
                                   agent.currentWorkflow == HybridWorkflowType.MecanimReader))
            {
                useGpuAnimations = false;
            }

            if (useGpuAnimations)
            {
                // Safety: MecanimReader/BoneTracker should never reach GPU path
                int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                if (agent.currentWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                {
                    Debug.LogError($"[EnemyMasses] GPU locomotion blocked: agent={agent.index} currentWorkflow={agent.currentWorkflow} actualWorkflowId={actualWorkflowId}");
                    useGpuAnimations = false;
                }
                // Double-check GPU readiness right before API call to prevent race conditions
                if (!IsGpuCrowdReady(agent))
                {
                    // Fallback to Mecanim if GPU became invalid between checks
                    if (agent.mecanimAnimator != null)
                    {
                        EnsureMecanimLocomotionState(agent, transitionTime);
                        UpdateMecanimLocomotionParameters(agent, clampedWeight, false);
                    }
                    return;
                }

                // Decide between 1D and 2D GPU blending
                (AnimationClip front, AnimationClip back, AnimationClip left, AnimationClip right) directionalClips = (null, null, null, null);
                bool hasDirectional = HasDirectionalLocomotion(agent, out directionalClips);
                bool wants2D = GetLocomotionBlendMode(agent) == LocomotionBlendMode.TwoD && hasDirectional;

                float directionalThreshold = GetDirectionalLocomotionThreshold(agent);
                bool use2DThisFrame = wants2D && clampedWeight >= directionalThreshold;

                if (use2DThisFrame)
                {
                    StartGpuDirectionalLocomotionBlend(agent, clampedWeight, transitionTime, directionalClips);
                }
                else
                {
                    StartGpu1DLocomotionBlend(agent, clampedWeight, transitionTime, clips);
                }
            }
            else
            {
                // For BoneTracker and MecanimReader workflows, use Mecanim Animator
                if (agent.mecanimAnimator != null)
                {
                    EnsureMecanimLocomotionState(agent, transitionTime);
                    // Set Speed parameter for blend tree (idle=0, walk=0.5, run=1.0)
                    UpdateMecanimLocomotionParameters(agent, clampedWeight, false);
                }
            }
        }

        /// <summary>
        /// Ensures the agent is in idle animation state.
        /// </summary>
        public void EnsureIdleAnimation(CrowdAgent agent, float transitionTime = 0.18f)
        {
            if (agent == null || agent.isDead || agent.ragdollActivated)
                return;

            // Don't overwrite flight animation states — the flight system manages its own transitions
            if (agent.isFlying || agent.currentState == CrowdAnimationState.FlightTakeOff ||
                agent.currentState == CrowdAnimationState.Flying || agent.currentState == CrowdAnimationState.FlightLanding)
                return;

            agent.currentState = CrowdAnimationState.Idle;
            agent.cachedBlendWeight = -1f;

            // Get the proper idle clip (with faction overrides)
            var (idleClip, _, _) = GetLocomotionClipsForAgent(agent);

            bool useGpuAnimations = _settings.controlAnimationsWithGPUI && DoesWorkflowSupportGpuAnimations(agent.currentWorkflow) && IsGpuCrowdReady(agent);

            if (useGpuAnimations && idleClip != null)
            {
                // Safety: MecanimReader/BoneTracker should never reach GPU path
                int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                if (agent.currentWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                {
                    Debug.LogError($"[EnemyMasses] GPU idle blocked: agent={agent.index} currentWorkflow={agent.currentWorkflow} actualWorkflowId={actualWorkflowId}");
                    useGpuAnimations = false;
                }

                // Double-check GPU readiness right before API call to prevent race conditions
                if (IsGpuCrowdReady(agent))
                {
                    if (!agent.idleAnimationWasGpu)
                    {
                        agent.crowdInstance?.StartAnimation(idleClip, -1f, 1f, transitionTime, null, true);
                        agent.idleAnimationWasGpu = true;
                        agent.lastIdleAnimationHash = -1;
                    }
                }
                else if (agent.mecanimAnimator != null)
                {
                    // Fallback to Mecanim if GPU became invalid
                    agent.idleAnimationWasGpu = false;
                    UpdateMecanimLocomotionParameters(agent, 0f, true);
                }
            }
            else if (agent.mecanimAnimator != null)
            {
                // For Mecanim, always set Speed=0 to use blend tree's idle state
                agent.idleAnimationWasGpu = false;
                UpdateMecanimLocomotionParameters(agent, 0f, true);

                // If we have a locomotion blend tree, ensure it's active
                if (EnsureMecanimLocomotionState(agent, transitionTime))
                    return;

                // Only try to crossfade to explicit idle clip if one exists
                if (idleClip != null)
                {
                    int idleHash = Animator.StringToHash(idleClip.name);
                    if (!agent.mecanimAnimator.HasState(0, idleHash))
                    {
                        // State doesn't exist, Speed=0 will handle idle via blend tree
                        return;
                    }
                    if (agent.lastIdleAnimationHash != idleHash)
                    {
                        agent.mecanimAnimator.CrossFade(idleHash, transitionTime, 0, 0f);
                        agent.lastIdleAnimationHash = idleHash;
                    }
                }
            }
        }

        /// <summary>
        /// Forces an animation refresh for an agent that just became visible.
        /// This fixes stale animation data from GPU buffer reuse when agents spawn outside the frustum.
        /// Also called by the flight system to immediately restore ground animations after landing.
        /// </summary>
        public void ForceAnimationRefresh(CrowdAgent agent, float normalizedSpeed, float transitionTime = 0f)
        {
            if (agent == null || agent.crowdInstance == null)
                return;
                
            // Clear all animation caches to force a fresh state
            agent.cachedBlendWeight = -1f;
            agent.lastIdleAnimationHash = -1;
            agent.idleAnimationWasGpu = false;
            agent.currentState = CrowdAnimationState.Idle; // Reset to idle, will transition to correct state
            
            // Get the appropriate animation clip based on current movement
            var (idleClip, walkClip, runClip) = GetLocomotionClipsForAgent(agent);
            
            bool useGpu = _settings.controlAnimationsWithGPUI && 
                          DoesWorkflowSupportGpuAnimations(agent.currentWorkflow) && 
                          IsGpuCrowdReady(agent);
            
            if (useGpu)
            {
                // Force GPU animation — use provided transitionTime for smooth blending
                // from the previous clip (e.g. flight landing → ground locomotion).
                AnimationClip targetClip;
                if (normalizedSpeed >= LOCOMOTION_ENTER_THRESHOLD)
                {
                    // Use walk or run based on speed
                    if (normalizedSpeed >= 0.7f && runClip != null)
                        targetClip = runClip;
                    else if (walkClip != null)
                        targetClip = walkClip;
                    else
                        targetClip = idleClip;
                    agent.currentState = CrowdAnimationState.Locomotion;
                }
                else
                {
                    targetClip = idleClip;
                    agent.currentState = CrowdAnimationState.Idle;
                }
                
                if (targetClip != null && agent.crowdInstance.AnimatorWorkflow != null)
                {
                    agent.crowdInstance.StartAnimation(targetClip, 0f, 1f, transitionTime, null, true);
                }
            }
            else if (agent.mecanimAnimator != null)
            {
                // Force Mecanim to correct state
                float speedParam = normalizedSpeed >= LOCOMOTION_ENTER_THRESHOLD ? normalizedSpeed : 0f;
                agent.mecanimAnimator.SetFloat("Speed", speedParam);
                agent.currentState = speedParam > 0 ? CrowdAnimationState.Locomotion : CrowdAnimationState.Idle;
            }
        }

        /// <summary>
        /// Resets cached animation state to force animation updates.
        /// </summary>
        public void ResetAnimations()
        {
            _needsReset = true;
        }

        /// <summary>
        /// Synchronizes animation states for all agents (useful after workflow changes).
        /// </summary>
        public void SynchronizeAllAnimationStates()
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent?.mecanimAnimator == null)
                    continue;

                float currentSpeed = CalculateNormalizedSpeed(agent);
                bool workflowUsesMemcanim = agent.currentWorkflow == HybridWorkflowType.MecanimReader ||
                                          agent.currentWorkflow == HybridWorkflowType.BoneTracker;

                if (workflowUsesMemcanim)
                {
                    // Synchronize Speed parameter with current movement
                    bool hasReachedDest = agent.hasArrived || agent.currentState == CrowdAnimationState.Idle;
                    UpdateMecanimLocomotionParameters(agent, currentSpeed, hasReachedDest);
                }

                // Clear cached blend weight to force update on next animation cycle
                agent.cachedBlendWeight = -1f;
            }
        }

        /// <summary>
        /// Checks if an agent should have its animation processed this frame.
        /// </summary>
        public bool ShouldProcessAnimation(CrowdAgent agent, Camera camera, Transform animationCullReference)
        {
            if (agent == null)
                return false;

            if (!_settings.controlAnimationsWithGPUI && agent.mecanimAnimator != null)
                return true;

            if (!_settings.cullAnimationsByDistance)
                return true;

            Transform reference = animationCullReference != null ? animationCullReference : camera?.transform;
            if (reference == null)
                return true;

            Transform agentTransform = null;
            if (agent.crowdInstance != null)
                agentTransform = agent.crowdInstance.transform;
            else if (agent.navigationAgent != null)
                agentTransform = agent.navigationAgent.Transform;

            if (agentTransform == null)
                return true;

            float sqrDistance = (agentTransform.position - reference.position).sqrMagnitude;
            float animationCullDistanceSqr = _settings.animationCullDistance * _settings.animationCullDistance;
            return sqrDistance <= animationCullDistanceSqr;
        }

        #region Helper Methods

        private bool HasReachedDestination(CrowdAgent agent)
        {
            var navAgent = agent?.navigationAgent;
            if (navAgent == null || !navAgent.IsOnNavMesh)
                return false;

            float stopDistance = navAgent.StoppingDistance + 0.05f;
            if (navAgent.HasPath && !navAgent.PathPending)
            {
                if (navAgent.RemainingDistance <= stopDistance)
                    return true;
            }

            bool isStopped = navAgent.IsStopped;
            bool pathPlanned = navAgent.HasPath || navAgent.PathPending;
            bool hasLowVelocity = navAgent.Velocity.sqrMagnitude < 0.01f;
            bool hasLowSmoothedSpeed = agent != null && agent.smoothedSpeed < 0.05f;

            if (!pathPlanned && hasLowVelocity && hasLowSmoothedSpeed)
                return true;

            // If stopped but still have a path and are beyond stopping distance, do not treat as arrived.
            if (isStopped && pathPlanned)
            {
                if (!navAgent.PathPending && navAgent.HasPath && navAgent.RemainingDistance > stopDistance)
                    return false;
            }

            return isStopped;
        }

        private void UpdateMecanimLocomotionParameters(CrowdAgent agent, float normalizedSpeed, bool hasReachedDestination)
        {
            var animator = agent?.mecanimAnimator;
            if (animator == null)
                return;

            var (speedParam, dirXParam, dirYParam) = GetLocomotionParameterNames(agent);
            float speedValue = hasReachedDestination ? 0f : normalizedSpeed;
            TrySetAnimatorFloat(animator, speedParam, speedValue);

            bool useTwoD = GetLocomotionBlendMode(agent) == LocomotionBlendMode.TwoD;
            Vector2 planar = Vector2.zero;

            if (!useTwoD)
            {
                return;
            }

            var navAgent = agent.navigationAgent;
            if (navAgent != null)
            {
                Vector3 velocity = navAgent.DesiredVelocity;
                if (velocity.sqrMagnitude < 0.0001f)
                    velocity = navAgent.Velocity;

                Transform basis = navAgent.Transform != null ? navAgent.Transform : agent.crowdInstance?.transform;
                if (basis != null)
                {
                    Vector3 localVelocity = basis.InverseTransformDirection(velocity);
                    planar = new Vector2(localVelocity.x, localVelocity.z);
                }

                if (navAgent.Speed > Mathf.Epsilon)
                    planar /= navAgent.Speed;
            }

            if (hasReachedDestination)
                planar = Vector2.zero;

            planar = Vector2.ClampMagnitude(planar, 1f);

            TrySetAnimatorFloat(animator, dirXParam, planar.x);
            TrySetAnimatorFloat(animator, dirYParam, planar.y);

            // Debug logging removed.
        }

        private void StartGpu1DLocomotionBlend(CrowdAgent agent, float clampedWeight, float transitionTime, (AnimationClip idle, AnimationClip walk, AnimationClip run) clips)
        {
            // Use GPU-based animation for ComputeAnimator workflow
            if (clips.walk != null && clips.run != null)
            {
                float runWeight = Mathf.Clamp01(Mathf.InverseLerp(0.45f, 0.9f, clampedWeight));
                Vector4 weights = Vector4.zero;
                weights.x = 1f - runWeight;
                weights.y = runWeight;

                if (agent.crowdInstance.AnimatorWorkflow == null)
                    return;

                GPUICrowdAPI.StartBlend(
                    agent.crowdInstance,
                    weights,
                    clips.walk,
                    clips.run,
                    null,
                    null,
                    null,
                    null,
                    transitionTime,
                    null,
                    true
                );
            }
            else
            {
                AnimationClip clip = clips.run != null ? clips.run : clips.walk;
                if (clip == null)
                    return;

                if (agent.crowdInstance.AnimatorWorkflow == null)
                    return;

                agent.crowdInstance.StartAnimation(clip, -1f, Mathf.Lerp(0.5f, 1.5f, clampedWeight), transitionTime, null, true);
            }
        }

        private void StartGpuDirectionalLocomotionBlend(CrowdAgent agent, float clampedWeight, float transitionTime, (AnimationClip front, AnimationClip back, AnimationClip left, AnimationClip right) directionalClips)
        {
            if (agent == null || agent.crowdInstance == null)
                return;

            // Require at least forward and side clips to avoid degenerate blends
            if (directionalClips.front == null && directionalClips.back == null && directionalClips.left == null && directionalClips.right == null)
                return;

            Vector2 planar = GetPlanarMotionVector(agent);
            float magnitude = planar.magnitude;
            if (magnitude < 0.001f)
            {
                // Too small to meaningfully blend directionally; fallback to idle or 1D
                var locClips = GetLocomotionClipsForAgent(agent);
                StartGpu1DLocomotionBlend(agent, 0f, transitionTime, locClips);
                return;
            }

            float ax = Mathf.Abs(planar.x);
            float az = Mathf.Abs(planar.y);
            float sum = ax + az;
            if (sum < 0.001f)
                sum = 1f;

            float wf = planar.y > 0f ? az / sum : 0f;
            float wb = planar.y < 0f ? az / sum : 0f;
            float wr = planar.x > 0f ? ax / sum : 0f;
            float wl = planar.x < 0f ? ax / sum : 0f;

            // Redirect missing clips to available ones to avoid zeros
            ResolveDirectionalFallbacks(ref wf, ref wb, ref wl, ref wr, directionalClips);

            Vector4 weights = new Vector4(wf, wr, wb, wl);

            if (agent.crowdInstance.AnimatorWorkflow == null)
                return;

            GPUICrowdAPI.StartBlend(
                agent.crowdInstance,
                weights,
                directionalClips.front,
                directionalClips.right,
                directionalClips.back,
                directionalClips.left,
                null,
                null,
                transitionTime,
                null,
                true
            );
        }

        private void ResolveDirectionalFallbacks(
            ref float wf,
            ref float wb,
            ref float wl,
            ref float wr,
            (AnimationClip front, AnimationClip back, AnimationClip left, AnimationClip right) clips)
        {
            float total = wf + wb + wl + wr;
            if (total < 0.001f)
                return;

            // Reassign weights to available directions if missing
            if (wf > 0f && clips.front == null)
            {
                float redistribute = wf;
                wf = 0f;
                wb += redistribute * 0.5f;
                if (clips.left != null) wl += redistribute * 0.25f;
                if (clips.right != null) wr += redistribute * 0.25f;
            }
            if (wb > 0f && clips.back == null)
            {
                float redistribute = wb;
                wb = 0f;
                wf += redistribute * 0.5f;
                if (clips.left != null) wl += redistribute * 0.25f;
                if (clips.right != null) wr += redistribute * 0.25f;
            }
            if (wl > 0f && clips.left == null)
            {
                float redistribute = wl;
                wl = 0f;
                wf += redistribute * 0.5f;
                wb += redistribute * 0.5f;
            }
            if (wr > 0f && clips.right == null)
            {
                float redistribute = wr;
                wr = 0f;
                wf += redistribute * 0.5f;
                wb += redistribute * 0.5f;
            }

            float newTotal = wf + wb + wl + wr;
            if (newTotal <= 0f)
            {
                wf = 1f; wb = wl = wr = 0f;
            }
            else
            {
                float inv = 1f / newTotal;
                wf *= inv; wb *= inv; wl *= inv; wr *= inv;
            }
        }

        private Vector2 GetPlanarMotionVector(CrowdAgent agent)
        {
            var navAgent = agent?.navigationAgent;
            if (navAgent == null)
                return Vector2.zero;

            Vector3 velocity = navAgent.DesiredVelocity;
            if (velocity.sqrMagnitude < 0.0001f)
                velocity = navAgent.Velocity;

            Transform basis = navAgent.Transform != null ? navAgent.Transform : agent.crowdInstance?.transform;
            if (basis != null)
            {
                Vector3 local = basis.InverseTransformDirection(velocity);
                Vector2 planar = new Vector2(local.x, local.z);
                if (navAgent.Speed > Mathf.Epsilon)
                    planar /= navAgent.Speed;
                return Vector2.ClampMagnitude(planar, 1f);
            }

            return Vector2.zero;
        }

        private (string speedParam, string dirXParam, string dirYParam) GetLocomotionParameterNames(CrowdAgent agent)
        {
            string speedParam = _settings.locomotionSpeedFloatParameter;
            string dirXParam = _settings.locomotionDirectionXFloatParameter;
            string dirYParam = _settings.locomotionDirectionYFloatParameter;

            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                {
                    if (!string.IsNullOrWhiteSpace(crowd.locomotionSpeedFloatParameterOverride))
                        speedParam = crowd.locomotionSpeedFloatParameterOverride;
                    if (!string.IsNullOrWhiteSpace(crowd.locomotionDirectionXFloatParameterOverride))
                        dirXParam = crowd.locomotionDirectionXFloatParameterOverride;
                    if (!string.IsNullOrWhiteSpace(crowd.locomotionDirectionYFloatParameterOverride))
                        dirYParam = crowd.locomotionDirectionYFloatParameterOverride;
                }
            }

            return (speedParam, dirXParam, dirYParam);
        }

        private bool HasDirectionalLocomotion(CrowdAgent agent, out (AnimationClip front, AnimationClip back, AnimationClip left, AnimationClip right) clips)
        {
            bool enabled = _settings.enableDirectionalLocomotion;
            DirectionalLocomotionClips set = _settings.directionalLocomotionAnimations;

            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                var crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                {
                    if (crowd.enableDirectionalLocomotionOverride)
                        enabled = true;
                    set = crowd.directionalLocomotionAnimationsOverride ?? set;
                }
            }

            AnimationClip front = set != null ? set.frontClip : null;
            AnimationClip back = set != null ? set.backClip : null;
            AnimationClip left = set != null ? set.leftClip : null;
            AnimationClip right = set != null ? set.rightClip : null;

            clips = (front, back, left, right);
            return enabled && (front != null || back != null || left != null || right != null);
        }

        private float GetDirectionalLocomotionThreshold(CrowdAgent agent)
        {
            float threshold = _settings.directionalLocomotionThreshold;

            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                var crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                    threshold = crowd.directionalLocomotionThresholdOverride;
            }

            return Mathf.Clamp01(threshold);
        }

        private LocomotionBlendMode GetLocomotionBlendMode(CrowdAgent agent)
        {
            // Simplify: enabling directional locomotion implies TwoD; otherwise OneD.
            bool enabled = _settings.enableDirectionalLocomotion;

            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null && crowd.enableDirectionalLocomotionOverride)
                    enabled = true;
            }

            return enabled ? LocomotionBlendMode.TwoD : LocomotionBlendMode.OneD;
        }

        private void TrySetAnimatorFloat(Animator animator, string parameterName, float value)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
                return;

            int hash = Animator.StringToHash(parameterName);
            if (hash == 0)
                return;

            int compositeKey = hash ^ (animator.GetInstanceID() << 5);
            if (_validatedAnimatorParameters.Contains(compositeKey))
            {
                animator.SetFloat(hash, value);
                return;
            }

            if (!AnimatorHasParameter(animator, hash, AnimatorControllerParameterType.Float))
            {
                WarnMissingAnimatorParameter(animator, parameterName, hash, AnimatorControllerParameterType.Float);
                return;
            }

            _validatedAnimatorParameters.Add(compositeKey);
            animator.SetFloat(hash, value);
        }

        private bool AnimatorHasParameter(Animator animator, int parameterHash, AnimatorControllerParameterType type)
        {
            var parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.type == type && p.nameHash == parameterHash)
                    return true;
            }
            return false;
        }

        private bool ShouldLogBoneTrackerDebug(CrowdAgent agent, int intervalFrames = 30)
        {
            if (!ENABLE_BONE_TRACKER_DEBUG || agent == null)
                return false;

            if (agent.currentWorkflow != HybridWorkflowType.BoneTracker)
                return false;

            if (agent.index >= 5)
                return false;

            int frame = Time.frameCount;
            if (!_boneTrackerDebugLastFrame.TryGetValue(agent.index, out int lastFrame))
            {
                _boneTrackerDebugLastFrame[agent.index] = frame;
                return true;
            }

            if (frame - lastFrame >= intervalFrames)
            {
                _boneTrackerDebugLastFrame[agent.index] = frame;
                return true;
            }

            return false;
        }

        private void WarnMissingAnimatorParameter(Animator animator, string parameterName, int parameterHash, AnimatorControllerParameterType type)
        {
            if (_loggedMissingAnimatorParameters.Contains(parameterHash))
                return;

            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            string controllerName = controller != null ? controller.name : "<null controller>";
            Debug.LogWarning($"Animator '{controllerName}' is missing a {type} parameter named '{parameterName}'.", animator);
            _loggedMissingAnimatorParameters.Add(parameterHash);
        }

        public void ResetIdleAnimationCache(CrowdAgent agent, bool resetAnimatorState = true)
        {
            if (agent == null)
                return;

            if (resetAnimatorState)
                agent.lastIdleAnimationHash = -1;
            agent.idleAnimationWasGpu = false;
        }

        private bool EnsureMecanimLocomotionState(CrowdAgent agent, float transitionTime)
        {
            if (agent?.mecanimAnimator == null)
                return false;

            string stateName = GetLocomotionStateNameForAgent(agent);
            if (string.IsNullOrWhiteSpace(stateName))
                return false;

            int locomotionHash = Animator.StringToHash(stateName);
            if (locomotionHash == 0)
                return false;

            if (!agent.mecanimAnimator.HasState(0, locomotionHash))
            {
                WarnMissingAnimatorState(agent.mecanimAnimator, stateName, locomotionHash);
                return false;
            }

            if (agent.lastIdleAnimationHash != locomotionHash)
            {
                agent.mecanimAnimator.CrossFade(locomotionHash, Mathf.Max(0f, transitionTime), 0, 0f);
                agent.lastIdleAnimationHash = locomotionHash;
            }

            return true;
        }

        private string GetLocomotionStateNameForAgent(CrowdAgent agent)
        {
            string stateName = _settings.locomotionStateName;

            // Check if agent belongs to a faction with overrides
            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null && !string.IsNullOrWhiteSpace(crowd.locomotionStateNameOverride))
                    stateName = crowd.locomotionStateNameOverride;
            }

            return stateName;
        }

        private (AnimationClip falling, AnimationClip landing) GetAirborneClipsForAgent(CrowdAgent agent)
        {
            AnimationClip falling = _settings.fallingClip;
            AnimationClip landing = _settings.landingClip;

            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                {
                    if (crowd.fallingClipOverride != null)
                        falling = crowd.fallingClipOverride;
                    if (crowd.landingClipOverride != null)
                        landing = crowd.landingClipOverride;
                }
            }

            return (falling, landing);
        }

        private AnimationClip GetLiftGetUpClipForAgent(CrowdAgent agent)
        {
            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null && crowd.liftGetUpClipOverride != null)
                    return crowd.liftGetUpClipOverride;
            }

            return null;
        }

        private (AnimationClip idle, AnimationClip walk, AnimationClip run) GetLocomotionClipsForAgent(CrowdAgent agent)
        {
            AnimationClip idle = _settings.idleClip;
            AnimationClip walk = _settings.walkClip;
            AnimationClip run = _settings.runClip;

            // Check if agent belongs to a faction with overrides
            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                {
                    if (crowd.idleClipOverride != null)
                        idle = crowd.idleClipOverride;
                    if (crowd.walkClipOverride != null)
                        walk = crowd.walkClipOverride;
                    if (crowd.runClipOverride != null)
                        run = crowd.runClipOverride;
                }
            }

            return (idle, walk, run);
        }

        private static bool DoesWorkflowSupportGpuAnimations(HybridWorkflowType workflowType)
        {
            // Both BoneTracker and MecanimReader do NOT have internal GPU animator systems
            // They rely on external Unity Animator components for animation
            // Only ComputeAnimator supports GPU animation calls (StartAnimation, StartBlend)
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
            // This can happen when agents are rapidly spawned/removed and buffer indices become stale
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
        /// This prevents "bufferIndex out of bounds" errors when agents are rapidly spawned/removed.
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

        private void WarnMissingAnimatorState(Animator animator, string stateName, int stateHash)
        {
            if (animator == null)
                return;

            if (_loggedMissingAnimatorStates.Contains(stateHash))
                return;

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            string controllerName = controller != null ? controller.name : "<null controller>";
            Debug.LogWarning($"Animator '{controllerName}' is missing a state named '{stateName}' on layer 0. Create a state with this exact name or update the controller to match the requested clip.", animator);
            _loggedMissingAnimatorStates.Add(stateHash);
        }

        #endregion
        
        #region Phase 2: Settings Helpers (EnemyCrowd as single source of truth)
        
        private float GetMinBlendUpdateDelta(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.minBlendUpdateDelta : _settings.minBlendUpdateDelta;
        }
        
        private bool GetControlAnimationsWithGPUI(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.controlAnimationsWithGPUI : _settings.controlAnimationsWithGPUI;
        }
        
        private float GetSpeedSmoothing(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.speedSmoothing : _settings.speedSmoothing;
        }
        
        private bool GetThrottleAnimationUpdates(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.throttleAnimationUpdates : _settings.throttleAnimationUpdates;
        }
        
        private int GetMaxAnimationUpdatesPerFrame(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.maxAnimationUpdatesPerFrame : _settings.maxAnimationUpdatesPerFrame;
        }
        
        private bool GetCullAnimationsByDistance(CrowdAgent agent)
        {
            // Phase 3 Refined: Animation culling is now a global setting
            return _settings.cullAnimationsByDistance;
        }
        
        private float GetAnimationCullDistance(CrowdAgent agent)
        {
            // Phase 3 Refined: Animation cull distance is now a global setting
            return _settings.animationCullDistance;
        }
        
        #endregion
    }
}
