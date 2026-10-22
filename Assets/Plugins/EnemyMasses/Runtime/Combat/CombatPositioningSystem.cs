using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages combat positioning, formation slots, and attack queue coordination.
    /// Prevents enemies from clumping and controls who can attack simultaneously.
    /// </summary>
    public class CombatPositioningSystem
    {
        private readonly CombatSettings _settings;
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        
        // Attack queue management
        private readonly HashSet<int> _activeAttackers = new HashSet<int>();
        private readonly Queue<int> _attackQueue = new Queue<int>();
        
        // Formation slot management (for FormationSlots mode)
        private readonly Dictionary<int, int> _agentToSlotIndex = new Dictionary<int, int>();
        private readonly HashSet<int> _occupiedSlots = new HashSet<int>();
        
        // Organized Swarm wave management (per faction)
        private readonly Dictionary<int, int> _factionActiveWaveIndex = new Dictionary<int, int>(); // factionIndex -> current active wave
        private readonly Dictionary<int, float> _factionWaveStartTime = new Dictionary<int, float>(); // factionIndex -> when current wave started
        private readonly Dictionary<int, bool> _factionWaveAssigned = new Dictionary<int, bool>(); // factionIndex -> whether waves have been assigned
        
        // Hybrid combat AI decision system
        private readonly HybridCombatDecisionSystem _hybridDecisionSystem;
        
        public CombatPositioningSystem(CombatSettings settings, List<CrowdAgent> agents, List<EnemyCrowd> enemyCrowds)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
            _enemyCrowds = enemyCrowds ?? throw new System.ArgumentNullException(nameof(enemyCrowds));
            _hybridDecisionSystem = new HybridCombatDecisionSystem();
        }
        
        /// <summary>
        /// Recalculates formation slots for a specific faction.
        /// </summary>
        private int CalculateFormationSlotsForFaction(EnemyCrowd faction)
        {
            if (faction == null)
                return 12; // Default
                
            float formationRadius = faction.formationRadius;
            float minAngle = faction.minAngleBetweenEnemies;
            float circumference = 2f * Mathf.PI * formationRadius;
            float minSpacing = formationRadius * Mathf.Tan(minAngle * Mathf.Deg2Rad);
            return Mathf.Max(3, Mathf.FloorToInt(circumference / minSpacing));
        }
        
        /// <summary>
        /// Calculates the ideal position for an agent based on their faction's positioning mode.
        /// </summary>
        public Vector3 GetCombatPosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex, out bool shouldStop)
        {
            shouldStop = false;
            
            EnemyCrowd faction = GetEnemyCrowd(agent);
            CombatPositioningMode mode = faction != null ? faction.positioningMode : CombatPositioningMode.SimpleApproach;
            
            // Update hybrid mode using AI decision system
            if (faction != null && faction.combatRangeType == CombatRangeType.Hybrid)
            {
                float currentDistance = Vector3.Distance(GetAgentPosition(agent), targetPosition);
                CombatRangeType newMode = DetermineHybridMode(agent, currentDistance, faction);
                
                // Track mode switches
                if (newMode != agent.activeHybridMode)
                {
                    agent.activeHybridMode = newMode;
                    agent.lastHybridModeSwitchTime = Time.time;
                }
            }
            
            Vector3 basePosition;
            
            switch (mode)
            {
                case CombatPositioningMode.Swarm:
                    basePosition = GetSwarmPosition(agent, targetPosition, out shouldStop);
                    break;
                    
                case CombatPositioningMode.QueuedCircle:
                    basePosition = GetQueuedCirclePosition(agent, targetPosition, agentIndex, out shouldStop);
                    break;
                    
                case CombatPositioningMode.FormationSlots:
                    basePosition = GetFormationSlotPosition(agent, targetPosition, agentIndex, out shouldStop);
                    break;
                    
                case CombatPositioningMode.Staggered:
                    basePosition = GetStaggeredPosition(agent, targetPosition, agentIndex, out shouldStop);
                    break;
                    
                case CombatPositioningMode.OrganizedSwarm:
                    basePosition = GetOrganizedSwarmPosition(agent, targetPosition, agentIndex, faction, out shouldStop);
                    break;
                    
                case CombatPositioningMode.BattleLine:
                    basePosition = GetBattleLinePosition(agent, targetPosition, agentIndex, faction, out shouldStop);
                    break;
                    
                case CombatPositioningMode.SimpleApproach:
                default:
                    basePosition = GetSimpleApproachPosition(agent, targetPosition, out shouldStop);
                    break;
            }
            
            // Apply spacing to prevent clumping
            // PRIORITY: reformationSpacing (for formation) > agentSpacing (for combat)
            bool applySpacing = false;
            float spacingToUse = 0f;
            
            if (faction != null)
            {
                float currentDistance = Vector3.Distance(GetAgentPosition(agent), targetPosition);
                var effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);
                
                // Check if agent is in REFORMATION range (outside combat, maintaining formation)
                // Use reformationSpacing when agent is far enough to be in formation mode
                bool isInReformationRange = currentDistance >= faction.formationDissolveDistance;
                
                if (isInReformationRange && faction.reformationSpacing > 0f)
                {
                    // REFORMATION MODE: Use reformationSpacing - agent is in formation, not combat
                    applySpacing = true;
                    spacingToUse = faction.reformationSpacing;
                }
                else if (faction.agentSpacing > 0f)
                {
                    // COMBAT MODE: Use agentSpacing - agent is in close combat range
                    
                    // OPPORTUNISTIC ATTACKS: Skip spacing when agent is in attack range and can attack immediately
                    // This ensures agents attack as soon as player walks into range, ignoring spacing rules
                    if (faction.allowOpportunisticAttacks && IsInAttackRange(agent, currentDistance))
                    {
                        applySpacing = false; // In attack range with opportunistic attacks - skip spacing, attack now!
                    }
                    // Skip spacing for active wave in OrganizedSwarm (they rush as a group)
                    else if (mode == CombatPositioningMode.OrganizedSwarm && effectiveRangeType == CombatRangeType.Melee && agent.isActiveWave)
                    {
                        applySpacing = false; // Active wave melee agents in OrganizedSwarm: no spacing, rush the target
                    }
                    else
                    {
                        applySpacing = true;
                        spacingToUse = faction.agentSpacing;
                    }
                }
            }
            
            if (applySpacing && spacingToUse > 0f)
            {
                basePosition = ApplyAgentSpacing(agent, basePosition, agentIndex, spacingToUse);
            }
            
            return basePosition;
        }
        
        /// <summary>
        /// Gets the current number of active attackers (for debugging).
        /// </summary>
        public int GetActiveAttackerCount()
        {
            return _activeAttackers.Count;
        }
        
        /// <summary>
        /// Checks if an agent can start attacking based on their faction's positioning mode, queue rules, and combat range type.
        /// </summary>
        public bool CanAgentAttack(CrowdAgent agent, int agentIndex, float distanceToTarget)
        {
            // Use agent's direct enemyCrowd reference first (Phase 2), fallback to list lookup
            EnemyCrowd faction = agent.enemyCrowd ?? GetEnemyCrowd(agent);
            
            // HOLDPOSITION STANCE - HIGHEST PRIORITY (before opportunistic checks)
            // HoldPosition agents stay at their assigned position and attack when in range
            // They bypass all positioning mode rules (slots, max attackers, etc.) since they don't move
            // This is critical for mounted wall units and guard posts
            if (agent.combatStance == CombatStance.HoldPosition)
            {
                // HoldPosition agents use ENGAGEMENT RADIUS instead of strict melee/ranged distance
                // because they can't move closer - they attack anything in their "zone of control"
                float holdPositionRange = faction != null ? faction.engagementRadius : 15f;
                
                // Also check faction's actual attack range as a fallback
                float actualAttackRange = 3f; // default
                if (faction != null)
                {
                    switch (faction.combatRangeType)
                    {
                        case CombatRangeType.Melee:
                            actualAttackRange = faction.meleeMaxDistance;
                            break;
                        case CombatRangeType.Ranged:
                            actualAttackRange = faction.rangedMaxDistance;
                            break;
                        case CombatRangeType.Hybrid:
                            // For Hybrid, determine which mode based on distance
                            // HoldPosition agents at range should use ranged, close should use melee
                            if (distanceToTarget > faction.meleeMaxDistance)
                            {
                                agent.activeHybridMode = CombatRangeType.Ranged;
                                actualAttackRange = faction.rangedMaxDistance;
                            }
                            else
                            {
                                agent.activeHybridMode = CombatRangeType.Melee;
                                actualAttackRange = faction.meleeMaxDistance;
                            }
                            break;
                    }
                }
                
                // Use the LARGER of engagement radius or actual attack range
                // This ensures HoldPosition agents can attack at reasonable distances
                float effectiveRange = Mathf.Max(holdPositionRange, actualAttackRange);
                return distanceToTarget <= effectiveRange;
            }
            
            // OPPORTUNISTIC ATTACKS - HIGHEST PRIORITY
            // If enabled and agent is in attack range, allow attack immediately
            // This bypasses ALL other checks - slot assignment, max attackers, positioning mode, etc.
            if (faction != null && faction.allowOpportunisticAttacks)
            {
                // Only check if in attack range - nothing else matters
                if (IsInAttackRange(agent, distanceToTarget))
                {
                    return true;
                }
            }
            
            // --- Normal attack logic (only reached if opportunistic attacks disabled or out of range) ---
            
            // Already attacking
            if (_activeAttackers.Contains(agentIndex))
                return true;
            
            // Check if distance is valid for the agent's faction combat range type
            if (!IsInAttackRange(agent, distanceToTarget))
                return false;
            
            CombatPositioningMode mode = faction != null ? faction.positioningMode : CombatPositioningMode.SimpleApproach;
            int maxAttackers = faction != null ? faction.maxConcurrentAttackers : 0;
            
            // Check positioning mode rules
            switch (mode)
            {
                case CombatPositioningMode.Swarm:
                    // Everyone can attack anytime in swarm mode (if in range)
                    return true;
                    
                case CombatPositioningMode.SimpleApproach:
                    // When maxAttackers = 0 (unlimited), skip limit check
                    if (maxAttackers == 0)
                        return true;
                    
                    // Otherwise respect maxConcurrentAttackers
                    if (_activeAttackers.Count >= maxAttackers)
                        return false;
                    
                    return true;
                    
                case CombatPositioningMode.QueuedCircle:
                case CombatPositioningMode.FormationSlots:
                    // For these modes, agent must have an active attack slot to attack
                    // This prevents agents from attacking while moving to position
                    if (!agent.hasActiveAttackSlot)
                        return false;
                    
                    // When maxAttackers = 0 (unlimited), skip queue logic entirely
                    if (maxAttackers == 0)
                        return true;
                    
                    // Already checked that we're in range (IsInAttackRange above)
                    // and have an attack slot, so allow the attack
                    return true;
                    
                case CombatPositioningMode.Staggered:
                    // When maxAttackers = 0 (unlimited), skip limit check
                    if (maxAttackers == 0)
                        return true;
                    
                    // Staggered mode: loose max attackers constraint with natural timing variation
                    // This creates Diablo 4-style variety where attacks feel less coordinated
                    if (_activeAttackers.Count >= maxAttackers + 2)
                    {
                        // Allow +2 over max for more organic feeling, but still prevent total chaos
                        return false;
                    }
                    // Note: The staggeredAttackDelayOffset will be used by the combat system
                    // to further delay attacks and create unsynchronized patterns
                    return true;
                    
                case CombatPositioningMode.OrganizedSwarm:
                    // Organized Swarm: only active wave agents can attack (unless reacting to threat)
                    // Active wave agents attack like Swarm (everyone can attack)
                    if (agent.isActiveWave)
                        return true;
                    
                    // Non-active wave agents can only attack if they were recently attacked (reactive defense)
                    if (faction != null && faction.reactiveDefense && agent.wasRecentlyAttacked)
                    {
                        // Check if reaction window is still valid (5 seconds)
                        if (Time.time - agent.lastAttackedTime < 5f)
                            return true;
                        // Clear the flag after timeout
                        agent.wasRecentlyAttacked = false;
                    }
                    
                    return false;
                    
                case CombatPositioningMode.BattleLine:
                    // Battle Line: Any agent in attack range can attack
                    // The positioning system ensures front-rank agents are placed in attack range
                    // Rear-rank agents stay back but can attack if an enemy comes close (opportunistic)
                    // This was already checked by opportunistic attack logic at the top, 
                    // but we return true here for any agent that passed the IsInAttackRange check
                    return true;
                    
                default:
                    return true;
            }
        }
        
        /// <summary>
        /// Gets the EnemyCrowd faction for an agent.
        /// </summary>
        private EnemyCrowd GetEnemyCrowd(CrowdAgent agent)
        {
            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
                return _enemyCrowds[agent.enemyCrowdIndex];
            return null;
        }
        
        /// <summary>
        /// Checks if a distance is within valid attack range based on agent's faction combat range type.
        /// For Hybrid agents, checks against the currently active mode (melee or ranged).
        /// </summary>
        public bool IsInAttackRange(CrowdAgent agent, float distance)
        {
            // Use agent's direct enemyCrowd reference first (Phase 2), fallback to list lookup
            EnemyCrowd faction = agent.enemyCrowd ?? GetEnemyCrowd(agent);
            if (faction == null)
                return distance <= 3f; // Default fallback
            
            // Add tolerance for minimum distance to handle agents that got slightly too close
            // This prevents the "walks into player and can't attack" issue
            const float MIN_DISTANCE_TOLERANCE = 0.15f;
            
            switch (faction.combatRangeType)
            {
                case CombatRangeType.Melee:
                    float meleeMinWithTolerance = Mathf.Max(0f, faction.meleeMinDistance - MIN_DISTANCE_TOLERANCE);
                    return distance >= meleeMinWithTolerance && distance <= faction.meleeMaxDistance;
                    
                case CombatRangeType.Ranged:
                    float rangedMinWithTolerance = Mathf.Max(0f, faction.rangedMinDistance - MIN_DISTANCE_TOLERANCE);
                    return distance >= rangedMinWithTolerance && distance <= faction.rangedMaxDistance;
                    
                case CombatRangeType.Hybrid:
                    // Check range based on currently active mode
                    if (agent.activeHybridMode == CombatRangeType.Ranged)
                    {
                        float hybridRangedMinWithTolerance = Mathf.Max(0f, faction.rangedMinDistance - MIN_DISTANCE_TOLERANCE);
                        return distance >= hybridRangedMinWithTolerance && distance <= faction.rangedMaxDistance;
                    }
                    else
                    {
                        float hybridMeleeMinWithTolerance = Mathf.Max(0f, faction.meleeMinDistance - MIN_DISTANCE_TOLERANCE);
                        return distance >= hybridMeleeMinWithTolerance && distance <= faction.meleeMaxDistance;
                    }
                    
                default:
                    return distance <= 3f; // Default fallback
            }
        }
        
        /// <summary>
        /// Determines the active combat mode for Hybrid agents.
        /// Uses lightweight distance-based switching by default, or advanced AI if enabled per faction.
        /// </summary>
        private CombatRangeType DetermineHybridMode(CrowdAgent agent, float currentDistance, EnemyCrowd faction)
        {
            if (faction == null || faction.combatRangeType != CombatRangeType.Hybrid)
                return CombatRangeType.Melee;

            if (agent != null)
            {
                if (agent.combatRangeOverride == CombatRangeOverrideMode.ForceMelee)
                    return CombatRangeType.Melee;
                if (agent.combatRangeOverride == CombatRangeOverrideMode.ForceRanged)
                    return CombatRangeType.Ranged;
            }
            
            // Use advanced AI decision system if enabled (more variety but higher CPU cost)
            if (faction.useAdvancedHybridAI)
            {
                return DetermineHybridModeAdvanced(agent, currentDistance, faction);
            }
            
            // Otherwise use lightweight distance-based switching (better performance)
            return DetermineHybridModeSimple(agent, currentDistance, faction);
        }
        
        /// <summary>
        /// Lightweight distance-based hybrid mode switching with hysteresis.
        /// Very fast, suitable for 500+ agents. All agents behave identically based on distance.
        /// </summary>
        private CombatRangeType DetermineHybridModeSimple(CrowdAgent agent, float currentDistance, EnemyCrowd faction)
        {
            // Calculate transition thresholds with hysteresis to prevent rapid switching
            float meleeToRangedThreshold = faction.meleeMaxDistance + 1.5f; // Switch to ranged when beyond melee max + buffer
            float rangedToMeleeThreshold = faction.meleeMaxDistance - 0.5f; // Switch to melee when within melee max - buffer
            
            // Check if currently in ranged mode
            bool isCurrentlyRanged = agent.activeHybridMode == CombatRangeType.Ranged;
            
            if (isCurrentlyRanged)
            {
                // Switch to melee if target comes close
                if (currentDistance <= rangedToMeleeThreshold)
                {
                    return CombatRangeType.Melee;
                }
                return CombatRangeType.Ranged; // Stay ranged
            }
            else
            {
                // Switch to ranged if target moves away
                if (currentDistance >= meleeToRangedThreshold)
                {
                    return CombatRangeType.Ranged;
                }
                return CombatRangeType.Melee; // Stay melee
            }
        }
        
        /// <summary>
        /// Advanced AI-based hybrid mode decision making with personality and tactics.
        /// More expensive (O(n) scan per agent), provides variety and intelligent behavior.
        /// Best for smaller crowds (< 100 agents) or when variety is more important than performance.
        /// </summary>
        private CombatRangeType DetermineHybridModeAdvanced(CrowdAgent agent, float currentDistance, EnemyCrowd faction)
        {
            // Count nearby allies by their combat mode for tactical decisions
            Vector3 agentPosition = GetAgentPosition(agent);
            float tacticalAwarenessRadius = 15f; // Check allies within 15m
            
            _hybridDecisionSystem.CountNearbyAlliesByMode(
                agent,
                agentPosition,
                _agents,
                agent.enemyCrowdIndex,
                tacticalAwarenessRadius,
                out int nearbyMelee,
                out int nearbyRanged
            );
            
            // Calculate time since last mode switch
            float timeSinceLastSwitch = Time.time - agent.lastHybridModeSwitchTime;
            
            // Use AI system to evaluate best combat mode
            CombatRangeType recommendedMode = _hybridDecisionSystem.EvaluateCombatMode(
                agent,
                currentDistance,
                faction,
                nearbyMelee,
                nearbyRanged,
                timeSinceLastSwitch
            );
            
            return recommendedMode;
        }
        
        /// <summary>
        /// Gets the effective combat range type for positioning logic.
        /// For Hybrid agents, returns the currently active mode (Melee or Ranged).
        /// For non-Hybrid agents, returns their base combat range type.
        /// </summary>
        private CombatRangeType GetEffectiveCombatRangeType(CrowdAgent agent, EnemyCrowd faction)
        {
            if (faction == null)
                return CombatRangeType.Melee;
            
            // For Hybrid type, use the currently active mode
            if (faction.combatRangeType == CombatRangeType.Hybrid)
                return agent.activeHybridMode;
            
            // For non-Hybrid, use base type
            return faction.combatRangeType;
        }
        
        /// <summary>
        /// Gets the ideal positioning distance based on agent's faction combat range type.
        /// For Hybrid agents, uses the currently active mode.
        /// </summary>
        public float GetIdealCombatDistance(CrowdAgent agent)
        {
            EnemyCrowd faction = GetEnemyCrowd(agent);
            if (faction == null)
                return 2f; // Default fallback
            
            switch (faction.combatRangeType)
            {
                case CombatRangeType.Melee:
                    // Center of attack range for optimal positioning
                    return (faction.meleeMinDistance + faction.meleeMaxDistance) * 0.5f;
                    
                case CombatRangeType.Ranged:
                    return faction.rangedPreferredDistance;
                    
                case CombatRangeType.Hybrid:
                    // Use active mode to determine distance
                    if (agent.activeHybridMode == CombatRangeType.Ranged)
                        return faction.rangedPreferredDistance;
                    else
                        return (faction.meleeMinDistance + faction.meleeMaxDistance) * 0.5f;
                    
                default:
                    return 2f; // Default fallback
            }
        }
        
        /// <summary>
        /// Gets the maximum attack distance for an agent based on their combat range type.
        /// This ensures positioning systems don't place agents outside attack range.
        /// </summary>
        public float GetMaxAttackDistance(CrowdAgent agent)
        {
            EnemyCrowd faction = GetEnemyCrowd(agent);
            if (faction == null)
                return 2f; // Default fallback
            
            CombatRangeType effectiveType = GetEffectiveCombatRangeType(agent, faction);
            
            switch (effectiveType)
            {
                case CombatRangeType.Melee:
                    return faction.meleeMaxDistance;
                    
                case CombatRangeType.Ranged:
                    return faction.rangedMaxDistance;
                    
                default:
                    return faction.meleeMaxDistance;
            }
        }
        
        /// <summary>
        /// Registers an agent as actively attacking.
        /// </summary>
        public void BeginAttack(int agentIndex)
        {
            bool wasNew = _activeAttackers.Add(agentIndex);
            
            // Remove from queue if present
            if (_attackQueue.Contains(agentIndex))
            {
                var temp = new Queue<int>();
                while (_attackQueue.Count > 0)
                {
                    int idx = _attackQueue.Dequeue();
                    if (idx != agentIndex)
                        temp.Enqueue(idx);
                }
                while (temp.Count > 0)
                    _attackQueue.Enqueue(temp.Dequeue());
            }
            

        }
        
        /// <summary>
        /// Unregisters an agent from active attackers, allowing next in queue to attack.
        /// </summary>
        public void EndAttack(int agentIndex)
        {
            bool wasActive = _activeAttackers.Remove(agentIndex);
            
            // Process attack queue (handles queue management across all modes)
            ProcessAttackQueue();
        }
        
        /// <summary>
        /// Assigns a formation slot to an agent (for FormationSlots mode).
        /// Actual slot positioning is calculated per-faction in GetFormationSlotPosition.
        /// </summary>
        public int AssignFormationSlot(int agentIndex)
        {
            // Already has a slot
            if (_agentToSlotIndex.TryGetValue(agentIndex, out int existingSlot))
                return existingSlot;
            
            // Use a generous default for slot assignment (actual formation uses per-faction calculation)
            const int defaultMaxSlots = 24;
            
            // Find first available slot
            for (int i = 0; i < defaultMaxSlots; i++)
            {
                if (!_occupiedSlots.Contains(i))
                {
                    _occupiedSlots.Add(i);
                    _agentToSlotIndex[agentIndex] = i;
                    return i;
                }
            }
            
            // All slots full, assign overflow slot
            int overflowSlot = defaultMaxSlots + (_agentToSlotIndex.Count % defaultMaxSlots);
            _agentToSlotIndex[agentIndex] = overflowSlot;
            return overflowSlot;
        }
        
        /// <summary>
        /// Releases a formation slot when agent dies or leaves combat.
        /// </summary>
        public void ReleaseFormationSlot(int agentIndex)
        {
            if (_agentToSlotIndex.TryGetValue(agentIndex, out int slot))
            {
                _occupiedSlots.Remove(slot);
                _agentToSlotIndex.Remove(agentIndex);
            }
        }
        
        /// <summary>
        /// Clears all combat state for an agent.
        /// </summary>
        public void ClearAgentState(int agentIndex)
        {
            _activeAttackers.Remove(agentIndex);
            ReleaseFormationSlot(agentIndex);
            
            // Remove from queue
            if (_attackQueue.Contains(agentIndex))
            {
                var temp = new Queue<int>();
                while (_attackQueue.Count > 0)
                {
                    int idx = _attackQueue.Dequeue();
                    if (idx != agentIndex)
                        temp.Enqueue(idx);
                }
                while (temp.Count > 0)
                    _attackQueue.Enqueue(temp.Dequeue());
            }
        }
        
        #region Position Calculation Methods
        
        private Vector3 GetSwarmPosition(CrowdAgent agent, Vector3 targetPosition, out bool shouldStop)
        {
            shouldStop = false;
            // Swarm: just rush directly at target, no stopping
            return targetPosition;
        }
        
        private Vector3 GetSimpleApproachPosition(CrowdAgent agent, Vector3 targetPosition, out bool shouldStop)
        {
            Vector3 agentPos = GetAgentPosition(agent);
            float distance = Vector3.Distance(agentPos, targetPosition);
            float idealDistance = GetIdealCombatDistance(agent);
            
            EnemyCrowd faction = GetEnemyCrowd(agent);
            CombatRangeType effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);

            // SIMPLE MELEE RUSH: ignore offsets/buffers, just charge the target
            if (effectiveRangeType == CombatRangeType.Melee)
            {
                shouldStop = false; // Never stop early; combat system will stop when attacking
                return targetPosition; // Drive NavMeshAgent straight into the target
            }
            
            // For ranged combat, stop at preferred distance
            if (effectiveRangeType == CombatRangeType.Ranged)
            {
                shouldStop = _settings.stopMovementWhenClose && Mathf.Abs(distance - idealDistance) < 1f;
                
                // Calculate position at ideal ranged distance
                if (distance > idealDistance + 0.5f)
                    return targetPosition; // Move closer
                else if (distance < idealDistance - 0.5f)
                {
                    // Move away to maintain distance
                    Vector3 direction = (agentPos - targetPosition).normalized;
                    return targetPosition + direction * idealDistance;
                }
                return agentPos; // Already at ideal distance
            }
            
            // For melee (including Hybrid in melee mode), position at ideal melee distance
            float stopBuffer = faction != null ? faction.closeRangeStopBuffer : 0.35f;
            float stopDistance = idealDistance + stopBuffer;
            shouldStop = _settings.stopMovementWhenClose && distance <= stopDistance;
            
            // If too close, back away to ideal distance
            if (distance < idealDistance - 0.5f)
            {
                Vector3 direction = (agentPos - targetPosition).normalized;
                return targetPosition + direction * idealDistance;
            }
            
            // If at good distance, hold position
            if (distance <= stopDistance)
                return agentPos;
            
            // If too far, move closer to ideal distance
            Vector3 directionToTarget = (targetPosition - agentPos).normalized;
            return targetPosition - directionToTarget * idealDistance;
        }
        
        /// <summary>
        /// QueuedCircle positioning: Up to maxConcurrentAttackers agents are positioned in attack range.
        /// Excess agents wait in outer rings. Agents rotate after their attack slot duration expires.
        /// </summary>
        private Vector3 GetQueuedCirclePosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex, out bool shouldStop)
        {
            shouldStop = false;
            Vector3 agentPos = GetAgentPosition(agent);
            float distanceToTarget = Vector3.Distance(agentPos, targetPosition);
            float currentTime = Time.time;
            
            EnemyCrowd faction = GetEnemyCrowd(agent);
            float formationRadius = faction != null ? faction.formationRadius : 3f;
            float minAngle = faction != null ? faction.minAngleBetweenEnemies : 30f;
            int maxAttackers = faction != null ? faction.maxConcurrentAttackers : 3;
            float slotDuration = faction != null ? faction.attackSlotDuration : 5f;
            
            // Get combat range info
            CombatRangeType effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);
            float idealDistance = GetIdealCombatDistance(agent);
            
            // Calculate inner ring radius based on combat type
            float innerRingRadius;
            if (effectiveRangeType == CombatRangeType.Melee)
            {
                innerRingRadius = idealDistance + 0.3f; // Slightly outside ideal to allow approach
            }
            else if (effectiveRangeType == CombatRangeType.Ranged)
            {
                innerRingRadius = faction?.rangedPreferredDistance ?? formationRadius;
            }
            else
            {
                innerRingRadius = formationRadius;
            }
            
            // === ATTACK SLOT MANAGEMENT ===
            
            // Check if this agent's slot has expired (time-based rotation)
            bool slotExpired = false;
            if (agent.hasActiveAttackSlot && slotDuration > 0f)
            {
                slotExpired = currentTime - agent.attackSlotStartTime >= slotDuration;
            }
            
            // Force rotation if slot expired
            if (slotExpired && agent.hasActiveAttackSlot)
            {
                agent.hasActiveAttackSlot = false;
                _activeAttackers.Remove(agentIndex);
            }
            
            // Count current active slot holders
            int currentSlotHolders = _activeAttackers.Count;
            bool hasAvailableSlot = maxAttackers <= 0 || currentSlotHolders < maxAttackers;
            
            // Determine if this agent should have an attack slot
            bool shouldHaveSlot = agent.hasActiveAttackSlot || 
                                  (hasAvailableSlot && !agent.hasActiveAttackSlot);
            
            // Assign slot if agent should have one and doesn't
            if (shouldHaveSlot && !agent.hasActiveAttackSlot)
            {
                agent.hasActiveAttackSlot = true;
                agent.attackSlotStartTime = currentTime;
                _activeAttackers.Add(agentIndex);
            }
            
            // === POSITION CALCULATION ===
            
            if (agent.hasActiveAttackSlot)
            {
                // ACTIVE SLOT: Position at attack range
                // Calculate angle spread for agents with slots
                int slotIndex = 0;
                int slotCount = 0;
                foreach (int idx in _activeAttackers)
                {
                    if (idx == agentIndex) slotIndex = slotCount;
                    slotCount++;
                }
                
                // Spread active attackers evenly around the target
                float angleStep = slotCount > 0 ? 360f / slotCount : 360f;
                float angle = slotIndex * angleStep;
                
                // Position at ideal attack distance
                Vector3 attackOffset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * idealDistance;
                Vector3 attackPosition = targetPosition + attackOffset;
                
                // Movement logic
                float stopBuffer = faction != null ? faction.closeRangeStopBuffer : 0.35f;
                float desiredStopDistance = idealDistance + stopBuffer;
                
                if (effectiveRangeType == CombatRangeType.Ranged)
                {
                    shouldStop = Mathf.Abs(distanceToTarget - idealDistance) < 1f;
                    
                    if (distanceToTarget > idealDistance + 0.5f)
                        return targetPosition; // Move closer
                    else if (distanceToTarget < idealDistance - 0.5f)
                    {
                        Vector3 direction = (agentPos - targetPosition).normalized;
                        return targetPosition + direction * idealDistance;
                    }
                    return agentPos;
                }
                
                // Melee positioning
                shouldStop = _settings.stopMovementWhenClose && distanceToTarget <= desiredStopDistance;
                
                if (distanceToTarget < idealDistance - 0.5f)
                {
                    Vector3 direction = (agentPos - targetPosition).normalized;
                    return targetPosition + direction * idealDistance;
                }
                
                if (distanceToTarget <= desiredStopDistance)
                    return agentPos;
                
                // Move toward attack position
                return attackPosition;
            }
            else
            {
                // NO SLOT: Position in waiting ring (just outside attack range)
                // Use agent index modulo to determine position in waiting ring
                int agentsPerRing = Mathf.Max(3, Mathf.CeilToInt(360f / minAngle));
                int ringIndex = 1; // Waiting agents are in ring 1+
                int positionInRing = agentIndex % agentsPerRing;
                
                // Calculate ring radius - position just outside max attack range so agents can attack when slots open
                float maxAttackDist = GetMaxAttackDistance(agent);
                float waitingBuffer = 1.5f; // Stay this far outside attack range
                float ringRadius = maxAttackDist + waitingBuffer + ((ringIndex - 1) * 2f);
                
                // Calculate angle
                float angleStep = 360f / agentsPerRing;
                float angle = positionInRing * angleStep;
                
                Vector3 waitOffset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * ringRadius;
                Vector3 waitPosition = targetPosition + waitOffset;
                
                float distToWait = Vector3.Distance(agentPos, waitPosition);
                shouldStop = distToWait <= 0.5f;
                
                return waitPosition;
            }
        }
        
        /// <summary>
        /// FormationSlots positioning: Similar to QueuedCircle but uses fixed slot assignments.
        /// Up to maxConcurrentAttackers agents are positioned in attack range with time-based rotation.
        /// </summary>
        private Vector3 GetFormationSlotPosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex, out bool shouldStop)
        {
            shouldStop = false;
            Vector3 agentPos = GetAgentPosition(agent);
            float distanceToTarget = Vector3.Distance(agentPos, targetPosition);
            float currentTime = Time.time;
            
            EnemyCrowd faction = GetEnemyCrowd(agent);
            float formationRadius = faction != null ? faction.formationRadius : 3f;
            int maxAttackers = faction != null ? faction.maxConcurrentAttackers : 3;
            float slotDuration = faction != null ? faction.attackSlotDuration : 5f;
            
            // Get combat range info
            CombatRangeType effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);
            float idealDistance = GetIdealCombatDistance(agent);
            
            // Calculate inner ring radius based on combat type
            float innerRingRadius;
            if (effectiveRangeType == CombatRangeType.Melee)
            {
                innerRingRadius = idealDistance + 0.3f;
            }
            else if (effectiveRangeType == CombatRangeType.Ranged)
            {
                innerRingRadius = faction?.rangedPreferredDistance ?? formationRadius;
            }
            else
            {
                innerRingRadius = formationRadius;
            }
            
            // === ATTACK SLOT MANAGEMENT ===
            
            // Check if slot expired
            bool slotExpired = false;
            if (agent.hasActiveAttackSlot && slotDuration > 0f)
            {
                slotExpired = currentTime - agent.attackSlotStartTime >= slotDuration;
            }
            
            if (slotExpired && agent.hasActiveAttackSlot)
            {
                agent.hasActiveAttackSlot = false;
                _activeAttackers.Remove(agentIndex);
            }
            
            int currentSlotHolders = _activeAttackers.Count;
            bool hasAvailableSlot = maxAttackers <= 0 || currentSlotHolders < maxAttackers;
            
            bool shouldHaveSlot = agent.hasActiveAttackSlot || 
                                  (hasAvailableSlot && !agent.hasActiveAttackSlot);
            
            if (shouldHaveSlot && !agent.hasActiveAttackSlot)
            {
                agent.hasActiveAttackSlot = true;
                agent.attackSlotStartTime = currentTime;
                _activeAttackers.Add(agentIndex);
            }
            
            // === POSITION CALCULATION ===
            
            if (agent.hasActiveAttackSlot)
            {
                // Calculate slot position in attack ring
                int slotIndex = 0;
                int slotCount = 0;
                foreach (int idx in _activeAttackers)
                {
                    if (idx == agentIndex) slotIndex = slotCount;
                    slotCount++;
                }
                
                float angleStep = slotCount > 0 ? 360f / slotCount : 360f;
                float angle = slotIndex * angleStep;
                
                Vector3 attackOffset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * idealDistance;
                Vector3 attackPosition = targetPosition + attackOffset;
                
                float stopBuffer = faction != null ? faction.closeRangeStopBuffer : 0.35f;
                float desiredStopDistance = idealDistance + stopBuffer;
                
                if (effectiveRangeType == CombatRangeType.Ranged)
                {
                    shouldStop = Mathf.Abs(distanceToTarget - idealDistance) < 1f;
                    
                    if (distanceToTarget > idealDistance + 0.5f)
                        return targetPosition;
                    else if (distanceToTarget < idealDistance - 0.5f)
                    {
                        Vector3 direction = (agentPos - targetPosition).normalized;
                        return targetPosition + direction * idealDistance;
                    }
                    return agentPos;
                }
                
                shouldStop = _settings.stopMovementWhenClose && distanceToTarget <= desiredStopDistance;
                
                if (distanceToTarget < idealDistance - 0.5f)
                {
                    Vector3 direction = (agentPos - targetPosition).normalized;
                    return targetPosition + direction * idealDistance;
                }
                
                if (distanceToTarget <= desiredStopDistance)
                    return agentPos;
                
                return attackPosition;
            }
            else
            {
                // Waiting position - just outside attack range
                int maxSlotsForFaction = CalculateFormationSlotsForFaction(faction);
                int slot = AssignFormationSlot(agentIndex);
                int slotInRing = slot % maxSlotsForFaction;
                
                // Position just outside max attack range so agents can attack when slots open
                float maxAttackDist = GetMaxAttackDistance(agent);
                float waitingBuffer = 1.5f;
                float ringRadius = maxAttackDist + waitingBuffer;
                
                float angleStep = 360f / maxSlotsForFaction;
                float angle = slotInRing * angleStep;
                
                Vector3 waitOffset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * ringRadius;
                Vector3 waitPosition = targetPosition + waitOffset;
                
                float distToWait = Vector3.Distance(agentPos, waitPosition);
                shouldStop = distToWait <= 0.5f;
                
                return waitPosition;
            }
        }
        
        private Vector3 GetStaggeredPosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex, out bool shouldStop)
        {
            shouldStop = false;
            Vector3 agentPos = GetAgentPosition(agent);
            float distanceToTarget = Vector3.Distance(agentPos, targetPosition);
            
            EnemyCrowd faction = GetEnemyCrowd(agent);
            float idealDistance = GetIdealCombatDistance(agent);
            CombatRangeType effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);
            
            // Initialize staggered values if not set (first time for this agent)
            if (agent.staggeredAngleOffset == 0f && agent.staggeredDistanceVariance == 0f)
            {
                // Random angle offset: -45 to +45 degrees for positioning variety
                agent.staggeredAngleOffset = UnityEngine.Random.Range(-45f, 45f);
                
                // Random distance variance: -10% to +10% to stay within attack range
                // Smaller variance ensures agents stay in attack range
                agent.staggeredDistanceVariance = UnityEngine.Random.Range(-0.1f, 0.1f);
                
                // Random attack delay: 0 to 0.5 seconds for unsynchronized attacks
                agent.staggeredAttackDelayOffset = UnityEngine.Random.Range(0f, 0.5f);
                
                // Next reposition time: add variety to movement patterns
                agent.staggeredNextRepositionTime = Time.time + UnityEngine.Random.Range(2f, 5f);
            }
            
            // Occasionally update positioning values to create dynamic movement
            if (Time.time >= agent.staggeredNextRepositionTime)
            {
                agent.staggeredAngleOffset = UnityEngine.Random.Range(-45f, 45f);
                agent.staggeredDistanceVariance = UnityEngine.Random.Range(-0.1f, 0.1f);
                agent.staggeredNextRepositionTime = Time.time + UnityEngine.Random.Range(3f, 6f);
            }
            
            // Calculate varied ideal distance for this agent, clamped to attack range
            float variedIdealDistance = idealDistance * (1f + agent.staggeredDistanceVariance);
            
            // Clamp to attack range to ensure agents can attack
            float minDist = effectiveRangeType == CombatRangeType.Ranged 
                ? (faction?.rangedMinDistance ?? 5f) 
                : (faction?.meleeMinDistance ?? 1f);
            float maxDist = effectiveRangeType == CombatRangeType.Ranged 
                ? (faction?.rangedMaxDistance ?? 15f) 
                : (faction?.meleeMaxDistance ?? 2f);
            variedIdealDistance = Mathf.Clamp(variedIdealDistance, minDist + 0.1f, maxDist - 0.1f);
            
            // Calculate direction with angle offset for positioning variety
            Vector3 directionToAgent = (agentPos - targetPosition).normalized;
            Quaternion angleVariation = Quaternion.Euler(0f, agent.staggeredAngleOffset, 0f);
            Vector3 variedDirection = angleVariation * directionToAgent;
            
            // Calculate ideal position with variations
            Vector3 idealPosition = targetPosition + variedDirection * variedIdealDistance;
            float distanceToIdeal = Vector3.Distance(agentPos, idealPosition);
            
            // Behavior based on combat range type
            if (effectiveRangeType == CombatRangeType.Ranged)
            {
                // Ranged: maintain preferred distance with variation
                shouldStop = Mathf.Abs(distanceToTarget - variedIdealDistance) < 1.5f;
                
                if (distanceToIdeal > 1f)
                    return idealPosition;
                
                return agentPos; // Hold varied position
            }
            else
            {
                // Melee: approach with variation, stopping at varied distance
                float stopBuffer = faction != null ? faction.closeRangeStopBuffer : 0.35f;
                float variedStopDistance = variedIdealDistance + stopBuffer;
                shouldStop = _settings.stopMovementWhenClose && distanceToTarget <= variedStopDistance;
                
                // If too close, back away to varied ideal distance
                if (distanceToTarget < variedIdealDistance - 0.5f)
                {
                    return idealPosition;
                }
                
                // If at good distance (with tolerance for variation), hold position
                if (distanceToIdeal < 0.8f)
                    return agentPos;
                
                // Move to varied ideal position
                return idealPosition;
            }
        }
        
        /// <summary>
        /// Organized Swarm positioning: agents are divided into wave groups.
        /// Active wave attacks in swarm style, waiting waves position at respectful distance.
        /// </summary>
        private Vector3 GetOrganizedSwarmPosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex, EnemyCrowd faction, out bool shouldStop)
        {
            shouldStop = false;
            
            if (faction == null)
            {
                // Fallback to simple swarm
                return GetSwarmPosition(agent, targetPosition, out shouldStop);
            }
            
            // Ensure wave groups are assigned and managed for this faction
            UpdateOrganizedSwarmWaves(faction, agent.enemyCrowdIndex);
            
            Vector3 agentPos = GetAgentPosition(agent);
            float distanceToTarget = Vector3.Distance(agentPos, targetPosition);
            
            // Check if agent is in active wave or reacting to attack
            bool canAttack = agent.isActiveWave || 
                            (faction.reactiveDefense && agent.wasRecentlyAttacked && Time.time - agent.lastAttackedTime < 5f);
            
            if (canAttack)
            {
                // Active wave: behave like Swarm - rush directly at target
                return GetSwarmPosition(agent, targetPosition, out shouldStop);
            }
            
            // Waiting wave: position at respectful distance (just outside attack range)
            // Use max attack distance + buffer as minimum waiting distance
            float maxAttackDist = GetMaxAttackDistance(agent);
            float waitingDistance = Mathf.Max(faction.waveWaitingDistance, maxAttackDist + 1.5f);
            float idealDistance = GetIdealCombatDistance(agent);
            
            // If already beyond waiting distance, just hold position or move closer to waiting distance
            if (distanceToTarget >= waitingDistance)
            {
                // Too far, move to waiting distance
                Vector3 dirToTarget = (targetPosition - agentPos).normalized;
                Vector3 waitingPosition = targetPosition - dirToTarget * waitingDistance;
                
                float distToWaitingPos = Vector3.Distance(agentPos, waitingPosition);
                shouldStop = distToWaitingPos < 1f;
                
                return waitingPosition;
            }
            
            // Inside waiting distance - back away to waiting distance (unless in attack range and reactive)
            if (distanceToTarget < waitingDistance - 0.5f)
            {
                // Too close, back away
                Vector3 dirFromTarget = (agentPos - targetPosition).normalized;
                
                // Position around the target at waiting distance with some angle variance based on agent index
                float angleOffset = (agentIndex % 12) * 30f; // Spread waiting agents around the target
                Quaternion rotation = Quaternion.Euler(0f, angleOffset, 0f);
                Vector3 offsetDir = rotation * dirFromTarget;
                
                Vector3 waitingPosition = targetPosition + offsetDir * waitingDistance;
                shouldStop = false;
                return waitingPosition;
            }
            
            // At good waiting distance, hold position
            shouldStop = true;
            return agentPos;
        }
        
        /// <summary>
        /// Updates wave assignments and active wave rotation for OrganizedSwarm mode.
        /// Called each frame to manage wave transitions.
        /// </summary>
        private void UpdateOrganizedSwarmWaves(EnemyCrowd faction, int factionIndex)
        {
            if (faction == null || factionIndex < 0)
                return;
            
            float currentTime = Time.time;
            
            // Initialize wave tracking for this faction if needed
            if (!_factionWaveAssigned.ContainsKey(factionIndex) || !_factionWaveAssigned[factionIndex])
            {
                AssignWaveGroups(faction, factionIndex);
                _factionActiveWaveIndex[factionIndex] = 0;
                _factionWaveStartTime[factionIndex] = currentTime;
                _factionWaveAssigned[factionIndex] = true;
            }
            
            // Check if it's time to rotate to next wave
            if (!_factionWaveStartTime.ContainsKey(factionIndex))
                _factionWaveStartTime[factionIndex] = currentTime;
            
            float waveStartTime = _factionWaveStartTime[factionIndex];
            float totalWaveDuration = faction.waveAttackDuration + faction.waveCooldown;
            
            if (currentTime - waveStartTime >= totalWaveDuration)
            {
                // Rotate to next wave
                int currentWave = _factionActiveWaveIndex.ContainsKey(factionIndex) ? _factionActiveWaveIndex[factionIndex] : 0;
                int nextWave = (currentWave + 1) % faction.waveGroupCount;
                
                _factionActiveWaveIndex[factionIndex] = nextWave;
                _factionWaveStartTime[factionIndex] = currentTime;
                
                // Update all agents' active wave status
                UpdateAgentActiveWaveStatus(factionIndex, nextWave);
            }
        }
        
        /// <summary>
        /// Assigns wave groups to all agents of a faction.
        /// Divides agents evenly across the specified number of wave groups.
        /// </summary>
        private void AssignWaveGroups(EnemyCrowd faction, int factionIndex)
        {
            if (faction == null)
                return;
            
            int waveGroupCount = Mathf.Max(1, faction.waveGroupCount);
            int agentInFactionIndex = 0;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.enemyCrowdIndex != factionIndex)
                    continue;
                
                // Assign wave group based on order (distributes evenly)
                agent.waveGroupIndex = agentInFactionIndex % waveGroupCount;
                agent.isActiveWave = (agent.waveGroupIndex == 0); // First wave is initially active
                agentInFactionIndex++;
            }
        }
        
        /// <summary>
        /// Updates all agents' isActiveWave status when wave rotates.
        /// </summary>
        private void UpdateAgentActiveWaveStatus(int factionIndex, int activeWaveIndex)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.enemyCrowdIndex != factionIndex)
                    continue;
                
                agent.isActiveWave = (agent.waveGroupIndex == activeWaveIndex);
            }
        }
        
        /// <summary>
        /// Call this when an agent takes damage to enable reactive defense for OrganizedSwarm.
        /// </summary>
        public void NotifyAgentAttacked(CrowdAgent agent)
        {
            if (agent == null)
                return;
            
            agent.wasRecentlyAttacked = true;
            agent.lastAttackedTime = Time.time;
        }
        
        /// <summary>
        /// Resets wave assignments for a faction (call when agents spawn/despawn).
        /// </summary>
        public void ResetFactionWaves(int factionIndex)
        {
            if (factionIndex < 0)
                return;
            
            _factionWaveAssigned[factionIndex] = false;
            _factionActiveWaveIndex.Remove(factionIndex);
            _factionWaveStartTime.Remove(factionIndex);
        }
        
        #endregion
        
        #region Battle Line Positioning
        
        // Battle line state per faction
        private readonly Dictionary<int, BattleLineState> _battleLineStates = new Dictionary<int, BattleLineState>();
        
        private class BattleLineState
        {
            public Vector3 lineCenter;
            public Vector3 lineDirection; // Direction the line faces (toward enemy)
            public Vector3 lineRight; // Perpendicular direction (for spreading agents)
            public float currentAdvanceDistance;
            public float lastUpdateTime;
            public int totalAgentsInLine;
            public bool isInitialized;
        }
        
        /// <summary>
        /// Battle Line positioning: Forms proper military-style battle lines.
        /// Creates front ranks, support ranks, and flanking units.
        /// </summary>
        private Vector3 GetBattleLinePosition(CrowdAgent agent, Vector3 targetPosition, int agentIndex, EnemyCrowd faction, out bool shouldStop)
        {
            shouldStop = false;
            
            if (faction == null || agent.navigationAgent == null)
            {
                return GetSimpleApproachPosition(agent, targetPosition, out shouldStop);
            }
            
            Vector3 agentPos = GetAgentPosition(agent);
            float distanceToTarget = Vector3.Distance(agentPos, targetPosition);
            
            // Get or create battle line state for this faction
            int factionIndex = agent.enemyCrowdIndex;
            if (!_battleLineStates.TryGetValue(factionIndex, out BattleLineState lineState))
            {
                lineState = new BattleLineState();
                _battleLineStates[factionIndex] = lineState;
            }
            
            // Initialize or update battle line formation
            UpdateBattleLineState(lineState, faction, factionIndex, targetPosition);
            
            // Determine agent's role and position in the formation
            int factionAgentIndex = GetAgentIndexWithinFaction(agent, factionIndex);
            int totalFactionAgents = CountFactionAgents(factionIndex);
            
            // Calculate how many agents should be flankers
            int flankerCount = Mathf.FloorToInt(totalFactionAgents * faction.battleLineFlankingRatio);
            int lineAgents = totalFactionAgents - flankerCount;
            
            Vector3 targetPositionFlat = new Vector3(targetPosition.x, agentPos.y, targetPosition.z);
            
            // Determine if this agent is a flanker or front line
            bool isFlanker = factionAgentIndex >= lineAgents;
            
            if (isFlanker)
            {
                return GetBattleLineFlankerPosition(agent, targetPositionFlat, factionAgentIndex - lineAgents, flankerCount, lineState, faction, out shouldStop);
            }
            else
            {
                return GetBattleLineRankPosition(agent, targetPositionFlat, factionAgentIndex, lineAgents, lineState, faction, out shouldStop);
            }
        }
        
        /// <summary>
        /// Updates the battle line state - calculates line center, direction, and advance progress.
        /// </summary>
        private void UpdateBattleLineState(BattleLineState state, EnemyCrowd faction, int factionIndex, Vector3 targetPosition)
        {
            float currentTime = Time.time;
            
            // Throttle updates for performance
            if (state.isInitialized && currentTime - state.lastUpdateTime < 0.1f)
                return;
            
            state.lastUpdateTime = currentTime;
            
            // Calculate average position of all faction agents to determine line center
            Vector3 sumPositions = Vector3.zero;
            int count = 0;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.enemyCrowdIndex != factionIndex || agent.navigationAgent == null)
                    continue;
                
                sumPositions += GetAgentPosition(agent);
                count++;
            }
            
            if (count == 0)
                return;
            
            state.totalAgentsInLine = count;
            Vector3 averagePosition = sumPositions / count;
            
            // Direction from line center to target
            Vector3 toTarget = (targetPosition - averagePosition);
            toTarget.y = 0; // Keep on horizontal plane
            
            if (toTarget.sqrMagnitude < 0.01f)
            {
                state.lineDirection = Vector3.forward;
            }
            else
            {
                state.lineDirection = toTarget.normalized;
            }
            
            // Right vector perpendicular to line direction (for spreading agents)
            state.lineRight = Vector3.Cross(Vector3.up, state.lineDirection).normalized;
            
            // Calculate ideal line center position based on engagement distance and advance
            float distanceToTarget = Vector3.Distance(averagePosition, targetPosition);
            
            // Use configured engagement distance, but clamp to max attack range for the combat type
            // For melee, we want to advance more aggressively (closer to target)
            float maxAttackRange;
            float engagementDist;
            
            if (faction.combatRangeType == CombatRangeType.Melee)
            {
                // Melee: Line should advance close to target, individual agents will break off to engage
                maxAttackRange = faction.meleeMaxDistance;
                // Set engagement distance to just outside melee range - agents will break off from here
                engagementDist = Mathf.Min(faction.battleLineEngagementDistance, maxAttackRange + 0.5f);
            }
            else if (faction.combatRangeType == CombatRangeType.Ranged)
            {
                maxAttackRange = faction.rangedMaxDistance;
                engagementDist = Mathf.Min(faction.battleLineEngagementDistance, maxAttackRange - 0.3f);
            }
            else // Hybrid
            {
                // For hybrid, use the larger range to keep options open
                maxAttackRange = Mathf.Max(faction.rangedMaxDistance, faction.meleeMaxDistance);
                engagementDist = Mathf.Min(faction.battleLineEngagementDistance, maxAttackRange - 0.3f);
            }
            
            if (!state.isInitialized)
            {
                // Initialize at current distance
                state.currentAdvanceDistance = distanceToTarget;
                state.isInitialized = true;
            }
            else if (faction.battleLineAutoAdvance && state.currentAdvanceDistance > engagementDist)
            {
                // Slowly advance the line
                // Melee lines advance faster to close the gap
                float advanceSpeed = faction.combatRangeType == CombatRangeType.Melee 
                    ? faction.battleLineAdvanceSpeed * 1.5f 
                    : faction.battleLineAdvanceSpeed;
                state.currentAdvanceDistance -= advanceSpeed * Time.deltaTime;
                state.currentAdvanceDistance = Mathf.Max(state.currentAdvanceDistance, engagementDist);
            }
            
            // Line center is positioned at advance distance from target
            state.lineCenter = targetPosition - state.lineDirection * state.currentAdvanceDistance;
        }
        
        /// <summary>
        /// Calculates position for an agent in the main battle line ranks.
        /// </summary>
        private Vector3 GetBattleLineRankPosition(CrowdAgent agent, Vector3 targetPosition, int lineIndex, int totalLineAgents, BattleLineState state, EnemyCrowd faction, out bool shouldStop)
        {
            shouldStop = false;
            
            int agentsPerRank = faction.battleLineAgentsPerRank;
            float spacing = faction.battleLineSpacing;
            float rankDepth = faction.battleLineRankDepth;
            float jitter = faction.battleLinePositionJitter;
            
            // Calculate which rank and position within rank
            int rank = lineIndex / agentsPerRank;
            int positionInRank = lineIndex % agentsPerRank;
            
            // Calculate how many agents are in this rank
            int agentsInThisRank = Mathf.Min(agentsPerRank, totalLineAgents - (rank * agentsPerRank));
            
            // Center the rank
            float rankWidth = (agentsInThisRank - 1) * spacing;
            float startOffset = -rankWidth / 2f;
            
            // Position along the rank (left to right)
            float lateralOffset = startOffset + positionInRank * spacing;
            
            // Apply jitter for natural look (use agent index as seed for consistency)
            if (jitter > 0f)
            {
                // Deterministic random based on agent index
                float jitterX = (Mathf.Sin(agent.index * 12.9898f) * 0.5f + 0.5f) * 2f - 1f;
                float jitterZ = (Mathf.Sin(agent.index * 78.233f) * 0.5f + 0.5f) * 2f - 1f;
                lateralOffset += jitterX * jitter;
            }
            
            // Distance from line center (further back for rear ranks)
            float depthOffset = rank * rankDepth;
            
            // Calculate final position
            Vector3 position = state.lineCenter
                             + state.lineRight * lateralOffset
                             - state.lineDirection * depthOffset;
            
            // Add jitter to depth as well
            if (jitter > 0f)
            {
                float jitterDepth = (Mathf.Sin(agent.index * 43.758f) * 0.5f + 0.5f) * 2f - 1f;
                position -= state.lineDirection * (jitterDepth * jitter * 0.5f);
            }
            
            // Check if we're close enough to stop
            Vector3 agentPos = GetAgentPosition(agent);
            float distToPosition = Vector3.Distance(agentPos, position);
            float distToTarget = Vector3.Distance(agentPos, targetPosition);
            
            // Determine effective range type for this agent
            CombatRangeType effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);
            
            // MELEE AGENTS: Always move toward target for close combat
            // Battle Line for melee means "advance in formation until close, then engage"
            if (effectiveRangeType == CombatRangeType.Melee)
            {
                float meleeEngageDistance = faction.meleeMaxDistance + 0.5f; // Start engaging just outside attack range
                
                // CRITICAL FIX: For melee, check distance to INDIVIDUAL attack target, not faction center
                // Battle Line uses faction center for formation positioning, but melee engagement
                // should be based on the assigned individual enemy target
                Vector3 individualTargetPos = targetPosition; // Default to faction center
                float distToIndividualTarget = distToTarget;
                bool hasIndividualTarget = false;
                
                if (agent.hasAttackTargetPosition && agent.attackTargetPosition != Vector3.zero)
                {
                    individualTargetPos = agent.attackTargetPosition;
                    distToIndividualTarget = Vector3.Distance(agentPos, individualTargetPos);
                    hasIndividualTarget = true;
                }
                
                // DEBUG: Log melee battle line targeting for first few agents
                if (agent.index < 5 && Time.frameCount % 300 == 0)
                {
                    Debug.Log($"[BattleLineMelee] Agent {agent.index}: distToFactionCenter={distToTarget:F1}m, distToIndividualTarget={distToIndividualTarget:F1}m, hasIndividualTarget={hasIndividualTarget}, meleeRange=[{faction.meleeMinDistance:F1}-{faction.meleeMaxDistance:F1}], rank={rank}");
                }
                
                // Check if in attack range of INDIVIDUAL target - stop and fight
                if (distToIndividualTarget >= faction.meleeMinDistance && distToIndividualTarget <= faction.meleeMaxDistance)
                {
                    shouldStop = true;
                    return agentPos; // Already in range, stay and fight
                }
                
                // Calculate optimal melee position toward INDIVIDUAL target (not faction center)
                float optimalMeleeDistance = (faction.meleeMinDistance + faction.meleeMaxDistance) * 0.5f;
                Vector3 dirToIndividualTarget = (individualTargetPos - agentPos).normalized;
                Vector3 meleePosition = individualTargetPos - dirToIndividualTarget * optimalMeleeDistance;
                
                // Check against INDIVIDUAL target distance for melee engagement
                if (distToIndividualTarget <= meleeEngageDistance)
                {
                    // Close enough to individual target - move directly to melee attack position
                    shouldStop = false;
                    return meleePosition;
                }
                
                // Not yet close enough to break formation
                // Check if formation position is between us and target, or if we should bypass it
                float distFormationToTarget = Vector3.Distance(position, individualTargetPos);
                
                // If auto-advance is OFF or formation is too far, melee agents should still advance
                // Front rank always advances toward target; rear ranks follow formation but move forward
                if (rank == 0 || !faction.battleLineAutoAdvance)
                {
                    // Front rank OR no auto-advance: Move toward optimal melee position
                    // Blend between formation and direct approach based on distance
                    float blendFactor = Mathf.Clamp01((distToIndividualTarget - meleeEngageDistance) / 10f); // Blend over 10m
                    
                    // When far: follow formation position (blendFactor near 1)
                    // When closer: go directly to target (blendFactor near 0)
                    if (faction.battleLineAutoAdvance)
                    {
                        // With auto-advance, blend toward target
                        Vector3 advancePosition = Vector3.Lerp(meleePosition, position, blendFactor * 0.5f);
                        shouldStop = false;
                        return advancePosition;
                    }
                    else
                    {
                        // NO auto-advance: Melee agents MUST advance on their own
                        // Move directly toward target, maintaining lateral formation spread
                        Vector3 lateralOffset3D = state.lineRight * (lateralOffset + (rank * spacing * 0.3f));
                        Vector3 directAdvancePosition = meleePosition + lateralOffset3D * 0.5f;
                        shouldStop = false;
                        return directAdvancePosition;
                    }
                }
                else
                {
                    // Rear ranks with auto-advance ON: follow formation
                    shouldStop = distToPosition < 0.5f;
                    return position;
                }
            }
            else
            {
                // RANGED/HYBRID RANGED AGENTS: Maintain formation, stop when in range
                if (rank == 0)
                {
                    float attackRange = faction.combatRangeType == CombatRangeType.Ranged ? faction.rangedMaxDistance : faction.meleeMaxDistance;
                    
                    if (distToTarget <= attackRange)
                    {
                        shouldStop = true;
                        // Let them attack from current position when in range
                        return agentPos;
                    }
                }
            }
            
            shouldStop = distToPosition < 0.5f;
            return position;
        }
        
        /// <summary>
        /// Calculates position for flanking units that circle around the enemy.
        /// </summary>
        private Vector3 GetBattleLineFlankerPosition(CrowdAgent agent, Vector3 targetPosition, int flankerIndex, int totalFlankers, BattleLineState state, EnemyCrowd faction, out bool shouldStop)
        {
            shouldStop = false;
            
            float flankingWidth = faction.battleLineFlankingWidth;
            float engagementDist = faction.battleLineEngagementDistance;
            
            // Split flankers between left and right
            bool isRightFlank = flankerIndex % 2 == 0;
            int sideIndex = flankerIndex / 2;
            int flankersPerSide = Mathf.CeilToInt(totalFlankers / 2f);
            
            // Angle for this flanker (spread from 45 to 135 degrees on each side)
            float baseAngle = isRightFlank ? 45f : -45f;
            float spreadAngle = flankersPerSide > 1 ? (90f / (flankersPerSide - 1)) * sideIndex : 0f;
            float totalAngle = baseAngle + (isRightFlank ? spreadAngle : -spreadAngle);
            
            // Calculate flanking position
            float radians = totalAngle * Mathf.Deg2Rad;
            Vector3 flankDirection = Quaternion.Euler(0f, totalAngle, 0f) * (-state.lineDirection);
            
            // Flankers position at engagement distance from target, but from the side
            Vector3 position = targetPosition + flankDirection * (engagementDist + flankingWidth * 0.3f);
            
            // Add some variance based on flanker index
            float jitter = faction.battleLinePositionJitter;
            if (jitter > 0f)
            {
                float jitterX = (Mathf.Sin(agent.index * 12.9898f) * 0.5f + 0.5f) * 2f - 1f;
                float jitterZ = (Mathf.Sin(agent.index * 78.233f) * 0.5f + 0.5f) * 2f - 1f;
                position += new Vector3(jitterX, 0f, jitterZ) * jitter;
            }
            
            // Check if close enough to attack
            Vector3 agentPos = GetAgentPosition(agent);
            float distToTarget = Vector3.Distance(agentPos, targetPosition);
            
            // Determine effective range type for this agent
            CombatRangeType effectiveRangeType = GetEffectiveCombatRangeType(agent, faction);
            
            // MELEE FLANKERS: Always advance toward target for close combat
            if (effectiveRangeType == CombatRangeType.Melee)
            {
                // CRITICAL FIX: Use INDIVIDUAL target distance for melee engagement check
                Vector3 individualTargetPos = targetPosition;
                float distToIndividualTarget = distToTarget;
                
                if (agent.hasAttackTargetPosition && agent.attackTargetPosition != Vector3.zero)
                {
                    individualTargetPos = agent.attackTargetPosition;
                    distToIndividualTarget = Vector3.Distance(agentPos, individualTargetPos);
                }
                
                // Check if in attack range of INDIVIDUAL target - stop and fight
                if (distToIndividualTarget >= faction.meleeMinDistance && distToIndividualTarget <= faction.meleeMaxDistance)
                {
                    shouldStop = true;
                    return agentPos; // In range, fight!
                }
                
                // Calculate optimal melee position toward INDIVIDUAL target (from flank angle)
                float optimalMeleeDistance = (faction.meleeMinDistance + faction.meleeMaxDistance) * 0.5f;
                Vector3 dirToIndividualTarget = (individualTargetPos - agentPos).normalized;
                // For flankers, blend between direct approach and flanking angle
                Vector3 meleePosition = individualTargetPos - dirToIndividualTarget * optimalMeleeDistance;
                
                float meleeEngageDistance = faction.meleeMaxDistance + 0.5f;
                
                if (distToIndividualTarget <= meleeEngageDistance)
                {
                    // Close enough to individual target - move directly to melee attack position
                    shouldStop = false;
                    return meleePosition;
                }
                
                // Not yet close - if auto-advance is OFF, flankers must advance on their own
                if (!faction.battleLineAutoAdvance)
                {
                    // Move toward the flanking melee position
                    shouldStop = false;
                    return meleePosition;
                }
                
                // With auto-advance, follow formation but blend toward target
                float distToPosition = Vector3.Distance(agentPos, position);
                shouldStop = distToPosition < 1f;
                return position;
            }
            else
            {
                // Ranged flankers stop when in range
                float attackRange = faction.combatRangeType == CombatRangeType.Ranged ? faction.rangedMaxDistance : faction.meleeMaxDistance;
                
                if (distToTarget <= attackRange)
                {
                    shouldStop = true;
                    return agentPos;
                }
            }
            
            float distToPos = Vector3.Distance(agentPos, position);
            shouldStop = distToPos < 1f;
            return position;
        }
        
        /// <summary>
        /// Gets the index of an agent within its faction (0-based).
        /// </summary>
        private int GetAgentIndexWithinFaction(CrowdAgent agent, int factionIndex)
        {
            int index = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent other = _agents[i];
                if (other == null || other.isDead || other.enemyCrowdIndex != factionIndex)
                    continue;
                
                if (other == agent)
                    return index;
                
                index++;
            }
            return 0;
        }
        
        /// <summary>
        /// Counts total living agents in a faction.
        /// </summary>
        private int CountFactionAgents(int factionIndex)
        {
            int count = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                CrowdAgent other = _agents[i];
                if (other == null || other.isDead || other.enemyCrowdIndex != factionIndex)
                    continue;
                count++;
            }
            return count;
        }
        
        /// <summary>
        /// Resets battle line state for a faction (call when spawning new groups).
        /// </summary>
        public void ResetBattleLineState(int factionIndex)
        {
            if (_battleLineStates.ContainsKey(factionIndex))
            {
                _battleLineStates[factionIndex].isInitialized = false;
            }
        }
        
        #endregion
        
        #region Helper Methods

        private static Vector3 GetAgentPosition(CrowdAgent agent)
        {
            return agent != null ? agent.WorldPosition : Vector3.zero;
        }
        
        /// <summary>
        /// Adjusts agent position to maintain spacing from other agents of the same faction.
        /// Prevents clumping by pushing agents away from nearby allies.
        /// </summary>
        private Vector3 ApplyAgentSpacing(CrowdAgent agent, Vector3 desiredPosition, int agentIndex, float minSpacing)
        {
            if (agent?.navigationAgent == null || minSpacing <= 0f)
                return desiredPosition;
            
            Vector3 currentPos = GetAgentPosition(agent);
            Vector3 adjustedPosition = desiredPosition;
            Vector3 separationForce = Vector3.zero;
            int nearbyCount = 0;
            
            // Check spacing against other agents of the same faction
            float spacingCheckRadius = minSpacing * 3f; // Check wider radius for better separation
            float minSpacingSqr = minSpacing * minSpacing;
            float checkRadiusSqr = spacingCheckRadius * spacingCheckRadius;
            
            // Early exit limit - only check nearby agents within radius to avoid O(n²) nightmare
            // With spatial rejection, this gives good balance between performance and spacing quality
            const int maxChecks = 50; // Check up to 50 nearby agents for spacing
            int checksPerformed = 0;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                if (i == agentIndex)
                    continue;
                
                CrowdAgent otherAgent = _agents[i];
                
                // Only check spacing against same faction
                if (otherAgent == null || otherAgent.isDead || 
                    otherAgent.enemyCrowdIndex != agent.enemyCrowdIndex ||
                    otherAgent.navigationAgent == null)
                    continue;
                
                Vector3 otherPos = GetAgentPosition(otherAgent);
                Vector3 toOther = otherPos - currentPos;
                float distanceSqr = toOther.sqrMagnitude;
                
                // Skip agents too far away (spatial early rejection)
                if (distanceSqr > checkRadiusSqr)
                    continue;
                
                checksPerformed++;
                
                // If too close, apply separation force
                if (distanceSqr < minSpacingSqr && distanceSqr > 0.01f)
                {
                    float distance = Mathf.Sqrt(distanceSqr);
                    // Stronger force when closer
                    float forceMagnitude = (minSpacing - distance) / minSpacing;
                    separationForce -= toOther.normalized * forceMagnitude;
                    nearbyCount++;
                }
                
                // Early exit after checking enough nearby agents
                if (checksPerformed >= maxChecks)
                    break;
            }
            
            // Apply separation force if we found nearby agents
            if (nearbyCount > 0)
            {
                // Don't average - keep force strong to overcome NavMesh convergence
                separationForce.y = 0f; // Keep on ground plane
                
                // Stronger separation force to work in harmony with NavMesh
                // Scale up with more nearby agents for stronger push
                float separationStrength = Mathf.Clamp(nearbyCount / 2f, 0.5f, 2f); // 0.5x to 2x strength
                adjustedPosition = desiredPosition + separationForce * minSpacing * separationStrength;
                adjustedPosition.y = desiredPosition.y; // Maintain height
            }
            
            return adjustedPosition;
        }
        
        private void ProcessAttackQueue()
        {
            // Allow next agents in queue to attack
            // Note: Per-faction max attackers are checked in CanAgentAttack, not here
            while (_attackQueue.Count > 0)
            {
                int nextAgentIndex = _attackQueue.Dequeue();
                
                // Verify agent is still valid and in range
                if (nextAgentIndex >= 0 && nextAgentIndex < _agents.Count)
                {
                    var agent = _agents[nextAgentIndex];
                    if (agent != null && !agent.isDead && agent.hasAttackTargetPosition)
                    {
                        // Agent can now attack (will be processed in next combat update)
                        // Don't add to activeAttackers here - let BeginAttack handle it
                        break;
                    }
                }
            }
        }
        
        private int CountAgentsInCombat()
        {
            int count = 0;
            foreach (var agent in _agents)
            {
                if (agent != null && !agent.isDead && agent.hasAttackTargetPosition)
                    count++;
            }
            return Mathf.Max(3, count);
        }
        
        #endregion
        
        #region Debug Info
        
        /// <summary>
        /// Gets current attack queue statistics for debugging.
        /// </summary>
        public (int active, int queued, int slots) GetQueueStats()
        {
            // Slots are now calculated per-faction, not globally
            return (_activeAttackers.Count, _attackQueue.Count, -1);
        }
        
        #endregion
    }
}
