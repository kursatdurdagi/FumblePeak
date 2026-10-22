using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Abstraction for fog-of-war visibility queries.
    /// Implemented by FogOfWarLite to allow other systems (e.g., minimap) to query visibility
    /// without referencing the fog assembly directly.
    /// </summary>
    public interface IFogVisibility
    {
        /// <summary>
        /// Returns true if the given world position is considered revealed (alpha below maxAlpha).
        /// This includes areas that are refogging but not yet fully hidden.
        /// </summary>
        bool IsRevealed(Vector3 worldPos, byte maxAlpha = 250);

        /// <summary>
        /// Returns true if the given world position is actively visible right now (not refogged).
        /// Use this for minimap visibility when you want icons to hide during refog.
        /// </summary>
        /// <param name="worldPos">World position to check</param>
        /// <param name="maxAlpha">Maximum alpha to consider "actively visible" (default 32 = very clear)</param>
        bool IsActivelyVisible(Vector3 worldPos, byte maxAlpha = 32);
    }
}
