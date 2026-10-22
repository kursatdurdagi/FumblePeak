using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill that spawns objects/particles that orbit around the player.
    /// Objects deal damage to enemies they pass through.
    /// Examples: Bible (Vampire Survivors), Rotating Swords, Protective Orbs
    /// </summary>
    public class OrbitingSkill : BaseSkill
    {
        [Header("Orbiting Settings")]
        [Tooltip("Number of orbiting objects")]
        [SerializeField] private int objectCount = 3;
        
        [Tooltip("Additional objects per level")]
        [SerializeField] private int objectsPerLevel = 1;
        
        [Tooltip("Orbit radius around player")]
        [SerializeField] private float orbitRadius = 2f;
        
        [Tooltip("Orbit radius increase per level")]
        [SerializeField] private float radiusPerLevel = 0.2f;
        
        [Tooltip("Rotation speed in degrees per second")]
        [SerializeField] private float rotationSpeed = 180f;
        
        [Tooltip("Rotation speed increase per level")]
        [SerializeField] private float rotationSpeedPerLevel = 15f;
        
        [Tooltip("Should objects rotate around their own axis")]
        [SerializeField] private bool selfRotate = true;
        
        [Tooltip("Self rotation speed multiplier")]
        [SerializeField] private float selfRotationMultiplier = 2f;
        
        [Header("Damage Settings")]
        [Tooltip("Damage radius of each orbiting object")]
        [SerializeField] private float damageRadius = 0.5f;
        
        [Tooltip("Damage radius increase per level")]
        [SerializeField] private float damageRadiusPerLevel = 0.1f;
        
        [Tooltip("Minimum time between hits on the same enemy (per orbit object)")]
        [SerializeField] private float hitCooldownPerEnemy = 0.5f;
        
        [Header("Orbit Style")]
        [Tooltip("Vertical offset from player")]
        [SerializeField] private float verticalOffset = 1f;
        
        [Tooltip("Should objects bob up and down")]
        [SerializeField] private bool enableBobbing = false;
        
        [Tooltip("Bobbing amplitude")]
        [SerializeField] private float bobbingAmplitude = 0.2f;
        
        [Tooltip("Bobbing frequency")]
        [SerializeField] private float bobbingFrequency = 2f;
        
        [Tooltip("Should orbit be elliptical")]
        [SerializeField] private bool ellipticalOrbit = false;
        
        [Tooltip("Ellipse ratio (width/height)")]
        [SerializeField] private float ellipseRatio = 0.7f;
        
        // Runtime
        private List<GameObject> orbitingObjects = new List<GameObject>();
        private float currentAngle = 0f;
        private int currentObjectCount;
        private float currentOrbitRadius;
        private float currentRotationSpeed;
        private float currentDamageRadius;
        
        // Hit tracking per object
        private Dictionary<int, Dictionary<int, float>> objectHitTimers = new Dictionary<int, Dictionary<int, float>>();
        
        protected override void Awake()
        {
            base.Awake();
            baseCooldown = 0f; // Orbiting skills are always active
        }
        
        public override void RecalculateStats()
        {
            base.RecalculateStats();
            
            currentObjectCount = objectCount + (objectsPerLevel * (currentLevel - 1));
            currentOrbitRadius = orbitRadius + (radiusPerLevel * (currentLevel - 1));
            currentRotationSpeed = rotationSpeed + (rotationSpeedPerLevel * (currentLevel - 1));
            currentDamageRadius = damageRadius + (damageRadiusPerLevel * (currentLevel - 1));
            
            // Update orbit objects if already spawned
            UpdateOrbitObjectCount();
        }
        
        protected override void OnActivate()
        {
            // Only spawn orbiting objects if we don't have any yet
            // Orbiting objects are persistent, not respawned each activation
            if (orbitingObjects.Count == 0)
            {
                SpawnOrbitObjects();
            }
        }
        
        protected override void UpdateSkillLogic()
        {
            if (playerTransform == null) return;
            
            // Update orbit angle
            currentAngle += currentRotationSpeed * Time.deltaTime;
            if (currentAngle > 360f) currentAngle -= 360f;
            
            // Update each orbiting object position
            UpdateOrbitPositions();
            
            // Check for damage
            CheckDamage();
        }
        
        private void SpawnOrbitObjects()
        {
            if (visualPrefab == null)
            {
                Debug.LogWarning($"[OrbitingSkill] {skillName}: No visual prefab assigned!");
                return;
            }
            
            for (int i = 0; i < currentObjectCount; i++)
            {
                SpawnOrbitObject(i);
            }
        }
        
        private void SpawnOrbitObject(int index)
        {
            var obj = Instantiate(visualPrefab, transform);
            obj.name = $"{skillName}_Orbit_{index}";
            orbitingObjects.Add(obj);
            objectHitTimers[index] = new Dictionary<int, float>();
        }
        
        private void UpdateOrbitObjectCount()
        {
            // Add new objects if level increased
            while (orbitingObjects.Count < currentObjectCount)
            {
                SpawnOrbitObject(orbitingObjects.Count);
            }
            
            // Remove excess objects if somehow we have too many
            while (orbitingObjects.Count > currentObjectCount)
            {
                int lastIndex = orbitingObjects.Count - 1;
                if (orbitingObjects[lastIndex] != null)
                {
                    Destroy(orbitingObjects[lastIndex]);
                }
                orbitingObjects.RemoveAt(lastIndex);
                objectHitTimers.Remove(lastIndex);
            }
        }
        
        private void UpdateOrbitPositions()
        {
            if (playerTransform == null) return;
            
            Vector3 playerPos = playerTransform.position;
            float angleStep = 360f / currentObjectCount;
            
            for (int i = 0; i < orbitingObjects.Count; i++)
            {
                if (orbitingObjects[i] == null) continue;
                
                float angle = (currentAngle + angleStep * i) * Mathf.Deg2Rad;
                
                // Calculate position
                float radiusX = currentOrbitRadius;
                float radiusZ = ellipticalOrbit ? currentOrbitRadius * ellipseRatio : currentOrbitRadius;
                
                float x = Mathf.Cos(angle) * radiusX;
                float z = Mathf.Sin(angle) * radiusZ;
                float y = verticalOffset;
                
                // Apply bobbing
                if (enableBobbing)
                {
                    float bobOffset = Mathf.Sin((Time.time + i * 0.5f) * bobbingFrequency) * bobbingAmplitude;
                    y += bobOffset;
                }
                
                Vector3 localPos = new Vector3(x, y, z);
                orbitingObjects[i].transform.position = playerPos + localPos;
                
                // Self rotation
                if (selfRotate)
                {
                    float selfRotSpeed = currentRotationSpeed * selfRotationMultiplier;
                    orbitingObjects[i].transform.Rotate(Vector3.up, selfRotSpeed * Time.deltaTime);
                }
            }
        }
        
        private void CheckDamage()
        {
            if (skillManager == null) return;
            
            for (int objIndex = 0; objIndex < orbitingObjects.Count; objIndex++)
            {
                if (orbitingObjects[objIndex] == null) continue;
                
                Vector3 objPos = orbitingObjects[objIndex].transform.position;
                var agentsInRange = GetAgentsInRange(objPos, currentDamageRadius);
                
                foreach (int agentIndex in agentsInRange)
                {
                    // Check hit cooldown for this object against this enemy
                    if (objectHitTimers.TryGetValue(objIndex, out var hitTimers))
                    {
                        if (hitTimers.TryGetValue(agentIndex, out float lastHit))
                        {
                            if (Time.time - lastHit < hitCooldownPerEnemy)
                            {
                                continue; // Still on cooldown
                            }
                        }
                    }
                    
                    // Deal damage
                    DealDamageToAgent(agentIndex, currentDamage);
                    SpawnHitEffect(skillManager.GetAgentPosition(agentIndex));
                    
                    // Record hit time
                    if (!objectHitTimers.ContainsKey(objIndex))
                    {
                        objectHitTimers[objIndex] = new Dictionary<int, float>();
                    }
                    objectHitTimers[objIndex][agentIndex] = Time.time;
                }
            }
            
            // Clean up old hit timers
            CleanupHitTimers();
        }
        
        private void CleanupHitTimers()
        {
            float cleanupThreshold = hitCooldownPerEnemy * 2f;
            
            foreach (var objTimers in objectHitTimers.Values)
            {
                var keysToRemove = new List<int>();
                foreach (var kvp in objTimers)
                {
                    if (Time.time - kvp.Value > cleanupThreshold)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    objTimers.Remove(key);
                }
            }
        }
        
        protected override void OnLevelUp()
        {
            base.OnLevelUp();
            UpdateOrbitObjectCount();
        }
        
        public override string GetStatsDescription()
        {
            return $"Objects: {currentObjectCount}\n" +
                   $"Damage: {currentDamage:F0}\n" +
                   $"Radius: {currentOrbitRadius:F1}m\n" +
                   $"Speed: {currentRotationSpeed:F0}°/s";
        }
        
        public override string GetNextLevelDescription()
        {
            if (IsMaxLevel) return "MAX LEVEL";
            
            int nextObjects = objectCount + (objectsPerLevel * currentLevel);
            float nextDamage = baseDamage + (damagePerLevel * currentLevel);
            float nextRadius = orbitRadius + (radiusPerLevel * currentLevel);
            
            return $"Objects: {nextObjects} (+{objectsPerLevel})\n" +
                   $"Damage: {nextDamage:F0} (+{damagePerLevel:F0})\n" +
                   $"Radius: {nextRadius:F1}m";
        }
        
        private void OnDestroy()
        {
            // Cleanup spawned objects
            foreach (var obj in orbitingObjects)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            orbitingObjects.Clear();
        }
        
        private void OnDisable()
        {
            // Hide orbiting objects when skill is disabled
            foreach (var obj in orbitingObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                }
            }
        }
        
        private void OnEnable()
        {
            // Show orbiting objects when skill is enabled
            foreach (var obj in orbitingObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(true);
                }
            }
        }
    }
}
