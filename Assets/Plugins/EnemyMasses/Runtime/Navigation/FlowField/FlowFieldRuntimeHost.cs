using System;
using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.FlowField.Debug;
using Arawn.EnemyMasses.Runtime.Navigation.Climbing;

namespace Arawn.EnemyMasses.Runtime.Navigation.FlowField
{
    /// <summary>
    /// Scene host for building and exposing a shared runtime flow field.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class FlowFieldRuntimeHost : MonoBehaviour
    {
        [Serializable]
        private struct CostLayerRule
        {
            [Tooltip("Optional label for readability.")]
            public string label;

            [Tooltip("Any collider in these layers raises the cell traversal cost.")]
            public LayerMask layers;

            [Range(1, 254)]
            [Tooltip("Absolute cost assigned when this rule matches. Higher means less preferred.")]
            public int cost;
        }

        private sealed class ColliderInstanceIdComparer : IComparer<Collider>
        {
            public static readonly ColliderInstanceIdComparer Instance = new ColliderInstanceIdComparer();

            public int Compare(Collider left, Collider right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null)
                    return 1;
                if (right == null)
                    return -1;
                return left.GetInstanceID().CompareTo(right.GetInstanceID());
            }
        }

        [SerializeField] private FlowFieldRuntimeConfig config;
        [SerializeField] private bool autoRebuildFromTrackedTarget = false;
        [SerializeField] private Transform trackedTarget;
        [SerializeField] private bool centerGridOnHostTransform = true;
        [SerializeField] private Vector3 originOffset = Vector3.zero;
        [SerializeField] private float rebuildInterval = 0.25f;
        [SerializeField] private byte defaultCellCost = 1;

        [Header("Obstacle Sampling")]
        [SerializeField] private bool enableObstacleSampling = false;
        [SerializeField] private LayerMask obstacleLayers = 0;
        [SerializeField] [Range(0.1f, 1f)] private float obstacleProbeCellRadius = 0.45f;
        [SerializeField] [Min(0f)] private float obstacleClearance = 0f;
        [SerializeField] [Min(0.1f)] private float obstacleProbeHeight = 2f;
        [SerializeField] private float obstacleProbeVerticalOffset = 1f;
        [SerializeField] private QueryTriggerInteraction obstacleQueryTriggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Cost Layers (Optional)")]
        [SerializeField] private bool enableCostLayers = false;
        [SerializeField] private CostLayerRule[] costLayerRules = Array.Empty<CostLayerRule>();
        [SerializeField] [Range(0.1f, 1f)] private float costProbeCellRadius = 0.45f;
        [SerializeField] private QueryTriggerInteraction costLayerQueryTriggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Dynamic Obstacle Rebuild")]
        [SerializeField] private bool autoRebuildWhenDynamicObstaclesChange = false;
        [SerializeField] [Min(0.05f)] private float dynamicObstacleCheckInterval = 0.2f;

        [Header("Flow Field Climb Links")]
        [SerializeField] private bool enableClimbableFlowFieldLinks = true;
        [SerializeField] private bool climbLinksBidirectional = true;
        [SerializeField] [Min(0.1f)] private float climbLinkCostMultiplier = 1f;
        [SerializeField] [Min(0)] private int climbLinkEndpointSearchRadiusCells = 2;
        [SerializeField] [Min(0.1f)] private float climbableDiscoveryInterval = 1f;

        [Header("Editor Gizmos")]
        [SerializeField] private bool drawEditorGizmos = true;
        [SerializeField] private bool drawGizmosOnlyWhenSelected = false;
        [SerializeField] private bool drawGridBounds = true;
        [SerializeField] private bool drawGridLines = true;
        [SerializeField] private bool drawBlockedCells = true;
        [SerializeField] private bool drawFlowVectors = false;
        [SerializeField] private bool drawTargetCell = true;
        [SerializeField] private float gizmoYOffset = 0.05f;
        [SerializeField] private float gizmoVectorScale = 0.4f;
        [SerializeField] private Color gridBoundsColor = new Color(0.12f, 0.95f, 0.9f, 0.85f);
        [SerializeField] private Color gridLineColor = new Color(0.2f, 0.85f, 1f, 0.2f);
        [SerializeField] private Color targetCellColor = new Color(1f, 0.8f, 0.2f, 0.75f);

