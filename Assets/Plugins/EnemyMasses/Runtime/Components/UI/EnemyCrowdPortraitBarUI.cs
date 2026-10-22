using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if TMP_PRESENT
using UITextComponent = TMPro.TextMeshProUGUI;
#else
using UITextComponent = UnityEngine.UI.Text;
#endif

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Controls when crowd portraits become visible in the portrait bar.
    /// </summary>
    public enum PortraitVisibilityMode
    {
        /// <summary>Show portraits for all registered player-controlled crowds immediately, even before any agents are spawned.</summary>
        [Tooltip("Show all player-controlled crowd portraits immediately, regardless of spawn status.")]
        ShowAll,

        /// <summary>Only show a crowd's portrait once at least one agent of that crowd has been spawned.</summary>
        [Tooltip("Only show a crowd portrait after at least one agent of that crowd has been spawned.\n" +
                 "Works well with Remove Dead Portraits disabled — unspawned crowds are never shown or marked dead.")]
        ShowOnceSpawned
    }

    /// <summary>
    /// Displays a portrait bar for all player-controlled EnemyCrowd types.
    /// Each portrait shows the crowd's average health, turns grayscale on full wipe,
    /// highlights red when agents are under attack, and supports click-to-select /
    /// double-click-to-focus-camera.
    /// </summary>
    [AddComponentMenu("Enemy Masses/UI/Crowd Portrait Bar")]
    public class EnemyCrowdPortraitBarUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [Tooltip("The RTS Controller. Auto-resolved at runtime if left empty.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("The Crowd Controller. Auto-resolved at runtime if left empty.")]
        public EnemyMassesCrowdController crowdController;

        [Header("UI References")]
        [Tooltip("The root RectTransform of the portrait bar.")]
        public RectTransform barRoot;

        [Tooltip("The container for portrait slots (should have a HorizontalLayoutGroup).")]
        public RectTransform slotContainer;

        [Tooltip("Tooltip panel shown when hovering a portrait. Hidden by default.")]
        public RectTransform tooltipPanel;

        [Tooltip("Text component inside the tooltip panel.")]
        public UITextComponent tooltipText;

        [Header("Appearance")]
        [Tooltip("Size of each portrait slot in pixels.")]
        public Vector2 slotSize = new Vector2(64, 80);

        [Tooltip("Color of the health bar fill.")]
        public Color healthBarColor = new Color(0.2f, 0.8f, 0.2f, 1f);

        [Tooltip("Color of the health bar background.")]
        public Color healthBarBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);

        [Tooltip("Color of the frame when agents are under attack.")]
        public Color underAttackFrameColor = new Color(0.9f, 0.15f, 0.1f, 1f);

        [Tooltip("Normal frame color.")]
        public Color normalFrameColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        [Tooltip("Selected frame color.")]
        public Color selectedFrameColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Tooltip("Tint applied to the portrait when all agents of the crowd are dead.")]
        public Color deadPortraitTint = new Color(0.25f, 0.25f, 0.25f, 1f);

        [Header("Behavior")]
        [Tooltip("Controls when crowd portraits appear in the bar.\n\n" +
                 "Show All: Portraits for all player-controlled crowds are created immediately on startup.\n\n" +
                 "Show Once Spawned: A crowd's portrait only appears after at least one agent of that crowd " +
                 "has been spawned. Works well with Remove Dead Portraits disabled — unspawned crowds are " +
                 "never shown or marked dead.")]
        public PortraitVisibilityMode portraitVisibility = PortraitVisibilityMode.ShowAll;

        [Tooltip("Remove dead crowd portraits after they turn grayscale.")]
        public bool removeDeadPortraits = false;

        [Tooltip("Delay in seconds before a dead crowd's portrait is removed (if enabled).")]
        public float deadRemovalDelay = 3f;

        [Tooltip("Time window in seconds: an agent is considered 'under attack' if it took damage within this window.")]
        public float underAttackWindow = 2f;

        [Tooltip("How often (in seconds) the bar refreshes health and attack state.")]
        public float updateInterval = 0.15f;

        [Tooltip("Double-click time window in seconds for focus-camera.")]
        public float doubleClickTime = 0.3f;

        [Tooltip("Radius in world units to find the densest cluster of agents when focusing camera.")]
        public float focusClusterRadius = 30f;

        #endregion

        #region Runtime State

        private readonly List<PortraitSlot> _slots = new List<PortraitSlot>();
        private float _nextUpdateTime;
        private Camera _camera;

        /// <summary>
        /// Tracks which crowd indices already have a portrait slot.
        /// Used by <see cref="PortraitVisibilityMode.ShowOnceSpawned"/> to lazily
        /// create slots when the first agent of a crowd appears.
        /// </summary>
        private readonly HashSet<int> _slottedCrowdIndices = new HashSet<int>();

        /// <summary>
        /// Per-crowd-index tracking data.
        /// </summary>
        private class PortraitSlot
        {
            // Identity
            public int enemyCrowdIndex;
            public EnemyCrowd enemyCrowd;

            // External skill caster (non-null when this slot represents an IExternalSkillCaster)
            public IExternalSkillCaster externalCaster;

            // UI elements
            public GameObject root;
            public RawImage portraitImage;
            public Image frameImage;
            public Image healthBarFill;
            public Image healthBarBg;
            public UITextComponent nameLabel;

            // State
            public float averageHealth;  // 0..1
            public bool isDead;
            public bool isUnderAttack;
            public bool isSelected;
            public float deadSince;      // Time.time when fully dead; -1 if alive
            public float lastClickTime;  // for double-click detection

            // Debug
            public bool _healthDebugLogged;

            public bool IsExternal => externalCaster != null;
        }

        #endregion

        #region Lifecycle

        private void Awake()
        {
            AutoResolveReferences();
        }

        private void OnEnable()
        {
            AutoResolveReferences();
            EnsureTooltipNonInteractive();
            RebuildSlots();

            if (crowdController != null)
                crowdController.EventAgentHealthChanged += HandleAgentHealthChanged;

            if (rtsController != null)
                rtsController.ExternalSelectableRegistered += HandleExternalSelectableRegistered;
        }

        private void OnDisable()
        {
            if (crowdController != null)
                crowdController.EventAgentHealthChanged -= HandleAgentHealthChanged;

            if (rtsController != null)
                rtsController.ExternalSelectableRegistered -= HandleExternalSelectableRegistered;
        }

        private void HandleExternalSelectableRegistered(IRTSExternalSelectable selectable)
        {
            // When a new external selectable registers after the initial build,
            // check if it's an IExternalSkillCaster that wants a portrait slot.
            if (selectable is IExternalSkillCaster caster && caster.ShowInPortraitBar)
            {
                // Avoid duplicates — check if we already have a slot for this caster
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].IsExternal && ReferenceEquals(_slots[i].externalCaster, caster))
                        return;
                }

                var slot = CreateSlotForExternal(caster);
                _slots.Add(slot);
            }
        }

        private void Update()
        {
            if (crowdController == null) return;

            if (Time.time >= _nextUpdateTime)
            {
                _nextUpdateTime = Time.time + updateInterval;
                UpdateAllSlots();
            }
        }

        #endregion

        #region Auto-Resolve

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
                crowdController = rtsController.crowdController;

            if (crowdController == null)
            {
#if UNITY_2023_1_OR_NEWER
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
#else
                crowdController = FindObjectOfType<EnemyMassesCrowdController>();
#endif
            }

            if (rtsController != null)
            {
                var cam = rtsController.targetCamera;
                _camera = cam != null ? cam : Camera.main;
            }
            else
            {
                _camera = Camera.main;
            }
        }

        #endregion

        #region Slot Building

        /// <summary>
        /// Rebuilds the slot list from the crowd controller's EnemyCrowds,
        /// filtered to player-controlled factions only.
        /// </summary>
        public void RebuildSlots()
        {
            // Clear old slots
            foreach (var slot in _slots)
            {
                if (slot.root != null)
                    Destroy(slot.root);
            }
            _slots.Clear();
            _slottedCrowdIndices.Clear();

            if (crowdController == null) return;

            var crowds = crowdController.EnemyCrowds;
            if (crowds == null) return;

            bool deferUnspawned = portraitVisibility == PortraitVisibilityMode.ShowOnceSpawned;
            var agents = deferUnspawned ? crowdController.GetAgents() : null;

            for (int i = 0; i < crowds.Count; i++)
            {
                EnemyCrowd crowd = crowds[i];
                if (crowd == null) continue;

                // Only show player-controlled crowds
                if (rtsController != null && !IsPlayerControlled(crowd))
                    continue;

                // In ShowOnceSpawned mode, skip crowds that have no living or dead agents yet
                if (deferUnspawned && !HasAnyAgentForCrowd(agents, i))
                    continue;

                var slot = CreateSlot(i, crowd);
                _slots.Add(slot);
                _slottedCrowdIndices.Add(i);
            }

            // Also show external skill casters that want portrait bar presence
            if (rtsController != null)
            {
                var externals = rtsController.ExternalSelectables;
                if (externals != null)
                {
                    for (int i = 0; i < externals.Count; i++)
                    {
                        if (externals[i] is IExternalSkillCaster caster && caster.ShowInPortraitBar)
                        {
                            var slot = CreateSlotForExternal(caster);
                            _slots.Add(slot);
                        }
                    }
                }
            }
        }

        private bool IsPlayerControlled(EnemyCrowd crowd)
        {
            if (rtsController == null) return true;
            if (rtsController.playerControlledFactions == null ||
                !rtsController.playerControlledFactions.HasAnyFaction)
                return true; // No filter = all are player-controlled
            return rtsController.playerControlledFactions.ContainsFaction(crowd.factionName);
        }

        /// <summary>
        /// Returns true if at least one CrowdAgent exists for the given crowd index.
        /// Used by <see cref="PortraitVisibilityMode.ShowOnceSpawned"/> to decide
        /// whether to create a portrait slot.
        /// </summary>
        private bool HasAnyAgentForCrowd(List<CrowdAgent> agents, int crowdIndex)
        {
            if (agents == null) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i] != null && agents[i].enemyCrowdIndex == crowdIndex)
                    return true;
            }
            return false;
        }

        private PortraitSlot CreateSlot(int crowdIndex, EnemyCrowd crowd)
        {
            var slot = new PortraitSlot
            {
                enemyCrowdIndex = crowdIndex,
                enemyCrowd = crowd,
                deadSince = -1f,
                lastClickTime = -999f
            };

            // Root
            var rootGO = new GameObject($"Portrait_{crowd.enemyTypeName}", typeof(RectTransform));
            rootGO.transform.SetParent(slotContainer, false);
            var rootRect = rootGO.GetComponent<RectTransform>();
            rootRect.sizeDelta = slotSize;
            slot.root = rootGO;

            // Frame / border (background image that shows colored border)
            var frameGO = new GameObject("Frame", typeof(RectTransform));
            frameGO.transform.SetParent(rootGO.transform, false);
            var frameRect = frameGO.GetComponent<RectTransform>();
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = new Vector2(1f, 1f);
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = new Vector2(0f, -16f); // leave room for health bar
            slot.frameImage = frameGO.AddComponent<Image>();
            slot.frameImage.color = normalFrameColor;

            // Portrait image (inset from frame)
            var portraitGO = new GameObject("Portrait", typeof(RectTransform));
            portraitGO.transform.SetParent(frameGO.transform, false);
            var portraitRect = portraitGO.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(2, 2);
            portraitRect.offsetMax = new Vector2(-2, -2);
            slot.portraitImage = portraitGO.AddComponent<RawImage>();

            if (crowd.portrait != null)
            {
                slot.portraitImage.texture = crowd.portrait;
            }
            else
            {
                // Fallback: solid faction color
                slot.portraitImage.texture = null;
                slot.portraitImage.color = crowd.factionColor;
            }

            // Health bar background
            var hpBgGO = new GameObject("HealthBarBg", typeof(RectTransform));
            hpBgGO.transform.SetParent(rootGO.transform, false);
            var hpBgRect = hpBgGO.GetComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 0f);
            hpBgRect.anchorMax = new Vector2(1f, 0f);
            hpBgRect.pivot = new Vector2(0f, 0f);
            hpBgRect.anchoredPosition = Vector2.zero;
            hpBgRect.sizeDelta = new Vector2(0f, 14f);
            slot.healthBarBg = hpBgGO.AddComponent<Image>();
            slot.healthBarBg.color = healthBarBackgroundColor;

            // Health bar fill
            var hpFillGO = new GameObject("HealthBarFill", typeof(RectTransform));
            hpFillGO.transform.SetParent(hpBgGO.transform, false);
            var hpFillRect = hpFillGO.GetComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = new Vector2(1f, 1f);
            hpFillRect.pivot = new Vector2(0f, 0.5f);
            hpFillRect.offsetMin = new Vector2(1, 1);
            hpFillRect.offsetMax = new Vector2(-1, -1);
            slot.healthBarFill = hpFillGO.AddComponent<Image>();
            slot.healthBarFill.color = healthBarColor;

            // Name label (overlay on health bar)
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(hpBgGO.transform, false);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(2, 0);
            labelRect.offsetMax = new Vector2(-2, 0);
            slot.nameLabel = labelGO.AddComponent<UITextComponent>();
            slot.nameLabel.fontSize = 9;
            slot.nameLabel.color = Color.white;
