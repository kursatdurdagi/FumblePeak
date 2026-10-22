using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Arawn.EnemyMasses.Runtime
{
    [AddComponentMenu("Enemy Masses/RTS/Health Bar UI")]
    public class EnemyMassesRTSHealthBarUI : MonoBehaviour
    {
        public enum DisplayScope
        {
            SelectedAgentsOnly,
            Always
        }

        public enum DisplayMode
        {
            IndividualAgents,
            Average
        }

        public enum AverageGrouping
        {
            Combined,
            EnemyCrowd,
            SpawnGroup
        }

        public enum FactionFilter
        {
            All,
            PlayerControlledOnly,
            NonPlayerOnly
        }

        [Header("References")]
        [Tooltip("RTS controller used to read selection data. If empty, tries to find one.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("Crowd controller used to read agent health. If empty, tries to find one.")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Slider prefab or scene Slider used as the health bar template.")]
        public Slider healthBarPrefab;

        [Tooltip("Parent container for spawned health bars. If empty, uses this RectTransform.")]
        public RectTransform healthBarContainer;

        [Header("Display")]
        public DisplayScope displayScope = DisplayScope.SelectedAgentsOnly;
        public DisplayMode displayMode = DisplayMode.IndividualAgents;
        public AverageGrouping averageGrouping = AverageGrouping.Combined;

        [Header("Average Settings")]
        [Tooltip("Include dead agents (0 health) in average calculations.")]
        public bool includeDeadAgentsInAverage = true;

        [Tooltip("Keep dead agents that were removed from selection so averages don't jump up.")]
        public bool preserveDeadSelectionInAverage = true;

        [Header("Faction Filter")]
        [Tooltip("Filter by RTS player-controlled factions. Requires RTS controller with playerControlledFactions set.")]
        public FactionFilter factionFilter = FactionFilter.All;

        [Tooltip("Show bars for dead agents (health <= 0).")]
        public bool includeDeadAgents = false;

        [Tooltip("Maximum number of health bars to show (0 = unlimited).")]
        public int maxBars = 50;

        [Header("Update")]
        [Tooltip("Seconds between UI refreshes.")]
        [Range(0.02f, 1f)]
        public float updateInterval = 0.1f;

        [Tooltip("Disable the template Slider in the scene at runtime.")]
        public bool hideTemplateAtRuntime = true;

        [Header("Performance")]
        [Tooltip("Use event-driven aggregation for average health calculations.")]
        public bool useAggregateCache = true;

        [Tooltip("Seconds between full aggregate cache rebuilds (0 = rebuild only when needed).")]
        [Range(0f, 5f)]
        public float aggregateResyncInterval = 1f;

        [Header("Colors")]
        public Color fullHealthColor = new Color(0.2f, 1f, 0.2f, 1f);
        public Color midHealthColor = new Color(1f, 0.85f, 0.2f, 1f);
        public Color lowHealthColor = new Color(1f, 0.25f, 0.2f, 1f);

        [Range(0f, 1f)]
        public float midHealthThreshold = 0.6f;

        [Range(0f, 1f)]
        public float lowHealthThreshold = 0.3f;

        [Header("Labels")]
        [Tooltip("Show a label per health bar (optional).")]
        public bool showLabels = false;

        [Tooltip("Child path under the slider to find a label Text (e.g., 'Label').")]
        public string labelChildPath = "Label";

        [Tooltip("Fallback to the first Text found in the slider hierarchy if the path is not found.")]
        public bool fallbackToAnyText = true;

        [Tooltip("Label when showing a combined average of all agents.")]
        public string combinedLabel = "Combined";

        [Tooltip("Label when showing a combined average of the current selection.")]
        public string selectedLabel = "Selected";

        [Tooltip("Label format for EnemyCrowd averages. Tokens: {faction}, {type}, {index}")]
        public string enemyCrowdLabelFormat = "{faction} {type} ({index})";

        [Tooltip("Label format for SpawnGroup averages. Tokens: {group}, {faction}, {type}, {index}")]
        public string spawnGroupLabelFormat = "Spawn Group {group}";

        [Tooltip("Label format for individual agents. Tokens: {index}, {faction}, {type}")]
        public string agentLabelFormat = "Agent {index}";

        private readonly List<Slider> _bars = new List<Slider>();
        private readonly List<float> _barValues = new List<float>();
        private readonly List<Image> _barFills = new List<Image>();
        private readonly List<Text> _barLabels = new List<Text>();
        private readonly List<string> _labelValues = new List<string>();
        private readonly HashSet<int> _selectedIndexCache = new HashSet<int>();
        private readonly HashSet<int> _deadSelectionIndices = new HashSet<int>();
        private readonly Dictionary<int, DeadSelectionSnapshot> _deadSelectionSnapshots = new Dictionary<int, DeadSelectionSnapshot>();
        private readonly List<AgentCacheEntry> _agentCache = new List<AgentCacheEntry>();
        private readonly Dictionary<int, AggregateTotals> _crowdTotals = new Dictionary<int, AggregateTotals>();
        private readonly Dictionary<int, AggregateTotals> _spawnGroupTotals = new Dictionary<int, AggregateTotals>();
        private readonly Dictionary<int, AggregateTotals> _selectedCrowdTotals = new Dictionary<int, AggregateTotals>();
        private readonly Dictionary<int, AggregateTotals> _selectedSpawnGroupTotals = new Dictionary<int, AggregateTotals>();
        private readonly Dictionary<int, AggregateTotals> _scratchTotals = new Dictionary<int, AggregateTotals>();
        private readonly List<int> _scratchKeys = new List<int>();
        private AggregateTotals _combinedTotals;
        private AggregateTotals _selectedCombinedTotals;
        private bool _aggregateCacheDirty = true;
        private bool _selectedTotalsDirty = true;
        private float _nextAggregateResyncTime;
        private int _cachedAgentCount;
        private FactionFilter _cachedFactionFilter = FactionFilter.All;
        private EnemyMassesCrowdController _healthEventController;
        private float _nextUpdateTime;
        private bool _templateHidden;

        private void Awake()
        {
            AutoResolveReferences();
        }

        private void OnEnable()
        {
            AutoResolveReferences();
            SubscribeSelectionEvents();
            SubscribeHealthEvents();
            HideTemplateIfNeeded();
            ClearSelectionCaches();
            ClearAggregateCache();
            RefreshBars(true);
        }

        private void OnDisable()
        {
            UnsubscribeSelectionEvents();
            UnsubscribeHealthEvents();
            ClearSelectionCaches();
            ClearAggregateCache();
        }

        private void Update()
        {
            if (Time.time < _nextUpdateTime) return;
            RefreshBars(false);
        }

        private void OnValidate()
        {
            if (healthBarContainer == null)
            {
                if (transform is RectTransform rectTransform)
                {
                    healthBarContainer = rectTransform;
                }
            }

            updateInterval = Mathf.Max(0.02f, updateInterval);
            aggregateResyncInterval = Mathf.Max(0f, aggregateResyncInterval);
            maxBars = Mathf.Max(0, maxBars);
            midHealthThreshold = Mathf.Clamp01(midHealthThreshold);
            lowHealthThreshold = Mathf.Clamp01(lowHealthThreshold);
            if (lowHealthThreshold > midHealthThreshold)
            {
                lowHealthThreshold = midHealthThreshold;
            }
        }

        private void AutoResolveReferences()
        {
            if (rtsController == null)
            {
#if UNITY_2023_1_OR_NEWER
                rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
#else
                rtsController = FindObjectOfType<EnemyMassesRTSController>();
#endif
            }

            if (crowdController == null && rtsController != null)
            {
                crowdController = rtsController.crowdController;
            }

            if (crowdController == null)
            {
#if UNITY_2023_1_OR_NEWER
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
#else
                crowdController = FindObjectOfType<EnemyMassesCrowdController>();
#endif
            }

            SubscribeHealthEvents();
        }

        private void SubscribeSelectionEvents()
        {
            if (rtsController == null) return;
            rtsController.SelectionChanged += HandleSelectionChanged;
            rtsController.SelectionCleared += HandleSelectionCleared;
        }

        private void UnsubscribeSelectionEvents()
        {
            if (rtsController == null) return;
            rtsController.SelectionChanged -= HandleSelectionChanged;
            rtsController.SelectionCleared -= HandleSelectionCleared;
        }

        private void SubscribeHealthEvents()
        {
            if (_healthEventController == crowdController) return;

            if (_healthEventController != null)
            {
                _healthEventController.EventAgentHealthChanged -= HandleAgentHealthChanged;
            }

            _healthEventController = crowdController;

            if (_healthEventController != null)
            {
                _healthEventController.EventAgentHealthChanged += HandleAgentHealthChanged;
            }

            _aggregateCacheDirty = true;
            _selectedTotalsDirty = true;
        }

        private void UnsubscribeHealthEvents()
        {
            if (_healthEventController == null) return;
            _healthEventController.EventAgentHealthChanged -= HandleAgentHealthChanged;
            _healthEventController = null;
        }

        private void HandleSelectionChanged(SelectionChangedEventData data)
        {
            UpdateSelectionCaches(data);
            RefreshBars(true);
        }

        private void HandleSelectionCleared()
        {
            ClearSelectionCaches();
            RefreshBars(true);
        }

        private void HandleAgentHealthChanged(int agentIndex, float newHealth, float maxHealth)
        {
            if (!useAggregateCache) return;
            if (_aggregateCacheDirty) return;

            if (agentIndex < 0 || agentIndex >= _agentCache.Count)
            {
                _aggregateCacheDirty = true;
                _selectedTotalsDirty = true;
                return;
            }

            AgentCacheEntry entry = _agentCache[agentIndex];
            float clampedHealth = Mathf.Max(0f, newHealth);
            float resolvedMax = maxHealth > 0f ? maxHealth : entry.maxHealth;
            bool newDead = clampedHealth <= 0f;

            float deltaHealth = clampedHealth - entry.health;
            float oldMax = entry.maxHealth;
            bool oldDead = entry.isDead;

            entry.health = clampedHealth;
            entry.maxHealth = resolvedMax;
            entry.isDead = newDead;
            _agentCache[agentIndex] = entry;

            if (entry.passesFilter)
            {
                ApplyAgentDelta(ref _combinedTotals, deltaHealth, oldMax, resolvedMax, oldDead, newDead);
                UpdateAggregateDictionary(_crowdTotals, entry.crowdKey, deltaHealth, oldMax, resolvedMax, oldDead, newDead);
                UpdateAggregateDictionary(_spawnGroupTotals, entry.spawnGroupKey, deltaHealth, oldMax, resolvedMax, oldDead, newDead);

                if (!_selectedTotalsDirty && _selectedIndexCache.Contains(agentIndex))
                {
                    ApplyAgentDelta(ref _selectedCombinedTotals, deltaHealth, oldMax, resolvedMax, oldDead, newDead);
                    UpdateAggregateDictionary(_selectedCrowdTotals, entry.crowdKey, deltaHealth, oldMax, resolvedMax, oldDead, newDead);
                    UpdateAggregateDictionary(_selectedSpawnGroupTotals, entry.spawnGroupKey, deltaHealth, oldMax, resolvedMax, oldDead, newDead);
                }
            }
        }

        public void RefreshBars(bool force)
        {
            if (healthBarPrefab == null)
            {
                SetActiveBarCount(0);
                return;
            }

            AutoResolveReferences();

            if (!force && Time.time < _nextUpdateTime) return;
            _nextUpdateTime = Time.time + updateInterval;

            if (displayScope == DisplayScope.SelectedAgentsOnly)
            {
                var selected = rtsController != null ? rtsController.SelectedAgents : null;
                if (selected == null || selected.Count == 0)
                {
                    SetActiveBarCount(0);
                    return;
                }
            }

            _barValues.Clear();
            _labelValues.Clear();

            if (displayMode == DisplayMode.IndividualAgents)
            {
                BuildIndividualBars();
            }
            else
            {
                BuildAverageBars();
            }

            int desired = _barValues.Count;
            if (maxBars > 0) desired = Mathf.Min(desired, maxBars);

            EnsureBarCount(desired);

            for (int i = 0; i < desired; i++)
            {
                var slider = _bars[i];
                if (slider == null) continue;
                slider.gameObject.SetActive(true);
                float value = Mathf.Clamp01(_barValues[i]);
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.wholeNumbers = false;
                slider.SetValueWithoutNotify(value);
                ApplyBarColor(i, value);
                ApplyBarLabel(i);
            }

            SetActiveBarCount(desired);
        }

        private void BuildIndividualBars()
        {
            if (crowdController == null) return;

            var agents = crowdController.GetAgents();
            if (agents == null || agents.Count == 0) return;

            if (displayScope == DisplayScope.SelectedAgentsOnly)
            {
                var selected = rtsController != null ? rtsController.SelectedAgents : null;
                if (selected == null) return;

                for (int i = 0; i < selected.Count; i++)
                {
                    int agentIndex = selected[i];
                    if (TryGetAgentHealth(agentIndex, agents, out float health, out float maxHealth))
                    {
                        _barValues.Add(health / maxHealth);
                        if (showLabels)
                        {
                            _labelValues.Add(BuildAgentLabel(agentIndex, agents[agentIndex]));
                        }
                        if (maxBars > 0 && _barValues.Count >= maxBars) break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    if (TryGetAgentHealth(i, agents, out float health, out float maxHealth))
                    {
                        _barValues.Add(health / maxHealth);
                        if (showLabels)
                        {
                            _labelValues.Add(BuildAgentLabel(i, agents[i]));
                        }
                        if (maxBars > 0 && _barValues.Count >= maxBars) break;
                    }
                }
            }
        }

        private void BuildAverageBars()
        {
            if (crowdController == null) return;

            if (useAggregateCache)
            {
                if (displayScope == DisplayScope.SelectedAgentsOnly)
                {
                    if (TryBuildSelectionAverageBarsFromCache())
                    {
                        return;
                    }
                }
                else
                {
                    if (TryBuildAlwaysAverageBarsFromCache())
                    {
                        return;
                    }
                }
            }

            var agents = crowdController.GetAgents();
            if (agents == null || agents.Count == 0) return;

            if (averageGrouping == AverageGrouping.Combined)
            {
                if (TryGetCombinedAverage(agents, out float value))
                {
                    _barValues.Add(value);
                    if (showLabels)
                    {
                        _labelValues.Add(displayScope == DisplayScope.SelectedAgentsOnly ? selectedLabel : combinedLabel);
                    }
                }

                return;
            }

            var totals = new Dictionary<int, Vector2>();
            Dictionary<int, string> labels = showLabels ? new Dictionary<int, string>() : null;

            if (displayScope == DisplayScope.SelectedAgentsOnly)
            {
                var selected = rtsController != null ? rtsController.SelectedAgents : null;
                if (selected == null) return;

                for (int i = 0; i < selected.Count; i++)
                {
                    int agentIndex = selected[i];
                    if (!TryGetAverageInputs(agentIndex, agents, out float health, out float maxHealth, out CrowdAgent agent))
                    {
                        continue;
                    }

                    int key = GetGroupingKey(agent, averageGrouping);
                    Accumulate(totals, key, health, maxHealth);
                    if (showLabels)
                    {
                        RegisterGroupLabel(labels, key, agent, averageGrouping);
                    }
                }

                AppendDeadSelectionAverages(agents, totals, labels);
            }
            else
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    if (!TryGetAverageInputs(i, agents, out float health, out float maxHealth, out CrowdAgent agent))
                    {
                        continue;
                    }

                    int key = GetGroupingKey(agent, averageGrouping);
                    Accumulate(totals, key, health, maxHealth);
                    if (showLabels)
                    {
                        RegisterGroupLabel(labels, key, agent, averageGrouping);
                    }
                }
            }

            if (totals.Count == 0) return;

            var keys = new List<int>(totals.Keys);
            keys.Sort();

            for (int i = 0; i < keys.Count; i++)
            {
                var totalsEntry = totals[keys[i]];
                if (totalsEntry.y <= 0f) continue;
                _barValues.Add(totalsEntry.x / totalsEntry.y);
                if (showLabels)
                {
                    _labelValues.Add(labels != null && labels.TryGetValue(keys[i], out var label) ? label : combinedLabel);
                }
                if (maxBars > 0 && _barValues.Count >= maxBars) break;
            }
        }

        private bool TryBuildAlwaysAverageBarsFromCache()
        {
            EnsureAggregateCache();
            if (_aggregateCacheDirty) return false;

            if (averageGrouping == AverageGrouping.Combined)
            {
                if (TryGetAverageFromTotals(_combinedTotals, out float value))
                {
                    _barValues.Add(value);
                    if (showLabels)
                    {
                        _labelValues.Add(combinedLabel);
                    }
                }

                return true;
            }

            var totals = averageGrouping == AverageGrouping.EnemyCrowd ? _crowdTotals : _spawnGroupTotals;
            if (totals.Count == 0) return true;

            _scratchKeys.Clear();
            foreach (var key in totals.Keys)
            {
                _scratchKeys.Add(key);
            }
            _scratchKeys.Sort();

            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                int key = _scratchKeys[i];
                if (!totals.TryGetValue(key, out var entry)) continue;
                if (!TryGetAverageFromTotals(entry, out float value)) continue;

                _barValues.Add(value);
                if (showLabels)
                {
                    _labelValues.Add(averageGrouping == AverageGrouping.SpawnGroup
                        ? BuildSpawnGroupLabel(key)
                        : BuildEnemyCrowdLabel(key));
                }

                if (maxBars > 0 && _barValues.Count >= maxBars) break;
            }

            return true;
        }

        private bool TryBuildSelectionAverageBarsFromCache()
        {
            if (rtsController == null) return false;
            EnsureAggregateCache();
            if (_aggregateCacheDirty) return false;

            var selected = rtsController.SelectedAgents;
            if (selected == null || selected.Count == 0) return true;

            if (_selectedTotalsDirty)
            {
                RebuildSelectionTotals();
            }
            if (_selectedTotalsDirty) return false;

            if (averageGrouping == AverageGrouping.Combined)
            {
                AggregateTotals totals = _selectedCombinedTotals;
                AppendDeadSelectionCombinedTotals(crowdController.GetAgents(), ref totals);

                if (TryGetAverageFromTotals(totals, out float value))
                {
                    _barValues.Add(value);
                    if (showLabels)
                    {
                        _labelValues.Add(selectedLabel);
                    }
                }

                return true;
            }

            var source = averageGrouping == AverageGrouping.EnemyCrowd ? _selectedCrowdTotals : _selectedSpawnGroupTotals;
            if (source.Count == 0) return true;

            _scratchTotals.Clear();
            foreach (var entry in source)
            {
                _scratchTotals[entry.Key] = entry.Value;
            }

            AppendDeadSelectionGroupTotals(crowdController.GetAgents(), _scratchTotals);

            _scratchKeys.Clear();
            foreach (var key in _scratchTotals.Keys)
            {
                _scratchKeys.Add(key);
            }
            _scratchKeys.Sort();

            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                int key = _scratchKeys[i];
                if (!_scratchTotals.TryGetValue(key, out var totals)) continue;
                if (!TryGetAverageFromTotals(totals, out float value)) continue;

                _barValues.Add(value);
                if (showLabels)
                {
                    _labelValues.Add(averageGrouping == AverageGrouping.SpawnGroup
                        ? BuildSpawnGroupLabel(key)
                        : BuildEnemyCrowdLabel(key));
                }

                if (maxBars > 0 && _barValues.Count >= maxBars) break;
            }

            return true;
        }

        private bool TryGetAverageFromTotals(AggregateTotals totals, out float value)
        {
            value = 0f;
            float totalMax = includeDeadAgentsInAverage ? totals.totalMaxAll : totals.totalMaxAlive;
            if (totalMax <= 0f) return false;
            value = totals.totalHealth / totalMax;
            return true;
        }

        private bool TryGetCombinedAverage(List<CrowdAgent> agents, out float value)
        {
            value = 0f;
            float totalHealth = 0f;
            float totalMax = 0f;

            if (displayScope == DisplayScope.SelectedAgentsOnly)
            {
                var selected = rtsController != null ? rtsController.SelectedAgents : null;
                if (selected == null || selected.Count == 0) return false;

                for (int i = 0; i < selected.Count; i++)
                {
                    int agentIndex = selected[i];
                    if (TryGetAverageInputs(agentIndex, agents, out float health, out float maxHealth))
                    {
                        totalHealth += health;
                        totalMax += maxHealth;
                    }
                }

                AppendDeadSelectionCombined(agents, ref totalHealth, ref totalMax);
            }
            else
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    if (TryGetAverageInputs(i, agents, out float health, out float maxHealth))
                    {
                        totalHealth += health;
                        totalMax += maxHealth;
                    }
                }
            }

            if (totalMax <= 0f) return false;
            value = totalHealth / totalMax;
            return true;
        }

        private bool TryGetAgentHealth(int agentIndex, List<CrowdAgent> agents, out float health, out float maxHealth)
        {
            return TryGetAgentHealth(agentIndex, agents, out health, out maxHealth, out _);
        }

        private bool TryGetAgentHealth(int agentIndex, List<CrowdAgent> agents, out float health, out float maxHealth, out CrowdAgent agent)
        {
            health = 0f;
            maxHealth = 0f;
            agent = null;

            if (agents == null || agentIndex < 0 || agentIndex >= agents.Count) return false;

            agent = agents[agentIndex];
            if (agent == null) return false;

            if (!includeDeadAgents && (agent.isDead || agent.health <= 0f)) return false;
            if (!PassesFactionFilter(agentIndex, agent)) return false;

            health = Mathf.Max(0f, agent.health);
            maxHealth = crowdController != null ? crowdController.GetAgentMaxHealth(agentIndex) : 0f;

            if (maxHealth <= 0f) return false;
            return true;
        }

        private bool TryGetAverageInputs(int agentIndex, List<CrowdAgent> agents, out float health, out float maxHealth)
        {
            return TryGetAverageInputs(agentIndex, agents, out health, out maxHealth, out _);
        }

        private bool TryGetAverageInputs(int agentIndex, List<CrowdAgent> agents, out float health, out float maxHealth, out CrowdAgent agent)
        {
            health = 0f;
            maxHealth = 0f;
            agent = null;

            if (agents == null || agentIndex < 0 || agentIndex >= agents.Count) return false;

            agent = agents[agentIndex];
            if (agent == null) return false;

            bool isDead = agent.isDead || agent.health <= 0f;
            if (!includeDeadAgentsInAverage && isDead) return false;
            if (!PassesFactionFilter(agentIndex, agent)) return false;

            maxHealth = crowdController != null ? crowdController.GetAgentMaxHealth(agentIndex) : 0f;
            if (maxHealth <= 0f) return false;

            health = isDead ? 0f : Mathf.Max(0f, agent.health);
            return true;
        }

        private int GetGroupingKey(CrowdAgent agent, AverageGrouping grouping)
        {
            if (agent == null) return -1;

            switch (grouping)
            {
                case AverageGrouping.SpawnGroup:
                {
                    if (agent.spawnGroupId >= 0)
                    {
                        return agent.spawnGroupId;
                    }

                    return 100000 + Mathf.Max(0, agent.enemyCrowdIndex);
                }
                case AverageGrouping.EnemyCrowd:
                default:
                    return agent.enemyCrowdIndex;
            }
        }

        private int GetSpawnGroupKey(CrowdAgent agent)
        {
            if (agent == null) return -1;
            if (agent.spawnGroupId >= 0) return agent.spawnGroupId;
            return 100000 + Mathf.Max(0, agent.enemyCrowdIndex);
        }

        private void Accumulate(Dictionary<int, Vector2> totals, int key, float health, float maxHealth)
        {
            if (totals.TryGetValue(key, out var current))
            {
                totals[key] = new Vector2(current.x + health, current.y + maxHealth);
            }
            else
            {
                totals[key] = new Vector2(health, maxHealth);
            }
        }

        private void AddAgentToTotals(ref AggregateTotals totals, float health, float maxHealth, bool isDead)
        {
            if (maxHealth <= 0f) return;
            totals.totalHealth += health;
            totals.totalMaxAll += maxHealth;
            if (!isDead)
            {
                totals.totalMaxAlive += maxHealth;
            }
        }

        private void AddAgentToDictionary(Dictionary<int, AggregateTotals> totals, int key, float health, float maxHealth, bool isDead)
        {
            if (maxHealth <= 0f) return;
            if (!totals.TryGetValue(key, out var current))
            {
                current = default;
            }

            AddAgentToTotals(ref current, health, maxHealth, isDead);
            totals[key] = current;
        }

        private void ApplyAgentDelta(ref AggregateTotals totals, float deltaHealth, float oldMax, float newMax, bool oldDead, bool newDead)
        {
            totals.totalHealth += deltaHealth;
            totals.totalMaxAll += (newMax - oldMax);

            if (oldDead == newDead)
            {
                if (!newDead)
                {
                    totals.totalMaxAlive += (newMax - oldMax);
                }
            }
            else if (oldDead && !newDead)
            {
                totals.totalMaxAlive += newMax;
            }
            else if (!oldDead && newDead)
            {
                totals.totalMaxAlive -= oldMax;
            }

            if (totals.totalHealth < 0f) totals.totalHealth = 0f;
            if (totals.totalMaxAll < 0f) totals.totalMaxAll = 0f;
            if (totals.totalMaxAlive < 0f) totals.totalMaxAlive = 0f;
        }

        private void UpdateAggregateDictionary(Dictionary<int, AggregateTotals> totals, int key, float deltaHealth, float oldMax, float newMax, bool oldDead, bool newDead)
        {
            if (!totals.TryGetValue(key, out var current))
            {
                _aggregateCacheDirty = true;
                _selectedTotalsDirty = true;
                return;
            }

            ApplyAgentDelta(ref current, deltaHealth, oldMax, newMax, oldDead, newDead);
            totals[key] = current;
        }

        private void RegisterGroupLabel(Dictionary<int, string> labels, int key, CrowdAgent agent, AverageGrouping grouping)
        {
            if (labels == null || labels.ContainsKey(key)) return;
            labels[key] = BuildGroupLabel(agent, grouping);
        }

        private string BuildGroupLabel(CrowdAgent agent, AverageGrouping grouping)
        {
            if (agent == null) return combinedLabel;

            switch (grouping)
            {
                case AverageGrouping.SpawnGroup:
                    if (agent.spawnGroupId >= 0)
                    {
                        return FormatSpawnGroupLabel(agent.spawnGroupId, agent.enemyCrowdIndex,
                            agent.enemyCrowd != null ? agent.enemyCrowd.factionName : string.Empty,
                            agent.enemyCrowd != null ? agent.enemyCrowd.enemyTypeName : string.Empty);
                    }
                    return BuildEnemyCrowdLabel(agent);
                case AverageGrouping.EnemyCrowd:
                default:
                    return BuildEnemyCrowdLabel(agent);
            }
        }

        private string BuildEnemyCrowdLabel(CrowdAgent agent)
        {
            string faction = agent.enemyCrowd != null ? agent.enemyCrowd.factionName : "Faction";
            string type = agent.enemyCrowd != null ? agent.enemyCrowd.enemyTypeName : "Type";
            int index = agent.enemyCrowdIndex;

            return enemyCrowdLabelFormat
                .Replace("{faction}", faction)
                .Replace("{type}", type)
                .Replace("{index}", index.ToString());
        }

        private string BuildEnemyCrowdLabel(int enemyCrowdIndex)
        {
            var crowd = crowdController != null ? crowdController.GetEnemyCrowdAtIndex(enemyCrowdIndex) : null;
            string faction = crowd != null ? crowd.factionName : "Faction";
            string type = crowd != null ? crowd.enemyTypeName : "Type";

            return enemyCrowdLabelFormat
                .Replace("{faction}", faction)
                .Replace("{type}", type)
                .Replace("{index}", enemyCrowdIndex.ToString());
        }

        private string BuildSpawnGroupLabel(int spawnGroupKey)
        {
            if (spawnGroupKey >= 100000)
            {
                int crowdIndex = spawnGroupKey - 100000;
                var crowd = crowdController != null ? crowdController.GetEnemyCrowdAtIndex(crowdIndex) : null;
                string factionName = crowd != null ? crowd.factionName : string.Empty;
                string typeName = crowd != null ? crowd.enemyTypeName : string.Empty;
                return FormatSpawnGroupLabel(crowdIndex, crowdIndex, factionName, typeName);
            }

            int factionIndex = -1;
            string factionLabel = string.Empty;
            string typeLabel = string.Empty;

            if (crowdController != null)
            {
                var spawnGroups = crowdController.GetSpawnGroups();
                if (spawnGroups != null && spawnGroups.TryGetValue(spawnGroupKey, out var spawnGroup))
                {
                    factionIndex = spawnGroup.factionIndex;
                    factionLabel = spawnGroup.factionName;
                }

                var crowd = crowdController.GetEnemyCrowdAtIndex(factionIndex);
                if (crowd != null)
                {
                    if (string.IsNullOrEmpty(factionLabel))
                    {
                        factionLabel = crowd.factionName;
                    }
                    typeLabel = crowd.enemyTypeName;
                }
            }

            return FormatSpawnGroupLabel(spawnGroupKey, factionIndex, factionLabel, typeLabel);
        }

        private string FormatSpawnGroupLabel(int groupId, int factionIndex, string factionName, string typeName)
        {
            string label = spawnGroupLabelFormat;
            label = label.Replace("{group}", groupId.ToString());
            label = label.Replace("{faction}", string.IsNullOrEmpty(factionName) ? "Faction" : factionName);
            label = label.Replace("{type}", string.IsNullOrEmpty(typeName) ? "Type" : typeName);
            label = label.Replace("{index}", factionIndex >= 0 ? factionIndex.ToString() : groupId.ToString());
            return label;
        }

        private string BuildAgentLabel(int agentIndex, CrowdAgent agent)
        {
            string faction = agent != null && agent.enemyCrowd != null ? agent.enemyCrowd.factionName : "Faction";
            string type = agent != null && agent.enemyCrowd != null ? agent.enemyCrowd.enemyTypeName : "Type";

            return agentLabelFormat
                .Replace("{index}", agentIndex.ToString())
                .Replace("{faction}", faction)
                .Replace("{type}", type);
        }

        private bool PassesFactionFilter(int agentIndex, CrowdAgent agent)
        {
            if (factionFilter == FactionFilter.All) return true;

            bool isPlayer = IsPlayerControlled(agent);
            if (factionFilter == FactionFilter.PlayerControlledOnly) return isPlayer;
            return !isPlayer;
        }

        private bool IsPlayerControlled(CrowdAgent agent)
        {
            if (rtsController == null) return true;
            if (rtsController.AllFactionsSelectable) return true;

            string factionName = agent?.enemyCrowd != null ? agent.enemyCrowd.factionName : null;
            return !string.IsNullOrEmpty(factionName) &&
                   rtsController.playerControlledFactions != null &&
                   rtsController.playerControlledFactions.ContainsFaction(factionName);
        }

        private void UpdateSelectionCaches(SelectionChangedEventData data)
        {
            _selectedIndexCache.Clear();
            if (data?.selectedIndices != null)
            {
                for (int i = 0; i < data.selectedIndices.Count; i++)
                {
                    _selectedIndexCache.Add(data.selectedIndices[i]);
                }
            }

            if (!preserveDeadSelectionInAverage || !includeDeadAgentsInAverage || displayScope != DisplayScope.SelectedAgentsOnly)
            {
                _deadSelectionIndices.Clear();
                _deadSelectionSnapshots.Clear();
                _selectedTotalsDirty = true;
                if (useAggregateCache && displayMode == DisplayMode.Average)
                {
                    RebuildSelectionTotals();
                }
                return;
            }

            bool isReplacement = data != null &&
                                 data.newlySelected != null &&
                                 data.newlyDeselected != null &&
                                 data.totalSelected == data.newlySelected.Count &&
                                 data.newlyDeselected.Count > 0;

            if (isReplacement)
            {
                _deadSelectionIndices.Clear();
                _deadSelectionSnapshots.Clear();
            }

            if (data?.newlySelected != null)
            {
                for (int i = 0; i < data.newlySelected.Count; i++)
                {
                    int idx = data.newlySelected[i];
                    _deadSelectionIndices.Remove(idx);
                    _deadSelectionSnapshots.Remove(idx);
                }
            }

            if (data?.newlyDeselected != null)
            {
                for (int i = 0; i < data.newlyDeselected.Count; i++)
                {
                    int idx = data.newlyDeselected[i];
                    if (IsAgentDead(idx))
                    {
                        _deadSelectionIndices.Add(idx);
                        CacheDeadSelectionSnapshot(idx);
                    }
                    else
                    {
                        _deadSelectionIndices.Remove(idx);
                        _deadSelectionSnapshots.Remove(idx);
                    }
                }
            }

            _selectedTotalsDirty = true;
            if (useAggregateCache && displayMode == DisplayMode.Average)
            {
                RebuildSelectionTotals();
            }
        }

        private void ClearSelectionCaches()
        {
            _selectedIndexCache.Clear();
            _deadSelectionIndices.Clear();
            _deadSelectionSnapshots.Clear();
            _selectedCombinedTotals = default;
            _selectedCrowdTotals.Clear();
            _selectedSpawnGroupTotals.Clear();
            _selectedTotalsDirty = false;
        }

        private void ClearAggregateCache()
        {
            _agentCache.Clear();
            _crowdTotals.Clear();
            _spawnGroupTotals.Clear();
            _combinedTotals = default;
            _aggregateCacheDirty = true;
            _cachedAgentCount = 0;
            _nextAggregateResyncTime = 0f;
            _selectedCombinedTotals = default;
            _selectedCrowdTotals.Clear();
            _selectedSpawnGroupTotals.Clear();
            _selectedTotalsDirty = true;
        }

        private void EnsureAggregateCache()
        {
            if (!useAggregateCache || crowdController == null) return;

            bool filterChanged = _cachedFactionFilter != factionFilter;
            bool agentCountChanged = crowdController.GetAgentCount() != _cachedAgentCount;
            bool needsResync = aggregateResyncInterval > 0f && Time.time >= _nextAggregateResyncTime;

            if (_aggregateCacheDirty || filterChanged || agentCountChanged || needsResync)
            {
                RebuildAggregateCache();
            }
        }

        private void RebuildAggregateCache()
        {
            if (crowdController == null)
            {
                _aggregateCacheDirty = true;
                return;
            }

            var agents = crowdController.GetAgents();
            if (agents == null)
            {
                _aggregateCacheDirty = true;
                return;
            }

            _agentCache.Clear();
            _crowdTotals.Clear();
            _spawnGroupTotals.Clear();
            _combinedTotals = default;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null)
                {
                    _agentCache.Add(default);
                    continue;
                }

                float maxHealth = crowdController.GetAgentMaxHealth(i);
                if (maxHealth <= 0f)
                {
                    _agentCache.Add(default);
                    continue;
                }

                float health = Mathf.Max(0f, agent.health);
                bool isDead = agent.isDead || health <= 0f;
                int crowdKey = agent.enemyCrowdIndex;
                int spawnGroupKey = GetSpawnGroupKey(agent);
                bool passes = PassesFactionFilter(i, agent);

                var entry = new AgentCacheEntry
                {
                    health = health,
                    maxHealth = maxHealth,
                    crowdKey = crowdKey,
                    spawnGroupKey = spawnGroupKey,
                    isDead = isDead,
                    passesFilter = passes
                };

                _agentCache.Add(entry);

                if (!passes) continue;

                AddAgentToTotals(ref _combinedTotals, health, maxHealth, isDead);
                AddAgentToDictionary(_crowdTotals, crowdKey, health, maxHealth, isDead);
                AddAgentToDictionary(_spawnGroupTotals, spawnGroupKey, health, maxHealth, isDead);
            }

            _cachedAgentCount = agents.Count;
            _cachedFactionFilter = factionFilter;
            _nextAggregateResyncTime = aggregateResyncInterval > 0f ? Time.time + aggregateResyncInterval : float.PositiveInfinity;
            _aggregateCacheDirty = false;

            if (displayScope == DisplayScope.SelectedAgentsOnly && displayMode == DisplayMode.Average)
            {
                RebuildSelectionTotals();
            }
            else
            {
                _selectedTotalsDirty = true;
            }
        }

        private void RebuildSelectionTotals()
        {
            _selectedCombinedTotals = default;
            _selectedCrowdTotals.Clear();
            _selectedSpawnGroupTotals.Clear();
            _selectedTotalsDirty = false;

            if (!useAggregateCache || crowdController == null || rtsController == null) return;
            if (_aggregateCacheDirty) return;

            var selected = rtsController.SelectedAgents;
            if (selected == null || selected.Count == 0) return;

            for (int i = 0; i < selected.Count; i++)
            {
                int agentIndex = selected[i];
                if (agentIndex < 0 || agentIndex >= _agentCache.Count) continue;
                var entry = _agentCache[agentIndex];
                if (!entry.passesFilter || entry.maxHealth <= 0f) continue;

                AddAgentToTotals(ref _selectedCombinedTotals, entry.health, entry.maxHealth, entry.isDead);
                AddAgentToDictionary(_selectedCrowdTotals, entry.crowdKey, entry.health, entry.maxHealth, entry.isDead);
                AddAgentToDictionary(_selectedSpawnGroupTotals, entry.spawnGroupKey, entry.health, entry.maxHealth, entry.isDead);
            }
        }

        private bool IsAgentDead(int agentIndex)
        {
            if (crowdController == null) return false;
            var agents = crowdController.GetAgents();
            if (agents == null || agentIndex < 0 || agentIndex >= agents.Count) return false;
            var agent = agents[agentIndex];
            return agent != null && (agent.isDead || agent.health <= 0f);
        }

        private void CacheDeadSelectionSnapshot(int agentIndex)
        {
            if (_deadSelectionSnapshots.ContainsKey(agentIndex)) return;
            if (crowdController == null) return;
            var agents = crowdController.GetAgents();
            if (agents == null || agentIndex < 0 || agentIndex >= agents.Count) return;

            var agent = agents[agentIndex];
            if (agent == null) return;

            float maxHealth = crowdController.GetAgentMaxHealth(agentIndex);
            if (maxHealth <= 0f) return;

            var snapshot = new DeadSelectionSnapshot
            {
                enemyCrowdIndex = agent.enemyCrowdIndex,
                spawnGroupId = agent.spawnGroupId,
                factionName = agent.enemyCrowd != null ? agent.enemyCrowd.factionName : string.Empty,
                enemyTypeName = agent.enemyCrowd != null ? agent.enemyCrowd.enemyTypeName : string.Empty,
                maxHealth = maxHealth
            };

            _deadSelectionSnapshots[agentIndex] = snapshot;
        }

        private bool TryGetCachedDeadSnapshot(int agentIndex, out DeadSelectionSnapshot snapshot)
        {
            return _deadSelectionSnapshots.TryGetValue(agentIndex, out snapshot) && snapshot.maxHealth > 0f;
        }

        private void AppendDeadSelectionCombinedTotals(List<CrowdAgent> agents, ref AggregateTotals totals)
        {
            if (!preserveDeadSelectionInAverage || !includeDeadAgentsInAverage || _deadSelectionIndices.Count == 0) return;
            if (agents == null) return;

            foreach (int idx in _deadSelectionIndices)
            {
                if (_selectedIndexCache.Contains(idx)) continue;
                if (TryGetAverageInputs(idx, agents, out float health, out float maxHealth))
                {
                    AddAgentToTotals(ref totals, health, maxHealth, true);
                }
                else if (TryGetCachedDeadSnapshot(idx, out var snapshot))
                {
                    AddAgentToTotals(ref totals, 0f, snapshot.maxHealth, true);
                }
            }
        }

        private void AppendDeadSelectionGroupTotals(List<CrowdAgent> agents, Dictionary<int, AggregateTotals> totals)
        {
            if (!preserveDeadSelectionInAverage || !includeDeadAgentsInAverage || _deadSelectionIndices.Count == 0) return;
            if (agents == null) return;

            foreach (int idx in _deadSelectionIndices)
            {
                if (_selectedIndexCache.Contains(idx)) continue;
                if (TryGetAverageInputs(idx, agents, out float health, out float maxHealth, out CrowdAgent agent))
                {
                    int key = GetGroupingKey(agent, averageGrouping);
                    AddAgentToDictionary(totals, key, health, maxHealth, true);
                }
                else if (TryGetCachedDeadSnapshot(idx, out var snapshot))
                {
                    int key = GetGroupingKey(snapshot, averageGrouping);
                    AddAgentToDictionary(totals, key, 0f, snapshot.maxHealth, true);
                }
            }
        }

        private void AppendDeadSelectionCombined(List<CrowdAgent> agents, ref float totalHealth, ref float totalMax)
        {
            if (!preserveDeadSelectionInAverage || _deadSelectionIndices.Count == 0) return;

            foreach (int idx in _deadSelectionIndices)
            {
                if (_selectedIndexCache.Contains(idx)) continue;
                if (TryGetAverageInputs(idx, agents, out float health, out float maxHealth))
                {
                    totalHealth += health;
                    totalMax += maxHealth;
                }
                else if (TryGetCachedDeadSnapshot(idx, out var snapshot))
                {
                    totalMax += snapshot.maxHealth;
                }
            }
        }

        private void AppendDeadSelectionAverages(List<CrowdAgent> agents, Dictionary<int, Vector2> totals, Dictionary<int, string> labels)
        {
            if (!preserveDeadSelectionInAverage || _deadSelectionIndices.Count == 0) return;

            foreach (int idx in _deadSelectionIndices)
            {
                if (_selectedIndexCache.Contains(idx)) continue;
                if (TryGetAverageInputs(idx, agents, out float health, out float maxHealth, out CrowdAgent agent))
                {
                    int key = GetGroupingKey(agent, averageGrouping);
                    Accumulate(totals, key, health, maxHealth);
                    if (showLabels)
                    {
                        RegisterGroupLabel(labels, key, agent, averageGrouping);
                    }
                }
                else if (TryGetCachedDeadSnapshot(idx, out var snapshot))
                {
                    int key = GetGroupingKey(snapshot, averageGrouping);
                    Accumulate(totals, key, 0f, snapshot.maxHealth);
                    if (showLabels)
                    {
                        RegisterGroupLabel(labels, key, snapshot, averageGrouping);
                    }
                }
            }
        }

        private int GetGroupingKey(DeadSelectionSnapshot snapshot, AverageGrouping grouping)
        {
            switch (grouping)
            {
                case AverageGrouping.SpawnGroup:
                    if (snapshot.spawnGroupId >= 0)
                    {
                        return snapshot.spawnGroupId;
                    }

                    return 100000 + Mathf.Max(0, snapshot.enemyCrowdIndex);
                case AverageGrouping.EnemyCrowd:
                default:
                    return snapshot.enemyCrowdIndex;
            }
        }

        private void RegisterGroupLabel(Dictionary<int, string> labels, int key, DeadSelectionSnapshot snapshot, AverageGrouping grouping)
        {
            if (labels == null || labels.ContainsKey(key)) return;
            labels[key] = BuildGroupLabel(snapshot, grouping);
        }

        private string BuildGroupLabel(DeadSelectionSnapshot snapshot, AverageGrouping grouping)
        {
            switch (grouping)
            {
                case AverageGrouping.SpawnGroup:
                    if (snapshot.spawnGroupId >= 0)
                    {
                        return FormatSpawnGroupLabel(snapshot.spawnGroupId, snapshot.enemyCrowdIndex,
                            snapshot.factionName, snapshot.enemyTypeName);
                    }
                    return BuildEnemyCrowdLabel(snapshot);
                case AverageGrouping.EnemyCrowd:
                default:
                    return BuildEnemyCrowdLabel(snapshot);
            }
        }

        private string BuildEnemyCrowdLabel(DeadSelectionSnapshot snapshot)
        {
            string faction = string.IsNullOrEmpty(snapshot.factionName) ? "Faction" : snapshot.factionName;
            string type = string.IsNullOrEmpty(snapshot.enemyTypeName) ? "Type" : snapshot.enemyTypeName;
            int index = snapshot.enemyCrowdIndex;

            return enemyCrowdLabelFormat
                .Replace("{faction}", faction)
                .Replace("{type}", type)
                .Replace("{index}", index.ToString());
        }

        private struct AggregateTotals
        {
            public float totalHealth;
            public float totalMaxAlive;
            public float totalMaxAll;
        }

        private struct AgentCacheEntry
        {
            public float health;
            public float maxHealth;
            public int crowdKey;
            public int spawnGroupKey;
            public bool isDead;
            public bool passesFilter;
        }

        private struct DeadSelectionSnapshot
        {
            public int enemyCrowdIndex;
            public int spawnGroupId;
            public string factionName;
            public string enemyTypeName;
            public float maxHealth;
        }

        private void EnsureBarCount(int desired)
        {
            if (desired <= 0) return;

            RectTransform container = GetContainer();
            if (container == null)
            {
                Debug.LogWarning("[Enemy Masses] HealthBarUI missing a valid container (RectTransform).", this);
                return;
            }

            while (_bars.Count < desired)
            {
                var instance = Instantiate(healthBarPrefab, container);
                instance.gameObject.SetActive(true);
                _bars.Add(instance);
                _barFills.Add(GetFillImage(instance));
                _barLabels.Add(GetLabelText(instance));
            }
        }

        private void SetActiveBarCount(int count)
        {
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] != null)
                {
                    _bars[i].gameObject.SetActive(i < count);
                }
                if (i < _barLabels.Count && _barLabels[i] != null)
                {
                    _barLabels[i].gameObject.SetActive(i < count && showLabels);
                }
            }
        }

        private void ApplyBarColor(int index, float normalized)
        {
            var fill = GetFillImageForBar(index);
            if (fill == null) return;

            if (normalized <= 0f)
            {
                Color transparent = fill.color;
                transparent.a = 0f;
                fill.color = transparent;
                return;
            }

            if (normalized <= lowHealthThreshold)
            {
                fill.color = lowHealthColor;
            }
            else if (normalized <= midHealthThreshold)
            {
                fill.color = midHealthColor;
            }
            else
            {
                fill.color = fullHealthColor;
            }
        }

        private Image GetFillImageForBar(int index)
        {
            if (index < 0 || index >= _bars.Count) return null;

            while (_barFills.Count < _bars.Count)
            {
                _barFills.Add(null);
            }

            if (_barFills[index] == null)
            {
                _barFills[index] = GetFillImage(_bars[index]);
            }

            return _barFills[index];
        }

        private Image GetFillImage(Slider slider)
        {
            if (slider == null) return null;
            if (slider.fillRect != null)
            {
                return slider.fillRect.GetComponent<Image>();
            }

            var fill = slider.transform.Find("Fill Area/Fill");
            return fill != null ? fill.GetComponent<Image>() : null;
        }

        private void ApplyBarLabel(int index)
        {
            if (!showLabels) return;

            var label = GetLabelForBar(index);
            if (label == null) return;

            string text = index < _labelValues.Count ? _labelValues[index] : string.Empty;
            label.text = text;
            label.gameObject.SetActive(true);
        }

        private Text GetLabelForBar(int index)
        {
            if (index < 0 || index >= _bars.Count) return null;

            while (_barLabels.Count < _bars.Count)
            {
                _barLabels.Add(null);
            }

            if (_barLabels[index] == null)
            {
                _barLabels[index] = GetLabelText(_bars[index]);
            }

            return _barLabels[index];
        }

        private Text GetLabelText(Slider slider)
        {
            if (slider == null) return null;

            if (!string.IsNullOrEmpty(labelChildPath))
            {
                var child = slider.transform.Find(labelChildPath);
                if (child != null)
                {
                    return child.GetComponent<Text>();
                }
            }

            if (fallbackToAnyText)
            {
                return slider.GetComponentInChildren<Text>(true);
            }

            return null;
        }

        private RectTransform GetContainer()
        {
            if (healthBarContainer != null) return healthBarContainer;
            if (transform is RectTransform rectTransform) return rectTransform;
            return null;
        }

        private void HideTemplateIfNeeded()
        {
            if (_templateHidden || !hideTemplateAtRuntime) return;
            if (healthBarPrefab == null) return;

            if (healthBarPrefab.gameObject.scene.IsValid())
            {
                healthBarPrefab.gameObject.SetActive(false);
                _templateHidden = true;
            }
        }
    }
}
