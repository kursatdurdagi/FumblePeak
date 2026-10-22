using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Server-to-client state reconciliation system.
    /// Periodically sends authoritative snapshots to override client state.
    /// 
    /// This is the "nuclear option" for anti-cheat - even if a client modifies
    /// their local state, the server will periodically reset it to truth.
    /// 
    /// Usage:
    /// - Server creates snapshots at regular intervals
    /// - Clients apply snapshots to correct any drift or cheating
    /// - Supports delta compression for bandwidth efficiency
    /// </summary>
    public class StateReconciliationSystem
    {
        // Configuration
        private readonly float _snapshotInterval;
        private readonly float _urgentReconcileThreshold;
        private readonly bool _enableDeltaCompression;
        
        // State tracking
        private float _lastSnapshotTime;
        private uint _snapshotSequence;
        private readonly Dictionary<uint, AgentStateSnapshot> _lastSentSnapshots = new();
        
        // Callbacks
        private System.Func<int, AgentStateSnapshot> _getAgentState;
        private System.Action<uint, AgentStateSnapshot> _applyAgentState;
        
        // Statistics
        private int _snapshotsSent;
        private int _reconciliationsApplied;
        private int _urgentReconciliations;

        /// <summary>
        /// Creates a state reconciliation system.
        /// </summary>
        /// <param name="snapshotInterval">Seconds between full state snapshots</param>
        /// <param name="urgentReconcileThreshold">Health difference % that triggers immediate reconcile</param>
        /// <param name="enableDeltaCompression">Only send changed fields</param>
        public StateReconciliationSystem(
            float snapshotInterval = 1f,
            float urgentReconcileThreshold = 0.2f,
            bool enableDeltaCompression = true)
        {
            _snapshotInterval = snapshotInterval;
            _urgentReconcileThreshold = urgentReconcileThreshold;
            _enableDeltaCompression = enableDeltaCompression;
        }

        /// <summary>
        /// Sets the callbacks for getting and applying agent state.
        /// Must be called before using the system.
        /// </summary>
        /// <param name="getState">Function to get current state for an agent index</param>
        /// <param name="applyState">Action to apply a snapshot to a network ID</param>
        public void SetCallbacks(
            System.Func<int, AgentStateSnapshot> getState,
            System.Action<uint, AgentStateSnapshot> applyState)
        {
            _getAgentState = getState;
            _applyAgentState = applyState;
        }

        /// <summary>
        /// Called by server to check if a snapshot should be sent.
        /// </summary>
        /// <returns>True if it's time for a snapshot</returns>
        public bool ShouldSendSnapshot()
        {
            return Time.time - _lastSnapshotTime >= _snapshotInterval;
        }

        /// <summary>
        /// Creates a state snapshot for a single agent.
        /// Call this on the server to generate data to send to clients.
        /// </summary>
        /// <param name="agentIndex">Local index of the agent</param>
        /// <returns>Snapshot data, or null if agent not found</returns>
        public AgentStateSnapshot CreateSnapshot(int agentIndex)
        {
            if (_getAgentState == null)
                return default;

            var snapshot = _getAgentState(agentIndex);
            snapshot.sequence = ++_snapshotSequence;
            snapshot.serverTime = Time.time;
            
            return snapshot;
        }

        /// <summary>
        /// Creates snapshots for multiple agents.
        /// </summary>
        /// <param name="agentIndices">Indices of agents to snapshot</param>
        /// <returns>Array of snapshots</returns>
        public AgentStateSnapshot[] CreateBatchSnapshot(IReadOnlyList<int> agentIndices)
        {
            if (_getAgentState == null || agentIndices == null)
                return null;

            var snapshots = new AgentStateSnapshot[agentIndices.Count];
            uint batchSequence = ++_snapshotSequence;
            float serverTime = Time.time;

            for (int i = 0; i < agentIndices.Count; i++)
            {
                snapshots[i] = _getAgentState(agentIndices[i]);
                snapshots[i].sequence = batchSequence;
                snapshots[i].serverTime = serverTime;
            }

            _lastSnapshotTime = serverTime;
            _snapshotsSent += agentIndices.Count;
            
            return snapshots;
        }

        /// <summary>
        /// Creates a delta snapshot (only changed fields since last snapshot).
        /// More bandwidth efficient but requires tracking last sent state.
        /// </summary>
        /// <param name="agentIndex">Local index of the agent</param>
        /// <param name="networkId">Network ID for lookup</param>
        /// <returns>Delta snapshot with only changed fields flagged</returns>
        public AgentStateSnapshot CreateDeltaSnapshot(int agentIndex, uint networkId)
        {
            if (_getAgentState == null)
                return default;

            var current = _getAgentState(agentIndex);
            current.sequence = ++_snapshotSequence;
            current.serverTime = Time.time;

            if (_enableDeltaCompression && _lastSentSnapshots.TryGetValue(networkId, out var lastSent))
            {
                // Calculate delta flags
                current.deltaFlags = CalculateDeltaFlags(lastSent, current);
            }
            else
            {
                // Full snapshot
                current.deltaFlags = AgentStateDeltaFlags.All;
            }

            // Store for next delta calculation
            _lastSentSnapshots[networkId] = current;
            _snapshotsSent++;
            
            return current;
        }

        /// <summary>
        /// Applies a server snapshot on the client.
        /// This overwrites local state with authoritative server state.
        /// </summary>
        /// <param name="snapshot">Snapshot received from server</param>
        public void ApplyServerSnapshot(AgentStateSnapshot snapshot)
        {
            if (_applyAgentState == null)
                return;

            _applyAgentState(snapshot.networkId, snapshot);
            _reconciliationsApplied++;
        }

        /// <summary>
        /// Checks if an urgent reconciliation is needed based on state divergence.
        /// </summary>
        /// <param name="clientHealth">Client's local health value</param>
        /// <param name="serverHealth">Server's authoritative health</param>
        /// <param name="maxHealth">Maximum health for percentage calculation</param>
        /// <returns>True if divergence exceeds threshold</returns>
        public bool NeedsUrgentReconciliation(float clientHealth, float serverHealth, float maxHealth)
        {
            if (maxHealth <= 0)
                return false;

            float divergence = Mathf.Abs(clientHealth - serverHealth) / maxHealth;
            
            if (divergence >= _urgentReconcileThreshold)
            {
                _urgentReconciliations++;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Creates an urgent reconciliation snapshot for immediate sync.
        /// Use when significant state divergence is detected.
        /// </summary>
        public AgentStateSnapshot CreateUrgentSnapshot(int agentIndex, ReconciliationReason reason)
        {
            var snapshot = CreateSnapshot(agentIndex);
            snapshot.isUrgent = true;
            snapshot.reconciliationReason = reason;
            _urgentReconciliations++;
            return snapshot;
        }

        /// <summary>
        /// Clears tracking data for an agent.
        /// </summary>
        public void ClearAgent(uint networkId)
        {
            _lastSentSnapshots.Remove(networkId);
        }

        /// <summary>
        /// Gets reconciliation statistics.
        /// </summary>
        public (int sent, int applied, int urgent) GetStatistics()
        {
            return (_snapshotsSent, _reconciliationsApplied, _urgentReconciliations);
        }

        /// <summary>
        /// Resets all tracking data.
        /// </summary>
        public void Reset()
        {
            _lastSentSnapshots.Clear();
            _lastSnapshotTime = 0;
            _snapshotSequence = 0;
            _snapshotsSent = 0;
            _reconciliationsApplied = 0;
            _urgentReconciliations = 0;
        }

        /// <summary>
        /// Resets statistics counters without clearing snapshot data.
        /// </summary>
        public void ResetStatistics()
        {
            _snapshotsSent = 0;
            _reconciliationsApplied = 0;
            _urgentReconciliations = 0;
        }

        private AgentStateDeltaFlags CalculateDeltaFlags(AgentStateSnapshot last, AgentStateSnapshot current)
        {
            AgentStateDeltaFlags flags = AgentStateDeltaFlags.None;

            if (Vector3.Distance(last.position, current.position) > 0.01f)
                flags |= AgentStateDeltaFlags.Position;
            
            if (Quaternion.Angle(last.rotation, current.rotation) > 1f)
                flags |= AgentStateDeltaFlags.Rotation;
            
            if (Mathf.Abs(last.health - current.health) > 0.1f)
                flags |= AgentStateDeltaFlags.Health;
            
            if (last.isDead != current.isDead)
                flags |= AgentStateDeltaFlags.DeathState;
            
            if (last.combatState != current.combatState)
                flags |= AgentStateDeltaFlags.CombatState;
            
            if (last.navigationState != current.navigationState)
                flags |= AgentStateDeltaFlags.NavigationState;
            
            if (last.targetNetworkId != current.targetNetworkId)
                flags |= AgentStateDeltaFlags.Target;
            
            if (Vector3.Distance(last.destination, current.destination) > 0.1f)
                flags |= AgentStateDeltaFlags.Destination;
            
            // Skill-relevant state (resource, level/stats, cooldowns, active skill)
            // Fast-path: skip array comparison when generation counters match.
            if (last.resourceStateVersion != 0 && last.resourceStateVersion == current.resourceStateVersion)
            { /* resource arrays unchanged */ }
            else if (!ResourceStateEquals(last.resourceState, current.resourceState))
                flags |= AgentStateDeltaFlags.ResourceState;
            
            if (last.level != current.level ||
                Mathf.Abs(last.experience - current.experience) > 0.01f)
                flags |= AgentStateDeltaFlags.LevelStats;
            
            if (last.cooldownStateVersion != 0 && last.cooldownStateVersion == current.cooldownStateVersion)
            { /* cooldown arrays unchanged */ }
            else if (!CooldownStateEquals(last.skillCooldownState, current.skillCooldownState))
                flags |= AgentStateDeltaFlags.SkillCooldowns;
            
            if (last.activeSkillSlotIndex != current.activeSkillSlotIndex ||
                last.isCasting != current.isCasting)
                flags |= AgentStateDeltaFlags.ActiveSkill;

            if (last.isFlying != current.isFlying ||
                Mathf.Abs(last.flightHeight - current.flightHeight) > 0.5f)
                flags |= AgentStateDeltaFlags.Flying;

            return flags;
        }

        /// <summary>
        /// Compares two resource state arrays for equality.
        /// </summary>
        private static bool ResourceStateEquals(
            AgentResourceState[] a, AgentResourceState[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                // Use integer keys for O(1) comparison when available;
                // fall back to string comparison only if neither side is registered.
                if (a[i].resourceKey != 0 || b[i].resourceKey != 0)
                {
                    if (a[i].resourceKey != b[i].resourceKey) return false;
                }
                else if (a[i].resourceName != b[i].resourceName) return false;
                
                if (Mathf.Abs(a[i].current - b[i].current) > 0.01f) return false;
                if (Mathf.Abs(a[i].max - b[i].max) > 0.01f) return false;
            }
            return true;
        }

        /// <summary>
        /// Compares two cooldown state arrays for equality.
        /// </summary>
        private static bool CooldownStateEquals(
            AgentSkillCooldownState[] a, AgentSkillCooldownState[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].slotIndex != b[i].slotIndex) return false;
                if (Mathf.Abs(a[i].cooldownEndTime - b[i].cooldownEndTime) > 0.05f) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Complete state snapshot for an agent.
    /// Sent from server to client for reconciliation.
    /// </summary>
    [System.Serializable]
    public struct AgentStateSnapshot
    {
        /// <summary>Network ID of the agent</summary>
        public uint networkId;
        
        /// <summary>Sequence number for ordering</summary>
        public uint sequence;
        
        /// <summary>Server time when snapshot was taken</summary>
        public float serverTime;
        
        /// <summary>Flags indicating which fields are included (for delta compression)</summary>
        public AgentStateDeltaFlags deltaFlags;
        
        /// <summary>Whether this is an urgent reconciliation</summary>
        public bool isUrgent;
        
        /// <summary>Reason for reconciliation (if urgent)</summary>
        public ReconciliationReason reconciliationReason;

        // Transform
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        
        // Health
        public float health;
        public float maxHealth;
        public bool isDead;
        public bool isInvulnerable;
        
        // Combat
        public CrowdCombatState combatState;
        public float combatStateEndTime;
        public int targetAgentIndex;
        public uint targetNetworkId;
        public float nextAttackTime;
        
        // Navigation
        public AgentNavigationState navigationState;
        public Vector3 destination;
        public bool hasManualCommand;
        
        // Status effects (simplified)
        public float damageMultiplier;
        public float speedMultiplier;
        public float damageReceivedMultiplier;
        
        // ═══ SKILL-RELEVANT STATE (added for competitive multiplayer) ═══
        
        // Resource state (mana, energy, rage, etc.)
        /// <summary>Per-resource current/max values for this agent. Null if no resources.</summary>
        public AgentResourceState[] resourceState;
        
        // Level & scaling stats
        /// <summary>Agent level for scaling formula computation</summary>
        public int level;
        /// <summary>Agent experience (for near-levelup reconciliation)</summary>
        public float experience;
        /// <summary>Key stat values used by scaling formulas. Null if no stat-based scaling.</summary>
        public AgentStatEntry[] scalingStats;
        
        // Skill cooldown state
        /// <summary>Per-slot cooldown end times. Null if no skills.</summary>
        public AgentSkillCooldownState[] skillCooldownState;
        
        /// <summary>Generation counter for resource array — skip comparison when equal and non-zero.</summary>
        public uint resourceStateVersion;
        /// <summary>Generation counter for cooldown array — skip comparison when equal and non-zero.</summary>
        public uint cooldownStateVersion;
        /// <summary>Global skill cooldown end time (server time)</summary>
        public float globalSkillCooldownEndTime;
        /// <summary>Last skill use time (for global cooldown tracking)</summary>
        public float lastSkillUseTime;
        
        // Active skill state
        /// <summary>Currently casting skill slot index (-1 if idle)</summary>
        public int activeSkillSlotIndex;
        /// <summary>Whether the agent is currently casting a skill</summary>
        public bool isCasting;

        // ═══ FLIGHT STATE (3D navigation / persistent flyers) ═══

        /// <summary>Whether the agent is currently in flight mode</summary>
        public bool isFlying;
        /// <summary>Current flight altitude (Y offset from ground or absolute Y). 0 when grounded.</summary>
        public float flightHeight;
    }

    /// <summary>
    /// Resource state for a single resource type on an agent.
    /// Used inside AgentStateSnapshot for network reconciliation.
    /// </summary>
    [System.Serializable]
    public struct AgentResourceState
    {
        /// <summary>Resource name (e.g. "Mana", "Energy")</summary>
        public string resourceName;
        /// <summary>
        /// Integer key from <see cref="NetworkStringRegistry"/> for fast comparison.
        /// Set via <see cref="SetResourceName"/> or by assigning manually after
        /// calling <see cref="NetworkStringRegistry.GetOrRegister"/>.
        /// </summary>
        public int resourceKey;
        /// <summary>Current resource value</summary>
        public float current;
        /// <summary>Maximum resource value</summary>
        public float max;

        /// <summary>
        /// Sets both <see cref="resourceName"/> and <see cref="resourceKey"/> in one call.
        /// </summary>
        public void SetResourceName(string name)
        {
            resourceName = name;
            resourceKey = NetworkStringRegistry.GetOrRegister(name);
        }
    }

    /// <summary>
    /// A single stat entry for scaling formula reconciliation.
    /// Carries the stat name and its current value.
    /// </summary>
    [System.Serializable]
    public struct AgentStatEntry
    {
        /// <summary>Stat name (e.g. "Strength", "Intelligence")</summary>
        public string statName;
        /// <summary>
        /// Integer key from <see cref="NetworkStringRegistry"/> for fast comparison.
        /// </summary>
        public int statKey;
        /// <summary>Current stat value</summary>
        public float value;

        /// <summary>
        /// Sets both <see cref="statName"/> and <see cref="statKey"/> in one call.
        /// </summary>
        public void SetStatName(string name)
        {
            statName = name;
            statKey = NetworkStringRegistry.GetOrRegister(name);
        }
    }

    /// <summary>
    /// Per-slot cooldown state for network reconciliation.
    /// </summary>
    [System.Serializable]
    public struct AgentSkillCooldownState
    {
        /// <summary>Slot index (0-based)</summary>
        public int slotIndex;
        /// <summary>Cooldown end time (server time). 0 = not on cooldown.</summary>
        public float cooldownEndTime;
        /// <summary>Total cooldown duration (for UI progress display)</summary>
        public float cooldownDuration;
    }

    /// <summary>
    /// Flags for delta compression - only sync fields that changed.
    /// </summary>
    [System.Flags]
    public enum AgentStateDeltaFlags : ushort
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Velocity = 1 << 2,
        Health = 1 << 3,
        DeathState = 1 << 4,
        CombatState = 1 << 5,
        Target = 1 << 6,
        NavigationState = 1 << 7,
        Destination = 1 << 8,
        StatusEffects = 1 << 9,
        Invulnerable = 1 << 10,
        
        // Skill-relevant state (added for competitive multiplayer)
        ResourceState = 1 << 11,
        LevelStats = 1 << 12,
        SkillCooldowns = 1 << 13,
        ActiveSkill = 1 << 14,

        // Flight state (3D navigation / persistent flyers)
        Flying = 1 << 15,
        
        // Common combinations
        Transform = Position | Rotation | Velocity,
        Combat = CombatState | Target,
        SkillState = ResourceState | LevelStats | SkillCooldowns | ActiveSkill,
        All = 0xFFFF
    }

    /// <summary>
    /// Reason for an urgent reconciliation.
    /// </summary>
    public enum ReconciliationReason
    {
        /// <summary>Periodic scheduled sync</summary>
        Scheduled,
        /// <summary>Health divergence detected</summary>
        HealthDivergence,
        /// <summary>Death state mismatch</summary>
        DeathMismatch,
        /// <summary>Position mismatch (speed hack suspected)</summary>
        PositionMismatch,
        /// <summary>Combat state invalid</summary>
        InvalidCombatState,
        /// <summary>Requested by client</summary>
        ClientRequest,
        /// <summary>Anti-cheat triggered</summary>
        AntiCheat,
        /// <summary>Resource state divergence (mana/energy desync)</summary>
        ResourceDivergence,
        /// <summary>Cooldown state mismatch</summary>
        CooldownMismatch,
        /// <summary>Skill/scaling stat divergence</summary>
        StatDivergence
    }
}
