using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Spawns a crowd and keeps its spawn groups patrolling. Supports free-roam radius or waypoints.
    /// </summary>
    public class EnemyMassesSpawnPatrol : MonoBehaviour
    {
        public enum PatrolMode
        {
            FreeRoam,
            Waypoints
        }

        public enum SpawnMode
        {
            StartOnStart,
            Interval,
            Manual
        }

        [Header("Spawn Settings")]
        [Tooltip("The crowd controller that will spawn agents")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Which enemy crowd faction to spawn (index in Enemy Crowds list)")]
        public int enemyCrowdIndex = 0;

        [Tooltip("Number of agents to spawn per wave")]
        [Min(1)]
        public int spawnCount = 50;

        [Tooltip("Formation type for spawned agents")]
        public FormationType spawnFormation = FormationType.Circle;

        [Tooltip("Spacing override for this spawn. 0 = use faction default; non-zero overrides (abs value used)")]
        public float spawnFormationSpacing = 0f;

        [Tooltip("If enabled, the spawner's formation overrides the EnemyCrowd Override Formation setting.")]
        public bool forceSpawnerFormation = false;

        [Header("Spawn Timing")]
        public SpawnMode spawnMode = SpawnMode.StartOnStart;

        [Tooltip("Delay before the first spawn")]
        [Min(0f)]
        public float startDelay = 0f;

        [Tooltip("Seconds between each spawn wave (Interval mode only)")]
        [Min(0.01f)]
        public float spawnInterval = 8f;

        [Tooltip("Maximum number of waves to spawn (0 = infinite, Interval mode only)")]
        [Min(0)]
        public int maxWaves = 0;

        [Header("Spawn Location")]
        [Tooltip("Where agents will spawn (position + rotation)")]
        public Transform spawnLocation;

        [Tooltip("If no location is set, use this transform's position")]
        public bool useThisTransformIfNoLocation = true;

        [Header("Patrol")]
        public PatrolMode patrolMode = PatrolMode.FreeRoam;

        [Tooltip("Optional center for free roam patrol. If null, uses spawn group position.")]
        public Transform patrolCenter;

        [Tooltip("Free roam radius around the patrol center")]
        [Min(0.1f)]
        public float patrolRadius = 25f;

        [Tooltip("Distance considered 'arrived' for patrol points")]
        [Min(0.1f)]
        public float patrolArrivalDistance = 1.5f;

        [Tooltip("Minimum distance between consecutive patrol targets (0 = auto based on arrival distance).")]
        [Min(0f)]
        public float patrolMinTargetDistance = 0f;

        [Tooltip("Seconds to wait after arriving at a patrol point")]
        [Min(0f)]
        public float patrolWaitTime = 0f;

        [Tooltip("How often to evaluate patrol state (seconds)")]
        [Range(0.05f, 2f)]
        public float patrolUpdateInterval = 0.25f;

        [Tooltip("Randomize the initial patrol update to avoid all spawners updating on the same frame.")]
        public bool staggerPatrolUpdates = true;

        [Tooltip("Use a shared scheduler to spread patrol updates across frames when many spawners are active.")]
        public bool useCentralScheduler = true;

        [Tooltip("Max spawners processed per frame by the central scheduler.")]
        [Min(1)]
        public int centralSchedulerMaxSpawnersPerFrame = 3;

        [Tooltip("Keep spawn group formation while patrolling")]
        public bool useFormationForPatrol = true;

        [Tooltip("Pause patrol while any agent in the group is engaged in combat")]
        public bool pausePatrolWhenEngaged = true;

        [Header("Waypoints")]
        public List<Transform> patrolWaypoints = new List<Transform>();

        [Tooltip("Loop back to the first waypoint after the last")]
        public bool loopWaypoints = true;

        [Tooltip("Pick the next waypoint at random instead of sequential order")]
        public bool randomizeWaypoints = false;

        [Header("Combat")]
        [Tooltip("Enable faction-vs-faction battle mode so patrols react to enemy factions")]
        public bool enableFactionBattleMode = true;

        [Header("Visual Feedback")]
        [Tooltip("Show spawn and patrol gizmos in editor")]
        public bool showGizmos = true;

        [Tooltip("Show formation layout preview for the spawn count")]
        public bool showFormationPreview = true;

        [Tooltip("Show the patrol radius for Free Roam mode")]
        public bool showPatrolRadius = true;

        [Tooltip("Custom gizmo color (leave default for auto-color based on faction)")]
        public Color customGizmoColor = Color.clear;

        [Header("Debug")]
        [Tooltip("Logs patrol diagnostics to help debug retargeting and flicker.")]
        public bool debugPatrolLogs = false;

        [Tooltip("Include agent-level patrol diagnostics (can be noisy).")]
        public bool debugPatrolVerbose = false;

        [Tooltip("Seconds between debug log entries per patrol group.")]
        [Min(0.1f)]
        public float debugPatrolLogInterval = 1f;

        private Coroutine _spawnRoutine;
        private int _wavesSpawned;
        private float _nextPatrolUpdate;
        private readonly List<PatrolGroup> _patrolGroups = new List<PatrolGroup>();
        private const int PatrolTargetSampleAttempts = 6;
        private const int DebugVerboseAgentLimit = 5;
        private const float PatrolDestChangeThreshold = 0.5f;
        private const float PatrolDestChangeIgnoreSeconds = 0.25f;

        private readonly Dictionary<int, PatrolAgentDebugState> _patrolAgentDebugStates = new Dictionary<int, PatrolAgentDebugState>();

        private static readonly Color[] FactionColors = new Color[]
        {
            new Color(1f, 0.3f, 0.3f, 0.5f),    // Red
            new Color(0.3f, 0.6f, 1f, 0.5f),    // Blue
            new Color(0.3f, 1f, 0.3f, 0.5f),    // Green
            new Color(1f, 0.8f, 0.2f, 0.5f),    // Yellow
            new Color(1f, 0.4f, 0.8f, 0.5f),    // Pink
            new Color(0.6f, 0.3f, 1f, 0.5f),    // Purple
            new Color(1f, 0.6f, 0.2f, 0.5f),    // Orange
            new Color(0.2f, 0.9f, 0.9f, 0.5f),  // Cyan
        };

        private class PatrolGroup
        {
            public int spawnGroupId;
            public int waypointIndex;
            public Vector3 lastTarget;
            public bool hasTarget;
            public float nextMoveTime;
            public float lastTargetSetTime;
            public float nextDebugLogTime;
        }

        private class PatrolAgentDebugState
        {
            public AgentNavigationState navState;
            public CrowdCombatState combatState;
            public bool hasAttackTargetPosition;
            public int attackTargetAgentIndex;
            public Transform attackTargetTransform;
            public bool hasManualCommand;
            public bool hasRTSFormationPosition;
            public bool isAggroed;
            public bool navAvailable;
            public bool navHasPath;
            public bool navPathPending;
            public bool navStopped;
            public Vector3 navDestination;
            public float nextChangeLogTime;
        }

        private struct PatrolArrivalDiagnostics
        {
            public int aliveAgents;
            public int navMissing;
            public int pathPending;
            public int hasPath;
            public int noPath;
            public int farByRemaining;
            public int farByLastTarget;
            public float closestRemaining;
            public float farthestRemaining;
            public float closestTargetDistance;
            public float farthestTargetDistance;
            public float timeSinceTargetSet;
            public float minTravelTime;
            public int firstFailingAgentIndex;
            public string firstFailureReason;
            public float firstFailureDistance;
        }

        public bool IsSpawning => _spawnRoutine != null;
        public int WavesSpawned => _wavesSpawned;

        private void OnValidate()
        {
            spawnCount = Mathf.Max(1, spawnCount);
            startDelay = Mathf.Max(0f, startDelay);
            spawnInterval = Mathf.Max(0.01f, spawnInterval);
            maxWaves = Mathf.Max(0, maxWaves);
            patrolRadius = Mathf.Max(0.1f, patrolRadius);
            patrolArrivalDistance = Mathf.Max(0.1f, patrolArrivalDistance);
            patrolMinTargetDistance = Mathf.Max(0f, patrolMinTargetDistance);
            patrolWaitTime = Mathf.Max(0f, patrolWaitTime);
            patrolUpdateInterval = Mathf.Clamp(patrolUpdateInterval, 0.05f, 2f);
            centralSchedulerMaxSpawnersPerFrame = Mathf.Max(1, centralSchedulerMaxSpawnersPerFrame);
            debugPatrolLogInterval = Mathf.Max(0.1f, debugPatrolLogInterval);

            if (float.IsNaN(spawnFormationSpacing) || float.IsInfinity(spawnFormationSpacing))
                spawnFormationSpacing = 0f;
        }

        private void Start()
        {
            if (!Application.isPlaying)
                return;

            if (spawnMode == SpawnMode.StartOnStart)
            {
                StartCoroutine(SpawnOnceDelayed());
            }
            else if (spawnMode == SpawnMode.Interval)
            {
                StartSpawning();
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            _nextPatrolUpdate = staggerPatrolUpdates
                ? Time.time + Random.Range(0f, patrolUpdateInterval)
                : Time.time;

            if (useCentralScheduler)
            {
                PatrolScheduler.Register(this);
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (useCentralScheduler)
                return;

            if (Time.time < _nextPatrolUpdate)
                return;

            _nextPatrolUpdate = Time.time + patrolUpdateInterval;
            UpdatePatrolGroups();
        }

        private void OnDisable()
        {
            StopSpawning();
            _patrolGroups.Clear();
            PatrolScheduler.Unregister(this);
        }

        public void StartSpawning()
        {
            if (!Application.isPlaying)
                return;

            if (_spawnRoutine != null)
                return;

            if (!CanStartSpawning())
                return;

            _spawnRoutine = StartCoroutine(SpawnLoop());
        }

        public void StopSpawning()
        {
            if (_spawnRoutine != null)
            {
                StopCoroutine(_spawnRoutine);
                _spawnRoutine = null;
            }
        }

        public void ResetWaveCounter()
        {
            _wavesSpawned = 0;
        }

        /// <summary>
        /// Manually trigger a single wave (does not count toward max waves).
        /// </summary>
        public void SpawnNow()
        {
            if (!Application.isPlaying)
                return;

            SpawnOnce(false);
        }

        private IEnumerator SpawnOnceDelayed()
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            SpawnOnce(true);
        }

        private bool CanStartSpawning()
        {
            if (crowdController == null)
            {
                Debug.LogWarning($"[SpawnPatrol] {gameObject.name}: No crowd controller assigned!", this);
                return false;
            }

            if (spawnMode == SpawnMode.Interval && maxWaves > 0 && _wavesSpawned >= maxWaves)
            {
                Debug.LogWarning($"[SpawnPatrol] {gameObject.name}: Max waves reached ({maxWaves}). Reset wave counter to start again.", this);
                return false;
            }

            return true;
        }

        private IEnumerator SpawnLoop()
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            while (true)
            {
                if (!SpawnOnce(true))
                {
                    _spawnRoutine = null;
                    yield break;
                }

                if (maxWaves > 0 && _wavesSpawned >= maxWaves)
                {
                    _spawnRoutine = null;
                    yield break;
                }

                yield return new WaitForSeconds(spawnInterval);
            }
        }

        private bool SpawnOnce(bool countWave)
        {
            if (!CanSpawn())
                return false;

            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();

            EnemyCrowd crowdConfig = GetCrowdConfig();
            bool restoreOverride = false;
            bool originalOverride = false;

            if (forceSpawnerFormation && crowdConfig != null && crowdConfig.overrideFormation)
            {
                originalOverride = crowdConfig.overrideFormation;
                crowdConfig.overrideFormation = false;
                restoreOverride = true;
            }

            List<int> spawnedIndices;
            try
            {
                spawnedIndices = crowdController.SpawnEnemyCrowd(
                    enemyCrowdIndex,
                    spawnPos,
                    spawnRot,
                    spawnCount,
                    spawnFormation,
                    spawnFormationSpacing
                );
            }
            finally
            {
                if (restoreOverride && crowdConfig != null)
                {
                    crowdConfig.overrideFormation = originalOverride;
                }
            }

            if (spawnedIndices != null && spawnedIndices.Count > 0)
            {
                if (enableFactionBattleMode)
                {
                    crowdController.FactionBattleMode = true;
                }

                RegisterPatrolGroup(spawnedIndices);

                if (countWave)
                    _wavesSpawned++;

                return true;
            }

            Debug.LogWarning($"[SpawnPatrol] {gameObject.name}: Failed to spawn agents!", this);
            return false;
        }

        private bool CanSpawn()
        {
            if (crowdController == null)
            {
                Debug.LogWarning($"[SpawnPatrol] {gameObject.name}: No crowd controller assigned!", this);
                return false;
            }

            return true;
        }

        private void RegisterPatrolGroup(List<int> spawnedIndices)
        {
            int spawnGroupId = ResolveSpawnGroupId(spawnedIndices);
            if (spawnGroupId < 0)
                return;

            var group = new PatrolGroup
            {
                spawnGroupId = spawnGroupId,
                waypointIndex = 0,
                lastTarget = Vector3.zero,
                hasTarget = false,
                nextMoveTime = 0f
            };

            _patrolGroups.Add(group);

            AssignNextPatrolTarget(group, "Initial");
        }

        private int ResolveSpawnGroupId(List<int> spawnedIndices)
        {
            if (crowdController == null || spawnedIndices == null)
                return -1;

            for (int i = 0; i < spawnedIndices.Count; i++)
            {
                int agentIndex = spawnedIndices[i];
                var agent = crowdController.GetAgent(agentIndex);
                if (agent != null && agent.spawnGroupId >= 0)
                {
                    return agent.spawnGroupId;
                }
            }

            return -1;
        }

        private void UpdatePatrolGroups()
        {
            if (crowdController == null || _patrolGroups.Count == 0)
                return;

            for (int i = _patrolGroups.Count - 1; i >= 0; i--)
            {
                var group = _patrolGroups[i];
                if (!IsGroupAlive(group.spawnGroupId))
                {
                    if (debugPatrolLogs)
                    {
                        Debug.Log($"[SpawnPatrol][Debug] {gameObject.name}: Removing patrol group {group.spawnGroupId} (no alive agents).", this);
                    }
                    _patrolGroups.RemoveAt(i);
                    continue;
                }

                // If another system (e.g., EnemyMassesRTSAI) has claimed these agents
                // by clearing their ManualCommand state, stop patrolling the group.
                if (!IsGroupUnderPatrolControl(group.spawnGroupId))
                {
                    if (debugPatrolLogs)
                    {
                        Debug.Log($"[SpawnPatrol][Debug] {gameObject.name}: Removing patrol group {group.spawnGroupId} (agents claimed by AI or another system — ManualCommand cleared).", this);
                    }
                    _patrolGroups.RemoveAt(i);
                    continue;
                }

                if (pausePatrolWhenEngaged && IsGroupEngaged(group.spawnGroupId))
                {
                    if (debugPatrolLogs && Time.time >= group.nextDebugLogTime)
                    {
                        Debug.Log($"[SpawnPatrol][Debug] {gameObject.name}: Group {group.spawnGroupId} patrol paused (engaged).", this);
                        group.nextDebugLogTime = Time.time + debugPatrolLogInterval;
                    }
                    if (debugPatrolLogs)
                    {
                        LogPatrolStateChanges(group);
                    }
                    continue;
                }

                bool arrived;
                PatrolArrivalDiagnostics diagnostics = default;
                if (debugPatrolLogs && group.hasTarget)
                {
                    arrived = IsGroupArrived(group, true, out diagnostics);
                    if (Time.time >= group.nextDebugLogTime)
                    {
                        LogPatrolDiagnostics(group, arrived, diagnostics);
                        if (debugPatrolVerbose)
                        {
                            LogPatrolAgentDetails(group);
                        }
                        group.nextDebugLogTime = Time.time + debugPatrolLogInterval;
                    }
                    LogPatrolStateChanges(group);
                }
                else
                {
                    arrived = group.hasTarget && IsGroupArrived(group);
                }

                if (arrived && group.nextMoveTime <= 0f)
                {
                    group.nextMoveTime = Time.time + patrolWaitTime;
                }

                if (!group.hasTarget || (arrived && Time.time >= group.nextMoveTime))
                {
                    string reason = !group.hasTarget ? "NoTarget" : "Arrived";
                    AssignNextPatrolTarget(group, reason);
                }
            }
        }

        private bool IsGroupAlive(int spawnGroupId)
        {
            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(spawnGroupId, out var spawnGroup))
                return false;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return false;

            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var agent = agents[agentIndex];
                if (agent != null && !agent.isDead)
                    return true;
            }

            return false;
        }

        private bool IsGroupEngaged(int spawnGroupId)
        {
            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(spawnGroupId, out var spawnGroup))
                return false;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return false;

            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                    continue;

                if (agent.isAggroed || agent.attackTargetAgentIndex >= 0 || agent.attackTargetTransform != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if at least one alive agent in the group is still under patrol control
        /// (has ManualCommand navigation state). Returns false when an external system
        /// (e.g., EnemyMassesRTSAI) has claimed the agents by clearing ManualCommand.
        /// </summary>
        private bool IsGroupUnderPatrolControl(int spawnGroupId)
        {
            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(spawnGroupId, out var spawnGroup))
                return false;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return false;

            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                    continue;

                // At least one alive agent still has ManualCommand = patrol is still active
                if (agent.hasManualCommand &&
                    agent.navigationState == AgentNavigationState.ManualCommand)
                    return true;
            }

            // No alive agents remain under patrol control
            return false;
        }

        private bool IsGroupArrived(PatrolGroup group)
        {
            return IsGroupArrived(group, false, out _);
        }

        private bool IsGroupArrived(PatrolGroup group, bool computeDiagnostics, out PatrolArrivalDiagnostics diagnostics)
        {
            diagnostics = default;
            if (group == null)
                return false;

            float minTravelTime = Mathf.Max(0.2f, patrolUpdateInterval);
            float timeSinceTargetSet = Time.time - group.lastTargetSetTime;
            if (computeDiagnostics)
            {
                diagnostics.minTravelTime = minTravelTime;
                diagnostics.timeSinceTargetSet = timeSinceTargetSet;
            }

            if (timeSinceTargetSet < minTravelTime)
            {
                if (computeDiagnostics)
                {
                    diagnostics.firstFailureReason = "minTravelTime";
                    diagnostics.firstFailureDistance = timeSinceTargetSet;
                }
                return false;
            }

            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(group.spawnGroupId, out var spawnGroup))
                return false;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return false;

            bool hasAlive = false;

            if (computeDiagnostics)
            {
                diagnostics.closestRemaining = float.PositiveInfinity;
                diagnostics.closestTargetDistance = float.PositiveInfinity;
                diagnostics.farthestRemaining = 0f;
                diagnostics.farthestTargetDistance = 0f;
                diagnostics.firstFailingAgentIndex = -1;
            }

            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                    continue;

                hasAlive = true;
                var nav = agent.navigationAgent;
                if (!CanControlNavigationAgent(nav))
                {
                    if (computeDiagnostics)
                    {
                        diagnostics.aliveAgents++;
                        diagnostics.navMissing++;
                    }
                    continue;
                }

                if (nav.Capabilities.SupportsPathPending && nav.PathPending)
                {
                    if (computeDiagnostics)
                    {
                        diagnostics.aliveAgents++;
                        diagnostics.pathPending++;
                        if (string.IsNullOrEmpty(diagnostics.firstFailureReason))
                        {
                            diagnostics.firstFailureReason = "pathPending";
                            diagnostics.firstFailingAgentIndex = agentIndex;
                        }
                    }
                    return false;
                }

                float arrivalDistance = patrolArrivalDistance;
                if (nav.StoppingDistance + 0.1f > arrivalDistance)
                {
                    arrivalDistance = nav.StoppingDistance + 0.1f;
                }

                if (nav.HasPath)
                {
                    if (nav.RemainingDistance > arrivalDistance)
                    {
                        if (computeDiagnostics)
                        {
                            diagnostics.aliveAgents++;
                            diagnostics.hasPath++;
                            diagnostics.farByRemaining++;
                            diagnostics.closestRemaining = Mathf.Min(diagnostics.closestRemaining, nav.RemainingDistance);
                            diagnostics.farthestRemaining = Mathf.Max(diagnostics.farthestRemaining, nav.RemainingDistance);
                            if (string.IsNullOrEmpty(diagnostics.firstFailureReason))
                            {
                                diagnostics.firstFailureReason = "remainingDistance";
                                diagnostics.firstFailingAgentIndex = agentIndex;
                                diagnostics.firstFailureDistance = nav.RemainingDistance;
                            }
                        }
                        return false;
                    }
                    if (computeDiagnostics)
                    {
                        diagnostics.aliveAgents++;
                        diagnostics.hasPath++;
                        diagnostics.closestRemaining = Mathf.Min(diagnostics.closestRemaining, nav.RemainingDistance);
                        diagnostics.farthestRemaining = Mathf.Max(diagnostics.farthestRemaining, nav.RemainingDistance);
                    }
                }
                else
                {
                    Vector3 currentPos = agent.WorldPosition;
                    Vector3 targetPos = nav.Destination;
                    float arrivalSqr = arrivalDistance * arrivalDistance;
                    if ((currentPos - targetPos).sqrMagnitude > arrivalSqr)
                    {
                        if (computeDiagnostics)
                        {
                            float distance = Vector3.Distance(currentPos, targetPos);
                            diagnostics.aliveAgents++;
                            diagnostics.noPath++;
                            diagnostics.farByLastTarget++;
                            diagnostics.closestTargetDistance = Mathf.Min(diagnostics.closestTargetDistance, distance);
                            diagnostics.farthestTargetDistance = Mathf.Max(diagnostics.farthestTargetDistance, distance);
                            if (string.IsNullOrEmpty(diagnostics.firstFailureReason))
                            {
                                diagnostics.firstFailureReason = "noPathDistance";
                                diagnostics.firstFailingAgentIndex = agentIndex;
                                diagnostics.firstFailureDistance = distance;
                            }
                        }
                        return false;
                    }
                    if (computeDiagnostics)
                    {
                        float distance = Vector3.Distance(currentPos, targetPos);
                        diagnostics.aliveAgents++;
                        diagnostics.noPath++;
                        diagnostics.closestTargetDistance = Mathf.Min(diagnostics.closestTargetDistance, distance);
                        diagnostics.farthestTargetDistance = Mathf.Max(diagnostics.farthestTargetDistance, distance);
                    }
                }
            }

            if (computeDiagnostics)
            {
                if (!hasAlive && string.IsNullOrEmpty(diagnostics.firstFailureReason))
                {
                    diagnostics.firstFailureReason = "noAliveAgents";
                }
            }

            return hasAlive;
        }

        private void AssignNextPatrolTarget(PatrolGroup group, string reason = null)
        {
            if (crowdController == null || group == null)
                return;

            if (!TryGetNextPatrolTarget(group, out Vector3 target, out int waypointIndex))
                return;

            group.lastTarget = target;
            group.hasTarget = true;
            group.nextMoveTime = 0f;
            group.lastTargetSetTime = Time.time;

            // useManualCommand=true prevents formation system from overriding patrol destinations
            // This is the key fix for patrol flickering - ManualCommand state is protected
            crowdController.SetDestinationForSpawnGroup(group.spawnGroupId, target, useFormationForPatrol, useManualCommand: true);

            if (debugPatrolLogs && !string.IsNullOrEmpty(reason))
            {
                string waypointInfo = patrolMode == PatrolMode.Waypoints ? $" waypointIndex={waypointIndex}" : string.Empty;
                Debug.Log($"[SpawnPatrol][Debug] {gameObject.name}: Assign patrol target (group={group.spawnGroupId}, reason={reason}, mode={patrolMode}{waypointInfo}) target={target}", this);
            }
        }

        private bool TryGetNextPatrolTarget(PatrolGroup group, out Vector3 target, out int waypointIndex)
        {
            target = Vector3.zero;
            waypointIndex = -1;

            if (patrolMode == PatrolMode.Waypoints)
            {
                if (patrolWaypoints == null || patrolWaypoints.Count == 0)
                    return false;

                int index;
                if (randomizeWaypoints)
                {
                    index = Random.Range(0, patrolWaypoints.Count);
                }
                else
                {
                    index = Mathf.Clamp(group.waypointIndex, 0, patrolWaypoints.Count - 1);
                    if (index >= patrolWaypoints.Count)
                        index = 0;
                }

                Transform waypoint = patrolWaypoints[index];
                if (waypoint == null)
                    return false;

                waypointIndex = index;
                target = waypoint.position;

                if (!randomizeWaypoints)
                {
                    int nextIndex = index + 1;
                    if (nextIndex >= patrolWaypoints.Count)
                    {
                        nextIndex = loopWaypoints ? 0 : patrolWaypoints.Count - 1;
                    }
                    group.waypointIndex = nextIndex;
                }

                return true;
            }

            Vector3 center = GetPatrolCenter(group.spawnGroupId);
            float minTargetDistance = GetMinPatrolTargetDistance();
            float minTargetDistanceSqr = minTargetDistance * minTargetDistance;
            Vector3 candidate = center;

            for (int attempt = 0; attempt < PatrolTargetSampleAttempts; attempt++)
            {
                Vector2 offset2D = Random.insideUnitCircle * patrolRadius;
                Vector3 sample = center + new Vector3(offset2D.x, 0f, offset2D.y);

                if (TrySamplePatrolPosition(group.spawnGroupId, sample, patrolRadius, out Vector3 sampledPosition))
                {
                    candidate = sampledPosition;
                }
                else
                {
                    candidate = center;
                }

                if (!group.hasTarget || minTargetDistance <= 0f ||
                    (candidate - group.lastTarget).sqrMagnitude >= minTargetDistanceSqr)
                {
                    target = candidate;
                    return true;
                }
            }

            target = candidate;

            return true;
        }

        private bool TrySamplePatrolPosition(int spawnGroupId, Vector3 sample, float radius, out Vector3 sampledPosition)
        {
            sampledPosition = sample;
            if (crowdController == null)
                return false;

            var spawnGroups = crowdController.GetSpawnGroups();
            var agents = crowdController.GetAgents();
            if (spawnGroups == null || agents == null || !spawnGroups.TryGetValue(spawnGroupId, out var spawnGroup))
                return false;

            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var navAgent = agents[agentIndex]?.navigationAgent;
                if (navAgent == null)
                    continue;

                if (navAgent.SamplePosition(sample, out sampledPosition, radius))
                    return true;
            }

            return false;
        }

        private Vector3 GetSpawnPosition()
        {
            if (spawnLocation != null)
                return spawnLocation.position;

            if (useThisTransformIfNoLocation)
                return transform.position;

            return Vector3.zero;
        }

        private Quaternion GetSpawnRotation()
        {
            if (spawnLocation != null)
                return spawnLocation.rotation;

            if (useThisTransformIfNoLocation)
                return transform.rotation;

            return Quaternion.identity;
        }

        private Vector3 GetPatrolCenter(int spawnGroupId)
        {
            if (patrolCenter != null)
                return patrolCenter.position;

            if (crowdController != null)
            {
                var spawnGroups = crowdController.GetSpawnGroups();
                if (spawnGroups != null && spawnGroups.TryGetValue(spawnGroupId, out var spawnGroup))
                {
                    return spawnGroup.spawnPosition;
                }
            }

            if (spawnLocation != null)
                return spawnLocation.position;

            if (useThisTransformIfNoLocation)
                return transform.position;

            return transform.position;
        }

        private float GetMinPatrolTargetDistance()
        {
            if (patrolMinTargetDistance > 0f)
                return patrolMinTargetDistance;

            float autoDistance = patrolArrivalDistance * 2f;
            if (patrolRadius > 0f)
                autoDistance = Mathf.Min(autoDistance, patrolRadius * 0.75f);

            return Mathf.Max(0f, autoDistance);
        }

        private void LogPatrolDiagnostics(PatrolGroup group, bool arrived, PatrolArrivalDiagnostics diagnostics)
        {
            string message = $"[SpawnPatrol][Debug] {gameObject.name}: Group {group.spawnGroupId} " +
                             $"arrived={arrived} hasTarget={group.hasTarget} mode={patrolMode} " +
                             $"lastTarget={group.lastTarget} timeSinceSet={diagnostics.timeSinceTargetSet:0.00}s " +
                             $"minTravel={diagnostics.minTravelTime:0.00}s " +
                             $"alive={diagnostics.aliveAgents} navMissing={diagnostics.navMissing} " +
                             $"pathPending={diagnostics.pathPending} hasPath={diagnostics.hasPath} " +
                             $"noPath={diagnostics.noPath} farRemain={diagnostics.farByRemaining} " +
                             $"farTarget={diagnostics.farByLastTarget}";

            if (diagnostics.hasPath > 0)
            {
                message += $" remainingDist(min/max)=({diagnostics.closestRemaining:0.00}/{diagnostics.farthestRemaining:0.00})";
            }

            if (diagnostics.noPath > 0)
            {
                message += $" targetDist(min/max)=({diagnostics.closestTargetDistance:0.00}/{diagnostics.farthestTargetDistance:0.00})";
            }

            if (!string.IsNullOrEmpty(diagnostics.firstFailureReason))
            {
                message += $" firstFail={diagnostics.firstFailureReason}";
                if (diagnostics.firstFailingAgentIndex >= 0 || diagnostics.firstFailureDistance > 0f)
                {
                    message += $" agent={diagnostics.firstFailingAgentIndex} dist={diagnostics.firstFailureDistance:0.00}";
                }
            }

            Debug.Log(message, this);
        }

        private void LogPatrolAgentDetails(PatrolGroup group)
        {
            if (crowdController == null || group == null)
                return;

            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(group.spawnGroupId, out var spawnGroup))
                return;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return;

            int logged = 0;
            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                    continue;

                var nav = agent.navigationAgent;
                if (nav == null)
                {
                    Debug.Log($"[SpawnPatrol][Debug] Group {group.spawnGroupId} agent={agentIndex} nav=NULL", this);
                    logged++;
                }
                else
                {
                    float distToTarget = Vector3.Distance(agent.WorldPosition, group.lastTarget);
                    Debug.Log($"[SpawnPatrol][Debug] Group {group.spawnGroupId} agent={agentIndex} " +
                              $"hasPath={nav.HasPath} pending={nav.PathPending} remain={nav.RemainingDistance:0.00} " +
                              $"distToTarget={distToTarget:0.00} stopped={nav.IsStopped} speed={nav.Speed:0.00}", this);
                    logged++;
                }

                if (logged >= DebugVerboseAgentLimit)
                    break;
            }
        }

        private void LogPatrolStateChanges(PatrolGroup group)
        {
            if (!debugPatrolLogs || crowdController == null || group == null)
                return;

            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(group.spawnGroupId, out var spawnGroup))
                return;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return;

            float timeSinceTargetSet = Time.time - group.lastTargetSetTime;
            bool allowDestinationChangeLogs = timeSinceTargetSet > PatrolDestChangeIgnoreSeconds;

            foreach (int agentIndex in spawnGroup.agentIndices)
            {
                if (agentIndex < 0 || agentIndex >= agents.Count)
                    continue;

                var agent = agents[agentIndex];
                if (agent == null || agent.isDead)
                {
                    _patrolAgentDebugStates.Remove(agentIndex);
                    continue;
                }

                var nav = agent.navigationAgent;
                bool navAvailable = CanControlNavigationAgent(nav);

                if (!_patrolAgentDebugStates.TryGetValue(agentIndex, out var state))
                {
                    state = new PatrolAgentDebugState
                    {
                        navState = agent.navigationState,
                        combatState = agent.combatState,
                        hasAttackTargetPosition = agent.hasAttackTargetPosition,
                        attackTargetAgentIndex = agent.attackTargetAgentIndex,
                        attackTargetTransform = agent.attackTargetTransform,
                        hasManualCommand = agent.hasManualCommand,
                        hasRTSFormationPosition = agent.hasRTSFormationPosition,
                        isAggroed = agent.isAggroed,
                        navAvailable = navAvailable,
                        navHasPath = navAvailable && nav.HasPath,
                        navPathPending = navAvailable && nav.PathPending,
                        navStopped = navAvailable && nav.IsStopped,
                        navDestination = navAvailable ? nav.Destination : Vector3.zero,
                        nextChangeLogTime = 0f
                    };

                    _patrolAgentDebugStates[agentIndex] = state;
                    continue;
                }

                if (Time.time < state.nextChangeLogTime)
                {
                    UpdatePatrolDebugState(agent, nav, navAvailable, state);
                    continue;
                }

                var changes = new List<string>();

                if (state.navAvailable != navAvailable)
                {
                    changes.Add($"navAvailable {state.navAvailable}->{navAvailable}");
                }

                if (state.navState != agent.navigationState)
                {
                    changes.Add($"navState {state.navState}->{agent.navigationState}");
                }

                if (state.combatState != agent.combatState)
                {
                    changes.Add($"combat {state.combatState}->{agent.combatState}");
                }

                if (state.hasManualCommand != agent.hasManualCommand)
                {
                    changes.Add($"manualCommand {state.hasManualCommand}->{agent.hasManualCommand}");
                }

                if (state.hasRTSFormationPosition != agent.hasRTSFormationPosition)
                {
                    changes.Add($"rtsFormationPos {state.hasRTSFormationPosition}->{agent.hasRTSFormationPosition}");
                }

                if (state.isAggroed != agent.isAggroed)
                {
                    changes.Add($"aggro {state.isAggroed}->{agent.isAggroed}");
                }

                if (state.hasAttackTargetPosition != agent.hasAttackTargetPosition ||
                    state.attackTargetAgentIndex != agent.attackTargetAgentIndex ||
                    state.attackTargetTransform != agent.attackTargetTransform)
                {
                    string oldTargetName = state.attackTargetTransform != null ? state.attackTargetTransform.name : "null";
                    string newTargetName = agent.attackTargetTransform != null ? agent.attackTargetTransform.name : "null";
                    changes.Add($"attackTarget pos={state.hasAttackTargetPosition}->{agent.hasAttackTargetPosition} " +
                                $"idx={state.attackTargetAgentIndex}->{agent.attackTargetAgentIndex} " +
                                $"transform={oldTargetName}->{newTargetName}");
                }

                if (navAvailable)
                {
                    if (state.navHasPath != nav.HasPath || state.navPathPending != nav.PathPending || state.navStopped != nav.IsStopped)
                    {
                        changes.Add($"nav flags hasPath={state.navHasPath}->{nav.HasPath} " +
                                    $"pending={state.navPathPending}->{nav.PathPending} " +
                                    $"stopped={state.navStopped}->{nav.IsStopped}");
                    }

                    float destDelta = Vector3.Distance(state.navDestination, nav.Destination);
                    if (destDelta > PatrolDestChangeThreshold && allowDestinationChangeLogs)
                    {
                        changes.Add($"destination changed delta={destDelta:0.00} dest={nav.Destination}");
                    }
                }

                if (changes.Count > 0)
                {
                    string combined = string.Join(", ", changes);
                    Debug.Log($"[SpawnPatrol][Debug] Group {group.spawnGroupId} agent={agentIndex} {combined}", this);
                    state.nextChangeLogTime = Time.time + debugPatrolLogInterval;
                }

                UpdatePatrolDebugState(agent, nav, navAvailable, state);
            }
        }

        private void UpdatePatrolDebugState(CrowdAgent agent, INavigationAgent nav, bool navAvailable, PatrolAgentDebugState state)
        {
            state.navState = agent.navigationState;
            state.combatState = agent.combatState;
            state.hasAttackTargetPosition = agent.hasAttackTargetPosition;
            state.attackTargetAgentIndex = agent.attackTargetAgentIndex;
            state.attackTargetTransform = agent.attackTargetTransform;
            state.hasManualCommand = agent.hasManualCommand;
            state.hasRTSFormationPosition = agent.hasRTSFormationPosition;
            state.isAggroed = agent.isAggroed;
            state.navAvailable = navAvailable;

            if (navAvailable)
            {
                state.navHasPath = nav.HasPath;
                state.navPathPending = nav.PathPending;
                state.navStopped = nav.IsStopped;
                state.navDestination = nav.Destination;
            }
            else
            {
                state.navHasPath = false;
                state.navPathPending = false;
                state.navStopped = false;
                state.navDestination = Vector3.zero;
            }
        }

        private static bool CanControlNavigationAgent(INavigationAgent navigationAgent)
        {
            if (navigationAgent == null)
                return false;

            if (!navigationAgent.Capabilities.RequiresSurfaceValidationForControl)
                return true;

            return navigationAgent.IsOnNavMesh;
        }

        public Color GetGizmoColor()
        {
            if (customGizmoColor != Color.clear)
                return customGizmoColor;

            return FactionColors[Mathf.Abs(enemyCrowdIndex) % FactionColors.Length];
        }

        private FormationType GetEffectiveFormationType()
        {
            var crowd = GetCrowdConfig();
            if (crowd != null && crowd.overrideFormation && !forceSpawnerFormation)
                return crowd.formationType;

            return spawnFormation;
        }

        private EnemyCrowd GetCrowdConfig()
        {
            if (crowdController == null || crowdController.EnemyCrowds == null)
                return null;

            if (enemyCrowdIndex < 0 || enemyCrowdIndex >= crowdController.EnemyCrowds.Count)
                return null;

            return crowdController.EnemyCrowds[enemyCrowdIndex];
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            Color gizmoColor = GetGizmoColor();
            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(spawnPos, 0.5f);
            DrawDirectionalArrow(spawnPos, spawnRot, 3f, gizmoColor);

            if (showPatrolRadius && patrolMode == PatrolMode.FreeRoam)
            {
                Vector3 center = patrolCenter != null ? patrolCenter.position : spawnPos;
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.35f);
                Gizmos.DrawWireSphere(center, patrolRadius);
            }

            if (patrolMode == PatrolMode.Waypoints && patrolWaypoints != null && patrolWaypoints.Count > 0)
            {
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.5f);
                for (int i = 0; i < patrolWaypoints.Count; i++)
                {
                    var waypoint = patrolWaypoints[i];
                    if (waypoint == null) continue;
                    Gizmos.DrawWireSphere(waypoint.position, 0.35f);
                    int nextIndex = i + 1;
                    if (nextIndex >= patrolWaypoints.Count)
                    {
                        if (!loopWaypoints) continue;
                        nextIndex = 0;
                    }
                    var nextWaypoint = patrolWaypoints[nextIndex];
                    if (nextWaypoint != null)
                    {
                        Gizmos.DrawLine(waypoint.position, nextWaypoint.position);
                    }
                }
            }

            if (showFormationPreview)
            {
                DrawFormationPreview(spawnPos, spawnRot, gizmoColor);
            }
        }

        private void DrawFormationPreview(Vector3 spawnPos, Quaternion spawnRot, Color color)
        {
            FormationType formation = GetEffectiveFormationType();
            EnemyCrowd crowd = GetCrowdConfig();

            SpawnSettings previewSettings = BuildPreviewSettings(crowd, formation);
            float destinationSpread = GetDestinationSpread(crowd);

            var formationSystem = new EnemyMassesFormationSystem(previewSettings);

            int previewCount = Mathf.Clamp(spawnCount, 1, 256);
            for (int i = 0; i < previewCount; i++)
            {
                Vector3 pos = formationSystem.CalculateFormationPosition(
                    i,
                    previewCount,
                    spawnPos,
                    destinationSpread,
                    spawnRot,
                    formation
                );

                Gizmos.color = new Color(color.r, color.g, color.b, 0.55f);
                Gizmos.DrawWireSphere(pos, 0.15f);
            }
        }

        private SpawnSettings BuildPreviewSettings(EnemyCrowd crowd, FormationType formation)
        {
            return new SpawnSettings
            {
                formationType = formation,
                formationSize = crowd != null ? crowd.defaultFormationSize : 10f,
                formationSpacing = !Mathf.Approximately(spawnFormationSpacing, 0f)
                    ? Mathf.Abs(spawnFormationSpacing)
                    : (crowd != null ? crowd.defaultFormationSpacing : 2f),
                formationThickness = crowd != null ? crowd.defaultFormationThickness : 3f
            };
        }

        private float GetDestinationSpread(EnemyCrowd crowd)
        {
            return crowd != null ? crowd.destinationSpread : 25f;
        }

        private void DrawDirectionalArrow(Vector3 position, Quaternion rotation, float length, Color color)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            Vector3 arrowTip = position + forward * length;
            Vector3 arrowBase = position + forward * (length * 0.3f);

            Gizmos.color = color;
            float shaftWidth = 0.15f;
            Gizmos.DrawLine(position + right * shaftWidth, arrowBase + right * shaftWidth);
            Gizmos.DrawLine(position - right * shaftWidth, arrowBase - right * shaftWidth);
            Gizmos.DrawLine(position + up * shaftWidth, arrowBase + up * shaftWidth);
            Gizmos.DrawLine(position - up * shaftWidth, arrowBase - up * shaftWidth);

            float headWidth = 0.6f;
            float headHeight = 0.3f;
            Vector3 headLeft = arrowBase + right * headWidth;
            Vector3 headRight = arrowBase - right * headWidth;
            Vector3 headTop = arrowBase + up * headHeight;
            Vector3 headBottom = arrowBase - up * headHeight;

            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawLine(headLeft, arrowTip);
            Gizmos.DrawLine(headRight, arrowTip);
            Gizmos.DrawLine(headLeft, headRight);
            Gizmos.DrawLine(headTop, arrowTip);
            Gizmos.DrawLine(headBottom, arrowTip);
            Gizmos.DrawLine(headTop, headBottom);

            int fillLines = 4;
            for (int i = 1; i < fillLines; i++)
            {
                float t = (float)i / fillLines;
                Vector3 leftPoint = Vector3.Lerp(headLeft, arrowTip, t);
                Vector3 rightPoint = Vector3.Lerp(headRight, arrowTip, t);
                Gizmos.DrawLine(leftPoint, rightPoint);
            }
        }
