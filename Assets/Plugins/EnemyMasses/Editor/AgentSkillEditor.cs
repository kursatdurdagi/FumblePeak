using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Arawn.EnemyMasses.Runtime.Skills;
using Arawn.EnemyMasses.Editor.Validation;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Modern UI Toolkit-based custom editor for AgentSkill ScriptableObjects.
    /// Matches the visual style of EnemyCrowdEditor with tabs and organized sections.
    /// </summary>
    [CustomEditor(typeof(AgentSkill))]
    public class AgentSkillEditor : UnityEditor.Editor
    {
        // Cache of all SkillAction types for the dropdown
        private static Type[] _actionTypes;
        private static string[] _actionTypeNames;
        private static string[] _actionTypeMenuLabels;
        private const string ActionDragDataKey = "EnemyMasses.AgentSkillEditor.ActionDragData";
        private static string _copiedActionJson;
        
        private VisualElement _root;
        private VisualElement _contentContainer;
        private VisualElement _validationSummaryContainer;
        private VisualElement _skillPreviewContainer;
        private string _currentTab = "basic";
        private readonly List<Action> _validatedFieldRefreshers = new List<Action>();
        private SerializedProperty _onCastActionsProperty;
        private SerializedProperty _onTrajectoryActionsProperty;
        private SerializedProperty _onFlightActionsProperty;
        private SerializedProperty _onHitActionsProperty;
        private SerializedProperty _onFinishActionsProperty;

        private sealed class SkillActionDragData
        {
            public UnityEngine.Object Owner;
            public string PropertyPath;
            public int SourceIndex;
        }

        private class SkillActionClipboardContainer : ScriptableObject
        {
            [SerializeReference] public SkillAction action;
        }
        
        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            _onCastActionsProperty = serializedObject.FindProperty("onCastActions");
            _onTrajectoryActionsProperty = serializedObject.FindProperty("onTrajectoryActions");
            _onFlightActionsProperty = serializedObject.FindProperty("onFlightActions");
            _onHitActionsProperty = serializedObject.FindProperty("onHitActions");
            _onFinishActionsProperty = serializedObject.FindProperty("onFinishActions");
            CacheActionTypes();
            
            // Load stylesheet (shared with EnemyCrowdEditor)
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Plugins/EnemyMasses/Editor/EnemyCrowdEditorStyle.uss");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);
            
            // Create header
            _root.Add(CreateHeader());
            
            // Create validation summary container
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
            
            // Create holistic skill preview (shown on all tabs)
            _skillPreviewContainer = CreateSkillPreviewContainer();
            _root.Add(_skillPreviewContainer);
            
            // Show initial tab
            ShowTab(_currentTab);
            
            // Register callback to refresh validation and preview when serialized object changes
            _root.RegisterCallback<SerializedPropertyChangeEvent>(evt => 
            {
                RefreshValidationSummary();
                RefreshSkillPreview();
                RefreshValidatedFields();
            });
            
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
            
            var summary = AgentSkillValidationRules.Validate(serializedObject);
            var summaryElement = EditorFieldValidation.CreateValidationSummaryElement(summary);
            _validationSummaryContainer.Add(summaryElement);
        }
        
        /// <summary>
        /// Creates the holistic skill preview container that appears at the bottom of all tabs.
        /// </summary>
        private VisualElement CreateSkillPreviewContainer()
        {
            var container = new VisualElement();
            container.name = "skill-preview-container";
            container.style.marginTop = 12;
            container.style.marginLeft = 4;
            container.style.marginRight = 4;
            container.style.marginBottom = 4;
            container.style.paddingTop = 10;
            container.style.paddingBottom = 10;
            container.style.paddingLeft = 12;
            container.style.paddingRight = 12;
            container.style.backgroundColor = new Color(0.15f, 0.18f, 0.15f, 1f);
            container.style.borderTopLeftRadius = container.style.borderTopRightRadius = 
                container.style.borderBottomLeftRadius = container.style.borderBottomRightRadius = 6;
            container.style.borderTopWidth = container.style.borderBottomWidth = 
                container.style.borderLeftWidth = container.style.borderRightWidth = 1;
            container.style.borderTopColor = container.style.borderBottomColor = 
                container.style.borderLeftColor = container.style.borderRightColor = new Color(0.4f, 0.6f, 0.4f, 0.5f);
            
            // Header
            var header = new Label("⚔ Skill Preview");
            header.style.fontSize = 12;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new Color(0.7f, 0.9f, 0.7f);
            header.style.marginBottom = 6;
            container.Add(header);
            
            // Preview content label
            var previewContent = new Label();
            previewContent.name = "skill-preview-content";
            previewContent.style.fontSize = 11;
            previewContent.style.color = new Color(0.75f, 0.85f, 0.75f);
            previewContent.style.whiteSpace = WhiteSpace.Normal;
            previewContent.style.marginLeft = 4;
            container.Add(previewContent);
            
            // Initial refresh
            RefreshSkillPreviewContent(previewContent);
            
            return container;
        }
        
        /// <summary>
        /// Refreshes the skill preview display.
        /// </summary>
        private void RefreshSkillPreview()
        {
            if (_skillPreviewContainer == null) return;
            
            var previewContent = _skillPreviewContainer.Q<Label>("skill-preview-content");
            if (previewContent != null)
            {
                RefreshSkillPreviewContent(previewContent);
            }
        }
        
        /// <summary>
        /// Builds and sets the holistic skill preview text.
        /// </summary>
        private void RefreshSkillPreviewContent(Label previewLabel)
        {
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject is AgentSkill skill)
            {
                previewLabel.text = skill.GetSkillPreviewText(true);
                return;
            }

            previewLabel.text = string.Empty;
        }
        
        private VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("header");
            
            // Icon preview (shown when an icon sprite is assigned)
            var iconProp = serializedObject.FindProperty("icon");
            
            var iconPreviewContainer = new VisualElement();
            iconPreviewContainer.style.alignItems = Align.Center;
            iconPreviewContainer.style.marginBottom = 6;
            
            var iconPreview = new UnityEngine.UIElements.Image();
            iconPreview.name = "header-icon-preview";
            iconPreview.style.width = 64;
            iconPreview.style.height = 64;
            iconPreview.style.borderTopWidth = 1;
            iconPreview.style.borderBottomWidth = 1;
            iconPreview.style.borderLeftWidth = 1;
            iconPreview.style.borderRightWidth = 1;
            iconPreview.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f);
            iconPreview.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f);
            iconPreview.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f);
            iconPreview.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            iconPreview.style.borderTopLeftRadius = 6;
            iconPreview.style.borderTopRightRadius = 6;
            iconPreview.style.borderBottomLeftRadius = 6;
            iconPreview.style.borderBottomRightRadius = 6;
            iconPreview.scaleMode = ScaleMode.ScaleToFit;
            
            // Set initial icon
            void UpdateIconPreview()
            {
                serializedObject.Update();
                var sprite = iconProp.objectReferenceValue as Sprite;
                if (sprite != null && sprite.texture != null)
                {
                    iconPreview.image = sprite.texture;
                    iconPreviewContainer.style.display = DisplayStyle.Flex;
                }
                else
                {
                    iconPreview.image = null;
                    iconPreviewContainer.style.display = DisplayStyle.None;
                }
            }
            UpdateIconPreview();
            
            iconPreviewContainer.Add(iconPreview);
            header.Add(iconPreviewContainer);
            
            // Track icon property changes to update preview
            iconPreviewContainer.TrackPropertyValue(iconProp, _ => UpdateIconPreview());
            
            // Icon + Title row
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.Center;
            titleRow.style.alignItems = Align.Center;
            
            var titleLabel = new Label("Agent Skill");
            titleLabel.AddToClassList("header-title");
            titleRow.Add(titleLabel);
            header.Add(titleRow);
            
            // Skill name subtitle
            var skillNameProp = serializedObject.FindProperty("skillName");
            var skillName = skillNameProp.stringValue;
            if (!string.IsNullOrEmpty(skillName) && skillName != "New Skill")
            {
                var subtitleLabel = new Label(skillName);
                subtitleLabel.AddToClassList("header-subtitle");
                header.Add(subtitleLabel);
            }
            
            // Skill type badge
            var skillTypeProp = serializedObject.FindProperty("skillType");
            var skillType = (SkillType)skillTypeProp.enumValueIndex;
            var typeBadge = new Label(GetSkillTypeIcon(skillType) + " " + skillType.ToString());
            typeBadge.style.marginTop = 4;
            typeBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            typeBadge.style.fontSize = 11;
            typeBadge.style.color = GetSkillTypeColor(skillType);
            header.Add(typeBadge);
            
            return header;
        }
        
        private string GetSkillTypeIcon(SkillType type) => type switch
        {
            SkillType.Attack => "[ATK]",
            SkillType.Heal => "[HEAL]",
            SkillType.Buff => "[BUFF]",
            SkillType.Debuff => "[DEBUFF]",
            SkillType.Summon => "[SUMMON]",
            SkillType.Movement => "[MOVE]",
            SkillType.Utility => "[UTIL]",
            _ => "-"
        };
        
        private Color GetSkillTypeColor(SkillType type) => type switch
        {
            SkillType.Attack => new Color(1f, 0.4f, 0.4f),
            SkillType.Heal => new Color(0.4f, 1f, 0.4f),
            SkillType.Buff => new Color(0.4f, 0.8f, 1f),
            SkillType.Debuff => new Color(0.8f, 0.4f, 1f),
            SkillType.Summon => new Color(1f, 0.8f, 0.4f),
            SkillType.Movement => new Color(0.4f, 1f, 1f),
            SkillType.Utility => new Color(0.8f, 0.8f, 0.8f),
            _ => Color.white
        };
        
        private VisualElement CreateTabBar()
        {
            var tabBar = new VisualElement();
            tabBar.AddToClassList("tab-bar");
            
            AddTab(tabBar, "basic", "Basic");
            AddTab(tabBar, "targeting", "Targeting");
            AddTab(tabBar, "effects", "Effects");
            AddTab(tabBar, "ai", "AI Behavior");
            AddTab(tabBar, "scaling", "Scaling");
            
            return tabBar;
        }
        
        private void AddTab(VisualElement parent, string tabId, string tabLabel)
        {
            var button = new Button(() => ShowTab(tabId));
            button.text = tabLabel;
            button.AddToClassList("tab-button");
            button.name = $"tab-{tabId}";
            if (_currentTab == tabId)
                button.AddToClassList("tab-active");
            parent.Add(button);
        }
        
        private void ShowTab(string tabId)
        {
            _currentTab = tabId;
            _contentContainer.Clear();
            _validatedFieldRefreshers.Clear();
            
            // Update tab button states
            var tabButtons = _root.Query<Button>(className: "tab-button").ToList();
            foreach (var btn in tabButtons)
            {
                btn.RemoveFromClassList("tab-active");
                if (btn.name == $"tab-{tabId}")
                    btn.AddToClassList("tab-active");
            }
            
            VisualElement content = tabId switch
            {
                "basic" => CreateBasicTab(),
                "targeting" => CreateTargetingTab(),
                "effects" => CreateEffectsTab(),
                "ai" => CreateAITab(),
                "scaling" => CreateScalingTab(),
                _ => CreateBasicTab()
            };
            
            _contentContainer.Add(content);
        }

        private void RefreshValidatedFields()
        {
            for (int i = 0; i < _validatedFieldRefreshers.Count; i++)
            {
                _validatedFieldRefreshers[i]?.Invoke();
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
            
            // Skill Name - Required field with validation
            AddValidatedField(identitySection, "skillName", "Skill Name", so =>
            {
                var prop = so.FindProperty("skillName");
                if (prop == null) return ValidationStatus.RequiredIncomplete;
                var val = prop.stringValue;
                return string.IsNullOrWhiteSpace(val) || val == "New Skill" 
                    ? ValidationStatus.RequiredIncomplete 
                    : ValidationStatus.RequiredComplete;
            }, "Required: Give your skill a unique, descriptive name");
            
            AddPropertyField(identitySection, "description", "Description");
            AddPropertyField(identitySection, "icon", "Icon");
            AddPropertyField(identitySection, "autocastEligibility", "Auto-Cast");
            AddPropertyField(identitySection, "skillBarClickSound", "Skill Bar Click Sound");
            AddPropertyField(identitySection, "skillBarAutoCastOnSound", "Skill Bar Auto-Cast On Sound");
            AddPropertyField(identitySection, "skillBarAutoCastOffSound", "Skill Bar Auto-Cast Off Sound");
            AddPropertyField(identitySection, "skillBarConfirmSound", "Skill Bar Confirm Sound");
            AddPropertyField(identitySection, "skillType", "Skill Type");
            AddPropertyField(identitySection, "attackReachType", "Attack Reach");
            AddPropertyField(identitySection, "activationMode", "Activation Mode");

            var localizationSection = new Foldout { text = "Localization (Optional)", value = false };
            localizationSection.AddToClassList("subsection");

            var localizationInfo = new HelpBox(
                "Optional Unity Localization integration.\n" +
                "When com.unity.localization is installed, table/key values are resolved at runtime.\n" +
                "Skill Preview can also be overridden by a localized full preview entry.",
                HelpBoxMessageType.Info);
            localizationSection.Add(localizationInfo);

            AddPropertyField(localizationSection, "skillNameLocalization.enabled", "Localize Skill Name");
            AddPropertyField(localizationSection, "skillNameLocalization.tableCollection", "Skill Name Table");
            AddPropertyField(localizationSection, "skillNameLocalization.entryKey", "Skill Name Entry Key");
            AddPropertyField(localizationSection, "skillNameLocalization.fallbackToSourceText", "Skill Name Fallback");

            AddPropertyField(localizationSection, "descriptionLocalization.enabled", "Localize Description");
            AddPropertyField(localizationSection, "descriptionLocalization.tableCollection", "Description Table");
            AddPropertyField(localizationSection, "descriptionLocalization.entryKey", "Description Entry Key");
            AddPropertyField(localizationSection, "descriptionLocalization.fallbackToSourceText", "Description Fallback");

            AddPropertyField(localizationSection, "skillPreviewLocalization.enabled", "Localize Preview Override");
            AddPropertyField(localizationSection, "skillPreviewLocalization.tableCollection", "Preview Table");
            AddPropertyField(localizationSection, "skillPreviewLocalization.entryKey", "Preview Entry Key");
            AddPropertyField(localizationSection, "skillPreviewLocalization.fallbackToSourceText", "Preview Fallback");

            identitySection.Add(localizationSection);
            
            container.Add(identitySection);
            
            // Timing Section
            var timingSection = new Foldout { text = "Timing", value = true };
            timingSection.AddToClassList("section");
            
            // Cooldown - Required field with validation
            AddValidatedField(timingSection, "cooldown", "Cooldown (seconds)", so =>
            {
                var prop = so.FindProperty("cooldown");
                if (prop == null) return ValidationStatus.RequiredIncomplete;
                return prop.floatValue >= 0 
                    ? ValidationStatus.RequiredComplete 
                    : ValidationStatus.RequiredIncomplete;
            }, "Required: Set the cooldown duration (0 = no cooldown)");
            
            AddPropertyField(timingSection, "duration", "Duration (seconds)");
            AddPropertyField(timingSection, "castTime", "Cast Time");
            AddPropertyField(timingSection, "lockMovementDuringCast", "Stop Movement During Cast");
            AddPropertyField(timingSection, "castInterruptedByMovement", "Interrupted by Movement");
            AddPropertyField(timingSection, "castInterruptedByDamage", "Interrupted by Damage");
            AddPropertyField(timingSection, "cooldownMode", "Cooldown Mode");
            
            var cooldownHelp = new HelpBox(
                "PerAgent: Each unit has independent cooldowns.\n" +
                "PerEnemyCrowd: Units in the same EnemyCrowd share the cooldown.\n" +
                "PerFaction: All units share the cooldown (prevents mass skill spam).",
                HelpBoxMessageType.Info);
            timingSection.Add(cooldownHelp);
            
            container.Add(timingSection);
            
            // Critical Strike Section
            var critSection = new Foldout { text = "Critical Strike", value = false };
            critSection.AddToClassList("section");
            
            AddPropertyField(critSection, "criticalChance", "Critical Chance");
            AddPropertyField(critSection, "criticalMultiplier", "Critical Multiplier");
            
            container.Add(critSection);
            
            // Resource Cost Section
            var resourceSection = new Foldout { text = "Resource Cost", value = false };
            resourceSection.AddToClassList("section");
            
            var resourceHelp = new HelpBox(
                "When enabled, the skill requires a resource (e.g. Mana, Energy) to cast. " +
                "If the agent lacks the required amount, the skill is blocked.\n\n" +
                "Resource Amount is the base cost. If you enable Level Scaling (in the Scaling tab), " +
                "the actual cost will be computed via the Resource Cost Scaling formula " +
                "(e.g. cost increases or decreases with caster level).\n\n" +
                "Built-in Provider: Call SkillResourceManager.Instance.Provider.InitializeAgent() " +
                "to set up resource pools per agent. No extra packages needed.\n\n" +
                "Game Creator 2 Stats: Add a Traits component to your crowd prefab, create an " +
                "Attribute whose ID matches the Resource Name below (case-insensitive), then register " +
                "the GC2 adapter at startup:\n" +
                "  SkillResourceManager.Instance.SetProvider(new GC2StatsSkillResourceProvider());",
                HelpBoxMessageType.Info);
            resourceSection.Add(resourceHelp);
            
            AddPropertyField(resourceSection, "hasResourceCost", "Has Resource Cost");
            AddPropertyField(resourceSection, "resourceName", "Resource Name");
            AddPropertyField(resourceSection, "resourceCost", "Resource Amount");
            
            container.Add(resourceSection);
            
            return container;
        }
        
        #endregion
        
        #region Targeting Tab
        
        private VisualElement CreateTargetingTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Targeting Type Section
            var typeSection = new Foldout { text = "Target Selection", value = true };
            typeSection.AddToClassList("section");
            
            // Targeting Type dropdown
            var targetingTypeField = new PropertyField(serializedObject.FindProperty("targetingType"), "Targeting Type");
            targetingTypeField.Bind(serializedObject);
            typeSection.Add(targetingTypeField);
            
            // Conditional fields container for targeting-specific settings
            var conditionalContainer = new VisualElement();
            conditionalContainer.name = "targeting-conditional-fields";
            conditionalContainer.style.marginTop = 8;
            typeSection.Add(conditionalContainer);
            
            // Helper to refresh conditional fields based on targeting type
            void RefreshTargetingFields()
            {
                conditionalContainer.Clear();
                serializedObject.Update();
                
                var targetingTypeProp = serializedObject.FindProperty("targetingType");
                var targetingType = (SkillTargetingType)targetingTypeProp.enumValueIndex;
                
                // Determine which fields to show based on targeting type
                bool showRange = true;
                bool showMaxTargets = false;
                bool showAOERadius = false;
                bool showConeAngle = false;
                bool showLineWidth = false;
                bool showCanTargetDead = false;
                string helpText = "";
                
                switch (targetingType)
                {
                    case SkillTargetingType.Self:
                        showRange = false;
                        helpText = "Skill affects only the caster. No targeting required.";
                        break;
                        
                    case SkillTargetingType.SingleEnemy:
                        showCanTargetDead = true;
                        helpText = "Targets a single enemy within range.\n" +
                                   "AI will pick the current combat target.";
                        break;
                        
                    case SkillTargetingType.SingleAlly:
                        showCanTargetDead = true;
                        helpText = "Targets a single ally within range.\n" +
                                   "Useful for single-target heals or buffs.";
                        break;
                        
                    case SkillTargetingType.ClosestEnemy:
                        showCanTargetDead = true;
                        helpText = "Automatically targets the closest enemy in range.\n" +
                                   "Good for auto-attacks and reactive skills.";
                        break;
                        
                    case SkillTargetingType.LowestHealthAlly:
                        showCanTargetDead = true;
                        helpText = "Automatically targets the ally with lowest health.\n" +
                                   "Perfect for smart healing abilities.";
                        break;
                        
                    case SkillTargetingType.GroundTargetedAOE:
                        showAOERadius = true;
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects all valid targets within the AOE radius.\n" +
                                   "Centered on a ground point (artillery strike style).";
                        break;
                        
                    case SkillTargetingType.AOEAroundSelf:
                        showRange = false;
                        showAOERadius = true;
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects all valid targets in a radius around the caster.\n" +
                                   "Examples: Whirlwind, Shockwave, Aura effects.";
                        break;
                        
                    case SkillTargetingType.AOEAroundEnemy:
                        showAOERadius = true;
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects all enemies in a radius around the target.\n" +
                                   "Examples: Exploding projectiles, chain reactions.";
                        break;
                        
                    case SkillTargetingType.AOEAroundAlly:
                        showAOERadius = true;
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects all allies in a radius around the target.\n" +
                                   "Examples: Group heals, team buffs.";
                        break;
                        
                    case SkillTargetingType.Cone:
                        showConeAngle = true;
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects all targets in a cone in front of the caster.\n" +
                                   "The cone extends from caster to Max Range.\n" +
                                   "Examples: Cleave, Breath attacks, Shockwave.";
                        break;
                        
                    case SkillTargetingType.Line:
                        showLineWidth = true;
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects all targets in a line from caster to max range.\n" +
                                   "Examples: Piercing shot, Laser beam, Charge.";
                        break;
                        
                    case SkillTargetingType.Chain:
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Chains from the primary target to nearby targets.\n" +
                                   "Max Targets determines how many jumps occur.\n" +
                                   "Examples: Chain Lightning, Bouncing Blade.";
                        break;
                        
                    case SkillTargetingType.AllEnemiesInRange:
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects ALL enemies within range.\n" +
                                   "Use Range to limit the area.\n" +
                                   "Examples: Intimidating Shout, Battle Cry.";
                        break;
                        
                    case SkillTargetingType.AllAlliesInRange:
                        showMaxTargets = true;
                        showCanTargetDead = true;
                        helpText = "Affects ALL allies within range.\n" +
                                   "Examples: Rally cry, Aura buff.";
                        break;
                    
                    case SkillTargetingType.AllCrowdMembers:
                        showRange = false;
                        showCanTargetDead = true;
                        helpText = "Affects ALL agents belonging to the same EnemyCrowd as the caster.\n" +
                                   "Ignores range — crowd membership is the only filter.\n" +
                                   "Pair with SingleCast + PerEnemyCrowd cooldown for crowd-wide buffs.\n" +
                                   "Examples: Crowd Invulnerability, Mass Shield, Rally.";
                        break;
                    
                    case SkillTargetingType.AllFactionMembers:
                        showRange = false;
                        showCanTargetDead = true;
                        helpText = "Affects ALL agents belonging to the same faction as the caster.\n" +
                                   "Ignores range — faction membership is the only filter.\n" +
                                   "Broader than AllCrowdMembers: includes all EnemyCrowds in the faction.\n" +
                                   "Pair with SingleCast + PerFaction cooldown for faction-wide buffs.\n" +
                                   "Examples: Faction Invulnerability, Global Rally, Mass Resurrection.";
                        break;
                        
                    case SkillTargetingType.Projectile:
                        showRange = false; // Range is in projectileTargeting
                        showCanTargetDead = false; // In projectileTargeting
                        helpText = "Fires a projectile that applies Effects on impact.\n" +
                                   "Configure trajectory, AOE, and piercing below.\n" +
                                   "Add DamageEffect in Effects tab for damage.";
                        break;
                    
                    case SkillTargetingType.Hitscan:
                        showCanTargetDead = true;
                        helpText = "Instant ranged hit with no projectile travel.\n" +
                                   "Applies Effects immediately on the selected enemy.\n" +
                                   "Good for guns, lasers, and shotgun-style attacks.";
                        break;
                        
                    default:
                        helpText = "Select a targeting type to see specific options.";
                        break;
                }
                
                // Add help text at top
                if (!string.IsNullOrEmpty(helpText))
                {
                    var helpBox = new HelpBox(helpText, HelpBoxMessageType.Info);
                    conditionalContainer.Add(helpBox);
                }
                
                // Projectile-specific settings
                if (targetingType == SkillTargetingType.Projectile)
                {
                    var projectileGroup = CreateProjectileTargetingSection();
                    conditionalContainer.Add(projectileGroup);
                    conditionalContainer.Add(CreateProjectileVisualsFoldout());
                }
                
                // Range fields
                if (showRange)
                {
                    var rangeGroup = new VisualElement();
                    rangeGroup.style.marginTop = 8;
                    
                    var rangeLabel = new Label("Range");
                    rangeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    rangeLabel.style.marginBottom = 4;
                    rangeGroup.Add(rangeLabel);
                    
                    AddPropertyField(rangeGroup, "minRange", "Min Range");
                    AddPropertyField(rangeGroup, "maxRange", "Max Range");
                    
                    conditionalContainer.Add(rangeGroup);
                }
                
                // AOE Radius
                if (showAOERadius)
                {
                    var aoeGroup = new VisualElement();
                    aoeGroup.style.marginTop = 8;
                    
                    var aoeLabel = new Label("Area of Effect");
                    aoeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    aoeLabel.style.marginBottom = 4;
                    aoeGroup.Add(aoeLabel);
                    
                    AddPropertyField(aoeGroup, "aoeRadius", "AOE Radius");
                    
                    conditionalContainer.Add(aoeGroup);
                }
                
                // Cone settings
                if (showConeAngle)
                {
                    var coneGroup = new VisualElement();
                    coneGroup.style.marginTop = 8;
                    
                    var coneLabel = new Label("Cone Shape");
                    coneLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    coneLabel.style.marginBottom = 4;
                    coneGroup.Add(coneLabel);
                    
                    AddPropertyField(coneGroup, "coneAngle", "Cone Angle (degrees)");
                    AddPropertyField(coneGroup, "maxRange", "Cone Length (Max Range)");
                    
                    // Visual indicator of cone size
                    var angleProp = serializedObject.FindProperty("coneAngle");
                    var rangeProp = serializedObject.FindProperty("maxRange");
                    var coneInfo = new Label($"Cone: {angleProp.floatValue:F0}° wide, {rangeProp.floatValue:F1}m long");
                    coneInfo.style.color = new Color(0.5f, 0.8f, 0.5f);
                    coneInfo.style.marginTop = 4;
                    coneGroup.Add(coneInfo);
                    
                    conditionalContainer.Add(coneGroup);
                }
                
                // Line settings
                if (showLineWidth)
                {
                    var lineGroup = new VisualElement();
                    lineGroup.style.marginTop = 8;
                    
                    var lineLabel = new Label("Line Shape");
                    lineLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    lineLabel.style.marginBottom = 4;
                    lineGroup.Add(lineLabel);
                    
                    AddPropertyField(lineGroup, "lineWidth", "Line Width");
                    AddPropertyField(lineGroup, "maxRange", "Line Length (Max Range)");
                    
                    // Visual indicator
                    var widthProp = serializedObject.FindProperty("lineWidth");
                    var rangeProp = serializedObject.FindProperty("maxRange");
                    var lineInfo = new Label($"Line: {widthProp.floatValue:F1}m wide, {rangeProp.floatValue:F1}m long");
                    lineInfo.style.color = new Color(0.5f, 0.8f, 0.5f);
                    lineInfo.style.marginTop = 4;
                    lineGroup.Add(lineInfo);
                    
                    conditionalContainer.Add(lineGroup);
                }
                
                // Max targets
                if (showMaxTargets)
                {
                    var targetsGroup = new VisualElement();
                    targetsGroup.style.marginTop = 8;
                    
                    var targetsLabel = new Label("Target Limits");
                    targetsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    targetsLabel.style.marginBottom = 4;
                    targetsGroup.Add(targetsLabel);
                    
                    AddPropertyField(targetsGroup, "maxTargets", "Max Targets (0 = unlimited)");
                    
                    conditionalContainer.Add(targetsGroup);
                }
                
                // Can target dead
                if (showCanTargetDead)
                {
                    var deadGroup = new VisualElement();
                    deadGroup.style.marginTop = 8;
                    
                    AddPropertyField(deadGroup, "canTargetDead", "Can Target Dead Units");
                    
                    var canTargetDeadProp = serializedObject.FindProperty("canTargetDead");
                    if (canTargetDeadProp.boolValue)
                    {
                        var deadHelp = new HelpBox(
                            "This skill can affect dead units.\n" +
                            "Examples: Resurrect, Corpse Explosion, Soul Harvest.",
                            HelpBoxMessageType.Info);
                        deadGroup.Add(deadHelp);
                    }
                    
                    conditionalContainer.Add(deadGroup);
                }
            }
            
            // Initial refresh
            RefreshTargetingFields();
            
            // Re-bind when targeting type changes
            targetingTypeField.RegisterValueChangeCallback(evt => RefreshTargetingFields());
            
            container.Add(typeSection);
            
            return container;
        }
        
        /// <summary>
        /// Creates the UI section for Projectile targeting configuration.
        /// </summary>
        private VisualElement CreateProjectileTargetingSection()
        {
            var container = new VisualElement();
            container.style.marginTop = 8;
            
            var projectileProp = serializedObject.FindProperty("projectileTargeting");
            
            // Trajectory Section
            var trajectoryFoldout = new Foldout { text = "Trajectory", value = true };
            trajectoryFoldout.style.marginTop = 8;
            
            var trajectoryTypeField = new PropertyField(serializedObject.FindProperty("projectileTargeting.trajectory"), "Trajectory Type");
            trajectoryTypeField.Bind(serializedObject);
            trajectoryFoldout.Add(trajectoryTypeField);

            var targetHitBehaviorField = new PropertyField(serializedObject.FindProperty("projectileTargeting.targetHitBehavior"), "On Target Hit");
            targetHitBehaviorField.Bind(serializedObject);
            trajectoryFoldout.Add(targetHitBehaviorField);
            
            // Arc Settings (only for Arc trajectory)
            var arcSettingsContainer = new VisualElement();
            arcSettingsContainer.name = "arc-settings";
            arcSettingsContainer.style.marginTop = 8;
            arcSettingsContainer.style.marginLeft = 8;
            
            AddPropertyField(arcSettingsContainer, "projectileTargeting.arcMode", "Arc Height Mode");
            
            // Arc mode specific fields
            var launchAngleField = new VisualElement();
            AddPropertyField(launchAngleField, "projectileTargeting.launchAngle", "Launch Angle");
            arcSettingsContainer.Add(launchAngleField);
            
            var flightTimeField = new VisualElement();
            AddPropertyField(flightTimeField, "projectileTargeting.flightTime", "Flight Time");
            arcSettingsContainer.Add(flightTimeField);
            
            var arcHeightField = new VisualElement();
            AddPropertyField(arcHeightField, "projectileTargeting.arcHeight", "Arc Height");
            arcSettingsContainer.Add(arcHeightField);
            
            AddPropertyField(arcSettingsContainer, "projectileTargeting.gravity", "Gravity");
            
            trajectoryFoldout.Add(arcSettingsContainer);
            
            // Linear Settings (only for Linear trajectory)
            var linearSettingsContainer = new VisualElement();
            linearSettingsContainer.name = "linear-settings";
            linearSettingsContainer.style.marginTop = 8;
            linearSettingsContainer.style.marginLeft = 8;
            
            AddPropertyField(linearSettingsContainer, "projectileTargeting.linearSpeed", "Projectile Speed");
            
            trajectoryFoldout.Add(linearSettingsContainer);
            
            // Homing Settings (only for Homing trajectory)
            var homingSettingsContainer = new VisualElement();
            homingSettingsContainer.name = "homing-settings";
            homingSettingsContainer.style.marginTop = 8;
            homingSettingsContainer.style.marginLeft = 8;
            
            AddPropertyField(homingSettingsContainer, "projectileTargeting.homingTurnRate", "Turn Rate (deg/s)");
            AddPropertyField(homingSettingsContainer, "projectileTargeting.homingSpeed", "Speed");
            AddPropertyField(homingSettingsContainer, "projectileTargeting.homingAcceleration", "Acceleration");
            
            trajectoryFoldout.Add(homingSettingsContainer);

            // Trajectory Piercing Settings (supported projectile trajectories)
            var trajectoryPiercingContainer = new VisualElement();
            trajectoryPiercingContainer.name = "trajectory-piercing-container";
            trajectoryPiercingContainer.style.marginTop = 8;
            trajectoryPiercingContainer.style.marginLeft = 8;

            var trajectoryPiercingField = new PropertyField(
                serializedObject.FindProperty("projectileTargeting.enableTrajectoryPiercing"),
                "Enable Trajectory Piercing");
            trajectoryPiercingField.Bind(serializedObject);
            trajectoryPiercingContainer.Add(trajectoryPiercingField);

            var trajectoryPiercingSettingsContainer = new VisualElement();
            trajectoryPiercingSettingsContainer.name = "trajectory-piercing-settings";
            trajectoryPiercingSettingsContainer.style.marginLeft = 8;
            AddPropertyField(trajectoryPiercingSettingsContainer, "projectileTargeting.maxTrajectoryPierceTargets", "Max Trajectory Hits");
            AddPropertyField(trajectoryPiercingSettingsContainer, "projectileTargeting.trajectoryPierceFalloff", "Trajectory Falloff");
            trajectoryPiercingContainer.Add(trajectoryPiercingSettingsContainer);

            trajectoryFoldout.Add(trajectoryPiercingContainer);
            
            // Function to update trajectory-specific visibility
            void UpdateTrajectorySettingsVisibility()
            {
                serializedObject.Update();
                var trajProp = serializedObject.FindProperty("projectileTargeting.trajectory");
                var trajectory = (SkillProjectileTrajectory)trajProp.enumValueIndex;
                
                arcSettingsContainer.style.display = trajectory == SkillProjectileTrajectory.Arc ? DisplayStyle.Flex : DisplayStyle.None;
                linearSettingsContainer.style.display = trajectory == SkillProjectileTrajectory.Linear ? DisplayStyle.Flex : DisplayStyle.None;
                homingSettingsContainer.style.display = trajectory == SkillProjectileTrajectory.Homing ? DisplayStyle.Flex : DisplayStyle.None;
                bool supportsTrajectoryPiercing =
                    trajectory == SkillProjectileTrajectory.Linear ||
                    trajectory == SkillProjectileTrajectory.Arc ||
                    trajectory == SkillProjectileTrajectory.Homing;
                trajectoryPiercingContainer.style.display = supportsTrajectoryPiercing ? DisplayStyle.Flex : DisplayStyle.None;
                
                bool piercingEnabled = serializedObject.FindProperty("projectileTargeting.enableTrajectoryPiercing").boolValue;
                var trajectoryActionsProp = serializedObject.FindProperty("onTrajectoryActions");
                bool hasTrajectoryActions = trajectoryActionsProp != null && trajectoryActionsProp.arraySize > 0;
                bool piercePrimaryTarget =
                    serializedObject.FindProperty("projectileTargeting.targetHitBehavior").enumValueIndex ==
                    (int)ProjectileTargetHitBehavior.PierceThrough;
                trajectoryPiercingSettingsContainer.style.display =
                    supportsTrajectoryPiercing && (piercingEnabled || hasTrajectoryActions || piercePrimaryTarget)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                
                // Also update arc mode specific fields
                if (trajectory == SkillProjectileTrajectory.Arc)
                {
                    var arcModeProp = serializedObject.FindProperty("projectileTargeting.arcMode");
                    var arcMode = (ArcHeightMode)arcModeProp.enumValueIndex;
                    
                    launchAngleField.style.display = arcMode == ArcHeightMode.LaunchAngle ? DisplayStyle.Flex : DisplayStyle.None;
                    flightTimeField.style.display = arcMode == ArcHeightMode.FlightTime ? DisplayStyle.Flex : DisplayStyle.None;
                    arcHeightField.style.display = arcMode == ArcHeightMode.FixedHeight ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
            
            // Initial visibility
            UpdateTrajectorySettingsVisibility();
            
            // Register callback for trajectory type change
            trajectoryTypeField.RegisterValueChangeCallback(evt => UpdateTrajectorySettingsVisibility());
            trajectoryPiercingField.RegisterValueChangeCallback(evt => UpdateTrajectorySettingsVisibility());
            targetHitBehaviorField.RegisterValueChangeCallback(evt => UpdateTrajectorySettingsVisibility());
            
            // Register callback for arc mode change
            var arcModeField = arcSettingsContainer.Q<PropertyField>();
            if (arcModeField != null)
            {
                arcModeField.RegisterValueChangeCallback(evt => UpdateTrajectorySettingsVisibility());
            }
            
            container.Add(trajectoryFoldout);
            
            // Target Selection Section
            var targetSelectionFoldout = new Foldout { text = "Target Selection", value = true };
            targetSelectionFoldout.style.marginTop = 8;
            
            AddPropertyField(targetSelectionFoldout, "projectileTargeting.canTargetEnemies", "Can Target Enemies");
            AddPropertyField(targetSelectionFoldout, "projectileTargeting.canTargetAllies", "Can Target Allies");
            AddPropertyField(targetSelectionFoldout, "projectileTargeting.canTargetDead", "Can Target Dead");
            
            container.Add(targetSelectionFoldout);
            
            // Range Section
            var rangeFoldout = new Foldout { text = "Range", value = true };
            rangeFoldout.style.marginTop = 8;
            
            AddPropertyField(rangeFoldout, "projectileTargeting.minRange", "Min Range");
            AddPropertyField(rangeFoldout, "projectileTargeting.maxRange", "Max Range");
            
            container.Add(rangeFoldout);
            
            // Info about action phase usage for projectile skills
            var infoBox = new HelpBox(
                "Add DamageEffect in the Effects tab to apply damage.\n" +
                "On Trajectory Hit actions execute when the projectile crosses targets while flying.\n" +
                "During Flight (Timed) actions execute repeatedly by interval while the projectile is active.\n" +
                "Trajectory Piercing toggle can still force pass-through even with no trajectory hit actions.\n" +
                "On Target Hit controls whether projectile stops with fade, stops instantly (no fade), pierces through,\n" +
                "or ignores blockers and stops only on the designated target (with/without fade).\n" +
                "Use On Hit actions for terminal impact effects (burst/aoe/finish).\n" +
                "Use AOEOnImpactEffect for area damage with falloff at impact.",
                HelpBoxMessageType.Info);
            infoBox.style.marginTop = 12;
            container.Add(infoBox);
            
            return container;
        }
        
        #endregion
        
        #region Effects Tab
        
        private VisualElement CreateEffectsTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Info box
            var infoBox = new HelpBox(
                "Actions define what happens during each phase of the skill. " +
                "Add SkillActions to On Cast, On Trajectory Hit, During Flight (Timed), On Hit, and On Finish to control behavior, VFX, and SFX.",
                HelpBoxMessageType.Info);
            container.Add(infoBox);
            
            container.Add(CreateActionPhaseSection("On Cast", "onCastActions", "No On Cast actions configured."));
            container.Add(CreateActionPhaseSection("On Trajectory Hit", "onTrajectoryActions", "No On Trajectory Hit actions configured."));
            container.Add(CreateActionPhaseSection("During Flight (Timed)", "onFlightActions", "No During Flight actions configured."));
            container.Add(CreateFlightActionTimingSection());
            container.Add(CreateActionPhaseSection("On Hit", "onHitActions", "No On Hit actions configured."));
            container.Add(CreateActionPhaseSection("On Finish", "onFinishActions", "No On Finish actions configured."));
            
            // Animation Section
            var animSection = new Foldout { text = "Animation", value = true };
            animSection.AddToClassList("section");
            
            // Animation Type dropdown
            var animTypeField = new PropertyField(serializedObject.FindProperty("animationType"), "Animation Type");
            animTypeField.Bind(serializedObject);
            animSection.Add(animTypeField);
            
            // Conditional fields container
            var conditionalContainer = new VisualElement();
            conditionalContainer.name = "animation-conditional-fields";
            animSection.Add(conditionalContainer);
            
            // Helper to refresh conditional fields
            void RefreshAnimationFields()
            {
                conditionalContainer.Clear();
                serializedObject.Update();
                
                var animTypeProp = serializedObject.FindProperty("animationType");
                var animType = (SkillAnimationType)animTypeProp.enumValueIndex;
                
                switch (animType)
                {
                    case SkillAnimationType.CustomTrigger:
                        // Just the trigger field
                        AddPropertyField(conditionalContainer, "customAnimationTrigger", "Trigger Parameter");
                        var triggerHelp = new HelpBox(
                            "Enter the Animator trigger parameter name.\n" +
                            "Works with: Mecanim Reader, Bone Tracker workflows.",
                            HelpBoxMessageType.Info);
                        conditionalContainer.Add(triggerHelp);
                        break;
                        
                    case SkillAnimationType.SkillSpecificAnimation:
                        // Animation Clip field
                        AddPropertyField(conditionalContainer, "skillAnimationClip", "Animation Clip");
                        
                        // Trigger Parameter field
                        AddPropertyField(conditionalContainer, "customAnimationTrigger", "Trigger Parameter");
                        
                        // Duration override
                        AddPropertyField(conditionalContainer, "animationDurationOverride", "Duration Override (0 = auto)");
                        
                        // Show auto-calculated duration if clip is assigned
                        var clipProp = serializedObject.FindProperty("skillAnimationClip");
                        if (clipProp.objectReferenceValue != null)
                        {
                            var clip = clipProp.objectReferenceValue as AnimationClip;
                            if (clip != null)
                            {
                                var durationLabel = new Label($"Clip Duration: {clip.length:F2}s");
                                durationLabel.style.color = new Color(0.5f, 0.8f, 0.5f);
                                durationLabel.style.marginLeft = 16;
                                conditionalContainer.Add(durationLabel);
                            }
                        }
                        
                        // Workflow help box
                        var workflowHelp = new HelpBox(
                            "Workflow Requirements:\n" +
                            "• Compute Animator: Animation Clip required\n" +
                            "• Bone Tracker / Mecanim: Trigger Parameter required\n" +
                            "• Recommended: Set both for runtime workflow switching",
                            HelpBoxMessageType.Info);
                        conditionalContainer.Add(workflowHelp);
                        break;
                        
                    case SkillAnimationType.UseMeleeAnimation:
                    case SkillAnimationType.UseRangedAnimation:
                    case SkillAnimationType.UseCastAnimation:
                        // Show info about which EnemyCrowd triggers will be used
                        var factionHelp = new HelpBox(
                            $"Uses trigger parameters from EnemyCrowd Animation Tab.\n" +
                            $"Configure in: {GetTriggerFieldName(animType)}",
                            HelpBoxMessageType.Info);
                        conditionalContainer.Add(factionHelp);
                        break;
                        
                    case SkillAnimationType.None:
                    default:
                        // No additional fields needed
                        break;
                }
            }
            
            // Initial refresh
            RefreshAnimationFields();
            
            // Re-bind when animation type changes
            animTypeField.RegisterValueChangeCallback(evt => RefreshAnimationFields());
            
            container.Add(animSection);
            
            return container;
        }

        private VisualElement CreateActionPhaseSection(string title, string propertyPath, string emptyMessage)
        {
            var section = new Foldout { text = $"{title} Actions", value = true };
            section.AddToClassList("section");
            
            var listProp = serializedObject.FindProperty(propertyPath);
            int count = listProp != null ? listProp.arraySize : 0;
            
            var statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            statusRow.style.marginBottom = 4;
            statusRow.style.paddingLeft = 4;
            statusRow.style.paddingRight = 4;
            statusRow.style.paddingTop = 4;
            statusRow.style.paddingBottom = 4;
            statusRow.style.borderTopLeftRadius = 4f;
            statusRow.style.borderTopRightRadius = 4f;
            statusRow.style.borderBottomLeftRadius = 4f;
            statusRow.style.borderBottomRightRadius = 4f;
            
            var statusLabel = new Label(count > 0 ? $"{count} action(s) configured" : emptyMessage);
            statusLabel.style.flexGrow = 1;
            statusRow.Add(statusLabel);
            section.Add(statusRow);
            
            var actionsContainer = new VisualElement();
            actionsContainer.name = $"{propertyPath}-container";
            RebuildActionList(propertyPath, actionsContainer, emptyMessage);
            section.Add(actionsContainer);
            
            var addButtonRow = new VisualElement();
            addButtonRow.style.flexDirection = FlexDirection.Row;
            addButtonRow.style.marginTop = 8;
            
            var addButton = new Button(() => ShowAddActionMenu(propertyPath, actionsContainer, emptyMessage));
            addButton.text = "+ Add Action";
            addButton.style.flexGrow = 1;
            addButtonRow.Add(addButton);

            var pasteButton = new Button(() => PasteAction(propertyPath, int.MaxValue, actionsContainer, emptyMessage));
            pasteButton.text = "Paste Action";
            pasteButton.style.marginLeft = 6;
            pasteButton.style.flexGrow = 1;
            addButtonRow.Add(pasteButton);
            
            section.Add(addButtonRow);
            
            return section;
        }

        private VisualElement CreateFlightActionTimingSection()
        {
            var section = new Foldout { text = "During Flight Timing", value = false };
            section.AddToClassList("section");

            AddPropertyField(section, "onFlightActionsInterval", "Tick Interval");
            AddPropertyField(section, "onFlightActionsStartDelay", "Start Delay");
            AddPropertyField(section, "onFlightActionsMaxTicks", "Max Ticks (0 = unlimited)");

            var info = new HelpBox(
                "Used only by Projectile skills.\n" +
                "During Flight actions run repeatedly while the projectile is active.\n" +
                "Set Spawn Location to TargetPosition in SFX/VFX actions to follow the projectile path.",
                HelpBoxMessageType.Info);
            section.Add(info);

            void UpdateVisibility()
            {
                var targetingTypeProp = serializedObject.FindProperty("targetingType");
                bool isProjectile = targetingTypeProp != null &&
                    targetingTypeProp.enumValueIndex == (int)SkillTargetingType.Projectile;
                section.style.display = isProjectile ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateVisibility();
            section.TrackPropertyValue(serializedObject.FindProperty("targetingType"), _ => UpdateVisibility());

            return section;
        }
        
        private string GetTriggerFieldName(SkillAnimationType animType)
        {
            return animType switch
            {
                SkillAnimationType.UseMeleeAnimation => "meleeAttackTriggerParametersOverride (or attackTriggerParametersOverride)",
                SkillAnimationType.UseRangedAnimation => "rangedAttackTriggerParametersOverride (or attackTriggerParametersOverride)",
                SkillAnimationType.UseCastAnimation => "Custom trigger 'Cast' (add to animator)",
                _ => "EnemyCrowd Animation Tab"
            };
        }
        
        private void RebuildActionList(string propertyPath, VisualElement container, string emptyMessage)
        {
            container.Clear();
            serializedObject.Update();
            
            var listProp = serializedObject.FindProperty(propertyPath);
            if (listProp == null)
            {
                container.Add(new Label($"Missing property: {propertyPath}"));
                return;
            }
            
            if (listProp.arraySize == 0)
            {
                var emptyLabel = new Label(emptyMessage);
                emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                emptyLabel.style.marginTop = 8;
                emptyLabel.style.marginBottom = 8;
                container.Add(emptyLabel);
                return;
            }
            
            for (int i = 0; i < listProp.arraySize; i++)
            {
                container.Add(CreateActionElement(propertyPath, listProp, i, container, emptyMessage));
            }
        }
        
        private VisualElement CreateActionElement(string propertyPath, SerializedProperty listProp, int index, VisualElement parentContainer, string emptyMessage)
        {
            var elementProp = listProp.GetArrayElementAtIndex(index);
            var actionObj = elementProp.managedReferenceValue;
            
            var effectBox = new VisualElement();
            effectBox.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            effectBox.style.borderTopLeftRadius = 4;
            effectBox.style.borderTopRightRadius = 4;
            effectBox.style.borderBottomLeftRadius = 4;
            effectBox.style.borderBottomRightRadius = 4;
            effectBox.style.paddingTop = 8;
            effectBox.style.paddingBottom = 8;
            effectBox.style.paddingLeft = 10;
            effectBox.style.paddingRight = 10;
            effectBox.style.marginBottom = 6;
            
            // Header row
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            
            // Effect type icon and name
            string typeName = actionObj != null ? actionObj.GetType().Name : "(None)";
            string icon = GetActionIcon(typeName);
            // Apply display-friendly name overrides, then generic suffix stripping
            typeName = typeName switch
            {
                "FlyEffect" => "Fly (Legacy)",
                _ => typeName.EndsWith("Effect") ? typeName.Substring(0, typeName.Length - 6)
                   : typeName.EndsWith("Action") ? typeName.Substring(0, typeName.Length - 6)
                   : typeName
            };

            var dragHandle = new Label("|||");
            dragHandle.tooltip = "Drag to reorder";
            dragHandle.style.width = 18;
            dragHandle.style.unityTextAlign = TextAnchor.MiddleCenter;
            dragHandle.style.color = new Color(0.7f, 0.7f, 0.7f);
            dragHandle.style.marginRight = 4;
            RegisterDragHandle(dragHandle, propertyPath, index);
            
            var foldout = new Foldout();
            foldout.text = $"{icon} {index + 1}. {typeName}";
            foldout.value = elementProp.isExpanded;
            foldout.style.flexGrow = 1;
            
            // Buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginLeft = 6;
            
            // Remove
            var removeBtn = new Button(() =>
            {
                listProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                RebuildActionList(propertyPath, parentContainer, emptyMessage);
            });
            removeBtn.text = "✕";
            removeBtn.style.width = 24;
            removeBtn.style.height = 20;
            removeBtn.style.color = new Color(1f, 0.4f, 0.4f);
            buttonContainer.Add(removeBtn);
            
            headerRow.Add(dragHandle);
            headerRow.Add(foldout);
            headerRow.Add(buttonContainer);
            effectBox.Add(headerRow);

            RegisterActionContextMenu(effectBox, propertyPath, index, parentContainer, emptyMessage);

            RegisterDropTarget(effectBox, propertyPath, index, parentContainer, emptyMessage);
            
            // Content (properties) - always create, foldout handles visibility
            if (actionObj != null)
            {
                var contentContainer = new VisualElement();
                contentContainer.style.marginLeft = 16;
                contentContainer.style.marginTop = 4;
                
                // Description preview label (created first so we can reference it in callbacks)
                Label previewLabel = null;
                if (actionObj is SkillAction)
                {
                    previewLabel = new Label();
                    previewLabel.style.marginTop = 8;
                    previewLabel.style.fontSize = 11;
                    previewLabel.style.color = new Color(0.7f, 0.9f, 0.7f);
                    previewLabel.style.whiteSpace = WhiteSpace.Normal;
                }
                
                // Helper to update preview text
                void UpdatePreview()
                {
                    if (previewLabel != null && elementProp.managedReferenceValue is SkillAction action)
                    {
                        string conditionsText = action.GetConditionsDescription();
                        var skill = serializedObject.targetObject as AgentSkill;
                        string effectText = action.GetDescription(skill);
                        
                        if (!string.IsNullOrEmpty(conditionsText))
                        {
                            previewLabel.text = $"Preview: {conditionsText} {effectText}";
                        }
                        else
                        {
                            previewLabel.text = "Preview: " + effectText;
                        }
                    }
                }
                
                // Draw all child properties
                var iterator = elementProp.Copy();
                var endProp = iterator.GetEndProperty();
                bool isMovementEffect = actionObj is MovementEffect;
                SerializedProperty movementTypeProp = null;
                PropertyField movementTypeField = null;
                PropertyField dashSpeedField = null;
                PropertyField leapHeightField = null;
                PropertyField homingDashField = null;
                PropertyField homingLeapField = null;
                PropertyField capDistanceField = null;

                bool isInvokeMethodAction = actionObj is InvokeMethodAction;
                SerializedProperty invokeTargetProp = null;
                PropertyField invokeTargetField = null;
                PropertyField targetObjectNameField = null;
                PropertyField targetTagField = null;
                PropertyField singletonTypeNameField = null;
                
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProp))
                {
                    enterChildren = false;
                    var propField = new PropertyField(iterator);
                    propField.Bind(serializedObject);
                    
                    // Register change callback to update preview immediately
                    propField.RegisterCallback<SerializedPropertyChangeEvent>(evt => 
                    {
                        UpdatePreview();
                    });
                    
                    if (isMovementEffect)
                    {
                        switch (iterator.name)
                        {
                            case "movementType":
                                movementTypeProp = iterator.Copy();
                                movementTypeField = propField;
                                break;
                            case "dashSpeed":
                                dashSpeedField = propField;
                                break;
                            case "leapHeight":
                                leapHeightField = propField;
                                break;
                            case "homingDash":
                                homingDashField = propField;
                                break;
                            case "homingLeap":
                                homingLeapField = propField;
                                break;
                            case "capToSampledTargetDistance":
                                capDistanceField = propField;
                                break;
                        }
                    }

                    if (isInvokeMethodAction)
                    {
                        switch (iterator.name)
                        {
                            case "target":
                                invokeTargetProp = iterator.Copy();
                                invokeTargetField = propField;
                                break;
                            case "targetObjectName":
                                targetObjectNameField = propField;
                                break;
                            case "targetTag":
                                targetTagField = propField;
                                break;
                            case "singletonTypeName":
                                singletonTypeNameField = propField;
                                break;
                        }
                    }
                    
                    contentContainer.Add(propField);
                }
                
                if (isMovementEffect && movementTypeProp != null)
                {
                    void UpdateMovementFields()
                    {
                        var type = (MovementType)movementTypeProp.enumValueIndex;
                        bool isDash = type == MovementType.Dash;
                        bool isLeap = type == MovementType.Leap;

                        if (dashSpeedField != null)
                            dashSpeedField.style.display = isDash ? DisplayStyle.Flex : DisplayStyle.None;
                        if (leapHeightField != null)
                            leapHeightField.style.display = isLeap ? DisplayStyle.Flex : DisplayStyle.None;
                        if (homingDashField != null)
                            homingDashField.style.display = isDash ? DisplayStyle.Flex : DisplayStyle.None;
                        if (homingLeapField != null)
                            homingLeapField.style.display = isLeap ? DisplayStyle.Flex : DisplayStyle.None;
                        if (capDistanceField != null)
                            capDistanceField.style.display = (isDash || isLeap) ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    
                    UpdateMovementFields();
                    if (movementTypeField != null)
                    {
                        movementTypeField.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                        {
                            UpdateMovementFields();
                        });
                    }
                }

                if (isInvokeMethodAction && invokeTargetProp != null)
                {
                    void UpdateInvokeMethodFields()
                    {
                        var selectedTarget = (MethodCallTarget)invokeTargetProp.enumValueIndex;

                        if (targetObjectNameField != null)
                        {
                            targetObjectNameField.style.display = selectedTarget == MethodCallTarget.TargetByName
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        if (targetTagField != null)
                        {
                            targetTagField.style.display = selectedTarget == MethodCallTarget.TargetByTag
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        if (singletonTypeNameField != null)
                        {
                            singletonTypeNameField.style.display = selectedTarget == MethodCallTarget.SingletonType
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }
                    }

                    UpdateInvokeMethodFields();
                    if (invokeTargetField != null)
                    {
                        invokeTargetField.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
                        {
                            UpdateInvokeMethodFields();
                        });
                    }
                }
                
                // Add preview label at the end
                if (previewLabel != null)
                {
                    UpdatePreview(); // Set initial text
                    contentContainer.Add(previewLabel);
                }
                
                foldout.Add(contentContainer);
                
                // Register foldout change to save state
                foldout.RegisterValueChangedCallback(evt => 
                {
                    elementProp.isExpanded = evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                });
            }
            else if (actionObj == null)
            {
                var warningLabel = new Label("Action is null - remove this entry");
                warningLabel.style.color = new Color(1f, 0.6f, 0.4f);
                warningLabel.style.marginLeft = 16;
                effectBox.Add(warningLabel);
            }
            
            return effectBox;
        }

        private void RegisterActionContextMenu(
            VisualElement targetElement,
            string propertyPath,
            int index,
            VisualElement parentContainer,
            string emptyMessage)
        {
            targetElement.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                bool canCopy = false;

                serializedObject.Update();
                var listProp = serializedObject.FindProperty(propertyPath);
                if (listProp != null && index >= 0 && index < listProp.arraySize)
                {
                    var elementProp = listProp.GetArrayElementAtIndex(index);
                    canCopy = elementProp.managedReferenceValue is SkillAction;
                }

                evt.menu.AppendAction(
                    "Copy Action",
                    _ =>
                    {
                        serializedObject.Update();
                        var list = serializedObject.FindProperty(propertyPath);
                        if (list == null || index < 0 || index >= list.arraySize)
                            return;

                        var element = list.GetArrayElementAtIndex(index);
                        if (element.managedReferenceValue is SkillAction action && TryCopyAction(action))
                        {
                            RebuildActionList(propertyPath, parentContainer, emptyMessage);
                        }
                    },
                    canCopy ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

                evt.menu.AppendAction(
                    "Paste Action Below",
                    _ => PasteAction(propertyPath, index + 1, parentContainer, emptyMessage),
                    CanPasteCopiedAction() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    "Delete Action",
                    _ =>
                    {
                        serializedObject.Update();
                        var list = serializedObject.FindProperty(propertyPath);
                        if (list == null || index < 0 || index >= list.arraySize)
                            return;

                        list.DeleteArrayElementAtIndex(index);
                        serializedObject.ApplyModifiedProperties();
                        RebuildActionList(propertyPath, parentContainer, emptyMessage);
                    });
            }));
        }
        
        private string GetActionIcon(string typeName) => typeName switch
        {
            "DamageEffect" => "[DMG]",
            "HealEffect" => "[HEAL]",
            "ApplyStatusEffect" => "[STATUS]",
            "KnockbackEffect" => "[KB]",
            "SummonEffect" => "[SUMMON]",
            "MovementEffect" => "[MOVE]",
            "PiercingEffect" => "[PIERCE]",
            "AOEOnImpactEffect" => "[AOE]",
            "ChargeEffect" => "[CHARGE]",
            "ProjectileArcEffect" => "[PROJ]",
            "SpawnVFXAction" => "[VFX]",
            "PlaySFXAction" => "[SFX]",
            "StopSFXAction" => "[SFX]",
            "WaitAction" => "[WAIT]",
            "ImpactPhysicsAction" => "[PHYS]",
            "RunGC2InstructionsAction" => "[GC2]",
            "InvokeMethodAction" => "[CALL]",
            "FlyEffect" => "[FLY]",
            _ => "-"
        };
        
        private void ShowAddActionMenu(string propertyPath, VisualElement container, string emptyMessage)
        {
            var menu = new GenericMenu();
            
            for (int i = 0; i < _actionTypes.Length; i++)
            {
                var type = _actionTypes[i];
                var label = _actionTypeMenuLabels[i];
                
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    serializedObject.Update();
                    var listProp = serializedObject.FindProperty(propertyPath);
                    int newIndex = listProp.arraySize;
                    listProp.InsertArrayElementAtIndex(newIndex);
                    var newElement = listProp.GetArrayElementAtIndex(newIndex);
                    newElement.managedReferenceValue = Activator.CreateInstance(type);
                    newElement.isExpanded = true;
                    serializedObject.ApplyModifiedProperties();
                    RebuildActionList(propertyPath, container, emptyMessage);
                });
            }
            
            if (_actionTypes.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No action types found"));
            }
            
            menu.ShowAsContext();
        }

        private static bool CanPasteCopiedAction()
        {
            return !string.IsNullOrEmpty(_copiedActionJson);
        }

        private static bool TryCopyAction(SkillAction action)
        {
            if (action == null)
                return false;

            var container = ScriptableObject.CreateInstance<SkillActionClipboardContainer>();
            try
            {
                container.action = action;
                _copiedActionJson = EditorJsonUtility.ToJson(container, false);
                return !string.IsNullOrEmpty(_copiedActionJson);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AgentSkillEditor] Failed to copy action: {e.Message}");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        private static bool TryGetPastedAction(out SkillAction action)
        {
            action = null;
            if (!CanPasteCopiedAction())
                return false;

            var container = ScriptableObject.CreateInstance<SkillActionClipboardContainer>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(_copiedActionJson, container);
                action = container.action;
                container.action = null;
                return action != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AgentSkillEditor] Failed to paste action: {e.Message}");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        private void PasteAction(string propertyPath, int insertIndex, VisualElement parentContainer, string emptyMessage)
        {
            if (!TryGetPastedAction(out SkillAction action))
            {
                Debug.LogWarning("[AgentSkillEditor] No copied action available to paste.");
                return;
            }

            serializedObject.Update();
            var listProp = serializedObject.FindProperty(propertyPath);
            if (listProp == null)
                return;

            int clampedIndex = Mathf.Clamp(insertIndex, 0, listProp.arraySize);
            listProp.InsertArrayElementAtIndex(clampedIndex);
            var elementProp = listProp.GetArrayElementAtIndex(clampedIndex);
            elementProp.managedReferenceValue = action;
            elementProp.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
            RebuildActionList(propertyPath, parentContainer, emptyMessage);
        }

        private void RegisterDragHandle(VisualElement dragHandle, string propertyPath, int index)
        {
            Vector2 mouseDownPosition = Vector2.zero;
            bool isPressed = false;
            bool dragStarted = false;

            dragHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                isPressed = true;
                dragStarted = false;
                mouseDownPosition = evt.localMousePosition;
                dragHandle.CaptureMouse();
                evt.StopPropagation();
            });

            dragHandle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!isPressed || dragStarted)
                    return;

                Vector2 delta = evt.localMousePosition - mouseDownPosition;
                if (delta.sqrMagnitude < 9f)
                    return;

                dragStarted = true;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.SetGenericData(ActionDragDataKey, new SkillActionDragData
                {
                    Owner = serializedObject.targetObject,
                    PropertyPath = propertyPath,
                    SourceIndex = index
                });
                DragAndDrop.StartDrag($"Skill Action {index + 1}");

                // Release capture once drag starts so drop targets receive events reliably.
                if (dragHandle.HasMouseCapture())
                    dragHandle.ReleaseMouse();
                isPressed = false;

                evt.StopPropagation();
            });

            dragHandle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                if (dragHandle.HasMouseCapture())
                    dragHandle.ReleaseMouse();

                isPressed = false;
                dragStarted = false;
                evt.StopPropagation();
            });

            dragHandle.RegisterCallback<MouseCaptureOutEvent>(_ =>
            {
                isPressed = false;
                dragStarted = false;
            });
        }

        private void RegisterDropTarget(
            VisualElement dropTarget,
            string propertyPath,
            int targetIndex,
            VisualElement parentContainer,
            string emptyMessage)
        {
            dropTarget.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!TryGetDragData(propertyPath, out _))
                    return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                SetDropTargetHighlight(dropTarget, true);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            dropTarget.RegisterCallback<DragLeaveEvent>(_ =>
            {
                SetDropTargetHighlight(dropTarget, false);
            });

            dropTarget.RegisterCallback<DragExitedEvent>(_ =>
            {
                SetDropTargetHighlight(dropTarget, false);
            });

            dropTarget.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!TryGetDragData(propertyPath, out SkillActionDragData dragData))
                    return;

                DragAndDrop.AcceptDrag();
                DragAndDrop.SetGenericData(ActionDragDataKey, null);
                SetDropTargetHighlight(dropTarget, false);
                MoveAction(propertyPath, dragData.SourceIndex, targetIndex, parentContainer, emptyMessage);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        private bool TryGetDragData(string propertyPath, out SkillActionDragData dragData)
        {
            dragData = DragAndDrop.GetGenericData(ActionDragDataKey) as SkillActionDragData;
            if (dragData == null)
                return false;

            if (!ReferenceEquals(dragData.Owner, serializedObject.targetObject))
                return false;

            return string.Equals(dragData.PropertyPath, propertyPath, StringComparison.Ordinal);
        }

        private void MoveAction(
            string propertyPath,
            int sourceIndex,
            int targetIndex,
            VisualElement parentContainer,
            string emptyMessage)
        {
            serializedObject.Update();
            var listProp = serializedObject.FindProperty(propertyPath);
            if (listProp == null || listProp.arraySize == 0)
                return;

            if (sourceIndex < 0 || sourceIndex >= listProp.arraySize)
                return;

            int clampedTargetIndex = Mathf.Clamp(targetIndex, 0, listProp.arraySize - 1);
            if (sourceIndex == clampedTargetIndex)
                return;

            listProp.MoveArrayElement(sourceIndex, clampedTargetIndex);
            serializedObject.ApplyModifiedProperties();
            RebuildActionList(propertyPath, parentContainer, emptyMessage);
        }

        private static void SetDropTargetHighlight(VisualElement element, bool highlighted)
        {
            float width = highlighted ? 2f : 0f;
            Color color = highlighted ? new Color(0.35f, 0.7f, 0.95f, 0.9f) : Color.clear;

            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }
        
        #endregion
        
        #region Visuals Helpers
        
        private VisualElement CreateProjectileVisualsFoldout()
        {
            var projectileSection = new Foldout { text = "Projectile Visuals", value = true };
            projectileSection.AddToClassList("section");
            
            AddPropertyField(projectileSection, "projectileArc.enabled", "Enable Projectile Visuals");
            AddValidatedField(projectileSection, "projectileArc.projectilePrefab", "Projectile Prefab", so =>
            {
                var targetingTypeProp = so.FindProperty("targetingType");
                bool isProjectileType = targetingTypeProp != null &&
                    targetingTypeProp.enumValueIndex == (int)SkillTargetingType.Projectile;
                
                var prefabProp = so.FindProperty("projectileArc.projectilePrefab");
                bool hasPrefab = prefabProp != null && prefabProp.objectReferenceValue != null;
                
                return isProjectileType
                    ? (hasPrefab ? ValidationStatus.RequiredComplete : ValidationStatus.RequiredIncomplete)
                    : (hasPrefab ? ValidationStatus.OptionalComplete : ValidationStatus.OptionalDisabled);
            }, "Required when using Projectile targeting type");
            
            AddPropertyField(projectileSection, "projectileArc.visualScale", "Visual Scale");
            AddPropertyField(projectileSection, "projectileArc.orientToVelocity", "Orient to Velocity");
            
            var arcSettingsFoldout = new Foldout { text = "Arc Settings", value = true };
            arcSettingsFoldout.style.marginLeft = 8;
            AddPropertyField(arcSettingsFoldout, "projectileArc.arcMode", "Arc Mode");
            AddPropertyField(arcSettingsFoldout, "projectileArc.launchAngle", "Launch Angle");
            AddPropertyField(arcSettingsFoldout, "projectileArc.flightTime", "Flight Time");
            AddPropertyField(arcSettingsFoldout, "projectileArc.arcHeight", "Arc Height");
            AddPropertyField(arcSettingsFoldout, "projectileArc.gravity", "Gravity");
            projectileSection.Add(arcSettingsFoldout);
            
            var linearSettingsFoldout = new Foldout { text = "Linear Settings", value = true };
            linearSettingsFoldout.style.marginLeft = 8;
            AddPropertyField(linearSettingsFoldout, "projectileArc.linearSpeed", "Linear Speed");
            projectileSection.Add(linearSettingsFoldout);
            
            var homingSettingsFoldout = new Foldout { text = "Homing Settings", value = true };
            homingSettingsFoldout.style.marginLeft = 8;
            AddPropertyField(homingSettingsFoldout, "projectileArc.homingTurnRate", "Turn Rate (°/s)");
            AddPropertyField(homingSettingsFoldout, "projectileArc.homingSpeed", "Initial Speed");
            AddPropertyField(homingSettingsFoldout, "projectileArc.homingAcceleration", "Acceleration");
            projectileSection.Add(homingSettingsFoldout);
            
            var projectileAudioFoldout = new Foldout { text = "Audio", value = false };
            projectileAudioFoldout.style.marginLeft = 8;
            AddPropertyField(projectileAudioFoldout, "projectileArc.launchSound", "Launch Sound");
            AddPropertyField(projectileAudioFoldout, "projectileArc.launchVolume", "Launch Volume");
            projectileSection.Add(projectileAudioFoldout);
            
            var spawnPointFoldout = new Foldout { text = "Spawn Point", value = false };
            spawnPointFoldout.style.marginLeft = 8;
            AddPropertyField(spawnPointFoldout, "projectileArc.spawnOffset", "Spawn Offset");
            AddPropertyField(spawnPointFoldout, "projectileArc.spawnDelay", "Spawn Delay");
            AddPropertyField(spawnPointFoldout, "projectileArc.spawnBoneName", "Spawn Bone Name");
            projectileSection.Add(spawnPointFoldout);
            
            var multiProjectileFoldout = new Foldout { text = "Multi-Projectile", value = false };
            multiProjectileFoldout.style.marginLeft = 8;
            AddPropertyField(multiProjectileFoldout, "projectileArc.projectileCount", "Projectile Count");
            AddPropertyField(multiProjectileFoldout, "projectileArc.spreadAngle", "Spread Angle");
            AddPropertyField(multiProjectileFoldout, "projectileArc.volleyDelay", "Volley Delay");
            projectileSection.Add(multiProjectileFoldout);
            
            var trajectoryHelp = new HelpBox("", HelpBoxMessageType.Info);
            UpdateProjectileArcHelp(trajectoryHelp, serializedObject);
            projectileSection.Add(trajectoryHelp);
            
            void UpdateTrajectoryFoldoutVisibility()
            {
                var trajectoryProp = serializedObject.FindProperty("projectileArc.trajectory");
                if (trajectoryProp == null) return;
                var trajectoryIndex = trajectoryProp.enumValueIndex;
                
                linearSettingsFoldout.style.display = trajectoryIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                arcSettingsFoldout.style.display = trajectoryIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;
                homingSettingsFoldout.style.display = trajectoryIndex == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            
            UpdateTrajectoryFoldoutVisibility();
            projectileSection.TrackPropertyValue(serializedObject.FindProperty("projectileArc.trajectory"), _ =>
            {
                UpdateProjectileArcHelp(trajectoryHelp, serializedObject);
                UpdateTrajectoryFoldoutVisibility();
            });
            
            return projectileSection;
        }
        
        /// <summary>
        /// Adds VFX configuration fields for a SkillVFXConfig property.
        /// </summary>
        private void AddVFXConfigFields(VisualElement parent, string propertyPath, string label)
        {
            var vfxFoldout = new Foldout { text = label, value = false };
            vfxFoldout.style.marginLeft = 8;
            
            // VFX Reference (main field)
            AddPropertyField(vfxFoldout, $"{propertyPath}.vfx", "VFX Reference");
            
            // Spawn Point Settings
            var spawnPointFoldout = new Foldout { text = "Spawn Point", value = false };
            spawnPointFoldout.style.marginLeft = 8;
            AddPropertyField(spawnPointFoldout, $"{propertyPath}.spawnPointMode", "Spawn Mode");
            AddPropertyField(spawnPointFoldout, $"{propertyPath}.spawnOffset", "Offset");
            AddPropertyField(spawnPointFoldout, $"{propertyPath}.boneName", "Bone Name");
            AddPropertyField(spawnPointFoldout, $"{propertyPath}.spawnTransform", "Transform");
            vfxFoldout.Add(spawnPointFoldout);
            
            // Timing Settings
            var timingFoldout = new Foldout { text = "Timing", value = false };
            timingFoldout.style.marginLeft = 8;
            AddPropertyField(timingFoldout, $"{propertyPath}.spawnDelay", "Spawn Delay");
            AddPropertyField(timingFoldout, $"{propertyPath}.durationOverride", "Duration Override (0 = auto)");
            vfxFoldout.Add(timingFoldout);
            
            // Transform Settings
            var transformFoldout = new Foldout { text = "Transform", value = false };
            transformFoldout.style.marginLeft = 8;
            AddPropertyField(transformFoldout, $"{propertyPath}.scale", "Scale");
            AddPropertyField(transformFoldout, $"{propertyPath}.attachToParent", "Attach to Parent");
            AddPropertyField(transformFoldout, $"{propertyPath}.inheritRotation", "Inherit Rotation");
            vfxFoldout.Add(transformFoldout);
            
            // Performance Settings
            var perfFoldout = new Foldout { text = "Performance", value = false };
            perfFoldout.style.marginLeft = 8;
            AddPropertyField(perfFoldout, $"{propertyPath}.maxActiveInstances", "Max Active Instances (0 = unlimited)");
            AddPropertyField(perfFoldout, $"{propertyPath}.maxCameraDistance", "Max Camera Distance");
            AddPropertyField(perfFoldout, $"{propertyPath}.lodDistance", "LOD Distance");
            vfxFoldout.Add(perfFoldout);
            
            parent.Add(vfxFoldout);
        }
        
        /// <summary>
        /// Adds SFX configuration fields for a SkillSFXConfig property.
        /// </summary>
        private void AddSFXConfigFields(VisualElement parent, string propertyPath, string label)
        {
            var sfxFoldout = new Foldout { text = label, value = false };
            sfxFoldout.style.marginLeft = 8;
            
            // Audio clip (main field)
            AddPropertyField(sfxFoldout, $"{propertyPath}.clip", "Audio Clip");
            
            // Volume & Pitch
            var volumeFoldout = new Foldout { text = "Volume & Pitch", value = false };
            volumeFoldout.style.marginLeft = 8;
            AddPropertyField(volumeFoldout, $"{propertyPath}.volume", "Volume");
            AddPropertyField(volumeFoldout, $"{propertyPath}.basePitch", "Base Pitch");
            AddPropertyField(volumeFoldout, $"{propertyPath}.pitchVariation", "Pitch Variation (±)");
            sfxFoldout.Add(volumeFoldout);
            
            // Spatialization
            var spatialFoldout = new Foldout { text = "Spatialization", value = false };
            spatialFoldout.style.marginLeft = 8;
            AddPropertyField(spatialFoldout, $"{propertyPath}.spatialBlend", "Spatial Blend (0=2D, 1=3D)");
            AddPropertyField(spatialFoldout, $"{propertyPath}.minDistance", "Min Distance");
            AddPropertyField(spatialFoldout, $"{propertyPath}.maxDistance", "Max Distance");
            sfxFoldout.Add(spatialFoldout);
            
            // Performance
            var perfFoldout = new Foldout { text = "Performance", value = false };
            perfFoldout.style.marginLeft = 8;
            AddPropertyField(perfFoldout, $"{propertyPath}.maxActiveInstances", "Max Active Instances (0 = unlimited)");
            AddPropertyField(perfFoldout, $"{propertyPath}.maxCameraDistance", "Max Camera Distance");
            AddPropertyField(perfFoldout, $"{propertyPath}.delay", "Delay");
            sfxFoldout.Add(perfFoldout);
            
            parent.Add(sfxFoldout);
        }
        
        /// <summary>
        /// Adds Aura VFX configuration fields for a SkillAuraVFXConfig property.
        /// </summary>
        private void AddAuraVFXConfigFields(VisualElement parent, string propertyPath, string label)
        {
            var auraFoldout = new Foldout { text = label, value = false };
            auraFoldout.style.marginLeft = 8;
            
            // VFX Reference (main field)
            AddPropertyField(auraFoldout, $"{propertyPath}.vfx", "VFX Reference");
            
            // Sizing Settings
            var sizingFoldout = new Foldout { text = "Sizing", value = true };
            sizingFoldout.style.marginLeft = 8;
            AddPropertyField(sizingFoldout, $"{propertyPath}.scaleToAOERadius", "Scale to AOE Radius");
            AddPropertyField(sizingFoldout, $"{propertyPath}.baseScale", "Base Scale");
            AddPropertyField(sizingFoldout, $"{propertyPath}.radiusOffset", "Radius Offset");
            auraFoldout.Add(sizingFoldout);
            
            // Positioning Settings
            var positionFoldout = new Foldout { text = "Positioning", value = false };
            positionFoldout.style.marginLeft = 8;
            AddPropertyField(positionFoldout, $"{propertyPath}.groundOffset", "Ground Offset");
            AddPropertyField(positionFoldout, $"{propertyPath}.snapToGround", "Snap to Ground");
            AddPropertyField(positionFoldout, $"{propertyPath}.groundLayers", "Ground Layers");
            auraFoldout.Add(positionFoldout);
            
            // Animation Settings
            var animFoldout = new Foldout { text = "Animation", value = false };
            animFoldout.style.marginLeft = 8;
            AddPropertyField(animFoldout, $"{propertyPath}.fadeInDuration", "Fade In Duration");
            AddPropertyField(animFoldout, $"{propertyPath}.fadeOutDuration", "Fade Out Duration");
            AddPropertyField(animFoldout, $"{propertyPath}.pulseIntensity", "Pulse Intensity");
            AddPropertyField(animFoldout, $"{propertyPath}.pulseSpeed", "Pulse Speed");
            auraFoldout.Add(animFoldout);
            
            // Rotation Settings
            var rotationFoldout = new Foldout { text = "Rotation", value = false };
            rotationFoldout.style.marginLeft = 8;
            AddPropertyField(rotationFoldout, $"{propertyPath}.rotationSpeed", "Rotation Speed (°/s)");
            auraFoldout.Add(rotationFoldout);
            
            // Performance Settings
            var perfFoldout = new Foldout { text = "Performance", value = false };
            perfFoldout.style.marginLeft = 8;
            AddPropertyField(perfFoldout, $"{propertyPath}.maxCameraDistance", "Max Camera Distance");
            AddPropertyField(perfFoldout, $"{propertyPath}.lodDistance", "LOD Distance");
            auraFoldout.Add(perfFoldout);
            
            parent.Add(auraFoldout);
        }
        
        #endregion
        
        #region AI Tab
        
        private VisualElement CreateAITab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Priority Section
            var prioritySection = new Foldout { text = "Skill Priority", value = true };
            prioritySection.AddToClassList("section");
            
            AddPropertyField(prioritySection, "priority", "Priority");
            
            var priorityHelp = new HelpBox(
                "Higher priority skills are used first when conditions are met.\n" +
                "Example: Auto-attack = 1, Strong skill = 5, Ultimate = 10",
                HelpBoxMessageType.Info);
            prioritySection.Add(priorityHelp);
            
            container.Add(prioritySection);
            
            // Conditions Section
            var conditionsSection = new Foldout { text = "Use Conditions", value = true };
            conditionsSection.AddToClassList("section");
            
            AddPropertyField(conditionsSection, "useConditions", "Conditions");
            AddPropertyField(conditionsSection, "useChance", "Use Chance (0-1)");
            
            var conditionsHelp = new HelpBox(
                "Select conditions that must be met for AI to use this skill.\n" +
                "'Always' means no conditions required.\n" +
                "'Use Chance' adds randomness (0.5 = 50% chance to use when available).",
                HelpBoxMessageType.Info);
            conditionsSection.Add(conditionsHelp);
            
            container.Add(conditionsSection);
            
            // Thresholds Section
            var thresholdsSection = new Foldout { text = "Condition Thresholds", value = false };
            thresholdsSection.AddToClassList("section");
            
            var selfHealthGroup = new VisualElement();
            selfHealthGroup.style.marginBottom = 8;
            var selfHealthLabel = new Label("Self Health Thresholds");
            selfHealthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            selfHealthGroup.Add(selfHealthLabel);
            AddPropertyField(selfHealthGroup, "selfHealthLowThreshold", "Low Threshold");
            AddPropertyField(selfHealthGroup, "selfHealthHighThreshold", "High Threshold");
            thresholdsSection.Add(selfHealthGroup);
            
            var targetHealthGroup = new VisualElement();
            targetHealthGroup.style.marginBottom = 8;
            var targetHealthLabel = new Label("Target Health Thresholds");
            targetHealthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            targetHealthGroup.Add(targetHealthLabel);
            AddPropertyField(targetHealthGroup, "targetHealthLowThreshold", "Low Threshold");
            AddPropertyField(targetHealthGroup, "targetHealthHighThreshold", "High Threshold");
            thresholdsSection.Add(targetHealthGroup);
            
            var allyHealthGroup = new VisualElement();
            allyHealthGroup.style.marginBottom = 8;
            var allyHealthLabel = new Label("Ally Health Threshold");
            allyHealthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            allyHealthGroup.Add(allyHealthLabel);
            AddPropertyField(allyHealthGroup, "allyHealthLowThreshold", "Low Threshold");
            thresholdsSection.Add(allyHealthGroup);
            
            AddPropertyField(thresholdsSection, "multipleEnemiesThreshold", "Multiple Enemies Count");
            
            container.Add(thresholdsSection);
            
            return container;
        }
        
        #endregion
        
        #region Scaling Tab
        
        private VisualElement CreateScalingTab()
        {
            var container = new ScrollView();
            container.AddToClassList("tab-content");
            
            // Overview Section
            var overviewSection = new Foldout { text = "Level Scaling Overview", value = true };
            overviewSection.AddToClassList("section");
            
            var overviewHelp = new HelpBox(
                "Configure how skill values scale with agent level (1-99).\n\n" +
                "Scaling Mode:\n" +
                "• None: No scaling, uses base values\n" +
                "• Local: Uses config defined on this skill\n" +
                "• Global: Uses config from EnemyCrowd (shared)\n\n" +
                "Formula Types:\n" +
                "• Linear: base + (level-1) × bonus\n" +
                "• Percentage: base × (1 + (level-1) × %/100)\n" +
                "• Exponential: base × rate^(level-1)\n" +
                "• StatBased: base + stat × multiplier",
                HelpBoxMessageType.Info);
            overviewSection.Add(overviewHelp);
            container.Add(overviewSection);
            
            // Scaling Mode Section
            var modeSection = new Foldout { text = "Scaling Mode", value = true };
            modeSection.AddToClassList("section");
            
            var scalingModeProp = serializedObject.FindProperty("scalingMode");
            
            // Local Scaling Config Section (only shown when mode is Local)
            var scalingSection = new Foldout { text = "Local Scaling Configuration", value = true };
            scalingSection.AddToClassList("section");
            
            if (scalingModeProp != null)
            {
                var modeField = new PropertyField(scalingModeProp, "Scaling Mode");
                modeField.Bind(serializedObject);
                modeSection.Add(modeField);
                
                var modeHelp = new HelpBox(
                    "• None: Skill uses raw base values with no scaling\n" +
                    "• Local: Configure scaling below (per-skill)\n" +
                    "• Global: Uses EnemyCrowd's Global Skill Scaling config",
                    HelpBoxMessageType.Info);
                modeSection.Add(modeHelp);
                
                // Update visibility based on current mode
                void UpdateLocalScalingVisibility()
                {
                    // SkillScalingMode: None = 0, Local = 1, Global = 2
                    bool isLocal = scalingModeProp.enumValueIndex == 1;
                    scalingSection.style.display = isLocal ? DisplayStyle.Flex : DisplayStyle.None;
                }
                
                // Set initial visibility
                UpdateLocalScalingVisibility();
                
                // Update when mode changes
                modeField.RegisterValueChangeCallback(evt => 
                {
                    serializedObject.Update();
                    UpdateLocalScalingVisibility();
                });
            }
            
            container.Add(modeSection);
            
            var localConfigHelp = new HelpBox(
                "This configuration is only used when Scaling Mode = 'Local'.\n" +
                "For global scaling, configure in EnemyCrowd → Combat → Level & Skill Scaling.",
                HelpBoxMessageType.Info);
            scalingSection.Add(localConfigHelp);
            
            var scalingConfigProp = serializedObject.FindProperty("scalingConfig");
            if (scalingConfigProp != null)
            {
                var scalingField = new PropertyField(scalingConfigProp);
                scalingField.Bind(serializedObject);
                scalingSection.Add(scalingField);
            }
            else
            {
                var errorBox = new HelpBox(
                    "scalingConfig property not found. Make sure AgentSkill has the field:\n" +
                    "[SerializeField] private SkillScalingConfig scalingConfig;",
                    HelpBoxMessageType.Error);
                scalingSection.Add(errorBox);
            }
            
            container.Add(scalingSection);
            
            // External Integration Section
            var integrationSection = new Foldout { text = "External Stat System Integration", value = false };
            integrationSection.AddToClassList("section");
            
            var integrationHelp = new HelpBox(
                "To use an external stat system (e.g., Game Creator 2):\n\n" +
                "1. Implement ISkillStatProvider interface\n" +
                "2. Register at game start:\n" +
                "   SkillStatProviderRegistry.RegisterProvider(yourProvider);\n\n" +
                "See GameCreator2Integration.cs example in:\n" +
                "Assets/Plugins/EnemyMasses/Runtime/Skills/Scaling/Examples/",
                HelpBoxMessageType.Info);
            integrationSection.Add(integrationHelp);
            
            container.Add(integrationSection);
            
            return container;
        }
        
        #endregion
        
        #region Helpers
        
        private void AddPropertyField(VisualElement parent, string propertyName, string label = null)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
            {
                var field = label != null ? new PropertyField(prop, label) : new PropertyField(prop);
                field.Bind(serializedObject);
                parent.Add(field);
            }
            else
            {
                Debug.LogWarning($"[AgentSkillEditor] Property not found: {propertyName}");
            }
        }
        
        /// <summary>
        /// Adds a validated property field with visual feedback.
        /// </summary>
        private VisualElement AddValidatedField(
            VisualElement parent,
            string propertyName,
            string label,
            Func<SerializedObject, ValidationStatus> validator,
            string helpTooltip = null)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginTop = 2;
            container.style.marginBottom = 2;
            
            // Validation status icon
            var status = validator(serializedObject);
            var iconLabel = new Label(EditorFieldValidation.GetStatusIcon(status));
            iconLabel.tooltip = GetValidationTooltip(status);
            iconLabel.style.marginRight = 4;
            iconLabel.style.fontSize = 10;
            container.Add(iconLabel);
            
            // Property field
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
            {
                var fieldContainer = new VisualElement();
                fieldContainer.style.flexGrow = 1;
                
                var field = new PropertyField(prop, label);
                field.Bind(serializedObject);
                
                if (!string.IsNullOrEmpty(helpTooltip))
                    field.tooltip = helpTooltip;
                
                fieldContainer.Add(field);

                void UpdateValidationVisual()
                {
                    serializedObject.Update();
                    var currentStatus = validator(serializedObject);
                    iconLabel.text = EditorFieldValidation.GetStatusIcon(currentStatus);
                    iconLabel.tooltip = GetValidationTooltip(currentStatus);
                    EditorFieldValidation.ApplyValidationStyle(fieldContainer, currentStatus);
                }

                // Initial style application
                UpdateValidationVisual();

                // Live updates while editing this field
                field.RegisterCallback<SerializedPropertyChangeEvent>(_ => UpdateValidationVisual());
                fieldContainer.TrackPropertyValue(prop, _ => UpdateValidationVisual());
                _validatedFieldRefreshers.Add(UpdateValidationVisual);
                
                container.Add(fieldContainer);
            }
            else
            {
                Debug.LogWarning($"[AgentSkillEditor] Property not found: {propertyName}");
            }
            
            parent.Add(container);
            return container;
        }
        
        private string GetValidationTooltip(ValidationStatus status)
        {
            return status switch
            {
                ValidationStatus.RequiredIncomplete => "Required field - please configure",
                ValidationStatus.RequiredComplete => "Required field - configured correctly",
                ValidationStatus.OptionalIncomplete => "Optional field - needs configuration if you want this feature",
                ValidationStatus.OptionalComplete => "Optional feature - configured correctly",
                ValidationStatus.OptionalDisabled => "Optional feature - not currently used",
                _ => ""
            };
        }
        
        /// <summary>
        /// Updates the Projectile Arc help box based on the selected trajectory type.
        /// </summary>
        private void UpdateProjectileArcHelp(HelpBox helpBox, SerializedObject so)
        {
            var trajectoryProp = so.FindProperty("projectileArc.trajectory");
            if (trajectoryProp == null)
            {
                helpBox.text = "Projectile Arc enables ranged skill attacks with server-authoritative networking.\n\n" +
                              "• Fully integrated with EnemyMassesProjectileSystem\n" +
                              "• Same mathematical formulas as EnemyCrowd Charge Attack\n" +
                              "• Automatic network sync for multiplayer";
                return;
            }
            
            var trajectory = (SkillProjectileTrajectory)trajectoryProp.enumValueIndex;
            
            switch (trajectory)
            {
                case SkillProjectileTrajectory.Arc:
                    helpBox.text = "🏹 Arc Trajectory (Parabolic)\n\n" +
                                  "Projectile follows a parabolic arc like a thrown grenade or catapult stone.\n\n" +
                                  "Arc Mode Options:\n" +
                                  "• Launch Angle: Realistic ballistics - 45° for max range\n" +
                                  "• Flight Time: Fixed arrival time (guaranteed hits)\n" +
                                  "• Fixed Height: Consistent visual arc height\n\n" +
                                  "Uses same math as EnemyCrowd Charge Attack projectiles:\n" +
                                  "v₀² = (g·x²) / (2·cos²(θ)·(x·tan(θ) - y))";
                    break;
                    
                case SkillProjectileTrajectory.Linear:
                    helpBox.text = "➡ Linear Trajectory\n\n" +
                                  "Projectile travels in a straight line like an arrow or bullet.\n\n" +
                                  "Best for:\n" +
                                  "• Arrows, bolts, bullets\n" +
                                  "• Magic missiles, energy beams\n" +
                                  "• Fast-traveling projectiles\n\n" +
                                  "Enable Piercing to hit multiple targets in a line.";
                    break;
                    
                case SkillProjectileTrajectory.Homing:
                    helpBox.text = "🔮 Homing Trajectory\n\n" +
                                  "Projectile tracks and follows the target.\n\n" +
                                  "Best for:\n" +
                                  "• Guided missiles\n" +
                                  "• Seeking magic orbs\n" +
                                  "• Tracking spells\n\n" +
                                  "Adjust Turn Rate for evasion difficulty.\n" +
                                  "Note: Currently uses linear fallback.";
                    break;
            }
        }
        
        /// <summary>
        /// Find all classes that inherit from SkillAction.
        /// </summary>
        private static void CacheActionTypes()
        {
            if (_actionTypes != null) return;
            
            var baseType = typeof(SkillAction);
            var types = new List<Type>();
            var names = new List<string>();
            var menuLabels = new List<string>();
            
            // Find all non-abstract classes that inherit from SkillAction
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type))
                        {
                            types.Add(type);
                            
                            // Format name nicely
                            string name = type.Name;
                            name = name switch
                            {
                                "FlyEffect" => "Fly (Legacy)",
                                _ => name.EndsWith("Effect") ? name.Substring(0, name.Length - 6)
                                   : name.EndsWith("Action") ? name.Substring(0, name.Length - 6)
                                   : name
                            };
                            names.Add(name);
                            
                            // Menu label with icon
                            string menuLabel = type.Name switch
                            {
                                "DamageEffect" => "Damage",
                                "HealEffect" => "Heal",
                                "ApplyStatusEffect" => "Apply Status Effect",
                                "KnockbackEffect" => "Knockback",
                                "SummonEffect" => "Summon",
                                "MovementEffect" => "Movement",
                                "PiercingEffect" => "Piercing (Line)",
                                "AOEOnImpactEffect" => "AOE on Impact (Explosion)",
                                "ChargeEffect" => "Charge Attack (All-in-One)",
                                "ProjectileArcEffect" => "Projectile Arc",
                                "SpawnVFXAction" => "Spawn VFX",
                                "PlaySFXAction" => "Play SFX",
                                "StopSFXAction" => "Stop SFX",
                                "WaitAction" => "Wait",
                                "StartOrbitalVFXAction" => "Start Orbital VFX",
                                "StopOrbitalVFXAction" => "Stop Orbital VFX",
                                "ImpactPhysicsAction" => "Impact Physics",
                                "RunGC2InstructionsAction" => "Run GC2 Instructions",
                                "InvokeMethodAction" => "Invoke Method",
                                "FlyEffect" => "Fly (Legacy)",
                                _ => name
                            };
                            menuLabels.Add(menuLabel);
                        }
                    }
                }
                catch { }
            }
            
            _actionTypes = types.ToArray();
            _actionTypeNames = names.ToArray();
            _actionTypeMenuLabels = menuLabels.ToArray();
        }
        
        #endregion
    }
    
    /// <summary>
    /// Property drawer for SkillAction - handles SerializeReference in lists.
    /// </summary>
    [CustomPropertyDrawer(typeof(SkillAction), true)]
    public class SkillActionDrawer : PropertyDrawer
    {
        private static Type[] _actionTypes;
        private static string[] _actionTypeNames;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            CacheActionTypes();

            var root = new VisualElement();
            root.style.marginTop = 2;
            root.style.marginBottom = 2;

            var options = new List<string> { "Select Action Type..." };
            if (_actionTypeNames != null && _actionTypeNames.Length > 0)
            {
                options.AddRange(_actionTypeNames);
            }

            int selectedIndex = 0;
            var actionObj = property.managedReferenceValue;
            if (actionObj != null)
            {
                Type actionType = actionObj.GetType();
                for (int i = 0; i < _actionTypes.Length; i++)
                {
                    if (_actionTypes[i] == actionType)
                    {
                        selectedIndex = i + 1;
                        break;
                    }
                }
            }

            var actionTypePopup = new PopupField<string>("Action Type", options, selectedIndex);
            root.Add(actionTypePopup);

            var content = new VisualElement();
            content.style.marginLeft = 8;
            root.Add(content);

            void RebuildContent()
            {
                content.Clear();

                if (property.managedReferenceValue == null)
                    return;

                bool isInvokeMethodAction = property.managedReferenceValue is InvokeMethodAction;
                SerializedProperty invokeTargetProp = null;
                PropertyField invokeTargetField = null;
                PropertyField targetObjectNameField = null;
                PropertyField targetTagField = null;
                PropertyField singletonTypeNameField = null;

                var iterator = property.Copy();
                var endProperty = iterator.GetEndProperty();

                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
                {
                    enterChildren = false;

                    SerializedProperty childProp = iterator.Copy();
                    var field = new PropertyField(childProp);
                    field.Bind(property.serializedObject);
                    content.Add(field);

                    if (isInvokeMethodAction)
                    {
                        switch (childProp.name)
                        {
                            case "target":
                                invokeTargetProp = childProp;
                                invokeTargetField = field;
                                break;
                            case "targetObjectName":
                                targetObjectNameField = field;
                                break;
                            case "targetTag":
                                targetTagField = field;
                                break;
                            case "singletonTypeName":
                                singletonTypeNameField = field;
                                break;
                        }
                    }
                }

                if (isInvokeMethodAction && invokeTargetProp != null)
                {
                    void UpdateInvokeMethodFields()
                    {
                        var selectedTarget = (MethodCallTarget)invokeTargetProp.enumValueIndex;

                        if (targetObjectNameField != null)
                        {
                            targetObjectNameField.style.display = selectedTarget == MethodCallTarget.TargetByName
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        if (targetTagField != null)
                        {
                            targetTagField.style.display = selectedTarget == MethodCallTarget.TargetByTag
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }

                        if (singletonTypeNameField != null)
                        {
                            singletonTypeNameField.style.display = selectedTarget == MethodCallTarget.SingletonType
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                        }
                    }

                    UpdateInvokeMethodFields();
                    if (invokeTargetField != null)
                    {
                        invokeTargetField.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
                        {
                            UpdateInvokeMethodFields();
                        });
                    }
                }
            }

            actionTypePopup.RegisterValueChangedCallback(_ =>
            {
                property.serializedObject.Update();

                int popupValueIndex = actionTypePopup.index;
                if (popupValueIndex <= 0)
                {
                    property.managedReferenceValue = null;
                }
                else if (popupValueIndex - 1 < _actionTypes.Length)
                {
                    property.managedReferenceValue = Activator.CreateInstance(_actionTypes[popupValueIndex - 1]);
                }

                property.serializedObject.ApplyModifiedProperties();
                property.serializedObject.Update();
                RebuildContent();
            });

            RebuildContent();
            return root;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            CacheActionTypes();
            
            EditorGUI.BeginProperty(position, label, property);
            
            var actionObj = property.managedReferenceValue;
            
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            
            // Type selector dropdown
            string currentTypeName = actionObj != null ? actionObj.GetType().Name : "Select Action Type...";
            if (currentTypeName.EndsWith("Effect"))
                currentTypeName = currentTypeName.Substring(0, currentTypeName.Length - 6);
            if (currentTypeName.EndsWith("Action"))
                currentTypeName = currentTypeName.Substring(0, currentTypeName.Length - 6);
            
            Rect dropdownRect = new Rect(position.x, position.y, position.width, lineHeight);
            
            if (EditorGUI.DropdownButton(dropdownRect, new GUIContent(currentTypeName), FocusType.Passive))
            {
                var menu = new GenericMenu();
                
                for (int i = 0; i < _actionTypes.Length; i++)
                {
                    var type = _actionTypes[i];
                    var name = _actionTypeNames[i];
                    bool isSelected = actionObj != null && actionObj.GetType() == type;
                    
                    menu.AddItem(new GUIContent(name), isSelected, () =>
                    {
                        property.serializedObject.Update();
                        property.managedReferenceValue = Activator.CreateInstance(type);
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
                
                menu.ShowAsContext();
            }
            
            // Draw child properties
            if (actionObj != null)
            {
                float yOffset = lineHeight + spacing;
                
                var iterator = property.Copy();
                var endProp = iterator.GetEndProperty();
                
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProp))
                {
                    enterChildren = false;
                    
                    float propHeight = EditorGUI.GetPropertyHeight(iterator, true);
                    Rect propRect = new Rect(position.x, position.y + yOffset, position.width, propHeight);
                    
                    EditorGUI.PropertyField(propRect, iterator, true);
                    yOffset += propHeight + spacing;
                }
            }
            
            EditorGUI.EndProperty();
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            
            var actionObj = property.managedReferenceValue;
            if (actionObj != null)
            {
                var iterator = property.Copy();
                var endProp = iterator.GetEndProperty();
                
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProp))
                {
                    enterChildren = false;
                    height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
                }
            }
            
            return height;
        }
        
        private static void CacheActionTypes()
        {
            if (_actionTypes != null) return;
            
            var baseType = typeof(SkillAction);
            var types = new List<Type>();
            var names = new List<string>();
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type))
                        {
                            types.Add(type);
                            string name = type.Name;
                            name = name switch
                            {
                                "FlyEffect" => "Fly (Legacy)",
                                _ => name.EndsWith("Effect") ? name.Substring(0, name.Length - 6)
                                   : name.EndsWith("Action") ? name.Substring(0, name.Length - 6)
                                   : name
                            };
                            names.Add(name);
                        }
                    }
                }
                catch { }
            }
            
            _actionTypes = types.ToArray();
            _actionTypeNames = names.ToArray();
        }
    }
}
