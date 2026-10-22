using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.FlowField;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField.Debug
{
    /// <summary>
    /// Scene component entry point for drawing flow field debug gizmos.
    /// </summary>
    public class FlowFieldDebugVisualizer : MonoBehaviour
    {
        [SerializeField] private bool drawBlockedCells = true;
        [SerializeField] private bool drawVectors = true;
        [SerializeField] private float yOffset = 0.05f;
        [SerializeField] private float vectorScale = 0.4f;

        private IFlowFieldRuntimeService _runtimeService;
        private FlowFieldData _fieldOverride;

        public void SetRuntimeService(IFlowFieldRuntimeService runtimeService)
        {
            _runtimeService = runtimeService;
        }

        public void SetField(FlowFieldData field)
        {
            _fieldOverride = field;
        }

        private void OnDrawGizmos()
        {
            FlowFieldData field = _runtimeService?.CurrentField ?? _fieldOverride;
            FlowFieldDebugGizmoDrawer.Draw(field, drawBlockedCells, drawVectors, yOffset, vectorScale);
        }
    }
}
