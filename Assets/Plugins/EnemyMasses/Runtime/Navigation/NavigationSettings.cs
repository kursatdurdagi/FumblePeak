using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration settings for the Enemy Masses navigation system.
    /// </summary>
    [System.Serializable]
    public class NavigationSettings
    {
        [Header("Destination")]
        [Tooltip("Spread radius for random destination generation")]
        [Min(0f)]
        public float destinationSpread = 25f;
        
        [Header("Destination Throttling")]
        [Tooltip("Distribute new destination assignments across multiple frames")]
        public bool throttleDestinationUpdates = true;
        
        [Tooltip("Maximum destination updates per frame")]
        [Min(1)]
        public int maxDestinationUpdatesPerFrame = 256;
        
        [Tooltip("Extra destination updates reserved for visible agents")]
        [Min(0)]
        public int priorityDestinationBonus = 128;
        
        [Header("Navigation Sync")]
        [Tooltip("Seconds between NavMesh syncs for offscreen agents")]
        [Min(0.01f)]
        public float offscreenNavSyncInterval = 0.25f;
        
        [Tooltip("Lerp factor used when aligning agent rotation to its velocity")]
        [Range(0f, 1f)]
        public float navmeshRotationLerp = 0.15f;
        
        [Header("Stuck Recovery")]
        [Tooltip("Enable stuck recovery system")]
        public bool enableStuckRecovery = true;
        
        [Tooltip("Teleport stuck agents to destination")]
        public bool teleportStuckAgents = true;
        
        [Tooltip("Seconds an agent can stagnate before force-move")]
        [Min(0.5f)]
        public float stuckTimeout = 6f;
        
        [Tooltip("Minimum distance improvement to count as progress")]
        [Min(0f)]
        public float stuckProximitySlack = 0.5f;
        
        [Tooltip("Velocity magnitude considered as movement")]
        [Min(0f)]
        public float stuckVelocityThreshold = 0.15f;
        
        [Tooltip("Retry interval when teleport is disabled")]
        [Min(0.5f)]
        public float stuckRetryInterval = 4f;
        
        [Tooltip("Maximum retry attempts before giving up")]
        [Min(0)]
        public int stuckRetryAttempts = 3;
        
        [Tooltip("Teleport after retries are exhausted")]
        public bool teleportAfterRetries = false;
        
        [Header("Arrival Sync")]
        [Tooltip("Automatically stop nearby agents when one reaches destination")]
        public bool enableArrivalSync = true;
        
        [Tooltip("Maximum distance to sync arrivals")]
        [Min(0f)]
        public float arrivalSyncDistance = 5f;
        
        [Tooltip("Reapply formation when all agents arrive")]
        public bool reapplyFormationOnArrival = false;
    }
}
