using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime.Localization;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Central ScriptableObject that maps each <see cref="StatusEffectType"/> to an
    /// icon sprite, display name, and optional tint colour. Shared across all status
    /// effect UI components so icons are defined once and reused everywhere.
    /// <para>
    /// Create via <b>Assets → Create → Enemy Masses → Status Effect UI Registry</b>.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "StatusEffectUIRegistry", menuName = "Enemy Masses/Status Effect UI Registry", order = 20)]
    public class StatusEffectUIRegistry : ScriptableObject
    {
        // ─── Per-Effect Entries ──────────────────────────────────────────

        [System.Serializable]
        public class Entry
        {
            [Tooltip("The status effect type this entry represents.")]
            public StatusEffectType effectType;

            [Tooltip("Icon displayed in the status bar for this effect.")]
            public Sprite icon;

            [Tooltip("Display name shown to the player. Leave empty to auto-generate from the enum name.")]
            public string displayName;

            [Tooltip("Optional Unity Localization reference for the display name.")]
            public OptionalLocalizedString displayNameLocalization;

            [Tooltip("Tint colour applied to the icon. White = use the icon's original colours. Override to tint per-effect.")]
            public Color tint = Color.white;

            [Tooltip("Optional short description shown in tooltips.")]
            [TextArea(1, 3)]
            public string tooltip;

            [Tooltip("Optional Unity Localization reference for the tooltip text.")]
            public OptionalLocalizedString tooltipLocalization;
        }

        [Header("Per-Effect Icons & Names")]
        [Tooltip("One entry per status effect that should appear in UI. Effects not listed fall back to category defaults.")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        // ─── Category Fallbacks ──────────────────────────────────────────

        [Header("Category Defaults")]
        [Tooltip("Fallback icon for buff effects without a specific entry.")]
        public Sprite defaultBuffIcon;

        [Tooltip("Fallback icon for debuff effects without a specific entry.")]
        public Sprite defaultDebuffIcon;

        [Tooltip("Fallback icon for damage-over-time effects without a specific entry.")]
        public Sprite defaultDotIcon;

        [Tooltip("Fallback icon for crowd control effects without a specific entry.")]
        public Sprite defaultCrowdControlIcon;

        [Header("Category Colours")]
        public Color buffColor = new Color(0.2f, 1f, 0.4f, 1f);
        public Color debuffColor = new Color(1f, 0.3f, 0.25f, 1f);
        public Color dotColor = new Color(1f, 0.6f, 0.2f, 1f);
        public Color crowdControlColor = new Color(1f, 0.85f, 0.2f, 1f);

        // ─── Runtime Lookup ──────────────────────────────────────────────

        private Dictionary<StatusEffectType, Entry> _lookup;

        /// <summary>All entries configured on this registry.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Resolves the icon for a given effect type.
        /// Returns the per-effect icon first, then the category default, then null.
        /// </summary>
        public Sprite GetIcon(StatusEffectType type)
        {
            EnsureLookup();
            if (_lookup.TryGetValue(type, out var entry) && entry.icon != null)
                return entry.icon;

            return GetCategoryDefaultIcon(StatusEffectHelper.GetCategory(type));
        }

        /// <summary>
        /// Returns the display name for a given effect type.
        /// Falls back to a nicified version of the enum name.
        /// </summary>
        public string GetDisplayName(StatusEffectType type)
        {
            EnsureLookup();
            if (_lookup.TryGetValue(type, out var entry))
            {
                string source = !string.IsNullOrEmpty(entry.displayName)
                    ? entry.displayName
                    : NicifyName(type.ToString());
                return entry.displayNameLocalization.Resolve(source);
            }

            return NicifyName(type.ToString());
        }

        /// <summary>
        /// Returns the tooltip text for a given effect type, or empty string if none.
        /// </summary>
        public string GetTooltip(StatusEffectType type)
        {
            EnsureLookup();
            if (_lookup.TryGetValue(type, out var entry))
            {
                string source = !string.IsNullOrEmpty(entry.tooltip)
                    ? entry.tooltip
                    : string.Empty;
                return entry.tooltipLocalization.Resolve(source);
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns the tint colour for a given effect type.
        /// Uses the per-entry tint if set, otherwise the category colour.
        /// </summary>
        public Color GetColor(StatusEffectType type)
        {
            EnsureLookup();
            if (_lookup.TryGetValue(type, out var entry))
                return entry.tint;

            return GetCategoryColor(StatusEffectHelper.GetCategory(type));
        }

        /// <summary>
        /// Returns the category colour for the given category.
        /// </summary>
        public Color GetCategoryColor(StatusEffectCategory category)
        {
            switch (category)
            {
                case StatusEffectCategory.CrowdControl: return crowdControlColor;
                case StatusEffectCategory.DamageOverTime: return dotColor;
                case StatusEffectCategory.Debuff: return debuffColor;
                case StatusEffectCategory.Buff:
                default: return buffColor;
            }
        }

        /// <summary>
        /// Returns the default icon for a given category.
        /// </summary>
        public Sprite GetCategoryDefaultIcon(StatusEffectCategory category)
        {
            switch (category)
            {
                case StatusEffectCategory.CrowdControl: return defaultCrowdControlIcon;
                case StatusEffectCategory.DamageOverTime: return defaultDotIcon;
                case StatusEffectCategory.Debuff: return defaultDebuffIcon;
                case StatusEffectCategory.Buff:
                default: return defaultBuffIcon;
            }
        }

        /// <summary>
        /// Returns true if the registry has a specific entry for the given effect type.
        /// </summary>
        public bool HasEntry(StatusEffectType type)
        {
            EnsureLookup();
            return _lookup.ContainsKey(type);
        }

        // ─── Internals ──────────────────────────────────────────────────

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<StatusEffectType, Entry>();
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                _lookup[e.effectType] = e;
            }
        }

        private void OnValidate()
        {
            // Invalidate runtime cache when edited in the Inspector
            _lookup = null;
        }

        private static string NicifyName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder(raw.Length + 4);
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
