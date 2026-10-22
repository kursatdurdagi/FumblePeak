using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Debug component that visualizes virtual hitbox zones in the Scene view.
    /// Can be added to individual agents or used by the combat system to draw gizmos for all agents.
    /// </summary>
    [ExecuteInEditMode]
    public class VirtualHitboxGizmoVisualizer : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("The hitbox configuration to visualize")]
        public VirtualHitboxConfig hitboxConfig;
        
        [Tooltip("Override color for all zones (leave white to use zone-specific colors)")]
        public Color overrideColor = Color.white;
        
        [Tooltip("Draw wireframe instead of solid")]
        public bool wireframeMode = false;
        
        [Tooltip("Show zone labels in Scene view")]
        public bool showLabels = true;
        
        [Tooltip("Always show gizmos, not just when selected")]
        public bool alwaysShow = false;
        
        private void OnDrawGizmos()
        {
            if (alwaysShow && hitboxConfig != null && hitboxConfig.showDebugGizmos)
            {
                DrawHitboxGizmos();
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!alwaysShow && hitboxConfig != null && hitboxConfig.showDebugGizmos)
            {
                DrawHitboxGizmos();
            }
        }
        
        private void DrawHitboxGizmos()
        {
            if (hitboxConfig == null || hitboxConfig.hitZones == null)
                return;
            
            Vector3 basePosition = transform.position;
            
            foreach (var zone in hitboxConfig.hitZones)
            {
                if (zone == null) continue;
                
                // Calculate zone center
                float centerHeight = (zone.minHeight + zone.maxHeight) / 2f;
                float zoneHeight = zone.maxHeight - zone.minHeight;
                Vector3 zoneCenter = basePosition + Vector3.up * centerHeight;
                
                // Determine color
                Color zoneColor = (overrideColor != Color.white) ? overrideColor : zone.debugColor;
                zoneColor.a = hitboxConfig.debugGizmoAlpha;
                
                // Draw the zone as a capsule/cylinder
                if (wireframeMode)
                {
                    Gizmos.color = zoneColor;
                    DrawWireCapsule(zoneCenter, zone.radius, zoneHeight);
                }
                else
                {
                    Gizmos.color = zoneColor;
                    DrawSolidCapsule(zoneCenter, zone.radius, zoneHeight);
                }
                
                // Draw label
#if UNITY_EDITOR
                if (showLabels)
                {
                    GUIStyle style = new GUIStyle();
                    style.normal.textColor = zone.isCriticalZone ? Color.red : Color.white;
                    style.fontStyle = zone.isCriticalZone ? FontStyle.Bold : FontStyle.Normal;
                    style.alignment = TextAnchor.MiddleCenter;
                    
                    string label = zone.zoneName;
                    if (zone.isCriticalZone)
                        label += " (CRIT)";
                    label += $"\n{zone.damageMultiplier:F1}x";
                    
                    Handles.Label(zoneCenter + Vector3.right * (zone.radius + 0.2f), label, style);
                }
#endif
            }
            
            // Draw height reference line
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(basePosition, basePosition + Vector3.up * hitboxConfig.agentHeight);
        }
        
        private void DrawWireCapsule(Vector3 center, float radius, float height)
        {
            float cylinderHeight = Mathf.Max(0f, height - radius * 2f);
            Vector3 topSphereCenter = center + Vector3.up * (cylinderHeight / 2f);
            Vector3 bottomSphereCenter = center - Vector3.up * (cylinderHeight / 2f);
            
            // Draw spheres at top and bottom
            Gizmos.DrawWireSphere(topSphereCenter, radius);
            Gizmos.DrawWireSphere(bottomSphereCenter, radius);
            
            // Draw connecting lines
            Vector3 forward = Vector3.forward * radius;
            Vector3 right = Vector3.right * radius;
            
            Gizmos.DrawLine(topSphereCenter + forward, bottomSphereCenter + forward);
            Gizmos.DrawLine(topSphereCenter - forward, bottomSphereCenter - forward);
            Gizmos.DrawLine(topSphereCenter + right, bottomSphereCenter + right);
            Gizmos.DrawLine(topSphereCenter - right, bottomSphereCenter - right);
        }
        
        private void DrawSolidCapsule(Vector3 center, float radius, float height)
        {
            // Unity doesn't have built-in solid capsule gizmo, so we approximate with spheres
            float cylinderHeight = Mathf.Max(0f, height - radius * 2f);
            int segments = Mathf.Max(3, Mathf.CeilToInt(cylinderHeight / (radius * 0.5f)));
            
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float yOffset = Mathf.Lerp(-cylinderHeight / 2f, cylinderHeight / 2f, t);
                Vector3 spherePos = center + Vector3.up * yOffset;
                Gizmos.DrawSphere(spherePos, radius);
            }
        }
        
        /// <summary>
        /// Static method to draw hitbox gizmos for any agent from an external system.
        /// </summary>
        public static void DrawAgentHitbox(Vector3 agentPosition, VirtualHitboxConfig config, float alpha = 0.3f, bool wireframe = false)
        {
            if (config == null || config.hitZones == null || !config.showDebugGizmos)
                return;
            
            foreach (var zone in config.hitZones)
            {
                if (zone == null) continue;
                
                float centerHeight = (zone.minHeight + zone.maxHeight) / 2f;
                float zoneHeight = zone.maxHeight - zone.minHeight;
                Vector3 zoneCenter = agentPosition + Vector3.up * centerHeight;
                
                Color zoneColor = zone.debugColor;
                zoneColor.a = alpha;
                Gizmos.color = zoneColor;
                
                if (wireframe)
                {
                    DrawWireCapsuleStatic(zoneCenter, zone.radius, zoneHeight);
                }
                else
                {
                    DrawSolidCapsuleStatic(zoneCenter, zone.radius, zoneHeight);
                }
            }
        }
        
        private static void DrawWireCapsuleStatic(Vector3 center, float radius, float height)
        {
            float cylinderHeight = Mathf.Max(0f, height - radius * 2f);
            Vector3 topSphereCenter = center + Vector3.up * (cylinderHeight / 2f);
            Vector3 bottomSphereCenter = center - Vector3.up * (cylinderHeight / 2f);
            
            Gizmos.DrawWireSphere(topSphereCenter, radius);
            Gizmos.DrawWireSphere(bottomSphereCenter, radius);
            
            Vector3 forward = Vector3.forward * radius;
            Vector3 right = Vector3.right * radius;
            
            Gizmos.DrawLine(topSphereCenter + forward, bottomSphereCenter + forward);
            Gizmos.DrawLine(topSphereCenter - forward, bottomSphereCenter - forward);
            Gizmos.DrawLine(topSphereCenter + right, bottomSphereCenter + right);
            Gizmos.DrawLine(topSphereCenter - right, bottomSphereCenter - right);
        }
        
        private static void DrawSolidCapsuleStatic(Vector3 center, float radius, float height)
        {
            float cylinderHeight = Mathf.Max(0f, height - radius * 2f);
            int segments = Mathf.Max(3, Mathf.CeilToInt(cylinderHeight / (radius * 0.5f)));
            
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float yOffset = Mathf.Lerp(-cylinderHeight / 2f, cylinderHeight / 2f, t);
                Vector3 spherePos = center + Vector3.up * yOffset;
                Gizmos.DrawSphere(spherePos, radius);
            }
        }
    }
    
#if UNITY_EDITOR
    /// <summary>
    /// Custom editor for VirtualHitboxGizmoVisualizer with preview and setup tools.
    /// </summary>
    [CustomEditor(typeof(VirtualHitboxGizmoVisualizer))]
    public class VirtualHitboxGizmoVisualizerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            VirtualHitboxGizmoVisualizer visualizer = (VirtualHitboxGizmoVisualizer)target;
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);
            
            if (visualizer.hitboxConfig != null)
            {
                if (visualizer.hitboxConfig.hitZones == null || visualizer.hitboxConfig.hitZones.Length == 0)
                {
                    if (GUILayout.Button("Initialize Default Humanoid Zones"))
                    {
                        Undo.RecordObject(visualizer, "Initialize Hitbox Zones");
                        visualizer.hitboxConfig.InitializeDefaultZones();
                        EditorUtility.SetDirty(visualizer);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Hitbox has {visualizer.hitboxConfig.hitZones.Length} zones defined.\n" +
                        $"Enabled: {visualizer.hitboxConfig.enabled}", 
                        MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a VirtualHitboxConfig to visualize hitboxes.", MessageType.Warning);
            }
        }
    }
#endif
}
