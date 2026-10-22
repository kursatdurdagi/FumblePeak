using System.Collections.Generic;
using EnemyMasses.VFX;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Combat-focused zone that periodically applies status effects or heals in an AoE.
    /// Supports on/off cycles, faction filters, and manual start/stop controls.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Enemy Masses/Combat/Combat Effect Zone")]
    public class EnemyMassesCombatEffectZone : MonoBehaviour
    {
        public enum CombatZoneEffectMode
        {
            StatusEffect,
            InstantHeal
        }

        public enum CombatZoneFilterMode
        {
            AllFactions,
            OnlyListedFactions,
            ExcludeListedFactions
        }

        [System.Serializable]
        public class ZoneAudioCue
        {
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
            [Range(0f, 0.5f)] public float pitchVariation = 0.05f;
            [Range(0f, 1f)] public float spatialBlend = 1f;
            [Min(0f)] public float minDistance = 1f;
            [Min(0f)] public float maxDistance = 50f;
            [Tooltip("Max concurrent plays for this clip (0 = use global default).")]
            public int maxConcurrent = 0;

            public bool HasClip => clip != null;
        }

        [Header("Controller")]
        [Tooltip("Optional crowd controller. If empty, uses EnemyMassesCrowdController.Instance.")]
        public EnemyMassesCrowdController crowdController;

        [Header("Zone")]
        [Tooltip("Radius of the combat zone")]
        public float radius = 6f;

        [Header("Timing")]
        [Tooltip("Start the zone automatically on Start")]
        public bool startOnStart = true;

        [Tooltip("Delay before the first activation")]
        public float startDelay = 0f;

        [Tooltip("How long the zone stays active. 0 = always active.")]
        public float activeDuration = 3f;

        [Tooltip("Time between activations when using an active duration")]
        public float inactiveDuration = 10f;

        [Tooltip("How often to apply the effect while active (seconds)")]
        public float applyInterval = 1f;

        [Header("Effect")]
        [Tooltip("How the zone applies its effect")]
        public CombatZoneEffectMode effectMode = CombatZoneEffectMode.StatusEffect;

        [Tooltip("Status effect to apply")]
        public StatusEffectType effectType = StatusEffectType.Regeneration;

        [Tooltip("Duration of the status effect")]
        public float effectDuration = 5f;

        [Tooltip("Effect value (damage/heal per tick, or multiplier for buffs)")]
        public float effectValue = 5f;

        [Tooltip("Tick interval for DOT/HOT effects")]
        public float effectTickInterval = 1f;

        [Tooltip("Instant heal amount (Instant Heal mode only)")]
        public float instantHealAmount = 5f;

        [Header("VFX")]
        [Tooltip("VFX played when the zone activates")]
        public VFXReference startVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };

        [Tooltip("VFX played when the zone deactivates")]
        public VFXReference endVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };

        [Tooltip("VFX played while the zone is active")]
        public VFXReference activeVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };

        [Tooltip("Seconds between active VFX spawns (0 = only once on activation)")]
        public float activeVFXInterval = 0f;

        [Tooltip("VFX played when one or more agents are within radius")]
        public VFXReference presenceVFX = new VFXReference { type = VFXType.None, defaultDuration = 1f };

        [Tooltip("Seconds between presence VFX spawns while agents are inside (0 = only once on entry)")]
        public float presenceVFXInterval = 0f;

        [Tooltip("Scale multiplier for all zone VFX")]
        public float vfxScale = 1f;

        [Tooltip("Offset from the zone center for VFX spawns")]
        public Vector3 vfxOffset = Vector3.zero;

        [Header("Audio")]
        [Tooltip("One-shot sound played when the zone activates")]
        public ZoneAudioCue startSound = new ZoneAudioCue();

        [Tooltip("One-shot sound played when the zone deactivates")]
        public ZoneAudioCue endSound = new ZoneAudioCue();

        [Tooltip("Looping sound played while the zone is active")]
        public ZoneAudioCue activeLoopSound = new ZoneAudioCue();

        [Tooltip("Looping sound played while agents are within the radius")]
        public ZoneAudioCue presenceLoopSound = new ZoneAudioCue();

        [Header("Filtering")]
        [Tooltip("Which factions the zone affects")]
        public CombatZoneFilterMode factionFilterMode = CombatZoneFilterMode.AllFactions;

        [Tooltip("Faction list used for Only/Exclude modes")]
        public List<EnemyCrowd> factionFilter = new List<EnemyCrowd>();

        [Header("Visual Debug")]
        public bool showGizmos = true;
        public Color gizmoColor = Color.clear;

        [Header("Radius Line")]
        [Tooltip("Optional LineRenderer to draw the radius circle")]
        public LineRenderer radiusLineRenderer;

        [Tooltip("Number of segments used to approximate the circle")]
        [Range(8, 256)]
        public int radiusLineSegments = 64;

        [Tooltip("Vertical offset for the radius line")]
        public float radiusLineHeight = 0.02f;

        [Tooltip("Update the line renderer in edit mode")]
        public bool updateLineRendererInEditor = true;

        [Tooltip("Update the line renderer every frame while playing")]
        public bool updateLineRendererEveryFrame = true;

        [Header("Radius Line Animation")]
        [Tooltip("Animate the radius line during play mode")]
        public bool animateRadiusLine = true;

        [Tooltip("Animate the radius line in edit mode")]
        public bool animateRadiusLineInEditor = false;

        [Tooltip("Pulse speed for line animation")]
        public float radiusLinePulseSpeed = 1.2f;

        [Range(0f, 1f)]
        [Tooltip("Alpha pulse strength (0 = no alpha pulse)")]
        public float radiusLineAlphaPulse = 0.2f;

        [Range(0f, 1f)]
        [Tooltip("Width pulse strength (0 = no width pulse)")]
        public float radiusLineWidthPulse = 0.15f;

        [Tooltip("Seconds to fade the line in/out when the zone activates/deactivates")]
        public float radiusLineFadeDuration = 0.25f;

        private EnemyMassesCrowdController _controller;
        private CrowdAudioManager _audioManager;
        private VFXSpawner _vfxSpawner;
        private Transform _vfxPoolRoot;
        private readonly HashSet<EnemyCrowd> _factionFilterSet = new HashSet<EnemyCrowd>();
        private float _nextApplyTime;
        private float _nextStateTime;
        private float _nextActiveVfxTime;
        private float _nextPresenceVfxTime;
        private bool _isActive;
        private bool _isRunning;
        private bool _hasAgentsInRadius;
        private AudioSource _activeLoopSource;
        private AudioSource _presenceLoopSource;
        private Vector3[] _radiusLinePoints;
        private LineRenderer _cachedLineRenderer;
        private bool _baseLineEnabled = true;
        private float _baseLineWidth = 1f;
        private Gradient _baseLineGradient;
        private Gradient _animatedLineGradient;
        private GradientColorKey[] _baseColorKeys;
        private GradientAlphaKey[] _baseAlphaKeys;
        private GradientColorKey[] _animatedColorKeys;
        private GradientAlphaKey[] _animatedAlphaKeys;
        private bool _lineDefaultsCached;
        private float _radiusLineFadeAlpha = 1f;

        public bool IsActive => _isActive;
        public bool IsRunning => _isRunning;

        private void OnValidate()
        {
            radius = Mathf.Max(0.1f, radius);
            startDelay = Mathf.Max(0f, startDelay);
            activeDuration = Mathf.Max(0f, activeDuration);
            inactiveDuration = Mathf.Max(0f, inactiveDuration);
            applyInterval = Mathf.Max(0.05f, applyInterval);
            effectDuration = Mathf.Max(0f, effectDuration);
            effectTickInterval = Mathf.Max(0.1f, effectTickInterval);
            instantHealAmount = Mathf.Max(0f, instantHealAmount);
            activeVFXInterval = Mathf.Max(0f, activeVFXInterval);
            presenceVFXInterval = Mathf.Max(0f, presenceVFXInterval);
            vfxScale = Mathf.Max(0.01f, vfxScale);
            radiusLineSegments = Mathf.Clamp(radiusLineSegments, 8, 256);
            radiusLinePulseSpeed = Mathf.Max(0f, radiusLinePulseSpeed);
            radiusLineAlphaPulse = Mathf.Clamp01(radiusLineAlphaPulse);
            radiusLineWidthPulse = Mathf.Clamp01(radiusLineWidthPulse);
            radiusLineFadeDuration = Mathf.Max(0f, radiusLineFadeDuration);

            ClampAudioCue(startSound);
            ClampAudioCue(endSound);
            ClampAudioCue(activeLoopSound);
            ClampAudioCue(presenceLoopSound);

            RebuildFactionFilter();
            CacheLineRendererDefaults(true);
            UpdateLineRenderer(true);
        }

        private void Awake()
        {
            RebuildFactionFilter();
        }

        private void Start()
        {
            if (!Application.isPlaying)
                return;

            ResolveController();
            UpdateLineRenderer(true);

            if (startOnStart)
                StartZone();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                if (updateLineRendererInEditor)
                {
                    UpdateLineRenderer(false);
                }
                _radiusLineFadeAlpha = 1f;
                if (animateRadiusLineInEditor)
                {
                    AnimateLineRenderer();
                }
                else
                {
                    RestoreLineRendererDefaults();
                }
                return;
            }

            if (updateLineRendererEveryFrame)
            {
                UpdateLineRenderer(false);
            }
            UpdateLineFade();
            if (animateRadiusLine)
            {
                AnimateLineRenderer();
            }
            else
            {
                RestoreLineRendererDefaults();
            }

            UpdateLoopPositions();

            _vfxSpawner?.ReclaimFinishedVFX();

            if (!_isRunning)
                return;

            if (!EnsureController())
                return;

            if (!_isActive)
            {
                if (Time.time >= _nextStateTime)
                    ActivateZone();
                return;
            }

            if (UsesCycle() && Time.time >= _nextStateTime)
            {
                DeactivateZone();
                return;
            }

            UpdateActiveVFX();
            UpdatePresenceVFX();

            if (Time.time < _nextApplyTime)
                return;

            _nextApplyTime = Time.time + applyInterval;
            bool hasAgents = ApplyEffect();
            UpdatePresenceState(hasAgents);
        }

        private void OnDisable()
        {
            StopZoneInternal(false);
        }

        public void StartZone()
        {
            if (!Application.isPlaying)
                return;

            _isRunning = true;
            _isActive = false;
            _hasAgentsInRadius = false;
            _radiusLineFadeAlpha = 0f;
            _nextApplyTime = Time.time;
            _nextActiveVfxTime = 0f;
            _nextPresenceVfxTime = 0f;

            if (startDelay <= 0f)
            {
                ActivateZone();
            }
            else
            {
                _nextStateTime = Time.time + startDelay;
            }
        }

        public void StopZone()
        {
            StopZoneInternal(true);
        }

        public void RestartZone()
        {
            StopZone();
            StartZone();
        }

        public Color GetGizmoColor()
        {
            if (gizmoColor.a > 0f)
                return gizmoColor;

            if (effectMode == CombatZoneEffectMode.InstantHeal)
                return new Color(0.3f, 1f, 0.4f, 0.6f);

            return StatusEffectHelper.GetCategory(effectType) switch
            {
                StatusEffectCategory.Buff => new Color(0.3f, 1f, 0.3f, 0.6f),
                StatusEffectCategory.CrowdControl => new Color(0.9f, 0.8f, 0.3f, 0.6f),
                _ => new Color(1f, 0.3f, 0.3f, 0.6f)
            };
        }

        private void ActivateZone()
        {
            _isActive = true;
            _nextApplyTime = Time.time;
            _nextStateTime = UsesCycle() ? Time.time + activeDuration : float.PositiveInfinity;

            PlayStartCue();
            SpawnZoneVFX(startVFX);
            StartActiveLoop();

            SpawnZoneVFX(activeVFX);
            _nextActiveVfxTime = activeVFXInterval > 0f
                ? Time.time + activeVFXInterval
                : 0f;
        }

        private void DeactivateZone()
        {
            _isActive = false;
            _nextStateTime = Time.time + inactiveDuration;

            StopActiveLoop();
            StopPresenceLoop();
            _hasAgentsInRadius = false;
            _nextPresenceVfxTime = 0f;

            SpawnZoneVFX(endVFX);
            PlayEndCue();
        }

        private bool UsesCycle()
        {
            return activeDuration > 0f && inactiveDuration > 0f;
        }

        private void ResolveController()
        {
            if (_controller != null)
                return;

            // Priority: serialized reference > singleton > scene search
            if (crowdController != null)
            {
                _controller = crowdController;
            }
            else if (EnemyMassesCrowdController.Instance != null)
            {
                _controller = EnemyMassesCrowdController.Instance;
            }
            else
            {
                // Fallback for runtime instantiation when singleton isn't set yet
                _controller = FindFirstObjectByType<EnemyMassesCrowdController>();
            }
        }

        private bool EnsureController()
        {
            if (_controller != null)
                return true;

            ResolveController();

            if (_controller == null)
            {
                Debug.LogWarning($"[EnemyMassesCombatEffectZone] No EnemyMassesCrowdController found. Disabling {gameObject.name}");
                _isRunning = false;
                _isActive = false;
                return false;
            }

            return true;
        }

        private bool ApplyEffect()
        {
            if (radius <= 0f)
                return false;

            Vector3 center = transform.position;
            float radiusSqr = radius * radius;

            List<int> nearby = _controller.GetAgentsInRadius(center, radius);
            if (nearby.Count > 0)
            {
                return ApplyToAgentIndices(nearby, center, radiusSqr);
            }

            return ApplyToAllAgents(center, radiusSqr);
        }

        private bool ApplyToAgentIndices(List<int> indices, Vector3 center, float radiusSqr)
        {
            bool hasAgents = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int agentIndex = indices[i];
                CrowdAgent agent = _controller.GetAgent(agentIndex);
                if (agent == null || agent.isDead)
                    continue;

                if (!IsFactionAllowed(agent.enemyCrowd))
                    continue;

                Vector3 agentPos = GetAgentPosition(agent);
                if ((agentPos - center).sqrMagnitude > radiusSqr)
                    continue;

                ApplyToAgent(agentIndex, agent);
                hasAgents = true;
            }

            return hasAgents;
        }

        private bool ApplyToAllAgents(Vector3 center, float radiusSqr)
        {
            List<CrowdAgent> agents = _controller.GetAgents();
            bool hasAgents = false;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null || agent.isDead)
                    continue;

                if (!IsFactionAllowed(agent.enemyCrowd))
                    continue;

                Vector3 agentPos = GetAgentPosition(agent);
                if ((agentPos - center).sqrMagnitude > radiusSqr)
                    continue;

                ApplyToAgent(i, agent);
                hasAgents = true;
            }

            return hasAgents;
        }

        private void ApplyToAgent(int agentIndex, CrowdAgent agent)
        {
            if (effectMode == CombatZoneEffectMode.InstantHeal)
            {
                _controller.HealAgent(agentIndex, instantHealAmount);
            }
            else
            {
                _controller.ApplyStatusEffect(
                    agentIndex,
                    effectType,
                    effectDuration,
                    effectValue,
                    effectTickInterval,
                    -1);
            }
        }

        private bool IsFactionAllowed(EnemyCrowd crowd)
        {
            if (factionFilterMode == CombatZoneFilterMode.AllFactions)
                return true;

            bool isListed = crowd != null && _factionFilterSet.Contains(crowd);

            return factionFilterMode == CombatZoneFilterMode.OnlyListedFactions
                ? isListed
                : !isListed;
        }

        private void RebuildFactionFilter()
        {
            _factionFilterSet.Clear();
            if (factionFilter == null)
                return;

            for (int i = 0; i < factionFilter.Count; i++)
            {
                EnemyCrowd crowd = factionFilter[i];
                if (crowd != null)
                    _factionFilterSet.Add(crowd);
            }
        }

        private void ClampAudioCue(ZoneAudioCue cue)
        {
            if (cue == null)
                return;

            cue.minDistance = Mathf.Max(0f, cue.minDistance);
            cue.maxDistance = Mathf.Max(cue.minDistance, cue.maxDistance);
            if (cue.maxConcurrent < 0)
                cue.maxConcurrent = 0;
        }

        private void EnsureVFXSpawner()
        {
            if (_vfxSpawner != null)
                return;

            if (_vfxPoolRoot == null)
            {
                var root = new GameObject("CombatEffectZoneVFX");
                root.transform.SetParent(transform, false);
                _vfxPoolRoot = root.transform;
            }

            _vfxSpawner = new VFXSpawner(_vfxPoolRoot);
        }

        private void SpawnZoneVFX(VFXReference vfx)
        {
            if (vfx == null || !vfx.IsValid)
                return;

            EnsureVFXSpawner();

            Vector3 position = transform.position + vfxOffset;
            float duration = vfx.GetDuration();
            _vfxSpawner.SpawnEffect(vfx, position, Quaternion.identity, duration, vfxScale);
        }

        private void UpdateActiveVFX()
        {
            if (!_isActive || activeVFX == null || !activeVFX.IsValid)
                return;

            if (activeVFXInterval <= 0f)
                return;

            if (Time.time < _nextActiveVfxTime)
                return;

            _nextActiveVfxTime = Time.time + activeVFXInterval;
            SpawnZoneVFX(activeVFX);
        }

        private void UpdatePresenceVFX()
        {
            if (!_isActive || !_hasAgentsInRadius)
                return;

            if (presenceVFX == null || !presenceVFX.IsValid)
                return;

            if (presenceVFXInterval <= 0f)
                return;

            if (Time.time < _nextPresenceVfxTime)
                return;

            _nextPresenceVfxTime = Time.time + presenceVFXInterval;
            SpawnZoneVFX(presenceVFX);
        }

        private void UpdatePresenceState(bool hasAgents)
        {
            if (hasAgents == _hasAgentsInRadius)
                return;

            _hasAgentsInRadius = hasAgents;

            if (_hasAgentsInRadius)
            {
                SpawnZoneVFX(presenceVFX);
                _nextPresenceVfxTime = presenceVFXInterval > 0f
                    ? Time.time + presenceVFXInterval
                    : 0f;

                StartPresenceLoop();
            }
            else
            {
                StopPresenceLoop();
                _nextPresenceVfxTime = 0f;
            }
        }

        private void PlayStartCue()
        {
            PlayOneShot(startSound);
        }

        private void PlayEndCue()
        {
            PlayOneShot(endSound);
        }

        private void PlayOneShot(ZoneAudioCue cue)
        {
            if (cue == null || !cue.HasClip)
                return;

            var audioManager = GetAudioManager();
            if (audioManager == null)
                return;

            int maxConcurrent = cue.maxConcurrent > 0 ? cue.maxConcurrent : -1;
            audioManager.PlaySound(
                cue.clip,
                transform.position,
                cue.volume,
                cue.pitchVariation,
                cue.spatialBlend,
                maxConcurrent,
                cue.minDistance,
                cue.maxDistance);
        }

        private void StartActiveLoop()
        {
            if (activeLoopSound == null || !activeLoopSound.HasClip)
                return;

            StopActiveLoop();

            var audioManager = GetAudioManager();
            if (audioManager == null)
                return;

            int maxConcurrent = activeLoopSound.maxConcurrent > 0 ? activeLoopSound.maxConcurrent : -1;
            _activeLoopSource = audioManager.PlayLoopingSound(
                activeLoopSound.clip,
                transform.position,
                activeLoopSound.volume,
                activeLoopSound.pitchVariation,
                activeLoopSound.spatialBlend,
                maxConcurrent,
                activeLoopSound.minDistance,
                activeLoopSound.maxDistance);
        }

        private void StopActiveLoop()
        {
            StopLoop(ref _activeLoopSource);
        }

        private void StartPresenceLoop()
        {
            if (presenceLoopSound == null || !presenceLoopSound.HasClip)
                return;

            if (_presenceLoopSource != null)
                return;

            var audioManager = GetAudioManager();
            if (audioManager == null)
                return;

            int maxConcurrent = presenceLoopSound.maxConcurrent > 0 ? presenceLoopSound.maxConcurrent : -1;
            _presenceLoopSource = audioManager.PlayLoopingSound(
                presenceLoopSound.clip,
                transform.position,
                presenceLoopSound.volume,
                presenceLoopSound.pitchVariation,
                presenceLoopSound.spatialBlend,
                maxConcurrent,
                presenceLoopSound.minDistance,
                presenceLoopSound.maxDistance);
        }

        private void StopPresenceLoop()
        {
            StopLoop(ref _presenceLoopSource);
        }

        private void StopLoop(ref AudioSource source)
        {
            if (source == null)
                return;

            var audioManager = CrowdAudioManager.Instance;
            if (audioManager != null)
            {
                audioManager.ReleaseSound(source);
            }
            else
            {
                source.Stop();
            }

            source = null;
        }

        private CrowdAudioManager GetAudioManager()
        {
            if (_audioManager != null)
                return _audioManager;

            _audioManager = CrowdAudioManager.EnsureInstance();
            return _audioManager;
        }

        private void UpdateLoopPositions()
        {
            if (_activeLoopSource != null)
                _activeLoopSource.transform.position = transform.position;

            if (_presenceLoopSource != null)
                _presenceLoopSource.transform.position = transform.position;
        }

        private void StopZoneInternal(bool playEndCue)
        {
            if (_isActive && playEndCue)
            {
                StopAllZoneVFX();
                SpawnZoneVFX(endVFX);
                PlayEndCue();
            }
            else
            {
                StopAllZoneVFX();
            }

            StopActiveLoop();
            StopPresenceLoop();

            _isRunning = false;
            _isActive = false;
            _hasAgentsInRadius = false;
            _nextActiveVfxTime = 0f;
            _nextPresenceVfxTime = 0f;
        }

        private void StopAllZoneVFX()
        {
            if (_vfxSpawner == null)
                return;

            _vfxSpawner.StopAllEffects();
        }

        private void UpdateLineFade()
        {
            if (radiusLineRenderer == null)
                return;

            CacheLineRendererDefaults(false);

            float targetAlpha = (_isRunning && _isActive) ? 1f : 0f;
            if (radiusLineFadeDuration <= 0f)
            {
                _radiusLineFadeAlpha = targetAlpha;
            }
            else
            {
                float delta = Time.deltaTime / radiusLineFadeDuration;
                _radiusLineFadeAlpha = Mathf.MoveTowards(_radiusLineFadeAlpha, targetAlpha, delta);
            }

            radiusLineRenderer.enabled = _baseLineEnabled && _radiusLineFadeAlpha > 0.001f;
        }

        private void UpdateLineRenderer(bool force)
        {
            if (radiusLineRenderer == null)
                return;

            if (!force && !Application.isPlaying && !updateLineRendererInEditor)
                return;

            int segments = Mathf.Clamp(radiusLineSegments, 8, 256);
            if (_radiusLinePoints == null || _radiusLinePoints.Length != segments)
                _radiusLinePoints = new Vector3[segments];

            CacheLineRendererDefaults(false);

            float step = Mathf.PI * 2f / segments;
            float lineRadius = Mathf.Max(0.1f, radius);

            if (radiusLineRenderer.useWorldSpace)
            {
                Vector3 center = transform.position + Vector3.up * radiusLineHeight;
                for (int i = 0; i < segments; i++)
                {
                    float angle = step * i;
                    _radiusLinePoints[i] = center + new Vector3(Mathf.Cos(angle) * lineRadius, 0f, Mathf.Sin(angle) * lineRadius);
                }
            }
            else
            {
                Vector3 center = Vector3.up * radiusLineHeight;
                for (int i = 0; i < segments; i++)
                {
                    float angle = step * i;
                    _radiusLinePoints[i] = center + new Vector3(Mathf.Cos(angle) * lineRadius, 0f, Mathf.Sin(angle) * lineRadius);
                }
            }

            radiusLineRenderer.loop = true;
            radiusLineRenderer.positionCount = segments;
            radiusLineRenderer.SetPositions(_radiusLinePoints);
        }

        private void CacheLineRendererDefaults(bool force)
        {
            if (radiusLineRenderer == null)
            {
                _cachedLineRenderer = null;
                _lineDefaultsCached = false;
                return;
            }

            bool shouldRefresh = force || !_lineDefaultsCached || _cachedLineRenderer != radiusLineRenderer;
            if (!shouldRefresh)
                return;

            bool refreshEnabled = !_lineDefaultsCached || _cachedLineRenderer != radiusLineRenderer || !Application.isPlaying;
            _cachedLineRenderer = radiusLineRenderer;
            if (refreshEnabled)
                _baseLineEnabled = radiusLineRenderer.enabled;
            _baseLineWidth = Mathf.Max(0.001f, radiusLineRenderer.widthMultiplier);
            _baseLineGradient = CopyGradient(radiusLineRenderer.colorGradient);
            _baseColorKeys = _baseLineGradient.colorKeys;
            _baseAlphaKeys = _baseLineGradient.alphaKeys;
            _animatedColorKeys = new GradientColorKey[_baseColorKeys.Length];
            _animatedAlphaKeys = new GradientAlphaKey[_baseAlphaKeys.Length];
            _animatedLineGradient = new Gradient();
            _lineDefaultsCached = true;
        }

        private void AnimateLineRenderer()
        {
            ApplyLineAppearance(true);
        }

        private void RestoreLineRendererDefaults()
        {
            ApplyLineAppearance(false);
        }

        private void ApplyLineAppearance(bool animatePulse)
        {
            if (radiusLineRenderer == null)
                return;

            CacheLineRendererDefaults(false);
            if (!_lineDefaultsCached || _baseAlphaKeys == null || _baseColorKeys == null)
                return;

            if (!Application.isPlaying && radiusLineRenderer.enabled != _baseLineEnabled)
                radiusLineRenderer.enabled = _baseLineEnabled;

            float pulse = 0f;
            if (animatePulse && radiusLinePulseSpeed > 0f &&
                (radiusLineAlphaPulse > 0f || radiusLineWidthPulse > 0f))
            {
                float t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
                pulse = Mathf.Sin(t * radiusLinePulseSpeed * Mathf.PI * 2f);
            }

            float alphaMultiplier = _radiusLineFadeAlpha;
            float widthMultiplier = 1f;

            if (animatePulse)
            {
                alphaMultiplier *= 1f + radiusLineAlphaPulse * pulse;
                widthMultiplier *= 1f + radiusLineWidthPulse * pulse;
            }

            alphaMultiplier = Mathf.Clamp01(alphaMultiplier);
            widthMultiplier = Mathf.Max(0.001f, _baseLineWidth * widthMultiplier);

            for (int i = 0; i < _baseColorKeys.Length; i++)
            {
                _animatedColorKeys[i] = _baseColorKeys[i];
            }

            for (int i = 0; i < _baseAlphaKeys.Length; i++)
            {
                float alpha = Mathf.Clamp01(_baseAlphaKeys[i].alpha * alphaMultiplier);
                _animatedAlphaKeys[i] = new GradientAlphaKey(alpha, _baseAlphaKeys[i].time);
            }

            _animatedLineGradient.SetKeys(_animatedColorKeys, _animatedAlphaKeys);
            radiusLineRenderer.colorGradient = _animatedLineGradient;
            radiusLineRenderer.widthMultiplier = widthMultiplier;
        }

        private static Gradient CopyGradient(Gradient source)
        {
            var gradient = new Gradient();
            if (source != null)
            {
                gradient.SetKeys(source.colorKeys, source.alphaKeys);
            }
            return gradient;
        }

        private Vector3 GetAgentPosition(CrowdAgent agent)
        {
            if (agent.navigationAgent != null)
                return agent.WorldPosition;

            if (agent.crowdInstance != null)
                return agent.crowdInstance.transform.position;

            return transform.position;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos)
                return;

            Color color = GetGizmoColor();
            if (Application.isPlaying && (!_isRunning || !_isActive))
                color = new Color(color.r, color.g, color.b, color.a * 0.35f);

            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, radius);

            Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.3f);
            Gizmos.DrawSphere(transform.position, radius);
        }
    }
}
