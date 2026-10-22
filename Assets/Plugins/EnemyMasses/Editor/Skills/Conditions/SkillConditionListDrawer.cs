using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;
using System.Linq;
using Arawn.EnemyMasses.Runtime.Skills.Conditions;

namespace Arawn.EnemyMasses.Editor.Skills.Conditions
{
    /// <summary>
    /// Custom property drawer for SkillConditionList.
    /// Provides a clean, foldout-based UI for managing conditions similar to GC2's ConditionList.
    /// </summary>
    [CustomPropertyDrawer(typeof(SkillConditionList))]
    public class SkillConditionListDrawer : PropertyDrawer
    {
        // Type cache for condition types
        private static Type[] s_ConditionTypes;
        private static string[] s_ConditionTypeNames;
        private static bool s_TypesCached = false;
        
        private static void CacheConditionTypes()
        {
            if (s_TypesCached) return;
            
            // Find all non-abstract types that inherit from SkillCondition
            var baseType = typeof(SkillCondition);
            s_ConditionTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => t != baseType && !t.IsAbstract && baseType.IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToArray();
            
            s_ConditionTypeNames = s_ConditionTypes
                .Select(t => ObjectNames.NicifyVariableName(t.Name.Replace("Condition", "")))
                .ToArray();
            
            s_TypesCached = true;
        }
        
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            CacheConditionTypes();
            
            var container = new VisualElement();
            container.style.marginTop = 2;
            container.style.marginBottom = 2;
            
            // Create foldout for the condition list
            var foldout = new Foldout
            {
                text = "Conditions",
                value = false
            };
            foldout.style.marginLeft = 0;
            
            // Get properties
            var logicProp = property.FindPropertyRelative("logic");
            var conditionsProp = property.FindPropertyRelative("conditions");
            
            // Add header with logic selector and add button
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 4;
            
            // Logic dropdown
            var logicContainer = new VisualElement();
            logicContainer.style.flexDirection = FlexDirection.Row;
            logicContainer.style.alignItems = Align.Center;
            
            var logicLabel = new Label("Logic:");
            logicLabel.style.marginRight = 4;
            logicLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            
            var logicField = new EnumField((ConditionLogic)logicProp.enumValueIndex);
            logicField.style.minWidth = 60;
            logicField.RegisterValueChangedCallback(evt =>
            {
                logicProp.enumValueIndex = (int)(ConditionLogic)evt.newValue;
                logicProp.serializedObject.ApplyModifiedProperties();
            });
            
            logicContainer.Add(logicLabel);
            logicContainer.Add(logicField);
            
            // Add condition button
            var addButton = new Button(() =>
            {
                ShowAddConditionMenu(conditionsProp, container);
            })
            {
                text = "+ Add Condition"
            };
            addButton.style.flexGrow = 0;
            
            header.Add(logicContainer);
            header.Add(addButton);
            
            foldout.Add(header);
            
            // Container for condition entries
            var entriesContainer = new VisualElement();
            entriesContainer.name = "ConditionEntries";
            entriesContainer.style.marginLeft = 8;
            
            // Build initial entries
            RebuildConditionEntries(conditionsProp, entriesContainer, container);
            
            foldout.Add(entriesContainer);
            
            // Update foldout text with count
            UpdateFoldoutText(foldout, conditionsProp);
            
            // Track changes to rebuild UI
            foldout.TrackPropertyValue(conditionsProp, prop =>
            {
                RebuildConditionEntries(conditionsProp, entriesContainer, container);
                UpdateFoldoutText(foldout, conditionsProp);
            });
            
            container.Add(foldout);
            
            return container;
        }
        
        private void UpdateFoldoutText(Foldout foldout, SerializedProperty conditionsProp)
        {
            int count = conditionsProp.arraySize;
            foldout.text = count == 0 ? "Conditions (None)" : $"Conditions ({count})";
        }
        
        private void RebuildConditionEntries(SerializedProperty conditionsProp, VisualElement entriesContainer, VisualElement root)
        {
            entriesContainer.Clear();
            
            if (conditionsProp.arraySize == 0)
            {
                var emptyLabel = new Label("No conditions - effect always executes");
                emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                emptyLabel.style.marginTop = 4;
                emptyLabel.style.marginBottom = 4;
                entriesContainer.Add(emptyLabel);
                return;
            }
            
            for (int i = 0; i < conditionsProp.arraySize; i++)
            {
                var conditionProp = conditionsProp.GetArrayElementAtIndex(i);
                var entry = CreateConditionEntry(conditionProp, conditionsProp, i, root);
                entriesContainer.Add(entry);
            }
        }
        
