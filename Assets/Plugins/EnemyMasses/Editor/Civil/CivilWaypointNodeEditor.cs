using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Civil;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Editor.Civil
{
    /// <summary>
    /// Custom editor for CivilWaypointNode with UIToolkit inspector and scene tools.
    /// </summary>
    [CustomEditor(typeof(CivilWaypointNode))]
    public class CivilWaypointNodeEditor : UnityEditor.Editor
    {
        private CivilWaypointNode _targetNode;
        
        // Connection creation state
        private static CivilWaypointNode _connectionSource;
        private static bool _isCreatingConnection;
        
        // Editor preferences
        private static bool _showGizmos = true;
        private static bool _snapToNavMesh = true;
        
        // Colors for different waypoint types
        private static readonly Color PathColor = new Color(0.2f, 0.6f, 1f);
        private static readonly Color ExploreColor = new Color(0.2f, 0.8f, 0.4f);
        private static readonly Color DespawnColor = new Color(1f, 0.4f, 0.2f);
        private static readonly Color ActivityColor = new Color(0.9f, 0.7f, 0.2f);
        private static readonly Color LeanColor = new Color(0.2f, 0.8f, 0.8f);
        private static readonly Color SitColor = new Color(0.8f, 0.5f, 0.2f);
        private static readonly Color SpawnColor = new Color(0.6f, 0.2f, 0.9f);
        
        private SerializedProperty _waypointType;
        private SerializedProperty _channelWidth;
        private SerializedProperty _arrivalRadius;
        private SerializedProperty _connections;
        private SerializedProperty _exploreRadius;
        private SerializedProperty _exploreTimeOverride;
        private SerializedProperty _availableActivities;
        private SerializedProperty _activityPosition;
        private SerializedProperty _activityFacing;
        private SerializedProperty _maxConcurrentActivities;
        private SerializedProperty _leanSpots;
        private SerializedProperty _sitSpots;
        private SerializedProperty _spawnEntries;
        private SerializedProperty _spawnRadius;
        private SerializedProperty _spawnAndNavigate;
        private SerializedProperty _maxTotalSpawnedAlive;
        private SerializedProperty _allowedFactions;
        
        private void OnEnable()
        {
            _targetNode = target as CivilWaypointNode;
            
            _waypointType = serializedObject.FindProperty("waypointType");
            _channelWidth = serializedObject.FindProperty("defaultChannelWidth");
            _arrivalRadius = serializedObject.FindProperty("arrivalRadius");
            _connections = serializedObject.FindProperty("connections");
            _exploreRadius = serializedObject.FindProperty("exploreRadius");
            _exploreTimeOverride = serializedObject.FindProperty("exploreTimeOverride");
            _availableActivities = serializedObject.FindProperty("availableActivities");
            _activityPosition = serializedObject.FindProperty("activityPosition");
            _activityFacing = serializedObject.FindProperty("activityFacing");
            _maxConcurrentActivities = serializedObject.FindProperty("maxConcurrentActivities");
            _leanSpots = serializedObject.FindProperty("leanSpots");
            _sitSpots = serializedObject.FindProperty("sitSpots");
            _spawnEntries = serializedObject.FindProperty("spawnEntries");
            _spawnRadius = serializedObject.FindProperty("spawnRadius");
            _spawnAndNavigate = serializedObject.FindProperty("spawnAndNavigate");
            _maxTotalSpawnedAlive = serializedObject.FindProperty("maxTotalSpawnedAlive");
            _allowedFactions = serializedObject.FindProperty("allowedFactions");
        }
        
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.style.paddingTop = 10;
            
            // Header with type indicator
            var header = CreateHeader();
            root.Add(header);
            
            root.Add(new Space(10));
            
            // Core settings
            var coreSection = CreateSection("Core Settings");
            coreSection.Add(new PropertyField(_waypointType));
            coreSection.Add(new PropertyField(_channelWidth));
            coreSection.Add(new PropertyField(_arrivalRadius));
            coreSection.Add(new PropertyField(_allowedFactions));
            root.Add(coreSection);
            
            // Type-specific settings
            var typeSection = CreateTypeSpecificSection();
            root.Add(typeSection);
            
            // Connections section
            var connectionsSection = CreateConnectionsSection();
            root.Add(connectionsSection);
            
            // Tools section
            var toolsSection = CreateToolsSection();
            root.Add(toolsSection);
            
            return root;
        }
        
        private VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.backgroundColor = GetTypeColor(_targetNode.waypointType);
            header.style.borderBottomLeftRadius = 4;
            header.style.borderBottomRightRadius = 4;
            header.style.borderTopLeftRadius = 4;
            header.style.borderTopRightRadius = 4;
            header.style.paddingLeft = 10;
            header.style.paddingRight = 10;
            header.style.paddingTop = 5;
            header.style.paddingBottom = 5;
            
            var titleLabel = new Label($"Civil Waypoint: {_targetNode.waypointType}");
            titleLabel.style.color = Color.white;
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);
            
            var indexLabel = new Label($"ID: {_targetNode.GetInstanceID()}");
            indexLabel.style.color = new Color(1f, 1f, 1f, 0.7f);
            indexLabel.style.fontSize = 10;
            header.Add(indexLabel);
            
            return header;
        }
        
        private Foldout CreateSection(string title)
        {
            var section = new Foldout { text = title };
            section.value = true;
            section.style.marginTop = 5;
            section.style.marginBottom = 5;
            return section;
        }
        
        private VisualElement CreateTypeSpecificSection()
        {
            var container = new VisualElement();
            container.name = "type-specific-section";
            
            // This will be rebuilt when type changes
            RefreshTypeSpecificUI(container);
            
            // Register callback for type changes
            var typeField = new PropertyField(_waypointType);
            typeField.RegisterValueChangeCallback(evt => RefreshTypeSpecificUI(container));
            
            return container;
        }
        
        private void RefreshTypeSpecificUI(VisualElement container)
        {
            container.Clear();
            
            CivilWaypointType type = (CivilWaypointType)_waypointType.enumValueIndex;
            
            switch (type)
            {
                case CivilWaypointType.Path:
                    // Path has no additional settings beyond core
                    break;
                    
                case CivilWaypointType.Explore:
                    var exploreSection = CreateSection("Exploration Settings");
                    exploreSection.Add(new PropertyField(_exploreRadius, "Explore Radius"));
                    exploreSection.Add(new PropertyField(_exploreTimeOverride, "Explore Time (Override)"));
                    
                    var exploreHelp = new HelpBox(
                        "Agents will wander randomly within the explore radius. Set explore time to 0,0 to use faction defaults.",
                        HelpBoxMessageType.Info);
                    exploreSection.Add(exploreHelp);
                    
                    container.Add(exploreSection);
                    break;
                    
                case CivilWaypointType.Despawn:
                    var despawnSection = CreateSection("Despawn Settings");
                    var despawnHelp = new HelpBox(
                        "Agents arriving here will fade out and return to the pool. Great for exits, portals, or building entrances.",
                        HelpBoxMessageType.Info);
                    despawnSection.Add(despawnHelp);
                    container.Add(despawnSection);
                    break;
                    
                case CivilWaypointType.Activity:
                    var activitySection = CreateSection("Activity Settings");
                    var activitiesFoldout = new Foldout { text = "Available Activities", value = true };
                    activitiesFoldout.Add(new PropertyField(_availableActivities) { name = "availableActivitiesField" });

                    var addActivityBtn = new Button(() => AddNewActivity()) { text = "Add Activity" };
                    addActivityBtn.style.marginTop = 4;
                    activitiesFoldout.Add(addActivityBtn);

                    activitySection.Add(activitiesFoldout);
                    activitySection.Add(new PropertyField(_activityPosition, "Activity Position"));
                    activitySection.Add(new PropertyField(_activityFacing, "Activity Facing"));
                    activitySection.Add(new PropertyField(_maxConcurrentActivities, "Max Concurrent"));

                    var activityHelp = new HelpBox(
                        "Agents can perform activities here with animations and props. Requires BoneTracker workflow for prop attachments.",
                        HelpBoxMessageType.Info);
                    activitySection.Add(activityHelp);
                    
                    container.Add(activitySection);
                    break;

                case CivilWaypointType.Lean:
                    var leanSection = CreateSection("Lean Settings");
                    leanSection.Add(new PropertyField(_leanSpots, "Lean Spots"));

                    var leanHelp = new HelpBox(
                        "Agents reserve a free lean spot, snap to its marker position/rotation, and play the configured Interaction Definition. " +
                        "Duration defaults to 10s when not overridden.",
                        HelpBoxMessageType.Info);
                    leanSection.Add(leanHelp);

                    container.Add(leanSection);
                    break;

                case CivilWaypointType.Sit:
                    var sitSection = CreateSection("Sit Settings");
                    sitSection.Add(new PropertyField(_sitSpots, "Sit Spots"));

                    var sitHelp = new HelpBox(
                        "Agents reserve a free sit spot, snap to its marker position/rotation, and play the configured Interaction Definition. " +
                        "Duration defaults to 10s when not overridden.",
                        HelpBoxMessageType.Info);
                    sitSection.Add(sitHelp);

                    container.Add(sitSection);
                    break;
                    
                case CivilWaypointType.Spawn:
                    var spawnSection = CreateSection("Spawn Settings");
                    
                    // Spawn mode selection
                    var spawnModeProp = serializedObject.FindProperty("spawnMode");
                    var spawnModeField = new PropertyField(spawnModeProp, "Spawn Mode");
                    spawnModeField.Bind(serializedObject);
                    spawnSection.Add(spawnModeField);
                    
                    spawnSection.Add(new Space(5));
                    
                    // Fixed Radius settings
                    var fixedRadiusContainer = new VisualElement();
                    fixedRadiusContainer.Add(new PropertyField(_spawnRadius, "Spawn Radius"));
                    spawnSection.Add(fixedRadiusContainer);
                    
                    // Camera Frustum settings
                    var frustumContainer = new VisualElement();
                    var frustumEdgeDistProp = serializedObject.FindProperty("frustumEdgeDistance");
                    var frustumMinDistProp = serializedObject.FindProperty("frustumMinCameraDistance");
                    var frustumMaxDistProp = serializedObject.FindProperty("frustumMaxCameraDistance");
                    
                    frustumContainer.Add(new PropertyField(frustumEdgeDistProp, "Edge Distance"));
                    frustumContainer.Add(new PropertyField(frustumMinDistProp, "Min Camera Distance"));
                    frustumContainer.Add(new PropertyField(frustumMaxDistProp, "Max Camera Distance"));
                    
                    var frustumHelp = new HelpBox(
                        "Agents spawn just outside the camera view and walk into frame naturally. " +
                        "Great for open-world scenes where you want continuous foot traffic.",
                        HelpBoxMessageType.Info);
                    frustumContainer.Add(frustumHelp);
                    spawnSection.Add(frustumContainer);
                    
                    // Show/hide based on spawn mode
                    void UpdateSpawnModeVisibility()
                    {
                        bool isFrustum = spawnModeProp.enumValueIndex == (int)CivilSpawnMode.CameraFrustumEdge;
                        fixedRadiusContainer.style.display = isFrustum ? DisplayStyle.None : DisplayStyle.Flex;
                        frustumContainer.style.display = isFrustum ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    UpdateSpawnModeVisibility();
                    
                    spawnModeField.RegisterValueChangeCallback(evt => UpdateSpawnModeVisibility());
                    
                    spawnSection.Add(new Space(5));
                    
                    spawnSection.Add(new PropertyField(_spawnAndNavigate, "Spawn & Navigate"));
                    spawnSection.Add(new PropertyField(_maxTotalSpawnedAlive, "Max Total Alive"));
                    
                    spawnSection.Add(new Space(10));
                    
                    // Spawn entries header
                    var entriesHeader = new Label("Faction Spawn Entries");
                    entriesHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                    entriesHeader.style.marginTop = 10;
                    entriesHeader.style.marginBottom = 5;
                    spawnSection.Add(entriesHeader);
                    
                    // Spawn entries list with custom drawing
                    var entriesList = CreateSpawnEntriesUI();
                    spawnSection.Add(entriesList);
                    
                    var spawnHelp = new HelpBox(
                        "Configure which factions spawn here and at what intervals. " +
                        "Faction Index -1 means any civil-enabled faction. " +
                        "Use weights to adjust spawn probability when multiple factions can spawn.",
                        HelpBoxMessageType.Info);
                    spawnSection.Add(spawnHelp);
                    
                    container.Add(spawnSection);
                    break;
            }
        }
        
        private VisualElement CreateConnectionsSection()
        {
            var section = CreateSection("Connections");
            
            // Connection list
            var connectionList = new PropertyField(_connections);
            section.Add(connectionList);
            
            // Connection tools
            var toolsRow = new VisualElement();
            toolsRow.style.flexDirection = FlexDirection.Row;
            toolsRow.style.marginTop = 5;
            
            var startConnectionBtn = new Button(() => StartConnectionMode())
            {
                text = _isCreatingConnection ? "Cancel Connection" : "Start Connection"
            };
            startConnectionBtn.style.flexGrow = 1;
            toolsRow.Add(startConnectionBtn);
            
            var autoConnectBtn = new Button(() => AutoConnectNearby())
            {
                text = "Auto-Connect Nearby"
            };
            autoConnectBtn.style.flexGrow = 1;
            toolsRow.Add(autoConnectBtn);
            
            section.Add(toolsRow);
            
            // Help text
            if (_isCreatingConnection)
            {
                var help = new HelpBox(
                    "Click another waypoint in the Scene view to create a connection.",
                    HelpBoxMessageType.Info);
                section.Add(help);
            }
            
            return section;
        }
        
        private VisualElement CreateToolsSection()
        {
            var section = CreateSection("Tools");
            
            // Gizmo toggle
            var gizmoToggle = new Toggle("Show Gizmos") { value = _showGizmos };
            gizmoToggle.RegisterValueChangedCallback(evt =>
            {
                _showGizmos = evt.newValue;
                SceneView.RepaintAll();
            });
            section.Add(gizmoToggle);
            
            // Snap to NavMesh toggle
            var snapToggle = new Toggle("Snap to NavMesh") { value = _snapToNavMesh };
            snapToggle.RegisterValueChangedCallback(evt =>
            {
                _snapToNavMesh = evt.newValue;
            });
            section.Add(snapToggle);
            
            // Snap button
            var snapBtn = new Button(() => SnapToNavMesh()) { text = "Snap to NavMesh Now" };
            section.Add(snapBtn);
            
            // Duplicate button
            var duplicateBtn = new Button(() => DuplicateWaypoint()) { text = "Duplicate Waypoint" };
            section.Add(duplicateBtn);
            
            // Select connected
            var selectConnectedBtn = new Button(() => SelectConnectedWaypoints()) { text = "Select Connected" };
            section.Add(selectConnectedBtn);
            
            return section;
        }
        
        private void StartConnectionMode()
        {
            if (_isCreatingConnection && _connectionSource == _targetNode)
            {
                // Cancel connection mode
                _isCreatingConnection = false;
                _connectionSource = null;
            }
            else
            {
                // Start connection mode
                _isCreatingConnection = true;
                _connectionSource = _targetNode;
            }
            
            SceneView.RepaintAll();
        }
        
        private void AutoConnectNearby()
        {
            float maxDistance = 15f;
            CivilWaypointNode[] allNodes = Object.FindObjectsByType<CivilWaypointNode>(FindObjectsSortMode.None);
            
            Undo.RecordObject(_targetNode, "Auto-Connect Nearby Waypoints");
            
            int added = 0;
            foreach (var node in allNodes)
            {
                if (node == _targetNode)
                    continue;
                
                float distance = Vector3.Distance(_targetNode.transform.position, node.transform.position);
                if (distance <= maxDistance)
                {
                    // Check if already connected
                    bool alreadyConnected = false;
                    foreach (var conn in _targetNode.connections)
                    {
                        if (conn.target == node)
                        {
                            alreadyConnected = true;
                            break;
                        }
                    }
                    
                    if (!alreadyConnected)
                    {
                        _targetNode.connections.Add(new CivilWaypointConnection
                        {
                            target = node,
                            weight = 1f,
                            bidirectional = true,
                            channelWidth = _targetNode.defaultChannelWidth
                        });
                        added++;
                    }
                }
            }
            
            EditorUtility.SetDirty(_targetNode);
            Debug.Log($"[Civil Waypoints] Auto-connected {added} nearby waypoints to {_targetNode.name}");
        }
        
        private void SnapToNavMesh()
        {
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(_targetNode.transform.position, out hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Undo.RecordObject(_targetNode.transform, "Snap to NavMesh");
                _targetNode.transform.position = hit.position;
                EditorUtility.SetDirty(_targetNode);
            }
            else
            {
                Debug.LogWarning($"[Civil Waypoints] Could not find NavMesh near {_targetNode.name}");
            }
        }
        
        private void DuplicateWaypoint()
        {
            GameObject duplicate = Instantiate(_targetNode.gameObject);
            duplicate.name = _targetNode.name + " (Copy)";
            duplicate.transform.position = _targetNode.transform.position + Vector3.right * 3f;
            duplicate.transform.SetParent(_targetNode.transform.parent);
            
            // Clear connections on duplicate
            var duplicateNode = duplicate.GetComponent<CivilWaypointNode>();
            if (duplicateNode != null)
            {
                duplicateNode.connections.Clear();
            }
            
            Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate Waypoint");
            Selection.activeGameObject = duplicate;
        }
        
        private void SelectConnectedWaypoints()
        {
            List<GameObject> selected = new List<GameObject> { _targetNode.gameObject };
            
            foreach (var conn in _targetNode.connections)
            {
                if (conn.target != null)
                {
                    selected.Add(conn.target.gameObject);
                }
            }
            
            Selection.objects = selected.ToArray();
        }
        
        private VisualElement CreateSpawnEntriesUI()
        {
            var container = new VisualElement();
            
            // Get available factions from EnemyMassesCrowdController if available
            var crowdController = Object.FindFirstObjectByType<EnemyMassesCrowdController>();
            List<string> factionNames = new List<string> { "Any Civil Faction (-1)" };
            
            if (crowdController != null && crowdController.EnemyCrowds != null)
            {
                for (int i = 0; i < crowdController.EnemyCrowds.Count; i++)
                {
                    var crowd = crowdController.EnemyCrowds[i];
                    string name = crowd != null ? crowd.name : $"Faction {i}";
                    factionNames.Add($"{i}: {name}");
                }
            }
            
            // Default property field for the list
            var listField = new PropertyField(_spawnEntries);
            listField.RegisterCallback<ChangeEvent<int>>(evt =>
            {
                // Refresh UI when list changes
                container.MarkDirtyRepaint();
            });
            container.Add(listField);
            
            container.Add(new Space(5));
            
            // Quick add buttons
            var quickAddRow = new VisualElement();
            quickAddRow.style.flexDirection = FlexDirection.Row;
            quickAddRow.style.marginTop = 5;
            
            var addEntryBtn = new Button(() => AddSpawnEntry()) 
            { 
                text = "Add Spawn Entry",
                tooltip = "Add a new faction spawn entry with default settings"
            };
            addEntryBtn.style.flexGrow = 1;
            quickAddRow.Add(addEntryBtn);
            
            if (crowdController != null && crowdController.EnemyCrowds != null && crowdController.EnemyCrowds.Count > 0)
            {
                var addAllBtn = new Button(() => AddAllFactions(crowdController))
                {
                    text = "Add All Factions",
                    tooltip = "Add spawn entries for all registered civil-enabled factions"
                };
                addAllBtn.style.flexGrow = 1;
                quickAddRow.Add(addAllBtn);
            }
            
            container.Add(quickAddRow);
            
            // Runtime info if playing
            if (Application.isPlaying && _targetNode.spawnEntries != null && _targetNode.spawnEntries.Count > 0)
            {
                container.Add(new Space(10));
                
                var runtimeLabel = new Label("Runtime Status");
                runtimeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                container.Add(runtimeLabel);
                
                var statusBox = new VisualElement();
                statusBox.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
                statusBox.style.borderTopLeftRadius = statusBox.style.borderTopRightRadius = 
                    statusBox.style.borderBottomLeftRadius = statusBox.style.borderBottomRightRadius = 4;
                statusBox.style.paddingLeft = statusBox.style.paddingRight = 
                    statusBox.style.paddingTop = statusBox.style.paddingBottom = 5;
                statusBox.style.marginTop = 3;
                
                statusBox.Add(new Label($"Total Alive: {_targetNode.TotalSpawnedAlive}"));
                statusBox.Add(new Label($"Can Spawn: {_targetNode.CanSpawn}"));
                
                float nextSpawn = _targetNode.GetTimeUntilNextSpawn();
                string nextSpawnText = nextSpawn < float.MaxValue ? $"{nextSpawn:F1}s" : "N/A";
                statusBox.Add(new Label($"Next Spawn In: {nextSpawnText}"));
                
                container.Add(statusBox);
            }
            
            return container;
        }
        
        private void AddSpawnEntry()
        {
            serializedObject.Update();
            _spawnEntries.InsertArrayElementAtIndex(_spawnEntries.arraySize);
            
            // Set default values for the new entry
            var newEntry = _spawnEntries.GetArrayElementAtIndex(_spawnEntries.arraySize - 1);
            newEntry.FindPropertyRelative("factionIndex").intValue = -1;
            newEntry.FindPropertyRelative("weight").floatValue = 1f;
            newEntry.FindPropertyRelative("intervalMode").enumValueIndex = 1; // RandomRange
            newEntry.FindPropertyRelative("fixedInterval").floatValue = 10f;
            newEntry.FindPropertyRelative("minInterval").floatValue = 5f;
            newEntry.FindPropertyRelative("maxInterval").floatValue = 15f;
            newEntry.FindPropertyRelative("spawnCount").intValue = 1;
            newEntry.FindPropertyRelative("maxAliveAgents").intValue = 0;
            newEntry.FindPropertyRelative("enabled").boolValue = true;
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void AddAllFactions(EnemyMassesCrowdController crowdController)
        {
            serializedObject.Update();
            
            for (int i = 0; i < crowdController.EnemyCrowds.Count; i++)
            {
                var crowd = crowdController.EnemyCrowds[i];
                if (crowd == null) continue;
                
                // Check if crowd has civil settings enabled
                // For now, add all factions - can filter by civilSettings.enabled later
                
                _spawnEntries.InsertArrayElementAtIndex(_spawnEntries.arraySize);
                var newEntry = _spawnEntries.GetArrayElementAtIndex(_spawnEntries.arraySize - 1);
                newEntry.FindPropertyRelative("factionIndex").intValue = i;
                newEntry.FindPropertyRelative("weight").floatValue = 1f;
                newEntry.FindPropertyRelative("intervalMode").enumValueIndex = 1; // RandomRange
                newEntry.FindPropertyRelative("fixedInterval").floatValue = 10f;
                newEntry.FindPropertyRelative("minInterval").floatValue = 5f;
                newEntry.FindPropertyRelative("maxInterval").floatValue = 15f;
                newEntry.FindPropertyRelative("spawnCount").intValue = 1;
                newEntry.FindPropertyRelative("maxAliveAgents").intValue = 0;
                newEntry.FindPropertyRelative("enabled").boolValue = true;
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void AddNewActivity()
        {
            serializedObject.Update();

            if (_availableActivities == null)
                return;

            Undo.RecordObject(_targetNode, "Add Activity");

            int newIndex = _availableActivities.arraySize;
            _availableActivities.InsertArrayElementAtIndex(newIndex);

            var newActivity = _availableActivities.GetArrayElementAtIndex(newIndex);
            newActivity.FindPropertyRelative("activityId").stringValue = $"activity_{newIndex + 1}";
            newActivity.FindPropertyRelative("displayName").stringValue = "New Activity";
            newActivity.FindPropertyRelative("animationClip").objectReferenceValue = null;
            newActivity.FindPropertyRelative("loopAnimation").boolValue = true;
            newActivity.FindPropertyRelative("minDuration").floatValue = 5f;
            newActivity.FindPropertyRelative("maxDuration").floatValue = 30f;
            newActivity.FindPropertyRelative("selectionWeight").floatValue = 1f;
            newActivity.FindPropertyRelative("approachSpeedMultiplier").floatValue = 0.5f;
            newActivity.FindPropertyRelative("propAttachments").arraySize = 0;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_targetNode);
            Repaint();
        }
        
        private Color GetTypeColor(CivilWaypointType type)
        {
            return type switch
            {
                CivilWaypointType.Path => PathColor,
                CivilWaypointType.Explore => ExploreColor,
                CivilWaypointType.Despawn => DespawnColor,
                CivilWaypointType.Activity => ActivityColor,
                CivilWaypointType.Lean => LeanColor,
                CivilWaypointType.Sit => SitColor,
                CivilWaypointType.Spawn => SpawnColor,
                _ => Color.gray
            };
        }
        
        private void OnSceneGUI()
        {
            if (_targetNode == null)
                return;
            
            // Handle connection creation clicks
            if (_isCreatingConnection && _connectionSource == _targetNode)
            {
                HandleConnectionCreation();
            }
            
            // Draw handles for channel width
            DrawChannelWidthHandle();
            
            // Draw arrival radius handle
            DrawArrivalRadiusHandle();
            
            // Draw type-specific handles
            switch (_targetNode.waypointType)
            {
                case CivilWaypointType.Explore:
                    DrawExploreRadiusHandle();
                    break;
                case CivilWaypointType.Activity:
                    DrawActivityHandles();
                    break;
                case CivilWaypointType.Spawn:
                    DrawSpawnModeLabel();
                    break;
            }
        }
        
        private void DrawSpawnModeLabel()
        {
            if (_targetNode.spawnMode == CivilSpawnMode.CameraFrustumEdge)
            {
                Handles.BeginGUI();
                Vector3 labelWorldPos = _targetNode.transform.position + Vector3.up * 1.5f;
                var labelPos = HandleUtility.WorldToGUIPoint(labelWorldPos);
                
                GUIStyle style = new GUIStyle(EditorStyles.helpBox);
                style.alignment = TextAnchor.MiddleCenter;
                style.normal.textColor = new Color(0.2f, 0.8f, 1f);
                style.fontStyle = FontStyle.Bold;
                
                GUI.Label(new Rect(labelPos.x - 80, labelPos.y - 15, 160, 30), 
                    "📷 Camera Frustum Spawn", style);
                Handles.EndGUI();
            }
        }
        
        private void HandleConnectionCreation()
        {
            Event e = Event.current;
            
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Raycast to find clicked waypoint
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                RaycastHit hit;
                
                if (Physics.Raycast(ray, out hit, 1000f))
                {
                    var clickedNode = hit.collider.GetComponent<CivilWaypointNode>();
                    if (clickedNode == null)
                    {
                        clickedNode = hit.collider.GetComponentInParent<CivilWaypointNode>();
                    }
                    
                    if (clickedNode != null && clickedNode != _connectionSource)
                    {
                        CreateConnection(_connectionSource, clickedNode);
                        _isCreatingConnection = false;
                        _connectionSource = null;
                        e.Use();
                    }
                }
            }
            
            // Draw line from source to mouse position
            Vector3 mouseWorldPos = HandleUtility.GUIPointToWorldRay(e.mousePosition).GetPoint(10f);
            Handles.color = Color.yellow;
            Handles.DrawDottedLine(_targetNode.transform.position, mouseWorldPos, 4f);
            
            // Info label
            Handles.BeginGUI();
            var labelPos = HandleUtility.WorldToGUIPoint(_targetNode.transform.position + Vector3.up * 2f);
            GUI.Label(new Rect(labelPos.x - 100, labelPos.y - 30, 200, 20), 
                "Click another waypoint to connect", EditorStyles.helpBox);
            Handles.EndGUI();
            
            SceneView.RepaintAll();
        }
        
        private void CreateConnection(CivilWaypointNode from, CivilWaypointNode to)
        {
            Undo.RecordObject(from, "Create Waypoint Connection");
            
            // Check if connection already exists
            foreach (var conn in from.connections)
            {
                if (conn.target == to)
                {
                    Debug.LogWarning($"[Civil Waypoints] Connection already exists between {from.name} and {to.name}");
                    return;
                }
            }
            
            from.connections.Add(new CivilWaypointConnection
            {
                target = to,
                weight = 1f,
                bidirectional = true,
                channelWidth = from.defaultChannelWidth
            });
            
            EditorUtility.SetDirty(from);
            Debug.Log($"[Civil Waypoints] Created connection from {from.name} to {to.name}");
        }
        
        private void DrawChannelWidthHandle()
        {
            Vector3 pos = _targetNode.transform.position;
            float width = _targetNode.defaultChannelWidth;
            
            // Draw disc handle for channel width
            Handles.color = GetTypeColor(_targetNode.waypointType);
            
            EditorGUI.BeginChangeCheck();
            float newWidth = Handles.RadiusHandle(Quaternion.identity, pos, width * 0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetNode, "Change Channel Width");
                _targetNode.defaultChannelWidth = newWidth * 2f;
                EditorUtility.SetDirty(_targetNode);
            }
        }
        
        private void DrawArrivalRadiusHandle()
        {
            Vector3 pos = _targetNode.transform.position;
            
            Handles.color = new Color(1f, 1f, 0f, 0.3f);
            Handles.DrawSolidDisc(pos, Vector3.up, _targetNode.arrivalRadius);
            
            Handles.color = new Color(1f, 1f, 0f, 1f);
            Handles.DrawWireDisc(pos, Vector3.up, _targetNode.arrivalRadius);
        }
        
        private void DrawExploreRadiusHandle()
        {
            Vector3 pos = _targetNode.transform.position;
            
            Handles.color = new Color(ExploreColor.r, ExploreColor.g, ExploreColor.b, 0.1f);
            Handles.DrawSolidDisc(pos, Vector3.up, _targetNode.exploreRadius);
            
            Handles.color = ExploreColor;
            EditorGUI.BeginChangeCheck();
            float newRadius = Handles.RadiusHandle(Quaternion.identity, pos, _targetNode.exploreRadius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetNode, "Change Explore Radius");
                _targetNode.exploreRadius = newRadius;
                EditorUtility.SetDirty(_targetNode);
            }
        }
        
        private void DrawActivityHandles()
        {
            // Activity position handle
            if (_targetNode.activityPosition != null)
            {
                Handles.color = ActivityColor;
                Handles.DrawDottedLine(_targetNode.transform.position, _targetNode.activityPosition.position, 4f);
                Handles.SphereHandleCap(0, _targetNode.activityPosition.position, Quaternion.identity, 0.3f, EventType.Repaint);
            }
            
            // Activity facing handle
            if (_targetNode.activityFacing != null)
            {
                Handles.color = Color.cyan;
                Handles.ArrowHandleCap(0, _targetNode.activityFacing.position, 
                    _targetNode.activityFacing.rotation, 1f, EventType.Repaint);
            }
        }
        
        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Path Waypoint", false, 60)]
        private static void CreatePathWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Path, menuCommand);
        }
        
        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Explore Waypoint", false, 61)]
        private static void CreateExploreWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Explore, menuCommand);
        }
        
        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Despawn Waypoint", false, 62)]
        private static void CreateDespawnWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Despawn, menuCommand);
        }
        
        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Activity Waypoint", false, 63)]
        private static void CreateActivityWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Activity, menuCommand);
        }
        
        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Spawn Waypoint", false, 64)]
        private static void CreateSpawnWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Spawn, menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Lean Waypoint", false, 65)]
        private static void CreateLeanWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Lean, menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Civil Waypoint/Sit Waypoint", false, 66)]
        private static void CreateSitWaypoint(MenuCommand menuCommand)
        {
            CreateWaypoint(CivilWaypointType.Sit, menuCommand);
        }
        
        private static void CreateWaypoint(CivilWaypointType type, MenuCommand menuCommand)
        {
            // Create waypoint GameObject
            GameObject waypointGO = new GameObject($"CivilWaypoint_{type}");
            
            // Add components
            var node = waypointGO.AddComponent<CivilWaypointNode>();
            node.waypointType = type;
            
            // Add a small collider for click detection
            var collider = waypointGO.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = true;
            
            // Parent to selected object or manager
            GameObjectUtility.SetParentAndAlign(waypointGO, menuCommand.context as GameObject);
            
            // Position in front of scene view camera
            if (SceneView.lastActiveSceneView != null)
            {
                waypointGO.transform.position = SceneView.lastActiveSceneView.pivot;
            }
            
            // Snap to NavMesh
            if (_snapToNavMesh)
            {
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(waypointGO.transform.position, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    waypointGO.transform.position = hit.position;
                }
            }
            
            // Register for undo
            Undo.RegisterCreatedObjectUndo(waypointGO, $"Create {type} Waypoint");
            
            // Select the new waypoint
            Selection.activeGameObject = waypointGO;
        }
    }
    
    /// <summary>
    /// Helper class for spacing in UI.
    /// </summary>
    internal class Space : VisualElement
    {
        public Space(float height)
        {
            style.height = height;
        }
    }
}
