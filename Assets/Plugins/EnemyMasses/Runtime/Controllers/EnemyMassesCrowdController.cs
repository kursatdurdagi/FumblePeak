using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
using GPUInstancerPro.PrefabModule;
using Arawn.EnemyMasses.Runtime.Civil;
using Arawn.EnemyMasses.Runtime.Factions;
using Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using SkillVFXManager = Arawn.EnemyMasses.Runtime.Skills.SkillVFXManager;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Main controller that coordinates all Enemy Masses subsystems.
    /// Provides the public API for spawning, controlling, and managing crowd agents.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EnemyMassesCrowdController : MonoBehaviour
    {
        #region Singleton
        
        /// <summary>
        /// Static instance for easy access. Set automatically in Awake.
        /// Note: There should typically be only one EnemyMassesCrowdController per scene.
        /// </summary>
        public static EnemyMassesCrowdController Instance { get; private set; }
        
        #endregion
        
        #region Serialized Settings

        [Header("Core Settings")]
        [SerializeField] private List<EnemyCrowd> enemyCrowds = new();
        
        /// <summary>
        /// Gets the list of enemy crowd factions configured for this controller.
        /// </summary>
        public List<EnemyCrowd> EnemyCrowds => enemyCrowds;
        
        [SerializeField] private List<Transform> playerTargets = new();
        [SerializeField] private Transform playerTarget;

        [Tooltip("Optional RTS controller used to detect player-controlled factions when evaluating player auto-targeting in faction battles.")]
        [SerializeField] private EnemyMassesRTSController rtsController;
        
        // Disengage grace period - set by RTSController when issuing move commands
        // Prevents Offensive agents from re-acquiring targets for a short time after receiving move orders
        private float _disengageGracePeriod = 2f;
        
        /// <summary>
        /// Gets or sets the disengage grace period in seconds.
        /// After receiving a move command, Offensive agents won't acquire new targets for this duration.
        /// Allows agents to retreat from combat without immediately re-engaging.
        /// </summary>
        public float DisengageGracePeriod
        {
            get => _disengageGracePeriod;
            set => _disengageGracePeriod = Mathf.Max(0f, value);
        }
        
        /// <summary>
        /// Gets the player target transform used for click-to-move and spawn triggers.
        /// </summary>
        public Transform PlayerTarget => GetPrimaryPlayerTarget();

        /// <summary>
        /// Gets the list of player targets used for multi-player targeting.
        /// </summary>
        public IReadOnlyList<Transform> PlayerTargets => playerTargets;
        [SerializeField] private bool developerTestMode;

        [Header("Debug")]
        [Tooltip("Enable verbose runtime debug logs from EnemyMassesCrowdController.")]
        [SerializeField] private bool enableDebugLogs = false;
        
        [Header("Click-to-Move (Runtime)")]
        [Tooltip("Enable click-to-move in Game View during play mode")]
        [SerializeField] private bool enableClickToMove = false;
        
        [Tooltip("Which faction to move when clicking. -1 = All factions, 0+ = specific faction index")]
        [SerializeField] private int clickToMoveFactionIndex = -1;
        
        /// <summary>
        /// Gets or sets the faction index for click-to-move. -1 means all factions.
        /// </summary>
        public int ClickToMoveFactionIndex
        {
            get => clickToMoveFactionIndex;
            set => clickToMoveFactionIndex = value;
        }

        [Header("Faction Battle Mode (Debug)")]
        [Tooltip("Enable faction vs faction battle mode. Agents will fight each other instead of the player.")]
        [SerializeField] private bool factionBattleMode = false;
        
        /// <summary>
        /// Gets or sets whether faction battle mode is enabled.
        /// When enabled, agents attack other factions instead of the player.
        /// </summary>
        public bool FactionBattleMode
        {
            get => factionBattleMode;
            set
            {
                factionBattleMode = value;
                if (factionBattleMode && _initialized)
                {
                    // Clear all player targets and reassign faction targets
                    AssignFactionBattleTargets();
                }
            }
        }
        
        [Tooltip("Battle type: AllVsAll = every agent for themselves, FactionVsFaction = faction-based teams")]
        [SerializeField] private FactionBattleType factionBattleType = FactionBattleType.FactionVsFaction;
        
        /// <summary>
        /// Gets or sets the faction battle type.
        /// </summary>
        public FactionBattleType FactionBattleType
        {
            get => factionBattleType;
            set => factionBattleType = value;
        }
        
        [Tooltip("How often to reassign targets during faction battle (seconds). Lower = more dynamic battles.")]
        [SerializeField] [Range(0.5f, 10f)] private float factionBattleTargetUpdateInterval = 2f;
        
        [Tooltip("Max agents to process per frame for faction target assignment (0 = all at once). Higher values spread CPU load but delay target updates.")]
        [SerializeField] [Range(0, 100)] private int maxFactionTargetUpdatesPerFrame = 50;
        
        private float _nextFactionBattleTargetUpdate;
        private int _factionTargetUpdateCursor = 0; // Tracks which agent to process next
        private int _animationUpdateCursor = 0; // Rotates animation updates when throttling
        private int _animationVisibleUpdateCursor = 0; // Rotates visible animation updates
        private int _animationOffscreenUpdateCursor = 0; // Rotates offscreen animation updates

        #endregion
        
        #region Global Settings (Phase 3 Refined)
        
        // Phase 3 Refined: These settings are truly GLOBAL and affect all agents
        // Performance settings MUST be global as they control system-wide throttling
        [Header("═══ GLOBAL PERFORMANCE SETTINGS ═══")]
        [Tooltip("Controls system-wide performance optimization")]
        [SerializeField] private PerformanceSettings performanceSettings = new PerformanceSettings();
        
        /// <summary>
        /// Returns true if the spatial grid is enabled and available.
        /// </summary>
        public bool IsSpatialGridEnabled => performanceSettings != null && performanceSettings.enableSpatialGrid && _spatialGrid != null;

        [Header("═══ GLOBAL HIT DETECTION ═══")]
        [Tooltip("Distance = virtual hitboxes; Collider = Unity collider/trigger-based hits (auto-adds root collider if missing).")]
        [SerializeField] private HitDetectionMode hitDetectionMode = HitDetectionMode.Distance;
        
        [Header("═══ GLOBAL DAMAGE SETTINGS ═══")]
        [Tooltip("Global multiplier applied to all incoming damage. Use GC2 Instructions to modify at runtime for buffs/debuffs.")]
        [SerializeField] private float incomingDamageMultiplier = 1f;

        [Tooltip("Multiplier applied only to damage coming from the Player Target. Use GC2 Instructions to buff/nerf player damage against crowds without touching weapons.")]
        [SerializeField] private float playerDamageMultiplier = 1f;
        
        /// <summary>
        /// Global multiplier applied to all incoming damage to agents.
        /// Values > 1 increase damage (agents take more damage), values < 1 reduce damage.
        /// Modify at runtime via GC2 Instructions for damage buffs/debuffs.
        /// </summary>
        public float IncomingDamageMultiplier
        {
            get => incomingDamageMultiplier;
            set => incomingDamageMultiplier = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Multiplier applied to damage coming from the Player Target (non-agent attacker).
        /// </summary>
        public float PlayerDamageMultiplier
        {
            get => playerDamageMultiplier;
            set => playerDamageMultiplier = Mathf.Max(0f, value);
        }
        
        [Header("═══ GLOBAL ANIMATION SETTINGS ═══")]
        [Tooltip("Reference transform for animation culling (usually main camera)")]
        [SerializeField] private Transform animationCullReference;
        
        [Tooltip("Distance beyond which animations are culled")]
        [SerializeField] private float animationCullDistance = 150f;
        
        [Tooltip("Enable distance-based animation culling")]
        [SerializeField] private bool cullAnimationsByDistance = true;
        
        [Header("═══ GLOBAL AUDIO SETTINGS ═══")]
        [Tooltip("Maximum footstep sounds that can be triggered per frame. Prevents audio spam.")]
        [Range(1, 50)]
        [SerializeField] private int maxFootstepsPerFrame = 8;
        
        [Tooltip("Maximum total concurrent footstep audio sources globally. Limits total footstep audio regardless of clip variety.")]
        [Range(1, 100)]
        [SerializeField] private int maxGlobalFootstepSources = 16;

        [Tooltip("Maximum concurrent one-shot PlaySFX actions from AgentSkill globally.\n0 = unlimited.")]
        [Range(0, 200)]
        [SerializeField] private int maxGlobalSkillOneShotSFX = 0;

        [Tooltip("Maximum concurrent looping PlaySFX actions from AgentSkill globally.\n0 = unlimited.")]
        [Range(0, 100)]
        [SerializeField] private int maxGlobalSkillLoopingSFX = 0;

        [Tooltip("Maximum concurrent PlayVFX actions from AgentSkill globally.\n0 = unlimited.")]
        [Range(0, 500)]
        [SerializeField] private int maxGlobalSkillVFXInstances = 0;
        
        [Header("═══ GLOBAL SPAWN SETTINGS ═══")]
        [Tooltip("GPU Instancer Prefab Manager (typically one per scene)")]
        [SerializeField] private GPUInstancerPro.PrefabModule.GPUIPrefabManager prefabManager;
        
        [Tooltip("Register agents with GPUI immediately on spawn")]
        [SerializeField] private bool registerImmediately = false;
        
        // Per-faction defaults (fallback values from first EnemyCrowd)
        private SpawnSettings _defaultSpawnSettings;
        private NavigationSettings _defaultNavigationSettings;
        private AnimationSettings _defaultAnimationSettings;
        private CombatSettings _defaultCombatSettings;

        #endregion

        #region Events

        public event Action<GPUICrowdInstance> EventAgentKilled;
        public event Action<int, float, float> EventAgentHealthChanged;
        
        /// <summary>
        /// Fired when an EnemyCrowd agent hits the Player Target (via melee or ranged attack).
        /// Parameters: (attackerAgent, attackerPosition, targetTransform, damage, hitPoint, isMeleeAttack)
        /// Use this event to apply damage to the player via GC2's health system or custom logic.
        /// </summary>
        public event Action<CrowdAgent, Vector3, Transform, float, Vector3, bool> EventPlayerTargetHit;

        #endregion
        
        #region Network Events
        
        /// <summary>
        /// Fired when agents are spawned. Use for network synchronization.
        /// The SpawnEventData contains all information needed to replicate spawn on clients.
        /// </summary>
        public event Action<SpawnEventData> NetworkEventAgentSpawned;
        
        /// <summary>
        /// Fired when an agent takes damage. Use for network damage validation/sync.
        /// The DamageEventData contains attacker, target, damage amount, and result.
        /// </summary>
        public event Action<DamageEventData> NetworkEventDamageApplied;
        
        /// <summary>
        /// Fired when an agent dies. Use for network death synchronization.
        /// The DeathEventData contains killer info, death position, and ragdoll state.
        /// </summary>
        public event Action<DeathEventData> NetworkEventAgentDied;
        
        /// <summary>
        /// Fired when an agent's health changes (damage or healing).
        /// More detailed than EventAgentHealthChanged - includes source info.
        /// </summary>
        public event Action<HealthChangeEventData> NetworkEventHealthChanged;
        
        /// <summary>
        /// Fired when an agent's combat or navigation state changes.
        /// Use for state machine synchronization across network.
        /// </summary>
        public event Action<StateChangeEventData> NetworkEventStateChanged;
        
        /// <summary>
        /// Fired when agents are despawned/removed. Use for network cleanup.
        /// Parameters: (networkIds[], despawnReason)
        /// </summary>
        public event Action<uint[], DespawnReason> NetworkEventAgentsDespawned;

        #endregion

        #region Subsystems

        private EnemyMassesFormationSystem _formationSystem;
        private EnemyMassesSpawnController _spawnController;
        private EnemyMassesStuckRecoverySystem _stuckRecoverySystem;
        private EnemyMassesNavigationController _navigationController;
        private EnemyMassesRagdollSystem _ragdollSystem;
        private EnemyMassesAnimationController _animationController;
        private EnemyMassesVisibilityManager _visibilityManager;
        private EnemyMassesHybridWorkflowSystem _hybridWorkflowSystem;
        private EnemyMassesCombatSystem _combatSystem;
        private EnemyMassesPerformanceManager _performanceManager;
        private CivilNavigationController _civilNavigationController;
        private FootstepAudioManager _footstepAudioManager;

        #endregion

        #region Core Data

        private readonly List<CrowdAgent> _agents = new();
        private readonly Dictionary<GPUICrowdInstance, int> _instanceLookup = new();
        private SpatialGrid _spatialGrid;
        private Camera _cachedCamera;
        private bool _initialized;
        
        // Performance caches to avoid per-frame allocations
        private readonly List<int> _cachedActiveAgents = new();
        private readonly List<int> _animationRefreshIndices = new();
        private readonly List<int> _animationPriorityVisibleIndices = new();
        private readonly List<int> _animationVisibleIndices = new();
        private readonly List<int> _animationOffscreenIndices = new();

        // Per-faction overrides for Hybrid combat mode (force melee/ranged)
        private readonly Dictionary<int, CombatRangeOverrideMode> _factionCombatRangeOverrides = new();
        private readonly Dictionary<string, CombatRangeOverrideMode> _typeCombatRangeOverrides = new();
        
        // Camera tracking for optimization
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private bool _cameraHasMovedThisFrame;
        
        // Active agents tracking (rebuild only when needed)
        private bool _activeAgentsListDirty = true;
        private int _lastAgentCount = 0;
        
        // ═══════════════════════════════════════════════════════════════════
        // Network Data
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Lookup from network ID to agent index. Only populated in multiplayer.</summary>
        private readonly Dictionary<uint, int> _networkIdLookup = new();
        
        /// <summary>Next available network ID for spawned agents (server only).</summary>
        private uint _nextNetworkId = 1;
        
        /// <summary>Current spawn group ID for batch spawning.</summary>
        private int _currentSpawnGroupId = 0;

        #endregion

        #region Debug Logging

        [Header("Debug Logging")]
        [Tooltip("Log animation/navigation/combat state for a single agent to diagnose flicker.")]
        [SerializeField] private bool _debugAnimationFlickerLogs = false;

        [Tooltip("When true, log the first selected RTS agent (falls back to Debug Agent Index if none selected).")]
        [SerializeField] private bool _debugAnimationUseSelectedAgent = true;

        [Tooltip("Agent index to log when Debug Animation Logs is enabled.")]
        [SerializeField] private int _debugAnimationAgentIndex = 0;

        [Tooltip("Log only when key state changes (animation/combat/workflow/destination).")]
        [SerializeField] private bool _debugAnimationLogOnlyOnChange = true;

        [Tooltip("Seconds between debug logs (set to 0 for no throttling).")]
        [SerializeField] private float _debugAnimationLogInterval = 0.25f;

        private float _nextAnimationDebugLogTime;
        private DebugAnimationSnapshot _lastAnimationDebugSnapshot;
        private bool _hasAnimationDebugSnapshot;

        [Header("Debug - A* Fake Physics")]
        [Tooltip("If enabled, disables fake physics impulses for A* agents (no lift/pull/push).")]
        [SerializeField] private bool disableAstarFakePhysics = false;

        public bool DisableAstarFakePhysics => disableAstarFakePhysics;

        private struct DebugAnimationSnapshot
        {
            public CrowdAnimationState animationState;
            public CrowdCombatState combatState;
            public AgentNavigationState navigationState;
            public HybridWorkflowType workflow;
            public bool isDead;
            public bool isVisible;
            public bool needsAnimationRefresh;
            public bool hasPath;
            public bool pathPending;
            public bool isStopped;
            public bool hasArrived;
            public int destinationGeneration;
            public Vector3 destination;
            public Vector3 lastDestination;
            public float remainingDistance;
            public float normalizedSpeed;
            public float desiredSpeed;
            public float velocitySpeed;
            public bool isInstanced;
            public int bufferIndex;
            public int renderKey;
            public int workflowId;

            public bool HasMeaningfulChange(DebugAnimationSnapshot other)
            {
                if (animationState != other.animationState) return true;
                if (combatState != other.combatState) return true;
                if (navigationState != other.navigationState) return true;
                if (workflow != other.workflow) return true;
                if (workflowId != other.workflowId) return true;
                if (isDead != other.isDead) return true;
                if (isVisible != other.isVisible) return true;
                if (needsAnimationRefresh != other.needsAnimationRefresh) return true;
                if (hasPath != other.hasPath) return true;
                if (pathPending != other.pathPending) return true;
                if (isStopped != other.isStopped) return true;
                if (hasArrived != other.hasArrived) return true;
                if (destinationGeneration != other.destinationGeneration) return true;
                if (isInstanced != other.isInstanced) return true;
                if (bufferIndex != other.bufferIndex) return true;
                if (renderKey != other.renderKey) return true;
                if ((destination - other.destination).sqrMagnitude > 0.05f * 0.05f) return true;
                if ((lastDestination - other.lastDestination).sqrMagnitude > 0.05f * 0.05f) return true;
                if (Mathf.Abs(remainingDistance - other.remainingDistance) > 0.2f) return true;
                if (Mathf.Abs(normalizedSpeed - other.normalizedSpeed) > 0.05f) return true;
                if (Mathf.Abs(desiredSpeed - other.desiredSpeed) > 0.2f) return true;
                if (Mathf.Abs(velocitySpeed - other.velocitySpeed) > 0.2f) return true;
                return false;
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Instance = this;
            Initialize();
        }

        private void Update()
        {
            if (!_initialized)
                return;
            
            // Skip updates during play mode exit to prevent GPUI errors
            // GPUI systems start shutting down before our Update stops
            if (!Application.isPlaying || !GPUIRenderingSystem.IsActive)
                return;

            // Handle runtime click-to-move
            if (enableClickToMove)
            {
                HandleClickToMove();
            }

            UpdateAllSystems();
        }

        private void OnValidate()
        {
            maxFootstepsPerFrame = Mathf.Clamp(maxFootstepsPerFrame, 1, 50);
            maxGlobalFootstepSources = Mathf.Clamp(maxGlobalFootstepSources, 1, 100);
            maxGlobalSkillOneShotSFX = Mathf.Max(0, maxGlobalSkillOneShotSFX);
            maxGlobalSkillLoopingSFX = Mathf.Max(0, maxGlobalSkillLoopingSFX);
            maxGlobalSkillVFXInstances = Mathf.Max(0, maxGlobalSkillVFXInstances);

            if (!Application.isPlaying)
                return;

            _footstepAudioManager?.SetGlobalLimits(maxFootstepsPerFrame, maxGlobalFootstepSources);
            ApplySkillActionGlobalCaps();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Cleanup();
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_initialized)
                return;

            EnsurePlayerTargetsList();

            // Phase 3: Create default settings objects from first EnemyCrowd
            CreateDefaultSettings();

            // Validate settings
            if (!ValidateSettings())
                return;

            // Find camera
            _cachedCamera = Camera.main;
            if (_cachedCamera == null)
                _cachedCamera = FindFirstObjectByType<Camera>();

            ResolveRTSController();

            ApplySkillActionGlobalCaps();

            // Create spatial grid from global performance settings
            if (performanceSettings != null && performanceSettings.enableSpatialGrid)
                _spatialGrid = new SpatialGrid(performanceSettings.gridCellSize);

            // Initialize subsystems
            InitializeSubsystems();

            _initialized = true;
        }

        private void ResolveRTSController()
        {
            if (rtsController != null)
                return;

#if UNITY_2023_1_OR_NEWER
            rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
#else
            rtsController = FindObjectOfType<EnemyMassesRTSController>();
#endif
        }

        private void EnsurePlayerTargetsList()
        {
            if (playerTargets == null)
                playerTargets = new List<Transform>();
        }

        /// <summary>
        /// Returns the primary player target (first non-null in list, otherwise legacy single target).
        /// </summary>
        public Transform GetPrimaryPlayerTarget()
        {
            if (playerTargets != null)
            {
                for (int i = 0; i < playerTargets.Count; i++)
                {
                    Transform target = playerTargets[i];
                    if (target != null)
                        return target;
                }
            }

            return playerTarget;
        }

        /// <summary>
        /// Returns the closest player target to a world position (list-aware, falls back to legacy target).
        /// </summary>
        public Transform GetNearestPlayerTarget(Vector3 position)
        {
            Transform bestTarget = null;
            float bestSqr = float.MaxValue;

            if (playerTargets != null)
            {
                for (int i = 0; i < playerTargets.Count; i++)
                {
                    Transform target = playerTargets[i];
                    if (target == null)
                        continue;

                    float distSqr = (target.position - position).sqrMagnitude;
                    if (distSqr < bestSqr)
                    {
                        bestSqr = distSqr;
                        bestTarget = target;
                    }
                }
            }

            if (playerTarget != null)
            {
                float distSqr = (playerTarget.position - position).sqrMagnitude;
                if (distSqr < bestSqr)
                {
                    bestSqr = distSqr;
                    bestTarget = playerTarget;
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// Checks if a transform belongs to any configured player target (direct or child).
        /// </summary>
        public bool IsPlayerTargetTransform(Transform candidate)
        {
            if (candidate == null)
                return false;

            if (playerTargets != null)
            {
                for (int i = 0; i < playerTargets.Count; i++)
                {
                    Transform target = playerTargets[i];
                    if (target == null)
                        continue;

                    if (candidate == target || candidate.IsChildOf(target))
                        return true;
                }
            }

            return playerTarget != null && (candidate == playerTarget || candidate.IsChildOf(playerTarget));
        }

        /// <summary>
        /// Adds a player target to the list if not already present.
        /// </summary>
        public bool AddPlayerTarget(Transform target)
        {
            if (target == null)
                return false;

            EnsurePlayerTargetsList();
            playerTargets.RemoveAll(t => t == null);

            if (playerTargets.Contains(target))
                return false;

            playerTargets.Add(target);
            if (playerTarget == null)
                playerTarget = target;

            return true;
        }

        /// <summary>
        /// Removes a player target from the list and legacy field.
        /// </summary>
        public bool RemovePlayerTarget(Transform target)
        {
            if (target == null)
                return false;

            EnsurePlayerTargetsList();
            int removed = playerTargets.RemoveAll(t => t == target);
            bool removedLegacy = false;
            if (playerTarget == target)
            {
                playerTarget = null;
                removedLegacy = true;
            }

            return removed > 0 || removedLegacy;
        }

        private void CreateDefaultSettings()
        {
            // Phase 3: Create default settings objects for subsystem initialization
            // These are used as fallbacks when agent.enemyCrowd is null
            // If we have crowds, use the first one's settings as baseline
            EnemyCrowd firstCrowd = enemyCrowds != null && enemyCrowds.Count > 0 ? enemyCrowds[0] : null;

            _defaultSpawnSettings = new SpawnSettings
            {
                prefabManager = prefabManager,  // Use global prefabManager
                registerImmediately = registerImmediately,  // Use global registerImmediately
                indicatorPrefab = firstCrowd?.indicatorPrefab,
                formationType = firstCrowd?.defaultFormationType ?? FormationType.Random,
                formationSize = firstCrowd?.defaultFormationSize ?? 10f,
                formationSpacing = firstCrowd?.defaultFormationSpacing ?? 2f,
                formationThickness = firstCrowd?.defaultFormationThickness ?? 3f
            };

            _defaultNavigationSettings = new NavigationSettings
            {
                destinationSpread = firstCrowd?.destinationSpread ?? 25f,
                throttleDestinationUpdates = firstCrowd?.throttleDestinationUpdates ?? true,
                maxDestinationUpdatesPerFrame = firstCrowd?.maxDestinationUpdatesPerFrame ?? 256,
                priorityDestinationBonus = firstCrowd?.priorityDestinationBonus ?? 128,
                offscreenNavSyncInterval = firstCrowd?.offscreenNavSyncInterval ?? 0.25f,
                navmeshRotationLerp = firstCrowd?.navmeshRotationLerp ?? 0.15f,
                enableStuckRecovery = firstCrowd?.enableStuckRecovery ?? true,
                teleportStuckAgents = firstCrowd?.teleportStuckAgents ?? true,
                stuckTimeout = firstCrowd?.stuckTimeout ?? 6f,
                stuckProximitySlack = firstCrowd?.stuckProximitySlack ?? 0.5f,
                stuckVelocityThreshold = firstCrowd?.stuckVelocityThreshold ?? 0.15f,
                stuckRetryInterval = firstCrowd?.stuckRetryInterval ?? 4f,
                stuckRetryAttempts = firstCrowd?.stuckRetryAttempts ?? 3,
                teleportAfterRetries = firstCrowd?.teleportAfterRetries ?? false,
                enableArrivalSync = firstCrowd?.enableArrivalSync ?? true,
                arrivalSyncDistance = firstCrowd?.arrivalSyncDistance ?? 5f,
                reapplyFormationOnArrival = firstCrowd?.reapplyFormationOnArrival ?? false
            };

            _defaultAnimationSettings = new AnimationSettings
            {
                minBlendUpdateDelta = firstCrowd?.minBlendUpdateDelta ?? 0.02f,
                controlAnimationsWithGPUI = firstCrowd?.controlAnimationsWithGPUI ?? true,
                animatorWorkflow = firstCrowd?.animatorWorkflow ?? AnimatorWorkflowOverride.ComputeAnimator,
                locomotionStateName = firstCrowd?.locomotionStateNameOverride ?? "Locomotion",
                locomotionBlendMode = firstCrowd?.locomotionBlendModeOverride ?? LocomotionBlendMode.OneD,
                locomotionSpeedFloatParameter = string.IsNullOrWhiteSpace(firstCrowd?.locomotionSpeedFloatParameterOverride) ? "Speed" : firstCrowd.locomotionSpeedFloatParameterOverride,
                locomotionDirectionXFloatParameter = string.IsNullOrWhiteSpace(firstCrowd?.locomotionDirectionXFloatParameterOverride) ? "MoveX" : firstCrowd.locomotionDirectionXFloatParameterOverride,
                locomotionDirectionYFloatParameter = string.IsNullOrWhiteSpace(firstCrowd?.locomotionDirectionYFloatParameterOverride) ? "MoveY" : firstCrowd.locomotionDirectionYFloatParameterOverride,
                enableDirectionalLocomotion = firstCrowd?.enableDirectionalLocomotionOverride ?? false,
                directionalLocomotionAnimations = firstCrowd != null ? firstCrowd.directionalLocomotionAnimationsOverride : new DirectionalLocomotionClips(),
                directionalLocomotionThreshold = firstCrowd?.directionalLocomotionThresholdOverride ?? 0.4f,
                throttleAnimationUpdates = firstCrowd?.throttleAnimationUpdates ?? true,
                maxAnimationUpdatesPerFrame = firstCrowd?.maxAnimationUpdatesPerFrame ?? 500,
                priorityAnimationBonus = firstCrowd?.priorityAnimationBonus ?? 250,
                cullAnimationsByDistance = cullAnimationsByDistance,  // Use global setting
                animationCullDistance = animationCullDistance,  // Use global setting
                animationCullReference = animationCullReference,  // Use global setting
                speedSmoothing = firstCrowd?.speedSmoothing ?? 8f,
                fallingClip = firstCrowd?.fallingClipOverride,
                landingClip = firstCrowd?.landingClipOverride
            };

            _defaultCombatSettings = new CombatSettings
            {
                hitDetectionMode = hitDetectionMode,
                enableCombatSystem = firstCrowd?.enableCombatSystem ?? false,
                engagementRadius = firstCrowd?.engagementRadius ?? 15f,
                agentBaseHealth = firstCrowd?.agentBaseHealth ?? 50f,
                agentHealthVariance = firstCrowd?.agentHealthVariance ?? 15f,
                agentAttackDamage = firstCrowd?.agentAttackDamage ?? 10f,
                attackCooldown = firstCrowd?.attackCooldown ?? 2f,
                attackCooldownJitter = firstCrowd?.attackCooldownJitter ?? new Vector2(0.5f, 1.5f),
                blockChance = firstCrowd?.blockChance ?? 0.2f,
                blockHoldTimeRange = firstCrowd?.blockHoldTimeRange ?? new Vector2(0.4f, 0.85f),
                maxCombatUpdatesPerFrame = firstCrowd?.maxCombatUpdatesPerFrame ?? 512,
                navTargetUpdateInterval = firstCrowd?.navTargetUpdateInterval ?? 0.2f,
                maintainFacingBetweenAttacks = firstCrowd?.maintainFacingBetweenAttacks ?? true,
                stopMovementWhenClose = firstCrowd?.stopMovementWhenClose ?? true,
                lockMovementDuringAttacks = firstCrowd?.lockMovementDuringAttacks ?? true,
                triggerHitReactionOnDamage = firstCrowd?.triggerHitReactionOnDamage ?? true,
                enableMeleeAutoAttack = firstCrowd?.enableMeleeAutoAttack ?? true,
                enablePostAttackRetreat = firstCrowd?.enablePostAttackRetreat ?? true,
                postAttackRetreatDistance = firstCrowd?.postAttackRetreatDistance ?? new Vector2(0.75f, 1.5f)
            };

            // Performance settings are now global (serialized field), not per-faction
        }

        private bool ValidateSettings()
        {
            // Check global performance settings (initialize if needed)
            if (performanceSettings == null)
            {
                performanceSettings = new PerformanceSettings();
                Debug.LogWarning("[EnemyMasses] PerformanceSettings was null, initialized with defaults.");
            }
            
            // Check EnemyCrowd assets
            if (enemyCrowds == null || enemyCrowds.Count == 0)
            {
                Debug.LogError("[EnemyMasses] No EnemyCrowd assets configured! Add at least one EnemyCrowd asset to spawn agents.");
                return false;
            }

            // Validate that at least one crowd has required settings
            bool hasValidCrowd = false;
            for (int i = 0; i < enemyCrowds.Count; i++)
            {
                if (enemyCrowds[i] != null && enemyCrowds[i].prefab != null)
                {
                    hasValidCrowd = true;
                    break;
                }
            }

            if (!hasValidCrowd)
            {
                Debug.LogError("[EnemyMasses] No valid EnemyCrowd assets found! Each EnemyCrowd must have a prefab assigned.");
                return false;
            }

            return true;
        }

        private void InitializeSubsystems()
        {
            // Phase 3: All subsystems now use default settings for construction
            // Actual per-agent values come from agent.enemyCrowd via Phase 2 helper methods

            // Formation System
            _formationSystem = new EnemyMassesFormationSystem(_defaultSpawnSettings);

            // Visibility Manager (needs to be first to provide lists for navigation)
            _visibilityManager = new EnemyMassesVisibilityManager(
                performanceSettings,
                _agents);
            _visibilityManager.Initialize();

            // Navigation Controller
            _navigationController = new EnemyMassesNavigationController(
                _defaultNavigationSettings,
                performanceSettings,
                _defaultCombatSettings,
                _formationSystem,
                _agents,
                enemyCrowds,
                _visibilityManager.PriorityAgentIndices as List<int> ?? new List<int>(),
                _visibilityManager.VisibleAgentIndices as List<int> ?? new List<int>(),
                _visibilityManager.OffscreenAgentIndices as List<int> ?? new List<int>(),
                _visibilityManager.PriorityAgentSet);
            Transform primaryTarget = GetPrimaryPlayerTarget();
            _navigationController.Initialize(primaryTarget != null ? primaryTarget.position : Vector3.zero);
            // Set civil agent callback to exclude civil agents from formation navigation
            _navigationController.IsAgentCivilCallback = IsAgentCivil;

            // Spawn Controller
            _spawnController = new EnemyMassesSpawnController(
                _defaultSpawnSettings,
                _defaultAnimationSettings,
                _defaultNavigationSettings,
                performanceSettings,
                _formationSystem,
                _agents,
                enemyCrowds,
                _instanceLookup);
            _spawnController.HitDetectionMode = hitDetectionMode;
            _spawnController.Initialize(transform);

            // Stuck Recovery System
            _stuckRecoverySystem = new EnemyMassesStuckRecoverySystem(
                _defaultNavigationSettings,
                _formationSystem);

            // Ragdoll System
            _ragdollSystem = new EnemyMassesRagdollSystem(
                _defaultCombatSettings,
                enemyCrowds);
            
            // Wire up ragdoll callbacks to spawn controller:
            // 1. Force Bone Tracker workflow for ragdoll-enabled factions (must happen first!)
            // 2. Then deactivate ragdoll physics (disable colliders/make rigidbodies kinematic)
            _spawnController.ForceBoneTrackerForRagdollCallback = ForceBoneTrackerWorkflow;
            _spawnController.InitializeRagdollCallback = _ragdollSystem.DeactivateRagdollPhysics;

            // Animation Controller
            _animationController = new EnemyMassesAnimationController(
                _defaultAnimationSettings,
                _agents,
                enemyCrowds);

            // Hybrid Workflow System
            _hybridWorkflowSystem = new EnemyMassesHybridWorkflowSystem(
                performanceSettings,
                _defaultAnimationSettings,
                _agents,
                _cachedCamera);
            _hybridWorkflowSystem.Initialize();

            // Combat System
            _combatSystem = new EnemyMassesCombatSystem(
                _defaultCombatSettings,
                _defaultAnimationSettings,
                _agents,
                enemyCrowds,
                GetNearestPlayerTarget,
                GetPrimaryPlayerTarget);
            _combatSystem.Initialize();

            // Performance Manager
            _performanceManager = new EnemyMassesPerformanceManager(
                performanceSettings,
                _defaultCombatSettings,
                _agents,
                _spatialGrid);
            _performanceManager.Initialize();

            // Civil Navigation Controller (handles autonomous NPC waypoint navigation)
            _civilNavigationController = new CivilNavigationController(
                this,
                _agents,
                enemyCrowds);
            _civilNavigationController.Initialize();
            
            // Footstep Audio Manager (handles interval-based and event-triggered footstep sounds)
            _footstepAudioManager = new FootstepAudioManager(_agents, enemyCrowds);
            _footstepAudioManager.SetGlobalLimits(maxFootstepsPerFrame, maxGlobalFootstepSources);
            
            // Wire up civil animation blend callback to use walk animation instead of run
            _animationController.GetCivilAnimationBlendCallback = _civilNavigationController.GetCivilAnimationBlend;
            
            // Wire up corner smoothing check callback to prevent idle animation during corner smoothing
            _animationController.IsCivilAgentCornerSmoothingCallback = _civilNavigationController.IsCivilAgentCornerSmoothing;

            // Hook up events
            _combatSystem.OnAgentKilled += HandleAgentKilled;
            _combatSystem.OnAgentHealthChanged += HandleAgentHealthChanged;
            _combatSystem.OnPlayerTargetHit += HandlePlayerTargetHit;
            
            // Wire up ragdoll system to combat system for death ragdoll activation
            _combatSystem.SetRagdollSystem(_ragdollSystem, ForceBoneTrackerWorkflow);
            
            // Ensure Projectile System exists (auto-create if needed)
            EnsureProjectileSystemExists();
        }
        
        /// <summary>
        /// Ensures the EnemyMassesProjectileSystem singleton exists.
        /// Auto-creates it if not found in the scene.
        /// </summary>
        private void EnsureProjectileSystemExists()
        {
            // First check the static Instance
            if (EnemyMassesProjectileSystem.Instance != null)
            {
                return;
            }
            
            // Try to find existing component - Instance may be null due to script execution order
            // (this Awake may run before EnemyMassesProjectileSystem.Awake sets the static Instance)
            var existingSystem = FindFirstObjectByType<EnemyMassesProjectileSystem>();
            if (existingSystem != null)
            {
                return;
            }
            
            // Auto-create the projectile system
            GameObject projectileSystemGO = new GameObject("EnemyMassesProjectileSystem");
            projectileSystemGO.transform.SetParent(transform);
            projectileSystemGO.AddComponent<EnemyMassesProjectileSystem>();
        }

        #endregion

        #region Update Loop

        private void UpdateAllSystems()
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return;

            // Update civil navigation FIRST - it needs to run even with 0 agents
            // to handle auto-spawning from spawn waypoints
            _civilNavigationController.Update(Time.deltaTime);
            
            int agentCount = _agents.Count;
            if (agentCount == 0)
                return;

            // Update faction battle targets if enabled
            // PERFORMANCE: Process in batches across multiple frames to spread CPU load
            if (factionBattleMode)
            {
                if (maxFactionTargetUpdatesPerFrame <= 0)
                {
                    // No batching - process all at once when timer fires (original behavior)
                    if (Time.time >= _nextFactionBattleTargetUpdate)
                    {
                        AssignFactionBattleTargets();
                        _nextFactionBattleTargetUpdate = Time.time + factionBattleTargetUpdateInterval;
                    }
                }
                else
                {
                    // Batched processing - process a portion of agents each frame
                    AssignFactionBattleTargetsBatched(maxFactionTargetUpdatesPerFrame);
                }
            }
            
            // When faction battle is disabled, we don't need to do anything special
            // The combat system will naturally re-assign the player target

            // Cache camera transform to avoid repeated property access
            Transform cameraTransform = _cachedCamera != null ? _cachedCamera.transform : null;
            
            // Check if camera moved this frame (optimization to skip frustum updates)
            _cameraHasMovedThisFrame = false;
            if (cameraTransform != null)
            {
                Vector3 currentCamPos = cameraTransform.position;
                Quaternion currentCamRot = cameraTransform.rotation;
                
                // Check for significant movement (squared magnitude is faster than magnitude)
                _cameraHasMovedThisFrame = (currentCamPos - _lastCameraPosition).sqrMagnitude > 0.01f ||
                                          Quaternion.Angle(currentCamRot, _lastCameraRotation) > 1f;
                
                if (_cameraHasMovedThisFrame)
                {
                    _lastCameraPosition = currentCamPos;
                    _lastCameraRotation = currentCamRot;
                }
            }

            // Update visibility and frustum culling (optimized - only update frustum if camera moved)
            if (_cameraHasMovedThisFrame || Time.frameCount % 3 == 0) // Update every 3 frames minimum even if static
            {
                _visibilityManager.UpdateFrustumPlanes(_cachedCamera);
            }
            
            _visibilityManager.UpdateVisibilityLists(
                agentCount,
                performanceSettings.prioritizeVisibleAgents,
                _cachedCamera,
                null, // syncAgentCallback - not needed for basic operation
                null, // checkProgressCallback - not needed for basic operation
                performanceSettings.enableLargeCrowdOptimizations);

            // Update performance systems (spatial grid, anti-gridlock, dead cleanup)
            _performanceManager.UpdatePerformanceSystems(IsAgentActiveForNavigation);

            // Update navigation (throttled destination assignment)
            // Always update navigation to support click-to-move in developer mode
            _navigationController.UpdateDestinations(
                performanceSettings.prioritizeVisibleAgents,
                _spatialGrid);

            // Update attack target destinations (agents chasing specific targets)
            // Use higher quota since these agents need to continuously track moving targets
            int attackTargetQuota = Mathf.Max(100, _defaultNavigationSettings.maxDestinationUpdatesPerFrame * 2);
            _navigationController.UpdateAttackTargets(attackTargetQuota);

            // Sync NavMeshAgent movement to transforms (required for updatePosition=false)
            // Only rebuild active agents list when agent count changed or list is marked dirty
            if (_activeAgentsListDirty || _lastAgentCount != agentCount || Time.frameCount % 30 == 0)
            {
                _cachedActiveAgents.Clear();
                for (int i = 0; i < agentCount; i++)
                {
                    CrowdAgent agent = _agents[i];
                    // Include alive agents OR dead agents with active impulses/crash
                    if (agent != null && (!_performanceManager.IsAgentDead(i) || agent.hasNavmeshOffsetImpulse || agent.hasAstarKnockbackImpulse || agent.isCrashing))
                    {
                        if (agent.navigationAgent == null)
                            continue;

                        if (disableAstarFakePhysics &&
                            agent.navigationAgent.Capabilities.SupportsMoveBasedKnockback &&
                            agent.navigationAgent.NavigationType.RequiresAStar())
                        {
                            if (agent.moveBasedKnockbackForcedStop && agent.navigationAgent.Enabled)
                                agent.navigationAgent.IsStopped = agent.moveBasedKnockbackWasStoppedBeforeImpulse;

                            agent.hasAstarKnockbackImpulse = false;
                            agent.astarKnockbackVelocity = Vector3.zero;
                            agent.astarKnockbackVerticalVelocity = 0f;
                            agent.astarVisualOffsetY = 0f;
                            agent.astarKnockbackEndTime = 0f;
                            agent.moveBasedKnockbackForcedStop = false;
                            agent.moveBasedKnockbackWasStoppedBeforeImpulse = false;

                            if (agent.hasNavmeshOffsetImpulse)
                            {
                                agent.hasNavmeshOffsetImpulse = false;
                                agent.navmeshOffset = Vector3.zero;
                                agent.navmeshOffsetVelocity = Vector3.zero;
                                agent.navmeshOffsetDuration = 0f;
                                agent.navmeshOffsetEndTime = 0f;
                            }
                        }

                        if (agent.navigationAgent.NavigationType != NavigationType.FlowField &&
                            !agent.navigationAgent.Capabilities.SupportsNavmeshOffsetImpulse &&
                            !agent.navigationAgent.UpdatePosition &&
                            !agent.hasNavmeshOffsetImpulse &&
                            !agent.hasAstarKnockbackImpulse)
                        {
                            agent.navigationAgent.UpdatePosition = true;
                            agent.navigationAgent.UpdateRotation = true;
                        }
                        
                        bool needsSync = agent.navigationAgent.Capabilities.SupportsNavmeshOffsetImpulse ||
                                         !agent.navigationAgent.UpdatePosition ||
                                         agent.hasNavmeshOffsetImpulse ||
                                         agent.hasAstarKnockbackImpulse ||
                                         agent.isCrashing;
                        
                        if (needsSync)
                            _cachedActiveAgents.Add(i);
                    }
                }
                _activeAgentsListDirty = false;
                _lastAgentCount = agentCount;
            }
            
            // Use rotation lerp setting (0-1 range, applied per-frame via Slerp)
            // PERFORMANCE: Pass offscreen sync interval for time-based throttling of offscreen agents
            _navigationController.SyncAgentMovement(
                _cachedActiveAgents, 
                _defaultNavigationSettings.navmeshRotationLerp,
                _defaultNavigationSettings.offscreenNavSyncInterval);

            bool throttleAnimations = _defaultAnimationSettings.throttleAnimationUpdates &&
                                      performanceSettings.preset != PerformancePreset.None;

            if (throttleAnimations && performanceSettings.prioritizeVisibleAgents && performanceSettings.rotateVisibleAnimationUpdates)
            {
                _visibilityManager.BuildAnimationUpdateLists(
                    _animationRefreshIndices,
                    _animationPriorityVisibleIndices,
                    _animationVisibleIndices,
                    _animationOffscreenIndices);

                int refreshCount = _animationRefreshIndices.Count;
                int visibleCount = _animationPriorityVisibleIndices.Count + _animationVisibleIndices.Count;
                int offscreenCount = _animationOffscreenIndices.Count;
                int totalCount = refreshCount + visibleCount + offscreenCount;

                if (totalCount > 0)
                {
                    int quota = Mathf.Min(_defaultAnimationSettings.maxAnimationUpdatesPerFrame, totalCount);
                    quota = Mathf.Min(quota + _defaultAnimationSettings.priorityAnimationBonus, totalCount);
                    quota = Mathf.Clamp(quota, 1, totalCount);

                    int processed = 0;
                    processed += ProcessAnimationListSequential(_animationRefreshIndices, quota);

                    int remaining = quota - processed;
                    if (remaining > 0 && visibleCount > 0)
                    {
                        processed += ProcessVisibleAnimationListWithCursor(
                            _animationPriorityVisibleIndices,
                            _animationVisibleIndices,
                            remaining,
                            ref _animationVisibleUpdateCursor);
                    }

                    remaining = quota - processed;
                    if (remaining > 0 && offscreenCount > 0)
                    {
                        ProcessAnimationListWithCursor(_animationOffscreenIndices, remaining, ref _animationOffscreenUpdateCursor);
                    }
                }
            }
            else
            {
                // Build animation priority list
                var animationPriorityIndices = _visibilityManager.BuildAnimationPriorityList();

                // Update animations (locomotion blending, state management)
                int animCount = animationPriorityIndices.Count;
                if (animCount > 0)
                {
                    int quota = throttleAnimations
                        ? Mathf.Min(_defaultAnimationSettings.maxAnimationUpdatesPerFrame, animCount)
                        : animCount;

                    if (throttleAnimations && performanceSettings.prioritizeVisibleAgents)
                        quota = Mathf.Min(quota + _defaultAnimationSettings.priorityAnimationBonus, animCount);

                    quota = Mathf.Clamp(quota, 1, animCount);

                    int processed = 0;
                    int cursor = performanceSettings.prioritizeVisibleAgents ? 0 : _animationUpdateCursor;

                    while (processed < quota)
                    {
                        int listIndex = (cursor + processed) % animCount;
                        int agentIndex = animationPriorityIndices[listIndex];
                        if (agentIndex >= 0 && agentIndex < agentCount)
                        {
                            CrowdAgent agent = _agents[agentIndex];
                            if (agent != null)
                            {
                                float normalizedSpeed = _animationController.CalculateNormalizedSpeed(agent);
                                _animationController.HandleAnimation(agent, normalizedSpeed, _defaultCombatSettings.enableCombatSystem);
                            }
                        }

                        processed++;
                    }

                    if (!performanceSettings.prioritizeVisibleAgents && throttleAnimations && quota < animCount)
                        _animationUpdateCursor = (cursor + processed) % animCount;
                    else
                        _animationUpdateCursor = 0;
                }
            }

            // Update flight animation transitions (take-off → loop, landing → idle)
            UpdateFlightAnimations();

            // Update footstep audio (interval-based playback)
            _footstepAudioManager?.UpdateFootsteps(Time.deltaTime);
            
            // Update ambient sounds (periodic idle/alive sounds like chants, grunts)
            _footstepAudioManager?.UpdateAmbientSounds(Time.deltaTime);

            // Update combat system
            _combatSystem.UpdateCombat(
                developerTestMode,
                _defaultCombatSettings.maxCombatUpdatesPerFrame,
                (agent) => _performanceManager.IsAgentDead(_agents.IndexOf(agent)),
                null, // Developer mode callbacks not implemented in this extraction
                (agent) => _animationController.EnsureIdleAnimation(agent), // Ensure idle animation for agents with no target
                null,
                null,
                factionBattleMode, // Pass faction battle mode flag
                IsAgentCivil); // Skip civil agents - they use their own navigation system

            // Update sinking agents every frame for smooth animation (separate from combat cursor)
            _combatSystem.UpdateSinkingAgents();

            // Reclaim finished VFX back to pools
            _combatSystem.ReclaimFinishedVFX();

            // Process delayed animator disables (allows death animations to be read by MecanimReader)
            _performanceManager.UpdateDeadAgentAnimatorDisable();

            // Update hybrid workflows (distance-based workflow switching)
            _hybridWorkflowSystem.UpdateHybridWorkflows(
                ShouldUseRagdoll,
                ForceBoneTrackerWorkflow,
                DeactivateRagdoll,
                _animationController.CalculateNormalizedSpeed,
                _animationController.StartLocomotionBlend);

            // Note: Civil navigation is updated at the start of UpdateAllSystems
            // to ensure auto-spawning works even with 0 agents

            // Update stuck recovery - throttled to reduce per-frame cost
            // Only check a subset of agents per frame
            int stuckCheckCount = Mathf.Min(50, agentCount); // Check max 50 agents per frame
            for (int i = 0; i < stuckCheckCount; i++)
            {
                int agentIndex = (_stuckCheckCursor + i) % agentCount;
                CrowdAgent agent = _agents[agentIndex];
                if (agent != null && !_performanceManager.IsAgentDead(agentIndex))
                {
                    _stuckRecoverySystem.CheckAgentProgress(agent);
                }
            }
            _stuckCheckCursor = (_stuckCheckCursor + stuckCheckCount) % agentCount;

            MaybeLogAnimationDebug();
        }

        private int ProcessAnimationListSequential(List<int> indices, int quota)
        {
            int processed = 0;
            int count = indices.Count;
            for (int i = 0; i < count && processed < quota; i++)
            {
                ProcessAnimationAgent(indices[i]);
                processed++;
            }

            return processed;
        }

        private int ProcessAnimationListWithCursor(List<int> indices, int quota, ref int cursor)
        {
            int count = indices.Count;
            if (count == 0 || quota <= 0)
                return 0;

            int processed = 0;
            int start = cursor;
            while (processed < quota && processed < count)
            {
                int listIndex = (start + processed) % count;
                ProcessAnimationAgent(indices[listIndex]);
                processed++;
            }

            cursor = processed >= count ? 0 : (start + processed) % count;
            return processed;
        }

        private int ProcessVisibleAnimationListWithCursor(
            List<int> priorityIndices,
            List<int> visibleIndices,
            int quota,
            ref int cursor)
        {
            int priorityCount = priorityIndices.Count;
            int visibleCount = visibleIndices.Count;
            int total = priorityCount + visibleCount;
            if (total == 0 || quota <= 0)
                return 0;

            int processed = 0;
            int start = cursor;
            while (processed < quota && processed < total)
            {
                int listIndex = (start + processed) % total;
                int agentIndex = listIndex < priorityCount
                    ? priorityIndices[listIndex]
                    : visibleIndices[listIndex - priorityCount];

                ProcessAnimationAgent(agentIndex);
                processed++;
            }

            cursor = processed >= total ? 0 : (start + processed) % total;
            return processed;
        }

        private void ProcessAnimationAgent(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null)
                return;

            float normalizedSpeed = _animationController.CalculateNormalizedSpeed(agent);
            _animationController.HandleAnimation(agent, normalizedSpeed, _defaultCombatSettings.enableCombatSystem);
        }
        
        // Cursor for throttled stuck checking
        private int _stuckCheckCursor = 0;

        #endregion

        #region Public API - Spawning

        /// <summary>
        /// Spawns agents from a specific enemy crowd faction by index.
        /// </summary>
        public List<int> SpawnEnemyCrowd(int enemyCrowdIndex, Vector3 spawnPosition, Quaternion spawnRotation, int count, FormationType formation = FormationType.Random, float? formationSpacingOverride = null)
        {
            if (enemyCrowdIndex < 0 || enemyCrowdIndex >= enemyCrowds.Count)
            {
                Debug.LogError($"[EnemyMasses] Invalid enemy crowd index: {enemyCrowdIndex}. Valid range: 0-{enemyCrowds.Count - 1}");
                return null;
            }

            EnemyCrowd enemyCrowd = enemyCrowds[enemyCrowdIndex];
            if (enemyCrowd == null || enemyCrowd.prefab == null)
            {
                Debug.LogError($"[EnemyMasses] Enemy crowd at index {enemyCrowdIndex} has no prefab assigned!");
                return null;
            }

            float originalSpacing = _defaultSpawnSettings.formationSpacing;
            if (formationSpacingOverride.HasValue && !Mathf.Approximately(formationSpacingOverride.Value, 0f))
                _defaultSpawnSettings.formationSpacing = Mathf.Abs(formationSpacingOverride.Value);

            List<int> spawnedIndices;
            try
            {
                spawnedIndices = _spawnController.SpawnEnemyCrowd(
                    enemyCrowdIndex,
                    spawnPosition,
                    spawnRotation,
                    count,
                    formation);
            }
            finally
            {
                _defaultSpawnSettings.formationSpacing = originalSpacing;
            }

            if (spawnedIndices != null && spawnedIndices.Count > 0)
            {
                // Initialize combat for spawned agents
                foreach (int index in spawnedIndices)
                {
                    if (index >= 0 && index < _agents.Count)
                    {
                        var agent = _agents[index];
                        _combatSystem.InitializeCombatAgent(agent, true);
                        ApplyFactionCombatRangeOverride(agent);
                    }
                }
                
                // If faction battle mode is active, immediately assign targets for new agents
                if (factionBattleMode)
                {
                    AssignFactionBattleTargets();
                }
                
                // Schedule delayed GPUI registration verification to catch invisible agents
                // This runs after GPU Instancer has processed its deferred queue
                StartCoroutine(VerifyGPUIRegistrationDelayed(spawnedIndices, 0.1f));
                
                // ═══ Network Hook: Fire spawn event for network synchronization ═══
                FireNetworkSpawnEvent(spawnedIndices, enemyCrowdIndex, spawnPosition, spawnRotation, formation);
            }

            return spawnedIndices;
        }
        
        /// <summary>
        /// Verifies GPUI registration for spawned agents after a delay.
        /// Fixes any agents that failed to register properly (invisible agents).
        /// </summary>
        private System.Collections.IEnumerator VerifyGPUIRegistrationDelayed(List<int> agentIndices, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            int fixedCount = 0;
            int syncedCount = 0;
            
            foreach (int index in agentIndices)
            {
                if (index < 0 || index >= _agents.Count)
                    continue;
                    
                var agent = _agents[index];
                if (agent == null || agent.isDead || agent.prefabComponent == null)
                    continue;
                
                var gpuiPrefab = agent.prefabComponent;
                
                // Check if not properly instanced
                bool notRegistered = !gpuiPrefab.IsInstanced || gpuiPrefab.bufferIndex < 0;
                
                if (notRegistered)
                {
                    // Agent is definitely not registered - force re-registration
                    ForceGPUIReRegistration(agent, gpuiPrefab);
                    fixedCount++;
                }
                else
                {
                    // Agent appears registered, but let's force sync to ensure visibility
                    // This catches edge cases where IsInstanced=true but GPUI rendering failed
                    ForceGPUISync(agent, gpuiPrefab);
                    syncedCount++;
                }
            }
            
        }

        /// <summary>
        /// Spawns agents from a specific enemy crowd faction by index at a transform's position.
        /// </summary>
        public List<int> SpawnEnemyCrowd(int enemyCrowdIndex, Transform spawnTransform, int count, FormationType formation = FormationType.Random, float? formationSpacingOverride = null)
        {
            if (spawnTransform == null)
            {
                Debug.LogError("[EnemyMasses] Spawn transform is null!");
                return null;
            }

            return SpawnEnemyCrowd(enemyCrowdIndex, spawnTransform.position, spawnTransform.rotation, count, formation, formationSpacingOverride);
        }

        /// <summary>
        /// Spawns agents from a specific enemy crowd faction by name.
        /// </summary>
        public List<int> SpawnEnemyCrowdByName(string factionName, Vector3 spawnPosition, int count, FormationType formation = FormationType.Random, float? formationSpacingOverride = null)
        {
            int index = enemyCrowds.FindIndex(ec => ec.factionName == factionName);
            if (index < 0)
            {
                Debug.LogError($"[EnemyMasses] No enemy crowd found with faction name: {factionName}");
                return null;
            }

            return SpawnEnemyCrowd(index, spawnPosition, Quaternion.identity, count, formation, formationSpacingOverride);
        }

        /// <summary>
        /// Spawns agents from a specific enemy crowd faction by name at a transform's position.
        /// </summary>
        public List<int> SpawnEnemyCrowdByName(string factionName, Transform spawnTransform, int count, FormationType formation = FormationType.Random, float? formationSpacingOverride = null)
        {
            if (spawnTransform == null)
            {
                Debug.LogError("[EnemyMasses] Spawn transform is null!");
                return null;
            }

            return SpawnEnemyCrowdByName(factionName, spawnTransform.position, count, formation, formationSpacingOverride);
        }
        
        /// <summary>
        /// Spawns summoned agents with full network synchronization.
        /// Used by skill system for summon effects. Properly sets owner, summoner, and timed despawn.
        /// </summary>
        /// <param name="factionIndex">Index of the EnemyCrowd to spawn</param>
        /// <param name="spawnPosition">Center position for spawning</param>
        /// <param name="count">Number of agents to summon</param>
        /// <param name="summonerAgent">The agent performing the summon</param>
        /// <param name="duration">Duration before despawn (0 = permanent)</param>
        /// <param name="inheritTarget">Whether summons inherit summoner's target</param>
        /// <param name="spawnRadius">Radius around position to spawn in</param>
        /// <returns>List of spawned agent indices, or null on failure</returns>
        public List<int> SpawnSummonedAgents(
            int factionIndex, 
            Vector3 spawnPosition, 
            int count, 
            CrowdAgent summonerAgent, 
            float duration = 0f, 
            bool inheritTarget = true,
            float spawnRadius = 3f)
        {
            if (factionIndex < 0 || factionIndex >= enemyCrowds.Count)
            {
                Debug.LogError($"[EnemyMasses] Invalid faction index for summon: {factionIndex}");
                return null;
            }
            
            // In network mode, only server can summon
            if (NetworkAuthorityProvider.IsNetworkMode)
            {
                var spawnAuthority = NetworkAuthorityProvider.SpawnAuthority;
                if (spawnAuthority != null && !spawnAuthority.IsServer)
                {
                    // Client should not directly summon - request will be sent via skill execution
                    Debug.LogWarning("[EnemyMasses] Client attempted direct summon - should go through skill system");
                    return null;
                }
            }
            
            // Spawn using circle formation around position
            var spawnedIndices = SpawnEnemyCrowd(factionIndex, spawnPosition, Quaternion.identity, count, FormationType.Circle, spawnRadius);
            
            if (spawnedIndices == null || spawnedIndices.Count == 0)
                return null;
            
            // Get summoner info
            int summonerIndex = summonerAgent?.index ?? -1;
            uint summonerNetworkId = summonerAgent?.networkId ?? 0;
            int ownerPlayerId = summonerAgent?.ownerPlayerId ?? -1;
            int targetIndex = (summonerAgent != null && inheritTarget) ? summonerAgent.attackTargetAgentIndex : -1;
            float expireTime = duration > 0 ? Time.time + duration : 0f;
            
            // Configure each summoned agent
            foreach (int agentIndex in spawnedIndices)
            {
                var summon = GetAgent(agentIndex);
                if (summon == null) continue;
                
                // Mark as summoned
                summon.isSummoned = true;
                summon.summonerIndex = summonerIndex;
                summon.summonerNetworkId = summonerNetworkId;
                summon.summonExpireTime = expireTime;
                summon.summonInheritTarget = inheritTarget;
                
                // Set owner (authoritative since we're server or single-player)
                if (ownerPlayerId >= 0)
                {
                    summon.SetOwnerAuthoritative(ownerPlayerId);
                }
                
                // Inherit target
                if (targetIndex >= 0)
                {
                    summon.attackTargetAgentIndex = targetIndex;
                }
            }
            
            // Fire network event with owner info for synchronization
            FireNetworkSpawnEvent(spawnedIndices, factionIndex, spawnPosition, Quaternion.identity, FormationType.Circle, ownerPlayerId);
            
            // Schedule timed despawn if duration > 0
            if (duration > 0)
            {
                StartCoroutine(DespawnSummonsAfterDelay(spawnedIndices, duration));
            }
            
            return spawnedIndices;
        }
        
        /// <summary>
        /// Coroutine to despawn summoned agents after their duration expires.
        /// </summary>
        private System.Collections.IEnumerator DespawnSummonsAfterDelay(List<int> agentIndices, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            foreach (int index in agentIndices)
            {
                var agent = GetAgent(index);
                if (agent != null && !agent.isDead && agent.isSummoned)
                {
                    // Check if expire time has actually passed (in case duration was extended)
                    if (agent.summonExpireTime > 0 && Time.time >= agent.summonExpireTime)
                    {
                        KillAgent(index, useRagdoll: false);
                    }
                }
            }
        }
        
        /// <summary>
        /// Extends the duration of a summoned agent.
        /// </summary>
        /// <param name="agentIndex">Index of the summoned agent</param>
        /// <param name="additionalDuration">Additional time to add (seconds)</param>
        public void ExtendSummonDuration(int agentIndex, float additionalDuration)
        {
            var agent = GetAgent(agentIndex);
            if (agent == null || !agent.isSummoned) return;
            
            if (agent.summonExpireTime > 0)
            {
                agent.summonExpireTime += additionalDuration;
            }
        }
        
        /// <summary>
        /// Despawns all summons from a specific summoner.
        /// </summary>
        /// <param name="summonerIndex">Index of the summoner agent</param>
        public void DespawnAllSummons(int summonerIndex)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent != null && agent.isSummoned && agent.summonerIndex == summonerIndex && !agent.isDead)
                {
                    KillAgent(i, useRagdoll: false);
                }
            }
        }
        
        /// <summary>
        /// Fixes any invisible agents by re-registering them with GPU Instancer.
        /// Call this if agents appear invisible after spawning.
        /// </summary>
        /// <returns>Number of agents that were fixed.</returns>
        public int FixInvisibleAgents()
        {
            return FixInvisibleAgents(forceAllAgents: false);
        }
        
        /// <summary>
        /// Fixes any invisible agents by re-registering them with GPU Instancer.
        /// </summary>
        /// <param name="forceAllAgents">If true, force re-sync for ALL living agents, not just those detected as unregistered.</param>
        /// <returns>Number of agents that were fixed.</returns>
        public int FixInvisibleAgents(bool forceAllAgents)
        {
            int fixedCount = 0;
            int forceSyncCount = 0;
            
            foreach (var agent in _agents)
            {
                if (agent == null || agent.isDead || agent.prefabComponent == null)
                    continue;
                
                var gpuiPrefab = agent.prefabComponent;
                
                // Check if not properly instanced or has invalid buffer index
                bool notRegistered = !gpuiPrefab.IsInstanced || gpuiPrefab.bufferIndex < 0 || gpuiPrefab.renderKey == 0;
                
                if (notRegistered)
                {
                    // Clear any stale state before re-registering
                    // This handles the case where IsInstanced was true but rendering failed
                    ForceGPUIReRegistration(agent, gpuiPrefab);
                    fixedCount++;
                }
                else if (forceAllAgents)
                {
                    // Agent appears registered, but force a sync anyway
                    // This catches cases where IsInstanced=true but GPUI isn't actually rendering
                    ForceGPUISync(agent, gpuiPrefab);
                    forceSyncCount++;
                }
            }
            
            if (fixedCount > 0 || forceSyncCount > 0)
            {
                LogDebug($"[EnemyMasses] FixInvisibleAgents: Re-registered {fixedCount} agents, force-synced {forceSyncCount} agents.");
            }
            
            return fixedCount + forceSyncCount;
        }
        
        /// <summary>
        /// Forces complete re-registration of an agent with GPUI.
        /// Used when an agent is detected as unregistered.
        /// </summary>
        private void ForceGPUIReRegistration(CrowdAgent agent, GPUIPrefab gpuiPrefab)
        {
            // Try to re-register — but only if a valid prototype exists.
            // Non-humanoid agents (vehicles/drones) may not have a registered prototype
            // and calling AddPrefabInstance without one causes "Unknown prefab ID" errors.
            if (gpuiPrefab.registeredManager != null)
            {
                int protoIndex = gpuiPrefab.registeredManager.GetPrototypeIndex(gpuiPrefab);
                if (protoIndex >= 0)
                {
                    GPUIPrefabAPI.AddPrefabInstanceImmediate(gpuiPrefab.registeredManager, gpuiPrefab, protoIndex);
                }
                // else: no prototype registered — skip to avoid "Unknown prefab ID" error
            }
            // else: no registered manager — skip to avoid error spam
            
            // Force transform sync
            ForceGPUISync(agent, gpuiPrefab);
        }
        
        /// <summary>
        /// Forces GPUI to sync transform data for an agent.
        /// Used when an agent might be invisible despite appearing registered.
        /// </summary>
        private void ForceGPUISync(CrowdAgent agent, GPUIPrefab gpuiPrefab)
        {
            if (agent.crowdInstance != null)
            {
                // Mark transform as changed to force update
                agent.crowdInstance.transform.hasChanged = true;
                
                // Force GPUI to update transform data
                if (gpuiPrefab.registeredManager != null)
                {
                    gpuiPrefab.registeredManager.UpdateTransformData(gpuiPrefab);
                    
                    // Also force the manager to update all modified matrices
                    gpuiPrefab.registeredManager.RequireTransformUpdate();
                }
            }
        }
        
        /// <summary>
        /// Emergency fix: Forces all agent mesh renderers to be enabled.
        /// Use this as a last resort if GPUI is completely broken.
        /// WARNING: This will significantly impact performance with many agents.
        /// </summary>
        public int ForceEnableAllRenderers()
        {
            int enabledCount = 0;
            
            foreach (var agent in _agents)
            {
                if (agent == null || agent.isDead || agent.crowdInstance == null)
                    continue;
                
                // Get all renderers on the agent and enable them
                var renderers = agent.crowdInstance.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (!renderer.enabled)
                    {
                        renderer.enabled = true;
                        enabledCount++;
                    }
                }
            }
            
            if (enabledCount > 0)
            {
                Debug.LogWarning($"[EnemyMasses] ForceEnableAllRenderers: Enabled {enabledCount} renderers. " +
                               "This may impact performance. Use only as emergency fallback.");
            }
            
            return enabledCount;
        }

        #endregion

        #region Public API - Combat & Damage

        /// <summary>
        /// Assigns a target agent for the specified attacker agent to pursue and attack.
        /// </summary>
        public bool SetAgentTarget(int attackerAgentIndex, int targetAgentIndex)
        {
            if (attackerAgentIndex < 0 || attackerAgentIndex >= _agents.Count)
            {
                Debug.LogError($"[EnemyMasses] Invalid attacker agent index: {attackerAgentIndex}. Valid range: 0-{_agents.Count - 1}");
                return false;
            }

            if (targetAgentIndex < 0 || targetAgentIndex >= _agents.Count)
            {
                Debug.LogError($"[EnemyMasses] Invalid target agent index: {targetAgentIndex}. Valid range: 0-{_agents.Count - 1}");
                return false;
            }

            CrowdAgent attacker = _agents[attackerAgentIndex];
            CrowdAgent target = _agents[targetAgentIndex];

            if (attacker == null || target == null)
                return false;

            if (attacker.isDead)
            {
                Debug.LogWarning($"[EnemyMasses] Cannot assign target to dead agent {attackerAgentIndex}");
                return false;
            }

            if (target.isDead)
            {
                Debug.LogWarning($"[EnemyMasses] Cannot assign dead agent {targetAgentIndex} as target");
                return false;
            }

            attacker.attackTargetAgentIndex = targetAgentIndex;
            return true;
        }

        /// <summary>
        /// Overrides Hybrid combat mode for all agents in a faction (force melee, force ranged, or reset to default).
        /// Only applicable when the faction's Combat Range Type is Hybrid.
        /// </summary>
        /// <param name="factionIndex">Index of the faction in the enemyCrowds list</param>
        /// <param name="mode">Override mode to apply</param>
        /// <returns>Number of agents updated</returns>
        public int SetCombatRangeOverrideForFaction(int factionIndex, CombatRangeOverrideMode mode)
        {
            var faction = GetEnemyCrowdAtIndex(factionIndex);
            if (faction == null)
            {
                Debug.LogWarning($"[EnemyMasses] Faction index {factionIndex} not found!");
                return 0;
            }

            if (!CanApplyCombatRangeOverride(faction, mode, factionIndex))
                return 0;

            if (mode == CombatRangeOverrideMode.Default)
                _factionCombatRangeOverrides.Remove(factionIndex);
            else
                _factionCombatRangeOverrides[factionIndex] = mode;

            int updated = 0;
            foreach (var agent in _agents)
            {
                if (agent == null || agent.enemyCrowdIndex != factionIndex)
                    continue;

                ApplyFactionCombatRangeOverride(agent);
                updated++;
            }

            return updated;
        }

        /// <summary>
        /// Overrides Hybrid combat mode for all agents in a faction by name.
        /// </summary>
        /// <param name="factionName">Name of the faction</param>
        /// <param name="mode">Override mode to apply</param>
        /// <returns>Number of agents updated</returns>
        public int SetCombatRangeOverrideForFaction(string factionName, CombatRangeOverrideMode mode)
        {
            int factionIndex = -1;
            for (int i = 0; i < enemyCrowds.Count; i++)
            {
                if (enemyCrowds[i] != null && enemyCrowds[i].factionName == factionName)
                {
                    factionIndex = i;
                    break;
                }
            }

            if (factionIndex == -1)
            {
                Debug.LogWarning($"[EnemyMasses] Faction '{factionName}' not found!");
                return 0;
            }

            return SetCombatRangeOverrideForFaction(factionIndex, mode);
        }

        /// <summary>
        /// Overrides Hybrid combat mode for a single agent.
        /// </summary>
        /// <param name="agentIndex">Index of the agent</param>
        /// <param name="mode">Override mode to apply</param>
        /// <returns>True if the agent was updated</returns>
        public bool SetCombatRangeOverrideForAgent(int agentIndex, CombatRangeOverrideMode mode)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
            {
                Debug.LogWarning($"[EnemyMasses] Agent index {agentIndex} out of range (0-{_agents.Count - 1})!");
                return false;
            }

            var agent = _agents[agentIndex];
            if (agent == null || agent.enemyCrowd == null)
                return false;

            if (mode != CombatRangeOverrideMode.Default &&
                !CanApplyCombatRangeOverride(agent.enemyCrowd, mode, agent.enemyCrowdIndex))
            {
                return false;
            }

            if (mode == CombatRangeOverrideMode.Default)
            {
                ApplyFactionCombatRangeOverride(agent);
            }
            else
            {
                ApplyCombatRangeOverrideToAgent(agent, mode);
            }

            return true;
        }

        /// <summary>
        /// Overrides Hybrid combat mode for a list of agents (for RTS selections).
        /// </summary>
        /// <param name="agentIndices">Agent indices to update</param>
        /// <param name="mode">Override mode to apply</param>
        /// <returns>Number of agents updated</returns>
        public int SetCombatRangeOverrideForAgents(IReadOnlyList<int> agentIndices, CombatRangeOverrideMode mode)
        {
            if (agentIndices == null || agentIndices.Count == 0)
                return 0;

            int updated = 0;
            var warnedFactions = new HashSet<int>();

            for (int i = 0; i < agentIndices.Count; i++)
            {
                int agentIndex = agentIndices[i];
                if (agentIndex < 0 || agentIndex >= _agents.Count)
                    continue;

                var agent = _agents[agentIndex];
                if (agent == null || agent.enemyCrowd == null)
                    continue;

                if (mode != CombatRangeOverrideMode.Default &&
                    !IsCombatRangeOverrideCompatible(agent.enemyCrowd, mode))
                {
                    if (!warnedFactions.Contains(agent.enemyCrowdIndex))
                    {
                        CanApplyCombatRangeOverride(agent.enemyCrowd, mode, agent.enemyCrowdIndex);
                        warnedFactions.Add(agent.enemyCrowdIndex);
                    }
                    continue;
                }

                if (mode == CombatRangeOverrideMode.Default)
                {
                    ApplyFactionCombatRangeOverride(agent);
                }
                else
                {
                    ApplyCombatRangeOverrideToAgent(agent, mode);
                }
                updated++;
            }

            return updated;
        }

        /// <summary>
        /// Overrides Hybrid combat mode for all agents matching an EnemyCrowd type name.
        /// </summary>
        /// <param name="enemyTypeName">EnemyCrowd enemyTypeName to match</param>
        /// <param name="mode">Override mode to apply</param>
        /// <returns>Number of agents updated</returns>
        public int SetCombatRangeOverrideForType(string enemyTypeName, CombatRangeOverrideMode mode)
        {
            if (string.IsNullOrWhiteSpace(enemyTypeName))
            {
                Debug.LogWarning("[EnemyMasses] Enemy type name is empty.");
                return 0;
            }

            string trimmedType = enemyTypeName.Trim();

            if (mode == CombatRangeOverrideMode.Default)
                _typeCombatRangeOverrides.Remove(trimmedType);
            else
                _typeCombatRangeOverrides[trimmedType] = mode;

            int updated = 0;
            var warnedFactions = new HashSet<int>();

            foreach (var agent in _agents)
            {
                if (agent == null || agent.enemyCrowd == null)
                    continue;

                string agentType = agent.enemyCrowd.enemyTypeName;
                if (string.IsNullOrWhiteSpace(agentType) || !string.Equals(agentType.Trim(), trimmedType))
                    continue;

                if (mode != CombatRangeOverrideMode.Default &&
                    !IsCombatRangeOverrideCompatible(agent.enemyCrowd, mode))
                {
                    if (!warnedFactions.Contains(agent.enemyCrowdIndex))
                    {
                        CanApplyCombatRangeOverride(agent.enemyCrowd, mode, agent.enemyCrowdIndex);
                        warnedFactions.Add(agent.enemyCrowdIndex);
                    }
                    continue;
                }

                if (mode == CombatRangeOverrideMode.Default)
                {
                    ApplyFactionCombatRangeOverride(agent);
                }
                else
                {
                    ApplyCombatRangeOverrideToAgent(agent, mode);
                }
                updated++;
            }

            return updated;
        }

        /// <summary>
        /// Calculates the final damage after applying all multipliers (global, faction, per-agent).
        /// </summary>
        private float CalculateFinalDamage(int agentIndex, float baseDamage, bool isPlayerAttack)
        {
            float finalDamage = baseDamage;

            // Apply player-only multiplier when damage is sourced from Player Target (non-agent)
            if (isPlayerAttack)
            {
                finalDamage *= playerDamageMultiplier;
            }

            finalDamage *= incomingDamageMultiplier;
            
            if (agentIndex >= 0 && agentIndex < _agents.Count)
            {
                CrowdAgent agent = _agents[agentIndex];
                if (agent != null)
                {
                    // Apply faction multiplier
                    if (agent.enemyCrowd != null)
                    {
                        finalDamage *= agent.enemyCrowd.incomingDamageMultiplier;
                    }
                    
                    // Apply per-agent multiplier
                    finalDamage *= agent.incomingDamageMultiplier;
                }
            }
            
            return finalDamage;
        }

        /// <summary>
        /// Applies damage to an agent by index. 
        /// Damage is multiplied by global, faction, and per-agent IncomingDamageMultipliers.
        /// </summary>
        public bool TryApplyDamage(int agentIndex, float damageAmount, bool allowBlock = true, bool forceKill = false,
            Vector3? attackerForward = null, Vector3? attackerUp = null, Vector3? attackerPosition = null, int attackerAgentIndex = -1, bool isPlayerAttack = false)
        {
            float finalDamage = CalculateFinalDamage(agentIndex, damageAmount, isPlayerAttack);
            return _combatSystem.TryApplyDamage(agentIndex, finalDamage, allowBlock, forceKill, attackerForward, attackerUp, attackerPosition, attackerAgentIndex);
        }
        
        /// <summary>
        /// Applies damage to an agent by index with explicit hit point for zone-based damage.
        /// Use this when you have a specific hit position (e.g., weapon strike position) to enable
        /// the Virtual Hitbox system to calculate zone-based damage multipliers (headshots, etc.)
        /// Damage is multiplied by global, faction, and per-agent IncomingDamageMultipliers.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to damage</param>
        /// <param name="damageAmount">Base damage amount</param>
        /// <param name="hitPoint">World position where the hit occurred (used for hitbox zone detection)</param>
        /// <param name="allowBlock">Whether the agent can block this damage</param>
        /// <param name="attackerForward">Optional direction the attacker is facing</param>
        /// <returns>True if the agent was killed, false otherwise</returns>
        public bool TryApplyDamageAtPoint(int agentIndex, float damageAmount, Vector3 hitPoint, bool allowBlock = true,
            Vector3? attackerForward = null, bool isPlayerAttack = false, bool forceKill = false)
        {
            float finalDamage = CalculateFinalDamage(agentIndex, damageAmount, isPlayerAttack);
            return _combatSystem.TryApplyDamageAtPoint(agentIndex, finalDamage, hitPoint, allowBlock, attackerForward, forceKill);
        }

        /// <summary>
        /// Applies damage to an agent via GPUICrowdInstance. 
        /// Damage is multiplied by global, faction, and per-agent IncomingDamageMultipliers.
        /// </summary>
        public bool TryApplyDamage(GPUICrowdInstance instance, float damageAmount, bool allowBlock = true, bool forceKill = false,
            Vector3? attackerForward = null, Vector3? attackerUp = null, Vector3? attackerPosition = null, int attackerAgentIndex = -1, bool isPlayerAttack = false)
        {
            int agentIndex = GetAgentIndexFromInstance(instance);
            float finalDamage = CalculateFinalDamage(agentIndex, damageAmount, isPlayerAttack);
            return _combatSystem.TryApplyDamage(instance, finalDamage, allowBlock, forceKill, attackerForward, attackerUp,
                attackerPosition, GetAgentIndexFromInstance, attackerAgentIndex);
        }

        /// <summary>
        /// Immediately kills an agent by index.
        /// </summary>
        public bool KillAgent(int agentIndex, bool useRagdoll = true)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;

            return TryApplyDamage(agentIndex, 99999f, false, true);
        }

        /// <summary>
        /// Gets the remaining cooldown for a specific skill slot on an agent.
        /// Used by AgentSkillBarUI for cooldown display.
        /// </summary>
        public float GetSkillCooldownRemaining(CrowdAgent agent, int skillSlotIndex)
        {
            return _combatSystem?.GetSkillCooldownRemaining(agent, skillSlotIndex) ?? 0f;
        }

        /// <summary>
        /// Immediately kills an agent by GPUICrowdInstance.
        /// </summary>
        public bool KillAgent(GPUICrowdInstance instance, bool useRagdoll = true)
        {
            return TryApplyDamage(instance, 99999f, false, true);
        }

        /// <summary>
        /// Respawns a dead/disabled agent at a new position.
        /// Resets all death-related state including sinking, ragdoll, and combat state.
        /// Use this to re-enable pooled agents instead of spawning new ones.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to respawn</param>
        /// <param name="spawnPosition">World position to respawn at</param>
        /// <param name="spawnRotation">Rotation for the respawned agent</param>
        /// <returns>True if respawn succeeded</returns>
        public bool RespawnAgent(int agentIndex, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
            {
                Debug.LogError($"[EnemyMasses] Invalid agent index for respawn: {agentIndex}");
                return false;
            }

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null)
            {
                Debug.LogError($"[EnemyMasses] Agent at index {agentIndex} is null");
                return false;
            }

            // Get the GameObject
            GameObject agentObject = null;
            if (agent.crowdInstance != null)
                agentObject = agent.crowdInstance.gameObject;
            else if (agent.navigationAgent != null && agent.navigationAgent.Transform != null)
                agentObject = agent.navigationAgent.Transform.gameObject;
            else if (agent.transform != null)
                agentObject = agent.transform.gameObject;

            if (agentObject == null)
            {
                Debug.LogError($"[EnemyMasses] Agent {agentIndex} has no GameObject to respawn");
                return false;
            }

            // === RESET DEATH SINKING STATE ===
            agent.isSinking = false;
            agent.sinkStartTime = 0f;
            agent.sinkStartPosition = Vector3.zero;
            agent.sinkTargetPosition = Vector3.zero;
            agent.ragdollSinkPending = false;
            agent.ragdollSinkScheduleTime = 0f;

            // === SET NEW POSITION ===
            agentObject.transform.position = spawnPosition;
            agentObject.transform.rotation = spawnRotation;

            // Update GPU Instancer crowd instance transform data
            if (agent.crowdInstance != null)
            {
                agent.crowdInstance.transform.position = spawnPosition;
                agent.crowdInstance.transform.rotation = spawnRotation;
                agent.crowdInstance.transform.hasChanged = true;
            }

            // Also update via prefab component if available
            if (agent.prefabComponent != null && agent.prefabComponent.registeredManager != null)
            {
                agent.prefabComponent.registeredManager.UpdateTransformData(agent.prefabComponent);
            }

            // Warp navigation agent to new position
            if (agent.navigationAgent != null)
            {
                // CRITICAL: Reset updatePosition/updateRotation in case agent died while traversing a link
                bool supportsNavmeshOffsetImpulse = agent.navigationAgent.Capabilities.SupportsNavmeshOffsetImpulse;
                bool controllerDrivesTransformSync = supportsNavmeshOffsetImpulse ||
                                                     agent.navigationAgent.NavigationType == NavigationType.FlowField;
                agent.navigationAgent.UpdatePosition = !controllerDrivesTransformSync;
                agent.navigationAgent.UpdateRotation = !controllerDrivesTransformSync;
                
                agent.navigationAgent.Warp(spawnPosition);
                agent.navigationAgent.Position = spawnPosition;
                agent.navigationAgent.ResetPath();
                agent.navigationAgent.IsStopped = false;
                agent.navigationAgent.Enabled = true;
            }

            // === RE-ENABLE GAMEOBJECT ===
            if (!agentObject.activeSelf)
            {
                agentObject.SetActive(true);
            }

            // === RESET RAGDOLL STATE ===
            if (agent.ragdollActivated || agent.ragdollPending)
            {
                DeactivateRagdoll(agent, true);
            }
            agent.ragdollActivated = false;
            agent.ragdollPending = false;
            agent.ragdollActivationTime = 0f;
            agent.hasPendingRagdollImpulse = false;
            agent.pendingRagdollImpulse = Vector3.zero;
            agent.hasAttackerOrientation = false;
            agent.ragdollSinkPending = false;
            agent.ragdollSinkScheduleTime = 0f;

            // === RESET DEATH STATE ===
            agent.isDead = false;
            agent.deathTime = 0f;
            agent.deathAnimationCompleted = false;
            agent.pendingCleanup = false;

            // === RESET COMBAT STATE ===
            _combatSystem.InitializeCombatAgent(agent, true);
            ApplyFactionCombatRangeOverride(agent);

            // === RESET ANIMATION STATE ===
            agent.currentState = CrowdAnimationState.Idle;
            agent.cachedBlendWeight = -1f;
            agent.lastIdleAnimationHash = -1;
            agent.smoothedSpeed = 0f;

            // Re-enable animator if needed
            if (agent.mecanimAnimator != null)
            {
                if (!agent.mecanimAnimator.enabled && agent.animatorWasEnabled)
                {
                    agent.mecanimAnimator.enabled = true;
                }
                agent.mecanimAnimator.speed = agent.animatorPreviousSpeed > 0 ? agent.animatorPreviousSpeed : 1f;
                agent.mecanimAnimator.SetFloat("Speed", 0f);
            }

            // === RESET NAVIGATION STATE ===
            agent.navigationState = AgentNavigationState.Formation;
            agent.hasArrived = false;
            agent.isRetreating = false;
            agent.lastDestination = spawnPosition;
            agent.lastProgressTime = Time.time;
            agent.lastKnownDistance = float.PositiveInfinity;
            agent.remainingRetries = 3;

            // === RE-REGISTER WITH GPU INSTANCER ===
            // Always attempt to re-register - the prefab might exist but be unregistered after death cleanup
            if (agent.crowdInstance != null)
            {
                GPUIPrefab gpuiPrefab = agent.prefabComponent;
                if (gpuiPrefab == null && agent.crowdInstance.TryGetComponent(out GPUIPrefab foundPrefab))
                {
                    gpuiPrefab = foundPrefab;
                    agent.prefabComponent = gpuiPrefab;
                }
                
                if (gpuiPrefab != null)
                {
                    // Check if NOT instanced (was removed on death) - need to re-add
                    if (!gpuiPrefab.IsInstanced)
                    {
                        // Use immediate registration to prevent invisible agents
                        if (gpuiPrefab.registeredManager != null)
                        {
                            int protoIndex = gpuiPrefab.registeredManager.GetPrototypeIndex(gpuiPrefab);
                            if (protoIndex >= 0)
                            {
                                GPUIPrefabAPI.AddPrefabInstanceImmediate(gpuiPrefab.registeredManager, gpuiPrefab, protoIndex);
                            }
                            else
                            {
                                EnemyMassesGPUIHelper.SafeAddPrefabInstance(gpuiPrefab);
                            }
                        }
                        else
                        {
                            EnemyMassesGPUIHelper.SafeAddPrefabInstance(gpuiPrefab);
                        }
                        
                        // Verify registration - if still not instanced, force retry
                        if (!gpuiPrefab.IsInstanced)
                        {
                            Debug.LogWarning($"[Respawn] GPUI re-registration failed for agent {agent.index}, forcing retry...");
                            GPUIPrefabAPI.AddPrefabInstance(gpuiPrefab);
                        }
                    }
                    
                    // Force transform update to sync GPU position
                    agent.crowdInstance.transform.hasChanged = true;
                    if (gpuiPrefab.registeredManager != null)
                    {
                        gpuiPrefab.registeredManager.UpdateTransformData(gpuiPrefab);
                    }
                }
            }

            // === RESET PERFORMANCE MANAGER STATE ===
            _performanceManager.RemoveFromDeadAgents(agent);

            // === REMOVE FROM SINKING LIST ===
            _combatSystem.RemoveFromSinkingAgents(agent);

            // === PLAY IDLE ANIMATION ===
            if (agent.crowdInstance != null)
            {
                EnemyCrowd faction = agent.enemyCrowd;
                AnimationClip idleClip = faction?.idleClipOverride ?? _defaultAnimationSettings.idleClip;
                if (idleClip != null)
                {
                    // CRITICAL: Check workflow type - only ComputeAnimator supports GPU animation calls
                    // BoneTracker and MecanimReader use external Unity Animator components
                    var workflow = agent.crowdInstance.AnimatorWorkflow;
                    bool workflowSupportsGpuAnimations = workflow != null && 
                        workflow.GetID() != GPUIAWBoneTracker.WORKFLOW_ID && 
                        workflow.GetID() != GPUIAWMecanimReader.WORKFLOW_ID;
                    
                    if (workflowSupportsGpuAnimations)
                    {
                        int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                        if (agent.currentWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                        {
                            // Try to realign the instance workflow to ComputeAnimator
                            if (agent.crowdInstance != null)
                            {
                                agent.crowdInstance.AnimatorWorkflowID = GPUIAWComputeAnimator.WORKFLOW_ID;
                                actualWorkflowId = agent.crowdInstance.AnimatorWorkflowID;
                            }

                            if (agent.currentWorkflow != HybridWorkflowType.ComputeAnimator || actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                            {
                                Debug.LogError($"[EnemyMasses] GPU respawn idle blocked: agent={agent.index} currentWorkflow={agent.currentWorkflow} actualWorkflowId={actualWorkflowId}");
                                workflowSupportsGpuAnimations = false;
                            }
                        }
                    }

                    if (workflowSupportsGpuAnimations)
                    {
                        // Validate buffer bounds before calling GPU animation API
                        var prefab = agent.crowdInstance.PrefabComponent;
                        if (prefab != null && prefab.IsInstanced && prefab.renderKey != 0 && 
                            prefab.bufferIndex >= 0 && GPUIRenderingSystem.IsActive &&
                            GPUIRenderingSystem.TryGetRenderSource(prefab.renderKey, out var rs) &&
                            prefab.bufferIndex < rs.bufferSize)
                        {
                            // Use transition time 0 and start from clip beginning to immediately override
                            // any stale animation data in the GPU buffer from previous buffer occupant
                            agent.crowdInstance.StartAnimation(idleClip, 0f, 1f, 0f, true, false);
                        }
                        else
                        {
                            // Instance not yet ready - StartAnimation will internally queue via _OnProcessed
                            // Use normalizedClipTime=0 and transitionTime=0 to snap to idle immediately when ready
                            agent.crowdInstance.StartAnimation(idleClip, 0f, 1f, 0f, true, false);
                        }
                    }
                    else if (agent.mecanimAnimator != null)
                    {
                        // For BoneTracker/MecanimReader, use Mecanim Animator
                        agent.mecanimAnimator.SetFloat("Speed", 0f);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Respawns a dead/disabled agent by GPUICrowdInstance reference.
        /// </summary>
        public bool RespawnAgent(GPUICrowdInstance instance, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            int agentIndex = GetAgentIndexFromInstance(instance);
            if (agentIndex < 0)
            {
                Debug.LogError("[EnemyMasses] Cannot find agent for the given GPUICrowdInstance");
                return false;
            }
            return RespawnAgent(agentIndex, spawnPosition, spawnRotation);
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        // FLIGHT MODE API
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Puts an agent into flight mode — plays take-off animation (if configured) then loops the flight clip.
        /// For ComputeAnimator: uses <see cref="EnemyCrowd.flightTakeOffClipOverride"/> and <see cref="EnemyCrowd.flightLoopClipOverride"/>.
        /// For Mecanim/BoneTracker: sets <see cref="EnemyCrowd.flightBoolParameterOverride"/> to true
        /// and fires <see cref="EnemyCrowd.flightTakeOffTriggerParameterOverride"/> if configured.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to put into flight mode.</param>
        /// <param name="height">Flight altitude override. 0 = use EnemyCrowd.flightHeight default.</param>
        /// <returns>True if flight mode was successfully initiated.</returns>
        public bool SetAgentFlying(int agentIndex, float height = 0f)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null || agent.isDead || agent.isFlying)
                return false;

            EnemyCrowd crowd = agent.enemyCrowd;
            if (crowd == null)
                return false;

            agent.isFlying = true;

            // Resolve flight height: explicit override > crowd default
            float effectiveHeight = height > 0f ? height : crowd.flightHeight;
            agent.flightTargetHeight = effectiveHeight;

            bool prefersMecanim = IsMecanimPreferredForAgent(agent, crowd);

            // ── Mecanim / BoneTracker path ──
            if (prefersMecanim)
            {
                Animator animator = agent.mecanimAnimator;
                if (animator != null)
                {
                    // Set bool parameter
                    if (!string.IsNullOrWhiteSpace(crowd.flightBoolParameterOverride))
                    {
                        TrySetAnimatorBool(animator, crowd.flightBoolParameterOverride, true);
                    }
                    // Fire take-off trigger
                    if (!string.IsNullOrWhiteSpace(crowd.flightTakeOffTriggerParameterOverride))
                    {
                        TrySetAnimatorTrigger(animator, crowd.flightTakeOffTriggerParameterOverride);
                    }
                }

                agent.currentState = CrowdAnimationState.Flying;
                agent.cachedBlendWeight = -1f;
                return true;
            }

            // ── ComputeAnimator path ──
            AnimationClip takeOffClip = crowd.flightTakeOffClipOverride;
            AnimationClip idleClip = crowd.flightIdleClipOverride;
            AnimationClip loopClip = crowd.flightLoopClipOverride;

            if (takeOffClip == null && loopClip == null && idleClip == null)
            {
                // No clips configured — just set the flag
                agent.currentState = CrowdAnimationState.Flying;
                agent.cachedBlendWeight = -1f;
                return true;
            }

            if (takeOffClip != null)
            {
                // Play take-off, schedule idle/loop after it finishes
                float groundToFlightTransition = crowd.flightGroundToTakeOffTransitionTime;
                bool playedTakeOff = TryPlayFlightClip(agent, takeOffClip, false, groundToFlightTransition);
                if (playedTakeOff)
                {
                    agent.currentState = CrowdAnimationState.FlightTakeOff;
                    agent.flightTakeOffEndTime = Time.time + Mathf.Max(0.01f, takeOffClip.length - 0.03f);
                }
                else
                {
                    agent.currentState = CrowdAnimationState.Flying;
                }
            }
            else
            {
                // No take-off clip — go straight to idle or loop
                AnimationClip initialClip = idleClip != null ? idleClip : loopClip;
                if (initialClip != null)
                {
                    TryPlayFlightClip(agent, initialClip, true, crowd.flightGroundToTakeOffTransitionTime);
                }
                agent.currentState = CrowdAnimationState.Flying;
            }

            agent.cachedBlendWeight = -1f;
            return true;
        }

        /// <summary>
        /// Lands an agent from flight mode — plays landing animation (if configured) then returns to idle/locomotion.
        /// For ComputeAnimator: plays <see cref="EnemyCrowd.flightLandClipOverride"/> then restores normal animation.
        /// For Mecanim/BoneTracker: sets <see cref="EnemyCrowd.flightBoolParameterOverride"/> to false
        /// and fires <see cref="EnemyCrowd.flightLandTriggerParameterOverride"/> if configured.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to land.</param>
        /// <returns>True if landing was successfully initiated.</returns>
        public bool SetAgentLanded(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null || !agent.isFlying)
                return false;

            EnemyCrowd crowd = agent.enemyCrowd;
            if (crowd == null)
                return false;
            bool prefersMecanim = IsMecanimPreferredForAgent(agent, crowd);

            // ── Mecanim / BoneTracker path ──
            if (prefersMecanim)
            {
                Animator animator = agent.mecanimAnimator;
                if (animator != null)
                {
                    // Clear bool parameter
                    if (!string.IsNullOrWhiteSpace(crowd.flightBoolParameterOverride))
                    {
                        TrySetAnimatorBool(animator, crowd.flightBoolParameterOverride, false);
                    }
                    // Fire landing trigger
                    if (!string.IsNullOrWhiteSpace(crowd.flightLandTriggerParameterOverride))
                    {
                        TrySetAnimatorTrigger(animator, crowd.flightLandTriggerParameterOverride);
                    }
                }

                agent.isFlying = false;
                agent.flightTargetHeight = 0f;
                agent.currentState = CrowdAnimationState.Idle;
                agent.cachedBlendWeight = -1f;
                RestoreGroundAnimation(agent, crowd.flightLandToGroundTransitionTime);
                return true;
            }

            // ── ComputeAnimator path ──
            AnimationClip landClip = crowd.flightLandClipOverride;

            if (landClip != null)
            {
                bool playedLand = TryPlayFlightClip(agent, landClip, false);
                if (playedLand)
                {
                    agent.currentState = CrowdAnimationState.FlightLanding;
                    // Allow extra blend tail: landing clip plays, then a smooth crossfade
                    // into ground locomotion follows (handled in UpdateFlightAnimations).
                    agent.flightLandEndTime = Time.time + Mathf.Max(0.01f, landClip.length - 0.03f);
                    agent.flightTargetHeight = 0f;
                }
                else
                {
                    agent.isFlying = false;
                    agent.flightTargetHeight = 0f;
                    agent.currentState = CrowdAnimationState.Idle;
                    RestoreGroundAnimation(agent, crowd.flightLandToGroundTransitionTime);
                }
            }
            else
            {
                agent.isFlying = false;
                agent.flightTargetHeight = 0f;
                agent.currentState = CrowdAnimationState.Idle;
                RestoreGroundAnimation(agent, crowd.flightLandToGroundTransitionTime);
            }

            agent.cachedBlendWeight = -1f;
            return true;
        }

        /// <summary>
        /// Sets flight mode for all alive agents in the specified crowd.
        /// </summary>
        /// <param name="enemyCrowdIndex">Index of the EnemyCrowd to affect.</param>
        /// <param name="fly">True to enter flight mode, false to land.</param>
        /// <param name="height">Flight altitude override. 0 = use EnemyCrowd.flightHeight default.</param>
        /// <returns>Number of agents that were successfully toggled.</returns>
        public int SetCrowdFlying(int enemyCrowdIndex, bool fly, float height = 0f)
        {
            int count = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead || agent.enemyCrowdIndex != enemyCrowdIndex)
                    continue;

                bool success = fly ? SetAgentFlying(i, height) : SetAgentLanded(i);
                if (success) count++;
            }
            return count;
        }

        /// <summary>
        /// Updates flight animation transitions for agents in take-off or landing phases.
        /// Should be called once per frame from the main update loop.
        /// </summary>
        internal void UpdateFlightAnimations()
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead)
                    continue;

                // Take-off → flying transition
                if (agent.currentState == CrowdAnimationState.FlightTakeOff && Time.time >= agent.flightTakeOffEndTime)
                {
                    EnemyCrowd crowd = agent.enemyCrowd;
                    if (crowd != null)
                    {
                        bool prefersMecanim = IsMecanimPreferredForAgent(agent, crowd);
                        float transition = crowd.flightTransitionTime;
                        if (!prefersMecanim)
                        {
                            // Pick idle or locomotion clip based on current speed
                            AnimationClip idleClip = crowd.flightIdleClipOverride;
                            AnimationClip loopClip = crowd.flightLoopClipOverride;
                            bool isMoving = agent.smoothedSpeed > 0.05f;
                            AnimationClip clip = isMoving ? (loopClip ?? idleClip) : (idleClip ?? loopClip);
                            if (clip != null)
                            {
                                TryPlayFlightClip(agent, clip, true, transition);
                            }
                        }
                        else
                        {
                            // Mecanim: CrossFade to flight idle state if configured
                            if (!string.IsNullOrWhiteSpace(crowd.flightIdleStateNameOverride))
                            {
                                Animator animator = agent.mecanimAnimator;
                                if (animator != null)
                                {
                                    int stateHash = Animator.StringToHash(crowd.flightIdleStateNameOverride);
                                    if (stateHash != 0 && animator.HasState(0, stateHash))
                                    {
                                        animator.CrossFade(stateHash, transition, 0, 0f);
                                    }
                                }
                            }
                        }
                    }
                    agent.currentState = CrowdAnimationState.Flying;
                }

                // Flying state: switch between idle and locomotion clips based on movement
                if (agent.currentState == CrowdAnimationState.Flying && agent.isFlying)
                {
                    EnemyCrowd crowd = agent.enemyCrowd;
                    if (crowd == null) continue;

                    bool prefersMecanim = IsMecanimPreferredForAgent(agent, crowd);
                    if (prefersMecanim) continue; // Mecanim handles blending via parameters

                    AnimationClip idleClip = crowd.flightIdleClipOverride;
                    AnimationClip loopClip = crowd.flightLoopClipOverride;
                    if (idleClip == null || loopClip == null) continue; // Need both to switch

                    bool isMoving = agent.smoothedSpeed > 0.05f;
                    bool wantsIdle = !isMoving;

                    // Use cachedBlendWeight to track current flight sub-state:
                    // -1 = uninitialized, 0 = idle, 1 = locomotion
                    float desiredWeight = wantsIdle ? 0f : 1f;
                    if (Mathf.Approximately(agent.cachedBlendWeight, desiredWeight))
                        continue;

                    AnimationClip targetClip = wantsIdle ? idleClip : loopClip;
                    TryPlayFlightClip(agent, targetClip, true); // Uses crowd.flightTransitionTime via default
                    agent.cachedBlendWeight = desiredWeight;
                }

                // Landing → idle transition: smooth crossfade into ground locomotion
                if (agent.currentState == CrowdAnimationState.FlightLanding && Time.time >= agent.flightLandEndTime)
                {
                    EnemyCrowd crowd = agent.enemyCrowd;
                    float landTransition = crowd != null ? crowd.flightLandToGroundTransitionTime : 0.45f;

                    agent.isFlying = false;
                    agent.currentState = CrowdAnimationState.Idle;
                    agent.cachedBlendWeight = -1f;
                    RestoreGroundAnimation(agent, landTransition);
                }
            }
        }

        /// <summary>
        /// Attempts to play a flight animation clip on the given agent.
        /// Uses GPU crowd animation for ComputeAnimator, Mecanim CrossFade for others.
        /// </summary>
        private bool TryPlayFlightClip(CrowdAgent agent, AnimationClip clip, bool loop, float transitionTime = -1f)
        {
            if (agent == null || clip == null)
                return false;

            // Resolve transition time: explicit override → crowd config → default
            if (transitionTime < 0f)
                transitionTime = agent.enemyCrowd?.flightTransitionTime ?? 0.3f;

            if (agent.crowdInstance != null && agent.crowdInstance.AnimatorWorkflow != null)
            {
                try
                {
                    agent.crowdInstance.StartAnimation(clip, -1f, 1f, transitionTime, null, loop);
                    return true;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[EnemyMasses] Failed to start flight clip '{clip.name}' for agent {agent.index}: {e.Message}");
                    return false;
                }
            }

            // Fallback to Mecanim if crowdInstance isn't available
            Animator animator = agent.mecanimAnimator;
            if (animator != null)
            {
                int stateHash = Animator.StringToHash(clip.name);
                if (stateHash != 0 && animator.HasState(0, stateHash))
                {
                    animator.CrossFade(stateHash, transitionTime, 0, 0f);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether an agent is using a Mecanim-based animator workflow.
        /// </summary>
        private static bool IsMecanimPreferredForAgent(CrowdAgent agent, EnemyCrowd crowd)
        {
            if (agent == null) return false;
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

        /// <summary>
        /// Safely sets a bool parameter on an Animator, logging a warning if the parameter doesn't exist.
        /// </summary>
        private static void TrySetAnimatorBool(Animator animator, string parameterName, bool value)
        {
            for (int i = 0; i < animator.parameterCount; i++)
            {
                var param = animator.GetParameter(i);
                if (param.name == parameterName && param.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(parameterName, value);
                    return;
                }
            }
        }

        /// <summary>
        /// Safely fires a trigger on an Animator.
        /// </summary>
        private static void TrySetAnimatorTrigger(Animator animator, string parameterName)
        {
            for (int i = 0; i < animator.parameterCount; i++)
            {
                var param = animator.GetParameter(i);
                if (param.name == parameterName && param.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(parameterName);
                    return;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        // STATUS EFFECTS & HEALING API
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Restores the ground idle/locomotion animation after exiting flight mode.
        /// Directly plays the correct clip via the animation controller, bypassing the
        /// throttled HandleAnimation update loop to ensure immediate visual feedback.
        /// Also resets cached animation state so subsequent HandleAnimation calls
        /// don't skip the agent due to stale state.
        /// </summary>
        private void RestoreGroundAnimation(CrowdAgent agent, float transitionTime = 0f)
        {
            if (agent == null || agent.isDead)
                return;

            // Reset animation caches so HandleAnimation doesn't think the agent
            // is already in the correct state
            agent.cachedBlendWeight = -1f;
            agent.lastIdleAnimationHash = -1;
            agent.idleAnimationWasGpu = false;
            agent.needsAnimationRefresh = true;

            // Directly force-play the ground animation via the animation controller
            if (_animationController != null)
            {
                float normalizedSpeed = _animationController.CalculateNormalizedSpeed(agent);
                _animationController.ForceAnimationRefresh(agent, normalizedSpeed, transitionTime);
            }
        }
        
        /// <summary>
        /// Gets the combat system for advanced damage/effect handling.
        /// Used by skill effects for network-aware damage application.
        /// </summary>
        public EnemyMassesCombatSystem GetCombatSystem() => _combatSystem;

        /// <summary>
        /// Gets the shared formation system used for calculating agent positions in formations.
        /// This is the same system used during spawning, ensuring consistent formation math
        /// across spawning, RTS move commands, and scripted formation moves.
        /// </summary>
        public EnemyMassesFormationSystem FormationSystem => _formationSystem;
        
        /// <summary>
        /// Apply a status effect to an agent by index.
        /// Use StatusEffectType.Regeneration with value = health per tick for healing over time.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to affect</param>
        /// <param name="type">Type of status effect to apply</param>
        /// <param name="duration">Duration in seconds (0 = permanent until dispelled)</param>
        /// <param name="value">Effect value (damage/healing per tick, stat multiplier, etc.)</param>
        /// <param name="tickInterval">For DOT/HOT effects: seconds between ticks (default: 1)</param>
        /// <param name="sourceAgentIndex">Source agent index (-1 for player/environment)</param>
        /// <returns>Slot index where effect was placed, or -1 if failed</returns>
        public int ApplyStatusEffect(int agentIndex, StatusEffectType type, float duration, float value, 
                                    float tickInterval = 1f, int sourceAgentIndex = -1, int maxStacks = 1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return -1;

            return _combatSystem.ApplyStatusEffect(agentIndex, type, duration, value, tickInterval, sourceAgentIndex, maxStacks);
        }
        
        /// <summary>
        /// Apply a status effect to an agent using a StatusEffectConfig asset.
        /// </summary>
        public int ApplyStatusEffect(int agentIndex, StatusEffectConfig config, int sourceAgentIndex = -1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return -1;

            return _combatSystem.ApplyStatusEffect(agentIndex, config, sourceAgentIndex);
        }
        
        /// <summary>
        /// Apply a status effect to all agents within a radius of a position.
        /// Perfect for creating healing zones, AOE debuffs, buff auras, etc.
        /// Use StatusEffectType.Regeneration for healing zones.
        /// </summary>
        /// <example>
        /// // Create a healing zone that heals 5 HP/sec for 10 seconds
        /// controller.ApplyStatusEffectInRadius(transform.position, 5f, StatusEffectType.Regeneration, 10f, 5f);
        /// 
        /// // Create a poison cloud that deals 3 damage/sec for 8 seconds to faction 1
        /// controller.ApplyStatusEffectInRadius(transform.position, 8f, StatusEffectType.Poison, 8f, 3f, 1f, -1, 1);
        /// </example>
        /// <param name="center">World position center of the effect</param>
        /// <param name="radius">Radius of the effect area</param>
        /// <param name="type">Type of status effect to apply</param>
        /// <param name="duration">Duration in seconds</param>
        /// <param name="value">Effect value (damage/healing per tick)</param>
        /// <param name="tickInterval">Seconds between ticks (default: 1)</param>
        /// <param name="sourceAgentIndex">Source agent index (-1 for environment)</param>
        /// <param name="targetFactionIndex">Optional: only affect agents of this faction</param>
        /// <returns>Number of agents affected</returns>
        public int ApplyStatusEffectInRadius(Vector3 center, float radius, StatusEffectType type, float duration, 
                                            float value, float tickInterval = 1f, int sourceAgentIndex = -1,
                                            int? targetFactionIndex = null)
        {
            return _combatSystem.ApplyStatusEffectAOE(center, radius, type, duration, value, tickInterval, sourceAgentIndex, targetFactionIndex);
        }
        
        /// <summary>
        /// Heal an agent by a flat amount (instant heal).
        /// </summary>
        /// <param name="agentIndex">Index of the agent to heal</param>
        /// <param name="healAmount">Amount of health to restore</param>
        /// <returns>True if agent was healed (was alive and not at full health)</returns>
        public bool HealAgent(int agentIndex, float healAmount)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;
                
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead)
                return false;
            
            float maxHealth = GetAgentMaxHealthInternal(agent);
            if (agent.health >= maxHealth)
                return false;

            float previousHealth = agent.health;
            agent.health = Mathf.Min(agent.health + healAmount, maxHealth);
            if (!Mathf.Approximately(previousHealth, agent.health))
            {
                EventAgentHealthChanged?.Invoke(agentIndex, agent.health, maxHealth);
            }
            return true;
        }
        
        /// <summary>
        /// Heal an agent by GPUICrowdInstance reference.
        /// </summary>
        public bool HealAgent(GPUICrowdInstance instance, float healAmount)
        {
            int agentIndex = GetAgentIndexFromInstance(instance);
            return HealAgent(agentIndex, healAmount);
        }
        
        /// <summary>
        /// Heal all agents within a radius (instant heal).
        /// </summary>
        /// <param name="center">World position center</param>
        /// <param name="radius">Radius of the heal area</param>
        /// <param name="healAmount">Amount of health to restore per agent</param>
        /// <param name="targetFactionIndex">Optional: only heal agents of this faction</param>
        /// <returns>Number of agents healed</returns>
        public int HealAgentsInRadius(Vector3 center, float radius, float healAmount, int? targetFactionIndex = null)
        {
            int healed = 0;
            float radiusSqr = radius * radius;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead) continue;
                
                float maxHealth = GetAgentMaxHealthInternal(agent);
                if (agent.health >= maxHealth) continue;
                
                if (targetFactionIndex.HasValue && agent.enemyCrowdIndex != targetFactionIndex.Value) continue;
                
                Vector3 agentPos = agent.navigationAgent != null ? agent.WorldPosition :
                                   (agent.crowdInstance != null ? agent.crowdInstance.transform.position : Vector3.zero);
                                   
                if ((agentPos - center).sqrMagnitude <= radiusSqr)
                {
                    float previousHealth = agent.health;
                    agent.health = Mathf.Min(agent.health + healAmount, maxHealth);
                    if (!Mathf.Approximately(previousHealth, agent.health))
                    {
                        EventAgentHealthChanged?.Invoke(i, agent.health, maxHealth);
                    }
                    healed++;
                }
            }
            
            return healed;
        }
        
        /// <summary>
        /// Remove a specific status effect from an agent.
        /// </summary>
        public void RemoveStatusEffect(int agentIndex, StatusEffectType type, bool wasDispelled = false)
        {
            _combatSystem.RemoveStatusEffect(agentIndex, type, wasDispelled);
        }
        
        /// <summary>
        /// Dispel all negative effects (debuffs, DOTs, CC) from an agent.
        /// Use this for cleanse/purify abilities.
        /// </summary>
        /// <returns>Number of effects dispelled</returns>
        public int DispelNegativeEffects(int agentIndex)
        {
            return _combatSystem.DispelNegativeEffects(agentIndex);
        }
        
        /// <summary>
        /// Dispel all positive effects (buffs) from an agent.
        /// Use this for purge/strip abilities.
        /// </summary>
        /// <returns>Number of effects dispelled</returns>
        public int DispelPositiveEffects(int agentIndex)
        {
            return _combatSystem.DispelPositiveEffects(agentIndex);
        }
        
        /// <summary>
        /// Check if an agent has a specific status effect active.
        /// </summary>
        public bool HasStatusEffect(int agentIndex, StatusEffectType type)
        {
            return _combatSystem.HasStatusEffect(agentIndex, type);
        }
        
        /// <summary>
        /// Clear all status effects from an agent (for respawn, transformation, etc.)
        /// </summary>
        public void ClearAllStatusEffects(int agentIndex)
        {
            _combatSystem.ClearAllStatusEffects(agentIndex);
        }
        
        /// <summary>
        /// Get an agent's current health.
        /// </summary>
        public float GetAgentHealth(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return 0f;
            return _agents[agentIndex]?.health ?? 0f;
        }
        
        /// <summary>
        /// Get an agent's maximum health.
        /// </summary>
        public float GetAgentMaxHealth(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return 0f;
            var agent = _agents[agentIndex];
            if (agent == null) return 0f;
            return GetAgentMaxHealthInternal(agent);
        }
        
        /// <summary>
        /// Get an agent's health as a normalized value (0-1).
        /// </summary>
        public float GetAgentHealthNormalized(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return 0f;
            var agent = _agents[agentIndex];
            if (agent == null) return 0f;
            float maxHealth = GetAgentMaxHealthInternal(agent);
            if (maxHealth <= 0f) return 0f;
            return agent.health / maxHealth;
        }
        
        /// <summary>
        /// Set an agent's health directly (clamped to 0-maxHealth).
        /// Does not trigger death if set to 0 - use KillAgent() for that.
        /// </summary>
        public void SetAgentHealth(int agentIndex, float health)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;
            var agent = _agents[agentIndex];
            if (agent == null) return;
            float maxHealth = GetAgentMaxHealthInternal(agent);
            float previousHealth = agent.health;
            agent.health = Mathf.Clamp(health, 0f, maxHealth);
            if (!Mathf.Approximately(previousHealth, agent.health))
            {
                EventAgentHealthChanged?.Invoke(agentIndex, agent.health, maxHealth);
            }
        }
        
        /// <summary>
        /// Internal helper to resolve max health.
        /// </summary>
        private float GetAgentMaxHealthInternal(CrowdAgent agent)
        {
            if (agent == null) return 0f;
            if (agent.maxHealth > 0f)
                return agent.maxHealth;

            if (agent.enemyCrowd != null)
            {
                return Mathf.Max(1f, agent.enemyCrowd.agentBaseHealth + agent.enemyCrowd.agentHealthVariance);
            }
            return 100f; // Default fallback
        }

        #endregion

        #region Public API - Audio

        /// <summary>
        /// Plays a footstep sound for a specific agent.
        /// Use this from animation events via GPUI Crowd Event Definition.
        /// </summary>
        /// <param name="agentIndex">Index of the agent</param>
        /// <param name="surfaceType">Optional surface type for varied sounds</param>
        public void PlayFootstepForAgent(int agentIndex, FootstepSurfaceType surfaceType = FootstepSurfaceType.Default)
        {
            _footstepAudioManager?.PlayFootstepForAgent(agentIndex, surfaceType);
        }
        
        /// <summary>
        /// Plays a footstep sound for a specific agent.
        /// Use this from animation events via GPUI Crowd Event Definition.
        /// </summary>
        /// <param name="agent">The agent</param>
        /// <param name="surfaceType">Optional surface type for varied sounds</param>
        public void PlayFootstepForAgent(CrowdAgent agent, FootstepSurfaceType surfaceType = FootstepSurfaceType.Default)
        {
            _footstepAudioManager?.PlayFootstepForAgent(agent, surfaceType);
        }
        
        /// <summary>
        /// Plays a footstep sound at a specific world position for a faction.
        /// </summary>
        public void PlayFootstepAtPosition(Vector3 position, EnemyCrowd faction, FootstepSurfaceType surfaceType = FootstepSurfaceType.Default)
        {
            _footstepAudioManager?.PlayFootstepAtPosition(position, faction, surfaceType);
        }
        
        /// <summary>
        /// Gets the FootstepAudioManager for advanced usage or custom event handlers.
        /// </summary>
        public FootstepAudioManager FootstepAudio => _footstepAudioManager;
        
        /// <summary>
        /// Gets the CombatSystem for external access (e.g., status effects, damage processing).
        /// </summary>
        public EnemyMassesCombatSystem CombatSystem => _combatSystem;
        
        /// <summary>
        /// Sets the global footstep audio limits at runtime.
        /// </summary>
        /// <param name="maxPerFrame">Maximum footsteps that can be triggered per frame (1-50)</param>
        /// <param name="maxGlobalSources">Maximum total concurrent footstep audio sources (1-100)</param>
        public void SetFootstepLimits(int maxPerFrame, int maxGlobalSources)
        {
            maxFootstepsPerFrame = Mathf.Clamp(maxPerFrame, 1, 50);
            maxGlobalFootstepSources = Mathf.Clamp(maxGlobalSources, 1, 100);
            _footstepAudioManager?.SetGlobalLimits(maxFootstepsPerFrame, maxGlobalFootstepSources);
        }

        /// <summary>
        /// Sets global caps for AgentSkill PlaySFX/PlayVFX actions.
        /// Use 0 for unlimited.
        /// </summary>
        public void SetSkillActionGlobalCaps(int maxOneShotSFX, int maxLoopingSFX, int maxVFXInstances)
        {
            maxGlobalSkillOneShotSFX = Mathf.Max(0, maxOneShotSFX);
            maxGlobalSkillLoopingSFX = Mathf.Max(0, maxLoopingSFX);
            maxGlobalSkillVFXInstances = Mathf.Max(0, maxVFXInstances);
            ApplySkillActionGlobalCaps();
        }
        
        /// <summary>
        /// Current number of active footstep audio sources (approximate).
        /// </summary>
        public int ActiveFootstepSources => _footstepAudioManager?.ActiveFootstepSources ?? 0;
        
        /// <summary>
        /// Enables debug logging for footstep audio to diagnose interval issues.
        /// </summary>
        public void SetFootstepDebugLogging(bool enabled)
        {
            if (_footstepAudioManager != null)
                _footstepAudioManager.DebugLogging = enabled;
        }

        private void ApplySkillActionGlobalCaps()
        {
            SkillVFXManager.SetGlobalCaps(
                maxGlobalSkillOneShotSFX,
                maxGlobalSkillLoopingSFX,
                maxGlobalSkillVFXInstances);
        }

        #endregion

        #region Public API - Navigation

        /// <summary>
        /// Marks the active agents list as dirty so it will be rebuilt on the next frame.
        /// Call this when a dead agent receives an impulse that needs to be processed.
        /// </summary>
        public void MarkActiveAgentsListDirty()
        {
            _activeAgentsListDirty = true;
        }

        /// <summary>
        /// Sets a new destination for all agents.
        /// </summary>
        public void SetDestination(Vector3 targetPosition)
        {
            _navigationController.SetTarget(targetPosition);
        }

        /// <summary>
        /// Sets a destination for agents from a specific faction by index.
        /// All agents of this faction will form their own formation independent of other factions.
        /// </summary>
        /// <param name="factionIndex">Index of the faction in the enemyCrowds list</param>
        /// <param name="targetPosition">World position to move to</param>
        /// <param name="useFormation">If true, arrange agents in formation. If false, all go to same point.</param>
        public void SetDestinationForFaction(int factionIndex, Vector3 targetPosition, bool useFormation = true)
        {
            // Get faction configuration
            var faction = GetEnemyCrowdAtIndex(factionIndex);
            if (faction == null)
            {
                Debug.LogWarning($"[EnemyMasses] Faction index {factionIndex} not found!");
                return;
            }

            // Collect all alive agents from this faction
            var factionAgents = new System.Collections.Generic.List<int>();
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent?.navigationAgent != null && agent.enemyCrowdIndex == factionIndex && !agent.isDead)
                {
                    factionAgents.Add(i);
                }
            }

            if (factionAgents.Count == 0)
            {
                Debug.LogWarning($"[EnemyMasses] Faction {factionIndex} has no alive agents!");
                return;
            }

            // Determine formation to use
            FormationType formationType = faction.overrideFormation 
                ? faction.formationType 
                : _defaultSpawnSettings.formationType;

            int movedCount = 0;
            int formationIndex = 0;

            foreach (int agentIndex in factionAgents)
            {
                var agent = _agents[agentIndex];
                var navAgent = agent.navigationAgent;
                
                Vector3 destination;
                if (useFormation)
                {
                    // Calculate formation position for THIS FACTION only
                    destination = _formationSystem.CalculateFormationPosition(
                        formationIndex,
                        factionAgents.Count,
                        targetPosition,
                        _defaultNavigationSettings.destinationSpread,
                        navAgent,
                        formationType
                    );
                    formationIndex++;
                }
                else
                {
                    destination = targetPosition;
                }

                if (navAgent != null && navAgent.Enabled)
                {
                    if (!navAgent.Capabilities.RequiresSurfaceValidationForControl || navAgent.IsOnNavMesh)
                    {
                        bool success = navAgent.SetDestination(destination);
                        if (!success && navAgent.SamplePosition(destination, out Vector3 sampled, 5f))
                        {
                            navAgent.SetDestination(sampled);
                        }
                    }
                }
                agent.hasArrived = false;
                movedCount++;
            }
            
            LogDebug($"[EnemyMasses] Set destination for faction {factionIndex} ('{faction.factionName}'): " +
                     $"{targetPosition} ({movedCount} agents) [Formation: {(useFormation ? formationType.ToString() : "None")}]");
        }

        /// <summary>
        /// Sets a destination for agents from a specific faction by name.
        /// All agents of this faction will form their own formation independent of other factions.
        /// </summary>
        /// <param name="factionName">Name of the faction</param>
        /// <param name="targetPosition">World position to move to</param>
        /// <param name="useFormation">If true, arrange agents in formation. If false, all go to same point.</param>
        public void SetDestinationForFaction(string factionName, Vector3 targetPosition, bool useFormation = true)
        {
            int factionIndex = -1;
            for (int i = 0; i < enemyCrowds.Count; i++)
            {
                if (enemyCrowds[i].factionName == factionName)
                {
                    factionIndex = i;
                    break;
                }
            }
            
            if (factionIndex == -1)
            {
                Debug.LogWarning($"[EnemyMasses] Faction '{factionName}' not found!");
                return;
            }
            
            SetDestinationForFaction(factionIndex, targetPosition, useFormation);
        }

        /// <summary>
        /// Clears all agent destinations.
        /// </summary>
        public void ClearAllDestinations()
        {
            foreach (var agent in _agents)
            {
                if (agent?.navigationAgent != null)
                {
                    agent.navigationAgent.ResetPath();
                    agent.hasArrived = false;
                }
            }
        }

        /// <summary>
        /// Clears destinations for agents from a specific faction.
        /// </summary>
        /// <param name="factionIndex">Index of the faction in the enemyCrowds list</param>
        public void ClearDestinationsForFaction(int factionIndex)
        {
            int clearedCount = 0;
            
            foreach (var agent in _agents)
            {
                if (agent?.navigationAgent != null && agent.enemyCrowdIndex == factionIndex)
                {
                    agent.navigationAgent.ResetPath();
                    agent.hasArrived = false;
                    clearedCount++;
                }
            }
            
            LogDebug($"[EnemyMasses] Cleared destinations for faction {factionIndex} ({clearedCount} agents)");
        }

        /// <summary>
        /// Sets an attack target transform for all agents in a specific faction.
        /// Agents will continuously chase and attack this target.
        /// </summary>
        /// <param name="factionIndex">Index of the faction in the enemyCrowds list</param>
        /// <param name="targetTransform">Transform to attack</param>
        public void SetAttackTargetForFaction(int factionIndex, Transform targetTransform)
        {
            if (targetTransform == null)
            {
                Debug.LogWarning($"[EnemyMasses] Cannot set null attack target for faction {factionIndex}!");
                return;
            }

            // Get faction configuration
            var faction = GetEnemyCrowdAtIndex(factionIndex);
            if (faction == null)
            {
                Debug.LogWarning($"[EnemyMasses] Faction index {factionIndex} not found!");
                return;
            }

            int targetSetCount = 0;
            
            // Set attack target for all alive agents in this faction
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent?.navigationAgent != null && agent.enemyCrowdIndex == factionIndex && !agent.isDead)
                {
                    agent.attackTargetTransform = targetTransform;
                    agent.attackTargetPosition = targetTransform.position;
                    agent.hasAttackTargetPosition = true;
                    agent.attackTargetAgentIndex = -1; // Not attacking another agent
                    if (agent.navigationState == AgentNavigationState.Formation)
                    {
                        agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                    targetSetCount++;
                }
            }
            
            // Notify navigation controller to apply warmup delay
            _navigationController.NotifyAttackTargetsSet();
        }

        /// <summary>
        /// Sets an attack target transform for all agents in a specific spawn group.
        /// Only affects agents that were spawned together in the same batch.
        /// </summary>
        /// <param name="spawnGroupId">ID of the spawn group</param>
        /// <param name="targetTransform">Transform to attack</param>
        public void SetAttackTargetForSpawnGroup(int spawnGroupId, Transform targetTransform)
        {
            if (targetTransform == null)
            {
                Debug.LogWarning($"[EnemyMasses] Cannot set null attack target for spawn group {spawnGroupId}!");
                return;
            }

            var spawnGroups = _spawnController.GetSpawnGroups();
            if (!spawnGroups.TryGetValue(spawnGroupId, out SpawnGroup spawnGroup))
            {
                Debug.LogWarning($"[EnemyMasses] Spawn group {spawnGroupId} not found!");
                return;
            }

            int targetSetCount = 0;
            
            // Set attack target for all alive agents in this spawn group
            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= _agents.Count)
                    continue;
                    
                var agent = _agents[agentIndex];
                if (agent?.navigationAgent != null && !agent.isDead)
                {
                    agent.attackTargetTransform = targetTransform;
                    agent.attackTargetPosition = targetTransform.position;
                    agent.hasAttackTargetPosition = true;
                    agent.attackTargetAgentIndex = -1; // Not attacking another agent
                    if (agent.navigationState == AgentNavigationState.Formation)
                    {
                        agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                    targetSetCount++;
                }
            }
            
            if (targetSetCount > 0)
            {
                // Notify navigation controller to apply warmup delay
                _navigationController.NotifyAttackTargetsSet();
            }
        }

        /// <summary>
        /// Sets an attack target transform for specific agents by their indices.
        /// </summary>
        /// <param name="agentIndices">List of agent indices to affect</param>
        /// <param name="targetTransform">Transform to attack</param>
        public void SetAttackTargetForAgents(List<int> agentIndices, Transform targetTransform)
        {
            if (targetTransform == null || agentIndices == null || agentIndices.Count == 0)
                return;

            int targetSetCount = 0;
            
            foreach (int agentIndex in agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= _agents.Count)
                    continue;
                    
                var agent = _agents[agentIndex];
                if (agent?.navigationAgent != null && !agent.isDead)
                {
                    agent.attackTargetTransform = targetTransform;
                    agent.attackTargetPosition = targetTransform.position;
                    agent.hasAttackTargetPosition = true;
                    agent.attackTargetAgentIndex = -1; // Not attacking another agent
                    if (agent.navigationState == AgentNavigationState.Formation)
                    {
                        agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                    targetSetCount++;
                }
            }
            
            if (targetSetCount > 0)
            {
                _navigationController.NotifyAttackTargetsSet();
            }
        }

        /// <summary>
        /// Sets a destination for all agents in a specific spawn group.
        /// The group will form its own formation independent of other agents.
        /// </summary>
        /// <param name="spawnGroupId">ID of the spawn group to move</param>
        /// <param name="targetPosition">Target position to move to</param>
        /// <param name="useFormation">If true, arrange agents in formation. If false, all go to same point.</param>
        /// <param name="useManualCommand">If true, sets agents to ManualCommand state to prevent formation system override (for patrol system).</param>
        public void SetDestinationForSpawnGroup(int spawnGroupId, Vector3 targetPosition, bool useFormation = true, bool useManualCommand = false)
        {
            var spawnGroups = _spawnController.GetSpawnGroups();
            if (!spawnGroups.TryGetValue(spawnGroupId, out SpawnGroup spawnGroup))
            {
                Debug.LogWarning($"[EnemyMasses] Spawn group {spawnGroupId} not found!");
                return;
            }

            // Count alive agents in this group
            var aliveAgentIndices = new System.Collections.Generic.List<int>();
            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex >= 0 && agentIndex < _agents.Count)
                {
                    var agent = _agents[agentIndex];
                    if (agent?.navigationAgent != null && agent.health > 0)
                    {
                        aliveAgentIndices.Add(agentIndex);
                    }
                }
            }

            if (aliveAgentIndices.Count == 0)
            {
                Debug.LogWarning($"[EnemyMasses] Spawn group {spawnGroupId} has no alive agents!");
                return;
            }

            int movedCount = 0;
            int formationIndex = 0;

            foreach (int agentIndex in aliveAgentIndices)
            {
                var agent = _agents[agentIndex];
                var navAgent = agent.navigationAgent;
                
                Vector3 destination;
                if (useFormation)
                {
                    // Calculate formation position for THIS GROUP only
                    // formationIndex goes 0, 1, 2... within THIS group
                    // aliveAgentIndices.Count is the total for THIS group
                    // Use the spawn group's stored formation spacing to maintain consistent spacing
                    destination = _formationSystem.CalculateFormationPosition(
                        formationIndex,
                        aliveAgentIndices.Count,
                        targetPosition,
                        _defaultSpawnSettings.formationSize,
                        spawnGroup.formation,  // Use the formation they were spawned with
                        spawnGroup.formationSpacing,  // Use the spacing they were spawned with
                        _defaultSpawnSettings.formationThickness,
                        navAgent
                    );
                    formationIndex++;
                }
                else
                {
                    // All agents go to the exact same point
                    destination = targetPosition;
                }

                // Ensure navigation agent is active before setting destination
                if (navAgent != null && navAgent.Enabled && navAgent.Transform != null && navAgent.Transform.gameObject.activeInHierarchy)
                {
                    // Resume agent if stopped (happens when agents arrive at previous destination)
                    if (navAgent.IsStopped)
                    {
                        navAgent.IsStopped = false;
                    }
                    
                    // Only set destination if on NavMesh
                    if (!navAgent.Capabilities.RequiresSurfaceValidationForControl || navAgent.IsOnNavMesh)
                    {
                        // Set the destination
                        bool success = navAgent.SetDestination(destination);
                        
                        // If SetDestination failed, try with a sampled point nearby
                        if (!success)
                        {
                            if (navAgent.SamplePosition(destination, out Vector3 sampled, 5f))
                            {
                                navAgent.SetDestination(sampled);
                            }
                        }
                    }
                    
                    // Reset destination retries to prevent false force-warping from the navigation controller
                    agent.destinationRetries = 0;
                }
                
                agent.hasArrived = false;
                
                // Set ManualCommand state to prevent formation system override
                // This is critical for patrol groups - without this, the formation system
                // will continuously override their destinations causing animation flickering
                if (useManualCommand)
                {
                    // Don't override CombatOverride state - let combat system handle it
                    if (agent.navigationState != AgentNavigationState.CombatOverride)
                    {
                        // Always update manualCommandDestination when useManualCommand is true
                        // This ensures the destination stays in sync when patrol assigns new waypoints
                        agent.manualCommandDestination = destination;
                        agent.hasManualCommand = true;
                        
                        // Only change navigation state if not already in ManualCommand
                        if (agent.navigationState != AgentNavigationState.ManualCommand)
                        {
                            agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                        }
                    }
                }
                
                movedCount++;
            }
        }

        /// <summary>
        /// Sets a destination for a single agent by index.
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the agents list</param>
        /// <param name="targetPosition">Target position to move to</param>
        /// <returns>True if destination was set successfully</returns>
        public bool SetDestinationForAgent(int agentIndex, Vector3 targetPosition)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
            {
                //Debug.LogWarning($"[EnemyMasses] Agent index {agentIndex} out of range (0-{_agents.Count - 1})!");
                return false;
            }

            var agent = _agents[agentIndex];
            if (agent == null || agent.navigationAgent == null)
            {
                //Debug.LogWarning($"[EnemyMasses] Agent at index {agentIndex} is null or has no NavigationAgent!");
                return false;
            }

            if (agent.isDead)
            {
                //Debug.LogWarning($"[EnemyMasses] Agent at index {agentIndex} is dead!");
                return false;
            }

            agent.navigationAgent.SetDestination(targetPosition);
            agent.hasArrived = false;

            return true;
        }

        /// <summary>
        /// Clears the destination for a single agent by index.
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the agents list</param>
        /// <returns>True if destination was cleared successfully</returns>
        public bool ClearDestinationForAgent(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
            {
                Debug.LogWarning($"[EnemyMasses] Agent index {agentIndex} out of range (0-{_agents.Count - 1})!");
                return false;
            }

            var agent = _agents[agentIndex];
            if (agent == null || agent.navigationAgent == null)
            {
                Debug.LogWarning($"[EnemyMasses] Agent at index {agentIndex} is null or has no NavigationAgent!");
                return false;
            }

            agent.navigationAgent.ResetPath();
            agent.hasArrived = false;

            string agentName = agent.crowdInstance?.gameObject.name ?? "Unknown";
            LogDebug($"[EnemyMasses] Cleared destination for agent {agentIndex} ('{agentName}')");
            return true;
        }

        /// <summary>
        /// Clears destinations for all agents in a specific spawn group.
        /// </summary>
        /// <param name="spawnGroupId">ID of the spawn group to clear</param>
        public void ClearDestinationsForSpawnGroup(int spawnGroupId)
        {
            var spawnGroups = _spawnController.GetSpawnGroups();
            if (!spawnGroups.TryGetValue(spawnGroupId, out SpawnGroup spawnGroup))
            {
                Debug.LogWarning($"[EnemyMasses] Spawn group {spawnGroupId} not found!");
                return;
            }

            int clearedCount = 0;
            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex >= 0 && agentIndex < _agents.Count)
                {
                    var agent = _agents[agentIndex];
                    if (agent?.navigationAgent != null)
                    {
                        agent.navigationAgent.ResetPath();
                        agent.hasArrived = false;
                        clearedCount++;
                    }
                }
            }

            LogDebug($"[EnemyMasses] Cleared destinations for spawn group {spawnGroupId} " +
                     $"('{spawnGroup.factionName}', {clearedCount} agents)");
        }

        #endregion

        #region Public API - Queries

        /// <summary>
        /// Gets the faction index of this controller.
        /// Returns the index of the first EnemyCrowd if multiple exist, or -1 if none.
        /// </summary>
        public int GetFactionIndex()
        {
            // If this controller has enemy crowds, return the first one's index
            // For single-faction controllers, this is straightforward
            // For multi-faction controllers, this returns 0 (first faction)
            return enemyCrowds.Count > 0 ? 0 : -1;
        }
        
        /// <summary>
        /// Gets the faction index for a specific agent.
        /// </summary>
        /// <param name="agentIndex">The agent index</param>
        /// <returns>The faction index (enemyCrowdIndex) of the agent, or -1 if invalid</returns>
        public int GetAgentFactionIndex(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return -1;
            
            var agent = _agents[agentIndex];
            return agent?.enemyCrowdIndex ?? -1;
        }

        /// <summary>
        /// Gets the total number of spawned agents.
        /// </summary>
        public int GetAgentCount()
        {
            return _agents.Count;
        }

        /// <summary>
        /// Gets the number of alive agents.
        /// </summary>
        public int GetAliveAgentCount()
        {
            int count = 0;
            foreach (var agent in _agents)
            {
                if (agent != null && !agent.isDead)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Gets the number of dead agents.
        /// </summary>
        public int GetDeadAgentCount()
        {
            return _performanceManager.GetDeadAgentCount();
        }

        /// <summary>
        /// Gets an agent by index.
        /// </summary>
        public CrowdAgent GetAgent(int index)
        {
            if (index < 0 || index >= _agents.Count)
                return null;
            return _agents[index];
        }
        
        /// <summary>
        /// Gets an agent by its GPUICrowdInstance.
        /// Used by animation event handlers to find the agent for a crowd instance.
        /// </summary>
        public CrowdAgent GetAgentByCrowdInstance(GPUInstancerPro.CrowdAnimations.GPUICrowdInstance crowdInstance)
        {
            if (crowdInstance == null) return null;
            
            // Fast path: use the instance lookup dictionary
            if (_instanceLookup.TryGetValue(crowdInstance, out int index))
            {
                if (index >= 0 && index < _agents.Count)
                    return _agents[index];
            }
            
            // Fallback: linear search (shouldn't happen in normal operation)
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i]?.crowdInstance == crowdInstance)
                    return _agents[i];
            }
            
            return null;
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // Network Lookup Methods
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Gets an agent by its network ID.
        /// Returns null if agent not found or not in multiplayer mode.
        /// </summary>
        /// <param name="networkId">The network ID assigned during spawn</param>
        /// <returns>The CrowdAgent or null if not found</returns>
        public CrowdAgent GetAgentByNetworkId(uint networkId)
        {
            if (networkId == 0) return null;
            
            if (_networkIdLookup.TryGetValue(networkId, out int index))
            {
                if (index >= 0 && index < _agents.Count)
                    return _agents[index];
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets the agent index by network ID.
        /// Returns -1 if not found.
        /// </summary>
        public int GetAgentIndexByNetworkId(uint networkId)
        {
            if (networkId == 0) return -1;
            return _networkIdLookup.TryGetValue(networkId, out int index) ? index : -1;
        }
        
        /// <summary>
        /// Gets the network ID for an agent index.
        /// Returns 0 if agent not found or not networked.
        /// </summary>
        public uint GetNetworkIdByIndex(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return 0;
            return _agents[agentIndex]?.networkId ?? 0;
        }
        
        /// <summary>
        /// Registers a network ID mapping for an agent.
        /// Called by network layer when spawning agents on clients.
        /// </summary>
        public void RegisterNetworkId(int agentIndex, uint networkId)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count || networkId == 0)
                return;
            
            var agent = _agents[agentIndex];
            if (agent == null) return;
            
            // Remove old mapping if exists
            if (agent.networkId != 0 && agent.networkId != networkId)
            {
                _networkIdLookup.Remove(agent.networkId);
            }
            
            agent.networkId = networkId;
            _networkIdLookup[networkId] = agentIndex;
        }
        
        /// <summary>
        /// Unregisters a network ID mapping and cleans up anti-cheat trackers.
        /// Called when agents are despawned.
        /// </summary>
        public void UnregisterNetworkId(uint networkId)
        {
            if (networkId == 0) return;
            
            if (_networkIdLookup.TryGetValue(networkId, out int index))
            {
                if (index >= 0 && index < _agents.Count)
                {
                    var agent = _agents[index];
                    if (agent != null)
                        agent.ClearNetworkState();
                }
                _networkIdLookup.Remove(networkId);
            }
            
            // Clean up per-agent anti-cheat tracker data so dictionaries
            // don't grow unbounded over the course of a match.
            AntiCheatComponent.Instance?.Manager?.ClearAgent(networkId);
        }
        
        /// <summary>
        /// Gets all agents owned by a specific player.
        /// </summary>
        /// <param name="playerId">Player ID to filter by</param>
        /// <param name="results">List to populate with agent indices</param>
        /// <returns>Number of agents found</returns>
        public int GetAgentsByOwner(int playerId, List<int> results)
        {
            results.Clear();
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent != null && !agent.isDead && agent.ownerPlayerId == playerId)
                {
                    results.Add(i);
                }
            }
            
            return results.Count;
        }
        
        /// <summary>
        /// Allocates the next network ID (server only).
        /// </summary>
        public uint AllocateNetworkId()
        {
            return _nextNetworkId++;
        }
        
        /// <summary>
        /// Gets the next spawn group ID and increments the counter.
        /// Use to batch spawns for network synchronization.
        /// </summary>
        public int GetNextSpawnGroupId()
        {
            return _currentSpawnGroupId++;
        }

        /// <summary>
        /// Gets all agents in a radius from a center point.
        /// </summary>
        public List<int> GetAgentsInRadius(Vector3 center, float radius)
        {
            return _performanceManager.GetAgentsInRadius(center, radius);
        }

        /// <summary>
        /// Checks if an agent is dead.
        /// </summary>
        public bool IsAgentDead(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return true;

            var agent = _agents[agentIndex];
            return agent == null || agent.isDead;
        }

        /// <summary>
        /// Gets the number of configured enemy crowd factions.
        /// </summary>
        public int GetFactionCount()
        {
            return enemyCrowds?.Count ?? 0;
        }

        /// <summary>
        /// Gets the EnemyCrowd configuration at the specified index.
        /// </summary>
        public EnemyCrowd GetEnemyCrowdAtIndex(int index)
        {
            if (enemyCrowds == null || index < 0 || index >= enemyCrowds.Count)
                return null;
            
            return enemyCrowds[index];
        }

        /// <summary>
        /// Gets all active spawn groups.
        /// </summary>
        public IReadOnlyDictionary<int, SpawnGroup> GetSpawnGroups()
        {
            return _spawnController?.GetSpawnGroups();
        }

        /// <summary>
        /// Gets the list of all agents (for editor use).
        /// </summary>
        public List<CrowdAgent> GetAgents()
        {
            return _agents;
        }

        /// <summary>
        /// Gets the navigation controller for advanced operations.
        /// </summary>
        public EnemyMassesNavigationController GetNavigationController()
        {
            return _navigationController;
        }
        
        /// <summary>
        /// Gets a CrowdAgent by its associated GameObject.
        /// Used for RTS selection to determine faction membership.
        /// </summary>
        /// <param name="gameObject">The GameObject to find the agent for</param>
        /// <returns>The CrowdAgent if found, null otherwise</returns>
        public CrowdAgent GetAgentByGameObject(GameObject gameObject)
        {
            if (gameObject == null) return null;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null) continue;
                
                // Check navigation agent transform
                if (agent.navigationAgent != null && agent.navigationAgent.Transform != null && agent.navigationAgent.Transform.gameObject == gameObject)
                {
                    return agent;
                }
                
                // Check GPUICrowdInstance
                if (agent.crowdInstance != null && agent.crowdInstance.gameObject == gameObject)
                {
                    return agent;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets all agents within a radius from a center point (returns CrowdAgent objects).
        /// Used by projectile system for AOE damage.
        /// </summary>
        public List<CrowdAgent> GetCrowdAgentsInRadius(UnityEngine.Vector3 center, float radius)
        {
            if (_spatialGrid != null && performanceSettings != null && performanceSettings.enableSpatialGrid)
            {
                var indices = _performanceManager.GetAgentsInRadius(center, radius);
                var result = new List<CrowdAgent>(indices.Count);
                for (int i = 0; i < indices.Count; i++)
                {
                    int agentIndex = indices[i];
                    if (agentIndex < 0 || agentIndex >= _agents.Count)
                        continue;

                    var agent = _agents[agentIndex];
                    if (agent == null || agent.isDead || agent.navigationAgent == null)
                        continue;

                    result.Add(agent);
                }

                return result;
            }

            var fallback = new List<CrowdAgent>();
            float radiusSqr = radius * radius;

            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;

                Vector3 agentPos = agent.WorldPosition;
                float distSqr = (agentPos - center).sqrMagnitude;

                if (distSqr <= radiusSqr)
                {
                    fallback.Add(agent);
                }
            }

            return fallback;
        }
        
        /// <summary>
        /// Finds the agent index by NavMeshAgent reference.
        /// Used by projectile system for hit detection.
        /// </summary>
        public int FindAgentIndexByNavMeshAgent(UnityEngine.AI.NavMeshAgent navAgent)
        {
            if (navAgent == null) return -1;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var crowdNavigation = _agents[i]?.navigationAgent;
                if (crowdNavigation == null)
                    continue;

                if (crowdNavigation.GetUnderlyingAgent<UnityEngine.AI.NavMeshAgent>() == navAgent)
                    return i;
            }
            
            return -1;
        }

        /// <summary>
        /// Finds the agent index by navigation agent reference (A* or other adapters).
        /// </summary>
        public int FindAgentIndexByNavigationAgent(INavigationAgent navAgent)
        {
            if (navAgent == null) return -1;

            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i]?.navigationAgent == navAgent)
                    return i;
            }

            return -1;
        }
        
        /// <summary>
        /// Applies damage to an agent from the projectile system.
        /// </summary>
        public void DealDamageToAgent(int agentIndex, float damage, Vector3 damageDirection, int sourceAgentIndex = -1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return;

            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;
            
            // Get attacker position for ally defense system
            Vector3? attackerPosition = null;
            if (sourceAgentIndex >= 0 && sourceAgentIndex < _agents.Count)
            {
                var sourceAgent = _agents[sourceAgentIndex];
                if (sourceAgent?.navigationAgent != null)
                    attackerPosition = sourceAgent.WorldPosition;
                else if (sourceAgent?.crowdInstance != null)
                    attackerPosition = sourceAgent.crowdInstance.transform.position;
            }
            
            TryApplyDamage(agentIndex, damage, true, false, damageDirection, Vector3.up, attackerPosition, sourceAgentIndex);
        }

        /// <summary>
        /// Returns whether the controller is initialized.
        /// </summary>
        public bool IsInitialized => _initialized;

        private void LogDebug(string message, UnityEngine.Object context = null)
        {
            if (!enableDebugLogs)
                return;

            if (context != null)
                Debug.Log(message, context);
            else
                Debug.Log(message);
        }

        /// <summary>
        /// Finds all alive agents and returns their indices.
        /// </summary>
        public System.Collections.Generic.List<int> GetAliveAgentIndices()
        {
            var aliveIndices = new System.Collections.Generic.List<int>();
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent != null && !agent.isDead && agent.health > 0)
                {
                    aliveIndices.Add(i);
                }
            }
            return aliveIndices;
        }

        /// <summary>
        /// Clears all spawned agents and resets the system state.
        /// </summary>
        public void ClearAllAgents()
        {
            int count = _agents.Count;
            
            LogDebug($"[EnemyMasses] Clearing {count} agents...");
            
            // Destroy all agent GameObjects
            for (int i = _agents.Count - 1; i >= 0; i--)
            {
                var agent = _agents[i];
                if (agent?.crowdInstance != null)
                {
                    // IMPORTANT: Remove from GPUI BEFORE destroying to prevent buffer index corruption
                    // Use safe helper to prevent errors during shutdown
                    EnemyMassesGPUIHelper.SafeRemovePrefabInstance(agent.prefabComponent);
                    agent.prefabComponent = null;
                    
                    Destroy(agent.crowdInstance.gameObject);
                }
            }
            
            // Clear all lists
            _agents.Clear();
            _instanceLookup.Clear();
            
            // Reset spatial grid
            if (_spatialGrid != null)
            {
                _spatialGrid.Clear();
            }
            
            LogDebug($"[EnemyMasses] [OK] Cleared all {count} agents and reset state");
        }

        /// <summary>
        /// Logs a breakdown of agents by faction.
        /// </summary>
        public void LogFactionBreakdown()
        {
            if (enemyCrowds == null || enemyCrowds.Count == 0)
            {
                LogDebug("[EnemyMasses] No factions configured.");
                return;
            }

            Dictionary<string, int> factionCounts = new Dictionary<string, int>();
            
            foreach (var agent in _agents)
            {
                if (agent == null) continue;
                
                string factionName = "Unknown";
                if (agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < enemyCrowds.Count)
                {
                    var crowd = enemyCrowds[agent.enemyCrowdIndex];
                    if (crowd != null)
                        factionName = crowd.factionName;
                }
                
                if (!factionCounts.ContainsKey(factionName))
                    factionCounts[factionName] = 0;
                
                factionCounts[factionName]++;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== [EnemyMasses] Faction Breakdown ===");
            sb.AppendLine($"Total Agents: {_agents.Count}");
            sb.AppendLine($"Total Factions: {factionCounts.Count}");
            sb.AppendLine("");
            
            foreach (var kvp in factionCounts)
            {
                float percentage = (_agents.Count > 0) ? (kvp.Value / (float)_agents.Count * 100f) : 0f;
                sb.AppendLine($"  {kvp.Key}: {kvp.Value} agents ({percentage:F1}%)");
            }
            
            LogDebug(sb.ToString());
        }

        #endregion

        #region Public API - Civil Navigation

        /// <summary>
        /// Gets the civil navigation controller for advanced civil NPC operations.
        /// </summary>
        public CivilNavigationController GetCivilNavigationController()
        {
            return _civilNavigationController;
        }
        
        /// <summary>
        /// Checks if an agent is in civil mode (autonomous waypoint navigation).
        /// </summary>
        public bool IsAgentCivil(int agentIndex)
        {
            return _civilNavigationController?.IsAgentCivil(agentIndex) ?? false;
        }
        
        /// <summary>
        /// Disables civil mode for a specific agent, making them participate in combat/formation behavior.
        /// The agent will immediately start using the combat and navigation systems.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to convert from civil to combat mode</param>
        /// <returns>True if successful, false if agent not found or not in civil mode</returns>
        public bool DisableAgentCivilMode(int agentIndex)
        {
            if (_civilNavigationController == null || agentIndex < 0 || agentIndex >= _agents.Count)
                return false;
            
            var agent = _agents[agentIndex];
            if (agent == null)
                return false;
            
            // Check if agent is actually in civil mode
            if (!_civilNavigationController.IsAgentCivil(agentIndex))
                return false;
            
            // Remove civil state - this cleans up props, waypoint registrations, etc.
            _civilNavigationController.RemoveCivilState(agentIndex);
            
            // Clear the civil mode flag on the agent
            agent.isCivilMode = false;
            
            // Reset navigation state to formation so they rejoin their group
            agent.navigationState = AgentNavigationState.Formation;
            
            // Clear any civil-specific state
            agent.civilStateIndex = -1;
            
            LogDebug($"[EnemyCrowd] Agent {agentIndex} converted from civil to combat mode");
            return true;
        }
        
        /// <summary>
        /// Enables civil mode for a specific agent, removing them from combat/formation behavior.
        /// The agent will start following civil navigation waypoints if available.
        /// Note: The agent's faction (EnemyCrowd) must have enableCivilMode set to true for full civil behavior.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to convert to civil mode</param>
        /// <param name="startWaypoint">Optional: The waypoint to start civil navigation from. If null, agent stays at current position.</param>
        /// <returns>True if successful, false if agent not found or already in civil mode</returns>
        public bool EnableAgentCivilMode(int agentIndex, CivilWaypointNode startWaypoint = null)
        {
            if (_civilNavigationController == null || agentIndex < 0 || agentIndex >= _agents.Count)
                return false;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead)
                return false;

            if (!CanUseCivilNavigation(agent))
            {
                Debug.LogWarning($"[EnemyCrowd] Cannot enable civil mode for agent {agentIndex}. Civil mode currently requires Unity NavMesh navigation.");
                return false;
            }
            
            // Check if agent is already in civil mode
            if (_civilNavigationController.IsAgentCivil(agentIndex))
                return false;
            
            // Set civil mode flag
            agent.isCivilMode = true;
            
            // If a waypoint is provided, spawn at that waypoint
            if (startWaypoint != null)
            {
                _civilNavigationController.SpawnCivilAgent(agent, startWaypoint);
            }
            else
            {
                // Initialize civil state at current position
                _civilNavigationController.InitializeCivilAgentAtCurrentPosition(agent);
            }
            
            LogDebug($"[EnemyCrowd] Agent {agentIndex} converted from combat to civil mode");
            return true;
        }
        
        /// <summary>
        /// Gets civil navigation statistics.
        /// </summary>
        public CivilAgentStats GetCivilStats()
        {
            return _civilNavigationController?.GetStats() ?? default;
        }
        
        /// <summary>
        /// Spawns a civil agent at a specific spawn waypoint.
        /// </summary>
        /// <param name="agentIndex">Index of an existing agent to convert to civil mode</param>
        /// <param name="spawnWaypoint">The spawn waypoint to start at</param>
        public void SpawnCivilAgent(int agentIndex, CivilWaypointNode spawnWaypoint)
        {
            if (_civilNavigationController == null || agentIndex < 0 || agentIndex >= _agents.Count)
                return;
            
            var agent = _agents[agentIndex];
            if (agent == null)
                return;

            if (!CanUseCivilNavigation(agent))
            {
                Debug.LogWarning($"[EnemyCrowd] Cannot spawn civil agent {agentIndex}. Civil mode currently requires Unity NavMesh navigation.");
                return;
            }
            
            agent.isCivilMode = true;
            _civilNavigationController.SpawnCivilAgent(agent, spawnWaypoint);
        }
        
        /// <summary>
        /// Forces a civil agent to despawn with fade effect.
        /// </summary>
        public void DespawnCivilAgent(int agentIndex)
        {
            _civilNavigationController?.ForceDespawn(agentIndex);
        }
        
        /// <summary>
        /// Forces a civil agent to navigate to a specific waypoint.
        /// </summary>
        public void NavigateCivilAgentToWaypoint(int agentIndex, CivilWaypointNode targetWaypoint)
        {
            _civilNavigationController?.ForceNavigateToWaypoint(agentIndex, targetWaypoint);
        }
        
        /// <summary>
        /// Forces a civil agent to start a specific activity.
        /// </summary>
        public void StartCivilActivity(int agentIndex, CivilActivityDefinition activity)
        {
            _civilNavigationController?.ForceStartActivity(agentIndex, activity);
        }

        private static bool CanUseCivilNavigation(CrowdAgent agent)
        {
            var navAgent = agent?.navigationAgent;
            if (navAgent == null)
                return false;

            if (!navAgent.Capabilities.RequiresSurfaceValidationForControl)
                return false;

            return agent.navAgent != null;
        }

        #endregion

        #region Helper Methods

        private bool IsAgentActiveForNavigation(CrowdAgent agent)
        {
            if (agent == null || agent.isDead || agent.ragdollActivated)
                return false;

            var navAgent = agent.navigationAgent;
            return navAgent != null && navAgent.Enabled;
        }

        private bool IsAgentAtDestination(CrowdAgent agent)
        {
            if (agent == null || agent.navigationAgent == null)
                return false;

            if (agent.hasArrived)
                return true;

            var navAgent = agent.navigationAgent;
            if (!navAgent.Enabled)
                return false;
            
            if (navAgent.Capabilities.RequiresSurfaceValidationForControl && !navAgent.IsOnNavMesh)
                return false;

            if (!navAgent.HasPath)
                return false;

            if (navAgent.PathPending)
                return false;

            float remainingDistance = navAgent.RemainingDistance;
            return remainingDistance <= navAgent.StoppingDistance + 0.1f;
        }

        private bool ShouldUseRagdoll(CrowdAgent agent)
        {
            return _ragdollSystem.ShouldUseRagdollForAgent(agent);
        }

        private bool CanApplyCombatRangeOverride(EnemyCrowd faction, CombatRangeOverrideMode mode, int factionIndex)
        {
            if (mode == CombatRangeOverrideMode.Default)
                return true;

            if (faction == null)
                return false;

            if (faction.combatRangeType != CombatRangeType.Hybrid)
            {
                string requested = mode == CombatRangeOverrideMode.ForceRanged ? "Ranged" : "Melee";
                string reason = $"Combat Range Type is {faction.combatRangeType}.";
                if (faction.combatRangeType == CombatRangeType.Melee && mode == CombatRangeOverrideMode.ForceRanged)
                    reason = "Ranged is disabled because Combat Range Type is Melee.";
                else if (faction.combatRangeType == CombatRangeType.Ranged && mode == CombatRangeOverrideMode.ForceMelee)
                    reason = "Melee is disabled because Combat Range Type is Ranged.";

                Debug.LogWarning($"[EnemyMasses] Combat range override '{requested}' ignored for faction {factionIndex} ('{faction.factionName}'): " +
                               $"{reason} Set to Hybrid to enable melee + ranged switching.");
                return false;
            }

            return true;
        }

        private bool IsCombatRangeOverrideCompatible(EnemyCrowd faction, CombatRangeOverrideMode mode)
        {
            if (mode == CombatRangeOverrideMode.Default)
                return true;

            return faction != null && faction.combatRangeType == CombatRangeType.Hybrid;
        }

        private CombatRangeOverrideMode GetFactionCombatRangeOverrideMode(int factionIndex)
        {
            return _factionCombatRangeOverrides.TryGetValue(factionIndex, out var mode)
                ? mode
                : CombatRangeOverrideMode.Default;
        }

        private CombatRangeOverrideMode GetTypeCombatRangeOverrideMode(string enemyTypeName)
        {
            if (string.IsNullOrWhiteSpace(enemyTypeName))
                return CombatRangeOverrideMode.Default;

            string trimmedType = enemyTypeName.Trim();
            if (string.IsNullOrEmpty(trimmedType))
                return CombatRangeOverrideMode.Default;

            return _typeCombatRangeOverrides.TryGetValue(trimmedType, out var mode)
                ? mode
                : CombatRangeOverrideMode.Default;
        }

        private void ApplyCombatRangeOverrideToAgent(CrowdAgent agent, CombatRangeOverrideMode mode)
        {
            if (agent == null)
                return;

            agent.combatRangeOverride = mode;

            if (mode == CombatRangeOverrideMode.ForceMelee)
            {
                agent.activeHybridMode = CombatRangeType.Melee;
                agent.lastHybridModeSwitchTime = Time.time;
            }
            else if (mode == CombatRangeOverrideMode.ForceRanged)
            {
                agent.activeHybridMode = CombatRangeType.Ranged;
                agent.lastHybridModeSwitchTime = Time.time;
            }
        }

        private void ApplyFactionCombatRangeOverride(CrowdAgent agent)
        {
            if (agent == null)
                return;

            string typeName = agent.enemyCrowd != null ? agent.enemyCrowd.enemyTypeName : null;
            var typeOverride = GetTypeCombatRangeOverrideMode(typeName);
            if (typeOverride != CombatRangeOverrideMode.Default)
            {
                ApplyCombatRangeOverrideToAgent(agent, typeOverride);
                return;
            }

            ApplyCombatRangeOverrideToAgent(agent, GetFactionCombatRangeOverrideMode(agent.enemyCrowdIndex));
        }

        private void ForceBoneTrackerWorkflow(CrowdAgent agent)
        {
            if (agent == null)
                return;

            _hybridWorkflowSystem.ForceBoneTrackerWorkflow(
                agent,
                DeactivateRagdoll,
                _animationController.CalculateNormalizedSpeed,
                _animationController.StartLocomotionBlend);
        }

        private void DeactivateRagdoll(CrowdAgent agent, bool force)
        {
            _ragdollSystem.DeactivateRagdollPhysics(agent, force);
        }

        private int GetAgentIndexFromInstance(GPUICrowdInstance instance)
        {
            if (instance == null || !_instanceLookup.TryGetValue(instance, out int index))
                return -1;
            return index;
        }

        /// <summary>
        /// Public lookup for mapping GPUICrowdInstance (or its colliders) back to agent index.
        /// Used by external systems (debug zones, skills) in collider-based hit detection.
        /// </summary>
        public int TryGetAgentIndexFromInstance(GPUICrowdInstance instance)
        {
            return GetAgentIndexFromInstance(instance);
        }

        private void HandleAgentKilled(GPUICrowdInstance instance)
        {
            if (!_instanceLookup.TryGetValue(instance, out int agentIndex))
                return;

            if (agentIndex >= 0 && agentIndex < _agents.Count)
            {
                var agent = _agents[agentIndex];
                _performanceManager.RegisterDeadAgent(agent);

                if (agent != null)
                {
                    
                    // ═══ Network Hook: Fire death event for network synchronization ═══
                    // Determine killer info from agent's last attacker tracking
                    int killerIndex = agent.lastDamagedByAgentIndex;
                    Vector3 deathPos = agent.transform != null ? agent.transform.position : Vector3.zero;
                    Vector3 deathDir = agent.hasAttackerOrientation ? agent.lastAttackerForward : Vector3.forward;
                    bool useRagdoll = agent.ragdollPending;
                    
                    FireNetworkDeathEvent(agentIndex, killerIndex, deathPos, deathDir, useRagdoll);
                }

                // Disable components if configured (always call so ragdoll path can disable NavMeshAgent)
                if (_defaultCombatSettings.disableComponentsOnDeath)
                {
                    _performanceManager.DisableDeadAgentComponents(agent, ShouldUseRagdoll);
                }
                
                // Clear any agents targeting this dead agent — prevents stale targets
                // that cause agents to remain in combat state and break formations.
                // Previously only ran in factionBattleMode, but stale targets from manual
                // skill/attack commands also need cleanup.
                ClearTargetsPointingToAgent(agentIndex);
                
                // If the agent died with an active knockback impulse or crash descent,
                // mark the active agents list dirty so SyncAgentMovement processes it
                // on the very next frame instead of waiting up to 30 frames.
                if (agent.hasNavmeshOffsetImpulse || agent.hasAstarKnockbackImpulse || agent.isCrashing)
                {
                    _activeAgentsListDirty = true;
                }
            }

            EventAgentKilled?.Invoke(instance);
        }

        private void HandleAgentHealthChanged(CrowdAgent agent, float newHealth, float maxHealth)
        {
            if (agent == null) return;
            int agentIndex = agent.index;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return;
            EventAgentHealthChanged?.Invoke(agentIndex, newHealth, maxHealth);
            
            // ═══ Network Hook: Fire detailed health change event ═══
            // Note: We don't have previousHealth here, so approximate it
            // For more detailed damage tracking, use NetworkEventDamageApplied instead
            FireNetworkHealthChangeEvent(agentIndex, newHealth, newHealth, maxHealth, HealthChangeSource.Damage);
        }
        
        private void HandlePlayerTargetHit(CrowdAgent attackerAgent, Vector3 attackerPosition, Transform targetTransform, float damage, Vector3 hitPoint, bool isMeleeAttack)
        {
            // Forward the event to external subscribers (like GC2 integration)
            EventPlayerTargetHit?.Invoke(attackerAgent, attackerPosition, targetTransform, damage, hitPoint, isMeleeAttack);
        }

        #endregion

        #region Faction Battle Mode

        /// <summary>
        /// Assigns combat targets to all agents for faction battle mode.
        /// Uses aggro range system - agents only engage enemies within their detection range.
        /// </summary>
        public void AssignFactionBattleTargets()
        {
            int agentCount = _agents.Count;
            if (agentCount < 2) return;

            float currentTime = Time.time;

            for (int i = 0; i < agentCount; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.ragdollActivated)
                    continue;

                bool canTargetPlayer = CanAutoTargetPlayer(agent);

                // CRITICAL: First, clear any player target reference
                // This ensures agents don't fall back to attacking the player
                if (!canTargetPlayer && IsPlayerTargetTransform(agent.attackTargetTransform))
                {
                    agent.isAggroed = false;
                    agent.isChasing = false;
                    ClearAgentAttackTarget(agent, true);
                }

                // Get aggro settings from EnemyCrowd or use defaults
                float aggroRange = agent.enemyCrowd?.aggroRange ?? 50f;
                float aggroLossRange = agent.enemyCrowd?.aggroLossRange ?? 60f;
                float aggroLossDelay = agent.enemyCrowd?.aggroLossDelay ?? 3f;

                // === COMBAT STANCE HANDLING ===
                // Passive: Agents never engage in combat automatically - skip all aggro processing
                // HoldPosition: Agents don't seek new targets, only attack what comes to them
                // Defensive: Agents only engage targets that attacked them
                if (agent.combatStance == CombatStance.Passive)
                {
                    // Passive agents never auto-acquire targets - clear any existing aggro
                    if (agent.isAggroed || agent.attackTargetAgentIndex >= 0 || agent.attackTargetTransform != null)
                    {
                        // Only clear if not under explicit manual attack command
                        if (!agent.HasExplicitAttackIntent)
                        {
                            agent.isAggroed = false;
                            agent.isChasing = false;
                            ClearAgentAttackTarget(agent, true);
                        }
                    }
                    continue; // Skip all further aggro processing for passive agents
                }

                bool canSeekNewTargets = agent.combatStance == CombatStance.Offensive;
                bool canChaseTargets = agent.combatStance != CombatStance.HoldPosition;

                // Check if current target is still valid
                if (agent.attackTargetAgentIndex >= 0)
                {
                    CrowdAgent currentTarget = agent.attackTargetAgentIndex < _agents.Count ? _agents[agent.attackTargetAgentIndex] : null;

                    if (currentTarget == null || currentTarget.isDead || currentTarget.ragdollActivated)
                    {
                        agent.isAggroed = false;
                        agent.isChasing = false;
                        // ClearAgentAttackTarget → ReturnToFormation() handles all cleanup
                        // including commandIntent and hasManualAttackCommand.
                        ClearAgentAttackTarget(agent, true);
                    }
                    else if (agent.HasExplicitAttackIntent)
                    {
                        // Player explicitly commanded this attack — skip all automatic aggro loss logic.
                        // Just keep tracking the target until it dies or a new command is issued.
                        UpdateAgentTargetTracking(agent, currentTarget);
                        continue;
                    }
                    else if (agent.isAggroed)
                    {
                        // Target is still alive - check if still in range
                        Vector3 agentPos = GetAgentPosition(agent);
                        Vector3 targetPos = GetAgentPosition(currentTarget);
                        float distSqr = (targetPos - agentPos).sqrMagnitude;
                        float aggroLossRangeSqr = aggroLossRange * aggroLossRange;

                        // HoldPosition: Don't chase, but keep target if they're close
                        if (!canChaseTargets)
                        {
                            // For HoldPosition, only keep target if they're within attack range (use engagement radius)
                            float holdEngageRange = agent.enemyCrowd?.engagementRadius ?? 15f;
                            float holdEngageRangeSqr = holdEngageRange * holdEngageRange;
                            if (distSqr > holdEngageRangeSqr)
                            {
                                // Target moved out of hold position attack range - drop target
                                agent.isAggroed = false;
                                agent.isChasing = false;
                                ClearAgentAttackTarget(agent, true);
                                continue;
                            }
                            // Target still in range - keep attacking but don't chase
                            agent.isChasing = false;
                            UpdateAgentTargetTracking(agent, currentTarget);
                            continue;
                        }

                        if (distSqr <= aggroLossRangeSqr)
                        {
                            // Target still in range - update tracking and keep target
                            agent.lastTargetInRangeTime = currentTime;
                            UpdateAgentTargetTracking(agent, currentTarget);
                            continue; // Keep current target
                        }
                        else
                        {
                            // Target out of range - check if we should lose aggro
                            float timeOutOfRange = currentTime - agent.lastTargetInRangeTime;
                            if (timeOutOfRange < aggroLossDelay)
                            {
                                // Still within grace period - keep chasing
                                UpdateAgentTargetTracking(agent, currentTarget);
                                continue;
                            }
                            // Grace period expired - fully clear old target and find new one
                            agent.isAggroed = false;
                            agent.isChasing = false;
                            ClearAgentAttackTarget(agent, true);
                        }
                    }
                    else
                    {
                        // Agent has a valid target but is not aggroed and not manually attacking.
                        // This is an orphaned state — clear it so the agent can re-engage or return to formation.
                        ClearAgentAttackTarget(agent, true);
                    }
                }

                if (TryMaintainPlayerTarget(agent, canTargetPlayer, currentTime, aggroLossRange, aggroLossDelay, canChaseTargets))
                {
                    continue;
                }

                // Skip target acquisition for non-offensive stances (unless they can retaliate)
                if (!canSeekNewTargets)
                {
                    // Defensive stance: Only acquire targets that attacked us
                    if (agent.combatStance == CombatStance.Defensive)
                    {
                        // Check if we were recently attacked and have a valid attacker to retaliate against
                        if (agent.lastDamagedByAgentIndex >= 0 && 
                            currentTime - agent.lastDamagedByEnemyTime < 10f)
                        {
                            CrowdAgent attacker = agent.lastDamagedByAgentIndex < _agents.Count ? 
                                                  _agents[agent.lastDamagedByAgentIndex] : null;
                            if (attacker != null && !attacker.isDead && !attacker.ragdollActivated)
                            {
                                // Retaliate against attacker
                                agent.isAggroed = true;
                                agent.aggroAcquiredTime = currentTime;
                                agent.lastTargetInRangeTime = currentTime;
                                agent.isChasing = true;
                                UpdateAgentTargetTracking(agent, attacker);
                                
                                if (agent.navigationState == AgentNavigationState.Formation)
                                {
                                    agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                                }
                                // Allow agents at RTS formation position to break out and chase (Bug #3 fix)
                                else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasRTSFormationPosition)
                                {
                                    agent.brokeFromRTSFormation = true;
                                    agent.hasRTSFormationPosition = false;
                                    agent.hasManualCommand = false;
                                    agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                                }
                                // Allow patrol agents (hasManualCommand but not hasRTSFormationPosition) to break out and chase
                                else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasManualCommand)
                                {
                                    // Don't set brokeFromRTSFormation - patrol agents should resume patrol after combat
                                    agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                                    // IMPORTANT: Re-set hasManualCommand=true AFTER ForceSetNavigationState because it clears the flag
                                    agent.hasManualCommand = true;
                                }
                            }
                        }
                    }
                    // HoldPosition stance: Find targets that come within attack range
                    else if (agent.combatStance == CombatStance.HoldPosition)
                    {
                        float holdEngageRange = agent.enemyCrowd?.engagementRadius ?? 15f;
                        CrowdAgent nearbyTarget = FindFactionBattleTarget(agent, i, holdEngageRange);
                        if (nearbyTarget != null)
                        {
                            agent.isAggroed = true;
                            agent.aggroAcquiredTime = currentTime;
                            agent.lastTargetInRangeTime = currentTime;
                            agent.isChasing = false; // Don't chase!
                            UpdateAgentTargetTracking(agent, nearbyTarget);
                            // Don't change navigation state - stay at position
                        }
                        else if (canTargetPlayer)
                        {
                            float holdEngageRangeSqr = holdEngageRange * holdEngageRange;
                            Vector3 agentPos = GetAgentPosition(agent);
                            Transform nearestPlayerTarget = GetNearestPlayerTarget(agentPos);
                            if (nearestPlayerTarget != null)
                            {
                                float distSqr = (nearestPlayerTarget.position - agentPos).sqrMagnitude;
                                if (distSqr <= holdEngageRangeSqr)
                                {
                                    AssignPlayerTarget(agent, currentTime, false);
                                }
                            }
                        }
                    }
                    continue; // Don't do normal target acquisition
                }

                // Check disengage grace period - skip target acquisition if agent recently received a move command
                // This allows Offensive agents to disengage from combat and retreat when ordered
                if (agent.moveCommandIssuedTime > 0f && (currentTime - agent.moveCommandIssuedTime) < _disengageGracePeriod)
                {
                    continue; // Still in disengage grace period - don't acquire new targets
                }

                // Find a valid target within aggro range (Offensive stance only reaches here)
                CrowdAgent target = FindFactionBattleTarget(agent, i, aggroRange);

                bool usePlayerTarget = false;
                float playerDistSqr = 0f;
                Vector3 agentPosition = Vector3.zero;
                if (canTargetPlayer)
                {
                    agentPosition = GetAgentPosition(agent);
                    Transform nearestPlayerTarget = GetNearestPlayerTarget(agentPosition);
                    if (nearestPlayerTarget != null)
                    {
                        playerDistSqr = (nearestPlayerTarget.position - agentPosition).sqrMagnitude;
                        float aggroRangeSqr = aggroRange * aggroRange;
                        if (playerDistSqr <= aggroRangeSqr)
                        {
                            if (target == null)
                            {
                                usePlayerTarget = true;
                            }
                            else
                            {
                                Vector3 targetPos = GetAgentPosition(target);
                                float targetDistSqr = (targetPos - agentPosition).sqrMagnitude;
                                usePlayerTarget = playerDistSqr < targetDistSqr;
                            }
                        }
                    }
                }

                if (usePlayerTarget)
                {
                    AssignPlayerTarget(agent, currentTime, true);
                }
                else if (target != null)
                {
                    // Acquire aggro on new target
                    agent.isAggroed = true;
                    agent.aggroAcquiredTime = currentTime;
                    agent.lastTargetInRangeTime = currentTime;
                    agent.isChasing = true;
                    
                    UpdateAgentTargetTracking(agent, target);
                    
                    // CRITICAL: Transition to ChasingTarget state so navigation will process this agent
                    if (agent.navigationState == AgentNavigationState.Formation)
                    {
                        agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                    // Allow agents at RTS formation position to break out and chase (Bug #3 fix)
                    else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasRTSFormationPosition)
                    {
                        // Mark that this agent broke out from RTS formation - so it stays put after combat instead of rejoining crowd formation
                        agent.brokeFromRTSFormation = true;
                        // Clear RTS formation position flags so agent can chase freely
                        agent.hasRTSFormationPosition = false;
                        agent.hasManualCommand = false;
                        // Use ForceSetNavigationState to bypass ManualCommand protection
                        agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                    // Allow patrol agents (hasManualCommand but not hasRTSFormationPosition) to break out and chase
                    else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasManualCommand)
                    {
                        // Don't set brokeFromRTSFormation - patrol agents should resume patrol after combat
                        // Use ForceSetNavigationState to bypass ManualCommand protection
                        agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                        // IMPORTANT: Re-set hasManualCommand=true AFTER ForceSetNavigationState because it clears the flag
                        // This allows the RTS patrol system to resume the patrol after combat ends
                        agent.hasManualCommand = true;
                    }
                }
                else
                {
                    // No target in range - clear aggro state
                    if (agent.isAggroed)
                    {
                        agent.isAggroed = false;
                        agent.isChasing = false;
                        ClearAgentAttackTarget(agent, true);
                        
                        // Return to formation if was chasing/in combat (but NOT if agent broke from RTS formation - they stay put)
                        // Also preserve ManualCommand for patrol agents - they should return to ManualCommand, not Formation
                        if ((agent.navigationState == AgentNavigationState.ChasingTarget || 
                             agent.navigationState == AgentNavigationState.CombatOverride) && 
                            !agent.brokeFromRTSFormation)
                        {
                            if (agent.hasManualCommand)
                            {
                                agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                            }
                            else
                            {
                                agent.SetNavigationState(AgentNavigationState.Formation);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZED: Processes faction battle target assignment in batches across multiple frames.
        /// This spreads CPU load over time instead of causing frame spikes.
        /// </summary>
        /// <param name="maxPerFrame">Maximum agents to process this frame</param>
        private void AssignFactionBattleTargetsBatched(int maxPerFrame)
        {
            int agentCount = _agents.Count;
            if (agentCount < 2) return;

            float currentTime = Time.time;
            int processed = 0;

            // Process from current cursor position
            while (processed < maxPerFrame && processed < agentCount)
            {
                // Wrap cursor if needed
                if (_factionTargetUpdateCursor >= agentCount)
                    _factionTargetUpdateCursor = 0;

                int i = _factionTargetUpdateCursor;
                _factionTargetUpdateCursor++;
                processed++;

                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.ragdollActivated)
                    continue;

                // Process single agent (same logic as AssignFactionBattleTargets but for one agent)
                ProcessSingleAgentFactionTarget(agent, i, currentTime);
            }
        }

        /// <summary>
        /// Processes faction battle target logic for a single agent.
        /// Extracted from AssignFactionBattleTargets for batched processing.
        /// </summary>
        private void ProcessSingleAgentFactionTarget(CrowdAgent agent, int agentIndex, float currentTime)
        {
            bool canTargetPlayer = CanAutoTargetPlayer(agent);

            // CRITICAL: First, clear any player target reference
            if (!canTargetPlayer && IsPlayerTargetTransform(agent.attackTargetTransform))
            {
                agent.isAggroed = false;
                agent.isChasing = false;
                ClearAgentAttackTarget(agent, true);
            }

            // Get aggro settings from EnemyCrowd or use defaults
            float aggroRange = agent.enemyCrowd?.aggroRange ?? 50f;
            float aggroLossRange = agent.enemyCrowd?.aggroLossRange ?? 60f;
            float aggroLossDelay = agent.enemyCrowd?.aggroLossDelay ?? 3f;

            // === COMBAT STANCE HANDLING ===
            bool canSeekNewTargets = agent.combatStance == CombatStance.Offensive;
            bool canChaseTargets = agent.combatStance != CombatStance.HoldPosition;

            // Check if current target is still valid
            if (agent.attackTargetAgentIndex >= 0)
            {
                CrowdAgent currentTarget = agent.attackTargetAgentIndex < _agents.Count ? _agents[agent.attackTargetAgentIndex] : null;

                if (currentTarget == null || currentTarget.isDead || currentTarget.ragdollActivated)
                {
                    agent.isAggroed = false;
                    agent.isChasing = false;
                    // NOTE: Do NOT clear hasManualAttackCommand here — same reason as above.
                    // Let the combat system handle it via hasExplicitAttackOrder.
                    ClearAgentAttackTarget(agent, true);
                }
                else if (agent.HasExplicitAttackIntent)
                {
                    // Player explicitly commanded this attack — skip all automatic aggro loss logic.
                    // Just keep tracking the target until it dies or a new command is issued.
                    UpdateAgentTargetTracking(agent, currentTarget);
                    return;
                }
                else if (agent.isAggroed)
                {
                    Vector3 agentPos = GetAgentPosition(agent);
                    Vector3 targetPos = GetAgentPosition(currentTarget);
                    float distSqr = (targetPos - agentPos).sqrMagnitude;
                    float aggroLossRangeSqr = aggroLossRange * aggroLossRange;

                    if (!canChaseTargets)
                    {
                        float holdEngageRange = agent.enemyCrowd?.engagementRadius ?? 15f;
                        float holdEngageRangeSqr = holdEngageRange * holdEngageRange;
                        if (distSqr > holdEngageRangeSqr)
                        {
                            agent.isAggroed = false;
                            agent.isChasing = false;
                            ClearAgentAttackTarget(agent, true);
                            return;
                        }
                        agent.isChasing = false;
                        UpdateAgentTargetTracking(agent, currentTarget);
                        return;
                    }

                    if (distSqr <= aggroLossRangeSqr)
                    {
                        agent.lastTargetInRangeTime = currentTime;
                        UpdateAgentTargetTracking(agent, currentTarget);
                        return;
                    }
                    else
                    {
                        float timeOutOfRange = currentTime - agent.lastTargetInRangeTime;
                        if (timeOutOfRange < aggroLossDelay)
                        {
                            UpdateAgentTargetTracking(agent, currentTarget);
                            return;
                        }
                        // Aggro timed out — fully clear the old target so the agent
                        // doesn't remain stuck with hasAttackTargetPosition=true but
                        // isAggroed=false, which prevents the cleanup path at the
                        // bottom of this method from firing.
                        agent.isAggroed = false;
                        agent.isChasing = false;
                        ClearAgentAttackTarget(agent, true);
                    }
                }
                else
                {
                    // Agent has a valid target but is not aggroed and not manually attacking.
                    // This is an orphaned state — clear it so the agent can re-engage or return to formation.
                    ClearAgentAttackTarget(agent, true);
                }
            }

            if (TryMaintainPlayerTarget(agent, canTargetPlayer, currentTime, aggroLossRange, aggroLossDelay, canChaseTargets))
                return;

            // Skip target acquisition for non-offensive stances
            if (!canSeekNewTargets)
            {
                if (agent.combatStance == CombatStance.Defensive)
                {
                    if (agent.lastDamagedByAgentIndex >= 0 && 
                        currentTime - agent.lastDamagedByEnemyTime < 10f)
                    {
                        CrowdAgent attacker = agent.lastDamagedByAgentIndex < _agents.Count ? 
                                              _agents[agent.lastDamagedByAgentIndex] : null;
                        if (attacker != null && !attacker.isDead && !attacker.ragdollActivated)
                        {
                            agent.isAggroed = true;
                            agent.aggroAcquiredTime = currentTime;
                            agent.lastTargetInRangeTime = currentTime;
                            agent.isChasing = true;
                            UpdateAgentTargetTracking(agent, attacker);
                            
                            if (agent.navigationState == AgentNavigationState.Formation)
                                agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                            // Allow agents at RTS formation position to break out and chase (Bug #3 fix)
                            else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasRTSFormationPosition)
                            {
                                agent.brokeFromRTSFormation = true;
                                agent.hasRTSFormationPosition = false;
                                agent.hasManualCommand = false;
                                agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                            }
                            // Allow patrol agents (hasManualCommand but not hasRTSFormationPosition) to break out and chase
                            else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasManualCommand)
                            {
                                agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                                // IMPORTANT: Re-set hasManualCommand=true AFTER ForceSetNavigationState because it clears the flag
                                agent.hasManualCommand = true;
                            }
                        }
                    }
                }
                else if (agent.combatStance == CombatStance.HoldPosition)
                {
                    float holdEngageRange = agent.enemyCrowd?.engagementRadius ?? 15f;
                    CrowdAgent nearbyTarget = FindFactionBattleTarget(agent, agentIndex, holdEngageRange);
                    if (nearbyTarget != null)
                    {
                        agent.isAggroed = true;
                        agent.aggroAcquiredTime = currentTime;
                        agent.lastTargetInRangeTime = currentTime;
                        agent.isChasing = false;
                        UpdateAgentTargetTracking(agent, nearbyTarget);
                    }
                    else if (canTargetPlayer)
                    {
                        float holdEngageRangeSqr = holdEngageRange * holdEngageRange;
                        Vector3 agentPos = GetAgentPosition(agent);
                        Transform nearestPlayerTarget = GetNearestPlayerTarget(agentPos);
                        if (nearestPlayerTarget != null)
                        {
                            float distSqr = (nearestPlayerTarget.position - agentPos).sqrMagnitude;
                            if (distSqr <= holdEngageRangeSqr)
                            {
                                AssignPlayerTarget(agent, currentTime, false);
                            }
                        }
                    }
                }
                return;
            }

            // Check disengage grace period - skip target acquisition if agent recently received a move command
            // This allows Offensive agents to disengage from combat and retreat when ordered
            if (agent.moveCommandIssuedTime > 0f && (currentTime - agent.moveCommandIssuedTime) < _disengageGracePeriod)
            {
                return; // Still in disengage grace period - don't acquire new targets
            }

            // Find a valid target within aggro range (Offensive stance only)
            CrowdAgent target = FindFactionBattleTarget(agent, agentIndex, aggroRange);

            bool usePlayerTarget = false;
            float playerDistSqr = 0f;
            Vector3 agentPosition = Vector3.zero;
            if (canTargetPlayer)
            {
                agentPosition = GetAgentPosition(agent);
                Transform nearestPlayerTarget = GetNearestPlayerTarget(agentPosition);
                if (nearestPlayerTarget != null)
                {
                    playerDistSqr = (nearestPlayerTarget.position - agentPosition).sqrMagnitude;
                    float aggroRangeSqr = aggroRange * aggroRange;
                    if (playerDistSqr <= aggroRangeSqr)
                    {
                        if (target == null)
                        {
                            usePlayerTarget = true;
                        }
                        else
                        {
                            Vector3 targetPos = GetAgentPosition(target);
                            float targetDistSqr = (targetPos - agentPosition).sqrMagnitude;
                            usePlayerTarget = playerDistSqr < targetDistSqr;
                        }
                    }
                }
            }

            if (usePlayerTarget)
            {
                AssignPlayerTarget(agent, currentTime, true);
            }
            else if (target != null)
            {
                agent.isAggroed = true;
                agent.aggroAcquiredTime = currentTime;
                agent.lastTargetInRangeTime = currentTime;
                agent.isChasing = true;
                
                UpdateAgentTargetTracking(agent, target);
                
                if (agent.navigationState == AgentNavigationState.Formation)
                    agent.SetNavigationState(AgentNavigationState.ChasingTarget);
                // Allow agents at RTS formation position to break out and chase (Bug #3 fix)
                else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasRTSFormationPosition)
                {
                    agent.brokeFromRTSFormation = true;
                    agent.hasRTSFormationPosition = false;
                    agent.hasManualCommand = false;
                    agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                }
                // Allow patrol agents (hasManualCommand but not hasRTSFormationPosition) to break out and chase
                else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasManualCommand)
                {
                    // Don't set brokeFromRTSFormation - patrol agents should resume patrol after combat
                    agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                    // IMPORTANT: Re-set hasManualCommand=true AFTER ForceSetNavigationState because it clears the flag
                    // This allows the RTS patrol system to resume the patrol after combat ends
                    agent.hasManualCommand = true;
                }
            }
            else
            {
                if (agent.isAggroed)
                {
                    agent.isAggroed = false;
                    agent.isChasing = false;
                    ClearAgentAttackTarget(agent, true);
                    
                    // Return to formation if was chasing/in combat (but NOT if agent broke from RTS formation - they stay put)
                    // Also preserve ManualCommand for patrol agents
                    // Note: ClearAgentAttackTarget now handles CombatOverride too, but this is an additional fallback
                    if ((agent.navigationState == AgentNavigationState.ChasingTarget || 
                         agent.navigationState == AgentNavigationState.CombatOverride) && 
                        !agent.brokeFromRTSFormation)
                    {
                        if (agent.hasManualCommand)
                        {
                            agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                        }
                        else
                        {
                            agent.SetNavigationState(AgentNavigationState.Formation);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Helper to update agent's target tracking fields.
        /// </summary>
        private void UpdateAgentTargetTracking(CrowdAgent agent, CrowdAgent target)
        {
            agent.attackTargetAgentIndex = target.index;
            agent.attackTargetTransform = target.navigationAgent?.Transform ?? 
                                          (target.crowdInstance != null ? target.crowdInstance.transform : null);
            agent.hasAttackTargetPosition = true;
            agent.attackTargetPosition = agent.attackTargetTransform != null ? 
                                         agent.attackTargetTransform.position : Vector3.zero;
        }

        private void ClearAgentAttackTarget(CrowdAgent agent, bool resetNavigation)
        {
            if (agent == null)
                return;

            bool hadExplicitAttackIntent = agent.HasExplicitAttackIntent;

            agent.hasAttackTargetPosition = false;
            agent.attackTargetAgentIndex = -1;
            agent.attackTargetTransform = null;
            agent.attackTargetPosition = Vector3.zero;

            if (!resetNavigation)
                return;

            // Handle combat-related nav states. Agents can be in ChasingTarget, CombatOverride,
            // or orphaned ManualCommand (hasManualCommand=false) after target cleared mid-chase.
            bool needsNavReset = agent.navigationState == AgentNavigationState.ChasingTarget ||
                                 agent.navigationState == AgentNavigationState.CombatOverride ||
                                 (agent.navigationState == AgentNavigationState.ManualCommand && !agent.hasManualCommand);

            if (needsNavReset)
            {
                if (agent.hasManualCommand)
                {
                    // If this agent was explicitly ordered to attack (RTS attack command or skill-target chase),
                    // don't resume an old manual destination when the target dies. That causes units to walk
                    // toward stale positions instead of rejoining formation.
                    if (hadExplicitAttackIntent && agent.combatStance != CombatStance.HoldPosition)
                    {
                        agent.ReturnToFormation();
                        _navigationController?.RequestFormationReassignment();
                    }
                    else
                    {
                        // Agent has a valid manual destination — return to ManualCommand
                        if (agent.navigationAgent != null &&
                            (!agent.navigationAgent.Capabilities.RequiresSurfaceValidationForControl || agent.navigationAgent.IsOnNavMesh))
                        {
                            agent.navigationAgent.ResetPath();
                        }
                        agent.hasArrived = true;
                        agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                    }
                }
                else if (agent.brokeFromRTSFormation && agent.hasRTSFormationPosition)
                {
                    // Agent broke from RTS formation AND still has a valid formation position —
                    // return to ManualCommand so they hold at their RTS-assigned position.
                    if (agent.navigationAgent != null &&
                        (!agent.navigationAgent.Capabilities.RequiresSurfaceValidationForControl || agent.navigationAgent.IsOnNavMesh))
                    {
                        agent.navigationAgent.ResetPath();
                    }
                    agent.hasArrived = true;
                    agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                }
                else if (agent.combatStance == CombatStance.HoldPosition)
                {
                    // HoldPosition agents should NOT go to Formation — they must return
                    // to ManualCommand with their hold position. The hold position is
                    // preserved by CommandAgentsToChaseTarget so agents return to their
                    // original position after combat ends.
                    agent.ClearCommandIntent();
                    agent.isAggroed = false;
                    agent.isChasing = false;
                    agent.brokeFromRTSFormation = false;
                    agent.hasRTSFormationPosition = false;
                    agent.hasFormationFacing = false;

                    if (!agent.hasHoldPositionSet)
                    {
                        // Fallback: if hold position was somehow lost, use current position
                        agent.holdPositionLocation = agent.WorldPosition;
                        agent.hasHoldPositionSet = true;
                        Debug.LogWarning($"[RTS FormationTrace] HoldPosition fallback anchor restored: agent={agent.index}, using currentPos={agent.WorldPosition}. This can cause subtle formation drift.");
                    }
                    agent.manualCommandDestination = agent.holdPositionLocation;
                    agent.hasManualCommand = true;
                    agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);

                    // Navigate back to hold position. Agents chased to a combat location
                    // and need to walk back to their original "Hold the Line" position.
                    if (agent.navigationAgent != null &&
                        (!agent.navigationAgent.Capabilities.RequiresSurfaceValidationForControl || agent.navigationAgent.IsOnNavMesh))
                    {
                        if (agent.navigationAgent.IsStopped)
                            agent.navigationAgent.IsStopped = false;

                        bool destSet = agent.navigationAgent.SetDestination(agent.holdPositionLocation);
                        agent.hasArrived = !destSet; // Only mark arrived if we couldn't set destination
                    }
                    else
                    {
                        agent.hasArrived = true;
                    }
                }
                else
                {
                    // No manual command, no valid formation position, not HoldPosition —
                    // use centralized formation recovery.
                    // ReturnToFormation() clears ALL combat/formation flags consistently and ensures
                    // the agent gets a clean Formation state. Then request the navigation controller
                    // to reassign formation positions so agents don't stand idle after combat.
                    agent.ReturnToFormation();
                    _navigationController?.RequestFormationReassignment();
                }
            }

        }

        private bool CanAutoTargetPlayer(CrowdAgent agent)
        {
            if (agent == null || rtsController == null)
                return false;

            if (GetPrimaryPlayerTarget() == null)
                return false;

            if (rtsController.crowdController != null && rtsController.crowdController != this)
                return false;

            if (rtsController.playerControlledFactions == null || rtsController.AllFactionsSelectable)
                return false;

            string factionName = agent.enemyCrowd != null ? agent.enemyCrowd.factionName : null;
            if (string.IsNullOrEmpty(factionName))
                return false;

            return !rtsController.playerControlledFactions.ContainsFaction(factionName);
        }

        private void UpdatePlayerTargetTracking(CrowdAgent agent, Transform target)
        {
            if (target == null)
                return;

            agent.attackTargetAgentIndex = -1;
            agent.attackTargetTransform = target;
            agent.hasAttackTargetPosition = true;
            agent.attackTargetPosition = target.position;
        }

        private void AssignPlayerTarget(CrowdAgent agent, float currentTime, bool canChaseTargets)
        {
            Transform target = GetNearestPlayerTarget(GetAgentPosition(agent));
            if (target == null)
                return;

            agent.isAggroed = true;
            agent.aggroAcquiredTime = currentTime;
            agent.lastTargetInRangeTime = currentTime;
            agent.isChasing = canChaseTargets;
            UpdatePlayerTargetTracking(agent, target);

            if (!canChaseTargets)
                return;

            if (agent.navigationState == AgentNavigationState.Formation)
            {
                agent.SetNavigationState(AgentNavigationState.ChasingTarget);
            }
            else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasRTSFormationPosition)
            {
                agent.brokeFromRTSFormation = true;
                agent.hasRTSFormationPosition = false;
                agent.hasManualCommand = false;
                agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
            }
            // Allow patrol agents (hasManualCommand but not hasRTSFormationPosition) to break out and chase player
            else if (agent.navigationState == AgentNavigationState.ManualCommand && agent.hasManualCommand)
            {
                // Don't set brokeFromRTSFormation - patrol agents should resume patrol after combat
                agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                // IMPORTANT: Re-set hasManualCommand=true AFTER ForceSetNavigationState because it clears the flag
                agent.hasManualCommand = true;
            }
        }

        private bool TryMaintainPlayerTarget(
            CrowdAgent agent,
            bool canTargetPlayer,
            float currentTime,
            float aggroLossRange,
            float aggroLossDelay,
            bool canChaseTargets)
        {
            if (!agent.isAggroed || !canTargetPlayer || agent.attackTargetTransform == null || !IsPlayerTargetTransform(agent.attackTargetTransform))
                return false;

            Vector3 agentPos = GetAgentPosition(agent);
            Transform currentTarget = agent.attackTargetTransform;
            float currentDistSqr = (currentTarget.position - agentPos).sqrMagnitude;

            // Allow retargeting to a closer player target so agents don't stick to the first one forever.
            Transform nearestTarget = GetNearestPlayerTarget(agentPos);
            if (nearestTarget != null && nearestTarget != currentTarget)
            {
                float nearestDistSqr = (nearestTarget.position - agentPos).sqrMagnitude;
                float engageRange = agent.enemyCrowd?.engagementRadius ?? 15f;
                float engageRangeSqr = engageRange * engageRange;
                float allowedRangeSqr = canChaseTargets ? aggroLossRange * aggroLossRange : engageRangeSqr;

                bool nearestInEngage = nearestDistSqr <= engageRangeSqr;
                bool currentInEngage = currentDistSqr <= engageRangeSqr;

                if ((nearestInEngage && !currentInEngage) || (nearestDistSqr < currentDistSqr && nearestDistSqr <= allowedRangeSqr))
                {
                    currentTarget = nearestTarget;
                    currentDistSqr = nearestDistSqr;
                    UpdatePlayerTargetTracking(agent, currentTarget);
                    agent.lastTargetInRangeTime = currentTime;
                }
            }

            if (!canChaseTargets)
            {
                float holdEngageRange = agent.enemyCrowd?.engagementRadius ?? 15f;
                float holdEngageRangeSqr = holdEngageRange * holdEngageRange;
                if (currentDistSqr > holdEngageRangeSqr)
                {
                    agent.isAggroed = false;
                    agent.isChasing = false;
                    ClearAgentAttackTarget(agent, true);
                    return true;
                }

                agent.isChasing = false;
                UpdatePlayerTargetTracking(agent, currentTarget);
                return true;
            }

            float aggroLossRangeSqr = aggroLossRange * aggroLossRange;
            if (currentDistSqr <= aggroLossRangeSqr)
            {
                agent.lastTargetInRangeTime = currentTime;
                UpdatePlayerTargetTracking(agent, currentTarget);
                return true;
            }

            float timeOutOfRange = currentTime - agent.lastTargetInRangeTime;
            if (timeOutOfRange < aggroLossDelay)
            {
                UpdatePlayerTargetTracking(agent, currentTarget);
                return true;
            }

            agent.isAggroed = false;
            agent.isChasing = false;
            return false;
        }

        /// <summary>
        /// Helper to get agent's current world position.
        /// </summary>
        private Vector3 GetAgentPosition(CrowdAgent agent)
        {
            if (agent == null)
                return Vector3.zero;

            if (agent.navigationAgent != null)
                return agent.WorldPosition;

            if (agent.crowdInstance != null)
                return agent.crowdInstance.transform.position;

            return Vector3.zero;
        }

        /// <summary>
        /// Public accessor to get an agent's world position by index for external systems (skills, debug).
        /// </summary>
        public Vector3 GetAgentPosition(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return Vector3.zero;
            var agent = _agents[agentIndex];
            return agent != null ? GetAgentPosition(agent) : Vector3.zero;
        }

        /// <summary>
        /// Finds a valid target for an agent in faction battle mode within the specified aggro range.
        /// PERFORMANCE OPTIMIZED: Uses spatial grid when available for O(k) instead of O(n) lookup,
        /// where k is the number of nearby agents.
        /// </summary>
        private CrowdAgent FindFactionBattleTarget(CrowdAgent agent, int agentIndex, float aggroRange)
        {
            CrowdAgent bestTarget = null;
            float bestDistanceSqr = float.MaxValue;
            float aggroRangeSqr = aggroRange * aggroRange;
            
            Vector3 agentPos = GetAgentPosition(agent);
            
            // Get vertical targeting settings from faction
            float maxHeightDiff = agent.enemyCrowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = agent.enemyCrowd?.requireLineOfSight ?? false;
            LayerMask losLayers = agent.enemyCrowd?.lineOfSightBlockingLayers ?? ~0;
            var losMode = agent.enemyCrowd?.losCheckMode ?? EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly;
            
            // For ClosestCandidatesOnly mode: collect candidates first, then LOS check only top N
            List<(CrowdAgent target, float distSqr, Vector3 pos)> losCandidates = null;
            if (requireLOS && losMode == EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly)
            {
                losCandidates = new List<(CrowdAgent, float, Vector3)>(8);
            }

            // PERFORMANCE: Use spatial grid for fast proximity lookup when available
            if (_spatialGrid != null && performanceSettings.enableSpatialGrid)
            {
                // Get nearby agents from spatial grid - O(k) instead of O(n)
                List<int> nearbyAgents = _spatialGrid.GetAgentsInRadius(agentPos, aggroRange);
                
                for (int i = 0; i < nearbyAgents.Count; i++)
                {
                    int targetIndex = nearbyAgents[i];
                    if (targetIndex == agentIndex) continue; // Don't target self
                    if (targetIndex < 0 || targetIndex >= _agents.Count) continue;
                    
                    CrowdAgent potentialTarget = _agents[targetIndex];
                    if (potentialTarget == null || potentialTarget.isDead || potentialTarget.ragdollActivated)
                        continue;

                    // Check faction relationship rules
                    if (factionBattleType == FactionBattleType.FactionVsFaction)
                    {
                        // Only target agents that the relationship system considers hostile
                        if (!FactionQuery.ShouldAttack(agent, potentialTarget))
                            continue;
                    }
                    else
                    {
                        // AllVsAll mode: still respect ally/self relationships
                        if (FactionQuery.IsFriendly(agent, potentialTarget))
                            continue;
                    }
                    
                    // FLYING IMMUNITY CHECK: Skip flying targets if agent has no anti-air capability
                    if (potentialTarget.isFlying && agent.enemyCrowd != null && !agent.enemyCrowd.HasAntiAirCapability)
                        continue;

                    // Find closest target WITHIN AGGRO RANGE
                    Vector3 targetPos = GetAgentPosition(potentialTarget);
                    float distSqr = (targetPos - agentPos).sqrMagnitude;
                    
                    // AGGRO RANGE CHECK: Only consider targets within detection range
                    if (distSqr > aggroRangeSqr)
                        continue;
                    
                    // VERTICAL HEIGHT CHECK: Skip targets too far above/below (CHEAP - always run)
                    if (maxHeightDiff > 0f)
                    {
                        float heightDiff = Mathf.Abs(targetPos.y - agentPos.y);
                        if (heightDiff > maxHeightDiff)
                            continue;
                    }
                    
                    // LINE OF SIGHT CHECK with performance modes
                    if (requireLOS)
                    {
                        if (losMode == EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly)
                        {
                            // Defer LOS check - collect candidates first
                            losCandidates.Add((potentialTarget, distSqr, targetPos));
                            continue;
                        }
                        else if (losMode == EnemyCrowd.LOSCheckMode.Probabilistic)
                        {
                            // 25% chance to check LOS (amortized cost)
                            if (UnityEngine.Random.value < 0.75f)
                            {
                                // Skip LOS check this time - accept target if closest
                                if (distSqr < bestDistanceSqr)
                                {
                                    bestDistanceSqr = distSqr;
                                    bestTarget = potentialTarget;
                                }
                                continue;
                            }
                        }
                        
                        // EveryCheck mode or Probabilistic rolled to check
                        Vector3 rayStart = agentPos + Vector3.up * 1f;
                        Vector3 rayEnd = targetPos + Vector3.up * 1f;
                        Vector3 rayDir = rayEnd - rayStart;
                        float rayDist = rayDir.magnitude;
                        
                        if (Physics.Raycast(rayStart, rayDir.normalized, rayDist, losLayers, QueryTriggerInteraction.Ignore))
                            continue; // Blocked by obstacle
                    }
                    
                    if (distSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distSqr;
                        bestTarget = potentialTarget;
                    }
                }
            }
            else
            {
                // Fallback: Linear search through all agents (O(n))
                int agentCount = _agents.Count;
                for (int i = 0; i < agentCount; i++)
                {
                    if (i == agentIndex) continue; // Don't target self
                    
                    CrowdAgent potentialTarget = _agents[i];
                    if (potentialTarget == null || potentialTarget.isDead || potentialTarget.ragdollActivated)
                        continue;

                    // Check faction relationship rules
                    if (factionBattleType == FactionBattleType.FactionVsFaction)
                    {
                        // Only target agents that the relationship system considers hostile
                        if (!FactionQuery.ShouldAttack(agent, potentialTarget))
                            continue;
                    }
                    else
                    {
                        // AllVsAll mode: still respect ally/self relationships
                        if (FactionQuery.IsFriendly(agent, potentialTarget))
                            continue;
                    }
                    
                    // FLYING IMMUNITY CHECK: Skip flying targets if agent has no anti-air capability
                    if (potentialTarget.isFlying && agent.enemyCrowd != null && !agent.enemyCrowd.HasAntiAirCapability)
                        continue;

                    // Find closest target WITHIN AGGRO RANGE
                    Vector3 targetPos = GetAgentPosition(potentialTarget);
                    float distSqr = (targetPos - agentPos).sqrMagnitude;
                    
                    // AGGRO RANGE CHECK: Only consider targets within detection range
                    if (distSqr > aggroRangeSqr)
                        continue;
                    
                    // VERTICAL HEIGHT CHECK: Skip targets too far above/below (CHEAP - always run)
                    if (maxHeightDiff > 0f)
                    {
                        float heightDiff = Mathf.Abs(targetPos.y - agentPos.y);
                        if (heightDiff > maxHeightDiff)
                            continue;
                    }
                    
                    // LINE OF SIGHT CHECK with performance modes
                    if (requireLOS)
                    {
                        if (losMode == EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly)
                        {
                            // Defer LOS check - collect candidates first
                            losCandidates.Add((potentialTarget, distSqr, targetPos));
                            continue;
                        }
                        else if (losMode == EnemyCrowd.LOSCheckMode.Probabilistic)
                        {
                            // 25% chance to check LOS
                            if (UnityEngine.Random.value < 0.75f)
                            {
                                if (distSqr < bestDistanceSqr)
                                {
                                    bestDistanceSqr = distSqr;
                                    bestTarget = potentialTarget;
                                }
                                continue;
                            }
                        }
                        
                        Vector3 rayStart = agentPos + Vector3.up * 1f;
                        Vector3 rayEnd = targetPos + Vector3.up * 1f;
                        Vector3 rayDir = rayEnd - rayStart;
                        float rayDist = rayDir.magnitude;
                        
                        if (Physics.Raycast(rayStart, rayDir.normalized, rayDist, losLayers, QueryTriggerInteraction.Ignore))
                            continue; // Blocked by obstacle
                    }
                    
                    if (distSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distSqr;
                        bestTarget = potentialTarget;
                    }
                }
            }
            
            // ClosestCandidatesOnly: Sort candidates by distance and LOS check only top 3
            if (requireLOS && losMode == EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly && losCandidates != null && losCandidates.Count > 0)
            {
                // Sort by distance (closest first)
                losCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));
                
                // Only raycast top 3 closest candidates
                int checksToPerform = Mathf.Min(3, losCandidates.Count);
                for (int i = 0; i < checksToPerform; i++)
                {
                    var candidate = losCandidates[i];
                    Vector3 rayStart = agentPos + Vector3.up * 1f;
                    Vector3 rayEnd = candidate.pos + Vector3.up * 1f;
                    Vector3 rayDir = rayEnd - rayStart;
                    float rayDist = rayDir.magnitude;
                    
                    if (!Physics.Raycast(rayStart, rayDir.normalized, rayDist, losLayers, QueryTriggerInteraction.Ignore))
                    {
                        // Found valid target with LOS
                        return candidate.target;
                    }
                }
                // All top candidates blocked - no valid target
                return null;
            }

            return bestTarget;
        }

        /// <summary>
        /// Clears all agent targets that point to a specific dead agent.
        /// </summary>
        private void ClearTargetsPointingToAgent(int deadAgentIndex)
        {
            int retargetedCount = 0;
            int clearedCount = 0;
            int agentCount = _agents.Count;
            for (int i = 0; i < agentCount; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead)
                    continue;

                if (agent.attackTargetAgentIndex == deadAgentIndex)
                {
                    // Get aggro range for this agent
                    float aggroRange = agent.enemyCrowd?.aggroRange ?? 50f;
                    
                    // IMMEDIATELY find a new target instead of waiting for interval
                    CrowdAgent newTarget = FindFactionBattleTarget(agent, i, aggroRange);
                    if (newTarget != null)
                    {
                        // Assign new target immediately
                        agent.attackTargetAgentIndex = newTarget.index;
                        agent.attackTargetTransform = newTarget.navigationAgent?.Transform ?? 
                                                      (newTarget.crowdInstance != null ? newTarget.crowdInstance.transform : null);
                        agent.hasAttackTargetPosition = true;
                        agent.attackTargetPosition = agent.attackTargetTransform != null ? 
                                                     agent.attackTargetTransform.position : Vector3.zero;
                        
                        // Update aggro tracking
                        agent.isAggroed = true;
                        agent.isChasing = true;
                        agent.lastTargetInRangeTime = Time.time;
                        
                        // Ensure navigation state is ChasingTarget — must handle ALL possible states,
                        // not just Formation. Agents can be stuck in ManualCommand or CombatOverride
                        // from previous combat that the aggro system or combat system left them in.
                        if (agent.navigationState != AgentNavigationState.ChasingTarget)
                        {
                            agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                        }

                        retargetedCount++;
                    }
                    else
                    {
                        // No target available - clear and wait
                        agent.isAggroed = false;
                        agent.isChasing = false;
                        ClearAgentAttackTarget(agent, true);
                        clearedCount++;
                    }
                }
            }

        }

        /// <summary>
        /// Gets the number of alive agents in a specific faction.
        /// </summary>
        public int GetAliveAgentCountForFaction(int factionIndex)
        {
            int count = 0;
            foreach (var agent in _agents)
            {
                if (agent != null && !agent.isDead && agent.enemyCrowdIndex == factionIndex)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Gets the total number of alive agents across all factions.
        /// </summary>
        public int GetTotalAliveAgentCount()
        {
            int count = 0;
            foreach (var agent in _agents)
            {
                if (agent != null && !agent.isDead)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Checks if there is a faction battle winner (only one faction has agents left).
        /// Returns the winning faction index, or -1 if battle is still ongoing or no winner.
        /// </summary>
        public int GetFactionBattleWinner()
        {
            if (!factionBattleMode) return -1;

            int winningFaction = -1;
            int factionsWithAgents = 0;

            for (int i = 0; i < enemyCrowds.Count; i++)
            {
                if (GetAliveAgentCountForFaction(i) > 0)
                {
                    winningFaction = i;
                    factionsWithAgents++;
                }
            }

            // If exactly one faction has agents, that's the winner
            return factionsWithAgents == 1 ? winningFaction : -1;
        }

        /// <summary>
        /// Starts a faction battle with the current spawned agents.
        /// </summary>
        public void StartFactionBattle()
        {
            factionBattleMode = true;
            _nextFactionBattleTargetUpdate = 0f; // Trigger immediate target assignment
        }

        /// <summary>
        /// Stops the faction battle mode.
        /// </summary>
        public void StopFactionBattle()
        {
            factionBattleMode = false;
            
            // Clear all agent-to-agent targets
            foreach (var agent in _agents)
            {
                if (agent == null) continue;
                
                // Only clear faction-battle (agent-to-agent) targets, preserve player targets
                if (agent.attackTargetAgentIndex >= 0)
                {
                    agent.attackTargetAgentIndex = -1;
                    // Only clear hasAttackTargetPosition if there's no player Transform target
                    if (agent.attackTargetTransform == null)
                    {
                        agent.hasAttackTargetPosition = false;
                    }
                }
            }
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            if (_combatSystem != null)
            {
                _combatSystem.OnAgentKilled -= HandleAgentKilled;
                _combatSystem.OnAgentHealthChanged -= HandleAgentHealthChanged;
                _combatSystem.OnPlayerTargetHit -= HandlePlayerTargetHit;
            }

            if (_performanceManager != null)
            {
                _performanceManager.RestoreAllCollisionSettings();
                _performanceManager.ClearSpatialGrid();
                _performanceManager.ClearDeadAgents();
            }

            _agents.Clear();
            _instanceLookup.Clear();

            SkillVFXManager.ResetBudgets();

            _initialized = false;
        }

        #endregion

        #region Debug & Gizmos
        
        [Header("Debug Visualization")]
        [Tooltip("Master toggle to disable ALL gizmo drawing for better Editor performance")]
        [SerializeField] private bool _enableGizmos = true;
        
        [Tooltip("Maximum agents to show hitbox gizmos for (performance limit). Gizmos are controlled per-faction in EnemyCrowd settings.")]
        [SerializeField] private int _maxHitboxGizmosCount = 50;
        
        [Tooltip("Use wireframe mode for hitbox gizmos (better visibility when many agents)")]
        [SerializeField] private bool _hitboxGizmoWireframe = false;
        
        // Always draw hitbox gizmos based on per-faction settings
        private void OnDrawGizmos()
        {
            if (!_enableGizmos || !Application.isPlaying || !_initialized)
                return;
            
            DrawVirtualHitboxGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_enableGizmos || !Application.isPlaying || !_initialized)
                return;

            // Draw agent positions
            Gizmos.color = Color.green;
            foreach (var agent in _agents)
            {
                if (agent?.navigationAgent != null && !agent.isDead)
                {
                    Gizmos.DrawWireSphere(agent.WorldPosition, 0.5f);
                }
            }

            // Draw dead agents
            Gizmos.color = Color.red;
            foreach (var agent in _agents)
            {
                if (agent?.navigationAgent != null && agent.isDead)
                {
                    Gizmos.DrawWireSphere(agent.WorldPosition, 0.3f);
                }
            }
        }
        
        private void DrawVirtualHitboxGizmos()
        {
            int drawn = 0;
            
            foreach (var agent in _agents)
            {
                if (agent?.navigationAgent == null || agent.isDead)
                    continue;
                
                if (drawn >= _maxHitboxGizmosCount)
                    break;
                
                var faction = agent.enemyCrowd;
                // Only check the per-faction setting - no controller toggle needed
                if (faction == null || !faction.virtualHitbox.enabled || !faction.virtualHitbox.showDebugGizmos)
                    continue;
                
                VirtualHitboxGizmoVisualizer.DrawAgentHitbox(
                    agent.WorldPosition,
                    faction.virtualHitbox,
                    faction.virtualHitbox.debugGizmoAlpha,
                    _hitboxGizmoWireframe
                );
                
                drawn++;
            }
        }

        private void MaybeLogAnimationDebug()
        {
            if (!_debugAnimationFlickerLogs || !Application.isPlaying)
                return;

            int agentIndex = ResolveDebugAgentIndex();
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null)
                return;

            INavigationAgent nav = agent.navigationAgent;
            Vector3 destination = nav != null ? nav.Destination : Vector3.zero;
            bool hasPath = nav != null && nav.HasPath;
            bool pathPending = nav != null && nav.PathPending;
            bool isStopped = nav != null && nav.IsStopped;
            bool isOnNavMesh = nav != null && nav.IsOnNavMesh;
            float remainingDistance = nav != null ? nav.RemainingDistance : 0f;
            float desiredSpeed = nav != null ? nav.DesiredVelocity.magnitude : 0f;
            float velocitySpeed = nav != null ? nav.Velocity.magnitude : 0f;
            float normalizedSpeed = _animationController != null
                ? _animationController.CalculateNormalizedSpeed(agent)
                : (nav != null && nav.Speed > 0f ? velocitySpeed / nav.Speed : 0f);

            GPUIPrefab prefab = agent.prefabComponent;
            bool isInstanced = prefab != null && prefab.IsInstanced;
            int bufferIndex = prefab != null ? prefab.bufferIndex : -1;
            int renderKey = prefab != null ? prefab.renderKey : 0;
            int workflowId = agent.crowdInstance != null ? agent.crowdInstance.AnimatorWorkflowID : -1;

            DebugAnimationSnapshot snapshot = new DebugAnimationSnapshot
            {
                animationState = agent.currentState,
                combatState = agent.combatState,
                navigationState = agent.navigationState,
                workflow = agent.currentWorkflow,
                isDead = agent.isDead,
                isVisible = agent.isVisible,
                needsAnimationRefresh = agent.needsAnimationRefresh,
                hasPath = hasPath,
                pathPending = pathPending,
                isStopped = isStopped,
                hasArrived = agent.hasArrived,
                destinationGeneration = agent.destinationGeneration,
                destination = destination,
                lastDestination = agent.lastDestination,
                remainingDistance = remainingDistance,
                normalizedSpeed = normalizedSpeed,
                desiredSpeed = desiredSpeed,
                velocitySpeed = velocitySpeed,
                isInstanced = isInstanced,
                bufferIndex = bufferIndex,
                renderKey = renderKey,
                workflowId = workflowId
            };

            bool hasChange = !_hasAnimationDebugSnapshot || snapshot.HasMeaningfulChange(_lastAnimationDebugSnapshot);
            float interval = Mathf.Max(0f, _debugAnimationLogInterval);
            bool timeReady = interval <= 0f || Time.time >= _nextAnimationDebugLogTime;

            if (_debugAnimationLogOnlyOnChange && !hasChange)
                return;

            if (!timeReady && !hasChange)
                return;

            if (timeReady)
                _nextAnimationDebugLogTime = Time.time + interval;

            _lastAnimationDebugSnapshot = snapshot;
            _hasAnimationDebugSnapshot = true;

            string factionName = agent.enemyCrowd != null ? agent.enemyCrowd.factionName : "Faction";
            string typeName = agent.enemyCrowd != null ? agent.enemyCrowd.enemyTypeName : "Type";

            LogDebug(
                $"[EnemyMasses][AnimDebug] t={Time.time:0.00} agent={agent.index} " +
                $"faction={factionName} type={typeName} " +
                $"state={agent.currentState} combat={agent.combatState} nav={agent.navigationState} " +
                $"wf={agent.currentWorkflow} wfId={workflowId} vis={agent.isVisible} refresh={agent.needsAnimationRefresh} dead={agent.isDead} " +
                $"speedNorm={normalizedSpeed:0.00} desired={desiredSpeed:0.00} vel={velocitySpeed:0.00} onNav={isOnNavMesh} " +
                $"hasPath={hasPath} pending={pathPending} stopped={isStopped} remain={remainingDistance:0.00} arrived={agent.hasArrived} " +
                $"destGen={agent.destinationGeneration} dest={destination} lastDest={agent.lastDestination} " +
                $"gpui={isInstanced} key={renderKey} buffer={bufferIndex}",
                this);
        }

        private int ResolveDebugAgentIndex()
        {
            if (_debugAnimationUseSelectedAgent && rtsController != null)
            {
                var selected = rtsController.SelectedAgents;
                if (selected != null && selected.Count > 0)
                    return selected[0];
            }

            return _debugAnimationAgentIndex;
        }

        #endregion

        #region Click-to-Move Runtime

        /// <summary>
        /// Gets mouse button down state - works with both legacy Input and new Input System.
        /// </summary>
        private bool GetMouseButtonDown()
        {
            // Try new Input System first (via reflection to avoid assembly reference)
            var mouseType = System.Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            if (mouseType != null)
            {
                try
                {
                    var currentProperty = mouseType.GetProperty("current");
                    if (currentProperty != null)
                    {
                        var mouse = currentProperty.GetValue(null);
                        if (mouse != null)
                        {
                            var leftButtonProperty = mouseType.GetProperty("leftButton");
                            if (leftButtonProperty != null)
                            {
                                var leftButton = leftButtonProperty.GetValue(mouse);
                                var wasPressedThisFrameMethod = leftButton?.GetType().GetProperty("wasPressedThisFrame");
                                if (wasPressedThisFrameMethod != null)
                                {
                                    return (bool)wasPressedThisFrameMethod.GetValue(leftButton);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fall through to legacy input
                }
            }
            
            // Fallback to legacy Input (will throw error if active input is set to new system only)
            try
            {
                return Input.GetMouseButtonDown(0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets mouse position - works with both legacy Input and new Input System.
        /// </summary>
        private Vector3 GetMousePosition()
        {
            // Try new Input System first
            var mouseType = System.Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            if (mouseType != null)
            {
                try
                {
                    var currentProperty = mouseType.GetProperty("current");
                    if (currentProperty != null)
                    {
                        var mouse = currentProperty.GetValue(null);
                        if (mouse != null)
                        {
                            var positionProperty = mouseType.GetProperty("position");
                            if (positionProperty != null)
                            {
                                var position = positionProperty.GetValue(mouse);
                                var readValueMethod = position?.GetType().GetMethod("ReadValue");
                                if (readValueMethod != null)
                                {
                                    var vec2 = (UnityEngine.Vector2)readValueMethod.Invoke(position, null);
                                    return new Vector3(vec2.x, vec2.y, 0);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fall through to legacy input
                }
            }
            
            // Fallback to legacy Input
            try
            {
                return Input.mousePosition;
            }
            catch
            {
                return Vector3.zero;
            }
        }

        /// <summary>
        /// Handles mouse click input for setting agent destinations at runtime.
        /// Works in Game View during play mode.
        /// Supports both legacy Input and new Input System.
        /// </summary>
        private void HandleClickToMove()
        {
            // Check for left mouse button click (works with both input systems)
            bool leftClickDown = GetMouseButtonDown();
            Vector3 mousePosition = GetMousePosition();
            
            if (leftClickDown)
            {
                // Get main camera
                Camera cam = Camera.main;
                if (cam == null)
                {
                    Debug.LogWarning("[EnemyMasses] Click-to-Move: No main camera found!");
                    return;
                }

                // Cast ray from mouse position
                Ray ray = cam.ScreenPointToRay(mousePosition);
                
                // Try physics raycast first
                Vector3 targetPoint;
                if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                {
                    targetPoint = hit.point;
                    LogDebug($"[EnemyMasses] Click-to-Move: Hit {hit.collider.name} at {targetPoint}");
                }
                else
                {
                    // No physics hit, use far point
                    targetPoint = ray.origin + ray.direction * 1000f;
                    LogDebug($"[EnemyMasses] Click-to-Move: No physics hit, using far point");
                }

                // Sample navigation fallback surface near clicked point.
                if (EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(targetPoint, 10f, out Vector3 sampledPoint))
                {
                    // Set destination based on selected faction
                    if (clickToMoveFactionIndex < 0)
                    {
                        // Move all factions
                        SetDestination(sampledPoint);
                        LogDebug($"[EnemyMasses] [OK] Click-to-Move: Set destination to {sampledPoint} for ALL {_agents.Count} agents");
                    }
                    else if (clickToMoveFactionIndex < enemyCrowds.Count)
                    {
                        // Move specific faction
                        SetDestinationForFaction(clickToMoveFactionIndex, sampledPoint);
                        string factionDisplayName = enemyCrowds[clickToMoveFactionIndex]?.factionName ?? $"Faction {clickToMoveFactionIndex}";
                        int factionAgentCount = _agents.Count(a => a != null && !a.isDead && a.enemyCrowdIndex == clickToMoveFactionIndex);
                        LogDebug($"[EnemyMasses] [OK] Click-to-Move: Set destination to {sampledPoint} for faction '{factionDisplayName}' ({factionAgentCount} agents)");
                    }
                    else
                    {
                        Debug.LogWarning($"[EnemyMasses] [X] Click-to-Move: Invalid faction index {clickToMoveFactionIndex}. Valid range: -1 (all) or 0-{enemyCrowds.Count - 1}");
                    }
                    
                    // Draw debug visualization with faction-specific color
                    Color debugColor = clickToMoveFactionIndex < 0 ? Color.cyan : 
                        (clickToMoveFactionIndex < enemyCrowds.Count && enemyCrowds[clickToMoveFactionIndex] != null ? 
                         enemyCrowds[clickToMoveFactionIndex].factionColor : Color.white);
                    Debug.DrawLine(sampledPoint, sampledPoint + Vector3.up * 5f, debugColor, 5f);
                    Debug.DrawLine(sampledPoint + Vector3.left, sampledPoint + Vector3.right, debugColor, 5f);
                    Debug.DrawLine(sampledPoint + Vector3.forward, sampledPoint + Vector3.back, debugColor, 5f);
                }
                else
                {
                    Debug.LogWarning($"[EnemyMasses] [X] Click-to-Move: No NavMesh found near {targetPoint}");
                }
            }
        }

        #endregion
        
        #region Network Event Helpers
        
        /// <summary>
        /// Fires the network spawn event for newly spawned agents.
        /// Called internally after successful spawn.
        /// </summary>
        private void FireNetworkSpawnEvent(List<int> spawnedIndices, int factionIndex, Vector3 spawnPosition, Quaternion spawnRotation, FormationType formation, int ownerPlayerId = -1)
        {
            if (NetworkEventAgentSpawned == null) return;
            
            int count = spawnedIndices.Count;
            var positions = new Vector3[count];
            var rotations = new Quaternion[count];
            var networkIds = new uint[count];
            var agentIndices = new int[count];
            
            // Assign network IDs if spawn authority is available
            bool isNetworkMode = NetworkAuthorityProvider.IsNetworkMode;
            var spawnAuthority = NetworkAuthorityProvider.SpawnAuthority;
            int spawnGroupId = isNetworkMode ? GetNextSpawnGroupId() : -1;
            
            for (int i = 0; i < count; i++)
            {
                int agentIndex = spawnedIndices[i];
                agentIndices[i] = agentIndex;
                
                var agent = _agents[agentIndex];
                if (agent?.transform != null)
                {
                    positions[i] = agent.transform.position;
                    rotations[i] = agent.transform.rotation;
                    
                    // Assign network ID and register lookup
                    if (isNetworkMode && spawnAuthority != null && spawnAuthority.IsServer)
                    {
                        uint netId = AllocateNetworkId();
                        agent.networkId = netId;
                        agent.isNetworkAuthoritative = true;
                        agent.spawnGroupId = spawnGroupId;
                        _networkIdLookup[netId] = agentIndex;
                        networkIds[i] = netId;
                    }
                    
                    // Set owner if provided
                    if (ownerPlayerId >= 0)
                    {
                        agent.SetOwnerAuthoritative(ownerPlayerId);
                    }
                }
            }
            
            var eventData = new SpawnEventData
            {
                agentIndices = agentIndices,
                networkIds = networkIds,
                factionIndex = factionIndex,
                positions = positions,
                rotations = rotations,
                ownerPlayerId = ownerPlayerId,
                spawnGroupId = spawnGroupId,
                formation = formation,
                timestamp = Time.time
            };
            
            NetworkEventAgentSpawned.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires the network damage event when an agent takes damage.
        /// Called from combat system after damage is applied.
        /// </summary>
        public void FireNetworkDamageEvent(int targetIndex, int attackerIndex, float damage, float previousHealth, float newHealth, Vector3 hitPoint, Vector3 attackDirection, bool wasBlocked, bool isDead, DamageType damageType = DamageType.Melee)
        {
            if (NetworkEventDamageApplied == null) return;
            
            var target = GetAgent(targetIndex);
            var attacker = attackerIndex >= 0 ? GetAgent(attackerIndex) : null;
            
            var eventData = new DamageEventData
            {
                targetIndex = targetIndex,
                targetNetworkId = target?.networkId ?? 0,
                attackerIndex = attackerIndex,
                attackerNetworkId = attacker?.networkId ?? 0,
                damage = damage,
                previousHealth = previousHealth,
                newHealth = newHealth,
                hitPoint = hitPoint,
                attackDirection = attackDirection,
                wasBlocked = wasBlocked,
                isDead = isDead,
                damageType = damageType,
                timestamp = Time.time
            };
            
            NetworkEventDamageApplied.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires the network death event when an agent dies.
        /// Called from combat system after death is confirmed.
        /// </summary>
        public void FireNetworkDeathEvent(int agentIndex, int killerIndex, Vector3 deathPosition, Vector3 deathDirection, bool useRagdoll)
        {
            if (NetworkEventAgentDied == null) return;
            
            var agent = GetAgent(agentIndex);
            var killer = killerIndex >= 0 ? GetAgent(killerIndex) : null;
            
            var eventData = new DeathEventData
            {
                agentIndex = agentIndex,
                networkId = agent?.networkId ?? 0,
                killerIndex = killerIndex,
                killerNetworkId = killer?.networkId ?? 0,
                killerPlayerId = killer?.ownerPlayerId ?? -1,
                deathPosition = deathPosition,
                deathDirection = deathDirection,
                factionIndex = agent?.enemyCrowdIndex ?? -1,
                ownerPlayerId = agent?.ownerPlayerId ?? -1,
                useRagdoll = useRagdoll,
                timestamp = Time.time
            };
            
            NetworkEventAgentDied.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires the network health change event.
        /// More detailed than EventAgentHealthChanged.
        /// </summary>
        public void FireNetworkHealthChangeEvent(int agentIndex, float previousHealth, float newHealth, float maxHealth, HealthChangeSource source)
        {
            if (NetworkEventHealthChanged == null) return;
            
            var agent = GetAgent(agentIndex);
            
            var eventData = new HealthChangeEventData
            {
                agentIndex = agentIndex,
                networkId = agent?.networkId ?? 0,
                previousHealth = previousHealth,
                newHealth = newHealth,
                maxHealth = maxHealth,
                changeAmount = newHealth - previousHealth,
                source = source,
                timestamp = Time.time
            };
            
            NetworkEventHealthChanged.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires the network state change event when combat or navigation state changes.
        /// </summary>
        public void FireNetworkStateChangeEvent(int agentIndex, CrowdCombatState prevCombat, CrowdCombatState newCombat, AgentNavigationState prevNav, AgentNavigationState newNav)
        {
            if (NetworkEventStateChanged == null) return;
            
            var agent = GetAgent(agentIndex);
            
            var eventData = new StateChangeEventData
            {
                agentIndex = agentIndex,
                networkId = agent?.networkId ?? 0,
                previousCombatState = prevCombat,
                newCombatState = newCombat,
                previousNavState = prevNav,
                newNavState = newNav,
                timestamp = Time.time
            };
            
            NetworkEventStateChanged.Invoke(eventData);
        }
        
        /// <summary>
        /// Fires the network despawn event when agents are removed.
        /// </summary>
        public void FireNetworkDespawnEvent(uint[] networkIds, DespawnReason reason)
        {
            if (NetworkEventAgentsDespawned == null || networkIds == null || networkIds.Length == 0) return;
            
            NetworkEventAgentsDespawned.Invoke(networkIds, reason);
        }
        
        #endregion
    }
}
