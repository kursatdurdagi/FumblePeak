namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Defines the attack range behavior type for combat agents.
    /// </summary>
    public enum CombatRangeType
    {
        /// <summary>
        /// Close-range combat only. Agents must be very close to attack (swords, fists, etc.)
        /// </summary>
        Melee,
        
        /// <summary>
        /// Long-range combat only. Agents maintain distance and attack from afar (bows, guns, magic)
        /// </summary>
        Ranged,
        
        /// <summary>
        /// Can attack at both melee and ranged distances. Switches behavior based on distance.
        /// </summary>
        Hybrid
    }

    /// <summary>
    /// Optional runtime override for Hybrid combat range switching.
    /// </summary>
    public enum CombatRangeOverrideMode
    {
        /// <summary>
        /// Use the EnemyCrowd's default combat range behavior.
        /// </summary>
        Default,

        /// <summary>
        /// Force Hybrid agents to stay in melee mode.
        /// </summary>
        ForceMelee,

        /// <summary>
        /// Force Hybrid agents to stay in ranged mode.
        /// </summary>
        ForceRanged
    }
}
