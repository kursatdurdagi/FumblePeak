using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Modern RTS Camera Controller with ScriptableObject-based presets.
    /// Supports edge panning, drag panning, WASD movement, zoom, rotation, and camera bookmarks.
    /// Users can create custom presets and switch between them at runtime.
    /// Designed for Unity 6 LTS and newer using the new Input System.
    /// </summary>
    public class RTSCameraController : MonoBehaviour
    {
        #region Enums
        
        /// <summary>
        /// Mouse button for drag operations
        /// </summary>
        public enum DragButton
        {
            Left = 0,
            Right = 1,
            Middle = 2
        }
        
        #endregion
        
        #region Inspector Fields
        
        [Header("Camera Presets")]
        [Tooltip("List of available camera presets. Switch between them at runtime.")]
        [SerializeField] private List<RTSCameraPreset> cameraPresets = new List<RTSCameraPreset>();

        [Tooltip("Index of the active preset in the cameraPresets list.")]
        [SerializeField] private int activePresetIndex = 0;

        [Header("Control Settings")]
        [Tooltip("If disabled, the RTS Camera Controller will not control the camera on start. " +
                 "Useful when integrating with other camera systems (e.g., Game Creator 2). " +
                 "Call SetControlActive(true) or use 'Transition to RTS Camera' instruction to enable.")]
        [SerializeField] private bool controlActiveOnStart = true;

        [Header("Scheme Transition")]
        [Tooltip("Enable smooth transitions when switching presets via TransitionToPreset.")]
        [SerializeField] private bool enablePresetTransitions = true;

        [Tooltip("Duration in seconds for preset transitions.")]
        [Range(0.05f, 5f)]
        [SerializeField] private float presetTransitionDuration = 0.6f;

        [Tooltip("Curve used for preset transition easing.")]
        [SerializeField] private AnimationCurve presetTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        [Header("Movement Settings")]
        [Tooltip("Base camera movement speed")]
        [SerializeField] private float moveSpeed = 20f;
        
        [Tooltip("Movement speed when holding shift (sprint)")]
        [SerializeField] private float fastMoveSpeed = 40f;
        
        [Tooltip("Movement acceleration for smooth starts/stops")]
        [SerializeField] private float moveAcceleration = 8f;
        
        [Tooltip("Movement deceleration when input stops")]
        [SerializeField] private float moveDeceleration = 12f;
        
        [Tooltip("Enable smooth movement interpolation")]
        [SerializeField] private bool smoothMovement = true;
        
        [Header("Edge Panning")]
        [Tooltip("Enable edge panning (cursor at screen edge moves camera)")]
        [SerializeField] private bool enableEdgePan = true;
        
        [Tooltip("Distance from screen edge to trigger panning (in pixels)")]
        [Range(1, 50)]
        [SerializeField] private int edgePanBorder = 10;
        
        [Tooltip("Edge pan speed multiplier")]
        [Range(0.1f, 2f)]
        [SerializeField] private float edgePanSpeedMultiplier = 1f;
        
        [Tooltip("Disable edge pan when cursor is outside game window")]
        [SerializeField] private bool disableEdgePanOutsideWindow = true;
        
        [Header("Drag Panning")]
        [Tooltip("Enable drag panning (hold mouse button + drag to pan)")]
        [SerializeField] private bool enableDragPan = true;
        
        [Tooltip("Input action for drag panning (default: Middle Mouse Button)")]
        [SerializeField] 
        public InputAction dragPanInput = new InputAction("Drag Pan", InputActionType.Button, "<Mouse>/middleButton");
        
        [Tooltip("Invert drag pan direction (drag left = camera moves left)")]
        [SerializeField] private bool invertDragPan = false;
        
        [Tooltip("Drag pan sensitivity")]
        [Range(0.1f, 5f)]
        [SerializeField] private float dragPanSensitivity = 1f;
        
        [Header("Keyboard Movement")]
        [Tooltip("Enable WASD/Arrow key movement")]
        [SerializeField] private bool enableKeyboardPan = true;
        
        [Header("Zoom Settings")]
        [Tooltip("Enable mouse scroll zoom")]
        [SerializeField] private bool enableZoom = true;
        
        [Tooltip("Zoom speed")]
        [Range(1f, 50f)]
        [SerializeField] private float zoomSpeed = 10f;
        
        [Tooltip("Minimum zoom distance (closest to ground)")]
        [SerializeField] private float minZoomDistance = 5f;
        
        [Tooltip("Maximum zoom distance (furthest from ground)")]
        [SerializeField] private float maxZoomDistance = 100f;
        
        [Tooltip("Smooth zoom interpolation")]
        [SerializeField] private bool smoothZoom = true;
        
        [Tooltip("Zoom smoothing speed")]
        [Range(1f, 20f)]
        [SerializeField] private float zoomSmoothSpeed = 8f;
        
        [Tooltip("Zoom towards mouse cursor (like Google Maps)")]
        [SerializeField] private bool zoomToCursor = true;
        
        [Header("Rotation Settings")]
        [Tooltip("Enable camera rotation")]
        [SerializeField] private bool enableRotation = true;
        
        [Tooltip("Input action for camera rotation (default: Right Mouse Button)")]
        [SerializeField] 
        public InputAction rotationInput = new InputAction("Camera Rotation", InputActionType.Button, "<Mouse>/rightButton");
        
        [Tooltip("Rotation speed")]
        [Range(0.1f, 5f)]
        [SerializeField] private float rotationSpeed = 1f;

        [Tooltip("Rotate camera without holding the rotation input")]
        [SerializeField] private bool rotateWithoutButton = false;

        [Tooltip("Enable vertical tilt (pitch)")]
        [SerializeField] private bool enableTilt = true;
        
        [Tooltip("Minimum tilt angle (negative = look upward)")]
        [Range(-89f, 89f)]
        [SerializeField] private float minTiltAngle = 30f;
        
        [Tooltip("Maximum tilt angle (looking more horizontal)")]
        [Range(-89f, 89f)]
        [SerializeField] private float maxTiltAngle = 80f;
        
        [Tooltip("Smooth rotation interpolation")]
        [SerializeField] private bool smoothRotation = true;
        
        [Tooltip("Rotation smoothing speed")]
        [Range(1f, 20f)]
        [SerializeField] private float rotationSmoothSpeed = 10f;
        
        [Header("Camera Bounds")]
        [Tooltip("Enable camera movement bounds")]
        [SerializeField] private bool enableBounds = true;
        
        [Tooltip("Minimum X position")]
        [SerializeField] private float minX = -100f;
        
        [Tooltip("Maximum X position")]
        [SerializeField] private float maxX = 100f;
        
        [Tooltip("Minimum Z position")]
        [SerializeField] private float minZ = -100f;
        
        [Tooltip("Maximum Z position")]
        [SerializeField] private float maxZ = 100f;

        [Tooltip("Optional mesh renderer to auto-set bounds from its world-space AABB (editor helper)")]
        [SerializeField] private MeshRenderer boundsMeshRenderer;
        
        [Header("Camera Bookmarks")]
        [Tooltip("Enable camera position bookmarks (modifier+1-9 to save, 1-9 to load)")]
        [SerializeField] private bool enableBookmarks = true;
        
        [Tooltip("Number of available bookmark slots")]
        [Range(1, 9)]
        [SerializeField] private int bookmarkSlots = 4;
        
        [Tooltip("Input actions for bookmark slots 1-9. Assign keys like 1, 2, 3, etc.")]
        [SerializeField] 
        public InputAction[] bookmarkSlotInputs = new InputAction[9]
        {
            new InputAction("Bookmark 1", InputActionType.Button, "<Keyboard>/1"),
            new InputAction("Bookmark 2", InputActionType.Button, "<Keyboard>/2"),
            new InputAction("Bookmark 3", InputActionType.Button, "<Keyboard>/3"),
            new InputAction("Bookmark 4", InputActionType.Button, "<Keyboard>/4"),
            new InputAction("Bookmark 5", InputActionType.Button, "<Keyboard>/5"),
            new InputAction("Bookmark 6", InputActionType.Button, "<Keyboard>/6"),
            new InputAction("Bookmark 7", InputActionType.Button, "<Keyboard>/7"),
            new InputAction("Bookmark 8", InputActionType.Button, "<Keyboard>/8"),
            new InputAction("Bookmark 9", InputActionType.Button, "<Keyboard>/9")
        };
        
        [Tooltip("Input action for the modifier key to save bookmarks. Default is Alt key.")]
        [SerializeField] 
        public InputAction bookmarkSaveModifier = new InputAction("Bookmark Save Modifier", InputActionType.Button, "<Keyboard>/leftAlt");
        
        [Header("Input Actions")]
        [Tooltip("Input action for moving forward (typically W or Up Arrow).")]
        [SerializeField]
        public InputAction moveForwardInput = new InputAction("Move Forward", InputActionType.Button, "<Keyboard>/w");
        
        [Tooltip("Input action for moving backward (typically S or Down Arrow).")]
        [SerializeField]
        public InputAction moveBackwardInput = new InputAction("Move Backward", InputActionType.Button, "<Keyboard>/s");
        
        [Tooltip("Input action for moving left (typically A or Left Arrow).")]
        [SerializeField]
        public InputAction moveLeftInput = new InputAction("Move Left", InputActionType.Button, "<Keyboard>/a");
        
        [Tooltip("Input action for moving right (typically D or Right Arrow).")]
        [SerializeField]
        public InputAction moveRightInput = new InputAction("Move Right", InputActionType.Button, "<Keyboard>/d");
        
        [Tooltip("Input action for sprint/fast move (typically Left Shift).")]
        [SerializeField]
        public InputAction sprintInput = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
        
        [Tooltip("Smooth transition to bookmarks")]
        [SerializeField] private bool smoothBookmarkTransition = true;
        
        [Tooltip("Bookmark transition speed")]
        [Range(1f, 20f)]
        [SerializeField] private float bookmarkTransitionSpeed = 5f;
        
        [Header("Focus/Follow Target")]
        [Tooltip("Enable focusing on targets (reserved for future use)")]
        #pragma warning disable CS0414 // Field is assigned but never used
        [SerializeField] private bool enableFocus = true;
        #pragma warning restore CS0414
        
        [Tooltip("Smooth transition when focusing")]
        [SerializeField] private bool smoothFocusTransition = true;
        
        [Tooltip("Focus transition speed (reserved for future use)")]
        #pragma warning disable CS0414 // Field is assigned but never used
        [Range(1f, 20f)]
        [SerializeField] private float focusTransitionSpeed = 8f;
        #pragma warning restore CS0414
        
        [Tooltip("Current follow target (set via script or inspector)")]
        [SerializeField] private Transform followTarget;
        
        [Tooltip("Enable camera follow mode")]
        [SerializeField] private bool followMode = false;
        
        [Tooltip("Follow smoothness")]
        [Range(1f, 20f)]
        [SerializeField] private float followSmoothness = 5f;

        [Tooltip("Offset applied to the follow target position (camera pivot).")]
        [SerializeField] private Vector3 followTargetOffset = Vector3.zero;

        [Tooltip("Interpret followTargetOffset in the target's local space.")]
        [SerializeField] private bool followOffsetInTargetSpace = false;

        [Tooltip("Enable smooth look-ahead based on target movement direction.")]
        [SerializeField] private bool enableLookahead = false;

        [Tooltip("Seconds ahead to project the target's movement for look-ahead.")]
        [Range(0f, 1.5f)]
        [SerializeField] private float lookaheadTime = 0.2f;

        [Tooltip("Smooth time for look-ahead offset changes.")]
        [Range(0.01f, 2f)]
        [SerializeField] private float lookaheadSmoothTime = 0.15f;

        [Tooltip("Maximum distance for look-ahead offset.")]
        [Range(0f, 20f)]
        [SerializeField] private float maxLookaheadDistance = 3f;

        [Header("Auto-Orbit (Arkham-Style)")]
        [Tooltip("Enable automatic camera orbit that follows the target's movement direction. " +
                 "The camera stays behind the target without requiring manual rotation input.")]
        [SerializeField] private bool enableAutoOrbit = false;

        [Tooltip("How fast the camera rotates to stay behind the moving target.")]
        [Range(0.5f, 20f)]
        [SerializeField] private float autoOrbitSpeed = 5f;

        [Tooltip("Minimum target speed (units/sec) before the camera starts orbiting.")]
        [Range(0.01f, 3f)]
        [SerializeField] private float autoOrbitDeadZone = 0.15f;

        [Tooltip("Yaw offset from directly behind the target's movement direction. " +
                 "0 = directly behind, positive = offset to the right.")]
        [Range(-180f, 180f)]
        [SerializeField] private float autoOrbitYawOffset = 0f;

        [Tooltip("When the target stops moving, smoothly return to the target's facing direction " +
                 "after this delay (seconds). Set to 0 to hold the last orbit angle.")]
        [Range(0f, 5f)]
        [SerializeField] private float autoOrbitReturnDelay = 0f;

        [Tooltip("Use the target transform's forward direction as the return angle when the " +
                 "target stops moving (requires autoOrbitReturnDelay > 0).")]
        [SerializeField] private bool autoOrbitReturnToTargetForward = true;

        [Header("Third Person (Action RPG)")]
        [Tooltip("Use the follow target's Y position (plus offset) as the camera pivot height.")]
        [SerializeField] private bool followUseTargetHeight = false;

        [Tooltip("Vertical offset applied when followUseTargetHeight is enabled.")]
        [SerializeField] private float followTargetHeightOffset = 1.5f;
        
        [Header("Advanced")]
        [Tooltip("Use unscaled time (camera works during pause)")]
        [SerializeField] private bool useUnscaledTime = true;
        
        [Tooltip("Ground layer for raycasting (used for zoom-to-cursor)")]
        [SerializeField] private LayerMask groundLayer = ~0;
        
        [Tooltip("Height above ground to maintain")]
        [SerializeField] private float heightAboveGround = 0f;
        
        [Tooltip("Enable terrain following (camera stays at fixed height above terrain)")]
        [SerializeField] private bool terrainFollowing = false;

        [Header("Camera Collision")]
        [Tooltip("Prevent the camera from clipping through geometry.")]
        [SerializeField] private bool enableCameraCollision = false;

        [Tooltip("Layers that block the camera (walls, ground, props).")]
        [SerializeField] private LayerMask cameraCollisionLayers = ~0;

        [Tooltip("Collision probe radius for the camera.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float cameraCollisionRadius = 0.25f;

        [Tooltip("Extra distance to keep from collision surfaces.")]
        [Range(0f, 1f)]
        [SerializeField] private float cameraCollisionBuffer = 0.1f;

        [Tooltip("Ignore trigger colliders when resolving camera collision.")]
        [SerializeField] private bool cameraCollisionIgnoreTriggers = true;

        [Tooltip("When looking upward (negative pitch), keep the camera above ground by moving it along the line of sight.")]
        [SerializeField] private bool enableLineOfSightGroundReposition = false;

        [Tooltip("Extra distance to keep from the ground when line-of-sight repositioning is used.")]
        [Range(0f, 1f)]
        [SerializeField] private float lineOfSightGroundBuffer = 0.1f;
        
        [Header("Initial Setup")]
        [Tooltip("Initial camera height/zoom distance")]
        [SerializeField] private float initialZoom = 20f;
        
        [Tooltip("Initial camera pitch angle (45 = typical RTS angle)")]
        [SerializeField] private float initialPitch = 45f;
        
        [Tooltip("Initial camera yaw angle")]
        [SerializeField] private float initialYaw = 0f;
        
        #endregion
        
        #region Private Fields
        
        // Input
        private Vector2 _mousePosition;
        private Vector2 _mouseDelta;
        private Vector2 _scrollDelta;
        private Vector2 _keyboardInput;
        private bool _isDraggingPan;
        private bool _isDraggingRotate;
        private bool _isShiftHeld;
        private bool _isBookmarkModifierHeld;
        private Vector3 _dragStartWorldPosition;
        private Vector3 _dragStartCameraPosition;
        private Vector2 _lastDragMousePosition;
        
        // Movement
        private Vector3 _currentVelocity;
        private Vector3 _targetPosition;
        private float _targetZoom;
        private float _currentZoom;
        private float _targetYaw;
        private float _targetPitch;
        private float _currentYaw;
        private float _currentPitch;
        
        // Bookmarks
        private CameraBookmark[] _bookmarks;
        
        // Focus/Transition
        private bool _isTransitioning;
        private Vector3 _transitionTargetPosition;
        private float _transitionTargetZoom;
        private float _transitionTargetYaw;
        private float _transitionTargetPitch;
        private bool _isTimedTransition;
        private float _transitionElapsed;
        private float _transitionDuration;
        private AnimationCurve _transitionCurve;
        private Vector3 _transitionStartPosition;
        private float _transitionStartZoom;
        private float _transitionStartYaw;
        private float _transitionStartPitch;

        private Transform _pendingFollowTarget;
        private bool _pendingFollowMode;
        private Dictionary<RTSCameraPreset, CameraPresetState> _presetStates;
        private int _pendingPresetIndex;

        // Cached
        private Camera _camera;
        private Transform _cameraTransform;
        private float _suppressZoomUntilTime;

        // Look-ahead
        private Vector3 _lastFollowTargetPosition;
        private Vector3 _currentLookahead;
        private Vector3 _lookaheadVelocity;
        private bool _hasFollowTargetPosition;

        // Auto-orbit
        private Vector3 _autoOrbitLastTargetPos;
        private bool _autoOrbitHasLastPos;
        private float _autoOrbitStoppedTimer;
        private float _autoOrbitCurrentYawVelocity;
        
        #endregion
        
        #region Structs
        
        [System.Serializable]
        private struct CameraBookmark
        {
            public bool isSet;
            public Vector3 position;
            public float zoom;
            public float yaw;
            public float pitch;
        }

        [System.Serializable]
        private struct CameraPresetState
        {
            public bool isSet;
            public Vector3 position;
            public float zoom;
            public float yaw;
            public float pitch;
        }
        
        #endregion

        #region Public Properties

        /// <summary>
        /// Get the list of available camera presets.
        /// </summary>
        public IReadOnlyList<RTSCameraPreset> CameraPresets => cameraPresets;

        /// <summary>
        /// Get the currently active preset index.
        /// </summary>
        public int ActivePresetIndex => activePresetIndex;

        /// <summary>
        /// Get the currently active preset, or null if none.
        /// </summary>
        public RTSCameraPreset ActivePreset => GetPresetAt(activePresetIndex);

        /// <summary>
        /// Get the name of the currently active preset.
        /// </summary>
        public string ActivePresetName => ActivePreset != null ? ActivePreset.presetName : "None";

        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            _cameraTransform = _camera != null ? _camera.transform : transform;
            
            // Initialize control active state from inspector setting
            _isControlActive = controlActiveOnStart;
            
            // Initialize bookmarks and preset states
            _bookmarks = new CameraBookmark[9];
            _presetStates = new Dictionary<RTSCameraPreset, CameraPresetState>();
            _pendingPresetIndex = -1;
            
            // Initialize current state
            InitializeFromCurrentTransform();
            
            // Apply initial preset
            if (cameraPresets.Count > 0 && activePresetIndex >= 0 && activePresetIndex < cameraPresets.Count)
            {
                ApplyPreset(cameraPresets[activePresetIndex]);
                SavePresetState(cameraPresets[activePresetIndex]);
            }
        }
        
        private void OnEnable()
        {
            // Enable InputActions
            if (enableBookmarks)
            {
                bookmarkSaveModifier?.Enable();
                if (bookmarkSlotInputs != null)
                {
                    foreach (var input in bookmarkSlotInputs)
                        input?.Enable();
                }
            }
            
            // Enable movement InputActions
            moveForwardInput?.Enable();
            moveBackwardInput?.Enable();
            moveLeftInput?.Enable();
            moveRightInput?.Enable();
            sprintInput?.Enable();
            
            // Enable mouse InputActions
            dragPanInput?.Enable();
            rotationInput?.Enable();
        }
        
        private void OnDisable()
        {
            // Disable InputActions
            bookmarkSaveModifier?.Disable();
            if (bookmarkSlotInputs != null)
            {
                foreach (var input in bookmarkSlotInputs)
                    input?.Disable();
            }
            
            // Disable movement InputActions
            moveForwardInput?.Disable();
            moveBackwardInput?.Disable();
            moveLeftInput?.Disable();
            moveRightInput?.Disable();
            sprintInput?.Disable();
            
            // Disable mouse InputActions
            dragPanInput?.Disable();
            rotationInput?.Disable();
        }
        
        private void Update()
        {
            // Skip processing if control is disabled (e.g., another camera system is active)
            if (!_isControlActive) return;
            
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            
            // Read input
            ReadInput();
            
            // Process bookmarks (always, so user can interrupt transitions)
            ProcessBookmarks();
            
            // Process input
            if (!_isTransitioning)
            {
                ProcessEdgePan(deltaTime);
                ProcessDragPan(deltaTime);
                ProcessKeyboardPan(deltaTime);
                ProcessZoom(deltaTime);
                ProcessRotation(deltaTime);
            }
            
            // Follow target
            if (followMode && followTarget != null)
            {
                ProcessFollowTarget(deltaTime);
            }
            
            // Auto-orbit: rotate yaw to stay behind target movement direction
            if (enableAutoOrbit && followMode && followTarget != null)
            {
                ProcessAutoOrbit(deltaTime);
            }
            
            // Apply movement
            ApplyMovement(deltaTime);
            
            // Process transitions
            ProcessTransition(deltaTime);
            
            // Apply bounds
            if (enableBounds)
            {
                ApplyBounds();
            }
            
            // Apply terrain following
            if (terrainFollowing)
            {
                ApplyTerrainFollowing();
            }
            
            // Update camera position and rotation
            UpdateCameraTransform(deltaTime);
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeFromCurrentTransform()
        {
            // For RTS camera, the target position is the point on the ground the camera looks at
            // We calculate this from the camera's current position and rotation
            
            // Use initial values for zoom and rotation
            _currentZoom = initialZoom;
            _targetZoom = initialZoom;
            _currentPitch = initialPitch;
            _targetPitch = initialPitch;
            _currentYaw = initialYaw;
            _targetYaw = initialYaw;
            
            // Calculate the ground target position based on current camera position
            // or use the transform position as the pivot point
            if (_camera != null)
            {
                // Calculate where on the ground the camera should be looking
                // Start from camera position and project to ground
                Vector3 cameraPos = _camera.transform.position;
                
                // Calculate the offset from pivot to camera based on zoom and pitch
                float pitchRad = _currentPitch * Mathf.Deg2Rad;
                float horizontalDist = _currentZoom * Mathf.Cos(pitchRad);
                float verticalDist = _currentZoom * Mathf.Sin(pitchRad);
                
                // The target position (pivot point on ground) is ahead of the camera
                float yawRad = _currentYaw * Mathf.Deg2Rad;
                _targetPosition = new Vector3(
                    cameraPos.x + Mathf.Sin(yawRad) * horizontalDist,
                    0f,
                    cameraPos.z + Mathf.Cos(yawRad) * horizontalDist
                );
            }
            else
            {
                _targetPosition = transform.position;
                _targetPosition.y = 0f;
            }
        }
        
        /// <summary>
        /// Apply settings from a camera preset.
        /// </summary>
        public void ApplyPreset(RTSCameraPreset preset)
        {
            if (preset == null) return;

            // Movement
            moveSpeed = preset.moveSpeed;
            fastMoveSpeed = preset.fastMoveSpeed;
            moveAcceleration = preset.moveAcceleration;
            moveDeceleration = preset.moveDeceleration;
            smoothMovement = preset.smoothMovement;

            // Edge Pan
            enableEdgePan = preset.enableEdgePan;
            edgePanBorder = preset.edgePanBorder;
            edgePanSpeedMultiplier = preset.edgePanSpeedMultiplier;
            disableEdgePanOutsideWindow = preset.disableEdgePanOutsideWindow;

            // Drag Pan
            enableDragPan = preset.enableDragPan;
            dragPanInput = new InputAction("Drag Pan", InputActionType.Button, RTSCameraPreset.GetMouseButtonPath(preset.dragPanButton));
            dragPanInput.Enable();
            invertDragPan = preset.invertDragPan;
            dragPanSensitivity = preset.dragPanSensitivity;

            // Keyboard
            enableKeyboardPan = preset.enableKeyboardPan;

            // Zoom
            enableZoom = preset.enableZoom;
            zoomSpeed = preset.zoomSpeed;
            minZoomDistance = preset.minZoomDistance;
            maxZoomDistance = preset.maxZoomDistance;
            smoothZoom = preset.smoothZoom;
            zoomSmoothSpeed = preset.zoomSmoothSpeed;
            zoomToCursor = preset.zoomToCursor;

            // Rotation
            enableRotation = preset.enableRotation;
            rotationInput = new InputAction("Camera Rotation", InputActionType.Button, RTSCameraPreset.GetMouseButtonPath(preset.rotationButton));
            rotationInput.Enable();
            rotationSpeed = preset.rotationSpeed;
            rotateWithoutButton = preset.rotateWithoutButton;
            enableTilt = preset.enableTilt;
            minTiltAngle = preset.minTiltAngle;
            maxTiltAngle = preset.maxTiltAngle;
            smoothRotation = preset.smoothRotation;
            rotationSmoothSpeed = preset.rotationSmoothSpeed;

            // Initial setup
            initialZoom = preset.initialZoom;
            initialPitch = preset.initialPitch;
            initialYaw = preset.initialYaw;

            // Bounds
            enableBounds = preset.enableBounds;

            // Focus
            enableFocus = preset.enableFocus;
            smoothFocusTransition = preset.smoothFocusTransition;
            focusTransitionSpeed = preset.focusTransitionSpeed;

            // Follow/Third Person
            followMode = preset.enableFollowMode && followTarget != null;
            followSmoothness = preset.followSmoothness;
            followTargetOffset = preset.followTargetOffset;
            followOffsetInTargetSpace = preset.followOffsetInTargetSpace;
            followUseTargetHeight = preset.followUseTargetHeight;
            followTargetHeightOffset = preset.followTargetHeightOffset;

            // Look-ahead
            enableLookahead = preset.enableLookahead;
            lookaheadTime = preset.lookaheadTime;
            lookaheadSmoothTime = preset.lookaheadSmoothTime;
            maxLookaheadDistance = preset.maxLookaheadDistance;

            // Auto-orbit
            enableAutoOrbit = preset.enableAutoOrbit;
            autoOrbitSpeed = preset.autoOrbitSpeed;
            autoOrbitDeadZone = preset.autoOrbitDeadZone;
            autoOrbitYawOffset = preset.autoOrbitYawOffset;
            autoOrbitReturnDelay = preset.autoOrbitReturnDelay;
            autoOrbitReturnToTargetForward = preset.autoOrbitReturnToTargetForward;
            ResetAutoOrbit();

            // Collision
            enableCameraCollision = preset.enableCameraCollision;
            cameraCollisionRadius = preset.cameraCollisionRadius;
            cameraCollisionBuffer = preset.cameraCollisionBuffer;
            cameraCollisionIgnoreTriggers = preset.cameraCollisionIgnoreTriggers;
            enableLineOfSightGroundReposition = preset.enableLineOfSightGroundReposition;
            lineOfSightGroundBuffer = preset.lineOfSightGroundBuffer;

            // Terrain
            terrainFollowing = preset.terrainFollowing;
            heightAboveGround = preset.heightAboveGround;

            // Apply initial zoom/pitch if this is a fresh transition
            if (preset.enableFollowMode)
            {
                SetZoom(preset.initialZoom);
                SetRotation(_targetYaw, preset.initialPitch);
            }
        }

        /// <summary>
        /// Transition to a preset by index with optional smooth animation.
        /// </summary>
        /// <param name="presetIndex">Index in the cameraPresets list.</param>
        /// <param name="followOverride">Optional follow target to use with this preset.</param>
        /// <param name="useCachedState">If true, restore the last saved state for this preset.</param>
        public void TransitionToPreset(int presetIndex, Transform followOverride = null, bool useCachedState = true)
        {
            RTSCameraPreset preset = GetPresetAt(presetIndex);
            if (preset == null)
            {
                Debug.LogWarning($"[RTSCamera] Invalid preset index: {presetIndex}");
                return;
            }

            TransitionToPreset(preset, followOverride, useCachedState);
        }

        /// <summary>
        /// Transition to a preset by name with optional smooth animation.
        /// </summary>
        /// <param name="presetName">Name of the preset to transition to.</param>
        /// <param name="followOverride">Optional follow target to use with this preset.</param>
        /// <param name="useCachedState">If true, restore the last saved state for this preset.</param>
        public void TransitionToPreset(string presetName, Transform followOverride = null, bool useCachedState = true)
        {
            int index = GetPresetIndex(presetName);
            if (index < 0)
            {
                Debug.LogWarning($"[RTSCamera] Preset not found: {presetName}");
                return;
            }

            TransitionToPreset(index, followOverride, useCachedState);
        }

        /// <summary>
        /// Transition to a preset with optional smooth animation.
        /// </summary>
        /// <param name="preset">The preset to transition to.</param>
        /// <param name="followOverride">Optional follow target to use with this preset.</param>
        /// <param name="useCachedState">If true, restore the last saved state for this preset.</param>
        public void TransitionToPreset(RTSCameraPreset preset, Transform followOverride = null, bool useCachedState = true)
        {
            if (preset == null) return;

            int presetIndex = cameraPresets.IndexOf(preset);
            if (presetIndex < 0)
            {
                Debug.LogWarning($"[RTSCamera] Preset '{preset.presetName}' is not in the cameraPresets list.");
                return;
            }

            if (!enablePresetTransitions || presetTransitionDuration <= 0f)
            {
                ApplyPresetImmediate(presetIndex, followOverride, useCachedState);
                return;
            }

            // Save current preset state before transitioning
            RTSCameraPreset currentPreset = ActivePreset;
            if (currentPreset != null)
            {
                SavePresetState(currentPreset);
            }

            Transform desiredFollowTarget = followOverride != null ? followOverride : followTarget;
            bool desiredFollowMode = preset.enableFollowMode && desiredFollowTarget != null;

            CameraPresetState targetState = useCachedState && TryGetPresetState(preset, out CameraPresetState cached)
                ? cached
                : GetDefaultPresetState(preset, desiredFollowTarget);

            followMode = false;

            _pendingPresetIndex = presetIndex;
            _pendingFollowTarget = desiredFollowTarget;
            _pendingFollowMode = desiredFollowMode;

            StartTimedTransition(
                targetState.position,
                targetState.zoom,
                targetState.yaw,
                targetState.pitch,
                presetTransitionDuration,
                presetTransitionCurve);
        }

        /// <summary>
        /// Get a preset by index.
        /// </summary>
        public RTSCameraPreset GetPresetAt(int index)
        {
            if (cameraPresets == null || index < 0 || index >= cameraPresets.Count)
                return null;
            return cameraPresets[index];
        }

        /// <summary>
        /// Get the index of a preset by name (case-insensitive).
        /// </summary>
        public int GetPresetIndex(string presetName)
        {
            if (string.IsNullOrEmpty(presetName) || cameraPresets == null)
                return -1;

            for (int i = 0; i < cameraPresets.Count; i++)
            {
                if (cameraPresets[i] != null && 
                    string.Equals(cameraPresets[i].presetName, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Add a preset to the list at runtime.
        /// </summary>
        public void AddPreset(RTSCameraPreset preset)
        {
            if (preset == null || cameraPresets.Contains(preset))
                return;
            cameraPresets.Add(preset);
        }

        /// <summary>
        /// Remove a preset from the list at runtime.
        /// </summary>
        public bool RemovePreset(RTSCameraPreset preset)
        {
            if (preset == null)
                return false;
            
            int index = cameraPresets.IndexOf(preset);
            if (index < 0)
                return false;

            // Don't allow removing the active preset
            if (index == activePresetIndex)
            {
                Debug.LogWarning($"[RTSCamera] Cannot remove the active preset '{preset.presetName}'.");
                return false;
            }

            cameraPresets.RemoveAt(index);

            // Adjust active index if needed
            if (index < activePresetIndex)
            {
                activePresetIndex--;
            }

            // Clean up cached state
            if (_presetStates != null && _presetStates.ContainsKey(preset))
            {
                _presetStates.Remove(preset);
            }

            return true;
        }

        /// <summary>
        /// Get the number of available presets.
        /// </summary>
        public int PresetCount => cameraPresets?.Count ?? 0;
        
        #endregion
        
        #region Input Reading
        
        private void ReadInput()
        {
            // Mouse position
            _mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            _mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            _scrollDelta = Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;
            
            // Keyboard - using InputActions
            _keyboardInput = Vector2.zero;
            
            if (moveForwardInput != null && moveForwardInput.IsPressed())
                _keyboardInput.y += 1f;
            if (moveBackwardInput != null && moveBackwardInput.IsPressed())
                _keyboardInput.y -= 1f;
            if (moveRightInput != null && moveRightInput.IsPressed())
                _keyboardInput.x += 1f;
            if (moveLeftInput != null && moveLeftInput.IsPressed())
                _keyboardInput.x -= 1f;
            
            _isShiftHeld = sprintInput != null && sprintInput.IsPressed();
            
            // Read bookmark modifier from InputAction
            _isBookmarkModifierHeld = bookmarkSaveModifier.IsPressed();
            
            // Mouse buttons for drag operations using InputActions
            // Drag pan
            if (enableDragPan)
            {
                bool dragPanButtonPressed = dragPanInput != null && dragPanInput.IsPressed();
                
                if (dragPanButtonPressed && !_isDraggingPan)
                {
                    StartDragPan();
                }
                else if (!dragPanButtonPressed && _isDraggingPan)
                {
                    EndDragPan();
                }
            }
            
            // Rotation
            if (enableRotation)
            {
                if (rotateWithoutButton)
                {
                    _isDraggingRotate = !_isDraggingPan;
                }
                else
                {
                    bool rotateButtonPressed = rotationInput != null && rotationInput.IsPressed();
                    
                    if (rotateButtonPressed && !_isDraggingRotate && !_isDraggingPan)
                    {
                        _isDraggingRotate = true;
                    }
                    else if (!rotateButtonPressed && _isDraggingRotate)
                    {
                        _isDraggingRotate = false;
                    }
                }
            }
            else
            {
                _isDraggingRotate = false;
            }
        }
        
        #endregion
        
        #region Edge Panning
        
        private void ProcessEdgePan(float deltaTime)
        {
            if (!enableEdgePan) return;
            
            // Check if cursor is in game window
            if (disableEdgePanOutsideWindow)
            {
                if (_mousePosition.x < 0 || _mousePosition.x > Screen.width ||
                    _mousePosition.y < 0 || _mousePosition.y > Screen.height)
                {
                    return;
                }
            }
            
            Vector3 panDirection = Vector3.zero;
            
            // Left edge
            if (_mousePosition.x <= edgePanBorder)
            {
                panDirection.x -= 1f;
            }
            // Right edge
            else if (_mousePosition.x >= Screen.width - edgePanBorder)
            {
                panDirection.x += 1f;
            }
            
            // Bottom edge
            if (_mousePosition.y <= edgePanBorder)
            {
                panDirection.z -= 1f;
            }
            // Top edge
            else if (_mousePosition.y >= Screen.height - edgePanBorder)
            {
                panDirection.z += 1f;
            }
            
            if (panDirection != Vector3.zero)
            {
                float speed = (_isShiftHeld ? fastMoveSpeed : moveSpeed) * edgePanSpeedMultiplier;
                ApplyPanInput(panDirection.normalized, speed, deltaTime);
            }
        }
        
        #endregion
        
        #region Drag Panning
        
        private void StartDragPan()
        {
            _isDraggingPan = true;
            _dragStartWorldPosition = GetMouseWorldPosition();
            _dragStartCameraPosition = _targetPosition;
            _lastDragMousePosition = _mousePosition;
        }
        
        private void EndDragPan()
        {
            _isDraggingPan = false;
        }
        
        private void ProcessDragPan(float deltaTime)
        {
            if (!enableDragPan || !_isDraggingPan) return;
            
            // Use screen-space delta to avoid feedback loop with camera movement
            Vector2 screenDelta = _mousePosition - _lastDragMousePosition;
            
            if (screenDelta.sqrMagnitude < 0.01f) return;
            
            // Convert screen delta to world movement based on current camera orientation and zoom
            // Scale movement by zoom level (further = faster pan) and screen size
            float scaleFactor = _currentZoom * 0.002f * dragPanSensitivity;
            
            // Get camera-relative directions on XZ plane
            float yawRad = _currentYaw * Mathf.Deg2Rad;
            Vector3 right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
            Vector3 forward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            
            // Calculate world movement from screen delta
            Vector3 worldDelta = right * screenDelta.x + forward * screenDelta.y;
            worldDelta *= scaleFactor;
            
            if (!invertDragPan)
            {
                worldDelta = -worldDelta;
            }
            
            _targetPosition += worldDelta;
            _lastDragMousePosition = _mousePosition;
        }
        
        private Vector3 GetMouseWorldPosition()
        {
            if (_camera == null) return Vector3.zero;
            
            Ray ray = _camera.ScreenPointToRay(_mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                return ray.GetPoint(distance);
            }
            
            return Vector3.zero;
        }
        
        #endregion
        
        #region Keyboard Panning
        
        private void ProcessKeyboardPan(float deltaTime)
        {
            if (!enableKeyboardPan) return;
            if (_keyboardInput == Vector2.zero) return;
            
            // Pass raw input direction - ApplyPanInput will convert to world space
            // Using _targetYaw ensures movement direction is based on desired orientation,
            // not the smoothed current orientation which can cause direction drift during rotation
            Vector3 direction = new Vector3(_keyboardInput.x, 0f, _keyboardInput.y).normalized;
            
            float speed = _isShiftHeld ? fastMoveSpeed : moveSpeed;
            ApplyPanInput(direction, speed, deltaTime);
        }
        
        #endregion
        
        #region Movement Application
        
        private void ApplyPanInput(Vector3 direction, float speed, float deltaTime)
        {
            // Convert direction to world space based on TARGET yaw (not current)
            // This ensures movement direction matches the intended camera orientation,
            // preventing direction drift during rotation
            float yawRad = _targetYaw * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            Vector3 right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
            
            Vector3 worldDirection = forward * direction.z + right * direction.x;
            
            if (smoothMovement)
            {
                // Accelerate towards target velocity
                Vector3 targetVelocity = worldDirection * speed;
                _currentVelocity = Vector3.Lerp(_currentVelocity, targetVelocity, moveAcceleration * deltaTime);
            }
            else
            {
                _currentVelocity = worldDirection * speed;
            }
        }
        
        private void ApplyMovement(float deltaTime)
        {
            // Decelerate if no input
            if (_keyboardInput == Vector2.zero && !_isDraggingPan)
            {
                if (smoothMovement && _currentVelocity.magnitude > 0.01f)
                {
                    _currentVelocity = Vector3.Lerp(_currentVelocity, Vector3.zero, moveDeceleration * deltaTime);
                }
                else if (!smoothMovement)
                {
                    _currentVelocity = Vector3.zero;
                }
            }
            
            // Apply velocity to target position
            if (!_isDraggingPan)
            {
                _targetPosition += _currentVelocity * deltaTime;
            }
        }
        
        #endregion
        
        #region External Zoom Suppression

        /// <summary>
        /// Temporarily suppress scroll-wheel zoom, useful when another system consumes scroll input (e.g., formation cycling).
        /// </summary>
        /// <param name="duration">Seconds to suppress zoom for.</param>
        public void SuppressZoom(float duration = 0.2f)
        {
            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            float until = now + Mathf.Max(0f, duration);
            if (until > _suppressZoomUntilTime)
            {
                _suppressZoomUntilTime = until;
            }
        }

        private bool IsZoomSuppressed(float currentTime)
        {
            return currentTime < _suppressZoomUntilTime;
        }

        #endregion

        #region Zoom
        
        private void ProcessZoom(float deltaTime)
        {
            if (!enableZoom) return;

            float currentTime = useUnscaledTime ? Time.unscaledTime : Time.time;
            if (IsZoomSuppressed(currentTime)) return;
            
            float scrollInput = _scrollDelta.y;
            if (Mathf.Abs(scrollInput) < 0.01f) return;
            
            // Normalize scroll input
            scrollInput = Mathf.Sign(scrollInput);
            
            // Calculate zoom change
            float zoomChange = -scrollInput * zoomSpeed * deltaTime * 10f;
            
            // Zoom to cursor position
            if (zoomToCursor && scrollInput > 0) // Only when zooming in
            {
                Vector3 cursorWorldPos = GetMouseWorldPosition();
                Vector3 directionToCursor = cursorWorldPos - _targetPosition;
                directionToCursor.y = 0f;
                
                // Move camera towards cursor while zooming in
                float zoomFactor = Mathf.Abs(zoomChange) / (_targetZoom - minZoomDistance + 1f);
                _targetPosition += directionToCursor * zoomFactor * 0.1f;
            }
            
            _targetZoom = Mathf.Clamp(_targetZoom + zoomChange, minZoomDistance, maxZoomDistance);
        }
        
        #endregion
        
        #region Rotation
        
        private void ProcessRotation(float deltaTime)
        {
            if (!enableRotation || !_isDraggingRotate) return;
            
            // Horizontal rotation (yaw)
            _targetYaw += _mouseDelta.x * rotationSpeed;
            
            // Normalize yaw to 0-360
            if (_targetYaw < 0f) _targetYaw += 360f;
            if (_targetYaw >= 360f) _targetYaw -= 360f;
            
            // Vertical rotation (pitch/tilt)
            if (enableTilt)
            {
                _targetPitch -= _mouseDelta.y * rotationSpeed;
                _targetPitch = Mathf.Clamp(_targetPitch, minTiltAngle, maxTiltAngle);
            }
        }
        
        #endregion
        
        #region Bookmarks
        
        private void ProcessBookmarks()
        {
            if (!enableBookmarks) return;
            if (bookmarkSlotInputs == null) return;
            
            // Check bookmark slot InputActions
            for (int i = 0; i < bookmarkSlots && i < bookmarkSlotInputs.Length; i++)
            {
                var slotInput = bookmarkSlotInputs[i];
                if (slotInput == null) continue;
                
                if (slotInput.WasPressedThisFrame())
                {
                    if (_isBookmarkModifierHeld)
                    {
                        // Save bookmark
                        SaveBookmark(i);
                    }
                    else
                    {
                        // Load bookmark
                        LoadBookmark(i);
                    }
                }
            }
        }
        
        /// <summary>
        /// Save current camera state to a bookmark slot
        /// </summary>
        public void SaveBookmark(int slot)
        {
            if (slot < 0 || slot >= _bookmarks.Length) return;
            
            _bookmarks[slot] = new CameraBookmark
            {
                isSet = true,
                position = _targetPosition,
                zoom = _targetZoom,
                yaw = _targetYaw,
                pitch = _targetPitch
            };
            
            Debug.Log($"[RTSCamera] Saved bookmark {slot + 1}");
        }
        
        /// <summary>
        /// Load camera state from a bookmark slot
        /// </summary>
        public void LoadBookmark(int slot)
        {
            if (slot < 0 || slot >= _bookmarks.Length) return;
            if (!_bookmarks[slot].isSet) return;
            
            var bookmark = _bookmarks[slot];
            
            if (smoothBookmarkTransition)
            {
                StartTransition(bookmark.position, bookmark.zoom, bookmark.yaw, bookmark.pitch);
            }
            else
            {
                _targetPosition = bookmark.position;
                _targetZoom = bookmark.zoom;
                _targetYaw = bookmark.yaw;
                _targetPitch = bookmark.pitch;
            }
            
            Debug.Log($"[RTSCamera] Loaded bookmark {slot + 1}");
        }
        
        #endregion
        
        #region Focus/Follow
        
        /// <summary>
        /// Focus camera on a world position
        /// </summary>
        public void FocusOnPosition(Vector3 position, bool instant = false)
        {
            if (instant || !smoothFocusTransition)
            {
                _targetPosition = new Vector3(position.x, _targetPosition.y, position.z);
            }
            else
            {
                StartTransition(new Vector3(position.x, _targetPosition.y, position.z), _targetZoom, _targetYaw, _targetPitch);
            }
        }
        
        /// <summary>
        /// Focus camera on a transform
        /// </summary>
        public void FocusOnTarget(Transform target, bool instant = false)
        {
            if (target == null) return;
            FocusOnPosition(target.position, instant);
        }
        
        /// <summary>
        /// Start following a target
        /// </summary>
        public void StartFollowing(Transform target)
        {
            followTarget = target;
            followMode = true;
            ResetAutoOrbit();
        }
        
        /// <summary>
        /// Stop following
        /// </summary>
        public void StopFollowing()
        {
            followMode = false;
        }
        
        /// <summary>
        /// Set the follow target without changing follow mode
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;
        }
        
        /// <summary>
        /// Set follow mode enabled/disabled
        /// </summary>
        public void SetFollowMode(bool enabled)
        {
            followMode = enabled;
        }
        
        /// <summary>
        /// Get current follow mode state
        /// </summary>
        public bool IsFollowModeEnabled => followMode;

        /// <summary>
        /// Get whether auto-orbit is currently enabled.
        /// </summary>
        public bool IsAutoOrbitEnabled => enableAutoOrbit;

        /// <summary>
        /// Enable or disable auto-orbit at runtime.
        /// </summary>
        public void SetAutoOrbit(bool enabled)
        {
            enableAutoOrbit = enabled;
            if (enabled) ResetAutoOrbit();
        }
        
        /// <summary>
        /// Get current follow target
        /// </summary>
        public Transform FollowTarget => followTarget;
        
        /// <summary>
        /// Check if camera control is currently active
        /// </summary>
        public bool IsControlActive => enabled && _isControlActive;
        
        // Internal flag for external systems to temporarily disable control
        private bool _isControlActive = true;
        
        /// <summary>
        /// Enable or disable camera control. When disabled, the camera stops processing input
        /// but maintains its current state. Useful for integrating with other camera systems.
        /// </summary>
        /// <param name="active">True to enable camera control, false to disable.</param>
        public void SetControlActive(bool active)
        {
            _isControlActive = active;
            
            if (active)
            {
                OnControlEnabled?.Invoke(this);
            }
            else
            {
                OnControlDisabled?.Invoke(this);
            }
        }
        
        /// <summary>
        /// Get the current camera position managed by this controller.
        /// </summary>
        public Vector3 CurrentPosition => _targetPosition;
        
        /// <summary>
        /// Get the current camera rotation managed by this controller.
        /// </summary>
        public Quaternion CurrentRotation => Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        
        /// <summary>
        /// Get the current zoom distance.
        /// </summary>
        public float CurrentZoom => _currentZoom;
        
        /// <summary>
        /// Sync the controller's internal state to match the camera's current transform.
        /// Useful when returning control from another camera system.
        /// </summary>
        public void SyncFromCameraTransform()
        {
            if (_cameraTransform == null) return;
            
            _targetPosition = _cameraTransform.position;
            
            Vector3 euler = _cameraTransform.rotation.eulerAngles;
            _targetYaw = euler.y;
            _currentYaw = _targetYaw;
            _targetPitch = euler.x;
            _currentPitch = _targetPitch;
            
            // Reset velocities
            _currentVelocity = Vector3.zero;
        }
        
        /// <summary>
        /// Event fired when camera control is enabled via SetControlActive(true).
        /// </summary>
        public event Action<RTSCameraController> OnControlEnabled;
        
        /// <summary>
        /// Event fired when camera control is disabled via SetControlActive(false).
        /// </summary>
        public event Action<RTSCameraController> OnControlDisabled;
        
        private void ProcessFollowTarget(float deltaTime)
        {
            if (followTarget == null)
            {
                followMode = false;
                return;
            }

            Vector3 offset = followTargetOffset;
            if (followOffsetInTargetSpace)
            {
                offset = followTarget.TransformDirection(offset);
            }

            float targetY = followUseTargetHeight
                ? followTarget.position.y + followTargetHeightOffset
                : _targetPosition.y;
            Vector3 targetPos = followTarget.position + offset;
            targetPos.y = targetY + offset.y;

            if (enableLookahead)
            {
                if (!_hasFollowTargetPosition)
                {
                    _lastFollowTargetPosition = followTarget.position;
                    _currentLookahead = Vector3.zero;
                    _lookaheadVelocity = Vector3.zero;
                    _hasFollowTargetPosition = true;
                }

                Vector3 delta = followTarget.position - _lastFollowTargetPosition;
                Vector3 velocity = deltaTime > 0f ? delta / deltaTime : Vector3.zero;
                Vector3 rawLookahead = velocity * lookaheadTime;
                rawLookahead.y = 0f;

                if (maxLookaheadDistance > 0f)
                {
                    rawLookahead = Vector3.ClampMagnitude(rawLookahead, maxLookaheadDistance);
                }

                float smoothTime = Mathf.Max(0.01f, lookaheadSmoothTime);
                _currentLookahead = Vector3.SmoothDamp(
                    _currentLookahead,
                    rawLookahead,
                    ref _lookaheadVelocity,
                    smoothTime,
                    Mathf.Infinity,
                    deltaTime
                );

                targetPos += _currentLookahead;
                _lastFollowTargetPosition = followTarget.position;
            }
            else
            {
                _currentLookahead = Vector3.zero;
                _lookaheadVelocity = Vector3.zero;
                _hasFollowTargetPosition = false;
            }

            _targetPosition = Vector3.Lerp(_targetPosition, targetPos, followSmoothness * deltaTime);
        }

        private void ResetFollowLookahead()
        {
            _currentLookahead = Vector3.zero;
            _lookaheadVelocity = Vector3.zero;
            _hasFollowTargetPosition = false;
        }

        #endregion

        #region Auto-Orbit

        /// <summary>
        /// Automatically rotate the camera yaw to stay behind the follow target's
        /// movement direction. Inspired by Batman Arkham / God of War cameras.
        /// </summary>
        private void ProcessAutoOrbit(float deltaTime)
        {
            if (followTarget == null) return;

            Vector3 currentPos = followTarget.position;

            // Initialise last-known position on the first frame
            if (!_autoOrbitHasLastPos)
            {
                _autoOrbitLastTargetPos = currentPos;
                _autoOrbitHasLastPos = true;
                return;
            }

            Vector3 delta = currentPos - _autoOrbitLastTargetPos;
            delta.y = 0f; // ignore vertical movement
            float speed = deltaTime > 0f ? delta.magnitude / deltaTime : 0f;

            if (speed > autoOrbitDeadZone)
            {
                // Target is moving — compute desired yaw (angle of movement direction)
                // Atan2 gives the angle from +Z axis, clockwise = positive, which matches Unity yaw
                float desiredYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg + 180f + autoOrbitYawOffset;
                desiredYaw = NormalizeAngle(desiredYaw);

                // Smoothly rotate towards the desired yaw
                _targetYaw = Mathf.SmoothDampAngle(
                    _targetYaw,
                    desiredYaw,
                    ref _autoOrbitCurrentYawVelocity,
                    1f / Mathf.Max(0.01f, autoOrbitSpeed),
                    Mathf.Infinity,
                    deltaTime
                );

                // Reset stopped timer while moving
                _autoOrbitStoppedTimer = 0f;
            }
            else
            {
                // Target stopped moving
                if (autoOrbitReturnDelay > 0f)
                {
                    _autoOrbitStoppedTimer += deltaTime;

                    if (_autoOrbitStoppedTimer >= autoOrbitReturnDelay)
                    {
                        // Return to target's facing direction (or hold if no return configured)
                        float returnYaw;
                        if (autoOrbitReturnToTargetForward)
                        {
                            // behind the target = target's forward + 180
                            Vector3 fwd = followTarget.forward;
                            fwd.y = 0f;
                            returnYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + 180f + autoOrbitYawOffset;
                        }
                        else
                        {
                            // hold current angle
                            returnYaw = _targetYaw;
                        }

                        returnYaw = NormalizeAngle(returnYaw);
                        _targetYaw = Mathf.SmoothDampAngle(
                            _targetYaw,
                            returnYaw,
                            ref _autoOrbitCurrentYawVelocity,
                            1f / Mathf.Max(0.01f, autoOrbitSpeed * 0.5f),
                            Mathf.Infinity,
                            deltaTime
                        );
                    }
                }
                // else: hold current yaw forever (autoOrbitReturnDelay == 0)
            }

            _autoOrbitLastTargetPos = currentPos;
        }

        /// <summary>
        /// Reset auto-orbit tracking state. Call when switching targets or presets.
        /// </summary>
        private void ResetAutoOrbit()
        {
            _autoOrbitHasLastPos = false;
            _autoOrbitStoppedTimer = 0f;
            _autoOrbitCurrentYawVelocity = 0f;
        }

        /// <summary>
        /// Normalize an angle to the 0-360 range.
        /// </summary>
        private static float NormalizeAngle(float angle)
        {
            while (angle < 0f) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }
        
        #endregion
        
        #region Transitions

        private void StartTimedTransition(
            Vector3 position,
            float zoom,
            float yaw,
            float pitch,
            float duration,
            AnimationCurve curve)
        {
            _isTransitioning = true;
            _isTimedTransition = true;
            _transitionElapsed = 0f;
            _transitionDuration = Mathf.Max(0.01f, duration);
            _transitionCurve = curve;

            _transitionStartPosition = _targetPosition;
            _transitionStartZoom = _targetZoom;
            _transitionStartYaw = _targetYaw;
            _transitionStartPitch = _targetPitch;

            _transitionTargetPosition = position;
            _transitionTargetZoom = zoom;
            _transitionTargetYaw = yaw;
            _transitionTargetPitch = pitch;
        }
        
        private void StartTransition(Vector3 position, float zoom, float yaw, float pitch)
        {
            _isTransitioning = true;
            _isTimedTransition = false;
            _transitionElapsed = 0f;
            _transitionDuration = 0f;
            _transitionCurve = null;
            _pendingPresetIndex = -1;
            _pendingFollowTarget = null;
            _pendingFollowMode = false;
            _transitionTargetPosition = position;
            _transitionTargetZoom = zoom;
            _transitionTargetYaw = yaw;
            _transitionTargetPitch = pitch;
        }
        
        private void ProcessTransition(float deltaTime)
        {
            if (!_isTransitioning) return;

            if (_isTimedTransition)
            {
                _transitionElapsed += deltaTime;
                float normalized = Mathf.Clamp01(_transitionElapsed / _transitionDuration);
                float eased = _transitionCurve != null ? _transitionCurve.Evaluate(normalized) : normalized;

                _targetPosition = Vector3.Lerp(_transitionStartPosition, _transitionTargetPosition, eased);
                _targetZoom = Mathf.Lerp(_transitionStartZoom, _transitionTargetZoom, eased);
                _targetYaw = Mathf.LerpAngle(_transitionStartYaw, _transitionTargetYaw, eased);
                _targetPitch = Mathf.Lerp(_transitionStartPitch, _transitionTargetPitch, eased);

                if (normalized >= 1f)
                {
                    _targetPosition = _transitionTargetPosition;
                    _targetZoom = _transitionTargetZoom;
                    _targetYaw = _transitionTargetYaw;
                    _targetPitch = _transitionTargetPitch;
                    _isTransitioning = false;
                    _isTimedTransition = false;
                    CompleteTransition();
                }

                return;
            }

            float speed = bookmarkTransitionSpeed * deltaTime;

            _targetPosition = Vector3.Lerp(_targetPosition, _transitionTargetPosition, speed);
            _targetZoom = Mathf.Lerp(_targetZoom, _transitionTargetZoom, speed);
            _targetYaw = Mathf.LerpAngle(_targetYaw, _transitionTargetYaw, speed);
            _targetPitch = Mathf.Lerp(_targetPitch, _transitionTargetPitch, speed);

            // Check if transition is complete
            float positionDist = Vector3.Distance(_targetPosition, _transitionTargetPosition);
            float zoomDist = Mathf.Abs(_targetZoom - _transitionTargetZoom);
            float yawDist = Mathf.Abs(Mathf.DeltaAngle(_targetYaw, _transitionTargetYaw));
            float pitchDist = Mathf.Abs(_targetPitch - _transitionTargetPitch);

            if (positionDist < 0.01f && zoomDist < 0.1f && yawDist < 0.5f && pitchDist < 0.5f)
            {
                _targetPosition = _transitionTargetPosition;
                _targetZoom = _transitionTargetZoom;
                _targetYaw = _transitionTargetYaw;
                _targetPitch = _transitionTargetPitch;
                _isTransitioning = false;
                CompleteTransition();
            }
        }

        private void CompleteTransition()
        {
            if (_pendingPresetIndex < 0) return;

            RTSCameraPreset preset = GetPresetAt(_pendingPresetIndex);
            if (preset == null)
            {
                _pendingPresetIndex = -1;
                _pendingFollowTarget = null;
                _pendingFollowMode = false;
                return;
            }

            activePresetIndex = _pendingPresetIndex;
            _pendingPresetIndex = -1;

            ApplyPreset(preset);

            followTarget = _pendingFollowTarget;
            _pendingFollowTarget = null;

            followMode = _pendingFollowMode && followTarget != null;
            _pendingFollowMode = false;

            if (followMode)
            {
                ResetFollowLookahead();
            }

            SavePresetState(preset);
        }

        private void ApplyPresetImmediate(int presetIndex, Transform followOverride, bool useCachedState)
        {
            RTSCameraPreset currentPreset = ActivePreset;
            if (currentPreset != null)
            {
                SavePresetState(currentPreset);
            }

            RTSCameraPreset preset = GetPresetAt(presetIndex);
            if (preset == null) return;

            Transform desiredFollowTarget = followOverride != null ? followOverride : followTarget;
            bool desiredFollowMode = preset.enableFollowMode && desiredFollowTarget != null;

            CameraPresetState targetState = useCachedState && TryGetPresetState(preset, out CameraPresetState cached)
                ? cached
                : GetDefaultPresetState(preset, desiredFollowTarget);

            activePresetIndex = presetIndex;
            ApplyPreset(preset);

            followTarget = desiredFollowTarget;
            followMode = desiredFollowMode;

            _targetPosition = targetState.position;
            _targetZoom = targetState.zoom;
            _targetYaw = targetState.yaw;
            _targetPitch = targetState.pitch;

            if (followMode)
            {
                ResetFollowLookahead();
            }

            SavePresetState(preset);
        }

        private void SavePresetState(RTSCameraPreset preset)
        {
            if (_presetStates == null || preset == null) return;

            _presetStates[preset] = new CameraPresetState
            {
                isSet = true,
                position = _targetPosition,
                zoom = _targetZoom,
                yaw = _targetYaw,
                pitch = _targetPitch
            };
        }

        private bool TryGetPresetState(RTSCameraPreset preset, out CameraPresetState state)
        {
            state = default;
            if (_presetStates == null || preset == null) return false;

            return _presetStates.TryGetValue(preset, out state) && state.isSet;
        }

        private CameraPresetState GetDefaultPresetState(RTSCameraPreset preset, Transform desiredFollowTarget)
        {
            // For presets that don't enable rotation, use the preset's initialYaw
            // so the camera resets to a known orientation (e.g., ClassicRTS expects yaw=0)
            float targetYaw = preset.enableRotation ? _targetYaw : preset.initialYaw;
            
            CameraPresetState state = new CameraPresetState
            {
                isSet = true,
                // Use initialZoom/initialPitch as the default starting point for this preset
                zoom = preset.initialZoom,
                pitch = preset.initialPitch,
                yaw = targetYaw
            };

            state.position = preset.enableFollowMode
                ? GetFollowPivotForTransition(desiredFollowTarget)
                : GetRtsPivotForTransition();

            return state;
        }

        private Vector3 GetFollowPivotForTransition(Transform target)
        {
            if (target == null) return _targetPosition;

            Vector3 offset = followTargetOffset;
            if (followOffsetInTargetSpace)
            {
                offset = target.TransformDirection(offset);
            }

            float targetY = followUseTargetHeight
                ? target.position.y + followTargetHeightOffset
                : _targetPosition.y;

            Vector3 targetPos = target.position + offset;
            targetPos.y = targetY + offset.y;
            return targetPos;
        }

        private Vector3 GetRtsPivotForTransition()
        {
            Vector3 pivot = _targetPosition;
            if (Physics.Raycast(pivot + Vector3.up * 1000f, Vector3.down, out RaycastHit hit, 2000f, groundLayer))
            {
                pivot.y = hit.point.y + heightAboveGround;
            }

            return pivot;
        }
        
        #endregion
        
        #region Bounds & Terrain
        
        private void ApplyBounds()
        {
            _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);
            _targetPosition.z = Mathf.Clamp(_targetPosition.z, minZ, maxZ);
        }
        
        private void ApplyTerrainFollowing()
        {
            // Raycast down to find terrain height
            if (Physics.Raycast(_targetPosition + Vector3.up * 1000f, Vector3.down, out RaycastHit hit, 2000f, groundLayer))
            {
                _targetPosition.y = hit.point.y + heightAboveGround;
            }
        }
        
        #endregion
        
        #region Camera Transform Update
        
        private void UpdateCameraTransform(float deltaTime)
        {
            // Smooth zoom
            if (smoothZoom)
            {
                _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, zoomSmoothSpeed * deltaTime);
            }
            else
            {
                _currentZoom = _targetZoom;
            }
            
            // Smooth rotation
            if (smoothRotation)
            {
                _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, rotationSmoothSpeed * deltaTime);
                _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, rotationSmoothSpeed * deltaTime);
            }
            else
            {
                _currentYaw = _targetYaw;
                _currentPitch = _targetPitch;
            }
            
            // Calculate camera position based on pivot point, zoom, and angles
            // The pivot point (_targetPosition) is on the ground where the camera looks
            float pitchRad = _currentPitch * Mathf.Deg2Rad;
            float yawRad = _currentYaw * Mathf.Deg2Rad;
            
            // Calculate offset from pivot to camera
            float horizontalDist = _currentZoom * Mathf.Cos(pitchRad);
            float verticalDist = _currentZoom * Mathf.Sin(pitchRad);
            
            Vector3 cameraOffset = new Vector3(
                -Mathf.Sin(yawRad) * horizontalDist,
                verticalDist,
                -Mathf.Cos(yawRad) * horizontalDist
            );
            
            // Position camera
            Vector3 cameraPosition = _targetPosition + cameraOffset;
            cameraPosition = ResolveCameraCollision(_targetPosition, cameraPosition);
            cameraPosition = ResolveLineOfSightGround(_targetPosition, cameraPosition);
            
            // Apply to transform
            if (_camera != null)
            {
                _camera.transform.position = cameraPosition;
                _camera.transform.rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            }
            else
            {
                transform.position = cameraPosition;
                transform.rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            }
        }
        
        #endregion

        #region Camera Collision

        private Vector3 ResolveCameraCollision(Vector3 pivot, Vector3 desiredPosition)
        {
            if (!enableCameraCollision) return desiredPosition;

            Vector3 direction = desiredPosition - pivot;
            float distance = direction.magnitude;
            if (distance <= 0.001f) return desiredPosition;

            direction /= distance;
            float castDistance = distance;
            QueryTriggerInteraction query = cameraCollisionIgnoreTriggers
                ? QueryTriggerInteraction.Ignore
                : QueryTriggerInteraction.Collide;

            if (Physics.SphereCast(pivot, cameraCollisionRadius, direction, out RaycastHit hit, castDistance, cameraCollisionLayers, query))
            {
                float adjustedDistance = Mathf.Max(0f, hit.distance - cameraCollisionBuffer);
                return pivot + direction * adjustedDistance;
            }

            return desiredPosition;
        }

        #endregion

        #region Line of Sight

        private Vector3 ResolveLineOfSightGround(Vector3 pivot, Vector3 desiredPosition)
        {
            if (!enableLineOfSightGroundReposition) return desiredPosition;
            if (_currentPitch >= 0f) return desiredPosition;

            Vector3 direction = desiredPosition - pivot;
            float distance = direction.magnitude;
            if (distance <= 0.001f) return desiredPosition;

            direction /= distance;

            if (Physics.Raycast(pivot, direction, out RaycastHit hit, distance, groundLayer, QueryTriggerInteraction.Ignore))
            {
                float adjustedDistance = Mathf.Max(0f, hit.distance - lineOfSightGroundBuffer);
                return pivot + direction * adjustedDistance;
            }

            return desiredPosition;
        }

        #endregion
        
        #region Public API
        
        /// <summary>
        /// Get current camera position (pivot point)
        /// </summary>
        public Vector3 GetPosition() => _targetPosition;
        
        /// <summary>
        /// Set camera position instantly
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            _targetPosition = position;
        }
        
        /// <summary>
        /// Get current zoom level
        /// </summary>
        public float GetZoom() => _currentZoom;
        
        /// <summary>
        /// Set zoom level instantly
        /// </summary>
        public void SetZoom(float zoom)
        {
            _targetZoom = Mathf.Clamp(zoom, minZoomDistance, maxZoomDistance);
            _currentZoom = _targetZoom;
        }
        
        /// <summary>
        /// Get current rotation (yaw, pitch)
        /// </summary>
        public Vector2 GetRotation() => new Vector2(_currentYaw, _currentPitch);
        
        /// <summary>
        /// Set rotation instantly
        /// </summary>
        public void SetRotation(float yaw, float pitch)
        {
            _targetYaw = yaw;
            _targetPitch = Mathf.Clamp(pitch, minTiltAngle, maxTiltAngle);
            _currentYaw = _targetYaw;
            _currentPitch = _targetPitch;
        }
        
        /// <summary>
        /// Set camera bounds
        /// </summary>
        public void SetBounds(float minX, float maxX, float minZ, float maxZ)
        {
            this.minX = minX;
            this.maxX = maxX;
            this.minZ = minZ;
            this.maxZ = maxZ;
        }

        /// <summary>
        /// Get current camera movement bounds.
        /// </summary>
        public void GetBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = this.minX;
            maxX = this.maxX;
            minZ = this.minZ;
            maxZ = this.maxZ;
        }
        
        /// <summary>
        /// Enable or disable all camera controls
        /// </summary>
        public void SetControlsEnabled(bool enabled)
        {
            enableEdgePan = enabled;
            enableDragPan = enabled;
            enableKeyboardPan = enabled;
            enableZoom = enabled;
            enableRotation = enabled;
        }
        
        /// <summary>
        /// Shake the camera (for impacts, explosions, etc.)
        /// </summary>
        public void Shake(float intensity, float duration)
        {
            StartCoroutine(ShakeCoroutine(intensity, duration));
        }
        
        private System.Collections.IEnumerator ShakeCoroutine(float intensity, float duration)
        {
            float elapsed = 0f;
            Vector3 originalPosition = _targetPosition;
            
            while (elapsed < duration)
            {
                float x = UnityEngine.Random.Range(-1f, 1f) * intensity;
                float z = UnityEngine.Random.Range(-1f, 1f) * intensity;
                
                _targetPosition = originalPosition + new Vector3(x, 0f, z);
                
                elapsed += Time.deltaTime;
                intensity = Mathf.Lerp(intensity, 0f, elapsed / duration);
                
                yield return null;
            }
            
            _targetPosition = originalPosition;
        }
        
        #endregion
        
        #region Editor Helpers
        
        #if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure min values are less than max values
            if (minZoomDistance > maxZoomDistance)
                minZoomDistance = maxZoomDistance;
            
            if (minTiltAngle > maxTiltAngle)
                minTiltAngle = maxTiltAngle;

            minTiltAngle = Mathf.Clamp(minTiltAngle, -89f, 89f);
            maxTiltAngle = Mathf.Clamp(maxTiltAngle, -89f, 89f);
            
            if (minX > maxX)
                minX = maxX;
            
            if (minZ > maxZ)
                minZ = maxZ;
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!enableBounds) return;
            
            // Draw bounds
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Vector3 center = new Vector3((minX + maxX) / 2f, transform.position.y, (minZ + maxZ) / 2f);
            Vector3 size = new Vector3(maxX - minX, 1f, maxZ - minZ);
            Gizmos.DrawCube(center, size);
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(center, size);
        }
        #endif
        
        #endregion
    }
}
