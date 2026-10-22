using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Interface for server-authoritative transform synchronization.
    /// Handles position and rotation sync at a configurable tick rate.
    /// 
    /// Implement this interface to integrate with your networking solution's
    /// transform sync system (NetworkTransform, NetworkRigidbody, etc.).
    /// </summary>
    public interface INetworkTransformAuthority
    {
        /// <summary>
        /// Called each simulation tick with transform data for dirty agents.
        /// The network layer should batch these and send to clients.
        /// 
        /// Only called for agents that have moved since the last tick.
        /// </summary>
        /// <param name="snapshots">Array of transform snapshots to sync</param>
        void OnTransformBatchUpdate(TransformSnapshot[] snapshots);

        /// <summary>
        /// Called when receiving transform data from the network.
        /// Should be used to update agent positions with interpolation.
        /// </summary>
        /// <param name="snapshots">Received transform snapshots</param>
        /// <param name="serverTime">Server timestamp for this batch</param>
        void OnNetworkTransformReceived(TransformSnapshot[] snapshots, float serverTime);

        /// <summary>
        /// Returns the interpolated transform for a networked agent.
        /// Called during rendering to get smooth positions between network ticks.
        /// </summary>
        /// <param name="networkId">Network ID of the agent</param>
        /// <param name="renderTime">Current render time (with interpolation delay)</param>
        /// <param name="position">Output interpolated position</param>
        /// <param name="rotation">Output interpolated rotation</param>
        /// <returns>True if interpolated transform was available</returns>
        bool TryGetInterpolatedTransform(uint networkId, float renderTime, 
            out Vector3 position, out Quaternion rotation);

        /// <summary>
        /// Tick rate for position updates (updates per second).
        /// Higher values = smoother but more bandwidth. 20 is typical for RTS.
        /// </summary>
        int TransformSyncTickRate { get; }

        /// <summary>
        /// Interpolation delay in seconds. Higher values = smoother but more latency.
        /// Typical values: 0.05-0.1 seconds (50-100ms).
        /// </summary>
        float InterpolationDelay { get; }

        /// <summary>
        /// Whether to enable extrapolation when no new data is received.
        /// Useful for hiding packet loss but can cause rubber-banding.
        /// </summary>
        bool EnableExtrapolation { get; }

        /// <summary>
        /// Maximum extrapolation time in seconds before snapping to last known position.
        /// </summary>
        float MaxExtrapolationTime { get; }
    }

    /// <summary>
    /// Compact transform data for network sync.
    /// Designed for efficient batching and serialization.
    /// </summary>
    [System.Serializable]
    public struct TransformSnapshot
    {
        /// <summary>Network ID of the agent</summary>
        public uint networkId;
        
        /// <summary>World position</summary>
        public Vector3 position;
        
        /// <summary>Rotation (can be compressed to euler or quaternion)</summary>
        public Quaternion rotation;
        
        /// <summary>Current movement speed (for animation sync)</summary>
        public float speed;
        
        /// <summary>Server timestamp when this snapshot was taken</summary>
        public float timestamp;
        
        /// <summary>
        /// Delta flags indicating which fields changed since last sync.
        /// Bit 0: position changed
        /// Bit 1: rotation changed  
        /// Bit 2: speed changed
        /// </summary>
        public byte deltaFlags;

        // Delta flag constants
        public const byte DELTA_POSITION = 1 << 0;
        public const byte DELTA_ROTATION = 1 << 1;
        public const byte DELTA_SPEED = 1 << 2;
        public const byte DELTA_ALL = DELTA_POSITION | DELTA_ROTATION | DELTA_SPEED;

        public bool HasPositionChange => (deltaFlags & DELTA_POSITION) != 0;
        public bool HasRotationChange => (deltaFlags & DELTA_ROTATION) != 0;
        public bool HasSpeedChange => (deltaFlags & DELTA_SPEED) != 0;

        /// <summary>
        /// Creates a full snapshot with all fields marked as changed.
        /// </summary>
        public static TransformSnapshot CreateFull(uint networkId, Vector3 position, 
            Quaternion rotation, float speed, float timestamp)
        {
            return new TransformSnapshot
            {
                networkId = networkId,
                position = position,
                rotation = rotation,
                speed = speed,
                timestamp = timestamp,
                deltaFlags = DELTA_ALL
            };
        }
    }
}
