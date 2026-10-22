using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for server-authoritative skill execution validation.
    /// Routes skill execution requests through the server for validation,
    /// preventing clients from executing skills without authorization.
    ///
    /// Flow:
    /// 1. Client calls OnSkillExecutionRequest() — returns true for local prediction
    /// 2. Request sent to server
    /// 3. Server validates (ownership, cooldown, resource, range) and calls OnSkillExecutionConfirmed()
    /// 4. If prediction was wrong, OnSkillExecutionRejected() is called for rollback
    ///
    /// Resource and cooldown state are validated server-side. Clients receive
    /// periodic resource/cooldown sync via OnResourceStateSync() and OnCooldownStateSync().
    /// </summary>
    public interface INetworkSkillAuthority
    {
        /// <summary>
        /// Called when a player requests skill execution (client → server).
        /// Return true to apply the skill locally as a prediction (for responsiveness).
        /// Return false to wait for server confirmation before any local effect.
        ///
        /// Server always returns true and processes immediately.
        /// </summary>
        /// <param name="request">Skill execution request data</param>
        /// <returns>True to predict locally, false to wait for server</returns>
        bool OnSkillExecutionRequest(SkillExecutionRequestData request);

        /// <summary>
        /// Called when the server confirms a skill execution.
        /// Clients should apply (or reconcile) the skill result.
        /// </summary>
        /// <param name="confirmation">Confirmed execution data from server</param>
        void OnSkillExecutionConfirmed(SkillExecutionConfirmationData confirmation);

        /// <summary>
        /// Called when the server rejects a skill execution request.
        /// Client should rollback any predicted effects and show feedback.
        /// </summary>
        /// <param name="rejection">Rejection data with reason</param>
        void OnSkillExecutionRejected(SkillExecutionRejectionData rejection);

        /// <summary>
        /// Called by the server to broadcast resource state to a specific client.
        /// Keeps client-side UI (mana bars, skill availability) in sync.
        /// </summary>
        /// <param name="states">Array of resource snapshots for the client's agents</param>
        /// <param name="serverTime">Server timestamp for this sync</param>
        void OnResourceStateSync(ResourceStateSnapshot[] states, float serverTime);

        /// <summary>
        /// Called by the server to broadcast cooldown state to a specific client.
        /// Keeps client-side cooldown timers accurate after lag spikes or desync.
        /// </summary>
        /// <param name="states">Array of cooldown snapshots for the client's agents</param>
        /// <param name="serverTime">Server timestamp for this sync</param>
        void OnCooldownStateSync(CooldownStateSnapshot[] states, float serverTime);

        /// <summary>
        /// Client requests a full resource + cooldown sync for specific agents.
        /// Used on reconnect, after lag spikes, or when agents become visible.
        /// </summary>
        /// <param name="agentNetworkIds">Network IDs of agents to sync</param>
        void RequestSkillStateSync(uint[] agentNetworkIds);

        /// <summary>
        /// Whether to predict skill execution locally on the client.
        /// If true, the client applies visual/audio effects immediately and
        /// rolls back if the server rejects. If false, waits for confirmation.
        /// </summary>
        bool EnableSkillPrediction { get; }

        /// <summary>
        /// Tick rate for resource/cooldown state sync (updates per second).
        /// Lower values save bandwidth. Typical: 2–5 for RTS, 5–10 for fast-paced.
        /// </summary>
        int SkillStateSyncTickRate { get; }

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }

        // ═══════════════════════════════════════════════════════════════════════
        // BROADCASTS — Server → Clients (after successful execution)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called on the server after a skill is successfully executed.
        /// Broadcasts the cast event to all clients so they can play VFX/animations
        /// for agents they can see. Implementors should serialize and send to all
        /// connected clients (or only those with visibility of the caster).
        /// </summary>
        /// <param name="castData">Serializable skill cast data for network transport</param>
        void BroadcastSkillCast(NetworkSkillCastEventData castData);

        /// <summary>
        /// Called on the server after a skill is successfully executed.
        /// Pushes the authoritative cooldown state for the caster to all clients
        /// that own or observe that agent. This provides immediate cooldown sync
        /// rather than waiting for the periodic tick-based sync.
        /// </summary>
        /// <param name="snapshot">Cooldown state of the caster agent after execution</param>
        void PushCooldownState(CooldownStateSnapshot snapshot);

        /// <summary>
        /// Called on a client when a skill cast broadcast is received from the server.
        /// The client should resolve the caster agent from <paramref name="castData"/>.casterNetworkId,
        /// then play the appropriate VFX/SFX/animation for that skill.
        /// 
        /// This is only called for remote agents — the local player's own agents
        /// play effects immediately during local execution or prediction.
        /// </summary>
        /// <param name="castData">Skill cast data received from server</param>
        void OnRemoteSkillCastReceived(NetworkSkillCastEventData castData);

        /// <summary>
        /// Called on a client when an immediate cooldown push is received from the server
        /// (in response to a skill execution). The client should apply the authoritative
        /// cooldown end times to the local cooldown trackers for the specified agent.
        /// </summary>
        /// <param name="snapshot">Authoritative cooldown state from server</param>
        void OnCooldownStatePush(CooldownStateSnapshot snapshot);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SKILL EXECUTION — Request / Confirmation / Rejection
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Data for a client → server skill execution request.
    /// The client sends this when a player (or auto-cast) wants to use a skill.
    /// </summary>
    [System.Serializable]
    public struct SkillExecutionRequestData
    {
        /// <summary>Network ID of the caster agent</summary>
        public uint casterNetworkId;

        /// <summary>
        /// Skill identifier (AgentSkill.SkillId — the ScriptableObject asset name).
        /// The server resolves this to the actual AgentSkill on its side.
        /// </summary>
        public string skillId;

        /// <summary>Slot index in the caster's skill loadout (0-based)</summary>
        public int slotIndex;
        
        /// <summary>Network ID of the primary target agent (0 if ground-targeted or self)</summary>
        public uint targetNetworkId;

        /// <summary>
        /// Ground target position (for GroundTargetedAOE skills).
        /// Ignored for non-ground-targeted skills.
        /// </summary>
        public Vector3 groundTargetPosition;

        /// <summary>Whether a ground target position is provided</summary>
        public bool hasGroundTarget;

        /// <summary>Player ID of the requesting player (for ownership validation)</summary>
        public int playerId;

        /// <summary>Sequence number for tracking request → confirmation/rejection</summary>
        public int sequence;

        /// <summary>Client timestamp when the request was issued</summary>
        public float clientTime;
    }

    /// <summary>
    /// Data sent from server → clients when a skill execution is confirmed.
    /// Includes the authoritative result so clients can reconcile predictions.
    /// </summary>
    [System.Serializable]
    public struct SkillExecutionConfirmationData
    {
        /// <summary>Network ID of the caster agent</summary>
        public uint casterNetworkId;

        /// <summary>Skill identifier</summary>
        public string skillId;

        /// <summary>Slot index</summary>
        public int slotIndex;

        /// <summary>Sequence number matching the original request</summary>
        public int sequence;

        /// <summary>Whether the skill was a critical hit (server-rolled)</summary>
        public bool isCritical;

        /// <summary>Final damage/heal multiplier applied by server</summary>
        public float effectMultiplier;

        /// <summary>Authoritative target position used by server</summary>
        public Vector3 targetPosition;

        /// <summary>Network IDs of all agents affected by the skill</summary>
        public uint[] affectedAgentNetworkIds;

        /// <summary>Resource remaining on caster after cost deduction</summary>
        public float remainingResource;

        /// <summary>Cooldown end time (server time) for this skill slot</summary>
        public float cooldownEndTime;

        /// <summary>Server timestamp of execution</summary>
        public float serverTime;
    }

    /// <summary>
    /// Data sent from server → client when a skill execution request is rejected.
    /// Client should undo any predicted effects and optionally show UI feedback.
    /// </summary>
    [System.Serializable]
    public struct SkillExecutionRejectionData
    {
        /// <summary>Network ID of the caster agent</summary>
        public uint casterNetworkId;

        /// <summary>Skill identifier</summary>
        public string skillId;

        /// <summary>Slot index</summary>
        public int slotIndex;

        /// <summary>Sequence number matching the original request</summary>
        public int sequence;

        /// <summary>Why the server rejected the request</summary>
        public SkillRejectReason reason;

        /// <summary>Server timestamp of rejection</summary>
        public float serverTime;
    }

    /// <summary>
    /// Reason a skill execution request was rejected by the server.
    /// </summary>
    public enum SkillRejectReason
    {
        /// <summary>Player doesn't own the caster agent</summary>
        NotOwner,
        /// <summary>Skill is currently on cooldown</summary>
        OnCooldown,
        /// <summary>Insufficient resource (mana, energy, etc.)</summary>
        InsufficientResource,
        /// <summary>Target is invalid (dead, out of range, wrong faction)</summary>
        InvalidTarget,
        /// <summary>Caster is invalid (dead, stunned, silenced)</summary>
        InvalidCaster,
        /// <summary>Skill not found in caster's loadout</summary>
        SkillNotFound,
        /// <summary>Skill slot is disabled</summary>
        SlotDisabled,
        /// <summary>Global cooldown active</summary>
        GlobalCooldown,
        /// <summary>Rate limited (too many skill requests)</summary>
        RateLimited,
        /// <summary>Anti-cheat validation failed</summary>
        AntiCheatBlocked,
        /// <summary>Other/unspecified reason</summary>
        Other
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STATE SYNC — Resource & Cooldown snapshots
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Snapshot of an agent's resource state for network synchronization.
    /// Sent periodically from server → client to keep client-side UI accurate.
    /// </summary>
    [System.Serializable]
    public struct ResourceStateSnapshot
    {
        /// <summary>Network ID of the agent</summary>
        public uint networkId;

        /// <summary>Resource entries for this agent (one per resource type)</summary>
        public ResourceEntry[] resources;

        /// <summary>Server time when snapshot was taken</summary>
        public float serverTime;
    }

    /// <summary>
    /// A single resource type's current and max values.
    /// </summary>
    [System.Serializable]
    public struct ResourceEntry
    {
        /// <summary>Resource name (e.g. "Mana", "Energy", "Rage")</summary>
        public string resourceName;

        /// <summary>
        /// Integer key from <see cref="NetworkStringRegistry"/> for fast comparison.
        /// Set via <see cref="SetResourceName"/>.
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
    /// Snapshot of an agent's skill cooldown state for network synchronization.
    /// Sent periodically from server → client to keep cooldown timers accurate.
    /// </summary>
    [System.Serializable]
    public struct CooldownStateSnapshot
    {
        /// <summary>Network ID of the agent</summary>
        public uint networkId;

        /// <summary>Cooldown entries for this agent's skills</summary>
        public CooldownEntry[] cooldowns;

        /// <summary>Global cooldown end time (server time)</summary>
        public float globalCooldownEndTime;

        /// <summary>Server time when snapshot was taken</summary>
        public float serverTime;
    }

    /// <summary>
    /// Cooldown state for a single skill slot.
    /// </summary>
    [System.Serializable]
    public struct CooldownEntry
    {
        /// <summary>Slot index (0-based)</summary>
        public int slotIndex;

        /// <summary>Skill identifier</summary>
        public string skillId;

        /// <summary>
        /// Integer key from <see cref="NetworkStringRegistry"/> for fast comparison.
        /// Set via <see cref="SetSkillId"/>.
        /// </summary>
        public int skillKey;

        /// <summary>When the cooldown ends (server time). 0 = not on cooldown.</summary>
        public float cooldownEndTime;

        /// <summary>Total cooldown duration (for UI progress display)</summary>
        public float cooldownDuration;

        /// <summary>
        /// Sets both <see cref="skillId"/> and <see cref="skillKey"/> in one call.
        /// </summary>
        public void SetSkillId(string id)
        {
            skillId = id;
            skillKey = NetworkStringRegistry.GetOrRegister(id);
        }
    }
}
