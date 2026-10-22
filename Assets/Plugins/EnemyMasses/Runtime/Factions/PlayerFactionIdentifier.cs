using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Specialized identifier for player characters.
    /// Provides additional convenience methods for common player scenarios.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Player Faction Identifier")]
    public class PlayerFactionIdentifier : ExternalFactionIdentifier
    {
        [Header("Player Settings")]
        [Tooltip("If true, this player is always considered an enemy by all crowd agents.\n" +
                 "Use for players in survival/action games.")]
        [SerializeField] private bool alwaysEnemyToAgents = true;
        
        private void Awake()
        {
            if (alwaysEnemyToAgents)
            {
                SetRelationshipOverride(true, RelationshipType.Enemy);
            }
        }
        
        /// <summary>
        /// Makes this player friendly to all agents (e.g., for cutscenes or safe zones).
        /// </summary>
        public void SetFriendlyToAll()
        {
            SetRelationshipOverride(true, RelationshipType.Ally);
        }
        
        /// <summary>
        /// Makes this player an enemy to all agents (normal gameplay).
        /// </summary>
        public void SetEnemyToAll()
        {
            SetRelationshipOverride(true, RelationshipType.Enemy);
        }
        
        /// <summary>
        /// Makes this player neutral to all agents (they won't attack unless provoked).
        /// </summary>
        public void SetNeutralToAll()
        {
            SetRelationshipOverride(true, RelationshipType.Neutral);
        }
        
        /// <summary>
        /// Disables override and uses faction-based relationships.
        /// </summary>
        public void UseFactionRelationships()
        {
            SetRelationshipOverride(false);
        }
    }
}
