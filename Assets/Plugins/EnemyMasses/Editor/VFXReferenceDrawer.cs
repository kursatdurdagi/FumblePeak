using UnityEngine;
using UnityEditor;
using EnemyMasses.VFX;

namespace EnemyMasses.Editor
{
    /// <summary>
    /// Custom property drawer for VFXReference that shows a clean interface
    /// for selecting between ParticleSystem prefabs and VFX Graph assets.
    /// ParticleSystem duration is auto-calculated from the prefab.
    /// VFX Graph duration is manually specified.
    /// </summary>
    [CustomPropertyDrawer(typeof(VFXReference))]
    public class VFXReferenceDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var typeProp = property.FindPropertyRelative("type");
            var vfxType = (VFXType)typeProp.enumValueIndex;
            
            int lines = 2; // Label + type dropdown
            
            if (vfxType == VFXType.ParticleSystem)
            {
                lines += 2; // Prefab + auto-duration display
            }
            else if (vfxType == VFXType.VFXGraph)
            {
                lines += 2; // Asset + duration
            }
            
            return (EditorGUIUtility.singleLineHeight + Spacing) * lines;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            var typeProp = property.FindPropertyRelative("type");
            var particleProp = property.FindPropertyRelative("particleSystemPrefab");
            var vfxGraphProp = property.FindPropertyRelative("vfxGraphAsset");
            var durationProp = property.FindPropertyRelative("defaultDuration");
            
            var vfxType = (VFXType)typeProp.enumValueIndex;
            
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float y = position.y;
            
            // Label with foldout style
            Rect labelRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.LabelField(labelRect, label, EditorStyles.boldLabel);
            y += lineHeight + Spacing;
            
            // Indent
            EditorGUI.indentLevel++;
            
            // Type selection
            Rect typeRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.PropertyField(typeRect, typeProp, new GUIContent("VFX Type", 
                "Choose between legacy ParticleSystem (CPU) or VFX Graph (GPU-accelerated)"));
            y += lineHeight + Spacing;
            
