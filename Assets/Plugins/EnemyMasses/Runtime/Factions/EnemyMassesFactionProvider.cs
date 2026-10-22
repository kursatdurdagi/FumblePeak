using System;
using System.Collections.Generic;
using UnityEngine;
#if MEMORYPACK
using MemoryPack;
#endif

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Configuration for faction relationships.
    /// Defines how this faction relates to other factions by default.
    /// </summary>
    [Serializable]
    public class FactionRelationshipConfig
    {
        [Tooltip("Default relationship toward the player(s). Most factions are enemies to the player.")]
        public RelationshipType defaultPlayerRelationship = RelationshipType.Enemy;
        
        [Tooltip("Default relationship toward other factions. Used when no specific relationship is defined.")]
        public RelationshipType defaultFactionRelationship = RelationshipType.Enemy;
        
        [Tooltip("Relationship toward agents of the same faction but different owners (in multi-player scenarios).")]
        public RelationshipType sameFactionDifferentOwner = RelationshipType.Ally;
        
        [Tooltip("If true, agents with the same owner are always allies regardless of faction.")]
        public bool sameOwnerAlwaysAlly = true;
        
        [Tooltip("Specific relationships with other factions. Overrides the default faction relationship.")]
        public List<SpecificFactionRelationship> specificRelationships = new List<SpecificFactionRelationship>();
    }
    
    /// <summary>
    /// Defines a specific relationship with a named faction.
    /// </summary>
    [Serializable]
    public class SpecificFactionRelationship
    {
        [Tooltip("Name of the target faction.")]
        public string targetFactionName;
        
        [Tooltip("Relationship toward this faction.")]
        public RelationshipType relationship;
        
        public SpecificFactionRelationship()
        {
            targetFactionName = "";
            relationship = RelationshipType.Enemy;
        }
        
        public SpecificFactionRelationship(string factionName, RelationshipType rel)
        {
            targetFactionName = factionName;
            relationship = rel;
        }
    }

    /// <summary>
    /// Built-in faction provider for EnemyMasses.
    /// Provides a simple, high-performance faction system suitable for thousands of agents.
    /// Can be used standalone or as a fallback when third-party systems aren't available.
    /// </summary>
    public class EnemyMassesFactionProvider : IFactionProvider, IFactionRelationshipEditor
    {
        #region IFactionProvider Implementation
        
        public string ProviderId => "enemymasses.builtin";
        public string DisplayName => "Enemy Masses Built-in";
        public int Priority => 0; // Lowest priority - fallback provider
        public bool IsAvailable => true; // Always available
        
        #endregion
        
        #region Private Fields
        
        private EnemyMassesCrowdController _controller;
        private readonly Dictionary<(int, int), RelationshipType> _factionRelationshipCache = new();
        
        // Player faction handling
        private readonly HashSet<int> _playerControlledFactions = new();
        private Transform _playerTarget;
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            _controller = EnemyMassesCrowdController.Instance;
            InvalidateCache();
        }
        
        public void Shutdown()
        {
            _factionRelationshipCache.Clear();
            _playerControlledFactions.Clear();
            _controller = null;
        }
        
        /// <summary>
        /// Sets the player target transform for player relationship queries.
        /// </summary>
        public void SetPlayerTarget(Transform playerTarget)
        {
            _playerTarget = playerTarget;
        }
        
        /// <summary>
        /// Marks a faction as player-controlled.
        /// Player-controlled factions are friendly to the player.
        /// </summary>
        public void SetPlayerControlledFaction(int factionIndex, bool isPlayerControlled)
        {
            if (isPlayerControlled)
                _playerControlledFactions.Add(factionIndex);
            else
                _playerControlledFactions.Remove(factionIndex);
            
            InvalidateCache();
        }
        
        /// <summary>
        /// Invalidates the relationship cache.
        /// Call this when faction relationships change at runtime.
        /// </summary>
        public void InvalidateCache()
        {
            _factionRelationshipCache.Clear();
        }
        
        #endregion
        
        #region Relationship Queries
        
        public RelationshipType GetRelationship(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            if (sourceAgent == null || targetAgent == null)
                return RelationshipType.Neutral;
            
            // Same agent = Self
            if (sourceAgent.index == targetAgent.index)
                return RelationshipType.Self;
            
            // Same owner = always allies (if configured)
            if (HaveSameOwner(sourceAgent, targetAgent))
            {
                var config = GetRelationshipConfig(sourceAgent.enemyCrowd);
                if (config.sameOwnerAlwaysAlly)
                    return RelationshipType.Ally;
            }
            
            // Same faction check: same index OR same factionName
            bool sameFaction = sourceAgent.enemyCrowdIndex == targetAgent.enemyCrowdIndex
                || (!string.IsNullOrEmpty(sourceAgent.enemyCrowd?.factionName)
                    && sourceAgent.enemyCrowd.factionName == targetAgent.enemyCrowd?.factionName);
            
            if (sameFaction)
            {
                if (sourceAgent.ownerPlayerId != targetAgent.ownerPlayerId &&
                    sourceAgent.ownerPlayerId >= 0 && targetAgent.ownerPlayerId >= 0)
                {
                    var config = GetRelationshipConfig(sourceAgent.enemyCrowd);
                    return config.sameFactionDifferentOwner;
                }
                return RelationshipType.Self; // Same faction, same owner or no owner
            }
            
            // Different factions - use faction relationship
            return GetFactionRelationship(sourceAgent.enemyCrowdIndex, targetAgent.enemyCrowdIndex);
        }
        
        public RelationshipType GetRelationship(int sourceAgentIndex, int targetAgentIndex)
        {
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return RelationshipType.Neutral;
            }
            
            var sourceAgent = _controller.GetAgent(sourceAgentIndex);
            var targetAgent = _controller.GetAgent(targetAgentIndex);
            
            return GetRelationship(sourceAgent, targetAgent);
        }
        
        public RelationshipType GetRelationshipToExternal(CrowdAgent sourceAgent, GameObject targetObject)
        {
            if (sourceAgent == null || targetObject == null)
                return RelationshipType.Neutral;
            
            // Check if target is the player
            if (_playerTarget != null && targetObject.transform == _playerTarget)
                return GetPlayerRelationship(sourceAgent);
            
            // Check if target has any faction identifier component
            // Third-party adapters can add their own identification here
            var factionId = targetObject.GetComponent<IExternalFactionIdentifier>();
            if (factionId != null)
            {
                // External object has faction info - query its relationship
                return factionId.GetRelationshipFrom(sourceAgent);
            }
            
            // Default: treat unknown external objects as enemies (standard behavior)
            return GetRelationshipConfig(sourceAgent.enemyCrowd).defaultPlayerRelationship;
        }
        
        public RelationshipType GetFactionRelationship(EnemyCrowd sourceFaction, EnemyCrowd targetFaction)
        {
            if (sourceFaction == null || targetFaction == null)
                return RelationshipType.Neutral;
            
            // Same faction = Self (by reference or by factionName)
            if (sourceFaction == targetFaction)
                return RelationshipType.Self;
            
            if (!string.IsNullOrEmpty(sourceFaction.factionName)
                && sourceFaction.factionName == targetFaction.factionName)
                return RelationshipType.Self;
            
            // Check cached relationships
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return RelationshipType.Enemy;
            }
            
            int sourceIndex = _controller.EnemyCrowds.IndexOf(sourceFaction);
            int targetIndex = _controller.EnemyCrowds.IndexOf(targetFaction);
            
            return GetFactionRelationship(sourceIndex, targetIndex);
        }
        
        public RelationshipType GetFactionRelationship(int sourceFactionIndex, int targetFactionIndex)
        {
            // Same faction = Self
            if (sourceFactionIndex == targetFactionIndex)
                return RelationshipType.Self;
            
            // Invalid indices
            if (sourceFactionIndex < 0 || targetFactionIndex < 0)
                return RelationshipType.Neutral;
            
            // Check cache
            var key = (sourceFactionIndex, targetFactionIndex);
            if (_factionRelationshipCache.TryGetValue(key, out var cached))
                return cached;
            
            // Compute relationship
            var relationship = ComputeFactionRelationship(sourceFactionIndex, targetFactionIndex);
            _factionRelationshipCache[key] = relationship;
            
            return relationship;
        }
        
        public RelationshipType GetPlayerRelationship(CrowdAgent sourceAgent)
        {
            if (sourceAgent == null)
                return RelationshipType.Neutral;
            
            // Check if this agent's faction is player-controlled
            if (_playerControlledFactions.Contains(sourceAgent.enemyCrowdIndex))
                return RelationshipType.Self;
            
            return GetPlayerRelationship(sourceAgent.enemyCrowd);
        }
        
        public RelationshipType GetPlayerRelationship(EnemyCrowd sourceFaction)
        {
            if (sourceFaction == null)
                return RelationshipType.Neutral;
            
            var config = GetRelationshipConfig(sourceFaction);
            return config.defaultPlayerRelationship;
        }
        
        #endregion
        
        #region Combat Helpers
        
        public bool ShouldAttack(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return GetRelationship(sourceAgent, targetAgent).IsHostile();
        }
        
        public bool ShouldSupport(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return GetRelationship(sourceAgent, targetAgent).IsFriendly();
        }
        
        #endregion
        
        #region Owner Management
        
        public int GetOwnerPlayerId(CrowdAgent agent)
        {
            return agent?.ownerPlayerId ?? -1;
        }
        
        public void SetOwnerPlayerId(CrowdAgent agent, int playerId)
        {
            if (agent != null)
            {
                agent.ownerPlayerId = playerId;
            }
        }
        
        public bool HaveSameOwner(CrowdAgent agentA, CrowdAgent agentB)
        {
            if (agentA == null || agentB == null)
                return false;
            
            int ownerA = agentA.ownerPlayerId;
            int ownerB = agentB.ownerPlayerId;
            
            // Both must have valid owners (>= 0) and be the same
            return ownerA >= 0 && ownerA == ownerB;
        }
        
        public IEnumerable<CrowdAgent> GetAgentsByOwner(int playerId)
        {
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) yield break;
            }
            
            var agents = _controller.GetAgents();
            foreach (var agent in agents)
            {
                if (agent != null && agent.ownerPlayerId == playerId)
                    yield return agent;
            }
        }
        
        public IEnumerable<CrowdAgent> GetAgentsByRelationship(CrowdAgent sourceAgent, RelationshipFlags relationshipFlags)
        {
            if (sourceAgent == null || _controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) yield break;
            }
            
            var agents = _controller.GetAgents();
            foreach (var agent in agents)
            {
                if (agent != null && agent != sourceAgent)
                {
                    var relationship = GetRelationship(sourceAgent, agent);
                    if (relationship.MatchesFlags(relationshipFlags))
                        yield return agent;
                }
            }
        }
        
        #endregion
        
        #region Private Helpers
        
        private RelationshipType ComputeFactionRelationship(int sourceFactionIndex, int targetFactionIndex)
        {
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return RelationshipType.Enemy;
            }
            
            var crowds = _controller.EnemyCrowds;
            
            if (sourceFactionIndex < 0 || sourceFactionIndex >= crowds.Count ||
                targetFactionIndex < 0 || targetFactionIndex >= crowds.Count)
                return RelationshipType.Enemy;
            
            var sourceFaction = crowds[sourceFactionIndex];
            var targetFaction = crowds[targetFactionIndex];
            
            if (sourceFaction == null || targetFaction == null)
                return RelationshipType.Enemy;
            
            // Same faction by name = Self (different SO assets sharing the same factionName)
            if (!string.IsNullOrEmpty(sourceFaction.factionName)
                && sourceFaction.factionName == targetFaction.factionName)
                return RelationshipType.Self;
            
            var config = GetRelationshipConfig(sourceFaction);
            
            // Check specific relationships first
            foreach (var specific in config.specificRelationships)
            {
                if (specific.targetFactionName == targetFaction.factionName)
                    return specific.relationship;
            }
            
            // Return default faction relationship
            return config.defaultFactionRelationship;
        }
        
        private FactionRelationshipConfig GetRelationshipConfig(EnemyCrowd faction)
        {
            // If faction has custom config, use it
            // Otherwise return default enemy config
            return faction?.factionRelationshipConfig ?? new FactionRelationshipConfig();
        }
        
        #endregion
        
        #region Runtime Relationship Modification
        
        /// <summary>
        /// Sets the relationship between two factions at runtime.
        /// This modifies the source faction's relationship toward the target faction.
        /// </summary>
        /// <param name="sourceFactionName">Name of the source faction</param>
        /// <param name="targetFactionName">Name of the target faction</param>
        /// <param name="relationship">The relationship to set</param>
        /// <param name="bidirectional">If true, also sets target→source relationship</param>
        /// <returns>True if successful</returns>
        public bool SetFactionRelationship(string sourceFactionName, string targetFactionName, 
            RelationshipType relationship, bool bidirectional = false)
        {
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return false;
            }
            
            var sourceFaction = GetFactionByName(sourceFactionName);
            var targetFaction = GetFactionByName(targetFactionName);
            
            if (sourceFaction == null || targetFaction == null)
            {
                Debug.LogWarning($"[FactionProvider] Cannot set relationship: faction not found " +
                                 $"(source: {sourceFactionName}, target: {targetFactionName})");
                return false;
            }
            
            SetFactionRelationshipInternal(sourceFaction, targetFactionName, relationship);
            
            if (bidirectional)
            {
                SetFactionRelationshipInternal(targetFaction, sourceFactionName, relationship);
            }
            
            InvalidateCache();
            return true;
        }
        
        /// <summary>
        /// Sets the relationship between two factions at runtime by index.
        /// </summary>
        public bool SetFactionRelationship(int sourceFactionIndex, int targetFactionIndex, 
            RelationshipType relationship, bool bidirectional = false)
        {
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return false;
            }
            
            var crowds = _controller.EnemyCrowds;
            if (sourceFactionIndex < 0 || sourceFactionIndex >= crowds.Count ||
                targetFactionIndex < 0 || targetFactionIndex >= crowds.Count)
                return false;
            
            var sourceFaction = crowds[sourceFactionIndex];
            var targetFaction = crowds[targetFactionIndex];
            
            if (sourceFaction == null || targetFaction == null)
                return false;
            
            SetFactionRelationshipInternal(sourceFaction, targetFaction.factionName, relationship);
            
            if (bidirectional)
            {
                SetFactionRelationshipInternal(targetFaction, sourceFaction.factionName, relationship);
            }
            
            InvalidateCache();
            return true;
        }
        
        /// <summary>
        /// Sets the default relationship a faction has toward all other factions.
        /// </summary>
        public bool SetFactionDefaultRelationship(string factionName, RelationshipType relationship)
        {
            var faction = GetFactionByName(factionName);
            if (faction == null) return false;
            
            faction.factionRelationshipConfig.defaultFactionRelationship = relationship;
            InvalidateCache();
            return true;
        }
        
        /// <summary>
        /// Sets the relationship a faction has toward the player.
        /// </summary>
        public bool SetFactionPlayerRelationship(string factionName, RelationshipType relationship)
        {
            var faction = GetFactionByName(factionName);
            if (faction == null) return false;
            
            faction.factionRelationshipConfig.defaultPlayerRelationship = relationship;
            InvalidateCache();
            return true;
        }
        
        /// <summary>
        /// Sets how a faction treats same-faction units with different owners.
        /// </summary>
        public bool SetSameFactionDifferentOwnerRelationship(string factionName, RelationshipType relationship)
        {
            var faction = GetFactionByName(factionName);
            if (faction == null) return false;
            
            faction.factionRelationshipConfig.sameFactionDifferentOwner = relationship;
            InvalidateCache();
            return true;
        }
        
        /// <summary>
        /// Removes a specific faction relationship (reverts to default).
        /// </summary>
        public bool RemoveFactionRelationship(string sourceFactionName, string targetFactionName, bool bidirectional = false)
        {
            var sourceFaction = GetFactionByName(sourceFactionName);
            var targetFaction = GetFactionByName(targetFactionName);
            
            if (sourceFaction == null || targetFaction == null)
                return false;
            
            RemoveFactionRelationshipInternal(sourceFaction, targetFactionName);
            
            if (bidirectional)
            {
                RemoveFactionRelationshipInternal(targetFaction, sourceFactionName);
            }
            
            InvalidateCache();
            return true;
        }
        
        /// <summary>
        /// Gets a faction EnemyCrowd by name.
        /// </summary>
        public EnemyCrowd GetFactionByName(string factionName)
        {
            if (string.IsNullOrEmpty(factionName) || _controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return null;
            }
            
            foreach (var crowd in _controller.EnemyCrowds)
            {
                if (crowd != null && crowd.factionName == factionName)
                    return crowd;
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets a faction index by name.
        /// </summary>
        public int GetFactionIndexByName(string factionName)
        {
            if (string.IsNullOrEmpty(factionName) || _controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return -1;
            }
            
            var crowds = _controller.EnemyCrowds;
            for (int i = 0; i < crowds.Count; i++)
            {
                if (crowds[i] != null && crowds[i].factionName == factionName)
                    return i;
            }
            
            return -1;
        }
        
        private void SetFactionRelationshipInternal(EnemyCrowd faction, string targetFactionName, RelationshipType relationship)
        {
            var config = faction.factionRelationshipConfig;
            
            // Check if relationship already exists
            for (int i = 0; i < config.specificRelationships.Count; i++)
            {
                if (config.specificRelationships[i].targetFactionName == targetFactionName)
                {
                    config.specificRelationships[i].relationship = relationship;
                    return;
                }
            }
            
            // Add new relationship
            config.specificRelationships.Add(new SpecificFactionRelationship(targetFactionName, relationship));
        }
        
        private void RemoveFactionRelationshipInternal(EnemyCrowd faction, string targetFactionName)
        {
            var config = faction.factionRelationshipConfig;
            
            for (int i = config.specificRelationships.Count - 1; i >= 0; i--)
            {
                if (config.specificRelationships[i].targetFactionName == targetFactionName)
                {
                    config.specificRelationships.RemoveAt(i);
                    return;
                }
            }
        }
        
        #endregion
        
        #region Serialization Support
        
        /// <summary>
        /// Gets all faction relationship data for serialization.
        /// </summary>
        public FactionSystemSnapshot CreateSnapshot()
        {
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return null;
            }
            
            var snapshot = new FactionSystemSnapshot
            {
                FactionSnapshots = new List<FactionRelationshipSnapshot>(),
                PlayerControlledFactions = new List<int>(_playerControlledFactions)
            };
            
            foreach (var crowd in _controller.EnemyCrowds)
            {
                if (crowd == null) continue;
                
                var factionSnapshot = new FactionRelationshipSnapshot
                {
                    FactionName = crowd.factionName,
                    DefaultPlayerRelationship = crowd.factionRelationshipConfig.defaultPlayerRelationship,
                    DefaultFactionRelationship = crowd.factionRelationshipConfig.defaultFactionRelationship,
                    SameFactionDifferentOwner = crowd.factionRelationshipConfig.sameFactionDifferentOwner,
                    SameOwnerAlwaysAlly = crowd.factionRelationshipConfig.sameOwnerAlwaysAlly,
                    SpecificRelationships = new List<SpecificFactionRelationshipSnapshot>()
                };
                
                foreach (var specific in crowd.factionRelationshipConfig.specificRelationships)
                {
                    factionSnapshot.SpecificRelationships.Add(new SpecificFactionRelationshipSnapshot
                    {
                        TargetFactionName = specific.targetFactionName,
                        Relationship = specific.relationship
                    });
                }
                
                snapshot.FactionSnapshots.Add(factionSnapshot);
            }
            
            return snapshot;
        }
        
        /// <summary>
        /// Restores faction relationships from a snapshot.
        /// </summary>
        public void RestoreFromSnapshot(FactionSystemSnapshot snapshot)
        {
            if (snapshot == null) return;
            
            if (_controller == null)
            {
                _controller = EnemyMassesCrowdController.Instance;
                if (_controller == null) return;
            }
            
            // Restore player-controlled factions
            _playerControlledFactions.Clear();
            if (snapshot.PlayerControlledFactions != null)
            {
                foreach (int factionIndex in snapshot.PlayerControlledFactions)
                {
                    _playerControlledFactions.Add(factionIndex);
                }
            }
            
            // Restore faction relationships
            foreach (var factionSnapshot in snapshot.FactionSnapshots)
            {
                var crowd = GetFactionByName(factionSnapshot.FactionName);
                if (crowd == null) continue;
                
                crowd.factionRelationshipConfig.defaultPlayerRelationship = factionSnapshot.DefaultPlayerRelationship;
                crowd.factionRelationshipConfig.defaultFactionRelationship = factionSnapshot.DefaultFactionRelationship;
                crowd.factionRelationshipConfig.sameFactionDifferentOwner = factionSnapshot.SameFactionDifferentOwner;
                crowd.factionRelationshipConfig.sameOwnerAlwaysAlly = factionSnapshot.SameOwnerAlwaysAlly;
                
                crowd.factionRelationshipConfig.specificRelationships.Clear();
                foreach (var specific in factionSnapshot.SpecificRelationships)
                {
                    crowd.factionRelationshipConfig.specificRelationships.Add(
                        new SpecificFactionRelationship(specific.TargetFactionName, specific.Relationship));
                }
            }
            
            InvalidateCache();
            Debug.Log("[FactionProvider] Restored faction relationships from snapshot.");
        }
        
        #endregion
    }
    
    #region Snapshot Classes
    
    /// <summary>
    /// Snapshot of the entire faction system for persistence.
    /// </summary>
    [Serializable]
