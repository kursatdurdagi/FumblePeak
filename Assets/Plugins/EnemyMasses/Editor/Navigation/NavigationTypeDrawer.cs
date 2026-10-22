using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom property drawer for NavigationType that only shows available options.
    /// A* Pathfinding options are hidden when the package is not installed.
    /// </summary>
    [CustomPropertyDrawer(typeof(NavigationType))]
    public class NavigationTypeDrawer : PropertyDrawer
    {
        private static NavigationType[] _availableTypes;
        private static string[] _displayNames;
        private static bool _initialized;
        
        private static void Initialize()
        {
            if (_initialized) return;
            
            _availableTypes = NavigationAgentFactory.GetAvailableTypes();
            _displayNames = new string[_availableTypes.Length];
            
            for (int i = 0; i < _availableTypes.Length; i++)
            {
                _displayNames[i] = _availableTypes[i].GetDisplayName();
            }
            
            _initialized = true;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Initialize();
            
            EditorGUI.BeginProperty(position, label, property);
            
            // Get current value (use intValue to get the actual enum value, not the index)
            NavigationType currentValue = (NavigationType)property.intValue;
            
            // Find current index in available types
            int currentIndex = 0;
            for (int i = 0; i < _availableTypes.Length; i++)
            {
                if (_availableTypes[i] == currentValue)
                {
                    currentIndex = i;
                    break;
                }
            }
            
            // Check if current value is not available (e.g., A* was uninstalled)
            bool isCurrentValueUnavailable = !NavigationAgentFactory.IsTypeAvailable(currentValue);
            
            if (isCurrentValueUnavailable)
            {
                // Show warning and force to first available type
                EditorGUI.BeginChangeCheck();
                
                // Draw warning icon
                Rect warningRect = new Rect(position.x, position.y, 20, EditorGUIUtility.singleLineHeight);
                Rect popupRect = new Rect(position.x + 22, position.y, position.width - 22, EditorGUIUtility.singleLineHeight);
                
                GUI.Label(warningRect, new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml").image, 
                    $"'{currentValue}' is not available. The required package may not be installed."));
                
                int newIndex = EditorGUI.Popup(popupRect, label, 0, GetDisplayNamesWithWarning(currentValue));
                
                if (EditorGUI.EndChangeCheck() || newIndex != currentIndex)
                {
                    if (newIndex < _availableTypes.Length)
                    {
                        property.intValue = (int)_availableTypes[newIndex];
                    }
                }
            }
            else
            {
                // Normal display
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(position, label, currentIndex, GetGUIContentArray());
                
                if (EditorGUI.EndChangeCheck())
                {
                    property.intValue = (int)_availableTypes[newIndex];
                }
            }
            
            EditorGUI.EndProperty();
        }
        
        private GUIContent[] GetGUIContentArray()
        {
            GUIContent[] contents = new GUIContent[_availableTypes.Length];
            for (int i = 0; i < _availableTypes.Length; i++)
            {
                contents[i] = new GUIContent(_displayNames[i], _availableTypes[i].GetTooltip());
            }
            return contents;
        }
        
        private GUIContent[] GetDisplayNamesWithWarning(NavigationType unavailableType)
        {
            GUIContent[] contents = new GUIContent[_availableTypes.Length + 1];
            
            // First entry is the unavailable type with warning
            contents[0] = new GUIContent($"⚠ {unavailableType} (Unavailable)", 
                "This navigation type is not available. Select a different option.");
            
            // Rest are available types
            for (int i = 0; i < _availableTypes.Length; i++)
            {
                contents[i + 1] = new GUIContent(_displayNames[i], _availableTypes[i].GetTooltip());
            }
            
            return contents;
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
        
        /// <summary>
        /// Force re-initialization (call this if packages are installed/uninstalled at runtime).
        /// </summary>
        public static void RefreshAvailableTypes()
        {
            _initialized = false;
        }
    }
}
