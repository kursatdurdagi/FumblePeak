using UnityEngine;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration for melee attack effects (swing VFX/audio and hit impact VFX/audio).
    /// Enables visual/audio feedback for melee attacks without requiring Animation Events.
    /// </summary>
    [System.Serializable]
    public class MeleeAttackEffectsConfig
    {
        [Header("Enable Melee Effects")]
        [Tooltip("Enable VFX and audio effects for melee attacks.")]
        public bool enabled = false;
        
        [Header("Swing/Attack Effects")]
        [Tooltip("VFX to spawn when the agent swings their weapon.\n" +
                 "- ParticleSystem: Legacy CPU-based particles (slash trails, weapon glow)\n" +
                 "- VFX Graph: GPU-accelerated particles (recommended for large crowds)")]
        public VFXReference swingVFX = new VFXReference { type = VFXType.None, defaultDuration = 0.5f };
        
        [Tooltip("How long the swing VFX should remain (0 = use particle system duration)")]
        [Range(0f, 2f)]
        public float swingVFXDuration = 0f;
        
        [Tooltip("Offset from agent root where swing VFX spawns (typically weapon position)")]
        public Vector3 swingVFXOffset = new Vector3(0f, 1f, 0.5f);
        
        [Tooltip("Sound to play when the agent swings their weapon (whoosh, slash, grunt)")]
        public AudioClip swingSound;
        
        [Tooltip("Volume of the swing sound")]
        [Range(0f, 1f)]
        public float swingSoundVolume = 0.8f;
        
        [Tooltip("Random pitch variation for swing sound (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float swingSoundPitchVariation = 0.1f;
        
        [Tooltip("Spatial blend for swing sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float swingSoundSpatialBlend = 1f;
        
        [Tooltip("Delay from attack animation start before swing effects play (sync with animation)")]
        [Range(0f, 1f)]
        public float swingEffectDelay = 0.1f;
        
        [Header("Hit Impact Effects")]
        [Tooltip("VFX to spawn at the hit point when the melee attack connects.\n" +
                 "- ParticleSystem: Blood splatter, sparks, dust clouds\n" +
                 "- VFX Graph: GPU-accelerated impact effects")]
        public VFXReference hitImpactVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Tooltip("How long the impact VFX should remain (0 = use particle system duration)")]
        [Range(0f, 2f)]
        public float hitImpactVFXDuration = 0f;
        
        [Tooltip("Sound to play when the melee attack hits a target (thud, slash, crunch)")]
        public AudioClip hitImpactSound;
        
        [Tooltip("Volume of the impact sound")]
        [Range(0f, 1f)]
        public float hitImpactSoundVolume = 1f;
        
        [Tooltip("Random pitch variation for impact sound (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float hitImpactSoundPitchVariation = 0.1f;
        
        [Tooltip("Spatial blend for impact sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float hitImpactSoundSpatialBlend = 1f;
        
        /// <summary>
        /// Gets the effective swing VFX reference.
        /// </summary>
        public VFXReference GetEffectiveSwingVFX()
        {
            if (swingVFX != null && swingVFX.IsValid)
                return swingVFX;
            return null;
        }
        
        /// <summary>
        /// Gets the effective hit impact VFX reference.
        /// </summary>
        public VFXReference GetEffectiveHitImpactVFX()
        {
            if (hitImpactVFX != null && hitImpactVFX.IsValid)
                return hitImpactVFX;
            return null;
        }
    }

    /// <summary>
    /// Configuration for death-time effects (VFX and audio) played when an agent dies.
    /// Mirrors the melee effects workflow so designers can pick VFX type and control spawn offsets.
    /// </summary>
    [System.Serializable]
    public class DeathEffectsConfig
    {
        [Header("Enable Death Effects")]
        [Tooltip("Enable VFX and audio when an agent dies.")]
        public bool enabled = true;

        [Header("Renderer Binding (Particle Shape)")]
        [Tooltip("Bind particle shape to a renderer (SkinnedMeshRenderer/MeshRenderer). Only applies to ParticleSystem death VFX.")]
        public bool bindShapeToRenderer = false;

        [Tooltip("Prefer SkinnedMeshRenderer when searching for a renderer (falls back to MeshRenderer if not found)." )]
        public bool preferSkinnedRenderer = true;

        [Tooltip("Automatically pick the first renderer found (SkinnedMeshRenderer first if preferred). If false, uses the name filter only.")]
        public bool autoFindFirstRenderer = true;

        [Tooltip("Optional renderer GameObject name to bind the particle shape to. Exact match; searched before auto-pick.")]
        public string rendererName;
        
        [Header("Visuals")]
        [Tooltip("VFX to spawn when the agent dies. Supports ParticleSystem prefabs and VFX Graph assets.")]
        public VFXReference deathVFX = new VFXReference { type = VFXType.None, defaultDuration = 2f };
        
        [Tooltip("How long the death VFX should remain (0 = use asset duration)")]
        [Range(0f, 5f)]
        public float deathVFXDuration = 0f;
        
        [Tooltip("Local-space offset from the agent root where the death VFX is spawned.")]
        public Vector3 deathVFXOffset = Vector3.zero;
        
        [Tooltip("Legacy particle prefab fallback for death VFX (keeps pre-existing setups working).")]
        public GameObject legacyDeathEffectPrefab;
        
        [Header("Audio")]
        [Tooltip("Volume applied to death sounds.")]
        [Range(0f, 1f)]
        public float deathSoundVolume = 1f;
        
        [Tooltip("Random pitch variation for death sounds (±). 0.1 = 10% up/down.")]
        [Range(0f, 0.5f)]
        public float deathSoundPitchVariation = 0.1f;
        
        [Tooltip("Spatial blend for death sounds (0 = 2D, 1 = 3D).")]
        [Range(0f, 1f)]
        public float deathSoundSpatialBlend = 1f;

        /// <summary>
        /// Returns true if any VFX is configured (new VFXReference or legacy prefab).
        /// </summary>
        public bool HasAnyVFX(GameObject legacyPrefab = null)
        {
            return (deathVFX != null && deathVFX.IsValid) || legacyDeathEffectPrefab != null || legacyPrefab != null;
        }
        
        /// <summary>
        /// Resolves the VFX to use, preferring the new VFXReference and falling back to the legacy prefab.
        /// </summary>
        public VFXReference GetEffectiveDeathVFX(GameObject legacyPrefab = null)
        {
            if (deathVFX != null && deathVFX.IsValid)
                return deathVFX;

            GameObject prefabToUse = legacyDeathEffectPrefab != null ? legacyDeathEffectPrefab : legacyPrefab;
            if (prefabToUse == null)
                return null;

            float duration = deathVFXDuration > 0f
                ? deathVFXDuration
                : VFXReference.CalculateParticleSystemDuration(prefabToUse);
            
            return new VFXReference
            {
                type = VFXType.ParticleSystem,
                particleSystemPrefab = prefabToUse,
                defaultDuration = duration
            };
        }
    }
    
    /// <summary>
    /// Configuration for hitscan attack effects (muzzle flash, sounds, impact VFX).
    /// Used when rangedAttackMode is Hitscan for visual/audio feedback.
    /// </summary>
    [System.Serializable]
    public class HitscanEffectsConfig
    {
        [Header("Spawn Point Settings")]
        [Tooltip("How to determine the muzzle/spawn point for VFX:\n" +
                 "- Offset: Use spawnOffset from agent root (simple, fast)\n" +
                 "- Transform: Reference a transform in the prefab (recommended)\n" +
                 "- BonePath: Find bone by path in hierarchy\n" +
                 "- BoneName: Search for bone by name anywhere in hierarchy")]
        public ProjectileSpawnPointMode spawnPointMode = ProjectileSpawnPointMode.Offset;
        
        [Tooltip("Direct reference to the muzzle transform in the agent prefab.\n" +
                 "Drag the muzzle/gun barrel bone from your prefab here.\n" +
                 "Used when spawnPointMode is Transform.")]
        public Transform spawnPointTransform;
        
        [Tooltip("Path to the muzzle transform relative to agent root.\n" +
                 "Example: 'Armature/Hips/Spine/Chest/RightArm/RightHand/Gun_Muzzle'\n" +
                 "Used when spawnPointMode is BonePath.")]
        public string spawnBonePath = "";
        
        [Tooltip("Name of the bone/transform to use as muzzle point.\n" +
                 "Example: 'Gun_Muzzle', 'Muzzle_Flash_Point'\n" +
                 "Used when spawnPointMode is BoneName.")]
        public string spawnBoneName = "";
        
        [Tooltip("Fallback offset from agent position if bone is not found, or primary offset when using Offset mode")]
        public Vector3 spawnOffset = new Vector3(0f, 1.5f, 0.5f);
        
        [Header("Muzzle Effects")]
        [Tooltip("VFX for muzzle flash when firing.\n" +
                 "- ParticleSystem: Legacy CPU-based particles (easier setup)\n" +
                 "- VFX Graph: GPU-accelerated particles (better performance for mass battles)")]
        public VFXReference muzzleVFX = new VFXReference { type = VFXType.None, defaultDuration = 0.5f };
        
        [Tooltip("(Legacy) VFX prefab to spawn at muzzle when firing. Use muzzleVFX instead for new setups.")]
        [System.Obsolete("Use muzzleVFX instead")]
        public GameObject muzzleVFXPrefab;
        
        [Tooltip("How long the muzzle VFX should remain before being destroyed (0 = use particle system duration)")]
        [Range(0f, 2f)]
        public float muzzleVFXDuration = 0f;
        
        [Tooltip("Sound to play when firing (gunshot, laser zap, etc.)")]
        public AudioClip fireSound;
        
        [Tooltip("Volume of the fire sound")]
        [Range(0f, 1f)]
        public float fireSoundVolume = 1f;
        
        [Tooltip("Random pitch variation range for fire sound (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float fireSoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for fire sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float fireSoundSpatialBlend = 1f;
        
        [Header("Impact Effects")]
        [Tooltip("VFX for hit impact effect (sparks, blood, dust).\n" +
                 "- ParticleSystem: Legacy CPU-based particles (easier setup)\n" +
                 "- VFX Graph: GPU-accelerated particles (better performance for mass battles)")]
        public VFXReference impactVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Tooltip("(Legacy) VFX prefab to spawn at hit location. Use impactVFX instead for new setups.")]
        [System.Obsolete("Use impactVFX instead")]
        public GameObject impactVFXPrefab;
        
        [Tooltip("How long the impact VFX should remain before being destroyed (0 = use particle system duration)")]
        [Range(0f, 2f)]
        public float impactVFXDuration = 0f;
        
        [Tooltip("Sound to play on hit")]
        public AudioClip impactSound;
        
        [Tooltip("Volume of the impact sound")]
        [Range(0f, 1f)]
        public float impactSoundVolume = 1f;
        
        [Tooltip("Random pitch variation range for impact sound (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float impactSoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for impact sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float impactSoundSpatialBlend = 1f;
        
        [Header("Timing")]
        [Tooltip("Time delay after attack animation starts before effects spawn (sync with fire animation)")]
        [Range(0f, 2f)]
        public float effectDelay = 0.1f;
        
        [Header("Piercing (Penetration)")]
        [Tooltip("Enable piercing for hitscan attacks.\n" +
                 "When enabled, the raycast continues through enemies,\n" +
                 "damaging all in the line of fire (e.g., railgun, piercing laser).")]
        public bool enablePiercing = false;
        
        [Tooltip("Maximum number of enemies the hitscan can pierce through.\n" +
                 "0 = unlimited (pierce all enemies in range)")]
        [Min(0)]
        public int maxPierceTargets = 3;
        
        [Tooltip("Damage multiplier applied to each subsequent target.\n" +
                 "1.0 = same damage for all, 0.8 = 20% less each target.\n" +
                 "E.g., 0.8 means: 100% → 80% → 64% → 51%...")]
        [Range(0.1f, 1f)]
        public float pierceDamageFalloff = 0.8f;
        
        [Tooltip("VFX to spawn at each pierce point (optional).\n" +
                 "If not set, uses the main impact VFX.")]
        public VFXReference pierceVFX = new VFXReference { type = VFXType.None, defaultDuration = 0.5f };
        
        [Header("AOE/Splash Damage")]
        [Tooltip("Enable area of effect damage on impact.\n" +
                 "Use for: Shotgun spread, explosive rounds, energy weapons")]
        public bool enableAOE = false;
        
        [Tooltip("Area of effect radius for splash damage (0 = single target only).\n" +
                 "Use for: Shotgun spread, explosive rounds, energy weapons")]
        [Range(0f, 20f)]
        public float aoeRadius = 0f;
        
        [Tooltip("Splash damage amount (separate from main target damage).\n" +
                 "Agents within AOE radius will receive this damage with falloff based on distance.")]
        [Range(0f, 1000f)]
        public float aoeDamage = 0f;
        
        [Tooltip("Minimum damage multiplier at the edge of AOE radius.\n" +
                 "0.3 = 30% damage at edge, full damage at center")]
        [Range(0f, 1f)]
        public float aoeFalloffMinDamage = 0.3f;
        
        [Tooltip("VFX for AOE explosion effect (separate from impact VFX).\n" +
                 "- ParticleSystem: Legacy CPU-based particles\n" +
                 "- VFX Graph: GPU-accelerated (recommended for explosions)")]
        public VFXReference aoeVFX = new VFXReference { type = VFXType.None, defaultDuration = 2f };
        
        [Tooltip("(Legacy) VFX prefab for AOE explosion. Use aoeVFX instead for new setups.")]
        [System.Obsolete("Use aoeVFX instead")]
        public GameObject aoeVFXPrefab;
        
        /// <summary>
        /// Gets the effective muzzle VFX reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveMuzzleVFX()
        {
            if (muzzleVFX != null && muzzleVFX.IsValid)
                return muzzleVFX;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (muzzleVFXPrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = muzzleVFXPrefab,
                    defaultDuration = muzzleVFXDuration > 0 ? muzzleVFXDuration : 0.5f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
        
        /// <summary>
        /// Gets the effective impact VFX reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveImpactVFX()
        {
            if (impactVFX != null && impactVFX.IsValid)
                return impactVFX;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (impactVFXPrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = impactVFXPrefab,
                    defaultDuration = impactVFXDuration > 0 ? impactVFXDuration : 1f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
        
        /// <summary>
        /// Gets the effective AOE VFX reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveAoeVFX()
        {
            if (aoeVFX != null && aoeVFX.IsValid)
                return aoeVFX;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (aoeVFXPrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = aoeVFXPrefab,
                    defaultDuration = 2f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
    }

    /// <summary>
    /// Configuration for projectile attacks used by EnemyCrowd factions.
    /// Attach to EnemyCrowd ScriptableObject to enable ranged projectile attacks.
    /// </summary>
    [System.Serializable]
    public class ProjectileAttackConfig
    {
        [Header("Projectile Type")]
        [Tooltip("Type of projectile trajectory:\n" +
                 "- Linear: Straight line (arrows, bullets, magic bolts)\n" +
                 "- Parabolic: Arc trajectory (grenades, thrown weapons)")]
        public ProjectileTrajectoryType trajectoryType = ProjectileTrajectoryType.Linear;
        
        [Header("Visual Settings")]
        [Tooltip("Projectile visual.\n" +
                 "- ParticleSystem: Legacy CPU-based prefab (arrows, bullets, magic bolts)\n" +
                 "- VFX Graph: GPU-accelerated (recommended for mass projectiles)")]
        public VFXReference projectileVisual = new VFXReference { type = VFXType.None, defaultDuration = 5f };
        
        [Tooltip("(Legacy) Prefab to spawn for projectile visual. Use projectileVisual instead for new setups.")]
        [System.Obsolete("Use projectileVisual instead")]
        public GameObject projectilePrefab;
        
        [Tooltip("Scale of the projectile visual")]
        [Range(0.1f, 5f)]
        public float visualScale = 1f;
        
        [Tooltip("VFX for projectile impact (explosion, sparks, etc.).\n" +
                 "- ParticleSystem: Legacy CPU-based particles\n" +
                 "- VFX Graph: GPU-accelerated (recommended for high-frequency effects)")]
        public VFXReference impactVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Tooltip("(Legacy) VFX prefab to spawn on projectile hit. Use impactVFX instead for new setups.")]
        [System.Obsolete("Use impactVFX instead")]
        public GameObject impactVFXPrefab;
        
        [Tooltip("VFX for projectile trail (optional).\n" +
                 "- ParticleSystem: Legacy CPU-based trails\n" +
                 "- VFX Graph: GPU-accelerated trails (better for mass projectiles)")]
        public VFXReference trailVFX = new VFXReference { type = VFXType.None, defaultDuration = 3f };
        
        [Tooltip("(Legacy) VFX prefab for projectile trail. Use trailVFX instead for new setups.")]
        [System.Obsolete("Use trailVFX instead")]
        public GameObject trailVFXPrefab;
        
        [Header("Linear Projectile Settings")]
        [Tooltip("Speed of linear projectiles (units per second)")]
        [Range(5f, 200f)]
        public float projectileSpeed = 30f;
        
        [Header("Piercing (Linear Only)")]
        [Tooltip("Enable piercing for linear projectiles.\\n" +
                 "When enabled, projectiles continue through enemies,\\n" +
                 "damaging all in their path (e.g., arrows, bolts, magic missiles).\\n" +
                 "NOTE: Only applies to Linear trajectory, not Parabolic/Arc.")]
        public bool enablePiercing = false;
        
        [Tooltip("Maximum number of enemies the projectile can pierce through.\\n" +
                 "0 = unlimited (pierce all enemies until max range)")]
        [Min(0)]
        public int maxPierceTargets = 3;
        
        [Tooltip("Damage multiplier applied to each subsequent target.\\n" +
                 "1.0 = same damage for all, 0.8 = 20% less each target.\\n" +
                 "E.g., 0.8 means: 100% → 80% → 64% → 51%...")]
        [Range(0.1f, 1f)]
        public float pierceDamageFalloff = 0.8f;
        
        [Tooltip("VFX to spawn at each pierce point (optional).\\n" +
                 "If not set, uses the main impact VFX.")]
        public VFXReference pierceVFX = new VFXReference { type = VFXType.None, defaultDuration = 0.5f };
        
        [Header("Parabolic Projectile Settings")]
        [Tooltip("Launch angle for parabolic projectiles (degrees)")]
        [Range(15f, 75f)]
        public float launchAngle = 45f;
        
        [Tooltip("Fixed flight time (seconds). If 0, calculated from angle.")]
        [Range(0f, 5f)]
        public float fixedFlightTime = 0f;
        
        [Header("Damage Settings")]
        [Tooltip("Base damage per projectile hit")]
        [Range(1f, 1000f)]
        public float damage = 10f;
        
        [Header("AOE/Splash Damage")]
        [Tooltip("Enable area of effect damage on impact.\n" +
                 "Use for: Rockets, explosive arrows, grenades, magic explosions")]
        public bool enableAOE = false;
        
        [Tooltip("Area of effect radius (0 = single target)")]
        [Range(0f, 20f)]
        public float aoeRadius = 0f;
        
        [Tooltip("Damage falloff at AOE edge (multiplier)\n" +
                 "0.3 = 30% damage at edge, 100% at center")]
        [Range(0f, 1f)]
        public float aoeFalloffMinDamage = 0.3f;
        
        [Tooltip("VFX for AOE explosion effect (separate from impact VFX).\n" +
                 "- ParticleSystem: Legacy CPU-based particles\n" +
                 "- VFX Graph: GPU-accelerated (highly recommended for explosions)")]
        public VFXReference aoeVFX = new VFXReference { type = VFXType.None, defaultDuration = 2f };
        
        [Tooltip("(Legacy) VFX prefab for AOE explosion. Use aoeVFX instead for new setups.")]
        [System.Obsolete("Use aoeVFX instead")]
        public GameObject aoeVFXPrefab;
        
        [Header("Spawn Point Settings")]
        [Tooltip("How to determine the projectile spawn point:\n" +
                 "- Offset: Use spawnOffset from agent root (simple, fast)\n" +
                 "- Transform: Reference a transform in the prefab (recommended)\n" +
                 "- BonePath: Find bone by path in hierarchy\n" +
                 "- BoneName: Search for bone by name anywhere in hierarchy")]
        public ProjectileSpawnPointMode spawnPointMode = ProjectileSpawnPointMode.Offset;
        
        [Tooltip("Direct reference to the spawn point transform in the agent prefab.\n" +
                 "Drag the muzzle/hand bone from your prefab here.\n" +
                 "Used when spawnPointMode is Transform.")]
        public Transform spawnPointTransform;
        
        [Tooltip("Path to the spawn point transform relative to agent root (e.g., 'Armature/Hips/Spine/Chest/RightShoulder/RightArm/RightHand/Gun_Muzzle').\n" +
                 "Used when spawnPointMode is BonePath.")]
        public string spawnBonePath = "";
        
        [Tooltip("Name of the bone/transform to use as spawn point (e.g., 'Gun_Muzzle', 'RightHand').\n" +
                 "Used when spawnPointMode is BoneName. Searches entire hierarchy.")]
        public string spawnBoneName = "";
        
        [Tooltip("Fallback offset from agent position if bone is not found, or primary offset when using Offset mode")]
        public Vector3 spawnOffset = new Vector3(0f, 1.5f, 0.5f);
        
        [Tooltip("Time delay after attack animation starts before projectile spawns (sync with throw/fire animation)")]
        [Range(0f, 2f)]
        public float spawnDelay = 0.3f;
        
        [Header("Audio")]
        [Tooltip("Sound to play when projectile is fired")]
        public AudioClip fireSound;
        
        [Tooltip("Volume of the fire sound")]
        [Range(0f, 1f)]
        public float fireSoundVolume = 1f;
        
        [Tooltip("Random pitch variation range for fire sound (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float fireSoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for fire sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float fireSoundSpatialBlend = 1f;
        
        [Tooltip("Sound to play when projectile hits")]
        public AudioClip impactSound;
        
        [Tooltip("Volume of the impact sound")]
        [Range(0f, 1f)]
        public float impactSoundVolume = 1f;
        
        [Tooltip("Random pitch variation range for impact sound (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float impactSoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for impact sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float impactSoundSpatialBlend = 1f;
        
        /// <summary>
        /// Creates a default config for arrow/bolt type projectiles.
        /// </summary>
        public static ProjectileAttackConfig CreateArrowConfig()
        {
            return new ProjectileAttackConfig
            {
                trajectoryType = ProjectileTrajectoryType.Linear,
                projectileSpeed = 40f,
                damage = 15f,
                aoeRadius = 0f,
                spawnOffset = new Vector3(0f, 1.5f, 0.3f),
                spawnDelay = 0.4f
            };
        }
        
        /// <summary>
        /// Creates a default config for grenade type projectiles.
        /// </summary>
        public static ProjectileAttackConfig CreateGrenadeConfig()
        {
            return new ProjectileAttackConfig
            {
                trajectoryType = ProjectileTrajectoryType.Parabolic,
                launchAngle = 50f,
                damage = 30f,
                aoeRadius = 5f,
                aoeFalloffMinDamage = 0.3f,
                spawnOffset = new Vector3(0f, 1.2f, 0.3f),
                spawnDelay = 0.5f
            };
        }
        
        /// <summary>
        /// Creates a default config for magic bolt type projectiles.
        /// </summary>
        public static ProjectileAttackConfig CreateMagicBoltConfig()
        {
            return new ProjectileAttackConfig
            {
                trajectoryType = ProjectileTrajectoryType.Linear,
                projectileSpeed = 25f,
                damage = 20f,
                aoeRadius = 0f,
                spawnOffset = new Vector3(0f, 1.3f, 0.5f),
                spawnDelay = 0.6f
            };
        }
        
        /// <summary>
        /// Gets the effective impact VFX reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveImpactVFX()
        {
            if (impactVFX != null && impactVFX.IsValid)
                return impactVFX;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (impactVFXPrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = impactVFXPrefab,
                    defaultDuration = 1f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
        
        /// <summary>
        /// Gets the effective projectile visual reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveProjectileVisual()
        {
            if (projectileVisual != null && projectileVisual.IsValid)
                return projectileVisual;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (projectilePrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = projectilePrefab,
                    defaultDuration = 5f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
        
        /// <summary>
        /// Gets the effective trail VFX reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveTrailVFX()
        {
            if (trailVFX != null && trailVFX.IsValid)
                return trailVFX;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (trailVFXPrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = trailVFXPrefab,
                    defaultDuration = 3f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
        
        /// <summary>
        /// Gets the effective AOE VFX reference (new VFXReference or legacy prefab).
        /// </summary>
        public VFXReference GetEffectiveAoeVFX()
        {
            if (aoeVFX != null && aoeVFX.IsValid)
                return aoeVFX;
            
            // Fall back to legacy prefab
            #pragma warning disable 0618
            if (aoeVFXPrefab != null)
            {
                return new VFXReference
                {
                    type = VFXType.ParticleSystem,
                    particleSystemPrefab = aoeVFXPrefab,
                    defaultDuration = 2f
                };
            }
            #pragma warning restore 0618
            
            return null;
        }
    }
    
    /// <summary>
    /// How to determine the projectile spawn point on an agent.
    /// </summary>
    public enum ProjectileSpawnPointMode
    {
        /// <summary>
        /// Use a simple offset from agent root position (fastest, no hierarchy search)
        /// </summary>
        Offset,
        
        /// <summary>
        /// Use a direct Transform reference from the prefab (recommended - fast and explicit)
        /// </summary>
        Transform,
        
        /// <summary>
        /// Find spawn point by exact path in hierarchy (fast after initial cache)
        /// Example: "Armature/Hips/Spine/Chest/RightArm/RightHand/Gun_Muzzle"
        /// </summary>
        BonePath,
        
        /// <summary>
        /// Search for spawn point by name anywhere in hierarchy (slower, but flexible)
        /// Example: "Gun_Muzzle" or "RightHand"
        /// </summary>
        BoneName
    }
    
    /// <summary>
    /// Type of projectile trajectory.
    /// </summary>
    public enum ProjectileTrajectoryType
    {
        /// <summary>
        /// Straight line trajectory (arrows, bullets, magic bolts)
        /// </summary>
        Linear,
        
        /// <summary>
        /// Arc trajectory following gravity (grenades, thrown weapons)
        /// </summary>
        Parabolic
    }
}
