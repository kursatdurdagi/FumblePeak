using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Defines a zone on an agent's body for hit detection without using colliders.
    /// Each zone has a height range (relative to agent's feet), radius, and damage multiplier.
    /// </summary>
    [System.Serializable]
    public class HitboxZone
    {
        [Tooltip("Name of this hit zone (for debugging and events)")]
        public string zoneName = "Body";
        
        [Tooltip("Minimum height of this zone relative to agent's feet (0 = ground level)")]
        [Min(0f)]
        public float minHeight = 0f;
        
        [Tooltip("Maximum height of this zone relative to agent's feet")]
        [Min(0f)]
        public float maxHeight = 1.8f;
        
        [Tooltip("Radius of this zone (horizontal distance from agent center)")]
        [Min(0.01f)]
        public float radius = 0.3f;
        
        [Tooltip("Damage multiplier for hits to this zone. 2.0 = double damage (critical), 0.5 = half damage")]
        [Range(0.1f, 10f)]
        public float damageMultiplier = 1f;
        
        [Tooltip("If true, hits to this zone trigger critical hit effects (different VFX, sound, etc.)")]
        public bool isCriticalZone = false;
        
        [Tooltip("Chance (0-1) for AI attacks to hit this zone. Used for random zone selection in AI vs AI combat. " +
                 "All zone chances should sum to ~1.0 for best results.")]
        [Range(0f, 1f)]
        public float hitChance = 0.5f;
        
        [Tooltip("Color used for debug gizmo visualization in Scene view")]
        public Color debugColor = new Color(1f, 1f, 0f, 0.3f);
        
        /// <summary>
        /// Check if a world point falls within this zone relative to the agent's position.
        /// </summary>
        /// <param name="hitPoint">World position of the hit</param>
        /// <param name="agentPosition">World position of the agent (feet/root)</param>
        /// <returns>True if the hit point is within this zone</returns>
        public bool ContainsPoint(Vector3 hitPoint, Vector3 agentPosition)
        {
            // Check height (Y axis relative to agent)
            float relativeHeight = hitPoint.y - agentPosition.y;
            if (relativeHeight < minHeight || relativeHeight > maxHeight)
                return false;
            
            // Check horizontal distance (XZ plane)
            Vector3 flatHitPoint = new Vector3(hitPoint.x, agentPosition.y, hitPoint.z);
            Vector3 flatAgentPos = new Vector3(agentPosition.x, agentPosition.y, agentPosition.z);
            float horizontalDistance = Vector3.Distance(flatHitPoint, flatAgentPos);
            
            return horizontalDistance <= radius;
        }
        
        /// <summary>
        /// Create a default head hitbox zone.
        /// </summary>
        public static HitboxZone CreateHeadZone()
        {
            return new HitboxZone
            {
                zoneName = "Head",
                minHeight = 1.5f,
                maxHeight = 1.9f,
                radius = 0.15f,
                damageMultiplier = 2f,
                isCriticalZone = true,
                hitChance = 0.1f, // 10% chance for AI to hit head
                debugColor = new Color(1f, 0f, 0f, 0.4f) // Red for critical
            };
        }
        
        /// <summary>
        /// Create a default torso hitbox zone.
        /// </summary>
        public static HitboxZone CreateTorsoZone()
        {
            return new HitboxZone
            {
                zoneName = "Torso",
                minHeight = 0.8f,
                maxHeight = 1.5f,
                radius = 0.25f,
                damageMultiplier = 1f,
                isCriticalZone = false,
                hitChance = 0.7f, // 70% chance for AI to hit torso
                debugColor = new Color(0f, 1f, 0f, 0.3f) // Green for normal
            };
        }
        
        /// <summary>
        /// Create a default legs hitbox zone.
        /// </summary>
        public static HitboxZone CreateLegsZone()
        {
            return new HitboxZone
            {
                zoneName = "Legs",
                minHeight = 0f,
                maxHeight = 0.8f,
                radius = 0.2f,
                damageMultiplier = 0.7f,
                isCriticalZone = false,
                hitChance = 0.2f, // 20% chance for AI to hit legs
                debugColor = new Color(0f, 0.5f, 1f, 0.3f) // Blue for reduced
            };
        }

        /// <summary>
        /// Create a center-mass sphere zone for non-humanoid entities (vehicles, spaceships, drones).
        /// Covers the full bounding volume with uniform damage — no head/torso/legs distinction.
        /// </summary>
        /// <param name="boundingRadius">Horizontal radius of the entity (meters).</param>
        /// <param name="boundingHeight">Total height of the entity (meters).</param>
        public static HitboxZone CreateCenterMassZone(float boundingRadius = 2f, float boundingHeight = 2f)
        {
            return new HitboxZone
            {
                zoneName = "Hull",
                minHeight = 0f,
                maxHeight = boundingHeight,
                radius = boundingRadius,
                damageMultiplier = 1f,
                isCriticalZone = false,
                hitChance = 1f, // 100% — single zone, always hit
                debugColor = new Color(1f, 0.5f, 0f, 0.3f) // Orange for vehicle
            };
        }
    }
    
    /// <summary>
    /// Configuration for virtual hitbox system that provides zone-based damage 
    /// without using physics colliders - optimized for mass combat scenarios.
    /// </summary>
    [System.Serializable]
    public class VirtualHitboxConfig
    {
        [Tooltip("Enable virtual hitbox system for zone-based damage (head = critical, etc.)")]
        public bool enabled = false;
        
        [Tooltip("Total height of the agent (used for calculations and default zone setup)")]
        [Min(0.5f)]
        public float agentHeight = 1.8f;
        
        [Tooltip("Hit zones defined for this agent type. Zones are checked from first to last - first matching zone wins.")]
        public HitboxZone[] hitZones = new HitboxZone[0];
        
        [Tooltip("Default damage multiplier when hit doesn't match any defined zone")]
        [Range(0.1f, 2f)]
        public float defaultDamageMultiplier = 1f;
        
        [Header("Visual Feedback")]
        [Tooltip("VFX prefab to spawn on critical hits (headshots, etc.)")]
        public GameObject criticalHitVFXPrefab;
        
        [Tooltip("Duration to keep critical hit VFX alive (0 = use particle system duration)")]
        [Range(0f, 3f)]
        public float criticalHitVFXDuration = 1f;
        
        [Tooltip("Sound to play on critical hits")]
        public AudioClip criticalHitSound;
        
        [Tooltip("Volume of critical hit sound")]
        [Range(0f, 1f)]
        public float criticalHitSoundVolume = 1f;
        
        [Header("Debug")]
        [Tooltip("Show hitbox zones as gizmos in Scene view (editor only)")]
        public bool showDebugGizmos = true;
        
        [Tooltip("Alpha transparency for debug gizmo spheres")]
        [Range(0f, 1f)]
        public float debugGizmoAlpha = 0.3f;
        
        // Cached total hit chance for normalization
        private float _cachedTotalHitChance = -1f;
        
        /// <summary>
        /// Initialize with default humanoid hitbox zones (head, torso, legs).
        /// </summary>
        public void InitializeDefaultZones()
        {
            hitZones = new HitboxZone[]
            {
                HitboxZone.CreateHeadZone(),
                HitboxZone.CreateTorsoZone(),
                HitboxZone.CreateLegsZone()
            };
        }

        /// <summary>
        /// Initialize with a single center-mass sphere hitbox suitable for vehicles, spaceships, drones.
        /// The sphere covers the full bounding volume with no critical/weak zones.
        /// </summary>
        /// <param name="boundingRadius">Radius of the vehicle bounding sphere.</param>
        /// <param name="boundingHeight">Height of the vehicle bounding volume.</param>
        public void InitializeVehicleZones(float boundingRadius = 2f, float boundingHeight = 2f)
        {
            agentHeight = boundingHeight;
            hitZones = new HitboxZone[]
            {
                HitboxZone.CreateCenterMassZone(boundingRadius, boundingHeight)
            };
        }

        /// <summary>
        /// Initializes hitbox zones from a <see cref="HitboxPreset"/> enum.
        /// Used by the setup wizard and EnemyCrowd configuration.
        /// </summary>
        /// <param name="preset">The hitbox preset to apply.</param>
        /// <param name="boundingRadius">Vehicle bounding radius (only used for Vehicle preset).</param>
        /// <param name="boundingHeight">Vehicle bounding height (only used for Vehicle preset).</param>
        public void InitializeFromPreset(HitboxPreset preset, float boundingRadius = 2f, float boundingHeight = 2f)
        {
            switch (preset)
            {
                case HitboxPreset.Humanoid:
                    InitializeDefaultZones();
                    break;
                case HitboxPreset.Vehicle:
                    InitializeVehicleZones(boundingRadius, boundingHeight);
                    break;
                case HitboxPreset.Custom:
                    // Don't override user-defined zones
                    break;
            }
        }
        
        /// <summary>
        /// Get the hitbox zone that contains the given hit point.
        /// </summary>
        /// <param name="hitPoint">World position of the hit</param>
        /// <param name="agentPosition">World position of the agent (feet/root)</param>
        /// <returns>The matching zone, or null if no zone contains the point</returns>
        public HitboxZone GetZoneAtPoint(Vector3 hitPoint, Vector3 agentPosition)
        {
            if (!enabled || hitZones == null || hitZones.Length == 0)
                return null;
            
            // Check zones in order (first match wins - allows priority)
            for (int i = 0; i < hitZones.Length; i++)
            {
                if (hitZones[i] != null && hitZones[i].ContainsPoint(hitPoint, agentPosition))
                    return hitZones[i];
            }
            
            return null;
        }
        
        /// <summary>
        /// Calculate the damage multiplier for a hit at the given point.
        /// </summary>
        /// <param name="hitPoint">World position of the hit</param>
        /// <param name="agentPosition">World position of the agent (feet/root)</param>
        /// <param name="hitZone">Output: the zone that was hit (null if default)</param>
        /// <returns>Damage multiplier to apply</returns>
        public float GetDamageMultiplier(Vector3 hitPoint, Vector3 agentPosition, out HitboxZone hitZone)
        {
            hitZone = GetZoneAtPoint(hitPoint, agentPosition);
            
            if (hitZone != null)
                return hitZone.damageMultiplier;
            
            return defaultDamageMultiplier;
        }
        
        /// <summary>
        /// Randomly select a hit zone based on configured hit chances.
        /// Ultra-performant for AI vs AI combat - just a random roll, no raycasts.
        /// </summary>
        /// <returns>The randomly selected zone, or null if no zones defined</returns>
        public HitboxZone GetRandomZoneByChance()
        {
            if (!enabled || hitZones == null || hitZones.Length == 0)
                return null;
            
            // Calculate total hit chance (cache for performance)
            if (_cachedTotalHitChance < 0f)
            {
                _cachedTotalHitChance = 0f;
                for (int i = 0; i < hitZones.Length; i++)
                {
                    if (hitZones[i] != null)
                        _cachedTotalHitChance += hitZones[i].hitChance;
                }
            }
            
            if (_cachedTotalHitChance <= 0f)
                return hitZones.Length > 0 ? hitZones[0] : null;
            
            // Roll the dice
            float roll = UnityEngine.Random.value * _cachedTotalHitChance;
            float cumulative = 0f;
            
            for (int i = 0; i < hitZones.Length; i++)
            {
                if (hitZones[i] == null) continue;
                
                cumulative += hitZones[i].hitChance;
                if (roll <= cumulative)
                    return hitZones[i];
            }
            
            // Fallback to last zone
            return hitZones[hitZones.Length - 1];
        }
        
        /// <summary>
        /// Get damage multiplier using random zone selection (for AI vs AI combat).
        /// </summary>
        /// <param name="hitZone">Output: the randomly selected zone</param>
        /// <returns>Damage multiplier from the selected zone</returns>
        public float GetRandomDamageMultiplier(out HitboxZone hitZone)
        {
            hitZone = GetRandomZoneByChance();
            
            if (hitZone != null)
                return hitZone.damageMultiplier;
            
            return defaultDamageMultiplier;
        }
        
        /// <summary>
        /// Get damage multiplier using random zone selection with per-agent modifiers.
        /// Used for AI vs AI combat with buff/debuff support (auras, spells, etc.)
        /// </summary>
        /// <param name="criticalChanceModifier">Multiplier for critical zone chances (from attacker buff)</param>
        /// <param name="hitZone">Output: the randomly selected zone</param>
        /// <returns>Damage multiplier from the selected zone</returns>
        public float GetRandomDamageMultiplierWithModifiers(float criticalChanceModifier, out HitboxZone hitZone)
        {
            hitZone = GetRandomZoneByChanceWithModifier(criticalChanceModifier);
            
            if (hitZone != null)
                return hitZone.damageMultiplier;
            
            return defaultDamageMultiplier;
        }
        
        /// <summary>
        /// Randomly select a hit zone with per-agent critical chance modifier.
        /// Allows buffs/auras to increase headshot chance for specific agents.
        /// </summary>
        /// <param name="criticalChanceModifier">Multiplier for critical zones (1 = normal, 2 = double crit chance)</param>
        /// <returns>The randomly selected zone</returns>
        public HitboxZone GetRandomZoneByChanceWithModifier(float criticalChanceModifier)
        {
            if (!enabled || hitZones == null || hitZones.Length == 0)
                return null;
            
            // Calculate modified total (can't use cache when modifier applied)
            float modifiedTotal = 0f;
            for (int i = 0; i < hitZones.Length; i++)
            {
                if (hitZones[i] == null) continue;
                float chance = hitZones[i].hitChance;
                if (hitZones[i].isCriticalZone)
                    chance *= criticalChanceModifier;
                modifiedTotal += chance;
            }
            
            if (modifiedTotal <= 0f)
                return hitZones.Length > 0 ? hitZones[0] : null;
            
            // Roll with modified chances
            float roll = UnityEngine.Random.value * modifiedTotal;
            float cumulative = 0f;
            
            for (int i = 0; i < hitZones.Length; i++)
            {
                if (hitZones[i] == null) continue;
                float chance = hitZones[i].hitChance;
                if (hitZones[i].isCriticalZone)
                    chance *= criticalChanceModifier;
                    
                cumulative += chance;
                if (roll <= cumulative)
                    return hitZones[i];
            }
            
            return hitZones[hitZones.Length - 1];
        }
        
        /// <summary>
        /// Invalidate cached hit chance total. Call this if you modify zone hit chances at runtime.
        /// </summary>
        public void InvalidateHitChanceCache()
        {
            _cachedTotalHitChance = -1f;
        }
        
        /// <summary>
        /// Estimate hit height based on attack type (melee vs ranged) and attacker position.
        /// Used when exact hit point isn't available.
        /// </summary>
        /// <param name="attackerPosition">Position of the attacker</param>
        /// <param name="targetPosition">Position of the target</param>
        /// <param name="isMeleeAttack">True if melee attack, false if ranged</param>
        /// <param name="attackHeightOffset">Height offset for the attack (e.g., sword swing height)</param>
        /// <returns>Estimated hit point in world space</returns>
        public static Vector3 EstimateHitPoint(Vector3 attackerPosition, Vector3 targetPosition, 
            bool isMeleeAttack, float attackHeightOffset = 1.0f)
        {
            // Direction from attacker to target
            Vector3 direction = (targetPosition - attackerPosition).normalized;
            
            // Estimate hit point based on attack type
            if (isMeleeAttack)
            {
                // Melee attacks typically hit at weapon height
                return new Vector3(
                    targetPosition.x,
                    targetPosition.y + attackHeightOffset,
                    targetPosition.z
                );
            }
            else
            {
                // Ranged attacks - use the attacker's aim height projected to target
                float attackerAimHeight = attackerPosition.y + attackHeightOffset;
                float horizontalDistance = Vector3.Distance(
                    new Vector3(attackerPosition.x, 0, attackerPosition.z),
                    new Vector3(targetPosition.x, 0, targetPosition.z)
                );
                
                // Simple linear trajectory estimation (no arc)
                float heightDifference = attackerAimHeight - (targetPosition.y + attackHeightOffset);
                float estimatedHitHeight = attackerAimHeight - (heightDifference * 0.5f);
                
                return new Vector3(
                    targetPosition.x,
                    Mathf.Clamp(estimatedHitHeight, targetPosition.y, targetPosition.y + 2f),
                    targetPosition.z
                );
            }
        }
    }
    
    /// <summary>
    /// Result of a hit zone detection, containing all relevant information.
    /// </summary>
    public struct HitZoneResult
    {
        /// <summary>The zone that was hit, or null for default body hit</summary>
        public HitboxZone zone;
        
        /// <summary>Damage multiplier from the hit zone</summary>
        public float damageMultiplier;
        
        /// <summary>Whether this hit is considered critical</summary>
        public bool isCritical;
        
        /// <summary>The world position where the hit occurred</summary>
        public Vector3 hitPoint;
        
        /// <summary>Name of the zone for logging/events</summary>
        public string zoneName => zone?.zoneName ?? "Body";
        
        /// <summary>Create a default (non-critical body) hit result</summary>
        public static HitZoneResult Default(Vector3 hitPoint)
        {
            return new HitZoneResult
            {
                zone = null,
                damageMultiplier = 1f,
                isCritical = false,
                hitPoint = hitPoint
            };
        }
    }
}
