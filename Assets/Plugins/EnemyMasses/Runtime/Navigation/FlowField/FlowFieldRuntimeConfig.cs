using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Authoring asset for flow field runtime build dimensions and policy.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FlowFieldRuntimeConfig",
        menuName = "Enemy Masses/Navigation/Flow Field Runtime Config")]
    public class FlowFieldRuntimeConfig : ScriptableObject
    {
        [Min(1)] public int width = 128;
        [Min(1)] public int height = 128;
        [Min(0.1f)] public float cellSize = 1f;
        public FlowFieldBuildSettings buildSettings = FlowFieldBuildSettings.Default;
    }
}
