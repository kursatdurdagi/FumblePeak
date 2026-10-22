using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that rains projectiles/effects from above in an area.
    /// Examples: Runetracer, Clock Lancet, Fire rain, Meteor shower
    /// </summary>
    public class RainSkill : BaseSkill
    {
        [Header("Rain Settings")]
        [Tooltip("Radius of the rain area")]
        [SerializeField] private float rainRadius = 6f;
        
        [Tooltip("Radius increase per level")]
        [SerializeField] private float radiusPerLevel = 0.5f;
        
        [Tooltip("Number of projectiles per second")]
        [SerializeField] private float projectilesPerSecond = 3f;
        
        [Tooltip("Additional projectiles per second per level")]
        [SerializeField] private float projectilesPerLevel = 0.5f;
        
        [Tooltip("Duration of the rain effect")]
        [SerializeField] private float rainDuration = 3f;
        
        [Tooltip("Duration increase per level")]
        [SerializeField] private float durationPerLevel = 0.3f;
        
        [Header("Projectile Settings")]
        [Tooltip("Height from which projectiles fall")]
        [SerializeField] private float dropHeight = 15f;
        
        [Tooltip("Projectile fall speed")]
        [SerializeField] private float fallSpeed = 20f;
        
        [Tooltip("Impact damage radius")]
        [SerializeField] private float impactRadius = 1f;
        
        [Tooltip("Impact radius increase per level")]
        [SerializeField] private float impactRadiusPerLevel = 0.1f;
        
        [Header("Targeting")]
        [Tooltip("How the rain area is positioned")]
        [SerializeField] private RainTargetMode targetMode = RainTargetMode.AroundPlayer;
        
        [Tooltip("Offset from player for the rain center")]
        [SerializeField] private float targetOffset = 0f;
        
        [Tooltip("For TargetEnemies mode - prioritize enemy clusters")]
        [SerializeField] private bool targetClusters = true;
        
        [Header("Visual")]
        [Tooltip("Prefab for falling projectile")]
        [SerializeField] private GameObject fallingProjectilePrefab;
        
        [Tooltip("Effect played on impact")]
        [SerializeField] private ParticleSystem impactEffectPrefab;
        
        [Tooltip("Warning indicator before impact")]
        [SerializeField] private GameObject warningIndicatorPrefab;
        
        [Tooltip("Warning time before impact")]
        [SerializeField]
        #pragma warning disable CS0414 // Exposed for future warning indicator implementation
        private float warningTime = 0.5f;
        #pragma warning restore CS0414
        
        public enum RainTargetMode
        {
            AroundPlayer,     // Rain centered on player
            ForwardOfPlayer,  // Rain in front of player
            TargetEnemies,    // Rain on enemy positions
            Random            // Random position in range
        }
        
        // Current stats
        private float currentRainRadius;
        private float currentProjectilesPerSecond;
        private float currentRainDuration;
        private float currentImpactRadius;
        
        // Runtime
        private bool isRaining;
        private float rainStartTime;
        private float lastProjectileTime;
        private Vector3 rainCenter;
        private List<FallingProjectile> activeProjectiles = new List<FallingProjectile>();
        
        private class FallingProjectile
        {
            public Vector3 targetPosition;
            public Vector3 currentPosition;
            public GameObject visual;
            public GameObject warningIndicator;
            public float damage;
            public float impactRadius;
            public bool hasImpacted;
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentRainRadius = rainRadius + (radiusPerLevel * (currentLevel - 1));
            currentProjectilesPerSecond = projectilesPerSecond + (projectilesPerLevel * (currentLevel - 1));
            currentRainDuration = rainDuration + (durationPerLevel * (currentLevel - 1));
            currentImpactRadius = impactRadius + (impactRadiusPerLevel * (currentLevel - 1));
        }
        
        protected override void OnActivate()
        {
            isRaining = true;
            rainStartTime = Time.time;
            lastProjectileTime = Time.time;
            rainCenter = GetRainCenter();
        }
        
        protected override void UpdateSkillLogic()
        {
            // Update active projectiles
            UpdateProjectiles();
            
            if (!isRaining) return;
            
            // Check if rain duration expired
            if (Time.time - rainStartTime > currentRainDuration)
            {
                isRaining = false;
                return;
            }
            
            // Spawn new projectiles
            float spawnInterval = 1f / currentProjectilesPerSecond;
            while (Time.time - lastProjectileTime >= spawnInterval)
            {
                SpawnRainProjectile();
                lastProjectileTime += spawnInterval;
            }
        }
        
        private Vector3 GetRainCenter()
        {
            Vector3 playerPos = playerTransform.position;
            
            switch (targetMode)
            {
                case RainTargetMode.AroundPlayer:
                    return playerPos;
                    
                case RainTargetMode.ForwardOfPlayer:
                    return playerPos + playerTransform.forward * targetOffset;
                    
                case RainTargetMode.TargetEnemies:
                    if (targetClusters)
                    {
                        return FindEnemyClusterCenter(playerPos);
                    }
                    int closest = skillManager.GetClosestAgent(playerPos, currentRainRadius * 2f);
                    return closest >= 0 ? skillManager.GetAgentPosition(closest) : playerPos;
                    
                case RainTargetMode.Random:
                    Vector2 randomOffset = Random.insideUnitCircle * targetOffset;
                    return playerPos + new Vector3(randomOffset.x, 0, randomOffset.y);
            }
            
            return playerPos;
        }
        
        private Vector3 FindEnemyClusterCenter(Vector3 playerPos)
        {
            var agents = skillManager.GetAgentsInRange(playerPos, currentRainRadius * 2f);
            
            if (agents.Count == 0) return playerPos;
            
            // Find position with most enemies nearby
            Vector3 bestPos = playerPos;
            int bestCount = 0;
            
            foreach (int agentIndex in agents)
            {
                Vector3 pos = skillManager.GetAgentPosition(agentIndex);
                int nearby = skillManager.GetEnemyCountInRange(pos, currentRainRadius);
                
                if (nearby > bestCount)
                {
                    bestCount = nearby;
                    bestPos = pos;
                }
            }
            
            return bestPos;
        }
        
        private void SpawnRainProjectile()
        {
            // Random position within rain area
            Vector2 randomCircle = Random.insideUnitCircle * currentRainRadius;
            Vector3 targetPos = rainCenter + new Vector3(randomCircle.x, 0, randomCircle.y);
            
            // For enemy targeting mode, bias toward enemy positions
            if (targetMode == RainTargetMode.TargetEnemies)
            {
                var agents = skillManager.GetAgentsInRange(rainCenter, currentRainRadius);
                if (agents.Count > 0 && Random.value < 0.7f) // 70% chance to target enemy
                {
                    int randomAgent = agents[Random.Range(0, agents.Count)];
                    targetPos = skillManager.GetAgentPosition(randomAgent);
                }
            }
            
            var projectile = new FallingProjectile
            {
                targetPosition = targetPos,
                currentPosition = targetPos + Vector3.up * dropHeight,
                damage = currentDamage,
                impactRadius = currentImpactRadius,
                hasImpacted = false
            };
            
            // Spawn warning indicator
            if (warningIndicatorPrefab != null)
            {
                projectile.warningIndicator = Instantiate(warningIndicatorPrefab, targetPos, Quaternion.identity, transform);
                
                // Scale to impact radius
                float scale = currentImpactRadius * 2f;
                projectile.warningIndicator.transform.localScale = new Vector3(scale, 0.1f, scale);
            }
            
            // Spawn falling visual
            if (fallingProjectilePrefab != null)
            {
                projectile.visual = Instantiate(fallingProjectilePrefab, projectile.currentPosition, Quaternion.identity, transform);
            }
            
            activeProjectiles.Add(projectile);
        }
        
        private void UpdateProjectiles()
        {
            for (int i = activeProjectiles.Count - 1; i >= 0; i--)
            {
                var projectile = activeProjectiles[i];
                
                if (projectile.hasImpacted) continue;
                
                // Move projectile down
                projectile.currentPosition += Vector3.down * fallSpeed * Time.deltaTime;
                
                if (projectile.visual != null)
                {
                    projectile.visual.transform.position = projectile.currentPosition;
                }
                
                // Check for impact
                if (projectile.currentPosition.y <= projectile.targetPosition.y)
                {
                    OnProjectileImpact(projectile);
                    
                    // Cleanup
                    if (projectile.visual != null) Destroy(projectile.visual);
                    if (projectile.warningIndicator != null) Destroy(projectile.warningIndicator);
                    
                    activeProjectiles.RemoveAt(i);
                }
            }
        }
        
        private void OnProjectileImpact(FallingProjectile projectile)
        {
            projectile.hasImpacted = true;
            
            // Spawn impact effect (respects effect limits)
            SpawnEffect(impactEffectPrefab, projectile.targetPosition);
            
            // Deal damage in area
            var agents = GetAgentsInRange(projectile.targetPosition, projectile.impactRadius);
            
            foreach (int agentIndex in agents)
            {
                DealDamageToAgent(agentIndex, projectile.damage);
                PlayHitSound(); // Sound per hit (will be limited by effect system)
            }
        }
        
        public override string GetStatsDescription()
        {
            return $"Radius: {currentRainRadius:F1}m\n" +
                   $"Damage: {currentDamage:F0}\n" +
                   $"Rate: {currentProjectilesPerSecond:F1}/s\n" +
                   $"Duration: {currentRainDuration:F1}s";
        }
        
        private void OnDestroy()
        {
            foreach (var projectile in activeProjectiles)
            {
                if (projectile.visual != null) Destroy(projectile.visual);
                if (projectile.warningIndicator != null) Destroy(projectile.warningIndicator);
            }
            activeProjectiles.Clear();
        }
    }
}
