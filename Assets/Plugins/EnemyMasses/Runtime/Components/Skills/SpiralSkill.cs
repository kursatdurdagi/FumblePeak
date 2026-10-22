using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that fires projectiles in a spiral pattern from the player.
    /// Examples: Carréllo, Phiera Der Tuphello, Eight The Sparrow
    /// </summary>
    public class SpiralSkill : BaseSkill
    {
        [Header("Spiral Settings")]
        [Tooltip("Number of projectile streams")]
        [SerializeField] private int streamCount = 1;
        
        [Tooltip("Additional streams per level")]
        [SerializeField] private int streamsPerLevel = 1;
        
        [Tooltip("Rotation speed of the spiral (degrees/sec)")]
        [SerializeField] private float spiralRotationSpeed = 180f;
        
        [Tooltip("Rotation speed increase per level")]
        [SerializeField] private float rotationSpeedPerLevel = 20f;
        
        [Tooltip("Projectiles fired per second per stream")]
        [SerializeField] private float fireRate = 5f;
        
        [Tooltip("Fire rate increase per level")]
        [SerializeField] private float fireRatePerLevel = 0.5f;
        
        [Header("Projectile Settings")]
        [Tooltip("Projectile speed")]
        [SerializeField] private float projectileSpeed = 10f;
        
        [Tooltip("Speed increase per level")]
        [SerializeField] private float speedPerLevel = 1f;
        
        [Tooltip("Projectile lifetime")]
        [SerializeField] private float projectileLifetime = 2f;
        
        [Tooltip("Projectile hit radius")]
        [SerializeField] private float hitRadius = 0.3f;
        
        [Tooltip("Hit radius increase per level")]
        [SerializeField] private float hitRadiusPerLevel = 0.05f;
        
        [Tooltip("Pierce count (0 = no pierce)")]
        [SerializeField] private int pierceCount = 0;
        
        [Tooltip("Pierce increase per level")]
        [SerializeField] private int piercePerLevel = 1;
        
        [Header("Spiral Pattern")]
        [Tooltip("Direction of spiral rotation")]
        [SerializeField] private bool clockwise = true;
        
        [Tooltip("Outward spiral expansion (0 = pure rotation)")]
        [SerializeField] private float outwardSpeed = 0f;
        
        [Tooltip("Starting offset from player")]
        [SerializeField] private float startOffset = 0.5f;
        
        [Tooltip("Vertical offset")]
        [SerializeField] private float verticalOffset = 1f;
        
        [Header("Visual")]
        [Tooltip("Scale of projectiles")]
        [SerializeField] private Vector3 projectileScale = Vector3.one * 0.3f;
        
        [Tooltip("Scale increase per level")]
        [SerializeField] private float scalePerLevel = 0.05f;
        
        // Current stats
        private int currentStreamCount;
        private float currentSpiralSpeed;
        private float currentFireRate;
        private float currentProjectileSpeed;
        private float currentHitRadius;
        private int currentPierceCount;
        private Vector3 currentScale;
        
        // Runtime
        private float currentAngle = 0f;
        private float lastFireTime;
        private List<SpiralProjectile> activeProjectiles = new List<SpiralProjectile>();
        
        private class SpiralProjectile
        {
            public GameObject visual;
            public Vector3 position;
            public Vector3 direction;
            public float spawnTime;
            public int remainingPierces;
            public HashSet<int> hitEnemies = new HashSet<int>();
        }
        
        protected override void Awake()
        {
            base.Awake();
            baseCooldown = 0f; // Spiral is continuous
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentStreamCount = streamCount + (streamsPerLevel * (currentLevel - 1));
            currentSpiralSpeed = spiralRotationSpeed + (rotationSpeedPerLevel * (currentLevel - 1));
            currentFireRate = fireRate + (fireRatePerLevel * (currentLevel - 1));
            currentProjectileSpeed = projectileSpeed + (speedPerLevel * (currentLevel - 1));
            currentHitRadius = hitRadius + (hitRadiusPerLevel * (currentLevel - 1));
            currentPierceCount = pierceCount + (piercePerLevel * (currentLevel - 1));
            currentScale = projectileScale * (1f + scalePerLevel * (currentLevel - 1));
        }
        
        protected override void OnActivate()
        {
            // Spiral starts immediately
            lastFireTime = Time.time;
        }
        
        protected override void UpdateSkillLogic()
        {
            if (playerTransform == null) return;
            
            // Update spiral angle
            float rotationDir = clockwise ? 1f : -1f;
            currentAngle += currentSpiralSpeed * rotationDir * Time.deltaTime;
            if (currentAngle > 360f) currentAngle -= 360f;
            if (currentAngle < 0f) currentAngle += 360f;
            
            // Fire projectiles
            float fireInterval = 1f / currentFireRate;
            while (Time.time - lastFireTime >= fireInterval)
            {
                FireProjectiles();
                lastFireTime += fireInterval;
            }
            
            // Update active projectiles
            UpdateProjectiles();
        }
        
        private void FireProjectiles()
        {
            Vector3 playerPos = playerTransform.position + Vector3.up * verticalOffset;
            float angleStep = 360f / currentStreamCount;
            
            for (int i = 0; i < currentStreamCount; i++)
            {
                float angle = (currentAngle + angleStep * i) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 spawnPos = playerPos + direction * startOffset;
                
                SpawnProjectile(spawnPos, direction);
            }
        }
        
        private void SpawnProjectile(Vector3 position, Vector3 direction)
        {
            var projectile = new SpiralProjectile
            {
                position = position,
                direction = direction,
                spawnTime = Time.time,
                remainingPierces = currentPierceCount
            };
            
            if (visualPrefab != null)
            {
                projectile.visual = Instantiate(visualPrefab, position, Quaternion.LookRotation(direction), transform);
                projectile.visual.transform.localScale = currentScale;
            }
            
            activeProjectiles.Add(projectile);
        }
        
        private void UpdateProjectiles()
        {
            for (int i = activeProjectiles.Count - 1; i >= 0; i--)
            {
                var projectile = activeProjectiles[i];
                
                // Check lifetime
                if (Time.time - projectile.spawnTime > projectileLifetime)
                {
                    DestroyProjectile(projectile);
                    activeProjectiles.RemoveAt(i);
                    continue;
                }
                
                // Move projectile
                Vector3 movement = projectile.direction * currentProjectileSpeed * Time.deltaTime;
                
                // Apply outward expansion if enabled
                if (outwardSpeed > 0)
                {
                    Vector3 fromPlayer = (projectile.position - playerTransform.position);
                    fromPlayer.y = 0;
                    if (fromPlayer.sqrMagnitude > 0.01f)
                    {
                        movement += fromPlayer.normalized * outwardSpeed * Time.deltaTime;
                    }
                }
                
                projectile.position += movement;
                
                if (projectile.visual != null)
                {
                    projectile.visual.transform.position = projectile.position;
                    projectile.visual.transform.rotation = Quaternion.LookRotation(projectile.direction);
                }
                
                // Check for hits
                if (CheckProjectileHits(projectile))
                {
                    if (projectile.remainingPierces <= 0)
                    {
                        DestroyProjectile(projectile);
                        activeProjectiles.RemoveAt(i);
                    }
                }
            }
        }
        
        private bool CheckProjectileHits(SpiralProjectile projectile)
        {
            var agents = GetAgentsInRange(projectile.position, currentHitRadius);
            bool hitSomething = false;
            
            foreach (int agentIndex in agents)
            {
                if (projectile.hitEnemies.Contains(agentIndex)) continue;
                
                DealDamageToAgent(agentIndex, currentDamage);
                SpawnHitEffect(skillManager.GetAgentPosition(agentIndex));
                projectile.hitEnemies.Add(agentIndex);
                hitSomething = true;
                
                if (projectile.remainingPierces > 0)
                {
                    projectile.remainingPierces--;
                }
                else
                {
                    return true; // Should destroy projectile
                }
            }
            
            return hitSomething && projectile.remainingPierces <= 0;
        }
        
        private void DestroyProjectile(SpiralProjectile projectile)
        {
            if (projectile.visual != null)
            {
                Destroy(projectile.visual);
            }
        }
        
        public override string GetStatsDescription()
        {
            string desc = $"Streams: {currentStreamCount}\n" +
                          $"Damage: {currentDamage:F0}\n" +
                          $"Fire Rate: {currentFireRate:F1}/s\n" +
                          $"Speed: {currentProjectileSpeed:F0}";
            
            if (currentPierceCount > 0)
                desc += $"\nPierce: {currentPierceCount}";
            
            return desc;
        }
        
        private void OnDestroy()
        {
            foreach (var projectile in activeProjectiles)
            {
                if (projectile.visual != null)
                {
                    Destroy(projectile.visual);
                }
            }
            activeProjectiles.Clear();
        }
    }
}
