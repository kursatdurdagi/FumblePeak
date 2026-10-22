using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Shared registry for non-RTS controlled units that still participate in fog and minimap systems.
    /// </summary>
    public static class RTSWorldUnitRegistry
    {
        private static readonly HashSet<IRTSFogRevealer> FogRevealers = new HashSet<IRTSFogRevealer>();
        private static readonly HashSet<IRTSMinimapIcon> MinimapIcons = new HashSet<IRTSMinimapIcon>();

        public static IReadOnlyCollection<IRTSFogRevealer> RegisteredFogRevealers => FogRevealers;
        public static IReadOnlyCollection<IRTSMinimapIcon> RegisteredMinimapIcons => MinimapIcons;

        public static void RegisterFogRevealer(IRTSFogRevealer revealer)
        {
            if (revealer == null) return;
            FogRevealers.Add(revealer);
        }

        public static void UnregisterFogRevealer(IRTSFogRevealer revealer)
        {
            if (revealer == null) return;
            FogRevealers.Remove(revealer);
        }

        public static void RegisterMinimapIcon(IRTSMinimapIcon icon)
        {
            if (icon == null) return;
            MinimapIcons.Add(icon);
        }

        public static void UnregisterMinimapIcon(IRTSMinimapIcon icon)
        {
            if (icon == null) return;
            MinimapIcons.Remove(icon);
        }
    }
}