        private VisualElement CreateConditionEntry(SerializedProperty conditionProp, SerializedProperty arrayProp, int index, VisualElement root)
        {
            var entry = new VisualElement();
            entry.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            entry.style.borderTopLeftRadius = entry.style.borderTopRightRadius = 
                entry.style.borderBottomLeftRadius = entry.style.borderBottomRightRadius = 4;
            entry.style.marginBottom = 4;
            entry.style.paddingLeft = entry.style.paddingRight = 8;
            entry.style.paddingTop = entry.style.paddingBottom = 4;
            
            // Header row with type name and remove button
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;
            
            // Get the managed reference type name
            string typeName = GetConditionTypeName(conditionProp);
            var typeLabel = new Label(typeName);
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            typeLabel.style.color = new Color(0.7f, 0.9f, 1f);
            
            // Move up/down and remove buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            
            if (index > 0)
            {
                var moveUpBtn = new Button(() =>
                {
                    arrayProp.MoveArrayElement(index, index - 1);
                    arrayProp.serializedObject.ApplyModifiedProperties();
                })
                {
                    text = "▲"
                };
                moveUpBtn.style.width = 24;
                moveUpBtn.style.marginRight = 2;
                buttonContainer.Add(moveUpBtn);
            }
            
            if (index < arrayProp.arraySize - 1)
            {
                var moveDownBtn = new Button(() =>
                {
                    arrayProp.MoveArrayElement(index, index + 1);
                    arrayProp.serializedObject.ApplyModifiedProperties();
                })
                {
                    text = "▼"
                };
                moveDownBtn.style.width = 24;
                moveDownBtn.style.marginRight = 4;
                buttonContainer.Add(moveDownBtn);
            }
            
            var removeBtn = new Button(() =>
            {
                arrayProp.DeleteArrayElementAtIndex(index);
                arrayProp.serializedObject.ApplyModifiedProperties();
            })
            {
                text = "✕"
            };
            removeBtn.style.width = 24;
            removeBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            buttonContainer.Add(removeBtn);
            
            headerRow.Add(typeLabel);
            headerRow.Add(buttonContainer);
            entry.Add(headerRow);
            
            // Property fields for the condition
            var propsContainer = new VisualElement();
            propsContainer.style.marginLeft = 8;
            
            // Draw all visible children of the managed reference
            var iter = conditionProp.Copy();
            var endProp = conditionProp.GetEndProperty();
            
            if (iter.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(iter, endProp))
                        break;
                    
                    // Skip the managed reference type field itself
                    if (iter.propertyPath.EndsWith("managedReferenceValue"))
                        continue;
                    
                    var field = new PropertyField(iter.Copy());
                    field.Bind(conditionProp.serializedObject);
                    propsContainer.Add(field);
                }
                while (iter.NextVisible(false));
            }
            
            entry.Add(propsContainer);
            
            return entry;
        }
        
        private string GetConditionTypeName(SerializedProperty conditionProp)
        {
            if (conditionProp.managedReferenceValue == null)
                return "Null Condition";
            
            var type = conditionProp.managedReferenceValue.GetType();
            return ObjectNames.NicifyVariableName(type.Name.Replace("Condition", ""));
        }
        
        private void ShowAddConditionMenu(SerializedProperty conditionsProp, VisualElement container)
        {
            var menu = new GenericMenu();
            
            for (int i = 0; i < s_ConditionTypes.Length; i++)
            {
                var type = s_ConditionTypes[i];
                var name = s_ConditionTypeNames[i];
                
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    // Create new instance of the condition
                    var instance = Activator.CreateInstance(type);
                    
                    // Add to array
                    int newIndex = conditionsProp.arraySize;
                    conditionsProp.InsertArrayElementAtIndex(newIndex);
                    var newElement = conditionsProp.GetArrayElementAtIndex(newIndex);
                    newElement.managedReferenceValue = instance;
                    
                    conditionsProp.serializedObject.ApplyModifiedProperties();
                });
            }
            
            if (s_ConditionTypes.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No condition types found"));
            }
            
            menu.ShowAsContext();
        }
        
        // IMGUI fallback for contexts that don't support UIElements
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
