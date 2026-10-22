using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Controls how many times a skill fires when multiple agents are selected.
    /// </summary>
    public enum SkillActivationMode
    {
        [Tooltip("The skill is queued on every selected agent.\n" +
                 "30 agents selected = 30 casts.\n" +
                 "Use for: Auto-attacks, individual buffs, per-agent abilities.")]
        PerAgent,
        
        [Tooltip("The skill fires only once, regardless of how many agents are selected.\n" +
                 "The first valid agent in the selection casts it.\n" +
                 "Use for: Ultimates, global spells, summons, expensive abilities.")]
        SingleCast
    }
    
    /// <summary>
    /// The type/category of a skill. Used for AI decision-making and UI grouping.
    /// </summary>
    public enum SkillType
    {
        [Tooltip("Offensive skill that deals damage to enemies.")]
        Attack,
        
        [Tooltip("Restores health to self or allies.")]
        Heal,
        
        [Tooltip("Applies a positive effect to self or allies (damage boost, defense, speed, etc.).")]
        Buff,
        
        [Tooltip("Applies a negative effect to enemies (slow, weaken, blind, etc.).")]
        Debuff,
        
        [Tooltip("Creates additional units or objects.")]
        Summon,
        
        [Tooltip("Movement or positioning skill (dash, teleport, leap).")]
        Movement,
        
        [Tooltip("General utility (taunt, stealth, interrupt).")]
        Utility
    }
    
    /// <summary>
    /// Determines whether an attack can reach flying agents.
    /// Used by both AgentSkill and EnemyCrowd auto-attack to decide
    /// whether damage should be blocked against airborne targets.
    /// </summary>
    public enum AttackReachType
    {
        [Tooltip("Close-range attack. Cannot damage flying agents at altitude.\n" +
                 "Example: Sword slash, spear thrust, fists.")]
        Melee,
        
        [Tooltip("Ranged attack that cannot reach flying agents.\n" +
                 "Example: Low-arc bow, crossbow, thrown rock — unable to hit high-altitude targets.")]
        Ranged,
        
        [Tooltip("Ranged attack that CAN damage flying agents.\n" +
                 "Example: Anti-air turret, homing missile, high-velocity rifle, magic bolt.")]
        RangedAntiAir
    }
    
    /// <summary>
    /// How the skill selects its target(s).
    /// </summary>
    public enum SkillTargetingType
    {
        [Tooltip("Affects only the caster. Example: Self-heal, defensive stance.")]
        Self,
        
        [Tooltip("Targets a single enemy within range. Example: Fireball, melee strike.")]
        SingleEnemy,
        
        [Tooltip("Targets a single ally within range. Example: Heal, buff.")]
        SingleAlly,
        
        [Tooltip("Targets the closest enemy automatically. Example: Auto-attack.")]
        ClosestEnemy,
        
        [Tooltip("Targets the lowest-health ally automatically. Example: Smart heal.")]
        LowestHealthAlly,
        
        [Tooltip("Affects all units in a radius around the caster. Example: War cry, shockwave.")]
        AOEAroundSelf,
        
        [Tooltip("Affects all units in a radius around the target enemy. Example: Fireball explosion.")]
        AOEAroundEnemy,
        
        [Tooltip("Affects all units in a radius around the target ally. Example: Healing circle.")]
        AOEAroundAlly,
        
        [Tooltip("Affects all units in a radius around a ground point. Example: Artillery strike.")]
        GroundTargetedAOE,
        
        [Tooltip("Affects all units in a cone in front of the caster. Example: Flame breath, cleave.")]
        Cone,
        
        [Tooltip("Affects all units along a line. Example: Piercing shot.")]
        Line,
        
        [Tooltip("Chains from target to nearby targets. Example: Chain lightning.")]
        Chain,
        
        [Tooltip("Affects all allies within range. Example: War cry, aura.")]
        AllAlliesInRange,
        
        [Tooltip("Affects all enemies within range. Example: Intimidating shout.")]
        AllEnemiesInRange,
        
        [Tooltip("Affects ALL agents belonging to the same EnemyCrowd as the caster.\n" +
                 "Ignores range — crowd membership is the only filter.\n" +
                 "Pair with SingleCast + PerEnemyCrowd cooldown for crowd-wide buffs.\n" +
                 "Examples: Crowd Invulnerability, Mass Shield, Rally.")]
        AllCrowdMembers,
        
        [Tooltip("Affects ALL agents belonging to the same faction as the caster.\n" +
                 "Ignores range — faction membership is the only filter.\n" +
                 "Pair with SingleCast + PerFaction cooldown for faction-wide buffs.\n" +
                 "Examples: Faction Invulnerability, Global Rally, Mass Resurrection.")]
        AllFactionMembers,
        
        [Tooltip("Fires a projectile that applies effects on impact.\n" +
                 "Supports Linear, Arc, and Homing trajectories.\n" +
                 "Optional AOE on impact and piercing.\n" +
                 "Examples: Fireball, Arrow, Magic Missile.")]
        Projectile,
        
        [Tooltip("Instant ranged hit with no projectile travel.\n" +
                 "Applies effects immediately on the selected target.\n" +
                 "Examples: Rifle shot, laser beam, shotgun blast.")]
        Hitscan
    }
    
    /// <summary>
    /// Determines whether skill cooldowns are tracked per-agent or shared across the faction.
    /// </summary>
    public enum SkillCooldownMode
    {
        [Tooltip("Each agent tracks its own cooldown independently.\n\n" +
                 "Use for: Most combat skills, auto-attacks, individual abilities.\n" +
                 "Example: Each Orc Warrior can use 'Shield Bash' on their own timer.")]
        PerAgent = 0,
        
        [Tooltip("All agents belonging to the same EnemyCrowd asset share the cooldown.\n\n" +
                 "Use for: Crowd-specific abilities that should be limited per EnemyCrowd type.\n" +
                 "Example: Each Orc Archer crowd shares a single 'Volley' cooldown, " +
                 "but Orc Warriors have their own separate cooldown.")]
        PerEnemyCrowd = 2,

        [Tooltip("All agents of this faction share the same cooldown.\n\n" +
                 "Use for: Powerful faction abilities, summoning skills, global buffs.\n" +
                 "Example: Only one 'War Cry' can be active at a time across all Orc Warriors.\n" +
                 "Good for: Preventing spam of powerful abilities in large groups.")]
        PerFaction = 1
    }
    
    /// <summary>
    /// When the AI should consider using this skill.
    /// </summary>
    [Flags]
    public enum SkillUseCondition
    {
        None = 0,
        
        [Tooltip("Always consider using this skill when off cooldown.")]
        Always = 1 << 0,
        
        [Tooltip("Use when caster health is below threshold.")]
        SelfHealthLow = 1 << 1,
        
        [Tooltip("Use when caster health is above threshold (save heals for when needed).")]
        SelfHealthHigh = 1 << 2,
        
        [Tooltip("Use when an ally nearby has low health.")]
        AllyHealthLow = 1 << 3,
        
        [Tooltip("Use when multiple enemies are grouped together (for AOE).")]
        MultipleEnemiesNearby = 1 << 4,
        
        [Tooltip("Use when target is fleeing/moving away.")]
        TargetFleeing = 1 << 5,
        
        [Tooltip("Use when in melee range.")]
        InMeleeRange = 1 << 6,
        
        [Tooltip("Use when at range (not in melee).")]
        AtRange = 1 << 7,
        
        [Tooltip("Use when no allies have this buff active.")]
        BuffNotActive = 1 << 8,
        
        [Tooltip("Use at the start of combat engagement.")]
        CombatStart = 1 << 9,
        
        [Tooltip("Use when outnumbered.")]
        Outnumbered = 1 << 10,
        
        [Tooltip("Use when the target has high health (opener skill).")]
        TargetHealthHigh = 1 << 11,
        
        [Tooltip("Use when the target has low health (finisher skill).")]
        TargetHealthLow = 1 << 12
    }
    
    /// <summary>
    /// Level of detail for skill VFX based on camera distance.
    /// </summary>
    public enum SkillVFXLOD
    {
        [Tooltip("Always show full VFX regardless of distance.")]
        AlwaysShow,
        
        [Tooltip("Show VFX only when near the camera (half LOD distance).")]
        NearOnly,
        
        [Tooltip("Show VFX at medium distance (full LOD distance).")]
        MediumDistance,
        
        [Tooltip("Show VFX at far distance (2x LOD distance).")]
        FarDistance,
        
        [Tooltip("Never show VFX for this skill.")]
        NeverShow
    }
    
    /// <summary>
    /// The animation to play when using a skill.
    /// </summary>
    public enum SkillAnimationType
    {
        [Tooltip("No animation, skill is instant.")]
        None,
        
        [Tooltip("Use the faction's standard melee attack animation.")]
        UseMeleeAnimation,
        
        [Tooltip("Use the faction's standard ranged attack animation.")]
        UseRangedAnimation,
        
        [Tooltip("Use the faction's cast/channel animation.")]
        UseCastAnimation,
        
        [Tooltip("Use a custom animation trigger name (Mecanim/Bone Tracker).")]
        CustomTrigger,
        
        [Tooltip("Configure a skill-specific animation.\n" +
                 "• Compute Animator: Reference Animation Clip\n" +
                 "• Bone Tracker/Mecanim: Set Trigger Parameter\n" +
                 "• Both recommended for runtime workflow switching")]
        SkillSpecificAnimation
    }
    
    /// <summary>
    /// Type of visual indicator to display during ground-targeted AOE skill targeting.
    /// Shows the player where the AOE will land and its radius.
    /// </summary>
    public enum GroundTargetIndicatorType
    {
        [Tooltip("No ground indicator — only the targeting cursor is shown.")]
        None,

        [Tooltip("Auto-generated circle using a LineRenderer.\n" +
                 "Zero dependencies, works in all render pipelines.\n" +
                 "Best default choice — drawn at the AOE radius.")]
        RuntimeCircle,

        [Tooltip("Instantiate a custom prefab at the ground target position.\n" +
                 "Assign your own projector, particle system, VFX Graph, or any GameObject.\n" +
                 "The prefab is scaled uniformly to match the skill's AOE radius.")]
        CustomPrefab
    }
}
