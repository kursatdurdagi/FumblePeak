using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration for footstep audio playback on crowd agents.
    /// Supports both interval-based random playback and animation event-synchronized playback.
    /// </summary>
    [System.Serializable]
    public class FootstepAudioConfig
    {
        [Header("Footstep Clips")]
        [Tooltip("Array of footstep sound clips. A random clip is selected for each footstep.")]
        public AudioClip[] footstepClips;
        
        [Header("Volume & Pitch")]
        [Tooltip("Base volume for footstep sounds (0-1).")]
        [Range(0f, 1f)]
        public float volume = 0.5f;
        
        [Tooltip("Random pitch variation (e.g., 0.1 = ±10% pitch). Adds natural variation.")]
        [Range(0f, 0.5f)]
        public float pitchVariation = 0.1f;
        
        [Header("3D Audio")]
        [Tooltip("Spatial blend: 0 = 2D (everywhere), 1 = 3D (positional).")]
        [Range(0f, 1f)]
        public float spatialBlend = 1f;
        
        [Tooltip("Minimum distance for 3D falloff.")]
        [Min(0.1f)]
        public float minDistance = 1f;
        
        [Tooltip("Maximum distance for 3D falloff.")]
        [Min(1f)]
        public float maxDistance = 30f;
        
        [Header("Playback Mode")]
        [Tooltip("How footsteps are triggered:\n" +
                 "• Interval: Random interval while moving (simple, no animation sync)\n" +
                 "• AnimationEvent: Requires GPUI Crowd Event Definition setup (precise sync)")]
        public FootstepPlaybackMode playbackMode = FootstepPlaybackMode.Interval;
        
        [Header("Interval Mode Settings")]
        [Tooltip("Minimum time between footsteps when using Interval mode.")]
        [Range(0.1f, 2f)]
        public float minInterval = 0.25f;
        
        [Tooltip("Maximum time between footsteps when using Interval mode.")]
        [Range(0.1f, 2f)]
        public float maxInterval = 0.45f;
        
        [Tooltip("Minimum speed (m/s) before footsteps play. Prevents sounds when barely moving.")]
        [Range(0f, 2f)]
        public float minSpeedThreshold = 0.5f;
        
        [Tooltip("If true, footstep interval scales with agent speed (faster = more frequent).")]
        public bool scaleIntervalWithSpeed = true;
        
        [Tooltip("Reference speed for interval scaling. At this speed, intervals are as configured.\n" +
                 "Faster = shorter intervals, slower = longer intervals.")]
        [Range(1f, 10f)]
        public float referenceSpeed = 3.5f;
        
        [Header("Concurrency")]
        [Tooltip("Maximum concurrent footstep sounds per faction. Prevents audio overload with many agents.")]
        [Range(1, 20)]
        public int maxConcurrentSounds = 8;
        
        [Header("Surface Types (Optional)")]
        [Tooltip("Enable surface-based footstep variation. Uses terrain texture sampling and physics tags.")]
        public bool enableSurfaceDetection = false;
        
        [Tooltip("Surface mapping asset. Defines how terrain textures and physics materials map to surface types.")]
        public SurfaceMapping surfaceMapping;
        
        [Tooltip("Footstep clips for grass/soft surfaces (dirt, mud, foliage).")]
        public AudioClip[] grassFootstepClips;
        
        [Tooltip("Footstep clips for stone/hard surfaces (concrete, rock, brick).")]
        public AudioClip[] stoneFootstepClips;
        
        [Tooltip("Footstep clips for water/shallow surfaces (puddles, streams).")]
        public AudioClip[] waterFootstepClips;
        
        [Tooltip("Footstep clips for wood surfaces (planks, floors, bridges).")]
        public AudioClip[] woodFootstepClips;
        
        [Tooltip("Footstep clips for metal surfaces (grates, platforms, sci-fi floors).")]
        public AudioClip[] metalFootstepClips;
        
        [Tooltip("Footstep clips for sand surfaces (beaches, deserts).")]
        public AudioClip[] sandFootstepClips;
        
        [Tooltip("Footstep clips for snow surfaces (ice, frost).")]
        public AudioClip[] snowFootstepClips;
        
        /// <summary>
        /// Returns true if footstep audio is configured and enabled.
        /// </summary>
        public bool IsEnabled => footstepClips != null && footstepClips.Length > 0;
        
        /// <summary>
        /// Gets a random footstep clip from the main array.
        /// </summary>
        public AudioClip GetRandomClip()
        {
            if (footstepClips == null || footstepClips.Length == 0)
                return null;
            return footstepClips[Random.Range(0, footstepClips.Length)];
        }
        
        /// <summary>
        /// Gets a random footstep clip for a specific surface type.
        /// Falls back to main clips if surface clips are not configured.
        /// </summary>
        public AudioClip GetRandomClipForSurface(FootstepSurfaceType surface)
        {
            AudioClip[] clips = surface switch
            {
                FootstepSurfaceType.Grass => grassFootstepClips,
                FootstepSurfaceType.Stone => stoneFootstepClips,
                FootstepSurfaceType.Water => waterFootstepClips,
                FootstepSurfaceType.Wood => woodFootstepClips,
                FootstepSurfaceType.Metal => metalFootstepClips,
                FootstepSurfaceType.Sand => sandFootstepClips,
                FootstepSurfaceType.Snow => snowFootstepClips,
                _ => footstepClips
            };
            
            // Fall back to default clips if surface-specific clips not configured
            if (clips == null || clips.Length == 0)
                clips = footstepClips;
            
            if (clips == null || clips.Length == 0)
                return null;
                
            return clips[Random.Range(0, clips.Length)];
        }
        
        /// <summary>
        /// Gets whether any clips are configured for a specific surface type.
        /// </summary>
        public bool HasClipsForSurface(FootstepSurfaceType surface)
        {
            AudioClip[] clips = surface switch
            {
                FootstepSurfaceType.Grass => grassFootstepClips,
                FootstepSurfaceType.Stone => stoneFootstepClips,
                FootstepSurfaceType.Water => waterFootstepClips,
                FootstepSurfaceType.Wood => woodFootstepClips,
                FootstepSurfaceType.Metal => metalFootstepClips,
                FootstepSurfaceType.Sand => sandFootstepClips,
                FootstepSurfaceType.Snow => snowFootstepClips,
                _ => footstepClips
            };
            return clips != null && clips.Length > 0;
        }
        
        /// <summary>
        /// Calculates the footstep interval based on agent speed.
        /// </summary>
        /// <param name="currentSpeed">Current agent movement speed</param>
        /// <returns>Time until next footstep</returns>
        public float GetInterval(float currentSpeed)
        {
            float baseInterval = Random.Range(minInterval, maxInterval);
            
            if (!scaleIntervalWithSpeed || currentSpeed <= 0f)
                return baseInterval;
            
            // Scale interval inversely with speed
            // At referenceSpeed, interval = baseInterval
            // Faster = shorter interval, slower = longer interval
            float speedRatio = referenceSpeed / Mathf.Max(currentSpeed, 0.1f);
            return baseInterval * Mathf.Clamp(speedRatio, 0.5f, 2f);
        }
        
        /// <summary>
        /// Creates a default footstep config suitable for humanoid infantry.
        /// </summary>
        public static FootstepAudioConfig CreateDefault()
        {
            return new FootstepAudioConfig
            {
                volume = 0.5f,
                pitchVariation = 0.1f,
                spatialBlend = 1f,
                minDistance = 1f,
                maxDistance = 30f,
                playbackMode = FootstepPlaybackMode.Interval,
                minInterval = 0.3f,
                maxInterval = 0.45f,
                minSpeedThreshold = 0.5f,
                scaleIntervalWithSpeed = true,
                referenceSpeed = 3.5f,
                maxConcurrentSounds = 8
            };
        }
        
        /// <summary>
        /// Creates a footstep config for heavy/armored units (slower, louder).
        /// </summary>
        public static FootstepAudioConfig CreateHeavyArmor()
        {
            return new FootstepAudioConfig
            {
                volume = 0.7f,
                pitchVariation = 0.05f,
                spatialBlend = 1f,
                minDistance = 2f,
                maxDistance = 40f,
                playbackMode = FootstepPlaybackMode.Interval,
                minInterval = 0.4f,
                maxInterval = 0.6f,
                minSpeedThreshold = 0.3f,
                scaleIntervalWithSpeed = true,
                referenceSpeed = 2.5f,
                maxConcurrentSounds = 6
            };
        }
        
        /// <summary>
        /// Creates a footstep config for cavalry/mounted units (4-legged rhythm).
        /// </summary>
        public static FootstepAudioConfig CreateCavalry()
        {
            return new FootstepAudioConfig
            {
                volume = 0.6f,
                pitchVariation = 0.08f,
                spatialBlend = 1f,
                minDistance = 2f,
                maxDistance = 50f,
                playbackMode = FootstepPlaybackMode.Interval,
                minInterval = 0.15f,
                maxInterval = 0.25f,
                minSpeedThreshold = 1f,
                scaleIntervalWithSpeed = true,
                referenceSpeed = 6f,
                maxConcurrentSounds = 10
            };
        }
    }
    
    /// <summary>
    /// How footstep sounds are triggered.
    /// </summary>
    public enum FootstepPlaybackMode
    {
        /// <summary>
        /// Simple interval-based playback while moving.
        /// No animation synchronization but works out of the box.
        /// </summary>
        Interval,
        
        /// <summary>
        /// Footsteps triggered by animation events via GPUI Crowd Event Definition.
        /// Requires setup but provides perfect synchronization with foot plants.
        /// </summary>
        AnimationEvent,
        
        /// <summary>
        /// Footsteps disabled for this faction.
        /// </summary>
        Disabled
    }
    
    /// <summary>
    /// Surface type for footstep audio variation.
    /// </summary>
    public enum FootstepSurfaceType
    {
        Default,
        Grass,
        Stone,
        Water,
        Wood,
        Metal,
        Sand,
        Snow
    }
}
