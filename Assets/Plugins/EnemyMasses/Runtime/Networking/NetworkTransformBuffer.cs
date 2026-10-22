using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Ring buffer for storing transform snapshots for network interpolation.
    /// Used on clients to smooth out network jitter and provide seamless movement.
    /// 
    /// Usage:
    /// 1. Call AddSnapshot() when receiving network data
    /// 2. Call Interpolate() each frame to get smooth position
    /// </summary>
    public class NetworkTransformBuffer
    {
        private readonly TransformSnapshot[] _buffer;
        private readonly int _capacity;
        private int _head;
        private int _count;
        private float _interpolationDelay;

        /// <summary>
        /// Creates a new transform buffer.
        /// </summary>
        /// <param name="capacity">Number of snapshots to store (typically 10-20)</param>
        /// <param name="interpolationDelay">Delay in seconds for interpolation (typically 0.1)</param>
        public NetworkTransformBuffer(int capacity = 16, float interpolationDelay = 0.1f)
        {
            _capacity = capacity;
            _buffer = new TransformSnapshot[capacity];
            _interpolationDelay = interpolationDelay;
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Gets or sets the interpolation delay in seconds.
        /// </summary>
        public float InterpolationDelay
        {
            get => _interpolationDelay;
            set => _interpolationDelay = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Number of snapshots currently stored.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Whether the buffer has enough data for interpolation.
        /// </summary>
        public bool HasEnoughData => _count >= 2;

        /// <summary>
        /// The oldest timestamp in the buffer.
        /// </summary>
        public float OldestTimestamp => _count > 0 ? GetSnapshot(0).timestamp : 0f;

        /// <summary>
        /// The newest timestamp in the buffer.
        /// </summary>
        public float NewestTimestamp => _count > 0 ? GetSnapshot(_count - 1).timestamp : 0f;

        /// <summary>
        /// Adds a new snapshot to the buffer.
        /// </summary>
        public void AddSnapshot(TransformSnapshot snapshot)
        {
            // Fast-path: most network snapshots arrive in order, so the
            // insertion point is almost always at the tail.
            if (_count > 0 && snapshot.timestamp >= GetSnapshot(_count - 1).timestamp)
            {
                // Append at tail — no scan or shift needed.
                if (_count >= _capacity)
                {
                    // Remove oldest
                    _head = (_head + 1) % _capacity;
                    _count--;
                }
                int actualIndex = (_head + _count) % _capacity;
                _buffer[actualIndex] = snapshot;
                _count++;
                return;
            }

            // Slow path: out-of-order packet, find correct insertion point.
            int insertIndex = _count;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (GetSnapshot(i).timestamp <= snapshot.timestamp)
                {
                    insertIndex = i + 1;
                    break;
                }
                insertIndex = i;
            }

            // If buffer is full and new snapshot is oldest, skip it
            if (_count >= _capacity && insertIndex == 0)
                return;

            // Make room if needed
            if (_count >= _capacity)
            {
                // Remove oldest
                _head = (_head + 1) % _capacity;
                _count--;
                insertIndex--;
            }

            // Insert at the correct position
            if (insertIndex < _count)
            {
                // Shift newer snapshots forward
                for (int i = _count - 1; i >= insertIndex; i--)
                {
                    int srcIdx = (_head + i) % _capacity;
                    int dstIdx = (_head + i + 1) % _capacity;
                    _buffer[dstIdx] = _buffer[srcIdx];
                }
            }

            int slowIdx = (_head + insertIndex) % _capacity;
            _buffer[slowIdx] = snapshot;
            _count++;
        }

        /// <summary>
        /// Gets an interpolated transform for the given render time.
        /// </summary>
        /// <param name="currentTime">Current time (typically Time.time)</param>
        /// <param name="position">Output interpolated position</param>
        /// <param name="rotation">Output interpolated rotation</param>
        /// <param name="speed">Output interpolated speed</param>
        /// <returns>True if interpolation was successful</returns>
        public bool Interpolate(float currentTime, out Vector3 position, out Quaternion rotation, out float speed)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            speed = 0f;

            if (_count < 2)
            {
                if (_count == 1)
                {
                    var snap = GetSnapshot(0);
                    position = snap.position;
                    rotation = snap.rotation;
                    speed = snap.speed;
                    return true;
                }
                return false;
            }

            float renderTime = currentTime - _interpolationDelay;

            // Find the two snapshots to interpolate between.
            // Search backward from newest — the render time is usually near the
            // end of the buffer (newest data minus interpolation delay).
            int beforeIndex = -1;
            int afterIndex = -1;

            for (int i = _count - 2; i >= 0; i--)
            {
                var current = GetSnapshot(i);
                var next = GetSnapshot(i + 1);

                if (current.timestamp <= renderTime && next.timestamp >= renderTime)
                {
                    beforeIndex = i;
                    afterIndex = i + 1;
                    break;
                }
            }

            // Handle edge cases
            if (beforeIndex == -1)
            {
                // Render time is before all snapshots - use oldest
                if (renderTime < GetSnapshot(0).timestamp)
                {
                    var snap = GetSnapshot(0);
                    position = snap.position;
                    rotation = snap.rotation;
                    speed = snap.speed;
                    return true;
                }
                
                // Render time is after all snapshots - extrapolate from newest
                beforeIndex = _count - 2;
                afterIndex = _count - 1;
            }

            var before = GetSnapshot(beforeIndex);
            var after = GetSnapshot(afterIndex);

            // Calculate interpolation factor
            float duration = after.timestamp - before.timestamp;
            float t = duration > 0.0001f ? (renderTime - before.timestamp) / duration : 0f;
            t = Mathf.Clamp01(t);

            // Interpolate
            position = Vector3.Lerp(before.position, after.position, t);
            rotation = Quaternion.Slerp(before.rotation, after.rotation, t);
            speed = Mathf.Lerp(before.speed, after.speed, t);

            return true;
        }

        /// <summary>
        /// Gets an extrapolated transform when no recent data is available.
        /// </summary>
        /// <param name="currentTime">Current time</param>
        /// <param name="maxExtrapolationTime">Maximum time to extrapolate</param>
        /// <param name="position">Output extrapolated position</param>
        /// <param name="rotation">Output extrapolated rotation</param>
        /// <param name="speed">Output speed</param>
        /// <returns>True if extrapolation was possible</returns>
        public bool Extrapolate(float currentTime, float maxExtrapolationTime, 
            out Vector3 position, out Quaternion rotation, out float speed)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            speed = 0f;

            if (_count < 2)
                return false;

            var newest = GetSnapshot(_count - 1);
            var previous = GetSnapshot(_count - 2);

            float extrapolationTime = currentTime - _interpolationDelay - newest.timestamp;
            
            if (extrapolationTime > maxExtrapolationTime)
            {
                // Too much time has passed, just return last known position
                position = newest.position;
                rotation = newest.rotation;
                speed = newest.speed;
                return true;
            }

            // Calculate velocity from last two snapshots
            float dt = newest.timestamp - previous.timestamp;
            if (dt <= 0.0001f)
            {
                position = newest.position;
                rotation = newest.rotation;
                speed = newest.speed;
                return true;
            }

            Vector3 velocity = (newest.position - previous.position) / dt;
            
            // Extrapolate position
            position = newest.position + velocity * extrapolationTime;
            rotation = newest.rotation; // Don't extrapolate rotation
            speed = newest.speed;

            return true;
        }

        /// <summary>
        /// Clears all snapshots from the buffer.
        /// </summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Gets a snapshot by logical index (0 = oldest, count-1 = newest).
        /// </summary>
        private TransformSnapshot GetSnapshot(int logicalIndex)
        {
            int actualIndex = (_head + logicalIndex) % _capacity;
            return _buffer[actualIndex];
        }
    }
}
