using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Arawn.EnemyMasses.Runtime.Skills;

#if TMP_PRESENT
using UITextComponent = TMPro.TextMeshProUGUI;
#else
using UITextComponent = UnityEngine.UI.Text;
#endif

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// UI element representing a single skill slot in the AgentSkillBarUI.
    /// Displays the skill icon, keybind label, cooldown overlay, and tooltip on hover.
    /// Created and managed by AgentSkillBarUI at runtime.
    /// </summary>
    public class AgentSkillSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("UI References")]
        [Tooltip("Image displaying the skill icon.")]
        public Image skillIcon;

        [Tooltip("Text displaying the input key label (e.g. 'Q', '1').")]
        public UITextComponent keybindLabel;

        [Tooltip("Overlay image used for cooldown sweep fill effect.")]
        public Image cooldownOverlay;

        [Tooltip("Text displaying the remaining cooldown time.")]
        public UITextComponent cooldownText;

        [Tooltip("Border/highlight image for hover or active state.")]
        public Image borderImage;

        [Tooltip("Overlay image shown when auto-cast is active (optional).\n" +
                 "If assigned, displayed on top of the skill icon with the auto-cast tint.")]
        public Image autocastOverlayImage;

        [Header("Colors")]
        [Tooltip("Normal border color.")]
        public Color normalBorderColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        [Tooltip("Hover border color.")]
        public Color hoverBorderColor = new Color(0.8f, 0.7f, 0.2f, 1f);

        [Tooltip("Border color when auto-cast is enabled (right-click toggle).\n" +
                 "Overridden at runtime by the parent AgentSkillBarUI's autocastBorderColor.")]
        public Color autocastBorderColor = new Color(1f, 0.85f, 0.1f, 1f);

        [Tooltip("On-cooldown icon tint.")]
        public Color cooldownIconTint = new Color(0.4f, 0.4f, 0.4f, 1f);

        [Tooltip("Ready icon tint (full color).")]
        public Color readyIconTint = Color.white;

        /// <summary>
        /// The skill currently displayed in this slot.
        /// </summary>
        public AgentSkill Skill { get; private set; }

        /// <summary>
        /// The skill slot index within the EnemyCrowd's skill system config.
        /// </summary>
        public int SlotIndex { get; private set; } = -1;

        /// <summary>
        /// Reference to the parent bar for callbacks.
        /// </summary>
        public AgentSkillBarUI ParentBar { get; set; }

        private bool _isOnCooldown;
        private float _cooldownTotal;
        private float _cooldownRemaining;
        private bool _isTargetingHighlighted;
        private Color _targetingHighlightColor;
        private bool _isAutocastActive;
        private Sprite _autocastOverlaySprite;
        private Color _autocastOverlayTint = new Color(1f, 0.85f, 0.1f, 0.35f);

        private void Awake()
        {
            ResolveReferencesIfMissing();
        }

        private void OnEnable()
        {
            ResolveReferencesIfMissing();
        }

        private void ResolveReferencesIfMissing()
        {
            if (skillIcon == null)
                skillIcon = transform.Find("Icon")?.GetComponent<Image>();

            if (keybindLabel == null)
                keybindLabel = transform.Find("KeybindLabel")?.GetComponent<UITextComponent>();

            if (cooldownOverlay == null)
                cooldownOverlay = transform.Find("CooldownOverlay")?.GetComponent<Image>();

            if (cooldownText == null)
                cooldownText = transform.Find("CooldownText")?.GetComponent<UITextComponent>();

            if (borderImage == null)
                borderImage = transform.Find("Border")?.GetComponent<Image>();

            if (autocastOverlayImage == null)
                autocastOverlayImage = transform.Find("AutocastOverlay")?.GetComponent<Image>();
        }

        /// <summary>
        /// Configures this slot with the given skill data.
        /// </summary>
        public void Setup(AgentSkill skill, int slotIndex, string keybindText)
        {
            ResolveReferencesIfMissing();

            Skill = skill;
            SlotIndex = slotIndex;

            if (skillIcon != null)
            {
                if (skill.Icon != null)
                {
                    skillIcon.sprite = skill.Icon;
                    skillIcon.color = readyIconTint;
                    skillIcon.enabled = true;
                }
                else
                {
                    skillIcon.sprite = null;
                    skillIcon.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                }
            }

            if (keybindLabel != null)
            {
                keybindLabel.text = keybindText ?? "";
            }

            if (cooldownOverlay != null)
            {
                cooldownOverlay.type = Image.Type.Filled;
                cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
                cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
                cooldownOverlay.fillClockwise = false;
                cooldownOverlay.fillAmount = 0f;
                cooldownOverlay.gameObject.SetActive(false);
            }

            if (cooldownText != null)
            {
                cooldownText.text = "";
                cooldownText.gameObject.SetActive(false);
            }

            if (borderImage != null)
            {
                borderImage.color = normalBorderColor;
            }

            _isOnCooldown = false;
            _isAutocastActive = false;
        }

        /// <summary>
        /// Updates cooldown display. Called each frame by the parent bar.
        /// </summary>
        public void UpdateCooldown(float remaining, float total)
        {
            ResolveReferencesIfMissing();

            _cooldownRemaining = remaining;
            _cooldownTotal = total;

            bool onCooldown = remaining > 0f && total > 0f;

            if (onCooldown != _isOnCooldown)
            {
                _isOnCooldown = onCooldown;

                if (cooldownOverlay != null)
                    cooldownOverlay.gameObject.SetActive(onCooldown);

                if (cooldownText != null)
                    cooldownText.gameObject.SetActive(onCooldown);

                if (skillIcon != null)
                    skillIcon.color = onCooldown ? cooldownIconTint : readyIconTint;
            }

            if (onCooldown)
            {
                if (cooldownOverlay != null)
                    cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / total);

                if (cooldownText != null)
                {
                    bool useMinutes = ParentBar != null && ParentBar.showCooldownAsMinutes && remaining >= 60f;
                    if (useMinutes)
                    {
                        int minutes = Mathf.CeilToInt(remaining / 60f);
                        string abbr = ParentBar.GetMinuteAbbreviationString();
                        cooldownText.text = $"{minutes} {abbr}";
                    }
                    else
                    {
                        cooldownText.text = remaining >= 1f
                            ? Mathf.CeilToInt(remaining).ToString()
                            : remaining.ToString("F1");
                    }
                }
            }
            else
            {
                if (cooldownOverlay != null)
                    cooldownOverlay.fillAmount = 0f;

                if (cooldownText != null)
                    cooldownText.text = "";
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (borderImage != null && !_isTargetingHighlighted && !_isAutocastActive)
                borderImage.color = hoverBorderColor;

            if (ParentBar != null && Skill != null)
                ParentBar.ShowTooltip(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (borderImage != null && !_isTargetingHighlighted)
                borderImage.color = _isAutocastActive ? autocastBorderColor : normalBorderColor;

            if (ParentBar != null)
                ParentBar.HideTooltip();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (ParentBar == null || Skill == null) return;
            
            if (ParentBar.IsAutoCastToggleButton(eventData))
            {
                // Auto-cast toggle (configurable input action)
                ParentBar.OnSlotRightClicked(this);
            }
            else
            {
                // Left-click / other: activate skill (existing behavior)
                ParentBar.OnSlotClicked(this);
            }
        }

        /// <summary>
        /// Sets or clears the targeting highlight on this slot.
        /// When active, the border shows a distinct color indicating this skill is pending target selection.
        /// </summary>
        public void SetTargetingHighlight(bool active, Color highlightColor)
        {
            _isTargetingHighlighted = active;
            _targetingHighlightColor = highlightColor;

            if (borderImage != null)
            {
                borderImage.color = active ? highlightColor : (_isAutocastActive ? autocastBorderColor : normalBorderColor);
            }
        }
        
        /// <summary>
        /// Sets or clears the auto-cast visual state.
        /// When active, the border uses the autocastBorderColor and shows the optional overlay sprite.
        /// </summary>
        public void SetAutocastVisual(bool active)
        {
            _isAutocastActive = active;
            
            if (borderImage != null && !_isTargetingHighlighted)
            {
                borderImage.color = active ? autocastBorderColor : normalBorderColor;
            }

            UpdateAutocastOverlay(active);
        }

        /// <summary>
        /// Applies keybind label styling pushed from the parent AgentSkillBarUI.
        /// Call before Setup() so the style is applied when the text is set.
        /// </summary>
#if TMP_PRESENT
        public void ApplyKeybindLabelStyle(Color color, int fontSize, TMPro.TMP_FontAsset fontAsset)
#else
        public void ApplyKeybindLabelStyle(Color color, int fontSize, Font font)
#endif
        {
            ResolveReferencesIfMissing();

            if (keybindLabel != null)
            {
                keybindLabel.color = color;
                keybindLabel.fontSize = fontSize;
#if TMP_PRESENT
                if (fontAsset != null)
                    keybindLabel.font = fontAsset;
#else
                if (font != null)
                    keybindLabel.font = font;
#endif
            }
        }

        /// <summary>
        /// Configures the overlay sprite and tint pushed from the parent AgentSkillBarUI.
        /// Call before SetAutocastVisual() or Setup() so the overlay is ready.
        /// </summary>
        public void ApplyAutocastOverlaySettings(Sprite overlaySprite, Color overlayTint)
        {
            _autocastOverlaySprite = overlaySprite;
            _autocastOverlayTint = overlayTint;

            // Ensure the overlay image exists if a sprite is assigned
            if (overlaySprite != null)
            {
                EnsureAutocastOverlayImage();
                if (autocastOverlayImage != null)
                {
                    autocastOverlayImage.sprite = overlaySprite;
                    autocastOverlayImage.color = overlayTint;
                }
            }

            // Sync current state
            UpdateAutocastOverlay(_isAutocastActive);
        }

        private void UpdateAutocastOverlay(bool active)
        {
            if (autocastOverlayImage != null)
            {
                autocastOverlayImage.gameObject.SetActive(active && _autocastOverlaySprite != null);
                if (active && _autocastOverlaySprite != null)
                {
                    autocastOverlayImage.sprite = _autocastOverlaySprite;
                    autocastOverlayImage.color = _autocastOverlayTint;
                }
            }
        }

        /// <summary>
        /// Creates the AutocastOverlay child Image on demand if it doesn't exist.
        /// </summary>
        private void EnsureAutocastOverlayImage()
        {
            if (autocastOverlayImage != null) return;

            // Try to find existing
            var existing = transform.Find("AutocastOverlay");
            if (existing != null)
            {
                autocastOverlayImage = existing.GetComponent<Image>();
                if (autocastOverlayImage != null) return;
            }

            // Create programmatically — placed above the icon but below cooldown overlay
            var overlayGO = new GameObject("AutocastOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(transform, false);
            var rect = overlayGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(2, 2);
            rect.offsetMax = new Vector2(-2, -2);

            autocastOverlayImage = overlayGO.GetComponent<Image>();
            autocastOverlayImage.raycastTarget = false;
            autocastOverlayImage.color = _autocastOverlayTint;
            overlayGO.SetActive(false);

            // Move it behind the cooldown overlay (sibling index after icon, before cooldown)
            var cooldownIdx = cooldownOverlay != null ? cooldownOverlay.transform.GetSiblingIndex() : -1;
            if (cooldownIdx > 0)
                overlayGO.transform.SetSiblingIndex(cooldownIdx);
        }

        /// <summary>
        /// Whether auto-cast is currently shown as active on this slot.
        /// </summary>
        public bool IsAutocastActive => _isAutocastActive;
    }
}
