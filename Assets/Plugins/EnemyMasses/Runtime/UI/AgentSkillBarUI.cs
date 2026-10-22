using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Arawn.EnemyMasses.Runtime.Skills;
using Arawn.EnemyMasses.Runtime.Damageables;

#if TMP_PRESENT
using UITextComponent = TMPro.TextMeshProUGUI;
#else
using UITextComponent = UnityEngine.UI.Text;
#endif

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// Binding between a skill slot index and its input action.
    /// Allows designers to assign a key per skill slot in the inspector.
    /// </summary>
    [Serializable]
    public class SkillSlotInputBinding
    {
        [Tooltip("Display label for this binding (for inspector readability).")]
        public string label = "Skill 1";

#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action for activating this skill slot.")]
        public InputAction inputAction = new InputAction("Skill 1", InputActionType.Button, "<Keyboard>/q");
#else
        [Tooltip("Legacy key for activating this skill slot.")]
        public KeyCode legacyKey = KeyCode.Q;
#endif
    }

    /// <summary>
    /// How the word "minutes" is abbreviated on the cooldown label.
    /// </summary>
    public enum MinuteAbbreviation
    {
        [InspectorName("M")]   M,
        [InspectorName("m")]   m,
        [InspectorName("Min.")] Min_dot,
        [InspectorName("min.")] min_dot
    }

    /// <summary>
    /// WoW-style skill bar UI that displays skills from the currently selected EnemyCrowd agents.
    /// Dynamically updates when RTS selection changes. Supports Unity's new Input System for keybinds.
    /// 
    /// Add to scene via: Right-click Hierarchy > Enemy Masses > UI > Agent Skill Bar
    /// </summary>
    [AddComponentMenu("Enemy Masses/UI/Agent Skill Bar")]
    public class AgentSkillBarUI : MonoBehaviour
    {
        #region References

        [Header("References")]
        [Tooltip("RTS Controller to listen for selection changes. Auto-discovered if null.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("Crowd Controller for accessing agent data and skill execution. Auto-discovered if null.")]
        public EnemyMassesCrowdController crowdController;

        #endregion

        #region Skill Bar Layout

        [Header("Skill Bar Layout")]
        [Tooltip("Root container that holds the skill slots. Hidden when no skills to show.")]
        public RectTransform skillBarRoot;

        [Tooltip("Container (HorizontalLayoutGroup) where skill slot UI elements are spawned.")]
        public RectTransform slotContainer;

        [Tooltip("Prefab for each skill slot. If null, slots are created programmatically at runtime.")]
        public GameObject skillSlotPrefab;

        [Tooltip("Size of each skill slot in pixels.")]
        public Vector2 slotSize = new Vector2(64, 64);

        [Tooltip("Spacing between skill slots in pixels.")]
        public float slotSpacing = 4f;

        [Tooltip("Padding around the skill bar.")]
        public RectOffset barPadding;

        #endregion

        #region Input Bindings

        [Header("Input Bindings")]
        [Tooltip("Input bindings for each skill slot. The index maps to the skill slot index on the selected EnemyCrowd.\n" +
                 "Configure the desired key per slot. Unused bindings (beyond the skill count) are ignored.")]
        public List<SkillSlotInputBinding> skillInputBindings = new List<SkillSlotInputBinding>();

        #endregion

        #region Tooltip

        [Header("Tooltip")]
        [Tooltip("Panel that shows the skill preview/tooltip on hover.")]
        public RectTransform tooltipPanel;

        [Tooltip("Text element for the tooltip content (supports rich text).")]
        public UITextComponent tooltipText;

        [Tooltip("Offset of the tooltip from the hovered slot.")]
        public Vector2 tooltipOffset = new Vector2(0, 80);

        [Tooltip("Use rich text formatting in the tooltip (bold headers, etc.).")]
        public bool tooltipRichText = true;

        #endregion

        #region Targeting

        [Header("Targeting")]
        [Tooltip("Cursor texture shown during skill targeting mode (e.g. crosshair). Assign a Texture2D asset.")]
        public Texture2D targetingCursorTexture;

        [Tooltip("Hotspot offset for the targeting cursor (typically center of texture).")]
        public Vector2 targetingCursorHotspot = new Vector2(16, 16);

        [Tooltip("LayerMask for raycasting potential targets (agents, colliders). Uses RTS Controller's attackTargetMask if not set.")]
        public LayerMask targetRaycastMask = -1;

        [Tooltip("LayerMask for ground raycasting (for fallback target finding and ground-targeted AOE). Uses RTS Controller's groundMask if not set.")]
        public LayerMask groundRaycastMask = -1;

        [Tooltip("Radius for finding nearby agents when clicking near the ground (same as RTS attackTargetClickRadius).")]
        public float targetClickRadius = 3f;

        [Tooltip("Border color on the skill slot that is actively being targeted.")]
        public Color activeTargetingBorderColor = new Color(0.2f, 1f, 0.4f, 1f);

        [Header("Ground Target Indicator")]
        [Tooltip("Type of visual indicator shown on the ground during GroundTargetedAOE targeting.")]
        public GroundTargetIndicatorType groundIndicatorType = GroundTargetIndicatorType.RuntimeCircle;

        [Tooltip("Color of the ground indicator when the target is within range of at least one selected agent.")]
        public Color groundIndicatorInRangeColor = new Color(0.2f, 1f, 0.4f, 0.8f);

        [Tooltip("Color of the ground indicator when the target is out of range of all selected agents.")]
        public Color groundIndicatorOutOfRangeColor = new Color(1f, 0.2f, 0.2f, 0.8f);

        [Tooltip("Number of line segments used to draw the RuntimeCircle indicator. Higher = smoother circle.")]
        [Range(16, 128)]
        public int groundIndicatorSegments = 48;

        [Tooltip("Width of the RuntimeCircle line.")]
        public float groundIndicatorLineWidth = 0.08f;

        [Tooltip("Height offset above the ground for the indicator to prevent z-fighting.")]
        public float groundIndicatorYOffset = 0.05f;

        [Tooltip("Custom prefab to instantiate as the ground indicator. " +
                 "Only used when Indicator Type is set to CustomPrefab.\n" +
                 "The prefab will be scaled uniformly so that 1 unit radius = skill AOE radius.")]
        public GameObject groundIndicatorPrefab;

        [Header("Max Range Indicator")]
        [Tooltip("Show a max-range indicator during manual targeting for non-ground skills (SingleEnemy, Projectile, Cone, etc.).")]
        public bool showMaxRangeIndicator = true;

        [Tooltip("Type of visual indicator shown for max-range targeting.")]
        public GroundTargetIndicatorType maxRangeIndicatorType = GroundTargetIndicatorType.RuntimeCircle;

        [Tooltip("Also show the max-range indicator during GroundTargetedAOE targeting.")]
        public bool showMaxRangeForGroundTargetedAOE = true;

        [Tooltip("Color of the max-range indicator when the cursor point is within range of at least one selected caster.")]
        public Color maxRangeIndicatorInRangeColor = new Color(0.2f, 0.7f, 1f, 0.75f);

        [Tooltip("Color of the max-range indicator when the cursor point is out of range of all selected casters.")]
        public Color maxRangeIndicatorOutOfRangeColor = new Color(1f, 0.4f, 0.2f, 0.75f);

        [Tooltip("Number of line segments used to draw the RuntimeCircle max-range indicator. Higher = smoother circle.")]
        [Range(16, 128)]
        public int maxRangeIndicatorSegments = 64;

        [Tooltip("Width of the RuntimeCircle max-range indicator line.")]
        public float maxRangeIndicatorLineWidth = 0.07f;

        [Tooltip("Height offset above the ground for the max-range indicator to prevent z-fighting.")]
        public float maxRangeIndicatorYOffset = 0.03f;

        [Tooltip("Custom prefab to instantiate as the max-range indicator. " +
                 "Only used when Indicator Type is set to CustomPrefab.\n" +
                 "The prefab will be scaled uniformly so that 1 unit radius = skill MaxRange.")]
        public GameObject maxRangeIndicatorPrefab;

        #endregion

        #region Auto-Cast Defaults

        [Header("Auto-Cast Defaults")]
        [HideInInspector, System.Obsolete("Use AgentSkill.autocastEligibility instead. This field is no longer read at runtime.")]
        public bool defaultAutocastEnabled = false;

#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action for toggling auto-cast on the hovered skill slot.\n" +
                 "Default: right mouse button. Can be rebound to any button or key.")]
        public InputAction autoCastToggleAction = new InputAction("Auto-Cast Toggle", InputActionType.Button, "<Mouse>/rightButton");
#else
        [Tooltip("Legacy key for toggling auto-cast on the hovered skill slot.")]
        public KeyCode autoCastToggleLegacyKey = KeyCode.Mouse1;
#endif

        [Header("Auto-Cast Visuals")]
        [Tooltip("Border color applied to skill slots when auto-cast is active.\n" +
                 "Overrides each slot's autocastBorderColor field.")]
        public Color autocastBorderColor = new Color(1f, 0.85f, 0.1f, 1f);

        [Tooltip("Optional overlay sprite shown on top of the skill icon when auto-cast is active.\n" +
                 "If null, only the border color is used. If set, both border color and overlay are shown.")]
        public Sprite autocastOverlaySprite;

        [Tooltip("Tint color for the auto-cast overlay sprite.\n" +
                 "Use alpha to control opacity (e.g. semi-transparent glow).")]
        public Color autocastOverlayTint = new Color(1f, 0.85f, 0.1f, 0.35f);

        #endregion

        #region Appearance

        [Header("Appearance")]
        [Tooltip("Background image for the skill bar panel.")]
        public Image barBackground;

        [Tooltip("Background color for the skill bar.")]
        public Color barBackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);

        [Tooltip("Hide the skill bar completely when no agents are selected.")]
        public bool hideWhenNoSelection = true;

        [Tooltip("Fade animation duration when showing/hiding the bar (0 = instant).")]
        [Range(0f, 1f)]
        public float fadeDuration = 0.15f;

        [Tooltip("When enabled, cooldowns of 60 seconds or more are shown as minutes (e.g. '2 min.').\n" +
                 "Once the remaining time drops below 60 seconds it switches back to seconds.")]
        public bool showCooldownAsMinutes = true;

        [Tooltip("How 'minutes' is abbreviated on the cooldown label.")]
        public MinuteAbbreviation minuteAbbreviation = MinuteAbbreviation.min_dot;

        [Tooltip("Color for the keybind label text on each skill slot.\n" +
                 "Leave at default for a warm gold tone.")]
        public Color keybindLabelColor = new Color(0.9f, 0.85f, 0.6f, 1f);

        [Tooltip("Font size for the keybind label text.")]
        [Range(6, 36)]
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

        #endregion

        #region Audio

        [Header("Audio")]
        [Tooltip("Global sound played when any skill slot is left-clicked.\n" +
                 "Individual skills can override this via their own Skill Bar Click Sound field.")]
        public AudioClip clickSound;

        [Tooltip("Global sound played when auto-cast is turned ON for any skill slot.\n" +
                 "Individual skills can override this via their own Auto-Cast On Sound field.")]
        public AudioClip autoCastOnSound;

        [Tooltip("Global sound played when auto-cast is turned OFF for any skill slot.\n" +
                 "Individual skills can override this via their own Auto-Cast Off Sound field.")]
        public AudioClip autoCastOffSound;

        [Tooltip("Global sound played when a skill target is confirmed (right-click or input button during targeting mode).\n" +
                 "Individual skills can override this via their own Skill Bar Confirm Sound field.")]
        public AudioClip confirmSound;

        [Tooltip("Optional sound played when the player tries to activate a skill that is still on cooldown.\n" +
                 "If null, no sound is played — the click is silently ignored.")]
        public AudioClip cooldownErrorSound;

        [Tooltip("Volume for skill bar UI sounds (click, right-click, confirm).")]
        [Range(0f, 1f)]
        public float clickSoundVolume = 1f;

        [Tooltip("Base pitch for skill bar UI sounds.\n" +
                 "1.0 = normal pitch, 0.5 = half speed, 2.0 = double speed.")]
        [Range(0.5f, 2f)]
        public float basePitch = 1f;

        [Tooltip("Random pitch variation applied symmetrically around Base Pitch.\n" +
                 "E.g. 0.1 with base 1.0 produces a random pitch in [0.9, 1.1] each time.")]
        [Range(0f, 0.5f)]
        public float pitchVariation = 0f;

        #endregion

        #region Events

        [Header("Events")]
        [Tooltip("Fired when a skill is activated via input or click. Parameter: skill slot index.")]
        public UnityEngine.Events.UnityEvent<int> onSkillActivated;

        #endregion

        #region Debug

        [Header("Debug")]
        [Tooltip("Enable detailed debug logging for the skill bar targeting system.")]
        public bool debugMode;

        #endregion

        // Runtime state
        private EnemyCrowd _currentEnemyCrowd;
        private IExternalSkillCaster _currentExternalCaster;
        private List<AgentSkillSlotUI> _activeSlots = new List<AgentSkillSlotUI>();
        private List<int> _currentSelectedIndices = new List<int>();
        private CanvasGroup _canvasGroup;
        private float _fadeTarget;
        private bool _isVisible;
        private Camera _camera;

        // Targeting mode state
        private bool _targetingModeActive;
        private AgentSkillSlotUI _pendingTargetSlot;
        private bool _cursorOverrideActive;
        private Texture2D _runtimeCursorTexture;

        // Ground target indicator state
        private GameObject _groundIndicatorInstance;
        private LineRenderer _groundIndicatorLine;
        private Material _groundIndicatorMaterial;

        // Max-range indicator state (manual non-ground targeting)
        private GameObject _maxRangeIndicatorInstance;
        private LineRenderer _maxRangeIndicatorLine;
        private Material _maxRangeIndicatorMaterial;
        private float _maxRangeIndicatorRadius;
        private bool _maxRangeIndicatorUsesConeShape;
        private bool _maxRangeIndicatorUsesLineShape;
        private float _maxRangeIndicatorConeAngle;
        private float _maxRangeIndicatorSkillLineWidth;

        // Delayed suppress release — keeps RTS command input suppressed until the
        // command button is fully released, preventing held-state leakage.
        private bool _suppressUntilCommandRelease;

        // One-frame delay: after the command button is released we keep suppress
        // active for one more frame so the RTS controller's _commandButtonUp path
        // doesn't fire a move command from the same button release that confirmed
        // the skill target.
        private bool _suppressClearNextFrame;

        // Object pool for slot instances
        private List<GameObject> _slotPool = new List<GameObject>();

        // Tracks EnemyCrowds that have already had auto-cast defaults applied
        // so we don't reset player toggles on re-selection of the same crowd.
        private HashSet<EnemyCrowd> _autocastDefaultsApplied = new HashSet<EnemyCrowd>();

        // Currently hovered skill slot (used for auto-cast toggle via input action).
        private AgentSkillSlotUI _hoveredSlot;

        // Cached white sprite for cooldown overlay fill rendering
        private static Sprite _whiteSprite;

        private void Start()
        {
            if (barPadding == null)
                barPadding = new RectOffset(8, 8, 8, 8);

            // Auto-discover references
            if (rtsController == null)
            {
#if UNITY_2023_1_OR_NEWER
                rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
#else
                rtsController = FindObjectOfType<EnemyMassesRTSController>();
#endif
            }

            if (crowdController == null)
            {
#if UNITY_2023_1_OR_NEWER
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
#else
                crowdController = FindObjectOfType<EnemyMassesCrowdController>();
#endif
            }

            _camera = Camera.main;

            // Auto-load cursor texture from Resources if inspector reference is missing
            if (targetingCursorTexture == null)
            {
                targetingCursorTexture = Resources.Load<Texture2D>("EnemyMassesCursorTarget");
                if (targetingCursorTexture != null)
                    Debug.Log("[AgentSkillBar] targetingCursorTexture was NULL — auto-loaded from Resources/EnemyMassesCursorTarget");
                else
                    Debug.LogWarning("[AgentSkillBar] targetingCursorTexture is NULL and Resources/EnemyMassesCursorTarget not found. " +
                        "Assign a Texture2D in the inspector or place target.png in a Resources folder.");
            }

            if (debugMode)
            {
                Debug.Log($"[AgentSkillBar] Start — rtsController={rtsController != null}, crowdController={crowdController != null}, " +
                    $"camera={_camera != null}, " +
                    $"targetingCursorTexture={targetingCursorTexture != null}");
            }

            // Sync raycast masks from the RTS controller if defaults weren't overridden
            if (rtsController != null)
            {
                if (targetRaycastMask == -1) targetRaycastMask = rtsController.attackTargetMask;
                if (groundRaycastMask == -1) groundRaycastMask = rtsController.groundMask;
                if (targetClickRadius <= 0f) targetClickRadius = rtsController.attackTargetClickRadius;
            }

            // Setup canvas group for fading
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // Apply bar background
            if (barBackground != null)
                barBackground.color = barBackgroundColor;

            // Setup layout group on slot container
            if (slotContainer != null)
            {
                var layoutGroup = slotContainer.GetComponent<HorizontalLayoutGroup>();
                if (layoutGroup == null)
                    layoutGroup = slotContainer.gameObject.AddComponent<HorizontalLayoutGroup>();

                layoutGroup.spacing = slotSpacing;
                layoutGroup.padding = barPadding;
                layoutGroup.childAlignment = TextAnchor.MiddleCenter;
                layoutGroup.childForceExpandWidth = false;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childControlWidth = false;
                layoutGroup.childControlHeight = false;
            }

            // Subscribe to selection events
            if (rtsController != null)
            {
                rtsController.SelectionChanged += HandleSelectionChanged;
                rtsController.SelectionCleared += HandleSelectionCleared;
            }

            // Auto-populate default keybindings if none configured
            if (skillInputBindings == null || skillInputBindings.Count == 0)
                PopulateDefaultBindings();

            // Enable input actions
            EnableInputActions();

            // Ensure tooltip panel has a VerticalLayoutGroup for ContentSizeFitter to work
            EnsureTooltipLayout();

            // Hide tooltip
            HideTooltip();

            // Initial state: hidden
            SetBarVisible(false, true);
        }

        /// <summary>
        /// Creates a runtime-compatible cursor texture from the assigned targetingCursorTexture.
        /// Cursor.SetCursor requires: RGBA32, readable, alphaIsTransparency, no mip chain.
        /// The source texture from the asset database may not meet these requirements.
        /// Uses direct pixel copy when readable (most reliable), GPU Blit as fallback.
        /// </summary>
        private void EnsureRuntimeCursorTexture()
        {
            if (_runtimeCursorTexture != null) return;
            if (targetingCursorTexture == null) return;

            int w = targetingCursorTexture.width;
            int h = targetingCursorTexture.height;

            // Cursor must be ≤ 256 on most platforms; use 32 for performance
            int cursorSize = Mathf.Clamp(Mathf.Max(w, h), 2, 64);

            _runtimeCursorTexture = new Texture2D(cursorSize, cursorSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            bool pixelsCopied = false;

            // Strategy 1: Direct pixel copy (most reliable — avoids GPU Blit issues on some platforms)
            try
            {
                if (targetingCursorTexture.isReadable)
                {
                    Color[] srcPixels = targetingCursorTexture.GetPixels();
                    if (w == cursorSize && h == cursorSize)
                    {
                        _runtimeCursorTexture.SetPixels(srcPixels);
                    }
                    else
                    {
                        // Nearest-neighbour resize
                        Color[] dstPixels = new Color[cursorSize * cursorSize];
                        for (int y = 0; y < cursorSize; y++)
                            for (int x = 0; x < cursorSize; x++)
                            {
                                int sx = Mathf.Clamp(x * w / cursorSize, 0, w - 1);
                                int sy = Mathf.Clamp(y * h / cursorSize, 0, h - 1);
                                dstPixels[y * cursorSize + x] = srcPixels[sy * w + sx];
                            }
                        _runtimeCursorTexture.SetPixels(dstPixels);
                    }
                    pixelsCopied = true;
                }
            }
            catch (System.Exception)
            {
                // Texture may report isReadable=true but still fail; fall through to Blit
            }

            // Strategy 2: GPU Blit fallback (for non-readable or compressed textures)
            if (!pixelsCopied)
            {
                RenderTexture rt = RenderTexture.GetTemporary(cursorSize, cursorSize, 0, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Bilinear;
                RenderTexture prev = RenderTexture.active;
                Graphics.Blit(targetingCursorTexture, rt);
                RenderTexture.active = rt;
                _runtimeCursorTexture.ReadPixels(new Rect(0, 0, cursorSize, cursorSize), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }

            _runtimeCursorTexture.Apply(false, false); // updateMipmaps=false, makeNoLongerReadable=false

            // Update hotspot to center of the runtime texture
            targetingCursorHotspot = new Vector2(cursorSize / 2f, cursorSize / 2f);
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (rtsController != null)
            {
                rtsController.SelectionChanged -= HandleSelectionChanged;
                rtsController.SelectionCleared -= HandleSelectionCleared;
            }

            // Disable input actions
            DisableInputActions();

            // Restore cursor if targeting was active and re-enable RTS commands
            CancelTargetingMode();
            _suppressUntilCommandRelease = false; // Cancel any pending delayed release
            _suppressClearNextFrame = false;
            if (rtsController != null)
                rtsController.suppressCommandInput = false;

            // Clean up runtime cursor texture
            if (_runtimeCursorTexture != null && _runtimeCursorTexture != targetingCursorTexture)
            {
                DestroyImmediate(_runtimeCursorTexture);
                _runtimeCursorTexture = null;
            }

            // Clean up ground indicator
            HideGroundIndicator();
            HideMaxRangeIndicator();
        }

        private void Update()
        {
            // Handle fade
            if (_canvasGroup != null && fadeDuration > 0f)
            {
                float target = _isVisible ? 1f : 0f;
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, target, Time.unscaledDeltaTime / fadeDuration);

                if (!_isVisible && _canvasGroup.alpha <= 0f && skillBarRoot != null && skillBarRoot.gameObject.activeSelf)
                    skillBarRoot.gameObject.SetActive(false);
            }

            // Handle input
            ProcessInput();

            // Handle targeting mode
            if (_targetingModeActive)
                ProcessTargetingMode();

            // Release RTS command suppression once the command button is fully released,
            // but delay the actual clear by one extra frame. This prevents the RTS
            // controller's _commandButtonUp branch from issuing a move command on the
            // same frame the button is released (the release that confirmed the skill).
            if (_suppressClearNextFrame)
            {
                if (rtsController != null)
                    rtsController.suppressCommandInput = false;
                _suppressClearNextFrame = false;
            }
            else if (_suppressUntilCommandRelease)
            {
                bool commandStillHeld = false;
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) commandStillHeld = Mouse.current.rightButton.isPressed;
#else
                commandStillHeld = Input.GetMouseButton(1);
#endif
                if (!commandStillHeld)
                {
                    _suppressUntilCommandRelease = false;
                    _suppressClearNextFrame = true;
                }
            }

            // Fire queued skills when agents walk into range (WC3-style)
            ProcessQueuedSkills();

            // Update cooldowns
            UpdateCooldowns();
        }

        #region Selection Handling

        private void HandleSelectionChanged(SelectionChangedEventData data)
        {
            // Don't cancel targeting mode from selection changes — the targeting
            // system manages its own lifecycle (Escape / target click / miss-click).
            // Selection changes during targeting can happen due to input overlap.

            if (data == null || data.selectedIndices == null || data.selectedIndices.Count == 0)
            {
                // No crowd agents selected — check if an external skill caster is selected
                if (TryShowExternalSkillCaster())
                    return;

                ClearSkillBar();
                return;
            }

            // Crowd agents are selected — clear any external caster reference
            _currentExternalCaster = null;

            _currentSelectedIndices.Clear();
            _currentSelectedIndices.AddRange(data.selectedIndices);

            // Get the EnemyCrowd from the first selected agent
            EnemyCrowd selectedCrowd = GetEnemyCrowdFromSelection(data.selectedIndices);

            if (selectedCrowd == null)
            {
                ClearSkillBar();
                return;
            }

            // Only rebuild if the crowd changed
            if (selectedCrowd != _currentEnemyCrowd)
            {
                _currentEnemyCrowd = selectedCrowd;
                RebuildSkillBar(selectedCrowd);
            }
        }

        /// <summary>
        /// Checks if the RTS controller has a selected external selectable that implements
        /// <see cref="IExternalSkillCaster"/> and, if so, rebuilds the skill bar for it.
        /// </summary>
        private bool TryShowExternalSkillCaster()
        {
            if (rtsController == null) return false;

            var externals = rtsController.SelectedExternalAgents;
            if (externals == null || externals.Count == 0) return false;

            // Use the first external selectable that implements IExternalSkillCaster
            for (int i = 0; i < externals.Count; i++)
            {
                if (externals[i] is IExternalSkillCaster caster)
                {
                    if (caster.SkillConfig == null || !caster.SkillConfig.enableSkillSystem)
                        continue;

                    if (caster != (IExternalSkillCaster)_currentExternalCaster)
                    {
                        _currentEnemyCrowd = null;
                        _currentSelectedIndices.Clear();
                        _currentExternalCaster = caster;
                        RebuildSkillBarForExternal(caster);
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the skill bar from an <see cref="IExternalSkillCaster"/> rather than an EnemyCrowd.
        /// Mirrors <see cref="RebuildSkillBar(EnemyCrowd)"/> but sources skill slots from the caster interface.
        /// </summary>
        private void RebuildSkillBarForExternal(IExternalSkillCaster caster)
        {
            ClearSlots();

            var skillSlots = caster.SkillSlots;
            if (skillSlots == null || skillSlots.Count == 0)
            {
                SetBarVisible(false);
                return;
            }

            if (debugMode)
                Debug.Log($"[AgentSkillBar] RebuildSkillBarForExternal — caster={caster.EnemyTypeName}, slotCount={skillSlots.Count}");

            for (int i = 0; i < skillSlots.Count; i++)
            {
                var slot = skillSlots[i];
                if (slot == null || slot.skill == null || !slot.enabled)
                    continue;

                var slotUI = CreateSlotUI(i);
                if (slotUI != null)
                {
                    slotUI.autocastBorderColor = autocastBorderColor;
                    slotUI.ApplyAutocastOverlaySettings(autocastOverlaySprite, autocastOverlayTint);
                    slotUI.ApplyKeybindLabelStyle(keybindLabelColor, keybindLabelFontSize,
#if TMP_PRESENT
                        keybindLabelFontTMP
#else
                        keybindLabelFont
#endif
                    );

                    string keybindText = GetKeybindDisplayText(i);
                    slotUI.Setup(slot.skill, i, keybindText);
                    slotUI.ParentBar = this;
                    slotUI.SetAutocastVisual(false); // External casters don't support auto-cast yet
                    slotUI.transform.SetAsLastSibling();
                    _activeSlots.Add(slotUI);
                }
            }

            SetBarVisible(_activeSlots.Count > 0);
        }

        private void HandleSelectionCleared()
        {
            // When targeting mode is active, don't clear the bar or cancel targeting.
            // The RTS controller may fire SelectionCleared due to input overlap (e.g. left-click
            // on a skill slot also registers as a ground click). Targeting mode handles its own
            // cancellation via Escape or ProcessTargetingMode miss-click.
            if (_targetingModeActive)
            {
                if (debugMode) Debug.Log("[AgentSkillBar] HandleSelectionCleared — ignored (targeting mode active).");
                return;
            }

            ClearSkillBar();
        }

        private EnemyCrowd GetEnemyCrowdFromSelection(List<int> selectedIndices)
        {
            if (crowdController == null || selectedIndices == null || selectedIndices.Count == 0)
                return null;

            // Use the first selected agent to determine the EnemyCrowd
            var agent = crowdController.GetAgent(selectedIndices[0]);
            if (agent == null) return null;

            // Only show skills for player-controlled factions
            if (rtsController != null && !rtsController.IsAgentPlayerControlled(selectedIndices[0]))
                return null;

            return agent.enemyCrowd;
        }

        #endregion

        #region Skill Bar Building

        private void RebuildSkillBar(EnemyCrowd enemyCrowd)
        {
            // Clear existing slots
            ClearSlots();

            if (enemyCrowd == null || !enemyCrowd.skillSystemConfig.enableSkillSystem)
            {
                SetBarVisible(false);
                return;
            }

            var skillSlots = enemyCrowd.skillSystemConfig.skillSlots;
            if (skillSlots == null || skillSlots.Count == 0)
            {
                SetBarVisible(false);
                return;
            }

            // On first display for this crowd, initialize per-slot auto-cast overrides
            // based on the designer toggle. The override fields are [NonSerialized]
            // so they always start at false/AIControlled, but we still apply defaults
            // here based on each skill's AutocastEligibility setting.
            bool firstDisplay = !_autocastDefaultsApplied.Contains(enemyCrowd);
            if (firstDisplay)
                _autocastDefaultsApplied.Add(enemyCrowd);

            if (debugMode)
                Debug.Log($"[AgentSkillBar] RebuildSkillBar — crowd={enemyCrowd.name}, firstDisplay={firstDisplay}, slotCount={skillSlots.Count}");

            // Create a slot UI for each valid skill
            for (int i = 0; i < skillSlots.Count; i++)
            {
                var slot = skillSlots[i];
                if (slot == null || slot.skill == null || !slot.enabled)
                    continue;

                // Apply auto-cast defaults on first display for this crowd,
                // using each skill's own AutocastEligibility setting.
                if (firstDisplay)
                {
                    var eligibility = slot.skill.Autocast;
                    if (eligibility == AutocastEligibility.DefaultOn)
                    {
                        // Skill wants auto-cast ON by default
                        slot.overrideControlMode = true;
                        slot.controlModeOverride = SkillControlMode.Hybrid;
                    }
                    else if (eligibility == AutocastEligibility.DefaultOff)
                    {
                        // Explicit OFF so AI evaluation paths that include non-overridden
                        // slots still respect DefaultOff for this crowd at runtime.
                        slot.overrideControlMode = true;
                        slot.controlModeOverride = SkillControlMode.ManualOnly;
                    }
                    else
                    {
                        // NotEligible: keep no per-slot override.
                        slot.overrideControlMode = false;
                    }
                }

                var slotUI = CreateSlotUI(i);
                if (slotUI != null)
                {
                    // Push bar-level visual settings into each slot
                    slotUI.autocastBorderColor = autocastBorderColor;
                    slotUI.ApplyAutocastOverlaySettings(autocastOverlaySprite, autocastOverlayTint);
                    slotUI.ApplyKeybindLabelStyle(keybindLabelColor, keybindLabelFontSize,
#if TMP_PRESENT
                        keybindLabelFontTMP
#else
                        keybindLabelFont
#endif
                    );

                    string keybindText = GetKeybindDisplayText(i);
                    slotUI.Setup(slot.skill, i, keybindText);
                    slotUI.ParentBar = this;
                    
                    // Set auto-cast visual based on effective auto-cast state.
                    bool isAutocast = IsSlotAutocastEnabled(slot);
                    slotUI.SetAutocastVisual(isAutocast);

                    if (debugMode)
                        Debug.Log($"[AgentSkillBar]   Slot {i}: skill={slot.skill.SkillName}, autocastEligibility={slot.skill.Autocast}, overrideControlMode={slot.overrideControlMode}, isAutocast={isAutocast}");
                    
                    // Ensure sibling order matches the EnemyCrowd skill slot order.
                    // Pooled slots may retain stale sibling indices, causing the
                    // layout to shuffle on deselect/reselect.
                    slotUI.transform.SetAsLastSibling();

                    _activeSlots.Add(slotUI);
                }
            }

            SetBarVisible(_activeSlots.Count > 0);
        }

        private void ClearSkillBar()
        {
            _currentEnemyCrowd = null;
            _currentExternalCaster = null;
            _currentSelectedIndices.Clear();
            ClearSlots();
            SetBarVisible(false);
        }

        private void ClearSlots()
        {
            foreach (var slot in _activeSlots)
            {
                if (slot != null && slot.gameObject != null)
                {
                    slot.gameObject.SetActive(false);
                    _slotPool.Add(slot.gameObject);
                }
            }
            _activeSlots.Clear();
        }

        private AgentSkillSlotUI CreateSlotUI(int slotIndex)
        {
            GameObject slotGO = null;

            // Reuse from pool
            if (_slotPool.Count > 0)
            {
                slotGO = _slotPool[_slotPool.Count - 1];
                _slotPool.RemoveAt(_slotPool.Count - 1);
                slotGO.SetActive(true);
            }
            else if (skillSlotPrefab != null)
            {
                slotGO = Instantiate(skillSlotPrefab, slotContainer);
            }
            else
            {
                slotGO = CreateDefaultSlotUI();
            }

            if (slotGO == null) return null;

            slotGO.transform.SetParent(slotContainer, false);

            var slotUI = slotGO.GetComponent<AgentSkillSlotUI>();
            if (slotUI == null)
                slotUI = slotGO.AddComponent<AgentSkillSlotUI>();

            return slotUI;
        }

        /// <summary>
        /// Creates a skill slot UI programmatically when no prefab is assigned.
        /// </summary>
        private GameObject CreateDefaultSlotUI()
        {
            // Root slot object
            var slotGO = new GameObject("SkillSlot", typeof(RectTransform));
            var slotRect = slotGO.GetComponent<RectTransform>();
            slotRect.sizeDelta = slotSize;

            var slotUI = slotGO.AddComponent<AgentSkillSlotUI>();

            // Background / Border
            var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderGO.transform.SetParent(slotGO.transform, false);
            var borderRect = borderGO.GetComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = Vector2.zero;
            var borderImg = borderGO.GetComponent<Image>();
            borderImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            slotUI.borderImage = borderImg;

            // Icon background (slightly inset)
            var iconBgGO = new GameObject("IconBg", typeof(RectTransform), typeof(Image));
            iconBgGO.transform.SetParent(slotGO.transform, false);
            var iconBgRect = iconBgGO.GetComponent<RectTransform>();
            iconBgRect.anchorMin = Vector2.zero;
            iconBgRect.anchorMax = Vector2.one;
            iconBgRect.offsetMin = new Vector2(2, 2);
            iconBgRect.offsetMax = new Vector2(-2, -2);
            var iconBgImg = iconBgGO.GetComponent<Image>();
            iconBgImg.color = new Color(0.08f, 0.08f, 0.12f, 1f);

            // Skill Icon
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(4, 4);
            iconRect.offsetMax = new Vector2(-4, -4);
            var iconImg = iconGO.GetComponent<Image>();
            iconImg.preserveAspect = true;
            slotUI.skillIcon = iconImg;

            // Cooldown overlay (radial fill)
            var cdOverlayGO = new GameObject("CooldownOverlay", typeof(RectTransform), typeof(Image));
            cdOverlayGO.transform.SetParent(slotGO.transform, false);
            var cdOverlayRect = cdOverlayGO.GetComponent<RectTransform>();
            cdOverlayRect.anchorMin = Vector2.zero;
            cdOverlayRect.anchorMax = Vector2.one;
            cdOverlayRect.offsetMin = new Vector2(2, 2);
            cdOverlayRect.offsetMax = new Vector2(-2, -2);
            var cdOverlayImg = cdOverlayGO.GetComponent<Image>();
            cdOverlayImg.color = new Color(0f, 0f, 0f, 0.6f);
            cdOverlayImg.type = Image.Type.Filled;
            cdOverlayImg.fillMethod = Image.FillMethod.Radial360;
            cdOverlayImg.fillOrigin = (int)Image.Origin360.Top;
            cdOverlayImg.fillClockwise = false;
            cdOverlayImg.fillAmount = 0f;
            // Assign a white sprite so the Filled type renders the radial sweep correctly
            if (_whiteSprite == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (int p = 0; p < 16; p++) pixels[p] = Color.white;
                tex.SetPixels(pixels);
                tex.Apply(false, true);
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            cdOverlayImg.sprite = _whiteSprite;
            cdOverlayGO.SetActive(false);
            slotUI.cooldownOverlay = cdOverlayImg;

            // Cooldown text
            var cdTextGO = new GameObject("CooldownText", typeof(RectTransform), typeof(UITextComponent));
            cdTextGO.transform.SetParent(slotGO.transform, false);
            var cdTextRect = cdTextGO.GetComponent<RectTransform>();
            cdTextRect.anchorMin = Vector2.zero;
            cdTextRect.anchorMax = Vector2.one;
            cdTextRect.offsetMin = Vector2.zero;
            cdTextRect.offsetMax = Vector2.zero;
            var cdText = cdTextGO.GetComponent<UITextComponent>();
            cdText.text = "";
            cdText.fontSize = 18;
            cdText.color = Color.white;
#if TMP_PRESENT
            cdText.alignment = TMPro.TextAlignmentOptions.Center;
            cdText.fontStyle = TMPro.FontStyles.Bold;
            cdText.enableAutoSizing = false;
#else
            cdText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cdText.font == null) cdText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            cdText.alignment = TextAnchor.MiddleCenter;
            cdText.fontStyle = FontStyle.Bold;
            cdText.resizeTextForBestFit = false;
#endif
            cdTextGO.SetActive(false);
            slotUI.cooldownText = cdText;

            // Keybind label (top-left corner)
            var keybindGO = new GameObject("KeybindLabel", typeof(RectTransform), typeof(UITextComponent));
            keybindGO.transform.SetParent(slotGO.transform, false);
            var keybindRect = keybindGO.GetComponent<RectTransform>();
            keybindRect.anchorMin = new Vector2(0, 1);
            keybindRect.anchorMax = new Vector2(0, 1);
            keybindRect.pivot = new Vector2(0, 1);
            keybindRect.anchoredPosition = new Vector2(4, -2);
            keybindRect.sizeDelta = new Vector2(24, 16);
            var keybindText = keybindGO.GetComponent<UITextComponent>();
            keybindText.text = "";
            keybindText.fontSize = keybindLabelFontSize;
            keybindText.color = keybindLabelColor;
#if TMP_PRESENT
            keybindText.alignment = TMPro.TextAlignmentOptions.TopLeft;
            keybindText.fontStyle = TMPro.FontStyles.Bold;
            keybindText.enableAutoSizing = false;
            if (keybindLabelFontTMP != null)
                keybindText.font = keybindLabelFontTMP;
#else
            if (keybindLabelFont != null)
                keybindText.font = keybindLabelFont;
            else
            {
                keybindText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (keybindText.font == null) keybindText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            keybindText.alignment = TextAnchor.UpperLeft;
            keybindText.fontStyle = FontStyle.Bold;
            keybindText.resizeTextForBestFit = false;
#endif
            slotUI.keybindLabel = keybindText;

            return slotGO;
        }

        #endregion

        #region Input Handling

        private void EnableInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            if (skillInputBindings != null)
            {
                for (int i = 0; i < skillInputBindings.Count; i++)
                    skillInputBindings[i]?.inputAction?.Enable();
            }
            autoCastToggleAction?.Enable();
#endif
        }

        private void DisableInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            if (skillInputBindings != null)
            {
                for (int i = 0; i < skillInputBindings.Count; i++)
                    skillInputBindings[i]?.inputAction?.Disable();
            }
            autoCastToggleAction?.Disable();
#endif
        }

        private void ProcessInput()
        {
            if (_activeSlots.Count == 0 || _currentEnemyCrowd == null)
                return;

#if ENABLE_INPUT_SYSTEM
            if (skillInputBindings == null) return;

            for (int i = 0; i < skillInputBindings.Count && i < _activeSlots.Count; i++)
            {
                var binding = skillInputBindings[i];
                if (binding?.inputAction != null && binding.inputAction.WasPressedThisFrame())
                {
                    var slot = _activeSlots[i];
                    if (slot?.Skill != null && IsSkillOnCooldownForAllSelected(slot.SlotIndex))
                    {
                        PlaySoundWithPitch(cooldownErrorSound);
                        if (debugMode)
                            Debug.Log($"[AgentSkillBar] Skill {slot.Skill.SkillName} is on cooldown — ignoring keybind.");
                        continue;
                    }
                    PlayClickSound(slot?.Skill);
                    ActivateSkillSlot(slot);
                }
            }
#else
            if (skillInputBindings == null) return;

            for (int i = 0; i < skillInputBindings.Count && i < _activeSlots.Count; i++)
            {
                var binding = skillInputBindings[i];
                if (Input.GetKeyDown(binding.legacyKey))
                {
                    var slot = _activeSlots[i];
                    if (slot?.Skill != null && IsSkillOnCooldownForAllSelected(slot.SlotIndex))
                    {
                        PlaySoundWithPitch(cooldownErrorSound);
                        if (debugMode)
                            Debug.Log($"[AgentSkillBar] Skill {slot.Skill.SkillName} is on cooldown — ignoring keybind.");
                        continue;
                    }
                    PlayClickSound(slot?.Skill);
                    ActivateSkillSlot(slot);
                }
            }
#endif
        }

        /// <summary>
        /// Returns the display text for the keybind at the given binding index.
        /// Extracts the human-readable key letter from the InputAction binding path.
        /// </summary>
        public string GetKeybindDisplayText(int bindingIndex)
        {
            if (skillInputBindings == null || bindingIndex < 0 || bindingIndex >= skillInputBindings.Count)
                return "";

            var binding = skillInputBindings[bindingIndex];
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
            return binding.legacyKey.ToString();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>
        /// Extracts a short display name from an InputSystem binding path.
        /// E.g., "&lt;Keyboard&gt;/q" → "Q", "&lt;Keyboard&gt;/1" → "1"
        /// </summary>
        private static string ExtractKeyDisplayName(string bindingPath)
        {
            if (string.IsNullOrEmpty(bindingPath)) return "";

            // Find the last '/' and take what's after it
            int slashIndex = bindingPath.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < bindingPath.Length - 1)
            {
                string keyPart = bindingPath.Substring(slashIndex + 1);
                // Strip InputSystem #(displayName) wrapper, e.g. "#(A)" → "A"
                if (keyPart.StartsWith("#(") && keyPart.EndsWith(")"))
                    keyPart = keyPart.Substring(2, keyPart.Length - 3);
                // Clean up common InputSystem naming
                keyPart = keyPart.Replace("numpad", "N");
                return keyPart.ToUpperInvariant();
            }

            return bindingPath.ToUpperInvariant();
        }
#endif

        #endregion

        #region Skill Activation & Targeting Mode

        /// <summary>
        /// Called when a skill slot is clicked or its keybind is pressed.
        /// Determines whether to cast immediately or enter targeting mode.
        /// </summary>
        internal void OnSlotClicked(AgentSkillSlotUI slot)
        {
            if (slot?.Skill != null && IsSkillOnCooldownForAllSelected(slot.SlotIndex))
            {
                PlaySoundWithPitch(cooldownErrorSound);
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] Skill {slot.Skill.SkillName} is on cooldown — ignoring click.");
                return;
            }

            PlayClickSound(slot?.Skill);
            ActivateSkillSlot(slot);
        }

        /// <summary>
        /// Returns true if the given pointer event corresponds to the auto-cast toggle input.
        /// Called by AgentSkillSlotUI.OnPointerClick to decide between skill activation and auto-cast toggle.
        /// </summary>
        internal bool IsAutoCastToggleButton(PointerEventData eventData)
        {
#if ENABLE_INPUT_SYSTEM
            // OnPointerClick fires on button RELEASE, so WasPressedThisFrame() is stale.
            // Instead, inspect the binding path to determine which mouse button is configured,
            // then compare against eventData.button.
            if (autoCastToggleAction != null && autoCastToggleAction.bindings.Count > 0)
            {
                string path = autoCastToggleAction.bindings[0].effectivePath;
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.Contains("rightButton"))  return eventData.button == PointerEventData.InputButton.Right;
                    if (path.Contains("leftButton"))   return eventData.button == PointerEventData.InputButton.Left;
                    if (path.Contains("middleButton")) return eventData.button == PointerEventData.InputButton.Middle;
                    // Non-mouse binding (keyboard/gamepad) — check the action state directly.
                    // WasPressedThisFrame works here because keyboard presses and OnPointerClick
                    // (from the primary pointer) can coincide in the same frame.
                    return autoCastToggleAction.WasPressedThisFrame();
                }
            }
            // Fallback: right-click
            return eventData.button == PointerEventData.InputButton.Right;
#else
            // Map legacy KeyCode to PointerEventData button
            switch (autoCastToggleLegacyKey)
            {
                case KeyCode.Mouse0: return eventData.button == PointerEventData.InputButton.Left;
                case KeyCode.Mouse1: return eventData.button == PointerEventData.InputButton.Right;
                case KeyCode.Mouse2: return eventData.button == PointerEventData.InputButton.Middle;
                default:
                    // For keyboard keys, check Input.GetKeyDown (fires same frame as click)
                    return Input.GetKeyDown(autoCastToggleLegacyKey);
            }
#endif
        }
        
        /// <summary>
        /// Called when a skill slot is right-clicked (or auto-cast toggle action is pressed while hovering).
        /// Toggles per-slot auto-cast: switches the slot's control mode between AIControlled and ManualOnly.
        /// Similar to Warcraft 3's right-click auto-cast toggle (e.g. Priest's Inner Fire).
        /// </summary>
        internal void OnSlotRightClicked(AgentSkillSlotUI slot)
        {
            if (slot == null || slot.Skill == null || (_currentEnemyCrowd == null && _currentExternalCaster == null))
                return;

            // External casters: toggle auto-cast on their own skill config
            if (_currentExternalCaster != null)
            {
                if (slot.Skill.Autocast == AutocastEligibility.NotEligible) return;
                var extSlots = _currentExternalCaster.SkillSlots;
                if (extSlots == null || slot.SlotIndex < 0 || slot.SlotIndex >= extSlots.Count) return;
                var extSlot = extSlots[slot.SlotIndex];
                if (extSlot == null) return;
                bool extAutocast = IsSlotAutocastEnabled(extSlot);
                if (extAutocast)
                {
                    PlayAutoCastOffSound(slot.Skill);
                    // Explicit OFF so DefaultOn skills can be disabled.
                    extSlot.overrideControlMode = true;
                    extSlot.controlModeOverride = SkillControlMode.ManualOnly;
                    slot.SetAutocastVisual(false);
                }
                else
                {
                    PlayAutoCastOnSound(slot.Skill);
                    extSlot.overrideControlMode = true;
                    extSlot.controlModeOverride = SkillControlMode.Hybrid;
                    slot.SetAutocastVisual(true);
                }
                return;
            }

            // Skills marked NotEligible cannot be auto-cast — ignore the toggle
            if (slot.Skill.Autocast == AutocastEligibility.NotEligible)
            {
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] Auto-cast toggle ignored for slot {slot.SlotIndex} ({slot.Skill.SkillName}) — skill is not eligible for auto-cast.");
                return;
            }
            
            var skillConfig = _currentEnemyCrowd.skillSystemConfig;
            if (skillConfig?.skillSlots == null || slot.SlotIndex < 0 || slot.SlotIndex >= skillConfig.skillSlots.Count)
                return;
            
            var skillSlot = skillConfig.skillSlots[slot.SlotIndex];
            if (skillSlot == null) return;
            
            // Toggle auto-cast state
            bool currentlyAutocast = IsSlotAutocastEnabled(skillSlot);

            if (currentlyAutocast)
            {
                // Turn OFF auto-cast explicitly so DefaultOn skills can be disabled.
                PlayAutoCastOffSound(slot.Skill);
                skillSlot.overrideControlMode = true;
                skillSlot.controlModeOverride = SkillControlMode.ManualOnly;
                slot.SetAutocastVisual(false);
                
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] Auto-cast DISABLED for slot {slot.SlotIndex} ({slot.Skill.SkillName})");
            }
            else
            {
                // Turn ON auto-cast: override this slot to Hybrid (AI evaluates + player can still trigger manually)
                PlayAutoCastOnSound(slot.Skill);
                skillSlot.overrideControlMode = true;
                skillSlot.controlModeOverride = SkillControlMode.Hybrid;
                slot.SetAutocastVisual(true);
                
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] Auto-cast ENABLED for slot {slot.SlotIndex} ({slot.Skill.SkillName})");
            }
        }

        /// <summary>
        /// Returns true if the skill at the given slot index is on cooldown for ALL
        /// currently selected agents. Used to block activation when no agent can fire.
        /// </summary>
        private bool IsSkillOnCooldownForAllSelected(int slotIndex)
        {
            // External skill caster: single cooldown source
            if (_currentExternalCaster != null)
                return _currentExternalCaster.IsSkillOnCooldown(slotIndex);

            if (crowdController == null || _currentSelectedIndices.Count == 0)
                return false;

            var fsmInstance = FactionSkillManager.Instance;

            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                var agent = crowdController.GetAgent(_currentSelectedIndices[i]);
                if (agent == null || agent.isDead) continue;

                // Check both cooldown sources (combat system + FactionSkillManager)
                float combatRemaining = crowdController.GetSkillCooldownRemaining(agent, slotIndex);
                float fsmRemaining = fsmInstance != null ? fsmInstance.GetCooldownRemaining(agent, slotIndex) : 0f;
                float remaining = Mathf.Max(combatRemaining, fsmRemaining);
                if (remaining <= 0f)
                    return false; // at least one agent is ready
            }

            return true; // every living selected agent has this skill on cooldown
        }

        /// <summary>
        /// Resolves whether a skill slot is currently auto-cast enabled.
        /// Mirrors runtime evaluation in SkillExecutor.
        /// </summary>
        private static bool IsSlotAutocastEnabled(AgentSkillSlot slot)
        {
            if (slot?.skill == null || !slot.enabled)
                return false;

            if (slot.skill.Autocast == AutocastEligibility.NotEligible)
                return false;

            if (slot.overrideControlMode)
                return slot.controlModeOverride != SkillControlMode.ManualOnly;

            return slot.skill.Autocast == AutocastEligibility.DefaultOn;
        }

        /// <summary>
        /// Plays the click sound for the given skill. Uses the per-skill clip if assigned,
        /// otherwise falls back to the global clickSound on this bar.
        /// </summary>
        private void PlayClickSound(AgentSkill skill)
        {
            AudioClip clip = skill != null ? skill.SkillBarClickSound : null;
            if (clip == null) clip = clickSound;
            PlaySoundWithPitch(clip);
        }

        /// <summary>
        /// Plays the auto-cast ON sound for the given skill. Uses the per-skill clip if assigned,
        /// otherwise falls back to the global autoCastOnSound on this bar.
        /// </summary>
        private void PlayAutoCastOnSound(AgentSkill skill)
        {
            AudioClip clip = skill != null ? skill.SkillBarAutoCastOnSound : null;
            if (clip == null) clip = autoCastOnSound;
            PlaySoundWithPitch(clip);
        }

        /// <summary>
        /// Plays the auto-cast OFF sound for the given skill. Uses the per-skill clip if assigned,
        /// otherwise falls back to the global autoCastOffSound on this bar.
        /// </summary>
        private void PlayAutoCastOffSound(AgentSkill skill)
        {
            AudioClip clip = skill != null ? skill.SkillBarAutoCastOffSound : null;
            if (clip == null) clip = autoCastOffSound;
            PlaySoundWithPitch(clip);
        }

        /// <summary>
        /// Plays the confirmation sound for the given skill. Used when the player confirms
        /// a target during targeting mode (right-click / command button). Uses the per-skill
        /// clip if assigned, otherwise falls back to the global confirmSound on this bar.
        /// </summary>
        private void PlayConfirmSound(AgentSkill skill)
        {
            AudioClip clip = skill != null ? skill.SkillBarConfirmSound : null;
            if (clip == null) clip = confirmSound;
            PlaySoundWithPitch(clip);
        }

        /// <summary>
        /// Plays an AudioClip at the camera position with the configured volume, base pitch,
        /// and random pitch variation. Uses a temporary AudioSource so pitch can be applied
        /// (AudioSource.PlayClipAtPoint does not support pitch).
        /// </summary>
        private void PlaySoundWithPitch(AudioClip clip)
        {
            if (clip == null) return;

            Vector3 pos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            float pitch = basePitch + UnityEngine.Random.Range(-pitchVariation, pitchVariation);

            // Create a temporary GameObject with AudioSource so we can set pitch
            var go = new GameObject("SkillBarSound");
            go.transform.position = pos;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = clickSoundVolume;
            source.pitch = pitch;
            source.spatialBlend = 0f; // 2D sound
            source.Play();
            UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(Mathf.Abs(pitch), 0.01f) + 0.1f);
        }

        /// <summary>
        /// Returns the display string for the current minute abbreviation setting.
        /// </summary>
        internal string GetMinuteAbbreviationString()
        {
            return minuteAbbreviation switch
            {
                MinuteAbbreviation.M       => "M",
                MinuteAbbreviation.m       => "m",
                MinuteAbbreviation.Min_dot => "Min.",
                MinuteAbbreviation.min_dot => "min.",
                _                          => "min."
            };
        }

        /// <summary>
        /// Sets the minute abbreviation style at runtime.
        /// </summary>
        public void SetMinuteAbbreviation(MinuteAbbreviation abbreviation)
        {
            minuteAbbreviation = abbreviation;
        }

        private void ActivateSkillSlot(AgentSkillSlotUI slot)
        {
            if (slot == null || slot.Skill == null)
            {
                if (debugMode) Debug.Log("[AgentSkillBar] ActivateSkillSlot — slot or skill is null, aborting.");
                return;
            }
            if (_currentSelectedIndices.Count == 0 && _currentExternalCaster == null)
            {
                if (debugMode) Debug.Log("[AgentSkillBar] ActivateSkillSlot — no agents or external caster selected, aborting.");
                return;
            }

            if (debugMode)
                Debug.Log($"[AgentSkillBar] ActivateSkillSlot — skill={slot.Skill.SkillName}, slotIndex={slot.SlotIndex}, " +
                    $"targeting={slot.Skill.TargetingType}, selectedCount={_currentSelectedIndices.Count}, external={_currentExternalCaster != null}");

            // If we're already in targeting mode and click the same slot, cancel
            if (_targetingModeActive && _pendingTargetSlot == slot)
            {
                if (debugMode) Debug.Log("[AgentSkillBar] Same slot clicked again — cancelling targeting mode.");
                CancelTargetingMode();
                return;
            }

            // If we're already in targeting mode and click a different slot, switch
            if (_targetingModeActive)
            {
                if (debugMode) Debug.Log("[AgentSkillBar] Different slot clicked — switching targeting mode.");
                CancelTargetingMode();
            }

            // Determine if the skill needs manual target selection or can be cast immediately
            if (RequiresManualTargeting(slot.Skill))
            {
                if (debugMode) Debug.Log($"[AgentSkillBar] Skill requires manual targeting — entering targeting mode.");
                EnterTargetingMode(slot);
            }
            else
            {
                // Self-cast and auto-target skills: execute immediately
                if (debugMode) Debug.Log($"[AgentSkillBar] Skill does NOT require manual targeting — executing immediately.");
                ExecuteSkillOnSelection(slot.SlotIndex, -1, null);
                onSkillActivated?.Invoke(slot.SlotIndex);
            }
        }

        /// <summary>
        /// Returns true if the skill requires the player to manually select a target.
        /// Self-cast, auto-target, and area-around-self skills cast immediately.
        /// </summary>
        private static bool RequiresManualTargeting(AgentSkill skill)
        {
            switch (skill.TargetingType)
            {
                case SkillTargetingType.Self:
                case SkillTargetingType.AOEAroundSelf:
                case SkillTargetingType.ClosestEnemy:
                case SkillTargetingType.LowestHealthAlly:
                case SkillTargetingType.AllAlliesInRange:
                case SkillTargetingType.AllEnemiesInRange:
                case SkillTargetingType.AllCrowdMembers:
                case SkillTargetingType.AllFactionMembers:
                    return false;

                case SkillTargetingType.SingleEnemy:
                case SkillTargetingType.SingleAlly:
                case SkillTargetingType.AOEAroundEnemy:
                case SkillTargetingType.AOEAroundAlly:
                case SkillTargetingType.Cone:
                case SkillTargetingType.Line:
                case SkillTargetingType.Chain:
                case SkillTargetingType.Projectile:
                case SkillTargetingType.Hitscan:
                case SkillTargetingType.GroundTargetedAOE:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Enters targeting mode: cursor changes, active slot highlighted, waiting for target click.
        /// </summary>
        private void EnterTargetingMode(AgentSkillSlotUI slot)
        {
            _targetingModeActive = true;
            _pendingTargetSlot = slot;

            // Suppress RTS command processing while targeting
            if (rtsController != null)
                rtsController.suppressCommandInput = true;

            // Highlight the active slot
            slot.SetTargetingHighlight(true, activeTargetingBorderColor);

            // Change cursor — try runtime RGBA32 copy first, fall back to raw asset texture
            if (targetingCursorTexture != null)
            {
                EnsureRuntimeCursorTexture();
                if (_runtimeCursorTexture != null)
                {
                    // Use ForceSoftware — hardware cursors (CursorMode.Auto) fail silently on Linux
                    Cursor.SetCursor(_runtimeCursorTexture, targetingCursorHotspot, CursorMode.ForceSoftware);
                }
                else
                {
                    // Fallback: use raw asset texture with ForceSoftware to bypass format restrictions
                    Cursor.SetCursor(targetingCursorTexture, targetingCursorHotspot, CursorMode.ForceSoftware);
                }
                _cursorOverrideActive = true;
                Debug.Log($"[AgentSkillBar] EnterTargetingMode — cursor set (runtime={_runtimeCursorTexture != null}, " +
                    $"size={_runtimeCursorTexture?.width ?? 0}x{_runtimeCursorTexture?.height ?? 0}, " +
                    $"hotspot={targetingCursorHotspot}, readable={targetingCursorTexture.isReadable})");
            }
            else
            {
                Debug.LogWarning("[AgentSkillBar] EnterTargetingMode — targetingCursorTexture is NULL, cursor will not change!");
            }

            if (debugMode)
                Debug.Log($"[AgentSkillBar] EnterTargetingMode — skill={slot.Skill.SkillName}, slotIndex={slot.SlotIndex}, " +
                    $"RTS command input suppressed={rtsController != null}");

            // Show max-range indicator for manual-targeted skills.
            if (ShouldShowMaxRangeIndicator(slot.Skill))
                ShowMaxRangeIndicator(slot.Skill);

            // Show ground indicator for GroundTargetedAOE skills.
            if (slot.Skill.TargetingType == SkillTargetingType.GroundTargetedAOE)
                ShowGroundIndicator(slot.Skill.AOERadius);
        }

        /// <summary>
        /// Cancels targeting mode: restores cursor, clears slot highlight.
        /// </summary>
        public void CancelTargetingMode()
        {
            if (!_targetingModeActive) return;

            if (debugMode) Debug.Log("[AgentSkillBar] CancelTargetingMode — restoring cursor and slot highlight.");

            _targetingModeActive = false;

            // Keep RTS command input suppressed until the command button is fully
            // released. This prevents the held-button state from leaking into the
            // RTS controller’s formation adjustment logic with stale drag-start data.
            if (rtsController != null)
                _suppressUntilCommandRelease = true;

            // Restore slot highlight
            if (_pendingTargetSlot != null)
            {
                _pendingTargetSlot.SetTargetingHighlight(false, Color.clear);
                _pendingTargetSlot = null;
            }

            // Restore cursor
            if (_cursorOverrideActive)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                _cursorOverrideActive = false;
            }

            // Hide ground indicator
            HideGroundIndicator();
            HideMaxRangeIndicator();
        }

        #region Targeting Indicators

        /// <summary>
        /// Creates and shows the ground target indicator at the cursor world position.
        /// </summary>
        private void ShowGroundIndicator(float radius)
        {
            HideGroundIndicator(); // Clean up any previous indicator

            switch (groundIndicatorType)
            {
                case GroundTargetIndicatorType.RuntimeCircle:
                    CreateGroundRuntimeCircleIndicator(radius);
                    break;

                case GroundTargetIndicatorType.CustomPrefab:
                    if (groundIndicatorPrefab != null)
                    {
                        _groundIndicatorInstance = Instantiate(groundIndicatorPrefab);
                        // Scale uniformly so a 1-unit-radius prefab matches the AOE radius
                        _groundIndicatorInstance.transform.localScale = Vector3.one * radius;
                        ConfigureIndicatorForFogVisibility(_groundIndicatorInstance);
                    }
                    break;

                case GroundTargetIndicatorType.None:
                default:
                    break;
            }
        }

        /// <summary>
        /// Creates and shows the max-range indicator centered on the current selection.
        /// </summary>
        private void ShowMaxRangeIndicator(AgentSkill skill)
        {
            HideMaxRangeIndicator(); // Clean up any previous indicator
            if (skill == null) return;

            float radius = skill.MaxRange;
            if (radius <= 0f) return;

            _maxRangeIndicatorRadius = radius;
            _maxRangeIndicatorUsesConeShape = skill.TargetingType == SkillTargetingType.Cone;
            _maxRangeIndicatorUsesLineShape = skill.TargetingType == SkillTargetingType.Line;
            _maxRangeIndicatorConeAngle = skill.ConeAngle;
            _maxRangeIndicatorSkillLineWidth = skill.LineWidth;

            switch (maxRangeIndicatorType)
            {
                case GroundTargetIndicatorType.RuntimeCircle:
                    CreateMaxRangeRuntimeIndicator(radius);
                    break;

                case GroundTargetIndicatorType.CustomPrefab:
                    if (maxRangeIndicatorPrefab != null)
                    {
                        _maxRangeIndicatorInstance = Instantiate(maxRangeIndicatorPrefab);
                        _maxRangeIndicatorInstance.transform.localScale = Vector3.one * radius;
                        ConfigureIndicatorForFogVisibility(_maxRangeIndicatorInstance);
                        SetMaxRangeIndicatorColor(maxRangeIndicatorInRangeColor);
                    }
                    break;

                case GroundTargetIndicatorType.None:
                default:
                    break;
            }
        }

        private bool ShouldShowMaxRangeIndicator(AgentSkill skill)
        {
            if (!showMaxRangeIndicator || skill == null) return false;
            if (skill.MaxRange <= 0f) return false;
            if (skill.TargetingType == SkillTargetingType.GroundTargetedAOE && !showMaxRangeForGroundTargetedAOE)
                return false;
            return true;
        }

        /// <summary>
        /// Destroys the active ground target indicator.
        /// </summary>
        private void HideGroundIndicator()
        {
            if (_groundIndicatorInstance != null)
            {
                Destroy(_groundIndicatorInstance);
                _groundIndicatorInstance = null;
                _groundIndicatorLine = null;
            }

            if (_groundIndicatorMaterial != null)
            {
                Destroy(_groundIndicatorMaterial);
                _groundIndicatorMaterial = null;
            }
        }

        /// <summary>
        /// Destroys the active max-range indicator.
        /// </summary>
        private void HideMaxRangeIndicator()
        {
            if (_maxRangeIndicatorInstance != null)
            {
                Destroy(_maxRangeIndicatorInstance);
                _maxRangeIndicatorInstance = null;
                _maxRangeIndicatorLine = null;
            }

            if (_maxRangeIndicatorMaterial != null)
            {
                Destroy(_maxRangeIndicatorMaterial);
                _maxRangeIndicatorMaterial = null;
            }

            _maxRangeIndicatorRadius = 0f;
            _maxRangeIndicatorUsesConeShape = false;
            _maxRangeIndicatorUsesLineShape = false;
            _maxRangeIndicatorConeAngle = 0f;
            _maxRangeIndicatorSkillLineWidth = 0f;
        }

        /// <summary>
        /// Creates a runtime LineRenderer circle for ground-targeted AOE preview.
        /// </summary>
        private void CreateGroundRuntimeCircleIndicator(float radius)
        {
            _groundIndicatorInstance = new GameObject("GroundTargetIndicator");
            _groundIndicatorLine = _groundIndicatorInstance.AddComponent<LineRenderer>();

            _groundIndicatorMaterial = new Material(Shader.Find("Sprites/Default"));
            _groundIndicatorLine.material = _groundIndicatorMaterial;

            _groundIndicatorLine.useWorldSpace = true;
            _groundIndicatorLine.loop = true;
            _groundIndicatorLine.startWidth = groundIndicatorLineWidth;
            _groundIndicatorLine.endWidth = groundIndicatorLineWidth;
            _groundIndicatorLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _groundIndicatorLine.receiveShadows = false;
            _groundIndicatorLine.alignment = LineAlignment.TransformZ;
            _groundIndicatorLine.positionCount = Mathf.Max(16, groundIndicatorSegments);

            ConfigureIndicatorForFogVisibility(_groundIndicatorInstance);
            SetGroundIndicatorColor(groundIndicatorInRangeColor);
        }

        /// <summary>
        /// Creates a runtime LineRenderer circle for manual max-range preview.
        /// </summary>
        private void CreateMaxRangeRuntimeIndicator(float radius)
        {
            _maxRangeIndicatorInstance = new GameObject("MaxRangeTargetIndicator");
            _maxRangeIndicatorLine = _maxRangeIndicatorInstance.AddComponent<LineRenderer>();

            _maxRangeIndicatorMaterial = new Material(Shader.Find("Sprites/Default"));
            _maxRangeIndicatorLine.material = _maxRangeIndicatorMaterial;

            _maxRangeIndicatorLine.useWorldSpace = true;
            _maxRangeIndicatorLine.loop = !(_maxRangeIndicatorUsesConeShape || _maxRangeIndicatorUsesLineShape);

            float runtimeWidth = maxRangeIndicatorLineWidth;
            if (_maxRangeIndicatorUsesLineShape)
            {
                // Line-targeted skills visualize their actual hit width.
                runtimeWidth = _maxRangeIndicatorSkillLineWidth > 0f
                    ? _maxRangeIndicatorSkillLineWidth
                    : maxRangeIndicatorLineWidth;
            }
            runtimeWidth = Mathf.Max(0.01f, runtimeWidth);
            _maxRangeIndicatorLine.startWidth = runtimeWidth;
            _maxRangeIndicatorLine.endWidth = runtimeWidth;
            _maxRangeIndicatorLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _maxRangeIndicatorLine.receiveShadows = false;
            // Line-target indicators should billboard to camera so thickness remains readable
            // from pitched/isometric RTS camera angles (e.g. 45 degrees).
            _maxRangeIndicatorLine.alignment = _maxRangeIndicatorUsesLineShape
                ? LineAlignment.View
                : LineAlignment.TransformZ;

            if (_maxRangeIndicatorUsesConeShape)
            {
                // center + arc points + center
                int arcPointCount = Mathf.Max(8, maxRangeIndicatorSegments / 2);
                _maxRangeIndicatorLine.positionCount = arcPointCount + 2;
            }
            else if (_maxRangeIndicatorUsesLineShape)
            {
                _maxRangeIndicatorLine.positionCount = 2;
            }
            else
            {
                _maxRangeIndicatorLine.positionCount = Mathf.Max(16, maxRangeIndicatorSegments);
            }

            ConfigureIndicatorForFogVisibility(_maxRangeIndicatorInstance);
            SetMaxRangeIndicatorColor(maxRangeIndicatorInRangeColor);
            if (debugMode)
                Debug.Log($"[AgentSkillBar] Showing max-range indicator (radius={radius:F2}, cone={_maxRangeIndicatorUsesConeShape}, line={_maxRangeIndicatorUsesLineShape})");
        }

        /// <summary>
        /// Updates a runtime circle (LineRenderer) by writing points around a center in world space.
        /// </summary>
        private static void UpdateRuntimeCirclePoints(LineRenderer line, Vector3 center, float radius)
        {
            if (line == null) return;

            int segments = line.positionCount;
            if (segments <= 0) return;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius));
            }
        }

        /// <summary>
        /// Updates a LineRenderer as a cone sector outline: center -> arc -> center.
        /// </summary>
        private static void UpdateRuntimeConePoints(LineRenderer line, Vector3 center, float radius, float coneAngleDeg, Vector3 forward)
        {
            if (line == null) return;

            int arcPointCount = Mathf.Max(2, line.positionCount - 2);
            float halfAngle = Mathf.Clamp(coneAngleDeg * 0.5f, 0f, 180f);

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            line.SetPosition(0, center);
            for (int i = 0; i < arcPointCount; i++)
            {
                float t = arcPointCount == 1 ? 0.5f : i / (float)(arcPointCount - 1);
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                line.SetPosition(i + 1, center + dir * radius);
            }
            line.SetPosition(arcPointCount + 1, center);
        }

        /// <summary>
        /// Updates a LineRenderer as a directional line segment.
        /// Width is controlled by the LineRenderer start/end width.
        /// </summary>
        private static void UpdateRuntimeLinePoints(LineRenderer line, Vector3 start, Vector3 direction, float length)
        {
            if (line == null || line.positionCount < 2)
                return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector3.forward;
            else
                direction.Normalize();

            line.SetPosition(0, start);
            line.SetPosition(1, start + direction * Mathf.Max(0f, length));
        }

        /// <summary>
        /// Updates the ground indicator position and color each frame during targeting.
        /// Called from ProcessTargetingMode before input checks.
        /// </summary>
        private void UpdateGroundIndicator()
        {
            if (_groundIndicatorInstance == null) return;
            if (_pendingTargetSlot == null) return;

            if (!TryGetCursorGroundPoint(out Vector3 groundPoint))
            {
                // No valid ground under cursor: show invalid color feedback.
                SetGroundIndicatorColor(groundIndicatorOutOfRangeColor);
                return;
            }

            groundPoint.y += groundIndicatorYOffset;

            // Position the indicator at the ground point
            _groundIndicatorInstance.transform.position = groundPoint;

            // Reposition circle vertices around the new center (world space)
            if (_groundIndicatorLine != null)
            {
                float radius = _pendingTargetSlot.Skill.AOERadius;
                UpdateRuntimeCirclePoints(_groundIndicatorLine, groundPoint, radius);
            }

            // Check range — green if at least one selected caster is in range, red otherwise
            bool inRange = IsGroundTargetInRange(groundPoint, _pendingTargetSlot.Skill.MinRange, _pendingTargetSlot.Skill.MaxRange);
            SetGroundIndicatorColor(inRange ? groundIndicatorInRangeColor : groundIndicatorOutOfRangeColor);
        }

        /// <summary>
        /// Updates the max-range indicator position and color each frame during targeting.
        /// </summary>
        private void UpdateMaxRangeIndicator()
        {
            if (_maxRangeIndicatorInstance == null) return;
            if (_pendingTargetSlot == null) return;

            Vector3? centerOpt = GetCurrentTargetingCenter();
            if (!centerOpt.HasValue) return;

            Vector3 center = centerOpt.Value;
            center.y += maxRangeIndicatorYOffset;
            _maxRangeIndicatorInstance.transform.position = center;

            bool inRange = false;
            bool hasCursorGround = TryGetCursorGroundPoint(out Vector3 cursorGroundPoint);
            if (hasCursorGround)
                inRange = IsGroundTargetInRange(cursorGroundPoint, _pendingTargetSlot.Skill.MinRange, _pendingTargetSlot.Skill.MaxRange);

            if (_maxRangeIndicatorLine != null)
            {
                Vector3 forward = Vector3.forward;
                if (hasCursorGround)
                {
                    forward = cursorGroundPoint - centerOpt.Value;
                }
                else if (_camera != null)
                {
                    forward = _camera.transform.forward;
                }

                if (_maxRangeIndicatorUsesConeShape)
                {
                    UpdateRuntimeConePoints(
                        _maxRangeIndicatorLine,
                        center,
                        _maxRangeIndicatorRadius,
                        _maxRangeIndicatorConeAngle,
                        forward);
                }
                else if (_maxRangeIndicatorUsesLineShape)
                {
                    float length = _maxRangeIndicatorRadius;
                    if (hasCursorGround)
                    {
                        Vector3 flat = cursorGroundPoint - centerOpt.Value;
                        flat.y = 0f;
                        length = Mathf.Min(flat.magnitude, _maxRangeIndicatorRadius);
                    }

                    UpdateRuntimeLinePoints(
                        _maxRangeIndicatorLine,
                        center,
                        forward,
                        length);
                }
                else
                {
                    UpdateRuntimeCirclePoints(_maxRangeIndicatorLine, center, _maxRangeIndicatorRadius);
                }
            }

            SetMaxRangeIndicatorColor(inRange ? maxRangeIndicatorInRangeColor : maxRangeIndicatorOutOfRangeColor);
        }

        /// <summary>
        /// Returns true if the cursor currently points at valid ground and outputs the hit point.
        /// </summary>
        private bool TryGetCursorGroundPoint(out Vector3 groundPoint)
        {
            groundPoint = Vector3.zero;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return false;

            Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
            mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            mousePos = Input.mousePosition;
#endif

            Ray ray = _camera.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, groundRaycastMask))
                return false;

            groundPoint = hit.point;
            return true;
        }

        /// <summary>
        /// Computes the center point used for max-range indicator placement.
        /// Uses external caster transform when available, otherwise selected crowd agents.
        /// </summary>
        private Vector3? GetCurrentTargetingCenter()
        {
            if (_currentExternalCaster != null && _currentExternalCaster.Transform != null)
                return _currentExternalCaster.Transform.position;

            if (crowdController == null || _currentSelectedIndices.Count == 0)
                return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                var agent = crowdController.GetAgent(_currentSelectedIndices[i]);
                if (agent == null || agent.isDead) continue;

                sum += agent.WorldPosition;
                count++;
            }

            if (count == 0) return null;
            return sum / count;
        }

        /// <summary>
        /// Returns true if the ground target point is within the skill's max range
        /// of at least one selected caster.
        /// </summary>
        private bool IsGroundTargetInRange(Vector3 groundPoint, float minRange, float maxRange)
        {
            bool hasMaxRange = maxRange > 0f;
            bool hasMinRange = minRange > 0f;
            float maxRangeSqr = maxRange * maxRange;
            float minRangeSqr = minRange * minRange;

            if (crowdController != null)
            {
                for (int i = 0; i < _currentSelectedIndices.Count; i++)
                {
                    var agent = crowdController.GetAgent(_currentSelectedIndices[i]);
                    if (agent == null || agent.isDead) continue;

                    float dx = agent.WorldPosition.x - groundPoint.x;
                    float dz = agent.WorldPosition.z - groundPoint.z;
                    float distSqr = dx * dx + dz * dz;
                    bool inMax = !hasMaxRange || distSqr <= maxRangeSqr;
                    bool inMin = !hasMinRange || distSqr >= minRangeSqr;
                    if (inMax && inMin)
                        return true;
                }
            }

            if (_currentExternalCaster != null && _currentExternalCaster.Transform != null)
            {
                Vector3 casterPos = _currentExternalCaster.Transform.position;
                float dx = casterPos.x - groundPoint.x;
                float dz = casterPos.z - groundPoint.z;
                float distSqr = dx * dx + dz * dz;
                bool inMax = !hasMaxRange || distSqr <= maxRangeSqr;
                bool inMin = !hasMinRange || distSqr >= minRangeSqr;
                if (inMax && inMin)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sets line/prefab color on a targeting indicator instance.
        /// </summary>
        private static void SetIndicatorColor(GameObject indicatorInstance, LineRenderer line, Material lineMaterial, Color color)
        {
            if (line != null)
            {
                line.startColor = color;
                line.endColor = color;
            }

            if (lineMaterial != null)
                ApplyColorToMaterial(lineMaterial, color);

            if (line != null || indicatorInstance == null)
                return;

            // For custom prefabs, try to set color on all renderers
            var renderers = indicatorInstance.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.materials)
                    ApplyColorToMaterial(mat, color);
            }

            // Also apply to UI Graphics for UI-based indicator prefabs
            var graphics = indicatorInstance.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var graphic in graphics)
                graphic.color = color;
        }

        /// <summary>
        /// Applies a color to common material color properties across different shaders.
        /// </summary>
        private static void ApplyColorToMaterial(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", color);
        }

        /// <summary>
        /// Sets the color of the ground indicator (LineRenderer or custom prefab renderer).
        /// </summary>
        private void SetGroundIndicatorColor(Color color)
        {
            SetIndicatorColor(_groundIndicatorInstance, _groundIndicatorLine, _groundIndicatorMaterial, color);
        }

        /// <summary>
        /// Sets the color of the max-range indicator (LineRenderer or custom prefab renderer).
        /// </summary>
        private void SetMaxRangeIndicatorColor(Color color)
        {
            SetIndicatorColor(_maxRangeIndicatorInstance, _maxRangeIndicatorLine, _maxRangeIndicatorMaterial, color);
        }

        /// <summary>
        /// Applies the RTS controller indicator render settings (render-above-fog, sorting order) to skill indicators.
        /// </summary>
        private void ConfigureIndicatorForFogVisibility(GameObject indicator)
        {
            if (indicator == null || rtsController == null) return;
            if (!rtsController.renderAboveFog) return;

            foreach (var renderer in indicator.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in renderer.materials)
                {
                    mat.renderQueue = 4000;
                    mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    mat.SetInt("_ZWrite", 0);
                }

                if (renderer is SpriteRenderer spriteRenderer)
                    spriteRenderer.sortingOrder = rtsController.indicatorSortingOrder;
            }

            var canvas = indicator.GetComponent<Canvas>();
            if (canvas != null)
                canvas.sortingOrder = rtsController.indicatorSortingOrder;

            foreach (var childCanvas in indicator.GetComponentsInChildren<Canvas>(true))
                childCanvas.sortingOrder = rtsController.indicatorSortingOrder;
        }

        #endregion

        /// <summary>
        /// Returns true if the skill bar is currently in targeting mode (waiting for a click on a target).
        /// </summary>
        public bool IsInTargetingMode => _targetingModeActive;

        /// <summary>
        /// The skill that is currently pending target selection, or null if not in targeting mode.
        /// </summary>
        public AgentSkill PendingTargetSkill => _pendingTargetSlot?.Skill;

        /// <summary>
        /// Processes targeting mode input each frame.
        /// Waits for the command button (right-click) to pick a target, Escape or left-click on empty to cancel.
        /// </summary>
        private void ProcessTargetingMode()
        {
            if (!_targetingModeActive || _pendingTargetSlot == null) return;

            // Update ground indicator position and range coloring every frame
            UpdateGroundIndicator();
            UpdateMaxRangeIndicator();

            // Cancel on Escape
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelTargetingMode();
                return;
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelTargetingMode();
                return;
            }
