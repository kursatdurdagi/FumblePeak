using System;
using UnityEngine;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Damageables;
using Networking = Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Factions;
using Arawn.EnemyMasses.Runtime.Skills.Conditions;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Base class for all skill actions. Derive from this to create custom actions.
    /// Actions are what actually happens when a skill phase executes (damage, heal, VFX, SFX, etc.).
    /// Supports conditions for dynamic activation (like Game Creator 2's Instruction system).
    /// </summary>
    [Serializable]
    public abstract class SkillAction
    {
        [Tooltip("Conditions that must be met for this action to execute.\n" +
                 "If empty, the action always executes.\n" +
                 "Use for: Level-gated features, conditional bonuses, chance-based effects.")]
        [SerializeField] protected SkillConditionList conditions = new SkillConditionList();
        
        [Tooltip("Delay in seconds before this action triggers after skill cast.\n" +
                 "Use for: Multi-hit skills, delayed explosions, channel effects.")]
        [SerializeField] protected float delay = 0f;

        [Tooltip("If enabled, later actions in the same phase won't execute before this action reaches its execution time.\n" +
                 "Useful for sequencing (for example: delayed AOE before VFX).")]
        [SerializeField] protected bool waitForCompletionBeforeNextAction = false;
        
        /// <summary>
        /// The conditions that must pass for this effect to execute.
        /// </summary>
        public SkillConditionList Conditions => conditions;
        
        /// <summary>
        /// Check if all conditions are met for this effect to execute.
        /// </summary>
        public bool CheckConditions(SkillExecutionContext context)
        {
            return conditions == null || conditions.Check(context);
        }
        
        /// <summary>
        /// Helper method to check if two agents are enemies.
        /// Uses the FactionSystemManager if available, otherwise falls back to faction name comparison.
        /// </summary>
        protected static bool AreAgentsEnemies(CrowdAgent agentA, CrowdAgent agentB)
        {
            if (agentA == null || agentB == null)
                return false;
                
            // Use the faction system if available
            var factionSystem = FactionSystemManager.Instance;
            if (factionSystem != null)
            {
                return factionSystem.GetRelationship(agentA, agentB) == RelationshipType.Enemy;
            }
            
            // Fallback to faction name comparison (different factions = enemies)
            string factionA = agentA.enemyCrowd?.factionName ?? "";
            string factionB = agentB.enemyCrowd?.factionName ?? "";
            return factionA != factionB;
        }
        
        /// <summary>
        /// Execute this effect on the target(s).
        /// Called by SkillExecutor after conditions are checked.
        /// </summary>
        /// <param name="context">All information about the skill execution.</param>
        public abstract void Execute(SkillExecutionContext context);
        
        /// <summary>
        /// Get a description of this effect for UI display.
        /// </summary>
        public abstract string GetDescription();
        
        /// <summary>
        /// Get a description of this effect with skill context for UI display.
        /// Override this in effects that need skill data (like ChargeEffect using skill range).
        /// </summary>
        /// <param name="skill">The skill this effect belongs to (can be null in editor before assignment).</param>
        public virtual string GetDescription(AgentSkill skill) => GetDescription();
        
        /// <summary>
        /// Get the conditions description for UI display.
        /// </summary>
        public string GetConditionsDescription()
        {
            if (conditions == null || conditions.Count == 0)
                return "";
            return $"[IF: {conditions.GetDescription()}]";
        }
        
        /// <summary>
        /// The delay before this action triggers.
        /// </summary>
        public float Delay => delay;
        
        /// <summary>
        /// Total delay before execution (base delay + action-specific delay).
        /// </summary>
        public virtual float GetTotalDelay() => delay;

        /// <summary>
        /// When true, the action timeline is blocked until this action executes.
        /// </summary>
        public bool WaitForCompletionBeforeNextAction => waitForCompletionBeforeNextAction;

        /// <summary>
        /// Whether this action should only execute on authoritative server simulation.
        /// Override for actions that intentionally run outside strict simulation authority.
        /// </summary>
        public virtual bool RequiresServerAuthority => true;

        /// <summary>
        /// Temporarily strips invulnerability from an agent so the next damage call bypasses it.
        /// Returns the original value — caller MUST restore via <see cref="RestoreInvulnerable"/>.
        /// </summary>
        protected static bool SuppressInvulnerable(CrowdAgent agent)
        {
            if (agent == null) return false;
            bool was = agent.isInvulnerable;
            if (was) agent.isInvulnerable = false;
            return was;
        }

        /// <summary>
        /// Restores the invulnerable flag saved by <see cref="SuppressInvulnerable"/>.
        /// </summary>
        protected static void RestoreInvulnerable(CrowdAgent agent, bool previousValue)
        {
            if (agent != null && previousValue)
                agent.SetInvulnerableAuthoritative(true);
        }
    }

    /// <summary>
    /// Base class for all skill effects. Derive from this to create custom effects.
    /// Effects are what actually happens when a skill is executed (damage, heal, buff, etc.).
    /// </summary>
    [Serializable]
    public abstract class SkillEffect : SkillAction
    {
    }
    
    /// <summary>
    /// Context passed to skill actions during execution.
    /// Contains all information about the caster, target(s), and skill.
    /// </summary>
    public class SkillExecutionContext
    {
        /// <summary>The agent using the skill.</summary>
        public CrowdAgent Caster { get; set; }
        
        /// <summary>The primary target (for single-target skills).</summary>
        public CrowdAgent PrimaryTarget { get; set; }
        
        /// <summary>All targets affected by this skill (for AOE).</summary>
        public CrowdAgent[] AllTargets { get; set; }
        
        /// <summary>World position where the skill was targeted (for ground-targeted skills).</summary>
        public Vector3 TargetPosition { get; set; }
        
        /// <summary>The skill being executed.</summary>
        public AgentSkill Skill { get; set; }
        
        /// <summary>The controller managing these agents.</summary>
        public EnemyMassesCrowdController Controller { get; set; }
        
        /// <summary>Whether this execution was a critical hit.</summary>
        public bool IsCritical { get; set; }
        
        /// <summary>Multiplier for effect strength (from buffs, etc.).</summary>
        public float EffectMultiplier { get; set; } = 1f;

        /// <summary>Unique ID for this skill execution instance.</summary>
        public int ExecutionId { get; set; }

        /// <summary>Projectile network/runtime ID for projectile-phase callbacks (0 if not a projectile context).</summary>
        public int NetworkProjectileId { get; set; }

        /// <summary>
        /// Optional non-agent damageable target (e.g. <see cref="EnemyMassesDamageableObject"/>).
        /// When set, skill actions that deal damage should apply damage to this object
        /// even when <see cref="AllTargets"/> is empty.
        /// </summary>
        public IEnemyMassesDamageable DamageableTarget { get; set; }

        /// <summary>
        /// Fallback transform for the caster when <see cref="Caster"/> is <c>null</c>.
        /// Used by <see cref="StandaloneSkillCaster"/> so projectile skills can resolve
        /// the spawn position without a <see cref="CrowdAgent"/>.
        /// </summary>
        public Transform CasterTransform { get; set; }
    }

    /// <summary>
    /// Common spawn locations for skill actions (VFX/SFX).
    /// </summary>
    public enum SkillActionSpawnLocation
    {
        Caster,
        Target,
        TargetGround,
        TargetPosition,
        Midpoint
    }

    /// <summary>
    /// Defines when a looping PlaySFXAction should be automatically stopped.
    /// </summary>
    public enum SkillSFXLoopStopPolicy
    {
        OnHit,
        OnFinish,
        OnProjectileDespawn,
        Manual
    }

    /// <summary>
    /// Adds a sequencing pause before subsequent actions in the same phase execute.
    /// </summary>
    [Serializable]
    public class WaitAction : SkillAction
    {
        [Header("Wait")]
        [Tooltip("Additional wait time in seconds added to actions that come after this in the same phase.")]
        [SerializeField, Min(0f)] private float duration = 0.25f;

        /// <summary>
        /// Wait duration in seconds, excluding the base action delay field.
        /// </summary>
        public float Duration => duration;

        public override void Execute(SkillExecutionContext context)
        {
            // Intentionally empty. SkillExecutor handles timeline offsets for WaitAction.
        }

        public override string GetDescription()
        {
            float totalWait = Mathf.Max(0f, GetTotalDelay() + duration);
            return $"Wait {totalWait:0.##}s";
        }
    }
    
    /// <summary>
    /// Spawns a VFX at a chosen location.
    /// </summary>
    [Serializable]
    public class SpawnVFXAction : SkillAction
    {
        [Header("VFX")]
        [SerializeField] private SkillVFXConfig vfx = new SkillVFXConfig();
        
        [Tooltip("Where to spawn the VFX.")]
        [SerializeField] private SkillActionSpawnLocation spawnLocation = SkillActionSpawnLocation.Caster;
        
        public override float GetTotalDelay()
        {
            float extra = vfx != null ? vfx.spawnDelay : 0f;
            return delay + extra;
        }
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context == null || vfx == null || !vfx.IsValid)
                return;
            
            Transform anchor = GetAnchorTransform(context, spawnLocation);
            Vector3 position = ResolveSpawnPosition(context, anchor, spawnLocation);
            Quaternion rotation = ResolveSpawnRotation(anchor);
            Transform parent = vfx.attachToParent ? anchor : null;
            
            int skillId = context.Skill != null ? context.Skill.GetInstanceID() : 0;
            SkillVFXManager.SpawnVFX(vfx, position, rotation, parent, skillId);
        }
        
        public override string GetDescription()
        {
            return $"Spawn VFX at {spawnLocation}";
        }
        
        private Transform GetAnchorTransform(SkillExecutionContext context, SkillActionSpawnLocation location)
        {
            switch (location)
            {
                case SkillActionSpawnLocation.Target:
                case SkillActionSpawnLocation.TargetGround:
                    return context.PrimaryTarget?.transform;
                case SkillActionSpawnLocation.Caster:
                case SkillActionSpawnLocation.Midpoint:
                case SkillActionSpawnLocation.TargetPosition:
                default:
                    return context.Caster?.transform;
            }
        }
        
        private Vector3 ResolveSpawnPosition(SkillExecutionContext context, Transform anchor, SkillActionSpawnLocation location)
        {
            Vector3 casterPos = context.Caster != null ? context.Caster.WorldPosition : context.TargetPosition;
            Vector3 targetPos = context.PrimaryTarget != null ? context.PrimaryTarget.WorldPosition : context.TargetPosition;
            
            switch (location)
            {
                case SkillActionSpawnLocation.Caster:
                    return ResolvePositionFromTransform(anchor, casterPos);
                case SkillActionSpawnLocation.Target:
                    return ResolvePositionFromTransform(anchor, targetPos);
                case SkillActionSpawnLocation.TargetGround:
                    return ResolveGroundPosition(targetPos);
                case SkillActionSpawnLocation.Midpoint:
                    return Vector3.Lerp(casterPos, targetPos, 0.5f) + vfx.spawnOffset;
                case SkillActionSpawnLocation.TargetPosition:
                default:
                    return context.TargetPosition + vfx.spawnOffset;
            }
        }
        
        private Vector3 ResolvePositionFromTransform(Transform anchor, Vector3 fallback)
        {
            if (anchor == null)
                return fallback + vfx.spawnOffset;
            
            switch (vfx.spawnPointMode)
            {
                case SkillVFXSpawnPointMode.Offset:
                    return anchor.position + anchor.TransformDirection(vfx.spawnOffset);
                case SkillVFXSpawnPointMode.Transform:
                    if (vfx.spawnTransform != null)
                        return vfx.spawnTransform.position + vfx.spawnOffset;
                    return anchor.position + vfx.spawnOffset;
                case SkillVFXSpawnPointMode.BoneName:
                    if (!string.IsNullOrEmpty(vfx.boneName))
                    {
                        Transform bone = anchor.Find(vfx.boneName) ?? FindDeepChild(anchor, vfx.boneName);
                        if (bone != null)
                            return bone.position + bone.TransformDirection(vfx.spawnOffset);
                    }
                    return anchor.position + vfx.spawnOffset;
                default:
                    return anchor.position + vfx.spawnOffset;
            }
        }
        
        private Vector3 ResolveGroundPosition(Vector3 targetPos)
        {
            if (Physics.Raycast(targetPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f))
                return hit.point + vfx.spawnOffset;
            return targetPos + vfx.spawnOffset;
        }
        
        private Quaternion ResolveSpawnRotation(Transform anchor)
        {
            if (vfx.inheritRotation && anchor != null)
                return anchor.rotation;
            return Quaternion.identity;
        }
        
        private Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                var result = FindDeepChild(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
    
    /// <summary>
    /// Plays a sound effect at a chosen location.
    /// </summary>
    [Serializable]
    public class PlaySFXAction : SkillAction
    {
        [Header("SFX")]
        [SerializeField] private SkillSFXConfig sfx = new SkillSFXConfig();
        
        [Tooltip("Where to play the sound.")]
        [SerializeField] private SkillActionSpawnLocation spawnLocation = SkillActionSpawnLocation.Caster;

        [Header("Looping")]
        [Tooltip("If enabled, this action starts a looping audio source instead of a one-shot SFX.")]
        [SerializeField] private bool loop = false;

        [Tooltip("When this looping SFX should be stopped automatically.\n" +
                 "Manual requires a Stop SFX action.")]
        [SerializeField] private SkillSFXLoopStopPolicy loopStopPolicy = SkillSFXLoopStopPolicy.OnFinish;

        [Tooltip("Optional group key for this loop.\n" +
                 "Use the same key in Stop SFX to stop a specific looping sound.")]
        [SerializeField] private string loopKey = "default";
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context == null || sfx == null || !sfx.IsValid)
                return;
            
            Vector3 position = ResolvePosition(context, spawnLocation);

            if (!loop)
            {
                SkillVFXManager.PlaySFX(sfx, position);
                return;
            }

            int casterIndex = context.Caster?.index ?? -1;
            int skillId = context.Skill != null ? context.Skill.GetInstanceID() : 0;
            SkillSFXLoopStopPolicy effectivePolicy = loopStopPolicy;

            // OnCast phases may not have a projectile runtime ID yet.
            // Fall back to OnFinish so loops cannot leak if no projectile ID is available.
            if (effectivePolicy == SkillSFXLoopStopPolicy.OnProjectileDespawn &&
                context.NetworkProjectileId == 0)
            {
                effectivePolicy = SkillSFXLoopStopPolicy.OnFinish;
            }

            SkillVFXManager.PlayLoopingSFX(
                sfx,
                position,
                casterIndex,
                skillId,
                context.ExecutionId,
                context.NetworkProjectileId,
                effectivePolicy,
                loopKey
            );
        }
        
        public override string GetDescription()
        {
            return loop
                ? $"Play Looping SFX at {spawnLocation} (Stop: {loopStopPolicy})"
                : $"Play SFX at {spawnLocation}";
        }
        
        private Vector3 ResolvePosition(SkillExecutionContext context, SkillActionSpawnLocation location)
        {
            Vector3 casterPos = context.Caster != null ? context.Caster.WorldPosition : context.TargetPosition;
            Vector3 targetPos = context.PrimaryTarget != null ? context.PrimaryTarget.WorldPosition : context.TargetPosition;
            
            switch (location)
            {
                case SkillActionSpawnLocation.Target:
                    return targetPos;
                case SkillActionSpawnLocation.TargetGround:
                    if (Physics.Raycast(targetPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f))
                        return hit.point;
                    return targetPos;
                case SkillActionSpawnLocation.Midpoint:
                    return Vector3.Lerp(casterPos, targetPos, 0.5f);
                case SkillActionSpawnLocation.TargetPosition:
                    return context.TargetPosition;
                case SkillActionSpawnLocation.Caster:
                default:
                    return casterPos;
            }
        }
    }

    /// <summary>
    /// Stops active looping SFX started by PlaySFXAction in the current skill context.
    /// </summary>
    [Serializable]
    public class StopSFXAction : SkillAction
    {
        [Tooltip("Optional loop key filter.\n" +
                 "Empty = stop all matching loops for this context.")]
        [SerializeField] private string loopKey = "";

        [Tooltip("If enabled, stop loops regardless of policy.\n" +
                 "If disabled, only loops with Manual stop policy are stopped.")]
        [SerializeField] private bool stopAnyPolicy = false;

        public override void Execute(SkillExecutionContext context)
        {
            if (context == null)
                return;

            SkillVFXManager.StopLoopingSFXForContext(
                context,
                stopAnyPolicy ? (SkillSFXLoopStopPolicy?)null : SkillSFXLoopStopPolicy.Manual,
                loopKey
            );
        }

        public override string GetDescription()
        {
            if (string.IsNullOrEmpty(loopKey))
            {
                return stopAnyPolicy
                    ? "Stop Looping SFX (All Policies)"
                    : "Stop Looping SFX (Manual Policy)";
            }

            return stopAnyPolicy
                ? $"Stop Looping SFX '{loopKey}' (All Policies)"
                : $"Stop Looping SFX '{loopKey}' (Manual Policy)";
        }
    }
    
    /// <summary>
    /// Starts an orbital VFX effect around the caster.
    /// </summary>
    [Serializable]
    public class StartOrbitalVFXAction : SkillAction
    {
        [Header("Orbital VFX")]
        [SerializeField] private OrbitalVFXConfig config = new OrbitalVFXConfig();
        
        [Tooltip("Optional key to identify this orbital effect for stop actions.\n" +
                 "Use the same key in Stop Orbital VFX to remove it.")]
        [SerializeField] private string effectKey = "default";
        
        [Tooltip("Duration in seconds.\n" +
                 "0 = use skill duration (or cooldown if duration is 0)\n" +
                 "< 0 = infinite until manually stopped")]
        [SerializeField] private float durationOverride = 0f;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context?.Caster == null || config == null || !config.IsValid)
                return;
            
            Transform casterTransform = context.Caster.transform;
            if (casterTransform == null)
                return;
            
            float duration = durationOverride;
            if (durationOverride == 0f)
            {
                if (context.Skill != null && context.Skill.Duration > 0f)
                    duration = context.Skill.Duration;
                else
                    duration = context.Skill != null ? context.Skill.Cooldown : 0f;
            }
            else if (durationOverride < 0f)
                duration = 0f;
            
            int skillId = context.Skill != null ? context.Skill.GetInstanceID() : 0;
            
            OrbitalVFXManager.Instance.AddOrbitalEffect(
                context.Caster.index,
                casterTransform,
                config,
                duration,
                skillId,
                effectKey
            );
        }
        
        public override string GetDescription()
        {
            string key = string.IsNullOrEmpty(effectKey) ? "default" : effectKey;
            return $"Start Orbital VFX ({key})";
        }
    }
    
    /// <summary>
    /// Stops orbital VFX effects started by the same skill.
    /// </summary>
    [Serializable]
    public class StopOrbitalVFXAction : SkillAction
    {
        [Tooltip("Optional key to identify which orbital effect to stop.\n" +
                 "Leave empty to stop all orbitals from this skill.")]
        [SerializeField] private string effectKey = "default";
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context?.Caster == null || !OrbitalVFXManager.IsActive)
                return;
            
            int skillId = context.Skill != null ? context.Skill.GetInstanceID() : 0;
            string key = string.IsNullOrEmpty(effectKey) ? string.Empty : effectKey;
            
            if (skillId == 0 && string.IsNullOrEmpty(key))
            {
                OrbitalVFXManager.Instance.RemoveAllEffectsFromAgent(context.Caster.index);
                return;
            }
            
            OrbitalVFXManager.Instance.RemoveOrbitalEffects(context.Caster.index, skillId, key);
        }
        
        public override string GetDescription()
        {
            string key = string.IsNullOrEmpty(effectKey) ? "default" : effectKey;
            return $"Stop Orbital VFX ({key})";
        }
    }
    
    /// <summary>
    /// Applies impact physics using the Fake Physics system.
    /// </summary>
    [Serializable]
    public class ImpactPhysicsAction : SkillAction
    {
        [Header("Impact Physics")]
        [SerializeField] private ImpactPhysicsConfig physics = new ImpactPhysicsConfig();

        [Header("Radius (Optional)")]
        [Tooltip("If enabled, impact physics is applied to units in a radius instead of only current skill targets.")]
        [SerializeField] private bool useRadius = false;

        [Tooltip("Radius used when Radius mode is enabled.")]
        [SerializeField, Range(0.5f, 30f)] private float impactRadius = 3f;

        [Tooltip("Center point for Radius mode.")]
        [SerializeField] private AOECenterMode radiusCenterMode = AOECenterMode.AtTarget;

        [Tooltip("Maximum targets affected in Radius mode (0 = unlimited).")]
        [SerializeField, Range(0, 50)] private int maxRadiusTargets = 0;

        [Tooltip("Can Radius mode affect enemies?")]
        [SerializeField] private bool canHitEnemies = true;

        [Tooltip("Can Radius mode affect allies?")]
        [SerializeField] private bool canHitAllies = false;

        [Tooltip("Can Radius mode affect the caster?")]
        [SerializeField] private bool canHitSelf = false;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context == null || physics == null || !physics.enabled)
                return;

            var targets = GetTargetsForImpactPhysics(context);
            if (targets.Count == 0)
                return;

            if (useRadius)
            {
                context.AllTargets = targets.ToArray();
            }

            var physicsContext = new SkillExecutionContext
            {
                Caster = context.Caster,
                PrimaryTarget = context.PrimaryTarget,
                AllTargets = targets.ToArray(),
                TargetPosition = context.TargetPosition,
                Skill = context.Skill,
                Controller = context.Controller,
                IsCritical = context.IsCritical,
                EffectMultiplier = context.EffectMultiplier,
                ExecutionId = context.ExecutionId,
                NetworkProjectileId = context.NetworkProjectileId
            };

            SkillExecutor.ApplyImpactPhysics(physics, physicsContext);
        }
        
        public override string GetDescription()
        {
            if (physics == null || !physics.enabled)
                return "Impact Physics (disabled)";

            string desc = $"Impact Physics: {physics.physicsType}";
            if (useRadius)
                desc += $" ({impactRadius}m radius)";
            return desc;
        }

        private System.Collections.Generic.List<CrowdAgent> GetTargetsForImpactPhysics(SkillExecutionContext context)
        {
            var targets = new System.Collections.Generic.List<CrowdAgent>();

            if (!useRadius)
            {
                if (context.AllTargets == null || context.AllTargets.Length == 0)
                    return targets;

                foreach (var target in context.AllTargets)
                {
                    if (target != null && !target.isDead)
                    {
                        targets.Add(target);
                    }
                }

                return targets;
            }

            if (context.Controller == null || context.Caster == null)
                return targets;

            Vector3 center = GetRadiusCenter(context);
            float radiusSqr = impactRadius * impactRadius;
            int agentCount = context.Controller.GetAgentCount();
            var inRadius = new System.Collections.Generic.List<(CrowdAgent agent, float sqrDistance)>();

            for (int i = 0; i < agentCount; i++)
            {
                var agent = context.Controller.GetAgent(i);
                if (agent == null || agent.isDead)
                    continue;

                if (agent == context.Caster && !canHitSelf)
                    continue;

                bool isEnemy = AreAgentsEnemies(context.Caster, agent);
                bool isAlly = agent != context.Caster && !isEnemy;

                if (isEnemy && !canHitEnemies)
                    continue;
                if (isAlly && !canHitAllies)
                    continue;
                if (agent == context.Caster && !canHitSelf)
                    continue;

                float sqrDistance = (agent.WorldPosition - center).sqrMagnitude;
                if (sqrDistance > radiusSqr)
                    continue;

                inRadius.Add((agent, sqrDistance));
            }

            inRadius.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));

            int limit = maxRadiusTargets > 0
                ? Mathf.Min(maxRadiusTargets, inRadius.Count)
                : inRadius.Count;

            for (int i = 0; i < limit; i++)
            {
                targets.Add(inRadius[i].agent);
            }

            return targets;
        }

        private Vector3 GetRadiusCenter(SkillExecutionContext context)
        {
            switch (radiusCenterMode)
            {
                case AOECenterMode.AtTarget:
                    if (context.PrimaryTarget != null)
                        return context.PrimaryTarget.WorldPosition;
                    if (context.AllTargets != null && context.AllTargets.Length > 0 && context.AllTargets[0] != null)
                        return context.AllTargets[0].WorldPosition;
                    return context.TargetPosition;

                case AOECenterMode.AtCaster:
                    return context.Caster != null ? context.Caster.WorldPosition : Vector3.zero;

                case AOECenterMode.AtTargetPosition:
                default:
                    return context.TargetPosition;
            }
        }
    }
    
    /// <summary>
    /// Deals damage to the target(s).
    /// </summary>
    [Serializable]
    public class DamageEffect : SkillEffect
    {
        [Header("Invulnerable Override")]
        [Tooltip("If true, this damage ignores the Invulnerable status effect on targets.\n" +
                 "The target's invulnerable state is suppressed for the damage call only — it is restored immediately after.")]
        [SerializeField] private bool ignoreInvulnerable = false;

        [Header("Damage Settings")]
        [Tooltip("How the damage value is calculated:\n" +
                 "• Fixed Value: Use the exact damage amount specified below\n" +
                 "• Base Damage Multiplier: Multiply by the caster's base attack damage (set in EnemyCrowd > Melee Attack Damage)")]
        [SerializeField] private DamageMode damageMode = DamageMode.FixedValue;
        
        [Tooltip("When Mode = Fixed Value: Exact damage dealt.\n" +
                 "When Mode = Base Damage Multiplier: Multiplier applied to EnemyCrowd.meleeAttackDamage.\n" +
                 "Example: 1.5 = deal 150% of the agent's base attack damage.")]
        [SerializeField] private float baseDamage = 10f;
        
        [Tooltip("Random variance in damage (0-1). 0.2 = ±20% damage variation.")]
        [SerializeField, Range(0f, 1f)] private float damageVariance = 0.1f;
        
        [Tooltip("Type of damage for resistance calculations.\n" +
                 "Physical: Reduced by armor\n" +
                 "Magical: Reduced by magic resist\n" +
                 "True: Ignores all resistances")]
        [SerializeField] private DamageType damageType = DamageType.Physical;
        
        [Tooltip("Additional multiplier applied to caster's base damage stat.\n" +
                 "0 = no additional scaling, 1 = add 100% of caster's damage stat.\n" +
                 "This stacks with Base Damage Multiplier mode.")]
        [SerializeField, Range(0f, 3f)] private float casterDamageScaling = 0f;

        [Tooltip("If true, this effect can critically strike.")]
        [SerializeField] private bool canCrit = true;
        
        public float BaseDamage => baseDamage;
        public DamageType DamageType => damageType;
        
        public override void Execute(SkillExecutionContext context)
        {
            bool hasAgentTargets = context.AllTargets != null && context.AllTargets.Length > 0;
            bool hasDamageableTarget = context.DamageableTarget != null && context.DamageableTarget.IsAlive;

            if (!hasAgentTargets && !hasDamageableTarget)
                return;

            bool debugDamageLogging = context.Caster?.enemyCrowd?.enableDebugDamageLogging == true;
            float rawDamage = CalculateDamage(context, debugDamageLogging);

            // ── Damageable object path (non-agent targets) ──────────────
            if (hasDamageableTarget)
            {
                var dmgTarget = context.DamageableTarget;
                float finalDamage = rawDamage; // No resistance system on damageable objects
                Vector3 hitPoint = dmgTarget.DamageTransform != null
                    ? dmgTarget.DamageTransform.position
                    : context.TargetPosition;

                dmgTarget.ApplyDamage(finalDamage, context.Caster, hitPoint);

                if (debugDamageLogging)
                    Debug.Log($"[DamageEffect] {context.Skill?.SkillName ?? "Unknown"} -> DamageableObject " +
                        $"'{dmgTarget.DamageTransform?.name}': Raw={rawDamage:F1}, Final={finalDamage:F1}");
            }

            // ── Standard agent targets ──────────────────────────────────
            if (!hasAgentTargets) return;
            
            foreach (var target in context.AllTargets)
            {
                if (target == null || target.isDead) continue;
                
                // Flying immunity: skip airborne targets when the skill cannot reach them
                if (target.isFlying && context.Skill != null && !context.Skill.CanDamageFlying)
                    continue;
                
                bool targetDebugLogging = debugDamageLogging || target.enemyCrowd?.enableDebugDamageLogging == true;
                
                Vector3? hitPoint = target != null ? (Vector3?)target.WorldPosition : null;
                int attackerIndex = context.Caster?.index ?? -1;
                
                // Suppress invulnerability if configured
                bool wasInvulnerable = ignoreInvulnerable ? SuppressInvulnerable(target) : false;

                // Get combat system from controller for network-aware damage
                var combatSystem = context.Controller?.GetCombatSystem();
                
                if (combatSystem != null)
                {
                    // Map our DamageType to network DamageType (now includes elemental types)
                    var networkDamageType = damageType switch
                    {
                        DamageType.Physical => Networking.DamageType.Physical,
                        DamageType.Magical => Networking.DamageType.Magical,
                        DamageType.Fire => Networking.DamageType.Fire,
                        DamageType.Ice => Networking.DamageType.Ice,
                        DamageType.Lightning => Networking.DamageType.Lightning,
                        DamageType.Poison => Networking.DamageType.Poison,
                        DamageType.True => Networking.DamageType.True,
                        _ => Networking.DamageType.Normal
                    };
                    
                    // SERVER-AUTHORITATIVE: Send RAW damage - server will calculate resistance
                    // This ensures resistance can't be bypassed by malicious clients
                    if (targetDebugLogging)
                    {
                        Debug.Log($"[DAMAGE DEBUG] Calling RequestDamage: targetIdx={target.index}, rawDmg={rawDamage:F1}, attackerIdx={attackerIndex}, type={networkDamageType}");
                    }
                    bool requestResult = combatSystem.RequestDamage(target.index, rawDamage, attackerIndex, hitPoint, networkDamageType);
                    if (targetDebugLogging)
                    {
                        Debug.Log($"[DAMAGE DEBUG] RequestDamage returned: {requestResult}, target HP now: {target.health}/{target.maxHealth}");
                    }
                    
                    // Debug logging (shows what client would calculate, server may differ)
                    if (target.enemyCrowd?.enableDebugDamageLogging == true)
                    {
                        float resistance = target.GetEffectiveResistance(damageType);
                        float predictedFinal = target.ApplyResistance(rawDamage, damageType);
                        Debug.Log($"[DamageEffect] {context.Skill?.SkillName ?? "Unknown"} -> Agent {target.index}: " +
                                 $"Raw={rawDamage:F1}, Type={damageType}, ClientResist={resistance:F1}, " +
                                 $"PredictedFinal={predictedFinal:F1} (Server will recalculate)");
                    }
                }
                else
                {
                    // Single-player fallback: Apply resistance locally (no network)
                    float finalDamage = target.ApplyResistance(rawDamage, damageType);
                    
                    if (target.enemyCrowd?.enableDebugDamageLogging == true)
                    {
                        float resistance = target.GetEffectiveResistance(damageType);
                        float reduction = rawDamage - finalDamage;
                        Debug.Log($"[DamageEffect] {context.Skill?.SkillName ?? "Unknown"} -> Agent {target.index}: " +
                                 $"Raw={rawDamage:F1}, Type={damageType}, Resist={resistance:F1}, " +
                                 $"Reduction={reduction:F1} ({(reduction/rawDamage*100):F1}%), Final={finalDamage:F1}");
                    }
                    
                    Vector3 damageDir = context.Caster != null 
                        ? (target.WorldPosition - context.Caster.WorldPosition).normalized 
                        : Vector3.forward;
                    context.Controller?.DealDamageToAgent(target.index, finalDamage, damageDir, attackerIndex);
                }

                // Restore invulnerability
                if (ignoreInvulnerable) RestoreInvulnerable(target, wasInvulnerable);
            }
        }
        
        private float CalculateDamage(SkillExecutionContext context, bool debugDamageLogging)
        {
            float damage;
            
            if (debugDamageLogging)
            {
                Debug.Log($"[CALC DEBUG] CalculateDamage: baseDamage={baseDamage}, damageMode={damageMode}, EffectMultiplier={context.EffectMultiplier}");
            }
            
            // Calculate base damage based on mode
            if (damageMode == DamageMode.BaseDamageMultiplier)
            {
                // baseDamage is treated as a multiplier of meleeAttackDamage
                float baseAttack = context.Caster?.enemyCrowd?.meleeAttackDamage ?? 10f;
                damage = baseAttack * baseDamage;
            }
            else
            {
                // Fixed value mode - use baseDamage directly
                damage = baseDamage;
            }
            
            // Apply skill scaling if available
            if (context.Skill != null && context.Skill.HasScaling)
            {
                damage = context.Skill.GetScaledDamage(damage, context.Caster);
            }
            
            // Add caster's damage stat if additional scaling is enabled
            if (casterDamageScaling > 0 && context.Caster != null)
            {
                float casterDamage = context.Caster.enemyCrowd?.meleeAttackDamage ?? 0f;
                // Also factor in the agent's outgoing damage multiplier
                casterDamage *= context.Caster.outgoingDamageMultiplier;
                damage += casterDamage * casterDamageScaling;
            }
            
            // Apply variance
            if (damageVariance > 0)
            {
                float variance = UnityEngine.Random.Range(-damageVariance, damageVariance);
                damage *= (1f + variance);
            }
            
            // Apply crit
            if (context.IsCritical && canCrit)
            {
                damage *= 2f; // Could be configurable
            }
            
            // Apply effect multiplier (from buffs, etc.)
            if (debugDamageLogging)
            {
                Debug.Log($"[CALC DEBUG] Before multiplier: damage={damage:F1}, EffectMultiplier={context.EffectMultiplier}");
            }
            damage *= context.EffectMultiplier;
            if (debugDamageLogging)
            {
                Debug.Log($"[CALC DEBUG] After multiplier: damage={damage:F1}");
            }
            
            return Mathf.Max(1f, damage);
        }
        
        public override string GetDescription()
        {
            string desc;
            if (damageMode == DamageMode.BaseDamageMultiplier)
                desc = $"Deals {baseDamage * 100}% base attack as {damageType} damage";
            else
                desc = $"Deals {baseDamage} {damageType} damage";
            if (casterDamageScaling > 0)
                desc += $" (+{casterDamageScaling * 100}% of attack damage)";
            if (ignoreInvulnerable)
                desc += " (ignores invulnerability)";
            return desc;
        }
    }
    
    /// <summary>
    /// Type of damage for resistance calculations.
    /// </summary>
    public enum DamageType
    {
        Physical,
        Magical,
        Fire,
        Ice,
        Lightning,
        Poison,
        True // Ignores resistances
    }
    
    /// <summary>
    /// How damage values are calculated for skill effects.
    /// </summary>
    public enum DamageMode
    {
        [Tooltip("Use the exact damage value specified.")]
        FixedValue,
        
        [Tooltip("Multiply the caster's base attack damage (from EnemyCrowd.meleeAttackDamage) by the specified multiplier.")]
        BaseDamageMultiplier
    }
    
    /// <summary>
    /// How charge distance is determined for ChargeEffect.
    /// </summary>
    public enum ChargeDistanceMode
    {
        [Tooltip("Use the skill's Max Range from the Targeting tab.\nKeeps range configuration in one place.")]
        UseSkillRange,
        
        [Tooltip("Charge a specific fixed distance regardless of target position.")]
        FixedDistance,
        
        [Tooltip("Charge toward the target up to a maximum distance.")]
        TowardTargetCapped
    }

    /// <summary>
    /// Heals the target(s).
    /// </summary>
    [Serializable]
    public class HealEffect : SkillEffect
    {
        [Header("Heal Settings")]
        [Tooltip("Base healing amount.\n" +
                 "Example: 50 heals for 50 HP.")]
        [SerializeField] private float baseHeal = 20f;
        
        [Tooltip("Heal as percentage of target's max health (0-1).\n" +
                 "Example: 0.2 heals for 20% of max HP.\n" +
                 "Added to baseHeal.")]
        [SerializeField, Range(0f, 1f)] private float percentHeal = 0f;
        
        [Tooltip("Random variance in healing (0-1).")]
        [SerializeField, Range(0f, 1f)] private float healVariance = 0.1f;
        
        [Tooltip("Can this heal overheal (exceed max health)?")]
        [SerializeField] private bool canOverheal = false;

        [Tooltip("If true, this effect can critically heal.")]
        [SerializeField] private bool canCrit = true;
        
        public float BaseHeal => baseHeal;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context.AllTargets == null || context.AllTargets.Length == 0)
                return;
            
            foreach (var target in context.AllTargets)
            {
                if (target == null || target.isDead) continue;
                
                float heal = CalculateHeal(context, target);
                
                // Get max health from controller
                float maxHealth = context.Controller?.GetAgentMaxHealth(target.index) ?? target.enemyCrowd?.agentBaseHealth ?? 100f;
                
                // Apply heal
                target.health = canOverheal 
                    ? target.health + heal 
                    : Mathf.Min(target.health + heal, maxHealth);
            }
        }
        
        private float CalculateHeal(SkillExecutionContext context, CrowdAgent target)
        {
            float heal = baseHeal;
            
            // Apply skill scaling if available
            if (context.Skill != null && context.Skill.HasScaling)
            {
                heal = context.Skill.GetScaledHealing(baseHeal, context.Caster);
            }
            
            // Add percent-based heal
            if (percentHeal > 0 && target.enemyCrowd != null)
            {
                float maxHealth = context.Controller?.GetAgentMaxHealth(target.index) ?? target.enemyCrowd.agentBaseHealth;
                heal += maxHealth * percentHeal;
            }
            
            // Apply variance
            if (healVariance > 0)
            {
                float variance = UnityEngine.Random.Range(-healVariance, healVariance);
                heal *= (1f + variance);
            }
            
            // Apply crit
            if (context.IsCritical && canCrit)
            {
                heal *= 1.5f;
            }
            
            // Apply effect multiplier
            heal *= context.EffectMultiplier;
            
            return Mathf.Max(1f, heal);
        }
        
        public override string GetDescription()
        {
            string desc = $"Heals for {baseHeal}";
            if (percentHeal > 0)
                desc += $" + {percentHeal * 100}% of max health";
            return desc;
        }
    }
    
    /// <summary>
    /// Applies a status effect to the target(s).
    /// </summary>
    [Serializable]
    public class ApplyStatusEffect : SkillEffect
    {
        [Header("Status Effect Settings")]
        [Tooltip("The status effect to apply.\n" +
                 "Must match a StatusEffectType in your project.")]
        [SerializeField] private StatusEffectType statusEffect;
        
        [Tooltip("Duration of the status effect in seconds.\n" +
                 "0 = uses the skill's Duration from the Timing tab.")]
        [SerializeField] private float duration = 0f;
        
        [Tooltip("Number of stacks to apply (for stackable effects).")]
        [SerializeField] private int stacks = 1;
        
        [Tooltip("Value/strength of the effect (damage per tick, slow amount, etc.).\n" +
                 "Interpretation depends on the status effect type.")]
        [SerializeField] private float effectValue = 0f;
        
        [Tooltip("Chance to apply the effect (0-1). 1 = always applies.")]
        [SerializeField, Range(0f, 1f)] private float applicationChance = 1f;
        
        public StatusEffectType StatusEffect => statusEffect;
        public float Duration => duration;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context.AllTargets == null || context.AllTargets.Length == 0)
                return;
            
            // Resolve duration: use the action's own duration if set, otherwise
            // fall back to the skill's Duration from the Timing tab.
            float baseDuration = duration > 0f
                ? duration
                : (context.Skill != null ? context.Skill.Duration : 0f);

            float scaledDuration = baseDuration;
            float scaledValue = effectValue;
            if (context.Skill != null && context.Skill.HasScaling)
            {
                scaledDuration = context.Skill.GetScaledDuration(baseDuration, context.Caster);
                scaledValue = context.Skill.GetScaledStatusEffect(effectValue, context.Caster);
            }
            
            foreach (var target in context.AllTargets)
            {
                if (target == null || target.isDead) continue;
                
                // Check application chance
                if (applicationChance < 1f && UnityEngine.Random.value > applicationChance)
                    continue;
                
                // Apply status effect through controller
                context.Controller?.ApplyStatusEffect(
                    target.index,
                    statusEffect,
                    scaledDuration,
                    scaledValue,
                    tickInterval: 1f,
                    sourceAgentIndex: context.Caster?.index ?? -1,
                    maxStacks: stacks
                );
            }
        }
        
        public override string GetDescription()
        {
            string desc = $"Applies {statusEffect}";
            if (duration > 0)
                desc += $" for {duration}s";
            else
                desc += " (uses skill duration)";
            if (applicationChance < 1f)
                desc += $" ({applicationChance * 100}% chance)";
            return desc;
        }

        public override string GetDescription(AgentSkill skill)
        {
            string desc = $"Applies {statusEffect}";
            if (duration > 0)
            {
                desc += $" for {duration}s";
            }
            else if (skill != null && skill.Duration > 0)
            {
                desc += $" for {skill.Duration}s";
            }
            if (applicationChance < 1f)
                desc += $" ({applicationChance * 100}% chance)";
            return desc;
        }
    }
    
    /// <summary>
    /// Applies knockback or pull to target(s).
    /// </summary>
    [Serializable]
    public class KnockbackEffect : SkillEffect
    {
        [Header("Knockback Settings")]
        [Tooltip("Force of the knockback.\n" +
                 "Positive = push away from caster\n" +
                 "Negative = pull toward caster")]
        [SerializeField] private float force = 10f;
        
        [Tooltip("Duration of the knockback movement.")]
        [SerializeField] private float duration = 0.3f;
        
        [Tooltip("Should targets be stunned during knockback?")]
        [SerializeField] private bool stunDuringKnockback = true;
        
        [Tooltip("Vertical lift force applied during knockback.\n" +
                 "Values >= 1.5 will trigger get-up animation.")]
        [SerializeField] private float liftForce = 0f;
        
        [Tooltip("Gravity applied during knockback (default 25).")]
        [SerializeField] private float gravity = 25f;
        
        [Tooltip("Horizontal drag/damping (default 4).")]
        [SerializeField] private float drag = 4f;

        [Header("Radius (Optional)")]
        [Tooltip("If enabled, knockback is applied to units in a radius instead of only current skill targets.")]
        [SerializeField] private bool useRadius = false;

        [Tooltip("Radius used when Radius mode is enabled.")]
        [SerializeField, Range(0.5f, 30f)] private float knockbackRadius = 3f;

        [Tooltip("Center point for Radius mode.")]
        [SerializeField] private AOECenterMode radiusCenterMode = AOECenterMode.AtTarget;

        [Tooltip("Maximum targets affected in Radius mode (0 = unlimited).")]
        [SerializeField, Range(0, 50)] private int maxRadiusTargets = 0;

        [Tooltip("Can Radius mode affect enemies?")]
        [SerializeField] private bool canHitEnemies = true;

        [Tooltip("Can Radius mode affect allies?")]
        [SerializeField] private bool canHitAllies = false;

        [Tooltip("Can Radius mode affect the caster?")]
        [SerializeField] private bool canHitSelf = false;
        
        public float Force => force;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context == null || context.Caster == null)
                return;

            var targets = GetTargetsForKnockback(context);
            if (targets.Count == 0)
                return;

            if (useRadius)
            {
                context.AllTargets = targets.ToArray();
            }
            
            Vector3 casterPos = context.Caster.WorldPosition;
            float now = Time.time;
            
            foreach (var target in targets)
            {
                if (target == null || target.isDead) continue;
                if (target.isDisplacementImmune) continue;
                
                Vector3 targetPos = target.WorldPosition;
                // Calculate direction (positive force = push away, negative = pull toward)
                Vector3 horizontal = Vector3.ProjectOnPlane(targetPos - casterPos, Vector3.up).normalized;
                if (horizontal == Vector3.zero)
                    horizontal = Vector3.forward;
                
                // Calculate velocity with lift component
                Vector3 velocity = horizontal * force;
                velocity.y += liftForce;

                bool handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                    target,
                    velocity,
                    duration,
                    drag,
                    gravity,
                    Mathf.Abs(force),
                    liftForce,
                    context.Controller);

                if (!handledByAstar)
                {
                    // Apply knockback using the navmesh offset system
                    // (Compatible with fake physics system)
                    target.hasNavmeshOffsetImpulse = true;
                    target.navmeshOffsetVelocity = velocity;
                    target.navmeshOffsetGroundPosition = target.WorldPosition;
                    target.navmeshOffset = Vector3.zero;
                    target.navmeshOffsetDuration = duration;
                    target.navmeshOffsetEndTime = now + duration;
                    target.navmeshOffsetGravity = gravity;
                    target.navmeshOffsetDrag = drag;
                    
                    // Store impulse values for animation system (compatible with PlayerSkillManager)
                    target.lastNavmeshImpulsePush = Mathf.Abs(force);
                    target.lastNavmeshImpulseLift = liftForce;
                    target.navmeshImpulseAnimationPlayed = false;
                    
                    // Mark lift impulse for get-up flow (threshold from InstructionCrowdNavmeshImpulse)
                    const float LiftGetUpThreshold = 1.5f;
                    target.liftImpulseActive = liftForce >= LiftGetUpThreshold;
                }
                
                // Lock attacks during knockback
                float lockEnd = now + duration + 0.5f;
                target.impulseAttackLockEndTime = Mathf.Max(target.impulseAttackLockEndTime, lockEnd);
                
                // Apply stun if configured
                if (stunDuringKnockback)
                {
                    context.Controller?.ApplyStatusEffect(
                        target.index,
                        StatusEffectType.Stun,
                        duration,
                        0f,
                        tickInterval: 1f,
                        sourceAgentIndex: context.Caster.index,
                        maxStacks: 1
                    );
                }
            }
        }

        private System.Collections.Generic.List<CrowdAgent> GetTargetsForKnockback(SkillExecutionContext context)
        {
            var targets = new System.Collections.Generic.List<CrowdAgent>();

            if (!useRadius)
            {
                if (context.AllTargets == null || context.AllTargets.Length == 0)
                    return targets;

                foreach (var target in context.AllTargets)
                {
                    if (target != null && !target.isDead)
                    {
                        targets.Add(target);
                    }
                }

                return targets;
            }

            if (context.Controller == null)
                return targets;

            Vector3 center = GetRadiusCenter(context);
            float radiusSqr = knockbackRadius * knockbackRadius;
            int agentCount = context.Controller.GetAgentCount();
            var inRadius = new System.Collections.Generic.List<(CrowdAgent agent, float sqrDistance)>();

            for (int i = 0; i < agentCount; i++)
            {
                var agent = context.Controller.GetAgent(i);
                if (agent == null || agent.isDead)
                    continue;

                if (agent == context.Caster && !canHitSelf)
                    continue;

                bool isEnemy = AreAgentsEnemies(context.Caster, agent);
                bool isAlly = agent != context.Caster && !isEnemy;

                if (isEnemy && !canHitEnemies)
                    continue;
                if (isAlly && !canHitAllies)
                    continue;
                if (agent == context.Caster && !canHitSelf)
                    continue;

                float sqrDistance = (agent.WorldPosition - center).sqrMagnitude;
                if (sqrDistance > radiusSqr)
                    continue;

                inRadius.Add((agent, sqrDistance));
            }

            inRadius.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));

            int limit = maxRadiusTargets > 0
                ? Mathf.Min(maxRadiusTargets, inRadius.Count)
                : inRadius.Count;

            for (int i = 0; i < limit; i++)
            {
                targets.Add(inRadius[i].agent);
            }

            return targets;
        }

        private Vector3 GetRadiusCenter(SkillExecutionContext context)
        {
            switch (radiusCenterMode)
            {
                case AOECenterMode.AtTarget:
                    if (context.PrimaryTarget != null)
                        return context.PrimaryTarget.WorldPosition;
                    if (context.AllTargets != null && context.AllTargets.Length > 0 && context.AllTargets[0] != null)
                        return context.AllTargets[0].WorldPosition;
                    return context.TargetPosition;

                case AOECenterMode.AtCaster:
                    return context.Caster != null ? context.Caster.WorldPosition : Vector3.zero;

                case AOECenterMode.AtTargetPosition:
                default:
                    return context.TargetPosition;
            }
        }
        
        public override string GetDescription()
        {
            string desc = force > 0
                ? $"Knocks targets back with {force} force"
                : $"Pulls targets toward caster with {Mathf.Abs(force)} force";

            if (useRadius)
                desc += $" in {knockbackRadius}m radius";

            return desc;
        }
    }
    
    /// <summary>
    /// Spawns additional units with full network synchronization.
    /// Properly integrates with INetworkSpawnAuthority for server-authoritative summoning.
    /// </summary>
    [Serializable]
    public class SummonEffect : SkillEffect
    {
        [Header("Summon Settings")]
        [Tooltip("The EnemyCrowd (faction) to summon units from.")]
        [SerializeField] private EnemyCrowd summonedFaction;
        
        [Tooltip("Number of units to summon.")]
        [SerializeField] private int summonCount = 2;
        
        [Tooltip("Radius around caster where summons appear.")]
        [SerializeField] private float summonRadius = 3f;
        
        [Tooltip("Duration summons exist before despawning (0 = permanent).")]
        [SerializeField] private float summonDuration = 30f;
        
        [Tooltip("Should summons inherit the caster's target?")]
        [SerializeField] private bool inheritTarget = true;
        
        [Tooltip("Should summons inherit the caster's owner (for multiplayer)?")]
        [SerializeField] private bool inheritOwner = true;
        
        public EnemyCrowd SummonedFaction => summonedFaction;
        public int SummonCount => summonCount;
        public float SummonRadius => summonRadius;
        public float SummonDuration => summonDuration;
        public bool InheritTarget => inheritTarget;
        public bool InheritOwner => inheritOwner;
        
        public override void Execute(SkillExecutionContext context)
        {
            if (summonedFaction == null || context.Controller == null || context.Caster == null)
                return;
            
            Vector3 casterPos = context.Caster.WorldPosition;
            
            // Find the faction index
            int factionIndex = context.Controller.EnemyCrowds.IndexOf(summonedFaction);
            if (factionIndex < 0)
            {
                Debug.LogWarning($"[SummonEffect] Summoned faction '{summonedFaction.factionName}' not found in controller");
                return;
            }
            
            // Use the controller's server-authoritative summon method
            // This handles: network ID assignment, owner sync, target inheritance, timed despawn
            var spawnedIndices = context.Controller.SpawnSummonedAgents(
                factionIndex,
                casterPos,
                summonCount,
                context.Caster,
                summonDuration,
                inheritTarget,
                summonRadius
            );
            
            if (spawnedIndices == null || spawnedIndices.Count == 0)
            {
                Debug.LogWarning($"[SummonEffect] Failed to spawn {summonCount} {summonedFaction.factionName}");
            }
        }
        
        public override string GetDescription()
        {
            string factionName = summonedFaction != null ? summonedFaction.factionName : "units";
            string desc = $"Summons {summonCount} {factionName}";
            if (summonDuration > 0)
                desc += $" for {summonDuration}s";
            return desc;
        }
    }
    
    /// <summary>
    /// Teleports or dashes the caster to a location.
    /// </summary>
    [Serializable]
    public class MovementEffect : SkillEffect
    {
        [Header("Movement Settings")]
        [Tooltip("Type of movement.\n" +
                 "Dash: Quick movement in a direction\n" +
                 "Teleport: Instant position change\n" +
                 "Leap: Arc movement to target")]
        [SerializeField] private MovementType movementType = MovementType.Dash;
        
        [Tooltip("Distance to move (for Dash) or max range (for Teleport/Leap).")]
        [SerializeField] private float distance = 5f;
        
        [Tooltip("Duration of the movement (0 = instant for Teleport).\n" +
                 "For Dash: used only when Dash Speed is 0.")]
        [SerializeField] private float movementDuration = 0.3f;
        
        [Tooltip("Dash speed in units/sec (0 = use Distance / Movement Duration).")]
        [SerializeField] private float dashSpeed = 0f;
        
        [Tooltip("Leap peak height in units (0 = auto based on Distance).")]
        [SerializeField] private float leapHeight = 0f;

        [Tooltip("If enabled, Dash/Leap travel distance is capped to the sampled target distance at cast time.")]
        [SerializeField] private bool capToSampledTargetDistance = false;

        [Tooltip("If enabled, Dash continuously updates direction toward the moving target while dashing.")]
        [SerializeField] private bool homingDash = false;

        [Tooltip("If enabled, Leap continuously updates horizontal direction toward the moving target while airborne.")]
        [SerializeField] private bool homingLeap = false;
        
        [Tooltip("Is caster invulnerable during movement?")]
        [SerializeField] private bool invulnerableDuring = false;
        
        [Tooltip("Move toward target (true) or toward target position (false)?")]
        [SerializeField] private bool moveTowardTarget = true;
        
        public MovementType Type => movementType;
        public float Distance => distance;
        public float DashSpeed => dashSpeed;
        public float LeapHeight => leapHeight;
        public float MovementDuration => movementDuration;
        public bool HomingDash => homingDash;
        public bool HomingLeap => homingLeap;
        public bool CapToSampledTargetDistance => capToSampledTargetDistance;

        private static readonly System.Collections.Generic.Dictionary<int, int> _homingMovementTokens =
            new System.Collections.Generic.Dictionary<int, int>();
        
        public float GetAutoImpactDelay()
        {
            if (movementType != MovementType.Leap)
                return 0f;
            
            return Mathf.Max(0f, movementDuration);
        }
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context.Caster == null || context.Controller == null)
                return;
            
            Vector3 casterPos = context.Caster.WorldPosition;
            Vector3 targetPos = context.TargetPosition;
            
            if (moveTowardTarget && context.PrimaryTarget != null)
            {
                targetPos = context.PrimaryTarget.WorldPosition;
            }
            
            Vector3 direction = (targetPos - casterPos).normalized;
            float sampledDistanceToTarget = Vector3.Distance(casterPos, targetPos);
            float travelDistance = (movementType == MovementType.Teleport || capToSampledTargetDistance)
                ? Mathf.Min(distance, sampledDistanceToTarget)
                : distance;
            Vector3 destination = casterPos + direction * travelDistance;
            
            float dashDuration = movementDuration;
            if (movementType == MovementType.Dash && dashSpeed > 0f && travelDistance > 0f)
            {
                dashDuration = travelDistance / dashSpeed;
            }
            
            switch (movementType)
            {
                case MovementType.Dash:
                    // Use navmesh offset impulse for dash
                    ApplyDashImpulse(
                        context.Caster,
                        context.Controller,
                        direction,
                        travelDistance,
                        dashDuration,
                        moveTowardTarget && homingDash,
                        context.PrimaryTarget,
                        targetPos);
                    break;
                    
                case MovementType.Teleport:
                    // Direct position warp
                    context.Caster.navigationAgent?.Warp(destination);
                    break;
                    
                case MovementType.Leap:
                    // Leap uses the same impulse system with upward velocity
                    bool useExplicitHeight = leapHeight > 0f;
                    float peakHeight = useExplicitHeight ? leapHeight : travelDistance * 0.3f;
                    ApplyLeapImpulse(
                        context.Caster,
                        context.Controller,
                        direction,
                        travelDistance,
                        movementDuration,
                        peakHeight,
                        useExplicitHeight,
                        moveTowardTarget && homingLeap,
                        context.PrimaryTarget,
                        targetPos);
                    break;
            }
            
            // Apply invulnerability if configured
            float invulnerabilityDuration = movementType == MovementType.Dash ? dashDuration : movementDuration;
            if (invulnerableDuring && invulnerabilityDuration > 0)
            {
                context.Controller.ApplyStatusEffect(
                    context.Caster.index,
                    StatusEffectType.Invulnerable,
                    invulnerabilityDuration,
                    0f,
                    tickInterval: 1f,
                    sourceAgentIndex: context.Caster.index,
                    maxStacks: 1
                );
            }
        }
        
        private void ApplyDashImpulse(
            CrowdAgent caster,
            EnemyMassesCrowdController controller,
            Vector3 direction,
            float distance,
            float duration,
            bool enableHoming,
            CrowdAgent homingTarget,
            Vector3 fallbackTargetPos)
        {
            if (caster == null) return;
            
            float now = Time.time;
            float safeDuration = Mathf.Max(0.01f, duration);
            float horizontalSpeed = distance / safeDuration;
            
            Vector3 velocity = direction * horizontalSpeed;
            bool handledByAstar = false;
            if (!enableHoming)
            {
                handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                    caster,
                    velocity,
                    safeDuration,
                    0f,
                    0f,
                    horizontalSpeed,
                    0f);
            }
            
            if (!handledByAstar)
            {
                caster.hasNavmeshOffsetImpulse = true;
                caster.navmeshOffsetVelocity = velocity;
                caster.navmeshOffsetGroundPosition = caster.WorldPosition;
                caster.navmeshOffset = Vector3.zero;
                caster.navmeshOffsetDuration = safeDuration;
                caster.navmeshOffsetEndTime = now + safeDuration;
                caster.navmeshOffsetGravity = 0f; // No gravity for dash
                caster.navmeshOffsetDrag = 0f; // No drag for consistent speed
                
                // Animation system fields (compatible with PlayerSkillManager)
                caster.lastNavmeshImpulsePush = horizontalSpeed;
                caster.lastNavmeshImpulseLift = 0f;
                caster.navmeshImpulseAnimationPlayed = false;
                caster.liftImpulseActive = false;

                if (enableHoming)
                {
                    StartHomingMovementRoutine(
                        controller,
                        caster,
                        homingTarget,
                        fallbackTargetPos,
                        horizontalSpeed,
                        preserveVerticalVelocity: false,
                        safeDuration);
                }
            }
            
            // Lock attacks during dash
            caster.impulseAttackLockEndTime = Mathf.Max(caster.impulseAttackLockEndTime, now + safeDuration + 0.2f);
        }
        
        private void ApplyLeapImpulse(
            CrowdAgent caster,
            EnemyMassesCrowdController controller,
            Vector3 direction,
            float distance,
            float duration,
            float peakHeight,
            bool useExplicitHeight,
            bool enableHoming,
            CrowdAgent homingTarget,
            Vector3 fallbackTargetPos)
        {
            if (caster == null) return;
            
            float now = Time.time;
            float safeDuration = Mathf.Max(0.01f, duration);
            
            // Calculate velocity with upward arc
            float horizontalSpeed = distance / safeDuration;
            peakHeight = Mathf.Max(0f, peakHeight);
            
            float gravity;
            float upwardSpeed;
            if (useExplicitHeight && peakHeight > 0f)
            {
                // Solve for gravity so the arc hits peakHeight at t/2 and lands at t = duration.
                gravity = 8f * peakHeight / (safeDuration * safeDuration);
                upwardSpeed = 4f * peakHeight / safeDuration;
            }
            else
            {
                gravity = 9.8f;
                float halfTime = safeDuration * 0.5f;
                upwardSpeed = (peakHeight + 0.5f * gravity * halfTime * halfTime) / halfTime;
            }
            
            Vector3 velocity = direction * horizontalSpeed + Vector3.up * upwardSpeed;
            bool handledByAstar = false;
            if (!enableHoming)
            {
                handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                    caster,
                    velocity,
                    safeDuration,
                    0.5f,
                    gravity,
                    horizontalSpeed,
                    upwardSpeed);
            }
            
            if (!handledByAstar)
            {
                caster.hasNavmeshOffsetImpulse = true;
                caster.navmeshOffsetVelocity = velocity;
                caster.navmeshOffsetGroundPosition = caster.WorldPosition;
                caster.navmeshOffset = Vector3.zero;
                caster.navmeshOffsetDuration = safeDuration;
                caster.navmeshOffsetEndTime = now + safeDuration;
                caster.navmeshOffsetGravity = gravity;
                caster.navmeshOffsetDrag = 0.5f;
                
                // Animation system fields (compatible with PlayerSkillManager)
                caster.lastNavmeshImpulsePush = horizontalSpeed;
                caster.lastNavmeshImpulseLift = upwardSpeed;
                caster.navmeshImpulseAnimationPlayed = false;
                
                // Trigger get-up animation flow for significant lifts
                const float LiftGetUpThreshold = 1.5f;
                caster.liftImpulseActive = upwardSpeed >= LiftGetUpThreshold;

                if (enableHoming)
                {
                    StartHomingMovementRoutine(
                        controller,
                        caster,
                        homingTarget,
                        fallbackTargetPos,
                        horizontalSpeed,
                        preserveVerticalVelocity: true,
                        safeDuration);
                }
            }
            
            // Lock attacks during leap + landing
            caster.impulseAttackLockEndTime = Mathf.Max(caster.impulseAttackLockEndTime, now + safeDuration + 0.5f);
        }

        private void StartHomingMovementRoutine(
            EnemyMassesCrowdController controller,
            CrowdAgent caster,
            CrowdAgent target,
            Vector3 fallbackTargetPos,
            float horizontalSpeed,
            bool preserveVerticalVelocity,
            float duration)
        {
            if (controller == null || caster == null || horizontalSpeed <= 0f || duration <= 0f)
                return;

            int token = GetNextHomingToken(caster.index);
            controller.StartCoroutine(HomingMovementRoutine(
                caster,
                target,
                fallbackTargetPos,
                horizontalSpeed,
                preserveVerticalVelocity,
                duration,
                token));
        }

        private static int GetNextHomingToken(int agentIndex)
        {
            if (_homingMovementTokens.TryGetValue(agentIndex, out int existing))
            {
                int next = existing + 1;
                _homingMovementTokens[agentIndex] = next;
                return next;
            }

            _homingMovementTokens[agentIndex] = 1;
            return 1;
        }

        private static bool IsHomingTokenActive(int agentIndex, int token)
        {
            return _homingMovementTokens.TryGetValue(agentIndex, out int activeToken) && activeToken == token;
        }

        private static System.Collections.IEnumerator HomingMovementRoutine(
            CrowdAgent caster,
            CrowdAgent target,
            Vector3 fallbackTargetPos,
            float horizontalSpeed,
            bool preserveVerticalVelocity,
            float duration,
            int token)
        {
            float endTime = Time.time + duration;

            while (Time.time < endTime)
            {
                if (caster == null || caster.isDead)
                    yield break;

                if (!IsHomingTokenActive(caster.index, token))
                    yield break;

                if (!caster.hasNavmeshOffsetImpulse || Time.time >= caster.navmeshOffsetEndTime)
                    yield break;

                Vector3 currentTargetPos = fallbackTargetPos;
                if (target != null && !target.isDead)
                    currentTargetPos = target.WorldPosition;

                Vector3 horizontalDirection = Vector3.ProjectOnPlane(currentTargetPos - caster.WorldPosition, Vector3.up).normalized;
                if (horizontalDirection == Vector3.zero)
                {
                    Vector3 fallbackDirection = caster.transform != null
                        ? Vector3.ProjectOnPlane(caster.transform.forward, Vector3.up).normalized
                        : Vector3.forward;
                    horizontalDirection = fallbackDirection == Vector3.zero ? Vector3.forward : fallbackDirection;
                }

                float verticalVelocity = preserveVerticalVelocity ? caster.navmeshOffsetVelocity.y : 0f;
                caster.navmeshOffsetVelocity = new Vector3(
                    horizontalDirection.x * horizontalSpeed,
                    verticalVelocity,
                    horizontalDirection.z * horizontalSpeed);

                yield return null;
            }
        }
        
        public override string GetDescription()
        {
            string capText = capToSampledTargetDistance ? " (capped)" : "";
            switch (movementType)
            {
                case MovementType.Dash:
                    string dashHomingText = homingDash ? " [Homing]" : "";
                    if (dashSpeed > 0f)
                        return $"Dash {distance} units @ {dashSpeed} u/s{dashHomingText}{capText}";
                    return $"Dash {distance} units{dashHomingText}{capText}";
                case MovementType.Leap:
                    string leapHomingText = homingLeap ? " [Homing]" : "";
                    if (leapHeight > 0f)
                        return $"Leap {distance} units (height {leapHeight}){leapHomingText}{capText}";
                    return $"Leap {distance} units{leapHomingText}{capText}";
                case MovementType.Teleport:
                    return $"Teleport {distance} units";
                default:
                    return $"{movementType} {distance} units";
            }
        }
    }
    
    public enum MovementType
    {
        Dash,
        Teleport,
        Leap
    }
    
    /// <summary>
    /// Piercing effect - damages all targets in a line from the caster.
    /// Can be used with charge attacks, dash skills, or any forward-moving ability.
    /// </summary>
    [Serializable]
    public class PiercingEffect : SkillEffect
    {
        [Header("Invulnerable Override")]
        [Tooltip("If true, this damage ignores the Invulnerable status effect on targets.\n" +
                 "The target's invulnerable state is suppressed for each damage call only — it is restored immediately after.")]
        [SerializeField] private bool ignoreInvulnerable = false;

        [Header("Piercing Settings")]
        [Tooltip("Width of the pierce hitbox.\n" +
                 "Targets within this distance from the pierce line take damage.")]
        [SerializeField, Range(0.5f, 10f)] private float pierceWidth = 1.5f;
        
        [Tooltip("Length of the pierce line from caster.\n" +
                 "0 = use skill's max range.")]
        [SerializeField] private float pierceLength = 0f;
        
        [Tooltip("Maximum number of targets to pierce (0 = unlimited).")]
        [SerializeField, Range(0, 20)] private int maxPierceTargets = 0;
        
        [Tooltip("Effect intensity multiplier per pierce.\n" +
                 "First target always receives 100%.\n" +
                 "0.8 = each subsequent target receives 80% of previous.")]
        [SerializeField, Range(0.1f, 1f)] private float pierceFalloff = 0.8f;
        
        [Header("Damage Settings")]
        [Tooltip("How the damage value is calculated:\n" +
                 "• Fixed Value: Use the exact damage amount specified\n" +
                 "• Base Damage Multiplier: Multiply by the caster's base attack damage (set in EnemyCrowd > Melee Attack Damage)")]
        [SerializeField] private DamageMode damageMode = DamageMode.FixedValue;
        
        [Tooltip("When Mode = Fixed Value: Exact damage dealt to each pierced target.\n" +
                 "When Mode = Base Damage Multiplier: Multiplier applied to EnemyCrowd.meleeAttackDamage.\n" +
                 "0 = use damage from other DamageEffect in this skill.")]
        [SerializeField] private float baseDamage = 0f;
        
        [Tooltip("Type of damage for resistance calculations.")]
        [SerializeField] private DamageType damageType = DamageType.Physical;
        
        [Tooltip("Minimum distance between pierce damage applications.\n" +
                 "Prevents the same enemy from being hit multiple times.")]
        [SerializeField, Range(0.1f, 3f)] private float pierceTickDistance = 0.5f;
        
        [Tooltip("Direction mode for piercing.\n" +
                 "• TowardTarget: Pierce in direction of primary target\n" +
                 "• Forward: Pierce in caster's forward direction\n" +
                 "• MovementDirection: Pierce in caster's movement direction")]
        [SerializeField] private PierceDirectionMode directionMode = PierceDirectionMode.TowardTarget;

        [Tooltip("If true, this effect can critically strike.")]
        [SerializeField] private bool canCrit = true;
        
        public float PierceWidth => pierceWidth;
        public float PierceLength => pierceLength;
        public int MaxPierceTargets => maxPierceTargets;
        public float PierceFalloff => pierceFalloff;
        public float BaseDamage => baseDamage;
        public DamageType DamageTypeProp => damageType;
        
        /// <summary>
        /// Calculates effect multiplier for the Nth pierced target.
        /// </summary>
        public float GetPierceMultiplier(int pierceIndex)
        {
            if (pierceIndex <= 0)
                return 1f;
            
            return Mathf.Pow(pierceFalloff, pierceIndex);
        }
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context.Caster == null || context.Controller == null)
                return;
            
            Vector3 casterPos = context.Caster.WorldPosition;
            
            // Determine pierce direction
            Vector3 direction = GetPierceDirection(context, casterPos);
            
            // Determine pierce length
            float length = pierceLength > 0 ? pierceLength : (context.Skill?.MaxRange ?? 10f);
            
            // Find all potential targets in the pierce path
            int agentCount = context.Controller.GetAgentCount();
            var piercedTargets = new System.Collections.Generic.List<(CrowdAgent agent, float distance)>();
            
            for (int i = 0; i < agentCount; i++)
            {
                var agent = context.Controller.GetAgent(i);
                if (agent == null || agent.isDead || agent == context.Caster)
                {
                    continue;
                }
                
                // Check if this agent is an enemy of the caster
                if (!AreAgentsEnemies(context.Caster, agent))
                {
                    continue;
                }
                
                Vector3 agentPos = agent.WorldPosition;
                
                // Calculate distance along the pierce line
                Vector3 toAgent = agentPos - casterPos;
                float distanceAlongLine = Vector3.Dot(toAgent, direction);
                
                // Must be in front of caster and within length
                if (distanceAlongLine < 0 || distanceAlongLine > length)
                {
                    continue;
                }
                
                // Calculate perpendicular distance from pierce line
                Vector3 pointOnLine = casterPos + direction * distanceAlongLine;
                float perpendicularDistance = Vector3.Distance(agentPos, pointOnLine);
                
                // Must be within pierce width
                if (perpendicularDistance > pierceWidth * 0.5f)
                {
                    continue;
                }
                
                piercedTargets.Add((agent, distanceAlongLine));
            }
            
            // Sort by distance (closest first)
            piercedTargets.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            // Apply damage to pierced targets
            int hitCount = 0;
            float lastHitDistance = -pierceTickDistance;
            
            foreach (var (target, distance) in piercedTargets)
            {
                // Check max targets
                if (maxPierceTargets > 0 && hitCount >= maxPierceTargets)
                {
                    break;
                }
                
                // Check minimum distance between hits
                if (distance - lastHitDistance < pierceTickDistance)
                {
                    continue;
                }
                
                // Flying immunity: skip airborne targets when the skill cannot reach them
                if (target.isFlying && context.Skill != null && !context.Skill.CanDamageFlying)
                    continue;
                
                // Calculate damage with falloff
                float multiplier = GetPierceMultiplier(hitCount);
                float damage = 0f;
                
                if (baseDamage > 0)
                {
                    if (damageMode == DamageMode.BaseDamageMultiplier)
                    {
                        // baseDamage is a multiplier of meleeAttackDamage
                        float baseAttack = context.Caster?.enemyCrowd?.meleeAttackDamage ?? 10f;
                        damage = baseAttack * baseDamage * multiplier;
                    }
                    else
                    {
                        // Fixed value
                        damage = baseDamage * multiplier;
                    }
                }
                
                // Apply damage if we have base damage
                if (damage > 0)
                {
                    // Apply skill scaling
                    if (context.Skill != null && context.Skill.HasScaling)
                    {
                        damage = context.Skill.GetScaledDamage(damage, context.Caster);
                    }
                    
                    // Apply crit
                    if (context.IsCritical && canCrit)
                    {
                        damage *= context.Skill?.CriticalMultiplier ?? 2f;
                    }
                    
                    // Apply effect multiplier
                    damage *= context.EffectMultiplier;
                    
                    // Suppress invulnerability if configured
                    bool wasInvulnerable = ignoreInvulnerable ? SuppressInvulnerable(target) : false;

                    // Deal damage (send raw damage - server applies resistance)
                    var combatSystem = context.Controller.GetCombatSystem();
                    if (combatSystem != null)
                    {
                        var networkDamageType = damageType switch
                        {
                            DamageType.Physical => Networking.DamageType.Physical,
                            DamageType.Magical => Networking.DamageType.Magical,
                            DamageType.Fire => Networking.DamageType.Fire,
                            DamageType.Ice => Networking.DamageType.Ice,
                            DamageType.Lightning => Networking.DamageType.Lightning,
                            DamageType.Poison => Networking.DamageType.Poison,
                            DamageType.True => Networking.DamageType.True,
                            _ => Networking.DamageType.Normal
                        };
                        combatSystem.RequestDamage(
                            target.index,
                            damage,
                            context.Caster?.index ?? -1,
                            target.WorldPosition,
                            networkDamageType);
                    }
                    else
                    {
                        context.Controller.DealDamageToAgent(target.index, damage, direction, context.Caster?.index ?? -1);
                    }

                    // Restore invulnerability
                    if (ignoreInvulnerable) RestoreInvulnerable(target, wasInvulnerable);
                }
                
                // Store for other effects (like knockback) that may want the pierce targets
                // This modifies the context's AllTargets to include pierce hits
                var currentTargets = context.AllTargets != null 
                    ? new System.Collections.Generic.List<CrowdAgent>(context.AllTargets) 
                    : new System.Collections.Generic.List<CrowdAgent>();
                if (!currentTargets.Contains(target))
                {
                    currentTargets.Add(target);
                    context.AllTargets = currentTargets.ToArray();
                }
                
                hitCount++;
                lastHitDistance = distance;
            }
        }
        
        private Vector3 GetPierceDirection(SkillExecutionContext context, Vector3 casterPos)
        {
            switch (directionMode)
            {
                case PierceDirectionMode.TowardTarget:
                    if (context.PrimaryTarget != null)
                    {
                        Vector3 targetPos = context.PrimaryTarget.WorldPosition;
                        return (targetPos - casterPos).normalized;
                    }
                    else if (context.TargetPosition != Vector3.zero)
                    {
                        return (context.TargetPosition - casterPos).normalized;
                    }
                    goto case PierceDirectionMode.Forward;
                    
                case PierceDirectionMode.Forward:
                    return context.Caster.navigationAgent?.Transform?.forward ?? context.Caster.transform?.forward ?? Vector3.forward;
                    
                case PierceDirectionMode.MovementDirection:
                    if (context.Caster.CurrentVelocity.sqrMagnitude > 0.1f)
                    {
                        return context.Caster.CurrentVelocity.normalized;
                    }
                    goto case PierceDirectionMode.Forward;
                    
                default:
                    return Vector3.forward;
            }
        }
        
        public override string GetDescription()
        {
            string desc = $"Pierces through enemies (width: {pierceWidth}m)";
            if (maxPierceTargets > 0)
                desc += $", max {maxPierceTargets} targets";
            if (baseDamage > 0)
            {
                if (damageMode == DamageMode.BaseDamageMultiplier)
                    desc += $", dealing {baseDamage * 100}% base attack as {damageType} damage";
                else
                    desc += $", dealing {baseDamage} {damageType} damage";
            }
            if (pierceFalloff < 1f)
                desc += $" ({pierceFalloff * 100}% falloff per target)";
            if (ignoreInvulnerable)
                desc += " (ignores invulnerability)";
            return desc;
        }
    }
    
    public enum PierceDirectionMode
    {
        TowardTarget,
        Forward,
        MovementDirection
    }
    
    /// <summary>
    /// AOE on Impact effect - damages all targets in a radius around the impact point.
    /// Can be used with any skill for explosion, shockwave, or area damage effects.
    /// Supports damage falloff from center to edge.
    /// </summary>
    [Serializable]
    public class AOEOnImpactEffect : SkillEffect
    {
        [Header("Invulnerable Override")]
        [Tooltip("If true, this damage ignores the Invulnerable status effect on targets.\n" +
                 "The target's invulnerable state is suppressed for each damage call only — it is restored immediately after.")]
        [SerializeField] private bool ignoreInvulnerable = false;

        [Header("AOE Settings")]
        [Tooltip("Radius of the area of effect.\n" +
                 "All valid targets within this radius receive damage/effects.")]
        [SerializeField, Range(0.5f, 100f)] private float aoeRadius = 5f;
        
        [Tooltip("When enabled, the AOE uses full 3D (spherical) distance so it can reach targets above and below.\n" +
                 "When disabled (default), the AOE uses 2D (XZ plane) distance — ground-level effects like shockwaves\n" +
                 "won't reach flying targets overhead.")]
        [SerializeField] private bool use3DAOE = false;
        
        [Tooltip("Maximum number of targets hit by AOE (0 = unlimited).")]
        [SerializeField, Range(0, 50)] private int maxTargets = 0;
        
        [Tooltip("Effect intensity multiplier at edge of AOE.\n" +
                 "Center always receives 100%.\n" +
                 "0.3 = edge receives 30% damage.")]
        [SerializeField, Range(0f, 1f)] private float falloffAtEdge = 0.3f;
        
        [Header("Damage Settings")]
        [Tooltip("How the damage value is calculated:\n" +
                 "• Fixed Value: Use the exact damage amount specified\n" +
                 "• Base Damage Multiplier: Multiply by the caster's base attack damage (set in EnemyCrowd > Melee Attack Damage)")]
        [SerializeField] private DamageMode damageMode = DamageMode.FixedValue;
        
        [Tooltip("When Mode = Fixed Value: Exact damage dealt at center of AOE.\n" +
                 "When Mode = Base Damage Multiplier: Multiplier applied to EnemyCrowd.meleeAttackDamage.\n" +
                 "0 = use damage from other DamageEffect in this skill.")]
        [SerializeField] private float baseDamage = 0f;
        
        [Tooltip("Type of damage for resistance calculations.")]
        [SerializeField] private DamageType damageType = DamageType.Physical;
        
        [Tooltip("AOE center point.\n" +
                 "• AtTarget: Center on primary target's position\n" +
                 "• AtCaster: Center on caster's position\n" +
                 "• AtTargetPosition: Center on the targeted ground position")]
        [SerializeField] private AOECenterMode centerMode = AOECenterMode.AtTarget;
        
        [Tooltip("Can this AOE hit enemies?")]
        [SerializeField] private bool canHitEnemies = true;
        
        [Tooltip("Can this AOE hit allies (for healing/buff AOE)?")]
        [SerializeField] private bool canHitAllies = false;
        
        [Tooltip("Can this AOE hit the caster?")]
        [SerializeField] private bool canHitSelf = false;

        [Tooltip("If true, this effect can critically strike.")]
        [SerializeField] private bool canCrit = true;
        
        public float AOERadius => aoeRadius;
        public bool Use3DAOE => use3DAOE;
        public int MaxTargets => maxTargets;
        public float FalloffAtEdge => falloffAtEdge;
        public float BaseDamage => baseDamage;
        public DamageType DamageTypeProp => damageType;
        
        /// <summary>
        /// Calculates effect multiplier for a target at the given distance from center.
        /// </summary>
        public float GetFalloffMultiplier(float distanceFromCenter)
        {
            if (distanceFromCenter <= 0f)
                return 1f;
            
            if (distanceFromCenter >= aoeRadius)
                return 0f;
            
            // Linear falloff from center to edge
            float t = distanceFromCenter / aoeRadius;
            return Mathf.Lerp(1f, falloffAtEdge, t);
        }
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context.Controller == null)
                return;
            
            // Determine AOE center point
            Vector3 center = GetAOECenter(context);
            
            // Find all potential targets in the AOE
            int agentCount = context.Controller.GetAgentCount();
            var hitTargets = new System.Collections.Generic.List<(CrowdAgent agent, float distance, float multiplier)>();
            
            for (int i = 0; i < agentCount; i++)
            {
                var agent = context.Controller.GetAgent(i);
                if (agent == null || agent.isDead)
                    continue;
                
                // Check self targeting
                if (agent == context.Caster && !canHitSelf)
                    continue;
                
                // Check if target is valid based on faction
                bool isEnemy = context.Caster != null && AreAgentsEnemies(context.Caster, agent);
                bool isAlly = context.Caster != null && !isEnemy && agent != context.Caster;
                
                if (isEnemy && !canHitEnemies)
                    continue;
                if (isAlly && !canHitAllies)
                    continue;
                if (agent == context.Caster && !canHitSelf)
                    continue;
                
                Vector3 agentPos = agent.WorldPosition;
                
                // Use 2D (XZ plane) or 3D (spherical) distance based on setting
                float distance;
                if (use3DAOE)
                {
                    distance = Vector3.Distance(center, agentPos);
                }
                else
                {
                    // 2D distance on XZ plane — ground effects don't reach flying targets above
                    float dx = agentPos.x - center.x;
                    float dz = agentPos.z - center.z;
                    distance = Mathf.Sqrt(dx * dx + dz * dz);
                }
                
                // Must be within radius
                if (distance > aoeRadius)
                    continue;
                
                float multiplier = GetFalloffMultiplier(distance);
                hitTargets.Add((agent, distance, multiplier));
            }
            
            // Sort by distance (closest first for max target limit)
            hitTargets.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            // Apply damage to targets
            int hitCount = 0;
            var affectedTargets = new System.Collections.Generic.List<CrowdAgent>();
            
            foreach (var (target, distance, multiplier) in hitTargets)
            {
                // Check max targets
                if (maxTargets > 0 && hitCount >= maxTargets)
                    break;
                
                // Flying immunity: skip airborne targets when the skill cannot reach them
                if (target.isFlying && context.Skill != null && !context.Skill.CanDamageFlying)
                    continue;
                
                // Calculate damage with falloff
                float damage = 0f;
                
                if (baseDamage > 0)
                {
                    if (damageMode == DamageMode.BaseDamageMultiplier)
                    {
                        // baseDamage is a multiplier of meleeAttackDamage
                        float baseAttack = context.Caster?.enemyCrowd?.meleeAttackDamage ?? 10f;
                        damage = baseAttack * baseDamage * multiplier;
                    }
                    else
                    {
                        // Fixed value
                        damage = baseDamage * multiplier;
                    }
                }
                
                // Apply damage if we have base damage
                if (damage > 0)
                {
                    // Apply skill scaling
                    if (context.Skill != null && context.Skill.HasScaling)
                    {
                        damage = context.Skill.GetScaledDamage(damage, context.Caster);
                    }
                    
                    // Apply crit
                    if (context.IsCritical && canCrit)
                    {
                        damage *= context.Skill?.CriticalMultiplier ?? 2f;
                    }
                    
                    // Apply effect multiplier
                    damage *= context.EffectMultiplier;
                    
                    // Deal damage
                    Vector3 damageDir = target.WorldPosition - center;
                    damageDir = damageDir.normalized;
                    
                    // Suppress invulnerability if configured
                    bool wasInvulnerable = ignoreInvulnerable ? SuppressInvulnerable(target) : false;

                    // Send raw damage - server applies resistance
                    var combatSystem = context.Controller.GetCombatSystem();
                    if (combatSystem != null)
                    {
                        var networkDamageType = damageType switch
                        {
                            DamageType.Physical => Networking.DamageType.Physical,
                            DamageType.Magical => Networking.DamageType.Magical,
                            DamageType.Fire => Networking.DamageType.Fire,
                            DamageType.Ice => Networking.DamageType.Ice,
                            DamageType.Lightning => Networking.DamageType.Lightning,
                            DamageType.Poison => Networking.DamageType.Poison,
                            DamageType.True => Networking.DamageType.True,
                            _ => Networking.DamageType.AOE
                        };
                        combatSystem.RequestDamage(target.index, damage, context.Caster?.index ?? -1, 
                            target.WorldPosition, networkDamageType);
                    }
                    else
                    {
                        context.Controller.DealDamageToAgent(target.index, damage, damageDir, context.Caster?.index ?? -1);
                    }

                    // Restore invulnerability
                    if (ignoreInvulnerable) RestoreInvulnerable(target, wasInvulnerable);
                }
                
                affectedTargets.Add(target);
                hitCount++;
            }
            
            // Update context's AllTargets to include all AOE hits
            if (affectedTargets.Count > 0)
            {
                var currentTargets = context.AllTargets != null 
                    ? new System.Collections.Generic.List<CrowdAgent>(context.AllTargets) 
                    : new System.Collections.Generic.List<CrowdAgent>();
                foreach (var target in affectedTargets)
                {
                    if (!currentTargets.Contains(target))
                        currentTargets.Add(target);
                }
                context.AllTargets = currentTargets.ToArray();
            }
        }
        
        private Vector3 GetAOECenter(SkillExecutionContext context)
        {
            switch (centerMode)
            {
                case AOECenterMode.AtTarget:
                    if (context.PrimaryTarget != null)
                    {
                        return context.PrimaryTarget.WorldPosition;
                    }
                    return context.TargetPosition;
                    
                case AOECenterMode.AtCaster:
                    if (context.Caster != null)
                    {
                        return context.Caster.WorldPosition;
                    }
                    return Vector3.zero;
                    
                case AOECenterMode.AtTargetPosition:
                    return context.TargetPosition;
                    
                default:
                    return context.TargetPosition;
            }
        }
        
        public override string GetDescription()
        {
            string shape = use3DAOE ? "3D sphere" : "2D ground";
            string desc = $"AOE explosion (radius: {aoeRadius}m, {shape})";
            if (maxTargets > 0)
                desc += $", max {maxTargets} targets";
            if (baseDamage > 0)
            {
                if (damageMode == DamageMode.BaseDamageMultiplier)
                    desc += $", dealing {baseDamage * 100}% base attack as {damageType} damage";
                else
                    desc += $", dealing {baseDamage} {damageType} damage";
            }
            if (falloffAtEdge < 1f)
                desc += $" ({falloffAtEdge * 100}% at edge)";
            if (ignoreInvulnerable)
                desc += " (ignores invulnerability)";
            return desc;
        }
    }
    
    public enum AOECenterMode
    {
        AtTarget,
        AtCaster,
        AtTargetPosition
    }
    
    /// <summary>
    /// Charge effect - combines movement with continuous piercing damage during the charge.
    /// Tramples through enemies, dealing damage to each one passed.
    /// Optionally applies AOE impact and knockback at the end of the charge.
    /// This is a complete replacement for the old ChargeAttackConfig system.
    /// </summary>
    [Serializable]
    public class ChargeEffect : SkillEffect
    {
        [Header("═══ INVULNERABLE OVERRIDE ═══")]
        [Tooltip("If true, all damage from this charge (pierce and impact) ignores the Invulnerable status effect on targets.\n" +
                 "The target's invulnerable state is suppressed for each damage call only — it is restored immediately after.")]
        [SerializeField] private bool ignoreInvulnerable = false;

        [Header("═══ CHARGE MOVEMENT ═══")]
        [Tooltip("How the charge distance is determined:\n" +
                 "• Use Skill Range: Uses Max Range from Targeting tab (single source of truth)\n" +
                 "• Fixed Distance: Charge exactly the specified distance\n" +
                 "• Toward Target Capped: Charge toward target up to max distance")]
        [SerializeField] private ChargeDistanceMode distanceMode = ChargeDistanceMode.UseSkillRange;
        
        [Tooltip("Distance to charge (used when Distance Mode is 'Fixed Distance').")]
        [SerializeField] private float chargeDistance = 10f;
        
        [Tooltip("Maximum distance when charging toward a target (used when Distance Mode is 'Toward Target Capped').")]
        [SerializeField] private float maxChargeDistance = 15f;
        
        [Tooltip("Speed multiplier during charge.\n" +
                 "Higher values = faster charge.\n" +
                 "Final speed = base agent speed * speedMultiplier")]
        [SerializeField, Range(1f, 10f)] private float speedMultiplier = 4f;
        
        [Tooltip("Duration of the charge in seconds.\n" +
                 "If 0, calculated from distance and speed.")]
        [SerializeField] private float chargeDuration = 0f;
        
        [Tooltip("Is caster invulnerable during charge?")]
        [SerializeField] private bool invulnerableDuring = false;
        
        [Tooltip("Minimum caster level required for invulnerability to activate.\n" +
                 "Level is read from EnemyCrowd.baseLevel at runtime.")]
        [SerializeField, Range(1, 99)] private int invulnerableRequiredLevel = 1;
        
        [Header("═══ PIERCE/TRAMPLE DAMAGE ═══")]
        [Tooltip("Enable piercing damage during charge.\n" +
                 "Damages all enemies the agent passes through.")]
        [SerializeField] private bool enablePiercing = true;
        
        [Tooltip("Minimum caster level required for piercing to activate.\n" +
                 "Level is read from EnemyCrowd.baseLevel at runtime.")]
        [SerializeField, Range(1, 99)] private int piercingRequiredLevel = 1;
        
        [Tooltip("Width of the trample hitbox.\n" +
                 "Enemies within this distance of the charge path take damage.")]
        [SerializeField, Range(0.5f, 5f)] private float pierceWidth = 1.5f;
        
        [Tooltip("How the pierce damage is calculated:\n" +
                 "• Fixed Value: Use the exact damage amount\n" +
                 "• Base Damage Multiplier: Multiply by the caster's base attack damage (set in EnemyCrowd > Melee Attack Damage)")]
        [SerializeField] private DamageMode pierceDamageMode = DamageMode.FixedValue;
        
        [Tooltip("When Mode = Fixed Value: Damage dealt to each trampled enemy.\n" +
                 "When Mode = Base Damage Multiplier: Multiplier applied to EnemyCrowd.meleeAttackDamage.")]
        [SerializeField] private float pierceDamage = 20f;
        
        [Tooltip("Damage multiplier for subsequent pierced targets.\n" +
                 "1.0 = full damage to all, 0.7 = 70% each subsequent target.")]
        [SerializeField, Range(0.1f, 1f)] private float pierceFalloff = 1f;
        
        [Tooltip("Maximum enemies that can be trampled (0 = unlimited).")]
        [SerializeField, Range(0, 20)] private int maxPierceTargets = 0;
        
        [Tooltip("Minimum distance between trample damage applications.\n" +
                 "Prevents the same enemy from being hit multiple times.")]
        [SerializeField, Range(0.5f, 3f)] private float pierceTickDistance = 1f;
        
        [Header("═══ IMPACT DAMAGE (END OF CHARGE) ═══")]
        [Tooltip("Enable impact damage at the end of the charge.")]
        [SerializeField] private bool enableImpact = true;
        
        [Tooltip("Minimum caster level required for impact damage to activate.\n" +
                 "Level is read from EnemyCrowd.baseLevel at runtime.")]
        [SerializeField, Range(1, 99)] private int impactRequiredLevel = 1;
        
        [Tooltip("How the impact damage is calculated:\n" +
                 "• Fixed Value: Use the exact damage amount\n" +
                 "• Base Damage Multiplier: Multiply by the caster's base attack damage (set in EnemyCrowd > Melee Attack Damage)")]
        [SerializeField] private DamageMode impactDamageMode = DamageMode.FixedValue;
        
        [Tooltip("When Mode = Fixed Value: Damage dealt at impact point.\n" +
                 "When Mode = Base Damage Multiplier: Multiplier applied to EnemyCrowd.meleeAttackDamage.")]
        [SerializeField] private float impactDamage = 30f;
        
        [Tooltip("Enable AOE at impact point.")]
        [SerializeField] private bool enableImpactAOE = true;
        
        [Tooltip("Minimum caster level required for impact AOE to activate.\n" +
                 "Level is read from EnemyCrowd.baseLevel at runtime.")]
        [SerializeField, Range(1, 99)] private int impactAOERequiredLevel = 1;
        
        [Tooltip("Radius of AOE damage on impact.")]
        [SerializeField, Range(0f, 15f)] private float impactAOERadius = 3f;
        
        [Tooltip("Damage falloff at edge of impact AOE.\n" +
                 "0.3 = 30% damage at edge, 100% at center.")]
        [SerializeField, Range(0f, 1f)] private float impactAOEFalloff = 0.3f;

        [Tooltip("If true, this effect can critically strike.")]
        [SerializeField] private bool canCrit = true;
        
        [Header("═══ KNOCKBACK ═══")]
        [Tooltip("Apply knockback to trampled enemies.")]
        [SerializeField] private bool enablePierceKnockback = false;
        
        [Tooltip("Minimum caster level required for pierce knockback to activate.")]
        [SerializeField, Range(1, 99)] private int pierceKnockbackRequiredLevel = 1;
        
        [Tooltip("Knockback force for trampled enemies.")]
        [SerializeField, Range(0f, 20f)] private float pierceKnockbackForce = 5f;
        
        [Tooltip("Apply knockback at impact point.")]
        [SerializeField] private bool enableImpactKnockback = true;
        
        [Tooltip("Minimum caster level required for impact knockback to activate.")]
        [SerializeField, Range(1, 99)] private int impactKnockbackRequiredLevel = 1;
        
        [Tooltip("Knockback force at impact.")]
        [SerializeField, Range(0f, 20f)] private float impactKnockbackForce = 8f;
        
        [Tooltip("Vertical lift force during knockback.\n" +
                 "Values >= 1.5 trigger get-up animations.")]
        [SerializeField, Range(0f, 10f)] private float knockbackLiftForce = 2f;
        
        [Tooltip("Duration of knockback movement.")]
        [SerializeField, Range(0.2f, 2f)] private float knockbackDuration = 0.5f;
        
        [Header("═══ DAMAGE TYPE ═══")]
        [Tooltip("Type of damage for resistance calculations.")]
        [SerializeField] private DamageType damageType = DamageType.Physical;
        
        [Header("═══ STATUS EFFECT ON HIT ═══")]
        [Tooltip("Apply a status effect to trampled enemies during the charge.")]
        [SerializeField] private bool applyStatusOnPierce = false;
        
        [Tooltip("Minimum caster level required for pierce status effect to activate.")]
        [SerializeField, Range(1, 99)] private int pierceStatusRequiredLevel = 1;
        
        [Tooltip("Apply a status effect to enemies hit at the impact point.")]
        [SerializeField] private bool applyStatusOnImpact = false;
        
        [Tooltip("Minimum caster level required for impact status effect to activate.")]
        [SerializeField, Range(1, 99)] private int impactStatusRequiredLevel = 1;
        
        [Tooltip("The status effect to apply (e.g., Stun, Slow, Burn).\n" +
                 "Only applied if 'Apply Status On Pierce' or 'Apply Status On Impact' is enabled.")]
        [SerializeField] private StatusEffectType statusEffect = StatusEffectType.Stun;
        
        [Tooltip("Duration of the status effect in seconds.")]
        [SerializeField] private float statusDuration = 2f;
        
        [Tooltip("Value/strength of the status effect.\n" +
                 "Interpretation depends on the status type (e.g., slow percentage, damage per tick).")]
        [SerializeField] private float statusValue = 0f;
        
        [Tooltip("Number of stacks to apply (for stackable effects).")]
        [SerializeField] private int statusStacks = 1;
        
        /// <summary>
        /// Gets the caster's level from their EnemyCrowd configuration.
        /// </summary>
        private int GetCasterLevel(CrowdAgent caster)
        {
            return caster?.enemyCrowd?.baseLevel ?? 1;
        }
        
        /// <summary>
        /// Checks if a feature is enabled AND the caster meets the level requirement.
        /// </summary>
        private bool IsFeatureActive(bool enabled, int requiredLevel, int casterLevel)
        {
            return enabled && casterLevel >= requiredLevel;
        }
        
        public override void Execute(SkillExecutionContext context)
        {
            if (context.Caster == null || context.Controller == null)
                return;
            
            // Get caster level for feature checks
            int casterLevel = GetCasterLevel(context.Caster);
            
            Vector3 startPos = context.Caster.WorldPosition;
            
            // Determine charge direction and distance
            Vector3 targetPos = context.PrimaryTarget != null ? context.PrimaryTarget.WorldPosition : context.TargetPosition;
            Vector3 direction = (targetPos - startPos).normalized;
            if (direction == Vector3.zero)
                direction = context.Caster.navigationAgent?.Transform?.forward ?? context.Caster.transform?.forward ?? Vector3.forward;
            
            // Calculate actual charge distance based on mode
            float distToTarget = Vector3.Distance(startPos, targetPos);
            float actualDistance = distanceMode switch
            {
                ChargeDistanceMode.UseSkillRange => context.Skill != null 
                    ? Mathf.Min(distToTarget, context.Skill.MaxRange) 
                    : Mathf.Min(distToTarget, maxChargeDistance),
                ChargeDistanceMode.FixedDistance => chargeDistance,
                ChargeDistanceMode.TowardTargetCapped => Mathf.Min(distToTarget, maxChargeDistance),
                _ => Mathf.Min(distToTarget, maxChargeDistance)
            };
            
            // Calculate charge duration
            float baseSpeed = context.Caster.navigationAgent?.Speed ?? 3.5f;
            float chargeSpeed = baseSpeed * speedMultiplier;
            float duration = chargeDuration > 0 ? chargeDuration : actualDistance / chargeSpeed;
            
            Vector3 endPos = startPos + direction * actualDistance;
            
            // Start the charge coroutine for continuous piercing
            context.Controller.StartCoroutine(ExecuteChargeCoroutine(
                context, startPos, endPos, direction, actualDistance, duration, chargeSpeed, casterLevel
            ));
        }
        
        private System.Collections.IEnumerator ExecuteChargeCoroutine(
            SkillExecutionContext context,
            Vector3 startPos,
            Vector3 endPos,
            Vector3 direction,
            float distance,
            float duration,
            float speed,
            int casterLevel)
        {
            var caster = context.Caster;
            if (caster == null) yield break;
            
            float startTime = Time.time;
            float endTime = startTime + duration;
            
            // Apply movement impulse
            ApplyChargeImpulse(caster, direction, distance, duration);
            
            // Apply invulnerability if configured and level requirement met
            if (IsFeatureActive(invulnerableDuring, invulnerableRequiredLevel, casterLevel) && duration > 0)
            {
                context.Controller.ApplyStatusEffect(
                    caster.index,
                    StatusEffectType.Invulnerable,
                    duration,
                    0f,
                    tickInterval: 1f,
                    sourceAgentIndex: caster.index,
                    maxStacks: 1
                );
            }
            
            // Track pierced enemies to avoid hitting the same one multiple times
            var piercedEnemies = new System.Collections.Generic.HashSet<int>();
            var lastPiercePositions = new System.Collections.Generic.Dictionary<int, Vector3>();
            int pierceCount = 0;
            
            // Continuous pierce checking during charge (if enabled and level requirement met)
            if (IsFeatureActive(enablePiercing, piercingRequiredLevel, casterLevel))
            {
                while (Time.time < endTime && caster != null && !caster.isDead)
                {
                    Vector3 currentPos = caster.WorldPosition;
                    
                    // Find enemies in the trample path
                    int agentCount = context.Controller.GetAgentCount();
                    for (int i = 0; i < agentCount; i++)
                    {
                        if (maxPierceTargets > 0 && pierceCount >= maxPierceTargets)
                            break;
                        
                        var target = context.Controller.GetAgent(i);
                        if (target == null || target.isDead || target == caster)
                            continue;
                        
                        // Check if enemy
                        if (!AreAgentsEnemies(caster, target))
                            continue;
                        
                        Vector3 targetPos = target.WorldPosition;
                        
                        // Check if within trample width
                        float distToPath = DistanceToLine(targetPos, currentPos, direction);
                        if (distToPath > pierceWidth * 0.5f)
                            continue;
                        
                        // Check if in front of us (along charge direction)
                        float distAlongPath = Vector3.Dot(targetPos - currentPos, direction);
                        if (distAlongPath < -1f || distAlongPath > speed * 0.2f) // Small lookahead
                            continue;
                        
                        // Check minimum distance since last hit on this target
                        if (piercedEnemies.Contains(target.index))
                        {
                            if (lastPiercePositions.TryGetValue(target.index, out Vector3 lastPos))
                            {
                                if (Vector3.Distance(currentPos, lastPos) < pierceTickDistance)
                                    continue;
                            }
                        }
                        
                        // Apply pierce damage with falloff
                        float falloffMultiplier = Mathf.Pow(pierceFalloff, pierceCount);
                        float damage;
                        
                        if (pierceDamageMode == DamageMode.BaseDamageMultiplier)
                        {
                            float baseAttack = caster.enemyCrowd?.meleeAttackDamage ?? 10f;
                            damage = baseAttack * pierceDamage * falloffMultiplier;
                        }
                        else
                        {
                            damage = pierceDamage * falloffMultiplier;
                        }
                        
                        // Apply scaling
                        if (context.Skill != null && context.Skill.HasScaling)
                            damage = context.Skill.GetScaledDamage(damage, caster);
                        
                        // Apply crit
                        if (context.IsCritical && canCrit)
                            damage *= context.Skill?.CriticalMultiplier ?? 2f;
                        
                        damage *= context.EffectMultiplier;
                        
                        // Deal damage
                        ApplyDamage(context, target, damage, direction);
                        
                        // Apply knockback if enabled and level requirement met
                        if (IsFeatureActive(enablePierceKnockback, pierceKnockbackRequiredLevel, casterLevel))
                        {
                            ApplyKnockback(target, direction, pierceKnockbackForce, knockbackLiftForce * 0.5f, knockbackDuration);
                        }
                        
                        // Apply status effect if enabled and level requirement met
                        if (IsFeatureActive(applyStatusOnPierce, pierceStatusRequiredLevel, casterLevel))
                        {
                            ApplyStatusToTarget(context, target);
                        }
                        
                        // Track this hit
                        piercedEnemies.Add(target.index);
                        lastPiercePositions[target.index] = currentPos;
                        pierceCount++;
                    }
                    
                    yield return null; // Check every frame
                }
            }
            else
            {
                // No piercing, just wait for charge to complete
                yield return new WaitForSeconds(duration);
            }
            
            // Impact at end of charge (if enabled and level requirement met)
            if (IsFeatureActive(enableImpact, impactRequiredLevel, casterLevel) && caster != null && !caster.isDead)
            {
                Vector3 impactPos = caster.WorldPosition;
                
                // Check if AOE impact is active (both enabled and level requirement met)
                if (IsFeatureActive(enableImpactAOE, impactAOERequiredLevel, casterLevel))
                {
                    // AOE impact
                    int agentCount = context.Controller.GetAgentCount();
                    var hitTargets = new System.Collections.Generic.List<(CrowdAgent agent, float dist)>();
                    
                    for (int i = 0; i < agentCount; i++)
                    {
                        var target = context.Controller.GetAgent(i);
                        if (target == null || target.isDead || target == caster)
                            continue;
                        
                        if (!AreAgentsEnemies(caster, target))
                            continue;
                        
                        Vector3 targetPos = target.WorldPosition;
                        
                        float dist = Vector3.Distance(impactPos, targetPos);
                        if (dist <= impactAOERadius)
                        {
                            hitTargets.Add((target, dist));
                        }
                    }
                    
                    // Apply impact damage with falloff
                    foreach (var (target, dist) in hitTargets)
                    {
                        float falloff = 1f - (dist / impactAOERadius) * (1f - impactAOEFalloff);
                        float damage;
                        
                        if (impactDamageMode == DamageMode.BaseDamageMultiplier)
                        {
                            float baseAttack = caster.enemyCrowd?.meleeAttackDamage ?? 10f;
                            damage = baseAttack * impactDamage * falloff;
                        }
                        else
                        {
                            damage = impactDamage * falloff;
                        }
                        
                        // Apply scaling
                        if (context.Skill != null && context.Skill.HasScaling)
                            damage = context.Skill.GetScaledDamage(damage, caster);
                        
                        // Apply crit
                        if (context.IsCritical && canCrit)
                            damage *= context.Skill?.CriticalMultiplier ?? 2f;
                        
                        damage *= context.EffectMultiplier;
                        
                        Vector3 knockbackDir = target.WorldPosition - impactPos;
                        knockbackDir = knockbackDir.normalized;
                        if (knockbackDir == Vector3.zero) knockbackDir = direction;
                        
                        ApplyDamage(context, target, damage, knockbackDir);
                        
                        // Apply impact knockback if enabled and level requirement met
                        if (IsFeatureActive(enableImpactKnockback, impactKnockbackRequiredLevel, casterLevel))
                        {
                            ApplyKnockback(target, knockbackDir, impactKnockbackForce, knockbackLiftForce, knockbackDuration);
                        }
                        
                        // Apply status effect if enabled and level requirement met
                        if (IsFeatureActive(applyStatusOnImpact, impactStatusRequiredLevel, casterLevel))
                        {
                            ApplyStatusToTarget(context, target);
                        }
                    }
                }
                else
                {
                    // Single target impact (primary target or closest)
                    var target = context.PrimaryTarget;
                    if (target != null && !target.isDead && AreAgentsEnemies(caster, target))
                    {
                        float damage;
                        
                        if (impactDamageMode == DamageMode.BaseDamageMultiplier)
                        {
                            float baseAttack = caster.enemyCrowd?.meleeAttackDamage ?? 10f;
                            damage = baseAttack * impactDamage;
                        }
                        else
                        {
                            damage = impactDamage;
                        }
                        
                        if (context.Skill != null && context.Skill.HasScaling)
                            damage = context.Skill.GetScaledDamage(damage, caster);
                        
                        if (context.IsCritical && canCrit)
                            damage *= context.Skill?.CriticalMultiplier ?? 2f;
                        
                        damage *= context.EffectMultiplier;
                        
                        ApplyDamage(context, target, damage, direction);
                        
                        // Apply impact knockback if enabled and level requirement met
                        if (IsFeatureActive(enableImpactKnockback, impactKnockbackRequiredLevel, casterLevel))
                        {
                            ApplyKnockback(target, direction, impactKnockbackForce, knockbackLiftForce, knockbackDuration);
                        }
                        
                        // Apply status effect if enabled and level requirement met
                        if (IsFeatureActive(applyStatusOnImpact, impactStatusRequiredLevel, casterLevel))
                        {
                            ApplyStatusToTarget(context, target);
                        }
                    }
                }
            }
        }
        
        private void ApplyStatusToTarget(SkillExecutionContext context, CrowdAgent target)
        {
            if (target == null || target.isDead || context.Controller == null)
                return;
            
            float scaledDuration = statusDuration;
            float scaledValue = statusValue;
            
            // Apply skill scaling if available
            if (context.Skill != null && context.Skill.HasScaling)
            {
                scaledDuration = context.Skill.GetScaledDuration(statusDuration, context.Caster);
                scaledValue = context.Skill.GetScaledStatusEffect(statusValue, context.Caster);
            }
            
            context.Controller.ApplyStatusEffect(
                target.index,
                statusEffect,
                scaledDuration,
                scaledValue,
                tickInterval: 1f,
                sourceAgentIndex: context.Caster?.index ?? -1,
                maxStacks: statusStacks
            );
        }
        
        private void ApplyChargeImpulse(CrowdAgent caster, Vector3 direction, float distance, float duration)
        {
            if (caster == null) return;
            
            float now = Time.time;
            float horizontalSpeed = distance / duration;
            
            Vector3 velocity = direction * horizontalSpeed;
            bool handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                caster,
                velocity,
                duration,
                0f,
                0f,
                horizontalSpeed,
                0f);
            
            if (!handledByAstar)
            {
                caster.hasNavmeshOffsetImpulse = true;
                caster.navmeshOffsetVelocity = velocity;
                caster.navmeshOffsetGroundPosition = caster.WorldPosition;
                caster.navmeshOffset = Vector3.zero;
                caster.navmeshOffsetDuration = duration;
                caster.navmeshOffsetEndTime = now + duration;
                caster.navmeshOffsetGravity = 0f;
                caster.navmeshOffsetDrag = 0f;
                
                // Animation system fields
                caster.lastNavmeshImpulsePush = horizontalSpeed;
                caster.lastNavmeshImpulseLift = 0f;
                caster.navmeshImpulseAnimationPlayed = false;
                caster.liftImpulseActive = false;
            }
            
            // Lock attacks during charge
            caster.impulseAttackLockEndTime = Mathf.Max(caster.impulseAttackLockEndTime, now + duration + 0.3f);
        }
        
        private void ApplyDamage(SkillExecutionContext context, CrowdAgent target, float damage, Vector3 direction)
        {
            // Flying immunity: skip airborne targets when the skill cannot reach them
            if (target.isFlying && context.Skill != null && !context.Skill.CanDamageFlying)
                return;
            
            // Suppress invulnerability if configured
            bool wasInvulnerable = ignoreInvulnerable ? SuppressInvulnerable(target) : false;

            // Send raw damage - server applies resistance
            var combatSystem = context.Controller.GetCombatSystem();
            if (combatSystem != null)
            {
                var networkDamageType = damageType switch
                {
                    DamageType.Physical => Networking.DamageType.Physical,
                    DamageType.Magical => Networking.DamageType.Magical,
                    DamageType.Fire => Networking.DamageType.Fire,
                    DamageType.Ice => Networking.DamageType.Ice,
                    DamageType.Lightning => Networking.DamageType.Lightning,
                    DamageType.Poison => Networking.DamageType.Poison,
                    DamageType.True => Networking.DamageType.True,
                    _ => Networking.DamageType.Normal
                };
                combatSystem.RequestDamage(target.index, damage, context.Caster?.index ?? -1, 
                    target.WorldPosition, networkDamageType);
            }
            else
            {
                context.Controller.DealDamageToAgent(target.index, damage, direction, context.Caster?.index ?? -1);
            }

            // Restore invulnerability
            if (ignoreInvulnerable) RestoreInvulnerable(target, wasInvulnerable);
        }
        
        private void ApplyKnockback(CrowdAgent target, Vector3 direction, float force, float lift, float duration)
        {
            if (target == null) return;
            
            float now = Time.time;
            Vector3 velocity = direction * force;
            velocity.y += lift;
            bool handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                target,
                velocity,
                duration,
                4f,
                25f,
                force,
                lift);
            
            if (!handledByAstar)
            {
                target.hasNavmeshOffsetImpulse = true;
                target.navmeshOffsetVelocity = velocity;
                target.navmeshOffsetGroundPosition = target.WorldPosition;
                target.navmeshOffset = Vector3.zero;
                target.navmeshOffsetDuration = duration;
                target.navmeshOffsetEndTime = now + duration;
                target.navmeshOffsetGravity = 25f;
                target.navmeshOffsetDrag = 4f;
                
                target.lastNavmeshImpulsePush = force;
                target.lastNavmeshImpulseLift = lift;
                target.navmeshImpulseAnimationPlayed = false;
                
                const float LiftGetUpThreshold = 1.5f;
                target.liftImpulseActive = lift >= LiftGetUpThreshold;
            }
            
            target.impulseAttackLockEndTime = Mathf.Max(target.impulseAttackLockEndTime, now + duration + 0.5f);
        }
        
        /// <summary>
        /// Calculate perpendicular distance from a point to a line.
        /// </summary>
        private float DistanceToLine(Vector3 point, Vector3 lineStart, Vector3 lineDirection)
        {
            Vector3 toPoint = point - lineStart;
            Vector3 projected = Vector3.Project(toPoint, lineDirection);
            return (toPoint - projected).magnitude;
        }
        
        /// <summary>
        /// Helper to format level requirement text for descriptions.
        /// </summary>
        private string LevelReq(int level) => level > 1 ? $" [Lv{level}+]" : "";
        
        public override string GetDescription()
        {
            return GetDescription(null);
        }
        
        public override string GetDescription(AgentSkill skill)
        {
            var parts = new System.Collections.Generic.List<string>();
            
            // Distance description based on mode
            string distanceText = distanceMode switch
            {
                ChargeDistanceMode.UseSkillRange => skill != null 
                    ? $"Charge {skill.MaxRange}m (skill range)" 
                    : "Charge (skill range)",
                ChargeDistanceMode.FixedDistance => $"Charge {chargeDistance}m",
                ChargeDistanceMode.TowardTargetCapped => $"Charge up to {maxChargeDistance}m",
                _ => "Charge"
            };
            parts.Add(distanceText);
            
            if (enablePiercing)
            {
                string pierceDmg = pierceDamageMode == DamageMode.BaseDamageMultiplier
                    ? $"{pierceDamage * 100}% base attack"
                    : $"{pierceDamage} damage";
                parts.Add($"trample for {pierceDmg}{LevelReq(piercingRequiredLevel)}");
            }
            
            if (enableImpact)
            {
                string impactDmg = impactDamageMode == DamageMode.BaseDamageMultiplier
                    ? $"{impactDamage * 100}% base attack"
                    : $"{impactDamage} damage";
                string aoe = enableImpactAOE ? $" AOE {impactAOERadius}m{LevelReq(impactAOERequiredLevel)}" : "";
                parts.Add($"impact {impactDmg}{LevelReq(impactRequiredLevel)}{aoe}");
            }
            
            if (enablePierceKnockback || enableImpactKnockback)
            {
                var kbParts = new System.Collections.Generic.List<string>();
                if (enablePierceKnockback) kbParts.Add($"pierce{LevelReq(pierceKnockbackRequiredLevel)}");
                if (enableImpactKnockback) kbParts.Add($"impact{LevelReq(impactKnockbackRequiredLevel)}");
                parts.Add($"knockback ({string.Join("/", kbParts)})");
            }
            
            if (applyStatusOnPierce || applyStatusOnImpact)
            {
                var statusParts = new System.Collections.Generic.List<string>();
                if (applyStatusOnPierce) statusParts.Add($"pierce{LevelReq(pierceStatusRequiredLevel)}");
                if (applyStatusOnImpact) statusParts.Add($"impact{LevelReq(impactStatusRequiredLevel)}");
                parts.Add($"{statusEffect} {statusDuration}s ({string.Join("/", statusParts)})");
            }
            
            if (invulnerableDuring)
            {
                parts.Add($"invulnerable{LevelReq(invulnerableRequiredLevel)}");
            }
            
            if (ignoreInvulnerable)
            {
                parts.Add("ignores invulnerability");
            }
            
            return string.Join(", ", parts);
        }
    }
}
