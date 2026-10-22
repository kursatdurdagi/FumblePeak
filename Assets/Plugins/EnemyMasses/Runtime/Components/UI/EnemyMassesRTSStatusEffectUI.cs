using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Arawn.EnemyMasses.Runtime
{
    [AddComponentMenu("Enemy Masses/RTS/Status Effect UI")]
    public class EnemyMassesRTSStatusEffectUI : MonoBehaviour
    {
        public enum DisplayScope
        {
            SelectedAgentsOnly,
            Always
        }

        public enum FactionFilter
        {
            All,
            PlayerControlledOnly,
            NonPlayerOnly
        }

        [System.Serializable]
        public class StatusEffectIconOverride
        {
            public StatusEffectType effectType;
            public Sprite icon;
        }

        [Header("References")]
        [Tooltip("RTS controller used to read selection data. If empty, tries to find one.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("Crowd controller used to read status effects. If empty, tries to find one.")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Prefab or scene object used as the status effect template.")]
        public GameObject statusEffectPrefab;

        [Tooltip("Parent container for spawned status effect entries. If empty, uses this RectTransform.")]
        public RectTransform statusEffectContainer;

        [Header("Display")]
        public DisplayScope displayScope = DisplayScope.SelectedAgentsOnly;

        [Tooltip("Filter by RTS player-controlled factions. Requires RTS controller with playerControlledFactions set.")]
        public FactionFilter factionFilter = FactionFilter.All;

        [Tooltip("Show effects on dead agents (if any remain).")]
        public bool includeDeadAgents = false;

        [Tooltip("Maximum number of status effect entries to show (0 = unlimited).")]
        public int maxItems = 100;

        [Header("Category Filters")]
        public bool showCrowdControl = true;
        public bool showDamageOverTime = true;
        public bool showDebuffs = true;
        public bool showBuffs = true;

        [Header("Update")]
        [Tooltip("Seconds between UI refreshes.")]
        [Range(0.02f, 1f)]
        public float updateInterval = 0.1f;

        [Tooltip("Disable the template object in the scene at runtime.")]
        public bool hideTemplateAtRuntime = true;

        [Header("Icons")]
        [Tooltip("Child path under the entry to find the icon Image (e.g., 'Icon').")]
        public string iconChildPath = "Icon";

        [Tooltip("Fallback to the first Image found in the entry hierarchy if the path is not found.")]
        public bool fallbackToAnyImage = true;

        [Tooltip("Optional icon overrides per status effect type.")]
        public List<StatusEffectIconOverride> iconOverrides = new List<StatusEffectIconOverride>();

        public Sprite defaultBuffIcon;
        public Sprite defaultDebuffIcon;
        public Sprite defaultDotIcon;
        public Sprite defaultCrowdControlIcon;

        [Header("Colors")]
        public bool colorizeIcons = true;
        public bool colorizeLabels = false;
        public Color buffColor = new Color(0.2f, 1f, 0.4f, 1f);
        public Color debuffColor = new Color(1f, 0.3f, 0.25f, 1f);
        public Color dotColor = new Color(1f, 0.6f, 0.2f, 1f);
        public Color crowdControlColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Labels")]
        [Tooltip("Show a label per status effect entry.")]
        public bool showLabels = true;

        [Tooltip("Child path under the entry to find a label Text (e.g., 'Label').")]
        public string labelChildPath = "Label";

        [Tooltip("Fallback to the first Text found in the entry hierarchy if the path is not found.")]
        public bool fallbackToAnyText = true;

        [Tooltip("Label format tokens: {effect}, {category}, {stacks}, {remaining}, {agent}, {faction}, {type}")]
        public string labelFormat = "{effect} {remaining}";

        [Tooltip("Show stack counts in the label (when stacks are present).")]
        public bool showStacks = true;

        [Tooltip("Hide stacks when value is 1.")]
        public bool showStacksOnlyWhenStacked = true;

        [Tooltip("Show remaining time in the label.")]
        public bool showRemainingTime = true;

        [Tooltip("Numeric format for remaining time (seconds).")]
        public string remainingTimeFormat = "0.#";

        [Tooltip("Suffix for remaining time (e.g., 's').")]
        public string remainingTimeSuffix = "s";

        [Tooltip("Label to use for effects with no expiry.")]
        public string permanentLabel = "Permanent";

        private readonly List<StatusEffectEntry> _entries = new List<StatusEffectEntry>();
        private readonly List<GameObject> _items = new List<GameObject>();
        private readonly List<Image> _itemIcons = new List<Image>();
        private readonly List<Text> _itemLabels = new List<Text>();
        private readonly Dictionary<StatusEffectType, Sprite> _iconLookup = new Dictionary<StatusEffectType, Sprite>();
        private readonly Dictionary<StatusEffectType, string> _effectNameCache = new Dictionary<StatusEffectType, string>();
        private float _nextUpdateTime;
        private bool _templateHidden;
        private bool _iconLookupDirty = true;

        private const float PermanentTimeThreshold = 999999f;

        private void Awake()
        {
            AutoResolveReferences();
        }

        private void OnEnable()
        {
            AutoResolveReferences();
            SubscribeSelectionEvents();
            HideTemplateIfNeeded();
            RefreshEntries(true);
        }

        private void OnDisable()
        {
            UnsubscribeSelectionEvents();
        }

        private void Update()
        {
            if (Time.time < _nextUpdateTime) return;
            RefreshEntries(false);
        }

        private void OnValidate()
        {
            if (statusEffectContainer == null)
            {
                if (transform is RectTransform rectTransform)
                {
                    statusEffectContainer = rectTransform;
                }
            }

            updateInterval = Mathf.Max(0.02f, updateInterval);
            maxItems = Mathf.Max(0, maxItems);
            _iconLookupDirty = true;
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

        private void HandleSelectionChanged(SelectionChangedEventData data)
        {
            RefreshEntries(true);
        }

        private void HandleSelectionCleared()
        {
            RefreshEntries(true);
        }

        public void RefreshEntries(bool force)
        {
            if (statusEffectPrefab == null)
            {
                SetActiveItemCount(0);
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
                    SetActiveItemCount(0);
                    return;
                }
            }

            _entries.Clear();
            BuildEntries();

            int desired = _entries.Count;
            if (maxItems > 0)
            {
                desired = Mathf.Min(desired, maxItems);
            }

            EnsureItemCount(desired);

            for (int i = 0; i < desired; i++)
            {
                var entry = _entries[i];
                ApplyEntry(i, entry);
            }

            SetActiveItemCount(desired);
        }

        private void BuildEntries()
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
                    if (AddAgentEffects(agentIndex, agents))
                        return;
                }
            }
            else
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    if (AddAgentEffects(i, agents))
                        return;
                }
            }
        }

        private bool AddAgentEffects(int agentIndex, List<CrowdAgent> agents)
        {
            if (agentIndex < 0 || agentIndex >= agents.Count) return false;

            var agent = agents[agentIndex];
            if (agent == null) return false;

            if (!includeDeadAgents && (agent.isDead || agent.health <= 0f)) return false;
            if (!PassesFactionFilter(agent)) return false;
            if (agent.activeEffectCount <= 0) return false;

            var effects = agent.activeStatusEffects;
            if (effects == null || effects.Length == 0) return false;

            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (!effect.isActive) continue;

                if (!PassesCategoryFilter(effect.type)) continue;

                bool isPermanent = effect.expireTime >= PermanentTimeThreshold;
                float remaining = isPermanent ? -1f : effect.expireTime - Time.time;
                if (!isPermanent && remaining <= 0f) continue;

                var entry = new StatusEffectEntry
                {
                    agentIndex = agentIndex,
                    effectType = effect.type,
                    category = StatusEffectHelper.GetCategory(effect.type),
                    stacks = Mathf.Max(1, effect.stacks),
                    remaining = remaining,
                    isPermanent = isPermanent,
                    enemyCrowd = agent.enemyCrowd
                };

                _entries.Add(entry);

                if (maxItems > 0 && _entries.Count >= maxItems)
                {
                    return true;
                }
            }

            return false;
        }

        private bool PassesCategoryFilter(StatusEffectType type)
        {
            switch (StatusEffectHelper.GetCategory(type))
            {
                case StatusEffectCategory.CrowdControl:
                    return showCrowdControl;
                case StatusEffectCategory.DamageOverTime:
                    return showDamageOverTime;
                case StatusEffectCategory.Debuff:
                    return showDebuffs;
                case StatusEffectCategory.Buff:
                    return showBuffs;
                default:
                    return true;
            }
        }

        private bool PassesFactionFilter(CrowdAgent agent)
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

        private void ApplyEntry(int index, StatusEffectEntry entry)
        {
            var item = GetItem(index);
            if (item == null) return;

            var icon = GetIconForItem(index);
            if (icon != null)
            {
                Sprite resolved = ResolveIcon(entry);
                if (resolved != null)
                {
                    icon.sprite = resolved;
                }

                if (colorizeIcons)
                {
                    icon.color = GetCategoryColor(entry.category);
                }
            }

            var label = GetLabelForItem(index);
            if (label != null)
            {
                if (showLabels)
                {
                    label.text = BuildLabel(entry);
                    if (colorizeLabels)
                    {
                        label.color = GetCategoryColor(entry.category);
                    }
                    label.gameObject.SetActive(true);
                }
                else
                {
                    label.gameObject.SetActive(false);
                }
            }
        }

        private string BuildLabel(StatusEffectEntry entry)
        {
            string factionName = entry.enemyCrowd != null ? entry.enemyCrowd.factionName : "Faction";
            string typeName = entry.enemyCrowd != null ? entry.enemyCrowd.enemyTypeName : "Type";

            string label = labelFormat ?? string.Empty;
            label = label.Replace("{effect}", GetEffectName(entry.effectType));
            label = label.Replace("{category}", entry.category.ToString());
            label = label.Replace("{agent}", entry.agentIndex.ToString());
            label = label.Replace("{faction}", factionName);
            label = label.Replace("{type}", typeName);

            string stacksText = entry.stacks.ToString();
            if (!showStacks || (showStacksOnlyWhenStacked && entry.stacks <= 1))
            {
                stacksText = string.Empty;
            }
            label = label.Replace("{stacks}", stacksText);

            string remainingText = string.Empty;
            if (showRemainingTime)
            {
                if (entry.isPermanent)
                {
                    remainingText = permanentLabel;
                }
                else
                {
                    float remaining = Mathf.Max(0f, entry.remaining);
                    remainingText = remaining.ToString(remainingTimeFormat) + remainingTimeSuffix;
                }
            }
            label = label.Replace("{remaining}", remainingText);

            return label;
        }

        private Color GetCategoryColor(StatusEffectCategory category)
        {
            switch (category)
            {
                case StatusEffectCategory.CrowdControl:
                    return crowdControlColor;
                case StatusEffectCategory.DamageOverTime:
                    return dotColor;
                case StatusEffectCategory.Debuff:
                    return debuffColor;
                case StatusEffectCategory.Buff:
                default:
                    return buffColor;
            }
        }

        private Sprite ResolveIcon(StatusEffectEntry entry)
        {
            if (_iconLookupDirty)
            {
                BuildIconLookup();
            }

            if (_iconLookup.TryGetValue(entry.effectType, out var icon) && icon != null)
            {
                return icon;
            }

            switch (entry.category)
            {
                case StatusEffectCategory.CrowdControl:
                    return defaultCrowdControlIcon;
                case StatusEffectCategory.DamageOverTime:
                    return defaultDotIcon;
                case StatusEffectCategory.Debuff:
                    return defaultDebuffIcon;
                case StatusEffectCategory.Buff:
                default:
                    return defaultBuffIcon;
            }
        }

        private void BuildIconLookup()
        {
            _iconLookup.Clear();
            if (iconOverrides == null)
            {
                _iconLookupDirty = false;
                return;
            }

            for (int i = 0; i < iconOverrides.Count; i++)
            {
                var entry = iconOverrides[i];
                if (entry == null || entry.icon == null) continue;
                _iconLookup[entry.effectType] = entry.icon;
            }

            _iconLookupDirty = false;
        }

        private string GetEffectName(StatusEffectType type)
        {
            if (_effectNameCache.TryGetValue(type, out var name))
            {
                return name;
            }

            name = NicifyEffectName(type.ToString());
            _effectNameCache[type] = name;
            return name;
        }

        private static string NicifyEffectName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var builder = new StringBuilder(raw.Length + 4);
            builder.Append(raw[0]);
            for (int i = 1; i < raw.Length; i++)
            {
                char current = raw[i];
                char previous = raw[i - 1];

                if (char.IsUpper(current) && !char.IsUpper(previous))
                {
                    builder.Append(' ');
                }
                else if (char.IsDigit(current) && !char.IsDigit(previous))
                {
                    builder.Append(' ');
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private void EnsureItemCount(int desired)
        {
            if (desired <= 0) return;

            RectTransform container = GetContainer();
            if (container == null)
            {
                Debug.LogWarning("[Enemy Masses] StatusEffectUI missing a valid container (RectTransform).", this);
                return;
            }

            while (_items.Count < desired)
            {
                var instance = Instantiate(statusEffectPrefab, container);
                instance.gameObject.SetActive(true);
                _items.Add(instance);
                _itemIcons.Add(GetIconImage(instance));
                _itemLabels.Add(GetLabelText(instance));
            }
        }

        private void SetActiveItemCount(int count)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null)
                {
                    _items[i].SetActive(i < count);
                }
                if (i < _itemLabels.Count && _itemLabels[i] != null)
                {
                    _itemLabels[i].gameObject.SetActive(i < count && showLabels);
                }
            }
        }

        private GameObject GetItem(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            return _items[index];
        }

        private Image GetIconForItem(int index)
        {
            if (index < 0 || index >= _items.Count) return null;

            while (_itemIcons.Count < _items.Count)
            {
                _itemIcons.Add(null);
            }

            if (_itemIcons[index] == null)
            {
                _itemIcons[index] = GetIconImage(_items[index]);
            }

            return _itemIcons[index];
        }

        private Text GetLabelForItem(int index)
        {
            if (index < 0 || index >= _items.Count) return null;

            while (_itemLabels.Count < _items.Count)
            {
                _itemLabels.Add(null);
            }

            if (_itemLabels[index] == null)
            {
                _itemLabels[index] = GetLabelText(_items[index]);
            }

            return _itemLabels[index];
        }

        private Image GetIconImage(GameObject item)
        {
            if (item == null) return null;

            if (!string.IsNullOrEmpty(iconChildPath))
            {
                var child = item.transform.Find(iconChildPath);
                if (child != null)
                {
                    return child.GetComponent<Image>();
                }
            }

            var rootImage = item.GetComponent<Image>();
            if (rootImage != null) return rootImage;

            if (fallbackToAnyImage)
            {
                return item.GetComponentInChildren<Image>(true);
            }

            return null;
        }

        private Text GetLabelText(GameObject item)
        {
            if (item == null) return null;

            if (!string.IsNullOrEmpty(labelChildPath))
            {
                var child = item.transform.Find(labelChildPath);
                if (child != null)
                {
                    return child.GetComponent<Text>();
                }
            }

            var rootText = item.GetComponent<Text>();
            if (rootText != null) return rootText;

            if (fallbackToAnyText)
            {
                return item.GetComponentInChildren<Text>(true);
            }

            return null;
        }

        private RectTransform GetContainer()
        {
            if (statusEffectContainer != null) return statusEffectContainer;
            if (transform is RectTransform rectTransform) return rectTransform;
            return null;
        }

        private void HideTemplateIfNeeded()
        {
            if (_templateHidden || !hideTemplateAtRuntime) return;
            if (statusEffectPrefab == null) return;

            if (statusEffectPrefab.scene.IsValid())
            {
                statusEffectPrefab.SetActive(false);
                _templateHidden = true;
            }
        }

        private struct StatusEffectEntry
        {
            public int agentIndex;
            public StatusEffectType effectType;
            public StatusEffectCategory category;
            public int stacks;
            public float remaining;
            public bool isPermanent;
            public EnemyCrowd enemyCrowd;
        }
    }
}