#endif

            // Detect command button (right-click) for target confirmation
            bool commandPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                commandPressed = true;
#else
            if (Input.GetMouseButtonDown(1))
                commandPressed = true;
#endif

            if (!commandPressed) return;

            if (debugMode) Debug.Log("[AgentSkillBar] ProcessTargetingMode \u2014 command button pressed, raycasting for target...");

            // Raycast for target
            if (_camera == null) _camera = Camera.main;
            if (_camera == null)
            {
                if (debugMode) Debug.LogWarning("[AgentSkillBar] ProcessTargetingMode — Camera.main is null!");
                return;
            }

            Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
            mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            mousePos = Input.mousePosition;
#endif

            Ray ray = _camera.ScreenPointToRay(mousePos);
            var skill = _pendingTargetSlot.Skill;
            int slotIndex = _pendingTargetSlot.SlotIndex;

            // Ground-targeted AOE: use ground hit point
            if (skill.TargetingType == SkillTargetingType.GroundTargetedAOE)
            {
                if (Physics.Raycast(ray, out RaycastHit groundHit, 1000f, groundRaycastMask))
                {
                    if (debugMode) Debug.Log($"[AgentSkillBar] Ground-targeted AOE \u2014 hit point={groundHit.point}");
                    ExecuteSkillOnSelection(slotIndex, -1, groundHit.point);
                    CommandAgentsToMoveInRangeOfGround(groundHit.point, skill.MaxRange);
                    PlayConfirmSound(skill);
                    onSkillActivated?.Invoke(slotIndex);
                    CancelTargetingMode();
                }
                else
                {
                    if (debugMode) Debug.Log($"[AgentSkillBar] Ground-targeted AOE \u2014 raycast missed (groundRaycastMask={groundRaycastMask.value})");
                }
                return;
            }

            // Agent-targeted skills: find target agent
            int targetAgentIndex = RaycastForTargetAgent(ray, skill);

            if (targetAgentIndex >= 0)
            {
                // Valid target found — queue skill and command agents toward target
                if (debugMode) Debug.Log($"[AgentSkillBar] Target found — agentIndex={targetAgentIndex}, queuing skill.");
                ExecuteSkillOnSelection(slotIndex, targetAgentIndex, null);
                CommandAgentsToSkillTarget(targetAgentIndex, skill);
                PlayConfirmSound(skill);
                onSkillActivated?.Invoke(slotIndex);
                CancelTargetingMode();
            }
            else
            {
                // No agent found — try IEnemyMassesDamageable on the hit object
                IEnemyMassesDamageable damageable = null;
                Vector3 damageablePos = Vector3.zero;

                if (Physics.Raycast(ray, out RaycastHit dmgHit, 1000f, targetRaycastMask))
                {
                    damageable = dmgHit.transform.GetComponentInParent<IEnemyMassesDamageable>();
                    if (damageable != null)
                        damageablePos = dmgHit.point;
                }

                if (damageable != null && damageable.IsAlive)
                {
                    if (debugMode) Debug.Log($"[AgentSkillBar] Damageable target found — {damageable.DamageTransform.name}");
                    ExecuteSkillOnSelection(slotIndex, -1, (Vector3?)damageablePos, damageable);
                    CommandAgentsToMoveToDamageable(damageable, skill);
                    PlayConfirmSound(skill);
                    onSkillActivated?.Invoke(slotIndex);
                    CancelTargetingMode();
                }
                else
                {
                    // Clicked on empty ground while in agent-targeting mode — cancel
                    if (debugMode) Debug.Log("[AgentSkillBar] No valid target found under cursor — cancelling targeting mode.");
                    CancelTargetingMode();
                }
            }
        }

        /// <summary>
        /// Raycasts to find a target agent under the cursor, based on the skill's targeting type.
        /// Mirrors the RTS Controller's TryPerformAttackCommand logic.
        /// </summary>
        private int RaycastForTargetAgent(Ray ray, AgentSkill skill)
        {
            if (crowdController == null)
            {
                if (debugMode) Debug.LogWarning("[AgentSkillBar] RaycastForTargetAgent — crowdController is null!");
                return -1;
            }

            int targetAgentIndex = -1;

            // Direct raycast against agent colliders
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, targetRaycastMask))
            {
                targetAgentIndex = FindAgentIndexFromTransform(hit.transform);
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] RaycastForTargetAgent — direct hit on '{hit.transform.name}', " +
                        $"agentIndex={targetAgentIndex}, targetRaycastMask={targetRaycastMask.value}");
            }
            else
            {
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] RaycastForTargetAgent — direct raycast missed (targetRaycastMask={targetRaycastMask.value})");
            }

            // Fallback: ground raycast + nearest agent to hit point
            if (targetAgentIndex < 0)
            {
                if (Physics.Raycast(ray, out RaycastHit groundHit, 1000f, groundRaycastMask))
                {
                    targetAgentIndex = FindNearestValidAgentToPoint(groundHit.point, targetClickRadius, skill);
                    if (debugMode)
                        Debug.Log($"[AgentSkillBar] RaycastForTargetAgent — ground fallback hit={groundHit.point}, " +
                            $"nearestAgent={targetAgentIndex}, clickRadius={targetClickRadius}");
                }
                else
                {
                    if (debugMode)
                        Debug.Log($"[AgentSkillBar] RaycastForTargetAgent — ground raycast also missed (groundRaycastMask={groundRaycastMask.value})");
                }
            }

            // Validate the target suits the skill's targeting type
            if (targetAgentIndex >= 0 && !IsValidTargetForSkill(targetAgentIndex, skill))
            {
                if (debugMode)
                    Debug.Log($"[AgentSkillBar] RaycastForTargetAgent — target {targetAgentIndex} failed validation for {skill.TargetingType}");
                targetAgentIndex = -1;
            }

            return targetAgentIndex;
        }

        /// <summary>
        /// Walks up the transform hierarchy to match a raycast hit to an agent index.
        /// Mirrors EnemyMassesRTSController.GetAgentIndexFromHit().
        /// </summary>
        private int FindAgentIndexFromTransform(Transform hitTransform)
        {
            if (crowdController == null) return -1;

            var agents = crowdController.GetAgents();
            Transform current = hitTransform;

            while (current != null)
            {
                GameObject currentGO = current.gameObject;

                for (int i = 0; i < agents.Count; i++)
                {
                    var agent = agents[i];
                    if (agent == null) continue;

                    if (agent.crowdInstance != null && agent.crowdInstance.gameObject == currentGO)
                        return i;

                    if (agent.navigationAgent != null)
                    {
                        var underlying = agent.navigationAgent.GetUnderlyingAgent<Component>();
                        if (underlying != null && underlying.gameObject == currentGO)
                            return i;
                    }
                }

                current = current.parent;
            }

            return -1;
        }

        /// <summary>
        /// Finds the nearest agent to a world point that is a valid target for the skill.
        /// </summary>
        private int FindNearestValidAgentToPoint(Vector3 point, float maxDistance, AgentSkill skill)
        {
            if (crowdController == null) return -1;

            var agents = crowdController.GetAgents();
            int nearestIndex = -1;
            float nearestDistSq = maxDistance * maxDistance;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead) continue;

                Vector3 agentPos = agent.WorldPosition;
                float dx = agentPos.x - point.x;
                float dz = agentPos.z - point.z;
                float distSq = dx * dx + dz * dz;

                if (distSq < nearestDistSq && IsValidTargetForSkill(i, skill))
                {
                    nearestDistSq = distSq;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        /// <summary>
        /// Determines if the agent at the given index is a valid target for the skill's targeting type.
        /// Enemy-targeting skills require a non-player-controlled, different-faction agent.
        /// Ally-targeting skills require a same-faction agent.
        /// </summary>
        private bool IsValidTargetForSkill(int targetIndex, AgentSkill skill)
        {
            if (crowdController == null || rtsController == null) return false;

            var targetAgent = crowdController.GetAgent(targetIndex);
            if (targetAgent == null || targetAgent.isDead) return false;

            bool needsEnemy = IsEnemyTargetingSkill(skill);
            bool needsAlly = IsAllyTargetingSkill(skill);

            if (needsEnemy)
            {
                // Must not be player-controlled and must be a different faction
                if (rtsController.IsAgentPlayerControlled(targetIndex)) return false;

                if (_currentEnemyCrowd != null && targetAgent.enemyCrowd != null)
                {
                    if (targetAgent.enemyCrowd.factionName == _currentEnemyCrowd.factionName)
                        return false;
                }
                return true;
            }

            if (needsAlly)
            {
                // Must be same faction
                if (_currentEnemyCrowd != null && targetAgent.enemyCrowd != null)
                {
                    return targetAgent.enemyCrowd.factionName == _currentEnemyCrowd.factionName;
                }
                return false;
            }

            // For other types (Cone, Line, etc.) — accept any non-dead agent
            return true;
        }

        private static bool IsEnemyTargetingSkill(AgentSkill skill)
        {
            switch (skill.TargetingType)
            {
                case SkillTargetingType.SingleEnemy:
                case SkillTargetingType.AOEAroundEnemy:
                case SkillTargetingType.Chain:
                case SkillTargetingType.Projectile:
                case SkillTargetingType.Hitscan:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAllyTargetingSkill(AgentSkill skill)
        {
            switch (skill.TargetingType)
            {
                case SkillTargetingType.SingleAlly:
                case SkillTargetingType.AOEAroundAlly:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Queues the pending skill on all selected agents, optionally with a target.
        /// </summary>
        private void ExecuteSkillOnSelection(int slotIndex, int targetAgentIndex, Vector3? groundTarget,
            IEnemyMassesDamageable damageableTarget = null)
        {
            // ── External skill caster path ──────────────────────────────
            if (_currentExternalCaster != null)
            {
                bool success = _currentExternalCaster.TryUseSkill(slotIndex, targetAgentIndex,
                    groundTarget, crowdController, damageableTarget);

                if (debugMode)
                    Debug.Log($"[AgentSkillBar] External caster skill {slotIndex} — success={success}");
                return;
            }

            // ── Standard crowd-agent path ───────────────────────────────
            if (crowdController == null) return;
            if (_currentSelectedIndices.Count == 0) return;

            // Determine activation mode from the skill definition
            bool singleCastMode = false;
            {
                // Peek at the first valid agent's skill config to read the mode
                for (int i = 0; i < _currentSelectedIndices.Count; i++)
                {
                    var peekAgent = crowdController.GetAgent(_currentSelectedIndices[i]);
                    if (peekAgent == null || peekAgent.enemyCrowd != _currentEnemyCrowd) continue;
                    var peekConfig = peekAgent.enemyCrowd?.skillSystemConfig;
                    if (peekConfig == null || slotIndex < 0 || slotIndex >= peekConfig.skillSlots.Count) continue;
                    var peekSlot = peekConfig.skillSlots[slotIndex];
                    if (peekSlot?.skill != null)
                    {
                        singleCastMode = peekSlot.skill.ActivationMode == Skills.SkillActivationMode.SingleCast;
                    }
                    break;
                }
            }

            int queuedCount = 0;
            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                var agent = crowdController.GetAgent(_currentSelectedIndices[i]);
                if (agent == null || agent.enemyCrowd != _currentEnemyCrowd) continue;

                // Validate skill slot exists in the agent's EnemyCrowd config
                var skillConfig = agent.enemyCrowd?.skillSystemConfig;
                if (skillConfig == null || slotIndex < 0 || slotIndex >= skillConfig.skillSlots.Count)
                    continue;

                var slot = skillConfig.skillSlots[slotIndex];
                if (slot?.skill == null || !slot.enabled)
                    continue;

                // Queue the skill directly on the agent — bypasses FactionSkillManager
                agent.queuedSkillSlot = slotIndex;
                agent.queuedSkillTargetIndex = targetAgentIndex;
                agent.queuedSkillGroundTarget = groundTarget;
                agent.queuedSkillDamageableTarget = damageableTarget;
                agent.queuedSkillTime = Time.time;
                queuedCount++;

                // Single Cast mode: only queue on one agent
                if (singleCastMode)
                    break;
            }

        }

        /// <summary>
        /// Commands selected agents to move within casting range of a target agent.
        /// WC3-style: agents already in MaxRange are stopped and left to cast immediately.
        /// Agents out of range navigate toward the target, stopping at MaxRange.
        /// Does NOT put agents into full chase/combat mode — skill execution is
        /// handled by ProcessQueuedSkills, not the combat system.
        /// </summary>
        private void CommandAgentsToSkillTarget(int targetAgentIndex, AgentSkill skill)
        {
            if (crowdController == null) return;

            var agents = crowdController.GetAgents();
            if (targetAgentIndex < 0 || targetAgentIndex >= agents.Count) return;

            CrowdAgent targetAgent = agents[targetAgentIndex];
            if (targetAgent == null || targetAgent.isDead) return;

            Vector3 targetPosition = targetAgent.WorldPosition;
            float maxRange = skill.MaxRange;
            float effectiveRange = maxRange * 0.85f;

            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                int agentIndex = _currentSelectedIndices[i];
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;

                CrowdAgent attacker = agents[agentIndex];
                if (attacker == null || attacker.isDead || attacker.navigationAgent == null) continue;

                float dist = Vector3.Distance(attacker.WorldPosition, targetPosition);

                if (dist <= maxRange)
                {
                    // Already in range — stop movement, ProcessQueuedSkills will fire next frame
                    if (!attacker.navigationAgent.IsStopped)
                        attacker.navigationAgent.IsStopped = true;

                    if (debugMode)
                        Debug.Log($"[AgentSkillBar] Agent {agentIndex} already in skill range " +
                            $"(dist={dist:F1}, maxRange={maxRange:F1}), waiting to cast.");
                    continue;
                }

                // Out of range — move toward the target's position, stopping at effectiveRange
                Vector3 direction = (attacker.WorldPosition - targetPosition).normalized;
                Vector3 destination = targetPosition + direction * effectiveRange;

                if (attacker.navigationAgent.SamplePosition(destination, out Vector3 validPos, maxRange))
                    destination = validPos;

                attacker.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                attacker.hasManualCommand = true;
                attacker.manualCommandDestination = destination;
                attacker.combatState = CrowdCombatState.Idle;
                attacker.moveCommandIssuedTime = Time.time;
                attacker.hasRTSFormationPosition = false;
                attacker.brokeFromRTSFormation = false;
                attacker.hasFormationFacing = false;

                // Clear combat/chase state so the combat system does not re-engage
                // the old attack target and fight with ManualCommand for nav control.
                attacker.attackTargetAgentIndex = -1;
                attacker.attackTargetTransform = null;
                attacker.hasAttackTargetPosition = false;
                attacker.isChasing = false;
                attacker.ClearCommandIntent();

                if (attacker.navigationAgent.IsStopped)
                    attacker.navigationAgent.IsStopped = false;

                attacker.navigationAgent.SetDestination(destination);

                if (debugMode)
                    Debug.Log($"[AgentSkillBar] Agent {agentIndex} moving into skill range: " +
                        $"dist={dist:F1}, maxRange={maxRange:F1}, destination={destination}");
            }
        }

        /// <summary>
        /// Commands selected agents to move within casting range of a damageable object.
        /// Same stop-in-range / move-to-range logic as <see cref="CommandAgentsToSkillTarget"/>.
        /// </summary>
        private void CommandAgentsToMoveToDamageable(IEnemyMassesDamageable damageable, AgentSkill skill)
        {
            if (crowdController == null || damageable == null || damageable.DamageTransform == null) return;

            Vector3 targetPosition = damageable.DamageTransform.position;
            float maxRange = skill.MaxRange;
            float effectiveRange = maxRange * 0.85f;

            var agents = crowdController.GetAgents();
            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                int agentIndex = _currentSelectedIndices[i];
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;

                CrowdAgent attacker = agents[agentIndex];
                if (attacker == null || attacker.isDead || attacker.navigationAgent == null) continue;

                float dist = Vector3.Distance(attacker.WorldPosition, targetPosition);

                if (dist <= maxRange)
                {
                    if (!attacker.navigationAgent.IsStopped)
                        attacker.navigationAgent.IsStopped = true;
                    continue;
                }

                Vector3 direction = (attacker.WorldPosition - targetPosition).normalized;
                Vector3 destination = targetPosition + direction * effectiveRange;

                if (attacker.navigationAgent.SamplePosition(destination, out Vector3 validPos, maxRange))
                    destination = validPos;

                attacker.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                attacker.hasManualCommand = true;
                attacker.manualCommandDestination = destination;
                attacker.combatState = CrowdCombatState.Idle;
                attacker.moveCommandIssuedTime = Time.time;
                attacker.hasRTSFormationPosition = false;
                attacker.brokeFromRTSFormation = false;
                attacker.hasFormationFacing = false;

                attacker.attackTargetAgentIndex = -1;
                attacker.attackTargetTransform = null;
                attacker.hasAttackTargetPosition = false;
                attacker.isChasing = false;
                attacker.ClearCommandIntent();

                if (attacker.navigationAgent.IsStopped)
                    attacker.navigationAgent.IsStopped = false;

                attacker.navigationAgent.SetDestination(destination);
            }
        }

        /// <summary>
        /// Commands all selected agents to chase the target agent so they move into skill range.
        /// Mirrors the RTS Controller's PerformAttackCommand flow — sets all required combat,
        /// navigation, and formation state so agents properly transition to chasing.
        /// </summary>
        private void CommandAgentsToChaseTarget(int targetAgentIndex)
        {
            if (crowdController == null)
            {
                return;
            }

            var agents = crowdController.GetAgents();
            if (targetAgentIndex < 0 || targetAgentIndex >= agents.Count)
            {
                return;
            }

            CrowdAgent targetAgent = agents[targetAgentIndex];
            if (targetAgent == null || targetAgent.isDead)
            {
                return;
            }

            Transform targetTransform = targetAgent.navigationAgent?.GetUnderlyingAgent<Component>()?.transform;
            if (targetTransform == null)
            {
                return;
            }

            Vector3 targetPosition = targetTransform.position;


            int attackersAssigned = 0;

            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                int agentIndex = _currentSelectedIndices[i];
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;

                CrowdAgent attacker = agents[agentIndex];
                if (attacker == null || attacker.isDead || attacker.navigationAgent == null) continue;

                // Set attack target — mirrors RTS Controller's PerformAttackCommand
                attacker.attackTargetTransform = targetTransform;
                attacker.attackTargetPosition = targetPosition;
                attacker.hasAttackTargetPosition = true;
                attacker.attackTargetAgentIndex = targetAgentIndex;

                // Mark as explicit manual attack command (allows Passive agents to attack)
                attacker.SetExplicitAttackIntent(targetAgentIndex);

                // Clear manual locks before switching state to chasing.
                // PRESERVE holdPositionLocation and hasHoldPositionSet — these are only
                // consulted when the agent returns to Idle (not during chase), and we
                // need them intact so HoldPosition agents can return to their original
                // hold position after the target dies.
                attacker.hasManualCommand = false;
                attacker.manualCommandDestination = Vector3.zero;

                // Change navigation state to chasing
                attacker.ForceSetNavigationState(AgentNavigationState.ChasingTarget);
                attacker.isChasing = true;
                attacker.isAggroed = true;
                attacker.lastTargetInRangeTime = Time.time;
                attacker.aggroAcquiredTime = Time.time;

                // Set combat state to approach (chasing target)
                attacker.combatState = CrowdCombatState.Approach;

                // Clear formation flags — attack breaks formation
                attacker.hasFormationFacing = false;
                attacker.hasRTSFormationPosition = false;
                attacker.brokeFromRTSFormation = false;

                // Resume the navigation agent if it was stopped
                if (attacker.navigationAgent.IsStopped)
                {
                    attacker.navigationAgent.IsStopped = false;
                }

                attackersAssigned++;
            }

        }

        /// <summary>
        /// Commands selected agents to move within casting range of a ground target.
        /// Agents already in range are stopped and left to cast immediately.
        /// Agents out of range navigate to a point within MaxRange of the ground target.
        /// </summary>
        private void CommandAgentsToMoveInRangeOfGround(Vector3 groundTarget, float maxRange)
        {
            if (crowdController == null) return;

            var agents = crowdController.GetAgents();
            // Use 85% of max range so agents don't end up right at the edge
            float effectiveRange = maxRange * 0.85f;

            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                int agentIndex = _currentSelectedIndices[i];
                if (agentIndex < 0 || agentIndex >= agents.Count) continue;

                CrowdAgent agent = agents[agentIndex];
                if (agent == null || agent.isDead || agent.navigationAgent == null) continue;

                Vector3 agentPos = agent.WorldPosition;
                float dist = Vector3.Distance(agentPos, groundTarget);

                if (dist <= maxRange)
                {
                    // Already in range — stop movement so skill fires on next check
                    if (!agent.navigationAgent.IsStopped)
                    {
                        agent.navigationAgent.IsStopped = true;
                    }
                    continue;
                }

                // Out of range — compute a destination within effectiveRange of the ground target.
                // Move along the line from ground target toward the agent, stopping at effectiveRange.
                Vector3 direction = (agentPos - groundTarget).normalized;
                Vector3 destination = groundTarget + direction * effectiveRange;

                // Sample NavMesh to ensure valid position
                if (agent.navigationAgent.SamplePosition(destination, out Vector3 validPos, maxRange))
                {
                    destination = validPos;
                }

                // Set up move command — mirrors RTS Controller's MoveSelectedAgentsTo
                agent.ForceSetNavigationState(AgentNavigationState.ManualCommand);
                agent.attackTargetAgentIndex = -1;
                agent.attackTargetTransform = null;
                agent.hasAttackTargetPosition = false;
                agent.isChasing = false;
                agent.SetMoveIntent();
                agent.combatState = CrowdCombatState.Idle;
                agent.moveCommandIssuedTime = Time.time;
                agent.manualCommandDestination = destination;
                agent.hasManualCommand = true;
                agent.hasRTSFormationPosition = false;
                agent.brokeFromRTSFormation = false;
                agent.hasFormationFacing = false;

                if (agent.navigationAgent.IsStopped)
                    agent.navigationAgent.IsStopped = false;

                agent.navigationAgent.SetDestination(destination);

                if (debugMode)
                    Debug.Log($"[AgentSkillBar] Agent {agentIndex} moving to range of ground target: " +
                        $"dist={dist:F1}, maxRange={maxRange:F1}, destination={destination}");
            }
        }

        /// <summary>
        /// Each frame, checks selected agents that have a queued skill.
        /// When an agent arrives within MaxRange of its target (ground point or
        /// target agent), fires the skill via FactionSkillManager and clears the queue.
        /// This enables WC3-style "move into range then cast" for all skill types.
        /// </summary>
        /// <summary>
        /// Maximum time (seconds) a queued skill can stay pending before being
        /// automatically cleared. Prevents permanent "locked" states when a skill
        /// can never fire (target unreachable, cooldown desync with auto-cast, etc.).
        /// </summary>
        private const float QueuedSkillTimeout = 10f;

        private void ProcessQueuedSkills()
        {
            if (crowdController == null || _currentSelectedIndices.Count == 0) return;

            var skillManager = FactionSkillManager.Instance;
            if (skillManager == null)
            {
                // FactionSkillManager isn't in the scene yet — auto-add it to the
                // crowdController GameObject so manual skill execution works.
                if (crowdController != null)
                {
                    skillManager = crowdController.gameObject.GetComponent<FactionSkillManager>();
                    if (skillManager == null)
                    {
                        skillManager = crowdController.gameObject.AddComponent<FactionSkillManager>();
                        Debug.Log("[AgentSkillBar] Auto-added FactionSkillManager to crowdController.");
                    }
                }
                if (skillManager == null) return;
            }

            var agents = crowdController.GetAgents();
            float currentTime = Time.time;

            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                var agent = crowdController.GetAgent(_currentSelectedIndices[i]);
                if (agent == null || agent.isDead) continue;
                if (agent.queuedSkillSlot < 0) continue;
                
                // Skip agents that are mid-cast (cast time pending)
                if (agent.isCasting) continue;

                var skillConfig = agent.enemyCrowd?.skillSystemConfig;
                if (skillConfig == null) continue;
                if (agent.queuedSkillSlot >= skillConfig.skillSlots.Count)
                {
                    ClearAgentSkillQueue(agent);
                    continue;
                }

                var slot = skillConfig.skillSlots[agent.queuedSkillSlot];
                if (slot?.skill == null || !slot.enabled)
                {
                    ClearAgentSkillQueue(agent);
                    continue;
                }

                // ── Stale queue timeout ──────────────────────────────────────
                // If the queued skill has been pending for too long, clear it.
                // This prevents permanent "locked" states caused by cooldown
                // desync between FactionSkillManager and the combat system's
                // auto-cast, or targets that moved out of reach permanently.
                if (currentTime - agent.queuedSkillTime > QueuedSkillTimeout)
                {
                    if (debugMode)
                        Debug.Log($"[AgentSkillBar] Agent {agent.index} queued skill {slot.skill.SkillName} " +
                            $"timed out after {QueuedSkillTimeout}s — clearing stale queue.");
                    ClearAgentSkillQueue(agent);
                    continue;
                }

                // ── Check if the combat system already has this skill on cooldown ──
                // The combat system's auto-cast uses separate cooldown state from
                // FactionSkillManager. If the combat system just fired this skill,
                // skip firing it again this frame to prevent double-execution.
                float combatCooldownRemaining = crowdController.GetSkillCooldownRemaining(agent, agent.queuedSkillSlot);
                if (combatCooldownRemaining > 0f)
                {
                    // Combat system's auto-cast already fired this skill recently.
                    // Don't double-fire; wait for the cooldown to expire.
                    continue;
                }

                // Determine the target position and range check
                float dist;
                int targetIdx = agent.queuedSkillTargetIndex;

                if (agent.queuedSkillGroundTarget.HasValue)
                {
                    // Ground-targeted: distance to ground point
                    dist = Vector3.Distance(agent.WorldPosition, agent.queuedSkillGroundTarget.Value);
                }
                else if (targetIdx >= 0 && targetIdx < agents.Count)
                {
                    // Agent-targeted: distance to target agent
                    var targetAgent = agents[targetIdx];
                    if (targetAgent == null || targetAgent.isDead)
                    {
                        // Target died — clear queue
                        ClearAgentSkillQueue(agent);
                        continue;
                    }
                    dist = Vector3.Distance(agent.WorldPosition, targetAgent.WorldPosition);
                }
                else if (slot.skill.TargetingType == SkillTargetingType.Self ||
                         slot.skill.TargetingType == SkillTargetingType.AOEAroundSelf ||
                         slot.skill.TargetingType == SkillTargetingType.ClosestEnemy ||
                         slot.skill.TargetingType == SkillTargetingType.LowestHealthAlly ||
                         slot.skill.TargetingType == SkillTargetingType.AllAlliesInRange ||
                         slot.skill.TargetingType == SkillTargetingType.AllEnemiesInRange ||
                         slot.skill.TargetingType == SkillTargetingType.AllCrowdMembers ||
                         slot.skill.TargetingType == SkillTargetingType.AllFactionMembers)
                {
                    // Self-targeted and auto-target skills — always in range,
                    // let ForceUseSkill find the targets automatically.
                    dist = 0f;
                }
                else
                {
                    // Unknown state — clear to avoid permanent stale queue
                    if (debugMode)
                        Debug.Log($"[AgentSkillBar] Agent {agent.index} queued {slot.skill.SkillName} " +
                            $"has no valid target (type={slot.skill.TargetingType}) — clearing.");
                    ClearAgentSkillQueue(agent);
                    continue;
                }

                if (dist > slot.skill.MaxRange) continue;

                // In range — fire the skill
                int skillSlot = agent.queuedSkillSlot;
                Vector3? groundTarget = agent.queuedSkillGroundTarget;

                bool success = skillManager.ForceUseSkill(agent, skillSlot, groundTarget, targetIdx);

                // Whether it succeeded or failed on cooldown, clear the queue.
                // If we leave it queued, 299 agents spam ForceUseSkill every frame
                // for 10s until the stale timeout clears them.
                ClearAgentSkillQueue(agent);

                if (success)
                {
                    agent.lastSkillUseTime = currentTime;

                    // Stop the agent after casting (WC3-style: cast then idle)
                    if (agent.navigationAgent != null && !agent.navigationAgent.IsStopped)
                        agent.navigationAgent.IsStopped = true;

                    // Clear chase/combat state if it was set
                    if (agent.isChasing)
                    {
                        agent.isChasing = false;
                        agent.combatState = CrowdCombatState.Idle;
                    }

                    if (debugMode)
                        Debug.Log($"[AgentSkillBar] Agent {agent.index} cast {slot.skill.SkillName} " +
                            $"(dist={dist:F1}, maxRange={slot.skill.MaxRange:F1})");
                }
                else if (debugMode)
                {
                    Debug.Log($"[AgentSkillBar] Agent {agent.index} in range but ForceUseSkill failed " +
                        $"(cooldown or no targets). dist={dist:F1}, maxRange={slot.skill.MaxRange:F1}");
                }
            }
        }

        /// <summary>
        /// Clears all queued-skill fields on an agent. Centralised to avoid forgetting a field.
        /// </summary>
        private static void ClearAgentSkillQueue(CrowdAgent agent)
        {
            agent.queuedSkillSlot = -1;
            agent.queuedSkillTargetIndex = -1;
            agent.queuedSkillGroundTarget = null;
        }

        #endregion

        #region Cooldown Updates

        private void UpdateCooldowns()
        {
            if (_activeSlots.Count == 0) return;

            // ── External skill caster path ──────────────────────────────
            if (_currentExternalCaster != null)
            {
                for (int i = 0; i < _activeSlots.Count; i++)
                {
                    var slotUI = _activeSlots[i];
                    if (slotUI == null || slotUI.Skill == null) continue;

                    float remaining = _currentExternalCaster.GetCooldownRemaining(slotUI.SlotIndex);
                    float total = _currentExternalCaster.GetCooldownTotal(slotUI.SlotIndex);
                    slotUI.UpdateCooldown(remaining, total);
                }
                return;
            }

            // ── Standard crowd-agent path ───────────────────────────────
            if (crowdController == null || _currentSelectedIndices.Count == 0)
                return;

            // Use the first valid selected agent for cooldown display
            CrowdAgent displayAgent = null;
            for (int i = 0; i < _currentSelectedIndices.Count; i++)
            {
                int agentIndex = _currentSelectedIndices[i];
                var agent = crowdController.GetAgent(agentIndex);
                if (agent != null && !agent.isDead)
                {
                    displayAgent = agent;
                    break;
                }
            }

            if (displayAgent == null)
                return;

            // Check FactionSkillManager for cooldowns started via manual skill bar path
            var fsmInstance = FactionSkillManager.Instance;

            for (int i = 0; i < _activeSlots.Count; i++)
            {
                var slotUI = _activeSlots[i];
                if (slotUI == null || slotUI.Skill == null) continue;

                // The combat system and FactionSkillManager maintain separate cooldown
                // state. A skill fired via auto-cast only updates the combat system;
                // a skill fired via the manual skill bar only updates FactionSkillManager.
                // Use the maximum of both so the UI always reflects the true cooldown.
                float combatRemaining = crowdController.GetSkillCooldownRemaining(displayAgent, slotUI.SlotIndex);
                float fsmRemaining = fsmInstance != null ? fsmInstance.GetCooldownRemaining(displayAgent, slotUI.SlotIndex) : 0f;
                float remaining = Mathf.Max(combatRemaining, fsmRemaining);
                float total = slotUI.Skill.Cooldown;

                slotUI.UpdateCooldown(remaining, total);
            }
        }

        #endregion

        #region Tooltip

        /// <summary>
        /// Shows the skill preview tooltip for the hovered slot.
        /// Uses AgentSkill.GetSkillPreviewText() which already handles localization.
        /// </summary>
        internal void ShowTooltip(AgentSkillSlotUI slot)
        {
            _hoveredSlot = slot;

            if (tooltipPanel == null || tooltipText == null || slot.Skill == null)
                return;

            // AgentSkill already handles localization via its preview methods
            string previewText = slot.Skill.GetSkillPreviewText(tooltipRichText);
            
            // Append auto-cast status hint (only for skills eligible for auto-cast)
            if (slot.Skill.Autocast != Arawn.EnemyMasses.Runtime.Skills.AutocastEligibility.NotEligible)
            {
                string toggleHint = GetAutoCastToggleHint();
                if (slot.IsAutocastActive)
                {
                    previewText += tooltipRichText 
                        ? $"\n\n<color=#FFD700>Auto-Cast: ON</color>\n<color=#AAA><size=11>{toggleHint} to disable</size></color>" 
                        : $"\n\nAuto-Cast: ON\n{toggleHint} to disable";
                }
                else
                {
                    previewText += tooltipRichText 
                        ? $"\n\n<color=#888><size=11>{toggleHint} to toggle Auto-Cast</size></color>" 
                        : $"\n\n{toggleHint} to toggle Auto-Cast";
                }
            }
#if !TMP_PRESENT
            // Legacy UI.Text only supports #RRGGBB / #RRGGBBAA hex — expand shorthand #RGB colors
            previewText = ExpandShortHexColors(previewText);
#endif
            tooltipText.text = previewText;
            tooltipPanel.gameObject.SetActive(true);

            // Position tooltip above the slot
            var slotRect = slot.GetComponent<RectTransform>();
            if (slotRect != null)
            {
                Vector3 slotPos = slotRect.position;
                tooltipPanel.position = slotPos + (Vector3)tooltipOffset;
            }
        }

        /// <summary>
        /// Hides the tooltip.
        /// </summary>
        internal void HideTooltip()
        {
            _hoveredSlot = null;

            if (tooltipPanel != null)
                tooltipPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Returns a human-readable label for the auto-cast toggle binding
        /// (e.g. "Right-click", "Middle-click", "K") for use in tooltip hints.
        /// </summary>
        private string GetAutoCastToggleHint()
        {
#if ENABLE_INPUT_SYSTEM
            if (autoCastToggleAction != null && autoCastToggleAction.bindings.Count > 0)
            {
                string path = autoCastToggleAction.bindings[0].effectivePath;
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.Contains("rightButton")) return "Right-click";
                    if (path.Contains("leftButton")) return "Left-click";
                    if (path.Contains("middleButton")) return "Middle-click";
                    // Extract key name from path like "<Keyboard>/k"
                    int slash = path.LastIndexOf('/');
                    if (slash >= 0 && slash < path.Length - 1)
                        return path.Substring(slash + 1).ToUpperInvariant();
                }
            }
            return "Right-click";