#if TMP_PRESENT
            slot.nameLabel.alignment = TMPro.TextAlignmentOptions.Center;
            slot.nameLabel.enableWordWrapping = false;
            slot.nameLabel.overflowMode = TMPro.TextOverflowModes.Truncate;
#else
            slot.nameLabel.alignment = TextAnchor.MiddleCenter;
            slot.nameLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            slot.nameLabel.verticalOverflow = VerticalWrapMode.Truncate;
#endif
            string displayName = !string.IsNullOrEmpty(crowd.enemyTypeName) ? crowd.enemyTypeName : crowd.factionName;
            slot.nameLabel.text = displayName;

            // Click handler
            var clickHandler = rootGO.AddComponent<PortraitClickHandler>();
            clickHandler.Initialize(this, slot.enemyCrowdIndex);

            return slot;
        }

        /// <summary>
        /// Creates a portrait slot backed by an <see cref="IExternalSkillCaster"/> instead of an EnemyCrowd.
        /// </summary>
        private PortraitSlot CreateSlotForExternal(IExternalSkillCaster caster)
        {
            var slot = new PortraitSlot
            {
                enemyCrowdIndex = -1,
                enemyCrowd = null,
                externalCaster = caster,
                deadSince = -1f,
                lastClickTime = -999f
            };

            string displayName = !string.IsNullOrEmpty(caster.EnemyTypeName)
                ? caster.EnemyTypeName
                : caster.FactionName;

            // Root
            var rootGO = new GameObject($"Portrait_Ext_{displayName}", typeof(RectTransform));
            rootGO.transform.SetParent(slotContainer, false);
            var rootRect = rootGO.GetComponent<RectTransform>();
            rootRect.sizeDelta = slotSize;
            slot.root = rootGO;

            // Frame
            var frameGO = new GameObject("Frame", typeof(RectTransform));
            frameGO.transform.SetParent(rootGO.transform, false);
            var frameRect = frameGO.GetComponent<RectTransform>();
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = new Vector2(1f, 1f);
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = new Vector2(0f, -16f);
            slot.frameImage = frameGO.AddComponent<Image>();
            slot.frameImage.color = normalFrameColor;

            // Portrait
            var portraitGO = new GameObject("Portrait", typeof(RectTransform));
            portraitGO.transform.SetParent(frameGO.transform, false);
            var portraitRect = portraitGO.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(2, 2);
            portraitRect.offsetMax = new Vector2(-2, -2);
            slot.portraitImage = portraitGO.AddComponent<RawImage>();

            if (caster.Portrait != null)
                slot.portraitImage.texture = caster.Portrait;
            else
            {
                slot.portraitImage.texture = null;
                slot.portraitImage.color = caster.FactionColor;
            }

            // Health bar background
            var hpBgGO = new GameObject("HealthBarBg", typeof(RectTransform));
            hpBgGO.transform.SetParent(rootGO.transform, false);
            var hpBgRect = hpBgGO.GetComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 0f);
            hpBgRect.anchorMax = new Vector2(1f, 0f);
            hpBgRect.pivot = new Vector2(0f, 0f);
            hpBgRect.anchoredPosition = Vector2.zero;
            hpBgRect.sizeDelta = new Vector2(0f, 14f);
            slot.healthBarBg = hpBgGO.AddComponent<Image>();
            slot.healthBarBg.color = healthBarBackgroundColor;

            // Health bar fill
            var hpFillGO = new GameObject("HealthBarFill", typeof(RectTransform));
            hpFillGO.transform.SetParent(hpBgGO.transform, false);
            var hpFillRect = hpFillGO.GetComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = new Vector2(1f, 1f);
            hpFillRect.pivot = new Vector2(0f, 0.5f);
            hpFillRect.offsetMin = new Vector2(1, 1);
            hpFillRect.offsetMax = new Vector2(-1, -1);
            slot.healthBarFill = hpFillGO.AddComponent<Image>();
            slot.healthBarFill.color = healthBarColor;

            // Name label
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(hpBgGO.transform, false);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(2, 0);
            labelRect.offsetMax = new Vector2(-2, 0);
            slot.nameLabel = labelGO.AddComponent<UITextComponent>();
            slot.nameLabel.fontSize = 9;
            slot.nameLabel.color = Color.white;
