using System;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Defines the relationship between two factions or agents.
    /// This is the core output that EnemyMasses needs to determine combat behavior.
    /// </summary>
    public enum RelationshipType
    {
        /// <summary>
        /// Enemy - Will be targeted for attacks
        /// </summary>
        Enemy = 0,
        
        /// <summary>
        /// Neutral - Will not attack unless provoked (or configured to)
        /// </summary>
        Neutral = 1,
        
        /// <summary>
        /// Ally - Will be supported/healed, never attacked
        /// </summary>
        Ally = 2,
        
        /// <summary>
        /// Self - Same faction/owner, always friendly
        /// </summary>
        Self = 3
    }
    
    /// <summary>
    /// Flags version for queries that need to match multiple relationship types.
    /// </summary>
    [Flags]
    public enum RelationshipFlags
    {
        None = 0,
        Enemy = 1 << 0,   // 1
        Neutral = 1 << 1, // 2
        Ally = 1 << 2,    // 4
        Self = 1 << 3,    // 8
        
        /// <summary>All hostile relationships (enemies only)</summary>
        Hostile = Enemy,
        
        /// <summary>All friendly relationships (allies and self)</summary>
        Friendly = Ally | Self,
        
        /// <summary>All relationships</summary>
        All = Enemy | Neutral | Ally | Self
    }
    
    /// <summary>
    /// Extension methods for RelationshipType
    /// </summary>
    public static class RelationshipTypeExtensions
    {
        /// <summary>
        /// Returns true if this relationship type is hostile (enemy)
        /// </summary>
        public static bool IsHostile(this RelationshipType relationship)
        {
            return relationship == RelationshipType.Enemy;
        }
        
        /// <summary>
        /// Returns true if this relationship type is friendly (ally or self)
        /// </summary>
        public static bool IsFriendly(this RelationshipType relationship)
        {
            return relationship == RelationshipType.Ally || relationship == RelationshipType.Self;
        }
        
        /// <summary>
        /// Returns true if this relationship type is neutral
        /// </summary>
        public static bool IsNeutral(this RelationshipType relationship)
        {
            return relationship == RelationshipType.Neutral;
        }
        
        /// <summary>
        /// Converts RelationshipType to RelationshipFlags
        /// </summary>
        public static RelationshipFlags ToFlags(this RelationshipType relationship)
        {
            return relationship switch
            {
                RelationshipType.Enemy => RelationshipFlags.Enemy,
                RelationshipType.Neutral => RelationshipFlags.Neutral,
                RelationshipType.Ally => RelationshipFlags.Ally,
                RelationshipType.Self => RelationshipFlags.Self,
                _ => RelationshipFlags.None
            };
        }
        
        /// <summary>
        /// Returns true if the relationship type matches any of the flags
        /// </summary>
        public static bool MatchesFlags(this RelationshipType relationship, RelationshipFlags flags)
        {
            return (relationship.ToFlags() & flags) != RelationshipFlags.None;
        }
    }
}
