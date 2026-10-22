namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Defines how enemies position themselves and attack the target.
    /// Offers different gameplay styles for designers to choose from.
    /// </summary>
    public enum CombatPositioningMode
    {
        /// <summary>
        /// Vampire Survivors style: All enemies rush and attack simultaneously.
        /// No positioning restrictions, maximum aggression.
        /// </summary>
        Swarm,
        
        /// <summary>
        /// Batman/Spider-Man style: Enemies form a circle around target and queue attacks.
        /// Only N enemies attack at once, others wait in formation.
        /// </summary>
        QueuedCircle,
        
        /// <summary>
        /// Classic approach: Enemies approach but stop at range without formation.
        /// Simple stop-when-close behavior, minimal coordination.
        /// </summary>
        SimpleApproach,
        
        /// <summary>
        /// Advanced formation: Enemies take specific slots in a circle formation.
        /// Prevents clumping, maintains spacing, queues attacks with slot management.
        /// </summary>
        FormationSlots,
        
        /// <summary>
        /// Diablo 4 style: Enemies attack with randomized timing and positioning.
        /// Creates natural-feeling variety by staggering attacks, randomizing angles,
        /// and adding individual behavior variation. Prevents synchronized movements.
        /// </summary>
        Staggered,
        
        /// <summary>
        /// Organized wave-based swarm: Divides agents into groups that attack in waves.
        /// Each wave attacks in full swarm style, then waits while next wave takes over.
        /// Waiting agents position at respectful distance but react to threats.
        /// </summary>
        OrganizedSwarm,
        
        /// <summary>
        /// Military-style battle lines: Agents form proper front lines facing the enemy.
        /// Features shoulder-to-shoulder spacing, multiple ranks, flanking maneuvers,
        /// and coordinated advance/retreat. Best for faction vs faction battles.
        /// </summary>
        BattleLine
    }
}