#endif

        internal bool TryScheduledPatrolUpdate(float now)
        {
            if (!isActiveAndEnabled)
                return false;

            if (now < _nextPatrolUpdate)
                return false;

            _nextPatrolUpdate = now + patrolUpdateInterval;
            UpdatePatrolGroups();
            return true;
        }

        private static class PatrolScheduler
        {
            private static readonly List<EnemyMassesSpawnPatrol> Spawners = new List<EnemyMassesSpawnPatrol>();
            private static SchedulerBehaviour _behaviour;
            private static int _cursor;

            public static void Register(EnemyMassesSpawnPatrol spawner)
            {
                if (spawner == null || Spawners.Contains(spawner))
                    return;

                Spawners.Add(spawner);
                EnsureBehaviour();
            }

            public static void Unregister(EnemyMassesSpawnPatrol spawner)
            {
                if (spawner == null)
                    return;

                int index = Spawners.IndexOf(spawner);
                if (index >= 0)
                {
                    Spawners.RemoveAt(index);
                    if (_cursor > index)
                        _cursor--;
                }

                if (Spawners.Count == 0)
                {
                    DestroyBehaviour();
                }
            }

            private static void EnsureBehaviour()
            {
                if (_behaviour != null)
                    return;

                GameObject schedulerObject = new GameObject("EnemyMasses Patrol Scheduler");
                schedulerObject.hideFlags = HideFlags.DontSave;
                UnityEngine.Object.DontDestroyOnLoad(schedulerObject);
                _behaviour = schedulerObject.AddComponent<SchedulerBehaviour>();
            }

            private static void DestroyBehaviour()
            {
                if (_behaviour == null)
                    return;

                UnityEngine.Object.Destroy(_behaviour.gameObject);
                _behaviour = null;
            }

            private static int GetMaxSpawnersPerFrame()
            {
                int budget = 0;
                for (int i = Spawners.Count - 1; i >= 0; i--)
                {
                    var spawner = Spawners[i];
                    if (spawner == null)
                    {
                        Spawners.RemoveAt(i);
                        continue;
                    }

                    if (!spawner.useCentralScheduler)
                        continue;

                    budget = Mathf.Max(budget, spawner.centralSchedulerMaxSpawnersPerFrame);
                }

                return Mathf.Max(1, budget);
            }

            private static void Tick()
            {
                if (Spawners.Count == 0)
                    return;

                float now = Time.time;
                int budget = GetMaxSpawnersPerFrame();
                int processed = 0;
                int scanned = 0;
                int total = Spawners.Count;

                while (processed < budget && scanned < total && Spawners.Count > 0)
                {
                    if (_cursor >= Spawners.Count)
                        _cursor = 0;

                    var spawner = Spawners[_cursor];
                    _cursor++;
                    scanned++;

                    if (spawner == null)
                        continue;

                    if (!spawner.useCentralScheduler || !spawner.isActiveAndEnabled)
                        continue;

                    if (spawner.TryScheduledPatrolUpdate(now))
                        processed++;
                }
            }

            private class SchedulerBehaviour : MonoBehaviour
            {
                private void Update()
                {
                    if (!Application.isPlaying)
                        return;

                    Tick();
                }

                private void OnDestroy()
                {
                    Spawners.Clear();
                    _cursor = 0;
                    _behaviour = null;
                }
            }
        }
    }
}
