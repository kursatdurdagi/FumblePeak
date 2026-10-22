using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills.Conditions
{
    /// <summary>
    /// How multiple conditions in a list are evaluated.
    /// </summary>
    public enum ConditionLogic
    {
        [Tooltip("All conditions must pass (AND logic).")]
        All,
        
        [Tooltip("At least one condition must pass (OR logic).")]
        Any
    }
    
    /// <summary>
    /// A list of conditions that can be attached to a SkillAction.
    /// Similar to Game Creator 2's ConditionList.
    /// </summary>
    [Serializable]
    public class SkillConditionList
    {
        [Tooltip("How to combine multiple conditions.\n" +
                 "• All: Every condition must pass (AND)\n" +
                 "• Any: At least one must pass (OR)")]
        [SerializeField] private ConditionLogic logic = ConditionLogic.All;
        
        [Tooltip("The list of conditions to check.")]
        [SerializeReference] private List<SkillCondition> conditions = new List<SkillCondition>();
        
        /// <summary>
        /// The logic mode for combining conditions.
        /// </summary>
        public ConditionLogic Logic => logic;
        
        /// <summary>
        /// The conditions in this list.
        /// </summary>
        public IReadOnlyList<SkillCondition> Conditions => conditions;
        
        /// <summary>
        /// Number of conditions in the list.
        /// </summary>
        public int Count => conditions.Count;
        
        /// <summary>
        /// Check if all conditions are met based on the logic mode.
        /// Returns true if the list is empty (no conditions = always pass).
        /// </summary>
        public bool Check(SkillExecutionContext context)
        {
            if (conditions == null || conditions.Count == 0)
                return true; // No conditions = always pass
            
            if (logic == ConditionLogic.All)
            {
                // AND logic: all must pass
                foreach (var condition in conditions)
                {
                    if (condition == null) continue;
                    if (!condition.Check(context))
                        return false;
                }
                return true;
            }
            else // ConditionLogic.Any
            {
                // OR logic: at least one must pass
                foreach (var condition in conditions)
                {
                    if (condition == null) continue;
                    if (condition.Check(context))
                        return true;
                }
                return false;
            }
        }
        
        /// <summary>
        /// Get a human-readable description of all conditions.
        /// </summary>
        public string GetDescription()
        {
            if (conditions == null || conditions.Count == 0)
                return "(no conditions)";
            
            var descriptions = new List<string>();
            foreach (var condition in conditions)
            {
                if (condition != null)
                    descriptions.Add(condition.GetDescription());
            }
            
            if (descriptions.Count == 0)
                return "(no conditions)";
            
            string separator = logic == ConditionLogic.All ? " AND " : " OR ";
            return string.Join(separator, descriptions);
        }
        
        /// <summary>
        /// Add a condition to the list.
        /// </summary>
        public void Add(SkillCondition condition)
        {
            conditions ??= new List<SkillCondition>();
            conditions.Add(condition);
        }
        
        /// <summary>
        /// Remove a condition at the specified index.
        /// </summary>
        public void RemoveAt(int index)
        {
            if (conditions != null && index >= 0 && index < conditions.Count)
                conditions.RemoveAt(index);
        }
        
        /// <summary>
        /// Clear all conditions.
        /// </summary>
        public void Clear()
        {
            conditions?.Clear();
        }
    }
}