#else
            switch (autoCastToggleLegacyKey)
            {
                case KeyCode.Mouse0: return "Left-click";
                case KeyCode.Mouse1: return "Right-click";
                case KeyCode.Mouse2: return "Middle-click";
                default: return autoCastToggleLegacyKey.ToString();
            }
#endif
        }

        /// <summary>
        /// Ensures the tooltip panel has a VerticalLayoutGroup so ContentSizeFitter
        /// can compute preferred size from the text child. Without this, the panel
        /// collapses to 0 height and the tooltip text is invisible.
        /// </summary>
        private void EnsureTooltipLayout()
        {
            if (tooltipPanel == null) return;

            var layoutGroup = tooltipPanel.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup == null)
            {
                layoutGroup = tooltipPanel.gameObject.AddComponent<VerticalLayoutGroup>();
                layoutGroup.padding = new RectOffset(10, 10, 10, 10);
                layoutGroup.childForceExpandWidth = true;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childControlWidth = true;
                layoutGroup.childControlHeight = true;
                layoutGroup.childAlignment = TextAnchor.UpperLeft;
            }

            var fitter = tooltipPanel.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = tooltipPanel.gameObject.AddComponent<ContentSizeFitter>();
            }
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

#if !TMP_PRESENT
        /// <summary>
        /// Expands 3-char hex color codes (#RGB) to 6-char (#RRGGBB) for legacy UI.Text compatibility.
        /// Legacy Text doesn't support CSS-style shorthand hex colors.
        /// E.g. &lt;color=#8AC&gt; → &lt;color=#88AACC&gt;
        /// </summary>
        private static string ExpandShortHexColors(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"<color=#([0-9A-Fa-f])([0-9A-Fa-f])([0-9A-Fa-f])>",
                m => $"<color=#{m.Groups[1].Value}{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[2].Value}{m.Groups[3].Value}{m.Groups[3].Value}>");
        }
