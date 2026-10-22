using UnityEditor;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Factions;

namespace Arawn.EnemyMasses.Editor.Factions
{
    /// <summary>
    /// Property drawer for ReadOnlyAttribute - displays fields as read-only in the inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
    
    /// <summary>
    /// Property drawer for FactionRelationshipConfig - provides a cleaner inspector UI.
    /// </summary>
    [CustomPropertyDrawer(typeof(FactionRelationshipConfig))]
    public class FactionRelationshipConfigDrawer : PropertyDrawer
    {
        private bool _showSpecificRelationships = true;
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float currentY = position.y;
            
            // Foldout header
            var foldoutRect = new Rect(position.x, currentY, position.width, lineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            currentY += lineHeight + spacing;
            
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                
                // Player Relationship
                var playerRelProp = property.FindPropertyRelative("defaultPlayerRelationship");
                var playerRect = new Rect(position.x, currentY, position.width, lineHeight);
                EditorGUI.PropertyField(playerRect, playerRelProp, new GUIContent("Player Relationship", 
                    "How this faction relates to player characters by default."));
                currentY += lineHeight + spacing;
                
                // Default Faction Relationship
                var factionRelProp = property.FindPropertyRelative("defaultFactionRelationship");
                var factionRect = new Rect(position.x, currentY, position.width, lineHeight);
                EditorGUI.PropertyField(factionRect, factionRelProp, new GUIContent("Default Faction Relationship", 
                    "Default relationship toward other factions when no specific relationship is defined."));
                currentY += lineHeight + spacing;
                
                // Same Faction Different Owner
                var sameFactionProp = property.FindPropertyRelative("sameFactionDifferentOwner");
                var sameFactionRect = new Rect(position.x, currentY, position.width, lineHeight);
                EditorGUI.PropertyField(sameFactionRect, sameFactionProp, new GUIContent("Same Faction Diff. Owner", 
                    "Relationship between agents of the same faction but different player owners."));
                currentY += lineHeight + spacing;
                
                // Same Owner Always Ally
                var sameOwnerProp = property.FindPropertyRelative("sameOwnerAlwaysAlly");
                var sameOwnerRect = new Rect(position.x, currentY, position.width, lineHeight);
                EditorGUI.PropertyField(sameOwnerRect, sameOwnerProp, new GUIContent("Same Owner = Ally", 
                    "When enabled, agents with the same owner are always allies regardless of faction."));
                currentY += lineHeight + spacing;
                
                // Specific Relationships header
                var specificProp = property.FindPropertyRelative("specificRelationships");
                currentY += spacing * 2;
                
                var headerRect = new Rect(position.x, currentY, position.width, lineHeight);
                _showSpecificRelationships = EditorGUI.Foldout(headerRect, _showSpecificRelationships, 
                    new GUIContent($"Specific Faction Relationships ({specificProp.arraySize})", 
                    "Override relationships for specific factions by name."), true);
                currentY += lineHeight + spacing;
                
                if (_showSpecificRelationships)
                {
                    // Draw the array
                    EditorGUI.indentLevel++;
                    
                    // Array size
                    var sizeRect = new Rect(position.x, currentY, position.width, lineHeight);
                    specificProp.arraySize = EditorGUI.IntField(sizeRect, "Size", specificProp.arraySize);
                    currentY += lineHeight + spacing;
                    
                    // Array elements
                    for (int i = 0; i < specificProp.arraySize; i++)
                    {
                        var element = specificProp.GetArrayElementAtIndex(i);
                        var nameProp = element.FindPropertyRelative("targetFactionName");
                        var relProp = element.FindPropertyRelative("relationship");
                        
                        // Draw on single line
                        float halfWidth = (position.width - 20) / 2;
                        var nameRect = new Rect(position.x, currentY, halfWidth, lineHeight);
                        var relRect = new Rect(position.x + halfWidth + 10, currentY, halfWidth, lineHeight);
                        
                        EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);
                        EditorGUI.PropertyField(relRect, relProp, GUIContent.none);
                        currentY += lineHeight + spacing;
                    }
                    
                    // Add button
                    var addRect = new Rect(position.x + position.width - 60, currentY, 60, lineHeight);
                    if (GUI.Button(addRect, "+ Add"))
                    {
                        specificProp.InsertArrayElementAtIndex(specificProp.arraySize);
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.EndProperty();
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = lineHeight + spacing; // Foldout
            
            if (property.isExpanded)
            {
                height += (lineHeight + spacing) * 4; // 4 main properties
                height += spacing * 2; // Extra spacing before specific relationships
                height += lineHeight + spacing; // Specific relationships header
                
                if (_showSpecificRelationships)
                {
                    var specificProp = property.FindPropertyRelative("specificRelationships");
                    height += lineHeight + spacing; // Size field
                    height += (lineHeight + spacing) * specificProp.arraySize; // Elements
                    height += lineHeight + spacing; // Add button
                }
            }
            
            return height;
        }
    }
    
    /// <summary>
    /// Property drawer for SpecificFactionRelationship.
    /// </summary>
    [CustomPropertyDrawer(typeof(SpecificFactionRelationship))]
    public class SpecificFactionRelationshipDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            var nameProp = property.FindPropertyRelative("targetFactionName");
            var relProp = property.FindPropertyRelative("relationship");
            
            float halfWidth = (position.width - 10) / 2;
            var nameRect = new Rect(position.x, position.y, halfWidth, EditorGUIUtility.singleLineHeight);
            var relRect = new Rect(position.x + halfWidth + 10, position.y, halfWidth, EditorGUIUtility.singleLineHeight);
            
            EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);
            EditorGUI.PropertyField(relRect, relProp, GUIContent.none);
            
            EditorGUI.EndProperty();
        }
    }
}
