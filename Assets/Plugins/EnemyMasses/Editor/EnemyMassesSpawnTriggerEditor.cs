using UnityEngine;
using UnityEditor;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesSpawnTrigger))]
    public class EnemyMassesSpawnTriggerEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesSpawnTrigger _trigger;

        private SerializedProperty _crowdController;
        private SerializedProperty _enemyCrowdIndex;
        private SerializedProperty _spawnCount;
        private SerializedProperty _spawnFormation;
        private SerializedProperty _spawnLocation;
        private SerializedProperty _useThisTransformIfNoLocation;
        private SerializedProperty _activationType;
        private SerializedProperty _requiredTag;
        private SerializedProperty _requiredLayer;
        private SerializedProperty _triggerShape;
        private SerializedProperty _triggerOnce;
        private SerializedProperty _triggerCooldown;
        private SerializedProperty _autoSetDestination;
        private SerializedProperty _autoAttackTriggeringObject;
        private SerializedProperty _showGizmos;
        private SerializedProperty _customGizmoColor;

        private void OnEnable()
        {
            // Guard against null target during domain reload or object deletion
            if (target == null) return;
            
            _trigger = target as Runtime.EnemyMassesSpawnTrigger;
            if (_trigger == null) return;

            _crowdController = serializedObject.FindProperty("crowdController");
            _enemyCrowdIndex = serializedObject.FindProperty("enemyCrowdIndex");
            _spawnCount = serializedObject.FindProperty("spawnCount");
            _spawnFormation = serializedObject.FindProperty("spawnFormation");
            _spawnLocation = serializedObject.FindProperty("spawnLocation");
            _useThisTransformIfNoLocation = serializedObject.FindProperty("useThisTransformIfNoLocation");
            _activationType = serializedObject.FindProperty("activationType");
            _requiredTag = serializedObject.FindProperty("requiredTag");
            _requiredLayer = serializedObject.FindProperty("requiredLayer");
            _triggerShape = serializedObject.FindProperty("triggerShape");
            _triggerOnce = serializedObject.FindProperty("triggerOnce");
            _triggerCooldown = serializedObject.FindProperty("triggerCooldown");
            _autoSetDestination = serializedObject.FindProperty("autoSetDestination");
            _autoAttackTriggeringObject = serializedObject.FindProperty("autoAttackTriggeringObject");
            _showGizmos = serializedObject.FindProperty("showGizmos");
            _customGizmoColor = serializedObject.FindProperty("customGizmoColor");
            
            // Ensure collider matches trigger shape on load
            ValidateCollider();
        }
        
        private void ValidateCollider()
        {
            if (_trigger == null) return;
            
            Collider currentCollider = _trigger.GetComponent<Collider>();
            Runtime.EnemyMassesSpawnTrigger.TriggerShape shape = (Runtime.EnemyMassesSpawnTrigger.TriggerShape)_triggerShape.enumValueIndex;
            
            bool needsSync = false;
            if (currentCollider == null)
            {
                needsSync = true;
            }
            else
            {
                needsSync = (shape == Runtime.EnemyMassesSpawnTrigger.TriggerShape.Box && !(currentCollider is BoxCollider)) ||
                           (shape == Runtime.EnemyMassesSpawnTrigger.TriggerShape.Sphere && !(currentCollider is SphereCollider)) ||
                           (shape == Runtime.EnemyMassesSpawnTrigger.TriggerShape.Capsule && !(currentCollider is CapsuleCollider));
            }
            
            if (needsSync)
            {
                _trigger.SyncColliderToShape();
                EditorUtility.SetDirty(_trigger.gameObject);
            }
        }

        public override void OnInspectorGUI()
        {
            // Guard against null target
            if (_trigger == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }
            
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawTriggerSettings();
            EditorGUILayout.Space(5);

            DrawSpawnLocationSettings();
            EditorGUILayout.Space(5);

            DrawTriggerFilters();
            EditorGUILayout.Space(5);

            DrawTriggerBehavior();
            EditorGUILayout.Space(5);

            DrawVisualSettings();
            EditorGUILayout.Space(10);

            DrawRuntimeControls();
            EditorGUILayout.Space(5);

            DrawQuickSetupButtons();

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

            Color gizmoColor = _trigger.GetGizmoColor();
            GUI.color = gizmoColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Enemy Masses Spawn Trigger", titleStyle);
            
            EditorGUILayout.EndVertical();
        }

        private void DrawTriggerSettings()
        {
            EditorGUILayout.LabelField("Trigger Settings", EditorStyles.boldLabel);
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
                    // Show faction dropdown
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

            EditorGUILayout.EndVertical();
        }

        private void DrawSpawnLocationSettings()
        {
            EditorGUILayout.LabelField("Spawn Location", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_spawnLocation);

            if (_spawnLocation.objectReferenceValue == null)
            {
                EditorGUILayout.PropertyField(_useThisTransformIfNoLocation, new GUIContent("Use Trigger Position"));
                
                if (_useThisTransformIfNoLocation.boolValue)
                {
                    EditorGUILayout.HelpBox("Agents will spawn at this trigger's position.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("No spawn location set! Agents will spawn at world origin.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Spawn Location", GUILayout.Height(20)))
                {
                    Selection.activeGameObject = ((Transform)_spawnLocation.objectReferenceValue).gameObject;
                    SceneView.lastActiveSceneView.FrameSelected();
                }
                if (GUILayout.Button("Create at Trigger", GUILayout.Height(20)))
                {
                    CreateSpawnLocationAtTrigger();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTriggerFilters()
        {
            EditorGUILayout.LabelField("Trigger Filters", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_activationType);

            Runtime.EnemyMassesSpawnTrigger.TriggerActivationType type = 
                (Runtime.EnemyMassesSpawnTrigger.TriggerActivationType)_activationType.enumValueIndex;

            switch (type)
            {
                case Runtime.EnemyMassesSpawnTrigger.TriggerActivationType.PlayerTarget:
                    EditorGUILayout.HelpBox("Triggers when the Player Target from the Crowd Controller enters.", MessageType.Info);
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerActivationType.Tag:
                    EditorGUILayout.PropertyField(_requiredTag);
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerActivationType.Layer:
                    EditorGUILayout.PropertyField(_requiredLayer);
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerActivationType.TagAndLayer:
                    EditorGUILayout.PropertyField(_requiredTag);
                    EditorGUILayout.PropertyField(_requiredLayer);
                    EditorGUILayout.HelpBox("Object must match BOTH tag AND layer.", MessageType.Info);
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerActivationType.TagOrLayer:
                    EditorGUILayout.PropertyField(_requiredTag);
                    EditorGUILayout.PropertyField(_requiredLayer);
                    EditorGUILayout.HelpBox("Object must match EITHER tag OR layer.", MessageType.Info);
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerActivationType.Any:
                    EditorGUILayout.HelpBox("Any object entering will trigger spawning.", MessageType.Warning);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTriggerBehavior()
        {
            EditorGUILayout.LabelField("Trigger Behavior", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_triggerShape);
            if (EditorGUI.EndChangeCheck())
            {
                // Sync collider when shape changes
                serializedObject.ApplyModifiedProperties();
                var trigger = target as Runtime.EnemyMassesSpawnTrigger;
                if (trigger != null)
                {
                    trigger.SyncColliderToShape();
                    EditorUtility.SetDirty(trigger.gameObject);
                }
            }
            
            // Show info about auto-sync
            EditorGUILayout.HelpBox("Collider type automatically syncs with Trigger Shape. No manual collider setup needed.", MessageType.Info);
            
            EditorGUILayout.PropertyField(_triggerOnce);

            if (!_triggerOnce.boolValue)
            {
                EditorGUILayout.PropertyField(_triggerCooldown);
            }

            EditorGUILayout.Space(5);
            
            EditorGUILayout.PropertyField(_autoSetDestination);
            EditorGUILayout.PropertyField(_autoAttackTriggeringObject);
            
            // Show warning if both are enabled
            if (_autoSetDestination.boolValue && _autoAttackTriggeringObject.boolValue)
            {
                EditorGUILayout.HelpBox("Auto-Attack takes priority over Auto-Set Destination when enabled.", MessageType.Info);
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
                EditorGUILayout.PropertyField(_customGizmoColor);

                if (_customGizmoColor.colorValue == Color.clear)
                {
                    EditorGUILayout.HelpBox($"Using auto-color based on faction {_enemyCrowdIndex.intValue}.", MessageType.Info);
                    
                    // Show color preview
                    Color autoColor = _trigger.GetGizmoColor();
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
            if (!Application.isPlaying)
                return;

            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Manual Trigger", GUILayout.Height(30)))
            {
                _trigger.ManualTrigger();
            }

            if (GUILayout.Button("Reset Trigger", GUILayout.Height(30)))
            {
                _trigger.ResetTrigger();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawQuickSetupButtons()
        {
            EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Create Spawn Location", GUILayout.Height(25)))
            {
                CreateSpawnLocation();
            }

            if (GUILayout.Button("Auto-Find Controller", GUILayout.Height(25)))
            {
                AutoFindController();
            }

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Setup Box Trigger (5×5×5)", GUILayout.Height(25)))
            {
                SetupCollider(Runtime.EnemyMassesSpawnTrigger.TriggerShape.Box, new Vector3(5, 5, 5));
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Sphere (r=3)", GUILayout.Height(25)))
            {
                SetupCollider(Runtime.EnemyMassesSpawnTrigger.TriggerShape.Sphere, 3f);
            }

            if (GUILayout.Button("Capsule (r=2, h=4)", GUILayout.Height(25)))
            {
                SetupCollider(Runtime.EnemyMassesSpawnTrigger.TriggerShape.Capsule, 2f, 4f);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void CreateSpawnLocation()
        {
            GameObject spawnObj = new GameObject($"{_trigger.gameObject.name}_SpawnLocation");
            spawnObj.transform.position = _trigger.transform.position + _trigger.transform.forward * 10f;
            spawnObj.transform.rotation = _trigger.transform.rotation;

            Undo.RegisterCreatedObjectUndo(spawnObj, "Create Spawn Location");

            _spawnLocation.objectReferenceValue = spawnObj.transform;
            serializedObject.ApplyModifiedProperties();

            Selection.activeGameObject = spawnObj;
            SceneView.lastActiveSceneView.FrameSelected();
        }

        private void CreateSpawnLocationAtTrigger()
        {
            GameObject spawnObj = new GameObject($"{_trigger.gameObject.name}_SpawnLocation");
            spawnObj.transform.position = _trigger.transform.position;
            spawnObj.transform.rotation = _trigger.transform.rotation;

            Undo.RegisterCreatedObjectUndo(spawnObj, "Create Spawn Location at Trigger");

            _spawnLocation.objectReferenceValue = spawnObj.transform;
            serializedObject.ApplyModifiedProperties();
        }

        private void AutoFindController()
        {
            var controller = FindFirstObjectByType<Runtime.EnemyMassesCrowdController>();
            if (controller != null)
            {
                _crowdController.objectReferenceValue = controller;
                serializedObject.ApplyModifiedProperties();
                Debug.Log($"Found EnemyMassesCrowdController: {controller.gameObject.name}");
            }
            else
            {
                Debug.LogWarning("No EnemyMassesCrowdController found in scene!");
            }
        }

        private void SetupCollider(Runtime.EnemyMassesSpawnTrigger.TriggerShape shape, object sizeData, float height = 0f)
        {
            // Remove existing collider
            var existingCollider = _trigger.GetComponent<Collider>();
            if (existingCollider != null)
            {
                Undo.DestroyObjectImmediate(existingCollider);
            }

            // Add new collider
            switch (shape)
            {
                case Runtime.EnemyMassesSpawnTrigger.TriggerShape.Box:
                    var box = Undo.AddComponent<BoxCollider>(_trigger.gameObject);
                    box.size = (Vector3)sizeData;
                    box.isTrigger = true;
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerShape.Sphere:
                    var sphere = Undo.AddComponent<SphereCollider>(_trigger.gameObject);
                    sphere.radius = (float)sizeData;
                    sphere.isTrigger = true;
                    break;

                case Runtime.EnemyMassesSpawnTrigger.TriggerShape.Capsule:
                    var capsule = Undo.AddComponent<CapsuleCollider>(_trigger.gameObject);
                    capsule.radius = (float)sizeData;
                    capsule.height = height;
                    capsule.isTrigger = true;
                    break;
            }

            _triggerShape.enumValueIndex = (int)shape;
            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (!_trigger.showGizmos)
                return;

            // Draw label in scene view
            Color gizmoColor = _trigger.GetGizmoColor();
            GUIStyle style = new GUIStyle();
            style.normal.textColor = gizmoColor;
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 12;

            Handles.Label(_trigger.transform.position + Vector3.up * 2f, 
                         $"Spawn Trigger: Faction {_enemyCrowdIndex.intValue}", 
                         style);
        }
    }
}
