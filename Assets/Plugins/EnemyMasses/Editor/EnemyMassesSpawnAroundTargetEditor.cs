using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesSpawnAroundTarget))]
    public class EnemyMassesSpawnAroundTargetEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesSpawnAroundTarget _spawner;

        private SerializedProperty _crowdController;
        private SerializedProperty _enemyCrowdIndex;
        private SerializedProperty _totalSpawnCount;
        private SerializedProperty _spawnMode;
        private SerializedProperty _agentsPerWave;
        private SerializedProperty _spawnFormation;
        private SerializedProperty _spawnFormationSpacing;
        private SerializedProperty _startSpawningOnStart;
        private SerializedProperty _targetTransform;
        private SerializedProperty _spawnRadius;
        private SerializedProperty _minSpawnRadius;
        private SerializedProperty _uniformDistribution;
        private SerializedProperty _navMeshSearchRadius;
        private SerializedProperty _startDelay;
        private SerializedProperty _spawnInterval;
        private SerializedProperty _maxWaves;
        private SerializedProperty _autoAttackTarget;
        private SerializedProperty _autoMoveToTarget;
        private SerializedProperty _showGizmos;
        private SerializedProperty _customGizmoColor;
        private SerializedProperty _previewPointCount;

        // Free Roam properties
        private SerializedProperty _enableFreeRoam;
        private SerializedProperty _freeRoamCenterMode;
        private SerializedProperty _freeRoamCenterTransform;
        private SerializedProperty _freeRoamRadius;
        private SerializedProperty _freeRoamArrivalDistance;
        private SerializedProperty _freeRoamMinTargetDistance;
        private SerializedProperty _freeRoamWaitTime;
        private SerializedProperty _freeRoamUpdateInterval;
        private SerializedProperty _pauseFreeRoamWhenEngaged;
        private SerializedProperty _useFormationForFreeRoam;

        private void OnEnable()
        {
            if (target == null) return;
            
            _spawner = target as Runtime.EnemyMassesSpawnAroundTarget;
            if (_spawner == null) return;

            _crowdController = serializedObject.FindProperty("crowdController");
            _enemyCrowdIndex = serializedObject.FindProperty("enemyCrowdIndex");
            _totalSpawnCount = serializedObject.FindProperty("totalSpawnCount");
            _spawnMode = serializedObject.FindProperty("spawnMode");
            _agentsPerWave = serializedObject.FindProperty("agentsPerWave");
            _spawnFormation = serializedObject.FindProperty("spawnFormation");
            _spawnFormationSpacing = serializedObject.FindProperty("spawnFormationSpacing");
            _startSpawningOnStart = serializedObject.FindProperty("startSpawningOnStart");
            _targetTransform = serializedObject.FindProperty("targetTransform");
            _spawnRadius = serializedObject.FindProperty("spawnRadius");
            _minSpawnRadius = serializedObject.FindProperty("minSpawnRadius");
            _uniformDistribution = serializedObject.FindProperty("uniformDistribution");
            _navMeshSearchRadius = serializedObject.FindProperty("navMeshSearchRadius");
            _startDelay = serializedObject.FindProperty("startDelay");
            _spawnInterval = serializedObject.FindProperty("spawnInterval");
            _maxWaves = serializedObject.FindProperty("maxWaves");
            _autoAttackTarget = serializedObject.FindProperty("autoAttackTarget");
            _autoMoveToTarget = serializedObject.FindProperty("autoMoveToTarget");
            _showGizmos = serializedObject.FindProperty("showGizmos");
            _customGizmoColor = serializedObject.FindProperty("customGizmoColor");
            _previewPointCount = serializedObject.FindProperty("previewPointCount");

            // Free Roam properties
            _enableFreeRoam = serializedObject.FindProperty("enableFreeRoam");
            _freeRoamCenterMode = serializedObject.FindProperty("freeRoamCenterMode");
            _freeRoamCenterTransform = serializedObject.FindProperty("freeRoamCenterTransform");
            _freeRoamRadius = serializedObject.FindProperty("freeRoamRadius");
            _freeRoamArrivalDistance = serializedObject.FindProperty("freeRoamArrivalDistance");
            _freeRoamMinTargetDistance = serializedObject.FindProperty("freeRoamMinTargetDistance");
            _freeRoamWaitTime = serializedObject.FindProperty("freeRoamWaitTime");
            _freeRoamUpdateInterval = serializedObject.FindProperty("freeRoamUpdateInterval");
            _pauseFreeRoamWhenEngaged = serializedObject.FindProperty("pauseFreeRoamWhenEngaged");
            _useFormationForFreeRoam = serializedObject.FindProperty("useFormationForFreeRoam");
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
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawSpawnSettings();
            EditorGUILayout.Space(5);

            DrawTargetSettings();
            EditorGUILayout.Space(5);

            DrawIntervalSettings();
            EditorGUILayout.Space(5);

            DrawPostSpawnBehavior();
            EditorGUILayout.Space(5);

            DrawFreeRoamSettings();
            EditorGUILayout.Space(5);

            DrawVisualSettings();
            EditorGUILayout.Space(10);

            DrawRuntimeControls();

            serializedObject.ApplyModifiedProperties();
        }

        private new void DrawHeader()
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

            EditorGUILayout.LabelField("Enemy Masses Spawn Around Target", titleStyle);

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

            EditorGUILayout.PropertyField(_spawnMode);

            var mode = (Runtime.EnemyMassesSpawnAroundTarget.SpawnMode)_spawnMode.enumValueIndex;
            
            // Total Agents only applies to AllAtOnce mode
            if (mode == Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.AllAtOnce)
            {
                EditorGUILayout.PropertyField(_totalSpawnCount, new GUIContent("Total Agents"));
            }
            
            // Agents Per Wave for IntervalWaves mode
            if (mode == Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.IntervalWaves)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_agentsPerWave);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(_spawnFormation);
            EditorGUILayout.PropertyField(_spawnFormationSpacing, new GUIContent("Formation Spacing"));

            EditorGUILayout.EndVertical();
        }

        private void DrawTargetSettings()
        {
            EditorGUILayout.LabelField("Target Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_targetTransform, new GUIContent("Target"));

            if (_targetTransform.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a target Transform. Agents will spawn around this position.", MessageType.Warning);

                if (GUILayout.Button("Create Target Here", GUILayout.Height(22)))
                {
                    CreateTargetAtSpawner();
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Target", GUILayout.Height(20)))
                {
                    Selection.activeObject = _targetTransform.objectReferenceValue;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
                if (GUILayout.Button("Create at Spawner", GUILayout.Height(20)))
                {
                    CreateTargetAtSpawner();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            // Radius settings with visual slider
            EditorGUILayout.LabelField("Spawn Radius", EditorStyles.miniLabel);
            
            float minRadius = _minSpawnRadius.floatValue;
            float maxRadius = _spawnRadius.floatValue;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Min", GUILayout.Width(30));
            minRadius = EditorGUILayout.FloatField(minRadius, GUILayout.Width(50));
            EditorGUILayout.MinMaxSlider(ref minRadius, ref maxRadius, 0f, 100f);
            EditorGUILayout.LabelField("Max", GUILayout.Width(30));
            maxRadius = EditorGUILayout.FloatField(maxRadius, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
            
            _minSpawnRadius.floatValue = Mathf.Clamp(minRadius, 0f, maxRadius - 0.1f);
            _spawnRadius.floatValue = Mathf.Max(0.1f, maxRadius);

            if (_minSpawnRadius.floatValue > 0f)
            {
                EditorGUILayout.HelpBox($"Agents spawn in a ring: {_minSpawnRadius.floatValue:F1}m to {_spawnRadius.floatValue:F1}m from target.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"Agents spawn within {_spawnRadius.floatValue:F1}m of target.", MessageType.Info);
            }

            EditorGUILayout.Space(3);
            EditorGUILayout.PropertyField(_uniformDistribution, new GUIContent("Uniform Distribution"));
            if (!_uniformDistribution.boolValue)
            {
                EditorGUILayout.HelpBox("Linear distribution: more agents near the outer edge.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(_navMeshSearchRadius, new GUIContent("NavMesh Search Range"));

            EditorGUILayout.EndVertical();
        }

        private void DrawIntervalSettings()
        {
            EditorGUILayout.LabelField("Interval Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_startSpawningOnStart, new GUIContent("Auto Start"));
            EditorGUILayout.PropertyField(_startDelay);

            var mode = (Runtime.EnemyMassesSpawnAroundTarget.SpawnMode)_spawnMode.enumValueIndex;
            if (mode != Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.AllAtOnce)
            {
                EditorGUILayout.PropertyField(_spawnInterval);
                EditorGUILayout.PropertyField(_maxWaves, new GUIContent("Max Waves", "0 = unlimited waves"));

                int perWave = mode == Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.IntervalWaves 
                    ? _agentsPerWave.intValue 
                    : 1;
                int maxWavesVal = _maxWaves.intValue;

                string intervalDesc = mode switch
                {
                    Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.IntervalWaves => 
                        $"Spawns {perWave} agents every {_spawnInterval.floatValue:F1}s",
                    Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.IndividualAtIntervals => 
                        $"Spawns 1 agent every {_spawnInterval.floatValue:F1}s",
                    _ => ""
                };
                
                if (!string.IsNullOrEmpty(intervalDesc))
                {
                    string waveLimitText = maxWavesVal > 0 
                        ? $" (stops after {maxWavesVal} waves = {maxWavesVal * perWave} agents)" 
                        : " (unlimited waves)";
                    EditorGUILayout.HelpBox(intervalDesc + waveLimitText, MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox($"All {_totalSpawnCount.intValue} agents spawn immediately (in batches to prevent freezing).", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPostSpawnBehavior()
        {
            EditorGUILayout.LabelField("Post Spawn Behavior", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_autoAttackTarget, new GUIContent("Attack Target"));
            if (_autoAttackTarget.boolValue)
            {
                EditorGUILayout.HelpBox("Spawned agents will attack the target Transform.", MessageType.Info);
            }

            if (!_autoAttackTarget.boolValue)
            {
                EditorGUILayout.PropertyField(_autoMoveToTarget, new GUIContent("Move To Target"));
                if (_autoMoveToTarget.boolValue)
                {
                    EditorGUILayout.HelpBox("Spawned agents will move toward the target position.", MessageType.Info);
                }
            }

            if (_autoAttackTarget.boolValue && _autoMoveToTarget.boolValue)
            {
                EditorGUILayout.HelpBox("Attack Target takes priority over Move To Target.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFreeRoamSettings()
        {
            EditorGUILayout.LabelField("Free Roam (Post-Spawn Patrol)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableFreeRoam, new GUIContent("Enable Free Roam"));

            if (_enableFreeRoam.boolValue)
            {
                // Show warning if other post-spawn behaviors are enabled
                if (_autoAttackTarget.boolValue || _autoMoveToTarget.boolValue)
                {
                    EditorGUILayout.HelpBox("Free Roam will override Attack Target and Move To Target behaviors.", MessageType.Info);
                }

                EditorGUILayout.Space(5);

                // Center Mode
                EditorGUILayout.PropertyField(_freeRoamCenterMode, new GUIContent("Center Mode"));

                var centerMode = (Runtime.FreeRoamCenterMode)_freeRoamCenterMode.enumValueIndex;
                
                // Show help based on selected mode
                string centerModeHelp = centerMode switch
                {
                    Runtime.FreeRoamCenterMode.SpawnPosition => 
                        "Patrol center locked at spawn position. Ideal when target is a moving player.",
                    Runtime.FreeRoamCenterMode.TargetPositionAtSpawn => 
                        "Patrol center locked at target's position when spawned.",
                    Runtime.FreeRoamCenterMode.CustomTransform => 
                        "Patrol center follows a custom Transform.",
                    Runtime.FreeRoamCenterMode.FollowTarget => 
                        "Patrol area moves with the target. Agents stay near moving targets.",
                    _ => ""
                };
                
                if (!string.IsNullOrEmpty(centerModeHelp))
                {
                    EditorGUILayout.HelpBox(centerModeHelp, MessageType.Info);
                }

                // Show custom transform field only when needed
                if (centerMode == Runtime.FreeRoamCenterMode.CustomTransform)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_freeRoamCenterTransform, new GUIContent("Center Transform"));
                    if (_freeRoamCenterTransform.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Assign a Transform or it will fall back to spawn position.", MessageType.Warning);
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);

                // Radius
                EditorGUILayout.PropertyField(_freeRoamRadius, new GUIContent("Patrol Radius"));
                
                // Draw a visual comparison of spawn vs free roam radius
                float spawnRad = _spawnRadius.floatValue;
                float freeRoamRad = _freeRoamRadius.floatValue;
                if (Mathf.Abs(spawnRad - freeRoamRad) > 1f)
                {
                    string comparison = freeRoamRad > spawnRad 
                        ? $"Patrol area ({freeRoamRad:F0}m) is larger than spawn area ({spawnRad:F0}m)."
                        : $"Patrol area ({freeRoamRad:F0}m) is smaller than spawn area ({spawnRad:F0}m).";
                    EditorGUILayout.HelpBox(comparison, MessageType.Info);
                }

                EditorGUILayout.Space(5);

                // Movement settings
                EditorGUILayout.LabelField("Movement Settings", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_freeRoamArrivalDistance, new GUIContent("Arrival Distance"));
                EditorGUILayout.PropertyField(_freeRoamMinTargetDistance, new GUIContent("Min Target Distance"));
                if (_freeRoamMinTargetDistance.floatValue == 0f)
                {
                    EditorGUILayout.HelpBox("Auto: min distance = arrival distance × 2", MessageType.None);
                }
                EditorGUILayout.PropertyField(_freeRoamWaitTime, new GUIContent("Wait Time"));
                EditorGUILayout.PropertyField(_freeRoamUpdateInterval, new GUIContent("Update Interval"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);

                // Behavior toggles
                EditorGUILayout.LabelField("Behavior", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_pauseFreeRoamWhenEngaged, new GUIContent("Pause When Engaged"));
                if (_pauseFreeRoamWhenEngaged.boolValue)
                {
                    EditorGUILayout.HelpBox("Patrol pauses when agents are in combat.", MessageType.None);
                }
                EditorGUILayout.PropertyField(_useFormationForFreeRoam, new GUIContent("Use Formation"));
                if (_useFormationForFreeRoam.boolValue)
                {
                    EditorGUILayout.HelpBox("Agents maintain formation while patrolling.", MessageType.None);
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("Enable to make spawned agents patrol randomly within a radius.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVisualSettings()
        {
            EditorGUILayout.LabelField("Visual Feedback", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showGizmos);
            
            if (_showGizmos.boolValue)
            {
                EditorGUILayout.PropertyField(_previewPointCount, new GUIContent("Preview Points"));
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
                
                var mode = (Runtime.EnemyMassesSpawnAroundTarget.SpawnMode)_spawnMode.enumValueIndex;
                
                // Show different info based on mode
                if (mode == Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.AllAtOnce)
                {
                    EditorGUILayout.LabelField($"Agents Spawned: {_spawner.AgentsSpawned} / {_spawner.totalSpawnCount}");
                    
                    // Progress bar for AllAtOnce
                    float progress = _spawner.totalSpawnCount > 0 
                        ? (float)_spawner.AgentsSpawned / _spawner.totalSpawnCount 
                        : 0f;
                    Rect progressRect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(progressRect, progress, $"{progress * 100:F0}% Complete");
                }
                else
                {
                    // For interval modes, show wave count and total agents spawned
                    EditorGUILayout.LabelField($"Agents Spawned: {_spawner.AgentsSpawned}");
                    
                    string wavesText = _spawner.maxWaves > 0 
                        ? $"Waves Spawned: {_spawner.WavesSpawned} / {_spawner.maxWaves}"
                        : $"Waves Spawned: {_spawner.WavesSpawned} (unlimited)";
                    EditorGUILayout.LabelField(wavesText);
                    
                    // Progress bar for interval modes (if max waves set)
                    if (_spawner.maxWaves > 0)
                    {
                        float progress = (float)_spawner.WavesSpawned / _spawner.maxWaves;
                        Rect progressRect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                        EditorGUI.ProgressBar(progressRect, progress, $"{_spawner.WavesSpawned}/{_spawner.maxWaves} waves");
                    }
                }

                EditorGUILayout.Space(5);

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

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Spawn Wave", GUILayout.Height(24)))
                {
                    _spawner.SpawnWaveNow();
                }
                if (GUILayout.Button("Spawn Single", GUILayout.Height(24)))
                {
                    _spawner.SpawnSingleNow();
                }
                EditorGUILayout.EndHorizontal();

                // Reset buttons - different for each mode
                if (mode == Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.AllAtOnce)
                {
                    if (GUILayout.Button("Reset Spawn Counter", GUILayout.Height(22)))
                    {
                        _spawner.ResetSpawnCounter();
                    }
                }
                else
                {
                    if (GUILayout.Button("Reset Wave Counter", GUILayout.Height(22)))
                    {
                        _spawner.ResetWaveCounter();
                    }
                }

                // Show completion messages
                if (mode == Runtime.EnemyMassesSpawnAroundTarget.SpawnMode.AllAtOnce)
                {
                    if (_spawner.AgentsSpawned >= _spawner.totalSpawnCount)
                    {
                        EditorGUILayout.HelpBox("All agents spawned. Reset spawn counter to spawn again.", MessageType.Info);
                    }
                }
                else
                {
                    if (_spawner.maxWaves > 0 && _spawner.WavesSpawned >= _spawner.maxWaves)
                    {
                        EditorGUILayout.HelpBox("Max waves reached. Reset wave counter to spawn again.", MessageType.Info);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use runtime controls.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateTargetAtSpawner()
        {
            if (_spawner == null)
                return;

            GameObject targetGO = new GameObject("Spawn Target");
            Undo.RegisterCreatedObjectUndo(targetGO, "Create Spawn Target");

            Transform spawnerTransform = _spawner.transform;
            targetGO.transform.SetPositionAndRotation(spawnerTransform.position, spawnerTransform.rotation);
            targetGO.transform.localScale = Vector3.one;

            _targetTransform.objectReferenceValue = targetGO.transform;
            serializedObject.ApplyModifiedProperties();

            Selection.activeObject = targetGO;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}
