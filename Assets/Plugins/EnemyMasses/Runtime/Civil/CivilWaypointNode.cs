using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Civil
{
    /// <summary>
    /// Individual waypoint node in the civil navigation system.
    /// Place these in your scene to define navigation paths for civil NPCs.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Civil/Waypoint Node")]
    public class CivilWaypointNode : MonoBehaviour
    {
        [Header("Waypoint Type")]
        [Tooltip("Type of behavior at this waypoint.")]
        public CivilWaypointType waypointType = CivilWaypointType.Path;
        
        [Header("Connections")]
        [Tooltip("Connected waypoints that agents can travel to from here.")]
        public List<CivilWaypointConnection> connections = new List<CivilWaypointConnection>();
        
        [Header("Path Settings")]
        [Tooltip("Radius within which agents consider themselves 'arrived' at this waypoint.")]
        [Range(0.5f, 10f)]
        public float arrivalRadius = 2f;
        
        [Tooltip("Default channel width for connections from this waypoint.")]
        [Range(0f, 10f)]
        public float defaultChannelWidth = 2f;
        
        [Header("Explore Settings (for Explore type)")]
        [Tooltip("Radius within which agents can freely explore.")]
        [Range(1f, 50f)]
        public float exploreRadius = 10f;
        
        [Tooltip("Override explore time for this waypoint (0 = use faction default).")]
        public Vector2 exploreTimeOverride = Vector2.zero;
        
        [Header("Activity Settings (for Activity type)")]
        [Tooltip("Activities available at this waypoint.")]
        public List<CivilActivityDefinition> availableActivities = new List<CivilActivityDefinition>();
        
        [Tooltip("Exact position where agent stands during activity.")]
        public Transform activityPosition;
        
        [Tooltip("Direction agent faces during activity.")]
        public Transform activityFacing;
        
        [Tooltip("Maximum agents performing activities here simultaneously.")]
        [Min(1)]
        public int maxConcurrentActivities = 1;

        [Header("Lean Settings (for Lean type)")]
        [Tooltip("Lean spots agents can reserve. Position/rotation come from each marker.")]
        public List<CivilLeanSpot> leanSpots = new List<CivilLeanSpot>();

        [Header("Sit Settings (for Sit type)")]
        [Tooltip("Sit spots agents can reserve. Position/rotation come from each marker.")]
        public List<CivilSitSpot> sitSpots = new List<CivilSitSpot>();
        
        [Header("Spawn Settings (for Spawn type)")]
        [Tooltip("List of factions that can spawn at this waypoint with individual timing settings.")]
        public List<CivilSpawnEntry> spawnEntries = new List<CivilSpawnEntry>();
        
        [Tooltip("How spawn positions are determined.")]
        public CivilSpawnMode spawnMode = CivilSpawnMode.FixedRadius;
        
        [Tooltip("(FixedRadius mode) Spawn radius around this waypoint. Agents will spawn at random positions within this radius.")]
        [Min(0f)]
        public float spawnRadius = 3f;
        
        [Tooltip("(CameraFrustumEdge mode) Distance outside the camera frustum where agents spawn.")]
        [Range(1f, 30f)]
        public float frustumEdgeDistance = 5f;
        
        [Tooltip("(CameraFrustumEdge mode) Minimum distance from camera to spawn agents.")]
        [Min(5f)]
        public float frustumMinCameraDistance = 10f;
        
        [Tooltip("(CameraFrustumEdge mode) Maximum distance from camera to spawn agents.")]
        [Min(10f)]
        public float frustumMaxCameraDistance = 50f;
        
        [Tooltip("If true, spawned agents immediately start navigating. If false, they idle briefly.")]
        public bool spawnAndNavigate = true;
        
        [Tooltip("Maximum total agents spawned from this waypoint that can be alive (0 = unlimited).")]
        [Min(0)]
        public int maxTotalSpawnedAlive = 0;
        
        // Legacy field for backward compatibility - converts to spawnEntries on Awake
        [SerializeField, HideInInspector]
        private float spawnRatePerMinute = 5f;
        
        [Header("Despawn Settings (for Despawn type)")]
        [Tooltip("Agents will be removed when they reach this point.")]
        public bool immediateRemoval = false;
        
        [Header("Tags & Filtering")]
        [Tooltip("Optional tags for this waypoint (for filtering which agents can use it).")]
        public string[] tags;
        
        [Tooltip("Faction indices that can use this waypoint (-1 = all factions).")]
        public int[] allowedFactions = new int[] { -1 };
        
        [Header("Gizmo Settings")]
        public Color gizmoColor = Color.cyan;
        public bool showConnections = true;
        public bool showRadius = true;
        
        // Runtime state
        private int _currentActivityCount;
        private int _totalSpawnedAliveCount;
        private List<int> _agentsAtWaypoint = new List<int>();
        
        /// <summary>Unique ID assigned at runtime for fast lookups.</summary>
        public int RuntimeId { get; internal set; } = -1;
        
        /// <summary>Cached NavMesh position for this waypoint.</summary>
        public Vector3 NavMeshPosition { get; private set; }
        
        /// <summary>Is this waypoint valid (on NavMesh)?</summary>
        public bool IsValid { get; private set; }
        
        /// <summary>Number of agents currently at this waypoint.</summary>
        public int AgentCount => _agentsAtWaypoint.Count;
        
        /// <summary>Can another agent start an activity here?</summary>
        public bool CanStartActivity => _currentActivityCount < maxConcurrentActivities;
        
        /// <summary>Returns true if this is a spawn waypoint and can spawn (has any ready entry).</summary>
        public bool CanSpawn => waypointType == CivilWaypointType.Spawn && GetNextSpawnEntry() != null;
        
        /// <summary>Total agents spawned from this waypoint that are currently alive.</summary>
        public int TotalSpawnedAlive => _totalSpawnedAliveCount;
        
        private void Awake()
        {
            MigrateLegacySpawnSettings();
            ValidateNavMeshPosition();
            InitializeSpawnTimers();
        }
        
        /// <summary>
        /// Migrates old spawnRatePerMinute to new spawnEntries system.
        /// </summary>
        private void MigrateLegacySpawnSettings()
        {
            // If we have the old setting but no entries, create a default entry
            if (waypointType == CivilWaypointType.Spawn && 
                spawnEntries.Count == 0 && 
                spawnRatePerMinute > 0)
            {
                float intervalFromRate = 60f / Mathf.Max(0.1f, spawnRatePerMinute);
                spawnEntries.Add(new CivilSpawnEntry
                {
                    factionIndex = -1, // Any faction
                    intervalMode = SpawnIntervalMode.Fixed,
                    fixedInterval = intervalFromRate,
                    minInterval = intervalFromRate * 0.5f,
                    maxInterval = intervalFromRate * 1.5f,
                    spawnCount = 1,
                    enabled = true
                });
            }
        }
        
        /// <summary>
        /// Initializes spawn timers for all entries.
        /// </summary>
        public void InitializeSpawnTimers()
        {
            float currentTime = Time.time;
            foreach (var entry in spawnEntries)
            {
                entry.lastSpawnTime = currentTime;
                // Stagger initial spawns randomly
                entry.nextSpawnTime = currentTime + Random.Range(0f, entry.GetNextInterval());
            }
        }
        
        /// <summary>
        /// Validates and caches the NavMesh position for this waypoint.
        /// </summary>
        public void ValidateNavMeshPosition()
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                NavMeshPosition = hit.position;
                IsValid = true;
            }
            else
            {
                NavMeshPosition = transform.position;
                IsValid = false;
                Debug.LogWarning($"[CivilWaypoint] Waypoint '{name}' is not near a NavMesh!", this);
            }
        }
        
        /// <summary>
        /// Gets a random position within the channel to this waypoint.
        /// </summary>
        public Vector3 GetChannelPosition(CivilWaypointNode fromWaypoint)
        {
            if (fromWaypoint == null)
                return NavMeshPosition;
            
            // Find the connection from the source waypoint
            float channelWidth = defaultChannelWidth;
            foreach (var conn in fromWaypoint.connections)
            {
                if (conn.target == this)
                {
                    channelWidth = conn.channelWidth;
                    break;
                }
            }
            
            if (channelWidth <= 0f)
                return NavMeshPosition;
            
            // Calculate perpendicular offset direction
            Vector3 direction = (NavMeshPosition - fromWaypoint.NavMeshPosition).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
            
            // Random offset within channel
            float offset = Random.Range(-channelWidth * 0.5f, channelWidth * 0.5f);
            Vector3 targetPos = NavMeshPosition + perpendicular * offset;
            
            // Ensure position is on NavMesh
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, channelWidth, NavMesh.AllAreas))
            {
                return hit.position;
            }
            
            return NavMeshPosition;
        }
        
        /// <summary>
        /// Gets a random exploration position within the explore radius.
        /// </summary>
        public Vector3 GetExplorePosition()
        {
            if (waypointType != CivilWaypointType.Explore)
                return NavMeshPosition;
            
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * exploreRadius;
                Vector3 targetPos = NavMeshPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
                
                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }
            
            return NavMeshPosition;
        }
        
        /// <summary>
        /// Gets a random spawn position based on the spawn mode.
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            if (waypointType != CivilWaypointType.Spawn)
                return NavMeshPosition;
            
            switch (spawnMode)
            {
                case CivilSpawnMode.CameraFrustumEdge:
                    return GetCameraFrustumSpawnPosition();
                    
                case CivilSpawnMode.FixedRadius:
                default:
                    return GetFixedRadiusSpawnPosition();
            }
        }
        
        /// <summary>
        /// Gets a spawn position within the fixed radius around the waypoint.
        /// </summary>
        private Vector3 GetFixedRadiusSpawnPosition()
        {
            if (spawnRadius <= 0f)
                return NavMeshPosition;
            
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
                Vector3 targetPos = NavMeshPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
                
                // Use a reasonable sample distance (5 units) regardless of spawn radius
                // This ensures we find a valid NavMesh position near the random point
                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }
            
            return NavMeshPosition;
        }
        
        /// <summary>
        /// Gets a spawn position just outside the camera frustum.
        /// Agents will appear off-screen and walk into view naturally.
        /// </summary>
        private Vector3 GetCameraFrustumSpawnPosition()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                // Fallback to fixed radius if no camera
                return GetFixedRadiusSpawnPosition();
            }
            
            // Try multiple times to find a valid position
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Vector3 spawnPos = CalculateFrustumEdgePosition(mainCamera);
                
                // Validate on NavMesh
                if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                {
                    // Additional check: make sure it's actually outside the frustum
                    Vector3 viewportPoint = mainCamera.WorldToViewportPoint(hit.position);
                    bool isOutsideFrustum = viewportPoint.x < -0.05f || viewportPoint.x > 1.05f ||
                                            viewportPoint.y < -0.05f || viewportPoint.y > 1.05f ||
                                            viewportPoint.z < 0;
                    
                    if (isOutsideFrustum)
                    {
                        // Check it's not inside geometry using a simple raycast down
                        if (Physics.Raycast(hit.position + Vector3.up * 2f, Vector3.down, out RaycastHit groundHit, 5f))
                        {
                            // Valid spawn position
                            return hit.position;
                        }
                        else
                        {
                            // No ground hit needed if we're on NavMesh
                            return hit.position;
                        }
                    }
                }
            }
            
            // Fallback: if we couldn't find a valid frustum position, use fixed radius
            return GetFixedRadiusSpawnPosition();
        }
        
        /// <summary>
        /// Calculates a position just outside the camera frustum edge.
        /// Works correctly with all camera types: top-down, isometric, first-person, third-person.
        /// </summary>
        private Vector3 CalculateFrustumEdgePosition(Camera camera)
        {
            // Get the ground plane Y position (use waypoint's Y as reference)
            float groundY = transform.position.y;
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, groundY, 0));
            
            // Get camera orientation info
            Vector3 camForward = camera.transform.forward;
            Vector3 camRight = camera.transform.right;
            float cameraPitch = Vector3.Angle(camForward, Vector3.down) - 90f; // -90 = looking straight down, 0 = horizontal, 90 = looking up
            
            // Determine which edges are valid for spawning based on camera angle
            // For horizontal/upward-looking cameras, top edge rays won't hit ground
            // For cameras looking steeply down, bottom edge is very close
            List<int> validEdges = new List<int>();
            
            // Left and right edges are almost always valid
            validEdges.Add(0); // Left
            validEdges.Add(1); // Right
            
            // Top edge is valid if camera is looking downward enough (pitch < 30°)
            if (cameraPitch < 30f)
            {
                validEdges.Add(2); // Top
            }
            
            // Bottom edge is valid if camera isn't looking too steeply down
            // (otherwise bottom edge hits ground too close to camera)
            if (cameraPitch > -60f)
            {
                validEdges.Add(3); // Bottom
            }
            
            // Pick a random valid edge
            int edge = validEdges[Random.Range(0, validEdges.Count)];
            
            // Random position along the edge
            float edgePosition = Random.Range(0.2f, 0.8f);
            
            // Calculate viewport position just outside the chosen edge
            float viewportX, viewportY;
            float edgeOffset = 0.15f + Random.Range(0f, 0.1f);
            
            switch (edge)
            {
                case 0: // Left edge
                    viewportX = -edgeOffset;
                    viewportY = edgePosition;
                    break;
                case 1: // Right edge
                    viewportX = 1f + edgeOffset;
                    viewportY = edgePosition;
                    break;
                case 2: // Top edge
                    viewportX = edgePosition;
                    viewportY = 1f + edgeOffset;
                    break;
                case 3: // Bottom edge
                default:
                    viewportX = edgePosition;
                    viewportY = -edgeOffset;
                    break;
            }
            
            // Cast a ray from the camera through the viewport point
            Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0));
            
            // Check if ray will hit ground at a reasonable distance
            if (groundPlane.Raycast(ray, out float hitDistance))
            {
                // Validate the hit distance is within acceptable range
                if (hitDistance > 0 && hitDistance < frustumMaxCameraDistance * 3f)
                {
                    Vector3 worldPos = ray.GetPoint(hitDistance);
                    float distFromCamera = Vector3.Distance(camera.transform.position, worldPos);
                    
                    // If within acceptable range, use it
                    if (distFromCamera >= frustumMinCameraDistance && distFromCamera <= frustumMaxCameraDistance)
                    {
                        return worldPos;
                    }
                    
                    // Otherwise, adjust along the direction from camera to hit point
                    Vector3 cameraGroundPos = camera.transform.position;
                    cameraGroundPos.y = groundY;
                    
                    Vector3 dirFromCamera = (worldPos - cameraGroundPos).normalized;
                    float targetDist = Random.Range(frustumMinCameraDistance, frustumMaxCameraDistance);
                    
                    return cameraGroundPos + dirFromCamera * targetDist;
                }
            }
            
            // Fallback: calculate position using camera orientation projected onto ground
            // This handles cases where the ray doesn't hit ground (looking up/horizontal)
            return CalculateFallbackSpawnPosition(camera, edge, groundY);
        }
        
        /// <summary>
        /// Calculates a fallback spawn position when ray-plane intersection fails.
        /// Uses camera orientation to determine spawn position outside visible area.
        /// </summary>
        private Vector3 CalculateFallbackSpawnPosition(Camera camera, int preferredEdge, float groundY)
        {
            // Project camera forward onto ground plane
            Vector3 camForwardFlat = camera.transform.forward;
            camForwardFlat.y = 0;
            
            // Handle edge case: camera looking straight down or up
            if (camForwardFlat.sqrMagnitude < 0.01f)
            {
                float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                camForwardFlat = new Vector3(Mathf.Cos(randomAngle), 0, Mathf.Sin(randomAngle));
            }
            else
            {
                camForwardFlat.Normalize();
            }
            
            Vector3 camRightFlat = Vector3.Cross(Vector3.up, camForwardFlat).normalized;
            
            // Camera position projected to ground
            Vector3 cameraGroundPos = camera.transform.position;
            cameraGroundPos.y = groundY;
            
            // Calculate spawn distance
            float spawnDist = Random.Range(frustumMinCameraDistance, frustumMaxCameraDistance);
            
            // Calculate position based on edge
            // For first/third-person: forward is "ahead", sides are left/right
            Vector3 spawnOffset;
            
            switch (preferredEdge)
            {
                case 0: // Left - spawn to the left of visible area
                    spawnOffset = camForwardFlat * spawnDist * 0.7f - camRightFlat * spawnDist * 0.8f;
                    break;
                case 1: // Right - spawn to the right of visible area
                    spawnOffset = camForwardFlat * spawnDist * 0.7f + camRightFlat * spawnDist * 0.8f;
                    break;
                case 2: // Top/Forward - spawn ahead of visible area
                    spawnOffset = camForwardFlat * spawnDist * 1.2f + camRightFlat * Random.Range(-0.3f, 0.3f) * spawnDist;
                    break;
                case 3: // Bottom/Behind - spawn behind (useful for rear spawns)
                default:
                    // For horizontal cameras, "bottom" of screen is close to camera
                    // Spawn to the side instead
                    float sideDir = Random.value > 0.5f ? 1f : -1f;
                    spawnOffset = camForwardFlat * spawnDist * 0.3f + camRightFlat * sideDir * spawnDist * 0.9f;
                    break;
            }
            
            return cameraGroundPos + spawnOffset;
        }
        
        /// <summary>
        /// Selects the next waypoint based on selection mode and weights.
        /// </summary>
        public CivilWaypointNode SelectNextWaypoint(WaypointSelectionMode mode, CivilWaypointNode previousWaypoint = null, string[] agentTags = null)
        {
            if (connections.Count == 0)
                return null;
            
            // Filter valid connections
            List<CivilWaypointConnection> validConnections = new List<CivilWaypointConnection>();
            foreach (var conn in connections)
            {
                if (conn.target == null || !conn.target.IsValid)
                    continue;
                
                // Optionally skip where we came from to avoid backtracking
                if (previousWaypoint != null && conn.target == previousWaypoint && connections.Count > 1)
                    continue;
                
                // Check required tags
                if (conn.requiredTags != null && conn.requiredTags.Length > 0)
                {
                    if (agentTags == null || !HasRequiredTags(agentTags, conn.requiredTags))
                        continue;
                }
                
                validConnections.Add(conn);
            }
            
            if (validConnections.Count == 0)
            {
                // Fallback: allow going back if no other options
                foreach (var conn in connections)
                {
                    if (conn.target != null && conn.target.IsValid)
                        validConnections.Add(conn);
                }
            }
            
            if (validConnections.Count == 0)
                return null;
            
            switch (mode)
            {
                case WaypointSelectionMode.Random:
                    return validConnections[Random.Range(0, validConnections.Count)].target;
                
                case WaypointSelectionMode.Sequential:
                    // Find next in sequence (based on array order)
                    int currentIndex = 0;
                    for (int i = 0; i < connections.Count; i++)
                    {
                        if (connections[i].target == previousWaypoint)
                        {
                            currentIndex = i;
                            break;
                        }
                    }
                    int nextIndex = (currentIndex + 1) % validConnections.Count;
                    return validConnections[nextIndex].target;
                
                case WaypointSelectionMode.LeastCrowded:
                    CivilWaypointNode leastCrowded = validConnections[0].target;
                    int minAgents = leastCrowded.AgentCount;
                    foreach (var conn in validConnections)
                    {
                        if (conn.target.AgentCount < minAgents)
                        {
                            minAgents = conn.target.AgentCount;
                            leastCrowded = conn.target;
                        }
                    }
                    return leastCrowded;
                
                case WaypointSelectionMode.WeightedRandom:
                default:
                    return SelectWeightedRandom(validConnections);
            }
        }
        
        private CivilWaypointNode SelectWeightedRandom(List<CivilWaypointConnection> validConnections)
        {
            float totalWeight = 0f;
            foreach (var conn in validConnections)
            {
                totalWeight += conn.weight;
            }
            
            float randomValue = Random.Range(0f, totalWeight);
            float currentWeight = 0f;
            
            foreach (var conn in validConnections)
            {
                currentWeight += conn.weight;
                if (randomValue <= currentWeight)
                {
                    return conn.target;
                }
            }
            
            return validConnections[validConnections.Count - 1].target;
        }
        
        private bool HasRequiredTags(string[] agentTags, string[] requiredTags)
        {
            foreach (string required in requiredTags)
            {
                bool found = false;
                foreach (string agentTag in agentTags)
                {
                    if (agentTag == required)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }
        
        /// <summary>
        /// Selects a random activity from available activities.
        /// </summary>
        public CivilActivityDefinition SelectActivity()
        {
            if (availableActivities.Count == 0)
                return null;
            
            float totalWeight = 0f;
            foreach (var activity in availableActivities)
            {
                totalWeight += activity.selectionWeight;
            }
            
            float randomValue = Random.Range(0f, totalWeight);
            float currentWeight = 0f;
            
            foreach (var activity in availableActivities)
            {
                currentWeight += activity.selectionWeight;
                if (randomValue <= currentWeight)
                {
                    return activity;
                }
            }
            
            return availableActivities[availableActivities.Count - 1];
        }
        
        /// <summary>
        /// Registers an agent as present at this waypoint.
        /// </summary>
        public void RegisterAgent(int agentIndex)
        {
            if (!_agentsAtWaypoint.Contains(agentIndex))
                _agentsAtWaypoint.Add(agentIndex);
        }
        
        /// <summary>
        /// Unregisters an agent from this waypoint.
        /// </summary>
        public void UnregisterAgent(int agentIndex)
        {
            _agentsAtWaypoint.Remove(agentIndex);
        }
        
        /// <summary>
        /// Call when an activity starts at this waypoint.
        /// </summary>
        public void OnActivityStarted()
        {
            _currentActivityCount++;
        }
        
        /// <summary>
        /// Call when an activity ends at this waypoint.
        /// </summary>
        public void OnActivityEnded()
        {
            _currentActivityCount = Mathf.Max(0, _currentActivityCount - 1);
        }
        
        /// <summary>
        /// Gets the next spawn entry that is ready to spawn.
        /// Uses weighted random selection among ready entries.
        /// </summary>
        /// <returns>A spawn entry ready to spawn, or null if none available.</returns>
        public CivilSpawnEntry GetNextSpawnEntry()
        {
            if (waypointType != CivilWaypointType.Spawn)
                return null;
            
            // Check global spawn limit
            if (maxTotalSpawnedAlive > 0 && _totalSpawnedAliveCount >= maxTotalSpawnedAlive)
                return null;
            
            float currentTime = Time.time;
            List<CivilSpawnEntry> readyEntries = new List<CivilSpawnEntry>();
            float totalWeight = 0f;
            
            foreach (var entry in spawnEntries)
            {
                if (entry.CanSpawn(currentTime))
                {
                    readyEntries.Add(entry);
                    totalWeight += entry.weight;
                }
            }
            
            if (readyEntries.Count == 0)
                return null;
            
            // Weighted random selection
            float randomValue = Random.Range(0f, totalWeight);
            float currentWeight = 0f;
            
            foreach (var entry in readyEntries)
            {
                currentWeight += entry.weight;
                if (randomValue <= currentWeight)
                {
                    return entry;
                }
            }
            
            return readyEntries[readyEntries.Count - 1];
        }
        
        /// <summary>
        /// Gets all spawn entries that are ready to spawn right now.
        /// </summary>
        public List<CivilSpawnEntry> GetReadySpawnEntries()
        {
            List<CivilSpawnEntry> readyEntries = new List<CivilSpawnEntry>();
            
            if (waypointType != CivilWaypointType.Spawn)
                return readyEntries;
            
            if (maxTotalSpawnedAlive > 0 && _totalSpawnedAliveCount >= maxTotalSpawnedAlive)
                return readyEntries;
            
            float currentTime = Time.time;
            foreach (var entry in spawnEntries)
            {
                if (entry.CanSpawn(currentTime))
                {
                    readyEntries.Add(entry);
                }
            }
            
            return readyEntries;
        }
        
        /// <summary>
        /// Gets the time until the next spawn entry becomes ready.
        /// </summary>
        /// <returns>Time in seconds until next spawn, or float.MaxValue if no spawning possible.</returns>
        public float GetTimeUntilNextSpawn()
        {
            if (waypointType != CivilWaypointType.Spawn || spawnEntries.Count == 0)
                return float.MaxValue;
            
            if (maxTotalSpawnedAlive > 0 && _totalSpawnedAliveCount >= maxTotalSpawnedAlive)
                return float.MaxValue;
            
            float currentTime = Time.time;
            float minWait = float.MaxValue;
            
            foreach (var entry in spawnEntries)
            {
                if (!entry.enabled)
                    continue;
                
                if (entry.maxAliveAgents > 0 && entry.currentAliveCount >= entry.maxAliveAgents)
                    continue;
                
                float waitTime = entry.nextSpawnTime - currentTime;
                if (waitTime < minWait)
                    minWait = waitTime;
            }
            
            return Mathf.Max(0f, minWait);
        }
        
        /// <summary>
        /// Call when an agent spawns at this waypoint from a specific entry.
        /// </summary>
        /// <param name="entry">The spawn entry that produced this agent. If null, updates global count only.</param>
        public void OnAgentSpawned(CivilSpawnEntry entry = null)
        {
            _totalSpawnedAliveCount++;
            entry?.OnSpawned(Time.time);
        }
        
        /// <summary>
        /// Call when an agent that was spawned from this waypoint despawns or dies.
        /// </summary>
        /// <param name="entry">The spawn entry that produced this agent. If null, updates global count only.</param>
        public void OnAgentDespawned(CivilSpawnEntry entry = null)
        {
            _totalSpawnedAliveCount = Mathf.Max(0, _totalSpawnedAliveCount - 1);
            entry?.OnDespawned();
        }
        
        /// <summary>
        /// Adds a spawn entry for a specific faction.
        /// </summary>
        public CivilSpawnEntry AddSpawnEntry(int factionIndex = -1, SpawnIntervalMode intervalMode = SpawnIntervalMode.RandomRange)
        {
            var entry = new CivilSpawnEntry
            {
                factionIndex = factionIndex,
                intervalMode = intervalMode,
                enabled = true
            };
            
            entry.nextSpawnTime = Time.time + entry.GetNextInterval();
            spawnEntries.Add(entry);
            return entry;
        }
        
        /// <summary>
        /// Removes a spawn entry.
        /// </summary>
        public bool RemoveSpawnEntry(CivilSpawnEntry entry)
        {
            return spawnEntries.Remove(entry);
        }
        
        /// <summary>
        /// Checks if a faction can use this waypoint.
        /// </summary>
        public bool IsFactionAllowed(int factionIndex)
        {
            if (allowedFactions == null || allowedFactions.Length == 0)
                return true;
            
            foreach (int allowed in allowedFactions)
            {
                if (allowed == -1 || allowed == factionIndex)
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Gets a lean spot by index (null-safe).
        /// </summary>
        public CivilLeanSpot GetLeanSpot(int index)
        {
            if (leanSpots == null || index < 0 || index >= leanSpots.Count)
                return null;
            return leanSpots[index];
        }

        /// <summary>
        /// Attempts to reserve a free lean spot for the given agent.
        /// </summary>
        public bool TryReserveLeanSpot(int agentIndex, out CivilLeanSpot spot, out int spotIndex)
        {
            spot = null;
            spotIndex = -1;
            if (waypointType != CivilWaypointType.Lean || leanSpots == null || leanSpots.Count == 0)
                return false;

            for (int i = 0; i < leanSpots.Count; i++)
            {
                var candidate = leanSpots[i];
                if (candidate == null)
                    continue;

                if (!candidate.IsOccupied || candidate.occupantAgentIndex == agentIndex)
                {
                    candidate.occupantAgentIndex = agentIndex;
                    spot = candidate;
                    spotIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Releases any lean spot held by the agent.
        /// </summary>
        public void ReleaseLeanSpot(int agentIndex)
        {
            if (leanSpots == null)
                return;

            foreach (var spot in leanSpots)
            {
                spot?.Release(agentIndex);
            }
        }

        /// <summary>
        /// Gets a sit spot by index (null-safe).
        /// </summary>
        public CivilSitSpot GetSitSpot(int index)
        {
            if (sitSpots == null || index < 0 || index >= sitSpots.Count)
                return null;
            return sitSpots[index];
        }

        /// <summary>
        /// Attempts to reserve a free sit spot for the given agent.
        /// </summary>
        public bool TryReserveSitSpot(int agentIndex, out CivilSitSpot spot, out int spotIndex)
        {
            spot = null;
            spotIndex = -1;
            if (waypointType != CivilWaypointType.Sit || sitSpots == null || sitSpots.Count == 0)
                return false;

            for (int i = 0; i < sitSpots.Count; i++)
            {
                var candidate = sitSpots[i];
                if (candidate == null)
                    continue;

                if (!candidate.IsOccupied || candidate.occupantAgentIndex == agentIndex)
                {
                    candidate.occupantAgentIndex = agentIndex;
                    spot = candidate;
                    spotIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Releases any sit spot held by the agent.
        /// </summary>
        public void ReleaseSitSpot(int agentIndex)
        {
            if (sitSpots == null)
                return;

            foreach (var spot in sitSpots)
            {
                spot?.Release(agentIndex);
            }
        }
        
        /// <summary>
        /// Creates bidirectional connections automatically.
        /// </summary>
        public void CreateBidirectionalConnections()
        {
            foreach (var conn in connections)
            {
                if (conn.bidirectional && conn.target != null)
                {
                    // Check if reverse connection exists
                    bool hasReverse = false;
                    foreach (var reverseConn in conn.target.connections)
                    {
                        if (reverseConn.target == this)
                        {
                            hasReverse = true;
                            break;
                        }
                    }
                    
                    if (!hasReverse)
                    {
                        conn.target.connections.Add(new CivilWaypointConnection
                        {
                            target = this,
                            weight = conn.weight,
                            channelWidth = conn.channelWidth,
                            bidirectional = true
                        });
                    }
                }
            }
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Color color = GetGizmoColor();
            Gizmos.color = color;
            
            // Draw waypoint marker based on type
            switch (waypointType)
            {
                case CivilWaypointType.Path:
                    Gizmos.DrawWireSphere(transform.position, 0.5f);
                    break;
                case CivilWaypointType.Explore:
                    Gizmos.DrawWireSphere(transform.position, 0.75f);
                    if (showRadius)
                    {
                        Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);
                        DrawWireCircle(transform.position, exploreRadius, 32);
                    }
                    break;
                case CivilWaypointType.Despawn:
                    Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
                    break;
                case CivilWaypointType.Activity:
                    DrawActivityGizmo(color);
                    break;
                case CivilWaypointType.Spawn:
                    DrawSpawnGizmo(color);
                    break;
            }
            
            // Draw arrival radius
            if (showRadius && arrivalRadius > 0)
            {
                Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
                DrawWireCircle(transform.position, arrivalRadius, 24);
            }
            
            // Draw connections
            if (showConnections)
            {
                foreach (var conn in connections)
                {
                    if (conn.target != null)
                    {
                        DrawConnectionGizmo(conn, color);
                        // Always draw channel width when showConnections is true
                        if (conn.channelWidth > 0)
                        {
                            DrawChannelPreview(conn, color);
                        }
                    }
                }
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            Color color = GetGizmoColor();
            
            // Highlight selected waypoint
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, 0.6f);
            
            // Draw channel width preview for connections
            foreach (var conn in connections)
            {
                if (conn.target != null && conn.channelWidth > 0)
                {
                    DrawChannelPreview(conn, color);
                }
            }
        }
        
        private Color GetGizmoColor()
        {
            if (gizmoColor != Color.cyan)
                return gizmoColor;
            
            return waypointType switch
            {
                CivilWaypointType.Path => Color.cyan,
                CivilWaypointType.Explore => Color.green,
                CivilWaypointType.Despawn => Color.red,
                CivilWaypointType.Activity => Color.yellow,
                CivilWaypointType.Spawn => Color.magenta,
                _ => Color.white
            };
        }
        
        private void DrawActivityGizmo(Color color)
        {
            // Draw diamond shape for activity
            float size = 0.5f;
            Vector3 pos = transform.position;
            
            Gizmos.DrawLine(pos + Vector3.up * size, pos + Vector3.right * size);
            Gizmos.DrawLine(pos + Vector3.right * size, pos - Vector3.up * size);
            Gizmos.DrawLine(pos - Vector3.up * size, pos - Vector3.right * size);
            Gizmos.DrawLine(pos - Vector3.right * size, pos + Vector3.up * size);
            
            Gizmos.DrawLine(pos + Vector3.up * size, pos + Vector3.forward * size);
            Gizmos.DrawLine(pos + Vector3.forward * size, pos - Vector3.up * size);
            Gizmos.DrawLine(pos - Vector3.up * size, pos - Vector3.forward * size);
            Gizmos.DrawLine(pos - Vector3.forward * size, pos + Vector3.up * size);
            
            // Draw activity position and facing
            if (activityPosition != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(activityPosition.position, 0.3f);
            }
            
            if (activityFacing != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(activityPosition != null ? activityPosition.position : pos, 
                              activityFacing.forward * 1.5f);
            }
        }
        
        private void DrawSpawnGizmo(Color color)
        {
            // Draw star shape for spawn
            float size = 0.5f;
            Vector3 pos = transform.position;
            
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Gizmos.DrawLine(pos, pos + dir * size);
            }
            Gizmos.DrawLine(pos, pos + Vector3.up * size);
            
            // Draw spawn area based on mode
            if (showRadius)
            {
                if (spawnMode == CivilSpawnMode.FixedRadius && spawnRadius > 0)
                {
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
                    DrawWireCircle(transform.position, spawnRadius, 24);
                }
                else if (spawnMode == CivilSpawnMode.CameraFrustumEdge)
                {
                    // Draw indicator showing this is camera-based spawning
                    Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
                    
                    // Draw camera icon-like shape
                    float camSize = 0.6f;
                    Vector3 camPos = pos + Vector3.up * 0.5f;
                    
                    // Camera body
                    Gizmos.DrawWireCube(camPos, new Vector3(camSize, camSize * 0.6f, camSize * 0.4f));
                    
                    // Lens
                    Vector3 lensPos = camPos + Vector3.forward * camSize * 0.3f;
                    Gizmos.DrawWireSphere(lensPos, camSize * 0.15f);
                    
                    // Draw text label in scene view (handled by editor)
                }
            }
        }
        
        private void DrawConnectionGizmo(CivilWaypointConnection conn, Color color)
        {
            Vector3 start = transform.position;
            Vector3 end = conn.target.transform.position;
            Vector3 direction = (end - start).normalized;
            
            // Offset for bidirectional visibility
            if (conn.bidirectional)
            {
                Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized * 0.1f;
                start += perpendicular;
                end += perpendicular;
            }
            
            // Draw main line
            Gizmos.color = color;
            Gizmos.DrawLine(start, end);
            
            // Draw arrow head
            Vector3 arrowPos = Vector3.Lerp(start, end, 0.7f);
            Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
            
            Gizmos.DrawLine(arrowPos, arrowPos - direction * 0.3f + right * 0.15f);
            Gizmos.DrawLine(arrowPos, arrowPos - direction * 0.3f - right * 0.15f);
        }
        
        private void DrawChannelPreview(CivilWaypointConnection conn, Color color)
        {
            Vector3 start = transform.position;
            Vector3 end = conn.target.transform.position;
            Vector3 direction = (end - start).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
            float halfWidth = conn.channelWidth * 0.5f;
            
            Gizmos.color = new Color(color.r, color.g, color.b, 0.15f);
            
            // Draw channel edges
            Gizmos.DrawLine(start + perpendicular * halfWidth, end + perpendicular * halfWidth);
            Gizmos.DrawLine(start - perpendicular * halfWidth, end - perpendicular * halfWidth);
            
            // Draw width indicators
            Gizmos.DrawLine(start + perpendicular * halfWidth, start - perpendicular * halfWidth);
            Gizmos.DrawLine(end + perpendicular * halfWidth, end - perpendicular * halfWidth);
        }
        
        private void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + Vector3.right * radius;
            
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }
        }
#endif
    }
}
