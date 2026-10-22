using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesSpawnPatrol))]
    public class EnemyMassesSpawnPatrolEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesSpawnPatrol _spawner;

        private SerializedProperty _crowdController;
        private SerializedProperty _enemyCrowdIndex;
        private SerializedProperty _spawnCount;
        private SerializedProperty _spawnFormation;
        private SerializedProperty _spawnFormationSpacing;
        private SerializedProperty _forceSpawnerFormation;
        private SerializedProperty _spawnMode;
        private SerializedProperty _startDelay;
        private SerializedProperty _spawnInterval;
        private SerializedProperty _maxWaves;
        private SerializedProperty _spawnLocation;
        private SerializedProperty _useThisTransformIfNoLocation;
        private SerializedProperty _patrolMode;
        private SerializedProperty _patrolCenter;
        private SerializedProperty _patrolRadius;
        private SerializedProperty _patrolArrivalDistance;
        private SerializedProperty _patrolMinTargetDistance;
        private SerializedProperty _patrolWaitTime;
        private SerializedProperty _patrolUpdateInterval;
        private SerializedProperty _staggerPatrolUpdates;
        private SerializedProperty _useCentralScheduler;
        private SerializedProperty _centralSchedulerMaxSpawnersPerFrame;
        private SerializedProperty _useFormationForPatrol;
        private SerializedProperty _pausePatrolWhenEngaged;
        private SerializedProperty _patrolWaypoints;
        private SerializedProperty _loopWaypoints;
        private SerializedProperty _randomizeWaypoints;
        private SerializedProperty _enableFactionBattleMode;
        private SerializedProperty _showGizmos;
        private SerializedProperty _showFormationPreview;
        private SerializedProperty _showPatrolRadius;
        private SerializedProperty _customGizmoColor;
        private SerializedProperty _debugPatrolLogs;
        private SerializedProperty _debugPatrolVerbose;
        private SerializedProperty _debugPatrolLogInterval;

        private void OnEnable()
        {
            // Guard against null target during script reloads or object destruction
            if (target == null)
                return;

            _spawner = target as Runtime.EnemyMassesSpawnPatrol;
            if (_spawner == null)
                return;

            _crowdController = serializedObject.FindProperty("crowdController");
            _enemyCrowdIndex = serializedObject.FindProperty("enemyCrowdIndex");
            _spawnCount = serializedObject.FindProperty("spawnCount");
            _spawnFormation = serializedObject.FindProperty("spawnFormation");
            _spawnFormationSpacing = serializedObject.FindProperty("spawnFormationSpacing");
            _forceSpawnerFormation = serializedObject.FindProperty("forceSpawnerFormation");
            _spawnMode = serializedObject.FindProperty("spawnMode");
            _startDelay = serializedObject.FindProperty("startDelay");
            _spawnInterval = serializedObject.FindProperty("spawnInterval");
            _maxWaves = serializedObject.FindProperty("maxWaves");
            _spawnLocation = serializedObject.FindProperty("spawnLocation");
            _useThisTransformIfNoLocation = serializedObject.FindProperty("useThisTransformIfNoLocation");
            _patrolMode = serializedObject.FindProperty("patrolMode");
            _patrolCenter = serializedObject.FindProperty("patrolCenter");
            _patrolRadius = serializedObject.FindProperty("patrolRadius");
            _patrolArrivalDistance = serializedObject.FindProperty("patrolArrivalDistance");
            _patrolMinTargetDistance = serializedObject.FindProperty("patrolMinTargetDistance");
            _patrolWaitTime = serializedObject.FindProperty("patrolWaitTime");
            _patrolUpdateInterval = serializedObject.FindProperty("patrolUpdateInterval");
            _staggerPatrolUpdates = serializedObject.FindProperty("staggerPatrolUpdates");
            _useCentralScheduler = serializedObject.FindProperty("useCentralScheduler");
            _centralSchedulerMaxSpawnersPerFrame = serializedObject.FindProperty("centralSchedulerMaxSpawnersPerFrame");
            _useFormationForPatrol = serializedObject.FindProperty("useFormationForPatrol");
            _pausePatrolWhenEngaged = serializedObject.FindProperty("pausePatrolWhenEngaged");
            _patrolWaypoints = serializedObject.FindProperty("patrolWaypoints");
            _loopWaypoints = serializedObject.FindProperty("loopWaypoints");
            _randomizeWaypoints = serializedObject.FindProperty("randomizeWaypoints");
            _enableFactionBattleMode = serializedObject.FindProperty("enableFactionBattleMode");
            _showGizmos = serializedObject.FindProperty("showGizmos");
            _showFormationPreview = serializedObject.FindProperty("showFormationPreview");
            _showPatrolRadius = serializedObject.FindProperty("showPatrolRadius");
            _customGizmoColor = serializedObject.FindProperty("customGizmoColor");
            _debugPatrolLogs = serializedObject.FindProperty("debugPatrolLogs");
            _debugPatrolVerbose = serializedObject.FindProperty("debugPatrolVerbose");
            _debugPatrolLogInterval = serializedObject.FindProperty("debugPatrolLogInterval");
        }

        public override void OnInspectorGUI()
        {
            if (_spawner == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeaderSection();
            EditorGUILayout.Space(10);

            DrawSpawnSettings();
            EditorGUILayout.Space(5);

            DrawSpawnTiming();
            EditorGUILayout.Space(5);

            DrawSpawnLocation();
            EditorGUILayout.Space(5);

            DrawPatrolSettings();
            EditorGUILayout.Space(5);

            DrawWaypointSettings();
            EditorGUILayout.Space(5);

            DrawCombatSettings();
            EditorGUILayout.Space(5);

            DrawVisuals();
            EditorGUILayout.Space(10);

            DrawDebugSettings();
            EditorGUILayout.Space(10);

            DrawRuntimeControls();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeaderSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            Color gizmoColor = _spawner.GetGizmoColor();
            GUI.color = gizmoColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Enemy Masses Patrol Spawner", titleStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawSpawnSettings()
        {
            EditorGUILayout.LabelField("Spawn Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_crowdController);

            if (_crowdController.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign an EnemyMassesCrowdController to enable spawning.", MessageType.Warning);
            }
            else
            {
                var controller = _crowdController.objectReferenceValue as Runtime.EnemyMassesCrowdController;
                if (controller != null && controller.EnemyCrowds != null)
                {
                    string[] factionNames = new string[controller.EnemyCrowds.Count];
                    for (int i = 0; i < controller.EnemyCrowds.Count; i++)
                    {
                        var crowd = controller.EnemyCrowds[i];
                        factionNames[i] = crowd != null ? $"{i}: {crowd.factionName} - {crowd.enemyTypeName}" : $"{i}: (null)";
                    }

                    int currentIndex = _enemyCrowdIndex.intValue;
                    int newIndex = EditorGUILayout.Popup("Enemy Crowd Faction", currentIndex, factionNames);
                    if (newIndex != currentIndex)
                    {
                        _enemyCrowdIndex.intValue = newIndex;
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(_enemyCrowdIndex);
                }
            }

            Runtime.EnemyCrowd selectedCrowd = null;
            int selectedIndex = _enemyCrowdIndex.intValue;
            if (_crowdController.objectReferenceValue is Runtime.EnemyMassesCrowdController crowdControllerRef &&
                crowdControllerRef.EnemyCrowds != null &&
                selectedIndex >= 0 && selectedIndex < crowdControllerRef.EnemyCrowds.Count)
            {
                selectedCrowd = crowdControllerRef.EnemyCrowds[selectedIndex];
            }

            EditorGUILayout.PropertyField(_spawnCount);
            EditorGUILayout.PropertyField(_spawnFormation);
            EditorGUILayout.PropertyField(_spawnFormationSpacing, new GUIContent("Formation Spacing"));
            EditorGUILayout.PropertyField(_forceSpawnerFormation, new GUIContent("Force Spawner Formation"));

            if (selectedCrowd != null && selectedCrowd.overrideFormation && !_forceSpawnerFormation.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "EnemyCrowd Override Formation is enabled, so the crowd's formation type will be used instead of the spawner setting.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSpawnTiming()
        {
            EditorGUILayout.LabelField("Spawn Timing", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_spawnMode);

            var mode = (Runtime.EnemyMassesSpawnPatrol.SpawnMode)_spawnMode.enumValueIndex;
            if (mode == Runtime.EnemyMassesSpawnPatrol.SpawnMode.StartOnStart)
            {
                EditorGUILayout.PropertyField(_startDelay);
                EditorGUILayout.HelpBox("Spawns once on start (after Start Delay).", MessageType.Info);
            }
            else if (mode == Runtime.EnemyMassesSpawnPatrol.SpawnMode.Interval)
            {
                EditorGUILayout.PropertyField(_startDelay);
                EditorGUILayout.PropertyField(_spawnInterval);
                EditorGUILayout.PropertyField(_maxWaves);
                if (_maxWaves.intValue == 0)
                {
                    EditorGUILayout.HelpBox("Max Waves = 0 means infinite spawning.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Manual mode does not auto-spawn. Use Spawn Now at runtime.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSpawnLocation()
        {
            EditorGUILayout.LabelField("Spawn Location", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_spawnLocation);

            if (_spawnLocation.objectReferenceValue == null)
            {
                EditorGUILayout.PropertyField(_useThisTransformIfNoLocation, new GUIContent("Use Spawner Position"));

                if (_useThisTransformIfNoLocation.boolValue)
                {
                    EditorGUILayout.HelpBox("Agents will spawn at this GameObject's position.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("No spawn location set! Agents will spawn at world origin.", MessageType.Warning);
                }

                if (GUILayout.Button("Create Spawn Location Here", GUILayout.Height(22)))
                {
                    CreateSpawnLocationAtSpawner();
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Spawn Location", GUILayout.Height(20)))
                {
                    Selection.activeObject = _spawnLocation.objectReferenceValue;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
                if (GUILayout.Button("Create at Spawner", GUILayout.Height(20)))
                {
                    CreateSpawnLocationAtSpawner();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPatrolSettings()
        {
            EditorGUILayout.LabelField("Patrol Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_patrolMode);
            EditorGUILayout.PropertyField(_useFormationForPatrol);
            EditorGUILayout.PropertyField(_pausePatrolWhenEngaged);
            EditorGUILayout.PropertyField(_patrolArrivalDistance);
            EditorGUILayout.PropertyField(_patrolMinTargetDistance);
            EditorGUILayout.PropertyField(_patrolWaitTime);
            EditorGUILayout.PropertyField(_patrolUpdateInterval);
            EditorGUILayout.PropertyField(_staggerPatrolUpdates);
            EditorGUILayout.PropertyField(_useCentralScheduler);
            if (_useCentralScheduler.boolValue)
            {
                EditorGUILayout.PropertyField(_centralSchedulerMaxSpawnersPerFrame);
                EditorGUILayout.HelpBox("Central scheduler spreads patrol updates across frames when many spawners are active.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawWaypointSettings()
        {
            var mode = (Runtime.EnemyMassesSpawnPatrol.PatrolMode)_patrolMode.enumValueIndex;

            EditorGUILayout.LabelField(mode == Runtime.EnemyMassesSpawnPatrol.PatrolMode.FreeRoam
                ? "Free Roam"
                : "Waypoints", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (mode == Runtime.EnemyMassesSpawnPatrol.PatrolMode.FreeRoam)
            {
                EditorGUILayout.PropertyField(_patrolCenter);
                EditorGUILayout.PropertyField(_patrolRadius);
                if (_patrolCenter.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("If empty, the spawn group origin is used.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.PropertyField(_patrolWaypoints, true);
                EditorGUILayout.PropertyField(_loopWaypoints);
                EditorGUILayout.PropertyField(_randomizeWaypoints);

                if (_patrolWaypoints.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("Assign at least one waypoint for patrol.", MessageType.Warning);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCombatSettings()
        {
            EditorGUILayout.LabelField("Combat", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableFactionBattleMode, new GUIContent("Enable Faction Battle Mode"));
            if (_enableFactionBattleMode.boolValue)
            {
                EditorGUILayout.HelpBox("Faction battle mode enables faction-vs-faction aggro. Player targets are still considered when the RTS controller and player-controlled factions are set.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVisuals()
        {
            EditorGUILayout.LabelField("Visual Feedback", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showGizmos);
            if (_showGizmos.boolValue)
            {
                EditorGUILayout.PropertyField(_showFormationPreview);
                EditorGUILayout.PropertyField(_showPatrolRadius);
                EditorGUILayout.PropertyField(_customGizmoColor);

                if (_customGizmoColor.colorValue == Color.clear)
                {
                    EditorGUILayout.HelpBox($"Using auto-color based on faction {_enemyCrowdIndex.intValue}.", MessageType.Info);
                    Color autoColor = _spawner.GetGizmoColor();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Preview Color:");
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(50, 18), autoColor);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeControls()
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (Application.isPlaying)
            {
                string status = _spawner.IsSpawning ? "Spawning" : "Idle";
                EditorGUILayout.LabelField($"Status: {status}");
                EditorGUILayout.LabelField($"Waves Spawned: {_spawner.WavesSpawned}");

                var mode = (Runtime.EnemyMassesSpawnPatrol.SpawnMode)_spawnMode.enumValueIndex;
                if (mode == Runtime.EnemyMassesSpawnPatrol.SpawnMode.Interval)
                {
                    if (_spawner.IsSpawning)
                    {
                        if (GUILayout.Button("Stop Spawning", GUILayout.Height(24)))
                        {
                            _spawner.StopSpawning();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Start Spawning", GUILayout.Height(24)))
                        {
                            _spawner.StartSpawning();
                        }
                    }
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Spawn Now", GUILayout.Height(24)))
                {
                    _spawner.SpawnNow();
                }
                if (GUILayout.Button("Reset Wave Counter", GUILayout.Height(24)))
                {
                    _spawner.ResetWaveCounter();
                }
                EditorGUILayout.EndHorizontal();

                if (mode == Runtime.EnemyMassesSpawnPatrol.SpawnMode.Interval &&
                    _spawner.maxWaves > 0 && _spawner.WavesSpawned >= _spawner.maxWaves)
                {
                    EditorGUILayout.HelpBox("Max waves reached. Reset wave counter to spawn again.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use runtime controls.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDebugSettings()
        {
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_debugPatrolLogs);
            if (_debugPatrolLogs.boolValue)
            {
                EditorGUILayout.PropertyField(_debugPatrolVerbose);
                EditorGUILayout.PropertyField(_debugPatrolLogInterval);
                EditorGUILayout.HelpBox("Enable verbose logs only when needed. Logs are throttled per patrol group.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateSpawnLocationAtSpawner()
        {
            if (_spawner == null)
                return;

            GameObject spawnPoint = new GameObject("Patrol Spawn Location");
            Undo.RegisterCreatedObjectUndo(spawnPoint, "Create Spawn Location");

            Transform spawnerTransform = _spawner.transform;
            spawnPoint.transform.SetPositionAndRotation(spawnerTransform.position, spawnerTransform.rotation);
            spawnPoint.transform.localScale = Vector3.one;

            _spawnLocation.objectReferenceValue = spawnPoint.transform;
            serializedObject.ApplyModifiedProperties();

            Selection.activeObject = spawnPoint;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}
