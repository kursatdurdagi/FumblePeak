using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that creates an expanding nova/shockwave from the player.
    /// Examples: King Bible evolved, Santa Water, Explosion effect
    /// </summary>
    public class NovaSkill : BaseSkill
    {
        [Header("Nova Settings")]
        [Tooltip("Maximum radius the nova expands to")]
        [SerializeField] private float maxRadius = 8f;
        
        [Tooltip("Radius increase per level")]
        [SerializeField] private float radiusPerLevel = 1f;
        
        [Tooltip("Starting radius of the nova")]
        [SerializeField] private float startRadius = 0.5f;
        
        [Tooltip("Expansion speed")]
        [SerializeField] private float expansionSpeed = 15f;
        
        [Tooltip("Expansion speed increase per level")]
        [SerializeField] private float expansionSpeedPerLevel = 2f;
        
        [Tooltip("Number of nova waves per activation")]
        [SerializeField] private int waveCount = 1;
        
        [Tooltip("Additional waves per level")]
        [SerializeField] private int wavesPerLevel = 0;
        
        [Tooltip("Delay between waves")]
        [SerializeField] private float waveDelay = 0.3f;
        
        [Header("Damage")]
        [Tooltip("Width of the damage ring")]
        [SerializeField] private float damageRingWidth = 1f;
        
        [Tooltip("Damage ring width increase per level")]
        [SerializeField] private float ringWidthPerLevel = 0.1f;
        
        [Tooltip("Enemies can only be hit once per nova")]
        [SerializeField] private bool hitOncePerNova = true;
        
        [Header("Visual")]
        [Tooltip("Visual prefab for the expanding ring")]
        [SerializeField] private GameObject ringVisualPrefab;
        
        [Tooltip("Should ring scale with expansion")]
        [SerializeField] private bool scaleRingVisual = true;
        
        [Tooltip("Particle effect at nova origin")]
        [SerializeField] private ParticleSystem originEffectPrefab;
        
        [Header("Knockback")]
        [Tooltip("Push enemies outward")]
        [SerializeField] private bool applyKnockback = true;
        
        [Tooltip("Horizontal knockback force (m/s)")]
        [SerializeField] private float knockbackForce = 5f;
        
        [Tooltip("Vertical lift force (m/s) - adds upward push")]
        [SerializeField] private float knockbackLiftForce = 1f;
        
        // Current stats
        private float currentMaxRadius;
        private float currentExpansionSpeed;
        private int currentWaveCount;
        private float currentRingWidth;
        
        // Active novas
        private List<ActiveNova> activeNovas = new List<ActiveNova>();
        private int wavesRemaining;
        private float lastWaveTime;
        
        private class ActiveNova
        {
            public float currentRadius;
            public Vector3 origin;
            public GameObject visual;
            public HashSet<int> hitAgents = new HashSet<int>();
            public float damage;
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentMaxRadius = maxRadius + (radiusPerLevel * (currentLevel - 1));
            currentExpansionSpeed = expansionSpeed + (expansionSpeedPerLevel * (currentLevel - 1));
            currentWaveCount = waveCount + (wavesPerLevel * (currentLevel - 1));
            currentRingWidth = damageRingWidth + (ringWidthPerLevel * (currentLevel - 1));
        }
        
        protected override void OnActivate()
        {
            wavesRemaining = currentWaveCount;
            lastWaveTime = Time.time - waveDelay; // Allow immediate first wave
            
            // Spawn origin effect
            if (originEffectPrefab != null)
            {
                var effect = Instantiate(originEffectPrefab, playerTransform.position, Quaternion.identity);
                effect.Play();
                Destroy(effect.gameObject, effect.main.duration + 1f);
            }
        }
        
        protected override void UpdateSkillLogic()
        {
            // Spawn waves
            if (wavesRemaining > 0 && Time.time - lastWaveTime >= waveDelay)
            {
                SpawnNova();
                wavesRemaining--;
                lastWaveTime = Time.time;
            }
            
            // Update active novas
            UpdateNovas();
        }
        
        private void SpawnNova()
        {
            var nova = new ActiveNova
            {
                currentRadius = startRadius,
                origin = playerTransform.position,
                damage = currentDamage
            };
            
            if (ringVisualPrefab != null)
            {
                nova.visual = Instantiate(ringVisualPrefab, nova.origin, Quaternion.identity, transform);
                UpdateNovaVisual(nova);
            }
            
            activeNovas.Add(nova);
        }
        
        private void UpdateNovas()
        {
            for (int i = activeNovas.Count - 1; i >= 0; i--)
            {
                var nova = activeNovas[i];
                
                // Expand nova
                nova.currentRadius += currentExpansionSpeed * Time.deltaTime;
                
                // Check for hits in the ring
                CheckNovaHits(nova);
                
                // Update visual
                UpdateNovaVisual(nova);
                
                // Check if nova is complete
                if (nova.currentRadius >= currentMaxRadius)
                {
                    if (nova.visual != null)
                    {
                        Destroy(nova.visual);
                    }
                    activeNovas.RemoveAt(i);
                }
            }
        }
        
        private void CheckNovaHits(ActiveNova nova)
        {
            // Get agents in the damage ring (outer radius - inner radius)
            float outerRadius = nova.currentRadius + currentRingWidth / 2f;
            float innerRadius = Mathf.Max(0, nova.currentRadius - currentRingWidth / 2f);
            
            var agentsInOuter = GetAgentsInRange(nova.origin, outerRadius);
            
            foreach (int agentIndex in agentsInOuter)
            {
                // Check if already hit this nova
                if (hitOncePerNova && nova.hitAgents.Contains(agentIndex)) continue;
                
                // Check if within ring (not inside inner radius)
                Vector3 agentPos = skillManager.GetAgentPosition(agentIndex);
                float distance = Vector3.Distance(nova.origin, agentPos);
                
                if (distance >= innerRadius && distance <= outerRadius)
                {
                    DealDamageToAgent(agentIndex, nova.damage);
                    nova.hitAgents.Add(agentIndex);
                    SpawnHitEffect(agentPos);
                    
                    // Apply knockback (push away from nova center)
                    if (applyKnockback && knockbackForce > 0f)
                    {
                        Vector3 knockbackDir = (agentPos - nova.origin).normalized;
                        ApplyKnockback(agentIndex, knockbackDir, knockbackForce, knockbackLiftForce);
                    }
                }
            }
        }
        
        private void UpdateNovaVisual(ActiveNova nova)
        {
            if (nova.visual == null) return;
            
            if (scaleRingVisual)
            {
                float scale = nova.currentRadius * 2f; // Diameter
                nova.visual.transform.localScale = new Vector3(scale, 1f, scale);
            }
            
            // Fade out as it expands
            var renderer = nova.visual.GetComponent<Renderer>();
            if (renderer != null && renderer.material.HasProperty("_Color"))
            {
                float alpha = 1f - (nova.currentRadius / currentMaxRadius);
                Color color = renderer.material.color;
                color.a = alpha;
                renderer.material.color = color;
            }
        }
        
        public override string GetStatsDescription()
        {
            return $"Radius: {currentMaxRadius:F1}m\n" +
                   $"Damage: {currentDamage:F0}\n" +
                   $"Waves: {currentWaveCount}\n" +
                   $"Speed: {currentExpansionSpeed:F0}";
        }
        
        private void OnDestroy()
        {
            foreach (var nova in activeNovas)
            {
                if (nova.visual != null)
                {
                    Destroy(nova.visual);
                }
            }
            activeNovas.Clear();
        }
    }
}
