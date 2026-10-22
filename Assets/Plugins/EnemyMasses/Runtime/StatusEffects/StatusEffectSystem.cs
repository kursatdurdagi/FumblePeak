using UnityEngine;
using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime.Networking;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Core system for managing status effects across all agents.
    /// Provides batched processing for performance with thousands of agents.
    /// </summary>
    public class StatusEffectSystem
    {
        // Reference to main controller
        private EnemyMassesCrowdController controller;
        
        // Callback delegates for integration with other systems
        public delegate void OnDamageTickDelegate(CrowdAgent agent, int slotIndex, float damage, StatusEffectType damageType);
        public delegate void OnHealTickDelegate(CrowdAgent agent, int slotIndex, float heal, StatusEffectType healType);
        public delegate void OnEffectAppliedDelegate(CrowdAgent agent, int slotIndex, StatusEffectType type);
        public delegate void OnEffectRemovedDelegate(CrowdAgent agent, int slotIndex, StatusEffectType type, bool wasDispelled);
        public delegate void OnEffectExpiredDelegate(CrowdAgent agent, int slotIndex, StatusEffectType type);
        
        public OnDamageTickDelegate OnDamageTick;
        public OnHealTickDelegate OnHealTick;
        public OnEffectAppliedDelegate OnEffectApplied;
        public OnEffectRemovedDelegate OnEffectRemoved;
        public OnEffectExpiredDelegate OnEffectExpired;
        
        // Reusable buffer for effect slots (avoid allocations)
        private int[] effectSlotBuffer = new int[CrowdAgent.MAX_STATUS_EFFECTS];
        
        // Performance tracking
        private int effectsProcessedThisFrame;
        private int dotsProcessedThisFrame;
        
        public StatusEffectSystem(EnemyMassesCrowdController controller)
        {
            this.controller = controller;
        }
        
        /// <summary>
        /// Apply a status effect to an agent from a config.
        /// Returns the slot index where effect was placed, or -1 if failed.
        /// </summary>
        public int ApplyEffect(CrowdAgent agent, StatusEffectConfig config, int sourceAgentIndex = -1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return -1;
            if (agent == null || config == null) return -1;
            if (agent.isDead) return -1;
            
            // Check CC immunity
            if (HasCCImmunity(agent) && StatusEffectHelper.IsCrowdControl(config.effectType))
            {
                return -1;
            }
            
            // Check for existing effect of same type
            int existingSlot = agent.FindEffectOfType(config.effectType);
            
            if (existingSlot >= 0)
            {
                // Handle stacking or refreshing
                return HandleExistingEffect(agent, existingSlot, config, sourceAgentIndex);
            }
            else
            {
                // Apply new effect
                return ApplyNewEffect(agent, config, sourceAgentIndex);
            }
        }
        
        /// <summary>
        /// Apply a status effect directly without config (for runtime generation).
        /// </summary>
        public int ApplyEffect(CrowdAgent agent, StatusEffectType type, float duration, float value, 
                               float tickInterval = 1f, int maxStacks = 1, int sourceAgentIndex = -1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return -1;
            if (agent == null) return -1;
            if (agent.isDead) return -1;
            
            // Check CC immunity
            if (HasCCImmunity(agent) && StatusEffectHelper.IsCrowdControl(type))
            {
                return -1;
            }
            
            // Check for existing effect
            int existingSlot = agent.FindEffectOfType(type);
            
            if (existingSlot >= 0)
            {
                // Refresh duration on existing effect
                ref ActiveStatusEffect existing = ref agent.activeStatusEffects[existingSlot];
                existing.expireTime = duration > 0 ? Time.time + duration : float.MaxValue;
                ApplyImmediateFlags(agent, type);
                agent.statusEffectsDirty = true;
                return existingSlot;
            }
            
            // Find free slot
            int slot = agent.FindFreeEffectSlot();
            if (slot < 0) return -1;
            
            // Create new effect
            float currentTime = Time.time;
            agent.activeStatusEffects[slot] = new ActiveStatusEffect
            {
                type = type,
                isActive = true,
                expireTime = duration > 0 ? currentTime + duration : float.MaxValue,
                lastTickTime = currentTime,
                tickValue = value,
                tickInterval = tickInterval,
                modifierValue = value,
                sourceAgentIndex = sourceAgentIndex,
                stacks = 1,
                maxStacks = maxStacks
            };
            
            agent.activeEffectCount++;
            ApplyImmediateFlags(agent, type);
            agent.statusEffectsDirty = true;
            
            OnEffectApplied?.Invoke(agent, slot, type);
            
            return slot;
        }
        
        private int HandleExistingEffect(CrowdAgent agent, int slot, StatusEffectConfig config, int sourceAgentIndex)
        {
            ref ActiveStatusEffect existing = ref agent.activeStatusEffects[slot];
            float currentTime = Time.time;
            
            if (config.canStack && existing.stacks < existing.maxStacks)
            {
                // Add a stack
                existing.stacks++;
                existing.modifierValue = config.value * existing.stacks;
                existing.tickValue = config.value * existing.stacks;
                
                switch (config.stackBehavior)
                {
                    case StackBehavior.Refresh:
                        existing.expireTime = config.duration > 0 ? currentTime + config.duration : float.MaxValue;
                        break;
                    case StackBehavior.Extend:
                        existing.expireTime += config.duration;
                        break;
                    // Independent stacking would need separate slots - not supported with simple stacking
                }
            }
            else
            {
                // Just refresh duration (default behavior for non-stackable)
                existing.expireTime = config.duration > 0 ? currentTime + config.duration : float.MaxValue;
            }
            
            existing.sourceAgentIndex = sourceAgentIndex;
            ApplyImmediateFlags(agent, config.effectType);
            agent.statusEffectsDirty = true;
            
            return slot;
        }
        
        private int ApplyNewEffect(CrowdAgent agent, StatusEffectConfig config, int sourceAgentIndex)
        {
            int slot = agent.FindFreeEffectSlot();
            if (slot < 0) return -1;
            
            agent.activeStatusEffects[slot] = config.CreateActiveEffect(Time.time, sourceAgentIndex);
            agent.activeEffectCount++;
            ApplyImmediateFlags(agent, config.effectType);
            agent.statusEffectsDirty = true;
            
            OnEffectApplied?.Invoke(agent, slot, config.effectType);
            
            return slot;
        }

        private static void ApplyImmediateFlags(CrowdAgent agent, StatusEffectType type)
        {
            if (agent == null) return;

            if (type == StatusEffectType.CCImmune)
            {
                agent.isCCImmune = true;
            }
            else if (type == StatusEffectType.Invulnerable)
            {
                agent.isInvulnerable = true;
            }
            else if (type == StatusEffectType.DisplacementImmune)
            {
                agent.isDisplacementImmune = true;
            }
        }

        private static bool HasCCImmunity(CrowdAgent agent)
        {
            if (agent == null) return false;
            return agent.isCCImmune || agent.HasEffect(StatusEffectType.CCImmune);
        }
        
        /// <summary>
        /// Remove a specific effect by slot index.
        /// </summary>
        public void RemoveEffect(CrowdAgent agent, int slot, bool wasDispelled = false)
        {
            if (agent == null || slot < 0 || slot >= CrowdAgent.MAX_STATUS_EFFECTS) return;
            if (!agent.activeStatusEffects[slot].isActive) return;
            
            StatusEffectType type = agent.activeStatusEffects[slot].type;
            
            agent.activeStatusEffects[slot].Clear();
            agent.activeEffectCount = Mathf.Max(0, agent.activeEffectCount - 1);
            agent.statusEffectsDirty = true;
            
            OnEffectRemoved?.Invoke(agent, slot, type, wasDispelled);
        }
        
        /// <summary>
        /// Remove all effects of a specific type.
        /// </summary>
        public int RemoveEffectsOfType(CrowdAgent agent, StatusEffectType type, bool wasDispelled = false)
        {
            if (agent == null) return 0;
            
            int removed = 0;
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive && agent.activeStatusEffects[i].type == type)
                {
                    RemoveEffect(agent, i, wasDispelled);
                    removed++;
                }
            }
            return removed;
        }
        
        /// <summary>
        /// Dispel (remove) all negative effects from an agent.
        /// </summary>
        public int DispelNegativeEffects(CrowdAgent agent)
        {
            if (agent == null) return 0;
            
            int dispelled = 0;
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive && 
                    StatusEffectHelper.IsNegativeEffect(agent.activeStatusEffects[i].type))
                {
                    RemoveEffect(agent, i, wasDispelled: true);
                    dispelled++;
                }
            }
            return dispelled;
        }
        
        /// <summary>
        /// Dispel (remove) all positive effects from an agent.
        /// </summary>
        public int DispelPositiveEffects(CrowdAgent agent)
        {
            if (agent == null) return 0;
            
            int dispelled = 0;
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive && 
                    !StatusEffectHelper.IsNegativeEffect(agent.activeStatusEffects[i].type))
                {
                    RemoveEffect(agent, i, wasDispelled: true);
                    dispelled++;
                }
            }
            return dispelled;
        }
        
        /// <summary>
        /// Dispel all effects of a specific category.
        /// </summary>
        public int DispelCategory(CrowdAgent agent, StatusEffectCategory category)
        {
            if (agent == null) return 0;
            
            int dispelled = 0;
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive && 
                    StatusEffectHelper.GetCategory(agent.activeStatusEffects[i].type) == category)
                {
                    RemoveEffect(agent, i, wasDispelled: true);
                    dispelled++;
                }
            }
            return dispelled;
        }
        
        /// <summary>
        /// Remove all effects from an agent (on death, despawn, etc).
        /// </summary>
        public void ClearAllEffects(CrowdAgent agent)
        {
            if (agent == null) return;
            
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                if (agent.activeStatusEffects[i].isActive)
                {
                    agent.activeStatusEffects[i].Clear();
                }
            }
            agent.activeEffectCount = 0;
            agent.statusEffectsDirty = true;
            agent.ResetCachedModifiers();
        }
        
        /// <summary>
        /// Process all status effects for a single agent.
        /// Call this from the main update loop for each agent.
        /// </summary>
        public void ProcessAgentEffects(CrowdAgent agent, float currentTime)
        {
            if (agent == null || agent.activeStatusEffects == null || agent.activeEffectCount == 0)
            {
                // Ensure modifiers are reset if no effects
                if (agent != null && agent.statusEffectsDirty)
                {
                    RecalculateCachedModifiers(agent);
                }
                return;
            }
            
            bool anyExpired = false;
            
            // Process each active effect
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                ref ActiveStatusEffect effect = ref agent.activeStatusEffects[i];
                if (!effect.isActive) continue;
                
                effectsProcessedThisFrame++;
                
                // Check expiration
                if (currentTime >= effect.expireTime)
                {
                    StatusEffectType type = effect.type;
                    effect.Clear();
                    agent.activeEffectCount--;
                    anyExpired = true;
                    OnEffectExpired?.Invoke(agent, i, type);
                    continue;
                }
                
                // Process DOT/HOT ticks
                if (StatusEffectHelper.IsDamageOverTime(effect.type))
                {
                    ProcessDOTTick(agent, ref effect, i, currentTime);
                }
                else if (StatusEffectHelper.IsHealOverTime(effect.type))
                {
                    ProcessHOTTick(agent, ref effect, i, currentTime);
                }
            }
            
            if (anyExpired)
            {
                agent.statusEffectsDirty = true;
            }
            
            // Recalculate modifiers if dirty
            if (agent.statusEffectsDirty)
            {
                RecalculateCachedModifiers(agent);
            }
        }
        
        private void ProcessDOTTick(CrowdAgent agent, ref ActiveStatusEffect effect, int slot, float currentTime)
        {
            if (currentTime - effect.lastTickTime >= effect.tickInterval)
            {
                float damage = effect.tickValue;
                effect.lastTickTime = currentTime;
                dotsProcessedThisFrame++;
                
                // Notify for damage application (combat system will handle actual damage)
                OnDamageTick?.Invoke(agent, slot, damage, effect.type);
                
                // Check if damage breaks effect (like Sleep)
                // Note: actual effect removal happens when combat system applies damage
            }
        }
        
        private void ProcessHOTTick(CrowdAgent agent, ref ActiveStatusEffect effect, int slot, float currentTime)
        {
            if (currentTime - effect.lastTickTime >= effect.tickInterval)
            {
                float heal = effect.tickValue;
                effect.lastTickTime = currentTime;
                
                OnHealTick?.Invoke(agent, slot, heal, effect.type);
            }
        }
        
        /// <summary>
        /// Called when an agent takes damage. Checks for effects broken by damage.
        /// </summary>
        public void OnAgentTookDamage(CrowdAgent agent)
        {
            if (agent == null || agent.activeStatusEffects == null) return;
            
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                ref ActiveStatusEffect effect = ref agent.activeStatusEffects[i];
                if (!effect.isActive) continue;
                
                if (StatusEffectHelper.BrokenByDamage(effect.type))
                {
                    RemoveEffect(agent, i, wasDispelled: false);
                }
            }
        }
        
        /// <summary>
        /// Recalculate all cached modifiers from active effects.
        /// </summary>
        public void RecalculateCachedModifiers(CrowdAgent agent)
        {
            if (agent == null)
            {
                return;
            }
            
            // Reset to defaults
            agent.ResetCachedModifiers();
            
            if (agent.activeStatusEffects == null || agent.activeEffectCount == 0)
            {
                agent.statusEffectsDirty = false;
                return;
            }
            
            // Accumulate modifiers from all active effects
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS; i++)
            {
                ref ActiveStatusEffect effect = ref agent.activeStatusEffects[i];
                if (!effect.isActive) continue;
                
                ApplyModifierFromEffect(agent, ref effect);
            }
            
            agent.statusEffectsDirty = false;
        }
        
        private void ApplyModifierFromEffect(CrowdAgent agent, ref ActiveStatusEffect effect)
        {
            switch (effect.type)
            {
                // ═══ CROWD CONTROL ═══
                case StatusEffectType.Stun:
                case StatusEffectType.Freeze:
                case StatusEffectType.Sleep:
                    agent.canMove = false;
                    agent.canAttack = false;
                    break;
                    
                case StatusEffectType.Daze:
                    agent.canAttack = false;
                    agent.cachedMoveSpeedMultiplier *= 0.5f; // 50% slow
                    break;
                    
                case StatusEffectType.Stagger:
                    // Brief interrupt - handled by combat system resetting attack timer
                    agent.canAttack = false;
                    break;
                    
                case StatusEffectType.Fear:
                    agent.canAttack = false;
                    agent.isFeared = true;
                    agent.fearSourceAgentIndex = effect.sourceAgentIndex;
                    break;
                    
                case StatusEffectType.Taunt:
                    agent.isTaunted = true;
                    agent.tauntSourceAgentIndex = effect.sourceAgentIndex;
                    break;
                    
                case StatusEffectType.Slow:
                    agent.cachedMoveSpeedMultiplier *= effect.modifierValue; // Value should be 0-1
                    break;
                    
                case StatusEffectType.Root:
                    agent.canMove = false;
                    break;
                    
                case StatusEffectType.Silence:
                    // Prevents special abilities - checked by ability system
                    break;
                    
                // ═══ DEBUFFS ═══
                case StatusEffectType.ArmorBreak:
                    // Reduce armor (negative bonus = armor reduction)
                    // Value is the amount of armor to reduce (e.g., 50 = reduce armor by 50)
                    agent.bonusArmor -= effect.modifierValue;
                    // Also apply legacy multiplier for backward compatibility
                    if (effect.modifierValue > 1f)
                        agent.cachedDamageReceivedMultiplier *= effect.modifierValue;
                    break;
                    
                case StatusEffectType.Weaken:
                    agent.cachedDamageMultiplier *= effect.modifierValue; // Value < 1 = less damage dealt
                    break;
                    
                case StatusEffectType.Vulnerable:
                    agent.cachedDamageReceivedMultiplier *= effect.modifierValue; // Value > 1 = more damage taken
                    break;
                    
                case StatusEffectType.AttackSpeedSlow:
                    agent.cachedAttackSpeedMultiplier *= effect.modifierValue; // Value < 1 = slower attack
                    break;
                    
                case StatusEffectType.BlockBreak:
                    agent.cachedBlockChanceBonus -= effect.modifierValue; // Reduce block chance
                    break;
                    
                case StatusEffectType.ExposedWeakness:
                    // Already handled by receivedCriticalChanceModifier in hitbox system
                    agent.receivedCriticalChanceModifier *= effect.modifierValue;
                    break;
                    
                // ═══ BUFFS ═══
                case StatusEffectType.ArmorBuff:
                    // Add armor (positive bonus = armor increase)
                    // Value is the amount of armor to add (e.g., 50 = add 50 armor)
                    agent.bonusArmor += effect.modifierValue;
                    // Also apply legacy multiplier for backward compatibility
                    if (effect.modifierValue < 1f && effect.modifierValue > 0f)
                        agent.cachedDamageReceivedMultiplier *= effect.modifierValue;
                    break;
                    
                case StatusEffectType.HealthBuff:
                    // Handled separately - this increases max health
                    break;
                    
                case StatusEffectType.Regeneration:
                    // Handled by HOT processing
                    break;
                    
                case StatusEffectType.AttackSpeedBuff:
                    agent.cachedAttackSpeedMultiplier *= effect.modifierValue; // Value > 1 = faster attack
                    break;
                    
                case StatusEffectType.DamageBuff:
                    agent.cachedDamageMultiplier *= effect.modifierValue; // Value > 1 = more damage
                    break;
                    
                case StatusEffectType.BlockBuff:
                    agent.cachedBlockChanceBonus += effect.modifierValue; // Add to block chance
                    break;
                    
                case StatusEffectType.SpeedBuff:
                    agent.cachedMoveSpeedMultiplier *= effect.modifierValue; // Value > 1 = faster
                    break;
                    
                case StatusEffectType.CriticalBuff:
                    agent.criticalHitChanceModifier *= effect.modifierValue; // Integrate with hitbox system
                    break;
                    
                case StatusEffectType.Invulnerable:
                    agent.isInvulnerable = true;
                    break;
                    
                case StatusEffectType.CCImmune:
                    agent.isCCImmune = true;
                    break;
                    
                case StatusEffectType.DisplacementImmune:
                    agent.isDisplacementImmune = true;
                    break;
                    
                case StatusEffectType.Shield:
                    agent.cachedShieldAmount += effect.modifierValue;
                    break;
                    
                // ═══ RESISTANCE BUFFS ═══
                case StatusEffectType.MagicResistBuff:
                    agent.bonusMagicResist += effect.modifierValue;
                    break;
                    
                case StatusEffectType.FireResistBuff:
                    agent.bonusFireResistance += effect.modifierValue;
                    break;
                    
                case StatusEffectType.IceResistBuff:
                    agent.bonusIceResistance += effect.modifierValue;
                    break;
                    
                case StatusEffectType.LightningResistBuff:
                    agent.bonusLightningResistance += effect.modifierValue;
                    break;
                    
                case StatusEffectType.PoisonResistBuff:
                    agent.bonusPoisonResistance += effect.modifierValue;
                    break;
                    
                // ═══ RESISTANCE DEBUFFS ═══
                case StatusEffectType.MagicResistBreak:
                    agent.bonusMagicResist -= effect.modifierValue;
                    break;
                    
                case StatusEffectType.FireVulnerability:
                    agent.bonusFireResistance -= effect.modifierValue;
                    break;
                    
                case StatusEffectType.IceVulnerability:
                    agent.bonusIceResistance -= effect.modifierValue;
                    break;
                    
                case StatusEffectType.LightningVulnerability:
                    agent.bonusLightningResistance -= effect.modifierValue;
                    break;
                    
                case StatusEffectType.PoisonVulnerability:
                    agent.bonusPoisonResistance -= effect.modifierValue;
                    break;
            }
        }
        
        /// <summary>
        /// Get the damage remaining after shield absorption.
        /// Reduces shield amount accordingly.
        /// </summary>
        public float ApplyShieldAbsorption(CrowdAgent agent, float damage)
        {
            if (agent == null || agent.cachedShieldAmount <= 0) return damage;
            
            float absorbed = Mathf.Min(damage, agent.cachedShieldAmount);
            
            // Reduce shield effect(s)
            float remainingAbsorption = absorbed;
            for (int i = 0; i < CrowdAgent.MAX_STATUS_EFFECTS && remainingAbsorption > 0; i++)
            {
                ref ActiveStatusEffect effect = ref agent.activeStatusEffects[i];
                if (!effect.isActive || effect.type != StatusEffectType.Shield) continue;
                
                float shieldReduction = Mathf.Min(remainingAbsorption, effect.modifierValue);
                effect.modifierValue -= shieldReduction;
                remainingAbsorption -= shieldReduction;
                
                // Remove depleted shield
                if (effect.modifierValue <= 0)
                {
                    RemoveEffect(agent, i, wasDispelled: false);
                }
            }
            
            agent.cachedShieldAmount = Mathf.Max(0, agent.cachedShieldAmount - absorbed);
            
            return damage - absorbed;
        }
        
        /// <summary>
        /// Apply Health Buff effect - increases max and current health.
        /// Call this when the buff is first applied.
        /// </summary>
        public void ApplyHealthBuff(CrowdAgent agent, float healthIncrease)
        {
            if (agent == null || agent.enemyCrowd == null) return;
            
            agent.health += healthIncrease;
            // Note: max health increase would need to be tracked separately
            // For now, just add to current health
        }
        
        /// <summary>
        /// Revert Health Buff effect when it expires.
        /// </summary>
        public void RevertHealthBuff(CrowdAgent agent, float healthIncrease)
        {
            if (agent == null) return;
            
            // Don't let health go below 1 when buff expires
            agent.health = Mathf.Max(1f, agent.health - healthIncrease);
        }
        
        /// <summary>
        /// Reset performance counters (call at frame start).
        /// </summary>
        public void ResetFrameCounters()
        {
            effectsProcessedThisFrame = 0;
            dotsProcessedThisFrame = 0;
        }
        
        /// <summary>Get number of effects processed this frame.</summary>
        public int GetEffectsProcessedThisFrame() => effectsProcessedThisFrame;
        
        /// <summary>Get number of DOT ticks processed this frame.</summary>
        public int GetDOTsProcessedThisFrame() => dotsProcessedThisFrame;
    }
}
