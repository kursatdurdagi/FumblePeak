using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that fires projectiles outward from the player in multiple directions.
    /// Can be configured for radial burst, targeted shots, or directional fire.
    /// Examples: Magic Wand, Fire Wand, Knife, Axes
    /// </summary>
    public class ProjectileSkill : BaseSkill
    {
        [Header("Projectile Settings")]
        [Tooltip("Number of projectiles per burst")]
        [SerializeField] private int projectileCount = 1;
        
        [Tooltip("Additional projectiles per level")]
        [SerializeField] private int projectilesPerLevel = 1;
        
        [Tooltip("Speed of projectiles")]
        [SerializeField] private float projectileSpeed = 15f;
        
        [Tooltip("Speed increase per level")]
        [SerializeField] private float speedPerLevel = 1f;
        
        [Tooltip("Lifetime of projectiles in seconds")]
        [SerializeField] private float projectileLifetime = 3f;
        
        [Tooltip("Projectile hit radius")]
        [SerializeField] private float hitRadius = 0.5f;
        
        [Tooltip("Hit radius increase per level")]
        [SerializeField] private float hitRadiusPerLevel = 0.05f;
        
        [Header("Firing Pattern")]
        [Tooltip("How projectiles are aimed")]
        [SerializeField] private ProjectileFiringMode firingMode = ProjectileFiringMode.TargetClosest;
        
        [Tooltip("Arc spread for ArcSpread mode (degrees)")]
        [SerializeField] private float arcSpread = 45f;
        
        [Tooltip("For random spread - max angle deviation")]
        [SerializeField] private float randomSpread = 10f;
        
        [Header("Pierce & Bounce")]
        [Tooltip("Number of enemies the projectile can pierce through")]
        [SerializeField] private int pierceCount = 0;
        
        [Tooltip("Additional pierce per level")]
        [SerializeField] private int piercePerLevel = 0;
        
        [Tooltip("Number of times projectile can bounce/chain")]
        [SerializeField] private int bounceCount = 0;
        
        [Tooltip("Additional bounces per level")]
        [SerializeField] private int bouncePerLevel = 0;
        
        [Tooltip("Bounce search range for next target")]
        [SerializeField] private float bounceRange = 5f;
        
        [Header("Visual")]
        [Tooltip("Scale of projectiles")]
        [SerializeField] private Vector3 projectileScale = Vector3.one;
        
        [Tooltip("Scale increase per level")]
        [SerializeField] private float scalePerLevel = 0.1f;
        
        [Tooltip("Should projectile rotate to face direction")]
        [SerializeField] private bool rotateToDirection = true;
        
        [Tooltip("Trail effect prefab (optional)")]
        [SerializeField] private TrailRenderer trailPrefab;
        
        [Header("Spawn Position")]
        [Tooltip("Offset from player position for spawn")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0, 1, 0);
        
        // Current stats
        private int currentProjectileCount;
        private float currentProjectileSpeed;
        private float currentHitRadius;
        private int currentPierceCount;
        private int currentBounceCount;
        private Vector3 currentScale;
        
        // Active projectiles
        private List<ActiveProjectile> activeProjectiles = new List<ActiveProjectile>();
        
        public enum ProjectileFiringMode
        {
            TargetClosest,       // Fire at closest enemy
            TargetRandom,        // Fire at random enemies in range
            RadialBurst,         // Fire in all directions equally spaced
            ForwardFan,          // Fire in a fan shape forward
            ArcSpread,           // Fire in an arc towards closest enemy
            PlayerFacing,        // Fire in direction player is facing
            RandomDirection      // Fire in random directions
        }
        
        private class ActiveProjectile
        {
            public GameObject gameObject;
            public Vector3 direction;
            public float spawnTime;
            public int remainingPierces;
            public int remainingBounces;
            public HashSet<int> hitEnemies = new HashSet<int>();
            public float damage;
            public float hitRadius;
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentProjectileCount = projectileCount + (projectilesPerLevel * (currentLevel - 1));
            currentProjectileSpeed = projectileSpeed + (speedPerLevel * (currentLevel - 1));
            currentHitRadius = hitRadius + (hitRadiusPerLevel * (currentLevel - 1));
            currentPierceCount = pierceCount + (piercePerLevel * (currentLevel - 1));
            currentBounceCount = bounceCount + (bouncePerLevel * (currentLevel - 1));
            currentScale = projectileScale * (1f + scalePerLevel * (currentLevel - 1));
        }
        
        protected override void OnActivate()
        {
            FireProjectiles();
        }
        
        protected override void UpdateSkillLogic()
        {
            UpdateProjectiles();
        }
        
        private void FireProjectiles()
        {
            if (visualPrefab == null || skillManager == null) return;
            
            Vector3 spawnPos = playerTransform.position + spawnOffset;
            List<Vector3> directions = GetFiringDirections(spawnPos);
            
            for (int i = 0; i < directions.Count; i++)
            {
                SpawnProjectile(spawnPos, directions[i]);
            }
        }
        
        private List<Vector3> GetFiringDirections(Vector3 spawnPos)
        {
            var directions = new List<Vector3>();
            
            switch (firingMode)
            {
                case ProjectileFiringMode.TargetClosest:
                    directions = GetClosestTargetDirections(spawnPos);
                    break;
                    
                case ProjectileFiringMode.TargetRandom:
                    directions = GetRandomTargetDirections(spawnPos);
                    break;
                    
                case ProjectileFiringMode.RadialBurst:
                    directions = GetRadialDirections();
                    break;
                    
                case ProjectileFiringMode.ForwardFan:
                    directions = GetForwardFanDirections();
                    break;
                    
                case ProjectileFiringMode.ArcSpread:
                    directions = GetArcSpreadDirections(spawnPos);
                    break;
                    
                case ProjectileFiringMode.PlayerFacing:
                    directions = GetPlayerFacingDirections();
                    break;
                    
                case ProjectileFiringMode.RandomDirection:
                    directions = GetRandomDirections();
                    break;
            }
            
            return directions;
        }
        
        private List<Vector3> GetClosestTargetDirections(Vector3 spawnPos)
        {
            var directions = new List<Vector3>();
            var targets = skillManager.GetAgentsInRangeWithDistance(spawnPos, currentRange);
            
            // Sort by distance
            targets.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            for (int i = 0; i < currentProjectileCount; i++)
            {
                if (i < targets.Count)
                {
                    Vector3 targetPos = skillManager.GetAgentPosition(targets[i].index);
                    Vector3 dir = (targetPos - spawnPos).normalized;
                    dir = ApplySpread(dir);
                    directions.Add(dir);
                }
                else
                {
                    // No more targets, fire in random direction
                    directions.Add(GetRandomDirection());
                }
            }
            
            return directions;
        }
        
        private List<Vector3> GetRandomTargetDirections(Vector3 spawnPos)
        {
            var directions = new List<Vector3>();
            var targets = skillManager.GetRandomAgentsInRange(spawnPos, currentRange, currentProjectileCount);
            
            for (int i = 0; i < currentProjectileCount; i++)
            {
                if (i < targets.Count)
                {
                    Vector3 targetPos = skillManager.GetAgentPosition(targets[i]);
                    Vector3 dir = (targetPos - spawnPos).normalized;
                    dir = ApplySpread(dir);
                    directions.Add(dir);
                }
                else
                {
                    directions.Add(GetRandomDirection());
                }
            }
            
            return directions;
        }
        
        private List<Vector3> GetRadialDirections()
        {
            var directions = new List<Vector3>();
            float angleStep = 360f / currentProjectileCount;
            
            for (int i = 0; i < currentProjectileCount; i++)
            {
                float angle = angleStep * i * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                directions.Add(dir);
            }
            
            return directions;
        }
        
        private List<Vector3> GetForwardFanDirections()
        {
            var directions = new List<Vector3>();
            Vector3 forward = playerTransform.forward;
            
            if (currentProjectileCount == 1)
            {
                directions.Add(forward);
            }
            else
            {
                float startAngle = -arcSpread / 2f;
                float angleStep = arcSpread / (currentProjectileCount - 1);
                
                for (int i = 0; i < currentProjectileCount; i++)
                {
                    float angle = startAngle + angleStep * i;
                    Vector3 dir = Quaternion.Euler(0, angle, 0) * forward;
                    directions.Add(dir);
                }
            }
            
            return directions;
        }
        
        private List<Vector3> GetArcSpreadDirections(Vector3 spawnPos)
        {
            var directions = new List<Vector3>();
            
            // Get direction to closest enemy
            int closestAgent = skillManager.GetClosestAgent(spawnPos, currentRange);
            Vector3 baseDir = playerTransform.forward;
            
            if (closestAgent >= 0)
            {
                Vector3 targetPos = skillManager.GetAgentPosition(closestAgent);
                baseDir = (targetPos - spawnPos).normalized;
                baseDir.y = 0;
                baseDir.Normalize();
            }
            
            if (currentProjectileCount == 1)
            {
                directions.Add(baseDir);
            }
            else
            {
                float startAngle = -arcSpread / 2f;
                float angleStep = arcSpread / (currentProjectileCount - 1);
                
                for (int i = 0; i < currentProjectileCount; i++)
                {
                    float angle = startAngle + angleStep * i;
                    Vector3 dir = Quaternion.Euler(0, angle, 0) * baseDir;
                    directions.Add(dir);
                }
            }
            
            return directions;
        }
        
        private List<Vector3> GetPlayerFacingDirections()
        {
            var directions = new List<Vector3>();
            Vector3 forward = playerTransform.forward;
            
            for (int i = 0; i < currentProjectileCount; i++)
            {
                Vector3 dir = ApplySpread(forward);
                directions.Add(dir);
            }
            
            return directions;
        }
        
        private List<Vector3> GetRandomDirections()
        {
            var directions = new List<Vector3>();
            
            for (int i = 0; i < currentProjectileCount; i++)
            {
                directions.Add(GetRandomDirection());
            }
            
            return directions;
        }
        
        private Vector3 GetRandomDirection()
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        }
        
        private Vector3 ApplySpread(Vector3 baseDir)
        {
            if (randomSpread <= 0) return baseDir;
            
            float spreadAngle = Random.Range(-randomSpread, randomSpread);
            return Quaternion.Euler(0, spreadAngle, 0) * baseDir;
        }
        
        private void SpawnProjectile(Vector3 position, Vector3 direction)
        {
            var obj = Instantiate(visualPrefab, position, Quaternion.identity, transform);
            obj.transform.localScale = currentScale;
            
            if (rotateToDirection)
            {
                obj.transform.rotation = Quaternion.LookRotation(direction);
            }
            
            var projectile = new ActiveProjectile
            {
                gameObject = obj,
                direction = direction,
                spawnTime = Time.time,
                remainingPierces = currentPierceCount,
                remainingBounces = currentBounceCount,
                damage = currentDamage,
                hitRadius = currentHitRadius
            };
            
            activeProjectiles.Add(projectile);
        }
        
        private void UpdateProjectiles()
        {
            for (int i = activeProjectiles.Count - 1; i >= 0; i--)
            {
                var projectile = activeProjectiles[i];
                
                if (projectile.gameObject == null)
                {
                    activeProjectiles.RemoveAt(i);
                    continue;
                }
                
                // Check lifetime
                if (Time.time - projectile.spawnTime > projectileLifetime)
                {
                    DestroyProjectile(projectile);
                    activeProjectiles.RemoveAt(i);
                    continue;
                }
                
                // Move projectile
                Vector3 movement = projectile.direction * currentProjectileSpeed * Time.deltaTime;
                projectile.gameObject.transform.position += movement;
                
                // Check for hits
                if (CheckProjectileHits(projectile))
                {
                    if (projectile.remainingPierces <= 0 && projectile.remainingBounces <= 0)
                    {
                        DestroyProjectile(projectile);
                        activeProjectiles.RemoveAt(i);
                    }
                }
            }
        }
        
        private bool CheckProjectileHits(ActiveProjectile projectile)
        {
            if (skillManager == null) return false;
            
            Vector3 pos = projectile.gameObject.transform.position;
            var agentsInRange = GetAgentsInRange(pos, projectile.hitRadius);
            
            foreach (int agentIndex in agentsInRange)
            {
                // Skip already hit enemies (for pierce)
                if (projectile.hitEnemies.Contains(agentIndex)) continue;
                
                // Deal damage
                DealDamageToAgent(agentIndex, projectile.damage);
                SpawnHitEffect(skillManager.GetAgentPosition(agentIndex));
                projectile.hitEnemies.Add(agentIndex);
                
                // Handle pierce
                if (projectile.remainingPierces > 0)
                {
                    projectile.remainingPierces--;
                    continue; // Keep going through more enemies
                }
                
                // Handle bounce
                if (projectile.remainingBounces > 0)
                {
                    projectile.remainingBounces--;
                    BounceProjectile(projectile);
                    return true;
                }
                
                return true; // Hit something, should destroy
            }
            
            return false;
        }
        
        private void BounceProjectile(ActiveProjectile projectile)
        {
            Vector3 pos = projectile.gameObject.transform.position;
            
            // Find next target
            var nearbyAgents = skillManager.GetAgentsInRangeWithDistance(pos, bounceRange);
            
            int nextTarget = -1;
            float closestDist = float.MaxValue;
            
            foreach (var (index, distance) in nearbyAgents)
            {
                if (projectile.hitEnemies.Contains(index)) continue;
                
                if (distance < closestDist)
                {
                    closestDist = distance;
                    nextTarget = index;
                }
            }
            
            if (nextTarget >= 0)
            {
                Vector3 targetPos = skillManager.GetAgentPosition(nextTarget);
                projectile.direction = (targetPos - pos).normalized;
                
                if (rotateToDirection)
                {
                    projectile.gameObject.transform.rotation = Quaternion.LookRotation(projectile.direction);
                }
            }
            // If no target found, projectile continues in same direction
        }
        
        private void DestroyProjectile(ActiveProjectile projectile)
        {
            if (projectile.gameObject != null)
            {
                Destroy(projectile.gameObject);
            }
        }
        
        public override string GetStatsDescription()
        {
            string desc = $"Projectiles: {currentProjectileCount}\n" +
                          $"Damage: {currentDamage:F0}\n" +
                          $"Speed: {currentProjectileSpeed:F0}";
            
            if (currentPierceCount > 0)
                desc += $"\nPierce: {currentPierceCount}";
            
            if (currentBounceCount > 0)
                desc += $"\nBounce: {currentBounceCount}";
            
            return desc;
        }
        
        private void OnDestroy()
        {
            foreach (var projectile in activeProjectiles)
            {
                if (projectile.gameObject != null)
                {
                    Destroy(projectile.gameObject);
                }
            }
            activeProjectiles.Clear();
        }
    }
}
