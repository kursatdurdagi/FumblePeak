using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using Debug = UnityEngine.Debug;
#endif

#if TMP_PRESENT
using UITextComponent = TMPro.TextMeshProUGUI;
#else
using UITextComponent = UnityEngine.UI.Text;
#endif

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// Displays active status effects (buffs, debuffs, DOTs, crowd control) for the
    /// currently selected EnemyCrowd agents as a horizontal icon bar — similar to
    /// <see cref="AgentSkillBarUI"/> but for status effects.
    /// <para>
    /// Create via <b>Hierarchy → Enemy Masses → UI → Status Effect Info UI</b>.
    /// </para>
    /// <para>
    /// Icons are resolved via a shared <see cref="StatusEffectUIRegistry"/> ScriptableObject.
    /// If no registry is assigned, a per-component <see cref="iconOverrides"/> list is used instead.
    /// </para>
    /// </summary>
    [AddComponentMenu("Enemy Masses/UI/Status Effect Info UI")]
    public class StatusEffectInfoUI : MonoBehaviour
    {
        // ─── Serialized References ──────────────────────────────────────

        [Header("References")]
        [Tooltip("RTS controller for selection tracking. Auto-discovered if null.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("Crowd controller for agent data. Auto-discovered if null.")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Central icon registry (ScriptableObject). Create via Assets > Create > Enemy Masses > Status Effect UI Registry.")]
        public StatusEffectUIRegistry registry;

        [Header("UI Layout")]
        [Tooltip("Root RectTransform of the status bar (background).")]
        public RectTransform barRoot;

        [Tooltip("Container that holds all spawned effect slots.")]
        public RectTransform slotContainer;

        [Tooltip("Optional background image for the bar (auto-hidden when empty).")]
        public Image barBackground;

        [Header("Slot Appearance")]
        [Tooltip("Size of each effect icon slot.")]
        public Vector2 slotSize = new Vector2(36, 36);

        [Tooltip("Spacing between slots.")]
        public float slotSpacing = 4f;

        [Tooltip("Max effect slots to display (0 = unlimited).")]
        public int maxSlots = 24;

        [Header("Display")]
        [Tooltip("Show remaining time beneath each icon.")]
        public bool showDurationText = true;

        [Tooltip("Show stack count on icons with stacks > 1.")]
        public bool showStackCount = true;

        [Tooltip("Colorize icons by category (buff/debuff/dot/cc).")]
        public bool colorizeIcons = true;

        [Tooltip("Hide the bar when no effects are active.")]
        public bool autoHideWhenEmpty = true;

        [Header("Category Filters")]
        public bool showBuffs = true;
        public bool showDebuffs = true;
        public bool showDamageOverTime = true;
        public bool showCrowdControl = true;

        [Header("Tooltip")]
        [Tooltip("Show a tooltip panel when hovering over an effect slot.")]
        public bool enableTooltips = true;

        [Tooltip("Panel that displays tooltip content.")]
        public RectTransform tooltipPanel;

        [Tooltip("Text component inside the tooltip panel.")]
        public UITextComponent tooltipText;

        [Header("Update")]
        [Tooltip("Seconds between UI refreshes.")]
        [Range(0.02f, 0.5f)]
        public float updateInterval = 0.1f;

        [Tooltip("Log gathered effects to the console for debugging.")]
        public bool debugLog = false;

        [Header("Inline Icon Overrides (used when no Registry is assigned)")]
        [Tooltip("Per-effect icon overrides. Only used when Registry is null.")]
        public List<InlineIconOverride> iconOverrides = new List<InlineIconOverride>();

        public Sprite fallbackBuffIcon;
        public Sprite fallbackDebuffIcon;
        public Sprite fallbackDotIcon;
        public Sprite fallbackCCIcon;

        // ─── Types ──────────────────────────────────────────────────────

        [System.Serializable]
        public class InlineIconOverride
        {
            public StatusEffectType effectType;
            public Sprite icon;
        }

        private struct EffectSlotData
        {
            public StatusEffectType type;
            public StatusEffectCategory category;
            public int stacks;
            public float remaining;
            public bool isPermanent;
        }

        // ─── Runtime State ──────────────────────────────────────────────

        private readonly List<int> _selectedIndices = new List<int>();
        private readonly List<EffectSlotData> _activeEffects = new List<EffectSlotData>();
        private readonly List<GameObject> _slotObjects = new List<GameObject>();
        private readonly List<Image> _slotIcons = new List<Image>();
        private readonly List<UITextComponent> _slotDurationTexts = new List<UITextComponent>();
        private readonly List<UITextComponent> _slotStackTexts = new List<UITextComponent>();
        private readonly Dictionary<StatusEffectType, Sprite> _inlineLookup = new Dictionary<StatusEffectType, Sprite>();
        private bool _inlineLookupDirty = true;
        private float _nextUpdateTime;
        private int _hoveredSlotIndex = -1;
        private CanvasGroup _canvasGroup;

        private const float PermanentTimeThreshold = 999999f;
        private static readonly StringBuilder _sb = new StringBuilder(64);

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            AutoResolve();
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            AutoResolve();
            Subscribe();
            Refresh(true);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (Time.time < _nextUpdateTime) return;
            Refresh(false);
        }

        private void OnValidate()
        {
            _inlineLookupDirty = true;
            updateInterval = Mathf.Max(0.02f, updateInterval);
            maxSlots = Mathf.Max(0, maxSlots);
        }

        // ─── Auto-Discovery ─────────────────────────────────────────────

        private void AutoResolve()
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
                crowdController = rtsController.crowdController;

            if (crowdController == null)
            {
#if UNITY_2023_1_OR_NEWER
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
#else
                crowdController = FindObjectOfType<EnemyMassesCrowdController>();
#endif
            }
        }

        // ─── Selection Events ────────────────────────────────────────────

        private void Subscribe()
        {
            if (rtsController == null) return;
            rtsController.SelectionChanged += OnSelectionChanged;
            rtsController.SelectionCleared += OnSelectionCleared;
        }

        private void Unsubscribe()
        {
            if (rtsController == null) return;
            rtsController.SelectionChanged -= OnSelectionChanged;
            rtsController.SelectionCleared -= OnSelectionCleared;
        }

        private void OnSelectionChanged(SelectionChangedEventData data)
        {
            // Guard against being called after the object is destroyed
            // (can happen during play-mode exit when teardown order varies).
            if (this == null) return;

            _selectedIndices.Clear();
            if (data?.selectedIndices != null)
                _selectedIndices.AddRange(data.selectedIndices);
            Refresh(true);
        }

        private void OnSelectionCleared()
        {
            if (this == null) return;

            _selectedIndices.Clear();
            Refresh(true);
        }

        // ─── Core Refresh ────────────────────────────────────────────────

        private void Refresh(bool force)
        {
            if (!force && Time.time < _nextUpdateTime) return;
            _nextUpdateTime = Time.time + updateInterval;

            GatherActiveEffects();

            if (debugLog && _activeEffects.Count > 0)
            {
                _sb.Clear();
                _sb.Append("[StatusEffectInfoUI] Gathered ").Append(_activeEffects.Count).Append(" effect(s): ");
                for (int d = 0; d < _activeEffects.Count; d++)
                {
                    if (d > 0) _sb.Append(", ");
                    var fx = _activeEffects[d];
                    _sb.Append(fx.type).Append(" (").Append(fx.category).Append(')');
                    if (fx.isPermanent) _sb.Append(" [permanent]");
                    else _sb.Append(' ').Append(fx.remaining.ToString("0.#")).Append('s');
                    if (fx.stacks > 1) _sb.Append(" x").Append(fx.stacks);
                }
                Debug.Log(_sb.ToString());
            }

            int count = _activeEffects.Count;
            if (maxSlots > 0 && count > maxSlots)
                count = maxSlots;

            int previousSlotCount = _slotObjects.Count;
            EnsureSlotCount(count);
            SetActiveSlotsCount(count);

            for (int i = 0; i < count; i++)
                ApplySlot(i, _activeEffects[i]);

            // Force layout rebuild if the slot count changed so
            // ContentSizeFitter / HorizontalLayoutGroup update correctly.
            if (_slotObjects.Count != previousSlotCount || force)
            {
                var target = slotContainer != null ? slotContainer : (transform as RectTransform);
                if (target != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(target);
            }

            UpdateBarVisibility(count);
            UpdateTooltip();
        }

        // ─── Gather Effects ──────────────────────────────────────────────

        private void GatherActiveEffects()
        {
            _activeEffects.Clear();

            if (crowdController == null) return;
            if (_selectedIndices.Count == 0) return;

            var agents = crowdController.GetAgents();
            if (agents == null) return;

            // Use a set to de-duplicate effects across multiple selected agents.
            // Show the "worst case" (highest stack, shortest remaining) for each type.
            var seen = new Dictionary<StatusEffectType, int>(); // type → index in _activeEffects

            for (int s = 0; s < _selectedIndices.Count; s++)
            {
                int idx = _selectedIndices[s];
                if (idx < 0 || idx >= agents.Count) continue;

                var agent = agents[idx];
                if (agent == null || agent.isDead) continue;
                if (agent.activeEffectCount <= 0) continue;

                var effects = agent.activeStatusEffects;
                if (effects == null) continue;

                for (int e = 0; e < effects.Length; e++)
                {
                    var fx = effects[e];
                    if (!fx.isActive) continue;
                    if (!PassesCategoryFilter(fx.type)) continue;

                    bool isPermanent = fx.expireTime >= PermanentTimeThreshold;
                    float remaining = isPermanent ? -1f : fx.expireTime - Time.time;
                    if (!isPermanent && remaining <= 0f) continue;

                    int stacks = Mathf.Max(1, fx.stacks);

                    if (seen.TryGetValue(fx.type, out int existingIdx))
                    {
                        // Merge: keep highest stacks and shortest remaining
                        var existing = _activeEffects[existingIdx];
                        if (stacks > existing.stacks)
                            existing.stacks = stacks;
                        if (!isPermanent && (existing.isPermanent || remaining < existing.remaining))
                            existing.remaining = remaining;
                        existing.isPermanent = existing.isPermanent && isPermanent;
                        _activeEffects[existingIdx] = existing;
                    }
                    else
                    {
                        seen[fx.type] = _activeEffects.Count;
                        _activeEffects.Add(new EffectSlotData
                        {
                            type = fx.type,
                            category = StatusEffectHelper.GetCategory(fx.type),
                            stacks = stacks,
                            remaining = remaining,
                            isPermanent = isPermanent
                        });
                    }
                }
            }
        }

        private bool PassesCategoryFilter(StatusEffectType type)
        {
            switch (StatusEffectHelper.GetCategory(type))
            {
                case StatusEffectCategory.Buff: return showBuffs;
                case StatusEffectCategory.Debuff: return showDebuffs;
                case StatusEffectCategory.DamageOverTime: return showDamageOverTime;
                case StatusEffectCategory.CrowdControl: return showCrowdControl;
                default: return true;
            }
        }

        // ─── Slot Management ─────────────────────────────────────────────

        private void EnsureSlotCount(int desired)
        {
            while (_slotObjects.Count < desired)
                CreateSlot();
        }

        private void CreateSlot()
        {
            RectTransform parent = slotContainer != null ? slotContainer : (transform as RectTransform);
            if (parent == null) return;

            // Root slot object
            var slotGO = new GameObject($"EffectSlot_{_slotObjects.Count}", typeof(RectTransform));
            slotGO.transform.SetParent(parent, false);
            var slotRect = slotGO.GetComponent<RectTransform>();
            slotRect.sizeDelta = slotSize;

            // LayoutElement so HorizontalLayoutGroup / ContentSizeFitter
            // can compute correct preferred size for this slot.
            var layoutElement = slotGO.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = slotSize.x;
            layoutElement.preferredHeight = slotSize.y;
            layoutElement.minWidth = slotSize.x;
            layoutElement.minHeight = slotSize.y;

            // Icon image fills the slot
            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.preserveAspect = true;

            // Duration text (bottom-center)
            UITextComponent durationText = null;
            if (showDurationText)
            {
                var durGO = new GameObject("Duration", typeof(RectTransform));
                durGO.transform.SetParent(slotGO.transform, false);
                var durRect = durGO.GetComponent<RectTransform>();
                durRect.anchorMin = new Vector2(0f, 0f);
                durRect.anchorMax = new Vector2(1f, 0.35f);
                durRect.offsetMin = Vector2.zero;
                durRect.offsetMax = Vector2.zero;
                durationText = durGO.AddComponent<UITextComponent>();
                durationText.fontSize = 9;
                durationText.color = Color.white;
#if TMP_PRESENT
                durationText.alignment = TMPro.TextAlignmentOptions.Bottom;
                durationText.enableWordWrapping = false;
                durationText.overflowMode = TMPro.TextOverflowModes.Overflow;
#else
                durationText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (durationText.font == null) durationText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                durationText.alignment = TextAnchor.LowerCenter;
#endif
                // Outline for readability
                var outline = durGO.AddComponent<Outline>();
                outline.effectColor = new Color(0, 0, 0, 0.8f);
                outline.effectDistance = new Vector2(1, -1);
            }

            // Stack text (top-right)
            UITextComponent stackText = null;
            if (showStackCount)
            {
                var stackGO = new GameObject("Stacks", typeof(RectTransform));
                stackGO.transform.SetParent(slotGO.transform, false);
                var stackRect = stackGO.GetComponent<RectTransform>();
                stackRect.anchorMin = new Vector2(0.5f, 0.6f);
                stackRect.anchorMax = new Vector2(1f, 1f);
                stackRect.offsetMin = Vector2.zero;
                stackRect.offsetMax = Vector2.zero;
                stackText = stackGO.AddComponent<UITextComponent>();
                stackText.fontSize = 10;
                stackText.color = Color.white;
#if TMP_PRESENT
                stackText.alignment = TMPro.TextAlignmentOptions.TopRight;
                stackText.fontStyle = TMPro.FontStyles.Bold;
                stackText.enableWordWrapping = false;
#else
                stackText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (stackText.font == null) stackText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                stackText.alignment = TextAnchor.UpperRight;
                stackText.fontStyle = FontStyle.Bold;
#endif
                var outline = stackGO.AddComponent<Outline>();
                outline.effectColor = new Color(0, 0, 0, 0.8f);
                outline.effectDistance = new Vector2(1, -1);
            }

            // Transparent raycast target so the slot receives pointer events.
            var slotBg = slotGO.AddComponent<Image>();
            slotBg.color = Color.clear;
            slotBg.raycastTarget = true;

            // EventTrigger for tooltip hover detection.
            if (enableTooltips && tooltipPanel != null)
            {
                int slotIndex = _slotObjects.Count; // capture for closure
                var trigger = slotGO.AddComponent<EventTrigger>();

                var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enterEntry.callback.AddListener((_) => SetHoveredSlot(slotIndex));
                trigger.triggers.Add(enterEntry);

                var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exitEntry.callback.AddListener((_) => ClearHoveredSlot());
                trigger.triggers.Add(exitEntry);
            }

            _slotObjects.Add(slotGO);
            _slotIcons.Add(iconImg);
            _slotDurationTexts.Add(durationText);
            _slotStackTexts.Add(stackText);
        }

        private void SetActiveSlotsCount(int count)
        {
            for (int i = 0; i < _slotObjects.Count; i++)
            {
                if (_slotObjects[i] != null)
                    _slotObjects[i].SetActive(i < count);
            }
        }

        private void ApplySlot(int index, EffectSlotData data)
        {
            // Icon
            if (index < _slotIcons.Count && _slotIcons[index] != null)
            {
                var icon = _slotIcons[index];
                icon.sprite = ResolveIcon(data.type, data.category);
                icon.color = colorizeIcons ? ResolveColor(data.type, data.category) : Color.white;
            }

            // Duration text
            if (index < _slotDurationTexts.Count && _slotDurationTexts[index] != null)
            {
                var txt = _slotDurationTexts[index];
                if (data.isPermanent)
                {
                    txt.text = "";
                    txt.gameObject.SetActive(false);
                }
                else
                {
                    float rem = Mathf.Max(0f, data.remaining);
                    txt.text = rem >= 10f ? Mathf.CeilToInt(rem).ToString() : rem.ToString("0.#");
                    txt.gameObject.SetActive(true);
                }
            }

            // Stack text
            if (index < _slotStackTexts.Count && _slotStackTexts[index] != null)
            {
                var txt = _slotStackTexts[index];
                if (data.stacks > 1)
                {
                    txt.text = data.stacks.ToString();
                    txt.gameObject.SetActive(true);
                }
                else
                {
                    txt.gameObject.SetActive(false);
                }
            }
        }

        // ─── Visibility ──────────────────────────────────────────────────

        private void UpdateBarVisibility(int activeCount)
        {
            bool visible = activeCount > 0 || !autoHideWhenEmpty;

            if (barRoot != null)
                barRoot.gameObject.SetActive(visible);

            if (barBackground != null)
                barBackground.enabled = visible;

            if (_canvasGroup != null)
                _canvasGroup.alpha = visible ? 1f : 0f;
        }

        // ─── Tooltip ─────────────────────────────────────────────────────

        /// <summary>
        /// Called by an EventTrigger or custom hover script on a slot.
        /// Sets the hovered slot index so the tooltip content updates.
        /// </summary>
        public void SetHoveredSlot(int index)
        {
            _hoveredSlotIndex = index;
            UpdateTooltip();
        }

        /// <summary>
        /// Clears the hover state and hides the tooltip.
        /// </summary>
        public void ClearHoveredSlot()
        {
            _hoveredSlotIndex = -1;
            UpdateTooltip();
        }

        private void UpdateTooltip()
        {
            if (tooltipPanel == null || tooltipText == null)
                return;

            if (!enableTooltips || _hoveredSlotIndex < 0 || _hoveredSlotIndex >= _activeEffects.Count)
            {
                tooltipPanel.gameObject.SetActive(false);
                return;
            }

            var data = _activeEffects[_hoveredSlotIndex];
            _sb.Clear();

            string displayName = registry != null
                ? registry.GetDisplayName(data.type)
                : NicifyName(data.type.ToString());

            _sb.Append("<b>").Append(displayName).Append("</b>");

            if (data.stacks > 1)
                _sb.Append("  x").Append(data.stacks);

            _sb.AppendLine();

            // Category
            _sb.Append("<color=#AAAAAA>").Append(data.category.ToString()).Append("</color>");
            _sb.AppendLine();

            // Duration
            if (data.isPermanent)
                _sb.Append("Permanent");
            else
                _sb.Append(Mathf.Max(0f, data.remaining).ToString("0.#")).Append("s remaining");

            // Tooltip from registry
            if (registry != null)
            {
                string tip = registry.GetTooltip(data.type);
                if (!string.IsNullOrEmpty(tip))
                {
                    _sb.AppendLine();
                    _sb.Append(tip);
                }
            }

            tooltipText.text = _sb.ToString();
            tooltipPanel.gameObject.SetActive(true);
        }

        // ─── Icon Resolution ─────────────────────────────────────────────

        private Sprite ResolveIcon(StatusEffectType type, StatusEffectCategory category)
        {
            // Registry takes priority
            if (registry != null)
                return registry.GetIcon(type);

            // Inline overrides
            if (_inlineLookupDirty)
                RebuildInlineLookup();

            if (_inlineLookup.TryGetValue(type, out var sprite) && sprite != null)
                return sprite;

            // Category fallbacks
            switch (category)
            {
                case StatusEffectCategory.Buff: return fallbackBuffIcon;
                case StatusEffectCategory.Debuff: return fallbackDebuffIcon;
                case StatusEffectCategory.DamageOverTime: return fallbackDotIcon;
                case StatusEffectCategory.CrowdControl: return fallbackCCIcon;
                default: return fallbackBuffIcon;
            }
        }

        private Color ResolveColor(StatusEffectType type, StatusEffectCategory category)
        {
            if (registry != null)
                return registry.GetColor(type);

            // Hard-coded fallback colours (same defaults as registry)
            switch (category)
            {
                case StatusEffectCategory.CrowdControl: return new Color(1f, 0.85f, 0.2f, 1f);
                case StatusEffectCategory.DamageOverTime: return new Color(1f, 0.6f, 0.2f, 1f);
                case StatusEffectCategory.Debuff: return new Color(1f, 0.3f, 0.25f, 1f);
                case StatusEffectCategory.Buff:
                default: return new Color(0.2f, 1f, 0.4f, 1f);
            }
        }

        private void RebuildInlineLookup()
        {
            _inlineLookup.Clear();
            if (iconOverrides != null)
            {
                for (int i = 0; i < iconOverrides.Count; i++)
                {
                    var o = iconOverrides[i];
                    if (o?.icon != null)
                        _inlineLookup[o.effectType] = o.icon;
                }
            }
            _inlineLookupDirty = false;
        }

        // ─── Util ────────────────────────────────────────────────────────

        private static string NicifyName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length + 4);
            sb.Append(raw[0]);
            for (int i = 1; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsUpper(c) && !char.IsUpper(raw[i - 1]))
                    sb.Append(' ');
                else if (char.IsDigit(c) && !char.IsDigit(raw[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
