using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that chains damage between enemies.
    /// Examples: Lightning Ring, Chain Lightning, Bouncing projectiles
    /// </summary>
    public class ChainLightningSkill : BaseSkill
    {
        [Header("Chain Settings")]
        [Tooltip("Number of enemies the chain can jump to")]
        [SerializeField] private int chainCount = 3;
        
        [Tooltip("Additional chains per level")]
        [SerializeField] private int chainsPerLevel = 1;
        
        [Tooltip("Maximum range to find next chain target")]
        [SerializeField] private float chainRange = 5f;
        
        [Tooltip("Chain range increase per level")]
        [SerializeField] private float chainRangePerLevel = 0.5f;
        
        [Tooltip("Damage reduction per chain (0-1)")]
        [SerializeField] [Range(0f, 0.5f)] private float damageDecayPerChain = 0.1f;
        
        [Tooltip("Number of simultaneous chain starters")]
        [SerializeField] private int simultaneousChains = 1;
        
        [Tooltip("Additional simultaneous chains per level")]
        [SerializeField] private int simultaneousChainsPerLevel = 0;
        
        [Header("Initial Target")]
        [Tooltip("Range to find initial target from player")]
        [SerializeField] private float initialTargetRange = 10f;
        
        [Tooltip("How to select initial targets")]
        [SerializeField] private InitialTargetMode initialTargetMode = InitialTargetMode.Closest;
        
        [Header("Visual")]
        [Tooltip("Line renderer for chain visual (spawned per chain)")]
        [SerializeField] private LineRenderer chainVisualPrefab;
        
        [Tooltip("Duration the chain visual stays visible")]
        [SerializeField] private float visualDuration = 0.3f;
        
        [Tooltip("Effect at each chain hit point")]
        [SerializeField] private ParticleSystem chainHitEffectPrefab;
        
        [Tooltip("Chain animation speed (how fast it jumps)")]
        [SerializeField] 
        #pragma warning disable CS0414 // Exposed for future animation timing
        private float chainSpeed = 20f;
        #pragma warning restore CS0414
        
        public enum InitialTargetMode
        {
            Closest,
            Random,
            HighestHealth,
            MostClustered
        }
        
        // Current stats
        private int currentChainCount;
        private float currentChainRange;
        private int currentSimultaneousChains;
        
        // Active chains
        private List<ActiveChain> activeChains = new List<ActiveChain>();
        
        private class ActiveChain
        {
            public List<Vector3> points = new List<Vector3>();
            public List<int> hitAgents = new List<int>();
            public int currentIndex;
            public float damage;
            public GameObject visualObject;
            public LineRenderer lineRenderer;
            public float spawnTime;
            public bool isComplete;
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentChainCount = chainCount + (chainsPerLevel * (currentLevel - 1));
            currentChainRange = chainRange + (chainRangePerLevel * (currentLevel - 1));
            currentSimultaneousChains = simultaneousChains + (simultaneousChainsPerLevel * (currentLevel - 1));
        }
        
        protected override void OnActivate()
        {
            // Find initial targets
            var initialTargets = GetInitialTargets();
            
            foreach (int targetIndex in initialTargets)
            {
                if (targetIndex >= 0)
                {
                    StartChain(targetIndex);
                }
            }
        }
        
        protected override void UpdateSkillLogic()
        {
            UpdateActiveChains();
        }
        
        private List<int> GetInitialTargets()
        {
            var targets = new List<int>();
            Vector3 playerPos = playerTransform.position;
            
            switch (initialTargetMode)
            {
                case InitialTargetMode.Closest:
                    var closest = skillManager.GetAgentsInRangeWithDistance(playerPos, initialTargetRange);
                    closest.Sort((a, b) => a.distance.CompareTo(b.distance));
                    for (int i = 0; i < currentSimultaneousChains && i < closest.Count; i++)
                    {
                        targets.Add(closest[i].index);
                    }
                    break;
                    
                case InitialTargetMode.Random:
                    targets = skillManager.GetRandomAgentsInRange(playerPos, initialTargetRange, currentSimultaneousChains);
                    break;
                    
                case InitialTargetMode.MostClustered:
                    targets = GetMostClusteredTargets();
                    break;
            }
            
            return targets;
        }
        
        private List<int> GetMostClusteredTargets()
        {
            var targets = new List<int>();
            Vector3 playerPos = playerTransform.position;
            
            var allInRange = skillManager.GetAgentsInRange(playerPos, initialTargetRange);
            var scored = new List<(int index, int nearby)>();
            
            foreach (int agentIndex in allInRange)
            {
                Vector3 pos = skillManager.GetAgentPosition(agentIndex);
                int nearby = skillManager.GetEnemyCountInRange(pos, currentChainRange);
                scored.Add((agentIndex, nearby));
            }
            
            scored.Sort((a, b) => b.nearby.CompareTo(a.nearby));
            
            for (int i = 0; i < currentSimultaneousChains && i < scored.Count; i++)
            {
                targets.Add(scored[i].index);
            }
            
            return targets;
        }
        
        private void StartChain(int initialTarget)
        {
            var chain = new ActiveChain
            {
                currentIndex = 0,
                damage = currentDamage,
                spawnTime = Time.time,
                isComplete = false
            };
            
            // Add player as starting point
            chain.points.Add(playerTransform.position);
            
            // Add initial target
            Vector3 targetPos = skillManager.GetAgentPosition(initialTarget);
            chain.points.Add(targetPos);
            chain.hitAgents.Add(initialTarget);
            
            // Deal damage to first target
            DealDamageToAgent(initialTarget, chain.damage);
            SpawnHitEffect(targetPos);
            
            // Find chain targets
            FindChainTargets(chain, targetPos);
            
            // Create visual
            CreateChainVisual(chain);
            
            activeChains.Add(chain);
        }
        
        private void FindChainTargets(ActiveChain chain, Vector3 currentPos)
        {
            for (int i = 0; i < currentChainCount; i++)
            {
                int nextTarget = FindNextChainTarget(chain, currentPos);
                
                if (nextTarget < 0) break; // No more targets
                
                Vector3 nextPos = skillManager.GetAgentPosition(nextTarget);
                chain.points.Add(nextPos);
                chain.hitAgents.Add(nextTarget);
                
                // Calculate decayed damage
                float decayedDamage = chain.damage * (1f - damageDecayPerChain * (i + 1));
                decayedDamage = Mathf.Max(1f, decayedDamage); // Minimum 1 damage
                
                DealDamageToAgent(nextTarget, decayedDamage);
                SpawnHitEffect(nextPos);
                
                currentPos = nextPos;
            }
            
            chain.isComplete = true;
        }
        
        private int FindNextChainTarget(ActiveChain chain, Vector3 currentPos)
        {
            var nearby = skillManager.GetAgentsInRangeWithDistance(currentPos, currentChainRange);
            nearby.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            foreach (var (index, _) in nearby)
            {
                if (!chain.hitAgents.Contains(index))
                {
                    return index;
                }
            }
            
            return -1;
        }
        
        private void CreateChainVisual(ActiveChain chain)
        {
            if (chainVisualPrefab == null || chain.points.Count < 2) return;
            
            chain.visualObject = new GameObject("ChainVisual");
            chain.visualObject.transform.SetParent(transform);
            
            chain.lineRenderer = Instantiate(chainVisualPrefab, chain.visualObject.transform);
            chain.lineRenderer.positionCount = chain.points.Count;
            chain.lineRenderer.SetPositions(chain.points.ToArray());
        }
        
        private void UpdateActiveChains()
        {
            for (int i = activeChains.Count - 1; i >= 0; i--)
            {
                var chain = activeChains[i];
                
                // Check if visual should be removed
                if (Time.time - chain.spawnTime > visualDuration)
                {
                    if (chain.visualObject != null)
                    {
                        Destroy(chain.visualObject);
                    }
                    activeChains.RemoveAt(i);
                }
            }
        }
        
        private new void SpawnHitEffect(Vector3 position)
        {
            // Use the base class SpawnEffect which respects effect limits
            SpawnEffect(chainHitEffectPrefab, position);
        }
        
        public override string GetStatsDescription()
        {
            return $"Chains: {currentChainCount}\n" +
                   $"Damage: {currentDamage:F0}\n" +
                   $"Range: {currentChainRange:F1}m\n" +
                   $"Targets: {currentSimultaneousChains}";
        }
        
        private void OnDestroy()
        {
            foreach (var chain in activeChains)
            {
                if (chain.visualObject != null)
                {
                    Destroy(chain.visualObject);
                }
            }
            activeChains.Clear();
        }
    }
}
