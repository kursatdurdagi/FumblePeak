using System;
using UnityEngine;
using UnityEngine.VFX;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// How to determine the spawn point for VFX on an agent.
    /// </summary>
    public enum SkillVFXSpawnPointMode
    {
        [Tooltip("Use a simple offset from agent root position (fastest, no hierarchy search)")]
        Offset,
        
        [Tooltip("Use a direct Transform reference from the prefab (recommended - fast and explicit)")]
        Transform,
        
        [Tooltip("Find spawn point by name anywhere in hierarchy (slower, but flexible).\n" +
                 "Example: 'RightHand', 'Weapon_Muzzle'")]
        BoneName
    }
    
    /// <summary>
    /// Where to spawn impact VFX.
    /// </summary>
    public enum SkillImpactVFXLocation
    {
        [Tooltip("Spawn at the target's position")]
        AtTarget,
        
        [Tooltip("Spawn at the ground below the target")]
        AtTargetGround,
        
        [Tooltip("Spawn between caster and target")]
        Midpoint,
        
        [Tooltip("Spawn at a specific world position (for ground-targeted skills)")]
        AtTargetPosition
    }
    
    /// <summary>
    /// Configuration for a single VFX element (cast, impact, trail, etc.)
    /// </summary>
    [Serializable]
    public class SkillVFXConfig
    {
        [Header("VFX Reference")]
        [Tooltip("The visual effect to spawn.")]
        public VFXReference vfx = new VFXReference();
        
        [Header("Spawn Point")]
        [Tooltip("How to determine where to spawn the VFX.")]
        public SkillVFXSpawnPointMode spawnPointMode = SkillVFXSpawnPointMode.Offset;
        
        [Tooltip("Offset from spawn point (local space).\n" +
                 "X = right/left, Y = up/down, Z = forward/back")]
        public Vector3 spawnOffset = new Vector3(0f, 1.5f, 0.5f);
        
        [Tooltip("Name of bone/transform to spawn VFX at.\n" +
                 "Used when spawnPointMode is BoneName.")]
        public string boneName = "";
        
        [Tooltip("Direct transform reference for spawn point.\n" +
                 "Used when spawnPointMode is Transform.")]
        public Transform spawnTransform;
        
        [Header("Timing")]
        [Tooltip("Delay before spawning VFX (for animation sync).\n" +
                 "Example: 0.3s to sync with attack animation wind-up.")]
        [Range(0f, 5f)]
        public float spawnDelay = 0f;
        
        [Tooltip("Override the auto-calculated duration.\n" +
                 "0 = use auto-calculated duration from VFXReference.\n" +
                 "For looping effects, this controls when they stop.")]
        public float durationOverride = 0f;
        
        [Header("Transform")]
        [Tooltip("Scale multiplier for the VFX.")]
        public float scale = 1f;
        
        [Tooltip("Should the VFX follow the caster/target?")]
        public bool attachToParent = false;
        
        [Tooltip("Inherit rotation from spawn point.")]
        public bool inheritRotation = true;
        
        [Header("Performance")]
        [Tooltip("Maximum active instances of this VFX.\n" +
                 "0 = unlimited (use global budget).")]
        public int maxActiveInstances = 0;
        
        [Tooltip("Minimum distance from camera to show VFX.\n" +
                 "VFX beyond this distance are not spawned.")]
        public float maxCameraDistance = 100f;
        
        [Tooltip("Distance at which VFX quality is reduced (LOD).")]
        public float lodDistance = 30f;
        
        /// <summary>
        /// Is this VFX configuration valid and has an effect assigned?
        /// </summary>
        public bool IsValid => vfx != null && vfx.IsValid;
        
        /// <summary>
        /// Gets the effective duration for this VFX.
        /// </summary>
        public float GetDuration()
        {
            if (durationOverride > 0f)
                return durationOverride;
            return vfx?.GetDuration() ?? 2f;
        }
    }
    
    /// <summary>
    /// Configuration for a single SFX element (cast, impact, etc.)
    /// </summary>
    [Serializable]
    public class SkillSFXConfig
    {
        [Header("Audio Clip")]
        [Tooltip("The sound to play.")]
        public AudioClip clip;
        
        [Header("Volume & Pitch")]
        [Tooltip("Base volume for this sound.")]
        [Range(0f, 1f)]
        public float volume = 1f;
        
        [Tooltip("Random pitch variation (±).\n" +
                 "0.1 = pitch varies between 0.9 and 1.1")]
        [Range(0f, 0.5f)]
        public float pitchVariation = 0.1f;
        
        [Tooltip("Base pitch (1 = normal).")]
        [Range(0.5f, 2f)]
        public float basePitch = 1f;
        
        [Header("Spatialization")]
        [Tooltip("Spatial blend (0 = 2D, 1 = full 3D).")]
        [Range(0f, 1f)]
        public float spatialBlend = 0.9f;
        
        [Tooltip("Minimum distance for 3D audio falloff.")]
        public float minDistance = 1f;
        
        [Tooltip("Maximum distance for 3D audio falloff.")]
        public float maxDistance = 50f;
        
        [Header("Performance")]
        [Tooltip("Maximum distance from camera to play sound.\n" +
                 "Sounds beyond this distance are not played.")]
        public float maxCameraDistance = 60f;

        [Tooltip("Maximum concurrent active instances for this SFX clip/config.\n" +
                 "Counts both one-shot and looping instances.\n" +
                 "When the cap is reached, the oldest instance fades out over 0.2s.\n" +
                 "0 = unlimited (global caps still apply).")]
        public int maxActiveInstances = 0;
        
        [Tooltip("Delay before playing sound (for animation sync).")]
        [Range(0f, 5f)]
        public float delay = 0f;
        
        /// <summary>
        /// Is this SFX configuration valid?
        /// </summary>
        public bool IsValid => clip != null;
        
        /// <summary>
        /// Gets a random pitch value with variation applied.
        /// </summary>
        public float GetRandomPitch()
        {
            if (pitchVariation <= 0f)
                return basePitch;
            return basePitch + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        }
    }
    
    /// <summary>
    /// Configuration for Aura/Zone VFX that displays on the ground around the caster.
    /// Perfect for AOE Around Self skills, damage auras, healing circles, etc.
    /// </summary>
    [Serializable]
    public class SkillAuraVFXConfig
    {
        [Header("Aura VFX")]
        [Tooltip("VFX reference for the aura effect.\n" +
                 "Use a ground-based circle, ring, or area effect.")]
        public VFXReference vfx;
        
        [Header("Sizing")]
        [Tooltip("Enable to automatically scale the VFX to match the skill's AOE radius.")]
        public bool scaleToAOERadius = true;
        
        [Tooltip("Base scale when not using AOE radius scaling.\n" +
                 "When scaling to AOE, this acts as a multiplier.")]
        public float baseScale = 1f;
        
        [Tooltip("Additional radius offset (in units).\n" +
                 "Useful for making the visual slightly larger/smaller than the actual AOE.")]
        public float radiusOffset = 0f;
        
        [Header("Positioning")]
        [Tooltip("Height offset from ground level.\n" +
                 "Small positive values (0.01-0.1) prevent z-fighting with terrain.")]
        public float groundOffset = 0.05f;
        
        [Tooltip("Spawn the aura at ground level using raycast.\n" +
                 "Disable for flying units or special cases.")]
        public bool snapToGround = true;
        
        [Tooltip("Layer mask for ground detection when snapping.")]
        public LayerMask groundLayers = 1; // Default layer
        
        [Header("Animation")]
        [Tooltip("Time to fade in the aura (seconds).")]
        [Range(0f, 2f)]
        public float fadeInDuration = 0.3f;
        
        [Tooltip("Time to fade out the aura when skill ends (seconds).")]
        [Range(0f, 2f)]
        public float fadeOutDuration = 0.5f;
        
        [Tooltip("Pulse/breathing animation intensity.\n" +
                 "0 = no pulse, 1 = strong pulse.")]
        [Range(0f, 1f)]
        public float pulseIntensity = 0f;
        
        [Tooltip("Pulse speed (pulses per second).")]
        [Range(0.1f, 5f)]
        public float pulseSpeed = 1f;
        
        [Header("Rotation")]
        [Tooltip("Rotation speed around Y axis (degrees/second).\n" +
                 "0 = no rotation, positive = clockwise.")]
        public float rotationSpeed = 0f;
        
        [Header("Performance")]
        [Tooltip("Maximum distance from camera to show aura VFX.")]
        public float maxCameraDistance = 80f;
        
        [Tooltip("LOD distance - aura may be simplified beyond this range.")]
        public float lodDistance = 40f;
        
        /// <summary>
        /// Is this aura VFX configuration valid?
        /// </summary>
        public bool IsValid => vfx != null && vfx.IsValid;
        
        /// <summary>
        /// Calculate the scale for the aura based on AOE radius.
        /// </summary>
        /// <param name="aoeRadius">The AOE radius from the skill targeting config.</param>
        /// <returns>The scale to apply to the VFX.</returns>
        public float CalculateScale(float aoeRadius)
        {
            if (!scaleToAOERadius || aoeRadius <= 0f)
                return baseScale;
            
            // Assume the base VFX is 1 unit in diameter, so radius 0.5
            // Scale factor = (desiredRadius + offset) / 0.5 * baseScale
            float targetRadius = aoeRadius + radiusOffset;
            return (targetRadius / 0.5f) * baseScale;
        }
        
        /// <summary>
        /// Gets the ground position for spawning the aura.
        /// </summary>
        /// <param name="casterPosition">The caster's world position.</param>
        /// <returns>The position to spawn the aura at.</returns>
        public Vector3 GetSpawnPosition(Vector3 casterPosition)
        {
            if (!snapToGround)
                return new Vector3(casterPosition.x, casterPosition.y + groundOffset, casterPosition.z);
            
            // Raycast down to find ground
            if (Physics.Raycast(casterPosition + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayers))
            {
                return hit.point + Vector3.up * groundOffset;
            }
            
            // Fallback to caster position with offset
            return new Vector3(casterPosition.x, casterPosition.y + groundOffset, casterPosition.z);
        }
    }
    
    /// <summary>
    /// Complete VFX/SFX configuration for a skill.
    /// Groups all visual and audio effects for organized configuration.
    /// </summary>
    [Serializable]
    public class SkillEffectsConfig
    {
        [Header("═══ CAST EFFECTS ═══")]
        [Tooltip("VFX spawned at the caster when skill starts.")]
        public SkillVFXConfig castVFX = new SkillVFXConfig();
        
        [Tooltip("Sound played when skill cast begins.")]
        public SkillSFXConfig castSFX = new SkillSFXConfig();
        
        [Header("═══ IMPACT EFFECTS ═══")]
        [Tooltip("VFX spawned at impact/target location.")]
        public SkillVFXConfig impactVFX = new SkillVFXConfig();
        
        [Tooltip("Where to spawn impact VFX.")]
        public SkillImpactVFXLocation impactLocation = SkillImpactVFXLocation.AtTarget;
        
        [Tooltip("Sound played on impact.")]
        public SkillSFXConfig impactSFX = new SkillSFXConfig();
        
        [Header("═══ PROJECTILE/TRAIL EFFECTS ═══")]
        [Tooltip("VFX for projectile or trail (travels with projectile).")]
        public SkillVFXConfig trailVFX = new SkillVFXConfig();
        
        [Tooltip("Looping sound for projectile travel.")]
        public SkillSFXConfig projectileSFX = new SkillSFXConfig();
        
        [Header("═══ CHANNEL/DURATION EFFECTS ═══")]
        [Tooltip("VFX that plays for the duration of the skill (channeled spells, buffs).")]
        public SkillVFXConfig durationVFX = new SkillVFXConfig();
        
        [Tooltip("Looping sound for duration effects (channeled spells).")]
        public SkillSFXConfig durationSFX = new SkillSFXConfig();
        
        [Header("═══ AURA/ZONE EFFECTS ═══")]
        [Tooltip("Ground-based VFX showing AOE radius. Perfect for damage auras, healing circles, buff zones.\n\n" +
                 "• Spawns at caster's feet (ground level)\n" +
                 "• Can auto-scale to match AOE radius\n" +
                 "• Stays active for skill duration")]
        public SkillAuraVFXConfig auraVFX = new SkillAuraVFXConfig();
        
        [Tooltip("Ambient looping sound for the aura zone.")]
        public SkillSFXConfig auraSFX = new SkillSFXConfig();
    }
    
    /// <summary>
    /// Manages spawning and cleanup of skill VFX with proper lifecycle handling.
    /// </summary>
    public static class SkillVFXManager
    {
        // Track active VFX counts per skill for budget enforcement
        private static System.Collections.Generic.Dictionary<int, int> _activeVFXCounts = 
            new System.Collections.Generic.Dictionary<int, int>();

        private const float SfxVoiceStealFadeDuration = 0.2f;

        private class ActiveOneShotSFX
        {
            public AudioSource audioSource;
            public float startedAtTime;
        }

        private static readonly System.Collections.Generic.List<ActiveOneShotSFX> _activeOneShotSFX =
            new System.Collections.Generic.List<ActiveOneShotSFX>(256);

        private static int _activeGlobalVFXCount;
        private static int _maxGlobalSkillOneShotSFX;
        private static int _maxGlobalSkillLoopingSFX;
        private static int _maxGlobalSkillVFXInstances;

        private class ActiveLoopingSFX
        {
            public AudioSource audioSource;
            public float startedAtTime;
            public int casterIndex;
            public int skillId;
            public int executionId;
            public int networkProjectileId;
            public SkillSFXLoopStopPolicy stopPolicy;
            public string loopKey;
        }

        private static readonly System.Collections.Generic.List<ActiveLoopingSFX> _activeLoopingSFX =
            new System.Collections.Generic.List<ActiveLoopingSFX>(128);
        
        /// <summary>
        /// Spawns a VFX with proper configuration and lifecycle management.
        /// </summary>
        /// <param name="config">VFX configuration</param>
        /// <param name="spawnPosition">World position to spawn at</param>
        /// <param name="spawnRotation">Rotation for the VFX</param>
        /// <param name="parent">Optional parent transform (if attachToParent is true)</param>
        /// <param name="skillId">Skill ID for budget tracking</param>
        /// <returns>The spawned GameObject, or null if budget exceeded or out of range</returns>
        public static GameObject SpawnVFX(
            SkillVFXConfig config,
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            Transform parent = null,
            int skillId = 0)
        {
            if (config == null || !config.IsValid)
                return null;
            
            // Check camera distance
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                float distanceToCamera = Vector3.Distance(spawnPosition, mainCamera.transform.position);
                if (distanceToCamera > config.maxCameraDistance)
                    return null;
            }
            
            // Check budget
            if (config.maxActiveInstances > 0)
            {
                _activeVFXCounts.TryGetValue(skillId, out int count);
                if (count >= config.maxActiveInstances)
                    return null;
            }

            if (_maxGlobalSkillVFXInstances > 0 && _activeGlobalVFXCount >= _maxGlobalSkillVFXInstances)
                return null;
            
            // Spawn based on VFX type
            GameObject vfxInstance = null;
            
            if (config.vfx.IsParticleSystem)
            {
                vfxInstance = UnityEngine.Object.Instantiate(
                    config.vfx.particleSystemPrefab,
                    spawnPosition + (spawnRotation * config.spawnOffset),
                    config.inheritRotation ? spawnRotation : Quaternion.identity
                );
            }
            else if (config.vfx.IsVFXGraph)
            {
                // Create a GameObject with VisualEffect component
                vfxInstance = new GameObject("SkillVFX_" + config.vfx.vfxGraphAsset.name);
                vfxInstance.transform.position = spawnPosition + (spawnRotation * config.spawnOffset);
                vfxInstance.transform.rotation = config.inheritRotation ? spawnRotation : Quaternion.identity;
                
                var visualEffect = vfxInstance.AddComponent<VisualEffect>();
                visualEffect.visualEffectAsset = config.vfx.vfxGraphAsset;
                visualEffect.Play();
            }
            
            if (vfxInstance == null)
                return null;
            
            // Apply scale
            if (config.scale != 1f)
            {
                vfxInstance.transform.localScale *= config.scale;
            }
            
            // Parent if needed
            if (config.attachToParent && parent != null)
            {
                vfxInstance.transform.SetParent(parent, true);
            }
            
            // Track budget
            if (config.maxActiveInstances > 0)
            {
                _activeVFXCounts.TryGetValue(skillId, out int count);
                _activeVFXCounts[skillId] = count + 1;
            }

            _activeGlobalVFXCount++;
            
            // Add auto-cleanup component
            float duration = config.GetDuration();
            var cleanup = vfxInstance.AddComponent<SkillVFXCleanup>();
            cleanup.Initialize(skillId, duration, config.maxActiveInstances > 0, true);
            
            return vfxInstance;
        }
        
        /// <summary>
        /// Plays a skill SFX with proper configuration.
        /// </summary>
        public static AudioSource PlaySFX(SkillSFXConfig config, Vector3 position)
        {
            if (config == null || !config.IsValid)
                return null;
            
            // Check camera distance
            if (!IsWithinCameraRange(config.maxCameraDistance, position))
                return null;

            CleanupInvalidOneShotSFX();
            CleanupInvalidLoopingSFX();
            if (_maxGlobalSkillOneShotSFX > 0 && _activeOneShotSFX.Count >= _maxGlobalSkillOneShotSFX)
                return null;

            if (config.maxActiveInstances > 0 &&
                CountActiveSFXInstances(config.clip) >= config.maxActiveInstances)
            {
                if (!TryFadeOutOldestInstanceForClip(config.clip, SfxVoiceStealFadeDuration))
                    return null;
            }
            
            // Create temporary audio source
            var audioGO = new GameObject("SkillSFX_" + config.clip.name);
            audioGO.transform.position = position;
            
            var audioSource = audioGO.AddComponent<AudioSource>();
            audioSource.clip = config.clip;
            audioSource.volume = config.volume;
            audioSource.pitch = config.GetRandomPitch();
            audioSource.spatialBlend = config.spatialBlend;
            audioSource.minDistance = config.minDistance;
            audioSource.maxDistance = config.maxDistance;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.playOnAwake = false;
            
            if (config.delay > 0f)
            {
                audioSource.PlayDelayed(config.delay);
            }
            else
            {
                audioSource.Play();
            }
            
            // Auto-destroy after clip finishes
            UnityEngine.Object.Destroy(audioGO, config.clip.length + config.delay + 0.1f);
            _activeOneShotSFX.Add(new ActiveOneShotSFX
            {
                audioSource = audioSource,
                startedAtTime = Time.unscaledTime
            });
            
            return audioSource;
        }

        /// <summary>
        /// Plays a looping SFX that can later be stopped by skill lifecycle triggers.
        /// </summary>
        public static AudioSource PlayLoopingSFX(
            SkillSFXConfig config,
            Vector3 position,
            int casterIndex,
            int skillId,
            int executionId,
            int networkProjectileId,
            SkillSFXLoopStopPolicy stopPolicy,
            string loopKey)
        {
            if (config == null || !config.IsValid)
                return null;

            if (!IsWithinCameraRange(config.maxCameraDistance, position))
                return null;

            CleanupInvalidOneShotSFX();
            CleanupInvalidLoopingSFX();
            string normalizedLoopKey = string.IsNullOrEmpty(loopKey) ? string.Empty : loopKey;

            for (int i = 0; i < _activeLoopingSFX.Count; i++)
            {
                var existing = _activeLoopingSFX[i];
                if (existing == null || existing.audioSource == null)
                    continue;

                if (existing.stopPolicy != stopPolicy)
                    continue;

                if (!string.Equals(existing.loopKey, normalizedLoopKey, StringComparison.Ordinal))
                    continue;

                if (!MatchesContext(existing, executionId, networkProjectileId, casterIndex, skillId))
                    continue;

                // Keep one loop instance per (context + key + policy).
                if (existing.audioSource != null)
                {
                    existing.audioSource.transform.position = position;
                    return existing.audioSource;
                }
            }

            if (_maxGlobalSkillLoopingSFX > 0 && _activeLoopingSFX.Count >= _maxGlobalSkillLoopingSFX)
                return null;

            if (config.maxActiveInstances > 0 &&
                CountActiveSFXInstances(config.clip) >= config.maxActiveInstances)
            {
                if (!TryFadeOutOldestInstanceForClip(config.clip, SfxVoiceStealFadeDuration))
                    return null;
            }

            var audioGO = new GameObject("SkillSFXLoop_" + config.clip.name);
            audioGO.transform.position = position;

            var audioSource = audioGO.AddComponent<AudioSource>();
            audioSource.clip = config.clip;
            audioSource.volume = config.volume;
            audioSource.pitch = config.GetRandomPitch();
            audioSource.spatialBlend = config.spatialBlend;
            audioSource.minDistance = config.minDistance;
            audioSource.maxDistance = config.maxDistance;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.playOnAwake = false;
            audioSource.loop = true;

            if (config.delay > 0f)
            {
                audioSource.PlayDelayed(config.delay);
            }
            else
            {
                audioSource.Play();
            }

            _activeLoopingSFX.Add(new ActiveLoopingSFX
            {
                audioSource = audioSource,
                startedAtTime = Time.unscaledTime,
                casterIndex = casterIndex,
                skillId = skillId,
                executionId = executionId,
                networkProjectileId = networkProjectileId,
                stopPolicy = stopPolicy,
                loopKey = normalizedLoopKey
            });

            return audioSource;
        }

        /// <summary>
        /// Stops looping SFX that match a skill execution context.
        /// </summary>
        public static int StopLoopingSFXForContext(
            SkillExecutionContext context,
            SkillSFXLoopStopPolicy? policyFilter,
            string loopKey = "")
        {
            if (context == null)
                return 0;

            CleanupInvalidLoopingSFX();

            int executionId = context.ExecutionId;
            int networkProjectileId = context.NetworkProjectileId;
            int casterIndex = context.Caster?.index ?? -1;
            int skillId = context.Skill != null ? context.Skill.GetInstanceID() : 0;
            string normalizedLoopKey = string.IsNullOrEmpty(loopKey) ? string.Empty : loopKey;
            bool filterByLoopKey = !string.IsNullOrEmpty(normalizedLoopKey);

            int stoppedCount = 0;
            for (int i = _activeLoopingSFX.Count - 1; i >= 0; i--)
            {
                var loop = _activeLoopingSFX[i];
                if (loop == null || loop.audioSource == null)
                {
                    _activeLoopingSFX.RemoveAt(i);
                    continue;
                }

                if (policyFilter.HasValue && loop.stopPolicy != policyFilter.Value)
                    continue;

                if (filterByLoopKey &&
                    !string.Equals(loop.loopKey, normalizedLoopKey, StringComparison.Ordinal))
                    continue;

                if (!MatchesContext(loop, executionId, networkProjectileId, casterIndex, skillId))
                    continue;

                StopAndDestroyLoopAt(i);
                stoppedCount++;
            }

            return stoppedCount;
        }

        /// <summary>
        /// Sets global caps for skill-driven one-shot SFX, looping SFX, and VFX instances.
        /// Set any cap to 0 for unlimited.
        /// </summary>
        public static void SetGlobalCaps(int maxOneShotSFX, int maxLoopingSFX, int maxVFXInstances)
        {
            _maxGlobalSkillOneShotSFX = Mathf.Max(0, maxOneShotSFX);
            _maxGlobalSkillLoopingSFX = Mathf.Max(0, maxLoopingSFX);
            _maxGlobalSkillVFXInstances = Mathf.Max(0, maxVFXInstances);
        }

        /// <summary>
        /// Stops projectile-linked looping SFX configured with OnProjectileDespawn policy.
        /// </summary>
        public static int StopLoopingSFXByProjectile(int networkProjectileId)
        {
            if (networkProjectileId == 0)
                return 0;

            CleanupInvalidLoopingSFX();

            int stoppedCount = 0;
            for (int i = _activeLoopingSFX.Count - 1; i >= 0; i--)
            {
                var loop = _activeLoopingSFX[i];
                if (loop == null || loop.audioSource == null)
                {
                    _activeLoopingSFX.RemoveAt(i);
                    continue;
                }

                if (loop.stopPolicy != SkillSFXLoopStopPolicy.OnProjectileDespawn)
                    continue;

                if (loop.networkProjectileId != networkProjectileId)
                    continue;

                StopAndDestroyLoopAt(i);
                stoppedCount++;
            }

            return stoppedCount;
        }

        private static bool IsWithinCameraRange(float maxDistance, Vector3 position)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                return true;

            float distanceToCamera = Vector3.Distance(position, mainCamera.transform.position);
            return distanceToCamera <= maxDistance;
        }

        private static bool MatchesContext(
            ActiveLoopingSFX loop,
            int executionId,
            int networkProjectileId,
            int casterIndex,
            int skillId)
        {
            if (networkProjectileId != 0)
            {
                // When stopping from a projectile context, prefer projectile-specific matching.
                if (loop.networkProjectileId != 0)
                    return loop.networkProjectileId == networkProjectileId;
                // Non-projectile loops from the same execution can still be matched below.
            }

            if (executionId > 0 && loop.executionId > 0)
                return loop.executionId == executionId;

            if (casterIndex >= 0 && skillId != 0 && loop.casterIndex >= 0 && loop.skillId != 0)
                return loop.casterIndex == casterIndex && loop.skillId == skillId;

            return false;
        }

        private static void CleanupInvalidLoopingSFX()
        {
            for (int i = _activeLoopingSFX.Count - 1; i >= 0; i--)
            {
                if (_activeLoopingSFX[i] == null || _activeLoopingSFX[i].audioSource == null)
                {
                    _activeLoopingSFX.RemoveAt(i);
                }
            }
        }

        private static void CleanupInvalidOneShotSFX()
        {
            for (int i = _activeOneShotSFX.Count - 1; i >= 0; i--)
            {
                var oneShot = _activeOneShotSFX[i];
                if (oneShot == null || oneShot.audioSource == null)
                    _activeOneShotSFX.RemoveAt(i);
            }
        }

        private static int CountActiveSFXInstances(AudioClip clip)
        {
            if (clip == null)
                return 0;

            int count = 0;
            for (int i = 0; i < _activeOneShotSFX.Count; i++)
            {
                var oneShot = _activeOneShotSFX[i];
                if (oneShot?.audioSource != null && oneShot.audioSource.clip == clip)
                    count++;
            }

            for (int i = 0; i < _activeLoopingSFX.Count; i++)
            {
                var loop = _activeLoopingSFX[i];
                if (loop?.audioSource != null && loop.audioSource.clip == clip)
                    count++;
            }

            return count;
        }

        private static bool TryFadeOutOldestInstanceForClip(AudioClip clip, float fadeDuration)
        {
            if (clip == null)
                return false;

            CleanupInvalidOneShotSFX();
            CleanupInvalidLoopingSFX();

            float oldestTime = float.MaxValue;
            int oldestOneShotIndex = -1;
            int oldestLoopIndex = -1;

            for (int i = 0; i < _activeOneShotSFX.Count; i++)
            {
                var oneShot = _activeOneShotSFX[i];
                if (oneShot?.audioSource == null || oneShot.audioSource.clip != clip)
                    continue;

                if (oneShot.startedAtTime < oldestTime)
                {
                    oldestTime = oneShot.startedAtTime;
                    oldestOneShotIndex = i;
                    oldestLoopIndex = -1;
                }
            }

            for (int i = 0; i < _activeLoopingSFX.Count; i++)
            {
                var loop = _activeLoopingSFX[i];
                if (loop?.audioSource == null || loop.audioSource.clip != clip)
                    continue;

                if (loop.startedAtTime < oldestTime)
                {
                    oldestTime = loop.startedAtTime;
                    oldestOneShotIndex = -1;
                    oldestLoopIndex = i;
                }
            }

            AudioSource sourceToFade = null;

            if (oldestOneShotIndex >= 0)
            {
                sourceToFade = _activeOneShotSFX[oldestOneShotIndex]?.audioSource;
                _activeOneShotSFX.RemoveAt(oldestOneShotIndex);
            }
            else if (oldestLoopIndex >= 0)
            {
                sourceToFade = _activeLoopingSFX[oldestLoopIndex]?.audioSource;
                _activeLoopingSFX.RemoveAt(oldestLoopIndex);
            }

            if (sourceToFade == null)
                return false;

            FadeOutAndDestroySource(sourceToFade, fadeDuration);
            return true;
        }

        private static void FadeOutAndDestroySource(AudioSource source, float fadeDuration)
        {
            if (source == null)
                return;

            var fader = source.GetComponent<SkillSFXFadeOut>();
            if (fader == null)
                fader = source.gameObject.AddComponent<SkillSFXFadeOut>();

            fader.Begin(source, fadeDuration);
        }

        private static void StopAndDestroyLoopAt(int index)
        {
            if (index < 0 || index >= _activeLoopingSFX.Count)
                return;

            var loop = _activeLoopingSFX[index];
            _activeLoopingSFX.RemoveAt(index);

            if (loop?.audioSource == null)
                return;

            loop.audioSource.Stop();
            UnityEngine.Object.Destroy(loop.audioSource.gameObject);
        }
        
        /// <summary>
        /// Decrements the active VFX count for a skill.
        /// Called by SkillVFXCleanup component.
        /// </summary>
        internal static void DecrementActiveCount(int skillId)
        {
            if (_activeVFXCounts.TryGetValue(skillId, out int count))
            {
                _activeVFXCounts[skillId] = Mathf.Max(0, count - 1);
            }
        }

        internal static void DecrementGlobalVFXCount()
        {
            _activeGlobalVFXCount = Mathf.Max(0, _activeGlobalVFXCount - 1);
        }
        
        /// <summary>
        /// Resets all VFX budgets. Call when changing scenes.
        /// </summary>
        public static void ResetBudgets()
        {
            _activeVFXCounts.Clear();
            _activeGlobalVFXCount = 0;
            _activeOneShotSFX.Clear();

            for (int i = _activeLoopingSFX.Count - 1; i >= 0; i--)
            {
                var loop = _activeLoopingSFX[i];
                if (loop?.audioSource != null)
                {
                    loop.audioSource.Stop();
                    UnityEngine.Object.Destroy(loop.audioSource.gameObject);
                }
            }
            _activeLoopingSFX.Clear();
        }
    }
    
    /// <summary>
    /// Component that handles proper VFX cleanup - stopping particle systems
    /// gracefully instead of instantly disabling them.
    /// </summary>
    public class SkillVFXCleanup : MonoBehaviour
    {
        private int _skillId;
        private float _duration;
        private float _startTime;
        private bool _trackBudget;
        private bool _trackGlobalBudget;
        private bool _isStopping;
        private ParticleSystem[] _particleSystems;
        private VisualEffect[] _visualEffects;
        
        public void Initialize(int skillId, float duration, bool trackBudget, bool trackGlobalBudget)
        {
            _skillId = skillId;
            _duration = duration;
            _trackBudget = trackBudget;
            _trackGlobalBudget = trackGlobalBudget;
            _startTime = Time.time;
            
            // Cache components
            _particleSystems = GetComponentsInChildren<ParticleSystem>();
            _visualEffects = GetComponentsInChildren<VisualEffect>();
        }
        
        private void Update()
        {
            if (_isStopping) return;
            
            // Check if duration has elapsed
            if (Time.time - _startTime >= _duration)
            {
                StopGracefully();
            }
        }
        
        /// <summary>
        /// Stops the VFX gracefully (particles fade out, not instant disappear).
        /// </summary>
        public void StopGracefully()
        {
            if (_isStopping) return;
            _isStopping = true;
            
            // Stop particle systems (they'll finish emitting then clean up)
            if (_particleSystems != null)
            {
                foreach (var ps in _particleSystems)
                {
                    if (ps != null)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
            
            // Stop VFX Graphs
            if (_visualEffects != null)
            {
                foreach (var vfx in _visualEffects)
                {
                    if (vfx != null)
                    {
                        vfx.Stop();
                    }
                }
            }
            
            // Calculate remaining particle lifetime and destroy after that
            float maxRemainingLifetime = 0f;
            if (_particleSystems != null)
            {
                foreach (var ps in _particleSystems)
                {
                    if (ps != null)
                    {
                        var main = ps.main;
                        float lifetime = GetMaxValue(main.startLifetime);
                        maxRemainingLifetime = Mathf.Max(maxRemainingLifetime, lifetime);
                    }
                }
            }
            
            // Destroy after particles have faded
            Destroy(gameObject, maxRemainingLifetime + 0.5f);
        }
        
        private void OnDestroy()
        {
            if (_trackGlobalBudget)
            {
                SkillVFXManager.DecrementGlobalVFXCount();
                _trackGlobalBudget = false;
            }

            // Decrement budget counter
            if (_trackBudget)
            {
                SkillVFXManager.DecrementActiveCount(_skillId);
                _trackBudget = false;
            }
        }
        
        private static float GetMaxValue(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(curve.constantMin, curve.constantMax);
                default:
                    return curve.constant;
            }
        }
        
        /// <summary>
        /// Stops the VFX immediately (for forced cleanup).
        /// </summary>
        public void StopImmediate()
        {
            if (_trackGlobalBudget)
            {
                SkillVFXManager.DecrementGlobalVFXCount();
                _trackGlobalBudget = false;
            }

            if (_trackBudget)
            {
                SkillVFXManager.DecrementActiveCount(_skillId);
                _trackBudget = false; // Prevent double-decrement
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Fades out an AudioSource over time and then destroys its GameObject.
    /// Used for voice stealing when SFX max instances are reached.
    /// </summary>
    public class SkillSFXFadeOut : MonoBehaviour
    {
        private AudioSource _source;
        private float _startVolume;
        private float _startTime;
        private float _duration;
        private bool _active;

        public void Begin(AudioSource source, float duration)
        {
            _source = source;
            if (_source == null)
            {
                Destroy(gameObject);
                return;
            }

            _startVolume = _source.volume;
            _startTime = Time.unscaledTime;
            _duration = Mathf.Max(0.01f, duration);
            _active = true;
        }

        private void Update()
        {
            if (!_active || _source == null)
            {
                if (_source == null)
                    Destroy(gameObject);
                return;
            }

            float elapsed = Time.unscaledTime - _startTime;
            float t = Mathf.Clamp01(elapsed / _duration);
            _source.volume = Mathf.Lerp(_startVolume, 0f, t);

            if (t < 1f)
                return;

            _source.Stop();
            Destroy(_source.gameObject);
            _active = false;
        }
    }
    
    #region Impact Physics Configuration
    
    /// <summary>
    /// Type of physics impulse to apply on skill impact.
    /// Uses the Fake Physics system (NavMesh Agent Offset).
    /// </summary>
    public enum ImpactPhysicsType
    {
        [Tooltip("No physics effect on impact.")]
        None,
        
        [Tooltip("Push targets away from the caster.\n" +
                 "Direction: Away from caster.\n" +
                 "Example: Shield Bash, Explosion, Force Push.")]
        Push,
        
        [Tooltip("Pull targets toward the caster.\n" +
                 "Direction: Toward caster.\n" +
                 "Example: Death Grip, Grappling Hook, Vacuum.")]
        Pull,
        
        [Tooltip("Launch targets into the air.\n" +
                 "Direction: Upward.\n" +
                 "Example: Uppercut, Earth Spike, Geyser.")]
        Lift,
        
        [Tooltip("Slam targets into the ground.\n" +
                 "Direction: Downward (minimal horizontal).\n" +
                 "Example: Ground Pound, Meteor Strike.")]
        Slam,
        
        [Tooltip("Push targets away + lift them.\n" +
                 "Direction: Away + Up.\n" +
                 "Example: Charge Attack, Explosive Barrel.")]
        Knockback,
        
        [Tooltip("Launch targets forward in caster's facing direction.\n" +
                 "Direction: Caster's forward.\n" +
                 "Example: Shoulder Tackle, Bull Rush.")]
        DirectionalPush,
        
        [Tooltip("Pull targets from caster's facing direction toward caster.\n" +
                 "Direction: Opposite of caster's forward.\n" +
                 "Example: Vacuum Breath, Grappling Hook, Wind Inhale.")]
        DirectionalPull,
        
        [Tooltip("Full custom control over all physics parameters.\n" +
                 "Use when presets don't match your needs.")]
        Custom
    }
    
    /// <summary>
    /// Preset configurations for impact physics force magnitudes.
    /// Direction is controlled by ImpactPhysicsType, not by the preset.
    /// Matches the style used in EnemyCrowd Combat Tab (Charge Attack Knockback).
    /// </summary>
    public enum ImpactPhysicsPreset
    {
        [Tooltip("Light tap - minimal force.\n" +
                 "Force: 3, Lift: 0, Duration: 0.2s")]
        Light,
        
        [Tooltip("Gentle force - soft impact.\n" +
                 "Force: 5, Lift: 0, Duration: 0.3s")]
        Gentle,
        
        [Tooltip("Standard force - moderate impact.\n" +
                 "Force: 6, Lift: 1, Duration: 0.4s")]
        Medium,
        
        [Tooltip("Heavy force - strong impact with lift.\n" +
                 "Force: 10, Lift: 2, Duration: 0.5s")]
        Heavy,
        
        [Tooltip("Strong force - powerful impact.\n" +
                 "Force: 12, Lift: 1, Duration: 0.4s")]
        Strong,
        
        [Tooltip("Massive force - sends targets flying.\n" +
                 "Force: 15, Lift: 4, Duration: 0.7s")]
        Massive,
        
        [Tooltip("Pure vertical launch - uppercut style.\n" +
                 "Force: 2, Lift: 8, Duration: 0.6s")]
        Launch,
        
        [Tooltip("Custom values - use the fields below.")]
        Custom
    }
    
    /// <summary>
    /// Complete configuration for impact physics effects.
    /// Uses the EnemyMasses Fake Physics system (NavMesh Agent Offset).
    /// </summary>
    [Serializable]
    public class ImpactPhysicsConfig
    {
        [Header("Impact Physics")]
        [Tooltip("Enable physics effects when this skill hits targets.")]
        public bool enabled = false;
        
        [Tooltip("Type of physics impulse to apply.\n" +
                 "Each type has different direction behavior.")]
        public ImpactPhysicsType physicsType = ImpactPhysicsType.Push;
        
        [Tooltip("Quick preset for common knockback values.\n" +
                 "Select 'Custom' to use the fields below.")]
        public ImpactPhysicsPreset preset = ImpactPhysicsPreset.Medium;
        
        [Header("Force Settings (Custom)")]
        [Tooltip("Horizontal push/pull force (m/s).\n" +
                 "Positive = push away, Negative = pull toward.\n" +
                 "EnemyCrowd Charge default: 5-6")]
        [Range(-20f, 20f)]
        public float horizontalForce = 6f;
        
        [Tooltip("Vertical lift force (m/s).\n" +
                 "Values >= 1.5 trigger 'knocked up' animation.\n" +
                 "EnemyCrowd Charge default: 2")]
        [Range(-5f, 15f)]
        public float liftForce = 2f;
        
        [Header("Duration & Physics")]
        [Tooltip("Duration of the impulse effect (seconds).\n" +
                 "Longer = further travel distance.\n" +
                 "EnemyCrowd Charge default: 0.5s")]
        [Range(0.1f, 2f)]
        public float duration = 0.5f;
        
        [Tooltip("Gravity applied during impulse (units/s²).\n" +
                 "Higher = faster return to ground.\n" +
                 "Default: 25")]
        [Range(5f, 50f)]
        public float gravity = 25f;
        
        [Tooltip("Horizontal drag/damping.\n" +
                 "Higher = momentum bleeds faster.\n" +
                 "Default: 4")]
        [Range(0f, 15f)]
        public float drag = 4f;
        
        [Header("Behavior")]
        [Tooltip("Stun targets during the impulse?\n" +
                 "Prevents them from attacking while flying.")]
        public bool stunDuring = true;
        
        [Tooltip("Lock target attacks during + after impulse.\n" +
                 "Adds a short recovery period.")]
        public bool lockAttacksDuring = true;
        
        [Tooltip("Additional attack lock time after impulse ends (seconds).")]
        [Range(0f, 2f)]
        public float attackLockRecovery = 0.3f;
        
        /// <summary>
        /// Gets the effective horizontal force magnitude based on preset or custom value.
        /// Always returns positive - direction is controlled by Physics Type.
        /// </summary>
        public float GetHorizontalForce()
        {
            if (preset == ImpactPhysicsPreset.Custom)
                return Mathf.Abs(horizontalForce); // Always positive, direction from Physics Type
            
            return preset switch
            {
                ImpactPhysicsPreset.Light => 3f,
                ImpactPhysicsPreset.Gentle => 5f,
                ImpactPhysicsPreset.Medium => 6f,
                ImpactPhysicsPreset.Heavy => 10f,
                ImpactPhysicsPreset.Strong => 12f,
                ImpactPhysicsPreset.Massive => 15f,
                ImpactPhysicsPreset.Launch => 2f,
                _ => Mathf.Abs(horizontalForce)
            };
        }
        
        /// <summary>
        /// Gets the effective lift force based on preset or custom value.
        /// </summary>
        public float GetLiftForce()
        {
            if (preset == ImpactPhysicsPreset.Custom)
                return liftForce;
            
            return preset switch
            {
                ImpactPhysicsPreset.Light => 0f,
                ImpactPhysicsPreset.Gentle => 0f,
                ImpactPhysicsPreset.Medium => 1f,
                ImpactPhysicsPreset.Heavy => 2f,
                ImpactPhysicsPreset.Strong => 1f,
                ImpactPhysicsPreset.Massive => 4f,
                ImpactPhysicsPreset.Launch => 8f,
                _ => liftForce
            };
        }
        
        /// <summary>
        /// Gets the effective duration based on preset or custom value.
        /// </summary>
        public float GetDuration()
        {
            if (preset == ImpactPhysicsPreset.Custom)
                return duration;
            
            return preset switch
            {
                ImpactPhysicsPreset.Light => 0.2f,
                ImpactPhysicsPreset.Gentle => 0.3f,
                ImpactPhysicsPreset.Medium => 0.4f,
                ImpactPhysicsPreset.Heavy => 0.5f,
                ImpactPhysicsPreset.Strong => 0.4f,
                ImpactPhysicsPreset.Massive => 0.7f,
                ImpactPhysicsPreset.Launch => 0.6f,
                _ => duration
            };
        }
        
        /// <summary>
        /// Gets the effective gravity based on preset.
        /// </summary>
        public float GetGravity()
        {
            if (preset == ImpactPhysicsPreset.Custom)
                return gravity;
            
            // Most presets use default gravity
            return preset switch
            {
                ImpactPhysicsPreset.Launch => 15f, // Lower gravity for longer air time
                ImpactPhysicsPreset.Massive => 20f,
                _ => 25f
            };
        }
        
        /// <summary>
        /// Gets the effective drag based on preset.
        /// </summary>
        public float GetDrag()
        {
            if (preset == ImpactPhysicsPreset.Custom)
                return drag;
            
            return preset switch
            {
                ImpactPhysicsPreset.Light => 6f, // Higher drag, stops faster
                ImpactPhysicsPreset.Gentle => 5f,
                ImpactPhysicsPreset.Medium => 4f,
                ImpactPhysicsPreset.Heavy => 3f,
                ImpactPhysicsPreset.Strong => 3f,
                ImpactPhysicsPreset.Massive => 2f, // Low drag, slides further
                ImpactPhysicsPreset.Launch => 1f, // Minimal drag for air time
                _ => 4f
            };
        }
        
        /// <summary>
        /// Calculate the velocity vector based on physics type and positions.
        /// Physics Type controls direction, Preset/Custom controls magnitude.
        /// </summary>
        public Vector3 CalculateVelocity(Vector3 casterPosition, Vector3 targetPosition, Vector3 casterForward)
        {
            // GetHorizontalForce() now always returns positive magnitude
            float hForce = GetHorizontalForce();
            float vForce = GetLiftForce();
            
            Vector3 horizontal;
            
            switch (physicsType)
            {
                case ImpactPhysicsType.Push:
                case ImpactPhysicsType.Knockback:
                    // Push away from caster
                    horizontal = Vector3.ProjectOnPlane(targetPosition - casterPosition, Vector3.up).normalized;
                    break;
                    
                case ImpactPhysicsType.Pull:
                    // Pull toward caster (direction reversed, force stays positive)
                    horizontal = Vector3.ProjectOnPlane(casterPosition - targetPosition, Vector3.up).normalized;
                    break;
                    
                case ImpactPhysicsType.Lift:
                    // Minimal horizontal, mostly vertical
                    horizontal = Vector3.ProjectOnPlane(targetPosition - casterPosition, Vector3.up).normalized;
                    hForce *= 0.2f; // Reduce horizontal component
                    break;
                    
                case ImpactPhysicsType.Slam:
                    // Downward force with minimal horizontal
                    horizontal = Vector3.ProjectOnPlane(targetPosition - casterPosition, Vector3.up).normalized;
                    hForce *= 0.3f;
                    vForce = -Mathf.Abs(vForce) * 0.5f; // Downward force (less than gravity)
                    break;
                    
                case ImpactPhysicsType.DirectionalPush:
                    // Push in caster's facing direction
                    horizontal = Vector3.ProjectOnPlane(casterForward, Vector3.up).normalized;
                    break;
                    
                case ImpactPhysicsType.DirectionalPull:
                    // Pull from caster's facing direction (opposite of forward)
                    horizontal = Vector3.ProjectOnPlane(-casterForward, Vector3.up).normalized;
                    break;
                    
                case ImpactPhysicsType.Custom:
                default:
                    // Custom: use raw horizontalForce value (can be negative for pull)
                    horizontal = Vector3.ProjectOnPlane(targetPosition - casterPosition, Vector3.up).normalized;
                    // For Custom type, allow negative force to reverse direction
                    if (preset == ImpactPhysicsPreset.Custom && horizontalForce < 0)
                    {
                        horizontal = -horizontal;
                        hForce = Mathf.Abs(horizontalForce);
                    }
                    break;
            }
            
            // Fallback if direction is zero
            if (horizontal == Vector3.zero)
                horizontal = Vector3.forward;
            
            return horizontal * hForce + Vector3.up * vForce;
        }
    }
    
    #endregion
}
