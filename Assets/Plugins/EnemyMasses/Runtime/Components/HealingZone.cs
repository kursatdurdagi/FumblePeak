using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Example component that creates a healing zone for Enemy Masses agents.
    /// Place this on a GameObject to heal all agents within the specified radius.
    /// 
    /// This demonstrates how third-party developers can use the public API to create
    /// custom gameplay mechanics like healing fountains, buff zones, poison clouds, etc.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Healing Zone")]
    public class HealingZone : MonoBehaviour
    {
        [Header("Zone Settings")]
        [Tooltip("Radius of the healing zone")]
        public float radius = 5f;
        
        [Tooltip("How often to apply healing (in seconds)")]
        public float tickInterval = 1f;
        
        [Header("Healing Settings")]
        [Tooltip("Health restored per tick")]
        public float healPerTick = 5f;
        
        [Tooltip("Use regeneration status effect instead of instant heal")]
        public bool useRegenerationEffect = false;
        
        [Tooltip("Duration of regeneration effect when using status effect mode")]
        public float regenerationDuration = 5f;
        
        [Header("Filtering")]
        [Tooltip("Only heal agents from this faction index. Leave at -1 to heal all factions.")]
        public int targetFactionIndex = -1;
        
        [Header("Visual Debug")]
        [Tooltip("Show the healing zone radius in Scene view")]
        public bool showGizmo = true;
        public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.3f);
        
        private float _nextTickTime;
        private EnemyMassesCrowdController _controller;
        
        private void Start()
        {
            _controller = EnemyMassesCrowdController.Instance;
            if (_controller == null)
            {
                Debug.LogWarning($"[HealingZone] No EnemyMassesCrowdController found in scene. Disabling {gameObject.name}");
                enabled = false;
            }
        }
        
        private void Update()
        {
            if (_controller == null) return;
            if (Time.time < _nextTickTime) return;
            
            _nextTickTime = Time.time + tickInterval;
            
            int? factionFilter = targetFactionIndex >= 0 ? targetFactionIndex : (int?)null;
            
            if (useRegenerationEffect)
            {
                // Apply regeneration status effect - heals over time
                // Agents already with regen won't get stacking effects (depending on your StatusEffect settings)
                int affected = _controller.ApplyStatusEffectInRadius(
                    transform.position, 
                    radius, 
                    StatusEffectType.Regeneration, 
                    regenerationDuration, 
                    healPerTick,
                    tickInterval,
                    -1, // sourceAgentIndex: -1 = environment/zone
                    factionFilter
                );
                
                if (affected > 0)
                {
                    Debug.Log($"[HealingZone] Applied regeneration to {affected} agents");
                }
            }
            else
            {
                // Instant heal - simpler but requires agents to stay in zone
                int healed = _controller.HealAgentsInRadius(
                    transform.position, 
                    radius, 
                    healPerTick,
                    factionFilter
                );
                
                if (healed > 0)
                {
                    Debug.Log($"[HealingZone] Healed {healed} agents for {healPerTick} HP");
                }
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!showGizmo) return;
            
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, radius);
            
            // Draw filled sphere with transparency
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, gizmoColor.a * 0.3f);
            Gizmos.DrawSphere(transform.position, radius);
        }
    }
}
