using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Lightweight data snapshot for a single minimap marker.
    /// Designed for allocation-free reads — providers fill pre-allocated buffers.
    /// </summary>
    public struct MinimapMarker
    {
        /// <summary>World-space position (only X/Z are used for 2D mapping).</summary>
        public Vector3 Position;

        /// <summary>Display color (faction, owner, or per-crowd override).</summary>
        public Color Color;

        /// <summary>True if the unit is dead.</summary>
        public bool IsDead;

        /// <summary>True if the unit is currently selected by the player.</summary>
        public bool IsSelected;

        /// <summary>True if this unit belongs to a player-controlled faction.</summary>
        public bool IsPlayerControlled;

        /// <summary>
        /// Unique identifier for grouping (e.g., faction hash or crowd instance ID).
        /// Used by renderers to batch by color/shape.
        /// </summary>
        public int GroupId;

        /// <summary>
        /// Alert intensity for under-attack flashing. 0 = no alert, 1 = full flash.
        /// Set by the minimap data provider when an agent has recently been damaged.
        /// Minimap renderers should ping-pong the marker color based on this value.
        /// </summary>
        public float AlertIntensity;

        /// <summary>
        /// Internal agent index used by the data provider for alert lookups.
        /// Not intended for external use by minimap renderers.
        /// </summary>
        public int AgentIndex;
    }

    /// <summary>
    /// Pure data interface for minimap systems.
    /// Decouples agent data from rendering — any minimap UI (built-in or 3rd party)
    /// can subscribe to this provider without depending on cameras, layers, or EnemyMasses internals.
    /// </summary>
    public interface IMinimapDataProvider
    {
        /// <summary>
        /// Fills <paramref name="buffer"/> with current marker data and returns the count written.
        /// The buffer is owned by the caller and may be reused across frames.
        /// If the buffer is too small, only the first <c>buffer.Length</c> markers are written.
        /// </summary>
        /// <param name="buffer">Pre-allocated array to fill.</param>
        /// <returns>Number of markers written (≤ buffer.Length).</returns>
        int GetMarkers(MinimapMarker[] buffer);

        /// <summary>
        /// Total number of markers currently available (including dead, filtered, etc.).
        /// Use this to size your buffer before calling <see cref="GetMarkers"/>.
        /// </summary>
        int MarkerCount { get; }

        /// <summary>
        /// World-space bounds of all tracked units.
        /// Useful for auto-fitting minimap zoom.
        /// </summary>
        Bounds WorldBounds { get; }

        /// <summary>
        /// Fired when a significant change occurs (agents spawned/despawned, faction change).
        /// Lightweight listeners can use this instead of polling every frame.
        /// </summary>
        event Action OnMarkersChanged;
    }
}
