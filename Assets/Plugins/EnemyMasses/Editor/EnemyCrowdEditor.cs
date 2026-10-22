using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
using GPUInstancerPro.PrefabModule;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Civil;
using Arawn.EnemyMasses.Runtime.Factions;
using Arawn.EnemyMasses.Runtime.Skills;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Editor.Validation;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Modern UI Toolkit-based custom editor for EnemyCrowd ScriptableObjects.
    /// Automatically adds required components (GPUIPrefab, GPUICrowdInstance, NavMeshAgent) 
    /// to the referenced prefab when assigned.
    /// </summary>
    [CustomEditor(typeof(Runtime.EnemyCrowd))]
    public class EnemyCrowdEditor : UnityEditor.Editor
    {
        private VisualElement _root;
        private VisualElement _contentContainer;
        private VisualElement _validationSummaryContainer;
        private string _currentTab = "basic";
        private bool _isRefreshing = false;
        private int _lastAttackModeValue = -1;
        private int _lastSecondaryModeValue = -1;
        private bool _lastEnableSecondaryValue = false;
        private Button _animationsTabButton;
        
        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Plugins/EnemyMasses/Editor/EnemyCrowdEditorStyle.uss");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);
            
            // Create header with validation summary
            _root.Add(CreateHeader());
            
            // Create validation summary container (will be updated dynamically)
            _validationSummaryContainer = new VisualElement();
            _validationSummaryContainer.name = "validation-summary";
            _root.Add(_validationSummaryContainer);
            RefreshValidationSummary();
            
            // Create tab bar
            _root.Add(CreateTabBar());
            
            // Create content container
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("content-container");
            _root.Add(_contentContainer);
            
            // Show initial tab
            ShowTab(_currentTab);
            
            // Register callback to refresh validation when serialized object changes
            _root.RegisterCallback<SerializedPropertyChangeEvent>(evt => RefreshValidationSummary());
            
            return _root;
        }
        
        /// <summary>
        /// Refreshes the validation summary display.
        /// </summary>
        private void RefreshValidationSummary()
        {
            if (_validationSummaryContainer == null) return;
            
            _validationSummaryContainer.Clear();
            serializedObject.Update();
            
            var summary = EnemyCrowdValidationRules.Validate(serializedObject);
            var summaryElement = EditorFieldValidation.CreateValidationSummaryElement(summary);
            _validationSummaryContainer.Add(summaryElement);
        }
        
        private VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("header");
            
            var titleLabel = new Label("Enemy Crowd Faction");
            titleLabel.AddToClassList("header-title");
            header.Add(titleLabel);
            
            var factionNameProp = serializedObject.FindProperty("factionName");
            var factionName = factionNameProp.stringValue;
            if (!string.IsNullOrEmpty(factionName))
            {
                var subtitleLabel = new Label(factionName);
                subtitleLabel.AddToClassList("header-subtitle");
                header.Add(subtitleLabel);
            }
            
            return header;
        }
        
        private VisualElement CreateTabBar()
        {
            var tabBar = new VisualElement();
            tabBar.AddToClassList("tab-bar");
            
            AddTab(tabBar, "basic", "Basic Info");
            AddTab(tabBar, "configuration", "Configuration");
            AddTab(tabBar, "combat", "Combat");
            AddTab(tabBar, "skills", "Skills");
            AddTab(tabBar, "animations", "Animations");
            _animationsTabButton = tabBar.Query<Button>(className: "tab-button").ToList().FindLast(b => b.text == "Animations");
            AddTab(tabBar, "civil", "Civil");
            
            return tabBar;
        }

        private void AddTab(VisualElement parent, string tabId, string tabLabel)
        {
            var button = new Button(() => ShowTab(tabId));
            button.text = tabLabel;
            button.AddToClassList("tab-button");
            if (_currentTab == tabId)
                button.AddToClassList("tab-active");
            parent.Add(button);
        }
        
        private void ShowTab(string tabId)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            
            try
            {
                _currentTab = tabId;
                _contentContainer.Clear();
                
                // Update tab button states
                var tabButtons = _root.Query<Button>(className: "tab-button").ToList();
                foreach (var btn in tabButtons)
                {
                    btn.RemoveFromClassList("tab-active");
                }
                
                VisualElement content = null;
                try
                {
                    content = tabId switch
                    {
                        "basic" => CreateBasicTab(),
                        "configuration" => CreateConfigurationTab(),
                        "combat" => CreateCombatTab(),
                        "skills" => CreateSkillsTab(),
                        "animations" => CreateAnimationsTab(),
                        "civil" => CreateCivilTab(),
                        _ => CreateBasicTab()
                    };
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[EnemyCrowdEditor] Error creating {tabId} tab: {ex.Message}\n{ex.StackTrace}");
                    content = new HelpBox($"Error loading {tabId} tab: {ex.Message}", HelpBoxMessageType.Error);
                }
            
                if (content != null)
                {
                    _contentContainer.Add(content);
                    EnemyCrowdEditorMeta.Apply(content);
                
                    // Re-highlight active tab
                    var activeTabButton = tabButtons.Find(b => b.text == tabId switch
                    {
                        "basic" => "Basic Info",
                        "configuration" => "Configuration",
                        "combat" => "Combat",
                        "skills" => "Skills",
                        "animations" => "Animations",
                        "civil" => "Civil",
                        _ => "Basic Info"
                    });
                    activeTabButton?.AddToClassList("tab-active");
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }
        
        #region Basic Tab
        
        private VisualElement CreateBasicTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Identity Section
            var identitySection = new Foldout { text = "Identity", value = true };
            identitySection.AddToClassList("section");
            
            // Required: Faction Name (validated)
            AddValidatedField(identitySection, "factionName", "Faction Name", 
                isRequired: true, 
                validator: EditorFieldValidation.ValidateStringNotEmpty,
                incompleteMessage: "Enter a faction name (e.g., 'Orcs', 'Undead')");
            
            var enemyTypeField = new PropertyField(serializedObject.FindProperty("enemyTypeName"));
            enemyTypeField.Bind(serializedObject);
            identitySection.Add(enemyTypeField);

            var localizationSection = new Foldout { text = "Localization (Optional)", value = false };
            localizationSection.AddToClassList("subsection");

            var localizationInfo = new HelpBox(
                "Optional Unity Localization integration.\n" +
                "When com.unity.localization is installed, table/key values are resolved at runtime.\n" +
                "If missing or unresolved, EnemyCrowd falls back to the raw text fields.",
                HelpBoxMessageType.Info);
            localizationSection.Add(localizationInfo);

            AddBoundField(localizationSection, "factionNameLocalization.enabled");
            AddBoundField(localizationSection, "factionNameLocalization.tableCollection");
            AddBoundField(localizationSection, "factionNameLocalization.entryKey");
            AddBoundField(localizationSection, "factionNameLocalization.fallbackToSourceText");

            AddBoundField(localizationSection, "enemyTypeNameLocalization.enabled");
            AddBoundField(localizationSection, "enemyTypeNameLocalization.tableCollection");
            AddBoundField(localizationSection, "enemyTypeNameLocalization.entryKey");
            AddBoundField(localizationSection, "enemyTypeNameLocalization.fallbackToSourceText");

            identitySection.Add(localizationSection);
            
            var colorField = new PropertyField(serializedObject.FindProperty("factionColor"));
            colorField.Bind(serializedObject);
            identitySection.Add(colorField);
            
            container.Add(identitySection);
            
            // Portrait Section
            var portraitSection = new Foldout { text = "Portrait", value = true };
            portraitSection.AddToClassList("section");
            
            var portraitInfo = new HelpBox(
                "Manual: assign your own portrait texture.\n" +
                "Auto Generate: creates a portrait from the prefab.\n" +
                "  • Humanoid models: captures head, neck, and shoulders.\n" +
                "  • Non-humanoid models: captures the full object.",
                HelpBoxMessageType.Info);
            portraitSection.Add(portraitInfo);
            
            // Portrait Mode dropdown
            var portraitModeProp = serializedObject.FindProperty("portraitMode");
            var portraitModeField = new PropertyField(portraitModeProp, "Portrait Mode");
            portraitModeField.Bind(serializedObject);
            portraitSection.Add(portraitModeField);
            
            // Portrait texture field
            var portraitProp = serializedObject.FindProperty("portrait");
            var portraitField = new PropertyField(portraitProp, "Portrait");
            portraitField.Bind(serializedObject);
            portraitSection.Add(portraitField);
            
            // Preview image (shows current portrait)
            var portraitPreviewContainer = new VisualElement();
            portraitPreviewContainer.name = "portrait-preview-container";
            portraitPreviewContainer.style.alignItems = Align.Center;
            portraitPreviewContainer.style.marginTop = 5;
            portraitPreviewContainer.style.marginBottom = 5;
            
            var portraitPreviewImage = new UnityEngine.UIElements.Image();
            portraitPreviewImage.name = "portrait-preview";
            portraitPreviewImage.style.width = 128;
            portraitPreviewImage.style.height = 128;
            portraitPreviewImage.style.borderTopWidth = 1;
            portraitPreviewImage.style.borderBottomWidth = 1;
            portraitPreviewImage.style.borderLeftWidth = 1;
            portraitPreviewImage.style.borderRightWidth = 1;
            portraitPreviewImage.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            portraitPreviewImage.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            portraitPreviewImage.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);
            portraitPreviewImage.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
            
            // Set initial preview
            if (portraitProp.objectReferenceValue is Texture2D currentTex)
                portraitPreviewImage.image = currentTex;
            
            portraitPreviewContainer.Add(portraitPreviewImage);
            portraitSection.Add(portraitPreviewContainer);
            
            // Track portrait property changes to update preview
            portraitPreviewContainer.TrackPropertyValue(portraitProp, prop =>
            {
                portraitPreviewImage.image = prop.objectReferenceValue as Texture2D;
            });
            
            // Auto-generate button (shown when mode is AutoGenerate)
            var generateButtonContainer = new VisualElement();
            generateButtonContainer.name = "generate-button-container";
            generateButtonContainer.style.marginTop = 5;
            
            var generateButton = new UnityEngine.UIElements.Button(() =>
            {
                serializedObject.Update();
                var prefabPropForGen = serializedObject.FindProperty("prefab");
                var portraitPropForGen = serializedObject.FindProperty("portrait");
                
                if (prefabPropForGen.objectReferenceValue == null)
                {
                    EditorUtility.DisplayDialog("Portrait Generator", 
                        "Please assign a prefab first before generating a portrait.", "OK");
                    return;
                }
                
                var crowd = (Runtime.EnemyCrowd)target;
                Texture2D generated = EnemyCrowdPortraitGenerator.GeneratePortrait(
                    (GameObject)prefabPropForGen.objectReferenceValue, crowd);
                
                if (generated != null)
                {
                    portraitPropForGen.objectReferenceValue = generated;
                    serializedObject.ApplyModifiedProperties();
                    portraitPreviewImage.image = generated;
                    EditorUtility.DisplayDialog("Portrait Generator", 
                        "Portrait generated successfully!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Portrait Generator", 
                        "Failed to generate portrait. Check the console for details.", "OK");
                }
            })
            {
                text = "Generate Portrait from Prefab",
                tooltip = "Auto-generates a portrait by rendering the assigned prefab.\n" +
                          "Humanoid: captures head/neck/shoulders.\nNon-humanoid: captures the full model."
            };
            generateButton.style.height = 30;
            generateButtonContainer.Add(generateButton);
            
            // Show/hide generate button based on mode
            void UpdatePortraitModeVisibility()
            {
                serializedObject.Update();
                bool isAutoMode = portraitModeProp.enumValueIndex == (int)Runtime.PortraitMode.AutoGenerate;
                generateButtonContainer.style.display = isAutoMode 
                    ? DisplayStyle.Flex 
                    : DisplayStyle.None;
            }
            UpdatePortraitModeVisibility();
            
            // Track mode changes
            generateButtonContainer.TrackPropertyValue(portraitModeProp, _ => UpdatePortraitModeVisibility());
            
            portraitSection.Add(generateButtonContainer);
            container.Add(portraitSection);
            
            // Navigation System Section (before Prefab Setup so user knows what component to expect)
            var navSystemSection = new Foldout { text = "Navigation System", value = true };
            navSystemSection.AddToClassList("section");
            
            var navSystemInfo = new HelpBox(
                "Select which navigation system to use. The prefab must have the matching navigation component.",
                HelpBoxMessageType.Info);
            navSystemSection.Add(navSystemInfo);
            
            // Custom navigation type dropdown that handles A* availability
            AddNavigationTypeField(navSystemSection);
            
            container.Add(navSystemSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // AGENT ARCHETYPE SECTION
            // ═══════════════════════════════════════════════════════════════════
            var archetypeSection = new Foldout { text = "Agent Archetype", value = true };
            archetypeSection.AddToClassList("section");
            
            var archetypeInfo = new HelpBox(
                "Defines the fundamental body type and movement mode.\n" +
                "• Humanoid: Standard bipedal characters (soldiers, zombies).\n" +
                "• FlyingHumanoid: Humanoids that spawn airborne (angels, jetpack troops).\n" +
                "• Flying Vehicle: Non-humanoid flyers with no locomotion animations (spaceships, drones).\n" +
                "• Ground Vehicle: Non-humanoid ground entities (tanks, mechs).",
                HelpBoxMessageType.Info);
            archetypeSection.Add(archetypeInfo);
            
            AddBoundField(archetypeSection, "agentArchetype");
            AddBoundField(archetypeSection, "persistentFlight");
            
            // 3D Flight sub-section — only visible when archetype requires 3D navigation
            var flight3DSubsection = new Foldout { text = "3D Flight Settings", value = true };
            flight3DSubsection.AddToClassList("subsection");
            
            var flight3DInfo = new HelpBox(
                "Configure volumetric 3D flight behavior. These settings only apply when " +
                "the agent archetype uses 3D navigation (Flying Vehicle, or FlyingHumanoid with Persistent Flight).",
                HelpBoxMessageType.Info);
            flight3DSubsection.Add(flight3DInfo);
            
            AddBoundField(flight3DSubsection, "flight3DNavigationType");
            AddBoundField(flight3DSubsection, "cruisingAltitude");
            AddBoundField(flight3DSubsection, "altitudeVariance");
            AddBoundField(flight3DSubsection, "verticalSpeed");
            AddBoundField(flight3DSubsection, "bankAngle");
            AddBoundField(flight3DSubsection, "bankSpeed");
            AddBoundField(flight3DSubsection, "headingSmoothRate");
            AddBoundField(flight3DSubsection, "maxPitchAngle");
            AddBoundField(flight3DSubsection, "rotationAxisLock");
            AddBoundField(flight3DSubsection, "hoverBobAmplitude");
            AddBoundField(flight3DSubsection, "hoverBobFrequency");
            AddBoundField(flight3DSubsection, "maintainAltitude");
            
            // 3D Formation sub-section
            var formation3DSubsection = new Foldout { text = "3D Formation Layers", value = false };
            formation3DSubsection.AddToClassList("subsection");
            
            AddBoundField(formation3DSubsection, "formationLayers");
            AddBoundField(formation3DSubsection, "formationLayerSpacing");
            flight3DSubsection.Add(formation3DSubsection);
            
            archetypeSection.Add(flight3DSubsection);
            
            // Vehicle / Non-Humanoid sub-section
            var vehicleSubsection = new Foldout { text = "Vehicle / Non-Humanoid Settings", value = false };
            vehicleSubsection.AddToClassList("subsection");
            
            AddBoundField(vehicleSubsection, "hitboxPreset");
            AddBoundField(vehicleSubsection, "vehicleBoundingRadius");
            AddBoundField(vehicleSubsection, "vehicleBoundingHeight");
            
            archetypeSection.Add(vehicleSubsection);
            
            // Visibility logic: show/hide sub-sections based on archetype
            var agentArchetypeProp = serializedObject.FindProperty("agentArchetype");
            var persistentFlightProp = serializedObject.FindProperty("persistentFlight");
            
            void UpdateArchetypeVisibility()
            {
                serializedObject.Update();
                var currentArchetype = (AgentArchetype)agentArchetypeProp.intValue;
                bool isPersistent = persistentFlightProp.boolValue;
                
                bool showFlight3D = currentArchetype == AgentArchetype.FlyingVehicle || 
                                    (currentArchetype == AgentArchetype.FlyingHumanoid && isPersistent);
                bool showVehicle = currentArchetype == AgentArchetype.FlyingVehicle || 
                                   currentArchetype == AgentArchetype.GroundVehicle;
                bool showPersistentFlight = currentArchetype == AgentArchetype.FlyingHumanoid || 
                                            currentArchetype == AgentArchetype.FlyingVehicle;
                
                flight3DSubsection.style.display = showFlight3D ? DisplayStyle.Flex : DisplayStyle.None;
                vehicleSubsection.style.display = showVehicle ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Auto-set hitbox preset when switching to/from non-humanoid
                var hitboxProp = serializedObject.FindProperty("hitboxPreset");
                if (hitboxProp != null)
                {
                    bool isNonHumanoidNow = currentArchetype == AgentArchetype.FlyingVehicle || 
                                            currentArchetype == AgentArchetype.GroundVehicle;
                    var currentPreset = (HitboxPreset)hitboxProp.intValue;
                    if (isNonHumanoidNow && currentPreset == HitboxPreset.Humanoid)
                    {
                        hitboxProp.intValue = (int)HitboxPreset.Vehicle;
                        serializedObject.ApplyModifiedProperties();
                    }
                    else if (!isNonHumanoidNow && currentPreset == HitboxPreset.Vehicle)
                    {
                        hitboxProp.intValue = (int)HitboxPreset.Humanoid;
                        serializedObject.ApplyModifiedProperties();
                    }
                }
                
                // Find the persistentFlight field and show/hide it
                foreach (var field in archetypeSection.Query<PropertyField>().ToList())
                {
                    if (field.bindingPath == "persistentFlight")
                        field.style.display = showPersistentFlight ? DisplayStyle.Flex : DisplayStyle.None;
                }
                
                // Hide the Animations tab for non-humanoid archetypes (no animation clips needed)
                bool isNonHumanoid = currentArchetype == AgentArchetype.FlyingVehicle || 
                                     currentArchetype == AgentArchetype.GroundVehicle;
                if (_animationsTabButton != null)
                {
                    _animationsTabButton.style.display = isNonHumanoid ? DisplayStyle.None : DisplayStyle.Flex;
                    
                    // If currently viewing the animations tab and archetype changed to non-humanoid, redirect
                    if (isNonHumanoid && _currentTab == "animations")
                    {
                        ShowTab("configuration");
                    }
                }
            }
            UpdateArchetypeVisibility();
            
            // Track changes to archetype and persistent flight
            archetypeSection.TrackPropertyValue(agentArchetypeProp, _ => UpdateArchetypeVisibility());
            archetypeSection.TrackPropertyValue(persistentFlightProp, _ => UpdateArchetypeVisibility());
            
            container.Add(archetypeSection);
            
            // Prefab Setup Section
            var prefabSection = new Foldout { text = "Prefab Setup", value = true };
            prefabSection.AddToClassList("section");
            
            // Required: Prefab (validated)
            AddValidatedField(prefabSection, "prefab", "Prefab", 
                isRequired: true, 
                validator: EditorFieldValidation.ValidateObjectNotNull,
                incompleteMessage: "Assign a character prefab for this crowd");
            
            var componentStatusContainer = new VisualElement();
            componentStatusContainer.name = "component-status-container";
            
            void RefreshComponentStatus()
            {
                serializedObject.UpdateIfRequiredOrScript();
                componentStatusContainer.Clear();
                componentStatusContainer.Add(CreateComponentStatusUI());
            }
            
            RefreshComponentStatus();
            prefabSection.Add(componentStatusContainer);
            
            // Keep component status in sync with prefab/navigation changes immediately.
            // TrackPropertyValue fires after the serialized value is committed.
            var prefabProp = serializedObject.FindProperty("prefab");
            if (prefabProp != null)
            {
                componentStatusContainer.TrackPropertyValue(prefabProp, _ =>
                {
                    RefreshComponentStatus();
                    EnsureAStarUpdatePositionOnPrefab();
                });
            }
            
            var navigationTypeProp = serializedObject.FindProperty("navigationType");
            if (navigationTypeProp != null)
            {
                componentStatusContainer.TrackPropertyValue(navigationTypeProp, _ =>
                {
                    RefreshComponentStatus();
                    EnsureAStarUpdatePositionOnPrefab();
                });
            }
            
            container.Add(prefabSection);
            
            // Formation Section
            var formationSection = new Foldout { text = "Formation Overrides", value = false };
            formationSection.AddToClassList("section");
            
            formationSection.Add(new PropertyField(serializedObject.FindProperty("overrideFormation")) { bindingPath = "overrideFormation" });
            formationSection.Add(new PropertyField(serializedObject.FindProperty("formationType")) { bindingPath = "formationType" });
            formationSection.Add(new PropertyField(serializedObject.FindProperty("formationTypeOverride")) { bindingPath = "formationTypeOverride" });
            formationSection.Bind(serializedObject);
            
            container.Add(formationSection);
            
            // Visual Section
            var visualSection = new Foldout { text = "Visual Customization", value = false };
            visualSection.AddToClassList("section");
            
            visualSection.Add(new PropertyField(serializedObject.FindProperty("factionMaterial")) { bindingPath = "factionMaterial" });
            visualSection.Bind(serializedObject);
            
            container.Add(visualSection);
            
            // Minimap Color Override Section
            var minimapColorSection = new Foldout { text = "Minimap Color Override", value = false };
            minimapColorSection.AddToClassList("section");
            
            var minimapColorInfo = new HelpBox(
                "Override how this faction appears on the minimap.\n" +
                "• Use Enemy Type Color Override for same-faction different-role units (Elite vs Regular)\n" +
                "• Use Owner Player Color for PvP/multi-player where each player's units need distinct colors",
                HelpBoxMessageType.Info);
            minimapColorSection.Add(minimapColorInfo);
            
            // Use Minimap Color Override toggle
            var useMinimapOverrideField = new PropertyField(
                serializedObject.FindProperty("useMinimapColorOverride"), 
                "Use Enemy Type Color Override");
            useMinimapOverrideField.tooltip = "Enable to use a custom color on the minimap instead of the faction color.";
            minimapColorSection.Add(useMinimapOverrideField);
            
            // Color override field
            var minimapOverrideColorField = new PropertyField(
                serializedObject.FindProperty("minimapColorOverride"), 
                "Override Color");
            minimapOverrideColorField.tooltip = "Custom color for this enemy type on the minimap.";
            minimapColorSection.Add(minimapOverrideColorField);
            
            // Separator
            var separator = new VisualElement();
            separator.style.height = 10;
            minimapColorSection.Add(separator);
            
            // Use Owner Player Color toggle
            var useOwnerColorField = new PropertyField(
                serializedObject.FindProperty("useOwnerPlayerColor"), 
                "Use Owner Player Color");
            useOwnerColorField.tooltip = "When enabled, units show their owner player's color (from PlayerRegistry) on the minimap. " +
                "Useful for PvP or multi-player where each player's units should be visually distinct.";
            minimapColorSection.Add(useOwnerColorField);
            
            var ownerColorNote = new HelpBox(
                "Owner Player Color requires players to be registered in PlayerRegistry.\n" +
                "Each player's color is set via PlayerRegistry.RegisterPlayer() or SetPlayerColor().\n" +
                "This takes priority over Enemy Type Color Override when enabled.",
                HelpBoxMessageType.None);
            ownerColorNote.style.marginTop = 5;
            minimapColorSection.Add(ownerColorNote);
            
            minimapColorSection.Bind(serializedObject);
            container.Add(minimapColorSection);
            
            // Faction Relationships Section
            var factionRelSection = new Foldout { text = "Faction Relationships", value = true };
            factionRelSection.AddToClassList("section");
            
            var factionRelInfo = new HelpBox(
                "Configure how this faction relates to players and other factions.\n" +
                "• Use the built-in system for simple friend/foe relationships\n" +
                "• Or integrate with Love/Hate or GC2 Factions for advanced systems",
                HelpBoxMessageType.Info);
            factionRelSection.Add(factionRelInfo);
            
            // Faction Relationship Config
            var factionRelConfigProp = serializedObject.FindProperty("factionRelationshipConfig");
            if (factionRelConfigProp != null)
            {
                // Quick Preset Buttons
                var presetSection = new VisualElement();
                presetSection.style.marginBottom = 10;
                presetSection.style.marginTop = 5;
                
                var presetLabel = new Label("Quick Presets:");
                presetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                presetLabel.style.marginBottom = 5;
                presetSection.Add(presetLabel);
                
                var presetButtonRow = new VisualElement();
                presetButtonRow.style.flexDirection = FlexDirection.Row;
                presetButtonRow.style.flexWrap = Wrap.Wrap;
                
                // Enemy Faction Preset
                var enemyPresetBtn = new Button(() => ApplyFactionPreset(factionRelConfigProp, FactionPreset.EnemyFaction))
                {
                    text = "Enemy",
                    tooltip = "Standard enemy faction: Hostile to player and other factions."
                };
                enemyPresetBtn.style.marginRight = 5;
                enemyPresetBtn.style.marginBottom = 5;
                presetButtonRow.Add(enemyPresetBtn);
                
                // Ally Faction Preset
                var allyPresetBtn = new Button(() => ApplyFactionPreset(factionRelConfigProp, FactionPreset.AllyFaction))
                {
                    text = "Ally",
                    tooltip = "Friendly faction: Allied with player, hostile to enemies."
                };
                allyPresetBtn.style.marginRight = 5;
                allyPresetBtn.style.marginBottom = 5;
                presetButtonRow.Add(allyPresetBtn);
                
                // Neutral Faction Preset
                var neutralPresetBtn = new Button(() => ApplyFactionPreset(factionRelConfigProp, FactionPreset.NeutralFaction))
                {
                    text = "Neutral",
                    tooltip = "Non-hostile faction: Won't attack unless provoked."
                };
                neutralPresetBtn.style.marginRight = 5;
                neutralPresetBtn.style.marginBottom = 5;
                presetButtonRow.Add(neutralPresetBtn);
                
                // Player's Own Faction Preset
                var playerFactionBtn = new Button(() => ApplyFactionPreset(factionRelConfigProp, FactionPreset.PlayerOwned))
                {
                    text = "Player's Own",
                    tooltip = "Player-owned faction: For units the player directly controls."
                };
                playerFactionBtn.style.marginRight = 5;
                playerFactionBtn.style.marginBottom = 5;
                presetButtonRow.Add(playerFactionBtn);
                
                // Competitive Multi-Player Preset
                var competitiveBtn = new Button(() => ApplyFactionPreset(factionRelConfigProp, FactionPreset.CompetitiveMultiplayer))
                {
                    text = "PvP",
                    tooltip = "Competitive: Same faction but different owners are enemies."
                };
                competitiveBtn.style.marginBottom = 5;
                presetButtonRow.Add(competitiveBtn);
                
                presetSection.Add(presetButtonRow);
                factionRelSection.Add(presetSection);
                
                // Example Setup Guide (Foldout)
                var examplesSection = new Foldout { text = "Setup Examples & Guide", value = false };
                examplesSection.style.marginBottom = 10;
                
                var examplesText = new Label(
                    "═══════════════════════════════════════════\n" +
                    "COMMON FACTION SETUPS:\n" +
                    "═══════════════════════════════════════════\n\n" +
                    
                    "ENEMY FACTION (Monsters, Bandits, etc.)\n" +
                    "   - Player Relationship: Enemy\n" +
                    "   - Default Faction Relationship: Enemy\n" +
                    "   - Same Faction Diff. Owner: Ally\n" +
                    "   - Same Owner = Ally: Yes\n" +
                    "   -> Attacks player and other factions\n\n" +
                    
                    "ALLY FACTION (Guards, Friendly NPCs)\n" +
                    "   - Player Relationship: Ally\n" +
                    "   - Default Faction Relationship: Enemy\n" +
                    "   - Same Faction Diff. Owner: Ally\n" +
                    "   - Same Owner = Ally: Yes\n" +
                    "   -> Helps player, attacks enemies\n\n" +
                    
                    "NEUTRAL FACTION (Wildlife, Merchants)\n" +
                    "   - Player Relationship: Neutral\n" +
                    "   - Default Faction Relationship: Neutral\n" +
                    "   - Same Faction Diff. Owner: Ally\n" +
                    "   - Same Owner = Ally: Yes\n" +
                    "   -> Won't attack unless provoked\n\n" +
                    
                    "PLAYER'S OWN FACTION (Summons, Pets)\n" +
                    "   - Player Relationship: Self\n" +
                    "   - Default Faction Relationship: Enemy\n" +
                    "   - Same Faction Diff. Owner: Ally\n" +
                    "   - Same Owner = Ally: Yes\n" +
                    "   -> Treats player as self, attacks enemies\n\n" +
                    
                    "═══════════════════════════════════════════\n" +
                    "MULTIPLAYER SCENARIOS:\n" +
                    "═══════════════════════════════════════════\n\n" +
                    
                    "CO-OP (Same team, different players)\n" +
                    "   - Same Faction Diff. Owner: Ally\n" +
                    "   - Same Owner = Ally: Yes\n" +
                    "   -> All players' units work together\n\n" +
                    
                    "COMPETITIVE/PVP (Players fight each other)\n" +
                    "   - Same Faction Diff. Owner: Enemy\n" +
                    "   - Same Owner = Ally: Yes\n" +
                    "   -> Each player's units fight other players'\n\n" +
                    
                    "═══════════════════════════════════════════\n" +
                    "SPECIFIC RELATIONSHIPS:\n" +
                    "═══════════════════════════════════════════\n\n" +
                    
                    "To make specific factions interact differently:\n" +
                    "1. Expand 'Specific Faction Relationships'\n" +
                    "2. Add entry with target faction name\n" +
                    "3. Set the desired relationship\n\n" +
                    
                    "Example: Wolves and Bears are both enemies\n" +
                    "to player, but neutral to each other:\n" +
                    "   On 'Wolves' faction:\n" +
                    "   - Add specific: 'Bears' → Neutral\n" +
                    "   On 'Bears' faction:\n" +
                    "   - Add specific: 'Wolves' → Neutral"
                );
#if UNITY_6000_0_OR_NEWER
                examplesText.style.whiteSpace = WhiteSpace.PreWrap;
#else
                examplesText.style.whiteSpace = WhiteSpace.Normal;
#endif
                examplesText.style.fontSize = 11;
                examplesText.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
                examplesText.style.paddingTop = 10;
                examplesText.style.paddingBottom = 10;
                examplesText.style.paddingLeft = 10;
                examplesText.style.paddingRight = 10;
                examplesText.style.borderTopLeftRadius = 5;
                examplesText.style.borderTopRightRadius = 5;
                examplesText.style.borderBottomLeftRadius = 5;
                examplesText.style.borderBottomRightRadius = 5;
                examplesSection.Add(examplesText);
                
                factionRelSection.Add(examplesSection);
                
                // Player Relationship
                var playerRelField = new PropertyField(factionRelConfigProp.FindPropertyRelative("defaultPlayerRelationship"), "Player Relationship");
                playerRelField.tooltip = "How this faction relates to player characters by default.";
                factionRelSection.Add(playerRelField);
                
                // Default Faction Relationship
                var defaultFactionField = new PropertyField(factionRelConfigProp.FindPropertyRelative("defaultFactionRelationship"), "Default Faction Relationship");
                defaultFactionField.tooltip = "Default relationship toward other factions when no specific relationship is defined.";
                factionRelSection.Add(defaultFactionField);
                
                // Same Faction Different Owner
                var sameFactionField = new PropertyField(factionRelConfigProp.FindPropertyRelative("sameFactionDifferentOwner"), "Same Faction Diff. Owner");
                sameFactionField.tooltip = "Relationship between agents of the same faction but different player owners (multiplayer scenarios).";
                factionRelSection.Add(sameFactionField);
                
                // Same Owner Always Ally
                var sameOwnerField = new PropertyField(factionRelConfigProp.FindPropertyRelative("sameOwnerAlwaysAlly"), "Same Owner = Ally");
                sameOwnerField.tooltip = "When enabled, agents with the same owner are always allies regardless of faction.";
                factionRelSection.Add(sameOwnerField);
                
                // Specific Relationships
                var specificRelSection = new Foldout { text = "Specific Faction Relationships", value = false };
                specificRelSection.AddToClassList("subsection");
                
                var specificRelHelp = new Label("Override default relationships for specific factions by name.");
                specificRelHelp.style.whiteSpace = WhiteSpace.Normal;
                specificRelHelp.style.marginBottom = 5;
                specificRelHelp.style.color = new Color(0.5f, 0.5f, 0.5f);
                specificRelHelp.style.fontSize = 10;
                specificRelSection.Add(specificRelHelp);
                
                var specificRelField = new PropertyField(factionRelConfigProp.FindPropertyRelative("specificRelationships"));
                specificRelSection.Add(specificRelField);
                
                factionRelSection.Add(specificRelSection);
            }
            else
            {
                factionRelSection.Add(new HelpBox("factionRelationshipConfig property not found.", HelpBoxMessageType.Warning));
            }
            
            factionRelSection.Bind(serializedObject);
            container.Add(factionRelSection);
            
            return container;
        }
        
        private VisualElement CreateVirtualHitboxSection()
        {
            var hitboxSection = new Foldout { text = "Virtual Hitbox System", value = false };
            hitboxSection.AddToClassList("section");
            
            // Description
            var description = new HelpBox(
                "Zone-based damage without colliders. Define body zones (head, torso, legs) with different damage multipliers. " +
                "Headshots can deal critical damage, leg shots reduced damage, etc.",
                HelpBoxMessageType.Info);
            hitboxSection.Add(description);
            
            // Main enable toggle
            var enabledProp = serializedObject.FindProperty("virtualHitbox.enabled");
            var enabledField = new PropertyField(enabledProp, "Enable Hitbox System");
            enabledField.bindingPath = "virtualHitbox.enabled";
            hitboxSection.Add(enabledField);
            
            // Container for settings (shown only when enabled)
            var settingsContainer = new VisualElement();
            settingsContainer.name = "hitbox-settings";
            settingsContainer.style.marginLeft = 15;
            
            // Agent height
            var heightProp = serializedObject.FindProperty("virtualHitbox.agentHeight");
            var heightField = new PropertyField(heightProp, "Agent Height");
            heightField.bindingPath = "virtualHitbox.agentHeight";
            settingsContainer.Add(heightField);
            
            // Default multiplier
            var defaultMultProp = serializedObject.FindProperty("virtualHitbox.defaultDamageMultiplier");
            var defaultMultField = new PropertyField(defaultMultProp, "Default Damage Multiplier");
            defaultMultField.bindingPath = "virtualHitbox.defaultDamageMultiplier";
            settingsContainer.Add(defaultMultField);
            
            // Hit zones array
            var zonesProp = serializedObject.FindProperty("virtualHitbox.hitZones");
            var zonesField = new PropertyField(zonesProp, "Hit Zones");
            zonesField.bindingPath = "virtualHitbox.hitZones";
            settingsContainer.Add(zonesField);
            
            // Quick setup button
            var setupButton = new Button(() =>
            {
                serializedObject.Update();
                var hitboxProp = serializedObject.FindProperty("virtualHitbox");
                var zonesPropLocal = hitboxProp.FindPropertyRelative("hitZones");
                
                if (zonesPropLocal.arraySize == 0)
                {
                    // Add default humanoid zones with hit chances (head=10%, torso=70%, legs=20%)
                    zonesPropLocal.arraySize = 3;
                    
                    // Head zone - 10% hit chance, 2x damage (critical)
                    SetHitboxZone(zonesPropLocal.GetArrayElementAtIndex(0), "Head", 1.5f, 1.9f, 0.15f, 2f, true, 0.1f, new Color(1f, 0f, 0f, 0.4f));
                    
                    // Torso zone - 70% hit chance, 1x damage
                    SetHitboxZone(zonesPropLocal.GetArrayElementAtIndex(1), "Torso", 0.8f, 1.5f, 0.25f, 1f, false, 0.7f, new Color(0f, 1f, 0f, 0.3f));
                    
                    // Legs zone - 20% hit chance, 0.7x damage
                    SetHitboxZone(zonesPropLocal.GetArrayElementAtIndex(2), "Legs", 0f, 0.8f, 0.2f, 0.7f, false, 0.2f, new Color(0f, 0.5f, 1f, 0.3f));
                    
                    serializedObject.ApplyModifiedProperties();
                }
            });
            setupButton.text = "Initialize Default Humanoid Zones";
            setupButton.tooltip = "Creates head (2x critical), torso (1x), and legs (0.7x) zones";
            settingsContainer.Add(setupButton);
            
            // Visual feedback section
            var vfxSection = new Foldout { text = "Critical Hit Effects", value = false };
            vfxSection.AddToClassList("subsection");
            
            vfxSection.Add(new PropertyField(serializedObject.FindProperty("virtualHitbox.criticalHitVFXPrefab")) { bindingPath = "virtualHitbox.criticalHitVFXPrefab" });
            vfxSection.Add(new PropertyField(serializedObject.FindProperty("virtualHitbox.criticalHitVFXDuration")) { bindingPath = "virtualHitbox.criticalHitVFXDuration" });
            vfxSection.Add(new PropertyField(serializedObject.FindProperty("virtualHitbox.criticalHitSound")) { bindingPath = "virtualHitbox.criticalHitSound" });
            vfxSection.Add(new PropertyField(serializedObject.FindProperty("virtualHitbox.criticalHitSoundVolume")) { bindingPath = "virtualHitbox.criticalHitSoundVolume" });
            vfxSection.Bind(serializedObject);
            settingsContainer.Add(vfxSection);
            
            // Debug section
            var debugSection = new Foldout { text = "Debug Visualization", value = false };
            debugSection.AddToClassList("subsection");
            
            debugSection.Add(new PropertyField(serializedObject.FindProperty("virtualHitbox.showDebugGizmos")) { bindingPath = "virtualHitbox.showDebugGizmos" });
            debugSection.Add(new PropertyField(serializedObject.FindProperty("virtualHitbox.debugGizmoAlpha")) { bindingPath = "virtualHitbox.debugGizmoAlpha" });
            debugSection.Bind(serializedObject);
            settingsContainer.Add(debugSection);
            
            hitboxSection.Add(settingsContainer);
            
            // Update visibility based on enabled state
            enabledField.RegisterCallback<ChangeEvent<bool>>(evt =>
            {
                settingsContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            // Set initial visibility
            settingsContainer.style.display = enabledProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            
            hitboxSection.Bind(serializedObject);
            
            return hitboxSection;
        }
        
        private void SetHitboxZone(SerializedProperty zoneProp, string name, float minHeight, float maxHeight, float radius, float damageMultiplier, bool isCritical, float hitChance, Color debugColor)
        {
            zoneProp.FindPropertyRelative("zoneName").stringValue = name;
            zoneProp.FindPropertyRelative("minHeight").floatValue = minHeight;
            zoneProp.FindPropertyRelative("maxHeight").floatValue = maxHeight;
            zoneProp.FindPropertyRelative("radius").floatValue = radius;
            zoneProp.FindPropertyRelative("damageMultiplier").floatValue = damageMultiplier;
            zoneProp.FindPropertyRelative("isCriticalZone").boolValue = isCritical;
            zoneProp.FindPropertyRelative("hitChance").floatValue = hitChance;
            zoneProp.FindPropertyRelative("debugColor").colorValue = debugColor;
        }
        
        private VisualElement CreateComponentStatusUI()
        {
            serializedObject.UpdateIfRequiredOrScript();
            
            var statusContainer = new VisualElement();
            statusContainer.AddToClassList("component-status");
            
            var prefabProp = serializedObject.FindProperty("prefab");
            var prefab = prefabProp.objectReferenceValue as GameObject;
            
            if (prefab == null)
            {
                var warning = new HelpBox("Assign a prefab to check component status", HelpBoxMessageType.Warning);
                statusContainer.Add(warning);
                return statusContainer;
            }
            
            bool hasGPUIPrefab = prefab.GetComponent<GPUIPrefab>() != null;
            bool hasGPUICrowdInstance = prefab.GetComponent<GPUICrowdInstance>() != null;
            
            // Check for all navigation components
            bool hasNavMeshAgent = prefab.GetComponent<NavMeshAgent>() != null;
            bool hasFlowFieldAdapter = prefab.GetComponent<Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.FlowFieldAgentAdapter>() != null;
#if ASTAR_EXISTS
            bool hasAIPath = prefab.GetComponent<Pathfinding.AIPath>() != null;
            bool hasRichAI = prefab.GetComponent<Pathfinding.RichAI>() != null;
            bool hasAILerp = prefab.GetComponent<Pathfinding.AILerp>() != null;
            bool hasRVOController = prefab.GetComponent<Pathfinding.RVO.RVOController>() != null;
#else
            bool hasAIPath = false;
            bool hasRichAI = false;
            bool hasAILerp = false;
            bool hasRVOController = false;
#endif
            
            // Check for navigation component based on NavigationType
            var navigationTypeProp = serializedObject.FindProperty("navigationType");
            var navigationType = (NavigationType)navigationTypeProp.intValue;
            
            bool hasCorrectNavigationComponent = false;
            string requiredComponentName = "NavMeshAgent";
            string conflictingComponentName = null;
            bool requiresRvoController = false;
            
            switch (navigationType)
            {
                case NavigationType.UnityNavMesh:
                    hasCorrectNavigationComponent = hasNavMeshAgent;
                    requiredComponentName = "NavMeshAgent";
                    // Check for conflicting A* components
                    if (hasAIPath) conflictingComponentName = "AIPath (A*)";
                    else if (hasRichAI) conflictingComponentName = "RichAI (A*)";
                    else if (hasAILerp) conflictingComponentName = "AILerp (A*)";
                    break;
                case NavigationType.FlowField:
                    hasCorrectNavigationComponent = hasFlowFieldAdapter;
                    requiredComponentName = "FlowFieldAgentAdapter";
                    if (hasNavMeshAgent) conflictingComponentName = "NavMeshAgent";
                    else if (hasAIPath) conflictingComponentName = "AIPath (A*)";
                    else if (hasRichAI) conflictingComponentName = "RichAI (A*)";
                    else if (hasAILerp) conflictingComponentName = "AILerp (A*)";
                    break;
#if ASTAR_EXISTS
                case NavigationType.AStarAIPath:
                    hasCorrectNavigationComponent = hasAIPath;
                    requiredComponentName = "AIPath (A*)";
                    if (hasNavMeshAgent) conflictingComponentName = "NavMeshAgent";
                    else if (hasRichAI) conflictingComponentName = "RichAI (A*)";
                    else if (hasAILerp) conflictingComponentName = "AILerp (A*)";
                    break;
                case NavigationType.AStarRichAI:
                    hasCorrectNavigationComponent = hasRichAI;
                    requiredComponentName = "RichAI (A*)";
                    if (hasNavMeshAgent) conflictingComponentName = "NavMeshAgent";
                    else if (hasAIPath) conflictingComponentName = "AIPath (A*)";
                    else if (hasAILerp) conflictingComponentName = "AILerp (A*)";
                    break;
                case NavigationType.AStarAILerp:
                    hasCorrectNavigationComponent = hasAILerp;
                    requiredComponentName = "AILerp (A*)";
                    if (hasNavMeshAgent) conflictingComponentName = "NavMeshAgent";
                    else if (hasAIPath) conflictingComponentName = "AIPath (A*)";
                    else if (hasRichAI) conflictingComponentName = "RichAI (A*)";
                    break;
                case NavigationType.AStarRVO:
                    hasCorrectNavigationComponent = hasAIPath || hasRichAI;
                    requiredComponentName = "AIPath or RichAI (A*)";
                    requiresRvoController = true;
                    if (hasNavMeshAgent) conflictingComponentName = "NavMeshAgent";
                    else if (hasAILerp) conflictingComponentName = "AILerp (A*)";
                    break;
#endif
                case NavigationType.Flight3D:
                    // Flight3DNavigationAgent is created at runtime by NavigationAgentFactory — no prefab component needed
                    hasCorrectNavigationComponent = true;
                    requiredComponentName = "Flight3DNavigationAgent (runtime)";
                    if (hasNavMeshAgent) conflictingComponentName = "NavMeshAgent";
                    else if (hasAIPath) conflictingComponentName = "AIPath (A*)";
                    else if (hasRichAI) conflictingComponentName = "RichAI (A*)";
                    else if (hasAILerp) conflictingComponentName = "AILerp (A*)";
                    break;
                default:
                    hasCorrectNavigationComponent = hasNavMeshAgent;
                    break;
            }
            
            // Determine if GPUICrowdInstance is needed (only for humanoid archetypes with animations)
            var archetypeProp = serializedObject.FindProperty("agentArchetype");
            var archetype = (AgentArchetype)archetypeProp.intValue;
            bool isNonHumanoid = archetype == AgentArchetype.FlyingVehicle || archetype == AgentArchetype.GroundVehicle;
            
            var statusLabel = new Label("Required Components:");
            statusLabel.AddToClassList("config-description");
            statusContainer.Add(statusLabel);
            
            statusContainer.Add(CreateStatusRow("GPUIPrefab", hasGPUIPrefab));
            if (!isNonHumanoid)
            {
                statusContainer.Add(CreateStatusRow("GPUICrowdInstance", hasGPUICrowdInstance));
            }
            else
            {
                statusContainer.Add(CreateStatusRow("GPUICrowdInstance (optional)", hasGPUICrowdInstance || true));
            }
            statusContainer.Add(CreateStatusRow(requiredComponentName, hasCorrectNavigationComponent));
            if (requiresRvoController)
            {
                statusContainer.Add(CreateStatusRow("RVOController (A*)", hasRVOController));
            }
            
            // Show warning if there's a conflicting navigation component
            if (conflictingComponentName != null)
            {
                var conflictRow = new VisualElement();
                conflictRow.style.flexDirection = FlexDirection.Row;
                conflictRow.style.justifyContent = Justify.SpaceBetween;
                conflictRow.style.marginBottom = 2;
                
                var conflictLabel = new Label(conflictingComponentName);
                conflictRow.Add(conflictLabel);
                
                var conflictStatus = new Label("[!] Wrong Type");
                conflictStatus.style.color = new Color(1f, 0.6f, 0.2f); // Orange warning color
                conflictRow.Add(conflictStatus);
                
                statusContainer.Add(conflictRow);
                
                var conflictWarning = new HelpBox(
                    $"Prefab has {conflictingComponentName} but NavigationType is set to {navigationType.GetDisplayName()}. " +
                    "Consider changing NavigationType in Configuration tab or removing the conflicting component from the prefab.",
                    HelpBoxMessageType.Warning);
                statusContainer.Add(conflictWarning);
            }
            
            bool needsCrowdInstance = !isNonHumanoid && !hasGPUICrowdInstance;
            if (!hasGPUIPrefab || needsCrowdInstance || !hasCorrectNavigationComponent || (requiresRvoController && !hasRVOController))
            {
                var addButton = new Button(() => 
                { 
                    EnsureRequiredComponents(prefab);
                    // Update component status after adding components
                    var container = _root.Q<VisualElement>("component-status-container");
                    if (container != null)
                    {
                        container.Clear();
                        container.Add(CreateComponentStatusUI());
                    }
                });
                addButton.text = "Add Missing Components";
                addButton.AddToClassList("primary-button");
                statusContainer.Add(addButton);
            }
            else
            {
                var info = new HelpBox("All required components present", HelpBoxMessageType.Info);
                statusContainer.Add(info);
            }
            
            return statusContainer;
        }
        
        private VisualElement CreateStatusRow(string componentName, bool present)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 2;
            
            var label = new Label(componentName);
            row.Add(label);
            
            var status = new Label(present ? "[OK] Present" : "[X] Missing");
            status.style.color = present ? new Color(0.4f, 0.8f, 0.4f) : new Color(1f, 0.4f, 0.4f);
            row.Add(status);
            
            return row;
        }
        
        #endregion
        
        #region Configuration Tab
        
        private VisualElement CreateConfigurationTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            var infoBox = new HelpBox(
                "Phase 3 Refined: Per-faction behavior settings (Navigation, Combat, Animations). " +
                "Global settings (Performance, Animation Culling, Prefab Manager) are in the Controller.",
                HelpBoxMessageType.Info
            );
            container.Add(infoBox);
            
            // Spawn Settings (only per-faction stuff remains)
            var spawnSection = new Foldout { text = "Spawn Settings", value = false };
            spawnSection.AddToClassList("section");
            
            AddBoundField(spawnSection, "indicatorPrefab");
            AddBoundField(spawnSection, "defaultFormationType");
            AddBoundField(spawnSection, "defaultFormationSize");
            AddBoundField(spawnSection, "defaultFormationSpacing");
            AddBoundField(spawnSection, "defaultFormationThickness");
            
            container.Add(spawnSection);
            
            // Navigation Settings
            var navSection = new Foldout { text = "Navigation Settings", value = true };
            navSection.AddToClassList("section");
            
            AddBoundField(navSection, "agentBaseSpeed");
            AddBoundField(navSection, "speedMultiplier");
            AddBoundField(navSection, "destinationSpread");
            AddBoundField(navSection, "throttleDestinationUpdates");
            AddBoundField(navSection, "maxDestinationUpdatesPerFrame");
            AddBoundField(navSection, "priorityDestinationBonus");
            AddBoundField(navSection, "offscreenNavSyncInterval");
            AddBoundField(navSection, "navmeshRotationLerp");
            
            // Stuck Recovery subsection
            var stuckRecovery = new Foldout { text = "Stuck Recovery", value = false };
            AddBoundField(stuckRecovery, "enableStuckRecovery");
            AddBoundField(stuckRecovery, "teleportStuckAgents");
            AddBoundField(stuckRecovery, "stuckTimeout");
            AddBoundField(stuckRecovery, "stuckProximitySlack");
            AddBoundField(stuckRecovery, "stuckVelocityThreshold");
            AddBoundField(stuckRecovery, "stuckRetryInterval");
            AddBoundField(stuckRecovery, "stuckRetryAttempts");
            AddBoundField(stuckRecovery, "teleportAfterRetries");
            navSection.Add(stuckRecovery);
            
            // Arrival Sync subsection
            var arrivalSync = new Foldout { text = "Arrival Sync", value = false };
            AddBoundField(arrivalSync, "enableArrivalSync");
            AddBoundField(arrivalSync, "arrivalSyncDistance");
            AddBoundField(arrivalSync, "reapplyFormationOnArrival");
            navSection.Add(arrivalSync);
            
            // Formation Reformation subsection
            var formationReformation = new Foldout { text = "Formation Reformation", value = true };
            formationReformation.AddToClassList("highlight-section");
            
            var reformationInfo = new HelpBox(
                "Formation Reformation: Agents maintain formation while moving toward distant targets, " +
                "then dissolve formation when close for melee combat.",
                HelpBoxMessageType.Info
            );
            formationReformation.Add(reformationInfo);
            
            AddBoundField(formationReformation, "enableFormationReformation");
            AddBoundField(formationReformation, "reformationDistance");
            AddBoundField(formationReformation, "formationDissolveDistance");
            AddBoundField(formationReformation, "reformationSize");
            AddBoundField(formationReformation, "reformationSpacing");
            AddBoundField(formationReformation, "reformationThickness");
            navSection.Add(formationReformation);
            
            container.Add(navSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // AUDIO SETTINGS
            // ═══════════════════════════════════════════════════════════════════
            var audioSection = new Foldout { text = "Audio", value = false };
            audioSection.AddToClassList("section");
            
            // Faction Audio Settings
            var factionAudioLabel = new Label("Faction Voice Settings");
            factionAudioLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            factionAudioLabel.style.marginTop = 5;
            audioSection.Add(factionAudioLabel);
            
            var factionAudioHelp = new Label("Controls audio for clips in this section only (attack grunts, death screams, RTS voices, ambient chatter).\nWeapon sounds (projectile, melee, hitscan) in the Combat tab have their own settings.");
            factionAudioHelp.style.whiteSpace = WhiteSpace.Normal;
            factionAudioHelp.style.marginBottom = 5;
            factionAudioHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            factionAudioHelp.style.fontSize = 10;
            audioSection.Add(factionAudioHelp);
            
            AddBoundField(audioSection, "factionAudioVolume");
            AddBoundField(audioSection, "factionAudioPitchVariation");
            AddBoundField(audioSection, "factionAudioSpatialBlend");
            AddBoundField(audioSection, "factionAudioMinDistance");
            AddBoundField(audioSection, "factionAudioMaxDistance");
            
            // Audio Pooling / Concurrency
            var poolingLabel = new Label("Audio Pooling");
            poolingLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            poolingLabel.style.marginTop = 10;
            audioSection.Add(poolingLabel);
            
            var poolingHelp = new Label("Limits concurrent sounds per clip to prevent audio overload when thousands of agents attack.");
            poolingHelp.style.whiteSpace = WhiteSpace.Normal;
            poolingHelp.style.marginBottom = 5;
            poolingHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            poolingHelp.style.fontSize = 10;
            audioSection.Add(poolingHelp);
            
            AddBoundField(audioSection, "maxConcurrentSoundsPerClip");
            
            // Combat Audio
            var combatAudioLabel = new Label("Combat Audio");
            combatAudioLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            combatAudioLabel.style.marginTop = 10;
            audioSection.Add(combatAudioLabel);
            
            AddBoundField(audioSection, "attackSounds");
            
            // RTS Selection Audio
            var selectionAudioLabel = new Label("RTS Selection Audio");
            selectionAudioLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            selectionAudioLabel.style.marginTop = 10;
            audioSection.Add(selectionAudioLabel);
            
            var selectionHelp = new Label("Single: Individual voices (<threshold agents). Group: Army sounds (≥threshold agents).");
            selectionHelp.style.whiteSpace = WhiteSpace.Normal;
            selectionHelp.style.marginBottom = 5;
            selectionHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            selectionHelp.style.fontSize = 10;
            audioSection.Add(selectionHelp);
            
            AddBoundField(audioSection, "selectionSoundsSingle");
            AddBoundField(audioSection, "selectionSoundsGroup");
            AddBoundField(audioSection, "groupSelectionThreshold");
            
            // RTS Command Audio
            var commandAudioLabel = new Label("RTS Command Audio");
            commandAudioLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            commandAudioLabel.style.marginTop = 10;
            audioSection.Add(commandAudioLabel);
            
            var commandHelp = new Label("Single: Individual acknowledgements. Group: Army march/battle sounds.");
            commandHelp.style.whiteSpace = WhiteSpace.Normal;
            commandHelp.style.marginBottom = 5;
            commandHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            commandHelp.style.fontSize = 10;
            audioSection.Add(commandHelp);
            
            AddBoundField(audioSection, "commandSoundsSingle");
            AddBoundField(audioSection, "commandSoundsGroup");
            AddBoundField(audioSection, "groupCommandThreshold");
            
            // Ambient Audio
            var ambientAudioLabel = new Label("Ambient Audio");
            ambientAudioLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            ambientAudioLabel.style.marginTop = 10;
            audioSection.Add(ambientAudioLabel);
            
            var ambientHelp = new Label("Periodic sounds while agents are idle/alive (chants, grunts, groans).");
            ambientHelp.style.whiteSpace = WhiteSpace.Normal;
            ambientHelp.style.marginBottom = 5;
            ambientHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            ambientHelp.style.fontSize = 10;
            audioSection.Add(ambientHelp);
            
            AddBoundField(audioSection, "ambientSounds");
            AddBoundField(audioSection, "ambientSoundChance");
            AddBoundField(audioSection, "ambientSoundCooldown");
            
            // Footstep Audio Section
            var footstepHelp = new Label("Plays faction-wide footstep sounds based on moving agent count. Uses single sounds for small groups, group/marching sounds for larger groups. Supports Interval or Animation Event playback.");
            footstepHelp.style.whiteSpace = WhiteSpace.Normal;
            footstepHelp.style.marginBottom = 5;
            footstepHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            footstepHelp.style.fontSize = 10;
            audioSection.Add(footstepHelp);
            
            // Enable Footstep toggle
            var useGroupFootstepProp = serializedObject.FindProperty("useGroupFootstepAudio");
            var useGroupFootstepField = new PropertyField(useGroupFootstepProp, "Enable Footstep Audio");
            useGroupFootstepField.Bind(serializedObject);
            audioSection.Add(useGroupFootstepField);
            
            // Group footstep config container (shown when enabled)
            var groupFootstepContainer = new VisualElement();
            groupFootstepContainer.style.marginLeft = 15;
            
            // ═══════════════════════════════════════════════════════════════════
            // Basic Settings Foldout
            // ═══════════════════════════════════════════════════════════════════
            var basicSettingsFoldout = new Foldout { text = "Basic Settings", value = true };
            
            // Footstep Clips Section
            var clipsLabel = new Label("Footstep Clips");
            clipsLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            clipsLabel.style.marginTop = 5;
            basicSettingsFoldout.Add(clipsLabel);
            
            // Single footstep sounds (for small groups)
            var footstepSoundsSingleProp = serializedObject.FindProperty("footstepSoundsSingle");
            var footstepSoundsSingleField = new PropertyField(footstepSoundsSingleProp, "Single (< threshold)");
            footstepSoundsSingleField.Bind(serializedObject);
            basicSettingsFoldout.Add(footstepSoundsSingleField);
            
            // Group footstep sounds (for large groups)
            var footstepSoundsGroupProp = serializedObject.FindProperty("footstepSoundsGroup");
            var footstepSoundsGroupField = new PropertyField(footstepSoundsGroupProp, "Group (≥ threshold)");
            footstepSoundsGroupField.Bind(serializedObject);
            basicSettingsFoldout.Add(footstepSoundsGroupField);
            
            // Threshold
            var thresholdProp = serializedObject.FindProperty("groupFootstepThreshold");
            var thresholdField = new PropertyField(thresholdProp, "Group Threshold");
            thresholdField.tooltip = "Number of moving agents to switch from single to group sounds";
            thresholdField.Bind(serializedObject);
            basicSettingsFoldout.Add(thresholdField);
            
            groupFootstepContainer.Add(basicSettingsFoldout);
            
            // ═══════════════════════════════════════════════════════════════════
            // Volume & Pitch Foldout
            // ═══════════════════════════════════════════════════════════════════
            var volumePitchFoldout = new Foldout { text = "Volume & Pitch", value = false };
            
            var groupVolumeProp = serializedObject.FindProperty("groupFootstepVolume");
            var groupVolumeField = new PropertyField(groupVolumeProp, "Volume");
            groupVolumeField.Bind(serializedObject);
            volumePitchFoldout.Add(groupVolumeField);
            
            var groupPitchProp = serializedObject.FindProperty("groupFootstepPitchVariation");
            var groupPitchField = new PropertyField(groupPitchProp, "Pitch Variation");
            groupPitchField.Bind(serializedObject);
            volumePitchFoldout.Add(groupPitchField);
            
            groupFootstepContainer.Add(volumePitchFoldout);
            
            // ═══════════════════════════════════════════════════════════════════
            // 3D Audio Foldout
            // ═══════════════════════════════════════════════════════════════════
            var audio3DFoldout = new Foldout { text = "3D Audio", value = false };
            
            var spatialBlendProp = serializedObject.FindProperty("groupFootstepSpatialBlend");
            var spatialBlendField = new PropertyField(spatialBlendProp, "Spatial Blend");
            spatialBlendField.tooltip = "0 = 2D (everywhere), 1 = 3D (positional)";
            spatialBlendField.Bind(serializedObject);
            audio3DFoldout.Add(spatialBlendField);
            
            var minDistProp = serializedObject.FindProperty("groupFootstepMinDistance");
            var minDistField = new PropertyField(minDistProp, "Min Distance");
            minDistField.Bind(serializedObject);
            audio3DFoldout.Add(minDistField);
            
            var maxDistProp = serializedObject.FindProperty("groupFootstepMaxDistance");
            var maxDistField = new PropertyField(maxDistProp, "Max Distance");
            maxDistField.Bind(serializedObject);
            audio3DFoldout.Add(maxDistField);
            
            groupFootstepContainer.Add(audio3DFoldout);
            
            // ═══════════════════════════════════════════════════════════════════
            // Playback Settings Foldout
            // ═══════════════════════════════════════════════════════════════════
            var playbackFoldout = new Foldout { text = "Playback Settings", value = true };
            
            // Playback Mode
            var playbackModeLabel = new Label("Playback Mode");
            playbackModeLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            playbackModeLabel.style.marginTop = 5;
            playbackFoldout.Add(playbackModeLabel);
            
            var playbackModeProp = serializedObject.FindProperty("groupFootstepPlaybackMode");
            var playbackModeField = new PropertyField(playbackModeProp, "Playback Mode");
            playbackModeField.Bind(serializedObject);
            playbackFoldout.Add(playbackModeField);
            
            // Interval Mode Settings container
            var intervalSettingsLabel = new Label("Interval Mode Settings");
            intervalSettingsLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            intervalSettingsLabel.style.marginTop = 10;
            playbackFoldout.Add(intervalSettingsLabel);
            
            var intervalContainer = new VisualElement();
            intervalContainer.style.marginLeft = 10;
            
            var minIntervalProp = serializedObject.FindProperty("groupFootstepMinInterval");
            var minIntervalField = new PropertyField(minIntervalProp, "Min Interval");
            minIntervalField.Bind(serializedObject);
            intervalContainer.Add(minIntervalField);
            
            var maxIntervalProp = serializedObject.FindProperty("groupFootstepMaxInterval");
            var maxIntervalField = new PropertyField(maxIntervalProp, "Max Interval");
            maxIntervalField.Bind(serializedObject);
            intervalContainer.Add(maxIntervalField);
            
            var minSpeedProp = serializedObject.FindProperty("groupFootstepMinSpeed");
            var minSpeedField = new PropertyField(minSpeedProp, "Min Speed Threshold");
            minSpeedField.tooltip = "Minimum agent speed to count as 'moving'";
            minSpeedField.Bind(serializedObject);
            intervalContainer.Add(minSpeedField);
            
            var scaleIntervalProp = serializedObject.FindProperty("scaleIntervalWithAgentCount");
            var scaleIntervalField = new PropertyField(scaleIntervalProp, "Scale Interval With Agent Count");
            scaleIntervalField.tooltip = "If enabled, more agents = faster/busier footstep interval";
            scaleIntervalField.Bind(serializedObject);
            intervalContainer.Add(scaleIntervalField);
            
            playbackFoldout.Add(intervalContainer);
            
            // Show/hide interval settings based on playback mode
            intervalContainer.style.display = playbackModeProp.enumValueIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            intervalSettingsLabel.style.display = playbackModeProp.enumValueIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            
            playbackModeField.RegisterValueChangeCallback(evt =>
            {
                serializedObject.Update();
                bool isInterval = playbackModeProp.enumValueIndex == 0;
                intervalContainer.style.display = isInterval ? DisplayStyle.Flex : DisplayStyle.None;
                intervalSettingsLabel.style.display = isInterval ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            // Concurrency
            var concurrencyLabel = new Label("Concurrency");
            concurrencyLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            concurrencyLabel.style.marginTop = 10;
            playbackFoldout.Add(concurrencyLabel);
            
            var maxConcurrentProp = serializedObject.FindProperty("groupFootstepMaxConcurrent");
            var maxConcurrentField = new PropertyField(maxConcurrentProp, "Max Concurrent Sounds");
            maxConcurrentField.Bind(serializedObject);
            playbackFoldout.Add(maxConcurrentField);
            
            groupFootstepContainer.Add(playbackFoldout);
            
            // ═══════════════════════════════════════════════════════════════════
            // Surface Detection Foldout
            // ═══════════════════════════════════════════════════════════════════
            var surfaceFoldout = new Foldout { text = "Surface Detection", value = false };
            
            // Enable surface detection toggle
            var enableGroupSurfaceProp = serializedObject.FindProperty("enableGroupSurfaceDetection");
            var enableGroupSurfaceField = new PropertyField(enableGroupSurfaceProp, "Enable Surface Detection");
            enableGroupSurfaceField.Bind(serializedObject);
            surfaceFoldout.Add(enableGroupSurfaceField);
            
            // Surface detection container (shown when enabled)
            var groupSurfaceContainer = new VisualElement();
            groupSurfaceContainer.style.marginLeft = 15;
            
            // Surface mapping reference
            var groupSurfaceMappingProp = serializedObject.FindProperty("groupSurfaceMapping");
            var groupSurfaceMappingField = new PropertyField(groupSurfaceMappingProp, "Surface Mapping");
            groupSurfaceMappingField.Bind(serializedObject);
            groupSurfaceContainer.Add(groupSurfaceMappingField);
            
            // Stone clips
            var stoneFoldout = new Foldout { text = "Stone/Concrete Clips", value = false };
            AddSurfaceClipFields(stoneFoldout, "groupFootstepsStoneSingle", "groupFootstepsStoneGroup");
            groupSurfaceContainer.Add(stoneFoldout);
            
            // Water clips
            var waterFoldout = new Foldout { text = "Water/Puddle Clips", value = false };
            AddSurfaceClipFields(waterFoldout, "groupFootstepsWaterSingle", "groupFootstepsWaterGroup");
            groupSurfaceContainer.Add(waterFoldout);
            
            // Metal clips
            var metalFoldout = new Foldout { text = "Metal Clips", value = false };
            AddSurfaceClipFields(metalFoldout, "groupFootstepsMetalSingle", "groupFootstepsMetalGroup");
            groupSurfaceContainer.Add(metalFoldout);
            
            // Wood clips
            var woodFoldout = new Foldout { text = "Wood Clips", value = false };
            AddSurfaceClipFields(woodFoldout, "groupFootstepsWoodSingle", "groupFootstepsWoodGroup");
            groupSurfaceContainer.Add(woodFoldout);
            
            // Sand clips
            var sandFoldout = new Foldout { text = "Sand Clips", value = false };
            AddSurfaceClipFields(sandFoldout, "groupFootstepsSandSingle", "groupFootstepsSandGroup");
            groupSurfaceContainer.Add(sandFoldout);
            
            // Snow clips
            var snowFoldout = new Foldout { text = "Snow Clips", value = false };
            AddSurfaceClipFields(snowFoldout, "groupFootstepsSnowSingle", "groupFootstepsSnowGroup");
            groupSurfaceContainer.Add(snowFoldout);
            
            surfaceFoldout.Add(groupSurfaceContainer);
            
            // Initial visibility based on surface detection enabled
            groupSurfaceContainer.style.display = enableGroupSurfaceProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Register callback to show/hide surface settings
            enableGroupSurfaceField.RegisterValueChangeCallback(evt =>
            {
                serializedObject.Update();
                groupSurfaceContainer.style.display = enableGroupSurfaceProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            groupFootstepContainer.Add(surfaceFoldout);
            
            // Animation Event help box (shown when AnimationEvent mode is selected)
            var animEventHelp = new HelpBox(
                "For Animation Event mode: Use GPUI Crowd Event Definition to trigger footsteps at specific animation times.\n\n" +
                "Quick Setup:\n" +
                "1. Add animation events at foot-plant times in your walk/run clips\n" +
                "2. Call FootstepAudioManager.PlayGroupFootstepFromEvent() from the event",
                HelpBoxMessageType.Info
            );
            animEventHelp.style.marginTop = 10;
            animEventHelp.style.display = playbackModeProp.enumValueIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            groupFootstepContainer.Add(animEventHelp);
            
            // Update help box visibility when playback mode changes
            playbackModeField.RegisterValueChangeCallback(evt =>
            {
                serializedObject.Update();
                animEventHelp.style.display = playbackModeProp.enumValueIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            audioSection.Add(groupFootstepContainer);
            
            // Initial visibility based on enabled state
            groupFootstepContainer.style.display = useGroupFootstepProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Register callback to show/hide when enabled changes
            useGroupFootstepField.RegisterValueChangeCallback(evt =>
            {
                serializedObject.Update();
                groupFootstepContainer.style.display = useGroupFootstepProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            container.Add(audioSection);
            
            // Performance Settings are now global in the Controller
            
            return container;
        }
        
        private void AddBoundField(VisualElement parent, string propertyName)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
            {
                var field = new PropertyField(prop);
                field.bindingPath = propertyName;
                field.userData = $"{propertyName} {prop.displayName}";
                field.Bind(serializedObject);
                parent.Add(field);
            }
        }
        
        /// <summary>
        /// Adds a custom navigation type dropdown that handles A* availability gracefully.
        /// When A* is not available, only shows Unity NavMesh and resets any A* values.
        /// </summary>
        private void AddNavigationTypeField(VisualElement parent)
        {
            var prop = serializedObject.FindProperty("navigationType");
            if (prop == null) return;
            
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginTop = 2;
            container.style.marginBottom = 2;
            container.userData = "navigationType Navigation Type";
            
            var label = new Label("Navigation Type");
            label.style.width = 150;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            container.Add(label);
            
            // Build list of available options
            var options = new System.Collections.Generic.List<string>
            {
                "Unity NavMesh",
                "Flow Field",
                "Flight 3D (Volumetric)"
            };
            var values = new System.Collections.Generic.List<int>
            {
                0,  // NavigationType.UnityNavMesh = 0
                20, // NavigationType.FlowField = 20
                30  // NavigationType.Flight3D = 30
            };
            
#if ASTAR_EXISTS
            options.Add("A* AIPath (Grid, Point)");
            options.Add("A* RichAI (Navmesh)");
            options.Add("A* AILerp (Exact)");
            options.Add("A* RVO (Local Avoidance)");
            values.Add(10); // NavigationType.AStarAIPath = 10
            values.Add(11); // NavigationType.AStarRichAI = 11
            values.Add(12); // NavigationType.AStarAILerp = 12
            values.Add(13); // NavigationType.AStarRVO = 13
#endif
            
            int currentValue = prop.intValue;
            int currentIndex = values.IndexOf(currentValue);
            
            // If current value is not available (e.g., A* type but A* not installed), auto-fix it
            bool needsReset = currentIndex < 0;
            if (needsReset)
            {
                currentIndex = 0; // Default to Unity NavMesh
                
                // Actually persist the fix to the asset
                prop.intValue = 0;
                serializedObject.ApplyModifiedProperties();
                
                // Show info that we auto-fixed it
                var infoBox = new HelpBox(
                    $"Navigation type was automatically reset from unknown (value: {currentValue}) to Unity NavMesh " +
                    "because the original navigation system is not available. The asset has been updated.",
                    HelpBoxMessageType.Info);
                parent.Add(infoBox);
            }
            
            var dropdown = new PopupField<string>(options, currentIndex);
            dropdown.style.flexGrow = 1;
            
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int selectedIndex = options.IndexOf(evt.newValue);
                if (selectedIndex >= 0 && selectedIndex < values.Count)
                {
                    prop.intValue = values[selectedIndex];
                    serializedObject.ApplyModifiedProperties();
                }
            });
            
            container.Add(dropdown);
            parent.Add(container);
        }
        
        /// <summary>
        /// Adds a bound field with validation styling (colored background based on status).
        /// </summary>
        private void AddValidatedField(VisualElement parent, string propertyName, string label, 
            bool isRequired, System.Func<SerializedProperty, bool> validator, string incompleteMessage = null)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop == null) return;
            
            bool isValid = validator?.Invoke(prop) ?? true;
            var status = isRequired 
                ? (isValid ? ValidationStatus.RequiredComplete : ValidationStatus.RequiredIncomplete)
                : (isValid ? ValidationStatus.OptionalComplete : ValidationStatus.OptionalIncomplete);
            
            var container = new VisualElement();
            container.userData = $"{propertyName} {label ?? prop.displayName}";
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginTop = 2;
            container.style.marginBottom = 2;
            container.style.paddingLeft = 4;
            container.style.paddingRight = 4;
            container.style.paddingTop = 2;
            container.style.paddingBottom = 2;
            EditorFieldValidation.ApplyValidationStyle(container, status);
            
            // Status icon
            var icon = new Label(EditorFieldValidation.GetStatusIcon(status));
            icon.style.width = 20;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.marginRight = 4;
            container.Add(icon);
            
            // Property field
            var field = new PropertyField(prop, label ?? prop.displayName);
            field.bindingPath = propertyName;
            field.userData = $"{propertyName} {label ?? prop.displayName}";
            field.style.flexGrow = 1;
            field.Bind(serializedObject);
            
            // Update validation when value changes
            field.RegisterValueChangeCallback(evt =>
            {
                serializedObject.Update();
                bool newIsValid = validator?.Invoke(prop) ?? true;
                var newStatus = isRequired 
                    ? (newIsValid ? ValidationStatus.RequiredComplete : ValidationStatus.RequiredIncomplete)
                    : (newIsValid ? ValidationStatus.OptionalComplete : ValidationStatus.OptionalIncomplete);
                
                EditorFieldValidation.ApplyValidationStyle(container, newStatus);
                icon.text = EditorFieldValidation.GetStatusIcon(newStatus);
                RefreshValidationSummary();
            });
            
            container.Add(field);
            
            // Tooltip with incomplete message
            if (!isValid && !string.IsNullOrEmpty(incompleteMessage))
            {
                container.tooltip = incompleteMessage;
            }
            
            parent.Add(container);
        }
        
        private void AddSurfaceClipFields(VisualElement parent, string singlePropertyName, string groupPropertyName)
        {
            var singleProp = serializedObject.FindProperty(singlePropertyName);
            if (singleProp != null)
            {
                var singleField = new PropertyField(singleProp, "Single (< threshold)");
                singleField.bindingPath = singlePropertyName;
                singleField.userData = $"{singlePropertyName} {singleProp.displayName} single";
                singleField.Bind(serializedObject);
                parent.Add(singleField);
            }
            
            var groupProp = serializedObject.FindProperty(groupPropertyName);
            if (groupProp != null)
            {
                var groupField = new PropertyField(groupProp, "Group (≥ threshold)");
                groupField.bindingPath = groupPropertyName;
                groupField.userData = $"{groupPropertyName} {groupProp.displayName} group";
                groupField.Bind(serializedObject);
                parent.Add(groupField);
            }
        }
        
        #endregion
        
        #region Combat Tab
        
        private VisualElement CreateCombatTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            var crowd = target as Runtime.EnemyCrowd;

            // Global hit detection reminder
            var hitDetectionNotice = new HelpBox(
                "Hit detection mode (Distance vs Collider) is configured globally in the Enemy Masses Crowd Controller inspector.",
                HelpBoxMessageType.Info
            );
            container.Add(hitDetectionNotice);
            
            // ═══════════════════════════════════════════════════════════════════
            // ENABLE COMBAT TOGGLE (Top priority)
            // ═══════════════════════════════════════════════════════════════════
            var enableCombatProp = serializedObject.FindProperty("enableCombatSystem");
            var enableCombatField = new PropertyField(enableCombatProp, "Enable Combat System");
            enableCombatField.style.marginBottom = 10;
            enableCombatField.style.marginTop = 5;
            container.Add(enableCombatField);
            
            // Check if combat system is enabled - hide all combat settings if disabled
            bool combatEnabled = enableCombatProp != null && enableCombatProp.boolValue;
            
            // Create container for all combat-dependent settings
            var combatSettingsContainer = new VisualElement();
            combatSettingsContainer.style.display = combatEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Register callback to show/hide combat settings when toggle changes
            enableCombatField.RegisterValueChangeCallback(evt => {
                bool newValue = evt.changedProperty.boolValue;
                combatSettingsContainer.style.display = newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            // ═══════════════════════════════════════════════════════════════════
            // COMBAT BEHAVIOR TEMPLATE (Primary Selection - Dropdown Style)
            // ═══════════════════════════════════════════════════════════════════
            var templateSection = new VisualElement();
            templateSection.style.marginBottom = 15;
            templateSection.style.paddingTop = 10;
            templateSection.style.paddingBottom = 10;
            templateSection.style.paddingLeft = 5;
            templateSection.style.paddingRight = 5;
            templateSection.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            templateSection.style.borderTopLeftRadius = 5;
            templateSection.style.borderTopRightRadius = 5;
            templateSection.style.borderBottomLeftRadius = 5;
            templateSection.style.borderBottomRightRadius = 5;
            
            var templateLabel = new Label("Combat Behavior Template");
            templateLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            templateLabel.style.fontSize = 13;
            templateLabel.style.marginBottom = 5;
            templateSection.Add(templateLabel);
            
            // Build dropdown choices with descriptions
            var templateChoices = new System.Collections.Generic.List<string>
            {
                "Melee Swarm - All rush in (Vampire Survivors)",
                "Melee Queued - Circle and queue attacks (Batman)",
                "Melee Battle Line - Military ranks (Total War)",
                "Ranged Stationary - Maintain distance, fire at will",
                "Ranged Battle Line - Formation volleys",
                "Hybrid Adaptive - Switch melee/ranged by distance",
                "Melee Waves - Wave-based attacks (Diablo 4)",
                "Custom - Manual configuration"
            };
            
            var templateProp = serializedObject.FindProperty("combatBehaviorTemplate");
            int currentIndex = templateProp.enumValueIndex;
            
            var templateDropdown = new PopupField<string>("", templateChoices, currentIndex);
            templateDropdown.style.marginTop = 5;
            templateDropdown.RegisterValueChangedCallback(evt => {
                int newIndex = templateChoices.IndexOf(evt.newValue);
                templateProp.enumValueIndex = newIndex;
                serializedObject.ApplyModifiedProperties();
                
                // Auto-apply template when selection changes (except for Custom)
                if (crowd != null && newIndex != (int)CombatBehaviorTemplate.Custom)
                {
                    Undo.RecordObject(crowd, "Apply Combat Template");
                    crowd.ApplyCombatTemplate();
                    EditorUtility.SetDirty(crowd);
                    serializedObject.Update();
                }
                
                // Refresh the entire combat tab to update visibility
                ShowTab("combat");
            });
            templateSection.Add(templateDropdown);
            
            combatSettingsContainer.Add(templateSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // HEALTH SETTINGS (Required - Prominently displayed)
            // ═══════════════════════════════════════════════════════════════════
            var healthSection = new VisualElement();
            healthSection.style.marginBottom = 15;
            healthSection.style.paddingTop = 10;
            healthSection.style.paddingBottom = 10;
            healthSection.style.paddingLeft = 10;
            healthSection.style.paddingRight = 10;
            healthSection.style.backgroundColor = new Color(0.15f, 0.25f, 0.15f, 0.4f);
            healthSection.style.borderTopLeftRadius = 5;
            healthSection.style.borderTopRightRadius = 5;
            healthSection.style.borderBottomLeftRadius = 5;
            healthSection.style.borderBottomRightRadius = 5;
            healthSection.style.borderLeftWidth = 3;
            healthSection.style.borderLeftColor = new Color(0.3f, 0.7f, 0.3f);
            
            var healthHeader = new Label("Health Settings (Required)");
            healthHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            healthHeader.style.fontSize = 13;
            healthHeader.style.marginBottom = 8;
            healthSection.Add(healthHeader);
            
            // Required: Base Health (validated)
            AddValidatedField(healthSection, "agentBaseHealth", "Base Health", 
                isRequired: true, 
                validator: EditorFieldValidation.ValidatePositiveNumber,
                incompleteMessage: "Set base health > 0 for agents to survive");
            
            AddBoundField(healthSection, "healthMultiplier");
            AddBoundField(healthSection, "agentHealthVariance");
            
            combatSettingsContainer.Add(healthSection);

            // ═══════════════════════════════════════════════════════════════════
            // ATTACK DAMAGE (Core - Prominently displayed)
            // ═══════════════════════════════════════════════════════════════════
            var attackDamageSection = new VisualElement();
            attackDamageSection.style.marginBottom = 15;
            attackDamageSection.style.paddingTop = 10;
            attackDamageSection.style.paddingBottom = 10;
            attackDamageSection.style.paddingLeft = 10;
            attackDamageSection.style.paddingRight = 10;
            attackDamageSection.style.backgroundColor = new Color(0.12f, 0.2f, 0.3f, 0.28f);
            attackDamageSection.style.borderTopLeftRadius = 5;
            attackDamageSection.style.borderTopRightRadius = 5;
            attackDamageSection.style.borderBottomLeftRadius = 5;
            attackDamageSection.style.borderBottomRightRadius = 5;
            attackDamageSection.style.borderLeftWidth = 3;
            attackDamageSection.style.borderLeftColor = new Color(0.35f, 0.7f, 1f);

            var attackDamageHeader = new Label("Attack Damage (Core)");
            attackDamageHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            attackDamageHeader.style.fontSize = 13;
            attackDamageHeader.style.marginBottom = 4;
            attackDamageSection.Add(attackDamageHeader);

            var attackDamageHelp = new Label(
                "Melee Attack Damage is only used when melee auto-attack is enabled and the agent is in melee mode. " +
                "Ranged Attack Damage is only used when ranged auto-attacks are enabled " +
                "(Attack Mode is not Disabled (Use Skills)) and the agent is in ranged mode. " +
                "If that path is disabled, the corresponding value is ignored.");
            attackDamageHelp.style.whiteSpace = WhiteSpace.Normal;
            attackDamageHelp.style.marginBottom = 6;
            attackDamageHelp.style.color = new Color(0.78f, 0.84f, 0.9f);
            attackDamageSection.Add(attackDamageHelp);

            AddBoundField(attackDamageSection, "meleeAttackDamage");
            AddBoundField(attackDamageSection, "rangedAttackDamage");

            combatSettingsContainer.Add(attackDamageSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // CONFLICT WARNINGS (Show for Custom mode if there are conflicts)
            // ═══════════════════════════════════════════════════════════════════
            var currentTemplateForWarnings = (CombatBehaviorTemplate)templateProp.enumValueIndex;
            if (currentTemplateForWarnings == CombatBehaviorTemplate.Custom && crowd != null)
            {
                var conflicts = CombatBehaviorTemplateHelper.GetSettingConflicts(crowd);
                if (conflicts.Count > 0)
                {
                    var warningBox = new VisualElement();
                    warningBox.style.backgroundColor = new Color(0.6f, 0.3f, 0f, 0.3f);
                    warningBox.style.borderTopLeftRadius = 5;
                    warningBox.style.borderTopRightRadius = 5;
                    warningBox.style.borderBottomLeftRadius = 5;
                    warningBox.style.borderBottomRightRadius = 5;
                    warningBox.style.paddingTop = 8;
                    warningBox.style.paddingBottom = 8;
                    warningBox.style.paddingLeft = 10;
                    warningBox.style.paddingRight = 10;
                    warningBox.style.marginBottom = 10;
                    
                    var warningTitle = new Label("⚠ Setting Conflicts Detected");
                    warningTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                    warningTitle.style.color = new Color(1f, 0.8f, 0.4f);
                    warningTitle.style.marginBottom = 5;
                    warningBox.Add(warningTitle);
                    
                    foreach (var conflict in conflicts)
                    {
                        var conflictLabel = new Label("- " + conflict);
                        conflictLabel.style.color = new Color(1f, 0.9f, 0.7f);
                        conflictLabel.style.whiteSpace = WhiteSpace.Normal;
                        conflictLabel.style.marginLeft = 10;
                        warningBox.Add(conflictLabel);
                    }
                    
                    var fixButton = new Button(() => {
                        Undo.RecordObject(crowd, "Auto-Fix Combat Conflicts");
                        CombatBehaviorTemplateHelper.ValidateAndFixCriticalConflicts(crowd);
                        EditorUtility.SetDirty(crowd);
                        serializedObject.Update();
                        ShowTab("combat");
                    });
                    fixButton.text = "Auto-Fix Conflicts";
                    fixButton.style.marginTop = 8;
                    warningBox.Add(fixButton);
                    
                    combatSettingsContainer.Add(warningBox);
                }
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // CORE COMBAT STATS (Always Visible - these are independent of template)
            // ═══════════════════════════════════════════════════════════════════
            var statsSection = new Foldout { text = "Combat Stats", value = true };
            statsSection.AddToClassList("section");
            
            // Aggro/Detection Range Settings
            var aggroHeader = new Label("Aggro/Detection Range");
            aggroHeader.AddToClassList("section-header");
            aggroHeader.style.marginTop = 5;
            aggroHeader.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            statsSection.Add(aggroHeader);
            
            var aggroHelp = new Label("Controls when agents detect enemies and start chasing.");
            aggroHelp.AddToClassList("help-text");
            aggroHelp.style.whiteSpace = WhiteSpace.Normal;
            aggroHelp.style.marginBottom = 5;
            aggroHelp.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
            statsSection.Add(aggroHelp);
            
            AddBoundField(statsSection, "aggroRange");
            AddBoundField(statsSection, "sightRange");
            AddBoundField(statsSection, "aggroLossRange");
            AddBoundField(statsSection, "aggroLossDelay");
            
            // Vertical Targeting Settings
            var verticalTargetingHeader = new Label("Vertical Targeting");
            verticalTargetingHeader.AddToClassList("section-header");
            verticalTargetingHeader.style.marginTop = 10;
            verticalTargetingHeader.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            statsSection.Add(verticalTargetingHeader);
            
            var verticalTargetingHelp = new Label("Prevents targeting through walls/floors. Height check is free; LOS has performance modes for large battles.");
            verticalTargetingHelp.AddToClassList("help-text");
            verticalTargetingHelp.style.whiteSpace = WhiteSpace.Normal;
            verticalTargetingHelp.style.marginBottom = 5;
            verticalTargetingHelp.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
            statsSection.Add(verticalTargetingHelp);
            
            AddBoundField(statsSection, "maxTargetHeightDifference");
            AddBoundField(statsSection, "requireLineOfSight");
            AddBoundField(statsSection, "losCheckMode");
            AddBoundField(statsSection, "lineOfSightBlockingLayers");
            
            // Ally Defense System Settings
            var allyDefenseHeader = new Label("Ally Defense System");
            allyDefenseHeader.AddToClassList("section-header");
            allyDefenseHeader.style.marginTop = 10;
            allyDefenseHeader.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            statsSection.Add(allyDefenseHeader);
            
            var allyDefenseHelp = new Label("When enabled, nearby allies respond when faction members are attacked. Critical for melee vs ranged survival.");
            allyDefenseHelp.AddToClassList("help-text");
            allyDefenseHelp.style.whiteSpace = WhiteSpace.Normal;
            allyDefenseHelp.style.marginBottom = 5;
            allyDefenseHelp.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
            statsSection.Add(allyDefenseHelp);
            
            AddBoundField(statsSection, "enableAllyDefense");
            AddBoundField(statsSection, "allyDefenseRadius");
            AddBoundField(statsSection, "allyDefenseTargetAttacker");
            AddBoundField(statsSection, "allyDefenseMaxResponders");
            AddBoundField(statsSection, "allyDefenseAlertCooldown");
            AddBoundField(statsSection, "allyRushSpeedMultiplier");
            
            // Combat Engagement Settings
            var engagementHeader = new Label("Combat Engagement");
            engagementHeader.AddToClassList("section-header");
            engagementHeader.style.marginTop = 10;
            engagementHeader.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            statsSection.Add(engagementHeader);
            
            var engagementHelp = new Label("Controls when actual combat begins (attacking, circling, etc.).");
            engagementHelp.AddToClassList("help-text");
            engagementHelp.style.whiteSpace = WhiteSpace.Normal;
            engagementHelp.style.marginBottom = 5;
            engagementHelp.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
            statsSection.Add(engagementHelp);
            
            AddBoundField(statsSection, "engagementRadius");
            AddBoundField(statsSection, "combatRangeType");
            
            // Ranged Attack Range Settings - Always visible for easy access
            var attackRangeHeader = new Label("Ranged Attack Range (Min/Max)");
            attackRangeHeader.style.marginTop = 5;
            attackRangeHeader.style.marginLeft = 15;
            attackRangeHeader.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;
            attackRangeHeader.style.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
            statsSection.Add(attackRangeHeader);
            
            AddBoundField(statsSection, "rangedMinDistance");
            AddBoundField(statsSection, "rangedMaxDistance");
            AddBoundField(statsSection, "rangedPreferredDistance");
            
            AddBoundField(statsSection, "damageMultiplier");
            AddBoundField(statsSection, "attackCooldown");
            AddBoundField(statsSection, "attackCooldownJitter");
            AddBoundField(statsSection, "blockChance");
            AddBoundField(statsSection, "blockHoldTimeRange");
            AddBoundField(statsSection, "enableDebugDamageLogging");
            AddBoundField(statsSection, "maxCombatUpdatesPerFrame");
            
            combatSettingsContainer.Add(statsSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // ARMOR & RESISTANCES SECTION
            // ═══════════════════════════════════════════════════════════════════
            var resistanceSection = new Foldout { text = "Armor & Resistances", value = false };
            resistanceSection.style.marginTop = 10;
            
            var resistanceHelp = new HelpBox(
                "Configure damage mitigation for this faction.\n\n" +
                "Formula: finalDamage = rawDamage × (100 / (100 + resistance))\n" +
                "• 0 resistance = 100% damage (no reduction)\n" +
                "• 100 resistance = 50% damage\n" +
                "• Negative values = vulnerability (take extra damage)\n\n" +
                "Armor reduces Physical damage.\n" +
                "Magic Resist reduces all magical types (unless elemental overrides enabled).",
                HelpBoxMessageType.Info);
            resistanceSection.Add(resistanceHelp);
            
            AddBoundField(resistanceSection, "armor");
            AddBoundField(resistanceSection, "magicResist");
            
            // Elemental Resistances subsection
            var elementalFoldout = new Foldout { text = "Elemental Resistances (Optional)", value = false };
            elementalFoldout.style.marginTop = 8;
            elementalFoldout.style.marginLeft = 10;
            
            var elementalHelp = new HelpBox(
                "Enable individual elemental resistances to override Magic Resist for specific damage types.\n" +
                "When disabled, all magical damage types use Magic Resist.",
                HelpBoxMessageType.Info);
            elementalFoldout.Add(elementalHelp);
            
            AddBoundField(elementalFoldout, "useElementalResistances");
            AddBoundField(elementalFoldout, "fireResistance");
            AddBoundField(elementalFoldout, "iceResistance");
            AddBoundField(elementalFoldout, "lightningResistance");
            AddBoundField(elementalFoldout, "poisonResistance");
            
            resistanceSection.Add(elementalFoldout);
            combatSettingsContainer.Add(resistanceSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // LEVEL & SCALING SECTION
            // ═══════════════════════════════════════════════════════════════════
            var levelSection = new Foldout { text = "Level & Skill Scaling", value = false };
            levelSection.style.marginTop = 10;
            
            var levelHelp = new HelpBox(
                "Configure level and global skill scaling for this crowd.\n\n" +
                "• Base Level: Starting level for all agents (1-99)\n" +
                "• Level Variance: Random ± variance for mixed-strength enemies\n" +
                "• Global Scaling: Used by skills with Scaling Mode set to 'Global'",
                HelpBoxMessageType.Info);
            levelSection.Add(levelHelp);
            
            AddBoundField(levelSection, "baseLevel");
            AddBoundField(levelSection, "levelVariance");
            
            // Global Scaling Config subsection
            var globalScalingFoldout = new Foldout { text = "Global Skill Scaling Config", value = false };
            globalScalingFoldout.style.marginTop = 8;
            globalScalingFoldout.style.marginLeft = 10;
            
            var globalScalingHelp = new HelpBox(
                "This scaling config is shared by all skills that have Scaling Mode = 'Global'.\n" +
                "Configure once here, apply to all faction skills automatically.",
                HelpBoxMessageType.Info);
            globalScalingFoldout.Add(globalScalingHelp);
            
            AddBoundField(globalScalingFoldout, "globalSkillScaling");
            levelSection.Add(globalScalingFoldout);
            
            combatSettingsContainer.Add(levelSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // TEMPLATE-SPECIFIC SECTIONS (Only shown for relevant templates)
            // ═══════════════════════════════════════════════════════════════════
            var currentTemplate = (CombatBehaviorTemplate)templateProp.enumValueIndex;
            bool isCustom = currentTemplate == CombatBehaviorTemplate.Custom;
            bool isBattleLine = currentTemplate == CombatBehaviorTemplate.MeleeBattleLine || 
                               currentTemplate == CombatBehaviorTemplate.RangedBattleLine;
            bool isWaves = currentTemplate == CombatBehaviorTemplate.MeleeWaves;
            bool isHybrid = currentTemplate == CombatBehaviorTemplate.HybridAdaptive;
            bool isRangedCapable = currentTemplate == CombatBehaviorTemplate.RangedStationary ||
                                   currentTemplate == CombatBehaviorTemplate.RangedBattleLine ||
                                   currentTemplate == CombatBehaviorTemplate.HybridAdaptive ||
                                   isCustom;
            
            // Battle Line Settings (only for BattleLine templates or Custom)
            if (isBattleLine || isCustom)
            {
                var battleLineSection = new Foldout { text = "Battle Line Settings", value = true };
                battleLineSection.AddToClassList("section");
                
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineSpacing")) { bindingPath = "battleLineSpacing" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineRankDepth")) { bindingPath = "battleLineRankDepth" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineAgentsPerRank")) { bindingPath = "battleLineAgentsPerRank" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineFlankingRatio")) { bindingPath = "battleLineFlankingRatio" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineFlankingWidth")) { bindingPath = "battleLineFlankingWidth" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineEngagementDistance")) { bindingPath = "battleLineEngagementDistance" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineAutoAdvance")) { bindingPath = "battleLineAutoAdvance" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineAdvanceSpeed")) { bindingPath = "battleLineAdvanceSpeed" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLinePositionJitter")) { bindingPath = "battleLinePositionJitter" });
                
                // Chaotic Combat subsection
                var chaoticLabel = new Label("Chaotic Combat");
                chaoticLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                chaoticLabel.style.marginTop = 10;
                battleLineSection.Add(chaoticLabel);
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineChaoticCombat")) { bindingPath = "battleLineChaoticCombat" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineAttackTimingVariance")) { bindingPath = "battleLineAttackTimingVariance" });
                battleLineSection.Add(new PropertyField(serializedObject.FindProperty("battleLineAttackCooldownVariance")) { bindingPath = "battleLineAttackCooldownVariance" });
                
                battleLineSection.Bind(serializedObject);
                combatSettingsContainer.Add(battleLineSection);
            }
            
            // Wave Settings (only for MeleeWaves template or Custom)
            if (isWaves || isCustom)
            {
                var waveSection = new Foldout { text = "Wave Attack Settings", value = true };
                waveSection.AddToClassList("section");
                
                waveSection.Add(new PropertyField(serializedObject.FindProperty("waveGroupCount")) { bindingPath = "waveGroupCount" });
                waveSection.Add(new PropertyField(serializedObject.FindProperty("waveAttackDuration")) { bindingPath = "waveAttackDuration" });
                waveSection.Add(new PropertyField(serializedObject.FindProperty("waveCooldown")) { bindingPath = "waveCooldown" });
                waveSection.Add(new PropertyField(serializedObject.FindProperty("waveWaitingDistance")) { bindingPath = "waveWaitingDistance" });
                waveSection.Add(new PropertyField(serializedObject.FindProperty("reactiveDefense")) { bindingPath = "reactiveDefense" });
                
                waveSection.Bind(serializedObject);
                combatSettingsContainer.Add(waveSection);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // RANGED ATTACK SETTINGS (For ranged-capable templates)
            // Shown for: RangedStationary, RangedBattleLine, HybridAdaptive, Custom
            // ═══════════════════════════════════════════════════════════════════
            if (isRangedCapable)
            {
                var rangedAttackSection = new Foldout { text = "Ranged Attack Mode", value = true };
                rangedAttackSection.AddToClassList("section");
                
                // Different help text based on template
                string helpText = currentTemplate switch
                {
                    CombatBehaviorTemplate.RangedStationary => 
                        "Ranged Stationary units maintain distance and fire at will.\n\n" +
                        "- Hitscan: Instant damage (guns, lasers) - Most performant\n" +
                        "- Projectile: Visible arrows/bolts travel to target\n" +
                        "- ProjectileArc: Grenades/thrown objects with parabolic arc",
                    CombatBehaviorTemplate.RangedBattleLine => 
                        "Ranged Battle Line units fire volleys in formation.\n\n" +
                        "- Hitscan: Instant damage (musket volleys, laser volleys)\n" +
                        "- Projectile: Arrow/bolt volleys travel to target\n" +
                        "- ProjectileArc: Artillery/grenade volleys with arc",
                    CombatBehaviorTemplate.HybridAdaptive =>
                        "Hybrid units switch between melee and ranged based on distance.\n" +
                        "These settings apply when in ranged mode.\n\n" +
                        "- Hitscan: Instant damage when shooting\n" +
                        "- Projectile: Arrows/bolts when in ranged mode\n" +
                        "- ProjectileArc: Thrown grenades before closing to melee\n" +
                        "- Disabled (Use Skills): No legacy ranged attack, use Agent Skills instead",
                    _ => 
                        "Choose how ranged attacks deal damage:\n\n" +
                        "- Hitscan: Instant raycast damage (guns, lasers) - Most performant\n" +
                        "- Projectile: Visual projectile travels to target (arrows, bolts)\n" +
                        "- ProjectileArc: Parabolic arc with AOE (grenades, catapults)\n" +
                        "- Disabled (Use Skills): Disable legacy ranged auto-attacks and use Agent Skills"
                };
                
                var rangedHelpBox = new HelpBox(helpText, HelpBoxMessageType.Info);
                rangedAttackSection.Add(rangedHelpBox);
                
                AddBoundField(rangedAttackSection, "rangedAutoAttackCanHitFlying");
                
                // Attack Mode dropdown - using EnumField for better dropdown behavior
                var attackModeProp = serializedObject.FindProperty("rangedAttackMode");
                var currentEnumValue = (RangedAttackMode)attackModeProp.enumValueIndex;
                var attackModeField = new EnumField("Attack Mode", currentEnumValue);
                attackModeField.style.marginTop = 4;
                attackModeField.style.marginBottom = 4;
                
                // Track the current value to detect actual changes
                if (_lastAttackModeValue == -1)
                    _lastAttackModeValue = attackModeProp.enumValueIndex;
                
                attackModeField.RegisterValueChangedCallback(evt => {
                    // Apply the change to the serialized property
                    var newEnumValue = (RangedAttackMode)evt.newValue;
                    int newIndex = (int)newEnumValue;
                    
                    if (attackModeProp.enumValueIndex != newIndex)
                    {
                        attackModeProp.enumValueIndex = newIndex;
                        serializedObject.ApplyModifiedProperties();
                    }
                    
                    // Only refresh if the value actually changed and we're not already refreshing
                    if (!_isRefreshing && newIndex != _lastAttackModeValue)
                    {
                        _lastAttackModeValue = newIndex;
                        // Schedule refresh for next frame to avoid callback recursion
                        EditorApplication.delayCall += () => {
                            if (this != null && !_isRefreshing)
                            {
                                ShowTab("combat");
                            }
                        };
                    }
                });
                rangedAttackSection.Add(attackModeField);
                
                // Show configuration based on attack mode
                var currentMode = (RangedAttackMode)attackModeProp.enumValueIndex;
                bool currentModeUsesProjectileConfig = currentMode == RangedAttackMode.Projectile ||
                                                       currentMode == RangedAttackMode.ProjectileArc;
                bool currentModeDisabled = currentMode == RangedAttackMode.DisabledUseSkills;
                
                // HITSCAN - Show effects configuration
                if (currentMode == RangedAttackMode.Hitscan)
                {
                    var hitscanConfigContainer = new VisualElement();
                    hitscanConfigContainer.style.marginTop = 10;
                    hitscanConfigContainer.style.paddingLeft = 10;
                    hitscanConfigContainer.style.borderLeftWidth = 2;
                    hitscanConfigContainer.style.borderLeftColor = new Color(0.8f, 0.6f, 0.2f, 0.8f);
                    
                    var configLabel = new Label("[Lightning] Hitscan Effects Settings");
                    configLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    configLabel.style.marginBottom = 5;
                    hitscanConfigContainer.Add(configLabel);
                    
                    var hitscanConfigProp = serializedObject.FindProperty("hitscanEffectsConfig");
                    if (hitscanConfigProp != null)
                    {
                        // Spawn Point settings
                        var spawnLabel = new Label("Muzzle/Spawn Point");
                        spawnLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        spawnLabel.style.marginTop = 4;
                        hitscanConfigContainer.Add(spawnLabel);
                        
                        var hitscanSpawnModeProp = hitscanConfigProp.FindPropertyRelative("spawnPointMode");
                        var currentHitscanSpawnMode = (ProjectileSpawnPointMode)hitscanSpawnModeProp.enumValueIndex;
                        var hitscanSpawnModeField = new EnumField("Spawn Point Mode", currentHitscanSpawnMode);
                        hitscanSpawnModeField.style.marginTop = 4;
                        hitscanSpawnModeField.style.marginBottom = 4;
                        
                        hitscanSpawnModeField.RegisterValueChangedCallback(evt => {
                            var newSpawnMode = (ProjectileSpawnPointMode)evt.newValue;
                            int newIndex = (int)newSpawnMode;
                            
                            if (hitscanSpawnModeProp.enumValueIndex != newIndex)
                            {
                                hitscanSpawnModeProp.enumValueIndex = newIndex;
                                serializedObject.ApplyModifiedProperties();
                            }
                            
                            if (!_isRefreshing)
                            {
                                EditorApplication.delayCall += () => {
                                    if (this != null && !_isRefreshing)
                                        ShowTab("combat");
                                };
                            }
                        });
                        hitscanConfigContainer.Add(hitscanSpawnModeField);
                        
                        var hitscanSpawnMode = (ProjectileSpawnPointMode)hitscanSpawnModeProp.enumValueIndex;
                        
                        switch (hitscanSpawnMode)
                        {
                            case ProjectileSpawnPointMode.Transform:
                                hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("spawnPointTransform")) { label = "Muzzle Transform" });
                                break;
                            case ProjectileSpawnPointMode.BonePath:
                                hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("spawnBonePath")) { label = "Bone Path" });
                                break;
                            case ProjectileSpawnPointMode.BoneName:
                                hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("spawnBoneName")) { label = "Bone Name" });
                                break;
                        }
                        
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("spawnOffset")) { label = hitscanSpawnMode == ProjectileSpawnPointMode.Offset ? "Spawn Offset" : "Fallback Offset" });
                        
                        // Muzzle Effects
                        var muzzleLabel = new Label("Muzzle Effects");
                        muzzleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        muzzleLabel.style.marginTop = 8;
                        hitscanConfigContainer.Add(muzzleLabel);
                        
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("muzzleVFX")) { label = "Muzzle Flash VFX" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("muzzleVFXDuration")) { label = "VFX Duration Override (0 = auto)" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("fireSound")) { label = "Fire Sound" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("fireSoundVolume")) { label = "Fire Volume" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("fireSoundPitchVariation")) { label = "Fire Pitch Variation" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("fireSoundSpatialBlend")) { label = "Fire Spatial Blend" });
                        
                        // Impact Effects
                        var impactLabel = new Label("Impact Effects");
                        impactLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        impactLabel.style.marginTop = 8;
                        hitscanConfigContainer.Add(impactLabel);
                        
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("impactVFX")) { label = "Impact VFX" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("impactVFXDuration")) { label = "VFX Duration Override (0 = auto)" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("impactSound")) { label = "Impact Sound" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("impactSoundVolume")) { label = "Impact Volume" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("impactSoundPitchVariation")) { label = "Impact Pitch Variation" });
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("impactSoundSpatialBlend")) { label = "Impact Spatial Blend" });
                        
                        // Timing
                        var timingLabel = new Label("Timing");
                        timingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        timingLabel.style.marginTop = 8;
                        hitscanConfigContainer.Add(timingLabel);
                        
                        hitscanConfigContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("effectDelay")) { label = "Effect Delay (anim sync)" });
                        
                        // Piercing (Penetration)
                        var pierceLabel = new Label("Piercing (Penetration)");
                        pierceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        pierceLabel.style.marginTop = 8;
                        hitscanConfigContainer.Add(pierceLabel);
                        
                        var pierceHelpBox = new HelpBox(
                            "Enable piercing to hit multiple enemies in a line (railgun, piercing laser). " +
                            "Damage falls off with each target pierced.",
                            HelpBoxMessageType.Info);
                        pierceHelpBox.style.marginTop = 4;
                        pierceHelpBox.style.marginBottom = 4;
                        hitscanConfigContainer.Add(pierceHelpBox);
                        
                        // Pierce Enable Toggle
                        var enablePierceProp = hitscanConfigProp.FindPropertyRelative("enablePiercing");
                        var enablePierceToggle = new PropertyField(enablePierceProp) { label = "Enable Piercing" };
                        hitscanConfigContainer.Add(enablePierceToggle);
                        
                        // Pierce Settings Container (only visible when piercing is enabled)
                        var pierceSettingsContainer = new VisualElement();
                        pierceSettingsContainer.style.marginLeft = 10;
                        pierceSettingsContainer.style.display = enablePierceProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                        
                        pierceSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("maxPierceTargets")) { label = "Max Pierce Targets (0=unlimited)" });
                        pierceSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("pierceDamageFalloff")) { label = "Damage Falloff per Target" });
                        pierceSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("pierceVFX")) { label = "Pierce Impact VFX" });
                        
                        hitscanConfigContainer.Add(pierceSettingsContainer);
                        
                        // Update visibility when toggle changes
                        enablePierceToggle.RegisterCallback<ChangeEvent<bool>>(evt => {
                            pierceSettingsContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                        });
                        
                        // AOE/Splash Damage
                        var aoeLabel = new Label("AOE/Splash Damage");
                        aoeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        aoeLabel.style.marginTop = 8;
                        hitscanConfigContainer.Add(aoeLabel);
                        
                        var aoeHelpBox = new HelpBox(
                            "Add splash damage for shotguns, explosive rounds, or energy weapons. " +
                            "AOE damage is applied to all enemies within radius, separate from direct hit damage.",
                            HelpBoxMessageType.Info);
                        aoeHelpBox.style.marginTop = 4;
                        aoeHelpBox.style.marginBottom = 4;
                        hitscanConfigContainer.Add(aoeHelpBox);
                        
                        // AOE Enable Toggle
                        var enableAoeProp = hitscanConfigProp.FindPropertyRelative("enableAOE");
                        var enableAoeToggle = new PropertyField(enableAoeProp) { label = "Enable AOE" };
                        hitscanConfigContainer.Add(enableAoeToggle);
                        
                        // AOE Settings Container (only visible when AOE is enabled)
                        var aoeSettingsContainer = new VisualElement();
                        aoeSettingsContainer.style.marginLeft = 10;
                        aoeSettingsContainer.style.display = enableAoeProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                        
                        aoeSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("aoeRadius")) { label = "AOE Radius" });
                        aoeSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("aoeDamage")) { label = "AOE Damage" });
                        aoeSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("aoeFalloffMinDamage")) { label = "Min Damage at Edge" });
                        aoeSettingsContainer.Add(new PropertyField(hitscanConfigProp.FindPropertyRelative("aoeVFX")) { label = "AOE Explosion VFX" });
                        
                        hitscanConfigContainer.Add(aoeSettingsContainer);
                        
                        // Update visibility when toggle changes
                        enableAoeToggle.RegisterCallback<ChangeEvent<bool>>(evt => {
                            aoeSettingsContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                        });
                    }
                    
                    rangedAttackSection.Add(hitscanConfigContainer);
                }
                // PROJECTILE / PROJECTILE ARC - Show projectile configuration
                else if (currentModeUsesProjectileConfig)
                {
                    var projectileConfigContainer = new VisualElement();
                    projectileConfigContainer.style.marginTop = 10;
                    projectileConfigContainer.style.paddingLeft = 10;
                    projectileConfigContainer.style.borderLeftWidth = 2;
                    projectileConfigContainer.style.borderLeftColor = new Color(0.4f, 0.6f, 0.8f, 0.8f);
                    
                    var configLabel = new Label(currentMode == RangedAttackMode.Projectile ? 
                        "[Pin] Linear Projectile Settings" : "[Target] Arc Projectile Settings");
                    configLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    configLabel.style.marginBottom = 5;
                    projectileConfigContainer.Add(configLabel);
                    
                    var projectileConfigProp = serializedObject.FindProperty("projectileConfig");
                    if (projectileConfigProp != null)
                    {
                        // Always show these
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("projectileVisual")) { label = "Projectile Visual" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("visualScale")) { label = "Visual Scale" });

                        var projectileDamageNote = new HelpBox(
                            "Damage comes from the core Ranged Attack Damage field above.",
                            HelpBoxMessageType.Info);
                        projectileConfigContainer.Add(projectileDamageNote);
                        
                        // Show speed for linear, angle for arc
                        if (currentMode == RangedAttackMode.Projectile)
                        {
                            projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("projectileSpeed")) { label = "Projectile Speed" });
                            
                            // Piercing (Linear Only) - show after speed
                            var piercingLabel = new Label("Piercing (Penetration)");
                            piercingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            piercingLabel.style.marginTop = 8;
                            projectileConfigContainer.Add(piercingLabel);
                            
                            var piercingHelpBox = new HelpBox(
                                "Enable to let projectiles pass through enemies, damaging all in their path.",
                                HelpBoxMessageType.Info);
                            piercingHelpBox.style.marginTop = 4;
                            piercingHelpBox.style.marginBottom = 4;
                            projectileConfigContainer.Add(piercingHelpBox);
                            
                            // Pierce Enable Toggle
                            var enableProjPierceProp = projectileConfigProp.FindPropertyRelative("enablePiercing");
                            var enableProjPierceToggle = new PropertyField(enableProjPierceProp) { label = "Enable Piercing" };
                            projectileConfigContainer.Add(enableProjPierceToggle);
                            
                            // Pierce Settings Container
                            var projPierceSettingsContainer = new VisualElement();
                            projPierceSettingsContainer.style.marginLeft = 10;
                            projPierceSettingsContainer.style.display = enableProjPierceProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                            
                            projPierceSettingsContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("maxPierceTargets")) { label = "Max Targets (0=unlimited)" });
                            projPierceSettingsContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("pierceDamageFalloff")) { label = "Damage Falloff per Target" });
                            projPierceSettingsContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("pierceVFX")) { label = "Pierce Impact VFX" });
                            
                            projectileConfigContainer.Add(projPierceSettingsContainer);
                            
                            // Update visibility when toggle changes
                            enableProjPierceToggle.RegisterCallback<ChangeEvent<bool>>(evt => {
                                projPierceSettingsContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                            });
                        }
                        else // ProjectileArc
                        {
                            projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("launchAngle")) { label = "Launch Angle (degrees)" });
                            projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("fixedFlightTime")) { label = "Fixed Flight Time (0 = auto)" });
                        }
                        
                        // Spawn Point settings
                        var spawnLabel = new Label("Spawn Point");
                        spawnLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        spawnLabel.style.marginTop = 8;
                        projectileConfigContainer.Add(spawnLabel);
                        
                        var spawnModeProp = projectileConfigProp.FindPropertyRelative("spawnPointMode");
                        var currentSpawnMode = (ProjectileSpawnPointMode)spawnModeProp.enumValueIndex;
                        var spawnModeField = new EnumField("Spawn Point Mode", currentSpawnMode);
                        spawnModeField.style.marginTop = 4;
                        spawnModeField.style.marginBottom = 4;
                        
                        spawnModeField.RegisterValueChangedCallback(evt => {
                            var newSpawnMode = (ProjectileSpawnPointMode)evt.newValue;
                            int newIndex = (int)newSpawnMode;
                            
                            if (spawnModeProp.enumValueIndex != newIndex)
                            {
                                spawnModeProp.enumValueIndex = newIndex;
                                serializedObject.ApplyModifiedProperties();
                            }
                            
                            if (!_isRefreshing)
                            {
                                EditorApplication.delayCall += () => {
                                    if (this != null && !_isRefreshing)
                                        ShowTab("combat");
                                };
                            }
                        });
                        projectileConfigContainer.Add(spawnModeField);
                        
                        var spawnMode = (ProjectileSpawnPointMode)spawnModeProp.enumValueIndex;
                        
                        switch (spawnMode)
                        {
                            case ProjectileSpawnPointMode.Transform:
                                projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("spawnPointTransform")) { label = "Spawn Point Transform" });
                                var transformHelp = new HelpBox("Drag a transform from your agent prefab (e.g., Gun_Muzzle, RightHand)", HelpBoxMessageType.None);
                                projectileConfigContainer.Add(transformHelp);
                                break;
                                
                            case ProjectileSpawnPointMode.BonePath:
                                projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("spawnBonePath")) { label = "Bone Path" });
                                var pathHelp = new HelpBox("Path from agent root, e.g.:\nArmature/Hips/Spine/RightArm/RightHand/Gun_Muzzle", HelpBoxMessageType.None);
                                projectileConfigContainer.Add(pathHelp);
                                break;
                                
                            case ProjectileSpawnPointMode.BoneName:
                                projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("spawnBoneName")) { label = "Bone Name" });
                                var nameHelp = new HelpBox("Name of bone/transform to find, e.g.:\nGun_Muzzle, RightHand, Weapon_Tip", HelpBoxMessageType.None);
                                projectileConfigContainer.Add(nameHelp);
                                break;
                        }
                        
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("spawnOffset")) { label = spawnMode == ProjectileSpawnPointMode.Offset ? "Spawn Offset" : "Fallback Offset" });
                        
                        // Timing settings
                        var timingLabel = new Label("Timing");
                        timingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        timingLabel.style.marginTop = 8;
                        projectileConfigContainer.Add(timingLabel);
                        
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("spawnDelay")) { label = "Spawn Delay (animation sync)" });
                        
                        // VFX settings
                        var vfxLabel = new Label("Visual Effects");
                        vfxLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        vfxLabel.style.marginTop = 8;
                        projectileConfigContainer.Add(vfxLabel);
                        
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("impactVFX")) { label = "Impact VFX" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("trailVFX")) { label = "Trail VFX" });
                        
                        // Audio settings
                        var audioLabel = new Label("Audio");
                        audioLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        audioLabel.style.marginTop = 8;
                        projectileConfigContainer.Add(audioLabel);
                        
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("fireSound")) { label = "Fire Sound" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("fireSoundVolume")) { label = "Fire Sound Volume" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("fireSoundPitchVariation")) { label = "Fire Pitch Variation" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("fireSoundSpatialBlend")) { label = "Fire Spatial Blend" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("impactSound")) { label = "Impact Sound" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("impactSoundVolume")) { label = "Impact Sound Volume" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("impactSoundPitchVariation")) { label = "Impact Pitch Variation" });
                        projectileConfigContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("impactSoundSpatialBlend")) { label = "Impact Spatial Blend" });
                        
                        // AOE/Splash Damage settings
                        var aoeLabel = new Label("AOE/Splash Damage");
                        aoeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        aoeLabel.style.marginTop = 8;
                        projectileConfigContainer.Add(aoeLabel);
                        
                        var projAoeHelpBox = new HelpBox(
                            "Enable splash damage for explosive projectiles (grenades, rockets, artillery shells).",
                            HelpBoxMessageType.Info);
                        projAoeHelpBox.style.marginTop = 4;
                        projAoeHelpBox.style.marginBottom = 4;
                        projectileConfigContainer.Add(projAoeHelpBox);
                        
                        // AOE Enable Toggle
                        var enableProjectileAoeProp = projectileConfigProp.FindPropertyRelative("enableAOE");
                        var enableProjectileAoeToggle = new PropertyField(enableProjectileAoeProp) { label = "Enable AOE" };
                        projectileConfigContainer.Add(enableProjectileAoeToggle);
                        
                        // AOE Settings Container (only visible when AOE is enabled)
                        var projAoeSettingsContainer = new VisualElement();
                        projAoeSettingsContainer.style.marginLeft = 10;
                        projAoeSettingsContainer.style.display = enableProjectileAoeProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                        
                        projAoeSettingsContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("aoeRadius")) { label = "AOE Radius" });
                        projAoeSettingsContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("aoeFalloffMinDamage")) { label = "Min Damage at Edge" });
                        projAoeSettingsContainer.Add(new PropertyField(projectileConfigProp.FindPropertyRelative("aoeVFX")) { label = "AOE Explosion VFX" });
                        
                        projectileConfigContainer.Add(projAoeSettingsContainer);
                        
                        // Update visibility when toggle changes
                        enableProjectileAoeToggle.RegisterCallback<ChangeEvent<bool>>(evt => {
                            projAoeSettingsContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                        });
                    }
                    
                    rangedAttackSection.Add(projectileConfigContainer);
                }
                else if (currentModeDisabled)
                {
                    var disabledHelp = new HelpBox(
                        "Legacy ranged auto-attack is disabled. Agents in ranged mode can only deal ranged damage through Agent Skills.\n" +
                        "Hybrid units still use normal melee attacks when they switch to melee mode.",
                        HelpBoxMessageType.Info);
                    disabledHelp.style.marginTop = 10;
                    rangedAttackSection.Add(disabledHelp);

                    var skillConfigProp = serializedObject.FindProperty("skillSystemConfig");
                    var skillSystemEnabledProp = skillConfigProp?.FindPropertyRelative("enableSkillSystem");
                    if (skillSystemEnabledProp != null && !skillSystemEnabledProp.boolValue)
                    {
                        var warning = new HelpBox(
                            "Skill System is currently disabled. With Disabled (Use Skills) selected, ranged units will not perform legacy ranged attacks.",
                            HelpBoxMessageType.Warning);
                        warning.style.marginTop = 6;
                        rangedAttackSection.Add(warning);
                    }
                }
                
                // ─────────────────────────────────────────────────────────────
                // SECONDARY RANGED ATTACK (e.g., rifle + grenade)
                // ─────────────────────────────────────────────────────────────
                if (!currentModeDisabled)
                {
                    var secondarySection = new Foldout { text = "Secondary Ranged Attack", value = false };
                    secondarySection.style.marginTop = 10;
                
                    var secondaryHelpBox = new HelpBox(
                        "Enable a secondary attack for versatile units (e.g., rifle squad with grenades).\n" +
                        "The chance system works like block chance - each attack rolls to use primary or secondary.",
                        HelpBoxMessageType.Info);
                    secondarySection.Add(secondaryHelpBox);
                
                    var enableSecondaryProp = serializedObject.FindProperty("enableSecondaryRangedAttack");
                    var enableSecondaryField = new PropertyField(enableSecondaryProp, "Enable Secondary Attack");
                
                    // Track enable secondary changes
                    bool currentEnableSecondary = enableSecondaryProp.boolValue;
                    if (_lastEnableSecondaryValue != currentEnableSecondary && _lastEnableSecondaryValue != currentEnableSecondary)
                    {
                        _lastEnableSecondaryValue = currentEnableSecondary;
                    }
                    enableSecondaryField.RegisterValueChangeCallback(evt => {
                        bool newValue = enableSecondaryProp.boolValue;
                        if (!_isRefreshing && newValue != _lastEnableSecondaryValue)
                        {
                            _lastEnableSecondaryValue = newValue;
                            EditorApplication.delayCall += () => {
                                if (this != null && !_isRefreshing)
                                    ShowTab("combat");
                            };
                        }
                    });
                
                    secondarySection.Add(enableSecondaryField);
                
                    // Only show secondary settings if enabled
                    if (enableSecondaryProp.boolValue)
                    {
                        var secondaryContainer = new VisualElement();
                        secondaryContainer.style.marginTop = 5;
                        secondaryContainer.style.paddingLeft = 10;
                        secondaryContainer.style.borderLeftWidth = 2;
                        secondaryContainer.style.borderLeftColor = new Color(0.8f, 0.5f, 0.2f, 0.8f);
                    
                    // Chance slider
                    secondaryContainer.Add(new PropertyField(serializedObject.FindProperty("secondaryAttackChance")) { label = "Use Chance (0-1)" });
                    secondaryContainer.Add(new PropertyField(serializedObject.FindProperty("secondaryAttackCooldown")) { label = "Cooldown (seconds)" });
                    
                    // Secondary attack mode - using EnumField for better dropdown behavior
                    var secondaryModeProp = serializedObject.FindProperty("secondaryAttackMode");
                    var currentSecondaryEnumValue = (RangedAttackMode)secondaryModeProp.enumValueIndex;
                    var secondaryModeField = new EnumField("Secondary Mode", currentSecondaryEnumValue);
                    secondaryModeField.style.marginTop = 4;
                    secondaryModeField.style.marginBottom = 4;
                    
                    // Track secondary mode changes
                    if (_lastSecondaryModeValue == -1)
                        _lastSecondaryModeValue = secondaryModeProp.enumValueIndex;
                    
                    secondaryModeField.RegisterValueChangedCallback(evt => {
                        var newEnumValue = (RangedAttackMode)evt.newValue;
                        int newIndex = (int)newEnumValue;
                        
                        if (secondaryModeProp.enumValueIndex != newIndex)
                        {
                            secondaryModeProp.enumValueIndex = newIndex;
                            serializedObject.ApplyModifiedProperties();
                        }
                        
                        if (!_isRefreshing && newIndex != _lastSecondaryModeValue)
                        {
                            _lastSecondaryModeValue = newIndex;
                            EditorApplication.delayCall += () => {
                                if (this != null && !_isRefreshing)
                                    ShowTab("combat");
                            };
                        }
                    });
                    secondaryContainer.Add(secondaryModeField);
                    
                        var secondaryMode = (RangedAttackMode)secondaryModeProp.enumValueIndex;
                        bool secondaryUsesProjectileConfig = secondaryMode == RangedAttackMode.Projectile ||
                                                             secondaryMode == RangedAttackMode.ProjectileArc;
                    
                        // Secondary projectile config
                        if (secondaryUsesProjectileConfig)
                        {
                            var secondaryConfigLabel = new Label(secondaryMode == RangedAttackMode.Projectile ? 
                                "[Pin] Secondary Projectile Settings" : "[Bomb] Secondary Arc Settings (Grenades)");
                            secondaryConfigLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            secondaryConfigLabel.style.marginTop = 8;
                            secondaryContainer.Add(secondaryConfigLabel);
                        
                        var secondaryConfigProp = serializedObject.FindProperty("secondaryProjectileConfig");
                        if (secondaryConfigProp != null)
                        {
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("projectileVisual")) { label = "Projectile Visual" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("visualScale")) { label = "Visual Scale" });

                            var secondaryProjectileDamageNote = new HelpBox(
                                "Damage comes from the core Ranged Attack Damage field above.",
                                HelpBoxMessageType.Info);
                            secondaryContainer.Add(secondaryProjectileDamageNote);
                            
                            if (secondaryMode == RangedAttackMode.Projectile)
                            {
                                secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("projectileSpeed")) { label = "Projectile Speed" });
                            }
                            else // ProjectileArc
                            {
                                secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("launchAngle")) { label = "Launch Angle (degrees)" });
                                secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("fixedFlightTime")) { label = "Fixed Flight Time (0 = auto)" });
                            }
                            
                            // Spawn Point settings for secondary - using EnumField for better dropdown behavior
                            var secondarySpawnLabel = new Label("Spawn Point");
                            secondarySpawnLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            secondarySpawnLabel.style.marginTop = 8;
                            secondaryContainer.Add(secondarySpawnLabel);
                            
                            var secondarySpawnModeProp = secondaryConfigProp.FindPropertyRelative("spawnPointMode");
                            var currentSecondarySpawnMode = (ProjectileSpawnPointMode)secondarySpawnModeProp.enumValueIndex;
                            var secondarySpawnModeField = new EnumField("Spawn Point Mode", currentSecondarySpawnMode);
                            secondarySpawnModeField.style.marginTop = 4;
                            secondarySpawnModeField.style.marginBottom = 4;
                            
                            secondarySpawnModeField.RegisterValueChangedCallback(evt =>
                            {
                                var newSpawnMode = (ProjectileSpawnPointMode)evt.newValue;
                                int newIndex = (int)newSpawnMode;
                                
                                if (secondarySpawnModeProp.enumValueIndex != newIndex)
                                {
                                    secondarySpawnModeProp.enumValueIndex = newIndex;
                                    serializedObject.ApplyModifiedProperties();
                                }
                                
                                if (_isRefreshing) return;
                                EditorApplication.delayCall += () =>
                                {
                                    if (this != null && !_isRefreshing)
                                        ShowTab("combat");
                                };
                            });
                            secondaryContainer.Add(secondarySpawnModeField);
                            
                            var secondarySpawnMode = (ProjectileSpawnPointMode)secondarySpawnModeProp.enumValueIndex;
                            if (secondarySpawnMode == ProjectileSpawnPointMode.Transform)
                            {
                                secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("spawnPointTransform")) { label = "Spawn Transform (from Prefab)" });
                            }
                            else if (secondarySpawnMode == ProjectileSpawnPointMode.BonePath)
                            {
                                secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("spawnBonePath")) { label = "Bone Path" });
                            }
                            else if (secondarySpawnMode == ProjectileSpawnPointMode.BoneName)
                            {
                                secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("spawnBoneName")) { label = "Bone Name" });
                            }
                            
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("spawnOffset")) { label = secondarySpawnMode == ProjectileSpawnPointMode.Offset ? "Spawn Offset" : "Fallback Offset" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("spawnDelay")) { label = "Spawn Delay" });
                            
                            // VFX Settings
                            var secVfxLabel = new Label("Visual Effects");
                            secVfxLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            secVfxLabel.style.marginTop = 8;
                            secondaryContainer.Add(secVfxLabel);
                            
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("impactVFX")) { label = "Impact VFX" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("trailVFX")) { label = "Trail VFX" });
                            
                            // Audio Settings
                            var secAudioLabel = new Label("Audio");
                            secAudioLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            secAudioLabel.style.marginTop = 8;
                            secondaryContainer.Add(secAudioLabel);
                            
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("fireSound")) { label = "Fire Sound" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("fireSoundVolume")) { label = "Fire Sound Volume" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("fireSoundPitchVariation")) { label = "Fire Pitch Variation" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("fireSoundSpatialBlend")) { label = "Fire Spatial Blend" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("impactSound")) { label = "Impact Sound" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("impactSoundVolume")) { label = "Impact Sound Volume" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("impactSoundPitchVariation")) { label = "Impact Pitch Variation" });
                            secondaryContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("impactSoundSpatialBlend")) { label = "Impact Spatial Blend" });
                            
                            // AOE/Splash Damage Settings
                            var secAoeLabel = new Label("AOE/Splash Damage");
                            secAoeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            secAoeLabel.style.marginTop = 8;
                            secondaryContainer.Add(secAoeLabel);
                            
                            var secAoeHelpBox = new HelpBox(
                                "Enable splash damage for explosive projectiles (grenades, rockets, artillery shells).",
                                HelpBoxMessageType.Info);
                            secAoeHelpBox.style.marginTop = 4;
                            secAoeHelpBox.style.marginBottom = 4;
                            secondaryContainer.Add(secAoeHelpBox);
                            
                            // AOE Enable Toggle
                            var enableSecAoeProp = secondaryConfigProp.FindPropertyRelative("enableAOE");
                            var enableSecAoeToggle = new PropertyField(enableSecAoeProp) { label = "Enable AOE" };
                            secondaryContainer.Add(enableSecAoeToggle);
                            
                            // AOE Settings Container (only visible when AOE is enabled)
                            var secAoeSettingsContainer = new VisualElement();
                            secAoeSettingsContainer.style.marginLeft = 10;
                            secAoeSettingsContainer.style.display = enableSecAoeProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                            
                            secAoeSettingsContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("aoeRadius")) { label = "AOE Radius" });
                            secAoeSettingsContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("aoeFalloffMinDamage")) { label = "Min Damage at Edge" });
                            secAoeSettingsContainer.Add(new PropertyField(secondaryConfigProp.FindPropertyRelative("aoeVFX")) { label = "AOE Explosion VFX" });
                            
                            secondaryContainer.Add(secAoeSettingsContainer);
                            
                            // Update visibility when toggle changes
                            enableSecAoeToggle.RegisterCallback<ChangeEvent<bool>>(evt => {
                                secAoeSettingsContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                            });
                        }
                        else if (secondaryMode == RangedAttackMode.DisabledUseSkills)
                        {
                            var secondaryDisabledHelp = new HelpBox(
                                "Secondary mode is set to Disabled (Use Skills), so secondary legacy ranged attacks are ignored.",
                                HelpBoxMessageType.Info);
                            secondaryDisabledHelp.style.marginTop = 8;
                            secondaryContainer.Add(secondaryDisabledHelp);
                        }
                    }
                    else
                    {
                        var hitscanNote = new HelpBox("Secondary is Hitscan - uses instant damage (no projectile config needed)", HelpBoxMessageType.Info);
                        secondaryContainer.Add(hitscanNote);
                    }
                    
                        secondarySection.Add(secondaryContainer);
                    }
                    
                    rangedAttackSection.Add(secondarySection);
                }
                else
                {
                    var secondaryDisabledByPrimary = new HelpBox(
                        "Secondary Ranged Attack is hidden because primary Attack Mode is set to Disabled (Use Skills).",
                        HelpBoxMessageType.None);
                    secondaryDisabledByPrimary.style.marginTop = 10;
                    rangedAttackSection.Add(secondaryDisabledByPrimary);
                }
                
                rangedAttackSection.Bind(serializedObject);
                combatSettingsContainer.Add(rangedAttackSection);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // MELEE ATTACK SETTINGS (For melee-capable templates)
            // Shown for: MeleeSwarm, MeleeQueued, MeleeBattleLine, MeleeWaves, HybridAdaptive, Custom
            // ═══════════════════════════════════════════════════════════════════
            bool isMeleeCapable = currentTemplate == CombatBehaviorTemplate.MeleeSwarm ||
                                  currentTemplate == CombatBehaviorTemplate.MeleeQueued ||
                                  currentTemplate == CombatBehaviorTemplate.MeleeBattleLine ||
                                  currentTemplate == CombatBehaviorTemplate.MeleeWaves ||
                                  currentTemplate == CombatBehaviorTemplate.HybridAdaptive ||
                                  isCustom;
            
            if (isMeleeCapable)
            {
                var meleeAttackSection = new Foldout { text = "Melee Attack Mode", value = true };
                meleeAttackSection.AddToClassList("section");
                
                var meleeModeHelpBox = new HelpBox(
                    "Configure melee basic attacks and melee-specific hit behavior.\n\n" +
                    "• Disable melee auto attacks for skill-only melee units\n" +
                    "• Control min/max melee attack range\n" +
                    "• Configure melee VFX/audio and melee hit status effects",
                    HelpBoxMessageType.Info);
                meleeAttackSection.Add(meleeModeHelpBox);

                AddBoundField(meleeAttackSection, "enableMeleeAutoAttack");
                AddBoundField(meleeAttackSection, "meleeMinDistance");
                AddBoundField(meleeAttackSection, "meleeMaxDistance");
                
                var meleeEffectsHeader = new Label("Melee VFX & Audio");
                meleeEffectsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                meleeEffectsHeader.style.marginTop = 10;
                meleeAttackSection.Add(meleeEffectsHeader);

                var meleeEffectsHelpBox = new HelpBox(
                    "Configure VFX and audio for melee swings and impacts.\n\n" +
                    "• Swing Effects: Play when the agent attacks (slash trails, whoosh sounds)\n" +
                    "• Hit Impact: Play when the attack connects (blood splatter, impact sounds)",
                    HelpBoxMessageType.Info);
                meleeAttackSection.Add(meleeEffectsHelpBox);
                
                var meleeConfigProp = serializedObject.FindProperty("meleeAttackEffectsConfig");
                if (meleeConfigProp != null)
                {
                    // Enable toggle
                    var enabledProp = meleeConfigProp.FindPropertyRelative("enabled");
                    var enableToggle = new Toggle("Enable Melee Effects");
                    enableToggle.value = enabledProp.boolValue;
                    enableToggle.style.marginTop = 4;
                    enableToggle.style.marginBottom = 8;
                    enableToggle.RegisterValueChangedCallback(evt => {
                        enabledProp.boolValue = evt.newValue;
                        serializedObject.ApplyModifiedProperties();
                        // Refresh to show/hide the settings
                        if (!_isRefreshing)
                        {
                            EditorApplication.delayCall += () => {
                                if (this != null && !_isRefreshing)
                                    ShowTab("combat");
                            };
                        }
                    });
                    meleeAttackSection.Add(enableToggle);
                    
                    // Only show details if enabled
                    if (enabledProp.boolValue)
                    {
                        var meleeConfigContainer = new VisualElement();
                        meleeConfigContainer.style.paddingLeft = 10;
                        meleeConfigContainer.style.borderLeftWidth = 2;
                        meleeConfigContainer.style.borderLeftColor = new Color(0.6f, 0.3f, 0.2f, 0.8f);
                        
                        // Swing/Attack Effects
                        var swingLabel = new Label("Swing/Attack Effects");
                        swingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        swingLabel.style.marginTop = 4;
                        meleeConfigContainer.Add(swingLabel);
                        
                        var swingHelp = new Label("Effects when the agent swings their weapon.");
                        swingHelp.style.fontSize = 10;
                        swingHelp.style.color = new Color(0.5f, 0.5f, 0.5f);
                        swingHelp.style.marginBottom = 4;
                        meleeConfigContainer.Add(swingHelp);
                        
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingVFX")) { label = "Swing VFX" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingVFXDuration")) { label = "VFX Duration Override (0 = auto)" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingVFXOffset")) { label = "VFX Spawn Offset" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingSound")) { label = "Swing Sound" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingSoundVolume")) { label = "Volume" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingSoundPitchVariation")) { label = "Pitch Variation" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingSoundSpatialBlend")) { label = "Spatial Blend" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("swingEffectDelay")) { label = "Effect Delay" });
                        
                        // Hit Impact Effects
                        var impactLabel = new Label("Hit Impact Effects");
                        impactLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        impactLabel.style.marginTop = 12;
                        meleeConfigContainer.Add(impactLabel);
                        
                        var impactHelp = new Label("Effects when the attack connects with a target.");
                        impactHelp.style.fontSize = 10;
                        impactHelp.style.color = new Color(0.5f, 0.5f, 0.5f);
                        impactHelp.style.marginBottom = 4;
                        meleeConfigContainer.Add(impactHelp);
                        
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("hitImpactVFX")) { label = "Impact VFX" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("hitImpactVFXDuration")) { label = "VFX Duration Override (0 = auto)" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("hitImpactSound")) { label = "Impact Sound" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("hitImpactSoundVolume")) { label = "Volume" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("hitImpactSoundPitchVariation")) { label = "Pitch Variation" });
                        meleeConfigContainer.Add(new PropertyField(meleeConfigProp.FindPropertyRelative("hitImpactSoundSpatialBlend")) { label = "Spatial Blend" });
                        
                        meleeAttackSection.Add(meleeConfigContainer);
                    }
                }

                var meleeStatusHeader = new Label("Melee Hit Status Effects");
                meleeStatusHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                meleeStatusHeader.style.marginTop = 10;
                meleeAttackSection.Add(meleeStatusHeader);

                var meleeStatusHelp = new Label("Effects applied when melee attacks hit targets (bleed, poison, cleave, etc.).");
                meleeStatusHelp.style.whiteSpace = WhiteSpace.Normal;
                meleeStatusHelp.style.marginBottom = 5;
                meleeStatusHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
                meleeStatusHelp.style.fontSize = 10;
                meleeAttackSection.Add(meleeStatusHelp);

                AddBoundField(meleeAttackSection, "meleeAttackEffects");
                AddBoundField(meleeAttackSection, "meleeCleaveConfig");
                
                meleeAttackSection.Bind(serializedObject);
                combatSettingsContainer.Add(meleeAttackSection);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // CUSTOM TEMPLATE - FULL SETTINGS (Only shown for Custom)
            // ═══════════════════════════════════════════════════════════════════
            if (isCustom)
            {
                // Combat Range Section
                var rangeSection = new Foldout { text = "Combat Range Settings", value = true };
                rangeSection.AddToClassList("section");
                
                rangeSection.Add(new PropertyField(serializedObject.FindProperty("combatRangeType")) { bindingPath = "combatRangeType" });
                rangeSection.Add(new PropertyField(serializedObject.FindProperty("rangedMinDistance")) { bindingPath = "rangedMinDistance" });
                rangeSection.Add(new PropertyField(serializedObject.FindProperty("rangedMaxDistance")) { bindingPath = "rangedMaxDistance" });
                rangeSection.Add(new PropertyField(serializedObject.FindProperty("rangedPreferredDistance")) { bindingPath = "rangedPreferredDistance" });
                rangeSection.Bind(serializedObject);
                combatSettingsContainer.Add(rangeSection);
                
                // Combat Positioning Section
                var positioningSection = new Foldout { text = "Combat Positioning", value = true };
                positioningSection.AddToClassList("section");
                
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("positioningMode")) { bindingPath = "positioningMode" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("formationRadius")) { bindingPath = "formationRadius" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("maxConcurrentAttackers")) { bindingPath = "maxConcurrentAttackers" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("attackSlotDuration")) { bindingPath = "attackSlotDuration" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("allowOpportunisticAttacks")) { bindingPath = "allowOpportunisticAttacks" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("minAngleBetweenEnemies")) { bindingPath = "minAngleBetweenEnemies" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("closeRangeStopBuffer")) { bindingPath = "closeRangeStopBuffer" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("combatFacingSpeed")) { bindingPath = "combatFacingSpeed" });
                positioningSection.Add(new PropertyField(serializedObject.FindProperty("agentSpacing")) { bindingPath = "agentSpacing" });
                positioningSection.Bind(serializedObject);
                combatSettingsContainer.Add(positioningSection);
                
                // Behavior Section
                var behaviorSection = new Foldout { text = "Combat Behavior", value = true };
                behaviorSection.AddToClassList("section");
                
                AddBoundField(behaviorSection, "navTargetUpdateInterval");
                AddBoundField(behaviorSection, "maintainFacingBetweenAttacks");
                AddBoundField(behaviorSection, "stopMovementWhenClose");
                AddBoundField(behaviorSection, "lockMovementDuringAttacks");
                AddBoundField(behaviorSection, "triggerHitReactionOnDamage");
                AddBoundField(behaviorSection, "enablePostAttackRetreat");
                AddBoundField(behaviorSection, "postAttackRetreatDistance");
                
                combatSettingsContainer.Add(behaviorSection);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // DEATH EFFECTS (Always visible)
            // ═══════════════════════════════════════════════════════════════════
            var deathEffectsSection = new Foldout { text = "Death Effects", value = false };
            deathEffectsSection.AddToClassList("section");

            var deathEffectsHelp = new HelpBox(
                "Configure VFX/audio that play when an agent dies. Supports VFX Graph, particle prefabs, and death sound controls.",
                HelpBoxMessageType.Info);
            deathEffectsSection.Add(deathEffectsHelp);

            var deathEffectsProp = serializedObject.FindProperty("deathEffectsConfig");
            if (deathEffectsProp != null)
            {
                var enabledProp = deathEffectsProp.FindPropertyRelative("enabled");
                var enableToggle = new Toggle("Enable Death Effects")
                {
                    value = enabledProp.boolValue,
                    style = { marginTop = 4, marginBottom = 8 }
                };
                enableToggle.RegisterValueChangedCallback(evt =>
                {
                    enabledProp.boolValue = evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                    if (!_isRefreshing)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (this != null && !_isRefreshing)
                                ShowTab("combat");
                        };
                    }
                });
                deathEffectsSection.Add(enableToggle);

                if (enabledProp.boolValue)
                {
                    var deathEffectsContainer = new VisualElement();
                    deathEffectsContainer.style.paddingLeft = 10;
                    deathEffectsContainer.style.borderLeftWidth = 2;
                    deathEffectsContainer.style.borderLeftColor = new Color(0.2f, 0.5f, 0.7f, 0.8f);

                    var visualsLabel = new Label("Visuals (VFX)");
                    visualsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    visualsLabel.style.marginTop = 4;
                    deathEffectsContainer.Add(visualsLabel);

                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("deathVFX")) { label = "Death VFX" });
                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("deathVFXDuration")) { label = "VFX Duration Override (0 = auto)" });
                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("deathVFXOffset")) { label = "VFX Spawn Offset (Local)" });
                    var bindShapeField = new PropertyField(deathEffectsProp.FindPropertyRelative("bindShapeToRenderer")) { label = "Bind Particle Shape To Renderer" };
                    deathEffectsContainer.Add(bindShapeField);

                    var preferSkinnedField = new PropertyField(deathEffectsProp.FindPropertyRelative("preferSkinnedRenderer")) { label = "Prefer Skinned Mesh Renderer" };
                    deathEffectsContainer.Add(preferSkinnedField);

                    var autoFindField = new PropertyField(deathEffectsProp.FindPropertyRelative("autoFindFirstRenderer")) { label = "Auto-Find First Renderer" };
                    deathEffectsContainer.Add(autoFindField);

                    var rendererNameField = new PropertyField(deathEffectsProp.FindPropertyRelative("rendererName")) { label = "Renderer Name (Optional)" };
                    deathEffectsContainer.Add(rendererNameField);

                    // Hide name field when auto-find is enabled
                    bool autoFindEnabled = deathEffectsProp.FindPropertyRelative("autoFindFirstRenderer").boolValue;
                    rendererNameField.style.display = autoFindEnabled ? DisplayStyle.None : DisplayStyle.Flex;

                    autoFindField.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                    {
                        if (evt.changedProperty != null && evt.changedProperty.propertyPath.EndsWith("autoFindFirstRenderer"))
                        {
                            rendererNameField.style.display = evt.changedProperty.boolValue ? DisplayStyle.None : DisplayStyle.Flex;
                        }
                    });
                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("legacyDeathEffectPrefab")) { label = "Legacy Particle Prefab (Fallback)" });

                    var audioLabel = new Label("Audio");
                    audioLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    audioLabel.style.marginTop = 10;
                    deathEffectsContainer.Add(audioLabel);

                    deathEffectsContainer.Add(new PropertyField(serializedObject.FindProperty("deathSounds")) { label = "Death Sounds" });
                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("deathSoundVolume")) { label = "Volume" });
                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("deathSoundPitchVariation")) { label = "Pitch Variation" });
                    deathEffectsContainer.Add(new PropertyField(deathEffectsProp.FindPropertyRelative("deathSoundSpatialBlend")) { label = "Spatial Blend" });

                    deathEffectsSection.Add(deathEffectsContainer);
                }
            }

            // ── Flight Crash on Death (only visible for Flight 3D navigation) ──
            bool isFlight3D = serializedObject.FindProperty("navigationType")?.intValue == 30; // NavigationType.Flight3D
            if (isFlight3D)
            {
                var flightCrashContainer = new VisualElement();
                flightCrashContainer.style.marginTop = 8;

                var flightCrashHeader = new Label("Flight Crash on Death");
                flightCrashHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                flightCrashHeader.style.marginTop = 4;
                flightCrashContainer.Add(flightCrashHeader);

                var crashInfo = new HelpBox(
                    "When a flying agent is killed at altitude, simulate a physics-based crash descent " +
                    "with gravity, forward momentum, nose-down tumble, and spiral roll.",
                    HelpBoxMessageType.Info);
                flightCrashContainer.Add(crashInfo);

                var crashToggle = new PropertyField(serializedObject.FindProperty("flightCrashOnDeath")) { bindingPath = "flightCrashOnDeath", label = "Enable Crash on Death" };
                flightCrashContainer.Add(crashToggle);

                var crashParamsContainer = new VisualElement();
                crashParamsContainer.style.paddingLeft = 10;
                crashParamsContainer.style.borderLeftWidth = 2;
                crashParamsContainer.style.borderLeftColor = new Color(0.8f, 0.4f, 0.2f, 0.8f);
                crashParamsContainer.Add(new PropertyField(serializedObject.FindProperty("flightCrashGravity")) { bindingPath = "flightCrashGravity", label = "Crash Gravity" });
                crashParamsContainer.Add(new PropertyField(serializedObject.FindProperty("flightCrashInitialDownSpeed")) { bindingPath = "flightCrashInitialDownSpeed", label = "Initial Down Speed" });
                crashParamsContainer.Add(new PropertyField(serializedObject.FindProperty("flightCrashForwardMomentum")) { bindingPath = "flightCrashForwardMomentum", label = "Forward Momentum" });
                crashParamsContainer.Add(new PropertyField(serializedObject.FindProperty("flightCrashTumbleSpeed")) { bindingPath = "flightCrashTumbleSpeed", label = "Tumble Speed (°/s)" });
                crashParamsContainer.Add(new PropertyField(serializedObject.FindProperty("flightCrashSpiralSpeed")) { bindingPath = "flightCrashSpiralSpeed", label = "Spiral Speed (°/s)" });
                flightCrashContainer.Add(crashParamsContainer);

                // Show/hide crash params based on toggle
                crashToggle.RegisterValueChangeCallback(evt =>
                {
                    bool enabled = serializedObject.FindProperty("flightCrashOnDeath").boolValue;
                    crashParamsContainer.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
                });
                crashParamsContainer.style.display = serializedObject.FindProperty("flightCrashOnDeath").boolValue ? DisplayStyle.Flex : DisplayStyle.None;

                deathEffectsSection.Add(flightCrashContainer);
            }

            deathEffectsSection.Bind(serializedObject);
            combatSettingsContainer.Add(deathEffectsSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // DEATH & CLEANUP (Always visible - independent of combat template)
            // ═══════════════════════════════════════════════════════════════════
            var deathSection = new Foldout { text = "Death & Cleanup", value = false };
            deathSection.AddToClassList("section");
            
            AddBoundField(deathSection, "deadAgentCleanupDelay");
            AddBoundField(deathSection, "batchDeadAgentCleanup");
            AddBoundField(deathSection, "disableComponentsOnDeath");
            AddBoundField(deathSection, "optimizeDeadAgentLists");
            
            // Death Sinking subsection
            var sinkingLabel = new Label("Death Sinking Effect");
            sinkingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sinkingLabel.style.marginTop = 10;
            deathSection.Add(sinkingLabel);
            
            AddBoundField(deathSection, "enableDeathSinking");
            AddBoundField(deathSection, "deathSinkOffset");
            AddBoundField(deathSection, "deathSinkDuration");
            AddBoundField(deathSection, "deathSinkCurve");
            
            combatSettingsContainer.Add(deathSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // ADVANCED AI (Only for Hybrid Adaptive or Custom templates)
            // ═══════════════════════════════════════════════════════════════════
            if (isHybrid || isCustom)
            {
                var advancedSection = new Foldout { text = "Advanced Hybrid AI", value = false };
                advancedSection.AddToClassList("section");
                
                var hybridAIHelp = new Label("Controls AI decision-making for Hybrid combat mode (melee/ranged switching).");
                hybridAIHelp.AddToClassList("help-text");
                hybridAIHelp.style.whiteSpace = WhiteSpace.Normal;
                hybridAIHelp.style.marginBottom = 5;
                hybridAIHelp.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
                advancedSection.Add(hybridAIHelp);
                
                AddBoundField(advancedSection, "useAdvancedHybridAI");
                
                var cpuWarning = new Label("⚠ Advanced AI is CPU intensive. Recommended for <500 agents.\n" +
                                          "When OFF: Fast distance-based switching (500+ agents).\n" +
                                          "When ON: Tactical awareness, personality, ally scanning.");
                cpuWarning.style.whiteSpace = WhiteSpace.Normal;
                cpuWarning.style.marginTop = 5;
                cpuWarning.style.marginLeft = 15;
                cpuWarning.style.color = new UnityEngine.Color(0.7f, 0.6f, 0.4f);
                cpuWarning.style.fontSize = 10;
                advancedSection.Add(cpuWarning);
                
                combatSettingsContainer.Add(advancedSection);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // CHARGE ATTACK - DEPRECATED (Now use AgentSkills with ChargeEffect)
            // ═══════════════════════════════════════════════════════════════════
            var chargeConfigProp = serializedObject.FindProperty("chargeAttackConfig");
            var chargeEnabled = chargeConfigProp?.FindPropertyRelative("enableChargeAttack")?.boolValue ?? false;
            
            // Only show if legacy charge attack is enabled (for migration purposes)
            if (chargeEnabled)
            {
                var chargeSection = new Foldout { text = "⚠ Charge Attack (Legacy)", value = false };
                chargeSection.AddToClassList("section");
                
                var deprecationWarning = new HelpBox(
                    "⚠️ DEPRECATED: This charge attack system has been replaced by AgentSkills.\n\n" +
                    "Create an AgentSkill with a ChargeEffect instead - it offers:\n" +
                    "• Conditions (faction, level, health checks)\n" +
                    "• Level-gated features (unlock abilities as units level up)\n" +
                    "• Multiple effects per skill\n" +
                    "• Status effects on hit\n" +
                    "• Better integration with the skill system\n\n" +
                    "This section will be removed in a future update.", 
                    HelpBoxMessageType.Warning);
                chargeSection.Add(deprecationWarning);
                
                // Show enable toggle so user can disable it
                chargeSection.Add(new PropertyField(chargeConfigProp.FindPropertyRelative("enableChargeAttack")) { bindingPath = "chargeAttackConfig.enableChargeAttack" });
                
                chargeSection.Bind(serializedObject);
                combatSettingsContainer.Add(chargeSection);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // STATUS EFFECTS (Attack Effects & Support Abilities)
            // ═══════════════════════════════════════════════════════════════════
            var statusEffectsSection = new Foldout { text = "Status Effects & Abilities", value = false };
            statusEffectsSection.AddToClassList("section");
            
            var statusEffectsHelp = new Label("Configure status effects applied by attacks and support abilities for this faction.");
            statusEffectsHelp.AddToClassList("help-text");
            statusEffectsHelp.style.whiteSpace = WhiteSpace.Normal;
            statusEffectsHelp.style.marginBottom = 8;
            statusEffectsHelp.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
            statusEffectsSection.Add(statusEffectsHelp);
            
            // Ranged Attack Effects
            var rangedEffectsLabel = new Label("Ranged Attack Effects");
            rangedEffectsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            rangedEffectsLabel.style.marginTop = 10;
            statusEffectsSection.Add(rangedEffectsLabel);
            
            var rangedEffectsHelp = new Label("Effects applied when ranged attacks hit targets (e.g., burn, freeze, curse).");
            rangedEffectsHelp.style.whiteSpace = WhiteSpace.Normal;
            rangedEffectsHelp.style.marginBottom = 5;
            rangedEffectsHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            rangedEffectsHelp.style.fontSize = 10;
            statusEffectsSection.Add(rangedEffectsHelp);
            
            AddBoundField(statusEffectsSection, "rangedAttackEffects");
            
            // Support Abilities Header
            var supportHeader = new Label("Support Abilities");
            supportHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            supportHeader.style.marginTop = 15;
            supportHeader.style.fontSize = 12;
            supportHeader.style.color = new UnityEngine.Color(0.4f, 0.8f, 0.4f);
            statusEffectsSection.Add(supportHeader);
            
            var supportHelp = new Label("Configure healing and buff abilities for support-type units in this faction.");
            supportHelp.style.whiteSpace = WhiteSpace.Normal;
            supportHelp.style.marginBottom = 8;
            supportHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            supportHelp.style.fontSize = 10;
            statusEffectsSection.Add(supportHelp);
            
            // Healing Ability
            var healingLabel = new Label("Healing Ability");
            healingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            healingLabel.style.marginTop = 5;
            statusEffectsSection.Add(healingLabel);
            
            var healingHelp = new Label("Auto-cast healing on wounded allies within radius. Uses Regeneration or similar effect.");
            healingHelp.style.whiteSpace = WhiteSpace.Normal;
            healingHelp.style.marginBottom = 5;
            healingHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            healingHelp.style.fontSize = 10;
            statusEffectsSection.Add(healingHelp);
            
            AddBoundField(statusEffectsSection, "healingAbility");
            
            // Buff Ability
            var buffLabel = new Label("Buff Ability");
            buffLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            buffLabel.style.marginTop = 10;
            statusEffectsSection.Add(buffLabel);
            
            var buffHelp = new Label("Auto-cast buffs on nearby allies (e.g., damage buff, armor buff, speed buff).");
            buffHelp.style.whiteSpace = WhiteSpace.Normal;
            buffHelp.style.marginBottom = 5;
            buffHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            buffHelp.style.fontSize = 10;
            statusEffectsSection.Add(buffHelp);
            
            AddBoundField(statusEffectsSection, "buffAbility");
            
            // Special Abilities Toggle
            var specialAbilitiesLabel = new Label("Special Abilities Control");
            specialAbilitiesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            specialAbilitiesLabel.style.marginTop = 10;
            statusEffectsSection.Add(specialAbilitiesLabel);
            
            var specialAbilitiesHelp = new Label("Enable to unlock special abilities for this faction. Use AgentSkills to create custom abilities like Charge Attacks.");
            specialAbilitiesHelp.style.whiteSpace = WhiteSpace.Normal;
            specialAbilitiesHelp.style.marginBottom = 5;
            specialAbilitiesHelp.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            specialAbilitiesHelp.style.fontSize = 10;
            statusEffectsSection.Add(specialAbilitiesHelp);
            
            var specialAbilitiesProp = serializedObject.FindProperty("canUseSpecialAbilities");
            var specialAbilitiesField = new PropertyField(specialAbilitiesProp);
            specialAbilitiesField.bindingPath = "canUseSpecialAbilities";
            statusEffectsSection.Add(specialAbilitiesField);
            
            combatSettingsContainer.Add(statusEffectsSection);
            
            // ═══════════════════════════════════════════════════════════════════
            // VIRTUAL HITBOX SYSTEM
            // ═══════════════════════════════════════════════════════════════════
            var hitboxSection = CreateVirtualHitboxSection();
            combatSettingsContainer.Add(hitboxSection);
            
            // Add the combat settings container to the main container
            container.Add(combatSettingsContainer);
            
            return container;
        }
        
        #endregion
        
        #region Animations Tab
        
        private VisualElement CreateAnimationsTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            var info = new HelpBox("Override global animation settings for this faction. Leave empty to use controller defaults.", HelpBoxMessageType.Info);
            container.Add(info);
            
            // Animation Settings (culling is now global)
            var animSection = new Foldout { text = "Animation Settings", value = true };
            animSection.AddToClassList("section");
            
            AddBoundField(animSection, "minBlendUpdateDelta");
            AddBoundField(animSection, "controlAnimationsWithGPUI");
            var animatorWorkflowProp = serializedObject.FindProperty("animatorWorkflow");
            var animatorWorkflowField = new EnumField("Animator Workflow", (AnimatorWorkflowOverride)animatorWorkflowProp.enumValueIndex);
            animSection.Add(animatorWorkflowField);
            AddBoundField(animSection, "throttleAnimationUpdates");
            AddBoundField(animSection, "maxAnimationUpdatesPerFrame");
            AddBoundField(animSection, "priorityAnimationBonus");
            AddBoundField(animSection, "speedSmoothing");
            AddBoundField(animSection, "enableAirborneDebugLogs");
            
            container.Add(animSection);
            
            // Locomotion Section
            var locomotionSection = new Foldout { text = "Locomotion Animations", value = true };
            locomotionSection.AddToClassList("section");
            
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("idleClipOverride")) { bindingPath = "idleClipOverride" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("walkClipOverride")) { bindingPath = "walkClipOverride" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("runClipOverride")) { bindingPath = "runClipOverride" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("fallingClipOverride")) { bindingPath = "fallingClipOverride" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("landingClipOverride")) { bindingPath = "landingClipOverride" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("wallPlacementEnterClipOverride")) { bindingPath = "wallPlacementEnterClipOverride", label = "Wall Placement Enter Clip" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("wallPlacementLoopClipOverride")) { bindingPath = "wallPlacementLoopClipOverride", label = "Wall Placement Loop Clip" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("wallPlacementExitClipOverride")) { bindingPath = "wallPlacementExitClipOverride", label = "Wall Placement Exit Clip" });
            locomotionSection.Add(new PropertyField(serializedObject.FindProperty("rootMotionOverride")) { bindingPath = "rootMotionOverride", label = "Root Motion Override" });

            // Directional locomotion (2D blend / GPU 2D) lives with locomotion to avoid redundancy with combat directionals
            var directionalLocomotionToggle = new PropertyField(serializedObject.FindProperty("enableDirectionalLocomotionOverride"))
            {
                bindingPath = "enableDirectionalLocomotionOverride",
                label = "Enable Directional Locomotion"
            };
            locomotionSection.Add(directionalLocomotionToggle);

            var directionalLocomotionField = new PropertyField(serializedObject.FindProperty("directionalLocomotionAnimationsOverride"))
            {
                bindingPath = "directionalLocomotionAnimationsOverride",
                label = "Directional Locomotion Clips"
            };
            directionalLocomotionField.style.marginLeft = 8;

            var directionalThresholdField = new PropertyField(serializedObject.FindProperty("directionalLocomotionThresholdOverride"))
            {
                bindingPath = "directionalLocomotionThresholdOverride",
                label = "Directional Locomotion Threshold"
            };
            directionalThresholdField.style.marginLeft = 8;

            // Container to show/hide when directional locomotion is enabled
            var directionalContainer = new VisualElement();
            directionalContainer.style.marginLeft = 4;
            directionalContainer.Add(directionalLocomotionField);
            directionalContainer.Add(directionalThresholdField);
            locomotionSection.Add(directionalContainer);

            void UpdateDirectionalVisibility(bool enabled)
            {
                directionalContainer.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Initial state
            var dirProp = serializedObject.FindProperty("enableDirectionalLocomotionOverride");
            UpdateDirectionalVisibility(dirProp.boolValue);

            directionalLocomotionToggle.RegisterValueChangeCallback(evt =>
            {
                serializedObject.ApplyModifiedProperties();
                UpdateDirectionalVisibility(dirProp.boolValue);
            });
            locomotionSection.Bind(serializedObject);
            
            container.Add(locomotionSection);

            // ── Flight Animations Section ──────────────────────────────────────────────
            var flightSection = new Foldout { text = "Flight Animations", value = false };
            flightSection.AddToClassList("section");

            var flightInfo = new HelpBox(
                "Configure flight mode animations. Agents can enter flight mode via the FlyEffect skill action " +
                "or the public SetAgentFlying / SetAgentLanded methods on the controller.\n\n" +
                "ComputeAnimator: uses the clip fields below.\n" +
                "MecanimReader / BoneTracker: uses the parameter fields below.",
                HelpBoxMessageType.Info);
            flightSection.Add(flightInfo);

            // Flight height settings (shared across all workflow types)
            var flightHeightContainer = new VisualElement();
            var flightHeightHeader = new Label("Flight Height");
            flightHeightHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            flightHeightHeader.style.marginTop = 5;
            flightHeightContainer.Add(flightHeightHeader);
            flightHeightContainer.Add(new PropertyField(serializedObject.FindProperty("flightHeight")) { bindingPath = "flightHeight", label = "Flight Height" });
            flightHeightContainer.Add(new PropertyField(serializedObject.FindProperty("flightAscendSpeed")) { bindingPath = "flightAscendSpeed", label = "Ascend Speed" });
            flightHeightContainer.Add(new PropertyField(serializedObject.FindProperty("flightDescendSpeed")) { bindingPath = "flightDescendSpeed", label = "Descend Speed" });
            flightHeightContainer.Add(new PropertyField(serializedObject.FindProperty("flightBobAmplitude")) { bindingPath = "flightBobAmplitude", label = "Hover Bob Amplitude" });
            flightHeightContainer.Add(new PropertyField(serializedObject.FindProperty("flightBobFrequency")) { bindingPath = "flightBobFrequency", label = "Hover Bob Frequency" });
            flightSection.Add(flightHeightContainer);

            // Compute Animator Clips container (hidden when Mecanim)
            var flightClipsContainer = new VisualElement();
            var flightClipsHeader = new Label("Compute Animator Clips");
            flightClipsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            flightClipsHeader.style.marginTop = 5;
            flightClipsContainer.Add(flightClipsHeader);
            flightClipsContainer.Add(new PropertyField(serializedObject.FindProperty("flightTakeOffClipOverride")) { bindingPath = "flightTakeOffClipOverride", label = "Take-Off Clip" });
            flightClipsContainer.Add(new PropertyField(serializedObject.FindProperty("flightIdleClipOverride")) { bindingPath = "flightIdleClipOverride", label = "Flight Idle Clip" });
            flightClipsContainer.Add(new PropertyField(serializedObject.FindProperty("flightLoopClipOverride")) { bindingPath = "flightLoopClipOverride", label = "Flight Locomotion Clip" });
            flightClipsContainer.Add(new PropertyField(serializedObject.FindProperty("flightLandClipOverride")) { bindingPath = "flightLandClipOverride", label = "Landing Clip" });
            flightSection.Add(flightClipsContainer);

            // Mecanim / BoneTracker Parameters container (hidden when ComputeAnimator)
            var flightMecanimContainer = new VisualElement();
            var flightMecanimHeader = new Label("Animator Parameters");
            flightMecanimHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            flightMecanimHeader.style.marginTop = 5;
            flightMecanimContainer.Add(flightMecanimHeader);
            flightMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("flightBoolParameterOverride")) { bindingPath = "flightBoolParameterOverride", label = "Flight Bool Parameter" });
            flightMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("flightIdleStateNameOverride")) { bindingPath = "flightIdleStateNameOverride", label = "Flight Idle State Name" });
            flightMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("flightTakeOffTriggerParameterOverride")) { bindingPath = "flightTakeOffTriggerParameterOverride", label = "Take-Off Trigger Parameter" });
            flightMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("flightLandTriggerParameterOverride")) { bindingPath = "flightLandTriggerParameterOverride", label = "Landing Trigger Parameter" });
            flightSection.Add(flightMecanimContainer);

            flightSection.Bind(serializedObject);
            container.Add(flightSection);
            
            // Combat Animations Section
            var combatSection = new Foldout { text = "Combat Animations", value = true };
            combatSection.AddToClassList("section");
            
            combatSection.Add(new PropertyField(serializedObject.FindProperty("attackClipsOverride")) { bindingPath = "attackClipsOverride" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("meleeAttackClipsOverride")) { bindingPath = "meleeAttackClipsOverride" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("rangedAttackClipsOverride")) { bindingPath = "rangedAttackClipsOverride" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("blockClipsOverride")) { bindingPath = "blockClipsOverride" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("hitClipsOverride")) { bindingPath = "hitClipsOverride" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("pushImpulseClipsOverride")) { bindingPath = "pushImpulseClipsOverride", label = "Push Impulse Clips" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("pullImpulseClipsOverride")) { bindingPath = "pullImpulseClipsOverride", label = "Pull Impulse Clips" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("liftImpulseClipsOverride")) { bindingPath = "liftImpulseClipsOverride", label = "Lift Impulse Clips" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("liftGetUpClipOverride")) { bindingPath = "liftGetUpClipOverride", label = "Lift Get Up Clip" });
            combatSection.Add(new PropertyField(serializedObject.FindProperty("deathClipsOverride")) { bindingPath = "deathClipsOverride" });
            combatSection.Bind(serializedObject);
            
            container.Add(combatSection);
            
            // Climbing Animations Section
            var climbingSection = new Foldout { text = "Climbing Animations", value = false };
            climbingSection.AddToClassList("section");
            
            var climbingInfo = new HelpBox("Configure animations for wall/ladder climbing. Used when agents navigate climbable surfaces.", HelpBoxMessageType.Info);
            climbingSection.Add(climbingInfo);
            
            // Compute Animator Clips container
            var climbingClipsContainer = new VisualElement();
            var climbingClipsHeader = new Label("Compute Animator Clips");
            climbingClipsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            climbingClipsHeader.style.marginTop = 5;
            climbingClipsContainer.Add(climbingClipsHeader);
            
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbUpClip")) { bindingPath = "climbingAnimationConfig.climbUpClip", label = "Climb Up" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbDownClip")) { bindingPath = "climbingAnimationConfig.climbDownClip", label = "Climb Down" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbLeftClip")) { bindingPath = "climbingAnimationConfig.climbLeftClip", label = "Climb Left (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbRightClip")) { bindingPath = "climbingAnimationConfig.climbRightClip", label = "Climb Right (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.mountClip")) { bindingPath = "climbingAnimationConfig.mountClip", label = "Mount (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.dismountTopClip")) { bindingPath = "climbingAnimationConfig.dismountTopClip", label = "Dismount Top (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.dismountBottomClip")) { bindingPath = "climbingAnimationConfig.dismountBottomClip", label = "Dismount Bottom (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbIdleClip")) { bindingPath = "climbingAnimationConfig.climbIdleClip", label = "Climb Idle (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.fallClip")) { bindingPath = "climbingAnimationConfig.fallClip", label = "Fall (Edge Fall)" });
            
            // Transition Clips
            var transitionHeader = new Label("Transition Clips");
            transitionHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            transitionHeader.style.marginTop = 10;
            climbingClipsContainer.Add(transitionHeader);
            
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbUpTransitionClip")) { bindingPath = "climbingAnimationConfig.climbUpTransitionClip", label = "Climb Up → Locomotion (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbDownTransitionClip")) { bindingPath = "climbingAnimationConfig.climbDownTransitionClip", label = "Climb Down → Locomotion (Optional)" });
            climbingClipsContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.transitionClipDuration")) { bindingPath = "climbingAnimationConfig.transitionClipDuration", label = "Transition Duration (0=auto)" });
            
            climbingSection.Add(climbingClipsContainer);
            
            // Mecanim Parameters container
            var climbingMecanimContainer = new VisualElement();
            var mecanimHeader = new Label("Mecanim Parameters");
            mecanimHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            mecanimHeader.style.marginTop = 10;
            climbingMecanimContainer.Add(mecanimHeader);
            
            climbingMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbingBoolParameter")) { bindingPath = "climbingAnimationConfig.climbingBoolParameter", label = "Climbing Bool" });
            climbingMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbSpeedParameter")) { bindingPath = "climbingAnimationConfig.climbSpeedParameter", label = "Climb Speed Float" });
            climbingMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.climbHorizontalParameter")) { bindingPath = "climbingAnimationConfig.climbHorizontalParameter", label = "Climb Horizontal Float" });
            climbingMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.mountTriggerParameter")) { bindingPath = "climbingAnimationConfig.mountTriggerParameter", label = "Mount Trigger" });
            climbingMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.dismountTriggerParameter")) { bindingPath = "climbingAnimationConfig.dismountTriggerParameter", label = "Dismount Trigger" });
            climbingMecanimContainer.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.fallTriggerParameter")) { bindingPath = "climbingAnimationConfig.fallTriggerParameter", label = "Fall Trigger" });
            climbingSection.Add(climbingMecanimContainer);
            
            var timingHeader = new Label("Timing");
            timingHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            timingHeader.style.marginTop = 10;
            climbingSection.Add(timingHeader);
            
            climbingSection.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.mountDuration")) { bindingPath = "climbingAnimationConfig.mountDuration", label = "Mount Duration" });
            climbingSection.Add(new PropertyField(serializedObject.FindProperty("climbingAnimationConfig.dismountDuration")) { bindingPath = "climbingAnimationConfig.dismountDuration", label = "Dismount Duration" });
            climbingSection.Bind(serializedObject);
            
            container.Add(climbingSection);
            
            // Directional Animations Section
            var directionalSection = new Foldout { text = "Directional Animations", value = false };
            directionalSection.AddToClassList("section");
            
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("overrideDirectionalHitAnimations")) { bindingPath = "overrideDirectionalHitAnimations" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("hitAnimationsOverride")) { bindingPath = "hitAnimationsOverride" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("overrideDirectionalDeathAnimations")) { bindingPath = "overrideDirectionalDeathAnimations" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("deathAnimationsOverride")) { bindingPath = "deathAnimationsOverride" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("overrideDirectionalBlockAnimations")) { bindingPath = "overrideDirectionalBlockAnimations" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("blockAnimationsOverride")) { bindingPath = "blockAnimationsOverride" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("overrideDirectionalAttackAnimations")) { bindingPath = "overrideDirectionalAttackAnimations" });
            directionalSection.Add(new PropertyField(serializedObject.FindProperty("attackAnimationsOverride")) { bindingPath = "attackAnimationsOverride" });
            directionalSection.Bind(serializedObject);
            
            container.Add(directionalSection);
            
            // Root Motion Section
            var rootMotionSection = new Foldout { text = "Root Motion & Parameters", value = false };
            rootMotionSection.AddToClassList("section");
            
            rootMotionSection.Add(new PropertyField(serializedObject.FindProperty("enableRagdollOnDeath")) { bindingPath = "enableRagdollOnDeath" });
            rootMotionSection.Add(new PropertyField(serializedObject.FindProperty("rootMotionOverride")) { bindingPath = "rootMotionOverride", label = "Root Motion Override" });
            rootMotionSection.Add(new PropertyField(serializedObject.FindProperty("locomotionStateNameOverride")) { bindingPath = "locomotionStateNameOverride" });
            rootMotionSection.Bind(serializedObject);
            
            container.Add(rootMotionSection);
            
            // Animator Parameters Section
            var parametersSection = new Foldout { text = "Animator Parameters", value = false };
            parametersSection.AddToClassList("section");
            
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("attackTriggerParametersOverride")) { bindingPath = "attackTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("meleeAttackTriggerParametersOverride")) { bindingPath = "meleeAttackTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("rangedAttackTriggerParametersOverride")) { bindingPath = "rangedAttackTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("defendBoolParameterOverride")) { bindingPath = "defendBoolParameterOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("wallPlacementBoolParameterOverride")) { bindingPath = "wallPlacementBoolParameterOverride", label = "Wall Placement Bool Parameter" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("defendBoolParametersOverride")) { bindingPath = "defendBoolParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("hitTriggerParametersOverride")) { bindingPath = "hitTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("deathTriggerParametersOverride")) { bindingPath = "deathTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("directionalHitTriggerParametersOverride")) { bindingPath = "directionalHitTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("directionalBlockTriggerParametersOverride")) { bindingPath = "directionalBlockTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("directionalAttackTriggerParametersOverride")) { bindingPath = "directionalAttackTriggerParametersOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("locomotionSpeedFloatParameterOverride")) { bindingPath = "locomotionSpeedFloatParameterOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("locomotionDirectionXFloatParameterOverride")) { bindingPath = "locomotionDirectionXFloatParameterOverride" });
            parametersSection.Add(new PropertyField(serializedObject.FindProperty("locomotionDirectionYFloatParameterOverride")) { bindingPath = "locomotionDirectionYFloatParameterOverride" });
            parametersSection.Bind(serializedObject);
            
            container.Add(parametersSection);

            // Toggle clip vs Mecanim parameter visibility based on workflow
            void UpdateAnimationVisibility()
            {
                bool isMecanimWorkflow = animatorWorkflowProp.enumValueIndex == (int)AnimatorWorkflowOverride.MecanimReader ||
                                         animatorWorkflowProp.enumValueIndex == (int)AnimatorWorkflowOverride.BoneTracker;

                var clipDisplay = isMecanimWorkflow ? DisplayStyle.None : DisplayStyle.Flex;
                var mecanimDisplay = isMecanimWorkflow ? DisplayStyle.Flex : DisplayStyle.None;
                
                locomotionSection.style.display = clipDisplay;
                combatSection.style.display = clipDisplay;
                directionalSection.style.display = clipDisplay;
                
                // Climbing section containers
                climbingClipsContainer.style.display = clipDisplay;
                climbingMecanimContainer.style.display = mecanimDisplay;

                // Flight section containers
                flightClipsContainer.style.display = clipDisplay;
                flightMecanimContainer.style.display = mecanimDisplay;

                parametersSection.style.display = mecanimDisplay;
            }

            UpdateAnimationVisibility();
            animatorWorkflowField.RegisterValueChangedCallback(evt =>
            {
                var newValue = (AnimatorWorkflowOverride)evt.newValue;
                if (animatorWorkflowProp.enumValueIndex != (int)newValue)
                {
                    animatorWorkflowProp.enumValueIndex = (int)newValue;
                    serializedObject.ApplyModifiedProperties();
                }
                UpdateAnimationVisibility();
            });
            
            return container;
        }
        
        #endregion
        
        #region Skills Tab
        
        private VisualElement CreateSkillsTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Get skill system config property
            var skillConfigProp = serializedObject.FindProperty("skillSystemConfig");
            if (skillConfigProp == null)
            {
                var errorBox = new HelpBox("Skill system config not found. Please update EnemyCrowd.cs.", HelpBoxMessageType.Error);
                container.Add(errorBox);
                return container;
            }
            
            // Introduction
            var introBox = new HelpBox(
                "The Skill System allows agents to use special abilities beyond basic attacks.\n\n" +
                "Setup:\n" +
                "• Create AgentSkill assets via: Right-click > Create > Enemy Masses > Skills > Agent Skill\n" +
                "• Add skills to the Skill Slots list below\n" +
                "• Each skill can have: damage, healing, buffs, debuffs, summons, movement abilities\n" +
                "• Cooldowns can be per-agent, per-enemycrowd, or per-faction\n\n" +
                "Example:\n" +
                "  Slot 0: Sword Strike (basic attack, 1.5s cooldown)\n" +
                "  Slot 1: Shield Bash (stun, 8s cooldown)\n" +
                "  Slot 2: Rally Cry (buff allies, 20s cooldown, shared cooldown)",
                HelpBoxMessageType.Info);
            container.Add(introBox);
            
            // Enable Section
            var enableSection = new Foldout { text = "═══ SKILL SYSTEM ═══", value = true };
            enableSection.AddToClassList("section");
            
            var enableSkillsProp = skillConfigProp.FindPropertyRelative("enableSkillSystem");
            var enableField = new PropertyField(enableSkillsProp, "Enable Skill System");
            enableField.Bind(serializedObject);
            enableField.tooltip = "Enable or disable the skill system for this faction.";
            enableSection.Add(enableField);
            
            var controlModeProp = skillConfigProp.FindPropertyRelative("controlMode");
            var controlModeField = new PropertyField(controlModeProp, "Control Mode");
            controlModeField.Bind(serializedObject);
            controlModeField.tooltip = "AIControlled: AI decides when to use skills (default for enemies).\n" +
                                       "ManualOnly: Skills only trigger via API (for player units).\n" +
                                       "Hybrid: AI by default, but per-agent manual override possible.";
            enableSection.Add(controlModeField);
            
            var defaultCooldownModeProp = skillConfigProp.FindPropertyRelative("defaultCooldownMode");
            var cooldownModeField = new PropertyField(defaultCooldownModeProp, "Default Cooldown Mode");
            cooldownModeField.Bind(serializedObject);
            cooldownModeField.tooltip = "PerAgent: Each agent tracks cooldowns independently.\n" +
                                        "• Most flexible and granular.\n" +
                                        "• Highest CPU cost (each agent maintains its own cooldown state).\n\n" +
                                        "PerEnemyCrowd: All agents in the same EnemyCrowd share cooldowns.\n" +
                                        "• Good balance of control vs cost.\n" +
                                        "• Lower CPU cost than PerAgent.\n\n" +
                                        "PerFaction: All agents share cooldowns.\n" +
                                        "• Lowest CPU cost.\n" +
                                        "• Best for powerful abilities you don't want mass‑spam.";
            enableSection.Add(cooldownModeField);
            
            container.Add(enableSection);
            
            // Auto Attack Section
            var autoAttackSection = new Foldout { text = "Auto Attack", value = true };
            autoAttackSection.AddToClassList("section");
            
            var useAutoAttackProp = skillConfigProp.FindPropertyRelative("useAutoAttack");
            var autoAttackField = new PropertyField(useAutoAttackProp, "Use Auto Attack");
            autoAttackField.Bind(serializedObject);
            autoAttackField.tooltip = "If enabled, agents will use Combat Template's basic attack when no skills are available.";
            autoAttackSection.Add(autoAttackField);
            
            container.Add(autoAttackSection);
            
            // AI Settings Section
            var aiSection = new Foldout { text = "AI Behavior", value = true };
            aiSection.AddToClassList("section");
            
            var aiHelp = new HelpBox(
                "These settings control how the AI decides when to use skills. " +
                "Tuning these prevents skill spam in large armies.",
                HelpBoxMessageType.Info);
            aiSection.Add(aiHelp);
            
            var decisionIntervalProp = skillConfigProp.FindPropertyRelative("skillDecisionInterval");
            var decisionIntervalField = new PropertyField(decisionIntervalProp, "Decision Interval");
            decisionIntervalField.Bind(serializedObject);
            decisionIntervalField.tooltip = "How often (in seconds) agents evaluate which skill to use. " +
                                            "Lower = more responsive but higher CPU. Recommended: 0.5-1.0s for large armies.";
            aiSection.Add(decisionIntervalField);
            
            var globalCooldownProp = skillConfigProp.FindPropertyRelative("globalCooldown");
            var globalCooldownField = new PropertyField(globalCooldownProp, "Global Cooldown");
            globalCooldownField.Bind(serializedObject);
            globalCooldownField.tooltip = "Minimum time between ANY skill usage per agent. " +
                                          "Prevents back-to-back skill spam. Recommended: 0.5-1.5s.";
            aiSection.Add(globalCooldownField);
            
            var skillUseChanceProp = skillConfigProp.FindPropertyRelative("skillUseChance");
            var skillUseChanceField = new PropertyField(skillUseChanceProp, "Skill Use Chance");
            skillUseChanceField.Bind(serializedObject);
            skillUseChanceField.tooltip = "Probability that an agent will try to use a skill each evaluation (0-1). " +
                                          "1.0 = always try. Lower values reduce skill spam for large armies.";
            aiSection.Add(skillUseChanceField);
            
            var maxSkillsPerSecondProp = skillConfigProp.FindPropertyRelative("maxSkillsPerSecond");
            var maxSkillsPerSecondField = new PropertyField(maxSkillsPerSecondProp, "Max Skills/Second");
            maxSkillsPerSecondField.Bind(serializedObject);
            maxSkillsPerSecondField.tooltip = "Faction-wide rate limit for skills per second. " +
                                              "0 = unlimited. Use 5-20 for armies of 100+ agents.";
            aiSection.Add(maxSkillsPerSecondField);

            
            var timingVariationProp = skillConfigProp.FindPropertyRelative("skillTimingVariation");
            var timingVariationField = new PropertyField(timingVariationProp, "Timing Variation");
            timingVariationField.Bind(serializedObject);
            timingVariationField.tooltip = "Random variation in skill timing (0-1). " +
                                           "0 = synchronized. 0.3 = natural variation.";
            aiSection.Add(timingVariationField);
            
            var preferLowerCooldownsProp = skillConfigProp.FindPropertyRelative("preferLowerCooldowns");
            var preferLowerCooldownsField = new PropertyField(preferLowerCooldownsProp, "Prefer Lower Cooldowns");
            preferLowerCooldownsField.Bind(serializedObject);
            preferLowerCooldownsField.tooltip = "If enabled, AI prefers skills that will be ready again soon.";
            aiSection.Add(preferLowerCooldownsField);
            
            var conservativeUsageProp = skillConfigProp.FindPropertyRelative("conservativeSkillUsage");
            var conservativeUsageField = new PropertyField(conservativeUsageProp, "Conservative Usage");
            conservativeUsageField.Bind(serializedObject);
            conservativeUsageField.tooltip = "If enabled, agents wait for optimal conditions before using skills.";
            aiSection.Add(conservativeUsageField);
            
            container.Add(aiSection);
            
            // Skill Slots Section
            var slotsSection = new Foldout { text = "Skill Slots", value = true };
            slotsSection.AddToClassList("section");
            
            var slotsHelp = new HelpBox(
                "Add AgentSkill ScriptableObjects to the slots below. " +
                "Skills are evaluated by priority - higher priority skills are preferred when their conditions are met. " +
                "Create new skills via: Right-click > Create > Enemy Masses > Skills > Agent Skill",
                HelpBoxMessageType.Info);
            slotsSection.Add(slotsHelp);
            
            var skillSlotsProp = skillConfigProp.FindPropertyRelative("skillSlots");
            var skillSlotsField = new PropertyField(skillSlotsProp, "Skill Slots");
            skillSlotsField.Bind(serializedObject);
            slotsSection.Add(skillSlotsField);
            
            // Fix defaults when Unity's built-in list "+" adds a new element
            // (it copies the last element or zero-initializes, giving 0.1 from the Range attribute)
            int lastKnownSlotCount = skillSlotsProp.arraySize;
            skillSlotsField.TrackPropertyValue(skillSlotsProp, prop =>
            {
                int currentCount = prop.arraySize;
                if (currentCount > lastKnownSlotCount)
                {
                    // New element(s) were added — ensure multipliers default to 1
                    for (int si = lastKnownSlotCount; si < currentCount; si++)
                    {
                        var newSlot = prop.GetArrayElementAtIndex(si);
                        var cdMul = newSlot.FindPropertyRelative("_cooldownMultiplier");
                        var dmgMul = newSlot.FindPropertyRelative("_damageMultiplier");
                        if (cdMul != null && cdMul.floatValue < 0.2f) cdMul.floatValue = 1f;
                        if (dmgMul != null && dmgMul.floatValue < 0.2f) dmgMul.floatValue = 1f;
                    }
                    prop.serializedObject.ApplyModifiedProperties();
                }
                lastKnownSlotCount = currentCount;
            });
            
            // Add quick-add buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginTop = 8;
            
            var addSlotButton = new Button(() =>
            {
                int newIndex = skillSlotsProp.arraySize;
                skillSlotsProp.arraySize++;
                var newSlot = skillSlotsProp.GetArrayElementAtIndex(newIndex);
                newSlot.FindPropertyRelative("_cooldownMultiplier").floatValue = 1f;
                newSlot.FindPropertyRelative("_damageMultiplier").floatValue = 1f;
                newSlot.FindPropertyRelative("enabled").boolValue = true;
                serializedObject.ApplyModifiedProperties();
            });
            addSlotButton.text = "+ Add Skill Slot";
            addSlotButton.style.flexGrow = 1;
            buttonContainer.Add(addSlotButton);
            
            var clearSlotsButton = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Clear All Skill Slots", 
                    "Are you sure you want to remove all skill slots?", "Yes", "Cancel"))
                {
                    skillSlotsProp.arraySize = 0;
                    serializedObject.ApplyModifiedProperties();
                }
            });
            clearSlotsButton.text = "Clear All";
            clearSlotsButton.style.width = 80;
            buttonContainer.Add(clearSlotsButton);
            
            slotsSection.Add(buttonContainer);
            
            container.Add(slotsSection);
            
            // Tips Section
            var tipsSection = new Foldout { text = "Tips & Best Practices", value = false };
            tipsSection.AddToClassList("section");
            
            var tip1 = new HelpBox(
                "💡 Warcraft-Style Setup: Create 4 melee skills, 3 ranged skills, and 3 heal skills. " +
                "Use SkillUseCondition flags to control when each is used (e.g., heals only when SelfHealthLow).",
                HelpBoxMessageType.None);
            tipsSection.Add(tip1);
            
            var tip2 = new HelpBox(
                "💡 Shared Cooldowns: Use PerEnemyCrowd for crowd-specific abilities, or PerFaction " +
                "for powerful abilities you don't want 100 agents all casting at once (e.g., War Cry, Mass Heal).",
                HelpBoxMessageType.None);
            tipsSection.Add(tip2);
            
            var tip3 = new HelpBox(
                "💡 Skill Priority: Higher priority skills are checked first. " +
                "Set emergency heals to high priority so they trigger before regular attacks.",
                HelpBoxMessageType.None);
            tipsSection.Add(tip3);
            
            var tip4 = new HelpBox(
                "💡 VFX Budget: Set MaxActiveVFX on skills to prevent performance issues " +
                "when many agents use the same skill simultaneously.",
                HelpBoxMessageType.None);
            tipsSection.Add(tip4);
            
            container.Add(tipsSection);
            
            return container;
        }
        
        #endregion
        
        #region Civil Tab
        
        private VisualElement CreateCivilTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Get civil settings property
            var civilSettingsProp = serializedObject.FindProperty("civilSettings");
            if (civilSettingsProp == null)
            {
                var errorBox = new HelpBox("Civil settings not found. Please update EnemyCrowd.cs.", HelpBoxMessageType.Error);
                container.Add(errorBox);
                return container;
            }
            
            // Introduction
            var introBox = new HelpBox(
                "Civil NPC Mode enables autonomous waypoint navigation for non-combat agents. " +
                "Civil agents can walk paths, explore zones, perform activities with animations and props, " +
                "and spawn/despawn with fade effects. Perfect for citizens, workers, and ambient NPCs.",
                HelpBoxMessageType.Info);
            container.Add(introBox);
            
            // Enable Civil Mode Section
            var enableSection = new Foldout { text = "Civil Mode", value = true };
            enableSection.AddToClassList("section");
            
            var enableCivilProp = civilSettingsProp.FindPropertyRelative("enableCivilMode");
            var enableField = new PropertyField(enableCivilProp, "Enable Civil Mode");
            enableField.Bind(serializedObject);
            enableSection.Add(enableField);
            
            container.Add(enableSection);
            
            // Movement Section
            var movementSection = new Foldout { text = "Movement Settings", value = true };
            movementSection.AddToClassList("section");
            
            var movementStyleProp = civilSettingsProp.FindPropertyRelative("movementStyle");
            var movementStyleField = new PropertyField(movementStyleProp, "Movement Style");
            movementStyleField.Bind(serializedObject);
            movementSection.Add(movementStyleField);
            
            var walkSpeedProp = civilSettingsProp.FindPropertyRelative("walkSpeedMultiplier");
            var walkSpeedField = new PropertyField(walkSpeedProp, "Walk Speed Multiplier");
            walkSpeedField.Bind(serializedObject);
            movementSection.Add(walkSpeedField);
            
            var strollSpeedProp = civilSettingsProp.FindPropertyRelative("strollSpeedMultiplier");
            var strollSpeedField = new PropertyField(strollSpeedProp, "Stroll Speed Multiplier");
            strollSpeedField.Bind(serializedObject);
            movementSection.Add(strollSpeedField);
            
            var hurriedSpeedProp = civilSettingsProp.FindPropertyRelative("hurriedSpeedMultiplier");
            var hurriedSpeedField = new PropertyField(hurriedSpeedProp, "Hurried Speed Multiplier");
            hurriedSpeedField.Bind(serializedObject);
            movementSection.Add(hurriedSpeedField);
            
            container.Add(movementSection);
            
            // Navigation Section
            var navSection = new Foldout { text = "Navigation Behavior", value = true };
            navSection.AddToClassList("section");
            
            // Navigation Mode
            var navigationModeProp = civilSettingsProp.FindPropertyRelative("navigationMode");
            var navigationModeField = new PropertyField(navigationModeProp, "Navigation Mode");
            navigationModeField.Bind(serializedObject);
            navigationModeField.tooltip = "FollowConnections: Uses waypoint graph. FreeRoam: Ignores connections, walks to any waypoint via NavMesh.";
            navSection.Add(navigationModeField);
            
            var navModeHelp = new HelpBox(
                "FreeRoam mode lets agents walk to ANY waypoint using NavMesh pathfinding. " +
                "No connections between waypoints needed - perfect for open-world environments.",
                HelpBoxMessageType.Info);
            navSection.Add(navModeHelp);
            
            var selectionModeProp = civilSettingsProp.FindPropertyRelative("selectionMode");
            var selectionModeField = new PropertyField(selectionModeProp, "Waypoint Selection");
            selectionModeField.Bind(serializedObject);
            selectionModeField.tooltip = "How agents choose the next waypoint (used in FollowConnections mode)";
            navSection.Add(selectionModeField);
            
            var pathWaitTimeProp = civilSettingsProp.FindPropertyRelative("pathWaitTime");
            var pathWaitTimeField = new PropertyField(pathWaitTimeProp, "Path Wait Time (min-max)");
            pathWaitTimeField.Bind(serializedObject);
            navSection.Add(pathWaitTimeField);
            
            var exploreTimeProp = civilSettingsProp.FindPropertyRelative("exploreTime");
            var exploreTimeField = new PropertyField(exploreTimeProp, "Explore Time (min-max)");
            exploreTimeField.Bind(serializedObject);
            navSection.Add(exploreTimeField);
            
            var retargetIntervalProp = civilSettingsProp.FindPropertyRelative("exploreRetargetInterval");
            var retargetIntervalField = new PropertyField(retargetIntervalProp, "Explore Retarget Interval");
            retargetIntervalField.Bind(serializedObject);
            navSection.Add(retargetIntervalField);
            
            container.Add(navSection);
            
            // Free Roam Settings Section
            var freeRoamSection = new Foldout { text = "Free Roam Settings", value = true };
            freeRoamSection.AddToClassList("section");
            
            var freeRoamHelp = new HelpBox(
                "These settings only apply when Navigation Mode is set to FreeRoam. " +
                "Agents will pick waypoints randomly and let the NavMesh handle pathfinding.",
                HelpBoxMessageType.Info);
            freeRoamSection.Add(freeRoamHelp);
            
            var freeRoamMaxDistProp = civilSettingsProp.FindPropertyRelative("freeRoamMaxDistance");
            var freeRoamMaxDistField = new PropertyField(freeRoamMaxDistProp, "Max Waypoint Distance");
            freeRoamMaxDistField.Bind(serializedObject);
            freeRoamMaxDistField.tooltip = "Maximum distance to search for next waypoint. 0 = unlimited.";
            freeRoamSection.Add(freeRoamMaxDistField);
            
            var freeRoamPreferCloserProp = civilSettingsProp.FindPropertyRelative("freeRoamPreferCloser");
            var freeRoamPreferCloserField = new PropertyField(freeRoamPreferCloserProp, "Prefer Closer Waypoints");
            freeRoamPreferCloserField.Bind(serializedObject);
            freeRoamPreferCloserField.tooltip = "Bias selection toward closer waypoints for more local movement.";
            freeRoamSection.Add(freeRoamPreferCloserField);
            
            var freeRoamMemoryProp = civilSettingsProp.FindPropertyRelative("freeRoamRecentMemory");
            var freeRoamMemoryField = new PropertyField(freeRoamMemoryProp, "Recent Memory");
            freeRoamMemoryField.Bind(serializedObject);
            freeRoamMemoryField.tooltip = "Number of recent waypoints to avoid revisiting (0-10).";
            freeRoamSection.Add(freeRoamMemoryField);
            
            var freeRoamDespawnBiasProp = civilSettingsProp.FindPropertyRelative("freeRoamDespawnBias");
            var freeRoamDespawnBiasField = new PropertyField(freeRoamDespawnBiasProp, "Despawn Bias");
            freeRoamDespawnBiasField.Bind(serializedObject);
            freeRoamDespawnBiasField.tooltip = "Chance to pick a Despawn waypoint when available (0-1).";
            freeRoamSection.Add(freeRoamDespawnBiasField);
            
            container.Add(freeRoamSection);
            
            // Activity Section
            var activitySection = new Foldout { text = "Activity Settings", value = true };
            activitySection.AddToClassList("section");
            
            var activityChanceProp = civilSettingsProp.FindPropertyRelative("activityChance");
            var activityChanceField = new PropertyField(activityChanceProp, "Activity Chance");
            activityChanceField.Bind(serializedObject);
            activitySection.Add(activityChanceField);
            
            var activityCooldownProp = civilSettingsProp.FindPropertyRelative("activityCooldown");
            var activityCooldownField = new PropertyField(activityCooldownProp, "Activity Cooldown");
            activityCooldownField.Bind(serializedObject);
            activitySection.Add(activityCooldownField);
            
            var activityHelp = new HelpBox(
                "Activities require the BoneTracker workflow for prop attachments. " +
                "The system will automatically switch workflows when entering/exiting activities.",
                HelpBoxMessageType.Info);
            activitySection.Add(activityHelp);
            
            container.Add(activitySection);
            
            // Spawn/Despawn Section
            var lifecycleSection = new Foldout { text = "Spawn & Despawn", value = true };
            lifecycleSection.AddToClassList("section");
            
            var despawnChanceProp = civilSettingsProp.FindPropertyRelative("despawnChance");
            var despawnChanceField = new PropertyField(despawnChanceProp, "Despawn Chance (per waypoint)");
            despawnChanceField.Bind(serializedObject);
            lifecycleSection.Add(despawnChanceField);
            
            var minLifetimeProp = civilSettingsProp.FindPropertyRelative("minLifetimeBeforeDespawn");
            var minLifetimeField = new PropertyField(minLifetimeProp, "Min Lifetime Before Despawn");
            minLifetimeField.Bind(serializedObject);
            lifecycleSection.Add(minLifetimeField);
            
            var fadeDurationProp = civilSettingsProp.FindPropertyRelative("fadeTransitionDuration");
            var fadeDurationField = new PropertyField(fadeDurationProp, "Fade Transition Duration");
            fadeDurationField.Bind(serializedObject);
            lifecycleSection.Add(fadeDurationField);
            
            container.Add(lifecycleSection);
            
            // Performance Section
            var perfSection = new Foldout { text = "Performance", value = false };
            perfSection.AddToClassList("section");
            
            var maxDecisionsProp = civilSettingsProp.FindPropertyRelative("maxDecisionsPerFrame");
            var maxDecisionsField = new PropertyField(maxDecisionsProp, "Max Decisions Per Frame");
            maxDecisionsField.Bind(serializedObject);
            perfSection.Add(maxDecisionsField);
            
            var maxActivityUpdatesProp = civilSettingsProp.FindPropertyRelative("maxActivityUpdatesPerFrame");
            var maxActivityUpdatesField = new PropertyField(maxActivityUpdatesProp, "Max Activity Updates Per Frame");
            maxActivityUpdatesField.Bind(serializedObject);
            perfSection.Add(maxActivityUpdatesField);
            
            var distantIntervalProp = civilSettingsProp.FindPropertyRelative("distantUpdateInterval");
            var distantIntervalField = new PropertyField(distantIntervalProp, "Distant Update Interval");
            distantIntervalField.Bind(serializedObject);
            perfSection.Add(distantIntervalField);
            
            container.Add(perfSection);
            
            // Path Divergence Section
            var divergenceSection = new Foldout { text = "Path Divergence (Natural Movement)", value = true };
            divergenceSection.AddToClassList("section");
            
            var divergenceHelp = new HelpBox(
                "Path divergence adds natural variation to agent movement. Higher values create more organic, " +
                "wandering paths. Use low values for purposeful agents (workers) and high values for casual NPCs.",
                HelpBoxMessageType.Info);
            divergenceSection.Add(divergenceHelp);
            
            var pathDivergenceProp = civilSettingsProp.FindPropertyRelative("pathDivergence");
            var pathDivergenceField = new PropertyField(pathDivergenceProp, "Path Divergence");
            pathDivergenceField.Bind(serializedObject);
            pathDivergenceField.tooltip = "0 = direct paths, 1 = highly organic/wandering";
            divergenceSection.Add(pathDivergenceField);
            
            var divergenceFreqProp = civilSettingsProp.FindPropertyRelative("divergenceFrequency");
            var divergenceFreqField = new PropertyField(divergenceFreqProp, "Divergence Frequency");
            divergenceFreqField.Bind(serializedObject);
            divergenceFreqField.tooltip = "How often path variation changes. Lower = smoother curves";
            divergenceSection.Add(divergenceFreqField);
            
            var maxDivergenceOffsetProp = civilSettingsProp.FindPropertyRelative("maxDivergenceOffset");
            var maxDivergenceOffsetField = new PropertyField(maxDivergenceOffsetProp, "Max Divergence Offset (m)");
            maxDivergenceOffsetField.Bind(serializedObject);
            divergenceSection.Add(maxDivergenceOffsetField);
            
            var divergenceSmoothnessProp = civilSettingsProp.FindPropertyRelative("divergenceSmoothness");
            var divergenceSmoothnessField = new PropertyField(divergenceSmoothnessProp, "Divergence Smoothness");
            divergenceSmoothnessField.Bind(serializedObject);
            divergenceSection.Add(divergenceSmoothnessField);
            
            var enableLookAroundProp = civilSettingsProp.FindPropertyRelative("enableLookAroundStops");
            var enableLookAroundField = new PropertyField(enableLookAroundProp, "Enable Look-Around Stops");
            enableLookAroundField.Bind(serializedObject);
            divergenceSection.Add(enableLookAroundField);
            
            var lookAroundChanceProp = civilSettingsProp.FindPropertyRelative("lookAroundChance");
            var lookAroundChanceField = new PropertyField(lookAroundChanceProp, "Look-Around Chance");
            lookAroundChanceField.Bind(serializedObject);
            divergenceSection.Add(lookAroundChanceField);
            
            var lookAroundDurationProp = civilSettingsProp.FindPropertyRelative("lookAroundDuration");
            var lookAroundDurationField = new PropertyField(lookAroundDurationProp, "Look-Around Duration (min-max)");
            lookAroundDurationField.Bind(serializedObject);
            divergenceSection.Add(lookAroundDurationField);
            
            container.Add(divergenceSection);
            
            // Corner Smoothing Section
            var cornerSection = new Foldout { text = "Corner Smoothing", value = true };
            cornerSection.AddToClassList("section");
            
            var cornerHelp = new HelpBox(
                "Corner smoothing creates natural arc-based turns at sharp waypoint corners instead of robotic " +
                "pivot turns. Agents will smoothly curve around corners using bezier interpolation.",
                HelpBoxMessageType.Info);
            cornerSection.Add(cornerHelp);
            
            var enableCornerSmoothingProp = civilSettingsProp.FindPropertyRelative("enableCornerSmoothing");
            var enableCornerSmoothingField = new PropertyField(enableCornerSmoothingProp, "Enable Corner Smoothing");
            enableCornerSmoothingField.Bind(serializedObject);
            enableCornerSmoothingField.tooltip = "Enable smooth arc-based turns at sharp corners";
            cornerSection.Add(enableCornerSmoothingField);
            
            var cornerAngleThresholdProp = civilSettingsProp.FindPropertyRelative("cornerSmoothingAngleThreshold");
            var cornerAngleThresholdField = new PropertyField(cornerAngleThresholdProp, "Angle Threshold (°)");
            cornerAngleThresholdField.Bind(serializedObject);
            cornerAngleThresholdField.tooltip = "Minimum turning angle to trigger smoothing (45° = moderate turns, 90° = only sharp corners)";
            cornerSection.Add(cornerAngleThresholdField);
            
            var cornerRadiusProp = civilSettingsProp.FindPropertyRelative("cornerSmoothingRadius");
            var cornerRadiusField = new PropertyField(cornerRadiusProp, "Arc Radius (m)");
            cornerRadiusField.Bind(serializedObject);
            cornerRadiusField.tooltip = "Radius of the smoothing arc. Larger = wider turns";
            cornerSection.Add(cornerRadiusField);
            
            var cornerSegmentsProp = civilSettingsProp.FindPropertyRelative("cornerSmoothingSegments");
            var cornerSegmentsField = new PropertyField(cornerSegmentsProp, "Arc Segments");
            cornerSegmentsField.Bind(serializedObject);
            cornerSegmentsField.tooltip = "Number of intermediate points in the arc. More = smoother but more processing";
            cornerSection.Add(cornerSegmentsField);
            
            var cornerApproachProp = civilSettingsProp.FindPropertyRelative("cornerApproachFactor");
            var cornerApproachField = new PropertyField(cornerApproachProp, "Approach Factor");
            cornerApproachField.Bind(serializedObject);
            cornerApproachField.tooltip = "How early to start the arc (0.1 = late start, 0.5 = early start)";
            cornerSection.Add(cornerApproachField);
            
            container.Add(cornerSection);
            
            // Crowd Flow & Congestion Section
            var crowdFlowSection = new Foldout { text = "Crowd Flow & Congestion", value = true };
            crowdFlowSection.AddToClassList("section");
            
            var crowdFlowHelp = new HelpBox(
                "Crowd flow prevents traffic jams and creates natural pedestrian behavior. Agents dynamically " +
                "adjust speed based on density, avoid crowded areas, and form natural lanes in bidirectional traffic.",
                HelpBoxMessageType.Info);
            crowdFlowSection.Add(crowdFlowHelp);
            
            var enableCrowdFlowProp = civilSettingsProp.FindPropertyRelative("enableCrowdFlow");
            var enableCrowdFlowField = new PropertyField(enableCrowdFlowProp, "Enable Crowd Flow");
            enableCrowdFlowField.Bind(serializedObject);
            enableCrowdFlowField.tooltip = "Enable dynamic crowd flow management to prevent congestion";
            crowdFlowSection.Add(enableCrowdFlowField);
            
            var densityRadiusProp = civilSettingsProp.FindPropertyRelative("densityCheckRadius");
            var densityRadiusField = new PropertyField(densityRadiusProp, "Density Check Radius (m)");
            densityRadiusField.Bind(serializedObject);
            densityRadiusField.tooltip = "Radius to check for nearby agents when calculating local density";
            crowdFlowSection.Add(densityRadiusField);
            
            var congestionThresholdProp = civilSettingsProp.FindPropertyRelative("congestionThreshold");
            var congestionThresholdField = new PropertyField(congestionThresholdProp, "Congestion Threshold");
            congestionThresholdField.Bind(serializedObject);
            congestionThresholdField.tooltip = "Number of nearby agents that triggers congestion behavior";
            crowdFlowSection.Add(congestionThresholdField);
            
            var minSpeedMultProp = civilSettingsProp.FindPropertyRelative("minCongestionSpeedMult");
            var minSpeedMultField = new PropertyField(minSpeedMultProp, "Min Congestion Speed");
            minSpeedMultField.Bind(serializedObject);
            minSpeedMultField.tooltip = "Minimum speed multiplier when heavily congested (0.3 = 30% of normal)";
            crowdFlowSection.Add(minSpeedMultField);
            
            var avoidanceStrengthProp = civilSettingsProp.FindPropertyRelative("crowdAvoidanceStrength");
            var avoidanceStrengthField = new PropertyField(avoidanceStrengthProp, "Avoidance Strength");
            avoidanceStrengthField.Bind(serializedObject);
            avoidanceStrengthField.tooltip = "How strongly agents steer away from crowded areas";
            crowdFlowSection.Add(avoidanceStrengthField);
            
            var enableLanesProp = civilSettingsProp.FindPropertyRelative("enableLaneFormation");
            var enableLanesField = new PropertyField(enableLanesProp, "Enable Lane Formation");
            enableLanesField.Bind(serializedObject);
            enableLanesField.tooltip = "Agents naturally form lanes when walking in opposite directions";
            crowdFlowSection.Add(enableLanesField);
            
            var lanePrefProp = civilSettingsProp.FindPropertyRelative("lanePreference");
            var lanePrefField = new PropertyField(lanePrefProp, "Lane Preference");
            lanePrefField.Bind(serializedObject);
            lanePrefField.tooltip = "Which side agents prefer to pass on (Right = US/EU, Left = UK/JP)";
            crowdFlowSection.Add(lanePrefField);
            
            var enableReroutingProp = civilSettingsProp.FindPropertyRelative("enableDynamicRerouting");
            var enableReroutingField = new PropertyField(enableReroutingProp, "Enable Dynamic Rerouting");
            enableReroutingField.Bind(serializedObject);
            enableReroutingField.tooltip = "Allow agents to find alternate paths when congested (requires LeastCrowded waypoint selection)";
            crowdFlowSection.Add(enableReroutingField);
            
            container.Add(crowdFlowSection);
            
            // Social Interactions Section
            var socialSection = new Foldout { text = "Social Interactions", value = true };
            socialSection.AddToClassList("section");
            
            var socialHelp = new HelpBox(
                "Social interactions allow civil agents to engage in realistic social behaviors like waving, " +
                "talking, handshakes, and hugs. Complex interactions require BoneTracker workflow for IK/motion warping.",
                HelpBoxMessageType.Info);
            socialSection.Add(socialHelp);
            
            var socialSettingsProp = civilSettingsProp.FindPropertyRelative("socialSettings");
            if (socialSettingsProp != null)
            {
                var enableSocialProp = socialSettingsProp.FindPropertyRelative("enableSocialInteractions");
                var enableSocialField = new PropertyField(enableSocialProp, "Enable Social Interactions");
                enableSocialField.Bind(serializedObject);
                socialSection.Add(enableSocialField);
                
                var globalChanceProp = socialSettingsProp.FindPropertyRelative("globalInteractionChance");
                var globalChanceField = new PropertyField(globalChanceProp, "Global Interaction Chance");
                globalChanceField.Bind(serializedObject);
                socialSection.Add(globalChanceField);
                
                var globalCooldownProp = socialSettingsProp.FindPropertyRelative("globalCooldown");
                var globalCooldownField = new PropertyField(globalCooldownProp, "Global Cooldown (s)");
                globalCooldownField.Bind(serializedObject);
                socialSection.Add(globalCooldownField);
                
                var checkIntervalProp = socialSettingsProp.FindPropertyRelative("interactionCheckInterval");
                var checkIntervalField = new PropertyField(checkIntervalProp, "Check Interval (s)");
                checkIntervalField.Bind(serializedObject);
                socialSection.Add(checkIntervalField);
                
                var maxDetectionProp = socialSettingsProp.FindPropertyRelative("maxDetectionDistance");
                var maxDetectionField = new PropertyField(maxDetectionProp, "Max Detection Distance");
                maxDetectionField.Bind(serializedObject);
                socialSection.Add(maxDetectionField);
                
                var maxSpeedProp = socialSettingsProp.FindPropertyRelative("maxSpeedForInteraction");
                var maxSpeedField = new PropertyField(maxSpeedProp, "Max Speed for Interaction");
                maxSpeedField.Bind(serializedObject);
                socialSection.Add(maxSpeedField);
                
                // Performance subsection
                var socialPerfLabel = new Label("Performance");
                socialPerfLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                socialPerfLabel.style.marginTop = 10;
                socialSection.Add(socialPerfLabel);
                
                var maxConcurrentProp = socialSettingsProp.FindPropertyRelative("maxConcurrentInteractions");
                var maxConcurrentField = new PropertyField(maxConcurrentProp, "Max Concurrent Interactions");
                maxConcurrentField.Bind(serializedObject);
                socialSection.Add(maxConcurrentField);
                
                var maxChecksProp = socialSettingsProp.FindPropertyRelative("maxChecksPerFrame");
                var maxChecksField = new PropertyField(maxChecksProp, "Max Checks Per Frame");
                maxChecksField.Bind(serializedObject);
                socialSection.Add(maxChecksField);
                
                // Interactions list
                var interactionsLabel = new Label("Interaction Definitions");
                interactionsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                interactionsLabel.style.marginTop = 10;
                socialSection.Add(interactionsLabel);
                
                var interactionsProp = socialSettingsProp.FindPropertyRelative("interactions");
                var interactionsField = new PropertyField(interactionsProp);
                interactionsField.Bind(serializedObject);
                socialSection.Add(interactionsField);
                
                // Create defaults button
                var createDefaultsBtn = new Button(() => CreateDefaultSocialInteractions())
                {
                    text = "Create Default Interactions"
                };
                createDefaultsBtn.style.marginTop = 10;
                socialSection.Add(createDefaultsBtn);
            }
            else
            {
                var socialError = new HelpBox("Social settings not found. Please update CivilSettings.cs.", HelpBoxMessageType.Warning);
                socialSection.Add(socialError);
            }
            
            container.Add(socialSection);
            
            // Quick Actions
            var actionsSection = new VisualElement();
            actionsSection.style.marginTop = 15;
            actionsSection.style.paddingTop = 10;
            actionsSection.style.borderTopWidth = 1;
            actionsSection.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            
            var actionsLabel = new Label("Quick Actions");
            actionsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            actionsLabel.style.marginBottom = 5;
            actionsSection.Add(actionsLabel);
            
            var createManagerBtn = new Button(() => CreateOrSelectWaypointManager())
            {
                text = "Create/Select Waypoint Manager"
            };
            actionsSection.Add(createManagerBtn);
            
            var createPathBtn = new Button(() => CreatePathWaypoint())
            {
                text = "Create Path Waypoint"
            };
            actionsSection.Add(createPathBtn);

            var createLeanBtn = new Button(() => CreateLeanWaypoint())
            {
                text = "Create Lean Waypoint"
            };
            actionsSection.Add(createLeanBtn);

            var createSitBtn = new Button(() => CreateSitWaypoint())
            {
                text = "Create Sit Waypoint"
            };
            actionsSection.Add(createSitBtn);
            
            var docsBtn = new Button(() => Application.OpenURL("https://github.com/your-repo/docs/civil-npc"))
            {
                text = "Open Documentation"
            };
            actionsSection.Add(docsBtn);
            
            container.Add(actionsSection);
            
            return container;
        }
        
        private void CreateOrSelectWaypointManager()
        {
            // Try to find existing manager
            var existing = Object.FindFirstObjectByType<Runtime.Civil.CivilWaypointManager>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                return;
            }
            
            // Create new manager
            var managerGO = new GameObject("CivilWaypointManager");
            managerGO.AddComponent<Runtime.Civil.CivilWaypointManager>();
            
            Undo.RegisterCreatedObjectUndo(managerGO, "Create Civil Waypoint Manager");
            Selection.activeGameObject = managerGO;
            
            Debug.Log("[Civil] Created CivilWaypointManager. Add waypoints as children or in the scene.");
        }
        
        private void CreatePathWaypoint()
        {
            var waypointGO = new GameObject("CivilWaypoint_Path");
            var node = waypointGO.AddComponent<Runtime.Civil.CivilWaypointNode>();
            node.waypointType = Runtime.Civil.CivilWaypointType.Path;
            
            // Add trigger collider for click detection
            var collider = waypointGO.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = true;
            
            // Position in front of scene view camera
            if (SceneView.lastActiveSceneView != null)
            {
                waypointGO.transform.position = SceneView.lastActiveSceneView.pivot;
            }
            
            // Try to snap to NavMesh
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(waypointGO.transform.position, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                waypointGO.transform.position = hit.position;
            }
            
            Undo.RegisterCreatedObjectUndo(waypointGO, "Create Path Waypoint");
            Selection.activeGameObject = waypointGO;
        }

        private void CreateLeanWaypoint()
        {
            var waypointGO = new GameObject("CivilWaypoint_Lean");
            var node = waypointGO.AddComponent<Runtime.Civil.CivilWaypointNode>();
            node.waypointType = Runtime.Civil.CivilWaypointType.Lean;
            node.arrivalRadius = 1.25f;

            var collider = waypointGO.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = true;

            if (SceneView.lastActiveSceneView != null)
            {
                waypointGO.transform.position = SceneView.lastActiveSceneView.pivot;
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(waypointGO.transform.position, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                waypointGO.transform.position = hit.position;
            }

            Undo.RegisterCreatedObjectUndo(waypointGO, "Create Lean Waypoint");
            Selection.activeGameObject = waypointGO;
        }

        private void CreateSitWaypoint()
        {
            var waypointGO = new GameObject("CivilWaypoint_Sit");
            var node = waypointGO.AddComponent<Runtime.Civil.CivilWaypointNode>();
            node.waypointType = Runtime.Civil.CivilWaypointType.Sit;
            node.arrivalRadius = 1.25f;

            var collider = waypointGO.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = true;

            if (SceneView.lastActiveSceneView != null)
            {
                waypointGO.transform.position = SceneView.lastActiveSceneView.pivot;
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(waypointGO.transform.position, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                waypointGO.transform.position = hit.position;
            }

            Undo.RegisterCreatedObjectUndo(waypointGO, "Create Sit Waypoint");
            Selection.activeGameObject = waypointGO;
        }
        
        private void CreateDefaultSocialInteractions()
        {
            var crowd = target as Runtime.EnemyCrowd;
            if (crowd == null || crowd.civilSettings == null)
            {
                Debug.LogError("Cannot create default interactions: EnemyCrowd or civilSettings is null");
                return;
            }
            
            Undo.RecordObject(crowd, "Create Default Social Interactions");
            
            crowd.civilSettings.socialSettings ??= new SocialInteractionSettings();
            crowd.civilSettings.socialSettings.CreateDefaultInteractions();
            
            EditorUtility.SetDirty(crowd);
            Debug.Log($"Created {crowd.civilSettings.socialSettings.interactions.Count} default social interactions for {crowd.factionName}");
        }
        
        #endregion
        
        /// <summary>
        /// Ensures updatePosition is enabled on A* movement components for the referenced prefab.
        /// This avoids expensive manual sync work and keeps behavior consistent with EnemyMasses expectations.
        /// </summary>
        private void EnsureAStarUpdatePositionOnPrefab()
        {
#if ASTAR_EXISTS
            var navigationTypeProp = serializedObject.FindProperty("navigationType");
            if (navigationTypeProp == null) return;
            
            var navigationType = (NavigationType)navigationTypeProp.intValue;
            if (!navigationType.RequiresAStar()) return;
            
            var prefabProp = serializedObject.FindProperty("prefab");
            var prefabGameObject = prefabProp?.objectReferenceValue as GameObject;
            if (prefabGameObject == null) return;
            
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabGameObject);
            if (string.IsNullOrEmpty(prefabPath))
                prefabPath = AssetDatabase.GetAssetPath(prefabGameObject);
            
            if (string.IsNullOrEmpty(prefabPath))
                return;
            
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            bool modified = false;
            
            try
            {
                var aiPath = prefabContents.GetComponent<Pathfinding.AIPath>();
                if (aiPath != null && !aiPath.updatePosition)
                {
                    aiPath.updatePosition = true;
                    modified = true;
                }
                
                var richAI = prefabContents.GetComponent<Pathfinding.RichAI>();
                if (richAI != null && !richAI.updatePosition)
                {
                    richAI.updatePosition = true;
                    modified = true;
                }
                
                var aiLerp = prefabContents.GetComponent<Pathfinding.AILerp>();
                if (aiLerp != null && !aiLerp.updatePosition)
                {
                    aiLerp.updatePosition = true;
                    modified = true;
                }
                
                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
#endif
        }
        
        /// <summary>
        /// Ensures the prefab has all required components for GPU Instancer Crowd system.
        /// </summary>
        private void EnsureRequiredComponents(GameObject prefabGameObject)
        {
            if (prefabGameObject == null)
            {
                Debug.LogError("Cannot add components: prefab is null!");
                return;
            }
            
            // Get the prefab asset path
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabGameObject);
            if (string.IsNullOrEmpty(prefabPath))
            {
                prefabPath = AssetDatabase.GetAssetPath(prefabGameObject);
            }
            
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError($"Cannot modify prefab: {prefabGameObject.name} is not a prefab asset!", prefabGameObject);
                return;
            }
            
            bool modified = false;
            
            // Add GPUIPrefab if missing (using GPUI Pro utility)
            if (!prefabGameObject.HasComponent<GPUIPrefab>())
            {
                GPUIPrefabUtility.AddOrGetComponentToPrefab<GPUIPrefab>(prefabGameObject);
                modified = true;
                Debug.Log($"Added GPUIPrefab component to {prefabGameObject.name}", prefabGameObject);
            }
            
            // Add GPUICrowdInstance if missing — only for humanoid archetypes that need crowd animations
            var archetypePropEnsure = serializedObject.FindProperty("agentArchetype");
            var archetypeEnsure = (AgentArchetype)archetypePropEnsure.intValue;
            bool isNonHumanoidEnsure = archetypeEnsure == AgentArchetype.FlyingVehicle || archetypeEnsure == AgentArchetype.GroundVehicle;
            
            if (!isNonHumanoidEnsure && !prefabGameObject.HasComponent<GPUICrowdInstance>())
            {
                GPUICrowdInstance crowdInstance = GPUIPrefabUtility.AddOrGetComponentToPrefab<GPUICrowdInstance>(prefabGameObject);
                crowdInstance.LoadBoneTransforms(true);
                modified = true;
                Debug.Log($"Added GPUICrowdInstance component to {prefabGameObject.name}", prefabGameObject);
            }
            
            // Add navigation component based on NavigationType
            var navigationTypeProp = serializedObject.FindProperty("navigationType");
            var navigationType = (NavigationType)navigationTypeProp.intValue;
            
            bool needsNavComponent = false;
            string navComponentName = "";
            bool needsRvoController = false;
            
            switch (navigationType)
            {
                case NavigationType.UnityNavMesh:
                    needsNavComponent = !prefabGameObject.HasComponent<NavMeshAgent>();
                    navComponentName = "NavMeshAgent";
                    break;
                case NavigationType.FlowField:
                    needsNavComponent = !prefabGameObject.HasComponent<Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.FlowFieldAgentAdapter>();
                    navComponentName = "FlowFieldAgentAdapter";
                    break;
#if ASTAR_EXISTS
                case NavigationType.AStarAIPath:
                    needsNavComponent = !prefabGameObject.HasComponent<Pathfinding.AIPath>();
                    navComponentName = "AIPath";
                    break;
                case NavigationType.AStarRichAI:
                    needsNavComponent = !prefabGameObject.HasComponent<Pathfinding.RichAI>();
                    navComponentName = "RichAI";
                    break;
                case NavigationType.AStarAILerp:
                    needsNavComponent = !prefabGameObject.HasComponent<Pathfinding.AILerp>();
                    navComponentName = "AILerp";
                    break;
                case NavigationType.AStarRVO:
                    bool hasAIPath = prefabGameObject.HasComponent<Pathfinding.AIPath>();
                    bool hasRichAI = prefabGameObject.HasComponent<Pathfinding.RichAI>();
                    needsNavComponent = !hasAIPath && !hasRichAI;
                    navComponentName = hasRichAI ? "RichAI" : "AIPath";
                    needsRvoController = !prefabGameObject.HasComponent<Pathfinding.RVO.RVOController>();
                    break;
#endif
                case NavigationType.Flight3D:
                    // Flight3DNavigationAgent is created at runtime — no prefab component needed
                    needsNavComponent = false;
                    navComponentName = "Flight3DNavigationAgent (runtime)";
                    break;
                default:
                    needsNavComponent = !prefabGameObject.HasComponent<NavMeshAgent>();
                    navComponentName = "NavMeshAgent";
                    break;
            }
            
            if (needsNavComponent || needsRvoController)
            {
                // Load the prefab for editing
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
                
                try
                {
                    switch (navigationType)
                    {
                        case NavigationType.UnityNavMesh:
                        default:
                            NavMeshAgent agent = prefabContents.AddComponent<NavMeshAgent>();
                            // Set reasonable default values
                            agent.radius = 0.5f;
                            agent.height = 2f;
                            agent.speed = 3.5f;
                            agent.angularSpeed = 120f;
                            agent.acceleration = 8f;
                            agent.stoppingDistance = 0.5f;
                            agent.autoBraking = true;
                            break;
                        case NavigationType.FlowField:
                            prefabContents.AddComponent<Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.FlowFieldAgentAdapter>();
                            break;
#if ASTAR_EXISTS
                        case NavigationType.AStarAIPath:
                            var aiPath = prefabContents.AddComponent<Pathfinding.AIPath>();
                            aiPath.maxSpeed = 3.5f;
                            aiPath.rotationSpeed = 360f;
                            aiPath.slowdownDistance = 0.6f;
                            aiPath.endReachedDistance = 0.2f;
                            aiPath.updatePosition = true;
                            break;
                        case NavigationType.AStarRichAI:
                            var richAI = prefabContents.AddComponent<Pathfinding.RichAI>();
                            richAI.maxSpeed = 3.5f;
                            richAI.rotationSpeed = 360f;
                            richAI.slowdownTime = 0.4f;
                            richAI.endReachedDistance = 0.2f;
                            richAI.updatePosition = true;
                            break;
                        case NavigationType.AStarAILerp:
                            var aiLerp = prefabContents.AddComponent<Pathfinding.AILerp>();
                            aiLerp.speed = 3.5f;
                            aiLerp.rotationSpeed = 360f;
                            aiLerp.updatePosition = true;
                            break;
                        case NavigationType.AStarRVO:
                            if (!prefabContents.HasComponent<Pathfinding.AIPath>() && !prefabContents.HasComponent<Pathfinding.RichAI>())
                            {
                                var rvoAiPath = prefabContents.AddComponent<Pathfinding.AIPath>();
                                rvoAiPath.maxSpeed = 3.5f;
                                rvoAiPath.rotationSpeed = 360f;
                                rvoAiPath.slowdownDistance = 0.6f;
                                rvoAiPath.endReachedDistance = 0.2f;
                                rvoAiPath.updatePosition = true;
                            }
                            if (!prefabContents.HasComponent<Pathfinding.RVO.RVOController>())
                            {
                                prefabContents.AddComponent<Pathfinding.RVO.RVOController>();
                            }
                            break;
#endif
                    }
                    
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
                    modified = true;
                    string addedComponentsLabel = needsNavComponent ? navComponentName : string.Empty;
#if ASTAR_EXISTS
                    if (needsRvoController)
                    {
                        addedComponentsLabel = string.IsNullOrEmpty(addedComponentsLabel)
                            ? "RVOController"
                            : $"{addedComponentsLabel} + RVOController";
                    }
#endif
                    Debug.Log($"Added {addedComponentsLabel} component to {prefabGameObject.name} with default settings", prefabGameObject);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }
            }
            
            // Finalize if modifications were made
            if (modified)
            {
                // Save the prefab asset
                GPUIPrefabUtility.SavePrefabAsset(prefabGameObject);
                
                Debug.Log($"<color=green>Successfully added required components to prefab: {prefabGameObject.name}</color>", prefabGameObject);
                
                // Mark dirty and refresh
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.Log($"All required components already present on {prefabGameObject.name}", prefabGameObject);
            }
        }
        
        #region Faction Preset System
        
        /// <summary>
        /// Available faction presets for quick configuration.
        /// </summary>
        private enum FactionPreset
        {
            EnemyFaction,           // Standard enemy - hostile to player and others
            AllyFaction,            // Friendly to player, hostile to enemies  
            NeutralFaction,         // Won't attack unless provoked
            PlayerOwned,            // Player's own units (summons, pets)
            CompetitiveMultiplayer  // Same faction but different owners are enemies
        }
        
        /// <summary>
        /// Applies a faction preset to the relationship config.
        /// </summary>
        private void ApplyFactionPreset(SerializedProperty configProp, FactionPreset preset)
        {
            if (configProp == null) return;
            
            var playerRelProp = configProp.FindPropertyRelative("defaultPlayerRelationship");
            var factionRelProp = configProp.FindPropertyRelative("defaultFactionRelationship");
            var sameFactionDiffOwnerProp = configProp.FindPropertyRelative("sameFactionDifferentOwner");
            var sameOwnerAllyProp = configProp.FindPropertyRelative("sameOwnerAlwaysAlly");
            
            if (playerRelProp == null || factionRelProp == null || 
                sameFactionDiffOwnerProp == null || sameOwnerAllyProp == null)
            {
                Debug.LogWarning("[EnemyCrowdEditor] Could not find all faction relationship properties for preset.");
                return;
            }
            
            // RelationshipType enum values: Enemy=0, Neutral=1, Ally=2, Self=3
            const int Enemy = 0;
            const int Neutral = 1;
            const int Ally = 2;
            const int Self = 3;
            
            switch (preset)
            {
                case FactionPreset.EnemyFaction:
                    // Standard enemy faction - hostile to player and others
                    playerRelProp.enumValueIndex = Enemy;
                    factionRelProp.enumValueIndex = Enemy;
                    sameFactionDiffOwnerProp.enumValueIndex = Ally;
                    sameOwnerAllyProp.boolValue = true;
                    Debug.Log("[EnemyCrowdEditor] Applied 'Enemy Faction' preset: Hostile to player and other factions.");
                    break;
                    
                case FactionPreset.AllyFaction:
                    // Friendly faction - allied with player, fights enemies
                    playerRelProp.enumValueIndex = Ally;
                    factionRelProp.enumValueIndex = Enemy;
                    sameFactionDiffOwnerProp.enumValueIndex = Ally;
                    sameOwnerAllyProp.boolValue = true;
                    Debug.Log("[EnemyCrowdEditor] Applied 'Ally Faction' preset: Allied with player, hostile to other factions.");
                    break;
                    
                case FactionPreset.NeutralFaction:
                    // Neutral faction - won't attack unless provoked
                    playerRelProp.enumValueIndex = Neutral;
                    factionRelProp.enumValueIndex = Neutral;
                    sameFactionDiffOwnerProp.enumValueIndex = Ally;
                    sameOwnerAllyProp.boolValue = true;
                    Debug.Log("[EnemyCrowdEditor] Applied 'Neutral Faction' preset: Won't attack player or other factions unless provoked.");
                    break;
                    
                case FactionPreset.PlayerOwned:
                    // Player's own units - treats player as self
                    playerRelProp.enumValueIndex = Self;
                    factionRelProp.enumValueIndex = Enemy;
                    sameFactionDiffOwnerProp.enumValueIndex = Ally;
                    sameOwnerAllyProp.boolValue = true;
                    Debug.Log("[EnemyCrowdEditor] Applied 'Player's Own Faction' preset: Player-controlled units, hostile to enemies.");
                    break;
                    
                case FactionPreset.CompetitiveMultiplayer:
                    // Competitive - same faction different owners are enemies
                    playerRelProp.enumValueIndex = Enemy;
                    factionRelProp.enumValueIndex = Enemy;
                    sameFactionDiffOwnerProp.enumValueIndex = Enemy;  // Key difference!
                    sameOwnerAllyProp.boolValue = true;
                    Debug.Log("[EnemyCrowdEditor] Applied 'Competitive Multiplayer' preset: Units of same faction but different owners are enemies.");
                    break;
            }
            
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
        
        #endregion
    }
}
