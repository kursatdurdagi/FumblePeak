namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Defines the fundamental movement and body type of agents in an EnemyCrowd.
    /// Controls which animation clips are expected, how navigation works, and hitbox defaults.
    /// </summary>
    public enum AgentArchetype
    {
        /// <summary>Standard bipedal characters — soldiers, zombies, civilians. Ground-based navigation with full locomotion animation set.</summary>
        Humanoid,
        /// <summary>Humanoid agents that can fly persistently — angels, jetpack troops. Ground nav with flight height offset; can optionally land.</summary>
        FlyingHumanoid,
        /// <summary>Non-humanoid flying/hovering entities — spaceships, drones, gunships. 3D volumetric navigation, no locomotion animations expected.</summary>
        FlyingVehicle,
        /// <summary>Non-humanoid ground entities — tanks, siege engines, mechs. Ground-based navigation but no locomotion animations expected.</summary>
        GroundVehicle
    }

    /// <summary>
    /// Navigation mode for 3D (volumetric) movement used by flying vehicles and persistent flyers.
    /// </summary>
    public enum NavigationType3D
    {
        /// <summary>Simple boids-like steering with separation, alignment, cohesion, and obstacle avoidance. Lightweight and suitable for swarms.</summary>
        Boids,
        /// <summary>Waypoint-based 3D steering. Agents follow a sequence of 3D waypoints with smooth interpolation.</summary>
        Waypoint3D,
        /// <summary>Direct steering toward destination with altitude control. Simplest option — no inter-agent avoidance.</summary>
        DirectSteering
    }

    /// <summary>
    /// Flags controlling which rotation axes are locked (prevented from changing) on flying vehicles.
    /// Combine with bitwise OR to lock multiple axes.
    /// </summary>
    [System.Flags]
    public enum RotationAxisLock
    {
        /// <summary>No axes locked — full pitch, yaw, and bank.</summary>
        None = 0,
        /// <summary>Lock X axis — prevents pitch (nose-up / nose-down).</summary>
        LockPitch = 1 << 0,
        /// <summary>Lock Y axis — prevents yaw (turning left / right).</summary>
        LockYaw = 1 << 1,
        /// <summary>Lock Z axis — prevents bank/roll.</summary>
        LockRoll = 1 << 2
    }

    /// <summary>
    /// Preset configurations for virtual hitbox zones.
    /// </summary>
    public enum HitboxPreset
    {
        /// <summary>Standard head/torso/legs zones for humanoid characters.</summary>
        Humanoid,
        /// <summary>Single center-mass sphere for vehicles, drones, and non-humanoid entities.</summary>
        Vehicle,
        /// <summary>User-defined zones — the wizard/inspector won't override hitZones array.</summary>
        Custom
    }

    /// <summary>
    /// Animation state of a crowd agent
    /// </summary>
    public enum CrowdAnimationState
    {
        Idle,
        Locomotion,
        Falling,
        Landing,
        Flying,
        FlightTakeOff,
        FlightLanding
    }

    /// <summary>
    /// Combat state of a crowd agent
    /// </summary>
    public enum CrowdCombatState
    {
        Idle,
        Approach,
        Attacking,
        Blocking,
        Hit,
        Dead,
        Charging  // Special ability: Charge attack in progress
    }

    /// <summary>
    /// Determines which system currently controls an agent's navigation.
    /// </summary>
    public enum AgentNavigationState
    {
        Formation,       // Formation/Move-to-point logic controls the NavMeshAgent
        ChasingTarget,   // Attack target chase logic controls the NavMeshAgent
        CombatOverride,  // Combat positioning system owns the NavMeshAgent
        ManualCommand    // RTS-style manual command - agent follows player-issued move order
    }

    /// <summary>
    /// Animator workflow override options for crowd instances
    /// </summary>
    public enum AnimatorWorkflowOverride
    {
        KeepPrefabSetting,
        MecanimReader,
        ComputeAnimator,
        BoneTracker
    }

    /// <summary>
    /// Blend mode for locomotion animation control.
    /// OneD: single float (Speed). TwoD: planar X/Z floats for 2D blend trees.
    /// </summary>
    public enum LocomotionBlendMode
    {
        OneD = 0,
        TwoD = 1
    }

    /// <summary>
    /// Formation types for crowd spawning
    /// </summary>
    public enum FormationType
    {
        Random,
        Square,
        Circle,
        Triangle,
        Rectangle,
        HollowSquare,
        HollowCircle,
        HollowTriangle,
        HollowRectangle
    }

    /// <summary>
    /// Hybrid workflow types for performance optimization
    /// </summary>
    public enum HybridWorkflowType
    {
        ComputeAnimator,
        MecanimReader,
        BoneTracker
    }

    /// <summary>
    /// Dead agent cleanup modes
    /// </summary>
    public enum DeadAgentCleanupMode
    {
        None,       // Leave dead agents in scene
        Disable,    // Disable GameObjects
        Destroy,    // Destroy GameObjects
        Pool        // Recycle for reuse
    }

    /// <summary>
    /// Controls how multi-faction selections form up when given a move command.
    /// </summary>
    public enum FormationControlMode
    {
        /// <summary>
        /// All selected agents form one combined formation regardless of faction.
        /// This is the default behaviour.
        /// </summary>
        CombinedFormation,
        
        /// <summary>
        /// Each EnemyCrowd type keeps its own formation (using its EnemyCrowd.formationType).
        /// The individual formations are arranged side by side in a battle line and advance together,
        /// similar to Total War style games.
        /// </summary>
        BattleLine
    }

    /// <summary>
    /// Controls whether formation spacing is shared globally or tracked per EnemyCrowd.
    /// </summary>
    public enum FormationSpacingMode
    {
        /// <summary>
        /// All agents share a single global formation spacing value.
        /// </summary>
        Global,

        /// <summary>
        /// Each EnemyCrowd type can have its own formation spacing, adjustable at runtime.
        /// </summary>
        PerCrowd
    }

    /// <summary>
    /// Controls how the portrait for an EnemyCrowd is sourced.
    /// </summary>
    public enum PortraitMode
    {
        /// <summary>
        /// User manually assigns a portrait texture in the inspector.
        /// </summary>
        Manual,

        /// <summary>
        /// Portrait is auto-generated in the editor from the referenced prefab.
        /// Humanoid models capture head/neck/shoulders; non-humanoid captures the full model.
        /// </summary>
        AutoGenerate
    }

    /// <summary>
    /// Faction battle mode types for debug/testing
    /// </summary>
    public enum FactionBattleType
    {
        /// <summary>
        /// All agents fight against all other agents regardless of faction (battle royale)
        /// </summary>
        AllVsAll,
        
        /// <summary>
        /// Agents only attack agents from different factions (team-based)
        /// </summary>
        FactionVsFaction
    }

    /// <summary>
    /// Combat stance that controls how agents engage enemies.
    /// Works with Combat Behavior Templates, Ally Defense, and Formation systems.
    /// </summary>
    public enum CombatStance
    {
        /// <summary>
        /// Offensive - Default behavior. Agents actively seek and attack enemies based on aggro,
        /// charge forward, and pursue targets. Standard RTS attack mode.
        /// </summary>
        Offensive,

        /// <summary>
        /// Defensive - Agents don't initiate combat or chase enemies. They ignore aggro and
        /// only attack if they are attacked first. Good for holding formations without engaging.
        /// </summary>
        Defensive,

        /// <summary>
        /// HoldPosition - Agents stay at their current position and attack enemies within range
        /// but never chase or move from their spot. Ranged units fire at targets in range,
        /// melee units attack enemies that enter melee range. Ideal for guarding locations.
        /// </summary>
        HoldPosition,

        /// <summary>
        /// Passive - Agents will not engage in combat under any circumstances. They follow move commands,
        /// climb walls, and navigate normally, but have zero aggro range and will not attack even when
        /// attacked. They will only attack if given an explicit attack command. Useful for civilians,
        /// escorts, or units you want to keep completely out of combat.
        /// </summary>
        Passive
    }
}
