using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Tracks which player-controlled agents are under attack and provides
    /// alert intensity data for minimap marker flashing + audio alerts.
    ///
    /// Subscribes to <see cref="EnemyMassesCombatSystem.OnAgentDamaged"/> and records
    /// damage timestamps per agent. The data provider queries <see cref="GetAlertIntensity"/>
    /// each frame to set <see cref="MinimapMarker.AlertIntensity"/>.
    ///
    /// Inspired by Age of Empires 2: when player units are attacked, their minimap
    /// markers flash and an alarm sound plays with a configurable cooldown.
    /// </summary>
    [AddComponentMenu("Enemy Masses/UI/Minimap Alert System")]
    public class MinimapAlertSystem : MonoBehaviour
    {
        #region Inspector

        [Header("References")]
        [Tooltip("Crowd controller to subscribe to combat events. Auto-discovered if null.")]
        [SerializeField] private EnemyMassesCrowdController crowdController;

        [Tooltip("RTS controller for player faction detection. Auto-discovered if null.")]
        [SerializeField] private EnemyMassesRTSController rtsController;

        [Header("Flash Settings")]
        [Tooltip("How long a marker flashes after being hit (seconds).")]
        [SerializeField] private float alertDuration = 3f;

        [Tooltip("Flash frequency (full cycles per second).")]
        [SerializeField] private float flashFrequency = 3f;

        [Tooltip("Color to flash markers when under attack.")]
        [SerializeField] private Color alertColor = Color.white;

        [Tooltip("Scale up minimap markers by this factor when flashing (1 = no size change).")]
        [SerializeField] private float markerSizeMultiplier = 1.5f;

        [Header("View Indicator")]
        [Tooltip("Also flash the minimap view indicator border when units are under attack.")]
        [SerializeField] private bool flashViewIndicator = true;

        [Tooltip("Flash color for the view indicator border.")]
        [SerializeField] private Color viewIndicatorAlertColor = Color.red;

        [Header("Audio")]
        [Tooltip("Alert audio clip played when player units come under attack.")]
        [SerializeField] private AudioClip alertSound;

        [Tooltip("Volume of the alert sound.")]
        [Range(0f, 1f)]
        [SerializeField] private float alertVolume = 0.8f;

        [Tooltip("Minimum time (seconds) between alert sounds. Prevents spam when many units are hit.")]
        [SerializeField] private float alertSoundCooldown = 5f;

        [Tooltip("Only play alert when player-controlled units are attacked (recommended).")]
        [SerializeField] private bool onlyAlertForPlayerUnits = true;

        #endregion

        #region Public API

        /// <summary>Alert flash color (read by RTSMinimapUI).</summary>
        public Color AlertColor => alertColor;

        /// <summary>Flash frequency in Hz (read by RTSMinimapUI).</summary>
        public float FlashFrequency => flashFrequency;

        /// <summary>Alert duration in seconds.</summary>
        public float AlertDuration => alertDuration;

        /// <summary>Scale factor for minimap markers when flashing.</summary>
        public float MarkerSizeMultiplier => markerSizeMultiplier;

        /// <summary>Whether to also flash the minimap view indicator.</summary>
        public bool FlashViewIndicator => flashViewIndicator;

        /// <summary>Flash color for the view indicator border.</summary>
        public Color ViewIndicatorAlertColor => viewIndicatorAlertColor;

        /// <summary>
        /// Returns the alert intensity for the given agent index.
        /// 0 = not under attack, 1 = peak flash.
        /// Uses a sine wave fade-out over <see cref="alertDuration"/>.
        /// </summary>
        public float GetAlertIntensity(int agentIndex)
        {
            if (!_alertTimes.TryGetValue(agentIndex, out float lastHitTime))
                return 0f;

            float elapsed = Time.time - lastHitTime;
            if (elapsed >= alertDuration)
            {
                _alertTimes.Remove(agentIndex);
                return 0f;
            }

            // Fade-out envelope: 1 → 0 over alertDuration
            float envelope = 1f - (elapsed / alertDuration);

            // Ping-pong flash using abs(sin)
            float flash = Mathf.Abs(Mathf.Sin(elapsed * flashFrequency * Mathf.PI));

            return envelope * flash;
        }

        /// <summary>
        /// Whether any alert is currently active (at least one agent flashing).
        /// </summary>
        public bool HasActiveAlerts => _alertTimes.Count > 0;

        /// <summary>
        /// Number of currently flashing agents.
        /// </summary>
        public int ActiveAlertCount => _alertTimes.Count;

        /// <summary>
        /// Manually trigger an alert for a specific agent index.
        /// Useful for custom damage sources not wired through the combat system.
        /// </summary>
        public void TriggerAlert(int agentIndex)
        {
            _alertTimes[agentIndex] = Time.time;
        }

        #endregion

        #region Private

        // Agent index → time of last damage
        private readonly Dictionary<int, float> _alertTimes = new Dictionary<int, float>();
        private AudioSource _audioSource;
        private float _lastAlertSoundTime = float.MinValue;
        private EnemyMassesCombatSystem _combatSystem;

        private void Awake()
        {
            if (crowdController == null)
                crowdController = FindAnyObjectByType<EnemyMassesCrowdController>();
            if (rtsController == null)
                rtsController = FindAnyObjectByType<EnemyMassesRTSController>();
        }

        private void Start()
        {
            // Setup audio source (2D, non-spatial — UI alert sound)
            if (alertSound != null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 0f; // 2D
                _audioSource.loop = false;
            }

            // Subscribe to combat events
            if (crowdController != null)
            {
                _combatSystem = crowdController.CombatSystem;
                if (_combatSystem != null)
                    _combatSystem.OnAgentDamaged += HandleAgentDamaged;
            }
        }

        private void OnDestroy()
        {
            if (_combatSystem != null)
                _combatSystem.OnAgentDamaged -= HandleAgentDamaged;
        }

        private void LateUpdate()
        {
            // Prune expired alerts
            if (_alertTimes.Count > 0)
            {
                float now = Time.time;
                float expiry = alertDuration;
                _expiredKeys.Clear();

                foreach (var kvp in _alertTimes)
                {
                    if (now - kvp.Value >= expiry)
                        _expiredKeys.Add(kvp.Key);
                }

                for (int i = 0; i < _expiredKeys.Count; i++)
                    _alertTimes.Remove(_expiredKeys[i]);
            }
        }

        // Reusable list to avoid allocation during pruning
        private readonly List<int> _expiredKeys = new List<int>();

        private void HandleAgentDamaged(CrowdAgent agent, float damage, int attackerIndex)
        {
            if (agent == null || agent.isDead) return;
            if (damage <= 0f) return;

            // Only alert for player-controlled units
            if (onlyAlertForPlayerUnits)
            {
                if (rtsController == null || rtsController.playerControlledFactions == null)
                    return;

                string factionName = agent.enemyCrowd?.factionName;
                if (string.IsNullOrEmpty(factionName) ||
                    !rtsController.playerControlledFactions.ContainsFaction(factionName))
                    return;
            }

            // Record alert time
            _alertTimes[agent.index] = Time.time;

            // Play alert sound with cooldown
            if (alertSound != null && _audioSource != null)
            {
                float now = Time.time;
                if (now - _lastAlertSoundTime >= alertSoundCooldown)
                {
                    _lastAlertSoundTime = now;
                    _audioSource.PlayOneShot(alertSound, alertVolume);
                }
            }
        }

        #endregion
    }
}
