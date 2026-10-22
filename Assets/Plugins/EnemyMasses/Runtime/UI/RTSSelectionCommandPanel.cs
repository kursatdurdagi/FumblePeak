using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Localization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if TMP_PRESENT
using UITextComponent = TMPro.TextMeshProUGUI;
#else
using UITextComponent = UnityEngine.UI.Text;
#endif

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// Toggles a command UI panel based on RTS selection of player-controlled agents
    /// and forwards button presses to strict combat stance/stop commands.
    /// </summary>
    public class RTSSelectionCommandPanel : MonoBehaviour
    {
        // ── References ────────────────────────────────────────────────

        [Tooltip("RTS controller to subscribe to for selection and to send commands through.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("Root GameObject that contains the toggle and buttons.")]
        public GameObject panelRoot;

        // ── UI ────────────────────────────────────────────────────────────

        [Tooltip("Toggle strict stance mode. ON = strict Offensive/Defensive/Hold (won't help allies). OFF = non-strict versions (can help allies).")]
        public Toggle strictModeToggle;

        [Tooltip("Button that sets Offensive Strict on the current selection.")]
        public Button offensiveStrictButton;

        [Tooltip("Button that sets Defensive Strict on the current selection.")]
        public Button defensiveStrictButton;

        [Tooltip("Button that sets Hold Position Strict on the current selection.")]
        public Button holdStrictButton;

        [Tooltip("Button that sets Passive on the current selection. Agents won't attack unless explicitly commanded.")]
        public Button passiveButton;

        [Tooltip("Button that issues Stop for the current selection.")]
        public Button stopButton;

        [Tooltip("Button that enters Follow targeting mode. Click a valid target to have selected agents follow it.")]
        public Button followButton;

        // ── Visuals ───────────────────────────────────────────────────────

        [Tooltip("Normal tint color for command buttons (sci-fi panel theme).")]
        public Color buttonNormalColor = new Color(0.11f, 0.14f, 0.20f, 1f);

        [Tooltip("Hover tint color for command buttons.")]
        public Color buttonHoverColor = new Color(0.17f, 0.22f, 0.31f, 1f);

        [Tooltip("Pressed tint color for command buttons.")]
        public Color buttonPressedColor = new Color(0.23f, 0.66f, 1f, 1f);

        [Tooltip("Selected tint color to show the last issued command.")]
        public Color buttonSelectedColor = new Color(0.44f, 0.95f, 1f, 1f);

        [Tooltip("Disabled tint color for command buttons.")]
        public Color buttonDisabledColor = new Color(0.06f, 0.07f, 0.11f, 1f);

        // ── Audio ─────────────────────────────────────────────────────────

        [Tooltip("Audio clip to play when Offensive is clicked.")]
        public AudioClip offensiveClickSfx;

        [Tooltip("Audio clip to play when Defensive is clicked.")]
        public AudioClip defensiveClickSfx;

        [Tooltip("Audio clip to play when Hold Position is clicked.")]
        public AudioClip holdClickSfx;

        [Tooltip("Audio clip to play when Passive is clicked.")]
        public AudioClip passiveClickSfx;

        [Tooltip("Audio clip to play when Stop is clicked.")]
        public AudioClip stopClickSfx;

        [Tooltip("Audio clip to play when Follow target is confirmed.")]
        public AudioClip followClickSfx;

        [Tooltip("Audio clip to play when Follow targeting mode is entered.")]
        public AudioClip followEnterSfx;

        [Tooltip("Optional audio source used to play command click SFX. If null, a one-shot will be used.")]
        public AudioSource commandAudioSource;

        [Tooltip("Random pitch variation applied to command click SFX (± value around 1.0). Set to 0 to disable.")]
        [Range(0f, 0.5f)] public float commandSfxPitchVariation = 0.1f;

        [Tooltip("Audio clip to play when Strict Mode is enabled.")]
        public AudioClip strictOnSfx;

        [Tooltip("Audio clip to play when Strict Mode is disabled.")]
        public AudioClip strictOffSfx;

        // ── Behavior ──────────────────────────────────────────────────────

        [Tooltip("Hide the panel when no player-controlled agents are selected.")]
        public bool autoHideWhenNoPlayerSelection = true;

        [Tooltip("If enabled, Stop will also reset selected agents to default Offensive stance after canceling their current action.")]
        public bool stopResetsToOffensive = true;

        // ── Input Shortcuts ───────────────────────────────────────────────

        [Tooltip("Optional keyboard shortcut to toggle Strict Mode.")]
        public CommandInputBinding strictModeInput = new CommandInputBinding("Strict Toggle");

        [Tooltip("Optional keyboard shortcut for Offensive stance.")]
        public CommandInputBinding offensiveInput = new CommandInputBinding("Offensive");

        [Tooltip("Optional keyboard shortcut for Defensive stance.")]
        public CommandInputBinding defensiveInput = new CommandInputBinding("Defensive");

        [Tooltip("Optional keyboard shortcut for Hold Position stance.")]
        public CommandInputBinding holdInput = new CommandInputBinding("Hold");

        [Tooltip("Optional keyboard shortcut for Passive stance.")]
        public CommandInputBinding passiveInput = new CommandInputBinding("Passive");

        [Tooltip("Optional keyboard shortcut for Stop.")]
        public CommandInputBinding stopInput = new CommandInputBinding("Stop");

        [Tooltip("Optional keyboard shortcut for Follow.")]
        public CommandInputBinding followInput = new CommandInputBinding("Follow");

        // ── Follow Cursor ─────────────────────────────────────────────────

        [Header("Follow Cursor")]
        [Tooltip("Cursor texture shown while in Follow targeting mode. If null, cursor won't change.")]
        public Texture2D followCursorTexture;

        [Tooltip("Hotspot (pixel offset from top-left) for the follow cursor.")]
        public Vector2 followCursorHotspot = new Vector2(16, 16);

        // ── Keybind Label Appearance ──────────────────────────────────────

        [Tooltip("Show keybind label overlays on the command buttons.")]
        public bool showKeybindLabels = true;

        [Tooltip("Color for the keybind label text on each command button.\n" +
                 "Defaults to a warm gold that looks good on dark panel backgrounds.")]
        public Color keybindLabelColor = new Color(0.9f, 0.85f, 0.6f, 1f);

        [Tooltip("Font size for the keybind label text.")]
        public int keybindLabelFontSize = 11;

#if TMP_PRESENT
        [Tooltip("TMP font asset for the keybind label.\n" +
                 "If null, the default TMP font is used.")]
        public TMPro.TMP_FontAsset keybindLabelFontTMP;
#else
        [Tooltip("Legacy font for the keybind label.\n" +
                 "If null, the built-in font is used.")]
        public Font keybindLabelFont;
#endif

        // ── Tooltip Settings ──────────────────────────────────────────────

        [Header("Tooltip")]
        [Tooltip("Enable hover tooltips on command buttons.")]
        public bool enableTooltips = true;

        [Tooltip("RectTransform of the tooltip panel GameObject. Hidden when no button is hovered.")]
        public RectTransform tooltipPanel;

        [Tooltip("Text component inside the tooltip panel used to display the tooltip text.")]
        public UITextComponent tooltipText;

        [Tooltip("Vertical offset (pixels) above the hovered button to position the tooltip.")]
        public float tooltipOffsetY = 8f;

        [Tooltip("Tooltip for the Strict Mode toggle.")]
        public CommandTooltipEntry strictModeTooltip = new CommandTooltipEntry("Strict Mode");

        [Tooltip("Tooltip for the Offensive button.")]
        public CommandTooltipEntry offensiveTooltip = new CommandTooltipEntry("Offensive");

        [Tooltip("Tooltip for the Defensive button.")]
        public CommandTooltipEntry defensiveTooltip = new CommandTooltipEntry("Defensive");

        [Tooltip("Tooltip for the Hold Position button.")]
        public CommandTooltipEntry holdTooltip = new CommandTooltipEntry("Hold Position");

        [Tooltip("Tooltip for the Passive button.")]
        public CommandTooltipEntry passiveTooltip = new CommandTooltipEntry("Passive");

        [Tooltip("Tooltip for the Stop button.")]
        public CommandTooltipEntry stopTooltip = new CommandTooltipEntry("Stop");

        [Tooltip("Tooltip for the Follow button.")]
        public CommandTooltipEntry followTooltip = new CommandTooltipEntry("Follow");

        private bool _hasPlayerSelection;
        private bool _hasOffensive;
        private bool _hasDefensive;
        private bool _hasHold;
        private bool _hasPassive;

        // ── Follow Mode State ─────────────────────────────────────────────
        private bool _followModeActive;
        private bool _followCursorOverrideActive;
        private Texture2D _followRuntimeCursorTexture;
        private bool _followSuppressUntilRelease;
        private bool _followSuppressClearNextFrame;

        private void OnEnable()
        {
            WireButtons();
            Subscribe();
            EnableInputActions();
            ApplyKeybindLabels();
            WireTooltipTriggers();
            SetPanelVisible(false);
            HideTooltipPanel();
        }

        private void OnDisable()
        {
            if (_followModeActive)
                CancelFollowMode();
            Unsubscribe();
            DisableInputActions();
        }

        private void Subscribe()
        {
            if (rtsController != null)
            {
                rtsController.SelectionChanged += HandleSelectionChanged;
            }

            if (strictModeToggle != null)
            {
                strictModeToggle.onValueChanged.AddListener(HandleStrictToggleChanged);
            }
        }

        private void Unsubscribe()
        {
            if (rtsController != null)
            {
                rtsController.SelectionChanged -= HandleSelectionChanged;
            }

            if (strictModeToggle != null)
            {
                strictModeToggle.onValueChanged.RemoveListener(HandleStrictToggleChanged);
            }
        }

        private void WireButtons()
        {
            if (offensiveStrictButton != null)
            {
                offensiveStrictButton.onClick.AddListener(HandleOffensiveClicked);
                ApplyButtonColors(offensiveStrictButton);
            }

            if (defensiveStrictButton != null)
            {
                defensiveStrictButton.onClick.AddListener(HandleDefensiveClicked);
                ApplyButtonColors(defensiveStrictButton);
            }

            if (holdStrictButton != null)
            {
                holdStrictButton.onClick.AddListener(HandleHoldClicked);
                ApplyButtonColors(holdStrictButton);
            }

            if (passiveButton != null)
            {
                passiveButton.onClick.AddListener(HandlePassiveClicked);
                ApplyButtonColors(passiveButton);
            }

            if (stopButton != null)
            {
                stopButton.onClick.AddListener(HandleStopClicked);
                ApplyButtonColors(stopButton);
            }

            if (followButton != null)
            {
                followButton.onClick.AddListener(HandleFollowClicked);
                ApplyButtonColors(followButton);
            }
        }

        private void HandleSelectionChanged(SelectionChangedEventData data)
        {
            bool hasPlayerSelection = false;

            if (rtsController != null && data?.selectedIndices != null && data.selectedIndices.Count > 0)
            {
                hasPlayerSelection = data.selectedIndices.Any(rtsController.IsAgentPlayerControlled);
            }

            _hasPlayerSelection = hasPlayerSelection;

            if (autoHideWhenNoPlayerSelection && panelRoot != null)
            {
                SetPanelVisible(hasPlayerSelection);
            }

            RefreshButtonInteractable(hasPlayerSelection);

            RecomputeSelectionStances();
        }

        private void RefreshButtonInteractable(bool hasSelection = true)
        {
            bool canInteract = hasSelection;

            if (offensiveStrictButton != null)
                offensiveStrictButton.interactable = canInteract;
            if (defensiveStrictButton != null)
                defensiveStrictButton.interactable = canInteract;
            if (holdStrictButton != null)
                holdStrictButton.interactable = canInteract;
            if (passiveButton != null)
                passiveButton.interactable = canInteract;

            if (stopButton != null)
                stopButton.interactable = hasSelection;

            if (followButton != null)
                followButton.interactable = hasSelection;
        }

        private bool IsStrictMode()
        {
            return strictModeToggle != null && strictModeToggle.isOn;
        }

        private void Update()
        {
            // Process follow-mode suppress release (even if no player selection,
            // to avoid getting stuck in suppressed state)
            ProcessFollowSuppressRelease();

            if (!_hasPlayerSelection)
            {
                // If follow mode is active but selection was lost, cancel it
                if (_followModeActive)
                    CancelFollowMode();
                return;
            }

            // Process follow targeting clicks
            if (_followModeActive)
                ProcessFollowTargetingMode();

            ProcessInputShortcuts();
            RefreshActiveButtonVisuals();
        }

        private void RefreshActiveButtonVisuals()
        {
            SetButtonActiveVisual(offensiveStrictButton, _hasOffensive);
            SetButtonActiveVisual(defensiveStrictButton, _hasDefensive);
            SetButtonActiveVisual(holdStrictButton, _hasHold);
            SetButtonActiveVisual(passiveButton, _hasPassive);
        }

        private void RecomputeSelectionStances()
        {
            if (rtsController == null || rtsController.crowdController == null || rtsController.SelectedAgents == null)
            {
                _hasOffensive = _hasDefensive = _hasHold = _hasPassive = false;
                RefreshActiveButtonVisuals();
                return;
            }

            var agents = rtsController.crowdController.GetAgents();
            bool hasOff = false, hasDef = false, hasHold = false, hasPassive = false;

            foreach (int index in rtsController.SelectedAgents)
            {
                if (index < 0 || index >= agents.Count)
                    continue;

                if (!rtsController.IsAgentPlayerControlled(index))
                    continue;

                var agent = agents[index];
                if (agent == null || agent.isDead)
                    continue;

                switch (agent.combatStance)
                {
                    case CombatStance.Offensive:
                        hasOff = true; break;
                    case CombatStance.Defensive:
                        hasDef = true; break;
                    case CombatStance.HoldPosition:
                        hasHold = true; break;
                    case CombatStance.Passive:
                        hasPassive = true; break;
                }
            }

            _hasOffensive = hasOff;
            _hasDefensive = hasDef;
            _hasHold = hasHold;
            _hasPassive = hasPassive;

            RefreshActiveButtonVisuals();
        }

        private void PlayClickSfx(AudioClip clip)
        {
            if (clip == null)
                return;

            float pitch = 1f + UnityEngine.Random.Range(-commandSfxPitchVariation, commandSfxPitchVariation);

            if (commandAudioSource != null)
            {
                commandAudioSource.pitch = pitch;
                commandAudioSource.PlayOneShot(clip);
            }
            else
            {
                // Create a lightweight temp audio source to apply pitch variation.
                var pos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var go = new GameObject("RTSCommandSfxTemp");
                go.transform.position = pos;
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.pitch = pitch;
                src.PlayOneShot(clip);
                Destroy(go, clip.length / Mathf.Max(0.01f, pitch));
            }
        }

        private void HandleStopClicked()
        {
            rtsController?.StopSelectedAgents();
            if (stopResetsToOffensive)
            {
                // After cancel/stop, optionally return selected agents to default Offensive stance (non-strict)
                // so they resume helping allies and re-engaging nearby threats.
                rtsController?.SetSelectedAgentsOffensive();
            }
            PlayClickSfx(stopClickSfx);
            // Stop is transient; recompute to reflect resulting (default Offensive) stances.
            RecomputeSelectionStances();
        }

        private void HandleOffensiveClicked()
        {
            if (rtsController == null) return;
            
            // Check if any selected agents are in combat or broke from formation before changing stance
            bool shouldReform = ShouldTriggerPostCombatReformation();
            
            if (IsStrictMode())
                rtsController.SetSelectedAgentsOffensiveStrict();
            else
                rtsController.SetSelectedAgentsOffensive();
            
            // If agents were in combat, enable post-combat reformation so they regroup after the fight
            if (shouldReform)
            {
                rtsController.EnablePostCombatReformation();
            }
            
            PlayClickSfx(offensiveClickSfx);
            RecomputeSelectionStances();
        }

        private void HandleDefensiveClicked()
        {
            if (rtsController == null) return;
            
            // Check if any selected agents are in combat or broke from formation before changing stance
            bool shouldReform = ShouldTriggerPostCombatReformation();
            
            if (IsStrictMode())
                rtsController.SetSelectedAgentsDefensiveStrict();
            else
                rtsController.SetSelectedAgentsDefensive();
            
            // If agents were in combat, enable post-combat reformation so they regroup after the fight
            if (shouldReform)
            {
                rtsController.EnablePostCombatReformation();
            }
            
            PlayClickSfx(defensiveClickSfx);
            RecomputeSelectionStances();
        }

        private void HandleHoldClicked()
        {
            if (rtsController == null) return;
            if (IsStrictMode())
                rtsController.SetSelectedAgentsHoldPositionStrict();
            else
                rtsController.SetSelectedAgentsHoldPosition();
            PlayClickSfx(holdClickSfx);
            RecomputeSelectionStances();
        }

        private void HandlePassiveClicked()
        {
            if (rtsController == null) return;
            rtsController.SetSelectedAgentsPassive();
            PlayClickSfx(passiveClickSfx);
            RecomputeSelectionStances();
        }
        
        /// <summary>
        /// Checks if any selected agents are in combat or broke from RTS formation,
        /// indicating they should reform after combat ends.
        /// </summary>
        private bool ShouldTriggerPostCombatReformation()
        {
            if (rtsController == null || rtsController.crowdController == null || rtsController.SelectedAgents == null)
                return false;
            
            var agents = rtsController.crowdController.GetAgents();
            int checkedCount = 0;
            
            foreach (int index in rtsController.SelectedAgents)
            {
                if (index < 0 || index >= agents.Count)
                    continue;
                
                if (!rtsController.IsAgentPlayerControlled(index))
                    continue;
                
                var agent = agents[index];
                if (agent == null || agent.isDead)
                    continue;
                
                checkedCount++;
                
                // Check if agent is in combat or broke from formation
                if (agent.isAggroed || agent.isChasing || 
                    agent.attackTargetAgentIndex >= 0 || agent.attackTargetTransform != null ||
                    agent.navigationState == AgentNavigationState.ChasingTarget ||
                    agent.brokeFromRTSFormation)
                {
                    return true;
                }
            }
            
            return false;
        }

        private void HandleStrictToggleChanged(bool isOn)
        {
            var clip = isOn ? strictOnSfx : strictOffSfx;
            PlayClickSfx(clip);
            
            // Reapply current stance with the new strict mode setting to all selected agents.
            // This ensures agents in combat get their ignoreAllyDefenseWhileHolding updated immediately,
            // and triggers post-combat reformation when switching from strict to non-strict mode.
            ReapplyCurrentStanceWithStrictMode(isOn);
        }
        
        /// <summary>
        /// Reapplies the current combat stance to all selected agents with the specified strict mode setting.
        /// When switching from strict to non-strict during combat, also enables post-combat reformation tracking.
        /// </summary>
        private void ReapplyCurrentStanceWithStrictMode(bool strict)
        {
            if (rtsController == null || rtsController.crowdController == null || rtsController.SelectedAgents == null)
                return;
            
            var agents = rtsController.crowdController.GetAgents();
            bool anyInCombat = false;
            bool anyBrokeFromFormation = false;
            int processedCount = 0;
            
            foreach (int index in rtsController.SelectedAgents)
            {
                if (index < 0 || index >= agents.Count)
                    continue;
                
                if (!rtsController.IsAgentPlayerControlled(index))
                    continue;
                
                var agent = agents[index];
                if (agent == null || agent.isDead)
                    continue;
                
                processedCount++;
                
                // Update the strict mode flag for this agent
                agent.ignoreAllyDefenseWhileHolding = strict;
                
                // Track if any agents are currently in combat
                if (agent.isAggroed || agent.isChasing || 
                    agent.attackTargetAgentIndex >= 0 || agent.attackTargetTransform != null ||
                    agent.navigationState == AgentNavigationState.ChasingTarget)
                {
                    anyInCombat = true;
                }
                
                // Track if any agents broke from RTS formation
                if (agent.brokeFromRTSFormation)
                {
                    anyBrokeFromFormation = true;
                    
                    // If switching to non-strict mode, clear the broke-from-formation flag
                    // so agents will reform after combat ends
                    if (!strict)
                    {
                        agent.brokeFromRTSFormation = false;
                    }
                }
            }
            
            // If switching to non-strict mode and agents are in combat or broke from formation,
            // enable post-combat reformation tracking so they reform after the fight
            if (!strict && (anyInCombat || anyBrokeFromFormation))
            {
                rtsController.EnablePostCombatReformation();
            }
            
            RecomputeSelectionStances();
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelRoot == null)
                return;

            // If the panel root is the same GameObject as this component, don't disable it or we lose subscriptions; just toggle children.
            if (panelRoot == gameObject)
            {
                for (int i = 0; i < panelRoot.transform.childCount; i++)
                {
                    var child = panelRoot.transform.GetChild(i);
                    child.gameObject.SetActive(visible);
                }
                return;
            }

            panelRoot.SetActive(visible);
        }

        private void ApplyButtonColors(Button button)
        {
            if (button == null)
                return;

            var colors = button.colors;
            colors.normalColor = buttonNormalColor;
            colors.highlightedColor = buttonHoverColor;
            colors.pressedColor = buttonPressedColor;
            colors.selectedColor = buttonSelectedColor;
            colors.disabledColor = buttonDisabledColor;
            colors.fadeDuration = Mathf.Max(0.05f, colors.fadeDuration);
            button.colors = colors;
        }

        private void SetButtonActiveVisual(Button button, bool active)
        {
            if (button == null || button.targetGraphic == null)
                return;

            // Drive the button ColorBlock so visual stays even if EventSystem selection changes (e.g., middle-mouse panning).
            var colors = button.colors;
            colors.normalColor = active ? buttonSelectedColor : buttonNormalColor;
            colors.highlightedColor = active ? buttonSelectedColor : buttonHoverColor;
            colors.selectedColor = buttonSelectedColor;
            colors.pressedColor = buttonPressedColor;
            colors.disabledColor = buttonDisabledColor;
            colors.fadeDuration = Mathf.Max(0.05f, colors.fadeDuration);
            button.colors = colors;

            // Immediately apply target tint to override current state visuals.
            button.targetGraphic.color = active ? buttonSelectedColor : buttonNormalColor;
        }

        // ── Keybind Label Overlays ───────────────────────────────────────

        /// <summary>
        /// Creates or updates keybind label text overlays on each command button.
        /// </summary>
        public void ApplyKeybindLabels()
        {
            ApplyKeybindLabelToButton(offensiveStrictButton, offensiveInput);
            ApplyKeybindLabelToButton(defensiveStrictButton, defensiveInput);
            ApplyKeybindLabelToButton(holdStrictButton, holdInput);
            ApplyKeybindLabelToButton(passiveButton, passiveInput);
            ApplyKeybindLabelToButton(stopButton, stopInput);
            ApplyKeybindLabelToButton(followButton, followInput);
            ApplyKeybindLabelToToggle(strictModeToggle, strictModeInput);
        }

        private void ApplyKeybindLabelToButton(Button button, CommandInputBinding binding)
        {
            if (button == null) return;
            ApplyKeybindLabelToTransform(button.transform, binding);
        }

        private void ApplyKeybindLabelToToggle(Toggle toggle, CommandInputBinding binding)
        {
            if (toggle == null) return;

            // For toggles, parent the label to the targetGraphic (Background) instead of the
            // toggle root, because the Background child often covers the root area entirely
            // and would render on top of a root-level label.
            Transform labelParent = toggle.targetGraphic != null
                ? toggle.targetGraphic.transform
                : toggle.transform;

            ApplyKeybindLabelToTransform(labelParent, binding);
        }

        private void ApplyKeybindLabelToTransform(Transform parent, CommandInputBinding binding)
        {
            string displayText = showKeybindLabels ? GetKeybindDisplayText(binding) : "";

            // Find or create the label child
            var labelTransform = parent.Find("KeybindLabel");
            UITextComponent textComp;

            if (labelTransform != null)
            {
                textComp = labelTransform.GetComponent<UITextComponent>();
            }
            else
            {
                var labelGO = new GameObject("KeybindLabel", typeof(RectTransform), typeof(UITextComponent));
                labelGO.transform.SetParent(parent, false);
                var rect = labelGO.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(4, -2);
                rect.sizeDelta = new Vector2(24, 16);
                textComp = labelGO.GetComponent<UITextComponent>();

                // Compensate for any non-unit scale on the parent (e.g. Toggle Background
                // that is scaled up to fill the toggle area). Without this the label text
                // inherits the parent scale and appears oversized / blurry.
                Vector3 parentScale = parent.lossyScale;
                if (parentScale.x != 0 && parentScale.y != 0)
                {
                    Vector3 canvasScale = labelGO.transform.root.lossyScale;
                    float invX = canvasScale.x / parentScale.x;
                    float invY = canvasScale.y / parentScale.y;
                    labelGO.transform.localScale = new Vector3(invX, invY, 1f);
                }
            }

            if (textComp == null) return;

            // Ensure the label renders on top of other children (e.g. Toggle checkmark).
            textComp.transform.SetAsLastSibling();

            textComp.text = displayText;
            textComp.fontSize = keybindLabelFontSize;
            textComp.color = keybindLabelColor;
#if TMP_PRESENT
            textComp.alignment = TMPro.TextAlignmentOptions.TopLeft;
            textComp.fontStyle = TMPro.FontStyles.Bold;
            textComp.enableAutoSizing = false;
            if (keybindLabelFontTMP != null)
                textComp.font = keybindLabelFontTMP;
#else
            if (keybindLabelFont != null)
                textComp.font = keybindLabelFont;
            else
            {
                textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (textComp.font == null) textComp.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            textComp.alignment = TextAnchor.UpperLeft;
            textComp.fontStyle = FontStyle.Bold;
            textComp.resizeTextForBestFit = false;
            textComp.horizontalOverflow = HorizontalWrapMode.Overflow;
            textComp.verticalOverflow = VerticalWrapMode.Overflow;
            textComp.raycastTarget = false;
#endif
        }

        /// <summary>
        /// Returns the display text for a CommandInputBinding's effective key.
        /// Extracts the human-readable key letter from the InputAction binding path.
        /// </summary>
        public static string GetKeybindDisplayText(CommandInputBinding binding)
        {
            if (binding == null) return "";

#if ENABLE_INPUT_SYSTEM
            if (binding.inputAction == null) return "";

            var action = binding.inputAction;
            if (action.bindings.Count > 0)
            {
                string path = action.bindings[0].effectivePath;
                if (string.IsNullOrEmpty(path))
                    path = action.bindings[0].path;

                return ExtractKeyDisplayName(path);
            }

            return "";
#else
            return binding.legacyKey == KeyCode.None ? "" : binding.legacyKey.ToString();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static string ExtractKeyDisplayName(string bindingPath)
        {
            if (string.IsNullOrEmpty(bindingPath)) return "";

            int slashIndex = bindingPath.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < bindingPath.Length - 1)
            {
                string keyPart = bindingPath.Substring(slashIndex + 1);
                // Strip InputSystem #(displayName) wrapper, e.g. "#(A)" → "A"
                if (keyPart.StartsWith("#(") && keyPart.EndsWith(")"))
                    keyPart = keyPart.Substring(2, keyPart.Length - 3);
                keyPart = keyPart.Replace("numpad", "N");
                return keyPart.ToUpperInvariant();
            }

            return bindingPath.ToUpperInvariant();
        }
#endif

        // ── Input Shortcuts ──────────────────────────────────────────────

        private void EnableInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            EnableAction(strictModeInput);
            EnableAction(offensiveInput);
            EnableAction(defensiveInput);
            EnableAction(holdInput);
            EnableAction(passiveInput);
            EnableAction(stopInput);
            EnableAction(followInput);
#endif
        }

        private void DisableInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            DisableAction(strictModeInput);
            DisableAction(offensiveInput);
            DisableAction(defensiveInput);
            DisableAction(holdInput);
            DisableAction(passiveInput);
            DisableAction(stopInput);
            DisableAction(followInput);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static void EnableAction(CommandInputBinding binding)
        {
            if (binding?.inputAction != null)
                binding.inputAction.Enable();
        }

        private static void DisableAction(CommandInputBinding binding)
        {
            if (binding?.inputAction != null)
                binding.inputAction.Disable();
        }
#endif

        private void ProcessInputShortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            if (WasPressed(strictModeInput) && strictModeToggle != null)
                strictModeToggle.isOn = !strictModeToggle.isOn;
            if (WasPressed(offensiveInput) && offensiveStrictButton != null && offensiveStrictButton.interactable)
                HandleOffensiveClicked();
            if (WasPressed(defensiveInput) && defensiveStrictButton != null && defensiveStrictButton.interactable)
                HandleDefensiveClicked();
            if (WasPressed(holdInput) && holdStrictButton != null && holdStrictButton.interactable)
                HandleHoldClicked();
            if (WasPressed(passiveInput) && passiveButton != null && passiveButton.interactable)
                HandlePassiveClicked();
            if (WasPressed(stopInput) && stopButton != null && stopButton.interactable)
                HandleStopClicked();
            if (WasPressed(followInput) && followButton != null && followButton.interactable)
                HandleFollowClicked();
#else
            if (strictModeInput.HasKey() && Input.GetKeyDown(strictModeInput.legacyKey) && strictModeToggle != null)
                strictModeToggle.isOn = !strictModeToggle.isOn;
            if (offensiveInput.HasKey() && Input.GetKeyDown(offensiveInput.legacyKey) && offensiveStrictButton != null && offensiveStrictButton.interactable)
                HandleOffensiveClicked();
            if (defensiveInput.HasKey() && Input.GetKeyDown(defensiveInput.legacyKey) && defensiveStrictButton != null && defensiveStrictButton.interactable)
                HandleDefensiveClicked();
            if (holdInput.HasKey() && Input.GetKeyDown(holdInput.legacyKey) && holdStrictButton != null && holdStrictButton.interactable)
                HandleHoldClicked();
            if (passiveInput.HasKey() && Input.GetKeyDown(passiveInput.legacyKey) && passiveButton != null && passiveButton.interactable)
                HandlePassiveClicked();
            if (stopInput.HasKey() && Input.GetKeyDown(stopInput.legacyKey) && stopButton != null && stopButton.interactable)
                HandleStopClicked();
            if (followInput.HasKey() && Input.GetKeyDown(followInput.legacyKey) && followButton != null && followButton.interactable)
                HandleFollowClicked();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool WasPressed(CommandInputBinding binding)
        {
            return binding?.inputAction != null && binding.inputAction.WasPressedThisFrame();
        }
#endif

        // ── Follow Mode ──────────────────────────────────────────────────

        private void HandleFollowClicked()
        {
            if (rtsController == null) return;

            if (_followModeActive)
            {
                // Toggle off if already in follow mode
                CancelFollowMode();
                return;
            }

            EnterFollowMode();
        }

        /// <summary>
        /// Enters follow targeting mode: changes cursor, suppresses RTS commands,
        /// and waits for the player to click a valid follow target.
        /// </summary>
        private void EnterFollowMode()
        {
            _followModeActive = true;

            // Suppress RTS command processing while in follow targeting mode
            if (rtsController != null)
                rtsController.suppressCommandInput = true;

            // Change cursor
            if (followCursorTexture != null)
            {
                EnsureFollowRuntimeCursorTexture();
                Texture2D cursorTex = _followRuntimeCursorTexture != null ? _followRuntimeCursorTexture : followCursorTexture;
                Cursor.SetCursor(cursorTex, followCursorHotspot, CursorMode.ForceSoftware);
                _followCursorOverrideActive = true;
            }

            PlayClickSfx(followEnterSfx);

            // Highlight the follow button as active
            SetButtonActiveVisual(followButton, true);
        }

        /// <summary>
        /// Cancels follow targeting mode and restores the cursor.
        /// </summary>
        private void CancelFollowMode()
        {
            if (!_followModeActive) return;

            _followModeActive = false;

            // Delayed suppress release (same two-frame pattern as AgentSkillBarUI)
            if (rtsController != null)
                _followSuppressUntilRelease = true;

            // Restore cursor
            if (_followCursorOverrideActive)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                _followCursorOverrideActive = false;
            }

            // Remove follow button highlight
            SetButtonActiveVisual(followButton, false);
        }

        /// <summary>
        /// While in follow targeting mode, waits for the command button click on a valid target.
        /// Supports both collider-based targets (Player Targets, tagged objects) and
        /// collider-less crowd agents via ground raycast + proximity detection.
        /// Escape cancels.
        /// </summary>
        private void ProcessFollowTargetingMode()
        {
            // Cancel on Escape
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelFollowMode();
                return;
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelFollowMode();
                return;
            }
#endif

            // Detect command button press
            bool commandDown = false;
#if ENABLE_INPUT_SYSTEM
            if (rtsController != null && rtsController.commandButton != null)
                commandDown = rtsController.commandButton.WasPressedThisFrame();
#else
            if (rtsController != null)
                commandDown = Input.GetMouseButtonDown(rtsController.commandMouseButton);
#endif

            if (!commandDown) return;

            Debug.Log("[FollowMode] Command button pressed while in follow targeting mode.");

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[FollowMode] Camera.main is null — cannot raycast.");
                return;
            }

            Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
            mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            mousePos = Input.mousePosition;
