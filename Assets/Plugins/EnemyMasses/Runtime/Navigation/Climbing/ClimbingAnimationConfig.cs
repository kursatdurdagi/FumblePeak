using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.Climbing
{
    /// <summary>
    /// Animation configuration for climbing sequences.
    /// Assigned per EnemyCrowd for faction-specific climbing animations.
    /// </summary>
    [System.Serializable]
    public class ClimbingAnimationConfig
    {
        [Header("Core Climbing Clips")]
        [Tooltip("Animation clip for climbing upward")]
        public AnimationClip climbUpClip;
        
        [Tooltip("Animation clip for climbing downward")]
        public AnimationClip climbDownClip;
        
        [Header("Directional Climbing (Optional)")]
        [Tooltip("Animation clip for climbing/traversing left")]
        public AnimationClip climbLeftClip;
        
        [Tooltip("Animation clip for climbing/traversing right")]
        public AnimationClip climbRightClip;
        
        [Header("Mount/Dismount")]
        [Tooltip("Animation clip for mounting a climbable surface")]
        public AnimationClip mountClip;
        
        [Tooltip("Animation clip for dismounting at the top")]
        public AnimationClip dismountTopClip;
        
        [Tooltip("Animation clip for dismounting at the bottom")]
        public AnimationClip dismountBottomClip;
        
        [Header("Idle & Fall")]
        [Tooltip("Animation clip for holding position on a climb surface")]
        public AnimationClip climbIdleClip;
        
        [Tooltip("Animation clip for falling off a surface")]
        public AnimationClip fallClip;
        
        [Header("Transition Clips (Optional)")]
        [Tooltip("Transition from climb up to normal locomotion")]
        public AnimationClip climbUpTransitionClip;
        
        [Tooltip("Transition from climb down to normal locomotion")]
        public AnimationClip climbDownTransitionClip;
        
        [Tooltip("Duration for transition clips (0 = use clip length)")]
        public float transitionClipDuration = 0f;
        
        [Header("Mecanim Parameters (for Mecanim workflow)")]
        [Tooltip("Bool parameter name to set when climbing (e.g., 'IsClimbing')")]
        public string climbingBoolParameter = "IsClimbing";
        
        [Tooltip("Float parameter name for climb speed/direction (1 = up, -1 = down)")]
        public string climbSpeedParameter = "ClimbSpeed";
        
        [Tooltip("Trigger parameter name for mount animation")]
        public string mountTriggerParameter = "Mount";
        
        [Tooltip("Trigger parameter name for dismount animation")]
        public string dismountTriggerParameter = "Dismount";
        
        [Tooltip("Trigger parameter name for fall animation")]
        public string fallTriggerParameter = "Fall";
        
        [Header("Animation Durations")]
        [Tooltip("Duration of mount animation in seconds")]
        public float mountDuration = 0.5f;
        
        [Tooltip("Duration of dismount animation in seconds")]
        public float dismountDuration = 0.5f;
        
        /// <summary>
        /// Get the appropriate animation clip for the specified climbing animation type.
        /// </summary>
        public AnimationClip GetClip(ClimbingAnimationType animType)
        {
            switch (animType)
            {
                case ClimbingAnimationType.ClimbUp:
                    return climbUpClip;
                case ClimbingAnimationType.ClimbDown:
                    return climbDownClip;
                case ClimbingAnimationType.ClimbLeft:
                    return climbLeftClip;
                case ClimbingAnimationType.ClimbRight:
                    return climbRightClip;
                case ClimbingAnimationType.Mount:
                    return mountClip;
                case ClimbingAnimationType.Dismount:
                    return dismountTopClip; // Default to top dismount
                case ClimbingAnimationType.Idle:
                    return climbIdleClip;
                default:
                    return climbUpClip;
            }
        }
        
        /// <summary>
        /// Check if the config has the minimum required clips for basic climbing.
        /// </summary>
        public bool HasMinimumClips()
        {
            return climbUpClip != null || climbDownClip != null;
        }
    }
    
    /// <summary>
    /// Climbing animation type for proper animation selection.
    /// </summary>
    public enum ClimbingAnimationType
    {
        ClimbUp,
        ClimbDown,
        ClimbLeft,
        ClimbRight,
        Mount,
        Dismount,
        Idle // Holding position on climb surface
    }
}
