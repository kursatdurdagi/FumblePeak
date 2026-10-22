using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesRTSWorldAnchor))]
    public class EnemyMassesRTSWorldAnchorEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesRTSWorldAnchor _anchor;

        private SerializedProperty _anchorTransform;
        private SerializedProperty _faction;
        private SerializedProperty _minimapColor;
        private SerializedProperty _fogRevealRadius;
        private SerializedProperty _rulesProvider;
        private SerializedProperty _autoFindRulesProvider;
        private SerializedProperty _positionOffset;
        private SerializedProperty _showGizmos;

        private void OnEnable()
        {
            if (target == null) return;

            _anchor = target as Runtime.EnemyMassesRTSWorldAnchor;
            if (_anchor == null) return;

            _anchorTransform = serializedObject.FindProperty("m_Anchor");
            _faction = serializedObject.FindProperty("m_Faction");
            _minimapColor = serializedObject.FindProperty("m_MinimapColor");
            _fogRevealRadius = serializedObject.FindProperty("m_FogRevealRadius");
            _rulesProvider = serializedObject.FindProperty("m_RulesProvider");
            _autoFindRulesProvider = serializedObject.FindProperty("m_AutoFindRulesProvider");
            _positionOffset = serializedObject.FindProperty("m_PositionOffset");
            _showGizmos = serializedObject.FindProperty("m_ShowGizmos");
        }

        public override void OnInspectorGUI()
        {
            if (_anchor == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeaderSection();
            EditorGUILayout.Space(10);

            DrawAnchorSettings();
            EditorGUILayout.Space(5);

            DrawFactionSettings();
            EditorGUILayout.Space(5);

            DrawMinimapSettings();
            EditorGUILayout.Space(5);

            DrawFogSettings();
            EditorGUILayout.Space(5);

            DrawRulesSettings();
            EditorGUILayout.Space(5);

            DrawGizmoSettings();
            EditorGUILayout.Space(10);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeaderSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            Color headerColor = _minimapColor != null ? _minimapColor.colorValue : new Color(0.2f, 1f, 0.2f, 1f);
            if (headerColor.a <= 0f) headerColor.a = 1f;

            GUI.color = headerColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Enemy Masses World Anchor", titleStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawAnchorSettings()
        {
            EditorGUILayout.LabelField("Anchor Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_anchorTransform, new GUIContent("Anchor Transform"));
            EditorGUILayout.PropertyField(_positionOffset);

            EditorGUILayout.EndVertical();
        }

        private void DrawFactionSettings()
        {
            EditorGUILayout.LabelField("Faction", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_faction);

            EditorGUILayout.EndVertical();
        }

        private void DrawMinimapSettings()
        {
            EditorGUILayout.LabelField("Minimap", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_minimapColor);

            EditorGUILayout.EndVertical();
        }

        private void DrawFogSettings()
        {
            EditorGUILayout.LabelField("Fog of War", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_fogRevealRadius, new GUIContent("Reveal Radius"));
            if (_fogRevealRadius.floatValue <= 0f)
            {
                EditorGUILayout.HelpBox("Radius <= 0 disables fog reveal for this anchor.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRulesSettings()
        {
            EditorGUILayout.LabelField("Rules Provider", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_rulesProvider);
            EditorGUILayout.PropertyField(_autoFindRulesProvider, new GUIContent("Auto Find"));

            if (_rulesProvider.objectReferenceValue == null)
            {
                if (_autoFindRulesProvider.boolValue)
                {
                    EditorGUILayout.HelpBox("Will search parents and root children for EnemyMassesRTSWorldMarker.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("No rules provider assigned. Defaults to show on minimap and reveal only for PlayerControlled.", MessageType.Warning);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Rules Provider", GUILayout.Height(20)))
            {
                _anchor.RefreshRulesProvider();
                EditorUtility.SetDirty(_anchor);
            }
            if (_rulesProvider.objectReferenceValue != null && GUILayout.Button("Select Rules Provider", GUILayout.Height(20)))
            {
                Selection.activeObject = _rulesProvider.objectReferenceValue;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawGizmoSettings()
        {
            EditorGUILayout.LabelField("Gizmos", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showGizmos, new GUIContent("Show Gizmos"));

            EditorGUILayout.EndVertical();
        }
    }
}
