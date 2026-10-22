using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Server-side detection for speed/teleportation hacks.
    /// Tracks agent movement and flags suspicious position changes.
    /// 
    /// Usage:
    /// - Server creates one instance per game session
    /// - Call ValidateMovement() for each position update received
    /// - Agents exceeding speed limits accumulate violations
    /// - High violation counts trigger rejection or kick
    /// </summary>
    public class SpeedHackDetector
    {
        // Configuration
        private readonly float _baseSpeedTolerance;
        private readonly int _violationThreshold;
        private readonly float _violationDecayTime;
        private readonly float _teleportThreshold;
        
        // Per-agent tracking
        private readonly Dictionary<uint, AgentMovementTracker> _trackers = new();
        
        // Statistics
        private int _totalChecks;
        private int _violations;
        private int _teleportDetections;
        
        /// <summary>
        /// Creates a speed hack detector with specified tolerances.
        /// </summary>
        /// <param name="baseSpeedTolerance">Multiplier for max allowed speed (1.5 = 50% tolerance)</param>
        /// <param name="violationThreshold">Number of violations before flagging as cheater</param>
        /// <param name="violationDecayTime">Seconds before violations start decaying</param>
        /// <param name="teleportThreshold">Distance in one frame that's considered a teleport</param>
        public SpeedHackDetector(
            float baseSpeedTolerance = 1.5f,
            int violationThreshold = 5,
            float violationDecayTime = 10f,
            float teleportThreshold = 50f)
        {
            _baseSpeedTolerance = baseSpeedTolerance;
            _violationThreshold = violationThreshold;
            _violationDecayTime = violationDecayTime;
            _teleportThreshold = teleportThreshold;
        }

        /// <summary>
        /// Validates an agent's movement from their last known position.
        /// </summary>
        /// <param name="networkId">Network ID of the agent</param>
        /// <param name="newPosition">New position reported by client</param>
        /// <param name="maxSpeed">Maximum allowed speed for this agent type</param>
        /// <param name="result">Output detailed result</param>
        /// <returns>True if movement is valid, false if suspicious</returns>
        public bool ValidateMovement(uint networkId, Vector3 newPosition, float maxSpeed, 
            out MovementValidationResult result)
        {
            _totalChecks++;
            result = new MovementValidationResult { isValid = true };

            var tracker = GetOrCreateTracker(networkId);
            float currentTime = Time.time;

            // First position for this agent - just record it
            if (!tracker.hasLastPosition)
            {
                tracker.SetPosition(newPosition, currentTime);
                return true;
            }

            // Calculate movement
            float deltaTime = currentTime - tracker.lastUpdateTime;
            
            // Skip if no time has passed (same frame)
            if (deltaTime <= 0)
            {
                return true;
            }

            float distance = Vector3.Distance(tracker.lastPosition, newPosition);
            float actualSpeed = distance / deltaTime;

            // Teleport detection (instant large distance)
            if (distance > _teleportThreshold)
            {
                _teleportDetections++;
                tracker.AddViolation(currentTime, SpeedViolationType.Teleport);
                
                result.isValid = false;
                result.violationType = SpeedViolationType.Teleport;
                result.actualSpeed = actualSpeed;
                result.maxAllowedSpeed = maxSpeed * _baseSpeedTolerance;
                result.distance = distance;
                result.violationCount = tracker.violationCount;

                LogSuspicious(networkId, $"Teleport detected: {distance:F1}m in {deltaTime:F3}s");
                
                return false;
            }

            // Speed check
            float maxAllowedSpeed = maxSpeed * _baseSpeedTolerance;
            
            if (actualSpeed > maxAllowedSpeed)
            {
                _violations++;
                tracker.AddViolation(currentTime, SpeedViolationType.SpeedExceeded);
                
                result.isValid = tracker.violationCount < _violationThreshold;
                result.violationType = SpeedViolationType.SpeedExceeded;
                result.actualSpeed = actualSpeed;
                result.maxAllowedSpeed = maxAllowedSpeed;
                result.distance = distance;
                result.violationCount = tracker.violationCount;

                if (tracker.violationCount >= _violationThreshold)
                {
                    LogSuspicious(networkId, $"Speed hack threshold reached: {tracker.violationCount} violations. " +
                        $"Speed: {actualSpeed:F1} (max {maxAllowedSpeed:F1})");
                }

                // Still update position even if violation (prevents accumulation)
                tracker.SetPosition(newPosition, currentTime);
                return result.isValid;
            }

            // Valid movement - decay violations over time
            tracker.DecayViolations(currentTime, _violationDecayTime);
            tracker.SetPosition(newPosition, currentTime);
            
            return true;
        }

        /// <summary>
        /// Validates movement using server-known positions (more reliable).
        /// Call this when server has authoritative position data.
        /// </summary>
        /// <param name="networkId">Network ID of the agent</param>
        /// <param name="serverPosition">Server's authoritative position</param>
        /// <param name="clientPosition">Client's reported position</param>
        /// <param name="maxSpeed">Maximum allowed speed</param>
        /// <param name="serverDeltaTime">Time since last server update</param>
        /// <returns>True if positions are consistent</returns>
        public bool ValidateServerClientDelta(uint networkId, Vector3 serverPosition, Vector3 clientPosition,
            float maxSpeed, float serverDeltaTime)
        {
            float distance = Vector3.Distance(serverPosition, clientPosition);
            float maxDrift = maxSpeed * _baseSpeedTolerance * serverDeltaTime;
            
            // Allow some extra tolerance for network latency
            maxDrift += 2f; // 2 meter base tolerance
            
            if (distance > maxDrift)
            {
                var tracker = GetOrCreateTracker(networkId);
                tracker.AddViolation(Time.time, SpeedViolationType.PositionDesync);
                
                LogSuspicious(networkId, $"Position desync: {distance:F1}m drift (max {maxDrift:F1}m)");
                return false;
            }
            
            return true;
        }

        /// <summary>
        /// Gets the violation count for a specific agent.
        /// </summary>
        public int GetViolationCount(uint networkId)
        {
            return _trackers.TryGetValue(networkId, out var tracker) ? tracker.violationCount : 0;
        }

        /// <summary>
        /// Checks if an agent should be considered a cheater.
        /// </summary>
        public bool IsFlaggedAsCheater(uint networkId)
        {
            return GetViolationCount(networkId) >= _violationThreshold;
        }

        /// <summary>
        /// Resets tracking for an agent (call on respawn/teleport abilities).
        /// </summary>
        public void ResetTracking(uint networkId)
        {
            if (_trackers.TryGetValue(networkId, out var tracker))
            {
                tracker.Reset();
            }
        }

        /// <summary>
        /// Clears all tracking data for an agent (call on despawn).
        /// </summary>
        public void ClearTracker(uint networkId)
        {
            _trackers.Remove(networkId);
        }

        /// <summary>
        /// Gets detection statistics.
        /// </summary>
        public (int totalChecks, int violations, int teleports, float violationRate) GetStatistics()
        {
            float rate = _totalChecks > 0 ? (float)_violations / _totalChecks : 0f;
            return (_totalChecks, _violations, _teleportDetections, rate);
        }

        /// <summary>
        /// Resets all tracking data. Call between matches.
        /// </summary>
        public void Reset()
        {
            _trackers.Clear();
            _totalChecks = 0;
            _violations = 0;
            _teleportDetections = 0;
        }

        /// <summary>
        /// Resets statistics counters without clearing tracker data.
        /// </summary>
        public void ResetStatistics()
        {
            _totalChecks = 0;
            _violations = 0;
            _teleportDetections = 0;
        }

        /// <summary>
        /// Allows a legitimate teleport (ability, respawn, etc.)
        /// Call this BEFORE the teleport happens to prevent false positives.
        /// </summary>
        public void AllowTeleport(uint networkId, Vector3 targetPosition)
        {
            var tracker = GetOrCreateTracker(networkId);
            tracker.SetPosition(targetPosition, Time.time);
            tracker.allowNextLargeMovement = true;
        }

        private AgentMovementTracker GetOrCreateTracker(uint networkId)
        {
            if (!_trackers.TryGetValue(networkId, out var tracker))
            {
                tracker = new AgentMovementTracker();
                _trackers[networkId] = tracker;
            }
            return tracker;
        }

        private void LogSuspicious(uint networkId, string reason)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[SpeedHackDetector] Agent {networkId}: {reason}");
            #endif
        }

        /// <summary>
        /// Per-agent movement tracking.
        /// </summary>
        private class AgentMovementTracker
        {
            public Vector3 lastPosition;
            public float lastUpdateTime;
            public bool hasLastPosition;
            public int violationCount;
            public float lastViolationTime;
            public bool allowNextLargeMovement;

            public void SetPosition(Vector3 position, float time)
            {
                lastPosition = position;
                lastUpdateTime = time;
                hasLastPosition = true;
                allowNextLargeMovement = false;
            }

            public void AddViolation(float time, SpeedViolationType type)
            {
                if (allowNextLargeMovement && type == SpeedViolationType.Teleport)
                {
                    allowNextLargeMovement = false;
                    return; // Allowed teleport
                }
                
                violationCount++;
                lastViolationTime = time;
            }

            public void DecayViolations(float currentTime, float decayTime)
            {
                if (violationCount > 0 && currentTime - lastViolationTime > decayTime)
                {
                    violationCount = Mathf.Max(0, violationCount - 1);
                    lastViolationTime = currentTime;
                }
            }

            public void Reset()
            {
                hasLastPosition = false;
                violationCount = 0;
                allowNextLargeMovement = false;
            }
        }
    }

    /// <summary>
    /// Result of a movement validation check.
    /// </summary>
    public struct MovementValidationResult
    {
        /// <summary>Whether the movement was considered valid</summary>
        public bool isValid;
        
        /// <summary>Type of violation if invalid</summary>
        public SpeedViolationType violationType;
        
        /// <summary>Actual speed detected</summary>
        public float actualSpeed;
        
        /// <summary>Maximum speed allowed</summary>
        public float maxAllowedSpeed;
        
        /// <summary>Distance moved</summary>
        public float distance;
        
        /// <summary>Current violation count for this agent</summary>
        public int violationCount;
    }

    /// <summary>
    /// Type of speed-related violation.
    /// </summary>
    public enum SpeedViolationType
    {
        /// <summary>No violation</summary>
        None,
        /// <summary>Movement speed exceeded maximum</summary>
        SpeedExceeded,
        /// <summary>Instant large distance movement (teleport)</summary>
        Teleport,
        /// <summary>Client position doesn't match server prediction</summary>
        PositionDesync
    }
}
