using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Arawn.EnemyMasses.Runtime.Civil
{
    /// <summary>
    /// Scene-level manager for civil NPC waypoints.
    /// Maintains waypoint graph and provides efficient lookups.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Civil/Waypoint Manager")]
    [DefaultExecutionOrder(-100)]
    public class CivilWaypointManager : MonoBehaviour
    {
        [Header("Auto-Discovery")]
        [Tooltip("Automatically find all waypoints in children on start.")]
        public bool autoDiscoverWaypoints = true;
        
        [Tooltip("Include waypoints from entire scene (not just children).")]
        public bool searchEntireScene = false;
        
        [Header("Validation")]
        [Tooltip("Validate all waypoints on start (checks NavMesh, connections).")]
        public bool validateOnStart = true;
        
        [Tooltip("Create bidirectional connections automatically.")]
        public bool ensureBidirectional = true;
        
        [Header("Performance")]
        [Tooltip("Use spatial hashing for faster waypoint lookups.")]
        public bool useSpatialHashing = true;
        
        [Tooltip("Cell size for spatial hash grid.")]
        [Range(5f, 50f)]
        public float spatialHashCellSize = 20f;
        
        [Header("Debug")]
        [Tooltip("Show debug info in console.")]
        public bool debugMode = false;
        
        [Tooltip("Draw waypoint network in scene view.")]
        public bool drawNetworkGizmo = true;
        
        // All registered waypoints
        private List<CivilWaypointNode> _allWaypoints = new List<CivilWaypointNode>();
        
        // Categorized waypoint lists for fast access
        private List<CivilWaypointNode> _pathWaypoints = new List<CivilWaypointNode>();
        private List<CivilWaypointNode> _exploreWaypoints = new List<CivilWaypointNode>();
        private List<CivilWaypointNode> _despawnWaypoints = new List<CivilWaypointNode>();
        private List<CivilWaypointNode> _activityWaypoints = new List<CivilWaypointNode>();
        private List<CivilWaypointNode> _leanWaypoints = new List<CivilWaypointNode>();
        private List<CivilWaypointNode> _sitWaypoints = new List<CivilWaypointNode>();
        private List<CivilWaypointNode> _spawnWaypoints = new List<CivilWaypointNode>();
        
        // Spatial hash for position-based lookups
        private Dictionary<long, List<CivilWaypointNode>> _spatialHash;
        
        // Singleton (optional - can have multiple managers for different areas)
        private static CivilWaypointManager _instance;
        public static CivilWaypointManager Instance => _instance;
        
        /// <summary>All registered waypoints.</summary>
        public IReadOnlyList<CivilWaypointNode> AllWaypoints => _allWaypoints;
        
        /// <summary>Waypoints of type Path.</summary>
        public IReadOnlyList<CivilWaypointNode> PathWaypoints => _pathWaypoints;
        
        /// <summary>Waypoints of type Explore.</summary>
        public IReadOnlyList<CivilWaypointNode> ExploreWaypoints => _exploreWaypoints;
        
        /// <summary>Waypoints of type Despawn.</summary>
        public IReadOnlyList<CivilWaypointNode> DespawnWaypoints => _despawnWaypoints;
        
        /// <summary>Waypoints of type Activity.</summary>
        public IReadOnlyList<CivilWaypointNode> ActivityWaypoints => _activityWaypoints;
        
        /// <summary>Waypoints of type Spawn.</summary>
        public IReadOnlyList<CivilWaypointNode> SpawnWaypoints => _spawnWaypoints;
        
        /// <summary>Is the manager initialized?</summary>
        public bool IsInitialized { get; private set; }
        
        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Debug.LogWarning($"[CivilWaypointManager] Multiple managers detected. Using '{_instance.name}'.", this);
            }
        }
        
        private void Start()
        {
            Initialize();
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        /// <summary>
        /// Initializes the waypoint manager, discovering and validating waypoints.
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
                return;
            
            if (autoDiscoverWaypoints)
            {
                DiscoverWaypoints();
            }
            
            if (ensureBidirectional)
            {
                CreateBidirectionalConnections();
            }
            
            if (validateOnStart)
            {
                ValidateWaypoints();
            }
            
            if (useSpatialHashing)
            {
                BuildSpatialHash();
            }
            
            IsInitialized = true;
        }
        
        /// <summary>
        /// Discovers all waypoints in the scene or children.
        /// </summary>
        public void DiscoverWaypoints()
        {
            _allWaypoints.Clear();
            _pathWaypoints.Clear();
            _exploreWaypoints.Clear();
            _despawnWaypoints.Clear();
            _activityWaypoints.Clear();
            _leanWaypoints.Clear();
            _sitWaypoints.Clear();
            _spawnWaypoints.Clear();
            
            CivilWaypointNode[] foundWaypoints;
            
            if (searchEntireScene)
            {
                foundWaypoints = Object.FindObjectsByType<CivilWaypointNode>(FindObjectsSortMode.None);
            }
            else
            {
                foundWaypoints = GetComponentsInChildren<CivilWaypointNode>(true);
            }
            
            for (int i = 0; i < foundWaypoints.Length; i++)
            {
                CivilWaypointNode waypoint = foundWaypoints[i];
                RegisterWaypoint(waypoint, i);
            }
        }
        
        /// <summary>
        /// Registers a waypoint with the manager.
        /// </summary>
        public void RegisterWaypoint(CivilWaypointNode waypoint, int? runtimeId = null)
        {
            if (waypoint == null || _allWaypoints.Contains(waypoint))
                return;
            
            waypoint.RuntimeId = runtimeId ?? _allWaypoints.Count;
            _allWaypoints.Add(waypoint);
            
            // Categorize
            switch (waypoint.waypointType)
            {
                case CivilWaypointType.Path:
                    _pathWaypoints.Add(waypoint);
                    break;
                case CivilWaypointType.Explore:
                    _exploreWaypoints.Add(waypoint);
                    break;
                case CivilWaypointType.Despawn:
                    _despawnWaypoints.Add(waypoint);
                    break;
                case CivilWaypointType.Activity:
                    _activityWaypoints.Add(waypoint);
                    break;
                case CivilWaypointType.Lean:
                    _leanWaypoints.Add(waypoint);
                    break;
                case CivilWaypointType.Sit:
                    _sitWaypoints.Add(waypoint);
                    break;
                case CivilWaypointType.Spawn:
                    _spawnWaypoints.Add(waypoint);
                    break;
            }
            
            // Update spatial hash if already built
            if (_spatialHash != null)
            {
                AddToSpatialHash(waypoint);
            }
        }
        
        /// <summary>
        /// Unregisters a waypoint from the manager.
        /// </summary>
        public void UnregisterWaypoint(CivilWaypointNode waypoint)
        {
            if (waypoint == null)
                return;
            
            _allWaypoints.Remove(waypoint);
            _pathWaypoints.Remove(waypoint);
            _exploreWaypoints.Remove(waypoint);
            _despawnWaypoints.Remove(waypoint);
            _activityWaypoints.Remove(waypoint);
            _leanWaypoints.Remove(waypoint);
            _sitWaypoints.Remove(waypoint);
            _spawnWaypoints.Remove(waypoint);
            
            if (_spatialHash != null)
            {
                RemoveFromSpatialHash(waypoint);
            }
        }
        
        /// <summary>
        /// Validates all waypoints (checks NavMesh positions, connections).
        /// </summary>
        public void ValidateWaypoints()
        {
            int invalidCount = 0;
            int disconnectedCount = 0;
            
            foreach (var waypoint in _allWaypoints)
            {
                waypoint.ValidateNavMeshPosition();
                
                if (!waypoint.IsValid)
                {
                    invalidCount++;
                    Debug.LogWarning($"[CivilWaypointManager] Waypoint '{waypoint.name}' is not on NavMesh!", waypoint);
                }
                
                // Count disconnected waypoints (except spawn/despawn which can be endpoints)
                // Note: Disconnected waypoints are valid when using FreeRoam navigation mode
                if (waypoint.connections.Count == 0 && 
                    waypoint.waypointType != CivilWaypointType.Spawn &&
                    waypoint.waypointType != CivilWaypointType.Despawn)
                {
                    disconnectedCount++;
                }
                
                // Validate connections
                for (int i = waypoint.connections.Count - 1; i >= 0; i--)
                {
                    var conn = waypoint.connections[i];
                    if (conn.target == null)
                    {
                        Debug.LogWarning($"[CivilWaypointManager] Waypoint '{waypoint.name}' has null connection at index {i}.", waypoint);
                        waypoint.connections.RemoveAt(i);
                    }
                    else if (conn.target == waypoint)
                    {
                        Debug.LogWarning($"[CivilWaypointManager] Waypoint '{waypoint.name}' has self-connection.", waypoint);
                        waypoint.connections.RemoveAt(i);
                    }
                }
            }
            
            // Only warn about invalid waypoints - disconnected ones are fine for FreeRoam
            if (invalidCount > 0)
            {
                Debug.LogWarning($"[CivilWaypointManager] Validation: {invalidCount} waypoints not on NavMesh.");
            }
        }
        
        /// <summary>
        /// Creates bidirectional connections for all waypoints that have bidirectional enabled.
        /// </summary>
        public void CreateBidirectionalConnections()
        {
            foreach (var waypoint in _allWaypoints)
            {
                waypoint.CreateBidirectionalConnections();
            }
        }
        
        /// <summary>
        /// Finds the nearest waypoint to a position.
        /// </summary>
        public CivilWaypointNode FindNearestWaypoint(Vector3 position, CivilWaypointType? typeFilter = null, int factionIndex = -1)
        {
            if (useSpatialHashing && _spatialHash != null)
            {
                return FindNearestWaypointSpatial(position, typeFilter, factionIndex);
            }
            
            return FindNearestWaypointLinear(position, typeFilter, factionIndex);
        }
        
        private CivilWaypointNode FindNearestWaypointLinear(Vector3 position, CivilWaypointType? typeFilter, int factionIndex)
        {
            IEnumerable<CivilWaypointNode> searchList = _allWaypoints;
            
            if (typeFilter.HasValue)
            {
                searchList = GetWaypointsByType(typeFilter.Value);
            }
            
            CivilWaypointNode nearest = null;
            float nearestDistSq = float.MaxValue;
            
            foreach (var waypoint in searchList)
            {
                if (!waypoint.IsValid)
                    continue;
                
                if (factionIndex >= 0 && !waypoint.IsFactionAllowed(factionIndex))
                    continue;
                
                float distSq = (waypoint.NavMeshPosition - position).sqrMagnitude;
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = waypoint;
                }
            }
            
            return nearest;
        }
        
        private CivilWaypointNode FindNearestWaypointSpatial(Vector3 position, CivilWaypointType? typeFilter, int factionIndex)
        {
            // Start with the cell containing the position, then expand outward
            int searchRadius = 0;
            const int maxSearchRadius = 5;
            
            while (searchRadius <= maxSearchRadius)
            {
                CivilWaypointNode nearest = null;
                float nearestDistSq = float.MaxValue;
                
                // Check all cells within search radius
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    for (int dz = -searchRadius; dz <= searchRadius; dz++)
                    {
                        long hash = GetSpatialHash(position.x + dx * spatialHashCellSize, position.z + dz * spatialHashCellSize);
                        
                        if (_spatialHash.TryGetValue(hash, out List<CivilWaypointNode> waypoints))
                        {
                            foreach (var waypoint in waypoints)
                            {
                                if (!waypoint.IsValid)
                                    continue;
                                
                                if (typeFilter.HasValue && waypoint.waypointType != typeFilter.Value)
                                    continue;
                                
                                if (factionIndex >= 0 && !waypoint.IsFactionAllowed(factionIndex))
                                    continue;
                                
                                float distSq = (waypoint.NavMeshPosition - position).sqrMagnitude;
                                if (distSq < nearestDistSq)
                                {
                                    nearestDistSq = distSq;
                                    nearest = waypoint;
                                }
                            }
                        }
                    }
                }
                
                if (nearest != null)
                    return nearest;
                
                searchRadius++;
            }
            
            // Fallback to linear search
            return FindNearestWaypointLinear(position, typeFilter, factionIndex);
        }
        
        /// <summary>
        /// Finds waypoints within a radius of a position.
        /// </summary>
        public List<CivilWaypointNode> FindWaypointsInRadius(Vector3 position, float radius, CivilWaypointType? typeFilter = null, int factionIndex = -1)
        {
            List<CivilWaypointNode> result = new List<CivilWaypointNode>();
            float radiusSq = radius * radius;
            
            IEnumerable<CivilWaypointNode> searchList = _allWaypoints;
            if (typeFilter.HasValue)
            {
                searchList = GetWaypointsByType(typeFilter.Value);
            }
            
            foreach (var waypoint in searchList)
            {
                if (!waypoint.IsValid)
                    continue;
                
                if (factionIndex >= 0 && !waypoint.IsFactionAllowed(factionIndex))
                    continue;
                
                float distSq = (waypoint.NavMeshPosition - position).sqrMagnitude;
                if (distSq <= radiusSq)
                {
                    result.Add(waypoint);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Gets a random spawn waypoint that can spawn.
        /// </summary>
        public CivilWaypointNode GetAvailableSpawnWaypoint(int factionIndex = -1)
        {
            List<CivilWaypointNode> available = new List<CivilWaypointNode>();
            
            foreach (var waypoint in _spawnWaypoints)
            {
                if (waypoint.CanSpawn && waypoint.IsValid)
                {
                    if (factionIndex < 0 || waypoint.IsFactionAllowed(factionIndex))
                    {
                        available.Add(waypoint);
                    }
                }
            }
            
            if (available.Count == 0)
                return null;
            
            return available[Random.Range(0, available.Count)];
        }
        
        /// <summary>
        /// Gets waypoints by type.
        /// </summary>
        public IReadOnlyList<CivilWaypointNode> GetWaypointsByType(CivilWaypointType type)
        {
            return type switch
            {
                CivilWaypointType.Path => _pathWaypoints,
                CivilWaypointType.Explore => _exploreWaypoints,
                CivilWaypointType.Despawn => _despawnWaypoints,
                CivilWaypointType.Activity => _activityWaypoints,
                CivilWaypointType.Lean => _leanWaypoints,
                CivilWaypointType.Sit => _sitWaypoints,
                CivilWaypointType.Spawn => _spawnWaypoints,
                _ => _allWaypoints
            };
        }
        
        /// <summary>
        /// Gets a waypoint by runtime ID.
        /// </summary>
        public CivilWaypointNode GetWaypointById(int runtimeId)
        {
            if (runtimeId >= 0 && runtimeId < _allWaypoints.Count)
            {
                return _allWaypoints[runtimeId];
            }
            return null;
        }
        
        #region Spatial Hashing
        
        private void BuildSpatialHash()
        {
            _spatialHash = new Dictionary<long, List<CivilWaypointNode>>();
            
            foreach (var waypoint in _allWaypoints)
            {
                AddToSpatialHash(waypoint);
            }
        }
        
        private void AddToSpatialHash(CivilWaypointNode waypoint)
        {
            long hash = GetSpatialHash(waypoint.transform.position.x, waypoint.transform.position.z);
            
            if (!_spatialHash.TryGetValue(hash, out List<CivilWaypointNode> list))
            {
                list = new List<CivilWaypointNode>();
                _spatialHash[hash] = list;
            }
            
            if (!list.Contains(waypoint))
            {
                list.Add(waypoint);
            }
        }
        
        private void RemoveFromSpatialHash(CivilWaypointNode waypoint)
        {
            long hash = GetSpatialHash(waypoint.transform.position.x, waypoint.transform.position.z);
            
            if (_spatialHash.TryGetValue(hash, out List<CivilWaypointNode> list))
            {
                list.Remove(waypoint);
            }
        }
        
        private long GetSpatialHash(float x, float z)
        {
            int ix = Mathf.FloorToInt(x / spatialHashCellSize);
            int iz = Mathf.FloorToInt(z / spatialHashCellSize);
            return ((long)ix << 32) | (uint)iz;
        }
        
        #endregion
        
        private void LogWaypointStats()
        {
            Debug.Log($"[CivilWaypointManager] Stats:\n" +
                     $"  Total: {_allWaypoints.Count}\n" +
                     $"  Path: {_pathWaypoints.Count}\n" +
                     $"  Explore: {_exploreWaypoints.Count}\n" +
                     $"  Activity: {_activityWaypoints.Count}\n" +
                     $"  Spawn: {_spawnWaypoints.Count}\n" +
                     $"  Despawn: {_despawnWaypoints.Count}");
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawNetworkGizmo || _allWaypoints == null)
                return;
            
            // Draw all connections
            HashSet<(CivilWaypointNode, CivilWaypointNode)> drawnConnections = new HashSet<(CivilWaypointNode, CivilWaypointNode)>();
            
            foreach (var waypoint in _allWaypoints)
            {
                foreach (var conn in waypoint.connections)
                {
                    if (conn.target == null)
                        continue;
                    
                    // Avoid drawing bidirectional connections twice
                    var key = waypoint.GetInstanceID() < conn.target.GetInstanceID() 
                        ? (waypoint, conn.target) 
                        : (conn.target, waypoint);
                    
                    if (drawnConnections.Contains(key))
                        continue;
                    
                    drawnConnections.Add(key);
                    
                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                    Gizmos.DrawLine(waypoint.transform.position, conn.target.transform.position);
                }
            }
        }
#endif
    }
}
