using System;
using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime.Damageables;
using Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UI;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Orientation mode for direction indicators.
    /// </summary>
    public enum IndicatorOrientationMode
    {
        Object3D,
        FlatOnGround,
        Billboard
    }

    /// <summary>
    /// How attack target visual feedback behaves after issuing an attack command.
    /// </summary>
    public enum AttackTargetFeedbackMode
    {
        Persistent,
        FlashAndFade,
        FlashOnly
    }

    /// <summary>
    /// Data passed to selection change events.
    /// </summary>
    [Serializable]
    public class SelectionChangedEventData
    {
        public List<int> selectedIndices;
        public List<int> newlySelected;
        public List<int> newlyDeselected;
        public int totalSelected;
    }

    /// <summary>
    /// Stores patrol path data for an agent.
    /// </summary>
    [Serializable]
    public class AgentPatrolPath
    {
        public List<Vector3> waypoints = new List<Vector3>();
        public int currentWaypointIndex;
        public bool isLoop;
        public bool isReversing; // For non-loop paths, ping-pong back
        public bool isPaused; // Paused during combat
        public Vector3 combatInterruptPosition; // Position when combat started
        
        // Formation data - stores this agent's position within the patrol group
        public int formationIndex; // This agent's index within the formation (0, 1, 2, etc.)
        public int formationTotalCount; // Total agents in the patrol group
        public FormationType formationType; // Formation type to use
        public float formationSpacing; // Spacing between agents
        
        public Vector3 CurrentWaypoint => waypoints.Count > 0 ? waypoints[currentWaypointIndex] : Vector3.zero;
        
        public Vector3 GetNextWaypoint()
        {
            if (waypoints.Count == 0) return Vector3.zero;
            
            if (isLoop)
            {
                currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
            }
            else
            {
                // Ping-pong for non-loop paths
                if (isReversing)
                {
                    currentWaypointIndex--;
                    if (currentWaypointIndex < 0)
                    {
                        currentWaypointIndex = 1;
                        isReversing = false;
                    }
                }
                else
                {
                    currentWaypointIndex++;
                    if (currentWaypointIndex >= waypoints.Count)
                    {
                        currentWaypointIndex = waypoints.Count - 2;
                        isReversing = true;
                    }
                }
                // Clamp for safety
                currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 0, waypoints.Count - 1);
            }
            
            return waypoints[currentWaypointIndex];
        }
        
        public void Reset()
        {
            waypoints.Clear();
            currentWaypointIndex = 0;
            isLoop = false;
            isReversing = false;
            isPaused = false;
            combatInterruptPosition = Vector3.zero;
            formationIndex = 0;
            formationTotalCount = 1;
            formationType = FormationType.Rectangle;
            formationSpacing = 1.5f;
        }
    }

    /// <summary>
    /// RTS-style selection and command controller for EnemyMasses agents.
    /// Supports click selection, drag-rectangle selection, and click-to-move commands.
    /// Compatible with both legacy Input Manager and new Input System.
    /// </summary>
    [AddComponentMenu("Enemy Masses/RTS Controller")]
    public class EnemyMassesRTSController : MonoBehaviour
    {
        #region References

        [Header("References")]
        [Tooltip("The crowd controller to command. If null, will search for one in the scene.")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Camera used for raycasting. If null, uses Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Reference to the minimap controller. Clicks on the minimap will not deselect agents. If null, will try to find one in the scene.")]
#pragma warning disable CS0618 // Legacy minimap support — RTSMinimapUI handles click blocking via EventSystem instead
        public RTSMinimapController minimapController;
#pragma warning restore CS0618

        #endregion
        
        private class IndicatorInstanceTag : MonoBehaviour
        {
            public bool usesDefaultPrefab;
        }

        #region Selection Settings

        [Header("Selection Settings")]
        [Tooltip("Enable click selection of individual agents")]
        public bool enableClickSelection = true;

        [Tooltip("Enable double-click to select all agents of the same prefab/faction")]
        public bool enableDoubleClickGroupSelection = true;
        
        [Tooltip("When enabled, double-click group selection only selects agents visible in the camera frustum. When disabled, selects all agents of that faction regardless of visibility.")]
        public bool doubleClickOnlyInFrustum = false;

        [Tooltip("Enable drag-rectangle selection")]
        public bool enableDragSelection = true;

        [Tooltip("Time window for double-click detection (seconds)")]
        [Range(0.1f, 0.5f)]
        public float doubleClickTime = 0.3f;

        [Tooltip("Minimum drag distance to start rectangle selection (pixels)")]
        [Range(1f, 20f)]
        public float minDragDistance = 5f;

        [Tooltip("Layer mask for agent selection raycasts")]
        public LayerMask agentSelectionMask = -1;

        #endregion

        #region Command Settings

        [Header("Command Settings")]
        [Tooltip("Enable click-to-move commands for selected agents")]
        public bool enableMoveCommands = true;

        [Tooltip("Layer mask for ground/NavMesh raycasts")]
        public LayerMask groundMask = -1;

        [Tooltip("Formation type when moving selected agents")]
        public FormationType moveFormation = FormationType.Rectangle;

        [Tooltip("Controls how multi-faction selections are arranged.\n" +
                 "Combined Formation: all selected agents share one formation.\n" +
                 "Battle Line: each EnemyCrowd type uses its own formation arranged side by side (Total War style).")]
        public FormationControlMode formationControlMode = FormationControlMode.CombinedFormation;

        [Tooltip("Spacing between agents when moving in formation")]
        [Range(0.5f, 5f)]
        public float formationSpacing = 1.5f;

        [Tooltip("Controls whether formation spacing is shared globally or tracked individually per EnemyCrowd type.\n" +
                 "Global: all agents share the same spacing value.\n" +
                 "Per Crowd: each EnemyCrowd can have its own spacing, adjustable at runtime by selecting only that crowd and dragging.")]
        public FormationSpacingMode formationSpacingMode = FormationSpacingMode.Global;

        [Tooltip("Gap between EnemyCrowd groups when using Battle Line mode. Drag-to-adjust modifies this value when multiple crowd types are selected.")]
        [Range(0.5f, 20f)]
        public float battleLineGroupGap = 2.5f;

        [Tooltip("If true, selected agents match the slowest agent's movement speed during formation moves.")]
        public bool matchFormationSpeedToSlowest = true;

        [Header("Formation Cycling")]
        [Tooltip("Allow cycling formation type while holding the command button.")]
        public bool enableFormationCycling = true;

        [Tooltip("Allow mouse scroll while holding the command button to cycle formation types. Leave off to avoid interfering with camera zoom.")]
        public bool useMouseScrollWheelForFormationCycle = false;

        [Tooltip("Cooldown between formation cycles to prevent over-scrolling.")]
        [Range(0f, 0.5f)] public float formationCycleCooldown = 0.12f;

        [Header("Drag-to-Adjust Formation Spacing")]
        [Tooltip("Enable drag-to-adjust formation spacing. Hold command button and drag horizontally to adjust spacing before releasing to execute the move.")]
        public bool enableDragToAdjustSpacing = true;

        [Tooltip("Time in seconds the command button must be held before formation adjustment starts. " +
                 "Quick click-and-release commands execute immediately without showing the formation preview.")]
        [Range(0f, 1f)]
        public float formationAdjustDelay = 0.15f;


        [Tooltip("Remember adjusted spacing per-faction during the play session. Each EnemyCrowd will retain its custom spacing after adjustment.")]
        public bool persistSpacingPerFaction = true;

        [Tooltip("Minimum drag distance (in pixels) before spacing adjustment starts. Prevents accidental adjustments on simple clicks.")]
        [Range(1f, 20f)]
        public float spacingDragThreshold = 5f;

        [Tooltip("How much the spacing changes per pixel of horizontal drag.")]
        [Range(0.001f, 0.05f)]
        public float spacingDragSensitivity = 0.01f;

        [Tooltip("Minimum formation spacing when dragging.")]
        [Range(0.5f, 2f)]
        public float minDragSpacing = 0.5f;

        [Tooltip("Maximum formation spacing when dragging.")]
        [Range(3f, 15f)]
        public float maxDragSpacing = 10f;

        [Header("Formation Preview (Ghost Markers)")]
        [Tooltip("Show ghost markers previewing where agents will go while adjusting spacing.")]
        public bool showFormationPreview = true;

        [Tooltip("Prefab for ghost position markers. If null, uses a simple sphere primitive.")]
        public GameObject formationPreviewPrefab;

        [Tooltip("Maximum number of ghost markers to show. Limits performance impact with large selections.")]
        [Range(10, 200)]
        public int maxFormationPreviewMarkers = 50;

        [Tooltip("Color of the ghost formation markers.")]
        public Color formationPreviewColor = new Color(0.3f, 0.8f, 1f, 0.5f);

        [Tooltip("Size of the default ghost markers (when no prefab is set).")]
        [Range(0.1f, 2f)]
        public float formationPreviewMarkerSize = 0.5f;

        [Header("Formation Rotation Adjustment")]
        [Tooltip("Enable rotation adjustment. While adjusting spacing (command button held), also hold selection button to lock spacing and adjust rotation instead.")]
        public bool enableRotationAdjustment = true;

        [Tooltip("How many degrees the rotation changes per pixel of horizontal mouse movement.")]
        [Range(0.1f, 2f)]
        public float rotationDragSensitivity = 0.5f;

        [Tooltip("Prefab for the rotation direction marker. If null, creates a sphere positioned in front of the formation to indicate facing direction.")]
        public GameObject rotationMarkerPrefab;

        [Tooltip("Color of the rotation direction marker.")]
        public Color rotationMarkerColor = new Color(1f, 0.6f, 0.2f, 0.8f);

        [Tooltip("Size of the default rotation marker (when no prefab is set).")]
        [Range(0.2f, 2f)]
        public float rotationMarkerSize = 0.6f;

        [Tooltip("Distance from formation center to place the rotation marker.")]
        [Range(1f, 10f)]
        public float rotationMarkerDistance = 3f;

        [Tooltip("Rotate agents to face the commanded direction when they arrive at their destination.")]
        public bool applyFacingOnArrival = true;

        [Tooltip("How fast agents rotate to face the commanded direction (degrees per second). Set to 0 for instant rotation.")]
        [Range(0f, 720f)]
        public float facingRotationSpeed = 180f;

        [Header("Optimal Formation Assignment")]
        [Tooltip("When rotating formations (command + selection button), agents are assigned to the closest available formation position instead of fixed index order. " +
                 "This minimizes travel time when rotating formations significantly. Only applies to rotated moves, not regular move commands.")]
        public bool useOptimalFormationAssignment = true;

        #endregion

        #region Attack Command Settings

        [Header("Attack Commands")]
        [Tooltip("Enable attack commands when command-clicking on enemy agents. Enemy = non-player-controlled and different faction.")]
        public bool enableAttackCommands = true;

        [Tooltip("Layer mask for raycasting to detect enemy agents for attack targeting. Should match your agent colliders.")]
        public LayerMask attackTargetMask = -1;

        [Tooltip("Maximum distance to search for an agent near the click position (for agents without colliders).")]
        [Range(0.5f, 5f)]
        public float attackTargetClickRadius = 2f;

        [Header("Disengage Settings")]
        [Tooltip("Grace period after move commands where Offensive agents won't re-acquire targets. Allows agents to disengage from combat and retreat. Set to 0 to disable.")]
        [Range(0f, 10f)]
        public float disengageGracePeriod = 2f;

        #endregion

        #region Follow Command Settings

        [Header("Follow Command")]
        [Tooltip("Enable the follow command system. Selected agents can be ordered to follow a target Transform.")]
        public bool enableFollowCommand = true;

        [Tooltip("Tags that are valid follow targets when clicked in follow mode.\n" +
                 "Objects with any of these tags can be followed.\n" +
                 "Player Targets from the CrowdController are always valid regardless of tag.")]
        public List<string> followableTags = new List<string> { "Player" };

        [Tooltip("Layer mask for raycasting to detect follow targets.")]
        public LayerMask followTargetMask = -1;

        [Tooltip("Click radius for detecting follow targets.\n" +
                 "Used as the SphereCast radius for Phase 1 (collider-based targets)\n" +
                 "and as the proximity radius for Phase 2 (collider-less crowd agents)\n" +
                 "and Phase 3 (Player Target fallback). Increase this if targets are hard to click.")]
        [Range(0.5f, 10f)]
        public float followTargetClickRadius = 5f;

        [Tooltip("Minimum distance agents try to maintain from the follow target.\n" +
                 "Agents stop when closer than this and resume when the target moves away.")]
        [Range(1f, 20f)]
        public float followStopDistance = 3f;

        [Tooltip("How often (in seconds) follow-mode agents update their destination toward the target.\n" +
                 "Lower values = smoother tracking but more NavMesh queries.")]
        [Range(0.05f, 1f)]
        public float followUpdateInterval = 0.15f;

        #endregion

        #region Wall Placement Zone Settings

        [Header("Wall Placement Zones")]
        [Tooltip("Enable wall placement zone integration. When enabled, command-clicking on a wall placement zone will mount selected agents to available spots.")]
        public bool enableWallPlacementZones = true;

        [Tooltip("List of wall placement zones in the scene. If empty, zones will be found automatically at start.")]
        public List<EnemyMassesWallPlacementZone> wallPlacementZones = new List<EnemyMassesWallPlacementZone>();

        [Tooltip("Automatically discover wall placement zones in the scene at startup.")]
        public bool autoDiscoverWallPlacementZones = true;

        #endregion

        #region Input Settings

        [Header("Input Settings")]

#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action for selection (click to select, drag to box select). Default is left mouse button. Can be remapped to any mouse button or gamepad button.")]
        public InputAction selectionButton = new InputAction("Selection Button", InputActionType.Button, "<Mouse>/leftButton");

        [Tooltip("Input action for commands (move, attack). Default is right mouse button. Can be remapped to any mouse button or gamepad button.")]
        public InputAction commandButton = new InputAction("Command Button", InputActionType.Button, "<Mouse>/rightButton");

    [Tooltip("Input action to cycle formation up while holding the command button (e.g., D-Pad Up).")]
    public InputAction formationCycleUp = new InputAction("Formation Cycle Up", InputActionType.Button, "<Gamepad>/dpad/up");

    [Tooltip("Input action to cycle formation down while holding the command button (e.g., D-Pad Down).")]
    public InputAction formationCycleDown = new InputAction("Formation Cycle Down", InputActionType.Button, "<Gamepad>/dpad/down");

    [Tooltip("Input action (Value/Axis) to cycle formation while holding the command button (e.g., scroll Y). Bind to <Mouse>/scroll/y if desired.")]
    public InputAction formationCycleAxis = new InputAction("Formation Cycle Axis", InputActionType.Value);

        [Tooltip("Input action for additive selection (hold to add to selection instead of replacing). Default is Left Shift. Can be remapped to any key or gamepad button.")]
        public InputAction additiveSelectionModifier = new InputAction("Additive Selection Modifier", InputActionType.Button, "<Keyboard>/leftShift");

        [Tooltip("Input action for the modifier key to save control groups (modifier+0-9 to save, 0-9 to recall). Default is Ctrl key. Can be remapped to any key or gamepad button.")]
        public InputAction controlGroupSaveModifier = new InputAction("Control Group Save Modifier", InputActionType.Button, "<Keyboard>/leftCtrl");
#else
        [Tooltip("Mouse button for selection (0 = left, 1 = right, 2 = middle).")]
        public int selectionMouseButton = 0;

        [Tooltip("Mouse button for commands (0 = left, 1 = right, 2 = middle).")]
        public int commandMouseButton = 1;

        [Tooltip("Key to cycle formation up while holding the command button (legacy input). Optional.")]
        public KeyCode formationCycleUpKey = KeyCode.None;

        [Tooltip("Key to cycle formation down while holding the command button (legacy input). Optional.")]
        public KeyCode formationCycleDownKey = KeyCode.None;

    [Tooltip("Use mouse scroll while holding the command button to cycle formation types (legacy input). Leave off to avoid interfering with camera zoom.")]
    public bool legacyUseMouseScrollForFormationCycle = false;

        [Tooltip("Key to hold for additive selection (adds to current selection)")]
        public KeyCode additiveSelectionKey = KeyCode.LeftShift;

        [Tooltip("Modifier key for saving control groups (modifier+0-9 to save, 0-9 to recall).")]
        public KeyCode controlGroupSaveModifierKey = KeyCode.LeftControl;
#endif

        #endregion

        #region Faction Control

        [Header("Faction Control")]
        [Tooltip("Factions the player can select and command. If empty, all factions are selectable.")]
        public FactionMask playerControlledFactions;

        [Tooltip("When true, only player-controlled factions can be selected. When false, all factions can be selected but only player-controlled can be commanded.")]
        public bool restrictSelectionToPlayerFactions = true;

        #endregion

        #region Fog of War

        [Header("Fog of War")]
        [Tooltip("Hide non-player faction agents when they are not actively visible (refog hides them too).")]
        public bool hideNonPlayerFactionsInFog = true;

        [Tooltip("Provider that implements IFogVisibility (e.g., FogOfWarLite).")]
        public MonoBehaviour fogOfWarProvider;

        [Tooltip("Alpha threshold for active visibility; lower hides sooner during refog.")]
        [Range(1, 255)] public byte fogVisibilityThreshold = 32;

        [Tooltip("Seconds between fog visibility culling passes.")]
        [Range(0.02f, 0.5f)] public float fogCullingInterval = 0.1f;

        #endregion

        #region Visual Settings (Foldout)

        [Header("Visual Settings")]
        [Tooltip("Show selection rectangle while dragging")]
        public bool showSelectionRectangle = true;

        [Tooltip("Color of the selection rectangle border")]
        public Color selectionBorderColor = new Color(0.2f, 0.8f, 0.2f, 1f);

        [Tooltip("Color of the selection rectangle fill")]
        public Color selectionFillColor = new Color(0.2f, 0.8f, 0.2f, 0.15f);

        [Tooltip("Border thickness of the selection rectangle")]
        [Range(1f, 5f)]
        public float selectionBorderThickness = 2f;

        [Tooltip("Show visual indicator when giving move commands")]
        public bool showMoveIndicator = true;

        [Tooltip("Color of the move command indicator")]
        public Color moveIndicatorColor = new Color(0.2f, 0.6f, 1f, 0.8f);

        [Tooltip("Duration to show move indicator (seconds)")]
        [Range(0.1f, 2f)]
        public float moveIndicatorDuration = 0.5f;

        [Tooltip("Show selection highlight on selected agents")]
        public bool showSelectionHighlight = true;

        [Tooltip("Color of selection highlight ring")]
        public Color selectionHighlightColor = new Color(0.2f, 1f, 0.2f, 0.8f);

        [Tooltip("Master toggle to disable ALL gizmo drawing for better Editor performance")]
        public bool enableGizmos = true;

        #endregion

        #region Destination Indicator Settings

        [Header("Destination Indicator")]
        [Tooltip("Show a persistent indicator at the movement destination while agents are traveling. Only one indicator is shown regardless of agent count.")]
        public bool showDestinationIndicator = true;

        [Tooltip("Prefab for the destination indicator. Can be UI (Image/RawImage on World Space Canvas), VFX Graph, or ParticleSystem.")]
        public GameObject destinationIndicatorPrefab;

        [Tooltip("Offset from the destination position (world-space units)")]
        public Vector3 destinationIndicatorOffset = new Vector3(0f, 0.1f, 0f);

        [Tooltip("Hide destination indicator when all selected agents reach within this distance of the destination")]
        [Range(0.5f, 10f)]
        public float destinationReachedThreshold = 2f;

        [Tooltip("Hide indicators as soon as the first agent arrives at the destination, rather than waiting for all agents.")]
        public bool hideIndicatorsOnFirstArrival = true;

        [Tooltip("Fade out indicators over time instead of hiding instantly. Set to 0 for instant hide.")]
        [Range(0f, 3f)]
        public float indicatorFadeOutDuration = 1.5f;

        [Tooltip("Parent transform for the destination indicator. For UI indicators, set to a World Space Canvas. If null, spawns in world space.")]
        public Transform destinationIndicatorParent;

        [Header("Direction Line Indicator (Optional)")]
        [Tooltip("Show a direction indicator connecting selected agents to their destination")]
        public bool showDirectionIndicator = false;

        [Tooltip("Prefab for the direction indicator. Typically a stretched quad, line, or arrow pointing toward destination. " +
                 "Can also contain a LineRenderer component for line-based direction display.")]
        public GameObject directionIndicatorPrefab;

        [Header("LineRenderer Mode")]
        [Tooltip("If enabled, uses a LineRenderer component from the prefab to draw a line to the destination instead of positioning the prefab.")]
        public bool useLineRenderer = false;

        [Tooltip("Draw the actual NavMesh path instead of a straight line to the destination. " +
                 "More accurate but slightly more expensive.")]
        public bool drawPathInsteadOfDirection = false;

        [Tooltip("Height offset for the line above the ground")]
        public float lineHeightOffset = 0.5f;

        [Tooltip("How often to update the path when drawing path mode (seconds). Lower = smoother but more expensive.")]
        [Range(0.05f, 1f)]
        public float pathUpdateInterval = 0.1f;

        [Tooltip("Speed of interpolation between path updates. Higher = snappier, lower = smoother. Set to 0 for instant updates.")]
        [Range(0f, 20f)]
        public float pathInterpolationSpeed = 8f;

        [Header("Indicator Positioning")]
        [Tooltip("Offset from the selection center for the direction indicator")]
        public Vector3 directionIndicatorOffset = new Vector3(0f, 1f, 0f);

        [Tooltip("Rotation offset applied to the direction indicator (in euler angles). Use this to correct the prefab's default orientation.")]
        public Vector3 directionIndicatorRotationOffset = Vector3.zero;

        [Tooltip("How the indicator should be oriented in world space.\n" +
                 "• Object3D: Z+ points toward destination (for 3D meshes, particles, VFX)\n" +
                 "• FlatOnGround: Lies flat on XZ plane, rotates Y toward destination (for UI RawImage, decals)\n" +
                 "• Billboard: Always faces camera while pointing toward destination")]
        public IndicatorOrientationMode directionIndicatorOrientation = IndicatorOrientationMode.FlatOnGround;

        [Header("Dynamic Distance (for non-LineRenderer mode)")]
        [Tooltip("Minimum distance from selection center to show direction indicator")]
        [Range(1f, 50f)]
        public float directionIndicatorMinDistance = 1f;

        [Tooltip("Distance added per selected agent (scales with formation size). " +
                 "Example: 0.25 means 100 agents = 25 units distance, 400 agents = 100 units distance")]
        [Range(0f, 3f)]
        public float directionIndicatorDistancePerAgent = 0.25f;

        [Tooltip("Maximum distance from selection center (caps the dynamic distance)")]
        [Range(10f, 500f)]
        public float directionIndicatorMaxDistance = 200f;

        [Tooltip("Parent transform for the direction indicator. If null, spawns in world space.")]
        public Transform directionIndicatorParent;

        [Header("Fog of War Visibility")]
        [Tooltip("Ensure destination indicators render above the fog of war plane. Sets render queue to Overlay (4000).")]
        public bool renderAboveFog = true;

        [Tooltip("Sorting order for indicators (higher = rendered on top). Only applies to SpriteRenderers and Canvas elements.")]
        public int indicatorSortingOrder = 100;

        #endregion

        #region Waypoint Patrol Settings

        [Header("Waypoint Patrol")]
        [Tooltip("Enable waypoint patrol mode. Hold the queue modifier while clicking to queue waypoints instead of immediately moving.")]
        public bool enableWaypointPatrol = true;

#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action for the waypoint queue modifier. Hold this while clicking to queue waypoints. Default is Left Shift.")]
        public InputAction waypointQueueModifier = new InputAction("Waypoint Queue Modifier", InputActionType.Button, "<Keyboard>/leftShift");
#else
        [Tooltip("Key to hold for queuing waypoints instead of immediately moving.")]
        public KeyCode waypointQueueModifierKey = KeyCode.LeftShift;
#endif

        [Tooltip("Distance threshold to detect when the last waypoint is close to the first, creating a patrol loop.")]
        [Range(1f, 20f)]
        public float patrolLoopCloseDistance = 5f;

        [Tooltip("Show visual markers for queued waypoints.")]
        public bool showWaypointMarkers = true;

        [Tooltip("Prefab for waypoint markers. Independent from destination indicator to avoid lifecycle conflicts (fade out, hide on arrival, etc.).")]
        public GameObject waypointMarkerPrefab;

        [Tooltip("Offset from the waypoint position (world-space units)")]
        public Vector3 waypointMarkerOffset = new Vector3(0f, 0.1f, 0f);

        [Tooltip("Show lines connecting waypoints in the patrol path.")]
        public bool showWaypointLines = true;

        [Tooltip("Color for waypoint path lines.")]
        public Color waypointLineColor = new Color(0.2f, 0.8f, 1f, 0.8f);

        [Tooltip("Width of waypoint path lines.")]
        [Range(0.05f, 1f)]
        public float waypointLineWidth = 0.15f;

        [Tooltip("Color for patrol loop lines (when path forms a closed loop).")]
        public Color patrolLoopLineColor = new Color(0.2f, 1f, 0.5f, 0.8f);

        [Tooltip("Maximum number of waypoints per patrol path.")]
        [Range(2, 50)]
        public int maxWaypointsPerPath = 20;

        #endregion

        #region Selection Feedback Settings

        [Header("Selection Feedback - Visual")]
        [Tooltip("Enable shader-based highlight effect on selected agents (efficient for thousands of agents)")]
        public bool enableShaderHighlight = true;

        [Tooltip("Shader property name for highlight color (e.g., '_SelectionColor', '_EmissionColor')")]
        public string highlightColorProperty = "_SelectionColor";

        [Tooltip("Shader property name for highlight intensity/enabled (e.g., '_SelectionIntensity', '_Selected')")]
        public string highlightIntensityProperty = "_SelectionIntensity";

        [Tooltip("Color to apply to selected agents via shader")]
        public Color shaderHighlightColor = new Color(0.3f, 1f, 0.3f, 1f);

        [Tooltip("Intensity of the shader highlight effect")]
        [Range(0f, 2f)]
        public float shaderHighlightIntensity = 1f;

        [Header("Selection Feedback - Prefab (for VFX/UI)")]
        [Tooltip("Optional prefab to spawn for each selected agent. Can be world-space (VFX, particle) or UI (Image/RawImage).")]
        public GameObject selectionIndicatorPrefab;

        [Tooltip("Offset from agent position for the indicator prefab (world-space units)")]
        public Vector3 indicatorOffset = new Vector3(0f, 0.1f, 0f);

        [Tooltip("Maximum number of indicator prefabs to spawn. Use low values (20-100) for VFX/particles, higher (500-2000) for lightweight UI elements.")]
        [Range(1, 2000)]
        public int maxIndicatorPrefabs = 20;

        [Tooltip("Parent transform for spawned indicators. For UI indicators, set this to a Canvas or UI container. If null, uses this transform (world-space).")]
        public Transform indicatorParent;

        [Tooltip("Automatically update indicator positions each frame. Enable for UI indicators or when agents move. Disable for static VFX that handle their own positioning.")]
        public bool autoUpdateIndicatorPositions = true;

        [Tooltip("Automatically scale the selection indicator to match the agent's bounding size.\n" +
                 "Non-humanoid agents (vehicles) use vehicleBoundingRadius from the EnemyCrowd.\n" +
                 "Humanoid agents use the navigation agent radius.\n" +
                 "Disable this if you want all indicators to use the prefab's default scale.")]
        public bool autoScaleIndicatorToAgent = true;

        [Tooltip("Place selection indicators at ground level beneath Flight3D agents instead of at their flight altitude.\n" +
                 "Only applies to agents using the Flight3D navigation type (native 3D flyers).\n" +
                 "Agents using the Fly Effect skill action always show the indicator at their visual altitude.")]
        public bool indicatorAtGroundForFlying = true;

        [Tooltip("Screen offset for UI indicators (pixels). Only used when indicator parent is a Canvas.")]
        public Vector2 uiIndicatorScreenOffset = Vector2.zero;

        [Header("Selection Feedback - Events")]
        [Tooltip("Called when selection changes - use for UI updates, sound effects, etc.")]
        public UnityEvent<SelectionChangedEventData> onSelectionChanged;

        [Tooltip("Called when a single agent is selected")]
        public UnityEvent<int> onAgentSelected;

        [Tooltip("Called when a single agent is deselected")]
        public UnityEvent<int> onAgentDeselected;

        [Tooltip("Called when all agents are deselected")]
        public UnityEvent onSelectionCleared;

        [Tooltip("Called when a move command is issued")]
        public UnityEvent<Vector3, List<int>> onMoveCommandIssued;

        [Tooltip("Called when an attack command is issued on an enemy agent")]
        public UnityEvent<int, List<int>> onAttackCommandIssued;

        #endregion

        #region Attack Target Feedback Settings

        [Header("Attack Target Feedback - Visual")]
        [Tooltip("Enable visual feedback on the targeted enemy when issuing attack commands.")]
        public bool enableAttackTargetFeedback = true;

        [Tooltip("Enable shader-based highlight effect on the attack target (similar to selection highlight).")]
        public bool enableAttackTargetShaderHighlight = true;

        [Tooltip("Shader property name for attack target highlight color.")]
        public string attackTargetColorProperty = "_SelectionColor";

        [Tooltip("Shader property name for attack target highlight intensity.")]
        public string attackTargetIntensityProperty = "_SelectionIntensity";

        [Tooltip("Color to apply to the attack target via shader (red/orange recommended for hostiles).")]
        public Color attackTargetHighlightColor = new Color(1f, 0.3f, 0.2f, 1f);

        [Tooltip("Intensity of the attack target shader highlight effect.")]
        [Range(0f, 2f)]
        public float attackTargetHighlightIntensity = 1.2f;

        [Header("Attack Target Feedback - Prefab")]
        [Tooltip("Optional prefab to spawn on the attack target. Can be world-space VFX (targeting reticle, crosshair) or UI element.")]
        public GameObject attackTargetIndicatorPrefab;

        [Tooltip("Offset from target position for the attack target indicator (world-space units).")]
        public Vector3 attackTargetIndicatorOffset = new Vector3(0f, 0.1f, 0f);

        [Tooltip("Parent transform for attack target indicator. For UI indicators, set to a Canvas. If null, spawns in world space.")]
        public Transform attackTargetIndicatorParent;

        [Header("Attack Target Feedback - Behavior")]
        [Tooltip("How long the attack target highlight persists.\n" +
                 "• Persistent: Stays until target dies or new target is selected\n" +
                 "• FlashAndFade: Flashes on command, then fades out over time\n" +
                 "• FlashOnly: Quick flash on command, then disappears")]
        public AttackTargetFeedbackMode attackTargetFeedbackMode = AttackTargetFeedbackMode.Persistent;

        [Tooltip("Duration of the flash/fade effect (seconds). Only used for FlashAndFade and FlashOnly modes.")]
        [Range(0.1f, 5f)]
        public float attackTargetFeedbackDuration = 1.5f;

        [Tooltip("Number of flash pulses before fading. Only used for FlashAndFade mode.")]
        [Range(1, 10)]
        public int attackTargetFlashCount = 3;

        [Tooltip("Speed of the flash pulse effect.")]
        [Range(1f, 20f)]
        public float attackTargetFlashSpeed = 8f;

        [Tooltip("Clear attack target feedback when target dies.")]
        public bool clearFeedbackOnTargetDeath = true;

        [Tooltip("Automatically update attack target indicator position each frame (for moving targets).")]
        public bool autoUpdateAttackTargetPosition = true;

        #endregion

        #region Public Settings Continued

        [Header("RTS Audio Feedback")]
        [Tooltip("Enable automatic playback of faction-specific selection and command sounds.")]
        public bool enableRTSAudio = true;
        
        [Tooltip("Volume for RTS selection and command sounds.")]
        [Range(0f, 1f)]
        public float rtsAudioVolume = 1f;
        
        [Tooltip("Minimum time between selection sounds to prevent spam.")]
        [Range(0f, 1f)]
        public float selectionSoundCooldown = 0.2f;
        
        [Tooltip("Minimum time between command sounds to prevent spam.")]
        [Range(0f, 1f)]
        public float commandSoundCooldown = 0.1f;

        [Header("Debug Settings")]
        [Tooltip("Enable verbose debug logging")]
        public bool enableDebugLogging = false;

        #endregion
        
        #region RTS Audio State
        
        private AudioSource _rtsAudioSource;
        private float _lastSelectionSoundTime;
        private float _lastCommandSoundTime;
        
        #endregion

        #region C# Events (for programmers)

        /// <summary>
        /// Fired when selection changes. Provides detailed information about what changed.
        /// </summary>
        public event Action<SelectionChangedEventData> SelectionChanged;

        /// <summary>
        /// Fired when a single agent is added to selection.
        /// </summary>
        public event Action<int, CrowdAgent> AgentSelected;

        /// <summary>
        /// Fired when a single agent is removed from selection.
        /// </summary>
        public event Action<int, CrowdAgent> AgentDeselected;

        /// <summary>
        /// Fired when selection is completely cleared.
        /// </summary>
        public event Action SelectionCleared;

        /// <summary>
        /// Fired when a move command is issued to selected agents.
        /// </summary>
        public event Action<Vector3, IReadOnlyList<int>> MoveCommandIssued;

        /// <summary>
        /// Fired when an attack command is issued. Parameters: target agent index, list of attacking agent indices.
        /// </summary>
        public event Action<int, IReadOnlyList<int>> AttackCommandIssued;

        #endregion
        
        #region Network Events
        
        /// <summary>
        /// Fired when a move command is issued. Subscribe to route commands through network.
        /// The CommandEventData includes all info needed to replicate the command on server/clients.
        /// </summary>
        public event Action<CommandEventData> NetworkMoveCommandIssued;
        
        /// <summary>
        /// Fired when an attack command is issued. Subscribe to route through network.
        /// </summary>
        public event Action<CommandEventData> NetworkAttackCommandIssued;
        
        /// <summary>
        /// Fired when a stop command is issued (agents ordered to halt).
        /// </summary>
        public event Action<CommandEventData> NetworkStopCommandIssued;

        /// <summary>
        /// Fired when an <see cref="IRTSExternalSelectable"/> is registered via <see cref="RegisterExternalSelectable"/>.
        /// Listeners (e.g. the portrait bar) can use this to rebuild their slot list.
        /// </summary>
        public event Action<IRTSExternalSelectable> ExternalSelectableRegistered;
        
        /// <summary>
        /// Command sequence number for ordering and deduplication.
        /// </summary>
        private int _commandSequence = 0;

        #endregion

        #region Runtime State

        private static readonly FormationType[] DefaultFormationCycleOrder = new[]
        {
            FormationType.Rectangle,
            FormationType.Square,
            FormationType.Circle,
            FormationType.Triangle,
            FormationType.HollowRectangle,
            FormationType.HollowSquare,
            FormationType.HollowCircle,
            FormationType.HollowTriangle,
            FormationType.Random
        };

        // Selection state
        private List<int> _selectedAgentIndices = new List<int>();
        private List<IRTSExternalSelectable> _selectedExternalSelectables = new List<IRTSExternalSelectable>();
        private readonly List<IRTSExternalSelectable> _externalSelectables = new List<IRTSExternalSelectable>();
        private int _lastExternalSelectionCount;
        private HashSet<int> _previousSelection = new HashSet<int>();
        private bool _isDragging;
        private Vector2 _dragStartPosition;
        private Vector2 _dragCurrentPosition;
        private float _lastClickTime;
        private int _lastClickedAgentIndex = -1;
        private float _lastExternalClickTime;
        private IRTSExternalSelectable _lastClickedExternalSelectable;

        private int TotalSelectedCount => _selectedAgentIndices.Count + _selectedExternalSelectables.Count;

        // Move indicator state
        private Vector3 _moveIndicatorPosition;
        private float _moveIndicatorStartTime;
        private bool _showingMoveIndicator;

        /// <summary>
        /// When true, the controller skips command processing (move/attack) this frame.
        /// Set by AgentSkillBarUI targeting mode to prevent conflicting commands.
        /// </summary>
        [System.NonSerialized] public bool suppressCommandInput;

        // Input state
        private bool _selectionButtonDown;
        private bool _selectionButtonUp;
        private bool _commandButtonDown;
        private bool _commandButtonUp;
        private bool _commandButtonHeld;
        private bool _formationCycleUpPressed;
        private bool _formationCycleDownPressed;
        private float _formationCycleAxis;
        private float _scrollDeltaY;
        private bool _additiveKeyHeld;
        private Vector2 _mousePosition;

        // Drag-to-adjust spacing state
        private bool _isAdjustingSpacing;
        private Vector2 _spacingDragStartPosition;
        private float _temporarySpacing;
        private float _baseFormationSpacing; // Stores the original design-time value
        private float _commandPressTime; // Time.time when command button was pressed (for delay)

        // Per-faction spacing persistence (runtime only)
        private Dictionary<string, float> _factionSpacingOverrides = new Dictionary<string, float>();
        private float _externalSpacingOverride = -1f; // Persisted spacing for external-only selections (e.g., GC2 players)

        // Battle Line drag state
        private bool _isDraggingBattleLineGap; // true when drag adjusts group gap instead of formation spacing
        private float _temporaryBattleLineGap;
        private float _baseBattleLineGroupGap;

        // Per-crowd spacing drag state (when formationSpacingMode == PerCrowd and single crowd selected)
        private bool _isDraggingPerCrowdSpacing;
        private int _draggingCrowdIndex = -1; // which crowd's spacing we're adjusting
        private float _temporaryPerCrowdSpacing;
        private float _basePerCrowdSpacing;

        // Formation rotation adjustment state
        private bool _isAdjustingRotation;
        private float _temporaryRotation; // Euler Y angle in degrees
        private float _lockedSpacing; // Spacing value when rotation mode was entered
        private Vector2 _rotationDragStartPosition;
        private GameObject _rotationMarkerInstance;
        private float _nextFormationCycleTime;
        private FormationType[] _formationCycleOrder;

        // Formation preview (ghost markers) state
        private List<GameObject> _activePreviewMarkers = new List<GameObject>();
        private Queue<GameObject> _previewMarkerPool = new Queue<GameObject>();
        private Transform _previewMarkerParent;
        private Vector3 _previewCenter; // Cached raycast hit point for preview
        private bool _hasValidPreviewPosition;

        // Cached components
        private Camera _camera;
        private RTSCameraController _rtsCameraController;
        private IFogVisibility _fog;
        private float _lastFogCullTime;

        // Selection rectangle rendering
        private Texture2D _whiteTexture;
        private GUIStyle _borderStyle;

        // Selection feedback state
        private List<GameObject> _activeIndicators = new List<GameObject>();
        private Queue<GameObject> _indicatorPool = new Queue<GameObject>();
        private MaterialPropertyBlock _propertyBlock;
        private int _highlightColorId;
        private int _highlightIntensityId;
        private SelectionChangedEventData _eventData = new SelectionChangedEventData();

        // Attack target feedback state
        private int _currentAttackTargetIndex = -1;
        private GameObject _attackTargetIndicatorInstance;
        private float _attackTargetFeedbackStartTime;
        private bool _attackTargetFeedbackActive;
        private int _attackTargetColorId;
        private int _attackTargetIntensityId;
        private MaterialPropertyBlock _attackTargetPropertyBlock;

        // Control groups (keys 1-9 map to indices 0-8, key 0 maps to index 9)
        private List<int>[] _controlGroups = new List<int>[10];
        private List<IRTSExternalSelectable>[] _controlGroupsExternal = new List<IRTSExternalSelectable>[10];
        private int _lastRecalledGroupIndex = -1;
        private float _lastControlGroupRecallTime;
        
        // Destination indicator state
        private GameObject _destinationIndicatorInstance;
        private GameObject _directionIndicatorInstance;
        
        // Per-agent destination tracking (agentIndex -> destination)
        // Uses a dictionary for O(1) lookup - efficient for thousands of agents
        private Dictionary<int, Vector3> _agentDestinations = new Dictionary<int, Vector3>();
        private Dictionary<IRTSExternalSelectable, Vector3> _externalDestinations = new Dictionary<IRTSExternalSelectable, Vector3>();

        // Formation speed overrides (agentIndex -> original speed)
        private Dictionary<int, float> _formationSpeedOverrides = new Dictionary<int, float>();

        // Runtime per-EnemyCrowd formation type overrides (crowdIndex -> FormationType)
        // Used in Battle Line mode so cycling formations for a single-crowd selection persists per crowd without mutating the SO asset.
        private Dictionary<int, FormationType> _crowdFormationOverrides = new Dictionary<int, FormationType>();

        // Runtime per-EnemyCrowd spacing overrides (crowdIndex -> spacing)
        // Used when formationSpacingMode == PerCrowd so each crowd can have independent spacing.
        private Dictionary<int, float> _crowdSpacingOverrides = new Dictionary<int, float>();
        
        // Tracks when each agent's destination was set (for grace period to allow path calculation)
        private Dictionary<int, float> _agentDestinationSetTime = new Dictionary<int, float>();
        private Dictionary<IRTSExternalSelectable, float> _externalDestinationSetTime = new Dictionary<IRTSExternalSelectable, float>();
        private const float DESTINATION_GRACE_PERIOD = 0.5f; // Wait before checking if agent arrived
        
        // Cached state for current selection's destination (computed from _agentDestinations)
        private Vector3 _activeDestination;
        private bool _hasActiveDestinationForSelection;
        private HashSet<int> _selectedAgentsWithDestination = new HashSet<int>();
        private List<IRTSExternalSelectable> _selectedExternalWithDestination = new List<IRTSExternalSelectable>();
        
        // Tracks if indicators were hidden due to first arrival (prevents re-activation until new command)
        private bool _indicatorsHiddenByFirstArrival;
        
        // Agents currently rotating to face their formation direction (agentIndex -> target rotation)
        private Dictionary<int, Quaternion> _agentsRotatingToFace = new Dictionary<int, Quaternion>();
        
        // Fade-out state for indicators
        private bool _isFadingOutIndicators;
        private float _fadeOutStartTime;
        private float _currentFadeAlpha = 1f;
        private List<Renderer> _destinationIndicatorRenderers = new List<Renderer>();
        private List<Renderer> _directionIndicatorRenderers = new List<Renderer>();
        private Dictionary<Renderer, Color> _originalColors = new Dictionary<Renderer, Color>();
        private List<Graphic> _destinationIndicatorGraphics = new List<Graphic>();
        private List<Graphic> _directionIndicatorGraphics = new List<Graphic>();
        private Dictionary<Graphic, Color> _originalGraphicColors = new Dictionary<Graphic, Color>();
        private Color _originalLineRendererStartColor;
        private Color _originalLineRendererEndColor;
        
        // LineRenderer mode state
        private LineRenderer _directionLineRenderer;
        private NavMeshPath _lineRendererPath;
        private float _lastPathUpdateTime;
        private Vector3[] _pathPoints;
        
        // Path interpolation state
        private Vector3[] _targetPathPoints;
        private Vector3[] _currentPathPoints;
        private int _targetPathLength;
        private bool _hasPathToInterpolateTo;
        
        // UI indicator state
        private bool _isUIIndicator;
        private Canvas _indicatorCanvas;
        private RectTransform _indicatorCanvasRect;
        
        // Post-combat reformation state
        private bool _isAwaitingPostCombatReformation;
        private float _postCombatCheckStartTime;
        private float _postCombatGracePeriod = 1.5f; // Wait before checking if combat is over
        private Vector3 _lastAttackTargetPosition; // Used for reformation center point
        private bool _hasPostCombatReformationSnapshot;
        private FormationType _postCombatReformationFormation;
        private float _postCombatReformationSpacing;

        // Waypoint Patrol State
        private bool _waypointQueueModifierHeld;
        private bool _isQueuingWaypoints; // True when actively building a waypoint path
        private List<Vector3> _queuedWaypoints = new List<Vector3>(); // Waypoints being built for current selection
        private bool _isPatrolLoop; // True if current path forms a closed loop
        
        // Per-agent patrol tracking
        // Key: agentIndex, Value: patrol path data
        private Dictionary<int, AgentPatrolPath> _agentPatrolPaths = new Dictionary<int, AgentPatrolPath>();
        private Dictionary<IRTSExternalSelectable, AgentPatrolPath> _externalPatrolPaths = new Dictionary<IRTSExternalSelectable, AgentPatrolPath>();
        
        // Active patrol path visualization tracking
        private AgentPatrolPath _activeVisualizationPatrolPath; // The patrol path currently being visualized
        private bool _isShowingActivePatrol; // True if showing an active patrol path (not queuing)
        
        // Waypoint visualization
        private List<GameObject> _waypointMarkerInstances = new List<GameObject>();
        private LineRenderer _waypointPathLineRenderer;
        private Queue<GameObject> _waypointMarkerPool = new Queue<GameObject>();

        #endregion

        #region Public Properties

        /// <summary>
        /// Currently selected agent indices (read-only).
        /// </summary>
        public IReadOnlyList<int> SelectedAgents => _selectedAgentIndices;

        /// <summary>
        /// Currently selected external units (not backed by EnemyCrowd agents).
        /// </summary>
        public IReadOnlyList<IRTSExternalSelectable> SelectedExternalAgents => _selectedExternalSelectables;

        /// <summary>
        /// All registered external selectables.
        /// </summary>
        public IReadOnlyList<IRTSExternalSelectable> ExternalSelectables => _externalSelectables;

        /// <summary>
        /// Number of currently selected controllables (crowd + external).
        /// </summary>
        public int SelectedCount => TotalSelectedCount;

        /// <summary>
        /// Whether a drag selection is in progress.
        /// </summary>
        public bool IsDragging => _isDragging;

        /// <summary>
        /// Returns true if no faction filter is set (all factions selectable).
        /// </summary>
        public bool AllFactionsSelectable => playerControlledFactions == null || !playerControlledFactions.HasAnyFaction;

        /// <summary>
        /// Registers an external NavMesh-driven unit so it can be selected and commanded.
        /// </summary>
        public void RegisterExternalSelectable(IRTSExternalSelectable selectable)
        {
            if (selectable == null) return;
            if (_externalSelectables.Contains(selectable)) return;
            _externalSelectables.Add(selectable);
            ExternalSelectableRegistered?.Invoke(selectable);
        }

        /// <summary>
        /// Unregisters an external selectable and removes it from the active selection if present.
        /// </summary>
        public void UnregisterExternalSelectable(IRTSExternalSelectable selectable)
        {
            if (selectable == null) return;
            bool removed = _externalSelectables.Remove(selectable);
            removed |= _selectedExternalSelectables.Remove(selectable);
            if (removed)
            {
                OnSelectionChanged();
            }
        }

        /// <summary>
        /// Programmatically selects an external selectable (e.g. <see cref="IExternalSkillCaster"/>).
        /// If <paramref name="additive"/> is false, clears all current selection first.
        /// </summary>
        public void SelectExternalSelectable(IRTSExternalSelectable selectable, bool additive = false)
        {
            if (selectable == null) return;
            if (!additive)
            {
                _selectedAgentIndices.Clear();
                _selectedExternalSelectables.Clear();
            }
            if (!_selectedExternalSelectables.Contains(selectable))
                _selectedExternalSelectables.Add(selectable);
            OnSelectionChanged();
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_formationCycleOrder == null || _formationCycleOrder.Length == 0)
                _formationCycleOrder = DefaultFormationCycleOrder;

            // Create white texture for GUI drawing
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();

            // Initialize selection feedback
            _propertyBlock = new MaterialPropertyBlock();
            _highlightColorId = Shader.PropertyToID(highlightColorProperty);
            _highlightIntensityId = Shader.PropertyToID(highlightIntensityProperty);
            
            // Initialize attack target feedback
            _attackTargetPropertyBlock = new MaterialPropertyBlock();
            _attackTargetColorId = Shader.PropertyToID(attackTargetColorProperty);
            _attackTargetIntensityId = Shader.PropertyToID(attackTargetIntensityProperty);
            
            // Initialize event data lists
            _eventData.selectedIndices = new List<int>();
            _eventData.newlySelected = new List<int>();
            _eventData.newlyDeselected = new List<int>();
            
            // Initialize RTS audio source
            InitializeRTSAudio();
        }
        
        private void InitializeRTSAudio()
        {
            if (!enableRTSAudio) return;
            
            _rtsAudioSource = gameObject.AddComponent<AudioSource>();
            _rtsAudioSource.playOnAwake = false;
            _rtsAudioSource.spatialBlend = 0f; // 2D sound
            _rtsAudioSource.volume = rtsAudioVolume;
        }
        
        /// <summary>
        /// Plays an appropriate selection sound based on the number of selected agents.
        /// Uses faction-specific sounds with single vs. group threshold.
        /// </summary>
        private void PlaySelectionSound(int selectedCount)
        {
            if (!enableRTSAudio || _rtsAudioSource == null || selectedCount == 0)
                return;
            
            // Check cooldown to prevent sound spam
            if (Time.time - _lastSelectionSoundTime < selectionSoundCooldown)
                return;
            
            // Get faction from first selected agent
            EnemyCrowd faction = GetFactionFromSelection();
            AudioClip clip = faction != null ? faction.GetRandomSelectionSound(selectedCount) : null;
            float factionVolume = faction != null ? faction.factionAudioVolume : 1f;
            float pitchVariation = faction != null ? faction.factionAudioPitchVariation : 0f;

            // Fall back to external selectable's per-unit selection audio
            if (clip == null && _selectedExternalSelectables.Count > 0)
            {
                for (int i = 0; i < _selectedExternalSelectables.Count; i++)
                {
                    AudioClip externalClip = _selectedExternalSelectables[i].SelectionAudioClip;
                    if (externalClip != null)
                    {
                        clip = externalClip;
                        break;
                    }
                }
            }

            if (clip != null)
            {
                // Apply faction audio settings (RTS sounds use 2D spatial blend)
                float finalVolume = rtsAudioVolume * factionVolume;
                float pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
                
                _rtsAudioSource.volume = finalVolume;
                _rtsAudioSource.pitch = pitch;
                _rtsAudioSource.spatialBlend = 0f; // RTS selection sounds are always 2D
                _rtsAudioSource.PlayOneShot(clip);
                _lastSelectionSoundTime = Time.time;
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[RTS Audio] Playing selection sound for {selectedCount} agents: {clip.name} (vol={finalVolume:F2}, pitch={pitch:F2})");
                }
            }
        }
        
        /// <summary>
        /// Plays an appropriate command sound based on the number of commanded agents.
        /// Uses faction-specific sounds with single vs. group threshold.
        /// </summary>
        public void PlayCommandSound(int commandedCount)
        {
            if (!enableRTSAudio || _rtsAudioSource == null || commandedCount == 0)
                return;
            
            // Check cooldown to prevent sound spam
            if (Time.time - _lastCommandSoundTime < commandSoundCooldown)
                return;
            
            // Get faction from first selected agent
            EnemyCrowd faction = GetFactionFromSelection();
            if (faction == null)
                return;
            
            // Get appropriate sound clip based on commanded count
            AudioClip clip = faction.GetRandomCommandSound(commandedCount);
            if (clip != null)
            {
                // Apply faction audio settings (RTS sounds use 2D spatial blend)
                float finalVolume = rtsAudioVolume * faction.factionAudioVolume;
                float pitch = 1f + UnityEngine.Random.Range(-faction.factionAudioPitchVariation, faction.factionAudioPitchVariation);
                
                _rtsAudioSource.volume = finalVolume;
                _rtsAudioSource.pitch = pitch;
                _rtsAudioSource.spatialBlend = 0f; // RTS command sounds are always 2D
                _rtsAudioSource.PlayOneShot(clip);
                _lastCommandSoundTime = Time.time;
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[RTS Audio] Playing command sound for {commandedCount} agents: {clip.name} (vol={finalVolume:F2}, pitch={pitch:F2})");
                }
            }
        }
        
        /// <summary>
        /// Gets the EnemyCrowd faction from the first selected agent.
        /// </summary>
        private EnemyCrowd GetFactionFromSelection()
        {
            if (_selectedAgentIndices.Count == 0 || crowdController == null)
                return null;
            
            var agents = crowdController.GetAgents();
            int firstIndex = _selectedAgentIndices[0];
            
            if (firstIndex >= 0 && firstIndex < agents.Count)
            {
                CrowdAgent agent = agents[firstIndex];
                return agent?.enemyCrowd;
            }
            
            return null;
        }

        private void Start()
        {
            // Find crowd controller if not assigned
            if (crowdController == null)
            {
#if UNITY_2023_1_OR_NEWER
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
#else
                crowdController = FindObjectOfType<EnemyMassesCrowdController>();
#endif
                if (crowdController == null)
                {
                    Debug.LogError("[RTSController] No EnemyMassesCrowdController found in scene!", this);
                    enabled = false;
                    return;
                }
            }

            // Sync disengage grace period to crowd controller
            crowdController.DisengageGracePeriod = disengageGracePeriod;

            // Find minimap controller if not assigned
            if (minimapController == null)
            {
#if UNITY_2023_1_OR_NEWER
#pragma warning disable CS0618
                minimapController = FindFirstObjectByType<RTSMinimapController>();
#pragma warning restore CS0618
#else
#pragma warning disable CS0618
                minimapController = FindObjectOfType<RTSMinimapController>();
#pragma warning restore CS0618
#endif
            }

            // Get camera
            _camera = targetCamera != null ? targetCamera : Camera.main;
            if (_camera == null)
            {
                Debug.LogError("[RTSController] No camera found! Assign targetCamera or ensure Camera.main exists.", this);
                enabled = false;
                return;
            }

            _rtsCameraController = _camera.GetComponent<RTSCameraController>();

            // Set indicator parent
            if (indicatorParent == null)
                indicatorParent = transform;

            // Detect if we're using UI-based indicators
            CacheIndicatorCanvasInfo();

            CacheFogProvider();

#if ENABLE_INPUT_SYSTEM
            // Enable InputActions
            selectionButton?.Enable();
            commandButton?.Enable();
        formationCycleUp?.Enable();
        formationCycleDown?.Enable();
            formationCycleAxis?.Enable();
            additiveSelectionModifier?.Enable();
            controlGroupSaveModifier?.Enable();
            waypointQueueModifier?.Enable();
#endif

            // Discover wall placement zones if enabled
            if (enableWallPlacementZones && autoDiscoverWallPlacementZones)
            {
                DiscoverWallPlacementZones();
            }
        }

        private void OnValidate()
        {
            // Sync disengage grace period to crowd controller at runtime when changed in inspector
            if (Application.isPlaying && crowdController != null)
            {
                crowdController.DisengageGracePeriod = disengageGracePeriod;
            }
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            // Disable InputActions
            selectionButton?.Disable();
            commandButton?.Disable();
            formationCycleUp?.Disable();
            formationCycleDown?.Disable();
            formationCycleAxis?.Disable();
            additiveSelectionModifier?.Disable();
            controlGroupSaveModifier?.Disable();
            waypointQueueModifier?.Disable();
#endif

            // Clean up GUI texture
            if (_whiteTexture != null)
            {
                Destroy(_whiteTexture);
            }

            // Clean up pooled indicators
            foreach (var indicator in _indicatorPool)
            {
                if (indicator != null)
                    Destroy(indicator);
            }
            foreach (var indicator in _activeIndicators)
            {
                if (indicator != null)
                    Destroy(indicator);
            }
            _indicatorPool.Clear();
            _activeIndicators.Clear();

            // Clean up destination indicators
            CleanupDestinationIndicators();

            // Clean up formation preview markers
            CleanupFormationPreview();

            // Clean up rotation marker
            CleanupRotationMarker();

            // Clean up waypoint patrol resources
            CleanupWaypointResources();
        }

        private void Update()
        {
            if (crowdController == null || _camera == null)
                return;

            // Clean up dead agents from selection
            CleanupDeadAgentsFromSelection();

            ReadInput();
            ProcessControlGroups();
            ProcessInput();
            UpdateFogCulling();
            
            // Update agents rotating to face formation direction
            UpdateAgentFacingRotations();
            
            // Update follow target destinations for agents in follow mode
            UpdateFollowTargets();
            
            // Auto-update indicator positions (for UI or moving agents)
            if (autoUpdateIndicatorPositions && _activeIndicators.Count > 0)
            {
                UpdateIndicatorPositions();
            }

            // Update destination indicators
            UpdateDestinationIndicators();
            
            // Update waypoint patrol paths
            UpdatePatrolPaths();
            
            // Cleanup formation speed overrides when agents stop or leave manual command state
            CleanupFormationSpeedOverrides();

            // Update attack target feedback (flashing/fading)
            if (enableAttackTargetFeedback && _attackTargetFeedbackActive)
            {
                UpdateAttackTargetFeedback();
            }
            
            // Check for post-combat reformation (after attack command, when combat ends)
            if (_isAwaitingPostCombatReformation)
            {
                CheckPostCombatReformation();
            }
        }

        /// <summary>
        /// Removes dead or invalid agents from the selection list.
        /// Called every frame to keep selection valid.
        /// </summary>
        private void CleanupDeadAgentsFromSelection()
        {
            if (_selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            bool selectionChanged = false;
            List<int> removedIndices = new List<int>();

            for (int i = _selectedAgentIndices.Count - 1; i >= 0; i--)
            {
                int agentIndex = _selectedAgentIndices[i];
                
                // Remove if index is out of range
                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    _selectedAgentIndices.RemoveAt(i);
                    removedIndices.Add(agentIndex);
                    selectionChanged = true;
                    continue;
                }

                CrowdAgent agent = agents[agentIndex];
                
                // Remove if agent is null, dead, or has no navigation agent
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                {
                    // Remove shader highlight for this agent
                    if (enableShaderHighlight && agentIndex >= 0 && agentIndex < agents.Count)
                    {
                        ApplyShaderHighlight(agentIndex, false);
                    }
                    _selectedAgentIndices.RemoveAt(i);
                    removedIndices.Add(agentIndex);
                    selectionChanged = true;
                }
            }

            // Notify listeners if selection changed
            if (selectionChanged)
            {
                // Update event data for the change
                _eventData.newlySelected.Clear();
                _eventData.newlyDeselected.Clear();
                _eventData.newlyDeselected.AddRange(removedIndices);
                _eventData.selectedIndices.Clear();
                _eventData.selectedIndices.AddRange(_selectedAgentIndices);
                _eventData.totalSelected = _selectedAgentIndices.Count;

                // Fire deselection events
                foreach (int index in removedIndices)
                {
                    AgentDeselected?.Invoke(index, null);
                    onAgentDeselected?.Invoke(index);
                }

                if (_eventData.totalSelected == 0)
                {
                    SelectionCleared?.Invoke();
                    onSelectionCleared?.Invoke();
                }

                SelectionChanged?.Invoke(_eventData);
                onSelectionChanged?.Invoke(_eventData);

                // Update indicator prefabs
                UpdateSelectionIndicators();
            }
        }

        /// <summary>
        /// Enables post-combat reformation tracking for selected agents.
        /// Call this when agents may need to reform after their current combat ends,
        /// such as when toggling strict mode off during combat.
        /// </summary>
        public void EnablePostCombatReformation()
        {
            if (_isAwaitingPostCombatReformation)
                return; // Already tracking

            CapturePostCombatReformationSnapshot();
            
            _isAwaitingPostCombatReformation = true;
            _postCombatCheckStartTime = Time.time;
        }

        private void CapturePostCombatReformationSnapshot()
        {
            _postCombatReformationFormation = moveFormation;
            _postCombatReformationSpacing = formationSpacing;
            _hasPostCombatReformationSnapshot = true;
        }

        /// <summary>
        /// Checks if combat has ended for selected agents and triggers reformation.
        /// Called every frame while _isAwaitingPostCombatReformation is true.
        /// After combat ends, surviving selected agents reform into their formation.
        /// </summary>
        private void CheckPostCombatReformation()
        {
            // Wait for grace period before checking (let combat resolve)
            float timeSinceStart = Time.time - _postCombatCheckStartTime;
            if (timeSinceStart < _postCombatGracePeriod)
            {
                return;
            }

            // Check if any selected agents are still in combat
            var agents = crowdController.GetAgents();
            bool anyInCombat = false;
            int validAgentCount = 0;
            Vector3 averagePosition = Vector3.zero;

            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                validAgentCount++;
                averagePosition += agent.WorldPosition;

                // Check if agent is still attacking/chasing
                if (agent.attackTargetAgentIndex >= 0 || 
                    agent.attackTargetTransform != null ||
                    agent.hasAttackTargetPosition ||
                    agent.isAggroed ||
                    agent.isChasing ||
                    agent.navigationState == AgentNavigationState.ChasingTarget)
                {
                    anyInCombat = true;
                    // Keep checking - combat is still ongoing
                    break;
                }
            }

            // If combat is still happening, keep waiting
            if (anyInCombat)
            {
                // Reset the check timer so we keep checking periodically
                _postCombatCheckStartTime = Time.time - _postCombatGracePeriod + 0.5f; // Check again in 0.5s
                return;
            }

            // Combat is over! Reform the surviving agents
            _isAwaitingPostCombatReformation = false;

            if (validAgentCount < 2)
            {
                // Not enough agents to form a formation
                return;
            }

            // If all surviving selected agents are HoldPosition, skip group reformation.
            // Those agents already return via hold-position logic and forcing a group reform here
            // can fight with that process and distort the intended held shape.
            int holdPositionCount = 0;
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                if (agent.combatStance == CombatStance.HoldPosition)
                    holdPositionCount++;
            }

            if (holdPositionCount == validAgentCount)
            {
                return;
            }

            // Calculate center point for reformation (average position of survivors)
            averagePosition /= validAgentCount;

            // Find first valid agent as fallback source for formation settings
            CrowdAgent firstValidAgent = null;
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex >= 0 && agentIndex < agents.Count)
                {
                    CrowdAgent agent = agents[agentIndex];
                    if (agent != null && !agent.isDead && agent.navigationAgent != null)
                    {
                        firstValidAgent = agent;
                        break;
                    }
                }
            }

            // Prefer exact snapshot from the command that initiated tracking.
            // This prevents post-combat drift where reformation reverts to faction defaults
            // (e.g., Triangle/agentSpacing) instead of the command formation (e.g., Rectangle/custom spacing).
            if (_hasPostCombatReformationSnapshot)
            {
                moveFormation = _postCombatReformationFormation;
                if (_postCombatReformationSpacing > 0f)
                    formationSpacing = _postCombatReformationSpacing;
            }
            // Fallback to faction settings if no snapshot exists.
            else if (firstValidAgent?.enemyCrowd != null)
            {
                string factionName = firstValidAgent.enemyCrowd.factionName;
                if (!string.IsNullOrEmpty(factionName) && _factionSpacingOverrides.TryGetValue(factionName, out float factionSpacing))
                {
                    formationSpacing = factionSpacing;
                }
                else
                {
                    formationSpacing = firstValidAgent.enemyCrowd.agentSpacing;
                }
                moveFormation = firstValidAgent.enemyCrowd.formationType;
            }

            // Issue the move command with formation to the average position
            // This reuses existing formation logic (uses moveFormation and formationSpacing fields)
            MoveSelectedAgentsToWithRotation(averagePosition, 0f);
        }

        private void CacheFogProvider()
        {
            if (fogOfWarProvider != null)
            {
                _fog = fogOfWarProvider as IFogVisibility;
                if (_fog == null)
                {
                    _fog = fogOfWarProvider.GetComponent<IFogVisibility>();
                }
            }

            if (_fog == null)
            {
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IFogVisibility fv)
                    {
                        _fog = fv;
                        break;
                    }
                }
            }
        }

        private void UpdateFogCulling()
        {
            if (crowdController == null)
                return;

            if (!hideNonPlayerFactionsInFog)
            {
                RevealAllFogHiddenAgents();
                return;
            }

            if (_fog == null)
            {
                CacheFogProvider();
                if (_fog == null)
                {
                    RevealAllFogHiddenAgents();
                    return;
                }
            }

            if (Time.time - _lastFogCullTime < fogCullingInterval)
                return;

            _lastFogCullTime = Time.time;

            var agents = crowdController.GetAgents();
            if (agents == null || agents.Count == 0)
                return;

            bool hasFactionFilter = playerControlledFactions != null && playerControlledFactions.HasAnyFaction;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null || agent.prefabComponent == null)
                    continue;

                string factionName = agent.enemyCrowd != null ? agent.enemyCrowd.factionName : null;
                bool isPlayerFaction = !hasFactionFilter || (!string.IsNullOrEmpty(factionName) && playerControlledFactions.ContainsFaction(factionName));

                if (isPlayerFaction)
                {
                    RevealAgentFromFog(agent);
                    continue;
                }

                Vector3 pos = agent.WorldPosition;
                bool activelyVisible = _fog.IsActivelyVisible(pos, fogVisibilityThreshold);
                if (activelyVisible)
                {
                    RevealAgentFromFog(agent);
                }
                else
                {
                    HideAgentInFog(agent);
                }
            }
        }

        private void HideAgentInFog(CrowdAgent agent)
        {
            if (agent.isFogHidden)
                return;

            EnemyMassesGPUIHelper.SafeRemovePrefabInstance(agent.prefabComponent);
            agent.isFogHidden = true;
        }

        private void RevealAgentFromFog(CrowdAgent agent)
        {
            if (!agent.isFogHidden)
                return;

            var prefab = agent.prefabComponent;
            if (prefab != null)
            {
                if (!prefab.IsInstanced)
                {
                    EnemyMassesGPUIHelper.SafeAddPrefabInstance(prefab);
                }

                if (prefab.registeredManager != null)
                {
                    prefab.registeredManager.UpdateTransformData(prefab);
                }

                if (agent.crowdInstance != null)
                {
                    agent.crowdInstance.transform.hasChanged = true;
                }
            }

            agent.isFogHidden = false;
            
            // CRITICAL: Mark agent for animation refresh when revealed from fog
            // When the agent is re-added to GPUI, it may get a buffer slot with stale animation data
            // from a previously removed agent (e.g., dead animation). This flag ensures the animation
            // system forces the correct animation on the next update.
            agent.needsAnimationRefresh = true;
        }

        private void RevealAllFogHiddenAgents()
        {
            var agents = crowdController != null ? crowdController.GetAgents() : null;
            if (agents == null)
                return;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && agent.isFogHidden)
                {
                    RevealAgentFromFog(agent);
                }
            }
        }

        #endregion

        #region Input Reading

        private void ReadInput()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            _mousePosition = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            _scrollDeltaY = useMouseScrollWheelForFormationCycle && mouse != null ? mouse.scroll.ReadValue().y : 0f;

            _selectionButtonDown = selectionButton != null && selectionButton.WasPressedThisFrame();
            _selectionButtonUp = selectionButton != null && selectionButton.WasReleasedThisFrame();

            if (commandButton != null)
            {
                _commandButtonDown = commandButton.WasPressedThisFrame();
                _commandButtonUp = commandButton.WasReleasedThisFrame();
                _commandButtonHeld = commandButton.IsPressed();
            }
            else
            {
                _commandButtonDown = _commandButtonUp = _commandButtonHeld = false;
            }

            _formationCycleUpPressed = formationCycleUp != null && formationCycleUp.WasPressedThisFrame();
            _formationCycleDownPressed = formationCycleDown != null && formationCycleDown.WasPressedThisFrame();
            _formationCycleAxis = formationCycleAxis != null ? formationCycleAxis.ReadValue<float>() : 0f;

            // Read additive selection modifier from InputAction
            _additiveKeyHeld = additiveSelectionModifier != null && additiveSelectionModifier.IsPressed();
            
            // Read waypoint queue modifier from InputAction
            _waypointQueueModifierHeld = enableWaypointPatrol && waypointQueueModifier != null && waypointQueueModifier.IsPressed();
#else
            // Legacy Input Manager
            _mousePosition = Input.mousePosition;
            _scrollDeltaY = legacyUseMouseScrollForFormationCycle ? Input.mouseScrollDelta.y : 0f;
            _selectionButtonDown = Input.GetMouseButtonDown(selectionMouseButton);
            _selectionButtonUp = Input.GetMouseButtonUp(selectionMouseButton);
            _commandButtonDown = Input.GetMouseButtonDown(commandMouseButton);
            _commandButtonUp = Input.GetMouseButtonUp(commandMouseButton);
            _commandButtonHeld = Input.GetMouseButton(commandMouseButton);
            _formationCycleUpPressed = formationCycleUpKey != KeyCode.None && Input.GetKeyDown(formationCycleUpKey);
            _formationCycleDownPressed = formationCycleDownKey != KeyCode.None && Input.GetKeyDown(formationCycleDownKey);
            _formationCycleAxis = legacyUseMouseScrollForFormationCycle ? Input.mouseScrollDelta.y : 0f;
            _additiveKeyHeld = Input.GetKey(additiveSelectionKey);
            _waypointQueueModifierHeld = enableWaypointPatrol && Input.GetKey(waypointQueueModifierKey);
#endif
        }

        #endregion

        #region Control Groups

        /// <summary>
        /// Process control group hotkeys (modifier+0-9 to save, 0-9 to recall selection).
        /// </summary>
        private void ProcessControlGroups()
        {
            // Initialize control groups if needed
            for (int i = 0; i < _controlGroups.Length; i++)
            {
                if (_controlGroups[i] == null)
                    _controlGroups[i] = new List<int>();
                if (_controlGroupsExternal[i] == null)
                    _controlGroupsExternal[i] = new List<IRTSExternalSelectable>();
            }

            bool modifierHeld = false;
            int pressedNumber = -1;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // Check the configured modifier using InputAction
            modifierHeld = controlGroupSaveModifier.IsPressed();

            // Check number keys (wasPressedThisFrame for single press)
            if (keyboard.digit1Key.wasPressedThisFrame) pressedNumber = 1;
            else if (keyboard.digit2Key.wasPressedThisFrame) pressedNumber = 2;
            else if (keyboard.digit3Key.wasPressedThisFrame) pressedNumber = 3;
            else if (keyboard.digit4Key.wasPressedThisFrame) pressedNumber = 4;
            else if (keyboard.digit5Key.wasPressedThisFrame) pressedNumber = 5;
            else if (keyboard.digit6Key.wasPressedThisFrame) pressedNumber = 6;
            else if (keyboard.digit7Key.wasPressedThisFrame) pressedNumber = 7;
            else if (keyboard.digit8Key.wasPressedThisFrame) pressedNumber = 8;
            else if (keyboard.digit9Key.wasPressedThisFrame) pressedNumber = 9;
            else if (keyboard.digit0Key.wasPressedThisFrame) pressedNumber = 0;
#else
            // Legacy input: use KeyCode
            modifierHeld = Input.GetKey(controlGroupSaveModifierKey);

            // Check number keys (GetKeyDown for single press)
            if (Input.GetKeyDown(KeyCode.Alpha1)) pressedNumber = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) pressedNumber = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) pressedNumber = 3;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) pressedNumber = 4;
            else if (Input.GetKeyDown(KeyCode.Alpha5)) pressedNumber = 5;
            else if (Input.GetKeyDown(KeyCode.Alpha6)) pressedNumber = 6;
            else if (Input.GetKeyDown(KeyCode.Alpha7)) pressedNumber = 7;
            else if (Input.GetKeyDown(KeyCode.Alpha8)) pressedNumber = 8;
            else if (Input.GetKeyDown(KeyCode.Alpha9)) pressedNumber = 9;
            else if (Input.GetKeyDown(KeyCode.Alpha0)) pressedNumber = 0;
#endif

            if (pressedNumber < 0)
                return;

            // Map key to group index: keys 1-9 map to indices 0-8, key 0 maps to index 9
            int groupIndex = pressedNumber == 0 ? 9 : pressedNumber - 1;

            if (modifierHeld)
            {
                // Save current selection to control group
                SaveControlGroup(groupIndex);
            }
            else
            {
                // Check for double-press (same group key pressed twice quickly)
                float timeSinceLastRecall = Time.time - _lastControlGroupRecallTime;
                bool isDoubleTap = (groupIndex == _lastRecalledGroupIndex) && (timeSinceLastRecall < doubleClickTime);
                
                // Recall control group selection
                RecallControlGroup(groupIndex, isDoubleTap);
                
                // Update tracking for double-press detection
                _lastRecalledGroupIndex = groupIndex;
                _lastControlGroupRecallTime = Time.time;
            }
        }

        /// <summary>
        /// Saves the current selection to the specified control group.
        /// </summary>
        /// <param name="groupIndex">Control group index (0-9)</param>
        public void SaveControlGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= _controlGroups.Length)
                return;

            if (_controlGroups[groupIndex] == null)
                _controlGroups[groupIndex] = new List<int>();
            if (_controlGroupsExternal[groupIndex] == null)
                _controlGroupsExternal[groupIndex] = new List<IRTSExternalSelectable>();

            _controlGroups[groupIndex].Clear();
            _controlGroups[groupIndex].AddRange(_selectedAgentIndices);
            _controlGroupsExternal[groupIndex].Clear();
            _controlGroupsExternal[groupIndex].AddRange(_selectedExternalSelectables);

            if (enableDebugLogging)
            {
                int keyNumber = groupIndex == 9 ? 0 : groupIndex + 1;
                Debug.Log($"[RTS Control Group] Saved {_selectedAgentIndices.Count} agents and {_selectedExternalSelectables.Count} externals to group {keyNumber} (Ctrl+{keyNumber})");
            }
        }

        /// <summary>
        /// Recalls a previously saved control group selection.
        /// </summary>
        /// <param name="groupIndex">Control group index (0-9)</param>
        /// <param name="focusCamera">If true, centers the camera on the control group (triggered by double-tap)</param>
        public void RecallControlGroup(int groupIndex, bool focusCamera = false)
        {
            if (groupIndex < 0 || groupIndex >= _controlGroups.Length)
                return;

            if ((_controlGroups[groupIndex] == null || _controlGroups[groupIndex].Count == 0) &&
                (_controlGroupsExternal[groupIndex] == null || _controlGroupsExternal[groupIndex].Count == 0))
            {
                if (enableDebugLogging)
                {
                    int keyNumber = groupIndex == 9 ? 0 : groupIndex + 1;
                    Debug.Log($"[RTS Control Group] Group {keyNumber} is empty");
                }
                return;
            }

            // Filter out dead or invalid agents from the saved group
            var agents = crowdController.GetAgents();
            List<int> validIndices = new List<int>();
            List<IRTSExternalSelectable> validExternals = new List<IRTSExternalSelectable>();
            
            foreach (int index in _controlGroups[groupIndex])
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead && agent.navigationAgent != null)
                    {
                        // Check faction restrictions if enabled
                        if (!restrictSelectionToPlayerFactions || IsPlayerControlled(agent))
                        {
                            validIndices.Add(index);
                        }
                    }
                }
            }

            // Update the saved group to only contain valid agents
            _controlGroups[groupIndex].Clear();
            _controlGroups[groupIndex].AddRange(validIndices);

            if (_controlGroupsExternal[groupIndex] != null)
            {
                foreach (var selectable in _controlGroupsExternal[groupIndex])
                {
                    if (!IsExternalSelectableValid(selectable, requireSelectable: true))
                        continue;
                    if (restrictSelectionToPlayerFactions && !selectable.SkipFactionFilter && !AllFactionsSelectable)
                        continue;

                    validExternals.Add(selectable);
                }

                _controlGroupsExternal[groupIndex].Clear();
                _controlGroupsExternal[groupIndex].AddRange(validExternals);
            }

            if (validIndices.Count == 0 && validExternals.Count == 0)
            {
                if (enableDebugLogging)
                {
                    int keyNumber = groupIndex == 9 ? 0 : groupIndex + 1;
                    Debug.Log($"[RTS Control Group] Group {keyNumber} has no valid agents remaining");
                }
                return;
            }

            // Select the recalled agents
            SelectAgentsAndExternals(validIndices, validExternals, false);

            if (enableDebugLogging)
            {
                int keyNumber = groupIndex == 9 ? 0 : groupIndex + 1;
                Debug.Log($"[RTS Control Group] Recalled {validIndices.Count} agents and {validExternals.Count} externals from group {keyNumber}{(focusCamera ? " (focusing camera)" : "")}");
            }

            // Focus camera on control group if double-tapped
            if (focusCamera && (validIndices.Count > 0 || validExternals.Count > 0))
            {
                FocusCameraOnControlGroup(validIndices, validExternals);
            }
        }

        /// <summary>
        /// Focuses the camera on the center of the specified agents.
        /// </summary>
        private void FocusCameraOnControlGroup(List<int> agentIndices, List<IRTSExternalSelectable> externalSelectables)
        {
            if (_camera == null || agentIndices == null || agentIndices.Count == 0)
            {
                if (externalSelectables == null || externalSelectables.Count == 0)
                    return;
            }

            // Get RTSCameraController from the camera
            var cameraController = _camera.GetComponent<RTSCameraController>();
            if (cameraController == null)
                return;

            var agents = crowdController.GetAgents();
            Vector3 centerPosition = Vector3.zero;
            int validCount = 0;

            if (agentIndices != null)
            {
                foreach (int index in agentIndices)
                {
                    if (index >= 0 && index < agents.Count && agents[index] != null && agents[index].navigationAgent != null)
                    {
                        centerPosition += agents[index].WorldPosition;
                        validCount++;
                    }
                }
            }

            if (externalSelectables != null)
            {
                foreach (var selectable in externalSelectables)
                {
                    if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                        continue;

                    centerPosition += GetExternalSelectableWorldPosition(selectable);
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                centerPosition /= validCount;
                cameraController.FocusOnPosition(centerPosition);
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Control Group] Camera focusing on {validCount} units at {centerPosition}");
            }
        }

        /// <summary>
        /// Clears a specific control group.
        /// </summary>
        /// <param name="groupIndex">Control group index (0-9)</param>
        public void ClearControlGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= _controlGroups.Length)
                return;

            if (_controlGroups[groupIndex] != null)
                _controlGroups[groupIndex].Clear();
            if (_controlGroupsExternal[groupIndex] != null)
                _controlGroupsExternal[groupIndex].Clear();
        }

        /// <summary>
        /// Clears all control groups.
        /// </summary>
        public void ClearAllControlGroups()
        {
            for (int i = 0; i < _controlGroups.Length; i++)
            {
                if (_controlGroups[i] != null)
                    _controlGroups[i].Clear();
                if (_controlGroupsExternal[i] != null)
                    _controlGroupsExternal[i].Clear();
            }
        }

        /// <summary>
        /// Gets the agents in a specific control group.
        /// </summary>
        /// <param name="groupIndex">Control group index (0-9)</param>
        /// <returns>Read-only list of agent indices in the group</returns>
        public IReadOnlyList<int> GetControlGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= _controlGroups.Length || _controlGroups[groupIndex] == null)
                return new List<int>();
            
            return _controlGroups[groupIndex];
        }

        #endregion

        #region Input Processing

        private void ProcessInput()
        {
            // Skip all selection input when an external system (e.g. skill bar targeting mode) has suppressed it
            if (suppressCommandInput)
                return;

            // Skip selection input when clicking on UI elements
            // This prevents deselecting agents when clicking buttons
            if (IsPointerOverUI())
            {
                // Reset drag state if we were dragging and clicked on UI
                if (_isDragging)
                {
                    _isDragging = false;
                }
                return;
            }
            
            // Skip selection processing if we're adjusting formation spacing/rotation
            // (the selection button is used for rotation adjustment in that mode)
            bool isInFormationAdjustmentMode = _isAdjustingSpacing || _isAdjustingRotation;
            
            // Handle selection button down (only if not in formation adjustment mode)
            if (_selectionButtonDown && (enableClickSelection || enableDragSelection) && !isInFormationAdjustmentMode)
            {
                _dragStartPosition = _mousePosition;
                _isDragging = false;
                if (enableDebugLogging) Debug.Log($"[RTS Input] Selection button DOWN at {_mousePosition}");
            }

            // Check if we should start dragging (only if not in formation adjustment mode)
            if (enableDragSelection && !_isDragging && !isInFormationAdjustmentMode &&
                IsSelectionButtonHeld() && 
                Vector2.Distance(_dragStartPosition, _mousePosition) > minDragDistance)
            {
                _isDragging = true;
                if (enableDebugLogging) Debug.Log($"[RTS Input] Started DRAGGING - moved {Vector2.Distance(_dragStartPosition, _mousePosition):F1}px");
            }

            // Update drag position (only if actually dragging for selection, not formation adjustment)
            if (_isDragging && !isInFormationAdjustmentMode)
            {
                _dragCurrentPosition = _mousePosition;
            }

            // Handle selection button up (only if not in formation adjustment mode)
            if (_selectionButtonUp && !isInFormationAdjustmentMode)
            {
                // Calculate drag distance
                float dragDistance = Vector2.Distance(_dragStartPosition, _mousePosition);
                
                // Only consider it a drag if we actually moved significantly
                // This prevents small mouse movements during double-click from being treated as drags
                bool wasActualDrag = _isDragging && dragDistance > minDragDistance;
                
                if (enableDebugLogging) Debug.Log($"[RTS Input] Selection button UP - dragDist={dragDistance:F1}px, isDragging={_isDragging}, wasActualDrag={wasActualDrag}");
                
                if (wasActualDrag && enableDragSelection)
                {
                    // Complete drag selection
                    if (enableDebugLogging) Debug.Log("[RTS Input] -> Performing DRAG selection");
                    PerformDragSelection();
                }
                else if (enableClickSelection)
                {
                    // Perform click selection (including double-click detection)
                    if (enableDebugLogging) Debug.Log("[RTS Input] -> Performing CLICK selection");
                    PerformClickSelection();
                }

                _isDragging = false;
            }
            
            // Reset drag state if we were in formation adjustment mode and selection button was released
            if (_selectionButtonUp && isInFormationAdjustmentMode)
            {
                _isDragging = false;
            }

            // Handle command button (move command) with optional drag-to-adjust spacing
            ProcessMoveCommand();

            // Update move indicator
            if (_showingMoveIndicator && Time.time - _moveIndicatorStartTime > moveIndicatorDuration)
            {
                _showingMoveIndicator = false;
            }
        }

        private bool IsSelectionButtonHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return selectionButton != null && selectionButton.IsPressed();
#else
            return Input.GetMouseButton(selectionMouseButton);
#endif
        }

        /// <summary>
        /// Checks if the pointer is currently over an interactable UI element (Button, Toggle, etc.)
        /// or over the minimap. Used to prevent selection/deselection when clicking on UI.
        /// Only blocks input for actual interactable elements, not empty canvas areas.
        /// </summary>
        private bool IsPointerOverUI()
        {
            // Check if pointer is over the minimap - minimap handles its own click logic
            if (minimapController != null && minimapController.IsScreenPositionOverMinimap(_mousePosition))
            {
                return true;
            }
            
            // Check if EventSystem exists
            if (EventSystem.current == null)
                return false;
            
            // Use pointer event data to raycast against UI
            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = _mousePosition
            };
            
            var raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, raycastResults);
            
            // Check if any of the hit UI elements are interactable
            foreach (var result in raycastResults)
            {
                if (result.gameObject == null)
                    continue;
                
                // Check for common interactable UI components
                // Button, Toggle, Slider, Dropdown, InputField, ScrollRect, etc.
                // Also check for IPointerClickHandler to catch custom UI elements (e.g. skill slots)
                if (result.gameObject.GetComponent<UnityEngine.UI.Button>() != null ||
                    result.gameObject.GetComponent<UnityEngine.UI.Toggle>() != null ||
                    result.gameObject.GetComponent<UnityEngine.UI.Slider>() != null ||
                    result.gameObject.GetComponent<UnityEngine.UI.Dropdown>() != null ||
                    result.gameObject.GetComponent<UnityEngine.UI.InputField>() != null ||
                    result.gameObject.GetComponent<UnityEngine.UI.ScrollRect>() != null ||
                    result.gameObject.GetComponent<UnityEngine.UI.Scrollbar>() != null ||
                    result.gameObject.GetComponent<UnityEngine.EventSystems.IPointerClickHandler>() != null)
                {
                    return true;
                }
                
                // Also check parent objects in case the raycast hit a child (like button text)
                var parent = result.gameObject.transform.parent;
                while (parent != null)
                {
                    if (parent.GetComponent<UnityEngine.UI.Button>() != null ||
                        parent.GetComponent<UnityEngine.UI.Toggle>() != null ||
                        parent.GetComponent<UnityEngine.UI.Slider>() != null ||
                        parent.GetComponent<UnityEngine.UI.Dropdown>() != null ||
                        parent.GetComponent<UnityEngine.UI.InputField>() != null ||
                        parent.GetComponent<UnityEngine.UI.ScrollRect>() != null ||
                        parent.GetComponent<UnityEngine.UI.Scrollbar>() != null ||
                        parent.GetComponent<UnityEngine.EventSystems.IPointerClickHandler>() != null)
                    {
                        return true;
                    }
                    parent = parent.parent;
                }
            }
            
            return false;
        }

        #endregion

        #region Selection Logic

        private void PerformClickSelection()
        {
            Ray ray = _camera.ScreenPointToRay(_mousePosition);
            
            // First try: Physics raycast for agents with colliders
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, agentSelectionMask))
            {
                int agentIndex = GetAgentIndexFromHit(hit);
                
                if (agentIndex >= 0)
                {
                    if (TrySelectAgentAtIndex(agentIndex))
                        return;
                }

                IRTSExternalSelectable external = GetExternalSelectableFromHit(hit);
                if (external != null)
                {
                    if (TrySelectExternal(external))
                        return;
                }
            }

            // Second try: Ray-distance selection for units without colliders (accounts for camera pitch)
            const float clickRadius = 2f;
            int nearestAgentToRay = FindNearestAgentToRay(ray, clickRadius, out float agentDistSq, out float agentAlongRay);
            IRTSExternalSelectable nearestExternalToRay = FindNearestExternalSelectableToRay(ray, clickRadius, out float externalDistSq, out float externalAlongRay);

            if (nearestExternalToRay != null || nearestAgentToRay >= 0)
            {
                bool externalIsCloser = nearestExternalToRay != null &&
                                        (nearestAgentToRay < 0 ||
                                         externalDistSq < agentDistSq ||
                                         (Mathf.Approximately(externalDistSq, agentDistSq) && externalAlongRay < agentAlongRay));

                if (externalIsCloser)
                {
                    if (TrySelectExternal(nearestExternalToRay))
                        return;
                }

                if (nearestAgentToRay >= 0)
                {
                    if (TrySelectAgentAtIndex(nearestAgentToRay))
                        return;
                }
            }
            
            // Fallback: Ground-projected selection when ray distance didn't match anything
            if (Physics.Raycast(ray, out RaycastHit groundHit, 1000f, groundMask))
            {
                int nearestAgent = FindNearestAgentToPoint(groundHit.point, 2f); // 2 unit click radius
                if (nearestAgent >= 0)
                {
                    if (TrySelectAgentAtIndex(nearestAgent))
                        return;
                }

                IRTSExternalSelectable nearestExternal = FindNearestExternalSelectable(groundHit.point, 2f);
                if (nearestExternal != null)
                {
                    if (TrySelectExternal(nearestExternal))
                        return;
                }
            }

            // Clicked on nothing - deselect all (unless holding additive key)
            if (!_additiveKeyHeld)
            {
                ClearSelection();
            }
        }

        /// <summary>
        /// Finds the nearest agent to a world point within a given radius.
        /// Used for click selection when agents don't have colliders.
        /// </summary>
        private int FindNearestAgentToPoint(Vector3 point, float maxDistance)
        {
            var agents = crowdController.GetAgents();
            int nearestIndex = -1;
            float nearestDistSq = maxDistance * maxDistance;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                // Use the visual world position (includes flight offset) so clicking
                // near an airborne Fly-Effect agent's mesh can select it.
                Vector3 agentPos = GetAgentWorldPosition(agent);
                float dx = agentPos.x - point.x;
                float dz = agentPos.z - point.z;
                float distSq = dx * dx + dz * dz;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        /// <summary>
        /// Finds the nearest agent to a selection ray within a given radius.
        /// Uses ray distance to account for camera pitch with units above ground.
        /// </summary>
        private int FindNearestAgentToRay(Ray ray, float maxDistance, out float nearestDistSq, out float nearestAlongRay)
        {
            var agents = crowdController.GetAgents();
            int nearestIndex = -1;
            nearestDistSq = maxDistance * maxDistance;
            nearestAlongRay = float.PositiveInfinity;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                // Use the visual world position (includes flight offset) so the ray
                // test matches where the agent mesh actually is in 3D space.
                Vector3 agentPos = GetAgentWorldPosition(agent);
                if (!TryGetRayDistanceSq(ray, agentPos, out float distSq, out float alongRay))
                    continue;

                if (distSq <= nearestDistSq)
                {
                    if (distSq < nearestDistSq || alongRay < nearestAlongRay)
                    {
                        nearestDistSq = distSq;
                        nearestAlongRay = alongRay;
                        nearestIndex = i;
                    }
                }
            }

            return nearestIndex;
        }

        private IRTSExternalSelectable GetExternalSelectableFromHit(RaycastHit hit)
        {
            Transform current = hit.transform;
            while (current != null)
            {
                var behaviours = current.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IRTSExternalSelectable selectable)
                    {
                        return selectable;
                    }
                }
                current = current.parent;
            }
            return null;
        }

        private IRTSExternalSelectable FindNearestExternalSelectable(Vector3 point, float maxDistance)
        {
            IRTSExternalSelectable nearest = null;
            float nearestDistSq = maxDistance * maxDistance;

            for (int i = 0; i < _externalSelectables.Count; i++)
            {
                IRTSExternalSelectable selectable = _externalSelectables[i];
                if (!IsExternalSelectableValid(selectable, requireSelectable: true))
                    continue;

                Vector3 pos = GetExternalSelectableWorldPosition(selectable);
                float dx = pos.x - point.x;
                float dz = pos.z - point.z;
                float distSq = dx * dx + dz * dz;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = selectable;
                }
            }

            return nearest;
        }

        private IRTSExternalSelectable FindNearestExternalSelectableToRay(
            Ray ray,
            float maxDistance,
            out float nearestDistSq,
            out float nearestAlongRay)
        {
            IRTSExternalSelectable nearest = null;
            nearestDistSq = maxDistance * maxDistance;
            nearestAlongRay = float.PositiveInfinity;

            for (int i = 0; i < _externalSelectables.Count; i++)
            {
                IRTSExternalSelectable selectable = _externalSelectables[i];
                if (!IsExternalSelectableValid(selectable, requireSelectable: true))
                    continue;

                Vector3 pos = GetExternalSelectableWorldPosition(selectable);
                if (!TryGetRayDistanceSq(ray, pos, out float distSq, out float alongRay))
                    continue;

                if (distSq <= nearestDistSq)
                {
                    if (distSq < nearestDistSq || alongRay < nearestAlongRay)
                    {
                        nearestDistSq = distSq;
                        nearestAlongRay = alongRay;
                        nearest = selectable;
                    }
                }
            }

            return nearest;
        }

        private static Vector3 GetExternalSelectableWorldPosition(IRTSExternalSelectable selectable)
        {
            if (selectable == null) return Vector3.zero;
            INavigationAgent navigationAgent = GetExternalNavigationAgent(selectable);
            if (navigationAgent?.Transform != null) return navigationAgent.Transform.position;
            if (navigationAgent != null) return navigationAgent.Position;
            if (selectable.Transform != null) return selectable.Transform.position;
            if (TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent legacyNavAgent)) return legacyNavAgent.transform.position;
            return Vector3.zero;
        }

        private static INavigationAgent GetExternalNavigationAgent(IRTSExternalSelectable selectable)
        {
            if (selectable is IRTSExternalNavigationSelectable selectableWithNavigation)
                return selectableWithNavigation.NavigationAgent;

            NavMeshAgent navMeshAgent = selectable?.NavAgent;
            if (navMeshAgent == null)
                return null;

            return navMeshAgent.GetComponent<Navigation.Abstraction.Adapters.NavMeshAgentAdapter>();
        }

        private static bool TryGetLegacyExternalNavAgent(IRTSExternalSelectable selectable, out NavMeshAgent navMeshAgent)
        {
            navMeshAgent = selectable?.NavAgent;
            return navMeshAgent != null;
        }

        private static bool IsExternalSelectableValid(IRTSExternalSelectable selectable, bool requireSelectable)
        {
            // C# null check + Unity destroyed-object check (MonoBehaviours may be
            // destroyed but still referenced; comparing to Object catches that).
            if (selectable == null || (selectable is UnityEngine.Object uObj && uObj == null))
                return false;

            if (!selectable.IsAlive)
                return false;

            if (requireSelectable && !selectable.IsSelectable)
                return false;

            // Navigation agent is optional — units without one are still valid for selection,
            // skill bar, and portrait bar. Move commands will simply be ignored.
            return selectable.Transform != null;
        }

        private static bool CanControlExternalNavigation(IRTSExternalSelectable selectable)
        {
            if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                return false;

            INavigationAgent navigationAgent = GetExternalNavigationAgent(selectable);
            if (navigationAgent != null)
            {
                if (!navigationAgent.Enabled)
                    return false;

                if (!navigationAgent.Capabilities.RequiresSurfaceValidationForControl)
                    return true;

                return navigationAgent.IsOnNavMesh;
            }

            if (!TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent navMeshAgent))
                return false;

            return navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
        }

        private static bool TrySetExternalNavigationStopped(IRTSExternalSelectable selectable, bool isStopped, INavigationAgent navigationAgent = null)
        {
            if (navigationAgent == null)
                navigationAgent = GetExternalNavigationAgent(selectable);

            if (navigationAgent != null)
            {
                navigationAgent.IsStopped = isStopped;
                return true;
            }

            if (!TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent legacyNavAgent))
                return false;

            legacyNavAgent.isStopped = isStopped;
            return true;
        }

        private static bool TrySetExternalStoppingDistance(IRTSExternalSelectable selectable, float stoppingDistance, INavigationAgent navigationAgent = null)
        {
            if (navigationAgent == null)
                navigationAgent = GetExternalNavigationAgent(selectable);

            if (navigationAgent != null)
            {
                navigationAgent.StoppingDistance = stoppingDistance;
                return true;
            }

            if (!TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent legacyNavAgent))
                return false;

            legacyNavAgent.stoppingDistance = stoppingDistance;
            return true;
        }

        private static bool TryGetExternalPathState(IRTSExternalSelectable selectable, INavigationAgent navigationAgent, out bool hasPath, out bool pathPending)
        {
            hasPath = false;
            pathPending = false;

            if (navigationAgent != null)
            {
                hasPath = navigationAgent.HasPath;
                pathPending = navigationAgent.Capabilities.SupportsPathPending && navigationAgent.PathPending;
                return true;
            }

            if (!TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent legacyNavAgent))
                return false;

            hasPath = legacyNavAgent.hasPath;
            pathPending = legacyNavAgent.pathPending;
            return true;
        }

        private static bool TryGetExternalRemainingDistance(IRTSExternalSelectable selectable, INavigationAgent navigationAgent, out float remainingDistance)
        {
            remainingDistance = float.PositiveInfinity;

            if (navigationAgent != null)
            {
                remainingDistance = navigationAgent.RemainingDistance;
                return true;
            }

            if (!TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent legacyNavAgent))
                return false;

            remainingDistance = legacyNavAgent.remainingDistance;
            return true;
        }

        private bool EnsureExternalNavigationAvailable(IRTSExternalSelectable selectable, float sampleDistance, ref int recoveredCount)
        {
            if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                return false;

            INavigationAgent navigationAgent = GetExternalNavigationAgent(selectable);
            if (navigationAgent != null)
            {
                if (!navigationAgent.Enabled)
                    navigationAgent.Enabled = true;

                if (!navigationAgent.Capabilities.RequiresSurfaceValidationForControl || navigationAgent.IsOnNavMesh)
                    return true;

                Vector3 recoverAnchor = GetExternalSelectableWorldPosition(selectable);
                bool recovered = false;

                if (navigationAgent.SamplePosition(recoverAnchor, out Vector3 sampledPos, sampleDistance))
                {
                    recovered = navigationAgent.Warp(sampledPos);
                }
                else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(recoverAnchor, sampleDistance, out Vector3 navMeshPos))
                {
                    recovered = navigationAgent.Warp(navMeshPos);
                }

                if (!recovered && !navigationAgent.IsOnNavMesh)
                    return false;

                if (navigationAgent.IsOnNavMesh)
                    recoveredCount++;

                return true;
            }

            if (!TryGetLegacyExternalNavAgent(selectable, out NavMeshAgent navAgent))
                return false;

            if (!navAgent.enabled)
                navAgent.enabled = true;

            if (navAgent.isOnNavMesh)
                return true;

            Vector3 agentPos = navAgent.transform.position;
            if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(agentPos, sampleDistance, out Vector3 recoverHit))
            {
                navAgent.Warp(recoverHit);
                if (navAgent.isOnNavMesh)
                {
                    recoveredCount++;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetRayDistanceSq(Ray ray, Vector3 point, out float distSq, out float alongRay)
        {
            Vector3 toPoint = point - ray.origin;
            alongRay = Vector3.Dot(toPoint, ray.direction);
            if (alongRay < 0f)
            {
                distSq = float.PositiveInfinity;
                return false;
            }

            Vector3 closest = ray.origin + ray.direction * alongRay;
            distSq = (point - closest).sqrMagnitude;
            return true;
        }

        /// <summary>
        /// Attempts to select an agent at the given index, handling faction restrictions and click type.
        /// Returns true if selection was handled, false if click should fall through.
        /// </summary>
        private bool TrySelectAgentAtIndex(int agentIndex)
        {
            // Check faction restriction
            if (restrictSelectionToPlayerFactions && !IsPlayerControlled(agentIndex))
            {
                if (enableDebugLogging) Debug.Log($"[RTS] Agent {agentIndex} not player-controlled, ignoring click");
                // Clicked on non-player faction - treat as clicking nothing
                if (!_additiveKeyHeld) ClearSelection();
                return true; // Handled (as a clear)
            }

            float currentTime = Time.time;
            float timeSinceLastClick = currentTime - _lastClickTime;
            bool sameAgent = agentIndex == _lastClickedAgentIndex;
            bool withinTimeWindow = timeSinceLastClick < doubleClickTime;
            bool isDoubleClick = enableDoubleClickGroupSelection && sameAgent && withinTimeWindow;

            if (enableDebugLogging)
            {
                Debug.Log($"[RTS Click] agent={agentIndex}, lastAgent={_lastClickedAgentIndex}, " +
                          $"timeSince={timeSinceLastClick:F3}s, threshold={doubleClickTime}s, " +
                          $"sameAgent={sameAgent}, withinTime={withinTimeWindow}, isDouble={isDoubleClick}");
            }

            if (isDoubleClick)
            {
                // Double-click: select all agents of the same faction/prefab
                if (enableDebugLogging) Debug.Log($"[RTS] DOUBLE-CLICK detected on agent {agentIndex} - selecting group");
                SelectAgentGroup(agentIndex);
            }
            else
            {
                // Single click: select individual agent
                if (_additiveKeyHeld)
                {
                    ToggleAgentSelection(agentIndex);
                }
                else
                {
                    SelectSingleAgent(agentIndex);
                }
            }

            _lastClickTime = currentTime;
            _lastClickedAgentIndex = agentIndex;
            return true;
        }

        private bool TrySelectExternal(IRTSExternalSelectable selectable)
        {
            if (!IsExternalSelectableValid(selectable, requireSelectable: true))
                return false;

            if (restrictSelectionToPlayerFactions && !selectable.SkipFactionFilter && !AllFactionsSelectable)
            {
                if (!_additiveKeyHeld) ClearSelection();
                return true;
            }

            float currentTime = Time.time;
            float timeSinceLastClick = currentTime - _lastExternalClickTime;
            bool sameExternal = selectable == _lastClickedExternalSelectable;
            bool withinTimeWindow = timeSinceLastClick < doubleClickTime;
            bool isDoubleClick = enableDoubleClickGroupSelection && sameExternal && withinTimeWindow;

            if (isDoubleClick && TrySelectExternalGroup(selectable))
            {
                _lastExternalClickTime = currentTime;
                _lastClickedExternalSelectable = selectable;
                return true;
            }

            if (_additiveKeyHeld)
            {
                if (_selectedExternalSelectables.Contains(selectable))
                {
                    _selectedExternalSelectables.Remove(selectable);
                }
                else
                {
                    _selectedExternalSelectables.Add(selectable);
                }
            }
            else
            {
                _selectedAgentIndices.Clear();
                _selectedExternalSelectables.Clear();
                _selectedExternalSelectables.Add(selectable);
            }

            OnSelectionChanged();
            _lastExternalClickTime = currentTime;
            _lastClickedExternalSelectable = selectable;
            return true;
        }

        private void PerformDragSelection()
        {
            Rect selectionRect = GetSelectionRect();
            List<int> agentsInRect = new List<int>();
            List<IRTSExternalSelectable> externalsInRect = new List<IRTSExternalSelectable>();

            var agents = crowdController.GetAgents();
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead)
                    continue;

                // Check faction restriction
                if (restrictSelectionToPlayerFactions && !IsPlayerControlled(agent))
                    continue;

                Vector3 worldPos = GetAgentWorldPosition(agent);
                Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);

                // Check if agent is in front of camera and within selection rect
                if (screenPos.z > 0 && selectionRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                {
                    agentsInRect.Add(i);
                }
            }

            // External units (e.g., factionless NavMesh agents)
            for (int i = 0; i < _externalSelectables.Count; i++)
            {
                IRTSExternalSelectable selectable = _externalSelectables[i];
                if (!IsExternalSelectableValid(selectable, requireSelectable: true))
                    continue;

                if (restrictSelectionToPlayerFactions && !selectable.SkipFactionFilter && !AllFactionsSelectable)
                    continue;

                Vector3 worldPos = GetExternalSelectableWorldPosition(selectable);
                Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0 && selectionRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                {
                    externalsInRect.Add(selectable);
                }
            }

            if (_additiveKeyHeld)
            {
                // Add to existing selection
                foreach (int index in agentsInRect)
                {
                    if (!_selectedAgentIndices.Contains(index))
                    {
                        _selectedAgentIndices.Add(index);
                    }
                }

                foreach (var selectable in externalsInRect)
                {
                    if (!_selectedExternalSelectables.Contains(selectable))
                    {
                        _selectedExternalSelectables.Add(selectable);
                    }
                }
            }
            else
            {
                // Replace selection
                _selectedAgentIndices.Clear();
                _selectedAgentIndices.AddRange(agentsInRect);

                _selectedExternalSelectables.Clear();
                _selectedExternalSelectables.AddRange(externalsInRect);
            }

            OnSelectionChanged();
        }

        private int GetAgentIndexFromHit(RaycastHit hit)
        {
            // Try to find agent by matching GameObjects or navigation components
            Transform current = hit.transform;
            var agents = crowdController.GetAgents();
            
            while (current != null)
            {
                GameObject currentGO = current.gameObject;
                
                // Iterate through all agents and check if any of them match this GameObject
                for (int i = 0; i < agents.Count; i++)
                {
                    var agent = agents[i];
                    if (agent == null) continue;
                    
                    // Check crowdInstance GameObject
                    if (agent.crowdInstance != null && agent.crowdInstance.gameObject == currentGO)
                    {
                        return i;
                    }
                    
                    // Check navigation agent's underlying component
                    if (agent.navigationAgent != null)
                    {
                        var underlyingComponent = agent.navigationAgent.GetUnderlyingAgent<Component>();
                        if (underlyingComponent != null && underlyingComponent.gameObject == currentGO)
                        {
                            return i;
                        }
                    }
                }
                
                current = current.parent;
            }
            return -1;
        }

        /// <summary>
        /// Check if an agent belongs to a player-controlled faction.
        /// </summary>
        private bool IsPlayerControlled(CrowdAgent agent)
        {
            if (agent == null) return false;
            if (AllFactionsSelectable) return true;
            
            string factionName = agent.enemyCrowd != null ? agent.enemyCrowd.factionName : null;
            return !string.IsNullOrEmpty(factionName) && playerControlledFactions.ContainsFaction(factionName);
        }

        /// <summary>
        /// Check if an agent at the given index is player-controlled.
        /// </summary>
        private bool IsPlayerControlled(int agentIndex)
        {
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count) return false;
            return IsPlayerControlled(agents[agentIndex]);
        }

        /// <summary>
        /// Public wrapper to check if an agent index belongs to a player-controlled faction.
        /// </summary>
        public bool IsAgentPlayerControlled(int agentIndex)
        {
            return IsPlayerControlled(agentIndex);
        }

        private Vector3 GetAgentWorldPosition(CrowdAgent agent)
        {
            Vector3 pos;
            if (agent.navigationAgent != null)
                pos = agent.WorldPosition;
            else if (agent.crowdInstance != null)
                pos = agent.crowdInstance.transform.position;
            else
                pos = Vector3.zero;

            // Include flight height offset so selection indicators follow the elevated visual model
            if (agent.flightCurrentHeight > 0.01f)
            {
                pos.y += agent.flightCurrentHeight;

                // Include hover bob offset so indicator stays in sync with the bobbing visual
                EnemyCrowd flightCrowd = agent.enemyCrowd;
                if (flightCrowd != null && flightCrowd.flightBobAmplitude > 0f &&
                    Mathf.Approximately(agent.flightCurrentHeight, agent.flightTargetHeight) && agent.flightTargetHeight > 0.01f)
                {
                    float phase = agent.index * 2.3917f;
                    pos.y += Mathf.Sin(Time.time * flightCrowd.flightBobFrequency * 6.2831853f + phase)
                             * flightCrowd.flightBobAmplitude;
                }
            }

            return pos;
        }

        private void SelectSingleAgent(int agentIndex)
        {
            _selectedAgentIndices.Clear();
            _selectedExternalSelectables.Clear();
            _selectedAgentIndices.Add(agentIndex);
            OnSelectionChanged();
        }

        private void ToggleAgentSelection(int agentIndex)
        {
            if (_selectedAgentIndices.Contains(agentIndex))
            {
                _selectedAgentIndices.Remove(agentIndex);
            }
            else
            {
                _selectedAgentIndices.Add(agentIndex);
            }
            OnSelectionChanged();
        }

        private void SelectAgentGroup(int agentIndex)
        {
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count)
                return;

            CrowdAgent clickedAgent = agents[agentIndex];
            if (clickedAgent == null)
                return;

            // Get the faction/enemyCrowd of the clicked agent
            EnemyCrowd targetFaction = clickedAgent.enemyCrowd;
            int targetEnemyCrowdIndex = clickedAgent.enemyCrowdIndex;

            if (!_additiveKeyHeld)
            {
                _selectedAgentIndices.Clear();
                _selectedExternalSelectables.Clear();
            }
            
            // Get camera frustum planes if filtering by frustum
            Plane[] frustumPlanes = null;
            if (doubleClickOnlyInFrustum && _camera != null)
            {
                frustumPlanes = GeometryUtility.CalculateFrustumPlanes(_camera);
            }

            // Select all agents of the same faction
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead)
                    continue;

                // Check faction restriction
                if (restrictSelectionToPlayerFactions && !IsPlayerControlled(agent))
                    continue;

                bool sameGroup = (targetFaction != null && agent.enemyCrowd == targetFaction) ||
                                (targetFaction == null && agent.enemyCrowdIndex == targetEnemyCrowdIndex);

                if (sameGroup && !_selectedAgentIndices.Contains(i))
                {
                    // Check frustum visibility if enabled
                    if (frustumPlanes != null && agent.transform != null)
                    {
                        // Create a bounds for the agent (using a small sphere at agent position)
                        Bounds agentBounds = new Bounds(agent.transform.position, Vector3.one * 0.5f);
                        if (!GeometryUtility.TestPlanesAABB(frustumPlanes, agentBounds))
                        {
                            // Agent is outside camera frustum, skip it
                            continue;
                        }
                    }
                    
                    _selectedAgentIndices.Add(i);
                }
            }

            OnSelectionChanged();
        }

        private bool TrySelectExternalGroup(IRTSExternalSelectable selectable)
        {
            if (!IsGameCreatorPlayerSelectable(selectable))
                return false;

            if (!_additiveKeyHeld)
            {
                _selectedAgentIndices.Clear();
                _selectedExternalSelectables.Clear();
            }

            for (int i = 0; i < _externalSelectables.Count; i++)
            {
                IRTSExternalSelectable candidate = _externalSelectables[i];
                if (!IsExternalSelectableValid(candidate, requireSelectable: true))
                    continue;
                if (restrictSelectionToPlayerFactions && !candidate.SkipFactionFilter && !AllFactionsSelectable)
                    continue;
                if (!IsGameCreatorPlayerSelectable(candidate))
                    continue;

                if (!_selectedExternalSelectables.Contains(candidate))
                {
                    _selectedExternalSelectables.Add(candidate);
                }
            }

            OnSelectionChanged();
            return true;
        }

        private static bool IsGameCreatorPlayerSelectable(IRTSExternalSelectable selectable)
        {
            if (selectable == null) return false;

            const string gc2PlayerType = "GameCreator.Runtime.Characters.UnitPlayerEnemyMassesRTS";
            for (Type type = selectable.GetType(); type != null; type = type.BaseType)
            {
                if (string.Equals(type.FullName, gc2PlayerType, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Clear all selected agents.
        /// </summary>
        public void ClearSelection()
        {
            _selectedAgentIndices.Clear();
            _selectedExternalSelectables.Clear();
            _isAwaitingPostCombatReformation = false; // Cancel post-combat reformation tracking
            OnSelectionChanged();
        }

        /// <summary>
        /// Select agents by their indices.
        /// </summary>
        public void SelectAgents(IEnumerable<int> agentIndices, bool additive = false)
        {
            if (!additive)
            {
                _selectedAgentIndices.Clear();
            }

            foreach (int index in agentIndices)
            {
                if (!_selectedAgentIndices.Contains(index))
                {
                    _selectedAgentIndices.Add(index);
                }
            }

            OnSelectionChanged();
        }

        private void SelectAgentsAndExternals(
            IEnumerable<int> agentIndices,
            IEnumerable<IRTSExternalSelectable> externalSelectables,
            bool additive)
        {
            if (!additive)
            {
                _selectedAgentIndices.Clear();
                _selectedExternalSelectables.Clear();
            }

            if (agentIndices != null)
            {
                foreach (int index in agentIndices)
                {
                    if (!_selectedAgentIndices.Contains(index))
                    {
                        _selectedAgentIndices.Add(index);
                    }
                }
            }

            if (externalSelectables != null)
            {
                foreach (var selectable in externalSelectables)
                {
                    if (selectable != null && !_selectedExternalSelectables.Contains(selectable))
                    {
                        _selectedExternalSelectables.Add(selectable);
                    }
                }
            }

            OnSelectionChanged();
        }

        /// <summary>
        /// Select all agents of a specific faction.
        /// </summary>
        public void SelectFaction(int enemyCrowdIndex, bool additive = false)
        {
            if (!additive)
            {
                _selectedAgentIndices.Clear();
            }

            var agents = crowdController.GetAgents();
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead)
                    continue;

                if (agent.enemyCrowdIndex == enemyCrowdIndex && !_selectedAgentIndices.Contains(i))
                {
                    _selectedAgentIndices.Add(i);
                }
            }

            OnSelectionChanged();
        }

        #endregion

        #region Combat Stance API

        /// <summary>
        /// Sets the combat stance for all currently selected agents.
        /// </summary>
        /// <param name="stance">The combat stance to apply (Offensive, Defensive, or HoldPosition)</param>
        public void SetSelectedAgentsCombatStance(CombatStance stance)
        {
            if (crowdController == null || _selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead)
                    {
                        SetAgentCombatStance(agent, stance);
                    }
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set combat stance to {stance} for {_selectedAgentIndices.Count} selected agents");
        }

        /// <summary>
        /// Sets a specific agent to Offensive stance. Agents actively seek and attack enemies.
        /// </summary>
        public void SetSelectedAgentsOffensive()
        {
            SetSelectedAgentsCombatStance(CombatStance.Offensive);
        }

        /// <summary>
        /// Sets a specific agent to Defensive stance. Agents only attack if attacked first.
        /// </summary>
        public void SetSelectedAgentsDefensive()
        {
            SetSelectedAgentsCombatStance(CombatStance.Defensive);
        }

        /// <summary>
        /// Sets a specific agent to Hold Position stance. Agents stay in place and attack enemies in range.
        /// </summary>
        public void SetSelectedAgentsHoldPosition()
        {
            SetSelectedAgentsCombatStance(CombatStance.HoldPosition);
        }

        /// <summary>
        /// Sets selected agents to Passive stance. Agents will not engage in combat under any circumstances.
        /// They follow move commands, climb walls, and navigate normally, but have zero aggro range and 
        /// will not attack even when attacked. They will only attack if given an explicit attack command.
        /// </summary>
        public void SetSelectedAgentsPassive()
        {
            if (crowdController == null || _selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead)
                    {
                        SetAgentCombatStance(agent, CombatStance.Passive);
                        // Passive agents should ignore ally defense alerts
                        agent.ignoreAllyDefenseWhileHolding = true;
                        // Clear any existing attack targets (they won't auto-acquire new ones)
                        agent.isAggroed = false;
                        agent.isChasing = false;
                        if (!agent.hasManualAttackCommand)
                        {
                            agent.attackTargetAgentIndex = -1;
                            agent.attackTargetTransform = null;
                            agent.hasAttackTargetPosition = false;
                        }
                    }
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set passive stance for {_selectedAgentIndices.Count} selected agents");
        }

        /// <summary>
        /// Sets selected agents to Offensive stance AND ignores Ally Defense System alerts.
        /// These agents will actively seek enemies but won't respond to nearby allies being attacked.
        /// Useful for maintaining offensive pressure without units being pulled away to defend.
        /// </summary>
        /// <param name="ignoreAllyDefense">If true, agents won't respond to ally defense alerts</param>
        public void SetSelectedAgentsOffensiveStrict(bool ignoreAllyDefense = true)
        {
            if (crowdController == null || _selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead)
                    {
                        SetAgentCombatStance(agent, CombatStance.Offensive);
                        agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                    }
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set strict offensive (ignoreAllyDefense={ignoreAllyDefense}) for {_selectedAgentIndices.Count} selected agents");
        }

        /// <summary>
        /// Sets selected agents to Defensive stance AND ignores Ally Defense System alerts.
        /// These agents will only retaliate when attacked directly and won't respond to allies being attacked.
        /// Ideal for reserve units or guards that should stay at their posts.
        /// </summary>
        /// <param name="ignoreAllyDefense">If true, agents won't respond to ally defense alerts</param>
        public void SetSelectedAgentsDefensiveStrict(bool ignoreAllyDefense = true)
        {
            if (crowdController == null || _selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead)
                    {
                        SetAgentCombatStance(agent, CombatStance.Defensive);
                        agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                    }
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set strict defensive (ignoreAllyDefense={ignoreAllyDefense}) for {_selectedAgentIndices.Count} selected agents");
        }

        /// <summary>
        /// Sets selected agents to Hold Position stance AND ignores Ally Defense System alerts.
        /// These agents will stay at their position and not respond to nearby allies being attacked.
        /// Ideal for static guard posts or archer lines that should never leave their position.
        /// </summary>
        /// <param name="ignoreAllyDefense">If true, agents won't respond to ally defense alerts while holding position</param>
        public void SetSelectedAgentsHoldPositionStrict(bool ignoreAllyDefense = true)
        {
            if (crowdController == null || _selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead)
                    {
                        SetAgentCombatStance(agent, CombatStance.HoldPosition);
                        agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                    }
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set strict hold position (ignoreAllyDefense={ignoreAllyDefense}) for {_selectedAgentIndices.Count} selected agents");
        }

        /// <summary>
        /// Sets a specific agent to Hold Position with optional Ally Defense ignore.
        /// </summary>
        /// <param name="agentIndex">Index of the agent</param>
        /// <param name="ignoreAllyDefense">If true, agent won't respond to ally defense alerts while holding</param>
        public void SetAgentHoldPositionStrict(int agentIndex, bool ignoreAllyDefense = true)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            if (agentIndex >= 0 && agentIndex < agents.Count)
            {
                CrowdAgent agent = agents[agentIndex];
                if (agent != null && !agent.isDead)
                {
                    SetAgentCombatStance(agent, CombatStance.HoldPosition);
                    agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                }
            }
        }

        /// <summary>
        /// Sets a specific agent to Offensive stance with optional Ally Defense ignore.
        /// </summary>
        /// <param name="agentIndex">Index of the agent</param>
        /// <param name="ignoreAllyDefense">If true, agent won't respond to ally defense alerts</param>
        public void SetAgentOffensiveStrict(int agentIndex, bool ignoreAllyDefense = true)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            if (agentIndex >= 0 && agentIndex < agents.Count)
            {
                CrowdAgent agent = agents[agentIndex];
                if (agent != null && !agent.isDead)
                {
                    SetAgentCombatStance(agent, CombatStance.Offensive);
                    agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                }
            }
        }

        /// <summary>
        /// Sets a specific agent to Defensive stance with optional Ally Defense ignore.
        /// </summary>
        /// <param name="agentIndex">Index of the agent</param>
        /// <param name="ignoreAllyDefense">If true, agent won't respond to ally defense alerts</param>
        public void SetAgentDefensiveStrict(int agentIndex, bool ignoreAllyDefense = true)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            if (agentIndex >= 0 && agentIndex < agents.Count)
            {
                CrowdAgent agent = agents[agentIndex];
                if (agent != null && !agent.isDead)
                {
                    SetAgentCombatStance(agent, CombatStance.Defensive);
                    agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                }
            }
        }

        /// <summary>
        /// Sets all agents in a faction to Hold Position with optional Ally Defense ignore.
        /// </summary>
        /// <param name="enemyCrowdIndex">Index of the EnemyCrowd/faction</param>
        /// <param name="ignoreAllyDefense">If true, agents won't respond to ally defense alerts while holding</param>
        public void SetFactionHoldPositionStrict(int enemyCrowdIndex, bool ignoreAllyDefense = true)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            int count = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent != null && !agent.isDead && agent.enemyCrowdIndex == enemyCrowdIndex)
                {
                    SetAgentCombatStance(agent, CombatStance.HoldPosition);
                    agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                    count++;
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set strict hold position (ignoreAllyDefense={ignoreAllyDefense}) for {count} agents in faction {enemyCrowdIndex}");
        }

        /// <summary>
        /// Sets all agents in a faction to Offensive stance with optional Ally Defense ignore.
        /// </summary>
        /// <param name="enemyCrowdIndex">Index of the EnemyCrowd/faction</param>
        /// <param name="ignoreAllyDefense">If true, agents won't respond to ally defense alerts</param>
        public void SetFactionOffensiveStrict(int enemyCrowdIndex, bool ignoreAllyDefense = true)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            int count = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent != null && !agent.isDead && agent.enemyCrowdIndex == enemyCrowdIndex)
                {
                    SetAgentCombatStance(agent, CombatStance.Offensive);
                    agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                    count++;
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set strict offensive (ignoreAllyDefense={ignoreAllyDefense}) for {count} agents in faction {enemyCrowdIndex}");
        }

        /// <summary>
        /// Sets all agents in a faction to Defensive stance with optional Ally Defense ignore.
        /// </summary>
        /// <param name="enemyCrowdIndex">Index of the EnemyCrowd/faction</param>
        /// <param name="ignoreAllyDefense">If true, agents won't respond to ally defense alerts</param>
        public void SetFactionDefensiveStrict(int enemyCrowdIndex, bool ignoreAllyDefense = true)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            int count = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent != null && !agent.isDead && agent.enemyCrowdIndex == enemyCrowdIndex)
                {
                    SetAgentCombatStance(agent, CombatStance.Defensive);
                    agent.ignoreAllyDefenseWhileHolding = ignoreAllyDefense;
                    count++;
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set strict defensive (ignoreAllyDefense={ignoreAllyDefense}) for {count} agents in faction {enemyCrowdIndex}");
        }

        /// <summary>
        /// Sets the combat stance for a specific agent by index.
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the crowd controller's agent list</param>
        /// <param name="stance">The combat stance to apply</param>
        public void SetAgentCombatStanceByIndex(int agentIndex, CombatStance stance)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            if (agentIndex >= 0 && agentIndex < agents.Count)
            {
                CrowdAgent agent = agents[agentIndex];
                if (agent != null && !agent.isDead)
                {
                    SetAgentCombatStance(agent, stance);
                }
            }
        }

        /// <summary>
        /// Sets the combat stance for all agents in a specific faction/EnemyCrowd.
        /// </summary>
        /// <param name="enemyCrowdIndex">Index of the EnemyCrowd in the crowd controller</param>
        /// <param name="stance">The combat stance to apply</param>
        public void SetFactionCombatStance(int enemyCrowdIndex, CombatStance stance)
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            int count = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent != null && !agent.isDead && agent.enemyCrowdIndex == enemyCrowdIndex)
                {
                    SetAgentCombatStance(agent, stance);
                    count++;
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set combat stance to {stance} for {count} agents in faction {enemyCrowdIndex}");
        }

        /// <summary>
        /// Sets the combat stance for all agents in a faction by name.
        /// </summary>
        /// <param name="factionName">Name of the faction</param>
        /// <param name="stance">The combat stance to apply</param>
        public void SetFactionCombatStanceByName(string factionName, CombatStance stance)
        {
            if (crowdController == null || string.IsNullOrEmpty(factionName))
                return;

            var agents = crowdController.GetAgents();
            int count = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent != null && !agent.isDead && 
                    agent.enemyCrowd != null && agent.enemyCrowd.factionName == factionName)
                {
                    SetAgentCombatStance(agent, stance);
                    count++;
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Set combat stance to {stance} for {count} agents in faction '{factionName}'");
        }

        /// <summary>
        /// Gets the combat stance of a specific agent.
        /// </summary>
        public CombatStance GetAgentCombatStance(int agentIndex)
        {
            if (crowdController == null)
                return CombatStance.Offensive;

            var agents = crowdController.GetAgents();
            if (agentIndex >= 0 && agentIndex < agents.Count)
            {
                CrowdAgent agent = agents[agentIndex];
                if (agent != null)
                    return agent.combatStance;
            }
            return CombatStance.Offensive;
        }

        /// <summary>
        /// Internal method to apply combat stance to an agent with all necessary state changes.
        /// </summary>
        private void SetAgentCombatStance(CrowdAgent agent, CombatStance stance)
        {
            CombatStance previousStance = agent.combatStance;
            agent.combatStance = stance;

            switch (stance)
            {
                case CombatStance.Offensive:
                    // Offensive: Return to normal combat behavior
                    // Clear hold position state and ally defense ignore
                    agent.hasHoldPositionSet = false;
                    agent.ignoreAllyDefenseWhileHolding = false;

                    // Release manual/formation locks so agents can freely engage
                    agent.hasManualCommand = false;
                    agent.manualCommandDestination = Vector3.zero;
                    agent.hasRTSFormationPosition = false;
                    agent.brokeFromRTSFormation = false;

                    // Ensure navigation can resume normal behavior (only if on NavMesh)
                    if (agent.navigationAgent != null && agent.IsOnNavMesh)
                    {
                        agent.navigationAgent.IsStopped = false;
                    }
                    // Use force to leave ManualCommand state (Hold) so chase/aggro can resume
                    agent.ForceSetNavigationState(AgentNavigationState.Formation);
                    
                    // Allow aggro system to work normally
                    // If agent was in defensive mode and not engaged, allow them to seek targets
                    break;

                case CombatStance.Defensive:
                    // Defensive: Only attack if attacked
                    // Clear current aggro/target if not being attacked
                    if (!agent.wasRecentlyAttacked && Time.time - agent.lastDamagedByEnemyTime > 5f)
                    {
                        agent.isAggroed = false;
                        agent.isChasing = false;
                        agent.hasAttackTargetPosition = false;
                        agent.attackTargetAgentIndex = -1;
                        agent.attackTargetTransform = null;
                        
                        // Return to formation if was chasing
                        if (agent.navigationState == AgentNavigationState.ChasingTarget)
                        {
                            agent.SetNavigationState(AgentNavigationState.Formation);
                        }
                    }
                    // Clear hold position and ally defense ignore
                    agent.hasHoldPositionSet = false;
                    agent.ignoreAllyDefenseWhileHolding = false;

                    // Release manual/formation locks so defensive agents can still react when attacked
                    agent.hasManualCommand = false;
                    agent.manualCommandDestination = Vector3.zero;
                    agent.hasRTSFormationPosition = false;
                    agent.brokeFromRTSFormation = false;

                    if (agent.navigationAgent != null && agent.IsOnNavMesh)
                    {
                        agent.navigationAgent.IsStopped = false;
                    }
                    // Use force to leave ManualCommand state (Hold) so retaliation can work
                    agent.ForceSetNavigationState(AgentNavigationState.Formation);
                    break;

                case CombatStance.HoldPosition:
                    // HoldPosition: Maintain commanded destination/formation, attack in range only.
                    // Do NOT cancel current move; hold applies once they arrive (or immediately if already there).
                    agent.isChasing = false;
                    
                    // Reset attack cooldown so HoldPosition agents can attack immediately when enemies are in range
                    // This prevents them from being stuck waiting for old cooldowns from previous stances
                    if (agent.nextAttackTime > Time.time + 0.5f)
                    {
                        agent.nextAttackTime = Time.time + UnityEngine.Random.Range(0.1f, 0.5f);
                    }

                    if (agent.navigationAgent != null && agent.IsOnNavMesh)
                    {
                        Vector3 desiredHold = agent.manualCommandDestination;
                        bool hasPath = agent.navigationAgent.HasPath;

                        // If no manual destination recorded, fall back to current nav destination or position
                        if (agent.hasManualCommand)
                        {
                            desiredHold = agent.manualCommandDestination;
                        }
                        else if (hasPath)
                        {
                            desiredHold = agent.navigationAgent.Destination;
                        }
                        else
                        {
                            desiredHold = agent.WorldPosition;
                        }

                        float distToHold = Vector3.Distance(agent.WorldPosition, desiredHold);
                        bool atHold = distToHold <= agent.navigationAgent.StoppingDistance + 0.5f;

                        if (atHold)
                        {
                            // Already there: lock the position and stop
                            agent.holdPositionLocation = agent.WorldPosition;
                            agent.hasHoldPositionSet = true;
                            agent.hasManualCommand = false;
                            agent.manualCommandDestination = agent.holdPositionLocation;
                            agent.navigationAgent.ResetPath();
                            agent.navigationAgent.Velocity = Vector3.zero;
                            agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                        }
                        else
                        {
                            // Still moving: keep the path and mark a manual destination so Hold engages on arrival
                            agent.hasHoldPositionSet = false;
                            agent.hasManualCommand = true;
                            agent.manualCommandDestination = desiredHold;
                            agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);

                            // Ensure destination matches desired hold point
                            if (!hasPath || Vector3.Distance(agent.navigationAgent.Destination, desiredHold) > 0.1f)
                            {
                                agent.navigationAgent.SetDestination(desiredHold);
                            }
                        }
                    }
                    break;
            }

            if (enableDebugLogging && previousStance != stance)
                Debug.Log($"[RTS] Agent {agent.index}: Combat stance changed from {previousStance} to {stance}");
        }

        #endregion

        #region Selection Changed Handling

        private void OnSelectionChanged()
        {
            // Remove dead agents from selection
            var agents = crowdController.GetAgents();
            _selectedAgentIndices.RemoveAll(i => i < 0 || i >= agents.Count || agents[i] == null || agents[i].isDead);

            // Remove invalid external selections
            _selectedExternalSelectables.RemoveAll(sel => !IsExternalSelectableValid(sel, requireSelectable: false));

            int externalCount = _selectedExternalSelectables.Count;

            // Calculate what changed
            HashSet<int> currentSelection = new HashSet<int>(_selectedAgentIndices);
            
            // Find newly selected agents
            _eventData.newlySelected.Clear();
            foreach (int index in currentSelection)
            {
                if (!_previousSelection.Contains(index))
                {
                    _eventData.newlySelected.Add(index);
                }
            }
            
            // Find newly deselected agents
            _eventData.newlyDeselected.Clear();
            foreach (int index in _previousSelection)
            {
                if (!currentSelection.Contains(index))
                {
                    _eventData.newlyDeselected.Add(index);
                }
            }

            // Update event data
            _eventData.selectedIndices.Clear();
            _eventData.selectedIndices.AddRange(_selectedAgentIndices);
            _eventData.totalSelected = TotalSelectedCount;

            // Apply shader highlights to newly selected agents
            if (enableShaderHighlight)
            {
                foreach (int index in _eventData.newlySelected)
                {
                    ApplyShaderHighlight(index, true);
                }
                foreach (int index in _eventData.newlyDeselected)
                {
                    ApplyShaderHighlight(index, false);
                }
            }

            // Update indicator prefabs
            UpdateSelectionIndicators();

            // Update destination/direction indicators based on new selection
            OnSelectionChangedUpdateIndicators();

            // Fire C# events
            foreach (int index in _eventData.newlySelected)
            {
                CrowdAgent agent = (index >= 0 && index < agents.Count) ? agents[index] : null;
                AgentSelected?.Invoke(index, agent);
                onAgentSelected?.Invoke(index);
            }
            
            foreach (int index in _eventData.newlyDeselected)
            {
                CrowdAgent agent = (index >= 0 && index < agents.Count) ? agents[index] : null;
                AgentDeselected?.Invoke(index, agent);
                onAgentDeselected?.Invoke(index);
            }

            if (_eventData.totalSelected == 0 && (_previousSelection.Count > 0 || externalCount > 0 || _lastExternalSelectionCount > 0))
            {
                SelectionCleared?.Invoke();
                onSelectionCleared?.Invoke();
            }

            // Fire main selection changed event
            SelectionChanged?.Invoke(_eventData);
            onSelectionChanged?.Invoke(_eventData);
            
            // Play selection sound if new agents were selected
            if (_eventData.newlySelected.Count > 0 || _selectedExternalSelectables.Count > 0)
            {
                PlaySelectionSound(_eventData.totalSelected);
            }

            _lastExternalSelectionCount = externalCount;

            // Store current selection for next comparison
            _previousSelection.Clear();
            _previousSelection.UnionWith(currentSelection);
            
            // Update patrol path visualization based on new selection
            UpdatePatrolPathVisualizationForSelection();
        }

        private void ApplyShaderHighlight(int agentIndex, bool highlight)
        {
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count)
                return;

            CrowdAgent agent = agents[agentIndex];
            if (agent == null)
                return;

            // Try to get renderer from navigation agent transform or crowd instance
            Renderer renderer = null;
            var navTransform = agent.navigationAgent?.GetUnderlyingAgent<Component>()?.transform;
            if (navTransform != null)
            {
                renderer = navTransform.GetComponentInChildren<Renderer>();
            }
            if (renderer == null && agent.crowdInstance != null)
            {
                renderer = agent.crowdInstance.GetComponentInChildren<Renderer>();
            }

            if (renderer != null)
            {
                renderer.GetPropertyBlock(_propertyBlock);
                
                if (highlight)
                {
                    _propertyBlock.SetColor(_highlightColorId, shaderHighlightColor);
                    _propertyBlock.SetFloat(_highlightIntensityId, shaderHighlightIntensity);
                }
                else
                {
                    _propertyBlock.SetColor(_highlightColorId, Color.black);
                    _propertyBlock.SetFloat(_highlightIntensityId, 0f);
                }
                
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// Caches Canvas information if indicator parent is a UI element.
        /// Call this if you change indicatorParent at runtime.
        /// </summary>
        public void CacheIndicatorCanvasInfo()
        {
            _isUIIndicator = false;
            _indicatorCanvas = null;
            _indicatorCanvasRect = null;

            if (indicatorParent != null)
            {
                // Check if parent has a Canvas (direct or in parents)
                _indicatorCanvas = indicatorParent.GetComponentInParent<Canvas>();
                if (_indicatorCanvas != null)
                {
                    _isUIIndicator = true;
                    _indicatorCanvasRect = _indicatorCanvas.GetComponent<RectTransform>();
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS] UI indicator mode detected. Canvas: {_indicatorCanvas.name}, RenderMode: {_indicatorCanvas.renderMode}");
                }
            }
        }

        private void UpdateSelectionIndicators()
        {
            // Return all active indicators to pool or destroy if they use overrides
            foreach (var indicator in _activeIndicators)
            {
                if (indicator == null) continue;

                var indicatorComponent = indicator.GetComponent<SelectionIndicatorExample>();
                indicatorComponent?.OnHide();

                var tag = indicator.GetComponent<IndicatorInstanceTag>();
                bool canPool = selectionIndicatorPrefab != null && tag != null && tag.usesDefaultPrefab;

                if (canPool)
                {
                    indicator.SetActive(false);
                    _indicatorPool.Enqueue(indicator);
                }
                else
                {
                    Destroy(indicator);
                }
            }
            _activeIndicators.Clear();

            bool hasDefaultPrefab = selectionIndicatorPrefab != null;
            bool hasAnySelection = TotalSelectedCount > 0;
            bool hasOverridePrefab = _selectedExternalSelectables.Exists(sel => sel?.SelectionIndicatorOverride != null);
            if (!hasAnySelection || (!hasDefaultPrefab && !hasOverridePrefab))
                return;

            var agents = crowdController.GetAgents();
            var targets = new List<(GameObject prefab, CrowdAgent agent, IRTSExternalSelectable external)>();

            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead) continue;
                targets.Add((selectionIndicatorPrefab, agent, null));
            }

            foreach (var selectable in _selectedExternalSelectables)
            {
                if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                    continue;
                GameObject prefab = selectable.SelectionIndicatorOverride != null
                    ? selectable.SelectionIndicatorOverride
                    : selectionIndicatorPrefab;

                if (prefab == null) continue;
                targets.Add((prefab, null, selectable));
            }

            int indicatorsToSpawn = Mathf.Min(targets.Count, maxIndicatorPrefabs);

            for (int i = 0; i < indicatorsToSpawn; i++)
            {
                var target = targets[i];
                GameObject prefab = target.prefab ?? selectionIndicatorPrefab;
                if (prefab == null) continue;

                GameObject indicator;
                if (prefab == selectionIndicatorPrefab && _indicatorPool.Count > 0)
                {
                    indicator = _indicatorPool.Dequeue();
                    indicator.SetActive(true);
                }
                else
                {
                    indicator = Instantiate(prefab, indicatorParent);
                }

                var tag = indicator.GetComponent<IndicatorInstanceTag>() ?? indicator.AddComponent<IndicatorInstanceTag>();
                tag.usesDefaultPrefab = prefab == selectionIndicatorPrefab;

                if (target.agent != null)
                {
                    PositionIndicator(indicator, target.agent);
                }
                else if (target.external != null)
                {
                    PositionIndicator(indicator, target.external);
                }

                var indicatorComponent = indicator.GetComponent<SelectionIndicatorExample>();
                indicatorComponent?.OnShow();

                _activeIndicators.Add(indicator);
            }
        }

        /// <summary>
        /// Positions an indicator at an agent's location (handles both world-space and UI modes).
        /// For flying agents with <see cref="indicatorAtGroundForFlying"/> enabled, the indicator
        /// is placed at ground level. When <see cref="autoScaleIndicatorToAgent"/> is enabled,
        /// the indicator is scaled to match the agent's bounding radius.
        /// </summary>
        private void PositionIndicator(GameObject indicator, CrowdAgent agent)
        {
            Vector3 pos = GetAgentWorldPosition(agent);

            // Place indicator at ground level for Flight3D agents so it acts as a
            // projected shadow/silhouette rather than floating at altitude.
            // Legacy fly-offset agents (Fly Effect skill action) keep the indicator at their
            // visual altitude so the player can see and click the indicator where the mesh is.
            bool isFlight3D = agent.navigationAgent?.NavigationType == NavigationType.Flight3D;
            if (indicatorAtGroundForFlying && isFlight3D && agent.flightCurrentHeight > 0.1f)
            {
                pos.y -= agent.flightCurrentHeight;

                // Also remove the hover bob offset we added in GetAgentWorldPosition
                EnemyCrowd flightCrowd = agent.enemyCrowd;
                if (flightCrowd != null && flightCrowd.flightBobAmplitude > 0f &&
                    Mathf.Approximately(agent.flightCurrentHeight, agent.flightTargetHeight) && agent.flightTargetHeight > 0.01f)
                {
                    float phase = agent.index * 2.3917f;
                    pos.y -= Mathf.Sin(Time.time * flightCrowd.flightBobFrequency * 6.2831853f + phase)
                             * flightCrowd.flightBobAmplitude;
                }
            }

            PositionIndicator(indicator, pos);

            // Scale indicator to match agent bounding size
            if (autoScaleIndicatorToAgent && agent.enemyCrowd != null)
            {
                float radius;
                if (agent.enemyCrowd.IsNonHumanoid)
                {
                    // Vehicles use the designer-specified bounding radius
                    radius = agent.enemyCrowd.vehicleBoundingRadius;
                }
                else
                {
                    // Humanoids use the navigation agent radius (typically ~0.5)
                    radius = agent.navigationAgent?.Radius ?? 0.5f;
                }

                // Scale the indicator so that its diameter matches the agent's bounding diameter.
                // The default 1x1 prefab covers a 1-unit diameter, so scale = diameter.
                float diameter = radius * 2f;
                indicator.transform.localScale = new Vector3(diameter, diameter, diameter);
            }
        }

        private void PositionIndicator(GameObject indicator, IRTSExternalSelectable selectable)
        {
            if (selectable == null) return;
            Vector3 agentPos = GetExternalSelectableWorldPosition(selectable);
            PositionIndicator(indicator, agentPos + selectable.SelectionIndicatorOffset);
        }

        private void PositionIndicator(GameObject indicator, Vector3 agentPos)
        {
            if (_isUIIndicator)
            {
                // Check if agent is behind camera (for screen-space modes)
                if (_indicatorCanvas.renderMode != RenderMode.WorldSpace)
                {
                    Vector3 screenPos = _camera.WorldToScreenPoint(agentPos + indicatorOffset);
                    if (screenPos.z < 0)
                    {
                        indicator.SetActive(false);
                        return;
                    }
                }
                indicator.SetActive(true);

                RectTransform rectTransform = indicator.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    if (_indicatorCanvas.renderMode == RenderMode.WorldSpace)
                    {
                        // World Space Canvas: position directly in world coordinates (like regular 3D objects)
                        indicator.transform.position = agentPos + indicatorOffset;
                    }
                    else if (_indicatorCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        // Screen Space - Overlay: use RectTransformUtility for proper CanvasScaler support
                        // This keeps indicator at fixed pixel size regardless of camera zoom
                        Vector3 screenPos = _camera.WorldToScreenPoint(agentPos + indicatorOffset);
                        Vector2 localPoint;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _indicatorCanvasRect,
                            new Vector2(screenPos.x + uiIndicatorScreenOffset.x, screenPos.y + uiIndicatorScreenOffset.y),
                            null,  // null camera for Screen Space Overlay
                            out localPoint
                        );
                        rectTransform.anchoredPosition = localPoint;
                    }
                    else if (_indicatorCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                    {
                        // Screen Space - Camera: use RectTransformUtility
                        Vector3 screenPos = _camera.WorldToScreenPoint(agentPos + indicatorOffset);
                        Vector2 localPoint;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _indicatorCanvasRect,
                            new Vector2(screenPos.x + uiIndicatorScreenOffset.x, screenPos.y + uiIndicatorScreenOffset.y),
                            _indicatorCanvas.worldCamera,
                            out localPoint
                        );
                        rectTransform.anchoredPosition = localPoint;
                    }
                }
            }
            else
            {
                // World-space mode (no Canvas): position directly
                indicator.transform.position = agentPos + indicatorOffset;
            }
        }

        /// <summary>
        /// Updates positions of all active indicators. Called automatically if autoUpdateIndicatorPositions is true.
        /// </summary>
        private void UpdateIndicatorPositions()
        {
            if (_activeIndicators.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            int indicatorIndex = 0;

            // Update indicators for internal crowd agents
            for (int i = 0; i < _selectedAgentIndices.Count && indicatorIndex < _activeIndicators.Count; i++, indicatorIndex++)
            {
                GameObject indicator = _activeIndicators[indicatorIndex];
                if (indicator == null)
                    continue;

                int agentIndex = _selectedAgentIndices[i];
                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    indicator.SetActive(false);
                    continue;
                }

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                {
                    indicator.SetActive(false);
                    continue;
                }

                PositionIndicator(indicator, agent);
            }

            // Update indicators for external selectables (e.g., GC2 player)
            for (int i = 0; i < _selectedExternalSelectables.Count && indicatorIndex < _activeIndicators.Count; i++, indicatorIndex++)
            {
                GameObject indicator = _activeIndicators[indicatorIndex];
                if (indicator == null)
                    continue;

                IRTSExternalSelectable selectable = _selectedExternalSelectables[i];
                if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                {
                    indicator.SetActive(false);
                    continue;
                }

                PositionIndicator(indicator, selectable);
            }
        }

        /// <summary>
        /// Manually refresh selection indicators (call if agents move and you want indicators to follow).
        /// Note: If autoUpdateIndicatorPositions is true, this is called automatically.
        /// </summary>
        public void RefreshSelectionIndicators()
        {
            UpdateIndicatorPositions();
        }

        #endregion

        #region Attack Target Feedback

        /// <summary>
        /// Shows attack target feedback on the specified agent.
        /// </summary>
        private void ShowAttackTargetFeedback(int targetAgentIndex)
        {
            if (!enableAttackTargetFeedback)
                return;

            // Clear previous target feedback if different target
            if (_currentAttackTargetIndex >= 0 && _currentAttackTargetIndex != targetAgentIndex)
            {
                ClearAttackTargetFeedback();
            }

            _currentAttackTargetIndex = targetAgentIndex;
            _attackTargetFeedbackStartTime = Time.time;
            _attackTargetFeedbackActive = true;

            var agents = crowdController.GetAgents();
            if (targetAgentIndex < 0 || targetAgentIndex >= agents.Count)
                return;

            CrowdAgent targetAgent = agents[targetAgentIndex];
            if (targetAgent == null || targetAgent.isDead)
                return;

            // Apply shader highlight
            if (enableAttackTargetShaderHighlight)
            {
                ApplyAttackTargetShaderHighlight(targetAgentIndex, true, 1f);
            }

            // Spawn indicator prefab
            if (attackTargetIndicatorPrefab != null)
            {
                if (_attackTargetIndicatorInstance == null)
                {
                    Transform parent = attackTargetIndicatorParent != null ? attackTargetIndicatorParent : transform;
                    _attackTargetIndicatorInstance = Instantiate(attackTargetIndicatorPrefab, parent);
                }
                
                _attackTargetIndicatorInstance.SetActive(true);
                PositionAttackTargetIndicator(targetAgent);
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS Attack Feedback] Showing feedback on target {targetAgentIndex}, mode={attackTargetFeedbackMode}");
        }

        /// <summary>
        /// Clears attack target feedback from the current target.
        /// </summary>
        public void ClearAttackTargetFeedback()
        {
            if (_currentAttackTargetIndex >= 0 && enableAttackTargetShaderHighlight)
            {
                ApplyAttackTargetShaderHighlight(_currentAttackTargetIndex, false, 0f);
            }

            if (_attackTargetIndicatorInstance != null)
            {
                _attackTargetIndicatorInstance.SetActive(false);
            }

            _currentAttackTargetIndex = -1;
            _attackTargetFeedbackActive = false;

            if (enableDebugLogging)
                Debug.Log("[RTS Attack Feedback] Cleared attack target feedback");
        }

        /// <summary>
        /// Updates attack target feedback (handles flashing, fading, and position updates).
        /// Called from Update().
        /// </summary>
        private void UpdateAttackTargetFeedback()
        {
            if (!_attackTargetFeedbackActive || _currentAttackTargetIndex < 0)
                return;

            var agents = crowdController.GetAgents();
            if (_currentAttackTargetIndex >= agents.Count)
            {
                ClearAttackTargetFeedback();
                return;
            }

            CrowdAgent targetAgent = agents[_currentAttackTargetIndex];
            
            // Clear if target died
            if (targetAgent == null || targetAgent.isDead)
            {
                if (clearFeedbackOnTargetDeath)
                {
                    ClearAttackTargetFeedback();
                    return;
                }
            }

            float elapsed = Time.time - _attackTargetFeedbackStartTime;

            switch (attackTargetFeedbackMode)
            {
                case AttackTargetFeedbackMode.Persistent:
                    // Just update position, no fading
                    if (autoUpdateAttackTargetPosition && _attackTargetIndicatorInstance != null && targetAgent != null)
                    {
                        PositionAttackTargetIndicator(targetAgent);
                    }
                    break;

                case AttackTargetFeedbackMode.FlashAndFade:
                    float normalizedTime = elapsed / attackTargetFeedbackDuration;
                    if (normalizedTime >= 1f)
                    {
                        ClearAttackTargetFeedback();
                        return;
                    }

                    // Flash effect: sine wave pulses that fade out over time
                    float flashPhase = elapsed * attackTargetFlashSpeed;
                    float flashPulse = (Mathf.Sin(flashPhase * Mathf.PI * 2f) + 1f) * 0.5f; // 0-1 pulse
                    float fadeOut = 1f - normalizedTime;
                    float intensity = flashPulse * fadeOut;

                    if (enableAttackTargetShaderHighlight)
                    {
                        ApplyAttackTargetShaderHighlight(_currentAttackTargetIndex, true, intensity);
                    }

                    // Fade indicator alpha if it has a CanvasGroup or SpriteRenderer
                    if (_attackTargetIndicatorInstance != null)
                    {
                        SetIndicatorAlpha(_attackTargetIndicatorInstance, fadeOut);
                        if (autoUpdateAttackTargetPosition && targetAgent != null)
                        {
                            PositionAttackTargetIndicator(targetAgent);
                        }
                    }
                    break;

                case AttackTargetFeedbackMode.FlashOnly:
                    if (elapsed >= attackTargetFeedbackDuration * 0.3f) // Quick flash
                    {
                        ClearAttackTargetFeedback();
                        return;
                    }

                    // Quick flash pulse
                    float quickFlash = Mathf.Sin(elapsed * attackTargetFlashSpeed * Mathf.PI * 2f);
                    quickFlash = (quickFlash + 1f) * 0.5f;

                    if (enableAttackTargetShaderHighlight)
                    {
                        ApplyAttackTargetShaderHighlight(_currentAttackTargetIndex, true, quickFlash);
                    }
                    break;
            }
        }

        /// <summary>
        /// Applies shader highlight to an attack target.
        /// </summary>
        private void ApplyAttackTargetShaderHighlight(int agentIndex, bool highlight, float intensityMultiplier)
        {
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count)
                return;

            CrowdAgent agent = agents[agentIndex];
            if (agent == null)
                return;

            // Try to get renderer from navigation agent transform or crowd instance
            Renderer renderer = null;
            var navTransform = agent.navigationAgent?.GetUnderlyingAgent<Component>()?.transform;
            if (navTransform != null)
            {
                renderer = navTransform.GetComponentInChildren<Renderer>();
            }
            if (renderer == null && agent.crowdInstance != null)
            {
                renderer = agent.crowdInstance.GetComponentInChildren<Renderer>();
            }

            if (renderer != null)
            {
                renderer.GetPropertyBlock(_attackTargetPropertyBlock);
                
                if (highlight)
                {
                    _attackTargetPropertyBlock.SetColor(_attackTargetColorId, attackTargetHighlightColor);
                    _attackTargetPropertyBlock.SetFloat(_attackTargetIntensityId, attackTargetHighlightIntensity * intensityMultiplier);
                }
                else
                {
                    _attackTargetPropertyBlock.SetColor(_attackTargetColorId, Color.black);
                    _attackTargetPropertyBlock.SetFloat(_attackTargetIntensityId, 0f);
                }
                
                renderer.SetPropertyBlock(_attackTargetPropertyBlock);
            }
        }

        /// <summary>
        /// Positions the attack target indicator on the target agent.
        /// </summary>
        private void PositionAttackTargetIndicator(CrowdAgent agent)
        {
            if (_attackTargetIndicatorInstance == null || agent == null)
                return;

            Vector3 targetPos = GetAgentWorldPosition(agent);
            _attackTargetIndicatorInstance.transform.position = targetPos + attackTargetIndicatorOffset;
        }

        /// <summary>
        /// Sets the alpha of an indicator (supports CanvasGroup, SpriteRenderer, and Renderer materials).
        /// </summary>
        private void SetIndicatorAlpha(GameObject indicator, float alpha)
        {
            // Try CanvasGroup first (UI indicators)
            var canvasGroup = indicator.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
                return;
            }

            // Try SpriteRenderer
            var spriteRenderer = indicator.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                Color c = spriteRenderer.color;
                c.a = alpha;
                spriteRenderer.color = c;
                return;
            }

            // Try Renderer with material
            var renderer = indicator.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                Color c = renderer.material.color;
                c.a = alpha;
                renderer.material.color = c;
            }
        }

        /// <summary>
        /// Gets the current attack target index. Returns -1 if no target.
        /// </summary>
        public int GetCurrentAttackTarget()
        {
            return _currentAttackTargetIndex;
        }

        /// <summary>
        /// Returns true if attack target feedback is currently active.
        /// </summary>
        public bool IsAttackTargetFeedbackActive()
        {
            return _attackTargetFeedbackActive;
        }

        #endregion

        #region Move Commands

        /// <summary>
        /// Processes move command input with optional drag-to-adjust spacing and rotation.
        /// When enableDragToAdjustSpacing is true:
        /// - Press command button: Start tracking drag position
        /// - Hold command button + Drag horizontally: Adjust formation spacing preview
        /// - Hold command button + Hold selection button: Lock spacing and adjust rotation instead
        /// - Release command button: Execute move command with adjusted spacing/rotation, then reset
        /// When disabled, executes move command immediately on press.
        /// 
        /// When waypoint queue modifier is held:
        /// - Click adds waypoints to a patrol path instead of immediately moving
        /// - If final waypoint is near the first, creates a patrol loop
        /// </summary>
        private void ProcessMoveCommand()
        {
            if (suppressCommandInput || !enableMoveCommands || TotalSelectedCount == 0)
            {
                // Cancel any in-progress adjustment if selection is cleared
                if (_isAdjustingSpacing || _isAdjustingRotation)
                {
                    CancelSpacingAdjustment();
                }
                // Cancel waypoint queuing if selection is cleared
                if (_isQueuingWaypoints)
                {
                    CancelWaypointQueue();
                }
                return;
            }

            // Simple mode: execute immediately on press (original behavior)
            if (!enableDragToAdjustSpacing)
            {
                if (_commandButtonDown)
                {
                    // Check for waypoint queue modifier
                    if (_waypointQueueModifierHeld)
                    {
                        QueueWaypointAtMousePosition();
                    }
                    else
                    {
                        // Normal move command - clears any existing patrol
                        ClearPatrolPathsForSelection();
                        PerformMoveCommand();
                    }
                }
                return;
            }

            // Drag-to-adjust mode
            if (_commandButtonDown)
            {
                // Start tracking for potential spacing adjustment
                _spacingDragStartPosition = _mousePosition;
                _isAdjustingSpacing = false;
                _isAdjustingRotation = false;
                _commandPressTime = Time.time;
                
                // Get initial spacing - use faction override if enabled and available
                float initialSpacing = formationSpacing;
                if (persistSpacingPerFaction)
                {
                    string dominantFaction = GetDominantFactionFromSelection();
                    if (!string.IsNullOrEmpty(dominantFaction) && _factionSpacingOverrides.TryGetValue(dominantFaction, out float factionSpacing))
                    {
                        initialSpacing = factionSpacing;
                    }
                    else if (_selectedAgentIndices.Count == 0 && _selectedExternalSelectables.Count > 0 && _externalSpacingOverride > 0f)
                    {
                        initialSpacing = _externalSpacingOverride;
                    }
                }
                _temporarySpacing = initialSpacing;
                _temporaryRotation = 0f; // No rotation by default

                // Battle Line: detect if we should adjust group gap instead of formation spacing
                _isDraggingBattleLineGap = formationControlMode == FormationControlMode.BattleLine
                                           && HasMultipleCrowdTypesSelected();
                _temporaryBattleLineGap = battleLineGroupGap;

                // Per-crowd spacing: detect if we should adjust per-crowd spacing
                _isDraggingPerCrowdSpacing = false;
                _draggingCrowdIndex = -1;
                if (formationSpacingMode == FormationSpacingMode.PerCrowd && !_isDraggingBattleLineGap)
                {
                    int singleCrowd = GetSingleSelectedCrowdIndex();
                    if (singleCrowd >= 0)
                    {
                        _isDraggingPerCrowdSpacing = true;
                        _draggingCrowdIndex = singleCrowd;
                        float crowdSpacing = GetEffectiveCrowdSpacing(singleCrowd);
                        _temporaryPerCrowdSpacing = crowdSpacing;
                        _temporarySpacing = crowdSpacing; // so preview uses per-crowd value
                    }
                }
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Spacing] Command button pressed, tracking drag from {_spacingDragStartPosition}, initial spacing={initialSpacing:F2}, battleLineGap={_isDraggingBattleLineGap}, perCrowd={_isDraggingPerCrowdSpacing}");
            }
            else if (_commandButtonHeld)
            {
                // Skip formation adjustment until the delay has elapsed
                if (Time.time - _commandPressTime < formationAdjustDelay)
                    return;

                // Cycle formations while holding command
                if (enableFormationCycling)
                {
                    int dir = 0;
                    bool usedScrollForFormation = false;

                    if (useMouseScrollWheelForFormationCycle && Mathf.Abs(_scrollDeltaY) > 0.01f)
                    {
                        dir = _scrollDeltaY > 0f ? 1 : -1;
                        usedScrollForFormation = true;
                    }

                    if (Mathf.Abs(_formationCycleAxis) > 0.01f)
                    {
                        dir = _formationCycleAxis > 0f ? 1 : -1;
                    }

                    if (_formationCycleUpPressed) dir += 1;
                    if (_formationCycleDownPressed) dir -= 1;

                    if (usedScrollForFormation && _rtsCameraController != null)
                    {
                        _rtsCameraController.SuppressZoom(formationCycleCooldown + 0.05f);
                    }

                    if (dir != 0 && Time.time >= _nextFormationCycleTime)
                    {
                        CycleFormation(dir);
                        _nextFormationCycleTime = Time.time + formationCycleCooldown;
                    }
                }

                // Check for rotation adjustment mode: selection button pressed while adjusting spacing
                // OR selection button already held when spacing adjustment started
                bool selectionButtonActive = _selectionButtonDown || IsSelectionButtonHeld();
                
                if (enableRotationAdjustment && _isAdjustingSpacing && !_isAdjustingRotation && selectionButtonActive)
                {
                    // Enter rotation adjustment mode - lock spacing
                    _isAdjustingRotation = true;
                    _lockedSpacing = _temporarySpacing;
                    _rotationDragStartPosition = _mousePosition;
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Rotation] Entered rotation mode, spacing locked at {_lockedSpacing:F2}");
                }
                
                // Handle rotation adjustment
                if (_isAdjustingRotation)
                {
                    if (_selectionButtonUp || !IsSelectionButtonHeld())
                    {
                        // Exit rotation mode back to spacing mode (without clearing rotation)
                        _isAdjustingRotation = false;
                        HideRotationMarker();
                        
                        if (enableDebugLogging)
                            Debug.Log($"[RTS Rotation] Exited rotation mode, rotation={_temporaryRotation:F1}°");
                    }
                    else
                    {
                        // Adjust rotation based on horizontal drag from rotation start position
                        float horizontalDelta = _mousePosition.x - _rotationDragStartPosition.x;
                        _temporaryRotation = horizontalDelta * rotationDragSensitivity;
                        
                        // Update preview with rotation
                        UpdateFormationPreview();
                        UpdateRotationMarker();
                        
                        if (enableDebugLogging && Time.frameCount % 10 == 0)
                            Debug.Log($"[RTS Rotation] Adjusting: delta={horizontalDelta:F0}px, rotation={_temporaryRotation:F1}°");
                    }
                }
                else
                {
                    // Standard spacing adjustment
                    float horizontalDelta = _mousePosition.x - _spacingDragStartPosition.x;
                    
                    if (!_isAdjustingSpacing && Mathf.Abs(horizontalDelta) > spacingDragThreshold)
                    {
                        // Start adjusting spacing (or battle line gap or per-crowd spacing)
                        _isAdjustingSpacing = true;
                        _baseFormationSpacing = formationSpacing; // Store original value
                        _baseBattleLineGroupGap = battleLineGroupGap;
                        if (_isDraggingPerCrowdSpacing)
                            _basePerCrowdSpacing = _temporaryPerCrowdSpacing;
                        
                        if (enableDebugLogging)
                            Debug.Log($"[RTS Spacing] Started adjusting {(_isDraggingBattleLineGap ? "battle line gap" : _isDraggingPerCrowdSpacing ? $"per-crowd spacing (crowd {_draggingCrowdIndex})" : "spacing")} (drag threshold exceeded)");
                    }
                    
                    if (_isAdjustingSpacing)
                    {
                        float spacingDelta = horizontalDelta * spacingDragSensitivity;

                        if (_isDraggingBattleLineGap)
                        {
                            // Adjust group gap between EnemyCrowd blocks
                            _temporaryBattleLineGap = Mathf.Clamp(_baseBattleLineGroupGap + spacingDelta * 3f, 0.5f, 30f);
                        }
                        else if (_isDraggingPerCrowdSpacing)
                        {
                            // Adjust spacing for the single selected crowd only
                            _temporaryPerCrowdSpacing = Mathf.Clamp(_basePerCrowdSpacing + spacingDelta, minDragSpacing, maxDragSpacing);
                            _temporarySpacing = _temporaryPerCrowdSpacing; // feed into preview
                        }
                        else
                        {
                            // Adjust formation spacing (original behaviour)
                            _temporarySpacing = Mathf.Clamp(_baseFormationSpacing + spacingDelta, minDragSpacing, maxDragSpacing);
                        }
                        
                        // Update formation preview
                        UpdateFormationPreview();
                        
                        if (enableDebugLogging && Time.frameCount % 10 == 0)
                            Debug.Log($"[RTS Spacing] Adjusting: delta={horizontalDelta:F0}px, spacing={_temporarySpacing:F2}");
                    }
                }
            }
            else if (_commandButtonUp)
            {
                // Hide formation preview and rotation marker
                HideFormationPreview();
                HideRotationMarker();
                
                if (_isAdjustingSpacing || _isAdjustingRotation)
                {
                    // Use locked spacing if we were in rotation mode
                    float finalSpacing = _isAdjustingRotation ? _lockedSpacing : _temporarySpacing;
                    
                    // Persist battle line gap if user was dragging the gap between groups
                    if (_isDraggingBattleLineGap)
                    {
                        battleLineGroupGap = _temporaryBattleLineGap;
                    }

                    // Persist per-crowd spacing if user was dragging per-crowd
                    if (_isDraggingPerCrowdSpacing && _draggingCrowdIndex >= 0)
                    {
                        _crowdSpacingOverrides[_draggingCrowdIndex] = _temporaryPerCrowdSpacing;
                    }
                    
                    // Persist spacing per-faction if enabled (skip when doing per-crowd override —
                    // per-crowd spacing is stored in _crowdSpacingOverrides, not in faction overrides)
                    if (persistSpacingPerFaction && !_isDraggingPerCrowdSpacing)
                    {
                        if (_selectedAgentIndices.Count > 0)
                        {
                            SaveSpacingForSelectedFactions(finalSpacing);
                        }
                        else if (_selectedExternalSelectables.Count > 0)
                        {
                            _externalSpacingOverride = finalSpacing;
                        }
                    }
                    
                    // Execute move with adjusted spacing and rotation at the ORIGINAL click position.
                    // When dragging per-crowd spacing, do NOT overwrite formationSpacing — the per-crowd
                    // value is already persisted in _crowdSpacingOverrides and GetEffectiveCrowdSpacing
                    // reads it from there. Overwriting formationSpacing would contaminate other crowds.
                    float originalSpacing = formationSpacing;
                    if (!_isDraggingPerCrowdSpacing)
                        formationSpacing = finalSpacing;
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Command] Executing move with spacing: {finalSpacing:F2}, rotation: {_temporaryRotation:F1}° at locked position");
                    
                    // Check for waypoint queue modifier
                    if (_waypointQueueModifierHeld)
                    {
                        QueueWaypointAtScreenPosition(_spacingDragStartPosition);
                    }
                    else
                    {
                        // Normal move command - clears any existing patrol
                        ClearPatrolPathsForSelection();
                        // Use the position where command button was first pressed, not current mouse position
                        PerformMoveCommandAtPositionWithRotation(_spacingDragStartPosition, _temporaryRotation);
                    }
                    
                    // Reset to original spacing for next command
                    formationSpacing = originalSpacing;
                    _isAdjustingSpacing = false;
                    _isAdjustingRotation = false;
                    _isDraggingBattleLineGap = false;
                    _isDraggingPerCrowdSpacing = false;
                    _draggingCrowdIndex = -1;
                    _temporaryRotation = 0f;
                }
                else
                {
                    // Simple click (no drag) - use persisted faction spacing if enabled
                    float clickSpacing = formationSpacing;
                    if (persistSpacingPerFaction)
                    {
                        string dominantFaction = GetDominantFactionFromSelection();
                        if (!string.IsNullOrEmpty(dominantFaction) && _factionSpacingOverrides.TryGetValue(dominantFaction, out float factionSpacing))
                        {
                            clickSpacing = factionSpacing;
                        }
                        else if (_selectedAgentIndices.Count == 0 && _selectedExternalSelectables.Count > 0 && _externalSpacingOverride > 0f)
                        {
                            clickSpacing = _externalSpacingOverride;
                        }
                    }
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Spacing] Simple click, executing move with spacing: {clickSpacing:F2} (persisted={clickSpacing != formationSpacing})");
                    
                    // Temporarily apply the faction spacing for this command
                    float originalSpacing = formationSpacing;
                    formationSpacing = clickSpacing;
                    
                    // Check for waypoint queue modifier
                    if (_waypointQueueModifierHeld)
                    {
                        QueueWaypointAtMousePosition();
                    }
                    else
                    {
                        // Normal move command - clears any existing patrol
                        ClearPatrolPathsForSelection();
                        PerformMoveCommandAtPosition(_mousePosition);
                    }
                    
                    formationSpacing = originalSpacing;
                }
            }
        }

        /// <summary>
        /// Cancels any in-progress spacing/rotation adjustment without executing the move command.
        /// </summary>
        private void CancelSpacingAdjustment()
        {
            _isAdjustingSpacing = false;
            _isAdjustingRotation = false;
            _isDraggingBattleLineGap = false;
            _isDraggingPerCrowdSpacing = false;
            _draggingCrowdIndex = -1;
            _temporarySpacing = formationSpacing;
            _temporaryBattleLineGap = battleLineGroupGap;
            _temporaryPerCrowdSpacing = formationSpacing;
            _temporaryRotation = 0f;
            HideFormationPreview();
            HideRotationMarker();
            
            if (enableDebugLogging)
                Debug.Log("[RTS Spacing] Spacing/rotation adjustment cancelled");
        }

        private void CycleFormation(int direction)
        {
            if (!enableFormationCycling)
                return;

            if (_formationCycleOrder == null || _formationCycleOrder.Length == 0)
                _formationCycleOrder = DefaultFormationCycleOrder;

            // In Battle Line mode with a single crowd type selected, cycle that crowd's formation
            if (formationControlMode == FormationControlMode.BattleLine && !HasMultipleCrowdTypesSelected())
            {
                int crowdIndex = GetSingleSelectedCrowdIndex();
                if (crowdIndex >= 0)
                {
                    // Determine current formation for this crowd
                    FormationType currentFormation = GetEffectiveCrowdFormation(crowdIndex);

                    int currentIdx = System.Array.IndexOf(_formationCycleOrder, currentFormation);
                    if (currentIdx < 0) currentIdx = 0;

                    int nextIdx = currentIdx + direction;
                    int cnt = _formationCycleOrder.Length;
                    nextIdx = ((nextIdx % cnt) + cnt) % cnt;

                    _crowdFormationOverrides[crowdIndex] = _formationCycleOrder[nextIdx];

                    // Ensure preview anchor exists
                    if (_spacingDragStartPosition == Vector2.zero)
                        _spacingDragStartPosition = _mousePosition;

                    UpdateFormationPreview();

                    if (enableDebugLogging)
                    {
                        string crowdName = (crowdIndex >= 0 && crowdIndex < crowdController.EnemyCrowds.Count)
                            ? crowdController.EnemyCrowds[crowdIndex]?.enemyTypeName ?? crowdIndex.ToString()
                            : crowdIndex.ToString();
                        Debug.Log($"[RTS Formation] Cycled crowd '{crowdName}' formation to {_formationCycleOrder[nextIdx]} (dir={direction})");
                    }
                    return;
                }
            }

            // Default: cycle global moveFormation
            int currentIndex = System.Array.IndexOf(_formationCycleOrder, moveFormation);
            if (currentIndex < 0)
                currentIndex = 0;

            int nextIndex = currentIndex + direction;
            int count = _formationCycleOrder.Length;
            nextIndex = ((nextIndex % count) + count) % count;

            moveFormation = _formationCycleOrder[nextIndex];

            // Ensure preview anchor exists for on-the-fly preview updates
            if (_spacingDragStartPosition == Vector2.zero)
                _spacingDragStartPosition = _mousePosition;

            UpdateFormationPreview();

            if (enableDebugLogging)
                Debug.Log($"[RTS Formation] Cycled formation to {moveFormation} (dir={direction})");
        }

        /// <summary>
        /// Returns the single enemyCrowdIndex present in the current selection,
        /// or -1 if the selection is empty or contains multiple crowd types.
        /// </summary>
        private int GetSingleSelectedCrowdIndex()
        {
            var agents = crowdController?.GetAgents();
            if (agents == null) return -1;

            int result = -1;
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead) continue;
                if (result < 0)
                    result = agent.enemyCrowdIndex;
                else if (agent.enemyCrowdIndex != result)
                    return -1; // multiple crowd types
            }
            return result;
        }

        /// <summary>
        /// Gets the effective formation type for a given crowd index, considering runtime overrides.
        /// Priority: runtime override > SO overrideFormation > SO formationType > moveFormation fallback.
        /// </summary>
        private FormationType GetEffectiveCrowdFormation(int crowdIndex)
        {
            // Check runtime override first
            if (_crowdFormationOverrides.TryGetValue(crowdIndex, out FormationType runtimeOverride))
                return runtimeOverride;

            // Then check ScriptableObject fields
            if (crowdIndex >= 0 && crowdIndex < crowdController.EnemyCrowds.Count)
            {
                EnemyCrowd crowd = crowdController.EnemyCrowds[crowdIndex];
                if (crowd != null && crowd.overrideFormation)
                    return crowd.formationTypeOverride;
                if (crowd != null)
                    return crowd.formationType;
            }

            return moveFormation;
        }

        /// <summary>
        /// Gets the effective formation spacing for a specific crowd index.
        /// Priority: active drag value > per-crowd override > global formationSpacing.
        /// </summary>
        private float GetEffectiveCrowdSpacing(int crowdIndex)
        {
            if (formationSpacingMode == FormationSpacingMode.PerCrowd)
            {
                // During an active drag, the temporary value takes precedence for the crowd being adjusted
                if (_isDraggingPerCrowdSpacing && crowdIndex == _draggingCrowdIndex)
                    return _temporaryPerCrowdSpacing;

                if (_crowdSpacingOverrides.TryGetValue(crowdIndex, out float spacingOverride))
                    return spacingOverride;
            }

            return formationSpacing;
        }

        /// <summary>
        /// Gets the current spacing value (temporary if adjusting, otherwise the base value).
        /// Useful for UI display of the current spacing.
        /// </summary>
        public float GetCurrentFormationSpacing()
        {
            return _isAdjustingSpacing ? _temporarySpacing : formationSpacing;
        }

        /// <summary>
        /// Gets the current formation rotation adjustment in degrees.
        /// Returns 0 if not adjusting rotation.
        /// </summary>
        public float GetCurrentFormationRotation()
        {
            return (_isAdjustingSpacing || _isAdjustingRotation) ? _temporaryRotation : 0f;
        }

        /// <summary>
        /// Returns true if the user is currently in the middle of a drag-to-adjust spacing operation.
        /// Useful for UI to show a spacing preview.
        /// </summary>
        public bool IsAdjustingFormationSpacing => _isAdjustingSpacing;

        /// <summary>
        /// Returns true if the user is currently adjusting formation rotation.
        /// </summary>
        public bool IsAdjustingFormationRotation => _isAdjustingRotation;

        /// <summary>
        /// Gets the dominant faction (most common EnemyCrowd) from the current selection.
        /// Used for per-faction spacing persistence.
        /// </summary>
        private string GetDominantFactionFromSelection()
        {
            if (_selectedAgentIndices.Count == 0)
                return null;

            var agents = crowdController.GetAgents();
            Dictionary<string, int> factionCounts = new Dictionary<string, int>();

            foreach (int index in _selectedAgentIndices)
            {
                if (index < 0 || index >= agents.Count)
                    continue;

                CrowdAgent agent = agents[index];
                if (agent?.enemyCrowd == null)
                    continue;

                string factionName = agent.enemyCrowd.factionName;
                if (string.IsNullOrEmpty(factionName))
                    continue;

                if (!factionCounts.ContainsKey(factionName))
                    factionCounts[factionName] = 0;
                factionCounts[factionName]++;
            }

            // Find the faction with the most agents
            string dominantFaction = null;
            int maxCount = 0;
            foreach (var kvp in factionCounts)
            {
                if (kvp.Value > maxCount)
                {
                    maxCount = kvp.Value;
                    dominantFaction = kvp.Key;
                }
            }

            return dominantFaction;
        }

        /// <summary>
        /// Saves the formation spacing for all factions in the current selection.
        /// </summary>
        private void SaveSpacingForSelectedFactions(float spacing)
        {
            if (_selectedAgentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            HashSet<string> factionsToUpdate = new HashSet<string>();

            // Collect all unique factions in selection
            foreach (int index in _selectedAgentIndices)
            {
                if (index < 0 || index >= agents.Count)
                    continue;

                CrowdAgent agent = agents[index];
                if (agent?.enemyCrowd == null)
                    continue;

                string factionName = agent.enemyCrowd.factionName;
                if (!string.IsNullOrEmpty(factionName))
                    factionsToUpdate.Add(factionName);
            }

            // Update spacing for all factions
            foreach (string faction in factionsToUpdate)
            {
                _factionSpacingOverrides[faction] = spacing;
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Spacing] Saved spacing {spacing:F2} for faction '{faction}'");
            }
        }

        /// <summary>
        /// Gets the stored formation spacing for a specific faction.
        /// Returns the default formationSpacing if no override exists.
        /// </summary>
        public float GetFactionSpacing(string factionName)
        {
            if (string.IsNullOrEmpty(factionName))
                return formationSpacing;

            return _factionSpacingOverrides.TryGetValue(factionName, out float spacing) ? spacing : formationSpacing;
        }

        /// <summary>
        /// Sets the formation spacing for a specific faction.
        /// Persists during the play session.
        /// </summary>
        public void SetFactionSpacing(string factionName, float spacing)
        {
            if (string.IsNullOrEmpty(factionName))
                return;

            _factionSpacingOverrides[factionName] = Mathf.Clamp(spacing, minDragSpacing, maxDragSpacing);
        }

        /// <summary>
        /// Clears all per-faction spacing overrides, reverting to default spacing.
        /// </summary>
        public void ClearFactionSpacingOverrides()
        {
            _factionSpacingOverrides.Clear();
            
            if (enableDebugLogging)
                Debug.Log("[RTS Spacing] Cleared all per-faction spacing overrides");
        }

        /// <summary>
        /// Gets all faction spacing overrides as a dictionary.
        /// Useful for UI display or saving/loading.
        /// </summary>
        public IReadOnlyDictionary<string, float> GetAllFactionSpacingOverrides()
        {
            return _factionSpacingOverrides;
        }

        #region Formation Preview

        /// <summary>
        /// Updates the formation preview showing ghost markers at calculated positions.
        /// Called each frame while adjusting spacing or rotation.
        /// </summary>
        private void UpdateFormationPreview()
        {
            if (!showFormationPreview)
            {
                HideFormationPreview();
                return;
            }

            // Raycast to get destination position on ground
            // Use the LOCKED position where command button was first pressed, not current mouse position
            Ray ray = _camera.ScreenPointToRay(_spacingDragStartPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundMask))
            {
                if (TryGetCommandDestination(hit.point, out Vector3 previewCenter))
                {
                    _previewCenter = previewCenter;
                    _hasValidPreviewPosition = true;
                }
                else
                {
                    _hasValidPreviewPosition = false;
                }
            }
            else
            {
                _hasValidPreviewPosition = false;
            }

            if (!_hasValidPreviewPosition)
            {
                HideFormationPreview();
                return;
            }

            // Ensure parent transform exists
            if (_previewMarkerParent == null)
            {
                var parentGO = new GameObject("FormationPreviewMarkers");
                _previewMarkerParent = parentGO.transform;
            }

            // Calculate how many markers to show (limited by maxFormationPreviewMarkers)
            int totalAgents = TotalSelectedCount;
            int markersToShow = Mathf.Min(totalAgents, maxFormationPreviewMarkers);

            // Keep visual gaps reasonable relative to current spacing (middle ground for hollow outlines)
            // We cap the skip stride so consecutive preview markers are no more than ~3 * spacing apart.
            const float MaxPreviewGapMultiplier = 3f;
            float maxPreviewGap = Mathf.Max(0.0001f, formationSpacing * MaxPreviewGapMultiplier);
            int maxStrideByGap = Mathf.Max(1, Mathf.FloorToInt(maxPreviewGap / Mathf.Max(0.0001f, formationSpacing)));
            int markersNeededForGap = Mathf.CeilToInt((float)totalAgents / maxStrideByGap);
            markersToShow = Mathf.Min(maxFormationPreviewMarkers, Mathf.Max(markersToShow, markersNeededForGap));

            // Evenly sample agent slots so hollow formations render complete outlines even when capped by max markers
            // Example: if 800 agents and max=200, sample across full 0..799 index range to avoid showing only a partial ring
            bool useIndexSampling = markersToShow > 0 && totalAgents > markersToShow;

            // Use temporary spacing for calculations (or locked spacing if in rotation mode)
            // When dragging per-crowd spacing, do NOT overwrite formationSpacing — GetEffectiveCrowdSpacing
            // handles per-crowd values, and formationSpacing must remain the true global fallback.
            float originalSpacing = formationSpacing;
            if (!_isDraggingPerCrowdSpacing)
                formationSpacing = _isAdjustingRotation ? _lockedSpacing : _temporarySpacing;

            // Calculate rotation quaternion for applying to formation positions
            Quaternion formationRotation = Quaternion.Euler(0f, _temporaryRotation, 0f);
            INavigationAgent previewNavAgent = GetFirstSelectedNavigationAgent();

            // === BATTLE LINE PREVIEW ===
            // When in Battle Line mode, always use CalculateBattleLinePositions for preview
            // so per-crowd formation overrides (from cycling) and group gap are respected.
            bool useBattleLinePreview = formationControlMode == FormationControlMode.BattleLine;

            if (useBattleLinePreview)
            {
                var agents = crowdController?.GetAgents();
                if (agents != null)
                {
                    float previewGap = _isDraggingBattleLineGap ? _temporaryBattleLineGap : battleLineGroupGap;
                    var blPositions = CalculateBattleLinePositions(
                        _previewCenter, _temporaryRotation, agents, out _, previewGap);

                    int blMarkersToShow = Mathf.Min(blPositions.Count, maxFormationPreviewMarkers);

                    // Ensure we have enough markers
                    while (_activePreviewMarkers.Count < blMarkersToShow)
                    {
                        GameObject marker = GetOrCreatePreviewMarker();
                        _activePreviewMarkers.Add(marker);
                    }

                    // Hide excess markers
                    for (int i = blMarkersToShow; i < _activePreviewMarkers.Count; i++)
                    {
                        if (_activePreviewMarkers[i] != null)
                            _activePreviewMarkers[i].SetActive(false);
                    }

                    // Position markers from the battle line dictionary
                    // If we have more positions than markers, sample evenly
                    var blPosList = new List<Vector3>(blPositions.Values);
                    bool blSampling = blPosList.Count > blMarkersToShow;

                    for (int i = 0; i < blMarkersToShow; i++)
                    {
                        GameObject marker = _activePreviewMarkers[i];
                        if (marker == null) continue;

                        int posIdx = blSampling
                            ? Mathf.RoundToInt((float)i / (blMarkersToShow - 1) * (blPosList.Count - 1))
                            : i;

                        Vector3 formationPos = blPosList[posIdx];

                        // Sample NavMesh
                        if (previewNavAgent != null &&
                            previewNavAgent.SamplePosition(formationPos, out Vector3 sampledPos, formationSpacing * 2f))
                        {
                            formationPos = sampledPos;
                        }
                        else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(formationPos, formationSpacing * 2f, out Vector3 navSampledPos))
                        {
                            formationPos = navSampledPos;
                        }

                        marker.transform.position = formationPos + Vector3.up * 0.1f;
                        marker.SetActive(true);
                    }

                    // Restore original spacing
                    formationSpacing = originalSpacing;
                    return;
                }
            }

            // === COMBINED FORMATION PREVIEW (original path) ===

            // Ensure we have enough markers
            while (_activePreviewMarkers.Count < markersToShow)
            {
                GameObject marker = GetOrCreatePreviewMarker();
                _activePreviewMarkers.Add(marker);
            }

            // Hide excess markers
            for (int i = markersToShow; i < _activePreviewMarkers.Count; i++)
            {
                if (_activePreviewMarkers[i] != null)
                    _activePreviewMarkers[i].SetActive(false);
            }

            // Position visible markers
            for (int i = 0; i < markersToShow; i++)
            {
                GameObject marker = _activePreviewMarkers[i];
                if (marker == null) continue;

                int agentIndex = i;
                if (useIndexSampling)
                {
                    // Spread marker samples across the full agent index range to keep hollow shapes visually complete
                    agentIndex = markersToShow == 1
                        ? 0
                        : Mathf.RoundToInt((float)i / (markersToShow - 1) * (totalAgents - 1));
                }

                // Calculate formation position using existing methods (returns position relative to center)
                Vector3 formationPos = CalculateFormationPosition(agentIndex, totalAgents, Vector3.zero);
                
                // Apply rotation to the offset, then add center
                Vector3 rotatedOffset = formationRotation * formationPos;
                formationPos = _previewCenter + rotatedOffset;
                
                // Sample against the active navigation backend first, then NavMesh fallback.
                if (previewNavAgent != null &&
                    previewNavAgent.SamplePosition(formationPos, out Vector3 sampledPos, formationSpacing * 2f))
                {
                    formationPos = sampledPos;
                }
                else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(formationPos, formationSpacing * 2f, out Vector3 navSampledPos))
                {
                    formationPos = navSampledPos;
                }

                marker.transform.position = formationPos + Vector3.up * 0.1f; // Slight offset above ground
                marker.SetActive(true);
            }

            // Restore original spacing
            formationSpacing = originalSpacing;
        }

        /// <summary>
        /// Hides all formation preview markers.
        /// </summary>
        private void HideFormationPreview()
        {
            foreach (var marker in _activePreviewMarkers)
            {
                if (marker != null)
                {
                    marker.SetActive(false);
                    _previewMarkerPool.Enqueue(marker);
                }
            }
            _activePreviewMarkers.Clear();
            _hasValidPreviewPosition = false;
        }

        /// <summary>
        /// Gets or creates a preview marker from the pool.
        /// </summary>
        private GameObject GetOrCreatePreviewMarker()
        {
            GameObject marker;

            if (_previewMarkerPool.Count > 0)
            {
                marker = _previewMarkerPool.Dequeue();
                if (marker != null)
                {
                    marker.SetActive(true);
                    return marker;
                }
            }

            // Create new marker
            if (formationPreviewPrefab != null)
            {
                marker = Instantiate(formationPreviewPrefab, _previewMarkerParent);
            }
            else
            {
                // Create default sphere marker
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.SetParent(_previewMarkerParent);
                marker.transform.localScale = Vector3.one * formationPreviewMarkerSize;

                // Remove collider to prevent interference
                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                // Set up semi-transparent material
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Create a new material instance with transparency
                    var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (material == null)
                        material = new Material(Shader.Find("Standard"));
                    
                    if (material != null)
                    {
                        // Enable transparency
                        material.SetFloat("_Surface", 1); // Transparent
                        material.SetFloat("_Blend", 0); // Alpha blend
                        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetInt("_ZWrite", 0);
                        material.DisableKeyword("_ALPHATEST_ON");
                        material.EnableKeyword("_ALPHABLEND_ON");
                        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.renderQueue = 3000;
                        material.color = formationPreviewColor;
                        renderer.material = material;
                    }
                }

                marker.name = "FormationPreviewMarker";
            }

            return marker;
        }

        /// <summary>
        /// Cleans up formation preview resources on destroy.
        /// </summary>
        private void CleanupFormationPreview()
        {
            HideFormationPreview();
            
            while (_previewMarkerPool.Count > 0)
            {
                var marker = _previewMarkerPool.Dequeue();
                if (marker != null)
                    Destroy(marker);
            }

            if (_previewMarkerParent != null)
                Destroy(_previewMarkerParent.gameObject);
        }

        #endregion

        #region Rotation Marker

        /// <summary>
        /// Updates the rotation direction marker showing the formation's facing direction.
        /// </summary>
        private void UpdateRotationMarker()
        {
            if (!_hasValidPreviewPosition)
            {
                HideRotationMarker();
                return;
            }

            // Create marker if needed
            if (_rotationMarkerInstance == null)
            {
                _rotationMarkerInstance = CreateRotationMarker();
            }

            // Position the marker in front of the formation center
            Quaternion formationRotation = Quaternion.Euler(0f, _temporaryRotation, 0f);
            Vector3 forwardOffset = formationRotation * Vector3.forward * rotationMarkerDistance;
            Vector3 markerPosition = _previewCenter + forwardOffset;

            // Sample against the active navigation backend first, then NavMesh fallback.
            INavigationAgent navAgent = GetFirstSelectedNavigationAgent();
            if (navAgent != null &&
                navAgent.SamplePosition(markerPosition, out Vector3 sampledPos, rotationMarkerDistance))
            {
                markerPosition = sampledPos;
            }
            else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(markerPosition, rotationMarkerDistance, out Vector3 navSampledPos))
            {
                markerPosition = navSampledPos;
            }

            _rotationMarkerInstance.transform.position = markerPosition + Vector3.up * 0.2f;
            _rotationMarkerInstance.SetActive(true);
        }

        /// <summary>
        /// Hides the rotation direction marker.
        /// </summary>
        private void HideRotationMarker()
        {
            if (_rotationMarkerInstance != null)
            {
                _rotationMarkerInstance.SetActive(false);
            }
        }

        /// <summary>
        /// Creates the rotation direction marker.
        /// </summary>
        private GameObject CreateRotationMarker()
        {
            GameObject marker;

            if (rotationMarkerPrefab != null)
            {
                marker = Instantiate(rotationMarkerPrefab);
            }
            else
            {
                // Create a default sphere marker
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.localScale = Vector3.one * rotationMarkerSize;

                // Remove collider to prevent interference
                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                // Set up material with rotation marker color
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (material == null)
                        material = new Material(Shader.Find("Standard"));
                    
                    if (material != null)
                    {
                        // Enable transparency
                        material.SetFloat("_Surface", 1); // Transparent
                        material.SetFloat("_Blend", 0); // Alpha blend
                        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetInt("_ZWrite", 0);
                        material.DisableKeyword("_ALPHATEST_ON");
                        material.EnableKeyword("_ALPHABLEND_ON");
                        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.renderQueue = 3000;
                        material.color = rotationMarkerColor;
                        renderer.material = material;
                    }
                }

                marker.name = "RotationDirectionMarker";
            }

            return marker;
        }

        /// <summary>
        /// Cleans up rotation marker resources on destroy.
        /// </summary>
        private void CleanupRotationMarker()
        {
            if (_rotationMarkerInstance != null)
            {
                Destroy(_rotationMarkerInstance);
                _rotationMarkerInstance = null;
            }
        }

        #endregion

        /// <summary>
        /// Performs a move command using the current mouse position.
        /// </summary>
        private void PerformMoveCommand()
        {
            PerformMoveCommandAtPosition(_mousePosition);
        }

        /// <summary>
        /// Performs a move command using the specified screen position for raycasting.
        /// This allows locking the destination to the initial click position when drag-to-adjust spacing is used.
        /// </summary>
        private void PerformMoveCommandAtPosition(Vector2 screenPosition)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);

            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] PerformMoveCommand: selectedCount={TotalSelectedCount}, screenPos={screenPosition}");
            }

            // First, check if clicking on an enemy agent for attack command
            if (enableAttackCommands && TryPerformAttackCommand(ray, screenPosition))
            {
                return; // Attack command was issued
            }

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundMask))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[RTS] Raycast hit: point={hit.point}, collider={hit.collider.name}");
                }

                // Check if clicking on a wall placement zone
                if (enableWallPlacementZones && TryPerformWallPlacementCommand(hit.point))
                {
                    return; // Wall placement command was issued
                }
                
                // Resolve a valid destination for the active navigation system
                if (TryGetCommandDestination(hit.point, out Vector3 destination))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[RTS] Command destination valid: {destination}");
                    }
                    MoveSelectedAgentsTo(destination);

                    // Fire move command events
                    MoveCommandIssued?.Invoke(destination, _selectedAgentIndices);
                    onMoveCommandIssued?.Invoke(destination, new List<int>(_selectedAgentIndices));
                    
                    // ═══ Network Hook: Fire network move command event ═══
                    FireNetworkMoveCommand(destination, _selectedAgentIndices, moveFormation, formationSpacing, Vector3.zero);
                    
                    // Play command sound
                    PlayCommandSound(TotalSelectedCount);

                    // Show move indicator
                    if (showMoveIndicator)
                    {
                        _moveIndicatorPosition = destination;
                        _moveIndicatorStartTime = Time.time;
                        _showingMoveIndicator = true;
                    }
                }
                else
                {
                    if (enableDebugLogging)
                    {
                        Debug.LogWarning($"[RTS] Failed to resolve destination for point {hit.point}");
                    }
                }
            }
            else
            {
                // Raycast missed - this is normal when clicking on sky/UI, no need to spam warnings
                if (enableDebugLogging)
                {
                    Debug.Log($"[RTS] Raycast missed ground. GroundMask={groundMask.value}");
                }
            }
        }

        /// <summary>
        /// Performs a move command with rotation using the specified screen position.
        /// This allows locking the destination to the initial click position and applying formation rotation.
        /// </summary>
        private void PerformMoveCommandAtPositionWithRotation(Vector2 screenPosition, float rotationAngle)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);

            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] PerformMoveCommandWithRotation: selectedCount={TotalSelectedCount}, screenPos={screenPosition}, rotation={rotationAngle:F1}°");
            }

            // First, check if clicking on an enemy agent for attack command
            if (enableAttackCommands && TryPerformAttackCommand(ray, screenPosition))
            {
                return; // Attack command was issued
            }

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundMask))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[RTS] Raycast hit: point={hit.point}, collider={hit.collider.name}");
                }

                // Check if clicking on a wall placement zone
                if (enableWallPlacementZones && TryPerformWallPlacementCommand(hit.point))
                {
                    return; // Wall placement command was issued
                }
                
                // Resolve a valid destination for the active navigation system
                if (TryGetCommandDestination(hit.point, out Vector3 destination))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[RTS] Command destination valid: {destination}");
                    }
                    MoveSelectedAgentsToWithRotation(destination, rotationAngle);

                    // Fire move command events
                    MoveCommandIssued?.Invoke(destination, _selectedAgentIndices);
                    onMoveCommandIssued?.Invoke(destination, new List<int>(_selectedAgentIndices));
                    
                    // Play command sound
                    PlayCommandSound(TotalSelectedCount);

                    // Show move indicator
                    if (showMoveIndicator)
                    {
                        _moveIndicatorPosition = destination;
                        _moveIndicatorStartTime = Time.time;
                        _showingMoveIndicator = true;
                    }
                }
                else
                {
                    if (enableDebugLogging)
                    {
                        Debug.LogWarning($"[RTS] Failed to resolve destination for point {hit.point}");
                    }
                }
            }
            else
            {
                // Raycast missed - this is normal when clicking on sky/UI, no need to spam warnings
                if (enableDebugLogging)
                {
                    Debug.Log($"[RTS] Raycast missed ground. GroundMask={groundMask.value}");
                }
            }
        }

        private bool TryGetCommandDestination(Vector3 hitPoint, out Vector3 destination)
        {
            destination = hitPoint;

            var navAgent = GetFirstSelectedNavigationAgent();
            if (navAgent != null)
            {
                bool requiresSurfaceValidation = navAgent.Capabilities.RequiresSurfaceValidationForControl;
                float sampleDistance = requiresSurfaceValidation ? 5f : 10f;

                // Prefer selected backend sampling for non-NavMesh backends.
                if (!requiresSurfaceValidation &&
                    navAgent.SamplePosition(hitPoint, out Vector3 sampledPos, sampleDistance))
                {
                    destination = sampledPos;
                    return true;
                }

                // For NavMesh-like backends, keep NavMesh sampling first for consistency.
                if (requiresSurfaceValidation &&
                    EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(hitPoint, sampleDistance, out Vector3 navMeshSample))
                {
                    destination = navMeshSample;
                    return true;
                }

                // Backend sampling fallback.
                if (navAgent.SamplePosition(hitPoint, out sampledPos, sampleDistance))
                {
                    destination = sampledPos;
                    return true;
                }
            }

            // Secondary fallback: Unity NavMesh sampling when available.
            if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(hitPoint, 5f, out Vector3 fallbackNavHit))
            {
                destination = fallbackNavHit;
                return true;
            }

            // Last resort: use raw hit point
            return true;
        }

        private INavigationAgent GetFirstSelectedNavigationAgent()
        {
            if (crowdController == null)
            {
                for (int i = 0; i < _selectedExternalSelectables.Count; i++)
                {
                    INavigationAgent externalNavigation = GetExternalNavigationAgent(_selectedExternalSelectables[i]);
                    if (externalNavigation != null)
                        return externalNavigation;
                }

                return null;
            }

            var agents = crowdController.GetAgents();
            foreach (int index in _selectedAgentIndices)
            {
                if (index < 0 || index >= agents.Count) continue;
                var agent = agents[index];
                if (agent == null || agent.isDead) continue;
                if (agent.navigationAgent != null)
                    return agent.navigationAgent;
            }

            for (int i = 0; i < _selectedExternalSelectables.Count; i++)
            {
                INavigationAgent externalNavigation = GetExternalNavigationAgent(_selectedExternalSelectables[i]);
                if (externalNavigation != null)
                    return externalNavigation;
            }

            return null;
        }

        /// <summary>
        /// Move all selected agents to a destination position.
        /// </summary>
        public void MoveSelectedAgentsTo(Vector3 destination)
        {
            // Cancel any pending post-combat reformation since user issued a new command
            _isAwaitingPostCombatReformation = false;
            
            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] MoveSelectedAgentsTo: destination={destination}, selectedCount={TotalSelectedCount}");
            }
            
            if (TotalSelectedCount == 0)
            {
                if (enableDebugLogging) Debug.LogWarning("[RTS] No agents selected!");
                return;
            }

            var agents = crowdController.GetAgents();
            int validCount = 0;
            int recoveredCount = 0;
            List<IRTSExternalSelectable> validExternal = new List<IRTSExternalSelectable>();
            float slowestSpeed = float.MaxValue;

            // Count valid agents (and try to recover agents that fell off NavMesh)
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead && agent.navigationAgent != null)
                    {
                        // Log detailed state for debugging
                        if (enableDebugLogging)
                        {
                            Debug.Log($"[RTS] Agent {index} state: isOnNavMesh={agent.IsOnNavMesh}, enabled={agent.IsNavigationEnabled}, " +
                                      $"position={agent.WorldPosition}");
                        }
                        
                        // Try to recover agents that are off the NavMesh
                        if (!agent.IsOnNavMesh)
                        {
                            Vector3 agentPos = agent.WorldPosition;
                            
                            // First, ensure the navigation is enabled
                            if (!agent.IsNavigationEnabled)
                            {
                                agent.navigationAgent.Enabled = true;
                                if (enableDebugLogging) Debug.Log($"[RTS] Agent {index}: Re-enabled navigation agent");
                            }
                            
                            // Try to find a valid NavMesh position
                            if (agent.navigationAgent.SamplePosition(agentPos, out Vector3 recoverPos, 10f))
                            {
                                // Try Warp - this should place the agent on the NavMesh
                                bool warpSuccess = agent.navigationAgent.Warp(recoverPos);
                                
                                if (warpSuccess && agent.IsOnNavMesh)
                                {
                                    recoveredCount++;
                                    if (enableDebugLogging) Debug.Log($"[RTS] Agent {index} RECOVERED: Warped from {agentPos} to NavMesh at {recoverPos}");
                                }
                                else
                                {
                                    // Last resort: disable and re-enable at the new position
                                    agent.navigationAgent.Enabled = false;
                                    agent.navigationAgent.Position = recoverPos;
                                    agent.navigationAgent.Enabled = true;
                                    
                                    if (agent.IsOnNavMesh)
                                    {
                                        recoveredCount++;
                                        if (enableDebugLogging) Debug.Log($"[RTS] Agent {index} RECOVERED (via reset): Position={recoverPos}");
                                    }
                                    else
                                    {
                                        if (enableDebugLogging) Debug.LogWarning($"[RTS] Agent {index}: All recovery attempts failed. Warp={warpSuccess}, isOnNavMesh={agent.IsOnNavMesh}");
                                        continue;
                                    }
                                }
                            }
                            else
                            {
                                if (enableDebugLogging) Debug.LogWarning($"[RTS] Agent {index} invalid: Could not find NavMesh within 10m of position {agentPos}");
                                continue;
                            }
                        }
                        
                        // Now check if agent is valid
                        if (agent.IsOnNavMesh)
                        {
                            validCount++;
                            if (matchFormationSpeedToSlowest)
                            {
                                float speed = GetBaselineFormationSpeed(agent);
                                if (speed > 0f && speed < slowestSpeed)
                                    slowestSpeed = speed;
                            }
                        }
                        else
                        {
                            if (enableDebugLogging) Debug.LogWarning($"[RTS] Agent {index} invalid: Still not on NavMesh after recovery attempt");
                        }
                    }
                    // Dead agents are silently skipped - they'll be cleaned up by CleanupDeadAgentsFromSelection
                }
            }

            // Validate external selections using the navigation abstraction with legacy fallback.
            foreach (var selectable in _selectedExternalSelectables)
            {
                if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                    continue;

                if (!EnsureExternalNavigationAvailable(selectable, 10f, ref recoveredCount))
                    continue;

                validExternal.Add(selectable);
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] Valid agents for move: {(validCount + validExternal.Count)}/{TotalSelectedCount} (recovered: {recoveredCount})");
            }

            if (validCount + validExternal.Count == 0)
            {
                if (enableDebugLogging) Debug.LogWarning("[RTS] No valid agents to move!");
                return;
            }

            // === BATTLE LINE MODE ===
            // If Battle Line mode is active, pre-calculate per-group formation positions
            Dictionary<int, Vector3> battleLinePositions = null;
            float battleLineSlowestSpeed = float.MaxValue;
            if (formationControlMode == FormationControlMode.BattleLine)
            {
                battleLinePositions = CalculateBattleLinePositions(destination, 0f, agents, out battleLineSlowestSpeed);
                if (matchFormationSpeedToSlowest && battleLineSlowestSpeed < float.MaxValue)
                    slowestSpeed = battleLineSlowestSpeed;
            }

            // Calculate formation positions sequentially (no optimal assignment for regular moves)
            int positionIndex = 0;
            int totalValid = validCount + validExternal.Count;
            bool useSpeedMatch = matchFormationSpeedToSlowest && slowestSpeed < float.MaxValue;
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null || !agent.IsOnNavMesh)
                    continue;

                Vector3 formationPos;
                if (battleLinePositions != null && battleLinePositions.TryGetValue(agentIndex, out Vector3 blPos))
                    formationPos = blPos;
                else
                    formationPos = CalculateFormationPosition(positionIndex, totalValid, destination);
                
                // Sample NavMesh to ensure valid position
                if (agent.navigationAgent.SamplePosition(formationPos, out Vector3 validPos, formationSpacing * 2f))
                {
                    if (useSpeedMatch)
                    {
                        ApplyFormationSpeedOverride(agent, slowestSpeed);
                    }
                    
                    // Use ForceSetNavigationState to take control - this is an explicit player command
                    agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                    
                    // Clear attack target so agent follows move command instead of chasing enemies
                    agent.attackTargetAgentIndex = -1;
                    agent.attackTargetTransform = null;
                    agent.hasAttackTargetPosition = false;
                    agent.isChasing = false;
                    agent.isAggroed = false;
                    agent.SetMoveIntent();
                    
                    // Clear follow target — move command overrides follow
                    agent.followTargetTransform = null;
                    agent.isFollowing = false;
                    agent.followFormationOffset = Vector3.zero;
                    
                    // Clear combat state to idle so combat system doesn't interfere
                    agent.combatState = CrowdCombatState.Idle;
                    
                    // Set move command timestamp for disengage grace period
                    agent.moveCommandIssuedTime = Time.time;
                    
                    // Store the manual command destination for reference
                    agent.manualCommandDestination = validPos;
                    agent.hasManualCommand = true;
                    
                    // Clear RTS formation position flag since we're moving to a new position
                    agent.hasRTSFormationPosition = false;
                    
                    // Clear broke-from-RTS flag since this is a fresh command
                    agent.brokeFromRTSFormation = false;
                    
                    // Clear formation facing since this is a regular move without rotation
                    agent.hasFormationFacing = false;
                    
                    // Resume the navigation if it was stopped
                    if (agent.navigationAgent.IsStopped)
                    {
                        agent.navigationAgent.IsStopped = false;
                        if (enableDebugLogging) Debug.Log($"[RTS] Agent {agentIndex}: Resumed navigation (was stopped)");
                    }
                    
                    // Set the destination
                    bool destSet = agent.navigationAgent.SetDestination(validPos);
                    
                    // Log state AFTER changes
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[RTS] Agent {agentIndex} AFTER: destSet={destSet}, destination={validPos}, " +
                                  $"navState={agent.navigationState}, pathPending={agent.navigationAgent.PathPending}");
                    }
                }
                else
                {
                    if (enableDebugLogging) Debug.LogWarning($"[RTS] Agent {agentIndex}: SamplePosition FAILED for formationPos {formationPos}");
                }

                positionIndex++;
            }

            foreach (var selectable in validExternal)
            {
                Vector3 formationPos = CalculateFormationPosition(positionIndex, totalValid, destination);
                Vector3 externalDestination = formationPos;
                INavigationAgent externalNavigation = GetExternalNavigationAgent(selectable);

                if (externalNavigation != null)
                {
                    if (externalNavigation.SamplePosition(formationPos, out Vector3 sampledPos, formationSpacing * 2f))
                        externalDestination = sampledPos;
                }
                else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(formationPos, formationSpacing * 2f, out Vector3 navHit))
                {
                    externalDestination = navHit;
                }

                TrySetExternalNavigationStopped(selectable, false, externalNavigation);
                selectable.SetDestination(externalDestination);

                // Track destination for external selectables so indicators/lines work
                _externalDestinations[selectable] = externalDestination;
                _externalDestinationSetTime[selectable] = Time.time;

                positionIndex++;
            }

            // Activate destination indicator
            ActivateDestinationIndicator(destination, new List<int>(_selectedAgentIndices));
        }

        /// <summary>
        /// Move all selected agents to a destination position with formation rotation.
        /// </summary>
        public void MoveSelectedAgentsToWithRotation(Vector3 destination, float rotationAngle)
        {
            if (TotalSelectedCount == 0)
                return;

            var agents = crowdController.GetAgents();
            int validCount = 0;
            int recoveredCount = 0;
            List<IRTSExternalSelectable> validExternal = new List<IRTSExternalSelectable>();

            // Count valid agents (same recovery logic as MoveSelectedAgentsTo)
            foreach (int index in _selectedAgentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead && agent.navigationAgent != null)
                    {
                        // Try to recover agents that are off the navigable surface
                        if (!agent.IsOnNavMesh)
                        {
                            Vector3 agentPos = agent.WorldPosition;
                            
                            if (!agent.IsNavigationEnabled)
                            {
                                agent.navigationAgent.Enabled = true;
                            }
                            
                            if (agent.navigationAgent.SamplePosition(agentPos, out Vector3 recoverPos, 10f))
                            {
                                agent.navigationAgent.Position = recoverPos;
                                bool warpSuccess = agent.navigationAgent.Warp(recoverPos);
                                
                                if (warpSuccess && agent.IsOnNavMesh)
                                {
                                    recoveredCount++;
                                }
                                else
                                {
                                    agent.navigationAgent.Enabled = false;
                                    agent.navigationAgent.Position = recoverPos;
                                    agent.navigationAgent.Enabled = true;
                                    
                                    if (agent.IsOnNavMesh)
                                    {
                                        recoveredCount++;
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }

                        if (agent.IsOnNavMesh)
                        {
                            validCount++;
                        }
                    }
                }
            }

            // Validate external selections
            foreach (var selectable in _selectedExternalSelectables)
            {
                if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                    continue;

                if (!EnsureExternalNavigationAvailable(selectable, 10f, ref recoveredCount))
                    continue;

                validExternal.Add(selectable);
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] Valid agents for rotated move: {(validCount + validExternal.Count)}/{TotalSelectedCount} (recovered: {recoveredCount})");
            }

            if (validCount + validExternal.Count == 0)
            {
                if (enableDebugLogging) Debug.LogWarning("[RTS] No valid agents to move!");
                return;
            }

            // Build list of valid agent indices for assignment calculation
            List<int> validAgentIndices = new List<int>(validCount);
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null || !agent.IsOnNavMesh) continue;
                validAgentIndices.Add(agentIndex);
            }

            float slowestSpeed = float.MaxValue;
            if (matchFormationSpeedToSlowest)
            {
                foreach (int agentIndex in validAgentIndices)
                {
                    CrowdAgent agent = agents[agentIndex];
                    float speed = GetBaselineFormationSpeed(agent);
                    if (speed > 0f && speed < slowestSpeed)
                        slowestSpeed = speed;
                }
            }

            // Calculate formation positions with rotation and optimal assignment
            int totalValid = validCount + validExternal.Count;
            
            // === BATTLE LINE MODE (with rotation) ===
            Dictionary<int, Vector3> battleLinePositions = null;
            if (formationControlMode == FormationControlMode.BattleLine)
            {
                float blSlowestSpeed;
                battleLinePositions = CalculateBattleLinePositions(destination, rotationAngle, agents, out blSlowestSpeed);
                if (matchFormationSpeedToSlowest && blSlowestSpeed < float.MaxValue)
                    slowestSpeed = blSlowestSpeed;
            }
            
            Vector3[] formationPositions = (battleLinePositions == null)
                ? CalculateAllFormationPositions(totalValid, destination, rotationAngle)
                : null; // Not needed in battle line mode — positions come from the dictionary
            
            Vector3[] crowdFormationPositions = new Vector3[validCount];
            if (battleLinePositions != null)
            {
                // Fill crowdFormationPositions from the battle line dictionary (for optimal assignment)
                for (int i = 0; i < validAgentIndices.Count; i++)
                {
                    int agentIndex = validAgentIndices[i];
                    if (battleLinePositions.TryGetValue(agentIndex, out Vector3 blPos))
                        crowdFormationPositions[i] = blPos;
                }
            }
            else if (formationPositions != null && validCount > 0)
            {
                Array.Copy(formationPositions, crowdFormationPositions, validCount);
            }

            int[] assignments = null;
            
            if (useOptimalFormationAssignment && validCount > 1)
            {
                Vector3[] agentPositions = GetAgentCurrentPositions(validAgentIndices, agents);
                assignments = CalculateOptimalFormationAssignment(agentPositions, crowdFormationPositions);
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS] Using optimal formation assignment for {validCount} agents with rotation {rotationAngle:F1}°");
            }

            // Apply formation positions to agents
            for (int i = 0; i < validAgentIndices.Count; i++)
            {
                int agentIndex = validAgentIndices[i];
                CrowdAgent agent = agents[agentIndex];

                // Get the formation position index - either from optimal assignment or sequential
                int formationPosIndex = (assignments != null) ? assignments[i] : i;
                Vector3 formationPos = crowdFormationPositions[formationPosIndex];
                
                // Sample through the active navigation backend to ensure valid position.
                if (agent.navigationAgent.SamplePosition(formationPos, out Vector3 sampledPos, formationSpacing * 2f))
                {
                    if (matchFormationSpeedToSlowest && slowestSpeed < float.MaxValue)
                    {
                        ApplyFormationSpeedOverride(agent, slowestSpeed);
                    }

                    // Use ForceSetNavigationState to take control - this is an explicit player command
                    agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                    
                    // Clear attack target so agent follows move command instead of chasing enemies
                    agent.attackTargetAgentIndex = -1;
                    agent.attackTargetTransform = null;
                    agent.hasAttackTargetPosition = false;
                    agent.isChasing = false;
                    agent.isAggroed = false;
                    agent.SetMoveIntent();
                    
                    // Clear follow target — move command overrides follow
                    agent.followTargetTransform = null;
                    agent.isFollowing = false;
                    agent.followFormationOffset = Vector3.zero;
                    
                    // Clear combat state to idle so combat system doesn't interfere
                    agent.combatState = CrowdCombatState.Idle;
                    
                    // Set move command timestamp for disengage grace period
                    agent.moveCommandIssuedTime = Time.time;
                    
                    // Store the manual command destination for reference
                    agent.manualCommandDestination = sampledPos;
                    agent.hasManualCommand = true;
                    
                    // Clear RTS formation position flag since we're moving to a new position
                    agent.hasRTSFormationPosition = false;
                    
                    // Clear broke-from-RTS flag since this is a fresh command
                    agent.brokeFromRTSFormation = false;
                    
                    // Store the formation facing direction so agent will face this direction on arrival
                    agent.hasFormationFacing = true;
                    agent.formationFacingAngle = rotationAngle;
                    
                    // Resume the navigation agent if it was stopped
                    if (agent.navigationAgent.IsStopped)
                    {
                        agent.navigationAgent.IsStopped = false;
                    }
                    
                    // Set the destination
                    agent.navigationAgent.SetDestination(sampledPos);
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[RTS] Agent {agentIndex}: Rotated formation destination={sampledPos}, facing={rotationAngle:F1}°");
                    }
                }
                else
                {
                    if (enableDebugLogging) Debug.LogWarning($"[RTS] Agent {agentIndex}: SamplePosition FAILED for rotated formationPos {formationPos}");
                }
            }

            // Apply formation positions to external selectables (append after crowd agents)
            // In battle line mode, external selectables are not part of any EnemyCrowd group,
            // so fall back to combined formation for them.
            if (validExternal.Count > 0)
            {
                Vector3[] externalFormationPositions = formationPositions 
                    ?? CalculateAllFormationPositions(totalValid, destination, rotationAngle);
                int externalOffset = validCount;
                foreach (var selectable in validExternal)
                {
                    if (externalOffset >= externalFormationPositions.Length)
                        break;

                    Vector3 formationPos = externalFormationPositions[externalOffset];
                    Vector3 externalDestination = formationPos;
                    INavigationAgent externalNavigation = GetExternalNavigationAgent(selectable);

                    if (externalNavigation != null)
                    {
                        if (externalNavigation.SamplePosition(formationPos, out Vector3 sampledPos, formationSpacing * 2f))
                            externalDestination = sampledPos;
                    }
                    else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(formationPos, formationSpacing * 2f, out Vector3 navHit))
                    {
                        externalDestination = navHit;
                    }

                    TrySetExternalNavigationStopped(selectable, false, externalNavigation);
                    selectable.SetDestination(externalDestination);

                    // Track destination for external selectables so indicators/lines work
                    _externalDestinations[selectable] = externalDestination;
                    _externalDestinationSetTime[selectable] = Time.time;

                    externalOffset++;
                }
            }

            // Activate destination indicator
            ActivateDestinationIndicator(destination, new List<int>(_selectedAgentIndices));
        }

        #endregion

        #region Attack Commands

        /// <summary>
        /// Tries to perform an attack command if the ray hits an enemy agent.
        /// Returns true if an attack command was issued, false if no enemy was found.
        /// </summary>
        private bool TryPerformAttackCommand(Ray ray, Vector2 screenPosition)
        {
            if (_selectedAgentIndices.Count == 0)
            {
                return false;
            }

            int targetAgentIndex = -1;
            IEnemyMassesDamageable damageableTarget = null;

            // First try: Physics raycast for agents with colliders (and damageable props)
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, attackTargetMask))
            {
                targetAgentIndex = GetAgentIndexFromHit(hit);

                if (targetAgentIndex < 0)
                {
                    damageableTarget = hit.collider != null 
                        ? hit.collider.GetComponentInParent<IEnemyMassesDamageable>() 
                        : null;
                }
            }

            // Fallback: Distance-based selection for agents without colliders
            if (targetAgentIndex < 0)
            {
                if (Physics.Raycast(ray, out RaycastHit groundHit, 1000f, groundMask))
                {
                    // Use a specialized method that only finds NON-player-controlled agents
                    targetAgentIndex = FindNearestEnemyAgentToPoint(groundHit.point, attackTargetClickRadius);
                }
            }

            // If no agent found, try damageable object under cursor
            if (targetAgentIndex < 0 && damageableTarget != null && damageableTarget.IsAlive)
            {
                AttackTransform(damageableTarget.DamageTransform);
                return true;
            }

            // No agent found under click
            if (targetAgentIndex < 0)
            {
                return false;
            }

            // Validate target agent
            var agents = crowdController.GetAgents();
            if (targetAgentIndex >= agents.Count)
            {
                return false;
            }

            CrowdAgent targetAgent = agents[targetAgentIndex];
            if (targetAgent == null || targetAgent.isDead)
            {
                return false;
            }

            // Check if target is an enemy (not player-controlled)
            if (IsPlayerControlled(targetAgent))
            {
                return false;
            }

            // Check if target is from a different faction than the selected agents
            // Get faction of first selected agent as reference
            int firstSelectedIndex = _selectedAgentIndices[0];
            if (firstSelectedIndex < 0 || firstSelectedIndex >= agents.Count)
            {
                return false;
            }

            CrowdAgent firstSelectedAgent = agents[firstSelectedIndex];
            if (firstSelectedAgent == null || firstSelectedAgent.enemyCrowd == null)
            {
                return false;
            }

            string selectedFaction = firstSelectedAgent.enemyCrowd.factionName;
            string targetFaction = targetAgent.enemyCrowd?.factionName ?? "NULL";
            
            // If same faction, don't attack (friendly fire prevention)
            if (targetAgent.enemyCrowd != null && targetFaction == selectedFaction)
            {
                return false;
            }

            // Valid enemy target found - issue attack command
            PerformAttackCommand(targetAgentIndex, targetAgent);
            return true;
        }

        /// <summary>
        /// Finds the nearest NON-player-controlled agent to a world point within a given radius.
        /// Used for attack targeting when enemy agents don't have colliders.
        /// </summary>
        private int FindNearestEnemyAgentToPoint(Vector3 point, float maxDistance)
        {
            var agents = crowdController.GetAgents();
            int nearestIndex = -1;
            float nearestDistSq = maxDistance * maxDistance;
            
            // Get the faction of first selected agent to avoid friendly fire
            string selectedFaction = null;
            if (_selectedAgentIndices.Count > 0)
            {
                int firstSelected = _selectedAgentIndices[0];
                if (firstSelected >= 0 && firstSelected < agents.Count)
                {
                    CrowdAgent firstAgent = agents[firstSelected];
                    if (firstAgent?.enemyCrowd != null)
                    {
                        selectedFaction = firstAgent.enemyCrowd.factionName;
                    }
                }
            }

            int checkedCount = 0;
            int skippedPlayerControlled = 0;
            int skippedSameFaction = 0;
            int skippedTooFar = 0;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                checkedCount++;

                // Skip player-controlled agents - we're looking for enemies to attack
                if (IsPlayerControlled(agent))
                {
                    skippedPlayerControlled++;
                    continue;
                }

                // Skip same faction (friendly fire prevention)
                if (selectedFaction != null && agent.enemyCrowd != null && 
                    agent.enemyCrowd.factionName == selectedFaction)
                {
                    skippedSameFaction++;
                    continue;
                }

                // Use the visual world position (includes flight offset) so clicking
                // near an airborne agent's mesh can target it for attack.
                Vector3 agentPos = GetAgentWorldPosition(agent);
                float dx = agentPos.x - point.x;
                float dz = agentPos.z - point.z;
                float distSq = dx * dx + dz * dz;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestIndex = i;
                }
                else
                {
                    skippedTooFar++;
                }
            }

            return nearestIndex;
        }

        /// <summary>
        /// Finds the agent index that matches the given transform.
        /// Returns -1 if no matching agent is found.
        /// </summary>
        private int FindAgentIndexByTransform(Transform target)
        {
            if (target == null)
                return -1;

            var agents = crowdController.GetAgents();
            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.navigationAgent == null)
                    continue;

                // Check if the navigation agent's transform matches
                Transform agentTransform = agent.navigationAgent.GetUnderlyingAgent<Component>()?.transform;
                if (agentTransform == null)
                    continue;
                    
                if (agentTransform == target)
                    return i;

                // Also check the parent transform in case target is a child
                if (agentTransform.IsChildOf(target) || target.IsChildOf(agentTransform))
                    return i;

                // Check crowdInstance if available
                if (agent.crowdInstance != null && (agent.crowdInstance.transform == target || 
                    agent.crowdInstance.transform.IsChildOf(target) || target.IsChildOf(agent.crowdInstance.transform)))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Issues an attack command for all selected agents to attack the specified target agent.
        /// Agents will immediately move to close the gap and attack when in range.
        /// Unlike move commands, attack commands break formation - agents chase individually.
        /// </summary>
        private void PerformAttackCommand(int targetAgentIndex, CrowdAgent targetAgent)
        {
            string targetFaction = targetAgent.enemyCrowd?.factionName ?? "Unknown";

            var agents = crowdController.GetAgents();
            Transform targetTransform = targetAgent.navigationAgent?.GetUnderlyingAgent<Component>()?.transform;
            
            if (targetTransform == null)
            {
                return;
            }

            Vector3 targetPosition = targetTransform.position;
            
            int attackersAssigned = 0;
            int skippedInvalid = 0;
            int skippedNotOnNavMesh = 0;
            int recoveredCount = 0;

            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    skippedInvalid++;
                    continue;
                }

                CrowdAgent attacker = agents[agentIndex];
                if (attacker == null || attacker.isDead || attacker.navigationAgent == null)
                {
                    skippedInvalid++;
                    continue;
                }

                // Try to recover agents that are off the navigable surface (same logic as MoveSelectedAgentsTo)
                if (!attacker.IsOnNavMesh)
                {
                    Vector3 recoveryPos = attacker.WorldPosition;
                    
                    // First, ensure the navigation agent is enabled
                    if (!attacker.IsNavigationEnabled)
                    {
                        attacker.navigationAgent.Enabled = true;
                    }
                    
                    // Try to find a valid navigable position
                    if (attacker.navigationAgent.SamplePosition(recoveryPos, out Vector3 recoverPos, 10f))
                    {
                        // Move the position first, then warp
                        attacker.navigationAgent.Position = recoverPos;
                        
                        // Try Warp - this should place the agent on the navigable surface
                        bool warpSuccess = attacker.navigationAgent.Warp(recoverPos);
                        
                        if (warpSuccess && attacker.IsOnNavMesh)
                        {
                            recoveredCount++;
                        }
                        else
                        {
                            // Last resort: disable and re-enable at the new position
                            attacker.navigationAgent.Enabled = false;
                            attacker.navigationAgent.Position = recoverPos;
                            attacker.navigationAgent.Enabled = true;
                            
                            if (attacker.IsOnNavMesh)
                            {
                                recoveredCount++;
                            }
                            else
                            {
                                skippedNotOnNavMesh++;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        skippedNotOnNavMesh++;
                        continue;
                    }
                }

                // Set attack target
                attacker.attackTargetTransform = targetTransform;
                attacker.attackTargetPosition = targetPosition;
                attacker.hasAttackTargetPosition = true;
                attacker.attackTargetAgentIndex = targetAgentIndex;
                
                // Mark as explicit manual attack command (allows Passive agents to attack)
                attacker.SetExplicitAttackIntent(targetAgentIndex);

                // Clear hold/manual locks before switching state to chasing
                attacker.hasHoldPositionSet = false;
                attacker.hasManualCommand = false;
                attacker.manualCommandDestination = Vector3.zero;

                // Change navigation state to chasing
                attacker.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                attacker.isChasing = true;
                
                // Set combat state to approach (chasing target)
                attacker.combatState = CrowdCombatState.Approach;

                // Clear formation facing since we're attacking (break formation)
                attacker.hasFormationFacing = false;
                
                // Clear manual command flags - attack takes priority
                attacker.hasManualCommand = false;
                
                // Clear RTS formation position flag - attack breaks formation hold
                attacker.hasRTSFormationPosition = false;
                
                // Clear broke-from-RTS flag since this is an explicit attack command
                attacker.brokeFromRTSFormation = false;
                
                // Resume the navigation agent if it was stopped (e.g., at previous destination)
                bool wasStopped = attacker.navigationAgent.IsStopped;
                if (wasStopped)
                {
                    attacker.navigationAgent.IsStopped = false;
                }
                
                // IMMEDIATELY set destination to start closing the gap
                // This makes agents responsive like in classic RTS games (C&C, AoE)
                // Don't wait for UpdateAttackTargets - move NOW
                
                // Calculate ideal attack distance based on combat type
                float idealAttackDistance = 1.5f; // Default melee range
                if (attacker.enemyCrowd != null)
                {
                    if (attacker.enemyCrowd.combatRangeType == CombatRangeType.Melee ||
                        (attacker.enemyCrowd.combatRangeType == CombatRangeType.Hybrid && attacker.activeHybridMode == CombatRangeType.Melee))
                    {
                        idealAttackDistance = (attacker.enemyCrowd.meleeMinDistance + attacker.enemyCrowd.meleeMaxDistance) * 0.5f;
                    }
                    else if (attacker.enemyCrowd.combatRangeType == CombatRangeType.Ranged ||
                             (attacker.enemyCrowd.combatRangeType == CombatRangeType.Hybrid && attacker.activeHybridMode == CombatRangeType.Ranged))
                    {
                        idealAttackDistance = (attacker.enemyCrowd.rangedMinDistance + attacker.enemyCrowd.rangedMaxDistance) * 0.5f;
                    }
                }
                
                // Set stopping distance as backup (navigation agent will also stop at this distance)
                attacker.navigationAgent.StoppingDistance = idealAttackDistance;
                
                // Calculate distance and direction to target
                Vector3 agentPos = attacker.WorldPosition;
                Vector3 dirToTarget = targetPosition - agentPos;
                float distToTarget = dirToTarget.magnitude;
                
                // Only set destination if not already in range
                if (distToTarget > idealAttackDistance + 0.1f)
                {
                    // Calculate attack position: a point at attack range FROM the enemy, TOWARD the attacker
                    // This ensures agents stop at attack range and don't walk into/through the enemy
                    Vector3 dirNormalized = dirToTarget.normalized;
                    Vector3 attackPosition = targetPosition - dirNormalized * idealAttackDistance;
                    
                    // Verify the attack position is on navigable surface
                    bool navMeshValid = attacker.navigationAgent.SamplePosition(attackPosition, out Vector3 navHitPos, idealAttackDistance);
                    if (navMeshValid)
                    {
                        attackPosition = navHitPos;
                    }
                    
                    bool destSet = attacker.navigationAgent.SetDestination(attackPosition);
                    attacker.hasArrived = false;
                }
                else
                {
                    // Already in range - can attack immediately
                }

                attackersAssigned++;
            }

            
            if (attackersAssigned > 0)
            {
                // Notify navigation controller to apply warmup delay for target tracking
                crowdController.GetNavigationController()?.NotifyAttackTargetsSet();

                // Fire attack command events
                AttackCommandIssued?.Invoke(targetAgentIndex, _selectedAgentIndices);
                onAttackCommandIssued?.Invoke(targetAgentIndex, new List<int>(_selectedAgentIndices));
                
                // ═══ Network Hook: Fire network attack command event ═══
                FireNetworkAttackCommand(targetAgentIndex, targetAgent.networkId, _selectedAgentIndices);

                // Play command sound
                PlayCommandSound(attackersAssigned);

                // Show indicator at target position
                if (showMoveIndicator)
                {
                    _moveIndicatorPosition = targetTransform.position;
                    _moveIndicatorStartTime = Time.time;
                    _showingMoveIndicator = true;
                }

                // Show attack target feedback (shader highlight / indicator on enemy)
                if (enableAttackTargetFeedback)
                {
                    ShowAttackTargetFeedback(targetAgentIndex);
                }

                // Start tracking for post-combat reformation
                CapturePostCombatReformationSnapshot();
                _isAwaitingPostCombatReformation = true;
                _postCombatCheckStartTime = Time.time;
                _lastAttackTargetPosition = targetTransform.position;

                if (enableDebugLogging)
                    Debug.Log($"[RTS Attack] Attack command completed: {attackersAssigned} agents assigned to attack");
            }
        }

        /// <summary>
        /// Public method to issue an attack command on a specific agent index.
        /// Can be called from UI or other scripts.
        /// </summary>
        public void AttackTarget(int targetAgentIndex)
        {
            if (TotalSelectedCount == 0)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[RTS Attack] No agents selected for attack command!");
                return;
            }

            var agents = crowdController.GetAgents();
            if (targetAgentIndex < 0 || targetAgentIndex >= agents.Count)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[RTS Attack] Invalid target agent index: {targetAgentIndex}");
                return;
            }

            CrowdAgent targetAgent = agents[targetAgentIndex];
            if (targetAgent == null || targetAgent.isDead)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[RTS Attack] Target agent {targetAgentIndex} is null or dead!");
                return;
            }

            PerformAttackCommand(targetAgentIndex, targetAgent);
        }

        /// <summary>
        /// Public method to issue an attack command on a transform (e.g., the player).
        /// Agents will immediately move to close the gap and attack when in range.
        /// Can be called from UI or other scripts.
        /// </summary>
        public void AttackTransform(Transform target)
        {
            if (target == null)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[RTS Attack] Cannot attack null transform!");
                return;
            }

            if (TotalSelectedCount == 0)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[RTS Attack] No agents selected for attack command!");
                return;
            }

            var agents = crowdController.GetAgents();
            Vector3 targetPosition = target.position;
            int attackersAssigned = 0;
            List<IRTSExternalSelectable> validExternal = null;

            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent attacker = agents[agentIndex];
                if (attacker == null || attacker.isDead || attacker.navigationAgent == null)
                    continue;

                if (!attacker.IsOnNavMesh)
                    continue;

                // Set attack target
                attacker.attackTargetTransform = target;
                attacker.attackTargetPosition = targetPosition;
                attacker.hasAttackTargetPosition = true;
                attacker.attackTargetAgentIndex = -1; // Not a crowd agent

                // Mark as explicit manual attack command even for transform targets.
                // This prevents stale manual-destination fallback when the target disappears.
                attacker.SetExplicitAttackIntent(-1);

                // Change navigation state to chasing
                attacker.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                attacker.isChasing = true;
                
                // Set combat state to approach (chasing target)
                attacker.combatState = CrowdCombatState.Approach;
                
                // Clear formation - break formation when attacking
                attacker.hasFormationFacing = false;
                attacker.hasManualCommand = false;
                attacker.hasRTSFormationPosition = false;
                
                // Resume the navigation agent if it was stopped
                if (attacker.navigationAgent.IsStopped)
                {
                    attacker.navigationAgent.IsStopped = false;
                }
                
                // IMMEDIATELY set destination to close the gap (RTS-style responsiveness)
                // Calculate ideal attack distance based on combat type
                float idealAttackDistance = 1.5f;
                if (attacker.enemyCrowd != null)
                {
                    if (attacker.enemyCrowd.combatRangeType == CombatRangeType.Melee ||
                        (attacker.enemyCrowd.combatRangeType == CombatRangeType.Hybrid && attacker.activeHybridMode == CombatRangeType.Melee))
                    {
                        idealAttackDistance = (attacker.enemyCrowd.meleeMinDistance + attacker.enemyCrowd.meleeMaxDistance) * 0.5f;
                    }
                    else if (attacker.enemyCrowd.combatRangeType == CombatRangeType.Ranged ||
                             (attacker.enemyCrowd.combatRangeType == CombatRangeType.Hybrid && attacker.activeHybridMode == CombatRangeType.Ranged))
                    {
                        idealAttackDistance = (attacker.enemyCrowd.rangedMinDistance + attacker.enemyCrowd.rangedMaxDistance) * 0.5f;
                    }
                }
                
                // Set stopping distance as backup
                attacker.navigationAgent.StoppingDistance = idealAttackDistance;
                
                // Calculate distance and direction to target
                Vector3 agentPos = attacker.WorldPosition;
                Vector3 dirToTarget = targetPosition - agentPos;
                float distToTarget = dirToTarget.magnitude;
                
                if (distToTarget > idealAttackDistance + 0.1f)
                {
                    // Calculate attack position: stop at attack range FROM the enemy, not AT the enemy
                    Vector3 dirNormalized = dirToTarget.normalized;
                    Vector3 attackPosition = targetPosition - dirNormalized * idealAttackDistance;
                    
                    // Verify the attack position is on navigable surface
                    if (attacker.navigationAgent.SamplePosition(attackPosition, out Vector3 navHitPos, idealAttackDistance))
                    {
                        attackPosition = navHitPos;
                    }
                    
                    attacker.navigationAgent.SetDestination(attackPosition);
                    attacker.hasArrived = false;
                }

                attackersAssigned++;
            }

            // External selectables (e.g., GC2 players) move toward the target
            if (_selectedExternalSelectables.Count > 0)
            {
                validExternal = validExternal ?? new List<IRTSExternalSelectable>();
                foreach (var selectable in _selectedExternalSelectables)
                {
                    if (!IsExternalSelectableValid(selectable, requireSelectable: false))
                        continue;

                    int recoveryCount = 0;
                    if (!EnsureExternalNavigationAvailable(selectable, 10f, ref recoveryCount))
                        continue;

                    INavigationAgent externalNavigation = GetExternalNavigationAgent(selectable);
                    TrySetExternalNavigationStopped(selectable, false, externalNavigation);
                    TrySetExternalStoppingDistance(selectable, 1.5f, externalNavigation);

                    selectable.SetDestination(targetPosition);

                    attackersAssigned++;
                    validExternal.Add(selectable);
                }
            }

            if (attackersAssigned > 0)
            {
                crowdController.GetNavigationController()?.NotifyAttackTargetsSet();

                // Fire attack command events with -1 target index (non-crowd target)
                AttackCommandIssued?.Invoke(-1, _selectedAgentIndices);
                onAttackCommandIssued?.Invoke(-1, new List<int>(_selectedAgentIndices));
                PlayCommandSound(attackersAssigned);

                if (showMoveIndicator)
                {
                    _moveIndicatorPosition = targetPosition;
                    _moveIndicatorStartTime = Time.time;
                    _showingMoveIndicator = true;
                }

                // Show attack target feedback - try to find agent index if this transform belongs to one
                if (enableAttackTargetFeedback)
                {
                    int targetAgentIndex = FindAgentIndexByTransform(target);
                    if (targetAgentIndex >= 0)
                    {
                        ShowAttackTargetFeedback(targetAgentIndex);
                    }
                }

                if (enableDebugLogging)
                    Debug.Log($"[RTS Attack] {attackersAssigned} agents assigned to attack transform at {targetPosition}");
            }
        }

        #endregion

        #region Wall Placement Zones

        /// <summary>
        /// Discovers all EnemyMassesWallPlacementZone components in the scene and adds them to the list.
        /// </summary>
        public void DiscoverWallPlacementZones()
        {
#if UNITY_2023_1_OR_NEWER
            var zones = FindObjectsByType<EnemyMassesWallPlacementZone>(FindObjectsSortMode.None);
#else
            var zones = FindObjectsOfType<EnemyMassesWallPlacementZone>();
#endif

            foreach (var zone in zones)
            {
                if (!wallPlacementZones.Contains(zone))
                {
                    wallPlacementZones.Add(zone);
                }
            }

            // Clean up null references
            wallPlacementZones.RemoveAll(z => z == null);

            if (enableDebugLogging)
                Debug.Log($"[RTS] Discovered {wallPlacementZones.Count} wall placement zones");
        }

        /// <summary>
        /// Registers a wall placement zone dynamically at runtime.
        /// </summary>
        public void RegisterWallPlacementZone(EnemyMassesWallPlacementZone zone)
        {
            if (zone != null && !wallPlacementZones.Contains(zone))
            {
                wallPlacementZones.Add(zone);
            }
        }

        /// <summary>
        /// Unregisters a wall placement zone.
        /// </summary>
        public void UnregisterWallPlacementZone(EnemyMassesWallPlacementZone zone)
        {
            wallPlacementZones.Remove(zone);
        }

        /// <summary>
        /// Attempts to handle a command click as a wall placement zone assignment.
        /// Returns true if a zone was found and agents were assigned to spots.
        /// </summary>
        private bool TryPerformWallPlacementCommand(Vector3 worldPosition)
        {
            if (!enableWallPlacementZones || wallPlacementZones.Count == 0)
                return false;

            if (_selectedAgentIndices.Count == 0)
                return false;

            // Find the nearest zone that the click position is within
            // Use a reasonable click radius (10 units from zone center, or within the zone's spot area)
            const float DEFAULT_CLICK_RADIUS = 15f;
            EnemyMassesWallPlacementZone targetZone = null;
            float nearestDistSqr = float.MaxValue;

            foreach (var zone in wallPlacementZones)
            {
                if (zone == null)
                    continue;

                // Check if click is within zone's area or near it
                float distSqr = (worldPosition - zone.transform.position).sqrMagnitude;
                float clickRadiusSqr = DEFAULT_CLICK_RADIUS * DEFAULT_CLICK_RADIUS;

                // Also check if click is directly on a spot
                bool isOnSpot = zone.IsPositionInZone(worldPosition, 2f);

                if ((distSqr <= clickRadiusSqr || isOnSpot) && distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    targetZone = zone;
                }
            }

            if (targetZone == null)
                return false;

            var agents = crowdController != null ? crowdController.GetAgents() : null;
            var wallPlacementCandidates = new List<int>(_selectedAgentIndices.Count);
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agents == null || agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (SupportsWallPlacement(agent))
                    wallPlacementCandidates.Add(agentIndex);
            }

            if (wallPlacementCandidates.Count == 0)
                return false;

            // Assign selected agents to available spots nearest to the click position
            int assigned = targetZone.AssignAgentsToSpotsNearPosition(wallPlacementCandidates, worldPosition);

            if (assigned > 0)
            {
                if (enableDebugLogging)
                    Debug.Log($"[RTS] Assigned {assigned} agents to wall placement zone '{targetZone.name}'");

                // Play command sound
                PlayCommandSound(assigned);

                // Show indicator at zone position
                if (showMoveIndicator)
                {
                    _moveIndicatorPosition = targetZone.transform.position;
                    _moveIndicatorStartTime = Time.time;
                    _showingMoveIndicator = true;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets all registered wall placement zones.
        /// </summary>
        public IReadOnlyList<EnemyMassesWallPlacementZone> WallPlacementZones => wallPlacementZones;

        private static bool SupportsWallPlacement(CrowdAgent agent)
        {
            var navAgent = agent?.navigationAgent;
            if (navAgent == null)
                return false;

            if (navAgent.Capabilities.RequiresSurfaceValidationForControl && !navAgent.IsOnNavMesh)
                return false;

            return true;
        }

        #endregion

        #region Stop Command

        /// <summary>
        /// Stop all selected agents immediately at their current position.
        /// Clears any pending movement commands and keeps agents where they are.
        /// For HoldPosition agents, this updates their hold position to where they stopped.
        /// </summary>
        public void StopSelectedAgents()
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] StopSelectedAgents: selectedCount={_selectedAgentIndices.Count}");
            }
            
            if (_selectedAgentIndices.Count == 0)
            {
                if (enableDebugLogging) Debug.LogWarning("[RTS] No agents selected!");
                return;
            }

            var agents = crowdController.GetAgents();
            int stoppedCount = 0;

            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                RestoreFormationSpeedOverride(agentIndex, agent);

                if (!agent.IsOnNavMesh)
                    continue;

                // Stop the navigation agent immediately
                agent.navigationAgent.ResetPath();
                agent.navigationAgent.Velocity = Vector3.zero;
                
                // Clear manual command state
                agent.hasManualCommand = false;
                
                // Clear RTS formation position flags
                agent.hasRTSFormationPosition = false;
                agent.brokeFromRTSFormation = false;
                
                // Clear attack target so they don't start chasing
                agent.attackTargetAgentIndex = -1;
                agent.attackTargetTransform = null;
                agent.hasAttackTargetPosition = false;
                agent.isChasing = false;
                agent.ClearCommandIntent();
                
                // Clear follow target
                agent.followTargetTransform = null;
                agent.isFollowing = false;
                agent.followFormationOffset = Vector3.zero;
                
                // Clear any queued manual skill so the agent doesn't stay in a
                // "locked" state where ProcessQueuedSkills keeps retrying a stale skill.
                agent.queuedSkillSlot = -1;
                agent.queuedSkillTargetIndex = -1;
                agent.queuedSkillGroundTarget = null;
                agent.queuedSkillDamageableTarget = null;
                
                // For HoldPosition agents, update hold position to current location
                if (agent.combatStance == CombatStance.HoldPosition)
                {
                    agent.holdPositionLocation = agent.WorldPosition;
                    agent.hasHoldPositionSet = true;
                    // Keep in ManualCommand state so they stay put
                    agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS] Agent {agentIndex}: Stopped at {agent.WorldPosition}, updated hold position");
                }
                else
                {
                    // For other stances, return to Formation state (idle)
                    agent.ForceSetNavigationState(AgentNavigationState.Formation);
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS] Agent {agentIndex}: Stopped at {agent.WorldPosition}, returned to Formation");
                }
                
                stoppedCount++;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[RTS] Stopped {stoppedCount}/{_selectedAgentIndices.Count} agents");
            }

            // Clear patrol paths for stopped agents
            ClearPatrolPathsForSelection();

            // Deactivate destination indicator when stopping
            DeactivateDestinationIndicator();
        }

        // ── Follow Command ──────────────────────────────────────────────

        private float _nextFollowUpdateTime;

        /// <summary>
        /// Orders all currently selected agents to follow the specified Transform.
        /// Agents continuously track the target's position without entering combat.
        /// </summary>
        public void FollowSelectedAgents(Transform target)
        {
            if (target == null || crowdController == null || _selectedAgentIndices.Count == 0)
                return;

            // Cancel any pending post-combat reformation since user issued a new command
            _isAwaitingPostCombatReformation = false;

            var agents = crowdController.GetAgents();
            Vector3 targetPos = target.position;

            // --- Count valid agents and recover off-mesh agents ---
            int validCount = 0;
            float slowestSpeed = float.MaxValue;
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null) continue;
                if (!IsAgentPlayerControlled(agentIndex)) continue;

                // Try to recover agents that are off the NavMesh
                if (!agent.IsOnNavMesh)
                {
                    Vector3 agentPos = agent.WorldPosition;
                    if (!agent.IsNavigationEnabled)
                        agent.navigationAgent.Enabled = true;

                    if (agent.navigationAgent.SamplePosition(agentPos, out Vector3 recoverPos, 10f))
                    {
                        bool warpSuccess = agent.navigationAgent.Warp(recoverPos);
                        if (!warpSuccess || !agent.IsOnNavMesh)
                        {
                            agent.navigationAgent.Enabled = false;
                            agent.navigationAgent.Position = recoverPos;
                            agent.navigationAgent.Enabled = true;
                        }
                    }

                    if (!agent.IsOnNavMesh)
                    {
                        if (enableDebugLogging)
                            Debug.LogWarning($"[RTS] Follow: Agent {agentIndex} not on NavMesh, skipping.");
                        continue;
                    }
                }

                validCount++;
                if (matchFormationSpeedToSlowest)
                {
                    float speed = GetBaselineFormationSpeed(agent);
                    if (speed > 0f && speed < slowestSpeed)
                        slowestSpeed = speed;
                }
            }

            if (validCount == 0) return;

            // --- Calculate formation positions around the target ---
            // Respects the current FormationControlMode (CombinedFormation vs BattleLine)
            Dictionary<int, Vector3> battleLinePositions = null;
            if (formationControlMode == FormationControlMode.BattleLine)
            {
                float blSlowest;
                battleLinePositions = CalculateBattleLinePositions(targetPos, 0f, agents, out blSlowest);
                if (matchFormationSpeedToSlowest && blSlowest < float.MaxValue)
                    slowestSpeed = blSlowest;
            }

            bool useSpeedMatch = matchFormationSpeedToSlowest && slowestSpeed < float.MaxValue;
            int positionIndex = 0;
            int count = 0;

            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                if (!IsAgentPlayerControlled(agentIndex))
                    continue;

                if (!agent.IsOnNavMesh)
                    continue;

                // Calculate this agent's formation position around the target
                Vector3 formationPos;
                if (battleLinePositions != null && battleLinePositions.TryGetValue(agentIndex, out Vector3 blPos))
                    formationPos = blPos;
                else
                    formationPos = CalculateFormationPosition(positionIndex, validCount, targetPos);

                // Store the offset from target so UpdateFollowTargets can reapply it as the target moves
                agent.followFormationOffset = formationPos - targetPos;

                // Set follow state
                agent.followTargetTransform = target;
                agent.isFollowing = true;

                // Clear attack / combat state so agent doesn't fight while following
                agent.attackTargetAgentIndex = -1;
                agent.attackTargetTransform = null;
                agent.hasAttackTargetPosition = false;
                agent.isChasing = false;
                agent.isAggroed = false;
                agent.combatState = CrowdCombatState.Idle;

                // Set move intent so the crowd system knows this agent wants to move
                agent.SetMoveIntent();

                // Put agent into ManualCommand so formation/combat systems don't override
                agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                agent.hasManualCommand = true;
                agent.hasRTSFormationPosition = false;
                agent.brokeFromRTSFormation = false;
                agent.hasFormationFacing = false;
                agent.moveCommandIssuedTime = Time.time;

                if (useSpeedMatch)
                    ApplyFormationSpeedOverride(agent, slowestSpeed);

                // Resume navigation if it was stopped
                if (agent.navigationAgent.IsStopped)
                    agent.navigationAgent.IsStopped = false;

                // Sample NavMesh and set destination to formation position
                Vector3 destPos = formationPos;
                if (agent.navigationAgent.SamplePosition(formationPos, out Vector3 sampledPos, formationSpacing * 2f))
                    destPos = sampledPos;

                agent.manualCommandDestination = destPos;
                agent.navigationAgent.SetDestination(destPos);

                positionIndex++;
                count++;
            }

            // Force an immediate follow update
            _nextFollowUpdateTime = 0f;

            if (enableDebugLogging)
                Debug.Log($"[RTS] Follow command: {count} agents now following {target.name} in " +
                          $"{formationControlMode} formation");
        }

        /// <summary>
        /// Checks whether a Transform qualifies as a valid follow target.
        /// Valid if it has a followable tag OR is one of the CrowdController's Player Targets.
        /// </summary>
        public bool IsValidFollowTarget(Transform candidate)
        {
            if (candidate == null) return false;

            // Check Player Targets on the CrowdController
            if (crowdController != null && crowdController.IsPlayerTargetTransform(candidate))
                return true;

            // Check followable tags
            for (int i = 0; i < followableTags.Count; i++)
            {
                if (candidate.CompareTag(followableTags[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves a follow target from a screen click ray.
        /// First checks physics raycast for collider-based targets (Player Targets, tagged objects).
        /// Then falls back to ground raycast + agent proximity detection for collider-less crowd agents.
        /// </summary>
        /// <returns>True if a valid follow target was found.</returns>
        public bool TryResolveFollowTarget(Ray ray, out Transform followTarget)
        {
            followTarget = null;
            if (crowdController == null)
            {
                Debug.LogWarning("[FollowResolve] crowdController is null — cannot resolve follow target.");
                return false;
            }

            Debug.Log($"[FollowResolve] Attempting to resolve follow target. followTargetMask={(int)followTargetMask}, " +
                      $"PlayerTargets count={crowdController.PlayerTargets?.Count ?? 0}, " +
                      $"followableTags=[{string.Join(", ", followableTags)}]");

            // Cached hit points for Phase 3 fallback
            bool _phase1HitCached = false;
            Vector3 _phase1HitPoint = Vector3.zero;
            bool _phase2GroundHitCached = false;
            Vector3 _phase2GroundHitPoint = Vector3.zero;

            // Phase 1: SphereCast for collider-based targets (Player Targets, tagged objects)
            // SphereCast sweeps a sphere of followTargetClickRadius along the ray, catching
            // colliders that a thin ray would miss at oblique camera angles.
            if (Physics.SphereCast(ray, followTargetClickRadius, out RaycastHit hit, 1000f, followTargetMask))
            {
                _phase1HitCached = true;
                _phase1HitPoint = hit.point;

                Debug.Log($"[FollowResolve] Phase 1 — SphereCast (r={followTargetClickRadius}) hit collider '{hit.collider.name}' " +
                          $"on GameObject '{hit.collider.gameObject.name}' (layer={hit.collider.gameObject.layer}, " +
                          $"tag='{hit.collider.gameObject.tag}') at {hit.point}");

                // Walk up Transform hierarchy to find a valid follow target
                Transform candidate = hit.collider.transform;
                int depth = 0;
                while (candidate != null)
                {
                    bool isPlayerTarget = crowdController.IsPlayerTargetTransform(candidate);
                    bool hasFollowTag = false;
                    string candidateTag = "";
                    try { candidateTag = candidate.tag; } catch { candidateTag = "<untagged>"; }
                    for (int i = 0; i < followableTags.Count; i++)
                    {
                        try { if (candidate.CompareTag(followableTags[i])) { hasFollowTag = true; break; } } catch { }
                    }

                    Debug.Log($"[FollowResolve]   hierarchy[{depth}]: '{candidate.name}' — " +
                              $"IsPlayerTarget={isPlayerTarget}, hasFollowTag={hasFollowTag}, tag='{candidateTag}'");

                    if (IsValidFollowTarget(candidate))
                    {
                        Debug.Log($"[FollowResolve] ✓ Valid follow target found via hierarchy walk: '{candidate.name}'");
                        followTarget = candidate;
                        return true;
                    }
                    candidate = candidate.parent;
                    depth++;
                }

                Debug.Log("[FollowResolve] Hierarchy walk found no valid target. Checking if hit is a crowd agent...");

                // Collider hit but not a tagged/PlayerTarget — check if it's an agent via GetAgentIndexFromHit
                int hitAgentIndex = GetAgentIndexFromHit(hit);
                Debug.Log($"[FollowResolve] GetAgentIndexFromHit returned: {hitAgentIndex}");
                if (hitAgentIndex >= 0)
                {
                    Transform agentTransform = ResolveFollowableAgentTransform(hitAgentIndex);
                    if (agentTransform != null)
                    {
                        Debug.Log($"[FollowResolve] ✓ Valid crowd agent follow target: '{agentTransform.name}' (index {hitAgentIndex})");
                        followTarget = agentTransform;
                        return true;
                    }
                    else
                    {
                        Debug.Log($"[FollowResolve] Agent index {hitAgentIndex} resolved but transform was null or agent is selected/dead.");
                    }
                }
            }
            else
            {
                Debug.Log($"[FollowResolve] Phase 1 — SphereCast (r={followTargetClickRadius}) with followTargetMask={(int)followTargetMask} hit NOTHING.");
            }

            // Phase 2: Ground raycast + proximity detection for collider-less crowd agents
            Debug.Log($"[FollowResolve] Phase 2 — Ground raycast with groundMask={(int)groundMask}...");
            if (Physics.Raycast(ray, out RaycastHit groundHit, 1000f, groundMask))
            {
                _phase2GroundHitCached = true;
                _phase2GroundHitPoint = groundHit.point;

                Debug.Log($"[FollowResolve] Ground hit at {groundHit.point} on '{groundHit.collider.name}'. " +
                          $"Searching for nearest followable agent within radius {followTargetClickRadius}...");
                int nearestIndex = FindNearestFollowableAgentAtPoint(groundHit.point, followTargetClickRadius);
                Debug.Log($"[FollowResolve] Nearest followable agent index: {nearestIndex}");
                if (nearestIndex >= 0)
                {
                    Transform agentTransform = ResolveFollowableAgentTransform(nearestIndex);
                    if (agentTransform != null)
                    {
                        Debug.Log($"[FollowResolve] ✓ Valid ground-proximity agent follow target: '{agentTransform.name}' (index {nearestIndex})");
                        followTarget = agentTransform;
                        return true;
                    }
                }
            }
            else
            {
                Debug.Log("[FollowResolve] Phase 2 — Ground raycast hit NOTHING.");
            }

            // Phase 3: Proximity check against registered Player Targets
            // Player Targets have colliders but the ray may miss them (angled camera, small click offset).
            // Use the best known world point from Phase 1 or Phase 2 hit to find nearby Player Targets.
            // Prefer groundHit (Phase 2) as it gives the most accurate XZ reference on the play surface.
            // Fall back to Phase 1 hit, then to a fresh raycast with combined masks.
            Vector3 searchPoint = Vector3.zero;
            bool hasSearchPoint = false;

            if (_phase2GroundHitCached)
            {
                searchPoint = _phase2GroundHitPoint;
                hasSearchPoint = true;
            }
            else if (_phase1HitCached)
            {
                searchPoint = _phase1HitPoint;
                hasSearchPoint = true;
            }
            else if (Physics.Raycast(ray, out RaycastHit anyHit, 1000f, followTargetMask | groundMask))
            {
                searchPoint = anyHit.point;
                hasSearchPoint = true;
            }

            if (hasSearchPoint && crowdController.PlayerTargets != null)
            {
                Debug.Log($"[FollowResolve] Phase 3 — Checking Player Target proximity from {searchPoint} within radius {followTargetClickRadius}...");
                Transform nearestPT = null;
                float nearestPTDistSq = followTargetClickRadius * followTargetClickRadius;

                for (int i = 0; i < crowdController.PlayerTargets.Count; i++)
                {
                    Transform pt = crowdController.PlayerTargets[i];
                    if (pt == null) continue;

                    Vector3 ptPos = pt.position;
                    float dx = ptPos.x - searchPoint.x;
                    float dz = ptPos.z - searchPoint.z;
                    float distSq = dx * dx + dz * dz;

                    Debug.Log($"[FollowResolve]   PlayerTarget[{i}] '{pt.name}' at {ptPos}, distXZ={Mathf.Sqrt(distSq):F2}");

                    if (distSq < nearestPTDistSq)
                    {
                        nearestPTDistSq = distSq;
                        nearestPT = pt;
                    }
                }

                if (nearestPT != null)
                {
                    Debug.Log($"[FollowResolve] ✓ Player Target found via proximity: '{nearestPT.name}' (dist={Mathf.Sqrt(nearestPTDistSq):F2})");
                    followTarget = nearestPT;
                    return true;
                }
                else
                {
                    Debug.Log("[FollowResolve] Phase 3 — No Player Target within radius.");
                }
            }

            // Log registered Player Targets for comparison
            if (crowdController.PlayerTargets != null)
            {
                for (int i = 0; i < crowdController.PlayerTargets.Count; i++)
                {
                    var pt = crowdController.PlayerTargets[i];
                    if (pt != null)
                    {
                        Debug.Log($"[FollowResolve] Registered PlayerTarget[{i}]: '{pt.name}' " +
                                  $"(layer={pt.gameObject.layer}, tag='{pt.gameObject.tag}', instanceID={pt.GetInstanceID()})");
                    }
                    else
                    {
                        Debug.Log($"[FollowResolve] Registered PlayerTarget[{i}]: NULL");
                    }
                }
            }

            Debug.Log("[FollowResolve] ✗ No valid follow target found.");
            return false;
        }

        /// <summary>
        /// Finds the nearest alive crowd agent to a world point that is valid for following.
        /// Excludes currently selected agents (you can't follow yourself) and dead agents.
        /// </summary>
        private int FindNearestFollowableAgentAtPoint(Vector3 point, float maxDistance)
        {
            var agents = crowdController.GetAgents();
            int nearestIndex = -1;
            float nearestDistSq = maxDistance * maxDistance;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                // Exclude currently selected agents — can't follow yourself
                if (_selectedAgentIndices.Contains(i))
                    continue;

                Vector3 agentPos = agent.WorldPosition;
                float dx = agentPos.x - point.x;
                float dz = agentPos.z - point.z;
                float distSq = dx * dx + dz * dz;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        /// <summary>
        /// Gets a stable Transform for a crowd agent to use as a follow target.
        /// Returns null if the agent is dead, selected, or has no valid transform.
        /// </summary>
        private Transform ResolveFollowableAgentTransform(int agentIndex)
        {
            if (crowdController == null) return null;
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count) return null;

            CrowdAgent agent = agents[agentIndex];
            if (agent == null || agent.isDead) return null;

            // Exclude currently selected agents
            if (_selectedAgentIndices.Contains(agentIndex))
                return null;

            // Get the underlying nav agent transform (stable identity)
            Transform agentTransform = agent.navigationAgent?.GetUnderlyingAgent<Component>()?.transform;
            return agentTransform;
        }

        /// <summary>
        /// Per-frame update that refreshes NavMesh destinations for all agents in follow mode.
        /// Called from Update() at a configurable interval to balance responsiveness and performance.
        /// </summary>
        private void UpdateFollowTargets()
        {
            if (!enableFollowCommand) return;
            if (Time.time < _nextFollowUpdateTime) return;
            _nextFollowUpdateTime = Time.time + followUpdateInterval;

            var agents = crowdController.GetAgents();
            float stopDistSq = followStopDistance * followStopDistance;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || !agent.isFollowing || agent.isDead)
                    continue;

                // If the follow target was destroyed, cancel follow
                if (agent.followTargetTransform == null)
                {
                    agent.isFollowing = false;
                    agent.followFormationOffset = Vector3.zero;
                    agent.hasManualCommand = false;
                    agent.ForceSetNavigationState(AgentNavigationState.Formation);
                    continue;
                }

                // Apply formation offset to the target's current position
                Vector3 targetPos = agent.followTargetTransform.position + agent.followFormationOffset;
                Vector3 agentPos = agent.WorldPosition;
                float distSq = (targetPos - agentPos).sqrMagnitude;

                // If close enough to their formation slot, stop but stay in follow mode
                if (distSq <= stopDistSq)
                {
                    if (agent.navigationAgent != null && agent.navigationAgent.IsOnNavMesh)
                    {
                        agent.navigationAgent.ResetPath();
                    }
                    continue;
                }

                // Update destination toward formation position around target
                agent.manualCommandDestination = targetPos;
                if (agent.navigationAgent != null && agent.navigationAgent.IsOnNavMesh)
                {
                    // Ensure navigation is not stopped (may have been paused when within stopDistance)
                    if (agent.navigationAgent.IsStopped)
                        agent.navigationAgent.IsStopped = false;
                    agent.navigationAgent.SetDestination(targetPos);
                }
            }
        }

        /// <summary>
        /// Stop a specific agent immediately at their current position.
        /// </summary>
        public void StopAgent(int agentIndex)
        {
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count)
                return;

            CrowdAgent agent = agents[agentIndex];
            if (agent == null || agent.isDead || agent.navigationAgent == null)
                return;

            RestoreFormationSpeedOverride(agentIndex, agent);

            if (!agent.IsOnNavMesh)
                return;

            // Stop the navigation agent immediately
            agent.navigationAgent.ResetPath();
            agent.navigationAgent.Velocity = Vector3.zero;
            
            // Clear manual command state
            agent.hasManualCommand = false;
            
            // Clear RTS formation position flags
            agent.hasRTSFormationPosition = false;
            agent.brokeFromRTSFormation = false;
            
            // Clear attack target
            agent.attackTargetAgentIndex = -1;
            agent.attackTargetTransform = null;
            agent.hasAttackTargetPosition = false;
            agent.isChasing = false;
            agent.ClearCommandIntent();
            
            // Clear any queued manual skill
            agent.queuedSkillSlot = -1;
            agent.queuedSkillTargetIndex = -1;
            agent.queuedSkillGroundTarget = null;
            agent.queuedSkillDamageableTarget = null;
            
            // For HoldPosition agents, update hold position to current location
            if (agent.combatStance == CombatStance.HoldPosition)
            {
                agent.holdPositionLocation = agent.WorldPosition;
                agent.hasHoldPositionSet = true;
                agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
            }
            else
            {
                agent.ForceSetNavigationState(AgentNavigationState.Formation);
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Agent {agentIndex}: Stopped at {agent.WorldPosition}");
        }

        /// <summary>
        /// Stop all agents in a specific list immediately at their current positions.
        /// </summary>
        public void StopAgents(List<int> agentIndices)
        {
            if (agentIndices == null || agentIndices.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            
            foreach (int agentIndex in agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                RestoreFormationSpeedOverride(agentIndex, agent);

                if (!agent.IsOnNavMesh)
                    continue;

                agent.navigationAgent.ResetPath();
                agent.navigationAgent.Velocity = Vector3.zero;
                agent.hasManualCommand = false;
                agent.hasRTSFormationPosition = false;
                agent.brokeFromRTSFormation = false;
                agent.attackTargetAgentIndex = -1;
                agent.attackTargetTransform = null;
                agent.hasAttackTargetPosition = false;
                agent.isChasing = false;
                agent.ClearCommandIntent();
                
                if (agent.combatStance == CombatStance.HoldPosition)
                {
                    agent.holdPositionLocation = agent.WorldPosition;
                    agent.hasHoldPositionSet = true;
                    agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                }
                else
                {
                    agent.ForceSetNavigationState(AgentNavigationState.Formation);
                }
            }
        }

        /// <summary>
        /// Calculates a formation position by delegating to the shared <see cref="EnemyMassesFormationSystem"/>
        /// on the crowd controller. This ensures spawning and RTS move commands use identical formation math.
        /// Falls back to a simple offset when the crowd controller is not yet available.
        /// </summary>
        private Vector3 CalculateFormationPosition(int index, int totalCount, Vector3 center)
        {
            if (totalCount <= 1)
                return center;

            EnemyMassesFormationSystem formationSystem = crowdController != null
                ? crowdController.FormationSystem
                : null;

            if (formationSystem != null)
            {
                return formationSystem.CalculateFormationPosition(
                    index, totalCount, center, moveFormation, formationSpacing);
            }

            // Fallback: simple grid when the formation system is unavailable
            int side = Mathf.CeilToInt(Mathf.Sqrt(totalCount));
            int row = index / side;
            int col = index % side;
            float offsetX = (col - (side - 1) * 0.5f) * formationSpacing;
            float offsetZ = (row - (side - 1) * 0.5f) * formationSpacing;
            return center + new Vector3(offsetX, 0f, offsetZ);
        }

        #endregion

        #region Optimal Formation Assignment

        /// <summary>
        /// Calculates optimal agent-to-position assignments that minimize total travel distance.
        /// Uses a greedy algorithm for O(n²) performance instead of O(n³) Hungarian algorithm.
        /// </summary>
        /// <param name="agentPositions">Current world positions of agents (indexed by agent order in selection)</param>
        /// <param name="formationPositions">Target formation positions (indexed by formation slot)</param>
        /// <returns>Array where result[agentIndex] = formationPositionIndex that agent should go to</returns>
        private int[] CalculateOptimalFormationAssignment(Vector3[] agentPositions, Vector3[] formationPositions)
        {
            int count = agentPositions.Length;
            if (count != formationPositions.Length || count == 0)
            {
                // Return identity mapping if counts don't match
                int[] identity = new int[count];
                for (int i = 0; i < count; i++) identity[i] = i;
                return identity;
            }

            int[] assignments = new int[count];
            bool[] positionTaken = new bool[count];
            
            // For each agent, find the closest available position
            // Process agents in order of their distance to their closest available position (greedy)
            
            // First, calculate all distances
            float[,] distances = new float[count, count];
            for (int agent = 0; agent < count; agent++)
            {
                for (int pos = 0; pos < count; pos++)
                {
                    // Use squared distance for performance (no need for sqrt for comparison)
                    Vector3 diff = formationPositions[pos] - agentPositions[agent];
                    distances[agent, pos] = diff.x * diff.x + diff.z * diff.z; // Ignore Y for ground distance
                }
            }

            // Greedy assignment: repeatedly assign the globally shortest unassigned agent-position pair
            for (int i = 0; i < count; i++)
            {
                float bestDistance = float.MaxValue;
                int bestAgent = -1;
                int bestPosition = -1;

                // Find the shortest unassigned agent-position pair
                for (int agent = 0; agent < count; agent++)
                {
                    if (assignments[agent] != 0 && agent != 0) continue; // Skip if already assigned (use -1 marker)
                    // Actually need different approach - track which agents are assigned
                    
                    for (int pos = 0; pos < count; pos++)
                    {
                        if (positionTaken[pos]) continue;
                        
                        if (distances[agent, pos] < bestDistance)
                        {
                            bestDistance = distances[agent, pos];
                            bestAgent = agent;
                            bestPosition = pos;
                        }
                    }
                }

                // This approach has a bug - let me use a cleaner implementation
            }

            // Cleaner greedy implementation
            bool[] agentAssigned = new bool[count];
            for (int i = 0; i < count; i++) assignments[i] = -1;

            for (int iteration = 0; iteration < count; iteration++)
            {
                float bestDistance = float.MaxValue;
                int bestAgent = -1;
                int bestPosition = -1;

                for (int agent = 0; agent < count; agent++)
                {
                    if (agentAssigned[agent]) continue;
                    
                    for (int pos = 0; pos < count; pos++)
                    {
                        if (positionTaken[pos]) continue;
                        
                        if (distances[agent, pos] < bestDistance)
                        {
                            bestDistance = distances[agent, pos];
                            bestAgent = agent;
                            bestPosition = pos;
                        }
                    }
                }

                if (bestAgent >= 0 && bestPosition >= 0)
                {
                    assignments[bestAgent] = bestPosition;
                    agentAssigned[bestAgent] = true;
                    positionTaken[bestPosition] = true;
                }
            }

            return assignments;
        }

        /// <summary>
        /// Calculates all formation positions for the given count, applying optional rotation.
        /// </summary>
        private Vector3[] CalculateAllFormationPositions(int count, Vector3 center, float rotationAngle = 0f)
        {
            Vector3[] positions = new Vector3[count];
            Quaternion rotation = Quaternion.Euler(0f, rotationAngle, 0f);
            
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = CalculateFormationPosition(i, count, Vector3.zero);
                positions[i] = center + rotation * offset;
            }
            
            return positions;
        }

        /// <summary>
        /// Returns true if the current selection contains agents from more than one EnemyCrowd type.
        /// Used to decide whether drag-to-adjust should control the battle line group gap or formation spacing.
        /// </summary>
        private bool HasMultipleCrowdTypesSelected()
        {
            if (_selectedAgentIndices.Count < 2) return false;
            var agents = crowdController?.GetAgents();
            if (agents == null) return false;

            int firstCrowdIndex = -1;
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead) continue;
                if (firstCrowdIndex < 0)
                {
                    firstCrowdIndex = agent.enemyCrowdIndex;
                }
                else if (agent.enemyCrowdIndex != firstCrowdIndex)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Calculates formation positions in Battle Line mode.
        /// Groups agents by their EnemyCrowd type, computes each group's individual formation
        /// using its EnemyCrowd.formationType, then arranges the groups side by side.
        /// Returns a dictionary mapping agent index → world position.
        /// Also outputs the slowest speed across all agents when speed matching is enabled.
        /// </summary>
        private Dictionary<int, Vector3> CalculateBattleLinePositions(
            Vector3 destination,
            float rotationAngle,
            List<CrowdAgent> agents,
            out float slowestSpeed,
            float groupGapOverride = -1f)
        {
            slowestSpeed = float.MaxValue;
            var result = new Dictionary<int, Vector3>();

            // Group selected agents by enemyCrowdIndex
            var groups = new SortedDictionary<int, List<int>>(); // crowdIndex → list of agent indices
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null || !agent.IsOnNavMesh)
                    continue;

                if (!groups.TryGetValue(agent.enemyCrowdIndex, out var list))
                {
                    list = new List<int>();
                    groups[agent.enemyCrowdIndex] = list;
                }
                list.Add(agentIndex);

                if (matchFormationSpeedToSlowest)
                {
                    float speed = GetBaselineFormationSpeed(agent);
                    if (speed > 0f && speed < slowestSpeed)
                        slowestSpeed = speed;
                }
            }

            if (groups.Count == 0) return result;

            EnemyMassesFormationSystem formationSystem = crowdController?.FormationSystem;
            Quaternion rotation = Quaternion.Euler(0f, rotationAngle, 0f);

            // Calculate each group's formation dimensions so we can arrange them side by side.
            // Each group's width is derived from: spacing * cols (where cols ≈ sqrt(count)).
            // We lay groups out along the local X axis (perpendicular to the facing/advance direction).
            var groupEntries = new List<(int crowdIndex, List<int> agentIndices, float width, FormationType formation, float spacing)>();
            float totalWidth = 0f;

            foreach (var kvp in groups)
            {
                int crowdIndex = kvp.Key;
                List<int> agentIndices = kvp.Value;
                int count = agentIndices.Count;
                
                // Determine formation type for this group: use runtime override > SO override > SO default > global fallback
                FormationType groupFormation = moveFormation; // fallback
                float groupSpacing = GetEffectiveCrowdSpacing(crowdIndex);
                if (_crowdFormationOverrides.TryGetValue(crowdIndex, out FormationType runtimeFormation))
                {
                    groupFormation = runtimeFormation;
                }
                else if (crowdIndex >= 0 && crowdIndex < crowdController.EnemyCrowds.Count)
                {
                    EnemyCrowd crowd = crowdController.EnemyCrowds[crowdIndex];
                    if (crowd != null && crowd.overrideFormation)
                    {
                        groupFormation = crowd.formationTypeOverride;
                    }
                    else if (crowd != null)
                    {
                        groupFormation = crowd.formationType;
                    }
                }

                // Estimate group width: columns * spacing
                int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
                float groupWidth = cols * groupSpacing;
                
                groupEntries.Add((crowdIndex, agentIndices, groupWidth, groupFormation, groupSpacing));
                totalWidth += groupWidth;
            }

            // Add gaps between groups
            float gapBetweenGroups = groupGapOverride >= 0f ? groupGapOverride : battleLineGroupGap;
            totalWidth += Mathf.Max(0, groupEntries.Count - 1) * gapBetweenGroups;

            // Lay out groups along the local X axis, centered on the destination.
            // The local X axis is perpendicular to the advance direction (rotation).
            float currentX = -totalWidth * 0.5f;

            foreach (var entry in groupEntries)
            {
                int count = entry.agentIndices.Count;
                // Center of this group in local space
                float groupCenterX = currentX + entry.width * 0.5f;
                Vector3 localGroupCenter = new Vector3(groupCenterX, 0f, 0f);
                Vector3 worldGroupCenter = destination + rotation * localGroupCenter;

                // Calculate individual formation positions for this group
                for (int i = 0; i < count; i++)
                {
                    int agentIndex = entry.agentIndices[i];
                    Vector3 pos;
                    if (formationSystem != null)
                    {
                        pos = formationSystem.CalculateFormationPosition(
                            i, count, worldGroupCenter, entry.formation, entry.spacing, rotation);
                    }
                    else
                    {
                        // Fallback: simple grid
                        int side = Mathf.CeilToInt(Mathf.Sqrt(count));
                        int row = i / side;
                        int col = i % side;
                        float offsetX = (col - (side - 1) * 0.5f) * entry.spacing;
                        float offsetZ = (row - (side - 1) * 0.5f) * entry.spacing;
                        pos = worldGroupCenter + rotation * new Vector3(offsetX, 0f, offsetZ);
                    }
                    result[agentIndex] = pos;
                }

                currentX += entry.width + gapBetweenGroups;
            }

            return result;
        }

        /// <summary>
        /// Gets current world positions for the given agent indices.
        /// </summary>
        private Vector3[] GetAgentCurrentPositions(List<int> agentIndices, List<CrowdAgent> agents)
        {
            Vector3[] positions = new Vector3[agentIndices.Count];
            
            for (int i = 0; i < agentIndices.Count; i++)
            {
                int agentIndex = agentIndices[i];
                if (agentIndex >= 0 && agentIndex < agents.Count)
                {
                    CrowdAgent agent = agents[agentIndex];
                    if (agent != null && agent.navigationAgent != null)
                    {
                        positions[i] = agent.WorldPosition;
                    }
                }
            }
            
            return positions;
        }

        #endregion

        #region Destination Indicators

        /// <summary>
        /// Activates the destination indicator at the specified position for the given agents.
        /// Stores per-agent destinations so indicators work correctly when selection changes.
        /// Also tracks agent destinations for arrival detection (formation facing) even if indicators are disabled.
        /// </summary>
        private void ActivateDestinationIndicator(Vector3 destination, List<int> agentIndices)
        {
            bool hasInternalSelection = agentIndices != null && agentIndices.Count > 0;
            bool hasExternalSelection = _selectedExternalSelectables.Count > 0;

            if (!hasInternalSelection && !hasExternalSelection)
                return;

            // ALWAYS track agent destinations for arrival detection (formation facing feature)
            // This must happen even if visual indicators are disabled
            float currentTime = Time.time;

            if (hasInternalSelection)
            {
                foreach (int agentIndex in agentIndices)
                {
                    _agentDestinations[agentIndex] = destination;
                    _agentDestinationSetTime[agentIndex] = currentTime;
                }
            }

            if (hasExternalSelection)
            {
                foreach (var selectable in _selectedExternalSelectables)
                {
                    if (selectable == null) continue;
                    _externalDestinations[selectable] = destination;
                    _externalDestinationSetTime[selectable] = currentTime;
                }
            }

            // Early exit if visual indicators are disabled - but we've already tracked destinations above
            if (!showDestinationIndicator || destinationIndicatorPrefab == null)
                return;

            // Reset the first-arrival hidden flag since we have a new command
            _indicatorsHiddenByFirstArrival = false;
            
            // Cancel any in-progress fade-out since we have a new command
            CancelIndicatorFadeOut();

            // Directly set the active destination since we just commanded these agents
            _activeDestination = destination;
            _hasActiveDestinationForSelection = true;
            
            // Reset path interpolation so line starts fresh from selection center
            ResetPathInterpolation();
            
            // Update which selected agents have destinations
            _selectedAgentsWithDestination.Clear();
            _selectedExternalWithDestination.Clear();

            if (hasInternalSelection)
            {
                foreach (int agentIndex in agentIndices)
                {
                    if (_selectedAgentIndices.Contains(agentIndex))
                    {
                        _selectedAgentsWithDestination.Add(agentIndex);
                    }
                }
            }

            if (hasExternalSelection)
            {
                foreach (var selectable in _selectedExternalSelectables)
                {
                    if (selectable != null)
                        _selectedExternalWithDestination.Add(selectable);
                }
            }

            // Spawn indicator prefabs if needed
            EnsureIndicatorInstancesExist();

            // Show indicators
            if (_destinationIndicatorInstance != null)
            {
                _destinationIndicatorInstance.SetActive(true);
                UpdateDestinationIndicatorPosition();
            }

            if (showDirectionIndicator && _directionIndicatorInstance != null)
            {
                _directionIndicatorInstance.SetActive(true);
                UpdateDirectionIndicator();
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS] Destination indicator activated at {destination} for {agentIndices.Count} agents");
        }

        /// <summary>
        /// Ensures destination and direction indicator instances exist (lazy instantiation).
        /// </summary>
        private void EnsureIndicatorInstancesExist()
        {
            if (_destinationIndicatorInstance == null && destinationIndicatorPrefab != null)
            {
                Transform parent = destinationIndicatorParent != null ? destinationIndicatorParent : transform;
                _destinationIndicatorInstance = Instantiate(destinationIndicatorPrefab, parent);
                ConfigureIndicatorForFogVisibility(_destinationIndicatorInstance);
            }

            if (_directionIndicatorInstance == null && directionIndicatorPrefab != null && showDirectionIndicator)
            {
                Transform parent = directionIndicatorParent != null ? directionIndicatorParent : transform;
                _directionIndicatorInstance = Instantiate(directionIndicatorPrefab, parent);
                ConfigureIndicatorForFogVisibility(_directionIndicatorInstance);
                
                // Try to find LineRenderer component if useLineRenderer is enabled
                if (useLineRenderer)
                {
                    _directionLineRenderer = _directionIndicatorInstance.GetComponent<LineRenderer>();
                    if (_directionLineRenderer == null)
                    {
                        _directionLineRenderer = _directionIndicatorInstance.GetComponentInChildren<LineRenderer>();
                    }
                    
                    if (_directionLineRenderer != null)
                    {
                        // Initialize the NavMeshPath for path calculations
                        _lineRendererPath = new NavMeshPath();
                    }
                    else if (enableDebugLogging)
                    {
                        Debug.LogWarning("[RTS] useLineRenderer is enabled but no LineRenderer component found in direction indicator prefab!");
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the destination state based on currently selected agents.
        /// Only shows indicators if at least one selected agent has an active destination.
        /// </summary>
        private void RefreshDestinationStateForSelection()
        {
            _selectedAgentsWithDestination.Clear();
            _selectedExternalWithDestination.Clear();
            _hasActiveDestinationForSelection = false;
            
            // Find all selected agents that have destinations
            Vector3? commonDestination = null;
            
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (_agentDestinations.TryGetValue(agentIndex, out Vector3 dest))
                {
                    _selectedAgentsWithDestination.Add(agentIndex);
                    
                    // Use the first destination found (or check if they share a common one)
                    if (!commonDestination.HasValue)
                    {
                        commonDestination = dest;
                    }
                    else if (commonDestination.Value != dest)
                    {
                        // Multiple different destinations - use center point or just the first
                        // For simplicity, we use the first destination found
                    }
                }
            }

            // External selectables
            foreach (var selectable in _selectedExternalSelectables)
            {
                if (selectable == null)
                    continue;

                if (_externalDestinations.TryGetValue(selectable, out Vector3 dest))
                {
                    _selectedExternalWithDestination.Add(selectable);

                    if (!commonDestination.HasValue)
                    {
                        commonDestination = dest;
                    }
                    else if (commonDestination.Value != dest)
                    {
                        // Keep first destination for indicator positioning
                    }
                }
            }
            
            if ((_selectedAgentsWithDestination.Count > 0 || _selectedExternalWithDestination.Count > 0) && commonDestination.HasValue)
            {
                _activeDestination = commonDestination.Value;
                _hasActiveDestinationForSelection = true;
            }
        }

        /// <summary>
        /// Called when selection changes to update indicator visibility.
        /// </summary>
        private void OnSelectionChangedUpdateIndicators()
        {
            RefreshDestinationStateForSelection();
            
            if (_hasActiveDestinationForSelection)
            {
                EnsureIndicatorInstancesExist();
                
                if (_destinationIndicatorInstance != null)
                {
                    _destinationIndicatorInstance.SetActive(true);
                    UpdateDestinationIndicatorPosition();
                }
                
                if (showDirectionIndicator && _directionIndicatorInstance != null)
                {
                    _directionIndicatorInstance.SetActive(true);
                    UpdateDirectionIndicator();
                }
            }
            else
            {
                // No selected agents have destinations - hide indicators
                if (_destinationIndicatorInstance != null)
                    _destinationIndicatorInstance.SetActive(false);
                    
                if (_directionIndicatorInstance != null)
                    _directionIndicatorInstance.SetActive(false);
            }
        }

        /// <summary>
        /// Configures an indicator to render above the fog of war plane.
        /// </summary>
        private void ConfigureIndicatorForFogVisibility(GameObject indicator)
        {
            if (indicator == null)
                return;

            // Configure renderers to render above fog
            if (renderAboveFog)
            {
                // Handle MeshRenderers (VFX, particles, 3D objects)
                foreach (var renderer in indicator.GetComponentsInChildren<Renderer>(true))
                {
                    // Set render queue to Overlay (4000) to render above fog plane (typically 3000)
                    // Also set ZTest to Always since fog shader uses ZTest Always
                    foreach (var mat in renderer.materials)
                    {
                        mat.renderQueue = 4000;
                        // Disable depth testing so indicator renders over fog (fog uses ZTest Always)
                        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                        // Ensure we don't write to depth buffer
                        mat.SetInt("_ZWrite", 0);
                    }
                    
                    // Set sorting order for sprite renderers
                    if (renderer is SpriteRenderer spriteRenderer)
                    {
                        spriteRenderer.sortingOrder = indicatorSortingOrder;
                    }
                }

                // Handle UI Canvas elements
                var canvas = indicator.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.sortingOrder = indicatorSortingOrder;
                }
                
                // Handle Canvas in children (for prefabs with nested canvas)
                foreach (var childCanvas in indicator.GetComponentsInChildren<Canvas>(true))
                {
                    childCanvas.sortingOrder = indicatorSortingOrder;
                }
            }
        }

        /// <summary>
        /// Deactivates the destination indicator (hides but doesn't clear agent destinations).
        /// If fade duration is set, starts a fade-out animation instead of instant hide.
        /// </summary>
        private void DeactivateDestinationIndicator()
        {
            _hasActiveDestinationForSelection = false;
            _selectedAgentsWithDestination.Clear();
            _selectedExternalWithDestination.Clear();
            
            // Reset path interpolation state
            ResetPathInterpolation();

            // Start fade-out if duration is set, otherwise hide instantly
            if (indicatorFadeOutDuration > 0f)
            {
                StartIndicatorFadeOut();
            }
            else
            {
                HideIndicatorsInstantly();
            }

            if (enableDebugLogging)
                Debug.Log("[RTS] Destination indicator deactivated");
        }

        /// <summary>
        /// Immediately hides indicators without fade animation.
        /// </summary>
        private void HideIndicatorsInstantly()
        {
            _isFadingOutIndicators = false;
            _currentFadeAlpha = 1f;
            
            if (_destinationIndicatorInstance != null)
                _destinationIndicatorInstance.SetActive(false);

            if (_directionIndicatorInstance != null)
                _directionIndicatorInstance.SetActive(false);
        }

        /// <summary>
        /// Starts the fade-out animation for indicators.
        /// </summary>
        private void StartIndicatorFadeOut()
        {
            if (_isFadingOutIndicators)
                return; // Already fading
                
            _isFadingOutIndicators = true;
            _fadeOutStartTime = Time.time;
            _currentFadeAlpha = 1f;
            
            // Cache renderers and their original colors if not already cached
            CacheIndicatorRenderers();
        }

        /// <summary>
        /// Caches renderers from indicator prefabs for alpha manipulation during fade.
        /// </summary>
        private void CacheIndicatorRenderers()
        {
            // Cache destination indicator renderers and UI graphics
            if (_destinationIndicatorInstance != null && _destinationIndicatorRenderers.Count == 0 && _destinationIndicatorGraphics.Count == 0)
            {
                // Cache Renderers (3D objects)
                _destinationIndicatorRenderers.AddRange(_destinationIndicatorInstance.GetComponentsInChildren<Renderer>(true));
                foreach (var renderer in _destinationIndicatorRenderers)
                {
                    if (renderer.material != null && !_originalColors.ContainsKey(renderer))
                    {
                        _originalColors[renderer] = renderer.material.color;
                    }
                }
                
                // Cache UI Graphics (Image, RawImage, Text, etc.)
                _destinationIndicatorGraphics.AddRange(_destinationIndicatorInstance.GetComponentsInChildren<Graphic>(true));
                foreach (var graphic in _destinationIndicatorGraphics)
                {
                    if (!_originalGraphicColors.ContainsKey(graphic))
                    {
                        _originalGraphicColors[graphic] = graphic.color;
                    }
                }
            }
            
            // Cache direction indicator renderers and UI graphics
            if (_directionIndicatorInstance != null && _directionIndicatorRenderers.Count == 0 && _directionIndicatorGraphics.Count == 0)
            {
                // Cache Renderers (3D objects)
                _directionIndicatorRenderers.AddRange(_directionIndicatorInstance.GetComponentsInChildren<Renderer>(true));
                foreach (var renderer in _directionIndicatorRenderers)
                {
                    if (renderer.material != null && !_originalColors.ContainsKey(renderer))
                    {
                        _originalColors[renderer] = renderer.material.color;
                    }
                }
                
                // Cache UI Graphics (Image, RawImage, Text, etc.)
                _directionIndicatorGraphics.AddRange(_directionIndicatorInstance.GetComponentsInChildren<Graphic>(true));
                foreach (var graphic in _directionIndicatorGraphics)
                {
                    if (!_originalGraphicColors.ContainsKey(graphic))
                    {
                        _originalGraphicColors[graphic] = graphic.color;
                    }
                }
                
                // Also cache LineRenderer colors if present
                if (_directionLineRenderer != null)
                {
                    _originalLineRendererStartColor = _directionLineRenderer.startColor;
                    _originalLineRendererEndColor = _directionLineRenderer.endColor;
                }
            }
        }

        /// <summary>
        /// Updates the fade-out animation each frame.
        /// </summary>
        private void UpdateIndicatorFadeOut()
        {
            if (!_isFadingOutIndicators)
                return;
                
            float elapsed = Time.time - _fadeOutStartTime;
            float progress = Mathf.Clamp01(elapsed / indicatorFadeOutDuration);
            _currentFadeAlpha = 1f - progress;
            
            // Apply alpha to all renderers (3D objects)
            ApplyFadeAlphaToRenderers(_destinationIndicatorRenderers, _currentFadeAlpha);
            ApplyFadeAlphaToRenderers(_directionIndicatorRenderers, _currentFadeAlpha);
            
            // Apply alpha to all UI graphics (Image, RawImage, etc.)
            ApplyFadeAlphaToGraphics(_destinationIndicatorGraphics, _currentFadeAlpha);
            ApplyFadeAlphaToGraphics(_directionIndicatorGraphics, _currentFadeAlpha);
            
            // Apply alpha to LineRenderer if present
            if (_directionLineRenderer != null)
            {
                Color startColor = _originalLineRendererStartColor;
                Color endColor = _originalLineRendererEndColor;
                startColor.a *= _currentFadeAlpha;
                endColor.a *= _currentFadeAlpha;
                _directionLineRenderer.startColor = startColor;
                _directionLineRenderer.endColor = endColor;
            }
            
            // Fade complete - hide indicators
            if (progress >= 1f)
            {
                _isFadingOutIndicators = false;
                HideIndicatorsInstantly();
                
                // Restore original colors for next use
                RestoreIndicatorColors();
            }
        }

        /// <summary>
        /// Applies the fade alpha to a list of renderers.
        /// </summary>
        private void ApplyFadeAlphaToRenderers(List<Renderer> renderers, float alpha)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.material == null)
                    continue;
                    
                if (_originalColors.TryGetValue(renderer, out Color originalColor))
                {
                    Color newColor = originalColor;
                    newColor.a *= alpha;
                    renderer.material.color = newColor;
                }
            }
        }

        /// <summary>
        /// Applies the fade alpha to a list of UI Graphics (Image, RawImage, Text, etc.).
        /// </summary>
        private void ApplyFadeAlphaToGraphics(List<Graphic> graphics, float alpha)
        {
            foreach (var graphic in graphics)
            {
                if (graphic == null)
                    continue;
                    
                if (_originalGraphicColors.TryGetValue(graphic, out Color originalColor))
                {
                    Color newColor = originalColor;
                    newColor.a *= alpha;
                    graphic.color = newColor;
                }
            }
        }

        /// <summary>
        /// Restores original colors after fade completes.
        /// </summary>
        private void RestoreIndicatorColors()
        {
            // Restore Renderer colors (3D objects)
            foreach (var kvp in _originalColors)
            {
                if (kvp.Key != null && kvp.Key.material != null)
                {
                    kvp.Key.material.color = kvp.Value;
                }
            }
            
            // Restore UI Graphic colors (Image, RawImage, etc.)
            foreach (var kvp in _originalGraphicColors)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.color = kvp.Value;
                }
            }
            
            // Restore LineRenderer colors
            if (_directionLineRenderer != null)
            {
                _directionLineRenderer.startColor = _originalLineRendererStartColor;
                _directionLineRenderer.endColor = _originalLineRendererEndColor;
            }
        }

        /// <summary>
        /// Cancels any in-progress fade and restores full visibility.
        /// Called when a new move command is issued.
        /// </summary>
        private void CancelIndicatorFadeOut()
        {
            if (_isFadingOutIndicators)
            {
                _isFadingOutIndicators = false;
                _currentFadeAlpha = 1f;
                RestoreIndicatorColors();
            }
        }

        /// <summary>
        /// Removes destination tracking for specific agents (when they reach destination).
        /// </summary>
        private void ClearAgentDestinations(IEnumerable<int> agentIndices)
        {
            foreach (int agentIndex in agentIndices)
            {
                CrowdAgent agent = null;
                if (crowdController != null)
                {
                    var agents = crowdController.GetAgents();
                    if (agentIndex >= 0 && agentIndex < agents.Count)
                        agent = agents[agentIndex];
                }

                RestoreFormationSpeedOverride(agentIndex, agent);
                _agentDestinations.Remove(agentIndex);
                _agentDestinationSetTime.Remove(agentIndex);
            }
            
            // Refresh state in case current selection was affected
            RefreshDestinationStateForSelection();
        }

        private float GetBaselineFormationSpeed(CrowdAgent agent)
        {
            if (agent == null || agent.navigationAgent == null)
                return 0f;

            // Return the stored original baseSpeed if we already overrode this agent
            if (_formationSpeedOverrides.TryGetValue(agent.index, out float originalBaseSpeed))
                return originalBaseSpeed;

            // Use baseSpeed if the combat system has initialized it; otherwise read nav agent speed
            return agent.hasBaseSpeedSet ? agent.baseSpeed : agent.navigationAgent.Speed;
        }

        private void ApplyFormationSpeedOverride(CrowdAgent agent, float targetSpeed)
        {
            if (agent == null || agent.navigationAgent == null)
                return;

            // Save original baseSpeed so we can restore it later
            if (!_formationSpeedOverrides.ContainsKey(agent.index))
            {
                // Ensure baseSpeed is initialized before we save it
                if (!agent.hasBaseSpeedSet)
                {
                    agent.baseSpeed = agent.navigationAgent.Speed;
                    agent.hasBaseSpeedSet = true;
                }
                _formationSpeedOverrides[agent.index] = agent.baseSpeed;
            }

            // Override baseSpeed so the combat system's ApplySpeedMultipliers uses the formation speed
            agent.baseSpeed = Mathf.Max(0f, targetSpeed);

            // Apply immediately with current combat multipliers
            float combinedMultiplier = agent.allyRushSpeedMultiplier 
                                     * agent.chargeSpeedMultiplier 
                                     * agent.cachedMoveSpeedMultiplier;
            agent.navigationAgent.Speed = Mathf.Max(0f, targetSpeed * combinedMultiplier);
        }

        private void RestoreFormationSpeedOverride(int agentIndex, CrowdAgent agent)
        {
            if (!_formationSpeedOverrides.TryGetValue(agentIndex, out float originalBaseSpeed))
                return;

            if (agent != null && agent.navigationAgent != null)
            {
                // Restore original baseSpeed
                agent.baseSpeed = originalBaseSpeed;

                // Recalculate speed with current combat multipliers
                float combinedMultiplier = agent.allyRushSpeedMultiplier 
                                         * agent.chargeSpeedMultiplier 
                                         * agent.cachedMoveSpeedMultiplier;
                agent.navigationAgent.Speed = Mathf.Max(0f, originalBaseSpeed * combinedMultiplier);
            }

            _formationSpeedOverrides.Remove(agentIndex);
        }

        private void CleanupFormationSpeedOverrides()
        {
            if (_formationSpeedOverrides.Count == 0)
                return;

            List<int> toRestore = null;

            if (!matchFormationSpeedToSlowest)
            {
                toRestore = new List<int>(_formationSpeedOverrides.Keys);
            }
            else
            {
                var agents = crowdController.GetAgents();
                foreach (var kvp in _formationSpeedOverrides)
                {
                    int agentIndex = kvp.Key;
                    if (agentIndex < 0 || agentIndex >= agents.Count)
                    {
                        if (toRestore == null) toRestore = new List<int>();
                        toRestore.Add(agentIndex);
                        continue;
                    }

                    CrowdAgent agent = agents[agentIndex];
                    if (agent == null || agent.isDead || agent.navigationAgent == null)
                    {
                        if (toRestore == null) toRestore = new List<int>();
                        toRestore.Add(agentIndex);
                        continue;
                    }

                    if (!agent.hasManualCommand || agent.navigationState != AgentNavigationState.ManualCommand || !_agentDestinations.ContainsKey(agentIndex))
                    {
                        if (toRestore == null) toRestore = new List<int>();
                        toRestore.Add(agentIndex);
                    }
                }
            }

            if (toRestore == null || toRestore.Count == 0)
                return;

            var restoreAgents = crowdController.GetAgents();
            foreach (int agentIndex in toRestore)
            {
                CrowdAgent agent = (agentIndex >= 0 && agentIndex < restoreAgents.Count) ? restoreAgents[agentIndex] : null;
                RestoreFormationSpeedOverride(agentIndex, agent);
            }
        }

        private void ClearExternalDestinations(IEnumerable<IRTSExternalSelectable> selectables)
        {
            foreach (var selectable in selectables)
            {
                if (selectable == null) continue;
                _externalDestinations.Remove(selectable);
                _externalDestinationSetTime.Remove(selectable);
            }

            RefreshDestinationStateForSelection();
        }

        /// <summary>
        /// Computes the world-space center of selected agents/external selectables that have destinations.
        /// Returns null if no valid positions are available.
        /// </summary>
        private Vector3? GetSelectionCenterForIndicators()
        {
            int totalCount = _selectedAgentsWithDestination.Count + _selectedExternalWithDestination.Count;
            if (totalCount == 0)
                return null;

            Vector3 sum = Vector3.zero;
            int valid = 0;
            var agents = crowdController != null ? crowdController.GetAgents() : null;

            foreach (int agentIndex in _selectedAgentsWithDestination)
            {
                if (agents == null || agentIndex < 0 || agentIndex >= agents.Count)
                    continue;
                var agent = agents[agentIndex];
                if (agent?.navigationAgent != null)
                {
                    sum += agent.WorldPosition;
                    valid++;
                }
            }

            foreach (var selectable in _selectedExternalWithDestination)
            {
                if (IsExternalSelectableValid(selectable, requireSelectable: false))
                {
                    sum += GetExternalSelectableWorldPosition(selectable);
                    valid++;
                }
            }

            if (valid == 0)
                return null;

            return sum / valid;
        }

        /// <summary>
        /// Updates the position of the destination indicator.
        /// </summary>
        private void UpdateDestinationIndicatorPosition()
        {
            if (_destinationIndicatorInstance == null || !_hasActiveDestinationForSelection)
                return;

            Vector3 indicatorPos = _activeDestination + destinationIndicatorOffset;
            
            // Check if indicator is UI-based (has RectTransform and parent is Canvas)
            RectTransform rectTransform = _destinationIndicatorInstance.GetComponent<RectTransform>();
            if (rectTransform != null && destinationIndicatorParent != null)
            {
                Canvas canvas = destinationIndicatorParent.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
                {
                    // World space canvas - position directly
                    _destinationIndicatorInstance.transform.position = indicatorPos;
                }
                else if (canvas != null && _camera != null)
                {
                    // Screen space canvas - convert world to screen
                    Vector3 screenPos = _camera.WorldToScreenPoint(indicatorPos);
                    if (screenPos.z > 0) // In front of camera
                    {
                        rectTransform.position = screenPos;
                    }
                }
            }
            else
            {
                // World space object (VFX, particles, etc.)
                _destinationIndicatorInstance.transform.position = indicatorPos;
            }
        }

        /// <summary>
        /// Updates the direction indicator to point from selection center toward destination.
        /// </summary>
        private void UpdateDirectionIndicator()
        {
            if (_directionIndicatorInstance == null || !_hasActiveDestinationForSelection || !showDirectionIndicator)
                return;

            var selectionCenterOpt = GetSelectionCenterForIndicators();
            if (!selectionCenterOpt.HasValue)
            {
                _directionIndicatorInstance.SetActive(false);
                return;
            }

            Vector3 selectionCenter = selectionCenterOpt.Value;

            // Calculate direction and position
            Vector3 toDestination = _activeDestination - selectionCenter;
            float distanceToDestination = toDestination.magnitude;
            
            if (distanceToDestination < 0.1f)
            {
                _directionIndicatorInstance.SetActive(false);
                return;
            }

            _directionIndicatorInstance.SetActive(true);

            // Use LineRenderer mode if enabled and available
            if (useLineRenderer && _directionLineRenderer != null)
            {
                UpdateDirectionLineRenderer(selectionCenter, distanceToDestination);
                return;
            }

            // Standard indicator positioning mode
            Vector3 direction = toDestination.normalized;
            
            // Calculate dynamic distance based on number of selected agents
            // This scales with formation size so the indicator stays ahead of the group
            int agentCount = _selectedAgentsWithDestination.Count + _selectedExternalWithDestination.Count;
            float dynamicDistance = Mathf.Max(
                directionIndicatorMinDistance, 
                agentCount * directionIndicatorDistancePerAgent
            );
            dynamicDistance = Mathf.Min(dynamicDistance, directionIndicatorMaxDistance);
            
            // Also cap at half the distance to destination so indicator doesn't overshoot
            float indicatorDist = Mathf.Min(dynamicDistance, distanceToDestination * 0.5f);
            Vector3 indicatorPos = selectionCenter + direction * indicatorDist + directionIndicatorOffset;

            // Position and rotate the direction indicator
            _directionIndicatorInstance.transform.position = indicatorPos;
            
            // Calculate base rotation based on orientation mode
            Quaternion baseRotation;
            switch (directionIndicatorOrientation)
            {
                case IndicatorOrientationMode.FlatOnGround:
                    // Lies flat on XZ plane, Y rotation points toward destination
                    // First rotate to lie flat (X+ becomes forward on ground), then rotate Y toward destination
                    Vector3 flatDirection = new Vector3(direction.x, 0f, direction.z).normalized;
                    if (flatDirection.sqrMagnitude < 0.001f)
                        flatDirection = Vector3.forward;
                    float yAngle = Mathf.Atan2(flatDirection.x, flatDirection.z) * Mathf.Rad2Deg;
                    baseRotation = Quaternion.Euler(90f, yAngle, 0f);
                    break;
                    
                case IndicatorOrientationMode.Billboard:
                    // Face the camera while pointing toward destination
                    if (targetCamera != null)
                    {
                        Vector3 toCamera = targetCamera.transform.position - indicatorPos;
                        baseRotation = Quaternion.LookRotation(-toCamera, Vector3.up);
                        // Rotate around the camera-facing axis to point toward destination
                        Vector3 localDir = baseRotation * Vector3.forward;
                        Vector3 projectedDir = Vector3.ProjectOnPlane(direction, localDir).normalized;
                        if (projectedDir.sqrMagnitude > 0.001f)
                        {
                            float angle = Vector3.SignedAngle(baseRotation * Vector3.up, projectedDir, localDir);
                            baseRotation = Quaternion.AngleAxis(angle, localDir) * baseRotation;
                        }
                    }
                    else
                    {
                        baseRotation = Quaternion.LookRotation(direction, Vector3.up);
                    }
                    break;
                    
                case IndicatorOrientationMode.Object3D:
                default:
                    // Standard 3D object mode - Z+ points toward destination
                    baseRotation = Quaternion.LookRotation(direction, Vector3.up);
                    break;
            }
            
            // Apply user-defined rotation offset
            Quaternion offsetRotation = Quaternion.Euler(directionIndicatorRotationOffset);
            _directionIndicatorInstance.transform.rotation = baseRotation * offsetRotation;
        }

        /// <summary>
        /// Updates the LineRenderer to draw a line from selection center to destination.
        /// </summary>
        private void UpdateDirectionLineRenderer(Vector3 selectionCenter, float distanceToDestination)
        {
            if (_directionLineRenderer == null)
                return;

            // Position the indicator object at selection center
            _directionIndicatorInstance.transform.position = selectionCenter + directionIndicatorOffset;
            _directionIndicatorInstance.transform.rotation = Quaternion.identity;

            if (drawPathInsteadOfDirection)
            {
                // Draw the actual NavMesh path
                UpdateLineRendererWithPath(selectionCenter);
            }
            else
            {
                // Draw a straight line from selection center to destination
                Vector3 startPos = selectionCenter + new Vector3(0, lineHeightOffset, 0);
                Vector3 endPos = _activeDestination + new Vector3(0, lineHeightOffset, 0);
                
                _directionLineRenderer.positionCount = 2;
                _directionLineRenderer.SetPosition(0, startPos);
                _directionLineRenderer.SetPosition(1, endPos);
            }
        }

        /// <summary>
        /// Updates the LineRenderer to draw the actual NavMesh path with smooth interpolation.
        /// </summary>
        private void UpdateLineRendererWithPath(Vector3 selectionCenter)
        {
            bool shouldRecalculatePath = Time.time - _lastPathUpdateTime >= pathUpdateInterval;
            
            if (shouldRecalculatePath)
            {
                _lastPathUpdateTime = Time.time;
                RecalculateTargetPath(selectionCenter);
            }
            
            // Interpolate current points toward target points
            InterpolatePathPoints();
            
            // Apply interpolated points to LineRenderer
            ApplyPathToLineRenderer();
        }
        
        /// <summary>
        /// Recalculates the target path from selection center to destination.
        /// </summary>
        private void RecalculateTargetPath(Vector3 selectionCenter)
        {
            if (_lineRendererPath == null)
                _lineRendererPath = new NavMeshPath();

            Vector3 startPos = selectionCenter;
            
            // Sample start position against selected backend when possible.
            INavigationAgent navAgent = GetFirstSelectedNavigationAgent();
            if (navAgent != null && navAgent.SamplePosition(startPos, out Vector3 sampledStart, 5f))
            {
                startPos = sampledStart;
            }
            else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(startPos, 5f, out Vector3 startHit))
            {
                startPos = startHit;
            }

            // Calculate the path
            Vector3[] newPath;
            bool shouldUseNavMeshPath = navAgent == null || navAgent.Capabilities.RequiresSurfaceValidationForControl;
            if (shouldUseNavMeshPath &&
                NavMesh.CalculatePath(startPos, _activeDestination, NavMesh.AllAreas, _lineRendererPath) &&
                _lineRendererPath.corners.Length > 0)
            {
                newPath = new Vector3[_lineRendererPath.corners.Length];
                for (int i = 0; i < _lineRendererPath.corners.Length; i++)
                {
                    newPath[i] = _lineRendererPath.corners[i] + new Vector3(0, lineHeightOffset, 0);
                }
            }
            else
            {
                // Path calculation failed, use straight line
                newPath = new Vector3[2];
                newPath[0] = startPos + new Vector3(0, lineHeightOffset, 0);
                newPath[1] = _activeDestination + new Vector3(0, lineHeightOffset, 0);
            }
            
            // Store as target path
            _targetPathPoints = newPath;
            _targetPathLength = newPath.Length;
            _hasPathToInterpolateTo = true;
            
            // Initialize current path if needed
            if (_currentPathPoints == null || _currentPathPoints.Length == 0)
            {
                _currentPathPoints = new Vector3[_targetPathLength];
                System.Array.Copy(_targetPathPoints, _currentPathPoints, _targetPathLength);
            }
        }
        
        /// <summary>
        /// Smoothly interpolates current path points toward target path points.
        /// </summary>
        private void InterpolatePathPoints()
        {
            if (!_hasPathToInterpolateTo || _targetPathPoints == null || _targetPathLength == 0)
                return;
            
            // If interpolation is disabled, snap immediately
            if (pathInterpolationSpeed <= 0f)
            {
                if (_currentPathPoints == null || _currentPathPoints.Length != _targetPathLength)
                {
                    _currentPathPoints = new Vector3[_targetPathLength];
                }
                System.Array.Copy(_targetPathPoints, _currentPathPoints, _targetPathLength);
                return;
            }
            
            float t = pathInterpolationSpeed * Time.deltaTime;
            
            // Handle path length changes by resampling
            int currentLength = _currentPathPoints?.Length ?? 0;
            
            if (currentLength != _targetPathLength)
            {
                // Resample current path to match target length
                Vector3[] resampledPath = ResamplePath(_currentPathPoints, _targetPathLength);
                _currentPathPoints = resampledPath;
            }
            
            // Interpolate each point
            for (int i = 0; i < _targetPathLength; i++)
            {
                _currentPathPoints[i] = Vector3.Lerp(_currentPathPoints[i], _targetPathPoints[i], t);
            }
        }
        
        /// <summary>
        /// Resamples a path to have a different number of points while maintaining shape.
        /// </summary>
        private Vector3[] ResamplePath(Vector3[] sourcePath, int targetCount)
        {
            if (sourcePath == null || sourcePath.Length == 0 || targetCount <= 0)
            {
                return new Vector3[targetCount];
            }
            
            if (sourcePath.Length == 1)
            {
                Vector3[] result = new Vector3[targetCount];
                for (int i = 0; i < targetCount; i++)
                {
                    result[i] = sourcePath[0];
                }
                return result;
            }
            
            Vector3[] resampled = new Vector3[targetCount];
            
            // Calculate total length of source path
            float totalLength = 0f;
            float[] segmentLengths = new float[sourcePath.Length - 1];
            for (int i = 0; i < sourcePath.Length - 1; i++)
            {
                segmentLengths[i] = Vector3.Distance(sourcePath[i], sourcePath[i + 1]);
                totalLength += segmentLengths[i];
            }
            
            if (totalLength < 0.001f)
            {
                // Path is essentially a point
                for (int i = 0; i < targetCount; i++)
                {
                    resampled[i] = sourcePath[0];
                }
                return resampled;
            }
            
            // Sample at evenly spaced intervals
            for (int i = 0; i < targetCount; i++)
            {
                float targetDist = (i / (float)(targetCount - 1)) * totalLength;
                resampled[i] = SamplePathAtDistance(sourcePath, segmentLengths, targetDist);
            }
            
            return resampled;
        }
        
        /// <summary>
        /// Samples a point along the path at a given distance from the start.
        /// </summary>
        private Vector3 SamplePathAtDistance(Vector3[] path, float[] segmentLengths, float distance)
        {
            float accumulated = 0f;
            
            for (int i = 0; i < segmentLengths.Length; i++)
            {
                if (accumulated + segmentLengths[i] >= distance)
                {
                    float segmentT = (distance - accumulated) / segmentLengths[i];
                    return Vector3.Lerp(path[i], path[i + 1], segmentT);
                }
                accumulated += segmentLengths[i];
            }
            
            // Return last point if we've gone past the end
            return path[path.Length - 1];
        }
        
        /// <summary>
        /// Applies the current interpolated path points to the LineRenderer.
        /// </summary>
        private void ApplyPathToLineRenderer()
        {
            if (_currentPathPoints == null || _currentPathPoints.Length == 0)
                return;
            
            _directionLineRenderer.positionCount = _currentPathPoints.Length;
            _directionLineRenderer.SetPositions(_currentPathPoints);
        }
        
        /// <summary>
        /// Resets the path interpolation state so the next path starts fresh.
        /// </summary>
        private void ResetPathInterpolation()
        {
            _currentPathPoints = null;
            _targetPathPoints = null;
            _targetPathLength = 0;
            _hasPathToInterpolateTo = false;
            _lastPathUpdateTime = 0f;
        }

        /// <summary>
        /// Gets the center position of the current selection.
        /// </summary>
        private Vector3 GetSelectionCenter()
        {
            if (_selectedAgentIndices.Count == 0)
                return Vector3.zero;

            return GetAgentsCenterPosition(_selectedAgentIndices);
        }

        /// <summary>
        /// Gets the center position of a list of agents.
        /// </summary>
        private Vector3 GetAgentsCenterPosition(List<int> agentIndices)
        {
            if (agentIndices == null || agentIndices.Count == 0)
                return Vector3.zero;

            return GetAgentsCenterPositionInternal(agentIndices);
        }

        /// <summary>
        /// Gets the center position of a collection of agents (works with HashSet, List, etc).
        /// </summary>
        private Vector3 GetAgentsCenterPosition(IEnumerable<int> agentIndices)
        {
            return GetAgentsCenterPositionInternal(agentIndices);
        }

        /// <summary>
        /// Internal implementation for getting agent center position.
        /// </summary>
        private Vector3 GetAgentsCenterPositionInternal(IEnumerable<int> agentIndices)
        {
            if (agentIndices == null)
                return Vector3.zero;

            var agents = crowdController.GetAgents();
            Vector3 center = Vector3.zero;
            int validCount = 0;

            foreach (int index in agentIndices)
            {
                if (index >= 0 && index < agents.Count)
                {
                    CrowdAgent agent = agents[index];
                    if (agent != null && !agent.isDead && agent.navigationAgent != null)
                    {
                        center += agent.WorldPosition;
                        validCount++;
                    }
                }
            }

            return validCount > 0 ? center / validCount : Vector3.zero;
        }

        /// <summary>
        /// Updates destination indicators each frame. Checks if agents have reached destination.
        /// </summary>
        private void UpdateDestinationIndicators()
        {
            // Update fade-out animation if in progress
            UpdateIndicatorFadeOut();
            
            // Check all agents with destinations and remove those that have arrived
            CheckAndRemoveArrivedAgents();
            
            // If indicators were hidden due to first arrival, don't re-activate until new command
            if (_indicatorsHiddenByFirstArrival)
                return;
            
            // If currently fading out, don't update positions (let fade complete)
            if (_isFadingOutIndicators)
                return;
            
            // If no selected agents have destinations, nothing to update
            if (!_hasActiveDestinationForSelection)
                return;

            // Update indicator positions
            UpdateDestinationIndicatorPosition();
            UpdateDirectionIndicator();
        }

        /// <summary>
        /// Checks all agents with tracked destinations and removes those that have arrived.
        /// Efficient: only iterates agents that have destinations, not all agents.
        /// </summary>
        private void CheckAndRemoveArrivedAgents()
        {
            if (_agentDestinations.Count == 0 && _externalDestinations.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            List<int> arrivedAgents = null; // Lazy allocation
            List<IRTSExternalSelectable> arrivedExternal = null;
            bool anyArrived = false;
            
            float currentTime = Time.time;
            
            // Internal agents
            foreach (var kvp in _agentDestinations)
            {
                int agentIndex = kvp.Key;
                Vector3 destination = kvp.Value;
                
                // Skip agents that were just given a destination (grace period for path calculation)
                if (_agentDestinationSetTime.TryGetValue(agentIndex, out float setTime))
                {
                    if (currentTime - setTime < DESTINATION_GRACE_PERIOD)
                        continue;
                }
                
                if (agentIndex >= 0 && agentIndex < agents.Count)
                {
                    CrowdAgent agent = agents[agentIndex];
                    if (agent != null && agent.navigationAgent != null)
                    {
                        // Check if agent has arrived or is dead
                        bool hasArrived = agent.isDead;
                        
                        // Skip if navigation agent is disabled or not on navigable surface - can't determine arrival state
                        if (!hasArrived && (!agent.IsNavigationEnabled || !agent.IsOnNavMesh))
                        {
                            continue; // Don't remove destination, agent may be temporarily disabled
                        }
                        
                        if (!hasArrived)
                        {
                            float distance = Vector3.Distance(agent.WorldPosition, destination);
                            hasArrived = distance <= destinationReachedThreshold ||
                                        (!agent.navigationAgent.PathPending && !agent.navigationAgent.HasPath);
                        }
                        
                        if (hasArrived)
                        {
                            anyArrived = true;
                            if (arrivedAgents == null)
                                arrivedAgents = new List<int>();
                            arrivedAgents.Add(agentIndex);
                            
                            // Apply formation facing rotation if specified
                            if (!agent.isDead && agent.hasFormationFacing)
                            {
                                ApplyFormationFacing(agent, agentIndex);
                            }
                        }
                    }
                }
                else
                {
                    // Invalid agent index - mark for removal
                    if (arrivedAgents == null)
                        arrivedAgents = new List<int>();
                    arrivedAgents.Add(agentIndex);
                }
            }

            // External selectables (e.g., GC2 players)
            foreach (var kvp in _externalDestinations)
            {
                var selectable = kvp.Key;
                Vector3 destination = kvp.Value;

                if (selectable == null)
                    continue;

                if (_externalDestinationSetTime.TryGetValue(selectable, out float setTime))
                {
                    if (currentTime - setTime < DESTINATION_GRACE_PERIOD)
                        continue;
                }

                bool hasArrived = false;
                INavigationAgent externalNavigation = GetExternalNavigationAgent(selectable);

                if (externalNavigation != null)
                {
                    if (!externalNavigation.Enabled ||
                        (externalNavigation.Capabilities.RequiresSurfaceValidationForControl && !externalNavigation.IsOnNavMesh))
                    {
                        continue; // can't determine yet
                    }

                    float distance = Vector3.Distance(GetExternalSelectableWorldPosition(selectable), destination);
                    TryGetExternalPathState(selectable, externalNavigation, out bool externalHasPath, out bool externalPathPending);
                    hasArrived = distance <= destinationReachedThreshold || (!externalPathPending && !externalHasPath);
                }
                else if (CanControlExternalNavigation(selectable))
                {
                    if (TryGetExternalPathState(selectable, null, out bool legacyHasPath, out bool legacyPathPending))
                    {
                        float distance = Vector3.Distance(GetExternalSelectableWorldPosition(selectable), destination);
                        hasArrived = distance <= destinationReachedThreshold || (!legacyPathPending && !legacyHasPath);
                    }
                }
                else if (selectable.Transform != null)
                {
                    float distance = Vector3.Distance(selectable.Transform.position, destination);
                    hasArrived = distance <= destinationReachedThreshold;
                }

                if (hasArrived)
                {
                    anyArrived = true;
                    if (arrivedExternal == null)
                        arrivedExternal = new List<IRTSExternalSelectable>();
                    arrivedExternal.Add(selectable);
                }
            }
            
            // Hide indicators immediately when first agent arrives (if enabled)
            if (hideIndicatorsOnFirstArrival && anyArrived)
            {
                _indicatorsHiddenByFirstArrival = true;
                DeactivateDestinationIndicator();
            }
            
            // Remove arrived agents/external and refresh state
            bool removed = false;
            if (arrivedAgents != null && arrivedAgents.Count > 0)
            {
                ClearAgentDestinations(arrivedAgents);
                removed = true;
            }

            if (arrivedExternal != null && arrivedExternal.Count > 0)
            {
                ClearExternalDestinations(arrivedExternal);
                removed = true;
            }
            
            // Update indicator visibility based on new state (for non-first-arrival mode)
            if (removed && !hideIndicatorsOnFirstArrival && !_hasActiveDestinationForSelection)
            {
                DeactivateDestinationIndicator();
            }
        }

        /// <summary>
        /// Applies the formation facing direction to an agent that has arrived at its destination.
        /// Either starts smooth rotation or applies instant rotation based on settings.
        /// </summary>
        private void ApplyFormationFacing(CrowdAgent agent, int agentIndex)
        {
            if (!applyFacingOnArrival || agent == null || agent.navigationAgent == null)
                return;

            // Calculate target rotation from the formation facing angle
            Quaternion targetRotation = Quaternion.Euler(0f, agent.formationFacingAngle, 0f);
            
            // Get the transform from the underlying agent for rotation
            Transform agentTransform = agent.navigationAgent.GetUnderlyingAgent<Component>()?.transform;
            if (agentTransform == null)
                return;
            
            if (facingRotationSpeed <= 0f)
            {
                // Instant rotation
                agentTransform.rotation = targetRotation;
                agent.hasFormationFacing = false; // Clear the flag
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Facing] Agent {agentIndex} instantly rotated to {agent.formationFacingAngle:F1}°");
            }
            else
            {
                // Start smooth rotation - track this agent (agentIndex passed in, no expensive lookup needed)
                _agentsRotatingToFace[agentIndex] = targetRotation;
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Facing] Agent {agentIndex} starting smooth rotation to {agent.formationFacingAngle:F1}°");
            }
        }

        /// <summary>
        /// Updates agents that are currently rotating to face their formation direction.
        /// Called every frame from Update.
        /// </summary>
        private void UpdateAgentFacingRotations()
        {
            if (_agentsRotatingToFace.Count == 0)
                return;

            var agents = crowdController.GetAgents();
            List<int> completedAgents = null; // Lazy allocation
            float rotationStep = facingRotationSpeed * Time.deltaTime;

            foreach (var kvp in _agentsRotatingToFace)
            {
                int agentIndex = kvp.Key;
                Quaternion targetRotation = kvp.Value;

                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    if (completedAgents == null) completedAgents = new List<int>();
                    completedAgents.Add(agentIndex);
                    continue;
                }

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                {
                    if (completedAgents == null) completedAgents = new List<int>();
                    completedAgents.Add(agentIndex);
                    continue;
                }

                Transform agentTransform = agent.navigationAgent.GetUnderlyingAgent<Component>()?.transform;
                if (agentTransform == null)
                {
                    if (completedAgents == null) completedAgents = new List<int>();
                    completedAgents.Add(agentIndex);
                    continue;
                }
                
                // Rotate towards target
                agentTransform.rotation = Quaternion.RotateTowards(
                    agentTransform.rotation, 
                    targetRotation, 
                    rotationStep);

                // Check if rotation is complete
                float angleDiff = Quaternion.Angle(agentTransform.rotation, targetRotation);
                if (angleDiff < 0.5f)
                {
                    // Snap to final rotation and mark as complete
                    agentTransform.rotation = targetRotation;
                    agent.hasFormationFacing = false;
                    
                    if (completedAgents == null) completedAgents = new List<int>();
                    completedAgents.Add(agentIndex);
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Facing] Agent {agentIndex} completed rotation");
                }
            }

            // Remove completed agents
            if (completedAgents != null)
            {
                foreach (int agentIndex in completedAgents)
                {
                    _agentsRotatingToFace.Remove(agentIndex);
                }
            }
        }

        /// <summary>
        /// Cleans up destination indicator instances.
        /// </summary>
        private void CleanupDestinationIndicators()
        {
            if (_destinationIndicatorInstance != null)
            {
                Destroy(_destinationIndicatorInstance);
                _destinationIndicatorInstance = null;
            }

            if (_directionIndicatorInstance != null)
            {
                Destroy(_directionIndicatorInstance);
                _directionIndicatorInstance = null;
            }

            _hasActiveDestinationForSelection = false;
            _selectedAgentsWithDestination.Clear();
            _agentDestinations.Clear();
            _agentDestinationSetTime.Clear();
        }

        #endregion

        #region GUI Drawing

        private void OnGUI()
        {
            // Draw IMGUI selection rectangle while dragging
            if (!showSelectionRectangle || !enableDragSelection || !_isDragging)
                return;

            if (_whiteTexture == null || _camera == null)
                return;

            // Avoid drawing tiny jitter rectangles when drag just started
            Rect rect = GetSelectionRect();
            if (rect.width < 1f || rect.height < 1f)
                return;

            DrawSelectionRectangle();
        }

        private Rect GetSelectionRect()
        {
            float minX = Mathf.Min(_dragStartPosition.x, _dragCurrentPosition.x);
            float maxX = Mathf.Max(_dragStartPosition.x, _dragCurrentPosition.x);
            float minY = Mathf.Min(_dragStartPosition.y, _dragCurrentPosition.y);
            float maxY = Mathf.Max(_dragStartPosition.y, _dragCurrentPosition.y);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private void DrawSelectionRectangle()
        {
            Rect rect = GetSelectionRect();

            // Convert from screen coordinates (bottom-left origin) to GUI coordinates (top-left origin)
            float guiY = Screen.height - rect.y - rect.height;
            Rect guiRect = new Rect(rect.x, guiY, rect.width, rect.height);

            // Draw fill
            GUI.color = selectionFillColor;
            GUI.DrawTexture(guiRect, _whiteTexture);

            // Draw border
            GUI.color = selectionBorderColor;
            float t = selectionBorderThickness;

            // Top
            GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, guiRect.width, t), _whiteTexture);
            // Bottom
            GUI.DrawTexture(new Rect(guiRect.x, guiRect.y + guiRect.height - t, guiRect.width, t), _whiteTexture);
            // Left
            GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, t, guiRect.height), _whiteTexture);
            // Right
            GUI.DrawTexture(new Rect(guiRect.x + guiRect.width - t, guiRect.y, t, guiRect.height), _whiteTexture);

            GUI.color = Color.white;
        }

        #endregion

        #region Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!enableGizmos || !Application.isPlaying)
                return;

            // Draw selection highlight for selected agents
            if (showSelectionHighlight && crowdController != null)
            {
                var agents = crowdController.GetAgents();
                Gizmos.color = selectionHighlightColor;

                foreach (int index in _selectedAgentIndices)
                {
                    if (index >= 0 && index < agents.Count)
                    {
                        CrowdAgent agent = agents[index];
                        if (agent != null && !agent.isDead)
                        {
                            Vector3 pos = GetAgentWorldPosition(agent);
                            DrawSelectionCircle(pos, 0.8f);
                        }
                    }
                }
            }

            // Draw move indicator
            if (_showingMoveIndicator)
            {
                float elapsed = Time.time - _moveIndicatorStartTime;
                float alpha = 1f - (elapsed / moveIndicatorDuration);
                
                Gizmos.color = new Color(moveIndicatorColor.r, moveIndicatorColor.g, moveIndicatorColor.b, moveIndicatorColor.a * alpha);
                DrawMoveIndicator(_moveIndicatorPosition);
            }
        }

        private void DrawSelectionCircle(Vector3 center, float radius)
        {
            int segments = 24;
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + Vector3.right * radius;

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }
        }

        private void DrawMoveIndicator(Vector3 position)
        {
            // Draw circle
            DrawSelectionCircle(position, 1f);

            // Draw cross
            float size = 0.5f;
            Gizmos.DrawLine(position + Vector3.forward * size, position - Vector3.forward * size);
            Gizmos.DrawLine(position + Vector3.right * size, position - Vector3.right * size);

            // Draw vertical line
            Gizmos.DrawLine(position, position + Vector3.up * 2f);
        }
#endif

        #endregion

        #region Waypoint Patrol System

        /// <summary>
        /// Queue a waypoint at the current mouse position.
        /// </summary>
        private void QueueWaypointAtMousePosition()
        {
            QueueWaypointAtScreenPosition(_mousePosition);
        }

        /// <summary>
        /// Queue a waypoint at a specific screen position.
        /// </summary>
        private void QueueWaypointAtScreenPosition(Vector2 screenPosition)
        {
            if (!enableWaypointPatrol || TotalSelectedCount == 0) return;

            Ray ray = _camera.ScreenPointToRay(screenPosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundMask))
            {
                if (TryGetCommandDestination(hit.point, out Vector3 destination))
                {
                    AddWaypointToQueue(destination);
                }
                else if (enableDebugLogging)
                {
                    Debug.LogWarning($"[RTS Waypoint] Failed to resolve destination for point {hit.point}");
                }
            }
        }

        /// <summary>
        /// Add a waypoint to the current queue.
        /// </summary>
        private void AddWaypointToQueue(Vector3 position)
        {
            // Start queuing if not already
            if (!_isQueuingWaypoints)
            {
                _isQueuingWaypoints = true;
                _queuedWaypoints.Clear();
                _isPatrolLoop = false;
                
                if (enableDebugLogging)
                    Debug.Log("[RTS Waypoint] Started queuing waypoints");
            }

            // Check max waypoints
            if (_queuedWaypoints.Count >= maxWaypointsPerPath)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[RTS Waypoint] Max waypoints ({maxWaypointsPerPath}) reached");
                return;
            }

            // Check if this waypoint closes the loop (close to first waypoint)
            if (_queuedWaypoints.Count >= 2)
            {
                float distToFirst = Vector3.Distance(position, _queuedWaypoints[0]);
                if (distToFirst <= patrolLoopCloseDistance)
                {
                    // Close the loop - don't add this waypoint, just mark as loop
                    _isPatrolLoop = true;
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Waypoint] Patrol loop closed! Distance to first: {distToFirst:F2}");
                    
                    // Finalize the patrol path
                    FinalizeWaypointPath();
                    return;
                }
            }

            // Add waypoint
            _queuedWaypoints.Add(position);
            
            if (enableDebugLogging)
                Debug.Log($"[RTS Waypoint] Added waypoint {_queuedWaypoints.Count}: {position}");

            // Play command sound
            PlayCommandSound(1);

            // Update visualization
            UpdateWaypointVisualization();

            // If this is the first waypoint, move agents to it immediately
            if (_queuedWaypoints.Count == 1)
            {
                MoveSelectedAgentsTo(position);
                
                // Fire move command events
                MoveCommandIssued?.Invoke(position, _selectedAgentIndices);
                onMoveCommandIssued?.Invoke(position, new List<int>(_selectedAgentIndices));
            }
        }

        /// <summary>
        /// Finalize the waypoint path and assign it to selected agents.
        /// </summary>
        private void FinalizeWaypointPath()
        {
            if (_queuedWaypoints.Count < 2)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[RTS Waypoint] Cannot finalize path with less than 2 waypoints");
                CancelWaypointQueue();
                return;
            }

            // Count valid agents for formation calculation
            var agents = crowdController?.GetAgents();
            int validAgentCount = 0;
            List<int> validAgentIndices = new List<int>();
            
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (agents != null && agentIndex >= 0 && agentIndex < agents.Count)
                {
                    var agent = agents[agentIndex];
                    if (agent != null && !agent.isDead && agent.navigationAgent != null && agent.IsOnNavMesh)
                    {
                        validAgentIndices.Add(agentIndex);
                        validAgentCount++;
                    }
                }
            }
            
            // Count valid external selectables
            List<IRTSExternalSelectable> validExternals = new List<IRTSExternalSelectable>();
            foreach (var selectable in _selectedExternalSelectables)
            {
                if (CanControlExternalNavigation(selectable))
                {
                    validExternals.Add(selectable);
                    validAgentCount++;
                }
            }
            
            if (validAgentCount == 0)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[RTS Waypoint] No valid agents to assign patrol path");
                CancelWaypointQueue();
                return;
            }
            
            // Get current formation settings
            float currentSpacing = formationSpacing;
            if (persistSpacingPerFaction)
            {
                string dominantFaction = GetDominantFactionFromSelection();
                if (!string.IsNullOrEmpty(dominantFaction) && _factionSpacingOverrides.TryGetValue(dominantFaction, out float factionSpacing))
                {
                    currentSpacing = factionSpacing;
                }
            }
            
            // Create patrol path for each selected agent with formation data
            int formationIndex = 0;
            AgentPatrolPath firstCreatedPath = null; // Track first path for visualization
            
            foreach (int agentIndex in validAgentIndices)
            {
                var agent = agents[agentIndex];

                AgentPatrolPath path = new AgentPatrolPath();
                path.waypoints.AddRange(_queuedWaypoints);
                path.isLoop = _isPatrolLoop;
                path.currentWaypointIndex = 0;
                
                // Store formation data
                path.formationIndex = formationIndex;
                path.formationTotalCount = validAgentCount;
                path.formationType = moveFormation;
                path.formationSpacing = currentSpacing;
                
                _agentPatrolPaths[agentIndex] = path;
                
                // IMMEDIATELY start moving agent to first waypoint
                // This prevents the visual desync issue where agents appear frozen
                MoveAgentToWaypoint(agentIndex, agent, path);
                
                // Keep reference to first path for efficient visualization
                if (firstCreatedPath == null)
                    firstCreatedPath = path;
                    
                formationIndex++;
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Waypoint] Assigned patrol path to agent {agentIndex}: {path.waypoints.Count} waypoints, loop={path.isLoop}, formationIdx={path.formationIndex}/{path.formationTotalCount}");
            }

            // Handle external selectables with formation data
            foreach (var selectable in validExternals)
            {
                AgentPatrolPath path = new AgentPatrolPath();
                path.waypoints.AddRange(_queuedWaypoints);
                path.isLoop = _isPatrolLoop;
                path.currentWaypointIndex = 0;
                
                // Store formation data
                path.formationIndex = formationIndex;
                path.formationTotalCount = validAgentCount;
                path.formationType = moveFormation;
                path.formationSpacing = currentSpacing;
                
                _externalPatrolPaths[selectable] = path;
                
                // IMMEDIATELY start moving external to first waypoint
                MoveExternalToWaypoint(selectable, path);
                
                // Keep reference to first path for efficient visualization
                if (firstCreatedPath == null)
                    firstCreatedPath = path;
                    
                formationIndex++;
            }

            if (enableDebugLogging)
                Debug.Log($"[RTS Waypoint] Patrol path finalized: {_queuedWaypoints.Count} waypoints, loop={_isPatrolLoop}");

            // Clear queuing state
            _isQueuingWaypoints = false;
            _queuedWaypoints.Clear();
            
            // Show visualization directly - no need to search since we have the path
            if (firstCreatedPath != null)
            {
                ShowPatrolPathVisualization(firstCreatedPath, firstCreatedPath.isLoop);
            }
        }

        /// <summary>
        /// Cancel waypoint queuing without finalizing the path.
        /// </summary>
        private void CancelWaypointQueue()
        {
            _isQueuingWaypoints = false;
            _queuedWaypoints.Clear();
            _isPatrolLoop = false;
            ClearWaypointVisualization();
            
            if (enableDebugLogging)
                Debug.Log("[RTS Waypoint] Waypoint queue cancelled");
        }

        /// <summary>
        /// Clear patrol paths for all selected agents.
        /// Called when a normal move command is issued.
        /// </summary>
        private void ClearPatrolPathsForSelection()
        {
            foreach (int agentIndex in _selectedAgentIndices)
            {
                if (_agentPatrolPaths.ContainsKey(agentIndex))
                {
                    _agentPatrolPaths.Remove(agentIndex);
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Waypoint] Cleared patrol path for agent {agentIndex}");
                }
            }

            foreach (var selectable in _selectedExternalSelectables)
            {
                if (_externalPatrolPaths.ContainsKey(selectable))
                {
                    _externalPatrolPaths.Remove(selectable);
                }
            }

            // Also cancel any in-progress waypoint queue
            if (_isQueuingWaypoints)
            {
                CancelWaypointQueue();
            }
            
            // Clear the patrol visualization since we're clearing patrol paths
            if (_isShowingActivePatrol)
            {
                ClearWaypointVisualization();
            }
        }

        /// <summary>
        /// Clear patrol path for a specific agent.
        /// </summary>
        public void ClearPatrolPath(int agentIndex)
        {
            if (_agentPatrolPaths.ContainsKey(agentIndex))
            {
                _agentPatrolPaths.Remove(agentIndex);
                
                if (enableDebugLogging)
                    Debug.Log($"[RTS Waypoint] Cleared patrol path for agent {agentIndex}");
                
                // Update visualization in case this was the displayed patrol
                UpdatePatrolPathVisualizationForSelection();
            }
        }

        /// <summary>
        /// Check if an agent has reached their current waypoint and advance to next.
        /// Called during Update.
        /// </summary>
        private void UpdatePatrolPaths()
        {
            if (!enableWaypointPatrol) return;

            var agents = crowdController?.GetAgents();
            if (agents == null) return;

            // Update agent patrol paths
            List<int> completedAgents = null;
            
            foreach (var kvp in _agentPatrolPaths)
            {
                int agentIndex = kvp.Key;
                AgentPatrolPath path = kvp.Value;
                
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;
                
                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                {
                    // Mark for removal
                    completedAgents ??= new List<int>();
                    completedAgents.Add(agentIndex);
                    continue;
                }

                // Check if agent is in combat - but verify targets are actually valid/alive
                bool hasValidAgentTarget = false;
                if (agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < agents.Count)
                {
                    var targetAgent = agents[agent.attackTargetAgentIndex];
                    hasValidAgentTarget = targetAgent != null && !targetAgent.isDead && targetAgent.health > 0;
                }
                
                bool hasValidTransformTarget = false;
                if (agent.attackTargetTransform != null)
                {
                    // Check if target transform still exists and (if it has health component) is alive
                    hasValidTransformTarget = agent.attackTargetTransform.gameObject.activeInHierarchy;
                }
                
                bool inCombat = agent.isAggroed || agent.isChasing || hasValidAgentTarget || hasValidTransformTarget;
                
                // If we had a target but it's now dead/invalid, clear the attack target data
                bool hadTargetButNowInvalid = (agent.attackTargetAgentIndex >= 0 && !hasValidAgentTarget) ||
                                               (agent.attackTargetTransform != null && !hasValidTransformTarget);
                if (hadTargetButNowInvalid && !inCombat)
                {
                    agent.hasAttackTargetPosition = false;
                    agent.attackTargetAgentIndex = -1;
                    agent.attackTargetTransform = null;
                    agent.attackTargetPosition = Vector3.zero;
                    
                    // CRITICAL: Reset combat state to Idle so navigation sync resumes
                    // The SyncAgentMovement skips agents with combatState = Attacking/Blocking/Hit/Dead
                    agent.combatState = CrowdCombatState.Idle;
                    
                    // Clear any combat-related flags
                    agent.isChasing = false;
                    agent.isAggroed = false;
                    
                    // Also ensure agent is in proper state
                    if (agent.navigationState == AgentNavigationState.ChasingTarget || 
                        agent.navigationState == AgentNavigationState.CombatOverride)
                    {
                        agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                        agent.hasManualCommand = true;
                    }
                }
                
                if (inCombat)
                {
                    if (!path.isPaused)
                    {
                        path.isPaused = true;
                        path.combatInterruptPosition = agent.WorldPosition;
                    }
                    continue;
                }
                
                // Resume patrol after combat
                if (path.isPaused)
                {
                    path.isPaused = false;
                    
                    // Continue to current waypoint
                    MoveAgentToWaypoint(agentIndex, agent, path);
                    continue;
                }

                // Check if agent has reached current waypoint (using formation position)
                if (agent.navigationAgent == null || !agent.IsOnNavMesh) continue;

                // Calculate this agent's formation position for arrival check
                Vector3 formationPos = CalculateFormationPositionWithParams(
                    path.formationIndex,
                    path.formationTotalCount,
                    path.CurrentWaypoint,
                    path.formationType,
                    path.formationSpacing
                );
                
                float distanceToFormationPos = Vector3.Distance(agent.WorldPosition, formationPos);
                float remainingDistance = agent.navigationAgent.RemainingDistance;
                bool hasValidRemainingDistance = !float.IsNaN(remainingDistance) && !float.IsInfinity(remainingDistance);
                bool hasReached = distanceToFormationPos <= destinationReachedThreshold &&
                                  (!agent.navigationAgent.HasPath ||
                                   (hasValidRemainingDistance && remainingDistance <= destinationReachedThreshold));

                if (hasReached)
                {
                    // Advance to next waypoint
                    Vector3 nextWaypoint = path.GetNextWaypoint();
                    MoveAgentToWaypoint(agentIndex, agent, path);
                    
                    if (enableDebugLogging)
                        Debug.Log($"[RTS Waypoint] Agent {agentIndex} reached waypoint, advancing to {path.currentWaypointIndex}");
                }
            }

            // Remove completed/dead agents
            if (completedAgents != null)
            {
                foreach (int agentIndex in completedAgents)
                {
                    _agentPatrolPaths.Remove(agentIndex);
                }
            }

            // Update external patrol paths
            List<IRTSExternalSelectable> completedExternal = null;
            
            foreach (var kvp in _externalPatrolPaths)
            {
                var selectable = kvp.Key;
                AgentPatrolPath path = kvp.Value;
                
                if (selectable == null || !selectable.IsAlive)
                {
                    completedExternal ??= new List<IRTSExternalSelectable>();
                    completedExternal.Add(selectable);
                    continue;
                }

                INavigationAgent externalNavigation = GetExternalNavigationAgent(selectable);
                if (!CanControlExternalNavigation(selectable))
                {
                    continue;
                }

                // Skip if in combat (check via interface if available)
                // For now, just check basic state
                if (path.isPaused)
                {
                    // TODO: Check if combat is over
                    continue;
                }

                // Calculate formation position for distance check (same as what we'll move to)
                Vector3 formationPos = CalculateFormationPositionWithParams(
                    path.formationIndex,
                    path.formationTotalCount,
                    path.CurrentWaypoint,
                    path.formationType,
                    path.formationSpacing
                );
                
                float distanceToFormationPos = Vector3.Distance(GetExternalSelectableWorldPosition(selectable), formationPos);
                bool hasReached = false;
                if (TryGetExternalRemainingDistance(selectable, externalNavigation, out float remainingDistance))
                {
                    bool hasPathState = TryGetExternalPathState(selectable, externalNavigation, out bool hasPath, out _);
                    hasReached = distanceToFormationPos <= destinationReachedThreshold &&
                                 (remainingDistance <= destinationReachedThreshold || !hasPathState || !hasPath);
                }

                if (hasReached)
                {
                    Vector3 nextWaypoint = path.GetNextWaypoint();
                    
                    // Move external selectable using formation offsets
                    MoveExternalToWaypoint(selectable, path);
                }
            }

            // Remove completed external selectables
            if (completedExternal != null)
            {
                foreach (var selectable in completedExternal)
                {
                    _externalPatrolPaths.Remove(selectable);
                }
            }
        }

        /// <summary>
        /// Move a specific agent to their current waypoint using formation offsets.
        /// </summary>
        private void MoveAgentToWaypoint(int agentIndex, CrowdAgent agent, AgentPatrolPath path)
        {
            if (agent?.navigationAgent == null || !agent.IsOnNavMesh)
                return;

            Vector3 waypointCenter = path.CurrentWaypoint;
            
            // Calculate formation position based on stored formation data
            Vector3 formationPos = CalculateFormationPositionWithParams(
                path.formationIndex,
                path.formationTotalCount,
                waypointCenter,
                path.formationType,
                path.formationSpacing
            );
            
            // Sample navigable surface to ensure valid position
            Vector3 destination = formationPos;
            if (agent.navigationAgent.SamplePosition(formationPos, out Vector3 navHitPos, path.formationSpacing * 2f))
            {
                destination = navHitPos;
            }
            
            // CRITICAL: Ensure navigation agent is not stopped before setting destination
            if (agent.navigationAgent.IsStopped)
            {
                agent.navigationAgent.IsStopped = false;
            }
            
            // Set destination
            agent.navigationAgent.SetDestination(destination);
            agent.hasManualCommand = true;
            agent.manualCommandDestination = destination;
            agent.hasRTSFormationPosition = false;
            agent.brokeFromRTSFormation = false;
            agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
            
            // CRITICAL: Reset combat state to Idle so SyncAgentMovement doesn't skip this agent
            // The navigation sync skips agents with combatState = Attacking/Blocking/Hit/Dead
            agent.combatState = CrowdCombatState.Idle;
            
            // Clear any combat-related flags that might interfere with movement
            agent.hasArrived = false;
            agent.isChasing = false;
            agent.isAggroed = false;

            // Track destination
            _agentDestinations[agentIndex] = destination;
            _agentDestinationSetTime[agentIndex] = Time.time;
        }
        
        /// <summary>
        /// Move an external selectable to their current waypoint using formation offsets.
        /// </summary>
        private void MoveExternalToWaypoint(IRTSExternalSelectable selectable, AgentPatrolPath path)
        {
            if (selectable == null)
                return;

            INavigationAgent externalNavigation = GetExternalNavigationAgent(selectable);
            if (!CanControlExternalNavigation(selectable))
                return;

            TrySetExternalNavigationStopped(selectable, false, externalNavigation);

            Vector3 waypointCenter = path.CurrentWaypoint;
            
            // Calculate formation position based on stored formation data
            Vector3 formationPos = CalculateFormationPositionWithParams(
                path.formationIndex,
                path.formationTotalCount,
                waypointCenter,
                path.formationType,
                path.formationSpacing
            );
            
            // Sample NavMesh to ensure valid position
            Vector3 destination = formationPos;
            if (externalNavigation != null)
            {
                if (externalNavigation.SamplePosition(formationPos, out Vector3 sampledPos, path.formationSpacing * 2f))
                    destination = sampledPos;
            }
            else if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(formationPos, path.formationSpacing * 2f, out Vector3 navHit))
            {
                destination = navHit;
            }

            // Set destination through selectable API so non-NavMesh implementations can handle movement.
            selectable.SetDestination(destination);
            
            // Track destination
            _externalDestinations[selectable] = destination;
            _externalDestinationSetTime[selectable] = Time.time;
        }
        
        /// <summary>
        /// Calculate formation position with custom parameters (for patrol system).
        /// Delegates to the shared <see cref="EnemyMassesFormationSystem"/> for consistent formation math.
        /// </summary>
        private Vector3 CalculateFormationPositionWithParams(int index, int totalCount, Vector3 center, FormationType formation, float spacing)
        {
            if (totalCount <= 1)
                return center;

            EnemyMassesFormationSystem formationSystem = crowdController != null
                ? crowdController.FormationSystem
                : null;

            if (formationSystem != null)
            {
                return formationSystem.CalculateFormationPosition(
                    index, totalCount, center, formation, spacing);
            }

            // Fallback: simple grid when the formation system is unavailable
            int side = Mathf.CeilToInt(Mathf.Sqrt(totalCount));
            int row = index / side;
            int col = index % side;
            float offsetX = (col - (side - 1) * 0.5f) * spacing;
            float offsetZ = (row - (side - 1) * 0.5f) * spacing;
            return center + new Vector3(offsetX, 0f, offsetZ);
        }

        /// <summary>
        /// Update waypoint visualization (markers and lines).
        /// </summary>
        private void UpdateWaypointVisualization()
        {
            // Waypoint markers are independent from destination indicators to avoid lifecycle conflicts
            // (destination indicators have fade out, hide on arrival, etc. that would affect waypoint markers)
            if (!showWaypointMarkers && !showWaypointLines) return;

            // Clear existing visualization
            ClearWaypointVisualization();

            if (_queuedWaypoints.Count == 0) return;

            // Create waypoint markers (uses waypointMarkerPrefab, independent from destination indicator)
            if (showWaypointMarkers && waypointMarkerPrefab != null)
            {
                for (int i = 0; i < _queuedWaypoints.Count; i++)
                {
                    GameObject marker = GetOrCreateWaypointMarker(waypointMarkerPrefab);
                    marker.transform.position = _queuedWaypoints[i] + waypointMarkerOffset;
                    marker.SetActive(true);
                    _waypointMarkerInstances.Add(marker);
                }
            }

            // Create/update waypoint line
            if (showWaypointLines && _queuedWaypoints.Count >= 2)
            {
                if (_waypointPathLineRenderer == null)
                {
                    CreateWaypointLineRenderer();
                }

                if (_waypointPathLineRenderer != null)
                {
                    _waypointPathLineRenderer.positionCount = _queuedWaypoints.Count;
                    
                    for (int i = 0; i < _queuedWaypoints.Count; i++)
                    {
                        _waypointPathLineRenderer.SetPosition(i, _queuedWaypoints[i] + Vector3.up * lineHeightOffset);
                    }
                    
                    // Set color based on whether it's a loop preview
                    Color lineColor = _isPatrolLoop ? patrolLoopLineColor : waypointLineColor;
                    _waypointPathLineRenderer.startColor = lineColor;
                    _waypointPathLineRenderer.endColor = lineColor;
                    
                    _waypointPathLineRenderer.enabled = true;
                }
            }
        }

        /// <summary>
        /// Clear waypoint visualization.
        /// </summary>
        private void ClearWaypointVisualization()
        {
            // Return markers to pool
            foreach (var marker in _waypointMarkerInstances)
            {
                if (marker != null)
                {
                    marker.SetActive(false);
                    _waypointMarkerPool.Enqueue(marker);
                }
            }
            _waypointMarkerInstances.Clear();

            // Hide line renderer
            if (_waypointPathLineRenderer != null)
            {
                _waypointPathLineRenderer.enabled = false;
            }
            
            _activeVisualizationPatrolPath = null;
            _isShowingActivePatrol = false;
        }

        /// <summary>
        /// Update patrol path visualization based on currently selected agents.
        /// Shows the patrol path if any selected agent has one.
        /// Optimized for large selections (thousands of agents).
        /// </summary>
        private void UpdatePatrolPathVisualizationForSelection()
        {
            // Don't update if we're actively queuing waypoints (that has its own visualization)
            if (_isQueuingWaypoints) return;
            
            // OPTIMIZATION: Early exit if no patrol paths exist at all - O(1)
            // This is the most common case and avoids iterating through thousands of selected agents
            if (_agentPatrolPaths.Count == 0 && _externalPatrolPaths.Count == 0)
            {
                if (_isShowingActivePatrol)
                {
                    ClearWaypointVisualization();
                }
                return;
            }
            
            // OPTIMIZATION: Early exit if nothing is selected
            if (TotalSelectedCount == 0)
            {
                if (_isShowingActivePatrol)
                {
                    ClearWaypointVisualization();
                }
                return;
            }
            
            // Find a patrol path from the selected agents
            AgentPatrolPath patrolPath = null;
            bool isLoop = false;
            
            // OPTIMIZATION: Choose iteration strategy based on collection sizes
            // Dictionary.TryGetValue is O(1), List.Contains is O(n)
            // If we have few patrol paths, iterate over them and check if selected
            // If we have many patrol paths, iterate over selection and do dictionary lookup
            
            if (_agentPatrolPaths.Count > 0)
            {
                if (_agentPatrolPaths.Count <= _selectedAgentIndices.Count)
                {
                    // Fewer patrol paths than selected agents - iterate over patrol paths
                    // Use _previousSelection (HashSet) for O(1) contains check
                    foreach (var kvp in _agentPatrolPaths)
                    {
                        if (_previousSelection.Contains(kvp.Key))
                        {
                            patrolPath = kvp.Value;
                            isLoop = patrolPath.isLoop;
                            break;
                        }
                    }
                }
                else
                {
                    // More patrol paths than selected agents - iterate over selection
                    // Dictionary lookup is O(1)
                    foreach (int agentIndex in _selectedAgentIndices)
                    {
                        if (_agentPatrolPaths.TryGetValue(agentIndex, out var path))
                        {
                            patrolPath = path;
                            isLoop = path.isLoop;
                            break;
                        }
                    }
                }
            }
            
            // Check external selectables if no crowd agent patrol found
            if (patrolPath == null && _externalPatrolPaths.Count > 0)
            {
                foreach (var selectable in _selectedExternalSelectables)
                {
                    if (_externalPatrolPaths.TryGetValue(selectable, out var path))
                    {
                        patrolPath = path;
                        isLoop = path.isLoop;
                        break;
                    }
                }
            }
            
            // If no patrol path found, clear visualization
            if (patrolPath == null)
            {
                if (_isShowingActivePatrol)
                {
                    ClearWaypointVisualization();
                }
                return;
            }
            
            // OPTIMIZATION: Skip rendering if we're already showing this exact patrol path
            if (_isShowingActivePatrol && _activeVisualizationPatrolPath == patrolPath)
            {
                return;
            }
            
            // Show the patrol path visualization
            ShowPatrolPathVisualization(patrolPath, isLoop);
        }
        
        /// <summary>
        /// Display visualization for an active patrol path.
        /// </summary>
        private void ShowPatrolPathVisualization(AgentPatrolPath patrolPath, bool isLoop)
        {
            if (patrolPath == null || patrolPath.waypoints.Count == 0) return;
            
            // Check if we should show any visualization
            // Waypoint markers are independent from destination indicators to avoid lifecycle conflicts
            // (destination indicators have fade out, hide on arrival, etc. that would affect waypoint markers)
            if (!showWaypointMarkers && !showWaypointLines) return;
            
            // Clear existing visualization first
            ClearWaypointVisualization();
            
            _activeVisualizationPatrolPath = patrolPath;
            _isShowingActivePatrol = true;
            
            List<Vector3> waypoints = patrolPath.waypoints;
            
            // Create waypoint markers (uses waypointMarkerPrefab, independent from destination indicator)
            if (showWaypointMarkers && waypointMarkerPrefab != null)
            {
                for (int i = 0; i < waypoints.Count; i++)
                {
                    GameObject marker = GetOrCreateWaypointMarker(waypointMarkerPrefab);
                    marker.transform.position = waypoints[i] + waypointMarkerOffset;
                    marker.SetActive(true);
                    _waypointMarkerInstances.Add(marker);
                }
            }
            
            // Create/update waypoint line
            if (showWaypointLines && waypoints.Count >= 2)
            {
                if (_waypointPathLineRenderer == null)
                {
                    CreateWaypointLineRenderer();
                }
                
                if (_waypointPathLineRenderer != null)
                {
                    // For loops, add an extra point to close the path visually
                    int pointCount = isLoop ? waypoints.Count + 1 : waypoints.Count;
                    _waypointPathLineRenderer.positionCount = pointCount;
                    
                    for (int i = 0; i < waypoints.Count; i++)
                    {
                        _waypointPathLineRenderer.SetPosition(i, waypoints[i] + Vector3.up * lineHeightOffset);
                    }
                    
                    // Close the loop by connecting back to first waypoint
                    if (isLoop)
                    {
                        _waypointPathLineRenderer.SetPosition(waypoints.Count, waypoints[0] + Vector3.up * lineHeightOffset);
                    }
                    
                    // Set color based on whether it's a loop
                    Color lineColor = isLoop ? patrolLoopLineColor : waypointLineColor;
                    _waypointPathLineRenderer.startColor = lineColor;
                    _waypointPathLineRenderer.endColor = lineColor;
                    
                    _waypointPathLineRenderer.enabled = true;
                }
            }
        }

        /// <summary>
        /// Get or create a waypoint marker from the pool.
        /// </summary>
        private GameObject GetOrCreateWaypointMarker(GameObject prefab)
        {
            if (_waypointMarkerPool.Count > 0)
            {
                return _waypointMarkerPool.Dequeue();
            }

            GameObject marker = Instantiate(prefab, transform);
            marker.name = "WaypointMarker";
            return marker;
        }

        /// <summary>
        /// Create the line renderer for waypoint paths.
        /// </summary>
        private void CreateWaypointLineRenderer()
        {
            GameObject lineObj = new GameObject("WaypointPathLine");
            lineObj.transform.SetParent(transform);
            
            _waypointPathLineRenderer = lineObj.AddComponent<LineRenderer>();
            _waypointPathLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _waypointPathLineRenderer.startWidth = waypointLineWidth;
            _waypointPathLineRenderer.endWidth = waypointLineWidth;
            _waypointPathLineRenderer.startColor = waypointLineColor;
            _waypointPathLineRenderer.endColor = waypointLineColor;
            _waypointPathLineRenderer.useWorldSpace = true;
            _waypointPathLineRenderer.enabled = false;
        }

        /// <summary>
        /// Check if an agent has an active patrol path.
        /// </summary>
        public bool HasPatrolPath(int agentIndex)
        {
            return _agentPatrolPaths.ContainsKey(agentIndex);
        }

        /// <summary>
        /// Get the patrol path for an agent (returns null if none).
        /// </summary>
        public AgentPatrolPath GetPatrolPath(int agentIndex)
        {
            return _agentPatrolPaths.TryGetValue(agentIndex, out var path) ? path : null;
        }

        /// <summary>
        /// Clean up waypoint-related resources.
        /// </summary>
        private void CleanupWaypointResources()
        {
            // Clear markers
            foreach (var marker in _waypointMarkerInstances)
            {
                if (marker != null)
                    Destroy(marker);
            }
            _waypointMarkerInstances.Clear();

            foreach (var marker in _waypointMarkerPool)
            {
                if (marker != null)
                    Destroy(marker);
            }
            _waypointMarkerPool.Clear();

            // Destroy line renderer
            if (_waypointPathLineRenderer != null)
            {
                Destroy(_waypointPathLineRenderer.gameObject);
                _waypointPathLineRenderer = null;
            }

            // Clear patrol paths
            _agentPatrolPaths.Clear();
            _externalPatrolPaths.Clear();
            _queuedWaypoints.Clear();
        }

        #endregion
        
        #region Network Command Helpers
        
        /// <summary>
        /// Fires a network move command event.
        /// </summary>
        private void FireNetworkMoveCommand(Vector3 targetPosition, IReadOnlyList<int> agentIndices, FormationType formation, float spacing, Vector3 facingDirection)
        {
            if (NetworkMoveCommandIssued == null) return;
            
            var networkIds = new uint[agentIndices.Count];
            var indices = new int[agentIndices.Count];
            
            for (int i = 0; i < agentIndices.Count; i++)
            {
                indices[i] = agentIndices[i];
                var agent = crowdController?.GetAgent(agentIndices[i]);
                networkIds[i] = agent?.networkId ?? 0;
            }
            
            var eventData = new CommandEventData
            {
                commandType = CommandType.Move,
                agentIndices = indices,
                networkIds = networkIds,
                targetPosition = targetPosition,
                targetAgentIndex = -1,
                targetNetworkId = 0,
                formation = formation,
                formationSpacing = spacing,
                facingDirection = facingDirection,
                playerId = -1, // Set by network layer if needed
                sequence = ++_commandSequence,
                timestamp = Time.time
            };
            
            NetworkMoveCommandIssued.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires a network attack command event.
        /// </summary>
        private void FireNetworkAttackCommand(int targetAgentIndex, uint targetNetworkId, IReadOnlyList<int> agentIndices)
        {
            if (NetworkAttackCommandIssued == null) return;
            
            var networkIds = new uint[agentIndices.Count];
            var indices = new int[agentIndices.Count];
            
            for (int i = 0; i < agentIndices.Count; i++)
            {
                indices[i] = agentIndices[i];
                var agent = crowdController?.GetAgent(agentIndices[i]);
                networkIds[i] = agent?.networkId ?? 0;
            }
            
            var eventData = new CommandEventData
            {
                commandType = CommandType.Attack,
                agentIndices = indices,
                networkIds = networkIds,
                targetPosition = Vector3.zero,
                targetAgentIndex = targetAgentIndex,
                targetNetworkId = targetNetworkId,
                formation = FormationType.Random,
                formationSpacing = 0f,
                facingDirection = Vector3.zero,
                playerId = -1,
                sequence = ++_commandSequence,
                timestamp = Time.time
            };
            
            NetworkAttackCommandIssued.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires a network stop command event.
        /// </summary>
        public void FireNetworkStopCommand(IReadOnlyList<int> agentIndices)
        {
            if (NetworkStopCommandIssued == null) return;
            
            var networkIds = new uint[agentIndices.Count];
            var indices = new int[agentIndices.Count];
            
            for (int i = 0; i < agentIndices.Count; i++)
            {
                indices[i] = agentIndices[i];
                var agent = crowdController?.GetAgent(agentIndices[i]);
                networkIds[i] = agent?.networkId ?? 0;
            }
            
            var eventData = new CommandEventData
            {
                commandType = CommandType.Stop,
                agentIndices = indices,
                networkIds = networkIds,
                targetPosition = Vector3.zero,
                targetAgentIndex = -1,
                targetNetworkId = 0,
                formation = FormationType.Random,
                formationSpacing = 0f,
                facingDirection = Vector3.zero,
                playerId = -1,
                sequence = ++_commandSequence,
                timestamp = Time.time
            };
            
            NetworkStopCommandIssued.Invoke(eventData);
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // Network-Aware Command Methods (Request/Validate/Apply Pattern)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Requests a move command through the network authority system.
        /// In single-player or on the server, command is executed immediately.
        /// On clients in multiplayer, this sends a request to the server.
        /// </summary>
        /// <param name="agentIndices">Local indices of agents to move</param>
        /// <param name="destination">Target position</param>
        /// <param name="formation">Formation type</param>
        /// <param name="spacing">Formation spacing</param>
        /// <param name="facingDirection">Optional facing direction (zero = auto)</param>
        /// <returns>True if command was accepted (or predicted)</returns>
        public bool RequestMoveCommand(IReadOnlyList<int> agentIndices, Vector3 destination, 
            FormationType formation = FormationType.Random, float spacing = -1f, Vector3 facingDirection = default)
        {
            if (agentIndices == null || agentIndices.Count == 0)
                return false;

            var commandAuthority = NetworkAuthorityProvider.CommandAuthority;
            
            if (commandAuthority == null)
            {
                // Single-player: execute directly
                ExecuteMoveCommandInternal(agentIndices, destination, formation, spacing, facingDirection);
                return true;
            }

            // Multiplayer: build command data and route through authority
            var networkIds = new uint[agentIndices.Count];
            for (int i = 0; i < agentIndices.Count; i++)
            {
                var agent = crowdController?.GetAgent(agentIndices[i]);
                networkIds[i] = agent?.networkId ?? 0;
            }

            var command = new MoveCommandData
            {
                agentNetworkIds = networkIds,
                destination = destination,
                formation = formation,
                formationSpacing = spacing >= 0 ? spacing : formationSpacing,
                facingDirection = facingDirection,
                playerId = commandAuthority.LocalPlayerId,
                sequence = commandAuthority.GetNextCommandSequence(),
                clientTime = Time.time
            };

            bool shouldPredict = commandAuthority.OnMoveCommand(command);
            
            if (shouldPredict)
            {
                // Execute locally for client prediction
                ExecuteMoveCommandInternal(agentIndices, destination, formation, spacing, facingDirection);
            }

            return shouldPredict;
        }

        /// <summary>
        /// Requests an attack command through the network authority system.
        /// </summary>
        /// <param name="agentIndices">Local indices of attacking agents</param>
        /// <param name="targetIndex">Local index of target agent</param>
        /// <returns>True if command was accepted (or predicted)</returns>
        public bool RequestAttackCommand(IReadOnlyList<int> agentIndices, int targetIndex)
        {
            if (agentIndices == null || agentIndices.Count == 0 || targetIndex < 0)
                return false;

            var targetAgent = crowdController?.GetAgent(targetIndex);
            if (targetAgent == null)
                return false;

            var commandAuthority = NetworkAuthorityProvider.CommandAuthority;
            
            if (commandAuthority == null)
            {
                // Single-player: execute directly
                ExecuteAttackCommandInternal(agentIndices, targetIndex);
                return true;
            }

            // Multiplayer: build command data and route through authority
            var networkIds = new uint[agentIndices.Count];
            for (int i = 0; i < agentIndices.Count; i++)
            {
                var agent = crowdController?.GetAgent(agentIndices[i]);
                networkIds[i] = agent?.networkId ?? 0;
            }

            var command = new AttackCommandData
            {
                attackerNetworkIds = networkIds,
                targetNetworkId = targetAgent.networkId,
                playerId = commandAuthority.LocalPlayerId,
                sequence = commandAuthority.GetNextCommandSequence(),
                clientTime = Time.time
            };

            bool shouldPredict = commandAuthority.OnAttackCommand(command);
            
            if (shouldPredict)
            {
                // Execute locally for client prediction
                ExecuteAttackCommandInternal(agentIndices, targetIndex);
            }

            return shouldPredict;
        }

        /// <summary>
        /// Requests a stop command through the network authority system.
        /// </summary>
        /// <param name="agentIndices">Local indices of agents to stop</param>
        /// <param name="holdPosition">Whether to hold position</param>
        /// <returns>True if command was accepted</returns>
        public bool RequestStopCommand(IReadOnlyList<int> agentIndices, bool holdPosition = false)
        {
            if (agentIndices == null || agentIndices.Count == 0)
                return false;

            var commandAuthority = NetworkAuthorityProvider.CommandAuthority;
            
            if (commandAuthority == null)
            {
                // Single-player: execute directly
                ExecuteStopCommandInternal(agentIndices, holdPosition);
                return true;
            }

            // Multiplayer: build command data and route through authority
            var networkIds = new uint[agentIndices.Count];
            for (int i = 0; i < agentIndices.Count; i++)
            {
                var agent = crowdController?.GetAgent(agentIndices[i]);
                networkIds[i] = agent?.networkId ?? 0;
            }

            var command = new StopCommandData
            {
                agentNetworkIds = networkIds,
                holdPosition = holdPosition,
                playerId = commandAuthority.LocalPlayerId,
                sequence = commandAuthority.GetNextCommandSequence()
            };

            bool shouldPredict = commandAuthority.OnStopCommand(command);
            
            if (shouldPredict)
            {
                // Execute locally for client prediction
                ExecuteStopCommandInternal(agentIndices, holdPosition);
            }

            return shouldPredict;
        }

        /// <summary>
        /// Executes a move command received from the server.
        /// Use for remote player commands and server confirmations.
        /// </summary>
        public void ExecuteServerMoveCommand(uint[] agentNetworkIds, Vector3 destination, 
            FormationType formation, float spacing, Vector3 facingDirection)
        {
            if (agentNetworkIds == null || agentNetworkIds.Length == 0)
                return;

            var indices = new List<int>(agentNetworkIds.Length);
            foreach (uint networkId in agentNetworkIds)
            {
                int index = crowdController?.GetAgentIndexByNetworkId(networkId) ?? -1;
                if (index >= 0)
                    indices.Add(index);
            }

            if (indices.Count > 0)
            {
                ExecuteMoveCommandInternal(indices, destination, formation, spacing, facingDirection);
            }
        }

        /// <summary>
        /// Executes an attack command received from the server.
        /// </summary>
        public void ExecuteServerAttackCommand(uint[] attackerNetworkIds, uint targetNetworkId)
        {
            if (attackerNetworkIds == null || attackerNetworkIds.Length == 0)
                return;

            int targetIndex = crowdController?.GetAgentIndexByNetworkId(targetNetworkId) ?? -1;
            if (targetIndex < 0)
                return;

            var indices = new List<int>(attackerNetworkIds.Length);
            foreach (uint networkId in attackerNetworkIds)
            {
                int index = crowdController?.GetAgentIndexByNetworkId(networkId) ?? -1;
                if (index >= 0)
                    indices.Add(index);
            }

            if (indices.Count > 0)
            {
                ExecuteAttackCommandInternal(indices, targetIndex);
            }
        }

        /// <summary>
        /// Executes a stop command received from the server.
        /// </summary>
        public void ExecuteServerStopCommand(uint[] agentNetworkIds, bool holdPosition)
        {
            if (agentNetworkIds == null || agentNetworkIds.Length == 0)
                return;

            var indices = new List<int>(agentNetworkIds.Length);
            foreach (uint networkId in agentNetworkIds)
            {
                int index = crowdController?.GetAgentIndexByNetworkId(networkId) ?? -1;
                if (index >= 0)
                    indices.Add(index);
            }

            if (indices.Count > 0)
            {
                ExecuteStopCommandInternal(indices, holdPosition);
            }
        }

        // Internal execution methods (bypasses network for direct execution)
        
        private void ExecuteMoveCommandInternal(IReadOnlyList<int> agentIndices, Vector3 destination, 
            FormationType formation, float spacing, Vector3 facingDirection)
        {
            // Fire network event for subscribers
            FireNetworkMoveCommand(destination, agentIndices, formation, spacing, facingDirection);
            
            // Execute the actual move using existing formation move logic
            float actualSpacing = spacing >= 0 ? spacing : formationSpacing;
            float originalSpacing = formationSpacing;
            formationSpacing = actualSpacing;
            
            // Temporarily select these agents and move them
            MoveAgentsToFormationInternal(agentIndices, destination, formation, actualSpacing, facingDirection);
            
            formationSpacing = originalSpacing;
        }
        
        /// <summary>
        /// Internal method to move specific agents to a formation position.
        /// </summary>
        private void MoveAgentsToFormationInternal(IReadOnlyList<int> agentIndices, Vector3 destination,
            FormationType formation, float spacing, Vector3 facingDirection)
        {
            if (agentIndices == null || agentIndices.Count == 0)
                return;
                
            var agents = crowdController?.GetAgents();
            if (agents == null)
                return;
            
            // Calculate formation positions and move each agent
            int validIndex = 0;
            for (int i = 0; i < agentIndices.Count; i++)
            {
                int agentIndex = agentIndices[i];
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;
                    
                var agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;
                
                // Calculate formation position
                Vector3 formationPos = CalculateFormationPositionWithParams(
                    validIndex, agentIndices.Count, destination, formation, spacing);
                
                // Apply facing rotation if specified
                if (facingDirection != Vector3.zero)
                {
                    // Convert direction to Y-axis angle in degrees
                    float angle = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
                    agent.formationFacingAngle = angle;
                }
                
                // Set agent destination
                if (agent.IsOnNavMesh)
                {
                    agent.navigationAgent.SetDestination(formationPos);
                }
                
                validIndex++;
            }
        }

        private void ExecuteAttackCommandInternal(IReadOnlyList<int> agentIndices, int targetIndex)
        {
            var targetAgent = crowdController?.GetAgent(targetIndex);
            if (targetAgent == null)
                return;

            // Fire network event for subscribers
            FireNetworkAttackCommand(targetIndex, targetAgent.networkId, agentIndices);

            // Execute the actual attack command
            foreach (int agentIndex in agentIndices)
            {
                var agent = crowdController?.GetAgent(agentIndex);
                if (agent != null && !agent.isDead)
                {
                    // Set attack target
                    agent.attackTargetAgentIndex = targetIndex;
                    agent.hasAttackTargetPosition = true;
                    agent.attackTargetPosition = crowdController.GetAgentPosition(targetIndex);
                    agent.hasManualCommand = true;
                    
                    // Navigate towards target using navigation agent directly
                    var targetPos = crowdController.GetAgentPosition(targetIndex);
                    if (agent.navigationAgent != null && agent.IsOnNavMesh)
                    {
                        agent.navigationAgent.SetDestination(targetPos);
                    }
                }
            }
        }

        private void ExecuteStopCommandInternal(IReadOnlyList<int> agentIndices, bool holdPosition)
        {
            // Fire network event for subscribers
            FireNetworkStopCommand(agentIndices);

            // Execute the actual stop command
            foreach (int agentIndex in agentIndices)
            {
                var agent = crowdController?.GetAgent(agentIndex);
                if (agent != null && !agent.isDead)
                {
                    // Clear movement
                    agent.navigationAgent?.ResetPath();
                    agent.hasManualCommand = false;
                    agent.attackTargetAgentIndex = -1;
                    agent.hasAttackTargetPosition = false;
                    agent.attackTargetTransform = null;
                    agent.isChasing = false;
                    agent.isAggroed = false;
                    agent.ClearCommandIntent();
                    
                    if (holdPosition)
                    {
                        agent.combatStance = CombatStance.HoldPosition;
                        agent.holdPositionLocation = crowdController.GetAgentPosition(agentIndex);
                        agent.hasHoldPositionSet = true;
                    }
                }
            }
        }
        
        #endregion
    }
}
