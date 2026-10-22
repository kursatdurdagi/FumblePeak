using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Service contract for generating and sampling flow fields.
    /// </summary>
    public interface IFlowFieldRuntimeService
    {
        FlowFieldData CurrentField { get; }
        FlowFieldData BuildField(FlowFieldBuildRequest request);
        bool TrySampleDirection(Vector3 worldPosition, out Vector3 direction);
        void Clear();
    }
}
