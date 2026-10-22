using UnityEngine;
using System;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Civil
{
    /// <summary>
    /// Type of waypoint behavior.
    /// </summary>
    public enum CivilWaypointType
    {
        /// <summary>Standard path waypoint - agents navigate through with optional channel width variance.</summary>
        Path,
        
        /// <summary>Exploration zone - agents wander freely within radius before continuing.</summary>
        Explore,
        
        /// <summary>Despawn point - agents fade out and return to pool.</summary>
        Despawn,
        
        /// <summary>Activity point - agents perform animations/activities with optional props.</summary>
        Activity,

        /// <summary>Lean point - agents align to a marker and play a configured interaction animation.</summary>
        Lean,

        /// <summary>Sit point - agents sit at a marker and play a configured interaction animation.</summary>
        Sit,
        
        /// <summary>Spawn point - new agents can appear here (fading in).</summary>
        Spawn
    }
    
    /// <summary>
    /// How spawn points generate positions for new agents.
    /// </summary>
    public enum CivilSpawnMode
    {
        /// <summary>Spawn within a fixed radius around the waypoint position.</summary>
        FixedRadius,
        
        /// <summary>Spawn just outside the camera frustum, so agents walk into view naturally.</summary>
        CameraFrustumEdge
    }
    
    /// <summary>
    /// How agents select their next waypoint.
    /// </summary>
    public enum WaypointSelectionMode
    {
        /// <summary>Weighted random based on connection weights.</summary>
        WeightedRandom,
        
        /// <summary>Purely random selection.</summary>
        Random,
        
        /// <summary>Sequential through connected waypoints.</summary>
        Sequential,
        
        /// <summary>Prefer waypoints with fewer agents nearby.</summary>
        LeastCrowded
    }
    
    /// <summary>
    /// Navigation mode for civil agents - how they use waypoints.
    /// </summary>
    public enum CivilNavigationMode
    {
        /// <summary>Follow waypoint connections (traditional graph-based navigation).</summary>
        FollowConnections,
        
        /// <summary>Ignore connections, walk toward any waypoint using NavMesh pathfinding.</summary>
        FreeRoam
    }
    
    /// <summary>
    /// Civil agent movement style.
    /// </summary>
    public enum CivilMovementStyle
    {
        /// <summary>Normal walking pace.</summary>
        Walk,
        
        /// <summary>Casual strolling (slower).</summary>
        Stroll,
        
        /// <summary>Hurried walking (faster).</summary>
        Hurried,
        
        /// <summary>Random variation between styles.</summary>
        Varied
    }
    
    /// <summary>
    /// Spawn interval mode for civil spawn points.
    /// </summary>
    public enum SpawnIntervalMode
    {
        /// <summary>Fixed interval between spawns.</summary>
        Fixed,
        
        /// <summary>Random interval between min and max values.</summary>
        RandomRange
    }
    
    /// <summary>
    /// Side preference for lane formation in bidirectional traffic.
    /// </summary>
    public enum LaneSide
    {
        /// <summary>Pass on the right side (like US/Europe traffic).</summary>
        Right,
        
        /// <summary>Pass on the left side (like UK/Japan traffic).</summary>
        Left
    }
    
    /// <summary>
    /// Configuration for spawning a specific faction at a spawn waypoint.
    /// </summary>
    [Serializable]
    public class CivilSpawnEntry
    {
        [Tooltip("Index of the EnemyCrowd in EnemyMassesCrowdController.EnemyCrowds list. -1 = use any civil-enabled faction.")]
        public int factionIndex = -1;
        
        [Tooltip("Selection weight when multiple factions can spawn (higher = more likely).")]
        [Range(0.1f, 10f)]
        public float weight = 1f;
        
        [Tooltip("How spawn timing is determined.")]
        public SpawnIntervalMode intervalMode = SpawnIntervalMode.RandomRange;
        
        [Tooltip("Fixed interval in seconds between spawns (used when intervalMode = Fixed).")]
        [Min(0.1f)]
        public float fixedInterval = 10f;
        
        [Tooltip("Minimum interval in seconds between spawns (used when intervalMode = RandomRange).")]
        [Min(0.1f)]
        public float minInterval = 5f;
        
        [Tooltip("Maximum interval in seconds between spawns (used when intervalMode = RandomRange).")]
        [Min(0.1f)]
        public float maxInterval = 15f;
        
        [Tooltip("Number of agents to spawn each time (batch spawning).")]
        [Min(1)]
        public int spawnCount = 1;
        
        [Tooltip("Maximum total agents this entry can have alive at once (0 = unlimited).")]
        [Min(0)]
        public int maxAliveAgents = 0;
        
        [Tooltip("If true, this spawn entry is currently active.")]
        public bool enabled = true;
        
        // Runtime tracking
        [System.NonSerialized]
        public float lastSpawnTime;
        [System.NonSerialized]
        public float nextSpawnTime;
        [System.NonSerialized]
        public int currentAliveCount;
        
        /// <summary>
        /// Gets the next spawn interval based on the interval mode.
        /// </summary>
        public float GetNextInterval()
        {
            return intervalMode == SpawnIntervalMode.Fixed 
                ? fixedInterval 
                : UnityEngine.Random.Range(minInterval, maxInterval);
        }
        
        /// <summary>
        /// Checks if this entry can spawn (respects timing and max alive limits).
        /// </summary>
        public bool CanSpawn(float currentTime)
        {
            if (!enabled)
                return false;
            
            if (currentTime < nextSpawnTime)
                return false;
            
            if (maxAliveAgents > 0 && currentAliveCount >= maxAliveAgents)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Called when an agent from this entry spawns.
        /// </summary>
        public void OnSpawned(float currentTime)
        {
            lastSpawnTime = currentTime;
            nextSpawnTime = currentTime + GetNextInterval();
            currentAliveCount++;
        }
        
        /// <summary>
        /// Called when an agent from this entry despawns/dies.
        /// </summary>
        public void OnDespawned()
        {
            currentAliveCount = Mathf.Max(0, currentAliveCount - 1);
        }
    }
    
    /// <summary>
    /// Connection between two waypoints with navigation settings.
    /// </summary>
    [Serializable]
    public class CivilWaypointConnection
    {
        [Tooltip("Target waypoint this connection leads to.")]
        public CivilWaypointNode target;
        
        [Tooltip("Selection weight (higher = more likely to be chosen).")]
        [Range(0.1f, 10f)]
        public float weight = 1f;
        
        [Tooltip("Width of the path corridor. Agents offset randomly within this width.")]
        [Range(0f, 10f)]
        public float channelWidth = 2f;
        
        [Tooltip("If true, connection is bidirectional (auto-creates reverse).")]
        public bool bidirectional = true;
        
        [Tooltip("Optional: Required tags for agents to use this connection.")]
        public string[] requiredTags;
    }
    
    /// <summary>
    /// Configuration for an activity that agents can perform at Activity waypoints.
    /// </summary>
    [Serializable]
    public class CivilActivityDefinition
    {
        [Tooltip("Unique identifier for this activity.")]
        public string activityId;
        
        [Tooltip("Display name for the activity.")]
        public string displayName;
        
        [Tooltip("Animation clip to play during activity.")]
        public AnimationClip animationClip;
        
        [Tooltip("If true, animation loops until duration expires.")]
        public bool loopAnimation = true;
        
        [Tooltip("Minimum time to perform this activity.")]
        [Min(0.1f)]
        public float minDuration = 5f;
        
        [Tooltip("Maximum time to perform this activity.")]
        [Min(0.1f)]
        public float maxDuration = 30f;
        
        [Tooltip("Props to attach to agent bones during activity.")]
        public CivilPropAttachment[] propAttachments;
        
        [Tooltip("Selection weight when multiple activities available.")]
        [Range(0.1f, 10f)]
        public float selectionWeight = 1f;
        
        [Tooltip("Speed multiplier while approaching activity point (0 = stop at point).")]
        [Range(0f, 1f)]
        public float approachSpeedMultiplier = 0.5f;
        
        /// <summary>
        /// Event fired when activity starts.
        /// </summary>
        public event Action<int> OnActivityStarted;
        
        /// <summary>
        /// Event fired when activity ends.
        /// </summary>
        public event Action<int> OnActivityEnded;
        
        /// <summary>
        /// Event fired at specific normalized time during activity (0-1).
        /// </summary>
        public event Action<int, float> OnActivityProgress;
        
        internal void FireStarted(int agentIndex) => OnActivityStarted?.Invoke(agentIndex);
        internal void FireEnded(int agentIndex) => OnActivityEnded?.Invoke(agentIndex);
        internal void FireProgress(int agentIndex, float normalizedTime) => OnActivityProgress?.Invoke(agentIndex, normalizedTime);
    }

    /// <summary>
    /// A lean spot that agents can occupy at a Lean waypoint.
    /// </summary>
    [Serializable]
    public class CivilLeanSpot
    {
        [Tooltip("Marker transform agents align to when leaning. Position and rotation drive placement.")]
        public Transform marker;

        [Tooltip("Interaction Definition ID from the EnemyCrowd civil settings to play while leaning. If empty, the first interaction is used.")]
        public string interactionId = "lean";

        [Tooltip("How long the agent leans (seconds). 0 = use default of 10 seconds.")]
        [Min(0f)]
        public float duration = 10f;

        [System.NonSerialized]
        public int occupantAgentIndex = -1;

        /// <summary>True if a civil agent currently owns this spot.</summary>
        public bool IsOccupied => occupantAgentIndex >= 0;

        internal void Release(int agentIndex)
        {
            if (occupantAgentIndex == agentIndex)
            {
                occupantAgentIndex = -1;
            }
        }
    }

    /// <summary>
    /// A sit spot that agents can occupy at a Sit waypoint.
    /// </summary>
    [Serializable]
    public class CivilSitSpot
    {
        [Tooltip("Marker transform agents align to when sitting. Position and rotation drive placement.")]
        public Transform marker;

        [Tooltip("Interaction Definition ID from the EnemyCrowd civil settings to play while sitting. If empty, the first interaction is used.")]
        public string interactionId = "sit";

        [Tooltip("How long the agent sits (seconds). 0 = use default of 10 seconds.")]
        [Min(0f)]
        public float duration = 10f;

        [System.NonSerialized]
        public int occupantAgentIndex = -1;

        /// <summary>True if a civil agent currently owns this spot.</summary>
        public bool IsOccupied => occupantAgentIndex >= 0;

        internal void Release(int agentIndex)
        {
            if (occupantAgentIndex == agentIndex)
            {
                occupantAgentIndex = -1;
            }
        }
    }
    
    /// <summary>
    /// Configuration for attaching a prop to an agent's bone during an activity.
    /// </summary>
    [Serializable]
    public class CivilPropAttachment
    {
        [Tooltip("Prefab to instantiate and attach.")]
        public GameObject propPrefab;
        
        [Tooltip("Name of the bone to attach to (e.g., 'RightHand', 'Head').")]
        public string boneName;
        
        [Tooltip("Alternative: Full bone path from root (e.g., 'Armature/Hips/Spine/RightArm/RightHand').")]
        public string bonePath;
        
        [Tooltip("Local position offset from bone.")]
        public Vector3 positionOffset;
        
        [Tooltip("Local rotation offset from bone.")]
        public Vector3 rotationOffset;
        
        [Tooltip("Local scale for the prop.")]
        public Vector3 scale = Vector3.one;
        
        [Tooltip("Delay before showing prop (normalized time 0-1 of activity).")]
        [Range(0f, 1f)]
        public float showDelay = 0f;
        
        [Tooltip("When to hide prop (normalized time 0-1, 1 = end of activity).")]
        [Range(0f, 1f)]
        public float hideTime = 1f;
    }
    
    /// <summary>
    /// Settings for civil agent behavior, stored in EnemyCrowd ScriptableObject.
    /// </summary>
    [Serializable]
    public class CivilBehaviorSettings
    {
        [Header("Mode")]
        [Tooltip("Enable civil/NPC navigation mode for this faction.")]
        public bool enableCivilMode = false;
        
        [Tooltip("If true, disables combat system for civil agents.")]
        public bool disableCombat = true;
        
        [Header("Navigation")]
        [Tooltip("Navigation mode: FollowConnections uses waypoint graph, FreeRoam ignores connections and walks to any waypoint via NavMesh.")]
        public CivilNavigationMode navigationMode = CivilNavigationMode.FollowConnections;
        
        [Tooltip("How agents select their next waypoint (used with FollowConnections mode).")]
        public WaypointSelectionMode selectionMode = WaypointSelectionMode.WeightedRandom;
        
        [Tooltip("Default movement style for agents.")]
        public CivilMovementStyle movementStyle = CivilMovementStyle.Walk;
        
        [Tooltip("Speed multiplier for walking.")]
        [Range(0.1f, 2f)]
        public float walkSpeedMultiplier = 0.6f;
        
        [Tooltip("Speed multiplier for strolling.")]
        [Range(0.1f, 2f)]
        public float strollSpeedMultiplier = 0.35f;
        
        [Tooltip("Speed multiplier for hurried movement.")]
        [Range(0.1f, 2f)]
        public float hurriedSpeedMultiplier = 0.9f;
        
        [Tooltip("How long agents wait at path waypoints before continuing (randomized). Set to (0,0) for continuous flow without stopping.")]
        public Vector2 pathWaitTime = new Vector2(0f, 0f);
        
        [Header("Free Roam Settings")]
        [Tooltip("(FreeRoam mode) Maximum distance to search for next waypoint. 0 = unlimited.")]
        [Min(0f)]
        public float freeRoamMaxDistance = 0f;
        
        [Tooltip("(FreeRoam mode) Prefer waypoints closer to the agent.")]
        public bool freeRoamPreferCloser = false;
        
        [Tooltip("(FreeRoam mode) Avoid revisiting recent waypoints (number to remember).")]
        [Range(0, 10)]
        public int freeRoamRecentMemory = 3;
        
        [Tooltip("(FreeRoam mode) Chance to pick a Despawn waypoint when available (0-1).")]
        [Range(0f, 1f)]
        public float freeRoamDespawnBias = 0.15f;
        
        [Header("Exploration")]
        [Tooltip("How long agents explore at Explore waypoints before leaving.")]
        public Vector2 exploreTime = new Vector2(10f, 60f);
        
        [Tooltip("Time between picking new exploration targets.")]
        public Vector2 exploreRetargetInterval = new Vector2(3f, 8f);
        
        [Header("Activities")]
        [Tooltip("Chance (0-1) to perform an activity when passing an Activity waypoint.")]
        [Range(0f, 1f)]
        public float activityChance = 0.5f;
        
        [Tooltip("Cooldown after completing an activity before agent will do another.")]
        [Min(0f)]
        public float activityCooldown = 30f;
        
        [Header("Spawning & Despawning")]
        [Tooltip("Duration of fade in/out effect for spawn/despawn.")]
        [Range(0.1f, 5f)]
        public float fadeTransitionDuration = 1.5f;
        
        [Tooltip("Chance to choose a despawn waypoint when available.")]
        [Range(0f, 1f)]
        public float despawnChance = 0.1f;
        
        [Tooltip("Minimum time agent must exist before considering despawn.")]
        [Min(0f)]
        public float minLifetimeBeforeDespawn = 60f;
        
        [Header("Avoidance")]
        [Tooltip("Agents try to maintain this distance from each other.")]
        [Range(0.5f, 5f)]
        public float personalSpace = 1.2f;
        
        [Tooltip("How strongly agents avoid each other (0 = ignore, 1 = strong avoidance).")]
        [Range(0f, 1f)]
        public float avoidanceStrength = 0.7f;
        
        [Header("Performance")]
        [Tooltip("Maximum waypoint decisions per frame across all civil agents.")]
        [Min(1)]
        public int maxDecisionsPerFrame = 100;
        
        [Tooltip("Maximum activity state updates per frame.")]
        [Min(1)]
        public int maxActivityUpdatesPerFrame = 50;
        
        [Tooltip("Update interval for distant agents (seconds).")]
        [Range(0.1f, 2f)]
        public float distantUpdateInterval = 0.5f;
        
        [Header("Path Divergence")]
        [Tooltip("Amount of natural path variation (0 = direct paths, 1 = highly organic/wandering paths).")]
        [Range(0f, 1f)]
        public float pathDivergence = 0.3f;
        
        [Tooltip("Frequency of path variation points (lower = smoother curves, higher = more erratic).")]
        [Range(0.1f, 5f)]
        public float divergenceFrequency = 1f;
        
        [Tooltip("Maximum lateral offset from direct path (in meters).")]
        [Range(0.5f, 10f)]
        public float maxDivergenceOffset = 3f;
        
        [Tooltip("How smoothly agents adjust to divergence points.")]
        [Range(0.1f, 2f)]
        public float divergenceSmoothness = 0.5f;
        
        [Tooltip("Enable occasional stops to look around.")]
        public bool enableLookAroundStops = true;
        
        [Tooltip("Chance to stop and look around when divergence is high (0-1).")]
        [Range(0f, 1f)]
        public float lookAroundChance = 0.1f;
        
        [Tooltip("Duration of look-around stops.")]
        public Vector2 lookAroundDuration = new Vector2(1f, 3f);
        
        [Header("Corner Smoothing")]
        [Tooltip("Enable smooth arcs around sharp corners instead of robotic turns.")]
        public bool enableCornerSmoothing = true;
        
        [Tooltip("Minimum angle (degrees) between waypoints to trigger corner smoothing. Lower = more smoothing.")]
        [Range(15f, 90f)]
        public float cornerSmoothingAngleThreshold = 45f;
        
        [Tooltip("Radius of the arc when rounding corners (meters). Larger = wider, more natural turns.")]
        [Range(1f, 15f)]
        public float cornerSmoothingRadius = 3f;
        
        [Tooltip("Number of intermediate points to create along the smoothed arc. More = smoother curve.")]
        [Range(4, 12)]
        public int cornerSmoothingSegments = 8;
        
        [Tooltip("How early before reaching waypoint to start the corner arc (multiplier of radius).")]
        [Range(0.2f, 0.8f)]
        public float cornerApproachFactor = 0.5f;
        
        [Header("Crowd Flow & Congestion")]
        [Tooltip("Enable dynamic crowd flow management to prevent congestion.")]
        public bool enableCrowdFlow = true;
        
        [Tooltip("Radius to check for nearby agents when calculating density.")]
        [Range(1f, 10f)]
        public float densityCheckRadius = 4f;
        
        [Tooltip("Number of nearby agents before congestion triggers (agent count in radius).")]
        [Range(2, 20)]
        public int congestionThreshold = 5;
        
        [Tooltip("Minimum speed multiplier when in high density (0.2 = 20% of normal speed).")]
        [Range(0.1f, 0.8f)]
        public float minCongestionSpeedMult = 0.3f;
        
        [Tooltip("Enable agents to find alternate paths when congested.")]
        public bool enableDynamicRerouting = true;
        
        [Tooltip("How strongly agents avoid crowded areas (higher = more avoidance).")]
        [Range(0f, 2f)]
        public float crowdAvoidanceStrength = 1f;
        
        [Tooltip("Enable natural lane formation in bidirectional traffic.")]
        public bool enableLaneFormation = true;
        
        [Tooltip("Side preference for lane formation (affects which side agents pass each other).")]
        public LaneSide lanePreference = LaneSide.Right;
        
        [Header("Social Interactions")]
        [Tooltip("Settings for social interactions between civil agents.")]
        public SocialInteractionSettings socialSettings = new SocialInteractionSettings();
    }
    
    /// <summary>
    /// Per-agent state for civil navigation.
    /// </summary>
    [Serializable]
    public class CivilAgentState
    {
        /// <summary>Current navigation state.</summary>
        public CivilNavigationState navigationState;
        
        /// <summary>Current waypoint the agent is navigating to.</summary>
        public CivilWaypointNode currentTarget;
        
        /// <summary>Previous waypoint (where agent came from).</summary>
        public CivilWaypointNode previousWaypoint;
        
        /// <summary>Actual world position target (includes channel offset).</summary>
        public Vector3 targetPosition;
        
        /// <summary>Current movement style for this agent.</summary>
        public CivilMovementStyle currentMovementStyle;
        
        /// <summary>Time when agent reached current waypoint.</summary>
        public float arrivalTime;
        
        /// <summary>How long to wait at current location.</summary>
        public float waitDuration;
        
        /// <summary>Time when current activity started.</summary>
        public float activityStartTime;
        
        /// <summary>Duration of current activity.</summary>
        public float activityDuration;
        
        /// <summary>Current activity definition (if performing one).</summary>
        public CivilActivityDefinition currentActivity;
        
        /// <summary>Active prop instances for current activity.</summary>
        public List<GameObject> activeProps;
        
        /// <summary>Time of last activity completion (for cooldown).</summary>
        public float lastActivityEndTime;
        
        /// <summary>Fade progress (0 = invisible, 1 = fully visible).</summary>
        public float fadeProgress = 1f;
        
        /// <summary>Is agent currently fading in or out?</summary>
        public bool isFading;
        
        /// <summary>Fade direction (1 = fading in, -1 = fading out).</summary>
        public int fadeDirection;
        
        /// <summary>Time when exploration at current waypoint started.</summary>
        public float exploreStartTime;
        
        /// <summary>Next time to pick a new exploration target.</summary>
        public float nextExploreRetargetTime;
        
        /// <summary>Total time agent has existed (for despawn check).</summary>
        public float lifetime;
        
        /// <summary>Original animator workflow before activity switch.</summary>
        public HybridWorkflowType originalWorkflow;
        
        /// <summary>Last time this agent's state was updated.</summary>
        public float lastUpdateTime;
        
        /// <summary>The waypoint where this agent spawned (for despawn tracking).</summary>
        public CivilWaypointNode spawnWaypoint;
        
        /// <summary>The spawn entry that created this agent (for despawn tracking).</summary>
        public CivilSpawnEntry spawnEntry;
        
        /// <summary>Was this agent spawned outside the camera frustum (and started walking immediately)?</summary>
        public bool spawnedOutsideFrustum;
        
        // Free Roam State
        /// <summary>Recent waypoints visited (for FreeRoam mode to avoid repetition).</summary>
        public List<CivilWaypointNode> recentWaypoints;
        
        // Path Divergence State
        /// <summary>Current divergence offset being applied.</summary>
        public Vector3 currentDivergenceOffset;
        
        /// <summary>Target divergence offset to interpolate toward.</summary>
        public Vector3 targetDivergenceOffset;
        
        /// <summary>Next time to recalculate divergence offset.</summary>
        public float nextDivergenceTime;
        
        /// <summary>Random seed for consistent divergence per agent.</summary>
        public float divergenceSeed;
        
        /// <summary>Is agent currently in a look-around stop?</summary>
        public bool isLookingAround;
        
        /// <summary>Time when look-around started.</summary>
        public float lookAroundStartTime;
        
        /// <summary>Duration of current look-around.</summary>
        public float lookAroundDuration;
        
        /// <summary>Direction agent is looking during look-around.</summary>
        public Vector3 lookAroundDirection;
        
        // Social Interaction State
        /// <summary>Social interaction state for this agent.</summary>
        public AgentSocialState socialState;
        
        // Corner Smoothing State
        /// <summary>Is agent currently navigating a smoothed corner arc?</summary>
        public bool isCornerSmoothing;
        
        /// <summary>Has corner smoothing failed for the current waypoint? (prevents repeated attempts)</summary>
        public bool cornerSmoothingFailed;
        
        /// <summary>Arc points for the current corner being navigated.</summary>
        public Vector3[] cornerArcPoints;
        
        /// <summary>Current index in the corner arc points.</summary>
        public int currentArcIndex;
        
        /// <summary>The waypoint at the corner (to skip to after arc).</summary>
        public CivilWaypointNode cornerWaypoint;
        
        /// <summary>Position where corner smoothing started.</summary>
        public Vector3 cornerStartPosition;
        
        /// <summary>Final position after corner (toward next waypoint).</summary>
        public Vector3 cornerEndPosition;
        
        /// <summary>The next waypoint after the corner waypoint.</summary>
        public CivilWaypointNode postCornerWaypoint;
        
        /// <summary>Interpolation progress along the arc (0-1).</summary>
        public float arcProgress;
        
        /// <summary>Current velocity for smooth arc steering.</summary>
        public Vector3 arcVelocity;
        
        // Crowd Flow State
        /// <summary>Current local density around this agent.</summary>
        public float localDensity;
        
        /// <summary>Current speed multiplier from congestion.</summary>
        public float congestionSpeedMult;
        
        /// <summary>Accumulated avoidance offset for crowd steering.</summary>
        public Vector3 crowdAvoidanceOffset;
        
        /// <summary>Last time density was calculated.</summary>
        public float lastDensityCheckTime;
        
        /// <summary>Is agent currently yielding to others?</summary>
        public bool isYielding;
        
        /// <summary>Time when yielding started.</summary>
        public float yieldStartTime;
        
        /// <summary>Base speed before congestion adjustments.</summary>
        public float baseSpeed;

        // Lean State
        /// <summary>Current lean interaction definition being played.</summary>
        public SocialInteractionDefinition currentLeanInteraction;
        
        /// <summary>Index of the lean spot this agent reserved.</summary>
        public int currentLeanSpotIndex = -1;
        
        /// <summary>Time when the current lean should end.</summary>
        public float leanEndTime;

        // Sit State
        /// <summary>Current sit interaction definition being played.</summary>
        public SocialInteractionDefinition currentSitInteraction;

        /// <summary>Index of the sit spot this agent reserved.</summary>
        public int currentSitSpotIndex = -1;

        /// <summary>Time when the current sit should end.</summary>
        public float sitEndTime;
    }
    
    /// <summary>
    /// Navigation state machine states.
    /// </summary>
    public enum CivilNavigationState
    {
        /// <summary>Agent is idle, waiting for a destination.</summary>
        Idle,
        
        /// <summary>Agent is walking toward a waypoint.</summary>
        Walking,
        
        /// <summary>Agent has arrived and is waiting before continuing.</summary>
        Waiting,
        
        /// <summary>Agent is exploring freely within an area.</summary>
        Exploring,
        
        /// <summary>Agent is performing an activity.</summary>
        PerformingActivity,

        /// <summary>Agent is leaning at a marker-driven lean spot.</summary>
        Leaning,

        /// <summary>Agent is sitting at a marker-driven sit spot.</summary>
        Sitting,
        
        /// <summary>Agent is participating in a social interaction.</summary>
        SocialInteraction,
        
        /// <summary>Agent has stopped to look around.</summary>
        LookingAround,
        
        /// <summary>Agent is fading in (spawning).</summary>
        FadingIn,
        
        /// <summary>Agent is fading out (despawning).</summary>
        FadingOut,
        
        /// <summary>Agent is pooled/inactive.</summary>
        Pooled
    }
}
