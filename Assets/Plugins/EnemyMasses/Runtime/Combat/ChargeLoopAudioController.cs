using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Self-managing audio controller for charge loop sounds.
    /// Automatically fades out and self-destructs after a timeout,
    /// ensuring no orphaned audio sources remain playing.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ChargeLoopAudioController : MonoBehaviour
    {
        private AudioSource _audioSource;
        private float _startTime;
        private float _maxDuration;
        private float _fadeOutDuration = 0.5f;
        private bool _isStoppingManually;
        private bool _isFadingOut;
        private float _fadeStartTime;
        private float _originalVolume;
        private int _agentIndex;
        private System.Action<AudioSource> _releaseToPool;
        private System.Action<ChargeLoopAudioController> _onStopped;

        public int AgentIndex => _agentIndex;

        /// <summary>
        /// Initializes the controller with timeout parameters.
        /// </summary>
        /// <param name="agentIndex">The agent index for debugging</param>
        /// <param name="maxDuration">Maximum duration before auto-cleanup (based on charge distance/speed + buffer)</param>
        /// <param name="fadeOutDuration">Duration of fade out before destruction</param>
        public void Initialize(int agentIndex, float maxDuration, float fadeOutDuration = 0.5f, System.Action<AudioSource> releaseToPool = null, System.Action<ChargeLoopAudioController> onStopped = null)
        {
            _agentIndex = agentIndex;
            _audioSource = GetComponent<AudioSource>();
            _startTime = Time.time;
            _maxDuration = maxDuration;
            _fadeOutDuration = fadeOutDuration;
            _originalVolume = _audioSource.volume;
            _releaseToPool = releaseToPool;
            _onStopped = onStopped;
            _isStoppingManually = false;
            _isFadingOut = false;
            enabled = true;
        }

        /// <summary>
        /// Call this when the charge ends normally (impact or abort).
        /// Performs immediate cleanup without fade.
        /// </summary>
        public void StopImmediately()
        {
            if (_isStoppingManually) return;
            _isStoppingManually = true;
            
            CleanupAndDestroy();
        }

        /// <summary>
        /// Starts fade out and then destroys. Call this for graceful shutdown.
        /// </summary>
        public void FadeOutAndDestroy()
        {
            if (_isStoppingManually || _isFadingOut) return;
            _isFadingOut = true;
            _fadeStartTime = Time.time;
        }

        private void Update()
        {
            if (_isStoppingManually) return;
            
            float elapsed = Time.time - _startTime;
            
            // Handle fade out
            if (_isFadingOut)
            {
                float fadeElapsed = Time.time - _fadeStartTime;
                float fadeProgress = fadeElapsed / _fadeOutDuration;
                
                if (fadeProgress >= 1f)
                {
                    CleanupAndDestroy();
                    return;
                }
                
                _audioSource.volume = Mathf.Lerp(_originalVolume, 0f, fadeProgress);
                return;
            }
            
            // Check for timeout - trigger fade out
            if (elapsed >= _maxDuration)
            {
                FadeOutAndDestroy();
            }
        }

        private void CleanupAndDestroy()
        {
            _isStoppingManually = true;
            
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.loop = false;
                _audioSource.volume = 0f;
                _audioSource.clip = null;
                _audioSource.enabled = false;
            }

            _onStopped?.Invoke(this);
            _onStopped = null;

            if (_releaseToPool != null && _audioSource != null)
            {
                _releaseToPool(_audioSource);
                _audioSource = null;
                _releaseToPool = null;
                enabled = false;
                return;
            }
            
            #if UNITY_EDITOR
            Object.DestroyImmediate(gameObject);
            #else
            Object.Destroy(gameObject);
            #endif
        }
    }
}
