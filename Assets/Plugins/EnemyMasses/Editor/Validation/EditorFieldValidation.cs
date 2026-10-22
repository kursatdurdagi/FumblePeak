using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Editor.Validation
{
    /// <summary>
    /// Validation status for a field or section.
    /// </summary>
    public enum ValidationStatus
    {
        /// <summary>Required field that is not set up correctly</summary>
        RequiredIncomplete,
        /// <summary>Required field that is set up correctly</summary>
        RequiredComplete,
        /// <summary>Optional field that is enabled but not configured</summary>
        OptionalIncomplete,
        /// <summary>Optional field that is enabled and configured correctly</summary>
        OptionalComplete,
        /// <summary>Optional field that is disabled (no validation needed)</summary>
        OptionalDisabled,
        /// <summary>Informational only, no validation</summary>
        Info
    }
    
    /// <summary>
    /// Represents a validation rule for a field.
    /// </summary>
    public class FieldValidationRule
    {
        public string PropertyName { get; set; }
        public string DisplayName { get; set; }
        public bool IsRequired { get; set; }
        public string EnabledByProperty { get; set; } // If set, this is an optional field enabled by another property
        public Func<SerializedProperty, bool> Validator { get; set; }
        public string HelpText { get; set; }
        public string IncompleteMessage { get; set; }
    }
    
    /// <summary>
    /// Result of validating a single field.
    /// </summary>
    public class ValidationResult
    {
        public string PropertyName { get; set; }
        public string DisplayName { get; set; }
        public ValidationStatus Status { get; set; }
        public string Message { get; set; }
    }
    
    /// <summary>
    /// Summary of all validation results.
    /// </summary>
    public class ValidationSummary
    {
        public int RequiredTotal { get; set; }
        public int RequiredComplete { get; set; }
        public int OptionalEnabled { get; set; }
        public int OptionalComplete { get; set; }
        public List<ValidationResult> Results { get; set; } = new List<ValidationResult>();
        
        public bool IsFullyConfigured => RequiredComplete == RequiredTotal && OptionalComplete == OptionalEnabled;
        public bool HasRequiredIncomplete => RequiredComplete < RequiredTotal;
        public bool HasOptionalIncomplete => OptionalComplete < OptionalEnabled;
        
        public string GetSummaryText()
        {
            if (IsFullyConfigured && RequiredTotal > 0)
                return $"✓ All {RequiredTotal} required fields configured" + 
                       (OptionalEnabled > 0 ? $", {OptionalEnabled} optional features enabled" : "");
            
            var parts = new List<string>();
            
            if (HasRequiredIncomplete)
                parts.Add($"⚠ {RequiredTotal - RequiredComplete} required field(s) need setup");
            else if (RequiredTotal > 0)
                parts.Add($"✓ {RequiredComplete} required fields OK");
                
            if (HasOptionalIncomplete)
                parts.Add($"⚠ {OptionalEnabled - OptionalComplete} optional feature(s) need configuration");
            else if (OptionalEnabled > 0)
                parts.Add($"✓ {OptionalEnabled} optional features configured");
                
            return string.Join(" | ", parts);
        }
    }
    
    /// <summary>
    /// Utility class for validating editor fields and applying visual feedback.
    /// </summary>
    public static class EditorFieldValidation
    {
        // Colors for validation states
        public static readonly Color RequiredIncompleteColor = new Color(0.8f, 0.2f, 0.2f, 0.3f);
        public static readonly Color RequiredCompleteColor = new Color(0.2f, 0.7f, 0.2f, 0.2f);
        public static readonly Color OptionalIncompleteColor = new Color(0.9f, 0.6f, 0.1f, 0.3f);
        public static readonly Color OptionalCompleteColor = new Color(0.2f, 0.6f, 0.8f, 0.2f);
        public static readonly Color OptionalDisabledColor = new Color(0.5f, 0.5f, 0.5f, 0.1f);
        
        /// <summary>
        /// Validates all fields using the provided rules.
        /// </summary>
        public static ValidationSummary Validate(SerializedObject serializedObject, List<FieldValidationRule> rules)
        {
            var summary = new ValidationSummary();
            
            foreach (var rule in rules)
            {
                var result = ValidateField(serializedObject, rule);
                summary.Results.Add(result);
                
                switch (result.Status)
                {
                    case ValidationStatus.RequiredIncomplete:
                    case ValidationStatus.RequiredComplete:
                        summary.RequiredTotal++;
                        if (result.Status == ValidationStatus.RequiredComplete)
                            summary.RequiredComplete++;
                        break;
                        
                    case ValidationStatus.OptionalIncomplete:
                    case ValidationStatus.OptionalComplete:
                        summary.OptionalEnabled++;
                        if (result.Status == ValidationStatus.OptionalComplete)
                            summary.OptionalComplete++;
                        break;
                }
            }
            
            return summary;
        }
        
        /// <summary>
        /// Validates a single field.
        /// </summary>
        public static ValidationResult ValidateField(SerializedObject serializedObject, FieldValidationRule rule)
        {
            var result = new ValidationResult
            {
                PropertyName = rule.PropertyName,
                DisplayName = rule.DisplayName
            };
            
            var prop = serializedObject.FindProperty(rule.PropertyName);
            if (prop == null)
            {
                result.Status = ValidationStatus.Info;
                result.Message = "Property not found";
                return result;
            }
            
            // Check if this is an optional field that depends on another property
            if (!string.IsNullOrEmpty(rule.EnabledByProperty))
            {
                var enabledProp = serializedObject.FindProperty(rule.EnabledByProperty);
                if (enabledProp != null && !enabledProp.boolValue)
                {
                    result.Status = ValidationStatus.OptionalDisabled;
                    result.Message = "Feature disabled";
                    return result;
                }
            }
            
            // Run the validator
            bool isValid = rule.Validator?.Invoke(prop) ?? true;
            
            if (rule.IsRequired)
            {
                result.Status = isValid ? ValidationStatus.RequiredComplete : ValidationStatus.RequiredIncomplete;
            }
            else
            {
                result.Status = isValid ? ValidationStatus.OptionalComplete : ValidationStatus.OptionalIncomplete;
            }
            
            result.Message = isValid ? "OK" : (rule.IncompleteMessage ?? "Needs configuration");
            return result;
        }
        
        /// <summary>
        /// Gets the background color for a validation status.
        /// </summary>
        public static Color GetStatusColor(ValidationStatus status)
        {
            return status switch
            {
                ValidationStatus.RequiredIncomplete => RequiredIncompleteColor,
                ValidationStatus.RequiredComplete => RequiredCompleteColor,
                ValidationStatus.OptionalIncomplete => OptionalIncompleteColor,
                ValidationStatus.OptionalComplete => OptionalCompleteColor,
                ValidationStatus.OptionalDisabled => OptionalDisabledColor,
                _ => Color.clear
            };
        }
        
        /// <summary>
        /// Gets an icon for a validation status.
        /// </summary>
        public static string GetStatusIcon(ValidationStatus status)
        {
            return status switch
            {
                ValidationStatus.RequiredIncomplete => "🔴",
                ValidationStatus.RequiredComplete => "🟢",
                ValidationStatus.OptionalIncomplete => "🟡",
                ValidationStatus.OptionalComplete => "🔵",
                ValidationStatus.OptionalDisabled => "⚫",
                _ => "⚪"
            };
        }
        
        /// <summary>
        /// Creates a visual element that displays a validation summary.
        /// </summary>
        public static VisualElement CreateValidationSummaryElement(ValidationSummary summary)
        {
            var container = new VisualElement();
            container.style.marginBottom = 10;
            container.style.paddingTop = 8;
            container.style.paddingBottom = 8;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.borderTopLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderBottomRightRadius = 4;
            
            // Background color based on status
            if (summary.HasRequiredIncomplete)
                container.style.backgroundColor = RequiredIncompleteColor;
            else if (summary.HasOptionalIncomplete)
                container.style.backgroundColor = OptionalIncompleteColor;
            else
                container.style.backgroundColor = RequiredCompleteColor;
            
            // Summary text
            var label = new Label(summary.GetSummaryText());
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.Normal;
            container.Add(label);
            
            return container;
        }
        
        /// <summary>
        /// Applies validation styling to a property field.
        /// </summary>
        public static void ApplyValidationStyle(VisualElement element, ValidationStatus status)
        {
            element.style.backgroundColor = GetStatusColor(status);
            element.style.borderTopLeftRadius = 3;
            element.style.borderTopRightRadius = 3;
            element.style.borderBottomLeftRadius = 3;
            element.style.borderBottomRightRadius = 3;
            element.style.paddingLeft = 4;
            element.style.paddingRight = 4;
            element.style.marginTop = 1;
            element.style.marginBottom = 1;
        }
        
        /// <summary>
        /// Creates a validated property field with visual feedback.
        /// </summary>
        public static VisualElement CreateValidatedField(
            SerializedObject serializedObject,
            string propertyName,
            string label,
            ValidationStatus status,
            string tooltip = null)
        {
            var container = new VisualElement();
            ApplyValidationStyle(container, status);
            
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                
                // Status icon
                var icon = new Label(GetStatusIcon(status));
                icon.style.width = 20;
                icon.style.unityTextAlign = TextAnchor.MiddleCenter;
                row.Add(icon);
                
                // Property field
                var field = new PropertyField(prop, label);
                field.style.flexGrow = 1;
                field.tooltip = tooltip;
                field.Bind(serializedObject);
                row.Add(field);
                
                container.Add(row);
            }
            
            return container;
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // Common Validators
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Validates that an object reference is not null.</summary>
        public static bool ValidateObjectNotNull(SerializedProperty prop)
        {
            return prop.objectReferenceValue != null;
        }
        
        /// <summary>Validates that a string is not empty.</summary>
        public static bool ValidateStringNotEmpty(SerializedProperty prop)
        {
            return !string.IsNullOrWhiteSpace(prop.stringValue);
        }
        
        /// <summary>Validates that a number is greater than zero.</summary>
        public static bool ValidatePositiveNumber(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Float => prop.floatValue > 0,
                SerializedPropertyType.Integer => prop.intValue > 0,
                _ => true
            };
        }
        
        /// <summary>Validates that a number is not negative.</summary>
        public static bool ValidateNonNegative(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Float => prop.floatValue >= 0,
                SerializedPropertyType.Integer => prop.intValue >= 0,
                _ => true
            };
        }
        
        /// <summary>Validates that an array/list has at least one element.</summary>
        public static bool ValidateArrayNotEmpty(SerializedProperty prop)
        {
            return prop.isArray && prop.arraySize > 0;
        }
        
        /// <summary>Validates that an enum is not the default (0) value.</summary>
        public static bool ValidateEnumNotDefault(SerializedProperty prop)
        {
            return prop.enumValueIndex > 0;
        }
        
        /// <summary>Always returns true (for info-only fields).</summary>
        public static bool ValidateAlways(SerializedProperty prop) => true;
    }
}
