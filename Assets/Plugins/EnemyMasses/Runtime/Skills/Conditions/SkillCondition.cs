using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills.Conditions
{
    /// <summary>
    /// Base class for all skill conditions. Derive from this to create custom conditions.
    /// Conditions determine whether a SkillEffect should execute.
    /// Similar to Game Creator 2's Condition system for maximum flexibility.
    /// </summary>
    [Serializable]
    public abstract class SkillCondition
    {
        [Tooltip("If enabled, this condition is inverted (NOT logic).")]
        [SerializeField] protected bool invert = false;
        
        /// <summary>
        /// Check if this condition is met.
        /// </summary>
        /// <param name="context">The skill execution context.</param>
        /// <returns>True if condition passes (after invert logic).</returns>
        public bool Check(SkillExecutionContext context)
        {
            bool result = Evaluate(context);
            return invert ? !result : result;
        }
        
        /// <summary>
        /// Override this to implement your condition logic.
        /// </summary>
        protected abstract bool Evaluate(SkillExecutionContext context);
        
        /// <summary>
        /// Get a human-readable description of this condition.
        /// </summary>
        public abstract string GetDescription();
        
        /// <summary>
        /// Get the title/name of this condition type for the editor.
        /// </summary>
        public virtual string Title => GetType().Name.Replace("Condition", "");
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // LEVEL CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if the caster's level meets a threshold.
    /// Level is read from EnemyCrowd.baseLevel.
    /// </summary>
    [Serializable]
    public class LevelCondition : SkillCondition
    {
        public enum CompareMode { GreaterOrEqual, LessOrEqual, Equal, Greater, Less }
        
        [Tooltip("How to compare the caster's level against the threshold.")]
        [SerializeField] private CompareMode comparison = CompareMode.GreaterOrEqual;
        
        [Tooltip("The level threshold to compare against.")]
        [SerializeField, Range(1, 99)] private int levelThreshold = 1;
        
        public int LevelThreshold => levelThreshold;
        public CompareMode Comparison => comparison;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            int casterLevel = context.Caster?.enemyCrowd?.baseLevel ?? 1;
            
            return comparison switch
            {
                CompareMode.GreaterOrEqual => casterLevel >= levelThreshold,
                CompareMode.LessOrEqual => casterLevel <= levelThreshold,
                CompareMode.Equal => casterLevel == levelThreshold,
                CompareMode.Greater => casterLevel > levelThreshold,
                CompareMode.Less => casterLevel < levelThreshold,
                _ => true
            };
        }
        
        public override string GetDescription()
        {
            string op = comparison switch
            {
                CompareMode.GreaterOrEqual => "≥",
                CompareMode.LessOrEqual => "≤",
                CompareMode.Equal => "=",
                CompareMode.Greater => ">",
                CompareMode.Less => "<",
                _ => "?"
            };
            string desc = $"Level {op} {levelThreshold}";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Level";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // HEALTH CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if a target's health percentage meets a threshold.
    /// </summary>
    [Serializable]
    public class HealthCondition : SkillCondition
    {
        public enum TargetType { Caster, PrimaryTarget }
        public enum CompareMode { GreaterOrEqual, LessOrEqual, Equal, Greater, Less }
        
        [Tooltip("Whose health to check.")]
        [SerializeField] private TargetType target = TargetType.Caster;
        
        [Tooltip("How to compare the health percentage.")]
        [SerializeField] private CompareMode comparison = CompareMode.GreaterOrEqual;
        
        [Tooltip("Health percentage threshold (0-1). 0.5 = 50% health.")]
        [SerializeField, Range(0f, 1f)] private float healthThreshold = 0.5f;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            CrowdAgent agent = target == TargetType.Caster ? context.Caster : context.PrimaryTarget;
            if (agent == null) return false;
            
            float maxHealth = context.Controller?.GetAgentMaxHealth(agent.index) ?? 
                             agent.enemyCrowd?.agentBaseHealth ?? 100f;
            float healthPercent = maxHealth > 0 ? agent.health / maxHealth : 0f;
            
            return comparison switch
            {
                CompareMode.GreaterOrEqual => healthPercent >= healthThreshold,
                CompareMode.LessOrEqual => healthPercent <= healthThreshold,
                CompareMode.Equal => Mathf.Approximately(healthPercent, healthThreshold),
                CompareMode.Greater => healthPercent > healthThreshold,
                CompareMode.Less => healthPercent < healthThreshold,
                _ => true
            };
        }
        
        public override string GetDescription()
        {
            string op = comparison switch
            {
                CompareMode.GreaterOrEqual => "≥",
                CompareMode.LessOrEqual => "≤",
                CompareMode.Equal => "=",
                CompareMode.Greater => ">",
                CompareMode.Less => "<",
                _ => "?"
            };
            string who = target == TargetType.Caster ? "Caster" : "Target";
            string desc = $"{who} HP {op} {healthThreshold * 100}%";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Health";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // STATUS EFFECT CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if a target has (or doesn't have) a specific status effect.
    /// Note: Stack checking is not currently supported - only presence/absence is checked.
    /// </summary>
    [Serializable]
    public class HasStatusCondition : SkillCondition
    {
        public enum TargetType { Caster, PrimaryTarget }
        
        [Tooltip("Whose status to check.")]
        [SerializeField] private TargetType target = TargetType.PrimaryTarget;
        
        [Tooltip("The status effect to check for.")]
        [SerializeField] private StatusEffectType statusEffect = StatusEffectType.Stun;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            CrowdAgent agent = target == TargetType.Caster ? context.Caster : context.PrimaryTarget;
            if (agent == null || context.Controller == null) return false;
            
            // Use the controller's HasStatusEffect method
            return context.Controller.HasStatusEffect(agent.index, statusEffect);
        }
        
        public override string GetDescription()
        {
            string who = target == TargetType.Caster ? "Caster" : "Target";
            string desc = $"{who} has {statusEffect}";
            return invert ? $"{who} doesn't have {statusEffect}" : desc;
        }
        
        public override string Title => "Has Status";
    }
    
    /// <summary>
    /// Checks if a target has ANY negative status effect (crowd control, damage-over-time, or debuff).
    /// Useful for triggering "finish him" abilities, CC-chains, or defensive reactions.
    /// </summary>
    [Serializable]
    public class HasAnyNegativeStatusCondition : SkillCondition
    {
        public enum TargetType { Caster, PrimaryTarget }
        
        [Tooltip("Whose status effects to check.")]
        [SerializeField] private TargetType target = TargetType.PrimaryTarget;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            CrowdAgent agent = target == TargetType.Caster ? context.Caster : context.PrimaryTarget;
            if (agent == null || agent.activeStatusEffects == null) return false;
            
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive &&
                    StatusEffectHelper.IsNegativeEffect(agent.activeStatusEffects[i].type))
                    return true;
            }
            return false;
        }
        
        public override string GetDescription()
        {
            string who = target == TargetType.Caster ? "Caster" : "Target";
            return invert ? $"{who} has no negative status" : $"{who} has any negative status";
        }
        
        public override string Title => "Has Any Negative Status";
    }
    
    /// <summary>
    /// Checks if a target has ANY positive status effect (buff or resist-buff).
    /// Useful for purge abilities, dispel conditions, or buff-dependent skill chains.
    /// </summary>
    [Serializable]
    public class HasAnyPositiveStatusCondition : SkillCondition
    {
        public enum TargetType { Caster, PrimaryTarget }
        
        [Tooltip("Whose status effects to check.")]
        [SerializeField] private TargetType target = TargetType.PrimaryTarget;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            CrowdAgent agent = target == TargetType.Caster ? context.Caster : context.PrimaryTarget;
            if (agent == null || agent.activeStatusEffects == null) return false;
            
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive &&
                    !StatusEffectHelper.IsNegativeEffect(agent.activeStatusEffects[i].type))
                    return true;
            }
            return false;
        }
        
        public override string GetDescription()
        {
            string who = target == TargetType.Caster ? "Caster" : "Target";
            return invert ? $"{who} has no positive status" : $"{who} has any positive status";
        }
        
        public override string Title => "Has Any Positive Status";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // DISTANCE CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if the distance to target meets a threshold.
    /// </summary>
    [Serializable]
    public class DistanceCondition : SkillCondition
    {
        public enum CompareMode { LessOrEqual, GreaterOrEqual, Less, Greater }
        
        [Tooltip("How to compare the distance.")]
        [SerializeField] private CompareMode comparison = CompareMode.LessOrEqual;
        
        [Tooltip("Distance threshold in units.")]
        [SerializeField] private float distanceThreshold = 5f;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            if (context.Caster == null || context.PrimaryTarget == null) return false;
            
            Vector3 casterPos = context.Caster.WorldPosition;
            Vector3 targetPos = context.PrimaryTarget.WorldPosition;
            
            float distance = Vector3.Distance(casterPos, targetPos);
            
            return comparison switch
            {
                CompareMode.LessOrEqual => distance <= distanceThreshold,
                CompareMode.GreaterOrEqual => distance >= distanceThreshold,
                CompareMode.Less => distance < distanceThreshold,
                CompareMode.Greater => distance > distanceThreshold,
                _ => true
            };
        }
        
        public override string GetDescription()
        {
            string op = comparison switch
            {
                CompareMode.LessOrEqual => "≤",
                CompareMode.GreaterOrEqual => "≥",
                CompareMode.Less => "<",
                CompareMode.Greater => ">",
                _ => "?"
            };
            string desc = $"Distance {op} {distanceThreshold}m";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Distance";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // RANDOM/CHANCE CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Random chance condition - passes based on probability.
    /// </summary>
    [Serializable]
    public class ChanceCondition : SkillCondition
    {
        [Tooltip("Probability of this condition passing (0-1). 0.3 = 30% chance.")]
        [SerializeField, Range(0f, 1f)] private float chance = 0.5f;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            return UnityEngine.Random.value <= chance;
        }
        
        public override string GetDescription()
        {
            string desc = $"{chance * 100}% chance";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Chance";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TARGET COUNT CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if the number of targets meets a threshold.
    /// Useful for "only trigger AOE if 3+ enemies are nearby" type effects.
    /// </summary>
    [Serializable]
    public class TargetCountCondition : SkillCondition
    {
        public enum CompareMode { GreaterOrEqual, LessOrEqual, Equal, Greater, Less }
        
        [Tooltip("How to compare the target count.")]
        [SerializeField] private CompareMode comparison = CompareMode.GreaterOrEqual;
        
        [Tooltip("Number of targets to compare against.")]
        [SerializeField, Range(1, 50)] private int targetCount = 3;
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            int count = context.AllTargets?.Length ?? (context.PrimaryTarget != null ? 1 : 0);
            
            return comparison switch
            {
                CompareMode.GreaterOrEqual => count >= targetCount,
                CompareMode.LessOrEqual => count <= targetCount,
                CompareMode.Equal => count == targetCount,
                CompareMode.Greater => count > targetCount,
                CompareMode.Less => count < targetCount,
                _ => true
            };
        }
        
        public override string GetDescription()
        {
            string op = comparison switch
            {
                CompareMode.GreaterOrEqual => "≥",
                CompareMode.LessOrEqual => "≤",
                CompareMode.Equal => "=",
                CompareMode.Greater => ">",
                CompareMode.Less => "<",
                _ => "?"
            };
            string desc = $"Target count {op} {targetCount}";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Target Count";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CRITICAL HIT CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if the skill execution is a critical hit.
    /// Useful for "bonus effect on crit" mechanics.
    /// </summary>
    [Serializable]
    public class IsCriticalCondition : SkillCondition
    {
        protected override bool Evaluate(SkillExecutionContext context)
        {
            return context.IsCritical;
        }
        
        public override string GetDescription()
        {
            return invert ? "Not a critical hit" : "Is critical hit";
        }
        
        public override string Title => "Is Critical";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FACTION CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Checks if the caster is from a specific faction.
    /// </summary>
    [Serializable]
    public class CasterFactionCondition : SkillCondition
    {
        [Tooltip("The faction name to check against.")]
        [SerializeField] private string factionName = "";
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            string casterFaction = context.Caster?.enemyCrowd?.factionName ?? "";
            return string.Equals(casterFaction, factionName, StringComparison.OrdinalIgnoreCase);
        }
        
        public override string GetDescription()
        {
            string desc = $"Caster Faction = \"{factionName}\"";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Caster Faction";
    }
    
    /// <summary>
    /// Checks if the target is from a specific faction.
    /// Useful for faction-specific counters or special attacks against certain enemies.
    /// </summary>
    [Serializable]
    public class TargetFactionCondition : SkillCondition
    {
        [Tooltip("The faction name to check against.")]
        [SerializeField] private string factionName = "";
        
        protected override bool Evaluate(SkillExecutionContext context)
        {
            string targetFaction = context.PrimaryTarget?.enemyCrowd?.factionName ?? "";
            return string.Equals(targetFaction, factionName, StringComparison.OrdinalIgnoreCase);
        }
        
        public override string GetDescription()
        {
            string desc = $"Target Faction = \"{factionName}\"";
            return invert ? $"NOT ({desc})" : desc;
        }
        
        public override string Title => "Target Faction";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ALWAYS/NEVER CONDITIONS (for testing/debugging)
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Always passes. Useful as a placeholder or for testing.
    /// </summary>
    [Serializable]
    public class AlwaysCondition : SkillCondition
    {
        protected override bool Evaluate(SkillExecutionContext context) => true;
        
        public override string GetDescription() => invert ? "Never" : "Always";
        
        public override string Title => "Always";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FLIGHT CONDITIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if an agent is currently in flight mode (<see cref="CrowdAgent.isFlying"/>).
    /// Returns true when the agent is airborne (including take-off and landing phases).
    /// Useful for gating skills that should only work while flying (e.g. aerial attacks)
    /// or only on the ground (e.g. charge abilities).
    /// </summary>
    [Serializable]
    public class IsFlyingCondition : SkillCondition
    {
        public enum TargetType { Caster, PrimaryTarget }

        [Tooltip("Whose flight state to check.")]
        [SerializeField] private TargetType target = TargetType.Caster;

        protected override bool Evaluate(SkillExecutionContext context)
        {
            CrowdAgent agent = target == TargetType.Caster ? context.Caster : context.PrimaryTarget;
            if (agent == null) return false;
            return agent.isFlying;
        }

        public override string GetDescription()
        {
            string who = target == TargetType.Caster ? "Caster" : "Target";
            return invert ? $"{who} is not flying" : $"{who} is flying";
        }

        public override string Title => "Is Flying";
    }
}
