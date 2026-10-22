using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Base class for all player skills in the Vampire Survivors-style combat system.
    /// Skills are passive and automatically activate based on their cooldown.
    /// </summary>
    public abstract class BaseSkill : MonoBehaviour
    {
        [Header("Skill Info")]
        [Tooltip("Display name of the skill")]
        [SerializeField] protected string skillName = "Skill";
        
        [Tooltip("Description of what the skill does")]
        [TextArea(2, 4)]
        [SerializeField] protected string description = "";
        
        [Tooltip("Icon for UI display")]
        [SerializeField] protected Sprite icon;
        
        [Header("Skill Level")]
        [Tooltip("Current level of the skill (1-based)")]
        [SerializeField] protected int currentLevel = 1;
        
        [Tooltip("Maximum level this skill can reach")]
        [SerializeField] protected int maxLevel = 8;
        
        [Header("Base Stats")]
        [Tooltip("Base damage dealt per hit")]
        [SerializeField] protected float baseDamage = 10f;
        
        [Tooltip("Damage increase per level")]
        [SerializeField] protected float damagePerLevel = 5f;
        
        [Tooltip("Base cooldown between skill activations")]
        [SerializeField] protected float baseCooldown = 2f;
        
        [Tooltip("Cooldown reduction per level (percentage)")]
        [SerializeField] [Range(0f, 0.5f)] protected float cooldownReductionPerLevel = 0.05f;
        
        [Header("Targeting")]
        [Tooltip("Maximum range of the skill")]
        [SerializeField] protected float range = 10f;
        
        [Tooltip("Range increase per level")]
        [SerializeField] protected float rangePerLevel = 0.5f;
        
        [Header("Visual")]
        [Tooltip("Prefab to spawn for skill visuals")]
        [SerializeField] protected GameObject visualPrefab;
        
        [Tooltip("Particle system effect on hit")]
        [SerializeField] protected ParticleSystem hitEffectPrefab;
        
        [Header("Audio")]
        [Tooltip("Sound played when skill activates")]
        [SerializeField] protected AudioClip activationSound;
        
        [Tooltip("Sound played when skill hits an enemy")]
        [SerializeField] protected AudioClip hitSound;
        
        [Tooltip("Volume of skill sounds")]
        [SerializeField] [Range(0f, 1f)] protected float soundVolume = 1f;
        
        [Tooltip("0 = 2D (no falloff), 1 = 3D (full spatial audio)")]
        [SerializeField] [Range(0f, 1f)] protected float spatialBlend = 0f;
        
        [Tooltip("Random pitch variation (+/-) for more natural sounding effects")]
        [SerializeField] [Range(0f, 0.5f)] protected float pitchVariation = 0.1f;
        
        [Header("Effect Limits")]
        [Tooltip("Max VFX per activation (-1 = use global setting, 0 = unlimited)")]
        [SerializeField] protected int maxVFXPerActivation = -1;
        
        [Tooltip("Max hit sounds per activation (-1 = use global setting, 0 = unlimited)")]
        [SerializeField] protected int maxHitSoundsPerActivation = -1;
        
        // Reference to skill manager
        protected PlayerSkillManager skillManager;
        protected Transform playerTransform;
        protected AudioSource audioSource;
        
        // Cooldown tracking
        protected float lastActivationTime = -999f;
        protected bool isActive = true;
        
        // Stats cache
        protected float currentDamage;
        protected float currentCooldown;
        protected float currentRange;
        
        // Effect counters (reset each activation)
        protected int vfxSpawnedThisActivation = 0;
        protected int hitSoundsPlayedThisActivation = 0;
        
        // Properties
        public string SkillName => skillName;
        public string Description => description;
        public Sprite Icon => icon;
        public int CurrentLevel => currentLevel;
        public int MaxLevel => maxLevel;
        public bool IsMaxLevel => currentLevel >= maxLevel;
        public float Damage => currentDamage;
        public float Cooldown => currentCooldown;
        public float Range => currentRange;
        public bool IsActive { get => isActive; set => isActive = value; }
        public float CooldownRemaining => Mathf.Max(0, currentCooldown - (Time.time - lastActivationTime));
        public float CooldownProgress => Mathf.Clamp01((Time.time - lastActivationTime) / currentCooldown);
        public bool IsReady => Time.time - lastActivationTime >= currentCooldown;
        
        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
            audioSource.spatialBlend = spatialBlend;
            
            RecalculateStats();
        }
        
        /// <summary>
        /// Initialize the skill with references
        /// </summary>
        public virtual void Initialize(PlayerSkillManager manager, Transform player)
        {
            skillManager = manager;
            playerTransform = player;
            RecalculateStats();
        }
        
        /// <summary>
        /// Called every frame to update the skill
        /// </summary>
        public virtual void UpdateSkill()
        {
            if (!isActive) return;
            
            if (IsReady)
            {
                Activate();
            }
            
            UpdateSkillLogic();
        }
        
        /// <summary>
        /// Override to implement custom update logic (for continuous skills)
        /// </summary>
        protected virtual void UpdateSkillLogic() { }
        
        /// <summary>
        /// Activate the skill
        /// </summary>
        public virtual void Activate()
        {
            lastActivationTime = Time.time;
            ResetEffectCounters(); // Reset VFX/sound counters for this activation
            PlayActivationSound();
            OnActivate();
        }
        
        /// <summary>
        /// Override to implement skill activation logic
        /// </summary>
        protected abstract void OnActivate();
        
        /// <summary>
        /// Level up the skill
        /// </summary>
        public virtual void LevelUp()
        {
            if (currentLevel >= maxLevel) return;
            
            currentLevel++;
            RecalculateStats();
            OnLevelUp();
        }
        
        /// <summary>
        /// Set skill to specific level
        /// </summary>
        public virtual void SetLevel(int level)
        {
            currentLevel = Mathf.Clamp(level, 1, maxLevel);
            RecalculateStats();
        }
        
        /// <summary>
        /// Override for custom level up effects
        /// </summary>
        protected virtual void OnLevelUp() { }
        
        /// <summary>
        /// Recalculate all stats based on current level
        /// </summary>
        public virtual void RecalculateStats()
        {
            currentDamage = baseDamage + (damagePerLevel * (currentLevel - 1));
            currentCooldown = baseCooldown * (1f - (cooldownReductionPerLevel * (currentLevel - 1)));
            currentCooldown = Mathf.Max(0.1f, currentCooldown); // Minimum cooldown
            currentRange = range + (rangePerLevel * (currentLevel - 1));
        }
        
        /// <summary>
        /// Apply damage to an EnemyCrowd agent by index (may affect multiple agents across crowds)
        /// </summary>
        protected void DealDamageToAgent(int agentIndex, float damage)
        {
            if (skillManager == null) return;
            skillManager.DealDamageToAgent(agentIndex, damage, this);
        }
        
        /// <summary>
        /// Apply damage to an EnemyCrowd agent using AgentReference (recommended for multi-crowd support)
        /// </summary>
        protected void DealDamageToAgent(AgentReference agent, float damage)
        {
            if (skillManager == null || !agent.IsValid) return;
            skillManager.DealDamageToAgent(agent, damage, this);
        }
        
        /// <summary>
        /// Apply damage to an EnemyCrowd agent on a specific controller
        /// </summary>
        protected void DealDamageToAgent(EnemyMassesCrowdController controller, int agentIndex, float damage)
        {
            if (skillManager == null || controller == null) return;
            skillManager.DealDamageToAgent(controller, agentIndex, damage, this);
        }
        
        /// <summary>
        /// Get all enemy agents within range of a position (returns indices only)
        /// </summary>
        protected List<int> GetAgentsInRange(Vector3 position, float checkRange)
        {
            if (skillManager == null) return new List<int>();
            return skillManager.GetAgentsInRange(position, checkRange);
        }
        
        /// <summary>
        /// Get all enemy agents within range as AgentReferences (recommended for multi-crowd support)
        /// </summary>
        protected List<AgentReference> GetAgentReferencesInRange(Vector3 position, float checkRange)
        {
            if (skillManager == null) return new List<AgentReference>();
            return skillManager.GetAgentReferencesInRange(position, checkRange);
        }
        
        /// <summary>
        /// Get the closest enemy agent to a position (returns index only)
        /// </summary>
        protected int GetClosestAgent(Vector3 position, float maxRange)
        {
            if (skillManager == null) return -1;
            return skillManager.GetClosestAgent(position, maxRange);
        }
        
        /// <summary>
        /// Get the closest enemy agent as AgentReference (recommended for multi-crowd support)
        /// </summary>
        protected AgentReference GetClosestAgentReference(Vector3 position, float maxRange)
        {
            if (skillManager == null) return default;
            return skillManager.GetClosestAgentReference(position, maxRange);
        }
        
        #region Knockback / Impulse
        
        /// <summary>
        /// Apply knockback to an agent using fake physics (NavMesh offset system).
        /// This pushes agents without requiring rigidbodies - they stay on the NavMesh while visually moving.
        /// </summary>
        /// <param name="agentIndex">Agent index</param>
        /// <param name="direction">Direction of the knockback (will be normalized)</param>
        /// <param name="pushForce">Horizontal push strength (m/s)</param>
        /// <param name="liftForce">Vertical lift strength (m/s), default 0 for ground-only push</param>
        /// <param name="duration">How long the impulse runs (default 0.5s)</param>
        /// <param name="gravity">Downward gravity during impulse (default 25)</param>
        /// <param name="drag">Horizontal damping (default 4)</param>
        protected void ApplyKnockback(int agentIndex, Vector3 direction, float pushForce, float liftForce = 0f,
            float duration = 0.5f, float gravity = 25f, float drag = 4f)
        {
            if (skillManager == null) return;
            skillManager.ApplyKnockback(agentIndex, direction, pushForce, liftForce, duration, gravity, drag);
        }
        
        /// <summary>
        /// Apply knockback to an agent using AgentReference.
        /// </summary>
        protected void ApplyKnockback(AgentReference agent, Vector3 direction, float pushForce, float liftForce = 0f,
            float duration = 0.5f, float gravity = 25f, float drag = 4f)
        {
            if (skillManager == null || !agent.IsValid) return;
            skillManager.ApplyKnockback(agent, direction, pushForce, liftForce, duration, gravity, drag);
        }
        
        /// <summary>
        /// Apply knockback to all agents in a radius, pushing them away from a center point.
        /// </summary>
        /// <param name="center">Center point (agents are pushed away from here)</param>
        /// <param name="radius">Radius to affect</param>
        /// <param name="pushForce">Horizontal push strength (m/s)</param>
        /// <param name="liftForce">Vertical lift strength (m/s)</param>
        /// <param name="duration">How long the impulse runs</param>
        /// <param name="gravity">Downward gravity during impulse</param>
        /// <param name="drag">Horizontal damping</param>
        /// <returns>Number of agents affected</returns>
        protected int ApplyKnockbackInRadius(Vector3 center, float radius, float pushForce, float liftForce = 0f,
            float duration = 0.5f, float gravity = 25f, float drag = 4f)
        {
            if (skillManager == null) return 0;
            return skillManager.ApplyKnockbackInRadius(center, radius, pushForce, liftForce, duration, gravity, drag);
        }
        
        #endregion
        
        /// <summary>
        /// Reset effect counters. Call this at the start of each skill activation.
        /// </summary>
        protected void ResetEffectCounters()
        {
            vfxSpawnedThisActivation = 0;
            hitSoundsPlayedThisActivation = 0;
        }
        
        /// <summary>
        /// Get the effective max VFX limit (skill-specific or global)
        /// </summary>
        protected int GetMaxVFXLimit()
        {
            if (maxVFXPerActivation >= 0) return maxVFXPerActivation;
            return skillManager != null ? skillManager.GlobalMaxVFXPerActivation : 20;
        }
        
        /// <summary>
        /// Get the effective max hit sound limit (skill-specific or global)
        /// </summary>
        protected int GetMaxHitSoundLimit()
        {
            if (maxHitSoundsPerActivation >= 0) return maxHitSoundsPerActivation;
            return skillManager != null ? skillManager.GlobalMaxHitSoundsPerActivation : 5;
        }
        
        /// <summary>
        /// Check if we can spawn more VFX this activation
        /// </summary>
        protected bool CanSpawnVFX()
        {
            int limit = GetMaxVFXLimit();
            return limit == 0 || vfxSpawnedThisActivation < limit;
        }
        
        /// <summary>
        /// Check if we can play more hit sounds this activation
        /// </summary>
        protected bool CanPlayHitSound()
        {
            int limit = GetMaxHitSoundLimit();
            return limit == 0 || hitSoundsPlayedThisActivation < limit;
        }
        
        /// <summary>
        /// Spawn hit effect at position (respects effect limits)
        /// </summary>
        protected void SpawnHitEffect(Vector3 position)
        {
            if (hitEffectPrefab != null && CanSpawnVFX())
            {
                var effect = Instantiate(hitEffectPrefab, position, Quaternion.identity);
                effect.Play();
                Destroy(effect.gameObject, effect.main.duration + effect.main.startLifetime.constantMax);
                vfxSpawnedThisActivation++;
            }
            
            PlayHitSound();
        }
        
        /// <summary>
        /// Spawn a custom effect at position (respects effect limits)
        /// Use this for skill-specific effects like chainHitEffectPrefab
        /// </summary>
        protected ParticleSystem SpawnEffect(ParticleSystem prefab, Vector3 position, bool autoDestroy = true)
        {
            if (prefab == null || !CanSpawnVFX()) return null;
            
            var effect = Instantiate(prefab, position, Quaternion.identity);
            effect.Play();
            vfxSpawnedThisActivation++;
            
            if (autoDestroy)
            {
                Destroy(effect.gameObject, effect.main.duration + effect.main.startLifetime.constantMax);
            }
            
            return effect;
        }
        
        protected void PlayActivationSound()
        {
            if (activationSound != null && audioSource != null)
            {
                ApplyPitchVariation();
                audioSource.PlayOneShot(activationSound, soundVolume);
            }
        }
        
        protected void PlayHitSound()
        {
            if (hitSound != null && audioSource != null && CanPlayHitSound())
            {
                ApplyPitchVariation();
                audioSource.PlayOneShot(hitSound, soundVolume * 0.7f);
                hitSoundsPlayedThisActivation++;
            }
        }
        
        /// <summary>
        /// Apply random pitch variation to the audio source
        /// </summary>
        private void ApplyPitchVariation()
        {
            if (pitchVariation > 0f && audioSource != null)
            {
                audioSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            }
            else if (audioSource != null)
            {
                audioSource.pitch = 1f;
            }
        }
        
        /// <summary>
        /// Get stats description for UI
        /// </summary>
        public virtual string GetStatsDescription()
        {
            return $"Damage: {currentDamage:F0}\nCooldown: {currentCooldown:F1}s\nRange: {currentRange:F1}";
        }
        
        /// <summary>
        /// Get next level preview for UI
        /// </summary>
        public virtual string GetNextLevelDescription()
        {
            if (IsMaxLevel) return "MAX LEVEL";
            
            float nextDamage = baseDamage + (damagePerLevel * currentLevel);
            float nextCooldown = baseCooldown * (1f - (cooldownReductionPerLevel * currentLevel));
            float nextRange = range + (rangePerLevel * currentLevel);
            
            return $"Damage: {nextDamage:F0} (+{damagePerLevel:F0})\nCooldown: {nextCooldown:F1}s\nRange: {nextRange:F1}";
        }
    }
}
