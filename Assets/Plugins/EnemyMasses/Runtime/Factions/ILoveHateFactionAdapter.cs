using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Adapter interface for PixelCrushers Love/Hate integration.
    /// 
    /// To integrate Love/Hate with EnemyMasses:
    /// 1. Create a class that implements ILoveHateFactionAdapter
    /// 2. Add the [RequireComponent(typeof(FactionMember))] attribute if needed
    /// 3. Call FactionSystemManager.Instance.RegisterProvider(new YourLoveHateProvider())
    /// 
    /// This interface defines the bridge between Love/Hate's affinity-based relationships
    /// and EnemyMasses' simpler friend/foe/ally/neutral system.
    /// 
    /// IMPORTANT: This interface is defined WITHOUT any dependencies on Love/Hate.
    /// The actual implementation should be in a separate assembly that references Love/Hate.
    /// </summary>
    public interface ILoveHateFactionAdapter : IFactionProvider
    {
        /// <summary>
        /// Converts a Love/Hate affinity value to a RelationshipType.
        /// Love/Hate uses -100 to +100 affinity values.
        /// 
        /// Typical thresholds:
        /// - Below -50: Enemy
        /// - -50 to +25: Neutral  
        /// - Above +25: Ally
        /// 
        /// These thresholds should be configurable.
        /// </summary>
        /// <param name="affinity">The affinity value from Love/Hate (-100 to +100)</param>
        /// <returns>The corresponding relationship type</returns>
        RelationshipType ConvertAffinityToRelationship(float affinity);
        
        /// <summary>
        /// Gets the affinity threshold above which a relationship becomes Ally.
        /// </summary>
        float AllyThreshold { get; set; }
        
        /// <summary>
        /// Gets the affinity threshold below which a relationship becomes Enemy.
        /// </summary>
        float EnemyThreshold { get; set; }
        
        /// <summary>
        /// Registers an agent with the Love/Hate system.
        /// This creates a FactionMember entry for the agent if needed.
        /// </summary>
        /// <param name="agent">The crowd agent to register</param>
        /// <param name="factionId">The Love/Hate faction ID</param>
        void RegisterAgentWithLoveHate(CrowdAgent agent, int factionId);
        
        /// <summary>
        /// Unregisters an agent from the Love/Hate system.
        /// Call this when an agent dies or is despawned.
        /// </summary>
        /// <param name="agent">The crowd agent to unregister</param>
        void UnregisterAgentFromLoveHate(CrowdAgent agent);
        
        /// <summary>
        /// Reports a deed to the Love/Hate system.
        /// Use this when an agent performs an action that should affect relationships.
        /// </summary>
        /// <param name="actorAgent">The agent that performed the deed</param>
        /// <param name="targetAgent">The agent affected by the deed (can be null)</param>
        /// <param name="deedTag">The deed tag identifier</param>
        /// <param name="impact">The impact value (-100 to +100)</param>
        void ReportDeed(CrowdAgent actorAgent, CrowdAgent targetAgent, string deedTag, float impact);
        
        /// <summary>
        /// Gets the Love/Hate faction ID for an EnemyCrowd.
        /// Returns -1 if the faction isn't mapped.
        /// </summary>
        /// <param name="crowd">The EnemyCrowd to look up</param>
        /// <returns>The Love/Hate faction ID, or -1 if not mapped</returns>
        int GetLoveHateFactionId(EnemyCrowd crowd);
        
        /// <summary>
        /// Maps an EnemyCrowd faction to a Love/Hate faction ID.
        /// </summary>
        /// <param name="crowd">The EnemyCrowd to map</param>
        /// <param name="loveHateFactionId">The Love/Hate faction ID</param>
        void MapFactionToLoveHate(EnemyCrowd crowd, int loveHateFactionId);
    }
    
    /// <summary>
    /// Configuration for mapping EnemyCrowd factions to Love/Hate factions.
    /// Store this in a ScriptableObject or on a MonoBehaviour.
    /// </summary>
    [System.Serializable]
    public class LoveHateFactionMapping
    {
        [Tooltip("Reference to the EnemyCrowd faction")]
        public EnemyCrowd enemyCrowd;
        
        [Tooltip("Love/Hate faction ID (from FactionDatabase)")]
        public int loveHateFactionId = -1;
        
        [Tooltip("Love/Hate faction name (for display/debugging)")]
        public string loveHateFactionName;
    }
    
    /// <summary>
    /// Configuration for Love/Hate integration settings.
    /// </summary>
    [System.Serializable]
    public class LoveHateIntegrationSettings
    {
        [Header("Affinity Thresholds")]
        [Tooltip("Affinity value above which relationship becomes Ally")]
        [Range(-100f, 100f)]
        public float allyThreshold = 25f;
        
        [Tooltip("Affinity value below which relationship becomes Enemy")]
        [Range(-100f, 100f)]
        public float enemyThreshold = -50f;
        
        [Header("Faction Mappings")]
        [Tooltip("Mappings between EnemyCrowd factions and Love/Hate factions")]
        public List<LoveHateFactionMapping> factionMappings = new List<LoveHateFactionMapping>();
        
        [Header("Deed Reporting")]
        [Tooltip("Report attack deeds to Love/Hate system")]
        public bool reportAttackDeeds = true;
        
        [Tooltip("Report death deeds to Love/Hate system")]
        public bool reportDeathDeeds = true;
        
        [Tooltip("Deed tag for melee attacks")]
        public string meleeAttackDeedTag = "attack";
        
        [Tooltip("Deed tag for ranged attacks")]
        public string rangedAttackDeedTag = "attack";
        
        [Tooltip("Deed tag for kills")]
        public string killDeedTag = "kill";
        
        [Header("Performance")]
        [Tooltip("Cache affinity lookups for this many seconds")]
        [Range(0f, 10f)]
        public float affinityCacheDuration = 1f;
    }
}
