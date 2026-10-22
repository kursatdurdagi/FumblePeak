using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Runtime container for a generated flow field (cost + integration + direction).
    /// </summary>
    public sealed class FlowFieldData
    {
        public const byte BlockedCellCost = 255;

        private readonly bool[] _walkable;
        private readonly byte[] _cost;
        private readonly float[] _integration;
        private readonly Vector2Int[] _directions;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }
        public Vector2Int TargetCell { get; internal set; }
        public bool HasValidTarget { get; internal set; }

        public FlowFieldData(int width, int height, float cellSize, Vector3 origin)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSize = Mathf.Max(0.01f, cellSize);
            Origin = origin;

            int count = Width * Height;
            _walkable = new bool[count];
            _cost = new byte[count];
            _integration = new float[count];
            _directions = new Vector2Int[count];

            for (int i = 0; i < count; i++)
            {
                _walkable[i] = true;
                _cost[i] = 1;
                _integration[i] = float.PositiveInfinity;
                _directions[i] = Vector2Int.zero;
            }
        }

        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public int ToIndex(int x, int y)
        {
            return y * Width + x;
        }

        public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
        {
            int x = Mathf.FloorToInt((worldPosition.x - Origin.x) / CellSize);
            int y = Mathf.FloorToInt((worldPosition.z - Origin.z) / CellSize);
            cell = new Vector2Int(x, y);
            return IsInBounds(x, y);
        }

        public Vector3 GetCellCenterWorld(int x, int y)
        {
            float worldX = Origin.x + (x + 0.5f) * CellSize;
            float worldZ = Origin.z + (y + 0.5f) * CellSize;
            return new Vector3(worldX, Origin.y, worldZ);
        }

        public bool GetWalkable(int x, int y)
        {
            if (!IsInBounds(x, y))
                return false;
            return _walkable[ToIndex(x, y)];
        }

        public byte GetCost(int x, int y)
        {
            if (!IsInBounds(x, y))
                return BlockedCellCost;
            return _cost[ToIndex(x, y)];
        }

        public float GetIntegration(int x, int y)
        {
            if (!IsInBounds(x, y))
                return float.PositiveInfinity;
            return _integration[ToIndex(x, y)];
        }

        public Vector2Int GetDirectionCellOffset(int x, int y)
        {
            if (!IsInBounds(x, y))
                return Vector2Int.zero;
            return _directions[ToIndex(x, y)];
        }

        public bool TryGetDirectionWorld(int x, int y, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!IsInBounds(x, y))
                return false;

            Vector2Int offset = _directions[ToIndex(x, y)];
            if (offset == Vector2Int.zero)
                return false;

            direction = new Vector3(offset.x, 0f, offset.y).normalized;
            return direction.sqrMagnitude > 0.0001f;
        }

        internal void SetWalkable(int x, int y, bool value)
        {
            _walkable[ToIndex(x, y)] = value;
        }

        internal void SetCost(int x, int y, byte value)
        {
            _cost[ToIndex(x, y)] = value;
        }

        internal void SetIntegration(int x, int y, float value)
        {
            _integration[ToIndex(x, y)] = value;
        }

        internal void SetDirection(int x, int y, Vector2Int offset)
        {
            _directions[ToIndex(x, y)] = offset;
        }
    }
}
