using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for EnemyMassesDynamicWallPlacementRuntime.
    /// Matches the sectioned style used by WallPlacementZone and adds runtime utility controls.
    /// </summary>
    [CustomEditor(typeof(EnemyMassesDynamicWallPlacementRuntime))]
    public class EnemyMassesDynamicWallPlacementRuntimeEditor : UnityEditor.Editor
    {
        private EnemyMassesDynamicWallPlacementRuntime _runtime;

        private SerializedProperty _runtimeZoneProp;
        private SerializedProperty _rtsControllerProp;
        private SerializedProperty _wallSegmentsProp;

        private SerializedProperty _cursorSurfaceMaskProp;
        private SerializedProperty _cursorRayDistanceProp;
        private SerializedProperty _activationDistanceProp;
        private SerializedProperty _autoCollectChildWallSegmentsProp;
        private SerializedProperty _includeInactiveChildrenProp;

        private SerializedProperty _slotSpacingProp;
        private SerializedProperty _maxSlotsProp;
        private SerializedProperty _slotOffsetFromWallProp;
        private SerializedProperty _slotHeightOffsetProp;
        private SerializedProperty _slotEdgePaddingProp;
        private SerializedProperty _minSlotSeparationFactorProp;
        private SerializedProperty _slotWallClearanceProp;
        private SerializedProperty _wallClearanceIterationsProp;
        private SerializedProperty _cornerJoinDistanceProp;
        private SerializedProperty _rebuildMoveThresholdProp;
        private SerializedProperty _rebuildIntervalProp;
        private SerializedProperty _allowRebuildWhileOccupiedProp;
        private SerializedProperty _skipGapsBetweenWallsProp;

        private SerializedProperty _showRuntimeSpotMarkersProp;
        private SerializedProperty _keepZoneRegisteredWhenInactiveProp;
        private SerializedProperty _clearAssignmentsOnDeactivateProp;

        private SerializedProperty _enableDebugLoggingProp;

        private void OnEnable()
        {
            if (target == null)
                return;

            _runtime = target as EnemyMassesDynamicWallPlacementRuntime;
            if (_runtime == null)
                return;

            _runtimeZoneProp = serializedObject.FindProperty("runtimeZone");
            _rtsControllerProp = serializedObject.FindProperty("rtsController");
            _wallSegmentsProp = serializedObject.FindProperty("wallSegments");

            _cursorSurfaceMaskProp = serializedObject.FindProperty("cursorSurfaceMask");
            _cursorRayDistanceProp = serializedObject.FindProperty("cursorRayDistance");
            _activationDistanceProp = serializedObject.FindProperty("activationDistance");
            _autoCollectChildWallSegmentsProp = serializedObject.FindProperty("autoCollectChildWallSegments");
            _includeInactiveChildrenProp = serializedObject.FindProperty("includeInactiveChildren");

            _slotSpacingProp = serializedObject.FindProperty("slotSpacing");
            _maxSlotsProp = serializedObject.FindProperty("maxSlots");
            _slotOffsetFromWallProp = serializedObject.FindProperty("slotOffsetFromWall");
            _slotHeightOffsetProp = serializedObject.FindProperty("slotHeightOffset");
            _slotEdgePaddingProp = serializedObject.FindProperty("slotEdgePadding");
            _minSlotSeparationFactorProp = serializedObject.FindProperty("minSlotSeparationFactor");
            _slotWallClearanceProp = serializedObject.FindProperty("slotWallClearance");
            _wallClearanceIterationsProp = serializedObject.FindProperty("wallClearanceIterations");
            _cornerJoinDistanceProp = serializedObject.FindProperty("cornerJoinDistance");
            _rebuildMoveThresholdProp = serializedObject.FindProperty("rebuildMoveThreshold");
            _rebuildIntervalProp = serializedObject.FindProperty("rebuildInterval");
            _allowRebuildWhileOccupiedProp = serializedObject.FindProperty("allowRebuildWhileOccupied");
            _skipGapsBetweenWallsProp = serializedObject.FindProperty("skipGapsBetweenWalls");

            _showRuntimeSpotMarkersProp = serializedObject.FindProperty("showRuntimeSpotMarkers");
            _keepZoneRegisteredWhenInactiveProp = serializedObject.FindProperty("keepZoneRegisteredWhenInactive");
            _clearAssignmentsOnDeactivateProp = serializedObject.FindProperty("clearAssignmentsOnDeactivate");

            _enableDebugLoggingProp = serializedObject.FindProperty("enableDebugLogging");
        }

        public override void OnInspectorGUI()
        {
            if (_runtime == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeaderSection();
            EditorGUILayout.Space(10);

            DrawReferencesAndSegments();
            EditorGUILayout.Space(5);

            DrawCursorDetection();
            EditorGUILayout.Space(5);

            DrawSlotGeneration();
            EditorGUILayout.Space(5);

            DrawRuntimeBehavior();
            EditorGUILayout.Space(5);

            DrawDebugSection();
            EditorGUILayout.Space(10);

            DrawUtilityControls();

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed && !Application.isPlaying)
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawHeaderSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            Color headerColor = _runtime.RuntimeZone != null
                ? new Color(0.2f, 0.7f, 0.35f)
                : new Color(0.85f, 0.5f, 0.2f);

            GUI.color = headerColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Dynamic Wall Placement Runtime", titleStyle);

            string zoneName = _runtime.RuntimeZone != null ? _runtime.RuntimeZone.name : "(none)";
            EditorGUILayout.LabelField("Runtime Zone", zoneName);

            EditorGUILayout.EndVertical();
        }

        private void DrawReferencesAndSegments()
        {
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_runtimeZoneProp, new GUIContent("Runtime Zone"));
            EditorGUILayout.PropertyField(_rtsControllerProp, new GUIContent("RTS Controller"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Wall Segments", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_wallSegmentsProp, new GUIContent("Wall Box Colliders"), true);

            int valid, missing;
            CountWallSegments(out valid, out missing);
            EditorGUILayout.LabelField("Segment Stats", $"{valid} valid, {missing} missing");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Collect Children", GUILayout.Height(24)))
            {
                AutoCollectSegments();
            }

            if (GUILayout.Button("Remove Missing", GUILayout.Height(24)))
            {
                RemoveMissingSegments();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Create Runtime Zone", GUILayout.Height(24)))
            {
                CreateRuntimeZoneIfMissing();
            }

            if (valid == 0)
            {
                EditorGUILayout.HelpBox("No valid wall segments are assigned. Dynamic slots cannot be generated until at least one BoxCollider is configured.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCursorDetection()
        {
            EditorGUILayout.LabelField("Cursor Detection", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_cursorSurfaceMaskProp, new GUIContent("Surface Mask"));
            EditorGUILayout.PropertyField(_cursorRayDistanceProp, new GUIContent("Ray Distance"));
            EditorGUILayout.PropertyField(_activationDistanceProp, new GUIContent("Activation Distance"));
            EditorGUILayout.PropertyField(_autoCollectChildWallSegmentsProp, new GUIContent("Auto Collect Child Segments"));

            if (_autoCollectChildWallSegmentsProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_includeInactiveChildrenProp, new GUIContent("Include Inactive Children"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.HelpBox(
                "Preview activates when cursor world position is within Activation Distance from the nearest configured wall segment.",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private void DrawSlotGeneration()
        {
            EditorGUILayout.LabelField("Slot Generation", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_slotSpacingProp, new GUIContent("Slot Spacing"));
            EditorGUILayout.PropertyField(_maxSlotsProp, new GUIContent("Max Slots"));
            EditorGUILayout.PropertyField(_slotOffsetFromWallProp, new GUIContent("Offset From Wall"));
            EditorGUILayout.PropertyField(_slotHeightOffsetProp, new GUIContent("Height Offset"));
            EditorGUILayout.PropertyField(_slotEdgePaddingProp, new GUIContent("Edge Padding"));
            EditorGUILayout.PropertyField(_minSlotSeparationFactorProp, new GUIContent("Min Separation Factor"));
            EditorGUILayout.PropertyField(_slotWallClearanceProp, new GUIContent("Wall Clearance"));
            EditorGUILayout.PropertyField(_wallClearanceIterationsProp, new GUIContent("Clearance Iterations"));
            EditorGUILayout.PropertyField(_cornerJoinDistanceProp, new GUIContent("Corner Join Distance"));
            EditorGUILayout.PropertyField(_rebuildMoveThresholdProp, new GUIContent("Rebuild Move Threshold"));
            EditorGUILayout.PropertyField(_rebuildIntervalProp, new GUIContent("Rebuild Interval"));

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_allowRebuildWhileOccupiedProp, new GUIContent("Allow Rebuild While Occupied",
                "When enabled, slot positions can update even while agents occupy spots. Occupied agents silently keep their nearest slot."));

            EditorGUILayout.PropertyField(_skipGapsBetweenWallsProp, new GUIContent("Skip Gaps Between Walls",
                "When enabled, slots are only generated along actual wall geometry. Gaps between disconnected wall segments are skipped."));

            EditorGUILayout.HelpBox(
                "Slot rebuilds are automatically locked while any assigned agents are still pathfinding to their spots.",
                MessageType.Info);

            float estimatedLength = EstimateConfiguredWallLength();
            float spacing = Mathf.Max(0.25f, _slotSpacingProp.floatValue);
            float edgePadding = Mathf.Max(0f, _slotEdgePaddingProp.floatValue) * 2f;
            float usableLength = Mathf.Max(0f, estimatedLength - edgePadding);
            int estimatedSlots = usableLength > 0f
                ? Mathf.Clamp(Mathf.FloorToInt(usableLength / spacing) + 1, 1, Mathf.Max(1, _maxSlotsProp.intValue))
                : 0;
            float minSeparation = spacing * Mathf.Clamp(_minSlotSeparationFactorProp.floatValue, 0.1f, 1f);
            float wallClearance = Mathf.Max(0f, _slotWallClearanceProp.floatValue);

            EditorGUILayout.HelpBox(
                estimatedLength > 0f
                    ? $"Estimated wall length: {estimatedLength:F2}m (usable ~{usableLength:F2}m) -> approx {estimatedSlots} slots before pruning. Min separation ~= {minSeparation:F2}m, extra wall clearance = {wallClearance:F2}m."
                    : "Assign wall colliders to compute slot capacity estimates.",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeBehavior()
        {
            EditorGUILayout.LabelField("Runtime Behavior", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showRuntimeSpotMarkersProp, new GUIContent("Show Runtime Spot Markers"));
            EditorGUILayout.PropertyField(_keepZoneRegisteredWhenInactiveProp, new GUIContent("Keep Zone Registered When Inactive"));
            EditorGUILayout.PropertyField(_clearAssignmentsOnDeactivateProp, new GUIContent("Clear Assignments On Deactivate"));

            if (_clearAssignmentsOnDeactivateProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "When preview deactivates, all current assignments on the runtime zone will be cleared.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDebugSection()
        {
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableDebugLoggingProp, new GUIContent("Enable Debug Logging"));

            if (_enableDebugLoggingProp.boolValue)
            {
                EditorGUILayout.HelpBox("Console will show detailed runtime logs for segment registration and slot rebuilds.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUtilityControls()
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (Application.isPlaying)
            {
                DrawPlayModeStatus();

                EditorGUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Force Refresh Preview", GUILayout.Height(24)))
                {
                    ForceRefreshPreview();
                }
                if (GUILayout.Button("Hide Preview", GUILayout.Height(24)))
                {
                    ForceHidePreview(false);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Hide + Clear", GUILayout.Height(24)))
                {
                    ForceHidePreview(true);
                }

                using (new EditorGUI.DisabledScope(_runtime.RuntimeZone == null))
                {
                    if (GUILayout.Button("Clear Zone Assignments", GUILayout.Height(24)))
                    {
                        _runtime.RuntimeZone?.ClearAllAssignments();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("Configured Segments", _runtime.ConfiguredWallSegmentCount.ToString());
                EditorGUILayout.LabelField("Runtime Zone", _runtime.RuntimeZone != null ? _runtime.RuntimeZone.name : "(none)");

                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox("Enter Play Mode to use preview refresh/hide runtime controls.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPlayModeStatus()
        {
            DrawStatusLine("Preview Active", _runtime.PreviewActive, new Color(0.2f, 0.8f, 0.3f), new Color(0.8f, 0.45f, 0.2f));
            DrawStatusLine("Zone Registered", _runtime.ZoneRegisteredToRTS, new Color(0.2f, 0.8f, 0.3f), new Color(0.8f, 0.45f, 0.2f));

            var zone = _runtime.RuntimeZone;
            if (zone != null)
            {
                DrawStatusLine("Agents In Transit", zone.HasAgentsInTransit, new Color(0.85f, 0.6f, 0.15f), new Color(0.4f, 0.75f, 0.4f));
            }

            EditorGUILayout.LabelField("Configured Segments", _runtime.ConfiguredWallSegmentCount.ToString());
            EditorGUILayout.LabelField("Active Segment Cache", _runtime.ActiveWallSegmentCount.ToString());

            if (zone != null)
            {
                int total = zone.Spots?.Count ?? 0;
                int occupied = zone.OccupiedSpots;
                int available = zone.AvailableSpots;

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Runtime Zone", zone.name);
                EditorGUILayout.LabelField("Spot Capacity", $"{available} available / {occupied} occupied / {total} total");

                if (total > 0)
                {
                    float fill = (float)occupied / total;
                    Rect barRect = EditorGUILayout.GetControlRect(false, 20f);
                    EditorGUI.ProgressBar(barRect, fill, $"Occupancy {occupied}/{total} ({fill:P0})");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Runtime zone has not been created yet.", MessageType.Warning);
            }
        }

        private void DrawStatusLine(string label, bool status, Color onColor, Color offColor)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120f));

            GUIStyle valueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = status ? onColor : offColor }
            };

            EditorGUILayout.LabelField(status ? "YES" : "NO", valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void AutoCollectSegments()
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(_runtime, "Auto Collect Wall Segments");
            _runtime.CollectWallSegmentsFromChildren();

            EditorUtility.SetDirty(_runtime);
            MarkSceneDirty();
            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }

        private void RemoveMissingSegments()
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(_runtime, "Remove Missing Wall Segments");
            int removed = _runtime.RemoveNullWallSegments();

            if (removed > 0)
            {
                EditorUtility.SetDirty(_runtime);
                MarkSceneDirty();
            }

            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }

        private void CreateRuntimeZoneIfMissing()
        {
            serializedObject.ApplyModifiedProperties();

            if (_runtime.RuntimeZone != null)
            {
                Selection.activeObject = _runtime.RuntimeZone;
                return;
            }

            EnemyMassesWallPlacementZone zone = _runtime.EnsureRuntimeZone();
            if (zone == null)
                return;

            Undo.RegisterCreatedObjectUndo(zone.gameObject, "Create Runtime Wall Placement Zone");
            Undo.RecordObject(_runtime, "Assign Runtime Wall Placement Zone");

            serializedObject.Update();
            _runtimeZoneProp.objectReferenceValue = zone;
            serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(_runtime);
            EditorUtility.SetDirty(zone);
            MarkSceneDirty();

            Selection.activeObject = zone;
            Repaint();
            SceneView.RepaintAll();
        }

        private void ForceRefreshPreview()
        {
            serializedObject.ApplyModifiedProperties();
            _runtime.ForceRefreshPreview();
            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }

        private void ForceHidePreview(bool clearAssignments)
        {
            serializedObject.ApplyModifiedProperties();
            _runtime.ForceDeactivatePreview(clearAssignments);
            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }

        private void CountWallSegments(out int valid, out int missing)
        {
            valid = 0;
            missing = 0;

            if (_wallSegmentsProp == null || !_wallSegmentsProp.isArray)
                return;

            for (int i = 0; i < _wallSegmentsProp.arraySize; i++)
            {
                var element = _wallSegmentsProp.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue is BoxCollider box && box != null)
                {
                    valid++;
                }
                else
                {
                    missing++;
                }
            }
        }

        private IEnumerable<BoxCollider> EnumerateConfiguredSegments()
        {
            if (_wallSegmentsProp == null || !_wallSegmentsProp.isArray)
                yield break;

            for (int i = 0; i < _wallSegmentsProp.arraySize; i++)
            {
                var box = _wallSegmentsProp.GetArrayElementAtIndex(i).objectReferenceValue as BoxCollider;
                if (box == null)
                    continue;

                yield return box;
            }
        }

        private float EstimateConfiguredWallLength()
        {
            float total = 0f;

            foreach (var box in EnumerateConfiguredSegments())
            {
                if (TryExtractSegment(box, out Vector3 a, out Vector3 b))
                {
                    total += Vector3.Distance(a, b);
                }
            }

            return total;
        }

        private bool TryExtractSegment(BoxCollider box, out Vector3 a, out Vector3 b)
        {
            a = Vector3.zero;
            b = Vector3.zero;

            if (box == null || !box.enabled)
                return false;

            Vector3 worldCenter = box.transform.TransformPoint(box.center);
            Vector3 scaledSize = Vector3.Scale(box.size, Abs(box.transform.lossyScale));

            Vector3 xAxis = Vector3.ProjectOnPlane(box.transform.right, Vector3.up);
            Vector3 zAxis = Vector3.ProjectOnPlane(box.transform.forward, Vector3.up);

            float xLength = xAxis.magnitude * Mathf.Abs(scaledSize.x);
            float zLength = zAxis.magnitude * Mathf.Abs(scaledSize.z);

            Vector3 direction = xLength >= zLength ? xAxis : zAxis;
            float length = Mathf.Max(xLength, zLength);

            if (direction.sqrMagnitude <= 0.0001f || length <= 0.05f)
                return false;

            direction.Normalize();
            float half = length * 0.5f;

            a = worldCenter - direction * half;
            b = worldCenter + direction * half;
            return true;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private void MarkSceneDirty()
        {
            if (Application.isPlaying)
                return;

            if (_runtime != null && _runtime.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(_runtime.gameObject.scene);
            }
        }

        private void OnSceneGUI()
        {
            if (_runtime == null)
                return;

            serializedObject.UpdateIfRequiredOrScript();

            float activationDistance = _activationDistanceProp != null ? Mathf.Max(0f, _activationDistanceProp.floatValue) : 0f;
            float lineThickness = 4f;

            foreach (var box in EnumerateConfiguredSegments())
            {
                if (!TryExtractSegment(box, out Vector3 a, out Vector3 b))
                    continue;

                Handles.color = new Color(0.2f, 0.85f, 1f, 0.95f);
                Handles.DrawAAPolyLine(lineThickness, a, b);

                float endpointSize = HandleUtility.GetHandleSize((a + b) * 0.5f) * 0.05f;
                Handles.SphereHandleCap(0, a, Quaternion.identity, endpointSize, EventType.Repaint);
                Handles.SphereHandleCap(0, b, Quaternion.identity, endpointSize, EventType.Repaint);

                Vector3 dir = b - a;
                dir.y = 0f;
                if (activationDistance > 0f && dir.sqrMagnitude > 0.0001f)
                {
                    dir.Normalize();
                    Vector3 n = Vector3.Cross(Vector3.up, dir) * activationDistance;

                    Vector3 p1 = a + n;
                    Vector3 p2 = b + n;
                    Vector3 p3 = b - n;
                    Vector3 p4 = a - n;

                    Handles.color = new Color(0.2f, 0.85f, 1f, 0.08f);
                    Handles.DrawAAConvexPolygon(p1, p2, p3, p4);

                    Handles.color = new Color(0.2f, 0.85f, 1f, 0.35f);
                    Handles.DrawAAPolyLine(1.5f, p1, p2);
                    Handles.DrawAAPolyLine(1.5f, p4, p3);
                }
            }

            var zone = _runtime.RuntimeZone;
            if (zone == null || zone.Spots == null)
                return;

            float spotSize = HandleUtility.GetHandleSize(zone.transform.position) * 0.045f;
            Handles.color = new Color(0.2f, 1f, 0.5f, 0.9f);

            foreach (var spot in zone.Spots)
            {
                Handles.SphereHandleCap(0, spot.worldPosition, Quaternion.identity, spotSize, EventType.Repaint);
            }

            Handles.Label(
                zone.transform.position + Vector3.up * 1.4f,
                $"Dynamic Spots: {zone.Spots.Count}",
                new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } });
        }
    }
}
