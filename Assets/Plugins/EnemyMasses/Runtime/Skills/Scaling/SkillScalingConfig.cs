using UnityEngine;
using System;

namespace Arawn.EnemyMasses.Runtime.Skills.Scaling
{
    /// <summary>
    /// Configuration for how a skill's various properties scale with level/stats.
    /// Add this to AgentSkill to enable optional scaling.
    /// </summary>
    [Serializable]
    public class SkillScalingConfig
    {
        [Tooltip("Enable skill scaling based on level/stats")]
        public bool enableScaling = false;
        
        [Header("Damage Scaling")]
        [Tooltip("How damage values scale")]
        public SkillScalingFormula damageScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.Linear,
            baseValue = 10f,
            perLevelBonus = 2f
        };
        
        [Header("Healing Scaling")]
        [Tooltip("How healing values scale")]
        public SkillScalingFormula healingScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.Linear,
            baseValue = 15f,
            perLevelBonus = 3f
        };
        
        [Header("Effect Radius Scaling")]
        [Tooltip("How AOE radius scales (typically slower growth)")]
        public SkillScalingFormula radiusScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.Logarithmic,
            baseValue = 3f,
            perLevelBonus = 0.5f,
            maxValue = 15f // Cap radius growth
        };
        
        [Header("Duration Scaling")]
        [Tooltip("How effect durations scale")]
        public SkillScalingFormula durationScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.Percentage,
            baseValue = 5f,
            percentPerLevel = 2f,
            maxValue = 30f // Cap duration
        };
        
        [Header("Status Effect Scaling")]
        [Tooltip("How status effect strength scales (slow amount, DoT damage, etc.)")]
        public SkillScalingFormula statusEffectScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.Linear,
            baseValue = 1f,
            perLevelBonus = 0.1f
        };
        
        [Header("Cooldown Scaling")]
        [Tooltip("How cooldown scales (negative perLevelBonus to reduce cooldown with level)")]
        public SkillScalingFormula cooldownScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.None, // Disabled by default
            baseValue = 10f,
            perLevelBonus = -0.05f, // Reduces cooldown by 0.05s per level
            minValue = 1f // Minimum 1 second cooldown
        };
        
        [Header("Projectile Scaling")]
        [Tooltip("How projectile count scales (for multi-projectile skills)")]
        public SkillScalingFormula projectileCountScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.None,
            baseValue = 1f,
            perLevelBonus = 0f
        };
        
        [Header("Resource Cost Scaling")]
        [Tooltip("How resource cost scales with level.\n" +
                 "Use positive perLevelBonus to increase cost (higher-level skills cost more),\n" +
                 "or negative to decrease cost (mastery reduces cost).")]
        public SkillScalingFormula resourceCostScaling = new SkillScalingFormula
        {
            type = SkillScalingFormula.ScalingType.None, // Disabled by default
            baseValue = 10f,
            perLevelBonus = 0f,
            minValue = 0f // Cost can never go negative
        };
        
        /// <summary>
        /// Gets the scaled damage value.
        /// </summary>
        public float GetScaledDamage(float baseDamage, int level, object context = null)
        {
            if (!enableScaling || damageScaling.type == SkillScalingFormula.ScalingType.None)
                return baseDamage;
                
            // Use formula's base value, or override with provided base
            var formula = damageScaling;
            if (baseDamage > 0) formula.baseValue = baseDamage;
            return formula.Evaluate(level, context);
        }
        
        /// <summary>
        /// Gets the scaled healing value.
        /// </summary>
        public float GetScaledHealing(float baseHealing, int level, object context = null)
        {
            if (!enableScaling || healingScaling.type == SkillScalingFormula.ScalingType.None)
                return baseHealing;
                
            var formula = healingScaling;
            if (baseHealing > 0) formula.baseValue = baseHealing;
            return formula.Evaluate(level, context);
        }
        
        /// <summary>
        /// Gets the scaled AOE radius.
        /// </summary>
        public float GetScaledRadius(float baseRadius, int level, object context = null)
        {
            if (!enableScaling || radiusScaling.type == SkillScalingFormula.ScalingType.None)
                return baseRadius;
                
            var formula = radiusScaling;
            if (baseRadius > 0) formula.baseValue = baseRadius;
            return formula.Evaluate(level, context);
        }
        
        /// <summary>
        /// Gets the scaled duration.
        /// </summary>
        public float GetScaledDuration(float baseDuration, int level, object context = null)
        {
            if (!enableScaling || durationScaling.type == SkillScalingFormula.ScalingType.None)
                return baseDuration;
                
            var formula = durationScaling;
            if (baseDuration > 0) formula.baseValue = baseDuration;
            return formula.Evaluate(level, context);
        }
        
        /// <summary>
        /// Gets the scaled status effect strength.
        /// </summary>
        public float GetScaledStatusEffect(float baseValue, int level, object context = null)
        {
            if (!enableScaling || statusEffectScaling.type == SkillScalingFormula.ScalingType.None)
                return baseValue;
                
            var formula = statusEffectScaling;
            if (baseValue > 0) formula.baseValue = baseValue;
            return formula.Evaluate(level, context);
        }
        
        /// <summary>
        /// Gets the scaled cooldown.
        /// </summary>
        public float GetScaledCooldown(float baseCooldown, int level, object context = null)
        {
            if (!enableScaling || cooldownScaling.type == SkillScalingFormula.ScalingType.None)
                return baseCooldown;
                
            var formula = cooldownScaling;
            if (baseCooldown > 0) formula.baseValue = baseCooldown;
            return formula.Evaluate(level, context);
        }
        
        /// <summary>
        /// Gets the scaled projectile count (rounded to int).
        /// </summary>
        public int GetScaledProjectileCount(int baseCount, int level, object context = null)
        {
            if (!enableScaling || projectileCountScaling.type == SkillScalingFormula.ScalingType.None)
                return baseCount;
                
            var formula = projectileCountScaling;
            formula.baseValue = baseCount;
            return Mathf.RoundToInt(formula.Evaluate(level, context));
        }
        
        /// <summary>
        /// Gets the scaled resource cost.
        /// Returns the base cost if scaling is disabled or resource cost scaling type is None.
        /// </summary>
        public float GetScaledResourceCost(float baseCost, int level, object context = null)
        {
            if (!enableScaling || resourceCostScaling.type == SkillScalingFormula.ScalingType.None)
                return baseCost;
                
            var formula = resourceCostScaling;
            if (baseCost > 0) formula.baseValue = baseCost;
            return Mathf.Max(0f, formula.Evaluate(level, context));
        }
        
        /// <summary>
        /// Creates a default scaling config with common settings.
        /// </summary>
        public static SkillScalingConfig CreateDefault()
        {
            return new SkillScalingConfig
            {
                enableScaling = true,
                damageScaling = SkillScalingFormula.Linear(10f, 2f),
                healingScaling = SkillScalingFormula.Linear(15f, 3f),
                radiusScaling = new SkillScalingFormula
                {
                    type = SkillScalingFormula.ScalingType.Logarithmic,
                    baseValue = 3f,
                    perLevelBonus = 0.5f,
                    maxValue = 15f
                },
                durationScaling = SkillScalingFormula.Percentage(5f, 2f),
                statusEffectScaling = SkillScalingFormula.Linear(1f, 0.1f)
            };
        }
    }
    
    /// <summary>
    /// Extension methods for applying scaling to skill execution.
    /// </summary>
    public static class SkillScalingExtensions
    {
        /// <summary>
        /// Gets the caster's level for scaling calculations.
        /// </summary>
        public static int GetCasterLevel(this SkillExecutionContext context)
        {
            if (context.Caster == null) return 1;
            return SkillStatProviderRegistry.GetLevel(context.Caster);
        }
        
        /// <summary>
        /// Gets a stat value from the caster.
        /// </summary>
        public static float GetCasterStat(this SkillExecutionContext context, string statName)
        {
            if (context.Caster == null) return 0f;
            return SkillStatProviderRegistry.GetStat(statName, context.Caster);
        }
    }
}
