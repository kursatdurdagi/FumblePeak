using UnityEngine;
using System;

namespace Arawn.EnemyMasses.Runtime.Skills.Scaling
{
    /// <summary>
    /// Defines how a skill value scales with level and/or stats.
    /// Supports multiple built-in scaling types plus custom curves.
    /// </summary>
    [Serializable]
    public class SkillScalingFormula
    {
        /// <summary>
        /// The type of scaling calculation to use.
        /// </summary>
        public enum ScalingType
        {
            /// <summary>No scaling - returns base value only</summary>
            None,
            /// <summary>Linear: base + (level * perLevelBonus)</summary>
            Linear,
            /// <summary>Percentage: base * (1 + level * percentPerLevel / 100)</summary>
            Percentage,
            /// <summary>Exponential: base * (growthRate ^ (level - 1))</summary>
            Exponential,
            /// <summary>Logarithmic: base + (perLevelBonus * log(level + 1))</summary>
            Logarithmic,
            /// <summary>Polynomial: base + (perLevelBonus * level ^ exponent)</summary>
            Polynomial,
            /// <summary>Custom curve: base * curve.Evaluate(normalizedLevel)</summary>
            CustomCurve,
            /// <summary>Stat-based: base + (statValue * statMultiplier)</summary>
            StatBased,
            /// <summary>Compound: combines level scaling with stat scaling</summary>
            Compound
        }
        
        [Tooltip("The type of scaling calculation")]
        public ScalingType type = ScalingType.None;
        
        [Tooltip("The base value before any scaling is applied")]
        public float baseValue = 10f;
        
        [Header("Level Scaling")]
        [Tooltip("Flat bonus per level (for Linear, Logarithmic, Polynomial)")]
        public float perLevelBonus = 1f;
        
        [Tooltip("Percentage increase per level (for Percentage type)")]
        [Range(0f, 100f)]
        public float percentPerLevel = 5f;
        
        [Tooltip("Growth rate for exponential scaling (e.g., 1.05 = 5% per level)")]
        [Range(1f, 2f)]
        public float growthRate = 1.05f;
        
        [Tooltip("Exponent for polynomial scaling")]
        [Range(0.1f, 3f)]
        public float exponent = 1.5f;
        
        [Header("Custom Curve")]
        [Tooltip("Custom scaling curve. X = normalized level (0-1 for levels 1-99), Y = multiplier")]
        public AnimationCurve customCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
        
        [Tooltip("Maximum level for curve normalization")]
        public int maxLevel = 99;
        
        [Header("Stat Scaling")]
        [Tooltip("The stat name to use for stat-based scaling")]
        public string statName = "strength";
        
        [Tooltip("Multiplier applied to the stat value")]
        public float statMultiplier = 0.5f;
        
        [Header("Bounds")]
        [Tooltip("Minimum value (result will never go below this)")]
        public float minValue = 0f;
        
        [Tooltip("Maximum value (result will never exceed this). Set to 0 for no cap.")]
        public float maxValue = 0f;
        
        /// <summary>
        /// Evaluates the formula and returns the scaled value.
        /// </summary>
        /// <param name="level">The current level (1-99)</param>
        /// <param name="context">Context object for stat lookups (typically CrowdAgent)</param>
        /// <returns>The calculated value after scaling</returns>
        public float Evaluate(int level, object context = null)
        {
            float result = baseValue;
            level = Mathf.Max(1, level); // Ensure minimum level of 1
            
            switch (type)
            {
                case ScalingType.None:
                    result = baseValue;
                    break;
                    
                case ScalingType.Linear:
                    // base + (level - 1) * perLevelBonus
                    // Level 1 = base, Level 2 = base + perLevelBonus, etc.
                    result = baseValue + (level - 1) * perLevelBonus;
                    break;
                    
                case ScalingType.Percentage:
                    // base * (1 + (level - 1) * percentPerLevel / 100)
                    result = baseValue * (1f + (level - 1) * percentPerLevel / 100f);
                    break;
                    
                case ScalingType.Exponential:
                    // base * growthRate^(level - 1)
                    result = baseValue * Mathf.Pow(growthRate, level - 1);
                    break;
                    
                case ScalingType.Logarithmic:
                    // base + perLevelBonus * ln(level)
                    // Logarithmic growth - fast early, slow later
                    result = baseValue + perLevelBonus * Mathf.Log(level + 1);
                    break;
                    
                case ScalingType.Polynomial:
                    // base + perLevelBonus * (level - 1)^exponent
                    result = baseValue + perLevelBonus * Mathf.Pow(level - 1, exponent);
                    break;
                    
                case ScalingType.CustomCurve:
                    // Normalize level to 0-1 range and evaluate curve
                    float normalizedLevel = (float)(level - 1) / Mathf.Max(1, maxLevel - 1);
                    float curveMultiplier = customCurve?.Evaluate(normalizedLevel) ?? 1f;
                    result = baseValue * curveMultiplier;
                    break;
                    
                case ScalingType.StatBased:
                    // base + statValue * statMultiplier
                    float statValue = GetStatValue(context);
                    result = baseValue + statValue * statMultiplier;
                    break;
                    
                case ScalingType.Compound:
                    // Combines level scaling (linear) with stat scaling
                    float levelBonus = (level - 1) * perLevelBonus;
                    float statBonus = GetStatValue(context) * statMultiplier;
                    result = baseValue + levelBonus + statBonus;
                    break;
            }
            
            // Apply bounds
            result = Mathf.Max(minValue, result);
            if (maxValue > 0f)
            {
                result = Mathf.Min(maxValue, result);
            }
            
            return result;
        }
        
        /// <summary>
        /// Evaluates with an explicit stat value (for preview/editor).
        /// </summary>
        public float EvaluateWithStat(int level, float statValue)
        {
            float result = baseValue;
            level = Mathf.Max(1, level);
            
            switch (type)
            {
                case ScalingType.StatBased:
                    result = baseValue + statValue * statMultiplier;
                    break;
                case ScalingType.Compound:
                    float levelBonus = (level - 1) * perLevelBonus;
                    float statBonus = statValue * statMultiplier;
                    result = baseValue + levelBonus + statBonus;
                    break;
                default:
                    return Evaluate(level, null);
            }
            
            result = Mathf.Max(minValue, result);
            if (maxValue > 0f) result = Mathf.Min(maxValue, result);
            return result;
        }
        
        private float GetStatValue(object context)
        {
            if (string.IsNullOrEmpty(statName) || context == null)
                return 0f;
                
            return SkillStatProviderRegistry.GetStat(statName, context);
        }
        
        /// <summary>
        /// Creates a simple linear scaling formula.
        /// </summary>
        public static SkillScalingFormula Linear(float baseValue, float perLevel)
        {
            return new SkillScalingFormula
            {
                type = ScalingType.Linear,
                baseValue = baseValue,
                perLevelBonus = perLevel
            };
        }
        
        /// <summary>
        /// Creates a percentage-based scaling formula.
        /// </summary>
        public static SkillScalingFormula Percentage(float baseValue, float percentPerLevel)
        {
            return new SkillScalingFormula
            {
                type = ScalingType.Percentage,
                baseValue = baseValue,
                percentPerLevel = percentPerLevel
            };
        }
        
        /// <summary>
        /// Creates an exponential scaling formula.
        /// </summary>
        public static SkillScalingFormula Exponential(float baseValue, float growthRate)
        {
            return new SkillScalingFormula
            {
                type = ScalingType.Exponential,
                baseValue = baseValue,
                growthRate = growthRate
            };
        }
    }
}
