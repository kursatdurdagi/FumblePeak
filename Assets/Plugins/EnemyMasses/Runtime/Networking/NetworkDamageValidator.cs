using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Server-side validation for damage requests in competitive multiplayer.
    /// Implements anti-cheat heuristics to detect and reject invalid damage.
    /// 
    /// Usage:
    /// - Server creates one instance per game session
    /// - All damage requests pass through ValidateDamageRequest()
    /// - Suspicious patterns are logged and rejected
    /// </summary>
    public class NetworkDamageValidator
    {
        // Configuration
        private readonly float _maxDamagePerHit;
        private readonly float _maxDPS;
        private readonly float _maxAttackRange;
        private readonly float _attackCooldown;
        private readonly float _rangeTolerance;
        private readonly float _timingTolerance;
        
        // Per-agent tracking
        private readonly System.Collections.Generic.Dictionary<uint, AgentDamageTracker> _trackers = new();
        
        // Statistics
        private int _totalRequests;
        private int _rejectedRequests;
        
        /// <summary>
        /// Creates a damage validator with specified limits.
        /// </summary>
        /// <param name="maxDamagePerHit">Maximum damage allowed per single hit</param>
        /// <param name="maxDPS">Maximum damage per second from a single attacker</param>
        /// <param name="maxAttackRange">Maximum valid attack range</param>
        /// <param name="attackCooldown">Minimum time between attacks from same attacker</param>
        /// <param name="rangeTolerance">Multiplier for range checks (1.2 = 20% tolerance)</param>
        /// <param name="timingTolerance">Seconds of tolerance for timing checks</param>
        public NetworkDamageValidator(
            float maxDamagePerHit = 1000f,
            float maxDPS = 500f,
            float maxAttackRange = 20f,
            float attackCooldown = 0.3f,
            float rangeTolerance = 1.5f,
            float timingTolerance = 0.2f)
        {
            _maxDamagePerHit = maxDamagePerHit;
            _maxDPS = maxDPS;
            _maxAttackRange = maxAttackRange;
            _attackCooldown = attackCooldown;
            _rangeTolerance = rangeTolerance;
            _timingTolerance = timingTolerance;
        }

        /// <summary>
        /// Validates a damage request and returns whether it should be processed.
        /// </summary>
        /// <param name="request">The damage request to validate</param>
        /// <param name="attackerPosition">Current server-known position of attacker</param>
        /// <param name="targetPosition">Current server-known position of target</param>
        /// <param name="attackerIsDead">Whether attacker is dead on server</param>
        /// <param name="targetIsDead">Whether target is dead on server</param>
        /// <param name="attackerFactionIndex">Faction of attacker</param>
        /// <param name="targetFactionIndex">Faction of target</param>
        /// <param name="rejectReason">Output reason if rejected</param>
        /// <returns>True if damage is valid and should be applied</returns>
        public bool ValidateDamageRequest(
            DamageRequest request,
            Vector3 attackerPosition,
            Vector3 targetPosition,
            bool attackerIsDead,
            bool targetIsDead,
            int attackerFactionIndex,
            int targetFactionIndex,
            out DamageRejectReason rejectReason)
        {
            _totalRequests++;
            rejectReason = DamageRejectReason.None;

            // Basic validation
            if (request.targetNetworkId == 0)
            {
                rejectReason = DamageRejectReason.InvalidTarget;
                _rejectedRequests++;
                return false;
            }

            if (targetIsDead)
            {
                rejectReason = DamageRejectReason.TargetAlreadyDead;
                _rejectedRequests++;
                return false;
            }

            // Attacker validation (if there is one)
            if (request.attackerNetworkId != 0)
            {
                if (attackerIsDead)
                {
                    rejectReason = DamageRejectReason.AttackerDead;
                    _rejectedRequests++;
                    LogSuspicious(request, "Dead attacker tried to deal damage");
                    return false;
                }

                // Friendly fire check (if not intended)
                if (attackerFactionIndex >= 0 && targetFactionIndex >= 0 && 
                    attackerFactionIndex == targetFactionIndex)
                {
                    rejectReason = DamageRejectReason.FriendlyFire;
                    _rejectedRequests++;
                    return false;
                }

                // Range check
                float distance = Vector3.Distance(attackerPosition, targetPosition);
                float maxAllowedRange = _maxAttackRange * _rangeTolerance;
                
                if (distance > maxAllowedRange)
                {
                    rejectReason = DamageRejectReason.OutOfRange;
                    _rejectedRequests++;
                    LogSuspicious(request, $"Attack from {distance:F1}m (max {maxAllowedRange:F1}m)");
                    return false;
                }

                // Attack rate limiting
                var tracker = GetOrCreateTracker(request.attackerNetworkId);
                
                if (!tracker.CanAttack(Time.time, _attackCooldown - _timingTolerance))
                {
                    rejectReason = DamageRejectReason.AttackTooFast;
                    _rejectedRequests++;
                    LogSuspicious(request, "Attack rate exceeded");
                    return false;
                }

                // DPS check
                if (!tracker.ValidateDPS(request.damage, Time.time, _maxDPS * 1.5f))
                {
                    rejectReason = DamageRejectReason.DPSExceeded;
                    _rejectedRequests++;
                    LogSuspicious(request, $"DPS limit exceeded ({tracker.GetCurrentDPS():F1} > {_maxDPS})");
                    return false;
                }

                tracker.RecordAttack(request.damage, Time.time);
            }

            // Damage amount validation
            if (request.damage <= 0)
            {
                rejectReason = DamageRejectReason.InvalidDamage;
                _rejectedRequests++;
                return false;
            }

            if (request.damage > _maxDamagePerHit * 2f) // Allow some tolerance for crits
            {
                rejectReason = DamageRejectReason.DamageExceeded;
                _rejectedRequests++;
                LogSuspicious(request, $"Damage {request.damage} exceeds max {_maxDamagePerHit}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Updates limits based on current game configuration.
        /// Call when enemy crowd settings change.
        /// </summary>
        public void UpdateLimits(float maxDamage, float attackRange, float attackCooldown)
        {
            // This method would update the cached limits from actual game settings
            // For now, use constructor values as defaults
        }

        /// <summary>
        /// Clears tracking data for a specific agent (call on death/despawn).
        /// </summary>
        public void ClearTracker(uint networkId)
        {
            _trackers.Remove(networkId);
        }

        /// <summary>
        /// Gets validation statistics for monitoring.
        /// </summary>
        public (int total, int rejected, float rejectRate) GetStatistics()
        {
            float rate = _totalRequests > 0 ? (float)_rejectedRequests / _totalRequests : 0f;
            return (_totalRequests, _rejectedRequests, rate);
        }

        /// <summary>
        /// Resets all tracking data. Call between matches.
        /// </summary>
        public void Reset()
        {
            _trackers.Clear();
            _totalRequests = 0;
            _rejectedRequests = 0;
        }

        /// <summary>
        /// Resets statistics counters without clearing tracker data.
        /// </summary>
        public void ResetStatistics()
        {
            _totalRequests = 0;
            _rejectedRequests = 0;
        }

        private AgentDamageTracker GetOrCreateTracker(uint networkId)
        {
            if (!_trackers.TryGetValue(networkId, out var tracker))
            {
                tracker = new AgentDamageTracker();
                _trackers[networkId] = tracker;
            }
            return tracker;
        }

        private void LogSuspicious(DamageRequest request, string reason)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[NetworkDamageValidator] Suspicious damage request from player {request.requestingPlayerId}: {reason}. " +
                $"Attacker: {request.attackerNetworkId}, Target: {request.targetNetworkId}, Damage: {request.damage}");
            #endif
        }

        /// <summary>
        /// Per-agent tracking for rate limiting and DPS validation.
        /// </summary>
        private class AgentDamageTracker
        {
            private float _lastAttackTime;
            private float _damageWindowStart;
            private float _damageInWindow;
            private const float WINDOW_SIZE = 3f;

            public bool CanAttack(float currentTime, float minCooldown)
            {
                return currentTime >= _lastAttackTime + minCooldown;
            }

            public bool ValidateDPS(float damage, float currentTime, float maxDPS)
            {
                // Reset window if expired
                if (currentTime - _damageWindowStart > WINDOW_SIZE)
                {
                    _damageWindowStart = currentTime;
                    _damageInWindow = 0;
                }

                float windowDuration = Mathf.Max(0.1f, currentTime - _damageWindowStart);
                float projectedDPS = (_damageInWindow + damage) / windowDuration;
                
                return projectedDPS <= maxDPS;
            }

            public void RecordAttack(float damage, float currentTime)
            {
                _lastAttackTime = currentTime;
                
                if (currentTime - _damageWindowStart > WINDOW_SIZE)
                {
                    _damageWindowStart = currentTime;
                    _damageInWindow = damage;
                }
                else
                {
                    _damageInWindow += damage;
                }
            }

            public float GetCurrentDPS()
            {
                float windowDuration = Mathf.Max(0.1f, Time.time - _damageWindowStart);
                return _damageInWindow / windowDuration;
            }
        }
    }

    /// <summary>
    /// Damage request data for server validation.
    /// </summary>
    [System.Serializable]
    public struct DamageRequest
    {
        /// <summary>Network ID of the target</summary>
        public uint targetNetworkId;
        
        /// <summary>Network ID of the attacker (0 if non-agent damage)</summary>
        public uint attackerNetworkId;
        
        /// <summary>Requested damage amount</summary>
        public float damage;
        
        /// <summary>Hit position in world space</summary>
        public Vector3 hitPoint;
        
        /// <summary>Type of damage</summary>
        public DamageType damageType;
        
        /// <summary>Player ID making the request</summary>
        public int requestingPlayerId;
        
        /// <summary>Sequence number for tracking</summary>
        public int sequence;
        
        /// <summary>Client timestamp</summary>
        public float clientTime;
    }

    /// <summary>
    /// Reasons a damage request can be rejected.
    /// </summary>
    public enum DamageRejectReason
    {
        /// <summary>Request was valid</summary>
        None,
        /// <summary>Target agent not found or invalid</summary>
        InvalidTarget,
        /// <summary>Target was already dead</summary>
        TargetAlreadyDead,
        /// <summary>Attacker is dead</summary>
        AttackerDead,
        /// <summary>Attacking allied unit</summary>
        FriendlyFire,
        /// <summary>Target out of attack range</summary>
        OutOfRange,
        /// <summary>Attacking faster than possible</summary>
        AttackTooFast,
        /// <summary>Damage per second exceeded limits</summary>
        DPSExceeded,
        /// <summary>Damage amount invalid (negative or zero)</summary>
        InvalidDamage,
        /// <summary>Damage amount too high</summary>
        DamageExceeded,
        /// <summary>Player doesn't own attacker</summary>
        NotOwner,
        /// <summary>Other validation failure</summary>
        Other
    }
}
