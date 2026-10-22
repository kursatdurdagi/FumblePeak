using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Core interface that defines how EnemyMasses queries faction relationships.
    /// Implement this interface to provide custom faction logic or integrate with
    /// third-party systems like Love/Hate or NinjutsuGames Factions.
    /// 
    /// EnemyMasses only needs to know: "Is agent A friend or foe of agent B?"
    /// All the complex faction logic, reputation systems, etc. are handled by the provider.
    /// </summary>
    public interface IFactionProvider
    {
        /// <summary>
        /// Gets the relationship between two agents.
        /// This is the primary method used by EnemyMasses combat and targeting systems.
        /// </summary>
        /// <param name="sourceAgent">The agent asking about the relationship (e.g., attacker)</param>
        /// <param name="targetAgent">The agent being queried about (e.g., potential target)</param>
        /// <returns>The relationship type from source's perspective toward target</returns>
        RelationshipType GetRelationship(CrowdAgent sourceAgent, CrowdAgent targetAgent);
        
        /// <summary>
        /// Gets the relationship between two agents by their indices.
        /// Faster version when you only have agent indices available.
        /// </summary>
        /// <param name="sourceAgentIndex">Index of the source agent</param>
        /// <param name="targetAgentIndex">Index of the target agent</param>
        /// <returns>The relationship type from source's perspective toward target</returns>
        RelationshipType GetRelationship(int sourceAgentIndex, int targetAgentIndex);
        
        /// <summary>
        /// Gets the relationship between an agent and an external object (e.g., player character).
        /// Used for player targeting and damageable object interactions.
        /// </summary>
        /// <param name="sourceAgent">The agent asking about the relationship</param>
        /// <param name="targetObject">The external GameObject (player, structure, etc.)</param>
        /// <returns>The relationship type from source's perspective toward target</returns>
        RelationshipType GetRelationshipToExternal(CrowdAgent sourceAgent, GameObject targetObject);
        
        /// <summary>
        /// Gets the relationship between two EnemyCrowd factions.
        /// Used for faction-level decisions when specific agent context isn't available.
        /// </summary>
        /// <param name="sourceFaction">The faction asking about the relationship</param>
        /// <param name="targetFaction">The faction being queried about</param>
        /// <returns>The default relationship type between these factions</returns>
        RelationshipType GetFactionRelationship(EnemyCrowd sourceFaction, EnemyCrowd targetFaction);
        
        /// <summary>
        /// Gets the relationship between two factions by their indices.
        /// </summary>
        /// <param name="sourceFactionIndex">Index of the source faction in EnemyCrowds list</param>
        /// <param name="targetFactionIndex">Index of the target faction in EnemyCrowds list</param>
        /// <returns>The default relationship type between these factions</returns>
        RelationshipType GetFactionRelationship(int sourceFactionIndex, int targetFactionIndex);
        
        /// <summary>
        /// Gets the relationship toward the player(s) for an agent.
        /// </summary>
        /// <param name="sourceAgent">The agent asking about player relationship</param>
        /// <returns>The relationship type toward player(s)</returns>
        RelationshipType GetPlayerRelationship(CrowdAgent sourceAgent);
        
        /// <summary>
        /// Gets the relationship toward the player(s) for a faction.
        /// </summary>
        /// <param name="sourceFaction">The faction asking about player relationship</param>
        /// <returns>The default relationship type toward player(s)</returns>
        RelationshipType GetPlayerRelationship(EnemyCrowd sourceFaction);
        
        /// <summary>
        /// Checks if the source agent should consider the target as a valid attack target.
        /// This is a convenience method that typically returns true for Enemy relationships.
        /// </summary>
        /// <param name="sourceAgent">The potential attacker</param>
        /// <param name="targetAgent">The potential target</param>
        /// <returns>True if source should attack target</returns>
        bool ShouldAttack(CrowdAgent sourceAgent, CrowdAgent targetAgent);
        
        /// <summary>
        /// Checks if the source agent should consider the target as a valid support/heal target.
        /// This is a convenience method that typically returns true for Ally/Self relationships.
        /// </summary>
        /// <param name="sourceAgent">The potential supporter/healer</param>
        /// <param name="targetAgent">The potential target of support</param>
        /// <returns>True if source should support target</returns>
        bool ShouldSupport(CrowdAgent sourceAgent, CrowdAgent targetAgent);
        
        /// <summary>
        /// Gets the owner/player ID for an agent.
        /// Returns -1 if the agent has no owner (AI/server controlled).
        /// Used for player-based faction systems where the same faction can belong to different players.
        /// </summary>
        /// <param name="agent">The agent to query</param>
        /// <returns>Owner player ID, or -1 if no owner</returns>
        int GetOwnerPlayerId(CrowdAgent agent);
        
        /// <summary>
        /// Sets the owner/player ID for an agent.
        /// </summary>
        /// <param name="agent">The agent to update</param>
        /// <param name="playerId">The new owner player ID (-1 for no owner)</param>
        void SetOwnerPlayerId(CrowdAgent agent, int playerId);
        
        /// <summary>
        /// Checks if two agents belong to the same owner/player.
        /// </summary>
        /// <param name="agentA">First agent</param>
        /// <param name="agentB">Second agent</param>
        /// <returns>True if both agents have the same owner (and owner is not -1)</returns>
        bool HaveSameOwner(CrowdAgent agentA, CrowdAgent agentB);
        
        /// <summary>
        /// Gets all agents belonging to a specific owner/player.
        /// </summary>
        /// <param name="playerId">The player ID to search for</param>
        /// <returns>Collection of agents owned by this player</returns>
        System.Collections.Generic.IEnumerable<CrowdAgent> GetAgentsByOwner(int playerId);
        
        /// <summary>
        /// Gets all agents that match a specific relationship relative to the source agent.
        /// </summary>
        /// <param name="sourceAgent">The reference agent</param>
        /// <param name="relationshipFlags">Which relationship types to include</param>
        /// <returns>Collection of agents matching the relationship criteria</returns>
        System.Collections.Generic.IEnumerable<CrowdAgent> GetAgentsByRelationship(CrowdAgent sourceAgent, RelationshipFlags relationshipFlags);
        
        /// <summary>
        /// The unique identifier for this provider.
        /// Used for serialization and provider selection in the inspector.
        /// </summary>
        string ProviderId { get; }
        
        /// <summary>
        /// Display name shown in the inspector dropdown.
        /// </summary>
        string DisplayName { get; }
        
        /// <summary>
        /// Priority used when multiple providers are available.
        /// Higher priority providers are checked first.
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// Called when the provider is activated.
        /// </summary>
        void Initialize();
        
        /// <summary>
        /// Called when the provider is deactivated or destroyed.
        /// </summary>
        void Shutdown();
        
        /// <summary>
        /// Returns true if this provider is properly configured and ready to use.
        /// </summary>
        bool IsAvailable { get; }
    }
}
