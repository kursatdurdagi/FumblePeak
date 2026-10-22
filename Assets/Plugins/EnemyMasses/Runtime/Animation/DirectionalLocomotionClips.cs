using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Single-clip directional locomotion set for 2D directional blending.
    /// </summary>
    [System.Serializable]
    public class DirectionalLocomotionClips
    {
        [Tooltip("Forward locomotion clip (used for forward/diagonal forward movement).")]
        public AnimationClip frontClip;

        [Tooltip("Backward locomotion clip (used for backward/diagonal back movement). Optional.")]
        public AnimationClip backClip;

        [Tooltip("Left strafe locomotion clip (used for left/forward-left/back-left). Optional.")]
        public AnimationClip leftClip;

        [Tooltip("Right strafe locomotion clip (used for right/forward-right/back-right). Optional.")]
        public AnimationClip rightClip;
    }
}
