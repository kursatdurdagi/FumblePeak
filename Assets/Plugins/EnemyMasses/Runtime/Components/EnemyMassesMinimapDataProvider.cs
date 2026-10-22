using System;
using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Factions;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Concrete <see cref="IMinimapDataProvider"/> that reads agent data from
    /// <see cref="EnemyMassesCrowdController"/> and optional <see cref="EnemyMassesRTSController"/>.
    ///
    /// Attach to the same GameObject as the CrowdController, or assign references manually.
    /// Any minimap UI (built-in RTSMinimapUI, 3rd party compass/radar, etc.) can consume this.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Data/Minimap Data Provider")]
    public class EnemyMassesMinimapDataProvider : MonoBehaviour, IMinimapDataProvider
    {
        #region Inspector

        [Header("References")]
        [Tooltip("CrowdController to read agent data from. Auto-discovered if null.")]
        [SerializeField] private EnemyMassesCrowdController crowdController;

        [Tooltip("Optional RTS controller for selection state and player faction info.")]
        [SerializeField] private EnemyMassesRTSController rtsController;

        [Tooltip("Optional fog-of-war visibility provider (MonoBehaviour implementing IFogVisibility).")]
        [SerializeField] private MonoBehaviour fogOfWarProvider;

        [Header("Filtering")]
        [Tooltip("Include dead agents in the marker data.")]
        [SerializeField] private bool includeDeadAgents = false;

        [Tooltip("Hide non-player factions entirely (requires rtsController).")]
        [SerializeField] private bool hideNonPlayerFactions = true;

        [Tooltip("Only show non-player units when revealed by fog of war.")]
        [SerializeField] private bool requireFogForNonPlayers = true;

        [Header("Color Priority")]
        [Tooltip("Use faction colors from EnemyCrowd assets. If false, all markers use defaultColor.")]
        [SerializeField] private bool useFactionColors = true;

        [Tooltip("Default color when faction color is not available.")]
        [SerializeField] private Color defaultColor = Color.red;

        [Tooltip("Color for dead agents.")]
        [SerializeField] private Color deadColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

        [Header("External Icons")]
        [Tooltip("Include IRTSMinimapIcon entries from RTSWorldUnitRegistry.")]
        [SerializeField] private bool includeRegisteredIcons = true;

        [Header("Performance")]
        [Tooltip("How often to recalculate world bounds (seconds). 0 = every frame.")]
        [SerializeField] private float boundsUpdateInterval = 0.5f;

        #endregion

        #region IMinimapDataProvider

        /// <inheritdoc/>
        public int MarkerCount
        {
            get
            {
                int count = 0;
                if (crowdController != null)
                    count += crowdController.GetAgents()?.Count ?? 0;
                if (includeRegisteredIcons)
                    count += RTSWorldUnitRegistry.RegisteredMinimapIcons?.Count ?? 0;
                if (rtsController != null)
                    count += rtsController.ExternalSelectables?.Count ?? 0;
                return count;
            }
        }

        /// <inheritdoc/>
        public Bounds WorldBounds => _cachedBounds;

        /// <inheritdoc/>
        public event Action OnMarkersChanged;

        /// <inheritdoc/>
        public int GetMarkers(MinimapMarker[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return 0;

            int written = 0;
            int max = buffer.Length;

            // ─── Crowd Agents ────────────────────────────────────────
            if (crowdController != null)
            {
                var agents = crowdController.GetAgents();
                if (agents != null)
                {
                    // Cache selected set for O(1) lookups
                    _selectedSet.Clear();
                    if (rtsController != null)
                    {
                        var selected = rtsController.SelectedAgents;
                        for (int s = 0; s < selected.Count; s++)
                            _selectedSet.Add(selected[s]);
                    }

                    bool hasPlayerFilter = rtsController != null &&
                                           rtsController.playerControlledFactions != null &&
                                           rtsController.playerControlledFactions.HasAnyFaction;

                    for (int i = 0; i < agents.Count && written < max; i++)
                    {
                        var agent = agents[i];
                        if (agent == null) continue;
                        if (agent.isDead && !includeDeadAgents) continue;

                        // Faction filter
                        string factionName = agent.enemyCrowd?.factionName;
                        bool isPlayerFaction = !hasPlayerFilter ||
                            (!string.IsNullOrEmpty(factionName) &&
                             rtsController.playerControlledFactions.ContainsFaction(factionName));

                        if (hideNonPlayerFactions && !isPlayerFaction)
                        {
                            if (!requireFogForNonPlayers) continue;
                            if (_fog == null || !_fog.IsActivelyVisible(agent.WorldPosition)) continue;
                        }

                        ref var marker = ref buffer[written];
                        marker.Position = agent.WorldPosition;
                        marker.IsDead = agent.isDead;
                        marker.IsSelected = _selectedSet.Contains(i);
                        marker.IsPlayerControlled = isPlayerFaction;
                        marker.GroupId = agent.enemyCrowdIndex;
                        marker.AgentIndex = agent.index;
                        marker.Color = agent.isDead
                            ? deadColor
                            : ResolveAgentColor(agent, agent.enemyCrowd);
                        marker.AlertIntensity = _alertSystem != null
                            ? _alertSystem.GetAlertIntensity(agent.index)
                            : 0f;

                        written++;
                    }
                }
            }

            // ─── External Selectables (RTS Controller) ───────────────
            if (rtsController != null)
            {
                var externals = rtsController.ExternalSelectables;
                if (externals != null)
                {
                    for (int i = 0; i < externals.Count && written < max; i++)
                    {
                        var ext = externals[i];
                        if (ext == null) continue;

                        var mmIcon = ext as IRTSMinimapIcon;
                        if (mmIcon != null && !mmIcon.ShowOnMinimap) continue;

                        ref var marker = ref buffer[written];
                        marker.Position = mmIcon != null ? mmIcon.MinimapPosition : ((MonoBehaviour)ext).transform.position;
                        marker.Color = mmIcon != null ? mmIcon.MinimapColor : defaultColor;
                        marker.IsDead = false;
                        marker.IsSelected = false;
                        marker.IsPlayerControlled = true;
                        marker.GroupId = -1;

                        written++;
                    }
                }
            }

            // ─── Registered Minimap Icons ────────────────────────────
            if (includeRegisteredIcons)
            {
                var icons = RTSWorldUnitRegistry.RegisteredMinimapIcons;
                if (icons != null)
                {
                    foreach (var icon in icons)
                    {
                        if (written >= max) break;
                        if (icon == null || !icon.ShowOnMinimap) continue;

                        ref var marker = ref buffer[written];
                        marker.Position = icon.MinimapPosition;
                        marker.Color = icon.MinimapColor;
                        marker.IsDead = false;
                        marker.IsSelected = false;
                        marker.IsPlayerControlled = true;
                        marker.GroupId = -2;

                        written++;
                    }
                }
            }

            return written;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Manually fire <see cref="OnMarkersChanged"/> (e.g., after spawn/despawn).
        /// </summary>
        public void NotifyMarkersChanged() => OnMarkersChanged?.Invoke();

        /// <summary>Crowd controller reference (read-only).</summary>
        public EnemyMassesCrowdController CrowdController => crowdController;

        /// <summary>RTS controller reference (read-only).</summary>
        public EnemyMassesRTSController RTSController => rtsController;

        #endregion

        #region Private

        private IFogVisibility _fog;
        private MinimapAlertSystem _alertSystem;
        private Bounds _cachedBounds;
        private float _nextBoundsUpdate;
        private readonly HashSet<int> _selectedSet = new HashSet<int>();

        /// <summary>Alert system reference for external access (e.g., RTSMinimapUI flash rendering).</summary>
        public MinimapAlertSystem AlertSystem => _alertSystem;

        private void Awake()
        {
            if (crowdController == null)
                crowdController = GetComponentInParent<EnemyMassesCrowdController>();
            if (rtsController == null)
                rtsController = FindAnyObjectByType<EnemyMassesRTSController>();

            _alertSystem = GetComponentInParent<MinimapAlertSystem>();
            if (_alertSystem == null)
                _alertSystem = FindAnyObjectByType<MinimapAlertSystem>();

            DiscoverFog();
        }

        private void DiscoverFog()
        {
            if (fogOfWarProvider != null)
            {
                _fog = fogOfWarProvider as IFogVisibility;
                return;
            }
            // Scene search fallback
            var fogComponents = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in fogComponents)
            {
                if (mb is IFogVisibility fv)
                {
                    _fog = fv;
                    break;
                }
            }
        }

        private void Update()
        {
            if (Time.time >= _nextBoundsUpdate)
            {
                _nextBoundsUpdate = Time.time + boundsUpdateInterval;
                RecalculateBounds();
            }
        }

        private void RecalculateBounds()
        {
            if (crowdController == null) return;
            var agents = crowdController.GetAgents();
            if (agents == null || agents.Count == 0) return;

            // Determine if we should filter bounds to player agents only
            bool filterToPlayer = hideNonPlayerFactions &&
                                  rtsController != null &&
                                  rtsController.playerControlledFactions != null &&
                                  rtsController.playerControlledFactions.HasAnyFaction;

            Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, 0f, float.MinValue);
            bool any = false;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead) continue;

                // When filtering is active, only include player-controlled agents in bounds
                if (filterToPlayer)
                {
                    string factionName = agent.enemyCrowd?.factionName;
                    if (string.IsNullOrEmpty(factionName) ||
                        !rtsController.playerControlledFactions.ContainsFaction(factionName))
                        continue;
                }

                Vector3 p = agent.WorldPosition;
                if (p.x < min.x) min.x = p.x;
                if (p.z < min.z) min.z = p.z;
                if (p.x > max.x) max.x = p.x;
                if (p.z > max.z) max.z = p.z;
                any = true;
            }

            if (any)
            {
                Vector3 center = (min + max) * 0.5f;
                Vector3 size = max - min;
                _cachedBounds = new Bounds(center, size);
            }
        }

        /// <summary>
        /// Resolves minimap color for an agent.
        /// Priority: owner player color > minimap color override > faction color > default.
        /// </summary>
        private Color ResolveAgentColor(CrowdAgent agent, EnemyCrowd crowd)
        {
            if (crowd == null) return defaultColor;

            // Priority 1: Owner player color
            if (crowd.useOwnerPlayerColor && agent != null)
            {
                int ownerId = agent.ownerPlayerId;
                if (ownerId >= 0)
                {
                    var player = PlayerRegistry.GetPlayerByIndex(ownerId);
                    if (player != null)
                        return player.playerColor;
                }
            }

            // Priority 2: Minimap color override
            if (crowd.useMinimapColorOverride)
                return crowd.minimapColorOverride;

            // Priority 3: Faction color
            return useFactionColors ? crowd.factionColor : defaultColor;
        }

        #endregion
    }
}
