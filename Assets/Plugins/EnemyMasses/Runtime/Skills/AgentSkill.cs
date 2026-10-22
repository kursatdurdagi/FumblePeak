using System;
using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Localization;
using Arawn.EnemyMasses.Runtime.Skills.Scaling;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Controls whether a skill can be auto-cast and its initial toggle state.
    /// Configured per-skill on the AgentSkill ScriptableObject.
    /// </summary>
    public enum AutocastEligibility
    {
        [Tooltip("This skill cannot be auto-cast. The auto-cast toggle is hidden in the UI.\n" +
                 "Use for: Resurrection, Teleport, manually-targeted ultimates.")]
        NotEligible,
        
        [Tooltip("Auto-cast starts OFF. Players can toggle it on at runtime.\n" +
                 "Use for: Skills that are useful only in specific situations.")]
        DefaultOff,
        
        [Tooltip("Auto-cast starts ON. Players can toggle it off at runtime.\n" +
                 "Use for: Core combat skills you want the AI to use automatically.")]
        DefaultOn
    }

    /// <summary>
    /// Determines where skill scaling configuration comes from.
    /// </summary>
    public enum SkillScalingMode
    {
        /// <summary>No scaling - skill uses base values only</summary>
        None,
        /// <summary>Use local scaling config defined on this skill</summary>
        Local,
        /// <summary>Use global scaling config from EnemyCrowd</summary>
        Global
    }
    
    /// <summary>
    /// ScriptableObject defining an agent skill.
    /// Skills can be attacks, heals, buffs, summons, or any ability an agent can use.
    /// </summary>
    [CreateAssetMenu(fileName = "New Agent Skill", menuName = "Enemy Masses/Skills/Agent Skill")]
    [Icon("Assets/Plugins/EnemyMasses/Editor/Icons/skillIcon.png")]
    public class AgentSkill : ScriptableObject
    {
        #region Basic Info
        
        [Header("Basic Info")]
        [Tooltip("Display name of this skill.\n" +
                 "Example: 'Fireball', 'Shield Bash', 'Healing Wave'")]
        [SerializeField] private string skillName = "New Skill";
        
        [Tooltip("Description shown in tooltips and UI.")]
        [SerializeField, TextArea(2, 4)] private string description = "";

        [Tooltip("Optional Unity Localization reference for Skill Name display text.")]
        [SerializeField] private OptionalLocalizedString skillNameLocalization;

        [Tooltip("Optional Unity Localization reference for Description display text.")]
        [SerializeField] private OptionalLocalizedString descriptionLocalization;

        [Tooltip("Optional Unity Localization reference for overriding the full Skill Preview text block.")]
        [SerializeField] private OptionalLocalizedString skillPreviewLocalization;
        
        [Tooltip("Icon for UI display (skill bars, tooltips, etc.).")]
        [SerializeField] private Sprite icon;

        [Tooltip("Sound played when this skill is left-clicked on the Skill Bar UI.\n" +
                 "If null, the global Click Sound from AgentSkillBarUI is used instead.")]
        [SerializeField] private AudioClip skillBarClickSound;

        [Tooltip("Sound played when auto-cast is turned ON for this skill on the Skill Bar UI.\n" +
                 "If null, the global Auto-Cast On Sound from AgentSkillBarUI is used instead.")]
        [SerializeField] private AudioClip skillBarAutoCastOnSound;

        [Tooltip("Sound played when auto-cast is turned OFF for this skill on the Skill Bar UI.\n" +
                 "If null, the global Auto-Cast Off Sound from AgentSkillBarUI is used instead.")]
        [SerializeField] private AudioClip skillBarAutoCastOffSound;

        [Tooltip("Sound played when this skill's target is confirmed (e.g. right-click during targeting mode).\n" +
                 "If null, the global Confirm Sound from AgentSkillBarUI is used instead.")]
        [SerializeField] private AudioClip skillBarConfirmSound;

        [Tooltip("Controls whether this skill can be auto-cast via the Skill Bar UI.\n\n" +
                 "• Not Eligible: Cannot be auto-cast. Toggle is hidden in the UI.\n" +
                 "  Use for resurrection, teleport, manually-targeted ultimates.\n\n" +
                 "• Default Off: Auto-cast starts OFF. Players can toggle it on.\n" +
                 "  Use for situational skills (crowd control, dispels).\n\n" +
                 "• Default On: Auto-cast starts ON. Players can toggle it off.\n" +
                 "  Use for core combat skills (attack, heal, buff).")]
        [SerializeField] private AutocastEligibility autocastEligibility = AutocastEligibility.DefaultOff;
        
        [Tooltip("Category of this skill. Affects AI decision-making.\n\n" +
                 "• Attack: Offensive damage skill\n" +
                 "• Heal: Restores health\n" +
                 "• Buff: Positive effect on allies\n" +
                 "• Debuff: Negative effect on enemies\n" +
                 "• Summon: Creates additional units\n" +
                 "• Movement: Dash, teleport, leap\n" +
                 "• Utility: Everything else")]
        [SerializeField] private SkillType skillType = SkillType.Attack;

        [Tooltip("Determines whether this skill can damage flying agents.\n\n" +
                 "• Melee: Cannot hit flying agents at altitude (swords, spears).\n" +
                 "• Ranged: Cannot hit flying agents (low-arc bows, thrown weapons).\n" +
                 "• Ranged (Anti-Air): CAN hit flying agents (missiles, magic bolts, AA guns).")]
        [SerializeField] private AttackReachType attackReachType = AttackReachType.Melee;

        [Tooltip("Controls how many times this skill fires when multiple agents are selected.\n\n" +
                 "• Per Agent: Every selected agent casts the skill (default).\n" +
                 "  30 agents selected = 30 casts.\n\n" +
                 "• Single Cast: Only one agent casts the skill, no matter how many are selected.\n" +
                 "  Use for ultimates, global spells, summons, or expensive abilities.")]
        [SerializeField] private SkillActivationMode activationMode = SkillActivationMode.PerAgent;
        
        // Auto-generated ID (uses asset name if empty)
        private string _skillId;
        
        #endregion
        
        #region Targeting
        
        [Header("Targeting")]
        [Tooltip("How this skill selects its target(s).\n\n" +
                 "• Self: Affects only the caster\n" +
                 "• SingleEnemy/Ally: One target\n" +
                 "• ClosestEnemy/LowestHealthAlly: Auto-target\n" +
                 "• AreaOfEffect: All in radius\n" +
                 "• Cone/Line: Directional AOE\n" +
                 "• Projectile: Fires projectile, Effects apply on impact\n" +
                 "• Hitscan: Instant hit, no projectile travel")]
        [SerializeField] private SkillTargetingType targetingType = SkillTargetingType.SingleEnemy;
        
        [Tooltip("Configuration for Projectile targeting type.\n" +
                 "Defines trajectory, AOE on impact, and piercing behavior.\n" +
                 "Damage and effects come from the On Hit actions list.")]
        [SerializeField] private ProjectileTargetingConfig projectileTargeting = new ProjectileTargetingConfig();
        
        [Tooltip("Minimum range to use this skill.\n" +
                 "Example: Melee skill might require target within 3 units.\n" +
                 "Set to 0 for no minimum.")]
        [SerializeField] private float minRange = 0f;
        
        [Tooltip("Maximum range to use this skill.\n" +
                 "Example: Fireball might have 30 unit range.\n" +
                 "Set to 0 for unlimited (self-targeted skills).")]
        [SerializeField] private float maxRange = 10f;
        
        [Tooltip("Radius for AOE skills.\n" +
                 "Example: Fireball explosion radius of 5 units.")]
        [SerializeField] private float aoeRadius = 3f;
        
        [Tooltip("Angle in degrees for Cone targeting.\n" +
                 "Example: 90° cone for cleave attack.")]
        [SerializeField, Range(0f, 360f)] private float coneAngle = 60f;
        
        [Tooltip("Width for Line targeting.\n" +
                 "Example: 2 units wide for piercing shot.")]
        [SerializeField] private float lineWidth = 2f;
        
        [Tooltip("Maximum number of targets this skill can hit.\n" +
                 "0 = unlimited.\n" +
                 "Example: Chain Lightning hits max 5 targets.")]
        [SerializeField] private int maxTargets = 0;
        
        [Tooltip("Can this skill target dead units?\n" +
                 "Use for: Resurrect skills, corpse explosion.")]
        [SerializeField] private bool canTargetDead = false;
        
        #endregion
        
        #region Timing
        
        [Header("Timing")]
        [Tooltip("Cooldown in seconds before skill can be used again.\n" +
                 "Example: Basic attack = 1s, Ultimate = 60s")]
        [SerializeField] private float cooldown = 5f;
        
        [Tooltip("Duration in seconds that the skill remains active.\n" +
                 "0 = instant/no duration (behaves like current skills).\n" +
                 "Use for: Channels, auras, sustained effects.")]
        [SerializeField] private float duration = 0f;
        
        [Tooltip("Time in seconds to cast the skill before actions trigger.\n" +
                 "0 = instant cast.\n" +
                 "Example: Fireball has 0.5s cast time.")]
        [SerializeField] private float castTime = 0f;

        [Tooltip("If enabled, the caster stops moving while this skill is casting.\n" +
                 "Movement lock duration uses Cast Time.\n" +
                 "Ignored when Cast Time is 0.")]
        [SerializeField] private bool lockMovementDuringCast = false;
        
        [Tooltip("Can movement interrupt the cast?")]
        [SerializeField] private bool castInterruptedByMovement = true;
        
        [Tooltip("Can damage interrupt the cast?")]
        [SerializeField] private bool castInterruptedByDamage = false;
        
        [Tooltip("Cooldown mode:\n\n" +
                 "• PerAgent: Each agent tracks their own cooldown.\n" +
                 "  Use for: Normal abilities, auto-attacks.\n\n" +
                 "• PerEnemyCrowd: All agents in the same EnemyCrowd share the cooldown.\n" +
                 "  Use for: Crowd-specific abilities limited per EnemyCrowd type.\n\n" +
                 "• PerFaction: All agents of this faction share the cooldown.\n" +
                 "  Use for: Powerful abilities you don't want 100 orcs using at once.")]
        [SerializeField] private SkillCooldownMode cooldownMode = SkillCooldownMode.PerAgent;
        
        #endregion
        
        #region Critical Strike
        
        [Header("Critical Strike")]
        [Tooltip("Chance for this skill to critically hit (0-1).\n" +
                 "Example: 0.15 = 15% crit chance.")]
        [SerializeField, Range(0f, 1f)] private float criticalChance = 0f;
        
        [Tooltip("Damage multiplier when skill crits.\n" +
                 "Example: 2.0 = double damage on crit.")]
        [SerializeField] private float criticalMultiplier = 2f;
        
        #endregion
        
        #region Resource Cost
        
        [Header("Resource Cost (Optional)")]
        [Tooltip("Does this skill cost a resource to use?\n" +
                 "If your game doesn't use mana/energy, leave this off.")]
        [SerializeField] private bool hasResourceCost = false;
        
        [Tooltip("Name of the resource this costs.\n" +
                 "Example: 'Mana', 'Energy', 'Rage', 'Ammo'")]
        [SerializeField] private string resourceName = "Mana";
        
        [Tooltip("Amount of resource consumed when using this skill.")]
        [SerializeField] private float resourceCost = 10f;
        
        #endregion
        
        #region Actions (Phases)
        
        [Header("Actions - On Cast")]
        [Tooltip("Actions executed when the skill is cast (start phase).")]
        [SerializeReference] private List<SkillAction> onCastActions = new List<SkillAction>();
        
        [Header("Actions - On Trajectory Hit")]
        [Tooltip("Actions executed when a projectile crosses a target while traveling (pass-through hit).\n" +
                 "These are NOT continuous over time.\n" +
                 "For Projectile skills, having actions here also enables pass-through trajectory hits.\n" +
                 "Trajectory Piercing toggle can still force pass-through even with no actions.")]
        [SerializeReference] private List<SkillAction> onTrajectoryActions = new List<SkillAction>();

        [Header("Actions - During Flight (Timed)")]
        [Tooltip("Actions executed repeatedly while a projectile is still in flight.\n" +
                 "Useful for looped/travel audio, periodic VFX, and timed effects along the projectile path.\n" +
                 "Only used by Projectile targeting skills.")]
        [SerializeReference] private List<SkillAction> onFlightActions = new List<SkillAction>();

        [Tooltip("Interval in seconds between During Flight action executions.")]
        [SerializeField, Min(0.05f)] private float onFlightActionsInterval = 0.25f;

        [Tooltip("Delay in seconds before the first During Flight action tick.")]
        [SerializeField, Min(0f)] private float onFlightActionsStartDelay = 0f;

        [Tooltip("Maximum number of During Flight ticks per projectile.\n" +
                 "0 = unlimited (until projectile resolves).")]
        [SerializeField, Min(0)] private int onFlightActionsMaxTicks = 0;
        
        [Header("Actions - On Hit")]
        [Tooltip("Actions executed when the skill hits targets (impact phase).")]
        [SerializeReference] private List<SkillAction> onHitActions = new List<SkillAction>();
        
        [Header("Actions - On Finish")]
        [Tooltip("Actions executed when the skill finishes (end phase).")]
        [SerializeReference] private List<SkillAction> onFinishActions = new List<SkillAction>();
        
        #endregion
        
        #region Animation
        
        [Header("Animation")]
        [Tooltip("What animation to play when using this skill.\n\n" +
                 "• None: No animation\n" +
                 "• UseMeleeAnimation: Use faction's melee attack anim\n" +
                 "• UseRangedAnimation: Use faction's ranged attack anim\n" +
                 "• UseCastAnimation: Use faction's cast anim\n" +
                 "• CustomTrigger: Use a custom animation trigger name\n" +
                 "• SkillSpecificAnimation: Configure skill-specific animation")]
        [SerializeField] private SkillAnimationType animationType = SkillAnimationType.UseMeleeAnimation;
        
        [Tooltip("Custom animation trigger name (for CustomTrigger or SkillSpecificAnimation).\n" +
                 "This is sent to the animator via SetTrigger().\n" +
                 "Required for: Bone Tracker, Mecanim Reader workflows.")]
        [SerializeField] private string customAnimationTrigger = "";
        
        [Tooltip("Animation clip for this skill (for SkillSpecificAnimation).\n" +
                 "Required for: Compute Animator workflow.\n" +
                 "Recommended: Set both clip and trigger for runtime workflow switching.")]
        [SerializeField] private AnimationClip skillAnimationClip;
        
        [Tooltip("Override the animation duration.\n" +
                 "0 = auto-calculate from animation clip length.\n" +
                 "Only needed if no clip is assigned or you want a custom duration.")]
        [SerializeField] private float animationDurationOverride = 0f;
        
        #endregion
        
        #region VFX & SFX
        // VFX/SFX are configured via SkillActions in the phase lists.
        #endregion
        
        #region Projectile Arc
        
        [Header("Projectile Arc")]
        [Tooltip("Projectile configuration for ranged skill attacks.\n" +
                 "Supports arc (parabolic), linear, and homing trajectories.\n" +
                 "Uses server-authoritative networking.\n" +
                 "Examples: Fireballs, grenades, magic missiles, arrows.")]
        [SerializeField] private SkillProjectileConfig projectileArc = new SkillProjectileConfig();
        
        #endregion
        
        #region AI Behavior
        
        [Header("AI Behavior")]
        [Tooltip("Priority when AI chooses which skill to use.\n" +
                 "Higher = more likely to use when conditions are met.\n" +
                 "Example: Auto-attack = 1, Strong skill = 5, Ultimate = 10")]
        [SerializeField] private int priority = 1;
        
        [Tooltip("Conditions that must be met for AI to consider using this skill.\n" +
                 "Select multiple conditions - ALL must be true.\n\n" +
                 "Example: Heal skill with 'AllyHealthLow' won't be used if all allies are full HP.")]
        [SerializeField] private SkillUseCondition useConditions = SkillUseCondition.Always;
        
        [Tooltip("Health threshold for 'SelfHealthLow' condition (0-1).\n" +
                 "Example: 0.3 = use when below 30% HP.")]
        [SerializeField, Range(0f, 1f)] private float selfHealthLowThreshold = 0.3f;
        
        [Tooltip("Health threshold for 'SelfHealthHigh' condition (0-1).\n" +
                 "Example: 0.7 = use when above 70% HP.")]
        [SerializeField, Range(0f, 1f)] private float selfHealthHighThreshold = 0.7f;
        
        [Tooltip("Health threshold for 'AllyHealthLow' condition (0-1).")]
        [SerializeField, Range(0f, 1f)] private float allyHealthLowThreshold = 0.5f;
        
        [Tooltip("Health threshold for 'TargetHealthLow' condition (0-1).")]
        [SerializeField, Range(0f, 1f)] private float targetHealthLowThreshold = 0.25f;
        
        [Tooltip("Health threshold for 'TargetHealthHigh' condition (0-1).")]
        [SerializeField, Range(0f, 1f)] private float targetHealthHighThreshold = 0.75f;
        
        [Tooltip("Minimum enemies required for 'MultipleEnemiesNearby' condition.")]
        [SerializeField] private int multipleEnemiesThreshold = 3;
        
        [Tooltip("Random chance (0-1) to use skill even when conditions are met.\n" +
                 "1 = always use when available, 0.5 = 50% chance.\n" +
                 "Adds variety to AI behavior.")]
        [SerializeField, Range(0f, 1f)] private float useChance = 1f;
        
        #endregion
        
        #region Scaling System (Optional)
        
        [Header("Level/Stat Scaling (Optional)")]
        [Tooltip("Determines where scaling configuration comes from:\n\n" +
                 "• None: No scaling - uses base values\n" +
                 "• Local: Uses the scaling config defined below on this skill\n" +
                 "• Global: Uses scaling config from EnemyCrowd (shared across all skills)")]
        [SerializeField] private SkillScalingMode scalingMode = SkillScalingMode.None;
        
        [Tooltip("Local scaling configuration for this skill.\n" +
                 "Only used when Scaling Mode is set to 'Local'.\n\n" +
                 "Integration Options:\n" +
                 "• Built-in: Uses CrowdAgent.level property\n" +
                 "• External: Register ISkillStatProvider (e.g., Game Creator 2 Stats)")]
        [SerializeField] private SkillScalingConfig scalingConfig = new SkillScalingConfig();
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Unique ID for this skill. Uses asset name if not explicitly set.
        /// </summary>
        public string SkillId 
        {
            get
            {
                if (string.IsNullOrEmpty(_skillId))
                    _skillId = name; // ScriptableObject.name = asset file name
                return _skillId;
            }
        }
        
        public string SkillName => skillName;
        public string Description => description;
        public string LocalizedSkillName => GetLocalizedSkillName();
        public string LocalizedDescription => GetLocalizedDescription();
        public Sprite Icon => icon;
        public AudioClip SkillBarClickSound => skillBarClickSound;
        public AudioClip SkillBarAutoCastOnSound => skillBarAutoCastOnSound;
        public AudioClip SkillBarAutoCastOffSound => skillBarAutoCastOffSound;
        public AudioClip SkillBarConfirmSound => skillBarConfirmSound;
        public AutocastEligibility Autocast => autocastEligibility;
        public SkillType Type => skillType;
        public AttackReachType AttackReach => attackReachType;
        /// <summary>True when this skill can damage flying/airborne agents.</summary>
        public bool CanDamageFlying => attackReachType == AttackReachType.RangedAntiAir;
        public SkillActivationMode ActivationMode => activationMode;
        
        public SkillTargetingType TargetingType => targetingType;
        public ProjectileTargetingConfig ProjectileTargeting => projectileTargeting;
        public bool IsProjectileSkill => targetingType == SkillTargetingType.Projectile;
        public float MinRange => IsProjectileSkill ? projectileTargeting.minRange : minRange;
        public float MaxRange => IsProjectileSkill ? projectileTargeting.maxRange : maxRange;
        public float AOERadius => aoeRadius;
        public float ConeAngle => coneAngle;
        public float LineWidth => lineWidth;
        public int MaxTargets => maxTargets;
        public bool CanTargetDead => canTargetDead;
        
        public float Cooldown => cooldown;
        public float Duration => duration;
        public float CastTime => castTime;
        public bool LockMovementDuringCast => lockMovementDuringCast;
        public bool CastInterruptedByMovement => castInterruptedByMovement;
        public bool CastInterruptedByDamage => castInterruptedByDamage;
        public SkillCooldownMode CooldownMode => cooldownMode;
        
        public float CriticalChance => criticalChance;
        public float CriticalMultiplier => criticalMultiplier;
        
        public bool HasResourceCost => hasResourceCost;
        public string ResourceName => resourceName;
        public float ResourceCost => resourceCost;
        
        public IReadOnlyList<SkillAction> OnCastActions => onCastActions;
        public IReadOnlyList<SkillAction> OnTrajectoryActions => onTrajectoryActions;
        public IReadOnlyList<SkillAction> OnFlightActions => onFlightActions;
        public IReadOnlyList<SkillAction> OnHitActions => onHitActions;
        public IReadOnlyList<SkillAction> OnFinishActions => onFinishActions;
        public float OnFlightActionsInterval => onFlightActionsInterval;
        public float OnFlightActionsStartDelay => onFlightActionsStartDelay;
        public int OnFlightActionsMaxTicks => onFlightActionsMaxTicks;
        
        public SkillAnimationType AnimationType => animationType;
        public string CustomAnimationTrigger => customAnimationTrigger;
        public AnimationClip SkillAnimationClip => skillAnimationClip;
        
        /// <summary>
        /// Gets the effective animation duration.
        /// Auto-calculates from animation clip if no override is set.
        /// </summary>
        public float AnimationDuration
        {
            get
            {
                // Use override if set
                if (animationDurationOverride > 0f)
                    return animationDurationOverride;
                
                // Auto-calculate from clip if available
                if (skillAnimationClip != null)
                    return skillAnimationClip.length;
                
                // Default fallback
                return 0.5f;
            }
        }
        
        /// <summary>
        /// Gets the raw animation duration override value (0 = use clip length).
        /// </summary>
        public float AnimationDurationOverride => animationDurationOverride;

        // Projectile Arc
        public SkillProjectileConfig ProjectileArc => projectileArc;
        public bool HasProjectileArc => projectileArc != null && projectileArc.enabled;
        
        public int Priority => priority;
        public SkillUseCondition UseConditions => useConditions;
        public float SelfHealthLowThreshold => selfHealthLowThreshold;
        public float SelfHealthHighThreshold => selfHealthHighThreshold;
        public float AllyHealthLowThreshold => allyHealthLowThreshold;
        public float TargetHealthLowThreshold => targetHealthLowThreshold;
        public float TargetHealthHighThreshold => targetHealthHighThreshold;
        public int MultipleEnemiesThreshold => multipleEnemiesThreshold;
        public float UseChance => useChance;
        
        // Scaling System
        public SkillScalingMode ScalingMode => scalingMode;
        public SkillScalingConfig ScalingConfig => scalingConfig;
        public bool HasScaling => scalingMode != SkillScalingMode.None;
        public bool UsesLocalScaling => scalingMode == SkillScalingMode.Local;
        public bool UsesGlobalScaling => scalingMode == SkillScalingMode.Global;
        
        #endregion
        
        #region Methods

        /// <summary>
        /// Runtime-ready skill preview summary that mirrors the editor preview.
        /// Useful for HUD/tooltips without referencing editor code.
        /// </summary>
        public string SkillPreviewText => GetSkillPreviewText();
        public string LocalizedSkillPreviewText => GetSkillPreviewText();

        /// <summary>
        /// Resolves the localized skill name when configured, otherwise returns the raw skill name.
        /// </summary>
        public string GetLocalizedSkillName()
        {
            string sourceName = string.IsNullOrWhiteSpace(skillName) ? name : skillName;
            return skillNameLocalization.Resolve(sourceName);
        }

        /// <summary>
        /// Resolves the localized description when configured, otherwise returns the raw description.
        /// </summary>
        public string GetLocalizedDescription()
        {
            return descriptionLocalization.Resolve(description);
        }

        /// <summary>
        /// Builds a compact skill preview summary for runtime UI.
        /// Set richText=true if your UI text component supports rich text tags.
        /// </summary>
        public string GetSkillPreviewText(bool richText = false)
        {
            string generatedPreview = BuildSkillPreviewText(richText);
            return skillPreviewLocalization.Resolve(generatedPreview);
        }

        private string BuildSkillPreviewText(bool richText)
        {
            var sb = new System.Text.StringBuilder(768);
            string displayName = GetLocalizedSkillName();

            // === SKILL NAME & TYPE ===
            if (richText)
                sb.Append($"<b>{displayName}</b> ({skillType})");
            else
                sb.Append($"{displayName} ({skillType})");

            // === TIMING ===
            var timingParts = new List<string>(3);
            if (castTime > 0f)
                timingParts.Add($"{castTime:0.#}s cast");

            if (cooldown > 0f)
                timingParts.Add($"{cooldown:0.#}s CD");
            else
                timingParts.Add("No CD");

            if (timingParts.Count > 0)
                sb.Append($" • {string.Join(", ", timingParts)}");

            // === CRITICAL ===
            if (criticalChance > 0f)
                sb.Append($" • {criticalChance * 100f:0}% Crit (x{criticalMultiplier:0.#})");

            // === RESOURCE COST ===
            if (hasResourceCost)
            {
                string resourceLabel = string.IsNullOrWhiteSpace(resourceName) ? "Resource" : resourceName;
                sb.Append($" • {resourceCost:0} {resourceLabel}");
            }

            sb.AppendLine();

            // === TARGETING ===
            string targetingDescription = GetTargetingDescription(targetingType, MinRange, MaxRange);
            if (richText)
                sb.Append($"<color=#8AC>▸ {targetingDescription}</color>");
            else
                sb.Append($"▸ {targetingDescription}");

            switch (targetingType)
            {
                case SkillTargetingType.AOEAroundSelf:
                case SkillTargetingType.AOEAroundEnemy:
                case SkillTargetingType.AOEAroundAlly:
                case SkillTargetingType.GroundTargetedAOE:
                    sb.Append($" ({aoeRadius:0.#}m radius)");
                    break;
                case SkillTargetingType.Cone:
                    sb.Append($" ({coneAngle:0}° cone)");
                    break;
                case SkillTargetingType.Line:
                    sb.Append($" ({lineWidth:0.#}m wide)");
                    break;
                case SkillTargetingType.Projectile:
                    sb.Append($" ({projectileTargeting.trajectory})");
                    break;
                case SkillTargetingType.Hitscan:
                    sb.Append(" (Instant)");
                    break;
            }

            sb.AppendLine();

            // === ACTIONS ===
            bool hasAnyActions =
                HasPlayerVisibleActions(onCastActions) ||
                HasPlayerVisibleActions(onTrajectoryActions) ||
                HasPlayerVisibleActions(onFlightActions) ||
                HasPlayerVisibleActions(onHitActions) ||
                HasPlayerVisibleActions(onFinishActions);

            if (!hasAnyActions)
            {
                if (richText)
                    sb.Append("<color=#A88>▸ No actions configured</color>");
                else
                    sb.Append("▸ No actions configured");
                return sb.ToString();
            }

            AppendPreviewActionList(sb, "On Cast", onCastActions, richText);
            AppendPreviewActionList(sb, "On Trajectory Hit", onTrajectoryActions, richText);
            AppendPreviewActionList(sb, "During Flight (Timed)", onFlightActions, richText);
            AppendPreviewActionList(sb, "On Hit", onHitActions, richText);
            AppendPreviewActionList(sb, "On Finish", onFinishActions, richText);

            return sb.ToString();
        }

        /// <summary>
        /// Get a formatted description including all actions.
        /// </summary>
        public string GetFullDescription()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(GetLocalizedDescription());
            sb.AppendLine();
            AppendActionList(sb, "On Cast", onCastActions);
            AppendActionList(sb, "On Trajectory Hit", onTrajectoryActions);
            AppendActionList(sb, "During Flight (Timed)", onFlightActions);
            AppendActionList(sb, "On Hit", onHitActions);
            AppendActionList(sb, "On Finish", onFinishActions);
            
            if (cooldown > 0)
            {
                sb.AppendLine($"\nCooldown: {cooldown}s");
            }
            
            if (hasResourceCost)
            {
                sb.AppendLine($"Cost: {resourceCost} {resourceName}");
            }
            
            return sb.ToString();
        }

        private void AppendActionList(System.Text.StringBuilder sb, string label, List<SkillAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return;
            
            sb.AppendLine(label + ":");
            foreach (var action in actions)
            {
                if (action != null)
                {
                    sb.AppendLine($"• {action.GetDescription(this)}");
                }
            }
            sb.AppendLine();
        }

        private void AppendPreviewActionList(
            System.Text.StringBuilder sb,
            string label,
            List<SkillAction> actions,
            bool richText)
        {
            if (actions == null || actions.Count == 0)
                return;

            // Filter out implementation-detail actions that aren't relevant to players
            var playerActions = new List<SkillAction>();
            for (int i = 0; i < actions.Count; i++)
            {
                SkillAction action = actions[i];
                if (action == null) continue;
                if (IsImplementationDetailAction(action)) continue;
                playerActions.Add(action);
            }

            if (playerActions.Count == 0)
                return;

            sb.AppendLine();
            if (richText)
                sb.Append($"<color=#9D9>▸ {label}:</color>");
            else
                sb.Append($"▸ {label}:");

            int actionNumber = 0;
            for (int i = 0; i < playerActions.Count; i++)
            {
                SkillAction action = playerActions[i];

                // Tooltip comments render as plain descriptive text, not numbered entries
                if (action is TooltipCommentAction tooltipComment)
                {
                    string commentText = tooltipComment.GetDescription();
                    sb.AppendLine();
                    if (richText)
                        sb.Append($"   <i><color=#CCC>{commentText}</color></i>");
                    else
                        sb.Append($"   {commentText}");
                    continue;
                }

                actionNumber++;
                string conditionsText = action.GetConditionsDescription();
                string actionText = action.GetDescription(this);

                sb.AppendLine();
                if (!string.IsNullOrEmpty(conditionsText))
                {
                    if (richText)
                        sb.Append($"   {actionNumber}. <color=#AAA>{conditionsText}</color> {actionText}");
                    else
                        sb.Append($"   {actionNumber}. {conditionsText} {actionText}");
                }
                else
                {
                    sb.Append($"   {actionNumber}. {actionText}");
                }
            }
        }

        /// <summary>
        /// Returns true if the action list contains at least one player-visible action
        /// (i.e., not purely implementation details).
        /// </summary>
        private static bool HasPlayerVisibleActions(List<SkillAction> actions)
        {
            if (actions == null || actions.Count == 0) return false;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && !IsImplementationDetailAction(actions[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true for SkillActions that are implementation details (VFX, SFX, method invocations, waits)
        /// and should not be shown to players in UI tooltips.
        /// </summary>
        private static bool IsImplementationDetailAction(SkillAction action)
        {
            if (action is WaitAction) return true;
            if (action is SpawnVFXAction) return true;
            if (action is PlaySFXAction) return true;
            if (action is StopSFXAction) return true;
            if (action is StartOrbitalVFXAction) return true;
            if (action is StopOrbitalVFXAction) return true;
            if (action is InvokeMethodAction) return true;

            // RunGC2InstructionsAction may be conditionally compiled — check by type name
            string typeName = action.GetType().Name;
            if (typeName == "RunGC2InstructionsAction") return true;

            return false;
        }

        private static string GetTargetingDescription(SkillTargetingType type, float minRangeValue, float maxRangeValue)
        {
            string rangeText = minRangeValue > 0f
                ? $"Range: {minRangeValue:0.#}-{maxRangeValue:0.#}m"
                : $"Range: {maxRangeValue:0.#}m";

            return type switch
            {
                SkillTargetingType.SingleEnemy => $"Single Enemy • {rangeText}",
                SkillTargetingType.SingleAlly => $"Single Ally • {rangeText}",
                SkillTargetingType.Self => "Self-Cast",
                SkillTargetingType.ClosestEnemy => $"Closest Enemy • {rangeText}",
                SkillTargetingType.LowestHealthAlly => $"Lowest HP Ally • {rangeText}",
                SkillTargetingType.AOEAroundSelf => $"AOE Around Self • {rangeText}",
                SkillTargetingType.AOEAroundEnemy => $"AOE Around Enemy • {rangeText}",
                SkillTargetingType.AOEAroundAlly => $"AOE Around Ally • {rangeText}",
                SkillTargetingType.GroundTargetedAOE => $"Ground Target AOE • {rangeText}",
                SkillTargetingType.Cone => $"Cone Attack • {rangeText}",
                SkillTargetingType.Line => $"Line Attack • {rangeText}",
                SkillTargetingType.AllEnemiesInRange => $"All Enemies • {rangeText}",
                SkillTargetingType.AllAlliesInRange => $"All Allies • {rangeText}",
                SkillTargetingType.AllCrowdMembers => "All Crowd Members",
                SkillTargetingType.AllFactionMembers => "All Faction Members",
                SkillTargetingType.Projectile => $"Projectile • {rangeText}",
                SkillTargetingType.Hitscan => $"Hitscan • {rangeText}",
                SkillTargetingType.Chain => $"Chain Target • {rangeText}",
                _ => type.ToString()
            };
        }
        
        /// <summary>
        /// Check if an agent meets the conditions to use this skill.
        /// </summary>
        public bool CheckConditions(CrowdAgent caster, CrowdAgent target, EnemyMassesCrowdController controller)
        {
            if (useConditions == SkillUseCondition.None)
                return false;
            
            if (useConditions.HasFlag(SkillUseCondition.Always))
                return true;
            
            // Get max health from controller or fallback to base health
            float casterMaxHealth = controller?.GetAgentMaxHealth(caster.index) ?? caster.enemyCrowd?.agentBaseHealth ?? 100f;
            float casterHealthPercent = caster.health / casterMaxHealth;
            
            // Check self health conditions
            if (useConditions.HasFlag(SkillUseCondition.SelfHealthLow) && casterHealthPercent > selfHealthLowThreshold)
                return false;
            
            if (useConditions.HasFlag(SkillUseCondition.SelfHealthHigh) && casterHealthPercent < selfHealthHighThreshold)
                return false;
            
            // Check target health conditions
            if (target != null && target.enemyCrowd != null)
            {
                float targetMaxHealth = controller?.GetAgentMaxHealth(target.index) ?? target.enemyCrowd.agentBaseHealth;
                float targetHealthPercent = target.health / targetMaxHealth;
                
                if (useConditions.HasFlag(SkillUseCondition.TargetHealthLow) && targetHealthPercent > targetHealthLowThreshold)
                    return false;
                
                if (useConditions.HasFlag(SkillUseCondition.TargetHealthHigh) && targetHealthPercent < targetHealthHighThreshold)
                    return false;
            }
            
            // Check range conditions
            if (target != null)
            {
                float distance = Vector3.Distance(
                    caster.crowdInstance?.transform.position ?? Vector3.zero,
                    target.crowdInstance?.transform.position ?? Vector3.zero
                );
                
                if (useConditions.HasFlag(SkillUseCondition.InMeleeRange) && distance > 3f)
                    return false;
                
                if (useConditions.HasFlag(SkillUseCondition.AtRange) && distance <= 3f)
                    return false;
            }
            
            // Apply use chance
            if (useChance < 1f && UnityEngine.Random.value > useChance)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Check if a target is in range for this skill.
        /// </summary>
        public bool IsInRange(Vector3 casterPosition, Vector3 targetPosition)
        {
            float distance = Vector3.Distance(casterPosition, targetPosition);
            
            if (distance < minRange)
                return false;
            
            if (maxRange > 0 && distance > maxRange)
                return false;
            
            return true;
        }
        
        #endregion
        
        #region Scaling Methods
        
        /// <summary>
        /// Gets the effective scaling config based on scaling mode.
        /// Returns local config, global config from EnemyCrowd, or null.
        /// </summary>
        public SkillScalingConfig GetEffectiveScalingConfig(CrowdAgent caster)
        {
            switch (scalingMode)
            {
                case SkillScalingMode.Local:
                    return scalingConfig;
                    
                case SkillScalingMode.Global:
                    // Get global config from caster's EnemyCrowd
                    return caster?.GetGlobalScalingConfig();
                    
                case SkillScalingMode.None:
                default:
                    return null;
            }
        }
        
        /// <summary>
        /// Gets scaled damage for this skill based on caster level/stats.
        /// Returns base damage if scaling is disabled.
        /// </summary>
        /// <param name="baseDamage">The unscaled base damage value</param>
        /// <param name="caster">The agent casting the skill (for level/stat lookup)</param>
        public float GetScaledDamage(float baseDamage, CrowdAgent caster)
        {
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return baseDamage;
            int level = caster?.level ?? 1;
            return config.GetScaledDamage(baseDamage, level, caster);
        }
        
        /// <summary>
        /// Gets scaled healing for this skill based on caster level/stats.
        /// </summary>
        public float GetScaledHealing(float baseHealing, CrowdAgent caster)
        {
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return baseHealing;
            int level = caster?.level ?? 1;
            return config.GetScaledHealing(baseHealing, level, caster);
        }
        
        /// <summary>
        /// Gets scaled AOE radius for this skill.
        /// </summary>
        public float GetScaledRadius(float baseRadius, CrowdAgent caster)
        {
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return baseRadius;
            int level = caster?.level ?? 1;
            return config.GetScaledRadius(baseRadius, level, caster);
        }
        
        /// <summary>
        /// Gets scaled duration (for status effects, buffs, etc.).
        /// </summary>
        public float GetScaledDuration(float baseDuration, CrowdAgent caster)
        {
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return baseDuration;
            int level = caster?.level ?? 1;
            return config.GetScaledDuration(baseDuration, level, caster);
        }
        
        /// <summary>
        /// Gets scaled status effect strength (slow amount, DoT damage, etc.).
        /// </summary>
        public float GetScaledStatusEffect(float baseValue, CrowdAgent caster)
        {
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return baseValue;
            int level = caster?.level ?? 1;
            return config.GetScaledStatusEffect(baseValue, level, caster);
        }
        
        /// <summary>
        /// Gets scaled cooldown for this skill.
        /// Can decrease with level if configured with negative perLevelBonus.
        /// </summary>
        public float GetScaledCooldown(CrowdAgent caster)
        {
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return cooldown;
            int level = caster?.level ?? 1;
            return config.GetScaledCooldown(cooldown, level, caster);
        }
        
        /// <summary>
        /// Gets the scaled resource cost for this skill based on caster level/stats.
        /// Returns base <see cref="ResourceCost"/> if scaling is disabled.
        /// This is used by the resource providers to check and deduct the correct amount.
        /// </summary>
        public float GetScaledResourceCost(CrowdAgent caster)
        {
            if (!hasResourceCost) return 0f;
            var config = GetEffectiveScalingConfig(caster);
            if (config == null || !config.enableScaling) return resourceCost;
            int level = caster?.level ?? 1;
            return config.GetScaledResourceCost(resourceCost, level, caster);
        }
        
        /// <summary>
        /// Gets a scaling preview for UI/editor (without caster context).
        /// Only works with local scaling mode.
        /// </summary>
        public float PreviewScaledDamage(float baseDamage, int level)
        {
            if (scalingMode != SkillScalingMode.Local || scalingConfig == null) return baseDamage;
            return scalingConfig.GetScaledDamage(baseDamage, level, null);
        }
        
        #endregion
    }
}
