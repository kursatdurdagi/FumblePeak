using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Server-side validation for RTS command requests in competitive multiplayer.
    /// Validates ownership, command limits, and detects command flooding.
    /// 
    /// Usage:
    /// - Server creates one instance per game session
    /// - All command requests pass through ValidateCommand()
    /// - Invalid commands are rejected with reasons
    /// </summary>
    public class NetworkCommandValidator
    {
        // Configuration
        private readonly int _maxCommandsPerSecond;
        private readonly int _maxAgentsPerCommand;
        private readonly float _maxCommandRange;
        
        // Per-player tracking
        private readonly Dictionary<int, PlayerCommandTracker> _playerTrackers = new();
        
        // Agent ownership lookup (provided by game)
        private System.Func<uint, int> _getAgentOwner;
        private System.Func<uint, Vector3> _getAgentPosition;
        private System.Func<uint, bool> _isAgentValid;
        
        // Statistics
        private int _totalCommands;
        private int _rejectedCommands;
        
        /// <summary>
        /// Creates a command validator with specified limits.
        /// </summary>
        /// <param name="maxCommandsPerSecond">Maximum commands a player can issue per second</param>
        /// <param name="maxAgentsPerCommand">Maximum agents in a single command</param>
        /// <param name="maxCommandRange">Maximum distance for move commands</param>
        public NetworkCommandValidator(
            int maxCommandsPerSecond = 30,
            int maxAgentsPerCommand = 200,
            float maxCommandRange = 500f)
        {
            _maxCommandsPerSecond = maxCommandsPerSecond;
            _maxAgentsPerCommand = maxAgentsPerCommand;
            _maxCommandRange = maxCommandRange;
        }

        /// <summary>
        /// Sets the callback functions for looking up agent data.
        /// Must be called before validation can work.
        /// </summary>
        /// <param name="getAgentOwner">Returns owner player ID for a network ID (-1 if not found)</param>
        /// <param name="getAgentPosition">Returns world position for a network ID</param>
        /// <param name="isAgentValid">Returns true if network ID is valid and alive</param>
        public void SetLookupCallbacks(
            System.Func<uint, int> getAgentOwner,
            System.Func<uint, Vector3> getAgentPosition,
            System.Func<uint, bool> isAgentValid)
        {
            _getAgentOwner = getAgentOwner;
            _getAgentPosition = getAgentPosition;
            _isAgentValid = isAgentValid;
        }

        /// <summary>
        /// Validates a move command request.
        /// </summary>
        public bool ValidateMoveCommand(MoveCommandData command, out CommandRejectReason rejectReason)
        {
            _totalCommands++;
            rejectReason = CommandRejectReason.Other; // Default to Other when valid

            // Basic validation
            if (command.agentNetworkIds == null || command.agentNetworkIds.Length == 0)
            {
                rejectReason = CommandRejectReason.InvalidAgents;
                _rejectedCommands++;
                return false;
            }

            // Agent count limit
            if (command.agentNetworkIds.Length > _maxAgentsPerCommand)
            {
                rejectReason = CommandRejectReason.RuleViolation;
                _rejectedCommands++;
                LogSuspicious(command.playerId, $"Move command with {command.agentNetworkIds.Length} agents (max {_maxAgentsPerCommand})");
                return false;
            }

            // Rate limiting
            var tracker = GetOrCreatePlayerTracker(command.playerId);
            if (!tracker.CanIssueCommand(Time.time, _maxCommandsPerSecond))
            {
                rejectReason = CommandRejectReason.RateLimited;
                _rejectedCommands++;
                return false;
            }

            // Ownership validation
            if (_getAgentOwner != null)
            {
                foreach (uint networkId in command.agentNetworkIds)
                {
                    int owner = _getAgentOwner(networkId);
                    if (owner != command.playerId)
                    {
                        rejectReason = CommandRejectReason.NotOwner;
                        _rejectedCommands++;
                        LogSuspicious(command.playerId, $"Tried to command agent {networkId} owned by player {owner}");
                        return false;
                    }

                    if (_isAgentValid != null && !_isAgentValid(networkId))
                    {
                        rejectReason = CommandRejectReason.InvalidAgents;
                        _rejectedCommands++;
                        return false;
                    }
                }
            }

            // Distance validation
            if (_maxCommandRange > 0)
            {
                // Check if destination is reasonable (not absurdly far)
                if (_getAgentPosition != null && command.agentNetworkIds.Length > 0)
                {
                    Vector3 firstAgentPos = _getAgentPosition(command.agentNetworkIds[0]);
                    float distance = Vector3.Distance(firstAgentPos, command.destination);
                    
                    if (distance > _maxCommandRange)
                    {
                        rejectReason = CommandRejectReason.UnreachableDestination;
                        _rejectedCommands++;
                        LogSuspicious(command.playerId, $"Move command to {distance:F0}m away (max {_maxCommandRange:F0}m)");
                        return false;
                    }
                }
            }

            tracker.RecordCommand(Time.time);
            return true;
        }

        /// <summary>
        /// Validates an attack command request.
        /// </summary>
        public bool ValidateAttackCommand(AttackCommandData command, out CommandRejectReason rejectReason)
        {
            _totalCommands++;
            rejectReason = CommandRejectReason.Other;

            // Basic validation
            if (command.attackerNetworkIds == null || command.attackerNetworkIds.Length == 0)
            {
                rejectReason = CommandRejectReason.InvalidAgents;
                _rejectedCommands++;
                return false;
            }

            if (command.targetNetworkId == 0)
            {
                rejectReason = CommandRejectReason.InvalidTarget;
                _rejectedCommands++;
                return false;
            }

            // Agent count limit
            if (command.attackerNetworkIds.Length > _maxAgentsPerCommand)
            {
                rejectReason = CommandRejectReason.RuleViolation;
                _rejectedCommands++;
                return false;
            }

            // Rate limiting
            var tracker = GetOrCreatePlayerTracker(command.playerId);
            if (!tracker.CanIssueCommand(Time.time, _maxCommandsPerSecond))
            {
                rejectReason = CommandRejectReason.RateLimited;
                _rejectedCommands++;
                return false;
            }

            // Ownership validation
            if (_getAgentOwner != null)
            {
                foreach (uint networkId in command.attackerNetworkIds)
                {
                    int owner = _getAgentOwner(networkId);
                    if (owner != command.playerId)
                    {
                        rejectReason = CommandRejectReason.NotOwner;
                        _rejectedCommands++;
                        LogSuspicious(command.playerId, $"Tried to command agent {networkId} owned by player {owner}");
                        return false;
                    }
                }

                // Validate target isn't friendly
                int targetOwner = _getAgentOwner(command.targetNetworkId);
                if (targetOwner == command.playerId)
                {
                    rejectReason = CommandRejectReason.RuleViolation; // Friendly fire
                    _rejectedCommands++;
                    return false;
                }
            }

            // Target validation
            if (_isAgentValid != null && !_isAgentValid(command.targetNetworkId))
            {
                rejectReason = CommandRejectReason.InvalidTarget;
                _rejectedCommands++;
                return false;
            }

            tracker.RecordCommand(Time.time);
            return true;
        }

        /// <summary>
        /// Validates a stop command request.
        /// </summary>
        public bool ValidateStopCommand(StopCommandData command, out CommandRejectReason rejectReason)
        {
            _totalCommands++;
            rejectReason = CommandRejectReason.Other;

            // Basic validation
            if (command.agentNetworkIds == null || command.agentNetworkIds.Length == 0)
            {
                rejectReason = CommandRejectReason.InvalidAgents;
                _rejectedCommands++;
                return false;
            }

            // Agent count limit
            if (command.agentNetworkIds.Length > _maxAgentsPerCommand)
            {
                rejectReason = CommandRejectReason.RuleViolation;
                _rejectedCommands++;
                return false;
            }

            // Rate limiting
            var tracker = GetOrCreatePlayerTracker(command.playerId);
            if (!tracker.CanIssueCommand(Time.time, _maxCommandsPerSecond))
            {
                rejectReason = CommandRejectReason.RateLimited;
                _rejectedCommands++;
                return false;
            }

            // Ownership validation
            if (_getAgentOwner != null)
            {
                foreach (uint networkId in command.agentNetworkIds)
                {
                    int owner = _getAgentOwner(networkId);
                    if (owner != command.playerId)
                    {
                        rejectReason = CommandRejectReason.NotOwner;
                        _rejectedCommands++;
                        return false;
                    }
                }
            }

            tracker.RecordCommand(Time.time);
            return true;
        }

        /// <summary>
        /// Validates a skill execution request.
        /// Checks ownership, rate limiting, and basic validity of the request.
        /// Cooldown and resource checks are handled by SkillExecutor on the server.
        /// </summary>
        /// <param name="request">The skill execution request to validate</param>
        /// <param name="rejectReason">Output reason if rejected</param>
        /// <returns>True if the request passes basic validation</returns>
        public bool ValidateSkillRequest(SkillExecutionRequestData request, out SkillRejectReason rejectReason)
        {
            _totalCommands++;
            rejectReason = SkillRejectReason.Other;

            // Basic validation
            if (request.casterNetworkId == 0)
            {
                rejectReason = SkillRejectReason.InvalidCaster;
                _rejectedCommands++;
                return false;
            }

            if (string.IsNullOrEmpty(request.skillId))
            {
                rejectReason = SkillRejectReason.SkillNotFound;
                _rejectedCommands++;
                return false;
            }

            // Rate limiting (reuse per-player tracker)
            var tracker = GetOrCreatePlayerTracker(request.playerId);
            if (!tracker.CanIssueCommand(Time.time, _maxCommandsPerSecond))
            {
                rejectReason = SkillRejectReason.RateLimited;
                _rejectedCommands++;
                return false;
            }

            // Ownership validation
            if (_getAgentOwner != null)
            {
                int owner = _getAgentOwner(request.casterNetworkId);
                if (owner != request.playerId)
                {
                    rejectReason = SkillRejectReason.NotOwner;
                    _rejectedCommands++;
                    LogSuspicious(request.playerId, $"Tried to cast skill on agent {request.casterNetworkId} owned by player {owner}");
                    return false;
                }
            }

            // Caster validity
            if (_isAgentValid != null && !_isAgentValid(request.casterNetworkId))
            {
                rejectReason = SkillRejectReason.InvalidCaster;
                _rejectedCommands++;
                return false;
            }

            // Target validity (if targeting another agent)
            if (request.targetNetworkId != 0 && _isAgentValid != null && !_isAgentValid(request.targetNetworkId))
            {
                rejectReason = SkillRejectReason.InvalidTarget;
                _rejectedCommands++;
                return false;
            }

            tracker.RecordCommand(Time.time);
            return true;
        }

        /// <summary>
        /// Gets validation statistics for monitoring.
        /// </summary>
        public (int total, int rejected, float rejectRate) GetStatistics()
        {
            float rate = _totalCommands > 0 ? (float)_rejectedCommands / _totalCommands : 0f;
            return (_totalCommands, _rejectedCommands, rate);
        }

        /// <summary>
        /// Gets a specific player's command rate for monitoring.
        /// </summary>
        public float GetPlayerCommandRate(int playerId)
        {
            return _playerTrackers.TryGetValue(playerId, out var tracker) 
                ? tracker.GetCommandRate() 
                : 0f;
        }

        /// <summary>
        /// Clears tracking data for a player (call on disconnect).
        /// </summary>
        public void ClearPlayerTracker(int playerId)
        {
            _playerTrackers.Remove(playerId);
        }

        /// <summary>
        /// Resets all tracking data. Call between matches.
        /// </summary>
        public void Reset()
        {
            _playerTrackers.Clear();
            _totalCommands = 0;
            _rejectedCommands = 0;
        }

        /// <summary>
        /// Resets statistics counters without clearing tracker data.
        /// </summary>
        public void ResetStatistics()
        {
            _totalCommands = 0;
            _rejectedCommands = 0;
        }

        private PlayerCommandTracker GetOrCreatePlayerTracker(int playerId)
        {
            if (!_playerTrackers.TryGetValue(playerId, out var tracker))
            {
                tracker = new PlayerCommandTracker();
                _playerTrackers[playerId] = tracker;
            }
            return tracker;
        }

        private void LogSuspicious(int playerId, string reason)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[NetworkCommandValidator] Suspicious command from player {playerId}: {reason}");
            #endif
        }

        /// <summary>
        /// Per-player tracking for rate limiting.
        /// </summary>
        private class PlayerCommandTracker
        {
            private readonly Queue<float> _commandTimes = new();
            private const float WINDOW_SIZE = 1f;

            public bool CanIssueCommand(float currentTime, int maxPerSecond)
            {
                // Clean old entries
                while (_commandTimes.Count > 0 && currentTime - _commandTimes.Peek() > WINDOW_SIZE)
                {
                    _commandTimes.Dequeue();
                }

                return _commandTimes.Count < maxPerSecond;
            }

            public void RecordCommand(float currentTime)
            {
                _commandTimes.Enqueue(currentTime);
            }

            public float GetCommandRate()
            {
                return _commandTimes.Count / WINDOW_SIZE;
            }
        }
    }
    
    // Note: CommandRejectReason enum is defined in INetworkCommandAuthority.cs
    // The validator uses that existing enum plus additional validation-specific reasons below

    /// <summary>
    /// Extended rejection reasons for validator-specific checks.
    /// Maps to base CommandRejectReason where applicable.
    /// </summary>
    public static class CommandValidatorReasons
    {
        public const string NoAgentsSelected = "No agents selected";
        public const string TooManyAgents = "Too many agents in command";
        public const string FriendlyTarget = "Cannot attack friendly unit";
        public const string DestinationTooFar = "Move destination too far";
        public const string InvalidFormation = "Invalid formation type";
    }
}
