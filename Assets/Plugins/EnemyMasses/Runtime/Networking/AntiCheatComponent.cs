using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// MonoBehaviour wrapper for AntiCheatManager to allow inspector configuration.
    /// Attach this to a persistent GameObject to provide anti-cheat functionality.
    /// 
    /// Usage:
    /// 1. Add this component to a server-side manager GameObject
    /// 2. Configure settings in the inspector
    /// 3. Access the manager via AntiCheatComponent.Instance or the Manager property
    /// 4. Call validation methods during gameplay
    /// </summary>
    [AddComponentMenu("Enemy Masses/Networking/Anti-Cheat Manager")]
    public class AntiCheatComponent : MonoBehaviour
    {
        // Singleton instance
        private static AntiCheatComponent _instance;
        public static AntiCheatComponent Instance => _instance;
        
        [Header("Configuration")]
        [Tooltip("Select a preset configuration")]
        [SerializeField] private ConfigPreset _preset = ConfigPreset.Default;
        
        [Header("Suspicion Settings")]
        [Tooltip("Suspicion level that triggers flagging as cheater")]
        [SerializeField] private float _suspicionThreshold = 5f;
        
        [Tooltip("How fast suspicion decays per second")]
        [SerializeField] private float _suspicionDecayRate = 0.1f;
        
        [Header("Damage Validation")]
        [Tooltip("Maximum damage allowed per hit")]
        [SerializeField] private float _maxDamagePerHit = 500f;
        
        [Tooltip("Maximum damage per second allowed")]
        [SerializeField] private float _maxDPS = 300f;
        
        [Tooltip("Maximum attack range")]
        [SerializeField] private float _maxAttackRange = 25f;
        
        [Header("Speed Detection")]
        [Tooltip("Multiplier for allowed speed (1.5 = 50% tolerance)")]
        [SerializeField] private float _speedTolerance = 1.5f;
        
        [Tooltip("Number of violations before flagging")]
        [SerializeField] private int _speedViolationThreshold = 5;
        
        [Tooltip("Distance that counts as a teleport")]
        [SerializeField] private float _teleportThreshold = 50f;
        
        [Header("Command Validation")]
        [Tooltip("Maximum commands per second per player")]
        [SerializeField] private int _maxCommandsPerSecond = 30;
        
        [Tooltip("Maximum agents in a single command")]
        [SerializeField] private int _maxAgentsPerCommand = 200;
        
        [Header("Reconciliation")]
        [Tooltip("Seconds between server state snapshots")]
        [SerializeField] private float _reconciliationInterval = 1f;
        
        [Tooltip("Use delta compression for snapshots")]
        [SerializeField] private bool _enableDeltaCompression = true;
        
        [Header("Logging")]
        [Tooltip("Log cheat detections to console (dev builds only)")]
        [SerializeField] private bool _enableLogging = true;
        
        // Runtime manager instance
        private AntiCheatManager _manager;
        private float _decayTimer;
        public AntiCheatManager Manager => _manager;
        
        // Convenience accessors for subsystems
        public NetworkDamageValidator DamageValidator => _manager?.DamageValidator;
        public NetworkCommandValidator CommandValidator => _manager?.CommandValidator;
        public SpeedHackDetector SpeedDetector => _manager?.SpeedDetector;
        public StateReconciliationSystem ReconciliationSystem => _manager?.ReconciliationSystem;
        
        public enum ConfigPreset
        {
            Default,
            Strict,
            Lenient,
            Custom
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[AntiCheatComponent] Multiple instances detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            InitializeManager();
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        private void Update()
        {
            // Throttle suspicion decay — no gameplay benefit from running every frame.
            // 0.5 s gives smooth-enough decay while saving ~29/30 iterations per second.
            _decayTimer += Time.deltaTime;
            if (_decayTimer >= 0.5f)
            {
                _decayTimer -= 0.5f;
                _manager?.UpdateSuspicionDecay();
            }
        }
        
        private void InitializeManager()
        {
            var config = BuildConfig();
            _manager = new AntiCheatManager(config);
            
            // Hook up events for logging
            _manager.OnCheatDetected += OnCheatDetected;
            _manager.OnPlayerFlaggedAsCheater += OnPlayerFlagged;
        }
        
        private AntiCheatConfig BuildConfig()
        {
            // Start with preset
            AntiCheatConfig config = _preset switch
            {
                ConfigPreset.Strict => AntiCheatConfig.Strict,
                ConfigPreset.Lenient => AntiCheatConfig.Lenient,
                _ => AntiCheatConfig.Default
            };
            
            // Override with custom values if using custom preset
            if (_preset == ConfigPreset.Custom)
            {
                config.cheaterThreshold = _suspicionThreshold;
                config.suspicionDecayRate = _suspicionDecayRate;
                config.maxDamagePerHit = _maxDamagePerHit;
                config.maxDPS = _maxDPS;
                config.maxAttackRange = _maxAttackRange;
                config.speedTolerance = _speedTolerance;
                config.speedViolationThreshold = _speedViolationThreshold;
                config.teleportThreshold = _teleportThreshold;
                config.maxCommandsPerSecond = _maxCommandsPerSecond;
                config.maxAgentsPerCommand = _maxAgentsPerCommand;
                config.reconciliationInterval = _reconciliationInterval;
                config.enableDeltaCompression = _enableDeltaCompression;
                config.enableLogging = _enableLogging;
            }
            
            return config;
        }
        
        private void OnCheatDetected(int playerId, CheatType type, string details, float severity)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_enableLogging)
            {
                string severityLabel = severity > 0.7f ? "HIGH" : severity > 0.4f ? "MEDIUM" : "LOW";
                Debug.LogWarning($"[AntiCheat] [{severityLabel}] Player {playerId}: {type} - {details}");
            }
            #endif
        }
        
        private void OnPlayerFlagged(int playerId)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[AntiCheat] Player {playerId} has been FLAGGED AS CHEATER!");
            #endif
        }
        
        /// <summary>
        /// Resets all tracking data. Call between matches.
        /// </summary>
        public void ResetAll()
        {
            _manager?.Reset();
        }
        
        /// <summary>
        /// Gets current statistics for display.
        /// </summary>
        public AntiCheatStatistics GetStatistics()
        {
            return _manager?.GetStatistics() ?? default;
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Applies the selected preset in the editor.
        /// </summary>
        public void ApplyPreset(ConfigPreset preset)
        {
            _preset = preset;
            
            var config = preset switch
            {
                ConfigPreset.Strict => AntiCheatConfig.Strict,
                ConfigPreset.Lenient => AntiCheatConfig.Lenient,
                _ => AntiCheatConfig.Default
            };
            
            _suspicionThreshold = config.cheaterThreshold;
            _suspicionDecayRate = config.suspicionDecayRate;
            _maxDamagePerHit = config.maxDamagePerHit;
            _maxDPS = config.maxDPS;
            _maxAttackRange = config.maxAttackRange;
            _speedTolerance = config.speedTolerance;
            _speedViolationThreshold = config.speedViolationThreshold;
            _teleportThreshold = config.teleportThreshold;
            _maxCommandsPerSecond = config.maxCommandsPerSecond;
            _maxAgentsPerCommand = config.maxAgentsPerCommand;
            _reconciliationInterval = config.reconciliationInterval;
            _enableDeltaCompression = config.enableDeltaCompression;
            _enableLogging = config.enableLogging;
        }
        
        /// <summary>
        /// Gets the runtime manager for editor inspection.
        /// </summary>
        public AntiCheatManager GetRuntimeManager() => _manager;
        
        /// <summary>
        /// Gets all player suspicion levels for editor display.
        /// </summary>
        public System.Collections.Generic.Dictionary<int, float> GetAllSuspicionLevels()
        {
            return _manager?.GetAllSuspicionLevels() ?? new System.Collections.Generic.Dictionary<int, float>();
        }
        
        /// <summary>
        /// Resets suspicion for a player from the editor.
        /// </summary>
        public void ResetPlayerSuspicion(int playerId)
        {
            _manager?.ResetPlayerSuspicion(playerId);
        }
        
        /// <summary>
        /// Manually flags a player from the editor.
        /// </summary>
        public void FlagPlayer(int playerId)
        {
            _manager?.FlagPlayerAsCheater(playerId, "Manual flag from editor");
        }
        
        /// <summary>
        /// Resets statistics counters.
        /// </summary>
        public void ResetStatistics()
        {
            _manager?.ResetStatistics();
        }
        
        // Editor-visible properties
        public float SuspicionThreshold => _suspicionThreshold;
        public bool IsInitialized => _manager != null;
        #endif
    }
}
