using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Optional runtime wall placement helper.
    /// Dynamically generates manual spots along wall colliders when the cursor is near,
    /// then registers the managed EnemyMassesWallPlacementZone with RTS only while active.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Placement/Dynamic Wall Placement Runtime")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public class EnemyMassesDynamicWallPlacementRuntime : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Runtime wall placement zone managed by this component. If null, one is created automatically when needed.")]
        [SerializeField] private EnemyMassesWallPlacementZone runtimeZone;

        [Tooltip("RTS controller used to register/unregister the managed runtime zone.")]
        [SerializeField] private EnemyMassesRTSController rtsController;

        [Tooltip("Wall BoxColliders used to build dynamic slots. If empty and auto-collect is enabled, child BoxColliders are used.")]
        [SerializeField] private List<BoxCollider> wallSegments = new List<BoxCollider>();

        [Header("Cursor Detection")]
        [Tooltip("Layer mask used for cursor raycasts (usually ground/walkable layers).")]
        [SerializeField] private LayerMask cursorSurfaceMask = ~0;

        [Tooltip("Maximum cursor raycast distance.")]
        [SerializeField] [Min(1f)] private float cursorRayDistance = 1500f;

        [Tooltip("Cursor must be within this distance from the wall path to show dynamic slots.")]
        [SerializeField] [Min(0.1f)] private float activationDistance = 3f;

        [Tooltip("Automatically collect child BoxColliders into wallSegments if the list is empty.")]
        [SerializeField] private bool autoCollectChildWallSegments = true;

        [Tooltip("Include inactive child objects when auto-collecting wall segments.")]
        [SerializeField] private bool includeInactiveChildren = false;

        [Header("Slot Generation")]
        [Tooltip("Spacing between generated slots along the wall path.")]
        [SerializeField] [Min(0.25f)] private float slotSpacing = 1.5f;

        [Tooltip("Maximum generated slot count.")]
        [SerializeField] [Min(1)] private int maxSlots = 96;

        [Tooltip("Offset generated slots away from the wall path, toward the cursor side.")]
        [SerializeField] [Min(0f)] private float slotOffsetFromWall = 1f;

        [Tooltip("Vertical offset added to each generated slot.")]
        [SerializeField] private float slotHeightOffset = 0.1f;

        [Tooltip("Trim sampled positions at the start/end of the wall path to reduce seam crowding near corners.")]
        [SerializeField] [Min(0f)] private float slotEdgePadding = 0.15f;

        [Tooltip("Minimum allowed distance between generated slots, as a fraction of slot spacing.")]
        [SerializeField] [Range(0.1f, 1f)] private float minSlotSeparationFactor = 0.85f;

        [Tooltip("Extra clearance from wall geometry (in addition to half wall thickness). Prevents slots inside corner walls.")]
        [SerializeField] [Min(0f)] private float slotWallClearance = 0.2f;

        [Tooltip("Iterations for wall/corner penetration resolution when adjusting generated slots.")]
        [SerializeField] [Range(1, 8)] private int wallClearanceIterations = 4;

        [Tooltip("Maximum endpoint distance used to connect wall segments at corners (supports L-shapes).")]
        [SerializeField] [Min(0.01f)] private float cornerJoinDistance = 0.75f;

        [Tooltip("Minimum cursor movement before rebuilding the active slot preview.")]
        [SerializeField] [Min(0f)] private float rebuildMoveThreshold = 0.2f;

        [Tooltip("Minimum time between slot rebuilds while active.")]
        [SerializeField] [Min(0.02f)] private float rebuildInterval = 0.1f;

        [Tooltip("Allow slot preview rebuilds even while some spots are occupied. Occupied agents keep their positions.")]
        [SerializeField] private bool allowRebuildWhileOccupied = true;

        [Tooltip("Skip slot generation in gaps between disconnected wall segments. Only areas backed by actual wall geometry produce slots.")]
        [SerializeField] private bool skipGapsBetweenWalls = true;

        [Header("Runtime Behavior")]
        [Tooltip("Show runtime spot markers while the dynamic preview is active.")]
        [SerializeField] private bool showRuntimeSpotMarkers = true;

        [Tooltip("Keep the managed zone registered to RTS when preview is inactive.")]
        [SerializeField] private bool keepZoneRegisteredWhenInactive = false;

        [Tooltip("Clear all zone assignments whenever preview deactivates.")]
        [SerializeField] private bool clearAssignmentsOnDeactivate = false;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = false;

        private struct WallSegment
        {
            public Vector3 a;
            public Vector3 b;
            public float halfThickness;
            public BoxCollider sourceCollider;
        }

        private readonly List<WallSegment> _segmentCache = new List<WallSegment>();
        private readonly List<Vector3> _orderedPolyline = new List<Vector3>();
        private readonly List<float> _polylineLengths = new List<float>();
        private readonly List<Transform> _dynamicSpotTransforms = new List<Transform>();
        private readonly List<Vector3> _resolvedSlotPositions = new List<Vector3>();
        private readonly List<Vector3> _resolvedSlotNormals = new List<Vector3>();
        private readonly List<Vector3> _resolvedSlotTangents = new List<Vector3>();
        private readonly HashSet<int> _polylineGapIndices = new HashSet<int>();

        private Camera _camera;
        private bool _previewActive;
        private bool _zoneRegistered;
        private float _nextRebuildTime;
        private Vector3 _lastCursorWorldPoint;
        private bool _hasLastCursorPoint;

        /// <summary>Runtime-managed zone created by this component (if auto-created).</summary>
        public EnemyMassesWallPlacementZone RuntimeZone => runtimeZone;
        public bool PreviewActive => _previewActive;
        public bool ZoneRegisteredToRTS => _zoneRegistered;
        public int ConfiguredWallSegmentCount => wallSegments?.Count ?? 0;
        public int ActiveWallSegmentCount => _segmentCache.Count;

        private void Awake()
        {
            if (autoCollectChildWallSegments && wallSegments.Count == 0)
            {
                CollectWallSegmentsFromChildren();
            }

            if (runtimeZone != null)
            {
                PrepareRuntimeZone(runtimeZone);
            }
        }

        private void Start()
        {
            ResolveRTSController();

            if (runtimeZone != null && !keepZoneRegisteredWhenInactive)
            {
                // If RTS auto-discovered this zone, force it back out until preview becomes active.
                rtsController?.UnregisterWallPlacementZone(runtimeZone);
                _zoneRegistered = false;
            }
        }

        private void OnDisable()
        {
            SetPreviewActive(false, true);
        }

        private void OnDestroy()
        {
            SetPreviewActive(false, true);
        }

        private void Update()
        {
            if (!TryGetCursorWorldPoint(out Vector3 cursorWorldPoint))
            {
                SetPreviewActive(false, false);
                return;
            }

            if (!BuildWallSegments())
            {
                SetPreviewActive(false, false);
                return;
            }

            float nearestDist = GetNearestDistanceToWall(cursorWorldPoint, out _);
            if (nearestDist > activationDistance)
            {
                SetPreviewActive(false, false);
                return;
            }

            if (runtimeZone == null)
            {
                CreateRuntimeZone();
            }

            if (runtimeZone == null)
            {
                SetPreviewActive(false, false);
                return;
            }

            // Lock rebuilds while any assigned agents are still pathfinding to their spots.
            bool agentsInTransit = runtimeZone.HasAgentsInTransit;
            bool canRebuild = !agentsInTransit && (runtimeZone.OccupiedSpots == 0 || allowRebuildWhileOccupied);
            if (canRebuild && ShouldRebuildPreview(cursorWorldPoint))
            {
                RebuildRuntimeSpots(cursorWorldPoint);
                _lastCursorWorldPoint = cursorWorldPoint;
                _hasLastCursorPoint = true;
                _nextRebuildTime = Time.time + rebuildInterval;
            }

            SetPreviewActive(true, false);
        }

        /// <summary>
        /// Auto-collects child BoxColliders into the wall segment list.
        /// </summary>
        public void CollectWallSegmentsFromChildren()
        {
            wallSegments.Clear();
            BoxCollider[] colliders = GetComponentsInChildren<BoxCollider>(includeInactiveChildren);

            foreach (var collider in colliders)
            {
                if (collider == null)
                    continue;

                if (runtimeZone != null && collider.transform.IsChildOf(runtimeZone.transform))
                    continue;

                wallSegments.Add(collider);
            }
        }

        /// <summary>
        /// Removes null entries from the wall segment list.
        /// </summary>
        public int RemoveNullWallSegments()
        {
            if (wallSegments == null || wallSegments.Count == 0)
                return 0;

            int removed = 0;
            for (int i = wallSegments.Count - 1; i >= 0; i--)
            {
                if (wallSegments[i] != null)
                    continue;

                wallSegments.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Ensures a runtime zone exists and returns it.
        /// </summary>
        public EnemyMassesWallPlacementZone EnsureRuntimeZone()
        {
            if (runtimeZone == null)
            {
                CreateRuntimeZone();
            }

            return runtimeZone;
        }

        /// <summary>
        /// Forces a one-shot preview refresh from the current cursor position.
        /// Useful for custom editor/runtime utility buttons.
        /// </summary>
        public void ForceRefreshPreview()
        {
            if (!BuildWallSegments())
            {
                SetPreviewActive(false, false);
                return;
            }

            if (!TryGetCursorWorldPoint(out Vector3 cursorWorldPoint))
            {
                SetPreviewActive(false, false);
                return;
            }

            EnsureRuntimeZone();
            if (runtimeZone == null)
            {
                SetPreviewActive(false, false);
                return;
            }

            if (!runtimeZone.HasAgentsInTransit && (runtimeZone.OccupiedSpots == 0 || allowRebuildWhileOccupied))
            {
                RebuildRuntimeSpots(cursorWorldPoint);
            }

            _lastCursorWorldPoint = cursorWorldPoint;
            _hasLastCursorPoint = true;
            _nextRebuildTime = Time.time + rebuildInterval;

            SetPreviewActive(true, false);
        }

        /// <summary>
        /// Forces preview deactivation and unregisters the zone from RTS.
        /// </summary>
        public void ForceDeactivatePreview(bool clearAssignments = false)
        {
            if (clearAssignments && runtimeZone != null)
            {
                runtimeZone.ClearAllAssignments();
            }

            SetPreviewActive(false, true);
        }

        private bool ShouldRebuildPreview(Vector3 cursorWorldPoint)
        {
            if (!_previewActive)
                return true;

            if (!_hasLastCursorPoint)
                return true;

            if (Time.time >= _nextRebuildTime)
                return true;

            float moveThresholdSqr = rebuildMoveThreshold * rebuildMoveThreshold;
            return (cursorWorldPoint - _lastCursorWorldPoint).sqrMagnitude >= moveThresholdSqr;
        }

        private void SetPreviewActive(bool active, bool forceUnregister)
        {
            if (runtimeZone == null)
            {
                _previewActive = false;
                _hasLastCursorPoint = false;
                _zoneRegistered = false;
                return;
            }

            bool wasActive = _previewActive;
            _previewActive = active;

            if (active)
            {
                if (showRuntimeSpotMarkers)
                {
                    runtimeZone.SetRuntimeSpotMarkersVisible(true);
                }

                RegisterZoneToRTS();
                return;
            }

            if (wasActive)
            {
                _hasLastCursorPoint = false;

                if (showRuntimeSpotMarkers)
                {
                    runtimeZone.SetRuntimeSpotMarkersVisible(false);
                }

                if (clearAssignmentsOnDeactivate)
                {
                    runtimeZone.ClearAllAssignments();
                }
            }

            if (forceUnregister || !keepZoneRegisteredWhenInactive)
            {
                UnregisterZoneFromRTS();
            }
        }

        private void RegisterZoneToRTS()
        {
            if (_zoneRegistered || runtimeZone == null)
                return;

            if (runtimeZone.CrowdController == null)
            {
                // Wait until the zone Start() resolves the crowd controller.
                return;
            }

            var controller = ResolveRTSController();
            if (controller == null)
                return;

            controller.RegisterWallPlacementZone(runtimeZone);
            _zoneRegistered = true;

            if (enableDebugLogging)
            {
                Debug.Log($"[DynamicWallPlacement] Registered runtime zone '{runtimeZone.name}'");
            }
        }

        private void UnregisterZoneFromRTS()
        {
            if (!_zoneRegistered || runtimeZone == null)
                return;

            var controller = ResolveRTSController();
            if (controller != null)
            {
                controller.UnregisterWallPlacementZone(runtimeZone);
            }

            _zoneRegistered = false;

            if (enableDebugLogging)
            {
                Debug.Log($"[DynamicWallPlacement] Unregistered runtime zone '{runtimeZone.name}'");
            }
        }

        private EnemyMassesRTSController ResolveRTSController()
        {
            if (rtsController != null)
                return rtsController;

#if UNITY_2023_1_OR_NEWER
            rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
#else
            rtsController = FindObjectOfType<EnemyMassesRTSController>();
#endif

            return rtsController;
        }

        private void CreateRuntimeZone()
        {
            var zoneGo = new GameObject("DynamicWallPlacementZone");
            zoneGo.transform.SetParent(transform, false);

            runtimeZone = zoneGo.AddComponent<EnemyMassesWallPlacementZone>();
            PrepareRuntimeZone(runtimeZone);

            if (enableDebugLogging)
            {
                Debug.Log($"[DynamicWallPlacement] Created runtime zone on '{name}'");
            }
        }

        private void PrepareRuntimeZone(EnemyMassesWallPlacementZone zone)
        {
            if (zone == null)
                return;

            zone.ConfigureAsRuntimeManualZone(false);
            zone.SetRuntimeSpotMarkersVisible(false);
        }

        private bool TryGetCursorWorldPoint(out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;

            if (!TryResolveCamera(out Camera cam))
                return false;

            if (!TryGetMouseScreenPosition(out Vector2 mousePos))
                return false;

            Ray ray = cam.ScreenPointToRay(mousePos);
            int mask = cursorSurfaceMask.value == 0 ? ~0 : cursorSurfaceMask.value;

            if (Physics.Raycast(ray, out RaycastHit hit, cursorRayDistance, mask, QueryTriggerInteraction.Ignore))
            {
                worldPoint = hit.point;
                return true;
            }

            // Fallback plane so preview can still work in sparse test scenes.
            Plane fallbackPlane = new Plane(Vector3.up, transform.position);
            if (fallbackPlane.Raycast(ray, out float enter))
            {
                worldPoint = ray.GetPoint(enter);
                return true;
            }

            return false;
        }

        private bool TryResolveCamera(out Camera cam)
        {
            cam = _camera;

            if (rtsController != null && rtsController.targetCamera != null)
            {
                _camera = rtsController.targetCamera;
                cam = _camera;
                return true;
            }

            if (cam != null)
                return true;

            _camera = Camera.main;
            cam = _camera;
            return cam != null;
        }

        private bool TryGetMouseScreenPosition(out Vector2 mousePos)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                mousePos = Mouse.current.position.ReadValue();
                return true;
            }
#endif
            mousePos = Input.mousePosition;
            return true;
        }

        private bool BuildWallSegments()
        {
            _segmentCache.Clear();

            if (autoCollectChildWallSegments && wallSegments.Count == 0)
            {
                CollectWallSegmentsFromChildren();
            }

            for (int i = 0; i < wallSegments.Count; i++)
            {
                var box = wallSegments[i];
                if (box == null || !box.enabled || !box.gameObject.activeInHierarchy)
                    continue;

                if (TryExtractSegment(box, out WallSegment segment))
                {
                    _segmentCache.Add(segment);
                }
            }

            return _segmentCache.Count > 0;
        }

        private bool TryExtractSegment(BoxCollider box, out WallSegment segment)
        {
            segment = default;

            Vector3 worldCenter = box.transform.TransformPoint(box.center);
            Vector3 scaledSize = Vector3.Scale(box.size, Abs(box.transform.lossyScale));

            Vector3 xAxis = Vector3.ProjectOnPlane(box.transform.right, Vector3.up);
            Vector3 zAxis = Vector3.ProjectOnPlane(box.transform.forward, Vector3.up);

            float xLength = xAxis.magnitude * Mathf.Abs(scaledSize.x);
            float zLength = zAxis.magnitude * Mathf.Abs(scaledSize.z);

            Vector3 direction;
            float length;
            float thickness;

            if (xLength >= zLength)
            {
                direction = xAxis;
                length = xLength;
                thickness = zLength;
            }
            else
            {
                direction = zAxis;
                length = zLength;
                thickness = xLength;
            }

            if (direction.sqrMagnitude <= 0.0001f || length <= 0.05f)
                return false;

            direction.Normalize();
            float halfLength = length * 0.5f;

            segment.a = worldCenter - direction * halfLength;
            segment.b = worldCenter + direction * halfLength;
            segment.halfThickness = Mathf.Max(0.01f, thickness * 0.5f);
            segment.sourceCollider = box;
            return true;
        }

        private float GetNearestDistanceToWall(Vector3 worldPoint, out Vector3 nearestPoint)
        {
            nearestPoint = Vector3.zero;
            float bestDistSqr = float.MaxValue;

            for (int i = 0; i < _segmentCache.Count; i++)
            {
                var segment = _segmentCache[i];
                Vector3 closest = ClosestPointOnSegmentXZ(segment.a, segment.b, worldPoint);
                float distSqr = DistanceSqrXZ(closest, worldPoint);

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    nearestPoint = closest;
                }
            }

            return Mathf.Sqrt(bestDistSqr);
        }

        private void RebuildRuntimeSpots(Vector3 cursorWorldPoint)
        {
            if (runtimeZone == null)
                return;

            // Capture occupied agent assignments so they can be silently restored after rebuild.
            Dictionary<int, Vector3> occupiedAgents = null;
            if (runtimeZone.OccupiedSpots > 0)
            {
                occupiedAgents = new Dictionary<int, Vector3>();
                foreach (var spot in runtimeZone.Spots)
                {
                    if (spot.isOccupied)
                    {
                        occupiedAgents[spot.assignedAgentIndex] = spot.worldPosition;
                    }
                }
            }

            BuildOrderedPolyline(cursorWorldPoint);
            BuildPolylineLengthCache();

            float totalLength = _polylineLengths.Count > 0 ? _polylineLengths[_polylineLengths.Count - 1] : 0f;
            if (totalLength <= 0.001f)
            {
                EnsureSpotTransformPool(0);
                runtimeZone.RebuildManualSpotsFromChildren(false);
                RestoreOccupiedAgents(occupiedAgents);
                return;
            }

            float spacing = Mathf.Max(0.25f, slotSpacing);

            _resolvedSlotPositions.Clear();
            _resolvedSlotNormals.Clear();
            _resolvedSlotTangents.Clear();
            float minSeparation = Mathf.Max(0.05f, spacing * minSlotSeparationFactor);

            // Compute a single global side sign from the cursor position relative to the
            // nearest polyline point. The polyline has consistent winding (Cross(Up, tangent)
            // always points to the same logical side), so the sign transfers correctly
            // across perpendicular segments in L-shaped walls.
            float globalSideSign = GetPolylineSideSign(cursorWorldPoint);

            if (skipGapsBetweenWalls && _polylineGapIndices.Count > 0)
            {
                // Per-chain sampling: generate slots only along contiguous wall sections.
                GenerateSlotsPerChain(spacing, minSeparation, globalSideSign);
            }
            else
            {
                // Uniform sampling along entire polyline (original behavior).
                float edgePadding = Mathf.Clamp(slotEdgePadding, 0f, totalLength * 0.5f);
                float sampleStart = edgePadding;
                float sampleEnd = Mathf.Max(sampleStart, totalLength - edgePadding);
                float usableLength = Mathf.Max(0f, sampleEnd - sampleStart);

                int baseSlotCount = Mathf.Clamp(Mathf.FloorToInt(usableLength / spacing) + 1, 1, maxSlots);
                float lastBaseDistance = sampleStart + (baseSlotCount - 1) * spacing;
                bool addEndCap = baseSlotCount < maxSlots && (sampleEnd - lastBaseDistance) > spacing * 0.35f;

                GenerateSlotsForSide(baseSlotCount, sampleStart, sampleEnd, spacing, minSeparation, addEndCap, globalSideSign);
            }

            int targetSlotCount = Mathf.Min(_resolvedSlotPositions.Count, maxSlots);
            EnsureSpotTransformPool(targetSlotCount);

            for (int i = 0; i < targetSlotCount; i++)
            {
                SetSpotTransformPosition(i, _resolvedSlotPositions[i]);
            }

            runtimeZone.RebuildManualSpotsFromChildren(false);

            // Set per-spot wall directions so the zone can compute correct agent facing.
            for (int i = 0; i < targetSlotCount && i < _resolvedSlotNormals.Count; i++)
            {
                runtimeZone.SetSpotWallDirections(i, _resolvedSlotNormals[i], _resolvedSlotTangents[i]);
            }

            // Silently restore agents that were at spots before the rebuild.
            RestoreOccupiedAgents(occupiedAgents, globalSideSign);

            if (enableDebugLogging)
            {
                Debug.Log($"[DynamicWallPlacement] Rebuilt {targetSlotCount} runtime slots (gaps={_polylineGapIndices.Count}, wall length {totalLength:F2})");
            }
        }

        private void GenerateSlotsForSide(int baseSlotCount, float sampleStart, float sampleEnd, float spacing, float minSeparation, bool addEndCap, float sideSign)
        {
            for (int i = 0; i < baseSlotCount; i++)
            {
                float sampleDistance = Mathf.Min(sampleStart + i * spacing, sampleEnd);
                if (TryGetSlotPositionFromDistance(sampleDistance, sideSign, out Vector3 slotPosition, out Vector3 slotNormal, out Vector3 slotTangent))
                {
                    TryAddSlotPositionNonOverlapping(slotPosition, minSeparation, slotNormal, slotTangent);
                }
            }

            if (addEndCap)
            {
                if (TryGetSlotPositionFromDistance(sampleEnd, sideSign, out Vector3 endCapPosition, out Vector3 endCapNormal, out Vector3 endCapTangent))
                {
                    TryAddSlotPositionNonOverlapping(endCapPosition, minSeparation, endCapNormal, endCapTangent);
                }
            }
        }

        /// <summary>
        /// Generates slots per contiguous wall chain, skipping gap segments between
        /// disconnected wall groups. Each chain gets its own edge padding and slot count.
        /// </summary>
        private void GenerateSlotsPerChain(float spacing, float minSeparation, float sideSign)
        {
            if (_polylineLengths.Count < 2)
                return;

            int pointCount = _orderedPolyline.Count;
            int chainStartIdx = 0;

            for (int i = 0; i < pointCount - 1; i++)
            {
                if (_polylineGapIndices.Contains(i))
                {
                    // Chain ends at point i (start of the gap).
                    if (i > chainStartIdx)
                    {
                        GenerateChainSlots(chainStartIdx, i, spacing, minSeparation, sideSign);
                    }

                    // Next chain starts at point i+1 (end of the gap).
                    chainStartIdx = i + 1;
                }
            }

            // Final chain from chainStartIdx to the end.
            if (chainStartIdx < pointCount - 1)
            {
                GenerateChainSlots(chainStartIdx, pointCount - 1, spacing, minSeparation, sideSign);
            }
        }

        private void GenerateChainSlots(int startIdx, int endIdx, float spacing, float minSeparation, float sideSign)
        {
            float chainStart = _polylineLengths[startIdx];
            float chainEnd = _polylineLengths[endIdx];
            float chainLength = chainEnd - chainStart;

            if (chainLength <= 0.001f)
                return;

            int remaining = maxSlots - _resolvedSlotPositions.Count;
            if (remaining <= 0)
                return;

            float chainEdgePadding = Mathf.Clamp(slotEdgePadding, 0f, chainLength * 0.5f);
            float sampleStart = chainStart + chainEdgePadding;
            float sampleEnd = Mathf.Max(sampleStart, chainEnd - chainEdgePadding);
            float usableLength = Mathf.Max(0f, sampleEnd - sampleStart);

            int slotCount = Mathf.Clamp(Mathf.FloorToInt(usableLength / spacing) + 1, 1, remaining);
            float lastDist = sampleStart + (slotCount - 1) * spacing;
            bool endCap = _resolvedSlotPositions.Count + slotCount < maxSlots &&
                          (sampleEnd - lastDist) > spacing * 0.35f;

            GenerateSlotsForSide(slotCount, sampleStart, sampleEnd, spacing, minSeparation, endCap, sideSign);
        }

        private void RestoreOccupiedAgents(Dictionary<int, Vector3> occupiedAgents, float currentSideSign = 1f)
        {
            if (occupiedAgents == null || occupiedAgents.Count == 0 || runtimeZone == null)
                return;

            foreach (var kvp in occupiedAgents)
            {
                int agentIndex = kvp.Key;
                Vector3 previousPosition = kvp.Value;

                // Only restore agents that were on the same side of the wall as the
                // current cursor. When the cursor crosses to the other side, generating
                // new slots there, agents on the old side should NOT be silently
                // reassigned — the new slots must appear as available (green).
                float agentSideSign = GetPolylineSideSign(previousPosition);
                if (agentSideSign != currentSideSign)
                    continue;

                var nearestSpot = runtimeZone.FindNearestAvailableSpot(previousPosition);
                if (nearestSpot == null)
                    continue;

                runtimeZone.ReassignAgentToSpotSilent(agentIndex, nearestSpot);
            }
        }

        private void EnsureSpotTransformPool(int requiredCount)
        {
            if (runtimeZone == null)
                return;

            while (_dynamicSpotTransforms.Count < requiredCount)
            {
                int index = _dynamicSpotTransforms.Count;
                var spotGo = new GameObject($"DynamicSpot_{index:000}");
                spotGo.transform.SetParent(runtimeZone.transform, false);
                _dynamicSpotTransforms.Add(spotGo.transform);
            }

            for (int i = 0; i < _dynamicSpotTransforms.Count; i++)
            {
                bool shouldBeActive = i < requiredCount;
                if (_dynamicSpotTransforms[i].gameObject.activeSelf != shouldBeActive)
                {
                    _dynamicSpotTransforms[i].gameObject.SetActive(shouldBeActive);
                }
            }
        }

        private bool TryGetSlotPositionFromDistance(float sampleDistance, float sideSign, out Vector3 finalPoint, out Vector3 outNormal, out Vector3 outTangent)
        {
            finalPoint = Vector3.zero;
            outNormal = Vector3.zero;
            outTangent = Vector3.zero;

            if (!TrySamplePolyline(sampleDistance, out Vector3 samplePoint, out Vector3 tangent))
                return false;

            tangent.y = 0f;
            if (tangent.sqrMagnitude <= 0.0001f)
            {
                tangent = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;
                tangent.y = 0f;
            }

            tangent.Normalize();
            Vector3 normal = Vector3.Cross(Vector3.up, tangent);
            if (normal.sqrMagnitude <= 0.0001f)
                return false;
            
            normal.Normalize();

            // sideSign is determined globally from the nearest polyline point to the cursor.
            // Because the polyline has consistent winding, Cross(Up, tangent) always points
            // to the same logical side, so the sign is valid for all segments including
            // perpendicular ones in L-shaped walls.

            // Use a reference point on the same side as the generated slot for wall geometry resolve.
            Vector3 referencePoint = samplePoint + normal * sideSign;

            finalPoint = samplePoint + normal * (slotOffsetFromWall * sideSign);
            finalPoint.y += slotHeightOffset;

            if (!ResolveSlotAgainstWallGeometry(ref finalPoint, referencePoint))
                return false;

            // Near L-shaped corners, wall clearance resolution can push a slot through
            // the adjacent wall. The single-segment normal check is insufficient because
            // the slot stays on the correct side of ITS OWN wall but ends up on the wrong
            // side of the ADJACENT wall. Validate against all nearby polyline segments.
            if (!IsSlotOnCorrectSideOfPolyline(finalPoint, sideSign))
                return false;

            // Output the wall-relative directions for agent facing.
            // wallNormal points away from the wall (toward the agent side).
            outNormal = normal * sideSign;
            outTangent = tangent;

            return true;
        }

        /// <summary>
        /// Checks that the slot position is on the correct side (determined by sideSign)
        /// of every nearby polyline segment. This catches slots at L-shaped corners that
        /// get pushed through the adjacent wall by clearance resolution.
        /// </summary>
        private bool IsSlotOnCorrectSideOfPolyline(Vector3 slotPosition, float sideSign)
        {
            float maxCheckDist = slotOffsetFromWall + slotWallClearance + 1f;
            float maxCheckDistSqr = maxCheckDist * maxCheckDist;

            for (int i = 0; i < _orderedPolyline.Count - 1; i++)
            {
                // Skip gap segments — they have no physical wall and an arbitrary direction.
                if (skipGapsBetweenWalls && _polylineGapIndices.Contains(i))
                    continue;

                Vector3 a = _orderedPolyline[i];
                Vector3 b = _orderedPolyline[i + 1];

                Vector3 segTangent = b - a;
                segTangent.y = 0f;
                if (segTangent.sqrMagnitude <= 0.0001f)
                    continue;

                Vector3 closestOnSeg = ClosestPointOnSegmentXZ(a, b, slotPosition);
                float distSqr = DistanceSqrXZ(closestOnSeg, slotPosition);
                if (distSqr > maxCheckDistSqr)
                    continue;

                segTangent.Normalize();
                Vector3 segNormal = Vector3.Cross(Vector3.up, segTangent);
                if (segNormal.sqrMagnitude <= 0.0001f)
                    continue;
                segNormal.Normalize();

                Vector3 toSlot = slotPosition - closestOnSeg;
                toSlot.y = 0f;

                // If the slot is on the wrong side of this segment, reject it.
                // Small negative tolerance to avoid rejecting slots exactly on the wall line.
                if (Vector3.Dot(toSlot, segNormal) * sideSign < -0.01f)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines a global side sign for slot placement by finding the nearest point
        /// on the ordered polyline to the cursor and checking which side the cursor is on.
        /// Because the polyline has a consistent winding direction, Cross(Up, tangent)
        /// always points to the same logical side, so the sign transfers correctly across
        /// perpendicular segments in L-shaped walls.
        /// </summary>
        private float GetPolylineSideSign(Vector3 cursorWorldPoint)
        {
            if (_orderedPolyline.Count < 2)
                return 1f;

            float bestDistSqr = float.MaxValue;
            Vector3 bestPoint = _orderedPolyline[0];
            Vector3 bestTangent = Vector3.forward;

            for (int i = 0; i < _orderedPolyline.Count - 1; i++)
            {
                // Skip gap segments — their direction is arbitrary and would corrupt the side sign.
                if (skipGapsBetweenWalls && _polylineGapIndices.Contains(i))
                    continue;

                Vector3 a = _orderedPolyline[i];
                Vector3 b = _orderedPolyline[i + 1];
                Vector3 closest = ClosestPointOnSegmentXZ(a, b, cursorWorldPoint);
                float distSqr = DistanceSqrXZ(closest, cursorWorldPoint);

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    bestPoint = closest;
                    bestTangent = b - a;
                }
            }

            bestTangent.y = 0f;
            if (bestTangent.sqrMagnitude <= 0.0001f)
                return 1f;
            bestTangent.Normalize();

            Vector3 normal = Vector3.Cross(Vector3.up, bestTangent);
            if (normal.sqrMagnitude <= 0.0001f)
                return 1f;
            normal.Normalize();

            Vector3 toCursor = cursorWorldPoint - bestPoint;
            toCursor.y = 0f;
            return Vector3.Dot(toCursor, normal) >= 0f ? 1f : -1f;
        }

        private bool ResolveSlotAgainstWallGeometry(ref Vector3 slotPosition, Vector3 cursorWorldPoint)
        {
            if (_segmentCache.Count == 0)
                return true;

            int iterations = Mathf.Clamp(wallClearanceIterations, 1, 8);
            float clearance = Mathf.Max(0f, slotWallClearance);
            const float EPS = 0.0001f;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool adjustedAny = false;

                for (int i = 0; i < _segmentCache.Count; i++)
                {
                    var segment = _segmentCache[i];
                    Vector3 closest = ClosestPointOnSegmentXZ(segment.a, segment.b, slotPosition);
                    Vector3 away = slotPosition - closest;
                    away.y = 0f;

                    float distance = away.magnitude;
                    float requiredDistance = Mathf.Max(0.01f, segment.halfThickness + clearance);
                    float penetration = requiredDistance - distance;
                    if (penetration <= 0f)
                        continue;

                    Vector3 pushDirection;
                    if (distance > EPS)
                    {
                        pushDirection = away / distance;
                    }
                    else
                    {
                        Vector3 tangent = segment.b - segment.a;
                        tangent.y = 0f;
                        if (tangent.sqrMagnitude <= EPS)
                            tangent = Vector3.right;

                        tangent.Normalize();
                        Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
                        Vector3 toCursor = cursorWorldPoint - closest;
                        toCursor.y = 0f;
                        float sign = Vector3.Dot(toCursor, normal) >= 0f ? 1f : -1f;
                        pushDirection = normal * sign;
                    }

                    // Tiny epsilon prevents tiny re-penetration due precision.
                    slotPosition += pushDirection * (penetration + 0.001f);
                    adjustedAny = true;
                }

                if (!adjustedAny)
                    break;
            }

            // Final validation: reject if still intruding any wall clearance envelope.
            for (int i = 0; i < _segmentCache.Count; i++)
            {
                var segment = _segmentCache[i];
                Vector3 closest = ClosestPointOnSegmentXZ(segment.a, segment.b, slotPosition);
                float distance = Mathf.Sqrt(DistanceSqrXZ(slotPosition, closest));
                float requiredDistance = Mathf.Max(0.01f, segment.halfThickness + clearance);
                if (distance + 0.001f < requiredDistance)
                    return false;
            }

            // Safety: skip slots that still end up inside actual wall colliders.
            if (IsInsideAnyWallCollider(slotPosition))
                return false;

            return true;
        }

        private bool IsInsideAnyWallCollider(Vector3 worldPoint)
        {
            for (int i = 0; i < _segmentCache.Count; i++)
            {
                BoxCollider box = _segmentCache[i].sourceCollider;
                if (box == null || !box.enabled || !box.gameObject.activeInHierarchy)
                    continue;

                Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
                Vector3 half = box.size * 0.5f;

                if (Mathf.Abs(local.x) <= half.x + 0.001f &&
                    Mathf.Abs(local.y) <= half.y + 0.001f &&
                    Mathf.Abs(local.z) <= half.z + 0.001f)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetSpotTransformPosition(int index, Vector3 worldPosition)
        {
            if (index < 0 || index >= _dynamicSpotTransforms.Count)
                return;

            Transform spot = _dynamicSpotTransforms[index];
            spot.position = worldPosition;
            spot.rotation = Quaternion.identity;
        }

        private void TryAddSlotPositionNonOverlapping(Vector3 candidate, float minSeparation, Vector3 normal, Vector3 tangent)
        {
            float minSeparationSqr = minSeparation * minSeparation;
            for (int i = 0; i < _resolvedSlotPositions.Count; i++)
            {
                if (DistanceSqrXZ(candidate, _resolvedSlotPositions[i]) < minSeparationSqr)
                {
                    return;
                }
            }

            _resolvedSlotPositions.Add(candidate);
            _resolvedSlotNormals.Add(normal);
            _resolvedSlotTangents.Add(tangent);
        }

        private void BuildOrderedPolyline(Vector3 cursorWorldPoint)
        {
            _orderedPolyline.Clear();
            _polylineGapIndices.Clear();

            if (_segmentCache.Count == 0)
                return;

            bool[] used = new bool[_segmentCache.Count];

            int startSegmentIndex = FindClosestSegmentToPoint(cursorWorldPoint, used);
            if (startSegmentIndex < 0)
                return;

            var start = _segmentCache[startSegmentIndex];
            bool startAtA = DistanceSqrXZ(cursorWorldPoint, start.a) <= DistanceSqrXZ(cursorWorldPoint, start.b);

            Vector3 startPoint = startAtA ? start.a : start.b;
            Vector3 endPoint = startAtA ? start.b : start.a;

            AppendPolylinePoint(startPoint);
            AppendPolylinePoint(endPoint);

            used[startSegmentIndex] = true;
            int usedCount = 1;
            float maxJoinDistSqr = cornerJoinDistance * cornerJoinDistance;

            // --- Forward chain from the end of the polyline ---
            Vector3 currentPoint = endPoint;
            while (usedCount < _segmentCache.Count)
            {
                int nextSegmentIndex = FindClosestSegmentEndpoint(currentPoint, used, out bool nearIsA, out float nearDistSqr);
                if (nextSegmentIndex < 0 || nearDistSqr > maxJoinDistSqr)
                    break;

                var next = _segmentCache[nextSegmentIndex];
                Vector3 near = nearIsA ? next.a : next.b;
                Vector3 far = nearIsA ? next.b : next.a;

                AppendPolylinePoint(near);
                AppendPolylinePoint(far);

                currentPoint = far;
                used[nextSegmentIndex] = true;
                usedCount++;
            }

            // --- Backward chain from the start of the polyline ---
            // This handles L-shaped walls where the corner is at the polyline start.
            // Without this, a segment connecting to the start would be treated as
            // disconnected, creating a reverse-direction jump that corrupts winding.
            Vector3 chainStart = _orderedPolyline[0];
            while (usedCount < _segmentCache.Count)
            {
                int prevSegmentIndex = FindClosestSegmentEndpoint(chainStart, used, out bool nearIsA, out float nearDistSqr);
                if (prevSegmentIndex < 0 || nearDistSqr > maxJoinDistSqr)
                    break;

                var prev = _segmentCache[prevSegmentIndex];
                Vector3 near = nearIsA ? prev.a : prev.b;
                Vector3 far = nearIsA ? prev.b : prev.a;

                // Prepend: near connects to the existing polyline start, far becomes the new start.
                PrependPolylinePoint(near);
                PrependPolylinePoint(far);

                chainStart = far;
                used[prevSegmentIndex] = true;
                usedCount++;
            }

            // --- Handle truly disconnected segments ---
            // These are wall segments that are not reachable from the main chain.
            // Append them maintaining cursor-relative ordering.
            while (usedCount < _segmentCache.Count)
            {
                int disconnectedIndex = FindClosestSegmentToPoint(cursorWorldPoint, used);
                if (disconnectedIndex < 0)
                    break;

                var disconnected = _segmentCache[disconnectedIndex];
                bool disconnectedStartAtA = DistanceSqrXZ(cursorWorldPoint, disconnected.a) <= DistanceSqrXZ(cursorWorldPoint, disconnected.b);
                Vector3 disconnectedStart = disconnectedStartAtA ? disconnected.a : disconnected.b;
                Vector3 disconnectedEnd = disconnectedStartAtA ? disconnected.b : disconnected.a;

                // Record the gap between the end of the previous chain and this disconnected segment.
                int countBefore = _orderedPolyline.Count;
                AppendPolylinePoint(disconnectedStart);
                if (_orderedPolyline.Count > countBefore && countBefore > 0)
                {
                    _polylineGapIndices.Add(countBefore - 1);
                }
                AppendPolylinePoint(disconnectedEnd);

                used[disconnectedIndex] = true;
                usedCount++;

                // Try forward-chaining from this disconnected segment too
                currentPoint = disconnectedEnd;
                while (usedCount < _segmentCache.Count)
                {
                    int nextIdx = FindClosestSegmentEndpoint(currentPoint, used, out bool nA, out float nDSq);
                    if (nextIdx < 0 || nDSq > maxJoinDistSqr)
                        break;

                    var nx = _segmentCache[nextIdx];
                    Vector3 nNear = nA ? nx.a : nx.b;
                    Vector3 nFar = nA ? nx.b : nx.a;

                    AppendPolylinePoint(nNear);
                    AppendPolylinePoint(nFar);

                    currentPoint = nFar;
                    used[nextIdx] = true;
                    usedCount++;
                }
            }
        }

        private int FindClosestSegmentToPoint(Vector3 worldPoint, bool[] used)
        {
            int bestIndex = -1;
            float bestDistSqr = float.MaxValue;

            for (int i = 0; i < _segmentCache.Count; i++)
            {
                if (used != null && i < used.Length && used[i])
                    continue;

                var segment = _segmentCache[i];
                float distA = DistanceSqrXZ(worldPoint, segment.a);
                float distB = DistanceSqrXZ(worldPoint, segment.b);
                float dist = Mathf.Min(distA, distB);

                if (dist < bestDistSqr)
                {
                    bestDistSqr = dist;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private int FindClosestSegmentEndpoint(Vector3 worldPoint, bool[] used, out bool nearIsA, out float nearDistSqr)
        {
            nearIsA = true;
            nearDistSqr = float.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < _segmentCache.Count; i++)
            {
                if (used != null && i < used.Length && used[i])
                    continue;

                var segment = _segmentCache[i];
                float distA = DistanceSqrXZ(worldPoint, segment.a);
                if (distA < nearDistSqr)
                {
                    nearDistSqr = distA;
                    nearIsA = true;
                    bestIndex = i;
                }

                float distB = DistanceSqrXZ(worldPoint, segment.b);
                if (distB < nearDistSqr)
                {
                    nearDistSqr = distB;
                    nearIsA = false;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void AppendPolylinePoint(Vector3 point)
        {
            if (_orderedPolyline.Count == 0)
            {
                _orderedPolyline.Add(point);
                return;
            }

            Vector3 last = _orderedPolyline[_orderedPolyline.Count - 1];
            if (DistanceSqrXZ(last, point) > 0.0001f)
            {
                _orderedPolyline.Add(point);
            }
        }

        private void PrependPolylinePoint(Vector3 point)
        {
            if (_orderedPolyline.Count == 0)
            {
                _orderedPolyline.Insert(0, point);
                return;
            }

            Vector3 first = _orderedPolyline[0];
            if (DistanceSqrXZ(first, point) > 0.0001f)
            {
                _orderedPolyline.Insert(0, point);
            }
        }

        private void BuildPolylineLengthCache()
        {
            _polylineLengths.Clear();

            if (_orderedPolyline.Count == 0)
                return;

            _polylineLengths.Add(0f);
            float cumulative = 0f;

            for (int i = 1; i < _orderedPolyline.Count; i++)
            {
                cumulative += Mathf.Sqrt(DistanceSqrXZ(_orderedPolyline[i - 1], _orderedPolyline[i]));
                _polylineLengths.Add(cumulative);
            }
        }

        private bool TrySamplePolyline(float distance, out Vector3 samplePoint, out Vector3 tangent)
        {
            samplePoint = Vector3.zero;
            tangent = Vector3.forward;

            if (_orderedPolyline.Count < 2 || _polylineLengths.Count != _orderedPolyline.Count)
                return false;

            float totalLength = _polylineLengths[_polylineLengths.Count - 1];
            if (totalLength <= 0.0001f)
            {
                samplePoint = _orderedPolyline[0];
                tangent = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;
                return true;
            }

            float clampedDistance = Mathf.Clamp(distance, 0f, totalLength);

            for (int i = 0; i < _orderedPolyline.Count - 1; i++)
            {
                float segStart = _polylineLengths[i];
                float segEnd = _polylineLengths[i + 1];
                float segLength = segEnd - segStart;

                if (segLength <= 0.0001f)
                    continue;

                if (clampedDistance <= segEnd || i == _orderedPolyline.Count - 2)
                {
                    float t = Mathf.Clamp01((clampedDistance - segStart) / segLength);
                    Vector3 a = _orderedPolyline[i];
                    Vector3 b = _orderedPolyline[i + 1];

                    samplePoint = Vector3.Lerp(a, b, t);
                    tangent = b - a;
                    return true;
                }
            }

            samplePoint = _orderedPolyline[_orderedPolyline.Count - 1];
            tangent = _orderedPolyline[_orderedPolyline.Count - 1] - _orderedPolyline[_orderedPolyline.Count - 2];
            return true;
        }

        private static Vector3 ClosestPointOnSegmentXZ(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            Vector2 p2 = new Vector2(point.x, point.z);

            Vector2 ab = b2 - a2;
            float abSqr = ab.sqrMagnitude;

            float t = 0f;
            if (abSqr > 0.000001f)
            {
                t = Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / abSqr);
            }

            Vector2 c2 = a2 + ab * t;
            float y = Mathf.Lerp(a.y, b.y, t);
            return new Vector3(c2.x, y, c2.y);
        }

        private static float DistanceSqrXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}
