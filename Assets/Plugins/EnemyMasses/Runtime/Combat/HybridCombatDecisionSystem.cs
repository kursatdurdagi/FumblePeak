using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// AI decision system for Hybrid combat agents.
    /// Uses utility-based scoring to determine whether to engage in melee or ranged combat.
    /// </summary>
    public class HybridCombatDecisionSystem
    {
        private readonly System.Random _random;
        
        public HybridCombatDecisionSystem(int seed = 0)
        {
            _random = seed == 0 ? new System.Random() : new System.Random(seed);
        }
        
        /// <summary>
        /// Evaluates whether a Hybrid agent should switch combat modes.
        /// Returns the recommended mode based on multiple factors including distance, personality, and tactical considerations.
        /// </summary>
        public CombatRangeType EvaluateCombatMode(
            CrowdAgent agent,
            float currentDistance,
            EnemyCrowd faction,
            int nearbyAlliesInMelee,
            int nearbyAlliesInRanged,
            float timeSinceLastModeSwitch)
        {
            if (faction == null || faction.combatRangeType != CombatRangeType.Hybrid)
                return CombatRangeType.Melee;
            
            // Calculate utility scores for both modes
            float meleeUtility = CalculateMeleeUtility(
                agent, 
                currentDistance, 
                faction, 
                nearbyAlliesInMelee, 
                nearbyAlliesInRanged,
                timeSinceLastModeSwitch
            );
            
            float rangedUtility = CalculateRangedUtility(
                agent, 
                currentDistance, 
                faction, 
                nearbyAlliesInMelee, 
                nearbyAlliesInRanged,
                timeSinceLastModeSwitch
            );
            
            // Add hysteresis to prevent rapid mode switching
            float switchBias = 0.15f; // Current mode needs to be 15% worse to switch
            if (agent.activeHybridMode == CombatRangeType.Melee)
            {
                meleeUtility += switchBias;
            }
            else
            {
                rangedUtility += switchBias;
            }
            
            // Choose mode with higher utility
            return meleeUtility > rangedUtility ? CombatRangeType.Melee : CombatRangeType.Ranged;
        }
        
        /// <summary>
        /// Calculates utility score for engaging in melee combat (0-1 range).
        /// </summary>
        private float CalculateMeleeUtility(
            CrowdAgent agent,
            float distance,
            EnemyCrowd faction,
            int nearbyAlliesInMelee,
            int nearbyAlliesInRanged,
            float timeSinceLastSwitch)
        {
            float utility = 0f;
            
            // 1. Distance factor - melee is better at close range
            float optimalMeleeDistance = faction.meleeMinDistance;
            float maxMeleeDistance = faction.meleeMaxDistance;
            float distanceScore = 1f - Mathf.Clamp01(distance / (maxMeleeDistance * 1.5f));
            utility += distanceScore * 0.35f; // 35% weight
            
            // 2. Personality/Aggression factor - each agent has inherent preference
            float agentAggressionScore = GetAgentAggressionScore(agent.index);
            utility += agentAggressionScore * 0.20f; // 20% weight
            
            // 3. Tactical balance - prefer spreading out between melee/ranged
            // If too many allies are already in melee, ranged becomes more attractive
            int totalNearby = nearbyAlliesInMelee + nearbyAlliesInRanged;
            float meleeRatio = totalNearby > 0 ? (float)nearbyAlliesInMelee / totalNearby : 0.5f;
            float balanceScore = 1f - Mathf.Clamp01(meleeRatio); // Lower score if too many in melee
            utility += balanceScore * 0.15f; // 15% weight
            
            // 4. Opportunity factor - melee is better when target is exposed/close
            float opportunityScore = distance < optimalMeleeDistance * 1.2f ? 1f : 0f;
            utility += opportunityScore * 0.15f; // 15% weight
            
            // 5. Commitment factor - don't switch modes too quickly
            if (agent.activeHybridMode == CombatRangeType.Melee && timeSinceLastSwitch < 3f)
            {
                utility += 0.15f; // 15% bonus for staying in current mode
            }
            
            return Mathf.Clamp01(utility);
        }
        
        /// <summary>
        /// Calculates utility score for engaging in ranged combat (0-1 range).
        /// </summary>
        private float CalculateRangedUtility(
            CrowdAgent agent,
            float distance,
            EnemyCrowd faction,
            int nearbyAlliesInMelee,
            int nearbyAlliesInRanged,
            float timeSinceLastSwitch)
        {
            float utility = 0f;
            
            // 1. Distance factor - ranged is better at medium/long range
            float minRangedDistance = faction.meleeMaxDistance + 2f;
            float optimalRangedDistance = faction.rangedPreferredDistance;
            float distanceFromOptimal = Mathf.Abs(distance - optimalRangedDistance);
            float distanceScore = 1f - Mathf.Clamp01(distanceFromOptimal / optimalRangedDistance);
            
            // Bonus for being beyond melee range
            if (distance > minRangedDistance)
            {
                distanceScore = Mathf.Max(distanceScore, 0.7f);
            }
            utility += distanceScore * 0.35f; // 35% weight
            
            // 2. Personality/Caution factor - each agent has inherent preference
            float agentCautionScore = 1f - GetAgentAggressionScore(agent.index);
            utility += agentCautionScore * 0.20f; // 20% weight
            
            // 3. Tactical balance - prefer spreading out between melee/ranged
            int totalNearby = nearbyAlliesInMelee + nearbyAlliesInRanged;
            float rangedRatio = totalNearby > 0 ? (float)nearbyAlliesInRanged / totalNearby : 0.5f;
            float balanceScore = 1f - Mathf.Clamp01(rangedRatio); // Lower score if too many in ranged
            utility += balanceScore * 0.15f; // 15% weight
            
            // 4. Safety factor - ranged is better when many allies are in melee (stay back)
            if (nearbyAlliesInMelee >= 3)
            {
                utility += 0.15f; // 15% bonus for providing ranged support
            }
            
            // 5. Commitment factor - don't switch modes too quickly
            if (agent.activeHybridMode == CombatRangeType.Ranged && timeSinceLastSwitch < 3f)
            {
                utility += 0.15f; // 15% bonus for staying in current mode
            }
            
            return Mathf.Clamp01(utility);
        }
        
        /// <summary>
        /// Gets a consistent aggression score for an agent based on their index.
        /// Returns value between 0 (cautious/ranged preference) and 1 (aggressive/melee preference).
        /// </summary>
        private float GetAgentAggressionScore(int agentIndex)
        {
            // Use deterministic hash to give each agent a consistent personality
            // This ensures the same agent always has the same preference
            int hash = agentIndex * 73856093; // Prime number for good distribution
            float normalized = ((hash & 0x7FFFFFFF) % 1000) / 1000f; // Normalize to 0-1
            
            // Apply curve to create more varied distribution
            // This creates ~30% highly aggressive, ~30% highly cautious, ~40% balanced
            if (normalized < 0.3f)
            {
                return Mathf.Pow(normalized / 0.3f, 0.5f) * 0.4f; // Cautious (0-0.4)
            }
            else if (normalized > 0.7f)
            {
                return 0.6f + Mathf.Pow((normalized - 0.7f) / 0.3f, 0.5f) * 0.4f; // Aggressive (0.6-1.0)
            }
            else
            {
                return 0.4f + ((normalized - 0.3f) / 0.4f) * 0.2f; // Balanced (0.4-0.6)
            }
        }
        
        /// <summary>
        /// Counts nearby allies by their combat mode to inform tactical decisions.
        /// </summary>
        public void CountNearbyAlliesByMode(
            CrowdAgent agent,
            Vector3 agentPosition,
            System.Collections.Generic.List<CrowdAgent> allAgents,
            int agentFactionIndex,
            float checkRadius,
            out int nearbyMelee,
            out int nearbyRanged)
        {
            nearbyMelee = 0;
            nearbyRanged = 0;
            
            float checkRadiusSqr = checkRadius * checkRadius;
            
            for (int i = 0; i < allAgents.Count; i++)
            {
                CrowdAgent other = allAgents[i];
                
                // Skip self, dead agents, and different factions
                if (other.index == agent.index || 
                    other.isDead || 
                    other.enemyCrowdIndex != agentFactionIndex)
                    continue;
                
                // Check if within radius
                Vector3 otherPosition = other.navigationAgent != null
                    ? other.WorldPosition
                    : (other.transform != null ? other.transform.position : Vector3.zero);
                float distanceSqr = Vector3.SqrMagnitude(otherPosition - agentPosition);
                
                if (distanceSqr <= checkRadiusSqr)
                {
                    // Get other agent's faction to check their combat mode
                    if (other.enemyCrowdIndex >= 0)
                    {
                        if (other.activeHybridMode == CombatRangeType.Melee)
                        {
                            nearbyMelee++;
                        }
                        else if (other.activeHybridMode == CombatRangeType.Ranged)
                        {
                            nearbyRanged++;
                        }
                    }
                }
            }
        }
    }
}
