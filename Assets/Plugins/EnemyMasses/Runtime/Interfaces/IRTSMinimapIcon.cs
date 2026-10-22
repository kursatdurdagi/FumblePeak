using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Optional interface for external units that should render as minimap icons.
    /// </summary>
    public interface IRTSMinimapIcon
    {
        /// <summary>
        /// Whether this unit should show on the minimap icon system.
        /// </summary>
        bool ShowOnMinimap { get; }

        /// <summary>
        /// World-space position for the minimap icon.
        /// </summary>
        Vector3 MinimapPosition { get; }

        /// <summary>
        /// Icon color for this unit on the minimap.
        /// </summary>
        Color MinimapColor { get; }
    }
}