            // Show appropriate field based on type
            if (vfxType == VFXType.ParticleSystem)
            {
                // ParticleSystem prefab field
                Rect prefabRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(prefabRect, particleProp, new GUIContent("Prefab", 
                    "ParticleSystem prefab to spawn. Duration is auto-calculated from the particle system hierarchy."));
                if (EditorGUI.EndChangeCheck())
                {
                    // Prefab changed - duration will be recalculated at runtime
                    property.serializedObject.ApplyModifiedProperties();
                }
                y += lineHeight + Spacing;
                
                // Show auto-calculated duration (read-only)
                Rect durationRect = new Rect(position.x, y, position.width, lineHeight);
                GameObject prefab = particleProp.objectReferenceValue as GameObject;
                if (prefab != null)
                {
                    float autoDuration = VFXReference.CalculateParticleSystemDuration(prefab);
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.FloatField(durationRect, new GUIContent("Duration (Auto)", 
                        "Auto-calculated from ParticleSystem: startDelay + duration + startLifetime across all children"), autoDuration);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.FloatField(durationRect, new GUIContent("Duration (Auto)", 
                        "Assign a prefab to see auto-calculated duration"), 2f);
                    EditorGUI.EndDisabledGroup();
                }
            }
            else if (vfxType == VFXType.VFXGraph)
            {
                // VFX Graph asset field
                Rect assetRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.PropertyField(assetRect, vfxGraphProp, new GUIContent("VFX Graph Asset", 
                    "VFX Graph asset for GPU-accelerated particles. Best performance for mass battles."));
                y += lineHeight + Spacing;
                
                // Duration (editable for VFX Graph since it can't be auto-calculated)
                Rect durationRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.PropertyField(durationRect, durationProp, new GUIContent("Duration", 
                    "How long the effect lasts before the VFX instance is recycled. Set this based on your VFX Graph's expected lifetime."));
                
                // Ensure minimum duration
                if (durationProp.floatValue < 0.1f)
                {
                    durationProp.floatValue = 0.1f;
                }
            }
            
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
    }
    
    /// <summary>
    /// Helper class for drawing VFXReference fields in custom editors.
    /// </summary>
    public static class VFXReferenceEditorHelper
    {
        /// <summary>
        /// Draws a VFXReference field with standard styling.
        /// ParticleSystem duration is auto-calculated, VFX Graph duration is editable.
        /// </summary>
        public static void DrawVFXReference(VFXReference vfxRef, string label, string tooltip = "")
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), EditorStyles.boldLabel);
            
            EditorGUI.indentLevel++;
            
            // Type selection
            var newType = (VFXType)EditorGUILayout.EnumPopup(
                new GUIContent("VFX Type", "ParticleSystem (CPU) or VFX Graph (GPU)"),
                vfxRef.type);
            
            if (newType != vfxRef.type)
            {
                vfxRef.type = newType;
                vfxRef.InvalidateDurationCache();
            }
            
            switch (vfxRef.type)
            {
                case VFXType.ParticleSystem:
                    EditorGUI.BeginChangeCheck();
                    vfxRef.particleSystemPrefab = (GameObject)EditorGUILayout.ObjectField(
                        new GUIContent("Prefab", "ParticleSystem prefab. Duration is auto-calculated."),
                        vfxRef.particleSystemPrefab,
                        typeof(GameObject),
                        false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        vfxRef.InvalidateDurationCache();
                    }
                    
                    // Show auto-calculated duration (read-only)
                    if (vfxRef.particleSystemPrefab != null)
                    {
                        float autoDuration = VFXReference.CalculateParticleSystemDuration(vfxRef.particleSystemPrefab);
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.FloatField(
                            new GUIContent("Duration (Auto)", "Auto-calculated from ParticleSystem hierarchy"),
                            autoDuration);
                        EditorGUI.EndDisabledGroup();
                    }
                    break;
                    
                case VFXType.VFXGraph:
                    vfxRef.vfxGraphAsset = (UnityEngine.VFX.VisualEffectAsset)EditorGUILayout.ObjectField(
                        new GUIContent("VFX Graph Asset", "VFX Graph asset (GPU-accelerated)"),
                        vfxRef.vfxGraphAsset,
                        typeof(UnityEngine.VFX.VisualEffectAsset),
                        false);
                    
                    // Duration is editable for VFX Graph
                    vfxRef.defaultDuration = EditorGUILayout.FloatField(
                        new GUIContent("Duration", "Effect duration before recycling (VFX Graph only)"),
                        vfxRef.defaultDuration);
                    vfxRef.defaultDuration = Mathf.Max(0.1f, vfxRef.defaultDuration);
                    break;
            }
            
            // Status indicator
            if (vfxRef.type == VFXType.VFXGraph && vfxRef.vfxGraphAsset != null)
            {
                EditorGUILayout.HelpBox("[OK] GPU-accelerated (recommended)", MessageType.None);
            }
            else if (vfxRef.type == VFXType.ParticleSystem && vfxRef.particleSystemPrefab != null)
            {
                EditorGUILayout.HelpBox("CPU-based. VFX Graph recommended for high-frequency effects.", MessageType.Info);
            }
            
            EditorGUI.indentLevel--;
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draws a compact VFXReference field for use in lists/arrays.
        /// </summary>
        public static void DrawCompactVFXReference(VFXReference vfxRef, string label)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            
            var newType = (VFXType)EditorGUILayout.EnumPopup(vfxRef.type, GUILayout.Width(100));
            if (newType != vfxRef.type)
            {
                vfxRef.type = newType;
                vfxRef.InvalidateDurationCache();
            }
            
            switch (vfxRef.type)
            {
                case VFXType.ParticleSystem:
                    EditorGUI.BeginChangeCheck();
                    vfxRef.particleSystemPrefab = (GameObject)EditorGUILayout.ObjectField(
                        vfxRef.particleSystemPrefab, typeof(GameObject), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        vfxRef.InvalidateDurationCache();
                    }
                    break;
                    
                case VFXType.VFXGraph:
                    vfxRef.vfxGraphAsset = (UnityEngine.VFX.VisualEffectAsset)EditorGUILayout.ObjectField(
                        vfxRef.vfxGraphAsset, typeof(UnityEngine.VFX.VisualEffectAsset), false);
                    break;
                    
                default:
                    EditorGUILayout.LabelField("(None)", EditorStyles.miniLabel);
                    break;
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
