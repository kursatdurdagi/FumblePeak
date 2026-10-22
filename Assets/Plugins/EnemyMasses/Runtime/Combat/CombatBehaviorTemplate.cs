namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Determines how ranged attacks deal damage.
    /// </summary>
    public enum RangedAttackMode
    {
        /// <summary>
        /// Instant damage via raycast - ideal for guns, lasers, instant magic.
        /// Most performant option as it requires no projectile simulation.
        /// </summary>
        Hitscan,
        
        /// <summary>
        /// Spawns a visual projectile that travels to target - ideal for arrows, bolts, thrown weapons.
        /// Uses linear trajectory (straight line).
        /// </summary>
        Projectile,
        
        /// <summary>
        /// Spawns a projectile with parabolic arc - ideal for grenades, catapults, thrown bombs.
        /// Can have Area of Effect (AOE) damage on impact.
        /// </summary>
        ProjectileArc,

        /// <summary>
        /// Disables legacy ranged auto-attacks. Use Agent Skills for ranged damage instead.
        /// Useful for Hybrid units where melee uses basic attacks but ranged uses skills.
        /// </summary>
        [UnityEngine.InspectorName("Disabled (Use Skills)")]
        DisabledUseSkills
    }
    
    /// <summary>
    /// Pre-configured combat behavior templates that automatically set up all related combat settings.
    /// This prevents conflicting configurations and simplifies the inspector.
    /// </summary>
    public enum CombatBehaviorTemplate
    {
        /// <summary>
        /// Aggressive melee fighters that swarm the target.
        /// All rush in simultaneously - Vampire Survivors style.
        /// Settings: Melee range, Swarm positioning, no concurrent limit.
        /// </summary>
        MeleeSwarm,
        
        /// <summary>
        /// Disciplined melee fighters that form a circle and take turns attacking.
        /// Batman/Spider-Man style combat with attack queuing.
        /// Settings: Melee range, QueuedCircle positioning, 3-5 concurrent attackers.
        /// </summary>
        MeleeQueued,
        
        /// <summary>
        /// Military-style melee formation with battle lines.
        /// Agents form ranks and advance together - great for faction battles.
        /// Settings: Melee range, BattleLine positioning, formation-based attacks.
        /// </summary>
        MeleeBattleLine,
        
        /// <summary>
        /// Ranged attackers that maintain distance and fire volleys.
        /// Archers, gunners, mages that prefer to stay back.
        /// Settings: Ranged only, SimpleApproach, maintains preferred distance.
        /// </summary>
        RangedStationary,
        
        /// <summary>
        /// Ranged attackers in military formation.
        /// Battle line of archers/gunners that fire while maintaining formation.
        /// Settings: Ranged only, BattleLine positioning, coordinated volleys.
        /// </summary>
        RangedBattleLine,
        
        /// <summary>
        /// Flexible fighters that switch between ranged and melee.
        /// Attacks from range, switches to melee when enemies close in.
        /// Settings: Hybrid range, Staggered positioning, adaptive behavior.
        /// </summary>
        HybridAdaptive,
        
        /// <summary>
        /// Wave-based organized attack groups.
        /// Agents divided into waves that take turns attacking - Diablo 4 style.
        /// Settings: Melee range, OrganizedSwarm, wave rotation.
        /// </summary>
        MeleeWaves,
        
        /// <summary>
        /// Use custom settings - shows all individual options in inspector.
        /// For advanced users who need fine-grained control.
        /// </summary>
        Custom
    }
    
    /// <summary>
    /// Static helper class to apply combat behavior templates to EnemyCrowd settings.
    /// </summary>
    public static class CombatBehaviorTemplateHelper
    {
        /// <summary>
        /// Applies a combat behavior template to an EnemyCrowd, auto-configuring all related settings.
        /// </summary>
        public static void ApplyTemplate(EnemyCrowd crowd, CombatBehaviorTemplate template)
        {
            if (crowd == null) return;
            
            UnityEngine.Debug.Log($"[TemplateDebug] Applying template {template} to faction {crowd.factionName}");
            
            switch (template)
            {
                case CombatBehaviorTemplate.MeleeSwarm:
                    ApplyMeleeSwarm(crowd);
                    UnityEngine.Debug.Log($"[TemplateDebug] After MeleeSwarm: positioningMode={crowd.positioningMode}, combatRangeType={crowd.combatRangeType}");
                    break;
                case CombatBehaviorTemplate.MeleeQueued:
                    ApplyMeleeQueued(crowd);
                    break;
                case CombatBehaviorTemplate.MeleeBattleLine:
                    ApplyMeleeBattleLine(crowd);
                    break;
                case CombatBehaviorTemplate.RangedStationary:
                    ApplyRangedStationary(crowd);
                    break;
                case CombatBehaviorTemplate.RangedBattleLine:
                    ApplyRangedBattleLine(crowd);
                    break;
                case CombatBehaviorTemplate.HybridAdaptive:
                    ApplyHybridAdaptive(crowd);
                    break;
                case CombatBehaviorTemplate.MeleeWaves:
                    ApplyMeleeWaves(crowd);
                    break;
                case CombatBehaviorTemplate.Custom:
                    // Don't change anything - user wants manual control
                    // But still validate for critical conflicts
                    ValidateAndFixCriticalConflicts(crowd);
                    break;
            }
        }
        
        /// <summary>
        /// Validates and auto-fixes critical conflicts in combat settings.
        /// Called for Custom templates and can be called externally for validation.
        /// </summary>
        public static void ValidateAndFixCriticalConflicts(EnemyCrowd crowd)
        {
            // Fix ranged-specific conflicts
            if (crowd.combatRangeType == CombatRangeType.Ranged)
            {
                // Ranged units shouldn't have melee-style flanking
                if (crowd.positioningMode == CombatPositioningMode.BattleLine)
                {
                    if (crowd.battleLineFlankingRatio > 0f)
                    {
                        UnityEngine.Debug.LogWarning($"[EnemyMasses] Auto-corrected: Ranged battle line shouldn't use flanking. Set to 0.");
                        crowd.battleLineFlankingRatio = 0f;
                        crowd.battleLineFlankingWidth = 0f;
                    }
                    
                    // Ranged battle lines should hold position, not auto-advance
                    if (crowd.battleLineAutoAdvance)
                    {
                        UnityEngine.Debug.LogWarning($"[EnemyMasses] Auto-corrected: Ranged battle line should hold position. Disabled auto-advance.");
                        crowd.battleLineAutoAdvance = false;
                    }
                    
                    // Ensure engagement distance matches ranged preferred distance
                    if (crowd.battleLineEngagementDistance < crowd.rangedMinDistance)
                    {
                        UnityEngine.Debug.LogWarning($"[EnemyMasses] Auto-corrected: Battle line engagement distance was less than ranged min. Set to ranged preferred.");
                        crowd.battleLineEngagementDistance = crowd.rangedPreferredDistance;
                    }
                }
            }
            
            // Fix melee-specific conflicts
            if (crowd.combatRangeType == CombatRangeType.Melee)
            {
                if (crowd.positioningMode == CombatPositioningMode.BattleLine)
                {
                    // Melee battle lines need reasonable engagement distance
                    if (crowd.battleLineEngagementDistance > 5f)
                    {
                        UnityEngine.Debug.LogWarning($"[EnemyMasses] Auto-corrected: Melee battle line engagement distance was too large. Set to melee range.");
                        crowd.battleLineEngagementDistance = crowd.meleeMaxDistance;
                    }
                }
            }
            
            // Fix general positioning conflicts
            if (crowd.positioningMode != CombatPositioningMode.BattleLine)
            {
                // Non-battle-line modes shouldn't use battle line engagement distance
                // (The system should ignore it, but this keeps settings clean)
            }
        }
        
        /// <summary>
        /// Gets a list of current setting conflicts without auto-fixing.
        /// Useful for editor warnings.
        /// </summary>
        public static System.Collections.Generic.List<string> GetSettingConflicts(EnemyCrowd crowd)
        {
            var conflicts = new System.Collections.Generic.List<string>();
            
            if (crowd.combatRangeType == CombatRangeType.Ranged)
            {
                if (crowd.positioningMode == CombatPositioningMode.BattleLine)
                {
                    if (crowd.battleLineFlankingRatio > 0f)
                        conflicts.Add("Ranged battle line has flanking enabled - ranged units shouldn't flank");
                    
                    if (crowd.battleLineAutoAdvance)
                        conflicts.Add("Ranged battle line has auto-advance enabled - ranged units should hold position");
                    
                    if (crowd.battleLineEngagementDistance < crowd.rangedMinDistance)
                        conflicts.Add($"Battle line engagement ({crowd.battleLineEngagementDistance}m) is less than ranged min ({crowd.rangedMinDistance}m)");
                }
            }
            
            if (crowd.combatRangeType == CombatRangeType.Melee)
            {
                if (crowd.positioningMode == CombatPositioningMode.BattleLine)
                {
                    if (crowd.battleLineEngagementDistance > 5f)
                        conflicts.Add($"Melee battle line engagement distance ({crowd.battleLineEngagementDistance}m) is too far for melee");
                }
            }
            
            return conflicts;
        }
        
        /// <summary>
        /// Checks if the crowd's current settings match what the specified template would set.
        /// Used to detect when a template was selected but never actually applied.
        /// </summary>
        public static bool DoSettingsMatchTemplate(EnemyCrowd crowd, CombatBehaviorTemplate template)
        {
            if (crowd == null) return true;
            if (template == CombatBehaviorTemplate.Custom) return true; // Custom always "matches"
            
            // Check the critical settings that define each template
            switch (template)
            {
                case CombatBehaviorTemplate.MeleeSwarm:
                    return crowd.combatRangeType == CombatRangeType.Melee &&
                           crowd.positioningMode == CombatPositioningMode.Swarm;
                           
                case CombatBehaviorTemplate.MeleeQueued:
                    return crowd.combatRangeType == CombatRangeType.Melee &&
                           crowd.positioningMode == CombatPositioningMode.QueuedCircle;
                           
                case CombatBehaviorTemplate.MeleeBattleLine:
                    return crowd.combatRangeType == CombatRangeType.Melee &&
                           crowd.positioningMode == CombatPositioningMode.BattleLine;
                           
                case CombatBehaviorTemplate.RangedStationary:
                    return crowd.combatRangeType == CombatRangeType.Ranged &&
                           crowd.positioningMode == CombatPositioningMode.SimpleApproach;
                           
                case CombatBehaviorTemplate.RangedBattleLine:
                    return crowd.combatRangeType == CombatRangeType.Ranged &&
                           crowd.positioningMode == CombatPositioningMode.BattleLine;
                           
                case CombatBehaviorTemplate.HybridAdaptive:
                    return crowd.combatRangeType == CombatRangeType.Hybrid &&
                           crowd.positioningMode == CombatPositioningMode.Staggered;
                           
                case CombatBehaviorTemplate.MeleeWaves:
                    return crowd.combatRangeType == CombatRangeType.Melee &&
                           crowd.positioningMode == CombatPositioningMode.OrganizedSwarm;
                           
                default:
                    return true;
            }
        }
        
        private static void ApplyMeleeSwarm(EnemyCrowd crowd)
        {
            // Combat range
            crowd.combatRangeType = CombatRangeType.Melee;
            crowd.meleeMinDistance = 0.8f;
            crowd.meleeMaxDistance = 1.8f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.Swarm;
            crowd.formationRadius = 3f;
            crowd.maxConcurrentAttackers = 0; // Unlimited
            crowd.attackSlotDuration = 0f;
            crowd.allowOpportunisticAttacks = true;
            crowd.agentSpacing = 0.5f; // Tight
            
            // CRITICAL: Disable formation reformation for swarm - agents should rush directly
            crowd.enableFormationReformation = false;
            
            // Ally Defense - critical for melee vs ranged survival
            crowd.enableAllyDefense = true;
            crowd.allyDefenseRadius = 15f; // Smaller for swarm - local response
            crowd.allyDefenseTargetAttacker = true;
            crowd.allyDefenseMaxResponders = 6;
            crowd.allyDefenseAlertCooldown = 0.5f; // Fast response
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = false;
            crowd.stopMovementWhenClose = false;
            crowd.lockMovementDuringAttacks = false;
            crowd.enablePostAttackRetreat = false;
            crowd.closeRangeStopBuffer = 0.2f;
            crowd.combatFacingSpeed = 720f;
        }
        
        private static void ApplyMeleeQueued(EnemyCrowd crowd)
        {
            // Combat range
            crowd.combatRangeType = CombatRangeType.Melee;
            crowd.meleeMinDistance = 1.0f;
            crowd.meleeMaxDistance = 2.0f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.QueuedCircle;
            crowd.formationRadius = 4f;
            crowd.maxConcurrentAttackers = 4;
            crowd.attackSlotDuration = 6f;
            crowd.allowOpportunisticAttacks = true;
            crowd.minAngleBetweenEnemies = 30f;
            crowd.agentSpacing = 1.0f;
            
            // Disable formation reformation - queued circle has its own positioning
            crowd.enableFormationReformation = false;
            
            // Ally Defense - critical for melee vs ranged survival
            crowd.enableAllyDefense = true;
            crowd.allyDefenseRadius = 12f; // Smaller for queued - ring focuses attack
            crowd.allyDefenseTargetAttacker = true;
            crowd.allyDefenseMaxResponders = 4; // Match concurrent attackers
            crowd.allyDefenseAlertCooldown = 1f;
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = true;
            crowd.stopMovementWhenClose = true;
            crowd.lockMovementDuringAttacks = true;
            crowd.enablePostAttackRetreat = true;
            crowd.postAttackRetreatDistance = new UnityEngine.Vector2(0.5f, 1.2f);
            crowd.closeRangeStopBuffer = 0.35f;
            crowd.combatFacingSpeed = 720f;
        }
        
        private static void ApplyMeleeBattleLine(EnemyCrowd crowd)
        {
            // Combat range
            crowd.combatRangeType = CombatRangeType.Melee;
            crowd.meleeMinDistance = 1.0f;
            crowd.meleeMaxDistance = 2.5f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.BattleLine;
            crowd.formationRadius = 8f;
            crowd.maxConcurrentAttackers = 0; // Battle line handles this
            crowd.attackSlotDuration = 0f;
            crowd.allowOpportunisticAttacks = true;
            crowd.agentSpacing = 0f; // Battle line handles spacing
            
            // Battle line specific
            crowd.battleLineSpacing = 1.2f;
            crowd.battleLineRankDepth = 2f;
            crowd.battleLineAgentsPerRank = 15;
            crowd.battleLineFlankingRatio = 0.15f;
            crowd.battleLineFlankingWidth = 12f;
            crowd.battleLineEngagementDistance = 2f;
            crowd.battleLineAutoAdvance = true;
            crowd.battleLineAdvanceSpeed = 0.5f;
            
            // Battle line manages its own formation - disable reformation system
            crowd.enableFormationReformation = false;
            
            // Ally Defense - CRITICAL for battle line survival against ranged
            // Large radius to alert entire formation when front line is hit
            crowd.enableAllyDefense = true;
            crowd.allyDefenseRadius = 25f; // Large - alert entire formation
            crowd.allyDefenseTargetAttacker = true;
            crowd.allyDefenseMaxResponders = 12; // Significant response
            crowd.allyDefenseAlertCooldown = 0.5f; // Fast response
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = true;
            crowd.stopMovementWhenClose = false; // Battle line manages movement
            crowd.lockMovementDuringAttacks = false;
            crowd.enablePostAttackRetreat = false;
            crowd.closeRangeStopBuffer = 0.3f;
            crowd.combatFacingSpeed = 360f;
        }
        
        private static void ApplyRangedStationary(EnemyCrowd crowd)
        {
            // Combat range
            crowd.combatRangeType = CombatRangeType.Ranged;
            crowd.rangedMinDistance = 5f;
            crowd.rangedMaxDistance = 25f;
            crowd.rangedPreferredDistance = 12f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.SimpleApproach;
            crowd.formationRadius = 12f;
            crowd.maxConcurrentAttackers = 0; // All can fire
            crowd.attackSlotDuration = 0f;
            crowd.allowOpportunisticAttacks = true;
            crowd.agentSpacing = 1.5f;
            
            // Disable formation reformation - ranged units hold position
            crowd.enableFormationReformation = false;
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = true;
            crowd.stopMovementWhenClose = true;
            crowd.lockMovementDuringAttacks = false; // Can reposition while shooting
            crowd.enablePostAttackRetreat = false;
            crowd.closeRangeStopBuffer = 0f;
            crowd.combatFacingSpeed = 360f;
        }
        
        private static void ApplyRangedBattleLine(EnemyCrowd crowd)
        {
            // Combat range
            crowd.combatRangeType = CombatRangeType.Ranged;
            crowd.rangedMinDistance = 8f;
            crowd.rangedMaxDistance = 30f;
            crowd.rangedPreferredDistance = 15f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.BattleLine;
            crowd.formationRadius = 15f;
            crowd.maxConcurrentAttackers = 0;
            crowd.attackSlotDuration = 0f;
            crowd.allowOpportunisticAttacks = true;
            crowd.agentSpacing = 0f;
            
            // Battle line specific - ranged configuration
            crowd.battleLineSpacing = 1.5f;
            crowd.battleLineRankDepth = 1.5f;
            crowd.battleLineAgentsPerRank = 20;
            crowd.battleLineFlankingRatio = 0f; // Ranged doesn't flank
            crowd.battleLineFlankingWidth = 0f;
            crowd.battleLineEngagementDistance = 15f; // Stay at range
            crowd.battleLineAutoAdvance = false; // Hold position
            crowd.battleLineAdvanceSpeed = 0f;
            
            // Battle line manages its own formation - disable reformation system
            crowd.enableFormationReformation = false;
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = true;
            crowd.stopMovementWhenClose = true;
            crowd.lockMovementDuringAttacks = false;
            crowd.enablePostAttackRetreat = false;
            crowd.closeRangeStopBuffer = 0f;
            crowd.combatFacingSpeed = 360f;
        }
        
        private static void ApplyHybridAdaptive(EnemyCrowd crowd)
        {
            // Combat range - both melee and ranged
            crowd.combatRangeType = CombatRangeType.Hybrid;
            crowd.meleeMinDistance = 1.0f;
            crowd.meleeMaxDistance = 2.5f;
            crowd.rangedMinDistance = 4f;
            crowd.rangedMaxDistance = 18f;
            crowd.rangedPreferredDistance = 10f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.Staggered;
            crowd.formationRadius = 6f;
            crowd.maxConcurrentAttackers = 6;
            crowd.attackSlotDuration = 4f;
            crowd.allowOpportunisticAttacks = true;
            crowd.minAngleBetweenEnemies = 25f;
            crowd.agentSpacing = 1.2f;
            
            // Disable formation reformation - combat positioning handles everything
            crowd.enableFormationReformation = false;
            
            // Ally Defense - moderate response, can handle threats at range
            crowd.enableAllyDefense = true;
            crowd.allyDefenseRadius = 15f; // Medium radius
            crowd.allyDefenseTargetAttacker = true;
            crowd.allyDefenseMaxResponders = 4; // Limited response - they can shoot back
            crowd.allyDefenseAlertCooldown = 1.5f; // Slower cooldown
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = true;
            crowd.stopMovementWhenClose = true;
            crowd.lockMovementDuringAttacks = true;
            crowd.enablePostAttackRetreat = true;
            crowd.postAttackRetreatDistance = new UnityEngine.Vector2(0.8f, 1.5f);
            crowd.closeRangeStopBuffer = 0.3f;
            crowd.combatFacingSpeed = 540f;
        }
        
        private static void ApplyMeleeWaves(EnemyCrowd crowd)
        {
            // Combat range
            crowd.combatRangeType = CombatRangeType.Melee;
            crowd.meleeMinDistance = 0.8f;
            crowd.meleeMaxDistance = 2.0f;
            
            // Positioning
            crowd.positioningMode = CombatPositioningMode.OrganizedSwarm;
            crowd.formationRadius = 5f;
            crowd.maxConcurrentAttackers = 0; // Wave controls this
            crowd.attackSlotDuration = 0f;
            crowd.allowOpportunisticAttacks = true;
            crowd.agentSpacing = 0.6f;
            
            // Disable formation reformation - waves have their own positioning logic
            crowd.enableFormationReformation = false;
            crowd.agentSpacing = 0.6f;
            
            // Wave specific
            crowd.waveGroupCount = 4;
            crowd.waveAttackDuration = 5f;
            crowd.waveCooldown = 1.5f;
            crowd.waveWaitingDistance = 8f;
            crowd.reactiveDefense = true; // Individual reactive defense
            
            // Ally Defense - allows entire wave groups to respond
            crowd.enableAllyDefense = true;
            crowd.allyDefenseRadius = 20f; // Match waiting distance
            crowd.allyDefenseTargetAttacker = true;
            crowd.allyDefenseMaxResponders = 10; // A whole wave group
            crowd.allyDefenseAlertCooldown = 0.75f;
            
            // Behavior
            crowd.maintainFacingBetweenAttacks = true;
            crowd.stopMovementWhenClose = false;
            crowd.lockMovementDuringAttacks = false;
            crowd.enablePostAttackRetreat = false;
            crowd.closeRangeStopBuffer = 0.25f;
            crowd.combatFacingSpeed = 720f;
        }
        
        /// <summary>
        /// Gets a user-friendly description of what a template does.
        /// </summary>
        public static string GetTemplateDescription(CombatBehaviorTemplate template)
        {
            return template switch
            {
                CombatBehaviorTemplate.MeleeSwarm => 
                    "Aggressive melee swarm - all rush in (Vampire Survivors style)",
                CombatBehaviorTemplate.MeleeQueued => 
                    "Disciplined melee circle - take turns attacking (Batman style)",
                CombatBehaviorTemplate.MeleeBattleLine => 
                    "Military battle lines - ranks advance together (Total War style)",
                CombatBehaviorTemplate.RangedStationary => 
                    "Ranged attackers - maintain distance, fire at will",
                CombatBehaviorTemplate.RangedBattleLine => 
                    "Ranged battle line - coordinated volleys in formation",
                CombatBehaviorTemplate.HybridAdaptive => 
                    "Adaptive fighters - ranged at distance, melee up close",
                CombatBehaviorTemplate.MeleeWaves => 
                    "Wave attacks - groups take turns swarming (Diablo 4 style)",
                CombatBehaviorTemplate.Custom => 
                    "Manual configuration - all settings exposed",
                _ => "Unknown template"
            };
        }
    }
}
