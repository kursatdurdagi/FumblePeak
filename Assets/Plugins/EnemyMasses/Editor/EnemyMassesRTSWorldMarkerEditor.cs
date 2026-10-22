using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesRTSWorldMarker))]
    public class EnemyMassesRTSWorldMarkerEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesRTSWorldMarker _marker;

        private SerializedProperty _friendlyVisibility;
        private SerializedProperty _enemyVisibility;
        private SerializedProperty _neutralVisibility;
        private SerializedProperty _revealFogForFriendly;
        private SerializedProperty _revealFogForEnemy;
        private SerializedProperty _revealFogForNeutral;
        private SerializedProperty _fogProviderOverride;
        private SerializedProperty _fogVisibilityThreshold;

        private void OnEnable()
        {
            if (target == null) return;

            _marker = target as Runtime.EnemyMassesRTSWorldMarker;
            if (_marker == null) return;

            _friendlyVisibility = serializedObject.FindProperty("m_FriendlyVisibility");
            _enemyVisibility = serializedObject.FindProperty("m_EnemyVisibility");
            _neutralVisibility = serializedObject.FindProperty("m_NeutralVisibility");
            _revealFogForFriendly = serializedObject.FindProperty("m_RevealFogForFriendly");
            _revealFogForEnemy = serializedObject.FindProperty("m_RevealFogForEnemy");
            _revealFogForNeutral = serializedObject.FindProperty("m_RevealFogForNeutral");
            _fogProviderOverride = serializedObject.FindProperty("m_FogProviderOverride");
            _fogVisibilityThreshold = serializedObject.FindProperty("m_FogVisibilityThreshold");
        }

        public override void OnInspectorGUI()
        {
            if (_marker == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeaderSection();
            EditorGUILayout.Space(10);

            DrawVisibilityRules();
            EditorGUILayout.Space(5);

            DrawFogRules();
            EditorGUILayout.Space(5);

            DrawFogVisibility();
            EditorGUILayout.Space(5);

            DrawUtilities();
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

            GUI.color = new Color(0.2f, 0.8f, 1f, 1f);
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Enemy Masses World Marker (Rules)", titleStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawVisibilityRules()
        {
            EditorGUILayout.LabelField("Minimap Visibility", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_friendlyVisibility, new GUIContent("Player Controlled"));
            EditorGUILayout.PropertyField(_enemyVisibility, new GUIContent("Enemy"));
            EditorGUILayout.PropertyField(_neutralVisibility, new GUIContent("Neutral"));

            EditorGUILayout.EndVertical();
        }

        private void DrawFogRules()
        {
            EditorGUILayout.LabelField("Fog Reveal Rules", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_revealFogForFriendly, new GUIContent("Player Controlled"));
            EditorGUILayout.PropertyField(_revealFogForEnemy, new GUIContent("Enemy"));
            EditorGUILayout.PropertyField(_revealFogForNeutral, new GUIContent("Neutral"));

            EditorGUILayout.EndVertical();
        }

        private void DrawFogVisibility()
        {
            EditorGUILayout.LabelField("Fog Visibility", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_fogProviderOverride, new GUIContent("Fog Provider Override"));
            EditorGUILayout.PropertyField(_fogVisibilityThreshold, new GUIContent("Visibility Threshold"));

            if (_fogProviderOverride.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("If empty, a fog provider is auto-discovered at runtime.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUtilities()
        {
            EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("Refresh Anchor Cache", GUILayout.Height(22)))
            {
                Runtime.EnemyMassesRTSWorldAnchor.NotifyRulesProviderAdded(_marker);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
