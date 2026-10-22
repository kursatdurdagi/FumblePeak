using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Attach this component to any GameObject to make it a valid target for EnemyCrowd agent attacks.
    /// The component defines a virtual hit area (cylinder) and fires events when agents attack within range.
    /// 
    /// Use this for:
    /// - Player characters (alternative to PlayerTarget in controller)
    /// - NPCs that should receive damage from crowd agents
    /// - Destructible objects
    /// - Any GameObject that needs to detect crowd agent attacks
    /// </summary>
    [AddComponentMenu("Enemy Masses/Crowd Hit Receiver")]
    public class CrowdHitReceiver : MonoBehaviour
    {
        [Header("Hit Area")]
        [Tooltip("Radius of the virtual hit cylinder (in meters). Attacks within this radius will register as hits.")]
        [SerializeField] private float hitRadius = 0.5f;
        
        [Tooltip("Height of the virtual hit cylinder (in meters). Measured from the pivot point upward.")]
        [SerializeField] private float hitHeight = 1.8f;
        
        [Tooltip("Vertical offset for the hit cylinder base (useful if pivot is not at feet).")]
        [SerializeField] private float heightOffset = 0f;

        [Header("Detection Settings")]
        [Tooltip("How often to check for attacking agents (in seconds). Lower = more responsive but higher CPU cost.")]
        [SerializeField] private float checkInterval = 0.05f;
        
        [Tooltip("Only detect hits from agents of these factions. Leave empty to detect all factions.")]
        [SerializeField] private EnemyCrowd[] factionFilter;
        
        [Tooltip("If true, only detect melee attacks.")]
        [SerializeField] private bool onlyMelee = false;
        
        [Tooltip("If true, only detect ranged attacks.")]
        [SerializeField] private bool onlyRanged = false;
        
        [Tooltip("Cooldown between hits from the same agent (prevents rapid multi-hits).")]
        [SerializeField] private float hitCooldown = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color gizmoColor = new Color(1f, 0.3f, 0.3f, 0.3f);

        // Events
        /// <summary>
        /// Fired when an EnemyCrowd agent hits this receiver.
        /// Parameters: (CrowdAgent attacker, float damage, Vector3 hitPoint, bool isMelee)
        /// </summary>
        public event Action<CrowdAgent, float, Vector3, bool> OnHit;
        
        /// <summary>
        /// Static event for global hit detection (useful for GC2 events).
        /// Parameters: (CrowdHitReceiver receiver, CrowdAgent attacker, float damage, Vector3 hitPoint, bool isMelee)
        /// </summary>
        public static event Action<CrowdHitReceiver, CrowdAgent, float, Vector3, bool> OnAnyHit;

        // Properties
        public float HitRadius => hitRadius;
        public float HitHeight => hitHeight;
        public float HeightOffset => heightOffset;
        
        /// <summary>The last agent that hit this receiver.</summary>
        public CrowdAgent LastAttacker { get; private set; }
        
        /// <summary>The damage from the last hit.</summary>
        public float LastDamage { get; private set; }
        
        /// <summary>The world position of the last hit.</summary>
        public Vector3 LastHitPoint { get; private set; }
        
        /// <summary>Whether the last hit was a melee attack.</summary>
        public bool LastHitWasMelee { get; private set; }

        // Internal state
        private EnemyMassesCrowdController _controller;
        private float _lastCheckTime;
        private readonly System.Collections.Generic.Dictionary<int, float> _agentHitCooldowns = 
            new System.Collections.Generic.Dictionary<int, float>();
        private readonly System.Collections.Generic.HashSet<int> _agentsInAttackState = 
            new System.Collections.Generic.HashSet<int>();

        private void Start()
        {
            _controller = FindFirstObjectByType<EnemyMassesCrowdController>();
            if (_controller == null)
            {
                Debug.LogWarning($"[CrowdHitReceiver] No EnemyMassesCrowdController found. Hit detection will not work.");
                enabled = false;
            }
        }

        private void Update()
        {
            if (_controller == null) return;
            
            float time = Time.time;
            if (time - _lastCheckTime < checkInterval) return;
            _lastCheckTime = time;
            
            CheckForHits(time);
        }

        private void CheckForHits(float currentTime)
        {
            Vector3 myPosition = transform.position;
            float radiusSqr = hitRadius * hitRadius;
            
            int agentCount = _controller.GetAgentCount();
            
            for (int i = 0; i < agentCount; i++)
            {
                CrowdAgent agent = _controller.GetAgent(i);
                if (agent == null || agent.isDead) continue;
                
                // Check if agent is in attacking state
                bool isAttacking = agent.combatState == CrowdCombatState.Attacking;
                bool wasAttacking = _agentsInAttackState.Contains(i);
                
                if (isAttacking && !wasAttacking)
                {
                    // Agent just started attacking - check if we're the target
                    _agentsInAttackState.Add(i);
                    
                    // Check cooldown
                    if (_agentHitCooldowns.TryGetValue(i, out float cooldownEnd) && currentTime < cooldownEnd)
                        continue;
                    
                    // Check faction filter
                    if (!PassesFactionFilter(agent)) continue;
                    
                    // Determine if melee or ranged
                    bool isMelee = IsMeleeAttack(agent);
                    
                    // Check melee/ranged filter
                    if (onlyMelee && !isMelee) continue;
                    if (onlyRanged && isMelee) continue;
                    
                    // Get agent position
                    Vector3 agentPosition = GetAgentPosition(agent);
                    
                    // Check if attack is targeting us (distance-based)
                    float attackRange = GetAttackRange(agent, isMelee);
                    
                    if (IsWithinHitArea(agentPosition, myPosition, attackRange, isMelee))
                    {
                        // Calculate damage
                        float damage = CalculateDamage(agent);
                        
                        // Calculate hit point (closest point on our hit cylinder to agent)
                        Vector3 hitPoint = GetHitPoint(agentPosition, myPosition);
                        
                        // Set cooldown
                        _agentHitCooldowns[i] = currentTime + hitCooldown;
                        
                        // Update last hit info
                        LastAttacker = agent;
                        LastDamage = damage;
                        LastHitPoint = hitPoint;
                        LastHitWasMelee = isMelee;
                        
                        // Fire events
                        OnHit?.Invoke(agent, damage, hitPoint, isMelee);
                        OnAnyHit?.Invoke(this, agent, damage, hitPoint, isMelee);
                    }
                }
                else if (!isAttacking && wasAttacking)
                {
                    // Agent stopped attacking
                    _agentsInAttackState.Remove(i);
                }
            }
            
            // Clean up expired cooldowns periodically
            if (currentTime % 5f < checkInterval)
            {
                CleanupCooldowns(currentTime);
            }
        }

        private bool PassesFactionFilter(CrowdAgent agent)
        {
            if (factionFilter == null || factionFilter.Length == 0) return true;
            
            foreach (var faction in factionFilter)
            {
                if (agent.enemyCrowd == faction) return true;
            }
            return false;
        }

        private bool IsMeleeAttack(CrowdAgent agent)
        {
            if (agent.enemyCrowd == null) return true;
            
            var rangeType = agent.enemyCrowd.combatRangeType;
            if (rangeType == CombatRangeType.Melee) return true;
            if (rangeType == CombatRangeType.Ranged) return false;
            
            // Hybrid - check active mode
            return agent.activeHybridMode == CombatRangeType.Melee;
        }

        private float GetAttackRange(CrowdAgent agent, bool isMelee)
        {
            if (agent.enemyCrowd == null) return 2f;
            
            if (isMelee)
            {
                return agent.enemyCrowd.meleeMaxDistance;
            }
            else
            {
                return agent.enemyCrowd.rangedMaxDistance;
            }
        }

        private bool IsWithinHitArea(Vector3 attackerPos, Vector3 targetPos, float attackRange, bool isMelee)
        {
            // Check horizontal distance (XZ plane)
            float dx = attackerPos.x - targetPos.x;
            float dz = attackerPos.z - targetPos.z;
            float horizontalDistSqr = dx * dx + dz * dz;
            
            // Combined range: attack range + our hit radius
            float effectiveRange = attackRange + hitRadius;
            float effectiveRangeSqr = effectiveRange * effectiveRange;
            
            if (horizontalDistSqr > effectiveRangeSqr) return false;
            
            // Check vertical - attacker should be roughly at our height level
            float heightBottom = targetPos.y + heightOffset;
            float heightTop = heightBottom + hitHeight;
            
            // Give some vertical tolerance for the attacker
            float attackerHeight = 1.8f; // Assume standard humanoid height
            float attackerBottom = attackerPos.y;
            float attackerTop = attackerPos.y + attackerHeight;
            
            // Check if there's vertical overlap
            return attackerBottom <= heightTop && attackerTop >= heightBottom;
        }

        private Vector3 GetAgentPosition(CrowdAgent agent)
        {
            if (agent.crowdInstance != null)
                return agent.crowdInstance.transform.position;
            if (agent.navigationAgent != null)
                return agent.WorldPosition;
            if (agent.transform != null)
                return agent.transform.position;
            return Vector3.zero;
        }

        private Vector3 GetHitPoint(Vector3 attackerPos, Vector3 targetPos)
        {
            // Calculate hit point on the surface of our hit cylinder
            Vector3 direction = (attackerPos - targetPos);
            direction.y = 0; // Flatten to horizontal
            
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.forward;
            
            direction.Normalize();
            
            // Point on cylinder surface facing attacker
            Vector3 hitPoint = targetPos + direction * hitRadius;
            
            // Adjust height to be roughly chest level
            hitPoint.y = targetPos.y + heightOffset + hitHeight * 0.6f;
            
            return hitPoint;
        }

        private float CalculateDamage(CrowdAgent agent)
        {
            // Get base damage from controller's combat settings
            float baseDamage = 10f; // Default fallback
            
            if (_controller != null)
            {
                // Try to get damage from combat settings via reflection or public API
                // For now, use a reasonable default that can be overridden
                baseDamage = 25f;
            }
            
            // Apply faction damage multiplier
            if (agent.enemyCrowd != null)
            {
                baseDamage *= agent.enemyCrowd.damageMultiplier;
            }
            
            return baseDamage;
        }

        private void CleanupCooldowns(float currentTime)
        {
            var expiredKeys = new System.Collections.Generic.List<int>();
            foreach (var kvp in _agentHitCooldowns)
            {
                if (currentTime >= kvp.Value)
                    expiredKeys.Add(kvp.Key);
            }
            foreach (var key in expiredKeys)
            {
                _agentHitCooldowns.Remove(key);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            
            Gizmos.color = gizmoColor;
            
            Vector3 basePos = transform.position + Vector3.up * heightOffset;
            Vector3 topPos = basePos + Vector3.up * hitHeight;
            Vector3 centerPos = basePos + Vector3.up * (hitHeight * 0.5f);
            
            // Draw cylinder using wire spheres and lines
            int segments = 16;
            Vector3[] bottomCircle = new Vector3[segments];
            Vector3[] topCircle = new Vector3[segments];
            
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * hitRadius;
                float z = Mathf.Sin(angle) * hitRadius;
                
                bottomCircle[i] = basePos + new Vector3(x, 0, z);
                topCircle[i] = topPos + new Vector3(x, 0, z);
            }
            
            // Draw circles
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                Gizmos.DrawLine(bottomCircle[i], bottomCircle[next]);
                Gizmos.DrawLine(topCircle[i], topCircle[next]);
            }
            
            // Draw vertical lines
            for (int i = 0; i < segments; i += 4)
            {
                Gizmos.DrawLine(bottomCircle[i], topCircle[i]);
            }
            
            // Draw center line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(basePos, topPos);
        }
    }
}
