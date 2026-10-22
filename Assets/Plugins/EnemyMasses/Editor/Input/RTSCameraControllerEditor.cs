using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for RTSCameraController with preset-based configuration.
    /// </summary>
    [CustomEditor(typeof(RTSCameraController))]
    [CanEditMultipleObjects]
    public class RTSCameraControllerEditor : UnityEditor.Editor
    {
        #region Serialized Properties
        
        // Presets
        private SerializedProperty _cameraPresets;
        private SerializedProperty _activePresetIndex;
        private SerializedProperty _controlActiveOnStart;
        private SerializedProperty _enablePresetTransitions;
        private SerializedProperty _presetTransitionDuration;
        private SerializedProperty _presetTransitionCurve;
        
        // Movement
        private SerializedProperty _moveSpeed;
        private SerializedProperty _fastMoveSpeed;
        private SerializedProperty _moveAcceleration;
        private SerializedProperty _moveDeceleration;
        private SerializedProperty _smoothMovement;
        
        // Edge Panning
        private SerializedProperty _enableEdgePan;
        private SerializedProperty _edgePanBorder;
        private SerializedProperty _edgePanSpeedMultiplier;
        private SerializedProperty _disableEdgePanOutsideWindow;
        
        // Drag Panning
        private SerializedProperty _enableDragPan;
        private SerializedProperty _dragPanInput;
        private SerializedProperty _invertDragPan;
        private SerializedProperty _dragPanSensitivity;
        
        // Keyboard
        private SerializedProperty _enableKeyboardPan;
        
        // Zoom
        private SerializedProperty _enableZoom;
        private SerializedProperty _zoomSpeed;
        private SerializedProperty _minZoomDistance;
        private SerializedProperty _maxZoomDistance;
        private SerializedProperty _smoothZoom;
        private SerializedProperty _zoomSmoothSpeed;
        private SerializedProperty _zoomToCursor;
        
        // Rotation
        private SerializedProperty _enableRotation;
        private SerializedProperty _rotationInput;
        private SerializedProperty _rotationSpeed;
        private SerializedProperty _rotateWithoutButton;
        private SerializedProperty _enableTilt;
        private SerializedProperty _minTiltAngle;
        private SerializedProperty _maxTiltAngle;
        private SerializedProperty _smoothRotation;
        private SerializedProperty _rotationSmoothSpeed;
        
        // Bounds
        private SerializedProperty _enableBounds;
        private SerializedProperty _minX;
        private SerializedProperty _maxX;
        private SerializedProperty _minZ;
        private SerializedProperty _maxZ;
        private SerializedProperty _boundsMeshRenderer;
        
        // Bookmarks
        private SerializedProperty _enableBookmarks;
        private SerializedProperty _bookmarkSlots;
        private SerializedProperty _bookmarkSlotInputs;
        private SerializedProperty _bookmarkSaveModifier;
        private SerializedProperty _smoothBookmarkTransition;
        private SerializedProperty _bookmarkTransitionSpeed;
        
        // Input Actions
        private SerializedProperty _moveForwardInput;
        private SerializedProperty _moveBackwardInput;
        private SerializedProperty _moveLeftInput;
        private SerializedProperty _moveRightInput;
        private SerializedProperty _sprintInput;
        
        // Focus/Follow
        private SerializedProperty _enableFocus;
        private SerializedProperty _smoothFocusTransition;
        private SerializedProperty _focusTransitionSpeed;
        private SerializedProperty _followTarget;
        private SerializedProperty _followMode;
        private SerializedProperty _followSmoothness;
        private SerializedProperty _followTargetOffset;
        private SerializedProperty _followOffsetInTargetSpace;
        private SerializedProperty _enableLookahead;
        private SerializedProperty _lookaheadTime;
        private SerializedProperty _lookaheadSmoothTime;
        private SerializedProperty _maxLookaheadDistance;
        private SerializedProperty _followUseTargetHeight;
        private SerializedProperty _followTargetHeightOffset;

        // Auto-Orbit
        private SerializedProperty _enableAutoOrbit;
        private SerializedProperty _autoOrbitSpeed;
        private SerializedProperty _autoOrbitDeadZone;
        private SerializedProperty _autoOrbitYawOffset;
        private SerializedProperty _autoOrbitReturnDelay;
        private SerializedProperty _autoOrbitReturnToTargetForward;
        
        // Advanced
        private SerializedProperty _useUnscaledTime;
        private SerializedProperty _groundLayer;
        private SerializedProperty _heightAboveGround;
        private SerializedProperty _terrainFollowing;

        // Camera Collision
        private SerializedProperty _enableCameraCollision;
        private SerializedProperty _cameraCollisionLayers;
        private SerializedProperty _cameraCollisionRadius;
        private SerializedProperty _cameraCollisionBuffer;
        private SerializedProperty _cameraCollisionIgnoreTriggers;
        private SerializedProperty _enableLineOfSightGroundReposition;
        private SerializedProperty _lineOfSightGroundBuffer;
        
        // Initial Setup
        private SerializedProperty _initialZoom;
        private SerializedProperty _initialPitch;
        private SerializedProperty _initialYaw;
        
        #endregion
        
        #region Foldout States
        
        private static bool _showPresetTransition = true;
        private static bool _showMovement = false;
        private static bool _showEdgePan = false;
        private static bool _showDragPan = false;
        private static bool _showZoom = false;
        private static bool _showRotation = false;
        private static bool _showInitialSetup = false;
        private static bool _showBounds = false;
        private static bool _showBookmarks = false;
        private static bool _showInputActions = false;
        private static bool _showFollow = false;
        private static bool _showAdvanced = false;
        private static bool _showCollision = false;
        private static bool _showAutoOrbit = false;
        
        #endregion
        
        #region Styles
        
        private GUIStyle _headerStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _presetButtonStyle;
        private GUIStyle _infoBoxStyle;
        
        #endregion
        
        #region Constants
        
        private const string PresetInfoMessage =
            "Camera Presets let you define different camera configurations and switch between them.\n\n" +
            "• Click Add → Create to generate a preset (Classic RTS, Third Person, etc.)\n" +
            "• Or click Add → Empty Slot, then drag your own preset asset\n" +
            "• Check the Active checkbox to set the default preset\n" +
            "• Enable Transitions for smooth animated preset switching\n\n" +
            "At runtime, call SwitchToPreset(index) or SwitchToPreset(preset) to change presets.";
        
        #endregion
        
        private void OnEnable()
        {
            // Presets
            _cameraPresets = serializedObject.FindProperty("cameraPresets");
            _activePresetIndex = serializedObject.FindProperty("activePresetIndex");
            _controlActiveOnStart = serializedObject.FindProperty("controlActiveOnStart");
            _enablePresetTransitions = serializedObject.FindProperty("enablePresetTransitions");
            _presetTransitionDuration = serializedObject.FindProperty("presetTransitionDuration");
            _presetTransitionCurve = serializedObject.FindProperty("presetTransitionCurve");
            
            // Movement
            _moveSpeed = serializedObject.FindProperty("moveSpeed");
            _fastMoveSpeed = serializedObject.FindProperty("fastMoveSpeed");
            _moveAcceleration = serializedObject.FindProperty("moveAcceleration");
            _moveDeceleration = serializedObject.FindProperty("moveDeceleration");
            _smoothMovement = serializedObject.FindProperty("smoothMovement");
            
            // Edge Panning
            _enableEdgePan = serializedObject.FindProperty("enableEdgePan");
            _edgePanBorder = serializedObject.FindProperty("edgePanBorder");
            _edgePanSpeedMultiplier = serializedObject.FindProperty("edgePanSpeedMultiplier");
            _disableEdgePanOutsideWindow = serializedObject.FindProperty("disableEdgePanOutsideWindow");
            
            // Drag Panning
            _enableDragPan = serializedObject.FindProperty("enableDragPan");
            _dragPanInput = serializedObject.FindProperty("dragPanInput");
            _invertDragPan = serializedObject.FindProperty("invertDragPan");
            _dragPanSensitivity = serializedObject.FindProperty("dragPanSensitivity");
            
            // Keyboard
            _enableKeyboardPan = serializedObject.FindProperty("enableKeyboardPan");
            
            // Zoom
            _enableZoom = serializedObject.FindProperty("enableZoom");
            _zoomSpeed = serializedObject.FindProperty("zoomSpeed");
            _minZoomDistance = serializedObject.FindProperty("minZoomDistance");
            _maxZoomDistance = serializedObject.FindProperty("maxZoomDistance");
            _smoothZoom = serializedObject.FindProperty("smoothZoom");
            _zoomSmoothSpeed = serializedObject.FindProperty("zoomSmoothSpeed");
            _zoomToCursor = serializedObject.FindProperty("zoomToCursor");
            
            // Rotation
            _enableRotation = serializedObject.FindProperty("enableRotation");
            _rotationInput = serializedObject.FindProperty("rotationInput");
            _rotationSpeed = serializedObject.FindProperty("rotationSpeed");
            _rotateWithoutButton = serializedObject.FindProperty("rotateWithoutButton");
            _enableTilt = serializedObject.FindProperty("enableTilt");
            _minTiltAngle = serializedObject.FindProperty("minTiltAngle");
            _maxTiltAngle = serializedObject.FindProperty("maxTiltAngle");
            _smoothRotation = serializedObject.FindProperty("smoothRotation");
            _rotationSmoothSpeed = serializedObject.FindProperty("rotationSmoothSpeed");
            
            // Bounds
            _enableBounds = serializedObject.FindProperty("enableBounds");
            _minX = serializedObject.FindProperty("minX");
            _maxX = serializedObject.FindProperty("maxX");
            _minZ = serializedObject.FindProperty("minZ");
            _maxZ = serializedObject.FindProperty("maxZ");
            _boundsMeshRenderer = serializedObject.FindProperty("boundsMeshRenderer");
            
            // Bookmarks
            _enableBookmarks = serializedObject.FindProperty("enableBookmarks");
            _bookmarkSlots = serializedObject.FindProperty("bookmarkSlots");
            _bookmarkSlotInputs = serializedObject.FindProperty("bookmarkSlotInputs");
            _bookmarkSaveModifier = serializedObject.FindProperty("bookmarkSaveModifier");
            _smoothBookmarkTransition = serializedObject.FindProperty("smoothBookmarkTransition");
            _bookmarkTransitionSpeed = serializedObject.FindProperty("bookmarkTransitionSpeed");
            
            // Input Actions
            _moveForwardInput = serializedObject.FindProperty("moveForwardInput");
            _moveBackwardInput = serializedObject.FindProperty("moveBackwardInput");
            _moveLeftInput = serializedObject.FindProperty("moveLeftInput");
            _moveRightInput = serializedObject.FindProperty("moveRightInput");
            _sprintInput = serializedObject.FindProperty("sprintInput");
            
            // Focus/Follow
            _enableFocus = serializedObject.FindProperty("enableFocus");
            _smoothFocusTransition = serializedObject.FindProperty("smoothFocusTransition");
            _focusTransitionSpeed = serializedObject.FindProperty("focusTransitionSpeed");
            _followTarget = serializedObject.FindProperty("followTarget");
            _followMode = serializedObject.FindProperty("followMode");
            _followSmoothness = serializedObject.FindProperty("followSmoothness");
            _followTargetOffset = serializedObject.FindProperty("followTargetOffset");
            _followOffsetInTargetSpace = serializedObject.FindProperty("followOffsetInTargetSpace");
            _enableLookahead = serializedObject.FindProperty("enableLookahead");
            _lookaheadTime = serializedObject.FindProperty("lookaheadTime");
            _lookaheadSmoothTime = serializedObject.FindProperty("lookaheadSmoothTime");
            _maxLookaheadDistance = serializedObject.FindProperty("maxLookaheadDistance");
            _followUseTargetHeight = serializedObject.FindProperty("followUseTargetHeight");
            _followTargetHeightOffset = serializedObject.FindProperty("followTargetHeightOffset");

            // Auto-Orbit
            _enableAutoOrbit = serializedObject.FindProperty("enableAutoOrbit");
            _autoOrbitSpeed = serializedObject.FindProperty("autoOrbitSpeed");
            _autoOrbitDeadZone = serializedObject.FindProperty("autoOrbitDeadZone");
            _autoOrbitYawOffset = serializedObject.FindProperty("autoOrbitYawOffset");
            _autoOrbitReturnDelay = serializedObject.FindProperty("autoOrbitReturnDelay");
            _autoOrbitReturnToTargetForward = serializedObject.FindProperty("autoOrbitReturnToTargetForward");
            
            // Advanced
            _useUnscaledTime = serializedObject.FindProperty("useUnscaledTime");
            _groundLayer = serializedObject.FindProperty("groundLayer");
            _heightAboveGround = serializedObject.FindProperty("heightAboveGround");
            _terrainFollowing = serializedObject.FindProperty("terrainFollowing");

            // Camera Collision
            _enableCameraCollision = serializedObject.FindProperty("enableCameraCollision");
            _cameraCollisionLayers = serializedObject.FindProperty("cameraCollisionLayers");
            _cameraCollisionRadius = serializedObject.FindProperty("cameraCollisionRadius");
            _cameraCollisionBuffer = serializedObject.FindProperty("cameraCollisionBuffer");
            _cameraCollisionIgnoreTriggers = serializedObject.FindProperty("cameraCollisionIgnoreTriggers");
            _enableLineOfSightGroundReposition = serializedObject.FindProperty("enableLineOfSightGroundReposition");
            _lineOfSightGroundBuffer = serializedObject.FindProperty("lineOfSightGroundBuffer");
            
            // Initial Setup
            _initialZoom = serializedObject.FindProperty("initialZoom");
            _initialPitch = serializedObject.FindProperty("initialPitch");
            _initialYaw = serializedObject.FindProperty("initialYaw");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            InitStyles();
            
            DrawCameraHeader();
            DrawPresetsSection();
            
            EditorGUILayout.Space(10);
            
            DrawInitialSetupSection();
            DrawMovementSection();
            DrawEdgePanSection();
            DrawDragPanSection();
            DrawZoomSection();
            DrawRotationSection();
            DrawBoundsSection();
            DrawBookmarksSection();
            DrawInputActionsSection();
            DrawFollowSection();
            DrawAutoOrbitSection();
            DrawCameraCollisionSection();
            DrawAdvancedSection();
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 10, 10)
                };
            }

            if (_presetButtonStyle == null)
            {
                _presetButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 24,
                    fontStyle = FontStyle.Bold
                };
            }
            
            if (_infoBoxStyle == null)
            {
                _infoBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    richText = true,
                    fontSize = 11,
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }
        }
        
        private void DrawCameraHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("RTS Camera Controller", _headerStyle);
            EditorGUILayout.Space(5);
            
            // Control Active On Start - important for GC2 integration
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_controlActiveOnStart, new GUIContent("Control Active On Start", 
                "If disabled, the RTS Camera Controller will not control the camera on start. " +
                "Useful when integrating with Game Creator 2 or other camera systems."));
            
            if (!_controlActiveOnStart.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Camera control is disabled on start. The RTS Camera will not control the MainCamera until " +
                    "SetControlActive(true) is called or 'Transition to RTS Camera' instruction is used.",
                    MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }
        
        private void DrawPresetsSection()
        {
            EditorGUILayout.Space(8);
            
            // Clean preset list - click to select, that's it
            Rect presetsBoxRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header with add button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            // Minimal add button
            if (GUILayout.Button("Add", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                ShowAddPresetMenu();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(4);
            
            // Preset list - simple clickable rows
            if (_cameraPresets.arraySize == 0)
            {
                // Info box with usage guide
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(EditorGUIUtility.IconContent("_Help"), GUILayout.Width(18), GUILayout.Height(18));
                EditorGUILayout.LabelField("How to Use Camera Presets", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(PresetInfoMessage, _infoBoxStyle);
                EditorGUILayout.Space(4);
            }
            else
            {
                // Column headers
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Active", EditorStyles.miniLabel, GUILayout.Width(40));
                GUILayout.Label("Preset", EditorStyles.miniLabel);
                GUILayout.Space(22); // Space for remove button
                EditorGUILayout.EndHorizontal();
                
                int currentIndex = _activePresetIndex.intValue;
                
                for (int i = 0; i < _cameraPresets.arraySize; i++)
                {
                    DrawPresetRow(i, i == currentIndex);
                }
                
                // Show description of active preset
                if (currentIndex >= 0 && currentIndex < _cameraPresets.arraySize)
                {
                    var activePresetProp = _cameraPresets.GetArrayElementAtIndex(currentIndex);
                    var activePreset = activePresetProp.objectReferenceValue as RTSCameraPreset;
                    if (activePreset != null && !string.IsNullOrEmpty(activePreset.description))
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField(activePreset.description, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            
            // Drag and drop area hint
            EditorGUILayout.Space(2);
            Rect dropHintRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            
            // Handle drag and drop for entire presets box
            HandlePresetDragAndDrop(presetsBoxRect, dropHintRect);
            
            EditorGUILayout.EndVertical();
            
            // Transitions - collapsed by default, very minimal
            EditorGUILayout.Space(4);
            _showPresetTransition = EditorGUILayout.Foldout(_showPresetTransition, "Transitions", true);
            
            if (_showPresetTransition)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_enablePresetTransitions, new GUIContent("Animate"));
                
                if (_enablePresetTransitions.boolValue)
                {
                    EditorGUILayout.PropertyField(_presetTransitionDuration, new GUIContent("Duration"));
                    EditorGUILayout.PropertyField(_presetTransitionCurve, new GUIContent("Curve"));
                }
                EditorGUI.indentLevel--;
            }
        }
        
        private void DrawPresetRow(int index, bool isActive)
        {
            var presetProp = _cameraPresets.GetArrayElementAtIndex(index);
            var preset = presetProp.objectReferenceValue as RTSCameraPreset;
            
            // Row background
            Rect rowRect = EditorGUILayout.BeginHorizontal();
            
            // Selection highlight
            if (isActive)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.49f, 0.91f, 0.3f));
            }
            
            // Radio button style - click to select
            bool newActive = GUILayout.Toggle(isActive, "", GUILayout.Width(16));
            if (newActive && !isActive && preset != null)
            {
                _activePresetIndex.intValue = index;
                ApplyPresetToTargets(index, preset);
            }
            
            // Preset object field (for drag-drop)
            EditorGUI.BeginChangeCheck();
            var newPreset = EditorGUILayout.ObjectField(preset, typeof(RTSCameraPreset), false) as RTSCameraPreset;
            if (EditorGUI.EndChangeCheck())
            {
                presetProp.objectReferenceValue = newPreset;
            }
            
            // Remove button (subtle)
            if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                RemovePresetAtIndex(index);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void ShowAddPresetMenu()
        {
            GenericMenu menu = new GenericMenu();
            
            // Add empty slot option
            menu.AddItem(new GUIContent("Empty Slot (drag your preset)"), false, () =>
            {
                _cameraPresets.InsertArrayElementAtIndex(_cameraPresets.arraySize);
                _cameraPresets.GetArrayElementAtIndex(_cameraPresets.arraySize - 1).objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
            });
            
            menu.AddSeparator("");
            
            // Quick create options
            menu.AddItem(new GUIContent("Create/Classic RTS"), false, () => CreateAndAddPreset(RTSCameraPreset.CreateClassicRTS()));
            menu.AddItem(new GUIContent("Create/Modern RTS"), false, () => CreateAndAddPreset(RTSCameraPreset.CreateModernRTS()));
            menu.AddItem(new GUIContent("Create/Third Person"), false, () => CreateAndAddPreset(RTSCameraPreset.CreateThirdPerson()));
            menu.AddItem(new GUIContent("Create/Total War"), false, () => CreateAndAddPreset(RTSCameraPreset.CreateTotalWar()));
            menu.AddItem(new GUIContent("Create/Supreme Commander"), false, () => CreateAndAddPreset(RTSCameraPreset.CreateSupremeCommander()));
            menu.AddItem(new GUIContent("Create/Auto Orbit (Arkham-Style)"), false, () => CreateAndAddPreset(RTSCameraPreset.CreateAutoOrbit()));
            
            menu.ShowAsContext();
        }
        
        private void HandlePresetDragAndDrop(Rect dropAreaRect, Rect hintRect)
        {
            Event evt = Event.current;
            
            // Check if we're dragging RTSCameraPreset assets
            bool isDraggingPresets = false;
            if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is RTSCameraPreset)
                    {
                        isDraggingPresets = true;
                        break;
                    }
                }
            }
            
            // Draw drop hint
            if (isDraggingPresets && dropAreaRect.Contains(evt.mousePosition))
            {
                // Highlight the drop area
                EditorGUI.DrawRect(dropAreaRect, new Color(0.24f, 0.49f, 0.91f, 0.2f));
                GUI.Label(hintRect, "Drop to add preset(s)", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                GUI.Label(hintRect, "Drag & drop presets here", EditorStyles.centeredGreyMiniLabel);
            }
            
            // Handle drag and drop
            if (!dropAreaRect.Contains(evt.mousePosition))
                return;
                
            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (isDraggingPresets)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;
                    
                case EventType.DragPerform:
                    if (isDraggingPresets)
                    {
                        DragAndDrop.AcceptDrag();
                        
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is RTSCameraPreset preset)
                            {
                                // Check if preset is already in the list
                                bool alreadyExists = false;
                                for (int i = 0; i < _cameraPresets.arraySize; i++)
                                {
                                    if (_cameraPresets.GetArrayElementAtIndex(i).objectReferenceValue == preset)
                                    {
                                        alreadyExists = true;
                                        break;
                                    }
                                }
                                
                                if (!alreadyExists)
                                {
                                    int newIndex = _cameraPresets.arraySize;
                                    _cameraPresets.InsertArrayElementAtIndex(newIndex);
                                    _cameraPresets.GetArrayElementAtIndex(newIndex).objectReferenceValue = preset;
                                    
                                    // If this is the first preset, make it active
                                    if (newIndex == 0)
                                    {
                                        _activePresetIndex.intValue = 0;
                                    }
                                }
                            }
                        }
                        
                        serializedObject.ApplyModifiedProperties();
                        evt.Use();
                    }
                    break;
            }
        }
        
        private void RemovePresetAtIndex(int index)
        {
            var presetProp = _cameraPresets.GetArrayElementAtIndex(index);
            
            // Clear object reference first if it exists
            if (presetProp.objectReferenceValue != null)
            {
                presetProp.objectReferenceValue = null;
            }
            
            _cameraPresets.DeleteArrayElementAtIndex(index);
            
            // Adjust active index
            if (_activePresetIndex.intValue >= _cameraPresets.arraySize)
            {
                _activePresetIndex.intValue = Mathf.Max(0, _cameraPresets.arraySize - 1);
            }
            else if (_activePresetIndex.intValue > index)
            {
                _activePresetIndex.intValue--;
            }
        }
        
        private void ApplyPresetToTargets(int index, RTSCameraPreset preset)
        {
            if (preset == null) return;
            
            if (Application.isPlaying)
            {
                foreach (var obj in targets)
                {
                    var controller = obj as RTSCameraController;
                    if (controller != null)
                    {
                        controller.TransitionToPreset(index);
                    }
                }
            }
            else
            {
                foreach (var obj in targets)
                {
                    var controller = obj as RTSCameraController;
                    if (controller != null)
                    {
                        controller.ApplyPreset(preset);
                    }
                }
            }
        }
        
        private void CreateAndAddPreset(RTSCameraPreset preset)
        {
            // Get a path to save the preset
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Camera Preset",
                preset.presetName.Replace(" ", "") + "Preset",
                "asset",
                "Choose a location to save the preset");
            
            if (string.IsNullOrEmpty(path))
            {
                DestroyImmediate(preset);
                return;
            }
            
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            
            // Add to the list (but don't auto-activate)
            int newIndex = _cameraPresets.arraySize;
            _cameraPresets.InsertArrayElementAtIndex(newIndex);
            _cameraPresets.GetArrayElementAtIndex(newIndex).objectReferenceValue = preset;
            
            serializedObject.ApplyModifiedProperties();
            
            EditorGUIUtility.PingObject(preset);
        }
        
        private void DrawInitialSetupSection()
        {
            _showInitialSetup = EditorGUILayout.BeginFoldoutHeaderGroup(_showInitialSetup, "Initial Setup");
            
            if (_showInitialSetup)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.HelpBox(
                    "Configure the starting camera position. These values are overridden when a preset is applied.",
                    MessageType.Info);
                
                EditorGUILayout.PropertyField(_initialZoom, new GUIContent("Initial Zoom", "Starting camera distance/height"));
                EditorGUILayout.PropertyField(_initialPitch, new GUIContent("Initial Pitch", "Starting vertical angle (45 = typical RTS)"));
                EditorGUILayout.PropertyField(_initialYaw, new GUIContent("Initial Yaw", "Starting horizontal rotation"));
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawMovementSection()
        {
            _showMovement = EditorGUILayout.BeginFoldoutHeaderGroup(_showMovement, "Movement Settings");
            
            if (_showMovement)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.HelpBox("Movement settings can be configured per-preset or overridden here.", MessageType.None);
                
                EditorGUILayout.PropertyField(_moveSpeed, new GUIContent("Move Speed"));
                EditorGUILayout.PropertyField(_fastMoveSpeed, new GUIContent("Fast Speed (Shift)"));
                EditorGUILayout.PropertyField(_smoothMovement, new GUIContent("Smooth Movement"));
                
                if (_smoothMovement.boolValue)
                {
                    EditorGUILayout.PropertyField(_moveAcceleration, new GUIContent("Acceleration"));
                    EditorGUILayout.PropertyField(_moveDeceleration, new GUIContent("Deceleration"));
                }
                
                EditorGUILayout.PropertyField(_enableKeyboardPan, new GUIContent("Enable WASD/Arrows"));
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawEdgePanSection()
        {
            _showEdgePan = EditorGUILayout.BeginFoldoutHeaderGroup(_showEdgePan, "Edge Panning");
            
            if (_showEdgePan)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_enableEdgePan, new GUIContent("Enable Edge Pan"));
                
                if (_enableEdgePan.boolValue)
                {
                    EditorGUILayout.PropertyField(_edgePanBorder, new GUIContent("Edge Border (px)"));
                    EditorGUILayout.PropertyField(_edgePanSpeedMultiplier, new GUIContent("Speed Multiplier"));
                    EditorGUILayout.PropertyField(_disableEdgePanOutsideWindow, new GUIContent("Disable Outside Window"));
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawDragPanSection()
        {
            _showDragPan = EditorGUILayout.BeginFoldoutHeaderGroup(_showDragPan, "Drag Panning");
            
            if (_showDragPan)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_enableDragPan, new GUIContent("Enable Drag Pan"));
                
                if (_enableDragPan.boolValue)
                {
                    EditorGUILayout.PropertyField(_dragPanInput, new GUIContent("Drag Pan Input", "Input action binding for drag panning"));
                    EditorGUILayout.PropertyField(_invertDragPan, new GUIContent("Invert Direction"));
                    EditorGUILayout.PropertyField(_dragPanSensitivity, new GUIContent("Sensitivity"));
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawZoomSection()
        {
            _showZoom = EditorGUILayout.BeginFoldoutHeaderGroup(_showZoom, "Zoom Settings");
            
            if (_showZoom)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_enableZoom, new GUIContent("Enable Zoom"));
                
                if (_enableZoom.boolValue)
                {
                    EditorGUILayout.PropertyField(_zoomSpeed, new GUIContent("Zoom Speed"));
                    EditorGUILayout.PropertyField(_minZoomDistance, new GUIContent("Min Distance"));
                    EditorGUILayout.PropertyField(_maxZoomDistance, new GUIContent("Max Distance"));
                    EditorGUILayout.PropertyField(_smoothZoom, new GUIContent("Smooth Zoom"));
                    
                    if (_smoothZoom.boolValue)
                    {
                        EditorGUILayout.PropertyField(_zoomSmoothSpeed, new GUIContent("Smooth Speed"));
                    }
                    
                    EditorGUILayout.PropertyField(_zoomToCursor, new GUIContent("Zoom To Cursor"));
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawRotationSection()
        {
            _showRotation = EditorGUILayout.BeginFoldoutHeaderGroup(_showRotation, "Rotation Settings");
            
            if (_showRotation)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_enableRotation, new GUIContent("Enable Rotation"));
                
                if (_enableRotation.boolValue)
                {
                    EditorGUILayout.PropertyField(_rotationInput, new GUIContent("Rotation Input", "Input action binding for camera rotation"));
                    EditorGUILayout.PropertyField(_rotationSpeed, new GUIContent("Rotation Speed"));
                    EditorGUILayout.PropertyField(_rotateWithoutButton, new GUIContent("Rotate Without Button"));
                    EditorGUILayout.PropertyField(_enableTilt, new GUIContent("Enable Tilt (Pitch)"));
                    
                    if (_enableTilt.boolValue)
                    {
                        EditorGUILayout.PropertyField(_minTiltAngle, new GUIContent("Min Tilt Angle", "Negative values allow looking upward"));
                        EditorGUILayout.PropertyField(_maxTiltAngle, new GUIContent("Max Tilt Angle", "Negative values allow looking upward"));
                    }
                    
                    EditorGUILayout.PropertyField(_smoothRotation, new GUIContent("Smooth Rotation"));
                    
                    if (_smoothRotation.boolValue)
                    {
                        EditorGUILayout.PropertyField(_rotationSmoothSpeed, new GUIContent("Smooth Speed"));
                    }
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawBoundsSection()
        {
            _showBounds = EditorGUILayout.BeginFoldoutHeaderGroup(_showBounds, "Camera Bounds");
            
            if (_showBounds)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_enableBounds, new GUIContent("Enable Bounds"));
                
                if (_enableBounds.boolValue)
                {
                    EditorGUILayout.PropertyField(_boundsMeshRenderer, new GUIContent("Bounds Mesh Renderer"));

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(_minX, new GUIContent("Min X"));
                    EditorGUILayout.PropertyField(_maxX, new GUIContent("Max X"));
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(_minZ, new GUIContent("Min Z"));
                    EditorGUILayout.PropertyField(_maxZ, new GUIContent("Max Z"));
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Set From Terrain"))
                    {
                        SetBoundsFromTerrain();
                    }

                    using (new EditorGUI.DisabledScope(_boundsMeshRenderer.objectReferenceValue == null))
                    {
                        if (GUILayout.Button("Set From Mesh Renderer"))
                        {
                            SetBoundsFromMesh();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawBookmarksSection()
        {
            _showBookmarks = EditorGUILayout.BeginFoldoutHeaderGroup(_showBookmarks, "Camera Bookmarks");
            
            if (_showBookmarks)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_enableBookmarks, new GUIContent("Enable Bookmarks"));
                
                if (_enableBookmarks.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Press modifier+slot key to save camera position.\nPress slot key to jump to saved position.",
                        MessageType.Info);
                    
                    EditorGUILayout.PropertyField(_bookmarkSlots, new GUIContent("Number of Slots"));
                    EditorGUILayout.PropertyField(_bookmarkSaveModifier, new GUIContent("Save Modifier", "Input binding for the modifier key to save bookmarks. Default is Alt."));
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Bookmark Slot Keys", EditorStyles.boldLabel);
                    
                    if (_bookmarkSlotInputs != null && _bookmarkSlotInputs.isArray)
                    {
                        EditorGUI.indentLevel++;
                        int slotCount = Mathf.Min(_bookmarkSlots.intValue, _bookmarkSlotInputs.arraySize);
                        for (int i = 0; i < slotCount; i++)
                        {
                            EditorGUILayout.PropertyField(
                                _bookmarkSlotInputs.GetArrayElementAtIndex(i), 
                                new GUIContent($"Slot {i + 1}"));
                        }
                        EditorGUI.indentLevel--;
                    }
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.PropertyField(_smoothBookmarkTransition, new GUIContent("Smooth Transition"));
                    
                    if (_smoothBookmarkTransition.boolValue)
                    {
                        EditorGUILayout.PropertyField(_bookmarkTransitionSpeed, new GUIContent("Transition Speed"));
                    }
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawInputActionsSection()
        {
            _showInputActions = EditorGUILayout.BeginFoldoutHeaderGroup(_showInputActions, "Input Actions");
            
            if (_showInputActions)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.HelpBox(
                    "Configure keyboard bindings for camera movement. Each action can have multiple bindings via the Unity Input System.",
                    MessageType.Info);
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_moveForwardInput, new GUIContent("Move Forward", "Input action for moving the camera forward (default: W)"));
                EditorGUILayout.PropertyField(_moveBackwardInput, new GUIContent("Move Backward", "Input action for moving the camera backward (default: S)"));
                EditorGUILayout.PropertyField(_moveLeftInput, new GUIContent("Move Left", "Input action for moving the camera left (default: A)"));
                EditorGUILayout.PropertyField(_moveRightInput, new GUIContent("Move Right", "Input action for moving the camera right (default: D)"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Modifiers", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_sprintInput, new GUIContent("Sprint", "Input action for fast movement (default: Left Shift)"));
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawFollowSection()
        {
            _showFollow = EditorGUILayout.BeginFoldoutHeaderGroup(_showFollow, "Focus & Follow");
            
            if (_showFollow)
            {
                EditorGUI.indentLevel++;
                
                // Only show the Follow Target - the rest is configured in the preset
                EditorGUILayout.PropertyField(_followTarget, new GUIContent("Follow Target"));
                
                // Show current follow mode status (read from controller, controlled by preset)
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(_followMode, new GUIContent("Follow Mode Active"));
                EditorGUI.EndDisabledGroup();
                
                EditorGUILayout.Space(4);
                
                // Info box explaining that settings are in the preset
                EditorGUILayout.HelpBox(
                    "Focus & Follow behavior settings (smoothness, offsets, lookahead, etc.) are configured in the Camera Preset.\n\n" +
                    "• Select the active preset in the Project window to edit these settings\n" +
                    "• Enable 'Follow Mode' in the preset to auto-activate follow when the preset is applied\n" +
                    "• The Follow Target (above) is the scene object the camera will follow",
                    MessageType.Info);
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawAutoOrbitSection()
        {
            _showAutoOrbit = EditorGUILayout.BeginFoldoutHeaderGroup(_showAutoOrbit, "Auto-Orbit (Arkham-Style)");

            if (_showAutoOrbit)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_enableAutoOrbit, new GUIContent("Enable Auto-Orbit",
                    "Camera automatically rotates to stay behind the follow target's movement direction."));

                if (_enableAutoOrbit.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "The camera will automatically orbit behind the follow target's movement direction. " +
                        "Manual rotation input is not needed — ideal for controllers and action combat (Batman Arkham, God of War).",
                        MessageType.Info);

                    EditorGUILayout.PropertyField(_autoOrbitSpeed, new GUIContent("Orbit Speed", "How fast the camera swings behind the target."));
                    EditorGUILayout.PropertyField(_autoOrbitDeadZone, new GUIContent("Dead Zone", "Minimum target speed before orbit activates."));
                    EditorGUILayout.PropertyField(_autoOrbitYawOffset, new GUIContent("Yaw Offset", "Offset from directly behind (0 = behind, 90 = right side)."));

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Return Behaviour", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_autoOrbitReturnDelay, new GUIContent("Return Delay",
                        "Seconds to wait after target stops before returning to facing direction. 0 = hold angle."));

                    if (_autoOrbitReturnDelay.floatValue > 0f)
                    {
                        EditorGUILayout.PropertyField(_autoOrbitReturnToTargetForward, new GUIContent("Return To Target Forward",
                            "Use target's facing direction as return angle."));
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAdvancedSection()
        {
            _showAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(_showAdvanced, "Advanced Settings");
            
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(_useUnscaledTime, new GUIContent("Use Unscaled Time", 
                    "Camera works during pause (Time.timeScale = 0)"));
                EditorGUILayout.PropertyField(_groundLayer, new GUIContent("Ground Layer"));
                EditorGUILayout.PropertyField(_terrainFollowing, new GUIContent("Terrain Following"));
                
                if (_terrainFollowing.boolValue)
                {
                    EditorGUILayout.PropertyField(_heightAboveGround, new GUIContent("Height Above Ground"));
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawCameraCollisionSection()
        {
            _showCollision = EditorGUILayout.BeginFoldoutHeaderGroup(_showCollision, "Camera Collision");

            if (_showCollision)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_enableCameraCollision, new GUIContent("Enable Collision"));
                if (_enableCameraCollision.boolValue)
                {
                    EditorGUILayout.PropertyField(_cameraCollisionLayers, new GUIContent("Collision Layers"));
                    EditorGUILayout.PropertyField(_cameraCollisionRadius, new GUIContent("Probe Radius"));
                    EditorGUILayout.PropertyField(_cameraCollisionBuffer, new GUIContent("Surface Buffer"));
                    EditorGUILayout.PropertyField(_cameraCollisionIgnoreTriggers, new GUIContent("Ignore Triggers"));
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_enableLineOfSightGroundReposition, new GUIContent("Keep Above Ground (Negative Pitch)"));
                if (_enableLineOfSightGroundReposition.boolValue)
                {
                    EditorGUILayout.PropertyField(_lineOfSightGroundBuffer, new GUIContent("Ground Buffer"));
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void SetBoundsFromTerrain()
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                Vector3 terrainPos = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                
                _minX.floatValue = terrainPos.x;
                _maxX.floatValue = terrainPos.x + terrainSize.x;
                _minZ.floatValue = terrainPos.z;
                _maxZ.floatValue = terrainPos.z + terrainSize.z;
            }
            else
            {
                Debug.LogWarning("No active terrain found!");
            }
        }

        private void SetBoundsFromMesh()
        {
            if (_boundsMeshRenderer.objectReferenceValue == null)
            {
                Debug.LogWarning("Assign a MeshRenderer to use Set From Mesh Renderer.");
                return;
            }

            var renderer = _boundsMeshRenderer.objectReferenceValue as MeshRenderer;
            if (renderer == null)
            {
                Debug.LogWarning("Bounds Mesh Renderer must be a MeshRenderer component.");
                return;
            }

            Bounds b = renderer.bounds;
            _minX.floatValue = b.min.x;
            _maxX.floatValue = b.max.x;
            _minZ.floatValue = b.min.z;
            _maxZ.floatValue = b.max.z;
        }
    }
}
