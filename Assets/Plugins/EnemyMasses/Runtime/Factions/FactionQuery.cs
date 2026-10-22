using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Static helper class for quick faction relationship queries.
    /// Use this throughout EnemyMasses combat and targeting systems.
    /// 
    /// All methods delegate to FactionSystemManager.Instance, so the active
    /// provider (built-in, Love/Hate, GC2, etc.) handles the actual logic.
    /// </summary>
    public static class FactionQuery
    {
        #region Relationship Queries
        
        /// <summary>
        /// Gets the relationship from source to target.
        /// </summary>
        public static RelationshipType GetRelationship(CrowdAgent source, CrowdAgent target)
        {
            return FactionSystemManager.Instance.GetRelationship(source, target);
        }
        
        /// <summary>
        /// Gets the relationship from source to target by agent index.
        /// </summary>
        public static RelationshipType GetRelationship(int sourceIndex, int targetIndex)
        {
            return FactionSystemManager.Instance.GetRelationship(sourceIndex, targetIndex);
        }
        
        /// <summary>
        /// Gets the relationship between two factions.
        /// </summary>
        public static RelationshipType GetFactionRelationship(EnemyCrowd sourceFaction, EnemyCrowd targetFaction)
        {
            return FactionSystemManager.Instance.GetFactionRelationship(sourceFaction, targetFaction);
        }
        
        /// <summary>
        /// Gets the relationship between two factions by index.
        /// </summary>
        public static RelationshipType GetFactionRelationship(int sourceFactionIndex, int targetFactionIndex)
        {
            return FactionSystemManager.Instance.GetFactionRelationship(sourceFactionIndex, targetFactionIndex);
        }
        
        /// <summary>
        /// Gets an agent's relationship toward the player(s).
        /// </summary>
        public static RelationshipType GetPlayerRelationship(CrowdAgent agent)
        {
            return FactionSystemManager.Instance.GetPlayerRelationship(agent);
        }
        
        /// <summary>
        /// Gets a faction's relationship toward the player(s).
        /// </summary>
        public static RelationshipType GetPlayerRelationship(EnemyCrowd faction)
        {
            return FactionSystemManager.Instance.GetPlayerRelationship(faction);
        }
        
        /// <summary>
        /// Gets the relationship between an agent and an external object.
        /// </summary>
        public static RelationshipType GetRelationshipToExternal(CrowdAgent source, GameObject target)
        {
            return FactionSystemManager.Instance.GetRelationshipToExternal(source, target);
        }
        
        #endregion
        
        #region Combat Decisions
        
        /// <summary>
        /// Returns true if source should attack target.
        /// </summary>
        public static bool ShouldAttack(CrowdAgent source, CrowdAgent target)
        {
            return FactionSystemManager.Instance.ShouldAttack(source, target);
        }
        
        /// <summary>
        /// Returns true if source should support/heal target.
        /// </summary>
        public static bool ShouldSupport(CrowdAgent source, CrowdAgent target)
        {
            return FactionSystemManager.Instance.ShouldSupport(source, target);
        }
        
        /// <summary>
        /// Returns true if the relationship is hostile (enemy).
        /// </summary>
        public static bool IsHostile(CrowdAgent source, CrowdAgent target)
        {
            return GetRelationship(source, target).IsHostile();
        }
        
        /// <summary>
        /// Returns true if the relationship is friendly (ally or self).
        /// </summary>
        public static bool IsFriendly(CrowdAgent source, CrowdAgent target)
        {
            return GetRelationship(source, target).IsFriendly();
        }
        
        /// <summary>
        /// Returns true if the relationship is neutral.
        /// </summary>
        public static bool IsNeutral(CrowdAgent source, CrowdAgent target)
        {
            return GetRelationship(source, target).IsNeutral();
        }
        
        /// <summary>
        /// Returns true if two factions are enemies.
        /// </summary>
        public static bool AreFactionsHostile(int factionIndexA, int factionIndexB)
        {
            return GetFactionRelationship(factionIndexA, factionIndexB).IsHostile();
        }
        
        /// <summary>
        /// Returns true if two factions are allies.
        /// </summary>
        public static bool AreFactionsFriendly(int factionIndexA, int factionIndexB)
        {
            return GetFactionRelationship(factionIndexA, factionIndexB).IsFriendly();
        }
        
        #endregion
        
        #region Owner Queries
        
        /// <summary>
        /// Gets the owner player ID for an agent.
        /// Returns -1 if no owner (AI/server controlled).
        /// </summary>
        public static int GetOwner(CrowdAgent agent)
        {
            return FactionSystemManager.Instance.GetOwnerPlayerId(agent);
        }
        
        /// <summary>
        /// Sets the owner player ID for an agent.
        /// </summary>
        public static void SetOwner(CrowdAgent agent, int playerId)
        {
            FactionSystemManager.Instance.SetOwnerPlayerId(agent, playerId);
        }
        
        /// <summary>
        /// Returns true if two agents have the same owner.
        /// </summary>
        public static bool HaveSameOwner(CrowdAgent agentA, CrowdAgent agentB)
        {
            return FactionSystemManager.Instance.HaveSameOwner(agentA, agentB);
        }
        
        /// <summary>
        /// Gets all agents belonging to a specific player.
        /// </summary>
        public static IEnumerable<CrowdAgent> GetAgentsByOwner(int playerId)
        {
            return FactionSystemManager.Instance.GetAgentsByOwner(playerId);
        }
        
        #endregion
        
        #region Bulk Queries
        
        /// <summary>
        /// Gets all enemies of an agent.
        /// </summary>
        public static IEnumerable<CrowdAgent> GetEnemies(CrowdAgent agent)
        {
            return FactionSystemManager.Instance.GetAgentsByRelationship(agent, RelationshipFlags.Enemy);
        }
        
        /// <summary>
        /// Gets all allies of an agent (including self).
        /// </summary>
        public static IEnumerable<CrowdAgent> GetAllies(CrowdAgent agent)
        {
            return FactionSystemManager.Instance.GetAgentsByRelationship(agent, RelationshipFlags.Friendly);
        }
        
        /// <summary>
        /// Gets all agents matching the specified relationship flags.
        /// </summary>
        public static IEnumerable<CrowdAgent> GetAgentsByRelationship(CrowdAgent source, RelationshipFlags flags)
        {
            return FactionSystemManager.Instance.GetAgentsByRelationship(source, flags);
        }
        
        #endregion
        
        #region Utility
        
        /// <summary>
        /// Forces a refresh of all relationship caches.
        /// Call after modifying faction configurations at runtime.
        /// </summary>
        public static void RefreshRelationships()
        {
            FactionSystemManager.Instance.NotifyRelationshipsChanged();
        }
        
        /// <summary>
        /// Returns true if a third-party faction provider is active.
        /// </summary>
        public static bool UsingThirdPartyProvider => FactionSystemManager.Instance.UsingThirdPartyProvider;
        
        /// <summary>
        /// Gets the name of the active faction provider.
        /// </summary>
        public static string ActiveProviderName => FactionSystemManager.Instance.ActiveProvider?.DisplayName ?? "None";
        
        #endregion
    }
}
