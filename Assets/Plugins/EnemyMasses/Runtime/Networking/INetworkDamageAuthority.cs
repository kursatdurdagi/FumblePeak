using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for server-authoritative damage validation.
    /// Supports client-side prediction with server reconciliation.
    /// 
    /// Flow:
    /// 1. Client calls OnDamageAttempt() - returns true for local prediction
    /// 2. Damage request sent to server
    /// 3. Server validates and broadcasts OnDamageConfirmed()
    /// 4. If prediction was wrong, OnDamageRollback() is called
    /// </summary>
    public interface INetworkDamageAuthority
    {
        /// <summary>
        /// Called when damage is about to be applied locally.
        /// Return true to apply damage immediately (client prediction).
        /// Return false to wait for server confirmation.
        /// 
        /// Server always returns true and processes immediately.
        /// </summary>
        /// <param name="targetNetworkId">Network ID of the target agent</param>
        /// <param name="damage">Base damage amount (before multipliers)</param>
        /// <param name="attackerNetworkId">Network ID of attacker (0 if non-agent)</param>
        /// <param name="hitPoint">World position of the hit</param>
        /// <param name="damageType">Type of damage for special handling</param>
        /// <param name="sequence">Output sequence number for tracking this damage event</param>
        /// <returns>True to apply damage locally (prediction), false to wait</returns>
        bool OnDamageAttempt(uint targetNetworkId, float damage, uint attackerNetworkId,
            Vector3 hitPoint, DamageType damageType, out int sequence);

        /// <summary>
        /// Called when the server confirms damage was applied.
        /// Clients should update health to match server state.
        /// </summary>
        /// <param name="targetNetworkId">Network ID of the target agent</param>
        /// <param name="actualDamage">Actual damage dealt (after server validation)</param>
        /// <param name="newHealth">Authoritative health value after damage</param>
        /// <param name="isDead">Whether the agent died from this damage</param>
        /// <param name="sequence">Sequence number matching the original attempt</param>
        void OnDamageConfirmed(uint targetNetworkId, float actualDamage, float newHealth,
            bool isDead, int sequence);

        /// <summary>
        /// Called when client prediction was incorrect.
        /// Client should rollback to the correct health value.
        /// </summary>
        /// <param name="targetNetworkId">Network ID of the target agent</param>
        /// <param name="correctHealth">Correct health value from server</param>
        /// <param name="sequence">Sequence number of the incorrect prediction</param>
        /// <param name="reason">Why the prediction was wrong</param>
        void OnDamageRollback(uint targetNetworkId, float correctHealth, int sequence,
            DamageRollbackReason reason);

        /// <summary>
        /// Called when an agent dies on the server.
        /// This is the authoritative death event.
        /// </summary>
        /// <param name="networkId">Network ID of the dead agent</param>
        /// <param name="killerNetworkId">Network ID of the killer (0 if none)</param>
        /// <param name="killerPlayerId">Player ID of the killer (-1 if AI/environment)</param>
        /// <param name="deathPosition">World position where death occurred</param>
        void OnAgentDeath(uint networkId, uint killerNetworkId, int killerPlayerId,
            Vector3 deathPosition);

        /// <summary>
        /// Returns true if damage should be predicted locally for responsiveness.
        /// Set to false for strict server authority (more accurate but feels laggy).
        /// </summary>
        bool EnableClientPrediction { get; }

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }
    }

    /// <summary>
    /// Type of damage for network validation and special handling.
    /// Also used for resistance calculations on the server.
    /// </summary>
    public enum DamageType
    {
        /// <summary>Standard damage, no special handling</summary>
        Normal,
        /// <summary>Melee attack damage (uses Physical/Armor resistance)</summary>
        Melee,
        /// <summary>Ranged/projectile damage</summary>
        Ranged,
        /// <summary>Area of effect damage</summary>
        AOE,
        /// <summary>Damage over time tick</summary>
        DOT,
        /// <summary>Environmental/fall damage</summary>
        Environment,
        /// <summary>True damage (ignores armor/modifiers)</summary>
        True,
        
        // ═══ ELEMENTAL DAMAGE TYPES (for server-side resistance calculation) ═══
        /// <summary>Physical damage - reduced by Armor</summary>
        Physical = 10,
        /// <summary>Magical damage - reduced by Magic Resist</summary>
        Magical = 11,
        /// <summary>Fire damage - reduced by Fire Resistance (or Magic Resist)</summary>
        Fire = 12,
        /// <summary>Ice damage - reduced by Ice Resistance (or Magic Resist)</summary>
        Ice = 13,
        /// <summary>Lightning damage - reduced by Lightning Resistance (or Magic Resist)</summary>
        Lightning = 14,
        /// <summary>Poison damage - reduced by Poison Resistance</summary>
        Poison = 15
    }

    /// <summary>
    /// Reason why a damage prediction was rolled back.
    /// </summary>
    public enum DamageRollbackReason
    {
        /// <summary>Target was already dead on server</summary>
        TargetAlreadyDead,
        /// <summary>Target was invalid or not found</summary>
        InvalidTarget,
        /// <summary>Attacker couldn't deal damage (stunned, out of range, etc.)</summary>
        AttackerInvalid,
        /// <summary>Damage was blocked/parried on server</summary>
        DamageBlocked,
        /// <summary>Target had immunity or damage reduction</summary>
        DamageReduced,
        /// <summary>Anti-cheat validation failed</summary>
        ValidationFailed,
        /// <summary>Other/unspecified reason</summary>
        Other
    }
}
