using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Arawn.EnemyMasses.Runtime.Damageables;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Attach to any GameObject to give it one or more <see cref="AgentSkill"/>s that can be:
    /// <list type="bullet">
    ///   <item>Selected via the <see cref="EnemyMassesRTSController"/> click / drag selection</item>
    ///   <item>Shown in the <see cref="UI.AgentSkillBarUI"/> when selected</item>
    ///   <item>Fired by the player (or via script) through the standard skill execution pipeline</item>
    ///   <item>Displayed as a portrait in <see cref="EnemyCrowdPortraitBarUI"/> if portrait data is provided</item>
    /// </list>
    ///
    /// <para><b>Setup:</b> Attach this component, assign one or more AgentSkill ScriptableObjects in the
    /// Skill Config, and optionally fill in the Portrait section.  The component auto-registers with the
    /// first <see cref="EnemyMassesRTSController"/> found at runtime.</para>
    ///
    /// <para><b>Limitations (v1):</b> Skill actions that reference <c>context.Caster</c> will see <c>null</c>.
    /// Effects using <c>BaseDamageMultiplier</c> mode yield 0 damage because there is no crowd agent with a
    /// base-damage stat.  Use <c>FixedValue</c> damage mode for reliable damage delivery.
    /// Effects that spawn VFX at the caster transform fall back to <see cref="SkillExecutionContext.TargetPosition"/>.</para>
    /// </summary>
    [AddComponentMenu("Enemy Masses/Standalone Skill Caster")]
    public class StandaloneSkillCaster : MonoBehaviour, IExternalSkillCaster, IRTSFogRevealer
    {
        #region ═══ PORTRAIT / IDENTITY ═══

        [Tooltip("Faction name shown in the portrait bar tooltip.")]
        [SerializeField] private string factionName = "";

        [Tooltip("Unit / type name shown in the portrait bar label.")]
        [SerializeField] private string enemyTypeName = "";

        [Tooltip("Manual: assign your own portrait texture.\nAutoGenerate: create a portrait from the GameObject in the editor.")]
        #pragma warning disable CS0414 // Serialized field read by custom editor / future use
        [SerializeField] private PortraitMode portraitMode = PortraitMode.Manual;
        #pragma warning restore CS0414

        [Tooltip("Portrait texture. If null, factionColor is used as a solid-color fallback.")]
        [SerializeField] private Texture2D portrait;

        [Tooltip("Fallback color for the portrait when no texture is assigned.")]
        [SerializeField] private Color factionColor = Color.white;

        [Tooltip("Show this caster in the portrait bar when portrait data is provided.")]
        [SerializeField] private bool showInPortraitBar = true;

        #endregion

        #region ═══ FOG OF WAR ═══

        [Tooltip("Enable fog of war reveal around this caster.\n" +
                 "Requires FogOfWarLite in the scene.")]
        [SerializeField] private bool revealFogOfWar;

        [Tooltip("Radius in world units for fog of war reveal.")]
        [SerializeField] private float fogRevealRadius = 10f;

        #endregion

        #region ═══ SELECTION / RTS ═══

        [Tooltip("Whether this unit is player-controllable (selectable by the RTS controller).")]
        [SerializeField] private bool isPlayerControlled = true;

        [Tooltip("Optional per-unit offset for the selection indicator ring.")]
        [SerializeField] private Vector3 selectionIndicatorOffset = Vector3.zero;

        [Tooltip("Optional prefab override for the selection indicator.")]
        [SerializeField] private GameObject selectionIndicatorOverride;

        [Tooltip("Bypass the player-faction filter so factionless units can be selected.")]
        [SerializeField] private bool skipFactionFilter = true;

        [Tooltip("Optional child GameObject that is shown/hidden as the selection indicator.\n" +
                 "If left empty and Auto Create Selection Ring is enabled, a default ring is created at runtime.")]
        [SerializeField] private GameObject selectionIndicator;

        [Tooltip("Automatically create a default selection ring at runtime if no Selection Indicator is assigned " +
                 "and no Selection Indicator Prefab is set on the RTS controller.")]
        [SerializeField] private bool autoCreateSelectionRing = true;

        [Tooltip("Radius of the auto-created selection ring.")]
        [SerializeField] private float selectionRingRadius = 0.6f;

        [Tooltip("Color of the auto-created selection ring.")]
        [SerializeField] private Color selectionRingColor = new Color(0f, 1f, 0f, 0.7f);

        [Tooltip("Y-offset of the auto-created selection ring above the pivot.")]
        [SerializeField] private float selectionRingYOffset = 0.05f;

        [Tooltip("Optional audio clip played when this unit is selected via the RTS controller.\n" +
                 "If left empty, no selection sound is played for this caster.")]
        [SerializeField] private AudioClip selectionAudioClip;

        #endregion

        #region ═══ SKILLS ═══

        [Tooltip("Skill system configuration — enable the system and add skill slots here.\n" +
                 "Mirrors the EnemyCrowd > Skill System inspector.")]
        [SerializeField] private SkillSystemConfig skillSystemConfig = new SkillSystemConfig();

        #endregion

        #region ═══ AUTO-CAST AI ═══

        [Tooltip("Maximum range (in world units) at which the auto-cast AI scans for targets.\n" +
                 "If a skill's Max Range is smaller, the skill range is used instead.")]
        [SerializeField] private float autocastScanRange = 30f;

        [Tooltip("How often (in seconds) the auto-cast AI evaluates which skill to fire.\n" +
                 "Lower = more responsive but slightly more CPU. Recommended: 0.3–1.0.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float autocastDecisionInterval = 0.5f;

        [Tooltip("Random ±variation added to the evaluation interval so multiple casters\n" +
                 "don't all fire on the same frame. 0 = perfectly regular.")]
        [Range(0f, 1f)]
        [SerializeField] private float autocastTimingVariation = 0.2f;

        #endregion

        #region ═══ HEALTH ═══

        [Tooltip("Current health of this unit. Used for portrait bar health display.")]
        [SerializeField] private float health = 100f;

        [Tooltip("Maximum health. Used to compute HealthPercent.")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Window in seconds — the portrait shows 'under attack' if damage was received within this window.")]
        [SerializeField] private float underAttackWindow = 2f;

        #endregion

        #region ═══ DEBUG ═══

        [SerializeField] private bool debugMode;

        #endregion

        // ─────────────────────────────────────────────────────────────────
        // Runtime state
        // ─────────────────────────────────────────────────────────────────

        private EnemyMassesRTSController _rtsController;
        private EnemyMassesCrowdController _crowdController;
        private NavMeshAgent _navAgent;
        private float _lastDamageTime = -999f;
        private float[] _cooldownEndTimes;
        private bool _isAlive = true;
        private bool _isSelected;
        private GameObject _autoCreatedRing;
        private float _nextAutocastEvalTime;
        private bool _autocastDefaultsApplied;
        private static readonly Collider[] _overlapBuffer = new Collider[64];
        private CrowdAgent _cachedNearestAgent;
        private IEnemyMassesDamageable _cachedNearestDamageable;
        private float _cachedTargetScanRange;
        private int _cachedTargetScanFrame = -1;

        // ─────────────────────────────────────────────────────────────────
        // IExternalSkillCaster — portrait data
        // ─────────────────────────────────────────────────────────────────

        public string FactionName => factionName;
        public string EnemyTypeName => enemyTypeName;
        public Texture2D Portrait => portrait;
        public Color FactionColor => factionColor;
        public bool ShowInPortraitBar => showInPortraitBar &&
                                          (!string.IsNullOrEmpty(factionName) ||
                                           !string.IsNullOrEmpty(enemyTypeName) ||
                                           portrait != null);

        // ─────────────────────────────────────────────────────────────────
        // IExternalSkillCaster — skill data
        // ─────────────────────────────────────────────────────────────────

        public SkillSystemConfig SkillConfig => skillSystemConfig;
        public IReadOnlyList<AgentSkillSlot> SkillSlots => skillSystemConfig?.skillSlots;

        // ─────────────────────────────────────────────────────────────────
        // IExternalSkillCaster — live state
        // ─────────────────────────────────────────────────────────────────

        public float HealthPercent => maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
        public bool IsUnderAttack => Time.time - _lastDamageTime < underAttackWindow;
        float IExternalSkillCaster.UnderAttackWindow => underAttackWindow;

        // ─────────────────────────────────────────────────────────────────
        // IRTSFogRevealer
        // ─────────────────────────────────────────────────────────────────

        public bool RevealFogOfWar => revealFogOfWar;
        public Vector3 FogRevealPosition => transform.position;
        public float FogRevealRadius => revealFogOfWar ? fogRevealRadius : 0f;

        // ─────────────────────────────────────────────────────────────────
        // IRTSExternalSelectable
        // ─────────────────────────────────────────────────────────────────

        public NavMeshAgent NavAgent => _navAgent;
        public Transform Transform => transform;
        public Vector3 SelectionIndicatorOffset => selectionIndicatorOffset;
        public bool IsSelectable => isPlayerControlled && _isAlive;
        public bool IsAlive => _isAlive;
        public bool SkipFactionFilter => skipFactionFilter;
        public GameObject SelectionIndicatorOverride => selectionIndicatorOverride;
        public AudioClip SelectionAudioClip => selectionAudioClip;

        /// <summary>
        /// Called by the RTS controller when a move command is issued on this unit.
        /// Routes to NavMeshAgent if present; override in subclass for custom movement.
        /// </summary>
        public virtual void SetDestination(Vector3 destination)
        {
            if (_navAgent != null && _navAgent.isOnNavMesh)
            {
                _navAgent.isStopped = false;
                _navAgent.SetDestination(destination);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────

        protected virtual void Awake()
        {
            _navAgent = GetComponent<NavMeshAgent>();
            EnsureCooldownArray();
        }

        protected virtual void Start()
        {
            AutoResolveReferences();
            Register();
            SubscribeToSelection();
            SetupSelectionIndicator();
            InitializeAutocastDefaults();

            if (revealFogOfWar)
                RTSWorldUnitRegistry.RegisterFogRevealer(this);
        }

        protected virtual void Update()
        {
            if (!_isAlive) return;
            TickAutocast();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeFromSelection();
            Unregister();
            RTSWorldUnitRegistry.UnregisterFogRevealer(this);
            if (_autoCreatedRing != null)
                Destroy(_autoCreatedRing);
        }

        // ─────────────────────────────────────────────────────────────────
        // Auto-cast AI
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies <see cref="AutocastEligibility.DefaultOn"/> initial state
        /// for each skill slot — mirrors what <c>AgentSkillBarUI</c> does on
        /// first display.  Safe to call more than once (idempotent).
        /// </summary>
        private void InitializeAutocastDefaults()
        {
            if (_autocastDefaultsApplied) return;
            _autocastDefaultsApplied = true;

            if (skillSystemConfig?.skillSlots == null) return;

            for (int i = 0; i < skillSystemConfig.skillSlots.Count; i++)
            {
                var slot = skillSystemConfig.skillSlots[i];
                if (slot?.skill == null) continue;

                if (slot.skill.Autocast == AutocastEligibility.DefaultOn)
                {
                    slot.overrideControlMode = true;
                    slot.controlModeOverride = SkillControlMode.Hybrid;
                }
                // DefaultOff / NotEligible — leave at the defaults (off).
            }
        }

        /// <summary>
        /// Called every frame from <see cref="Update"/>. Evaluates auto-cast
        /// eligible skill slots at the configured decision interval and fires
        /// the highest-priority slot whose cooldown is ready and that has a
        /// valid target.
        /// </summary>
        private void TickAutocast()
        {
            if (skillSystemConfig?.skillSlots == null || skillSystemConfig.skillSlots.Count == 0)
                return;

            float currentTime = Time.time;
            if (currentTime < _nextAutocastEvalTime)
                return;

            // Schedule next evaluation with optional jitter
            float variation = autocastTimingVariation > 0f
                ? UnityEngine.Random.Range(-autocastTimingVariation, autocastTimingVariation)
                    * autocastDecisionInterval
                : 0f;
            _nextAutocastEvalTime = currentTime + autocastDecisionInterval + variation;

            // Invalidate target cache so the first HasAutocastTarget call this tick
            // triggers a fresh scan, and subsequent calls reuse the result.
            _cachedTargetScanFrame = -1;

            // --- Pick the best auto-cast slot ---
            int bestSlotIndex = -1;
            int bestPriority = int.MinValue;

            for (int i = 0; i < skillSystemConfig.skillSlots.Count; i++)
            {
                var slot = skillSystemConfig.skillSlots[i];
                if (slot?.skill == null || !slot.enabled) continue;

                // Only consider slots with auto-cast toggled on
                if (!slot.overrideControlMode || slot.controlModeOverride != SkillControlMode.Hybrid)
                    continue;
                if (slot.skill.Autocast == AutocastEligibility.NotEligible)
                    continue;

                // Cooldown check
                EnsureCooldownArray();
                if (i < _cooldownEndTimes.Length && _cooldownEndTimes[i] > currentTime)
                    continue;

                // Quick target availability check (uses cached scan)
                if (!HasAutocastTarget(slot.skill))
                    continue;

                int priority = slot.overridePriority ? slot.priorityOverride : slot.skill.Priority;
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestSlotIndex = i;
                }
            }

            if (bestSlotIndex < 0) return;

            var bestSkill = skillSystemConfig.skillSlots[bestSlotIndex].skill;

            // For GroundTargetedAOE, synthesize a ground target from the cached scan
            Vector3? groundTarget = null;
            if (bestSkill.TargetingType == SkillTargetingType.GroundTargetedAOE)
            {
                if (_cachedNearestAgent != null)
                    groundTarget = _cachedNearestAgent.WorldPosition;
                else if (_cachedNearestDamageable != null && _cachedNearestDamageable.DamageTransform != null)
                    groundTarget = _cachedNearestDamageable.DamageTransform.position;
                else
                    return;
            }

            // Reuse cached results: prefer crowd agent (handled by TryUseSkill's
            // ResolveTargets), fall back to damageable target.
            IEnemyMassesDamageable damageableTarget =
                _cachedNearestAgent == null ? _cachedNearestDamageable : null;

            bool fired = TryUseSkill(bestSlotIndex, -1, groundTarget, _crowdController, damageableTarget);

            if (fired && debugMode)
                Debug.Log($"[StandaloneSkillCaster] Auto-cast fired {bestSkill.SkillName} on {gameObject.name}");
        }

        /// <summary>
        /// Lightweight check: is there at least one valid target for the given
        /// skill within its effective range? Checks both CrowdAgents and
        /// <see cref="IEnemyMassesDamageable"/> objects in the physics world.
        /// Results are cached for the current evaluation tick so multiple
        /// slot checks don't re-scan.
        /// </summary>
        private bool HasAutocastTarget(AgentSkill skill)
        {
            // Skills that don't strictly need an external target
            switch (skill.TargetingType)
            {
                case SkillTargetingType.Self:
                case SkillTargetingType.AOEAroundSelf:
                    return true;
            }

            float range = Mathf.Min(skill.MaxRange, autocastScanRange);
            RefreshTargetCache(range);

            return _cachedNearestAgent != null || _cachedNearestDamageable != null;
        }

        /// <summary>
        /// Performs the actual target scan at most once per evaluation tick
        /// (per range value). Populates <see cref="_cachedNearestAgent"/>
        /// and <see cref="_cachedNearestDamageable"/>.
        /// </summary>
        private void RefreshTargetCache(float range)
        {
            int frame = Time.frameCount;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (_cachedTargetScanFrame == frame && _cachedTargetScanRange == range)
                return; // already scanned this tick at this range

            _cachedTargetScanFrame = frame;
            _cachedTargetScanRange = range;

            _cachedNearestAgent = (_crowdController != null) ? FindNearestCrowdAgent(range) : null;
            _cachedNearestDamageable = (_cachedNearestAgent == null) ? FindNearestDamageable(range) : null;
        }

        /// <summary>
        /// Finds the nearest living crowd agent within <paramref name="range"/>.
        /// Used by the auto-cast AI for quick target availability checks.
        /// </summary>
        private CrowdAgent FindNearestCrowdAgent(float range)
        {
            if (_crowdController == null) return null;
            var allAgents = _crowdController.GetAgents();
            if (allAgents == null) return null;

            float bestSqr = range * range;
            CrowdAgent best = null;
            Vector3 pos = transform.position;

            for (int i = 0; i < allAgents.Count; i++)
            {
                var a = allAgents[i];
                if (a == null || a.isDead) continue;
                float sqr = (a.WorldPosition - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = a;
                }
            }
            return best;
        }

        /// <summary>
        /// Finds the nearest living <see cref="IEnemyMassesDamageable"/> within
        /// <paramref name="range"/> using a physics overlap sphere.
        /// Excludes this caster's own GameObject.
        /// </summary>
        private IEnemyMassesDamageable FindNearestDamageable(float range)
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, range, _overlapBuffer);
            float bestSqr = range * range;
            IEnemyMassesDamageable best = null;
            Vector3 pos = transform.position;

            for (int i = 0; i < count; i++)
            {
                var col = _overlapBuffer[i];
                if (col == null) continue;

                var damageable = col.GetComponentInParent<IEnemyMassesDamageable>();
                if (damageable == null || !damageable.IsAlive) continue;

                // Don't target ourselves
                if (damageable.DamageTransform == transform) continue;

                float sqr = (damageable.DamageTransform.position - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = damageable;
                }
            }

            return best;
        }

        // ─────────────────────────────────────────────────────────────────
        // Registration
        // ─────────────────────────────────────────────────────────────────

        private void AutoResolveReferences()
        {
            if (_rtsController == null)
            {
#if UNITY_2023_1_OR_NEWER
                _rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
#else
                _rtsController = FindObjectOfType<EnemyMassesRTSController>();
#endif
            }

            if (_crowdController == null && _rtsController != null)
                _crowdController = _rtsController.crowdController;

            if (_crowdController == null)
            {
#if UNITY_2023_1_OR_NEWER
                _crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
#else
                _crowdController = FindObjectOfType<EnemyMassesCrowdController>();
#endif
            }
        }

        private void Register()
        {
            if (_rtsController != null)
            {
                _rtsController.RegisterExternalSelectable(this);

                if (debugMode)
                    Debug.Log($"[StandaloneSkillCaster] Registered with RTS controller: {gameObject.name}");
            }
        }

        private void Unregister()
        {
            if (_rtsController != null)
            {
                _rtsController.UnregisterExternalSelectable(this);

                if (debugMode)
                    Debug.Log($"[StandaloneSkillCaster] Unregistered from RTS controller: {gameObject.name}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Selection indicator
        // ─────────────────────────────────────────────────────────────────

        private void SubscribeToSelection()
        {
            if (_rtsController != null)
                _rtsController.SelectionChanged += OnRTSSelectionChanged;
        }

        private void UnsubscribeFromSelection()
        {
            if (_rtsController != null)
                _rtsController.SelectionChanged -= OnRTSSelectionChanged;
        }

        private void OnRTSSelectionChanged(SelectionChangedEventData data)
        {
            if (_rtsController == null) return;

            bool wasSelected = _isSelected;
            _isSelected = false;

            var selectedExternals = _rtsController.SelectedExternalAgents;
            for (int i = 0; i < selectedExternals.Count; i++)
            {
                if (ReferenceEquals(selectedExternals[i], this))
                {
                    _isSelected = true;
                    break;
                }
            }

            if (_isSelected != wasSelected)
                UpdateSelectionIndicatorVisual();
        }

        private void SetupSelectionIndicator()
        {
            // If the user already assigned a selection indicator, just make sure it starts hidden
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(false);
                return;
            }

            // Skip auto-creation if disabled
            if (!autoCreateSelectionRing) return;

            // Also skip if the RTS controller already has a prefab-based indicator system
            if (_rtsController != null && _rtsController.selectionIndicatorPrefab != null) return;

            // Auto-create a simple selection ring using a LineRenderer
            _autoCreatedRing = new GameObject("SelectionRing");
            _autoCreatedRing.transform.SetParent(transform, false);
            _autoCreatedRing.transform.localPosition = new Vector3(0f, selectionRingYOffset, 0f);

            var lineRenderer = _autoCreatedRing.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.receiveShadows = false;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.startWidth = 0.04f;
            lineRenderer.endWidth = 0.04f;
            lineRenderer.startColor = selectionRingColor;
            lineRenderer.endColor = selectionRingColor;
            lineRenderer.alignment = LineAlignment.TransformZ;
            lineRenderer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Use default particle/sprite material
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.material.color = selectionRingColor;

            // Build circle points
            const int segments = 32;
            lineRenderer.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * selectionRingRadius;
                float y = Mathf.Sin(angle) * selectionRingRadius;
                lineRenderer.SetPosition(i, new Vector3(x, y, 0f));
            }

            selectionIndicator = _autoCreatedRing;
            _autoCreatedRing.SetActive(false);

            if (debugMode)
                Debug.Log($"[StandaloneSkillCaster] Auto-created selection ring for {gameObject.name}");
        }

        private void UpdateSelectionIndicatorVisual()
        {
            if (selectionIndicator != null)
                selectionIndicator.SetActive(_isSelected);

            if (debugMode)
                Debug.Log($"[StandaloneSkillCaster] {gameObject.name} selection: {_isSelected}");
        }

        // ─────────────────────────────────────────────────────────────────
        // Cooldown management
        // ─────────────────────────────────────────────────────────────────

        private void EnsureCooldownArray()
        {
            int count = skillSystemConfig?.skillSlots?.Count ?? 0;
            if (_cooldownEndTimes == null || _cooldownEndTimes.Length != count)
                _cooldownEndTimes = new float[count];
        }

        public float GetCooldownRemaining(int slotIndex)
        {
            EnsureCooldownArray();
            if (slotIndex < 0 || slotIndex >= _cooldownEndTimes.Length) return 0f;
            return Mathf.Max(0f, _cooldownEndTimes[slotIndex] - Time.time);
        }

        public float GetCooldownTotal(int slotIndex)
        {
            if (skillSystemConfig?.skillSlots == null) return 0f;
            if (slotIndex < 0 || slotIndex >= skillSystemConfig.skillSlots.Count) return 0f;
            return skillSystemConfig.skillSlots[slotIndex].EffectiveCooldown;
        }

        public bool IsSkillOnCooldown(int slotIndex)
        {
            return GetCooldownRemaining(slotIndex) > 0f;
        }

        // ─────────────────────────────────────────────────────────────────
        // Skill execution
        // ─────────────────────────────────────────────────────────────────

        public bool TryUseSkill(int slotIndex, int targetAgentIndex, Vector3? groundTarget,
            EnemyMassesCrowdController controller, IEnemyMassesDamageable damageableTarget = null)
        {
            if (skillSystemConfig?.skillSlots == null) return false;
            if (slotIndex < 0 || slotIndex >= skillSystemConfig.skillSlots.Count) return false;

            var slot = skillSystemConfig.skillSlots[slotIndex];
            if (slot?.skill == null || !slot.enabled) return false;

            // Cooldown check
            EnsureCooldownArray();
            if (_cooldownEndTimes[slotIndex] > Time.time)
            {
                if (debugMode)
                    Debug.Log($"[StandaloneSkillCaster] Skill {slot.skill.SkillName} on cooldown " +
                        $"({GetCooldownRemaining(slotIndex):F1}s remaining)");
                return false;
            }

            var ctrl = controller ?? _crowdController;
            var skill = slot.skill;

            // Resolve targets
            var targets = ResolveTargets(skill, targetAgentIndex, groundTarget, ctrl);

            // GroundTargetedAOE can fire on empty ground; damageable target also counts;
            // Projectile skills resolve targets via the projectile hit callback system
            if (targets.Count == 0 && damageableTarget == null
                && skill.TargetingType != SkillTargetingType.GroundTargetedAOE
                && skill.TargetingType != SkillTargetingType.Projectile)
            {
                if (debugMode)
                    Debug.Log($"[StandaloneSkillCaster] No valid targets for {skill.SkillName}");
                return false;
            }

            // Resolve target position
            Vector3 resolvedTargetPosition;
            if (targets.Count > 0)
                resolvedTargetPosition = targets[0].WorldPosition;
            else if (damageableTarget != null && damageableTarget.DamageTransform != null)
                resolvedTargetPosition = damageableTarget.DamageTransform.position;
            else if (groundTarget.HasValue)
                resolvedTargetPosition = groundTarget.Value;
            else
                resolvedTargetPosition = transform.position;

            // Critical hit
            bool isCritical = UnityEngine.Random.value < skill.CriticalChance;
            float effectMultiplier = slot.damageMultiplier * (isCritical ? skill.CriticalMultiplier : 1f);

            // Build execution context (Caster = null for standalone caster;
            // CasterTransform provides the fallback position for projectile spawning)
            var context = new SkillExecutionContext
            {
                Caster = null,
                CasterTransform = transform,
                PrimaryTarget = targets.Count > 0 ? targets[0] : null,
                AllTargets = targets.ToArray(),
                TargetPosition = resolvedTargetPosition,
                Skill = skill,
                Controller = ctrl,
                IsCritical = isCritical,
                EffectMultiplier = effectMultiplier,
                ExecutionId = UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                NetworkProjectileId = 0,
                DamageableTarget = damageableTarget
            };

            bool isProjectileTargeting = skill.TargetingType == SkillTargetingType.Projectile;

            // OnCast actions always fire immediately
            SkillExecutor.ExecuteActions(skill.OnCastActions, context);

            if (isProjectileTargeting)
            {
                // Projectile skills: spawn a real projectile that travels from caster to target.
                // OnHit and OnFinish actions are deferred until the projectile impact callback.
                SkillExecutor.FireProjectileWithEffects(skill, context);

                // If the skill also has a visual arc (separate from Projectile targeting), fire it too
                if (skill.HasProjectileArc)
                    SkillExecutor.FireProjectileArc(skill.ProjectileArc, context);
            }
            else
            {
                // Non-projectile skills: execute hit and finish actions immediately
                SkillExecutor.ExecuteActions(skill.OnHitActions, context);
                SkillExecutor.ExecuteActions(skill.OnFinishActions, context);

                // Fire visual-only projectile arc if configured
                if (skill.HasProjectileArc)
                    SkillExecutor.FireProjectileArc(skill.ProjectileArc, context);
            }

            // Start cooldown
            _cooldownEndTimes[slotIndex] = Time.time + slot.EffectiveCooldown;

            if (debugMode)
                Debug.Log($"[StandaloneSkillCaster] Fired {skill.SkillName} — " +
                    $"{targets.Count} target(s), crit={isCritical}, cooldown={slot.EffectiveCooldown:F1}s");

            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Target resolution (simplified — no CrowdAgent caster)
        // ─────────────────────────────────────────────────────────────────

        private List<CrowdAgent> ResolveTargets(AgentSkill skill, int targetAgentIndex,
            Vector3? groundTarget, EnemyMassesCrowdController controller)
        {
            var targets = new List<CrowdAgent>();
            if (controller == null) return targets;

            var allAgents = controller.GetAgents();
            if (allAgents == null) return targets;

            // Explicit target agent
            if (targetAgentIndex >= 0 && targetAgentIndex < allAgents.Count)
            {
                var target = allAgents[targetAgentIndex];
                if (target != null && !target.isDead)
                {
                    targets.Add(target);

                    // For AOE around enemy, also gather agents near the target
                    if (skill.TargetingType == SkillTargetingType.AOEAroundEnemy)
                    {
                        GatherAgentsInRadius(allAgents, target.WorldPosition, skill.AOERadius,
                            skill.MaxTargets, targets, target);
                    }
                }
                return targets;
            }

            Vector3 origin = groundTarget ?? transform.position;

            switch (skill.TargetingType)
            {
                case SkillTargetingType.Self:
                    // No crowd agent caster — self-targeted skills have limited use
                    break;

                case SkillTargetingType.ClosestEnemy:
                case SkillTargetingType.SingleEnemy:
                case SkillTargetingType.Hitscan:
                    var nearest = FindNearestAgent(allAgents, origin, skill.MaxRange, deadFilter: true);
                    if (nearest != null) targets.Add(nearest);
                    break;

                case SkillTargetingType.AllEnemiesInRange:
                    GatherAgentsInRadius(allAgents, origin, skill.MaxRange, skill.MaxTargets, targets);
                    break;

                case SkillTargetingType.AOEAroundSelf:
                    GatherAgentsInRadius(allAgents, transform.position, skill.AOERadius, skill.MaxTargets, targets);
                    break;

                case SkillTargetingType.GroundTargetedAOE:
                    if (groundTarget.HasValue)
                    {
                        GatherAgentsInRadius(allAgents, groundTarget.Value, skill.AOERadius, skill.MaxTargets, targets);
                    }
                    break;

                case SkillTargetingType.Cone:
                    GatherAgentsInCone(allAgents, transform.position, transform.forward,
                        skill.MaxRange, skill.ConeAngle, skill.MaxTargets, targets);
                    break;

                case SkillTargetingType.Projectile:
                    // Resolve closest target for the projectile to aim at
                    var projectileTarget = FindNearestAgent(allAgents, origin, skill.MaxRange, deadFilter: true);
                    if (projectileTarget != null) targets.Add(projectileTarget);
                    break;

                default:
                    // Channeled, etc — let skill actions handle internally
                    break;
            }

            return targets;
        }

        private static CrowdAgent FindNearestAgent(IReadOnlyList<CrowdAgent> agents, Vector3 origin,
            float maxRange, bool deadFilter)
        {
            float bestDistSqr = maxRange * maxRange;
            CrowdAgent best = null;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null) continue;
                if (deadFilter && agent.isDead) continue;

                float distSqr = (agent.WorldPosition - origin).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = agent;
                }
            }

            return best;
        }

        private static void GatherAgentsInRadius(IReadOnlyList<CrowdAgent> agents, Vector3 center,
            float radius, int maxTargets, List<CrowdAgent> results, CrowdAgent exclude = null)
        {
            float radiusSqr = radius * radius;
            int limit = maxTargets > 0 ? maxTargets : int.MaxValue;

            for (int i = 0; i < agents.Count && results.Count < limit; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead) continue;
                if (agent == exclude) continue;
                if (results.Contains(agent)) continue;

                float distSqr = (agent.WorldPosition - center).sqrMagnitude;
                if (distSqr <= radiusSqr)
                    results.Add(agent);
            }
        }

        private static void GatherAgentsInCone(IReadOnlyList<CrowdAgent> agents, Vector3 origin,
            Vector3 forward, float range, float coneAngle, int maxTargets, List<CrowdAgent> results)
        {
            float rangeSqr = range * range;
            float halfAngle = coneAngle * 0.5f;
            int limit = maxTargets > 0 ? maxTargets : int.MaxValue;

            for (int i = 0; i < agents.Count && results.Count < limit; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.isDead) continue;

                Vector3 toAgent = agent.WorldPosition - origin;
                if (toAgent.sqrMagnitude > rangeSqr) continue;

                float angle = Vector3.Angle(forward, toAgent);
                if (angle <= halfAngle)
                    results.Add(agent);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Public API for gameplay scripts
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies damage to this unit from an external source.
        /// Updates health and triggers the "under attack" visual on the portrait bar.
        /// </summary>
        /// <param name="amount">Raw damage amount (positive).</param>
        /// <returns>True if the unit was killed by this damage.</returns>
        public bool ApplyDamage(float amount)
        {
            if (!_isAlive || amount <= 0f) return false;

            health = Mathf.Max(0f, health - amount);
            _lastDamageTime = Time.time;

            if (health <= 0f)
            {
                _isAlive = false;
                if (debugMode) Debug.Log($"[StandaloneSkillCaster] {gameObject.name} killed!");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Heals this unit.
        /// </summary>
        public void Heal(float amount)
        {
            if (!_isAlive || amount <= 0f) return;
            health = Mathf.Min(maxHealth, health + amount);
        }

        /// <summary>
        /// Revives the unit at the given health fraction (0–1).
        /// </summary>
        public void Revive(float healthFraction = 1f)
        {
            _isAlive = true;
            health = maxHealth * Mathf.Clamp01(healthFraction);
        }

        /// <summary>
        /// Gets or sets the current health directly.
        /// </summary>
        public float Health
        {
            get => health;
            set => health = Mathf.Clamp(value, 0f, maxHealth);
        }

        /// <summary>
        /// Gets or sets the maximum health.
        /// </summary>
        public float MaxHealth
        {
            get => maxHealth;
            set => maxHealth = Mathf.Max(1f, value);
        }

        /// <summary>
        /// Whether this unit is player-controlled (can be selected).
        /// </summary>
        public bool IsPlayerControlled
        {
            get => isPlayerControlled;
            set => isPlayerControlled = value;
        }

        /// <summary>
        /// Force use a skill by slot index. Convenience shortcut for <see cref="TryUseSkill"/>.
        /// </summary>
        public bool UseSkill(int slotIndex, int targetAgentIndex = -1, Vector3? groundTarget = null)
        {
            return TryUseSkill(slotIndex, targetAgentIndex, groundTarget, _crowdController);
        }
    }
}
