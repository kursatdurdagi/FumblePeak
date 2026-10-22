using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Spatial grid for efficient proximity queries and spatial batching of crowd operations.
    /// Optimized for 2D navigation on NavMesh surfaces.
    /// </summary>
    public class SpatialGrid
    {
        private readonly Dictionary<Vector2Int, List<int>> _grid;
        private readonly float _cellSize;
        private readonly float _inverseCellSize;
        private Vector3 _worldMin;
        private Vector3 _worldMax;
        private bool _boundsInitialized;

        public SpatialGrid(float cellSize)
        {
            _cellSize = cellSize;
            _inverseCellSize = 1f / cellSize;
            _grid = new Dictionary<Vector2Int, List<int>>();
            _boundsInitialized = false;
        }

        public void Clear()
        {
            foreach (var cell in _grid.Values)
                cell.Clear();
        }

        public void AddAgent(int agentIndex, Vector3 position)
        {
            Vector2Int gridPos = WorldToGrid(position);
            
            if (!_grid.TryGetValue(gridPos, out List<int> cell))
            {
                cell = new List<int>();
                _grid[gridPos] = cell;
            }
            
            cell.Add(agentIndex);
            UpdateBounds(position);
        }

        public List<int> GetAgentsInCell(Vector3 position)
        {
            Vector2Int gridPos = WorldToGrid(position);
            return _grid.TryGetValue(gridPos, out List<int> cell) ? cell : null;
        }

        public List<int> GetAgentsInRadius(Vector3 center, float radius)
        {
            List<int> result = new List<int>();
            int cellRadius = Mathf.CeilToInt(radius * _inverseCellSize);
            Vector2Int centerGrid = WorldToGrid(center);

            for (int x = -cellRadius; x <= cellRadius; x++)
            {
                for (int z = -cellRadius; z <= cellRadius; z++)
                {
                    Vector2Int gridPos = new Vector2Int(centerGrid.x + x, centerGrid.y + z);
                    if (_grid.TryGetValue(gridPos, out List<int> cell))
                    {
                        result.AddRange(cell);
                    }
                }
            }

            return result;
        }

        public IEnumerable<KeyValuePair<Vector2Int, List<int>>> GetAllCells()
        {
            return _grid;
        }

        private Vector2Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x * _inverseCellSize),
                Mathf.FloorToInt(worldPos.z * _inverseCellSize)
            );
        }

        private void UpdateBounds(Vector3 position)
        {
            if (!_boundsInitialized)
            {
                _worldMin = _worldMax = position;
                _boundsInitialized = true;
            }
            else
            {
                _worldMin = Vector3.Min(_worldMin, position);
                _worldMax = Vector3.Max(_worldMax, position);
            }
        }

        public Vector3 GetWorldMin() => _worldMin;
        public Vector3 GetWorldMax() => _worldMax;
        public float GetCellSize() => _cellSize;
    }
}