#if TMP_PRESENT
            slot.nameLabel.alignment = TMPro.TextAlignmentOptions.Center;
            slot.nameLabel.enableWordWrapping = false;
            slot.nameLabel.overflowMode = TMPro.TextOverflowModes.Truncate;
#else
            slot.nameLabel.alignment = TextAnchor.MiddleCenter;
            slot.nameLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            slot.nameLabel.verticalOverflow = VerticalWrapMode.Truncate;
#endif
            slot.nameLabel.text = displayName;

            // Click handler — uses a dedicated external index sentinel
            var clickHandler = rootGO.AddComponent<PortraitClickHandler>();
            clickHandler.InitializeExternal(this, caster);

            return slot;
        }

        #endregion

        #region Update Loop

        private void HandleAgentHealthChanged(int agentIndex, float newHealth, float maxHealth)
        {
            // Early refresh on next frame
            _nextUpdateTime = Mathf.Min(_nextUpdateTime, Time.time + 0.05f);
        }

        /// <summary>
        /// Scans the agent list for crowd indices that have spawned agents but no
        /// portrait slot yet, and creates slots for them on the fly.
        /// Only called when <see cref="portraitVisibility"/> is
        /// <see cref="PortraitVisibilityMode.ShowOnceSpawned"/>.
        /// </summary>
        private void CreateSlotsForNewlySpawnedCrowds(List<CrowdAgent> agents, int agentCount)
        {
            var crowds = crowdController.EnemyCrowds;
            if (crowds == null) return;

            for (int i = 0; i < agentCount; i++)
            {
                var agent = agents[i];
                if (agent == null) continue;

                int crowdIndex = agent.enemyCrowdIndex;
                if (_slottedCrowdIndices.Contains(crowdIndex))
                    continue;

                // New crowd index — verify it belongs to a valid, player-controlled crowd
                if (crowdIndex < 0 || crowdIndex >= crowds.Count)
                    continue;

                EnemyCrowd crowd = crowds[crowdIndex];
                if (crowd == null) continue;

                if (rtsController != null && !IsPlayerControlled(crowd))
                    continue;

                var slot = CreateSlot(crowdIndex, crowd);
                _slots.Add(slot);
                _slottedCrowdIndices.Add(crowdIndex);
            }
        }

        private void UpdateAllSlots()
        {
            if (crowdController == null) return;

            var agents = crowdController.GetAgents();
            int agentCount = agents != null ? agents.Count : 0;

            // In ShowOnceSpawned mode, check for newly spawned crowds that need portrait slots
            if (portraitVisibility == PortraitVisibilityMode.ShowOnceSpawned && agentCount > 0)
                CreateSlotsForNewlySpawnedCrowds(agents, agentCount);

            // Gather selected indices into a HashSet for fast lookup
            HashSet<int> selectedCrowdIndices = null;
            if (rtsController != null && rtsController.SelectedAgents != null)
            {
                selectedCrowdIndices = new HashSet<int>();
                var selected = rtsController.SelectedAgents;
                for (int i = 0; i < selected.Count; i++)
                {
                    int idx = selected[i];
                    if (idx >= 0 && idx < agentCount)
                    {
                        var agent = agents[idx];
                        if (agent != null)
                            selectedCrowdIndices.Add(agent.enemyCrowdIndex);
                    }
                }
            }

            float now = Time.time;

            for (int s = _slots.Count - 1; s >= 0; s--)
            {
                var slot = _slots[s];

                // ── External skill caster slot ──────────────────────────
                if (slot.IsExternal)
                {
                    var caster = slot.externalCaster;
                    slot.averageHealth = caster.HealthPercent;
                    slot.isDead = !caster.IsAlive;
                    slot.isUnderAttack = caster.IsUnderAttack;
                    slot.isSelected = false;

                    // Check if this caster is in the selected externals list
                    if (rtsController != null)
                    {
                        var selectedExt = rtsController.SelectedExternalAgents;
                        for (int i = 0; i < selectedExt.Count; i++)
                        {
                            if (ReferenceEquals(selectedExt[i], caster))
                            { slot.isSelected = true; break; }
                        }
                    }

                    if (slot.isDead && slot.deadSince < 0f)
                        slot.deadSince = now;
                    else if (!slot.isDead)
                        slot.deadSince = -1f;

                    if (removeDeadPortraits && slot.isDead && slot.deadSince > 0f &&
                        now - slot.deadSince >= deadRemovalDelay)
                    {
                        if (slot.root != null) Destroy(slot.root);
                        // External slots don't use _slottedCrowdIndices
                        _slots.RemoveAt(s);
                        continue;
                    }

                    ApplySlotVisuals(slot);
                    continue;
                }

                // ── Standard crowd slot ─────────────────────────────────
                // Compute average health and attack state
                float totalHealth = 0f;
                float totalMaxHealth = 0f;
                int livingCount = 0;
                int totalCount = 0;
                bool anyUnderAttack = false;

                for (int i = 0; i < agentCount; i++)
                {
                    var agent = agents[i];
                    if (agent == null || agent.enemyCrowdIndex != slot.enemyCrowdIndex)
                        continue;

                    totalCount++;

                    // Dead agents contribute 0 health but still count toward max
                    // so the average drops as agents die.
                    float agentMaxHealth = crowdController != null
                        ? crowdController.GetAgentMaxHealth(agent.index)
                        : agent.maxHealth;
                    if (agentMaxHealth <= 0f)
                        agentMaxHealth = Mathf.Max(1f, agent.maxHealth);
                    totalMaxHealth += agentMaxHealth;

                    if (!agent.isDead)
                    {
                        livingCount++;
                        totalHealth += agent.health;

                        // Under attack: took damage recently
                        if (now - agent.lastDamageTime < underAttackWindow)
                            anyUnderAttack = true;
                    }
                }

                // Average health ratio
                slot.averageHealth = totalMaxHealth > 0f ? totalHealth / totalMaxHealth : 0f;
                
                // Debug: Log health bar calculation when it seems wrong (health > 0 but bar < 90%)
                if (livingCount > 0 && slot.averageHealth < 0.9f && !slot._healthDebugLogged)
                {
                    // Find first agent for sample values
                    for (int dbg = 0; dbg < agentCount; dbg++)
                    {
                        var dbgAgent = agents[dbg];
                        if (dbgAgent != null && dbgAgent.enemyCrowdIndex == slot.enemyCrowdIndex && !dbgAgent.isDead)
                        {
                            Debug.Log($"[HealthBarDebug] CrowdPortraitBar '{slot.enemyCrowd?.name}': averageHealth={slot.averageHealth:F3} totalHealth={totalHealth:F1} totalMaxHealth={totalMaxHealth:F1} livingCount={livingCount} totalCount={totalCount} | Sample agent #{dbgAgent.index}: health={dbgAgent.health:F1} maxHealth={dbgAgent.maxHealth:F1} isDead={dbgAgent.isDead} factionBaseHealth={dbgAgent.enemyCrowd?.agentBaseHealth} factionHealthVariance={dbgAgent.enemyCrowd?.agentHealthVariance}");
                            slot._healthDebugLogged = true;
                            break;
                        }
                    }
                }
                
                slot.isUnderAttack = anyUnderAttack;
                slot.isDead = livingCount == 0 && totalCount > 0;
                slot.isSelected = selectedCrowdIndices != null && selectedCrowdIndices.Contains(slot.enemyCrowdIndex);

                // Track dead timestamp
                if (slot.isDead && slot.deadSince < 0f)
                    slot.deadSince = now;
                else if (!slot.isDead)
                    slot.deadSince = -1f;

                // Remove dead portrait after delay
                if (removeDeadPortraits && slot.isDead && slot.deadSince > 0f &&
                    now - slot.deadSince >= deadRemovalDelay)
                {
                    if (slot.root != null) Destroy(slot.root);
                    _slottedCrowdIndices.Remove(slot.enemyCrowdIndex);
                    _slots.RemoveAt(s);
                    continue;
                }

                // Update visuals
                ApplySlotVisuals(slot);
            }
        }

        private void ApplySlotVisuals(PortraitSlot slot)
        {
            // Health bar fill
            if (slot.healthBarFill != null)
            {
                var rt = slot.healthBarFill.rectTransform;
                rt.anchorMax = new Vector2(Mathf.Clamp01(slot.averageHealth), 1f);

                // Color: green → yellow → red
                slot.healthBarFill.color = slot.averageHealth > 0.5f
                    ? Color.Lerp(new Color(0.85f, 0.85f, 0.1f, 1f), healthBarColor, (slot.averageHealth - 0.5f) * 2f)
                    : Color.Lerp(new Color(0.85f, 0.15f, 0.1f, 1f), new Color(0.85f, 0.85f, 0.1f, 1f), slot.averageHealth * 2f);
            }

            // Frame color
            if (slot.frameImage != null)
            {
                if (slot.isUnderAttack && !slot.isDead)
                    slot.frameImage.color = underAttackFrameColor;
                else if (slot.isSelected && !slot.isDead)
                    slot.frameImage.color = selectedFrameColor;
                else if (slot.isDead)
                    slot.frameImage.color = normalFrameColor * 0.4f;
                else
                    slot.frameImage.color = normalFrameColor;
            }

            // Dead portrait: dark grayish tint
            if (slot.portraitImage != null)
            {
                slot.portraitImage.color = slot.isDead ? deadPortraitTint : Color.white;
            }
        }

        #endregion

        #region Click Handling

        private void OnSlotClicked(PortraitSlot slot)
        {
            if (rtsController == null || crowdController == null) return;
            if (slot.isDead) return;

            float now = Time.time;
            bool isDoubleClick = (now - slot.lastClickTime) < doubleClickTime;
            slot.lastClickTime = now;

            if (slot.IsExternal)
            {
                // External caster: select it via the RTS controller
                if (isDoubleClick)
                {
                    // Focus camera on the external caster
                    var casterTransform = slot.externalCaster.Transform;
                    if (casterTransform != null && _camera != null)
                    {
                        var cameraController = _camera.GetComponent<RTSCameraController>();
                        if (cameraController != null)
                            cameraController.FocusOnPosition(casterTransform.position);
                    }
                }
                rtsController.SelectExternalSelectable(slot.externalCaster);
                return;
            }

            if (isDoubleClick)
            {
                FocusCameraOnCrowd(slot.enemyCrowdIndex);
            }
            else
            {
                SelectCrowdAgents(slot.enemyCrowdIndex);
            }
        }

        /// <summary>
        /// Called by PortraitClickHandler when a slot is clicked.
        /// </summary>
        internal void OnSlotClickedByCrowdIndex(int crowdIndex)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsExternal && _slots[i].enemyCrowdIndex == crowdIndex)
                {
                    OnSlotClicked(_slots[i]);
                    return;
                }
            }
        }

        /// <summary>
        /// Called by <see cref="PortraitClickHandler"/> when an external-caster portrait is clicked.
        /// </summary>
        internal void OnSlotClickedByExternalCaster(IExternalSkillCaster caster)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsExternal && ReferenceEquals(_slots[i].externalCaster, caster))
                {
                    OnSlotClicked(_slots[i]);
                    return;
                }
            }
        }

        internal void ShowTooltipForCrowd(int crowdIndex)
        {
            if (tooltipPanel == null || tooltipText == null) return;

            EnemyCrowd crowd = null;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsExternal && _slots[i].enemyCrowdIndex == crowdIndex)
                {
                    crowd = _slots[i].enemyCrowd;
                    break;
                }
            }

            if (crowd == null) return;

            // Build tooltip: Enemy Type Name (localized) \n Faction Name (localized)
            string typeName = crowd.LocalizedEnemyTypeName;
            string factionName = crowd.LocalizedFactionName;
            tooltipText.text = $"{typeName}\n{factionName}";

            // Position tooltip above the cursor
            tooltipPanel.gameObject.SetActive(true);

