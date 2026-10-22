using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for batch state synchronization of agent data.
    /// Used to sync combat state, navigation state, health, and other
    /// non-transform data that changes less frequently.
    /// 
    /// This is separate from INetworkTransformAuthority to allow different
    /// sync rates (transforms sync faster, state syncs slower).
    /// </summary>
    public interface INetworkStateAuthority
    {
        /// <summary>
        /// Called at fixed intervals with agents whose state has changed.
        /// The network layer should batch and send to clients.
        /// </summary>
        /// <param name="dirtyStates">Array of changed agent states</param>
        /// <param name="serverTime">Server timestamp for this batch</param>
        void OnStateBatchUpdate(NetworkAgentState[] dirtyStates, float serverTime);

        /// <summary>
        /// Called when receiving state data from the server.
        /// Clients should update local agent states to match.
        /// </summary>
        /// <param name="states">Received agent states</param>
        /// <param name="serverTime">Server timestamp of this batch</param>
        void OnNetworkStateReceived(NetworkAgentState[] states, float serverTime);

        /// <summary>
        /// Called to request a full state sync for specific agents.
        /// Used when agents become relevant (enter fog visibility) or for error recovery.
        /// </summary>
        /// <param name="networkIds">Network IDs of agents to sync</param>
        void RequestFullStateSync(uint[] networkIds);

        /// <summary>
        /// Called when server sends full state for requested agents.
        /// </summary>
        /// <param name="states">Full agent states</param>
        void OnFullStateSyncReceived(NetworkAgentState[] states);

        /// <summary>
        /// Tick rate for state updates (updates per second).
        /// Typically lower than transform sync (5-10 ticks/sec vs 20 for transforms).
        /// </summary>
        int StateSyncTickRate { get; }

        /// <summary>
        /// Whether to use delta compression (only send changed fields).
        /// </summary>
        bool UseDeltaCompression { get; }

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }
    }

    /// <summary>
    /// Complete state snapshot for a networked agent.
    /// Used for state synchronization across the network.
    /// </summary>
    [System.Serializable]
    public struct NetworkAgentState
    {
        /// <summary>Network ID of the agent</summary>
        public uint networkId;
        
        /// <summary>Current health value</summary>
        public float health;
        
        /// <summary>Maximum health value</summary>
        public float maxHealth;
        
        /// <summary>Whether the agent is dead</summary>
        public bool isDead;
        
        /// <summary>Current combat state</summary>
        public CrowdCombatState combatState;
        
        /// <summary>Current navigation state</summary>
        public AgentNavigationState navigationState;
        
        /// <summary>Network ID of current attack target (-1 if none)</summary>
        public int targetNetworkId;
        
        /// <summary>Current animation state</summary>
        public CrowdAnimationState animationState;
        
        /// <summary>Whether the agent is blocking</summary>
        public bool isBlocking;
        
        /// <summary>Owner player ID (-1 for AI/server)</summary>
        public int ownerPlayerId;
        
        /// <summary>Faction/crowd index</summary>
        public int factionIndex;
        
        // ═══ SKILL-RELEVANT STATE (added for competitive multiplayer) ═══
        
        /// <summary>Agent level for scaling formula computation</summary>
        public int level;
        
        /// <summary>Per-resource current/max values. Null if no resources.</summary>
        public AgentResourceState[] resourceState;
        
        /// <summary>
        /// Monotonically increasing version counter for resource state.
        /// Callers should increment when any resource value changes.
        /// When non-zero and equal on both sides, CompareTo skips the element-wise comparison.
        /// </summary>
        public uint resourceStateVersion;
        
        /// <summary>Per-slot cooldown end times. Null if no skills.</summary>
        public AgentSkillCooldownState[] skillCooldownState;
        
        /// <summary>
        /// Monotonically increasing version counter for cooldown state.
        /// Callers should increment when any cooldown changes.
        /// When non-zero and equal on both sides, CompareTo skips the element-wise comparison.
        /// </summary>
        public uint cooldownStateVersion;
        
        /// <summary>Currently casting skill slot index (-1 if idle)</summary>
        public int activeSkillSlotIndex;
        
        /// <summary>Whether the agent is currently casting a skill</summary>
        public bool isCasting;
        
        /// <summary>Whether the agent is currently in flight mode</summary>
        public bool isFlying;

        /// <summary>Whether the agent is a persistent flyer (never lands unless explicitly triggered)</summary>
        public bool isPersistentFlyer;

        /// </summary>
        public ushort deltaFlags;

        // Delta flag constants
        public const ushort DELTA_HEALTH = 1 << 0;
        public const ushort DELTA_DEAD = 1 << 1;
        public const ushort DELTA_COMBAT_STATE = 1 << 2;
        public const ushort DELTA_NAV_STATE = 1 << 3;
        public const ushort DELTA_TARGET = 1 << 4;
        public const ushort DELTA_ANIMATION = 1 << 5;
        public const ushort DELTA_BLOCKING = 1 << 6;
        public const ushort DELTA_OWNER = 1 << 7;
        public const ushort DELTA_FACTION = 1 << 8;
        public const ushort DELTA_RESOURCE = 1 << 9;
        public const ushort DELTA_LEVEL = 1 << 10;
        public const ushort DELTA_COOLDOWNS = 1 << 11;
        public const ushort DELTA_ACTIVE_SKILL = 1 << 12;
        public const ushort DELTA_FLYING = 1 << 13;
        public const ushort DELTA_ALL = 0xFFFF;

        public bool HasHealthChange => (deltaFlags & DELTA_HEALTH) != 0;
        public bool HasDeadChange => (deltaFlags & DELTA_DEAD) != 0;
        public bool HasCombatStateChange => (deltaFlags & DELTA_COMBAT_STATE) != 0;
        public bool HasNavStateChange => (deltaFlags & DELTA_NAV_STATE) != 0;
        public bool HasTargetChange => (deltaFlags & DELTA_TARGET) != 0;
        public bool HasAnimationChange => (deltaFlags & DELTA_ANIMATION) != 0;
        public bool HasBlockingChange => (deltaFlags & DELTA_BLOCKING) != 0;
        public bool HasOwnerChange => (deltaFlags & DELTA_OWNER) != 0;
        public bool HasFactionChange => (deltaFlags & DELTA_FACTION) != 0;
        public bool HasResourceChange => (deltaFlags & DELTA_RESOURCE) != 0;
        public bool HasLevelChange => (deltaFlags & DELTA_LEVEL) != 0;
        public bool HasCooldownChange => (deltaFlags & DELTA_COOLDOWNS) != 0;
        public bool HasActiveSkillChange => (deltaFlags & DELTA_ACTIVE_SKILL) != 0;
        public bool HasFlyingChange => (deltaFlags & DELTA_FLYING) != 0;

        /// <summary>
        /// Creates a full state snapshot with all fields marked as changed.
        /// </summary>
        public static NetworkAgentState CreateFull(uint networkId, float health, float maxHealth,
            bool isDead, CrowdCombatState combatState, AgentNavigationState navState,
            int targetNetworkId, CrowdAnimationState animState, bool isBlocking,
            int ownerPlayerId, int factionIndex)
        {
            return new NetworkAgentState
            {
                networkId = networkId,
                health = health,
                maxHealth = maxHealth,
                isDead = isDead,
                combatState = combatState,
                navigationState = navState,
                targetNetworkId = targetNetworkId,
                animationState = animState,
                isBlocking = isBlocking,
                ownerPlayerId = ownerPlayerId,
                factionIndex = factionIndex,
                deltaFlags = DELTA_ALL
            };
        }

        /// <summary>
        /// Compares with another state and returns delta flags indicating differences.
        /// </summary>
        public ushort CompareTo(NetworkAgentState other)
        {
            ushort flags = 0;
            
            if (Mathf.Abs(health - other.health) > 0.01f) flags |= DELTA_HEALTH;
            if (isDead != other.isDead) flags |= DELTA_DEAD;
            if (combatState != other.combatState) flags |= DELTA_COMBAT_STATE;
            if (navigationState != other.navigationState) flags |= DELTA_NAV_STATE;
            if (targetNetworkId != other.targetNetworkId) flags |= DELTA_TARGET;
            if (animationState != other.animationState) flags |= DELTA_ANIMATION;
            if (isBlocking != other.isBlocking) flags |= DELTA_BLOCKING;
            if (ownerPlayerId != other.ownerPlayerId) flags |= DELTA_OWNER;
            if (factionIndex != other.factionIndex) flags |= DELTA_FACTION;
            if (level != other.level) flags |= DELTA_LEVEL;
            if (activeSkillSlotIndex != other.activeSkillSlotIndex ||
                isCasting != other.isCasting) flags |= DELTA_ACTIVE_SKILL;
            if (isFlying != other.isFlying ||
                isPersistentFlyer != other.isPersistentFlyer) flags |= DELTA_FLYING;
            // Fast-path: if both sides have the same non-zero generation counter
            // the arrays are unchanged — skip the expensive element-wise comparison.
            if (resourceStateVersion != 0 && resourceStateVersion == other.resourceStateVersion)
            { /* resource arrays match — skip */ }
            else if (!ResourceArrayEquals(resourceState, other.resourceState))
                flags |= DELTA_RESOURCE;
            
            if (cooldownStateVersion != 0 && cooldownStateVersion == other.cooldownStateVersion)
            { /* cooldown arrays match — skip */ }
            else if (!CooldownArrayEquals(skillCooldownState, other.skillCooldownState))
                flags |= DELTA_COOLDOWNS;
            
            return flags;
        }
        
        private static bool ResourceArrayEquals(AgentResourceState[] a, AgentResourceState[] b)
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
        
        private static bool CooldownArrayEquals(AgentSkillCooldownState[] a, AgentSkillCooldownState[] b)
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
}
