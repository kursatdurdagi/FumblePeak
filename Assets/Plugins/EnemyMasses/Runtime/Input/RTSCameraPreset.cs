using UnityEngine;
using UnityEngine.InputSystem;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// ScriptableObject-based camera preset for RTSCameraController.
    /// Users can create custom presets for different camera styles (RTS, Third Person, etc.)
    /// and switch between them at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "New Camera Preset", menuName = "Enemy Masses/Camera Preset", order = 100)]
    [Icon("Assets/Plugins/EnemyMasses/Editor/Icons/CamEnemyMasses.png")]
    public class RTSCameraPreset : ScriptableObject
    {
        [Header("Preset Info")]
        [Tooltip("Display name for this preset (shown in UI/logs).")]
        public string presetName = "New Preset";

        [Tooltip("Optional description of this preset's purpose.")]
        [TextArea(2, 4)]
        public string description = "";

        [Header("Initial Setup")]
        [Tooltip("Initial camera height/zoom distance.")]
        public float initialZoom = 20f;

        [Tooltip("Initial camera pitch angle (45 = typical RTS angle).")]
        public float initialPitch = 45f;

        [Tooltip("Initial camera yaw angle.")]
        public float initialYaw = 0f;

        [Header("Movement Settings")]
        [Tooltip("Base camera movement speed.")]
        public float moveSpeed = 20f;

        [Tooltip("Movement speed when holding shift (sprint).")]
        public float fastMoveSpeed = 40f;

        [Tooltip("Movement acceleration for smooth starts/stops.")]
        public float moveAcceleration = 8f;

        [Tooltip("Movement deceleration when input stops.")]
        public float moveDeceleration = 12f;

        [Tooltip("Enable smooth movement interpolation.")]
        public bool smoothMovement = true;

        [Header("Edge Panning")]
        [Tooltip("Enable edge panning (cursor at screen edge moves camera).")]
        public bool enableEdgePan = true;

        [Tooltip("Distance from screen edge to trigger panning (in pixels).")]
        [Range(1, 50)]
        public int edgePanBorder = 10;

        [Tooltip("Edge pan speed multiplier.")]
        [Range(0.1f, 2f)]
        public float edgePanSpeedMultiplier = 1f;

        [Tooltip("Disable edge pan when cursor is outside game window.")]
        public bool disableEdgePanOutsideWindow = true;

        [Header("Drag Panning")]
        [Tooltip("Enable drag panning (hold mouse button + drag to pan).")]
        public bool enableDragPan = true;

        [Tooltip("Which mouse button triggers drag pan.")]
        public DragPanButton dragPanButton = DragPanButton.Middle;

        [Tooltip("Invert drag pan direction (drag left = camera moves left).")]
        public bool invertDragPan = false;

        [Tooltip("Drag pan sensitivity.")]
        [Range(0.1f, 5f)]
        public float dragPanSensitivity = 1f;

        [Header("Keyboard Movement")]
        [Tooltip("Enable WASD/Arrow key movement.")]
        public bool enableKeyboardPan = true;

        [Header("Zoom Settings")]
        [Tooltip("Enable mouse scroll zoom.")]
        public bool enableZoom = true;

        [Tooltip("Zoom speed.")]
        [Range(1f, 50f)]
        public float zoomSpeed = 10f;

        [Tooltip("Minimum zoom distance (closest to ground).")]
        public float minZoomDistance = 5f;

        [Tooltip("Maximum zoom distance (furthest from ground).")]
        public float maxZoomDistance = 100f;

        [Tooltip("Smooth zoom interpolation.")]
        public bool smoothZoom = true;

        [Tooltip("Zoom smoothing speed.")]
        [Range(1f, 20f)]
        public float zoomSmoothSpeed = 8f;

        [Tooltip("Zoom towards mouse cursor (like Google Maps).")]
        public bool zoomToCursor = true;

        [Header("Rotation Settings")]
        [Tooltip("Enable camera rotation.")]
        public bool enableRotation = true;

        [Tooltip("Which mouse button triggers rotation.")]
        public DragPanButton rotationButton = DragPanButton.Right;

        [Tooltip("Rotation speed.")]
        [Range(0.1f, 5f)]
        public float rotationSpeed = 1f;

        [Tooltip("Rotate camera without holding the rotation input.")]
        public bool rotateWithoutButton = false;

        [Tooltip("Enable vertical tilt (pitch).")]
        public bool enableTilt = true;

        [Tooltip("Minimum tilt angle (negative = look upward).")]
        [Range(-89f, 89f)]
        public float minTiltAngle = 30f;

        [Tooltip("Maximum tilt angle (looking more horizontal).")]
        [Range(-89f, 89f)]
        public float maxTiltAngle = 80f;

        [Tooltip("Smooth rotation interpolation.")]
        public bool smoothRotation = true;

        [Tooltip("Rotation smoothing speed.")]
        [Range(1f, 20f)]
        public float rotationSmoothSpeed = 10f;

        [Header("Camera Bounds")]
        [Tooltip("Enable camera movement bounds.")]
        public bool enableBounds = true;

        [Header("Focus & Follow Settings")]
        [Tooltip("Enable focusing on targets.")]
        public bool enableFocus = true;

        [Tooltip("Smooth transition when focusing.")]
        public bool smoothFocusTransition = true;

        [Tooltip("Focus transition speed.")]
        [Range(1f, 20f)]
        public float focusTransitionSpeed = 8f;

        [Tooltip("Enable follow mode by default with this preset.")]
        public bool enableFollowMode = false;

        [Tooltip("Follow smoothness.")]
        [Range(1f, 20f)]
        public float followSmoothness = 5f;

        [Tooltip("Offset applied to the follow target position (camera pivot).")]
        public Vector3 followTargetOffset = Vector3.zero;

        [Tooltip("Interpret followTargetOffset in the target's local space.")]
        public bool followOffsetInTargetSpace = false;

        [Tooltip("Use the follow target's Y position (plus offset) as the camera pivot height.")]
        public bool followUseTargetHeight = false;

        [Tooltip("Vertical offset applied when followUseTargetHeight is enabled.")]
        public float followTargetHeightOffset = 1.5f;

        [Header("Auto-Orbit (Arkham-Style)")]
        [Tooltip("Enable automatic camera orbit that follows the target's movement direction. " +
                 "The camera stays behind the target without requiring manual rotation input. " +
                 "Great for action/combat games (Batman Arkham, God of War, etc.).")]
        public bool enableAutoOrbit = false;

        [Tooltip("How fast the camera rotates to stay behind the moving target (degrees/sec equivalent via smooth damp).")]
        [Range(0.5f, 20f)]
        public float autoOrbitSpeed = 5f;

        [Tooltip("Minimum target speed (units/sec) before the camera starts orbiting. " +
                 "Prevents jitter when the target is nearly stationary.")]
        [Range(0.01f, 3f)]
        public float autoOrbitDeadZone = 0.15f;

        [Tooltip("Yaw offset from directly behind the target's movement direction. " +
                 "0 = directly behind, positive = offset to the right.")]
        [Range(-180f, 180f)]
        public float autoOrbitYawOffset = 0f;

        [Tooltip("When the target stops moving, smoothly return to the target's facing direction " +
                 "after this delay (seconds). Set to 0 to hold the last orbit angle indefinitely.")]
        [Range(0f, 5f)]
        public float autoOrbitReturnDelay = 0f;

        [Tooltip("Use the target transform's forward direction as the return angle when the " +
                 "target stops moving (requires autoOrbitReturnDelay > 0).")]
        public bool autoOrbitReturnToTargetForward = true;

        [Header("Look-Ahead")]
        [Tooltip("Enable smooth look-ahead based on target movement direction.")]
        public bool enableLookahead = false;

        [Tooltip("Seconds ahead to project the target's movement for look-ahead.")]
        [Range(0f, 1.5f)]
        public float lookaheadTime = 0.2f;

        [Tooltip("Smooth time for look-ahead offset changes.")]
        [Range(0.01f, 2f)]
        public float lookaheadSmoothTime = 0.15f;

        [Tooltip("Maximum distance for look-ahead offset.")]
        [Range(0f, 20f)]
        public float maxLookaheadDistance = 3f;

        [Header("Camera Collision")]
        [Tooltip("Prevent the camera from clipping through geometry.")]
        public bool enableCameraCollision = false;

        [Tooltip("Collision probe radius for the camera.")]
        [Range(0.05f, 2f)]
        public float cameraCollisionRadius = 0.25f;

        [Tooltip("Extra distance to keep from collision surfaces.")]
        [Range(0f, 1f)]
        public float cameraCollisionBuffer = 0.1f;

        [Tooltip("Ignore trigger colliders when resolving camera collision.")]
        public bool cameraCollisionIgnoreTriggers = true;

        [Tooltip("When looking upward (negative pitch), keep the camera above ground by moving it along the line of sight.")]
        public bool enableLineOfSightGroundReposition = false;

        [Tooltip("Extra distance to keep from the ground when line-of-sight repositioning is used.")]
        [Range(0f, 1f)]
        public float lineOfSightGroundBuffer = 0.1f;

        [Header("Advanced")]
        [Tooltip("Enable terrain following (camera stays at fixed height above terrain).")]
        public bool terrainFollowing = false;

        [Tooltip("Height above ground to maintain.")]
        public float heightAboveGround = 0f;

        /// <summary>
        /// Mouse button options for drag operations.
        /// </summary>
        public enum DragPanButton
        {
            Left = 0,
            Right = 1,
            Middle = 2
        }

        /// <summary>
        /// Get the InputAction binding path for the specified drag button.
        /// </summary>
        public static string GetMouseButtonPath(DragPanButton button)
        {
            return button switch
            {
                DragPanButton.Left => "<Mouse>/leftButton",
                DragPanButton.Right => "<Mouse>/rightButton",
                DragPanButton.Middle => "<Mouse>/middleButton",
                _ => "<Mouse>/middleButton"
            };
        }

        #region Factory Methods for Common Presets

        /// <summary>
        /// Create a Classic RTS preset (StarCraft, Age of Empires style).
        /// </summary>
        public static RTSCameraPreset CreateClassicRTS()
        {
            var preset = CreateInstance<RTSCameraPreset>();
            preset.presetName = "Classic RTS";
            preset.description = "Edge pan + Middle mouse drag + Scroll zoom (StarCraft, Age of Empires)";
            
            preset.enableEdgePan = true;
            preset.enableDragPan = true;
            preset.dragPanButton = DragPanButton.Middle;
            preset.enableKeyboardPan = true;
            preset.enableRotation = false;
            preset.rotateWithoutButton = false;
            preset.enableTilt = false;
            preset.zoomToCursor = false;
            preset.enableBounds = true;
            preset.enableFollowMode = false;
            
            preset.initialZoom = 20f;
            preset.initialPitch = 45f;
            
            return preset;
        }

        /// <summary>
        /// Create a Modern RTS preset (Company of Heroes style).
        /// </summary>
        public static RTSCameraPreset CreateModernRTS()
        {
            var preset = CreateInstance<RTSCameraPreset>();
            preset.presetName = "Modern RTS";
            preset.description = "WASD movement + Middle mouse drag rotate + Scroll zoom (Company of Heroes)";
            
            preset.enableEdgePan = true;
            preset.enableDragPan = false;
            preset.enableKeyboardPan = true;
            preset.enableRotation = true;
            preset.rotationButton = DragPanButton.Middle;
            preset.rotateWithoutButton = false;
            preset.enableTilt = true;
            preset.zoomToCursor = true;
            preset.enableBounds = true;
            preset.enableFollowMode = false;
            
            preset.initialZoom = 20f;
            preset.initialPitch = 45f;
            
            return preset;
        }

        /// <summary>
        /// Create a Third Person / Action RPG preset.
        /// </summary>
        public static RTSCameraPreset CreateThirdPerson()
        {
            var preset = CreateInstance<RTSCameraPreset>();
            preset.presetName = "Third Person";
            preset.description = "Follow target + Right mouse rotate + Scroll zoom (Action RPG)";
            
            preset.enableEdgePan = false;
            preset.enableDragPan = false;
            preset.enableKeyboardPan = false;
            preset.enableZoom = true;
            preset.enableRotation = true;
            preset.rotationButton = DragPanButton.Right;
            preset.enableTilt = true;
            preset.zoomToCursor = false;
            preset.smoothMovement = true;
            preset.smoothRotation = true;
            preset.smoothZoom = true;
            
            preset.minTiltAngle = 15f;
            preset.maxTiltAngle = 70f;
            preset.minZoomDistance = 2.5f;
            preset.maxZoomDistance = 12f;
            preset.initialZoom = 6f;
            preset.initialPitch = 25f;
            
            preset.enableBounds = false;
            preset.terrainFollowing = false;
            preset.enableFollowMode = true;
            preset.enableCameraCollision = true;
            preset.enableLineOfSightGroundReposition = true;
            preset.followUseTargetHeight = true;
            preset.followTargetHeightOffset = 1.5f;
            preset.followSmoothness = 10f;
            preset.followTargetOffset = Vector3.zero;
            preset.followOffsetInTargetSpace = false;
            preset.enableLookahead = false;
            preset.lookaheadTime = 0.2f;
            preset.lookaheadSmoothTime = 0.15f;
            preset.maxLookaheadDistance = 3f;
            
            return preset;
        }

        /// <summary>
        /// Create a Total War style preset.
        /// </summary>
        public static RTSCameraPreset CreateTotalWar()
        {
            var preset = CreateInstance<RTSCameraPreset>();
            preset.presetName = "Total War";
            preset.description = "Edge pan + Right drag pan + Middle drag rotate + Scroll zoom";
            
            preset.enableEdgePan = true;
            preset.enableDragPan = true;
            preset.dragPanButton = DragPanButton.Right;
            preset.enableKeyboardPan = true;
            preset.enableRotation = true;
            preset.rotationButton = DragPanButton.Middle;
            preset.rotateWithoutButton = false;
            preset.enableTilt = true;
            preset.zoomToCursor = true;
            preset.enableBounds = true;
            preset.enableFollowMode = false;
            
            preset.initialZoom = 20f;
            preset.initialPitch = 45f;
            
            return preset;
        }

        /// <summary>
        /// Create a Supreme Commander style preset.
        /// </summary>
        public static RTSCameraPreset CreateSupremeCommander()
        {
            var preset = CreateInstance<RTSCameraPreset>();
            preset.presetName = "Supreme Commander";
            preset.description = "Edge pan + Middle drag pan + Right drag rotate + Scroll zoom";
            
            preset.enableEdgePan = true;
            preset.enableDragPan = true;
            preset.dragPanButton = DragPanButton.Middle;
            preset.enableKeyboardPan = true;
            preset.enableRotation = true;
            preset.rotationButton = DragPanButton.Right;
            preset.rotateWithoutButton = false;
            preset.enableTilt = true;
            preset.zoomToCursor = true;
            preset.enableBounds = true;
            preset.enableFollowMode = false;
            
            preset.initialZoom = 20f;
            preset.initialPitch = 45f;
            
            return preset;
        }

        /// <summary>
        /// Create an Arkham-style auto-orbit preset (Batman Arkham, God of War).
        /// Camera automatically rotates to stay behind the target's movement direction.
        /// No manual rotation input required — ideal for controllers and action combat.
        /// </summary>
        public static RTSCameraPreset CreateAutoOrbit()
        {
            var preset = CreateInstance<RTSCameraPreset>();
            preset.presetName = "Auto Orbit";
            preset.description = "Camera auto-orbits behind the target's movement direction (Batman Arkham, God of War). " +
                                 "No manual rotation needed — very controller-friendly.";

            // Disable all RTS-style input
            preset.enableEdgePan = false;
            preset.enableDragPan = false;
            preset.enableKeyboardPan = false;

            // Zoom via scroll
            preset.enableZoom = true;
            preset.zoomToCursor = false;
            preset.smoothZoom = true;
            preset.minZoomDistance = 3f;
            preset.maxZoomDistance = 15f;
            preset.zoomSmoothSpeed = 10f;

            // Disable manual rotation — the auto-orbit handles yaw
            preset.enableRotation = false;
            preset.rotateWithoutButton = false;
            preset.enableTilt = false;

            // Pitch / tilt limits
            preset.minTiltAngle = 10f;
            preset.maxTiltAngle = 60f;

            // Follow target
            preset.enableFollowMode = true;
            preset.followSmoothness = 10f;
            preset.followUseTargetHeight = true;
            preset.followTargetHeightOffset = 1.5f;
            preset.followTargetOffset = Vector3.zero;
            preset.followOffsetInTargetSpace = false;

            // Auto-orbit
            preset.enableAutoOrbit = true;
            preset.autoOrbitSpeed = 5f;
            preset.autoOrbitDeadZone = 0.15f;
            preset.autoOrbitYawOffset = 0f;
            preset.autoOrbitReturnDelay = 1.5f;
            preset.autoOrbitReturnToTargetForward = true;

            // Collision
            preset.enableCameraCollision = true;
            preset.enableLineOfSightGroundReposition = true;
            preset.cameraCollisionRadius = 0.25f;
            preset.cameraCollisionBuffer = 0.1f;

            // Sensible defaults
            preset.initialZoom = 8f;
            preset.initialPitch = 25f;
            preset.initialYaw = 0f;
            preset.enableBounds = false;
            preset.terrainFollowing = false;
            preset.smoothMovement = true;
            preset.smoothRotation = true;

            // Look-ahead (subtle)
            preset.enableLookahead = true;
            preset.lookaheadTime = 0.25f;
            preset.lookaheadSmoothTime = 0.2f;
            preset.maxLookaheadDistance = 2f;

            return preset;
        }

        #endregion

        #region Validation

        private void OnValidate()
        {
            // Ensure min values are less than max values
            if (minZoomDistance > maxZoomDistance)
                minZoomDistance = maxZoomDistance;

            if (minTiltAngle > maxTiltAngle)
                minTiltAngle = maxTiltAngle;

            minTiltAngle = Mathf.Clamp(minTiltAngle, -89f, 89f);
            maxTiltAngle = Mathf.Clamp(maxTiltAngle, -89f, 89f);

            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            fastMoveSpeed = Mathf.Max(moveSpeed, fastMoveSpeed);
            moveAcceleration = Mathf.Max(0.1f, moveAcceleration);
            moveDeceleration = Mathf.Max(0.1f, moveDeceleration);
            zoomSpeed = Mathf.Max(0.1f, zoomSpeed);
            rotationSpeed = Mathf.Max(0.1f, rotationSpeed);
            followSmoothness = Mathf.Max(0.1f, followSmoothness);
            autoOrbitSpeed = Mathf.Max(0.5f, autoOrbitSpeed);
            autoOrbitDeadZone = Mathf.Max(0.01f, autoOrbitDeadZone);
        }

        #endregion
    }
}
