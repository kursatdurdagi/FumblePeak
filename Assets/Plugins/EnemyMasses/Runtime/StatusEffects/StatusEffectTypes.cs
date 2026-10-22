using UnityEngine;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Types of status effects that can be applied to agents.
    /// Organized by category for easy identification.
    /// </summary>
    public enum StatusEffectType
    {
        // ═══════════════════════════════════════════════════════════════════
        // CROWD CONTROL (CC) - Disable/impair agent actions
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Agent cannot move or attack. Broken by damage.</summary>
        Stun = 0,
        
        /// <summary>Agent cannot move or attack. Broken by damage. Visual: frozen in place.</summary>
        Freeze = 1,
        
        /// <summary>Agent sleeps - cannot act. Broken by ANY damage (even DOT).</summary>
        Sleep = 2,
        
        /// <summary>Agent is dazed - reduced movement, can't attack. Often from electric damage.</summary>
        Daze = 3,
        
        /// <summary>Agent staggers - brief interrupt, resets attack timing.</summary>
        Stagger = 4,
        
        /// <summary>Agent is feared - runs away from source.</summary>
        Fear = 5,
        
        /// <summary>Agent is taunted - forced to attack taunt source.</summary>
        Taunt = 6,
        
        /// <summary>Agent moves slower.</summary>
        Slow = 7,
        
        /// <summary>Agent is rooted - can attack but cannot move.</summary>
        Root = 8,
        
        /// <summary>Agent is silenced - cannot use special abilities (charge, etc).</summary>
        Silence = 9,
        
        // ═══════════════════════════════════════════════════════════════════
        // DAMAGE OVER TIME (DOT) - Periodic damage
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Poison damage over time. Typically nature/acid.</summary>
        Poison = 20,
        
        /// <summary>Burn damage over time. Fire damage.</summary>
        Burn = 21,
        
        /// <summary>Bleed damage over time. Physical damage.</summary>
        Bleed = 22,
        
        /// <summary>Curse damage over time. Dark/shadow damage.</summary>
        Curse = 23,
        
        // ═══════════════════════════════════════════════════════════════════
        // DEBUFFS - Stat reductions
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Reduces armor/damage resistance.</summary>
        ArmorBreak = 40,
        
        /// <summary>Reduces attack damage dealt.</summary>
        Weaken = 41,
        
        /// <summary>Increases damage taken (vulnerability).</summary>
        Vulnerable = 42,
        
        /// <summary>Reduces attack speed (increases cooldown).</summary>
        AttackSpeedSlow = 43,
        
        /// <summary>Reduces block chance.</summary>
        BlockBreak = 44,
        
        /// <summary>Increases critical hit chance received (already implemented in hitbox system).</summary>
        ExposedWeakness = 45,
        
        // ═══════════════════════════════════════════════════════════════════
        // BUFFS - Positive stat modifiers
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Increases armor/damage resistance.</summary>
        ArmorBuff = 60,
        
        /// <summary>Increases maximum and current health.</summary>
        HealthBuff = 61,
        
        /// <summary>Regenerates health over time (HOT).</summary>
        Regeneration = 62,
        
        /// <summary>Increases attack speed (reduces cooldown).</summary>
        AttackSpeedBuff = 63,
        
        /// <summary>Increases attack damage dealt.</summary>
        DamageBuff = 64,
        
        /// <summary>Increases block chance.</summary>
        BlockBuff = 65,
        
        /// <summary>Increases movement speed.</summary>
        SpeedBuff = 66,
        
        /// <summary>Increases critical hit chance dealt.</summary>
        CriticalBuff = 67,
        
        /// <summary>Immune to damage for duration.</summary>
        Invulnerable = 68,
        
        /// <summary>Immune to crowd control effects.</summary>
        CCImmune = 69,
        
        /// <summary>Shield that absorbs damage before health.</summary>
        Shield = 70,
        
        /// <summary>Immune to displacement effects (knockback, push, pull) from the Fake Physics system.</summary>
        DisplacementImmune = 71,
        
        // ═══════════════════════════════════════════════════════════════════
        // RESISTANCE BUFFS - Elemental damage reduction
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Increases magic resistance (reduces magical damage).</summary>
        MagicResistBuff = 80,
        
        /// <summary>Increases fire resistance (reduces fire damage).</summary>
        FireResistBuff = 81,
        
        /// <summary>Increases ice resistance (reduces ice damage).</summary>
        IceResistBuff = 82,
        
        /// <summary>Increases lightning resistance (reduces lightning damage).</summary>
        LightningResistBuff = 83,
        
        /// <summary>Increases poison resistance (reduces poison damage).</summary>
        PoisonResistBuff = 84,
        
        // ═══════════════════════════════════════════════════════════════════
        // RESISTANCE DEBUFFS - Elemental vulnerability
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Reduces magic resistance (increases magical damage taken).</summary>
        MagicResistBreak = 90,
        
        /// <summary>Reduces fire resistance (increases fire damage taken).</summary>
        FireVulnerability = 91,
        
        /// <summary>Reduces ice resistance (increases ice damage taken).</summary>
        IceVulnerability = 92,
        
        /// <summary>Reduces lightning resistance (increases lightning damage taken).</summary>
        LightningVulnerability = 93,
        
        /// <summary>Reduces poison resistance (increases poison damage taken).</summary>
        PoisonVulnerability = 94,
    }
    
    /// <summary>
    /// Categories of effects for batch processing and immunity checks.
    /// </summary>
    public enum StatusEffectCategory
    {
        CrowdControl,
        DamageOverTime,
        Debuff,
        Buff
    }
    
    /// <summary>
    /// Runtime data for an active status effect on an agent.
    /// Designed to be stack-allocated and cache-friendly.
    /// </summary>
    [System.Serializable]
    public struct ActiveStatusEffect
    {
        /// <summary>Type of this effect</summary>
        public StatusEffectType type;
        
        /// <summary>Is this effect slot currently active</summary>
        public bool isActive;
        
        /// <summary>Time when effect expires (Time.time)</summary>
        public float expireTime;
        
        /// <summary>Time of last tick (for DOT/HOT effects)</summary>
        public float lastTickTime;
        
        /// <summary>Damage/heal per tick (for DOT/HOT)</summary>
        public float tickValue;
        
        /// <summary>Interval between ticks in seconds</summary>
        public float tickInterval;
        
        /// <summary>Stat modifier value (multiplier or flat bonus depending on effect)</summary>
        public float modifierValue;
        
        /// <summary>Source agent index (-1 for environmental/player)</summary>
        public int sourceAgentIndex;
        
        /// <summary>Number of stacks (for stackable effects)</summary>
        public int stacks;
        
        /// <summary>Maximum stacks allowed</summary>
        public int maxStacks;
        
        /// <summary>Clear this effect slot</summary>
        public void Clear()
        {
            isActive = false;
            type = default;
            expireTime = 0f;
            lastTickTime = 0f;
            tickValue = 0f;
            tickInterval = 0f;
            modifierValue = 0f;
            sourceAgentIndex = -1;
            stacks = 0;
            maxStacks = 1;
        }
    }
    
    /// <summary>
    /// Configuration for a status effect that can be applied.
    /// Used as a template when applying effects.
    /// </summary>
    [System.Serializable]
    public class StatusEffectConfig
    {
        [Header("Basic Settings")]
        [Tooltip("Type of status effect")]
        public StatusEffectType effectType;
        
        [Tooltip("Duration in seconds (0 = instant/permanent until dispelled)")]
        [Min(0f)]
        public float duration = 5f;
        
        [Header("Modifier Settings")]
        [Tooltip("Value of the effect:\n" +
                 "- For DOT/HOT: Damage/heal per tick\n" +
                 "- For stat modifiers: Multiplier (1.5 = +50%, 0.5 = -50%)\n" +
                 "- For shield: Shield amount")]
        public float value = 1f;
        
        [Tooltip("For DOT/HOT: Time between ticks in seconds")]
        [Min(0.1f)]
        public float tickInterval = 1f;
        
        [Header("Stacking")]
        [Tooltip("Can this effect stack multiple times")]
        public bool canStack = false;
        
        [Tooltip("Maximum number of stacks")]
        [Min(1)]
        public int maxStacks = 1;
        
        [Tooltip("How stacking behaves:\n" +
                 "- Refresh: New application refreshes duration\n" +
                 "- Extend: New application extends duration\n" +
                 "- Independent: Each stack has own duration")]
        public StackBehavior stackBehavior = StackBehavior.Refresh;
        
        [Header("Visual Feedback")]
        [Tooltip("VFX to spawn on effect apply")]
        public VFXReference applyVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Tooltip("VFX that persists while effect is active (spawned on agent)")]
        public VFXReference persistentVFX = new VFXReference { type = VFXType.None, defaultDuration = 5f };
        
        [Tooltip("VFX to spawn on effect expire/remove")]
        public VFXReference removeVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };
        
        [Header("Audio")]
        [Tooltip("Sound to play on effect apply")]
        public AudioClip applySound;
        
        [Tooltip("Volume of apply sound")]
        [Range(0f, 1f)]
        public float applySoundVolume = 1f;
        
        [Tooltip("Random pitch variation range (±). E.g., 0.1 = pitch varies from 0.9 to 1.1")]
        [Range(0f, 0.5f)]
        public float applySoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for apply sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float applySoundSpatialBlend = 1f;
        
        [Tooltip("Looping sound while effect is active")]
        public AudioClip loopSound;
        
        [Tooltip("Volume of loop sound")]
        [Range(0f, 1f)]
        public float loopSoundVolume = 0.7f;
        
        [Tooltip("Spatial blend for loop sound (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float loopSoundSpatialBlend = 1f;
        
        /// <summary>
        /// Create an ActiveStatusEffect from this config.
        /// </summary>
        public ActiveStatusEffect CreateActiveEffect(float currentTime, int sourceAgentIndex = -1)
        {
            return new ActiveStatusEffect
            {
                type = effectType,
                isActive = true,
                expireTime = duration > 0f ? currentTime + duration : float.MaxValue,
                lastTickTime = currentTime,
                tickValue = value,
                tickInterval = tickInterval,
                modifierValue = value,
                sourceAgentIndex = sourceAgentIndex,
                stacks = 1,
                maxStacks = maxStacks
            };
        }
    }
    
    /// <summary>
    /// How effect stacking behaves when reapplied.
    /// </summary>
    public enum StackBehavior
    {
        /// <summary>New application refreshes duration to full</summary>
        Refresh,
        
        /// <summary>New application extends duration by effect's base duration</summary>
        Extend,
        
        /// <summary>Each stack tracked independently</summary>
        Independent
    }
    
    /// <summary>
    /// Helper class for status effect utilities.
    /// </summary>
    public static class StatusEffectHelper
    {
        /// <summary>
        /// Get the category of a status effect type.
        /// </summary>
        public static StatusEffectCategory GetCategory(StatusEffectType type)
        {
            int typeValue = (int)type;
            
            if (typeValue < 20) return StatusEffectCategory.CrowdControl;
            if (typeValue < 40) return StatusEffectCategory.DamageOverTime;
            if (typeValue < 60) return StatusEffectCategory.Debuff;
            return StatusEffectCategory.Buff;
        }
        
        /// <summary>
        /// Check if an effect type is negative (CC, DOT, or Debuff).
        /// </summary>
        public static bool IsNegativeEffect(StatusEffectType type)
        {
            return GetCategory(type) != StatusEffectCategory.Buff;
        }
        
        /// <summary>
        /// Check if an effect type is a crowd control effect.
        /// </summary>
        public static bool IsCrowdControl(StatusEffectType type)
        {
            return GetCategory(type) == StatusEffectCategory.CrowdControl;
        }
        
        /// <summary>
        /// Check if an effect type prevents movement.
        /// </summary>
        public static bool PreventsMovement(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Stun:
                case StatusEffectType.Freeze:
                case StatusEffectType.Sleep:
                case StatusEffectType.Root:
                    return true;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Check if an effect type prevents attacking.
        /// </summary>
        public static bool PreventsAttack(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Stun:
                case StatusEffectType.Freeze:
                case StatusEffectType.Sleep:
                case StatusEffectType.Daze:
                case StatusEffectType.Fear:
                    return true;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Check if an effect type is broken by damage.
        /// </summary>
        public static bool BrokenByDamage(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Sleep:
                case StatusEffectType.Stun: // Optional - some games break stun on damage
                    return true;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Check if an effect type is a DOT that deals damage.
        /// </summary>
        public static bool IsDamageOverTime(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Poison:
                case StatusEffectType.Burn:
                case StatusEffectType.Bleed:
                case StatusEffectType.Curse:
                    return true;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Check if an effect type is a HOT that heals.
        /// </summary>
        public static bool IsHealOverTime(StatusEffectType type)
        {
            return type == StatusEffectType.Regeneration;
        }
    }
}
