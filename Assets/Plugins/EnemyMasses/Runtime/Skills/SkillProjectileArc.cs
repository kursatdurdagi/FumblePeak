using System;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Networking;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Trajectory type for skill projectiles.
    /// </summary>
    public enum SkillProjectileTrajectory
    {
        [Tooltip("Straight line trajectory (arrows, bullets, magic bolts)")]
        Linear,
        [Tooltip("Parabolic arc trajectory (grenades, catapults, thrown bombs)")]
        Arc,
        [Tooltip("Homing projectile that tracks target (guided missiles)")]
        Homing
    }

    /// <summary>
    /// Determines what happens when a projectile reaches its designated primary target.
    /// </summary>
    public enum ProjectileTargetHitBehavior
    {
        [Tooltip("Projectile resolves impact immediately on primary target and fades out.")]
        StopAndFadeOnHit,
        [Tooltip("Projectile passes through the primary target and keeps traveling.")]
        PierceThrough,
        [Tooltip("Projectile resolves impact immediately on primary target and despawns instantly (no fade).")]
        StopWithoutFadeOnHit,
        [Tooltip("Projectile ignores non-target enemies in its path and stops only when it reaches the designated target, then fades out.")]
        StopOnDesignatedTargetWithFade,
        [Tooltip("Projectile ignores non-target enemies in its path and stops only when it reaches the designated target, then despawns instantly.")]
        StopOnDesignatedTargetWithoutFade
    }
    
    /// <summary>
    /// How the projectile arc height is calculated.
    /// </summary>
    public enum ArcHeightMode
    {
        [Tooltip("Use a fixed launch angle (realistic ballistics)")]
        LaunchAngle,
        [Tooltip("Use a fixed flight time (guaranteed arrival)")]
        FlightTime,
        [Tooltip("Use a fixed arc height regardless of distance")]
        FixedHeight
    }
    
    /// <summary>
    /// Targeting configuration for Projectile-type skills.
    /// Defines WHO gets hit when the projectile lands.
    /// Damage/effects are applied through the Effects list.
    /// </summary>
    [Serializable]
    public class ProjectileTargetingConfig
    {
        [Header("Trajectory")]
        [Tooltip("Type of projectile trajectory.\n" +
                 "• Linear: Straight line (arrows, bullets, magic bolts)\n" +
                 "• Arc: Parabolic arc (grenades, catapults, thrown bombs)\n" +
                 "• Homing: Tracks target (guided missiles)")]
        public SkillProjectileTrajectory trajectory = SkillProjectileTrajectory.Linear;

        [Header("Impact Behavior")]
        [Tooltip("What happens when projectile reaches its designated primary target.\n" +
                 "• StopAndFadeOnHit: Stop on first valid hit and fade out.\n" +
                 "• StopWithoutFadeOnHit: Stop on first valid hit and despawn instantly.\n" +
                 "• PierceThrough: Continue trajectory past all hits.\n" +
                 "• StopOnDesignatedTargetWithFade: Ignore blockers and stop only on designated target, then fade out.\n" +
                 "• StopOnDesignatedTargetWithoutFade: Ignore blockers and stop only on designated target, then despawn instantly.")]
        public ProjectileTargetHitBehavior targetHitBehavior = ProjectileTargetHitBehavior.StopAndFadeOnHit;
        
        [Header("Target Selection")]
        [Tooltip("Can this projectile target enemies?")]
        public bool canTargetEnemies = true;
        
        [Tooltip("Can this projectile target allies (for healing/buff projectiles)?")]
        public bool canTargetAllies = false;
        
        [Tooltip("Can this projectile target dead units?")]
        public bool canTargetDead = false;
        
        [Header("Range")]
        [Tooltip("Minimum range to fire projectile.")]
        public float minRange = 0f;
        
        [Tooltip("Maximum range for the projectile.\n" +
                 "For Linear: Maximum travel distance.\n" +
                 "For Arc: Maximum throw distance.\n" +
                 "For Homing: Maximum lock-on range.")]
        public float maxRange = 30f;
        
        [Header("Arc Settings (Arc Trajectory Only)")]
        [Tooltip("How to calculate the arc height/trajectory.\n" +
                 "- LaunchAngle: Use a fixed launch angle (realistic ballistics)\n" +
                 "- FlightTime: Use a fixed flight time (guaranteed arrival)\n" +
                 "- FixedHeight: Use a fixed arc height regardless of distance")]
        public ArcHeightMode arcMode = ArcHeightMode.LaunchAngle;
        
        [Tooltip("Launch angle in degrees (for LaunchAngle mode).\n" +
                 "45 = optimal range, higher = more arc, lower = flatter")]
        [Range(15f, 75f)]
        public float launchAngle = 45f;
        
        [Tooltip("Fixed flight time in seconds (for FlightTime mode).\n" +
                 "Projectile will always take this long to reach target.")]
        [Range(0.3f, 5f)]
        public float flightTime = 1.5f;
        
        [Tooltip("Fixed arc height in units (for FixedHeight mode).\n" +
                 "Projectile will always reach this height above the starting point.")]
        [Range(1f, 20f)]
        public float arcHeight = 5f;
        
        [Tooltip("Gravity strength for arc calculation.\n" +
                 "Default matches Unity physics (9.81). Higher = faster fall.")]
        [Range(5f, 30f)]
        public float gravity = 9.81f;
        
        [Header("Linear Settings (Linear Trajectory Only)")]
        [Tooltip("Speed of linear projectiles (units per second).")]
        [Range(5f, 200f)]
        public float linearSpeed = 30f;
        
        [Header("Homing Settings (Homing Trajectory Only)")]
        [Tooltip("Turn rate for homing projectiles (degrees per second).")]
        [Range(30f, 720f)]
        public float homingTurnRate = 180f;
        
        [Tooltip("Initial speed of homing projectiles.")]
        [Range(5f, 100f)]
        public float homingSpeed = 20f;
        
        [Tooltip("Acceleration of homing projectiles.")]
        [Range(0f, 50f)]
        public float homingAcceleration = 10f;
        
        [Header("Trajectory Piercing")]
        [Tooltip("Allow projectile trajectories to pass through non-final targets and continue traveling.\n" +
                 "When enabled, On Trajectory Hit actions execute on each crossed target.\n" +
                 "Note: If On Trajectory Hit actions exist, pass-through is auto-enabled even if this is off.\n" +
                 "On Hit actions execute only at terminal impact.")]
        public bool enableTrajectoryPiercing = false;
        
        [Tooltip("Maximum number of pass-through targets before forcing impact.\n" +
                 "0 = unlimited (projectile stops only on final impact/range/environment).")]
        [Range(0, 50)]
        public int maxTrajectoryPierceTargets = 0;
        
        [Tooltip("Multiplier applied to On Trajectory Hit effect strength per pierced target.\n" +
                 "1 = no falloff.\n" +
                 "0.8 = each subsequent pierced target gets 80% of the previous.")]
        [Range(0.1f, 1f)]
        public float trajectoryPierceFalloff = 1f;
        
        /// <summary>
        /// Returns true if this config targets enemies (attack skill).
        /// </summary>
        public bool IsAttackProjectile => canTargetEnemies;
        
        /// <summary>
        /// Returns true if this config targets allies (support skill).
        /// </summary>
        public bool IsSupportProjectile => canTargetAllies && !canTargetEnemies;
    }
    
    /// <summary>
    /// Configuration for projectile visuals and audio (VFX/SFX tab).
    /// Supports both linear and arc trajectories with full networking support.
    /// Uses the same mathematical formulas as EnemyCrowd Charge Attack projectiles.
    /// </summary>
    [Serializable]
    public class SkillProjectileConfig
    {
        [Header("Enable")]
        [Tooltip("Enable projectile visuals for this skill.\n" +
                 "Note: For Projectile targeting type, damage/AOE/piercing\n" +
                 "are configured in the Targeting tab.")]
        public bool enabled = false;
        
        [Header("Trajectory Settings")]
        [Tooltip("How to calculate the arc height/trajectory (for Arc trajectory).")]
        public ArcHeightMode arcMode = ArcHeightMode.LaunchAngle;
        
        [Tooltip("Launch angle in degrees (for LaunchAngle mode).\n" +
                 "45° = optimal range, higher = more arc, lower = flatter")]
        [Range(15f, 75f)]
        public float launchAngle = 45f;
        
        [Tooltip("Fixed flight time in seconds (for FlightTime mode).\n" +
                 "Projectile will always take this long to reach target.")]
        [Range(0.3f, 5f)]
        public float flightTime = 1.5f;
        
        [Tooltip("Fixed arc height in units (for FixedHeight mode).\n" +
                 "Projectile will always reach this height above the starting point.")]
        [Range(1f, 20f)]
        public float arcHeight = 5f;
        
        [Tooltip("Gravity strength for arc calculation.\n" +
                 "Default matches Unity physics (9.81). Higher = faster fall.")]
        [Range(5f, 30f)]
        public float gravity = 9.81f;
        
        [Header("Linear Settings")]
        [Tooltip("Speed of linear projectiles (units per second).")]
        [Range(5f, 200f)]
        public float linearSpeed = 30f;

        [Header("Homing Settings")]
        [Tooltip("Turn rate for homing projectiles (degrees per second).")]
        [Range(30f, 720f)]
        public float homingTurnRate = 180f;
        
        [Tooltip("Initial speed of homing projectiles.")]
        [Range(5f, 100f)]
        public float homingSpeed = 20f;
        
        [Tooltip("Acceleration of homing projectiles.")]
        [Range(0f, 50f)]
        public float homingAcceleration = 10f;
        
        [Header("Visuals")]
        [Tooltip("Projectile visual (GameObject with ParticleSystem or mesh).")]
        public GameObject projectilePrefab;
        
        [Tooltip("Scale of the projectile visual.")]
        [Range(0.1f, 5f)]
        public float visualScale = 1f;
        
        [Tooltip("Trail effect attached to projectile.")]
        public GameObject trailPrefab;
        
        [Tooltip("Impact VFX spawned on hit.")]
        public GameObject impactVFX;
        
        [Tooltip("AOE explosion VFX (separate from impact).")]
        public GameObject aoeVFX;
        
        [Tooltip("Rotate projectile to face movement direction.")]
        public bool orientToVelocity = true;
        
        [Header("Audio")]
        [Tooltip("Sound when projectile is launched.")]
        public AudioClip launchSound;
        
        [Tooltip("Sound when projectile impacts.")]
        public AudioClip impactSound;
        
        [Tooltip("Volume of launch sound.")]
        [Range(0f, 1f)]
        public float launchVolume = 1f;
        
        [Tooltip("Volume of impact sound.")]
        [Range(0f, 1f)]
        public float impactVolume = 1f;
        
        [Header("Spawn Point")]
        [Tooltip("Offset from caster position to spawn projectile.")]
        public Vector3 spawnOffset = new Vector3(0f, 1.5f, 0.5f);
        
        [Tooltip("Delay after skill cast before projectile spawns (sync with animation).")]
        [Range(0f, 2f)]
        public float spawnDelay = 0.3f;
        
        [Tooltip("Bone name to spawn from (optional - for precise attachment).")]
        public string spawnBoneName = "";
        
        [Header("Multi-Projectile")]
        [Tooltip("Number of projectiles to spawn.")]
        [Range(1, 10)]
        public int projectileCount = 1;
        
        [Tooltip("Spread angle between multiple projectiles (degrees).")]
        [Range(0f, 60f)]
        public float spreadAngle = 15f;
        
        [Tooltip("Delay between each projectile in a volley.")]
        [Range(0f, 0.5f)]
        public float volleyDelay = 0f;
        
        // ═══════════════════════════════════════════════════════════════════
        // Legacy fields for backward compatibility with ProjectileArcEffect.
        // For new skills, use Projectile targeting type with DamageEffect.
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("Legacy Damage (for ProjectileArcEffect)")]
        [Tooltip("Base damage dealt by projectile impact.\n" +
                 "⚠️ For new skills, use Projectile targeting with DamageEffect instead.")]
        [Range(1f, 1000f)]
        public float damage = 25f;
        
        [Tooltip("Damage type for resistance calculations.\n" +
                 "⚠️ For new skills, use Projectile targeting with DamageEffect instead.")]
        public DamageType damageType = DamageType.Physical;
        
        [Header("Legacy AOE (for ProjectileArcEffect)")]
        [Tooltip("Enable area of effect damage on impact.\n" +
                 "⚠️ For new skills, configure AOE in Targeting tab.")]
        public bool enableAOE = false;
        
        [Tooltip("Radius of AOE damage (0 = single target only).\n" +
                 "⚠️ For new skills, configure AOE in Targeting tab.")]
        [Range(0f, 20f)]
        public float aoeRadius = 3f;
        
        [Tooltip("Damage multiplier at edge of AOE.\n" +
                 "⚠️ For new skills, configure AOE in Targeting tab.")]
        [Range(0f, 1f)]
        public float aoeFalloff = 0.3f;
        
        [Tooltip("Apply skill effects to AOE targets.\n" +
                 "⚠️ For new skills, Projectile targeting applies all Effects to hit targets.")]
        public bool applyEffectsToAOE = false;
        
        [Header("Legacy Piercing (for ProjectileArcEffect)")]
        [Tooltip("Allow linear projectiles to pierce through targets.\n" +
                 "⚠️ For new skills, configure piercing in Targeting tab.")]
        public bool enablePiercing = false;
        
        [Tooltip("Maximum number of targets to pierce.\n" +
                 "⚠️ For new skills, configure piercing in Targeting tab.")]
        [Range(0, 10)]
        public int maxPierceTargets = 3;
        
        [Tooltip("Damage falloff per pierce.\n" +
                 "⚠️ For new skills, configure piercing in Targeting tab.")]
        [Range(0.1f, 1f)]
        public float pierceFalloff = 0.8f;
        
        [Header("Legacy Trajectory (for ProjectileArcEffect)")]
        [Tooltip("Type of projectile trajectory.\n" +
                 "⚠️ For new skills, set trajectory in Targeting tab.")]
        public SkillProjectileTrajectory trajectory = SkillProjectileTrajectory.Linear;
        
        /// <summary>
        /// Returns true if the configuration is valid.
        /// </summary>
        public bool IsValid => enabled;
        
        /// <summary>
        /// Returns the NetworkProjectileType for networking based on trajectory.
        /// </summary>
        public static NetworkProjectileType GetNetworkType(SkillProjectileTrajectory trajectory)
        {
            return trajectory switch
            {
                SkillProjectileTrajectory.Linear => NetworkProjectileType.Linear,
                SkillProjectileTrajectory.Arc => NetworkProjectileType.Parabolic,
                SkillProjectileTrajectory.Homing => NetworkProjectileType.Homing,
                _ => NetworkProjectileType.Linear
            };
        }
        
        /// <summary>
        /// Gets the VFXReference for the projectile visual.
        /// </summary>
        public VFXReference GetProjectileVisual()
        {
            if (projectilePrefab == null) return null;
            return new VFXReference
            {
                type = VFXType.ParticleSystem,
                particleSystemPrefab = projectilePrefab,
                defaultDuration = 5f
            };
        }
        
        /// <summary>
        /// Gets the VFXReference for the impact effect.
        /// </summary>
        public VFXReference GetImpactVFX()
        {
            if (impactVFX == null) return null;
            return new VFXReference
            {
                type = VFXType.ParticleSystem,
                particleSystemPrefab = impactVFX,
                defaultDuration = 1f
            };
        }
        
        /// <summary>
        /// Gets the VFXReference for the trail effect.
        /// </summary>
        public VFXReference GetTrailVFX()
        {
            if (trailPrefab == null) return null;
            return new VFXReference
            {
                type = VFXType.ParticleSystem,
                particleSystemPrefab = trailPrefab,
                defaultDuration = 3f
            };
        }
        
        /// <summary>
        /// Gets the VFXReference for the AOE effect.
        /// </summary>
        public VFXReference GetAoeVFX()
        {
            if (aoeVFX == null) return null;
            return new VFXReference
            {
                type = VFXType.ParticleSystem,
                particleSystemPrefab = aoeVFX,
                defaultDuration = 2f
            };
        }
    }
    
    /// <summary>
    /// Skill effect that spawns a projectile with arc trajectory.
    /// Uses the same mathematical formulas as EnemyCrowd ProjectileArc attack mode.
    /// Fully integrated with EnemyMassesProjectileSystem for efficient batch processing.
    /// Supports server-authoritative networking.
    /// </summary>
    [Serializable]
    public class ProjectileArcEffect : SkillEffect
    {
        [Header("Projectile Settings")]
        [Tooltip("Configuration for the projectile.")]
        [SerializeField] private SkillProjectileConfig config = new SkillProjectileConfig();

        [Tooltip("If true, this effect can critically strike.")]
        [SerializeField] private bool canCrit = true;
        
        public SkillProjectileConfig Config => config;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return;

            if (config == null || !config.enabled)
                return;
            
            if (context.Caster == null || context.Controller == null)
                return;
            
            // Get caster position and spawn point
            Vector3 casterPos = GetCasterPosition(context.Caster);
            Vector3 spawnPos = GetSpawnPosition(context.Caster, casterPos);
            
            // Get target position
            Vector3 targetPos = GetTargetPosition(context);
            
            // Calculate damage with multipliers
            float baseDamage = config.damage * context.EffectMultiplier;
            if (context.IsCritical && canCrit)
            {
                baseDamage *= context.Skill?.CriticalMultiplier ?? 2f;
            }
            
            // Spawn projectile(s)
            for (int i = 0; i < config.projectileCount; i++)
            {
                Vector3 adjustedTarget = targetPos;
                
                // Apply spread for multiple projectiles
                if (config.projectileCount > 1 && config.spreadAngle > 0)
                {
                    float spreadOffset = (i - (config.projectileCount - 1) / 2f) * config.spreadAngle;
                    Vector3 toTarget = (targetPos - spawnPos).normalized;
                    adjustedTarget = targetPos + Quaternion.AngleAxis(spreadOffset, Vector3.up) * toTarget * 2f;
                }
                
                // Calculate spawn delay for this projectile in the volley
                float totalDelay = config.spawnDelay + (i * config.volleyDelay);
                
                if (totalDelay > 0)
                {
                    // Use coroutine for delayed spawn
                    context.Controller.StartCoroutine(SpawnProjectileDelayed(context, spawnPos, adjustedTarget, baseDamage, totalDelay));
                }
                else
                {
                    // Immediate spawn
                    SpawnProjectile(context, spawnPos, adjustedTarget, baseDamage);
                }
            }
        }
        
        private System.Collections.IEnumerator SpawnProjectileDelayed(SkillExecutionContext context, Vector3 spawnPos, Vector3 targetPos, float damage, float delay)
        {
            yield return new WaitForSeconds(delay);
            SpawnProjectile(context, spawnPos, targetPos, damage);
        }
        
        private void SpawnProjectile(SkillExecutionContext context, Vector3 spawnPos, Vector3 targetPos, float damage)
        {
            var projectileSystem = EnemyMassesProjectileSystem.Instance;
            if (projectileSystem == null)
            {
                Debug.LogWarning("[ProjectileArcEffect] No ProjectileSystem instance available.");
                return;
            }
            
            // Get target agent index if targeting an agent
            int targetAgentIndex = context.PrimaryTarget?.index ?? -1;
            
            // Register effects config with projectile system
            int effectsConfigId = RegisterEffectsConfig(projectileSystem);
            
            int projectileIndex = -1;
            float aoeRadius = config.enableAOE ? config.aoeRadius : 0f;
            
            switch (config.trajectory)
            {
                case SkillProjectileTrajectory.Arc:
                    projectileIndex = SpawnArcProjectile(
                        projectileSystem, spawnPos, targetPos, damage, aoeRadius,
                        context.Caster.index, targetAgentIndex, context.Caster.factionIndex,
                        effectsConfigId
                    );
                    break;
                    
                case SkillProjectileTrajectory.Linear:
                    projectileIndex = projectileSystem.FireLinearProjectile(
                        spawnPos, targetPos, config.linearSpeed, damage,
                        context.Caster.index, targetAgentIndex, context.Caster.factionIndex,
                        5f, true, config.projectilePrefab, config.visualScale,
                        effectsConfigId, aoeRadius
                    );
                    break;
                    
                case SkillProjectileTrajectory.Homing:
                    projectileIndex = projectileSystem.FireHomingProjectile(
                        spawnPos, targetPos, config.homingSpeed, damage,
                        context.Caster.index, targetAgentIndex, context.Caster.factionIndex,
                        config.homingTurnRate,
                        config.homingAcceleration,
                        10f, true, config.projectilePrefab, config.visualScale,
                        effectsConfigId, aoeRadius
                    );
                    break;
            }
            
            // Play launch sound
            if (config.launchSound != null)
            {
                AudioSource.PlayClipAtPoint(config.launchSound, spawnPos, config.launchVolume);
            }
        }
        
        private int SpawnArcProjectile(
            EnemyMassesProjectileSystem system,
            Vector3 spawnPos, Vector3 targetPos,
            float damage, float aoeRadius,
            int sourceAgentIndex, int targetAgentIndex, int sourceFactionIndex,
            int effectsConfigId)
        {
            // Determine flight time based on arc mode
            float? flightTime = null;
            float launchAngle = config.launchAngle;
            
            switch (config.arcMode)
            {
                case ArcHeightMode.FlightTime:
                    flightTime = config.flightTime;
                    break;
                    
                case ArcHeightMode.FixedHeight:
                    // t = 2 * sqrt(2h/g) for symmetric arc
                    float v0y = Mathf.Sqrt(2f * config.gravity * config.arcHeight);
                    flightTime = 2f * v0y / config.gravity;
                    break;
                    
                case ArcHeightMode.LaunchAngle:
                default:
                    launchAngle = config.launchAngle;
                    break;
            }
            
            return system.FireParabolicProjectile(
                spawnPos, targetPos, damage, aoeRadius,
                sourceAgentIndex, targetAgentIndex, sourceFactionIndex,
                flightTime, launchAngle,
                true, config.projectilePrefab, config.visualScale,
                effectsConfigId,
                0, // networkProjectileId
                true, // fadeProjectileOnHit
                config.gravity
            );
        }
        
        private int RegisterEffectsConfig(EnemyMassesProjectileSystem system)
        {
            var projectileVisual = config.GetProjectileVisual();
            var impactVFX = config.GetImpactVFX();
            var trailVFX = config.GetTrailVFX();
            var aoeVFX = config.enableAOE ? config.GetAoeVFX() : null;
            
            return system.RegisterEffectsConfig(
                projectileVisual,
                impactVFX,
                trailVFX,
                config.launchSound,
                config.impactSound,
                config.launchVolume,
                config.impactVolume,
                aoeVFX,
                0.05f, 0.05f, 1f, 1f,
                config.visualScale
            );
        }
        
        private Vector3 GetCasterPosition(CrowdAgent caster)
        {
            return caster?.WorldPosition ?? Vector3.zero;
        }
        
        private Vector3 GetSpawnPosition(CrowdAgent caster, Vector3 casterPos)
        {
            Transform casterTransform = caster?.navigationAgent?.Transform ?? caster?.crowdInstance?.transform ?? caster?.transform;
            if (casterTransform == null)
                return casterPos + config.spawnOffset;
            
            if (!string.IsNullOrEmpty(config.spawnBoneName))
            {
                Transform bone = FindBone(casterTransform, config.spawnBoneName);
                if (bone != null)
                    return bone.position;
            }
            
            return casterTransform.TransformPoint(config.spawnOffset);
        }
        
        private Transform FindBone(Transform root, string boneName)
        {
            if (root.name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
                return root;
            
            foreach (Transform child in root)
            {
                var result = FindBone(child, boneName);
                if (result != null)
                    return result;
            }
            return null;
        }
        
        private Vector3 GetTargetPosition(SkillExecutionContext context)
        {
            if (context.PrimaryTarget != null)
            {
                return context.PrimaryTarget.WorldPosition;
            }
            return context.TargetPosition;
        }
        
        public override string GetDescription()
        {
            string trajectoryName = config.trajectory switch
            {
                SkillProjectileTrajectory.Linear => "linear",
                SkillProjectileTrajectory.Arc => "arc",
                SkillProjectileTrajectory.Homing => "homing",
                _ => "projectile"
            };
            
            string aoeDesc = config.enableAOE && config.aoeRadius > 0 
                ? $" with {config.aoeRadius}m AOE" 
                : "";
            
            return $"Fires {config.projectileCount} {trajectoryName} projectile(s) dealing {config.damage} {config.damageType} damage{aoeDesc}";
        }
    }
}