        private readonly FlowFieldRuntimeService _service = new FlowFieldRuntimeService();
        private readonly List<Collider> _dynamicObstacleColliders = new List<Collider>(256);
        private readonly List<NavMeshLinkClimbable> _cachedClimbables = new List<NavMeshLinkClimbable>(32);
        private readonly List<FlowFieldDirectedLink> _flowFieldClimbLinks = new List<FlowFieldDirectedLink>(128);
        private Collider[] _dynamicObstacleBuffer = new Collider[256];
        private float _nextRebuildTime;
        private float _nextDynamicObstacleCheckTime;
        private float _nextClimbableDiscoveryTime;
        private bool _hasLastBuildTarget;
        private Vector3 _lastBuildTargetPosition;
        private bool _hasDynamicObstacleHash;
        private int _lastDynamicObstacleHash;
        private bool _rebuildRequested;
        private bool _dynamicObstaclesDirty;

        private const float DynamicHashPositionStep = 0.05f;
        private const float DynamicHashRotationStep = 2f;

        public FlowFieldRuntimeConfig Config => config;
        public IFlowFieldRuntimeService Service => _service;
        public FlowFieldData CurrentField => _service.CurrentField;

        private void Update()
        {
            if (config == null)
                return;

            bool shouldBuild = false;
            Vector3 rebuildTarget = Vector3.zero;

            if (autoRebuildFromTrackedTarget && trackedTarget != null && Time.time >= _nextRebuildTime)
            {
                shouldBuild = true;
                rebuildTarget = trackedTarget.position;
            }

            bool obstacleChangeTriggered = CheckDynamicObstacleStateChange();
            bool explicitTriggerRequested = _dynamicObstaclesDirty || _rebuildRequested;

            if (!shouldBuild && (obstacleChangeTriggered || explicitTriggerRequested))
            {
                if (Time.time < _nextRebuildTime)
                    return;

                if (!TryResolveRebuildTarget(out rebuildTarget))
                    return;

                shouldBuild = true;
            }

            if (!shouldBuild)
                return;

            TryBuildField(rebuildTarget);
            _nextRebuildTime = Time.time + GetEffectiveRebuildInterval();
            _rebuildRequested = false;
            _dynamicObstaclesDirty = false;
        }

        /// <summary>
        /// Builds a field to the given target using current config values.
        /// </summary>
        public bool TryBuildField(Vector3 targetWorldPosition)
        {
            if (config == null)
                return false;

            byte clampedDefaultCost = (byte)Mathf.Clamp(defaultCellCost, 1, 254);
            Vector3 origin = ResolveGridOrigin(config);
            var request = new FlowFieldBuildRequest
            {
                width = config.width,
                height = config.height,
                cellSize = config.cellSize,
                origin = origin,
                targetWorldPosition = targetWorldPosition,
                defaultCellCost = clampedDefaultCost,
                settings = config.buildSettings,
                isCellWalkable = null,
                getCellCost = null,
                directedLinks = null
            };

            bool useObstacleSampling = enableObstacleSampling && obstacleLayers.value != 0;
            bool useCostSampling = enableCostLayers && costLayerRules != null && costLayerRules.Length > 0;
            float cellSize = Mathf.Max(0.01f, config.cellSize);
            float probeHalfHeight = Mathf.Max(0.05f, obstacleProbeHeight * 0.5f);

            if (useObstacleSampling)
            {
                float obstacleRadius = Mathf.Max(0.01f,
                    cellSize * Mathf.Clamp(obstacleProbeCellRadius, 0.1f, 1f) + Mathf.Max(0f, obstacleClearance));
                Vector3 obstacleHalfExtents = new Vector3(obstacleRadius, probeHalfHeight, obstacleRadius);
                request.isCellWalkable = (x, y) =>
                    IsCellWalkable(x, y, origin, cellSize, obstacleHalfExtents);
            }

            if (useCostSampling)
            {
                float costRadius = Mathf.Max(0.01f, cellSize * Mathf.Clamp(costProbeCellRadius, 0.1f, 1f));
                Vector3 costHalfExtents = new Vector3(costRadius, probeHalfHeight, costRadius);
                request.getCellCost = (x, y) =>
                    GetCellCost(x, y, origin, cellSize, clampedDefaultCost, costHalfExtents);
            }

            if (enableClimbableFlowFieldLinks)
            {
                BuildClimbableFlowFieldLinks(
                    request.width,
                    request.height,
                    origin,
                    cellSize,
                    request.isCellWalkable,
                    _flowFieldClimbLinks);

                if (_flowFieldClimbLinks.Count > 0)
                    request.directedLinks = _flowFieldClimbLinks;
            }

            FlowFieldData field = _service.BuildField(request);
            bool buildSucceeded = field != null && field.HasValidTarget;

            if (buildSucceeded)
            {
                _lastBuildTargetPosition = targetWorldPosition;
                _hasLastBuildTarget = true;
            }

            if (useObstacleSampling && autoRebuildWhenDynamicObstaclesChange)
            {
                _lastDynamicObstacleHash = ComputeDynamicObstacleHash();
                _hasDynamicObstacleHash = true;
            }

            return buildSucceeded;
        }

