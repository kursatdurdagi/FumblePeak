// ═══════════════════════════════════════════════════════════════════════════════
// Example: Game Creator 2 Stats Module Integration
// ═══════════════════════════════════════════════════════════════════════════════
// 
// This file shows how to integrate the EnemyMasses skill scaling system with
// Game Creator 2's Stats module. This is NOT a required dependency - it's just
// an example for users who want to connect the two systems.
//
// IMPORTANT: This file will NOT compile unless you have Game Creator 2 installed.
// It's provided as documentation/reference only. Delete or exclude from build.
//
// To use:
// 1. Install Game Creator 2 and the Stats module from the Asset Store
// 2. Uncomment the code below
// 3. Call RegisterProvider in your game initialization
// ═══════════════════════════════════════════════════════════════════════════════

#if false // Set to true if you have Game Creator 2 Stats installed

using UnityEngine;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Skills.Scaling;

// Game Creator 2 namespaces (requires GC2 Stats module)
using GameCreator.Runtime.Stats;
using GameCreator.Runtime.Common;

namespace Arawn.EnemyMasses.Runtime.Skills.Integration
{
    /// <summary>
    /// Example integration adapter for Game Creator 2's Stats system.
    /// Maps GC2 Stats to the EnemyMasses skill scaling system.
    /// 
    /// Usage:
    /// <code>
    /// // In your game initialization (e.g., RuntimeInitializeOnLoadMethod)
    /// SkillStatProviderRegistry.RegisterProvider(new GameCreator2StatProvider());
    /// </code>
    /// </summary>
    public class GameCreator2StatProvider : ISkillStatProvider
    {
        /// <summary>
        /// Gets a stat value from the agent's GC2 Traits component.
        /// </summary>
        public float GetStat(string statName, object context)
        {
            Traits traits = GetTraits(context);
            if (traits == null) return 0f;
            
            // Try to get the stat from GC2's RuntimeStats
            var stat = traits.RuntimeStats.Get(statName);
            if (stat != null)
            {
                return (float)stat.Value;
            }
            
            // Fallback: check attributes
            var attribute = traits.RuntimeAttributes.Get(statName);
            if (attribute != null)
            {
                return (float)attribute.Value;
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Gets the level from a "level" stat or attribute.
        /// </summary>
        public int GetLevel(object context)
        {
            Traits traits = GetTraits(context);
            if (traits == null) return 1;
            
            // Common level stat names to check
            string[] levelStatNames = { "level", "Level", "LVL", "lvl", "character-level" };
            
            foreach (var statName in levelStatNames)
            {
                var stat = traits.RuntimeStats.Get(statName);
                if (stat != null)
                {
                    return Mathf.RoundToInt((float)stat.Value);
                }
            }
            
            return 1;
        }
        
        /// <summary>
        /// Checks if a stat exists in GC2.
        /// </summary>
        public bool HasStat(string statName)
        {
            // GC2 Stats are defined in the Stats asset - we assume it exists
            // A more thorough implementation would check the Stats database
            return true;
        }
        
        /// <summary>
        /// Gets the Traits component from a CrowdAgent or GameObject.
        /// </summary>
        private Traits GetTraits(object context)
        {
            if (context is CrowdAgent agent)
            {
                // Try to get Traits from the active navigation object first.
                if (agent.navigationAgent?.Transform != null)
                {
                    return agent.navigationAgent.Transform.GetComponent<Traits>();
                }
                if (agent.crowdInstance != null)
                {
                    return agent.crowdInstance.GetComponent<Traits>();
                }
                if (agent.transform != null)
                {
                    return agent.transform.GetComponent<Traits>();
                }
            }
            else if (context is GameObject go)
            {
                return go.GetComponent<Traits>();
            }
            else if (context is Component comp)
            {
                return comp.GetComponent<Traits>();
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Extended provider that supports GC2 Formula evaluation.
    /// </summary>
    public class GameCreator2ExtendedStatProvider : GameCreator2StatProvider, ISkillStatProviderExtended
    {
        /// <summary>
        /// Evaluates a GC2-style formula string.
        /// Note: GC2's Formula class would be used here for complex expressions.
        /// </summary>
        public float EvaluateFormula(string formula, object context, float baseValue)
        {
            // For simple formulas, we can parse them ourselves
            // For complex GC2 formulas, you'd use Formula.Calculate()
            
            // Example simple substitution:
            // "base + level * 2" -> substitute values
            
            int level = GetLevel(context);
            string evaluated = formula
                .Replace("base", baseValue.ToString())
                .Replace("level", level.ToString())
                .Replace("lvl", level.ToString());
            
            // For actual formula evaluation, use GC2's Formula system:
            // return Formula.Calculate(evaluated, context);
            
            // Fallback: return base value if we can't evaluate
            return baseValue;
        }
        
        /// <summary>
        /// Gets available stat names from GC2's Stats database.
        /// </summary>
        public string[] GetAvailableStatNames()
        {
            // In a real implementation, you'd query GC2's Stats asset
            // For now, return common stat names
            return new string[]
            {
                "level",
                "strength",
                "dexterity", 
                "intelligence",
                "vitality",
                "magic-power",
                "attack-damage",
                "ability-power",
                "armor",
                "magic-resist"
            };
        }
    }
    
    /// <summary>
    /// Static helper to auto-register GC2 provider on game start.
    /// </summary>
    public static class GameCreator2Integration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoRegister()
        {
            // Auto-register if GC2 Stats is detected
            // You might want more control over this - in that case, call manually
            
            #if UNITY_EDITOR
            Debug.Log("[EnemyMasses] Auto-registering Game Creator 2 stat provider");
            #endif
            
            SkillStatProviderRegistry.RegisterProvider(new GameCreator2ExtendedStatProvider());
        }
    }
}

#endif // GC2 Stats conditional compilation
