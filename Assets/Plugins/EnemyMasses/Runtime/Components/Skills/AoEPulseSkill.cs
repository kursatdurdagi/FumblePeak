using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that creates periodic area-of-effect damage around the player or at target locations.
    /// Examples: Garlic (VS), Lightning Ring, Holy Water, Song of Mana
    /// </summary>
    public class AoEPulseSkill : BaseSkill
    {
        [Header("AoE Settings")]
        [Tooltip("Area of effect radius")]
        [SerializeField] private float aoeRadius = 3f;
        
        [Tooltip("Radius increase per level")]
        [SerializeField] private float radiusPerLevel = 0.3f;
        
        [Tooltip("Number of simultaneous AoE zones")]
        [SerializeField] private int zoneCount = 1;
        
        [Tooltip("Additional zones per level")]
        [SerializeField] private int zonesPerLevel = 0;
        
        [Header("Pulse Settings")]
        [Tooltip("Duration of each AoE zone")]
        [SerializeField] private float zoneDuration = 1f;
        
        [Tooltip("How often damage is applied within the zone (0 = once)")]
        [SerializeField] private float tickRate = 0.2f;
        
        [Tooltip("Damage applied per tick (total damage = ticks * damage)")]
        [SerializeField] private bool damagePerTick = true;
        
        [Header("Targeting")]
        [Tooltip("Where the AoE zones spawn")]
        [SerializeField] private AoETargetMode targetMode = AoETargetMode.AroundPlayer;
        
        [Tooltip("For random/targeted modes - max distance from player")]
        [SerializeField] private float maxSpawnDistance = 8f;
        
        [Tooltip("For targeted mode - minimum enemies required")]
        [SerializeField] private int minEnemiesForTarget = 1;
        
        [Header("Visual")]
        [Tooltip("Visual effect for the AoE zone")]
        [SerializeField] private GameObject zoneVisualPrefab;
        
        [Tooltip("Should zone visual scale with radius")]
        [SerializeField] private bool scaleVisualToRadius = true;
        
        [Tooltip("Visual effect on damage tick")]
        [SerializeField] private ParticleSystem tickEffectPrefab;
        
        [Header("Knockback")]
        [Tooltip("Apply knockback to enemies")]
        [SerializeField] private bool applyKnockback = false;
        
        [Tooltip("Horizontal knockback force (m/s)")]
        [SerializeField] private float knockbackForce = 2f;
        
        [Tooltip("Vertical lift force (m/s)")]
        [SerializeField] private float knockbackLiftForce = 0f;
        
        public enum AoETargetMode
        {
            AroundPlayer,        // Always centered on player
            RandomNearPlayer,    // Random position near player
            TargetEnemyCluster,  // Target area with most enemies
            TargetClosestEnemy,  // Target closest enemy position
            FollowPlayer         // Creates persistent zone that follows player
        }
        
        // Current stats
        private float currentAoeRadius;
        private int currentZoneCount;
        
        // Active zones
        private List<ActiveZone> activeZones = new List<ActiveZone>();
        
        private class ActiveZone
        {
            public GameObject visual;
            public Vector3 position;
            public float spawnTime;
            public float lastTickTime;
            public bool followPlayer;
            public HashSet<int> hitThisTick = new HashSet<int>();
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentAoeRadius = aoeRadius + (radiusPerLevel * (currentLevel - 1));
            currentZoneCount = zoneCount + (zonesPerLevel * (currentLevel - 1));
        }
        
        protected override void OnActivate()
        {
            // Clear old zones
            foreach (var zone in activeZones)
            {
                if (zone.visual != null)
                {
                    Destroy(zone.visual);
                }
            }
            activeZones.Clear();
            
            // Spawn new zones
            var positions = GetTargetPositions();
            foreach (var pos in positions)
            {
                SpawnZone(pos, targetMode == AoETargetMode.FollowPlayer);
            }
        }
        
        protected override void UpdateSkillLogic()
        {
            UpdateZones();
        }
        
        private List<Vector3> GetTargetPositions()
        {
            var positions = new List<Vector3>();
            Vector3 playerPos = playerTransform.position;
            
            switch (targetMode)
            {
                case AoETargetMode.AroundPlayer:
                case AoETargetMode.FollowPlayer:
                    for (int i = 0; i < currentZoneCount; i++)
                    {
                        positions.Add(playerPos);
                    }
                    break;
                    
                case AoETargetMode.RandomNearPlayer:
                    for (int i = 0; i < currentZoneCount; i++)
                    {
                        Vector2 randomCircle = Random.insideUnitCircle * maxSpawnDistance;
                        positions.Add(playerPos + new Vector3(randomCircle.x, 0, randomCircle.y));
                    }
                    break;
                    
                case AoETargetMode.TargetEnemyCluster:
                    positions = FindEnemyClusters();
                    break;
                    
                case AoETargetMode.TargetClosestEnemy:
                    positions = FindClosestEnemyPositions();
                    break;
            }
            
            return positions;
        }
        
        private List<Vector3> FindEnemyClusters()
        {
            var positions = new List<Vector3>();
            Vector3 playerPos = playerTransform.position;
            
            // Get all enemies in range
            var enemies = skillManager.GetAgentsInRangeWithDistance(playerPos, maxSpawnDistance + currentAoeRadius);
            
            if (enemies.Count == 0)
            {
                // No enemies, fall back to player position
                for (int i = 0; i < currentZoneCount; i++)
                {
                    positions.Add(playerPos);
                }
                return positions;
            }
            
            // Find positions with highest enemy density
            var scored = new List<(Vector3 pos, int count)>();
            
            foreach (var (index, _) in enemies)
            {
                Vector3 pos = skillManager.GetAgentPosition(index);
                int nearbyCount = skillManager.GetEnemyCountInRange(pos, currentAoeRadius);
                scored.Add((pos, nearbyCount));
            }
            
            // Sort by enemy count
            scored.Sort((a, b) => b.count.CompareTo(a.count));
            
            // Take best positions
            for (int i = 0; i < currentZoneCount && i < scored.Count; i++)
            {
                if (scored[i].count >= minEnemiesForTarget)
                {
                    positions.Add(scored[i].pos);
                }
            }
            
            // Fill remaining with random positions
            while (positions.Count < currentZoneCount)
            {
                Vector2 randomCircle = Random.insideUnitCircle * maxSpawnDistance;
                positions.Add(playerPos + new Vector3(randomCircle.x, 0, randomCircle.y));
            }
            
            return positions;
        }
        
        private List<Vector3> FindClosestEnemyPositions()
        {
            var positions = new List<Vector3>();
            Vector3 playerPos = playerTransform.position;
            
            var enemies = skillManager.GetAgentsInRangeWithDistance(playerPos, maxSpawnDistance);
            enemies.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            for (int i = 0; i < currentZoneCount; i++)
            {
                if (i < enemies.Count)
                {
                    positions.Add(skillManager.GetAgentPosition(enemies[i].index));
                }
                else
                {
                    positions.Add(playerPos);
                }
            }
            
            return positions;
        }
        
        private void SpawnZone(Vector3 position, bool followPlayer)
        {
            var zone = new ActiveZone
            {
                position = position,
                spawnTime = Time.time,
                lastTickTime = Time.time,
                followPlayer = followPlayer
            };
            
            if (zoneVisualPrefab != null)
            {
                zone.visual = Instantiate(zoneVisualPrefab, position, Quaternion.identity, transform);
                
                if (scaleVisualToRadius)
                {
                    float scale = currentAoeRadius * 2f; // Diameter
                    zone.visual.transform.localScale = new Vector3(scale, zone.visual.transform.localScale.y, scale);
                }
            }
            
            activeZones.Add(zone);
            
            // Apply first tick of damage immediately
            ApplyZoneDamage(zone);
        }
        
        private void UpdateZones()
        {
            for (int i = activeZones.Count - 1; i >= 0; i--)
            {
                var zone = activeZones[i];
                
                // Check if zone expired
                if (Time.time - zone.spawnTime > zoneDuration)
                {
                    if (zone.visual != null)
                    {
                        Destroy(zone.visual);
                    }
                    activeZones.RemoveAt(i);
                    continue;
                }
                
                // Update position for following zones
                if (zone.followPlayer)
                {
                    zone.position = playerTransform.position;
                    if (zone.visual != null)
                    {
                        zone.visual.transform.position = zone.position;
                    }
                }
                
                // Apply tick damage
                if (tickRate > 0 && Time.time - zone.lastTickTime >= tickRate)
                {
                    zone.hitThisTick.Clear();
                    ApplyZoneDamage(zone);
                    zone.lastTickTime = Time.time;
                }
            }
        }
        
        private void ApplyZoneDamage(ActiveZone zone)
        {
            var agentsInRange = GetAgentsInRange(zone.position, currentAoeRadius);
            
            float damageAmount = damagePerTick ? currentDamage : currentDamage / Mathf.Max(1, zoneDuration / tickRate);
            
            foreach (int agentIndex in agentsInRange)
            {
                if (zone.hitThisTick.Contains(agentIndex)) continue;
                
                DealDamageToAgent(agentIndex, damageAmount);
                zone.hitThisTick.Add(agentIndex);
                
                // Spawn tick effect (respects effect limits)
                SpawnEffect(tickEffectPrefab, skillManager.GetAgentPosition(agentIndex));
                
                // Apply knockback (push away from zone center)
                if (applyKnockback && knockbackForce > 0f)
                {
                    Vector3 knockbackDir = (skillManager.GetAgentPosition(agentIndex) - zone.position).normalized;
                    ApplyKnockback(agentIndex, knockbackDir, knockbackForce, knockbackLiftForce);
                }
            }
        }
        
        public override string GetStatsDescription()
        {
            return $"Radius: {currentAoeRadius:F1}m\n" +
                   $"Damage: {currentDamage:F0}\n" +
                   $"Duration: {zoneDuration:F1}s\n" +
                   $"Zones: {currentZoneCount}";
        }
        
        private void OnDestroy()
        {
            foreach (var zone in activeZones)
            {
                if (zone.visual != null)
                {
                    Destroy(zone.visual);
                }
            }
            activeZones.Clear();
        }
    }
}
