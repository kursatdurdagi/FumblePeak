using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that creates a continuous beam/ray that damages enemies it touches.
    /// Examples: Death Spiral, La Borra, concentrated fire beam
    /// </summary>
    public class BeamSkill : BaseSkill
    {
        [Header("Beam Settings")]
        [Tooltip("Length of the beam")]
        [SerializeField] private float beamLength = 10f;
        
        [Tooltip("Beam length increase per level")]
        [SerializeField] private float lengthPerLevel = 1f;
        
        [Tooltip("Width of the beam for damage calculation")]
        [SerializeField] private float beamWidth = 0.5f;
        
        [Tooltip("Beam width increase per level")]
        [SerializeField] private float widthPerLevel = 0.1f;
        
        [Tooltip("Number of beams")]
        [SerializeField] private int beamCount = 1;
        
        [Tooltip("Additional beams per level")]
        [SerializeField] private int beamsPerLevel = 0;
        
        [Header("Beam Behavior")]
        [Tooltip("How the beam moves")]
        [SerializeField] private BeamBehavior behavior = BeamBehavior.RotateAroundPlayer;
        
        [Tooltip("Rotation speed for rotating beams (degrees/sec)")]
        [SerializeField] private float rotationSpeed = 90f;
        
        [Tooltip("Rotation speed increase per level")]
        [SerializeField] private float rotationSpeedPerLevel = 10f;
        
        [Tooltip("For TargetClosest mode - how fast beam tracks target")]
        [SerializeField] private float trackingSpeed = 180f;
        
        [Header("Damage")]
        [Tooltip("How often damage is applied (per second)")]
        [SerializeField] private float damageTickRate = 0.1f;
        
        [Tooltip("Minimum time between hits on same enemy")]
        [SerializeField] private float hitCooldownPerEnemy = 0.2f;
        
        [Header("Visual")]
        [Tooltip("Line renderer for beam visual")]
        [SerializeField] private LineRenderer beamLinePrefab;
        
        [Tooltip("Start point effect")]
        [SerializeField] private ParticleSystem startEffectPrefab;
        
        [Tooltip("End point/hit effect")]
        [SerializeField] private ParticleSystem endEffectPrefab;
        
        [Tooltip("Beam should scale with width")]
        [SerializeField] private bool scaleLineWidth = true;
        
        [Header("Spawn Position")]
        [Tooltip("Offset from player for beam origin")]
        [SerializeField] private Vector3 originOffset = new Vector3(0, 1, 0);
        
        public enum BeamBehavior
        {
            RotateAroundPlayer,   // Beams rotate around player
            TargetClosest,        // Beam aims at closest enemy
            TargetRandom,         // Beam aims at random enemy
            PlayerFacing,         // Beam fires in player facing direction
            Fixed                 // Beam in fixed direction
        }
        
        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = false;
        
        // Current stats
        private float currentBeamLength;
        private float currentBeamWidth;
        private int currentBeamCount;
        private float currentRotationSpeed;
        
        // Active beams
        private List<ActiveBeam> activeBeams = new List<ActiveBeam>();
        private float lastDamageTick;
        
        private class ActiveBeam
        {
            public float angle;
            public Vector3 direction;
            public GameObject visualObject;
            public LineRenderer lineRenderer;
            public ParticleSystem startEffect;
            public ParticleSystem endEffect;
            public Dictionary<int, float> hitCooldowns = new Dictionary<int, float>();
            public int targetAgent = -1;
        }
        
        protected override void Awake()
        {
            base.Awake();
            baseCooldown = 0f; // Beam is always active
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentBeamLength = beamLength + (lengthPerLevel * (currentLevel - 1));
            currentBeamWidth = beamWidth + (widthPerLevel * (currentLevel - 1));
            currentBeamCount = beamCount + (beamsPerLevel * (currentLevel - 1));
            currentRotationSpeed = rotationSpeed + (rotationSpeedPerLevel * (currentLevel - 1));
            
            UpdateBeamCount();
        }
        
        protected override void OnActivate()
        {
            // Only spawn beams if we don't have any yet
            // Beams are persistent, not respawned each activation
            if (activeBeams.Count == 0)
            {
                SpawnBeams();
            }
        }
        
        protected override void UpdateSkillLogic()
        {
            if (playerTransform == null) return;
            
            UpdateBeamDirections();
            UpdateBeamVisuals();
            
            // Damage tick
            if (Time.time - lastDamageTick >= damageTickRate)
            {
                ApplyBeamDamage();
                lastDamageTick = Time.time;
            }
        }
        
        private void SpawnBeams()
        {
            float angleStep = 360f / currentBeamCount;
            
            for (int i = 0; i < currentBeamCount; i++)
            {
                SpawnBeam(i, angleStep * i);
            }
        }
        
        private void SpawnBeam(int index, float startAngle)
        {
            var beam = new ActiveBeam
            {
                angle = startAngle,
                direction = GetDirectionFromAngle(startAngle)
            };
            
            // Create visual
            beam.visualObject = new GameObject($"{skillName}_Beam_{index}");
            beam.visualObject.transform.SetParent(transform);
            
            if (beamLinePrefab != null)
            {
                beam.lineRenderer = Instantiate(beamLinePrefab, beam.visualObject.transform);
                beam.lineRenderer.positionCount = 2;
                
                if (scaleLineWidth)
                {
                    beam.lineRenderer.startWidth = currentBeamWidth;
                    beam.lineRenderer.endWidth = currentBeamWidth;
                }
            }
            
            if (startEffectPrefab != null)
            {
                beam.startEffect = Instantiate(startEffectPrefab, beam.visualObject.transform);
            }
            
            if (endEffectPrefab != null)
            {
                beam.endEffect = Instantiate(endEffectPrefab, beam.visualObject.transform);
            }
            
            activeBeams.Add(beam);
        }
        
        private void UpdateBeamCount()
        {
            while (activeBeams.Count < currentBeamCount)
            {
                float angleStep = 360f / currentBeamCount;
                SpawnBeam(activeBeams.Count, angleStep * activeBeams.Count);
            }
            
            while (activeBeams.Count > currentBeamCount)
            {
                int lastIndex = activeBeams.Count - 1;
                if (activeBeams[lastIndex].visualObject != null)
                {
                    Destroy(activeBeams[lastIndex].visualObject);
                }
                activeBeams.RemoveAt(lastIndex);
            }
            
            // Update line widths
            foreach (var beam in activeBeams)
            {
                if (beam.lineRenderer != null && scaleLineWidth)
                {
                    beam.lineRenderer.startWidth = currentBeamWidth;
                    beam.lineRenderer.endWidth = currentBeamWidth;
                }
            }
        }
        
        private void UpdateBeamDirections()
        {
            float angleStep = 360f / currentBeamCount;
            
            for (int i = 0; i < activeBeams.Count; i++)
            {
                var beam = activeBeams[i];
                
                switch (behavior)
                {
                    case BeamBehavior.RotateAroundPlayer:
                        beam.angle += currentRotationSpeed * Time.deltaTime;
                        if (beam.angle > 360f) beam.angle -= 360f;
                        beam.direction = GetDirectionFromAngle(beam.angle);
                        break;
                        
                    case BeamBehavior.TargetClosest:
                        UpdateTargetedBeam(beam, true);
                        break;
                        
                    case BeamBehavior.TargetRandom:
                        UpdateTargetedBeam(beam, false);
                        break;
                        
                    case BeamBehavior.PlayerFacing:
                        beam.direction = playerTransform.forward;
                        // Apply offset for multiple beams
                        if (currentBeamCount > 1)
                        {
                            float offsetAngle = (i - (currentBeamCount - 1) / 2f) * 15f;
                            beam.direction = Quaternion.Euler(0, offsetAngle, 0) * beam.direction;
                        }
                        break;
                        
                    case BeamBehavior.Fixed:
                        // Direction doesn't change
                        break;
                }
            }
        }
        
        private void UpdateTargetedBeam(ActiveBeam beam, bool targetClosest)
        {
            Vector3 playerPos = playerTransform.position + originOffset;
            
            // Find target if we don't have one or target is dead
            if (beam.targetAgent < 0 || !skillManager.IsAgentAlive(beam.targetAgent))
            {
                if (targetClosest)
                {
                    beam.targetAgent = skillManager.GetClosestAgent(playerPos, currentBeamLength);
                }
                else
                {
                    var agents = skillManager.GetRandomAgentsInRange(playerPos, currentBeamLength, 1);
                    beam.targetAgent = agents.Count > 0 ? agents[0] : -1;
                }
            }
            
            if (beam.targetAgent >= 0)
            {
                Vector3 targetPos = skillManager.GetAgentPosition(beam.targetAgent);
                Vector3 desiredDir = (targetPos - playerPos).normalized;
                
                // Smooth tracking
                beam.direction = Vector3.RotateTowards(beam.direction, desiredDir, 
                    trackingSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
            }
        }
        
        private Vector3 GetDirectionFromAngle(float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad));
        }
        
        private void UpdateBeamVisuals()
        {
            Vector3 origin = playerTransform.position + originOffset;
            
            foreach (var beam in activeBeams)
            {
                Vector3 endPoint = origin + beam.direction * currentBeamLength;
                
                if (beam.lineRenderer != null)
                {
                    beam.lineRenderer.SetPosition(0, origin);
                    beam.lineRenderer.SetPosition(1, endPoint);
                }
                
                if (beam.startEffect != null)
                {
                    beam.startEffect.transform.position = origin;
                }
                
                if (beam.endEffect != null)
                {
                    beam.endEffect.transform.position = endPoint;
                }
            }
        }
        
        private void ApplyBeamDamage()
        {
            if (skillManager == null)
            {
                if (enableDebugLogging) Debug.LogWarning("[BeamSkill] skillManager is null!");
                return;
            }
            
            Vector3 origin = playerTransform.position + originOffset;
            
            foreach (var beam in activeBeams)
            {
                // Clean up old cooldowns
                CleanupHitCooldowns(beam);
                
                // Get all agents and check if they're in the beam path
                var agents = skillManager.GetAgentsInRange(origin, currentBeamLength + currentBeamWidth);
                
                if (enableDebugLogging && agents.Count > 0)
                {
                    Debug.Log($"[BeamSkill] Found {agents.Count} agents in range. Origin={origin}, Range={currentBeamLength + currentBeamWidth:F1}");
                }
                
                foreach (int agentIndex in agents)
                {
                    // Check cooldown
                    if (beam.hitCooldowns.TryGetValue(agentIndex, out float lastHit))
                    {
                        if (Time.time - lastHit < hitCooldownPerEnemy) continue;
                    }
                    
                    Vector3 agentPos = skillManager.GetAgentPosition(agentIndex);
                    
                    // Check if agent is within beam
                    if (IsPointInBeam(origin, beam.direction, agentPos))
                    {
                        if (enableDebugLogging)
                        {
                            Debug.Log($"[BeamSkill] <color=green>HIT!</color> Agent {agentIndex} at {agentPos}, Damage={currentDamage:F1}");
                        }
                        
                        DealDamageToAgent(agentIndex, currentDamage);
                        beam.hitCooldowns[agentIndex] = Time.time;
                        
                        // Spawn hit effect at agent position
                        SpawnHitEffect(agentPos);
                    }
                    else if (enableDebugLogging)
                    {
                        Debug.Log($"[BeamSkill] Agent {agentIndex} at {agentPos} NOT in beam (dir={beam.direction})");
                    }
                }
            }
        }
        
        private bool IsPointInBeam(Vector3 origin, Vector3 direction, Vector3 point)
        {
            // Project everything to 2D (XZ plane) since beam direction is horizontal
            Vector2 origin2D = new Vector2(origin.x, origin.z);
            Vector2 direction2D = new Vector2(direction.x, direction.z).normalized;
            Vector2 point2D = new Vector2(point.x, point.z);
            
            Vector2 toPoint = point2D - origin2D;
            
            // Project point onto beam line
            float dot = Vector2.Dot(toPoint, direction2D);
            
            // Check if projection is within beam length
            if (dot < 0 || dot > currentBeamLength) return false;
            
            // Check perpendicular distance (2D)
            Vector2 closestPoint = origin2D + direction2D * dot;
            float distance = Vector2.Distance(point2D, closestPoint);
            
            return distance <= currentBeamWidth;
        }
        
        private void CleanupHitCooldowns(ActiveBeam beam)
        {
            var keysToRemove = new List<int>();
            foreach (var kvp in beam.hitCooldowns)
            {
                if (Time.time - kvp.Value > hitCooldownPerEnemy * 2f)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                beam.hitCooldowns.Remove(key);
            }
        }
        
        public override string GetStatsDescription()
        {
            return $"Length: {currentBeamLength:F1}m\n" +
                   $"Width: {currentBeamWidth:F1}m\n" +
                   $"Damage: {currentDamage:F0}/tick\n" +
                   $"Beams: {currentBeamCount}";
        }
        
        private void OnDestroy()
        {
            foreach (var beam in activeBeams)
            {
                if (beam.visualObject != null)
                {
                    Destroy(beam.visualObject);
                }
            }
            activeBeams.Clear();
        }
        
        private void OnDisable()
        {
            foreach (var beam in activeBeams)
            {
                if (beam.visualObject != null)
                {
                    beam.visualObject.SetActive(false);
                }
            }
        }
        
        private void OnEnable()
        {
            foreach (var beam in activeBeams)
            {
                if (beam.visualObject != null)
                {
                    beam.visualObject.SetActive(true);
                }
            }
        }
    }
}
