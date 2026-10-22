using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Enemy Masses-owned hit validation result to avoid hard compile-time coupling
    /// to specific Networking Core versions.
    /// </summary>
    [Serializable]
    public struct HitValidationResult
    {
        public bool isValid;
        public HitRejectReason reason;
        public uint targetNetworkId;
        public Vector3 hitPoint;
        public Vector3 historicalPosition;
        public Bounds historicalBounds;
        public float distanceFromBounds;
        public NetworkTimestamp clientTimestamp;
        public NetworkTimestamp serverTimestamp;
        public string hitZoneName;
        public float damageMultiplier;
    }

    /// <summary>
    /// Local reject reasons mirrored from Networking Core.
    /// </summary>
    public enum HitRejectReason
    {
        None,
        EntityNotFound,
        EntityInactive,
        OutOfRange,
        RayMissed,
        TimestampTooOld,
        TimestampInFuture,
        NotAllowed,
        Other
    }

    /// <summary>
    /// Lightweight Enemy Masses timestamp wrapper used for lag-comp snapshots.
    /// Kept local so this assembly compiles without Networking Core.
    /// </summary>
    [Serializable]
    public struct NetworkTimestamp : IComparable<NetworkTimestamp>, IEquatable<NetworkTimestamp>
    {
        [SerializeField] private double m_Seconds;

        public NetworkTimestamp(double seconds)
        {
            m_Seconds = seconds;
        }

        public double Seconds => m_Seconds;

        public static NetworkTimestamp FromSeconds(double seconds) => new NetworkTimestamp(seconds);

        public int CompareTo(NetworkTimestamp other) => m_Seconds.CompareTo(other.m_Seconds);

        public bool Equals(NetworkTimestamp other) => m_Seconds.Equals(other.m_Seconds);

        public override bool Equals(object obj) => obj is NetworkTimestamp other && Equals(other);

        public override int GetHashCode() => m_Seconds.GetHashCode();

        public override string ToString() => m_Seconds.ToString("F6");

        public static bool operator <(NetworkTimestamp left, NetworkTimestamp right) => left.m_Seconds < right.m_Seconds;

        public static bool operator >(NetworkTimestamp left, NetworkTimestamp right) => left.m_Seconds > right.m_Seconds;

        public static bool operator <=(NetworkTimestamp left, NetworkTimestamp right) => left.m_Seconds <= right.m_Seconds;

        public static bool operator >=(NetworkTimestamp left, NetworkTimestamp right) => left.m_Seconds >= right.m_Seconds;

        public static bool operator ==(NetworkTimestamp left, NetworkTimestamp right) => left.Equals(right);

        public static bool operator !=(NetworkTimestamp left, NetworkTimestamp right) => !left.Equals(right);
    }

    /// <summary>
    /// Lag compensation adapter for Enemy Masses crowd agents.
    /// Efficiently tracks positions of large numbers of agents for server-side hit validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike CharacterLagCompensation (one component per character), this component
    /// manages lag compensation for entire crowds efficiently using batch operations.
    /// </para>
    /// <para>
    /// Usage:
    /// 1. Add this component to your EnemyMassesCrowdController GameObject
    /// 2. Configure history settings
    /// 3. Call RecordFrame() every server tick
    /// 4. Use ValidateHit() to check hits against historical positions
    /// </para>
    /// </remarks>
    [AddComponentMenu("Enemy Masses/Network/Crowd Lag Compensation")]
    public class EnemyMassesLagCompensation : MonoBehaviour
    {
        // INSPECTOR ──────────────────────────────────────────────────────────
        
        [Header("Configuration")]
        [Tooltip("Reference to the EnemyMassesCrowdController")]
        [SerializeField] private EnemyMassesCrowdController m_CrowdController;
        
        [Tooltip("Network ID offset for agents (agent index + offset = network ID)")]
        [SerializeField] private uint m_NetworkIdOffset = 100000;
        
        [Header("History Settings")]
        [Tooltip("How many snapshots to store per agent")]
        [SerializeField] private int m_HistorySize = 64;
        
        [Tooltip("How often to record snapshots (Hz)")]
        [SerializeField] private int m_SnapshotRate = 60;
        
        [Tooltip("Maximum rewind time for hit validation (seconds)")]
        [SerializeField] private float m_MaxRewindTime = 0.5f;
        
        [Tooltip("Extra tolerance for hit validation (meters)")]
        [SerializeField] private float m_HitTolerance = 0.3f;
        
        [Header("Hit Detection")]
        [Tooltip("Use crowd's virtual hitbox settings")]
        [SerializeField] private bool m_UseVirtualHitboxes = true;
        
        [Tooltip("Default agent radius if not using virtual hitboxes")]
        [SerializeField] private float m_DefaultRadius = 0.5f;
        
        [Tooltip("Default agent height if not using virtual hitboxes")]
        [SerializeField] private float m_DefaultHeight = 1.8f;
        
        [Header("Registration")]
        [Tooltip("Auto-register agents when they spawn")]
        [SerializeField] private bool m_AutoRegister = true;
        
        [Tooltip("Attempt global manager mode (falls back to local history if Networking Core is unavailable)")]
        [SerializeField] private bool m_UseGlobalManager = true;
        
        [Header("Performance — Spatial Grid")]
        [Tooltip("Cell size for spatial grid used in ValidateSphereHits broad-phase (meters). " +
                 "Should roughly match the largest expected AOE radius.")]
        [SerializeField] private float m_SpatialGridCellSize = 15f;
        
        [Tooltip("Extra radius added during spatial queries to account for agent movement " +
                 "between current and historical positions (meters). " +
                 "Set to maxAgentSpeed * maxRewindTime.")]
        [SerializeField] private float m_SpatialQueryPadding = 10f;
        
        [Header("Performance — Tiered Recording")]
        [Tooltip("Agents beyond this distance from any player record at a reduced rate (meters). " +
                 "Set to 0 to disable tiered recording (all agents record every tick).")]
        [SerializeField] private float m_TieredRecordDistance = 0f;
        
        [Tooltip("Low-priority agents record every Nth tick instead of every tick. " +
                 "For example, 4 means quarter-rate. Must be >= 2.")]
        [SerializeField] private int m_LowPriorityFrameSkip = 4;
        
        // PRIVATE FIELDS ─────────────────────────────────────────────────────
        
        private Dictionary<int, AgentLagCompensation> m_AgentAdapters;
        private List<int> m_ActiveAgents;
        private List<int> m_ToRemoveBuffer;   // Reused in RecordFrame to avoid per-tick allocation
        private bool m_Initialized;
        
        // Spatial grid for ValidateSphereHits broad-phase filtering
        private SpatialGrid m_SpatialGrid;
        private readonly List<int> m_SphereQueryBuffer = new(64);
        
        // Tiered recording state
        private int m_RecordTickCounter;
        
        // Pool for adapters
        private Stack<AgentLagCompensation> m_AdapterPool;
        private const int POOL_INITIAL_SIZE = 100;
        
        // EVENTS ─────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Called when an agent hit is validated.
        /// </summary>
        public event Action<int, HitValidationResult> OnAgentHitValidated;
        
        // PROPERTIES ─────────────────────────────────────────────────────────
        
        /// <summary>
        /// Reference to the managed EnemyMassesCrowdController.
        /// </summary>
        public EnemyMassesCrowdController CrowdController
        {
            get => m_CrowdController;
            set => m_CrowdController = value;
        }
        
        /// <summary>
        /// Number of agents currently tracked.
        /// </summary>
        public int TrackedAgentCount => m_ActiveAgents?.Count ?? 0;
        
        /// <summary>
        /// History size setting.
        /// </summary>
        public int HistorySize
        {
            get => m_HistorySize;
            set => m_HistorySize = value;
        }
        
        /// <summary>
        /// Snapshot rate setting.
        /// </summary>
        public int SnapshotRate
        {
            get => m_SnapshotRate;
            set => m_SnapshotRate = value;
        }
        
        /// <summary>
        /// Max rewind time setting.
        /// </summary>
        public float MaxRewindTime
        {
            get => m_MaxRewindTime;
            set => m_MaxRewindTime = value;
        }
        
        /// <summary>
        /// Hit tolerance setting.
        /// </summary>
        public float HitTolerance
        {
            get => m_HitTolerance;
            set => m_HitTolerance = value;
        }
        
        /// <summary>
        /// Whether to attempt global manager mode.
        /// </summary>
        public bool UseGlobalManager
        {
            get => m_UseGlobalManager;
            set => m_UseGlobalManager = value;
        }
        
        /// <summary>
        /// Convert agent index to network ID.
        /// </summary>
        public uint AgentIndexToNetworkId(int agentIndex) => (uint)agentIndex + m_NetworkIdOffset;
        
        /// <summary>
        /// Convert network ID to agent index.
        /// </summary>
        public int NetworkIdToAgentIndex(uint networkId) => (int)(networkId - m_NetworkIdOffset);
        
        // UNITY LIFECYCLE ────────────────────────────────────────────────────
        
        private void Awake()
        {
            if (m_CrowdController == null)
                m_CrowdController = GetComponent<EnemyMassesCrowdController>();
            
            m_AgentAdapters = new Dictionary<int, AgentLagCompensation>(256);
            m_ActiveAgents = new List<int>(256);
            m_AdapterPool = new Stack<AgentLagCompensation>(POOL_INITIAL_SIZE);
            m_ToRemoveBuffer = new List<int>(16);
            m_SpatialGrid = new SpatialGrid(m_SpatialGridCellSize);
            m_RecordTickCounter = 0;
            
            // Pre-populate pool
            for (int i = 0; i < POOL_INITIAL_SIZE; i++)
            {
                m_AdapterPool.Push(new AgentLagCompensation(this));
            }
        }
        
        private void Start()
        {
            Initialize();
        }
        
        private void OnDestroy()
        {
            UnregisterAll();
        }
        
        // INITIALIZATION ─────────────────────────────────────────────────────
        
        /// <summary>
        /// Initialize lag compensation. Called automatically on Start.
        /// </summary>
        public void Initialize()
        {
            if (m_Initialized)
                return;
            
            if (m_CrowdController == null)
            {
                Debug.LogError("[EnemyMassesLagComp] No EnemyMassesCrowdController assigned!");
                return;
            }
            
            // Networking Core integration is intentionally decoupled from this runtime assembly.
            // Fallback to local history validation when the global manager path is enabled.
            if (m_UseGlobalManager)
            {
                Debug.LogWarning(
                    "[EnemyMassesLagComp] Global manager integration requires Networking Core " +
                    "assembly references. Falling back to local lag-comp history.");
                m_UseGlobalManager = false;
            }
            
            // Subscribe to agent events
            if (m_AutoRegister)
            {
                // Hook into spawn/despawn events
                if (m_CrowdController != null)
                {
                    m_CrowdController.NetworkEventAgentSpawned += OnAgentSpawnedEvent;
                    m_CrowdController.NetworkEventAgentsDespawned += OnAgentsDespawnedEvent;
                }
                
                // Register existing agents
                RegisterExistingAgents();
            }
            
            m_Initialized = true;
        }
        
        // REGISTRATION ───────────────────────────────────────────────────────
        
        /// <summary>
        /// Register an agent for lag compensation tracking.
        /// </summary>
        public void RegisterAgent(int agentIndex)
        {
            if (m_AgentAdapters.ContainsKey(agentIndex))
                return;
            
            var adapter = GetAdapter();
            adapter.Initialize(agentIndex, m_HistorySize);
            m_AgentAdapters[agentIndex] = adapter;
            m_ActiveAgents.Add(agentIndex);
        }
        
        /// <summary>
        /// Unregister an agent from lag compensation tracking.
        /// </summary>
        public void UnregisterAgent(int agentIndex)
        {
            if (!m_AgentAdapters.TryGetValue(agentIndex, out var adapter))
                return;
            
            m_AgentAdapters.Remove(agentIndex);
            m_ActiveAgents.Remove(agentIndex);
            
            // Return to pool
            ReturnAdapter(adapter);
        }
        
        /// <summary>
        /// Register all currently existing agents.
        /// </summary>
        public void RegisterExistingAgents()
        {
            if (m_CrowdController == null)
                return;
            
            int agentCount = m_CrowdController.GetAgentCount();
            for (int i = 0; i < agentCount; i++)
            {
                var agent = m_CrowdController.GetAgent(i);
                if (agent != null && !agent.isDead)
                {
                    RegisterAgent(i);
                }
            }
        }
        
        /// <summary>
        /// Unregister all agents.
        /// </summary>
        public void UnregisterAll()
        {
            // Unsubscribe from events
            if (m_CrowdController != null)
            {
                m_CrowdController.NetworkEventAgentSpawned -= OnAgentSpawnedEvent;
                m_CrowdController.NetworkEventAgentsDespawned -= OnAgentsDespawnedEvent;
            }
            
            foreach (var agentIndex in m_ActiveAgents.ToArray())
            {
                UnregisterAgent(agentIndex);
            }
        }
        
        // RECORDING ──────────────────────────────────────────────────────────
        
        /// <summary>
        /// Record the current state of all tracked agents.
        /// Call this every server tick.
        /// </summary>
        public void RecordFrame(NetworkTimestamp timestamp)
        {
            if (m_CrowdController == null)
                return;
            
            m_RecordTickCounter++;
            
            // Reuse persistent buffer to avoid per-tick allocation
            m_ToRemoveBuffer.Clear();
            
            // Rebuild the spatial grid from current positions each tick.
            m_SpatialGrid.Clear();
            
            bool useTieredRecording = m_TieredRecordDistance > 0f && m_LowPriorityFrameSkip >= 2;
            bool isLowPriorityTick = useTieredRecording &&
                                     (m_RecordTickCounter % m_LowPriorityFrameSkip) != 0;
            
            foreach (var agentIndex in m_ActiveAgents)
            {
                var agent = m_CrowdController.GetAgent(agentIndex);
                if (agent == null || agent.isDead)
                {
                    m_ToRemoveBuffer.Add(agentIndex);
                    continue;
                }
                
                if (m_AgentAdapters.TryGetValue(agentIndex, out var adapter))
                {
                    // Tiered recording: skip low-priority agents on non-priority ticks.
                    // An agent is low-priority when no player owns it (AI-only)
                    // and tiered recording is enabled. Player-owned agents always
                    // record at full rate for responsive lag compensation.
                    bool isHighPriority = !useTieredRecording ||
                                          agent.ownerPlayerId >= 0;
                    
                    if (isHighPriority || !isLowPriorityTick)
                    {
                        adapter.History.RecordSnapshot(adapter, timestamp);
                    }
                    
                    // Always update the spatial grid with current position
                    // (regardless of whether we recorded a snapshot this tick).
                    m_SpatialGrid.Insert(agentIndex, adapter.Position);
                }
            }
            
            // Remove dead agents
            if (m_ToRemoveBuffer.Count > 0)
            {
                foreach (var idx in m_ToRemoveBuffer)
                {
                    UnregisterAgent(idx);
                }
            }
        }
        
        // HIT VALIDATION ─────────────────────────────────────────────────────
        
        /// <summary>
        /// Validate a hit against an agent at a historical timestamp.
        /// </summary>
        public HitValidationResult ValidateHit(int agentIndex, Vector3 hitPoint,
            NetworkTimestamp clientTimestamp)
        {
            if (!m_AgentAdapters.TryGetValue(agentIndex, out var adapter))
            {
                return new HitValidationResult
                {
                    isValid = false,
                    reason = HitRejectReason.EntityNotFound,
                    targetNetworkId = AgentIndexToNetworkId(agentIndex),
                    distanceFromBounds = float.PositiveInfinity
                };
            }
            
            HitValidationResult result;

            // Use local history
            if (adapter.History.TryGetStateAt(clientTimestamp, out var snapshot))
            {
                float distance = snapshot.DistanceToPoint(hitPoint);
                result = new HitValidationResult
                {
                    isValid = distance <= m_HitTolerance,
                    reason = distance <= m_HitTolerance
                        ? HitRejectReason.None
                        : HitRejectReason.OutOfRange,
                    targetNetworkId = adapter.NetworkId,
                    hitPoint = hitPoint,
                    historicalPosition = snapshot.position,
                    historicalBounds = snapshot.bounds,
                    distanceFromBounds = distance,
                    clientTimestamp = clientTimestamp
                };
            }
            else
            {
                result = new HitValidationResult
                {
                    isValid = false,
                    reason = HitRejectReason.EntityNotFound,
                    targetNetworkId = adapter.NetworkId,
                    distanceFromBounds = float.PositiveInfinity
                };
            }
            
            OnAgentHitValidated?.Invoke(agentIndex, result);
            return result;
        }
        
        /// <summary>
        /// Validate hits against all agents within a sphere.
        /// Uses a spatial grid as a broad-phase filter to avoid O(n) iteration
        /// of all tracked agents — only agents in nearby grid cells are tested.
        /// </summary>
        public int ValidateSphereHits(Vector3 center, float radius,
            NetworkTimestamp clientTimestamp, out List<(int agentIndex, HitValidationResult result)> hits)
        {
            hits = new List<(int, HitValidationResult)>();
            
            // Broad-phase: query the spatial grid with an expanded radius
            // to account for agent movement between current and historical positions.
            m_SpatialGrid.QuerySphere(center, radius + m_SpatialQueryPadding, m_SphereQueryBuffer);
            
            foreach (var agentIndex in m_SphereQueryBuffer)
            {
                var result = ValidateHit(agentIndex, center, clientTimestamp);
                
                // Narrow-phase: precise historical distance check
                if (result.distanceFromBounds <= radius + m_HitTolerance)
                {
                    result.isValid = true;
                    result.reason = HitRejectReason.None;
                    hits.Add((agentIndex, result));
                }
            }
            
            return hits.Count;
        }
        
        /// <summary>
        /// Get an agent's position at a historical timestamp.
        /// </summary>
        public bool TryGetAgentPositionAt(int agentIndex, NetworkTimestamp timestamp,
            out Vector3 position)
        {
            position = Vector3.zero;
            
            if (!m_AgentAdapters.TryGetValue(agentIndex, out var adapter))
                return false;
            
            return adapter.History.TryGetPositionAt(timestamp, out position);
        }
        
        // POOLING ────────────────────────────────────────────────────────────
        
        private AgentLagCompensation GetAdapter()
        {
            if (m_AdapterPool.Count > 0)
                return m_AdapterPool.Pop();
            
            return new AgentLagCompensation(this);
        }
        
        private void ReturnAdapter(AgentLagCompensation adapter)
        {
            adapter.Reset();
            m_AdapterPool.Push(adapter);
        }
        
        // AGENT CALLBACKS ────────────────────────────────────────────────────
        
        private void OnAgentSpawnedEvent(SpawnEventData data)
        {
            if (data.agentIndices != null)
            {
                foreach (var idx in data.agentIndices)
                {
                    RegisterAgent(idx);
                }
            }
        }
        
        private void OnAgentsDespawnedEvent(uint[] networkIds, DespawnReason reason)
        {
            if (networkIds != null)
            {
                foreach (var netId in networkIds)
                {
                    int agentIndex = NetworkIdToAgentIndex(netId);
                    UnregisterAgent(agentIndex);
                }
            }
        }
        
        // INNER CLASS: Agent Adapter ─────────────────────────────────────────
        
        /// <summary>
        /// Local lag-comp adapter for a single Enemy Masses agent.
        /// </summary>
        private class AgentLagCompensation
        {
            private readonly EnemyMassesLagCompensation m_Owner;
            private int m_AgentIndex;
            private LagCompensationHistory m_History;
            
            public AgentLagCompensation(EnemyMassesLagCompensation owner)
            {
                m_Owner = owner;
            }
            
            public void Initialize(int agentIndex, int historySize)
            {
                m_AgentIndex = agentIndex;
                m_History = new LagCompensationHistory(historySize);
            }
            
            public void Reset()
            {
                m_AgentIndex = -1;
                m_History?.Clear();
            }
            
            public LagCompensationHistory History => m_History;

            public uint NetworkId => m_Owner.AgentIndexToNetworkId(m_AgentIndex);
            
            public Vector3 Position
            {
                get
                {
                    if (m_Owner.m_CrowdController != null && m_AgentIndex >= 0)
                        return m_Owner.m_CrowdController.GetAgentPosition(m_AgentIndex);
                    return Vector3.zero;
                }
            }
            
            public Quaternion Rotation
            {
                get
                {
                    if (m_Owner.m_CrowdController != null && m_AgentIndex >= 0)
                    {
                        var agent = m_Owner.m_CrowdController.GetAgent(m_AgentIndex);
                        if (agent?.transform != null)
                            return agent.transform.rotation;
                    }
                    return Quaternion.identity;
                }
            }
            
            public Bounds Bounds
            {
                get
                {
                    Vector3 pos = Position;
                    float radius = m_Owner.GetAgentRadius(m_AgentIndex);
                    float height = m_Owner.GetAgentHeight(m_AgentIndex);
                    
                    Vector3 center = pos + Vector3.up * (height * 0.5f);
                    Vector3 size = new Vector3(radius * 2f, height, radius * 2f);
                    
                    return new Bounds(center, size);
                }
            }
            
            public bool IsActive
            {
                get
                {
                    if (m_Owner.m_CrowdController != null && m_AgentIndex >= 0)
                    {
                        var agent = m_Owner.m_CrowdController.GetAgent(m_AgentIndex);
                        return agent != null && !agent.isDead;
                    }
                    return false;
                }
            }
            
            public float Radius => m_Owner.GetAgentRadius(m_AgentIndex);
            
            public float Height => m_Owner.GetAgentHeight(m_AgentIndex);
        }

        private struct LagCompensationSnapshot
        {
            public NetworkTimestamp timestamp;
            public Vector3 position;
            public Bounds bounds;

            public float DistanceToPoint(Vector3 point)
            {
                Vector3 closestPoint = bounds.ClosestPoint(point);
                return Vector3.Distance(closestPoint, point);
            }
        }

        private sealed class LagCompensationHistory
        {
            private readonly LagCompensationSnapshot[] m_Snapshots;
            private int m_Head;
            private int m_Count;

            public LagCompensationHistory(int capacity)
            {
                int validatedCapacity = Mathf.Max(4, capacity);
                m_Snapshots = new LagCompensationSnapshot[validatedCapacity];
                m_Head = 0;
                m_Count = 0;
            }

            public void Clear()
            {
                m_Head = 0;
                m_Count = 0;
            }

            public void RecordSnapshot(AgentLagCompensation adapter, NetworkTimestamp timestamp)
            {
                var snapshot = new LagCompensationSnapshot
                {
                    timestamp = timestamp,
                    position = adapter.Position,
                    bounds = adapter.Bounds
                };

                int writeIndex;
                if (m_Count < m_Snapshots.Length)
                {
                    writeIndex = ToBufferIndex(m_Count);
                    m_Count++;
                }
                else
                {
                    writeIndex = m_Head;
                    m_Head = (m_Head + 1) % m_Snapshots.Length;
                }

                m_Snapshots[writeIndex] = snapshot;
            }

            public bool TryGetPositionAt(NetworkTimestamp timestamp, out Vector3 position)
            {
                if (TryGetStateAt(timestamp, out var snapshot))
                {
                    position = snapshot.position;
                    return true;
                }

                position = Vector3.zero;
                return false;
            }

            public bool TryGetStateAt(NetworkTimestamp timestamp, out LagCompensationSnapshot snapshot)
            {
                if (m_Count == 0)
                {
                    snapshot = default;
                    return false;
                }

                LagCompensationSnapshot oldest = GetSnapshot(0);
                LagCompensationSnapshot newest = GetSnapshot(m_Count - 1);

                if (timestamp <= oldest.timestamp)
                {
                    snapshot = oldest;
                    return true;
                }

                if (timestamp >= newest.timestamp)
                {
                    snapshot = newest;
                    return true;
                }

                double targetSeconds = timestamp.Seconds;
                for (int i = m_Count - 2; i >= 0; i--)
                {
                    LagCompensationSnapshot before = GetSnapshot(i);
                    LagCompensationSnapshot after = GetSnapshot(i + 1);
                    if (targetSeconds < before.timestamp.Seconds || targetSeconds > after.timestamp.Seconds)
                        continue;

                    double span = after.timestamp.Seconds - before.timestamp.Seconds;
                    if (span <= 0.000001d)
                    {
                        snapshot = before;
                        return true;
                    }

                    float t = Mathf.Clamp01((float)((targetSeconds - before.timestamp.Seconds) / span));
                    snapshot = new LagCompensationSnapshot
                    {
                        timestamp = timestamp,
                        position = Vector3.Lerp(before.position, after.position, t),
                        bounds = LerpBounds(before.bounds, after.bounds, t)
                    };
                    return true;
                }

                snapshot = newest;
                return true;
            }

            private int ToBufferIndex(int logicalIndex)
            {
                return (m_Head + logicalIndex) % m_Snapshots.Length;
            }

            private LagCompensationSnapshot GetSnapshot(int logicalIndex)
            {
                return m_Snapshots[ToBufferIndex(logicalIndex)];
            }

            private static Bounds LerpBounds(Bounds a, Bounds b, float t)
            {
                return new Bounds(Vector3.Lerp(a.center, b.center, t), Vector3.Lerp(a.size, b.size, t));
            }
        }
        
        // HELPERS ────────────────────────────────────────────────────────────
        
        private float GetAgentRadius(int agentIndex)
        {
            if (m_CrowdController != null)
            {
                var agent = m_CrowdController.GetAgent(agentIndex);
                if (agent?.navigationAgent != null)
                {
                    return agent.navigationAgent.Radius;
                }
            }
            return m_DefaultRadius;
        }
        
        private float GetAgentHeight(int agentIndex)
        {
            if (m_CrowdController != null)
            {
                var agent = m_CrowdController.GetAgent(agentIndex);
                // Try virtual hitbox first (has agentHeight), then navigationAgent
                if (m_UseVirtualHitboxes && agent?.enemyCrowd?.virtualHitbox != null)
                {
                    return agent.enemyCrowd.virtualHitbox.agentHeight;
                }
                if (agent?.navigationAgent != null)
                {
                    return agent.navigationAgent.Height;
                }
            }
            return m_DefaultHeight;
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // INNER CLASS: Spatial Grid
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Lightweight 2D (XZ) spatial grid for broad-phase filtering of agent positions.
        /// Rebuilt every tick from current positions. Cell lists are pooled to avoid
        /// per-tick allocation.
        /// </summary>
        private sealed class SpatialGrid
        {
            private readonly float _cellSize;
            private readonly Dictionary<long, List<int>> _cells;
            private readonly List<List<int>> _listPool;

            public SpatialGrid(float cellSize)
            {
                _cellSize = Mathf.Max(1f, cellSize);
                _cells = new Dictionary<long, List<int>>(256);
                _listPool = new List<List<int>>(64);
            }

            /// <summary>
            /// Removes all agents from the grid, returning cell lists to the pool.
            /// </summary>
            public void Clear()
            {
                foreach (var kvp in _cells)
                {
                    kvp.Value.Clear();
                    _listPool.Add(kvp.Value);
                }
                _cells.Clear();
            }

            /// <summary>
            /// Adds an agent to the grid cell containing <paramref name="position"/>.
            /// </summary>
            public void Insert(int agentIndex, Vector3 position)
            {
                long key = CellKey(position);
                if (!_cells.TryGetValue(key, out var list))
                {
                    if (_listPool.Count > 0)
                    {
                        list = _listPool[_listPool.Count - 1];
                        _listPool.RemoveAt(_listPool.Count - 1);
                    }
                    else
                    {
                        list = new List<int>(16);
                    }
                    _cells[key] = list;
                }
                list.Add(agentIndex);
            }

            /// <summary>
            /// Fills <paramref name="results"/> with agent indices in cells overlapping
            /// a sphere of the given <paramref name="radius"/> centered at <paramref name="center"/>.
            /// </summary>
            public void QuerySphere(Vector3 center, float radius, List<int> results)
            {
                results.Clear();
                int minX = Mathf.FloorToInt((center.x - radius) / _cellSize);
                int maxX = Mathf.FloorToInt((center.x + radius) / _cellSize);
                int minZ = Mathf.FloorToInt((center.z - radius) / _cellSize);
                int maxZ = Mathf.FloorToInt((center.z + radius) / _cellSize);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        long key = PackKey(x, z);
                        if (_cells.TryGetValue(key, out var list))
                        {
                            results.AddRange(list);
                        }
                    }
                }
            }

            private long CellKey(Vector3 pos)
            {
                int x = Mathf.FloorToInt(pos.x / _cellSize);
                int z = Mathf.FloorToInt(pos.z / _cellSize);
                return PackKey(x, z);
            }

            private static long PackKey(int x, int z)
            {
                return ((long)x << 32) | (uint)z;
            }
        }
    }
}
