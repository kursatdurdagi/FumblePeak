using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that passively buffs or affects the player and nearby enemies.
    /// Examples: Attractorb, Luck boost, Movement speed aura, Damage reduction
    /// </summary>
    public class PassiveAuraSkill : BaseSkill
    {
        [Header("Aura Settings")]
        [Tooltip("Radius of the aura effect")]
        [SerializeField] private float auraRadius = 5f;
        
        [Tooltip("Radius increase per level")]
        [SerializeField] private float radiusPerLevel = 0.5f;
        
        [Tooltip("Type of aura effect")]
        [SerializeField] private AuraType auraType = AuraType.DamageZone;
        
        [Header("Damage Zone Settings (for DamageZone type)")]
        [Tooltip("Damage tick rate")]
        [SerializeField] private float damageTickRate = 0.5f;
        
        [Header("Slow Zone Settings (for SlowZone type)")]
        [Tooltip("Slow percentage (0-1)")]
        [SerializeField] [Range(0f, 0.9f)] private float slowPercentage = 0.3f;
        
        [Tooltip("Slow increase per level")]
        [SerializeField] [Range(0f, 0.1f)] private float slowPerLevel = 0.05f;
        
        [Header("Pickup Magnet Settings (for PickupMagnet type)")]
        [Tooltip("Pull speed for items")]
        [SerializeField] private float pullSpeed = 10f;
        
        [Tooltip("Layer mask for pickup items")]
        [SerializeField] private LayerMask pickupLayerMask = -1;
        
        [Header("Fear Zone Settings (for FearZone type)")]
        [Tooltip("Enemies flee from player")]
        [SerializeField] private float fearDuration = 2f;
        
        [Header("Visual")]
        [Tooltip("Visual indicator for aura (circle on ground, particles, etc.)")]
        [SerializeField] private GameObject auraVisualPrefab;
        
        [Tooltip("Should visual scale with radius")]
        [SerializeField] private bool scaleVisualToRadius = true;
        
        [Tooltip("Pulsing effect for visual")]
        [SerializeField] private bool enablePulse = false;
        
        [Tooltip("Pulse speed")]
        [SerializeField] private float pulseSpeed = 2f;
        
        [Tooltip("Pulse amplitude")]
        [SerializeField] private float pulseAmplitude = 0.1f;
        
        public enum AuraType
        {
            DamageZone,      // Deals damage to enemies in range
            SlowZone,        // Slows enemies in range
            PickupMagnet,    // Pulls pickups toward player
            FearZone,        // Makes enemies flee
            BuffZone         // Buffs friendly units (future use)
        }
        
        // Current stats
        private float currentAuraRadius;
        private float currentSlowPercentage;
        
        // Visual
        private GameObject auraVisual;
        private float lastDamageTick;
        
        // Tracking
        private HashSet<int> affectedAgents = new HashSet<int>();
        
        protected override void Awake()
        {
            base.Awake();
            baseCooldown = 0f; // Aura is always active
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentAuraRadius = auraRadius + (radiusPerLevel * (currentLevel - 1));
            currentSlowPercentage = Mathf.Min(0.9f, slowPercentage + (slowPerLevel * (currentLevel - 1)));
            
            UpdateAuraVisualScale();
        }
        
        protected override void OnActivate()
        {
            SpawnAuraVisual();
        }
        
        protected override void UpdateSkillLogic()
        {
            if (playerTransform == null) return;
            
            UpdateAuraPosition();
            UpdateAuraPulse();
            
            switch (auraType)
            {
                case AuraType.DamageZone:
                    UpdateDamageZone();
                    break;
                case AuraType.SlowZone:
                    UpdateSlowZone();
                    break;
                case AuraType.PickupMagnet:
                    UpdatePickupMagnet();
                    break;
                case AuraType.FearZone:
                    UpdateFearZone();
                    break;
            }
        }
        
        private void SpawnAuraVisual()
        {
            if (auraVisualPrefab != null && auraVisual == null)
            {
                auraVisual = Instantiate(auraVisualPrefab, playerTransform.position, Quaternion.identity, transform);
                UpdateAuraVisualScale();
            }
        }
        
        private void UpdateAuraPosition()
        {
            if (auraVisual != null)
            {
                auraVisual.transform.position = playerTransform.position;
            }
        }
        
        private void UpdateAuraVisualScale()
        {
            if (auraVisual != null && scaleVisualToRadius)
            {
                float scale = currentAuraRadius * 2f;
                auraVisual.transform.localScale = new Vector3(scale, auraVisual.transform.localScale.y, scale);
            }
        }
        
        private void UpdateAuraPulse()
        {
            if (!enablePulse || auraVisual == null) return;
            
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude;
            float scale = currentAuraRadius * 2f * pulse;
            auraVisual.transform.localScale = new Vector3(scale, auraVisual.transform.localScale.y, scale);
        }
        
        private void UpdateDamageZone()
        {
            if (Time.time - lastDamageTick < damageTickRate) return;
            lastDamageTick = Time.time;
            
            var agents = GetAgentsInRange(playerTransform.position, currentAuraRadius);
            
            foreach (int agentIndex in agents)
            {
                DealDamageToAgent(agentIndex, currentDamage);
            }
        }
        
        private void UpdateSlowZone()
        {
            // Track which agents are in the zone
            var currentInRange = new HashSet<int>(GetAgentsInRange(playerTransform.position, currentAuraRadius));
            
            // Note: Actual slowing would need to be implemented in the EnemyCrowd system
            // This tracks which agents should be slowed
            
            // Find agents that left the zone
            var leftZone = new List<int>();
            foreach (int agentIndex in affectedAgents)
            {
                if (!currentInRange.Contains(agentIndex))
                {
                    leftZone.Add(agentIndex);
                }
            }
            
            // Remove slow from agents that left
            foreach (int agentIndex in leftZone)
            {
                affectedAgents.Remove(agentIndex);
                // crowdController.RemoveSpeedModifier(agentIndex, "SlowAura");
            }
            
            // Add slow to new agents
            foreach (int agentIndex in currentInRange)
            {
                if (!affectedAgents.Contains(agentIndex))
                {
                    affectedAgents.Add(agentIndex);
                    // crowdController.ApplySpeedModifier(agentIndex, "SlowAura", 1f - currentSlowPercentage);
                }
            }
        }
        
        private void UpdatePickupMagnet()
        {
            Vector3 playerPos = playerTransform.position;
            
            // Find all pickups in range
            Collider[] pickups = Physics.OverlapSphere(playerPos, currentAuraRadius, pickupLayerMask);
            
            foreach (var pickup in pickups)
            {
                // Move pickup toward player
                Vector3 direction = (playerPos - pickup.transform.position).normalized;
                pickup.transform.position += direction * pullSpeed * Time.deltaTime;
                
                // Check if close enough to collect
                if (Vector3.Distance(pickup.transform.position, playerPos) < 0.5f)
                {
                    // Trigger pickup collection
                    // The pickup itself should handle its collection logic
                }
            }
        }
        
        private void UpdateFearZone()
        {
            var agents = GetAgentsInRange(playerTransform.position, currentAuraRadius);
            
            foreach (int agentIndex in agents)
            {
                // Note: Fear would need to be implemented in the EnemyCrowd system
                // This would make the agent flee from the player
                // crowdController.ApplyFear(agentIndex, playerTransform.position, fearDuration);
            }
        }
        
        public override string GetStatsDescription()
        {
            string desc = $"Radius: {currentAuraRadius:F1}m\n";
            
            switch (auraType)
            {
                case AuraType.DamageZone:
                    desc += $"Damage: {currentDamage:F0}/tick";
                    break;
                case AuraType.SlowZone:
                    desc += $"Slow: {currentSlowPercentage * 100:F0}%";
                    break;
                case AuraType.PickupMagnet:
                    desc += $"Pull Speed: {pullSpeed:F0}";
                    break;
                case AuraType.FearZone:
                    desc += $"Fear: {fearDuration:F1}s";
                    break;
            }
            
            return desc;
        }
        
        private void OnDestroy()
        {
            if (auraVisual != null)
            {
                Destroy(auraVisual);
            }
            
            // Clean up any lingering effects on agents
            affectedAgents.Clear();
        }
        
        private void OnDisable()
        {
            if (auraVisual != null)
            {
                auraVisual.SetActive(false);
            }
            
            // Remove effects from all affected agents
            // foreach (int agentIndex in affectedAgents)
            // {
            //     crowdController.RemoveSpeedModifier(agentIndex, "SlowAura");
            // }
        }
        
        private void OnEnable()
        {
            if (auraVisual != null)
            {
                auraVisual.SetActive(true);
            }
        }
    }
}
