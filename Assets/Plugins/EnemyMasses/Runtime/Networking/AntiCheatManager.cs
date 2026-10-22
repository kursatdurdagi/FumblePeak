using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Unified anti-cheat manager that coordinates all detection systems.
    /// Provides a single point of configuration and monitoring for server-side security.
    /// 
    /// This class ties together:
    /// - NetworkDamageValidator (damage rate limiting, range checks)
    /// - NetworkCommandValidator (command rate limiting, ownership)
    /// - SpeedHackDetector (movement validation)
    /// - StateReconciliationSystem (periodic state correction)
    /// 
    /// Usage:
    /// - Create one instance on the server
    /// - Configure sensitivity levels based on game type
    /// - Call appropriate validation methods during gameplay
    /// - Monitor statistics for suspicious patterns
    /// </summary>
    public class AntiCheatManager
    {
        // Subsystems
        public NetworkDamageValidator DamageValidator { get; private set; }
        public NetworkCommandValidator CommandValidator { get; private set; }
        public SpeedHackDetector SpeedDetector { get; private set; }
        public StateReconciliationSystem ReconciliationSystem { get; private set; }

        // Per-player tracking
        private readonly Dictionary<int, PlayerSuspicionLevel> _playerSuspicion = new();
        
        // Cached return values to avoid per-call allocations
        private readonly Dictionary<CheatType, int> _cachedDetectionsByType = new();
        private readonly Dictionary<int, float> _cachedSuspicionLevels = new();
        
        // Configuration
        private readonly AntiCheatConfig _config;
        
        // Events
        /// <summary>Event fired when a cheat is detected. Args: playerId, cheatType, details, severity</summary>
        public event System.Action<int, CheatType, string, float> OnCheatDetected;
        /// <summary>Event fired when a player's suspicion level changes. Args: playerId, newSuspicionLevel</summary>
        public event System.Action<int, float> OnSuspicionLevelChanged;
        /// <summary>Event fired when a player is flagged as a cheater. Args: playerId</summary>
        public event System.Action<int> OnPlayerFlaggedAsCheater;
        
        /// <summary>Gets the current configuration.</summary>
        public AntiCheatConfig Config => _config;

        /// <summary>
        /// Creates an anti-cheat manager with default configuration.
        /// </summary>
        public AntiCheatManager() : this(AntiCheatConfig.Default) { }

        /// <summary>
        /// Creates an anti-cheat manager with custom configuration.
        /// </summary>
        public AntiCheatManager(AntiCheatConfig config)
        {
            _config = config;
            
            DamageValidator = new NetworkDamageValidator(
                config.maxDamagePerHit,
                config.maxDPS,
                config.maxAttackRange,
                config.attackCooldown,
                config.rangeTolerance,
                config.timingTolerance);

            CommandValidator = new NetworkCommandValidator(
                config.maxCommandsPerSecond,
                config.maxAgentsPerCommand,
                config.maxCommandRange);

            SpeedDetector = new SpeedHackDetector(
                config.speedTolerance,
                config.speedViolationThreshold,
                config.violationDecayTime,
                config.teleportThreshold);

            ReconciliationSystem = new StateReconciliationSystem(
                config.reconciliationInterval,
                config.urgentReconcileThreshold,
                config.enableDeltaCompression);
        }

        /// <summary>
        /// Registers a cheat detection event and updates player suspicion.
        /// </summary>
        /// <param name="playerId">Player who triggered the detection</param>
        /// <param name="type">Type of cheat detected</param>
        /// <param name="severity">Severity from 0-1 (1 = definite cheat)</param>
        /// <param name="details">Human-readable details</param>
        public void RegisterCheatDetection(int playerId, CheatType type, float severity, string details)
        {
            var suspicion = GetOrCreatePlayerSuspicion(playerId);
            float suspicionIncrease = severity * GetCheatWeight(type);
            
            suspicion.totalSuspicion += suspicionIncrease;
            suspicion.detectionCount++;
            suspicion.lastDetectionTime = Time.time;
            suspicion.lastCheatType = type;

            // Log detection
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_config.enableLogging)
            {
                Debug.LogWarning($"[AntiCheat] Player {playerId} - {type}: {details} (Suspicion: {suspicion.totalSuspicion:F2})");
            }
            #endif

            OnCheatDetected?.Invoke(playerId, type, details, severity);
            OnSuspicionLevelChanged?.Invoke(playerId, suspicion.totalSuspicion);

            // Check if threshold exceeded
            if (suspicion.totalSuspicion >= _config.cheaterThreshold && !suspicion.isFlagged)
            {
                suspicion.isFlagged = true;
                suspicion.flaggedTime = Time.time;
                OnPlayerFlaggedAsCheater?.Invoke(playerId);
                
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[AntiCheat] Player {playerId} FLAGGED AS CHEATER! Suspicion: {suspicion.totalSuspicion:F2}");
                #endif
            }
        }

        /// <summary>
        /// Gets the current suspicion level for a player.
        /// </summary>
        public float GetPlayerSuspicion(int playerId)
        {
            return _playerSuspicion.TryGetValue(playerId, out var suspicion) 
                ? suspicion.totalSuspicion 
                : 0f;
        }

        /// <summary>
        /// Checks if a player has been flagged as a cheater.
        /// </summary>
        public bool IsPlayerFlagged(int playerId)
        {
            return _playerSuspicion.TryGetValue(playerId, out var suspicion) && suspicion.isFlagged;
        }

        /// <summary>
        /// Updates suspicion decay for all players. Call once per second.
        /// </summary>
        public void UpdateSuspicionDecay()
        {
            float currentTime = Time.time;
            
            foreach (var kvp in _playerSuspicion)
            {
                var suspicion = kvp.Value;
                
                // Don't decay flagged players
                if (suspicion.isFlagged)
                    continue;

                // Decay suspicion over time
                float timeSinceDetection = currentTime - suspicion.lastDetectionTime;
                if (timeSinceDetection > _config.suspicionDecayDelay)
                {
                    float decay = _config.suspicionDecayRate * Time.deltaTime;
                    suspicion.totalSuspicion = Mathf.Max(0, suspicion.totalSuspicion - decay);
                }
            }
        }

        /// <summary>
        /// Gets comprehensive anti-cheat statistics.
        /// </summary>
        public AntiCheatStatistics GetStatistics()
        {
            var damageStats = DamageValidator.GetStatistics();
            var commandStats = CommandValidator.GetStatistics();
            var speedStats = SpeedDetector.GetStatistics();
            var reconcileStats = ReconciliationSystem.GetStatistics();

            int flaggedPlayers = 0;
            int suspiciousPlayers = 0;
            int totalDetections = 0;
            _cachedDetectionsByType.Clear();
            
            foreach (var kvp in _playerSuspicion)
            {
                if (kvp.Value.isFlagged)
                    flaggedPlayers++;
                else if (kvp.Value.totalSuspicion > _config.cheaterThreshold * 0.5f)
                    suspiciousPlayers++;
                    
                totalDetections += kvp.Value.detectionCount;
                
                // Track detection types
                if (kvp.Value.detectionCount > 0)
                {
                    var lastType = kvp.Value.lastCheatType;
                    if (!_cachedDetectionsByType.ContainsKey(lastType))
                        _cachedDetectionsByType[lastType] = 0;
                    _cachedDetectionsByType[lastType] += kvp.Value.detectionCount;
                }
            }

            return new AntiCheatStatistics
            {
                // Totals for editor
                totalDetections = totalDetections,
                playersFlagged = flaggedPlayers,
                playersTracked = _playerSuspicion.Count,
                detectionsByType = _cachedDetectionsByType,
                
                // Damage validation
                damageChecks = damageStats.total,
                damageRejections = damageStats.rejected,
                damageRejectRate = damageStats.rejectRate,
                
                // Command validation  
                commandChecks = commandStats.total,
                commandRejections = commandStats.rejected,
                commandRejectRate = commandStats.rejectRate,
                
                // Speed detection
                movementChecks = speedStats.totalChecks,
                speedViolations = speedStats.violations,
                teleportDetections = speedStats.teleports,
                
                // Reconciliation
                snapshotsSent = reconcileStats.sent,
                reconciliationsApplied = reconcileStats.applied,
                urgentReconciliations = reconcileStats.urgent,
                
                // Player status
                trackedPlayers = _playerSuspicion.Count,
                flaggedPlayers = flaggedPlayers,
                suspiciousPlayers = suspiciousPlayers
            };
        }

        /// <summary>
        /// Clears all tracking data for a player (call on disconnect).
        /// </summary>
        public void ClearPlayer(int playerId)
        {
            _playerSuspicion.Remove(playerId);
        }

        /// <summary>
        /// Clears all tracking data for an agent.
        /// </summary>
        public void ClearAgent(uint networkId)
        {
            DamageValidator.ClearTracker(networkId);
            SpeedDetector.ClearTracker(networkId);
            ReconciliationSystem.ClearAgent(networkId);
        }

        /// <summary>
        /// Gets all player suspicion levels for monitoring.
        /// Returns a cached dictionary — contents are valid until the next call.
        /// Do not hold a long-lived reference.
        /// </summary>
        public Dictionary<int, float> GetAllSuspicionLevels()
        {
            _cachedSuspicionLevels.Clear();
            foreach (var kvp in _playerSuspicion)
            {
                _cachedSuspicionLevels[kvp.Key] = kvp.Value.totalSuspicion;
            }
            return _cachedSuspicionLevels;
        }

        /// <summary>
        /// Resets suspicion level for a specific player.
        /// </summary>
        public void ResetPlayerSuspicion(int playerId)
        {
            if (_playerSuspicion.TryGetValue(playerId, out var suspicion))
            {
                suspicion.totalSuspicion = 0;
                suspicion.detectionCount = 0;
                suspicion.isFlagged = false;
                OnSuspicionLevelChanged?.Invoke(playerId, 0);
            }
        }

        /// <summary>
        /// Manually flags a player as a cheater.
        /// </summary>
        public void FlagPlayerAsCheater(int playerId, string reason)
        {
            var suspicion = GetOrCreatePlayerSuspicion(playerId);
            if (!suspicion.isFlagged)
            {
                suspicion.isFlagged = true;
                suspicion.flaggedTime = Time.time;
                OnPlayerFlaggedAsCheater?.Invoke(playerId);
                
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[AntiCheat] Player {playerId} manually flagged: {reason}");
                #endif
            }
        }

        /// <summary>
        /// Resets statistics counters without clearing player data.
        /// </summary>
        public void ResetStatistics()
        {
            DamageValidator.ResetStatistics();
            CommandValidator.ResetStatistics();
            SpeedDetector.ResetStatistics();
            ReconciliationSystem.ResetStatistics();
        }

        /// <summary>
        /// Resets all anti-cheat systems. Call between matches.
        /// </summary>
        public void Reset()
        {
            DamageValidator.Reset();
            CommandValidator.Reset();
            SpeedDetector.Reset();
            ReconciliationSystem.Reset();
            _playerSuspicion.Clear();
        }

        private PlayerSuspicionLevel GetOrCreatePlayerSuspicion(int playerId)
        {
            if (!_playerSuspicion.TryGetValue(playerId, out var suspicion))
            {
                suspicion = new PlayerSuspicionLevel();
                _playerSuspicion[playerId] = suspicion;
            }
            return suspicion;
        }

        private float GetCheatWeight(CheatType type)
        {
            return type switch
            {
                CheatType.SpeedHack => 1.0f,
                CheatType.Teleport => 1.5f,
                CheatType.DamageHack => 1.2f,
                CheatType.GodMode => 2.0f,
                CheatType.CommandFlooding => 0.3f,
                CheatType.OwnershipViolation => 1.5f,
                CheatType.RangeHack => 0.8f,
                CheatType.AttackSpeedHack => 1.0f,
                _ => 0.5f
            };
        }

        private class PlayerSuspicionLevel
        {
            public float totalSuspicion;
            public int detectionCount;
            public float lastDetectionTime;
            public CheatType lastCheatType;
            public bool isFlagged;
            public float flaggedTime;
        }
    }

    /// <summary>
    /// Configuration for the anti-cheat system.
    /// </summary>
    [System.Serializable]
    public struct AntiCheatConfig
    {
        // Damage validation
        public float maxDamagePerHit;
        public float maxDPS;
        public float maxAttackRange;
        public float attackCooldown;
        public float rangeTolerance;
        public float timingTolerance;
        
        // Command validation
        public int maxCommandsPerSecond;
        public int maxAgentsPerCommand;
        public float maxCommandRange;
        
        // Speed detection
        public float speedTolerance;
        public int speedViolationThreshold;
        public float violationDecayTime;
        public float teleportThreshold;
        
        // Reconciliation
        public float reconciliationInterval;
        public float urgentReconcileThreshold;
        public bool enableDeltaCompression;
        
        // Suspicion system
        public float cheaterThreshold;
        public float suspicionDecayRate;
        public float suspicionDecayDelay;
        
        // Logging
        public bool enableLogging;
        
        // Editor properties (mapped from internal names)
        public float suspicionThreshold { get => cheaterThreshold; set => cheaterThreshold = value; }
        public float speedToleranceMultiplier { get => speedTolerance; set => speedTolerance = value; }
        public float maxDpsMultiplier { get => maxDPS; set => maxDPS = value; }
        public int commandsPerSecondLimit { get => maxCommandsPerSecond; set => maxCommandsPerSecond = value; }

        /// <summary>
        /// Default balanced configuration.
        /// </summary>
        public static AntiCheatConfig Default => new AntiCheatConfig
        {
            // Damage
            maxDamagePerHit = 500f,
            maxDPS = 300f,
            maxAttackRange = 25f,
            attackCooldown = 0.3f,
            rangeTolerance = 1.5f,
            timingTolerance = 0.2f,
            
            // Commands
            maxCommandsPerSecond = 30,
            maxAgentsPerCommand = 200,
            maxCommandRange = 500f,
            
            // Speed
            speedTolerance = 1.5f,
            speedViolationThreshold = 5,
            violationDecayTime = 10f,
            teleportThreshold = 50f,
            
            // Reconciliation
            reconciliationInterval = 1f,
            urgentReconcileThreshold = 0.2f,
            enableDeltaCompression = true,
            
            // Suspicion
            cheaterThreshold = 5f,
            suspicionDecayRate = 0.1f,
            suspicionDecayDelay = 30f,
            
            // Logging
            enableLogging = true
        };

        /// <summary>
        /// Strict configuration for competitive play.
        /// </summary>
        public static AntiCheatConfig Strict => new AntiCheatConfig
        {
            maxDamagePerHit = 300f,
            maxDPS = 200f,
            maxAttackRange = 20f,
            attackCooldown = 0.4f,
            rangeTolerance = 1.2f,
            timingTolerance = 0.1f,
            
            maxCommandsPerSecond = 20,
            maxAgentsPerCommand = 100,
            maxCommandRange = 300f,
            
            speedTolerance = 1.2f,
            speedViolationThreshold = 3,
            violationDecayTime = 30f,
            teleportThreshold = 30f,
            
            reconciliationInterval = 0.5f,
            urgentReconcileThreshold = 0.1f,
            enableDeltaCompression = true,
            
            cheaterThreshold = 3f,
            suspicionDecayRate = 0.05f,
            suspicionDecayDelay = 60f,
            
            enableLogging = true
        };

        /// <summary>
        /// Lenient configuration for casual play.
        /// </summary>
        public static AntiCheatConfig Lenient => new AntiCheatConfig
        {
            maxDamagePerHit = 1000f,
            maxDPS = 500f,
            maxAttackRange = 50f,
            attackCooldown = 0.2f,
            rangeTolerance = 2f,
            timingTolerance = 0.5f,
            
            maxCommandsPerSecond = 60,
            maxAgentsPerCommand = 500,
            maxCommandRange = 1000f,
            
            speedTolerance = 2f,
            speedViolationThreshold = 10,
            violationDecayTime = 5f,
            teleportThreshold = 100f,
            
            reconciliationInterval = 2f,
            urgentReconcileThreshold = 0.3f,
            enableDeltaCompression = true,
            
            cheaterThreshold = 10f,
            suspicionDecayRate = 0.2f,
            suspicionDecayDelay = 15f,
            
            enableLogging = false
        };
    }

    /// <summary>
    /// Type of cheat detected.
    /// </summary>
    public enum CheatType
    {
        /// <summary>Movement faster than allowed</summary>
        SpeedHack,
        /// <summary>Instant position change</summary>
        Teleport,
        /// <summary>Dealing more damage than possible</summary>
        DamageHack,
        /// <summary>Taking no damage / infinite health</summary>
        GodMode,
        /// <summary>Issuing too many commands</summary>
        CommandFlooding,
        /// <summary>Controlling units you don't own</summary>
        OwnershipViolation,
        /// <summary>Attacking from impossible range</summary>
        RangeHack,
        /// <summary>Attacking faster than cooldown allows</summary>
        AttackSpeedHack,
        /// <summary>Exploiting skill system (casting unowned skills, bypassing cooldowns/resources)</summary>
        SkillExploit,
        /// <summary>Resource state manipulation (claiming more resources than possible)</summary>
        ResourceManipulation,
        /// <summary>Generic suspicious activity</summary>
        Suspicious,
        /// <summary>Unknown cheat type</summary>
        Unknown
    }

    /// <summary>
    /// Comprehensive anti-cheat statistics.
    /// </summary>
    public struct AntiCheatStatistics
    {
        // Totals for editor display
        public int totalDetections;
        public int playersFlagged;
        public int playersTracked;
        public Dictionary<CheatType, int> detectionsByType;
        
        // Damage validation
        public int damageChecks;
        public int damageRejections;
        public float damageRejectRate;
        
        // Command validation
        public int commandChecks;
        public int commandRejections;
        public float commandRejectRate;
        
        // Speed detection
        public int movementChecks;
        public int speedViolations;
        public int teleportDetections;
        
        // Reconciliation
        public int snapshotsSent;
        public int reconciliationsApplied;
        public int urgentReconciliations;
        
        // Players
        public int trackedPlayers;
        public int flaggedPlayers;
        public int suspiciousPlayers;

        public override string ToString()
        {
            return $"AntiCheat Stats:\n" +
                $"  Damage: {damageRejections}/{damageChecks} rejected ({damageRejectRate:P1})\n" +
                $"  Commands: {commandRejections}/{commandChecks} rejected ({commandRejectRate:P1})\n" +
                $"  Speed: {speedViolations} violations, {teleportDetections} teleports\n" +
                $"  Reconciliation: {snapshotsSent} sent, {urgentReconciliations} urgent\n" +
                $"  Players: {flaggedPlayers} flagged, {suspiciousPlayers} suspicious";
        }
    }
}
