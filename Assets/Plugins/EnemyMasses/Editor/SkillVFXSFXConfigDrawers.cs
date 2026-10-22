using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom property drawer for SkillVFXConfig that provides a compact, 
    /// organized display with nested foldouts.
    /// </summary>
    [CustomPropertyDrawer(typeof(SkillVFXConfig))]
    public class SkillVFXConfigDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        private const float IndentWidth = 15f;
        
        // Track foldout states per property path
        private static System.Collections.Generic.Dictionary<string, bool> _foldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        private static System.Collections.Generic.Dictionary<string, bool> _spawnFoldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        private static System.Collections.Generic.Dictionary<string, bool> _timingFoldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        private static System.Collections.Generic.Dictionary<string, bool> _perfFoldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        
        private bool GetFoldout(System.Collections.Generic.Dictionary<string, bool> dict, string key, bool defaultValue = false)
        {
            if (!dict.TryGetValue(key, out bool value))
            {
                dict[key] = defaultValue;
                return defaultValue;
            }
            return value;
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            string key = property.propertyPath;
            bool mainFoldout = GetFoldout(_foldoutStates, key, false);
            
            if (!mainFoldout)
                return EditorGUIUtility.singleLineHeight + Spacing;
            
            float height = EditorGUIUtility.singleLineHeight + Spacing; // Main foldout
            height += GetVFXReferenceHeight(property.FindPropertyRelative("vfx")); // VFX Reference
            
            // Spawn point section
            bool spawnFoldout = GetFoldout(_spawnFoldoutStates, key, false);
            height += EditorGUIUtility.singleLineHeight + Spacing;
            if (spawnFoldout)
            {
                height += (EditorGUIUtility.singleLineHeight + Spacing) * 4; // 4 spawn fields
            }
            
            // Timing section
            bool timingFoldout = GetFoldout(_timingFoldoutStates, key, false);
            height += EditorGUIUtility.singleLineHeight + Spacing;
            if (timingFoldout)
            {
                height += (EditorGUIUtility.singleLineHeight + Spacing) * 4; // delay, duration, scale, attach, inherit
            }
            
            // Performance section
            bool perfFoldout = GetFoldout(_perfFoldoutStates, key, false);
            height += EditorGUIUtility.singleLineHeight + Spacing;
            if (perfFoldout)
            {
                height += (EditorGUIUtility.singleLineHeight + Spacing) * 3;
            }
            
            return height;
        }
        
        private float GetVFXReferenceHeight(SerializedProperty vfxProp)
        {
            // Get the VFXReference property drawer height
            return EditorGUI.GetPropertyHeight(vfxProp, true);
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.style.marginTop = 2;
            root.style.marginBottom = 2;

            var mainFoldout = new Foldout
            {
                text = property.displayName,
                value = property.isExpanded
            };
            mainFoldout.RegisterValueChangedCallback(evt => property.isExpanded = evt.newValue);

            var vfxProp = property.FindPropertyRelative("vfx");
            if (vfxProp != null)
                mainFoldout.Add(new PropertyField(vfxProp, "VFX"));

            var spawnFoldout = new Foldout { text = "Spawn Point", value = false };
            AddFieldIfExists(spawnFoldout, property, "spawnPointMode", "Mode");
            AddFieldIfExists(spawnFoldout, property, "spawnOffset", "Offset");
            AddFieldIfExists(spawnFoldout, property, "boneName", "Bone Name");
            AddFieldIfExists(spawnFoldout, property, "spawnTransform", "Transform");
            mainFoldout.Add(spawnFoldout);

            var timingFoldout = new Foldout { text = "Timing & Transform", value = false };
            AddFieldIfExists(timingFoldout, property, "spawnDelay", "Spawn Delay");
            AddFieldIfExists(timingFoldout, property, "durationOverride", "Duration Override");
            AddFieldIfExists(timingFoldout, property, "scale", "Scale");
            AddFieldIfExists(timingFoldout, property, "attachToParent", "Attach to Parent");
            mainFoldout.Add(timingFoldout);

            var perfFoldout = new Foldout { text = "Performance", value = false };
            AddFieldIfExists(perfFoldout, property, "maxActiveInstances", "Max Instances");
            AddFieldIfExists(perfFoldout, property, "maxCameraDistance", "Max Camera Dist");
            AddFieldIfExists(perfFoldout, property, "lodDistance", "LOD Distance");
            mainFoldout.Add(perfFoldout);

            root.Add(mainFoldout);
            return root;
        }

        private static void AddFieldIfExists(VisualElement parent, SerializedProperty root, string propName, string label)
        {
            var prop = root.FindPropertyRelative(propName);
            if (prop == null) return;
            parent.Add(new PropertyField(prop, label));
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            string key = property.propertyPath;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float y = position.y;
            
            // Check if VFX is assigned to show status
            var vfxProp = property.FindPropertyRelative("vfx");
            var typeProp = vfxProp.FindPropertyRelative("type");
            var particleProp = vfxProp.FindPropertyRelative("particleSystemPrefab");
            var vfxGraphProp = vfxProp.FindPropertyRelative("vfxGraphAsset");
            
            bool hasVFX = (typeProp.enumValueIndex == 1 && particleProp.objectReferenceValue != null) ||
                          (typeProp.enumValueIndex == 2 && vfxGraphProp.objectReferenceValue != null);
            
            // Main foldout with status indicator
            Rect foldoutRect = new Rect(position.x, y, position.width, lineHeight);
            string statusIcon = hasVFX ? "✓ " : "";
            Color prevColor = GUI.contentColor;
            if (hasVFX)
                GUI.contentColor = new Color(0.5f, 1f, 0.5f); // Green tint
            
            bool mainFoldout = EditorGUI.Foldout(foldoutRect, GetFoldout(_foldoutStates, key), 
                new GUIContent($"{statusIcon}{label.text}", label.tooltip), true);
            _foldoutStates[key] = mainFoldout;
            
            GUI.contentColor = prevColor;
            y += lineHeight + Spacing;
            
            if (!mainFoldout)
            {
                EditorGUI.EndProperty();
                return;
            }
            
            // Indent
            EditorGUI.indentLevel++;
            
            // VFX Reference field
            Rect vfxRect = new Rect(position.x, y, position.width, GetVFXReferenceHeight(vfxProp));
            EditorGUI.PropertyField(vfxRect, vfxProp, new GUIContent("VFX", "The visual effect to spawn"));
            y += vfxRect.height + Spacing;
            
            // Spawn Point foldout
            Rect spawnFoldoutRect = new Rect(position.x, y, position.width, lineHeight);
            bool spawnFoldout = EditorGUI.Foldout(spawnFoldoutRect, GetFoldout(_spawnFoldoutStates, key), 
                "Spawn Point", true);
            _spawnFoldoutStates[key] = spawnFoldout;
            y += lineHeight + Spacing;
            
            if (spawnFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawProperty(position.x, y, position.width, property, "spawnPointMode", "Mode");
                y = DrawProperty(position.x, y, position.width, property, "spawnOffset", "Offset");
                y = DrawProperty(position.x, y, position.width, property, "boneName", "Bone Name");
                y = DrawProperty(position.x, y, position.width, property, "spawnTransform", "Transform");
                EditorGUI.indentLevel--;
            }
            
            // Timing/Transform foldout
            Rect timingFoldoutRect = new Rect(position.x, y, position.width, lineHeight);
            bool timingFoldout = EditorGUI.Foldout(timingFoldoutRect, GetFoldout(_timingFoldoutStates, key), 
                "Timing & Transform", true);
            _timingFoldoutStates[key] = timingFoldout;
            y += lineHeight + Spacing;
            
            if (timingFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawProperty(position.x, y, position.width, property, "spawnDelay", "Spawn Delay");
                y = DrawProperty(position.x, y, position.width, property, "durationOverride", "Duration Override");
                y = DrawProperty(position.x, y, position.width, property, "scale", "Scale");
                y = DrawProperty(position.x, y, position.width, property, "attachToParent", "Attach to Parent");
                EditorGUI.indentLevel--;
            }
            
            // Performance foldout
            Rect perfFoldoutRect = new Rect(position.x, y, position.width, lineHeight);
            bool perfFoldout = EditorGUI.Foldout(perfFoldoutRect, GetFoldout(_perfFoldoutStates, key), 
                "Performance", true);
            _perfFoldoutStates[key] = perfFoldout;
            y += lineHeight + Spacing;
            
            if (perfFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawProperty(position.x, y, position.width, property, "maxActiveInstances", "Max Instances");
                y = DrawProperty(position.x, y, position.width, property, "maxCameraDistance", "Max Camera Dist");
                y = DrawProperty(position.x, y, position.width, property, "lodDistance", "LOD Distance");
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
        
        private float DrawProperty(float x, float y, float width, SerializedProperty parent, string propName, string label)
        {
            var prop = parent.FindPropertyRelative(propName);
            if (prop != null)
            {
                Rect rect = new Rect(x, y, width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(rect, prop, new GUIContent(label));
                return y + EditorGUIUtility.singleLineHeight + Spacing;
            }
            return y;
        }
    }
    
    /// <summary>
    /// Custom property drawer for SkillSFXConfig that provides a compact display.
    /// </summary>
    [CustomPropertyDrawer(typeof(SkillSFXConfig))]
    public class SkillSFXConfigDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        
        private static System.Collections.Generic.Dictionary<string, bool> _foldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        private static System.Collections.Generic.Dictionary<string, bool> _volumeFoldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        private static System.Collections.Generic.Dictionary<string, bool> _spatialFoldoutStates = 
            new System.Collections.Generic.Dictionary<string, bool>();
        private static System.Collections.Generic.Dictionary<string, bool> _perfFoldoutStates =
            new System.Collections.Generic.Dictionary<string, bool>();
        
        private bool GetFoldout(System.Collections.Generic.Dictionary<string, bool> dict, string key, bool defaultValue = false)
        {
            if (!dict.TryGetValue(key, out bool value))
            {
                dict[key] = defaultValue;
                return defaultValue;
            }
            return value;
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.style.marginTop = 2;
            root.style.marginBottom = 2;

            var mainFoldout = new Foldout
            {
                text = property.displayName,
                value = property.isExpanded
            };
            mainFoldout.RegisterValueChangedCallback(evt => property.isExpanded = evt.newValue);

            AddFieldIfExists(mainFoldout, property, "clip", "Clip");

            var volumeFoldout = new Foldout { text = "Volume & Pitch", value = false };
            AddFieldIfExists(volumeFoldout, property, "volume", "Volume");
            AddFieldIfExists(volumeFoldout, property, "basePitch", "Base Pitch");
            AddFieldIfExists(volumeFoldout, property, "pitchVariation", "Pitch Variation");
            mainFoldout.Add(volumeFoldout);

            var spatialFoldout = new Foldout { text = "Spatialization", value = false };
            AddFieldIfExists(spatialFoldout, property, "spatialBlend", "Spatial Blend");
            AddFieldIfExists(spatialFoldout, property, "minDistance", "Min Distance");
            AddFieldIfExists(spatialFoldout, property, "maxDistance", "Max Distance");
            mainFoldout.Add(spatialFoldout);

            var perfFoldout = new Foldout { text = "Performance", value = false };
            AddFieldIfExists(perfFoldout, property, "maxActiveInstances", "Max Instances");
            AddFieldIfExists(perfFoldout, property, "maxCameraDistance", "Max Camera Dist");
            AddFieldIfExists(perfFoldout, property, "delay", "Delay");
            mainFoldout.Add(perfFoldout);

            root.Add(mainFoldout);
            return root;
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            string key = property.propertyPath;
            bool mainFoldout = GetFoldout(_foldoutStates, key, false);
            
            if (!mainFoldout)
                return EditorGUIUtility.singleLineHeight + Spacing;
            
            float height = EditorGUIUtility.singleLineHeight + Spacing; // Main foldout
            height += EditorGUIUtility.singleLineHeight + Spacing; // Clip field
            
            // Volume section
            bool volumeFoldout = GetFoldout(_volumeFoldoutStates, key, false);
            height += EditorGUIUtility.singleLineHeight + Spacing;
            if (volumeFoldout)
            {
                height += (EditorGUIUtility.singleLineHeight + Spacing) * 3;
            }
            
            // Spatial section
            bool spatialFoldout = GetFoldout(_spatialFoldoutStates, key, false);
            height += EditorGUIUtility.singleLineHeight + Spacing;
            if (spatialFoldout)
            {
                height += (EditorGUIUtility.singleLineHeight + Spacing) * 3;
            }

            // Performance section
            bool perfFoldout = GetFoldout(_perfFoldoutStates, key, false);
            height += EditorGUIUtility.singleLineHeight + Spacing;
            if (perfFoldout)
            {
                height += (EditorGUIUtility.singleLineHeight + Spacing) * 3;
            }
            
            return height;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            string key = property.propertyPath;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float y = position.y;
            
            // Check if clip is assigned
            var clipProp = property.FindPropertyRelative("clip");
            bool hasClip = clipProp.objectReferenceValue != null;
            
            // Main foldout with status
            Rect foldoutRect = new Rect(position.x, y, position.width, lineHeight);
            string statusIcon = hasClip ? "♪ " : "";
            Color prevColor = GUI.contentColor;
            if (hasClip)
                GUI.contentColor = new Color(0.5f, 0.8f, 1f); // Blue tint
            
            bool mainFoldout = EditorGUI.Foldout(foldoutRect, GetFoldout(_foldoutStates, key), 
                new GUIContent($"{statusIcon}{label.text}", label.tooltip), true);
            _foldoutStates[key] = mainFoldout;
            
            GUI.contentColor = prevColor;
            y += lineHeight + Spacing;
            
            if (!mainFoldout)
            {
                EditorGUI.EndProperty();
                return;
            }
            
            EditorGUI.indentLevel++;
            
            // Audio clip
            Rect clipRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.PropertyField(clipRect, clipProp, new GUIContent("Clip"));
            y += lineHeight + Spacing;
            
            // Volume foldout
            Rect volumeFoldoutRect = new Rect(position.x, y, position.width, lineHeight);
            bool volumeFoldout = EditorGUI.Foldout(volumeFoldoutRect, GetFoldout(_volumeFoldoutStates, key), 
                "Volume & Pitch", true);
            _volumeFoldoutStates[key] = volumeFoldout;
            y += lineHeight + Spacing;
            
            if (volumeFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawProperty(position.x, y, position.width, property, "volume", "Volume");
                y = DrawProperty(position.x, y, position.width, property, "basePitch", "Base Pitch");
                y = DrawProperty(position.x, y, position.width, property, "pitchVariation", "Pitch Variation");
                EditorGUI.indentLevel--;
            }
            
            // Spatial foldout
            Rect spatialFoldoutRect = new Rect(position.x, y, position.width, lineHeight);
            bool spatialFoldout = EditorGUI.Foldout(spatialFoldoutRect, GetFoldout(_spatialFoldoutStates, key), 
                "Spatialization", true);
            _spatialFoldoutStates[key] = spatialFoldout;
            y += lineHeight + Spacing;
            
            if (spatialFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawProperty(position.x, y, position.width, property, "spatialBlend", "Spatial Blend");
                y = DrawProperty(position.x, y, position.width, property, "minDistance", "Min Distance");
                y = DrawProperty(position.x, y, position.width, property, "maxDistance", "Max Distance");
                EditorGUI.indentLevel--;
            }

            // Performance foldout
            Rect perfFoldoutRect = new Rect(position.x, y, position.width, lineHeight);
            bool perfFoldout = EditorGUI.Foldout(perfFoldoutRect, GetFoldout(_perfFoldoutStates, key),
                "Performance", true);
            _perfFoldoutStates[key] = perfFoldout;
            y += lineHeight + Spacing;

            if (perfFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawProperty(position.x, y, position.width, property, "maxActiveInstances", "Max Instances");
                y = DrawProperty(position.x, y, position.width, property, "maxCameraDistance", "Max Camera Dist");
                y = DrawProperty(position.x, y, position.width, property, "delay", "Delay");
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
        
        private float DrawProperty(float x, float y, float width, SerializedProperty parent, string propName, string label)
        {
            var prop = parent.FindPropertyRelative(propName);
            if (prop != null)
            {
                Rect rect = new Rect(x, y, width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(rect, prop, new GUIContent(label));
                return y + EditorGUIUtility.singleLineHeight + Spacing;
            }
            return y;
        }

        private static void AddFieldIfExists(VisualElement parent, SerializedProperty root, string propName, string label)
        {
            var prop = root.FindPropertyRelative(propName);
            if (prop == null) return;
            parent.Add(new PropertyField(prop, label));
        }
    }
}
