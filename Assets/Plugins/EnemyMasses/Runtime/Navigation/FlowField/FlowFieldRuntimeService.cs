using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Default runtime implementation for cost/integration/vector flow fields.
    /// </summary>
    public sealed class FlowFieldRuntimeService : IFlowFieldRuntimeService
    {
        private static readonly Vector2Int[] CardinalNeighbors =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0)
        };

        private static readonly Vector2Int[] DiagonalNeighbors =
        {
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1),
            new Vector2Int(-1, 1)
        };

        private readonly List<int> _openSet = new List<int>(2048);
        private readonly HashSet<int> _openLookup = new HashSet<int>();
        private readonly Dictionary<int, List<FlowFieldDirectedLink>> _directedOutgoingLinkLookup =
            new Dictionary<int, List<FlowFieldDirectedLink>>(128);
        private readonly Dictionary<int, List<FlowFieldDirectedLink>> _directedIncomingLinkLookup =
            new Dictionary<int, List<FlowFieldDirectedLink>>(128);

        public FlowFieldData CurrentField { get; private set; }

        public FlowFieldData BuildField(FlowFieldBuildRequest request)
        {
            ValidateRequest(request);

            byte defaultCost = request.defaultCellCost <= 0 ? (byte)1 : request.defaultCellCost;
            var field = new FlowFieldData(request.width, request.height, request.cellSize, request.origin);

            for (int y = 0; y < field.Height; y++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    bool walkable = request.isCellWalkable == null || request.isCellWalkable(x, y);
                    byte cost = request.getCellCost != null ? request.getCellCost(x, y) : defaultCost;
                    cost = cost == 0 ? (byte)1 : cost;
                    cost = (byte)Mathf.Clamp(cost, 1, 254);

                    if (!walkable)
                        cost = FlowFieldData.BlockedCellCost;

                    field.SetWalkable(x, y, walkable);
                    field.SetCost(x, y, cost);
                    field.SetIntegration(x, y, float.PositiveInfinity);
                    field.SetDirection(x, y, Vector2Int.zero);
                }
            }

            if (!field.TryWorldToCell(request.targetWorldPosition, out Vector2Int targetCell))
            {
                CurrentField = field;
                return field;
            }

            if (!field.GetWalkable(targetCell.x, targetCell.y) &&
                !TryFindNearestWalkableCell(field, targetCell, out targetCell))
            {
                CurrentField = field;
                return field;
            }

            field.TargetCell = targetCell;
            field.HasValidTarget = true;

            BuildDirectedLinkLookup(field, request.directedLinks);
            BuildIntegrationField(field, targetCell, request.settings);
            BuildDirectionField(field, request.settings);

            CurrentField = field;
            return field;
        }

        public bool TrySampleDirection(Vector3 worldPosition, out Vector3 direction)
        {
            direction = Vector3.zero;
            var field = CurrentField;
            if (field == null)
                return false;

            if (!field.TryWorldToCell(worldPosition, out Vector2Int cell))
                return false;

            return field.TryGetDirectionWorld(cell.x, cell.y, out direction);
        }

        public void Clear()
        {
            CurrentField = null;
            _openSet.Clear();
            _openLookup.Clear();
            _directedOutgoingLinkLookup.Clear();
            _directedIncomingLinkLookup.Clear();
        }

        private void BuildIntegrationField(FlowFieldData field, Vector2Int targetCell, FlowFieldBuildSettings settings)
        {
            _openSet.Clear();
            _openLookup.Clear();

            int targetIndex = field.ToIndex(targetCell.x, targetCell.y);
            field.SetIntegration(targetCell.x, targetCell.y, 0f);
            _openSet.Add(targetIndex);
            _openLookup.Add(targetIndex);

            int maxIterations = Mathf.Max(1, settings.maxIntegrationIterations);
            int iterations = 0;

            while (_openSet.Count > 0 && iterations < maxIterations)
            {
                int currentIndex = PopLowestIntegrationIndex(field);
                int currentX = currentIndex % field.Width;
                int currentY = currentIndex / field.Width;
                float currentIntegration = field.GetIntegration(currentX, currentY);

                RelaxNeighbors(field, currentX, currentY, currentIntegration, CardinalNeighbors, 1f, settings);

                if (settings.allowDiagonalMovement)
                    RelaxNeighbors(field, currentX, currentY, currentIntegration, DiagonalNeighbors, 1.41421356f, settings);

                RelaxDirectedLinks(field, currentIndex, currentIntegration);

                iterations++;
            }
        }

        private void BuildDirectionField(FlowFieldData field, FlowFieldBuildSettings settings)
        {
            for (int y = 0; y < field.Height; y++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    if (!field.GetWalkable(x, y))
                    {
                        field.SetDirection(x, y, Vector2Int.zero);
                        continue;
                    }

                    float currentIntegration = field.GetIntegration(x, y);
                    if (float.IsInfinity(currentIntegration))
                    {
                        field.SetDirection(x, y, Vector2Int.zero);
                        continue;
                    }

                    Vector2Int bestDirection = Vector2Int.zero;
                    float bestIntegration = currentIntegration;

                    TryPickBetterDirection(field, x, y, CardinalNeighbors, ref bestIntegration, ref bestDirection);

                    if (settings.allowDiagonalMovement)
                        TryPickBetterDirection(field, x, y, DiagonalNeighbors, ref bestIntegration, ref bestDirection);

                    TryPickBetterDirectedLink(field, x, y, ref bestIntegration, ref bestDirection);

                    field.SetDirection(x, y, bestDirection);
                }
            }
        }

        private void RelaxNeighbors(
            FlowFieldData field,
            int currentX,
            int currentY,
            float currentIntegration,
            Vector2Int[] offsets,
            float moveMultiplier,
            FlowFieldBuildSettings settings)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2Int offset = offsets[i];
                int nextX = currentX + offset.x;
                int nextY = currentY + offset.y;

                if (!field.IsInBounds(nextX, nextY))
                    continue;

                if (!field.GetWalkable(nextX, nextY))
                    continue;

                if (!settings.allowCornerCutting && Mathf.Abs(offset.x) == 1 && Mathf.Abs(offset.y) == 1)
                {
                    if (!field.GetWalkable(currentX + offset.x, currentY) ||
                        !field.GetWalkable(currentX, currentY + offset.y))
                    {
                        continue;
                    }
                }

                float neighborCost = field.GetCost(nextX, nextY);
                float candidateIntegration = currentIntegration + neighborCost * moveMultiplier;

                if (candidateIntegration >= field.GetIntegration(nextX, nextY))
                    continue;

                field.SetIntegration(nextX, nextY, candidateIntegration);
                int nextIndex = field.ToIndex(nextX, nextY);
                if (_openLookup.Add(nextIndex))
                    _openSet.Add(nextIndex);
            }
        }

        private void RelaxDirectedLinks(
            FlowFieldData field,
            int currentIndex,
            float currentIntegration)
        {
            if (!_directedIncomingLinkLookup.TryGetValue(currentIndex, out List<FlowFieldDirectedLink> links))
                return;

            for (int i = 0; i < links.Count; i++)
            {
                FlowFieldDirectedLink link = links[i];
                int fromX = link.fromCell.x;
                int fromY = link.fromCell.y;

                if (!field.IsInBounds(fromX, fromY) || !field.GetWalkable(fromX, fromY))
                    continue;

                float targetCellCost = field.GetCost(link.toCell.x, link.toCell.y);
                float traversalMultiplier = Mathf.Max(0.01f, link.traversalCostMultiplier);
                float candidateIntegration = currentIntegration + targetCellCost * traversalMultiplier;

                if (candidateIntegration >= field.GetIntegration(fromX, fromY))
                    continue;

                field.SetIntegration(fromX, fromY, candidateIntegration);
                int fromIndex = field.ToIndex(fromX, fromY);
                if (_openLookup.Add(fromIndex))
                    _openSet.Add(fromIndex);
            }
        }

        private static void TryPickBetterDirection(
            FlowFieldData field,
            int x,
            int y,
            Vector2Int[] offsets,
            ref float bestIntegration,
            ref Vector2Int bestDirection)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2Int offset = offsets[i];
                int nx = x + offset.x;
                int ny = y + offset.y;

                if (!field.IsInBounds(nx, ny) || !field.GetWalkable(nx, ny))
                    continue;

                float neighborIntegration = field.GetIntegration(nx, ny);
                if (neighborIntegration < bestIntegration)
                {
                    bestIntegration = neighborIntegration;
                    bestDirection = offset;
                }
            }
        }

        private void TryPickBetterDirectedLink(
            FlowFieldData field,
            int x,
            int y,
            ref float bestIntegration,
            ref Vector2Int bestDirection)
        {
            int fromIndex = field.ToIndex(x, y);
            if (!_directedOutgoingLinkLookup.TryGetValue(fromIndex, out List<FlowFieldDirectedLink> links))
                return;

            for (int i = 0; i < links.Count; i++)
            {
                FlowFieldDirectedLink link = links[i];
                int toX = link.toCell.x;
                int toY = link.toCell.y;

                if (!field.IsInBounds(toX, toY) || !field.GetWalkable(toX, toY))
                    continue;

                float neighborIntegration = field.GetIntegration(toX, toY);
                if (neighborIntegration >= bestIntegration)
                    continue;

                bestIntegration = neighborIntegration;
                bestDirection = new Vector2Int(toX - x, toY - y);
            }
        }

        private int PopLowestIntegrationIndex(FlowFieldData field)
        {
            int bestListIndex = 0;
            int bestIndex = _openSet[0];
            int bx = bestIndex % field.Width;
            int by = bestIndex / field.Width;
            float bestValue = field.GetIntegration(bx, by);

            for (int i = 1; i < _openSet.Count; i++)
            {
                int candidateIndex = _openSet[i];
                int cx = candidateIndex % field.Width;
                int cy = candidateIndex / field.Width;
                float candidateValue = field.GetIntegration(cx, cy);

                if (candidateValue < bestValue)
                {
                    bestValue = candidateValue;
                    bestListIndex = i;
                    bestIndex = candidateIndex;
                }
            }

            _openSet.RemoveAt(bestListIndex);
            _openLookup.Remove(bestIndex);
            return bestIndex;
        }

        private static bool TryFindNearestWalkableCell(FlowFieldData field, Vector2Int origin, out Vector2Int result)
        {
            int maxRadius = Mathf.Max(field.Width, field.Height);
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                int minX = origin.x - radius;
                int maxX = origin.x + radius;
                int minY = origin.y - radius;
                int maxY = origin.y + radius;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        bool onPerimeter = x == minX || x == maxX || y == minY || y == maxY;
                        if (!onPerimeter || !field.IsInBounds(x, y))
                            continue;

                        if (field.GetWalkable(x, y))
                        {
                            result = new Vector2Int(x, y);
                            return true;
                        }
                    }
                }
            }

            result = origin;
            return false;
        }

        private void BuildDirectedLinkLookup(
            FlowFieldData field,
            IReadOnlyList<FlowFieldDirectedLink> directedLinks)
        {
            _directedOutgoingLinkLookup.Clear();
            _directedIncomingLinkLookup.Clear();

            if (directedLinks == null || directedLinks.Count == 0)
                return;

            for (int i = 0; i < directedLinks.Count; i++)
            {
                FlowFieldDirectedLink link = directedLinks[i];
                Vector2Int from = link.fromCell;
                Vector2Int to = link.toCell;

                if (from == to)
                    continue;

                if (!field.IsInBounds(from.x, from.y) || !field.IsInBounds(to.x, to.y))
                    continue;

                if (!field.GetWalkable(from.x, from.y) || !field.GetWalkable(to.x, to.y))
                    continue;

                link.traversalCostMultiplier = Mathf.Max(0.01f, link.traversalCostMultiplier);
                int fromIndex = field.ToIndex(from.x, from.y);
                if (!_directedOutgoingLinkLookup.TryGetValue(fromIndex, out List<FlowFieldDirectedLink> links))
                {
                    links = new List<FlowFieldDirectedLink>(2);
                    _directedOutgoingLinkLookup.Add(fromIndex, links);
                }

                links.Add(link);

                int toIndex = field.ToIndex(to.x, to.y);
                if (!_directedIncomingLinkLookup.TryGetValue(toIndex, out List<FlowFieldDirectedLink> incomingLinks))
                {
                    incomingLinks = new List<FlowFieldDirectedLink>(2);
                    _directedIncomingLinkLookup.Add(toIndex, incomingLinks);
                }

                incomingLinks.Add(link);
            }
        }

        private static void ValidateRequest(FlowFieldBuildRequest request)
        {
            if (request.width <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(request.width), "Flow field width must be > 0.");

            if (request.height <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(request.height), "Flow field height must be > 0.");

            if (request.cellSize <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(request.cellSize), "Flow field cell size must be > 0.");
        }
    }
}
