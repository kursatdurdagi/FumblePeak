#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.UI;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for AgentSkillBarUI.
    /// Draws input bindings similarly to EnemyMassesRTSControllerEditor's InputAction fields,
    /// with full support for the New Input System binding picker.
    /// </summary>
    [CustomEditor(typeof(AgentSkillBarUI))]
    public class AgentSkillBarUIEditor : UnityEditor.Editor
    {
        // References
        private SerializedProperty _rtsController;
        private SerializedProperty _crowdController;

        // Layout
        private SerializedProperty _skillBarRoot;
        private SerializedProperty _slotContainer;
        private SerializedProperty _skillSlotPrefab;
        private SerializedProperty _slotSize;
        private SerializedProperty _slotSpacing;
        private SerializedProperty _barPadding;

        // Input
        private SerializedProperty _skillInputBindings;

        // Tooltip
        private SerializedProperty _tooltipPanel;
        private SerializedProperty _tooltipText;
        private SerializedProperty _tooltipOffset;
        private SerializedProperty _tooltipRichText;

        // Appearance
        private SerializedProperty _barBackground;
        private SerializedProperty _barBackgroundColor;
        private SerializedProperty _hideWhenNoSelection;
        private SerializedProperty _fadeDuration;
        private SerializedProperty _showCooldownAsMinutes;
        private SerializedProperty _minuteAbbreviation;
        private SerializedProperty _keybindLabelColor;
        private SerializedProperty _keybindLabelFontSize;
        private SerializedProperty _keybindLabelFont;

        // Auto-Cast Defaults
        private SerializedProperty _defaultAutocastEnabled;
        private SerializedProperty _autoCastToggleAction;
        private SerializedProperty _autoCastToggleLegacyKey;
        private SerializedProperty _autocastBorderColor;
        private SerializedProperty _autocastOverlaySprite;
        private SerializedProperty _autocastOverlayTint;

        // Audio
        private SerializedProperty _clickSound;
        private SerializedProperty _autoCastOnSound;
        private SerializedProperty _autoCastOffSound;
        private SerializedProperty _confirmSound;
        private SerializedProperty _cooldownErrorSound;
        private SerializedProperty _clickSoundVolume;
        private SerializedProperty _basePitch;
        private SerializedProperty _pitchVariation;

        // Targeting
        private SerializedProperty _targetingCursorTexture;
        private SerializedProperty _targetingCursorHotspot;
        private SerializedProperty _targetRaycastMask;
        private SerializedProperty _groundRaycastMask;
        private SerializedProperty _targetClickRadius;
        private SerializedProperty _activeTargetingBorderColor;

        // Ground Target Indicator
        private SerializedProperty _groundIndicatorType;
        private SerializedProperty _groundIndicatorInRangeColor;
        private SerializedProperty _groundIndicatorOutOfRangeColor;
        private SerializedProperty _groundIndicatorSegments;
        private SerializedProperty _groundIndicatorLineWidth;
        private SerializedProperty _groundIndicatorYOffset;
        private SerializedProperty _groundIndicatorPrefab;

        // Max Range Indicator
        private SerializedProperty _showMaxRangeIndicator;
        private SerializedProperty _maxRangeIndicatorType;
        private SerializedProperty _showMaxRangeForGroundTargetedAOE;
        private SerializedProperty _maxRangeIndicatorInRangeColor;
        private SerializedProperty _maxRangeIndicatorOutOfRangeColor;
        private SerializedProperty _maxRangeIndicatorSegments;
        private SerializedProperty _maxRangeIndicatorLineWidth;
        private SerializedProperty _maxRangeIndicatorYOffset;
        private SerializedProperty _maxRangeIndicatorPrefab;

        // Events
        private SerializedProperty _onSkillActivated;

        // Foldout states
        private static bool _showReferences = true;
        private static bool _showLayout = true;
        private static bool _showInput = true;
        private static bool _showTooltip = false;
        private static bool _showTargeting = true;
        private static bool _showAppearance = false;
        private static bool _showAutocastDefaults = false;
        private static bool _showAudio = false;
        private static bool _showEvents = false;

        private void OnEnable()
        {
            if (target == null || !(target is AgentSkillBarUI))
                return;

            // References
            _rtsController = serializedObject.FindProperty("rtsController");
            _crowdController = serializedObject.FindProperty("crowdController");

            // Layout
            _skillBarRoot = serializedObject.FindProperty("skillBarRoot");
            _slotContainer = serializedObject.FindProperty("slotContainer");
            _skillSlotPrefab = serializedObject.FindProperty("skillSlotPrefab");
            _slotSize = serializedObject.FindProperty("slotSize");
            _slotSpacing = serializedObject.FindProperty("slotSpacing");
            _barPadding = serializedObject.FindProperty("barPadding");

            // Input
            _skillInputBindings = serializedObject.FindProperty("skillInputBindings");

            // Tooltip
            _tooltipPanel = serializedObject.FindProperty("tooltipPanel");
            _tooltipText = serializedObject.FindProperty("tooltipText");
            _tooltipOffset = serializedObject.FindProperty("tooltipOffset");
            _tooltipRichText = serializedObject.FindProperty("tooltipRichText");

            // Appearance
            _barBackground = serializedObject.FindProperty("barBackground");
            _barBackgroundColor = serializedObject.FindProperty("barBackgroundColor");
            _hideWhenNoSelection = serializedObject.FindProperty("hideWhenNoSelection");
            _fadeDuration = serializedObject.FindProperty("fadeDuration");
            _showCooldownAsMinutes = serializedObject.FindProperty("showCooldownAsMinutes");
            _minuteAbbreviation = serializedObject.FindProperty("minuteAbbreviation");
            _keybindLabelColor = serializedObject.FindProperty("keybindLabelColor");
            _keybindLabelFontSize = serializedObject.FindProperty("keybindLabelFontSize");
#if TMP_PRESENT
            _keybindLabelFont = serializedObject.FindProperty("keybindLabelFontTMP");
#else
            _keybindLabelFont = serializedObject.FindProperty("keybindLabelFont");
#endif

            // Auto-Cast Defaults
            _defaultAutocastEnabled = serializedObject.FindProperty("defaultAutocastEnabled");
#if ENABLE_INPUT_SYSTEM
            _autoCastToggleAction = serializedObject.FindProperty("autoCastToggleAction");
#else
            _autoCastToggleLegacyKey = serializedObject.FindProperty("autoCastToggleLegacyKey");
#endif
            _autocastBorderColor = serializedObject.FindProperty("autocastBorderColor");
            _autocastOverlaySprite = serializedObject.FindProperty("autocastOverlaySprite");
            _autocastOverlayTint = serializedObject.FindProperty("autocastOverlayTint");

            // Audio
            _clickSound = serializedObject.FindProperty("clickSound");
            _autoCastOnSound = serializedObject.FindProperty("autoCastOnSound");
            _autoCastOffSound = serializedObject.FindProperty("autoCastOffSound");
            _confirmSound = serializedObject.FindProperty("confirmSound");
            _cooldownErrorSound = serializedObject.FindProperty("cooldownErrorSound");
            _clickSoundVolume = serializedObject.FindProperty("clickSoundVolume");
            _basePitch = serializedObject.FindProperty("basePitch");
            _pitchVariation = serializedObject.FindProperty("pitchVariation");

            // Targeting
            _targetingCursorTexture = serializedObject.FindProperty("targetingCursorTexture");
            _targetingCursorHotspot = serializedObject.FindProperty("targetingCursorHotspot");
            _targetRaycastMask = serializedObject.FindProperty("targetRaycastMask");
            _groundRaycastMask = serializedObject.FindProperty("groundRaycastMask");
            _targetClickRadius = serializedObject.FindProperty("targetClickRadius");
            _activeTargetingBorderColor = serializedObject.FindProperty("activeTargetingBorderColor");

            // Ground Target Indicator
            _groundIndicatorType = serializedObject.FindProperty("groundIndicatorType");
            _groundIndicatorInRangeColor = serializedObject.FindProperty("groundIndicatorInRangeColor");
            _groundIndicatorOutOfRangeColor = serializedObject.FindProperty("groundIndicatorOutOfRangeColor");
            _groundIndicatorSegments = serializedObject.FindProperty("groundIndicatorSegments");
            _groundIndicatorLineWidth = serializedObject.FindProperty("groundIndicatorLineWidth");
            _groundIndicatorYOffset = serializedObject.FindProperty("groundIndicatorYOffset");
            _groundIndicatorPrefab = serializedObject.FindProperty("groundIndicatorPrefab");

            // Max Range Indicator
            _showMaxRangeIndicator = serializedObject.FindProperty("showMaxRangeIndicator");
            _maxRangeIndicatorType = serializedObject.FindProperty("maxRangeIndicatorType");
            _showMaxRangeForGroundTargetedAOE = serializedObject.FindProperty("showMaxRangeForGroundTargetedAOE");
            _maxRangeIndicatorInRangeColor = serializedObject.FindProperty("maxRangeIndicatorInRangeColor");
            _maxRangeIndicatorOutOfRangeColor = serializedObject.FindProperty("maxRangeIndicatorOutOfRangeColor");
            _maxRangeIndicatorSegments = serializedObject.FindProperty("maxRangeIndicatorSegments");
            _maxRangeIndicatorLineWidth = serializedObject.FindProperty("maxRangeIndicatorLineWidth");
            _maxRangeIndicatorYOffset = serializedObject.FindProperty("maxRangeIndicatorYOffset");
            _maxRangeIndicatorPrefab = serializedObject.FindProperty("maxRangeIndicatorPrefab");

            // Events
            _onSkillActivated = serializedObject.FindProperty("onSkillActivated");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();

            EditorGUILayout.Space(5);

            // References
            _showReferences = EditorGUILayout.BeginFoldoutHeaderGroup(_showReferences, "References");
            if (_showReferences)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_rtsController,
                    new GUIContent("RTS Controller", "RTS Controller to listen for selection changes. Auto-discovered if null."));
                EditorGUILayout.PropertyField(_crowdController,
                    new GUIContent("Crowd Controller", "Crowd Controller for accessing agent data and triggering skills. Auto-discovered if null."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Layout
            _showLayout = EditorGUILayout.BeginFoldoutHeaderGroup(_showLayout, "Skill Bar Layout");
            if (_showLayout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_skillBarRoot,
                    new GUIContent("Skill Bar Root", "Root container that holds the skill slots. Hidden when no skills to show."));
                EditorGUILayout.PropertyField(_slotContainer,
                    new GUIContent("Slot Container", "Container (HorizontalLayoutGroup) where skill slot UI elements are spawned."));
                EditorGUILayout.PropertyField(_skillSlotPrefab,
                    new GUIContent("Slot Prefab", "Prefab for each skill slot. If null, slots are created programmatically with a default look."));
                EditorGUILayout.PropertyField(_slotSize,
                    new GUIContent("Slot Size", "Size of each skill slot in pixels."));
                EditorGUILayout.PropertyField(_slotSpacing,
                    new GUIContent("Slot Spacing", "Spacing between skill slots in pixels."));
                EditorGUILayout.PropertyField(_barPadding,
                    new GUIContent("Bar Padding", "Padding around the skill bar."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Input Bindings
            _showInput = EditorGUILayout.BeginFoldoutHeaderGroup(_showInput, "Input Bindings");
            if (_showInput)
            {
                EditorGUI.indentLevel++;
                DrawInputBindingsSection();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Tooltip
            _showTooltip = EditorGUILayout.BeginFoldoutHeaderGroup(_showTooltip, "Tooltip");
            if (_showTooltip)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_tooltipPanel,
                    new GUIContent("Tooltip Panel", "Panel that shows the skill preview/tooltip on hover."));
                EditorGUILayout.PropertyField(_tooltipText,
                    new GUIContent("Tooltip Text", "Text element for the tooltip content."));
                EditorGUILayout.PropertyField(_tooltipOffset,
                    new GUIContent("Tooltip Offset", "Offset of the tooltip from the hovered slot."));
                EditorGUILayout.PropertyField(_tooltipRichText,
                    new GUIContent("Rich Text", "Use rich text formatting in the tooltip (bold headers, etc.)."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Targeting
            _showTargeting = EditorGUILayout.BeginFoldoutHeaderGroup(_showTargeting, "Targeting");
            if (_showTargeting)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Skills requiring a target (SingleEnemy, Hitscan, Projectile, etc.) enter targeting mode on click.\n" +
                    "Right-click a valid target to cast. Escape or right-click on empty ground to cancel.\n" +
                    "Self/auto-target skills cast immediately.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(_targetingCursorTexture,
                    new GUIContent("Cursor Texture", "Cursor shown during targeting mode (e.g. crosshair). 32×32 recommended."));
                EditorGUILayout.PropertyField(_targetingCursorHotspot,
                    new GUIContent("Cursor Hotspot", "Hotspot offset for the cursor (typically center of texture)."));
                EditorGUILayout.PropertyField(_targetRaycastMask,
                    new GUIContent("Target Raycast Mask", "LayerMask for raycasting agents. Defaults to RTS Controller's attackTargetMask."));
                EditorGUILayout.PropertyField(_groundRaycastMask,
                    new GUIContent("Ground Raycast Mask", "LayerMask for ground raycasting. Defaults to RTS Controller's groundMask."));
                EditorGUILayout.PropertyField(_targetClickRadius,
                    new GUIContent("Target Click Radius", "Radius for finding nearby agents when clicking near ground."));
                EditorGUILayout.PropertyField(_activeTargetingBorderColor,
                    new GUIContent("Active Targeting Color", "Border color on the skill slot that is actively being targeted."));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Ground Target Indicator", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_groundIndicatorType,
                    new GUIContent("Indicator Type",
                        "Visual indicator shown on the ground during GroundTargetedAOE targeting.\n\n" +
                        "None — no indicator, only the targeting cursor is shown.\n" +
                        "RuntimeCircle — auto-generated LineRenderer circle (zero dependencies).\n" +
                        "CustomPrefab — instantiate your own prefab (projector, particles, VFX, etc.)."));

                var indicatorType = (Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType)_groundIndicatorType.enumValueIndex;

                if (indicatorType != Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType.None)
                {
                    EditorGUILayout.PropertyField(_groundIndicatorInRangeColor,
                        new GUIContent("In-Range Color", "Indicator color when at least one selected agent is within skill range."));
                    EditorGUILayout.PropertyField(_groundIndicatorOutOfRangeColor,
                        new GUIContent("Out-of-Range Color", "Indicator color when all selected agents are out of range."));
                    EditorGUILayout.PropertyField(_groundIndicatorYOffset,
                        new GUIContent("Y Offset", "Height above ground to prevent z-fighting."));
                }

                if (indicatorType == Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType.RuntimeCircle)
                {
                    EditorGUILayout.PropertyField(_groundIndicatorSegments,
                        new GUIContent("Circle Segments", "Number of line segments. Higher = smoother circle."));
                    EditorGUILayout.PropertyField(_groundIndicatorLineWidth,
                        new GUIContent("Line Width", "Width of the circle line."));
                }
                else if (indicatorType == Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType.CustomPrefab)
                {
                    EditorGUILayout.PropertyField(_groundIndicatorPrefab,
                        new GUIContent("Indicator Prefab",
                            "Custom prefab to instantiate as the indicator.\n" +
                            "Scaled uniformly so 1 unit radius = skill AOE radius."));
                    if (_groundIndicatorPrefab.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Assign a prefab to use as the ground indicator. " +
                            "This can be a Projector, Particle System, VFX Graph, or any GameObject.",
                            MessageType.Warning);
                    }
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Max Range Indicator (Manual Targeting)", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showMaxRangeIndicator,
                    new GUIContent("Show Max Range Indicator",
                        "Show an additional indicator for manual-targeted skills (SingleEnemy, Hitscan, Projectile, Cone, Line, etc.) " +
                        "to visualize the skill's MaxRange around the current selection."));

                if (_showMaxRangeIndicator.boolValue)
                {
                    EditorGUILayout.PropertyField(_maxRangeIndicatorType,
                        new GUIContent("Indicator Type",
                            "Visual indicator shown for manual-targeting max range.\n\n" +
                            "None — no max range indicator.\n" +
                            "RuntimeCircle — auto-generated LineRenderer circle.\n" +
                            "CustomPrefab — instantiate your own prefab."));
                    EditorGUILayout.PropertyField(_showMaxRangeForGroundTargetedAOE,
                        new GUIContent("Also For Ground AOE",
                            "When enabled, GroundTargetedAOE targeting shows both the AOE circle and this max-range circle."));

                    var maxRangeType = (Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType)_maxRangeIndicatorType.enumValueIndex;
                    if (maxRangeType != Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType.None)
                    {
                        EditorGUILayout.PropertyField(_maxRangeIndicatorInRangeColor,
                            new GUIContent("In-Range Color",
                                "Indicator color when the hovered point is in range of at least one selected caster."));
                        EditorGUILayout.PropertyField(_maxRangeIndicatorOutOfRangeColor,
                            new GUIContent("Out-of-Range Color",
                                "Indicator color when the hovered point is out of range of all selected casters."));
                        EditorGUILayout.PropertyField(_maxRangeIndicatorYOffset,
                            new GUIContent("Y Offset", "Height above ground to prevent z-fighting."));
                    }

                    if (maxRangeType == Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType.RuntimeCircle)
                    {
                        EditorGUILayout.PropertyField(_maxRangeIndicatorSegments,
                            new GUIContent("Circle Segments", "Number of line segments. Higher = smoother circle."));
                        EditorGUILayout.PropertyField(_maxRangeIndicatorLineWidth,
                            new GUIContent("Line Width", "Width of the max-range circle line."));
                    }
                    else if (maxRangeType == Arawn.EnemyMasses.Runtime.Skills.GroundTargetIndicatorType.CustomPrefab)
                    {
                        EditorGUILayout.PropertyField(_maxRangeIndicatorPrefab,
                            new GUIContent("Indicator Prefab",
                                "Custom prefab to instantiate as the max-range indicator.\n" +
                                "Scaled uniformly so 1 unit radius = skill MaxRange."));
                        if (_maxRangeIndicatorPrefab.objectReferenceValue == null)
                        {
                            EditorGUILayout.HelpBox(
                                "Assign a prefab to use as the max-range indicator.",
                                MessageType.Warning);
                        }
                    }
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Appearance
            _showAppearance = EditorGUILayout.BeginFoldoutHeaderGroup(_showAppearance, "Appearance");
            if (_showAppearance)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_barBackground,
                    new GUIContent("Background Image", "Background image for the skill bar panel."));
                EditorGUILayout.PropertyField(_barBackgroundColor,
                    new GUIContent("Background Color", "Background color for the skill bar."));
                EditorGUILayout.PropertyField(_hideWhenNoSelection,
                    new GUIContent("Hide When No Selection", "Hide the skill bar completely when no agents are selected."));
                EditorGUILayout.PropertyField(_fadeDuration,
                    new GUIContent("Fade Duration", "Fade animation duration when showing/hiding (0 = instant)."));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Cooldown Display", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showCooldownAsMinutes,
                    new GUIContent("Show As Minutes",
                        "When enabled, cooldowns of 60s or more are displayed as minutes (e.g. '2 min.'). " +
                        "Switches back to seconds once below 60s."));
                if (_showCooldownAsMinutes.boolValue)
                {
                    EditorGUILayout.PropertyField(_minuteAbbreviation,
                        new GUIContent("Abbreviation",
                            "How 'minutes' is abbreviated on the cooldown label."));
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Keybind Label", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_keybindLabelColor,
                    new GUIContent("Label Color", "Color for the keybind label text on each skill slot."));
                EditorGUILayout.PropertyField(_keybindLabelFontSize,
                    new GUIContent("Label Font Size", "Font size for the keybind labels."));
                EditorGUILayout.PropertyField(_keybindLabelFont,
#if TMP_PRESENT
                    new GUIContent("Label Font (TMP)", "TMP font asset for keybind labels. If null, the default TMP font is used."));
#else
                    new GUIContent("Label Font", "Legacy font for keybind labels. If null, the built-in font is used."));
#endif
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Auto-Cast
            _showAutocastDefaults = EditorGUILayout.BeginFoldoutHeaderGroup(_showAutocastDefaults, "Auto-Cast");
            if (_showAutocastDefaults)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Auto-cast eligibility is now configured per-skill on the AgentSkill asset " +
                    "(NotEligible / DefaultOff / DefaultOn).\n\n" +
                    "Players can toggle auto-cast on eligible slots at runtime.",
                    MessageType.Info);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Toggle Input", EditorStyles.miniBoldLabel);
#if ENABLE_INPUT_SYSTEM
                if (_autoCastToggleAction != null)
                    EditorGUILayout.PropertyField(_autoCastToggleAction,
                        new GUIContent("Toggle Action",
                            "Input action for toggling auto-cast on the hovered skill slot. " +
                            "Default: right mouse button. Can be rebound to any button or key."));
#else
                if (_autoCastToggleLegacyKey != null)
                    EditorGUILayout.PropertyField(_autoCastToggleLegacyKey,
                        new GUIContent("Toggle Key",
                            "Legacy key for toggling auto-cast on the hovered skill slot."));
#endif

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Visuals", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_autocastBorderColor,
                    new GUIContent("Border Color",
                        "Border color applied to skill slots when auto-cast is active. " +
                        "Pushed to each slot at build time, overriding the slot's default."));
                EditorGUILayout.PropertyField(_autocastOverlaySprite,
                    new GUIContent("Overlay Sprite",
                        "Optional sprite shown on top of the skill icon when auto-cast is active. " +
                        "If null, only the border color change is used."));
                if (_autocastOverlaySprite.objectReferenceValue != null)
                {
                    EditorGUILayout.PropertyField(_autocastOverlayTint,
                        new GUIContent("Overlay Tint",
                            "Tint color for the auto-cast overlay sprite. Use alpha to control opacity."));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Audio
            _showAudio = EditorGUILayout.BeginFoldoutHeaderGroup(_showAudio, "Audio");
            if (_showAudio)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Global sounds for the skill bar UI.\n" +
                    "Individual skills can override these via their own Sound fields in the AgentSkill asset.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(_clickSound,
                    new GUIContent("Click Sound",
                        "AudioClip played when any skill slot is left-clicked. " +
                        "Per-skill overrides take priority if assigned on the AgentSkill asset."));
                EditorGUILayout.PropertyField(_autoCastOnSound,
                    new GUIContent("Auto-Cast On Sound",
                        "AudioClip played when auto-cast is turned ON for a skill slot. " +
                        "Per-skill overrides take priority if assigned on the AgentSkill asset."));
                EditorGUILayout.PropertyField(_autoCastOffSound,
                    new GUIContent("Auto-Cast Off Sound",
                        "AudioClip played when auto-cast is turned OFF for a skill slot. " +
                        "Per-skill overrides take priority if assigned on the AgentSkill asset."));
                EditorGUILayout.PropertyField(_confirmSound,
                    new GUIContent("Confirm Sound",
                        "AudioClip played when a skill target is confirmed during targeting mode (right-click / command button). " +
                        "Per-skill overrides take priority if assigned on the AgentSkill asset."));
                EditorGUILayout.PropertyField(_cooldownErrorSound,
                    new GUIContent("Cooldown Error Sound",
                        "Optional sound played when the player tries to activate a skill that is still on cooldown. " +
                        "If empty, the click is silently ignored."));
                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(_clickSoundVolume,
                    new GUIContent("Volume", "Volume for all skill bar UI sounds."));
                EditorGUILayout.PropertyField(_basePitch,
                    new GUIContent("Base Pitch", "Base pitch for skill bar UI sounds. 1.0 = normal."));
                EditorGUILayout.PropertyField(_pitchVariation,
                    new GUIContent("Pitch Variation", "Random pitch variation applied symmetrically around Base Pitch. " +
                        "E.g. 0.1 with base 1.0 produces a random pitch in [0.9, 1.1]."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Events
            _showEvents = EditorGUILayout.BeginFoldoutHeaderGroup(_showEvents, "Events");
            if (_showEvents)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_onSkillActivated,
                    new GUIContent("On Skill Activated", "Fired when a skill is activated via input or click. Parameter: skill slot index."));
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
            EditorGUILayout.LabelField("Agent Skill Bar", headerStyle);
            EditorGUILayout.Space(2);

            var infoStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 10,
                richText = true
            };
            EditorGUILayout.LabelField(
                "Displays skills from the currently selected player-controlled EnemyCrowd agents.\n" +
                "Skills update dynamically when selection changes. Supports keybinds, cooldown overlay, and tooltips.",
                infoStyle);
        }

        private void DrawInputBindingsSection()
        {
#if ENABLE_INPUT_SYSTEM
            EditorGUILayout.HelpBox(
                "Configure a key binding for each skill slot.\n" +
                "The key letter will be displayed on the skill bar.\n" +
                "Input Actions support mouse, keyboard, and gamepad.",
                MessageType.Info);
#else
            EditorGUILayout.HelpBox(
                "Configure a key for each skill slot (Legacy Input).\n" +
                "The key name will be displayed on the skill bar.",
                MessageType.Info);
#endif

            EditorGUILayout.Space(3);

            // Draw each binding
            for (int i = 0; i < _skillInputBindings.arraySize; i++)
            {
                var element = _skillInputBindings.GetArrayElementAtIndex(i);
                var labelProp = element.FindPropertyRelative("label");

                string displayLabel = labelProp != null && !string.IsNullOrEmpty(labelProp.stringValue)
                    ? labelProp.stringValue
                    : $"Slot {i + 1}";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(displayLabel, EditorStyles.miniBoldLabel, GUILayout.Width(80));

                // Remove button
                if (GUILayout.Button("×", GUILayout.Width(20), GUILayout.Height(18)))
                {
                    _skillInputBindings.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(labelProp, new GUIContent("Label", "Display label for this binding."));

#if ENABLE_INPUT_SYSTEM
                var inputActionProp = element.FindPropertyRelative("inputAction");
                if (inputActionProp != null)
                {
                    EditorGUILayout.PropertyField(inputActionProp,
                        new GUIContent("Input Action", "Input action for activating this skill slot."));
                }
#else
                var legacyKeyProp = element.FindPropertyRelative("legacyKey");
                if (legacyKeyProp != null)
                {
                    EditorGUILayout.PropertyField(legacyKeyProp,
                        new GUIContent("Key", "Key for activating this skill slot."));
                }
#endif

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("+ Add Binding", GUILayout.Height(24)))
            {
                int newIndex = _skillInputBindings.arraySize;
                _skillInputBindings.InsertArrayElementAtIndex(newIndex);

                var newElement = _skillInputBindings.GetArrayElementAtIndex(newIndex);
                var newLabel = newElement.FindPropertyRelative("label");
                if (newLabel != null)
                    newLabel.stringValue = $"Skill {newIndex + 1}";
            }

            if (GUILayout.Button("Populate Defaults (Q,W,E,R,T,1-5)", GUILayout.Height(24)))
            {
                if (_skillInputBindings.arraySize == 0 ||
                    EditorUtility.DisplayDialog("Populate Default Bindings",
                        "This will replace all existing bindings with defaults (Q, W, E, R, T, 1-5).\n\nContinue?",
                        "Yes", "Cancel"))
                {
                    serializedObject.ApplyModifiedProperties();
                    var bar = (AgentSkillBarUI)target;
                    Undo.RecordObject(bar, "Populate Default Bindings");
                    bar.PopulateDefaultBindings(10);
                    EditorUtility.SetDirty(bar);
                    serializedObject.Update();
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
