using System;
using UnityEngine;
using UnityEngine.VFX;

namespace EnemyMasses.VFX
{
    /// <summary>
    /// Type of VFX system to use.
    /// </summary>
    public enum VFXType
    {
        /// <summary>No VFX assigned</summary>
        None,
        /// <summary>Legacy ParticleSystem prefab (CPU-bound)</summary>
        ParticleSystem,
        /// <summary>VFX Graph asset (GPU-accelerated)</summary>
        VFXGraph
    }

    /// <summary>
    /// A flexible VFX reference that can hold either a ParticleSystem prefab or a VFX Graph asset.
    /// This allows game designers to choose between legacy ParticleSystem (easier to set up) 
    /// or VFX Graph (GPU-accelerated, better performance for mass battles).
    /// </summary>
    [Serializable]
    public class VFXReference
    {
        [Tooltip("Type of VFX system to use")]
        public VFXType type = VFXType.None;

        [Tooltip("ParticleSystem prefab (legacy, CPU-bound). Use for simple effects or when VFX Graph is not available.")]
        public GameObject particleSystemPrefab;

        [Tooltip("VFX Graph asset (GPU-accelerated). Recommended for high-frequency effects like impacts, muzzle flashes, etc.")]
        public VisualEffectAsset vfxGraphAsset;

        [Tooltip("Duration for VFX Graph effects (ParticleSystem duration is auto-calculated from the prefab)")]
        public float defaultDuration = 2f;
        
        // Cached auto-calculated duration for ParticleSystem prefabs
        [NonSerialized] private float _cachedParticleSystemDuration = -1f;
        [NonSerialized] private int _cachedPrefabInstanceId = 0;
        
        /// <summary>
        /// Maximum duration to use for looping particle systems (fallback)
        /// </summary>
        public const float MaxLoopingDuration = 10f;

        /// <summary>
        /// Returns true if a valid VFX is assigned (either ParticleSystem or VFX Graph)
        /// </summary>
        public bool IsValid
        {
            get
            {
                return type switch
                {
                    VFXType.ParticleSystem => particleSystemPrefab != null,
                    VFXType.VFXGraph => vfxGraphAsset != null,
                    _ => false
                };
            }
        }

        /// <summary>
        /// Returns true if this is using VFX Graph (GPU-accelerated)
        /// </summary>
        public bool IsVFXGraph => type == VFXType.VFXGraph && vfxGraphAsset != null;

        /// <summary>
        /// Returns true if this is using ParticleSystem (legacy)
        /// </summary>
        public bool IsParticleSystem => type == VFXType.ParticleSystem && particleSystemPrefab != null;
        
        /// <summary>
        /// Gets the effective duration for this VFX.
        /// For ParticleSystem: Auto-calculated from prefab (startDelay + duration + startLifetime)
        /// For VFX Graph: Uses the manually set defaultDuration
        /// </summary>
        public float GetDuration()
        {
            if (type == VFXType.ParticleSystem && particleSystemPrefab != null)
            {
                // Check cache validity
                int currentInstanceId = particleSystemPrefab.GetInstanceID();
                if (_cachedParticleSystemDuration < 0 || _cachedPrefabInstanceId != currentInstanceId)
                {
                    _cachedParticleSystemDuration = CalculateParticleSystemDuration(particleSystemPrefab);
                    _cachedPrefabInstanceId = currentInstanceId;
                }
                return _cachedParticleSystemDuration;
            }
            
            return defaultDuration;
        }
        
        /// <summary>
        /// Calculates the total duration of a ParticleSystem prefab including all children.
        /// Takes into account: startDelay, duration (if not looping), and startLifetime.
        /// </summary>
        /// <param name="prefab">The ParticleSystem prefab to analyze</param>
        /// <returns>The maximum duration across all particle systems in the hierarchy</returns>
        public static float CalculateParticleSystemDuration(GameObject prefab)
        {
            if (prefab == null) return 2f;
            
            var particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (particleSystems == null || particleSystems.Length == 0) return 2f;
            
            float maxDuration = 0f;
            bool anyNonLooping = false;
            
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                
                // Calculate total time for this particle system
                float startDelay = GetMaxValue(main.startDelay);
                float duration = main.duration;
                float startLifetime = GetMaxValue(main.startLifetime);
                
                float totalTime;
                if (main.loop)
                {
                    // Looping systems: use startDelay + one cycle + lifetime, capped at max
                    totalTime = Mathf.Min(startDelay + duration + startLifetime, MaxLoopingDuration);
                }
                else
                {
                    // Non-looping: full duration
                    totalTime = startDelay + duration + startLifetime;
                    anyNonLooping = true;
                }
                
                maxDuration = Mathf.Max(maxDuration, totalTime);
            }
            