#endif

        #endregion

        #region Visibility

        private void SetBarVisible(bool visible, bool instant = false)
        {
            _isVisible = visible;

            if (hideWhenNoSelection)
            {
                if (instant || fadeDuration <= 0f)
                {
                    if (_canvasGroup != null)
                        _canvasGroup.alpha = visible ? 1f : 0f;

                    if (skillBarRoot != null)
                        skillBarRoot.gameObject.SetActive(visible);
                }
                else if (visible && skillBarRoot != null)
                {
                    skillBarRoot.gameObject.SetActive(true);
                }
                // Fade-out hide is handled in Update()
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;
            }
        }

        #endregion

        #region Default Input Bindings

        /// <summary>
        /// Populates default input bindings (Q, W, E, R, T, 1-5) for up to 10 skill slots.
        /// Called from the editor or at design time.
        /// </summary>
        public void PopulateDefaultBindings(int count = 10)
        {
            if (skillInputBindings == null)
                skillInputBindings = new List<SkillSlotInputBinding>();

            skillInputBindings.Clear();

            string[] defaultKeys = { "q", "w", "e", "r", "t", "1", "2", "3", "4", "5" };

            for (int i = 0; i < Mathf.Min(count, defaultKeys.Length); i++)
            {
                var binding = new SkillSlotInputBinding
                {
                    label = $"Skill {i + 1}",
#if ENABLE_INPUT_SYSTEM
                    inputAction = new InputAction($"Skill {i + 1}", InputActionType.Button, $"<Keyboard>/{defaultKeys[i]}")
#else
                    legacyKey = (KeyCode)System.Enum.Parse(typeof(KeyCode), defaultKeys[i].Length == 1 && char.IsLetter(defaultKeys[i][0])
                        ? defaultKeys[i].ToUpperInvariant()
                        : "Alpha" + defaultKeys[i])
#endif
                };
                skillInputBindings.Add(binding);
            }
        }

        #endregion
    }
}
