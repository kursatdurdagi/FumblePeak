using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for per-player fog of war visibility in multiplayer.
    /// Used for interest management - agents hidden by fog don't need full sync.
    /// 
    /// In competitive multiplayer, fog state is critical for fairness:
    /// - Clients should only receive data for agents they can see
    /// - Server maintains authoritative fog state per player
    /// - Prevents information cheating (seeing through fog)
    /// </summary>
    public interface INetworkFogAuthority
    {
        /// <summary>
        /// Returns true if the specified agent is visible to the local player.
        /// Used to determine if we should render/update an agent locally.
        /// </summary>
        /// <param name="networkId">Network ID of the agent to check</param>
        /// <returns>True if agent is visible to local player</returns>
        bool IsAgentVisibleToLocalPlayer(uint networkId);

        /// <summary>
        /// Returns true if a world position is revealed for the local player.
        /// </summary>
        /// <param name="worldPosition">World position to check</param>
        /// <returns>True if position is not covered by fog</returns>
        bool IsPositionVisibleToLocalPlayer(Vector3 worldPosition);

        /// <summary>
        /// Called when an agent's visibility changes for the local player.
        /// Use for interest management - subscribe/unsubscribe from updates.
        /// </summary>
        /// <param name="networkId">Network ID of the agent</param>
        /// <param name="isNowVisible">True if agent became visible, false if hidden</param>
        void OnAgentVisibilityChanged(uint networkId, bool isNowVisible);

        /// <summary>
        /// Called by server to update fog visibility for a specific player.
        /// </summary>
        /// <param name="playerId">Player ID to update fog for</param>
        /// <param name="revealedAgents">Network IDs of agents now visible</param>
        /// <param name="hiddenAgents">Network IDs of agents now hidden</param>
        void OnFogVisibilityUpdate(int playerId, uint[] revealedAgents, uint[] hiddenAgents);

        /// <summary>
        /// Returns all agents currently visible to the local player.
        /// Used for rendering and local simulation.
        /// </summary>
        /// <returns>Array of network IDs for visible agents</returns>
        uint[] GetVisibleAgentIds();

        /// <summary>
        /// Non-allocating overload — fills <paramref name="result"/> with visible agent IDs.
        /// Prefer this over the array-returning version in per-frame code paths.
        /// The list is cleared before being populated.
        /// </summary>
        void GetVisibleAgentIds(List<uint> result);

        /// <summary>
        /// Returns all agents owned by a specific player.
        /// Owner's agents are always "visible" to themselves for control purposes.
        /// </summary>
        /// <param name="playerId">Player ID to query</param>
        /// <returns>Array of network IDs for owned agents</returns>
        uint[] GetAgentsOwnedByPlayer(int playerId);

        /// <summary>
        /// Non-allocating overload — fills <paramref name="result"/> with owned agent IDs.
        /// The list is cleared before being populated.
        /// </summary>
        void GetAgentsOwnedByPlayer(int playerId, List<uint> result);

        /// <summary>
        /// Registers a fog revealer (building, watchtower, etc.) for a player.
        /// </summary>
        /// <param name="position">World position of the revealer</param>
        /// <param name="radius">Reveal radius in world units</param>
        /// <param name="ownerPlayerId">Player who owns this revealer</param>
        /// <param name="revealerId">Unique ID for this revealer (for later removal)</param>
        void RegisterFogRevealer(Vector3 position, float radius, int ownerPlayerId, 
            uint revealerId);

        /// <summary>
        /// Unregisters a fog revealer.
        /// </summary>
        /// <param name="revealerId">ID of the revealer to remove</param>
        void UnregisterFogRevealer(uint revealerId);

        /// <summary>
        /// Updates a fog revealer's position (for moving revealers).
        /// </summary>
        /// <param name="revealerId">ID of the revealer to update</param>
        /// <param name="newPosition">New world position</param>
        void UpdateFogRevealerPosition(uint revealerId, Vector3 newPosition);

        /// <summary>
        /// Local player ID for ownership checks.
        /// </summary>
        int LocalPlayerId { get; }

        /// <summary>
        /// Whether fog of war is enabled for this match.
        /// If false, all agents are always visible.
        /// </summary>
        bool FogOfWarEnabled { get; }

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }
    }

    /// <summary>
    /// Data for a fog revealer (unit, building, etc.).
    /// </summary>
    [System.Serializable]
    public struct FogRevealerData
    {
        /// <summary>Unique ID for this revealer</summary>
        public uint revealerId;
        
        /// <summary>World position</summary>
        public Vector3 position;
        
        /// <summary>Reveal radius in world units</summary>
        public float radius;
        
        /// <summary>Player who owns this revealer</summary>
        public int ownerPlayerId;
        
        /// <summary>Whether this revealer is currently active</summary>
        public bool isActive;
        
        /// <summary>Optional network ID if this is an agent</summary>
        public uint agentNetworkId;
    }
}
