using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration settings for the Enemy Masses animation system.
    /// Holds all animation-related settings from the main controller.
    /// </summary>
    [System.Serializable]
    public class AnimationSettings
    {
        [Header("Animation Clips")]
        [Tooltip("Default idle animation clip")]
        public AnimationClip idleClip;
        
        [Tooltip("Default walk animation clip")]
        public AnimationClip walkClip;
        
        [Tooltip("Default run animation clip")]
        public AnimationClip runClip;

        [Tooltip("Default falling animation clip (looped while airborne)")]
        public AnimationClip fallingClip;
        
        [Tooltip("Optional landing animation clip played once when the agent returns to ground")]
        public AnimationClip landingClip;
        
        [Tooltip("Minimum difference in locomotion blend weight before another GPU blend update is sent.")]
        [Range(0.01f, 0.1f)]
        public float minBlendUpdateDelta = 0.02f;
        
        [Header("Animation Control")]
        [Tooltip("Use GPUI for animations when available")]
        public bool controlAnimationsWithGPUI = true;
        
        [Tooltip("Default animator workflow override")]
        public AnimatorWorkflowOverride animatorWorkflow = AnimatorWorkflowOverride.ComputeAnimator;
        
        [Tooltip("Mecanim locomotion state name (for non-GPU workflows)")]
        public string locomotionStateName = "Locomotion";

        [Tooltip("Locomotion blend mode: 1D Speed or 2D planar (X/Z) for blend trees.")]
        public LocomotionBlendMode locomotionBlendMode = LocomotionBlendMode.OneD;

        [Tooltip("Float parameter name for locomotion speed.")]
        public string locomotionSpeedFloatParameter = "Speed";

        [Tooltip("Float parameter name for planar X (strafe) movement in 2D blend trees.")]
        public string locomotionDirectionXFloatParameter = "MoveX";

        [Tooltip("Float parameter name for planar Y/Z (forward) movement in 2D blend trees.")]
        public string locomotionDirectionYFloatParameter = "MoveY";

        [Header("Directional Locomotion (2D GPU/Mecanim)")]
        [Tooltip("Enable directional locomotion clips (front/back/left/right) for true 2D blending.")]
        public bool enableDirectionalLocomotion = false;

        [Tooltip("Directional locomotion clips used when 2D blend mode is active.")]
        public DirectionalLocomotionClips directionalLocomotionAnimations = new DirectionalLocomotionClips();

        [Tooltip("Normalized speed threshold (0-1). Below this, use 1D walk/run; above, switch to directional 2D locomotion.")]
        [Range(0f, 1f)]
        public float directionalLocomotionThreshold = 0.4f;
        
        [Header("Animation Throttling")]
        [Tooltip("Limit animation state processing per frame")]
        public bool throttleAnimationUpdates = true;
        
        [Tooltip("Maximum animation updates per frame")]
        [Min(1)]
        public int maxAnimationUpdatesPerFrame = 500;
        
        [Tooltip("Extra animation updates reserved for visible agents")]
        [Min(0)]
        public int priorityAnimationBonus = 250;
        
        [Header("Animation Culling")]
        [Tooltip("Skip GPU animation updates for agents beyond this distance")]
        public bool cullAnimationsByDistance = true;
        
        [Tooltip("Distance at which to cull animations")]
        [Min(0f)]
        public float animationCullDistance = 150f;
        
        [Tooltip("Optional transform used as the culling pivot. Defaults to the main camera when left empty.")]
        public Transform animationCullReference;
        
        [Header("Speed Settings")]
        [Tooltip("How quickly animation speed reacts to NavMesh speed changes (higher = snappier)")]
        [Min(0f)]
        public float speedSmoothing = 8f;

        [Header("Directional Combat Animations")]
        [Tooltip("Enable directional hit animations (auto-enabled when clips are assigned)")]
        public bool enableDirectionalHitAnimations = true;

        [Tooltip("Directional animation clips for hit reactions")]
        public DirectionalAnimationSet hitAnimations = new DirectionalAnimationSet();

        [Tooltip("Enable directional death animations (auto-enabled when clips are assigned)")]
        public bool enableDirectionalDeathAnimations = true;

        [Tooltip("Directional animation clips for death animations")]
        public DirectionalAnimationSet deathAnimations = new DirectionalAnimationSet();

        [Tooltip("Enable directional block animations (auto-enabled when clips are assigned)")]
        public bool enableDirectionalBlockAnimations = true;

        [Tooltip("Directional animation clips for blocking")]
        public DirectionalAnimationSet blockAnimations = new DirectionalAnimationSet();

        [Tooltip("Enable directional attack animations (auto-enabled when clips are assigned)")]
        public bool enableDirectionalAttackAnimations = true;

        [Tooltip("Directional animation clips for attacks")]
        public DirectionalAnimationSet attackAnimations = new DirectionalAnimationSet();

        /// <summary>
        /// Checks if directional animations are available for a specific type.
        /// </summary>
        public bool HasDirectionalAnimations(DirectionalAnimationType type)
        {
            switch (type)
            {
                case DirectionalAnimationType.Hit:
                    return enableDirectionalHitAnimations && hitAnimations.IsEnabled;
                case DirectionalAnimationType.Death:
                    return enableDirectionalDeathAnimations && deathAnimations.IsEnabled;
                case DirectionalAnimationType.Block:
                    return enableDirectionalBlockAnimations && blockAnimations.IsEnabled;
                case DirectionalAnimationType.Attack:
                    return enableDirectionalAttackAnimations && attackAnimations.IsEnabled;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the directional animation set for a specific type.
        /// </summary>
        public DirectionalAnimationSet GetDirectionalAnimationSet(DirectionalAnimationType type)
        {
            switch (type)
            {
                case DirectionalAnimationType.Hit:
                    return hitAnimations;
                case DirectionalAnimationType.Death:
                    return deathAnimations;
                case DirectionalAnimationType.Block:
                    return blockAnimations;
                case DirectionalAnimationType.Attack:
                    return attackAnimations;
                default:
                    return null;
            }
        }
    }
}
