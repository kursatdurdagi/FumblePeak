using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Editor
{
    [CustomEditor(typeof(UI.RTSMinimapUI))]
    public class RTSMinimapUIEditor : UnityEditor.Editor
    {
        // Data
        private SerializedProperty _dataProviderSource;

        // World Bounds
        private SerializedProperty _worldCenter;
        private SerializedProperty _worldSize;
        private SerializedProperty _autoSyncBounds;
        private SerializedProperty _autoSyncPadding;

        // Background
        private SerializedProperty _backgroundImage;
        private SerializedProperty _backgroundColor;
        private SerializedProperty _captureBackgroundOnStart;
        private SerializedProperty _captureResolution;
        private SerializedProperty _captureDelay;
        private SerializedProperty _captureCullingMask;

        // Fog Overlay
        private SerializedProperty _showFogOverlay;
        private SerializedProperty _fogOverlayImage;
        private SerializedProperty _fogOfWarSource;
        private SerializedProperty _fogRefreshInterval;

        // Markers
        private SerializedProperty _markerSprite;
        private SerializedProperty _markerSize;
        private SerializedProperty _selectedMarkerSize;
        private SerializedProperty _maxMarkers;

        // View Indicator
        private SerializedProperty _showViewIndicator;
        private SerializedProperty _viewIndicatorRect;
        private SerializedProperty _viewIndicatorColor;
        private SerializedProperty _lockViewIndicatorSize;
        private SerializedProperty _lockedViewIndicatorSize;
        private SerializedProperty _maxViewIndicatorSize;

        // Interaction
        private SerializedProperty _leftClickMovesCamera;
        private SerializedProperty _rightClickMovesAgents;
        private SerializedProperty _cameraController;
        private SerializedProperty _rtsController;

        // Performance
        private SerializedProperty _refreshInterval;

        // Alerts
        private SerializedProperty _enableAlertFlash;
        private SerializedProperty _alertSystem;

        private void OnEnable()
        {
            _dataProviderSource = serializedObject.FindProperty("dataProviderSource");

            _worldCenter = serializedObject.FindProperty("worldCenter");
            _worldSize = serializedObject.FindProperty("worldSize");
            _autoSyncBounds = serializedObject.FindProperty("autoSyncBounds");
            _autoSyncPadding = serializedObject.FindProperty("autoSyncPadding");

            _backgroundImage = serializedObject.FindProperty("backgroundImage");
            _backgroundColor = serializedObject.FindProperty("backgroundColor");
            _captureBackgroundOnStart = serializedObject.FindProperty("captureBackgroundOnStart");
            _captureResolution = serializedObject.FindProperty("captureResolution");
            _captureDelay = serializedObject.FindProperty("captureDelay");
            _captureCullingMask = serializedObject.FindProperty("captureCullingMask");

            _showFogOverlay = serializedObject.FindProperty("showFogOverlay");
            _fogOverlayImage = serializedObject.FindProperty("fogOverlayImage");
            _fogOfWarSource = serializedObject.FindProperty("fogOfWarSource");
            _fogRefreshInterval = serializedObject.FindProperty("fogRefreshInterval");

            _markerSprite = serializedObject.FindProperty("markerSprite");
            _markerSize = serializedObject.FindProperty("markerSize");
            _selectedMarkerSize = serializedObject.FindProperty("selectedMarkerSize");
            _maxMarkers = serializedObject.FindProperty("maxMarkers");

            _showViewIndicator = serializedObject.FindProperty("showViewIndicator");
            _viewIndicatorRect = serializedObject.FindProperty("viewIndicatorRect");
            _viewIndicatorColor = serializedObject.FindProperty("viewIndicatorColor");
            _lockViewIndicatorSize = serializedObject.FindProperty("lockViewIndicatorSize");
            _lockedViewIndicatorSize = serializedObject.FindProperty("lockedViewIndicatorSize");
            _maxViewIndicatorSize = serializedObject.FindProperty("maxViewIndicatorSize");

            _leftClickMovesCamera = serializedObject.FindProperty("leftClickMovesCamera");
            _rightClickMovesAgents = serializedObject.FindProperty("rightClickMovesAgents");
            _cameraController = serializedObject.FindProperty("cameraController");
            _rtsController = serializedObject.FindProperty("rtsController");

            _refreshInterval = serializedObject.FindProperty("refreshInterval");

            _enableAlertFlash = serializedObject.FindProperty("enableAlertFlash");
            _alertSystem = serializedObject.FindProperty("alertSystem");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawDataProvider();
            EditorGUILayout.Space(5);

            DrawWorldBounds();
            EditorGUILayout.Space(5);

            DrawBackground();
            EditorGUILayout.Space(5);

            DrawFogOverlay();
            EditorGUILayout.Space(5);

            DrawMarkers();
            EditorGUILayout.Space(5);

            DrawViewIndicator();
            EditorGUILayout.Space(5);

            DrawInteraction();
            EditorGUILayout.Space(5);

            DrawAlerts();
            EditorGUILayout.Space(5);

            DrawPerformance();

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

            GUI.color = new Color(0.3f, 0.7f, 1f, 1f);
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("RTS Minimap UI", title);
            EditorGUILayout.LabelField("Camera-free minimap — works with Built-in, URP, and HDRP",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
            EditorGUILayout.EndVertical();
        }

        private void DrawDataProvider()
        {
            EditorGUILayout.LabelField("Data Provider", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_dataProviderSource,
                new GUIContent("Provider", "MonoBehaviour implementing IMinimapDataProvider. Auto-discovered if null."));

            if (_dataProviderSource.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "No data provider assigned. Will auto-discover an EnemyMassesMinimapDataProvider in the scene at runtime.\n" +
                    "Create one via GameObject > Enemy Masses > UI > Minimap.",
                    MessageType.Info);
            }
            else if (!(_dataProviderSource.objectReferenceValue is IMinimapDataProvider))
            {
                EditorGUILayout.HelpBox(
                    "Assigned MonoBehaviour does not implement IMinimapDataProvider.",
                    MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawWorldBounds()
        {
            EditorGUILayout.LabelField("World Bounds", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_autoSyncBounds,
                new GUIContent("Auto-Sync from Provider", "Automatically update world bounds from the data provider."));

            if (_autoSyncBounds.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_autoSyncPadding,
                    new GUIContent("Padding", "World-unit padding around auto-synced bounds."));
                EditorGUI.indentLevel--;

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(_worldCenter);
                EditorGUILayout.PropertyField(_worldSize);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.PropertyField(_worldCenter,
                    new GUIContent("Center (X, Z)", "World center of the displayed area."));
                EditorGUILayout.PropertyField(_worldSize,
                    new GUIContent("Size (W, D)", "World width and depth of the displayed area."));
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBackground()
        {
            EditorGUILayout.LabelField("Background", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_backgroundImage,
                new GUIContent("Image", "RawImage showing a static terrain capture or pre-baked minimap background."));
            EditorGUILayout.PropertyField(_backgroundColor,
                new GUIContent("Tint", "Background color tint."));

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_captureBackgroundOnStart,
                new GUIContent("Auto-Capture on Start", "Capture a top-down screenshot of the terrain at runtime."));

            if (_captureBackgroundOnStart.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_captureResolution,
                    new GUIContent("Resolution", "Pixel resolution of the captured texture."));
                EditorGUILayout.PropertyField(_captureDelay,
                    new GUIContent("Delay (s)", "Wait seconds before capturing to let the scene load."));
                EditorGUILayout.PropertyField(_captureCullingMask,
                    new GUIContent("Culling Mask", "Layers to include in the capture."));
                EditorGUI.indentLevel--;
            }

            if (_backgroundImage.objectReferenceValue == null && !_captureBackgroundOnStart.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "No background image assigned and auto-capture is disabled. The minimap will show markers only over a transparent background.\n" +
                    "Assign a RawImage or enable Auto-Capture for best results.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFogOverlay()
        {
            EditorGUILayout.LabelField("Fog of War Overlay", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showFogOverlay,
                new GUIContent("Show Fog Overlay", "Display the fog-of-war texture on the minimap."));

            if (_showFogOverlay.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_fogOfWarSource,
                    new GUIContent("Fog Source", "MonoBehaviour implementing IMinimapFogProvider. Auto-discovered if null."));
                EditorGUILayout.PropertyField(_fogOverlayImage,
                    new GUIContent("Overlay Image", "RawImage for the fog. Auto-created if null."));
                EditorGUILayout.PropertyField(_fogRefreshInterval,
                    new GUIContent("Refresh Interval (s)", "How often to update the fog overlay. 0 = every frame."));

                if (_fogOfWarSource.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No fog source assigned. Will auto-discover a MonoBehaviour implementing IMinimapFogProvider at runtime.",
                        MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMarkers()
        {
            EditorGUILayout.LabelField("Marker Appearance", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_markerSprite,
                new GUIContent("Sprite", "Marker sprite. Uses Unity's default white if null."));
            EditorGUILayout.PropertyField(_markerSize,
                new GUIContent("Size (px)", "Marker size in pixels."));
            EditorGUILayout.PropertyField(_selectedMarkerSize,
                new GUIContent("Selected Size (px)", "Size for selected agents."));
            EditorGUILayout.PropertyField(_maxMarkers,
                new GUIContent("Max Markers", "Maximum UI elements. Higher = more draw calls."));

            EditorGUILayout.EndVertical();
        }

        private void DrawViewIndicator()
        {
            EditorGUILayout.LabelField("View Indicator", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showViewIndicator,
                new GUIContent("Show", "Display a rectangle showing the camera's view."));

            if (_showViewIndicator.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_viewIndicatorRect,
                    new GUIContent("RectTransform", "Auto-created at runtime if null."));
                EditorGUILayout.PropertyField(_viewIndicatorColor, new GUIContent("Color"));

                EditorGUILayout.PropertyField(_lockViewIndicatorSize,
                    new GUIContent("Fixed Size", "Use a constant pixel size instead of computing from camera frustum."));

                if (_lockViewIndicatorSize.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_lockedViewIndicatorSize,
                        new GUIContent("Size (px)", "Fixed size in pixels."));
                    EditorGUI.indentLevel--;
                }
                else
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_maxViewIndicatorSize,
                        new GUIContent("Max Size (px)", "Maximum indicator size in pixels. Prevents it from covering the entire minimap when zoomed out. 0 = no limit."));
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInteraction()
        {
            EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_leftClickMovesCamera,
                new GUIContent("Left-Click → Camera", "Left-click pans the camera to the clicked position."));
            EditorGUILayout.PropertyField(_rightClickMovesAgents,
                new GUIContent("Right-Click → Move Agents", "Right-click issues a move command for selected agents."));

            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(_cameraController,
                new GUIContent("Camera Controller", "RTSCameraController. Auto-discovered if null."));
            EditorGUILayout.PropertyField(_rtsController,
                new GUIContent("RTS Controller", "EnemyMassesRTSController. Auto-discovered if null."));

            EditorGUILayout.EndVertical();
        }

        private void DrawPerformance()
        {
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_refreshInterval,
                new GUIContent("Refresh Interval (s)", "How often to recalculate marker positions. 0 = every frame."));

            EditorGUILayout.EndVertical();
        }

        private void DrawAlerts()
        {
            EditorGUILayout.LabelField("Under-Attack Alerts", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableAlertFlash,
                new GUIContent("Enable Alert Flash", "AoE2-style marker flashing when player units are attacked."));

            if (_enableAlertFlash.boolValue)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_alertSystem,
                    new GUIContent("Alert System", "MinimapAlertSystem component. Auto-discovered if null."));

                if (_alertSystem.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No MinimapAlertSystem assigned. Will auto-discover from the data provider or scene. " +
                        "All alert settings (colors, timing, audio, view indicator flash) are configured on the MinimapAlertSystem component.",
                        MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }
    }
}