#if MEMORYPACK
    [MemoryPackable]
    public partial class FactionSystemSnapshot
#else
    public class FactionSystemSnapshot
#endif
    {
        public List<FactionRelationshipSnapshot> FactionSnapshots;
        public List<int> PlayerControlledFactions;
    }
    
    /// <summary>
    /// Snapshot of a single faction's relationships.
    /// </summary>
    [Serializable]
#if MEMORYPACK
    [MemoryPackable]
    public partial class FactionRelationshipSnapshot
#else
    public class FactionRelationshipSnapshot
#endif
    {
        public string FactionName;
        public RelationshipType DefaultPlayerRelationship;
        public RelationshipType DefaultFactionRelationship;
        public RelationshipType SameFactionDifferentOwner;
        public bool SameOwnerAlwaysAlly;
        public List<SpecificFactionRelationshipSnapshot> SpecificRelationships;
    }
    
    /// <summary>
    /// Snapshot of a specific faction relationship.
    /// </summary>
    [Serializable]
#if MEMORYPACK
    [MemoryPackable]
    public partial class SpecificFactionRelationshipSnapshot
#else
    public class SpecificFactionRelationshipSnapshot
#endif
    {
        public string TargetFactionName;
        public RelationshipType Relationship;
    }
    
    #endregion
    
    /// <summary>
    /// Interface for external objects that have faction identity.
    /// Implement this on player characters, structures, or other GameObjects
    /// that agents should recognize for faction relationships.
    /// </summary>
    public interface IExternalFactionIdentifier
    {
        /// <summary>
        /// Gets the faction name of this external object.
        /// </summary>
        string FactionName { get; }
        
        /// <summary>
        /// Gets the owner/player ID of this external object.
        /// Returns -1 if no owner.
        /// </summary>
        int OwnerPlayerId { get; }
        
        /// <summary>
        /// Determines the relationship from a CrowdAgent's perspective toward this object.
        /// </summary>
        /// <param name="sourceAgent">The agent querying the relationship</param>
        /// <returns>The relationship type</returns>
        RelationshipType GetRelationshipFrom(CrowdAgent sourceAgent);
    }
}
