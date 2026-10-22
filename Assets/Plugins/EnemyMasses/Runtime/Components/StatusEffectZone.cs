using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// A configurable status effect zone for Enemy Masses agents.
    /// Place this on a GameObject to apply any status effect to agents within the radius.
    /// 
    /// Examples:
    /// - Healing fountain (Regeneration)
    /// - Poison cloud (Poison)
    /// - Burning ground (Burn)
    /// - Slow field (Slow)
    /// - Buff shrine (ArmorBuff, AttackSpeedBuff, etc.)
    /// </summary>
    [AddComponentMenu("Enemy Masses/Status Effect Zone")]
    public class StatusEffectZone : MonoBehaviour
    {
        [Header("Zone Settings")]
        [Tooltip("Radius of the effect zone")]
        public float radius = 5f;
        
        [Tooltip("How often to check for and affect agents (in seconds)")]
        public float applyInterval = 1f;
        
        [Header("Status Effect")]
        [Tooltip("Type of status effect to apply")]
        public StatusEffectType effectType = StatusEffectType.Regeneration;
        
        [Tooltip("Duration of the status effect")]
        public float effectDuration = 5f;
        
        [Tooltip("Effect value (damage/healing per tick for DOT/HOT, multiplier for stat effects)")]
        public float effectValue = 5f;
        
        [Tooltip("Tick interval for DOT/HOT effects")]
        public float effectTickInterval = 1f;
        
        [Header("Filtering")]
        [Tooltip("Only affect agents from this faction index. Leave at -1 to affect all factions.")]
        public int targetFactionIndex = -1;
        
        [Header("Lifetime")]
        [Tooltip("Zone lifetime in seconds. 0 = infinite")]
        public float lifetime = 0f;
        
        [Header("Visual Debug")]
        public bool showGizmo = true;
        public Color gizmoColor = Color.cyan;
        
        private float _nextApplyTime;
        private float _spawnTime;
        private EnemyMassesCrowdController _controller;
        
        private void Start()
        {
            _controller = EnemyMassesCrowdController.Instance;
            _spawnTime = Time.time;
            
            if (_controller == null)
            {
                Debug.LogWarning($"[StatusEffectZone] No EnemyMassesCrowdController found. Disabling {gameObject.name}");
                enabled = false;
            }
        }
        
        private void Update()
        {
            if (_controller == null) return;
            
            // Check lifetime
            if (lifetime > 0f && Time.time - _spawnTime >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            
            // Apply effect at interval
            if (Time.time < _nextApplyTime) return;
            _nextApplyTime = Time.time + applyInterval;
            
            int? factionFilter = targetFactionIndex >= 0 ? targetFactionIndex : (int?)null;
            
            int affected = _controller.ApplyStatusEffectInRadius(
                transform.position,
                radius,
                effectType,
                effectDuration,
                effectValue,
                effectTickInterval,
                -1, // environment source
                factionFilter
            );
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!showGizmo) return;
            
            // Use different colors based on effect category
            Color color = gizmoColor;
            if (IsNegativeEffect(effectType))
                color = new Color(1f, 0.3f, 0.3f, 0.5f); // Red for negative
            else if (IsPositiveEffect(effectType))
                color = new Color(0.3f, 1f, 0.3f, 0.5f); // Green for positive
            else
                color = new Color(0.8f, 0.8f, 0.3f, 0.5f); // Yellow for CC
            
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, radius);
            
            Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.3f);
            Gizmos.DrawSphere(transform.position, radius);
        }
        
        private bool IsNegativeEffect(StatusEffectType type)
        {
            return type == StatusEffectType.Poison ||
                   type == StatusEffectType.Burn ||
                   type == StatusEffectType.Bleed ||
                   type == StatusEffectType.Curse ||
                   type == StatusEffectType.ArmorBreak ||
                   type == StatusEffectType.Weaken ||
                   type == StatusEffectType.Vulnerable;
        }
        
        private bool IsPositiveEffect(StatusEffectType type)
        {
            return type == StatusEffectType.Regeneration ||
                   type == StatusEffectType.ArmorBuff ||
                   type == StatusEffectType.HealthBuff ||
                   type == StatusEffectType.AttackSpeedBuff;
        }
    }
}
