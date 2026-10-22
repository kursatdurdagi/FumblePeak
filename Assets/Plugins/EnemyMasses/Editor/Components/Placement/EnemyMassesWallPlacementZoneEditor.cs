using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for EnemyMassesWallPlacementZone.
    /// Provides visualization controls and quick actions.
    /// </summary>
    [CustomEditor(typeof(EnemyMassesWallPlacementZone))]
    public class EnemyMassesWallPlacementZoneEditor : UnityEditor.Editor
    {
        private EnemyMassesWallPlacementZone _zone;
        
        // Serialized properties
        private SerializedProperty _crowdControllerProp;
        private SerializedProperty _arrangementProp;
        private SerializedProperty _spotCountProp;
        private SerializedProperty _rowCountProp;
        private SerializedProperty _spotSpacingProp;
        private SerializedProperty _circleRadiusProp;
        private SerializedProperty _placementModeProp;
        private SerializedProperty _instantModeMaxDistanceProp;
        private SerializedProperty _enableAutoAssignProp;
        private SerializedProperty _autoAssignRadiusProp;
        private SerializedProperty _autoAssignIntervalProp;
        private SerializedProperty _allowedEnemyCrowdIndicesProp;
        private SerializedProperty _faceCenterProp;
        private SerializedProperty _customFacingDirectionProp;
        private SerializedProperty _spotFacingModeProp;
        
        // Combat Stance On Arrival
        private SerializedProperty _rtsControllerProp;
        private SerializedProperty _setStanceOnArrivalProp;
        private SerializedProperty _arrivalStanceProp;
        
        // Status Effects
        private SerializedProperty _applyStatusEffectOnArrivalProp;
        private SerializedProperty _arrivalStatusEffectTypeProp;
        private SerializedProperty _statusEffectValueProp;
        private SerializedProperty _statusEffectLingerDurationProp;
        
        // Status Effect VFX
        private SerializedProperty _statusEffectVFXTypeProp;
        private SerializedProperty _statusEffectParticleSystemProp;
        private SerializedProperty _statusEffectVFXGraphProp;
        private SerializedProperty _statusEffectVFXLocationProp;
        
        // Arrival/Departure Audio
        private SerializedProperty _arrivalSoundProp;
        private SerializedProperty _arrivalSoundVolumeProp;
        private SerializedProperty _arrivalSoundPitchVariationProp;
        private SerializedProperty _arrivalSoundSpatialBlendProp;
        private SerializedProperty _departureSoundProp;
        private SerializedProperty _departureSoundVolumeProp;
        private SerializedProperty _departureSoundPitchVariationProp;
        private SerializedProperty _departureSoundSpatialBlendProp;
        private SerializedProperty _maxConcurrentAudioSourcesProp;
        
        // Editor Visualization
        private SerializedProperty _showGizmosProp;
        private SerializedProperty _availableSpotColorProp;
        private SerializedProperty _occupiedSpotColorProp;
        private SerializedProperty _gizmoSizeProp;
        
        // Runtime Visualization
        private SerializedProperty _showRuntimeOutlineProp;
        private SerializedProperty _outlineColorProp;
        private SerializedProperty _outlineWidthProp;
        private SerializedProperty _showRuntimeSpotMarkersProp;
        private SerializedProperty _spotMarkerPrefabProp;
        private SerializedProperty _spotMarkerSizeProp;
        private SerializedProperty _runtimeAvailableColorProp;
        private SerializedProperty _runtimeOccupiedColorProp;
        
        private SerializedProperty _enableDebugLoggingProp;
        
        private void OnEnable()
        {
            // Guard against null target during script reloads or object destruction
            if (target == null)
                return;
            
            _zone = target as EnemyMassesWallPlacementZone;
            if (_zone == null)
                return;
            
            _crowdControllerProp = serializedObject.FindProperty("crowdController");
            _arrangementProp = serializedObject.FindProperty("arrangement");
            _spotCountProp = serializedObject.FindProperty("spotCount");
            _rowCountProp = serializedObject.FindProperty("rowCount");
            _spotSpacingProp = serializedObject.FindProperty("spotSpacing");
            _circleRadiusProp = serializedObject.FindProperty("circleRadius");
            _placementModeProp = serializedObject.FindProperty("placementMode");
            _instantModeMaxDistanceProp = serializedObject.FindProperty("instantModeMaxDistance");
            _enableAutoAssignProp = serializedObject.FindProperty("enableAutoAssign");
            _autoAssignRadiusProp = serializedObject.FindProperty("autoAssignRadius");
            _autoAssignIntervalProp = serializedObject.FindProperty("autoAssignInterval");
            _allowedEnemyCrowdIndicesProp = serializedObject.FindProperty("allowedEnemyCrowdIndices");
            _faceCenterProp = serializedObject.FindProperty("faceCenter");
            _customFacingDirectionProp = serializedObject.FindProperty("customFacingDirection");
            _spotFacingModeProp = serializedObject.FindProperty("spotFacingMode");
            
            // Combat Stance On Arrival
            _rtsControllerProp = serializedObject.FindProperty("rtsController");
            _setStanceOnArrivalProp = serializedObject.FindProperty("setStanceOnArrival");
            _arrivalStanceProp = serializedObject.FindProperty("arrivalStance");
            
            // Status Effects
            _applyStatusEffectOnArrivalProp = serializedObject.FindProperty("applyStatusEffectOnArrival");
            _arrivalStatusEffectTypeProp = serializedObject.FindProperty("arrivalStatusEffectType");
            _statusEffectValueProp = serializedObject.FindProperty("statusEffectValue");
            _statusEffectLingerDurationProp = serializedObject.FindProperty("statusEffectLingerDuration");
            
            // Status Effect VFX
            _statusEffectVFXTypeProp = serializedObject.FindProperty("statusEffectVFXType");
            _statusEffectParticleSystemProp = serializedObject.FindProperty("statusEffectParticleSystem");
            _statusEffectVFXGraphProp = serializedObject.FindProperty("statusEffectVFXGraph");
            _statusEffectVFXLocationProp = serializedObject.FindProperty("statusEffectVFXLocation");
            
            // Arrival/Departure Audio
            _arrivalSoundProp = serializedObject.FindProperty("arrivalSound");
            _arrivalSoundVolumeProp = serializedObject.FindProperty("arrivalSoundVolume");
            _arrivalSoundPitchVariationProp = serializedObject.FindProperty("arrivalSoundPitchVariation");
            _arrivalSoundSpatialBlendProp = serializedObject.FindProperty("arrivalSoundSpatialBlend");
            _departureSoundProp = serializedObject.FindProperty("departureSound");
            _departureSoundVolumeProp = serializedObject.FindProperty("departureSoundVolume");
            _departureSoundPitchVariationProp = serializedObject.FindProperty("departureSoundPitchVariation");
            _departureSoundSpatialBlendProp = serializedObject.FindProperty("departureSoundSpatialBlend");
            _maxConcurrentAudioSourcesProp = serializedObject.FindProperty("maxConcurrentAudioSources");
            
            // Editor Visualization
            _showGizmosProp = serializedObject.FindProperty("showGizmos");
            _availableSpotColorProp = serializedObject.FindProperty("availableSpotColor");
            _occupiedSpotColorProp = serializedObject.FindProperty("occupiedSpotColor");
            _gizmoSizeProp = serializedObject.FindProperty("gizmoSize");
            
            // Runtime Visualization
            _showRuntimeOutlineProp = serializedObject.FindProperty("showRuntimeOutline");
            _outlineColorProp = serializedObject.FindProperty("outlineColor");
            _outlineWidthProp = serializedObject.FindProperty("outlineWidth");
            _showRuntimeSpotMarkersProp = serializedObject.FindProperty("showRuntimeSpotMarkers");
            _spotMarkerPrefabProp = serializedObject.FindProperty("spotMarkerPrefab");
            _spotMarkerSizeProp = serializedObject.FindProperty("spotMarkerSize");
            _runtimeAvailableColorProp = serializedObject.FindProperty("runtimeAvailableColor");
            _runtimeOccupiedColorProp = serializedObject.FindProperty("runtimeOccupiedColor");
            
            _enableDebugLoggingProp = serializedObject.FindProperty("enableDebugLogging");
        }
        
        public override void OnInspectorGUI()
        {
            if (_zone == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }
            
            serializedObject.Update();
            
            EditorGUILayout.Space(5);
            DrawHeaderSection();
            EditorGUILayout.Space(10);
            
            DrawZoneSettings();
            EditorGUILayout.Space(5);
            
            DrawSpotArrangement();
            EditorGUILayout.Space(5);
            
            DrawPlacementMode();
            EditorGUILayout.Space(5);
            
            DrawAutoAssign();
            EditorGUILayout.Space(5);
            
            DrawFacing();
            EditorGUILayout.Space(5);
            
            DrawCombatStanceOnArrival();
            EditorGUILayout.Space(5);
            
            DrawStatusEffects();
            EditorGUILayout.Space(5);
            
            DrawArrivalDepartureAudio();
            EditorGUILayout.Space(5);
            
            DrawVisualization();
            EditorGUILayout.Space(5);
            
            DrawRuntimeVisualization();
            EditorGUILayout.Space(5);
            
            DrawDebug();
            EditorGUILayout.Space(10);
            
            DrawRuntimeControls();
            
            serializedObject.ApplyModifiedProperties();
            
            // Regenerate spots when settings change
            if (GUI.changed && !Application.isPlaying)
            {
                _zone.GenerateSpots();
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
            
            // Color bar based on available/occupied status
            Color headerColor = _zone.AvailableSpots > 0 
                ? new Color(0.2f, 0.7f, 0.3f) // Green when spots available
                : new Color(0.8f, 0.4f, 0.2f); // Orange when full
            
            GUI.color = headerColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;
            
            EditorGUILayout.LabelField("Wall Placement Zone", titleStyle);
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawZoneSettings()
        {
            EditorGUILayout.LabelField("Zone Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_crowdControllerProp);
            
            if (_crowdControllerProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign an EnemyMassesCrowdController to enable zone functionality.", MessageType.Warning);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawSpotArrangement()
        {
            EditorGUILayout.LabelField("Spot Arrangement", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_arrangementProp, new GUIContent("Arrangement Type"));
            
            var arrangement = (EnemyMassesWallPlacementZone.SpotArrangement)_arrangementProp.enumValueIndex;
            
            EditorGUILayout.Space(3);
            
            switch (arrangement)
            {
                case EnemyMassesWallPlacementZone.SpotArrangement.Line:
                    EditorGUILayout.PropertyField(_spotCountProp, new GUIContent("Spot Count"));
                    EditorGUILayout.PropertyField(_spotSpacingProp, new GUIContent("Spacing"));
                    EditorGUILayout.HelpBox("Spots arranged in a single line along the zone's right axis.", MessageType.Info);
                    break;
                    
                case EnemyMassesWallPlacementZone.SpotArrangement.Grid:
                    EditorGUILayout.PropertyField(_spotCountProp, new GUIContent("Columns"));
                    EditorGUILayout.PropertyField(_rowCountProp, new GUIContent("Rows"));
                    EditorGUILayout.PropertyField(_spotSpacingProp, new GUIContent("Spacing"));
                    
                    int totalSpots = _spotCountProp.intValue * _rowCountProp.intValue;
                    EditorGUILayout.HelpBox($"Grid of {_spotCountProp.intValue}x{_rowCountProp.intValue} = {totalSpots} spots.", MessageType.Info);
                    break;
                    
                case EnemyMassesWallPlacementZone.SpotArrangement.Circle:
                    EditorGUILayout.PropertyField(_spotCountProp, new GUIContent("Spot Count"));
                    EditorGUILayout.PropertyField(_circleRadiusProp, new GUIContent("Radius"));
                    EditorGUILayout.HelpBox("Spots arranged in a circle around the zone center.", MessageType.Info);
                    break;
                    
                case EnemyMassesWallPlacementZone.SpotArrangement.Manual:
                    int childCount = _zone.transform.childCount;
                    int activeChildren = 0;
                    foreach (Transform child in _zone.transform)
                    {
                        if (child.gameObject.activeInHierarchy)
                            activeChildren++;
                    }
                    
                    EditorGUILayout.LabelField("Child Transforms", $"{activeChildren} active / {childCount} total");
                    
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Add Spot Here", GUILayout.Height(22)))
                    {
                        CreateManualSpot(_zone.transform.position);
                    }
                    if (GUILayout.Button("Add Spot at Selection", GUILayout.Height(22)))
                    {
                        Vector3 pos = SceneView.lastActiveSceneView != null 
                            ? SceneView.lastActiveSceneView.camera.transform.position + SceneView.lastActiveSceneView.camera.transform.forward * 5f
                            : _zone.transform.position;
                        CreateManualSpot(pos);
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.HelpBox("Add child GameObjects to define spot positions. Each active child becomes a spot.", MessageType.Info);
                    break;
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawPlacementMode()
        {
            EditorGUILayout.LabelField("Placement Mode", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_placementModeProp, new GUIContent("Mode"));
            
            var placementMode = (EnemyMassesWallPlacementZone.PlacementMode)_placementModeProp.enumValueIndex;
            
            switch (placementMode)
            {
                case EnemyMassesWallPlacementZone.PlacementMode.Instant:
                    EditorGUILayout.PropertyField(_instantModeMaxDistanceProp, new GUIContent("Max Teleport Distance"));
                    EditorGUILayout.HelpBox(
                        "Agents within max distance teleport instantly. Agents farther away will pathfind instead to prevent exploits.",
                        MessageType.Info);
                    break;
                    
                case EnemyMassesWallPlacementZone.PlacementMode.Pathfind:
                    EditorGUILayout.HelpBox(
                        "Agents always pathfind to their assigned spot, regardless of distance.",
                        MessageType.Info);
                    break;
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawAutoAssign()
        {
            EditorGUILayout.LabelField("Auto-Assign", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_enableAutoAssignProp, new GUIContent("Enable Auto-Assign"));
            
            if (_enableAutoAssignProp.boolValue)
            {
                EditorGUILayout.PropertyField(_autoAssignRadiusProp, new GUIContent("Detection Radius"));
                EditorGUILayout.PropertyField(_autoAssignIntervalProp, new GUIContent("Check Interval"));
                EditorGUILayout.HelpBox(
                    "Agents within detection radius are automatically assigned to available spots.",
                    MessageType.Info);
            }
            
            EditorGUILayout.Space(3);
            DrawFactionFilter();
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawFactionFilter()
        {
            // Get faction names from crowd controller or all EnemyCrowd assets
            var crowdController = _crowdControllerProp.objectReferenceValue as EnemyMassesCrowdController;
            
            if (crowdController != null && crowdController.EnemyCrowds != null && crowdController.EnemyCrowds.Count > 0)
            {
                // Use factions from the assigned crowd controller
                DrawFactionMaskField(crowdController.EnemyCrowds);
            }
            else
            {
                // Find all EnemyCrowd assets in the project
                var allCrowds = FindAllEnemyCrowdAssets();
                if (allCrowds.Count > 0)
                {
                    EditorGUILayout.HelpBox("No Crowd Controller assigned. Showing all EnemyCrowd assets in project.", MessageType.Info);
                    DrawFactionMaskField(allCrowds);
                }
                else
                {
                    // Fallback to raw list
                    EditorGUILayout.PropertyField(_allowedEnemyCrowdIndicesProp, new GUIContent("Allowed Factions"));
                    if (_allowedEnemyCrowdIndicesProp.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox("Empty = all factions allowed.", MessageType.None);
                    }
                }
            }
        }
        
        private void DrawFactionMaskField(System.Collections.Generic.IList<Runtime.EnemyCrowd> crowds)
        {
            // Build faction names array
            string[] factionNames = new string[crowds.Count];
            for (int i = 0; i < crowds.Count; i++)
            {
                var crowd = crowds[i];
                factionNames[i] = crowd != null ? $"{i}: {crowd.factionName}" : $"{i}: (null)";
            }
            
            // Convert current list to mask
            int currentMask = 0;
            if (_allowedEnemyCrowdIndicesProp.arraySize == 0)
            {
                // Empty list means all allowed - set all bits
                currentMask = ~0; // All bits set
            }
            else
            {
                for (int i = 0; i < _allowedEnemyCrowdIndicesProp.arraySize; i++)
                {
                    int index = _allowedEnemyCrowdIndicesProp.GetArrayElementAtIndex(i).intValue;
                    if (index >= 0 && index < 32)
                    {
                        currentMask |= (1 << index);
                    }
                }
            }
            
            // Draw mask field
            EditorGUI.BeginChangeCheck();
            int newMask = EditorGUILayout.MaskField("Allowed Factions", currentMask, factionNames);
            
            if (EditorGUI.EndChangeCheck())
            {
                // Check if "Everything" is selected (all bits set for available factions)
                int allFactionsMask = 0;
                for (int i = 0; i < crowds.Count && i < 32; i++)
                {
                    allFactionsMask |= (1 << i);
                }
                
                bool isEverything = (newMask & allFactionsMask) == allFactionsMask;
                
                // Convert mask back to list
                _allowedEnemyCrowdIndicesProp.ClearArray();
                
                if (!isEverything && newMask != 0)
                {
                    // Only populate if not "Everything" and not "Nothing"
                    for (int i = 0; i < crowds.Count && i < 32; i++)
                    {
                        if ((newMask & (1 << i)) != 0)
                        {
                            _allowedEnemyCrowdIndicesProp.InsertArrayElementAtIndex(_allowedEnemyCrowdIndicesProp.arraySize);
                            _allowedEnemyCrowdIndicesProp.GetArrayElementAtIndex(_allowedEnemyCrowdIndicesProp.arraySize - 1).intValue = i;
                        }
                    }
                }
                // If isEverything, leave array empty (which means "all allowed")
                // If newMask == 0, leave array empty but show warning below
            }
            
            // Show info/warning
            if (_allowedEnemyCrowdIndicesProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("All factions allowed.", MessageType.None);
            }
        }
        
        private System.Collections.Generic.List<Runtime.EnemyCrowd> FindAllEnemyCrowdAssets()
        {
            var result = new System.Collections.Generic.List<Runtime.EnemyCrowd>();
            
            string[] guids = AssetDatabase.FindAssets("t:EnemyCrowd");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var crowd = AssetDatabase.LoadAssetAtPath<Runtime.EnemyCrowd>(path);
                if (crowd != null)
                {
                    result.Add(crowd);
                }
            }
            
            return result;
        }
        
        private void DrawFacing()
        {
            EditorGUILayout.LabelField("Agent Facing", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_spotFacingModeProp, new GUIContent("Spot Facing Mode"));

            var facingMode = (EnemyMassesWallPlacementZone.SpotFacingMode)_spotFacingModeProp.enumValueIndex;

            switch (facingMode)
            {
                case EnemyMassesWallPlacementZone.SpotFacingMode.None:
                    EditorGUILayout.Space(3);
                    EditorGUILayout.PropertyField(_faceCenterProp, new GUIContent("Face Zone Center"));
                    if (!_faceCenterProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(_customFacingDirectionProp, new GUIContent("Custom Direction"));
                        EditorGUILayout.HelpBox(
                            "Local direction agents will face. Set to (0,0,0) to use the zone's forward direction.",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Agents will face toward the zone center when at their spots.", MessageType.Info);
                    }
                    break;

                case EnemyMassesWallPlacementZone.SpotFacingMode.FacingAway:
                    EditorGUILayout.HelpBox(
                        "Agents face away from the wall (outward). Best for defense and cover positions.\n" +
                        "For dynamic walls: computed automatically from wall geometry.\n" +
                        "For static zones: uses the zone's forward direction.",
                        MessageType.Info);
                    break;

                case EnemyMassesWallPlacementZone.SpotFacingMode.FacingWall:
                    EditorGUILayout.HelpBox(
                        "Agents face toward the wall. Useful for repair, interaction, or breach scenarios.",
                        MessageType.Info);
                    break;

                case EnemyMassesWallPlacementZone.SpotFacingMode.AlongWallForward:
                    EditorGUILayout.HelpBox(
                        "Agents face along the wall in the polyline direction.\n" +
                        "For static zones: uses the zone's right direction.",
                        MessageType.Info);
                    break;

                case EnemyMassesWallPlacementZone.SpotFacingMode.AlongWallBackward:
                    EditorGUILayout.HelpBox(
                        "Agents face along the wall opposite to the polyline direction.\n" +
                        "For static zones: uses the zone's negative right direction.",
                        MessageType.Info);
                    break;
            }

            EditorGUILayout.EndVertical();
        }
        
        private void DrawCombatStanceOnArrival()
        {
            EditorGUILayout.LabelField("Combat Stance On Arrival", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_setStanceOnArrivalProp, new GUIContent("Set Stance On Arrival"));
            
            if (_setStanceOnArrivalProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_rtsControllerProp, new GUIContent("RTS Controller"));
                EditorGUILayout.PropertyField(_arrivalStanceProp, new GUIContent("Arrival Stance"));
                
                if (_rtsControllerProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No RTS Controller assigned. Stance will be set directly on the agent (less robust).",
                        MessageType.Warning);
                }
                
                // Show stance description
                var stance = (CombatStance)_arrivalStanceProp.enumValueIndex;
                string stanceDesc = stance switch
                {
                    CombatStance.Offensive => "Offensive: Agents actively seek and attack enemies.",
                    CombatStance.Defensive => "Defensive: Agents only attack if attacked first.",
                    CombatStance.HoldPosition => "Hold Position: Agents stay at their spot and attack enemies in range.",
                    CombatStance.Passive => "Passive: Agents will not engage in combat under any circumstances.",
                    _ => ""
                };
                
                if (!string.IsNullOrEmpty(stanceDesc))
                {
                    EditorGUILayout.HelpBox(stanceDesc, MessageType.Info);
                }
                
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("Agents keep their current stance when placed.", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawStatusEffects()
        {
            EditorGUILayout.LabelField("Status Effects On Arrival", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_applyStatusEffectOnArrivalProp, new GUIContent("Apply Status Effect"));
            
            if (_applyStatusEffectOnArrivalProp.boolValue)
            {
                EditorGUI.indentLevel++;
                
                // Effect settings
                EditorGUILayout.PropertyField(_arrivalStatusEffectTypeProp, new GUIContent("Effect Type"));
                EditorGUILayout.PropertyField(_statusEffectValueProp, new GUIContent("Effect Value"));
                EditorGUILayout.PropertyField(_statusEffectLingerDurationProp, new GUIContent("Linger Duration"));
                
                // Show effect description based on type
                var effectType = (StatusEffectType)_arrivalStatusEffectTypeProp.enumValueIndex;
                string effectDesc = GetEffectDescription(effectType);
                if (!string.IsNullOrEmpty(effectDesc))
                {
                    EditorGUILayout.HelpBox(effectDesc, MessageType.Info);
                }
                
                EditorGUILayout.Space(5);
                
                // VFX Settings
                EditorGUILayout.LabelField("Visual Effects", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_statusEffectVFXTypeProp, new GUIContent("VFX Type"));
                
                var vfxType = (VFXType)_statusEffectVFXTypeProp.enumValueIndex;
                if (vfxType == VFXType.ParticleSystem)
                {
                    EditorGUILayout.PropertyField(_statusEffectParticleSystemProp, new GUIContent("Particle Prefab"));
                }
                else if (vfxType == VFXType.VFXGraph)
                {
                    EditorGUILayout.PropertyField(_statusEffectVFXGraphProp, new GUIContent("VFX Graph Asset"));
                }
                
                if (vfxType != VFXType.None)
                {
                    EditorGUILayout.PropertyField(_statusEffectVFXLocationProp, new GUIContent("VFX Spawn Location"));
                    
                    var vfxLocation = (EnemyMassesWallPlacementZone.VFXSpawnLocation)_statusEffectVFXLocationProp.enumValueIndex;
                    string locationDesc = vfxLocation switch
                    {
                        EnemyMassesWallPlacementZone.VFXSpawnLocation.ZoneCenter => "Single VFX at zone center (efficient).",
                        EnemyMassesWallPlacementZone.VFXSpawnLocation.PerAgent => "Individual VFX attached to each agent (immersive).",
                        _ => ""
                    };
                    EditorGUILayout.HelpBox(locationDesc, MessageType.None);
                }
                
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("No status effects will be applied when agents arrive.", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawArrivalDepartureAudio()
        {
            EditorGUILayout.LabelField("Arrival/Departure Audio", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            bool hasAnySound = _arrivalSoundProp.objectReferenceValue != null || 
                               _departureSoundProp.objectReferenceValue != null;
            
            // Arrival Sound
            EditorGUILayout.LabelField("Arrival Sound", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_arrivalSoundProp, new GUIContent("Audio Clip"));
            
            if (_arrivalSoundProp.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_arrivalSoundVolumeProp, new GUIContent("Volume"));
                EditorGUILayout.PropertyField(_arrivalSoundPitchVariationProp, new GUIContent("Pitch Variation"));
                EditorGUILayout.PropertyField(_arrivalSoundSpatialBlendProp, new GUIContent("Spatial Blend"));
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(5);
            
            // Departure Sound
            EditorGUILayout.LabelField("Departure Sound", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_departureSoundProp, new GUIContent("Audio Clip"));
            
            if (_departureSoundProp.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_departureSoundVolumeProp, new GUIContent("Volume"));
                EditorGUILayout.PropertyField(_departureSoundPitchVariationProp, new GUIContent("Pitch Variation"));
                EditorGUILayout.PropertyField(_departureSoundSpatialBlendProp, new GUIContent("Spatial Blend"));
                EditorGUI.indentLevel--;
            }
            
            // Audio pooling settings
            if (hasAnySound)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Audio Pooling", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_maxConcurrentAudioSourcesProp, new GUIContent("Max Concurrent Sources"));
                EditorGUILayout.HelpBox(
                    "Limits simultaneous sounds to prevent audio spam when many agents arrive/leave at once.",
                    MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private string GetEffectDescription(StatusEffectType effectType)
        {
            return effectType switch
            {
                // Buffs
                StatusEffectType.ArmorBuff => "Increases armor/damage resistance. Value = multiplier (1.5 = +50%).",
                StatusEffectType.DamageBuff => "Increases attack damage. Value = multiplier (1.5 = +50%).",
                StatusEffectType.SpeedBuff => "Increases movement speed. Value = multiplier.",
                StatusEffectType.AttackSpeedBuff => "Increases attack speed. Value = multiplier.",
                StatusEffectType.HealthBuff => "Increases max health. Value = bonus health amount.",
                StatusEffectType.Regeneration => "Heals over time. Value = heal per tick.",
                StatusEffectType.Shield => "Absorbs damage. Value = shield amount.",
                StatusEffectType.BlockBuff => "Increases block chance. Value = bonus chance.",
                StatusEffectType.CriticalBuff => "Increases critical hit chance. Value = bonus chance.",
                StatusEffectType.Invulnerable => "Immune to all damage.",
                StatusEffectType.CCImmune => "Immune to crowd control effects.",
                
                // Debuffs (less common for defensive positions but available)
                StatusEffectType.Slow => "Reduces movement speed. Value = multiplier (0.5 = -50%).",
                StatusEffectType.Weaken => "Reduces attack damage. Value = multiplier.",
                StatusEffectType.Vulnerable => "Increases damage taken. Value = multiplier.",
                
                _ => ""
            };
        }
        
        private void DrawVisualization()
        {
            EditorGUILayout.LabelField("Visualization", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_showGizmosProp, new GUIContent("Show Gizmos"));
            
            if (_showGizmosProp.boolValue)
            {
                EditorGUILayout.PropertyField(_availableSpotColorProp, new GUIContent("Available Color"));
                EditorGUILayout.PropertyField(_occupiedSpotColorProp, new GUIContent("Occupied Color"));
                EditorGUILayout.PropertyField(_gizmoSizeProp, new GUIContent("Gizmo Size"));
                
                // Color preview
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Preview:", GUILayout.Width(60));
                
                Rect availRect = GUILayoutUtility.GetRect(30, 18);
                EditorGUI.DrawRect(availRect, _availableSpotColorProp.colorValue);
                EditorGUILayout.LabelField("Available", GUILayout.Width(60));
                
                Rect occRect = GUILayoutUtility.GetRect(30, 18);
                EditorGUI.DrawRect(occRect, _occupiedSpotColorProp.colorValue);
                EditorGUILayout.LabelField("Occupied", GUILayout.Width(60));
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawRuntimeVisualization()
        {
            EditorGUILayout.LabelField("Runtime Visualization", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Zone Outline
            EditorGUILayout.LabelField("Zone Outline", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_showRuntimeOutlineProp, new GUIContent("Show Outline"));
            
            if (_showRuntimeOutlineProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_outlineColorProp, new GUIContent("Outline Color"));
                EditorGUILayout.PropertyField(_outlineWidthProp, new GUIContent("Outline Width"));
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(5);
            
            // Spot Markers
            EditorGUILayout.LabelField("Spot Markers", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_showRuntimeSpotMarkersProp, new GUIContent("Show Spot Markers"));
            
            if (_showRuntimeSpotMarkersProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_spotMarkerPrefabProp, new GUIContent("Marker Prefab"));
                
                if (_spotMarkerPrefabProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Using default sphere markers. Assign a prefab for custom markers.", MessageType.None);
                }
                
                EditorGUILayout.PropertyField(_spotMarkerSizeProp, new GUIContent("Marker Size"));
                EditorGUILayout.PropertyField(_runtimeAvailableColorProp, new GUIContent("Available Color"));
                EditorGUILayout.PropertyField(_runtimeOccupiedColorProp, new GUIContent("Occupied Color"));
                
                // Color preview
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Preview:", GUILayout.Width(60));
                
                Rect availRect = GUILayoutUtility.GetRect(30, 18);
                EditorGUI.DrawRect(availRect, _runtimeAvailableColorProp.colorValue);
                EditorGUILayout.LabelField("Available", GUILayout.Width(60));
                
                Rect occRect = GUILayoutUtility.GetRect(30, 18);
                EditorGUI.DrawRect(occRect, _runtimeOccupiedColorProp.colorValue);
                EditorGUILayout.LabelField("Occupied", GUILayout.Width(60));
                
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.HelpBox(
                "Runtime visualization shows the zone and spot positions during gameplay using LineRenderer and sphere markers.",
                MessageType.Info);
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawDebug()
        {
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.PropertyField(_enableDebugLoggingProp, new GUIContent("Enable Debug Logging"));
            
            if (_enableDebugLoggingProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Debug logging enabled. Check Console for detailed placement information.",
                    MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawRuntimeControls()
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            if (Application.isPlaying)
            {
                // Zone stats
                int totalSpots = _zone.Spots?.Count ?? 0;
                int occupied = _zone.OccupiedSpots;
                int available = _zone.AvailableSpots;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel, GUILayout.Width(50));
                
                string statusText = available > 0 ? $"{available} spots available" : "Zone Full";
                Color statusColor = available > 0 ? new Color(0.2f, 0.8f, 0.3f) : new Color(0.9f, 0.5f, 0.2f);
                
                GUIStyle statusStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = statusColor } };
                EditorGUILayout.LabelField(statusText, statusStyle);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);
                
                // Progress bar
                if (totalSpots > 0)
                {
                    float fillPercent = (float)occupied / totalSpots;
                    Rect rect = EditorGUILayout.GetControlRect(false, 20);
                    EditorGUI.ProgressBar(rect, fillPercent, $"Occupied: {occupied}/{totalSpots} ({fillPercent:P0})");
                }
                
                EditorGUILayout.Space(5);
                
                // Action buttons
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear All", GUILayout.Height(24)))
                {
                    _zone.ClearAllAssignments();
                }
                if (GUILayout.Button("Regenerate Spots", GUILayout.Height(24)))
                {
                    _zone.GenerateSpots();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // Editor mode info
                int totalSpots = _zone.Spots?.Count ?? 0;
                EditorGUILayout.LabelField($"Configured Spots: {totalSpots}");
                
                EditorGUILayout.Space(3);
                
                if (GUILayout.Button("Regenerate Spots", GUILayout.Height(24)))
                {
                    _zone.GenerateSpots();
                    SceneView.RepaintAll();
                }
                
                EditorGUILayout.HelpBox("Enter Play Mode to see runtime status and controls.", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void CreateManualSpot(Vector3 position)
        {
            GameObject spot = new GameObject($"Spot {_zone.transform.childCount}");
            Undo.RegisterCreatedObjectUndo(spot, "Create Manual Spot");
            
            spot.transform.SetParent(_zone.transform);
            spot.transform.position = position;
            spot.transform.localRotation = Quaternion.identity;
            
            _zone.GenerateSpots();
            
            Selection.activeObject = spot;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        
        private void OnSceneGUI()
        {
            if (_zone == null)
                return;
            
            // Draw zone label
            Handles.Label(
                _zone.transform.position + Vector3.up * 2f,
                _zone.name,
                new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                });
            
            // Draw spot handles for manual arrangement
            if (_zone.Arrangement == EnemyMassesWallPlacementZone.SpotArrangement.Manual)
            {
                // Show position handles for child transforms
                foreach (Transform child in _zone.transform)
                {
                    if (!child.gameObject.activeInHierarchy)
                        continue;
                    
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPos = Handles.PositionHandle(child.position, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(child, "Move Spot");
                        child.position = newPos;
                        _zone.GenerateSpots();
                    }
                }
            }
            
            // Draw auto-assign radius if enabled
            if (_zone.EnableAutoAssign)
            {
                float radius = 10f;
                var field = _zone.GetType().GetField("autoAssignRadius", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null && field.GetValue(_zone) is float fieldValue)
                {
                    radius = fieldValue;
                }
                
                Handles.color = new Color(0.3f, 0.7f, 1f, 0.15f);
                Handles.DrawSolidDisc(_zone.transform.position, Vector3.up, radius);
                Handles.color = new Color(0.3f, 0.7f, 1f, 0.8f);
                Handles.DrawWireDisc(_zone.transform.position, Vector3.up, radius);
            }
        }
    }
}
