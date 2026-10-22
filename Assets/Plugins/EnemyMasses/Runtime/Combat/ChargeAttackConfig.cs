using UnityEngine;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration for melee charge attacks - a special ability that allows melee units
    /// to close the gap against ranged enemies with a powerful AOE impact.
    /// </summary>
    [System.Serializable]
    public class ChargeAttackConfig
    {
        [Header("Charge Trigger Settings")]
        [Tooltip("Enable charge attack ability for this faction.\n" +
                 "Requires 'Can Use Special Abilities' to be enabled.")]
        public bool enableChargeAttack = false;
        
        [Tooltip("Minimum distance to target required to trigger a charge.\n" +
                 "Below this distance, normal melee attacks are used instead.\n" +
                 "Recommended: 6-10m")]
        [Range(3f, 20f)]
        public float chargeMinDistance = 8f;
        
        [Tooltip("Maximum distance to target for charge to trigger.\n" +
                 "Beyond this distance, agents will close gap normally first.\n" +
                 "Recommended: 15-30m")]
        [Range(10f, 50f)]
        public float chargeMaxDistance = 25f;
        
        [Tooltip("Chance to use charge attack when conditions are met (0-1).\n" +
                 "1.0 = always charge when possible\n" +
                 "0.5 = 50% chance to charge")]
        [Range(0f, 1f)]
        public float chargeChance = 0.7f;
        
        [Header("Charge Movement")]
        [Tooltip("Speed multiplier during charge.\n" +
                 "Higher values = faster charge, harder to react to.\n" +
                 "Recommended: 3-5x")]
        [Range(2f, 8f)]
        public float chargeSpeedMultiplier = 4f;
        
        [Tooltip("Maximum duration of charge in seconds.\n" +
                 "Charge ends early if target is reached.\n" +
                 "Prevents infinite charges if path is blocked.")]
        [Range(1f, 10f)]
        public float chargeMaxDuration = 3f;
        
        [Header("Charge Damage")]
        [Tooltip("Base damage dealt on charge impact.\n" +
                 "This is in addition to normal attack damage.")]
        [Range(0f, 500f)]
        public float chargeDamage = 50f;
        
        [Tooltip("Damage multiplier based on distance traveled during charge.\n" +
                 "Longer charges deal more damage.\n" +
                 "Final damage = chargeDamage * (1 + distanceTraveled * distanceDamageMultiplier)")]
        [Range(0f, 0.5f)]
        public float distanceDamageMultiplier = 0.05f;
        
        [Header("AOE Impact")]
        [Tooltip("Enable area of effect damage on charge impact.\n" +
                 "Damages all enemies within radius at impact point.")]
        public bool enableAOE = true;
        
        [Tooltip("Radius of AOE damage on impact.\n" +
                 "0 = single target only")]
        [Range(0f, 15f)]
        public float aoeRadius = 3f;
        
        [Tooltip("Damage falloff at AOE edge (multiplier).\n" +
                 "0.3 = 30% damage at edge, 100% at center")]
        [Range(0f, 1f)]
        public float aoeFalloffMinDamage = 0.3f;
        
        [Header("Knockback (Fake Physics)")]
        [Tooltip("Horizontal knockback force applied to targets hit by charge (m/s).\n" +
                 "0 = no knockback. Uses the navmesh offset impulse system.")]
        [Range(0f, 20f)]
        public float knockbackForce = 5f;
        
        [Tooltip("Vertical lift force applied during knockback (m/s).\n" +
                 "Adds an upward arc to the knockback. Values >= 1.5 trigger 'knocked up' animations.")]
        [Range(0f, 10f)]
        public float knockbackLiftForce = 2f;
        
        [Tooltip("Duration of the knockback impulse (seconds).\n" +
                 "Longer durations result in further travel distance.")]
        [Range(0.2f, 2f)]
        public float knockbackDuration = 0.5f;
        
        [Tooltip("Gravity applied during knockback (units/s²).\n" +
                 "Higher values snap agents back to ground faster.")]
        [Range(5f, 50f)]
        public float knockbackGravity = 25f;
        
        [Tooltip("Horizontal drag applied during knockback.\n" +
                 "Higher values bleed off horizontal momentum faster.")]
        [Range(0f, 10f)]
        public float knockbackDrag = 4f;
        
        [Header("Piercing (Trample)")]
        [Tooltip("Enable piercing damage during charge.\n" +
                 "When enabled, the charging unit damages all enemies in its path,\n" +
                 "not just the target. Great for cavalry trampling through infantry.")]
        public bool enablePiercing = false;
        
        [Tooltip("Width of the pierce/trample hitbox during charge.\n" +
                 "Enemies within this distance of the charge path take damage.")]
        [Range(0.5f, 5f)]
        public float pierceWidth = 1.5f;
        
        [Tooltip("Maximum number of enemies that can be hit during a single charge.\n" +
                 "0 = unlimited (hit everyone in path)")]
        [Min(0)]
        public int maxPierceTargets = 0;
        
        [Tooltip("Damage multiplier for pierced targets (not the main target).\n" +
                 "1.0 = full damage, 0.5 = half damage")]
        [Range(0.1f, 1.5f)]
        public float pierceDamageMultiplier = 0.7f;
        
        [Tooltip("Minimum distance between pierce damage ticks.\n" +
                 "Prevents the same enemy from being hit multiple times in quick succession.")]
        [Range(0.5f, 3f)]
        public float pierceTickDistance = 1f;
        
        [Header("Cooldown")]
        [Tooltip("Cooldown between charge attacks (seconds).\n" +
                 "Prevents charge spam.")]
        [Range(5f, 60f)]
        public float chargeCooldown = 12f;
        
        [Tooltip("Random variance added to cooldown (±seconds).\n" +
                 "Prevents synchronized charges from multiple agents.")]
        [Range(0f, 10f)]
        public float cooldownVariance = 3f;
        
        [Header("Animation")]
        [Tooltip("Animation clip to play during charge.\n" +
                 "Should be a running/charging animation.\n" +
                 "If null, uses normal run animation.")]
        public AnimationClip chargeAnimationClip;
        
        [Tooltip("Animation clip to play on charge impact.\n" +
                 "Should be a slam/impact animation.\n" +
                 "If null, uses normal attack animation.")]
        public AnimationClip chargeImpactAnimationClip;
        
        [Tooltip("Duration of impact animation (seconds).\n" +
                 "Agent is locked in place during this time.")]
        [Range(0.3f, 2f)]
        public float impactAnimationDuration = 0.8f;
        
        [Header("Visual Effects")]
        [Tooltip("VFX to spawn during charge (dust trail, energy aura, etc.).\n" +
                 "Follows the charging agent.")]
        public VFXReference chargeTrailVFX = new VFXReference { type = VFXType.None, defaultDuration = 3f };
        
        [Tooltip("VFX to spawn at charge impact point (shockwave, explosion, etc.).")]
        public VFXReference chargeImpactVFX = new VFXReference { type = VFXType.None, defaultDuration = 2f };
        
        [Tooltip("Scale of impact VFX based on AOE radius.\n" +
                 "1.0 = VFX scale matches AOE radius")]
        [Range(0.5f, 3f)]
        public float impactVFXScale = 1f;
        
        [Header("Audio")]
        [Tooltip("Sound to play when charge begins (war cry, roar, etc.).")]
        public AudioClip chargeStartSound;
        
        [Tooltip("Volume of charge start sound.")]
        [Range(0f, 1f)]
        public float chargeStartVolume = 1f;
        
        [Tooltip("Looping sound during charge (footsteps, rumble, etc.).\n" +
                 "Stops when charge ends.")]
        public AudioClip chargeLoopSound;
        
        [Tooltip("Volume of charge loop sound.")]
        [Range(0f, 1f)]
        public float chargeLoopVolume = 0.7f;
        
        [Tooltip("Sound to play on charge impact (slam, explosion, etc.).")]
        public AudioClip chargeImpactSound;
        
        [Tooltip("Volume of impact sound.")]
        [Range(0f, 1f)]
        public float chargeImpactVolume = 1f;
        
        [Tooltip("Random pitch variation for sounds (±).\n" +
                 "0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.3f)]
        public float soundPitchVariation = 0.1f;
        
        [Tooltip("Spatial blend for charge sounds (0 = 2D, 1 = 3D).")]
        [Range(0f, 1f)]
        public float soundSpatialBlend = 1f;
        
        [Header("Status Effects on Impact")]
        [Tooltip("Status effects applied to enemies hit by the charge.\n" +
                 "Examples: Stun, Slow, Knockdown")]
        public AttackEffectsConfig impactEffects = new AttackEffectsConfig();
        
        /// <summary>
        /// Gets the actual cooldown with variance applied.
        /// </summary>
        public float GetRandomizedCooldown()
        {
            return chargeCooldown + Random.Range(-cooldownVariance, cooldownVariance);
        }
        
        /// <summary>
        /// Calculates final damage based on distance traveled.
        /// </summary>
        public float CalculateDamage(float distanceTraveled)
        {
            return chargeDamage * (1f + distanceTraveled * distanceDamageMultiplier);
        }
        
        /// <summary>
        /// Calculates AOE damage with falloff based on distance from impact center.
        /// </summary>
        public float CalculateAOEDamage(float baseDamage, float distanceFromCenter)
        {
            if (!enableAOE || aoeRadius <= 0f) return 0f;
            if (distanceFromCenter > aoeRadius) return 0f;
            
            float falloff = 1f - (distanceFromCenter / aoeRadius) * (1f - aoeFalloffMinDamage);
            return baseDamage * falloff;
        }
        
        /// <summary>
        /// Creates a default berserker-style charge config.
        /// </summary>
        public static ChargeAttackConfig CreateBerserkerConfig()
        {
            return new ChargeAttackConfig
            {
                enableChargeAttack = true,
                chargeMinDistance = 8f,
                chargeMaxDistance = 20f,
                chargeChance = 0.8f,
                chargeSpeedMultiplier = 4.5f,
                chargeDamage = 60f,
                enableAOE = true,
                aoeRadius = 3f,
                aoeFalloffMinDamage = 0.4f,
                knockbackForce = 6f,
                knockbackLiftForce = 2.5f,
                knockbackDuration = 0.5f,
                knockbackGravity = 25f,
                knockbackDrag = 4f,
                chargeCooldown = 10f
            };
        }
        
        /// <summary>
        /// Creates a default cavalry-style charge config (longer range, more damage).
        /// </summary>
        public static ChargeAttackConfig CreateCavalryConfig()
        {
            return new ChargeAttackConfig
            {
                enableChargeAttack = true,
                chargeMinDistance = 12f,
                chargeMaxDistance = 35f,
                chargeChance = 0.9f,
                chargeSpeedMultiplier = 5f,
                chargeDamage = 80f,
                distanceDamageMultiplier = 0.08f,
                enableAOE = true,
                aoeRadius = 4f,
                aoeFalloffMinDamage = 0.3f,
                knockbackForce = 10f,
                knockbackLiftForce = 4f,
                knockbackDuration = 0.6f,
                knockbackGravity = 25f,
                knockbackDrag = 3.5f,
                chargeCooldown = 15f,
                enablePiercing = true,
                pierceWidth = 2f,
                pierceDamageMultiplier = 0.8f
            };
        }
    }
    
    /// <summary>
    /// Configuration for melee cleave attacks - allows melee attacks to hit multiple enemies
    /// in a swing arc, simulating wide sword swings, hammer sweeps, or claw attacks.
    /// </summary>
    [System.Serializable]
    public class MeleeCleaveConfig
    {
        [Tooltip("Enable cleave for melee attacks.\\n" +
                 "When enabled, melee attacks can hit multiple enemies in a swing arc,\\n" +
                 "not just the primary target. Great for warriors with large weapons.")]
        public bool enableCleave = false;
        
        [Tooltip("Arc angle of the cleave attack (degrees).\\n" +
                 "90 = quarter circle in front\\n" +
                 "180 = half circle (wide sweep)\\n" +
                 "360 = full circle (spin attack)")]
        [Range(30f, 360f)]
        public float cleaveArc = 90f;
        
        [Tooltip("Range of the cleave attack.\\n" +
                 "Enemies within this distance AND within the arc angle are hit.\\n" +
                 "Typically matches or slightly exceeds melee attack range.")]
        [Range(1f, 10f)]
        public float cleaveRange = 2f;
        
        [Tooltip("Maximum number of additional enemies hit by cleave.\\n" +
                 "0 = unlimited (hit all in arc)\\n" +
                 "This is in ADDITION to the primary target.")]
        [Min(0)]
        public int maxCleaveTargets = 3;
        
        [Tooltip("Damage multiplier for cleaved targets (not the primary target).\\n" +
                 "1.0 = full damage, 0.5 = half damage")]
        [Range(0.1f, 1.5f)]
        public float cleaveDamageMultiplier = 0.6f;
        
        [Tooltip("Apply status effects from melee attacks to cleaved targets.\\n" +
                 "If disabled, only the primary target receives status effects.")]
        public bool applyStatusEffectsToCleave = false;
        
        [Tooltip("VFX to spawn for cleave effect (swing trail, slash effect).\\n" +
                 "Spawns at attacker position facing target direction.")]
        public VFXReference cleaveVFX = new VFXReference 
        { 
            type = VFXType.None, 
            defaultDuration = 0.5f 
        };
        
        [Tooltip("Sound to play for cleave hit (whoosh, slash).\\n" +
                 "Plays once per attack, not per target hit.")]
        public AudioClip cleaveSound;
        
        [Tooltip("Volume of cleave sound.")]
        [Range(0f, 1f)]
        public float cleaveSoundVolume = 0.8f;
    }
}
