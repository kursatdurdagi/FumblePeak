using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using EnemyMasses.VFX;
using Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Skills;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// High-performance projectile simulation system for EnemyMasses.
    /// Supports thousands of simultaneous projectiles using mathematical simulation
    /// instead of Unity physics for maximum efficiency.
    /// 
    /// Features:
    /// - Linear projectiles (arrows, bullets, magic bolts)
    /// - Parabolic projectiles (grenades, thrown weapons)
    /// - Object pooling for visual representations
    /// - Batch processing with Jobs system
    /// - Optional VFX trails
    /// </summary>
    public class EnemyMassesProjectileSystem : MonoBehaviour
    {
        #region Singleton
        
        private static EnemyMassesProjectileSystem _instance;
        public static EnemyMassesProjectileSystem Instance => _instance;
        
        #endregion
        
        #region Configuration
        
        [Header("Pool Settings")]
        [Tooltip("Maximum number of active projectiles")]
        [SerializeField] private int maxProjectiles = 2000;
        
        [Tooltip("Default projectile prefab for linear projectiles (arrow, bullet)")]
        [SerializeField] private GameObject defaultLinearProjectilePrefab;
        
        [Tooltip("Default projectile prefab for parabolic projectiles (grenade, thrown)")]
        [SerializeField] private GameObject defaultParabolicProjectilePrefab;
        
        [Header("Physics Settings")]
        [Tooltip("Gravity acceleration for parabolic projectiles")]
        [SerializeField] private float gravity = 9.81f;
        
        [Tooltip("Layer mask for hit detection")]
        [SerializeField] private LayerMask hitLayerMask = ~0;
        
        [Tooltip("Radius for projectile hit detection")]
        [SerializeField] private float hitDetectionRadius = 0.5f;
        
        [Header("Performance Settings")]
        [Tooltip("Use Jobs system for parallel simulation")]
        [SerializeField] private bool useJobsSystem = true;
        
        [Tooltip("Maximum raycasts per frame for hit detection")]
        [SerializeField] private int maxRaycastsPerFrame = 100;
        
        [Tooltip("Skip visual updates for projectiles too far from camera")]
        [SerializeField] private float visualCullDistance = 100f;
        
        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool drawDebugGizmos = false;
        
        #endregion
        
        #region Projectile Data Structures
        
        /// <summary>
        /// Compact struct for projectile simulation data.
        /// Designed for cache efficiency and Jobs compatibility.
        /// </summary>
        public struct ProjectileData
        {
            // Position and velocity
            public float3 position;
            public float3 velocity;
            public float3 startPosition;
            public float3 targetPosition;
            
            // Timing
            public float spawnTime;
            public float lifetime;
            public float flightTime; // For parabolic: calculated flight time
            public float simulationGravity; // Per-projectile gravity override (0 = system default)
            
            // Combat
            public float damage;
            public float aoeRadius; // 0 for single-target
            public int sourceAgentIndex;
            public int targetAgentIndex; // -1 for area/position target
            public int sourceFactionIndex;
            
            // Type
            public ProjectileType type;
            public bool isActive;
            public bool hasHit;
            public bool fadeVisualOnHit;
            public float homingTurnRate; // Degrees per second
            public float homingAcceleration; // Units per second squared
            
            // Visual
            public int visualPoolIndex; // -1 if no visual (for ParticleSystem/GameObject visuals)
            public float visualScale;
            public int customPrefabId; // Instance ID of custom prefab, 0 = use default
            public int effectsConfigId; // ID to look up effects config, 0 = none
            public int trailVisualIndex; // Index of trail VFX instance, -1 if none
            
            // VFX Graph projectile visual support
            public bool usesVFXGraph; // True if projectile visual uses VFX Graph instead of GameObject
            public int vfxGraphPoolIndex; // Index within the VFX Graph pool for this asset, -1 if not using VFX Graph
            
            // Networking / Skill Effects
            public int networkProjectileId; // Unique ID for network sync and skill effect callbacks (0 = none)
        }
        
        /// <summary>
        /// Stores VFX and audio references for a projectile configuration.
        /// Supports both legacy GameObject prefabs and new VFXReference (VFX Graph).
        /// </summary>
        public class ProjectileEffectsConfig
        {
            // Legacy GameObject prefab fields (for backward compatibility)
            [System.Obsolete("Use projectileVisual instead")]
            public GameObject projectilePrefab;
            [System.Obsolete("Use impactVFX instead")]
            public GameObject impactVFXPrefab;
            [System.Obsolete("Use trailVFX instead")]
            public GameObject trailVFXPrefab;
            [System.Obsolete("Use aoeVFX instead")]
            public GameObject aoeVFXPrefab;
            
            // New VFXReference fields (supports both ParticleSystem and VFX Graph)
            public VFXReference projectileVisual;
            public VFXReference impactVFX;
            public VFXReference trailVFX;
            public VFXReference aoeVFX;
            
            public float visualScale = 1f;
            public AudioClip fireSound;
            public AudioClip impactSound;
            public float fireSoundVolume = 1f;
            public float impactSoundVolume = 1f;
            public float fireSoundPitchVariation = 0.05f;
            public float impactSoundPitchVariation = 0.05f;
            public float fireSoundSpatialBlend = 1f;
            public float impactSoundSpatialBlend = 1f;
            
            /// <summary>
            /// Gets the effective projectile visual (new VFXReference or legacy prefab).
            /// </summary>
            public VFXReference GetEffectiveProjectileVisual()
            {
                if (projectileVisual != null && projectileVisual.IsValid)
                    return projectileVisual;
                
                #pragma warning disable 0618
                if (projectilePrefab != null)
                    return new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = projectilePrefab, defaultDuration = 5f };
                #pragma warning restore 0618
                
                return null;
            }
            
            /// <summary>
            /// Gets the effective impact VFX (new VFXReference or legacy prefab).
            /// </summary>
            public VFXReference GetEffectiveImpactVFX()
            {
                if (impactVFX != null && impactVFX.IsValid)
                    return impactVFX;
                
                #pragma warning disable 0618
                if (impactVFXPrefab != null)
                    return new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = impactVFXPrefab, defaultDuration = 1f };
                #pragma warning restore 0618
                
                return null;
            }
            
            /// <summary>
            /// Gets the effective trail VFX (new VFXReference or legacy prefab).
            /// </summary>
            public VFXReference GetEffectiveTrailVFX()
            {
                if (trailVFX != null && trailVFX.IsValid)
                    return trailVFX;
                
                #pragma warning disable 0618
                if (trailVFXPrefab != null)
                    return new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = trailVFXPrefab, defaultDuration = 3f };
                #pragma warning restore 0618
                
                return null;
            }
            
            /// <summary>
            /// Gets the effective AOE VFX (new VFXReference or legacy prefab).
            /// </summary>
            public VFXReference GetEffectiveAoeVFX()
            {
                if (aoeVFX != null && aoeVFX.IsValid)
                    return aoeVFX;
                
                #pragma warning disable 0618
                if (aoeVFXPrefab != null)
                    return new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = aoeVFXPrefab, defaultDuration = 2f };
                #pragma warning restore 0618
                
                return null;
            }
        }
        
        public enum ProjectileType : byte
        {
            Linear,     // Straight line (arrows, bullets, magic)
            Parabolic,  // Arc trajectory (grenades, thrown weapons)
            Homing      // Tracks target (guided missiles, magic)
        }

        private enum ProjectileVisualPoolType : byte
        {
            DefaultLinear,
            DefaultParabolic,
            CustomPrefab
        }

        private struct PendingProjectileVisualRelease
        {
            public ProjectileVisualPoolType poolType;
            public int customPrefabId;
            public int visualIndex;
            public float forceReleaseTime;
        }
        
        #endregion
        
        #region Internal State
        
        // Projectile data (NativeArray for Jobs compatibility)
        private NativeArray<ProjectileData> _projectiles;
        private int _activeProjectileCount;
        private Queue<int> _freeIndices;
        
        // Visual pool (default pools for backward compatibility)
        private List<GameObject> _linearVisualPool;
        private List<GameObject> _parabolicVisualPool;
        private Queue<int> _freeLinearVisuals;
        private Queue<int> _freeParabolicVisuals;
        
        // Per-prefab visual pools (for custom projectile prefabs from EnemyCrowd configs)
        private Dictionary<GameObject, List<GameObject>> _customPrefabPools;
        private Dictionary<GameObject, Queue<int>> _customPrefabFreeIndices;
        private Dictionary<int, GameObject> _prefabIdLookup; // Reverse lookup: prefabId -> prefab
        
        // Effects config lookup (for VFX and audio)
        private Dictionary<int, ProjectileEffectsConfig> _effectsConfigs;
        private int _nextEffectsConfigId = 1;
        
        // Trail VFX pools (keyed by trail prefab)
        private Dictionary<GameObject, List<GameObject>> _trailVFXPools;
        private Dictionary<GameObject, Queue<int>> _trailVFXFreeIndices;
        private Dictionary<int, GameObject> _trailPrefabIdLookup;
        
        // Active trails (projectile index -> trail instance)
        private Dictionary<int, (int prefabId, int poolIndex)> _activeTrails;
        
        // Pending trail releases (to avoid coroutine allocations)
        // Each entry: (prefabId, poolIndex, releaseTime)
        private List<(int prefabId, int poolIndex, float releaseTime)> _pendingTrailReleases;
        private const float TrailFadeDelay = 2f;

        // Pending projectile visual releases (to let particles fade out after projectile hit)
        private List<PendingProjectileVisualRelease> _pendingProjectileVisualReleases;
        private const float MaxProjectileVisualFadeDelay = 3f;
        
        // Cached ParticleSystem components per trail prefab
        private Dictionary<int, ParticleSystem> _cachedTrailParticleSystems;

        // Cached projectile ParticleSystem components by pool index
        private Dictionary<int, ParticleSystem[]> _cachedLinearProjectileParticleSystems;
        private Dictionary<int, ParticleSystem[]> _cachedParabolicProjectileParticleSystems;
        private Dictionary<int, Dictionary<int, ParticleSystem[]>> _cachedCustomProjectileParticleSystems;
        
        // Impact VFX pools (keyed by impact prefab)
        private Dictionary<GameObject, List<GameObject>> _impactVFXPools;
        private Dictionary<GameObject, Queue<int>> _impactVFXFreeIndices;
        private List<(GameObject prefab, int poolIndex, float releaseTime)> _activeImpacts;
        
        // Cached ParticleSystem components per impact prefab pool instance
        private Dictionary<GameObject, Dictionary<int, ParticleSystem>> _cachedImpactParticleSystems;
        
        // AOE VFX pools (keyed by AOE prefab)
        private Dictionary<GameObject, List<GameObject>> _aoeVFXPools;
        private Dictionary<GameObject, Queue<int>> _aoeVFXFreeIndices;
        private List<(GameObject prefab, int poolIndex, float releaseTime)> _activeAOEs;
        
        // Cached ParticleSystem components per AOE prefab pool instance
        private Dictionary<GameObject, Dictionary<int, ParticleSystem>> _cachedAOEParticleSystems;
        
        // Audio source pool for efficient sound playback
        private List<AudioSource> _audioSourcePool;
        private Queue<int> _freeAudioSources;
        private float[] _audioSourceReleaseTime; // When each audio source becomes free
        
        // VFX spawner for handling both ParticleSystem and VFX Graph
        private VFXSpawner _vfxSpawner;
        
        // VFX Graph projectile pools - for GPU-accelerated projectile visuals
        // Each VFX Graph asset gets a pool of VisualEffect components
        private Dictionary<UnityEngine.VFX.VisualEffectAsset, List<UnityEngine.VFX.VisualEffect>> _vfxGraphProjectilePools;
        private Dictionary<UnityEngine.VFX.VisualEffectAsset, Queue<int>> _vfxGraphProjectileFreeIndices;
        
        // Maps projectile index to VFX Graph instance (asset, pool index)
        private Dictionary<int, (UnityEngine.VFX.VisualEffectAsset asset, int poolIndex)> _activeVFXGraphProjectiles;
        
        // Cached property IDs for VFX Graph projectile updates
        private static readonly int VFXProjectilePositionId = Shader.PropertyToID("ProjectilePosition");
        private static readonly int VFXProjectileDirectionId = Shader.PropertyToID("ProjectileDirection");
        private static readonly int VFXProjectileVelocityId = Shader.PropertyToID("ProjectileVelocity");
        private static readonly int VFXProjectileScaleId = Shader.PropertyToID("ProjectileScale");
        private static readonly int VFXProjectileActiveId = Shader.PropertyToID("ProjectileActive");
        
        // Cached event IDs for VFX Graph projectiles
        private static readonly int VFXOnProjectileSpawnId = Shader.PropertyToID("OnProjectileSpawn");
        private static readonly int VFXOnProjectileHitId = Shader.PropertyToID("OnProjectileHit");
        private static readonly int VFXOnProjectileDestroyId = Shader.PropertyToID("OnProjectileDestroy");
        
        // Hit detection batching
        private List<int> _pendingHitChecks;
        private int _hitCheckIndex;
        
        // Reference to main controller
        private EnemyMassesCrowdController _crowdController;
        
        // Camera reference for culling
        private Transform _mainCameraTransform;
        
        // Statistics
        private int _projectilesFiredThisFrame;
        private int _projectilesHitThisFrame;
        
        #endregion
        
        #region Network Events
        
        // Network projectile ID counter (server only)
        // Used when generating IDs for network projectile spawn events
        private int _nextNetworkProjectileId = 1;
        
        /// <summary>
        /// Generates a unique network projectile ID. Call this when spawning projectiles
        /// that need to be synchronized across the network.
        /// </summary>
        public int GenerateNetworkProjectileId() => _nextNetworkProjectileId++;
        
        // These events are intentionally unused locally - they are hooks for network implementations
        // to subscribe to for multiplayer synchronization
#pragma warning disable CS0067 // Event is never used
        /// <summary>
        /// Fired when a projectile is spawned. Use for network synchronization.
        /// Subscribe to this event in your network authority implementation.
        /// </summary>
        public event System.Action<ProjectileSpawnData> NetworkProjectileSpawned;
        
        /// <summary>
        /// Fired when a projectile hits a target. Use for network damage validation.
        /// Subscribe to this event in your network authority implementation.
        /// </summary>
        public event System.Action<int, ProjectileHitData> NetworkProjectileHit;
        
        /// <summary>
        /// Fired when a projectile misses or expires.
        /// Subscribe to this event in your network authority implementation.
        /// </summary>
        public event System.Action<int, Vector3> NetworkProjectileMiss;
