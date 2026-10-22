using UnityEngine;
using UnityEngine.Serialization;
using GPUInstancerPro.CrowdAnimations;
using Arawn.EnemyMasses.Runtime.Civil;
using Arawn.EnemyMasses.Runtime.Navigation.Climbing;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Factions;
using Arawn.EnemyMasses.Runtime.Skills;
using Arawn.EnemyMasses.Runtime.Skills.Scaling;
using Arawn.EnemyMasses.Runtime.Localization;
using EnemyMasses.VFX;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// ScriptableObject that defines a faction/type of enemy crowd with specific prefab and characteristics.
    /// Create instances via: Right-click in Project > Create > Enemy Masses > Enemy Crowd
    /// Easily extendable for new features like health multipliers, damage bonuses, special abilities, etc.
    /// </summary>
    [CreateAssetMenu(fileName = "New Enemy Crowd", menuName = "Enemy Masses/Enemy Crowd", order = 1)]
    [Icon("Assets/Plugins/EnemyMasses/Editor/Icons/EnemyCrowdIcon.png")]
    public class EnemyCrowd : ScriptableObject
    {
        public string factionName = "Enemy Faction";
        
        public string enemyTypeName = "Soldier";

        [Header("Localization (Optional)")]
        [Tooltip("Optional Unity Localization reference for the displayed faction name.")]
        public OptionalLocalizedString factionNameLocalization;

        [Tooltip("Optional Unity Localization reference for the displayed enemy type name.")]
        public OptionalLocalizedString enemyTypeNameLocalization;
        
        public GameObject prefab;
        
        public Color factionColor = Color.white;

        // ============================================================================
        // AGENT ARCHETYPE
        // ============================================================================
        
        [Header("Agent Archetype")]
        [Tooltip("Defines the fundamental movement and body type of agents in this crowd.\n\n" +
                 "Humanoid: Standard bipedal characters (soldiers, zombies, civilians).\n" +
                 "FlyingHumanoid: Humanoid agents that start flying and can optionally land (angels, jetpack troops).\n" +
                 "FlyingVehicle: Non-humanoid flying/hovering entities that never use locomotion animations (spaceships, drones, gunships).\n" +
                 "GroundVehicle: Non-humanoid ground entities that don't use locomotion animations (tanks, mechs).")]
        public AgentArchetype agentArchetype = AgentArchetype.Humanoid;

        [Tooltip("When enabled, agents spawn already in flight mode and remain airborne permanently.\n" +
                 "They skip the take-off animation and start at the configured flight height.\n" +
                 "The landing animation is only played if explicitly triggered (e.g., by a skill or script).")]
        public bool persistentFlight = false;

        [Tooltip("Navigation type used for 3D free-flight movement (vehicles, spaceships).\n" +
                 "Only used when agentArchetype is FlyingVehicle or FlyingHumanoid with persistentFlight.\n" +
                 "When set to a 3D type, agents use volumetric steering instead of ground-based pathfinding.")]
        public NavigationType3D flight3DNavigationType = NavigationType3D.Boids;

        [Header("Portrait")]
        [Tooltip("Manual: assign your own portrait texture. AutoGenerate: create a portrait from the prefab in the editor.")]
        public PortraitMode portraitMode = PortraitMode.AutoGenerate;

        [Tooltip("The portrait texture used by UI to represent this EnemyCrowd. Assigned manually or auto-generated from the prefab.")]
        public Texture2D portrait;
        
        public bool useMinimapColorOverride = false;
        
        public Color minimapColorOverride = Color.red;
        
        public bool useOwnerPlayerColor = false;

        public FactionRelationshipConfig factionRelationshipConfig = new FactionRelationshipConfig();
        
        public Skills.SkillSystemConfig skillSystemConfig = new Skills.SkillSystemConfig();
        
        public bool overrideFormation = false;
        
        public FormationType formationType = FormationType.Circle;
        
        public FormationType formationTypeOverride = FormationType.Random;
        
        public Material factionMaterial;
        
        public DeathEffectsConfig deathEffectsConfig = new DeathEffectsConfig();
        
        public GameObject deathEffectPrefab;
        
        [Range(1, 99)]
        public int baseLevel = 1;
        
        [Range(0, 20)]
        public int levelVariance = 0;
        
        public SkillScalingConfig globalSkillScaling = new SkillScalingConfig();
        
        [Range(0.1f, 10f)]
        public float healthMultiplier = 1f;
        
        [Range(0.1f, 10f)]
        public float damageMultiplier = 1f;
        
        [Range(0f, 10f)]
        public float incomingDamageMultiplier = 1f;
        
        [Range(0.1f, 5f)]
        public float speedMultiplier = 1f;
        
        public float armor = 0f;
        
        public float magicResist = 0f;
        
        public bool useElementalResistances = false;
        
        public float fireResistance = 0f;
        
        public float iceResistance = 0f;
        
        public float lightningResistance = 0f;
        
        public float poisonResistance = 0f;

        public bool enableDebugDamageLogging = true;
        
        public VirtualHitboxConfig virtualHitbox = new VirtualHitboxConfig();
        
        public CombatBehaviorTemplate combatBehaviorTemplate = CombatBehaviorTemplate.MeleeQueued;
        
        public CombatRangeType combatRangeType = CombatRangeType.Melee;

        public bool enableMeleeAutoAttack = true;

        [Tooltip("When enabled, ranged auto-attacks can damage flying agents (FlyingVehicle, FlyingHumanoid).\n" +
                 "When disabled, ranged auto-attacks only hit ground targets — use Agent Skills with Ranged (Anti-Air) reach for anti-air.")]
        public bool rangedAutoAttackCanHitFlying = true;
        
        [Min(0.1f)]
        public float meleeMinDistance = 1.0f;
        
        [Min(0.1f)]
        public float meleeMaxDistance = 1.5f;
        
        public MeleeAttackEffectsConfig meleeAttackEffectsConfig = new MeleeAttackEffectsConfig();
        
        [Min(0.1f)]
        public float rangedMinDistance = 5f;
        
        [Min(1f)]
        public float rangedMaxDistance = 20f;
        
        [Min(1f)]
        public float rangedPreferredDistance = 10f;
        
        public RangedAttackMode rangedAttackMode = RangedAttackMode.Hitscan;
        
        public HitscanEffectsConfig hitscanEffectsConfig = new HitscanEffectsConfig();
        
        public ProjectileAttackConfig projectileConfig = new ProjectileAttackConfig();
        
        public bool enableSecondaryRangedAttack = false;
        
        [Range(0f, 1f)]
        public float secondaryAttackChance = 0.2f;
        
        public RangedAttackMode secondaryAttackMode = RangedAttackMode.ProjectileArc;
        
        public ProjectileAttackConfig secondaryProjectileConfig = new ProjectileAttackConfig();
        
        [Range(0f, 30f)]
        public float secondaryAttackCooldown = 5f;
        
        /// <summary>
        /// Returns true if this faction uses projectile attacks (either linear or parabolic).
        /// Convenience property for checking if projectile system should be used.
        /// </summary>
        public bool UseProjectileAttacks => IsProjectileMode(rangedAttackMode) || 
                                            (enableSecondaryRangedAttack && IsProjectileMode(secondaryAttackMode));
        
        /// <summary>
        /// Returns true if this faction uses parabolic/arc projectiles (grenades, catapults).
        /// </summary>
        public bool UseArcProjectiles => rangedAttackMode == RangedAttackMode.ProjectileArc ||
                                         (enableSecondaryRangedAttack && secondaryAttackMode == RangedAttackMode.ProjectileArc);

        /// <summary>
        /// Returns true when legacy ranged auto-attacks are enabled for primary ranged mode.
        /// </summary>
        public bool IsPrimaryRangedAttackEnabled => rangedAttackMode != RangedAttackMode.DisabledUseSkills;

        /// <summary>
        /// Returns true if this crowd has any means of damaging flying agents:
        /// ranged auto-attacks with <see cref="rangedAutoAttackCanHitFlying"/> enabled
        /// OR at least one skill with <see cref="Skills.AttackReachType.RangedAntiAir"/> reach.
        /// Used by the combat AI to skip flying targets the agent could never damage.
        /// </summary>
        public bool HasAntiAirCapability
        {
            get
            {
                // Ranged auto-attack path
                bool isRangedCapable = combatRangeType == CombatRangeType.Ranged ||
                                       combatRangeType == CombatRangeType.Hybrid;
                if (isRangedCapable && IsPrimaryRangedAttackEnabled && rangedAutoAttackCanHitFlying)
                    return true;

                // Skill path — any enabled slot with anti-air reach
                if (skillSystemConfig?.enableSkillSystem == true && skillSystemConfig.skillSlots != null)
                {
                    foreach (var slot in skillSystemConfig.skillSlots)
                    {
                        if (slot?.skill != null && slot.enabled && slot.skill.CanDamageFlying)
                            return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if the attack mode uses projectile simulation.
        /// </summary>
        public static bool IsProjectileMode(RangedAttackMode mode)
        {
            return mode == RangedAttackMode.Projectile || mode == RangedAttackMode.ProjectileArc;
        }

        public string LocalizedFactionName => GetDisplayFactionName();
        public string LocalizedEnemyTypeName => GetDisplayEnemyTypeName();

        /// <summary>
        /// Returns faction display name with optional localization fallback.
        /// </summary>
        public string GetDisplayFactionName()
        {
            return factionNameLocalization.Resolve(factionName);
        }

        /// <summary>
        /// Returns enemy type display name with optional localization fallback.
        /// </summary>
        public string GetDisplayEnemyTypeName()
        {
            return enemyTypeNameLocalization.Resolve(enemyTypeName);
        }

        /// <summary>
        /// Returns combined display label with optional localization fallback.
        /// </summary>
        public string GetDisplayLabel(string separator = " ")
        {
            string factionDisplay = GetDisplayFactionName();
            string enemyTypeDisplay = GetDisplayEnemyTypeName();

            if (string.IsNullOrWhiteSpace(enemyTypeDisplay))
                return factionDisplay;

            if (string.IsNullOrWhiteSpace(factionDisplay))
                return enemyTypeDisplay;

            return factionDisplay + separator + enemyTypeDisplay;
        }
        
        /// <summary>
        /// Determines which attack mode to use for a ranged attack based on chance and cooldowns.
        /// </summary>
        /// <param name="lastSecondaryAttackTime">Time of last secondary attack for this agent</param>
        /// <param name="currentTime">Current game time</param>
        /// <returns>The attack mode to use and whether it's the secondary attack</returns>
        public (RangedAttackMode mode, bool isSecondary, ProjectileAttackConfig config) GetRangedAttackMode(float lastSecondaryAttackTime, float currentTime)
        {
            // Legacy ranged auto-attack disabled (skills-only ranged).
            if (!IsPrimaryRangedAttackEnabled)
            {
                return (RangedAttackMode.DisabledUseSkills, false, null);
            }

            // Check if secondary attack is available
            if (enableSecondaryRangedAttack && 
                secondaryAttackChance > 0f &&
                (currentTime - lastSecondaryAttackTime) >= secondaryAttackCooldown)
            {
                // Roll for secondary attack
                if (UnityEngine.Random.value <= secondaryAttackChance)
                {
                    if (secondaryAttackMode != RangedAttackMode.DisabledUseSkills)
                    {
                        return (secondaryAttackMode, true, secondaryProjectileConfig);
                    }
                }
            }
            
            // Use primary attack
            return (rangedAttackMode, false, projectileConfig);
        }
        
        public CombatPositioningMode positioningMode = CombatPositioningMode.QueuedCircle;
        
        [Min(1f)]
        public float formationRadius = 3f;
        
        [Min(0)]
        public int maxConcurrentAttackers = 3;
        
        [Min(0f)]
        public float attackSlotDuration = 5f;
        
        public bool allowOpportunisticAttacks = true;
        
        [Range(10f, 90f)]
        public float minAngleBetweenEnemies = 30f;
        
        [Min(0f)]
        public float closeRangeStopBuffer = 0.35f;
        
        [Min(0f)]
        public float combatFacingSpeed = 720f;
        
        [Range(0f, 3f)]
        public float agentSpacing = 0.8f;
        
        [Range(2, 20)]
        public int waveGroupCount = 4;
        
        [Range(1f, 30f)]
        public float waveAttackDuration = 5f;
        
        [Range(0.5f, 10f)]
        public float waveCooldown = 1.5f;
        
        [Range(3f, 20f)]
        public float waveWaitingDistance = 8f;
        
        public bool reactiveDefense = true;
        
        [Range(0.5f, 3f)]
        public float battleLineSpacing = 1.2f;
        
        [Range(1f, 5f)]
        public float battleLineRankDepth = 2f;
        
        [Range(5, 50)]
        public int battleLineAgentsPerRank = 15;
        
        [Range(0f, 0.5f)]
        public float battleLineFlankingRatio = 0.15f;
        
        [Range(5f, 30f)]
        public float battleLineFlankingWidth = 12f;
        
        [Range(1f, 10f)]
        public float battleLineEngagementDistance = 2f;
        
        public bool battleLineAutoAdvance = true;
        
        [Range(0.1f, 2f)]
        public float battleLineAdvanceSpeed = 0.5f;
        
        [Range(0f, 1f)]
        public float battleLinePositionJitter = 0.3f;
        
        public bool battleLineChaoticCombat = true;
        
        [Range(0f, 1f)]
        public float battleLineAttackTimingVariance = 0.5f;
        
        [Range(0f, 1f)]
        public float battleLineAttackCooldownVariance = 0.3f;
        
        public bool useAdvancedHybridAI = false;
        
        [System.Obsolete("aggressionLevel is no longer used. Attack speed is controlled by attackCooldown and status effect cachedAttackSpeedMultiplier.")]
        [HideInInspector]
        [Range(0.1f, 5f)]
        public float aggressionLevel = 1f;
        
        public bool canUseSpecialAbilities = false;
        
        public ChargeAttackConfig chargeAttackConfig = new ChargeAttackConfig();
        
        public AttackEffectsConfig meleeAttackEffects = new AttackEffectsConfig();
        
        public MeleeCleaveConfig meleeCleaveConfig = new MeleeCleaveConfig();
        
        public AttackEffectsConfig rangedAttackEffects = new AttackEffectsConfig();
        
        public SupportAbilityConfig healingAbility = new SupportAbilityConfig
        {
            effect = new AttackStatusEffect
            {
                effectType = StatusEffectType.Regeneration,
                targetType = EffectTargetType.Ally
            }
        };
        
        public SupportAbilityConfig buffAbility = new SupportAbilityConfig
        {
            effect = new AttackStatusEffect
            {
                effectType = StatusEffectType.DamageBuff,
                targetType = EffectTargetType.Ally
            }
        };
        
        [Range(0f, 1f)]
        public float factionAudioVolume = 1f;
        
        [Range(0f, 0.5f)]
        public float factionAudioPitchVariation = 0.1f;
        
        [Range(0f, 1f)]
        public float factionAudioSpatialBlend = 0.8f;
        
        [Min(0.1f)]
        public float factionAudioMinDistance = 1f;
        
        [Min(1f)]
        public float factionAudioMaxDistance = 50f;
        
        [Range(1, 20)]
        public int maxConcurrentSoundsPerClip = 5;
        
        public AudioClip[] attackSounds;
        
        public AudioClip[] deathSounds;
        
        public AudioClip[] selectionSoundsSingle;
        
        public AudioClip[] selectionSoundsGroup;
        
        [Range(2, 50)]
        public int groupSelectionThreshold = 10;
        
        public AudioClip[] commandSoundsSingle;
        
        public AudioClip[] commandSoundsGroup;
        
        [Range(2, 50)]
        public int groupCommandThreshold = 10;
        
        public AudioClip[] ambientSounds;
        
        [Range(0f, 1f)]
        public float ambientSoundChance = 0.05f;
        
        [Min(1f)]
        public float ambientSoundCooldown = 5f;
        
        public bool enableFootstepAudio = false;
        
        public FootstepAudioConfig footstepAudioConfig = new FootstepAudioConfig();
        
        public bool useGroupFootstepAudio = false;
        
        public FootstepPlaybackMode groupFootstepPlaybackMode = FootstepPlaybackMode.Interval;
        
        public AudioClip[] footstepSoundsSingle;
        
        public AudioClip[] footstepSoundsGroup;
        
        [Range(2, 100)]
        public int groupFootstepThreshold = 10;
        
        [Range(0f, 1f)]
        public float groupFootstepVolume = 0.6f;
        
        [Range(0f, 0.3f)]
        public float groupFootstepPitchVariation = 0.05f;
        
        [Range(0f, 1f)]
        public float groupFootstepSpatialBlend = 1f;
        
        public float groupFootstepMinDistance = 2f;
        
        public float groupFootstepMaxDistance = 60f;
        
        [Range(0.05f, 2f)]
        public float groupFootstepMinInterval = 0.2f;
        
        [Range(0.05f, 2f)]
        public float groupFootstepMaxInterval = 0.4f;
        
        [Range(0.1f, 3f)]
        public float groupFootstepMinSpeed = 0.8f;
        
        public bool scaleIntervalWithAgentCount = true;
        
        [Range(1, 10)]
        public int groupFootstepMaxConcurrent = 2;
        
        public bool enableGroupSurfaceDetection = false;
        
        public SurfaceMapping groupSurfaceMapping;
        
        public AudioClip[] groupFootstepsStoneSingle;
        public AudioClip[] groupFootstepsStoneGroup;
        
        public AudioClip[] groupFootstepsWaterSingle;
        public AudioClip[] groupFootstepsWaterGroup;
        
        public AudioClip[] groupFootstepsMetalSingle;
        public AudioClip[] groupFootstepsMetalGroup;
        
        public AudioClip[] groupFootstepsWoodSingle;
        public AudioClip[] groupFootstepsWoodGroup;
        
        public AudioClip[] groupFootstepsSandSingle;
        public AudioClip[] groupFootstepsSandGroup;
        
        public AudioClip[] groupFootstepsSnowSingle;
        public AudioClip[] groupFootstepsSnowGroup;
        
        public bool enableAirborneDebugLogs = false;
        
        public AnimationClip idleClipOverride;
        
        public AnimationClip walkClipOverride;
        
        public AnimationClip runClipOverride;

        public AnimationClip fallingClipOverride;
        
        public AnimationClip landingClipOverride;

        public AnimationClip[] pushImpulseClipsOverride;

        public AnimationClip[] pullImpulseClipsOverride;

        public AnimationClip[] liftImpulseClipsOverride;

        public AnimationClip liftGetUpClipOverride;
        
        public AnimationClip[] attackClipsOverride;
        
        public AnimationClip[] meleeAttackClipsOverride;
        
        public AnimationClip[] rangedAttackClipsOverride;

        [Header("Wall Placement Animation Overrides")]
        [Tooltip("Optional one-shot clip played when agents are assigned to wall placement spots. Primarily used by ComputeAnimator workflows.")]
        public AnimationClip wallPlacementEnterClipOverride;

        [Tooltip("Optional looping clip played while agents are assigned to wall placement spots. Primarily used by ComputeAnimator workflows.")]
        public AnimationClip wallPlacementLoopClipOverride;

        [Tooltip("Optional one-shot clip played when agents leave wall placement spots. Primarily used by ComputeAnimator workflows.")]
        public AnimationClip wallPlacementExitClipOverride;

        [Tooltip("Optional Animator parameter enabled while agents are assigned to wall placement spots. Best for MecanimReader/BoneTracker workflows (e.g., TakeCover bool).")]
        public string wallPlacementBoolParameterOverride = "";

        [Header("Flight Animation Overrides")]
        [Tooltip("Default flight altitude above ground level (in world units).\n" +
                 "Agents smoothly ascend to this height on take-off and descend on landing.\n" +
                 "Set to 0 for ground-level flight (animation-only, no vertical offset).")]
        [Min(0f)]
        public float flightHeight = 0f;

        [Tooltip("Speed at which agents ascend to flight height (units per second).")]
        [Min(0.1f)]
        public float flightAscendSpeed = 5f;

        [Tooltip("Speed at which agents descend from flight height when landing (units per second).")]
        [Min(0.1f)]
        public float flightDescendSpeed = 8f;

        [Tooltip("Amplitude of the idle bobbing motion while hovering (units). Set to 0 to disable.")]
        [Min(0f)]
        public float flightBobAmplitude = 0.15f;

        [Tooltip("Frequency of the idle bobbing motion while hovering (cycles per second).")]
        [Min(0.01f)]
        public float flightBobFrequency = 0.6f;

        [Tooltip("Optional one-shot clip played when agents take off into flight mode.\n" +
                 "Primarily used by ComputeAnimator workflows.\n" +
                 "After this clip finishes, the flight loop clip plays automatically.")]
        public AnimationClip flightTakeOffClipOverride;

        [Tooltip("Looping clip played while agents are flying and stationary (not moving).\n" +
                 "Primarily used by ComputeAnimator workflows.\n" +
                 "If not set, the flight locomotion loop clip is used for all flying states.")]
        public AnimationClip flightIdleClipOverride;

        [Tooltip("Looping clip played while agents are in flight mode and moving.\n" +
                 "Primarily used by ComputeAnimator workflows.\n" +
                 "If no take-off clip is set, this plays immediately.")]
        public AnimationClip flightLoopClipOverride;

        [Tooltip("Optional one-shot clip played when agents land from flight mode.\n" +
                 "Primarily used by ComputeAnimator workflows.\n" +
                 "After this clip finishes, the agent returns to idle/locomotion.")]
        public AnimationClip flightLandClipOverride;

        [Tooltip("Optional Animator parameter toggled while agents are in flight mode.\n" +
                 "Best for MecanimReader/BoneTracker workflows (e.g., IsFlying bool).\n" +
                 "Set to true on take-off, false on landing.")]
        public string flightBoolParameterOverride = "";

        [Tooltip("Optional Animator state name to CrossFade into for the flight idle state.\n" +
                 "Best for MecanimReader/BoneTracker workflows (e.g., FlightIdle).\n" +
                 "Used when the agent is flying but stationary. Leave empty to let the Animator Controller handle it via parameters.")]
        public string flightIdleStateNameOverride = "";

        [Tooltip("Optional Animator trigger fired when agents take off into flight mode.\n" +
                 "Best for MecanimReader/BoneTracker workflows (e.g., TakeOff trigger).")]
        public string flightTakeOffTriggerParameterOverride = "";

        [Tooltip("Optional Animator trigger fired when agents land from flight mode.\n" +
                 "Best for MecanimReader/BoneTracker workflows (e.g., Land trigger).")]
        public string flightLandTriggerParameterOverride = "";

        [Header("Flight Animation Blending")]
        [Tooltip("Crossfade duration (seconds) when transitioning between flight animation clips " +
                 "(e.g. take-off → flight idle, flight idle → flight locomotion).\n" +
                 "Higher values produce smoother blends between flight states.")]
        [Range(0.05f, 1f)]
        public float flightTransitionTime = 0.3f;

        [Tooltip("Crossfade duration (seconds) when transitioning from the landing clip back to " +
                 "ground locomotion (idle/walk/run).\n" +
                 "This prevents the hard 'snap' at the end of the landing animation.\n" +
                 "Set higher for a more gradual blend back to ground movement.")]
        [Range(0.05f, 2f)]
        public float flightLandToGroundTransitionTime = 0.45f;

        [Tooltip("Crossfade duration (seconds) when transitioning from ground locomotion " +
                 "into the take-off clip (or directly into flight idle/loop when no take-off clip is assigned).\n" +
                 "Higher values smooth the jump from ground to airborne.")]
        [Range(0.05f, 1f)]
        public float flightGroundToTakeOffTransitionTime = 0.25f;

        [Header("Flight Crash on Death")]
        [Tooltip("When enabled, flying agents simulate a physics-based crash descent when killed in flight " +
                 "instead of freezing in the air.\n" +
                 "The vehicle accelerates downward under gravity with forward momentum and nose-down tumble.")]
        public bool flightCrashOnDeath = true;

        [Tooltip("Downward acceleration during crash descent (m/s²).\n" +
                 "Higher values make the vehicle plummet faster. Earth gravity ≈ 9.81.")]
        [Min(1f)]
        public float flightCrashGravity = 12f;

        [Tooltip("Initial downward velocity at the start of the crash (m/s).\n" +
                 "Adds an immediate downward impulse so the crash doesn't start from zero.")]
        [Min(0f)]
        public float flightCrashInitialDownSpeed = 2f;

        [Tooltip("Fraction of the agent's current movement speed preserved as forward momentum during the crash.\n" +
                 "0 = drop straight down, 1 = full speed carried forward.\n" +
                 "The momentum decays over time producing a ballistic arc.")]
        [Range(0f, 1f)]
        public float flightCrashForwardMomentum = 0.5f;

        [Tooltip("Nose-down tumble rate during crash descent (degrees per second).\n" +
                 "Creates a diving visual effect. The angle is capped at ~80° to prevent full inversion.")]
        [Min(0f)]
        public float flightCrashTumbleSpeed = 120f;

        [Tooltip("Roll spiral rate during crash descent (degrees per second).\n" +
                 "Adds a corkscrew rotation around the forward axis. Set to 0 for no spiral.")]
        [Min(0f)]
        public float flightCrashSpiralSpeed = 45f;

        public AnimationClip[] blockClipsOverride;
        
        public AnimationClip[] hitClipsOverride;
        
        public AnimationClip[] deathClipsOverride;
        
        public enum RootMotionOverrideMode
        {
            UseControllerSetting = 0,
            ForceEnabled = 1,
            ForceDisabled = 2
        }
        
        /// <summary>
        /// Controls how often line-of-sight checks are performed for performance optimization.
        /// </summary>
        public enum LOSCheckMode
        {
            /// <summary>Check LOS for every potential target. Most accurate but highest cost.</summary>
            EveryCheck = 0,
            /// <summary>Only raycast the 3 closest candidates after distance/height filtering. Good balance.</summary>
            ClosestCandidatesOnly = 1,
            /// <summary>25% random chance to check LOS per target. Amortizes cost over time.</summary>
            Probabilistic = 2
        }

        public RootMotionOverrideMode rootMotionOverride = RootMotionOverrideMode.UseControllerSetting;
        
        public string locomotionStateNameOverride = "";

        public LocomotionBlendMode locomotionBlendModeOverride = LocomotionBlendMode.OneD;

        public string locomotionSpeedFloatParameterOverride = "";

        public string locomotionDirectionXFloatParameterOverride = "";

        public string locomotionDirectionYFloatParameterOverride = "";

        public bool enableDirectionalLocomotionOverride = false;

        public DirectionalLocomotionClips directionalLocomotionAnimationsOverride = new DirectionalLocomotionClips();

        [Range(0f, 1f)]
        public float directionalLocomotionThresholdOverride = 0.4f;
        
        public string[] attackTriggerParametersOverride;

        public string[] meleeAttackTriggerParametersOverride;

        public string[] rangedAttackTriggerParametersOverride;
        
        public string defendBoolParameterOverride = "";
        
        public string[] defendBoolParametersOverride;
        
        public string[] hitTriggerParametersOverride;
        
        public string[] deathTriggerParametersOverride;

        public string[] directionalHitTriggerParametersOverride;

        public string[] directionalBlockTriggerParametersOverride;

        public string[] directionalAttackTriggerParametersOverride;
        
        public bool enableRagdollOnDeath = false;
        
        public bool overrideDirectionalHitAnimations = false;
        
        public DirectionalAnimationSet hitAnimationsOverride = new DirectionalAnimationSet();
        
        public bool overrideDirectionalDeathAnimations = false;
        
        public DirectionalAnimationSet deathAnimationsOverride = new DirectionalAnimationSet();
        
        public bool overrideDirectionalBlockAnimations = false;
        
        public DirectionalAnimationSet blockAnimationsOverride = new DirectionalAnimationSet();
        
        public bool overrideDirectionalAttackAnimations = false;
        
        public DirectionalAnimationSet attackAnimationsOverride = new DirectionalAnimationSet();
        
        public ClimbingAnimationConfig climbingAnimationConfig = new ClimbingAnimationConfig();
        
        #region Integrated Settings (Phase 1: All settings moved to EnemyCrowd)
        
        // ============================================================================
        // SPAWN SETTINGS
        // ============================================================================
        
        public GameObject indicatorPrefab;
        
        // Note: prefabManager and registerImmediately are now global settings in the controller
        
        public FormationType defaultFormationType = FormationType.Random;
        
        [Min(1f)]
        public float defaultFormationSize = 10f;
        
        [Min(0.5f)]
        public float defaultFormationSpacing = 2f;
        
        [Min(0.5f)]
        public float defaultFormationThickness = 3f;
        
        // ============================================================================
        // NAVIGATION SETTINGS
        // ============================================================================
        
        public NavigationType navigationType = NavigationType.UnityNavMesh;
        
        [Min(0.1f)]
        public float agentBaseSpeed = 3.5f;
        
        [Min(0f)]
        public float destinationSpread = 25f;
        
        public bool throttleDestinationUpdates = true;
        
        [Min(1)]
        public int maxDestinationUpdatesPerFrame = 256;
        
        [Min(0)]
        public int priorityDestinationBonus = 128;
        
        [Min(0.01f)]
        public float offscreenNavSyncInterval = 0.25f;
        
        [Range(0f, 1f)]
        public float navmeshRotationLerp = 0.15f;
        
        public bool enableStuckRecovery = true;
        
        public bool teleportStuckAgents = true;
        
        [Min(0.5f)]
        public float stuckTimeout = 6f;
        
        [Min(0f)]
        public float stuckProximitySlack = 0.5f;
        
        [Min(0f)]
        public float stuckVelocityThreshold = 0.15f;
        
        [Min(0.5f)]
        public float stuckRetryInterval = 4f;
        
        [Min(0)]
        public int stuckRetryAttempts = 3;
        
        public bool teleportAfterRetries = false;
        
        public bool enableArrivalSync = true;
        
        [Min(0f)]
        public float arrivalSyncDistance = 5f;
        
        public bool reapplyFormationOnArrival = false;
        
        public bool enableFormationReformation = false;
        
        [Min(0f)]
        public float reformationDistance = 20f;
        
        [Min(0f)]
        public float formationDissolveDistance = 10f;
        
        [Min(1f)]
        public float reformationSize = 8f;
        
        [Min(0.5f)]
        public float reformationSpacing = 2f;
        
        [Min(1f)]
        public float reformationThickness = 2f;
        
        // ============================================================================
        // ANIMATION SETTINGS
        // ============================================================================
        
        [Range(0.01f, 0.1f)]
        public float minBlendUpdateDelta = 0.02f;
        
        public bool controlAnimationsWithGPUI = true;
        
        public AnimatorWorkflowOverride animatorWorkflow = AnimatorWorkflowOverride.ComputeAnimator;
        
        public bool throttleAnimationUpdates = true;
        
        [Min(1)]
        public int maxAnimationUpdatesPerFrame = 500;
        
        [Min(0)]
        public int priorityAnimationBonus = 250;
        
        // Note: cullAnimationsByDistance, animationCullDistance, animationCullReference are now global settings in the controller
        
        [Min(0f)]
        public float speedSmoothing = 8f;
        
        // ============================================================================
        // COMBAT SETTINGS
        // ============================================================================
        
        public bool enableCombatSystem = false;
        
        [Min(1f)]
        public float aggroRange = 50f;
        
        [Min(0f)]
        public float sightRange = 0f;
        
        [Min(1f)]
        public float aggroLossRange = 60f;
        
        [Min(0f)]
        public float aggroLossDelay = 3f;
        
        [Min(0f)]
        public float maxTargetHeightDifference = 0f;
        
        public bool requireLineOfSight = false;
        
        public LOSCheckMode losCheckMode = LOSCheckMode.ClosestCandidatesOnly;
        
        public LayerMask lineOfSightBlockingLayers = ~0; // Default: all layers
        
        public bool enableAllyDefense = true;
        
        [Min(1f)]
        public float allyDefenseRadius = 20f;
        
        public bool allyDefenseTargetAttacker = true;
        
        [Min(0)]
        public int allyDefenseMaxResponders = 8;
        
        [Min(0f)]
        public float allyDefenseAlertCooldown = 1f;
        
        [Range(1f, 5f)]
        public float allyRushSpeedMultiplier = 2.5f;

        [Min(1f)]
        public float engagementRadius = 15f;
        
        [Min(1f)]
        public float agentBaseHealth = 50f;
        
        [Min(0f)]
        public float agentHealthVariance = 15f;
        
        [Min(0f)]
        [FormerlySerializedAs("agentAttackDamage")]
        public float meleeAttackDamage = 10f;

        [Min(0f)]
        public float rangedAttackDamage = 10f;

        [SerializeField, HideInInspector]
        private bool _splitDamageInitialized = false;

        /// <summary>
        /// Legacy compatibility accessor.
        /// New setups should use meleeAttackDamage and rangedAttackDamage.
        /// </summary>
        public float agentAttackDamage
        {
            get => meleeAttackDamage;
            set
            {
                meleeAttackDamage = value;
                rangedAttackDamage = value;
                _splitDamageInitialized = true;
            }
        }
        
        [Min(0f)]
        public float attackCooldown = 2f;
        
        public Vector2 attackCooldownJitter = new Vector2(0.5f, 1.5f);
        
        [Range(0f, 1f)]
        public float blockChance = 0.2f;
        
        public Vector2 blockHoldTimeRange = new Vector2(0.4f, 0.85f);
        
        [Min(8)]
        public int maxCombatUpdatesPerFrame = 512;
        
        [Min(0.1f)]
        public float navTargetUpdateInterval = 0.2f;
        
        public bool maintainFacingBetweenAttacks = true;
        
        public bool stopMovementWhenClose = true;
        
        public bool lockMovementDuringAttacks = true;

        public bool triggerHitReactionOnDamage = true;
        
        public bool enablePostAttackRetreat = true;
        
        public Vector2 postAttackRetreatDistance = new Vector2(0.75f, 1.5f);
        
        public Vector3 ragdollImpulse = Vector3.zero;
        
        public Vector3 ragdollImpulseMin = Vector3.zero;
        
        public Vector3 ragdollImpulseMax = Vector3.zero;
        
        public DeadAgentCleanupMode deadAgentCleanupMode = DeadAgentCleanupMode.Disable;
        
        [Min(0f)]
        public float deadAgentCleanupDelay = 5f;
        
        [Min(1)]
        public int maxDeadAgentCleanupPerFrame = 10;
        
        public bool batchDeadAgentCleanup = true;
        
        public bool disableComponentsOnDeath = true;
        
        public bool optimizeDeadAgentLists = true;
        
        public bool enableDeathSinking = false;
        
        public Vector3 deathSinkOffset = new Vector3(0f, -2f, 0f);
        
        [Min(0.1f)]
        public float deathSinkDuration = 2f;
        
        public AnimationCurve deathSinkCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        // ============================================================================
        // PERFORMANCE SETTINGS
        // ============================================================================
        // Note: All performance settings (throttling, priorities, spatial grid, visibility) 
        // are now GLOBAL settings in EnemyMassesCrowdController as they affect the entire system.
        // Per-faction performance differences are handled automatically based on crowd size and distance.
        
        // ============================================================================
        // CIVIL NPC SETTINGS
        // ============================================================================
        
        public CivilBehaviorSettings civilSettings = new CivilBehaviorSettings();
        
        #endregion
        
        // ============================================================================
        // VEHICLE / NON-HUMANOID SETTINGS
        // ============================================================================
        
        [Header("Vehicle / Non-Humanoid")]
        [Tooltip("Hitbox preset for this crowd. Humanoid uses head/torso/legs zones.\n" +
                 "Vehicle uses a single center-mass sphere. Custom allows manual zone definition.")]
        public HitboxPreset hitboxPreset = HitboxPreset.Humanoid;

        [Tooltip("For Vehicle archetype: overall bounding radius of the vessel (meters).\n" +
                 "Used for collision avoidance, target selection, and the center-mass hitbox.")]
        [Min(0.5f)]
        public float vehicleBoundingRadius = 2f;

        [Tooltip("For Vehicle archetype: overall bounding height of the vessel (meters).")]
        [Min(0.5f)]
        public float vehicleBoundingHeight = 2f;

        [Tooltip("For 3D navigation: preferred cruising altitude above the ground plane (world units).\n" +
                 "Agents will try to maintain this altitude during normal flight.")]
        [Min(0f)]
        public float cruisingAltitude = 10f;

        [Tooltip("For 3D navigation: how much altitude can vary randomly between agents to create visual depth.")]
        [Min(0f)]
        public float altitudeVariance = 3f;

        [Tooltip("For 3D navigation: vertical speed limit (units/sec) for ascending/descending to target altitude.")]
        [Min(0.1f)]
        public float verticalSpeed = 5f;

        [Tooltip("For 3D navigation: banking angle (degrees) applied when turning. Cosmetic only.")]
        [Range(0f, 60f)]
        public float bankAngle = 25f;

        [Tooltip("For 3D navigation: how fast agents bank into turns (degrees/sec).")]
        [Min(1f)]
        public float bankSpeed = 90f;

        [Tooltip("For 3D navigation: how quickly the visual heading adapts to velocity changes.\n" +
                 "Higher = snappier turns, lower = smoother, more airplane-like arcs.\n" +
                 "Range: 1 (very smooth) – 10 (instant).")]
        [Range(1f, 10f)]
        public float headingSmoothRate = 5f;

        [Tooltip("For 3D navigation: maximum nose-up / nose-down angle in degrees when climbing or descending.\n" +
                 "0 = nose stays level regardless of vertical speed.")]
        [Range(0f, 60f)]
        public float maxPitchAngle = 30f;

        [Tooltip("For 3D navigation: lock specific rotation axes on flying vehicles.\n" +
                 "LockPitch = nose stays level, LockYaw = no turning, LockRoll = no banking.\n" +
                 "Combine flags to lock multiple axes.")]
        public RotationAxisLock rotationAxisLock = RotationAxisLock.None;

        [Tooltip("For 3D navigation: amplitude of the idle hover bobbing motion (world units).\n" +
                 "Adds a subtle vertical sine wave to agents at cruising altitude.\n" +
                 "Set to 0 to disable.")]
        [Min(0f)]
        public float hoverBobAmplitude = 0.15f;

        [Tooltip("For 3D navigation: frequency of the idle hover bobbing motion (cycles per second).")]
        [Min(0.01f)]
        public float hoverBobFrequency = 0.6f;

        [Tooltip("For 3D navigation: when enabled, agents maintain their cruising altitude and only navigate on the XZ plane.\n" +
                 "Destinations are projected to the agent's personal altitude. Ideal for RTS-style games.\n" +
                 "When disabled, agents fly directly to the destination's Y position.")]
        public bool maintainAltitude = true;

        // ============================================================================
        // 3D FORMATION SETTINGS
        // ============================================================================

        [Header("3D Formation")]
        [Tooltip("Number of vertical layers in 3D formations. 1 = single plane (classic 2D formation).")]
        [Range(1, 10)]
        public int formationLayers = 1;

        [Tooltip("Vertical spacing between formation layers (world units).")]
        [Min(0.5f)]
        public float formationLayerSpacing = 5f;

        // ============================================================================
        // CONVENIENCE PROPERTIES
        // ============================================================================

        /// <summary>
        /// Returns true if this crowd's archetype requires 3D (volumetric) navigation.
        /// </summary>
        public bool Requires3DNavigation => agentArchetype == AgentArchetype.FlyingVehicle || 
                                            (persistentFlight && agentArchetype == AgentArchetype.FlyingHumanoid);

        /// <summary>
        /// Returns true if this crowd uses non-humanoid rendering/animation (no idle/walk/run/attack clips expected).
        /// </summary>
        public bool IsNonHumanoid => agentArchetype == AgentArchetype.FlyingVehicle || 
                                     agentArchetype == AgentArchetype.GroundVehicle;

        /// <summary>
        /// Returns true if agents in this crowd should start in flight mode on spawn.
        /// </summary>
        public bool SpawnsFlying => persistentFlight || agentArchetype == AgentArchetype.FlyingVehicle;
        
        /// <summary>
        /// Validates the enemy crowd data. Called by Unity Editor and at runtime.
        /// </summary>
        private void OnValidate()
        {
            EnsureSplitDamageInitialized();

            if (string.IsNullOrEmpty(factionName))
                factionName = name; // Use asset name as default
            
            if (string.IsNullOrEmpty(enemyTypeName))
                enemyTypeName = "Soldier"; // Default enemy type
                
            if (prefab == null)
                Debug.LogWarning($"Enemy Crowd '{GetFullName()}' has no prefab assigned!", this);
        }
        
        /// <summary>
        /// Gets the GPUICrowdInstance component from the prefab.
        /// Returns null if prefab is null or doesn't have the component.
        /// </summary>
        public GPUICrowdInstance GetCrowdInstance()
        {
            if (prefab == null)
                return null;
            return prefab.GetComponent<GPUICrowdInstance>();
        }
        
        /// <summary>
        /// Returns a formatted description of this enemy crowd for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"EnemyCrowd: {factionName} - {enemyTypeName} (Health: x{healthMultiplier}, Damage: x{damageMultiplier}, Speed: x{speedMultiplier})";
        }
        
        /// <summary>
        /// Gets the full display name combining faction and enemy type.
        /// Example: "Skylor Soldier", "Undead Legion Worker"
        /// </summary>
        public string GetFullName()
        {
            if (!string.IsNullOrEmpty(enemyTypeName))
                return $"{factionName} {enemyTypeName}";
            return factionName;
        }
        
        /// <summary>
        /// Calculates the final health value for an agent based on base health and this faction's multiplier.
        /// </summary>
        public float GetModifiedHealth(float baseHealth)
        {
            return baseHealth * healthMultiplier;
        }
        
        /// <summary>
        /// Calculates the final damage value based on base damage and this faction's multiplier.
        /// </summary>
        public float GetModifiedDamage(float baseDamage)
        {
            return baseDamage * damageMultiplier;
        }
        
        /// <summary>
        /// Calculates the final speed value based on base speed and this faction's multiplier.
        /// </summary>
        public float GetModifiedSpeed(float baseSpeed)
        {
            return baseSpeed * speedMultiplier;
        }
        
        /// <summary>
        /// OBSOLETE: Attack speed is now controlled by attackCooldown and status effect cachedAttackSpeedMultiplier.
        /// This method is kept for backward compatibility but does nothing useful.
        /// </summary>
        [System.Obsolete("Use attackCooldown directly. Attack speed is controlled by status effect cachedAttackSpeedMultiplier.")]
        public float GetModifiedAttackCooldown(float baseAttackCooldown)
        {
            #pragma warning disable CS0618 // aggressionLevel is obsolete
            return baseAttackCooldown / aggressionLevel;
            #pragma warning restore CS0618
        }
        
        /// <summary>
        /// Gets a random attack sound clip, or null if none are assigned.
        /// </summary>
        public AudioClip GetRandomAttackSound()
        {
            if (attackSounds == null || attackSounds.Length == 0)
                return null;
            return attackSounds[Random.Range(0, attackSounds.Length)];
        }
        
        /// <summary>
        /// Gets a random death sound clip from the faction list (used by death effects audio settings).
        /// </summary>
        public AudioClip GetRandomDeathSound()
        {
            if (deathSounds == null || deathSounds.Length == 0)
                return null;
            return deathSounds[Random.Range(0, deathSounds.Length)];
        }
        
        /// <summary>
        /// Resolves the death VFX using the new config or the legacy prefab fallback.
        /// </summary>
        public VFXReference GetDeathVFX()
        {
            if (deathEffectsConfig != null)
                return deathEffectsConfig.GetEffectiveDeathVFX(deathEffectPrefab);
            if (deathEffectPrefab != null)
                return new VFXReference { type = VFXType.ParticleSystem, particleSystemPrefab = deathEffectPrefab };
            return null;
        }
        
        /// <summary>
        /// Gets a random ambient sound clip, or null if none are assigned.
        /// </summary>
        public AudioClip GetRandomAmbientSound()
        {
            if (ambientSounds == null || ambientSounds.Length == 0)
                return null;
            return ambientSounds[Random.Range(0, ambientSounds.Length)];
        }
        
        /// <summary>
        /// Gets a random footstep sound clip, or null if footsteps are not enabled.
        /// </summary>
        public AudioClip GetRandomFootstepSound()
        {
            if (!enableFootstepAudio || footstepAudioConfig == null)
                return null;
            return footstepAudioConfig.GetRandomClip();
        }
        
        /// <summary>
        /// Gets a random footstep sound clip for a specific surface type.
        /// Falls back to default clips if surface-specific clips are not configured.
        /// </summary>
        public AudioClip GetRandomFootstepSound(FootstepSurfaceType surface)
        {
            if (!enableFootstepAudio || footstepAudioConfig == null)
                return null;
            return footstepAudioConfig.GetRandomClipForSurface(surface);
        }
        
        /// <summary>
        /// Gets a random group footstep sound based on the number of moving agents.
        /// Returns single footsteps for small groups, marching sounds for large groups.
        /// </summary>
        /// <param name="movingCount">Number of agents currently moving</param>
        /// <returns>Random group footstep sound clip, or null if none assigned</returns>
        public AudioClip GetRandomGroupFootstepSound(int movingCount)
        {
            return GetRandomGroupFootstepSound(movingCount, FootstepSurfaceType.Grass);
        }
        
        /// <summary>
        /// Gets a random group footstep sound based on the number of moving agents and surface type.
        /// Returns single footsteps for small groups, marching sounds for large groups.
        /// Falls back to default grass/dirt clips if surface-specific clips not assigned.
        /// </summary>
        /// <param name="movingCount">Number of agents currently moving</param>
        /// <param name="surfaceType">The surface type detected at the group center</param>
        /// <returns>Random group footstep sound clip, or null if none assigned</returns>
        public AudioClip GetRandomGroupFootstepSound(int movingCount, FootstepSurfaceType surfaceType)
        {
            if (!useGroupFootstepAudio)
                return null;
            
            bool isLargeGroup = movingCount >= groupFootstepThreshold;
            AudioClip[] sounds = null;
            
            // Try surface-specific clips first if surface detection is enabled
            if (enableGroupSurfaceDetection)
            {
                sounds = GetGroupFootstepClipsForSurface(surfaceType, isLargeGroup);
            }
            
            // Fall back to default clips if no surface-specific clips
            if (sounds == null || sounds.Length == 0)
            {
                sounds = isLargeGroup ? footstepSoundsGroup : footstepSoundsSingle;
            }
            
            if (sounds == null || sounds.Length == 0)
                return null;
            return sounds[Random.Range(0, sounds.Length)];
        }
        
        /// <summary>
        /// Gets the clip array for a specific surface type and group size.
        /// </summary>
        private AudioClip[] GetGroupFootstepClipsForSurface(FootstepSurfaceType surfaceType, bool isLargeGroup)
        {
            switch (surfaceType)
            {
                case FootstepSurfaceType.Stone:
                    return isLargeGroup ? groupFootstepsStoneGroup : groupFootstepsStoneSingle;
                case FootstepSurfaceType.Water:
                    return isLargeGroup ? groupFootstepsWaterGroup : groupFootstepsWaterSingle;
                case FootstepSurfaceType.Metal:
                    return isLargeGroup ? groupFootstepsMetalGroup : groupFootstepsMetalSingle;
                case FootstepSurfaceType.Wood:
                    return isLargeGroup ? groupFootstepsWoodGroup : groupFootstepsWoodSingle;
                case FootstepSurfaceType.Sand:
                    return isLargeGroup ? groupFootstepsSandGroup : groupFootstepsSandSingle;
                case FootstepSurfaceType.Snow:
                    return isLargeGroup ? groupFootstepsSnowGroup : groupFootstepsSnowSingle;
                case FootstepSurfaceType.Grass:
                default:
                    return isLargeGroup ? footstepSoundsGroup : footstepSoundsSingle;
            }
        }
        
        /// <summary>
        /// Returns true if group footstep audio is configured and enabled.
        /// </summary>
        public bool HasGroupFootstepAudio => useGroupFootstepAudio && 
            ((footstepSoundsSingle != null && footstepSoundsSingle.Length > 0) ||
             (footstepSoundsGroup != null && footstepSoundsGroup.Length > 0));
        
        /// <summary>
        /// Gets a random selection sound based on the number of selected agents.
        /// Returns single voice for small groups, army sounds for large groups.
        /// </summary>
        /// <param name="selectedCount">Number of agents being selected</param>
        /// <returns>Random selection sound clip, or null if none assigned</returns>
        public AudioClip GetRandomSelectionSound(int selectedCount)
        {
            AudioClip[] sounds = selectedCount >= groupSelectionThreshold ? selectionSoundsGroup : selectionSoundsSingle;
            if (sounds == null || sounds.Length == 0)
                return null;
            return sounds[Random.Range(0, sounds.Length)];
        }
        
        /// <summary>
        /// Gets a random command sound based on the number of commanded agents.
        /// Returns single voice for small groups, army sounds for large groups.
        /// </summary>
        /// <param name="commandedCount">Number of agents being commanded</param>
        /// <returns>Random command sound clip, or null if none assigned</returns>
        public AudioClip GetRandomCommandSound(int commandedCount)
        {
            AudioClip[] sounds = commandedCount >= groupCommandThreshold ? commandSoundsGroup : commandSoundsSingle;
            if (sounds == null || sounds.Length == 0)
                return null;
            return sounds[Random.Range(0, sounds.Length)];
        }
        
        /// <summary>
        /// Applies the current combat behavior template, auto-configuring all related settings.
        /// Call this after changing combatBehaviorTemplate to apply the new settings.
        /// </summary>
        public void ApplyCombatTemplate()
        {
            if (combatBehaviorTemplate != CombatBehaviorTemplate.Custom)
            {
                CombatBehaviorTemplateHelper.ApplyTemplate(this, combatBehaviorTemplate);
            }
        }
        
        /// <summary>
        /// Gets a description of the current combat behavior template.
        /// </summary>
        public string GetCombatTemplateDescription()
        {
            return CombatBehaviorTemplateHelper.GetTemplateDescription(combatBehaviorTemplate);
        }
        
        // Track the last applied template to detect changes
        [System.NonSerialized]
        private CombatBehaviorTemplate _lastAppliedTemplate = CombatBehaviorTemplate.Custom;
        
        /// <summary>
        /// Called when the asset is loaded or values change in the inspector.
        /// Auto-applies template when it changes (except for Custom mode).
        /// </summary>
        private void OnEnable()
        {
            EnsureSplitDamageInitialized();
            _lastAppliedTemplate = combatBehaviorTemplate;
        }

        private void EnsureSplitDamageInitialized()
        {
            if (!_splitDamageInitialized)
            {
                float migratedRangedDamage = meleeAttackDamage;
                if (rangedAttackMode == RangedAttackMode.Projectile || rangedAttackMode == RangedAttackMode.ProjectileArc)
                {
                    migratedRangedDamage = projectileConfig != null ? projectileConfig.damage : meleeAttackDamage;
                }

                rangedAttackDamage = Mathf.Max(0f, migratedRangedDamage);
                _splitDamageInitialized = true;
            }

            if (meleeAttackDamage < 0f)
                meleeAttackDamage = 0f;
            if (rangedAttackDamage < 0f)
                rangedAttackDamage = 0f;
        }
    }
}