#if ENABLE_INPUT_SYSTEM
            Vector3 mousePos = Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Input.mousePosition;
#else
            Vector3 mousePos = Input.mousePosition;
#endif
            tooltipPanel.position = new Vector3(mousePos.x, mousePos.y + 20f, 0f);

            // Force layout rebuild so ContentSizeFitter recalculates immediately
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipPanel);
        }

        internal void ShowTooltipForExternalCaster(IExternalSkillCaster caster)
        {
            if (tooltipPanel == null || tooltipText == null || caster == null) return;

            string typeName = caster.EnemyTypeName ?? "";
            string fName = caster.FactionName ?? "";
            tooltipText.text = $"{typeName}\n{fName}";

            tooltipPanel.gameObject.SetActive(true);

#if ENABLE_INPUT_SYSTEM
            Vector3 mousePos = Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Input.mousePosition;
#else
            Vector3 mousePos = Input.mousePosition;
#endif
            tooltipPanel.position = new Vector3(mousePos.x, mousePos.y + 20f, 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipPanel);
        }

        internal void HideTooltip()
        {
            if (tooltipPanel != null)
                tooltipPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Ensures the tooltip panel never blocks raycasts or intercepts pointer events.
        /// Without this, the tooltip appearing under the cursor causes OnPointerExit on the
        /// portrait slot → tooltip hides → OnPointerEnter fires again → rapid flicker.
        /// </summary>
        private void EnsureTooltipNonInteractive()
        {
            if (tooltipPanel == null) return;
            var cg = tooltipPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = tooltipPanel.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }

        private void SelectCrowdAgents(int enemyCrowdIndex)
        {
            // Use the RTS controller's SelectFaction which selects all living agents of a crowd index
            bool additive = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.leftCtrlKey.isPressed))
                additive = true;
