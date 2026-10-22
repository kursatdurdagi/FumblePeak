using UnityEngine;
using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Defines a formation zone where agents can be placed at predefined spots.
    /// Works like a custom formation preset - agents are commanded to available spots
    /// using the existing RTS/pathfinding systems.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Placement/Wall Placement Zone")]
    public class EnemyMassesWallPlacementZone : MonoBehaviour
    {
        #region Enums
        
        /// <summary>
        /// How spots are arranged in the zone.
        /// </summary>
        public enum SpotArrangement
        {
            /// <summary>Single row of spots along the zone's forward direction.</summary>
            Line,
            /// <summary>Grid of spots (rows x columns).</summary>
            Grid,
            /// <summary>Spots arranged in a circle around the center.</summary>
            Circle,
            /// <summary>Manually placed spots using child transforms.</summary>
            Manual
        }
        
        /// <summary>
        /// How agents reach their assigned spot.
        /// </summary>
        public enum PlacementMode
        {
            /// <summary>Agent instantly teleports/warps to the spot position (only if within max distance).</summary>
            Instant,
            /// <summary>Agent pathfinds to the spot from any distance.</summary>
            Pathfind
        }
        
        /// <summary>
        /// Where to spawn status effect VFX.
        /// </summary>
        public enum VFXSpawnLocation
        {
            /// <summary>Spawn VFX at the zone center (single VFX for all agents).</summary>
            ZoneCenter,
            /// <summary>Spawn VFX on each agent that arrives (one VFX per agent).</summary>
            PerAgent
        }

        /// <summary>
        /// Determines how agents face when placed at a wall spot.
        /// The wall normal is computed automatically for dynamic walls, or derived
        /// from the zone's forward direction for static zones.
        /// </summary>
        public enum SpotFacingMode
        {
            /// <summary>Keep the agent's existing rotation.</summary>
            None,
            /// <summary>Face away from the wall (outward — default for defense/cover).</summary>
            FacingAway,
            /// <summary>Face toward the wall (for repair, interact, breach).</summary>
            FacingWall,
            /// <summary>Face along the wall in the polyline/forward direction.</summary>
            AlongWallForward,
            /// <summary>Face along the wall opposite to the polyline/forward direction.</summary>
            AlongWallBackward
        }
        
        #endregion
        
        #region Settings
        
        [Header("Zone Settings")]
        [Tooltip("Reference to the crowd controller managing agents.")]
        [SerializeField] private EnemyMassesCrowdController crowdController;
        
        [Tooltip("How spots are arranged in the zone.")]
        [SerializeField] private SpotArrangement arrangement = SpotArrangement.Line;
        
        [Tooltip("Number of spots in the zone (for Line/Circle) or spots per row (for Grid).")]
        [SerializeField] [Min(1)] private int spotCount = 5;
        
        [Tooltip("Number of rows (for Grid arrangement).")]
        [SerializeField] [Min(1)] private int rowCount = 1;
        
        [Tooltip("Spacing between spots.")]
        [SerializeField] [Min(0.5f)] private float spotSpacing = 2f;
        
        [Tooltip("Radius for Circle arrangement.")]
        [SerializeField] [Min(1f)] private float circleRadius = 5f;
        
        [Header("Placement Mode")]
        [Tooltip("How agents reach their assigned spot.")]
        [SerializeField] private PlacementMode placementMode = PlacementMode.Instant;
        
        [Tooltip("Maximum distance from zone center for Instant mode. Agents farther will pathfind instead.")]
        [SerializeField] [Min(1f)] private float instantModeMaxDistance = 15f;
        
        [Header("Auto-Assign Settings")]
        [Tooltip("Enable distance-based automatic assignment of nearby agents to spots.")]
        [SerializeField] private bool enableAutoAssign = false;
        
        [Tooltip("Distance within which agents are automatically assigned to available spots.")]
        [SerializeField] [Min(1f)] private float autoAssignRadius = 10f;
        
        [Tooltip("How often to check for agents to auto-assign (seconds).")]
        [SerializeField] [Min(0.1f)] private float autoAssignInterval = 0.5f;
        
        [Tooltip("Only auto-assign agents from these EnemyCrowd indices. Empty = allow all.")]
        [SerializeField] private List<int> allowedEnemyCrowdIndices = new List<int>();
        
        [Header("Facing")]
        [Tooltip("How agents face when placed at a wall spot. Requires a wall normal per spot (set automatically for dynamic walls, uses zone forward for static).")]
        [SerializeField] private SpotFacingMode spotFacingMode = SpotFacingMode.FacingWall;

        [Tooltip("If true, agents face toward the zone center when at their spot. Only used when Spot Facing Mode is None.")]
        [SerializeField] private bool faceCenter = true;
        
        [Tooltip("Custom facing direction (if faceCenter is false and spotFacingMode is None). Uses zone's forward if zero.")]
        [SerializeField] private Vector3 customFacingDirection = Vector3.zero;
        
        [Header("Combat Stance On Arrival")]
        [Tooltip("Reference to the RTS Controller for setting combat stance.")]
        [SerializeField] private EnemyMassesRTSController rtsController;
        
        [Tooltip("If true, agents will have their combat stance changed when they arrive at their spot.")]
        [SerializeField] private bool setStanceOnArrival = false;
        
        [Tooltip("The combat stance to apply when agents arrive at their spot.")]
        [SerializeField] private CombatStance arrivalStance = CombatStance.HoldPosition;
        
        [Header("Status Effects")]
        [Tooltip("If true, applies a status effect to agents when they arrive at their spot.")]
        [SerializeField] private bool applyStatusEffectOnArrival = false;
        
        [Tooltip("Type of status effect to apply to agents at their spots.")]
        [SerializeField] private StatusEffectType arrivalStatusEffectType = StatusEffectType.ArmorBuff;
        
        [Tooltip("Value of the status effect (depends on type - multiplier for buffs, damage for DOTs).")]
        [SerializeField] private float statusEffectValue = 1.5f;
        
        [Tooltip("How long the effect lasts after agent leaves the spot (0 = removed immediately).")]
        [SerializeField] [Min(0f)] private float statusEffectLingerDuration = 0f;
        
        [Header("Status Effect VFX")]
        [Tooltip("VFX type to use for status effect visuals.")]
        [SerializeField] private VFXType statusEffectVFXType = VFXType.None;
        
        [Tooltip("ParticleSystem prefab for status effect (if VFX type is ParticleSystem).")]
        [SerializeField] private GameObject statusEffectParticleSystem;
        
        [Tooltip("VFX Graph asset for status effect (if VFX type is VFXGraph).")]
        [SerializeField] private UnityEngine.VFX.VisualEffectAsset statusEffectVFXGraph;
        
        [Tooltip("Where to spawn the status effect VFX.")]
        [SerializeField] private VFXSpawnLocation statusEffectVFXLocation = VFXSpawnLocation.PerAgent;
        
        [Header("Arrival/Departure Audio")]
        [Tooltip("Sound to play when an agent arrives at their spot.")]
        [SerializeField] private AudioClip arrivalSound;
        
        [Tooltip("Volume of arrival sound.")]
        [SerializeField] [Range(0f, 1f)] private float arrivalSoundVolume = 1f;
        
        [Tooltip("Random pitch variation for arrival sound (±value).")]
        [SerializeField] [Range(0f, 0.5f)] private float arrivalSoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for arrival sound (0 = 2D, 1 = 3D).")]
        [SerializeField] [Range(0f, 1f)] private float arrivalSoundSpatialBlend = 1f;
        
        [Tooltip("Sound to play when an agent leaves their spot.")]
        [SerializeField] private AudioClip departureSound;
        
        [Tooltip("Volume of departure sound.")]
        [SerializeField] [Range(0f, 1f)] private float departureSoundVolume = 1f;
        
        [Tooltip("Random pitch variation for departure sound (±value).")]
        [SerializeField] [Range(0f, 0.5f)] private float departureSoundPitchVariation = 0.05f;
        
        [Tooltip("Spatial blend for departure sound (0 = 2D, 1 = 3D).")]
        [SerializeField] [Range(0f, 1f)] private float departureSoundSpatialBlend = 1f;
        
        [Tooltip("Maximum concurrent audio sources for arrival/departure sounds (prevents audio spam).")]
        [SerializeField] [Range(1, 8)] private int maxConcurrentAudioSources = 4;
        
        [Header("Visualization (Editor)")]
        [Tooltip("Show spot gizmos in Scene view.")]
        [SerializeField] private bool showGizmos = true;
        
        [Tooltip("Color for available spots.")]
        [SerializeField] private Color availableSpotColor = new Color(0f, 1f, 0f, 0.5f);
        
        [Tooltip("Color for occupied spots.")]
        [SerializeField] private Color occupiedSpotColor = new Color(1f, 0.5f, 0f, 0.5f);
        
        [Tooltip("Size of spot gizmos.")]
        [SerializeField] [Min(0.1f)] private float gizmoSize = 0.5f;
        
        [Header("Runtime Visualization")]
        [Tooltip("Show the zone outline at runtime using a LineRenderer.")]
        [SerializeField] private bool showRuntimeOutline = false;
        
        [Tooltip("Color of the zone outline.")]
        [SerializeField] private Color outlineColor = new Color(0.3f, 0.8f, 1f, 0.8f);
        
        [Tooltip("Width of the outline line.")]
        [SerializeField] [Range(0.01f, 0.5f)] private float outlineWidth = 0.05f;
        
        [Tooltip("Show spot markers at runtime (sphere indicators).")]
        [SerializeField] private bool showRuntimeSpotMarkers = false;
        
        [Tooltip("Prefab for spot markers. If null, uses a simple sphere primitive.")]
        [SerializeField] private GameObject spotMarkerPrefab;
        
        [Tooltip("Size of spot marker spheres.")]
        [SerializeField] [Range(0.1f, 2f)] private float spotMarkerSize = 0.5f;
        
        [Tooltip("Color for available spot markers at runtime.")]
        [SerializeField] private Color runtimeAvailableColor = new Color(0f, 1f, 0f, 0.5f);
        
        [Tooltip("Color for occupied spot markers at runtime.")]
        [SerializeField] private Color runtimeOccupiedColor = new Color(1f, 0.5f, 0f, 0.5f);
        
        [Header("Debug")]
        [Tooltip("Enable debug logging for troubleshooting.")]
        [SerializeField] private bool enableDebugLogging = false;
        
        #endregion
        
        #region Runtime Data
        
        /// <summary>
        /// Represents a placement spot in the zone.
        /// </summary>
        public class PlacementSpot
        {
            public int index;
            public Vector3 localPosition;
            public Vector3 worldPosition;
            public int assignedAgentIndex = -1;
            public bool isOccupied => assignedAgentIndex >= 0;

            /// <summary>
            /// Wall normal direction for this spot (points away from the wall surface).
            /// Set automatically by EnemyMassesDynamicWallPlacementRuntime for dynamic walls.
            /// For static zones, defaults to the zone's forward direction.
            /// </summary>
            public Vector3 wallNormal;

            /// <summary>
            /// Wall tangent direction for this spot (along the wall surface).
            /// Set automatically by EnemyMassesDynamicWallPlacementRuntime for dynamic walls.
            /// For static zones, defaults to the zone's right direction.
            /// </summary>
            public Vector3 wallTangent;
        }
        
        private List<PlacementSpot> _spots = new List<PlacementSpot>();
        private float _nextAutoAssignTime;
        private bool _initialized;
        
        // Track agents moving to spots
        private Dictionary<int, int> _agentToSpotAssignment = new Dictionary<int, int>();
        
        // Track agents pathfinding toward instant teleport (will be warped when close enough)
        private HashSet<int> _agentsPendingInstantWarp = new HashSet<int>();
        
        // Track agents that have already had their stance applied (avoid applying multiple times)
        private HashSet<int> _agentsStanceApplied = new HashSet<int>();
        
        // Track agents that have arrived at their spot and received status effects
        private HashSet<int> _agentsArrivedWithEffects = new HashSet<int>();

        // Throttle per-agent arrival diagnostics to avoid excessive debug spam.
        private Dictionary<int, float> _arrivalDebugNextLogTime = new Dictionary<int, float>();

        private enum WallPlacementAnimatorParameterType
        {
            None,
            Bool,
            Int,
            Float,
            Trigger
        }

        private struct WallPlacementAnimationState
        {
            public string parameterName;
            public WallPlacementAnimatorParameterType parameterType;
            public bool usedClip;
            public bool hasPendingLoopClip;
            public float pendingLoopStartTime;
            public AnimationClip pendingLoopClip;
            public float nextLoopReinforceTime;
            public HybridWorkflowType appliedWorkflow;
        }

        // Tracks per-agent wall-placement animation state so we can cleanly restore on departure.
        private Dictionary<int, WallPlacementAnimationState> _wallPlacementAnimationStates = new Dictionary<int, WallPlacementAnimationState>();
        
        // Audio source pooling for arrival/departure sounds
        private List<AudioSource> _audioSourcePool;
        private Queue<int> _freeAudioSources;
        private float[] _audioSourceReleaseTime;
        private Transform _audioPoolContainer;
        
        // VFX instance spawned at zone center (if using ZoneCenter mode)
        private GameObject _centralVFXInstance;
        
        // Track per-agent VFX instances (if using PerAgent mode)
        private Dictionary<int, GameObject> _agentVFXInstances = new Dictionary<int, GameObject>();
        
        // Status effect system reference
        private StatusEffectSystem _statusEffectSystem;
        
        // Runtime visualization
        private LineRenderer _outlineRenderer;
        private List<GameObject> _spotMarkers = new List<GameObject>();
        private Transform _spotMarkersParent;
        private Material _availableMarkerMaterial;
        private Material _occupiedMarkerMaterial;
        
        #endregion
        
        #region Properties
        
        /// <summary>Gets the crowd controller reference.</summary>
        public EnemyMassesCrowdController CrowdController => crowdController;
        
        /// <summary>Gets all placement spots.</summary>
        public IReadOnlyList<PlacementSpot> Spots => _spots;
        
        /// <summary>Gets the number of available (unoccupied) spots.</summary>
        public int AvailableSpots => _spots.FindAll(s => !s.isOccupied).Count;
        
        /// <summary>Gets the number of occupied spots.</summary>
        public int OccupiedSpots => _spots.FindAll(s => s.isOccupied).Count;

        /// <summary>
        /// Returns true if any assigned agents are still pathfinding toward their spots.
        /// Used by the dynamic wall placement runtime to lock rebuilds until all agents arrive.
        /// Only counts agents whose current command destination still matches the assigned spot;
        /// agents that have been redirected (e.g. to attack) are not considered in transit.
        /// </summary>
        public bool HasAgentsInTransit
        {
            get
            {
                if (crowdController == null || _agentToSpotAssignment.Count == 0)
                    return false;

                var agents = crowdController.GetAgents();
                foreach (var kvp in _agentToSpotAssignment)
                {
                    int agentIndex = kvp.Key;
                    int spotIndex = kvp.Value;

                    if (agentIndex < 0 || agentIndex >= agents.Count)
                        continue;

                    var agent = agents[agentIndex];
                    if (agent == null || agent.isDead)
                        continue;

                    // Agents queued for instant warp are always considered in transit.
                    if (_agentsPendingInstantWarp.Contains(agentIndex))
                        return true;

                    // Only count as in transit if the agent has an active manual command AND
                    // their destination still points at the assigned spot (not redirected elsewhere).
                    if (!agent.hasManualCommand)
                        continue;

                    var spot = GetSpot(spotIndex);
                    if (spot == null)
                        continue;

                    float destDistSqr = (agent.manualCommandDestination - spot.worldPosition).sqrMagnitude;
                    // Tolerance of ~2m to account for navmesh sampling offsets.
                    if (destDistSqr < 4f)
                        return true;
                }

                return false;
            }
        }
        
        /// <summary>Gets or sets whether auto-assign is enabled.</summary>
        public bool EnableAutoAssign
        {
            get => enableAutoAssign;
            set => enableAutoAssign = value;
        }
        
        /// <summary>Gets the spot arrangement type.</summary>
        public SpotArrangement Arrangement => arrangement;
        
        /// <summary>Gets the placement mode.</summary>
        public PlacementMode Mode => placementMode;
        
        /// <summary>Gets the max distance for instant mode.</summary>
        public float InstantModeMaxDistance => instantModeMaxDistance;
        
        /// <summary>Gets the spot count setting.</summary>
        public int SpotCount => spotCount;
        
        /// <summary>Gets the row count for grid arrangement.</summary>
        public int RowCount => rowCount;
        
        /// <summary>Gets the spot spacing.</summary>
        public float SpotSpacing => spotSpacing;
        
        /// <summary>Gets the circle radius.</summary>
        public float CircleRadius => circleRadius;
        
        #endregion
        
        #region Events
        
        /// <summary>Invoked when an agent is assigned to a spot.</summary>
        public event System.Action<EnemyMassesWallPlacementZone, int, int> OnAgentAssigned;
        
        /// <summary>Invoked when an agent leaves a spot.</summary>
        public event System.Action<EnemyMassesWallPlacementZone, int, int> OnAgentLeft;
        
        /// <summary>Invoked when all spots become occupied.</summary>
        public event System.Action<EnemyMassesWallPlacementZone> OnZoneFull;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            Initialize();
        }
        
        private void Start()
        {
            if (crowdController == null)
            {
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
                if (crowdController == null)
                {
                    Debug.LogError($"[WallPlacementZone] {name}: No EnemyMassesCrowdController found!");
                    enabled = false;
                    return;
                }
            }
            
            // Initialize audio pool for arrival/departure sounds
            if (arrivalSound != null || departureSound != null)
            {
                InitializeAudioPool(maxConcurrentAudioSources);
            }
            
            // Get status effect system reference from CombatSystem
            var combatSystem = crowdController.CombatSystem;
            if (combatSystem != null)
            {
                _statusEffectSystem = combatSystem.GetStatusEffectSystem();
            }
            
            // Initialize runtime visualization
            InitializeRuntimeVisualization();
        }
        
        private void OnDestroy()
        {
            ClearWallPlacementAnimationsFromAssignedAgents();

            // Clean up audio pool
            if (_audioPoolContainer != null)
            {
                Destroy(_audioPoolContainer.gameObject);
            }
            
            // Clean up central VFX instance
            if (_centralVFXInstance != null)
            {
                Destroy(_centralVFXInstance);
            }
            
            // Clean up per-agent VFX instances
            foreach (var vfxInstance in _agentVFXInstances.Values)
            {
                if (vfxInstance != null)
                    Destroy(vfxInstance);
            }
            _agentVFXInstances.Clear();
            
            // Clean up runtime visualization
            CleanupRuntimeVisualization();
        }
        
        private void Update()
        {
            if (!_initialized || crowdController == null)
                return;
            
            // Update world positions of spots
            UpdateSpotWorldPositions();
            
            // Check for agents pending instant warp (pathfinding until close enough)
            if (placementMode == PlacementMode.Instant && _agentsPendingInstantWarp.Count > 0)
            {
                CheckPendingInstantWarps();
            }
            
            // Check for agents that have left their spots
            CheckAgentPositions();

            // Ensure wall-placement enter->loop transitions complete even if arrival checks are intermittent.
            UpdateWallPlacementAnimationTransitions();
            
            // Auto-assign nearby agents if enabled
            if (enableAutoAssign && Time.time >= _nextAutoAssignTime)
            {
                _nextAutoAssignTime = Time.time + autoAssignInterval;
                AutoAssignNearbyAgents();
            }
            
            // Update runtime visualization
            UpdateRuntimeVisualization();
        }
        
        private void OnValidate()
        {
            // Regenerate spots when settings change in editor
            if (Application.isPlaying && _initialized)
            {
                GenerateSpots();
            }
        }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes the zone and generates spots.
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
                return;
            
            GenerateSpots();
            _initialized = true;
        }
        
        /// <summary>
        /// Generates placement spots based on arrangement settings.
        /// </summary>
        public void GenerateSpots()
        {
            _spots.Clear();
            _agentToSpotAssignment.Clear();
            
            switch (arrangement)
            {
                case SpotArrangement.Line:
                    GenerateLineSpots();
                    break;
                case SpotArrangement.Grid:
                    GenerateGridSpots();
                    break;
                case SpotArrangement.Circle:
                    GenerateCircleSpots();
                    break;
                case SpotArrangement.Manual:
                    GenerateManualSpots();
                    break;
            }
            
            UpdateSpotWorldPositions();
        }
        
        private void GenerateLineSpots()
        {
            float totalWidth = (spotCount - 1) * spotSpacing;
            float startX = -totalWidth / 2f;
            
            for (int i = 0; i < spotCount; i++)
            {
                _spots.Add(new PlacementSpot
                {
                    index = i,
                    localPosition = new Vector3(startX + i * spotSpacing, 0f, 0f)
                });
            }
        }
        
        private void GenerateGridSpots()
        {
            float totalWidth = (spotCount - 1) * spotSpacing;
            float totalDepth = (rowCount - 1) * spotSpacing;
            float startX = -totalWidth / 2f;
            float startZ = -totalDepth / 2f;
            
            int index = 0;
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < spotCount; col++)
                {
                    _spots.Add(new PlacementSpot
                    {
                        index = index++,
                        localPosition = new Vector3(startX + col * spotSpacing, 0f, startZ + row * spotSpacing)
                    });
                }
            }
        }
        
        private void GenerateCircleSpots()
        {
            float angleStep = 360f / spotCount;
            
            for (int i = 0; i < spotCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                _spots.Add(new PlacementSpot
                {
                    index = i,
                    localPosition = new Vector3(
                        Mathf.Sin(angle) * circleRadius,
                        0f,
                        Mathf.Cos(angle) * circleRadius
                    )
                });
            }
        }
        
        private void GenerateManualSpots()
        {
            int index = 0;
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeInHierarchy || IsInternalRuntimeChild(child))
                    continue;

                _spots.Add(new PlacementSpot
                {
                    index = index++,
                    localPosition = transform.InverseTransformPoint(child.position)
                });
            }
            
            if (_spots.Count == 0)
            {
                bool suppressWarning = IsDynamicRuntimeManagedZone();
                if (!suppressWarning)
                {
                    Debug.LogWarning($"[WallPlacementZone] {name}: Manual arrangement has no child transforms for spots!");
                }
            }
        }

        private bool IsInternalRuntimeChild(Transform child)
        {
            if (child == null)
                return false;

            // Ignore helper objects created by this class so they are never interpreted as manual spots.
            if (child == _spotMarkersParent || child == _audioPoolContainer)
                return true;

            if (_outlineRenderer != null && child == _outlineRenderer.transform)
                return true;

            string childName = child.name;
            return childName == "SpotMarkers" ||
                   childName == "ZoneOutline" ||
                   childName == "WallPlacementZone_AudioPool";
        }

        private bool IsDynamicRuntimeManagedZone()
        {
            var directRuntime = GetComponent<EnemyMassesDynamicWallPlacementRuntime>();
            if (directRuntime != null && directRuntime.RuntimeZone == this)
                return true;

            var parentRuntime = GetComponentInParent<EnemyMassesDynamicWallPlacementRuntime>();
            if (parentRuntime == null)
                return false;

            // Normal dynamic-managed case.
            if (parentRuntime.RuntimeZone == this)
                return true;

            // Early lifecycle case: dynamic runtime is auto-creating this zone and RuntimeZone
            // has not been assigned yet when this zone's Awake/Initialize runs.
            return transform.parent == parentRuntime.transform &&
                   gameObject.name == "DynamicWallPlacementZone";
        }
        
        private void UpdateSpotWorldPositions()
        {
            foreach (var spot in _spots)
            {
                spot.worldPosition = transform.TransformPoint(spot.localPosition);

                // For static zones (no dynamic runtime), default the wall normal/tangent
                // from the zone transform if they haven't been set by a dynamic source.
                if (spot.wallNormal.sqrMagnitude < 0.001f)
                {
                    spot.wallNormal = transform.forward;
                    spot.wallTangent = transform.right;
                }
            }
        }
        
        #endregion
        
        #region Agent Assignment
        
        /// <summary>
        /// Assigns agents to available spots in the zone.
        /// Called when agents are commanded to this zone via RTS.
        /// </summary>
        /// <param name="agentIndices">Indices of agents to assign.</param>
        /// <returns>Number of agents successfully assigned.</returns>
        public int AssignAgentsToSpots(List<int> agentIndices)
        {
            return AssignAgentsToSpots(agentIndices, Vector3.zero, false);
        }

        /// <summary>
        /// Assigns agents to available spots nearest to a target position (e.g. cursor).
        /// Each agent is assigned to the closest still-available spot relative to
        /// <paramref name="targetPosition"/> rather than to the agent's own position.
        /// </summary>
        /// <param name="agentIndices">Indices of agents to assign.</param>
        /// <param name="targetPosition">World position used as the distance reference
        /// (typically the cursor hit point).</param>
        /// <returns>Number of agents successfully assigned.</returns>
        public int AssignAgentsToSpotsNearPosition(List<int> agentIndices, Vector3 targetPosition)
        {
            return AssignAgentsToSpots(agentIndices, targetPosition, true);
        }

        private int AssignAgentsToSpots(List<int> agentIndices, Vector3 targetPosition, bool useTargetPosition)
        {
            if (enableDebugLogging)
                Debug.Log($"[WallPlacementZone] AssignAgentsToSpots called with {agentIndices?.Count ?? 0} agents, available spots: {AvailableSpots}");
            
            if (crowdController == null || agentIndices == null || agentIndices.Count == 0)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[WallPlacementZone] AssignAgentsToSpots early exit: controller={crowdController != null}, indices={agentIndices?.Count ?? 0}");
                return 0;
            }
            
            var agents = crowdController.GetAgents();
            int assigned = 0;
            int skippedDead = 0;
            int skippedNotAllowed = 0;
            int skippedNoSpot = 0;
            
            foreach (int agentIndex in agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;
                
                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                {
                    skippedDead++;
                    continue;
                }
                
                // Check if agent is allowed
                if (!IsAgentAllowed(agent))
                {
                    skippedNotAllowed++;
                    continue;
                }
                
                // Find nearest available spot
                Vector3 referencePos = useTargetPosition ? targetPosition : agent.WorldPosition;
                PlacementSpot spot = FindNearestAvailableSpot(referencePos);
                if (spot == null)
                {
                    skippedNoSpot++;
                    break; // No more available spots
                }
                
                // Assign agent to spot
                if (AssignAgentToSpot(agentIndex, spot))
                {
                    assigned++;
                }
            }
            
            if (enableDebugLogging)
                Debug.Log($"[WallPlacementZone] AssignAgentsToSpots result: assigned={assigned}, skippedDead={skippedDead}, skippedNotAllowed={skippedNotAllowed}, skippedNoSpot={skippedNoSpot}");
            
            return assigned;
        }
        
        /// <summary>
        /// Assigns a single agent to a specific spot.
        /// </summary>
        public bool AssignAgentToSpot(int agentIndex, PlacementSpot spot)
        {
            if (crowdController == null || spot == null || spot.isOccupied)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[WallPlacementZone] AssignAgentToSpot early exit: controller={crowdController != null}, spot={spot != null}, occupied={spot?.isOccupied}");
                return false;
            }
            
            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count)
                return false;
            
            var agent = agents[agentIndex];
            if (agent == null || agent.isDead || agent.navigationAgent == null)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[WallPlacementZone] Agent {agentIndex} invalid: null={agent == null}, dead={agent?.isDead}, navAgent={agent?.navigationAgent != null}");
                return false;
            }
            
            // If agent was assigned to another spot, clear that first
            if (_agentToSpotAssignment.TryGetValue(agentIndex, out int oldSpotIndex))
            {
                if (oldSpotIndex >= 0 && oldSpotIndex < _spots.Count)
                {
                    _spots[oldSpotIndex].assignedAgentIndex = -1;
                }
            }
            
            // Assign to new spot
            spot.assignedAgentIndex = agentIndex;
            _agentToSpotAssignment[agentIndex] = spot.index;
            
            // Command agent to move to spot position
            MoveAgentToSpot(agent, spot);
            
            // Invoke event
            OnAgentAssigned?.Invoke(this, agentIndex, spot.index);
            
            // Check if zone is now full
            if (AvailableSpots == 0)
            {
                OnZoneFull?.Invoke(this);
            }
            
            return true;
        }

        /// <summary>
        /// Re-assigns an agent to a spot without issuing a movement command.
        /// Used when dynamic slot positions are rebuilt while agents are already in-place.
        /// </summary>
        public bool ReassignAgentToSpotSilent(int agentIndex, PlacementSpot spot)
        {
            if (crowdController == null || spot == null || spot.isOccupied)
                return false;

            var agents = crowdController.GetAgents();
            if (agentIndex < 0 || agentIndex >= agents.Count)
                return false;

            var agent = agents[agentIndex];
            if (agent == null || agent.isDead)
                return false;

            // Clear previous spot if any
            if (_agentToSpotAssignment.TryGetValue(agentIndex, out int oldSpotIndex))
            {
                if (oldSpotIndex >= 0 && oldSpotIndex < _spots.Count)
                {
                    _spots[oldSpotIndex].assignedAgentIndex = -1;
                }
            }

            spot.assignedAgentIndex = agentIndex;
            _agentToSpotAssignment[agentIndex] = spot.index;
            return true;
        }
        
        /// <summary>
        /// Commands an agent to move to their assigned spot.
        /// </summary>
        private void MoveAgentToSpot(CrowdAgent agent, PlacementSpot spot)
        {
            var navAgent = agent.navigationAgent;
            
            if (navAgent == null)
            {
                if (enableDebugLogging)
                    Debug.LogError($"[WallPlacementZone] Agent {agent.index} has no navigation agent!");
                return;
            }
            
            if (navAgent.Capabilities.RequiresSurfaceValidationForControl && !navAgent.IsOnNavMesh)
            {
                if (enableDebugLogging)
                    Debug.LogError($"[WallPlacementZone] Agent {agent.index} navigation is not on a valid surface! enabled={navAgent.Enabled}");
                return;
            }
            
            // Sample destination through active navigation backend when supported.
            Vector3 targetPosition = spot.worldPosition;
            bool foundSampledPos = navAgent.SamplePosition(targetPosition, out Vector3 sampledPos, 2f);
            if (foundSampledPos)
            {
                targetPosition = sampledPos;
            }
            else if (enableDebugLogging)
            {
                Debug.LogWarning($"[WallPlacementZone] Could not sample navigation position near spot {spot.index} at {spot.worldPosition}");
            }
            
            Vector3 agentPos = agent.WorldPosition;
            float distToSpot = Vector3.Distance(agentPos, targetPosition);
            
            if (enableDebugLogging)
                Debug.Log($"[WallPlacementZone] MoveAgentToSpot: agent={agent.index}, spot={spot.index}, mode={placementMode}, distToSpot={distToSpot:F2}, maxInstantDist={instantModeMaxDistance}, agentPos={agentPos}, targetPos={targetPosition}");
            
            // Handle placement mode
            if (placementMode == PlacementMode.Instant)
            {
                // Check if agent is within max distance for instant placement (distance to SPOT, not zone center)
                if (distToSpot <= instantModeMaxDistance)
                {
                    if (enableDebugLogging)
                        Debug.Log($"[WallPlacementZone] INSTANT WARP: Agent {agent.index} warping to spot {spot.index}");
                    
                    // Warp agent directly to the spot
                    WarpAgentToSpot(agent, spot, targetPosition);
                    return;
                }
                
                // Agent is too far - pathfind until close, then warp
                if (enableDebugLogging)
                    Debug.Log($"[WallPlacementZone] Agent {agent.index} too far for instant ({distToSpot:F2} > {instantModeMaxDistance}), pathfinding until in range then will warp");
                
                // Track this agent for pending instant warp
                _agentsPendingInstantWarp.Add(agent.index);
            }
            
            // Pathfind mode OR Instant mode with agent too far - use pathfinding
            if (enableDebugLogging)
                Debug.Log($"[WallPlacementZone] PATHFIND: Agent {agent.index} pathfinding to spot {spot.index}");
            
            // Stop any current movement first
            navAgent.ResetPath();
            
            // Ensure navigation is ready
            if (navAgent.IsStopped)
            {
                navAgent.IsStopped = false;
            }
            
            agent.manualCommandDestination = targetPosition;
            agent.hasManualCommand = true;
            agent.hasRTSFormationPosition = false;
            agent.brokeFromRTSFormation = false;
            
            // Set navigation state to ManualCommand
            if (agent.navigationState != AgentNavigationState.ManualCommand)
            {
                agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                agent.hasManualCommand = true; // Re-set after ForceSetNavigationState clears it
                agent.manualCommandDestination = targetPosition; // Re-set destination too
            }
            
            // Set the destination and check result
            bool setDestResult = navAgent.SetDestination(targetPosition);
            
            if (enableDebugLogging)
            {
                Debug.Log($"[WallPlacementZone] SetDestination result for agent {agent.index}: {setDestResult}, " +
                          $"pathPending={navAgent.PathPending}, hasPath={navAgent.HasPath}, " +
                          $"navState={agent.navigationState}, hasManualCmd={agent.hasManualCommand}");
            }
            
            if (!setDestResult)
            {
                Debug.LogError($"[WallPlacementZone] SetDestination FAILED for agent {agent.index}! " +
                              $"isOnSurface={navAgent.IsOnNavMesh}, enabled={navAgent.Enabled}");
            }
        }
        
        /// <summary>
        /// Warps an agent directly to their spot position.
        /// </summary>
        private void WarpAgentToSpot(CrowdAgent agent, PlacementSpot spot, Vector3 targetPosition)
        {
            var navAgent = agent.navigationAgent;
            if (navAgent == null) return;
            
            // Warp agent directly to the spot
            navAgent.Warp(targetPosition);
            navAgent.ResetPath();
            
            // Mark as arrived
            agent.hasManualCommand = false;
            agent.hasRTSFormationPosition = true;
            agent.manualCommandDestination = targetPosition;
            
            // Remove from pending warp set if present
            _agentsPendingInstantWarp.Remove(agent.index);
            
            // Apply facing immediately
            ApplySpotFacing(agent, spot);
            
            // Apply combat stance if enabled
            ApplyArrivalStance(agent);
        }
        
        /// <summary>
        /// Checks agents that are pathfinding toward their spot and warps them when close enough.
        /// Only used in Instant placement mode.
        /// </summary>
        private void CheckPendingInstantWarps()
        {
            if (_agentsPendingInstantWarp.Count == 0) return;
            
            var agents = crowdController.GetAgents();
            var toRemove = new List<int>();
            var toWarp = new List<(CrowdAgent agent, PlacementSpot spot, Vector3 targetPos)>();
            
            foreach (int agentIndex in _agentsPendingInstantWarp)
            {
                // Check if agent still has a spot assignment
                if (!_agentToSpotAssignment.TryGetValue(agentIndex, out int spotIndex))
                {
                    toRemove.Add(agentIndex);
                    continue;
                }
                
                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    toRemove.Add(agentIndex);
                    continue;
                }
                
                var agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                {
                    toRemove.Add(agentIndex);
                    continue;
                }
                
                var spot = GetSpot(spotIndex);
                if (spot == null)
                {
                    toRemove.Add(agentIndex);
                    continue;
                }
                
                // Check distance to spot
                Vector3 agentPos = agent.WorldPosition;
                float distToSpot = Vector3.Distance(agentPos, spot.worldPosition);
                
                if (distToSpot <= instantModeMaxDistance)
                {
                    // Agent is now close enough - queue for warp
                    if (enableDebugLogging)
                        Debug.Log($"[WallPlacementZone] Agent {agentIndex} now in range ({distToSpot:F2} <= {instantModeMaxDistance}), WARPING to spot {spotIndex}");
                    
                    // Sample target position through active navigation backend.
                    Vector3 targetPosition = spot.worldPosition;
                    if (agent.navigationAgent.SamplePosition(targetPosition, out Vector3 sampledPos, 2f))
                    {
                        targetPosition = sampledPos;
                    }
                    
                    toWarp.Add((agent, spot, targetPosition));
                }
            }
            
            // Warp agents after iteration is complete
            foreach (var (agent, spot, targetPos) in toWarp)
            {
                WarpAgentToSpot(agent, spot, targetPos);
            }
            
            // Clean up invalid entries
            foreach (int agentIndex in toRemove)
            {
                _agentsPendingInstantWarp.Remove(agentIndex);
            }
        }
        
        /// <summary>
        /// Removes an agent from their assigned spot.
        /// </summary>
        public void RemoveAgentFromSpot(int agentIndex)
        {
            // Also remove from pending warp set and stance applied set
            _agentsPendingInstantWarp.Remove(agentIndex);
            _agentsStanceApplied.Remove(agentIndex);
            _arrivalDebugNextLogTime.Remove(agentIndex);

            if (_wallPlacementAnimationStates.ContainsKey(agentIndex))
            {
                CrowdAgent agent = null;
                if (crowdController != null)
                {
                    var agents = crowdController.GetAgents();
                    if (agentIndex >= 0 && agentIndex < agents.Count)
                    {
                        agent = agents[agentIndex];
                    }
                }

                if (agent != null)
                {
                    ClearWallPlacementAnimation(agent);
                }
                else
                {
                    _wallPlacementAnimationStates.Remove(agentIndex);
                }
            }
            
            // Apply departure effects if agent had arrived with effects
            if (_agentsArrivedWithEffects.Contains(agentIndex))
            {
                ApplyDepartureEffects(agentIndex);
                _agentsArrivedWithEffects.Remove(agentIndex);
            }
            
            if (!_agentToSpotAssignment.TryGetValue(agentIndex, out int spotIndex))
                return;
            
            if (spotIndex >= 0 && spotIndex < _spots.Count)
            {
                var spot = _spots[spotIndex];
                spot.assignedAgentIndex = -1;
                OnAgentLeft?.Invoke(this, agentIndex, spotIndex);
            }
            
            _agentToSpotAssignment.Remove(agentIndex);
        }
        
        /// <summary>
        /// Clears all agent assignments.
        /// </summary>
        public void ClearAllAssignments()
        {
            ClearWallPlacementAnimationsFromAssignedAgents();

            // Apply departure effects for all agents that had arrived with effects
            foreach (int agentIndex in _agentsArrivedWithEffects)
            {
                ApplyDepartureEffects(agentIndex);
            }
            _agentsArrivedWithEffects.Clear();
            
            foreach (var spot in _spots)
            {
                if (spot.isOccupied)
                {
                    int agentIndex = spot.assignedAgentIndex;
                    spot.assignedAgentIndex = -1;
                    OnAgentLeft?.Invoke(this, agentIndex, spot.index);
                }
            }
            _agentToSpotAssignment.Clear();
            _agentsPendingInstantWarp.Clear();
            _agentsStanceApplied.Clear();
            _arrivalDebugNextLogTime.Clear();
            
            // Clean up central VFX
            if (_centralVFXInstance != null)
            {
                Destroy(_centralVFXInstance);
                _centralVFXInstance = null;
            }
            
            // Clean up per-agent VFX instances
            foreach (var vfxInstance in _agentVFXInstances.Values)
            {
                if (vfxInstance != null)
                    Destroy(vfxInstance);
            }
            _agentVFXInstances.Clear();
        }
        
        #endregion
        
        #region Spot Finding
        
        /// <summary>
        /// Finds the nearest available spot to a position.
        /// </summary>
        public PlacementSpot FindNearestAvailableSpot(Vector3 position)
        {
            PlacementSpot nearest = null;
            float nearestDistSqr = float.MaxValue;
            
            foreach (var spot in _spots)
            {
                if (spot.isOccupied)
                    continue;
                
                float distSqr = (spot.worldPosition - position).sqrMagnitude;
                if (distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    nearest = spot;
                }
            }
            
            return nearest;
        }

        /// <summary>
        /// Finds the N nearest available spots to a position, ordered by distance.
        /// </summary>
        public List<PlacementSpot> FindNearestAvailableSpots(Vector3 position, int count)
        {
            var candidates = new List<(PlacementSpot spot, float distSqr)>();
            foreach (var spot in _spots)
            {
                if (spot.isOccupied)
                    continue;
                float distSqr = (spot.worldPosition - position).sqrMagnitude;
                candidates.Add((spot, distSqr));
            }

            candidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            int resultCount = Mathf.Min(count, candidates.Count);
            var result = new List<PlacementSpot>(resultCount);
            for (int i = 0; i < resultCount; i++)
                result.Add(candidates[i].spot);

            return result;
        }
        
        /// <summary>
        /// Finds the spot at a specific index.
        /// </summary>
        public PlacementSpot GetSpot(int index)
        {
            if (index >= 0 && index < _spots.Count)
                return _spots[index];
            return null;
        }
        
        /// <summary>
        /// Gets the spot assigned to an agent.
        /// </summary>
        public PlacementSpot GetAgentSpot(int agentIndex)
        {
            if (_agentToSpotAssignment.TryGetValue(agentIndex, out int spotIndex))
            {
                return GetSpot(spotIndex);
            }
            return null;
        }
        
        /// <summary>
        /// Checks if an agent is allowed in this zone.
        /// </summary>
        public bool IsAgentAllowed(CrowdAgent agent)
        {
            if (agent == null || agent.isDead)
                return false;
            
            // Check faction filter
            if (allowedEnemyCrowdIndices.Count > 0)
            {
                return allowedEnemyCrowdIndices.Contains(agent.enemyCrowdIndex);
            }
            
            return true;
        }
        
        #endregion
        
        #region Auto-Assign
        
        /// <summary>
        /// Automatically assigns nearby agents to available spots.
        /// </summary>
        private void AutoAssignNearbyAgents()
        {
            if (AvailableSpots == 0)
                return;
            
            var agents = crowdController.GetAgents();
            float radiusSqr = autoAssignRadius * autoAssignRadius;
            Vector3 zoneCenter = transform.position;
            
            foreach (var agent in agents)
            {
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;
                
                // Skip if already assigned to a spot
                if (_agentToSpotAssignment.ContainsKey(agent.index))
                    continue;
                
                // Check if allowed
                if (!IsAgentAllowed(agent))
                    continue;
                
                // Check distance to zone center
                Vector3 agentPos = agent.WorldPosition;
                float distSqr = (agentPos - zoneCenter).sqrMagnitude;
                
                if (distSqr <= radiusSqr)
                {
                    // Find nearest available spot
                    PlacementSpot spot = FindNearestAvailableSpot(agentPos);
                    if (spot != null)
                    {
                        AssignAgentToSpot(agent.index, spot);
                        
                        if (AvailableSpots == 0)
                            break; // Zone is full
                    }
                }
            }
        }
        
        #endregion
        
        #region Position Checking
        
        /// <summary>
        /// Checks if assigned agents are still at their spots or have left.
        /// </summary>
        private void CheckAgentPositions()
        {
            if (crowdController == null)
                return;
            
            var agents = crowdController.GetAgents();
            var toRemove = new List<int>();
            
            foreach (var kvp in _agentToSpotAssignment)
            {
                int agentIndex = kvp.Key;
                int spotIndex = kvp.Value;
                
                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    toRemove.Add(agentIndex);
                    continue;
                }
                
                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                {
                    toRemove.Add(agentIndex);
                    continue;
                }
                
                // Check if agent has moved far from their spot
                var spot = GetSpot(spotIndex);
                if (spot != null && agent.navigationAgent != null)
                {
                    Vector3 agentPos = agent.WorldPosition;
                    float distSqr = (agentPos - spot.worldPosition).sqrMagnitude;
                    
                    // If agent is more than 5 units away and no longer has manual command to this spot,
                    // consider them as having left
                    if (distSqr > 25f && !agent.hasManualCommand)
                    {
                        toRemove.Add(agentIndex);
                    }
                }
            }
            
            // Remove agents that have left
            foreach (int agentIndex in toRemove)
            {
                RemoveAgentFromSpot(agentIndex);
            }
            
            // Also check for agents that arrived and should face the right direction
            CheckAgentFacing();
        }
        
        /// <summary>
        /// Updates facing for agents that have arrived at their spots.
        /// </summary>
        private void CheckAgentFacing()
        {
            if (crowdController == null)
                return;
            
            var agents = crowdController.GetAgents();
            
            foreach (var kvp in _agentToSpotAssignment)
            {
                int agentIndex = kvp.Key;
                int spotIndex = kvp.Value;
                
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;
                
                var agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;
                
                var spot = GetSpot(spotIndex);
                if (spot == null)
                    continue;
                
                // Check if agent has arrived at spot
                var navAgent = agent.navigationAgent;
                if (navAgent == null)
                    continue;

                Vector3 agentPos = agent.WorldPosition;
                float distSqr = (agentPos - spot.worldPosition).sqrMagnitude;
                float velocitySqr = navAgent.Velocity.sqrMagnitude;
                float remainingDistance = navAgent.RemainingDistance;
                float stoppingDistance = Mathf.Max(0.05f, navAgent.StoppingDistance + 0.1f);
                bool navArrival =
                    !navAgent.PathPending &&
                    distSqr < 4f &&
                    (!navAgent.HasPath || remainingDistance <= stoppingDistance);
                bool closeAndStationary = distSqr < 1f && velocitySqr < 0.01f;

                if (navArrival || closeAndStationary)
                {
                    _arrivalDebugNextLogTime.Remove(agent.index);

                    if (enableDebugLogging)
                    {
                        Debug.Log($"[WallPlacementZone] Agent {agent.index} arrival confirmed at spot {spot.index}. " +
                                  $"dist={Mathf.Sqrt(distSqr):F2}, velSqr={velocitySqr:F4}, remDist={remainingDistance:F2}, " +
                                  $"stopDist={stoppingDistance:F2}, navArrival={navArrival}, closeStationary={closeAndStationary}");
                    }

                    // Agent has arrived - apply facing
                    ApplySpotFacing(agent, spot);
                    
                    // Apply combat stance if enabled
                    ApplyArrivalStance(agent);
                    
                    // Apply status effects and audio on arrival (only once per arrival)
                    ApplyArrivalEffects(agent, spot);
                    
                    // Clear manual command so agent stays put but can still fight
                    if (agent.hasManualCommand)
                    {
                        agent.hasManualCommand = false;
                        agent.hasRTSFormationPosition = true; // Mark as at RTS position
                    }
                }
                else if (enableDebugLogging && distSqr < 9f)
                {
                    if (!_arrivalDebugNextLogTime.TryGetValue(agent.index, out float nextLogTime) || Time.time >= nextLogTime)
                    {
                        _arrivalDebugNextLogTime[agent.index] = Time.time + 0.75f;
                        Debug.Log($"[WallPlacementZone] Agent {agent.index} not yet arrived at spot {spot.index}. " +
                                  $"dist={Mathf.Sqrt(distSqr):F2}, velSqr={velocitySqr:F4}, remDist={remainingDistance:F2}, " +
                                  $"stopDist={stoppingDistance:F2}, pathPending={navAgent.PathPending}, hasPath={navAgent.HasPath}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Applies the correct facing direction to an agent at a spot.
        /// Only applies when agent is not in combat (allows free rotation during combat).
        /// </summary>
        private void ApplySpotFacing(CrowdAgent agent, PlacementSpot spot)
        {
            if (agent.transform == null)
                return;
            
            // Skip facing enforcement if agent is in combat (has target or in combat state)
            // This allows agents to freely rotate to face their enemies during combat
            if (agent.attackTargetTransform != null || 
                agent.combatState == CrowdCombatState.Attacking ||
                agent.combatState == CrowdCombatState.Approach ||
                agent.combatState == CrowdCombatState.Blocking ||
                agent.combatState == CrowdCombatState.Charging)
            {
                return;
            }

            Vector3 facingDir = Vector3.zero;

            if (spotFacingMode != SpotFacingMode.None && spot.wallNormal.sqrMagnitude > 0.001f)
            {
                switch (spotFacingMode)
                {
                    case SpotFacingMode.FacingAway:
                        facingDir = spot.wallNormal;
                        break;
                    case SpotFacingMode.FacingWall:
                        facingDir = -spot.wallNormal;
                        break;
                    case SpotFacingMode.AlongWallForward:
                        facingDir = spot.wallTangent.sqrMagnitude > 0.001f ? spot.wallTangent : Vector3.Cross(Vector3.up, spot.wallNormal);
                        break;
                    case SpotFacingMode.AlongWallBackward:
                        facingDir = spot.wallTangent.sqrMagnitude > 0.001f ? -spot.wallTangent : -Vector3.Cross(Vector3.up, spot.wallNormal);
                        break;
                }
                facingDir.y = 0f;
            }
            else if (spotFacingMode == SpotFacingMode.None)
            {
                // Legacy facing modes (faceCenter / customFacingDirection)
                if (faceCenter)
                {
                    facingDir = transform.position - spot.worldPosition;
                    facingDir.y = 0f;
                }
                else if (customFacingDirection.sqrMagnitude > 0.001f)
                {
                    facingDir = transform.TransformDirection(customFacingDirection);
                    facingDir.y = 0f;
                }
                else
                {
                    facingDir = transform.forward;
                }
            }
            
            if (facingDir.sqrMagnitude > 0.001f)
            {
                agent.transform.rotation = Quaternion.LookRotation(facingDir.normalized);
            }
        }
        
        /// <summary>
        /// Applies the arrival combat stance to an agent if enabled.
        /// </summary>
        private void ApplyArrivalStance(CrowdAgent agent)
        {
            if (!setStanceOnArrival)
                return;
            
            // Check if we've already applied stance to this agent
            if (_agentsStanceApplied.Contains(agent.index))
                return;
            
            // Mark as applied so we don't keep setting it
            _agentsStanceApplied.Add(agent.index);
            
            // Apply stance via RTS Controller if available
            if (rtsController != null)
            {
                rtsController.SetAgentCombatStanceByIndex(agent.index, arrivalStance);
                
                if (enableDebugLogging)
                    Debug.Log($"[WallPlacementZone] Applied {arrivalStance} stance to agent {agent.index} on arrival");
            }
            else
            {
                // Direct stance application if no RTS controller
                agent.combatStance = arrivalStance;
                
                // For HoldPosition, also set the hold position location
                if (arrivalStance == CombatStance.HoldPosition)
                {
                    agent.holdPositionLocation = agent.WorldPosition;
                    agent.hasHoldPositionSet = true;
                }
                
                if (enableDebugLogging)
                    Debug.Log($"[WallPlacementZone] Applied {arrivalStance} stance to agent {agent.index} directly (no RTS controller)");
            }
        }
        
        /// <summary>
        /// Applies status effects, VFX, and audio when an agent arrives at their spot.
        /// </summary>
        private void ApplyArrivalEffects(CrowdAgent agent, PlacementSpot spot)
        {
            ApplyWallPlacementAnimation(agent);

            // Check if we've already applied effects to this agent
            if (_agentsArrivedWithEffects.Contains(agent.index))
                return;
            
            // Mark as arrived with effects
            _agentsArrivedWithEffects.Add(agent.index);
            
            // Apply status effect
            if (applyStatusEffectOnArrival && _statusEffectSystem != null)
            {
                // Apply the status effect with a very long duration (will be removed on departure)
                float effectDuration = 3600f; // 1 hour - essentially permanent while at spot
                _statusEffectSystem.ApplyEffect(agent, arrivalStatusEffectType, effectDuration, statusEffectValue);
                
                if (enableDebugLogging)
                    Debug.Log($"[WallPlacementZone] Applied {arrivalStatusEffectType} (value={statusEffectValue}) to agent {agent.index}");
            }
            
            // Spawn VFX
            if (statusEffectVFXType != VFXType.None)
            {
                SpawnArrivalVFX(agent, spot);
            }
            
            // Play arrival sound
            if (arrivalSound != null)
            {
                Vector3 soundPosition = statusEffectVFXLocation == VFXSpawnLocation.ZoneCenter 
                    ? transform.position 
                    : spot.worldPosition;
                PlayPooledSound(arrivalSound, soundPosition, arrivalSoundVolume, arrivalSoundPitchVariation, arrivalSoundSpatialBlend);
            }
        }
        
        /// <summary>
        /// Applies departure effects when an agent leaves their spot.
        /// </summary>
        private void ApplyDepartureEffects(int agentIndex)
        {
            if (crowdController == null)
                return;
            
            var agents = crowdController.GetAgents();
            CrowdAgent agent = null;
            Vector3 agentPos = transform.position; // fallback to zone center
            
            if (agentIndex >= 0 && agentIndex < agents.Count)
            {
                agent = agents[agentIndex];
                if (agent?.navigationAgent != null)
                {
                    agentPos = agent.WorldPosition;
                }
            }

            if (agent != null)
            {
                ClearWallPlacementAnimation(agent);
            }
            else
            {
                _wallPlacementAnimationStates.Remove(agentIndex);
            }
            
            // Remove or let linger the status effect
            if (applyStatusEffectOnArrival && agent != null && _statusEffectSystem != null)
            {
                if (statusEffectLingerDuration <= 0f)
                {
                    // Remove immediately
                    int slot = agent.FindEffectOfType(arrivalStatusEffectType);
                    if (slot >= 0)
                    {
                        agent.activeStatusEffects[slot].Clear();
                        agent.activeEffectCount = Mathf.Max(0, agent.activeEffectCount - 1);
                        agent.statusEffectsDirty = true;
                    }
                }
                else
                {
                    // Set to expire after linger duration
                    int slot = agent.FindEffectOfType(arrivalStatusEffectType);
                    if (slot >= 0)
                    {
                        agent.activeStatusEffects[slot].expireTime = Time.time + statusEffectLingerDuration;
                    }
                }
                
                if (enableDebugLogging)
                {
                    string action = statusEffectLingerDuration <= 0f ? "Removed" : $"Set to expire in {statusEffectLingerDuration}s";
                    Debug.Log($"[WallPlacementZone] {action} {arrivalStatusEffectType} from agent {agentIndex}");
                }
            }
            
            // Clean up per-agent VFX
            if (_agentVFXInstances.TryGetValue(agentIndex, out GameObject vfxInstance))
            {
                if (vfxInstance != null)
                {
                    Destroy(vfxInstance);
                }
                _agentVFXInstances.Remove(agentIndex);
            }
            
            // Play departure sound
            if (departureSound != null)
            {
                PlayPooledSound(departureSound, agentPos, departureSoundVolume, departureSoundPitchVariation, departureSoundSpatialBlend);
            }
        }

        private void ApplyWallPlacementAnimation(CrowdAgent agent)
        {
            if (agent == null || agent.isDead)
                return;

            EnemyCrowd crowd = agent.enemyCrowd;
            if (crowd == null)
                return;

            AnimationClip wallEnterClip = crowd.wallPlacementEnterClipOverride;
            AnimationClip wallLoopClip = crowd.wallPlacementLoopClipOverride;
            string wallParameter = crowd.wallPlacementBoolParameterOverride;

            if (wallEnterClip == null && wallLoopClip == null && string.IsNullOrWhiteSpace(wallParameter))
                return;

            bool prefersMecanim = IsMecanimPreferredForWallPlacement(agent, crowd);
            const float wallLoopReinforceInterval = 0.35f;

            if (_wallPlacementAnimationStates.TryGetValue(agent.index, out WallPlacementAnimationState existingState))
            {
                // Re-apply parameter values on arrival checks to keep controllers synchronized.
                if (!string.IsNullOrEmpty(existingState.parameterName) && agent.mecanimAnimator != null)
                {
                    SetAnimatorParameterValue(agent.mecanimAnimator, existingState.parameterName, existingState.parameterType, true);
                }

                // If enter clip was played previously, transition into loop once enter duration elapsed.
                if (existingState.hasPendingLoopClip &&
                    existingState.pendingLoopClip != null &&
                    Time.time >= existingState.pendingLoopStartTime)
                {
                    if (TryPlayWallPlacementClip(agent, existingState.pendingLoopClip, prefersMecanim, true))
                    {
                        existingState.hasPendingLoopClip = false;
                        existingState.pendingLoopStartTime = 0f;
                        existingState.pendingLoopClip = null;
                        existingState.usedClip = true;
                        _wallPlacementAnimationStates[agent.index] = existingState;
                    }
                }

                // Re-assert loop clips periodically so generic idle updates cannot permanently override wall pose.
                if (existingState.appliedWorkflow == agent.currentWorkflow)
                {
                    bool clipDriven = string.IsNullOrEmpty(existingState.parameterName);
                    if (clipDriven &&
                        !existingState.hasPendingLoopClip &&
                        wallLoopClip != null &&
                        Time.time >= existingState.nextLoopReinforceTime)
                    {
                        if (TryPlayWallPlacementClip(agent, wallLoopClip, prefersMecanim, true))
                        {
                            existingState.usedClip = true;
                            existingState.nextLoopReinforceTime = Time.time + wallLoopReinforceInterval;
                            _wallPlacementAnimationStates[agent.index] = existingState;

                            if (enableDebugLogging)
                            {
                                Debug.Log($"[WallPlacementZone] Reapplied wall loop clip '{wallLoopClip.name}' for agent {agent.index}.");
                            }
                        }
                    }

                    return;
                }

                // Workflow changed while agent is still assigned; clear and re-apply for the new workflow.
                ClearWallPlacementAnimation(agent);
            }

            bool applied = false;
            var animationState = new WallPlacementAnimationState
            {
                appliedWorkflow = agent.currentWorkflow,
                parameterName = string.Empty,
                parameterType = WallPlacementAnimatorParameterType.None,
                usedClip = false,
                hasPendingLoopClip = false,
                pendingLoopStartTime = 0f,
                pendingLoopClip = null,
                nextLoopReinforceTime = 0f
            };

            Animator animator = agent.mecanimAnimator;

            // Mecanim workflows: prefer parameter control to support controller-driven loop states (e.g., TakeCover bool).
            if (prefersMecanim && animator != null && !string.IsNullOrWhiteSpace(wallParameter))
            {
                if (TryResolveAnimatorParameterType(animator, wallParameter, out WallPlacementAnimatorParameterType parameterType))
                {
                    SetAnimatorParameterValue(animator, wallParameter, parameterType, true);
                    animationState.parameterName = wallParameter;
                    animationState.parameterType = parameterType;
                    applied = true;
                }
                else if (enableDebugLogging)
                {
                    Debug.LogWarning($"[WallPlacementZone] Animator parameter '{wallParameter}' not found on agent {agent.index}.");
                }
            }

            // Clip-driven path: optional enter clip, then optional looping clip.
            if (!applied)
            {
                bool enterPlayed = false;

                if (wallEnterClip != null)
                {
                    enterPlayed = TryPlayWallPlacementClip(agent, wallEnterClip, prefersMecanim, false);
                    if (enterPlayed)
                    {
                        animationState.usedClip = true;
                        applied = true;
                    }
                }

                if (wallLoopClip != null)
                {
                    if (enterPlayed && wallEnterClip.length > 0.01f)
                    {
                        // Schedule loop handoff slightly before enter fully ends to reduce visible snap.
                        animationState.hasPendingLoopClip = true;
                        animationState.pendingLoopClip = wallLoopClip;
                        animationState.pendingLoopStartTime = Time.time + Mathf.Max(0.01f, wallEnterClip.length - 0.03f);
                        applied = true;
                    }
                    else if (TryPlayWallPlacementClip(agent, wallLoopClip, prefersMecanim, true))
                    {
                        animationState.usedClip = true;
                        animationState.nextLoopReinforceTime = Time.time + wallLoopReinforceInterval;
                        applied = true;
                    }
                }
            }

            // Last fallback for unexpected workflow/controller combinations: try parameter on Mecanim if available.
            if (!applied && animator != null && !string.IsNullOrWhiteSpace(wallParameter))
            {
                if (TryResolveAnimatorParameterType(animator, wallParameter, out WallPlacementAnimatorParameterType parameterType))
                {
                    SetAnimatorParameterValue(animator, wallParameter, parameterType, true);
                    animationState.parameterName = wallParameter;
                    animationState.parameterType = parameterType;
                    applied = true;
                }
            }

            if (!applied)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[WallPlacementZone] Failed to apply wall placement animation to agent {agent.index}. " +
                              $"prefersMecanim={prefersMecanim}, hasEnter={(wallEnterClip != null)}, hasLoop={(wallLoopClip != null)}, " +
                              $"hasParameter={!string.IsNullOrWhiteSpace(wallParameter)}");
                }
                return;
            }

            agent.cachedBlendWeight = -1f;
            agent.currentState = CrowdAnimationState.Idle;
            _wallPlacementAnimationStates[agent.index] = animationState;

            if (enableDebugLogging)
            {
                Debug.Log($"[WallPlacementZone] Applied wall placement animation to agent {agent.index}. " +
                          $"parameter='{animationState.parameterName}', usedClip={animationState.usedClip}, " +
                          $"pendingLoop={animationState.hasPendingLoopClip}, workflow={agent.currentWorkflow}");
            }
        }

        private static bool IsMecanimPreferredForWallPlacement(CrowdAgent agent, EnemyCrowd crowd)
        {
            if (agent == null)
                return false;

            bool prefersMecanim =
                agent.currentWorkflow == HybridWorkflowType.MecanimReader ||
                agent.currentWorkflow == HybridWorkflowType.BoneTracker;

            if (!prefersMecanim && crowd != null && crowd.animatorWorkflow != AnimatorWorkflowOverride.KeepPrefabSetting)
            {
                prefersMecanim =
                    crowd.animatorWorkflow == AnimatorWorkflowOverride.MecanimReader ||
                    crowd.animatorWorkflow == AnimatorWorkflowOverride.BoneTracker;
            }

            return prefersMecanim;
        }

        private bool TryPlayWallPlacementClip(
            CrowdAgent agent,
            AnimationClip clip,
            bool prefersMecanim,
            bool loop)
        {
            if (agent == null || clip == null)
                return false;

            if (prefersMecanim)
            {
                Animator animator = agent.mecanimAnimator;
                if (animator == null)
                    return false;

                int stateHash = Animator.StringToHash(clip.name);
                if (stateHash != 0 && animator.HasState(0, stateHash))
                {
                    animator.CrossFade(stateHash, 0.12f, 0, 0f);
                    return true;
                }

                if (enableDebugLogging)
                {
                    Debug.LogWarning($"[WallPlacementZone] Mecanim state '{clip.name}' not found for agent {agent.index}.");
                }

                return false;
            }

            if (agent.crowdInstance == null || agent.crowdInstance.AnimatorWorkflow == null)
                return false;

            try
            {
                agent.crowdInstance.StartAnimation(clip, -1f, 1f, 0.12f, null, loop);
                return true;
            }
            catch (System.Exception e)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"[WallPlacementZone] Failed to start wall placement clip '{clip.name}' for agent {agent.index}: {e.Message}");
                }

                return false;
            }
        }

        private void ClearWallPlacementAnimation(CrowdAgent agent)
        {
            if (agent == null)
                return;

            if (!_wallPlacementAnimationStates.TryGetValue(agent.index, out WallPlacementAnimationState state))
                return;

            EnemyCrowd crowd = agent.enemyCrowd;
            bool prefersMecanim = IsMecanimPreferredForWallPlacement(agent, crowd);

            Animator animator = agent.mecanimAnimator;
            if (animator != null && !string.IsNullOrEmpty(state.parameterName))
            {
                SetAnimatorParameterValue(animator, state.parameterName, state.parameterType, false);
            }

            bool controlledByParameter = !string.IsNullOrEmpty(state.parameterName);
            AnimationClip wallExitClip = crowd != null ? crowd.wallPlacementExitClipOverride : null;
            if (!controlledByParameter && wallExitClip != null)
            {
                TryPlayWallPlacementClip(agent, wallExitClip, prefersMecanim, false);
            }

            // Force animation controller to refresh locomotion/idle control after wall-slot override ends.
            agent.cachedBlendWeight = -1f;
            agent.idleAnimationWasGpu = false;
            agent.lastIdleAnimationHash = -1;

            _wallPlacementAnimationStates.Remove(agent.index);
        }

        private void UpdateWallPlacementAnimationTransitions()
        {
            if (_wallPlacementAnimationStates.Count == 0 || crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            var agentIndices = new List<int>(_wallPlacementAnimationStates.Keys);

            foreach (int agentIndex in agentIndices)
            {
                if (!_wallPlacementAnimationStates.TryGetValue(agentIndex, out WallPlacementAnimationState state))
                    continue;

                if (!state.hasPendingLoopClip || state.pendingLoopClip == null || Time.time < state.pendingLoopStartTime)
                    continue;

                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    _wallPlacementAnimationStates.Remove(agentIndex);
                    continue;
                }

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                {
                    _wallPlacementAnimationStates.Remove(agentIndex);
                    continue;
                }

                if (!_agentToSpotAssignment.ContainsKey(agentIndex))
                {
                    _wallPlacementAnimationStates.Remove(agentIndex);
                    continue;
                }

                bool prefersMecanim = IsMecanimPreferredForWallPlacement(agent, agent.enemyCrowd);
                if (!TryPlayWallPlacementClip(agent, state.pendingLoopClip, prefersMecanim, true))
                    continue;

                state.hasPendingLoopClip = false;
                state.pendingLoopStartTime = 0f;
                state.pendingLoopClip = null;
                state.usedClip = true;
                state.nextLoopReinforceTime = Time.time + 0.35f;
                _wallPlacementAnimationStates[agentIndex] = state;

                if (enableDebugLogging)
                {
                    Debug.Log($"[WallPlacementZone] Promoted pending wall loop clip for agent {agent.index}.");
                }
            }
        }

        private void ClearWallPlacementAnimationsFromAssignedAgents()
        {
            if (_wallPlacementAnimationStates.Count == 0)
                return;

            if (crowdController == null)
            {
                _wallPlacementAnimationStates.Clear();
                return;
            }

            var agents = crowdController.GetAgents();
            var agentIndices = new List<int>(_wallPlacementAnimationStates.Keys);

            foreach (int agentIndex in agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                {
                    _wallPlacementAnimationStates.Remove(agentIndex);
                    continue;
                }

                CrowdAgent agent = agents[agentIndex];
                if (agent == null)
                {
                    _wallPlacementAnimationStates.Remove(agentIndex);
                    continue;
                }

                ClearWallPlacementAnimation(agent);
            }
        }

        private static bool TryResolveAnimatorParameterType(
            Animator animator,
            string parameterName,
            out WallPlacementAnimatorParameterType parameterType)
        {
            parameterType = WallPlacementAnimatorParameterType.None;

            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
                return false;

            var parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.name != parameterName)
                    continue;

                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        parameterType = WallPlacementAnimatorParameterType.Bool;
                        return true;
                    case AnimatorControllerParameterType.Int:
                        parameterType = WallPlacementAnimatorParameterType.Int;
                        return true;
                    case AnimatorControllerParameterType.Float:
                        parameterType = WallPlacementAnimatorParameterType.Float;
                        return true;
                    case AnimatorControllerParameterType.Trigger:
                        parameterType = WallPlacementAnimatorParameterType.Trigger;
                        return true;
                }
            }

            return false;
        }

        private static void SetAnimatorParameterValue(
            Animator animator,
            string parameterName,
            WallPlacementAnimatorParameterType parameterType,
            bool active)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
                return;

            switch (parameterType)
            {
                case WallPlacementAnimatorParameterType.Bool:
                    animator.SetBool(parameterName, active);
                    break;
                case WallPlacementAnimatorParameterType.Int:
                    animator.SetInteger(parameterName, active ? 1 : 0);
                    break;
                case WallPlacementAnimatorParameterType.Float:
                    animator.SetFloat(parameterName, active ? 1f : 0f);
                    break;
                case WallPlacementAnimatorParameterType.Trigger:
                    if (active)
                        animator.SetTrigger(parameterName);
                    else
                        animator.ResetTrigger(parameterName);
                    break;
            }
        }
        
        /// <summary>
        /// Spawns VFX for an arriving agent.
        /// </summary>
        private void SpawnArrivalVFX(CrowdAgent agent, PlacementSpot spot)
        {
            if (statusEffectVFXLocation == VFXSpawnLocation.ZoneCenter)
            {
                // Spawn central VFX only once
                if (_centralVFXInstance == null)
                {
                    _centralVFXInstance = SpawnVFXInstance(transform.position, transform.rotation);
                }
            }
            else
            {
                // Spawn per-agent VFX
                if (!_agentVFXInstances.ContainsKey(agent.index) && agent.transform != null)
                {
                    var vfxInstance = SpawnVFXInstance(agent.transform.position, agent.transform.rotation, agent.transform);
                    if (vfxInstance != null)
                    {
                        _agentVFXInstances[agent.index] = vfxInstance;
                    }
                }
            }
        }
        
        /// <summary>
        /// Spawns a VFX instance based on the configured type.
        /// </summary>
        private GameObject SpawnVFXInstance(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            GameObject instance = null;
            
            if (statusEffectVFXType == VFXType.ParticleSystem && statusEffectParticleSystem != null)
            {
                instance = Instantiate(statusEffectParticleSystem, position, rotation, parent);
                var ps = instance.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ps.Play();
                }
            }
            else if (statusEffectVFXType == VFXType.VFXGraph && statusEffectVFXGraph != null)
            {
                instance = new GameObject("StatusEffect_VFX");
                instance.transform.SetPositionAndRotation(position, rotation);
                if (parent != null)
                    instance.transform.SetParent(parent);
                
                var vfx = instance.AddComponent<UnityEngine.VFX.VisualEffect>();
                vfx.visualEffectAsset = statusEffectVFXGraph;
                vfx.Play();
            }
            
            return instance;
        }
        
        #endregion
        
        #region Audio Pooling
        
        /// <summary>
        /// Initializes the audio source pool for efficient sound playback.
        /// </summary>
        private void InitializeAudioPool(int poolSize)
        {
            _audioSourcePool = new List<AudioSource>(poolSize);
            _freeAudioSources = new Queue<int>(poolSize);
            _audioSourceReleaseTime = new float[poolSize];
            
            // Create a container for audio sources
            GameObject audioContainer = new GameObject("WallPlacementZone_AudioPool");
            audioContainer.transform.SetParent(transform);
            _audioPoolContainer = audioContainer.transform;
            
            for (int i = 0; i < poolSize; i++)
            {
                GameObject audioObj = new GameObject($"AudioSource_{i}");
                audioObj.transform.SetParent(_audioPoolContainer);
                
                AudioSource source = audioObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f; // Default to 3D
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = 50f;
                
                _audioSourcePool.Add(source);
                _freeAudioSources.Enqueue(i);
                _audioSourceReleaseTime[i] = 0f;
            }
        }
        
        /// <summary>
        /// Plays a sound using the audio source pool.
        /// </summary>
        private void PlayPooledSound(AudioClip clip, Vector3 position, float volume, float pitchVariation, float spatialBlend)
        {
            if (clip == null || _audioSourcePool == null || _audioSourcePool.Count == 0)
                return;
            
            // Try to get a free audio source
            int audioIndex = GetFreeAudioSource();
            if (audioIndex < 0)
                return; // No free audio sources available
            
            AudioSource source = _audioSourcePool[audioIndex];
            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.spatialBlend = spatialBlend;
            source.Play();
            
            // Mark when this source will be free again
            _audioSourceReleaseTime[audioIndex] = Time.time + clip.length + 0.1f;
        }
        
        /// <summary>
        /// Gets a free audio source index from the pool.
        /// </summary>
        private int GetFreeAudioSource()
        {
            float currentTime = Time.time;
            
            // First try to get from queue
            while (_freeAudioSources.Count > 0)
            {
                int index = _freeAudioSources.Dequeue();
                if (_audioSourceReleaseTime[index] <= currentTime)
                {
                    return index;
                }
                // Put it back if not ready
                _freeAudioSources.Enqueue(index);
                break; // Avoid infinite loop
            }
            
            // Search for any finished audio source
            for (int i = 0; i < _audioSourcePool.Count; i++)
            {
                if (_audioSourceReleaseTime[i] <= currentTime && !_audioSourcePool[i].isPlaying)
                {
                    return i;
                }
            }
            
            // No free sources - find the one that will finish soonest (interrupt it)
            if (_audioSourcePool.Count > 0)
            {
                float earliestFinish = float.MaxValue;
                int earliestIndex = 0;
                
                for (int i = 0; i < _audioSourcePool.Count; i++)
                {
                    if (_audioSourceReleaseTime[i] < earliestFinish)
                    {
                        earliestFinish = _audioSourceReleaseTime[i];
                        earliestIndex = i;
                    }
                }
                
                _audioSourcePool[earliestIndex].Stop();
                return earliestIndex;
            }
            
            return -1;
        }
        
        #endregion
        
        #region Runtime Visualization
        
        /// <summary>
        /// Initializes the runtime visualization components (LineRenderer and spot markers).
        /// </summary>
        private void InitializeRuntimeVisualization()
        {
            // Create outline LineRenderer if enabled
            if (showRuntimeOutline)
            {
                CreateOutlineRenderer();
                UpdateOutlineRenderer();
            }
            
            // Create spot markers if enabled
            if (showRuntimeSpotMarkers)
            {
                CreateSpotMarkerMaterials();
                UpdateSpotMarkers();
            }
        }
        
        /// <summary>
        /// Cleans up runtime visualization components.
        /// </summary>
        private void CleanupRuntimeVisualization()
        {
            // Clean up outline renderer
            if (_outlineRenderer != null)
            {
                Destroy(_outlineRenderer.gameObject);
                _outlineRenderer = null;
            }
            
            // Clean up spot markers
            foreach (var marker in _spotMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
            _spotMarkers.Clear();
            
            if (_spotMarkersParent != null)
            {
                Destroy(_spotMarkersParent.gameObject);
                _spotMarkersParent = null;
            }
            
            // Clean up materials
            if (_availableMarkerMaterial != null)
            {
                Destroy(_availableMarkerMaterial);
                _availableMarkerMaterial = null;
            }
            if (_occupiedMarkerMaterial != null)
            {
                Destroy(_occupiedMarkerMaterial);
                _occupiedMarkerMaterial = null;
            }
        }
        
        /// <summary>
        /// Updates runtime visualization each frame.
        /// </summary>
        private void UpdateRuntimeVisualization()
        {
            // Update outline if enabled and renderer exists
            if (showRuntimeOutline && _outlineRenderer != null)
            {
                // Outline shape doesn't change at runtime unless zone moves
                // Just ensure it stays positioned correctly
                _outlineRenderer.transform.position = transform.position;
                _outlineRenderer.transform.rotation = transform.rotation;
            }
            else if (showRuntimeOutline && _outlineRenderer == null)
            {
                CreateOutlineRenderer();
                UpdateOutlineRenderer();
            }
            else if (!showRuntimeOutline && _outlineRenderer != null)
            {
                Destroy(_outlineRenderer.gameObject);
                _outlineRenderer = null;
            }
            
            // Update spot markers if enabled
            if (showRuntimeSpotMarkers)
            {
                UpdateSpotMarkers();
            }
            else if (_spotMarkers.Count > 0)
            {
                // Clean up markers if disabled at runtime
                foreach (var marker in _spotMarkers)
                {
                    if (marker != null)
                        Destroy(marker);
                }
                _spotMarkers.Clear();
            }
        }
        
        /// <summary>
        /// Creates the LineRenderer for the zone outline.
        /// </summary>
        private void CreateOutlineRenderer()
        {
            if (_outlineRenderer != null)
                return;
            
            var outlineGO = new GameObject("ZoneOutline");
            outlineGO.transform.SetParent(transform);
            outlineGO.transform.localPosition = Vector3.zero;
            outlineGO.transform.localRotation = Quaternion.identity;
            
            _outlineRenderer = outlineGO.AddComponent<LineRenderer>();
            _outlineRenderer.useWorldSpace = false;
            _outlineRenderer.loop = true;
            _outlineRenderer.startWidth = outlineWidth;
            _outlineRenderer.endWidth = outlineWidth;
            _outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outlineRenderer.receiveShadows = false;
            
            // Create material for the line
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (material.shader == null || !material.shader.isSupported)
                material = new Material(Shader.Find("Unlit/Color"));
            
            material.color = outlineColor;
            _outlineRenderer.material = material;
        }
        
        /// <summary>
        /// Updates the LineRenderer points based on arrangement type.
        /// </summary>
        private void UpdateOutlineRenderer()
        {
            if (_outlineRenderer == null || _spots.Count == 0)
                return;
            
            Vector3[] points;
            
            switch (arrangement)
            {
                case SpotArrangement.Line:
                    points = GetLineOutlinePoints();
                    break;
                    
                case SpotArrangement.Grid:
                    points = GetGridOutlinePoints();
                    break;
                    
                case SpotArrangement.Circle:
                    points = GetCircleOutlinePoints();
                    break;
                    
                case SpotArrangement.Manual:
                    points = GetConvexHullOutlinePoints();
                    break;
                    
                default:
                    points = new Vector3[0];
                    break;
            }
            
            _outlineRenderer.positionCount = points.Length;
            _outlineRenderer.SetPositions(points);
        }
        
        private Vector3[] GetLineOutlinePoints()
        {
            if (_spots.Count < 2)
                return new Vector3[0];
            
            // Create a rectangle around the line
            float padding = gizmoSize;
            Vector3 first = _spots[0].localPosition;
            Vector3 last = _spots[_spots.Count - 1].localPosition;
            
            Vector3 dir = (last - first).normalized;
            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized * padding;
            Vector3 extend = dir * padding;
            
            return new Vector3[]
            {
                first - extend - perp + Vector3.up * 0.1f,
                first - extend + perp + Vector3.up * 0.1f,
                last + extend + perp + Vector3.up * 0.1f,
                last + extend - perp + Vector3.up * 0.1f
            };
        }
        
        private Vector3[] GetGridOutlinePoints()
        {
            if (_spots.Count == 0)
                return new Vector3[0];
            
            // Find bounds of the grid
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            
            foreach (var spot in _spots)
            {
                minX = Mathf.Min(minX, spot.localPosition.x);
                maxX = Mathf.Max(maxX, spot.localPosition.x);
                minZ = Mathf.Min(minZ, spot.localPosition.z);
                maxZ = Mathf.Max(maxZ, spot.localPosition.z);
            }
            
            float padding = gizmoSize;
            float y = 0.1f;
            
            return new Vector3[]
            {
                new Vector3(minX - padding, y, minZ - padding),
                new Vector3(minX - padding, y, maxZ + padding),
                new Vector3(maxX + padding, y, maxZ + padding),
                new Vector3(maxX + padding, y, minZ - padding)
            };
        }
        
        private Vector3[] GetCircleOutlinePoints()
        {
            int segments = 32;
            var points = new Vector3[segments];
            float radius = circleRadius + gizmoSize;
            
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                points[i] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.1f,
                    Mathf.Sin(angle) * radius
                );
            }
            
            return points;
        }
        
        private Vector3[] GetConvexHullOutlinePoints()
        {
            if (_spots.Count < 3)
            {
                // Fallback to simple bounding box for less than 3 spots
                return GetGridOutlinePoints();
            }
            
            // Simple convex hull using gift wrapping algorithm
            var points2D = new List<Vector2>();
            foreach (var spot in _spots)
            {
                points2D.Add(new Vector2(spot.localPosition.x, spot.localPosition.z));
            }
            
            var hull = ComputeConvexHull(points2D);
            var result = new Vector3[hull.Count];
            
            float padding = gizmoSize;
            Vector2 center = Vector2.zero;
            foreach (var p in hull)
                center += p;
            center /= hull.Count;
            
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 dir = (hull[i] - center).normalized;
                Vector2 padded = hull[i] + dir * padding;
                result[i] = new Vector3(padded.x, 0.1f, padded.y);
            }
            
            return result;
        }
        
        private List<Vector2> ComputeConvexHull(List<Vector2> points)
        {
            if (points.Count < 3)
                return points;
            
            // Find leftmost point
            int leftmost = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].x < points[leftmost].x)
                    leftmost = i;
            }
            
            var hull = new List<Vector2>();
            int current = leftmost;
            
            do
            {
                hull.Add(points[current]);
                int next = 0;
                
                for (int i = 1; i < points.Count; i++)
                {
                    if (next == current)
                    {
                        next = i;
                        continue;
                    }
                    
                    float cross = Cross(points[current], points[next], points[i]);
                    if (cross < 0 || (cross == 0 && 
                        Vector2.Distance(points[current], points[i]) > Vector2.Distance(points[current], points[next])))
                    {
                        next = i;
                    }
                }
                
                current = next;
            }
            while (current != leftmost && hull.Count < points.Count);
            
            return hull;
        }
        
        private float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }
        
        /// <summary>
        /// Creates materials for spot markers.
        /// </summary>
        private void CreateSpotMarkerMaterials()
        {
            // Create available marker material
            _availableMarkerMaterial = CreateTransparentMaterial(runtimeAvailableColor);
            
            // Create occupied marker material
            _occupiedMarkerMaterial = CreateTransparentMaterial(runtimeOccupiedColor);
        }
        
        private Material CreateTransparentMaterial(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (material.shader == null || !material.shader.isSupported)
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
                material.color = color;
            }
            
            return material;
        }
        
        /// <summary>
        /// Updates spot marker positions and colors.
        /// </summary>
        private void UpdateSpotMarkers()
        {
            if (_spotMarkersParent == null)
            {
                var parentGO = new GameObject("SpotMarkers");
                parentGO.transform.SetParent(transform);
                parentGO.transform.localPosition = Vector3.zero;
                parentGO.transform.localRotation = Quaternion.identity;
                _spotMarkersParent = parentGO.transform;
            }
            
            if (_availableMarkerMaterial == null || _occupiedMarkerMaterial == null)
            {
                CreateSpotMarkerMaterials();
            }
            
            // Ensure we have enough markers
            while (_spotMarkers.Count < _spots.Count)
            {
                var marker = CreateSpotMarker();
                _spotMarkers.Add(marker);
            }
            
            // Hide excess markers
            for (int i = _spots.Count; i < _spotMarkers.Count; i++)
            {
                if (_spotMarkers[i] != null)
                    _spotMarkers[i].SetActive(false);
            }
            
            // Update marker positions and colors
            for (int i = 0; i < _spots.Count; i++)
            {
                var spot = _spots[i];
                var marker = _spotMarkers[i];
                
                if (marker == null)
                {
                    marker = CreateSpotMarker();
                    _spotMarkers[i] = marker;
                }
                
                marker.SetActive(true);
                marker.transform.position = spot.worldPosition + Vector3.up * (spotMarkerSize * 0.5f);
                
                // Update color based on occupancy
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = spot.isOccupied ? _occupiedMarkerMaterial : _availableMarkerMaterial;
                }
            }
        }
        
        /// <summary>
        /// Creates a single spot marker.
        /// </summary>
        private GameObject CreateSpotMarker()
        {
            GameObject marker;
            
            if (spotMarkerPrefab != null)
            {
                marker = Instantiate(spotMarkerPrefab, _spotMarkersParent);
            }
            else
            {
                // Create default sphere marker
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.SetParent(_spotMarkersParent);
                marker.transform.localScale = Vector3.one * spotMarkerSize;
                
                // Remove collider to prevent interference
                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                
                // Set initial material
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = _availableMarkerMaterial;
                }
            }
            
            marker.name = "SpotMarker";
            return marker;
        }
        
        /// <summary>
        /// Toggles runtime outline visibility.
        /// </summary>
        public void SetRuntimeOutlineVisible(bool visible)
        {
            showRuntimeOutline = visible;
            
            if (visible && _outlineRenderer == null && Application.isPlaying)
            {
                CreateOutlineRenderer();
                UpdateOutlineRenderer();
            }
            else if (!visible && _outlineRenderer != null)
            {
                Destroy(_outlineRenderer.gameObject);
                _outlineRenderer = null;
            }
        }
        
        /// <summary>
        /// Toggles runtime spot markers visibility.
        /// </summary>
        public void SetRuntimeSpotMarkersVisible(bool visible)
        {
            showRuntimeSpotMarkers = visible;
            
            if (!visible)
            {
                foreach (var marker in _spotMarkers)
                {
                    if (marker != null)
                        marker.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Configures this zone for runtime-generated manual spots.
        /// Keeps the existing class workflow and only switches arrangement to Manual.
        /// </summary>
        public void ConfigureAsRuntimeManualZone(bool clearAssignments = true)
        {
            arrangement = SpotArrangement.Manual;

            if (clearAssignments)
            {
                ClearAllAssignments();
            }

            GenerateSpots();
            _initialized = true;
        }

        /// <summary>
        /// Rebuilds spots from active child transforms using Manual arrangement.
        /// Intended for runtime systems that move/create spot child transforms dynamically.
        /// </summary>
        public void RebuildManualSpotsFromChildren(bool clearAssignments = true)
        {
            arrangement = SpotArrangement.Manual;

            if (clearAssignments)
            {
                ClearAllAssignments();
            }

            GenerateSpots();
        }

        /// <summary>
        /// Sets the wall normal and tangent for a specific spot.
        /// Called by EnemyMassesDynamicWallPlacementRuntime after generating dynamic slots.
        /// </summary>
        public void SetSpotWallDirections(int spotIndex, Vector3 wallNormal, Vector3 wallTangent)
        {
            if (spotIndex < 0 || spotIndex >= _spots.Count)
                return;

            var spot = _spots[spotIndex];
            spot.wallNormal = wallNormal;
            spot.wallTangent = wallTangent;
        }
        
        #endregion
        
        #region Public Utilities
        
        /// <summary>
        /// Gets the world position of the zone center.
        /// </summary>
        public Vector3 GetZoneCenter()
        {
            return transform.position;
        }
        
        /// <summary>
        /// Gets positions of all spots for external use (e.g., RTS formation).
        /// </summary>
        public List<Vector3> GetAllSpotPositions()
        {
            var positions = new List<Vector3>(_spots.Count);
            foreach (var spot in _spots)
            {
                positions.Add(spot.worldPosition);
            }
            return positions;
        }
        
        /// <summary>
        /// Gets positions of available spots only.
        /// </summary>
        public List<Vector3> GetAvailableSpotPositions()
        {
            var positions = new List<Vector3>();
            foreach (var spot in _spots)
            {
                if (!spot.isOccupied)
                {
                    positions.Add(spot.worldPosition);
                }
            }
            return positions;
        }
        
        /// <summary>
        /// Checks if a position is inside any spot's area.
        /// </summary>
        public bool IsPositionInZone(Vector3 position, float tolerance = 1f)
        {
            float toleranceSqr = tolerance * tolerance;
            foreach (var spot in _spots)
            {
                float distSqr = (spot.worldPosition - position).sqrMagnitude;
                if (distSqr <= toleranceSqr)
                    return true;
            }
            return false;
        }
        
        #endregion
        
        #region Gizmos
        
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;
            
            // Generate spots for preview if not initialized
            if (_spots == null || _spots.Count == 0)
            {
                GenerateSpots();
            }
            
            // Draw zone center
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
            
            // Draw auto-assign radius if enabled
            if (enableAutoAssign)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
                Gizmos.DrawWireSphere(transform.position, autoAssignRadius);
            }
            
            // Draw spots
            foreach (var spot in _spots)
            {
                Vector3 worldPos = Application.isPlaying ? spot.worldPosition : transform.TransformPoint(spot.localPosition);
                
                Gizmos.color = spot.isOccupied ? occupiedSpotColor : availableSpotColor;
                Gizmos.DrawSphere(worldPos, gizmoSize);
                
                // Draw facing direction
                Vector3 facingDir;
                if (faceCenter)
                {
                    facingDir = (transform.position - worldPos).normalized;
                }
                else if (customFacingDirection.sqrMagnitude > 0.001f)
                {
                    facingDir = transform.TransformDirection(customFacingDirection).normalized;
                }
                else
                {
                    facingDir = transform.forward;
                }
                facingDir.y = 0f;
                
                if (facingDir.sqrMagnitude > 0.001f)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawRay(worldPos, facingDir.normalized * gizmoSize * 2f);
                }
            }
            
            // Draw connections between spots
            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            for (int i = 0; i < _spots.Count - 1; i++)
            {
                Vector3 pos1 = Application.isPlaying ? _spots[i].worldPosition : transform.TransformPoint(_spots[i].localPosition);
                Vector3 pos2 = Application.isPlaying ? _spots[i + 1].worldPosition : transform.TransformPoint(_spots[i + 1].localPosition);
                Gizmos.DrawLine(pos1, pos2);
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            // Draw zone bounds when selected
            Gizmos.color = Color.cyan;
            
            if (arrangement == SpotArrangement.Line)
            {
                float totalWidth = (spotCount - 1) * spotSpacing;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(totalWidth + 2f, 0.1f, 2f));
                Gizmos.matrix = Matrix4x4.identity;
            }
            else if (arrangement == SpotArrangement.Grid)
            {
                float totalWidth = (spotCount - 1) * spotSpacing;
                float totalDepth = (rowCount - 1) * spotSpacing;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(totalWidth + 2f, 0.1f, totalDepth + 2f));
                Gizmos.matrix = Matrix4x4.identity;
            }
            else if (arrangement == SpotArrangement.Circle)
            {
                // Draw circle
                int segments = 32;
                for (int i = 0; i < segments; i++)
                {
                    float angle1 = (i / (float)segments) * Mathf.PI * 2f;
                    float angle2 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                    
                    Vector3 p1 = transform.position + new Vector3(Mathf.Sin(angle1), 0f, Mathf.Cos(angle1)) * circleRadius;
                    Vector3 p2 = transform.position + new Vector3(Mathf.Sin(angle2), 0f, Mathf.Cos(angle2)) * circleRadius;
                    
                    Gizmos.DrawLine(p1, p2);
                }
            }
        }
        
        #endregion
    }
}
