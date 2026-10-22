using UnityEngine;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Defines a status effect that can be applied by an attack.
    /// Used to add effects like stun, poison, slow to faction attacks.
    /// </summary>
    [System.Serializable]
    public class AttackStatusEffect
    {
        [Tooltip("Enable this status effect on attack")]
        public bool enabled = false;
        
        [Tooltip("Type of status effect to apply")]
        public StatusEffectType effectType = StatusEffectType.Stun;
        
        [Tooltip("Chance to apply this effect (0-1)\n" +
                 "1.0 = always applies\n" +
                 "0.5 = 50% chance")]
        [Range(0f, 1f)]
        public float applicationChance = 0.5f;
        
        [Tooltip("Duration of the effect in seconds\n" +
                 "0 = instant (for effects like damage) or permanent until dispelled")]
        [Min(0f)]
        public float duration = 3f;
        
        [Tooltip("Value of the effect:\n" +
                 "- For DOT: Damage per tick\n" +
                 "- For buffs/debuffs: Multiplier (1.5 = +50%)\n" +
                 "- For shield: Shield amount")]
        public float value = 1f;
        
        [Tooltip("For DOT/HOT effects: Time between ticks in seconds")]
        [Min(0.1f)]
        public float tickInterval = 1f;
        
        [Header("Target Selection")]
        [Tooltip("Who can be affected by this effect:\n" +
                 "- Enemy: Only enemies can be affected\n" +
                 "- Ally: Only allies can be affected (for buffs/heals)\n" +
                 "- Self: Only the attacker is affected\n" +
                 "- All: Anyone can be affected")]
        public EffectTargetType targetType = EffectTargetType.Enemy;
        
        [Header("Visual Feedback")]
        [Tooltip("VFX to spawn when effect is applied")]
        public VFXReference applyVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Header("Audio")]
        [Tooltip("Sound to play when effect is applied")]
        public AudioClip applySound;
        
        [Tooltip("Volume of apply sound")]
        [Range(0f, 1f)]
        public float applySoundVolume = 1f;
        
        [Tooltip("Random pitch variation range (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float applySoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float applySoundSpatialBlend = 1f;
        
        /// <summary>
        /// Roll to see if this effect should apply based on chance.
        /// </summary>
        public bool ShouldApply()
        {
            if (!enabled) return false;
            return Random.value <= applicationChance;
        }
        
        /// <summary>
        /// Create a StatusEffectConfig from this attack effect.
        /// </summary>
        public StatusEffectConfig ToStatusEffectConfig()
        {
            return new StatusEffectConfig
            {
                effectType = effectType,
                duration = duration,
                value = value,
                tickInterval = tickInterval,
                canStack = false,
                maxStacks = 1,
                stackBehavior = StackBehavior.Refresh,
                applyVFX = applyVFX,
                applySound = applySound
            };
        }
    }
    
    /// <summary>
    /// Who can be affected by an attack's status effect.
    /// </summary>
    public enum EffectTargetType
    {
        /// <summary>Only enemies can be affected (damage, debuffs, CC)</summary>
        Enemy,
        
        /// <summary>Only allies can be affected (buffs, heals)</summary>
        Ally,
        
        /// <summary>Only the attacker is affected (self-buffs)</summary>
        Self,
        
        /// <summary>Anyone can be affected</summary>
        All
    }
    
    /// <summary>
    /// Collection of status effects for an attack type.
    /// Supports multiple effects per attack (e.g., slow + damage over time).
    /// </summary>
    [System.Serializable]
    public class AttackEffectsConfig
    {
        [Tooltip("Primary effect applied on hit (e.g., stun, poison)")]
        public AttackStatusEffect primaryEffect = new AttackStatusEffect();
        
        [Tooltip("Secondary effect applied on hit (e.g., slow after stun)")]
        public AttackStatusEffect secondaryEffect = new AttackStatusEffect();
        
        [Tooltip("Bonus effect applied on critical hits only")]
        public AttackStatusEffect criticalHitEffect = new AttackStatusEffect();
        
        /// <summary>
        /// Check if any effects are enabled.
        /// </summary>
        public bool HasAnyEffects => primaryEffect.enabled || secondaryEffect.enabled || criticalHitEffect.enabled;
        
        /// <summary>
        /// Get all enabled effects that should apply (based on chance rolls).
        /// </summary>
        public System.Collections.Generic.List<AttackStatusEffect> GetEffectsToApply(bool isCriticalHit)
        {
            var effects = new System.Collections.Generic.List<AttackStatusEffect>();
            
            if (primaryEffect.ShouldApply())
                effects.Add(primaryEffect);
                
            if (secondaryEffect.ShouldApply())
                effects.Add(secondaryEffect);
                
            if (isCriticalHit && criticalHitEffect.ShouldApply())
                effects.Add(criticalHitEffect);
                
            return effects;
        }
    }
    
    /// <summary>
    /// Configuration for AOE healing/buff abilities.
    /// Used for support units that can heal or buff allies.
    /// </summary>
    [System.Serializable]
    public class SupportAbilityConfig
    {
        [Header("Enable")]
        [Tooltip("Enable this support ability for the faction")]
        public bool enabled = false;
        
        [Header("Targeting")]
        [Tooltip("Radius to search for allies to affect")]
        [Range(1f, 30f)]
        public float radius = 8f;
        
        [Tooltip("Maximum number of allies to affect per use\n" +
                 "0 = unlimited")]
        [Min(0)]
        public int maxTargets = 5;
        
        [Tooltip("Include self in the effect")]
        public bool includeSelf = true;
        
        [Header("Effect")]
        [Tooltip("The effect to apply to allies")]
        public AttackStatusEffect effect = new AttackStatusEffect
        {
            enabled = true,
            effectType = StatusEffectType.Regeneration,
            applicationChance = 1f,
            duration = 5f,
            value = 10f,
            tickInterval = 1f,
            targetType = EffectTargetType.Ally
        };
        
        [Header("Cooldown")]
        [Tooltip("Cooldown between uses in seconds")]
        [Range(5f, 120f)]
        public float cooldown = 15f;
        
        [Tooltip("Random variance added to cooldown")]
        [Range(0f, 10f)]
        public float cooldownVariance = 3f;
        
        [Header("Trigger Conditions")]
        [Tooltip("Minimum ally health percentage to trigger healing (0-1)\n" +
                 "0.5 = triggers when any ally is below 50% health")]
        [Range(0f, 1f)]
        public float triggerHealthThreshold = 0.6f;
        
        [Tooltip("Minimum number of valid targets required to use ability")]
        [Min(1)]
        public int minTargetsToTrigger = 1;
        
        [Header("Animation")]
        [Tooltip("Animation to play when using ability")]
        public AnimationClip abilityAnimation;
        
        [Tooltip("Duration of animation lock")]
        [Range(0.5f, 3f)]
        public float animationDuration = 1.5f;
        
        [Header("Visual Effects")]
        [Tooltip("VFX to spawn on the caster")]
        public VFXReference casterVFX = new VFXReference { type = VFXType.None, defaultDuration = 1.5f };
        
        [Tooltip("VFX to spawn on each affected ally")]
        public VFXReference targetVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Header("Audio")]
        [Tooltip("Sound to play when ability activates")]
        public AudioClip activationSound;
        
        [Tooltip("Volume of activation sound")]
        [Range(0f, 1f)]
        public float soundVolume = 1f;
        
        [Tooltip("Random pitch variation range (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float soundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for activation sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float soundSpatialBlend = 1f;
        
        /// <summary>
        /// Get randomized cooldown.
        /// </summary>
        public float GetRandomizedCooldown()
        {
            return cooldown + Random.Range(-cooldownVariance, cooldownVariance);
        }
    }
}
