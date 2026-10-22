using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Plays optional impulse reaction clips (push/pull/lift) for fake-physics navmesh impulses.
    /// Lives outside GC2 so non-GameCreator users can reuse the logic.
    /// </summary>
    public static class EnemyMassesImpulseAnimationHelper
    {
        private const float LiftClassificationThreshold = 1.5f; // Minimum lift to be treated as lift/get-up
        private const float ImpulseBlendOutLead = 0.2f; // Seconds before clip end to start blending back to locomotion/idle

        private enum ImpulseKind
        {
            None,
            Push,
            Pull,
            Lift
        }

        /// <summary>
        /// Classify impulse as push, pull, or lift using signed push/lift magnitudes.
        /// </summary>
        private static ImpulseKind GetImpulseKind(float pushEffective, float liftEffective)
        {
            float absPush = Mathf.Abs(pushEffective);

            // Only treat as lift when it meets the height threshold
            if (liftEffective >= LiftClassificationThreshold)
                return ImpulseKind.Lift;

            // Otherwise prefer push/pull classification
            if (absPush > 0.01f)
                return pushEffective >= 0f ? ImpulseKind.Push : ImpulseKind.Pull;

            // No meaningful push; ignore small lifts
            return ImpulseKind.None;
        }

        /// <summary>
        /// Try to play an impulse reaction clip for an agent, based on signed push/lift components.
        /// </summary>
        public static void PlayImpulseAnimation(CrowdAgent agent, float pushEffective, float liftEffective)
        {
            if (agent == null)
                return;

            var kind = GetImpulseKind(pushEffective, liftEffective);
            if (kind == ImpulseKind.None)
                return;

            var faction = agent.enemyCrowd;
            if (faction == null)
                return;

            AnimationClip[] clips = null;
            switch (kind)
            {
                case ImpulseKind.Push:
                    clips = faction.pushImpulseClipsOverride;
                    break;
                case ImpulseKind.Pull:
                    clips = faction.pullImpulseClipsOverride;
                    break;
                case ImpulseKind.Lift:
                    clips = faction.liftImpulseClipsOverride;
                    break;
            }

            if (clips == null || clips.Length == 0)
                return;

            var clip = clips.Length == 1 ? clips[0] : clips[Random.Range(0, clips.Length)];
            if (clip == null)
                return;

            // Avoid overriding critical states
            if (agent.ragdollActivated || agent.ragdollPending || agent.combatState == CrowdCombatState.Dead)
                return;

            // Don't override flight animations — the flight system manages its own transitions
            if (agent.isFlying || agent.currentState == CrowdAnimationState.FlightTakeOff ||
                agent.currentState == CrowdAnimationState.Flying || agent.currentState == CrowdAnimationState.FlightLanding)
                return;

            bool isGpuWorkflow = agent.currentWorkflow != HybridWorkflowType.BoneTracker &&
                                 agent.currentWorkflow != HybridWorkflowType.MecanimReader;

            if (isGpuWorkflow && agent.crowdInstance != null && agent.crowdInstance.AnimatorWorkflow != null)
            {
                try
                {
                    // Use a short transition into the impulse, then mark end time so locomotion can re-blend smoothly
                    agent.crowdInstance.StartAnimation(clip, -1f, 1f, 0.12f, null, false);
                    float duration = Mathf.Max(clip.length, 0.1f);
                    float now = Time.time;
                    agent.impulseAnimationActive = true;
                    agent.impulseAnimationEndTime = now + duration;
                    agent.impulseAnimationBlendOutTime = now + Mathf.Max(0.05f, duration - ImpulseBlendOutLead);
                    agent.impulseBlendOutTriggered = false;
                    agent.cachedBlendWeight = -1f; // ensures next locomotion blend re-crossfades after the impulse
                    agent.currentState = CrowdAnimationState.Idle;
                    agent.lastIdleAnimationHash = -1;
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ImpulseAnim] GPU start failed agent={agent.index} clip={clip?.name} error={ex.Message}");
                    // Fall through to Mecanim if available
                }
            }

            if (!isGpuWorkflow && agent.mecanimAnimator != null)
            {
                int stateHash = Animator.StringToHash(clip.name);
                if (agent.mecanimAnimator.HasState(0, stateHash))
                {
                    agent.mecanimAnimator.CrossFade(stateHash, 0.12f, 0, 0f);
                    float duration = Mathf.Max(clip.length, 0.1f);
                    float now = Time.time;
                    agent.impulseAnimationActive = true;
                    agent.impulseAnimationEndTime = now + duration;
                    agent.impulseAnimationBlendOutTime = now + Mathf.Max(0.05f, duration - ImpulseBlendOutLead);
                    agent.impulseBlendOutTriggered = false;
                    agent.cachedBlendWeight = -1f;
                    agent.currentState = CrowdAnimationState.Idle;
                    agent.lastIdleAnimationHash = -1;
                    Debug.Log($"[ImpulseAnim] Mecanim start agent={agent.index} clip={clip.name} duration={duration:F2} blendOut={agent.impulseAnimationBlendOutTime:F2} end={agent.impulseAnimationEndTime:F2}");
                }
            }
        }
    }
}
