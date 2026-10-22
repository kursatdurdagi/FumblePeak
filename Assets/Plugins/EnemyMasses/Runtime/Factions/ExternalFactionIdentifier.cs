using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Component to attach to external objects (players, structures, etc.) to identify their faction.
    /// This allows the faction system to correctly determine relationships between
    /// CrowdAgents and external GameObjects.
    /// 
    /// Example usage:
    /// - Attach to your player character
    /// - Set the FactionName to match your player faction (or leave empty to use default player relationship)
    /// - Set OwnerPlayerId in multiplayer to identify which player owns this object
    /// </summary>
    [AddComponentMenu("Enemy Masses/External Faction Identifier")]
    public class ExternalFactionIdentifier : MonoBehaviour, IExternalFactionIdentifier
    {
        [Header("Faction Identity")]
        [Tooltip("The faction name this object belongs to.\n" +
                 "Leave empty to use default player relationship logic.\n" +
                 "Set to a specific faction name to match an EnemyCrowd faction.")]
        [SerializeField] private string factionName = "";
        
        [Tooltip("The player ID that owns this object.\n" +
                 "-1 = No owner (use for NPCs or environment objects)\n" +
                 "0+ = Player ID (use for player characters in multiplayer)")]
        [SerializeField] private int ownerPlayerId = 0;
        
        [Header("Relationship Override")]
        [Tooltip("Override all faction logic and always return this relationship to crowd agents.\n" +
                 "Useful for player characters that should always be seen as enemies by all agents.")]
        [SerializeField] private bool overrideRelationship = false;
        
        [Tooltip("The relationship all agents will have toward this object (when override is enabled).")]
        [SerializeField] private RelationshipType overrideRelationshipType = RelationshipType.Enemy;
        
        #region IExternalFactionIdentifier Implementation
        
        public string FactionName => factionName;
        public int OwnerPlayerId => ownerPlayerId;
        
        public RelationshipType GetRelationshipFrom(CrowdAgent sourceAgent)
        {
            // Use override if enabled
            if (overrideRelationship)
            {
                return overrideRelationshipType;
            }
            
            if (sourceAgent == null)
                return RelationshipType.Neutral;
            
            // If this object has a faction name, find the matching EnemyCrowd
            if (!string.IsNullOrEmpty(factionName))
            {
                var controller = EnemyMassesCrowdController.Instance;
                if (controller != null)
                {
                    var crowds = controller.EnemyCrowds;
                    for (int i = 0; i < crowds.Count; i++)
                    {
                        if (crowds[i] != null && crowds[i].factionName == factionName)
                        {
                            // Found the faction - use faction relationship logic
                            return FactionSystemManager.Instance.GetFactionRelationship(
                                sourceAgent.enemyCrowdIndex, i);
                        }
                    }
                }
            }
            
            // Check owner relationship
            if (ownerPlayerId >= 0 && sourceAgent.ownerPlayerId >= 0)
            {
                if (ownerPlayerId == sourceAgent.ownerPlayerId)
                {
                    // Same owner = friendly
                    return RelationshipType.Ally;
                }
            }
            
            // Default to player relationship for this agent
            return FactionSystemManager.Instance.GetPlayerRelationship(sourceAgent);
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Sets the faction name at runtime.
        /// </summary>
        public void SetFactionName(string newFactionName)
        {
            factionName = newFactionName;
        }
        
        /// <summary>
        /// Sets the owner player ID at runtime.
        /// </summary>
        public void SetOwnerPlayerId(int newOwnerId)
        {
            ownerPlayerId = newOwnerId;
        }
        
        /// <summary>
        /// Enables or disables relationship override.
        /// </summary>
        public void SetRelationshipOverride(bool enabled, RelationshipType relationship = RelationshipType.Enemy)
        {
            overrideRelationship = enabled;
            overrideRelationshipType = relationship;
        }
        
        #endregion
    }
}
