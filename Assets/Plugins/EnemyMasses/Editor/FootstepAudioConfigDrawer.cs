using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom property drawer for FootstepAudioConfig.
    /// Shows/hides surface clip arrays based on enableSurfaceDetection toggle.
    /// </summary>
    [CustomPropertyDrawer(typeof(FootstepAudioConfig))]
    public class FootstepAudioConfigDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();
            container.style.marginTop = 5;
            
            // Basic footstep settings
            var basicSection = new Foldout { text = "Basic Settings", value = true };
            basicSection.style.marginBottom = 5;
            
            AddPropertyField(basicSection, property.FindPropertyRelative("footstepClips"), "Footstep Clips");
            AddPropertyField(basicSection, property.FindPropertyRelative("volume"), "Volume");
            AddPropertyField(basicSection, property.FindPropertyRelative("pitchVariation"), "Pitch Variation");
            container.Add(basicSection);
            
            // 3D Audio settings
            var audioSection = new Foldout { text = "3D Audio", value = false };
            audioSection.style.marginBottom = 5;
            
            AddPropertyField(audioSection, property.FindPropertyRelative("spatialBlend"), "Spatial Blend");
            AddPropertyField(audioSection, property.FindPropertyRelative("minDistance"), "Min Distance");
            AddPropertyField(audioSection, property.FindPropertyRelative("maxDistance"), "Max Distance");
            container.Add(audioSection);
            
            // Playback settings
            var playbackSection = new Foldout { text = "Playback Settings", value = true };
            playbackSection.style.marginBottom = 5;
            
            AddPropertyField(playbackSection, property.FindPropertyRelative("playbackMode"), "Playback Mode");
            AddPropertyField(playbackSection, property.FindPropertyRelative("minInterval"), "Min Interval");
            AddPropertyField(playbackSection, property.FindPropertyRelative("maxInterval"), "Max Interval");
            AddPropertyField(playbackSection, property.FindPropertyRelative("minSpeedThreshold"), "Min Speed Threshold");
            AddPropertyField(playbackSection, property.FindPropertyRelative("scaleIntervalWithSpeed"), "Scale Interval With Speed");
            AddPropertyField(playbackSection, property.FindPropertyRelative("referenceSpeed"), "Reference Speed");
            AddPropertyField(playbackSection, property.FindPropertyRelative("maxConcurrentSounds"), "Max Concurrent Sounds");
            container.Add(playbackSection);
            
            // Surface detection section
            var surfaceSection = new Foldout { text = "Surface Detection", value = false };
            surfaceSection.style.marginBottom = 5;
            
            var enableSurfaceProp = property.FindPropertyRelative("enableSurfaceDetection");
            var enableSurfaceField = new PropertyField(enableSurfaceProp, "Enable Surface Detection");
            surfaceSection.Add(enableSurfaceField);
            
            // Help box explaining surface detection
            var surfaceHelp = new HelpBox(
                "Surface detection samples terrain textures and uses physics materials/tags to determine ground type. " +
                "Create a Surface Mapping asset (Right-click → Create → Enemy Masses → Surface Mapping) to configure how textures map to surface types.",
                HelpBoxMessageType.Info
            );
            surfaceHelp.style.marginTop = 5;
            surfaceHelp.style.marginBottom = 10;
            surfaceSection.Add(surfaceHelp);
            
            // Container for surface detection sub-properties
            var surfaceSubContainer = new VisualElement();
            surfaceSubContainer.style.marginLeft = 15;
            
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("surfaceMapping"), "Surface Mapping");
            
            // Surface clip arrays
            var clipsLabel = new Label("Surface-Specific Clips");
            clipsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            clipsLabel.style.marginTop = 10;
            clipsLabel.style.marginBottom = 5;
            surfaceSubContainer.Add(clipsLabel);
            
            var clipsHelp = new Label("Add clips for each surface type. Falls back to default clips if empty.");
            clipsHelp.style.whiteSpace = WhiteSpace.Normal;
            clipsHelp.style.fontSize = 10;
            clipsHelp.style.color = new Color(0.5f, 0.5f, 0.5f);
            clipsHelp.style.marginBottom = 5;
            surfaceSubContainer.Add(clipsHelp);
            
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("grassFootstepClips"), "🌿 Grass Clips");
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("stoneFootstepClips"), "🪨 Stone Clips");
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("waterFootstepClips"), "💧 Water Clips");
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("woodFootstepClips"), "🪵 Wood Clips");
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("metalFootstepClips"), "⚙️ Metal Clips");
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("sandFootstepClips"), "🏜️ Sand Clips");
            AddPropertyField(surfaceSubContainer, property.FindPropertyRelative("snowFootstepClips"), "❄️ Snow Clips");
            
            surfaceSection.Add(surfaceSubContainer);
            container.Add(surfaceSection);
            
            // Initial visibility
            surfaceSubContainer.style.display = enableSurfaceProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Register callback to show/hide when enabled changes
            enableSurfaceField.RegisterValueChangeCallback(evt =>
            {
                surfaceSubContainer.style.display = evt.changedProperty.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            return container;
        }
        
        private void AddPropertyField(VisualElement parent, SerializedProperty prop, string label = null)
        {
            if (prop == null) 
            {
                Debug.LogWarning($"[FootstepAudioConfigDrawer] Property '{label}' not found!");
                return;
            }
            
            var field = label != null ? new PropertyField(prop, label) : new PropertyField(prop);
            field.BindProperty(prop);  // Ensure binding
            parent.Add(field);
        }
        
        // IMGUI fallback for older Unity versions
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Calculate height based on expanded foldouts
            float height = EditorGUIUtility.singleLineHeight; // Header
            
            if (property.isExpanded)
            {
                height += GetChildPropertiesHeight(property);
            }
            
            return height;
        }
        
        private float GetChildPropertiesHeight(SerializedProperty property)
        {
            float height = 0;
            SerializedProperty iterator = property.Copy();
            SerializedProperty endProperty = property.GetEndProperty();
            
            iterator.NextVisible(true); // Move to first child
            
            while (!SerializedProperty.EqualContents(iterator, endProperty))
            {
                height += EditorGUI.GetPropertyHeight(iterator, true);
                height += EditorGUIUtility.standardVerticalSpacing;
                
                if (!iterator.NextVisible(false))
                    break;
            }
            
            return height;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            // Draw default property field as fallback
            EditorGUI.PropertyField(position, property, label, true);
            
            EditorGUI.EndProperty();
        }
    }
}
