using UnityEngine;
using UnityEngine.AI;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
using GPUInstancerPro.PrefabModule;
using Arawn.EnemyMasses.Runtime.Civil;
using Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using Arawn.EnemyMasses.Runtime.Skills;
using Arawn.EnemyMasses.Runtime.Damageables;

namespace Arawn.EnemyMasses.Runtime
{
    public enum AgentCommandIntent
    {
        None,
        Move,
        AttackExplicit
    }

    /// <summary>
    /// Represents a single agent in the Enemy Masses crowd system.
    /// Contains all data needed to manage an individual agent's behavior, animation, and combat state.
    /// </summary>
    public class CrowdAgent
    {
        // Core Components
        public GPUICrowdInstance crowdInstance;
        
        /// <summary>
        /// The navigation agent abstraction. Supports both Unity NavMesh and A* Pathfinding.
        /// Use this for all navigation operations to ensure compatibility with both systems.
        /// </summary>
        public INavigationAgent navigationAgent;
        
        private INavigationAgent _cachedNavigationAgent;
        private NavMeshAgent _cachedNavMeshAgent;

        /// <summary>
        /// Legacy NavMeshAgent reference for backward compatibility.
        /// Returns the underlying NavMeshAgent if using Unity NavMesh, null otherwise.
        /// Prefer using navigationAgent for new code.
        /// </summary>
        public NavMeshAgent navMeshAgent
        {
            get
            {
                if (_cachedNavigationAgent != navigationAgent)
                {
                    _cachedNavigationAgent = navigationAgent;
                    _cachedNavMeshAgent = _cachedNavigationAgent?.GetUnderlyingAgent<NavMeshAgent>();
                }

                return _cachedNavMeshAgent;
            }
        }
        
        public Animator mecanimAnimator;
        public GPUIPrefab prefabComponent;
        
        /// <summary>Convenience alias for navMeshAgent (used by civil navigation)</summary>
        public NavMeshAgent navAgent => navMeshAgent;
        
        /// <summary>Transform shortcut for common position access</summary>
        public Transform transform => crowdInstance?.transform;
        
        /// <summary>
        /// Safe position access that works with both Unity NavMesh and A* Pathfinding.
        /// Uses navigationAgent.Position if available, falls back to transform.position.
        /// </summary>
        public Vector3 WorldPosition => navigationAgent?.Position ?? transform?.position ?? Vector3.zero;
        
        /// <summary>
        /// Safe velocity access that works with both Unity NavMesh and A* Pathfinding.
        /// Uses navigationAgent.Velocity if available, otherwise returns zero.
        /// </summary>
        public Vector3 CurrentVelocity => navigationAgent?.Velocity ?? Vector3.zero;
        
        /// <summary>
        /// Check if navigation is usable for control in the current backend.
        /// For backends that require surface validation (for example Unity NavMesh),
        /// this reflects the agent's surface status. For backends that do not,
        /// this returns true when a navigation agent exists.
        /// </summary>
        public bool IsOnNavMesh
        {
            get
            {
                var navAgent = navigationAgent;
                if (navAgent == null)
                    return false;

                if (!navAgent.Capabilities.RequiresSurfaceValidationForControl)
                    return true;

                return navAgent.IsOnNavMesh;
            }
        }
        
        /// <summary>
        /// Check if navigation is enabled and the agent can move.
        /// Works with both Unity NavMesh and A* Pathfinding.
        /// </summary>
        public bool IsNavigationEnabled => navigationAgent?.Enabled ?? false;
        
        // Agent Identification
        public int index;
        
        // Faction index - backing field with property for security (team switching prevention)
        private int _enemyCrowdIndex = -1;
        /// <summary>
        /// Index in enemyCrowds list, -1 for legacy spawns.
        /// In multiplayer, faction changes must be server-authorized.
        /// </summary>
        public int enemyCrowdIndex 
        { 
            get => _enemyCrowdIndex; 
            set
            {
                // Prevent team switching hacks - only authority can change faction
                if (NetworkAuthorityProvider.IsNetworkMode && !isNetworkAuthoritative && networkId != 0)
                {
                    if (_enemyCrowdIndex >= 0 && value != _enemyCrowdIndex) // Attempting to change faction
                    {
                        #if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning($"[Security] Client attempted faction change on agent {index} from {_enemyCrowdIndex} to {value}");
                        #endif
                        return;
                    }
                }
                _enemyCrowdIndex = value;
            }
        }
        
        /// <summary>
        /// Server-authoritative faction update.
        /// Only call this from network sync or spawn authority.
        /// </summary>
        public void SetFactionAuthoritative(int factionIndex)
        {
            _enemyCrowdIndex = factionIndex;
        }
        
        public EnemyCrowd enemyCrowd; // Direct reference to this agent's EnemyCrowd (Phase 2: single source of truth)
        public int spawnGroupId = -1; // Spawn group ID for batch control, -1 for unassigned
        public FormationType formationType = FormationType.Circle; // Formation type for this agent
        
        // ═══════════════════════════════════════════════════════════════════
        // Summon Fields
        // Used when this agent was summoned by another agent via skills.
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// True if this agent was summoned by another agent (temporary unit).
        /// Summoned agents may have special behavior and timed despawn.
        /// </summary>
        public bool isSummoned;
        
        /// <summary>
        /// Index of the agent that summoned this unit. -1 if not summoned.
        /// Used for summon AI (inherit target, follow summoner, etc.)
        /// </summary>
        public int summonerIndex = -1;
        
        /// <summary>
        /// Network ID of the summoner (for reconnection/sync).
        /// </summary>
        public uint summonerNetworkId;
        
        /// <summary>
        /// Time.time when this summoned agent should despawn.
        /// 0 = permanent (no timed despawn).
        /// </summary>
        public float summonExpireTime;
        
        /// <summary>
        /// Whether summoned agent should inherit and follow summoner's target.
        /// </summary>
        public bool summonInheritTarget = true;
        