        /// <summary>
        /// Requests a rebuild on the next eligible update tick.
        /// Uses tracked target if present, otherwise reuses the last built target.
        /// </summary>
        public void RequestRebuild()
        {
            _rebuildRequested = true;
        }

        /// <summary>
        /// Marks dynamic obstacles as dirty so the field rebuilds on the next eligible update.
        /// Useful for external systems that know an obstacle moved.
        /// </summary>
        public void MarkDynamicObstaclesDirty()
        {
            _dynamicObstaclesDirty = true;
        }

        public bool TrySampleDirection(Vector3 worldPosition, out Vector3 direction)
        {
            return _service.TrySampleDirection(worldPosition, out direction);
        }

        public void ClearField()
        {
            _service.Clear();
            _flowFieldClimbLinks.Clear();
        }

        private void BuildClimbableFlowFieldLinks(
            int width,
            int height,
            Vector3 origin,
            float cellSize,
            Func<int, int, bool> isCellWalkable,
            List<FlowFieldDirectedLink> outputLinks)
        {
            outputLinks.Clear();
            RefreshCachedClimbables();

            if (_cachedClimbables.Count == 0)
                return;

            for (int i = 0; i < _cachedClimbables.Count; i++)
            {
                NavMeshLinkClimbable climbable = _cachedClimbables[i];
                if (climbable == null)
                    continue;

                IReadOnlyList<NavMeshLinkClimbable.ClimbLinkInfo> links = climbable.GetClimbLinks();
                if (links == null || links.Count == 0)
                    continue;

                for (int j = 0; j < links.Count; j++)
                {
                    NavMeshLinkClimbable.ClimbLinkInfo link = links[j];
                    if (link == null)
                        continue;

                    if (!link.flowFieldEnabled)
                        continue;

                    float linkCost = Mathf.Max(0.1f, climbLinkCostMultiplier * Mathf.Max(0.1f, link.flowFieldCostMultiplier));

                    if (!TryResolveLinkCell(link.start, width, height, origin, cellSize, isCellWalkable, out Vector2Int fromCell))
                        continue;

                    if (!TryResolveLinkCell(link.end, width, height, origin, cellSize, isCellWalkable, out Vector2Int toCell))
                        continue;

                    if (fromCell == toCell)
                        continue;

                    outputLinks.Add(new FlowFieldDirectedLink
                    {
                        fromCell = fromCell,
                        toCell = toCell,
                        traversalCostMultiplier = linkCost
                    });

                    if (!climbLinksBidirectional)
                        continue;

                    outputLinks.Add(new FlowFieldDirectedLink
                    {
                        fromCell = toCell,
                        toCell = fromCell,
                        traversalCostMultiplier = linkCost
                    });
                }
            }
        }

