#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.UI;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for RTSSelectionCommandPanel.
    /// Organises fields into foldout groups and draws input bindings
    /// with full support for the New Input System binding picker.
    /// </summary>
    [CustomEditor(typeof(RTSSelectionCommandPanel))]
    public class RTSSelectionCommandPanelEditor : UnityEditor.Editor
    {
        // References
        private SerializedProperty _rtsController;
        private SerializedProperty _panelRoot;

        // UI
        private SerializedProperty _strictModeToggle;
        private SerializedProperty _offensiveStrictButton;
        private SerializedProperty _defensiveStrictButton;
        private SerializedProperty _holdStrictButton;
        private SerializedProperty _passiveButton;
        private SerializedProperty _stopButton;
        private SerializedProperty _followButton;

        // Visuals
        private SerializedProperty _buttonNormalColor;
        private SerializedProperty _buttonHoverColor;
        private SerializedProperty _buttonPressedColor;
        private SerializedProperty _buttonSelectedColor;
        private SerializedProperty _buttonDisabledColor;

        // Audio
        private SerializedProperty _offensiveClickSfx;
        private SerializedProperty _defensiveClickSfx;
        private SerializedProperty _holdClickSfx;
        private SerializedProperty _passiveClickSfx;
        private SerializedProperty _stopClickSfx;
        private SerializedProperty _followClickSfx;
        private SerializedProperty _followEnterSfx;
        private SerializedProperty _commandAudioSource;
        private SerializedProperty _commandSfxPitchVariation;
        private SerializedProperty _strictOnSfx;
        private SerializedProperty _strictOffSfx;

        // Behavior
        private SerializedProperty _autoHideWhenNoPlayerSelection;
        private SerializedProperty _stopResetsToOffensive;

        // Input Shortcuts
        private SerializedProperty _strictModeInput;
        private SerializedProperty _offensiveInput;
        private SerializedProperty _defensiveInput;
        private SerializedProperty _holdInput;
        private SerializedProperty _passiveInput;
        private SerializedProperty _stopInput;
        private SerializedProperty _followInput;

        // Keybind Label Appearance
        private SerializedProperty _showKeybindLabels;
        private SerializedProperty _keybindLabelColor;
        private SerializedProperty _keybindLabelFontSize;
        private SerializedProperty _keybindLabelFont;

        // Follow Cursor
        private SerializedProperty _followCursorTexture;
        private SerializedProperty _followCursorHotspot;

        // Tooltip
        private SerializedProperty _enableTooltips;
        private SerializedProperty _tooltipPanel;
        private SerializedProperty _tooltipText;
        private SerializedProperty _tooltipOffsetY;
        private SerializedProperty _strictModeTooltip;
        private SerializedProperty _offensiveTooltip;
        private SerializedProperty _defensiveTooltip;
        private SerializedProperty _holdTooltip;
        private SerializedProperty _passiveTooltip;
        private SerializedProperty _stopTooltip;
        private SerializedProperty _followTooltip;

        // Foldout states
        private static bool _showReferences = true;
        private static bool _showUI = true;
        private static bool _showVisuals = false;
        private static bool _showAudioSection = false;
        private static bool _showBehavior = true;
        private static bool _showInputSection = true;
        private static bool _showFollowSection = true;
        private static bool _showAppearance = false;
        private static bool _showTooltipSection = true;

        private void OnEnable()
        {
            if (target == null || !(target is RTSSelectionCommandPanel))
                return;

            // References
            _rtsController = serializedObject.FindProperty("rtsController");
            _panelRoot = serializedObject.FindProperty("panelRoot");

            // UI
            _strictModeToggle = serializedObject.FindProperty("strictModeToggle");
            _offensiveStrictButton = serializedObject.FindProperty("offensiveStrictButton");
            _defensiveStrictButton = serializedObject.FindProperty("defensiveStrictButton");
            _holdStrictButton = serializedObject.FindProperty("holdStrictButton");
            _passiveButton = serializedObject.FindProperty("passiveButton");
            _stopButton = serializedObject.FindProperty("stopButton");
            _followButton = serializedObject.FindProperty("followButton");

            // Visuals
            _buttonNormalColor = serializedObject.FindProperty("buttonNormalColor");
            _buttonHoverColor = serializedObject.FindProperty("buttonHoverColor");
            _buttonPressedColor = serializedObject.FindProperty("buttonPressedColor");
            _buttonSelectedColor = serializedObject.FindProperty("buttonSelectedColor");
            _buttonDisabledColor = serializedObject.FindProperty("buttonDisabledColor");

            // Audio
            _offensiveClickSfx = serializedObject.FindProperty("offensiveClickSfx");
            _defensiveClickSfx = serializedObject.FindProperty("defensiveClickSfx");
            _holdClickSfx = serializedObject.FindProperty("holdClickSfx");
            _passiveClickSfx = serializedObject.FindProperty("passiveClickSfx");
            _stopClickSfx = serializedObject.FindProperty("stopClickSfx");
            _followClickSfx = serializedObject.FindProperty("followClickSfx");
            _followEnterSfx = serializedObject.FindProperty("followEnterSfx");
            _commandAudioSource = serializedObject.FindProperty("commandAudioSource");
            _commandSfxPitchVariation = serializedObject.FindProperty("commandSfxPitchVariation");
            _strictOnSfx = serializedObject.FindProperty("strictOnSfx");
            _strictOffSfx = serializedObject.FindProperty("strictOffSfx");

            // Behavior
            _autoHideWhenNoPlayerSelection = serializedObject.FindProperty("autoHideWhenNoPlayerSelection");
            _stopResetsToOffensive = serializedObject.FindProperty("stopResetsToOffensive");

            // Input Shortcuts
            _strictModeInput = serializedObject.FindProperty("strictModeInput");
            _offensiveInput = serializedObject.FindProperty("offensiveInput");
            _defensiveInput = serializedObject.FindProperty("defensiveInput");
            _holdInput = serializedObject.FindProperty("holdInput");
            _passiveInput = serializedObject.FindProperty("passiveInput");
            _stopInput = serializedObject.FindProperty("stopInput");
            _followInput = serializedObject.FindProperty("followInput");

            // Follow Cursor
            _followCursorTexture = serializedObject.FindProperty("followCursorTexture");
            _followCursorHotspot = serializedObject.FindProperty("followCursorHotspot");

            // Tooltip
            _enableTooltips = serializedObject.FindProperty("enableTooltips");
            _tooltipPanel = serializedObject.FindProperty("tooltipPanel");
            _tooltipText = serializedObject.FindProperty("tooltipText");
            _tooltipOffsetY = serializedObject.FindProperty("tooltipOffsetY");
            _strictModeTooltip = serializedObject.FindProperty("strictModeTooltip");
            _offensiveTooltip = serializedObject.FindProperty("offensiveTooltip");
            _defensiveTooltip = serializedObject.FindProperty("defensiveTooltip");
            _holdTooltip = serializedObject.FindProperty("holdTooltip");
            _passiveTooltip = serializedObject.FindProperty("passiveTooltip");
            _stopTooltip = serializedObject.FindProperty("stopTooltip");
            _followTooltip = serializedObject.FindProperty("followTooltip");

            // Keybind Label Appearance
            _showKeybindLabels = serializedObject.FindProperty("showKeybindLabels");
            _keybindLabelColor = serializedObject.FindProperty("keybindLabelColor");
            _keybindLabelFontSize = serializedObject.FindProperty("keybindLabelFontSize");
#if TMP_PRESENT
            _keybindLabelFont = serializedObject.FindProperty("keybindLabelFontTMP");
#else
            _keybindLabelFont = serializedObject.FindProperty("keybindLabelFont");
#endif
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();

            EditorGUILayout.Space(5);

            // ── References ──
            _showReferences = EditorGUILayout.BeginFoldoutHeaderGroup(_showReferences, "References");
            if (_showReferences)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_rtsController,
                    new GUIContent("RTS Controller", "RTS Controller to subscribe to for selection and to send commands through."));
                EditorGUILayout.PropertyField(_panelRoot,
                    new GUIContent("Panel Root", "Root GameObject that contains the toggle and buttons."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── UI ──
            _showUI = EditorGUILayout.BeginFoldoutHeaderGroup(_showUI, "UI Elements");
            if (_showUI)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_strictModeToggle,
                    new GUIContent("Strict Mode Toggle", "Toggle strict stance mode. ON = strict (won't help allies). OFF = non-strict (can help allies)."));
                EditorGUILayout.PropertyField(_offensiveStrictButton,
                    new GUIContent("Offensive Button", "Button that sets Offensive Strict on the current selection."));
                EditorGUILayout.PropertyField(_defensiveStrictButton,
                    new GUIContent("Defensive Button", "Button that sets Defensive Strict on the current selection."));
                EditorGUILayout.PropertyField(_holdStrictButton,
                    new GUIContent("Hold Button", "Button that sets Hold Position Strict on the current selection."));
                EditorGUILayout.PropertyField(_passiveButton,
                    new GUIContent("Passive Button", "Button that sets Passive on the current selection."));
                EditorGUILayout.PropertyField(_stopButton,
                    new GUIContent("Stop Button", "Button that issues Stop for the current selection."));
                EditorGUILayout.PropertyField(_followButton,
                    new GUIContent("Follow Button", "Button that enters Follow targeting mode. Click a valid target to have selected agents follow it."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Visuals ──
            _showVisuals = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisuals, "Visuals");
            if (_showVisuals)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_buttonNormalColor,
                    new GUIContent("Normal Color", "Normal tint color for command buttons."));
                EditorGUILayout.PropertyField(_buttonHoverColor,
                    new GUIContent("Hover Color", "Hover tint color for command buttons."));
                EditorGUILayout.PropertyField(_buttonPressedColor,
                    new GUIContent("Pressed Color", "Pressed tint color for command buttons."));
                EditorGUILayout.PropertyField(_buttonSelectedColor,
                    new GUIContent("Selected Color", "Selected tint color to show the active stance."));
                EditorGUILayout.PropertyField(_buttonDisabledColor,
                    new GUIContent("Disabled Color", "Disabled tint color for command buttons."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Audio ──
            _showAudioSection = EditorGUILayout.BeginFoldoutHeaderGroup(_showAudioSection, "Audio");
            if (_showAudioSection)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Stance SFX", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_offensiveClickSfx,
                    new GUIContent("Offensive SFX", "Audio clip to play when Offensive is clicked."));
                EditorGUILayout.PropertyField(_defensiveClickSfx,
                    new GUIContent("Defensive SFX", "Audio clip to play when Defensive is clicked."));
                EditorGUILayout.PropertyField(_holdClickSfx,
                    new GUIContent("Hold SFX", "Audio clip to play when Hold Position is clicked."));
                EditorGUILayout.PropertyField(_passiveClickSfx,
                    new GUIContent("Passive SFX", "Audio clip to play when Passive is clicked."));
                EditorGUILayout.PropertyField(_stopClickSfx,
                    new GUIContent("Stop SFX", "Audio clip to play when Stop is clicked."));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Follow SFX", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_followClickSfx,
                    new GUIContent("Follow Confirm SFX", "Audio clip to play when a follow target is confirmed."));
                EditorGUILayout.PropertyField(_followEnterSfx,
                    new GUIContent("Follow Enter SFX", "Audio clip to play when follow targeting mode is entered."));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Strict Mode SFX", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_strictOnSfx,
                    new GUIContent("Strict ON SFX", "Audio clip to play when Strict Mode is enabled."));
                EditorGUILayout.PropertyField(_strictOffSfx,
                    new GUIContent("Strict OFF SFX", "Audio clip to play when Strict Mode is disabled."));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Playback", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_commandAudioSource,
                    new GUIContent("Audio Source", "Optional audio source for SFX. If null, a temporary one-shot is created."));
                EditorGUILayout.PropertyField(_commandSfxPitchVariation,
                    new GUIContent("Pitch Variation", "Random pitch variation applied to SFX (± value around 1.0)."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Behavior ──
            _showBehavior = EditorGUILayout.BeginFoldoutHeaderGroup(_showBehavior, "Behavior");
            if (_showBehavior)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_autoHideWhenNoPlayerSelection,
                    new GUIContent("Auto-Hide", "Hide the panel when no player-controlled agents are selected."));
                EditorGUILayout.PropertyField(_stopResetsToOffensive,
                    new GUIContent("Stop Resets To Offensive", "If enabled, Stop will also reset selected agents to default Offensive stance."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Input Shortcuts ──
            _showInputSection = EditorGUILayout.BeginFoldoutHeaderGroup(_showInputSection, "Input Shortcuts");
            if (_showInputSection)
            {
                EditorGUI.indentLevel++;

#if ENABLE_INPUT_SYSTEM
                EditorGUILayout.HelpBox(
                    "Optional keyboard shortcuts for each command.\n" +
                    "When assigned, the key letter will be displayed on the button as an overlay label.",
                    MessageType.Info);
#else
                EditorGUILayout.HelpBox(
                    "Optional key bindings for each command (Legacy Input).\n" +
                    "When assigned, the key name will be displayed on the button.",
                    MessageType.Info);
#endif

                EditorGUILayout.Space(3);
                DrawCommandInputBinding(_strictModeInput, "Strict Toggle");
                DrawCommandInputBinding(_offensiveInput, "Offensive");
                DrawCommandInputBinding(_defensiveInput, "Defensive");
                DrawCommandInputBinding(_holdInput, "Hold");
                DrawCommandInputBinding(_passiveInput, "Passive");
                DrawCommandInputBinding(_stopInput, "Stop");
                DrawCommandInputBinding(_followInput, "Follow");

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Follow ──
            _showFollowSection = EditorGUILayout.BeginFoldoutHeaderGroup(_showFollowSection, "Follow Command");
            if (_showFollowSection)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_followCursorTexture,
                    new GUIContent("Cursor Texture", "Cursor texture shown while in Follow targeting mode. If null, cursor won't change."));
                EditorGUILayout.PropertyField(_followCursorHotspot,
                    new GUIContent("Cursor Hotspot", "Hotspot (pixel offset from top-left) for the follow cursor."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Tooltip ──
            _showTooltipSection = EditorGUILayout.BeginFoldoutHeaderGroup(_showTooltipSection, "Tooltips");
            if (_showTooltipSection)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_enableTooltips,
                    new GUIContent("Enable Tooltips", "Show hover tooltips on command buttons."));

                if (_enableTooltips.boolValue)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Tooltip UI", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(_tooltipPanel,
                        new GUIContent("Tooltip Panel", "RectTransform of the tooltip panel. Hidden when no button is hovered."));
                    EditorGUILayout.PropertyField(_tooltipText,
                        new GUIContent("Tooltip Text", "Text component inside the tooltip panel."));
                    EditorGUILayout.PropertyField(_tooltipOffsetY,
                        new GUIContent("Offset Y", "Vertical offset (pixels) above the hovered button."));

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Button Tooltips", EditorStyles.miniBoldLabel);
                    EditorGUILayout.HelpBox(
                        "Each entry has a fallback text and an optional Unity Localization reference.\n" +
                        "When localization is enabled and the entry key resolves, the localized text is used.",
                        MessageType.Info);

                    EditorGUILayout.Space(3);
                    DrawTooltipEntry(_strictModeTooltip, "Strict Mode");
                    DrawTooltipEntry(_offensiveTooltip, "Offensive");
                    DrawTooltipEntry(_defensiveTooltip, "Defensive");
                    DrawTooltipEntry(_holdTooltip, "Hold Position");
                    DrawTooltipEntry(_passiveTooltip, "Passive");
                    DrawTooltipEntry(_stopTooltip, "Stop");
                    DrawTooltipEntry(_followTooltip, "Follow");
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // ── Appearance ──
            _showAppearance = EditorGUILayout.BeginFoldoutHeaderGroup(_showAppearance, "Appearance");
            if (_showAppearance)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Keybind Label", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showKeybindLabels,
                    new GUIContent("Show Labels", "Show keybind label overlays on the command buttons."));

                if (_showKeybindLabels.boolValue)
                {
                    EditorGUILayout.PropertyField(_keybindLabelColor,
                        new GUIContent("Label Color", "Color for the keybind label text on each command button."));
                    EditorGUILayout.PropertyField(_keybindLabelFontSize,
                        new GUIContent("Label Font Size", "Font size for the keybind labels."));
                    EditorGUILayout.PropertyField(_keybindLabelFont,
#if TMP_PRESENT
                        new GUIContent("Label Font (TMP)", "TMP font asset for keybind labels. If null, the default TMP font is used."));
#else
                        new GUIContent("Label Font", "Legacy font for keybind labels. If null, the built-in font is used."));
#endif
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInspectorHeader()
        {
            EditorGUILayout.Space(2);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("RTS Command Panel", headerStyle);
            EditorGUILayout.Space(2);

            var infoStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 10,
                richText = true
            };
            EditorGUILayout.LabelField(
                "Toggles a command UI panel based on RTS selection of player-controlled agents.\n" +
                "Forwards button presses to strict combat stance/stop commands.",
                infoStyle);
        }

        private void DrawCommandInputBinding(SerializedProperty bindingProp, string displayLabel)
        {
            if (bindingProp == null) return;

            var labelProp = bindingProp.FindPropertyRelative("label");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(displayLabel, EditorStyles.miniBoldLabel);

            EditorGUI.indentLevel++;

            if (labelProp != null)
            {
                EditorGUILayout.PropertyField(labelProp,
                    new GUIContent("Label", "Display label for this binding."));
            }

#if ENABLE_INPUT_SYSTEM
            var inputActionProp = bindingProp.FindPropertyRelative("inputAction");
            if (inputActionProp != null)
            {
                EditorGUILayout.PropertyField(inputActionProp,
                    new GUIContent("Input Action", "Input action for this command."));
            }
#else
            var legacyKeyProp = bindingProp.FindPropertyRelative("legacyKey");
            if (legacyKeyProp != null)
            {
                EditorGUILayout.PropertyField(legacyKeyProp,
                    new GUIContent("Key", "Key for this command."));
            }
#endif

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(2);
        }

        private void DrawTooltipEntry(SerializedProperty entryProp, string displayLabel)
        {
            if (entryProp == null) return;

            var textProp = entryProp.FindPropertyRelative("text");
            var locProp = entryProp.FindPropertyRelative("localization");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(displayLabel, EditorStyles.miniBoldLabel);

            EditorGUI.indentLevel++;

            if (textProp != null)
            {
                EditorGUILayout.PropertyField(textProp,
                    new GUIContent("Text", "Fallback tooltip text when localization is not configured or unavailable."));
            }

            if (locProp != null)
            {
                EditorGUILayout.PropertyField(locProp,
                    new GUIContent("Localization", "Optional Unity Localization reference for this tooltip."));
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(2);
        }
    }
}
#endif
