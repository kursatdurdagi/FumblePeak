using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Damageables;
using Arawn.EnemyMasses.Runtime.Factions;
using Networking = Arawn.EnemyMasses.Runtime.Networking;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Handles skill execution, target finding, cooldown management, and effect application.
    /// This is the core runtime system that makes skills actually work.
    /// 
    /// Key responsibilities:
    /// - Evaluate which skill an agent should use based on AI conditions
    /// - Find valid targets for skills
    /// - Check and apply cooldowns (per-agent, per-enemycrowd, or per-faction)
    /// - Execute skill effects on targets
    /// - Spawn VFX/SFX with budgeting
    /// </summary>
    public static class SkillExecutor
    {
        private static int _nextSkillExecutionId = 1;

        /// <summary>
        /// Reusable buffer for <see cref="BroadcastCooldownStatePush"/> to avoid
        /// allocating a <see cref="Networking.CooldownEntry"/>[] on every skill execution.
        /// Grown on demand; never shrunk.
        /// </summary>
        private static Networking.CooldownEntry[] _cooldownBuffer = new Networking.CooldownEntry[8];

        private static int GenerateSkillExecutionId()
        {
            int id = _nextSkillExecutionId++;
            if (_nextSkillExecutionId <= 0) _nextSkillExecutionId = 1;
            return id;
        }

        #region ═══════════════════════════════════════════════════════════════
        // SKILL EVALUATION - Which skill should agent use?
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Evaluates all available skills for an agent and returns the best one to use.
        /// Returns null if no skill is available or valid.
        /// </summary>
        /// <param name="agent">The agent that wants to use a skill</param>
        /// <param name="config">The skill system configuration from EnemyCrowd</param>
        /// <param name="agentState">Per-agent cooldown state</param>
        /// <param name="factionCooldowns">Per-faction shared cooldowns (can be null)</param>
        /// <param name="potentialTargets">Nearby enemies/allies for targeting</param>
        /// <param name="factionControlMode">The faction-level control mode (optional, defaults to AIControlled).</param>
        /// <returns>The best skill slot to use, or null if none available</returns>
        public static AgentSkillSlot EvaluateBestSkill(
            CrowdAgent agent,
            SkillSystemConfig config,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            List<CrowdAgent> potentialTargets,
            SkillControlMode factionControlMode = SkillControlMode.AIControlled)
        {
            if (!config.enableSkillSystem || config.skillSlots == null || config.skillSlots.Count == 0)
                return null;
            
            AgentSkillSlot bestSlot = null;
            int highestPriority = int.MinValue;
            
            // Debug: Log when evaluating skills for an agent attacking a flying target
            bool debugAntiAir = agent.attackTargetAgentIndex >= 0 && !agent._skillEvalDebugLogged;
            CrowdAgent debugTarget = null;
            if (debugAntiAir && agent.attackTargetAgentIndex < EnemyMassesCrowdController.Instance?.GetAgentCount())
            {
                debugTarget = EnemyMassesCrowdController.Instance?.GetAgent(agent.attackTargetAgentIndex);
                debugAntiAir = debugTarget != null && debugTarget.isFlying;
            }
            if (debugAntiAir)
            {
                Debug.Log($"[SkillEvalDebug] Agent {agent.index} evaluating skills vs flying target {agent.attackTargetAgentIndex}. " +
                    $"skillSlots={config.skillSlots.Count} controlMode={factionControlMode} nearbyTargets={potentialTargets?.Count ?? 0}");
                agent._skillEvalDebugLogged = true;
            }

            foreach (var slot in config.skillSlots)
            {
                if (slot?.skill == null || !slot.enabled) continue;
                
                // Skip slots explicitly forced to manual (auto-cast disabled for this slot).
                if (slot.overrideControlMode && slot.controlModeOverride == SkillControlMode.ManualOnly)
                    continue;

                // When faction mode is ManualOnly, Hybrid (auto-cast) slots are handled
                // by the dedicated TryExecuteAutocastSlots path below.
                if (slot.overrideControlMode &&
                    slot.controlModeOverride == SkillControlMode.Hybrid &&
                    factionControlMode == SkillControlMode.ManualOnly)
                    continue;
                
                // Check if skill is on cooldown
                if (IsSkillOnCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, config.defaultCooldownMode))
                    continue;
                
                // Check resource cost (mana, energy, etc.)
                if (slot.skill.HasResourceCost && !SkillResourceManager.Instance.HasResource(agent, slot.skill))
                    continue;
                
                // Check if AI conditions are met
                if (!CheckAIConditions(slot.skill, agent, potentialTargets))
                    continue;
                
                // Check if we have valid targets for this skill
                if (!HasValidTarget(slot.skill, agent, potentialTargets))
                {
                    if (debugAntiAir)
                        Debug.Log($"[SkillEvalDebug]   Slot '{slot.skill.SkillName}' REJECTED: HasValidTarget=false (CanDamageFlying={slot.skill.CanDamageFlying} attackReach={slot.skill.AttackReach} targetingType={slot.skill.TargetingType})");
                    continue;
                }
                
                if (debugAntiAir)
                    Debug.Log($"[SkillEvalDebug]   Slot '{slot.skill.SkillName}' PASSED all checks. CanDamageFlying={slot.skill.CanDamageFlying} priority={slot.skill.Priority}");
                
                // Get effective priority (slot override or skill default)
                int priority = slot.overridePriority ? slot.priorityOverride : slot.skill.Priority;
                
                if (priority > highestPriority)
                {
                    highestPriority = priority;
                    bestSlot = slot;
                }
            }
            
            return bestSlot;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // COOLDOWN MANAGEMENT
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Checks if a skill is currently on cooldown.
        /// Respects per-agent, per-enemycrowd, and per-faction cooldown modes.
        /// </summary>
        public static bool IsSkillOnCooldown(
            AgentSkillSlot slot,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultMode)
        {
            if (slot?.skill == null) return true;
            
            // Determine which cooldown mode to use
            SkillCooldownMode mode = slot.overrideCooldownMode 
                ? slot.cooldownModeOverride 
                : (slot.skill.CooldownMode != SkillCooldownMode.PerAgent ? slot.skill.CooldownMode : defaultMode);
            
            string skillId = slot.skill.SkillId;
            
            if (mode == SkillCooldownMode.PerFaction && factionCooldowns != null)
            {
                // Check faction-wide cooldown
                return factionCooldowns.IsOnCooldown(skillId);
            }
            else if (mode == SkillCooldownMode.PerEnemyCrowd && enemyCrowdCooldowns != null)
            {
                return enemyCrowdCooldowns.IsOnCooldown(skillId);
            }
            else
            {
                // Check per-agent cooldown
                return agentState?.IsOnCooldown(skillId) ?? false;
            }
        }
        
        /// <summary>
        /// Gets the remaining cooldown time for a skill.
        /// </summary>
        public static float GetRemainingCooldown(
            AgentSkillSlot slot,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultMode)
        {
            if (slot?.skill == null) return 0f;
            
            SkillCooldownMode mode = slot.overrideCooldownMode 
                ? slot.cooldownModeOverride 
                : (slot.skill.CooldownMode != SkillCooldownMode.PerAgent ? slot.skill.CooldownMode : defaultMode);
            
            string skillId = slot.skill.SkillId;
            
            if (mode == SkillCooldownMode.PerFaction && factionCooldowns != null)
            {
                return factionCooldowns.GetRemainingCooldown(skillId);
            }
            else if (mode == SkillCooldownMode.PerEnemyCrowd && enemyCrowdCooldowns != null)
            {
                return enemyCrowdCooldowns.GetRemainingCooldown(skillId);
            }
            else
            {
                return agentState?.GetRemainingCooldown(skillId) ?? 0f;
            }
        }
        
        /// <summary>
        /// Starts the cooldown for a skill after it's been used.
        /// </summary>
        public static void StartCooldown(
            AgentSkillSlot slot,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultMode)
        {
            if (slot?.skill == null) return;
            
            // Apply cooldown multiplier from slot
            float cooldownDuration = slot.skill.Cooldown * slot.cooldownMultiplier;
            
            SkillCooldownMode mode = slot.overrideCooldownMode 
                ? slot.cooldownModeOverride 
                : (slot.skill.CooldownMode != SkillCooldownMode.PerAgent ? slot.skill.CooldownMode : defaultMode);
            
            string skillId = slot.skill.SkillId;
            
            if (mode == SkillCooldownMode.PerFaction && factionCooldowns != null)
            {
                factionCooldowns.StartCooldown(skillId, cooldownDuration);
            }
            else if (mode == SkillCooldownMode.PerEnemyCrowd && enemyCrowdCooldowns != null)
            {
                enemyCrowdCooldowns.StartCooldown(skillId, cooldownDuration);
            }
            else
            {
                agentState?.StartCooldown(skillId, cooldownDuration);
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // AI CONDITIONS - Should the agent use this skill now?
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Checks if AI conditions for using a skill are met.
        /// </summary>
        public static bool CheckAIConditions(AgentSkill skill, CrowdAgent agent, List<CrowdAgent> potentialTargets)
        {
            if (skill == null) return false;
            
            var conditions = skill.UseConditions;
            
            // Always Available - no conditions
            if (conditions == SkillUseCondition.Always)
                return true;
            
            // Get agent health percentage
            float healthPercent = GetAgentHealthPercent(agent);
            
            // Check health-based conditions
            if (conditions.HasFlag(SkillUseCondition.SelfHealthLow) && healthPercent >= skill.SelfHealthLowThreshold)
                return false;
            
            if (conditions.HasFlag(SkillUseCondition.SelfHealthHigh) && healthPercent <= skill.SelfHealthHighThreshold)
                return false;
            
            // Check target count conditions
            int enemyCount = CountEnemiesInRange(agent, potentialTargets, skill.MaxRange);
            int allyCount = CountAlliesInRange(agent, potentialTargets, skill.MaxRange);
            
            if (conditions.HasFlag(SkillUseCondition.MultipleEnemiesNearby) && enemyCount < skill.MultipleEnemiesThreshold)
                return false;
            
            // Check ally health condition (for heals)
            if (conditions.HasFlag(SkillUseCondition.AllyHealthLow))
            {
                bool hasLowHealthAlly = false;
                foreach (var target in potentialTargets)
                {
                    if (target == null || target == agent) continue;
                    if (IsAlly(agent.enemyCrowd?.factionName ?? "", target.enemyCrowd?.factionName ?? ""))
                    {
                        float allyHealth = GetAgentHealthPercent(target);
                        if (allyHealth < skill.AllyHealthLowThreshold)
                        {
                            hasLowHealthAlly = true;
                            break;
                        }
                    }
                }
                if (!hasLowHealthAlly) return false;
            }
            
            // Check distance conditions (requires target)
            if (conditions.HasFlag(SkillUseCondition.AtRange))
            {
                var nearestEnemy = GetNearestEnemy(agent, potentialTargets);
                if (nearestEnemy != null)
                {
                    float distance = Vector3.Distance(agent.WorldPosition, nearestEnemy.WorldPosition);
                    if (distance < skill.MinRange) return false;
                }
            }
            
            if (conditions.HasFlag(SkillUseCondition.InMeleeRange))
            {
                var nearestEnemy = GetNearestEnemy(agent, potentialTargets);
                if (nearestEnemy != null)
                {
                    float distance = Vector3.Distance(agent.WorldPosition, nearestEnemy.WorldPosition);
                    if (distance > skill.MaxRange * 0.3f) return false; // "Melee" = within 30% of max range
                }
            }
            
            // Check target health conditions
            if (conditions.HasFlag(SkillUseCondition.TargetHealthLow) || conditions.HasFlag(SkillUseCondition.TargetHealthHigh))
            {
                var nearestEnemy = GetNearestEnemy(agent, potentialTargets);
                if (nearestEnemy != null)
                {
                    float targetHealth = GetAgentHealthPercent(nearestEnemy);
                    
                    if (conditions.HasFlag(SkillUseCondition.TargetHealthLow) && targetHealth > skill.TargetHealthLowThreshold)
                        return false;
                    
                    if (conditions.HasFlag(SkillUseCondition.TargetHealthHigh) && targetHealth < skill.TargetHealthHighThreshold)
                        return false;
                }
            }
            
            // Random chance (for variety)
            if (skill.UseChance < 1f)
            {
                if (Random.value > skill.UseChance) return false;
            }
            
            return true;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // TARGET FINDING
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Checks if the skill has at least one valid target.
        /// </summary>
        public static bool HasValidTarget(AgentSkill skill, CrowdAgent caster, List<CrowdAgent> potentialTargets)
        {
            if (skill == null) return false;
            
            // When the skill cannot damage flying agents, exclude them from target checks
            bool skipFlying = !skill.CanDamageFlying;
            
            switch (skill.TargetingType)
            {
                case SkillTargetingType.Self:
                    return true; // Self-targeted always has a "target"
                
                case SkillTargetingType.GroundTargetedAOE:
                    // For AI/auto-cast: require at least one enemy in range to
                    // synthesize the ground target. Without this, GroundTargetedAOE
                    // fires endlessly at the caster's own position after all enemies die.
                    // Manual targeting bypasses this (player picks the ground point).
                    return HasEnemyInRange(caster, potentialTargets, skill.MinRange, skill.MaxRange, skipFlying);
                
                case SkillTargetingType.SingleEnemy:
                case SkillTargetingType.ClosestEnemy:
                case SkillTargetingType.AOEAroundEnemy:
                case SkillTargetingType.Cone:
                case SkillTargetingType.Line:
                case SkillTargetingType.Chain:
                case SkillTargetingType.AllEnemiesInRange:
                case SkillTargetingType.Hitscan:
                    return HasEnemyInRange(caster, potentialTargets, skill.MinRange, skill.MaxRange, skipFlying);
                
                case SkillTargetingType.SingleAlly:
                case SkillTargetingType.LowestHealthAlly:
                case SkillTargetingType.AOEAroundAlly:
                case SkillTargetingType.AllAlliesInRange:
                    return HasAllyInRange(caster, potentialTargets, skill.MinRange, skill.MaxRange);
                
                case SkillTargetingType.AllCrowdMembers:
                case SkillTargetingType.AllFactionMembers:
                    return true; // Always valid — targets all agents in same crowd/faction
                
                case SkillTargetingType.AOEAroundSelf:
                    // Check if there are any valid targets in the AOE radius
                    return HasAnyTargetInRange(caster, potentialTargets, 0f, skill.AOERadius);
                
                case SkillTargetingType.Projectile:
                    // Projectile targeting: need an enemy (or ally for support) in range
                    var projConfig = skill.ProjectileTargeting;
                    if (projConfig.canTargetEnemies)
                        return HasEnemyInRange(caster, potentialTargets, projConfig.minRange, projConfig.maxRange, skipFlying);
                    if (projConfig.canTargetAllies)
                        return HasAllyInRange(caster, potentialTargets, projConfig.minRange, projConfig.maxRange);
                    return true; // No target requirements - could be ground-targeted projectile
                
                default:
                    return true;
            }
        }
        
        /// <summary>
        /// Checks if a specific target is valid for a skill.
        /// Used for manual targeting validation.
        /// </summary>
        private static bool IsValidTarget(AgentSkill skill, CrowdAgent caster, CrowdAgent target)
        {
            if (skill == null || caster == null || target == null || target.isDead)
                return false;
            
            // Check distance
            float distance = Vector3.Distance(caster.WorldPosition, target.WorldPosition);
            if (distance < skill.MinRange || distance > skill.MaxRange)
                return false;
            
            // Check relationship based on skill type
            bool isEnemy = IsEnemy(caster, target);
            bool isAlly = IsAlly(caster, target);
            
            switch (skill.Type)
            {
                case SkillType.Attack:
                case SkillType.Debuff:
                    return isEnemy;
                    
                case SkillType.Buff:
                case SkillType.Heal:
                    return isAlly || target == caster;
                    
                case SkillType.Utility:
                case SkillType.Movement:
                case SkillType.Summon:
                    return true; // Can target anyone or self
                    
                default:
                    return true;
            }
        }
        
        /// <summary>
        /// Finds all valid targets for a skill execution.
        /// </summary>
        public static List<CrowdAgent> FindTargets(
            AgentSkill skill,
            CrowdAgent caster,
            List<CrowdAgent> potentialTargets,
            Vector3? groundTarget = null)
        {
            var targets = new List<CrowdAgent>();
            if (skill == null || caster == null) return targets;
            
            Vector3 casterPos = caster.WorldPosition;
            
            switch (skill.TargetingType)
            {
                case SkillTargetingType.Self:
                    targets.Add(caster);
                    break;
                
                case SkillTargetingType.SingleEnemy:
                case SkillTargetingType.ClosestEnemy:
                case SkillTargetingType.Hitscan:
                    var nearestEnemy = GetNearestEnemyInRange(
                        caster,
                        potentialTargets,
                        skill.MinRange,
                        skill.MaxRange,
                        skipFlying: !skill.CanDamageFlying,
                        allowFlyingHeightBypass: skill.CanDamageFlying);
                    if (nearestEnemy != null) targets.Add(nearestEnemy);
                    break;
                
                case SkillTargetingType.SingleAlly:
                    var nearestAlly = GetNearestAllyInRange(caster, potentialTargets, skill.MinRange, skill.MaxRange);
                    if (nearestAlly != null) targets.Add(nearestAlly);
                    break;
                    
                case SkillTargetingType.LowestHealthAlly:
                    var lowestHealthAlly = GetLowestHealthAllyInRange(caster, potentialTargets, skill.MinRange, skill.MaxRange);
                    if (lowestHealthAlly != null) targets.Add(lowestHealthAlly);
                    break;
                
                case SkillTargetingType.AOEAroundSelf:
                    targets.AddRange(GetAllInRadius(caster, potentialTargets, casterPos, skill.AOERadius, skill.MaxTargets, skill.Type));
                    break;
                
                case SkillTargetingType.AOEAroundEnemy:
                    var enemyCenter = GetNearestEnemyInRange(
                        caster,
                        potentialTargets,
                        skill.MinRange,
                        skill.MaxRange,
                        skipFlying: !skill.CanDamageFlying,
                        allowFlyingHeightBypass: skill.CanDamageFlying);
                    if (enemyCenter != null)
                    {
                        targets.AddRange(GetAllInRadius(caster, potentialTargets, enemyCenter.WorldPosition, skill.AOERadius, skill.MaxTargets, skill.Type));
                    }
                    break;
                
                case SkillTargetingType.AOEAroundAlly:
                    var allyCenter = GetNearestAllyInRange(caster, potentialTargets, skill.MinRange, skill.MaxRange);
                    if (allyCenter != null)
                    {
                        targets.AddRange(GetAllInRadius(caster, potentialTargets, allyCenter.WorldPosition, skill.AOERadius, skill.MaxTargets, skill.Type));
                    }
                    break;
                
                case SkillTargetingType.GroundTargetedAOE:
                    if (groundTarget.HasValue)
                    {
                        targets.AddRange(GetAllInRadius(caster, potentialTargets, groundTarget.Value, skill.AOERadius, skill.MaxTargets, skill.Type));
                    }
                    break;
                
                case SkillTargetingType.Cone:
                    targets.AddRange(GetAllInCone(caster, potentialTargets, skill.MaxRange, skill.ConeAngle, skill.MaxTargets, skill.Type));
                    break;
                
                case SkillTargetingType.Line:
                    targets.AddRange(GetAllInLine(caster, potentialTargets, skill.MaxRange, skill.LineWidth, skill.MaxTargets, skill.Type));
                    break;
                
                case SkillTargetingType.Chain:
                    targets.AddRange(GetChainTargets(caster, potentialTargets, skill.MaxRange, skill.MaxTargets, skill.Type));
                    break;
                    
                case SkillTargetingType.AllEnemiesInRange:
                    targets.AddRange(GetAllEnemiesInRange(caster, potentialTargets, skill.MaxRange, skill.MaxTargets));
                    break;
                    
                case SkillTargetingType.AllAlliesInRange:
                    targets.AddRange(GetAllAlliesInRange(caster, potentialTargets, skill.MaxRange, skill.MaxTargets));
                    break;
                
                case SkillTargetingType.AllCrowdMembers:
                    targets.AddRange(GetAllCrowdMembers(caster, potentialTargets));
                    break;
                
                case SkillTargetingType.AllFactionMembers:
                    targets.AddRange(GetAllFactionMembers(caster, potentialTargets));
                    break;
                    
                case SkillTargetingType.Projectile:
                    // For Projectile targeting, find the initial target(s).
                    // OnHit actions are applied on projectile impact, not immediately.
                    // Trajectory pass-through actions are handled in projectile simulation.
                    var projCfg = skill.ProjectileTargeting;
                    if (projCfg.canTargetEnemies)
                    {
                        var enemy = GetNearestEnemyInRange(
                            caster,
                            potentialTargets,
                            projCfg.minRange,
                            projCfg.maxRange,
                            skipFlying: !skill.CanDamageFlying,
                            allowFlyingHeightBypass: skill.CanDamageFlying);
                        if (enemy != null) targets.Add(enemy);
                    }
                    else if (projCfg.canTargetAllies)
                    {
                        var ally = GetNearestAllyInRange(caster, potentialTargets, projCfg.minRange, projCfg.maxRange);
                        if (ally != null) targets.Add(ally);
                    }
                    break;
            }
            
            // Remove flying targets that the skill cannot reach
            FilterUnreachableFlyingTargets(skill, targets);
            
            return targets;
        }
        
        /// <summary>
        /// Removes targets that are flying when the skill cannot damage them.
        /// Called after target resolution so every targeting path is covered.
        /// </summary>
        private static void FilterUnreachableFlyingTargets(AgentSkill skill, List<CrowdAgent> targets)
        {
            if (skill == null || skill.CanDamageFlying) return;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (targets[i] != null && targets[i].isFlying)
                    targets.RemoveAt(i);
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // SKILL EXECUTION
        #endregion ════════════════════════════════════════════════════════════
        
        // ─── Sequence counter for skill execution requests ──────────────
        private static int _nextSkillRequestSequence = 1;
        
        /// <summary>
        /// Routes a skill execution through the network skill authority when in multiplayer.
        /// 
        /// In single-player or on the server: delegates directly to <see cref="ExecuteSkill"/>.
        /// On a client with <see cref="Networking.INetworkSkillAuthority"/>: creates a
        /// <see cref="Networking.SkillExecutionRequestData"/> and sends it to the server
        /// for validation. If skill prediction is enabled, the skill is also executed locally.
        /// 
        /// Use this instead of <see cref="ExecuteSkill"/> for all player-initiated (manual/UI)
        /// skill requests. AI-driven skill execution on the server should continue to call
        /// <see cref="ExecuteSkill"/> directly.
        /// </summary>
        /// <param name="slot">The skill slot being used</param>
        /// <param name="slotIndex">Index of the slot in the skill loadout</param>
        /// <param name="caster">The agent using the skill</param>
        /// <param name="targets">Pre-found targets for the skill</param>
        /// <param name="controller">The EnemyMassesCrowdController managing this agent</param>
        /// <param name="agentState">Per-agent runtime state</param>
        /// <param name="factionCooldowns">Per-faction cooldowns (can be null)</param>
        /// <param name="enemyCrowdCooldowns">Per-EnemyCrowd cooldowns (can be null)</param>
        /// <param name="defaultCooldownMode">Default cooldown mode from config</param>
        /// <param name="groundTargetPosition">Optional ground target for GroundTargetedAOE</param>
        /// <param name="playerId">Player ID for ownership validation (-1 for AI)</param>
        /// <returns>True if the skill was executed (server) or the request was sent (client)</returns>
        public static bool RequestSkillExecution(
            AgentSkillSlot slot,
            int slotIndex,
            CrowdAgent caster,
            List<CrowdAgent> targets,
            EnemyMassesCrowdController controller,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultCooldownMode,
            Vector3? groundTargetPosition = null,
            int playerId = -1,
            IEnemyMassesDamageable damageableTarget = null)
        {
            if (slot?.skill == null || caster == null)
                return false;
            
            var skillAuthority = Networking.NetworkAuthorityProvider.SkillAuthority;
            
            // If no skill authority (single-player) or we are the server → execute directly
            if (skillAuthority == null || Networking.NetworkAuthorityProvider.IsServerAuthority)
            {
                return ExecuteSkill(slot, caster, targets, controller, agentState,
                    factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode,
                    groundTargetPosition, damageableTarget);
            }
            
            // CLIENT PATH: build a request and send through the skill authority
            var request = new Networking.SkillExecutionRequestData
            {
                casterNetworkId = caster.networkId,
                skillId = slot.skill.SkillId,
                slotIndex = slotIndex,
                targetNetworkId = (targets != null && targets.Count > 0) ? targets[0].networkId : 0u,
                groundTargetPosition = groundTargetPosition ?? Vector3.zero,
                hasGroundTarget = groundTargetPosition.HasValue,
                playerId = playerId >= 0 ? playerId : (Networking.NetworkAuthorityProvider.CommandAuthority?.LocalPlayerId ?? 0),
                sequence = _nextSkillRequestSequence++,
                clientTime = Time.time
            };
            
            bool predicted = skillAuthority.OnSkillExecutionRequest(request);
            
            // If skill prediction is enabled and the authority allowed it, execute locally
            if (predicted && skillAuthority.EnableSkillPrediction)
            {
                ExecuteSkill(slot, caster, targets, controller, agentState,
                    factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode,
                    groundTargetPosition, damageableTarget);
            }
            
            return true; // Request sent (or predicted)
        }
        
        /// <summary>
        /// Executes a skill, applying all effects to targets.
        /// This is the main entry point for skill usage.
        /// </summary>
        /// <param name="slot">The skill slot being used</param>
        /// <param name="caster">The agent using the skill</param>
        /// <param name="targets">Pre-found targets for the skill</param>
        /// <param name="controller">The EnemyMassesCrowdController managing this agent</param>
        /// <param name="agentState">Per-agent runtime state</param>
        /// <param name="factionCooldowns">Per-faction cooldowns (can be null)</param>
        /// <param name="defaultCooldownMode">Default cooldown mode from config</param>
        /// <returns>True if skill was executed successfully</returns>
        public static bool ExecuteSkill(
            AgentSkillSlot slot,
            CrowdAgent caster,
            List<CrowdAgent> targets,
            EnemyMassesCrowdController controller,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultCooldownMode,
            Vector3? groundTargetPosition = null,
            IEnemyMassesDamageable damageableTarget = null)
        {
            if (slot?.skill == null || caster == null || targets == null)
                return false;

            // GroundTargetedAOE can fire on empty ground (no enemies in radius).
            // A damageable target also counts as a valid target when no agents found.
            // All other targeting types require at least one target.
            if (targets.Count == 0 && damageableTarget == null
                && slot.skill.TargetingType != SkillTargetingType.GroundTargetedAOE)
                return false;

            if (!Networking.NetworkAuthorityProvider.IsServerAuthority)
                return false;
            
            var skill = slot.skill;
            
            // Double-check cooldown (in case of race conditions)
            if (IsSkillOnCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode))
                return false;
            
            // Check resource cost (mana, energy, etc.)
            if (skill.HasResourceCost && !SkillResourceManager.Instance.HasResource(caster, skill))
                return false;
            
            // Calculate if this is a critical hit
            bool isCritical = Random.value < skill.CriticalChance;
            float effectMultiplier = slot.damageMultiplier * (isCritical ? skill.CriticalMultiplier : 1f);
            
            // Resolve the target position:
            // 1. If there are agent targets, use the primary target's position.
            // 2. If a ground target was provided (GroundTargetedAOE), use that.
            // 3. Fallback to the caster's position.
            Vector3 resolvedTargetPosition;
            if (targets.Count > 0)
                resolvedTargetPosition = targets[0].WorldPosition;
            else if (damageableTarget != null && damageableTarget.DamageTransform != null)
                resolvedTargetPosition = damageableTarget.DamageTransform.position;
            else if (groundTargetPosition.HasValue)
                resolvedTargetPosition = groundTargetPosition.Value;
            else
                resolvedTargetPosition = caster.WorldPosition;
            
            // Create execution context
            var context = new SkillExecutionContext
            {
                Caster = caster,
                PrimaryTarget = targets.Count > 0 ? targets[0] : null,
                AllTargets = targets.ToArray(),
                TargetPosition = resolvedTargetPosition,
                Skill = skill,
                Controller = controller,
                IsCritical = isCritical,
                EffectMultiplier = effectMultiplier,
                ExecutionId = GenerateSkillExecutionId(),
                NetworkProjectileId = 0,
                DamageableTarget = damageableTarget
            };

            // Deduct resource cost (mana, energy, etc.) before executing
            if (skill.HasResourceCost)
            {
                SkillResourceManager.Instance.ConsumeResource(caster, skill);
            }
            
            ApplyCastMovementLockIfNeeded(skill, caster);
            
            // For Projectile targeting type, OnHit/OnFinish actions are applied on projectile impact, not now
            bool isProjectileTargeting = skill.TargetingType == SkillTargetingType.Projectile;
            
            // On Cast actions (always fire immediately)
            ExecuteActions(skill.OnCastActions, context);
            
            // ═══════════════════════════════════════════════════════════════
            // CAST TIME DEFERRAL — if skill has a cast time > 0, store
            // context on the agent and defer OnHit / OnFinish / projectile /
            // animation / impact events until the cast completes.
            // ═══════════════════════════════════════════════════════════════
            float castDuration = Mathf.Max(0f, skill.CastTime);
            if (castDuration > 0f)
            {
                caster.isCasting = true;
                caster.castEndTime = Time.time + castDuration;
                caster.castingSlot = slot;
                caster.castingSkill = skill;
                caster.pendingCastContext = context;
                
                // Start cooldown immediately (can't re-queue while casting anyway)
                StartCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode);
                
                // Raise cast event now so listeners know casting started
                RaiseSkillCastEvent(slot, caster, skill, targets, context);
                
                // Network: broadcast cast start + cooldown sync
                BroadcastNetworkSkillCast(slot, caster, skill, context);
                BroadcastCooldownStatePush(slot, caster, agentState, factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode);
                
                return true; // OnHit, OnFinish, animation, etc. will fire via CompleteCast()
            }
            
            // ═══════════════════════════════════════════════════════════════
            // INSTANT EXECUTION — CastTime == 0 (original behaviour)
            // ═══════════════════════════════════════════════════════════════
            
            // On Hit actions (unless Projectile targeting - deferred to impact)
            if (!isProjectileTargeting)
            {
                SkillVFXManager.StopLoopingSFXForContext(context, SkillSFXLoopStopPolicy.OnHit);
                ExecuteActions(skill.OnHitActions, context);
            }
            
            // Fire projectile: either via Projectile targeting or legacy ProjectileArc
            if (isProjectileTargeting)
            {
                // New Projectile targeting system - effects applied on impact
                FireProjectileWithEffects(skill, context);
            }
            else if (skill.HasProjectileArc)
            {
                // Legacy ProjectileArc system (for non-Projectile targeting types)
                FireProjectileArc(skill.ProjectileArc, context);
            }
            
            // Trigger animation
            TriggerSkillAnimation(skill, caster);
            
            // Start cooldown
            StartCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode);
            
            // Raise OnSkillCast event (local C# event)
            RaiseSkillCastEvent(slot, caster, skill, targets, context);
            
            // Broadcast skill cast + cooldown state to remote clients (VFX/animation replication + cooldown sync)
            BroadcastNetworkSkillCast(slot, caster, skill, context);
            BroadcastCooldownStatePush(slot, caster, agentState, factionCooldowns, enemyCrowdCooldowns, defaultCooldownMode);
            
            // For non-projectile skills, raise impact events immediately
            if (!isProjectileTargeting)
            {
                RaiseSkillImpactEvents(caster, skill, targets, context);
            }
            
            // On Finish actions for non-projectile skills
            if (!isProjectileTargeting)
            {
                ExecuteFinishActions(skill, context, slot);
            }
            
            return true;
        }

        private static void ApplyCastMovementLockIfNeeded(AgentSkill skill, CrowdAgent caster)
        {
            if (skill == null || caster == null || !skill.LockMovementDuringCast)
                return;

            float castTime = Mathf.Max(0f, skill.CastTime);
            if (castTime <= 0f)
                return;

            float lockEndTime = Time.time + castTime;
            if (lockEndTime > caster.skillMovementLockEndTime)
            {
                caster.skillMovementLockEndTime = lockEndTime;
            }

            // Apply the lock immediately; combat update keeps it enforced until expiry.
            var navAgent = caster.navigationAgent;
            if (navAgent != null && navAgent.IsOnNavMesh)
            {
                navAgent.IsStopped = true;
                navAgent.Velocity = Vector3.zero;
            }
        }
        
        // ═══════════════════════════════════════════════════════════════
        // CAST TIME — Completion & Interruption
        // ═══════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Completes a pending cast: fires the deferred OnHit, projectile, animation,
        /// impact events, and OnFinish that were held back during the cast window.
        /// Called from the per-frame cast processing loop when castEndTime is reached.
        /// </summary>
        public static void CompleteCast(CrowdAgent agent)
        {
            if (agent == null || !agent.isCasting) return;
            
            var ctx = agent.pendingCastContext;
            var skill = agent.castingSkill;
            var slot = agent.castingSlot;
            
            // Clear cast state first (so callbacks don't see the agent as still casting)
            ClearCastState(agent);
            
            if (ctx == null || skill == null) return;
            
            bool isProjectileTargeting = skill.TargetingType == SkillTargetingType.Projectile;
            
            // OnHit actions
            if (!isProjectileTargeting)
            {
                SkillVFXManager.StopLoopingSFXForContext(ctx, SkillSFXLoopStopPolicy.OnHit);
                ExecuteActions(skill.OnHitActions, ctx);
            }
            
            // Fire projectile
            if (isProjectileTargeting)
            {
                FireProjectileWithEffects(skill, ctx);
            }
            else if (skill.HasProjectileArc)
            {
                FireProjectileArc(skill.ProjectileArc, ctx);
            }
            
            // Animation
            TriggerSkillAnimation(skill, agent);
            
            // Impact events (non-projectile)
            if (!isProjectileTargeting && ctx.AllTargets != null)
            {
                var targetsList = new List<CrowdAgent>(ctx.AllTargets);
                RaiseSkillImpactEvents(agent, skill, targetsList, ctx);
            }
            
            // OnFinish actions (non-projectile)
            if (!isProjectileTargeting)
            {
                ExecuteFinishActions(skill, ctx, slot);
            }
        }
        
        /// <summary>
        /// Interrupts a pending cast: clears all cast state without executing
        /// the deferred OnHit / OnFinish / projectile / animation.
        /// Movement lock is also cleared immediately.
        /// </summary>
        public static void InterruptCast(CrowdAgent agent)
        {
            if (agent == null || !agent.isCasting) return;
            
            ClearCastState(agent);
            
            // Clear movement lock so the agent can move again
            agent.skillMovementLockEndTime = 0f;
            var navAgent = agent.navigationAgent;
            if (navAgent != null && navAgent.IsOnNavMesh)
            {
                navAgent.IsStopped = false;
            }
        }
        
        /// <summary>
        /// Resets all per-agent cast-state fields to their defaults.
        /// </summary>
        private static void ClearCastState(CrowdAgent agent)
        {
            agent.isCasting = false;
            agent.castEndTime = 0f;
            agent.castingSlot = null;
            agent.castingSkill = null;
            agent.pendingCastContext = null;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // SKILL EVENTS
        #endregion ════════════════════════════════════════════════════════════

        public static void ExecuteActions(IReadOnlyList<SkillAction> actions, SkillExecutionContext context)
        {
            if (actions == null || context == null)
                return;

            float sequenceOffset = 0f;
            float latestScheduledTime = 0f;
            
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action == null)
                    continue;

                if (action is WaitAction waitAction)
                {
                    if (context.Controller == null)
                        continue;

                    float waitDuration = Mathf.Max(0f, waitAction.Duration);
                    float waitDelay = Mathf.Max(0f, waitAction.GetTotalDelay());
                    float totalWait = waitDuration + waitDelay;
                    if (totalWait <= 0f)
                        continue;

                    sequenceOffset = Mathf.Max(sequenceOffset, latestScheduledTime) + totalWait;
                    latestScheduledTime = Mathf.Max(latestScheduledTime, sequenceOffset);
                    continue;
                }

                float delay = sequenceOffset + Mathf.Max(0f, action.GetTotalDelay());
                latestScheduledTime = Mathf.Max(latestScheduledTime, delay);

                if (delay > 0f && context.Controller != null)
                {
                    context.Controller.StartCoroutine(DelayedActionExecute(delay, action, context));
                }
                else
                {
                    ExecuteActionImmediate(action, context);
                }

                if (action.WaitForCompletionBeforeNextAction)
                {
                    sequenceOffset = Mathf.Max(sequenceOffset, delay);
                }
            }
        }
        
        private static void ExecuteActionImmediate(SkillAction action, SkillExecutionContext context)
        {
            if (action == null)
                return;

            if (action.RequiresServerAuthority && !Networking.NetworkAuthorityProvider.IsServerAuthority)
                return;
            
            if (!action.CheckConditions(context))
            {
                return;
            }
            
            action.Execute(context);
        }
        
        private static System.Collections.IEnumerator DelayedActionExecute(float delay, SkillAction action, SkillExecutionContext context)
        {
            yield return new WaitForSeconds(delay);
            ExecuteActionImmediate(action, context);
        }

        /// <summary>
        /// Executes On Finish actions, optionally delayed by skill duration.
        /// Also raises the OnSkillFinish event after actions execute.
        /// </summary>
        public static void ExecuteFinishActions(AgentSkill skill, SkillExecutionContext context, AgentSkillSlot slot = null)
        {
            if (skill == null)
                return;
            
            float duration = skill.Duration;
            if (duration > 0f && context?.Controller != null)
            {
                context.Controller.StartCoroutine(DelayedFinishExecute(duration, skill, context, slot));
            }
            else
            {
                SkillVFXManager.StopLoopingSFXForContext(context, SkillSFXLoopStopPolicy.OnFinish);
                ExecuteActions(skill.OnFinishActions, context);
                RaiseSkillFinishEvent(skill, context, slot, wasCancelled: false);
            }
        }

        private static System.Collections.IEnumerator DelayedFinishExecute(float delay, AgentSkill skill, SkillExecutionContext context, AgentSkillSlot slot = null)
        {
            yield return new WaitForSeconds(delay);
            SkillVFXManager.StopLoopingSFXForContext(context, SkillSFXLoopStopPolicy.OnFinish);
            ExecuteActions(skill.OnFinishActions, context);
            RaiseSkillFinishEvent(skill, context, slot, wasCancelled: false);
        }
        
        /// <summary>
        /// Raises the OnSkillCast event through FactionSkillManager.
        /// </summary>
        private static void RaiseSkillCastEvent(
            AgentSkillSlot slot,
            CrowdAgent caster,
            AgentSkill skill,
            List<CrowdAgent> targets,
            SkillExecutionContext context)
        {
            var castData = new SkillCastEventData
            {
                Caster = caster,
                Skill = skill,
                Slot = slot,
                PrimaryTarget = context.PrimaryTarget,
                AllTargets = context.AllTargets,
                TargetPosition = context.TargetPosition,
                IsCritical = context.IsCritical,
                CastTime = Time.time
            };
            
            FactionSkillManager.RaiseSkillCastStatic(castData);
        }
        
        /// <summary>
        /// Raises OnSkillImpact events for all targets hit by a skill.
        /// Called immediately for non-projectile skills, or on projectile hit for projectile skills.
        /// </summary>
        internal static void RaiseSkillImpactEvents(
            CrowdAgent caster,
            AgentSkill skill,
            List<CrowdAgent> targets,
            SkillExecutionContext context)
        {
            if (targets == null || targets.Count == 0) return;
            
            // Calculate damage/healing from effects for event data
            float totalDamage = 0f;
            float totalHealing = 0f;
            
            if (skill.OnHitActions != null)
            {
                foreach (var action in skill.OnHitActions)
                {
                    if (action is DamageEffect damageEffect)
                    {
                        totalDamage += damageEffect.BaseDamage * context.EffectMultiplier;
                    }
                    else if (action is HealEffect healEffect)
                    {
                        totalHealing += healEffect.BaseHeal * context.EffectMultiplier;
                    }
                }
            }
            
            // Raise impact event for each target
            foreach (var target in targets)
            {
                if (target == null) continue;
                
                var impactData = new SkillImpactEventData
                {
                    Caster = caster,
                    Skill = skill,
                    Target = target,
                    ImpactPosition = target.WorldPosition,
                    DamageDealt = totalDamage,
                    HealingApplied = totalHealing,
                    IsCritical = context.IsCritical,
                    IsProjectileHit = false,
                    ImpactTime = Time.time
                };
                
                FactionSkillManager.RaiseSkillImpactStatic(impactData);
            }
        }
        
        /// <summary>
        /// Raises a single OnSkillImpact event for a projectile hit.
        /// Called by the projectile system when a skill projectile hits a target.
        /// </summary>
        public static void RaiseProjectileImpactEvent(
            CrowdAgent caster,
            AgentSkill skill,
            CrowdAgent target,
            Vector3 impactPosition,
            float damage,
            bool isCritical)
        {
            var impactData = new SkillImpactEventData
            {
                Caster = caster,
                Skill = skill,
                Target = target,
                ImpactPosition = impactPosition,
                DamageDealt = damage,
                HealingApplied = 0f,
                IsCritical = isCritical,
                IsProjectileHit = true,
                ImpactTime = Time.time
            };
            
            FactionSkillManager.RaiseSkillImpactStatic(impactData);
        }
        
        /// <summary>
        /// Raises the OnSkillFinish event through FactionSkillManager.
        /// Called after OnFinishActions have executed.
        /// </summary>
        private static void RaiseSkillFinishEvent(AgentSkill skill, SkillExecutionContext context, AgentSkillSlot slot, bool wasCancelled)
        {
            if (context == null) return;
            
            var finishData = new SkillFinishEventData
            {
                Caster = context.Caster,
                Skill = skill,
                Slot = slot,
                TargetPosition = context.TargetPosition,
                AllTargets = context.AllTargets,
                WasCancelled = wasCancelled,
                FinishTime = Time.time
            };
            
            FactionSkillManager.RaiseSkillFinishStatic(finishData);
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK BROADCASTS — Cooldown sync & VFX replication
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Broadcasts a skill cast event to remote clients for VFX/animation replication.
        /// Only runs on the server when a skill authority is available.
        /// Remote clients receive this via <see cref="Networking.INetworkSkillAuthority.OnRemoteSkillCastReceived"/>.
        /// </summary>
        private static void BroadcastNetworkSkillCast(
            AgentSkillSlot slot,
            CrowdAgent caster,
            AgentSkill skill,
            SkillExecutionContext context)
        {
            var skillAuthority = Networking.NetworkAuthorityProvider.SkillAuthority;
            if (skillAuthority == null || !skillAuthority.IsServer) return;
            
            var castData = new Networking.NetworkSkillCastEventData
            {
                casterNetworkId = caster.networkId,
                skillId = skill.SkillId,
                skillKey = Networking.NetworkStringRegistry.GetOrRegister(skill.SkillId),
                slotIndex = ResolveSlotIndex(slot, caster),
                targetPosition = context.TargetPosition,
                targetNetworkId = context.PrimaryTarget != null ? context.PrimaryTarget.networkId : 0u,
                isCritical = context.IsCritical,
                serverTime = Time.time,
                casterPlayerId = caster.ownerPlayerId
            };
            
            skillAuthority.BroadcastSkillCast(castData);
        }
        
        /// <summary>
        /// Pushes the authoritative cooldown state for a caster agent after skill execution.
        /// This provides immediate cooldown sync rather than waiting for periodic tick-based sync.
        /// Only runs on the server when a skill authority is available.
        /// Remote clients receive this via <see cref="Networking.INetworkSkillAuthority.OnCooldownStatePush"/>.
        /// </summary>
        private static void BroadcastCooldownStatePush(
            AgentSkillSlot executedSlot,
            CrowdAgent caster,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultMode)
        {
            var skillAuthority = Networking.NetworkAuthorityProvider.SkillAuthority;
            if (skillAuthority == null || !skillAuthority.IsServer) return;
            
            // Build cooldown entries for all of the caster's skill slots
            var skillConfig = caster.enemyCrowd?.skillSystemConfig;
            var slots = skillConfig?.skillSlots;
            if (slots == null || slots.Count == 0) return;
            
            // Reuse a static buffer; only allocate when slot count changes.
            // The buffer is handed directly to the snapshot — callers of
            // PushCooldownState must serialize it synchronously before the next
            // skill execution overwrites the buffer contents.
            int slotCount = slots.Count;
            if (_cooldownBuffer == null || _cooldownBuffer.Length != slotCount)
                _cooldownBuffer = new Networking.CooldownEntry[slotCount];
            
            for (int i = 0; i < slotCount; i++)
            {
                var slot = slots[i];
                if (slot?.skill == null)
                {
                    _cooldownBuffer[i] = new Networking.CooldownEntry
                    {
                        slotIndex = i,
                        skillId = string.Empty,
                        skillKey = Networking.NetworkStringRegistry.UNREGISTERED,
                        cooldownEndTime = 0f,
                        cooldownDuration = 0f
                    };
                    continue;
                }
                
                string skillId = slot.skill.SkillId;
                float remaining = GetCooldownRemaining(slot, agentState, factionCooldowns, enemyCrowdCooldowns, defaultMode);
                float duration = slot.skill.Cooldown * slot.cooldownMultiplier;
                
                _cooldownBuffer[i] = new Networking.CooldownEntry
                {
                    slotIndex = i,
                    skillId = skillId,
                    skillKey = Networking.NetworkStringRegistry.GetOrRegister(skillId),
                    cooldownEndTime = remaining > 0f ? Time.time + remaining : 0f,
                    cooldownDuration = duration
                };
            }
            
            var snapshot = new Networking.CooldownStateSnapshot
            {
                networkId = caster.networkId,
                cooldowns = _cooldownBuffer,
                globalCooldownEndTime = 0f, // Reserved for future global cooldown
                serverTime = Time.time
            };
            
            skillAuthority.PushCooldownState(snapshot);
        }
        
        /// <summary>
        /// Gets the remaining cooldown time for a skill slot, checking the appropriate tracker.
        /// </summary>
        private static float GetCooldownRemaining(
            AgentSkillSlot slot,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultMode)
        {
            if (slot?.skill == null) return 0f;
            
            string skillId = slot.skill.SkillId;
            SkillCooldownMode mode = slot.overrideCooldownMode 
                ? slot.cooldownModeOverride 
                : (slot.skill.CooldownMode != SkillCooldownMode.PerAgent ? slot.skill.CooldownMode : defaultMode);
            
            if (mode == SkillCooldownMode.PerFaction && factionCooldowns != null)
                return factionCooldowns.GetRemainingCooldown(skillId);
            else if (mode == SkillCooldownMode.PerEnemyCrowd && enemyCrowdCooldowns != null)
                return enemyCrowdCooldowns.GetRemainingCooldown(skillId);
            else
                return agentState?.GetRemainingCooldown(skillId) ?? 0f;
        }
        
        /// <summary>
        /// Resolves the slot index for an <see cref="AgentSkillSlot"/> within its caster's loadout.
        /// Returns -1 if the slot or config cannot be resolved.
        /// </summary>
        private static int ResolveSlotIndex(AgentSkillSlot slot, CrowdAgent caster)
        {
            if (slot == null || caster?.enemyCrowd?.skillSystemConfig?.skillSlots == null)
                return -1;
            return caster.enemyCrowd.skillSystemConfig.skillSlots.IndexOf(slot);
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // IMPACT PHYSICS (FAKE PHYSICS)
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Applies impact physics to targets using the Fake Physics system (NavMesh Agent Offset).
        /// This is the same system used by EnemyCrowd Charge Attack knockback.
        /// </summary>
        public static void ApplyImpactPhysics(ImpactPhysicsConfig config, SkillExecutionContext context)
        {
            if (config == null || !config.enabled || context.AllTargets == null || context.AllTargets.Length == 0)
                return;

            if (!Networking.NetworkAuthorityProvider.IsServerAuthority)
                return;
            
            if (config.physicsType == ImpactPhysicsType.None)
                return;
            
            Vector3 casterPos = context.Caster?.WorldPosition ?? Vector3.zero;
            Vector3 casterForward = context.Caster?.transform?.forward ?? Vector3.forward;
            
            float now = Time.time;
            float duration = config.GetDuration();
            float gravity = config.GetGravity();
            float drag = config.GetDrag();
            float hForce = config.GetHorizontalForce();
            float vForce = config.GetLiftForce();
            
            foreach (var target in context.AllTargets)
            {
                if (target == null || target.isDead) continue;
                if (target.isDisplacementImmune) continue;
                
                Vector3 targetPos = target.WorldPosition;
                
                // Calculate velocity based on physics type
                Vector3 velocity = config.CalculateVelocity(casterPos, targetPos, casterForward);

                if (EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                        target,
                        velocity,
                        duration,
                        drag,
                        gravity,
                        Mathf.Abs(hForce),
                        vForce,
                        context.Controller))
                {
                    // Lock attacks during impulse
                    if (config.lockAttacksDuring)
                    {
                        float lockEnd = now + duration + config.attackLockRecovery;
                        target.impulseAttackLockEndTime = Mathf.Max(target.impulseAttackLockEndTime, lockEnd);
                    }

                    // Apply stun if configured
                    if (config.stunDuring && context.Controller != null)
                    {
                        context.Controller.ApplyStatusEffect(
                            target.index,
                            StatusEffectType.Stun,
                            duration,
                            0f,
                            tickInterval: 1f,
                            sourceAgentIndex: context.Caster?.index ?? -1,
                            maxStacks: 1
                        );
                    }
                    continue;
                }
                
                // Apply the impulse using the Fake Physics system
                target.hasNavmeshOffsetImpulse = true;
                target.navmeshOffsetVelocity = velocity;
                target.navmeshOffsetGroundPosition = target.WorldPosition;
                target.navmeshOffset = Vector3.zero;
                target.navmeshOffsetDuration = duration;
                target.navmeshOffsetEndTime = now + duration;
                target.navmeshOffsetGravity = gravity;
                target.navmeshOffsetDrag = drag;
                
                // Store impulse values for animation system (compatible with PlayerSkillManager)
                target.lastNavmeshImpulsePush = Mathf.Abs(hForce);
                target.lastNavmeshImpulseLift = vForce;
                target.navmeshImpulseAnimationPlayed = false;
                
                // Trigger get-up animation flow for significant lifts
                const float LiftGetUpThreshold = 1.5f;
                target.liftImpulseActive = vForce >= LiftGetUpThreshold;
                
                // Lock attacks during impulse
                if (config.lockAttacksDuring)
                {
                    float lockEnd = now + duration + config.attackLockRecovery;
                    target.impulseAttackLockEndTime = Mathf.Max(target.impulseAttackLockEndTime, lockEnd);
                }
                
                // Apply stun if configured
                if (config.stunDuring && context.Controller != null)
                {
                    context.Controller.ApplyStatusEffect(
                        target.index,
                        StatusEffectType.Stun,
                        duration,
                        0f,
                        tickInterval: 1f,
                        sourceAgentIndex: context.Caster?.index ?? -1,
                        maxStacks: 1
                    );
                }
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // PROJECTILE TARGETING (NEW SYSTEM - EFFECTS ON IMPACT)
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Fires a projectile for Projectile targeting type skills.
        /// OnHit actions are applied when the projectile hits, not immediately.
        /// </summary>
        public static void FireProjectileWithEffects(AgentSkill skill, SkillExecutionContext context)
        {
            if (skill == null || !skill.IsProjectileSkill)
            {
                return;
            }

            if (!Networking.NetworkAuthorityProvider.IsServerAuthority)
                return;
            
            var targeting = skill.ProjectileTargeting;
            var visuals = skill.ProjectileArc; // Visual config from VFX/SFX tab
            
            if (context.Controller == null)
            {
                return;
            }

            // Standalone casters (e.g. StandaloneSkillCaster) have Caster == null but
            // provide CasterTransform so projectile spawn position can still be resolved.
            if (context.Caster == null && context.CasterTransform == null)
            {
                return;
            }
            
            // Get projectile system singleton
            var projectileSystem = EnemyMassesProjectileSystem.Instance;
            if (projectileSystem == null)
            {
                Debug.LogWarning("[SkillExecutor] No ProjectileSystem available for projectile skill.");
                return;
            }
            
            // Get spawn position from caster (or fallback transform)
            Vector3 casterPos = GetCasterPosition(context.Caster, context.CasterTransform);
            Vector3 spawnPos = visuals != null && visuals.enabled 
                ? GetProjectileSpawnPosition(visuals, context.Caster, casterPos, context.CasterTransform)
                : casterPos + new Vector3(0f, 1.5f, 0.5f);
            
            // Get target position - aim at center mass (chest height), not feet
            // transform.position is at feet level; add offset to target torso/chest area
            const float TargetHeightOffset = 1.2f; // Approximately chest height for humanoid agents
            Vector3 targetPos = context.PrimaryTarget != null
                ? context.PrimaryTarget.WorldPosition + Vector3.up * TargetHeightOffset
                : context.TargetPosition;
            
            int sourceAgentIndex = context.Caster?.index ?? -1;
            int targetAgentIndex = context.PrimaryTarget?.index ?? -1;
            int sourceFactionIndex = context.Caster?.factionIndex ?? -1;
            
            // Impact actions are processed on hit callback.
            // Trajectory pass-through actions are processed during projectile flight when enabled.
            float aoeRadius = 0f;
            
            // For Projectile targeting, we don't pass damage directly - we pass the skill actions
            // The projectile system will callback to apply OnHit actions on impact
            // For now, we'll extract base damage from DamageEffect if present
            float damage = 0f;
            foreach (var action in skill.OnHitActions)
            {
                if (action is DamageEffect damageEffect)
                {
                    damage = damageEffect.BaseDamage * context.EffectMultiplier;
                    break;
                }
            }
            
            int projectileCount = visuals?.projectileCount ?? 1;
            float spreadAngle = visuals?.spreadAngle ?? 15f;
            float volleyDelay = visuals?.volleyDelay ?? 0f;
            float spawnDelay = visuals?.spawnDelay ?? 0f;
            
            // Fire projectile(s)
            for (int i = 0; i < projectileCount; i++)
            {
                Vector3 adjustedTarget = targetPos;
                
                // Apply spread for multiple projectiles
                if (projectileCount > 1 && spreadAngle > 0)
                {
                    float spreadOffset = (i - (projectileCount - 1) / 2f) * spreadAngle;
                    Vector3 toTarget = (targetPos - spawnPos).normalized;
                    adjustedTarget = targetPos + Quaternion.AngleAxis(spreadOffset, Vector3.up) * toTarget * 2f;
                }
                
                // Calculate spawn delay for this projectile in the volley
                float totalDelay = spawnDelay + (i * volleyDelay);
                
                if (totalDelay > 0)
                {
                    // Schedule delayed spawn
                    context.Controller.StartCoroutine(DelayedProjectileWithEffectsSpawn(
                        totalDelay, skill, targeting, visuals, projectileSystem, spawnPos, adjustedTarget, damage,
                        sourceAgentIndex, targetAgentIndex, sourceFactionIndex, aoeRadius, context
                    ));
                }
                else
                {
                    // Immediate spawn
                    SpawnProjectileWithEffectsInternal(
                        skill, targeting, visuals, projectileSystem, spawnPos, adjustedTarget, damage,
                        sourceAgentIndex, targetAgentIndex, sourceFactionIndex, aoeRadius, context
                    );
                }
            }
        }
        
        /// <summary>
        /// Coroutine for delayed projectile spawning with effects.
        /// </summary>
        private static System.Collections.IEnumerator DelayedProjectileWithEffectsSpawn(
            float delay,
            AgentSkill skill,
            ProjectileTargetingConfig targeting,
            SkillProjectileConfig visuals,
            EnemyMassesProjectileSystem system,
            Vector3 spawnPos,
            Vector3 targetPos,
            float damage,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            float aoeRadius,
            SkillExecutionContext context)
        {
            yield return new WaitForSeconds(delay);
            SpawnProjectileWithEffectsInternal(
                skill, targeting, visuals, system, spawnPos, targetPos, damage,
                sourceAgentIndex, targetAgentIndex, sourceFactionIndex, aoeRadius, context
            );
        }
        
        /// <summary>
        /// Internal projectile spawn method for Projectile targeting type.
        /// Follows EnemyCrowd pattern: visuals are spawned locally on all clients,
        /// damage is validated through INetworkDamageAuthority on hit.
        /// </summary>
        private static void SpawnProjectileWithEffectsInternal(
            AgentSkill skill,
            ProjectileTargetingConfig targeting,
            SkillProjectileConfig visuals,
            EnemyMassesProjectileSystem system,
            Vector3 spawnPos,
            Vector3 targetPos,
            float damage,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            float aoeRadius,
            SkillExecutionContext context)
        {
            int projectileIndex = -1;
            
            // Get visual settings from VFX config (if available)
            GameObject projectilePrefab = visuals?.projectilePrefab;
            float visualScale = visuals?.visualScale ?? 1f;

            bool hasTrajectoryActions = skill?.OnTrajectoryActions != null && skill.OnTrajectoryActions.Count > 0;
            bool stopOnDesignatedTargetOnly = targeting != null &&
                (targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithFade ||
                 targeting.targetHitBehavior == ProjectileTargetHitBehavior.StopOnDesignatedTargetWithoutFade);
            bool piercePrimaryTarget = targeting != null && targeting.targetHitBehavior == ProjectileTargetHitBehavior.PierceThrough;
            bool fadeProjectileOnHit = targeting == null ||
                (targeting.targetHitBehavior != ProjectileTargetHitBehavior.StopWithoutFadeOnHit &&
                 targeting.targetHitBehavior != ProjectileTargetHitBehavior.StopOnDesignatedTargetWithoutFade);
            bool trajectoryPassThroughActive =
                targeting != null &&
                (targeting.trajectory == SkillProjectileTrajectory.Linear ||
                 targeting.trajectory == SkillProjectileTrajectory.Arc ||
                 targeting.trajectory == SkillProjectileTrajectory.Homing) &&
                (targeting.enableTrajectoryPiercing || hasTrajectoryActions || piercePrimaryTarget || stopOnDesignatedTargetOnly);

            Vector3 effectiveTargetPos = targetPos;
            int effectiveTargetAgentIndex = targetAgentIndex;

            if (trajectoryPassThroughActive && (piercePrimaryTarget || targetAgentIndex < 0))
            {
                if (targeting.trajectory != SkillProjectileTrajectory.Arc)
                {
                    Vector3 toTarget = targetPos - spawnPos;
                    float sqrDistanceToTarget = toTarget.sqrMagnitude;
                    if (sqrDistanceToTarget > 0.0001f)
                    {
                        float distanceToTarget = Mathf.Sqrt(sqrDistanceToTarget);
                        float travelDistance = targeting.maxRange > 0f
                            ? targeting.maxRange
                            : distanceToTarget;
                        effectiveTargetPos = spawnPos + toTarget.normalized * travelDistance;
                    }
                }

                // Terminal OnHit should resolve at actual terminal impact point,
                // not always force the originally selected target.
                effectiveTargetAgentIndex = -1;
            }
            
            // Generate a local projectile ID for callback tracking
            // This matches EnemyCrowd pattern: projectiles are spawned locally, damage is validated on hit
            // Uses hash of tick count + source for uniqueness
            int projectileCallbackId = (Time.frameCount * 1000 + sourceAgentIndex).GetHashCode();
            if (projectileCallbackId == 0) projectileCallbackId = 1; // Ensure non-zero
            
            switch (targeting.trajectory)
            {
                case SkillProjectileTrajectory.Arc:
                    // Use arc mode settings from visuals
                    float? flightTime = null;
                    float launchAngle = visuals?.launchAngle ?? 45f;
                    
                    if (visuals != null)
                    {
                        switch (visuals.arcMode)
                        {
                            case ArcHeightMode.FlightTime:
                                flightTime = visuals.flightTime;
                                break;
                            case ArcHeightMode.FixedHeight:
                                float v0y = Mathf.Sqrt(2f * (visuals.gravity) * visuals.arcHeight);
                                flightTime = 2f * v0y / visuals.gravity;
                                break;
                        }
                    }
                    
                    projectileIndex = system.FireParabolicProjectile(
                        spawnPos,
                        effectiveTargetPos,
                        damage,
                        aoeRadius,
                        sourceAgentIndex,
                        effectiveTargetAgentIndex,
                        sourceFactionIndex,
                        flightTime,
                        launchAngle,
                        true, // spawnVisual
                        projectilePrefab,
                        visualScale,
                        0, // effectsConfigId
                        projectileCallbackId,
                        fadeProjectileOnHit,
                        visuals?.gravity
                    );
                    break;
                    
                case SkillProjectileTrajectory.Linear:
                    projectileIndex = system.FireLinearProjectile(
                        spawnPos,
                        effectiveTargetPos,
                        visuals?.linearSpeed ?? 30f,
                        damage,
                        sourceAgentIndex,
                        effectiveTargetAgentIndex,
                        sourceFactionIndex,
                        5f, // lifetime
                        true, // spawnVisual
                        projectilePrefab,
                        visualScale,
                        0, // effectsConfigId
                        aoeRadius,
                        projectileCallbackId,
                        fadeProjectileOnHit
                    );
                    break;
                    
                case SkillProjectileTrajectory.Homing:
                    projectileIndex = system.FireHomingProjectile(
                        spawnPos,
                        effectiveTargetPos,
                        visuals?.homingSpeed ?? 20f,
                        damage,
                        sourceAgentIndex,
                        effectiveTargetAgentIndex,
                        sourceFactionIndex,
                        visuals?.homingTurnRate ?? 180f,
                        visuals?.homingAcceleration ?? 0f,
                        10f, // lifetime (longer for homing)
                        true,
                        projectilePrefab,
                        visualScale,
                        0,
                        aoeRadius,
                        projectileCallbackId,
                        fadeProjectileOnHit
                    );
                    break;
            }
            
            // Register skill callback so effects get applied on impact
            if (projectileIndex >= 0 && projectileCallbackId != 0)
            {
                var callbackContext = new SkillExecutionContext
                {
                    Caster = context.Caster,
                    CasterTransform = context.CasterTransform,
                    PrimaryTarget = context.PrimaryTarget,
                    AllTargets = context.AllTargets,
                    TargetPosition = context.TargetPosition,
                    Skill = context.Skill,
                    Controller = context.Controller,
                    IsCritical = context.IsCritical,
                    EffectMultiplier = context.EffectMultiplier,
                    ExecutionId = context.ExecutionId,
                    NetworkProjectileId = projectileCallbackId,
                    DamageableTarget = context.DamageableTarget
                };
                system.RegisterSkillProjectileCallback(projectileCallbackId, skill, callbackContext, targeting);
            }
            
            // Play launch sound
            if (visuals?.launchSound != null)
            {
                AudioSource.PlayClipAtPoint(visuals.launchSound, spawnPos, visuals.launchVolume);
            }
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // PROJECTILE ARC (SERVER AUTHORITATIVE)
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Fires a projectile arc for a skill using the EnemyMassesProjectileSystem.
        /// Uses the same mathematical formulas as EnemyCrowd ProjectileArc attack mode.
        /// </summary>
        public static void FireProjectileArc(SkillProjectileConfig config, SkillExecutionContext context)
        {
            if (config == null || !config.enabled)
                return;

            if (!Networking.NetworkAuthorityProvider.IsServerAuthority)
                return;
            
            if (context.Controller == null)
                return;

            if (context.Caster == null && context.CasterTransform == null)
                return;
            
            // Get projectile system singleton
            var projectileSystem = EnemyMassesProjectileSystem.Instance;
            if (projectileSystem == null)
            {
                Debug.LogWarning("[SkillExecutor] No ProjectileSystem available for projectile arc skill.");
                return;
            }
            
            // Get spawn position from caster (or fallback transform)
            Vector3 casterPos = GetCasterPosition(context.Caster, context.CasterTransform);
            Vector3 spawnPos = GetProjectileSpawnPosition(config, context.Caster, casterPos, context.CasterTransform);
            
            // Get target position
            Vector3 targetPos = GetProjectileTargetPosition(config, context);
            
            // Calculate damage with multipliers
            float baseDamage = config.damage * context.EffectMultiplier;
            
            int sourceAgentIndex = context.Caster?.index ?? -1;
            int targetAgentIndex = context.PrimaryTarget?.index ?? -1;
            int sourceFactionIndex = context.Caster?.factionIndex ?? -1;
            
            // Fire projectile(s)
            for (int i = 0; i < config.projectileCount; i++)
            {
                Vector3 adjustedTarget = targetPos;
                
                // Apply spread for multiple projectiles
                if (config.projectileCount > 1 && config.spreadAngle > 0)
                {
                    float spreadOffset = (i - (config.projectileCount - 1) / 2f) * config.spreadAngle;
                    Vector3 toTarget = (targetPos - spawnPos).normalized;
                    adjustedTarget = targetPos + Quaternion.AngleAxis(spreadOffset, Vector3.up) * toTarget * 2f;
                }
                
                // Calculate spawn delay for this projectile in the volley
                float totalDelay = config.spawnDelay + (i * config.volleyDelay);
                
                if (totalDelay > 0)
                {
                    // Schedule delayed spawn using coroutine
                    context.Controller.StartCoroutine(DelayedProjectileSpawn(
                        totalDelay, config, projectileSystem, spawnPos, adjustedTarget, baseDamage,
                        sourceAgentIndex, targetAgentIndex, sourceFactionIndex, context
                    ));
                }
                else
                {
                    // Immediate spawn
                    SpawnProjectileInternal(
                        config, projectileSystem, spawnPos, adjustedTarget, baseDamage,
                        sourceAgentIndex, targetAgentIndex, sourceFactionIndex, context
                    );
                }
            }
        }
        
        /// <summary>
        /// Coroutine for delayed projectile spawning.
        /// </summary>
        private static System.Collections.IEnumerator DelayedProjectileSpawn(
            float delay,
            SkillProjectileConfig config,
            EnemyMassesProjectileSystem system,
            Vector3 spawnPos,
            Vector3 targetPos,
            float damage,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            SkillExecutionContext context)
        {
            yield return new WaitForSeconds(delay);
            SpawnProjectileInternal(
                config, system, spawnPos, targetPos, damage,
                sourceAgentIndex, targetAgentIndex, sourceFactionIndex, context
            );
        }
        
        /// <summary>
        /// Internal projectile spawn method used by both immediate and delayed spawning.
        /// </summary>
        private static void SpawnProjectileInternal(
            SkillProjectileConfig config,
            EnemyMassesProjectileSystem system,
            Vector3 spawnPos,
            Vector3 targetPos,
            float damage,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex,
            SkillExecutionContext context)
        {
            int projectileIndex = -1;
            float aoeRadius = config.enableAOE ? config.aoeRadius : 0f;
            
            switch (config.trajectory)
            {
                case SkillProjectileTrajectory.Arc:
                    projectileIndex = SpawnArcProjectileInternal(
                        config, system, spawnPos, targetPos, damage, aoeRadius,
                        sourceAgentIndex, targetAgentIndex, sourceFactionIndex
                    );
                    break;
                    
                case SkillProjectileTrajectory.Linear:
                    projectileIndex = system.FireLinearProjectile(
                        spawnPos,
                        targetPos,
                        config.linearSpeed,
                        damage,
                        sourceAgentIndex,
                        targetAgentIndex,
                        sourceFactionIndex,
                        5f, // lifetime
                        true, // spawnVisual
                        config.projectilePrefab,
                        config.visualScale,
                        0, // effectsConfigId
                        aoeRadius
                    );
                    break;
                    
                case SkillProjectileTrajectory.Homing:
                    projectileIndex = system.FireHomingProjectile(
                        spawnPos,
                        targetPos,
                        config.homingSpeed,
                        damage,
                        sourceAgentIndex,
                        targetAgentIndex,
                        sourceFactionIndex,
                        config.homingTurnRate,
                        config.homingAcceleration,
                        10f, // lifetime (longer for homing)
                        true, // spawnVisual
                        config.projectilePrefab,
                        config.visualScale,
                        0, // effectsConfigId
                        aoeRadius
                    );
                    break;
            }
            
            // Play launch sound
            if (config.launchSound != null)
            {
                AudioSource.PlayClipAtPoint(config.launchSound, spawnPos, config.launchVolume);
            }
        }
        
        /// <summary>
        /// Spawns an arc (parabolic) projectile using the correct formula based on arc mode.
        /// </summary>
        private static int SpawnArcProjectileInternal(
            SkillProjectileConfig config,
            EnemyMassesProjectileSystem system,
            Vector3 spawnPos,
            Vector3 targetPos,
            float damage,
            float aoeRadius,
            int sourceAgentIndex,
            int targetAgentIndex,
            int sourceFactionIndex)
        {
            // Determine flight time based on arc mode
            float? flightTime = null;
            float launchAngle = config.launchAngle;
            
            switch (config.arcMode)
            {
                case ArcHeightMode.FlightTime:
                    flightTime = config.flightTime;
                    break;
                    
                case ArcHeightMode.FixedHeight:
                    // Calculate flight time from desired height
                    // h = v₀y² / (2g), so v₀y = sqrt(2gh)
                    // t_total ≈ 2 * v₀y / g for symmetric arc
                    float v0y = Mathf.Sqrt(2f * config.gravity * config.arcHeight);
                    flightTime = 2f * v0y / config.gravity;
                    break;
                    
                case ArcHeightMode.LaunchAngle:
                default:
                    // Use launch angle (system will calculate velocity)
                    launchAngle = config.launchAngle;
                    break;
            }
            
            return system.FireParabolicProjectile(
                spawnPos,
                targetPos,
                damage,
                aoeRadius,
                sourceAgentIndex,
                targetAgentIndex,
                sourceFactionIndex,
                flightTime,
                launchAngle,
                true, // spawnVisual
                config.projectilePrefab,
                config.visualScale,
                0, // effectsConfigId
                0, // networkProjectileId
                true, // fadeProjectileOnHit
                config.gravity
            );
        }
        
        /// <summary>
        /// Gets the caster position for projectile spawning.
        /// Falls back to <paramref name="fallbackTransform"/> when <paramref name="caster"/> is <c>null</c>
        /// (e.g. projectiles fired by a <see cref="StandaloneSkillCaster"/>).
        /// </summary>
        private static Vector3 GetCasterPosition(CrowdAgent caster, Transform fallbackTransform = null)
        {
            if (caster != null)
                return caster.WorldPosition;
            if (fallbackTransform != null)
                return fallbackTransform.position;
            return Vector3.zero;
        }
        
        /// <summary>
        /// Gets the projectile spawn position based on config.
        /// Falls back to <paramref name="fallbackTransform"/> when <paramref name="caster"/> is <c>null</c>
        /// (e.g. projectiles fired by a <see cref="StandaloneSkillCaster"/>).
        /// </summary>
        private static Vector3 GetProjectileSpawnPosition(SkillProjectileConfig config, CrowdAgent caster,
            Vector3 casterPos, Transform fallbackTransform = null)
        {
            Transform casterTransform = caster != null
                ? (caster.navigationAgent?.Transform ?? caster.transform)
                : fallbackTransform;

            if (casterTransform == null)
                return casterPos + config.spawnOffset;
            
            // Try to find spawn bone if specified
            if (!string.IsNullOrEmpty(config.spawnBoneName))
            {
                Transform bone = FindBoneRecursive(casterTransform, config.spawnBoneName);
                if (bone != null)
                    return bone.position;
            }
            
            // Apply offset in local space
            return casterTransform.TransformPoint(config.spawnOffset);
        }
        
        /// <summary>
        /// Recursively finds a bone by name.
        /// </summary>
        private static Transform FindBoneRecursive(Transform root, string boneName)
        {
            if (root.name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase))
                return root;
            
            foreach (Transform child in root)
            {
                var result = FindBoneRecursive(child, boneName);
                if (result != null)
                    return result;
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets the target position for the projectile.
        /// </summary>
        private static Vector3 GetProjectileTargetPosition(SkillProjectileConfig config, SkillExecutionContext context)
        {
            // Primary target agent
            if (context.PrimaryTarget != null)
            {
                return context.PrimaryTarget.WorldPosition;
            }
            
            // Ground-targeted position
            return context.TargetPosition;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // ANIMATION
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Triggers the animation for a skill on the caster.
        /// Uses the trigger names configured in the agent's EnemyCrowd settings.
        /// </summary>
        public static void TriggerSkillAnimation(AgentSkill skill, CrowdAgent agent)
        {
            if (skill == null || agent == null) return;
            
            // Handle SkillSpecificAnimation - may need to play clip directly
            if (skill.AnimationType == SkillAnimationType.SkillSpecificAnimation)
            {
                TriggerSkillSpecificAnimation(skill, agent);
                return;
            }
            
            // Standard animation trigger approach
            var animator = agent.mecanimAnimator;
            if (animator == null) return;
            
            string triggerName = GetAnimationTriggerForSkill(skill, agent);
            if (!string.IsNullOrEmpty(triggerName))
            {
                animator.SetTrigger(triggerName);
            }
        }
        
        /// <summary>
        /// Triggers a skill-specific animation, handling both Compute Animator (clip) and Mecanim/Bone Tracker (trigger) workflows.
        /// </summary>
        private static void TriggerSkillSpecificAnimation(AgentSkill skill, CrowdAgent agent)
        {
            if (skill == null || agent == null) return;
            
            // Use the agent's resolved runtime workflow (more reliable than static crowd config).
            bool useComputeAnimator = agent.currentWorkflow == HybridWorkflowType.ComputeAnimator;
            
            // For Compute Animator: Play the animation clip directly
            if (useComputeAnimator && skill.SkillAnimationClip != null)
            {
                PlayAnimationClipOnAgent(agent, skill.SkillAnimationClip);
            }
            
            // For Mecanim/Bone Tracker: Use trigger parameter when configured.
            if (!string.IsNullOrEmpty(skill.CustomAnimationTrigger))
            {
                var animator = agent.mecanimAnimator;
                if (animator != null)
                {
                    animator.SetTrigger(skill.CustomAnimationTrigger);
                }
            }
            else if (!useComputeAnimator && skill.SkillAnimationClip != null)
            {
                // Fallback: if no trigger is configured, try to play a Mecanim state that
                // matches the assigned clip name so Skill Specific Animation still works.
                TryPlayMecanimSkillClipByStateName(agent, skill.SkillAnimationClip);
            }
        }

        private static void TryPlayMecanimSkillClipByStateName(CrowdAgent agent, AnimationClip clip)
        {
            if (agent == null || clip == null)
                return;

            var animator = agent.mecanimAnimator;
            if (animator == null)
                return;

            int stateHash = Animator.StringToHash(clip.name);
            if (!animator.HasState(0, stateHash))
                return;

            animator.CrossFade(stateHash, 0.05f, 0, 0f);
        }
        
        /// <summary>
        /// Plays an animation clip directly on an agent using GPU Instancer's crowd animation system.
        /// Used for Compute Animator workflow.
        /// </summary>
        private static void PlayAnimationClipOnAgent(CrowdAgent agent, AnimationClip clip)
        {
            if (agent?.crowdInstance == null || clip == null) return;
            
            try
            {
                // Use GPUICrowdInstance.StartAnimation for Compute Animator workflow
                // Parameters: clip, startTime, speed, transitionTime, queue, loop
                agent.crowdInstance.StartAnimation(clip, -1f, 1f, 0.15f, null, false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SkillExecutor] Failed to play skill animation clip: {e.Message}");
            }
        }
        
        /// <summary>
        /// Gets the appropriate animation trigger name based on skill type and agent's EnemyCrowd configuration.
        /// </summary>
        private static string GetAnimationTriggerForSkill(AgentSkill skill, CrowdAgent agent)
        {
            switch (skill.AnimationType)
            {
                case SkillAnimationType.CustomTrigger:
                    return skill.CustomAnimationTrigger;
                    
                case SkillAnimationType.UseMeleeAnimation:
                    return GetMeleeAttackTrigger(agent);
                    
                case SkillAnimationType.UseRangedAnimation:
                    return GetRangedAttackTrigger(agent);
                    
                case SkillAnimationType.UseCastAnimation:
                    return GetCastTrigger(agent);
                    
                case SkillAnimationType.SkillSpecificAnimation:
                    // Handled separately in TriggerSkillSpecificAnimation
                    return skill.CustomAnimationTrigger;
                    
                case SkillAnimationType.None:
                default:
                    return null;
            }
        }
        
        /// <summary>
        /// Gets a melee attack trigger from the agent's EnemyCrowd configuration.
        /// Falls back to general attack triggers if no melee-specific triggers are defined.
        /// </summary>
        private static string GetMeleeAttackTrigger(CrowdAgent agent)
        {
            if (agent?.enemyCrowd == null)
                return "Attack"; // Default fallback
            
            var crowd = agent.enemyCrowd;
            
            // First, try melee-specific triggers
            if (crowd.meleeAttackTriggerParametersOverride != null && crowd.meleeAttackTriggerParametersOverride.Length > 0)
            {
                return GetRandomTrigger(crowd.meleeAttackTriggerParametersOverride);
            }
            
            // Fall back to general attack triggers
            if (crowd.attackTriggerParametersOverride != null && crowd.attackTriggerParametersOverride.Length > 0)
            {
                return GetRandomTrigger(crowd.attackTriggerParametersOverride);
            }
            
            // Default fallback - common trigger names
            return "Attack";
        }
        
        /// <summary>
        /// Gets a ranged attack trigger from the agent's EnemyCrowd configuration.
        /// Falls back to general attack triggers if no ranged-specific triggers are defined.
        /// </summary>
        private static string GetRangedAttackTrigger(CrowdAgent agent)
        {
            if (agent?.enemyCrowd == null)
                return "RangedAttack"; // Default fallback
            
            var crowd = agent.enemyCrowd;
            
            // First, try ranged-specific triggers
            if (crowd.rangedAttackTriggerParametersOverride != null && crowd.rangedAttackTriggerParametersOverride.Length > 0)
            {
                return GetRandomTrigger(crowd.rangedAttackTriggerParametersOverride);
            }
            
            // Fall back to general attack triggers (for games where melee/ranged use same triggers)
            if (crowd.attackTriggerParametersOverride != null && crowd.attackTriggerParametersOverride.Length > 0)
            {
                return GetRandomTrigger(crowd.attackTriggerParametersOverride);
            }
            
            // Default fallback - common trigger names
            return "RangedAttack";
        }
        
        /// <summary>
        /// Gets a cast/channel animation trigger from the agent's EnemyCrowd configuration.
        /// Falls back to "Cast" if no cast-specific triggers are defined.
        /// </summary>
        private static string GetCastTrigger(CrowdAgent agent)
        {
            if (agent?.enemyCrowd == null)
                return "Cast"; // Default fallback
            
            var crowd = agent.enemyCrowd;
            
            // Check for cast-specific triggers (if your EnemyCrowd has castTriggerParametersOverride)
            // For now, we'll use a custom trigger field if available, otherwise fall back
            // Future: Add castTriggerParametersOverride to EnemyCrowd for spell-caster factions
            
            // Try to find a cast animation trigger in the EnemyCrowd
            // This could be extended to support a dedicated castTriggerParametersOverride field
            
            return "Cast"; // Default - users can use CustomTrigger for faction-specific cast anims
        }
        
        /// <summary>
        /// Gets a random trigger from an array of triggers for animation variety.
        /// </summary>
        private static string GetRandomTrigger(string[] triggers)
        {
            if (triggers == null || triggers.Length == 0)
                return null;
            
            // Filter out empty/null entries
            var validTriggers = new System.Collections.Generic.List<string>();
            for (int i = 0; i < triggers.Length; i++)
            {
                if (!string.IsNullOrEmpty(triggers[i]))
                    validTriggers.Add(triggers[i]);
            }
            
            if (validTriggers.Count == 0)
                return null;
            
            return validTriggers[UnityEngine.Random.Range(0, validTriggers.Count)];
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // HELPER METHODS - Target finding utilities
        #endregion ════════════════════════════════════════════════════════════
        
        private static float GetAgentHealthPercent(CrowdAgent agent)
        {
            if (agent == null) return 1f;
            
            // Use the controller to get max health
            var controller = EnemyMassesCrowdController.Instance;
            if (controller != null)
            {
                float maxHealth = controller.GetAgentMaxHealth(agent.index);
                if (maxHealth > 0)
                {
                    return agent.health / maxHealth;
                }
            }
            
            // Fallback: Use base health from EnemyCrowd
            if (agent.enemyCrowd != null && agent.enemyCrowd.agentBaseHealth > 0)
            {
                return agent.health / agent.enemyCrowd.agentBaseHealth;
            }
            
            return 1f;
        }
        
        private static int CountEnemiesInRange(CrowdAgent agent, List<CrowdAgent> targets, float range)
        {
            if (agent == null || targets == null) return 0;
            
            int count = 0;
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (!IsEnemy(agentFaction, targetFaction)) continue;
                
                float dist = Vector3.Distance(pos, target.WorldPosition);
                if (dist <= range) count++;
            }
            
            return count;
        }
        
        private static int CountAlliesInRange(CrowdAgent agent, List<CrowdAgent> targets, float range)
        {
            if (agent == null || targets == null) return 0;
            
            int count = 0;
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (!IsAlly(agentFaction, targetFaction)) continue;
                
                float dist = Vector3.Distance(pos, target.WorldPosition);
                if (dist <= range) count++;
            }
            
            return count;
        }
        
        private static CrowdAgent GetNearestEnemy(CrowdAgent agent, List<CrowdAgent> targets)
        {
            return GetNearestEnemyInRange(agent, targets, 0f, float.MaxValue);
        }
        
        /// <summary>
        /// Checks if a target passes the vertical height and line of sight requirements from EnemyCrowd.
        /// </summary>
        private static bool PassesVerticalAndLOSCheck(CrowdAgent caster, CrowdAgent target, Vector3 casterPos, Vector3 targetPos)
        {
            if (caster?.enemyCrowd == null) return true;
            
            var crowd = caster.enemyCrowd;
            
            // VERTICAL HEIGHT CHECK
            float maxHeightDiff = crowd.maxTargetHeightDifference;
            if (maxHeightDiff > 0f)
            {
                float heightDiff = Mathf.Abs(targetPos.y - casterPos.y);
                if (heightDiff > maxHeightDiff)
                    return false;
            }
            
            // LINE OF SIGHT CHECK
            if (crowd.requireLineOfSight)
            {
                // Raycast from caster eye height to target center
                Vector3 eyePos = casterPos + Vector3.up * 1.5f;
                Vector3 targetCenter = targetPos + Vector3.up * 1.0f;
                Vector3 direction = targetCenter - eyePos;
                float distance = direction.magnitude;
                
                if (Physics.Raycast(eyePos, direction.normalized, distance, crowd.lineOfSightBlockingLayers))
                {
                    // LOS blocked
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Checks if a target passes the vertical height and LOS requirements.
        /// Optimized version for ClosestCandidatesOnly mode - only checks top N candidates.
        /// </summary>
        private static bool PassesLOSCheck(CrowdAgent caster, Vector3 casterPos, Vector3 targetPos, LayerMask losLayers)
        {
            // Raycast from caster eye height to target center
            Vector3 eyePos = casterPos + Vector3.up * 1.5f;
            Vector3 targetCenter = targetPos + Vector3.up * 1.0f;
            Vector3 direction = targetCenter - eyePos;
            float distance = direction.magnitude;
            
            return !Physics.Raycast(eyePos, direction.normalized, distance, losLayers);
        }
        
        private static CrowdAgent GetNearestEnemyInRange(
            CrowdAgent agent,
            List<CrowdAgent> targets,
            float minRange,
            float maxRange,
            bool skipFlying = false,
            bool allowFlyingHeightBypass = false)
        {
            if (agent == null || targets == null) return null;
            
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            // Get targeting settings from EnemyCrowd
            var crowd = agent.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = crowd?.requireLineOfSight ?? false;
            LayerMask losLayers = crowd?.lineOfSightBlockingLayers ?? ~0;
            var losMode = crowd?.losCheckMode ?? EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly;
            
            // For ClosestCandidatesOnly mode: collect candidates first, then LOS check only top N
            List<(CrowdAgent target, float dist, Vector3 targetPos)> losCandidates = null;
            if (requireLOS && losMode == EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly)
            {
                losCandidates = new List<(CrowdAgent, float, Vector3)>(8);
            }
            
            CrowdAgent nearest = null;
            float nearestDist = float.MaxValue;
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                // Skip flying agents when the calling skill cannot reach them
                if (skipFlying && target.isFlying) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (!IsEnemy(agentFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(pos, targetPos);
                
                if (dist < minRange || dist > maxRange) continue;
                
                // VERTICAL HEIGHT CHECK (always run - cheap)
                if (maxHeightDiff > 0f)
                {
                    bool bypassHeightLimitForFlyingTarget = allowFlyingHeightBypass && target.isFlying;
                    if (!bypassHeightLimitForFlyingTarget)
                    {
                        float heightDiff = Mathf.Abs(targetPos.y - pos.y);
                        if (heightDiff > maxHeightDiff)
                            continue;
                    }
                }
                
                // LINE OF SIGHT CHECK with performance modes
                if (requireLOS)
                {
                    if (losMode == EnemyCrowd.LOSCheckMode.ClosestCandidatesOnly)
                    {
                        // Defer LOS check - collect candidates first
                        losCandidates.Add((target, dist, targetPos));
                        continue;
                    }
                    else if (losMode == EnemyCrowd.LOSCheckMode.Probabilistic)
                    {
                        // 25% chance to check LOS (amortized cost)
                        if (UnityEngine.Random.value < 0.75f)
                        {
                            // Skip LOS check this time - accept target if closest
                            if (dist < nearestDist)
                            {
                                nearestDist = dist;
                                nearest = target;
                            }
                            continue;
                        }
                    }
                    
                    // Full LOS check
                    if (!PassesLOSCheck(agent, pos, targetPos, losLayers))
                        continue;
                }
                
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = target;
                }
            }
            
            // Process deferred LOS checks for ClosestCandidatesOnly mode
            if (losCandidates != null && losCandidates.Count > 0)
            {
                // Sort by distance and check LOS only for closest N candidates
                losCandidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                int maxCandidatesToCheck = Mathf.Min(losCandidates.Count, 5);
                
                for (int i = 0; i < maxCandidatesToCheck; i++)
                {
                    var candidate = losCandidates[i];
                    if (PassesLOSCheck(agent, pos, candidate.targetPos, losLayers))
                    {
                        return candidate.target;
                    }
                }
            }
            
            return nearest;
        }
        
        private static CrowdAgent GetNearestAllyInRange(CrowdAgent agent, List<CrowdAgent> targets, float minRange, float maxRange)
        {
            if (agent == null || targets == null) return null;
            
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            // Get targeting settings from EnemyCrowd (for height check - LOS usually not needed for allies)
            var crowd = agent.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            
            CrowdAgent nearest = null;
            float nearestDist = float.MaxValue;
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (!IsAlly(agentFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(pos, targetPos);
                
                if (dist < minRange || dist > maxRange) continue;
                
                // VERTICAL HEIGHT CHECK (for allies too - healers need to reach targets)
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - pos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = target;
                }
            }
            
            return nearest;
        }
        
        private static CrowdAgent GetLowestHealthAllyInRange(CrowdAgent agent, List<CrowdAgent> targets, float minRange, float maxRange)
        {
            if (agent == null || targets == null) return null;
            
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            // Get targeting settings from EnemyCrowd
            var crowd = agent.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            
            CrowdAgent lowestHealthAlly = null;
            float lowestHealth = float.MaxValue;
            
            foreach (var target in targets)
            {
                if (target == null) continue;
                
                // Allow targeting self for heals
                if (target != agent)
                {
                    string targetFaction = target.enemyCrowd?.factionName ?? "";
                    if (!IsAlly(agentFaction, targetFaction)) continue;
                }
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(pos, targetPos);
                
                if (dist < minRange || dist > maxRange) continue;
                
                // VERTICAL HEIGHT CHECK
                if (maxHeightDiff > 0f && target != agent)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - pos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                float healthPercent = GetAgentHealthPercent(target);
                if (healthPercent < lowestHealth)
                {
                    lowestHealth = healthPercent;
                    lowestHealthAlly = target;
                }
            }
            
            return lowestHealthAlly;
        }
        
        private static List<CrowdAgent> GetAllEnemiesInRange(CrowdAgent agent, List<CrowdAgent> targets, float maxRange, int maxTargets)
        {
            var result = new List<CrowdAgent>();
            if (agent == null || targets == null) return result;
            
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            // Get targeting settings from EnemyCrowd
            var crowd = agent.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = crowd?.requireLineOfSight ?? false;
            LayerMask losLayers = crowd?.lineOfSightBlockingLayers ?? ~0;
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (!IsEnemy(agentFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(pos, targetPos);
                
                if (dist > maxRange) continue;
                
                // VERTICAL HEIGHT CHECK
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - pos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                // LINE OF SIGHT CHECK (for AOE skills targeting all enemies)
                if (requireLOS && !PassesLOSCheck(agent, pos, targetPos, losLayers))
                    continue;
                
                result.Add(target);
                if (maxTargets > 0 && result.Count >= maxTargets) break;
            }
            
            return result;
        }
        
        private static List<CrowdAgent> GetAllAlliesInRange(CrowdAgent agent, List<CrowdAgent> targets, float maxRange, int maxTargets)
        {
            var result = new List<CrowdAgent>();
            if (agent == null || targets == null) return result;
            
            Vector3 pos = agent.WorldPosition;
            string agentFaction = agent.enemyCrowd?.factionName ?? "";
            
            // Get targeting settings from EnemyCrowd
            var crowd = agent.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            
            // Include self for ally-targeting skills
            result.Add(agent);
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (!IsAlly(agentFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(pos, targetPos);
                
                if (dist > maxRange) continue;
                
                // VERTICAL HEIGHT CHECK
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - pos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                result.Add(target);
                if (maxTargets > 0 && result.Count >= maxTargets) break;
            }
            
            return result;
        }
        
        /// <summary>
        /// Returns all agents (including the caster) that share the same EnemyCrowd asset.
        /// Range is ignored — crowd membership is the only filter.
        /// </summary>
        private static List<CrowdAgent> GetAllCrowdMembers(CrowdAgent caster, List<CrowdAgent> potentialTargets)
        {
            var result = new List<CrowdAgent>();
            if (caster == null) return result;
            
            var casterCrowd = caster.enemyCrowd;
            if (casterCrowd == null)
            {
                // No crowd reference — fallback to self only
                result.Add(caster);
                return result;
            }
            
            // Always include the caster
            result.Add(caster);
            
            if (potentialTargets == null) return result;
            
            foreach (var target in potentialTargets)
            {
                if (target == null || target == caster || target.isDead) continue;
                
                // Same EnemyCrowd asset reference = same crowd
                if (target.enemyCrowd == casterCrowd)
                {
                    result.Add(target);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Returns all agents (including the caster) that share the same faction.
        /// Range is ignored — faction membership is the only filter.
        /// </summary>
        private static List<CrowdAgent> GetAllFactionMembers(CrowdAgent caster, List<CrowdAgent> potentialTargets)
        {
            var result = new List<CrowdAgent>();
            if (caster == null) return result;
            
            string casterFaction = caster.enemyCrowd?.factionName ?? "";
            
            // Always include the caster
            result.Add(caster);
            
            if (potentialTargets == null || string.IsNullOrEmpty(casterFaction)) return result;
            
            foreach (var target in potentialTargets)
            {
                if (target == null || target == caster || target.isDead) continue;
                
                // Same faction = ally
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (IsAlly(casterFaction, targetFaction))
                {
                    result.Add(target);
                }
            }
            
            return result;
        }
        
        private static bool HasEnemyInRange(CrowdAgent agent, List<CrowdAgent> targets, float minRange, float maxRange, bool skipFlying = false)
        {
            return GetNearestEnemyInRange(
                agent,
                targets,
                minRange,
                maxRange,
                skipFlying,
                allowFlyingHeightBypass: !skipFlying) != null;
        }
        
        private static bool HasAllyInRange(CrowdAgent agent, List<CrowdAgent> targets, float minRange, float maxRange)
        {
            return GetNearestAllyInRange(agent, targets, minRange, maxRange) != null;
        }
        
        private static bool HasAnyTargetInRange(CrowdAgent agent, List<CrowdAgent> targets, float minRange, float maxRange)
        {
            if (agent == null || targets == null) return false;
            
            Vector3 pos = agent.WorldPosition;
            
            foreach (var target in targets)
            {
                if (target == null || target == agent) continue;
                
                float dist = Vector3.Distance(pos, target.WorldPosition);
                if (dist >= minRange && dist <= maxRange) return true;
            }
            
            return false;
        }
        
        private static List<CrowdAgent> GetAllInRadius(
            CrowdAgent caster,
            List<CrowdAgent> targets,
            Vector3 center,
            float radius,
            int maxTargets,
            SkillType skillType)
        {
            var result = new List<CrowdAgent>();
            if (targets == null) return result;
            
            string casterFaction = caster?.enemyCrowd?.factionName ?? "";
            bool targetEnemies = skillType == SkillType.Attack || skillType == SkillType.Debuff;
            bool targetAllies = skillType == SkillType.Heal || skillType == SkillType.Buff;
            
            // Get targeting settings from EnemyCrowd
            var crowd = caster?.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = crowd?.requireLineOfSight ?? false;
            LayerMask losLayers = crowd?.lineOfSightBlockingLayers ?? ~0;
            
            foreach (var target in targets)
            {
                if (target == null) continue;
                
                // For self-targeted skills, include self
                if (targetAllies && target == caster)
                {
                    result.Add(target);
                    continue;
                }
                
                if (target == caster) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                
                // Check faction relationship
                if (targetEnemies && !IsEnemy(casterFaction, targetFaction)) continue;
                if (targetAllies && !IsAlly(casterFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(center, targetPos);
                
                if (dist > radius) continue;
                
                // VERTICAL HEIGHT CHECK (using center point for AOE)
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - center.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                // LINE OF SIGHT CHECK (for AOE from center)
                if (requireLOS && targetEnemies)
                {
                    if (!PassesLOSCheck(caster, center, targetPos, losLayers))
                        continue;
                }
                
                result.Add(target);
                if (result.Count >= maxTargets) break;
            }
            
            return result;
        }
        
        private static List<CrowdAgent> GetAllInCone(
            CrowdAgent caster,
            List<CrowdAgent> targets,
            float range,
            float coneAngle,
            int maxTargets,
            SkillType skillType)
        {
            var result = new List<CrowdAgent>();
            if (caster == null || targets == null) return result;
            
            Vector3 casterPos = caster.WorldPosition;
            Vector3 forward = caster.navigationAgent?.Transform?.forward ?? caster.transform?.forward ?? Vector3.forward;
            float halfAngle = coneAngle * 0.5f;
            
            string casterFaction = caster.enemyCrowd?.factionName ?? "";
            bool targetEnemies = skillType == SkillType.Attack || skillType == SkillType.Debuff;
            
            // Get targeting settings from EnemyCrowd
            var crowd = caster.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = crowd?.requireLineOfSight ?? false;
            LayerMask losLayers = crowd?.lineOfSightBlockingLayers ?? ~0;
            
            foreach (var target in targets)
            {
                if (target == null || target == caster) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (targetEnemies && !IsEnemy(casterFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                Vector3 toTarget = targetPos - casterPos;
                float dist = toTarget.magnitude;
                
                if (dist > range) continue;
                
                float angle = Vector3.Angle(forward, toTarget);
                if (angle > halfAngle) continue;
                
                // VERTICAL HEIGHT CHECK
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - casterPos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                // LINE OF SIGHT CHECK (cone attacks are typically visible attacks)
                if (requireLOS && targetEnemies)
                {
                    if (!PassesLOSCheck(caster, casterPos, targetPos, losLayers))
                        continue;
                }
                
                result.Add(target);
                if (result.Count >= maxTargets) break;
            }
            
            return result;
        }
        
        private static List<CrowdAgent> GetAllInLine(
            CrowdAgent caster,
            List<CrowdAgent> targets,
            float range,
            float lineWidth,
            int maxTargets,
            SkillType skillType)
        {
            var result = new List<CrowdAgent>();
            if (caster == null || targets == null) return result;
            
            Vector3 casterPos = caster.WorldPosition;
            Vector3 forward = caster.navigationAgent?.Transform?.forward ?? caster.transform?.forward ?? Vector3.forward;
            float halfWidth = lineWidth * 0.5f;
            
            string casterFaction = caster.enemyCrowd?.factionName ?? "";
            bool targetEnemies = skillType == SkillType.Attack || skillType == SkillType.Debuff;
            
            // Get targeting settings from EnemyCrowd
            var crowd = caster.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = crowd?.requireLineOfSight ?? false;
            LayerMask losLayers = crowd?.lineOfSightBlockingLayers ?? ~0;
            
            foreach (var target in targets)
            {
                if (target == null || target == caster) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (targetEnemies && !IsEnemy(casterFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                Vector3 toTarget = targetPos - casterPos;
                float forwardDist = Vector3.Dot(toTarget, forward);
                
                if (forwardDist < 0 || forwardDist > range) continue;
                
                // Distance from line
                Vector3 pointOnLine = casterPos + forward * forwardDist;
                float perpDist = Vector3.Distance(targetPos, pointOnLine);
                
                if (perpDist > halfWidth) continue;
                
                // VERTICAL HEIGHT CHECK
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - casterPos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                // LINE OF SIGHT CHECK (line attacks typically require clear path)
                if (requireLOS && targetEnemies)
                {
                    if (!PassesLOSCheck(caster, casterPos, targetPos, losLayers))
                        continue;
                }
                
                result.Add(target);
                if (result.Count >= maxTargets) break;
            }
            
            return result;
        }
        
        private static List<CrowdAgent> GetChainTargets(
            CrowdAgent caster,
            List<CrowdAgent> targets,
            float range,
            int maxTargets,
            SkillType skillType)
        {
            var result = new List<CrowdAgent>();
            if (caster == null || targets == null) return result;
            
            string casterFaction = caster.enemyCrowd?.factionName ?? "";
            bool targetEnemies = skillType == SkillType.Attack || skillType == SkillType.Debuff;
            
            // Get targeting settings from EnemyCrowd
            var crowd = caster.enemyCrowd;
            float maxHeightDiff = crowd?.maxTargetHeightDifference ?? 0f;
            bool requireLOS = crowd?.requireLineOfSight ?? false;
            LayerMask losLayers = crowd?.lineOfSightBlockingLayers ?? ~0;
            Vector3 casterPos = caster.WorldPosition;
            
            // Find initial target
            CrowdAgent current = null;
            float nearestDist = float.MaxValue;
            
            foreach (var target in targets)
            {
                if (target == null || target == caster) continue;
                
                string targetFaction = target.enemyCrowd?.factionName ?? "";
                if (targetEnemies && !IsEnemy(casterFaction, targetFaction)) continue;
                
                Vector3 targetPos = target.WorldPosition;
                float dist = Vector3.Distance(casterPos, targetPos);
                
                if (dist > range || dist >= nearestDist) continue;
                
                // VERTICAL HEIGHT CHECK
                if (maxHeightDiff > 0f)
                {
                    float heightDiff = Mathf.Abs(targetPos.y - casterPos.y);
                    if (heightDiff > maxHeightDiff)
                        continue;
                }
                
                // LINE OF SIGHT CHECK for initial target
                if (requireLOS && targetEnemies)
                {
                    if (!PassesLOSCheck(caster, casterPos, targetPos, losLayers))
                        continue;
                }
                
                nearestDist = dist;
                current = target;
            }
            
            if (current == null) return result;
            
            result.Add(current);
            
            // Chain to next targets
            while (result.Count < maxTargets)
            {
                CrowdAgent next = null;
                nearestDist = float.MaxValue;
                Vector3 currentPos = current.WorldPosition;
                
                foreach (var target in targets)
                {
                    if (target == null || target == caster || result.Contains(target)) continue;
                    
                    string targetFaction = target.enemyCrowd?.factionName ?? "";
                    if (targetEnemies && !IsEnemy(casterFaction, targetFaction)) continue;
                    
                    Vector3 targetPos = target.WorldPosition;
                    float dist = Vector3.Distance(currentPos, targetPos);
                    float chainRange = range * 0.5f; // Chain range is half initial range
                    
                    if (dist > chainRange || dist >= nearestDist) continue;
                    
                    // VERTICAL HEIGHT CHECK for chain (relative to current chain position)
                    if (maxHeightDiff > 0f)
                    {
                        float heightDiff = Mathf.Abs(targetPos.y - currentPos.y);
                        if (heightDiff > maxHeightDiff)
                            continue;
                    }
                    
                    // LOS not required between chain links (lightning jumps, etc.)
                    
                    nearestDist = dist;
                    next = target;
                }
                
                if (next == null) break;
                
                result.Add(next);
                current = next;
            }
            
            return result;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // COMBAT SYSTEM INTEGRATION
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Result of attempting to use a skill before basic attack.
        /// </summary>
        public enum SkillAttemptResult
        {
            /// <summary>No skill system enabled or no skills configured</summary>
            SkillSystemDisabled,
            /// <summary>A skill was successfully executed instead of basic attack</summary>
            SkillExecuted,
            /// <summary>No skill was ready - proceed with basic attack</summary>
            UseBasicAttack
        }
        
        // Rate limiting tracking
        private static Dictionary<string, int> _skillsThisSecond = new Dictionary<string, int>();
        private static float _lastRateLimitReset = 0f;
        
        /// <summary>
        /// Called by the combat system when an agent is about to basic attack.
        /// Evaluates and potentially executes a skill instead.
        /// 
        /// Integration flow:
        /// 1. Combat system calls TryExecuteSkillBeforeAttack() when agent would attack
        /// 2. If skill is ready and valid → executes skill, returns SkillExecuted
        /// 3. If no skill ready → returns UseBasicAttack, combat system does basic attack
        /// </summary>
        /// <param name="agent">The agent about to attack</param>
        /// <param name="agentState">Per-agent runtime state (managed by FactionSkillManager)</param>
        /// <param name="factionCooldowns">Per-faction cooldowns (managed by FactionSkillManager)</param>
        /// <param name="nearbyAgents">Pre-gathered list of nearby agents for targeting</param>
        /// <param name="controller">The EnemyMassesCrowdController</param>
        /// <returns>SkillAttemptResult indicating what happened</returns>
        public static SkillAttemptResult TryExecuteSkillBeforeAttack(
            CrowdAgent agent,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            List<CrowdAgent> nearbyAgents,
            EnemyMassesCrowdController controller)
        {
            if (agent == null || agent.enemyCrowd == null)
                return SkillAttemptResult.SkillSystemDisabled;
            
            // Agent is mid-cast — no new skills until cast completes or is interrupted
            if (agent.isCasting)
                return SkillAttemptResult.SkillExecuted; // return SkillExecuted to suppress basic attack
            
            // Get skill config from faction
            var skillConfig = agent.enemyCrowd.skillSystemConfig;
            if (skillConfig == null || !skillConfig.enableSkillSystem)
                return SkillAttemptResult.SkillSystemDisabled;
            
            if (skillConfig.skillSlots == null || skillConfig.skillSlots.Count == 0)
                return SkillAttemptResult.SkillSystemDisabled;
            
            float currentTime = Time.time;
            
            // ═══════════════════════════════════════════════════════════════
            // CHECK CONTROL MODE - Manual vs AI
            // ═══════════════════════════════════════════════════════════════
            
            bool useManualControl = ShouldUseManualControl(agent, skillConfig.controlMode);
            
            if (useManualControl)
            {
                // MANUAL MODE: First try any queued (manually triggered) skill
                var manualResult = TryExecuteManualSkill(agent, agentState, factionCooldowns, enemyCrowdCooldowns, nearbyAgents, controller, skillConfig, currentTime);
                if (manualResult == SkillAttemptResult.SkillExecuted)
                    return manualResult;
                
                // Then check if any individual slots are overridden to AIControlled (auto-cast)
                // This enables WC3-style per-skill auto-cast even when the faction is ManualOnly
                if (HasAnyAutocastSlots(skillConfig))
                {
                    return TryExecuteAutocastSlots(agent, agentState, factionCooldowns, enemyCrowdCooldowns, nearbyAgents, controller, skillConfig, currentTime);
                }
                
                return manualResult;
            }
            
            // ═══════════════════════════════════════════════════════════════
            // AI CONTROL - Evaluate and use skills automatically
            // ═══════════════════════════════════════════════════════════════
            
            // Check global cooldown (prevents spam)
            if (skillConfig.globalCooldown > 0 && currentTime < agent.lastSkillUseTime + skillConfig.globalCooldown)
                return SkillAttemptResult.UseBasicAttack;
            
            // Check if it's time for skill evaluation (stagger evaluations for performance)
            if (currentTime < agent.nextSkillEvaluationTime)
                return SkillAttemptResult.UseBasicAttack;
            
            // Apply skill timing variation
            float variation = skillConfig.skillTimingVariation > 0 
                ? UnityEngine.Random.Range(0f, skillConfig.skillDecisionInterval * skillConfig.skillTimingVariation)
                : 0f;
            agent.nextSkillEvaluationTime = currentTime + skillConfig.skillDecisionInterval + variation;
            
            // Skill use chance (prevents 100% skill spam for large armies)
            if (skillConfig.skillUseChance < 1f && UnityEngine.Random.value > skillConfig.skillUseChance)
                return SkillAttemptResult.UseBasicAttack;
            
            // Rate limiting (faction-wide skills per second cap)
            string factionName = agent.enemyCrowd.factionName ?? "default";
            if (!CheckRateLimit(factionName, skillConfig.maxSkillsPerSecond, currentTime))
                return SkillAttemptResult.UseBasicAttack;
            
            // Evaluate best skill
            // Pass the faction-level control mode so EvaluateBestSkill knows whether
            // Hybrid (auto-cast) slots should be included in AI evaluation.
            var bestSlot = EvaluateBestSkill(
                agent,
                skillConfig,
                agentState,
                factionCooldowns,
                enemyCrowdCooldowns,
                nearbyAgents,
                skillConfig.controlMode
            );
            
            // No skill available - use basic attack
            if (bestSlot == null || bestSlot.skill == null)
                return SkillAttemptResult.UseBasicAttack;
            
            // Find targets for the skill
            // For GroundTargetedAOE in AI mode, synthesize the ground target from
            // the nearest enemy position so the AI can auto-cast area skills.
            Vector3? aiGroundTarget = null;
            if (bestSlot.skill.TargetingType == SkillTargetingType.GroundTargetedAOE)
            {
                var nearestEnemy = GetNearestEnemyInRange(
                    agent,
                    nearbyAgents,
                    bestSlot.skill.MinRange,
                    bestSlot.skill.MaxRange,
                    skipFlying: !bestSlot.skill.CanDamageFlying,
                    allowFlyingHeightBypass: bestSlot.skill.CanDamageFlying);
                if (nearestEnemy != null)
                    aiGroundTarget = nearestEnemy.WorldPosition;
                else
                    return SkillAttemptResult.UseBasicAttack; // No enemy to aim at — don't auto-cast into empty ground
            }
            var targets = FindTargets(bestSlot.skill, agent, nearbyAgents, aiGroundTarget);
            if (targets.Count == 0)
                return SkillAttemptResult.UseBasicAttack;
            
            // Execute the skill
            bool success = ExecuteSkill(
                bestSlot,
                agent,
                targets,
                controller,
                agentState,
                factionCooldowns,
                enemyCrowdCooldowns,
                skillConfig.defaultCooldownMode,
                aiGroundTarget
            );
            
            if (success)
            {
                agent.lastSkillUseTime = currentTime;
                IncrementRateLimit(factionName);
            }
            
            return success ? SkillAttemptResult.SkillExecuted : SkillAttemptResult.UseBasicAttack;
        }
        
        /// <summary>
        /// Determines if an agent should use manual skill control.
        /// </summary>
        private static bool ShouldUseManualControl(CrowdAgent agent, SkillControlMode controlMode)
        {
            switch (controlMode)
            {
                case SkillControlMode.AIControlled:
                    return false;
                case SkillControlMode.ManualOnly:
                    return true;
                case SkillControlMode.Hybrid:
                    return agent.useManualSkillControl;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Checks if any skill slots currently have effective auto-cast enabled.
        /// </summary>
        private static bool HasAnyAutocastSlots(SkillSystemConfig config)
        {
            if (config.skillSlots == null) return false;
            foreach (var slot in config.skillSlots)
            {
                if (IsSlotAutocastEnabled(slot))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves whether a slot is currently auto-cast enabled.
        /// - NotEligible: never auto-cast.
        /// - Override ManualOnly: forced OFF.
        /// - Override non-ManualOnly (Hybrid/AIControlled): forced ON.
        /// - No override: use skill default (DefaultOn = ON, DefaultOff = OFF).
        /// </summary>
        private static bool IsSlotAutocastEnabled(AgentSkillSlot slot)
        {
            if (slot?.skill == null || !slot.enabled)
                return false;

            if (slot.skill.Autocast == AutocastEligibility.NotEligible)
                return false;

            if (slot.overrideControlMode)
                return slot.controlModeOverride != SkillControlMode.ManualOnly;

            return slot.skill.Autocast == AutocastEligibility.DefaultOn;
        }
        
        /// <summary>
        /// Attempts to execute an auto-cast skill from slots whose effective auto-cast
        /// state is currently ON.
        /// Used when the faction-wide mode is ManualOnly but individual skills can still
        /// be auto-cast.
        /// </summary>
        private static SkillAttemptResult TryExecuteAutocastSlots(
            CrowdAgent agent,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            List<CrowdAgent> nearbyAgents,
            EnemyMassesCrowdController controller,
            SkillSystemConfig skillConfig,
            float currentTime)
        {
            // Check global cooldown
            if (skillConfig.globalCooldown > 0 && currentTime < agent.lastSkillUseTime + skillConfig.globalCooldown)
                return SkillAttemptResult.UseBasicAttack;
            
            // Check skill evaluation timing
            if (currentTime < agent.nextSkillEvaluationTime)
                return SkillAttemptResult.UseBasicAttack;
            
            // Apply timing variation
            float variation = skillConfig.skillTimingVariation > 0 
                ? UnityEngine.Random.Range(0f, skillConfig.skillDecisionInterval * skillConfig.skillTimingVariation)
                : 0f;
            agent.nextSkillEvaluationTime = currentTime + skillConfig.skillDecisionInterval + variation;
            
            // Skill use chance
            if (skillConfig.skillUseChance < 1f && UnityEngine.Random.value > skillConfig.skillUseChance)
                return SkillAttemptResult.UseBasicAttack;
            
            // Rate limiting
            string factionName = agent.enemyCrowd.factionName ?? "default";
            if (!CheckRateLimit(factionName, skillConfig.maxSkillsPerSecond, currentTime))
                return SkillAttemptResult.UseBasicAttack;
            
            // Evaluate only slots whose effective auto-cast state is ON.
            AgentSkillSlot bestSlot = null;
            int highestPriority = int.MinValue;
            
            foreach (var slot in skillConfig.skillSlots)
            {
                if (slot?.skill == null || !slot.enabled) continue;
                
                if (!IsSlotAutocastEnabled(slot))
                    continue;
                
                if (IsSkillOnCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, skillConfig.defaultCooldownMode))
                    continue;
                
                // Check resource cost (mana, energy, etc.)
                if (slot.skill.HasResourceCost && !SkillResourceManager.Instance.HasResource(agent, slot.skill))
                    continue;
                
                if (!CheckAIConditions(slot.skill, agent, nearbyAgents))
                    continue;
                
                if (!HasValidTarget(slot.skill, agent, nearbyAgents))
                    continue;
                
                int priority = slot.overridePriority ? slot.priorityOverride : slot.skill.Priority;
                if (priority > highestPriority)
                {
                    highestPriority = priority;
                    bestSlot = slot;
                }
            }
            
            if (bestSlot == null || bestSlot.skill == null)
                return SkillAttemptResult.UseBasicAttack;
            
            // For GroundTargetedAOE, synthesize the ground target from the nearest
            // enemy position so auto-cast can fire area skills (same as the full AI path).
            Vector3? aiGroundTarget = null;
            if (bestSlot.skill.TargetingType == SkillTargetingType.GroundTargetedAOE)
            {
                var nearestEnemy = GetNearestEnemyInRange(
                    agent,
                    nearbyAgents,
                    bestSlot.skill.MinRange,
                    bestSlot.skill.MaxRange,
                    skipFlying: !bestSlot.skill.CanDamageFlying,
                    allowFlyingHeightBypass: bestSlot.skill.CanDamageFlying);
                if (nearestEnemy != null)
                    aiGroundTarget = nearestEnemy.WorldPosition;
                else
                    return SkillAttemptResult.UseBasicAttack; // No enemy to aim at — don't auto-cast into empty ground
            }

            // Find targets
            var targets = FindTargets(bestSlot.skill, agent, nearbyAgents, aiGroundTarget);
            if (targets.Count == 0)
                return SkillAttemptResult.UseBasicAttack;
            
            // Execute
            bool success = ExecuteSkill(
                bestSlot, agent, targets, controller,
                agentState, factionCooldowns, enemyCrowdCooldowns,
                skillConfig.defaultCooldownMode,
                aiGroundTarget);
            
            if (success)
            {
                agent.lastSkillUseTime = currentTime;
                IncrementRateLimit(factionName);
            }
            
            return success ? SkillAttemptResult.SkillExecuted : SkillAttemptResult.UseBasicAttack;
        }
        
        /// <summary>
        /// Executes a manually queued skill for the agent.
        /// </summary>
        private static SkillAttemptResult TryExecuteManualSkill(
            CrowdAgent agent,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            List<CrowdAgent> nearbyAgents,
            EnemyMassesCrowdController controller,
            SkillSystemConfig skillConfig,
            float currentTime)
        {
            // Check if a skill is queued
            if (agent.queuedSkillSlot < 0 || agent.queuedSkillSlot >= skillConfig.skillSlots.Count)
                return SkillAttemptResult.UseBasicAttack; // No skill queued, use basic attack
            
            var slot = skillConfig.skillSlots[agent.queuedSkillSlot];
            if (slot?.skill == null || !slot.enabled)
            {
                agent.queuedSkillSlot = -1; // Clear invalid queue
                return SkillAttemptResult.UseBasicAttack;
            }
            
            // Check cooldown
            if (IsSkillOnCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, skillConfig.defaultCooldownMode))
                return SkillAttemptResult.UseBasicAttack;
            
            // Check global cooldown
            if (skillConfig.globalCooldown > 0 && currentTime < agent.lastSkillUseTime + skillConfig.globalCooldown)
                return SkillAttemptResult.UseBasicAttack;
            
            // Find targets
            List<CrowdAgent> targets;
            if (agent.queuedSkillTargetIndex >= 0)
            {
                // Use the explicitly specified target
                var targetAgent = FindAgentByIndex(nearbyAgents, agent.queuedSkillTargetIndex);
                if (targetAgent != null && IsValidTarget(slot.skill, agent, targetAgent))
                {
                    targets = new List<CrowdAgent> { targetAgent };
                }
                else
                {
                    // Invalid target, fall back to auto-targeting
                    targets = FindTargets(slot.skill, agent, nearbyAgents, agent.queuedSkillGroundTarget);
                }
            }
            else
            {
                targets = FindTargets(slot.skill, agent, nearbyAgents, agent.queuedSkillGroundTarget);
            }
            
            // GroundTargetedAOE can fire on empty ground; other types need targets.
            // Also allow firing with no agent targets if a damageable target is queued.
            if (targets.Count == 0 && agent.queuedSkillDamageableTarget == null
                && slot.skill.TargetingType != SkillTargetingType.GroundTargetedAOE)
                return SkillAttemptResult.UseBasicAttack;
            
            // Execute the skill — routes through INetworkSkillAuthority in multiplayer
            bool success = RequestSkillExecution(
                slot,
                agent.queuedSkillSlot,
                agent,
                targets,
                controller,
                agentState,
                factionCooldowns,
                enemyCrowdCooldowns,
                skillConfig.defaultCooldownMode,
                agent.queuedSkillGroundTarget,
                agent.ownerPlayerId,
                agent.queuedSkillDamageableTarget
            );
            
            // Clear queue on success
            if (success)
            {
                agent.queuedSkillSlot = -1;
                agent.queuedSkillTargetIndex = -1;
                agent.queuedSkillGroundTarget = null;
                agent.queuedSkillDamageableTarget = null;
                agent.lastSkillUseTime = currentTime;
            }
            
            return success ? SkillAttemptResult.SkillExecuted : SkillAttemptResult.UseBasicAttack;
        }
        
        /// <summary>
        /// Finds an agent by index from the nearby agents list.
        /// </summary>
        private static CrowdAgent FindAgentByIndex(List<CrowdAgent> agents, int index)
        {
            foreach (var agent in agents)
            {
                if (agent != null && agent.index == index)
                    return agent;
            }
            return null;
        }
        
        /// <summary>
        /// Checks if we're within the rate limit for skills per second.
        /// </summary>
        private static bool CheckRateLimit(string factionName, int maxSkillsPerSecond, float currentTime)
        {
            if (maxSkillsPerSecond <= 0) return true; // No limit
            
            // Reset counts every second
            if (currentTime - _lastRateLimitReset >= 1f)
            {
                _skillsThisSecond.Clear();
                _lastRateLimitReset = currentTime;
            }
            
            _skillsThisSecond.TryGetValue(factionName, out int count);
            return count < maxSkillsPerSecond;
        }
        
        /// <summary>
        /// Increments the rate limit counter for a faction.
        /// </summary>
        private static void IncrementRateLimit(string factionName)
        {
            if (_skillsThisSecond.TryGetValue(factionName, out int count))
            {
                _skillsThisSecond[factionName] = count + 1;
            }
            else
            {
                _skillsThisSecond[factionName] = 1;
            }
        }
        
        /// <summary>
        /// Checks if skills are enabled for an agent's faction.
        /// Used by the combat system to decide whether to check for skills.
        /// </summary>
        public static bool AreSkillsEnabled(CrowdAgent agent)
        {
            if (agent == null || agent.enemyCrowd == null)
                return false;
            
            var config = agent.enemyCrowd.skillSystemConfig;
            return config != null && config.enableSkillSystem && 
                   config.skillSlots != null && config.skillSlots.Count > 0;
        }
        
        #region ═══════════════════════════════════════════════════════════════
        // FACTION RELATIONSHIP CHECKS
        #endregion ════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Checks if two agents are enemies.
        /// Uses the FactionSystemManager if available, otherwise falls back to faction name comparison.
        /// </summary>
        private static bool IsEnemy(string factionA, string factionB)
        {
            if (string.IsNullOrEmpty(factionA) || string.IsNullOrEmpty(factionB))
                return factionA != factionB; // Different factions = enemies by default
            
            // Fallback: different factions are enemies
            return factionA != factionB;
        }
        
        /// <summary>
        /// Checks if two agents are enemies using the faction system.
        /// </summary>
        private static bool IsEnemy(CrowdAgent agentA, CrowdAgent agentB)
        {
            if (agentA == null || agentB == null)
                return false;
                
            // Use the faction system if available
            var factionSystem = FactionSystemManager.Instance;
            if (factionSystem != null)
            {
                return factionSystem.GetRelationship(agentA, agentB) == RelationshipType.Enemy;
            }
            
            // Fallback to faction name comparison
            string factionA = agentA.enemyCrowd?.factionName ?? "";
            string factionB = agentB.enemyCrowd?.factionName ?? "";
            return factionA != factionB;
        }
        
        /// <summary>
        /// Checks if two agents are allies.
        /// Uses the FactionSystemManager if available, otherwise falls back to faction name comparison.
        /// </summary>
        private static bool IsAlly(string factionA, string factionB)
        {
            if (string.IsNullOrEmpty(factionA) || string.IsNullOrEmpty(factionB))
                return factionA == factionB; // Same faction = allies
            
            // Fallback: same faction = allies
            return factionA == factionB;
        }
        
        /// <summary>
        /// Checks if two agents are allies using the faction system.
        /// </summary>
        private static bool IsAlly(CrowdAgent agentA, CrowdAgent agentB)
        {
            if (agentA == null || agentB == null)
                return false;
                
            // Same agent is ally with self
            if (agentA == agentB)
                return true;
                
            // Use the faction system if available
            var factionSystem = FactionSystemManager.Instance;
            if (factionSystem != null)
            {
                var relationship = factionSystem.GetRelationship(agentA, agentB);
                return relationship == RelationshipType.Ally;
            }
            
            // Fallback to faction name comparison
            string factionA = agentA.enemyCrowd?.factionName ?? "";
            string factionB = agentB.enemyCrowd?.factionName ?? "";
            return factionA == factionB;
        }
    }
    
    /// <summary>
    /// Helper component to auto-destroy VFX and update budget counter.
    /// </summary>
    public class VFXAutoDestroy : MonoBehaviour
    {
        private AgentSkill _skill;
        private System.Action _onDestroy;
        private float _lifetime = 5f;
        
        public void Initialize(AgentSkill skill, System.Action onDestroy, float lifetime = 5f)
        {
            _skill = skill;
            _onDestroy = onDestroy;
            _lifetime = lifetime;
            
            // Try to get lifetime from particle system
            var ps = GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                if (!main.loop)
                {
                    _lifetime = main.duration + main.startLifetime.constantMax;
                }
            }
            
            Destroy(gameObject, _lifetime);
        }
        
        private void OnDestroy()
        {
            _onDestroy?.Invoke();
        }
    }
}
