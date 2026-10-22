using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.FlowField;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField.Debug
{
    /// <summary>
    /// Debug drawing helper for visualizing flow field walkability and vectors.
    /// </summary>
    public static class FlowFieldDebugGizmoDrawer
    {
        public static void Draw(
            FlowFieldData field,
            bool drawBlockedCells = true,
            bool drawVectors = true,
            float yOffset = 0.05f,
            float vectorScale = 0.4f)
        {
            if (field == null)
                return;

            float halfCell = field.CellSize * 0.5f;
            float blockSize = Mathf.Max(0.02f, field.CellSize * 0.9f);

            for (int y = 0; y < field.Height; y++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    Vector3 center = field.GetCellCenterWorld(x, y) + Vector3.up * yOffset;
                    bool walkable = field.GetWalkable(x, y);

                    if (!walkable)
                    {
                        if (!drawBlockedCells)
                            continue;

                        Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 0.35f);
                        Gizmos.DrawCube(center, new Vector3(blockSize, 0.02f, blockSize));
                        continue;
                    }

                    if (!drawVectors || !field.TryGetDirectionWorld(x, y, out Vector3 direction))
                        continue;

                    Gizmos.color = new Color(0.2f, 0.9f, 0.95f, 0.9f);
                    Vector3 start = center;
                    Vector3 end = start + direction * halfCell * Mathf.Clamp01(vectorScale);
                    Gizmos.DrawLine(start, end);
                }
            }
        }
    }
}
