using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom property drawer for FactionMask that displays a dropdown similar to LayerMask.
    /// Automatically discovers all faction names from EnemyCrowd assets in the project.
    /// </summary>
    [CustomPropertyDrawer(typeof(FactionMask))]
    public class FactionMaskDrawer : PropertyDrawer
    {
        private static string[] _cachedFactionNames;
        private static double _lastCacheTime;
        private const double CACHE_DURATION = 2.0; // Refresh cache every 2 seconds
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            // Get the selected factions property
            SerializedProperty selectedFactionsProperty = property.FindPropertyRelative("selectedFactions");
            
            // Get all available factions
            string[] allFactions = GetAllFactionNames();
            
            // Build current selection set
            HashSet<string> currentSelection = new HashSet<string>();
            for (int i = 0; i < selectedFactionsProperty.arraySize; i++)
            {
                string faction = selectedFactionsProperty.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(faction))
                {
                    currentSelection.Add(faction);
                }
            }
            
            // Draw label
            position = EditorGUI.PrefixLabel(position, label);
            
            // Build display text
            string displayText;
            if (currentSelection.Count == 0)
            {
                displayText = "Nothing";
            }
            else if (allFactions.Length > 0 && currentSelection.Count == allFactions.Length)
            {
                displayText = "Everything";
            }
            else if (currentSelection.Count == 1)
            {
                displayText = currentSelection.First();
            }
            else
            {
                displayText = $"Mixed ({currentSelection.Count})";
            }
            
            // Draw dropdown button
            if (EditorGUI.DropdownButton(position, new GUIContent(displayText), FocusType.Keyboard))
            {
                GenericMenu menu = new GenericMenu();
                
                // Nothing option
                menu.AddItem(new GUIContent("Nothing"), currentSelection.Count == 0, () =>
                {
                    selectedFactionsProperty.ClearArray();
                    property.serializedObject.ApplyModifiedProperties();
                });
                
                // Everything option (only if there are factions)
                if (allFactions.Length > 0)
                {
                    menu.AddItem(new GUIContent("Everything"), currentSelection.Count == allFactions.Length, () =>
                    {
                        selectedFactionsProperty.ClearArray();
                        for (int i = 0; i < allFactions.Length; i++)
                        {
                            selectedFactionsProperty.InsertArrayElementAtIndex(i);
                            selectedFactionsProperty.GetArrayElementAtIndex(i).stringValue = allFactions[i];
                        }
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
                
                menu.AddSeparator("");
                
                // Individual faction options
                if (allFactions.Length == 0)
                {
                    menu.AddDisabledItem(new GUIContent("No factions found"));
                    menu.AddDisabledItem(new GUIContent("(Add EnemyCrowd assets with faction names)"));
                }
                else
                {
                    foreach (string faction in allFactions)
                    {
                        bool isSelected = currentSelection.Contains(faction);
                        string factionCapture = faction; // Capture for lambda
                        
                        menu.AddItem(new GUIContent(faction), isSelected, () =>
                        {
                            if (isSelected)
                            {
                                // Remove faction
                                for (int i = selectedFactionsProperty.arraySize - 1; i >= 0; i--)
                                {
                                    if (selectedFactionsProperty.GetArrayElementAtIndex(i).stringValue == factionCapture)
                                    {
                                        selectedFactionsProperty.DeleteArrayElementAtIndex(i);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                // Add faction
                                selectedFactionsProperty.InsertArrayElementAtIndex(selectedFactionsProperty.arraySize);
                                selectedFactionsProperty.GetArrayElementAtIndex(selectedFactionsProperty.arraySize - 1).stringValue = factionCapture;
                            }
                            property.serializedObject.ApplyModifiedProperties();
                        });
                    }
                }
                
                menu.DropDown(position);
            }
            
            EditorGUI.EndProperty();
        }
        
        /// <summary>
        /// Gets all unique faction names from EnemyCrowd assets in the project
        /// </summary>
        private string[] GetAllFactionNames()
        {
            // Use cached value if recent
            if (_cachedFactionNames != null && EditorApplication.timeSinceStartup - _lastCacheTime < CACHE_DURATION)
            {
                return _cachedFactionNames;
            }
            
            HashSet<string> factionNames = new HashSet<string>();
            
            // Find all EnemyCrowd assets in the project
            string[] guids = AssetDatabase.FindAssets("t:EnemyCrowd");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EnemyCrowd enemyCrowd = AssetDatabase.LoadAssetAtPath<EnemyCrowd>(path);
                if (enemyCrowd != null && !string.IsNullOrEmpty(enemyCrowd.factionName))
                {
                    factionNames.Add(enemyCrowd.factionName);
                }
            }
            
            // Also check scene for EnemyMassesCrowdController
            EnemyMassesCrowdController[] controllers = Object.FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller.EnemyCrowds != null)
                {
                    foreach (var crowd in controller.EnemyCrowds)
                    {
                        if (crowd != null && !string.IsNullOrEmpty(crowd.factionName))
                        {
                            factionNames.Add(crowd.factionName);
                        }
                    }
                }
            }
            
            // Sort and cache
            _cachedFactionNames = factionNames.OrderBy(f => f).ToArray();
            _lastCacheTime = EditorApplication.timeSinceStartup;
            
            return _cachedFactionNames;
        }
        
        /// <summary>
        /// Force refresh the faction cache (useful after adding new EnemyCrowd assets)
        /// </summary>
        public static void RefreshFactionCache()
        {
            _cachedFactionNames = null;
        }
    }
}
