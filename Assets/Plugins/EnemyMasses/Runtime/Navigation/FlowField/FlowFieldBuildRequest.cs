using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Input payload for building a flow field.
    /// </summary>
    public struct FlowFieldBuildRequest
    {
        public int width;
        public int height;
        public float cellSize;
        public Vector3 origin;
        public Vector3 targetWorldPosition;
        public byte defaultCellCost;
        public FlowFieldBuildSettings settings;

        /// <summary>
        /// Optional walkability callback. If null, all cells are walkable.
        /// </summary>
        public Func<int, int, bool> isCellWalkable;

        /// <summary>
        /// Optional cost callback. If null, defaultCellCost is used.
        /// Valid range: 1-254.
        /// </summary>
        public Func<int, int, byte> getCellCost;

        /// <summary>
        /// Optional directed traversal links evaluated during integration expansion.
        /// Useful for explicit transitions such as climb links in Flow Field mode.
        /// </summary>
        public IReadOnlyList<FlowFieldDirectedLink> directedLinks;

        public static FlowFieldBuildRequest CreateDefault(
            int width,
            int height,
            float cellSize,
            Vector3 origin,
            Vector3 targetWorldPosition)
        {
            return new FlowFieldBuildRequest
            {
                width = width,
                height = height,
                cellSize = cellSize,
                origin = origin,
                targetWorldPosition = targetWorldPosition,
                defaultCellCost = 1,
                settings = FlowFieldBuildSettings.Default,
                isCellWalkable = null,
                getCellCost = null,
                directedLinks = null
            };
        }
    }
}