        // ═══════════════════════════════════════════════════════════════════
        // Multiplayer / Networking Fields
        // These fields are only used when network authorities are assigned.
        // In single-player mode, they remain at default values.
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Unique network identifier for this agent. 
        /// 0 = not networked (single-player or server hasn't assigned ID yet).
        /// Assigned by INetworkSpawnAuthority when spawning in multiplayer.
        /// </summary>
        public uint networkId;
        
        // Owner player - backing field with property for security
        private int _ownerPlayerId = -1;
        /// <summary>
        /// Player ID that owns/controls this agent.
        /// -1 = AI-controlled or server-owned (no specific player).
        /// Used for authority checks and fog of war visibility.
        /// In multiplayer, only server can change ownership.
        /// </summary>
        public int ownerPlayerId 
        { 
            get => _ownerPlayerId; 
            set
            {
                // Prevent ownership hijacking - only server can reassign
                if (NetworkAuthorityProvider.IsNetworkMode && !isNetworkAuthoritative && networkId != 0)
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"[Security] Client attempted ownership change on agent {index} from {_ownerPlayerId} to {value}");
                    #endif
                    return;
                }
                _ownerPlayerId = value;
            }
        }
        
        /// <summary>
        /// Server-authoritative owner update.
        /// Only call this from network sync code or spawn authority.
        /// </summary>
        public void SetOwnerAuthoritative(int playerId)
        {
            _ownerPlayerId = playerId;
        }
        
        /// <summary>
        /// Whether this client has authority over this agent's state.
        /// True on server for all agents, true on client for locally-predicted agents.
        /// When false, client receives state from server via network sync.
        /// </summary>
        public bool isNetworkAuthoritative;
        
        /// <summary>
        /// Ring buffer for interpolating network transform updates on clients.
        /// Only allocated when networking is active and this is a remote agent.
        /// </summary>
        public NetworkTransformBuffer transformBuffer;
        
        /// <summary>
        /// Last network tick this agent was synchronized.
        /// Used for delta compression and stale data detection.
        /// </summary>
        public uint lastNetworkTick;
        
        /// <summary>
        /// Dirty flags for network state synchronization.
        /// Indicates which properties changed since last sync.
        /// Uses NetworkAgentState.DELTA_* constants.
        /// </summary>
        public ushort networkDirtyFlags;
        
        /// <summary>Convenience alias for enemyCrowdIndex (faction/crowd index)</summary>
        public int factionIndex => enemyCrowdIndex;
        
        // Animation State
        public CrowdAnimationState currentState;
        public float cachedBlendWeight;
        public int lastIdleAnimationHash = -1;
        public bool idleAnimationWasGpu = false;
        public bool usingRootMotion;
        public bool isAirborne;
        public bool landingRequested;
        public float landingClipEndTime;
        
        /// <summary>
        /// True while agent is climbing via NavMeshLink traversal.
        /// When true, locomotion animation updates are skipped to allow climb animation to play.
        /// </summary>
        public bool isClimbing;

        /// <summary>
        /// True while the agent is in flight mode.
        /// When true, locomotion animation updates are skipped to allow flight animation to play.
        /// Flight mode is activated via <see cref="EnemyMassesCrowdController.SetAgentFlying"/>
        /// or the <see cref="Arawn.EnemyMasses.Runtime.Skills.FlyEffect"/> skill action.
        /// </summary>
        public bool isFlying;

        /// <summary>
        /// Time when the flight take-off clip ends, used to schedule the flight loop clip.
        /// Only relevant for ComputeAnimator workflows with a take-off clip.
        /// </summary>
        public float flightTakeOffEndTime;

        /// <summary>
        /// Time when the flight landing clip ends, used to finalize landing.
        /// Only relevant for ComputeAnimator workflows with a landing clip.
        /// </summary>
        public float flightLandEndTime;

        /// <summary>
        /// Target flight altitude above ground level (world units).
        /// Set by <see cref="EnemyMassesCrowdController.SetAgentFlying"/>, cleared by <see cref="EnemyMassesCrowdController.SetAgentLanded"/>.
        /// The visual height smoothly lerps toward this value each frame.
        /// </summary>
        public float flightTargetHeight;

        /// <summary>
        /// Current interpolated flight height above ground level (world units).
        /// Smoothly moves toward <see cref="flightTargetHeight"/> in the navigation sync loop.
        /// When this reaches zero after landing, the flight height offset is fully resolved.
        /// </summary>
        public float flightCurrentHeight;

        // ── Debug flags (one-shot logs) ──
        public bool _skillFallbackDebugLogged;
        public bool _skillEvalDebugLogged;

        // ── Crash-from-sky state (dead flying agents) ──

        /// <summary>
        /// True while the dead agent is crash-descending from the sky after being killed in flight.
        /// Set by the combat system on death when the agent has significant altitude; cleared when
        /// <see cref="flightCurrentHeight"/> reaches zero.
        /// </summary>
        public bool isCrashing;

        /// <summary>
        /// Current downward velocity during crash descent (m/s, positive = downward).
        /// Accelerated by gravity each frame.
        /// </summary>
        public float crashVerticalVelocity;

        /// <summary>
        /// Remaining forward momentum during crash descent (m/s).
        /// Captured from the agent's velocity at the moment of death and decays over time.
        /// </summary>
        public float crashForwardSpeed;

        /// <summary>
        /// World-space forward direction captured at crash start.
        /// Used for forward momentum displacement and as the tumble/spiral axis.
        /// </summary>
        public Vector3 crashForwardDirection;

        /// <summary>
        /// Accumulated nose-down tumble angle during crash (degrees).
        /// Capped at ~80° to prevent the vehicle from going fully inverted.
        /// </summary>
        public float crashTumbleAngle;

        /// <summary>
        /// Ground-level Y position captured at crash start.
        /// The crash ends when the agent's visual Y position reaches this value.
        /// </summary>
        public float crashGroundY;

        // Navigation State
        public float smoothedSpeed;
        public Vector3 lastDestination;
        public float lastProgressTime;
        public float lastKnownDistance;
        public int remainingRetries;
        public float nextRetryTime;
        public bool hasArrived;
        public int destinationGeneration;
        public int destinationRetries; // Number of failed destination attempts for current generation
        public float pathPendingStartTime; // Time when pathPending became true
        public float lastNavSyncTime;
        public float lastAttackTargetUpdateTime; // Throttle updates for moving attack targets (A* pathfinding)
        public float lastCombatDestinationUpdateTime = -999f; // Throttle combat SetDestination spam for A* agents
        public Vector3 lastCombatDestination; // Last combat destination used for throttling

        // Visual navmesh offset (non-physics knockback/lift applied to NavMeshAgent position)
        public bool hasNavmeshOffsetImpulse;
        public Vector3 navmeshOffset;
        public Vector3 navmeshOffsetVelocity;
        public Vector3 navmeshOffsetGroundPosition; // Stored ground position when impulse starts - prevents position accumulation
        public float navmeshOffsetGravity;
        public float navmeshOffsetDrag;
        public float navmeshOffsetDuration;
        public float navmeshOffsetEndTime;
        public float lastNavmeshImpulsePush; // Signed push scalar used for impulse animation classification
        public float lastNavmeshImpulseLift; // Signed lift scalar used for impulse animation classification
        public bool navmeshImpulseAnimationPlayed; // Prevents replaying the same impulse animation every frame
        public bool impulseAnimationActive; // True while an impulse reaction clip is playing
        public float impulseAnimationEndTime; // Time when the current impulse reaction clip should end
        public float impulseAnimationBlendOutTime; // Time to start blending back to locomotion/idle before the impulse ends
        public bool impulseBlendOutTriggered; // Whether we've already started blending out of the impulse clip
        public bool liftImpulseActive; // True while a lift-type impulse is in effect (used for get-up flow)
        public bool liftGetUpPending; // True when agent has landed after lift and should play get-up
        public float impulseAttackLockEndTime; // Until this time, skip attack actions during/after impulses
        public bool disableNavMeshAfterImpulse;

        // A* knockback impulse (uses IAstarAI.Move instead of navmesh offset)
        public bool hasAstarKnockbackImpulse;
        public Vector3 astarKnockbackVelocity;
        public float astarKnockbackEndTime;
        public float astarKnockbackDrag;
        public float astarKnockbackVerticalVelocity;
        public float astarKnockbackGravity;
        public float astarVisualOffsetY;
        public bool moveBasedKnockbackForcedStop;
        public bool moveBasedKnockbackWasStoppedBeforeImpulse;
        public AgentNavigationState navigationState = AgentNavigationState.Formation;
        public float navigationStateChangedTime;
        
        // Performance & Visibility
        public bool isVisible;
        public bool wasVisible; // Previous frame visibility - used to detect visibility transitions
        public bool needsAnimationRefresh; // True when agent just became visible and needs animation forced
        public bool isFogHidden;
        public HybridWorkflowType currentWorkflow;
        public bool workflowMismatchWarned; // Prevents repeated log spam for workflow desync
        
        // Footstep Audio
        public float nextFootstepTime; // Time when next footstep sound can play (interval mode)
        
        // Ambient Audio
        public float nextAmbientSoundTime; // Time when next ambient sound can play
        
        // Civil NPC System
        /// <summary>Whether this agent is in civil mode (waypoint navigation, activities)</summary>
        public bool isCivilMode;
        /// <summary>Index of the civil agent state in CivilNavigationController (-1 if not civil)</summary>
        public int civilStateIndex = -1;
        
        // ═══════════════════════════════════════════════════════════════════
        // Combat State - Critical fields use properties for security
        // In multiplayer, these can only be modified by authoritative sources
        // ═══════════════════════════════════════════════════════════════════
        
        public CrowdCombatState combatState = CrowdCombatState.Idle;
        public float combatStateEndTime;
        
        // Health - backing field with property accessor for network security
        private float _health;
        /// <summary>
        /// Current health. In multiplayer, only server-authoritative sources should modify.
        /// Use SetHealthAuthoritative() for direct server updates that bypass validation.
        /// </summary>
        public float health 
        { 
            get => _health; 
            set
            {
                // In network mode, non-authoritative clients should not directly modify health
                // This is a soft guard - the actual enforcement is server-side
                if (NetworkAuthorityProvider.IsNetworkMode && !isNetworkAuthoritative && networkId != 0)
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"[Security] Client attempted direct health modification on agent {index}. Use network authority.");
                    #endif
                    return;
                }
                _health = value;
            }
        }
        
        /// <summary>
        /// Server-authoritative health update that bypasses client-side guards.
        /// Only call this from network sync code receiving server state.
        /// </summary>
        public void SetHealthAuthoritative(float serverHealth)
        {
            _health = serverHealth;
        }
        
        /// <summary>Maximum health for this agent. Set at spawn from faction config.</summary>
        public float maxHealth = 100f;
        
        /// <summary>
        /// Agent level (1-99). Used for skill scaling calculations.
        /// Defaults to 1. Can be set from external level systems via ISkillStatProvider.
        /// </summary>
        public int level = 1;
        
        /// <summary>
        /// Multiplier for outgoing damage (attack power). 
        /// Stacks with skill damage scaling. Default 1.0 = normal damage.
        /// </summary>
        public float outgoingDamageMultiplier = 1f;
        
        public float incomingDamageMultiplier = 1f; // Per-agent incoming damage multiplier. Stacks with global and faction multipliers.
        
        #region Resistance Modifiers (Runtime Buffs/Debuffs)
        
        /// <summary>
        /// Bonus armor from buffs/debuffs. Added to faction base armor.
        /// Positive = more armor, negative = armor reduction (ArmorBreak).
        /// </summary>
        public float bonusArmor = 0f;
        
        /// <summary>
        /// Bonus magic resistance from buffs/debuffs. Added to faction base magicResist.
        /// </summary>
        public float bonusMagicResist = 0f;
        
        /// <summary>
        /// Bonus fire resistance from buffs/debuffs.
        /// </summary>
        public float bonusFireResistance = 0f;
        
        /// <summary>
        /// Bonus ice resistance from buffs/debuffs.
        /// </summary>
        public float bonusIceResistance = 0f;
        
        /// <summary>
        /// Bonus lightning resistance from buffs/debuffs.
        /// </summary>
        public float bonusLightningResistance = 0f;
        
        /// <summary>
        /// Bonus poison resistance from buffs/debuffs.
        /// </summary>
        public float bonusPoisonResistance = 0f;
        
        /// <summary>
        /// Gets the effective armor (faction base + bonus from buffs).
        /// </summary>
        public float EffectiveArmor => (enemyCrowd?.armor ?? 0f) + bonusArmor;
        
        /// <summary>
        /// Gets the effective magic resistance (faction base + bonus from buffs).
        /// </summary>
        public float EffectiveMagicResist => (enemyCrowd?.magicResist ?? 0f) + bonusMagicResist;
        
        /// <summary>
        /// Gets the effective resistance for a specific damage type.
        /// </summary>
        public float GetEffectiveResistance(Skills.DamageType damageType)
        {
            return damageType switch
            {
                Skills.DamageType.Physical => EffectiveArmor,
                Skills.DamageType.Magical => EffectiveMagicResist,
                Skills.DamageType.Fire => GetElementalResistance(Skills.DamageType.Fire),
                Skills.DamageType.Ice => GetElementalResistance(Skills.DamageType.Ice),
                Skills.DamageType.Lightning => GetElementalResistance(Skills.DamageType.Lightning),
                Skills.DamageType.Poison => (enemyCrowd?.poisonResistance ?? 0f) + bonusPoisonResistance,
                Skills.DamageType.True => 0f, // True damage ignores all resistances
                _ => 0f
            };
        }
        
        /// <summary>
        /// Gets elemental resistance, using individual override if enabled, otherwise magic resist.
        /// </summary>
        private float GetElementalResistance(Skills.DamageType element)
        {
            if (enemyCrowd == null) return 0f;
            
            if (enemyCrowd.useElementalResistances)
            {
                return element switch
                {
                    Skills.DamageType.Fire => enemyCrowd.fireResistance + bonusFireResistance,
                    Skills.DamageType.Ice => enemyCrowd.iceResistance + bonusIceResistance,
                    Skills.DamageType.Lightning => enemyCrowd.lightningResistance + bonusLightningResistance,
                    _ => EffectiveMagicResist
                };
            }
            
            // Use magic resist as base for all magical damage types
            return EffectiveMagicResist;
        }
        
        /// <summary>
        /// Calculates the damage multiplier based on resistance.
        /// Formula: 100 / (100 + resistance)
        /// Returns: 1.0 at 0 resist, 0.5 at 100 resist, 2.0 at -100 resist
        /// </summary>
        public static float CalculateResistanceMultiplier(float resistance)
        {
            // Prevent division by zero and cap effectiveness
            float effectiveResist = Mathf.Max(resistance, -99f);
            return 100f / (100f + effectiveResist);
        }
        
        /// <summary>
        /// Applies resistance to damage and returns the mitigated amount.
        /// </summary>
        public float ApplyResistance(float rawDamage, Skills.DamageType damageType)
        {
            if (damageType == Skills.DamageType.True)
                return rawDamage; // True damage bypasses all resistances
            
            float resistance = GetEffectiveResistance(damageType);
            float multiplier = CalculateResistanceMultiplier(resistance);
            return rawDamage * multiplier;
        }
        
        /// <summary>
        /// Resets all resistance modifiers to zero (call when buffs expire or agent respawns).
        /// </summary>
        public void ResetResistanceModifiers()
        {
            bonusArmor = 0f;
            bonusMagicResist = 0f;
            bonusFireResistance = 0f;
            bonusIceResistance = 0f;
            bonusLightningResistance = 0f;
            bonusPoisonResistance = 0f;
        }
        
        #endregion
        
        /// <summary>
        /// Optional custom data dictionary for storing external system values (e.g., GC2 Stats).
        /// Only allocated when needed. Used by DefaultSkillStatProvider for custom stat lookups.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> customData;
        
        /// <summary>
        /// Gets the global skill scaling config from this agent's EnemyCrowd.
        /// Returns null if no EnemyCrowd is assigned or global scaling is not configured.
        /// </summary>
        public Skills.Scaling.SkillScalingConfig GetGlobalScalingConfig()
        {
            return enemyCrowd?.globalSkillScaling;
        }
        
        // Attack timing - backing field with property accessor for cheat prevention
        private float _nextAttackTime;
        /// <summary>
        /// Earliest time this agent can attack again. Guards against attack speed hacks.
        /// </summary>
        public float nextAttackTime 
        { 
            get => _nextAttackTime; 
            set
            {
                // Prevent setting attack time in the past (instant attack hack)
                if (NetworkAuthorityProvider.IsNetworkMode && !isNetworkAuthoritative && networkId != 0)
                {
                    // On non-authoritative clients, only allow setting future times
                    if (value < Time.time - 0.1f) // Small tolerance for network jitter
                    {
                        #if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning($"[Security] Client attempted to set past attack time on agent {index}");
                        #endif
                        return;
                    }
                }
                _nextAttackTime = value;
            }
        }
        
        public float lastSecondaryAttackTime; // Tracks cooldown for secondary ranged attack (grenades, etc.)
        
        // Death state - backing field with property accessor
        private bool _isDead;
        /// <summary>
        /// Whether this agent is dead. In multiplayer, resurrection must be server-authorized.
        /// </summary>
        public bool isDead 
        { 
            get => _isDead; 
            set
            {
                // Prevent resurrection hacks - once dead, only server can revive
                if (NetworkAuthorityProvider.IsNetworkMode && !isNetworkAuthoritative && networkId != 0)
                {
                    if (_isDead && !value) // Attempting to resurrect
                    {
                        #if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning($"[Security] Client attempted resurrection on agent {index}");
                        #endif
                        return;
                    }
                }
                _isDead = value;
            }
        }
        
        /// <summary>
        /// Server-authoritative death state update (allows resurrection).
        /// Only call this from network sync code.
        /// </summary>
        public void SetDeadAuthoritative(bool dead)
        {
            _isDead = dead;
        }
        
        public bool isBlocking;
        public float lastDamageTime;
        public int lastAttackClipIndex = -1;
        public float deathTime;
        
        // Projectile spawn point cache (for ranged attacks)
        public Transform projectileSpawnPoint;           // Cached spawn point for primary attack
        public Transform secondaryProjectileSpawnPoint;  // Cached spawn point for secondary attack
        public Transform hitscanSpawnPoint;              // Cached spawn point for hitscan muzzle effects
        public bool projectileSpawnPointsInitialized;

        // Death VFX renderer binding (particle shape)
        public Renderer deathVFXRenderer;
        
        // Formation Reformation tracking
        public float lastTargetDistance;
        public float targetDistanceChangeRate; // Positive = moving away, negative = approaching
        public bool deathAnimationCompleted;
        
        // Ragdoll System
        public bool ragdollActivated;
        public bool ragdollPending;
        public float ragdollActivationTime;
        public Rigidbody[] ragdollRigidbodies;
        public Collider[] ragdollColliders;
        public Collider mainCharacterCollider; // The main collider on the root (CapsuleCollider, etc.) - disabled during ragdoll
        public CharacterController characterController; // CharacterController if present - disabled during ragdoll
        public HybridWorkflowType lastAliveWorkflow;
        public bool hasPendingRagdollImpulse;
        public Vector3 pendingRagdollImpulse;
        public bool hasAttackerOrientation;
        public Vector3 lastAttackerForward;
        public Vector3 lastAttackerUp;
        
        // Combat Target Tracking
        public bool hasAttackTargetPosition;
        public Vector3 attackTargetPosition;
        public Transform attackTargetTransform;
        public int attackTargetAgentIndex = -1;
        
        // Aggro System
        public bool isAggroed;                    // Whether this agent has aggro on a target
        public float aggroAcquiredTime;           // Time when aggro was first acquired
        public float lastTargetInRangeTime;       // Last time target was within aggroLossRange
        public bool isChasing;                    // Whether agent is actively chasing (between aggro and engagement range)
        
        // Retreat System
        public bool isRetreating;
        public Vector3 retreatDestination;
        public float retreatReleaseTime;
        
        // RTS Manual Command System
        public AgentCommandIntent commandIntent = AgentCommandIntent.None;
        public int commandIntentTargetAgentIndex = -1;
        public bool hasManualCommand;
        public bool hasManualAttackCommand;      // Whether agent has an explicit attack command (allows Passive agents to attack when ordered)
        public Vector3 manualCommandDestination;
        public bool hasRTSFormationPosition;     // Whether agent is holding an RTS-commanded formation position (prevents NavigationController override)
        public bool brokeFromRTSFormation;       // Whether agent broke out from RTS formation position to chase - when combat ends, stay put instead of rejoining crowd formation
        public bool hasFormationFacing;           // Whether a specific facing direction was commanded
        public float formationFacingAngle;        // Y-axis rotation angle (degrees) for formation facing
        public float moveCommandIssuedTime;      // Time when last move command was issued (for disengage grace period)
        
        // Follow Target System (RTS follow command — non-combat, continuously tracks a Transform)
        public Transform followTargetTransform;   // The Transform being followed (null = not following)
        public bool isFollowing;                  // Whether agent is in follow mode
        public Vector3 followFormationOffset;     // Formation offset relative to target (so agents keep formation while following)
        
        // Combat Stance System (RTS-style offensive/defensive/hold position modes)
        public CombatStance combatStance = CombatStance.Offensive;
        public Vector3 holdPositionLocation;      // Position to hold when in HoldPosition stance
        public bool hasHoldPositionSet;           // Whether holdPositionLocation has been initialized
        public bool ignoreAllyDefenseWhileHolding; // When true, HoldPosition agents won't respond to ally defense alerts
        public float lastDamagedByEnemyTime;      // Time when last damaged (for Defensive stance retaliation)
        public int lastDamagedByAgentIndex = -1;  // Index of agent that last attacked us (for Defensive retaliation)
        
        // Developer Test Mode
        public int developerTargetIndex = -1;
        public float nextTargetSearchTime;
        
        // Clip Indices for Variety
        public int lastBlockClipIndex = -1;
        public int lastDeathClipIndex = -1;
        public int lastHitClipIndex = -1;
        
        // Animation Parameter Tracking
        public int activeDefendParameterIndex = -1;
        public int activeDeathParameterIndex = -1;
        public float combatAnimationTransitionEndTime; // Time when combat animation transition completes (prevents animation controller override)
        
        // Hybrid Combat Mode (for CombatRangeType.Hybrid)
        public CombatRangeType activeHybridMode = CombatRangeType.Melee; // Current active mode for hybrid agents
        public float lastHybridModeSwitchTime; // Time when mode last changed (for commitment tracking)
        public CombatRangeOverrideMode combatRangeOverride = CombatRangeOverrideMode.Default; // Optional runtime override for hybrid mode
        
        // ═══════════════════════════════════════════════════════════════════
        // Skill System - Manual Control
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// World time until which movement is locked by skill casting.
        /// 0 means no active movement lock.
        /// </summary>
        public float skillMovementLockEndTime;
        
        /// <summary>
        /// When true, this agent uses manual skill control regardless of faction setting.
        /// Only applies when SkillControlMode is Hybrid. Otherwise uses faction setting.
        /// </summary>
        public bool useManualSkillControl;
        
        /// <summary>
        /// Skill slot index queued for manual execution (-1 = none queued).
        /// Set this to trigger a specific skill on next attack opportunity.
        /// Reset to -1 after skill is executed or fails.
        /// </summary>
        public int queuedSkillSlot = -1;
        
        /// <summary>
        /// Optional target for the queued skill.
        /// -1 = use current combat target.
        /// Used for skills that need explicit targeting (heals, single-target buffs).
        /// </summary>
        public int queuedSkillTargetIndex = -1;
        
        /// <summary>
        /// Optional ground position for GroundTargetedAOE skills.
        /// null = no ground target specified (AI will synthesize one if needed).
        /// Set by the skill bar UI when the player clicks a ground location.
        /// </summary>
        public Vector3? queuedSkillGroundTarget;
        
        /// <summary>
        /// Optional non-agent damageable target for the queued skill.
        /// null = no damageable target (normal agent targeting).
        /// Set by the skill bar UI when the player clicks a DamageableObject.
        /// </summary>
        public IEnemyMassesDamageable queuedSkillDamageableTarget;
        
        /// <summary>
        /// Time.time when queuedSkillSlot was set.
        /// Used by ProcessQueuedSkills to expire stale queues that can never fire
        /// (e.g. target moved away permanently, cooldown desync with auto-cast).
        /// </summary>
        public float queuedSkillTime;
        
        /// <summary>
        /// Time when last skill was used (for global cooldown).
        /// Prevents skill spam regardless of individual skill cooldowns.
        /// </summary>
        public float lastSkillUseTime;
        
        /// <summary>
        /// Time of next AI skill evaluation for this agent.
        /// Used to stagger evaluations across frames for performance.
        /// </summary>
        public float nextSkillEvaluationTime;
        
        // ── Cast Time State ──────────────────────────────────────────────
        
        /// <summary>
        /// True while this agent is in the middle of a skill cast (cast time > 0).
        /// While casting, no new skills can be evaluated or queued.
        /// </summary>
        public bool isCasting;
        
        /// <summary>
        /// World time (Time.time) when the current cast finishes.
        /// Only valid when <see cref="isCasting"/> is true.
        /// </summary>
        public float castEndTime;
        
        /// <summary>
        /// The skill slot being cast (needed for cooldown + finish actions on completion).
        /// </summary>
        public AgentSkillSlot castingSlot;
        
        /// <summary>
        /// The skill asset being cast (cached for interrupt checks like CastInterruptedByDamage).
        /// </summary>
        public AgentSkill castingSkill;
        
        /// <summary>
        /// Execution context saved at cast start.
        /// Holds caster, targets, crit info, and everything needed to fire deferred
        /// OnHit / OnFinish / projectile / events when the cast completes.
        /// </summary>
        public SkillExecutionContext pendingCastContext;
        
        // Staggered Combat Positioning (for CombatPositioningMode.Staggered)
        public float staggeredAttackDelayOffset; // Random delay added to attack timing for this agent
        public float staggeredAngleOffset; // Random angle offset for positioning variety
        public float staggeredDistanceVariance; // Random distance variance from ideal position
        public float staggeredNextRepositionTime; // Time when agent can reposition to add movement variety
        
        // Organized Swarm Combat Positioning (for CombatPositioningMode.OrganizedSwarm)
        public int waveGroupIndex = -1; // Which wave group this agent belongs to (-1 = unassigned)
        public bool isActiveWave; // Whether this agent's wave is currently the active attacking wave
        public bool wasRecentlyAttacked; // Flag set when agent takes damage, allows defensive response
        public float lastAttackedTime; // Time when agent was last attacked (for reactive defense timeout)
        
        // Ally Defense System
        public float lastAllyAlertTime; // Time when this agent last triggered an ally alert (for cooldown)
        public int alertedByAgentIndex = -1; // Index of the ally who alerted us (the victim, -1 = not alerted)
        public int rushTargetAgentIndex = -1; // Index of the attacker we're rushing to fight (-1 = no rush target)
        public bool wasAlertedByAlly; // Flag set when this agent was alerted by a nearby ally being attacked
        public bool isRushing; // Whether this agent is currently in ally rush mode

        public bool HasExplicitAttackIntent => commandIntent == AgentCommandIntent.AttackExplicit || hasManualAttackCommand;

        public void SetExplicitAttackIntent(int targetAgentIndex)
        {
            commandIntent = AgentCommandIntent.AttackExplicit;
            commandIntentTargetAgentIndex = targetAgentIndex;
            hasManualAttackCommand = true;
        }

        public void SetMoveIntent()
        {
            commandIntent = AgentCommandIntent.Move;
            commandIntentTargetAgentIndex = -1;
            hasManualAttackCommand = false;
        }

        public void ClearCommandIntent()
        {
            commandIntent = AgentCommandIntent.None;
            commandIntentTargetAgentIndex = -1;
            hasManualAttackCommand = false;
        }

        /// <summary>
        /// Centralized method to return an agent to clean Formation state after combat ends.
        /// This is the SINGLE AUTHORITY for formation recovery — all systems must use this
        /// instead of ad-hoc flag manipulation. Ensures consistent state regardless of which
        /// system (CrowdController, CombatSystem, AggroLoop) triggers the return.
        /// </summary>
        public void ReturnToFormation()
        {
            // Clear all combat/target state
            hasAttackTargetPosition = false;
            attackTargetAgentIndex = -1;
            attackTargetTransform = null;
            attackTargetPosition = Vector3.zero;

            // Clear follow target
            followTargetTransform = null;
            isFollowing = false;
            followFormationOffset = Vector3.zero;

            // Clear command intent (explicit attack/move commands are over)
            ClearCommandIntent();

            // Clear all formation/manual flags — agent is returning to crowd formation
            brokeFromRTSFormation = false;
            hasRTSFormationPosition = false;
            hasFormationFacing = false;

            // Clear aggro state
            isAggroed = false;
            isChasing = false;

            // Reset navigation path so the formation system can assign a new one
            if (navigationAgent != null &&
                (!navigationAgent.Capabilities.RequiresSurfaceValidationForControl || navigationAgent.IsOnNavMesh))
            {
                navigationAgent.ResetPath();
            }
            hasArrived = true;

            // Force to Formation state (bypasses ManualCommand protection)
            this.ForceSetNavigationState(AgentNavigationState.Formation);
        }
        
        // Speed Management - unified system to prevent stacking issues
        public float baseSpeed; // Original speed before ANY modifications (set once at spawn)
        public bool hasBaseSpeedSet; // Whether baseSpeed has been initialized
        public float allyRushSpeedMultiplier = 1f; // Current ally rush multiplier (1 = no boost)
        public float chargeSpeedMultiplier = 1f; // Current charge multiplier (1 = no boost)
        
        // Charge Attack System
        public float lastChargeTime; // Time when this agent last performed a charge attack
        public float nextChargeTime; // Earliest time this agent can charge again
        public bool isCharging; // Whether this agent is currently in a charge attack
        public Vector3 chargeStartPosition; // Position where current charge began
        public Vector3 chargeTargetPosition; // Target position for current charge
        public float chargeStartTime; // Time when current charge began
        public AudioSource chargeLoopAudioSource; // Audio source for looping charge sound
        public ChargeLoopAudioController chargeLoopAudioController; // Self-managing audio controller with timeout
        
        // Virtual Hitbox Modifiers (per-agent buffs/debuffs for hit chance)
        // These modify the faction's base hit chances for this specific agent
        public float criticalHitChanceModifier = 1f; // Multiplier for critical zone hit chance (1 = no change, 2 = double)
        public float receivedCriticalChanceModifier = 1f; // Multiplier for incoming critical hits against this agent (debuff)
        
        // ═══════════════════════════════════════════════════════════════════
        // Status Effect System (Buffs, Debuffs, DOTs, CC)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Maximum status effects per agent (fixed allocation for performance)</summary>
        public const int MAX_STATUS_EFFECTS = 8;
        
        /// <summary>Fixed-size array of active status effects. Check isActive to see if slot is in use.</summary>
        public ActiveStatusEffect[] activeStatusEffects;
        
        /// <summary>Number of active effects (cached for fast iteration)</summary>
        public int activeEffectCount;
        
        /// <summary>Dirty flag - when true, recalculate cached modifiers</summary>
        public bool statusEffectsDirty = true;
        
        // Cached stat modifiers (recalculated when statusEffectsDirty is true)
        // These are MULTIPLIERS applied to base stats (1 = no change)
        
        /// <summary>Damage dealt multiplier (from DamageBuff, Weaken)</summary>
        public float cachedDamageMultiplier = 1f;
        
        /// <summary>Damage received multiplier (from ArmorBuff, ArmorBreak, Vulnerable)</summary>
        public float cachedDamageReceivedMultiplier = 1f;
        
        /// <summary>Attack speed multiplier (from AttackSpeedBuff, AttackSpeedSlow) - affects cooldown</summary>
        public float cachedAttackSpeedMultiplier = 1f;
        
        /// <summary>Movement speed multiplier (from SpeedBuff, Slow)</summary>
        public float cachedMoveSpeedMultiplier = 1f;
        
        /// <summary>Block chance bonus (additive, from BlockBuff, BlockBreak)</summary>
        public float cachedBlockChanceBonus = 0f;
        
        /// <summary>Critical hit chance bonus (additive, from CriticalBuff)</summary>
        public float cachedCritChanceBonus = 0f;
        
        /// <summary>Shield amount remaining (from Shield effect)</summary>
        public float cachedShieldAmount = 0f;
        
        // CC State Flags (cached for fast checks)
        /// <summary>Whether agent can move (false if Stunned, Frozen, Sleeping, Rooted)</summary>
        public bool canMove = true;
        
        /// <summary>Whether agent can attack (false if Stunned, Frozen, Sleeping, Dazed, Feared)</summary>
        public bool canAttack = true;
        
        /// <summary>Whether agent is immune to CC</summary>
        public bool isCCImmune = false;
        
        /// <summary>Whether agent is immune to displacement effects (knockback, push, pull).</summary>
        public bool isDisplacementImmune = false;
        
        // Invulnerability - backing field with property for security (god mode prevention)
        private bool _isInvulnerable = false;
        /// <summary>
        /// Whether agent is invulnerable to damage.
        /// In multiplayer, only server/authority can grant invulnerability.
        /// </summary>
        public bool isInvulnerable 
        { 
            get => _isInvulnerable; 
            set
            {
                // Prevent god mode hacks - only authority can set invulnerable
                if (NetworkAuthorityProvider.IsNetworkMode && !isNetworkAuthoritative && networkId != 0)
                {
                    if (value && !_isInvulnerable) // Attempting to enable invulnerability
                    {
                        #if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning($"[Security] Client attempted to enable invulnerability on agent {index}");
                        #endif
                        return;
                    }
                }
                _isInvulnerable = value;
            }
        }
        
        /// <summary>
        /// Server-authoritative invulnerability update.
        /// Only call this from network sync or status effect system.
        /// </summary>
        public void SetInvulnerableAuthoritative(bool invulnerable)
        {
            _isInvulnerable = invulnerable;
        }
        
        /// <summary>Whether agent is feared (for special behavior)</summary>
        public bool isFeared = false;
        
        /// <summary>Source agent index for fear (to flee from)</summary>
        public int fearSourceAgentIndex = -1;
        
        /// <summary>Whether agent is taunted (for special behavior)</summary>
        public bool isTaunted = false;
        
        /// <summary>Source agent index for taunt (to attack)</summary>
        public int tauntSourceAgentIndex = -1;
        
        /// <summary>Initialize status effect storage. Call during agent setup.</summary>
        public void InitializeStatusEffects()
        {
            if (activeStatusEffects == null)
            {
                activeStatusEffects = new ActiveStatusEffect[MAX_STATUS_EFFECTS];
            }
            else
            {
                // Clear all slots
                for (int i = 0; i < MAX_STATUS_EFFECTS; i++)
                {
                    activeStatusEffects[i].Clear();
                }
            }
            activeEffectCount = 0;
            statusEffectsDirty = true;
            ResetCachedModifiers();
        }
        
        /// <summary>Reset all cached modifiers to default values.</summary>
        public void ResetCachedModifiers()
        {
            cachedDamageMultiplier = 1f;
            cachedDamageReceivedMultiplier = 1f;
            cachedAttackSpeedMultiplier = 1f;
            cachedMoveSpeedMultiplier = 1f;
            cachedBlockChanceBonus = 0f;
            cachedCritChanceBonus = 0f;
            cachedShieldAmount = 0f;
            canMove = true;
            canAttack = true;
            isCCImmune = false;
            isDisplacementImmune = false;
            isInvulnerable = false;
            isFeared = false;
            fearSourceAgentIndex = -1;
            isTaunted = false;
            tauntSourceAgentIndex = -1;
            
            // Reset resistance modifiers (from buffs/debuffs)
            ResetResistanceModifiers();
        }
        
        /// <summary>Find a free slot for a new status effect. Returns -1 if none available.</summary>
        public int FindFreeEffectSlot()
        {
            if (activeStatusEffects == null) return -1;
            
            for (int i = 0; i < MAX_STATUS_EFFECTS; i++)
            {
                if (!activeStatusEffects[i].isActive)
                    return i;
            }
            return -1;
        }
        
        /// <summary>Find an existing effect of the given type. Returns -1 if not found.</summary>
        public int FindEffectOfType(StatusEffectType type)
        {
            if (activeStatusEffects == null) return -1;
            
            for (int i = 0; i < MAX_STATUS_EFFECTS; i++)
            {
                if (activeStatusEffects[i].isActive && activeStatusEffects[i].type == type)
                    return i;
            }
            return -1;
        }
        
        /// <summary>Check if agent has a specific effect type active.</summary>
        public bool HasEffect(StatusEffectType type)
        {
            return FindEffectOfType(type) >= 0;
        }
        
        /// <summary>Get all active effects of a category.</summary>
        public int GetEffectsOfCategory(StatusEffectCategory category, int[] outSlots)
        {
            if (activeStatusEffects == null || outSlots == null) return 0;
            
            int count = 0;
            for (int i = 0; i < MAX_STATUS_EFFECTS && count < outSlots.Length; i++)
            {
                if (activeStatusEffects[i].isActive && 
                    StatusEffectHelper.GetCategory(activeStatusEffects[i].type) == category)
                {
                    outSlots[count++] = i;
                }
            }
            return count;
        }
        
        // QueuedCircle Attack Slot Rotation
        public float attackSlotStartTime; // When this agent was assigned an attack slot
        public bool hasActiveAttackSlot; // Whether this agent currently has an attack slot
        public int queuedCircleRingIndex = -1; // Current ring assignment (-1 = unassigned)
        
        // Cleanup & Pooling
        public bool pendingCleanup;
        public bool animatorWasEnabled;
        public float animatorPreviousSpeed;
        
        // Death Sinking Effect
        public bool isSinking;
        public float sinkStartTime;
        public Vector3 sinkStartPosition;
        public Vector3 sinkTargetPosition;
        
        // Ragdoll Sinking (sinking after ragdoll has settled)
        public bool ragdollSinkPending;      // True if ragdoll sink is scheduled
        public float ragdollSinkScheduleTime; // Time when ragdoll sink should begin
        
        // Pending Combat Animation Retry (for when GPU animation fails temporarily)
        public AnimationClip pendingCombatClip;
        public float pendingCombatClipTransition;
        public bool pendingCombatClipLoop;
        public int pendingCombatClipRetries;
        public int pendingCombatClipAnimType; // CombatAnimationType as int (0=Hit, 1=Death, 2=Attack, 3=Block, 4=Other)
        
        // Delayed animator disable for dead agents (allows death animation to complete before disable)
        public bool pendingAnimatorDisable;
        public float animatorDisableTime; // Time.time when animator should be disabled
        
        // ═══════════════════════════════════════════════════════════════════
        // Network Helper Methods
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Whether this agent is networked (has a valid network ID).
        /// </summary>
        public bool IsNetworked => networkId != 0;
        
        /// <summary>
        /// Whether this agent is owned by the local player.
        /// Always true in single-player mode.
        /// </summary>
        public bool IsLocallyOwned => !IsNetworked || isNetworkAuthoritative;
        
        /// <summary>
        /// Initializes the network transform buffer for remote agent interpolation.
        /// Call when an agent becomes a remote (non-authoritative) networked agent.
        /// </summary>
        /// <param name="bufferSize">Number of snapshots to store</param>
        /// <param name="interpolationDelay">Delay in seconds for smooth interpolation</param>
        public void InitializeTransformBuffer(int bufferSize = 16, float interpolationDelay = 0.1f)
        {
            transformBuffer ??= new NetworkTransformBuffer(bufferSize, interpolationDelay);
            transformBuffer.Clear();
        }
        
        /// <summary>
        /// Clears network state when agent is despawned or recycled.
        /// </summary>
        public void ClearNetworkState()
        {
            networkId = 0;
            ownerPlayerId = -1;
            isNetworkAuthoritative = false;
            lastNetworkTick = 0;
            networkDirtyFlags = 0;
            transformBuffer?.Clear();
        }
        
        /// <summary>
        /// Creates a network state snapshot for synchronization.
        /// </summary>
        public NetworkAgentState CreateNetworkState()
        {
            return new NetworkAgentState
            {
                networkId = networkId,
                health = health,
                maxHealth = maxHealth,
                isDead = isDead,
                combatState = combatState,
                navigationState = navigationState,
                targetNetworkId = -1, // Resolved by caller
                animationState = currentState,
                isBlocking = isBlocking,
                ownerPlayerId = ownerPlayerId,
                factionIndex = enemyCrowdIndex,
                deltaFlags = networkDirtyFlags
            };
        }
        
        /// <summary>
        /// Applies a network state snapshot received from the server.
        /// </summary>
        public void ApplyNetworkState(NetworkAgentState state)
        {
            if (state.HasHealthChange)
            {
                health = state.health;
            }
            
            if (state.HasDeadChange)
            {
                isDead = state.isDead;
            }
            
            if (state.HasCombatStateChange)
            {
                combatState = state.combatState;
            }
            
            if (state.HasNavStateChange)
            {
                navigationState = state.navigationState;
            }
            
            if (state.HasBlockingChange)
            {
                isBlocking = state.isBlocking;
            }
            
            if (state.HasAnimationChange)
            {
                currentState = state.animationState;
            }
            
            // Position is handled separately via transform buffer for interpolation
        }
    }
}
