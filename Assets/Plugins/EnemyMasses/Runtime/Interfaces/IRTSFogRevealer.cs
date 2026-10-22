using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Optional interface for units that should reveal fog of war in Enemy Masses.
    /// </summary>
    public interface IRTSFogRevealer
    {
        /// <summary>
        /// Whether this unit should reveal fog of war.
        /// </summary>
        bool RevealFogOfWar { get; }

        /// <summary>
        /// World-space position used for fog revealing.
        /// </summary>
        Vector3 FogRevealPosition { get; }

        /// <summary>
        /// Reveal radius in world units.
        /// </summary>
        float FogRevealRadius { get; }
    }
}
