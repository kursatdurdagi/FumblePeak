using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for server-authoritative RTS command validation.
    /// Routes move and attack commands through the server for validation.
    /// 
    /// Supports client-side prediction for responsiveness with server reconciliation.
    /// </summary>
    public interface INetworkCommandAuthority
    {
        /// <summary>
        /// Called when a player issues a move command.
        /// Server validates ownership and executes. Clients can predict locally.
        /// </summary>
        /// <param name="command">Move command data</param>
        /// <returns>True if command was accepted for local execution (prediction)</returns>
        bool OnMoveCommand(MoveCommandData command);

        /// <summary>
        /// Called when a player issues an attack command.
        /// Server validates target and ownership, then executes.
        /// </summary>
        /// <param name="command">Attack command data</param>
        /// <returns>True if command was accepted for local execution (prediction)</returns>
        bool OnAttackCommand(AttackCommandData command);

        /// <summary>
        /// Called when a player issues a stop/hold command.
        /// </summary>
        /// <param name="command">Stop command data</param>
        /// <returns>True if command was accepted</returns>
        bool OnStopCommand(StopCommandData command);

        /// <summary>
        /// Called when the server confirms a command was executed.
        /// </summary>
        /// <param name="commandSequence">Sequence number of the confirmed command</param>
        /// <param name="success">Whether the command succeeded</param>
        /// <param name="result">Additional result data</param>
        void OnCommandConfirmed(int commandSequence, bool success, CommandResult result);

        /// <summary>
        /// Called when a command needs to be rolled back (prediction was wrong).
        /// </summary>
        /// <param name="commandSequence">Sequence number of the command to rollback</param>
        /// <param name="reason">Why the command was rejected</param>
        void OnCommandRollback(int commandSequence, CommandRejectReason reason);

        /// <summary>
        /// Called on clients when server broadcasts a command for remote players.
        /// Used to show other players' unit movements.
        /// </summary>
        /// <param name="command">The command issued by another player</param>
        void OnRemoteCommandReceived(RemoteCommandData command);

        /// <summary>
        /// Generates a sequence number for command tracking.
        /// </summary>
        int GetNextCommandSequence();

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }

        /// <summary>
        /// Local player ID for ownership validation.
        /// </summary>
        int LocalPlayerId { get; }
    }

    /// <summary>
    /// Data for a move command.
    /// </summary>
    [System.Serializable]
    public struct MoveCommandData
    {
        /// <summary>Network IDs of agents to move</summary>
        public uint[] agentNetworkIds;
        
        /// <summary>Target destination</summary>
        public Vector3 destination;
        
        /// <summary>Formation type to use</summary>
        public FormationType formation;
        
        /// <summary>Formation spacing</summary>
        public float formationSpacing;
        
        /// <summary>Optional facing direction (zero = face movement direction)</summary>
        public Vector3 facingDirection;
        
        /// <summary>Player ID issuing the command</summary>
        public int playerId;
        
        /// <summary>Sequence number for tracking</summary>
        public int sequence;
        
        /// <summary>Client timestamp when command was issued</summary>
        public float clientTime;
    }

    /// <summary>
    /// Data for an attack command.
    /// </summary>
    [System.Serializable]
    public struct AttackCommandData
    {
        /// <summary>Network IDs of attacking agents</summary>
        public uint[] attackerNetworkIds;
        
        /// <summary>Network ID of the target agent</summary>
        public uint targetNetworkId;
        
        /// <summary>Player ID issuing the command</summary>
        public int playerId;
        
        /// <summary>Sequence number for tracking</summary>
        public int sequence;
        
        /// <summary>Client timestamp when command was issued</summary>
        public float clientTime;
    }

    /// <summary>
    /// Data for a stop/hold command.
    /// </summary>
    [System.Serializable]
    public struct StopCommandData
    {
        /// <summary>Network IDs of agents to stop</summary>
        public uint[] agentNetworkIds;
        
        /// <summary>Whether to hold position (true) or just stop movement (false)</summary>
        public bool holdPosition;
        
        /// <summary>Player ID issuing the command</summary>
        public int playerId;
        
        /// <summary>Sequence number for tracking</summary>
        public int sequence;
    }

    /// <summary>
    /// Result data from a confirmed command.
    /// </summary>
    [System.Serializable]
    public struct CommandResult
    {
        /// <summary>Server-adjusted destination (may differ from requested)</summary>
        public Vector3 adjustedDestination;
        
        /// <summary>Agents that successfully received the command</summary>
        public uint[] successfulAgents;
        
        /// <summary>Agents that failed to receive the command</summary>
        public uint[] failedAgents;
        
        /// <summary>Server timestamp of execution</summary>
        public float serverTime;
    }

    /// <summary>
    /// Data for commands issued by remote players (received on clients).
    /// </summary>
    [System.Serializable]
    public struct RemoteCommandData
    {
        /// <summary>Type of command</summary>
        public CommandType commandType;
        
        /// <summary>Network IDs of agents involved</summary>
        public uint[] agentNetworkIds;
        
        /// <summary>Target position (for move commands)</summary>
        public Vector3 targetPosition;
        
        /// <summary>Target agent (for attack commands)</summary>
        public uint targetNetworkId;
        
        /// <summary>Player ID who issued the command</summary>
        public int playerId;
    }

    /// <summary>
    /// Type of RTS command.
    /// </summary>
    public enum CommandType
    {
        Move,
        Attack,
        Stop,
        Patrol,
        AttackMove
    }

    /// <summary>
    /// Reason a command was rejected by the server.
    /// </summary>
    public enum CommandRejectReason
    {
        /// <summary>Player doesn't own the commanded agents</summary>
        NotOwner,
        /// <summary>One or more agents are invalid (dead, despawned)</summary>
        InvalidAgents,
        /// <summary>Target is invalid (dead, despawned, out of range)</summary>
        InvalidTarget,
        /// <summary>Destination is unreachable</summary>
        UnreachableDestination,
        /// <summary>Command conflicts with game rules</summary>
        RuleViolation,
        /// <summary>Rate limiting (too many commands)</summary>
        RateLimited,
        /// <summary>Other/unspecified reason</summary>
        Other
    }
}