        private void RefreshCachedClimbables()
        {
            if (Application.isPlaying && _cachedClimbables.Count > 0 && Time.time < _nextClimbableDiscoveryTime)
                return;

            _nextClimbableDiscoveryTime = Application.isPlaying
                ? Time.time + Mathf.Max(0.1f, climbableDiscoveryInterval)
                : 0f;
            _cachedClimbables.Clear();

#if UNITY_2023_1_OR_NEWER
            NavMeshLinkClimbable[] found = FindObjectsByType<NavMeshLinkClimbable>(FindObjectsSortMode.None);
#else
            NavMeshLinkClimbable[] found = FindObjectsOfType<NavMeshLinkClimbable>();
#endif
            if (found == null || found.Length == 0)
                return;

            for (int i = 0; i < found.Length; i++)
            {
                NavMeshLinkClimbable climbable = found[i];
                if (climbable == null || !climbable.isActiveAndEnabled)
                    continue;

                _cachedClimbables.Add(climbable);
            }
        }

        private bool TryResolveLinkCell(
            Vector3 worldPosition,
            int width,
            int height,
            Vector3 origin,
            float cellSize,
            Func<int, int, bool> isCellWalkable,
            out Vector2Int cell)
        {
            int x = Mathf.FloorToInt((worldPosition.x - origin.x) / cellSize);
            int y = Mathf.FloorToInt((worldPosition.z - origin.z) / cellSize);
            cell = new Vector2Int(x, y);

            if (IsCellWalkableForLink(x, y, width, height, isCellWalkable))
                return true;

            int radius = Mathf.Max(0, climbLinkEndpointSearchRadiusCells);
            if (radius == 0)
                return false;

            int bestDistanceSqr = int.MaxValue;
            Vector2Int bestCell = cell;
            bool found = false;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int candidateX = x + dx;
                    int candidateY = y + dy;
                    if (!IsCellWalkableForLink(candidateX, candidateY, width, height, isCellWalkable))
                        continue;

                    int distanceSqr = dx * dx + dy * dy;
                    if (distanceSqr >= bestDistanceSqr)
                        continue;

                    bestDistanceSqr = distanceSqr;
                    bestCell = new Vector2Int(candidateX, candidateY);
                    found = true;
                }
            }

            if (!found)
                return false;

            cell = bestCell;
            return true;
        }

        private static bool IsCellWalkableForLink(
            int x,
            int y,
            int width,
            int height,
            Func<int, int, bool> isCellWalkable)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                return false;

            return isCellWalkable == null || isCellWalkable(x, y);
        }

        private bool TryResolveRebuildTarget(out Vector3 targetPosition)
        {
            if (trackedTarget != null)
            {
                targetPosition = trackedTarget.position;
                return true;
            }

            if (_hasLastBuildTarget)
            {
                targetPosition = _lastBuildTargetPosition;
                return true;
            }

            targetPosition = Vector3.zero;
            return false;
        }

        private bool CheckDynamicObstacleStateChange()
        {
            if (!autoRebuildWhenDynamicObstaclesChange || !enableObstacleSampling || obstacleLayers.value == 0 || config == null)
                return false;

            if (Time.time < _nextDynamicObstacleCheckTime)
                return false;

            _nextDynamicObstacleCheckTime = Time.time + Mathf.Max(0.05f, dynamicObstacleCheckInterval);
            int newHash = ComputeDynamicObstacleHash();

            if (!_hasDynamicObstacleHash)
            {
                _lastDynamicObstacleHash = newHash;
                _hasDynamicObstacleHash = true;
                return false;
            }

            if (newHash == _lastDynamicObstacleHash)
                return false;

            _lastDynamicObstacleHash = newHash;
            return true;
        }

        private int ComputeDynamicObstacleHash()
        {
            if (config == null || obstacleLayers.value == 0)
                return 0;

            Vector3 origin = ResolveGridOrigin(config);
            float cellSize = Mathf.Max(0.01f, config.cellSize);
            float widthWorld = Mathf.Max(1, config.width) * cellSize;
            float heightWorld = Mathf.Max(1, config.height) * cellSize;
            float halfProbeHeight = Mathf.Max(0.05f, obstacleProbeHeight * 0.5f);
            float clearance = Mathf.Max(0f, obstacleClearance);

            Vector3 center = origin + new Vector3(widthWorld * 0.5f, obstacleProbeVerticalOffset, heightWorld * 0.5f);
            Vector3 halfExtents = new Vector3(widthWorld * 0.5f + clearance, halfProbeHeight, heightWorld * 0.5f + clearance);

            int colliderCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _dynamicObstacleBuffer,
                Quaternion.identity,
                obstacleLayers,
                obstacleQueryTriggerInteraction);

            if (colliderCount == _dynamicObstacleBuffer.Length)
            {
                Array.Resize(ref _dynamicObstacleBuffer, _dynamicObstacleBuffer.Length * 2);
                colliderCount = Physics.OverlapBoxNonAlloc(
                    center,
                    halfExtents,
                    _dynamicObstacleBuffer,
                    Quaternion.identity,
                    obstacleLayers,
                    obstacleQueryTriggerInteraction);
            }

            _dynamicObstacleColliders.Clear();
            for (int i = 0; i < colliderCount; i++)
            {
                Collider collider = _dynamicObstacleBuffer[i];
                if (collider != null)
                    _dynamicObstacleColliders.Add(collider);
            }

            _dynamicObstacleColliders.Sort(ColliderInstanceIdComparer.Instance);

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _dynamicObstacleColliders.Count;

                for (int i = 0; i < _dynamicObstacleColliders.Count; i++)
                {
                    Collider collider = _dynamicObstacleColliders[i];
                    Transform obstacleTransform = collider.transform;
                    Vector3 position = obstacleTransform.position;
                    Vector3 rotation = obstacleTransform.eulerAngles;

                    hash = hash * 31 + collider.GetInstanceID();
                    hash = hash * 31 + (collider.enabled ? 1 : 0);
                    hash = hash * 31 + (collider.gameObject.activeInHierarchy ? 1 : 0);
                    hash = hash * 31 + QuantizeForHash(position.x, DynamicHashPositionStep);
                    hash = hash * 31 + QuantizeForHash(position.y, DynamicHashPositionStep);
                    hash = hash * 31 + QuantizeForHash(position.z, DynamicHashPositionStep);
                    hash = hash * 31 + QuantizeForHash(rotation.x, DynamicHashRotationStep);
                    hash = hash * 31 + QuantizeForHash(rotation.y, DynamicHashRotationStep);
                    hash = hash * 31 + QuantizeForHash(rotation.z, DynamicHashRotationStep);
                }

                return hash;
            }
        }

        private bool IsCellWalkable(int x, int y, Vector3 origin, float cellSize, Vector3 halfExtents)
        {
            Vector3 probeCenter = GetCellCenterWorld(origin, cellSize, x, y);
            probeCenter.y += obstacleProbeVerticalOffset;

            bool blocked = Physics.CheckBox(
                probeCenter,
                halfExtents,
                Quaternion.identity,
                obstacleLayers,
                obstacleQueryTriggerInteraction);

            return !blocked;
        }

        private byte GetCellCost(
            int x,
            int y,
            Vector3 origin,
            float cellSize,
            byte fallbackCost,
            Vector3 halfExtents)
        {
            if (!enableCostLayers || costLayerRules == null || costLayerRules.Length == 0)
                return fallbackCost;

            Vector3 probeCenter = GetCellCenterWorld(origin, cellSize, x, y);
            probeCenter.y += obstacleProbeVerticalOffset;
            int resolvedCost = fallbackCost;

            for (int i = 0; i < costLayerRules.Length; i++)
            {
                CostLayerRule rule = costLayerRules[i];
                if (rule.layers.value == 0)
                    continue;

                int ruleCost = Mathf.Clamp(rule.cost, 1, 254);
                if (ruleCost <= resolvedCost)
                    continue;

                bool isInLayer = Physics.CheckBox(
                    probeCenter,
                    halfExtents,
                    Quaternion.identity,
                    rule.layers,
                    costLayerQueryTriggerInteraction);

                if (isInLayer)
                    resolvedCost = ruleCost;
            }

            return (byte)Mathf.Clamp(resolvedCost, 1, 254);
        }

        private float GetEffectiveRebuildInterval()
        {
            if (config == null)
                return Mathf.Max(0.05f, rebuildInterval);

            float interval = config.buildSettings.dynamicObstacleRefreshInterval > 0f
                ? config.buildSettings.dynamicObstacleRefreshInterval
                : rebuildInterval;
            return Mathf.Max(0.05f, interval);
        }

        private static int QuantizeForHash(float value, float step)
        {
            if (step <= 0f)
                return Mathf.RoundToInt(value * 1000f);

            return Mathf.RoundToInt(value / step);
        }

        private static Vector3 GetCellCenterWorld(Vector3 origin, float cellSize, int x, int y)
        {
            return new Vector3(
                origin.x + (x + 0.5f) * cellSize,
                origin.y,
                origin.z + (y + 0.5f) * cellSize);
        }

        private void OnDrawGizmos()
        {
            if (drawGizmosOnlyWhenSelected)
                return;

            DrawEditorGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmosOnlyWhenSelected)
                return;

            DrawEditorGizmos();
        }

        private Vector3 ResolveGridOrigin(FlowFieldRuntimeConfig runtimeConfig)
        {
            if (!centerGridOnHostTransform)
                return transform.position + originOffset;

            float widthWorld = runtimeConfig.width * runtimeConfig.cellSize;
            float heightWorld = runtimeConfig.height * runtimeConfig.cellSize;
            return transform.position + originOffset - new Vector3(widthWorld * 0.5f, 0f, heightWorld * 0.5f);
        }

        private void DrawEditorGizmos()
        {
            if (!drawEditorGizmos || config == null)
                return;

            FlowFieldData field = CurrentField;

            int width = field != null ? field.Width : Mathf.Max(1, config.width);
            int height = field != null ? field.Height : Mathf.Max(1, config.height);
            float cellSize = field != null ? field.CellSize : Mathf.Max(0.01f, config.cellSize);
            Vector3 origin = field != null ? field.Origin : ResolveGridOrigin(config);
            float yOffset = Mathf.Max(0f, gizmoYOffset);

            DrawGridShape(origin, width, height, cellSize, yOffset);

            if (field != null)
            {
                FlowFieldDebugGizmoDrawer.Draw(
                    field,
                    drawBlockedCells,
                    drawFlowVectors,
                    yOffset,
                    Mathf.Clamp01(gizmoVectorScale));

                if (drawTargetCell && field.HasValidTarget && field.IsInBounds(field.TargetCell.x, field.TargetCell.y))
                {
                    Vector3 targetCenter = field.GetCellCenterWorld(field.TargetCell.x, field.TargetCell.y);
                    targetCenter.y += yOffset;

                    float targetSize = Mathf.Max(0.05f, field.CellSize * 0.7f);
                    Gizmos.color = targetCellColor;
                    Gizmos.DrawWireCube(targetCenter, new Vector3(targetSize, 0.02f, targetSize));
                }
            }
        }

        private void DrawGridShape(Vector3 origin, int width, int height, float cellSize, float yOffset)
        {
            if (width <= 0 || height <= 0 || cellSize <= 0f)
                return;

            float widthWorld = width * cellSize;
            float heightWorld = height * cellSize;
            Vector3 min = origin + Vector3.up * yOffset;

            if (drawGridBounds)
            {
                Gizmos.color = gridBoundsColor;
                Vector3 center = min + new Vector3(widthWorld * 0.5f, 0f, heightWorld * 0.5f);
                Gizmos.DrawWireCube(center, new Vector3(widthWorld, 0.02f, heightWorld));
            }

            if (!drawGridLines)
                return;

            Gizmos.color = gridLineColor;

            for (int x = 0; x <= width; x++)
            {
                float lineX = min.x + x * cellSize;
                Vector3 start = new Vector3(lineX, min.y, min.z);
                Vector3 end = new Vector3(lineX, min.y, min.z + heightWorld);
                Gizmos.DrawLine(start, end);
            }

            for (int y = 0; y <= height; y++)
            {
                float lineZ = min.z + y * cellSize;
                Vector3 start = new Vector3(min.x, min.y, lineZ);
                Vector3 end = new Vector3(min.x + widthWorld, min.y, lineZ);
                Gizmos.DrawLine(start, end);
            }
        }

        private void OnValidate()
        {
            if (costLayerRules == null)
                costLayerRules = Array.Empty<CostLayerRule>();
        }
    }
}
