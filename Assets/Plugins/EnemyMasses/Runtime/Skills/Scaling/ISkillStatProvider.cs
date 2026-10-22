using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills.Scaling
{
    /// <summary>
    /// Interface for providing stat values to the skill scaling system.
    /// Implement this interface to integrate external stat systems (e.g., Game Creator 2 Stats).
    /// 
    /// Example integration with Game Creator 2:
    /// <code>
    /// public class GC2StatProvider : ISkillStatProvider
    /// {
    ///     public float GetStat(string statName, object context)
    ///     {
    ///         if (context is CrowdAgent agent && agent.navigationAgent?.Transform != null)
    ///         {
    ///             var traits = agent.navigationAgent.Transform.GetComponent&lt;Traits&gt;();
    ///             if (traits != null)
    ///             {
    ///                 return (float)traits.RuntimeStats.Get(statName)?.Value ?? 0f;
    ///             }
    ///         }
    ///         return 0f;
    ///     }
    ///     
    ///     public int GetLevel(object context)
    ///     {
    ///         if (context is CrowdAgent agent && agent.navigationAgent?.Transform != null)
    ///         {
    ///             var traits = agent.navigationAgent.Transform.GetComponent&lt;Traits&gt;();
    ///             if (traits != null)
    ///             {
    ///                 return (int)traits.RuntimeStats.Get("level")?.Value ?? 1;
    ///             }
    ///         }
    ///         return 1;
    ///     }
    ///     
    ///     public bool HasStat(string statName) => true; // Or check RuntimeStats
    /// }
    /// </code>
    /// </summary>
    public interface ISkillStatProvider
    {
        /// <summary>
        /// Gets a stat value by name for the given context (typically a CrowdAgent).
        /// Common stat names: "strength", "intelligence", "dexterity", "level", "magic_power", etc.
        /// </summary>
        /// <param name="statName">The name of the stat to retrieve</param>
        /// <param name="context">The context object (usually CrowdAgent or GameObject)</param>
        /// <returns>The stat value, or 0 if not found</returns>
        float GetStat(string statName, object context);
        
        /// <summary>
        /// Gets the level of the entity for scaling calculations.
        /// </summary>
        /// <param name="context">The context object (usually CrowdAgent or GameObject)</param>
        /// <returns>The level (1-99 typically), defaults to 1</returns>
        int GetLevel(object context);
        
        /// <summary>
        /// Checks if a stat exists for the given context.
        /// </summary>
        /// <param name="statName">The stat name to check</param>
        /// <returns>True if the stat exists and can be queried</returns>
        bool HasStat(string statName);
    }
    
    /// <summary>
    /// Extended interface for stat providers that support formula evaluation.
    /// Implement this for more complex scaling calculations.
    /// </summary>
    public interface ISkillStatProviderExtended : ISkillStatProvider
    {
        /// <summary>
        /// Evaluates a custom formula string with the given context.
        /// Formula can reference stats by name: "base + (level * 5) + strength * 0.5"
        /// </summary>
        /// <param name="formula">The formula string to evaluate</param>
        /// <param name="context">The context object</param>
        /// <param name="baseValue">The base value to use in the formula</param>
        /// <returns>The calculated result</returns>
        float EvaluateFormula(string formula, object context, float baseValue);
        
        /// <summary>
        /// Gets all available stat names for editor dropdowns.
        /// </summary>
        string[] GetAvailableStatNames();
    }
}
