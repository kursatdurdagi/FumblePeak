using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// A skill slot in an EnemyCrowd's skill loadout.
    /// Wraps an AgentSkill with slot-specific settings.
    /// </summary>
    [Serializable]
    public class AgentSkillSlot
    {
        [Tooltip("The skill assigned to this slot.\n" +
                 "Drag an AgentSkill ScriptableObject here.")]
        public AgentSkill skill;
        
        [Tooltip("Is this skill slot enabled?\n" +
                 "Disabled slots are skipped by AI.")]
        public bool enabled = true;
        
        [Tooltip("Override the cooldown mode for this slot.\n" +
                 "If unchecked, uses the skill's default cooldown mode.")]
        public bool overrideCooldownMode = false;
        
        [Tooltip("Cooldown mode when override is enabled.")]
        public SkillCooldownMode cooldownModeOverride = SkillCooldownMode.PerAgent;
        
        [Tooltip("Override the priority for AI skill selection.\n" +
                 "If unchecked, uses the skill's default priority.")]
        public bool overridePriority = false;
        
        [Tooltip("Priority when override is enabled.\n" +
                 "Higher = more likely to use.")]
        public int priorityOverride = 1;
        
        [Tooltip("Slot-specific cooldown multiplier.\n" +
                 "1 = normal cooldown, 0.5 = 50% faster, 2 = 50% slower.\n" +
                 "Use for: Upgraded factions with faster abilities.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float _cooldownMultiplier = 1f;
        
        /// <summary>
        /// Slot-specific cooldown multiplier. Always returns at least 0.1f.
        /// Protects against legacy serialized data with 0 values.
        /// </summary>
        public float cooldownMultiplier
        {
            get => _cooldownMultiplier <= 0f ? 1f : _cooldownMultiplier;
            set => _cooldownMultiplier = Mathf.Max(0.1f, value);
        }
        
        /// <summary>
        /// Runtime-only per-slot control mode override for auto-cast toggling.
        /// Marked NonSerialized so stale values never persist on the ScriptableObject
        /// across play sessions (Unity editor does not revert runtime SO modifications).
        /// </summary>
        [System.NonSerialized]
        public bool overrideControlMode = false;
        
        /// <summary>
        /// The per-slot control mode when override is active.
        /// Runtime-only — not serialized for the same reasons as overrideControlMode.
        /// </summary>
        [System.NonSerialized]
        public SkillControlMode controlModeOverride = SkillControlMode.Hybrid;
        
        [Tooltip("Slot-specific damage/heal multiplier.\n" +
                 "1 = normal, 1.5 = 50% more powerful.\n" +
                 "Use for: Elite variants, upgraded factions.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float _damageMultiplier = 1f;
        
        /// <summary>
        /// Slot-specific damage/heal multiplier. Always returns at least 0.1f.
        /// Protects against legacy serialized data with 0 values.
        /// </summary>
        public float damageMultiplier
        {
            get => _damageMultiplier <= 0f ? 1f : _damageMultiplier;
            set => _damageMultiplier = Mathf.Max(0.1f, value);
        }
        
        /// <summary>
        /// The effective cooldown for this slot (skill cooldown * multiplier).
        /// </summary>
        public float EffectiveCooldown => (skill?.Cooldown ?? 0f) * cooldownMultiplier;
        
        /// <summary>
        /// Is this slot valid and usable?
        /// </summary>
        public bool IsValid => enabled && skill != null;
    }
    
    /// <summary>
    /// How the skill system is controlled for this faction.
    /// </summary>
    public enum SkillControlMode
    {
        [Tooltip("AI automatically decides when to use skills based on conditions.\n" +
                 "Best for: Enemy factions, NPC allies, automated gameplay.")]
        AIControlled,
        
        [Tooltip("Skills are only used when manually triggered via API.\n" +
                 "Best for: Player-controlled units, RTS-style gameplay.\n" +
                 "Use: FactionSkillManager.ForceUseSkill() or CrowdAgent.queuedSkillSlot")]
        ManualOnly,
        
        [Tooltip("AI controls skills by default, but can be overridden per-agent.\n" +
                 "Best for: Mixed armies with some player-controlled units.\n" +
                 "Set agent.useManualSkillControl = true for manual agents.")]
        Hybrid
    }
    
    /// <summary>
    /// Configuration for the skill system on an EnemyCrowd.
    /// </summary>
    [Serializable]
    public class SkillSystemConfig
    {
        [Header("═══ SKILL SYSTEM ═══")]
        [Tooltip("Enable the skill system for this faction.\n\n" +
                 "ON: Agents use skills from the Skill Slots list.\n" +
                 "OFF: Agents use legacy melee/ranged attacks only.\n\n" +
                 "Note: You can have both enabled during transition.")]
        public bool enableSkillSystem = false;
        
        [Tooltip("How skills are controlled for this faction.\n\n" +
                 "• AI Controlled: AI decides when to use skills (default for enemies)\n" +
                 "• Manual Only: Skills only trigger via API/player input\n" +
                 "• Hybrid: AI by default, but per-agent manual override possible")]
        public SkillControlMode controlMode = SkillControlMode.AIControlled;
        
        [Tooltip("How agents track skill cooldowns.\n\n" +
                 "• Per-Agent: Each agent has independent cooldowns.\n" +
                 "• Per-EnemyCrowd: Agents share cooldowns within the same EnemyCrowd asset.\n" +
                 "• Per-Faction: All agents of this faction share cooldowns.\n" +
                 "  Prevents skill spam in large groups.")]
        public SkillCooldownMode defaultCooldownMode = SkillCooldownMode.PerAgent;
        
        [Tooltip("Skills available to agents of this faction.\n" +
                 "Skills are evaluated by priority - higher priority skills checked first.")]
        public List<AgentSkillSlot> skillSlots = new List<AgentSkillSlot>();
        
        [Header("═══ AUTO-ATTACK ═══")]
        [Tooltip("Use Combat Template's basic attack when no skills are available.\n" +
                 "Damage/cooldown uses Combat Template's melee/ranged settings.")]
        public bool useAutoAttack = true;
        
        [Header("═══ AI BEHAVIOR ═══")]
        [Tooltip("How often the AI evaluates which skill to use (in seconds).\n" +
                 "Higher values = less CPU but slower reactions.\n" +
                 "Recommended: 0.5-1.0s for large armies.")]
        [Range(0.1f, 2f)]
        public float skillDecisionInterval = 0.5f;
        
        [Tooltip("Minimum time between ANY skill usage (global cooldown).\n" +
                 "Prevents agents from spamming multiple skills back-to-back.\n" +
                 "Recommended: 0.5-1.5s to feel natural.")]
        [Range(0f, 5f)]
        public float globalCooldown = 1f;
        
        [Tooltip("Random variation in skill timing (0-1).\n" +
                 "0 = all agents act identically (synchronized)\n" +
                 "0.3 = natural variation, feels more organic\n" +
                 "Higher values spread skill usage across time.")]
        [Range(0f, 1f)]
        public float skillTimingVariation = 0.3f;
        
        [Tooltip("Chance that an agent will consider using a skill each evaluation (0-1).\n" +
                 "1.0 = always try to use skills\n" +
                 "0.5 = 50% chance per evaluation\n" +
                 "Lower values make skill usage feel less spammy for large armies.")]
        [Range(0f, 1f)]
        public float skillUseChance = 0.7f;

        
        [Tooltip("Maximum agents that can use skills per faction per second.\n" +
                 "0 = unlimited\n" +
                 "Use to prevent overwhelming skill spam in large battles.\n" +
                 "Recommended: 5-20 for armies of 100+ agents.")]
        [Range(0, 100)]
        public int maxSkillsPerSecond = 0;
        
        [Tooltip("Should the AI prefer skills with lower cooldowns?\n" +
                 "If enabled, skills that will be ready again soon get priority.")]
        public bool preferLowerCooldowns = false;
        
        [Tooltip("Should agents wait for optimal conditions before using skills?\n" +
                 "If disabled, agents use skills whenever available and valid.\n" +
                 "If enabled, agents may wait for better opportunities (more targets, etc.)")]
        public bool conservativeSkillUsage = true;
    }
    
    /// <summary>
    /// Tracks per-agent skill cooldowns at runtime.
    /// Each agent has its own instance for independent cooldowns.
    /// </summary>
    [Serializable]
    public class SkillRuntimeState
    {
        // Cooldowns tracked by skill ID (uses AgentSkill.skillId)
        private Dictionary<string, float> _cooldownEndTimes = new Dictionary<string, float>();
        
        /// <summary>
        /// Checks if a skill is currently on cooldown.
        /// </summary>
        public bool IsOnCooldown(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return false;
            return _cooldownEndTimes.TryGetValue(skillId, out float endTime) && Time.time < endTime;
        }
        
        /// <summary>
        /// Checks if a skill is currently on cooldown.
        /// </summary>
        public bool IsOnCooldown(AgentSkill skill)
        {
            if (skill == null) return false;
            return IsOnCooldown(skill.SkillId);
        }
        
        /// <summary>
        /// Gets the remaining cooldown time for a skill.
        /// </summary>
        public float GetRemainingCooldown(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0f;
            if (_cooldownEndTimes.TryGetValue(skillId, out float endTime))
            {
                return Mathf.Max(0f, endTime - Time.time);
            }
            return 0f;
        }
        
        /// <summary>
        /// Gets the remaining cooldown time for a skill.
        /// </summary>
        public float GetRemainingCooldown(AgentSkill skill)
        {
            if (skill == null) return 0f;
            return GetRemainingCooldown(skill.SkillId);
        }
        
        /// <summary>
        /// Starts the cooldown for a skill.
        /// </summary>
        public void StartCooldown(string skillId, float duration)
        {
            if (string.IsNullOrEmpty(skillId)) return;
            _cooldownEndTimes[skillId] = Time.time + duration;
        }
        
        /// <summary>
        /// Starts the cooldown for a skill.
        /// </summary>
        public void StartCooldown(AgentSkill skill, float duration)
        {
            if (skill == null) return;
            StartCooldown(skill.SkillId, duration);
        }
        
        /// <summary>
        /// Resets all cooldowns.
        /// </summary>
        public void ResetAll()
        {
            _cooldownEndTimes.Clear();
        }
        
        /// <summary>
        /// Resets cooldown for a specific skill.
        /// </summary>
        public void ResetCooldown(string skillId)
        {
            if (_cooldownEndTimes.ContainsKey(skillId))
            {
                _cooldownEndTimes.Remove(skillId);
            }
        }
        
        /// <summary>
        /// Updates cooldowns (removes expired entries to save memory).
        /// Call this periodically for long-running sessions.
        /// </summary>
        public void Update(float deltaTime)
        {
            // Optional: cleanup expired cooldowns periodically to prevent dictionary growth
            // For most cases this isn't necessary as the dictionary stays small
        }
    }
    
    /// <summary>
    /// Tracks shared skill cooldowns (per-faction or per-enemycrowd).
    /// When an agent uses a shared cooldown mode, all agents in that scope share the cooldown.
    /// </summary>
    [Serializable]
    public class FactionSkillCooldowns
    {
        // Cooldowns tracked by faction + skill
        private Dictionary<string, float> _cooldownEndTimes = new Dictionary<string, float>();
        
        /// <summary>
        /// Checks if a skill is on shared cooldown.
        /// </summary>
        public bool IsOnCooldown(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return false;
            return _cooldownEndTimes.TryGetValue(skillId, out float endTime) && Time.time < endTime;
        }
        
        /// <summary>
        /// Gets the remaining cooldown time for a skill.
        /// </summary>
        public float GetRemainingCooldown(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0f;
            if (_cooldownEndTimes.TryGetValue(skillId, out float endTime))
            {
                return Mathf.Max(0f, endTime - Time.time);
            }
            return 0f;
        }
        
        /// <summary>
        /// Starts a shared cooldown for a skill.
        /// </summary>
        public void StartCooldown(string skillId, float duration)
        {
            if (string.IsNullOrEmpty(skillId)) return;
            _cooldownEndTimes[skillId] = Time.time + duration;
        }
        
        /// <summary>
        /// Starts a shared cooldown for a skill.
        /// </summary>
        public void StartCooldown(EnemyCrowd faction, AgentSkill skill, float duration)
        {
            if (faction == null || skill == null) return;
            StartCooldown(skill.SkillId, duration);
        }
        
        /// <summary>
        /// Resets all shared cooldowns.
        /// </summary>
        public void ResetAll()
        {
            _cooldownEndTimes.Clear();
        }
        
        /// <summary>
        /// Resets the shared cooldown for a specific skill.
        /// </summary>
        public void ResetCooldown(string skillId)
        {
            if (!string.IsNullOrEmpty(skillId) && _cooldownEndTimes.ContainsKey(skillId))
            {
                _cooldownEndTimes.Remove(skillId);
            }
        }
        
        /// <summary>
        /// Updates cooldowns.
        /// </summary>
        public void Update(float deltaTime)
        {
            // Optional: cleanup expired cooldowns periodically
        }
    }
}
