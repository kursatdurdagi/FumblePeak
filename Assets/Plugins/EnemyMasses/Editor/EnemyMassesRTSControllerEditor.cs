using UnityEngine;
using UnityEditor;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for EnemyMassesRTSController.
    /// Provides a clean inspector with foldouts for advanced settings.
    /// </summary>
    [CustomEditor(typeof(Arawn.EnemyMasses.Runtime.EnemyMassesRTSController))]
    public class EnemyMassesRTSControllerEditor : UnityEditor.Editor
    {
        // Serialized properties
        private SerializedProperty _crowdController;
        private SerializedProperty _targetCamera;

        private SerializedProperty _enableClickSelection;
        private SerializedProperty _enableDoubleClickGroupSelection;
        private SerializedProperty _doubleClickOnlyInFrustum;
        private SerializedProperty _enableDragSelection;
        private SerializedProperty _doubleClickTime;
        private SerializedProperty _minDragDistance;
        private SerializedProperty _agentSelectionMask;

        private SerializedProperty _enableMoveCommands;
        private SerializedProperty _groundMask;
        private SerializedProperty _formationControlMode;
        private SerializedProperty _moveFormation;
        private SerializedProperty _formationSpacing;
        private SerializedProperty _formationSpacingMode;
        private SerializedProperty _battleLineGroupGap;
        private SerializedProperty _matchFormationSpeedToSlowest;
        private SerializedProperty _enableFormationCycling;
        private SerializedProperty _useMouseScrollWheelForFormationCycle;
        private SerializedProperty _formationCycleCooldown;

        // Drag-to-Adjust Spacing
        private SerializedProperty _enableDragToAdjustSpacing;
        private SerializedProperty _formationAdjustDelay;
        private SerializedProperty _persistSpacingPerFaction;
        private SerializedProperty _spacingDragThreshold;
        private SerializedProperty _spacingDragSensitivity;
        private SerializedProperty _minDragSpacing;
        private SerializedProperty _maxDragSpacing;

        // Formation Preview
        private SerializedProperty _showFormationPreview;
        private SerializedProperty _formationPreviewPrefab;
        private SerializedProperty _maxFormationPreviewMarkers;
        private SerializedProperty _formationPreviewColor;
        private SerializedProperty _formationPreviewMarkerSize;

        // Rotation Adjustment
        private SerializedProperty _enableRotationAdjustment;
        private SerializedProperty _rotationDragSensitivity;
        private SerializedProperty _rotationMarkerPrefab;
        private SerializedProperty _rotationMarkerColor;
        private SerializedProperty _rotationMarkerSize;
        private SerializedProperty _rotationMarkerDistance;
        private SerializedProperty _applyFacingOnArrival;
        private SerializedProperty _facingRotationSpeed;
        
        // Optimal Formation Assignment
        private SerializedProperty _useOptimalFormationAssignment;

        // Attack Commands
        private SerializedProperty _enableAttackCommands;
        private SerializedProperty _attackTargetMask;
        private SerializedProperty _attackTargetClickRadius;

        // Disengage Settings
        private SerializedProperty _disengageGracePeriod;

        // Follow Command
        private SerializedProperty _enableFollowCommand;
        private SerializedProperty _followableTags;
        private SerializedProperty _followTargetMask;
        private SerializedProperty _followTargetClickRadius;
        private SerializedProperty _followStopDistance;
        private SerializedProperty _followUpdateInterval;

        // Wall Placement Zones
        private SerializedProperty _enableWallPlacementZones;
        private SerializedProperty _wallPlacementZones;
        private SerializedProperty _autoDiscoverWallPlacementZones;

        // Input Settings - New Input System
        private SerializedProperty _selectionButton;
        private SerializedProperty _commandButton;
        private SerializedProperty _formationCycleUp;
        private SerializedProperty _formationCycleDown;
        private SerializedProperty _formationCycleAxis;
        private SerializedProperty _additiveSelectionModifier;
        private SerializedProperty _controlGroupSaveModifier;
        // Input Settings - Legacy
        private SerializedProperty _selectionMouseButton;
        private SerializedProperty _commandMouseButton;
        private SerializedProperty _formationCycleUpKey;
        private SerializedProperty _formationCycleDownKey;
        private SerializedProperty _legacyUseMouseScrollForFormationCycle;
        private SerializedProperty _additiveSelectionKey;
        private SerializedProperty _controlGroupSaveModifierKey;

        // Faction Control
        private SerializedProperty _playerControlledFactions;
        private SerializedProperty _restrictSelectionToPlayerFactions;

        private SerializedProperty _showSelectionRectangle;
        private SerializedProperty _selectionBorderColor;
        private SerializedProperty _selectionFillColor;
        private SerializedProperty _selectionBorderThickness;
        private SerializedProperty _showMoveIndicator;
        private SerializedProperty _moveIndicatorColor;
        private SerializedProperty _moveIndicatorDuration;
        private SerializedProperty _showSelectionHighlight;
        private SerializedProperty _selectionHighlightColor;
        private SerializedProperty _enableGizmos;

        // Selection Feedback properties
        private SerializedProperty _enableShaderHighlight;
        private SerializedProperty _highlightColorProperty;
        private SerializedProperty _highlightIntensityProperty;
        private SerializedProperty _shaderHighlightColor;
        private SerializedProperty _shaderHighlightIntensity;
        private SerializedProperty _selectionIndicatorPrefab;
        private SerializedProperty _indicatorOffset;
        private SerializedProperty _maxIndicatorPrefabs;
        private SerializedProperty _indicatorParent;
        private SerializedProperty _autoScaleIndicatorToAgent;
        private SerializedProperty _indicatorAtGroundForFlying;
        private SerializedProperty _autoUpdateIndicatorPositions;

        // Attack Target Feedback properties
        private SerializedProperty _enableAttackTargetFeedback;
        private SerializedProperty _enableAttackTargetShaderHighlight;
        private SerializedProperty _attackTargetHighlightColor;
        private SerializedProperty _attackTargetHighlightIntensity;
        private SerializedProperty _attackTargetColorProperty;
        private SerializedProperty _attackTargetIntensityProperty;
        private SerializedProperty _attackTargetIndicatorPrefab;
        private SerializedProperty _attackTargetIndicatorOffset;
        private SerializedProperty _attackTargetIndicatorParent;
        private SerializedProperty _attackTargetFeedbackMode;
        private SerializedProperty _attackTargetFeedbackDuration;
        private SerializedProperty _attackTargetFlashSpeed;
        private SerializedProperty _clearFeedbackOnTargetDeath;
        private SerializedProperty _autoUpdateAttackTargetPosition;

        private SerializedProperty _onSelectionChanged;
        private SerializedProperty _onAgentSelected;
        private SerializedProperty _onAgentDeselected;
        private SerializedProperty _onSelectionCleared;
        private SerializedProperty _onMoveCommandIssued;
        private SerializedProperty _onAttackCommandIssued;

        // Destination Indicator properties
        private SerializedProperty _showDestinationIndicator;
        private SerializedProperty _destinationIndicatorPrefab;
        private SerializedProperty _destinationIndicatorOffset;
        private SerializedProperty _destinationReachedThreshold;
        private SerializedProperty _hideIndicatorsOnFirstArrival;
        private SerializedProperty _indicatorFadeOutDuration;
        private SerializedProperty _destinationIndicatorParent;
        private SerializedProperty _showDirectionIndicator;
        private SerializedProperty _directionIndicatorPrefab;
        private SerializedProperty _useLineRenderer;
        private SerializedProperty _drawPathInsteadOfDirection;
        private SerializedProperty _lineHeightOffset;
        private SerializedProperty _pathUpdateInterval;
        private SerializedProperty _pathInterpolationSpeed;
        private SerializedProperty _directionIndicatorOffset;
        private SerializedProperty _directionIndicatorRotationOffset;
        private SerializedProperty _directionIndicatorOrientation;
        private SerializedProperty _directionIndicatorMinDistance;
        private SerializedProperty _directionIndicatorDistancePerAgent;
        private SerializedProperty _directionIndicatorMaxDistance;
        private SerializedProperty _directionIndicatorParent;
        private SerializedProperty _renderAboveFog;
        private SerializedProperty _indicatorSortingOrder;

        // Foldout states
        private static bool _showReferences = true;
        private static bool _showSelectionSettings = true;
        private static bool _showCommandSettings = true;
        private static bool _showInputSettings = true;
        private static bool _showFactionControl = true;
        private static bool _showVisualSettings = false;
        private static bool _showDestinationIndicators = false;
        private static bool _showSelectionFeedback = false;
        private static bool _showAttackTargetFeedback = false;
        private static bool _showEvents = false;
        private static bool _showNetworkEvents = false;

        private void OnEnable()
        {
            if (target == null || !(target is Arawn.EnemyMasses.Runtime.EnemyMassesRTSController))
                return;
                
            // References
            _crowdController = serializedObject.FindProperty("crowdController");
            _targetCamera = serializedObject.FindProperty("targetCamera");

            // Selection Settings
            _enableClickSelection = serializedObject.FindProperty("enableClickSelection");
            _enableDoubleClickGroupSelection = serializedObject.FindProperty("enableDoubleClickGroupSelection");
            _doubleClickOnlyInFrustum = serializedObject.FindProperty("doubleClickOnlyInFrustum");
            _enableDragSelection = serializedObject.FindProperty("enableDragSelection");
            _doubleClickTime = serializedObject.FindProperty("doubleClickTime");
            _minDragDistance = serializedObject.FindProperty("minDragDistance");
            _agentSelectionMask = serializedObject.FindProperty("agentSelectionMask");

            // Command Settings
            _enableMoveCommands = serializedObject.FindProperty("enableMoveCommands");
            _groundMask = serializedObject.FindProperty("groundMask");
            _formationControlMode = serializedObject.FindProperty("formationControlMode");
            _moveFormation = serializedObject.FindProperty("moveFormation");
            _formationSpacing = serializedObject.FindProperty("formationSpacing");
            _formationSpacingMode = serializedObject.FindProperty("formationSpacingMode");
            _battleLineGroupGap = serializedObject.FindProperty("battleLineGroupGap");
            _matchFormationSpeedToSlowest = serializedObject.FindProperty("matchFormationSpeedToSlowest");
            _enableFormationCycling = serializedObject.FindProperty("enableFormationCycling");
            _useMouseScrollWheelForFormationCycle = serializedObject.FindProperty("useMouseScrollWheelForFormationCycle");
            _formationCycleCooldown = serializedObject.FindProperty("formationCycleCooldown");

            // Drag-to-Adjust Spacing
            _enableDragToAdjustSpacing = serializedObject.FindProperty("enableDragToAdjustSpacing");
            _formationAdjustDelay = serializedObject.FindProperty("formationAdjustDelay");
            _persistSpacingPerFaction = serializedObject.FindProperty("persistSpacingPerFaction");
            _spacingDragThreshold = serializedObject.FindProperty("spacingDragThreshold");
            _spacingDragSensitivity = serializedObject.FindProperty("spacingDragSensitivity");
            _minDragSpacing = serializedObject.FindProperty("minDragSpacing");
            _maxDragSpacing = serializedObject.FindProperty("maxDragSpacing");

            // Formation Preview
            _showFormationPreview = serializedObject.FindProperty("showFormationPreview");
            _formationPreviewPrefab = serializedObject.FindProperty("formationPreviewPrefab");
            _maxFormationPreviewMarkers = serializedObject.FindProperty("maxFormationPreviewMarkers");
            _formationPreviewColor = serializedObject.FindProperty("formationPreviewColor");
            _formationPreviewMarkerSize = serializedObject.FindProperty("formationPreviewMarkerSize");

            // Rotation Adjustment
            _enableRotationAdjustment = serializedObject.FindProperty("enableRotationAdjustment");
            _rotationDragSensitivity = serializedObject.FindProperty("rotationDragSensitivity");
            _rotationMarkerPrefab = serializedObject.FindProperty("rotationMarkerPrefab");
            _rotationMarkerColor = serializedObject.FindProperty("rotationMarkerColor");
            _rotationMarkerSize = serializedObject.FindProperty("rotationMarkerSize");
            _rotationMarkerDistance = serializedObject.FindProperty("rotationMarkerDistance");
            _applyFacingOnArrival = serializedObject.FindProperty("applyFacingOnArrival");
            _facingRotationSpeed = serializedObject.FindProperty("facingRotationSpeed");
            
            // Optimal Formation Assignment
            _useOptimalFormationAssignment = serializedObject.FindProperty("useOptimalFormationAssignment");

            // Attack Commands
            _enableAttackCommands = serializedObject.FindProperty("enableAttackCommands");
            _attackTargetMask = serializedObject.FindProperty("attackTargetMask");
            _attackTargetClickRadius = serializedObject.FindProperty("attackTargetClickRadius");

            // Disengage Settings
            _disengageGracePeriod = serializedObject.FindProperty("disengageGracePeriod");

            // Follow Command
            _enableFollowCommand = serializedObject.FindProperty("enableFollowCommand");
            _followableTags = serializedObject.FindProperty("followableTags");
            _followTargetMask = serializedObject.FindProperty("followTargetMask");
            _followTargetClickRadius = serializedObject.FindProperty("followTargetClickRadius");
            _followStopDistance = serializedObject.FindProperty("followStopDistance");
            _followUpdateInterval = serializedObject.FindProperty("followUpdateInterval");

            // Wall Placement Zones
            _enableWallPlacementZones = serializedObject.FindProperty("enableWallPlacementZones");
            _wallPlacementZones = serializedObject.FindProperty("wallPlacementZones");
            _autoDiscoverWallPlacementZones = serializedObject.FindProperty("autoDiscoverWallPlacementZones");

            // Input Settings
#if ENABLE_INPUT_SYSTEM
            _selectionButton = serializedObject.FindProperty("selectionButton");
            _commandButton = serializedObject.FindProperty("commandButton");
            _formationCycleUp = serializedObject.FindProperty("formationCycleUp");
            _formationCycleDown = serializedObject.FindProperty("formationCycleDown");
            _formationCycleAxis = serializedObject.FindProperty("formationCycleAxis");
            _additiveSelectionModifier = serializedObject.FindProperty("additiveSelectionModifier");
            _controlGroupSaveModifier = serializedObject.FindProperty("controlGroupSaveModifier");
#else
            _selectionMouseButton = serializedObject.FindProperty("selectionMouseButton");
            _commandMouseButton = serializedObject.FindProperty("commandMouseButton");
            _formationCycleUpKey = serializedObject.FindProperty("formationCycleUpKey");
            _formationCycleDownKey = serializedObject.FindProperty("formationCycleDownKey");
            _legacyUseMouseScrollForFormationCycle = serializedObject.FindProperty("legacyUseMouseScrollForFormationCycle");
            _additiveSelectionKey = serializedObject.FindProperty("additiveSelectionKey");
            _controlGroupSaveModifierKey = serializedObject.FindProperty("controlGroupSaveModifierKey");
#endif

            // Faction Control
            _playerControlledFactions = serializedObject.FindProperty("playerControlledFactions");
            _restrictSelectionToPlayerFactions = serializedObject.FindProperty("restrictSelectionToPlayerFactions");

            // Visual Settings
            _showSelectionRectangle = serializedObject.FindProperty("showSelectionRectangle");
            _selectionBorderColor = serializedObject.FindProperty("selectionBorderColor");
            _selectionFillColor = serializedObject.FindProperty("selectionFillColor");
            _selectionBorderThickness = serializedObject.FindProperty("selectionBorderThickness");
            _showMoveIndicator = serializedObject.FindProperty("showMoveIndicator");
            _moveIndicatorColor = serializedObject.FindProperty("moveIndicatorColor");
            _moveIndicatorDuration = serializedObject.FindProperty("moveIndicatorDuration");
            _showSelectionHighlight = serializedObject.FindProperty("showSelectionHighlight");
            _selectionHighlightColor = serializedObject.FindProperty("selectionHighlightColor");
            _enableGizmos = serializedObject.FindProperty("enableGizmos");

            // Selection Feedback
            _enableShaderHighlight = serializedObject.FindProperty("enableShaderHighlight");
            _highlightColorProperty = serializedObject.FindProperty("highlightColorProperty");
            _highlightIntensityProperty = serializedObject.FindProperty("highlightIntensityProperty");
            _shaderHighlightColor = serializedObject.FindProperty("shaderHighlightColor");
            _shaderHighlightIntensity = serializedObject.FindProperty("shaderHighlightIntensity");
            _selectionIndicatorPrefab = serializedObject.FindProperty("selectionIndicatorPrefab");
            _indicatorOffset = serializedObject.FindProperty("indicatorOffset");
            _maxIndicatorPrefabs = serializedObject.FindProperty("maxIndicatorPrefabs");
            _indicatorParent = serializedObject.FindProperty("indicatorParent");
            _autoScaleIndicatorToAgent = serializedObject.FindProperty("autoScaleIndicatorToAgent");
            _indicatorAtGroundForFlying = serializedObject.FindProperty("indicatorAtGroundForFlying");
            _autoUpdateIndicatorPositions = serializedObject.FindProperty("autoUpdateIndicatorPositions");

            // Attack Target Feedback
            _enableAttackTargetFeedback = serializedObject.FindProperty("enableAttackTargetFeedback");
            _enableAttackTargetShaderHighlight = serializedObject.FindProperty("enableAttackTargetShaderHighlight");
            _attackTargetHighlightColor = serializedObject.FindProperty("attackTargetHighlightColor");
            _attackTargetHighlightIntensity = serializedObject.FindProperty("attackTargetHighlightIntensity");
            _attackTargetColorProperty = serializedObject.FindProperty("attackTargetColorProperty");
            _attackTargetIntensityProperty = serializedObject.FindProperty("attackTargetIntensityProperty");
            _attackTargetIndicatorPrefab = serializedObject.FindProperty("attackTargetIndicatorPrefab");
            _attackTargetIndicatorOffset = serializedObject.FindProperty("attackTargetIndicatorOffset");
            _attackTargetIndicatorParent = serializedObject.FindProperty("attackTargetIndicatorParent");
            _attackTargetFeedbackMode = serializedObject.FindProperty("attackTargetFeedbackMode");
            _attackTargetFeedbackDuration = serializedObject.FindProperty("attackTargetFeedbackDuration");
            _attackTargetFlashSpeed = serializedObject.FindProperty("attackTargetFlashSpeed");
            _clearFeedbackOnTargetDeath = serializedObject.FindProperty("clearFeedbackOnTargetDeath");
            _autoUpdateAttackTargetPosition = serializedObject.FindProperty("autoUpdateAttackTargetPosition");

            // Events
            _onSelectionChanged = serializedObject.FindProperty("onSelectionChanged");
            _onAgentSelected = serializedObject.FindProperty("onAgentSelected");
            _onAgentDeselected = serializedObject.FindProperty("onAgentDeselected");
            _onSelectionCleared = serializedObject.FindProperty("onSelectionCleared");
            _onMoveCommandIssued = serializedObject.FindProperty("onMoveCommandIssued");
            _onAttackCommandIssued = serializedObject.FindProperty("onAttackCommandIssued");

            // Destination Indicators
            _showDestinationIndicator = serializedObject.FindProperty("showDestinationIndicator");
            _destinationIndicatorPrefab = serializedObject.FindProperty("destinationIndicatorPrefab");
            _destinationIndicatorOffset = serializedObject.FindProperty("destinationIndicatorOffset");
            _destinationReachedThreshold = serializedObject.FindProperty("destinationReachedThreshold");
            _hideIndicatorsOnFirstArrival = serializedObject.FindProperty("hideIndicatorsOnFirstArrival");
            _indicatorFadeOutDuration = serializedObject.FindProperty("indicatorFadeOutDuration");
            _destinationIndicatorParent = serializedObject.FindProperty("destinationIndicatorParent");
            _showDirectionIndicator = serializedObject.FindProperty("showDirectionIndicator");
            _directionIndicatorPrefab = serializedObject.FindProperty("directionIndicatorPrefab");
            _useLineRenderer = serializedObject.FindProperty("useLineRenderer");
            _drawPathInsteadOfDirection = serializedObject.FindProperty("drawPathInsteadOfDirection");
            _lineHeightOffset = serializedObject.FindProperty("lineHeightOffset");
            _pathUpdateInterval = serializedObject.FindProperty("pathUpdateInterval");
            _pathInterpolationSpeed = serializedObject.FindProperty("pathInterpolationSpeed");
            _directionIndicatorOffset = serializedObject.FindProperty("directionIndicatorOffset");
            _directionIndicatorRotationOffset = serializedObject.FindProperty("directionIndicatorRotationOffset");
            _directionIndicatorOrientation = serializedObject.FindProperty("directionIndicatorOrientation");
            _directionIndicatorMinDistance = serializedObject.FindProperty("directionIndicatorMinDistance");
            _directionIndicatorDistancePerAgent = serializedObject.FindProperty("directionIndicatorDistancePerAgent");
            _directionIndicatorMaxDistance = serializedObject.FindProperty("directionIndicatorMaxDistance");
            _directionIndicatorParent = serializedObject.FindProperty("directionIndicatorParent");
            _renderAboveFog = serializedObject.FindProperty("renderAboveFog");
            _indicatorSortingOrder = serializedObject.FindProperty("indicatorSortingOrder");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var controller = (Arawn.EnemyMasses.Runtime.EnemyMassesRTSController)target;

            // Title
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("RTS Selection & Commands", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // References Section
            _showReferences = EditorGUILayout.BeginFoldoutHeaderGroup(_showReferences, "References");
            if (_showReferences)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_crowdController);
                EditorGUILayout.PropertyField(_targetCamera, new GUIContent("Camera", "Camera used for raycasting. Uses Camera.main if not set."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Selection Settings Section
            _showSelectionSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showSelectionSettings, "Selection Settings");
            if (_showSelectionSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_enableClickSelection, new GUIContent("Click Selection", "Enable single-click selection of agents"));
                
                EditorGUI.BeginDisabledGroup(!_enableClickSelection.boolValue);
                EditorGUILayout.PropertyField(_enableDoubleClickGroupSelection, new GUIContent("Double-Click Group Select", "Double-click to select all agents of the same faction"));
                if (_enableDoubleClickGroupSelection.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_doubleClickTime, new GUIContent("Double-Click Time", "Time window for double-click detection"));
                    EditorGUILayout.PropertyField(_doubleClickOnlyInFrustum, new GUIContent("Only In Camera View", "Only select agents visible in camera frustum"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(3);

                EditorGUILayout.PropertyField(_enableDragSelection, new GUIContent("Drag Rectangle Selection", "Enable drag-to-select with rectangle"));
                if (_enableDragSelection.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_minDragDistance, new GUIContent("Min Drag Distance", "Minimum pixels to drag before rectangle appears"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(_agentSelectionMask, new GUIContent("Selection Layer Mask", "Layers to include in agent selection raycasts"));

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Command Settings Section
            _showCommandSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showCommandSettings, "Command Settings");
            if (_showCommandSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_enableMoveCommands, new GUIContent("Enable Move Commands", "Click to move selected agents"));

                EditorGUI.BeginDisabledGroup(!_enableMoveCommands.boolValue);
                EditorGUILayout.PropertyField(_groundMask, new GUIContent("Ground Layer Mask", "Layers to raycast for move destinations"));
                EditorGUILayout.PropertyField(_formationControlMode, new GUIContent("Formation Control Mode", "Combined Formation: all selected agents form one group.\nBattle Line: each EnemyCrowd keeps its own formation, arranged side by side like a Total War battle line."));
                EditorGUILayout.PropertyField(_moveFormation, new GUIContent("Move Formation", "Formation shape when moving agents (used as default for Combined Formation, or as fallback for Battle Line)"));
                EditorGUILayout.PropertyField(_formationSpacing, new GUIContent("Formation Spacing", "Distance between agents in formation"));
                if (_formationControlMode.enumValueIndex == (int)Arawn.EnemyMasses.Runtime.FormationControlMode.BattleLine)
                {
                    EditorGUILayout.PropertyField(_formationSpacingMode, new GUIContent("Formation Spacing Mode", "Global: all crowds share the same spacing value.\nPer Crowd: each crowd type can have its own spacing (drag-to-adjust while a single crowd type is selected)."));
                }
                if (_formationControlMode.enumValueIndex == (int)Arawn.EnemyMasses.Runtime.FormationControlMode.BattleLine)
                {
                    EditorGUILayout.PropertyField(_battleLineGroupGap, new GUIContent("Battle Line Group Gap", "Gap between EnemyCrowd groups in Battle Line mode. Drag-to-adjust modifies this value at runtime when multiple crowd types are selected."));
                }
                EditorGUILayout.PropertyField(_matchFormationSpeedToSlowest, new GUIContent("Match Slowest Speed", "Clamp formation move speed to the slowest selected agent"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Formation Cycling", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Hold the command button and use the configured bindings to cycle formation types before issuing the command.", MessageType.Info);
                EditorGUILayout.PropertyField(_enableFormationCycling, new GUIContent("Enable Formation Cycling", "Allow cycling formation type while holding the command button."));

                EditorGUI.BeginDisabledGroup(!_enableFormationCycling.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_useMouseScrollWheelForFormationCycle, new GUIContent("Use Scroll Wheel", "Allow mouse scroll while holding the command button to cycle formation types. Leave off to avoid camera zoom conflicts."));
                EditorGUILayout.PropertyField(_formationCycleCooldown, new GUIContent("Cycle Cooldown", "Cooldown between formation cycles to prevent over-scrolling."));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Drag-to-Adjust Spacing", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Hold command button and drag horizontally to adjust formation spacing before executing the move.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_enableDragToAdjustSpacing, new GUIContent("Enable Drag-to-Adjust", "Hold and drag to adjust spacing per-command"));
                
                EditorGUI.BeginDisabledGroup(!_enableDragToAdjustSpacing.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_formationAdjustDelay, new GUIContent("Adjust Delay (s)",
                    "Time in seconds the command button must be held before formation adjustment starts. " +
                    "Quick click-and-release commands execute immediately without showing the preview."));
                EditorGUILayout.PropertyField(_persistSpacingPerFaction, new GUIContent("Persist Per-Faction", "Remember spacing for each faction. Soldiers can have spacing=2, Rangers spacing=1, etc."));
                EditorGUILayout.PropertyField(_spacingDragThreshold, new GUIContent("Drag Threshold (px)", "Pixels to drag before adjustment starts"));
                EditorGUILayout.PropertyField(_spacingDragSensitivity, new GUIContent("Drag Sensitivity", "Spacing change per pixel dragged"));
                EditorGUILayout.PropertyField(_minDragSpacing, new GUIContent("Min Spacing", "Minimum allowed spacing when dragging"));
                EditorGUILayout.PropertyField(_maxDragSpacing, new GUIContent("Max Spacing", "Maximum allowed spacing when dragging"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Formation Preview (Ghost Markers)", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showFormationPreview, new GUIContent("Show Preview Markers", "Show ghost markers while adjusting spacing"));
                
                EditorGUI.BeginDisabledGroup(!_showFormationPreview.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_formationPreviewPrefab, new GUIContent("Preview Prefab", "Custom prefab for markers (uses spheres if null)"));
                EditorGUILayout.PropertyField(_maxFormationPreviewMarkers, new GUIContent("Max Preview Markers", "Limits markers for performance with large selections"));
                EditorGUILayout.PropertyField(_formationPreviewColor, new GUIContent("Preview Color", "Color of default sphere markers"));
                EditorGUILayout.PropertyField(_formationPreviewMarkerSize, new GUIContent("Marker Size", "Size of default sphere markers"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Formation Rotation Adjustment", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("While adjusting spacing, hold selection button to lock spacing and adjust formation rotation instead.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_enableRotationAdjustment, new GUIContent("Enable Rotation Adjustment", "Hold selection button while adjusting spacing to switch to rotation mode"));
                
                EditorGUI.BeginDisabledGroup(!_enableRotationAdjustment.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_rotationDragSensitivity, new GUIContent("Rotation Sensitivity", "Degrees of rotation per pixel dragged"));
                EditorGUILayout.PropertyField(_rotationMarkerPrefab, new GUIContent("Direction Marker Prefab", "Custom prefab for direction indicator (uses sphere if null)"));
                EditorGUILayout.PropertyField(_rotationMarkerColor, new GUIContent("Direction Marker Color", "Color of default direction marker"));
                EditorGUILayout.PropertyField(_rotationMarkerSize, new GUIContent("Direction Marker Size", "Size of default direction marker"));
                EditorGUILayout.PropertyField(_rotationMarkerDistance, new GUIContent("Marker Distance", "Distance from formation center to place direction marker"));
                
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Facing on Arrival", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_applyFacingOnArrival, new GUIContent("Apply Facing on Arrival", "Rotate agents to face the commanded direction when they arrive"));
                
                EditorGUI.BeginDisabledGroup(!_applyFacingOnArrival.boolValue);
                EditorGUILayout.PropertyField(_facingRotationSpeed, new GUIContent("Rotation Speed (°/s)", "How fast agents rotate to face. Set to 0 for instant rotation."));
                EditorGUI.EndDisabledGroup();
                
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();
                
                EditorGUI.EndDisabledGroup(); // End drag-to-adjust disabled group
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Optimal Formation Assignment", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("When rotating formations (command + selection button), agents are assigned to the closest formation position to minimize travel. Only applies to rotated moves, not regular move commands.", MessageType.Info);
                EditorGUILayout.PropertyField(_useOptimalFormationAssignment, new GUIContent("Use Optimal Assignment", "Assign agents to closest formation positions when rotating formations"));
                
                EditorGUI.EndDisabledGroup(); // End move commands disabled group

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Attack Commands", EditorStyles.miniBoldLabel);;
                EditorGUILayout.HelpBox("Command-click on an enemy agent to order selected units to attack. Enemy = not player-controlled and different faction.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_enableAttackCommands, new GUIContent("Enable Attack Commands", "Click on enemy agents to issue attack commands"));
                
                EditorGUI.BeginDisabledGroup(!_enableAttackCommands.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_attackTargetMask, new GUIContent("Target Layer Mask", "Layers to raycast for potential attack targets"));
                EditorGUILayout.PropertyField(_attackTargetClickRadius, new GUIContent("Click Radius", "Max distance from click to detect agents without colliders"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Disengage Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Grace period after move commands where Offensive agents won't re-acquire targets. Allows retreating from combat. Set to 0 to disable.", MessageType.Info);
                EditorGUILayout.PropertyField(_disengageGracePeriod, new GUIContent("Disengage Grace Period", "Seconds after move command before agents can acquire new targets"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Follow Command", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Order selected agents to follow a target. Supports Player Targets, tagged objects, and other crowd agents (detected via ground raycast + proximity since agents have no colliders).", MessageType.Info);

                EditorGUILayout.PropertyField(_enableFollowCommand, new GUIContent("Enable Follow Command", "Allow the follow command system"));

                EditorGUI.BeginDisabledGroup(!_enableFollowCommand.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_followTargetMask, new GUIContent("Target Layer Mask", "Layers to raycast for collider-based follow targets"));
                EditorGUILayout.PropertyField(_followTargetClickRadius, new GUIContent("Click Radius", "Max distance from ground click to detect crowd agents without colliders"));
                EditorGUILayout.PropertyField(_followStopDistance, new GUIContent("Stop Distance", "Min distance agents maintain from the target"));
                EditorGUILayout.PropertyField(_followUpdateInterval, new GUIContent("Update Interval", "How often follow agents update their destination (seconds)"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Wall Placement Zones", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Command-click on a Wall Placement Zone to mount selected agents at marked positions. Perfect for wall defenders, tower archers, and siege scenarios.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_enableWallPlacementZones, new GUIContent("Enable Wall Placement", "Allow command-clicking on Wall Placement Zones to mount agents"));
                
                EditorGUI.BeginDisabledGroup(!_enableWallPlacementZones.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_autoDiscoverWallPlacementZones, new GUIContent("Auto-Discover Zones", "Automatically find all Wall Placement Zones in the scene at startup"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Draw the followable tags list outside the foldout header to avoid nesting
            // (Unity 6 treats List/Array foldouts as nested headers inside BeginFoldoutHeaderGroup)
            if (_showCommandSettings && _enableFollowCommand.boolValue)
            {
                EditorGUI.indentLevel += 2;
                EditorGUILayout.PropertyField(_followableTags, new GUIContent("Followable Tags", "Tags valid as follow targets. Player Targets are always valid."));
                EditorGUI.indentLevel -= 2;
            }

            // Draw the zones list outside the foldout header to avoid nesting issues
            if (_showCommandSettings && _enableWallPlacementZones.boolValue)
            {
                EditorGUI.indentLevel += 2;
                EditorGUILayout.PropertyField(_wallPlacementZones, new GUIContent("Zones", "Manually assigned Wall Placement Zones (in addition to auto-discovered ones)"));
                
                // Runtime info
                if (Application.isPlaying)
                {
                    var rtsController = (Arawn.EnemyMasses.Runtime.EnemyMassesRTSController)target;
                    var zones = rtsController.WallPlacementZones;
                    if (zones != null && zones.Count > 0)
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.LabelField("Runtime Status", EditorStyles.miniLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Active Zones: {zones.Count}");
                        int totalAssigned = 0;
                        int totalSpots = 0;
                        foreach (var zone in zones)
                        {
                            if (zone != null)
                            {
                                totalAssigned += zone.OccupiedSpots;
                                totalSpots += zone.SpotCount;
                            }
                        }
                        EditorGUILayout.LabelField($"Assigned: {totalAssigned}/{totalSpots}");
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUI.indentLevel -= 2;
            }

            EditorGUILayout.Space(3);

            // Input Settings Section
            _showInputSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showInputSettings, "Input Settings");
            if (_showInputSettings)
            {
                EditorGUI.indentLevel++;

#if ENABLE_INPUT_SYSTEM
                EditorGUILayout.HelpBox(
                    "Configure input bindings for selection, commands, and modifiers.\nSupports mouse, keyboard, and gamepad buttons.",
                    MessageType.Info);
                
                EditorGUILayout.PropertyField(_selectionButton, new GUIContent("Selection Button", "Input action for selection (click to select, drag to box select). Default is left mouse button."));
                EditorGUILayout.PropertyField(_commandButton, new GUIContent("Command Button", "Input action for commands (move, attack). Default is right mouse button."));
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Formation Cycling", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_formationCycleUp, new GUIContent("Cycle Up", "Input action to cycle formation up while holding the command button."));
                EditorGUILayout.PropertyField(_formationCycleDown, new GUIContent("Cycle Down", "Input action to cycle formation down while holding the command button."));
                EditorGUILayout.PropertyField(_formationCycleAxis, new GUIContent("Cycle Axis", "Axis/Value binding to cycle formation while holding the command button (e.g., <Mouse>/scroll/y)."));
                
                EditorGUILayout.PropertyField(_additiveSelectionModifier, new GUIContent("Additive Selection Modifier", "Hold to add to selection instead of replacing. Default is Left Shift."));
                EditorGUILayout.PropertyField(_controlGroupSaveModifier, new GUIContent("Control Group Save Modifier", "Modifier+0-9 to save selection, 0-9 to recall. Default is Ctrl."));
#else
                EditorGUILayout.PropertyField(_selectionMouseButton, new GUIContent("Selection Mouse Button", "Mouse button for selection (0 = left, 1 = right, 2 = middle)"));
                EditorGUILayout.PropertyField(_commandMouseButton, new GUIContent("Command Mouse Button", "Mouse button for commands (0 = left, 1 = right, 2 = middle)"));
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Formation Cycling", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_formationCycleUpKey, new GUIContent("Cycle Up Key", "Key to cycle formation up while holding the command button (optional)."));
                EditorGUILayout.PropertyField(_formationCycleDownKey, new GUIContent("Cycle Down Key", "Key to cycle formation down while holding the command button (optional)."));
                EditorGUILayout.PropertyField(_legacyUseMouseScrollForFormationCycle, new GUIContent("Use Scroll Wheel", "Use mouse scroll while holding the command button to cycle formations (legacy input). Leave off to avoid camera zoom conflicts."));
                
                EditorGUILayout.PropertyField(_additiveSelectionKey, new GUIContent("Additive Selection Key", "Hold to add to selection instead of replacing"));
                EditorGUILayout.PropertyField(_controlGroupSaveModifierKey, new GUIContent("Control Group Save Modifier", "Modifier+0-9 to save selection, 0-9 to recall"));
#endif

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Faction Control Section
            _showFactionControl = EditorGUILayout.BeginFoldoutHeaderGroup(_showFactionControl, "Faction Control");
            if (_showFactionControl)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("Control which factions the player can select and command. Leave empty to allow all factions.", MessageType.Info);

                EditorGUILayout.PropertyField(_playerControlledFactions, new GUIContent("Player Factions", "Factions the player can select and command"));
                
                EditorGUILayout.PropertyField(_restrictSelectionToPlayerFactions, new GUIContent("Restrict Selection", "When enabled, only player-controlled factions can be selected. When disabled, all factions can be selected but only player-controlled can receive commands."));

                // Show helpful info about current state
                if (_playerControlledFactions.FindPropertyRelative("selectedFactions") != null)
                {
                    var factionNames = _playerControlledFactions.FindPropertyRelative("selectedFactions");
                    if (factionNames.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox("No factions selected - all factions are controllable.", MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"{factionNames.arraySize} faction(s) selected as player-controlled.", MessageType.None);
                    }
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Visual Settings Section (collapsed by default)
            _showVisualSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualSettings, "Visual Settings (Advanced)");
            if (_showVisualSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Selection Rectangle", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showSelectionRectangle, new GUIContent("Show Rectangle", "Show selection rectangle while dragging"));
                
                EditorGUI.BeginDisabledGroup(!_showSelectionRectangle.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_selectionBorderColor, new GUIContent("Border Color"));
                EditorGUILayout.PropertyField(_selectionFillColor, new GUIContent("Fill Color"));
                EditorGUILayout.PropertyField(_selectionBorderThickness, new GUIContent("Border Thickness"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("Move Indicator", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showMoveIndicator, new GUIContent("Show Indicator", "Show visual feedback when giving move commands"));
                
                EditorGUI.BeginDisabledGroup(!_showMoveIndicator.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_moveIndicatorColor, new GUIContent("Indicator Color"));
                EditorGUILayout.PropertyField(_moveIndicatorDuration, new GUIContent("Indicator Duration"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("Selection Highlight", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_showSelectionHighlight, new GUIContent("Show Highlight", "Show highlight ring under selected agents"));
                
                EditorGUI.BeginDisabledGroup(!_showSelectionHighlight.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_selectionHighlightColor, new GUIContent("Highlight Color"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("Editor Performance", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_enableGizmos, new GUIContent("Enable Gizmos", "Disable for better Editor FPS with large crowds"));
                EditorGUILayout.HelpBox("Gizmos only affect Editor performance, not builds.", MessageType.Info);

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Destination Indicators Section
            _showDestinationIndicators = EditorGUILayout.BeginFoldoutHeaderGroup(_showDestinationIndicators, "Destination Indicators");
            if (_showDestinationIndicators)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("Shows a single indicator at the movement destination for all selected agents. Efficient - only one indicator regardless of agent count.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_showDestinationIndicator, new GUIContent("Show Destination Indicator"));
                
                EditorGUI.BeginDisabledGroup(!_showDestinationIndicator.boolValue);
                EditorGUILayout.PropertyField(_destinationIndicatorPrefab, new GUIContent("Indicator Prefab", "Can be UI (Image/RawImage), VFX Graph, or ParticleSystem"));
                EditorGUILayout.PropertyField(_destinationIndicatorOffset, new GUIContent("Position Offset"));
                EditorGUILayout.PropertyField(_destinationReachedThreshold, new GUIContent("Reached Threshold", "Hide indicator when agents are within this distance"));
                EditorGUILayout.PropertyField(_hideIndicatorsOnFirstArrival, new GUIContent("Hide On First Arrival", "Hide indicators when the first agent arrives, rather than waiting for all"));
                EditorGUILayout.PropertyField(_indicatorFadeOutDuration, new GUIContent("Fade Out Duration", "Fade out indicators over time instead of instant hide. Set to 0 for instant."));
                EditorGUILayout.PropertyField(_destinationIndicatorParent, new GUIContent("Parent Transform", "For UI indicators, set to World Space Canvas"));
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Direction Indicator (Optional)", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Shows a direction indicator pointing from selected agents toward the destination.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_showDirectionIndicator, new GUIContent("Show Direction Indicator"));
                
                EditorGUI.BeginDisabledGroup(!_showDirectionIndicator.boolValue);
                EditorGUILayout.PropertyField(_directionIndicatorPrefab, new GUIContent("Indicator Prefab", "Arrow, line, or prefab with LineRenderer component"));
                
                // LineRenderer Mode Settings
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("LineRenderer Mode", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_useLineRenderer, new GUIContent("Use LineRenderer", "Draw line to destination using LineRenderer component from prefab"));
                
                if (_useLineRenderer.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_drawPathInsteadOfDirection, new GUIContent("Draw Path", "Draw actual NavMesh path instead of straight line"));
                    EditorGUILayout.PropertyField(_lineHeightOffset, new GUIContent("Line Height Offset", "Height above ground for the line"));
                    
                    if (_drawPathInsteadOfDirection.boolValue)
                    {
                        EditorGUILayout.PropertyField(_pathUpdateInterval, new GUIContent("Path Update Interval", "How often to recalculate path (seconds)"));
                        EditorGUILayout.PropertyField(_pathInterpolationSpeed, new GUIContent("Interpolation Speed", "Smoothness of path transitions. Higher = snappier, lower = smoother. 0 = instant."));
                    }
                    EditorGUI.indentLevel--;
                }
                
                // Only show these settings when NOT using LineRenderer mode
                if (!_useLineRenderer.boolValue)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Indicator Positioning", EditorStyles.miniLabel);
                    EditorGUILayout.PropertyField(_directionIndicatorOrientation, new GUIContent("Orientation Mode", 
                        "Object3D: Z+ points toward destination (3D meshes, particles, VFX)\n" +
                        "FlatOnGround: Lies flat on XZ plane (UI RawImage, decals)\n" +
                        "Billboard: Always faces camera"));
                    EditorGUILayout.PropertyField(_directionIndicatorOffset, new GUIContent("Position Offset"));
                    EditorGUILayout.PropertyField(_directionIndicatorRotationOffset, new GUIContent("Rotation Offset", "Euler angles to fine-tune the orientation"));
                    
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Dynamic Distance (scales with selection size)", EditorStyles.miniLabel);
                    EditorGUILayout.PropertyField(_directionIndicatorMinDistance, new GUIContent("Min Distance", "Minimum distance from selection center"));
                    EditorGUILayout.PropertyField(_directionIndicatorDistancePerAgent, new GUIContent("Distance Per Agent", "Distance added per selected agent (e.g., 0.25 = 100 agents → 25 units)"));
                    EditorGUILayout.PropertyField(_directionIndicatorMaxDistance, new GUIContent("Max Distance", "Maximum distance cap"));
                }
                
                EditorGUILayout.PropertyField(_directionIndicatorParent, new GUIContent("Parent Transform"));
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Fog of War Visibility", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_renderAboveFog, new GUIContent("Render Above Fog", "Ensure indicators render above the fog of war plane (render queue 4000)"));
                
                EditorGUI.BeginDisabledGroup(!_renderAboveFog.boolValue);
                EditorGUILayout.PropertyField(_indicatorSortingOrder, new GUIContent("Sorting Order", "Sorting order for sprites and UI canvas elements"));
                EditorGUI.EndDisabledGroup();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Selection Feedback Section
            _showSelectionFeedback = EditorGUILayout.BeginFoldoutHeaderGroup(_showSelectionFeedback, "Selection Feedback");
            if (_showSelectionFeedback)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Shader-Based Highlight (Efficient)", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Uses MaterialPropertyBlock to highlight selected agents. Very efficient for thousands of agents. Requires shader support for the specified properties.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_enableShaderHighlight, new GUIContent("Enable Shader Highlight"));
                
                EditorGUI.BeginDisabledGroup(!_enableShaderHighlight.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_highlightColorProperty, new GUIContent("Color Property", "Shader property name for highlight color"));
                EditorGUILayout.PropertyField(_highlightIntensityProperty, new GUIContent("Intensity Property", "Shader property name for highlight intensity"));
                EditorGUILayout.PropertyField(_shaderHighlightColor, new GUIContent("Highlight Color"));
                EditorGUILayout.PropertyField(_shaderHighlightIntensity, new GUIContent("Highlight Intensity"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("Prefab Indicators (for VFX)", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Spawns prefabs for selected agents. Use for VFX like selection rings, particles, etc. Limited to prevent performance issues.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_selectionIndicatorPrefab, new GUIContent("Indicator Prefab", "Optional prefab to spawn for each selected agent"));
                
                EditorGUI.BeginDisabledGroup(_selectionIndicatorPrefab.objectReferenceValue == null);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_indicatorOffset, new GUIContent("Position Offset", "Offset from agent position"));
                EditorGUILayout.PropertyField(_maxIndicatorPrefabs, new GUIContent("Max Indicators", "Maximum prefabs to spawn (prevents performance issues)"));
                EditorGUILayout.PropertyField(_indicatorParent, new GUIContent("Parent Transform", "Parent for spawned indicators"));
                EditorGUILayout.PropertyField(_autoUpdateIndicatorPositions, new GUIContent("Auto-Update Positions", "Update indicator positions each frame"));
                EditorGUILayout.PropertyField(_autoScaleIndicatorToAgent, new GUIContent("Auto-Scale to Agent", "Scale indicators to match agent bounding radius. Vehicles use vehicleBoundingRadius, humanoids use navigation agent radius."));
                EditorGUILayout.PropertyField(_indicatorAtGroundForFlying, new GUIContent("Ground-Level for Flying", "Project indicators to ground level beneath flying agents instead of at flight altitude."));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Attack Target Feedback Section
            _showAttackTargetFeedback = EditorGUILayout.BeginFoldoutHeaderGroup(_showAttackTargetFeedback, "Attack Target Feedback");
            if (_showAttackTargetFeedback)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("Visual feedback shown on enemy targets when issuing attack commands. Supports shader highlight and/or indicator prefab with flash/fade effects.", MessageType.Info);

                EditorGUILayout.PropertyField(_enableAttackTargetFeedback, new GUIContent("Enable Attack Target Feedback", "Master toggle for attack target visual feedback"));

                EditorGUI.BeginDisabledGroup(!_enableAttackTargetFeedback.boolValue);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Shader-Based Highlight", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_enableAttackTargetShaderHighlight, new GUIContent("Enable Shader Highlight"));

                EditorGUI.BeginDisabledGroup(!_enableAttackTargetShaderHighlight.boolValue);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_attackTargetHighlightColor, new GUIContent("Highlight Color", "Color applied to the attack target"));
                EditorGUILayout.PropertyField(_attackTargetHighlightIntensity, new GUIContent("Highlight Intensity", "Intensity of the highlight effect"));
                EditorGUILayout.PropertyField(_attackTargetColorProperty, new GUIContent("Color Property", "Shader property name for highlight color"));
                EditorGUILayout.PropertyField(_attackTargetIntensityProperty, new GUIContent("Intensity Property", "Shader property name for highlight intensity"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Indicator Prefab", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_attackTargetIndicatorPrefab, new GUIContent("Indicator Prefab", "Prefab spawned on the attack target (e.g., crosshair, circle)"));

                EditorGUI.BeginDisabledGroup(_attackTargetIndicatorPrefab.objectReferenceValue == null);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_attackTargetIndicatorOffset, new GUIContent("Position Offset", "Offset from target position"));
                EditorGUILayout.PropertyField(_attackTargetIndicatorParent, new GUIContent("Parent Transform", "Optional parent for the indicator"));
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Behavior Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_attackTargetFeedbackMode, new GUIContent("Feedback Mode", "Persistent: stays visible, FlashAndFade: pulses and fades, FlashOnly: quick flash"));
                EditorGUILayout.PropertyField(_attackTargetFeedbackDuration, new GUIContent("Duration", "How long the feedback lasts (for fading modes)"));
                EditorGUILayout.PropertyField(_attackTargetFlashSpeed, new GUIContent("Flash Speed", "Speed of the flash pulse effect"));
                EditorGUILayout.PropertyField(_clearFeedbackOnTargetDeath, new GUIContent("Clear On Target Death", "Automatically clear feedback when target dies"));
                EditorGUILayout.PropertyField(_autoUpdateAttackTargetPosition, new GUIContent("Follow Target", "Update indicator position as target moves"));

                EditorGUI.EndDisabledGroup();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Events Section
            _showEvents = EditorGUILayout.BeginFoldoutHeaderGroup(_showEvents, "Events (Designer Hooks)");
            if (_showEvents)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("Use these events to hook up UI updates, sound effects, VFX, or custom logic when selection changes.", MessageType.Info);
                
                EditorGUILayout.PropertyField(_onSelectionChanged, new GUIContent("On Selection Changed", "Called when selection changes - provides detailed change info"));
                EditorGUILayout.PropertyField(_onAgentSelected, new GUIContent("On Agent Selected", "Called when a single agent is added to selection"));
                EditorGUILayout.PropertyField(_onAgentDeselected, new GUIContent("On Agent Deselected", "Called when a single agent is removed from selection"));
                EditorGUILayout.PropertyField(_onSelectionCleared, new GUIContent("On Selection Cleared", "Called when all agents are deselected"));
                EditorGUILayout.PropertyField(_onMoveCommandIssued, new GUIContent("On Move Command", "Called when a move command is issued"));
                EditorGUILayout.PropertyField(_onAttackCommandIssued, new GUIContent("On Attack Command", "Called when an attack command is issued (targetIndex, attackerIndices)"));

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Network Events Section (C# events for multiplayer)
            DrawNetworkEventsSection();

            // Runtime Info (only in play mode)
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);
                
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.IntField("Selected Agents", controller.SelectedCount);
                EditorGUILayout.Toggle("Is Dragging", controller.IsDragging);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(3);
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Selection"))
                {
                    controller.ClearSelection();
                }
                if (GUILayout.Button("Refresh Indicators"))
                {
                    controller.RefreshSelectionIndicators();
                }
                EditorGUILayout.EndHorizontal();
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws the Network Events section showing C# events for multiplayer integration.
        /// </summary>
        private void DrawNetworkEventsSection()
        {
            _showNetworkEvents = EditorGUILayout.BeginFoldoutHeaderGroup(_showNetworkEvents, "Network Events (Multiplayer)");
            if (_showNetworkEvents)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox(
                    "These C# events are fired for network synchronization. Subscribe to them in your network authority implementation.\n\n" +
                    "Use NetworkAuthorityProvider.SetAuthority() to register your INetworkCommandAuthority.", 
                    MessageType.Info);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Available Events", EditorStyles.miniBoldLabel);

                // Event display boxes
                DrawNetworkEventInfo("NetworkMoveCommandIssued", "CommandEventData", 
                    "Fired when move command is issued. Contains: positions, agent network IDs, formation type, sequence number.");
                
                DrawNetworkEventInfo("NetworkAttackCommandIssued", "CommandEventData", 
                    "Fired when attack command is issued. Contains: attacker network IDs, target network ID, sequence number.");
                
                DrawNetworkEventInfo("NetworkStopCommandIssued", "CommandEventData", 
                    "Fired when stop command is issued. Contains: agent network IDs, sequence number.");

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Integration", EditorStyles.miniBoldLabel);
                
                var style = new GUIStyle(EditorStyles.helpBox);
                style.richText = true;
                EditorGUILayout.TextArea(
                    "<b>Example Usage:</b>\n" +
                    "rtsController.NetworkMoveCommandIssued += OnMoveCommand;\n\n" +
                    "void OnMoveCommand(CommandEventData data) {\n" +
                    "    // Send to server via your network solution\n" +
                    "    MyNetwork.Send(data);\n" +
                    "}", 
                    style);

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawNetworkEventInfo(string eventName, string dataType, string description)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{eventName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Data Type: {dataType}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }
    }
}