#endif

            Debug.Log($"[FollowMode] Mouse position: {mousePos}");

            Ray ray = cam.ScreenPointToRay(mousePos);

            // Delegate target resolution to the RTS controller, which handles both
            // collider-based targets (Player Targets, tagged objects) and
            // collider-less crowd agents (ground raycast + proximity detection).
            if (rtsController.TryResolveFollowTarget(ray, out Transform followTarget))
            {
                Debug.Log($"[FollowMode] Valid follow target found: '{followTarget.name}' — issuing follow command.");
                rtsController.FollowSelectedAgents(followTarget);
                PlayClickSfx(followClickSfx);
                CancelFollowMode();
                return;
            }

            Debug.Log("[FollowMode] No valid follow target found — cancelling follow mode.");
            // Clicked on nothing valid — cancel follow mode
            CancelFollowMode();
        }

        /// <summary>
        /// Delayed suppress release for follow mode (mirrors AgentSkillBarUI pattern).
        /// Waits for command button release + one extra frame before clearing RTS suppress.
        /// </summary>
        private void ProcessFollowSuppressRelease()
        {
            if (_followSuppressClearNextFrame)
            {
                if (rtsController != null)
                    rtsController.suppressCommandInput = false;
                _followSuppressClearNextFrame = false;
            }
            else if (_followSuppressUntilRelease)
            {
                bool commandStillHeld = false;
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) commandStillHeld = Mouse.current.rightButton.isPressed;
#else
                commandStillHeld = Input.GetMouseButton(1);
#endif
                if (!commandStillHeld)
                {
                    _followSuppressUntilRelease = false;
                    _followSuppressClearNextFrame = true;
                }
            }
        }

        /// <summary>
        /// Creates a runtime RGBA32 cursor texture from the follow cursor source texture,
        /// ensuring compatibility across platforms (same pattern as AgentSkillBarUI).
        /// </summary>
        private void EnsureFollowRuntimeCursorTexture()
        {
            if (_followRuntimeCursorTexture != null) return;
            if (followCursorTexture == null) return;

            int w = followCursorTexture.width;
            int h = followCursorTexture.height;
            int cursorSize = Mathf.Clamp(Mathf.Max(w, h), 2, 64);

            _followRuntimeCursorTexture = new Texture2D(cursorSize, cursorSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            bool pixelsCopied = false;
            try
            {
                if (followCursorTexture.isReadable)
                {
                    Color[] srcPixels = followCursorTexture.GetPixels();
                    if (w == cursorSize && h == cursorSize)
                    {
                        _followRuntimeCursorTexture.SetPixels(srcPixels);
                    }
                    else
                    {
                        Color[] dstPixels = new Color[cursorSize * cursorSize];
                        for (int y = 0; y < cursorSize; y++)
                            for (int x = 0; x < cursorSize; x++)
                            {
                                int sx = Mathf.Clamp(x * w / cursorSize, 0, w - 1);
                                int sy = Mathf.Clamp(y * h / cursorSize, 0, h - 1);
                                dstPixels[y * cursorSize + x] = srcPixels[sy * w + sx];
                            }
                        _followRuntimeCursorTexture.SetPixels(dstPixels);
                    }
                    pixelsCopied = true;
                }
            }
            catch (System.Exception) { }

            if (!pixelsCopied)
            {
                RenderTexture rt = RenderTexture.GetTemporary(cursorSize, cursorSize, 0, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Bilinear;
                RenderTexture prev = RenderTexture.active;
                Graphics.Blit(followCursorTexture, rt);
                RenderTexture.active = rt;
                _followRuntimeCursorTexture.ReadPixels(new Rect(0, 0, cursorSize, cursorSize), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }

            _followRuntimeCursorTexture.Apply(false, false);
            followCursorHotspot = new Vector2(cursorSize / 2f, cursorSize / 2f);
        }

        private void OnDestroy()
        {
            if (_followRuntimeCursorTexture != null)
            {
                Destroy(_followRuntimeCursorTexture);
                _followRuntimeCursorTexture = null;
            }
        }

        // ── Tooltip System ───────────────────────────────────────────────

        private string _activeTooltipKey;

        /// <summary>
        /// Ensures every button/toggle has an <see cref="RTSCommandTooltipTrigger"/> component
        /// wired back to this panel with the correct key.
        /// </summary>
        private void WireTooltipTriggers()
        {
            WireTooltipTrigger(strictModeToggle != null ? strictModeToggle.gameObject : null, "strict");
            WireTooltipTrigger(offensiveStrictButton != null ? offensiveStrictButton.gameObject : null, "offensive");
            WireTooltipTrigger(defensiveStrictButton != null ? defensiveStrictButton.gameObject : null, "defensive");
            WireTooltipTrigger(holdStrictButton != null ? holdStrictButton.gameObject : null, "hold");
            WireTooltipTrigger(passiveButton != null ? passiveButton.gameObject : null, "passive");
            WireTooltipTrigger(stopButton != null ? stopButton.gameObject : null, "stop");
            WireTooltipTrigger(followButton != null ? followButton.gameObject : null, "follow");
        }

        private void WireTooltipTrigger(GameObject buttonGO, string key)
        {
            if (buttonGO == null) return;

            var trigger = buttonGO.GetComponent<RTSCommandTooltipTrigger>();
            if (trigger == null)
                trigger = buttonGO.AddComponent<RTSCommandTooltipTrigger>();

            trigger.tooltipKey = key;
            trigger.panel = this;
        }

        /// <summary>
        /// Returns the <see cref="CommandTooltipEntry"/> for a given tooltip key,
        /// or null if the key is unknown.
        /// </summary>
        private CommandTooltipEntry GetTooltipEntry(string key)
        {
            switch (key)
            {
                case "strict":    return strictModeTooltip;
                case "offensive": return offensiveTooltip;
                case "defensive": return defensiveTooltip;
                case "hold":      return holdTooltip;
                case "passive":   return passiveTooltip;
                case "stop":      return stopTooltip;
                case "follow":    return followTooltip;
                default:          return null;
            }
        }

        /// <summary>
        /// Called by <see cref="RTSCommandTooltipTrigger"/> when the pointer enters a button.
        /// Resolves the tooltip text (with optional localization) and positions the panel above the button.
        /// </summary>
        public void ShowCommandTooltip(string key, GameObject buttonGO)
        {
            if (!enableTooltips || tooltipPanel == null || tooltipText == null)
                return;

            CommandTooltipEntry entry = GetTooltipEntry(key);
            if (entry == null) return;

            string resolved = entry.ResolvedText;
            if (string.IsNullOrEmpty(resolved))
                return;

            _activeTooltipKey = key;
            tooltipText.text = resolved;

            // Position the tooltip above the hovered button
            PositionTooltipAboveButton(buttonGO);

            tooltipPanel.gameObject.SetActive(true);
        }

        /// <summary>
        /// Called by <see cref="RTSCommandTooltipTrigger"/> when the pointer exits a button.
        /// Only hides if the given key matches the currently active tooltip.
        /// </summary>
        public void HideCommandTooltip(string key)
        {
            if (_activeTooltipKey == key)
            {
                HideTooltipPanel();
            }
        }

        private void HideTooltipPanel()
        {
            _activeTooltipKey = null;
            if (tooltipPanel != null)
                tooltipPanel.gameObject.SetActive(false);
        }

        private void PositionTooltipAboveButton(GameObject buttonGO)
        {
            if (buttonGO == null || tooltipPanel == null) return;

            RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
            if (buttonRect == null) return;

            Canvas canvas = tooltipPanel.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // Get the button's world-space corners and compute its top-center
            Vector3[] corners = new Vector3[4];
            buttonRect.GetWorldCorners(corners);
            // corners: 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right
            Vector3 topCenter = (corners[1] + corners[2]) * 0.5f;

            // Convert to the tooltip panel's parent space
            RectTransform tooltipParent = tooltipPanel.parent as RectTransform;
            if (tooltipParent == null) return;

            Vector2 localPoint;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    tooltipParent, RectTransformUtility.WorldToScreenPoint(cam, topCenter), cam, out localPoint))
                return;

            // Anchor the tooltip at the bottom-center, positioned above the button
            tooltipPanel.pivot = new Vector2(0.5f, 0f);
            tooltipPanel.anchoredPosition = new Vector2(localPoint.x, localPoint.y + tooltipOffsetY);
        }
    }

    /// <summary>
    /// Serializable input binding for a single command panel action.
    /// Uses InputAction with the New Input System, or KeyCode with Legacy.
    /// </summary>
    [System.Serializable]
    public class CommandInputBinding
    {
        [Tooltip("Display label for this binding.")]
        public string label;

#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action for this command (New Input System).")]
        public InputAction inputAction;
#else
        [Tooltip("Legacy key for this command.")]
        public KeyCode legacyKey = KeyCode.None;

        public bool HasKey() => legacyKey != KeyCode.None;
#endif

        public CommandInputBinding() { }

        public CommandInputBinding(string label)
        {
            this.label = label;
        }
    }

    /// <summary>
    /// Serializable tooltip entry for a single command button.
    /// Contains a fallback display text and an optional Unity Localization reference.
    /// </summary>
    [Serializable]
    public class CommandTooltipEntry
    {
        [Tooltip("Fallback tooltip text shown when localization is not available or not configured.")]
        public string text;

        [Tooltip("Optional Unity Localization reference for this tooltip.")]
        public OptionalLocalizedString localization;

        /// <summary>Returns the localized text if available, otherwise the fallback text.</summary>
        public string ResolvedText => localization.Resolve(text);

        public CommandTooltipEntry() { }

        public CommandTooltipEntry(string defaultText)
        {
            text = defaultText;
        }
    }
}
