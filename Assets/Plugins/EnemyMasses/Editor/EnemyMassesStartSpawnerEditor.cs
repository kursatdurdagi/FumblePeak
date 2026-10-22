using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesStartSpawner))]
    public class EnemyMassesStartSpawnerEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesStartSpawner _spawner;

        private SerializedProperty _crowdController;
        private SerializedProperty _enemyCrowdIndex;
        private SerializedProperty _spawnCount;
        private SerializedProperty _spawnFormation;
        private SerializedProperty _spawnFormationSpacing;
        private SerializedProperty _spawnOnStart;
        private SerializedProperty _spawnDelay;
        private SerializedProperty _spawnOnce;
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
            _spawner = (Runtime.EnemyMassesStartSpawner)target;

            _crowdController = serializedObject.FindProperty("crowdController");
            _enemyCrowdIndex = serializedObject.FindProperty("enemyCrowdIndex");
            _spawnCount = serializedObject.FindProperty("spawnCount");
            _spawnFormation = serializedObject.FindProperty("spawnFormation");
            _spawnFormationSpacing = serializedObject.FindProperty("spawnFormationSpacing");
            _spawnOnStart = serializedObject.FindProperty("spawnOnStart");
            _spawnDelay = serializedObject.FindProperty("spawnDelay");
            _spawnOnce = serializedObject.FindProperty("spawnOnce");
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
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawSpawnSettings();
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

            EditorGUILayout.LabelField("Enemy Masses Start Spawner", titleStyle);

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
            EditorGUILayout.PropertyField(_spawnOnStart);
            if (_spawnOnStart.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_spawnDelay, new GUIContent("Spawn Delay (seconds)"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.PropertyField(_spawnOnce);

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
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeControls()
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (Application.isPlaying)
            {
                if (GUILayout.Button("Spawn Now", GUILayout.Height(24)))
                {
                    _spawner.SpawnNow();
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

            GameObject spawnPoint = new GameObject("Start Spawn Location");
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
