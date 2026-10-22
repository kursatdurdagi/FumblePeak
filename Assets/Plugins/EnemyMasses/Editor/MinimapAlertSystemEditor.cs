using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Editor
{
    [CustomEditor(typeof(MinimapAlertSystem))]
    public class MinimapAlertSystemEditor : UnityEditor.Editor
    {
        private SerializedProperty _crowdController;
        private SerializedProperty _rtsController;
        private SerializedProperty _alertDuration;
        private SerializedProperty _flashFrequency;
        private SerializedProperty _alertColor;
        private SerializedProperty _markerSizeMultiplier;
        private SerializedProperty _flashViewIndicator;
        private SerializedProperty _viewIndicatorAlertColor;
        private SerializedProperty _alertSound;
        private SerializedProperty _alertVolume;
        private SerializedProperty _alertSoundCooldown;
        private SerializedProperty _onlyAlertForPlayerUnits;

        private void OnEnable()
        {
            _crowdController = serializedObject.FindProperty("crowdController");
            _rtsController = serializedObject.FindProperty("rtsController");
            _alertDuration = serializedObject.FindProperty("alertDuration");
            _flashFrequency = serializedObject.FindProperty("flashFrequency");
            _alertColor = serializedObject.FindProperty("alertColor");
            _markerSizeMultiplier = serializedObject.FindProperty("markerSizeMultiplier");
            _flashViewIndicator = serializedObject.FindProperty("flashViewIndicator");
            _viewIndicatorAlertColor = serializedObject.FindProperty("viewIndicatorAlertColor");
            _alertSound = serializedObject.FindProperty("alertSound");
            _alertVolume = serializedObject.FindProperty("alertVolume");
            _alertSoundCooldown = serializedObject.FindProperty("alertSoundCooldown");
            _onlyAlertForPlayerUnits = serializedObject.FindProperty("onlyAlertForPlayerUnits");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Minimap Alert System", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "AoE2-style under-attack alerts. When player units take damage, their minimap markers " +
                "flash and an alarm sound plays. Works with RTSMinimapUI + EnemyMassesMinimapDataProvider.",
                MessageType.None);

            EditorGUILayout.Space(10);

            // References
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_crowdController,
                new GUIContent("Crowd Controller", "Auto-discovered if null."));
            EditorGUILayout.PropertyField(_rtsController,
                new GUIContent("RTS Controller", "Used to determine player factions. Auto-discovered if null."));

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Flash Settings
            EditorGUILayout.LabelField("Flash Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_alertDuration,
                new GUIContent("Alert Duration (s)", "How long markers flash after being hit."));
            EditorGUILayout.PropertyField(_flashFrequency,
                new GUIContent("Flash Frequency (Hz)", "Full flash cycles per second. Higher = faster blinking."));
            EditorGUILayout.PropertyField(_alertColor,
                new GUIContent("Marker Alert Color", "Color to flash minimap markers when under attack."));
            EditorGUILayout.PropertyField(_markerSizeMultiplier,
                new GUIContent("Marker Size Multiplier", "Scale up minimap markers by this factor when flashing (1 = no change)."));

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // View Indicator
            EditorGUILayout.LabelField("View Indicator", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_flashViewIndicator,
                new GUIContent("Flash View Indicator", "Also flash the minimap view indicator border when units are under attack."));

            if (_flashViewIndicator.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_viewIndicatorAlertColor,
                    new GUIContent("Alert Color", "Flash color for the view indicator border."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Audio
            EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_alertSound,
                new GUIContent("Alert Sound", "Audio clip played when player units come under attack."));

            if (_alertSound.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_alertVolume,
                    new GUIContent("Volume", "Alert sound volume."));
                EditorGUILayout.PropertyField(_alertSoundCooldown,
                    new GUIContent("Cooldown (s)", "Minimum time between alert sounds to prevent spam."));
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Assign an AudioClip to play an alarm sound when units are attacked (like AoE2).",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Filter
            EditorGUILayout.LabelField("Filter", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_onlyAlertForPlayerUnits,
                new GUIContent("Player Units Only", "Only alert when player-controlled units are attacked."));

            EditorGUILayout.EndVertical();

            // Runtime info
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var system = (MinimapAlertSystem)target;
                EditorGUILayout.LabelField("Active Alerts", system.ActiveAlertCount.ToString());
                EditorGUILayout.LabelField("Has Active", system.HasActiveAlerts ? "Yes" : "No");

                EditorGUILayout.EndVertical();
                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