#pragma warning restore CS0067
        
        #endregion
        
        #region Skill Effects Callbacks
        
        /// <summary>
        /// Callback data for when a skill projectile hits its target(s).
        /// </summary>
        public struct SkillProjectileHitCallback
        {
            public int projectileIndex;
            public int networkProjectileId;
            public Vector3 hitPosition;
            public int[] hitAgentIndices;
            public float[] damageMultipliers; // Per-target multiplier (AOE falloff, pierce falloff)
            public bool isAOE;
            public bool isPierce;
        }
        
        /// <summary>
        /// Per-projectile runtime state for trajectory piercing.
        /// Tracks which agents were already hit while the projectile is in-flight.
        /// </summary>
        private class TrajectoryPierceState
        {
            public readonly HashSet<int> hitAgents = new HashSet<int>();
            public int pierceHitCount;
        }

        /// <summary>
        /// Per-projectile runtime state for timed in-flight skill actions.
        /// </summary>
        private class TimedFlightActionState
        {
            public float nextExecutionTime;
            public int executedTicks;
        }
        
        /// <summary>
        /// Registered callback for applying skill effects on projectile hit.
        /// Maps networkProjectileId to (skill, context, targeting config).
        /// </summary>
        private Dictionary<int, (AgentSkill skill, SkillExecutionContext context, ProjectileTargetingConfig targeting)> _skillProjectileCallbacks 
            = new Dictionary<int, (AgentSkill, SkillExecutionContext, ProjectileTargetingConfig)>();
        
        /// <summary>
        /// Runtime trajectory piercing state per projectile index.
        /// Used for Projectile skills when trajectory pass-through is active
        /// (explicit toggle or implicit via On Trajectory Hit actions).
        /// </summary>
        private Dictionary<int, TrajectoryPierceState> _trajectoryPierceStates 
            = new Dictionary<int, TrajectoryPierceState>();

        /// <summary>
        /// Runtime state for projectile skills that use repeated During Flight (Timed) actions.
        /// Keyed by projectile index.
        /// </summary>
        private Dictionary<int, TimedFlightActionState> _timedFlightActionStates
            = new Dictionary<int, TimedFlightActionState>();
        
        /// <summary>
        /// Registers a skill to have its effects applied when the projectile hits.
        /// Call this after spawning a skill projectile.
        /// </summary>
        /// <param name="networkProjectileId">The network projectile ID returned by spawn</param>
        /// <param name="skill">The skill whose effects to apply</param>
        /// <param name="context">The skill execution context</param>
        /// <param name="targeting">The projectile targeting configuration</param>
        public void RegisterSkillProjectileCallback(
            int networkProjectileId,
            AgentSkill skill,
            SkillExecutionContext context,
            ProjectileTargetingConfig targeting)
        {
            if (networkProjectileId == 0) return;
            _skillProjectileCallbacks[networkProjectileId] = (skill, context, targeting);
            if (enableDebugLogging)
            {
                Debug.Log($"[CALLBACK REG] Registered callback id={networkProjectileId}, skill={skill?.SkillName}, totalCallbacks={_skillProjectileCallbacks.Count}");
            }
        }
        
        /// <summary>
        /// Called internally when a skill projectile hits. Applies skill effects to targets.
        /// </summary>
        private void ProcessSkillProjectileHit(int networkProjectileId, SkillProjectileHitCallback hitData)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[PROJECTILE DEBUG] ProcessSkillProjectileHit called - projectileId={networkProjectileId}, hitAgents={hitData.hitAgentIndices?.Length ?? 0}, hitPos={hitData.hitPosition}");
            }
            
            if (!_skillProjectileCallbacks.TryGetValue(networkProjectileId, out var callbackData))
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"[PROJECTILE DEBUG] No callback found for projectile {networkProjectileId}! Available callbacks: {_skillProjectileCallbacks.Count}");
                }
                return;
            }
            
            var (skill, context, targeting) = callbackData;
            
            // Remove callback (one-shot)
            _skillProjectileCallbacks.Remove(networkProjectileId);
            
            if (enableDebugLogging)
            {
                Debug.Log($"[PROJECTILE DEBUG] Callback found - skill={skill?.SkillName ?? "NULL"}, controller={context.Controller != null}, onHitCount={skill?.OnHitActions?.Count ?? 0}");
            }
            
            if (skill == null || context.Controller == null)
            {
                if (enableDebugLogging)
                {
                    Debug.LogError($"[PROJECTILE DEBUG] ABORT: skill={skill != null}, controller={context.Controller != null}");
                }
                return;
            }
            
            // Calculate base damage for event reporting
            float baseDamage = 0f;
            foreach (var action in skill.OnHitActions)
            {
                if (action is DamageEffect damageEffect)
                {
                    baseDamage = damageEffect.BaseDamage * context.EffectMultiplier;
                    break;
                }
            }
            
            int directHitCount = hitData.hitAgentIndices?.Length ?? 0;
            if (enableDebugLogging)
            {
                Debug.Log($"[PROJECTILE DEBUG] Processing {directHitCount} hit targets...");
            }

            // Stop any looping SFX from this execution that are configured to end on hit.
            SkillVFXManager.StopLoopingSFXForContext(context, SkillSFXLoopStopPolicy.OnHit);
            
            var affectedAgents = new List<CrowdAgent>();
            bool executedOnHitActions = false;
            
            // Apply effects to each direct hit target
            for (int i = 0; i < directHitCount; i++)
            {
                int agentIndex = hitData.hitAgentIndices[i];
                float multiplier = (hitData.damageMultipliers != null && i < hitData.damageMultipliers.Length)
                    ? hitData.damageMultipliers[i]
                    : 1f;
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[PROJECTILE DEBUG] Target {i}: agentIndex={agentIndex}, multiplier={multiplier}");
                }
                
                var hitAgent = context.Controller.GetAgent(agentIndex);
                if (hitAgent == null || hitAgent.isDead)
                {
                    if (enableDebugLogging)
                    {
                        Debug.LogWarning($"[PROJECTILE DEBUG] Target {agentIndex} skipped: null={hitAgent == null}, dead={hitAgent?.isDead}");
                    }
                    continue;
                }
                
                // Create modified context for this target
                var targetContext = new SkillExecutionContext
                {
                    Caster = context.Caster,
                    CasterTransform = context.CasterTransform,
                    PrimaryTarget = hitAgent,
                    AllTargets = new CrowdAgent[] { hitAgent },
                    TargetPosition = hitData.hitPosition,
                    Skill = skill,
                    Controller = context.Controller,
                    IsCritical = context.IsCritical,
                    EffectMultiplier = context.EffectMultiplier * multiplier,
                    ExecutionId = context.ExecutionId,
                    NetworkProjectileId = context.NetworkProjectileId,
                    DamageableTarget = context.DamageableTarget
                };
                
                // Apply all OnHit actions to this target
                if (enableDebugLogging)
                {
                    Debug.Log($"[PROJECTILE DEBUG] Executing {skill.OnHitActions.Count} actions on agent {agentIndex} (HP before: {hitAgent.health}/{hitAgent.maxHealth})");
                }
                SkillExecutor.ExecuteActions(skill.OnHitActions, targetContext);
                executedOnHitActions = true;
                if (!affectedAgents.Contains(hitAgent))
                {
                    affectedAgents.Add(hitAgent);
                }
                
                // Raise OnSkillImpact event for this projectile hit
                SkillExecutor.RaiseProjectileImpactEvent(
                    context.Caster,
                    skill,
                    hitAgent,
                    hitData.hitPosition,
                    baseDamage * multiplier,
                    context.IsCritical
                );
            }
            
            // No direct hit target resolved - still execute OnHit actions at impact point.
            // This supports effects that resolve targets themselves (AOEOnImpact, Piercing, impact VFX, etc.).
            if (!executedOnHitActions)
            {
                var impactOnlyContext = new SkillExecutionContext
                {
                    Caster = context.Caster,
                    CasterTransform = context.CasterTransform,
                    PrimaryTarget = context.PrimaryTarget,
                    AllTargets = System.Array.Empty<CrowdAgent>(),
                    TargetPosition = hitData.hitPosition,
                    Skill = skill,
                    Controller = context.Controller,
                    IsCritical = context.IsCritical,
                    EffectMultiplier = context.EffectMultiplier,
                    ExecutionId = context.ExecutionId,
                    NetworkProjectileId = context.NetworkProjectileId,
                    DamageableTarget = context.DamageableTarget
                };
                
                if (enableDebugLogging)
                {
                    Debug.Log("[PROJECTILE DEBUG] No direct hit targets, executing OnHit actions at impact position.");
                }
                
                SkillExecutor.ExecuteActions(skill.OnHitActions, impactOnlyContext);
                
                if (impactOnlyContext.AllTargets != null)
                {
                    foreach (var target in impactOnlyContext.AllTargets)
                    {
                        if (target == null || target.isDead) continue;
                        if (affectedAgents.Contains(target)) continue;
                        
                        affectedAgents.Add(target);
                        
                        SkillExecutor.RaiseProjectileImpactEvent(
                            context.Caster,
                            skill,
                            target,
                            hitData.hitPosition,
                            baseDamage,
                            context.IsCritical
                        );
                    }
                }
            }
            
            // On Finish actions after projectile impact (always execute once projectile resolves).
            var finishContext = new SkillExecutionContext
            {
                Caster = context.Caster,
                CasterTransform = context.CasterTransform,
                PrimaryTarget = affectedAgents.Count > 0 ? affectedAgents[0] : context.PrimaryTarget,
                AllTargets = affectedAgents.ToArray(),
                TargetPosition = hitData.hitPosition,
                Skill = skill,
                Controller = context.Controller,
                IsCritical = context.IsCritical,
                EffectMultiplier = context.EffectMultiplier,
                ExecutionId = context.ExecutionId,
                NetworkProjectileId = context.NetworkProjectileId,
                DamageableTarget = context.DamageableTarget
            };
            
            SkillExecutor.ExecuteFinishActions(skill, finishContext);
        }
        
        #endregion
        
        #region Initialization
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            
            InitializePools();
        }
        
        private void Start()
        {
            _crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            _mainCameraTransform = Camera.main?.transform;
        }
        
        private void InitializePools()
        {
            // Initialize projectile data array
            _projectiles = new NativeArray<ProjectileData>(maxProjectiles, Allocator.Persistent);
            _freeIndices = new Queue<int>(maxProjectiles);
            
            for (int i = 0; i < maxProjectiles; i++)
            {
                _freeIndices.Enqueue(i);
            }
            
            // Initialize visual pools
            _linearVisualPool = new List<GameObject>(maxProjectiles / 2);
            _parabolicVisualPool = new List<GameObject>(maxProjectiles / 4);
            _freeLinearVisuals = new Queue<int>();
            _freeParabolicVisuals = new Queue<int>();
            
            // Initialize per-prefab pools for custom projectiles
            _customPrefabPools = new Dictionary<GameObject, List<GameObject>>();
            _customPrefabFreeIndices = new Dictionary<GameObject, Queue<int>>();
            _prefabIdLookup = new Dictionary<int, GameObject>();
            
            // Initialize effects config lookup
            _effectsConfigs = new Dictionary<int, ProjectileEffectsConfig>();
            
            // Initialize trail VFX pools
            _trailVFXPools = new Dictionary<GameObject, List<GameObject>>();
            _trailVFXFreeIndices = new Dictionary<GameObject, Queue<int>>();
            _trailPrefabIdLookup = new Dictionary<int, GameObject>();
            _activeTrails = new Dictionary<int, (int, int)>();
            _pendingTrailReleases = new List<(int, int, float)>(64);
            _cachedTrailParticleSystems = new Dictionary<int, ParticleSystem>(16);
            _pendingProjectileVisualReleases = new List<PendingProjectileVisualRelease>(64);
            _cachedLinearProjectileParticleSystems = new Dictionary<int, ParticleSystem[]>(64);
            _cachedParabolicProjectileParticleSystems = new Dictionary<int, ParticleSystem[]>(64);
            _cachedCustomProjectileParticleSystems = new Dictionary<int, Dictionary<int, ParticleSystem[]>>(16);
            
            // Initialize impact VFX pools
            _impactVFXPools = new Dictionary<GameObject, List<GameObject>>();
            _impactVFXFreeIndices = new Dictionary<GameObject, Queue<int>>();
            _activeImpacts = new List<(GameObject, int, float)>(128);
            _cachedImpactParticleSystems = new Dictionary<GameObject, Dictionary<int, ParticleSystem>>();
            
            // Initialize AOE VFX pools
            _aoeVFXPools = new Dictionary<GameObject, List<GameObject>>();
            _aoeVFXFreeIndices = new Dictionary<GameObject, Queue<int>>();
            _activeAOEs = new List<(GameObject, int, float)>(64);
            _cachedAOEParticleSystems = new Dictionary<GameObject, Dictionary<int, ParticleSystem>>();
            
            // Initialize VFX Graph projectile pools
            _vfxGraphProjectilePools = new Dictionary<UnityEngine.VFX.VisualEffectAsset, List<UnityEngine.VFX.VisualEffect>>();
            _vfxGraphProjectileFreeIndices = new Dictionary<UnityEngine.VFX.VisualEffectAsset, Queue<int>>();
            _activeVFXGraphProjectiles = new Dictionary<int, (UnityEngine.VFX.VisualEffectAsset, int)>(maxProjectiles);
            _trajectoryPierceStates = new Dictionary<int, TrajectoryPierceState>(maxProjectiles);
            _timedFlightActionStates = new Dictionary<int, TimedFlightActionState>(maxProjectiles);
            
            _pendingHitChecks = new List<int>(maxProjectiles);
            
            // Initialize audio source pool
            InitializeAudioPool(64); // Start with 64 pooled audio sources
            
            // Initialize VFX spawner for VFX Graph support
            _vfxSpawner = new VFXSpawner(transform);
            
            // Pre-spawn some visuals (only if defaults are assigned)
            PrewarmVisualPool(defaultLinearProjectilePrefab, _linearVisualPool, _freeLinearVisuals, 50);
            PrewarmVisualPool(defaultParabolicProjectilePrefab, _parabolicVisualPool, _freeParabolicVisuals, 20);
        }
        
        private void InitializeAudioPool(int poolSize)
        {
            _audioSourcePool = new List<AudioSource>(poolSize);
            _freeAudioSources = new Queue<int>(poolSize);
            _audioSourceReleaseTime = new float[poolSize];
            
            // Create a container for audio sources
            GameObject audioContainer = new GameObject("ProjectileAudioPool");
            audioContainer.transform.SetParent(transform);
            
            for (int i = 0; i < poolSize; i++)
            {
                GameObject audioObj = new GameObject($"AudioSource_{i}");
                audioObj.transform.SetParent(audioContainer.transform);
                
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
        
        private void PrewarmVisualPool(GameObject prefab, List<GameObject> pool, Queue<int> freeQueue, int count)
        {
            if (prefab == null) return;
            
            for (int i = 0; i < count; i++)
            {
                GameObject visual = Instantiate(prefab, transform);
                visual.SetActive(false);
                pool.Add(visual);
                freeQueue.Enqueue(pool.Count - 1);
            }
        }
        
        private void OnDestroy()
        {
            if (_projectiles.IsCreated)
            {
                _projectiles.Dispose();
            }
        }
        
        #endregion
        
        #region Public API - Fire Projectiles
        
        /// <summary>
        /// Registers an effects config and returns its ID for use with projectiles.
        /// Legacy overload for backward compatibility with GameObject prefabs.
        /// </summary>
        [System.Obsolete("Use RegisterEffectsConfig with VFXReference parameters for better performance with VFX Graph")]
        public int RegisterEffectsConfig(
            GameObject impactVFX, 
            GameObject trailVFX, 
            AudioClip fireSound, 
            AudioClip impactSound, 
            float fireSoundVolume = 1f, 
            float impactSoundVolume = 1f, 
            GameObject aoeVFX = null,
            float fireSoundPitchVariation = 0.05f,
            float impactSoundPitchVariation = 0.05f,
            float fireSoundSpatialBlend = 1f,
            float impactSoundSpatialBlend = 1f)
        {
            // Convert to VFXReference
            VFXReference impactRef = impactVFX != null 
                ? new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = impactVFX, defaultDuration = 1f }
                : null;
            VFXReference trailRef = trailVFX != null 
                ? new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = trailVFX, defaultDuration = 3f }
                : null;
            VFXReference aoeRef = aoeVFX != null 
                ? new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = aoeVFX, defaultDuration = 2f }
                : null;
            
            return RegisterEffectsConfig(null, impactRef, trailRef, fireSound, impactSound, 
                fireSoundVolume, impactSoundVolume, aoeRef,
                fireSoundPitchVariation, impactSoundPitchVariation,
                fireSoundSpatialBlend, impactSoundSpatialBlend, 1f);
        }
        
        /// <summary>
        /// Registers an effects config with VFXReference support and returns its ID for use with projectiles.
        /// Supports both ParticleSystem prefabs and VFX Graph assets.
        /// </summary>
        public int RegisterEffectsConfig(
            VFXReference projectileVisual,
            VFXReference impactVFX, 
            VFXReference trailVFX, 
            AudioClip fireSound, 
            AudioClip impactSound, 
            float fireSoundVolume = 1f, 
            float impactSoundVolume = 1f, 
            VFXReference aoeVFX = null,
            float fireSoundPitchVariation = 0.05f,
            float impactSoundPitchVariation = 0.05f,
            float fireSoundSpatialBlend = 1f,
            float impactSoundSpatialBlend = 1f,
            float visualScale = 1f)
        {
            bool hasAny = (projectileVisual != null && projectileVisual.IsValid) ||
                          (impactVFX != null && impactVFX.IsValid) || 
                          (trailVFX != null && trailVFX.IsValid) || 
                          fireSound != null || 
                          impactSound != null || 
                          (aoeVFX != null && aoeVFX.IsValid);
            
            if (!hasAny)
                return 0;
            
            int configId = _nextEffectsConfigId++;
            _effectsConfigs[configId] = new ProjectileEffectsConfig
            {
                projectileVisual = projectileVisual,
                impactVFX = impactVFX,
                trailVFX = trailVFX,
                aoeVFX = aoeVFX,
                visualScale = visualScale,
                fireSound = fireSound,
                impactSound = impactSound,
                fireSoundVolume = fireSoundVolume,
                impactSoundVolume = impactSoundVolume,
                fireSoundPitchVariation = fireSoundPitchVariation,
                impactSoundPitchVariation = impactSoundPitchVariation,
                fireSoundSpatialBlend = fireSoundSpatialBlend,
                impactSoundSpatialBlend = impactSoundSpatialBlend
            };
            return configId;
        }
        
        /// <summary>
        /// Fires a linear (straight-line) projectile from source to target.
        /// Used for arrows, bullets, magic bolts.
        /// </summary>
        public int FireLinearProjectile(
            Vector3 startPosition,
            Vector3 targetPosition,
            float speed,
            float damage,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            float lifetime = 5f,
            bool spawnVisual = true,
            GameObject customPrefab = null,
            float visualScale = 1f,
            int effectsConfigId = 0,
            float aoeRadius = 0f,
            int networkProjectileId = 0,
            bool fadeProjectileOnHit = true)
        {
            if (_freeIndices.Count == 0)
            {
                Debug.LogWarning("[ProjectileSystem] Max projectiles reached!");
                return -1;
            }
            
            int index = _freeIndices.Dequeue();
            
            Vector3 direction = (targetPosition - startPosition).normalized;
            
            // Determine visual index and custom prefab ID
            int visualIndex = -1;
            int customPrefabId = 0;
            bool usesVFXGraph = false;
            int vfxGraphPoolIndex = -1;
            
            // Check if we have a VFX Graph projectile visual from effects config
            VFXReference projectileVisualRef = null;
            if (effectsConfigId > 0 && _effectsConfigs.TryGetValue(effectsConfigId, out var config))
            {
                projectileVisualRef = config.GetEffectiveProjectileVisual();
            }
            
            if (spawnVisual)
            {
                // Check if using VFX Graph projectile visual
                if (projectileVisualRef != null && projectileVisualRef.type == VFXType.VFXGraph && projectileVisualRef.vfxGraphAsset != null)
                {
                    // Use VFX Graph for projectile visual
                    usesVFXGraph = true;
                    vfxGraphPoolIndex = AcquireVFXGraphProjectileVisual(projectileVisualRef.vfxGraphAsset, startPosition, direction, speed, visualScale, index);
                }
                else if (customPrefab != null)
                {
                    // Use custom prefab pool
                    var (prefabId, idx) = AcquireVisualForPrefab(customPrefab, visualScale);
                    customPrefabId = prefabId;
                    visualIndex = idx;
                }
                else if (projectileVisualRef != null && projectileVisualRef.type == VFXType.ParticleSystem && projectileVisualRef.particleSystemPrefab != null)
                {
                    // Use ParticleSystem prefab from effects config
                    var (prefabId, idx) = AcquireVisualForPrefab(projectileVisualRef.particleSystemPrefab, visualScale);
                    customPrefabId = prefabId;
                    visualIndex = idx;
                }
                else
                {
                    // Use default pool
                    visualIndex = AcquireVisual(ProjectileType.Linear);
                }
            }
            
            // Handle trail VFX
            int trailVisualIndex = -1;
            if (effectsConfigId > 0 && _effectsConfigs.TryGetValue(effectsConfigId, out var effectsConfig))
            {
                // Spawn trail VFX using new VFXReference or legacy prefab
                var trailVFXRef = effectsConfig.GetEffectiveTrailVFX();
                if (trailVFXRef != null && trailVFXRef.IsParticleSystem)
                {
                    trailVisualIndex = SpawnTrailVFX(trailVFXRef.particleSystemPrefab, startPosition, direction, index);
                }
                
                // Play fire sound
                if (effectsConfig.fireSound != null)
                {
                    PlaySoundAtPosition(effectsConfig.fireSound, startPosition, effectsConfig.fireSoundVolume, effectsConfig.fireSoundPitchVariation, effectsConfig.fireSoundSpatialBlend);
                }
            }
            
            ProjectileData data = new ProjectileData
            {
                position = startPosition,
                velocity = direction * speed,
                startPosition = startPosition,
                targetPosition = targetPosition,
                spawnTime = Time.time,
                lifetime = lifetime,
                flightTime = 0f,
                simulationGravity = 0f,
                damage = damage,
                aoeRadius = aoeRadius,
                sourceAgentIndex = sourceAgentIndex,
                targetAgentIndex = targetAgentIndex,
                sourceFactionIndex = sourceFactionIndex,
                type = ProjectileType.Linear,
                isActive = true,
                hasHit = false,
                fadeVisualOnHit = fadeProjectileOnHit,
                homingTurnRate = 0f,
                homingAcceleration = 0f,
                visualPoolIndex = visualIndex,
                visualScale = visualScale,
                customPrefabId = customPrefabId,
                effectsConfigId = effectsConfigId,
                trailVisualIndex = trailVisualIndex,
                usesVFXGraph = usesVFXGraph,
                vfxGraphPoolIndex = vfxGraphPoolIndex,
                networkProjectileId = networkProjectileId
            };
            
            _projectiles[index] = data;
            _activeProjectileCount++;
            _projectilesFiredThisFrame++;
            
            // Activate visual (for non-VFX Graph visuals)
            if (!usesVFXGraph && visualIndex >= 0)
            {
                if (customPrefabId > 0)
                {
                    ActivateCustomVisual(customPrefabId, visualIndex, startPosition, direction, visualScale);
                }
                else
                {
                    ActivateVisual(visualIndex, ProjectileType.Linear, startPosition, direction, visualScale);
                }
            }
            
            return index;
        }

        /// <summary>
        /// Fires a homing projectile that continuously tracks its target.
        /// Uses the same pooled visual/combat pipeline as linear projectiles,
        /// but switches simulation type to Homing and applies steering.
        /// </summary>
        public int FireHomingProjectile(
            Vector3 startPosition,
            Vector3 targetPosition,
            float speed,
            float damage,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            float homingTurnRate = 180f,
            float homingAcceleration = 0f,
            float lifetime = 10f,
            bool spawnVisual = true,
            GameObject customPrefab = null,
            float visualScale = 1f,
            int effectsConfigId = 0,
            float aoeRadius = 0f,
            int networkProjectileId = 0,
            bool fadeProjectileOnHit = true)
        {
            int index = FireLinearProjectile(
                startPosition,
                targetPosition,
                speed,
                damage,
                sourceAgentIndex,
                targetAgentIndex,
                sourceFactionIndex,
                lifetime,
                spawnVisual,
                customPrefab,
                visualScale,
                effectsConfigId,
                aoeRadius,
                networkProjectileId,
                fadeProjectileOnHit
            );

            if (index < 0) return index;

            ProjectileData data = _projectiles[index];
            data.type = ProjectileType.Homing;
            data.homingTurnRate = Mathf.Max(0f, homingTurnRate);
            data.homingAcceleration = homingAcceleration;
            _projectiles[index] = data;

            return index;
        }
        
        /// <summary>
        /// Fires a parabolic (arc trajectory) projectile from source to target.
        /// Uses kinematic equations: x(t) = x₀ + v₀·t + 0.5·a·t²
        /// Used for grenades, thrown weapons.
        /// </summary>
        public int FireParabolicProjectile(
            Vector3 startPosition,
            Vector3 targetPosition,
            float damage,
            float aoeRadius,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            float? flightTime = null,
            float launchAngle = 45f,
            bool spawnVisual = true,
            GameObject customPrefab = null,
            float visualScale = 1f,
            int effectsConfigId = 0,
            int networkProjectileId = 0,
            bool fadeProjectileOnHit = true,
            float? gravityOverride = null)
        {
            if (_freeIndices.Count == 0)
            {
                Debug.LogWarning("[ProjectileSystem] Max projectiles reached!");
                return -1;
            }
            
            int index = _freeIndices.Dequeue();
            float projectileGravity = gravityOverride.HasValue && gravityOverride.Value > 0f
                ? gravityOverride.Value
                : gravity;
            
            // Calculate launch velocity using projectile motion equations
            Vector3 displacement = targetPosition - startPosition;
            float horizontalDistance = new Vector2(displacement.x, displacement.z).magnitude;
            float verticalDistance = displacement.y;
            
            // If flight time not specified, calculate based on launch angle
            float t;
            Vector3 launchVelocity;
            
            if (flightTime.HasValue)
            {
                t = flightTime.Value;
                launchVelocity = CalculateLaunchVelocityFromTime(startPosition, targetPosition, t, projectileGravity);
            }
            else
            {
                launchVelocity = CalculateLaunchVelocityFromAngle(startPosition, targetPosition, launchAngle, projectileGravity, out t);
            }
            
            // Determine visual index and custom prefab ID
            int visualIndex = -1;
            int customPrefabId = 0;
            bool usesVFXGraph = false;
            int vfxGraphPoolIndex = -1;
            
            // Check if we have a VFX Graph projectile visual from effects config
            VFXReference projectileVisualRef = null;
            if (effectsConfigId > 0 && _effectsConfigs.TryGetValue(effectsConfigId, out var config))
            {
                projectileVisualRef = config.GetEffectiveProjectileVisual();
            }
            
            if (spawnVisual)
            {
                // Check if using VFX Graph projectile visual
                if (projectileVisualRef != null && projectileVisualRef.type == VFXType.VFXGraph && projectileVisualRef.vfxGraphAsset != null)
                {
                    // Use VFX Graph for projectile visual
                    usesVFXGraph = true;
                    float speed = launchVelocity.magnitude;
                    vfxGraphPoolIndex = AcquireVFXGraphProjectileVisual(projectileVisualRef.vfxGraphAsset, startPosition, launchVelocity.normalized, speed, visualScale, index);
                }
                else if (customPrefab != null)
                {
                    // Use custom prefab pool
                    var (prefabId, idx) = AcquireVisualForPrefab(customPrefab, visualScale);
                    customPrefabId = prefabId;
                    visualIndex = idx;
                }
                else if (projectileVisualRef != null && projectileVisualRef.type == VFXType.ParticleSystem && projectileVisualRef.particleSystemPrefab != null)
                {
                    // Use ParticleSystem prefab from effects config
                    var (prefabId, idx) = AcquireVisualForPrefab(projectileVisualRef.particleSystemPrefab, visualScale);
                    customPrefabId = prefabId;
                    visualIndex = idx;
                }
                else
                {
                    // Use default pool
                    visualIndex = AcquireVisual(ProjectileType.Parabolic);
                }
            }
            
            // Handle trail VFX and fire sound
            int trailVisualIndex = -1;
            if (effectsConfigId > 0 && _effectsConfigs.TryGetValue(effectsConfigId, out var effectsConfig))
            {
                // Spawn trail VFX using new VFXReference or legacy prefab
                var trailVFXRef = effectsConfig.GetEffectiveTrailVFX();
                if (trailVFXRef != null && trailVFXRef.IsParticleSystem)
                {
                    trailVisualIndex = SpawnTrailVFX(trailVFXRef.particleSystemPrefab, startPosition, launchVelocity.normalized, index);
                }
                
                // Play fire sound
                if (effectsConfig.fireSound != null)
                {
                    PlaySoundAtPosition(effectsConfig.fireSound, startPosition, effectsConfig.fireSoundVolume, effectsConfig.fireSoundPitchVariation, effectsConfig.fireSoundSpatialBlend);
                }
            }
            
            ProjectileData data = new ProjectileData
            {
                position = startPosition,
                velocity = launchVelocity,
                startPosition = startPosition,
                targetPosition = targetPosition,
                spawnTime = Time.time,
                lifetime = t + 1f, // Add buffer
                flightTime = t,
                simulationGravity = projectileGravity,
                damage = damage,
                aoeRadius = aoeRadius,
                sourceAgentIndex = sourceAgentIndex,
                targetAgentIndex = targetAgentIndex,
                sourceFactionIndex = sourceFactionIndex,
                type = ProjectileType.Parabolic,
                isActive = true,
                hasHit = false,
                fadeVisualOnHit = fadeProjectileOnHit,
                homingTurnRate = 0f,
                homingAcceleration = 0f,
                visualPoolIndex = visualIndex,
                visualScale = visualScale,
                customPrefabId = customPrefabId,
                effectsConfigId = effectsConfigId,
                trailVisualIndex = trailVisualIndex,
                usesVFXGraph = usesVFXGraph,
                vfxGraphPoolIndex = vfxGraphPoolIndex,
                networkProjectileId = networkProjectileId
            };
            
            _projectiles[index] = data;
            _activeProjectileCount++;
            _projectilesFiredThisFrame++;
            
            // Activate visual (for non-VFX Graph visuals)
            if (!usesVFXGraph && visualIndex >= 0)
            {
                if (customPrefabId > 0)
                {
                    ActivateCustomVisual(customPrefabId, visualIndex, startPosition, launchVelocity.normalized, visualScale);
                }
                else
                {
                    ActivateVisual(visualIndex, ProjectileType.Parabolic, startPosition, launchVelocity.normalized, visualScale);
                }
            }
            
            return index;
        }
        
        /// <summary>
        /// Calculates launch velocity to hit target in specified time.
        /// v₀ = (displacement - 0.5·g·t²) / t
        /// </summary>
        private Vector3 CalculateLaunchVelocityFromTime(Vector3 start, Vector3 target, float time, float gravityValue)
        {
            Vector3 displacement = target - start;
            
            // Horizontal velocity: vx = dx / t
            float vx = displacement.x / time;
            float vz = displacement.z / time;
            
            // Vertical velocity: vy = (dy + 0.5·g·t²) / t
            // From: y = y₀ + v₀y·t - 0.5·g·t²
            // Rearranged: v₀y = (y - y₀ + 0.5·g·t²) / t
            float vy = (displacement.y + 0.5f * gravityValue * time * time) / time;
            
            return new Vector3(vx, vy, vz);
        }
        
        /// <summary>
        /// Calculates launch velocity for a given launch angle.
        /// Returns the velocity and outputs the calculated flight time.
        /// </summary>
        private Vector3 CalculateLaunchVelocityFromAngle(
            Vector3 start,
            Vector3 target,
            float angleDegrees,
            float gravityValue,
            out float flightTime)
        {
            Vector3 displacement = target - start;
            float horizontalDistance = new Vector2(displacement.x, displacement.z).magnitude;
            float verticalDistance = displacement.y;
            
            float angleRad = angleDegrees * Mathf.Deg2Rad;
            float cosAngle = Mathf.Cos(angleRad);
            float sinAngle = Mathf.Sin(angleRad);
            float tanAngle = Mathf.Tan(angleRad);
            
            // From projectile motion:
            // x = v₀·cos(θ)·t
            // y = v₀·sin(θ)·t - 0.5·g·t²
            //
            // Solving for v₀:
            // v₀² = (g·x²) / (2·cos²(θ)·(x·tan(θ) - y))
            
            float denominator = 2f * cosAngle * cosAngle * (horizontalDistance * tanAngle - verticalDistance);
            
            // Check if trajectory is possible
            if (denominator <= 0)
            {
                // Can't reach target with this angle, use fallback
                flightTime = horizontalDistance / 10f; // Estimate
                return CalculateLaunchVelocityFromTime(start, target, flightTime, gravityValue);
            }
            
            float v0Squared = (gravityValue * horizontalDistance * horizontalDistance) / denominator;
            float v0 = Mathf.Sqrt(v0Squared);
            
            // Calculate flight time: t = x / (v₀·cos(θ))
            flightTime = horizontalDistance / (v0 * cosAngle);
            
            // Calculate velocity components
            Vector3 horizontalDir = new Vector3(displacement.x, 0, displacement.z).normalized;
            float vHorizontal = v0 * cosAngle;
            float vVertical = v0 * sinAngle;
            
            return horizontalDir * vHorizontal + Vector3.up * vVertical;
        }
        
        #endregion
        
        #region Update Loop
        
        private void Update()
        {
            _projectilesFiredThisFrame = 0;
            _projectilesHitThisFrame = 0;
            
            float deltaTime = Time.deltaTime;
            float currentTime = Time.time;
            
            if (useJobsSystem && _activeProjectileCount > 50)
            {
                UpdateProjectilesWithJobs(deltaTime, currentTime);
            }
            else
            {
                UpdateProjectilesSequential(deltaTime, currentTime);
            }
            
            // Process hit detection (batched)
            ProcessHitDetection();
            
            // Update visuals
            UpdateVisuals();
            
            // Update trail VFX positions
            UpdateTrailVFX();
            
            // Process pending trail releases (replaces coroutine-based deactivation)
            ProcessPendingTrailReleases(currentTime);

            // Process pending projectile visual releases so particle-based projectiles can fade naturally
            ProcessPendingProjectileVisualReleases(currentTime);
            
            // Reclaim finished VFX back to pools
            ReclaimFinishedVFX(currentTime);
            
            // Reclaim finished VFX from VFXSpawner (handles ParticleSystem pools for VFXReference)
            _vfxSpawner?.ReclaimFinishedVFX();
        }
        
        private void UpdateProjectilesSequential(float deltaTime, float currentTime)
        {
            for (int i = 0; i < maxProjectiles; i++)
            {
                ProjectileData data = _projectiles[i];
                
                if (!data.isActive) continue;
                
                // Check lifetime
                float age = currentTime - data.spawnTime;
                if (age > data.lifetime)
                {
                    DeactivateProjectile(i);
                    continue;
                }
                
                // Store previous position for hit detection
                float3 previousPosition = data.position;
                
                // Update position based on type
                switch (data.type)
                {
                    case ProjectileType.Linear:
                        // Simple linear motion: p = p₀ + v·t
                        data.position += data.velocity * deltaTime;
                        break;
                        
                    case ProjectileType.Parabolic:
                        // Apply gravity: v = v₀ - g·t, p = p₀ + v·dt
                        float projectileGravity = data.simulationGravity > 0f ? data.simulationGravity : gravity;
                        data.velocity.y -= projectileGravity * deltaTime;
                        data.position += data.velocity * deltaTime;
                        
                        // Check if below target Y (hit ground)
                        if (data.position.y <= data.targetPosition.y && age > 0.1f)
                        {
                            data.position.y = data.targetPosition.y;
                            data.hasHit = true;
                        }
                        break;
                        
                    case ProjectileType.Homing:
                        // Track target - aim at center mass, not feet
                        if (data.targetAgentIndex >= 0)
                        {
                            Vector3 targetPos = GetAgentPosition(data.targetAgentIndex, useHeightOffset: true);
                            if (targetPos != Vector3.zero)
                            {
                                float3 toTarget = (float3)targetPos - data.position;
                                if (math.lengthsq(toTarget) > 0.0001f)
                                {
                                    float3 desiredDir = math.normalize(toTarget);
                                    float speed = math.length(data.velocity);
                                    float3 currentDir = speed > 0.0001f
                                        ? data.velocity / speed
                                        : desiredDir;

                                    float maxRadiansDelta = math.radians(math.max(0f, data.homingTurnRate)) * deltaTime;
                                    float dot = math.clamp(math.dot(currentDir, desiredDir), -1f, 1f);
                                    float angle = math.acos(dot);

                                    if (angle > 0.0001f && maxRadiansDelta > 0f && maxRadiansDelta < angle)
                                    {
                                        float t = maxRadiansDelta / angle;
                                        currentDir = math.normalize(math.lerp(currentDir, desiredDir, t));
                                    }
                                    else
                                    {
                                        currentDir = desiredDir;
                                    }

                                    speed = math.max(0f, speed + data.homingAcceleration * deltaTime);
                                    data.velocity = currentDir * speed;
                                }
                            }
                        }
                        data.position += data.velocity * deltaTime;
                        break;
                }
                
                _projectiles[i] = data;
                
                // Queue hit check
                if (!data.hasHit)
                {
                    TryExecuteTimedFlightActions(i, data, currentTime);
                    _pendingHitChecks.Add(i);
                }
                else
                {
                    // Process hit immediately for parabolic (AOE)
                    ProcessProjectileHit(i);
                }
            }
        }
        
        private void UpdateProjectilesWithJobs(float deltaTime, float currentTime)
        {
            // TODO: Implement Jobs version for even better performance
            // For now, fall back to sequential
            UpdateProjectilesSequential(deltaTime, currentTime);
        }
        
        #endregion
        
        #region Hit Detection
        
        private void ProcessHitDetection()
        {
            int checksThisFrame = Mathf.Min(maxRaycastsPerFrame, _pendingHitChecks.Count);
            
            for (int i = 0; i < checksThisFrame; i++)
            {
                if (_hitCheckIndex >= _pendingHitChecks.Count)
                    _hitCheckIndex = 0;
                
                int projectileIndex = _pendingHitChecks[_hitCheckIndex];
                ProjectileData data = _projectiles[projectileIndex];
                
                if (!data.isActive)
                {
                    _pendingHitChecks.RemoveAt(_hitCheckIndex);
                    continue;
                }
                
                // For trajectory projectiles, do raycast hit detection during flight.
                // Keep legacy Arc behavior unless trajectory pass-through is active.
                bool shouldProcessInFlightHitDetection =
                    data.type == ProjectileType.Linear ||
                    data.type == ProjectileType.Homing ||
                    (data.type == ProjectileType.Parabolic && IsTrajectoryPassThroughActive(data));

                if (shouldProcessInFlightHitDetection)
                {
                    float3 direction = math.lengthsq(data.velocity) > 0.0001f
                        ? math.normalize(data.velocity)
                        : new float3(0f, 0f, 1f);
                    float distance = Mathf.Max(math.length(data.velocity) * Time.deltaTime * 2f, hitDetectionRadius * 1.25f); // Check ahead
                    bool sphereCastHit = Physics.SphereCast(data.position, hitDetectionRadius, direction, out RaycastHit hit, distance, hitLayerMask);
                    
                    if (sphereCastHit)
                    {
                        if (enableDebugLogging)
                        {
                            Debug.Log($"[PROJECTILE DEBUG] SphereCast HIT! collider={hit.collider.name}, point={hit.point}, layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}");
                        }
                        
                        // Check if we hit a valid target and get the agent index
                        int hitAgentIndex = GetHitAgentIndex(data, hit.collider);
                        if (enableDebugLogging)
                        {
                            Debug.Log($"[PROJECTILE DEBUG] GetHitAgentIndex returned: {hitAgentIndex}, IsEnvironmentHit={IsEnvironmentHit(hit.collider)}");
                        }
                        
                        bool isEnvironmentHit = IsEnvironmentHit(hit.collider);
                        
                        if (hitAgentIndex >= 0)
                        {
                            // Trajectory piercing handling for Projectile targeting skills.
                            // If handled as pass-through, keep flying. Otherwise process terminal impact.
                            if (TryHandleSkillTrajectoryPierceHit(projectileIndex, ref data, hitAgentIndex, hit.point, direction, out bool shouldImpactNow))
                            {
                                if (shouldImpactNow)
                                {
                                    data.hasHit = true;
                                    data.position = hit.point;
                                    data.targetAgentIndex = hitAgentIndex;
                                    _projectiles[projectileIndex] = data;
                                    ProcessProjectileHit(projectileIndex);
                                }
                                else
                                {
                                    _projectiles[projectileIndex] = data;
                                }
                            }
                            else
                            {
                                data.hasHit = true;
                                data.position = hit.point;
                                data.targetAgentIndex = hitAgentIndex;
                                _projectiles[projectileIndex] = data;
                                ProcessProjectileHit(projectileIndex);
                            }
                        }
                        else if (isEnvironmentHit)
                        {
                            // If collider-based hit detection doesn't resolve an agent,
                            // still try distance/virtual-hitbox proximity before treating it as terminal environment impact.
                            if (TryHandleSkillTrajectoryProximityHit(projectileIndex, ref data, direction, out bool shouldImpactNow))
                            {
                                if (shouldImpactNow)
                                {
                                    data.hasHit = true;
                                    _projectiles[projectileIndex] = data;
                                    ProcessProjectileHit(projectileIndex);
                                }
                                else
                                {
                                    _projectiles[projectileIndex] = data;
                                }
                            }
                            else
                            {
                                data.hasHit = true;
                                data.position = hit.point;
                                _projectiles[projectileIndex] = data;
                                ProcessProjectileHit(projectileIndex);
                            }
                        }
                        else
                        {
                            // Non-environment, non-target collider (e.g., ally/other collider noise):
                            // still allow virtual-hitbox trajectory fallback to resolve cross-path targets.
                            if (TryHandleSkillTrajectoryProximityHit(projectileIndex, ref data, direction, out bool shouldImpactNow))
                            {
                                if (shouldImpactNow)
                                {
                                    data.hasHit = true;
                                    _projectiles[projectileIndex] = data;
                                    ProcessProjectileHit(projectileIndex);
                                }
                                else
                                {
                                    _projectiles[projectileIndex] = data;
                                }
                            }
                        }
                    }
                    else
                    {
                        // First try terminal target proximity (for distance-based agents without colliders).
                        // Then try trajectory pass-through fallback.
                        if (TryResolveSkillTerminalTargetProximityHit(projectileIndex, ref data))
                        {
                            // Terminal impact resolved.
                        }
                        else if (TryHandleSkillTrajectoryProximityHit(projectileIndex, ref data, direction, out bool shouldImpactNow))
                        {
                            if (shouldImpactNow)
                            {
                                data.hasHit = true;
                                _projectiles[projectileIndex] = data;
                                ProcessProjectileHit(projectileIndex);
                            }
                            else
                            {
                                _projectiles[projectileIndex] = data;
                            }
                        }
                    }
                    
                    // Also check if we've passed the max range without hitting anything.
                    // Applies to straight and homing trajectories; parabolic uses ballistic terminal hit.
                    bool checkRangeEndpoint = data.type == ProjectileType.Linear || data.type == ProjectileType.Homing;
                    float distStartToCurrent = math.distance(data.startPosition, data.position);
                    float maxRange = math.distance(data.startPosition, data.targetPosition);
                    
                    if (checkRangeEndpoint && distStartToCurrent > maxRange && !data.hasHit)
                    {
                        // Reached range endpoint without collider hit.
                        // For targeted/skill/AOE projectiles, resolve an impact so OnHit effects still execute.
                        bool shouldResolveAsImpact =
                            data.networkProjectileId != 0 ||
                            data.targetAgentIndex >= 0 ||
                            data.aoeRadius > 0f;
                        
                        if (shouldResolveAsImpact)
                        {
                            data.hasHit = true;
                            data.position = data.targetPosition;
                            _projectiles[projectileIndex] = data;
                            ProcessProjectileHit(projectileIndex);
                        }
                        else
                        {
                            DeactivateProjectile(projectileIndex);
                        }
                    }
                }
                
                _pendingHitChecks.RemoveAt(_hitCheckIndex);
            }
        }
        
        /// <summary>
        /// Handles pass-through trajectory hits for skill projectiles.
        /// Pass-through is active when:
        /// - Trajectory Piercing is toggled on, or
        /// - The skill has On Trajectory Hit actions, or
        /// - Target hit behavior is set to PierceThrough, or
        /// - Target hit behavior is set to StopOnDesignatedTarget* (ignore blockers).
        /// Returns true when the hit was consumed by trajectory logic.
        /// </summary>
        private bool TryHandleSkillTrajectoryPierceHit(
            int projectileIndex,
            ref ProjectileData data,
            int hitAgentIndex,
            Vector3 hitPoint,
            float3 direction,
            out bool shouldImpactNow)
        {
            shouldImpactNow = false;
            
            if (hitAgentIndex < 0 || data.networkProjectileId == 0)
                return false;
            
            if (!_skillProjectileCallbacks.TryGetValue(data.networkProjectileId, out var callbackData))
                return false;
            
            var (skill, context, targeting) = callbackData;
            if (skill == null || context.Controller == null || targeting == null)
                return false;

            int primaryTargetIndex = context.PrimaryTarget?.index ?? -1;
            bool isPrimaryTargetHit = primaryTargetIndex >= 0 && hitAgentIndex == primaryTargetIndex;
            bool piercePrimaryTarget = targeting.targetHitBehavior == ProjectileTargetHitBehavior.PierceThrough;
            bool stopOnDesignatedTargetOnly =
                targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithFade ||
                targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithoutFade;
            if (isPrimaryTargetHit && !piercePrimaryTarget)
            {
                // Let normal impact flow resolve OnHit actions and projectile termination.
                return false;
            }
            
            bool hasTrajectoryActions = skill.OnTrajectoryActions != null && skill.OnTrajectoryActions.Count > 0;
            bool trajectoryPiercingActive =
                targeting.enableTrajectoryPiercing || hasTrajectoryActions || piercePrimaryTarget || stopOnDesignatedTargetOnly;
            
            if (!trajectoryPiercingActive || !IsTrajectoryPiercingSupported(targeting.trajectory))
                return false;
            
            // Non-primary hits are consumed as trajectory crossings in pass-through mode.
            // Primary target hits are handled by terminal impact flow above (unless PierceThrough is selected).
            
            if (!_trajectoryPierceStates.TryGetValue(projectileIndex, out var state))
            {
                state = new TrajectoryPierceState();
                _trajectoryPierceStates[projectileIndex] = state;
            }
            
            // Prevent repeated hits while the projectile is still inside the same collider.
            if (state.hitAgents.Contains(hitAgentIndex))
            {
                NudgeProjectileForward(ref data, hitPoint, direction);
                return true;
            }
            
            var hitAgent = context.Controller.GetAgent(hitAgentIndex);
            if (hitAgent == null || hitAgent.isDead)
            {
                NudgeProjectileForward(ref data, hitPoint, direction);
                return true;
            }
            
            float multiplier = Mathf.Pow(targeting.trajectoryPierceFalloff, state.pierceHitCount);
            var trajectoryContext = new SkillExecutionContext
            {
                Caster = context.Caster,
                CasterTransform = context.CasterTransform,
                PrimaryTarget = hitAgent,
                AllTargets = new CrowdAgent[] { hitAgent },
                TargetPosition = hitPoint,
                Skill = skill,
                Controller = context.Controller,
                IsCritical = context.IsCritical,
                EffectMultiplier = context.EffectMultiplier * multiplier,
                ExecutionId = context.ExecutionId,
                NetworkProjectileId = data.networkProjectileId,
                DamageableTarget = context.DamageableTarget
            };
            
            SkillExecutor.ExecuteActions(skill.OnTrajectoryActions, trajectoryContext);
            
            state.hitAgents.Add(hitAgentIndex);
            state.pierceHitCount++;
            
            // In designated-target-only modes, do not force premature impact via pierce-count cap.
            if (!stopOnDesignatedTargetOnly &&
                targeting.maxTrajectoryPierceTargets > 0 &&
                state.pierceHitCount >= targeting.maxTrajectoryPierceTargets)
            {
                shouldImpactNow = true;
                return true;
            }
            
            NudgeProjectileForward(ref data, hitPoint, direction);
            return true;
        }
        
        /// <summary>
        /// Proximity fallback for skill trajectory pass-through.
        /// Helps trigger OnTrajectory actions when collider-based spherecast misses.
        /// </summary>
        private bool TryHandleSkillTrajectoryProximityHit(
            int projectileIndex,
            ref ProjectileData data,
            float3 direction,
            out bool shouldImpactNow)
        {
            shouldImpactNow = false;
            
            if (_crowdController == null)
            {
                return false;
            }
            
            if (data.networkProjectileId == 0)
            {
                return false;
            }
            
            if (!_skillProjectileCallbacks.TryGetValue(data.networkProjectileId, out var callbackData))
            {
                return false;
            }
            
            var (skill, context, targeting) = callbackData;
            if (skill == null || context.Controller == null || targeting == null)
            {
                return false;
            }

            bool piercePrimaryTarget = targeting.targetHitBehavior == ProjectileTargetHitBehavior.PierceThrough;
            bool stopOnDesignatedTargetOnly =
                targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithFade ||
                targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithoutFade;
            
            bool hasTrajectoryActions = skill.OnTrajectoryActions != null && skill.OnTrajectoryActions.Count > 0;
            bool trajectoryPiercingActive =
                targeting.enableTrajectoryPiercing || hasTrajectoryActions || piercePrimaryTarget || stopOnDesignatedTargetOnly;
            if (!trajectoryPiercingActive || !IsTrajectoryPiercingSupported(targeting.trajectory))
            {
                return false;
            }
            
            // Distance-mode agents don't use colliders and projectiles fly at chest height,
            // so use a wider 3D search sphere and then filter with horizontal/vertical gates.
            float horizontalRadius = hitDetectionRadius * 1.5f;
            float verticalTolerance = AgentTargetHeightOffset + hitDetectionRadius + 0.5f;
            float searchRadius = Mathf.Sqrt(horizontalRadius * horizontalRadius + verticalTolerance * verticalTolerance);
            
            var nearbyAgents = _crowdController.GetCrowdAgentsInRadius(data.position, searchRadius);
            if (nearbyAgents == null || nearbyAgents.Count == 0)
            {
                return false;
            }
            
            float3 safeDirection = math.lengthsq(direction) > 0.0001f
                ? math.normalize(direction)
                : math.normalize(data.velocity);
            if (math.lengthsq(safeDirection) <= 0.0001f)
            {
                safeDirection = new float3(0f, 0f, 1f);
            }
            
            int bestAgentIndex = -1;
            Vector3 bestHitPoint = data.position;
            float bestForwardDistance = float.MaxValue;
            float bestHorizontalDistance = float.MaxValue;
            
            for (int i = 0; i < nearbyAgents.Count; i++)
            {
                var agent = nearbyAgents[i];
                if (agent == null || agent.enemyCrowdIndex == data.sourceFactionIndex)
                    continue;
                
                int candidateIndex = agent.index;
                if (candidateIndex < 0)
                    continue;
                
                Vector3 candidatePos = GetAgentPosition(candidateIndex, useHeightOffset: true);
                if (candidatePos == Vector3.zero)
                {
                    candidatePos = GetAgentPosition(candidateIndex);
                }
                
                float3 toAgent = candidatePos - (Vector3)data.position;
                float forwardDistance = math.dot(toAgent, safeDirection);
                
                // Ignore mostly-behind candidates for forward-traveling projectiles.
                if (forwardDistance < -horizontalRadius * 0.25f)
                {
                    continue;
                }
                
                float3 lateral = toAgent - safeDirection * math.max(0f, forwardDistance);
                float horizontalDistance = math.length(new float2(lateral.x, lateral.z));
                if (horizontalDistance > horizontalRadius)
                {
                    continue;
                }
                
                float verticalDelta = Mathf.Abs(candidatePos.y - ((Vector3)data.position).y);
                if (verticalDelta > verticalTolerance)
                {
                    continue;
                }
                
                if (forwardDistance < bestForwardDistance ||
                    (Mathf.Abs(forwardDistance - bestForwardDistance) < 0.001f && horizontalDistance < bestHorizontalDistance))
                {
                    bestForwardDistance = forwardDistance;
                    bestHorizontalDistance = horizontalDistance;
                    bestAgentIndex = candidateIndex;
                    bestHitPoint = candidatePos;
                }
            }
            
            if (bestAgentIndex < 0)
            {
                return false;
            }
            
            bool handled = TryHandleSkillTrajectoryPierceHit(
                projectileIndex,
                ref data,
                bestAgentIndex,
                bestHitPoint,
                safeDirection,
                out shouldImpactNow);
            
            if (handled && shouldImpactNow)
            {
                data.position = bestHitPoint;
                data.targetAgentIndex = bestAgentIndex;
            }
            
            return handled;
        }
        
        private void NudgeProjectileForward(ref ProjectileData data, Vector3 hitPoint, float3 direction)
        {
            float3 safeDirection = math.lengthsq(direction) > 0.0001f
                ? math.normalize(direction)
                : math.normalize(data.velocity);
            
            if (math.lengthsq(safeDirection) <= 0.0001f)
            {
                safeDirection = new float3(0f, 0f, 1f);
            }
            
            data.position = (float3)hitPoint + safeDirection * (hitDetectionRadius + 0.05f);
        }

        private bool IsTrajectoryPassThroughActive(ProjectileData data)
        {
            if (data.networkProjectileId == 0)
                return false;

            if (!_skillProjectileCallbacks.TryGetValue(data.networkProjectileId, out var callbackData))
                return false;

            var skill = callbackData.skill;
            var targeting = callbackData.targeting;
            if (skill == null || targeting == null)
                return false;

            bool hasTrajectoryActions = skill.OnTrajectoryActions != null && skill.OnTrajectoryActions.Count > 0;
            bool piercePrimaryTarget = targeting.targetHitBehavior == ProjectileTargetHitBehavior.PierceThrough;
            bool stopOnDesignatedTargetOnly =
                targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithFade ||
                targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithoutFade;
            return (targeting.enableTrajectoryPiercing || hasTrajectoryActions || piercePrimaryTarget || stopOnDesignatedTargetOnly) &&
                   IsTrajectoryPiercingSupported(targeting.trajectory);
        }

        /// <summary>
        /// Executes projectile skill actions that are configured for timed repeats during flight.
        /// These actions are independent of pass-through hit events.
        /// </summary>
        private void TryExecuteTimedFlightActions(int projectileIndex, ProjectileData data, float currentTime)
        {
            if (data.networkProjectileId == 0)
                return;

            if (!_skillProjectileCallbacks.TryGetValue(data.networkProjectileId, out var callbackData))
                return;

            var (skill, context, _) = callbackData;
            if (skill == null || context.Controller == null)
                return;

            var timedActions = skill.OnFlightActions;
            if (timedActions == null || timedActions.Count == 0)
                return;

            float interval = Mathf.Max(0.05f, skill.OnFlightActionsInterval);
            float startDelay = Mathf.Max(0f, skill.OnFlightActionsStartDelay);
            int maxTicks = Mathf.Max(0, skill.OnFlightActionsMaxTicks);

            if (!_timedFlightActionStates.TryGetValue(projectileIndex, out var state))
            {
                state = new TimedFlightActionState
                {
                    nextExecutionTime = data.spawnTime + startDelay,
                    executedTicks = 0
                };
                _timedFlightActionStates[projectileIndex] = state;
            }

            if (maxTicks > 0 && state.executedTicks >= maxTicks)
                return;

            if (currentTime < state.nextExecutionTime)
                return;

            var timedContext = new SkillExecutionContext
            {
                Caster = context.Caster,
                CasterTransform = context.CasterTransform,
                PrimaryTarget = context.PrimaryTarget,
                AllTargets = System.Array.Empty<CrowdAgent>(),
                TargetPosition = (Vector3)data.position,
                Skill = skill,
                Controller = context.Controller,
                IsCritical = context.IsCritical,
                EffectMultiplier = context.EffectMultiplier,
                ExecutionId = context.ExecutionId,
                NetworkProjectileId = data.networkProjectileId,
                DamageableTarget = context.DamageableTarget
            };

            SkillExecutor.ExecuteActions(timedActions, timedContext);

            state.executedTicks++;
            state.nextExecutionTime = currentTime + interval;
            _timedFlightActionStates[projectileIndex] = state;
        }

        /// <summary>
        /// Resolves terminal impact for skill projectiles in distance/virtual-hitbox setups
        /// where collider hits may not fire for agents.
        /// </summary>
        private bool TryResolveSkillTerminalTargetProximityHit(int projectileIndex, ref ProjectileData data)
        {
            if (data.networkProjectileId == 0 || _crowdController == null)
                return false;

            if (!_skillProjectileCallbacks.TryGetValue(data.networkProjectileId, out var callbackData))
                return false;

            var (skill, context, targeting) = callbackData;
            if (skill == null || context.Controller == null || targeting == null)
                return false;

            // If primary target is configured to pierce, keep traveling.
            if (targeting.targetHitBehavior == ProjectileTargetHitBehavior.PierceThrough)
                return false;

            int primaryTargetIndex = context.PrimaryTarget?.index ?? -1;
            if (primaryTargetIndex < 0)
                return false;

            var primaryTarget = context.Controller.GetAgent(primaryTargetIndex);
            if (primaryTarget == null || primaryTarget.isDead)
                return false;

            if (primaryTarget.enemyCrowdIndex == data.sourceFactionIndex)
                return false;

            Vector3 targetPos = GetAgentPosition(primaryTargetIndex, useHeightOffset: true);
            if (targetPos == Vector3.zero)
            {
                targetPos = GetAgentPosition(primaryTargetIndex, useHeightOffset: false);
                if (targetPos == Vector3.zero)
                    return false;
            }

            float impactDistance = hitDetectionRadius + 0.75f;
            if (((Vector3)data.position - targetPos).sqrMagnitude > impactDistance * impactDistance)
                return false;

            data.hasHit = true;
            data.position = targetPos;
            data.targetAgentIndex = primaryTargetIndex;
            _projectiles[projectileIndex] = data;
            ProcessProjectileHit(projectileIndex);
            return true;
        }

        private static bool IsTrajectoryPiercingSupported(SkillProjectileTrajectory trajectory)
        {
            return trajectory == SkillProjectileTrajectory.Linear ||
                   trajectory == SkillProjectileTrajectory.Arc ||
                   trajectory == SkillProjectileTrajectory.Homing;
        }
        
        /// <summary>
        /// Gets the agent index for a hit collider, or -1 if not an enemy agent.
        /// Returns -1 for same faction (friendly fire prevention).
        /// </summary>
        private int GetHitAgentIndex(ProjectileData data, Collider collider)
        {
            if (TryGetCrowdAgentIndexFromCollider(collider, out int hitAgentIndex))
            {
                var hitAgent = _crowdController.GetAgent(hitAgentIndex);
                if (hitAgent != null && hitAgent.enemyCrowdIndex != data.sourceFactionIndex)
                {
                    return hitAgentIndex;
                }
                return -1;
            }
            
            return -1; // Not an agent
        }
        
        /// <summary>
        /// Checks if the collider is environment (not an agent).
        /// </summary>
        private bool IsEnvironmentHit(Collider collider)
        {
            // If it maps to a crowd agent, it's not environment.
            if (TryGetCrowdAgentIndexFromCollider(collider, out _))
                return false;

            // Keep non-crowd navigation agents treated as non-environment (for example external controllables).
            var navigationAgent = collider.GetComponentInParent<INavigationAgent>();
            return navigationAgent == null;
        }
        
        private bool IsValidHitTarget(ProjectileData data, Collider collider)
        {
            if (TryGetCrowdAgentIndexFromCollider(collider, out int hitAgentIndex))
            {
                var hitAgent = _crowdController.GetAgent(hitAgentIndex);
                return hitAgent != null && hitAgent.enemyCrowdIndex != data.sourceFactionIndex;
            }
            
            // Hit environment or other object
            return true;
        }

        private bool TryGetCrowdAgentIndexFromCollider(Collider collider, out int hitAgentIndex)
        {
            hitAgentIndex = -1;
            if (collider == null || _crowdController == null)
                return false;

            var navigationAgent = collider.GetComponentInParent<INavigationAgent>();
            if (navigationAgent == null)
                return false;

            hitAgentIndex = _crowdController.FindAgentIndexByNavigationAgent(navigationAgent);
            return hitAgentIndex >= 0;
        }
        
        private void ProcessProjectileHit(int projectileIndex)
        {
            ProjectileData data = _projectiles[projectileIndex];
            
            if (enableDebugLogging)
            {
                Debug.Log($"[PROJ HIT] ProcessProjectileHit called - idx={projectileIndex}, networkId={data.networkProjectileId}, targetAgent={data.targetAgentIndex}, callbacksCount={_skillProjectileCallbacks.Count}");
            }
            
            // Check if this is a skill projectile with callbacks (non-zero ID includes negative IDs for single-player)
            if (data.networkProjectileId != 0 && _skillProjectileCallbacks.TryGetValue(data.networkProjectileId, out var callback))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[PROJ HIT] Found skill callback for projectile {data.networkProjectileId}, processing skill effects");
                }
                ProcessSkillProjectileHitInternal(projectileIndex, data, callback);
                _skillProjectileCallbacks.Remove(data.networkProjectileId);
                DeactivateProjectile(projectileIndex);
                _projectilesHitThisFrame++;
                return;
            }
            else if (data.networkProjectileId != 0)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"[PROJ HIT] Skill projectile {data.networkProjectileId} has NO callback! Available IDs: {string.Join(", ", _skillProjectileCallbacks.Keys)}");
                }
            }
            
            // Standard projectile processing (non-skill)
            // Spawn impact VFX and play impact sound
            ProjectileEffectsConfig effectsConfig = null;
            if (data.effectsConfigId > 0 && _effectsConfigs.TryGetValue(data.effectsConfigId, out effectsConfig))
            {
                Vector3 hitPosition = data.position;
                Vector3 hitNormal = -((Vector3)data.velocity).normalized;
                
                // Spawn impact VFX (using new VFXReference system)
                var impactVFX = effectsConfig.GetEffectiveImpactVFX();
                if (impactVFX != null && impactVFX.IsValid)
                {
                    SpawnVFX(impactVFX, hitPosition, hitNormal);
                }
                
                // Play impact sound
                if (effectsConfig.impactSound != null)
                {
                    PlaySoundAtPosition(effectsConfig.impactSound, hitPosition, effectsConfig.impactSoundVolume, effectsConfig.impactSoundPitchVariation, effectsConfig.impactSoundSpatialBlend);
                }
            }

            if (!NetworkAuthorityProvider.IsServerAuthority)
            {
                _projectilesHitThisFrame++;
                DeactivateProjectile(projectileIndex);
                return;
            }
            
            if (data.aoeRadius > 0)
            {
                // AOE damage
                ApplyAOEDamage(data.position, data.aoeRadius, data.damage, data.sourceFactionIndex, data.sourceAgentIndex);
                
                // Spawn AOE VFX (using new VFXReference system)
                var aoeVFX = effectsConfig?.GetEffectiveAoeVFX();
                if (aoeVFX != null && aoeVFX.IsValid)
                {
                    SpawnAOEVFX(aoeVFX, data.position, data.aoeRadius);
                }
            }
            else if (data.targetAgentIndex >= 0)
            {
                // Single target damage
                ApplyDamageToAgent(data.targetAgentIndex, data.damage, data.sourceAgentIndex);
            }
            
            _projectilesHitThisFrame++;
            
            // Deactivate projectile
            DeactivateProjectile(projectileIndex);
        }
        
        /// <summary>
        /// Internal processing for skill projectile hits - applies all skill effects
        /// </summary>
        private void ProcessSkillProjectileHitInternal(int projectileIndex, ProjectileData data, (AgentSkill skill, SkillExecutionContext context, ProjectileTargetingConfig targeting) callbackData)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[PROJECTILE DEBUG] ProcessSkillProjectileHitInternal - projectileIdx={projectileIndex}, networkId={data.networkProjectileId}, targetAgentIndex={data.targetAgentIndex}, aoeRadius={data.aoeRadius}");
            }
            
            Vector3 hitPosition = data.position;
            Vector3 hitNormal = -((Vector3)data.velocity).normalized;
            
            // Spawn impact VFX
            ProjectileEffectsConfig effectsConfig = null;
            if (data.effectsConfigId > 0 && _effectsConfigs.TryGetValue(data.effectsConfigId, out effectsConfig))
            {
                var impactVFX = effectsConfig.GetEffectiveImpactVFX();
                if (impactVFX != null && impactVFX.IsValid)
                {
                    SpawnVFX(impactVFX, hitPosition, hitNormal);
                }
                
                if (effectsConfig.impactSound != null)
                {
                    PlaySoundAtPosition(effectsConfig.impactSound, hitPosition, effectsConfig.impactSoundVolume, effectsConfig.impactSoundPitchVariation, effectsConfig.impactSoundSpatialBlend);
                }
            }

            if (!NetworkAuthorityProvider.IsServerAuthority)
                return;
            
            // Gather targets based on AOE
            List<int> hitAgentIndices = new List<int>();
            List<float> damageMultipliers = new List<float>();
            
            if (data.aoeRadius > 0)
            {
                // AOE - find all targets in radius
                var agents = _crowdController.GetCrowdAgentsInRadius(data.position, data.aoeRadius);
                foreach (var agent in agents)
                {
                    // Skip same faction
                    if (agent.enemyCrowdIndex == data.sourceFactionIndex) continue;
                    
                    hitAgentIndices.Add(agent.index);
                    
                    // Calculate falloff damage based on distance
                    float distance = Vector3.Distance(data.position, GetAgentPosition(agent.index));
                    float falloff = 1f - (distance / data.aoeRadius);
                    damageMultipliers.Add(Mathf.Max(0.3f, falloff));
                }
                
                // Spawn AOE VFX
                var aoeVFX = effectsConfig?.GetEffectiveAoeVFX();
                if (aoeVFX != null && aoeVFX.IsValid)
                {
                    SpawnAOEVFX(aoeVFX, data.position, data.aoeRadius);
                }
            }
            else if (data.targetAgentIndex >= 0)
            {
                // Direct hit on single target (targetAgentIndex was set during collision detection)
                hitAgentIndices.Add(data.targetAgentIndex);
                damageMultipliers.Add(1f);
            }
            else
            {
                // Linear projectile without explicit target - check for agents at impact point
                // This handles cases where the projectile collider hit but agent index wasn't captured
                float impactCheckRadius = hitDetectionRadius * 2f; // Slightly larger than collision radius
                var nearbyAgents = _crowdController.GetCrowdAgentsInRadius(data.position, impactCheckRadius);
                foreach (var agent in nearbyAgents)
                {
                    // Skip same faction
                    if (agent.enemyCrowdIndex == data.sourceFactionIndex) continue;
                    
                    hitAgentIndices.Add(agent.index);
                    damageMultipliers.Add(1f);
                    break; // Only hit first target for non-AOE
                }
            }
            
            if (enableDebugLogging)
            {
                Debug.Log($"[PROJECTILE DEBUG] Gathered {hitAgentIndices.Count} hit targets: [{string.Join(", ", hitAgentIndices)}]");
            }
            
            var hitData = new SkillProjectileHitCallback
            {
                projectileIndex = projectileIndex,
                networkProjectileId = data.networkProjectileId,
                hitPosition = hitPosition,
                hitAgentIndices = hitAgentIndices.ToArray(),
                damageMultipliers = damageMultipliers.ToArray(),
                isAOE = data.aoeRadius > 0,
                isPierce = false // TODO: Support pierce projectiles
            };
            
            // Always process OnHit callback, even with no direct targets.
            // Effects like AOEOnImpact/Piercing determine targets from impact context.
            ProcessSkillProjectileHit(data.networkProjectileId, hitData);
            
            if (enableDebugLogging)
            {
                Debug.Log($"[ProjectileSystem] Skill projectile {data.networkProjectileId} hit at {hitPosition}, affected {hitAgentIndices.Count} targets");
            }
        }
        
        private void ApplyDamageToAgent(int agentIndex, float damage, int sourceAgentIndex)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return;
            if (_crowdController == null) return;
            
            var targetAgent = _crowdController.GetAgent(agentIndex);
            if (targetAgent == null || targetAgent.isDead) return;
            
            // Get source agent for direction
            Vector3 damageDirection = Vector3.forward;
            var sourceAgent = _crowdController.GetAgent(sourceAgentIndex);
            if (sourceAgent != null)
            {
                Vector3 direction = targetAgent.WorldPosition - sourceAgent.WorldPosition;
                if (direction.sqrMagnitude > 0.0001f)
                    damageDirection = direction.normalized;
            }
            
            // Apply damage through the controller - pass source agent for ally defense
            _crowdController.DealDamageToAgent(agentIndex, damage, damageDirection, sourceAgentIndex);
            
            if (enableDebugLogging)
            {
                Debug.Log($"[ProjectileSystem] Hit agent {agentIndex} for {damage} damage from agent {sourceAgentIndex}");
            }
        }
        
        private void ApplyAOEDamage(float3 center, float radius, float damage, int sourceFactionIndex, int sourceAgentIndex)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return;
            if (_crowdController == null) return;
            
            // Find all agents in radius
            var agents = _crowdController.GetCrowdAgentsInRadius(center, radius);
            
            foreach (var agent in agents)
            {
                // Skip same faction
                if (agent.enemyCrowdIndex == sourceFactionIndex) continue;
                
                // Calculate falloff damage based on distance
                float distance = Vector3.Distance(center, GetAgentPosition(agent.index));
                float falloff = 1f - (distance / radius);
                float actualDamage = damage * Mathf.Max(0.3f, falloff);
                
                Vector3 damageDirection = (GetAgentPosition(agent.index) - (Vector3)center).normalized;
                // Pass source agent for ally defense
                _crowdController.DealDamageToAgent(agent.index, actualDamage, damageDirection, sourceAgentIndex);
            }
            
            if (enableDebugLogging)
            {
                Debug.Log($"[ProjectileSystem] AOE hit at {center} with radius {radius}, damage {damage}");
            }
        }
        
        /// <summary>
        /// Height offset to aim at center mass instead of feet.
        /// Approximately chest height for humanoid agents.
        /// </summary>
        private const float AgentTargetHeightOffset = 1.2f;
        
        private Vector3 GetAgentPosition(int agentIndex, bool useHeightOffset = false)
        {
            if (_crowdController == null) return Vector3.zero;
            
            var agent = _crowdController.GetAgent(agentIndex);
            if (agent == null)
                return Vector3.zero;

            Vector3 position = agent.WorldPosition;
            if (useHeightOffset)
                position.y += AgentTargetHeightOffset;

            return position;
        }
        
        #endregion
        
        #region Visual Management

        private static bool UsesLinearVisualPool(ProjectileType type)
        {
            return type == ProjectileType.Linear || type == ProjectileType.Homing;
        }
        
        private int AcquireVisual(ProjectileType type)
        {
            Queue<int> freeQueue;
            List<GameObject> pool;
            GameObject prefab;
            
            if (UsesLinearVisualPool(type))
            {
                freeQueue = _freeLinearVisuals;
                pool = _linearVisualPool;
                prefab = defaultLinearProjectilePrefab;
            }
            else
            {
                freeQueue = _freeParabolicVisuals;
                pool = _parabolicVisualPool;
                prefab = defaultParabolicProjectilePrefab;
            }
            
            // If no prefab assigned, create a simple fallback visual
            if (prefab == null)
            {
                Debug.LogWarning($"[ProjectileSystem] No prefab assigned for {type} projectiles - creating simple fallback visual.");
                prefab = CreateFallbackProjectilePrefab(type);
                if (UsesLinearVisualPool(type))
                    defaultLinearProjectilePrefab = prefab;
                else
                    defaultParabolicProjectilePrefab = prefab;
            }
            
            if (freeQueue.Count > 0)
            {
                int idx = freeQueue.Dequeue();
                return idx;
            }
            
            // Expand pool
            GameObject visual = Instantiate(prefab, transform);
            visual.SetActive(false);
            pool.Add(visual);
            return pool.Count - 1;
        }
        
        /// <summary>
        /// Acquires a visual from a custom prefab pool (for per-faction prefabs).
        /// Returns a tuple: (prefabInstanceId, visualIndex)
        /// </summary>
        private (int prefabId, int visualIndex) AcquireVisualForPrefab(GameObject customPrefab, float scale)
        {
            if (customPrefab == null)
            {
                return (-1, -1);
            }
            
            int prefabId = customPrefab.GetInstanceID();
            
            // Register in reverse lookup if not already
            if (!_prefabIdLookup.ContainsKey(prefabId))
            {
                _prefabIdLookup[prefabId] = customPrefab;
            }
            
            // Get or create pool for this prefab
            if (!_customPrefabPools.TryGetValue(customPrefab, out var pool))
            {
                pool = new List<GameObject>(32);
                _customPrefabPools[customPrefab] = pool;
                _customPrefabFreeIndices[customPrefab] = new Queue<int>();
            }
            
            var freeQueue = _customPrefabFreeIndices[customPrefab];
            
            // Try to reuse existing visual
            if (freeQueue.Count > 0)
            {
                int idx = freeQueue.Dequeue();
                
                // Apply scale if needed
                if (scale > 0 && idx < pool.Count)
                {
                    pool[idx].transform.localScale = Vector3.one * scale;
                }
                
                return (prefabId, idx);
            }
            
            // Instantiate new visual
            GameObject visual = Instantiate(customPrefab, transform);
            visual.SetActive(false);
            
            // Apply scale
            if (scale > 0)
            {
                visual.transform.localScale = Vector3.one * scale;
            }
            
            pool.Add(visual);
            
            return (prefabId, pool.Count - 1);
        }
        
        /// <summary>
        /// Acquires a VFX Graph projectile visual instance from pool.
        /// Creates pool for asset if it doesn't exist.
        /// </summary>
        private int AcquireVFXGraphProjectileVisual(UnityEngine.VFX.VisualEffectAsset vfxAsset, Vector3 position, Vector3 direction, float speed, float scale, int projectileIndex)
        {
            if (vfxAsset == null)
            {
                Debug.LogWarning("[ProjectileSystem] VFX Graph asset is null");
                return -1;
            }
            
            // Get or create pool for this VFX Graph asset
            if (!_vfxGraphProjectilePools.TryGetValue(vfxAsset, out var pool))
            {
                pool = new List<UnityEngine.VFX.VisualEffect>(32);
                _vfxGraphProjectilePools[vfxAsset] = pool;
                _vfxGraphProjectileFreeIndices[vfxAsset] = new Queue<int>();
            }
            
            var freeQueue = _vfxGraphProjectileFreeIndices[vfxAsset];
            int poolIndex;
            UnityEngine.VFX.VisualEffect vfx;
            
            if (freeQueue.Count > 0)
            {
                // Reuse existing instance
                poolIndex = freeQueue.Dequeue();
                vfx = pool[poolIndex];
                vfx.gameObject.SetActive(true);
            }
            else
            {
                // Create new instance
                GameObject vfxGO = new GameObject($"VFXProjectile_{vfxAsset.name}_{pool.Count}");
                vfxGO.transform.SetParent(transform);
                vfx = vfxGO.AddComponent<UnityEngine.VFX.VisualEffect>();
                vfx.visualEffectAsset = vfxAsset;
                pool.Add(vfx);
                poolIndex = pool.Count - 1;
            }
            
            // Set initial parameters
            UpdateVFXGraphProjectileVisual(vfx, position, direction, speed, scale, true);
            
            // Track the active VFX Graph projectile
            _activeVFXGraphProjectiles[projectileIndex] = (vfxAsset, poolIndex);
            
            return poolIndex;
        }
        
        /// <summary>
        /// Updates a VFX Graph projectile visual with new position/direction/velocity.
        /// </summary>
        private void UpdateVFXGraphProjectileVisual(UnityEngine.VFX.VisualEffect vfx, Vector3 position, Vector3 direction, float speed, float scale, bool isSpawn)
        {
            if (vfx == null) return;
            
            // Update position via property (GPU-side)
            if (vfx.HasVector3(VFXProjectilePositionId))
            {
                vfx.SetVector3(VFXProjectilePositionId, position);
            }
            else
            {
                // Fallback: move the GameObject
                vfx.transform.position = position;
            }
            
            // Update direction
            if (vfx.HasVector3(VFXProjectileDirectionId))
            {
                vfx.SetVector3(VFXProjectileDirectionId, direction);
            }
            else
            {
                // Fallback: rotate the GameObject
                if (direction != Vector3.zero)
                {
                    vfx.transform.rotation = Quaternion.LookRotation(direction);
                }
            }
            
            // Update velocity
            if (vfx.HasFloat(VFXProjectileVelocityId))
            {
                vfx.SetFloat(VFXProjectileVelocityId, speed);
            }
            
            // Update scale
            if (vfx.HasFloat(VFXProjectileScaleId))
            {
                vfx.SetFloat(VFXProjectileScaleId, scale);
            }
            else
            {
                vfx.transform.localScale = Vector3.one * scale;
            }
            
            // Set active state
            if (vfx.HasBool(VFXProjectileActiveId))
            {
                vfx.SetBool(VFXProjectileActiveId, true);
            }
            
            // Send spawn event on first activation
            if (isSpawn && vfx.HasInt(VFXOnProjectileSpawnId))
            {
                vfx.SendEvent(VFXOnProjectileSpawnId);
            }
            
            // Reinitialize to apply new values
            if (isSpawn)
            {
                vfx.Reinit();
                vfx.Play();
            }
        }
        
        /// <summary>
        /// Releases a VFX Graph projectile visual back to the pool.
        /// </summary>
        private void ReleaseVFXGraphProjectileVisual(int projectileIndex, bool wasHit)
        {
            if (!_activeVFXGraphProjectiles.TryGetValue(projectileIndex, out var vfxData))
                return;
            
            var (asset, poolIndex) = vfxData;
            
            if (_vfxGraphProjectilePools.TryGetValue(asset, out var pool) && poolIndex < pool.Count)
            {
                var vfx = pool[poolIndex];
                
                // Send hit/destroy event
                if (wasHit && vfx.HasInt(VFXOnProjectileHitId))
                {
                    vfx.SendEvent(VFXOnProjectileHitId);
                }
                else if (vfx.HasInt(VFXOnProjectileDestroyId))
                {
                    vfx.SendEvent(VFXOnProjectileDestroyId);
                }
                
                // Set inactive state
                if (vfx.HasBool(VFXProjectileActiveId))
                {
                    vfx.SetBool(VFXProjectileActiveId, false);
                }
                
                // Stop and deactivate
                vfx.Stop();
                vfx.gameObject.SetActive(false);
                
                // Return to free queue
                _vfxGraphProjectileFreeIndices[asset].Enqueue(poolIndex);
            }
            
            _activeVFXGraphProjectiles.Remove(projectileIndex);
        }
        
        /// <summary>
        /// Creates a simple fallback projectile prefab when none is assigned.
        /// </summary>
        private GameObject CreateFallbackProjectilePrefab(ProjectileType type)
        {
            GameObject fallback = new GameObject($"FallbackProjectile_{type}");
            fallback.transform.SetParent(transform);
            
            // Add a simple sphere mesh for visibility
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(fallback.transform);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = type == ProjectileType.Linear ? new Vector3(0.1f, 0.1f, 0.3f) : new Vector3(0.2f, 0.2f, 0.2f);
            
            // Remove collider (we don't need physics collision)
            var collider = sphere.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            
            // Set a bright color so it's visible
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = type == ProjectileType.Linear ? Color.yellow : Color.red;
                mat.SetColor("_EmissionColor", mat.color * 2f);
                mat.EnableKeyword("_EMISSION");
                renderer.material = mat;
            }
            
            fallback.SetActive(false);
            return fallback;
        }
        
        private void ActivateVisual(int visualIndex, ProjectileType type, Vector3 position, Vector3 direction, float scale = 1f)
        {
            List<GameObject> pool = UsesLinearVisualPool(type) ? _linearVisualPool : _parabolicVisualPool;
            
            if (visualIndex < 0 || visualIndex >= pool.Count) return;
            
            GameObject visual = pool[visualIndex];
            visual.transform.position = position;
            if (direction != Vector3.zero)
            {
                visual.transform.rotation = Quaternion.LookRotation(direction);
            }
            if (scale > 0 && scale != 1f)
            {
                visual.transform.localScale = Vector3.one * scale;
            }
            visual.SetActive(true);
        }
        
        /// <summary>
        /// Activates a visual from a custom prefab pool.
        /// </summary>
        private void ActivateCustomVisual(int prefabId, int visualIndex, Vector3 position, Vector3 direction, float scale = 1f)
        {
            if (prefabId <= 0 || visualIndex < 0) return;
            
            // Look up the prefab from the ID
            if (!_prefabIdLookup.TryGetValue(prefabId, out var prefab)) return;
            if (!_customPrefabPools.TryGetValue(prefab, out var pool)) return;
            
            if (visualIndex < pool.Count)
            {
                GameObject visual = pool[visualIndex];
                visual.transform.position = position;
                if (direction != Vector3.zero)
                {
                    visual.transform.rotation = Quaternion.LookRotation(direction);
                }
                if (scale > 0 && scale != 1f)
                {
                    visual.transform.localScale = Vector3.one * scale;
                }
                visual.SetActive(true);
            }
        }
        
        private void ReleaseVisual(int visualIndex, ProjectileType type, bool allowParticleFade)
        {
            List<GameObject> pool;
            Queue<int> freeQueue;
            
            if (UsesLinearVisualPool(type))
            {
                pool = _linearVisualPool;
                freeQueue = _freeLinearVisuals;
            }
            else
            {
                pool = _parabolicVisualPool;
                freeQueue = _freeParabolicVisuals;
            }
            
            if (visualIndex < 0 || visualIndex >= pool.Count) return;

            ProjectileVisualPoolType poolType = UsesLinearVisualPool(type)
                ? ProjectileVisualPoolType.DefaultLinear
                : ProjectileVisualPoolType.DefaultParabolic;

            if (allowParticleFade && TryScheduleProjectileVisualRelease(poolType, visualIndex, 0))
            {
                return;
            }

            pool[visualIndex].SetActive(false);
            freeQueue.Enqueue(visualIndex);
        }
        
        /// <summary>
        /// Releases a visual back to a custom prefab pool.
        /// </summary>
        private void ReleaseCustomVisual(int prefabId, int visualIndex, bool allowParticleFade)
        {
            if (prefabId <= 0 || visualIndex < 0) return;
            
            // Look up the prefab from the ID
            if (!_prefabIdLookup.TryGetValue(prefabId, out var prefab)) return;
            if (!_customPrefabPools.TryGetValue(prefab, out var pool)) return;
            if (!_customPrefabFreeIndices.TryGetValue(prefab, out var freeQueue)) return;
            
            if (visualIndex < pool.Count)
            {
                if (allowParticleFade && TryScheduleProjectileVisualRelease(ProjectileVisualPoolType.CustomPrefab, visualIndex, prefabId))
                {
                    return;
                }

                pool[visualIndex].SetActive(false);
                freeQueue.Enqueue(visualIndex);
            }
        }

        private bool TryScheduleProjectileVisualRelease(ProjectileVisualPoolType poolType, int visualIndex, int customPrefabId)
        {
            if (visualIndex < 0) return false;

            if (IsProjectileVisualReleasePending(poolType, visualIndex, customPrefabId))
            {
                return true;
            }

            var pending = new PendingProjectileVisualRelease
            {
                poolType = poolType,
                customPrefabId = customPrefabId,
                visualIndex = visualIndex,
                forceReleaseTime = Time.time + MaxProjectileVisualFadeDelay
            };

            if (!TryResolveProjectileVisual(pending, out var visual, out _))
            {
                return false;
            }

            if (visual == null || !visual.activeSelf)
            {
                return false;
            }

            var particleSystems = GetProjectileParticleSystems(pending, visual);
            if (particleSystems == null || particleSystems.Length == 0)
            {
                return false;
            }

            bool hasLiveParticles = false;
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null) continue;

                if (particleSystem.IsAlive(true))
                {
                    hasLiveParticles = true;
                }

                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (!hasLiveParticles)
            {
                return false;
            }

            _pendingProjectileVisualReleases.Add(pending);
            return true;
        }

        private bool IsProjectileVisualReleasePending(ProjectileVisualPoolType poolType, int visualIndex, int customPrefabId)
        {
            for (int i = 0; i < _pendingProjectileVisualReleases.Count; i++)
            {
                var pending = _pendingProjectileVisualReleases[i];
                if (pending.poolType == poolType &&
                    pending.visualIndex == visualIndex &&
                    pending.customPrefabId == customPrefabId)
                {
                    return true;
                }
            }

            return false;
        }

        private ParticleSystem[] GetProjectileParticleSystems(PendingProjectileVisualRelease pending, GameObject visual)
        {
            if (visual == null)
            {
                return null;
            }

            Dictionary<int, ParticleSystem[]> cache = null;

            switch (pending.poolType)
            {
                case ProjectileVisualPoolType.DefaultLinear:
                    cache = _cachedLinearProjectileParticleSystems;
                    break;

                case ProjectileVisualPoolType.DefaultParabolic:
                    cache = _cachedParabolicProjectileParticleSystems;
                    break;

                case ProjectileVisualPoolType.CustomPrefab:
                    if (!_cachedCustomProjectileParticleSystems.TryGetValue(pending.customPrefabId, out cache))
                    {
                        cache = new Dictionary<int, ParticleSystem[]>(32);
                        _cachedCustomProjectileParticleSystems[pending.customPrefabId] = cache;
                    }
                    break;
            }

            if (cache == null)
            {
                return null;
            }

            if (!cache.TryGetValue(pending.visualIndex, out var particleSystems) || particleSystems == null)
            {
                particleSystems = visual.GetComponentsInChildren<ParticleSystem>(true);
                cache[pending.visualIndex] = particleSystems;
            }

            return particleSystems;
        }

        private bool TryResolveProjectileVisual(PendingProjectileVisualRelease pending, out GameObject visual, out Queue<int> freeQueue)
        {
            visual = null;
            freeQueue = null;

            switch (pending.poolType)
            {
                case ProjectileVisualPoolType.DefaultLinear:
                    if (pending.visualIndex >= 0 && pending.visualIndex < _linearVisualPool.Count)
                    {
                        visual = _linearVisualPool[pending.visualIndex];
                        freeQueue = _freeLinearVisuals;
                        return true;
                    }
                    break;

                case ProjectileVisualPoolType.DefaultParabolic:
                    if (pending.visualIndex >= 0 && pending.visualIndex < _parabolicVisualPool.Count)
                    {
                        visual = _parabolicVisualPool[pending.visualIndex];
                        freeQueue = _freeParabolicVisuals;
                        return true;
                    }
                    break;

                case ProjectileVisualPoolType.CustomPrefab:
                    if (!_prefabIdLookup.TryGetValue(pending.customPrefabId, out var prefab))
                    {
                        return false;
                    }
                    if (!_customPrefabPools.TryGetValue(prefab, out var pool))
                    {
                        return false;
                    }
                    if (!_customPrefabFreeIndices.TryGetValue(prefab, out freeQueue))
                    {
                        return false;
                    }
                    if (pending.visualIndex >= 0 && pending.visualIndex < pool.Count)
                    {
                        visual = pool[pending.visualIndex];
                        return true;
                    }
                    break;
            }

            return false;
        }

        private void ProcessPendingProjectileVisualReleases(float currentTime)
        {
            for (int i = _pendingProjectileVisualReleases.Count - 1; i >= 0; i--)
            {
                var pending = _pendingProjectileVisualReleases[i];
                bool shouldRelease = currentTime >= pending.forceReleaseTime;

                if (!TryResolveProjectileVisual(pending, out var visual, out var freeQueue))
                {
                    shouldRelease = true;
                }
                else if (!shouldRelease)
                {
                    var particleSystems = GetProjectileParticleSystems(pending, visual);
                    if (particleSystems == null || particleSystems.Length == 0)
                    {
                        shouldRelease = true;
                    }
                    else
                    {
                        bool particlesAlive = false;
                        for (int j = 0; j < particleSystems.Length; j++)
                        {
                            ParticleSystem particleSystem = particleSystems[j];
                            if (particleSystem != null && particleSystem.IsAlive(true))
                            {
                                particlesAlive = true;
                                break;
                            }
                        }

                        shouldRelease = !particlesAlive;
                    }
                }

                if (!shouldRelease)
                {
                    continue;
                }

                if (TryResolveProjectileVisual(pending, out visual, out freeQueue))
                {
                    if (visual != null)
                    {
                        visual.SetActive(false);
                    }
                    freeQueue?.Enqueue(pending.visualIndex);
                }

                int lastIndex = _pendingProjectileVisualReleases.Count - 1;
                if (i < lastIndex)
                {
                    _pendingProjectileVisualReleases[i] = _pendingProjectileVisualReleases[lastIndex];
                }
                _pendingProjectileVisualReleases.RemoveAt(lastIndex);
            }
        }
        
        #endregion
        
        #region VFX and Audio Helpers
        
        /// <summary>
        /// Spawns a trail VFX and attaches it to track a projectile.
        /// Returns the pool index of the trail instance.
        /// </summary>
        private int SpawnTrailVFX(GameObject trailPrefab, Vector3 position, Vector3 direction, int projectileIndex)
        {
            if (trailPrefab == null) return -1;
            
            int prefabId = trailPrefab.GetInstanceID();
            
            // Register in lookup
            if (!_trailPrefabIdLookup.ContainsKey(prefabId))
            {
                _trailPrefabIdLookup[prefabId] = trailPrefab;
            }
            
            // Get or create pool
            if (!_trailVFXPools.TryGetValue(trailPrefab, out var pool))
            {
                pool = new List<GameObject>(32);
                _trailVFXPools[trailPrefab] = pool;
                _trailVFXFreeIndices[trailPrefab] = new Queue<int>();
            }
            
            var freeQueue = _trailVFXFreeIndices[trailPrefab];
            
            GameObject trailInstance;
            int poolIndex;
            
            if (freeQueue.Count > 0)
            {
                poolIndex = freeQueue.Dequeue();
                trailInstance = pool[poolIndex];
            }
            else
            {
                trailInstance = Instantiate(trailPrefab, transform);
                pool.Add(trailInstance);
                poolIndex = pool.Count - 1;
            }
            
            // Position and activate
            trailInstance.transform.position = position;
            if (direction != Vector3.zero)
            {
                trailInstance.transform.rotation = Quaternion.LookRotation(direction);
            }
            trailInstance.SetActive(true);
            
            // Restart particle system if present
            var particleSystem = trailInstance.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                particleSystem.Clear();
                particleSystem.Play();
            }
            
            // Track this trail
            _activeTrails[projectileIndex] = (prefabId, poolIndex);
            
            return poolIndex;
        }
        
        /// <summary>
        /// Releases a trail VFX back to its pool after a fade delay.
        /// OPTIMIZED: Uses time-based release list instead of coroutines to avoid GC allocations.
        /// </summary>
        private void ReleaseTrailVFX(int projectileIndex)
        {
            if (!_activeTrails.TryGetValue(projectileIndex, out var trailInfo)) return;
            
            var (prefabId, poolIndex) = trailInfo;
            
            if (_trailPrefabIdLookup.TryGetValue(prefabId, out var prefab) &&
                _trailVFXPools.TryGetValue(prefab, out var pool) &&
                _trailVFXFreeIndices.TryGetValue(prefab, out var freeQueue))
            {
                if (poolIndex < pool.Count)
                {
                    var trailInstance = pool[poolIndex];
                    
                    // Stop particle system emission (particles will fade naturally)
                    // Use cached component if available
                    if (!_cachedTrailParticleSystems.TryGetValue(prefabId * 10000 + poolIndex, out var particleSystem))
                    {
                        particleSystem = trailInstance.GetComponent<ParticleSystem>();
                        _cachedTrailParticleSystems[prefabId * 10000 + poolIndex] = particleSystem;
                    }
                    
                    if (particleSystem != null)
                    {
                        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    
                    // Add to pending releases instead of using coroutine
                    _pendingTrailReleases.Add((prefabId, poolIndex, Time.time + TrailFadeDelay));
                    
                    // Return to free queue immediately (it won't be reused until deactivated)
                    freeQueue.Enqueue(poolIndex);
                }
            }
            
            _activeTrails.Remove(projectileIndex);
        }
        
        /// <summary>
        /// Processes pending trail releases and deactivates faded trails.
        /// Called each frame from Update.
        /// </summary>
        private void ProcessPendingTrailReleases(float currentTime)
        {
            for (int i = _pendingTrailReleases.Count - 1; i >= 0; i--)
            {
                var (prefabId, poolIndex, releaseTime) = _pendingTrailReleases[i];
                
                if (currentTime >= releaseTime)
                {
                    // Deactivate the trail
                    if (_trailPrefabIdLookup.TryGetValue(prefabId, out var prefab) &&
                        _trailVFXPools.TryGetValue(prefab, out var pool) &&
                        poolIndex < pool.Count)
                    {
                        var trailInstance = pool[poolIndex];
                        if (trailInstance != null)
                        {
                            trailInstance.SetActive(false);
                        }
                    }
                    
                    // Remove from pending list (swap with last for O(1))
                    int lastIdx = _pendingTrailReleases.Count - 1;
                    if (i < lastIdx)
                        _pendingTrailReleases[i] = _pendingTrailReleases[lastIdx];
                    _pendingTrailReleases.RemoveAt(lastIdx);
                }
            }
        }
        
        /// <summary>
        /// Updates trail VFX positions to follow their projectiles.
        /// </summary>
        private void UpdateTrailVFX()
        {
            foreach (var kvp in _activeTrails)
            {
                int projectileIndex = kvp.Key;
                var (prefabId, poolIndex) = kvp.Value;
                
                if (projectileIndex < 0 || projectileIndex >= _projectiles.Length) continue;
                
                ProjectileData data = _projectiles[projectileIndex];
                if (!data.isActive) continue;
                
                if (_trailPrefabIdLookup.TryGetValue(prefabId, out var prefab) &&
                    _trailVFXPools.TryGetValue(prefab, out var pool))
                {
                    if (poolIndex < pool.Count)
                    {
                        var trailInstance = pool[poolIndex];
                        if (trailInstance != null && trailInstance.activeSelf)
                        {
                            trailInstance.transform.position = data.position;
                            if (math.lengthsq(data.velocity) > 0.01f)
                            {
                                trailInstance.transform.rotation = Quaternion.LookRotation(math.normalize(data.velocity));
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Spawns a VFX at the specified position using the appropriate system (VFX Graph or ParticleSystem).
        /// </summary>
        private void SpawnVFX(VFXReference vfxRef, Vector3 position, Vector3 normal)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return;
            
            if (vfxRef.IsVFXGraph)
            {
                // Use VFXGraphManager for GPU-accelerated VFX Graph
                _vfxSpawner.SpawnImpact(vfxRef, position, normal);
            }
            else if (vfxRef.IsParticleSystem)
            {
                // Use legacy ParticleSystem pooling
                SpawnImpactVFX(vfxRef.particleSystemPrefab, position, normal);
            }
        }
        
        /// <summary>
        /// Spawns an AOE VFX using the appropriate system (VFX Graph or ParticleSystem).
        /// </summary>
        private void SpawnAOEVFX(VFXReference vfxRef, Vector3 position, float radius)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return;
            
            if (vfxRef.IsVFXGraph)
            {
                // Use VFXGraphManager for GPU-accelerated VFX Graph
                _vfxSpawner.SpawnAOEExplosion(vfxRef, position, radius);
            }
            else if (vfxRef.IsParticleSystem)
            {
                // Use legacy ParticleSystem pooling
                SpawnAOEVFX(vfxRef.particleSystemPrefab, position, radius);
            }
        }
        
        /// <summary>
        /// Spawns an impact VFX at the specified position using object pooling.
        /// Legacy method for backward compatibility with GameObject prefabs.
        /// OPTIMIZED: Caches ParticleSystem components to avoid GetComponent calls.
        /// </summary>
        private void SpawnImpactVFX(GameObject impactPrefab, Vector3 position, Vector3 normal)
        {
            if (impactPrefab == null) return;
            
            // Get or create pool for this prefab
            if (!_impactVFXPools.TryGetValue(impactPrefab, out var pool))
            {
                pool = new List<GameObject>(32);
                _impactVFXPools[impactPrefab] = pool;
                _impactVFXFreeIndices[impactPrefab] = new Queue<int>();
                _cachedImpactParticleSystems[impactPrefab] = new Dictionary<int, ParticleSystem>();
            }
            
            var freeQueue = _impactVFXFreeIndices[impactPrefab];
            var cachedPS = _cachedImpactParticleSystems[impactPrefab];
            
            GameObject impact;
            int poolIndex;
            
            if (freeQueue.Count > 0)
            {
                poolIndex = freeQueue.Dequeue();
                impact = pool[poolIndex];
            }
            else
            {
                // Create new instance (cap pool size to prevent memory explosion)
                if (pool.Count >= 128)
                {
                    // Pool exhausted - find oldest and reclaim
                    if (_activeImpacts.Count > 0)
                    {
                        float oldestTime = float.MaxValue;
                        int oldestIdx = -1;
                        for (int i = 0; i < _activeImpacts.Count; i++)
                        {
                            if (_activeImpacts[i].prefab == impactPrefab && _activeImpacts[i].releaseTime < oldestTime)
                            {
                                oldestTime = _activeImpacts[i].releaseTime;
                                oldestIdx = i;
                            }
                        }
                        if (oldestIdx >= 0)
                        {
                            poolIndex = _activeImpacts[oldestIdx].poolIndex;
                            impact = pool[poolIndex];
                            int lastIdx = _activeImpacts.Count - 1;
                            if (oldestIdx < lastIdx) _activeImpacts[oldestIdx] = _activeImpacts[lastIdx];
                            _activeImpacts.RemoveAt(lastIdx);
                        }
                        else return;
                    }
                    else return;
                }
                else
                {
                    impact = Instantiate(impactPrefab, transform);
                    pool.Add(impact);
                    poolIndex = pool.Count - 1;
                }
            }
            
            // Position and activate
            impact.transform.position = position;
            impact.transform.rotation = Quaternion.LookRotation(normal != Vector3.zero ? normal : Vector3.up);
            impact.SetActive(true);
            
            // Get or cache ParticleSystem component
            if (!cachedPS.TryGetValue(poolIndex, out var particleSystem))
            {
                particleSystem = impact.GetComponent<ParticleSystem>();
                cachedPS[poolIndex] = particleSystem;
            }
            
            float duration = 3f;
            if (particleSystem != null)
            {
                particleSystem.Clear();
                particleSystem.Play();
                duration = particleSystem.main.duration + particleSystem.main.startLifetime.constantMax;
            }
            
            // Track for later release
            _activeImpacts.Add((impactPrefab, poolIndex, Time.time + duration + 0.1f));
        }
        
        /// <summary>
        /// Spawns an AOE VFX (explosion) at the specified position using object pooling.
        /// OPTIMIZED: Caches ParticleSystem components and caps pool size.
        /// </summary>
        private void SpawnAOEVFX(GameObject aoePrefab, Vector3 position, float aoeRadius)
        {
            if (aoePrefab == null) return;
            
            // Get or create pool for this prefab
            if (!_aoeVFXPools.TryGetValue(aoePrefab, out var pool))
            {
                pool = new List<GameObject>(16);
                _aoeVFXPools[aoePrefab] = pool;
                _aoeVFXFreeIndices[aoePrefab] = new Queue<int>();
                _cachedAOEParticleSystems[aoePrefab] = new Dictionary<int, ParticleSystem>();
            }
            
            var freeQueue = _aoeVFXFreeIndices[aoePrefab];
            var cachedPS = _cachedAOEParticleSystems[aoePrefab];
            
            GameObject aoeVFX;
            int poolIndex;
            
            if (freeQueue.Count > 0)
            {
                poolIndex = freeQueue.Dequeue();
                aoeVFX = pool[poolIndex];
            }
            else
            {
                // Create new instance (cap pool size)
                if (pool.Count >= 64)
                {
                    // Pool exhausted - find oldest and reclaim
                    if (_activeAOEs.Count > 0)
                    {
                        float oldestTime = float.MaxValue;
                        int oldestIdx = -1;
                        for (int i = 0; i < _activeAOEs.Count; i++)
                        {
                            if (_activeAOEs[i].prefab == aoePrefab && _activeAOEs[i].releaseTime < oldestTime)
                            {
                                oldestTime = _activeAOEs[i].releaseTime;
                                oldestIdx = i;
                            }
                        }
                        if (oldestIdx >= 0)
                        {
                            poolIndex = _activeAOEs[oldestIdx].poolIndex;
                            aoeVFX = pool[poolIndex];
                            int lastIdx = _activeAOEs.Count - 1;
                            if (oldestIdx < lastIdx) _activeAOEs[oldestIdx] = _activeAOEs[lastIdx];
                            _activeAOEs.RemoveAt(lastIdx);
                        }
                        else return;
                    }
                    else return;
                }
                else
                {
                    aoeVFX = Instantiate(aoePrefab, transform);
                    pool.Add(aoeVFX);
                    poolIndex = pool.Count - 1;
                }
            }
            
            // Position and activate
            aoeVFX.transform.position = position;
            aoeVFX.transform.rotation = Quaternion.identity;
            aoeVFX.SetActive(true);
            
            // Get or cache ParticleSystem component
            if (!cachedPS.TryGetValue(poolIndex, out var particleSystem))
            {
                particleSystem = aoeVFX.GetComponent<ParticleSystem>();
                cachedPS[poolIndex] = particleSystem;
            }
            
            float duration = 5f;
            if (particleSystem != null)
            {
                particleSystem.Clear();
                particleSystem.Play();
                duration = particleSystem.main.duration + particleSystem.main.startLifetime.constantMax;
            }
            
            // Track for later release
            _activeAOEs.Add((aoePrefab, poolIndex, Time.time + duration + 0.1f));
            
            if (enableDebugLogging)
            {
                Debug.Log($"[ProjectileSystem] Spawned AOE VFX at {position} with radius {aoeRadius}");
            }
        }
        
        /// <summary>
        /// Reclaims finished impact and AOE VFX back to their pools.
        /// </summary>
        private void ReclaimFinishedVFX(float currentTime)
        {
            // Reclaim finished impact VFX
            for (int i = _activeImpacts.Count - 1; i >= 0; i--)
            {
                var (prefab, poolIndex, releaseTime) = _activeImpacts[i];
                
                if (currentTime >= releaseTime)
                {
                    // Return to pool
                    if (_impactVFXPools.TryGetValue(prefab, out var pool) && poolIndex < pool.Count)
                    {
                        pool[poolIndex].SetActive(false);
                        _impactVFXFreeIndices[prefab].Enqueue(poolIndex);
                    }
                    
                    // Remove from active list (swap with last for O(1) removal)
                    int lastIndex = _activeImpacts.Count - 1;
                    if (i < lastIndex)
                    {
                        _activeImpacts[i] = _activeImpacts[lastIndex];
                    }
                    _activeImpacts.RemoveAt(lastIndex);
                }
            }
            
            // Reclaim finished AOE VFX
            for (int i = _activeAOEs.Count - 1; i >= 0; i--)
            {
                var (prefab, poolIndex, releaseTime) = _activeAOEs[i];
                
                if (currentTime >= releaseTime)
                {
                    // Return to pool
                    if (_aoeVFXPools.TryGetValue(prefab, out var pool) && poolIndex < pool.Count)
                    {
                        pool[poolIndex].SetActive(false);
                        _aoeVFXFreeIndices[prefab].Enqueue(poolIndex);
                    }
                    
                    // Remove from active list (swap with last for O(1) removal)
                    int lastIndex = _activeAOEs.Count - 1;
                    if (i < lastIndex)
                    {
                        _activeAOEs[i] = _activeAOEs[lastIndex];
                    }
                    _activeAOEs.RemoveAt(lastIndex);
                }
            }
        }
        
        /// <summary>
        /// Plays a sound at the specified position using CrowdAudioManager for efficiency.
        /// Supports pitch variation and spatial blend with per-clip concurrency limiting.
        /// </summary>
        private void PlaySoundAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariation = 0f, float spatialBlend = 1f)
        {
            if (clip == null) return;
            
            // Use CrowdAudioManager for intelligent per-clip pooling
            var audioManager = CrowdAudioManager.Instance;
            if (audioManager != null)
            {
                audioManager.PlaySound(clip, position, volume, pitchVariation, spatialBlend);
                return;
            }
            
            // Fallback to legacy pool if CrowdAudioManager not available
            int audioIndex = AcquireAudioSource();
            if (audioIndex < 0)
            {
                // Pool exhausted - skip this sound (better than GC allocation)
                if (enableDebugLogging)
                {
                    Debug.LogWarning("[ProjectileSystem] Audio pool exhausted, skipping sound");
                }
                return;
            }
            
            AudioSource audioSource = _audioSourcePool[audioIndex];
            audioSource.transform.position = position;
            audioSource.clip = clip;
            audioSource.volume = volume;
            audioSource.spatialBlend = spatialBlend;
            
            // Apply pitch variation
            float pitch = 1f;
            if (pitchVariation > 0f)
            {
                // Use audio source index to add variation when multiple sounds play same frame
                // This ensures consecutive sounds from the same frame have different pitches
                float indexOffset = (audioIndex * 0.1f) % 1f;
                float baseRandom = UnityEngine.Random.Range(-1f, 1f);
                // XOR-style mixing to break patterns
                float mixed = baseRandom + indexOffset;
                if (mixed > 1f) mixed -= 2f;
                if (mixed < -1f) mixed += 2f;
                pitch = 1f + mixed * pitchVariation;
            }
            audioSource.pitch = pitch;
            
            audioSource.Play();
            
            // Schedule when this audio source can be reused
            float clipDuration = clip.length / Mathf.Max(0.1f, pitch);
            _audioSourceReleaseTime[audioIndex] = Time.time + clipDuration + 0.05f;
        }
        
        /// <summary>
        /// Acquires an audio source from the pool. Returns -1 if pool is exhausted.
        /// Will also reclaim audio sources that have finished playing.
        /// </summary>
        private int AcquireAudioSource()
        {
            float currentTime = Time.time;
            
            // First, try to get from the free queue
            if (_freeAudioSources.Count > 0)
            {
                return _freeAudioSources.Dequeue();
            }
            
            // No free sources - try to reclaim finished ones
            for (int i = 0; i < _audioSourcePool.Count; i++)
            {
                if (_audioSourceReleaseTime[i] <= currentTime && !_audioSourcePool[i].isPlaying)
                {
                    return i;
                }
            }
            
            // Pool is truly exhausted - expand if we haven't hit a limit
            if (_audioSourcePool.Count < 256) // Hard cap at 256 audio sources
            {
                int newIndex = _audioSourcePool.Count;
                
                GameObject audioObj = new GameObject($"AudioSource_{newIndex}");
                audioObj.transform.SetParent(_audioSourcePool[0].transform.parent);
                
                AudioSource source = audioObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = 50f;
                
                _audioSourcePool.Add(source);
                
                // Expand the release time array
                float[] newReleaseTime = new float[_audioSourcePool.Count];
                System.Array.Copy(_audioSourceReleaseTime, newReleaseTime, _audioSourceReleaseTime.Length);
                _audioSourceReleaseTime = newReleaseTime;
                _audioSourceReleaseTime[newIndex] = 0f;
                
                return newIndex;
            }
            
            // Truly exhausted and at capacity
            return -1;
        }
        
        #endregion
        
        #region Visual Pool Management
        
        private void UpdateVisuals()
        {
            if (_mainCameraTransform == null)
            {
                _mainCameraTransform = Camera.main?.transform;
                if (_mainCameraTransform == null) return;
            }
            
            float cullDistSqr = visualCullDistance * visualCullDistance;
            
            for (int i = 0; i < maxProjectiles; i++)
            {
                ProjectileData data = _projectiles[i];
                
                if (!data.isActive) continue;
                
                // Handle VFX Graph projectile visuals
                if (data.usesVFXGraph && data.vfxGraphPoolIndex >= 0)
                {
                    // Update VFX Graph projectile position/direction
                    if (_activeVFXGraphProjectiles.TryGetValue(i, out var vfxData))
                    {
                        var (asset, poolIndex) = vfxData;
                        if (_vfxGraphProjectilePools.TryGetValue(asset, out var pool) && poolIndex < pool.Count)
                        {
                            var vfx = pool[poolIndex];
                            float speed = math.length(data.velocity);
                            Vector3 direction = speed > 0.01f ? (Vector3)math.normalize(data.velocity) : Vector3.forward;
                            
                            // Check distance culling
                            float distSqr = math.distancesq(data.position, (float3)_mainCameraTransform.position);
                            if (distSqr > cullDistSqr)
                            {
                                vfx.gameObject.SetActive(false);
                            }
                            else
                            {
                                vfx.gameObject.SetActive(true);
                                UpdateVFXGraphProjectileVisual(vfx, data.position, direction, speed, data.visualScale, false);
                            }
                        }
                    }
                    continue;
                }
                
                // Handle GameObject/ParticleSystem visuals
                if (data.visualPoolIndex < 0) continue;
                
                // Cull distant projectiles
                float distSqrPS = math.distancesq(data.position, (float3)_mainCameraTransform.position);
                
                // Get visual from appropriate pool
                GameObject visual = null;
                if (data.customPrefabId > 0)
                {
                    // Custom prefab pool - use reverse lookup
                    if (_prefabIdLookup.TryGetValue(data.customPrefabId, out var prefab) &&
                        _customPrefabPools.TryGetValue(prefab, out var customPool))
                    {
                        if (data.visualPoolIndex < customPool.Count)
                        {
                            visual = customPool[data.visualPoolIndex];
                        }
                    }
                }
                else
                {
                    // Default pool
                    List<GameObject> pool = UsesLinearVisualPool(data.type) ? _linearVisualPool : _parabolicVisualPool;
                    if (data.visualPoolIndex < pool.Count)
                    {
                        visual = pool[data.visualPoolIndex];
                    }
                }
                
                if (visual == null) continue;
                
                if (distSqrPS > cullDistSqr)
                {
                    visual.SetActive(false);
                }
                else
                {
                    visual.SetActive(true);
                    visual.transform.position = data.position;
                    
                    // Rotate to face velocity direction
                    if (math.lengthsq(data.velocity) > 0.01f)
                    {
                        visual.transform.rotation = Quaternion.LookRotation(math.normalize(data.velocity));
                    }
                }
            }
        }
        
        private void DeactivateProjectile(int index)
        {
            ProjectileData data = _projectiles[index];
            _trajectoryPierceStates.Remove(index);
            _timedFlightActionStates.Remove(index);

            if (data.networkProjectileId != 0)
            {
                SkillVFXManager.StopLoopingSFXByProjectile(data.networkProjectileId);
            }
            
            if (data.networkProjectileId != 0)
            {
                bool removed = _skillProjectileCallbacks.Remove(data.networkProjectileId);
                if (removed && enableDebugLogging)
                {
                    Debug.Log($"[ProjectileSystem] Removed stale skill callback for projectile {data.networkProjectileId}");
                }
            }
            
            // Release trail VFX
            ReleaseTrailVFX(index);
            
            // Release VFX Graph projectile visual
            if (data.usesVFXGraph && data.vfxGraphPoolIndex >= 0)
            {
                ReleaseVFXGraphProjectileVisual(index, data.hasHit && data.fadeVisualOnHit);
            }
            else if (data.visualPoolIndex >= 0)
            {
                bool allowParticleFade = data.hasHit && data.fadeVisualOnHit;

                if (data.customPrefabId > 0)
                {
                    // Release to custom prefab pool
                    ReleaseCustomVisual(data.customPrefabId, data.visualPoolIndex, allowParticleFade);
                }
                else
                {
                    // Release to default pool
                    ReleaseVisual(data.visualPoolIndex, data.type, allowParticleFade);
                }
            }
            
            data.isActive = false;
            data.hasHit = false;
            data.fadeVisualOnHit = true;
            data.visualPoolIndex = -1;
            data.customPrefabId = 0;
            data.effectsConfigId = 0;
            data.trailVisualIndex = -1;
            data.usesVFXGraph = false;
            data.vfxGraphPoolIndex = -1;
            data.networkProjectileId = 0;
            data.simulationGravity = 0f;
            data.homingTurnRate = 0f;
            data.homingAcceleration = 0f;
            _projectiles[index] = data;
            
            _freeIndices.Enqueue(index);
            _activeProjectileCount--;
        }
        
        #endregion
        
        #region Debug
        
        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || !Application.isPlaying) return;
            
            for (int i = 0; i < maxProjectiles; i++)
            {
                if (!_projectiles.IsCreated) return;
                
                ProjectileData data = _projectiles[i];
                if (!data.isActive) continue;
                
                Gizmos.color = data.type == ProjectileType.Linear ? Color.yellow : Color.red;
                Gizmos.DrawWireSphere(data.position, 0.2f);
                
                // Draw velocity
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(data.position, data.position + data.velocity * 0.2f);
                
                // Draw path to target
                Gizmos.color = Color.gray;
                Gizmos.DrawLine(data.position, data.targetPosition);
            }
        }
        
        #endregion
        
        #region Statistics
        
        public int ActiveProjectileCount => _activeProjectileCount;
        public int ProjectilesFiredThisFrame => _projectilesFiredThisFrame;
        public int ProjectilesHitThisFrame => _projectilesHitThisFrame;
        
        #endregion
    }
}
