using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Determines how the free roam center is calculated for spawned agents.
    /// </summary>
    public enum FreeRoamCenterMode
    {
        /// <summary>Use the position where agents spawned (locked at spawn time)</summary>
        SpawnPosition,
        /// <summary>Use the target transform's position at spawn time (locked, won't follow moving targets)</summary>
        TargetPositionAtSpawn,
        /// <summary>Use a custom transform as the center (can be static or moving)</summary>
        CustomTransform,
        /// <summary>Follow the target transform (free roam area moves with target)</summary>
        FollowTarget
    }

    /// <summary>
    /// Spawns enemy crowds at random positions within a radius around a target transform.
    /// Can spawn all at once, in batches at intervals, or individual agents at random positions.
    /// </summary>
    public class EnemyMassesSpawnAroundTarget : MonoBehaviour
    {
        public enum SpawnMode
        {
            /// <summary>Spawn all agents at once at random positions within the radius</summary>
            AllAtOnce,
            /// <summary>Spawn agents in waves at intervals, each wave at a random position within the radius</summary>
            IntervalWaves,
            /// <summary>Spawn individual agents at random positions within the radius at intervals</summary>
            IndividualAtIntervals
        }

        [Header("Spawn Settings")]
        [Tooltip("The crowd controller that will spawn agents")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Which enemy crowd faction to spawn (index in Enemy Crowds list)")]
        public int enemyCrowdIndex = 0;

        [Tooltip("Total number of agents to spawn")]
        [Min(1)]
        public int totalSpawnCount = 50;

        [Tooltip("How agents should be spawned around the target")]
        public SpawnMode spawnMode = SpawnMode.IntervalWaves;

        [Tooltip("Number of agents per wave (for IntervalWaves mode)")]
        [Min(1)]
        public int agentsPerWave = 10;

        [Tooltip("Formation type for spawned agents")]
        public FormationType spawnFormation = FormationType.Circle;

        [Tooltip("Spacing override for this spawn. 0 = use faction default; non-zero overrides (abs value used)")]
        public float spawnFormationSpacing = 0f;

        [Tooltip("Automatically start spawning in Start()")]
        public bool startSpawningOnStart = true;

        [Header("Target Settings")]
        [Tooltip("The target transform to spawn agents around")]
        public Transform targetTransform;

        [Tooltip("Radius around the target where agents can spawn")]
        [Min(0.1f)]
        public float spawnRadius = 20f;

        [Tooltip("Minimum distance from target center (creates a ring if > 0)")]
        [Min(0f)]
        public float minSpawnRadius = 0f;

        [Tooltip("Use uniform distribution within the radius (true) or more agents near edges (false)")]
        public bool uniformDistribution = true;

        [Tooltip("Max search distance when sampling a valid navigation point near the random position")]
        [Min(0.1f)]
        public float navMeshSearchRadius = 10f;

        [Header("Interval Settings")]
        [Tooltip("Delay before the first spawn")]
        [Min(0f)]
        public float startDelay = 0f;

        [Tooltip("Seconds between each spawn (for interval modes)")]
        [Min(0.01f)]
        public float spawnInterval = 2f;

        [Tooltip("Maximum number of waves to spawn (0 = unlimited, only applies to IntervalWaves and IndividualAtIntervals modes)")]
        [Min(0)]
        public int maxWaves = 0;

        [Header("Post Spawn Behavior")]
        [Tooltip("Automatically set the target as attack destination after spawning")]
        public bool autoAttackTarget = true;

        [Tooltip("If not attacking, set destination to target position")]
        public bool autoMoveToTarget = true;

        [Header("Free Roam (Post-Spawn Patrol)")]
        [Tooltip("Enable free roam behavior after spawning (agents will patrol randomly within a radius)")]
        public bool enableFreeRoam = false;

        [Tooltip("How to determine the center of the free roam area")]
        public FreeRoamCenterMode freeRoamCenterMode = FreeRoamCenterMode.SpawnPosition;

        [Tooltip("Custom transform to use as free roam center (only used when FreeRoamCenterMode is CustomTransform)")]
        public Transform freeRoamCenterTransform;

        [Tooltip("Radius around the free roam center where agents can patrol")]
        [Min(1f)]
        public float freeRoamRadius = 30f;

        [Tooltip("Distance considered 'arrived' at a patrol point")]
        [Min(0.1f)]
        public float freeRoamArrivalDistance = 1.5f;

        [Tooltip("Minimum distance between consecutive patrol targets (0 = auto based on arrival distance)")]
        [Min(0f)]
        public float freeRoamMinTargetDistance = 0f;

        [Tooltip("Seconds to wait after arriving at a patrol point before picking a new one")]
        [Min(0f)]
        public float freeRoamWaitTime = 2f;

        [Tooltip("How often to evaluate free roam state (seconds)")]
        [Range(0.05f, 2f)]
        public float freeRoamUpdateInterval = 0.25f;

        [Tooltip("Pause free roam while any agent in the group is engaged in combat")]
        public bool pauseFreeRoamWhenEngaged = true;

        [Tooltip("Keep spawn group formation while free roaming")]
        public bool useFormationForFreeRoam = false;

        [Header("Visual Feedback")]
        [Tooltip("Show spawn area gizmos in editor")]
        public bool showGizmos = true;

        [Tooltip("Custom gizmo color (leave default for auto-color based on faction)")]
        public Color customGizmoColor = Color.clear;

        [Tooltip("Number of preview spawn points to show")]
        [Range(0, 50)]
        public int previewPointCount = 12;

        private Coroutine _spawnRoutine;
        private int _agentsSpawned;
        private int _wavesSpawned;
        private List<Vector3> _previewPoints = new List<Vector3>();
        private int _previewSeed;
        private float _nextFreeRoamUpdate;
        private readonly List<FreeRoamGroup> _freeRoamGroups = new List<FreeRoamGroup>();
        private const int FreeRoamTargetSampleAttempts = 6;

        /// <summary>
        /// Tracks a spawn group for free roam patrol behavior.
        /// </summary>
        private class FreeRoamGroup
        {
            public int spawnGroupId;
            public Vector3 freeRoamCenter;  // The center point for this group's free roam area
            public Vector3 lastTarget;
            public bool hasTarget;
            public float nextMoveTime;
            public float lastTargetSetTime;
        }

        // Color palette for factions
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

        public bool IsSpawning => _spawnRoutine != null;
        public int AgentsSpawned => _agentsSpawned;
        public int RemainingAgents => Mathf.Max(0, totalSpawnCount - _agentsSpawned);
        public int WavesSpawned => _wavesSpawned;
        public int RemainingWaves => maxWaves > 0 ? Mathf.Max(0, maxWaves - _wavesSpawned) : -1;

        private void OnValidate()
        {
            totalSpawnCount = Mathf.Max(1, totalSpawnCount);
            agentsPerWave = Mathf.Max(1, agentsPerWave);
            spawnRadius = Mathf.Max(0.1f, spawnRadius);
            minSpawnRadius = Mathf.Clamp(minSpawnRadius, 0f, spawnRadius - 0.1f);
            startDelay = Mathf.Max(0f, startDelay);
            spawnInterval = Mathf.Max(0.01f, spawnInterval);
            maxWaves = Mathf.Max(0, maxWaves);
            navMeshSearchRadius = Mathf.Max(0.1f, navMeshSearchRadius);

            // Free roam validation
            freeRoamRadius = Mathf.Max(1f, freeRoamRadius);
            freeRoamArrivalDistance = Mathf.Max(0.1f, freeRoamArrivalDistance);
            freeRoamMinTargetDistance = Mathf.Max(0f, freeRoamMinTargetDistance);
            freeRoamWaitTime = Mathf.Max(0f, freeRoamWaitTime);
            freeRoamUpdateInterval = Mathf.Clamp(freeRoamUpdateInterval, 0.05f, 2f);

            if (float.IsNaN(spawnFormationSpacing) || float.IsInfinity(spawnFormationSpacing))
                spawnFormationSpacing = 0f;

            // Regenerate preview points when settings change
            RegeneratePreviewPoints();
        }

        private void Start()
        {
            if (!Application.isPlaying)
                return;

            if (startSpawningOnStart)
                StartSpawning();
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (!enableFreeRoam || _freeRoamGroups.Count == 0)
                return;

            if (Time.time < _nextFreeRoamUpdate)
                return;

            _nextFreeRoamUpdate = Time.time + freeRoamUpdateInterval;
            UpdateFreeRoamGroups();
        }

        private void OnDisable()
        {
            StopSpawning();
            _freeRoamGroups.Clear();
        }

        /// <summary>
        /// Starts the spawning process based on the configured spawn mode.
        /// </summary>
        public void StartSpawning()
        {
            if (!Application.isPlaying)
                return;

            if (_spawnRoutine != null)
                return;

            if (!CanStartSpawning())
                return;

            _spawnRoutine = StartCoroutine(SpawnRoutine());
        }

        /// <summary>
        /// Stops the spawning process.
        /// </summary>
        public void StopSpawning()
        {
            if (_spawnRoutine != null)
            {
                StopCoroutine(_spawnRoutine);
                _spawnRoutine = null;
            }
        }

        /// <summary>
        /// Resets the spawn counter to allow spawning again.
        /// </summary>
        public void ResetSpawnCounter()
        {
            _agentsSpawned = 0;
        }

        /// <summary>
        /// Resets the wave counter to allow spawning again (for interval modes with maxWaves > 0).
        /// </summary>
        public void ResetWaveCounter()
        {
            _wavesSpawned = 0;
        }

        /// <summary>
        /// Resets both spawn and wave counters.
        /// </summary>
        public void ResetAllCounters()
        {
            _agentsSpawned = 0;
            _wavesSpawned = 0;
        }

        /// <summary>
        /// Manually spawn a wave at a random position around the target.
        /// </summary>
        public void SpawnWaveNow()
        {
            if (!Application.isPlaying)
                return;

            SpawnWave(agentsPerWave);
        }

        /// <summary>
        /// Manually spawn a single agent at a random position around the target.
        /// </summary>
        public void SpawnSingleNow()
        {
            if (!Application.isPlaying)
                return;

            SpawnWave(1);
        }

        private bool CanStartSpawning()
        {
            if (crowdController == null)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: No crowd controller assigned!", this);
                return false;
            }

            if (targetTransform == null)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: No target transform assigned!", this);
                return false;
            }

            // For AllAtOnce mode, check totalSpawnCount
            if (spawnMode == SpawnMode.AllAtOnce && _agentsSpawned >= totalSpawnCount)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: All agents already spawned ({totalSpawnCount}). Reset counter to start again.", this);
                return false;
            }

            // For interval modes, check maxWaves (if set)
            if (spawnMode != SpawnMode.AllAtOnce && maxWaves > 0 && _wavesSpawned >= maxWaves)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: Max waves reached ({maxWaves}). Reset wave counter to start again.", this);
                return false;
            }

            return true;
        }

        private IEnumerator SpawnRoutine()
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            switch (spawnMode)
            {
                case SpawnMode.AllAtOnce:
                    yield return SpawnAllAtOnce();
                    break;

                case SpawnMode.IntervalWaves:
                    yield return SpawnInWaves();
                    break;

                case SpawnMode.IndividualAtIntervals:
                    yield return SpawnIndividually();
                    break;
            }

            _spawnRoutine = null;
        }

        private IEnumerator SpawnAllAtOnce()
        {
            int remaining = totalSpawnCount - _agentsSpawned;
            
            // Spawn in batches at different random positions
            int batchSize = Mathf.Max(1, agentsPerWave);
            while (remaining > 0)
            {
                int toSpawn = Mathf.Min(batchSize, remaining);
                SpawnWave(toSpawn);
                remaining = totalSpawnCount - _agentsSpawned;
                
                // Small yield to prevent freezing on large spawns
                if (remaining > 0)
                    yield return null;
            }
        }

        private IEnumerator SpawnInWaves()
        {
            // For interval modes, maxWaves controls spawning (0 = unlimited)
            // totalSpawnCount is ignored - use maxWaves to limit
            while (true)
            {
                SpawnWave(agentsPerWave);
                _wavesSpawned++;

                // Check max waves limit (0 = unlimited)
                if (maxWaves > 0 && _wavesSpawned >= maxWaves)
                    break;

                yield return new WaitForSeconds(spawnInterval);
            }
        }

        private IEnumerator SpawnIndividually()
        {
            // For interval modes, maxWaves controls spawning (0 = unlimited)
            // Each individual spawn counts as one "wave"
            while (true)
            {
                SpawnWave(1);
                _wavesSpawned++;

                // Check max waves limit (0 = unlimited)
                if (maxWaves > 0 && _wavesSpawned >= maxWaves)
                    break;

                yield return new WaitForSeconds(spawnInterval);
            }
        }

        private void SpawnWave(int count)
        {
            if (!CanSpawn())
                return;

            Vector3 spawnPos = GetRandomSpawnPosition();
            if (spawnPos == Vector3.zero)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: Could not find valid NavMesh position!", this);
                return;
            }

            // Face toward the target
            Vector3 targetPos = targetTransform.position;
            Vector3 directionToTarget = (targetPos - spawnPos).normalized;
            Quaternion spawnRot = directionToTarget != Vector3.zero 
                ? Quaternion.LookRotation(directionToTarget) 
                : Quaternion.identity;

            List<int> spawnedIndices = crowdController.SpawnEnemyCrowd(
                enemyCrowdIndex,
                spawnPos,
                spawnRot,
                count,
                spawnFormation,
                spawnFormationSpacing
            );

            if (spawnedIndices != null && spawnedIndices.Count > 0)
            {
                _agentsSpawned += spawnedIndices.Count;
                HandlePostSpawnBehavior(spawnedIndices, spawnPos);
            }
            else
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: Failed to spawn agents!", this);
            }
        }

        private bool CanSpawn()
        {
            if (crowdController == null)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: No crowd controller assigned!", this);
                return false;
            }

            if (targetTransform == null)
            {
                Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: No target transform assigned!", this);
                return false;
            }

            return true;
        }

        private Vector3 GetRandomSpawnPosition()
        {
            Vector3 targetPos = targetTransform.position;
            bool requiresSurfaceValidation = RequiresSurfaceValidationForSpawn();
            
            // Try multiple attempts to find a valid position for the active navigation backend.
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector3 randomPos = GetRandomPointInRadius(targetPos);
                bool hasSampledPosition = TrySamplePositionForSpawn(randomPos, navMeshSearchRadius, out Vector3 sampledPosition);
                Vector3 candidate = hasSampledPosition ? sampledPosition : randomPos;

                if (requiresSurfaceValidation && !hasSampledPosition)
                    continue;

                // Verify the found position is still within our spawn radius constraints
                float distanceFromTarget = Vector3.Distance(new Vector3(candidate.x, targetPos.y, candidate.z), targetPos);
                if (distanceFromTarget >= minSpawnRadius && distanceFromTarget <= spawnRadius)
                    return candidate;
            }

            if (!requiresSurfaceValidation)
            {
                return GetRandomPointInRadius(targetPos);
            }

            // Fallback: try to find any valid sampled position near the target.
            Debug.LogWarning($"[SpawnAroundTarget] {gameObject.name}: Could not find valid navigation position in radius, trying near target.", this);
            if (TrySamplePositionForSpawn(targetPos, spawnRadius, out Vector3 fallbackPosition))
                return fallbackPosition;

            Debug.LogError($"[SpawnAroundTarget] {gameObject.name}: No valid navigation position found! Ensure required navigation data is available in this area.", this);
            return Vector3.zero;
        }

        private Vector3 GetRandomPointInRadius(Vector3 center)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance;

            if (uniformDistribution)
            {
                // Uniform distribution within the ring/circle
                float minR2 = minSpawnRadius * minSpawnRadius;
                float maxR2 = spawnRadius * spawnRadius;
                distance = Mathf.Sqrt(Random.Range(minR2, maxR2));
            }
            else
            {
                // Linear distribution (more agents near edges)
                distance = Random.Range(minSpawnRadius, spawnRadius);
            }

            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            return center + offset;
        }

        private void HandlePostSpawnBehavior(List<int> spawnedIndices, Vector3 spawnPosition)
        {
            if (spawnedIndices == null || spawnedIndices.Count == 0)
                return;

            // Get the spawn group ID from the first spawned agent
            int spawnGroupId = -1;
            var agents = crowdController.GetAgents();
            if (agents != null && spawnedIndices[0] >= 0 && spawnedIndices[0] < agents.Count)
            {
                spawnGroupId = agents[spawnedIndices[0]].spawnGroupId;
            }

            // If free roam is enabled, register the group for patrol behavior
            if (enableFreeRoam && spawnGroupId >= 0)
            {
                Vector3 freeRoamCenter = CalculateFreeRoamCenter(spawnPosition);
                
                var freeRoamGroup = new FreeRoamGroup
                {
                    spawnGroupId = spawnGroupId,
                    freeRoamCenter = freeRoamCenter,
                    hasTarget = false,
                    nextMoveTime = Time.time + freeRoamWaitTime
                };
                _freeRoamGroups.Add(freeRoamGroup);
                return; // Free roam handles movement, skip other post-spawn behaviors
            }

            if (autoAttackTarget && targetTransform != null)
            {
                if (spawnGroupId >= 0)
                {
                    crowdController.SetAttackTargetForSpawnGroup(spawnGroupId, targetTransform);
                }
                else
                {
                    crowdController.SetAttackTargetForAgents(spawnedIndices, targetTransform);
                }
                return;
            }

            if (autoMoveToTarget && targetTransform != null)
            {
                Vector3 destination = targetTransform.position;
                
                if (spawnGroupId >= 0)
                {
                    crowdController.SetDestinationForSpawnGroup(spawnGroupId, destination, true);
                }
            }
        }

        /// <summary>
        /// Calculates the center point for free roam based on the configured mode.
        /// </summary>
        private Vector3 CalculateFreeRoamCenter(Vector3 spawnPosition)
        {
            switch (freeRoamCenterMode)
            {
                case FreeRoamCenterMode.SpawnPosition:
                    return spawnPosition;

                case FreeRoamCenterMode.TargetPositionAtSpawn:
                    return targetTransform != null ? targetTransform.position : spawnPosition;

                case FreeRoamCenterMode.CustomTransform:
                    return freeRoamCenterTransform != null ? freeRoamCenterTransform.position : spawnPosition;

                case FreeRoamCenterMode.FollowTarget:
                    // This mode updates dynamically, but we still need an initial value
                    return targetTransform != null ? targetTransform.position : spawnPosition;

                default:
                    return spawnPosition;
            }
        }

        /// <summary>
        /// Gets the current free roam center for a group (handles FollowTarget mode).
        /// </summary>
        private Vector3 GetCurrentFreeRoamCenter(FreeRoamGroup group)
        {
            switch (freeRoamCenterMode)
            {
                case FreeRoamCenterMode.FollowTarget:
                    return targetTransform != null ? targetTransform.position : group.freeRoamCenter;

                case FreeRoamCenterMode.CustomTransform:
                    return freeRoamCenterTransform != null ? freeRoamCenterTransform.position : group.freeRoamCenter;

                default:
                    // SpawnPosition and TargetPositionAtSpawn use the locked center
                    return group.freeRoamCenter;
            }
        }

        #region Free Roam Update

        private void UpdateFreeRoamGroups()
        {
            if (crowdController == null)
                return;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return;

            // Process each free roam group
            for (int i = _freeRoamGroups.Count - 1; i >= 0; i--)
            {
                var group = _freeRoamGroups[i];
                
                // Check if group still has alive agents
                if (!HasAliveAgentsInGroup(agents, group.spawnGroupId))
                {
                    _freeRoamGroups.RemoveAt(i);
                    continue;
                }

                // Check if group is engaged in combat
                if (pauseFreeRoamWhenEngaged && IsGroupEngaged(agents, group.spawnGroupId))
                {
                    // Reset target when combat ends
                    group.hasTarget = false;
                    continue;
                }

                // Update free roam center if following target
                Vector3 currentCenter = GetCurrentFreeRoamCenter(group);

                // Check if we need a new target
                if (!group.hasTarget || Time.time >= group.nextMoveTime)
                {
                    // Check if agents have arrived at current target
                    if (group.hasTarget && !HaveAgentsArrived(agents, group))
                    {
                        // Still moving to target, don't change yet
                        continue;
                    }

                    // Pick a new patrol target
                    Vector3 newTarget = GetRandomFreeRoamTarget(currentCenter, group);
                    if (newTarget != Vector3.zero)
                    {
                        SetFreeRoamDestination(group, newTarget);
                    }
                }
            }
        }

        private bool HasAliveAgentsInGroup(List<CrowdAgent> agents, int spawnGroupId)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && !agent.isDead && agent.spawnGroupId == spawnGroupId)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsGroupEngaged(List<CrowdAgent> agents, int spawnGroupId)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && !agent.isDead && agent.spawnGroupId == spawnGroupId)
                {
                    if (agent.combatState == CrowdCombatState.Attacking ||
                        agent.combatState == CrowdCombatState.Approach ||
                        agent.navigationState == AgentNavigationState.ChasingTarget ||
                        agent.isAggroed)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool HaveAgentsArrived(List<CrowdAgent> agents, FreeRoamGroup group)
        {
            if (!group.hasTarget)
                return true;

            // Minimum travel time check - don't check arrival too soon after setting destination
            // This prevents flickering from premature arrival detection
            float minTravelTime = Mathf.Max(0.2f, freeRoamUpdateInterval);
            float timeSinceTargetSet = Time.time - group.lastTargetSetTime;
            if (timeSinceTargetSet < minTravelTime)
                return false;

            // Use spawn groups for more efficient lookup
            var spawnGroups = crowdController.GetSpawnGroups();
            if (spawnGroups == null || !spawnGroups.TryGetValue(group.spawnGroupId, out var spawnGroup))
                return false;

            bool hasAlive = false;

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
                    continue;

                // If path is still being calculated, not arrived yet
                if (nav.Capabilities.SupportsPathPending && nav.PathPending)
                    return false;

                // Compute effective arrival distance (must be at least navigation stopping distance)
                float arrivalDistance = freeRoamArrivalDistance;
                if (nav.StoppingDistance + 0.1f > arrivalDistance)
                {
                    arrivalDistance = nav.StoppingDistance + 0.1f;
                }

                if (nav.HasPath)
                {
                    if (nav.RemainingDistance > arrivalDistance)
                        return false;
                }
                else
                {
                    // No path - check direct distance to target
                    Vector3 currentPos = agent.WorldPosition;
                    Vector3 targetPos = group.lastTarget;
                    float distSqr = (currentPos - targetPos).sqrMagnitude;
                    float arrivalSqr = arrivalDistance * arrivalDistance;
                    if (distSqr > arrivalSqr)
                        return false;
                }
            }

            return hasAlive;
        }

        private Vector3 GetRandomFreeRoamTarget(Vector3 center, FreeRoamGroup group)
        {
            float minTargetDist = freeRoamMinTargetDistance > 0f 
                ? freeRoamMinTargetDistance 
                : freeRoamArrivalDistance * 2f;
            bool requiresSurfaceValidation = RequiresSurfaceValidationForGroup(group.spawnGroupId);

            for (int attempt = 0; attempt < FreeRoamTargetSampleAttempts; attempt++)
            {
                // Get random point within free roam radius
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * freeRoamRadius; // Uniform distribution

                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * distance,
                    0f,
                    Mathf.Sin(angle) * distance
                );

                Vector3 target = center + offset;
                bool hasSampledPosition = TrySamplePositionForGroup(group.spawnGroupId, target, navMeshSearchRadius, out Vector3 sampledPosition);
                Vector3 candidate = hasSampledPosition ? sampledPosition : target;

                if (requiresSurfaceValidation && !hasSampledPosition)
                    continue;

                // Ensure minimum distance from last target
                if (group.hasTarget && Vector3.Distance(candidate, group.lastTarget) < minTargetDist)
                    continue;

                return candidate;
            }

            // Fallback to center if no valid point found
            if (TrySamplePositionForGroup(group.spawnGroupId, center, freeRoamRadius, out Vector3 fallbackPosition))
                return fallbackPosition;

            if (!requiresSurfaceValidation)
                return center;

            return Vector3.zero;
        }

        private void SetFreeRoamDestination(FreeRoamGroup group, Vector3 destination)
        {
            group.lastTarget = destination;
            group.hasTarget = true;
            group.lastTargetSetTime = Time.time;
            group.nextMoveTime = Time.time + freeRoamWaitTime + Random.Range(0f, freeRoamWaitTime * 0.5f);

            // useManualCommand=true prevents formation system from overriding patrol destinations
            // This is the key fix for patrol flickering - ManualCommand state is protected
            crowdController.SetDestinationForSpawnGroup(group.spawnGroupId, destination, useFormationForFreeRoam, useManualCommand: true);
        }

        private bool TrySamplePositionForSpawn(Vector3 position, float maxDistance, out Vector3 sampledPosition)
        {
            if (TrySamplePositionFromFactionAgents(position, maxDistance, out sampledPosition))
                return true;

            return EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(position, maxDistance, out sampledPosition);
        }

        private bool TrySamplePositionFromFactionAgents(Vector3 position, float maxDistance, out Vector3 sampledPosition)
        {
            sampledPosition = position;
            if (crowdController == null)
                return false;

            var agents = crowdController.GetAgents();
            if (agents == null)
                return false;

            for (int i = 0; i < agents.Count; i++)
            {
                var navAgent = agents[i]?.navigationAgent;
                if (navAgent == null)
                    continue;

                if (agents[i].enemyCrowdIndex != enemyCrowdIndex)
                    continue;

                if (navAgent.SamplePosition(position, out sampledPosition, maxDistance))
                    return true;
            }

            return false;
        }

        private bool TrySamplePositionForGroup(int spawnGroupId, Vector3 position, float maxDistance, out Vector3 sampledPosition)
        {
            sampledPosition = position;
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

                if (navAgent.SamplePosition(position, out sampledPosition, maxDistance))
                    return true;
            }

            return false;
        }

        private bool RequiresSurfaceValidationForSpawn()
        {
            if (TryGetFactionCapabilities(out NavigationCapabilities capabilities))
                return capabilities.RequiresSurfaceValidationForControl;

            return false;
        }

        private bool RequiresSurfaceValidationForGroup(int spawnGroupId)
        {
            if (crowdController != null)
            {
                var spawnGroups = crowdController.GetSpawnGroups();
                var agents = crowdController.GetAgents();
                if (spawnGroups != null && agents != null && spawnGroups.TryGetValue(spawnGroupId, out var spawnGroup))
                {
                    foreach (int agentIndex in spawnGroup.agentIndices)
                    {
                        if (agentIndex < 0 || agentIndex >= agents.Count)
                            continue;

                        var navAgent = agents[agentIndex]?.navigationAgent;
                        if (navAgent == null)
                            continue;

                        return navAgent.Capabilities.RequiresSurfaceValidationForControl;
                    }
                }
            }

            return RequiresSurfaceValidationForSpawn();
        }

        private bool TryGetFactionCapabilities(out NavigationCapabilities capabilities)
        {
            capabilities = default;
            if (crowdController == null)
                return false;

            var agents = crowdController.GetAgents();
            if (agents != null)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    var navAgent = agents[i]?.navigationAgent;
                    if (navAgent == null || agents[i].enemyCrowdIndex != enemyCrowdIndex)
                        continue;

                    capabilities = navAgent.Capabilities;
                    return true;
                }
            }

            if (crowdController.EnemyCrowds != null &&
                enemyCrowdIndex >= 0 &&
                enemyCrowdIndex < crowdController.EnemyCrowds.Count &&
                crowdController.EnemyCrowds[enemyCrowdIndex] != null)
            {
                capabilities = crowdController.EnemyCrowds[enemyCrowdIndex].navigationType == NavigationType.UnityNavMesh
                    ? NavigationCapabilities.UnityNavMesh
                    : (crowdController.EnemyCrowds[enemyCrowdIndex].navigationType == NavigationType.FlowField
                        ? NavigationCapabilities.FlowField
                        : NavigationCapabilities.AStar);
                return true;
            }

            return false;
        }

        private static bool CanControlNavigationAgent(INavigationAgent navigationAgent)
        {
            if (navigationAgent == null)
                return false;

            if (!navigationAgent.Capabilities.RequiresSurfaceValidationForControl)
                return true;

            return navigationAgent.IsOnNavMesh;
        }

        #endregion

        private void RegeneratePreviewPoints()
        {
            _previewPoints.Clear();
            
            if (targetTransform == null || previewPointCount <= 0)
                return;

            // Use a fixed seed based on object instance for consistent preview
            Random.State oldState = Random.state;
            Random.InitState(_previewSeed + GetInstanceID());

            Vector3 center = targetTransform.position;
            for (int i = 0; i < previewPointCount; i++)
            {
                _previewPoints.Add(GetRandomPointInRadius(center));
            }

            Random.state = oldState;
        }

        public Color GetGizmoColor()
        {
            if (customGizmoColor != Color.clear)
                return customGizmoColor;

            return FactionColors[Mathf.Abs(enemyCrowdIndex) % FactionColors.Length];
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            if (targetTransform == null)
                return;

            Color gizmoColor = GetGizmoColor();
            Vector3 targetPos = targetTransform.position;

            // Draw outer radius
            Gizmos.color = gizmoColor;
            DrawWireCircle(targetPos, Quaternion.identity, spawnRadius, 48);

            // Draw inner radius (min spawn distance)
            if (minSpawnRadius > 0f)
            {
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f);
                DrawWireCircle(targetPos, Quaternion.identity, minSpawnRadius, 32);
            }

            // Draw free roam radius if enabled
            if (enableFreeRoam)
            {
                Vector3 freeRoamCenter = GetFreeRoamGizmoCenter();
                Color freeRoamColor = new Color(0.2f, 0.8f, 0.2f, 0.5f); // Green for free roam
                
                Gizmos.color = freeRoamColor;
                DrawWireCircle(freeRoamCenter, Quaternion.identity, freeRoamRadius, 36);
                
                // Draw free roam center marker
                Gizmos.DrawWireSphere(freeRoamCenter, 0.3f);
                
                // If center is different from target, draw connection line
                if (freeRoamCenterMode == FreeRoamCenterMode.CustomTransform && freeRoamCenterTransform != null)
                {
                    Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
                    Gizmos.DrawLine(targetPos, freeRoamCenter);
                }
            }

            // Draw target marker
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(targetPos, 0.5f);

            // Draw cross at target
            float crossSize = 1f;
            Gizmos.DrawLine(targetPos - Vector3.right * crossSize, targetPos + Vector3.right * crossSize);
            Gizmos.DrawLine(targetPos - Vector3.forward * crossSize, targetPos + Vector3.forward * crossSize);

            // Draw preview spawn points
            if (previewPointCount > 0)
            {
                // Regenerate if needed (e.g., target moved)
                if (_previewPoints.Count != previewPointCount)
                {
                    RegeneratePreviewPoints();
                }

                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.6f);
                foreach (Vector3 point in _previewPoints)
                {
                    // Offset preview points to current target position
                    Vector3 adjustedPoint = point;
                    Gizmos.DrawWireSphere(adjustedPoint, 0.25f);
                    
                    // Draw direction arrow toward target
                    Vector3 dirToTarget = (targetPos - adjustedPoint).normalized;
                    if (dirToTarget != Vector3.zero)
                    {
                        Gizmos.DrawLine(adjustedPoint, adjustedPoint + dirToTarget * 0.8f);
                    }
                }
            }

            // Draw active free roam groups at runtime
            if (Application.isPlaying && enableFreeRoam && _freeRoamGroups.Count > 0)
            {
                DrawFreeRoamGroupGizmos();
            }

            // Draw spawn info text position (for Handles in editor script)
            DrawSpawnInfoGizmo(targetPos, gizmoColor);
        }

        private Vector3 GetFreeRoamGizmoCenter()
        {
            switch (freeRoamCenterMode)
            {
                case FreeRoamCenterMode.CustomTransform:
                    return freeRoamCenterTransform != null ? freeRoamCenterTransform.position : targetTransform.position;

                case FreeRoamCenterMode.FollowTarget:
                case FreeRoamCenterMode.TargetPositionAtSpawn:
                    return targetTransform.position;

                case FreeRoamCenterMode.SpawnPosition:
                default:
                    // In editor preview, show at target position since we don't know spawn pos yet
                    return targetTransform.position;
            }
        }

        private void DrawFreeRoamGroupGizmos()
        {
            Color groupColor = new Color(0.2f, 1f, 0.4f, 0.7f);
            
            foreach (var group in _freeRoamGroups)
            {
                // Draw the group's free roam center
                Vector3 center = GetCurrentFreeRoamCenter(group);
                Gizmos.color = groupColor;
                Gizmos.DrawWireSphere(center, 0.5f);
                
                // Draw current target if has one
                if (group.hasTarget)
                {
                    Gizmos.color = new Color(1f, 1f, 0.2f, 0.8f);
                    Gizmos.DrawWireSphere(group.lastTarget, 0.4f);
                    Gizmos.DrawLine(center, group.lastTarget);
                }
            }
        }

        private void DrawSpawnInfoGizmo(Vector3 position, Color color)
        {
            // Draw a small indicator showing spawn status
            Vector3 infoPos = position + Vector3.up * 2f;
            Gizmos.color = color;
            Gizmos.DrawWireCube(infoPos, Vector3.one * 0.3f);
        }

        private void DrawWireCircle(Vector3 center, Quaternion rotation, float radius, int segments)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;

            float angleStep = 360f / segments;
            Vector3 prevPoint = center + right * radius;

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos || targetTransform == null)
                return;

            Color gizmoColor = GetGizmoColor();
            Vector3 targetPos = targetTransform.position;

            // Draw filled disc preview when selected
            UnityEditor.Handles.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.1f);
            UnityEditor.Handles.DrawSolidDisc(targetPos, Vector3.up, spawnRadius);

            if (minSpawnRadius > 0f)
            {
                UnityEditor.Handles.color = new Color(0f, 0f, 0f, 0.2f);
                UnityEditor.Handles.DrawSolidDisc(targetPos, Vector3.up, minSpawnRadius);
            }

            // Draw free roam area when selected
            if (enableFreeRoam)
            {
                Vector3 freeRoamCenter = GetFreeRoamGizmoCenter();
                UnityEditor.Handles.color = new Color(0.2f, 0.8f, 0.2f, 0.08f);
                UnityEditor.Handles.DrawSolidDisc(freeRoamCenter, Vector3.up, freeRoamRadius);
            }

            // Draw label with spawn info
            string modeText = spawnMode switch
            {
                SpawnMode.AllAtOnce => "All at Once",
                SpawnMode.IntervalWaves => $"Waves of {agentsPerWave}",
                SpawnMode.IndividualAtIntervals => "Individual",
                _ => "Unknown"
            };

            string freeRoamText = enableFreeRoam 
                ? $"\nFree Roam: {freeRoamRadius:F1}m ({freeRoamCenterMode})" 
                : "";

            string label = $"Spawn Around Target\n" +
                          $"Total: {totalSpawnCount} | Mode: {modeText}\n" +
                          $"Radius: {minSpawnRadius:F1} - {spawnRadius:F1}m{freeRoamText}";

            UnityEditor.Handles.Label(targetPos + Vector3.up * 3f, label);
        }
#endif
    }
}
