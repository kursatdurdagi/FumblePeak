using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Adapter interface for NinjutsuGames Factions (Game Creator 2) integration.
    /// 
    /// To integrate NinjutsuGames Factions with EnemyMasses:
    /// 1. Create a class that implements IGC2FactionAdapter
    /// 2. Call FactionSystemManager.Instance.RegisterProvider(new YourGC2FactionProvider())
    /// 
    /// This interface defines the bridge between GC2 Factions' stance-based relationships
    /// and EnemyMasses' simpler friend/foe/ally/neutral system.
    /// 
    /// IMPORTANT: This interface is defined WITHOUT any dependencies on GC2/NinjutsuGames.
    /// The actual implementation should be in a separate assembly that references NinjutsuGames.Runtime.Factions.
    /// </summary>
    public interface IGC2FactionAdapter : IFactionProvider
    {
        /// <summary>
        /// Converts a GC2 Factions stance key to a RelationshipType.
        /// 
        /// Standard GC2 stance keys: "enemy", "neutral", "ally", etc.
        /// These are configured in FactionsRepository.
        /// </summary>
        /// <param name="stanceKey">The stance key from GC2 Factions</param>
        /// <returns>The corresponding relationship type</returns>
        RelationshipType ConvertStanceToRelationship(string stanceKey);
        
        /// <summary>
        /// Gets the GC2 Faction asset reference for an EnemyCrowd.
        /// Returns null if the faction isn't mapped.
        /// </summary>
        /// <param name="crowd">The EnemyCrowd to look up</param>
        /// <returns>The GC2 Faction reference as UnityEngine.Object, or null</returns>
        /// <remarks>Returns Object to avoid direct reference to NinjutsuGames.Runtime.Factions.Faction</remarks>
        Object GetGC2Faction(EnemyCrowd crowd);
        
        /// <summary>
        /// Gets the GC2 Faction unique ID string for an EnemyCrowd.
        /// Returns empty string if the faction isn't mapped.
        /// </summary>
        /// <param name="crowd">The EnemyCrowd to look up</param>
        /// <returns>The GC2 Faction unique ID string</returns>
        string GetGC2FactionId(EnemyCrowd crowd);
        
        /// <summary>
        /// Maps an EnemyCrowd faction to a GC2 Faction.
        /// </summary>
        /// <param name="crowd">The EnemyCrowd to map</param>
        /// <param name="gc2Faction">The GC2 Faction as UnityEngine.Object</param>
        void MapFactionToGC2(EnemyCrowd crowd, Object gc2Faction);
        
        /// <summary>
        /// Registers an agent as a Member of a GC2 Faction.
        /// </summary>
        /// <param name="agent">The crowd agent to register</param>
        /// <param name="gc2FactionId">The GC2 Faction unique ID</param>
        void RegisterAgentWithGC2(CrowdAgent agent, string gc2FactionId);
        
        /// <summary>
        /// Unregisters an agent from the GC2 Faction system.
        /// </summary>
        /// <param name="agent">The crowd agent to unregister</param>
        void UnregisterAgentFromGC2(CrowdAgent agent);
        
        /// <summary>
        /// Modifies reputation between factions at runtime.
        /// Useful for dynamic relationship changes based on gameplay events.
        /// </summary>
        /// <param name="fromCrowd">The source EnemyCrowd faction</param>
        /// <param name="toCrowd">The target EnemyCrowd faction</param>
        /// <param name="reputationChange">Amount to change reputation (positive = better, negative = worse)</param>
        void ModifyReputation(EnemyCrowd fromCrowd, EnemyCrowd toCrowd, int reputationChange);
        
        /// <summary>
        /// Gets the current reputation value between two factions.
        /// </summary>
        /// <param name="fromCrowd">The source EnemyCrowd faction</param>
        /// <param name="toCrowd">The target EnemyCrowd faction</param>
        /// <returns>The current reputation value</returns>
        int GetReputation(EnemyCrowd fromCrowd, EnemyCrowd toCrowd);
        
        /// <summary>
        /// Gets the stance key between two factions based on current reputation.
        /// </summary>
        /// <param name="fromCrowd">The source EnemyCrowd faction</param>
        /// <param name="toCrowd">The target EnemyCrowd faction</param>
        /// <returns>The stance key (e.g., "enemy", "neutral", "ally")</returns>
        string GetStance(EnemyCrowd fromCrowd, EnemyCrowd toCrowd);
    }
    
    /// <summary>
    /// Configuration for mapping EnemyCrowd factions to GC2 Factions.
    /// </summary>
    [System.Serializable]
    public class GC2FactionMapping
    {
        [Tooltip("Reference to the EnemyCrowd faction")]
        public EnemyCrowd enemyCrowd;
        
        [Tooltip("GC2 Faction unique ID (from Faction asset)")]
        public string gc2FactionId;
        
        [Tooltip("GC2 Faction asset reference (optional, for editor convenience)")]
        public Object gc2FactionAsset;
    }
    
    /// <summary>
    /// Configuration for stance-to-relationship mapping.
    /// </summary>
    [System.Serializable]
    public class StanceMapping
    {
        [Tooltip("The stance key from GC2 Factions")]
        public string stanceKey;
        
        [Tooltip("The corresponding EnemyMasses relationship type")]
        public RelationshipType relationship;
        
        public StanceMapping()
        {
            stanceKey = "";
            relationship = RelationshipType.Neutral;
        }
        
        public StanceMapping(string key, RelationshipType rel)
        {
            stanceKey = key;
            relationship = rel;
        }
    }
    
    /// <summary>
    /// Configuration for GC2 Factions integration settings.
    /// </summary>
    [System.Serializable]
    public class GC2FactionIntegrationSettings
    {
        [Header("Stance Mappings")]
        [Tooltip("How GC2 stance keys map to EnemyMasses relationships.\n" +
                 "Add entries for all stance keys used in your FactionsRepository.")]
        public List<StanceMapping> stanceMappings = new List<StanceMapping>()
        {
            new StanceMapping("enemy", RelationshipType.Enemy),
            new StanceMapping("hostile", RelationshipType.Enemy),
            new StanceMapping("neutral", RelationshipType.Neutral),
            new StanceMapping("friendly", RelationshipType.Ally),
            new StanceMapping("ally", RelationshipType.Ally)
        };
        
        [Header("Faction Mappings")]
        [Tooltip("Mappings between EnemyCrowd factions and GC2 Factions")]
        public List<GC2FactionMapping> factionMappings = new List<GC2FactionMapping>();
        
        [Header("Runtime Behavior")]
        [Tooltip("Modify reputation when agents attack each other")]
        public bool modifyReputationOnAttack = false;
        
        [Tooltip("Reputation change when an agent attacks another faction")]
        public int attackReputationChange = -5;
        
        [Tooltip("Modify reputation when agents kill each other")]
        public bool modifyReputationOnKill = false;
        
        [Tooltip("Reputation change when an agent kills another faction member")]
        public int killReputationChange = -20;
        
        [Header("Performance")]
        [Tooltip("Cache relationship lookups for this many seconds")]
        [Range(0f, 10f)]
        public float relationshipCacheDuration = 1f;
        
        /// <summary>
        /// Gets the relationship type for a stance key.
        /// Returns Neutral if the stance key isn't mapped.
        /// </summary>
        public RelationshipType GetRelationshipForStance(string stanceKey)
        {
            if (string.IsNullOrEmpty(stanceKey))
                return RelationshipType.Neutral;
            
            var stanceLower = stanceKey.ToLowerInvariant();
            foreach (var mapping in stanceMappings)
            {
                if (mapping.stanceKey.ToLowerInvariant() == stanceLower)
                    return mapping.relationship;
            }
            
            return RelationshipType.Neutral;
        }
    }
}
