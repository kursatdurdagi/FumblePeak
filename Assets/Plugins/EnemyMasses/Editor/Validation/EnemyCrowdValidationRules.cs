using UnityEditor;
using System.Collections.Generic;
using Arawn.EnemyMasses.Editor.Validation;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Defines validation rules for EnemyCrowd ScriptableObjects.
    /// Separates validation logic from the editor for maintainability.
    /// </summary>
    public static class EnemyCrowdValidationRules
    {
        /// <summary>
        /// Gets all validation rules for EnemyCrowd.
        /// </summary>
        public static List<FieldValidationRule> GetRules()
        {
            return new List<FieldValidationRule>
            {
                // ═══════════════════════════════════════════════════════════════════
                // REQUIRED FIELDS (Basic Setup)
                // ═══════════════════════════════════════════════════════════════════
                
                new FieldValidationRule
                {
                    PropertyName = "prefab",
                    DisplayName = "Prefab",
                    IsRequired = true,
                    Validator = EditorFieldValidation.ValidateObjectNotNull,
                    IncompleteMessage = "Assign a prefab for this crowd",
                    HelpText = "The character prefab to spawn for this faction"
                },
                
                new FieldValidationRule
                {
                    PropertyName = "factionName",
                    DisplayName = "Faction Name",
                    IsRequired = true,
                    Validator = EditorFieldValidation.ValidateStringNotEmpty,
                    IncompleteMessage = "Enter a faction name",
                    HelpText = "Unique name for this faction (e.g., 'Orcs', 'Undead')"
                },
                
                new FieldValidationRule
                {
                    PropertyName = "agentBaseHealth",
                    DisplayName = "Base Health",
                    IsRequired = true,
                    Validator = EditorFieldValidation.ValidatePositiveNumber,
                    IncompleteMessage = "Set base health > 0",
                    HelpText = "Starting health for agents in this crowd"
                },
                
                // ═══════════════════════════════════════════════════════════════════
                // OPTIONAL: SKILLS SYSTEM
                // ═══════════════════════════════════════════════════════════════════
                
                new FieldValidationRule
                {
                    PropertyName = "skillSystemConfig.skillSlots",
                    DisplayName = "Skill Slots",
                    IsRequired = false,
                    EnabledByProperty = "skillSystemConfig.enableSkillSystem",
                    Validator = (prop) => !prop.isArray || prop.arraySize > 0,
                    IncompleteMessage = "Add at least one skill",
                    HelpText = "Skills available to this faction"
                },
                
                // ═══════════════════════════════════════════════════════════════════
                // OPTIONAL: CIVIL AI
                // ═══════════════════════════════════════════════════════════════════
                
                new FieldValidationRule
                {
                    PropertyName = "civilAIConfig.civilBehaviorPreset",
                    DisplayName = "Civil Behavior",
                    IsRequired = false,
                    EnabledByProperty = "civilAIConfig.enableCivilAI",
                    Validator = (prop) => prop.enumValueIndex > 0, // Not "None"
                    IncompleteMessage = "Select a civil behavior preset",
                    HelpText = "How civilians behave when not in combat"
                },
                
                // ═══════════════════════════════════════════════════════════════════
                // OPTIONAL: LEVEL SCALING
                // ═══════════════════════════════════════════════════════════════════
                
                new FieldValidationRule
                {
                    PropertyName = "globalSkillScaling",
                    DisplayName = "Global Scaling",
                    IsRequired = false,
                    EnabledByProperty = "globalSkillScaling.enableScaling",
                    Validator = EditorFieldValidation.ValidateAlways, // Just needs to be enabled
                    IncompleteMessage = "Configure scaling formulas",
                    HelpText = "Global skill scaling configuration"
                },
            };
        }
        
        /// <summary>
        /// Validates an EnemyCrowd and returns a summary.
        /// </summary>
        public static ValidationSummary Validate(SerializedObject serializedObject)
        {
            return EditorFieldValidation.Validate(serializedObject, GetRules());
        }
    }
}
