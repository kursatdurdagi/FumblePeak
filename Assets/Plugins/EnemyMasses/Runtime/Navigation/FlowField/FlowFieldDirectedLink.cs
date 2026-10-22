using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Directed traversal edge used by flow-field integration. This allows the
    /// solver to "jump" from one cell to another (for example climb links).
    /// </summary>
    public struct FlowFieldDirectedLink
    {
        /// <summary>Source cell index in flow-field grid coordinates.</summary>
        public Vector2Int fromCell;

        /// <summary>Destination cell index in flow-field grid coordinates.</summary>
        public Vector2Int toCell;

        /// <summary>
        /// Cost multiplier applied when traversing this edge.
        /// 1 = similar to normal cardinal move cost, higher = less preferred.
        /// </summary>
        public float traversalCostMultiplier;
    }
}
