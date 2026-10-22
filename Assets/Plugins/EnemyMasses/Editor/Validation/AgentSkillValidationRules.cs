using UnityEditor;
using System.Collections.Generic;
using Arawn.EnemyMasses.Editor.Validation;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Defines validation rules for AgentSkill ScriptableObjects.
    /// </summary>
    public static class AgentSkillValidationRules
    {
        /// <summary>
        /// Gets all validation rules for AgentSkill.
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
                    PropertyName = "skillName",
                    DisplayName = "Skill Name",
                    IsRequired = true,
                    Validator = (prop) => !string.IsNullOrWhiteSpace(prop.stringValue) && prop.stringValue != "New Skill",
                    IncompleteMessage = "Enter a unique skill name",
                    HelpText = "Display name for this skill"
                },
                
                new FieldValidationRule
                {
                    PropertyName = "effects",
                    DisplayName = "Effects",
                    IsRequired = true,
                    Validator = EditorFieldValidation.ValidateArrayNotEmpty,
                    IncompleteMessage = "Add at least one effect (damage, heal, etc.)",
                    HelpText = "What this skill does when executed"
                },
                
                new FieldValidationRule
                {
                    PropertyName = "cooldown",
                    DisplayName = "Cooldown",
                    IsRequired = true,
                    Validator = EditorFieldValidation.ValidateNonNegative,
                    IncompleteMessage = "Set a cooldown time >= 0",
                    HelpText = "Time between skill uses"
                },
                
                // ═══════════════════════════════════════════════════════════════════
                // OPTIONAL: PROJECTILE TARGETING
                // ═══════════════════════════════════════════════════════════════════
                
                new FieldValidationRule
                {
                    PropertyName = "projectileTargeting.projectilePrefab",
                    DisplayName = "Projectile Prefab",
                    IsRequired = false,
                    EnabledByProperty = null, // Check targeting type manually
                    Validator = EditorFieldValidation.ValidateObjectNotNull,
                    IncompleteMessage = "Assign a projectile prefab",
                    HelpText = "The projectile to spawn"
                },
                
                // ═══════════════════════════════════════════════════════════════════
                // OPTIONAL: LOCAL SCALING
                // ═══════════════════════════════════════════════════════════════════
                
                new FieldValidationRule
                {
                    PropertyName = "scalingConfig",
                    DisplayName = "Local Scaling",
                    IsRequired = false,
                    EnabledByProperty = "scalingConfig.enableScaling",
                    Validator = EditorFieldValidation.ValidateAlways,
                    IncompleteMessage = "Configure scaling formulas",
                    HelpText = "Local skill scaling configuration"
                },
            };
        }
        
        /// <summary>
        /// Validates an AgentSkill and returns a summary.
        /// </summary>
        public static ValidationSummary Validate(SerializedObject serializedObject)
        {
            var rules = GetRules();
            
            // Custom validation: If targeting type is Projectile, projectile prefab is required
            var targetingTypeProp = serializedObject.FindProperty("targetingType");
            if (targetingTypeProp != null && targetingTypeProp.enumValueIndex == (int)SkillTargetingType.Projectile)
            {
                // Mark projectile prefab as required for this skill
                var projectileRule = rules.Find(r => r.PropertyName == "projectileTargeting.projectilePrefab");
                if (projectileRule != null)
                {
                    projectileRule.IsRequired = true;
                }
            }
            
            // Custom validation: If scaling mode is Local, local config matters
            var scalingModeProp = serializedObject.FindProperty("scalingMode");
            if (scalingModeProp != null && scalingModeProp.enumValueIndex == 1) // Local = 1
            {
                var scalingRule = rules.Find(r => r.PropertyName == "scalingConfig");
                if (scalingRule != null)
                {
                    scalingRule.IsRequired = false; // Still optional but shown
                    scalingRule.EnabledByProperty = null; // Always validate when mode is Local
                }
            }
            
            return EditorFieldValidation.Validate(serializedObject, rules);
        }
    }
}
