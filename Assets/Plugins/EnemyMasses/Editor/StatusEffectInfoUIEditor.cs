using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Editor
{
    [CustomEditor(typeof(UI.StatusEffectInfoUI))]
    public class StatusEffectInfoUIEditor : UnityEditor.Editor
    {
        private UI.StatusEffectInfoUI _target;

        // References
        private SerializedProperty _rtsController;
        private SerializedProperty _crowdController;
        private SerializedProperty _registry;

        // Layout
        private SerializedProperty _barRoot;
        private SerializedProperty _slotContainer;
        private SerializedProperty _barBackground;
        private SerializedProperty _slotSize;
        private SerializedProperty _slotSpacing;
        private SerializedProperty _maxSlots;

        // Display
        private SerializedProperty _showDurationText;
        private SerializedProperty _showStackCount;
        private SerializedProperty _colorizeIcons;
        private SerializedProperty _autoHideWhenEmpty;

        // Filters
        private SerializedProperty _showBuffs;
        private SerializedProperty _showDebuffs;
        private SerializedProperty _showDamageOverTime;
        private SerializedProperty _showCrowdControl;

        // Tooltip
        private SerializedProperty _enableTooltips;
        private SerializedProperty _tooltipPanel;
        private SerializedProperty _tooltipText;

        // Update
        private SerializedProperty _updateInterval;
        private SerializedProperty _debugLog;

        // Inline overrides
        private SerializedProperty _iconOverrides;
        private SerializedProperty _fallbackBuffIcon;
        private SerializedProperty _fallbackDebuffIcon;
        private SerializedProperty _fallbackDotIcon;
        private SerializedProperty _fallbackCCIcon;

        private void OnEnable()
        {
            _target = (UI.StatusEffectInfoUI)target;

            _rtsController = serializedObject.FindProperty("rtsController");
            _crowdController = serializedObject.FindProperty("crowdController");
            _registry = serializedObject.FindProperty("registry");

            _barRoot = serializedObject.FindProperty("barRoot");
            _slotContainer = serializedObject.FindProperty("slotContainer");
            _barBackground = serializedObject.FindProperty("barBackground");
            _slotSize = serializedObject.FindProperty("slotSize");
            _slotSpacing = serializedObject.FindProperty("slotSpacing");
            _maxSlots = serializedObject.FindProperty("maxSlots");

            _showDurationText = serializedObject.FindProperty("showDurationText");
            _showStackCount = serializedObject.FindProperty("showStackCount");
            _colorizeIcons = serializedObject.FindProperty("colorizeIcons");
            _autoHideWhenEmpty = serializedObject.FindProperty("autoHideWhenEmpty");

            _showBuffs = serializedObject.FindProperty("showBuffs");
            _showDebuffs = serializedObject.FindProperty("showDebuffs");
            _showDamageOverTime = serializedObject.FindProperty("showDamageOverTime");
            _showCrowdControl = serializedObject.FindProperty("showCrowdControl");

            _enableTooltips = serializedObject.FindProperty("enableTooltips");
            _tooltipPanel = serializedObject.FindProperty("tooltipPanel");
            _tooltipText = serializedObject.FindProperty("tooltipText");

            _updateInterval = serializedObject.FindProperty("updateInterval");
            _debugLog = serializedObject.FindProperty("debugLog");

            _iconOverrides = serializedObject.FindProperty("iconOverrides");
            _fallbackBuffIcon = serializedObject.FindProperty("fallbackBuffIcon");
            _fallbackDebuffIcon = serializedObject.FindProperty("fallbackDebuffIcon");
            _fallbackDotIcon = serializedObject.FindProperty("fallbackDotIcon");
            _fallbackCCIcon = serializedObject.FindProperty("fallbackCCIcon");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawReferences();
            EditorGUILayout.Space(5);

            DrawLayout();
            EditorGUILayout.Space(5);

            DrawDisplay();
            EditorGUILayout.Space(5);

            DrawFilters();
            EditorGUILayout.Space(5);

            DrawTooltip();
            EditorGUILayout.Space(5);

            DrawUpdate();
            EditorGUILayout.Space(5);

            DrawIconOverrides();

            serializedObject.ApplyModifiedProperties();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            GUI.color = new Color(0.2f, 1f, 0.4f, 1f);
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Status Effect Info UI", title);
            EditorGUILayout.EndVertical();
        }

        private void DrawReferences()
        {
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_rtsController);
            EditorGUILayout.PropertyField(_crowdController);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_registry, new GUIContent("Status Effect Registry (SO)"));

            if (_registry.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "No registry assigned. Create one via Assets > Create > Enemy Masses > Status Effect UI Registry.\n" +
                    "Without a registry, inline icon overrides below are used instead.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLayout()
        {
            EditorGUILayout.LabelField("UI Layout", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_barRoot);
            EditorGUILayout.PropertyField(_slotContainer);
            EditorGUILayout.PropertyField(_barBackground);
            EditorGUILayout.PropertyField(_slotSize);
            EditorGUILayout.PropertyField(_slotSpacing);
            EditorGUILayout.PropertyField(_maxSlots);

            EditorGUILayout.EndVertical();
        }

        private void DrawDisplay()
        {
            EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showDurationText);
            EditorGUILayout.PropertyField(_showStackCount);
            EditorGUILayout.PropertyField(_colorizeIcons);
            EditorGUILayout.PropertyField(_autoHideWhenEmpty);

            EditorGUILayout.EndVertical();
        }

        private void DrawFilters()
        {
            EditorGUILayout.LabelField("Category Filters", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showBuffs);
            EditorGUILayout.PropertyField(_showDebuffs);
            EditorGUILayout.PropertyField(_showDamageOverTime, new GUIContent("Show DOTs"));
            EditorGUILayout.PropertyField(_showCrowdControl, new GUIContent("Show Crowd Control"));

            EditorGUILayout.EndVertical();
        }

        private void DrawTooltip()
        {
            EditorGUILayout.LabelField("Tooltip", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableTooltips);
            if (_enableTooltips.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_tooltipPanel);
                EditorGUILayout.PropertyField(_tooltipText);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUpdate()
        {
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_updateInterval, new GUIContent("Refresh Interval (s)"));
            EditorGUILayout.PropertyField(_debugLog, new GUIContent("Debug Log", "Logs gathered status effects to the console every refresh. Disable to avoid spam."));
            EditorGUILayout.EndVertical();
        }

        private void DrawIconOverrides()
        {
            bool hasRegistry = _registry.objectReferenceValue != null;
            string label = hasRegistry
                ? "Inline Icon Overrides (ignored — registry assigned)"
                : "Inline Icon Overrides";

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (hasRegistry)
            {
                EditorGUILayout.HelpBox("A StatusEffectUIRegistry is assigned. These inline overrides are ignored.", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(hasRegistry);
            EditorGUILayout.PropertyField(_fallbackBuffIcon, new GUIContent("Default Buff Icon"));
            EditorGUILayout.PropertyField(_fallbackDebuffIcon, new GUIContent("Default Debuff Icon"));
            EditorGUILayout.PropertyField(_fallbackDotIcon, new GUIContent("Default DOT Icon"));
            EditorGUILayout.PropertyField(_fallbackCCIcon, new GUIContent("Default CC Icon"));
            EditorGUILayout.PropertyField(_iconOverrides, new GUIContent("Per-Effect Overrides"), true);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }
    }
}
