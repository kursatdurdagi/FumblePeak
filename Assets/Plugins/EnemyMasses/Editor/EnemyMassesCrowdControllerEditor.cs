using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Arawn.EnemyMasses.Runtime;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(EnemyMassesCrowdController))]
    public class EnemyMassesCrowdControllerEditor : UnityEditor.Editor
    {
        // TEMP: UITK inspector is causing DPI/panel errors in Unity 6; fall back to IMGUI for stability.
        private static bool USE_IMGUI_FALLBACK = false;

        private EnemyMassesCrowdController _controller;
        private VisualElement _root;
        private VisualElement _contentContainer;
        
        // Tab system
        private VisualElement _tabBar;
        private Dictionary<string, VisualElement> _tabContents = new Dictionary<string, VisualElement>();
        private string _currentTab = "setup";
        
        // Runtime state
        private bool _isPlayMode;
        
        // Spawn parameters
        private int _spawnCount = 50;
        private int _factionIndex = 0;
        private FormationType _formationType = FormationType.Circle;

        private void OnEnable()
        {
            if (target == null || !(target is EnemyMassesCrowdController))
                return;
            _controller = (EnemyMassesCrowdController)target;
        }

        public override VisualElement CreateInspectorGUI()
        {
            if (USE_IMGUI_FALLBACK)
                return null; // Use OnInspectorGUI (IMGUI) to avoid UITK text job crashes

            _root = new VisualElement();
            _isPlayMode = Application.isPlaying;
            
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Plugins/EnemyMasses/Editor/EnemyMassesCrowdControllerStyle.uss");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);
            
            CreateHeader();
            CreateTabBar();
            CreateContentContainer();
            
            // Build initial tab content
            ShowTab(_currentTab);
            
            // Update runtime data periodically if in play mode
            // Only schedule if we have a valid panel to prevent UI Toolkit threading issues
            if (_isPlayMode)
            {
                _root.RegisterCallback<AttachToPanelEvent>(evt => 
                {
                    _root.schedule.Execute(() => 
                    {
                        if (_root?.panel != null) 
                            UpdateRuntimeData();
                    }).Every(500);
                });
            }
            
            return _root;
        }

        public override void OnInspectorGUI()
        {
            if (!USE_IMGUI_FALLBACK)
            {
                base.OnInspectorGUI();
                return;
            }

            EditorGUILayout.HelpBox("UI Toolkit inspector temporarily disabled due to Unity 6 UITK text job errors. Using IMGUI fallback.", MessageType.Info);
            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

                if (GUILayout.Button("Assign Faction Battle Targets"))
                {
                    _controller.AssignFactionBattleTargets();
                }

                EditorGUILayout.BeginHorizontal();
                _spawnCount = EditorGUILayout.IntField("Spawn Count", Mathf.Max(1, _spawnCount));
                if (GUILayout.Button("Spawn First Faction"))
                {
                    _controller.SpawnEnemyCrowd(0, Vector3.zero, Quaternion.identity, _spawnCount);
                }
                EditorGUILayout.EndHorizontal();

                // Navmesh impulse debug toggle (IMGUI fallback)
                bool newDebug = EditorGUILayout.Toggle(new GUIContent("Navmesh Impulse Debug Logs", "Logs navmesh impulse apply/expire/settle events to the Console."), EnemyMassesNavigationController.NavmeshImpulseDebug);
                EnemyMassesNavigationController.NavmeshImpulseDebug = newDebug;
            }
        }

        private void CreateHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("header");
            
            var title = new Label("Enemy Masses");
            title.AddToClassList("header-title");
            header.Add(title);
            
            var subtitle = new Label("GPU-Accelerated Crowd System");
            subtitle.AddToClassList("header-subtitle");
            header.Add(subtitle);
            
            // Status indicator
            if (_isPlayMode)
            {
                var statusBar = new VisualElement();
                statusBar.AddToClassList("status-bar");
                
                var statusLabel = new Label();
                statusLabel.name = "status-label";
                statusLabel.AddToClassList("status-label");
                UpdateStatusLabel(statusLabel);
                statusBar.Add(statusLabel);
                
                header.Add(statusBar);
            }
            
            _root.Add(header);
        }

        private void CreateTabBar()
        {
            _tabBar = new VisualElement();
            _tabBar.AddToClassList("tab-bar");
            
            AddTab("setup", "Setup");
            // Phase 3: Configuration tab removed - all settings now in EnemyCrowd assets
            AddTab("control", "Control"); // Always show Control tab for Faction Battle settings
            
            if (_isPlayMode)
            {
                AddTab("spawn", "Spawn");
                AddTab("debug", "Debug");
            }
            
            // Network tab always visible (shows available events in edit mode, live stats in play mode)
            AddTab("network", "Network");
            
            _root.Add(_tabBar);
        }

        private void AddTab(string tabId, string tabLabel)
        {
            var tab = new Button(() => ShowTab(tabId));
            tab.text = tabLabel;
            tab.AddToClassList("tab-button");
            
            if (tabId == _currentTab)
                tab.AddToClassList("tab-active");
            
            tab.name = $"tab-{tabId}";
            _tabBar.Add(tab);
        }

        private void CreateContentContainer()
        {
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("content-container");
            _root.Add(_contentContainer);
        }

        private void ShowTab(string tabId)
        {
            _currentTab = tabId;
            _contentContainer.Clear();
            
            // Update tab button states
            foreach (var child in _tabBar.Children())
            {
                if (child is Button button)
                {
                    button.RemoveFromClassList("tab-active");
                    if (button.name == $"tab-{tabId}")
                        button.AddToClassList("tab-active");
                }
            }
            
            // Build tab content
            VisualElement content = null;
            
            switch (tabId)
            {
                case "setup":
                    content = CreateSetupTab();
                    break;
                // Phase 3: Configuration tab removed - all settings now in EnemyCrowd assets
                case "spawn":
                    content = CreateSpawnTab();
                    break;
                case "control":
                    content = CreateControlTab();
                    break;
                case "debug":
                    content = CreateDebugTab();
                    break;
                case "network":
                    content = CreateNetworkTab();
                    break;
            }
            
            if (content != null)
                _contentContainer.Add(content);
        }

        #region Setup Tab

        private VisualElement CreateSetupTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Phase 3 Refined Info
            var phase3Info = new HelpBox(
                "✨ Phase 3 Refined: Global system settings (Performance, Animation Culling, Spawn) are here. " +
                "Per-faction settings (Combat, Behavior, Formation) are in EnemyCrowd assets.",
                HelpBoxMessageType.Info
            );
            container.Add(phase3Info);
            
            // Quick Start Guide
            var quickStart = new Foldout { text = "Quick Start", value = true };
            quickStart.AddToClassList("section");
            
            var guide = new Label(
                "1. Add EnemyCrowd assets (each has all settings built-in)\n" +
                "2. Assign a player target (optional)\n" +
                "3. Enter Play Mode to spawn agents\n" +
                "4. Configure per-faction settings in EnemyCrowd assets"
            );
            guide.AddToClassList("guide-text");
            quickStart.Add(guide);
            container.Add(quickStart);
            
            // Core Setup
            var coreSection = new Foldout { text = "Core Setup", value = true };
            coreSection.AddToClassList("section");
            
            var crowdsProp = serializedObject.FindProperty("enemyCrowds");
            var crowdsField = new PropertyField(crowdsProp, "Enemy Crowd Factions");
            crowdsField.tooltip = "Add EnemyCrowd ScriptableObject assets here. Each represents a faction/type of enemy.";
            crowdsField.Bind(serializedObject);
            coreSection.Add(crowdsField);
            
            if (crowdsProp.arraySize == 0)
            {
                var warning = new HelpBox("No Enemy Crowd assets assigned! Create Enemy Crowd assets (Right-click > Create > Enemy Masses > Enemy Crowd) and add them to the list.", HelpBoxMessageType.Warning);
                coreSection.Add(warning);
            }
            else
            {
                var info = new Label($"{crowdsProp.arraySize} faction(s) configured");
                info.AddToClassList("info-label");
                coreSection.Add(info);
            }
            
            var targetsProp = serializedObject.FindProperty("playerTargets");
            var targetsField = new PropertyField(targetsProp, "Player Targets");
            targetsField.tooltip = "Optional list of player targets for multi-player targeting.";
            targetsField.Bind(serializedObject);
            coreSection.Add(targetsField);

            var targetProp = serializedObject.FindProperty("playerTarget");
            var targetField = new PropertyField(targetProp, "Legacy Player Target");
            targetField.tooltip = "Fallback single target used when Player Targets list is empty.";
            targetField.Bind(serializedObject);
            coreSection.Add(targetField);

            var rtsProp = serializedObject.FindProperty("rtsController");
            var rtsField = new PropertyField(rtsProp, "RTS Controller");
            rtsField.tooltip = "Optional RTS controller used to detect player-controlled factions for player auto-targeting in faction battles.";
            rtsField.Bind(serializedObject);
            coreSection.Add(rtsField);
            
            bool hasTargets = targetsProp.arraySize > 0 || targetProp.objectReferenceValue != null;
            if (!hasTargets)
            {
                var info = new HelpBox("Player Targets are optional. Assign targets if you want Enemy Crowd agents to attack a Player Character or other specific targets.", HelpBoxMessageType.Info);
                coreSection.Add(info);
            }
            
            container.Add(coreSection);
            
            // Global Settings (Phase 3 Refined)
            var globalSection = new Foldout { text = "Global Settings", value = true };
            globalSection.AddToClassList("section");
            
            var globalInfo = new HelpBox(
                "These settings affect ALL agents system-wide. Per-faction settings are configured in individual EnemyCrowd assets.",
                HelpBoxMessageType.Info
            );
            globalSection.Add(globalInfo);
            
            // Performance Settings with Preset System
            var perfProp = serializedObject.FindProperty("performanceSettings");
            var presetProp = perfProp.FindPropertyRelative("preset");
            var currentPreset = (PerformancePreset)presetProp.intValue;
            
            // Create container for performance settings
            var perfContainer = new VisualElement();
            perfContainer.style.marginTop = 5;
            
            // Preset dropdown (custom order to keep Prioritized between None and Moderate)
            var presetOptions = new List<PerformancePreset>
            {
                PerformancePreset.None,
                PerformancePreset.Prioritized,
                PerformancePreset.Moderate,
                PerformancePreset.Aggressive,
                PerformancePreset.VeryAggressive,
                PerformancePreset.Custom
            };

            if (!presetOptions.Contains(currentPreset))
                currentPreset = PerformancePreset.Moderate;

            string FormatPresetName(PerformancePreset preset) =>
                ObjectNames.NicifyVariableName(preset.ToString());

            var presetField = new PopupField<PerformancePreset>(
                "Performance Preset",
                presetOptions,
                currentPreset,
                FormatPresetName,
                FormatPresetName);
            presetField.tooltip = "Quick preset for performance settings:\n" +
                                  "• None: Full quality, no optimizations\n" +
                                  "• Prioritized: Moderate optimizations with visible-only animation rotation\n" +
                                  "• Moderate: Light throttling, good for 100-300 agents\n" +
                                  "• Aggressive: Stronger throttling, good for 300-1000 agents\n" +
                                  "• Very Aggressive: Maximum optimization for 1000+ agents\n" +
                                  "• Custom: Manual control of all settings";
            presetField.style.marginBottom = 5;
            perfContainer.Add(presetField);
            
            // Preset description label
            var presetDescLabel = new Label();
            presetDescLabel.style.fontSize = 11;
            presetDescLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            presetDescLabel.style.marginLeft = 3;
            presetDescLabel.style.marginBottom = 8;
            presetDescLabel.style.whiteSpace = WhiteSpace.Normal;
            UpdatePresetDescription(presetDescLabel, currentPreset);
            perfContainer.Add(presetDescLabel);
            
            // Detailed settings foldout (only visible in Custom mode)
            var detailedSettingsFoldout = new Foldout { text = "Custom Performance Settings", value = false };
            detailedSettingsFoldout.style.display = currentPreset == PerformancePreset.Custom 
                ? DisplayStyle.Flex : DisplayStyle.None;
            
            var perfField = new PropertyField(perfProp);
            perfField.Bind(serializedObject);
            detailedSettingsFoldout.Add(perfField);
            perfContainer.Add(detailedSettingsFoldout);
            
            // Handle preset changes
            presetField.RegisterValueChangedCallback(evt =>
            {
                var newPreset = (PerformancePreset)evt.newValue;
                presetProp.intValue = (int)newPreset;
                
                // Show/hide detailed settings based on preset
                detailedSettingsFoldout.style.display = newPreset == PerformancePreset.Custom 
                    ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Update description
                UpdatePresetDescription(presetDescLabel, newPreset);
                
                // Apply preset values if not Custom
                if (newPreset != PerformancePreset.Custom)
                {
                    ApplyPerformancePreset(perfProp, newPreset);
                }
                
                serializedObject.ApplyModifiedProperties();
            });
            
            globalSection.Add(perfContainer);

            // Hit Detection Mode
            var hitDetectionProp = serializedObject.FindProperty("hitDetectionMode");
            var hitDetectionFoldout = new Foldout { text = "Hit Detection", value = true };
            var hitDetectionHelp = new HelpBox(
                "Distance: virtual hitboxes (no colliders). Collider: Unity collider/trigger hits; controller auto-adds a root collider if missing.",
                HelpBoxMessageType.Info
            );
            hitDetectionFoldout.Add(hitDetectionHelp);
            var hitDetectionField = new PropertyField(hitDetectionProp, "Hit Detection Mode");
            hitDetectionField.Bind(serializedObject);
            hitDetectionFoldout.Add(hitDetectionField);
            globalSection.Add(hitDetectionFoldout);
            
            // Animation Culling Settings
            var animCullFoldout = new Foldout { text = "Animation Culling", value = false };
            
            var cullByDistProp = serializedObject.FindProperty("cullAnimationsByDistance");
            var cullByDistField = new PropertyField(cullByDistProp);
            cullByDistField.Bind(serializedObject);
            animCullFoldout.Add(cullByDistField);
            
            var cullDistProp = serializedObject.FindProperty("animationCullDistance");
            var cullDistField = new PropertyField(cullDistProp);
            cullDistField.Bind(serializedObject);
            animCullFoldout.Add(cullDistField);
            
            var cullRefProp = serializedObject.FindProperty("animationCullReference");
            var cullRefField = new PropertyField(cullRefProp);
            cullRefField.Bind(serializedObject);
            animCullFoldout.Add(cullRefField);
            
            globalSection.Add(animCullFoldout);
            
            // Audio Settings
            var audioFoldout = new Foldout { text = "Audio Settings", value = false };
            
            var maxFootstepsPerFrameProp = serializedObject.FindProperty("maxFootstepsPerFrame");
            var maxFootstepsPerFrameField = new PropertyField(maxFootstepsPerFrameProp, "Max Footsteps Per Frame");
            maxFootstepsPerFrameField.tooltip = "Maximum footstep sounds that can be triggered per frame. Prevents audio spam with many agents.";
            maxFootstepsPerFrameField.Bind(serializedObject);
            audioFoldout.Add(maxFootstepsPerFrameField);
            
            var maxGlobalFootstepSourcesProp = serializedObject.FindProperty("maxGlobalFootstepSources");
            var maxGlobalFootstepSourcesField = new PropertyField(maxGlobalFootstepSourcesProp, "Max Global Footstep Sources");
            maxGlobalFootstepSourcesField.tooltip = "Maximum total concurrent footstep AudioSources globally. Limits total footstep audio regardless of clip variety.";
            maxGlobalFootstepSourcesField.Bind(serializedObject);
            audioFoldout.Add(maxGlobalFootstepSourcesField);

            var maxGlobalSkillOneShotSfxProp = serializedObject.FindProperty("maxGlobalSkillOneShotSFX");
            var maxGlobalSkillOneShotSfxField = new PropertyField(maxGlobalSkillOneShotSfxProp, "Max Skill One-Shot SFX");
            maxGlobalSkillOneShotSfxField.tooltip = "Global cap for one-shot PlaySFX actions from AgentSkill. 0 = unlimited.";
            maxGlobalSkillOneShotSfxField.Bind(serializedObject);
            audioFoldout.Add(maxGlobalSkillOneShotSfxField);

            var maxGlobalSkillLoopingSfxProp = serializedObject.FindProperty("maxGlobalSkillLoopingSFX");
            var maxGlobalSkillLoopingSfxField = new PropertyField(maxGlobalSkillLoopingSfxProp, "Max Skill Looping SFX");
            maxGlobalSkillLoopingSfxField.tooltip = "Global cap for looping PlaySFX actions from AgentSkill. 0 = unlimited.";
            maxGlobalSkillLoopingSfxField.Bind(serializedObject);
            audioFoldout.Add(maxGlobalSkillLoopingSfxField);

            var maxGlobalSkillVfxProp = serializedObject.FindProperty("maxGlobalSkillVFXInstances");
            var maxGlobalSkillVfxField = new PropertyField(maxGlobalSkillVfxProp, "Max Skill VFX Instances");
            maxGlobalSkillVfxField.tooltip = "Global cap for PlayVFX actions from AgentSkill. 0 = unlimited.";
            maxGlobalSkillVfxField.Bind(serializedObject);
            audioFoldout.Add(maxGlobalSkillVfxField);
            
            var audioHelp = new HelpBox(
                "These settings limit global crowd and AgentSkill audiovisual concurrency.\n\n" +
                "• Footstep limits control crowd movement audio spam\n" +
                "• Skill caps control AgentSkill PlaySFX/PlayVFX burst density\n" +
                "• Set skill caps to 0 for unlimited behavior",
                HelpBoxMessageType.Info
            );
            audioFoldout.Add(audioHelp);
            
            globalSection.Add(audioFoldout);
            
            // Spawn Settings
            var spawnFoldout = new Foldout { text = "Spawn Settings", value = false };
            
            var prefabMgrProp = serializedObject.FindProperty("prefabManager");
            var prefabMgrField = new PropertyField(prefabMgrProp);
            prefabMgrField.Bind(serializedObject);
            spawnFoldout.Add(prefabMgrField);
            
            var registerImmProp = serializedObject.FindProperty("registerImmediately");
            var registerImmField = new PropertyField(registerImmProp);
            registerImmField.Bind(serializedObject);
            spawnFoldout.Add(registerImmField);
            
            globalSection.Add(spawnFoldout);
            
            container.Add(globalSection);
            
            // Phase 3: Quick Actions for EnemyCrowd management
            if (!_isPlayMode)
            {
                var quickActionsSection = new Foldout { text = "Quick Actions", value = false };
                quickActionsSection.AddToClassList("section");
                
                var createCrowdButton = new Button(() => CreateNewEnemyCrowd());
                createCrowdButton.text = "Create New EnemyCrowd Asset";
                createCrowdButton.tooltip = "Creates a new EnemyCrowd ScriptableObject with default settings";
                createCrowdButton.AddToClassList("action-button");
                quickActionsSection.Add(createCrowdButton);
                
                if (crowdsProp.arraySize > 0)
                {
                    var editFirstButton = new Button(() => SelectFirstEnemyCrowd());
                    editFirstButton.text = "Edit First EnemyCrowd";
                    editFirstButton.tooltip = "Opens the first EnemyCrowd asset for editing";
                    editFirstButton.AddToClassList("action-button");
                    quickActionsSection.Add(editFirstButton);
                }
                
                container.Add(quickActionsSection);
            }
            
            // Settings Reference Box
            var settingsInfo = new HelpBox(
                "All behavior settings (Spawn, Navigation, Animation, Combat, Performance) are now configured " +
                "in the EnemyCrowd assets above. Select an EnemyCrowd asset to customize that faction's complete behavior.",
                HelpBoxMessageType.Info
            );
            container.Add(settingsInfo);
            
            return container;
        }

        private void CreateNewEnemyCrowd()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create EnemyCrowd Asset",
                "New Enemy Crowd",
                "asset",
                "Choose where to save the new EnemyCrowd asset"
            );
            
            if (!string.IsNullOrEmpty(path))
            {
                var newCrowd = ScriptableObject.CreateInstance<EnemyCrowd>();
                AssetDatabase.CreateAsset(newCrowd, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                EditorGUIUtility.PingObject(newCrowd);
                Selection.activeObject = newCrowd;
                
                Debug.Log($"[EnemyMasses] Phase 3: Created new EnemyCrowd asset at {path}");
            }
        }

        private void SelectFirstEnemyCrowd()
        {
            if (_controller.EnemyCrowds != null && _controller.EnemyCrowds.Count > 0)
            {
                var firstCrowd = _controller.EnemyCrowds[0];
                if (firstCrowd != null)
                {
                    Selection.activeObject = firstCrowd;
                    EditorGUIUtility.PingObject(firstCrowd);
                }
                else
                {
                    EditorUtility.DisplayDialog("Empty Slot", "The first EnemyCrowd slot is empty.", "OK");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("No EnemyCrowds", "No EnemyCrowd assets are assigned to this controller.", "OK");
            }
        }

        #endregion

        // Phase 3: Configuration Tab removed - all settings now in EnemyCrowd assets

        #region Spawn Tab

        private VisualElement CreateSpawnTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Spawn Parameters
            var paramsSection = new Foldout { text = "Spawn Parameters", value = true };
            paramsSection.AddToClassList("section");
            
            var countSlider = new SliderInt("Agent Count", 1, 500) { value = _spawnCount };
            countSlider.RegisterValueChangedCallback(evt => _spawnCount = evt.newValue);
            paramsSection.Add(countSlider);
            
            var countLabel = new Label($"{_spawnCount} agents");
            countLabel.AddToClassList("slider-value");
            countLabel.name = "spawn-count-label";
            countSlider.RegisterValueChangedCallback(evt => 
            {
                countLabel.text = $"{evt.newValue} agents";
            });
            paramsSection.Add(countLabel);
            
            int factionCount = _controller.GetFactionCount();
            if (factionCount > 0)
            {
                var factionSlider = new SliderInt("Faction", 0, factionCount - 1) { value = _factionIndex };
                factionSlider.RegisterValueChangedCallback(evt => _factionIndex = evt.newValue);
                paramsSection.Add(factionSlider);
                
                var factionLabel = new Label(GetFactionName(_factionIndex));
                factionLabel.AddToClassList("slider-value");
                factionLabel.name = "faction-label";
                factionSlider.RegisterValueChangedCallback(evt => 
                {
                    factionLabel.text = GetFactionName(evt.newValue);
                });
                paramsSection.Add(factionLabel);
            }
            
            var formationField = new EnumField("Formation", _formationType);
            formationField.RegisterValueChangedCallback(evt => _formationType = (FormationType)evt.newValue);
            paramsSection.Add(formationField);
            
            container.Add(paramsSection);
            
            // Quick Spawn
            var quickSpawn = new VisualElement();
            quickSpawn.AddToClassList("button-group");
            
            var spawnAtController = new Button(() => SpawnAt(_controller.transform.position));
            spawnAtController.text = "Spawn at Controller";
            spawnAtController.AddToClassList("primary-button");
            quickSpawn.Add(spawnAtController);
            
            var spawnAtCamera = new Button(SpawnAtCamera);
            spawnAtCamera.text = "Spawn at Camera";
            spawnAtCamera.AddToClassList("primary-button");
            quickSpawn.Add(spawnAtCamera);
            
            container.Add(quickSpawn);
            
            // Advanced Spawn
            var advancedSection = new Foldout { text = "Advanced Spawn Options", value = false };
            advancedSection.AddToClassList("section");
            
            var spawnAllBtn = new Button(SpawnAllFactions);
            spawnAllBtn.text = "Spawn All Factions";
            spawnAllBtn.AddToClassList("secondary-button");
            advancedSection.Add(spawnAllBtn);
            
            var stressSection = new Foldout { text = "Stress Test", value = false };
            stressSection.AddToClassList("subsection");
            
            var stressWarning = new HelpBox("High agent counts may impact performance", HelpBoxMessageType.Warning);
            stressSection.Add(stressWarning);
            
            var stressButtons = new VisualElement();
            stressButtons.AddToClassList("button-grid");
            
            stressButtons.Add(CreateStressButton("1,000", 1000));
            stressButtons.Add(CreateStressButton("2,000", 2000));
            stressButtons.Add(CreateStressButton("5,000", 5000));
            stressButtons.Add(CreateStressButton("10,000", 10000));
            
            stressSection.Add(stressButtons);
            advancedSection.Add(stressSection);
            
            container.Add(advancedSection);
            
            return container;
        }

        private Button CreateStressButton(string label, int count)
        {
            var btn = new Button(() => SpawnStressTest(count));
            btn.text = label;
            btn.AddToClassList("stress-button");
            return btn;
        }

        #endregion

        #region Control Tab

        private VisualElement CreateControlTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // === Faction Battle Mode Section (Primary Feature) ===
            var battleSection = new Foldout { text = "Faction Battle Mode", value = true };
            battleSection.AddToClassList("section");
            
            var battleDescription = new Label("Enable agents to fight each other instead of the player. Perfect for testing combat or creating battle scenarios.");
            battleDescription.AddToClassList("help-text");
            battleDescription.style.whiteSpace = WhiteSpace.Normal;
            battleDescription.style.marginBottom = 10;
            battleSection.Add(battleDescription);
            
            // Battle Mode Toggle
            var battleModeToggle = new Toggle("Enable Faction Battle");
            battleModeToggle.tooltip = "When enabled, agents will attack other agents instead of the player";
            battleModeToggle.BindProperty(serializedObject.FindProperty("factionBattleMode"));
            battleSection.Add(battleModeToggle);
            
            // Battle Type Dropdown
            var battleTypeField = new PropertyField(serializedObject.FindProperty("factionBattleType"), "Battle Type");
            battleTypeField.tooltip = "AllVsAll: Every agent for themselves (battle royale)\nFactionVsFaction: Teams fight each other";
            battleSection.Add(battleTypeField);
            
            // Target Update Interval
            var updateIntervalField = new PropertyField(serializedObject.FindProperty("factionBattleTargetUpdateInterval"), "Target Update Interval");
            updateIntervalField.tooltip = "How often agents reassign targets (seconds). Lower = more dynamic battles.";
            battleSection.Add(updateIntervalField);
            
            // Max Target Updates Per Frame (performance batching)
            var maxTargetUpdatesField = new PropertyField(serializedObject.FindProperty("maxFactionTargetUpdatesPerFrame"), "Max Target Updates/Frame");
            maxTargetUpdatesField.tooltip = "Max agents to process per frame for target assignment (0 = all at once).\nHigher values spread CPU load but delay target updates.\nRecommended: 50 for 200+ agents.";
            battleSection.Add(maxTargetUpdatesField);
            
            // Battle Status (runtime only)
            if (_isPlayMode)
            {
                var statusContainer = new VisualElement();
                statusContainer.style.marginTop = 15;
                statusContainer.style.paddingTop = 10;
                statusContainer.style.borderTopWidth = 1;
                statusContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
                
                var statusLabel = new Label("Battle Status");
                statusLabel.AddToClassList("section-header");
                statusContainer.Add(statusLabel);
                
                var battleStatusInfo = new Label();
                battleStatusInfo.name = "battle-status-info";
                battleStatusInfo.AddToClassList("debug-text");
                UpdateBattleStatusInfo(battleStatusInfo);
                statusContainer.Add(battleStatusInfo);
                
                // Faction Stats
                var factionStatsLabel = new Label();
                factionStatsLabel.name = "faction-stats";
                factionStatsLabel.AddToClassList("debug-text");
                factionStatsLabel.style.marginTop = 5;
                UpdateFactionStats(factionStatsLabel);
                statusContainer.Add(factionStatsLabel);
                
                // Quick Action Buttons
                var buttonRow = new VisualElement();
                buttonRow.style.flexDirection = FlexDirection.Row;
                buttonRow.style.marginTop = 10;
                
                var startBattleBtn = new Button(() => {
                    _controller.StartFactionBattle();
                    UpdateBattleStatusInfo(battleStatusInfo);
                    UpdateFactionStats(factionStatsLabel);
                });
                startBattleBtn.text = "Start Battle";
                startBattleBtn.AddToClassList("primary-button");
                startBattleBtn.style.flexGrow = 1;
                buttonRow.Add(startBattleBtn);
                
                var stopBattleBtn = new Button(() => {
                    _controller.StopFactionBattle();
                    UpdateBattleStatusInfo(battleStatusInfo);
                    UpdateFactionStats(factionStatsLabel);
                });
                stopBattleBtn.text = "Stop Battle";
                stopBattleBtn.AddToClassList("secondary-button");
                stopBattleBtn.style.flexGrow = 1;
                stopBattleBtn.style.marginLeft = 5;
                buttonRow.Add(stopBattleBtn);
                
                statusContainer.Add(buttonRow);
                
                var refreshStatsBtn = new Button(() => {
                    UpdateBattleStatusInfo(battleStatusInfo);
                    UpdateFactionStats(factionStatsLabel);
                });
                refreshStatsBtn.text = "Refresh Stats";
                refreshStatsBtn.AddToClassList("secondary-button");
                refreshStatsBtn.style.marginTop = 5;
                statusContainer.Add(refreshStatsBtn);
                
                battleSection.Add(statusContainer);
            }
            
            container.Add(battleSection);
            
            // === Runtime-Only Sections ===
            if (_isPlayMode)
            {
                // Agent Statistics
                var statsSection = new VisualElement();
                statsSection.AddToClassList("stats-panel");
                statsSection.style.marginTop = 15;
                
                var agentCount = _controller.GetAgentCount();
                var statsLabel2 = new Label($"Active Agents: {agentCount}");
                statsLabel2.name = "agent-count-display";
                statsLabel2.AddToClassList("stats-large");
                
                if (agentCount > 5000)
                    statsLabel2.AddToClassList("stats-critical");
                else if (agentCount > 2000)
                    statsLabel2.AddToClassList("stats-warning");
                else
                    statsLabel2.AddToClassList("stats-normal");
                
                statsSection.Add(statsLabel2);
                container.Add(statsSection);
                
                // Quick Actions
                var actionsSection = new Foldout { text = "Quick Actions", value = true };
                actionsSection.AddToClassList("section");
                
                var actionButtons = new VisualElement();
                actionButtons.AddToClassList("button-group");
                
                var pauseBtn = new Button(() => Debug.Log("Pause agents"));
                pauseBtn.text = "Pause All";
                pauseBtn.AddToClassList("secondary-button");
                actionButtons.Add(pauseBtn);
                
                var resumeBtn = new Button(() => Debug.Log("Resume agents"));
                resumeBtn.text = "Resume All";
                resumeBtn.AddToClassList("secondary-button");
                actionButtons.Add(resumeBtn);
                
                actionsSection.Add(actionButtons);
                
                var clearBtn = new Button(ClearAllAgents);
                clearBtn.text = "Clear All Agents";
                clearBtn.AddToClassList("danger-button");
                actionsSection.Add(clearBtn);
                
                container.Add(actionsSection);
                
                // Faction Control
                if (_controller.GetFactionCount() > 0)
                {
                    var factionSection = new Foldout { text = "Faction Control", value = false };
                    factionSection.AddToClassList("section");
                    
                    for (int i = 0; i < _controller.GetFactionCount(); i++)
                    {
                        var factionName = GetFactionName(i);
                        var factionItem = new Foldout { text = factionName, value = false };
                        factionItem.AddToClassList("faction-item");
                        
                        var info = new Label("Faction-specific controls coming soon");
                        info.AddToClassList("placeholder-text");
                        factionItem.Add(info);
                        
                        factionSection.Add(factionItem);
                    }
                    
                    container.Add(factionSection);
                }
            }
            else
            {
                // Edit Mode Info
                var editModeInfo = new HelpBox(
                    "Agent statistics and runtime controls will be available when you enter Play Mode.",
                    HelpBoxMessageType.Info);
                editModeInfo.style.marginTop = 15;
                container.Add(editModeInfo);
            }
            
            return container;
        }

        #endregion

        #region Debug Tab

        private VisualElement CreateDebugTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // === Editor Performance Section ===
            var gizmoSection = new Foldout { text = "Editor Performance", value = true };
            gizmoSection.AddToClassList("section");
            
            var gizmoWarning = new HelpBox(
                "Gizmos can significantly impact Editor performance with large crowds. " +
                "Disable gizmos when testing performance, or minimize the Scene view.",
                HelpBoxMessageType.Info);
            gizmoSection.Add(gizmoWarning);
            
            var enableGizmosField = new PropertyField(serializedObject.FindProperty("_enableGizmos"), "Enable Gizmos");
            enableGizmosField.tooltip = "Master toggle - disable for better Editor FPS with large crowds";
            gizmoSection.Add(enableGizmosField);
            
            var maxGizmosField = new PropertyField(serializedObject.FindProperty("_maxHitboxGizmosCount"), "Max Hitbox Gizmos");
            maxGizmosField.tooltip = "Limit how many hitbox gizmos are drawn (per-faction showDebugGizmos must also be enabled)";
            gizmoSection.Add(maxGizmosField);
            
            var wireframeField = new PropertyField(serializedObject.FindProperty("_hitboxGizmoWireframe"), "Wireframe Mode");
            wireframeField.tooltip = "Use wireframe instead of solid gizmos for better visibility";
            gizmoSection.Add(wireframeField);
            
            container.Add(gizmoSection);
            
            // === Debug Information Section ===
            var debugSection = new Foldout { text = "Debug Information", value = true };
            debugSection.AddToClassList("section");
            
            var info = new Label();
            info.name = "debug-info";
            info.AddToClassList("debug-text");
            UpdateDebugInfo(info);
            debugSection.Add(info);
            
            var refreshBtn = new Button(() => UpdateDebugInfo(info));
            refreshBtn.text = "Refresh";
            refreshBtn.AddToClassList("secondary-button");
            debugSection.Add(refreshBtn);

            // Navmesh impulse debug toggle (play mode only)
            var navmeshImpulseToggle = new Toggle("Navmesh Impulse Debug Logs")
            {
                value = EnemyMassesNavigationController.NavmeshImpulseDebug,
                tooltip = "When enabled, logs navmesh impulse apply/expire/settle events to the Console. Play mode only."
            };
            navmeshImpulseToggle.SetEnabled(Application.isPlaying);
            navmeshImpulseToggle.RegisterValueChangedCallback(evt =>
            {
                if (Application.isPlaying)
                {
                    EnemyMassesNavigationController.NavmeshImpulseDebug = evt.newValue;
                }
                navmeshImpulseToggle.SetValueWithoutNotify(EnemyMassesNavigationController.NavmeshImpulseDebug);
            });
            debugSection.Add(navmeshImpulseToggle);

            // Animation flicker debug logging
            var animDebugSection = new Foldout { text = "Animation Debug Logs", value = false };
            animDebugSection.AddToClassList("subsection");

            animDebugSection.Add(new HelpBox(
                "Logs animation/combat/navigation state for a single agent to diagnose flicker. " +
                "Use Selected Agent mode to target an agent in the RTS selection.",
                HelpBoxMessageType.Info));

            animDebugSection.Add(new PropertyField(serializedObject.FindProperty("_debugAnimationFlickerLogs"), "Enable Animation Debug Logs"));
            animDebugSection.Add(new PropertyField(serializedObject.FindProperty("_debugAnimationUseSelectedAgent"), "Use Selected Agent"));
            animDebugSection.Add(new PropertyField(serializedObject.FindProperty("_debugAnimationAgentIndex"), "Debug Agent Index"));
            animDebugSection.Add(new PropertyField(serializedObject.FindProperty("_debugAnimationLogOnlyOnChange"), "Log Only On Change"));
            animDebugSection.Add(new PropertyField(serializedObject.FindProperty("_debugAnimationLogInterval"), "Log Interval"));
            debugSection.Add(animDebugSection);
            
            container.Add(debugSection);
            
            // === GPU Instancer Debug Section ===
            var gpuiSection = new Foldout { text = "GPU Instancer", value = true };
            gpuiSection.AddToClassList("section");
            
            var gpuiInfo = new Label();
            gpuiInfo.name = "gpui-debug-info";
            gpuiInfo.AddToClassList("debug-text");
            UpdateGPUIDebugInfo(gpuiInfo);
            gpuiSection.Add(gpuiInfo);
            
            var fixInvisibleBtn = new Button(() =>
            {
                if (Application.isPlaying && _controller != null)
                {
                    int fixed_count = _controller.FixInvisibleAgents();
                    UpdateGPUIDebugInfo(gpuiInfo);
                    EditorUtility.DisplayDialog("Fix Invisible Agents", 
                        fixed_count > 0 ? $"Fixed {fixed_count} invisible agents." : "No invisible agents found.", 
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Fix Invisible Agents", "Only available in Play Mode.", "OK");
                }
            });
            fixInvisibleBtn.text = "Fix Invisible Agents";
            fixInvisibleBtn.tooltip = "Re-registers any agents that failed to register with GPU Instancer";
            fixInvisibleBtn.AddToClassList("secondary-button");
            gpuiSection.Add(fixInvisibleBtn);
            
            // Force sync ALL agents button - more aggressive fix
            var forceSyncBtn = new Button(() =>
            {
                if (Application.isPlaying && _controller != null)
                {
                    int fixed_count = _controller.FixInvisibleAgents(forceAllAgents: true);
                    UpdateGPUIDebugInfo(gpuiInfo);
                    EditorUtility.DisplayDialog("Force Sync All Agents", 
                        $"Force-synced {fixed_count} agents with GPUI.", 
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Force Sync All Agents", "Only available in Play Mode.", "OK");
                }
            });
            forceSyncBtn.text = "Force Sync ALL Agents";
            forceSyncBtn.tooltip = "Forces GPUI transform sync for ALL living agents, not just those detected as unregistered";
            forceSyncBtn.AddToClassList("secondary-button");
            gpuiSection.Add(forceSyncBtn);
            
            // Emergency fallback - enable all Unity renderers
            var emergencyBtn = new Button(() =>
            {
                if (Application.isPlaying && _controller != null)
                {
                    bool confirm = EditorUtility.DisplayDialog("Force Enable All Renderers", 
                        "This will enable Unity's default mesh renderers for all agents.\n\n" +
                        "WARNING: This bypasses GPU Instancer and will significantly impact performance!\n\n" +
                        "Only use this as a last resort if agents are still invisible after other fixes.", 
                        "Enable Renderers", "Cancel");
                    
                    if (confirm)
                    {
                        int enabled_count = _controller.ForceEnableAllRenderers();
                        UpdateGPUIDebugInfo(gpuiInfo);
                        EditorUtility.DisplayDialog("Force Enable Renderers", 
                            $"Enabled {enabled_count} renderers. Performance may be impacted.", 
                            "OK");
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Force Enable Renderers", "Only available in Play Mode.", "OK");
                }
            });
            emergencyBtn.text = "Emergency: Enable Renderers";
            emergencyBtn.tooltip = "Last resort - bypasses GPUI and enables Unity renderers directly. Will impact performance!";
            emergencyBtn.AddToClassList("danger-button");
            gpuiSection.Add(emergencyBtn);
            
            var refreshGpuiBtn = new Button(() => UpdateGPUIDebugInfo(gpuiInfo));
            refreshGpuiBtn.text = "Refresh GPUI Info";
            refreshGpuiBtn.AddToClassList("secondary-button");
            gpuiSection.Add(refreshGpuiBtn);
            
            container.Add(gpuiSection);
            
            // Performance Metrics
            var perfSection = new Foldout { text = "Performance", value = false };
            perfSection.AddToClassList("section");
            
            var perfLabel = new Label("Performance metrics coming soon");
            perfLabel.AddToClassList("placeholder-text");
            perfSection.Add(perfLabel);
            
            container.Add(perfSection);
            
            return container;
        }

        #endregion

        #region Network Tab

        private VisualElement CreateNetworkTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // === Network Status Section ===
            var statusSection = new Foldout { text = "Network Status", value = true };
            statusSection.AddToClassList("section");
            
            // Check if NetworkAuthorityProvider is available
            bool isNetworkMode = false;
            try
            {
                isNetworkMode = Arawn.EnemyMasses.Runtime.Networking.NetworkAuthorityProvider.IsNetworkMode;
            }
            catch { }
            
            var statusBox = new HelpBox(
                isNetworkMode 
                    ? "✓ Network Authority Active - Multiplayer mode enabled" 
                    : "○ No Network Authority - Running in single-player mode",
                isNetworkMode ? HelpBoxMessageType.Info : HelpBoxMessageType.None
            );
            statusSection.Add(statusBox);
            
            if (_isPlayMode)
            {
                // Runtime Network Stats
                var statsContainer = new VisualElement();
                statsContainer.style.marginTop = 10;
                
                var statsLabel = new Label();
                statsLabel.name = "network-stats";
                statsLabel.AddToClassList("debug-text");
                UpdateNetworkStats(statsLabel);
                statsContainer.Add(statsLabel);
                
                var refreshBtn = new Button(() => UpdateNetworkStats(statsLabel));
                refreshBtn.text = "Refresh Stats";
                refreshBtn.AddToClassList("secondary-button");
                refreshBtn.style.marginTop = 5;
                statsContainer.Add(refreshBtn);
                
                statusSection.Add(statsContainer);
            }
            
            container.Add(statusSection);
            
            // === Available Network Events Section ===
            var eventsSection = new Foldout { text = "Available Network Events", value = true };
            eventsSection.AddToClassList("section");
            
            var eventsInfo = new HelpBox(
                "These C# events are fired during gameplay and can be subscribed to by your network implementation. " +
                "Use NetworkAuthorityProvider to register your authority.",
                HelpBoxMessageType.Info
            );
            eventsSection.Add(eventsInfo);
            
            // Event list
            var eventsList = new VisualElement();
            eventsList.style.marginTop = 5;
            
            AddEventItem(eventsList, "NetworkEventAgentSpawned", "SpawnEventData", "Fired when agents are spawned - includes positions, rotations, network IDs");
            AddEventItem(eventsList, "NetworkEventDamageApplied", "DamageEventData", "Fired when damage is applied - includes attacker, target, damage amount");
            AddEventItem(eventsList, "NetworkEventAgentDied", "DeathEventData", "Fired when an agent dies - includes killer info, death position");
            AddEventItem(eventsList, "NetworkEventHealthChanged", "HealthChangeEventData", "Fired on any health change - includes old/new health, source");
            AddEventItem(eventsList, "NetworkEventStateChanged", "StateChangeEventData", "Fired on combat/navigation state changes");
            AddEventItem(eventsList, "NetworkEventAgentsDespawned", "uint[], DespawnReason", "Fired when agents are despawned");
            
            eventsSection.Add(eventsList);
            container.Add(eventsSection);
            
            // === Network Authority Interfaces Section ===
            var interfacesSection = new Foldout { text = "Network Authority Interfaces", value = false };
            interfacesSection.AddToClassList("section");
            
            var interfacesInfo = new HelpBox(
                "Implement these interfaces in your networking solution (Fusion, Mirror, Netcode, etc.) " +
                "and register with NetworkAuthorityProvider.SetAuthority().",
                HelpBoxMessageType.Info
            );
            interfacesSection.Add(interfacesInfo);
            
            var interfacesList = new VisualElement();
            interfacesList.style.marginTop = 5;
            
            AddInterfaceItem(interfacesList, "INetworkSpawnAuthority", "Server-authoritative agent spawning/despawning");
            AddInterfaceItem(interfacesList, "INetworkTransformAuthority", "Position sync at tick rate with interpolation");
            AddInterfaceItem(interfacesList, "INetworkDamageAuthority", "Damage validation with client prediction");
            AddInterfaceItem(interfacesList, "INetworkCommandAuthority", "RTS command routing (move, attack, stop)");
            AddInterfaceItem(interfacesList, "INetworkStateAuthority", "Batch agent state synchronization");
            AddInterfaceItem(interfacesList, "INetworkFogAuthority", "Per-player fog of war visibility");
            AddInterfaceItem(interfacesList, "INetworkProjectileAuthority", "Server-authoritative projectile hits");
            
            interfacesSection.Add(interfacesList);
            container.Add(interfacesSection);
            
            // === Quick Setup Section ===
            var setupSection = new Foldout { text = "Quick Setup", value = false };
            setupSection.AddToClassList("section");
            
            var setupInfo = new Label(
                "For single-player testing or as a reference implementation:\n\n" +
                "1. Add 'LocalNetworkAuthority' component to a GameObject\n" +
                "2. It auto-registers with NetworkAuthorityProvider on Start\n" +
                "3. Enable Debug Logging to see network events in Console\n\n" +
                "For multiplayer, create your own implementation of INetworkAuthorityExtended."
            );
            setupInfo.AddToClassList("guide-text");
            setupInfo.style.whiteSpace = WhiteSpace.Normal;
            setupSection.Add(setupInfo);
            
            var createLocalBtn = new Button(() =>
            {
                var go = new UnityEngine.GameObject("LocalNetworkAuthority");
                go.AddComponent<Arawn.EnemyMasses.Runtime.Networking.LocalNetworkAuthority>();
                Selection.activeGameObject = go;
                Undo.RegisterCreatedObjectUndo(go, "Create LocalNetworkAuthority");
            });
            createLocalBtn.text = "Create LocalNetworkAuthority GameObject";
            createLocalBtn.AddToClassList("primary-button");
            createLocalBtn.style.marginTop = 10;
            setupSection.Add(createLocalBtn);
            
            container.Add(setupSection);
            
            return container;
        }
        
        private void AddEventItem(VisualElement parent, string eventName, string dataType, string description)
        {
            var item = new VisualElement();
            item.style.marginBottom = 8;
            item.style.paddingLeft = 5;
            item.style.borderLeftWidth = 2;
            item.style.borderLeftColor = new UnityEngine.Color(0.3f, 0.6f, 0.9f);
            
            var header = new Label($"<b>{eventName}</b> ({dataType})");
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            item.Add(header);
            
            var desc = new Label(description);
            desc.style.fontSize = 10;
            desc.style.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
            desc.style.whiteSpace = WhiteSpace.Normal;
            item.Add(desc);
            
            parent.Add(item);
        }
        
        private void AddInterfaceItem(VisualElement parent, string interfaceName, string description)
        {
            var item = new VisualElement();
            item.style.marginBottom = 6;
            item.style.paddingLeft = 5;
            item.style.borderLeftWidth = 2;
            item.style.borderLeftColor = new UnityEngine.Color(0.9f, 0.6f, 0.3f);
            
            var header = new Label($"<b>{interfaceName}</b>");
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            item.Add(header);
            
            var desc = new Label(description);
            desc.style.fontSize = 10;
            desc.style.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
            item.Add(desc);
            
            parent.Add(item);
        }
        
        private void UpdateNetworkStats(Label label)
        {
            if (label == null || !Application.isPlaying || _controller == null)
            {
                if (label != null)
                    label.text = "Network stats available in Play Mode.";
                return;
            }
            
            var agents = _controller.GetAgents();
            int totalAgents = agents?.Count ?? 0;
            int networkedAgents = 0;
            int localAgents = 0;
            int ownedByPlayer0 = 0;
            int ownedByServer = 0;
            
            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    if (agent == null || agent.isDead) continue;
                    
                    if (agent.networkId != 0)
                    {
                        networkedAgents++;
                        if (agent.isNetworkAuthoritative)
                            localAgents++;
                    }
                    
                    if (agent.ownerPlayerId == 0)
                        ownedByPlayer0++;
                    else if (agent.ownerPlayerId == -1)
                        ownedByServer++;
                }
            }
            
            bool isNetworkMode = false;
            try { isNetworkMode = Arawn.EnemyMasses.Runtime.Networking.NetworkAuthorityProvider.IsNetworkMode; } catch { }
            
            label.text = $"Network Mode: {(isNetworkMode ? "Active" : "Inactive")}\n" +
                        $"Total Agents: {totalAgents}\n" +
                        $"With Network ID: {networkedAgents}\n" +
                        $"Locally Authoritative: {localAgents}\n" +
                        $"Owned by Player 0: {ownedByPlayer0}\n" +
                        $"Server/AI Owned: {ownedByServer}";
        }

        #endregion

        #region Helper Methods
        
        private void UpdateGPUIDebugInfo(Label label)
        {
            if (label == null) return;
            
            if (!Application.isPlaying || _controller == null)
            {
                label.text = "GPUI debug info only available in Play Mode.";
                return;
            }
            
            var agents = _controller.GetAgents();
            if (agents == null || agents.Count == 0)
            {
                label.text = "No agents spawned.";
                return;
            }
            
            int totalAgents = agents.Count;
            int instanced = 0;
            int notInstanced = 0;
            int invalidBuffer = 0;
            int noRenderKey = 0;
            int noPrefabComponent = 0;
            
            foreach (var agent in agents)
            {
                if (agent == null || agent.isDead) continue;
                
                if (agent.prefabComponent == null)
                {
                    noPrefabComponent++;
                    continue;
                }
                
                var gpuiPrefab = agent.prefabComponent;
                if (gpuiPrefab.IsInstanced)
                    instanced++;
                else
                    notInstanced++;
                    
                if (gpuiPrefab.bufferIndex < 0)
                    invalidBuffer++;
                    
                if (gpuiPrefab.renderKey == 0)
                    noRenderKey++;
            }
            
            string status = instanced == totalAgents ? "All agents visible" : "Some agents may be invisible";
            
            label.text = $"GPUI Status: {status}\n" +
                        $"Total Agents: {totalAgents}\n" +
                        $"Instanced: {instanced}\n" +
                        $"Not Instanced: {notInstanced}\n" +
                        $"Invalid Buffer Index: {invalidBuffer}\n" +
                        $"No Render Key: {noRenderKey}\n" +
                        $"No Prefab Component: {noPrefabComponent}";
        }

        private void UpdateBattleStatusInfo(Label label)
        {
            if (label == null || !Application.isPlaying) return;
            
            bool battleActive = _controller.FactionBattleMode;
            string battleType = _controller.FactionBattleType.ToString();
            int aliveAgents = _controller.GetTotalAliveAgentCount();
            int winner = _controller.GetFactionBattleWinner();
            
            string status;
            if (!battleActive)
            {
                status = "Battle Mode: INACTIVE";
            }
            else if (winner >= 0)
            {
                string winnerName = GetFactionName(winner);
                status = $"WINNER: {winnerName}!";
            }
            else if (aliveAgents <= 1)
            {
                status = aliveAgents == 1 ? "Last Agent Standing!" : "All agents eliminated";
            }
            else
            {
                status = $"Battle Mode: ACTIVE ({battleType})";
            }
            
            label.text = status;
        }

        private void UpdateFactionStats(Label label)
        {
            if (label == null || !Application.isPlaying) return;
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Faction Standings:");
            
            int factionCount = _controller.GetFactionCount();
            for (int i = 0; i < factionCount; i++)
            {
                int aliveCount = _controller.GetAliveAgentCountForFaction(i);
                string factionName = GetFactionName(i);
                string statusIcon = aliveCount > 0 ? "+" : "-";
                sb.AppendLine($"  {statusIcon} {factionName}: {aliveCount} alive");
            }
            
            label.text = sb.ToString();
        }

        #endregion

        #region Helper Methods

        private string GetFactionName(int index)
        {
            if (index >= 0 && index < _controller.GetFactionCount())
            {
                var faction = _controller.GetEnemyCrowdAtIndex(index);
                return faction != null ? faction.factionName : $"Faction {index}";
            }
            return "Unknown";
        }

        private void UpdateStatusLabel(Label label)
        {
            if (label == null) return;
            int count = _controller.GetAgentCount();
            label.text = $"Active: {count} agents";
        }

        private void UpdateDebugInfo(Label label)
        {
            if (label == null) return;
            
            string info = $"Total Agents: {_controller.GetAgentCount()}\n";
            info += $"Factions: {_controller.GetFactionCount()}\n";
            info += $"Initialized: {_controller.IsInitialized}\n";
            info += $"Time: {Time.time:F1}s";
            
            label.text = info;
        }

        private void UpdateRuntimeData()
        {
            // Ensure we're on the main thread and have valid references
            if (_root == null || _controller == null || !Application.isPlaying)
                return;
            
            try
            {
                var statusLabel = _root.Q<Label>("status-label");
                if (statusLabel != null && statusLabel.panel != null)
                    UpdateStatusLabel(statusLabel);
                
                var agentCountLabel = _root.Q<Label>("agent-count-display");
                if (agentCountLabel != null && agentCountLabel.panel != null)
                {
                    int count = _controller.GetAgentCount();
                    agentCountLabel.text = $"Active Agents: {count}";
                    
                    agentCountLabel.RemoveFromClassList("stats-normal");
                    agentCountLabel.RemoveFromClassList("stats-warning");
                    agentCountLabel.RemoveFromClassList("stats-critical");
                    
                    if (count > 5000)
                        agentCountLabel.AddToClassList("stats-critical");
                    else if (count > 2000)
                        agentCountLabel.AddToClassList("stats-warning");
                    else
                        agentCountLabel.AddToClassList("stats-normal");
                }
            }
            catch (System.Exception)
            {
                // Silently ignore UI toolkit threading issues during play mode
            }
        }

        #endregion

        #region Spawn Methods

        private void SpawnAt(Vector3 position)
        {
            _controller.SpawnEnemyCrowd(_factionIndex, position, Quaternion.identity, _spawnCount, _formationType);
        }

        private void SpawnAtCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("No main camera found!");
                return;
            }
            
            Vector3 spawnPos = cam.transform.position + cam.transform.forward * 10f;
            SpawnAt(spawnPos);
        }

        private void SpawnAllFactions()
        {
            Vector3 basePos = _controller.transform.position;
            int totalSpawned = 0;
            
            for (int i = 0; i < _controller.GetFactionCount(); i++)
            {
                Vector3 offset = new Vector3(i * 15f, 0, 0);
                var spawned = _controller.SpawnEnemyCrowd(i, basePos + offset, Quaternion.identity, 
                    _spawnCount / _controller.GetFactionCount(), _formationType);
                
                if (spawned != null)
                    totalSpawned += spawned.Count;
            }
            
            Debug.Log($"Spawned {totalSpawned} agents across all factions");
        }

        private void SpawnStressTest(int count)
        {
            if (!EditorUtility.DisplayDialog("Stress Test", 
                $"Spawn {count} agents? This may impact performance.", 
                "Spawn", "Cancel"))
                return;
            
            Vector3 basePos = _controller.transform.position;
            int perFaction = count / Mathf.Max(1, _controller.GetFactionCount());
            int totalSpawned = 0;
            
            for (int i = 0; i < _controller.GetFactionCount(); i++)
            {
                Vector3 offset = new Vector3(i * 20f, 0, 0);
                var spawned = _controller.SpawnEnemyCrowd(i, basePos + offset, Quaternion.identity, 
                    perFaction, FormationType.Random);
                
                if (spawned != null)
                    totalSpawned += spawned.Count;
            }
            
            Debug.Log($"Stress test: {totalSpawned} agents spawned");
        }

        private void ClearAllAgents()
        {
            if (EditorUtility.DisplayDialog("Clear All Agents", 
                $"Delete all {_controller.GetAgentCount()} agents?", 
                "Clear", "Cancel"))
            {
                _controller.ClearAllAgents();
                Debug.Log("All agents cleared");
            }
        }

        #endregion

        #region Performance Preset Helpers

        private void UpdatePresetDescription(Label label, PerformancePreset preset)
        {
            switch (preset)
            {
                case PerformancePreset.None:
                    label.text = "Full quality rendering with no CPU optimizations. Best visual quality but highest CPU usage.";
                    break;
                case PerformancePreset.Prioritized:
                    label.text = "Moderate optimizations with visible-only animation rotation for fairer visible updates under throttling.";
                    break;
                case PerformancePreset.Moderate:
                    label.text = "Light optimizations with visibility prioritization and spatial grid. Recommended for 100-300 agents.";
                    break;
                case PerformancePreset.Aggressive:
                    label.text = "Priority-based updates, spatial batching, and anti-gridlock. Recommended for 300-1000 agents.";
                    break;
                case PerformancePreset.VeryAggressive:
                    label.text = "Maximum optimizations including hybrid workflows. Recommended for 1000+ agents.";
                    break;
                case PerformancePreset.Custom:
                    label.text = "Fine-tune all performance settings manually. Expand the foldout below to configure.";
                    break;
            }
        }

        private void ApplyPerformancePreset(SerializedProperty perfProp, PerformancePreset preset)
        {
            switch (preset)
            {
                case PerformancePreset.None:
                    ApplyNonePreset(perfProp);
                    break;
                case PerformancePreset.Prioritized:
                    ApplyPrioritizedPreset(perfProp);
                    break;
                case PerformancePreset.Moderate:
                    ApplyModeratePreset(perfProp);
                    break;
                case PerformancePreset.Aggressive:
                    ApplyAggressivePreset(perfProp);
                    break;
                case PerformancePreset.VeryAggressive:
                    ApplyVeryAggressivePreset(perfProp);
                    break;
            }
        }

        private void ApplyNonePreset(SerializedProperty p)
        {
            p.FindPropertyRelative("prioritizeVisibleAgents").boolValue = false;
            p.FindPropertyRelative("rotateVisibleAnimationUpdates").boolValue = false;
            p.FindPropertyRelative("priorityMaxDistance").floatValue = 100f;
            p.FindPropertyRelative("enablePriorityBasedVisibility").boolValue = false;
            p.FindPropertyRelative("enableSpatialGrid").boolValue = false;
            p.FindPropertyRelative("enableSpatialBatching").boolValue = false;
            p.FindPropertyRelative("enableAdaptiveScaling").boolValue = false;
            p.FindPropertyRelative("enableBurstMode").boolValue = false;
            p.FindPropertyRelative("enableAntiGridlock").boolValue = false;
            p.FindPropertyRelative("enableLargeCrowdOptimizations").boolValue = false;
            p.FindPropertyRelative("enableHybridWorkflows").boolValue = false;
            
            p.FindPropertyRelative("frustumUpdateInterval").floatValue = 0.016f;
            p.FindPropertyRelative("visibilityUpdateInterval").floatValue = 0.016f;
            p.FindPropertyRelative("visibilityUpdateFraction").floatValue = 1.0f;
            
            p.FindPropertyRelative("highPriorityUpdateInterval").floatValue = 0.016f;
            p.FindPropertyRelative("highPriorityUpdateFraction").floatValue = 1.0f;
            p.FindPropertyRelative("mediumPriorityUpdateInterval").floatValue = 0.016f;
            p.FindPropertyRelative("mediumPriorityUpdateFraction").floatValue = 1.0f;
            p.FindPropertyRelative("lowPriorityUpdateInterval").floatValue = 0.016f;
            p.FindPropertyRelative("lowPriorityUpdateFraction").floatValue = 1.0f;
        }

        private void ApplyPrioritizedPreset(SerializedProperty p)
        {
            p.FindPropertyRelative("prioritizeVisibleAgents").boolValue = true;
            p.FindPropertyRelative("rotateVisibleAnimationUpdates").boolValue = true;
            p.FindPropertyRelative("priorityMaxDistance").floatValue = 50f;
            p.FindPropertyRelative("enablePriorityBasedVisibility").boolValue = false;
            p.FindPropertyRelative("enableSpatialGrid").boolValue = true;
            p.FindPropertyRelative("gridCellSize").floatValue = 10f;
            p.FindPropertyRelative("enableSpatialBatching").boolValue = false;
            p.FindPropertyRelative("enableAdaptiveScaling").boolValue = false;
            p.FindPropertyRelative("enableBurstMode").boolValue = false;
            p.FindPropertyRelative("enableAntiGridlock").boolValue = true;
            p.FindPropertyRelative("gridlockDetectionRadius").floatValue = 1.5f;
            p.FindPropertyRelative("gridlockAgentThreshold").intValue = 5;
            p.FindPropertyRelative("crowdedAreaCollisionMultiplier").floatValue = 0.5f;
            p.FindPropertyRelative("collisionUpdateInterval").floatValue = 0.5f;
            p.FindPropertyRelative("enableLargeCrowdOptimizations").boolValue = false;
            p.FindPropertyRelative("enableHybridWorkflows").boolValue = false;
            
            p.FindPropertyRelative("frustumUpdateInterval").floatValue = 0.033f;
            p.FindPropertyRelative("visibilityUpdateInterval").floatValue = 0.1f;
            p.FindPropertyRelative("visibilityUpdateFraction").floatValue = 0.5f;
            
            p.FindPropertyRelative("highPriorityUpdateInterval").floatValue = 0.033f;
            p.FindPropertyRelative("highPriorityUpdateFraction").floatValue = 0.8f;
            p.FindPropertyRelative("highPriorityDistance").floatValue = 30f;
            p.FindPropertyRelative("mediumPriorityUpdateInterval").floatValue = 0.066f;
            p.FindPropertyRelative("mediumPriorityUpdateFraction").floatValue = 0.6f;
            p.FindPropertyRelative("mediumPriorityDistance").floatValue = 60f;
            p.FindPropertyRelative("lowPriorityUpdateInterval").floatValue = 0.1f;
            p.FindPropertyRelative("lowPriorityUpdateFraction").floatValue = 0.4f;
        }

        private void ApplyModeratePreset(SerializedProperty p)
        {
            p.FindPropertyRelative("prioritizeVisibleAgents").boolValue = true;
            p.FindPropertyRelative("rotateVisibleAnimationUpdates").boolValue = false;
            p.FindPropertyRelative("priorityMaxDistance").floatValue = 50f;
            p.FindPropertyRelative("enablePriorityBasedVisibility").boolValue = false;
            p.FindPropertyRelative("enableSpatialGrid").boolValue = true;
            p.FindPropertyRelative("gridCellSize").floatValue = 10f;
            p.FindPropertyRelative("enableSpatialBatching").boolValue = false;
            p.FindPropertyRelative("enableAdaptiveScaling").boolValue = false;
            p.FindPropertyRelative("enableBurstMode").boolValue = false;
            p.FindPropertyRelative("enableAntiGridlock").boolValue = true;
            p.FindPropertyRelative("gridlockDetectionRadius").floatValue = 1.5f;
            p.FindPropertyRelative("gridlockAgentThreshold").intValue = 5;
            p.FindPropertyRelative("crowdedAreaCollisionMultiplier").floatValue = 0.5f;
            p.FindPropertyRelative("collisionUpdateInterval").floatValue = 0.5f;
            p.FindPropertyRelative("enableLargeCrowdOptimizations").boolValue = false;
            p.FindPropertyRelative("enableHybridWorkflows").boolValue = false;
            
            p.FindPropertyRelative("frustumUpdateInterval").floatValue = 0.033f;
            p.FindPropertyRelative("visibilityUpdateInterval").floatValue = 0.1f;
            p.FindPropertyRelative("visibilityUpdateFraction").floatValue = 0.5f;
            
            p.FindPropertyRelative("highPriorityUpdateInterval").floatValue = 0.033f;
            p.FindPropertyRelative("highPriorityUpdateFraction").floatValue = 0.8f;
            p.FindPropertyRelative("highPriorityDistance").floatValue = 30f;
            p.FindPropertyRelative("mediumPriorityUpdateInterval").floatValue = 0.066f;
            p.FindPropertyRelative("mediumPriorityUpdateFraction").floatValue = 0.6f;
            p.FindPropertyRelative("mediumPriorityDistance").floatValue = 60f;
            p.FindPropertyRelative("lowPriorityUpdateInterval").floatValue = 0.1f;
            p.FindPropertyRelative("lowPriorityUpdateFraction").floatValue = 0.4f;
        }

        private void ApplyAggressivePreset(SerializedProperty p)
        {
            p.FindPropertyRelative("prioritizeVisibleAgents").boolValue = true;
            p.FindPropertyRelative("rotateVisibleAnimationUpdates").boolValue = false;
            p.FindPropertyRelative("priorityMaxDistance").floatValue = 40f;
            p.FindPropertyRelative("enablePriorityBasedVisibility").boolValue = true;
            p.FindPropertyRelative("enablePriorityFailsafe").boolValue = true;
            p.FindPropertyRelative("failsafeUpdateInterval").floatValue = 0.2f;
            p.FindPropertyRelative("enableSpatialGrid").boolValue = true;
            p.FindPropertyRelative("gridCellSize").floatValue = 10f;
            p.FindPropertyRelative("enableSpatialBatching").boolValue = true;
            p.FindPropertyRelative("maxAgentsPerCellPerFrame").intValue = 10;
            p.FindPropertyRelative("enableAdaptiveScaling").boolValue = true;
            p.FindPropertyRelative("enableBurstMode").boolValue = true;
            p.FindPropertyRelative("enableAntiGridlock").boolValue = true;
            p.FindPropertyRelative("gridlockDetectionRadius").floatValue = 1.5f;
            p.FindPropertyRelative("gridlockAgentThreshold").intValue = 4;
            p.FindPropertyRelative("crowdedAreaCollisionMultiplier").floatValue = 0.3f;
            p.FindPropertyRelative("collisionUpdateInterval").floatValue = 0.3f;
            p.FindPropertyRelative("enableLargeCrowdOptimizations").boolValue = false;
            p.FindPropertyRelative("enableHybridWorkflows").boolValue = false;
            
            p.FindPropertyRelative("frustumUpdateInterval").floatValue = 0.05f;
            p.FindPropertyRelative("visibilityUpdateInterval").floatValue = 0.15f;
            p.FindPropertyRelative("visibilityUpdateFraction").floatValue = 0.3f;
            
            p.FindPropertyRelative("highPriorityUpdateInterval").floatValue = 0.05f;
            p.FindPropertyRelative("highPriorityUpdateFraction").floatValue = 0.7f;
            p.FindPropertyRelative("highPriorityDistance").floatValue = 25f;
            p.FindPropertyRelative("mediumPriorityUpdateInterval").floatValue = 0.1f;
            p.FindPropertyRelative("mediumPriorityUpdateFraction").floatValue = 0.5f;
            p.FindPropertyRelative("mediumPriorityDistance").floatValue = 50f;
            p.FindPropertyRelative("lowPriorityUpdateInterval").floatValue = 0.15f;
            p.FindPropertyRelative("lowPriorityUpdateFraction").floatValue = 0.3f;
        }

        private void ApplyVeryAggressivePreset(SerializedProperty p)
        {
            p.FindPropertyRelative("prioritizeVisibleAgents").boolValue = true;
            p.FindPropertyRelative("rotateVisibleAnimationUpdates").boolValue = false;
            p.FindPropertyRelative("priorityMaxDistance").floatValue = 30f;
            p.FindPropertyRelative("enablePriorityBasedVisibility").boolValue = true;
            p.FindPropertyRelative("enablePriorityFailsafe").boolValue = true;
            p.FindPropertyRelative("failsafeUpdateInterval").floatValue = 0.3f;
            p.FindPropertyRelative("enableSpatialGrid").boolValue = true;
            p.FindPropertyRelative("gridCellSize").floatValue = 15f;
            p.FindPropertyRelative("enableSpatialBatching").boolValue = true;
            p.FindPropertyRelative("maxAgentsPerCellPerFrame").intValue = 5;
            p.FindPropertyRelative("enableAdaptiveScaling").boolValue = true;
            p.FindPropertyRelative("enableBurstMode").boolValue = true;
            p.FindPropertyRelative("enableAntiGridlock").boolValue = true;
            p.FindPropertyRelative("gridlockDetectionRadius").floatValue = 2f;
            p.FindPropertyRelative("gridlockAgentThreshold").intValue = 3;
            p.FindPropertyRelative("crowdedAreaCollisionMultiplier").floatValue = 0.2f;
            p.FindPropertyRelative("collisionUpdateInterval").floatValue = 0.2f;
            p.FindPropertyRelative("enableLargeCrowdOptimizations").boolValue = true;
            p.FindPropertyRelative("largeCrowdThreshold").intValue = 1000;
            p.FindPropertyRelative("enableHybridWorkflows").boolValue = true;
            p.FindPropertyRelative("hybridCloseDistance").floatValue = 20f;
            p.FindPropertyRelative("hybridMediumDistance").floatValue = 40f;
            p.FindPropertyRelative("hybridUpdateInterval").floatValue = 0.5f;
            p.FindPropertyRelative("maxCloseRangeAgents").intValue = 10;
            p.FindPropertyRelative("maxMediumRangeAgents").intValue = 30;
            
            p.FindPropertyRelative("frustumUpdateInterval").floatValue = 0.1f;
            p.FindPropertyRelative("visibilityUpdateInterval").floatValue = 0.2f;
            p.FindPropertyRelative("visibilityUpdateFraction").floatValue = 0.2f;
            
            p.FindPropertyRelative("highPriorityUpdateInterval").floatValue = 0.066f;
            p.FindPropertyRelative("highPriorityUpdateFraction").floatValue = 0.6f;
            p.FindPropertyRelative("highPriorityDistance").floatValue = 20f;
            p.FindPropertyRelative("mediumPriorityUpdateInterval").floatValue = 0.133f;
            p.FindPropertyRelative("mediumPriorityUpdateFraction").floatValue = 0.4f;
            p.FindPropertyRelative("mediumPriorityDistance").floatValue = 40f;
            p.FindPropertyRelative("lowPriorityUpdateInterval").floatValue = 0.2f;
            p.FindPropertyRelative("lowPriorityUpdateFraction").floatValue = 0.2f;
        }

        #endregion
    }
}