#else
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.LeftControl))
                additive = true;
#endif
            rtsController.SelectFaction(enemyCrowdIndex, additive);
        }

        private void FocusCameraOnCrowd(int enemyCrowdIndex)
        {
            if (_camera == null) return;

            var cameraController = _camera.GetComponent<RTSCameraController>();
            if (cameraController == null) return;

            // First select them
            SelectCrowdAgents(enemyCrowdIndex);

            // Find the densest cluster center
            var agents = crowdController.GetAgents();
            var positions = new List<Vector3>();

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead || agent.enemyCrowdIndex != enemyCrowdIndex)
                    continue;
                if (agent.navigationAgent == null) continue;
                positions.Add(agent.WorldPosition);
            }

            if (positions.Count == 0) return;

            // Find the position with the most neighbors within focusClusterRadius
            Vector3 bestCenter = positions[0];
            int bestCount = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                int count = 0;
                for (int j = 0; j < positions.Count; j++)
                {
                    if ((positions[i] - positions[j]).sqrMagnitude <= focusClusterRadius * focusClusterRadius)
                        count++;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    bestCenter = positions[i];
                }
            }

            // Compute actual center of agents within cluster radius from best point
            Vector3 clusterCenter = Vector3.zero;
            int clusterCount = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                if ((positions[i] - bestCenter).sqrMagnitude <= focusClusterRadius * focusClusterRadius)
                {
                    clusterCenter += positions[i];
                    clusterCount++;
                }
            }

            if (clusterCount > 0)
            {
                clusterCenter /= clusterCount;
                cameraController.FocusOnPosition(clusterCenter);
            }
        }

        #endregion
    }

    /// <summary>
    /// Handles click and hover events on a portrait slot.
    /// Attached to each slot's root GameObject.
    /// </summary>
    internal class PortraitClickHandler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private EnemyCrowdPortraitBarUI _bar;
        private int _crowdIndex = -1;
        private IExternalSkillCaster _externalCaster;

        public void Initialize(EnemyCrowdPortraitBarUI bar, int crowdIndex)
        {
            _bar = bar;
            _crowdIndex = crowdIndex;
            _externalCaster = null;
        }

        public void InitializeExternal(EnemyCrowdPortraitBarUI bar, IExternalSkillCaster caster)
        {
            _bar = bar;
            _crowdIndex = -1;
            _externalCaster = caster;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_bar == null) return;
            if (_externalCaster != null)
                _bar.OnSlotClickedByExternalCaster(_externalCaster);
            else
                _bar.OnSlotClickedByCrowdIndex(_crowdIndex);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_bar == null) return;
            if (_externalCaster != null)
                _bar.ShowTooltipForExternalCaster(_externalCaster);
            else
                _bar.ShowTooltipForCrowd(_crowdIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_bar == null) return;
            _bar.HideTooltip();
        }
    }
}
