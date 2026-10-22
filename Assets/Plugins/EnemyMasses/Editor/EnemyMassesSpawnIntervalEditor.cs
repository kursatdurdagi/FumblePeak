using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesSpawnInterval))]
    public class EnemyMassesSpawnIntervalEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesSpawnInterval _spawner;

        private SerializedProperty _crowdController;
        private SerializedProperty _enemyCrowdIndex;
        private SerializedProperty _spawnCount;
        private SerializedProperty _spawnFormation;
        private SerializedProperty _spawnFormationSpacing;
        private SerializedProperty _startSpawningOnStart;
        private SerializedProperty _startDelay;
        private SerializedProperty _spawnInterval;
        private SerializedProperty _maxWaves;
        private SerializedProperty _spawnLocation;
        private SerializedProperty _useThisTransformIfNoLocation;
        private SerializedProperty _autoSetDestination;
        private SerializedProperty _destinationOverride;
        private SerializedProperty _autoAttackTarget;
        private SerializedProperty _attackTarget;
        private SerializedProperty _showGizmos;
        private SerializedProperty _showFormationPreview;
        private SerializedProperty _customGizmoColor;

        private void OnEnable()
        {
            _spawner = (Runtime.EnemyMassesSpawnInterval)target;

            _crowdController = serializedObject.FindProperty("crowdController");
            _enemyCrowdIndex = serializedObject.FindProperty("enemyCrowdIndex");
            _spawnCount = serializedObject.FindProperty("spawnCount");
            _spawnFormation = serializedObject.FindProperty("spawnFormation");
            _spawnFormationSpacing = serializedObject.FindProperty("spawnFormationSpacing");
            _startSpawningOnStart = serializedObject.FindProperty("startSpawningOnStart");
            _startDelay = serializedObject.FindProperty("startDelay");
            _spawnInterval = serializedObject.FindProperty("spawnInterval");
            _maxWaves = serializedObject.FindProperty("maxWaves");
            _spawnLocation = serializedObject.FindProperty("spawnLocation");
            _useThisTransformIfNoLocation = serializedObject.FindProperty("useThisTransformIfNoLocation");
            _autoSetDestination = serializedObject.FindProperty("autoSetDestination");
            _destinationOverride = serializedObject.FindProperty("destinationOverride");
            _autoAttackTarget = serializedObject.FindProperty("autoAttackTarget");
            _attackTarget = serializedObject.FindProperty("attackTarget");
            _showGizmos = serializedObject.FindProperty("showGizmos");
            _showFormationPreview = serializedObject.FindProperty("showFormationPreview");
            _customGizmoColor = serializedObject.FindProperty("customGizmoColor");
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

            DrawIntervalSettings();
            EditorGUILayout.Space(5);

            DrawSpawnLocation();
            EditorGUILayout.Space(5);

            DrawPostSpawn();
            EditorGUILayout.Space(5);

            DrawVisuals();
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

            EditorGUILayout.LabelField("Enemy Masses Interval Spawner", titleStyle);

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

            EditorGUILayout.PropertyField(_spawnCount);
            EditorGUILayout.PropertyField(_spawnFormation);
            EditorGUILayout.PropertyField(_spawnFormationSpacing, new GUIContent("Formation Spacing"));

            EditorGUILayout.EndVertical();
        }

        private void DrawIntervalSettings()
        {
            EditorGUILayout.LabelField("Interval Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_startSpawningOnStart, new GUIContent("Auto Start"));
            EditorGUILayout.PropertyField(_startDelay);
            EditorGUILayout.PropertyField(_spawnInterval);
            EditorGUILayout.PropertyField(_maxWaves);

            if (_maxWaves.intValue == 0)
            {
                EditorGUILayout.HelpBox("Max Waves = 0 means infinite spawning.", MessageType.Info);
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

        private void DrawPostSpawn()
        {
            EditorGUILayout.LabelField("Post Spawn Behavior", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_autoSetDestination);
            if (_autoSetDestination.boolValue)
            {
                EditorGUILayout.PropertyField(_destinationOverride);
            }

            EditorGUILayout.PropertyField(_autoAttackTarget);
            if (_autoAttackTarget.boolValue)
            {
                EditorGUILayout.PropertyField(_attackTarget);
                if (_attackTarget.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("If empty, falls back to PlayerTarget from the controller.", MessageType.Info);
                }
            }

            if (_autoSetDestination.boolValue && _autoAttackTarget.boolValue)
            {
                EditorGUILayout.HelpBox("Auto-Attack takes priority over Auto-Set Destination when enabled.", MessageType.Info);
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
                if (GUILayout.Button("Spawn Now", GUILayout.Height(24)))
                {
                    _spawner.SpawnNow();
                }
                if (GUILayout.Button("Reset Wave Counter", GUILayout.Height(24)))
                {
                    _spawner.ResetWaveCounter();
                }
                EditorGUILayout.EndHorizontal();

                if (_spawner.maxWaves > 0 && _spawner.WavesSpawned >= _spawner.maxWaves)
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

        private void CreateSpawnLocationAtSpawner()
        {
            if (_spawner == null)
                return;

            GameObject spawnPoint = new GameObject("Interval Spawn Location");
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
