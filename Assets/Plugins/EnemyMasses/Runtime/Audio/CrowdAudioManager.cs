using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages audio playback for crowd agents with per-clip concurrency limits.
    /// Prevents thousands of agents from creating thousands of AudioSources.
    /// Each referenced AudioClip has a limited pool of concurrent sounds,
    /// and the oldest playing sound is faded out when the limit is reached.
    /// </summary>
    public class CrowdAudioManager : MonoBehaviour
    {
        #region Singleton

        public static CrowdAudioManager Instance { get; private set; }

        #endregion

        #region Settings

        [Header("Global Settings")]
        [Tooltip("Default maximum concurrent sounds per AudioClip reference. Can be overridden per EnemyCrowd.")]
        [Range(1, 20)]
        [SerializeField] private int defaultMaxConcurrentSoundsPerClip = 5;

        [Tooltip("Fade out duration when stopping old sounds to make room for new ones.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float fadeOutDuration = 0.15f;

        [Tooltip("Enable debug logging for audio pooling.")]
        [SerializeField] private bool debugLogging = false;

        #endregion

        #region Internal Classes

        /// <summary>
        /// Tracks a single playing audio source instance.
        /// </summary>
        private class PlayingSound
        {
            public AudioSource source;
            public float startTime;
            public float originalVolume;
            public bool isFadingOut;
            public float fadeStartTime;
        }

        /// <summary>
        /// Pool of audio sources for a specific AudioClip.
        /// </summary>
        private class ClipPool
        {
            public AudioClip clip;
            public List<PlayingSound> activeSounds = new List<PlayingSound>();
            public Queue<AudioSource> availableSources = new Queue<AudioSource>();
            public int maxConcurrent;
            public Transform poolContainer;
        }

        #endregion

        #region Private State

        private Dictionary<AudioClip, ClipPool> _clipPools = new Dictionary<AudioClip, ClipPool>();
        private Transform _audioPoolRoot;
        private List<PlayingSound> _fadeOutList = new List<PlayingSound>();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Create root container for audio sources
            _audioPoolRoot = new GameObject("CrowdAudioPool").transform;
            _audioPoolRoot.SetParent(transform);
        }

        private void Update()
        {
            ProcessFadeOuts();
            CleanupFinishedSounds();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Play a sound at a position with concurrency limiting.
        /// If the clip is already at max concurrent plays, fades out and stops the oldest.
        /// </summary>
        /// <param name="clip">The AudioClip to play.</param>
        /// <param name="position">World position to play at.</param>
        /// <param name="volume">Base volume (0-1).</param>
        /// <param name="pitchVariation">Random pitch variation (e.g., 0.1 = ±10%).</param>
        /// <param name="spatialBlend">0 = 2D, 1 = 3D.</param>
        /// <param name="maxConcurrent">Max concurrent sounds for this clip. Use -1 for default.</param>
        /// <param name="minDistance">3D audio min distance.</param>
        /// <param name="maxDistance">3D audio max distance.</param>
        public void PlaySound(
            AudioClip clip,
            Vector3 position,
            float volume = 1f,
            float pitchVariation = 0f,
            float spatialBlend = 1f,
            int maxConcurrent = -1,
            float minDistance = 1f,
            float maxDistance = 50f)
        {
            if (clip == null) return;

            int limit = maxConcurrent > 0 ? maxConcurrent : defaultMaxConcurrentSoundsPerClip;
            ClipPool pool = GetOrCreatePool(clip, limit);

            // If at limit, fade out the oldest sound
            if (pool.activeSounds.Count >= pool.maxConcurrent)
            {
                FadeOutOldest(pool);
            }

            // Get or create an audio source
            AudioSource source = AcquireSource(pool);
            if (source == null) return;

            // Configure and play
            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.loop = false;

            // Apply pitch variation
            float pitch = 1f;
            if (pitchVariation > 0f)
            {
                pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
            }
            source.pitch = pitch;

            source.Play();

            // Track this playing sound
            var playingSound = new PlayingSound
            {
                source = source,
                startTime = Time.time,
                originalVolume = volume,
                isFadingOut = false
            };
            pool.activeSounds.Add(playingSound);

            if (debugLogging)
            {
                Debug.Log($"[CrowdAudio] Playing '{clip.name}' at {position} (active: {pool.activeSounds.Count}/{pool.maxConcurrent})");
            }
        }

        /// <summary>
        /// Play a looping sound at a position with concurrency limiting.
        /// Returns the AudioSource so callers can manage lifetime.
        /// </summary>
        public AudioSource PlayLoopingSound(
            AudioClip clip,
            Vector3 position,
            float volume = 1f,
            float pitchVariation = 0f,
            float spatialBlend = 1f,
            int maxConcurrent = -1,
            float minDistance = 1f,
            float maxDistance = 50f)
        {
            if (clip == null) return null;

            int limit = maxConcurrent > 0 ? maxConcurrent : defaultMaxConcurrentSoundsPerClip;
            ClipPool pool = GetOrCreatePool(clip, limit);
            pool.maxConcurrent = limit;

            if (pool.activeSounds.Count >= pool.maxConcurrent)
            {
                StopOldestSound(pool);
            }

            bool allowCreate = pool.activeSounds.Count + pool.availableSources.Count < pool.maxConcurrent;
            AudioSource source = AcquireSource(pool, allowCreate);
            if (source == null) return null;

            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.loop = true;

            float pitch = 1f;
            if (pitchVariation > 0f)
            {
                pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
            }
            source.pitch = pitch;

            source.Play();

            var playingSound = new PlayingSound
            {
                source = source,
                startTime = Time.time,
                originalVolume = volume,
                isFadingOut = false
            };
            pool.activeSounds.Add(playingSound);

            if (debugLogging)
            {
                Debug.Log($"[CrowdAudio] Looping '{clip.name}' at {position} (active: {pool.activeSounds.Count}/{pool.maxConcurrent})");
            }

            return source;
        }

        /// <summary>
        /// Sets the global default for max concurrent sounds per clip.
        /// </summary>
        public void SetDefaultMaxConcurrent(int value)
        {
            defaultMaxConcurrentSoundsPerClip = Mathf.Clamp(value, 1, 50);
        }

        /// <summary>
        /// Updates the max concurrent limit for a specific clip pool.
        /// </summary>
        public void SetClipMaxConcurrent(AudioClip clip, int maxConcurrent)
        {
            if (clip == null) return;

            if (_clipPools.TryGetValue(clip, out var pool))
            {
                pool.maxConcurrent = Mathf.Max(1, maxConcurrent);
            }
        }

        /// <summary>
        /// Releases a pooled audio source back to its clip pool.
        /// </summary>
        public void ReleaseSound(AudioSource source)
        {
            if (source == null) return;

            for (int i = _fadeOutList.Count - 1; i >= 0; i--)
            {
                if (_fadeOutList[i].source == source)
                {
                    _fadeOutList.RemoveAt(i);
                }
            }

            foreach (var pool in _clipPools.Values)
            {
                for (int i = pool.activeSounds.Count - 1; i >= 0; i--)
                {
                    var sound = pool.activeSounds[i];
                    if (sound.source == source)
                    {
                        pool.activeSounds.RemoveAt(i);
                        ReleaseSource(pool, source);
                        return;
                    }
                }
            }

            source.Stop();
            source.loop = false;
            source.clip = null;
            source.gameObject.SetActive(false);
        }

        /// <summary>
        /// Gets current statistics for debugging.
        /// </summary>
        public void GetStats(out int totalPools, out int totalActiveSounds, out int totalPooledSources)
        {
            totalPools = _clipPools.Count;
            totalActiveSounds = 0;
            totalPooledSources = 0;

            foreach (var pool in _clipPools.Values)
            {
                totalActiveSounds += pool.activeSounds.Count;
                totalPooledSources += pool.availableSources.Count;
            }
        }

        #endregion

        #region Internal Methods

        private ClipPool GetOrCreatePool(AudioClip clip, int maxConcurrent)
        {
            if (!_clipPools.TryGetValue(clip, out var pool))
            {
                pool = new ClipPool
                {
                    clip = clip,
                    maxConcurrent = maxConcurrent,
                    activeSounds = new List<PlayingSound>(),
                    availableSources = new Queue<AudioSource>()
                };

                // Create container for this clip's sources
                pool.poolContainer = new GameObject($"Pool_{clip.name}").transform;
                pool.poolContainer.SetParent(_audioPoolRoot);

                _clipPools[clip] = pool;

                if (debugLogging)
                {
                    Debug.Log($"[CrowdAudio] Created pool for '{clip.name}' (max: {maxConcurrent})");
                }
            }

            return pool;
        }

        private AudioSource AcquireSource(ClipPool pool, bool allowCreate = true)
        {
            AudioSource source;

            if (pool.availableSources.Count > 0)
            {
                source = pool.availableSources.Dequeue();
                source.gameObject.SetActive(true);
                if (!source.enabled)
                    source.enabled = true;
            }
            else
            {
                if (!allowCreate)
                    return null;

                // Create new source
                var go = new GameObject($"Audio_{pool.clip.name}_{pool.activeSounds.Count}");
                go.transform.SetParent(pool.poolContainer);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.rolloffMode = AudioRolloffMode.Linear;
            }

            return source;
        }

        private void ReleaseSource(ClipPool pool, AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);
            pool.availableSources.Enqueue(source);
        }

        private void FadeOutOldest(ClipPool pool)
        {
            if (pool.activeSounds.Count == 0) return;

            // Find oldest non-fading sound
            PlayingSound oldest = null;
            float oldestTime = float.MaxValue;

            for (int i = 0; i < pool.activeSounds.Count; i++)
            {
                var sound = pool.activeSounds[i];
                if (!sound.isFadingOut && sound.startTime < oldestTime)
                {
                    oldestTime = sound.startTime;
                    oldest = sound;
                }
            }

            if (oldest != null)
            {
                oldest.isFadingOut = true;
                oldest.fadeStartTime = Time.time;
                _fadeOutList.Add(oldest);

                if (debugLogging)
                {
                    Debug.Log($"[CrowdAudio] Fading out oldest '{pool.clip.name}' (age: {Time.time - oldest.startTime:F2}s)");
                }
            }
        }

        private void StopOldestSound(ClipPool pool)
        {
            if (pool.activeSounds.Count == 0) return;

            PlayingSound oldest = null;
            float oldestTime = float.MaxValue;

            for (int i = 0; i < pool.activeSounds.Count; i++)
            {
                var sound = pool.activeSounds[i];
                if (!sound.isFadingOut && sound.startTime < oldestTime)
                {
                    oldestTime = sound.startTime;
                    oldest = sound;
                }
            }

            if (oldest == null)
            {
                oldest = pool.activeSounds[0];
            }

            if (oldest.source != null && oldest.source.TryGetComponent(out ChargeLoopAudioController controller))
            {
                controller.StopImmediately();
                return;
            }

            _fadeOutList.Remove(oldest);
            pool.activeSounds.Remove(oldest);
            ReleaseSource(pool, oldest.source);
        }

        private void ProcessFadeOuts()
        {
            for (int i = _fadeOutList.Count - 1; i >= 0; i--)
            {
                var sound = _fadeOutList[i];
                if (sound.source == null)
                {
                    _fadeOutList.RemoveAt(i);
                    continue;
                }

                float elapsed = Time.time - sound.fadeStartTime;
                float t = elapsed / fadeOutDuration;

                if (t >= 1f)
                {
                    // Fade complete - stop and release
                    sound.source.Stop();
                    _fadeOutList.RemoveAt(i);
                }
                else
                {
                    // Continue fading
                    sound.source.volume = Mathf.Lerp(sound.originalVolume, 0f, t);
                }
            }
        }

        private void CleanupFinishedSounds()
        {
            foreach (var pool in _clipPools.Values)
            {
                for (int i = pool.activeSounds.Count - 1; i >= 0; i--)
                {
                    var sound = pool.activeSounds[i];
                    if (sound.source == null || !sound.source.isPlaying)
                    {
                        if (sound.source != null)
                        {
                            ReleaseSource(pool, sound.source);
                        }
                        pool.activeSounds.RemoveAt(i);
                    }
                }
            }
        }

        #endregion

        #region Static Helper

        /// <summary>
        /// Ensures a CrowdAudioManager exists in the scene.
        /// Call this from systems that need audio playback.
        /// </summary>
        public static CrowdAudioManager EnsureInstance()
        {
            if (Instance != null) return Instance;

            var existing = FindFirstObjectByType<CrowdAudioManager>();
            if (existing != null) return existing;

            var go = new GameObject("CrowdAudioManager");
            return go.AddComponent<CrowdAudioManager>();
        }

        #endregion
    }
}
