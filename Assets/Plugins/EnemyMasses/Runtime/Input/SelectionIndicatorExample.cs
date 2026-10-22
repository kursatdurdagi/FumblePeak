using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Example selection indicator that can be used as a prefab for visual feedback.
    /// Attach this to a GameObject with a visual (sprite, particle system, projector, etc.)
    /// and assign it to the RTS Controller's Selection Indicator Prefab field.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Selection Indicator Example")]
    public class SelectionIndicatorExample : MonoBehaviour
    {
        [Header("Animation")]
        [Tooltip("Rotate the indicator over time")]
        public bool rotate = true;
        
        [Tooltip("Rotation speed in degrees per second")]
        public float rotationSpeed = 90f;

        [Tooltip("Pulse the scale over time")]
        public bool pulse = true;
        
        [Tooltip("Pulse speed")]
        public float pulseSpeed = 2f;
        
        [Tooltip("Pulse amount (scale multiplier)")]
        [Range(0f, 0.5f)]
        public float pulseAmount = 0.1f;

        [Header("Appearance")]
        [Tooltip("Base scale of the indicator")]
        public float baseScale = 1f;

        private Vector3 _initialScale;
        private float _pulsePhase;

        private void Awake()
        {
            _initialScale = transform.localScale;
            _pulsePhase = Random.Range(0f, Mathf.PI * 2f); // Random start phase for variety
        }

        private void Update()
        {
            // Rotation
            if (rotate)
            {
                transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
            }

            // Pulse
            if (pulse)
            {
                float pulseScale = 1f + Mathf.Sin(Time.time * pulseSpeed + _pulsePhase) * pulseAmount;
                transform.localScale = _initialScale * baseScale * pulseScale;
            }
        }

        /// <summary>
        /// Called by the RTS controller when the indicator is shown.
        /// Override this in a subclass to add custom initialization.
        /// </summary>
        public virtual void OnShow()
        {
            _pulsePhase = Random.Range(0f, Mathf.PI * 2f);
        }

        /// <summary>
        /// Called by the RTS controller when the indicator is hidden.
        /// Override this in a subclass to add custom cleanup.
        /// </summary>
        public virtual void OnHide()
        {
        }
    }
}
