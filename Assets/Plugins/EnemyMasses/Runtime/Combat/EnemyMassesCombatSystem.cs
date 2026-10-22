using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
using EnemyMasses.VFX;
using Arawn.EnemyMasses.Runtime.Damageables;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages combat behavior including attack state machines, damage application, hit reactions, blocking, and death.
    /// </summary>
    public class EnemyMassesCombatSystem
    {
        private readonly CombatSettings _settings;
        private readonly AnimationSettings _animationSettings;
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        private readonly System.Func<Vector3, Transform> _getNearestPlayerTarget;
        private readonly System.Func<Transform> _getPrimaryPlayerTarget;
        private bool _factionBattleModeActive;
        
        // Status Effect System
        private StatusEffectSystem _statusEffectSystem;
        
        // Ragdoll System - manages ragdoll physics activation on death
        private EnemyMassesRagdollSystem _ragdollSystem;
        private System.Action<CrowdAgent> _forceBoneTrackerCallback;

        // Dedicated list for sinking agents - updated every frame for smooth animation
        private readonly List<CrowdAgent> _sinkingAgents = new();
        
        // Faction center positions for BattleLine mode - cached per frame
        private readonly Dictionary<int, Vector3> _factionCenters = new();
        private int _lastFactionCenterFrame = -1;
        
        // Projectile system - pending projectiles to fire after animation delay
        private readonly List<PendingProjectile> _pendingProjectiles = new();

        private int _combatUpdateCursor;
        private float _lastCombatNavUpdateTime;
        private float[] _attackClipLengths;
        private float[] _hitClipLengths;
        private float _deathClipLength;
        private float _blockClipLength;

        private float _engagementRadiusSqr;
        private float _attackDistanceSqr;
        
        private CombatPositioningSystem _positioningSystem;
        
        // ═══════════════════════════════════════════════════════════════════
        // SKILL SYSTEM INTEGRATION
        // Per-agent and per-faction skill state management for combat loop integration
        // ═══════════════════════════════════════════════════════════════════
        
        // Per-agent skill runtime states (keyed by agent index)
        private readonly Dictionary<int, SkillRuntimeState> _agentSkillStates = new();
        
        // Per-faction shared cooldowns (keyed by faction name)
        private readonly Dictionary<string, FactionSkillCooldowns> _factionSkillCooldowns = new();
        
        // Per-EnemyCrowd shared cooldowns (keyed by EnemyCrowd asset)
        private readonly Dictionary<EnemyCrowd, FactionSkillCooldowns> _enemyCrowdSkillCooldowns = new();
        
        // Reusable lists for skill target gathering (reduces allocations)
        private readonly List<CrowdAgent> _skillTargetBuffer = new(64);
        
        // Cached max skill range per EnemyCrowd (avoids recalculating every frame)
        private readonly Dictionary<EnemyCrowd, float> _cachedMaxSkillRange = new();
        private float _lastSkillRangeCacheTime;
        

        private const float LOCOMOTION_EXIT_THRESHOLD = 0.02f;
        
        /// <summary>
        /// Stores data for a projectile that will be fired after animation sync delay.
        /// </summary>
        private struct PendingProjectile
        {
            public float fireTime;
            public int sourceAgentIndex;
            public int targetAgentIndex;
            public Vector3 targetPosition;
            public int factionIndex;
            public ProjectileAttackConfig config;
            public float baseDamage;
            public RangedAttackMode attackMode;
            public bool isSecondaryAttack;
        }
        
        /// <summary>
        /// Stores data for hitscan effects that will fire after animation sync delay.
        /// </summary>
        private struct PendingHitscanEffect
        {
            public float effectTime;
            public int sourceAgentIndex;
            public int targetAgentIndex;
            public Vector3 targetPosition;
            public int factionIndex;
            public HitscanEffectsConfig config;
            public float baseDamage; // Direct hit damage
            public float aoeDamage;  // AOE splash damage
            public float aoeRadius;  // AOE radius (0 = no AOE)
            public float aoeFalloff; // Min damage at AOE edge
        }
        
        /// <summary>
        /// Stores data for melee swing effects that will fire after animation sync delay.
        /// </summary>
        private struct PendingMeleeSwingEffect
        {
            public float effectTime;
            public int sourceAgentIndex;
            public int factionIndex;
            public MeleeAttackEffectsConfig config;
        }
        
        /// <summary>
        /// Stores data for melee impact effects that will fire when damage is dealt.
        /// </summary>
        private struct PendingMeleeImpactEffect
        {
            public float effectTime;
            public int sourceAgentIndex;
            public Vector3 hitPoint;
            public int factionIndex;
            public MeleeAttackEffectsConfig config;
        }
        
        private List<PendingHitscanEffect> _pendingHitscanEffects = new List<PendingHitscanEffect>();
        private List<PendingMeleeSwingEffect> _pendingMeleeSwingEffects = new List<PendingMeleeSwingEffect>();
        private List<PendingMeleeImpactEffect> _pendingMeleeImpactEffects = new List<PendingMeleeImpactEffect>();
        
        // Audio source pool for efficient hitscan sound playback
        private List<AudioSource> _audioSourcePool;
        private Queue<int> _freeAudioSources;
        private float[] _audioSourceReleaseTime;
        private Transform _audioPoolContainer;

        
        // VFX pools for efficient hitscan VFX (muzzle flash, impact, AOE)
        private Dictionary<GameObject, List<GameObject>> _vfxPools;
        private Dictionary<GameObject, Queue<int>> _vfxFreeIndices;
        private List<(GameObject prefab, int poolIndex, float releaseTime)> _activeVFX;
        private Transform _vfxPoolContainer;
        
        // Cached ParticleSystem components per VFX prefab pool instance
        private Dictionary<GameObject, Dictionary<int, ParticleSystem>> _cachedVFXParticleSystems;
        
        // VFX spawner for handling both ParticleSystem and VFX Graph
        private VFXSpawner _vfxSpawner;

        public event System.Action<GPUICrowdInstance> OnAgentKilled;
        public event System.Action<CrowdAgent, float, float> OnAgentHealthChanged;
        public event System.Action<CrowdAgent, CrowdCombatState, CrowdCombatState> OnCombatStateChanged;
        
        /// <summary>
        /// Fired when an EnemyCrowd agent hits the Player Target (via melee or ranged attack).
        /// Parameters: (attackerAgent, attackerPosition, targetTransform, damage, hitPoint, isMeleeAttack)
        /// Use this event to apply damage to the player via GC2's health system or custom logic.
        /// </summary>
        public event System.Action<CrowdAgent, Vector3, Transform, float, Vector3, bool> OnPlayerTargetHit;

        /// <summary>
        /// Fired after damage is applied to any agent (melee, ranged, skill, DOT).
        /// Parameters: (damagedAgent, finalDamage, attackerAgentIndex).
        /// attackerAgentIndex is -1 if no specific attacker (e.g., environmental damage).
        /// Subscribe to this to build minimap alert / under-attack indicators.
        /// </summary>
        public event System.Action<CrowdAgent, float, int> OnAgentDamaged;

        /// <summary>
        /// Creates a new combat system.
        /// </summary>
        public EnemyMassesCombatSystem(
            CombatSettings settings,
            AnimationSettings animationSettings,
            List<CrowdAgent> agents,
            List<EnemyCrowd> enemyCrowds,
            System.Func<Vector3, Transform> getNearestPlayerTarget,
            System.Func<Transform> getPrimaryPlayerTarget)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _animationSettings = animationSettings ?? throw new System.ArgumentNullException(nameof(animationSettings));
            _agents = agents ?? throw new System.ArgumentNullException(nameof(agents));
            _enemyCrowds = enemyCrowds ?? throw new System.ArgumentNullException(nameof(enemyCrowds));
            _getNearestPlayerTarget = getNearestPlayerTarget;
            _getPrimaryPlayerTarget = getPrimaryPlayerTarget;
        }

        private Transform GetNearestPlayerTarget(Vector3 position)
        {
            return _getNearestPlayerTarget != null ? _getNearestPlayerTarget(position) : null;
        }

        private Transform GetPrimaryPlayerTarget()
        {
            return _getPrimaryPlayerTarget != null ? _getPrimaryPlayerTarget() : null;
        }

        /// <summary>
        /// Initializes combat system with cached values.
        /// </summary>
        public void Initialize()
        {
            _engagementRadiusSqr = _settings.engagementRadius * _settings.engagementRadius;
            // _attackDistanceSqr is no longer used - ranges are per-faction now
            _attackDistanceSqr = _settings.engagementRadius * _settings.engagementRadius; // Use engagement as fallback
            CacheAnimationClipLengths();
            
            // Initialize positioning system
            _positioningSystem = new CombatPositioningSystem(_settings, _agents, _enemyCrowds);
            
            // Initialize status effect system
            _statusEffectSystem = new StatusEffectSystem(null); // Will be set via SetController
            _statusEffectSystem.OnDamageTick += HandleStatusEffectDamageTick;
            _statusEffectSystem.OnHealTick += HandleStatusEffectHealTick;
            _statusEffectSystem.OnEffectApplied += HandleStatusEffectApplied;
            _statusEffectSystem.OnEffectRemoved += HandleStatusEffectRemoved;
            
            // Initialize audio source pool for efficient hitscan sound playback
            InitializeAudioPool(64);
            
            // Ensure CrowdAudioManager exists for per-clip concurrent sound limiting
            CrowdAudioManager.EnsureInstance();
            
            // Initialize VFX pools for efficient hitscan VFX
            InitializeVFXPools();
            
            // Validate and fix any conflicting combat settings in factions
            ValidateFactionCombatSettings();
            
            // Initialize skill system faction cooldowns
            InitializeSkillSystem();
        }
        
        /// <summary>
        /// Initializes skill system state for all factions.
        /// </summary>
        private void InitializeSkillSystem()
        {
            _factionSkillCooldowns.Clear();
            _enemyCrowdSkillCooldowns.Clear();
            
            foreach (var faction in _enemyCrowds)
            {
                if (faction == null) continue;
                
                // Initialize faction cooldowns if skill system is enabled
                if (faction.skillSystemConfig != null && faction.skillSystemConfig.enableSkillSystem)
                {
                    if (!string.IsNullOrEmpty(faction.factionName) && !_factionSkillCooldowns.ContainsKey(faction.factionName))
                    {
                        _factionSkillCooldowns[faction.factionName] = new FactionSkillCooldowns();
                    }

                    if (!_enemyCrowdSkillCooldowns.ContainsKey(faction))
                    {
                        _enemyCrowdSkillCooldowns[faction] = new FactionSkillCooldowns();
                    }
                }
            }
        }
        
        /// <summary>
        /// Sets the ragdoll system reference for death ragdoll activation.
        /// Must be called after Initialize() and before combat updates.
        /// </summary>
        /// <param name="ragdollSystem">The ragdoll system instance</param>
        /// <param name="forceBoneTrackerCallback">Callback to force bone tracker workflow for ragdoll</param>
        public void SetRagdollSystem(EnemyMassesRagdollSystem ragdollSystem, System.Action<CrowdAgent> forceBoneTrackerCallback)
        {
            _ragdollSystem = ragdollSystem;
            _forceBoneTrackerCallback = forceBoneTrackerCallback;
        }
        
        /// <summary>
        /// Gets the status effect system for external access.
        /// </summary>
        public StatusEffectSystem GetStatusEffectSystem() => _statusEffectSystem;

        private void NotifyAgentHealthChanged(CrowdAgent agent, float previousHealth)
        {
            if (agent == null) return;

            float newHealth = agent.health;
            if (Mathf.Approximately(newHealth, previousHealth)) return;

            float maxHealth = agent.maxHealth > 0f
                ? agent.maxHealth
                : Mathf.Max(1f, GetAgentBaseHealth(agent) + GetAgentHealthVariance(agent));
            OnAgentHealthChanged?.Invoke(agent, newHealth, maxHealth);
        }
        
        #region Status Effect Callbacks
        
        private void HandleStatusEffectDamageTick(CrowdAgent agent, int slotIndex, float damage, StatusEffectType damageType)
        {
            if (agent == null || agent.isDead) return;
            float previousHealth = agent.health;
            
            // Apply damage from DOT
            float finalDamage = damage * agent.cachedDamageReceivedMultiplier;
            
            // Shield absorption
            if (agent.cachedShieldAmount > 0)
            {
                finalDamage = _statusEffectSystem.ApplyShieldAbsorption(agent, finalDamage);
            }
            
            // Check invulnerability
            if (agent.isInvulnerable)
            {
                finalDamage = 0f;
            }
            
            if (finalDamage > 0)
            {
                agent.health -= finalDamage;
                agent.lastDamageTime = Time.time;
                NotifyAgentHealthChanged(agent, previousHealth);
                
                // DOT damage can break sleep
                _statusEffectSystem.OnAgentTookDamage(agent);
                
                // Check for death
                if (agent.health <= 0)
                {
                    TriggerDeath(agent, Time.time);
                }
            }
        }
        
        private void HandleStatusEffectHealTick(CrowdAgent agent, int slotIndex, float heal, StatusEffectType healType)
        {
            if (agent == null || agent.isDead) return;
            float previousHealth = agent.health;
            
            // Use per-agent resolved max health when available.
            float maxHealth = agent.maxHealth > 0f
                ? agent.maxHealth
                : 100f;
            
            agent.health = Mathf.Min(agent.health + heal, maxHealth);
            NotifyAgentHealthChanged(agent, previousHealth);
        }
        
        private void HandleStatusEffectApplied(CrowdAgent agent, int slotIndex, StatusEffectType type)
        {
            if (agent == null) return;
            
            // Reset attack timer if staggered
            if (type == StatusEffectType.Stagger)
            {
                agent.nextAttackTime = Time.time + GetRandomizedAttackCooldown(agent);
            }
            
            // Apply movement speed changes
            if (type == StatusEffectType.Slow || type == StatusEffectType.SpeedBuff || 
                type == StatusEffectType.Daze || type == StatusEffectType.Root)
            {
                ApplySpeedMultipliers(agent);
            }
            
            // TODO: Spawn VFX/audio from StatusEffectConfig
        }
        
        private void HandleStatusEffectRemoved(CrowdAgent agent, int slotIndex, StatusEffectType type, bool wasDispelled)
        {
            if (agent == null) return;
            
            // Reapply movement speed when speed-affecting effect is removed
            if (type == StatusEffectType.Slow || type == StatusEffectType.SpeedBuff || 
                type == StatusEffectType.Daze || type == StatusEffectType.Root ||
                type == StatusEffectType.Stun || type == StatusEffectType.Freeze ||
                type == StatusEffectType.Sleep)
            {
                ApplySpeedMultipliers(agent);
            }
            
            // TODO: Spawn removal VFX/audio from StatusEffectConfig
        }
        
        #endregion
        
        #region Status Effect Public API
        
        /// <summary>
        /// Apply a status effect to an agent by index.
        /// </summary>
        /// <param name="agentIndex">Target agent index</param>
        /// <param name="type">Type of status effect</param>
        /// <param name="duration">Duration in seconds (0 = permanent until dispelled)</param>
        /// <param name="value">Effect value (damage per tick, stat multiplier, etc.)</param>
        /// <param name="tickInterval">For DOT/HOT: seconds between ticks</param>
        /// <param name="sourceAgentIndex">Source agent index (-1 for player/environment)</param>
        /// <returns>Slot index where effect was placed, or -1 if failed</returns>
        public int ApplyStatusEffect(int agentIndex, StatusEffectType type, float duration, float value, 
                                    float tickInterval = 1f, int sourceAgentIndex = -1, int maxStacks = 1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return -1;
            if (_statusEffectSystem == null) return -1;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return -1;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead) return -1;
            
            return _statusEffectSystem.ApplyEffect(agent, type, duration, value, tickInterval, maxStacks, sourceAgentIndex);
        }
        
        /// <summary>
        /// Apply a status effect to an agent using a config.
        /// </summary>
        public int ApplyStatusEffect(int agentIndex, StatusEffectConfig config, int sourceAgentIndex = -1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return -1;
            if (_statusEffectSystem == null || config == null) return -1;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return -1;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead) return -1;
            
            return _statusEffectSystem.ApplyEffect(agent, config, sourceAgentIndex);
        }
        
        /// <summary>
        /// Apply a status effect to all agents within radius of a position.
        /// </summary>
        /// <returns>Number of agents affected</returns>
        public int ApplyStatusEffectAOE(Vector3 center, float radius, StatusEffectType type, float duration, 
                                       float value, float tickInterval = 1f, int sourceAgentIndex = -1,
                                       int? targetFactionIndex = null)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority) return 0;
            if (_statusEffectSystem == null) return 0;
            
            int affected = 0;
            float radiusSqr = radius * radius;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead) continue;
                
                // Optional faction filter
                if (targetFactionIndex.HasValue && agent.enemyCrowdIndex != targetFactionIndex.Value) continue;
                
                // Distance check
                Vector3 agentPos = GetAgentPosition(agent);
                if ((agentPos - center).sqrMagnitude > radiusSqr) continue;
                
                if (_statusEffectSystem.ApplyEffect(agent, type, duration, value, tickInterval, 1, sourceAgentIndex) >= 0)
                {
                    affected++;
                }
            }
            
            return affected;
        }
        
        /// <summary>
        /// Remove a specific status effect from an agent.
        /// </summary>
        public void RemoveStatusEffect(int agentIndex, StatusEffectType type, bool wasDispelled = false)
        {
            if (_statusEffectSystem == null) return;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return;
            
            var agent = _agents[agentIndex];
            if (agent == null) return;
            
            _statusEffectSystem.RemoveEffectsOfType(agent, type, wasDispelled);
        }
        
        /// <summary>
        /// Dispel all negative effects from an agent.
        /// </summary>
        /// <returns>Number of effects dispelled</returns>
        public int DispelNegativeEffects(int agentIndex)
        {
            if (_statusEffectSystem == null) return 0;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return 0;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead) return 0;
            
            return _statusEffectSystem.DispelNegativeEffects(agent);
        }
        
        /// <summary>
        /// Dispel all positive effects (purge buffs) from an agent.
        /// </summary>
        /// <returns>Number of effects dispelled</returns>
        public int DispelPositiveEffects(int agentIndex)
        {
            if (_statusEffectSystem == null) return 0;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return 0;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead) return 0;
            
            return _statusEffectSystem.DispelPositiveEffects(agent);
        }
        
        /// <summary>
        /// Dispel all effects of a category from an agent.
        /// </summary>
        public int DispelCategory(int agentIndex, StatusEffectCategory category)
        {
            if (_statusEffectSystem == null) return 0;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return 0;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.isDead) return 0;
            
            return _statusEffectSystem.DispelCategory(agent, category);
        }
        
        /// <summary>
        /// Check if an agent has a specific status effect.
        /// </summary>
        public bool HasStatusEffect(int agentIndex, StatusEffectType type)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count) return false;
            
            var agent = _agents[agentIndex];
            if (agent == null) return false;
            
            return agent.HasEffect(type);
        }
        
        /// <summary>
        /// Clear all status effects from an agent (for death, respawn, etc.)
        /// </summary>
        public void ClearAllStatusEffects(int agentIndex)
        {
            if (_statusEffectSystem == null) return;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return;
            
            var agent = _agents[agentIndex];
            if (agent == null) return;
            
            _statusEffectSystem.ClearAllEffects(agent);
        }
        
        #endregion
        
        #region Attack Status Effect Application
        
        /// <summary>
        /// Apply status effects from an attacker's configured attack effects.
        /// Called when damage is dealt successfully.
        /// </summary>
        private void ApplyAttackStatusEffects(CrowdAgent target, int attackerAgentIndex, bool isMeleeAttack, bool isChargeAttack, bool isCriticalHit)
        {
            if (_statusEffectSystem == null) return;
            if (attackerAgentIndex < 0 || attackerAgentIndex >= _agents.Count) return;
            
            var attacker = _agents[attackerAgentIndex];
            if (attacker == null || attacker.enemyCrowd == null) return;
            
            EnemyCrowd attackerFaction = attacker.enemyCrowd;
            AttackEffectsConfig effectsConfig = null;
            
            // Determine which effects config to use based on attack type
            if (isChargeAttack)
            {
                effectsConfig = attackerFaction.chargeAttackConfig?.impactEffects;
            }
            else if (isMeleeAttack)
            {
                effectsConfig = attackerFaction.meleeAttackEffects;
            }
            else
            {
                // Ranged attack
                effectsConfig = attackerFaction.rangedAttackEffects;
            }
            
            if (effectsConfig == null || !effectsConfig.HasAnyEffects) return;
            
            // Get effects that passed their chance rolls
            var effectsToApply = effectsConfig.GetEffectsToApply(isCriticalHit);
            
            foreach (var effect in effectsToApply)
            {
                // Check target type compatibility
                if (!IsValidEffectTarget(effect, attacker, target)) continue;
                
                // Apply the effect
                _statusEffectSystem.ApplyEffect(target, effect.effectType, effect.duration, effect.value, 
                                               effect.tickInterval, 1, attackerAgentIndex);
                
                // Spawn VFX if configured
                if (effect.applyVFX != null && effect.applyVFX.IsValid)
                {
                    Vector3 vfxPos = target.WorldPosition;
                    SpawnVFX(effect.applyVFX, vfxPos, Quaternion.identity, null);
                }
                
                // Play sound if configured
                if (effect.applySound != null)
                {
                    Vector3 soundPos = target.WorldPosition;
                    PlaySoundAtPosition(effect.applySound, soundPos, effect.applySoundVolume, effect.applySoundPitchVariation, effect.applySoundSpatialBlend);
                }
            }
        }
        
        /// <summary>
        /// Check if target is valid for this effect based on target type.
        /// </summary>
        private bool IsValidEffectTarget(AttackStatusEffect effect, CrowdAgent attacker, CrowdAgent target)
        {
            switch (effect.targetType)
            {
                case EffectTargetType.Enemy:
                    // Target must be from different faction
                    return attacker.enemyCrowdIndex != target.enemyCrowdIndex;
                    
                case EffectTargetType.Ally:
                    // Target must be from same faction
                    return attacker.enemyCrowdIndex == target.enemyCrowdIndex;
                    
                case EffectTargetType.Self:
                    // Target must be the attacker
                    return attacker == target;
                    
                case EffectTargetType.All:
                    return true;
                    
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Process support ability for an agent (healing, buffing allies).
        /// Call this periodically from UpdateCombatAgent for support units.
        /// </summary>
        /// <returns>True if ability was used, false otherwise</returns>
        public bool TryUseSupportAbility(CrowdAgent agent, SupportAbilityConfig abilityConfig, float currentTime)
        {
            if (agent == null || abilityConfig == null || !abilityConfig.enabled) return false;
            if (!agent.canAttack) return false; // Can't use abilities if CC'd
            
            EnemyCrowd faction = GetEnemyCrowd(agent);
            if (faction == null || !faction.canUseSpecialAbilities) return false;
            
            // Check cooldown (using secondary attack time for support abilities)
            if (currentTime < agent.lastSecondaryAttackTime + abilityConfig.GetRandomizedCooldown()) return false;
            
            // Find valid targets
            Vector3 agentPos = GetAgentPosition(agent);
            var validTargets = new System.Collections.Generic.List<CrowdAgent>();
            float radiusSqr = abilityConfig.radius * abilityConfig.radius;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var target = _agents[i];
                if (target == null || target.isDead) continue;
                
                // Check if same faction (for ally buffs/heals)
                if (target.enemyCrowdIndex != agent.enemyCrowdIndex) continue;
                
                // Check if self should be included
                if (target == agent && !abilityConfig.includeSelf) continue;
                
                // Check distance
                Vector3 targetPos = GetAgentPosition(target);
                if ((targetPos - agentPos).sqrMagnitude > radiusSqr) continue;
                
                // For healing: check health threshold
                if (StatusEffectHelper.IsHealOverTime(abilityConfig.effect.effectType) ||
                    abilityConfig.effect.effectType == StatusEffectType.HealthBuff)
                {
                    float maxHealth = target.maxHealth > 0f
                        ? target.maxHealth
                        : 100f;
                    float healthPercent = target.health / maxHealth;
                    
                    if (healthPercent > abilityConfig.triggerHealthThreshold) continue;
                }
                
                validTargets.Add(target);
                
                // Check max targets limit
                if (abilityConfig.maxTargets > 0 && validTargets.Count >= abilityConfig.maxTargets) break;
            }
            
            // Check minimum targets
            if (validTargets.Count < abilityConfig.minTargetsToTrigger) return false;
            
            // Use ability!
            agent.lastSecondaryAttackTime = currentTime;
            
            // Play animation if configured
            if (abilityConfig.abilityAnimation != null)
            {
                PlayCombatClip(agent, abilityConfig.abilityAnimation, 0.15f, false);
                agent.combatStateEndTime = currentTime + abilityConfig.animationDuration;
            }
            
            // Spawn caster VFX
            if (abilityConfig.casterVFX != null && abilityConfig.casterVFX.IsValid)
            {
                SpawnVFX(abilityConfig.casterVFX, agentPos, Quaternion.identity, null);
            }
            
            // Play sound
            if (abilityConfig.activationSound != null)
            {
                PlaySoundAtPosition(abilityConfig.activationSound, agentPos, abilityConfig.soundVolume, abilityConfig.soundPitchVariation, abilityConfig.soundSpatialBlend);
            }
            
            // Apply effect to all valid targets
            foreach (var target in validTargets)
            {
                var effect = abilityConfig.effect;
                _statusEffectSystem?.ApplyEffect(target, effect.effectType, effect.duration, effect.value,
                                                effect.tickInterval, 1, agent.index);
                
                // Spawn target VFX
                if (abilityConfig.targetVFX != null && abilityConfig.targetVFX.IsValid)
                {
                    Vector3 targetPos = GetAgentPosition(target);
                    SpawnVFX(abilityConfig.targetVFX, targetPos, Quaternion.identity, null);
                }
            }
            
            return true; // Ability was used
        }
        
        #endregion
        
        /// <summary>
        /// Initializes VFX pools for efficient muzzle flash, impact, and AOE VFX.
        /// </summary>
        private void InitializeVFXPools()
        {
            _vfxPools = new Dictionary<GameObject, List<GameObject>>();
            _vfxFreeIndices = new Dictionary<GameObject, Queue<int>>();
            _activeVFX = new List<(GameObject, int, float)>(256);
            _cachedVFXParticleSystems = new Dictionary<GameObject, Dictionary<int, ParticleSystem>>();
            
            // Create a container for pooled VFX
            GameObject vfxContainer = new GameObject("CombatVFXPool");
            _vfxPoolContainer = vfxContainer.transform;
            
            // Initialize VFX spawner for both ParticleSystem and VFX Graph support
            _vfxSpawner = new VFXSpawner(_vfxPoolContainer);
        }
        
        #region Speed Management
        
        /// <summary>
        /// Ensures the agent's base speed is initialized. Should be called before any speed modifications.
        /// </summary>
        private void EnsureBaseSpeedInitialized(CrowdAgent agent)
        {
            if (agent.hasBaseSpeedSet || agent.navigationAgent == null) return;
            
            agent.baseSpeed = agent.navigationAgent.Speed;
            agent.hasBaseSpeedSet = true;
            agent.allyRushSpeedMultiplier = 1f;
            agent.chargeSpeedMultiplier = 1f;
            
            #if UNITY_EDITOR
            // Debug log removed to reduce noise
            #endif
        }
        
        /// <summary>
        /// Applies the combined speed multipliers to the agent's NavMeshAgent.
        /// Call this after modifying any speed multiplier.
        /// </summary>
        private void ApplySpeedMultipliers(CrowdAgent agent)
        {
            if (agent.navigationAgent == null || !agent.IsOnNavMesh) return;
            
            EnsureBaseSpeedInitialized(agent);
            
            // Apply all multipliers together - prevents stacking issues
            // Include status effect move speed modifier (cachedMoveSpeedMultiplier)
            float combinedMultiplier = agent.allyRushSpeedMultiplier * agent.chargeSpeedMultiplier * agent.cachedMoveSpeedMultiplier;
            float oldSpeed = agent.navigationAgent.Speed;
            agent.navigationAgent.Speed = agent.baseSpeed * combinedMultiplier;
        }
        
        /// <summary>
        /// Sets the ally rush speed multiplier and applies the new speed.
        /// </summary>
        private void SetAllyRushSpeedMultiplier(CrowdAgent agent, float multiplier)
        {
            EnsureBaseSpeedInitialized(agent);
            float oldMult = agent.allyRushSpeedMultiplier;
            agent.allyRushSpeedMultiplier = multiplier;
            
            ApplySpeedMultipliers(agent);
        }
        
        /// <summary>
        /// Sets the charge speed multiplier and applies the new speed.
        /// </summary>
        private void SetChargeSpeedMultiplier(CrowdAgent agent, float multiplier)
        {
            EnsureBaseSpeedInitialized(agent);
            float oldMult = agent.chargeSpeedMultiplier;
            agent.chargeSpeedMultiplier = multiplier;
            
            ApplySpeedMultipliers(agent);
        }
        
        /// <summary>
        /// Clears all speed multipliers and restores base speed.
        /// </summary>
        private void ClearAllSpeedMultipliers(CrowdAgent agent)
        {
            agent.allyRushSpeedMultiplier = 1f;
            agent.chargeSpeedMultiplier = 1f;
            agent.isRushing = false;
            agent.wasAlertedByAlly = false;
            agent.rushTargetAgentIndex = -1;
            agent.alertedByAgentIndex = -1;
            ApplySpeedMultipliers(agent);
        }
        
        /// <summary>
        /// Checks for and FIXES agents with stuck speed multipliers.
        /// Called periodically to clean up any orphaned rush/charge states.
        /// </summary>
        private void CleanupStuckSpeedMultipliers(float currentTime)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead) continue;
                
                bool hasRushMult = agent.allyRushSpeedMultiplier > 1.01f;
                bool hasChargeMult = agent.chargeSpeedMultiplier > 1.01f;
                bool needsCleanup = false;
                
                // Check for stuck ally rush multiplier
                if (hasRushMult)
                {
                    if (!agent.isRushing)
                    {
                        // Has rush multiplier but isRushing=false - stuck!
                        needsCleanup = true;
                    }
                    else
                    {
                        // Check if rush target is dead but multiplier still active
                        bool rushTargetDead = agent.rushTargetAgentIndex < 0 || 
                                              agent.rushTargetAgentIndex >= _agents.Count || 
                                              _agents[agent.rushTargetAgentIndex] == null ||
                                              _agents[agent.rushTargetAgentIndex].isDead;
                        if (rushTargetDead)
                        {
                            needsCleanup = true;
                        }
                        
                        // Check if rush timer expired (10 second max)
                        float timeSinceAlert = currentTime - agent.lastAllyAlertTime;
                        if (timeSinceAlert > 10f)
                        {
                            needsCleanup = true;
                        }
                    }
                }
                
                // Check for stuck charge multiplier
                if (hasChargeMult && !agent.isCharging)
                {
                    needsCleanup = true;
                }
                
                // Fix stuck multipliers
                if (needsCleanup)
                {
                    ClearAllSpeedMultipliers(agent);
                }
            }
        }
        
        #endregion
        
        #if UNITY_EDITOR
        /// <summary>
        /// Debug method to check for agents with stuck speed multipliers.
        /// Called periodically to detect issues.
        /// </summary>
        private void CheckForStuckSpeedMultipliers(float currentTime)
        {
            int stuckRushCount = 0;
            int stuckChargeCount = 0;
            var stuckAgents = new System.Text.StringBuilder();
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead) continue;
                
                bool hasRushMult = agent.allyRushSpeedMultiplier > 1.01f;
                bool hasChargeMult = agent.chargeSpeedMultiplier > 1.01f;
                
                // Check for stuck ally rush multiplier
                if (hasRushMult && !agent.isRushing)
                {
                    stuckRushCount++;
                    stuckAgents.Append($"\n  Agent {agent.index}: rushMult={agent.allyRushSpeedMultiplier:F2} but isRushing=false, wasAlertedByAlly={agent.wasAlertedByAlly}, rushTarget={agent.rushTargetAgentIndex}");
                }
                else if (hasRushMult && agent.isRushing)
                {
                    // Check if rush target is dead but multiplier still active
                    bool rushTargetDead = agent.rushTargetAgentIndex < 0 || 
                                          agent.rushTargetAgentIndex >= _agents.Count || 
                                          _agents[agent.rushTargetAgentIndex] == null ||
                                          _agents[agent.rushTargetAgentIndex].isDead;
                    if (rushTargetDead)
                    {
                        stuckRushCount++;
                        stuckAgents.Append($"\n  Agent {agent.index}: rushMult={agent.allyRushSpeedMultiplier:F2}, isRushing=true but rushTarget={agent.rushTargetAgentIndex} is DEAD/INVALID");
                    }
                    
                    // Check if rush timer expired
                    float timeSinceAlert = currentTime - agent.lastAllyAlertTime;
                    if (timeSinceAlert > 10f)
                    {
                        stuckRushCount++;
                        stuckAgents.Append($"\n  Agent {agent.index}: rushMult={agent.allyRushSpeedMultiplier:F2}, isRushing=true but alert was {timeSinceAlert:F1}s ago (>10s limit)");
                    }
                }
                
                // Check for stuck charge multiplier
                if (hasChargeMult && !agent.isCharging)
                {
                    stuckChargeCount++;
                    stuckAgents.Append($"\n  Agent {agent.index}: chargeMult={agent.chargeSpeedMultiplier:F2} but isCharging=false, combatState={agent.combatState}");
                }
            }
            
            if (stuckRushCount > 0 || stuckChargeCount > 0)
            {
                Debug.LogError($"[SpeedDebug] STUCK MULTIPLIERS DETECTED! RushStuck={stuckRushCount}, ChargeStuck={stuckChargeCount}{stuckAgents}");
            }
        }
        #endif
        
        /// <summary>
        /// Initializes the audio source pool for efficient sound playback without GC allocation.
        /// </summary>
        private void InitializeAudioPool(int poolSize)
        {
            _audioSourcePool = new List<AudioSource>(poolSize);
            _freeAudioSources = new Queue<int>(poolSize);
            _audioSourceReleaseTime = new float[poolSize];
            
            // Create a container for audio sources
            GameObject audioContainer = new GameObject("CombatAudioPool");
            _audioPoolContainer = audioContainer.transform;
            
            for (int i = 0; i < poolSize; i++)
            {
                GameObject audioObj = new GameObject($"AudioSource_{i}");
                audioObj.transform.SetParent(_audioPoolContainer);
                
                AudioSource source = audioObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f; // Default to 3D
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = 50f;
                
                _audioSourcePool.Add(source);
                _freeAudioSources.Enqueue(i);
                _audioSourceReleaseTime[i] = 0f;
            }
        }
        
        /// <summary>
        /// Validates and auto-corrects any conflicting combat settings in faction EnemyCrowd assets.
        /// </summary>
        private void ValidateFactionCombatSettings()
        {
            if (_enemyCrowds == null) return;
            
            foreach (var crowd in _enemyCrowds)
            {
                if (crowd == null) continue;
                
                // CRITICAL FIX: For non-Custom templates, verify settings match the template
                // If they don't match, the template was selected but never applied - apply it now
                if (crowd.combatBehaviorTemplate != CombatBehaviorTemplate.Custom)
                {
                    bool needsReapply = !CombatBehaviorTemplateHelper.DoSettingsMatchTemplate(crowd, crowd.combatBehaviorTemplate);
                    
                    if (needsReapply)
                    {
                        Debug.LogWarning($"[EnemyMasses] Faction '{crowd.factionName}' template is {crowd.combatBehaviorTemplate} but settings don't match. Auto-applying template at runtime.");
                        CombatBehaviorTemplateHelper.ApplyTemplate(crowd, crowd.combatBehaviorTemplate);
                    }
                    
                    // Also check for internal conflicts
                    var conflicts = CombatBehaviorTemplateHelper.GetSettingConflicts(crowd);
                    if (conflicts.Count > 0)
                    {
                        Debug.LogWarning($"[EnemyMasses] Faction '{crowd.factionName}' has combat setting conflicts: " +
                                       string.Join("; ", conflicts));
                    }
                }
            }
        }
        
        /// <summary>
        /// Calculates and caches the center position of each faction.
        /// Used for BattleLine positioning in Faction Battle mode.
        /// </summary>
        private void UpdateFactionCenters()
        {
            // Only update once per frame
            int currentFrame = Time.frameCount;
            if (currentFrame == _lastFactionCenterFrame)
                return;
            _lastFactionCenterFrame = currentFrame;
            
            _factionCenters.Clear();
            
            // Calculate center for each faction
            Dictionary<int, Vector3> sums = new();
            Dictionary<int, int> counts = new();
            
            int agentCount = _agents.Count;
            for (int i = 0; i < agentCount; i++)
            {
                CrowdAgent agent = _agents[i];
                if (agent == null || agent.isDead || agent.ragdollActivated)
                    continue;
                
                if (!agent.IsOnNavMesh)
                    continue;
                
                int factionIndex = agent.enemyCrowdIndex;
                if (factionIndex < 0)
                    continue;
                
                Vector3 pos = agent.WorldPosition;
                
                if (!sums.ContainsKey(factionIndex))
                {
                    sums[factionIndex] = pos;
                    counts[factionIndex] = 1;
                }
                else
                {
                    sums[factionIndex] += pos;
                    counts[factionIndex]++;
                }
            }
            
            // Compute averages
            foreach (var kvp in sums)
            {
                int factionIndex = kvp.Key;
                Vector3 sum = kvp.Value;
                int count = counts[factionIndex];
                _factionCenters[factionIndex] = sum / count;
            }
        }
        
        /// <summary>
        /// Gets the center position of a faction. Returns Vector3.zero if faction not found.
        /// </summary>
        public Vector3 GetFactionCenter(int factionIndex)
        {
            UpdateFactionCenters();
            return _factionCenters.TryGetValue(factionIndex, out Vector3 center) ? center : Vector3.zero;
        }
        
        /// <summary>
        /// Gets the center position of the enemy faction for an agent.
        /// Used for BattleLine positioning where we need to face the enemy army, not individual agents.
        /// </summary>
        public Vector3 GetEnemyFactionCenter(CrowdAgent agent)
        {
            if (!_factionBattleModeActive || agent == null || agent.enemyCrowdIndex < 0)
                return Vector3.zero;
            
            UpdateFactionCenters();
            
            // Find an enemy faction center (any faction that's not this agent's faction)
            foreach (var kvp in _factionCenters)
            {
                if (kvp.Key != agent.enemyCrowdIndex)
                    return kvp.Value;
            }
            
            return Vector3.zero;
        }

        /// <summary>
        /// Initializes combat state for a newly spawned agent.
        /// </summary>
        public void InitializeCombatAgent(CrowdAgent agent, bool randomizeAttackTimer)
        {
            if (agent == null)
                return;
            float previousHealth = agent.health;

            // Phase 2: Apply base health with variance from agent-specific settings
            float variance = GetAgentHealthVariance(agent) > 0f ? Random.Range(0f, GetAgentHealthVariance(agent)) : 0f;
            float baseHealth = Mathf.Max(1f, GetAgentBaseHealth(agent) + variance);

            // Apply faction health multiplier and level if available
            if (agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd faction = _enemyCrowds[agent.enemyCrowdIndex];
                if (faction != null)
                {
                    agent.health = faction.GetModifiedHealth(baseHealth);
                    
                    // Apply level with variance for skill scaling
                    int levelVariance = faction.levelVariance > 0 ? Random.Range(-faction.levelVariance, faction.levelVariance + 1) : 0;
                    agent.level = Mathf.Clamp(faction.baseLevel + levelVariance, 1, 99);
                }
                else
                {
                    agent.health = baseHealth;
                    agent.level = 1;
                }
            }
            else
            {
                agent.health = baseHealth;
                agent.level = 1;
            }

            // Persist each agent's rolled spawn health as their actual max health.
            // This keeps UI bars, healing, and percentage checks consistent.
            agent.maxHealth = Mathf.Max(1f, agent.health);
            agent.health = Mathf.Clamp(agent.health, 0f, agent.maxHealth);
            NotifyAgentHealthChanged(agent, previousHealth);

            agent.isDead = false;
            agent.isBlocking = false;
            agent.combatState = CrowdCombatState.Idle;
            agent.combatStateEndTime = Time.time;
            agent.lastDamageTime = -999f;
            agent.lastAttackClipIndex = -1;
            agent.skillMovementLockEndTime = 0f;

            float initialDelay = randomizeAttackTimer 
                ? Random.Range(0.1f, GetRandomizedAttackCooldown(agent)) 
                : GetRandomizedAttackCooldown(agent);
            agent.nextAttackTime = Time.time + Mathf.Max(0.1f, initialDelay);

            // Clear combat-related flags
            agent.lastBlockClipIndex = -1;
            agent.lastDeathClipIndex = -1;
            agent.activeDefendParameterIndex = -1;
            agent.activeDeathParameterIndex = -1;
            agent.hasAttackTargetPosition = false;
            agent.attackTargetPosition = Vector3.zero;
            agent.attackTargetTransform = null;
            agent.attackTargetAgentIndex = -1;
            agent.SetNavigationState(AgentNavigationState.Formation);
            
            // Initialize status effect storage
            agent.InitializeStatusEffects();
        }

        private void LogCombatSummary(Vector3 playerPosition)
        {
            int totalAgents = 0;
            int inRange = 0;
            int attacking = 0;
            int approaching = 0;
            int idle = 0;
            int activeWaveAgents = 0;
            int waitingWaveAgents = 0;
            float closestDist = float.MaxValue;
            float farthestInRange = 0f;
            
            EnemyCrowd sampleFaction = null;
            
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || agent.isDead) continue;
                
                totalAgents++;
                Vector3 agentPos = agent.transform != null ? agent.transform.position : 
                    (agent.navigationAgent != null ? agent.navigationAgent.Position : Vector3.zero);
                float dist = Vector3.Distance(agentPos, playerPosition);
                
                if (sampleFaction == null && agent.enemyCrowd != null)
                    sampleFaction = agent.enemyCrowd;
                
                float maxAttackDist = agent.enemyCrowd?.meleeMaxDistance ?? 1.5f;
                float minAttackDist = agent.enemyCrowd?.meleeMinDistance ?? 1.0f;
                
                if (dist >= minAttackDist - 0.15f && dist <= maxAttackDist)
                {
                    inRange++;
                    farthestInRange = Mathf.Max(farthestInRange, dist);
                }
                
                if (dist < closestDist) closestDist = dist;
                
                switch (agent.combatState)
                {
                    case CrowdCombatState.Attacking: attacking++; break;
                    case CrowdCombatState.Approach: approaching++; break;
                    case CrowdCombatState.Idle: idle++; break;
                }
                
                // Track OrganizedSwarm wave status
                if (agent.isActiveWave)
                    activeWaveAgents++;
                else if (agent.waveGroupIndex >= 0)
                    waitingWaveAgents++;
            }
            
            string modeName = sampleFaction?.positioningMode.ToString() ?? "Unknown";
            int maxAttackers = sampleFaction?.maxConcurrentAttackers ?? 0;
            
            string waveInfo = "";
            if (sampleFaction?.positioningMode == CombatPositioningMode.OrganizedSwarm)
            {
                waveInfo = $", ActiveWave={activeWaveAgents}, WaitingWaves={waitingWaveAgents}";
            }
        }

        /// <summary>
        /// Updates combat AI for a batch of agents per frame.
        /// </summary>
        public void UpdateCombat(
            bool developerTestMode,
            int maxUpdatesPerFrame,
            System.Func<CrowdAgent, bool> isAgentDeadCallback,
            System.Func<int, float, CrowdAgent, CrowdAgent> getOrAssignDeveloperTargetCallback,
            System.Action<CrowdAgent> ensureIdleAnimationCallback,
            System.Func<CrowdAgent, Vector3> getAgentWorldPositionCallback,
            System.Action<CrowdAgent, int, float> assignDeveloperTargetCallback,
            bool factionBattleMode = false,
            System.Func<int, bool> isAgentCivilCallback = null)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return;

            _factionBattleModeActive = factionBattleMode;
            
            int agentCount = _agents.Count;
            if (agentCount == 0)
                return;

            int updatesThisFrame = Mathf.Clamp(maxUpdatesPerFrame, 1, agentCount);
            float now = Time.time;
            Transform primaryPlayerTarget = GetPrimaryPlayerTarget();
            Vector3 playerPosition = primaryPlayerTarget != null ? primaryPlayerTarget.position : Vector3.zero;
            
            // Reset status effect counters at frame start
            _statusEffectSystem?.ResetFrameCounters();

            // Periodic combat summary log (every 120 frames = ~2 seconds at 60fps)
            if (Time.frameCount % 120 == 0 && agentCount > 0)
            {
                LogCombatSummary(playerPosition);
            }
            
            // Periodic speed multiplier cleanup (every 60 frames = ~1 second at 60fps)
            // This catches stuck multipliers when rush targets die
            if (Time.frameCount % 60 == 0)
            {
                CleanupStuckSpeedMultipliers(now);
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // SKILL SYSTEM COOLDOWN UPDATE
            // Update all skill cooldowns once per frame
            // ═══════════════════════════════════════════════════════════════════
            UpdateSkillCooldowns(Time.deltaTime);
            
            // ═══════════════════════════════════════════════════════════════════
            // CAST TIME PROCESSING
            // Check all agents for pending casts every frame (not batched).
            // Completes or interrupts casts as needed.
            // ═══════════════════════════════════════════════════════════════════
            ProcessCastingAgents(now);
            
            #if UNITY_EDITOR
            // Periodic speed multiplier audit (every 300 frames = ~5 seconds at 60fps)
            // Just for logging - the cleanup above does the actual fixing
            if (Time.frameCount % 300 == 0)
            {
                CheckForStuckSpeedMultipliers(now);
            }
            #endif

            for (int processed = 0; processed < updatesThisFrame; processed++)
            {
                if (_combatUpdateCursor >= agentCount)
                    _combatUpdateCursor = 0;

                int agentIndex = _combatUpdateCursor;
                CrowdAgent agent = _agents[agentIndex];
                _combatUpdateCursor++;

                if (agent == null)
                    continue;
                
                // Skip civil agents - they use their own navigation and don't participate in combat
                if (isAgentCivilCallback != null && isAgentCivilCallback(agentIndex))
                    continue;

                // Process status effects first (DOT ticks, expiration, modifier updates)
                if (_statusEffectSystem != null && agent.activeStatusEffects != null)
                {
                    _statusEffectSystem.ProcessAgentEffects(agent, now);
                }

                // Retry pending combat animations (death, attack, etc. that failed on previous frame)
                ProcessPendingCombatAnimation(agent);

                // Skip fully processed dead agents (but not if ragdoll active, sinking, or waiting to sink)
                if (isAgentDeadCallback != null && isAgentDeadCallback(agent) && 
                    !agent.ragdollPending && !agent.ragdollActivated && 
                    !agent.ragdollSinkPending && !agent.isSinking && 
                    agent.deathAnimationCompleted)
                    continue;

                if (developerTestMode)
                {
                    UpdateCombatAgentDeveloper(agent, agentIndex, now, 
                        getOrAssignDeveloperTargetCallback, 
                        ensureIdleAnimationCallback,
                        getAgentWorldPositionCallback,
                        assignDeveloperTargetCallback);
                }
                else
                {
                    UpdateCombatAgent(agent, now, ensureIdleAnimationCallback);
                }
            }
            
            // Process any pending projectiles that need to fire
            ProcessPendingProjectiles(now);
            
            // Process any pending hitscan effects that need to fire
            ProcessPendingHitscanEffects(now);
            
            // Process any pending melee effects (swing and impact)
            ProcessPendingMeleeEffects(now);
            
            // Periodic cleanup of orphaned charge audio objects (every 60 frames = ~1 second at 60fps)
            if (Time.frameCount % 60 == 0)
            {
                CleanupOrphanedChargeAudio();
            }
        }

        #region Combat Update Logic

        private static bool IsSkillMovementLocked(CrowdAgent agent, float currentTime)
        {
            if (agent == null || agent.skillMovementLockEndTime <= 0f)
                return false;

            if (currentTime >= agent.skillMovementLockEndTime)
            {
                agent.skillMovementLockEndTime = 0f;
                return false;
            }

            return true;
        }

        private void UpdateCombatAgent(
            CrowdAgent agent, 
            float currentTime,
            System.Action<CrowdAgent> ensureIdleAnimationCallback = null)
        {
            if (agent == null)
                return;
            
            // ═══════════════════════════════════════════════════════════════════
            // STATUS EFFECT CC CHECKS - Skip combat AI if agent is crowd controlled
            // ═══════════════════════════════════════════════════════════════════
            
            // Hard CC: Agent cannot move or attack (Stun, Freeze, Sleep)
            if (!agent.canMove && !agent.canAttack)
            {
                // Stop all movement
                StopNavigationAgent(agent, false);
                
                // Cleanup any active charge
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                    SetChargeSpeedMultiplier(agent, 1f);
                }
                
                // TODO: Play CC animation (frozen pose, sleep animation, etc.)
                return;
            }

            // Skill cast movement lock: keep the agent stationary while cast time is active.
            if (IsSkillMovementLocked(agent, currentTime))
            {
                StopNavigationAgent(agent, false);
                if (agent.hasAttackTargetPosition)
                {
                    FaceTarget(agent, agent.attackTargetPosition);
                }
                return;
            }
            
            // Soft CC: Agent cannot attack but can still move (Daze)
            // Will be checked below when deciding to attack
            
            // Fear: Agent flees from source
            if (agent.isFeared && agent.fearSourceAgentIndex >= 0 && agent.fearSourceAgentIndex < _agents.Count)
            {
                var fearSource = _agents[agent.fearSourceAgentIndex];
                if (fearSource != null && !fearSource.isDead && agent.IsOnNavMesh)
                {
                    // Flee in opposite direction
                    Vector3 agentPos = agent.WorldPosition;
                    Vector3 sourcePos = fearSource.WorldPosition;
                    Vector3 fleeDir = (agentPos - sourcePos).normalized;
                    Vector3 fleeTarget = agentPos + fleeDir * 20f; // Flee 20 units away
                    
                    // Sample navigation surface for valid flee position
                    if (TrySamplePosition(agent, fleeTarget, 10f, out Vector3 hitPosition))
                    {
                        TrySetCombatDestination(agent, hitPosition);
                    }
                }
                return;
            }
            
            // Taunt: Override attack target to taunt source
            if (agent.isTaunted && agent.tauntSourceAgentIndex >= 0 && agent.tauntSourceAgentIndex < _agents.Count)
            {
                var tauntSource = _agents[agent.tauntSourceAgentIndex];
                if (tauntSource != null && !tauntSource.isDead)
                {
                    // Force target to taunt source
                    agent.attackTargetAgentIndex = agent.tauntSourceAgentIndex;
                    agent.attackTargetTransform = null;
                    agent.attackTargetPosition = tauntSource.WorldPosition;
                    agent.hasAttackTargetPosition = true;
                }
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // SUPPORT ABILITIES - Healers/Buffers try to help allies
            // ═══════════════════════════════════════════════════════════════════
            var supportFaction = GetEnemyCrowd(agent);
            if (supportFaction != null && agent.canAttack) // Use canAttack as proxy for "can use abilities"
            {
                // Try healing ability (target: wounded same-faction ally)
                if (TryUseSupportAbility(agent, supportFaction.healingAbility, currentTime))
                {
                    // Successfully used healing, skip normal combat this frame
                    return;
                }
                
                // Try buff ability (target: allies or self)
                if (TryUseSupportAbility(agent, supportFaction.buffAbility, currentTime))
                {
                    // Successfully used buff, skip normal combat this frame
                    return;
                }
            }
            
            // Cleanup charge state if agent became invalid during charge
            if (agent.navigationAgent == null)
            {
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                }
                return;
            }

            // Skip agents under manual RTS command - they follow player orders, not combat AI
            // EXCEPTION: HoldPosition agents in ManualCommand CAN attack while holding position
            // EXCEPTION: Agents that have ARRIVED at their RTS formation position CAN fight (self-defense, ally defense)
            if (agent.navigationState == AgentNavigationState.ManualCommand)
            {
                // IMPORTANT: Still need to cleanup charge audio if agent was charging when given manual command
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                    SetChargeSpeedMultiplier(agent, 1f);
                }
                
                // HoldPosition stance with active move command: Let them reach destination first
                // When they arrive, update the hold position to the new location
                if (agent.combatStance == CombatStance.HoldPosition)
                {
                    if (agent.hasManualCommand && agent.IsOnNavMesh && agent.IsNavigationEnabled)
                    {
                        float distToDestination = Vector3.Distance(agent.WorldPosition, agent.manualCommandDestination);
                        if (distToDestination <= (agent.navigationAgent?.StoppingDistance ?? 0.5f) + 0.5f && 
                            agent.CurrentVelocity.sqrMagnitude < 0.01f)
                        {
                            // Reached new position - UPDATE hold position to here (not go back to old one!)
                            agent.holdPositionLocation = agent.WorldPosition;
                            agent.hasHoldPositionSet = true;
                            agent.hasManualCommand = false;
                            // Stay in ManualCommand state so navigation controller enforces hold position
                        }
                        else
                        {
                            // Still moving to new position - skip combat while moving
                            return;
                        }
                    }
                    // HoldPosition agents that are holding (not moving) should CONTINUE to combat logic
                    // They will attack enemies in range while staying at their hold position
                    // Fall through to combat processing below...
                }
                else
                {
                    // Non-HoldPosition agents in ManualCommand: Check if reached destination
                    if (agent.hasManualCommand && agent.IsOnNavMesh && agent.IsNavigationEnabled)
                    {
                        float distToDestination = Vector3.Distance(agent.WorldPosition, agent.manualCommandDestination);
                        if (distToDestination <= (agent.navigationAgent?.StoppingDistance ?? 0.5f) + 0.5f && 
                            agent.CurrentVelocity.sqrMagnitude < 0.01f)
                        {
                            // Reached RTS-commanded destination - mark as holding formation position
                            // This prevents NavigationController from overriding the position
                            agent.hasManualCommand = false;
                            agent.hasRTSFormationPosition = true;
                            // Keep in ManualCommand state to prevent NavigationController from giving new destinations
                            // (NavigationController skips ManualCommand agents)
                            // Fall through to combat processing - agents at their destination CAN fight!
                        }
                        else
                        {
                            // Still moving to destination - skip combat while moving
                            return;
                        }
                    }
                    else if (!agent.hasRTSFormationPosition)
                    {
                        // Agent is in ManualCommand but hasn't reached destination and has no valid NavMeshAgent
                        // Skip combat for now
                        return;
                    }
                    // Agents that have reached their RTS formation position (hasRTSFormationPosition = true)
                    // continue to combat processing below - they can defend themselves and allies!
                }
            }

            // Check for pending ragdoll activation
            if (agent.ragdollPending && currentTime >= agent.ragdollActivationTime)
            {
                TryActivateAgentRagdoll(agent);
            }

            if (agent.ragdollActivated)
            {
                // Cleanup charge if agent was ragdolled while charging
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                }
                
                // Check for ragdoll sink scheduling (ragdoll has settled, time to start sinking)
                if (agent.ragdollSinkPending && currentTime >= agent.ragdollSinkScheduleTime)
                {
                    InitializeRagdollSinking(agent, currentTime);
                }
                
                // Process sinking if active (even for ragdoll agents)
                if (agent.isSinking)
                {
                    ProcessDeathSinking(agent, currentTime);
                }
                
                return;
            }

            if (agent.isDead)
            {
                // Cleanup charge if agent died while charging
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                }
                
                StopNavigationAgent(agent, false);
                
                // Process death sinking effect (per-faction setting)
                // Check if agent is already sinking OR if we should enable sinking
                if (agent.isSinking)
                {
                    ProcessDeathSinking(agent, currentTime);
                }
                
                return;
            }

            Transform playerTarget = null;
            Vector3 playerPosition = Vector3.zero;
            if (!_factionBattleModeActive)
            {
                playerTarget = GetNearestPlayerTarget(agent.WorldPosition);
                if (playerTarget != null)
                    playerPosition = playerTarget.position;
            }

            // Only set player target if no attack target is already assigned
            // (allows spawn triggers and other systems to override the target)
            // SKIP in faction battle mode - agents should only target other agents
            if (!_factionBattleModeActive && playerTarget != null && agent.attackTargetTransform == null && !agent.hasAttackTargetPosition)
            {
                agent.attackTargetTransform = playerTarget;
                agent.attackTargetPosition = playerPosition;
                agent.hasAttackTargetPosition = true;
            }
            
            // In faction battle mode, skip agents with no target assigned
            if (_factionBattleModeActive && !agent.hasAttackTargetPosition && agent.attackTargetAgentIndex < 0)
            {
                // No valid target - stay in idle state and ensure idle animation plays
                if (agent.combatState != CrowdCombatState.Idle)
                    EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);
                
                // CRITICAL: Ensure idle animation is playing so agents don't T-pose
                ensureIdleAnimationCallback?.Invoke(agent);

                // Keep navigation agent enabled so patrol/manual destinations can still execute.
                if (agent.navigationAgent != null && agent.IsOnNavMesh && agent.IsNavigationEnabled)
                {
                    if (!agent.navigationAgent.HasPath && !agent.navigationAgent.PathPending)
                    {
                        StopNavigationAgent(agent, false);
                    }
                    else if (agent.navigationAgent.IsStopped)
                    {
                        agent.navigationAgent.IsStopped = false;
                    }
                }
                
                return;
            }
            
            // Update attack target position if transform is assigned (for moving targets)
            if (agent.attackTargetTransform != null)
            {
                agent.attackTargetPosition = agent.attackTargetTransform.position;
            }

            // HoldPosition stance: Don't transition to ChasingTarget, stay at position
            if (agent.hasAttackTargetPosition && agent.navigationState == AgentNavigationState.Formation && 
                agent.combatStance != CombatStance.HoldPosition)
            {
                agent.SetNavigationState(AgentNavigationState.ChasingTarget);
            }

            // Use the agent's attack target position for distance calculations
            // In faction battle mode, only use the assigned target position (no fallback to player)
            Vector3 targetPosition = agent.hasAttackTargetPosition ? agent.attackTargetPosition : 
                                    (_factionBattleModeActive ? agent.attackTargetPosition : playerPosition);

            // CRITICAL FAILSAFE: If agent has charge audio but is NOT in Charging state, cleanup immediately
            // This catches any edge case where the state changed without proper cleanup
            if (agent.isCharging && agent.combatState != CrowdCombatState.Charging)
            {
                CleanupChargeAudio(agent);
                agent.isCharging = false;
                SetChargeSpeedMultiplier(agent, 1f);
            }
            
            // Also cleanup if agent has audio source but isCharging is false (state mismatch)
            if (!agent.isCharging && agent.chargeLoopAudioSource != null)
            {
                CleanupChargeAudio(agent);
            }

            // Handle combat state transitions
            switch (agent.combatState)
            {
                case CrowdCombatState.Attacking:
                case CrowdCombatState.Blocking:
                case CrowdCombatState.Hit:
                    if (currentTime >= agent.combatStateEndTime)
                    {
                        if (agent.combatState == CrowdCombatState.Attacking)
                            agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);

                        EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);
                        
                        // Return after transitioning to prevent immediate re-attack in the same frame
                        return;
                    }
                    else
                    {
                        if (agent.combatState == CrowdCombatState.Attacking && _settings.lockMovementDuringAttacks)
                            StopNavigationAgent(agent, false);
                        else
                            StopNavigationAgent(agent, false);
                        return;
                    }
                    
                case CrowdCombatState.Charging:
                    // HoldPosition stance: Don't allow charge attacks
                    if (agent.combatStance == CombatStance.HoldPosition)
                    {
                        CleanupChargeAudio(agent);
                        agent.isCharging = false;
                        SetChargeSpeedMultiplier(agent, 1f);
                        EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);
                        return;
                    }
                    // Handle active charge - update position and check for impact
                    UpdateChargeAttack(agent, currentTime, targetPosition);
                    return; // Charging takes full control
            }

            Vector3 agentPosition = agent.WorldPosition;
            float sqrDistance = (agentPosition - targetPosition).sqrMagnitude;

            // Get faction-specific engagement radius, fallback to global setting
            // Also account for skill ranges — if a faction has skills with range exceeding
            // engagementRadius, use the max skill range so agents enter combat early enough
            // to fire long-range skills (e.g., rockets, projectiles on flying vehicles).
            EnemyCrowd faction = GetEnemyCrowd(agent);
            float factionEngagementRadius = GetEffectiveEngagementRadius(faction);
            float factionEngagementRadiusSqr = factionEngagementRadius * factionEngagementRadius;

            // Outside engagement range - idle (EXCEPT for alerted allies who need to rush)
            // IMPORTANT: Use faction-specific engagementRadius, not global _engagementRadiusSqr
            // This allows ranged units and mounted units to attack targets at their faction's range
            bool isAlertedAlly = agent.wasAlertedByAlly && Time.time - agent.lastAllyAlertTime < 10f;
            if (sqrDistance > factionEngagementRadiusSqr && !isAlertedAlly)
            {
                if (agent.combatState != CrowdCombatState.Idle)
                    EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);
                return;
            }
            
            // Alerted allies bypass engagement range check

            float distanceToTarget = Mathf.Sqrt(sqrDistance);
            if (TryInitiateChargeAttack(agent, faction, distanceToTarget, targetPosition, currentTime))
            {
                return; // Charge initiated, takes priority
            }
            
            // Check for OPPORTUNISTIC ATTACKS FIRST - before any positioning
            // This prevents the agent from being repositioned away from target before attacking
            
            var combatAnims = GetCombatAnimationsForAgent(agent);
            bool hasAttackAnims = combatAnims.attacks != null && combatAnims.attacks.Length > 0;
            // Non-humanoid agents (flying vehicles, ground vehicles) may have no attack animations
            // but still need to enter the attack path so the skill system can fire their skills.
            bool hasSkills = faction != null && faction.skillSystemConfig != null 
                             && faction.skillSystemConfig.enableSkillSystem 
                             && faction.skillSystemConfig.skillSlots != null 
                             && faction.skillSystemConfig.skillSlots.Count > 0;
            bool canAttack = hasAttackAnims || hasSkills;
            
            // For Staggered mode, add individual delay offset to create unsynchronized attacks
            float effectiveAttackTime = agent.nextAttackTime;
            if (faction != null && faction.positioningMode == CombatPositioningMode.Staggered)
            {
                effectiveAttackTime += agent.staggeredAttackDelayOffset;
            }
            
            // OPPORTUNISTIC ATTACK - HIGHEST PRIORITY
            // Check BEFORE positioning so agent attacks immediately when player walks into range
            // This prevents the positioning system from pushing the agent away before the attack triggers
            if (faction != null && faction.allowOpportunisticAttacks && canAttack)
            {
                // Check if we're in attack range - use direct check, bypass positioning system
                bool inOpportunisticRange = _positioningSystem.IsInAttackRange(agent, distanceToTarget);
                if (inOpportunisticRange)
                {
                    // STOP the agent - they're in attack range and should attack
                    StopNavigationAgent(agent, false);
                    
                    // Face the target while waiting/attacking
                    FaceTarget(agent, targetPosition);
                    
                    // For opportunistic attacks: Skip initial cooldown if agent hasn't attacked yet
                    // This ensures instant reaction when player walks into range
                    // lastAttackClipIndex == -1 means the agent has never attacked
                    bool isFirstAttack = agent.lastAttackClipIndex == -1;
                    bool cooldownReady = isFirstAttack || currentTime >= effectiveAttackTime;
                    
                    if (cooldownReady)
                    {
                        // IMMEDIATELY attack - don't let positioning interfere
                        // Check if we're attacking another agent (faction battle mode)
                        CrowdAgent targetAgentForAttack = null;
                        if (agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count)
                        {
                            targetAgentForAttack = _agents[agent.attackTargetAgentIndex];
                            if (targetAgentForAttack != null && targetAgentForAttack.isDead)
                                targetAgentForAttack = null; // Don't attack dead agents
                        }
                        
                        // ═══════════════════════════════════════════════════════════════
                        // SKILL SYSTEM INTEGRATION - Try skill before basic attack
                        // ═══════════════════════════════════════════════════════════════
                        if (TryExecuteSkillBeforeBasicAttack(agent, faction))
                        {
                            // Skill was executed instead of basic attack
                            agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                            return; // Skip all positioning - skill executed
                        }

                        if (ShouldSuppressLegacyBasicAttack(agent, faction))
                        {
                            DelayNextSkillOnlyAttackAttempt(agent, faction, currentTime);
                            return;
                        }
                        
                        Transform attackTarget = agent.attackTargetTransform != null ? agent.attackTargetTransform : playerTarget;
                        BeginAgentAttack(agent, currentTime, attackTarget, targetAgentForAttack);
                    }
                    // else: Agent is stopped and facing target, waiting for cooldown (only after first attack)
                    
                    return; // Skip all positioning - opportunistic attack mode takes priority
                }
            }
            
            // ALLY DEFENSE RUSH - Check if rush target died IMMEDIATELY (before any other logic)
            // This must run every frame regardless of animation state to prevent stuck speed multipliers
            if (agent.wasAlertedByAlly && agent.rushTargetAgentIndex >= 0)
            {
                bool rushTargetDead = agent.rushTargetAgentIndex >= _agents.Count || 
                                      _agents[agent.rushTargetAgentIndex] == null ||
                                      _agents[agent.rushTargetAgentIndex].isDead;
                if (rushTargetDead)
                {
                    // Rush target died - clear rush state and restore speed IMMEDIATELY
                    agent.wasAlertedByAlly = false;
                    agent.rushTargetAgentIndex = -1;
                    agent.alertedByAgentIndex = -1;
                    if (agent.isRushing)
                    {
                        agent.isRushing = false;
                        SetAllyRushSpeedMultiplier(agent, 1f);
                    }
                }
            }
            
            // Only update navigation and positioning if NOT currently in an active combat animation
            // This prevents formation logic from interfering with attacks
            bool isInCombatAnimation = agent.combatState == CrowdCombatState.Attacking ||
                                      agent.combatState == CrowdCombatState.Blocking ||
                                      agent.combatState == CrowdCombatState.Hit;
            
            // CRITICAL FIX: Don't reposition agents during combat animation transitions (Compute Animator)
            // This prevents stuttering caused by immediate repositioning after attacks
            bool isInAnimationTransition = agent.currentWorkflow == HybridWorkflowType.ComputeAnimator && 
                                          Time.time < agent.combatAnimationTransitionEndTime;
            
            // HOLDPOSITION FIX: Don't reposition HoldPosition agents - they stay at their spot and attack from there
            // This is critical for mounted wall units and guard posts
            bool isHoldPosition = agent.combatStance == CombatStance.HoldPosition;
            
            if (!isInCombatAnimation && !isInAnimationTransition && !isHoldPosition)
            {
                // ALLY DEFENSE RUSH: When alerted by ally, rush directly to the attacker (bypass BattleLine formation)
                // This allows back-line melee units to charge forward and protect wounded allies
                
                bool wasAlertedAndShouldRush = agent.wasAlertedByAlly && 
                                               faction != null && 
                                               faction.enableAllyDefense &&
                                               Time.time - agent.lastAllyAlertTime < 10f; // Rush for 10 seconds after alert
                
                // BATTLE LINE FIX: For BattleLine mode in Faction Battle, use enemy faction center for positioning
                // This ensures the battle line faces the enemy army as a whole, not individual targets
                // EXCEPTION: Alerted allies bypass formation to rush their target directly
                Vector3 positioningTargetPosition = targetPosition;
                if (_factionBattleModeActive && faction != null && faction.positioningMode == CombatPositioningMode.BattleLine && !wasAlertedAndShouldRush)
                {
                    Vector3 enemyCenter = GetEnemyFactionCenter(agent);
                    if (enemyCenter != Vector3.zero)
                    {
                        positioningTargetPosition = enemyCenter;
                    }
                }
                
                // Get combat position based on positioning mode (includes spacing)
                // For rushing allies, skip positioning system and rush directly to target
                Vector3 combatPosition;
                bool shouldStop;
                if (wasAlertedAndShouldRush)
                {
                    // ALLY RUSH MODE: Go directly to target, stop at melee distance
                    // BUT spread out to avoid AOE damage - each agent approaches from a different angle
                    float meleeMaxDist = faction != null ? faction.meleeMaxDistance : 2f;
                    float spacing = faction != null ? faction.agentSpacing : 0.8f;
                    Vector3 dirToTarget = (targetPosition - agentPosition);
                    float distToTarget = dirToTarget.magnitude;
                    
                    if (distToTarget > meleeMaxDist)
                    {
                        // Calculate spread angle based on agent index to avoid clumping
                        // Each rushing ally gets a different approach angle around the target
                        float spreadAngle = (agent.index % 8) * 45f; // 8 approach angles, 45 degrees apart
                        float spreadRadius = spacing * 1.5f; // How far apart allies spread around target
                        
                        // Create an offset position around the target based on spread angle
                        Vector3 spreadOffset = Quaternion.Euler(0, spreadAngle, 0) * Vector3.forward * spreadRadius;
                        Vector3 spreadTargetPos = targetPosition + spreadOffset;
                        
                        // Recalculate direction to spread position
                        Vector3 dirToSpreadTarget = (spreadTargetPos - agentPosition);
                        
                        // Rush toward spread position but stop at melee distance from actual target
                        combatPosition = spreadTargetPos - dirToSpreadTarget.normalized * (meleeMaxDist * 0.8f);
                        shouldStop = false;
                        
                        // AGGRESSIVE speed boost for rushing allies (configurable multiplier - need to close gap fast!)
                        if (agent.navigationAgent != null && agent.IsOnNavMesh)
                        {
                            float rushMultiplier = faction != null ? faction.allyRushSpeedMultiplier : 2.5f;
                            if (!agent.isRushing)
                            {
                                agent.isRushing = true;
                            }
                            SetAllyRushSpeedMultiplier(agent, rushMultiplier);
                        }
                    }
                    else
                    {
                        // Already in melee range - ATTACK!
                        combatPosition = agentPosition;
                        shouldStop = true;
                        
                        // Reset speed when in range and clear rush state
                        if (agent.isRushing)
                        {
                            agent.isRushing = false;
                            SetAllyRushSpeedMultiplier(agent, 1f);
                        }
                        // Clear alert flags - rush completed successfully
                        agent.wasAlertedByAlly = false;
                        agent.rushTargetAgentIndex = -1;
                        agent.alertedByAgentIndex = -1;
                    }
                }
                else
                {
                    // Not rushing - ensure speed is restored if was previously rushing
                    if (agent.isRushing)
                    {
                        agent.isRushing = false;
                        SetAllyRushSpeedMultiplier(agent, 1f);
                    }
                    // Also clear alert flags when not rushing anymore (e.g., 10 second timer expired)
                    if (agent.wasAlertedByAlly)
                    {
                        agent.wasAlertedByAlly = false;
                        agent.rushTargetAgentIndex = -1;
                        agent.alertedByAgentIndex = -1;
                    }
                    combatPosition = _positioningSystem.GetCombatPosition(agent, positioningTargetPosition, agent.index, out shouldStop);
                }
                float distanceToCombatPos = Vector3.Distance(agentPosition, combatPosition);
                
                // CRITICAL: If agent is off NavMesh, try to recover them
                if (!agent.IsOnNavMesh)
                {
                    // Try to warp agent back onto navigation surface at their current position
                    if (TrySamplePosition(agent, agentPosition, 5f, out Vector3 recoverPos))
                    {
                        float warpDist = Vector3.Distance(agentPosition, recoverPos);
                        Debug.LogWarning($"[CombatNavRecovery] Agent {agent.index} off-NavMesh, " +
                            $"warping from={agentPosition:F2} to={recoverPos:F2} (dist={warpDist:F1}m), " +
                            $"visible={agent.isVisible}, navState={agent.navigationState}, " +
                            $"isChasing={agent.isChasing}, isRushing={agent.isRushing}, " +
                            $"wasAlerted={agent.wasAlertedByAlly}");
                        
                        // Disable/enable cycle to reset NavMesh binding
                        if (agent.navigationAgent != null)
                        {
                            agent.navigationAgent.Enabled = false;
                            agent.navigationAgent.Warp(recoverPos);
                            agent.navigationAgent.Enabled = true;
                            agent.navigationAgent.Warp(recoverPos);
                        }
                    }
                    else
                    {
                        // Can't find valid NavMesh position - agent is in a bad spot
                        if (Time.frameCount % 300 == 0)
                        {
                            Debug.LogWarning($"[NavMeshRecovery] Agent {agent.index}: FAILED to find valid NavMesh position near {agentPosition}. Agent stuck off NavMesh!");
                        }
                        return; // Skip this agent for now
                    }
                }
                
                ResumeNavigationAgent(agent);
                
                // Validate combat position is on navigation surface before navigating
                Vector3 validCombatPosition = combatPosition;
                if (TrySamplePosition(agent, combatPosition, 2f, out Vector3 combatSample))
                {
                    validCombatPosition = combatSample;
                }
                else
                {
                    // Combat position is off NavMesh - use agent's current position instead
                    validCombatPosition = agentPosition;
                }
                
                float distToValidPos = Vector3.Distance(agentPosition, validCombatPosition);
                
                // Use larger threshold (0.5f) to allow continuous spacing corrections
                // This ensures spacing adjustments keep being applied as agents move
                if (agent.IsOnNavMesh && distToValidPos > 0.5f)
                {
                    TrySetCombatDestination(agent, validCombatPosition);
                }
                // When close (0.1-0.5m), apply gentle steering for spacing without full pathfinding
                else if (agent.IsOnNavMesh && distToValidPos > 0.1f && !shouldStop)
                {
                    // Use velocity to nudge agent toward spacing-adjusted position
                    // This allows continuous spacing corrections without constant SetDestination calls
                    Vector3 toTarget = (validCombatPosition - agentPosition).normalized;
                    if (agent.navigationAgent != null)
                        agent.navigationAgent.Velocity = toTarget * agent.navigationAgent.Speed * 0.3f;
                }
                else if (distToValidPos <= 0.1f && shouldStop)
                {
                    // Agent is already at combat position, explicitly stop them
                    StopNavigationAgent(agent, false);
                }
                
                // Stop if positioning system says to stop
                if (shouldStop)
                {
                    StopNavigationAgent(agent, false);
                    
                    // CRITICAL: Zero velocity to ensure animation system shows idle, not locomotion
                    // This prevents "walking in place" when agent is at formation position
                    if (agent.IsOnNavMesh && agent.navigationAgent != null)
                    {
                        agent.navigationAgent.Velocity = Vector3.zero;
                        agent.navigationAgent.ResetPath();
                    }
                    
                    // Also reset smoothed speed to trigger immediate idle animation
                    agent.smoothedSpeed = 0f;
                    
                    // Trigger idle animation if agent is waiting in formation (not actively in combat)
                    if (agent.combatState == CrowdCombatState.Idle || agent.combatState == CrowdCombatState.Approach)
                    {
                        ensureIdleAnimationCallback?.Invoke(agent);
                    }
                }
            }
            
            // Always face target when in combat range (applies during all states)
            // Use faction-specific engagement radius for consistent behavior with target acquisition
            if (_settings.maintainFacingBetweenAttacks && distanceToTarget <= factionEngagementRadius)
            {
                FaceTarget(agent, targetPosition);
            }

            // Use distance to TARGET (not combat position) for attack range check
            // Note: For opportunistic attacks, we already checked and returned above, so this is for normal attack flow
            bool positioningAllowsAttack = _positioningSystem.CanAgentAttack(agent, agent.index, distanceToTarget);
            
            if (canAttack && positioningAllowsAttack && currentTime >= effectiveAttackTime)
            {
                // Check if we're attacking another agent (faction battle mode)
                CrowdAgent targetAgentForAttack = null;
                if (agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count)
                {
                    targetAgentForAttack = _agents[agent.attackTargetAgentIndex];
                    if (targetAgentForAttack != null && targetAgentForAttack.isDead)
                        targetAgentForAttack = null; // Don't attack dead agents
                }
                
                // ═══════════════════════════════════════════════════════════════
                // SKILL SYSTEM INTEGRATION - Try skill before basic attack
                // ═══════════════════════════════════════════════════════════════
                if (TryExecuteSkillBeforeBasicAttack(agent, faction))
                {
                    // Skill was executed instead of basic attack
                    agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                    return;
                }

                if (ShouldSuppressLegacyBasicAttack(agent, faction))
                {
                    DelayNextSkillOnlyAttackAttempt(agent, faction, currentTime);
                    return;
                }
                
                // Use the agent's attack target transform for the attack
                Transform fallbackTarget = GetNearestPlayerTarget(agent.WorldPosition);
                Transform attackTarget = agent.attackTargetTransform != null ? agent.attackTargetTransform : fallbackTarget;
                BeginAgentAttack(agent, currentTime, attackTarget, targetAgentForAttack);
                return;
            }

            // ═══════════════════════════════════════════════════════════════
            // SKILL-ONLY FALLBACK — Agents that cannot pass the positioning
            // gate (melee distance) still need a skill-attempt path.
            //
            // Triggers when EITHER:
            //   a) No attack animations (vehicles) — original path, OR
            //   b) Target is flying and agent has anti-air skills — the melee
            //      positioning gate will NEVER pass for an airborne target,
            //      so we must bypass it and let skill evaluation handle range.
            //
            // Only requires the engagement-radius check (already passed to
            // reach this point) and the cooldown timer.
            // ═══════════════════════════════════════════════════════════════
            bool targetIsFlying = agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count
                                  && _agents[agent.attackTargetAgentIndex] != null
                                  && _agents[agent.attackTargetAgentIndex].isFlying;
            bool needsSkillFallback = !hasAttackAnims                          // (a) no attack anims
                                      || (targetIsFlying && hasSkills);        // (b) flying target + has skills

            // Debug: Log skill fallback evaluation for anti-air scenarios
            if (targetIsFlying && !agent._skillFallbackDebugLogged)
            {
                var targetAgent = _agents[agent.attackTargetAgentIndex];
                Debug.Log($"[SkillAntiAirDebug] Agent {agent.index} ('{agent.enemyCrowd?.name}') vs flying target {agent.attackTargetAgentIndex} ('{targetAgent?.enemyCrowd?.name}'). " +
                    $"needsSkillFallback={needsSkillFallback} hasAttackAnims={hasAttackAnims} hasSkills={hasSkills} " +
                    $"positioningAllowsAttack={positioningAllowsAttack} distToTarget={distanceToTarget:F1} " +
                    $"cooldownReady={currentTime >= effectiveAttackTime} effectiveAttackTime={effectiveAttackTime:F2} now={currentTime:F2}");
                agent._skillFallbackDebugLogged = true;
            }

            if (needsSkillFallback && hasSkills && currentTime >= effectiveAttackTime)
            {
                FaceTarget(agent, targetPosition);
                EnemyCrowd factionFallback = faction ?? GetEnemyCrowd(agent);
                bool skillResult = TryExecuteSkillBeforeBasicAttack(agent, factionFallback);
                Debug.Log($"[SkillAntiAirDebug] Agent {agent.index} skill-only fallback attempt: result={skillResult} faction='{factionFallback?.name}' skillSlots={factionFallback?.skillSystemConfig?.skillSlots?.Count ?? 0}");
                if (skillResult)
                {
                    agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                    return;
                }
                DelayNextSkillOnlyAttackAttempt(agent, factionFallback, currentTime);
                return;
            }

            // Approaching target
            if (agent.combatState != CrowdCombatState.Approach)
                EnterCombatState(agent, CrowdCombatState.Approach, 0f, currentTime);
        }

        private void UpdateCombatAgentDeveloper(
            CrowdAgent agent, 
            int agentIndex, 
            float currentTime,
            System.Func<int, float, CrowdAgent, CrowdAgent> getOrAssignDeveloperTargetCallback,
            System.Action<CrowdAgent> ensureIdleAnimationCallback,
            System.Func<CrowdAgent, Vector3> getAgentWorldPositionCallback,
            System.Action<CrowdAgent, int, float> assignDeveloperTargetCallback)
        {
            if (agent == null)
                return;
            
            // Cleanup charge state if agent became invalid during charge
            if (agent.navigationAgent == null)
            {
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                }
                return;
            }

            if (IsSkillMovementLocked(agent, currentTime))
            {
                StopNavigationAgent(agent, false);
                if (agent.hasAttackTargetPosition)
                {
                    FaceTarget(agent, agent.attackTargetPosition);
                }
                return;
            }

            // Skip agents under manual RTS command - they follow player orders, not combat AI
            // EXCEPTION: HoldPosition agents in ManualCommand CAN attack while holding position
            // EXCEPTION: Agents that have ARRIVED at their RTS formation position CAN fight (self-defense, ally defense)
            if (agent.navigationState == AgentNavigationState.ManualCommand)
            {
                // IMPORTANT: Still need to cleanup charge audio if agent was charging when given manual command
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                    SetChargeSpeedMultiplier(agent, 1f);
                }
                
                // HoldPosition stance with active move command: Let them reach destination first
                // When they arrive, update the hold position to the new location
                if (agent.combatStance == CombatStance.HoldPosition)
                {
                    if (agent.hasManualCommand && agent.IsOnNavMesh && agent.IsNavigationEnabled)
                    {
                        float distToDestination = Vector3.Distance(agent.WorldPosition, agent.manualCommandDestination);
                        if (distToDestination <= (agent.navigationAgent?.StoppingDistance ?? 0.5f) + 0.5f && 
                            agent.CurrentVelocity.sqrMagnitude < 0.01f)
                        {
                            // Reached new position - UPDATE hold position to here (not go back to old one!)
                            agent.holdPositionLocation = agent.WorldPosition;
                            agent.hasHoldPositionSet = true;
                            agent.hasManualCommand = false;
                            // Stay in ManualCommand state so navigation controller enforces hold position
                        }
                        else
                        {
                            // Still moving to new position - skip combat while moving
                            return;
                        }
                    }
                    // HoldPosition agents that are holding (not moving) should CONTINUE to combat logic
                    // They will attack enemies in range while staying at their hold position
                    // Fall through to combat processing below...
                }
                else
                {
                    // Non-HoldPosition agents in ManualCommand: Check if reached destination
                    if (agent.hasManualCommand && agent.IsOnNavMesh && agent.IsNavigationEnabled)
                    {
                        float distToDestination = Vector3.Distance(agent.WorldPosition, agent.manualCommandDestination);
                        if (distToDestination <= (agent.navigationAgent?.StoppingDistance ?? 0.5f) + 0.5f && 
                            agent.CurrentVelocity.sqrMagnitude < 0.01f)
                        {
                            // Reached RTS-commanded destination - mark as holding formation position
                            // This prevents NavigationController from overriding the position
                            agent.hasManualCommand = false;
                            agent.hasRTSFormationPosition = true;
                            // Keep in ManualCommand state to prevent NavigationController from giving new destinations
                            // (NavigationController skips ManualCommand agents)
                            // Fall through to combat processing - agents at their destination CAN fight!
                        }
                        else
                        {
                            // Still moving to destination - skip combat while moving
                            return;
                        }
                    }
                    else if (!agent.hasRTSFormationPosition)
                    {
                        // Agent is in ManualCommand but hasn't reached destination and has no valid NavMeshAgent
                        // Skip combat for now
                        return;
                    }
                    // Agents that have reached their RTS formation position (hasRTSFormationPosition = true)
                    // continue to combat processing below - they can defend themselves and allies!
                }
            }

            // Check for pending ragdoll activation
            if (agent.ragdollPending && currentTime >= agent.ragdollActivationTime)
            {
                TryActivateAgentRagdoll(agent);
            }

            if (agent.ragdollActivated)
            {
                // Cleanup charge if agent was ragdolled while charging
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                }
                
                // Check for ragdoll sink scheduling (ragdoll has settled, time to start sinking)
                if (agent.ragdollSinkPending && currentTime >= agent.ragdollSinkScheduleTime)
                {
                    InitializeRagdollSinking(agent, currentTime);
                }
                
                // Process sinking if active (even for ragdoll agents)
                if (agent.isSinking)
                {
                    ProcessDeathSinking(agent, currentTime);
                }
                
                return;
            }

            if (agent.isDead)
            {
                // Cleanup charge if agent died while charging
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                }
                
                StopNavigationAgent(agent, false);
                
                // Process death sinking effect if agent is sinking
                if (agent.isSinking)
                {
                    ProcessDeathSinking(agent, currentTime);
                }
                
                return;
            }

            // Handle combat state transitions
            if (agent.combatState == CrowdCombatState.Attacking ||
                agent.combatState == CrowdCombatState.Blocking ||
                agent.combatState == CrowdCombatState.Hit)
            {
                if (currentTime >= agent.combatStateEndTime)
                {
                    if (agent.combatState == CrowdCombatState.Attacking)
                        agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);

                    EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);
                }
                else
                {
                    if (agent.combatState == CrowdCombatState.Attacking && _settings.lockMovementDuringAttacks)
                        StopNavigationAgent(agent, false);
                    else
                        StopNavigationAgent(agent, false);
                    return;
                }
            }

            CrowdAgent targetAgent = getOrAssignDeveloperTargetCallback?.Invoke(agentIndex, currentTime, agent);
            if (targetAgent == null)
            {
                if (agent.combatState != CrowdCombatState.Idle)
                    EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);

                ensureIdleAnimationCallback?.Invoke(agent);
                StopNavigationAgent(agent, true);
                return;
            }

            Vector3 targetPosition = getAgentWorldPositionCallback != null ? getAgentWorldPositionCallback(targetAgent) : targetAgent.WorldPosition;
            agent.attackTargetAgentIndex = targetAgent.index;
            agent.attackTargetTransform = targetAgent.navigationAgent?.Transform ?? 
                                        (targetAgent.crowdInstance != null ? targetAgent.crowdInstance.transform : null);
            agent.attackTargetPosition = targetPosition;
            agent.hasAttackTargetPosition = true;
            if (agent.navigationState == AgentNavigationState.Formation)
            {
                agent.SetNavigationState(AgentNavigationState.ChasingTarget);
            }
            
            // CRITICAL FAILSAFE: If agent has charge audio but is NOT in Charging state, cleanup immediately
            if (agent.isCharging && agent.combatState != CrowdCombatState.Charging)
            {
                CleanupChargeAudio(agent);
                agent.isCharging = false;
                SetChargeSpeedMultiplier(agent, 1f);
            }
            
            // Also cleanup if agent has audio source but isCharging is false
            if (!agent.isCharging && agent.chargeLoopAudioSource != null)
            {
                CleanupChargeAudio(agent);
            }
            
            Vector3 agentPosition = getAgentWorldPositionCallback != null ? getAgentWorldPositionCallback(agent) : agent.WorldPosition;
            float sqrDistance = (agentPosition - targetPosition).sqrMagnitude;

            // Target too far or dead - reassign
            // Use skill-aware engagement radius so agents with long-range skills (e.g., rockets)
            // stay engaged with targets within their skill range
            EnemyCrowd factionForRadius = GetEnemyCrowd(agent);
            float effectiveEngRadiusDev = GetEffectiveEngagementRadius(factionForRadius);
            float effectiveEngRadiusSqrDev = effectiveEngRadiusDev * effectiveEngRadiusDev;
            if (sqrDistance > effectiveEngRadiusSqrDev || targetAgent.isDead)
            {
                assignDeveloperTargetCallback?.Invoke(agent, agentIndex, currentTime);
                targetAgent = getOrAssignDeveloperTargetCallback?.Invoke(agentIndex, currentTime, agent);
                
                if (targetAgent == null)
                {
                    if (agent.combatState != CrowdCombatState.Idle)
                        EnterCombatState(agent, CrowdCombatState.Idle, 0f, currentTime);

                    StopNavigationAgent(agent, false);
                    return;
                }

                targetPosition = getAgentWorldPositionCallback != null ? getAgentWorldPositionCallback(targetAgent) : targetAgent.WorldPosition;
                agentPosition = getAgentWorldPositionCallback != null ? getAgentWorldPositionCallback(agent) : agent.WorldPosition;
                sqrDistance = (agentPosition - targetPosition).sqrMagnitude;
            }

            // Within attack range - try to attack
            var combatAnims = GetCombatAnimationsForAgent(agent);
            bool hasAttackAnims = combatAnims.attacks != null && combatAnims.attacks.Length > 0;
            // Non-humanoid agents (flying vehicles, ground vehicles) may have no attack animations
            // but still need to enter the attack path so the skill system can fire their skills.
            EnemyCrowd factionDev = GetEnemyCrowd(agent);
            bool hasSkillsDev = factionDev != null && factionDev.skillSystemConfig != null 
                                && factionDev.skillSystemConfig.enableSkillSystem 
                                && factionDev.skillSystemConfig.skillSlots != null 
                                && factionDev.skillSystemConfig.skillSlots.Count > 0;
            bool canAttack = hasAttackAnims || hasSkillsDev;
            
            if (canAttack && sqrDistance <= _attackDistanceSqr && currentTime >= agent.nextAttackTime)
            {
                // ═══════════════════════════════════════════════════════════════
                // SKILL SYSTEM INTEGRATION - Try skill before basic attack
                // ═══════════════════════════════════════════════════════════════
                EnemyCrowd faction = GetEnemyCrowd(agent);
                if (TryExecuteSkillBeforeBasicAttack(agent, faction))
                {
                    // Skill was executed instead of basic attack
                    agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                    return;
                }

                if (ShouldSuppressLegacyBasicAttack(agent, faction))
                {
                    DelayNextSkillOnlyAttackAttempt(agent, faction, currentTime);
                    return;
                }
                
                BeginAgentAttack(agent, currentTime, null, targetAgent);
                return;
            }

            // ═══════════════════════════════════════════════════════════════
            // SKILL-ONLY FALLBACK (Developer Mode) — Agents that cannot pass
            // the _attackDistanceSqr gate still need a skill-attempt path.
            //
            // Triggers when EITHER:
            //   a) No attack animations (vehicles) — original path, OR
            //   b) Target is flying and agent has anti-air skills — the basic
            //      attack distance gate will never pass for airborne targets
            //      at altitude, so bypass it and let skill evaluation handle
            //      range checks.
            //
            // Only requires the engagement-radius check (already passed) and
            // the cooldown timer.
            // ═══════════════════════════════════════════════════════════════
            bool targetIsFlyingDev = targetAgent != null && targetAgent.isFlying;
            bool needsSkillFallbackDev = !hasAttackAnims                              // (a) no attack anims
                                         || (targetIsFlyingDev && hasSkillsDev);      // (b) flying target + has skills

            if (needsSkillFallbackDev && hasSkillsDev && currentTime >= agent.nextAttackTime)
            {
                FaceTarget(agent, targetPosition);
                EnemyCrowd factionFallback = GetEnemyCrowd(agent);
                if (TryExecuteSkillBeforeBasicAttack(agent, factionFallback))
                {
                    agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                    return;
                }
                DelayNextSkillOnlyAttackAttempt(agent, factionFallback, currentTime);
                return;
            }

            // Approaching target
            if (agent.combatState != CrowdCombatState.Approach)
                EnterCombatState(agent, CrowdCombatState.Approach, 0f, currentTime);
        }

        #endregion

        #region Combat State Management

        private void EnterCombatState(CrowdAgent agent, CrowdCombatState nextState, float duration, float currentTime)
        {
            if (agent == null)
                return;

            // Prevent redundant state transitions EXCEPT for Attacking state, which needs duration updates
            // Attacking state can be re-entered to update combatStateEndTime for new attack animations
            if (agent.combatState == nextState && nextState != CrowdCombatState.Attacking)
            {
                return;
            }

            CrowdCombatState previousState = agent.combatState;
            agent.combatState = nextState;
            agent.combatStateEndTime = duration > 0f ? currentTime + duration : currentTime;
            agent.isBlocking = nextState == CrowdCombatState.Blocking;

            if (nextState == CrowdCombatState.Dead)
            {
                agent.isDead = true;
                agent.hasArrived = true;
                
                // IMMEDIATELY cleanup charge audio if agent died while charging
                if (agent.isCharging)
                {
                    CleanupChargeAudio(agent);
                    agent.isCharging = false;
                    SetChargeSpeedMultiplier(agent, 1f);
                }
                
                // Clear attack target on death
                agent.attackTargetTransform = null;
                agent.attackTargetAgentIndex = -1;
                agent.hasAttackTargetPosition = false;
                
                // Clear any pending cast on death
                if (agent.isCasting)
                    SkillExecutor.InterruptCast(agent);
                
                // Cleanup orbital VFX on death
                if (OrbitalVFXManager.IsActive)
                    OrbitalVFXManager.Instance.RemoveAllEffectsFromAgent(agent.index);
                
                // Clear flight state so dead body descends to ground.
                // If the agent has significant altitude and crash-on-death is enabled,
                // initialize a physics-based crash descent instead of freezing in mid-air.
                if (agent.isFlying || agent.flightCurrentHeight > 0.01f)
                {
                    // For Flight3D agents, flightCurrentHeight may not reflect the true
                    // altitude (it uses a separate positioning system). Derive the actual
                    // height above ground from the transform position as a fallback.
                    float effectiveHeight = agent.flightCurrentHeight;
                    if (effectiveHeight < 0.01f && agent.transform != null)
                    {
                        // Estimate ground Y from the navigation agent or terrain raycast
                        float groundY = agent.transform.position.y;
                        if (agent.navigationAgent != null)
                        {
                            // Use navmesh/pathfinding ground position if available
                            var navPos = agent.navigationAgent.Position;
                            groundY = navPos.y;
                        }
                        effectiveHeight = agent.transform.position.y - groundY;
                    }

                    agent.isFlying = false;
                    agent.flightTargetHeight = 0f;

                    // ── Crash-from-sky initialization ──
                    EnemyCrowd crashCrowd = agent.enemyCrowd;
                    bool crashEnabled = crashCrowd != null && crashCrowd.flightCrashOnDeath;
                    if (effectiveHeight > 0.5f && crashEnabled)
                    {
                        // Sync flightCurrentHeight to the actual altitude for the crash loop
                        agent.flightCurrentHeight = effectiveHeight;
                        agent.isCrashing = true;
                        agent.crashVerticalVelocity = crashCrowd.flightCrashInitialDownSpeed;
                        agent.crashForwardSpeed = agent.navigationAgent != null
                            ? agent.navigationAgent.Velocity.magnitude * crashCrowd.flightCrashForwardMomentum
                            : 0f;
                        agent.crashForwardDirection = agent.transform != null
                            ? agent.transform.forward
                            : Vector3.forward;
                        agent.crashTumbleAngle = 0f;
                        agent.crashGroundY = agent.transform.position.y - effectiveHeight;
                    }
                }
            }
            else if (nextState == CrowdCombatState.Idle || nextState == CrowdCombatState.Approach)
            {
                agent.isDead = false;
                // DO NOT clear attack target - agents need to keep tracking when transitioning between combat states
            }

            // Clear cached blend weight when changing states
            agent.cachedBlendWeight = -1f;

            // Only clear attack target when attacking agent-to-agent in DEVELOPER MODE (not faction battle)
            // In faction battle mode, keep targets so agents continue pursuing across state transitions
            // For spawn triggers and external targets, keep the target across all states
            // IMPORTANT: Don't clear target during Hit/Blocking states - agent should resume combat after hit stun
            // IMPORTANT: Don't clear target for agents with explicit manual attack commands (skill bar, RTS attack-move)
            // — these agents were explicitly told to attack a target and must keep it across combat cycles
            if (!_factionBattleModeActive && 
                !agent.HasExplicitAttackIntent &&
                nextState != CrowdCombatState.Attacking && 
                nextState != CrowdCombatState.Hit &&
                nextState != CrowdCombatState.Blocking &&
                agent.attackTargetAgentIndex >= 0)
            {
                // This is agent-to-agent combat in developer mode, clear when transitioning to idle/approach
                agent.attackTargetTransform = null;
                agent.attackTargetAgentIndex = -1;
                agent.hasAttackTargetPosition = false;
            }
            
            // Notify positioning system about state changes
            UpdateNavigationStateForCombat(agent, nextState);
            if (nextState == CrowdCombatState.Attacking)
            {
                _positioningSystem?.BeginAttack(agent.index);
            }
            else if (previousState == CrowdCombatState.Attacking || 
                     previousState == CrowdCombatState.Blocking)
            {
                if (previousState == CrowdCombatState.Attacking)
                    _positioningSystem?.EndAttack(agent.index);
                
                // Explicitly transition back to locomotion after attack/block for all workflows
                // This fixes the "stuck at end of animation" issue
                if (nextState == CrowdCombatState.Idle || nextState == CrowdCombatState.Approach)
                {
                    float currentSpeed = agent.navigationAgent != null ? agent.smoothedSpeed : 0f;
                    float maxSpeed = agent.navigationAgent != null ? agent.navigationAgent.Speed : 1f;
                    float normalizedSpeed = maxSpeed > 0f ? Mathf.Clamp01(currentSpeed / maxSpeed) : 0f;
                    
                    // Reset animation state completely to prevent stuck frames
                    agent.cachedBlendWeight = -1f;
                    agent.lastIdleAnimationHash = -1;
                    agent.idleAnimationWasGpu = false;
                    
                    EnemyCrowd faction = agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count 
                        ? _enemyCrowds[agent.enemyCrowdIndex] : null;

                    // Handle Mecanim-based workflows (BoneTracker, MecanimReader)
                    if (agent.currentWorkflow == HybridWorkflowType.BoneTracker ||
                        agent.currentWorkflow == HybridWorkflowType.MecanimReader)
                    {
                        if (agent.mecanimAnimator != null && agent.mecanimAnimator.enabled)
                        {
                            // Get the locomotion state name (with faction override support)
                            string locomotionStateName = _animationSettings.locomotionStateName;
                            if (faction != null && !string.IsNullOrWhiteSpace(faction.locomotionStateNameOverride))
                                locomotionStateName = faction.locomotionStateNameOverride;

                            if (!string.IsNullOrWhiteSpace(locomotionStateName))
                            {
                                int locomotionHash = Animator.StringToHash(locomotionStateName);
                                if (agent.mecanimAnimator.HasState(0, locomotionHash))
                                {
                                    // Force crossfade back to locomotion state
                                    agent.mecanimAnimator.CrossFade(locomotionHash, 0.15f, 0, 0f);
                                    
                                    // Update Speed parameter for blend tree
                                    if (normalizedSpeed > 0.1f)
                                    {
                                        agent.mecanimAnimator.SetFloat("Speed", normalizedSpeed);
                                        agent.currentState = CrowdAnimationState.Locomotion;
                                    }
                                    else
                                    {
                                        agent.mecanimAnimator.SetFloat("Speed", 0f);
                                        agent.currentState = CrowdAnimationState.Idle;
                                    }
                                }
                            }
                        }
                    }
                    // Handle Compute Animator workflow (GPU)
                    else if (agent.currentWorkflow == HybridWorkflowType.ComputeAnimator &&
                             agent.crowdInstance != null && agent.crowdInstance.AnimatorWorkflow != null)
                    {
                        AnimationClip transitionClip = null;
                        
                        if (normalizedSpeed > 0.1f)
                        {
                            // Transition to locomotion (walk/run)
                            transitionClip = faction?.walkClipOverride ?? _animationSettings.walkClip;
                            if (transitionClip == null)
                                transitionClip = faction?.runClipOverride ?? _animationSettings.runClip;
                            agent.currentState = CrowdAnimationState.Locomotion;
                        }
                        else
                        {
                            // Transition to idle
                            transitionClip = faction?.idleClipOverride ?? _animationSettings.idleClip;
                            agent.currentState = CrowdAnimationState.Idle;
                        }
                        
                        // Explicitly start the animation to ensure transition happens
                        // Check buffer bounds first to prevent errors during rapid spawn/remove
                        if (transitionClip != null && IsBufferIndexValid(agent))
                        {
                            agent.crowdInstance.StartAnimation(transitionClip, -1f, 1f, 0.2f, null, true);
                        }
                    }
                }
            }
            else if (previousState == CrowdCombatState.Hit)
            {
                // After hit animation ends, explicitly restart locomotion/idle to prevent stuck animations
                if (nextState == CrowdCombatState.Idle || nextState == CrowdCombatState.Approach)
                {
                    float currentSpeed = agent.navigationAgent != null ? agent.smoothedSpeed : 0f;
                    float maxSpeed = agent.navigationAgent != null ? agent.navigationAgent.Speed : 1f;
                    float normalizedSpeed = maxSpeed > 0f ? Mathf.Clamp01(currentSpeed / maxSpeed) : 0f;

                    agent.cachedBlendWeight = -1f;
                    agent.lastIdleAnimationHash = -1;
                    agent.idleAnimationWasGpu = false;

                    // Handle Mecanim-based workflows (BoneTracker, MecanimReader)
                    if (agent.currentWorkflow == HybridWorkflowType.BoneTracker ||
                        agent.currentWorkflow == HybridWorkflowType.MecanimReader)
                    {
                        if (agent.mecanimAnimator != null && agent.mecanimAnimator.enabled)
                        {
                            EnemyCrowd faction = agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count
                                ? _enemyCrowds[agent.enemyCrowdIndex]
                                : null;

                            // Get the locomotion state name (with faction override support)
                            string locomotionStateName = _animationSettings.locomotionStateName;
                            if (faction != null && !string.IsNullOrWhiteSpace(faction.locomotionStateNameOverride))
                                locomotionStateName = faction.locomotionStateNameOverride;

                            if (!string.IsNullOrWhiteSpace(locomotionStateName))
                            {
                                int locomotionHash = Animator.StringToHash(locomotionStateName);
                                if (agent.mecanimAnimator.HasState(0, locomotionHash))
                                {
                                    // Force crossfade back to locomotion state
                                    agent.mecanimAnimator.CrossFade(locomotionHash, 0.15f, 0, 0f);
                                    
                                    // Update Speed parameter for blend tree
                                    if (normalizedSpeed > 0.1f)
                                    {
                                        agent.mecanimAnimator.SetFloat("Speed", normalizedSpeed);
                                        agent.currentState = CrowdAnimationState.Locomotion;
                                    }
                                    else
                                    {
                                        agent.mecanimAnimator.SetFloat("Speed", 0f);
                                        agent.currentState = CrowdAnimationState.Idle;
                                    }
                                }
                            }
                        }
                    }
                    // Handle Compute Animator workflow (GPU)
                    else if (agent.currentWorkflow == HybridWorkflowType.ComputeAnimator &&
                             agent.crowdInstance != null && agent.crowdInstance.AnimatorWorkflow != null)
                    {
                        EnemyCrowd faction = agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count
                            ? _enemyCrowds[agent.enemyCrowdIndex]
                            : null;

                        AnimationClip transitionClip = null;
                        if (normalizedSpeed > 0.1f)
                        {
                            transitionClip = faction?.walkClipOverride ?? _animationSettings.walkClip;
                            if (transitionClip == null)
                                transitionClip = faction?.runClipOverride ?? _animationSettings.runClip;
                            agent.currentState = CrowdAnimationState.Locomotion;
                        }
                        else
                        {
                            transitionClip = faction?.idleClipOverride ?? _animationSettings.idleClip;
                            agent.currentState = CrowdAnimationState.Idle;
                        }

                        if (transitionClip != null && IsBufferIndexValid(agent))
                        {
                            agent.crowdInstance.StartAnimation(transitionClip, -1f, 1f, 0.2f, null, true);
                        }
                    }
                }
            }
            
            // Clean up positioning state on death
            if (nextState == CrowdCombatState.Dead)
            {
                _positioningSystem?.ClearAgentState(agent.index);
            }

            OnCombatStateChanged?.Invoke(agent, previousState, nextState);
        }

        #endregion

        #region Attack System

        /// <summary>
        /// Returns the maximum range across all enabled skill slots for the given faction.
        /// Result is cached per-frame (refreshed every 2 seconds) to avoid repeated iteration.
        /// Returns 0 if the skill system is disabled or no skills have a positive range.
        /// </summary>
        private float GetMaxSkillRange(EnemyCrowd faction)
        {
            if (faction == null || faction.skillSystemConfig == null || !faction.skillSystemConfig.enableSkillSystem)
                return 0f;
            
            var slots = faction.skillSystemConfig.skillSlots;
            if (slots == null || slots.Count == 0)
                return 0f;
            
            // Refresh cache every 2 seconds (skill configuration rarely changes at runtime)
            float now = Time.time;
            if (now - _lastSkillRangeCacheTime > 2f)
            {
                _cachedMaxSkillRange.Clear();
                _lastSkillRangeCacheTime = now;
            }
            
            if (_cachedMaxSkillRange.TryGetValue(faction, out float cached))
                return cached;
            
            float maxRange = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.enabled || slot.skill == null) continue;
                float r = slot.skill.MaxRange;
                if (r > maxRange) maxRange = r;
            }
            
            _cachedMaxSkillRange[faction] = maxRange;
            return maxRange;
        }

        /// <summary>
        /// Returns the effective combat engagement radius for a faction, taking into account
        /// skill ranges. If a faction has skills with a range exceeding its engagement radius,
        /// the max skill range is used instead. This ensures agents enter combat mode early
        /// enough to fire long-range skills (e.g., rockets, projectiles).
        /// </summary>
        private float GetEffectiveEngagementRadius(EnemyCrowd faction)
        {
            float baseRadius = faction?.engagementRadius ?? _settings.engagementRadius;
            float maxSkillRange = GetMaxSkillRange(faction);
            return Mathf.Max(baseRadius, maxSkillRange);
        }

        /// <summary>
        /// Attempts to execute a skill before basic attack.
        /// Called when an agent is about to perform a basic attack.
        /// If a skill is available and ready, it will be executed instead of the basic attack.
        /// </summary>
        /// <param name="agent">The agent about to attack</param>
        /// <param name="faction">The agent's faction (EnemyCrowd)</param>
        /// <returns>True if a skill was executed, false to proceed with basic attack</returns>
        private bool TryExecuteSkillBeforeBasicAttack(CrowdAgent agent, EnemyCrowd faction)
        {
            // Quick check: Is skill system enabled for this faction?
            if (faction == null || faction.skillSystemConfig == null || !faction.skillSystemConfig.enableSkillSystem)
                return false;
            
            if (faction.skillSystemConfig.skillSlots == null || faction.skillSystemConfig.skillSlots.Count == 0)
                return false;

            // Get or create agent's skill state
            if (!_agentSkillStates.TryGetValue(agent.index, out var agentState))
            {
                agentState = new SkillRuntimeState();
                _agentSkillStates[agent.index] = agentState;
            }
            
            // Get faction cooldowns
            _factionSkillCooldowns.TryGetValue(faction.factionName ?? "", out var factionCooldowns);
            _enemyCrowdSkillCooldowns.TryGetValue(faction, out var enemyCrowdCooldowns);
            
            // Gather nearby agents for skill targeting
            // Ensure gather radius covers the longest skill range so projectile skills
            // can find targets at their full distance (e.g., 60m rockets).
            float baseGatherRadius = faction.skillSystemConfig.skillDecisionInterval > 0 ? 50f : 30f;
            float maxSkillRange = GetMaxSkillRange(faction);
            float gatherRadius = Mathf.Max(baseGatherRadius, maxSkillRange + 5f);
            GatherNearbyAgentsForSkills(agent, gatherRadius);
            
            // Try to execute a skill
            var result = SkillExecutor.TryExecuteSkillBeforeAttack(
                agent,
                agentState,
                factionCooldowns,
                enemyCrowdCooldowns,
                _skillTargetBuffer,
                EnemyMassesCrowdController.Instance
            );
            
            return result == SkillExecutor.SkillAttemptResult.SkillExecuted;
        }

        /// <summary>
        /// Returns true if the agent is currently fighting in ranged mode.
        /// Includes Hybrid agents while their active mode is ranged.
        /// </summary>
        private bool IsAgentInRangedCombatMode(CrowdAgent agent, EnemyCrowd faction)
        {
            if (agent == null || faction == null)
                return false;

            return faction.combatRangeType == CombatRangeType.Ranged ||
                   (faction.combatRangeType == CombatRangeType.Hybrid &&
                    agent.activeHybridMode == CombatRangeType.Ranged);
        }

        /// <summary>
        /// Returns true when legacy basic attacks should be skipped and the agent should wait for skills.
        /// </summary>
        private bool ShouldSuppressLegacyBasicAttack(CrowdAgent agent, EnemyCrowd faction)
        {
            if (agent == null || faction == null)
                return false;

            bool isRangedMode = IsAgentInRangedCombatMode(agent, faction);
            if (isRangedMode)
            {
                return faction.rangedAttackMode == RangedAttackMode.DisabledUseSkills;
            }

            return !GetEnableMeleeAutoAttack(agent);
        }

        /// <summary>
        /// Delays the next attack check so skills-only ranged agents do not re-evaluate every frame.
        /// </summary>
        private void DelayNextSkillOnlyAttackAttempt(CrowdAgent agent, EnemyCrowd faction, float currentTime)
        {
            if (agent == null)
                return;

            float retryDelay = 0.25f;
            var skillConfig = faction?.skillSystemConfig;
            if (skillConfig != null && skillConfig.enableSkillSystem && skillConfig.skillDecisionInterval > 0f)
            {
                retryDelay = Mathf.Max(0.05f, skillConfig.skillDecisionInterval * 0.5f);
            }

            agent.nextAttackTime = currentTime + retryDelay;
        }

        /// <summary>
        /// Gathers nearby agents for skill targeting into the reusable buffer.
        /// </summary>
        private void GatherNearbyAgentsForSkills(CrowdAgent agent, float radius)
        {
            _skillTargetBuffer.Clear();
            
            if (agent == null || agent.navigationAgent == null) return;
            
            Vector3 agentPos = agent.WorldPosition;
            float radiusSqr = radius * radius;

            var controller = EnemyMassesCrowdController.Instance;
            if (controller != null && controller.IsSpatialGridEnabled)
            {
                var nearbyIndices = controller.GetAgentsInRadius(agentPos, radius);
                for (int i = 0; i < nearbyIndices.Count; i++)
                {
                    int index = nearbyIndices[i];
                    if (index < 0 || index >= _agents.Count)
                        continue;

                    var otherAgent = _agents[index];
                    if (otherAgent == null || otherAgent == agent || otherAgent.isDead) continue;
                    if (otherAgent.navigationAgent == null) continue;
                    
                    _skillTargetBuffer.Add(otherAgent);
                }

                return;
            }

            foreach (var otherAgent in _agents)
            {
                if (otherAgent == null || otherAgent == agent || otherAgent.isDead) continue;
                if (otherAgent.navigationAgent == null) continue;
                
                float sqrDist = (agentPos - otherAgent.WorldPosition).sqrMagnitude;
                if (sqrDist <= radiusSqr)
                {
                    _skillTargetBuffer.Add(otherAgent);
                }
            }
        }
        
        /// <summary>
        /// Updates skill cooldowns. Called each frame from UpdateCombat.
        /// </summary>
        private void UpdateSkillCooldowns(float deltaTime)
        {
            // Update faction cooldowns
            foreach (var cooldowns in _factionSkillCooldowns.Values)
            {
                cooldowns.Update(deltaTime);
            }
            
            // Update per-crowd cooldowns
            foreach (var cooldowns in _enemyCrowdSkillCooldowns.Values)
            {
                cooldowns.Update(deltaTime);
            }
            
            // Update per-agent cooldowns
            foreach (var state in _agentSkillStates.Values)
            {
                state.Update(deltaTime);
            }
        }
        
        /// <summary>
        /// Processes all agents that are currently casting a skill (cast time &gt; 0).
        /// Runs every frame (not batched) so cast windows complete precisely.
        /// Handles:
        ///   1. Cast completion — fires deferred OnHit / OnFinish / projectile / animation.
        ///   2. Movement interruption — if CastInterruptedByMovement is set and agent moved.
        /// Damage-based interruption is handled in <see cref="ApplyDamageToAgent"/>.
        /// </summary>
        private void ProcessCastingAgents(float currentTime)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent == null || !agent.isCasting) continue;
                
                // ── Movement Interrupt ──
                if (agent.castingSkill != null && agent.castingSkill.CastInterruptedByMovement)
                {
                    var nav = agent.navigationAgent;
                    if (nav != null)
                    {
                        // Detect if the agent has significant velocity (player issued a move command)
                        float sqrSpeed = nav.Velocity.sqrMagnitude;
                        if (sqrSpeed > 0.01f)
                        {
                            SkillExecutor.InterruptCast(agent);
                            continue;
                        }
                    }
                }
                
                // ── Cast Complete ──
                if (currentTime >= agent.castEndTime)
                {
                    SkillExecutor.CompleteCast(agent);
                }
            }
        }
        
        /// <summary>
        /// Cleans up skill state for a dead or removed agent.
        /// </summary>
        public void CleanupAgentSkillState(int agentIndex)
        {
            _agentSkillStates.Remove(agentIndex);
        }

        /// <summary>
        /// Gets the remaining cooldown for a skill on a specific agent.
        /// Used by AgentSkillBarUI for cooldown display without requiring FactionSkillManager.
        /// </summary>
        public float GetSkillCooldownRemaining(CrowdAgent agent, int skillSlotIndex)
        {
            if (agent == null || agent.enemyCrowd == null) return 0f;

            var skillConfig = agent.enemyCrowd.skillSystemConfig;
            if (skillConfig == null || skillSlotIndex < 0 || skillSlotIndex >= skillConfig.skillSlots.Count)
                return 0f;

            var slot = skillConfig.skillSlots[skillSlotIndex];
            if (slot?.skill == null) return 0f;

            _agentSkillStates.TryGetValue(agent.index, out var agentState);
            _factionSkillCooldowns.TryGetValue(agent.enemyCrowd.factionName ?? "", out var factionCooldowns);
            _enemyCrowdSkillCooldowns.TryGetValue(agent.enemyCrowd, out var enemyCrowdCooldowns);

            return SkillExecutor.GetRemainingCooldown(slot, agentState, factionCooldowns, enemyCrowdCooldowns, skillConfig.defaultCooldownMode);
        }

        private void BeginAgentAttack(CrowdAgent agent, float currentTime, Transform targetTransform = null, CrowdAgent targetAgent = null)
        {
            if (agent == null)
                return;
            
            // Status effect check: Cannot attack if crowd controlled
            if (!agent.canAttack)
                return;

            EnemyCrowd faction = GetEnemyCrowd(agent);
            bool isRangedCombatMode = IsAgentInRangedCombatMode(agent, faction);
            bool canUseRangedAutoAttack = faction != null &&
                                          isRangedCombatMode &&
                                          faction.IsPrimaryRangedAttackEnabled;
            bool canUseMeleeAutoAttack = !isRangedCombatMode && GetEnableMeleeAutoAttack(agent);

            if (!canUseRangedAutoAttack && !canUseMeleeAutoAttack)
            {
                agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                return;
            }

            // Don't clear existing target - we're updating it, not resetting it
            Vector3 targetPosition = Vector3.zero;
            bool hasExplicitTarget = false;

            if (targetTransform != null)
            {
                agent.attackTargetTransform = targetTransform;
                targetPosition = targetTransform.position;
                hasExplicitTarget = true;
            }
            else if (targetAgent != null)
            {
                agent.attackTargetAgentIndex = targetAgent.index;
                agent.attackTargetTransform = targetAgent.navigationAgent?.Transform ?? 
                                            (targetAgent.crowdInstance != null ? targetAgent.crowdInstance.transform : null);
                targetPosition = targetAgent.WorldPosition;
                hasExplicitTarget = true;
            }
            else if (agent.navigationAgent != null && agent.navigationAgent.HasPath)
            {
                targetPosition = agent.navigationAgent.SteeringTarget;
                hasExplicitTarget = true;
            }

            if (hasExplicitTarget)
            {
                agent.attackTargetPosition = targetPosition;
                agent.hasAttackTargetPosition = true;
                
                // CRITICAL: Face the target immediately before attacking (especially important for ranged attacks)
                FaceTargetImmediate(agent, targetPosition);
            }

            // Try to resolve attack clip (with directional support)
            var attackResult = ResolveAttackClip(agent);
            if (attackResult.clip == null)
            {
                agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                return;
            }

            AnimationClip clip = attackResult.clip;
            int clipIndex = attackResult.index;
            float clipDuration = _attackClipLengths.Length > clipIndex ? _attackClipLengths[clipIndex] : 
                               (clip != null ? Mathf.Max(clip.length, 0.35f) : 0.5f);
            
            agent.lastAttackClipIndex = clipIndex;
            EnterCombatState(agent, CrowdCombatState.Attacking, clipDuration, currentTime);
            agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
            
            PlayCombatClip(agent, clip, 0.05f, false, CombatAnimationType.Attack);

            // Ranged auto-attack path (hitscan/projectile/arc). Hybrid uses this only while in ranged mode.
            if (canUseRangedAutoAttack && faction != null)
            {
                // Flying immunity: ranged auto-attacks only hit flying targets when the faction allows it
                if (targetAgent != null && targetAgent.isFlying && !faction.rangedAutoAttackCanHitFlying)
                {
                    agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                    return;
                }
                
                // Fire ranged attack (hitscan or projectile) instead of melee instant damage.
                ScheduleProjectileFire(agent, faction, targetAgent, targetPosition, currentTime);
            }
            else if (canUseMeleeAutoAttack)
            {
                // MELEE ATTACK - schedule swing effects if configured
                if (faction != null && faction.meleeAttackEffectsConfig != null && faction.meleeAttackEffectsConfig.enabled)
                {
                    ScheduleMeleeSwingEffect(agent, faction, currentTime);
                }
                
                // Get attacker info for both agent and player hits
                Vector3? attackerForward = null;
                Vector3? attackerUp = null;
                Vector3? attackerPosition = null;

                Transform attackerTransform = agent.navigationAgent?.Transform ??
                                             (agent.crowdInstance != null ? agent.crowdInstance.transform : null);

                if (attackerTransform != null)
                {
                    attackerForward = attackerTransform.forward;
                    attackerUp = attackerTransform.up;
                    attackerPosition = attackerTransform.position;
                }

                // Apply instant melee damage to target agent
                if (targetAgent != null && targetAgent != agent)
                {
                    // Flying immunity: melee attacks cannot hit airborne agents
                    if (targetAgent.isFlying)
                    {
                        agent.nextAttackTime = currentTime + GetRandomizedAttackCooldown(agent);
                        return;
                    }
                    
                    // Pass attacker agent index for ally defense system
                    // Melee attacks - estimate hit point based on weapon swing height (typically chest/torso)
                    Vector3? meleeHitPoint = CalculateMeleeHitPoint(agent, targetAgent);
                    ApplyDamageToAgent(targetAgent, GetMeleeAttackDamage(agent), true, false, attackerForward, attackerUp, attackerPosition, agent.index, meleeHitPoint, true);
                    
                    // Schedule melee impact effect at hit point
                    if (faction != null && faction.meleeAttackEffectsConfig != null && faction.meleeAttackEffectsConfig.enabled && meleeHitPoint.HasValue)
                    {
                        ScheduleMeleeImpactEffect(agent, faction, meleeHitPoint.Value, currentTime);
                    }
                }
                else if (targetTransform != null && targetAgent == null)
                {
                    // Attacking a non-agent target (player target or damageable world object)
                    float damage = GetMeleeAttackDamage(agent);
                    if (faction != null)
                    {
                        damage *= faction.damageMultiplier;
                    }
                    
                    // Estimate hit point on the target (chest height)
                    Vector3 hitPoint = targetPosition + Vector3.up * 1.0f;
                    Vector3 attackerPos = attackerPosition ?? (attackerTransform != null ? attackerTransform.position : agent.transform?.position ?? Vector3.zero);

                    // Apply damage if the transform (or parent) is a damageable object (doors, props, objectives)
                    var damageable = targetTransform.GetComponentInParent<IEnemyMassesDamageable>();
                    if (damageable != null && damageable.IsAlive)
                    {
                        damageable.ApplyDamage(damage, agent, hitPoint);
                    }
                    
                    // Schedule melee impact effect at hit point on target
                    if (faction != null && faction.meleeAttackEffectsConfig != null && faction.meleeAttackEffectsConfig.enabled)
                    {
                        ScheduleMeleeImpactEffect(agent, faction, hitPoint, currentTime);
                    }
                    
                    OnPlayerTargetHit?.Invoke(agent, attackerPos, targetTransform, damage, hitPoint, true);
                }
            }
        }
        
        /// <summary>
        /// Schedules a projectile to be fired after the spawn delay.
        /// Uses the faction's GetRangedAttackMode to determine primary vs secondary attack.
        /// </summary>
        private void ScheduleProjectileFire(CrowdAgent agent, EnemyCrowd faction, CrowdAgent targetAgent, Vector3 targetPosition, float currentTime)
        {
            // Get the attack mode (primary or secondary based on chance and cooldown)
            var (attackMode, isSecondary, config) = faction.GetRangedAttackMode(agent.lastSecondaryAttackTime, currentTime);

            if (attackMode == RangedAttackMode.DisabledUseSkills)
            {
                return;
            }
            
            // Update secondary attack cooldown if used
            if (isSecondary)
            {
                agent.lastSecondaryAttackTime = currentTime;
            }
            
            // Initialize spawn points if needed (for both hitscan and projectile)
            if (!agent.projectileSpawnPointsInitialized)
            {
                InitializeProjectileSpawnPoints(agent, faction);
            }
            
            // If the chosen mode is Hitscan, apply instant damage instead of projectile
            if (attackMode == RangedAttackMode.Hitscan)
            {
                // Queue hitscan effects with delay for animation sync
                var hitscanConfig = faction.hitscanEffectsConfig;
                float effectTime = currentTime + (hitscanConfig?.effectDelay ?? 0.1f);
                
                // Get damage values - use faction damage multiplier
                float baseDamage = GetRangedAttackDamage(agent) * faction.damageMultiplier;
                
                // Only apply AOE if explicitly enabled
                bool aoeEnabled = hitscanConfig?.enableAOE ?? false;
                
                _pendingHitscanEffects.Add(new PendingHitscanEffect
                {
                    effectTime = effectTime,
                    sourceAgentIndex = agent.index,
                    targetAgentIndex = targetAgent?.index ?? -1,
                    targetPosition = targetPosition,
                    factionIndex = agent.enemyCrowdIndex,
                    config = hitscanConfig,
                    baseDamage = baseDamage,
                    aoeDamage = aoeEnabled ? (hitscanConfig?.aoeDamage ?? 0f) : 0f,
                    aoeRadius = aoeEnabled ? (hitscanConfig?.aoeRadius ?? 0f) : 0f,
                    aoeFalloff = hitscanConfig?.aoeFalloffMinDamage ?? 0.3f
                });
                return;
            }

            if (attackMode != RangedAttackMode.Projectile && attackMode != RangedAttackMode.ProjectileArc)
            {
                return;
            }

            if (config == null)
            {
                return;
            }
            
            // Calculate fire time for projectile (after spawn delay for animation sync)
            float fireTime = currentTime + config.spawnDelay;
            
            // Store pending projectile data
            _pendingProjectiles.Add(new PendingProjectile
            {
                fireTime = fireTime,
                sourceAgentIndex = agent.index,
                targetAgentIndex = targetAgent?.index ?? -1,
                targetPosition = targetPosition,
                factionIndex = agent.enemyCrowdIndex,
                config = config,
                baseDamage = GetRangedAttackDamage(agent) * faction.damageMultiplier,
                attackMode = attackMode,
                isSecondaryAttack = isSecondary
            });
        }
        
        /// <summary>
        /// Initializes the projectile spawn point transforms for an agent.
        /// Caches the transforms so we don't have to search every attack.
        /// </summary>
        private void InitializeProjectileSpawnPoints(CrowdAgent agent, EnemyCrowd faction)
        {
            agent.projectileSpawnPointsInitialized = true;
            
            Transform agentRoot = agent.navigationAgent?.Transform ??
                                 (agent.crowdInstance != null ? agent.crowdInstance.transform : null);
            
            if (agentRoot == null) return;
            
            // Initialize primary spawn point
            agent.projectileSpawnPoint = FindSpawnPointTransform(agentRoot, faction.projectileConfig);
            
            // Initialize secondary spawn point if enabled
            if (faction.enableSecondaryRangedAttack)
            {
                agent.secondaryProjectileSpawnPoint = FindSpawnPointTransform(agentRoot, faction.secondaryProjectileConfig);
            }
            
            // Initialize hitscan spawn point for muzzle effects
            agent.hitscanSpawnPoint = FindHitscanSpawnPointTransform(agentRoot, faction.hitscanEffectsConfig);
        }
        
        /// <summary>
        /// Finds the spawn point transform for hitscan effects based on the config's spawn point mode.
        /// </summary>
        private Transform FindHitscanSpawnPointTransform(Transform agentRoot, HitscanEffectsConfig config)
        {
            if (config == null) return null;
            
            switch (config.spawnPointMode)
            {
                case ProjectileSpawnPointMode.Transform:
                    if (config.spawnPointTransform != null)
                    {
                        string path = GetTransformPath(config.spawnPointTransform);
                        if (!string.IsNullOrEmpty(path))
                        {
                            return agentRoot.Find(path);
                        }
                        return FindChildByNameRecursive(agentRoot, config.spawnPointTransform.name);
                    }
                    break;
                    
                case ProjectileSpawnPointMode.BonePath:
                    if (!string.IsNullOrEmpty(config.spawnBonePath))
                    {
                        return agentRoot.Find(config.spawnBonePath);
                    }
                    break;
                    
                case ProjectileSpawnPointMode.BoneName:
                    if (!string.IsNullOrEmpty(config.spawnBoneName))
                    {
                        return FindChildByNameRecursive(agentRoot, config.spawnBoneName);
                    }
                    break;
                    
                case ProjectileSpawnPointMode.Offset:
                default:
                    return null;
            }
            
            return null;
        }
        
        /// <summary>
        /// Finds the spawn point transform based on the config's spawn point mode.
        /// </summary>
        private Transform FindSpawnPointTransform(Transform agentRoot, ProjectileAttackConfig config)
        {
            if (config == null) return null;
            
            switch (config.spawnPointMode)
            {
                case ProjectileSpawnPointMode.Transform:
                    // The config references a prefab transform - find the same path on this instance
                    if (config.spawnPointTransform != null)
                    {
                        // Get the relative path from the prefab's spawn point to find on the instance
                        string path = GetTransformPath(config.spawnPointTransform);
                        if (!string.IsNullOrEmpty(path))
                        {
                            return agentRoot.Find(path);
                        }
                        // Fallback: search by name if path doesn't work
                        return FindChildByNameRecursive(agentRoot, config.spawnPointTransform.name);
                    }
                    break;
                    
                case ProjectileSpawnPointMode.BonePath:
                    if (!string.IsNullOrEmpty(config.spawnBonePath))
                    {
                        return agentRoot.Find(config.spawnBonePath);
                    }
                    break;
                    
                case ProjectileSpawnPointMode.BoneName:
                    if (!string.IsNullOrEmpty(config.spawnBoneName))
                    {
                        return FindChildByNameRecursive(agentRoot, config.spawnBoneName);
                    }
                    break;
                    
                case ProjectileSpawnPointMode.Offset:
                default:
                    // No transform needed, will use offset from root
                    return null;
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets the relative path of a transform from its root (prefab root).
        /// Used to find the equivalent transform on spawned instances.
        /// </summary>
        private string GetTransformPath(Transform transform)
        {
            if (transform == null) return "";
            
            // Build path from transform up to root
            System.Text.StringBuilder path = new System.Text.StringBuilder();
            Transform current = transform;
            
            while (current.parent != null)
            {
                if (path.Length > 0)
                    path.Insert(0, "/");
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return path.ToString();
        }
        
        /// <summary>
        /// Recursively searches for a child transform by name.
        /// </summary>
        private Transform FindChildByNameRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                    
                var found = FindChildByNameRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
        
        /// <summary>
        /// Gets the world position for projectile spawn based on config and cached transforms.
        /// </summary>
        private Vector3 GetProjectileSpawnPosition(CrowdAgent agent, ProjectileAttackConfig config, bool isSecondary)
        {
            Transform agentRoot = agent.navigationAgent?.Transform ??
                                 (agent.crowdInstance != null ? agent.crowdInstance.transform : null);
            
            if (agentRoot == null)
                return Vector3.zero;
            
            // Get the cached spawn point transform
            Transform spawnPoint = isSecondary ? agent.secondaryProjectileSpawnPoint : agent.projectileSpawnPoint;
            
            if (spawnPoint != null)
            {
                // Use the spawn point position directly
                return spawnPoint.position;
            }
            else
            {
                // Fallback to offset from root
                return agentRoot.position + agentRoot.TransformDirection(config.spawnOffset);
            }
        }
        
        /// <summary>
        /// Processes pending projectiles and fires them when their delay has elapsed.
        /// </summary>
        private void ProcessPendingProjectiles(float currentTime)
        {
            if (_pendingProjectiles.Count == 0) return;
            
            var projectileSystem = EnemyMassesProjectileSystem.Instance;
            if (projectileSystem == null)
            {
                return;
            }
            
            for (int i = _pendingProjectiles.Count - 1; i >= 0; i--)
            {
                var pending = _pendingProjectiles[i];
                
                if (currentTime >= pending.fireTime)
                {
                    // Get source agent
                    CrowdAgent sourceAgent = pending.sourceAgentIndex >= 0 && pending.sourceAgentIndex < _agents.Count 
                                            ? _agents[pending.sourceAgentIndex] : null;
                    
                    if (sourceAgent == null || (sourceAgent.navigationAgent == null && sourceAgent.crowdInstance == null))
                    {
                        _pendingProjectiles.RemoveAt(i);
                        continue;
                    }
                    
                    // Get spawn position using cached spawn point or fallback to offset
                    Vector3 startPosition = GetProjectileSpawnPosition(sourceAgent, pending.config, pending.isSecondaryAttack);
                    
                    if (startPosition == Vector3.zero)
                    {
                        _pendingProjectiles.RemoveAt(i);
                        continue;
                    }
                    
                    // Get target position (update from moving target if applicable)
                    Vector3 targetPos = pending.targetPosition;
                    if (pending.targetAgentIndex >= 0 && pending.targetAgentIndex < _agents.Count)
                    {
                        CrowdAgent targetAgent = _agents[pending.targetAgentIndex];
                        if (targetAgent?.navigationAgent != null && !targetAgent.isDead)
                        {
                            // Update to current target position
                            targetPos = targetAgent.WorldPosition + Vector3.up * 1f;
                        }
                    }
                    
                    // Register effects config using VFXReference (supports both ParticleSystem and VFX Graph)
                    int effectsConfigId = projectileSystem.RegisterEffectsConfig(
                        pending.config.GetEffectiveProjectileVisual(),
                        pending.config.GetEffectiveImpactVFX(),
                        pending.config.GetEffectiveTrailVFX(),
                        pending.config.fireSound,
                        pending.config.impactSound,
                        pending.config.fireSoundVolume,
                        pending.config.impactSoundVolume,
                        pending.config.enableAOE ? pending.config.GetEffectiveAoeVFX() : null,  // Only include AOE VFX if AOE is enabled
                        pending.config.fireSoundPitchVariation,
                        pending.config.impactSoundPitchVariation,
                        pending.config.fireSoundSpatialBlend,
                        pending.config.impactSoundSpatialBlend,
                        pending.config.visualScale
                    );
                    
                    // Get the projectile prefab from VFXReference (only ParticleSystem type for now - VFX Graph projectiles not yet supported)
                    var projectileVisual = pending.config.GetEffectiveProjectileVisual();
                    GameObject projectilePrefab = projectileVisual?.IsParticleSystem == true ? projectileVisual.particleSystemPrefab : null;
                    
                    // Determine AOE radius - only apply if enableAOE is true
                    float effectiveAoeRadius = pending.config.enableAOE ? pending.config.aoeRadius : 0f;
                    
                    if (pending.attackMode == RangedAttackMode.Projectile)
                    {
                        int projectileId = projectileSystem.FireLinearProjectile(
                            startPosition,
                            targetPos,
                            pending.config.projectileSpeed,
                            pending.baseDamage,
                            pending.sourceAgentIndex,
                            pending.targetAgentIndex,
                            pending.factionIndex,
                            5f, // lifetime
                            true, // spawn visual
                            projectilePrefab, // custom prefab from EnemyCrowd (ParticleSystem only)
                            pending.config.visualScale,  // visual scale
                            effectsConfigId,  // effects config for VFX and audio
                            effectiveAoeRadius  // AOE radius (0 if disabled)
                        );
                    }
                    else // ProjectileArc
                    {
                        float? flightTime = pending.config.fixedFlightTime > 0 ? pending.config.fixedFlightTime : (float?)null;
                        int projectileId = projectileSystem.FireParabolicProjectile(
                            startPosition,
                            targetPos,
                            pending.baseDamage,
                            effectiveAoeRadius,  // AOE radius (0 if disabled)
                            pending.sourceAgentIndex,
                            pending.targetAgentIndex,
                            pending.factionIndex,
                            flightTime,
                            pending.config.launchAngle,
                            true, // spawn visual
                            projectilePrefab, // custom prefab from EnemyCrowd (ParticleSystem only)
                            pending.config.visualScale,  // visual scale
                            effectsConfigId  // effects config for VFX and audio
                        );
                    }
                    
                    _pendingProjectiles.RemoveAt(i);
                }
            }
        }
        
        /// <summary>
        /// Processes pending hitscan effects - spawns muzzle VFX/sound, applies damage, spawns impact VFX/sound.
        /// Supports both ParticleSystem prefabs and VFX Graph assets via VFXReference.
        /// </summary>
        private void ProcessPendingHitscanEffects(float currentTime)
        {
            if (_pendingHitscanEffects.Count == 0) return;
            
            for (int i = _pendingHitscanEffects.Count - 1; i >= 0; i--)
            {
                var pending = _pendingHitscanEffects[i];
                
                if (currentTime >= pending.effectTime)
                {
                    // Get source agent
                    CrowdAgent sourceAgent = pending.sourceAgentIndex >= 0 && pending.sourceAgentIndex < _agents.Count 
                                            ? _agents[pending.sourceAgentIndex] : null;
                    
                    if (sourceAgent == null || (sourceAgent.navigationAgent == null && sourceAgent.crowdInstance == null))
                    {
                        _pendingHitscanEffects.RemoveAt(i);
                        continue;
                    }
                    
                    // Get spawn position for muzzle effects
                    Vector3 muzzlePosition = GetHitscanSpawnPosition(sourceAgent, pending.config);
                    
                    // Spawn muzzle VFX (using VFXReference for both ParticleSystem and VFX Graph support)
                    var muzzleVFX = pending.config?.GetEffectiveMuzzleVFX();
                    if (muzzleVFX != null && muzzleVFX.IsValid && muzzlePosition != Vector3.zero)
                    {
                        Transform agentTransform = sourceAgent.navigationAgent?.Transform ??
                                                  (sourceAgent.crowdInstance != null ? sourceAgent.crowdInstance.transform : null);
                        Vector3 direction = agentTransform != null ? agentTransform.forward : Vector3.forward;
                        
                        float duration = pending.config.muzzleVFXDuration > 0 ? pending.config.muzzleVFXDuration : muzzleVFX.GetDuration();
                        SpawnVFX(muzzleVFX, muzzlePosition, direction, duration);
                    }
                    
                    // Play fire sound
                    if (pending.config?.fireSound != null && muzzlePosition != Vector3.zero)
                    {
                        EnemyCrowd sourceFaction = pending.factionIndex >= 0 && pending.factionIndex < _enemyCrowds.Count 
                                                   ? _enemyCrowds[pending.factionIndex] : null;
                        PlaySoundAtPosition(pending.config.fireSound, muzzlePosition, pending.config.fireSoundVolume, pending.config.fireSoundPitchVariation, pending.config.fireSoundSpatialBlend, sourceFaction);
                    }
                    
                    // Get target position (update from moving target if applicable)
                    Vector3 targetPos = pending.targetPosition;
                    CrowdAgent targetAgent = null;
                    if (pending.targetAgentIndex >= 0 && pending.targetAgentIndex < _agents.Count)
                    {
                        targetAgent = _agents[pending.targetAgentIndex];
                        if (targetAgent?.navigationAgent != null && !targetAgent.isDead)
                        {
                            targetPos = targetAgent.WorldPosition + Vector3.up * 1f;
                        }
                    }
                    
                    Vector3? attackerForward = null;
                    Vector3? attackerUp = null;
                    Vector3? attackerPosition = null;
                    Transform attackerTransform = sourceAgent.navigationAgent?.Transform ??
                                                 (sourceAgent.crowdInstance != null ? sourceAgent.crowdInstance.transform : null);
                    if (attackerTransform != null)
                    {
                        attackerForward = attackerTransform.forward;
                        attackerUp = attackerTransform.up;
                        attackerPosition = attackerTransform.position;
                    }
                    
                    // Apply instant damage to primary target
                    if (targetAgent != null && targetAgent != sourceAgent && !targetAgent.isDead)
                    {
                        // Pass source agent index for ally defense system and hit point for virtual hitbox
                        ApplyDamageToAgent(targetAgent, pending.baseDamage, true, false, attackerForward, attackerUp, attackerPosition, pending.sourceAgentIndex, targetPos, false);
                        
                        // Spawn impact VFX at target position (using VFXReference)
                        var impactVFX = pending.config?.GetEffectiveImpactVFX();
                        if (impactVFX != null && impactVFX.IsValid)
                        {
                            float duration = pending.config.impactVFXDuration > 0 ? pending.config.impactVFXDuration : impactVFX.GetDuration();
                            Vector3 hitNormal = (muzzlePosition - targetPos).normalized;
                            SpawnVFX(impactVFX, targetPos, hitNormal, duration);
                        }
                        
                        // Play impact sound at target position
                        if (pending.config?.impactSound != null)
                        {
                            EnemyCrowd impactFaction = pending.factionIndex >= 0 && pending.factionIndex < _enemyCrowds.Count 
                                                       ? _enemyCrowds[pending.factionIndex] : null;
                            PlaySoundAtPosition(pending.config.impactSound, targetPos, pending.config.impactSoundVolume, pending.config.impactSoundPitchVariation, pending.config.impactSoundSpatialBlend, impactFaction);
                        }
                    }
                    else if (targetAgent == null)
                    {
                        // Target is the player (hitscan ranged attack)
                        // Fire event so external systems (like GC2) can apply damage to the player
                        EnemyCrowd faction = pending.factionIndex >= 0 && pending.factionIndex < _enemyCrowds.Count 
                                            ? _enemyCrowds[pending.factionIndex] : null;
                        float damage = pending.baseDamage;
                        Vector3 attackerPos = attackerPosition ?? muzzlePosition;
                        Transform playerTarget = sourceAgent.attackTargetTransform != null 
                            ? sourceAgent.attackTargetTransform 
                            : GetNearestPlayerTarget(targetPos);
                        if (playerTarget == null)
                        {
                            _pendingHitscanEffects.RemoveAt(i);
                            continue;
                        }
                        
                        #if UNITY_EDITOR || DEVELOPMENT_BUILD
                        int subscriberCount = OnPlayerTargetHit?.GetInvocationList()?.Length ?? 0;
                        Debug.Log($"[EnemyMassesCombatSystem] Hitscan ranged hit on player target! Damage: {damage}, Subscribers: {subscriberCount}");
                        #endif
                        
                        OnPlayerTargetHit?.Invoke(sourceAgent, attackerPos, playerTarget, damage, targetPos, false);
                        
                        // Spawn impact VFX at target position (using VFXReference)
                        var impactVFX = pending.config?.GetEffectiveImpactVFX();
                        if (impactVFX != null && impactVFX.IsValid)
                        {
                            float duration = pending.config.impactVFXDuration > 0 ? pending.config.impactVFXDuration : impactVFX.GetDuration();
                            Vector3 hitNormal = (muzzlePosition - targetPos).normalized;
                            SpawnVFX(impactVFX, targetPos, hitNormal, duration);
                        }
                        
                        // Play impact sound at target position
                        if (pending.config?.impactSound != null)
                        {
                            EnemyCrowd impactFaction2 = pending.factionIndex >= 0 && pending.factionIndex < _enemyCrowds.Count 
                                                        ? _enemyCrowds[pending.factionIndex] : null;
                            PlaySoundAtPosition(pending.config.impactSound, targetPos, pending.config.impactSoundVolume, pending.config.impactSoundPitchVariation, pending.config.impactSoundSpatialBlend, impactFaction2);
                        }
                    }
                    
                    // Apply AOE/splash damage if configured
                    if (pending.aoeRadius > 0f && pending.aoeDamage > 0f)
                    {
                        ApplyHitscanAOEDamage(
                            targetPos, 
                            pending.aoeRadius, 
                            pending.aoeDamage, 
                            pending.aoeFalloff,
                            pending.factionIndex, 
                            pending.sourceAgentIndex,
                            pending.targetAgentIndex // Exclude primary target (already damaged)
                        );
                        
                        // Spawn AOE VFX if configured (using VFXReference)
                        var aoeVFX = pending.config?.GetEffectiveAoeVFX();
                        if (aoeVFX != null && aoeVFX.IsValid)
                        {
                            SpawnAOEVFX(aoeVFX, targetPos, pending.aoeRadius);
                        }
                    }
                    
                    _pendingHitscanEffects.RemoveAt(i);
                }
            }
        }
        
        /// <summary>
        /// Schedules a melee swing effect to play after the configured delay.
        /// </summary>
        private void ScheduleMeleeSwingEffect(CrowdAgent agent, EnemyCrowd faction, float currentTime)
        {
            if (agent == null || faction == null || faction.meleeAttackEffectsConfig == null)
                return;
            
            var config = faction.meleeAttackEffectsConfig;
            float effectTime = currentTime + config.swingEffectDelay;
            
            _pendingMeleeSwingEffects.Add(new PendingMeleeSwingEffect
            {
                effectTime = effectTime,
                sourceAgentIndex = agent.index,
                factionIndex = agent.enemyCrowdIndex,
                config = config
            });
        }
        
        /// <summary>
        /// Schedules a melee impact effect to play at the hit point.
        /// Impact effects fire immediately (or with minimal delay) when damage is dealt.
        /// </summary>
        private void ScheduleMeleeImpactEffect(CrowdAgent agent, EnemyCrowd faction, Vector3 hitPoint, float currentTime)
        {
            if (agent == null || faction == null || faction.meleeAttackEffectsConfig == null)
                return;
            
            var config = faction.meleeAttackEffectsConfig;
            // Impact effects fire immediately (no delay since they happen at moment of damage)
            float effectTime = currentTime;
            
            _pendingMeleeImpactEffects.Add(new PendingMeleeImpactEffect
            {
                effectTime = effectTime,
                sourceAgentIndex = agent.index,
                hitPoint = hitPoint,
                factionIndex = agent.enemyCrowdIndex,
                config = config
            });
        }
        
        /// <summary>
        /// Calculates the hit point for a melee attack based on attacker and target positions.
        /// Returns a point at torso/chest height on the target.
        /// </summary>
        private Vector3? CalculateMeleeHitPoint(CrowdAgent attacker, CrowdAgent target)
        {
            if (target == null) return null;
            
            Transform targetTransform = target.navigationAgent?.Transform ??
                                       (target.crowdInstance != null ? target.crowdInstance.transform : null);
            if (targetTransform == null) return null;
            
            // Get attacker transform for direction calculation
            Transform attackerTransform = attacker?.navigationAgent?.Transform ??
                                         (attacker?.crowdInstance != null ? attacker.crowdInstance.transform : null);
            
            // Hit point is at chest height on the target, slightly toward the attacker
            Vector3 targetPos = targetTransform.position;
            float chestHeight = 1.2f; // Approximate chest height
            
            Vector3 hitPoint = targetPos + Vector3.up * chestHeight;
            
            // Offset slightly toward attacker for better visual effect
            if (attackerTransform != null)
            {
                Vector3 toAttacker = (attackerTransform.position - targetPos).normalized;
                hitPoint += toAttacker * 0.2f; // Small offset toward attacker
            }
            
            return hitPoint;
        }
        
        /// <summary>
        /// Processes pending melee swing and impact effects.
        /// </summary>
        private void ProcessPendingMeleeEffects(float currentTime)
        {
            // Process swing effects (weapon whoosh sounds, swing trails)
            for (int i = _pendingMeleeSwingEffects.Count - 1; i >= 0; i--)
            {
                var pending = _pendingMeleeSwingEffects[i];
                
                if (currentTime >= pending.effectTime)
                {
                    // Get source agent
                    CrowdAgent sourceAgent = pending.sourceAgentIndex >= 0 && pending.sourceAgentIndex < _agents.Count 
                                            ? _agents[pending.sourceAgentIndex] : null;
                    
                    if (sourceAgent != null)
                    {
                        Transform agentTransform = sourceAgent.navigationAgent?.Transform ??
                                                  (sourceAgent.crowdInstance != null ? sourceAgent.crowdInstance.transform : null);
                        
                        if (agentTransform != null)
                        {
                            // Calculate swing effect position (offset from agent)
                            Vector3 swingPosition = agentTransform.position + agentTransform.TransformDirection(pending.config.swingVFXOffset);
                            Vector3 swingDirection = agentTransform.forward;
                            
                            // Spawn swing VFX (weapon trail, energy effect, etc.)
                            var swingVFX = pending.config.GetEffectiveSwingVFX();
                            if (swingVFX != null && swingVFX.IsValid)
                            {
                                float duration = pending.config.swingVFXDuration > 0 ? pending.config.swingVFXDuration : swingVFX.GetDuration();
                                SpawnVFX(swingVFX, swingPosition, swingDirection, duration);
                            }
                            
                            // Play swing sound (weapon whoosh)
                            if (pending.config.swingSound != null)
                            {
                                EnemyCrowd swingFaction = pending.factionIndex >= 0 && pending.factionIndex < _enemyCrowds.Count 
                                                          ? _enemyCrowds[pending.factionIndex] : null;
                                PlaySoundAtPosition(pending.config.swingSound, swingPosition, 
                                    pending.config.swingSoundVolume, 
                                    pending.config.swingSoundPitchVariation, 
                                    pending.config.swingSoundSpatialBlend,
                                    swingFaction);
                            }
                        }
                    }
                    
                    _pendingMeleeSwingEffects.RemoveAt(i);
                }
            }
            
            // Process impact effects (hit impacts, blood splatter)
            for (int i = _pendingMeleeImpactEffects.Count - 1; i >= 0; i--)
            {
                var pending = _pendingMeleeImpactEffects[i];
                
                if (currentTime >= pending.effectTime)
                {
                    // Get source agent for direction calculation
                    CrowdAgent sourceAgent = pending.sourceAgentIndex >= 0 && pending.sourceAgentIndex < _agents.Count 
                                            ? _agents[pending.sourceAgentIndex] : null;
                    
                    Transform attackerTransform = sourceAgent?.navigationAgent?.Transform ??
                                                 (sourceAgent?.crowdInstance != null ? sourceAgent.crowdInstance.transform : null);
                    
                    // Calculate impact direction (from attacker to hit point)
                    Vector3 impactDirection = Vector3.up;
                    if (attackerTransform != null)
                    {
                        impactDirection = (pending.hitPoint - attackerTransform.position).normalized;
                    }
                    
                    // Spawn hit impact VFX (blood, sparks, etc.)
                    var impactVFX = pending.config.GetEffectiveHitImpactVFX();
                    if (impactVFX != null && impactVFX.IsValid)
                    {
                        float duration = pending.config.hitImpactVFXDuration > 0 ? pending.config.hitImpactVFXDuration : impactVFX.GetDuration();
                        SpawnVFX(impactVFX, pending.hitPoint, impactDirection, duration);
                    }
                    
                    // Play hit impact sound
                    if (pending.config.hitImpactSound != null)
                    {
                        EnemyCrowd hitFaction = pending.factionIndex >= 0 && pending.factionIndex < _enemyCrowds.Count 
                                                ? _enemyCrowds[pending.factionIndex] : null;
                        PlaySoundAtPosition(pending.config.hitImpactSound, pending.hitPoint, 
                            pending.config.hitImpactSoundVolume, 
                            pending.config.hitImpactSoundPitchVariation, 
                            pending.config.hitImpactSoundSpatialBlend,
                            hitFaction);
                    }
                    
                    _pendingMeleeImpactEffects.RemoveAt(i);
                }
            }
        }
        
        /// <summary>
        /// Applies AOE damage for hitscan attacks (shotguns, explosives, energy weapons).
        /// Efficiently iterates through agents using spatial queries.
        /// </summary>
        private void ApplyHitscanAOEDamage(Vector3 center, float radius, float damage, float falloffMin, int sourceFactionIndex, int sourceAgentIndex, int excludeAgentIndex)
        {
            if (radius <= 0f || damage <= 0f) return;
            
            float radiusSqr = radius * radius;
            
            // Iterate through all agents (could be optimized with spatial partitioning for very large crowds)
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                
                // Skip invalid, dead, same faction, source, or already-hit primary target
                if (agent == null || agent.isDead || agent.navigationAgent == null)
                    continue;
                if (agent.enemyCrowdIndex == sourceFactionIndex)
                    continue;
                if (i == sourceAgentIndex || i == excludeAgentIndex)
                    continue;
                
                Vector3 agentPos = agent.WorldPosition;
                float distSqr = (agentPos - center).sqrMagnitude;
                
                if (distSqr <= radiusSqr)
                {
                    // Calculate falloff damage based on distance
                    float distance = Mathf.Sqrt(distSqr);
                    float falloff = 1f - (distance / radius);
                    float actualDamage = damage * Mathf.Max(falloffMin, falloff);
                    
                    // Apply damage - pass source agent index for ally defense system and hit position for virtual hitbox
                    Vector3 damageDirection = (agentPos - center).normalized;
                    // AOE hits are typically at ground level or center mass, so estimate hit point
                    Vector3 hitPoint = agentPos + Vector3.up * 1.0f; // Hit at torso level
                    ApplyDamageToAgent(agent, actualDamage, true, false, damageDirection, Vector3.up, center, sourceAgentIndex, hitPoint, false);
                }
            }
        }
        
        /// <summary>
        /// Gets the spawn position for hitscan muzzle effects.
        /// </summary>
        private Vector3 GetHitscanSpawnPosition(CrowdAgent agent, HitscanEffectsConfig config)
        {
            if (agent == null) return Vector3.zero;
            
            Transform agentRoot = agent.navigationAgent?.Transform ??
                                 (agent.crowdInstance != null ? agent.crowdInstance.transform : null);
            if (agentRoot == null) return Vector3.zero;
            
            // Try to use cached spawn point first
            if (agent.hitscanSpawnPoint != null)
            {
                return agent.hitscanSpawnPoint.position + (config?.spawnOffset ?? Vector3.zero);
            }
            
            // Fallback to offset from agent root
            Vector3 offset = config?.spawnOffset ?? new Vector3(0f, 1.5f, 0.5f);
            return agentRoot.position + agentRoot.TransformDirection(offset);
        }

        private void UpdateNavigationStateForCombat(CrowdAgent agent, CrowdCombatState nextState)
        {
            if (agent == null)
                return;

            switch (nextState)
            {
                case CrowdCombatState.Approach:
                case CrowdCombatState.Attacking:
                case CrowdCombatState.Blocking:
                case CrowdCombatState.Hit:
                    agent.SetNavigationState(AgentNavigationState.CombatOverride);
                    break;
                case CrowdCombatState.Idle:
                    bool shouldRemainManualControlled =
                        agent.hasManualCommand ||
                        agent.hasRTSFormationPosition ||
                        agent.brokeFromRTSFormation ||
                        agent.combatStance == CombatStance.HoldPosition;

                    // CRITICAL FIX: Agents with an explicit attack command (from skill bar or RTS attack-move)
                    // should ALWAYS continue chasing their target, even if shouldRemainManualControlled is true.
                    // Without this, HoldPosition-stance agents or agents with stale formation flags get locked
                    // into ManualCommand after skill usage, leading to "agents don't know where to go" state.
                    // Explicit attack intent is only set by explicit user action (PerformAttackCommand,
                    // CommandAgentsToChaseTarget) so it's safe to override shouldRemainManualControlled.
                    bool hasExplicitAttackOrder = agent.HasExplicitAttackIntent;

                    // Validate that hasAttackTargetPosition is backed by an actual target.
                    // The damage handler sets hasAttackTargetPosition=true for directional animation
                    // but does NOT set attackTargetAgentIndex, leaving a phantom target state.
                    // If there's no real target (no agent index and no Transform), clear the stale flag
                    // so the agent doesn't get stuck in ChasingTarget chasing nothing.
                    bool hasValidTarget = agent.hasAttackTargetPosition &&
                        (agent.attackTargetAgentIndex >= 0 || agent.attackTargetTransform != null);

                    // Additionally validate the target is actually alive — a dead target with a valid
                    // index is stale and should not keep the agent locked in chase/manual states
                    if (hasValidTarget && agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count)
                    {
                        var targetAgent = _agents[agent.attackTargetAgentIndex];
                        if (targetAgent == null || targetAgent.isDead)
                        {
                            hasValidTarget = false;
                        }
                    }

                    // Clear stale hasAttackTargetPosition if no valid target backs it
                    if (agent.hasAttackTargetPosition && !hasValidTarget)
                    {
                        agent.hasAttackTargetPosition = false;
                    }

                    if (hasValidTarget)
                    {
                        // Agents with an explicit attack order always chase, regardless of stance/flags.
                        // Hold-position/manual-locked agents WITHOUT explicit attack orders stay at position.
                        if (!hasExplicitAttackOrder && shouldRemainManualControlled && agent.combatStance == CombatStance.HoldPosition)
                        {
                            if (!agent.hasHoldPositionSet)
                            {
                                agent.holdPositionLocation = agent.WorldPosition;
                                agent.hasHoldPositionSet = true;
                            }

                            if (!agent.hasManualCommand)
                            {
                                agent.manualCommandDestination = agent.holdPositionLocation;
                            }

                            agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                        }
                        else
                        {
                            // Use ForceSetNavigationState to ensure transition even from ManualCommand
                            agent.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                        }
                    }
                    else if (hasExplicitAttackOrder)
                    {
                        // Agent had an explicit attack order but target is gone/dead.
                        // Use centralized formation recovery — clears all combat/formation flags
                        // consistently and ensures the agent gets a clean Formation state.
                        // Only call if not already in Formation to avoid repeated path resets.
                        if (agent.navigationState != AgentNavigationState.Formation)
                        {
                            agent.ReturnToFormation();
                        }
                        else
                        {
                            // Already in Formation (e.g., ClearAgentAttackTarget got here first).
                            // Just clear the stale intent so this branch doesn't fire again.
                            agent.ClearCommandIntent();
                        }
                    }
                    else if (shouldRemainManualControlled)
                    {
                        // If an RTS/manual-controlled agent loses its target, keep it out of formation redistribution.
                        if (agent.combatStance == CombatStance.HoldPosition)
                        {
                            if (!agent.hasHoldPositionSet)
                            {
                                agent.holdPositionLocation = agent.WorldPosition;
                                agent.hasHoldPositionSet = true;
                            }

                            if (!agent.hasManualCommand)
                            {
                                agent.manualCommandDestination = agent.holdPositionLocation;
                            }
                        }

                        agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                    }
                    else
                    {
                        // No explicit attack order, no manual control — ensure agent is in Formation.
                        // Use SetNavigationState (not ReturnToFormation) because this branch runs
                        // every frame for Idle agents. SetNavigationState is a no-op when already
                        // in Formation, preventing repeated path resets that cause back-and-forth walking.
                        agent.SetNavigationState(AgentNavigationState.Formation);
                    }
                    break;
                case CrowdCombatState.Dead:
                    agent.SetNavigationState(AgentNavigationState.Formation);
                    break;
            }
        }

        private static bool CanControlNavigationAgent(INavigationAgent navAgent)
        {
            if (navAgent == null || !navAgent.Enabled)
                return false;

            if (navAgent.Capabilities.RequiresSurfaceValidationForControl)
                return navAgent.IsOnNavMesh;

            return true;
        }

        private static void ResumeNavigationAgent(CrowdAgent agent)
        {
            var navAgent = agent?.navigationAgent;
            if (!CanControlNavigationAgent(navAgent))
                return;

            if (navAgent.IsStopped)
                navAgent.IsStopped = false;
        }

        private float GetCombatNavUpdateInterval(CrowdAgent agent)
        {
            float interval = agent != null && agent.enemyCrowd != null
                ? agent.enemyCrowd.navTargetUpdateInterval
                : _settings.navTargetUpdateInterval;

            // Guard for A* pathfinding: avoid spamming path requests every frame.
            if (interval <= 0f)
                interval = 0.1f;

            return interval;
        }

        private bool TrySetCombatDestination(CrowdAgent agent, Vector3 targetPosition)
        {
            if (agent?.navigationAgent == null)
                return false;

            var navAgent = agent.navigationAgent;
            if (!navAgent.Capabilities.ShouldThrottleFrequentDestinationUpdates)
                return navAgent.SetDestination(targetPosition);

            float interval = GetCombatNavUpdateInterval(agent);
            float now = Time.time;

            float distSq = (targetPosition - agent.lastCombatDestination).sqrMagnitude;
            bool movedSignificantly = distSq > 0.25f; // 0.5m threshold

            if (!movedSignificantly && now - agent.lastCombatDestinationUpdateTime < interval)
                return false;

            agent.lastCombatDestination = targetPosition;
            agent.lastCombatDestinationUpdateTime = now;

            return navAgent.SetDestination(targetPosition);
        }

        private static void StopNavigationAgent(CrowdAgent agent, bool disableAgent)
        {
            var navAgent = agent?.navigationAgent;
            if (navAgent == null)
                return;

            if (CanControlNavigationAgent(navAgent))
            {
                navAgent.IsStopped = true;
            }

            navAgent.Velocity = Vector3.zero;

            if (disableAgent && navAgent.Enabled)
                navAgent.Enabled = false;
        }

        private static bool TrySamplePosition(CrowdAgent agent, Vector3 position, float maxDistance, out Vector3 result)
        {
            result = position;
            if (agent?.navigationAgent != null)
                return agent.navigationAgent.SamplePosition(position, out result, maxDistance);

            return EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(position, maxDistance, out result);
        }

        private int GetNextAttackClipIndex(CrowdAgent agent)
        {
            var combatAnims = GetCombatAnimationsForAgent(agent);
            AnimationClip[] clips = combatAnims.attacks;

            if (clips == null || clips.Length == 0)
                return -1;

            if (clips.Length == 1)
                return clips[0] != null ? 0 : -1;

            int attempts = 0;
            int index = Random.Range(0, clips.Length);

            while (clips[index] == null && attempts < clips.Length)
            {
                index = Random.Range(0, clips.Length);
                attempts++;
            }

            if (clips[index] == null)
                return -1;

            if (index == agent.lastAttackClipIndex && clips.Length > 1)
            {
                index = (index + 1) % clips.Length;
                if (clips[index] == null)
                    return -1;
            }

            return index;
        }

        private (AnimationClip clip, int index) ResolveAttackClip(CrowdAgent agent)
        {
            EnemyCrowd faction = GetEnemyCrowd(agent);

            // Try directional animations first if enabled and target position is known
            if (agent != null && agent.hasAttackTargetPosition)
            {
                // Get fallback clips
                var combatAnims = GetCombatAnimationsForAgent(agent);
                AnimationClip[] fallbackClips = combatAnims.attacks;

                var result = DirectionalAnimationHelper.GetDirectionalClip(
                    DirectionalAnimationType.Attack,
                    _animationSettings,
                    faction,
                    agent,
                    agent.attackTargetPosition,
                    fallbackClips,
                    agent.lastAttackClipIndex
                );

                if (result.clip != null)
                    return result;
            }

            // Fallback to non-directional attack clip selection
            int clipIndex = GetNextAttackClipIndex(agent);
            if (clipIndex < 0)
                return (null, -1);

            var anims = GetCombatAnimationsForAgent(agent);
            return (anims.attacks[clipIndex], clipIndex);
        }

        private float GetRandomizedAttackCooldown()
        {
            float baseCooldown = Mathf.Max(0.1f, _settings.attackCooldown);
            float jitter = Random.Range(_settings.attackCooldownJitter.x, _settings.attackCooldownJitter.y);
            return baseCooldown + jitter;
        }
        
        /// <summary>
        /// Gets attack cooldown with chaotic combat variance for Battle Line mode.
        /// Each agent gets unique timing based on their index for consistent chaos.
        /// Also applies status effect attack speed modifiers.
        /// </summary>
        private float GetRandomizedAttackCooldown(CrowdAgent agent)
        {
            float baseCooldown = GetRandomizedAttackCooldown();
            
            // Check if agent's faction uses Battle Line with chaotic combat
            EnemyCrowd faction = agent?.enemyCrowd;
            if (faction != null && faction.positioningMode == CombatPositioningMode.BattleLine && faction.battleLineChaoticCombat)
            {
                // Apply chaotic combat variance
                float cooldownVariance = faction.battleLineAttackCooldownVariance;
                float timingVariance = faction.battleLineAttackTimingVariance;
                
                // Agent-specific variance based on their index (deterministic chaos)
                // This ensures each agent has their own rhythm
                float agentSeed = (agent.index * 0.618033988749895f) % 1f; // Golden ratio for good distribution
                float agentCooldownMod = 1f + (agentSeed * 2f - 1f) * cooldownVariance; // Range: [1-variance, 1+variance]
                
                // Additional random timing variance for each attack
                float randomTiming = 1f + (Random.value * 2f - 1f) * timingVariance;
                
                // Combine: base cooldown * agent's personal rhythm * random timing this attack
                baseCooldown = baseCooldown * agentCooldownMod * randomTiming;
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // STATUS EFFECT ATTACK SPEED MODIFIER
            // ═══════════════════════════════════════════════════════════════════
            // cachedAttackSpeedMultiplier > 1 = faster attacks = shorter cooldown
            // cachedAttackSpeedMultiplier < 1 = slower attacks = longer cooldown
            // Cooldown is divided by attack speed multiplier
            if (agent != null && Mathf.Abs(agent.cachedAttackSpeedMultiplier - 1f) > 0.001f)
            {
                baseCooldown /= Mathf.Max(0.1f, agent.cachedAttackSpeedMultiplier);
            }
            
            return Mathf.Max(0.1f, baseCooldown);
        }

        #endregion

        #region Damage & Hit System

        /// <summary>
        /// Applies damage to a specific agent by index.
        /// </summary>
        public bool TryApplyDamage(int agentIndex, float damageAmount, bool allowBlock = true, bool forceKill = false, 
            Vector3? attackerForward = null, Vector3? attackerUp = null, Vector3? attackerPosition = null, int attackerAgentIndex = -1)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return false;

            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null)
                return false;

            return ApplyDamageToAgent(agent, damageAmount, allowBlock, forceKill, attackerForward, attackerUp, attackerPosition, attackerAgentIndex);
        }

        /// <summary>
        /// Applies damage to a specific agent by index with an explicit hit point.
        /// This enables proper Virtual Hitbox zone detection for damage multipliers.
        /// </summary>
        /// <param name="agentIndex">Index of the agent to damage</param>
        /// <param name="damageAmount">Base damage amount (will be modified by zone multipliers)</param>
        /// <param name="hitPoint">World position where the hit occurred - used for zone detection (head/torso/legs)</param>
        /// <param name="allowBlock">Whether the agent can block this damage</param>
        /// <param name="attackerForward">Optional attacker facing direction for directional reactions</param>
        /// <returns>True if the agent was killed, false otherwise</returns>
        public bool TryApplyDamageAtPoint(int agentIndex, float damageAmount, Vector3 hitPoint, bool allowBlock = true,
            Vector3? attackerForward = null, bool forceKill = false)
        {
            if (!NetworkAuthorityProvider.IsServerAuthority)
                return false;

            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return false;

            CrowdAgent agent = _agents[agentIndex];
            if (agent == null)
                return false;

            return ApplyDamageToAgent(agent, damageAmount, allowBlock, forceKill: forceKill, 
                attackerForward: attackerForward, attackerUp: null, attackerPosition: hitPoint, 
                attackerAgentIndex: -1, hitPoint: hitPoint);
        }

        /// <summary>
        /// Applies damage to an agent via GPUICrowdInstance.
        /// </summary>
        public bool TryApplyDamage(GPUICrowdInstance instance, float damageAmount, bool allowBlock = true, bool forceKill = false,
            Vector3? attackerForward = null, Vector3? attackerUp = null, Vector3? attackerPosition = null,
            System.Func<GPUICrowdInstance, int> instanceLookupCallback = null, int attackerAgentIndex = -1)
        {
            if (instance == null || instanceLookupCallback == null)
                return false;

            int agentIndex = instanceLookupCallback(instance);
            if (agentIndex < 0)
                return false;

            return TryApplyDamage(agentIndex, damageAmount, allowBlock, forceKill, attackerForward, attackerUp, attackerPosition, attackerAgentIndex);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Network-Aware Damage Methods (Request/Validate/Apply Pattern)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Network sequence counter for damage events.
        /// </summary>
        private int _networkDamageSequence;

        /// <summary>
        /// Requests damage to be applied through the network authority system.
        /// In single-player or on the server, damage is applied immediately.
        /// On clients in multiplayer, this sends a request to the server.
        /// 
        /// Use this method for player-initiated damage (abilities, manual attacks).
        /// AI combat uses the standard methods for performance.
        /// </summary>
        /// <param name="targetIndex">Local index of the target agent</param>
        /// <param name="damage">Base damage amount</param>
        /// <param name="attackerIndex">Local index of the attacker (-1 for non-agent damage)</param>
        /// <param name="hitPoint">World position of the hit</param>
        /// <param name="damageType">Type of damage for resistance calculation and special handling</param>
        /// <returns>True if damage was applied (or predicted), false if rejected</returns>
        /// <remarks>
        /// SERVER-AUTHORITATIVE RESISTANCE:
        /// In multiplayer, the 'damage' parameter is RAW damage (before resistance).
        /// The server must call ApplyResistanceToRawDamage() to calculate final damage.
        /// This prevents clients from bypassing resistance calculations.
        /// </remarks>
        public bool RequestDamage(int targetIndex, float damage, int attackerIndex = -1, 
            Vector3? hitPoint = null, Networking.DamageType damageType = Networking.DamageType.Normal)
        {
            bool debugDamageLogging = _settings.enableDebugDamageLogging;
            
            if (targetIndex < 0 || targetIndex >= _agents.Count)
            {
                if (debugDamageLogging)
                {
                    Debug.LogWarning($"[REQUEST DEBUG] Invalid targetIndex! {targetIndex} (agents count: {_agents.Count})");
                }
                return false;
            }

            CrowdAgent target = _agents[targetIndex];
            if (target == null || target.isDead)
                return false;

            debugDamageLogging |= target.enemyCrowd?.enableDebugDamageLogging == true;
            if (debugDamageLogging)
            {
                Debug.Log($"[REQUEST DEBUG] RequestDamage called: targetIdx={targetIndex}, dmg={damage:F1}, attackerIdx={attackerIndex}, type={damageType}");
            }

            // Get attacker info
            CrowdAgent attacker = null;
            if (attackerIndex >= 0 && attackerIndex < _agents.Count)
                attacker = _agents[attackerIndex];

            // Check if we're in network mode
            var damageAuthority = NetworkAuthorityProvider.DamageAuthority;
            
            if (damageAuthority == null)
            {
                // Single-player: Apply resistance locally then apply damage
                float finalDamage = ApplyResistanceToRawDamage(target, damage, damageType);
                if (debugDamageLogging)
                {
                    Debug.Log($"[REQUEST DEBUG] Single-player mode: rawDmg={damage:F1} -> finalDmg={finalDamage:F1} (resist applied)");
                }
                bool result = TryApplyDamage(targetIndex, finalDamage, true, false, null, null, hitPoint, attackerIndex);
                if (debugDamageLogging)
                {
                    Debug.Log($"[REQUEST DEBUG] TryApplyDamage returned: {result}, target HP: {target.health}/{target.maxHealth}");
                }
                return result;
            }

            // Multiplayer: Send RAW damage to server - server will apply resistance
            int sequence = ++_networkDamageSequence;
            
            bool shouldPredict = damageAuthority.OnDamageAttempt(
                target.networkId,
                damage, // RAW damage - server calculates resistance!
                attacker?.networkId ?? 0,
                hitPoint ?? GetAgentPosition(target),
                damageType,
                out int assignedSequence);

            if (shouldPredict)
            {
                // Client prediction: Apply resistance locally for immediate feedback
                // Server will reconcile if different
                float predictedDamage = ApplyResistanceToRawDamage(target, damage, damageType);
                return TryApplyDamage(targetIndex, predictedDamage, true, false, null, null, hitPoint, attackerIndex);
            }

            // Waiting for server confirmation
            return false;
        }
        
        /// <summary>
        /// Applies resistance to raw damage based on damage type.
        /// Call this on the SERVER before applying damage authoritatively.
        /// </summary>
        /// <param name="target">Target agent receiving damage</param>
        /// <param name="rawDamage">Damage before resistance</param>
        /// <param name="networkDamageType">Network damage type (includes elemental types)</param>
        /// <returns>Final damage after resistance applied</returns>
        public static float ApplyResistanceToRawDamage(CrowdAgent target, float rawDamage, Networking.DamageType networkDamageType)
        {
            if (target == null) return rawDamage;
            
            // Map network DamageType to skill DamageType for resistance lookup
            var skillDamageType = networkDamageType switch
            {
                Networking.DamageType.Physical or Networking.DamageType.Melee => Skills.DamageType.Physical,
                Networking.DamageType.Magical => Skills.DamageType.Magical,
                Networking.DamageType.Fire => Skills.DamageType.Fire,
                Networking.DamageType.Ice => Skills.DamageType.Ice,
                Networking.DamageType.Lightning => Skills.DamageType.Lightning,
                Networking.DamageType.Poison or Networking.DamageType.DOT => Skills.DamageType.Poison,
                Networking.DamageType.True => Skills.DamageType.True,
                _ => Skills.DamageType.Physical // Default to physical for Normal, Ranged, AOE, Environment
            };
            
            return target.ApplyResistance(rawDamage, skillDamageType);
        }

        /// <summary>
        /// Applies damage received from the server (authoritative).
        /// Bypasses client-side prediction checks.
        /// Only call this from network sync code.
        /// </summary>
        /// <param name="targetNetworkId">Network ID of the target</param>
        /// <param name="damage">Damage amount (already validated by server)</param>
        /// <param name="newHealth">Authoritative health after damage</param>
        /// <param name="isDead">Whether the agent died</param>
        /// <param name="attackerNetworkId">Network ID of attacker (0 if none)</param>
        /// <param name="hitPoint">Hit position</param>
        public void ApplyServerDamage(uint targetNetworkId, float damage, float newHealth, bool isDead,
            uint attackerNetworkId = 0, Vector3? hitPoint = null)
        {
            int targetIndex = GetAgentIndexByNetworkId(targetNetworkId);
            if (targetIndex < 0)
                return;

            CrowdAgent target = _agents[targetIndex];
            if (target == null)
                return;

            int attackerIndex = attackerNetworkId != 0 ? GetAgentIndexByNetworkId(attackerNetworkId) : -1;

            // Set health directly (authoritative)
            float previousHealth = target.health;
            target.SetHealthAuthoritative(newHealth);
            
            // Notify health change
            NotifyAgentHealthChanged(target, previousHealth);

            // Handle death if needed
            if (isDead && !target.isDead)
            {
                target.SetDeadAuthoritative(true);
                TriggerDeath(target, Time.time);
            }
            else if (!isDead && previousHealth > newHealth)
            {
                // Trigger hit reaction
                if (ShouldTriggerHitReactionOnDamage(target))
                {
                    TriggerHit(target, Time.time);
                }
            }
        }

        /// <summary>
        /// Rolls back a predicted damage event.
        /// Called when server rejects client's damage prediction.
        /// </summary>
        /// <param name="targetNetworkId">Network ID of the target</param>
        /// <param name="correctHealth">Server's authoritative health value</param>
        public void RollbackDamage(uint targetNetworkId, float correctHealth)
        {
            int targetIndex = GetAgentIndexByNetworkId(targetNetworkId);
            if (targetIndex < 0)
                return;

            CrowdAgent target = _agents[targetIndex];
            if (target == null)
                return;

            // Restore correct health
            target.SetHealthAuthoritative(correctHealth);
            
            // If we incorrectly predicted death, undo it
            if (target.isDead && correctHealth > 0)
            {
                target.SetDeadAuthoritative(false);
                // Note: Undoing death visuals is complex - may need animation state reset
            }
        }

        /// <summary>
        /// Gets the agent index for a network ID.
        /// </summary>
        private int GetAgentIndexByNetworkId(uint networkId)
        {
            if (networkId == 0)
                return -1;

            // Linear search through agents
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i]?.networkId == networkId)
                    return i;
            }
            return -1;
        }

        // ═══════════════════════════════════════════════════════════════════

        private bool ApplyDamageToAgent(CrowdAgent agent, float damageAmount, bool allowBlock, bool forceKill, 
            Vector3? attackerForward = null, Vector3? attackerUp = null, Vector3? attackerPosition = null, 
            int attackerAgentIndex = -1, Vector3? hitPoint = null, bool isMeleeAttack = true, bool isChargeAttack = false)
        {
            if (agent == null || agent.isDead)
                return false;
            
            // ═══════════════════════════════════════════════════════════════════
            // STATUS EFFECT CHECKS
            // ═══════════════════════════════════════════════════════════════════
            
            // Invulnerability check
            if (agent.isInvulnerable && !forceKill)
            {
                // TODO: Spawn invulnerable VFX feedback
                return false;
            }

            float now = Time.time;
            
            // === Virtual Hitbox System ===
            // Skip when collider-based hit detection is selected globally.
            float hitboxMultiplier = 1f;
            HitboxZone hitZone = null;
            EnemyCrowd targetFaction = GetEnemyCrowd(agent);

            if (_settings.hitDetectionMode == HitDetectionMode.Distance && targetFaction != null && targetFaction.virtualHitbox.enabled)
            {
                Vector3 agentPosition = GetAgentPosition(agent);
                
                // Determine hit zone based on attack source:
                // - Explicit hitPoint provided (player attacks): Use position-based zone detection
                // - No hitPoint (AI attacks): Use random zone selection based on hit chances (performant)
                if (hitPoint.HasValue)
                {
                    // Player/explicit targeting: Check actual hit position against zones
                    hitboxMultiplier = targetFaction.virtualHitbox.GetDamageMultiplier(hitPoint.Value, agentPosition, out hitZone);
                }
                else
                {
                    // AI vs AI combat: Random zone selection with per-agent modifiers
                    // Get attacker's critical chance modifier (buff from auras/spells)
                    float attackerCritMod = 1f;
                    if (attackerAgentIndex >= 0 && attackerAgentIndex < _agents.Count)
                    {
                        var attacker = _agents[attackerAgentIndex];
                        if (attacker != null)
                            attackerCritMod = attacker.criticalHitChanceModifier;
                    }
                    
                    // Apply target's received critical chance modifier (debuff - makes target easier to crit)
                    float combinedCritMod = attackerCritMod * agent.receivedCriticalChanceModifier;
                    
                    // Use modified random selection if any modifiers are active
                    if (Mathf.Abs(combinedCritMod - 1f) > 0.001f)
                    {
                        hitboxMultiplier = targetFaction.virtualHitbox.GetRandomDamageMultiplierWithModifiers(combinedCritMod, out hitZone);
                    }
                    else
                    {
                        // No modifiers - use cached version for performance
                        hitboxMultiplier = targetFaction.virtualHitbox.GetRandomDamageMultiplier(out hitZone);
                    }
                }
                
                // Spawn critical hit effects if zone is critical
                if (hitZone != null && hitZone.isCriticalZone)
                {
                    // For critical VFX, estimate hit position at zone height
                    Vector3 criticalHitPos = hitPoint ?? (agentPosition + Vector3.up * ((hitZone.minHeight + hitZone.maxHeight) * 0.5f));
                    SpawnCriticalHitEffects(agent, targetFaction.virtualHitbox, criticalHitPos);
                }
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // STATUS EFFECT DAMAGE MODIFIERS
            // ═══════════════════════════════════════════════════════════════════
            
            float finalDamage = damageAmount * hitboxMultiplier;
            
            // Apply attacker's damage buff/debuff
            if (attackerAgentIndex >= 0 && attackerAgentIndex < _agents.Count)
            {
                var attacker = _agents[attackerAgentIndex];
                if (attacker != null)
                {
                    finalDamage *= attacker.cachedDamageMultiplier;
                }
            }
            
            // Apply defender's damage received multiplier (armor buff/debuff)
            finalDamage *= agent.cachedDamageReceivedMultiplier;
            
            // Shield absorption
            if (agent.cachedShieldAmount > 0 && _statusEffectSystem != null)
            {
                finalDamage = _statusEffectSystem.ApplyShieldAbsorption(agent, finalDamage);
                if (finalDamage <= 0 && !forceKill)
                {
                    // All damage absorbed by shield
                    return false;
                }
            }

            // Debug trace (developer-only): honor global or faction toggle
            bool debugDamageLogging = _settings.enableDebugDamageLogging || (targetFaction != null && targetFaction.enableDebugDamageLogging);
            if (debugDamageLogging)
            {
                // Debug log removed for production build
            }

            // Track attacker for combat stance retaliation (Defensive stance)
            if (attackerAgentIndex >= 0)
            {
                agent.lastDamagedByAgentIndex = attackerAgentIndex;
                agent.lastDamagedByEnemyTime = now;
            }

            // Block check with status effect block bonus
            float effectiveBlockChance = _settings.blockChance + agent.cachedBlockChanceBonus;
            if (!forceKill && allowBlock && HasBlockAnimationClip() && effectiveBlockChance > 0f && Random.value <= effectiveBlockChance)
            {
                TriggerBlock(agent, now);
                return false;
            }

            float previousHealth = agent.health;

            // Apply final damage
            if (!forceKill)
                agent.health -= Mathf.Max(0f, finalDamage);
            else
                agent.health = 0f;
            
            if (debugDamageLogging)
            {
                Debug.Log($"[APPLY DMG] Agent {agent.index}: prevHP={previousHealth:F1}, finalDmg={finalDamage:F1}, newHP={agent.health:F1}");
            }
            
            NotifyAgentHealthChanged(agent, previousHealth);
            
            // Fire OnAgentDamaged for minimap alerts / UI indicators
            OnAgentDamaged?.Invoke(agent, finalDamage, attackerAgentIndex);
            
            // Notify status effect system of damage (breaks Sleep, etc.)
            _statusEffectSystem?.OnAgentTookDamage(agent);
            
            // Interrupt skill cast if CastInterruptedByDamage is set
            if (agent.isCasting && agent.castingSkill != null && agent.castingSkill.CastInterruptedByDamage)
            {
                SkillExecutor.InterruptCast(agent);
            }

            // Store attacker orientation for ragdoll impulse and directional animations
            if (attackerForward.HasValue && attackerForward.Value.sqrMagnitude > 0.0001f)
            {
                agent.lastAttackerForward = attackerForward.Value.normalized;
                agent.lastAttackerUp = attackerUp.HasValue && attackerUp.Value.sqrMagnitude > 0.0001f
                    ? attackerUp.Value.normalized
                    : Vector3.up;
                agent.hasAttackerOrientation = true;
            }

            // Store attacker position for directional animations
            if (attackerPosition.HasValue)
            {
                agent.attackTargetPosition = attackerPosition.Value;
                agent.hasAttackTargetPosition = true;
            }

            agent.lastDamageTime = now;
            agent.nextAttackTime = now + GetRandomizedAttackCooldown(agent);
            
            // Notify positioning system that this agent was attacked (for OrganizedSwarm reactive defense)
            _positioningSystem.NotifyAgentAttacked(agent);
            
            // Alert nearby allies (critical for melee units facing ranged attackers)
            // Get attacker position: use provided position, or calculate from attacker agent
            Vector3? alertPosition = attackerPosition;
            if (!alertPosition.HasValue && attackerAgentIndex >= 0 && attackerAgentIndex < _agents.Count)
            {
                var attacker = _agents[attackerAgentIndex];
                if (attacker != null)
                {
                    alertPosition = GetAgentPosition(attacker);
                }
            }
            AlertNearbyAllies(agent, attackerAgentIndex, alertPosition);
            
            // ═══════════════════════════════════════════════════════════════════
            // ATTACK STATUS EFFECTS - Apply effects from attacker's attack type
            // ═══════════════════════════════════════════════════════════════════
            bool isCriticalHit = hitZone != null && hitZone.isCriticalZone;
            ApplyAttackStatusEffects(agent, attackerAgentIndex, isMeleeAttack, isChargeAttack, isCriticalHit);

            // Death or hit
            if (debugDamageLogging)
            {
                Debug.Log($"[DEATH CHECK] Agent {agent.index}: health={agent.health:F1}, isDead={agent.isDead}, checking death...");
            }
            if (agent.health <= 0f)
            {
                if (debugDamageLogging)
                {
                    Debug.Log($"[DEATH CHECK] Agent {agent.index}: TRIGGERING DEATH!");
                }
                TriggerDeath(agent, now);
                return true;
            }

            if (debugDamageLogging)
            {
                Debug.Log($"[DEATH CHECK] Agent {agent.index}: Health > 0, triggering hit reaction instead");
            }
            if (ShouldTriggerHitReactionOnDamage(agent))
            {
                TriggerHit(agent, now);
            }
            return true;
        }
        
        /// <summary>
        /// Spawns visual and audio effects for critical hits (headshots, etc.)
        /// </summary>
        private void SpawnCriticalHitEffects(CrowdAgent agent, VirtualHitboxConfig hitboxConfig, Vector3 hitPoint)
        {
            if (hitboxConfig == null)
                return;
            
            // Spawn critical hit VFX
            if (hitboxConfig.criticalHitVFXPrefab != null)
            {
                GameObject vfx = Object.Instantiate(hitboxConfig.criticalHitVFXPrefab, hitPoint, Quaternion.identity);
                
                float duration = hitboxConfig.criticalHitVFXDuration;
                if (duration <= 0f)
                {
                    // Try to get duration from particle system
                    ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
                    if (ps != null)
                    {
                        duration = ps.main.duration + ps.main.startLifetime.constantMax;
                    }
                    else
                    {
                        duration = 2f; // Default fallback
                    }
                }
                
                Object.Destroy(vfx, duration);
            }
            
            // Play critical hit sound
            if (hitboxConfig.criticalHitSound != null)
            {
                AudioSource.PlayClipAtPoint(hitboxConfig.criticalHitSound, hitPoint, hitboxConfig.criticalHitSoundVolume);
            }
        }

        private static bool HasCCImmunity(CrowdAgent agent)
        {
            if (agent == null) return false;

            // Use both the cached flag and direct effect lookup so immunity works immediately
            // even before the next cached-modifier recalculation pass.
            return agent.isCCImmune || agent.HasEffect(StatusEffectType.CCImmune);
        }

        private bool ShouldTriggerHitReactionOnDamage(CrowdAgent agent)
        {
            return agent != null &&
                   GetTriggerHitReactionOnDamage(agent) &&
                   !HasCCImmunity(agent);
        }

        private void TriggerHit(CrowdAgent agent, float currentTime)
        {
            AnimationClip clip = null;
            float duration = 0.4f;
            int clipIndex = -1;

            // Get faction configuration
            EnemyCrowd faction = GetEnemyCrowd(agent);

            // Try directional animations first if enabled and attacker position is known
            if (agent.hasAttackTargetPosition)
            {
                var combatAnims = GetCombatAnimationsForAgent(agent);
                var result = DirectionalAnimationHelper.GetDirectionalClip(
                    DirectionalAnimationType.Hit,
                    _animationSettings,
                    faction,
                    agent,
                    agent.attackTargetPosition,
                    combatAnims.hits,
                    agent.lastHitClipIndex
                );

                if (result.clip != null)
                {
                    clip = result.clip;
                    clipIndex = result.index;
                    duration = Mathf.Max(clip.length, 0.35f);
                }
            }

            // Fallback to non-directional clips if directional failed
            if (clip == null)
            {
                var combatAnims = GetCombatAnimationsForAgent(agent);
                AnimationClip[] clips = combatAnims.hits;

                if (clips != null && clips.Length > 0)
                {
                    clipIndex = Random.Range(0, clips.Length);
                    clip = clips[clipIndex];
                    if (_hitClipLengths.Length > clipIndex)
                        duration = _hitClipLengths[clipIndex];
                    else if (clip != null)
                        duration = Mathf.Max(clip.length, 0.35f);
                }
            }

            // Store the clip index to avoid repeats
            agent.lastHitClipIndex = clipIndex;

            EnterCombatState(agent, CrowdCombatState.Hit, duration, currentTime);
            
            // Play the hit animation via GPU Instancer
            if (clip != null)
            {
                PlayCombatClip(agent, clip, 0.05f, false, CombatAnimationType.Hit);
            }
        }

        private void TriggerBlock(CrowdAgent agent, float currentTime)
        {
            // Face the attacker immediately when blocking (use last attacker direction if available)
            if (agent.hasAttackTargetPosition)
            {
                FaceTargetImmediate(agent, agent.attackTargetPosition);
            }
            else if (agent.attackTargetTransform != null)
            {
                FaceTargetImmediate(agent, agent.attackTargetTransform.position);
            }
            else if (agent.hasAttackerOrientation)
            {
                // Face the direction the attack came from
                Transform agentTransform = agent.navigationAgent?.Transform ??
                                          (agent.crowdInstance != null ? agent.crowdInstance.transform : null);
                if (agentTransform != null)
                {
                    // Attacker was facing us, so we face back towards them
                    Vector3 attackerDirection = -agent.lastAttackerForward;
                    Vector3 targetPos = agentTransform.position + attackerDirection * 2f;
                    FaceTargetImmediate(agent, targetPos);
                }
            }
            
            AnimationClip blockAnimation = ResolveBlockClip(agent);
            float holdDuration = GetRandomizedBlockHoldDuration();
            float clipDuration = blockAnimation != null ? Mathf.Max(blockAnimation.length, 0f) : 0f;
            float duration = Mathf.Max(_blockClipLength, holdDuration, clipDuration);
            
            EnterCombatState(agent, CrowdCombatState.Blocking, duration, currentTime);
            
            // Play the block animation via GPU Instancer
            if (blockAnimation != null)
            {
                PlayCombatClip(agent, blockAnimation, 0.1f, false, CombatAnimationType.Block);
            }
            
            float nextAttack = currentTime + GetRandomizedAttackCooldown(agent);
            agent.nextAttackTime = Mathf.Max(nextAttack, currentTime + holdDuration);
        }

        private void TriggerDeath(CrowdAgent agent, float currentTime)
        {
            // Clear all status effects on death
            _statusEffectSystem?.ClearAllEffects(agent);
            
            AnimationClip deathAnimation = ResolveDeathClip(agent);
            
            float actualDeathClipLength = deathAnimation != null ? deathAnimation.length : _deathClipLength;
            float transitionTime = 0.08f;
            float totalAnimationTime = actualDeathClipLength + transitionTime;

            EnterCombatState(agent, CrowdCombatState.Dead, totalAnimationTime, currentTime);
            agent.deathAnimationCompleted = false;
            
            // Check if ragdoll should be used for this agent (per-faction setting)
            bool useRagdoll = _ragdollSystem != null && _ragdollSystem.ShouldUseRagdollForAgent(agent);
            
            // CRITICAL: Force Bone Tracker workflow FIRST if ragdoll is enabled
            // This needs to happen before disabling components
            if (useRagdoll)
            {
                _forceBoneTrackerCallback?.Invoke(agent);
            }

            // Play the death animation via GPU Instancer - use retry logic for critical death animation
            if (deathAnimation != null)
            {
                bool success = PlayCombatClipWithRetry(agent, deathAnimation, transitionTime, false, CombatAnimationType.Death, isCritical: true);
                if (!success)
                {
                    // If animation failed, force Mecanim speed to 0 to prevent locomotion showing
                    if (agent.mecanimAnimator != null)
                    {
                        agent.mecanimAnimator.speed = 0f;
                    }
                }
            }

            // Freeze locomotion immediately to prevent interruptions
            if (agent.mecanimAnimator != null)
                agent.mecanimAnimator.SetFloat("Speed", 0f);

            agent.nextAttackTime = float.PositiveInfinity;
            agent.deathTime = currentTime;
            agent.pendingCleanup = false;
            
            EnemyCrowd faction = GetEnemyCrowd(agent);

            // Spawn death-time VFX/audio
            PlayDeathEffects(agent, faction);
            
            // Initialize death sinking effect if enabled (per-faction setting) - but NOT if using ragdoll immediately
            // For ragdoll agents, sinking will be scheduled AFTER ragdoll has settled
            bool enableSinking = faction != null ? faction.enableDeathSinking : _settings.enableDeathSinking;
            
            if (!useRagdoll && enableSinking)
            {
                // Non-ragdoll: start sinking immediately after cleanup delay
                InitializeDeathSinking(agent, currentTime, faction);
                // Add to dedicated sinking list for efficient per-frame updates
                if (!_sinkingAgents.Contains(agent))
                    _sinkingAgents.Add(agent);
            }
            else if (!enableSinking)
            {
                // If sinking is disabled, we can stop A* navigation immediately on death.
                DisableAStarNavigationAfterDeath(agent);
            }
            
            // Setup ragdoll activation with delay
            if (useRagdoll)
            {
                Debug.Log($"[Death] Agent {agent.index}: Setting up ragdoll, hasAttackerOrientation: {agent.hasAttackerOrientation}, attackerForward: {agent.lastAttackerForward}");
                
                // Prepare ragdoll impulse based on attacker orientation
                _ragdollSystem.PrepareRagdollImpulse(agent, 
                    agent.hasAttackerOrientation ? (Vector3?)agent.lastAttackerForward : null,
                    agent.hasAttackerOrientation ? (Vector3?)agent.lastAttackerUp : null);
                
                // Debug log removed for production build
                
                // Always use a small delay to ensure workflow switch completes
                // Even if user set delay to 0, we need at least 0.1s for workflow to apply
                float ragdollDelay = _settings.ragdollActivationDelay;
                float actualDelay = Mathf.Max(0.1f, ragdollDelay);
                
                agent.ragdollPending = true;
                agent.ragdollActivationTime = currentTime + actualDelay;
                
                // Schedule ragdoll sinking if enabled for this faction
                // This will trigger AFTER the ragdoll has settled (cleanup delay after ragdoll activation)
                if (enableSinking)
                {
                    float cleanupDelay = faction != null ? faction.deadAgentCleanupDelay : _settings.deadAgentCleanupDelay;
                    agent.ragdollSinkPending = true;
                    agent.ragdollSinkScheduleTime = currentTime + actualDelay + cleanupDelay;
                    Debug.Log($"[Death] Agent {agent.index}: Ragdoll sink scheduled for {agent.ragdollSinkScheduleTime} (ragdoll delay: {actualDelay}s + cleanup delay: {cleanupDelay}s)");
                }
                
                Debug.Log($"[Death] Agent {agent.index}: Ragdoll pending, will activate at {agent.ragdollActivationTime} (delay: {actualDelay}s)");
            }

            OnAgentKilled?.Invoke(agent.crowdInstance);
        }

        #endregion

        #region Ragdoll Activation
        
        /// <summary>
        /// Attempts to activate ragdoll physics for an agent.
        /// Called when ragdoll activation delay has elapsed.
        /// </summary>
        private void TryActivateAgentRagdoll(CrowdAgent agent)
        {
            if (agent == null)
            {
                Debug.LogWarning("[Combat] TryActivateAgentRagdoll called with null agent");
                return;
            }
            
            if (_ragdollSystem == null)
            {
                Debug.LogWarning($"[Combat] Agent {agent.index}: No ragdoll system available");
                return;
            }
            
            Debug.Log($"[Combat] Agent {agent.index}: Calling ragdoll system TryActivateRagdoll");
            
            // Use the ragdoll system's activation logic with the bone tracker callback
            _ragdollSystem.TryActivateRagdoll(agent, _forceBoneTrackerCallback);
        }
        
        #endregion

        #region Helper Methods
        
        /// <summary>
        /// Plays a sound at the specified position using CrowdAudioManager for efficiency.
        /// Supports pitch variation and spatial blend with per-clip concurrency limiting.
        /// </summary>
        private void PlaySoundAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariation = 0f, float spatialBlend = 1f)
        {
            PlaySoundAtPosition(clip, position, volume, pitchVariation, spatialBlend, null);
        }
        
        /// <summary>
        /// Plays a sound at the specified position using CrowdAudioManager for efficiency.
        /// Uses the EnemyCrowd's maxConcurrentSoundsPerClip setting if provided.
        /// </summary>
        private void PlaySoundAtPosition(AudioClip clip, Vector3 position, float volume, float pitchVariation, float spatialBlend, EnemyCrowd faction)
        {
            if (clip == null) return;
            
            // Use CrowdAudioManager for intelligent per-clip pooling
            var audioManager = CrowdAudioManager.Instance;
            if (audioManager != null)
            {
                int maxConcurrent = faction != null ? faction.maxConcurrentSoundsPerClip : -1;
                float minDist = faction != null ? faction.factionAudioMinDistance : 1f;
                float maxDist = faction != null ? faction.factionAudioMaxDistance : 50f;
                
                audioManager.PlaySound(clip, position, volume, pitchVariation, spatialBlend, maxConcurrent, minDist, maxDist);
                return;
            }
            
            // Fallback to legacy pool if CrowdAudioManager not available
            int audioIndex = AcquireAudioSource();
            if (audioIndex < 0)
            {
                // Pool exhausted - skip this sound (better than GC allocation)
                return;
            }
            
            AudioSource audioSource = _audioSourcePool[audioIndex];
            audioSource.transform.position = position;
            audioSource.clip = clip;
            audioSource.volume = volume;
            audioSource.spatialBlend = spatialBlend;
            
            // Apply pitch variation
            float pitch = 1f;
            if (pitchVariation > 0f)
            {
                // Use audio source index to add variation when multiple sounds play same frame
                // This ensures consecutive sounds from the same frame have different pitches
                float indexOffset = (audioIndex * 0.1f) % 1f;
                float baseRandom = UnityEngine.Random.Range(-1f, 1f);
                // XOR-style mixing to break patterns
                float mixed = baseRandom + indexOffset;
                if (mixed > 1f) mixed -= 2f;
                if (mixed < -1f) mixed += 2f;
                pitch = 1f + mixed * pitchVariation;
            }
            audioSource.pitch = pitch;
            
            audioSource.Play();
            
            // Schedule when this audio source can be reused
            float clipDuration = clip.length / Mathf.Max(0.1f, pitch);
            _audioSourceReleaseTime[audioIndex] = Time.time + clipDuration + 0.05f;
        }
        
        /// <summary>
        /// Acquires an audio source from the pool. Returns -1 if pool is exhausted.
        /// Will also reclaim audio sources that have finished playing.
        /// </summary>
        private int AcquireAudioSource()
        {
            // If pool not initialized yet, return -1
            if (_audioSourcePool == null) return -1;
            
            float currentTime = Time.time;
            
            // First, try to get from the free queue
            if (_freeAudioSources.Count > 0)
            {
                return _freeAudioSources.Dequeue();
            }
            
            // No free sources - try to reclaim finished ones
            for (int i = 0; i < _audioSourcePool.Count; i++)
            {
                if (_audioSourceReleaseTime[i] <= currentTime && !_audioSourcePool[i].isPlaying)
                {
                    return i;
                }
            }
            
            // Pool is truly exhausted - expand if we haven't hit a limit
            if (_audioSourcePool.Count < 256) // Hard cap at 256 audio sources
            {
                int newIndex = _audioSourcePool.Count;
                
                GameObject audioObj = new GameObject($"AudioSource_{newIndex}");
                audioObj.transform.SetParent(_audioPoolContainer);
                
                AudioSource source = audioObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = 50f;
                
                _audioSourcePool.Add(source);
                
                // Expand the release time array
                float[] newReleaseTime = new float[_audioSourcePool.Count];
                System.Array.Copy(_audioSourceReleaseTime, newReleaseTime, _audioSourceReleaseTime.Length);
                _audioSourceReleaseTime = newReleaseTime;
                _audioSourceReleaseTime[newIndex] = 0f;
                
                return newIndex;
            }
            
            // Truly exhausted and at capacity
            return -1;
        }

        private void PlayDeathEffects(CrowdAgent agent, EnemyCrowd faction)
        {
            if (agent == null)
                return;

            var config = faction != null ? faction.deathEffectsConfig : _settings.deathEffectsConfig;
            if (config == null)
                return;

            Transform agentTransform = agent.navigationAgent?.Transform ??
                                       (agent.crowdInstance != null ? agent.crowdInstance.transform : null);
            if (agentTransform == null)
                return;

            // VFX
            bool playVfx = config.enabled && config.HasAnyVFX(faction?.deathEffectPrefab);
            if (playVfx)
            {
                var deathVFX = config.GetEffectiveDeathVFX(faction?.deathEffectPrefab);
                if (deathVFX != null && deathVFX.IsValid)
                {
                    Vector3 spawnPos = agentTransform.position + agentTransform.TransformDirection(config.deathVFXOffset);
                    Vector3 forward = agentTransform.forward;
                    float duration = config.deathVFXDuration > 0f ? config.deathVFXDuration : deathVFX.GetDuration();

                    System.Action<ParticleSystem> configure = null;
                    if (config.bindShapeToRenderer && deathVFX.IsParticleSystem && agent.deathVFXRenderer != null)
                    {
                        var cachedRenderer = agent.deathVFXRenderer;
                        configure = ps => ConfigureParticleShapeRenderer(ps, cachedRenderer);
                    }

                    SpawnVFX(deathVFX, spawnPos, forward, duration, configure);
                }
            }

            // Audio
            bool hasDeathSounds = faction != null && faction.deathSounds != null && faction.deathSounds.Length > 0;
            if (config.enabled && hasDeathSounds)
            {
                AudioClip clip = faction?.GetRandomDeathSound();
                if (clip != null)
                {
                    PlaySoundAtPosition(clip,
                        agentTransform.position,
                        config.deathSoundVolume,
                        config.deathSoundPitchVariation,
                        config.deathSoundSpatialBlend,
                        faction);
                }
            }
        }
        
        /// <summary>
        /// Spawns a VFX from the pool at the specified position and rotation.
        /// OPTIMIZED: Caches ParticleSystem components and caps pool size.
        /// </summary>
        private void SpawnPooledVFX(GameObject prefab, Vector3 position, Quaternion rotation, float duration = 0f, System.Action<ParticleSystem> configure = null)
        {
            if (prefab == null || _vfxPools == null) return;
            
            // Get or create pool for this prefab
            if (!_vfxPools.TryGetValue(prefab, out var pool))
            {
                pool = new List<GameObject>(32);
                _vfxPools[prefab] = pool;
                _vfxFreeIndices[prefab] = new Queue<int>();
                _cachedVFXParticleSystems[prefab] = new Dictionary<int, ParticleSystem>();
            }
            
            var freeQueue = _vfxFreeIndices[prefab];
            var cachedPS = _cachedVFXParticleSystems[prefab];
            
            GameObject vfx;
            int poolIndex;
            
            if (freeQueue.Count > 0)
            {
                poolIndex = freeQueue.Dequeue();
                vfx = pool[poolIndex];
            }
            else
            {
                // Cap pool size to prevent memory explosion
                if (pool.Count >= 128)
                {
                    // Pool exhausted - find oldest and reclaim
                    if (_activeVFX.Count > 0)
                    {
                        float oldestTime = float.MaxValue;
                        int oldestIdx = -1;
                        for (int i = 0; i < _activeVFX.Count; i++)
                        {
                            if (_activeVFX[i].prefab == prefab && _activeVFX[i].releaseTime < oldestTime)
                            {
                                oldestTime = _activeVFX[i].releaseTime;
                                oldestIdx = i;
                            }
                        }
                        if (oldestIdx >= 0)
                        {
                            poolIndex = _activeVFX[oldestIdx].poolIndex;
                            vfx = pool[poolIndex];
                            int lastIdx = _activeVFX.Count - 1;
                            if (oldestIdx < lastIdx) _activeVFX[oldestIdx] = _activeVFX[lastIdx];
                            _activeVFX.RemoveAt(lastIdx);
                        }
                        else return;
                    }
                    else return;
                }
                else
                {
                    // Create new instance
                    vfx = Object.Instantiate(prefab, _vfxPoolContainer);
                    pool.Add(vfx);
                    poolIndex = pool.Count - 1;
                }
            }
            
            // Position and activate
            vfx.transform.position = position;
            vfx.transform.rotation = rotation;
            vfx.SetActive(true);
            
            // Get or cache ParticleSystem component
            if (!cachedPS.TryGetValue(poolIndex, out var particleSystem))
            {
                particleSystem = vfx.GetComponent<ParticleSystem>();
                cachedPS[poolIndex] = particleSystem;
            }
            
            float effectDuration = duration;
            if (particleSystem != null)
            {
                configure?.Invoke(particleSystem);
                particleSystem.Clear();
                particleSystem.Play();
                if (effectDuration <= 0f)
                {
                    effectDuration = particleSystem.main.duration + particleSystem.main.startLifetime.constantMax;
                }
            }
            if (effectDuration <= 0f)
            {
                effectDuration = 2f; // Default fallback
            }
            
            // Track for later release
            _activeVFX.Add((prefab, poolIndex, Time.time + effectDuration + 0.1f));
        }
        
        /// <summary>
        /// Spawns a VFX using the appropriate system based on VFXReference type.
        /// Supports both ParticleSystem prefabs and VFX Graph assets.
        /// </summary>
        private void SpawnVFX(VFXReference vfxRef, Vector3 position, Vector3 direction, float duration, System.Action<ParticleSystem> configure = null)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return;
            
            if (vfxRef.IsVFXGraph)
            {
                // Use VFXSpawner for GPU-accelerated VFX Graph
                _vfxSpawner?.SpawnMuzzleFlash(vfxRef, position, direction, duration);
            }
            else if (vfxRef.IsParticleSystem)
            {
                // Use legacy ParticleSystem pooling
                Quaternion rotation = direction != Vector3.zero ? Quaternion.LookRotation(direction) : Quaternion.identity;
                SpawnPooledVFX(vfxRef.particleSystemPrefab, position, rotation, duration, configure);
            }
        }

        private static void ConfigureParticleShapeRenderer(ParticleSystem particleSystem, Renderer targetRenderer)
        {
            if (particleSystem == null || targetRenderer == null) return;

            var shape = particleSystem.shape;
            shape.enabled = true;

            if (targetRenderer is SkinnedMeshRenderer skinned)
            {
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = skinned;
                shape.meshRenderer = null;
            }
            else if (targetRenderer is MeshRenderer meshRenderer)
            {
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = meshRenderer;
                shape.skinnedMeshRenderer = null;
            }
        }
        
        /// <summary>
        /// Spawns an AOE VFX using the appropriate system based on VFXReference type.
        /// </summary>
        private void SpawnAOEVFX(VFXReference vfxRef, Vector3 position, float radius)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return;
            
            if (vfxRef.IsVFXGraph)
            {
                // Use VFXSpawner for GPU-accelerated VFX Graph
                _vfxSpawner?.SpawnAOEExplosion(vfxRef, position, radius);
            }
            else if (vfxRef.IsParticleSystem)
            {
                // Use legacy ParticleSystem pooling with scale based on radius
                SpawnPooledVFX(vfxRef.particleSystemPrefab, position, Quaternion.identity, vfxRef.GetDuration());
            }
        }
        
        /// <summary>
        /// Reclaims finished VFX back to their pools. Should be called each frame.
        /// </summary>
        public void ReclaimFinishedVFX()
        {
            // Reclaim legacy ParticleSystem pools
            if (_activeVFX != null)
            {
                float currentTime = Time.time;
                
                for (int i = _activeVFX.Count - 1; i >= 0; i--)
                {
                    var (prefab, poolIndex, releaseTime) = _activeVFX[i];
                    
                    if (currentTime >= releaseTime)
                    {
                        // Return to pool
                        if (_vfxPools.TryGetValue(prefab, out var pool) && poolIndex < pool.Count)
                        {
                            pool[poolIndex].SetActive(false);
                            _vfxFreeIndices[prefab].Enqueue(poolIndex);
                        }
                        
                        // Remove from active list (swap with last for O(1) removal)
                        int lastIndex = _activeVFX.Count - 1;
                        if (i < lastIndex)
                        {
                            _activeVFX[i] = _activeVFX[lastIndex];
                        }
                        _activeVFX.RemoveAt(lastIndex);
                    }
                }
            }
            
            // Reclaim VFXSpawner pools (for VFXReference ParticleSystems)
            _vfxSpawner?.ReclaimFinishedVFX();
        }

        private void CacheAnimationClipLengths()
        {
            // Cache attack clip lengths (using global clips as baseline)
            if (_settings.attackClips != null && _settings.attackClips.Length > 0)
            {
                _attackClipLengths = new float[_settings.attackClips.Length];
                for (int i = 0; i < _settings.attackClips.Length; i++)
                {
                    AnimationClip clip = _settings.attackClips[i];
                    _attackClipLengths[i] = clip != null ? Mathf.Max(clip.length, 0.35f) : 0.5f;
                }
            }
            else
            {
                _attackClipLengths = System.Array.Empty<float>();
            }

            // Cache hit clip lengths
            if (_settings.hitClips != null && _settings.hitClips.Length > 0)
            {
                _hitClipLengths = new float[_settings.hitClips.Length];
                for (int i = 0; i < _settings.hitClips.Length; i++)
                {
                    AnimationClip clip = _settings.hitClips[i];
                    _hitClipLengths[i] = clip != null ? Mathf.Max(clip.length, 0.25f) : 0.35f;
                }
            }
            else
            {
                _hitClipLengths = System.Array.Empty<float>();
            }

            // Cache death clip length
            float deathClipDuration = _settings.deathClip != null ? Mathf.Max(_settings.deathClip.length, 0.5f) : 0f;
            float deathArrayMax = GetMaxClipLength(_settings.deathClips, 0.5f);
            _deathClipLength = deathClipDuration > 0f || deathArrayMax > 0f 
                ? Mathf.Max(deathClipDuration, deathArrayMax, 0.5f) 
                : 1f;

            // Cache block clip length
            _blockClipLength = _settings.blockClip != null ? Mathf.Max(_settings.blockClip.length, 0.3f) : 0.4f;
        }

        private float GetMaxClipLength(AnimationClip[] clips, float minLength)
        {
            if (clips == null || clips.Length == 0)
                return 0f;

            float maxLength = 0f;
            foreach (var clip in clips)
            {
                if (clip != null)
                    maxLength = Mathf.Max(maxLength, clip.length);
            }

            return maxLength > 0f ? Mathf.Max(maxLength, minLength) : 0f;
        }

        /// <summary>
        /// Initializes the death sinking effect for an agent.
        /// Should be called when agent dies to set up sinking parameters.
        /// </summary>
        private void InitializeDeathSinking(CrowdAgent agent, float currentTime, EnemyCrowd faction)
        {
            if (agent == null)
                return;

            // Get agent's current position
            Vector3 currentPosition = Vector3.zero;
            if (agent.crowdInstance != null)
                currentPosition = agent.crowdInstance.transform.position;
            else if (agent.navigationAgent != null)
                currentPosition = agent.WorldPosition;

            // Use per-faction settings if available, fall back to global settings
            float cleanupDelay = faction != null ? faction.deadAgentCleanupDelay : _settings.deadAgentCleanupDelay;
            Vector3 sinkOffset = faction != null ? faction.deathSinkOffset : _settings.deathSinkOffset;
            
            // Set up sinking parameters
            agent.isSinking = true;
            agent.sinkStartTime = currentTime + cleanupDelay; // Start sinking after cleanup delay
            agent.sinkStartPosition = currentPosition;
            agent.sinkTargetPosition = currentPosition + sinkOffset;
        }

        /// <summary>
        /// Initializes sinking for a ragdoll agent after the ragdoll has settled.
        /// Makes rigidbodies kinematic and starts the sink from current ragdoll position.
        /// </summary>
        private void InitializeRagdollSinking(CrowdAgent agent, float currentTime)
        {
            if (agent == null)
                return;

            agent.ragdollSinkPending = false;
            
            Debug.Log($"[RagdollSink] Agent {agent.index}: Initializing ragdoll sinking");
            
            // Make all ragdoll rigidbodies kinematic to freeze them in place
            // This allows us to move the root transform without physics interference
            // IMPORTANT: Set velocity to zero BEFORE making kinematic (can't set velocity on kinematic bodies)
            if (agent.ragdollRigidbodies != null)
            {
                foreach (var rb in agent.ragdollRigidbodies)
                {
                    if (rb != null)
                    {
                        // First zero out velocities while still non-kinematic
                        if (!rb.isKinematic)
                        {
#if UNITY_6000_0_OR_NEWER
                            rb.linearVelocity = Vector3.zero;
#else
                            rb.velocity = Vector3.zero;
#endif
                            rb.angularVelocity = Vector3.zero;
                        }
                        // Then make kinematic
                        rb.isKinematic = true;
                    }
                }
                // Debug log removed for production build
            }
            
            // Get current root position (where the ragdoll ended up)
            Vector3 currentPosition = Vector3.zero;
            if (agent.crowdInstance != null)
                currentPosition = agent.crowdInstance.transform.position;
            else if (agent.navigationAgent != null)
                currentPosition = agent.WorldPosition;

            // Get per-faction settings
            EnemyCrowd faction = GetEnemyCrowd(agent);
            Vector3 sinkOffset = faction != null ? faction.deathSinkOffset : _settings.deathSinkOffset;
            
            // Set up sinking parameters - start immediately since cleanup delay already passed
            agent.isSinking = true;
            agent.sinkStartTime = currentTime; // Start immediately
            agent.sinkStartPosition = currentPosition;
            agent.sinkTargetPosition = currentPosition + sinkOffset;
            
            // Add to sinking list for efficient per-frame updates
            if (!_sinkingAgents.Contains(agent))
                _sinkingAgents.Add(agent);
            
            // Debug log removed for production build
        }

        /// <summary>
        /// Processes the death sinking effect for an agent.
        /// Gradually moves the agent from start position to target position based on sink duration.
        /// </summary>
        private void ProcessDeathSinking(CrowdAgent agent, float currentTime)
        {
            if (agent == null || !agent.isSinking)
                return;

            // Check if sinking should start
            if (currentTime < agent.sinkStartTime)
            {
                // Still waiting for cleanup delay
                return;
            }

            // Get per-faction settings
            EnemyCrowd faction = GetEnemyCrowd(agent);
            float sinkDuration = faction != null ? faction.deathSinkDuration : _settings.deathSinkDuration;
            AnimationCurve sinkCurve = faction != null ? faction.deathSinkCurve : _settings.deathSinkCurve;
            
            // Calculate sink progress (0 to 1)
            float sinkElapsed = currentTime - agent.sinkStartTime;
            float sinkProgress = Mathf.Clamp01(sinkElapsed / sinkDuration);
            
            // Apply animation curve if available
            float curvedProgress = sinkProgress;
            if (sinkCurve != null && sinkCurve.keys.Length > 0)
            {
                curvedProgress = sinkCurve.Evaluate(sinkProgress);
            }

            // Lerp position
            Vector3 newPosition = Vector3.Lerp(agent.sinkStartPosition, agent.sinkTargetPosition, curvedProgress);

            // Apply new position to agent
            if (agent.crowdInstance != null)
            {
                agent.crowdInstance.transform.position = newPosition;
            }
            else if (agent.navigationAgent != null)
            {
                agent.navigationAgent.Warp(newPosition);
            }

            // Check if sinking is complete
            if (sinkProgress >= 1f)
            {
                agent.isSinking = false;
                DisableAStarNavigationAfterDeath(agent);
                // Note: Don't set pendingCleanup here - let the performance manager handle it
                // after confirming isSinking is false
            }
        }

        /// <summary>
        /// Updates all sinking agents. Call this every frame for smooth sinking animation.
        /// This is separate from the combat cursor to ensure consistent animation regardless of agent count.
        /// </summary>
        public void UpdateSinkingAgents()
        {
            if (_sinkingAgents.Count == 0)
                return;

            float currentTime = Time.time;

            // Process all sinking agents every frame for smooth animation
            for (int i = _sinkingAgents.Count - 1; i >= 0; i--)
            {
                CrowdAgent agent = _sinkingAgents[i];
                
                if (agent == null || !agent.isSinking)
                {
                    // Remove completed or invalid agents
                    _sinkingAgents.RemoveAt(i);
                    continue;
                }

                ProcessDeathSinking(agent, currentTime);
            }
        }

        /// <summary>
        /// Gets the current count of agents actively sinking (for debugging/stats).
        /// </summary>
        public int SinkingAgentCount => _sinkingAgents.Count;

        /// <summary>
        /// Removes an agent from the sinking list.
        /// Used when respawning/pooling agents that were sinking.
        /// </summary>
        public void RemoveFromSinkingAgents(CrowdAgent agent)
        {
            if (agent != null)
            {
                _sinkingAgents.Remove(agent);
            }
        }

        private void DisableAStarNavigationAfterDeath(CrowdAgent agent)
        {
            if (agent == null || !agent.isDead)
                return;

            var navAgent = agent.navigationAgent;
            if (navAgent == null || !navAgent.Capabilities.ShouldThrottleFrequentDestinationUpdates)
                return;

            // Disable A* navigation after sinking is complete (or immediately if no sinking).
            // This prevents dead A* agents from continuing to tick in the update loop.
            navAgent.IsStopped = true;
            navAgent.ResetPath();
            navAgent.UpdatePosition = false;
            navAgent.UpdateRotation = false;
            navAgent.Enabled = false;
        }

        private EnemyCrowd GetEnemyCrowd(CrowdAgent agent)
        {
            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                return _enemyCrowds[agent.enemyCrowdIndex];
            }
            return null;
        }

        private (AnimationClip[] attacks, AnimationClip[] hits) GetCombatAnimationsForAgent(CrowdAgent agent)
        {
            if (agent != null && agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd faction = _enemyCrowds[agent.enemyCrowdIndex];
                if (faction != null)
                {
                    AnimationClip[] attackClips = null;
                    
                    // For Hybrid combat type, use mode-specific attack clips
                    if (faction.combatRangeType == CombatRangeType.Hybrid)
                    {
                        if (agent.activeHybridMode == CombatRangeType.Ranged && 
                            faction.rangedAttackClipsOverride != null && faction.rangedAttackClipsOverride.Length > 0)
                        {
                            attackClips = faction.rangedAttackClipsOverride;
                        }
                        else if (agent.activeHybridMode == CombatRangeType.Melee && 
                                 faction.meleeAttackClipsOverride != null && faction.meleeAttackClipsOverride.Length > 0)
                        {
                            attackClips = faction.meleeAttackClipsOverride;
                        }
                    }
                    
                    // Fall back to regular attack clips if no mode-specific clips or not hybrid
                    if (attackClips == null)
                    {
                        bool hasCustomAttacks = faction.attackClipsOverride != null && faction.attackClipsOverride.Length > 0;
                        attackClips = hasCustomAttacks ? faction.attackClipsOverride : _settings.attackClips;
                    }
                    
                    bool hasCustomHits = faction.hitClipsOverride != null && faction.hitClipsOverride.Length > 0;
                    
                    return (
                        attackClips,
                        hasCustomHits ? faction.hitClipsOverride : _settings.hitClips
                    );
                }
            }

            return (_settings.attackClips, _settings.hitClips);
        }

        private AnimationClip ResolveBlockClip(CrowdAgent agent)
        {
            EnemyCrowd faction = GetEnemyCrowd(agent);

            // Try directional animations first if enabled and attacker position is known
            if (agent != null && agent.hasAttackTargetPosition)
            {
                AnimationClip[] fallbackClips = null;
                if (faction?.blockClipsOverride != null && faction.blockClipsOverride.Length > 0)
                    fallbackClips = faction.blockClipsOverride;
                else if (_settings.blockClips != null && _settings.blockClips.Length > 0)
                    fallbackClips = _settings.blockClips;
                else if (_settings.blockClip != null)
                    fallbackClips = new[] { _settings.blockClip };

                var result = DirectionalAnimationHelper.GetDirectionalClip(
                    DirectionalAnimationType.Block,
                    _animationSettings,
                    faction,
                    agent,
                    agent.attackTargetPosition,
                    fallbackClips,
                    -1 // Blocks don't typically need repeat tracking
                );

                if (result.clip != null)
                    return result.clip;
            }

            // Fallback to non-directional clip
            if (faction?.blockClipsOverride != null && faction.blockClipsOverride.Length > 0)
                return faction.blockClipsOverride[Random.Range(0, faction.blockClipsOverride.Length)];

            return _settings.blockClip;
        }

        private AnimationClip ResolveDeathClip(CrowdAgent agent)
        {
            EnemyCrowd faction = GetEnemyCrowd(agent);

            // Try directional animations first if enabled and attacker position is known
            if (agent != null && agent.hasAttackTargetPosition)
            {
                // Gather fallback clips
                AnimationClip[] fallbackClips = null;
                if (faction != null && faction.deathClipsOverride != null && faction.deathClipsOverride.Length > 0)
                    fallbackClips = faction.deathClipsOverride;
                else if (_settings.deathClips != null && _settings.deathClips.Length > 0)
                    fallbackClips = _settings.deathClips;

                var result = DirectionalAnimationHelper.GetDirectionalClip(
                    DirectionalAnimationType.Death,
                    _animationSettings,
                    faction,
                    agent,
                    agent.attackTargetPosition,
                    fallbackClips,
                    agent.lastDeathClipIndex
                );

                if (result.clip != null)
                {
                    agent.lastDeathClipIndex = result.index;
                    return result.clip;
                }
            }

            // Fallback to non-directional clips
            if (faction != null)
            {
                if (faction.deathClipsOverride != null && faction.deathClipsOverride.Length > 0)
                {
                    int index = Random.Range(0, faction.deathClipsOverride.Length);
                    agent.lastDeathClipIndex = index;
                    return faction.deathClipsOverride[index];
                }
            }

            // Fall back to global clips
            if (_settings.deathClips != null && _settings.deathClips.Length > 0)
            {
                int index = Random.Range(0, _settings.deathClips.Length);
                agent.lastDeathClipIndex = index;
                return _settings.deathClips[index];
            }

            agent.lastDeathClipIndex = 0;
            return _settings.deathClip;
        }

        private bool HasBlockAnimationClip()
        {
            return (_settings.blockClips != null && _settings.blockClips.Length > 0) || _settings.blockClip != null || HasFactionBlockClips();
        }

        private bool HasFactionBlockClips()
        {
            if (_enemyCrowds == null)
                return false;

            foreach (var faction in _enemyCrowds)
            {
                if (faction?.blockClipsOverride != null && faction.blockClipsOverride.Length > 0)
                    return true;
            }

            return false;
        }

        private float GetRandomizedBlockHoldDuration()
        {
            return Random.Range(_settings.blockHoldTimeRange.x, _settings.blockHoldTimeRange.y);
        }
        
        /// <summary>
        /// Rotates an agent to face a target position smoothly using their faction's facing speed.
        /// </summary>
        private void FaceTarget(CrowdAgent agent, Vector3 targetPosition)
        {
            FaceTargetInternal(agent, targetPosition, immediate: false);
        }
        
        /// <summary>
        /// Immediately rotates an agent to face a target position (used for attack/block).
        /// </summary>
        private void FaceTargetImmediate(CrowdAgent agent, Vector3 targetPosition)
        {
            FaceTargetInternal(agent, targetPosition, immediate: true);
        }
        
        /// <summary>
        /// Internal method that handles both smooth and immediate facing.
        /// </summary>
        private void FaceTargetInternal(CrowdAgent agent, Vector3 targetPosition, bool immediate)
        {
            if (agent == null)
                return;

            Transform agentTransform = agent.navigationAgent != null
                ? agent.navigationAgent.Transform
                : agent.transform;
            if (agentTransform == null)
                return;

            Vector3 direction = targetPosition - agentTransform.position;
            direction.y = 0f; // Keep rotation on horizontal plane
            
            if (direction.sqrMagnitude < 0.0001f)
                return;
            
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            
            if (immediate)
            {
                // Immediate facing for attacks and blocks
                agentTransform.rotation = targetRotation;
            }
            else
            {
                // Smooth facing for general combat
                EnemyCrowd faction = GetEnemyCrowd(agent);
                float facingSpeed = faction != null ? faction.combatFacingSpeed : 720f;
                float rotationSpeed = facingSpeed * Time.deltaTime;
                agentTransform.rotation = Quaternion.RotateTowards(agentTransform.rotation, targetRotation, rotationSpeed);
            }
        }
        
        /// <summary>
        /// Animation type enum for proper trigger parameter selection in Mecanim workflows
        /// </summary>
        private enum CombatAnimationType
        {
            Hit,
            Death,
            Attack,
            Block,
            Other
        }
        
        /// <summary>
        /// Plays a combat animation clip on the agent (attack, block, hit, death).
        /// Handles both GPU-based animations (ComputeAnimator) and Mecanim-based animations.
        /// Returns true if the animation was successfully started.
        /// </summary>
        private bool PlayCombatClip(CrowdAgent agent, AnimationClip clip, float transitionTime, bool loop, CombatAnimationType animType = CombatAnimationType.Other)
        {
            if (agent == null || clip == null)
                return false;
            
            bool animationPlayed = false;
            
            // Determine if this is a GPU-based workflow (ComputeAnimator)
            bool isGpuWorkflow = agent.currentWorkflow != HybridWorkflowType.BoneTracker && 
                                 agent.currentWorkflow != HybridWorkflowType.MecanimReader;
            
            // GPU-based animation (ComputeAnimator workflow)
            if (isGpuWorkflow && agent.crowdInstance != null)
            {
                // Validate GPU animation is possible
                if (agent.crowdInstance.AnimatorWorkflow != null && IsBufferIndexValid(agent))
                {
                    try
                    {
                        agent.crowdInstance.StartAnimation(clip, -1f, 1f, transitionTime, null, loop);
                        animationPlayed = true;
                    }
                    catch (System.Exception e)
                    {
                        // GPU animation failed - for ComputeAnimator workflow, we don't fall back to Mecanim
                        animationPlayed = false;
                        Debug.LogWarning($"[PlayCombatClip] Agent {agent.index}: GPU animation FAILED: {e.Message}");
                    }
                }
                // For GPU workflows, don't fall through to Mecanim - it won't have the states
            }
            
            // Mecanim - ONLY for Mecanim-based workflows (BoneTracker, MecanimReader)
            if (!isGpuWorkflow && !animationPlayed && agent.mecanimAnimator != null)
            {
                int stateHash = Animator.StringToHash(clip.name);
                bool hasState = agent.mecanimAnimator.HasState(0, stateHash);
                
                // For Death animations, try triggers FIRST since they work better with Mecanim
                // (matching how hit animations work - they use triggers successfully)
                bool useTriggerFirst = (animType == CombatAnimationType.Death);
                
                if (hasState && !useTriggerFirst)
                {
                    agent.mecanimAnimator.CrossFade(stateHash, transitionTime, 0, 0f);
                    animationPlayed = true;
                }
                else
                {
                    // State not found by clip name - try trigger parameters based on animation type
                    EnemyCrowd faction = GetEnemyCrowd(agent);
                    
                    string[] triggers = null;
                    string triggerTypeName = "";
                    
                    switch (animType)
                    {
                        case CombatAnimationType.Hit:
                            triggers = faction?.hitTriggerParametersOverride;
                            if (triggers == null || triggers.Length == 0)
                                triggers = _settings?.hitTriggerParameters;
                            triggerTypeName = "hit";
                            break;
                            
                        case CombatAnimationType.Death:
                            triggers = faction?.deathTriggerParametersOverride;
                            if (triggers == null || triggers.Length == 0)
                                triggers = _settings?.deathTriggerParameters;
                            triggerTypeName = "death";
                            break;
                            
                        case CombatAnimationType.Attack:
                            triggers = faction?.attackTriggerParametersOverride;
                            if (triggers == null || triggers.Length == 0)
                                triggers = _settings?.attackTriggerParameters;
                            triggerTypeName = "attack";
                            break;
                            
                        case CombatAnimationType.Block:
                            // No specific block triggers defined yet, could add later
                            triggerTypeName = "block";
                            break;
                            
                        default:
                            // For Other/unknown types, don't try any triggers - just fail gracefully
                            break;
                    }
                    
                    if (triggers != null && triggers.Length > 0)
                    {
                        // Pick a random trigger from the list (for variety in hit1, hit2, hit3 etc)
                        int randomIndex = Random.Range(0, triggers.Length);
                        string triggerName = triggers[randomIndex];
                        
                        if (!string.IsNullOrEmpty(triggerName))
                        {
                            // Check if this trigger parameter exists
                            bool hasTrigger = false;
                            foreach (var param in agent.mecanimAnimator.parameters)
                            {
                                if (param.name == triggerName && param.type == AnimatorControllerParameterType.Trigger)
                                {
                                    hasTrigger = true;
                                    break;
                                }
                            }
                            
                            if (hasTrigger)
                            {
                                agent.mecanimAnimator.SetTrigger(triggerName);
                                animationPlayed = true;
                            }
                            else
                            {
                                Debug.LogWarning($"[PlayCombatClip] Agent {agent.index}: {triggerTypeName} trigger '{triggerName}' not found in Animator");
                            }
                        }
                    }
                    
                    // For Death animations, if trigger failed but state exists, try CrossFade as fallback
                    if (!animationPlayed && animType == CombatAnimationType.Death && hasState)
                    {
                        agent.mecanimAnimator.CrossFade(stateHash, transitionTime, 0, 0f);
                        animationPlayed = true;
                    }
                    
                    if (!animationPlayed && animType != CombatAnimationType.Other)
                    {
                        Debug.LogWarning($"[PlayCombatClip] Agent {agent.index}: Mecanim animation FAILED - no state '{clip.name}' and no {triggerTypeName} triggers configured/found");
                    }
                }
            }
            
            return animationPlayed;
        }
        
        /// <summary>
        /// Plays a combat animation with retry logic for critical animations like death.
        /// If the first attempt fails, it will retry on the next frame.
        /// </summary>
        private bool PlayCombatClipWithRetry(CrowdAgent agent, AnimationClip clip, float transitionTime, bool loop, CombatAnimationType animType, bool isCritical = false)
        {
            bool success = PlayCombatClip(agent, clip, transitionTime, loop, animType);
            
            if (!success && isCritical)
            {
                // Mark agent for animation retry on next frame
                agent.pendingCombatClip = clip;
                agent.pendingCombatClipTransition = transitionTime;
                agent.pendingCombatClipLoop = loop;
                agent.pendingCombatClipAnimType = (int)animType;
                agent.pendingCombatClipRetries = 0;
            }
            
            return success;
        }
        
        /// <summary>
        /// Processes pending combat animation retries for agents where animation failed.
        /// Called each frame to retry critical animations like death.
        /// </summary>
        private void ProcessPendingCombatAnimation(CrowdAgent agent)
        {
            if (agent == null || agent.pendingCombatClip == null)
                return;
            
            // Max 3 retry attempts
            const int MAX_RETRIES = 3;
            if (agent.pendingCombatClipRetries >= MAX_RETRIES)
            {
                // Give up and clear pending animation
                agent.pendingCombatClip = null;
                return;
            }
            
            agent.pendingCombatClipRetries++;
            
            bool success = PlayCombatClip(agent, agent.pendingCombatClip, agent.pendingCombatClipTransition, agent.pendingCombatClipLoop, (CombatAnimationType)agent.pendingCombatClipAnimType);
            
            if (success)
            {
                // Animation played successfully, clear pending
                agent.pendingCombatClip = null;
                
                // Restore animator speed if it was disabled
                if (agent.mecanimAnimator != null && agent.mecanimAnimator.speed == 0f)
                {
                    agent.mecanimAnimator.speed = 1f;
                }
            }
        }
        
        /// <summary>
        /// Validates that the agent's buffer index is within the current buffer size.
        /// This prevents "bufferIndex out of bounds" and "Can not find Crowd data" errors 
        /// when agents are rapidly spawned/removed.
        /// </summary>
        private static bool IsBufferIndexValid(CrowdAgent agent)
        {
            if (agent?.crowdInstance?.PrefabComponent == null)
                return false;
                
            var prefab = agent.crowdInstance.PrefabComponent;
            if (!prefab.IsInstanced || prefab.renderKey == 0 || prefab.bufferIndex < 0)
                return false;
                
            if (!GPUIRenderingSystem.IsActive)
                return false;
                
            if (!GPUIRenderingSystem.TryGetRenderSource(prefab.renderKey, out var renderSource))
                return false;
                
            if (prefab.bufferIndex >= renderSource.bufferSize)
                return false;
            
            // Validate crowd data exists to prevent "Can not find Crowd data for renderer" errors
            if (!GPUICrowdSkinningSystem.IsActive)
                return false;
            if (!GPUICrowdSkinningSystem.TryGetCrowdRenderSource(prefab.renderKey, out _))
                return false;
                
            return true;
        }

        #endregion
        
        #region Phase 2: Settings Helpers (EnemyCrowd as single source of truth)
        
        private bool GetEnableCombatSystem(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.enableCombatSystem : _settings.enableCombatSystem;
        }
        
        private float GetEngagementRadius(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.engagementRadius : _settings.engagementRadius;
        }
        
        private float GetAgentBaseHealth(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.agentBaseHealth : _settings.agentBaseHealth;
        }
        
        private float GetAgentHealthVariance(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.agentHealthVariance : _settings.agentHealthVariance;
        }
        
        private float GetMeleeAttackDamage(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.meleeAttackDamage : _settings.agentAttackDamage;
        }

        private float GetRangedAttackDamage(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.rangedAttackDamage : _settings.agentAttackDamage;
        }
        
        private float GetAttackCooldown(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.attackCooldown : _settings.attackCooldown;
        }
        
        private Vector2 GetAttackCooldownJitter(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.attackCooldownJitter : _settings.attackCooldownJitter;
        }
        
        private float GetBlockChance(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.blockChance : _settings.blockChance;
        }
        
        private Vector2 GetBlockHoldTimeRange(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.blockHoldTimeRange : _settings.blockHoldTimeRange;
        }
        
        private int GetMaxCombatUpdatesPerFrame(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.maxCombatUpdatesPerFrame : _settings.maxCombatUpdatesPerFrame;
        }
        
        private bool GetMaintainFacingBetweenAttacks(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.maintainFacingBetweenAttacks : _settings.maintainFacingBetweenAttacks;
        }
        
        private bool GetStopMovementWhenClose(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.stopMovementWhenClose : _settings.stopMovementWhenClose;
        }
        
        private bool GetLockMovementDuringAttacks(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.lockMovementDuringAttacks : _settings.lockMovementDuringAttacks;
        }

        private bool GetEnableMeleeAutoAttack(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.enableMeleeAutoAttack : _settings.enableMeleeAutoAttack;
        }

        private bool GetTriggerHitReactionOnDamage(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.triggerHitReactionOnDamage : _settings.triggerHitReactionOnDamage;
        }
        
        private bool GetEnablePostAttackRetreat(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.enablePostAttackRetreat : _settings.enablePostAttackRetreat;
        }
        
        private Vector2 GetPostAttackRetreatDistance(CrowdAgent agent)
        {
            return agent?.enemyCrowd != null ? agent.enemyCrowd.postAttackRetreatDistance : _settings.postAttackRetreatDistance;
        }
        
        #endregion
        
        #region Charge Attack System
        
        /// <summary>
        /// Attempts to initiate a charge attack for a melee agent.
        /// Returns true if charge was initiated, false otherwise.
        /// </summary>
        private bool TryInitiateChargeAttack(CrowdAgent agent, EnemyCrowd faction, float distanceToTarget, Vector3 targetPosition, float currentTime)
        {
            // Already charging - don't start another charge!
            if (agent.isCharging || agent.combatState == CrowdCombatState.Charging) return false;
            
            // Don't start charge if agent is under manual RTS control
            if (agent.navigationState == AgentNavigationState.ManualCommand) return false;
            
            // HoldPosition stance: Don't charge, stay in place
            if (agent.combatStance == CombatStance.HoldPosition) return false;
            
            // Check if faction has charge attacks enabled
            if (faction == null || !faction.canUseSpecialAbilities) return false;
            
            var chargeConfig = faction.chargeAttackConfig;
            if (chargeConfig == null || !chargeConfig.enableChargeAttack) return false;
            
            // Check if agent is on cooldown
            if (currentTime < agent.nextChargeTime) return false;
            
            // Check distance requirements
            if (distanceToTarget < chargeConfig.chargeMinDistance || distanceToTarget > chargeConfig.chargeMaxDistance)
                return false;
            
            // Check if this is a melee-capable faction (don't charge if purely ranged)
            if (faction.combatRangeType == CombatRangeType.Ranged) return false;
            
            // Check if target agent is still alive (don't charge at dead targets)
            if (agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count)
            {
                var targetAgent = _agents[agent.attackTargetAgentIndex];
                if (targetAgent == null || targetAgent.isDead)
                {
                    return false; // Target is dead, don't start charge
                }
            }
            
            // Random chance check
            if (Random.value > chargeConfig.chargeChance) return false;
            
            // All conditions met - initiate charge!
            StartChargeAttack(agent, faction, targetPosition, currentTime);
            return true;
        }
        
        /// <summary>
        /// Starts a charge attack for the given agent.
        /// </summary>
        private void StartChargeAttack(CrowdAgent agent, EnemyCrowd faction, Vector3 targetPosition, float currentTime)
        {
            var chargeConfig = faction.chargeAttackConfig;
            
            // Store charge state
            agent.isCharging = true;
            agent.chargeStartPosition = agent.WorldPosition;
            agent.chargeTargetPosition = targetPosition;
            agent.chargeStartTime = currentTime;
            agent.lastChargeTime = currentTime;
            
            // Enter charging combat state
            EnterCombatState(agent, CrowdCombatState.Charging, chargeConfig.chargeMaxDuration, currentTime);
            
            // Boost speed for charge using the unified speed system
            if (agent.navigationAgent != null && agent.IsOnNavMesh)
            {
                SetChargeSpeedMultiplier(agent, chargeConfig.chargeSpeedMultiplier);
                TrySetCombatDestination(agent, targetPosition);
            }
            
            // Play charge animation
            var chargeClip = chargeConfig.chargeAnimationClip;
            if (chargeClip != null)
            {
                PlayCombatClip(agent, chargeClip, 0.1f, true);
            }
            
            // Play charge start sound
            if (chargeConfig.chargeStartSound != null)
            {
                PlayChargeSound(agent, chargeConfig.chargeStartSound, chargeConfig.chargeStartVolume, 
                                chargeConfig.soundPitchVariation, chargeConfig.soundSpatialBlend);
            }
            
            // Start charge loop sound
            if (chargeConfig.chargeLoopSound != null)
            {
                StartChargeLoopSound(agent, chargeConfig);
            }
            
            // Spawn charge trail VFX
            if (chargeConfig.chargeTrailVFX.type != VFXType.None)
            {
                SpawnChargeTrailVFX(agent, chargeConfig);
            }
        }
        
        /// <summary>
        /// Updates an active charge attack - checks for impact or timeout.
        /// </summary>
        private void UpdateChargeAttack(CrowdAgent agent, float currentTime, Vector3 targetPosition)
        {
            var faction = GetEnemyCrowd(agent);
            if (faction == null)
            {
                EndChargeAttack(agent, false);
                return;
            }
            
            var chargeConfig = faction.chargeAttackConfig;
            Vector3 agentPosition = agent.WorldPosition;
            float distanceToTarget = Vector3.Distance(agentPosition, targetPosition);
            float chargeDuration = currentTime - agent.chargeStartTime;
            
            // Check for timeout
            if (chargeDuration >= chargeConfig.chargeMaxDuration)
            {
                EndChargeAttack(agent, false);
                return;
            }
            
            // Check if target died during charge - abort charge immediately
            if (agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count)
            {
                var targetAgent = _agents[agent.attackTargetAgentIndex];
                if (targetAgent == null || targetAgent.isDead)
                {
                    EndChargeAttack(agent, false);
                    return;
                }
            }
            
            // FAILSAFE: Check for max distance traveled - stop if agent went too far
            float distanceTraveledSoFar = Vector3.Distance(agent.chargeStartPosition, agentPosition);
            float maxAllowedDistance = chargeConfig.chargeMaxDistance * 1.5f; // 50% buffer beyond max charge distance
            if (distanceTraveledSoFar >= maxAllowedDistance)
            {
                EndChargeAttack(agent, false);
                return;
            }
            
            // Check for impact (reached melee range)
            float impactDistance = faction.meleeMaxDistance * 1.2f; // Slightly larger than melee range
            if (distanceToTarget <= impactDistance)
            {
                float distanceTraveled = Vector3.Distance(agent.chargeStartPosition, agentPosition);
                
                // Execute impact damage
                ExecuteChargeImpact(agent, faction, agentPosition, distanceTraveled, currentTime);
                EndChargeAttack(agent, true);
                return;
            }
            
            // Update charge target position (in case target moved)
            if (agent.navigationAgent != null && agent.IsOnNavMesh)
            {
                TrySetCombatDestination(agent, targetPosition);
            }
            
            // Update charge loop audio position (since it's not parented to agent)
            if (agent.chargeLoopAudioSource != null)
            {
                agent.chargeLoopAudioSource.transform.position = agentPosition;
            }
        }
        
        /// <summary>
        /// Executes the charge impact - deals damage, spawns VFX, plays sound.
        /// </summary>
        private void ExecuteChargeImpact(CrowdAgent agent, EnemyCrowd faction, Vector3 impactPosition, float distanceTraveled, float currentTime)
        {
            var chargeConfig = faction.chargeAttackConfig;
            float baseDamage = chargeConfig.CalculateDamage(distanceTraveled);
            
            // Find the primary target
            CrowdAgent primaryTarget = null;
            if (agent.attackTargetAgentIndex >= 0 && agent.attackTargetAgentIndex < _agents.Count)
            {
                primaryTarget = _agents[agent.attackTargetAgentIndex];
                if (primaryTarget != null && !primaryTarget.isDead)
                {
                    // Deal full damage to primary target - charge hits at torso level
                    Vector3 primaryHitPoint = GetAgentPosition(primaryTarget) + Vector3.up * 1.0f;
                    ApplyDamageToAgent(primaryTarget, baseDamage, true, false, null, null, impactPosition, agent.index, primaryHitPoint, true, true);
                    
                    // Apply knockback to primary target using fake physics impulse system
                    if (chargeConfig.knockbackForce > 0f && primaryTarget.navigationAgent != null)
                    {
                        Vector3 knockbackDir = (primaryTarget.WorldPosition - impactPosition).normalized;
                        ApplyChargeKnockback(primaryTarget, knockbackDir, chargeConfig, currentTime);
                    }
                }
            }
            
            // AOE damage to nearby enemies
            if (chargeConfig.enableAOE && chargeConfig.aoeRadius > 0f)
            {
                int agentFactionIndex = agent.enemyCrowdIndex;
                float aoeRadiusSqr = chargeConfig.aoeRadius * chargeConfig.aoeRadius;
                
                for (int i = 0; i < _agents.Count; i++)
                {
                    var targetAgent = _agents[i];
                    if (targetAgent == null || targetAgent.isDead) continue;
                    if (targetAgent == primaryTarget) continue; // Already damaged
                    if (targetAgent.enemyCrowdIndex == agentFactionIndex) continue; // Same faction
                    
                    if (targetAgent.navigationAgent == null) continue;
                    
                    float distSqr = (targetAgent.WorldPosition - impactPosition).sqrMagnitude;
                    if (distSqr <= aoeRadiusSqr)
                    {
                        float dist = Mathf.Sqrt(distSqr);
                        float aoeDamage = chargeConfig.CalculateAOEDamage(baseDamage, dist);
                        // AOE charge damage hits at torso level
                        Vector3 aoeHitPoint = targetAgent.WorldPosition + Vector3.up * 1.0f;
                        ApplyDamageToAgent(targetAgent, aoeDamage, true, false, null, null, impactPosition, agent.index, aoeHitPoint, true, true);
                        
                        // Apply knockback to AOE targets as well (with falloff based on distance)
                        if (chargeConfig.knockbackForce > 0f)
                        {
                            float distanceFalloff = 1f - (dist / chargeConfig.aoeRadius) * 0.5f; // 50-100% knockback based on distance
                            Vector3 aoeKnockbackDir = (targetAgent.WorldPosition - impactPosition).normalized;
                            ApplyChargeKnockback(targetAgent, aoeKnockbackDir, chargeConfig, currentTime, distanceFalloff);
                        }
                    }
                }
            }
            
            // Spawn impact VFX
            if (chargeConfig.chargeImpactVFX.type != VFXType.None)
            {
                SpawnChargeImpactVFX(impactPosition, chargeConfig);
            }
            
            // Play impact sound
            if (chargeConfig.chargeImpactSound != null)
            {
                PlayChargeSound(agent, chargeConfig.chargeImpactSound, chargeConfig.chargeImpactVolume,
                                chargeConfig.soundPitchVariation, chargeConfig.soundSpatialBlend);
            }
            
            // Play impact animation
            if (chargeConfig.chargeImpactAnimationClip != null)
            {
                PlayCombatClip(agent, chargeConfig.chargeImpactAnimationClip, 0.1f, false);
            }
        }
        
        /// <summary>
        /// Ends the charge attack and resets agent state.
        /// </summary>
        private void EndChargeAttack(CrowdAgent agent, bool wasSuccessful)
        {
            var faction = GetEnemyCrowd(agent);
            var chargeConfig = faction?.chargeAttackConfig;
            
            // Stop charge loop sound using the controller for clean shutdown
            if (agent.chargeLoopAudioController != null)
            {
                agent.chargeLoopAudioController.StopImmediately();
                agent.chargeLoopAudioController = null;
                agent.chargeLoopAudioSource = null;
            }
            else if (agent.chargeLoopAudioSource != null)
            {
                // Fallback for old audio sources without controller
                AudioSource audioToStop = agent.chargeLoopAudioSource;
                GameObject objToDestroy = audioToStop.gameObject;
                
                audioToStop.Stop();
                audioToStop.loop = false;
                audioToStop.volume = 0f;
                audioToStop.clip = null;
                audioToStop.enabled = false;
                
                agent.chargeLoopAudioSource = null;
                
                if (objToDestroy != null)
                {
                    #if UNITY_EDITOR
                    Object.DestroyImmediate(objToDestroy);
                    #else
                    Object.Destroy(objToDestroy);
                    #endif
                }
            }
            
            // Restore speed using unified speed system
            SetChargeSpeedMultiplier(agent, 1f);
            
            // Reset charge state
            agent.isCharging = false;
            
            // Set next charge cooldown
            if (chargeConfig != null)
            {
                agent.nextChargeTime = Time.time + chargeConfig.GetRandomizedCooldown();
            }
            
            // Return to idle state (or impact animation state if successful)
            float impactDuration = wasSuccessful && chargeConfig != null ? chargeConfig.impactAnimationDuration : 0f;
            EnterCombatState(agent, wasSuccessful ? CrowdCombatState.Attacking : CrowdCombatState.Idle, impactDuration, Time.time);
        }
        
        /// <summary>
        /// Applies a fake-physics knockback impulse to a target hit by a charge attack.
        /// Uses the navmesh offset system to create a parabolic arc while keeping the agent on the NavMesh.
        /// </summary>
        /// <param name="target">The agent to knock back</param>
        /// <param name="direction">Direction to push (normalized)</param>
        /// <param name="config">Charge config with knockback parameters</param>
        /// <param name="currentTime">Current game time</param>
        /// <param name="forceFalloff">Optional multiplier for reduced knockback (e.g., AOE edge falloff)</param>
        private void ApplyChargeKnockback(CrowdAgent target, Vector3 direction, ChargeAttackConfig config, float currentTime, float forceFalloff = 1f)
        {
            if (target == null || target.isDead) return;
            if (target.isDisplacementImmune) return;
            
            // Ensure we have a valid horizontal direction
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (horizontal == Vector3.zero)
                horizontal = Vector3.forward;
            
            // Calculate velocities with optional falloff
            float pushForce = config.knockbackForce * forceFalloff;
            float liftForce = config.knockbackLiftForce * forceFalloff;
            
            Vector3 velocity = horizontal * pushForce;
            velocity.y += liftForce;
            
            // Get agent's current position for ground reference
            bool hasNav = target.navigationAgent != null;
            Vector3 agentPos = hasNav ? target.WorldPosition : (target.transform != null ? target.transform.position : Vector3.zero);

            bool handledByAstar = EnemyMassesAstarKnockbackUtility.TryQueueAstarKnockback(
                target,
                velocity,
                config.knockbackDuration,
                config.knockbackDrag,
                config.knockbackGravity,
                pushForce,
                liftForce);

            if (!handledByAstar)
            {
                // Apply the fake-physics impulse (always override existing impulses from charge)
                target.hasNavmeshOffsetImpulse = true;
                target.navmeshOffset = Vector3.zero;
                target.navmeshOffsetVelocity = velocity;
                target.navmeshOffsetGroundPosition = hasNav ? target.navigationAgent.Position : agentPos;
                target.navmeshOffsetGravity = config.knockbackGravity;
                target.navmeshOffsetDrag = config.knockbackDrag;
                target.navmeshOffsetDuration = config.knockbackDuration;
                target.navmeshOffsetEndTime = currentTime + config.knockbackDuration;
                
                // Store impulse values for animation system
                target.lastNavmeshImpulsePush = pushForce;
                target.lastNavmeshImpulseLift = liftForce;
                target.navmeshImpulseAnimationPlayed = false;
                
                // Mark lift impulse for get-up animation flow (threshold from InstructionCrowdNavmeshImpulse)
                const float LiftGetUpThreshold = 1.5f;
                target.liftImpulseActive = liftForce >= LiftGetUpThreshold;
            }
            
            // Lock attacks during knockback to prevent immediate retaliation
            float lockEnd = currentTime + config.knockbackDuration + 0.5f;
            target.impulseAttackLockEndTime = Mathf.Max(target.impulseAttackLockEndTime, lockEnd);
        }
        
        /// <summary>
        /// Cleans up charge audio source. Called when agent dies/ragdolls during charge.
        /// </summary>
        private void CleanupChargeAudio(CrowdAgent agent)
        {
            // Use controller if available (preferred method)
            if (agent.chargeLoopAudioController != null)
            {
                agent.chargeLoopAudioController.StopImmediately();
                agent.chargeLoopAudioController = null;
                agent.chargeLoopAudioSource = null;
            }
            else if (agent.chargeLoopAudioSource != null)
            {
                // Fallback for old audio sources without controller
                AudioSource audioToStop = agent.chargeLoopAudioSource;
                GameObject objToStop = audioToStop.gameObject;
                
                audioToStop.Stop();
                audioToStop.loop = false;
                audioToStop.volume = 0f;
                audioToStop.clip = null;
                audioToStop.enabled = false;
                
                agent.chargeLoopAudioSource = null;

                var releaseAudioManager = CrowdAudioManager.Instance;
                if (releaseAudioManager != null)
                {
                    releaseAudioManager.ReleaseSound(audioToStop);
                }
                else if (objToStop != null)
                {
                    #if UNITY_EDITOR
                    Object.DestroyImmediate(objToStop);
                    #else
                    Object.Destroy(objToStop);
                    #endif
                }
            }
        }
        
        /// <summary>
        /// Periodically cleans up any orphaned ChargeLoopSound audio objects that may have been missed.
        /// With the ChargeLoopAudioController, orphaned audio should auto-cleanup via timeout,
        /// but this is a failsafe for any edge cases.
        /// </summary>
        private void CleanupOrphanedChargeAudio()
        {
            // Build a set of valid controller instances that are currently being tracked
            HashSet<int> validControllerInstanceIds = new HashSet<int>();
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (agent != null && agent.chargeLoopAudioController != null)
                {
                    validControllerInstanceIds.Add(agent.chargeLoopAudioController.GetInstanceID());
                }
            }
            
            // Find all ChargeLoopAudioController instances and destroy any not tracked by agents
            var allControllers = Object.FindObjectsByType<ChargeLoopAudioController>(FindObjectsSortMode.None);
            
            foreach (var controller in allControllers)
            {
                if (controller == null) continue;
                
                int instanceId = controller.GetInstanceID();
                if (!validControllerInstanceIds.Contains(instanceId))
                {
                    controller.StopImmediately();
                }
            }
            
            // Also check for any raw ChargeLoopSound objects without controllers (legacy)
            var allAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            
            foreach (var source in allAudioSources)
            {
                if (source == null || source.gameObject == null) continue;
                
                string objName = source.gameObject.name;
                if (!objName.StartsWith("ChargeLoopSound_Agent")) continue;
                
                // If it has a controller, skip (controller handles its own lifecycle)
                if (source.GetComponent<ChargeLoopAudioController>() != null) continue;
                
                // This is a legacy audio object without a controller - destroy it immediately
                source.Stop();
                source.loop = false;
                source.volume = 0f;
                source.clip = null;
                source.enabled = false;
                
                #if UNITY_EDITOR
                Object.DestroyImmediate(source.gameObject);
                #else
                Object.Destroy(source.gameObject);
                #endif
            }
        }

        private void HandleChargeLoopStopped(ChargeLoopAudioController controller)
        {
            if (controller == null) return;

            int agentIndex = controller.AgentIndex;
            if (agentIndex < 0 || agentIndex >= _agents.Count) return;

            var agent = _agents[agentIndex];
            if (agent != null && agent.chargeLoopAudioController == controller)
            {
                agent.chargeLoopAudioController = null;
                agent.chargeLoopAudioSource = null;
            }
        }
        
        /// <summary>
        /// Plays a one-shot charge sound effect.
        /// </summary>
        private void PlayChargeSound(CrowdAgent agent, AudioClip clip, float volume, float pitchVariation, float spatialBlend)
        {
            if (clip == null || agent == null) return;

            EnemyCrowd faction = GetEnemyCrowd(agent);
            Vector3 position = agent.WorldPosition;

            PlaySoundAtPosition(clip, position, volume, pitchVariation, spatialBlend, faction);
        }
        
        /// <summary>
        /// Starts a looping charge sound that follows the agent.
        /// </summary>
        private void StartChargeLoopSound(CrowdAgent agent, ChargeAttackConfig chargeConfig)
        {
            if (chargeConfig.chargeLoopSound == null || agent == null) return;

            EnemyCrowd faction = GetEnemyCrowd(agent);
            int maxConcurrent = faction != null ? faction.maxConcurrentSoundsPerClip : -1;
            float minDistance = faction != null ? faction.factionAudioMinDistance : 1f;
            float maxDistance = faction != null ? faction.factionAudioMaxDistance : 50f;
            
            // Clean up any existing audio source first (safety) - use the controller if available
            if (agent.chargeLoopAudioController != null)
            {
                agent.chargeLoopAudioController.StopImmediately();
                agent.chargeLoopAudioController = null;
                agent.chargeLoopAudioSource = null;
            }
            else if (agent.chargeLoopAudioSource != null)
            {
                AudioSource audioToStop = agent.chargeLoopAudioSource;
                audioToStop.Stop();
                audioToStop.loop = false;
                audioToStop.volume = 0f;
                audioToStop.clip = null;
                audioToStop.enabled = false;

                var releaseAudioManager = CrowdAudioManager.Instance;
                if (releaseAudioManager != null)
                {
                    releaseAudioManager.ReleaseSound(audioToStop);
                }
                else if (audioToStop.gameObject != null)
                {
                    #if UNITY_EDITOR
                    Object.DestroyImmediate(audioToStop.gameObject);
                    #else
                    Object.Destroy(audioToStop.gameObject);
                    #endif
                }
                agent.chargeLoopAudioSource = null;
            }
            
            // Calculate max duration based on charge distance and speed
            // This is the failsafe timeout - if audio plays longer than this, it auto-fades
            float baseSpeed = agent.navigationAgent != null ? agent.navigationAgent.Speed : 3.5f;
            float chargeSpeed = baseSpeed * chargeConfig.chargeSpeedMultiplier;
            chargeSpeed = chargeSpeed > 0 ? chargeSpeed : 10f; // Fallback
            float maxChargeTime = (chargeConfig.chargeMaxDistance / chargeSpeed) + 2f; // Add 2 second buffer
            maxChargeTime = Mathf.Min(maxChargeTime, chargeConfig.chargeMaxDuration + 1f); // Cap at max duration + 1

            var audioManager = CrowdAudioManager.Instance ?? CrowdAudioManager.EnsureInstance();
            if (audioManager == null) return;

            AudioSource source = audioManager.PlayLoopingSound(
                chargeConfig.chargeLoopSound,
                agent.WorldPosition,
                chargeConfig.chargeLoopVolume,
                chargeConfig.soundPitchVariation,
                chargeConfig.soundSpatialBlend,
                maxConcurrent,
                minDistance,
                maxDistance);

            if (source == null) return;

            var controller = source.GetComponent<ChargeLoopAudioController>();
            if (controller == null)
            {
                controller = source.gameObject.AddComponent<ChargeLoopAudioController>();
            }
            controller.Initialize(agent.index, maxChargeTime, 0.3f, audioManager.ReleaseSound, HandleChargeLoopStopped); // 0.3s fade out

            agent.chargeLoopAudioSource = source;
            agent.chargeLoopAudioController = controller;
        }
        
        /// <summary>
        /// Spawns the charge trail VFX that follows the agent.
        /// </summary>
        private void SpawnChargeTrailVFX(CrowdAgent agent, ChargeAttackConfig chargeConfig)
        {
            Transform agentTransform = agent?.navigationAgent?.Transform ?? agent?.crowdInstance?.transform;
            if (agentTransform == null) return;
            
            Vector3 position = agent.WorldPosition;
            Quaternion rotation = agentTransform.rotation;
            
            // Use the VFX spawning system from projectiles
            SpawnVFX(chargeConfig.chargeTrailVFX, position, rotation, agentTransform);
        }
        
        /// <summary>
        /// Spawns the charge impact VFX at the impact position.
        /// </summary>
        private void SpawnChargeImpactVFX(Vector3 impactPosition, ChargeAttackConfig chargeConfig)
        {
            float scale = chargeConfig.aoeRadius * chargeConfig.impactVFXScale;
            SpawnVFX(chargeConfig.chargeImpactVFX, impactPosition, Quaternion.identity, null, scale);
        }
        
        /// <summary>
        /// Helper method to spawn VFX using the VFXReference system.
        /// </summary>
        private void SpawnVFX(VFXReference vfxRef, Vector3 position, Quaternion rotation, Transform parent = null, float scale = 1f)
        {
            if (vfxRef.type == VFXType.None) return;
            
            switch (vfxRef.type)
            {
                case VFXType.ParticleSystem:
                    if (vfxRef.particleSystemPrefab != null)
                    {
                        var ps = Object.Instantiate(vfxRef.particleSystemPrefab, position, rotation, parent);
                        if (scale != 1f)
                            ps.transform.localScale = Vector3.one * scale;
                        Object.Destroy(ps.gameObject, vfxRef.defaultDuration);
                    }
                    break;
                    
                case VFXType.VFXGraph:
                    if (vfxRef.vfxGraphAsset != null)
                    {
                        // VFX Graph needs a GameObject with VisualEffect component
                        var vfxObj = new GameObject("ChargeVFX");
                        vfxObj.transform.position = position;
                        vfxObj.transform.rotation = rotation;
                        if (parent != null) vfxObj.transform.SetParent(parent);
                        if (scale != 1f) vfxObj.transform.localScale = Vector3.one * scale;
                        var vfx = vfxObj.AddComponent<UnityEngine.VFX.VisualEffect>();
                        vfx.visualEffectAsset = vfxRef.vfxGraphAsset;
                        vfx.Play();
                        Object.Destroy(vfxObj, vfxRef.defaultDuration);
                    }
                    break;
            }
        }
        
        #endregion
        
        #region Ally Defense System
        
        /// <summary>
        /// Alerts nearby allies when an agent is attacked. Alerted allies will engage the attacker.
        /// This creates realistic "rally to defense" behavior for melee units facing ranged attackers.
        /// </summary>
        /// <param name="victimAgent">The agent that was attacked</param>
        /// <param name="attackerAgentIndex">Index of the attacking agent (-1 if unknown, e.g., environmental damage)</param>
        /// <param name="attackerPosition">Position of the attacker for targeting</param>
        private void AlertNearbyAllies(CrowdAgent victimAgent, int attackerAgentIndex, Vector3? attackerPosition)
        {
            if (victimAgent == null || victimAgent.isDead)
            {
                return;
            }
            
            EnemyCrowd faction = victimAgent.enemyCrowd;
            if (faction == null)
            {
                return;
            }
            
            if (!faction.enableAllyDefense)
            {
                return;
            }
            
            float now = Time.time;
            
            // Check cooldown - don't spam alerts
            if (now - victimAgent.lastAllyAlertTime < faction.allyDefenseAlertCooldown)
            {
                return;
            }
            
            victimAgent.lastAllyAlertTime = now;
            
            // Get attacker agent if index is valid
            CrowdAgent attackerAgent = null;
            if (attackerAgentIndex >= 0 && attackerAgentIndex < _agents.Count)
            {
                attackerAgent = _agents[attackerAgentIndex];
            }
            

            
            // Get victim position
            Vector3 victimPos = GetAgentPosition(victimAgent);
            if (victimPos == Vector3.zero)
            {
                return;
            }
            
            float alertRadiusSqr = faction.allyDefenseRadius * faction.allyDefenseRadius;
            int maxResponders = faction.allyDefenseMaxResponders > 0 ? faction.allyDefenseMaxResponders : int.MaxValue;
            int respondersFound = 0;
            
            #if UNITY_EDITOR
            int alliesInFaction = 0;
            int alliesAlive = 0;
            int alliesInRange = 0;
            int alliesNotAggroed = 0;
            #endif
            
            // Find nearby allies to alert
            for (int i = 0; i < _agents.Count && respondersFound < maxResponders; i++)
            {
                CrowdAgent ally = _agents[i];
                if (ally == null || ally.isDead || ally.ragdollActivated)
                    continue;
                
                // Must be same faction
                if (ally.enemyCrowdIndex != victimAgent.enemyCrowdIndex)
                    continue;
                
                // HoldPosition agents with ignoreAllyDefense flag should not respond to ally alerts
                if (ally.combatStance == CombatStance.HoldPosition && ally.ignoreAllyDefenseWhileHolding)
                    continue;
                
                #if UNITY_EDITOR
                alliesInFaction++;
                #endif
                
                // Don't alert self
                if (ally.index == victimAgent.index)
                    continue;
                
                #if UNITY_EDITOR
                alliesAlive++;
                #endif
                
                // Check if ally can be redirected to help
                // Allow re-targeting if:
                // 1. Not aggroed at all, OR
                // 2. Aggroed but not currently in attack range (back-line units can help front line)
                // 3. Is currently charging (don't interrupt charges)
                bool canBeRedirected = false;
                string skipReason = "";
                
                // Don't interrupt charging agents
                if (ally.isCharging || ally.combatState == CrowdCombatState.Charging)
                {
                    skipReason = "Charging";
                    canBeRedirected = false;
                }
                else if (!ally.isAggroed || ally.attackTargetAgentIndex < 0)
                {
                    // Not aggroed - can definitely help
                    canBeRedirected = true;
                }
                else
                {
                    // Already aggroed - check if they're actually engaged in combat (within attack range)
                    // If they're far from their current target, they can switch to help the ally under attack
                    Vector3 allyPos = GetAgentPosition(ally);
                    float distToCurrentTarget = float.MaxValue;
                    
                    if (ally.attackTargetAgentIndex >= 0 && ally.attackTargetAgentIndex < _agents.Count)
                    {
                        var currentTarget = _agents[ally.attackTargetAgentIndex];
                        if (currentTarget != null && !currentTarget.isDead)
                        {
                            Vector3 targetPos = GetAgentPosition(currentTarget);
                            distToCurrentTarget = Vector3.Distance(allyPos, targetPos);
                        }
                    }
                    
                    // Get attack range (use melee range for melee units, ranged max distance for ranged)
                    float maxEngagementRange = faction.meleeMaxDistance > 0 ? faction.meleeMaxDistance * 1.5f : 5f;
                    if (faction.combatRangeType != CombatRangeType.Melee && faction.rangedMaxDistance > 0)
                    {
                        // Ranged units have larger engagement range
                        maxEngagementRange = Mathf.Max(maxEngagementRange, faction.rangedMaxDistance * 0.5f);
                    }
                    
                    // If not currently engaged (far from target), can be redirected
                    canBeRedirected = distToCurrentTarget > maxEngagementRange;
                    if (!canBeRedirected)
                    {
                        skipReason = $"Engaged (dist={distToCurrentTarget:F1}m, range={maxEngagementRange:F1}m)";
                    }
                }
                
                if (!canBeRedirected)
                    continue;
                
                #if UNITY_EDITOR
                alliesNotAggroed++;
                #endif
                
                // Check distance to victim (must be within alert radius)
                Vector3 allyPosition = GetAgentPosition(ally);
                float distSqr = (allyPosition - victimPos).sqrMagnitude;
                if (distSqr > alertRadiusSqr)
                    continue;
                
                #if UNITY_EDITOR
                alliesInRange++;
                #endif
                
                // Alert this ally!
                respondersFound++;
                ally.wasAlertedByAlly = true;
                ally.alertedByAgentIndex = victimAgent.index;
                ally.lastAllyAlertTime = now; // Track when this ally was alerted (for rush behavior duration)
                
                // Determine target
                if (faction.allyDefenseTargetAttacker && attackerAgent != null && !attackerAgent.isDead)
                {
                    // Target the attacker directly
                    ally.isAggroed = true;
                    ally.aggroAcquiredTime = now;
                    ally.lastTargetInRangeTime = now;
                    ally.isChasing = true;
                    ally.attackTargetAgentIndex = attackerAgentIndex;
                    ally.rushTargetAgentIndex = attackerAgentIndex; // Track for speed reset when target dies
                    ally.attackTargetTransform = attackerAgent.navigationAgent?.Transform ?? 
                                                  (attackerAgent.crowdInstance != null ? attackerAgent.crowdInstance.transform : null);
                    ally.hasAttackTargetPosition = true;
                    ally.attackTargetPosition = ally.attackTargetTransform != null ? ally.attackTargetTransform.position : 
                                               (attackerPosition ?? victimPos);
                    
                    // Transition to chasing state
                    if (ally.navigationState == AgentNavigationState.Formation)
                    {
                        ally.SetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                }
                else if (attackerPosition.HasValue)
                {
                    // Move toward attacker position (find own target when close)
                    ally.isAggroed = true;
                    ally.aggroAcquiredTime = now;
                    ally.isChasing = true;
                    ally.hasAttackTargetPosition = true;
                    ally.attackTargetPosition = attackerPosition.Value;
                    
                    if (ally.navigationState == AgentNavigationState.Formation)
                    {
                        ally.SetNavigationState(AgentNavigationState.ChasingTarget);
                    }
                }
                else
                {
                    // Just set as alerted - will find target through normal aggro system
                    ally.wasRecentlyAttacked = true;
                    ally.lastAttackedTime = now;
                }
            }
            

        }
        
        /// <summary>
        /// Gets the world position of an agent.
        /// </summary>
        private Vector3 GetAgentPosition(CrowdAgent agent)
        {
            if (agent == null) return Vector3.zero;
            if (agent.navigationAgent != null)
                return agent.WorldPosition;
            if (agent.crowdInstance != null)
                return agent.crowdInstance.transform.position;
            return Vector3.zero;
        }
        
        #endregion
        
        #region Debug Utilities
        
        /// <summary>
        /// Debug utility: Finds and logs all looping AudioSources in the scene.
        /// Call this from the Unity Inspector context menu when sound persists unexpectedly.
        /// </summary>
        [ContextMenu("Debug: Find All Looping Audio Sources")]
        private void DebugFindLoopingAudioSources()
        {
            #if UNITY_EDITOR
            var allAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            int loopingCount = 0;
            
            Debug.Log($"[ChargeAudio] === Scanning {allAudioSources.Length} AudioSources in scene ===");
            
            foreach (var source in allAudioSources)
            {
                if (source.isPlaying)
                {
                    string clipName = source.clip != null ? source.clip.name : "NULL";
                    string objPath = GetGameObjectPath(source.gameObject);
                    
                    if (source.loop)
                    {
                        loopingCount++;
                        Debug.LogWarning($"[ChargeAudio] LOOPING AUDIO: '{clipName}' on '{objPath}' (volume={source.volume:F2})");
                    }
                    else
                    {
                        Debug.Log($"[ChargeAudio] Playing (non-loop): '{clipName}' on '{objPath}'");
                    }
                }
            }
            
            Debug.Log($"[ChargeAudio] === Scan complete: {loopingCount} looping sources found ===");
            #endif
        }
        
        /// <summary>
        /// Debug utility: Force stops all charge-related audio in the scene.
        /// </summary>
        [ContextMenu("Debug: Force Stop All Charge Audio")]
        private void DebugForceStopAllChargeAudio()
        {
            #if UNITY_EDITOR
            var allAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            int stoppedCount = 0;
            
            foreach (var source in allAudioSources)
            {
                if (source.gameObject.name.Contains("ChargeLoopSound") || 
                    source.gameObject.name.Contains("ChargeSound"))
                {
                    source.Stop();
                    Object.Destroy(source.gameObject);
                    stoppedCount++;
                }
            }
            
            // Also clean up any agents with lingering charge audio refs
            if (_agents != null)
            {
                foreach (var agent in _agents)
                {
                    if (agent != null && agent.chargeLoopAudioSource != null)
                    {
                        agent.chargeLoopAudioSource.Stop();
                        if (agent.chargeLoopAudioSource.gameObject != null)
                        {
                            Object.Destroy(agent.chargeLoopAudioSource.gameObject);
                        }
                        agent.chargeLoopAudioSource = null;
                        stoppedCount++;
                    }
                }
            }
            
            Debug.Log($"[ChargeAudio] Force stopped {stoppedCount} charge audio sources");
            #endif
        }
        
        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
        
        #endregion
    }
}
