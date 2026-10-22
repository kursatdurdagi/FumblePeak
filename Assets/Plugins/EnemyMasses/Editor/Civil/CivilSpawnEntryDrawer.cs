using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Civil;

namespace Arawn.EnemyMasses.Editor.Civil
{
    /// <summary>
    /// Custom property drawer for CivilSpawnEntry to provide a cleaner UI.
    /// </summary>
    [CustomPropertyDrawer(typeof(CivilSpawnEntry))]
    public class CivilSpawnEntryDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float Spacing = 2f;
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return LineHeight;
            
            var intervalMode = property.FindPropertyRelative("intervalMode");
            bool isFixed = intervalMode.enumValueIndex == 0;
            
            // Base lines: header, faction, weight, intervalMode, spawnCount, maxAlive, enabled
            int lines = 8;
            
            // Add interval lines based on mode
            lines += isFixed ? 1 : 2; // fixed = 1 field, random = 2 fields
            
            return lines * (LineHeight + Spacing);
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            var factionIndex = property.FindPropertyRelative("factionIndex");
            var weight = property.FindPropertyRelative("weight");
            var intervalMode = property.FindPropertyRelative("intervalMode");
            var fixedInterval = property.FindPropertyRelative("fixedInterval");
            var minInterval = property.FindPropertyRelative("minInterval");
            var maxInterval = property.FindPropertyRelative("maxInterval");
            var spawnCount = property.FindPropertyRelative("spawnCount");
            var maxAliveAgents = property.FindPropertyRelative("maxAliveAgents");
            var enabled = property.FindPropertyRelative("enabled");
            
            // Get faction name for header
            string factionName = GetFactionDisplayName(factionIndex.intValue);
            string headerLabel = $"{factionName} ({GetIntervalDisplayText(property)})";
            
            Rect foldoutRect = new Rect(position.x, position.y, position.width, LineHeight);
            
            // Add color indicator based on enabled state
            Color originalColor = GUI.backgroundColor;
            if (!enabled.boolValue)
            {
                GUI.backgroundColor = new Color(0.6f, 0.6f, 0.6f);
            }
            
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, headerLabel, true);
            
            GUI.backgroundColor = originalColor;
            
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = position.y + LineHeight + Spacing;
                Rect lineRect = new Rect(position.x, y, position.width, LineHeight);
                
                // Enabled toggle
                EditorGUI.PropertyField(lineRect, enabled, new GUIContent("Enabled"));
                lineRect.y += LineHeight + Spacing;
                
                // Faction selection
                DrawFactionDropdown(lineRect, factionIndex);
                lineRect.y += LineHeight + Spacing;
                
                // Weight
                EditorGUI.PropertyField(lineRect, weight, new GUIContent("Selection Weight", "Higher weight = more likely to be selected when multiple entries can spawn"));
                lineRect.y += LineHeight + Spacing;
                
                // Interval mode
                EditorGUI.PropertyField(lineRect, intervalMode, new GUIContent("Interval Mode"));
                lineRect.y += LineHeight + Spacing;
                
                // Interval fields based on mode
                if (intervalMode.enumValueIndex == 0) // Fixed
                {
                    EditorGUI.PropertyField(lineRect, fixedInterval, new GUIContent("Interval (seconds)"));
                    lineRect.y += LineHeight + Spacing;
                }
                else // RandomRange
                {
                    EditorGUI.PropertyField(lineRect, minInterval, new GUIContent("Min Interval"));
                    lineRect.y += LineHeight + Spacing;
                    
                    EditorGUI.PropertyField(lineRect, maxInterval, new GUIContent("Max Interval"));
                    lineRect.y += LineHeight + Spacing;
                    
                    // Validate min/max
                    if (minInterval.floatValue > maxInterval.floatValue)
                    {
                        maxInterval.floatValue = minInterval.floatValue;
                    }
                }
                
                // Spawn count
                EditorGUI.PropertyField(lineRect, spawnCount, new GUIContent("Spawn Count", "Number of agents to spawn each time"));
                lineRect.y += LineHeight + Spacing;
                
                // Max alive
                EditorGUI.PropertyField(lineRect, maxAliveAgents, new GUIContent("Max Alive", "Maximum agents from this entry that can be alive (0 = unlimited)"));
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.EndProperty();
        }
        
        private string GetFactionDisplayName(int factionIndex)
        {
            if (factionIndex < 0)
                return "Any Faction";
            
            // Try to get actual faction name from CrowdController
            var crowdController = Object.FindFirstObjectByType<EnemyMassesCrowdController>();
            if (crowdController != null && 
                crowdController.EnemyCrowds != null && 
                factionIndex < crowdController.EnemyCrowds.Count)
            {
                var crowd = crowdController.EnemyCrowds[factionIndex];
                if (crowd != null)
                    return crowd.name;
            }
            
            return $"Faction {factionIndex}";
        }
        
        private string GetIntervalDisplayText(SerializedProperty property)
        {
            var intervalMode = property.FindPropertyRelative("intervalMode");
            
            if (intervalMode.enumValueIndex == 0) // Fixed
            {
                float interval = property.FindPropertyRelative("fixedInterval").floatValue;
                return $"every {interval:F1}s";
            }
            else // RandomRange
            {
                float min = property.FindPropertyRelative("minInterval").floatValue;
                float max = property.FindPropertyRelative("maxInterval").floatValue;
                return $"{min:F0}-{max:F0}s";
            }
        }
        
        private void DrawFactionDropdown(Rect position, SerializedProperty factionIndex)
        {
            // Build options list
            var options = new System.Collections.Generic.List<GUIContent>();
            options.Add(new GUIContent("Any Civil Faction (-1)"));
            
            var crowdController = Object.FindFirstObjectByType<EnemyMassesCrowdController>();
            if (crowdController != null && crowdController.EnemyCrowds != null)
            {
                for (int i = 0; i < crowdController.EnemyCrowds.Count; i++)
                {
                    var crowd = crowdController.EnemyCrowds[i];
                    string name = crowd != null ? crowd.name : $"Unnamed";
                    options.Add(new GUIContent($"{i}: {name}"));
                }
            }
            
            // Convert current faction index to popup index
            int popupIndex = factionIndex.intValue + 1; // -1 becomes 0, 0 becomes 1, etc.
            if (popupIndex < 0) popupIndex = 0;
            if (popupIndex >= options.Count) popupIndex = 0;
            
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(position, new GUIContent("Faction"), popupIndex, options.ToArray());
            
            if (EditorGUI.EndChangeCheck())
            {
                // Convert popup index back to faction index
                factionIndex.intValue = newIndex - 1;
            }
        }
    }
}
