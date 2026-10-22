using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Build settings for generating flow fields.
    /// </summary>
    [System.Serializable]
    public struct FlowFieldBuildSettings
    {
        [Tooltip("Allow diagonal traversal when building integration and vector fields.")]
        public bool allowDiagonalMovement;

        [Tooltip("Allow diagonal moves that cut across blocked corners.")]
        public bool allowCornerCutting;

        [Min(1)]
        [Tooltip("Safety cap to prevent runaway integration loops.")]
        public int maxIntegrationIterations;

        [Min(0f)]
        [Tooltip("How often dynamic obstacles should trigger field refresh.")]
        public float dynamicObstacleRefreshInterval;

        public static FlowFieldBuildSettings Default => new FlowFieldBuildSettings
        {
            allowDiagonalMovement = true,
            allowCornerCutting = false,
            maxIntegrationIterations = 200000,
            dynamicObstacleRefreshInterval = 0.2f
        };
    }
}
