using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for server-authoritative agent spawning and despawning.
    /// Implement this interface in your networking solution (Fusion, Mirror, Netcode, etc.)
    /// and assign it to EnemyMassesCrowdController to enable networked spawning.
    /// 
    /// When no authority is assigned, spawning works locally (single-player mode).
    /// </summary>
    public interface INetworkSpawnAuthority
    {
        /// <summary>
        /// Called before a local spawn attempt. Return false to prevent local spawning
        /// (client should request spawn from server instead).
        /// 
        /// In a server-authoritative setup:
        /// - Server returns true (spawn proceeds locally)
        /// - Client returns false (spawn request sent to server)
        /// </summary>
        /// <param name="factionIndex">Index of the EnemyCrowd to spawn</param>
        /// <param name="position">Requested spawn position</param>
        /// <param name="rotation">Requested spawn rotation</param>
        /// <param name="count">Number of agents to spawn</param>
        /// <param name="requestingPlayerId">Player ID requesting the spawn (-1 for server/AI)</param>
        /// <returns>True to allow local spawn, false to defer to server</returns>
        bool CanSpawnLocally(int factionIndex, Vector3 position, Quaternion rotation, 
            int count, int requestingPlayerId);

        /// <summary>
        /// Called after agents are spawned locally. Use this to:
        /// - Assign network IDs to the spawned agents
        /// - Broadcast spawn data to other clients
        /// - Register agents with the network state system
        /// </summary>
        /// <param name="agentIndices">Local indices of the spawned agents</param>
        /// <param name="factionIndex">Index of the EnemyCrowd that was spawned</param>
        /// <param name="positions">Spawn positions for each agent</param>
        /// <param name="rotations">Spawn rotations for each agent</param>
        /// <param name="ownerPlayerId">Player ID that owns these agents (-1 for server/AI)</param>
        /// <returns>Network IDs assigned to each agent (same order as agentIndices)</returns>
        uint[] OnAgentsSpawned(int[] agentIndices, int factionIndex, Vector3[] positions, 
            Quaternion[] rotations, int ownerPlayerId);

        /// <summary>
        /// Called on clients when the server requests spawning agents.
        /// The client should create the agents locally with the provided network IDs.
        /// </summary>
        /// <param name="networkIds">Pre-assigned network IDs for each agent</param>
        /// <param name="factionIndex">Index of the EnemyCrowd to spawn</param>
        /// <param name="positions">Spawn positions for each agent</param>
        /// <param name="rotations">Spawn rotations for each agent</param>
        /// <param name="ownerPlayerId">Player ID that owns these agents</param>
        void OnNetworkSpawnRequest(uint[] networkIds, int factionIndex, Vector3[] positions, 
            Quaternion[] rotations, int ownerPlayerId);

        /// <summary>
        /// Called before an agent is despawned (death, cleanup, explicit removal).
        /// Use this to notify other clients and clean up network state.
        /// </summary>
        /// <param name="agentIndex">Local index of the agent being despawned</param>
        /// <param name="networkId">Network ID of the agent (0 if not networked)</param>
        /// <param name="reason">Reason for despawn</param>
        void OnAgentDespawning(int agentIndex, uint networkId, DespawnReason reason);

        /// <summary>
        /// Called on clients when the server despawns an agent.
        /// </summary>
        /// <param name="networkId">Network ID of the agent to despawn</param>
        /// <param name="reason">Reason for despawn</param>
        void OnNetworkDespawnRequest(uint networkId, DespawnReason reason);

        /// <summary>
        /// Generates a new unique network ID. Called by server when spawning.
        /// </summary>
        /// <returns>A unique network ID that hasn't been used</returns>
        uint GenerateNetworkId();

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }

        /// <summary>
        /// Returns the local player ID.
        /// </summary>
        int LocalPlayerId { get; }
    }

    /// <summary>
    /// Reason for agent despawn, used for proper cleanup and network messaging.
    /// </summary>
    public enum DespawnReason
    {
        /// <summary>Agent died from damage</summary>
        Death,
        /// <summary>Agent was explicitly removed by game logic</summary>
        Removed,
        /// <summary>Scene/level cleanup</summary>
        SceneUnload,
        /// <summary>Player disconnected</summary>
        PlayerDisconnect,
        /// <summary>Other/unspecified reason</summary>
        Other
    }
}