            // If all systems are looping, cap at MaxLoopingDuration
            if (!anyNonLooping && maxDuration > MaxLoopingDuration)
            {
                maxDuration = MaxLoopingDuration;
            }
            
            // Ensure minimum duration
            return Mathf.Max(0.1f, maxDuration);
        }
        
        /// <summary>
        /// Gets the maximum value from a ParticleSystem.MinMaxCurve
        /// </summary>
        private static float GetMaxValue(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(curve.constantMin, curve.constantMax);
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    // For curves, sample at multiple points to find max
                    float max = 0f;
                    var curveToSample = curve.mode == ParticleSystemCurveMode.TwoCurves ? curve.curveMax : curve.curve;
                    if (curveToSample != null && curveToSample.length > 0)
                    {
                        for (int i = 0; i <= 10; i++)
                        {
                            float t = i / 10f;
                            max = Mathf.Max(max, curveToSample.Evaluate(t) * curve.curveMultiplier);
                        }
                    }
                    return max;
                default:
                    return curve.constant;
            }
        }
        
        /// <summary>
        /// Invalidates the cached duration (call when prefab changes)
        /// </summary>
        public void InvalidateDurationCache()
        {
            _cachedParticleSystemDuration = -1f;
            _cachedPrefabInstanceId = 0;
        }

        /// <summary>
        /// Gets a unique identifier for this VFX reference (for pooling)
        /// </summary>
        public int GetHashId()
        {
            return type switch
            {
                VFXType.ParticleSystem => particleSystemPrefab != null ? particleSystemPrefab.GetInstanceID() : 0,
                VFXType.VFXGraph => vfxGraphAsset != null ? vfxGraphAsset.GetInstanceID() : 0,
                _ => 0
            };
        }

        /// <summary>
        /// Implicit conversion from GameObject for backwards compatibility
        /// </summary>
        public static implicit operator VFXReference(GameObject prefab)
        {
            return new VFXReference
            {
                type = prefab != null ? VFXType.ParticleSystem : VFXType.None,
                particleSystemPrefab = prefab
            };
        }
    }

    /// <summary>
    /// Event names used by VFX Graph for spawning effects.
    /// These should match the event names in your VFX Graph assets.
    /// </summary>
    public static class VFXEventNames
    {
        public const string OnSpawn = "OnSpawn";
        public const string OnImpact = "OnImpact";
        public const string OnMuzzleFlash = "OnMuzzleFlash";
        public const string OnAOEExplosion = "OnAOEExplosion";
        public const string OnTrailStart = "OnTrailStart";
        public const string OnTrailEnd = "OnTrailEnd";
    }

    /// <summary>
    /// Property names used by VFX Graph for setting spawn parameters.
    /// These should match the exposed properties in your VFX Graph assets.
    /// </summary>
    public static class VFXPropertyNames
    {
        // Position and orientation
        public const string SpawnPosition = "SpawnPosition";
        public const string SpawnDirection = "SpawnDirection";
        public const string SpawnNormal = "SpawnNormal";
        public const string SpawnRotation = "SpawnRotation";

        // Scale and size
        public const string SpawnScale = "SpawnScale";
        public const string EffectRadius = "EffectRadius";

        // Color and appearance
        public const string SpawnColor = "SpawnColor";
        public const string Intensity = "Intensity";

        // Timing
        public const string Duration = "Duration";
        public const string Lifetime = "Lifetime";

        // AOE specific
        public const string AOERadius = "AOERadius";
        public const string DamageAmount = "DamageAmount";

        // Trail specific
        public const string TrailStartPosition = "TrailStartPosition";
        public const string TrailEndPosition = "TrailEndPosition";
        public const string TrailVelocity = "TrailVelocity";
    }
}
