using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime
{
    public enum RTSWorldAnchorFaction
    {
        PlayerControlled,
        Enemy,
        Neutral
    }

    [AddComponentMenu("Enemy Masses/RTS/World Anchor")]
    public class EnemyMassesRTSWorldAnchor : MonoBehaviour, IRTSFogRevealer, IRTSMinimapIcon
    {
        [Header("Anchor")]
        [Tooltip("Optional transform used for fog/minimap position. If null, uses this transform.")]
        [SerializeField] private Transform m_Anchor;

        [Header("Faction")]
        [SerializeField] private RTSWorldAnchorFaction m_Faction = RTSWorldAnchorFaction.PlayerControlled;

        [Header("Minimap")]
        [SerializeField] private Color m_MinimapColor = new Color(0.2f, 1f, 0.2f, 1f);

        [Header("Fog of War")]
        [SerializeField] private float m_FogRevealRadius = 10f;

        [Header("Rules")]
        [Tooltip("Optional rules provider. If null and auto-find is enabled, searches parents.")]
        [SerializeField] private EnemyMassesRTSWorldMarker m_RulesProvider;

        [Tooltip("Automatically find EnemyMassesRTSWorldMarker in parents when no rules provider is assigned.")]
        [SerializeField] private bool m_AutoFindRulesProvider = true;

        [Header("Position Offset")]
        [SerializeField] private Vector3 m_PositionOffset = Vector3.zero;

        [Header("Gizmos")]
        [SerializeField] private bool m_ShowGizmos = true;

        private static readonly Dictionary<int, EnemyMassesRTSWorldMarker> RulesProviderCache =
            new Dictionary<int, EnemyMassesRTSWorldMarker>();

        private EnemyMassesRTSWorldMarker _cachedRulesProvider;

        public static void NotifyRulesProviderAdded(EnemyMassesRTSWorldMarker marker)
        {
            if (marker == null) return;

            var root = marker.transform.root != null ? marker.transform.root : marker.transform;
            int cacheKey = root.GetInstanceID();
            RulesProviderCache[cacheKey] = marker;

            var anchors = root.GetComponentsInChildren<EnemyMassesRTSWorldAnchor>(true);
            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor != null)
                {
                    anchor.RefreshRulesProvider();
                }
            }
        }

        private void OnEnable()
        {
            CacheRulesProvider();
            RTSWorldUnitRegistry.RegisterFogRevealer(this);
            RTSWorldUnitRegistry.RegisterMinimapIcon(this);
        }

        private void OnDisable()
        {
            RTSWorldUnitRegistry.UnregisterFogRevealer(this);
            RTSWorldUnitRegistry.UnregisterMinimapIcon(this);
        }

        private void OnDestroy()
        {
            RTSWorldUnitRegistry.UnregisterFogRevealer(this);
            RTSWorldUnitRegistry.UnregisterMinimapIcon(this);
        }

        private void OnValidate()
        {
            if (float.IsNaN(m_FogRevealRadius) || float.IsInfinity(m_FogRevealRadius))
            {
                m_FogRevealRadius = 0f;
            }
            else if (m_FogRevealRadius < 0f)
            {
                m_FogRevealRadius = 0f;
            }

            CacheRulesProvider();
        }

        private void CacheRulesProvider()
        {
            if (m_RulesProvider != null)
            {
                _cachedRulesProvider = m_RulesProvider;
                CacheForRoot(_cachedRulesProvider);
                return;
            }

            if (!m_AutoFindRulesProvider)
            {
                _cachedRulesProvider = null;
                return;
            }

            var root = transform.root != null ? transform.root : transform;
            int cacheKey = root.GetInstanceID();

            if (RulesProviderCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached != null)
                {
                    _cachedRulesProvider = cached;
                    return;
                }

                RulesProviderCache.Remove(cacheKey);
            }

            _cachedRulesProvider = GetComponentInParent<EnemyMassesRTSWorldMarker>();
            if (_cachedRulesProvider == null)
            {
                _cachedRulesProvider = root.GetComponentInChildren<EnemyMassesRTSWorldMarker>(true);
            }

            CacheForRoot(_cachedRulesProvider);
        }

        public void RefreshRulesProvider()
        {
            CacheRulesProvider();
        }

        private void CacheForRoot(EnemyMassesRTSWorldMarker marker)
        {
            if (marker == null) return;
            var root = transform.root != null ? transform.root : transform;
            int cacheKey = root.GetInstanceID();
            RulesProviderCache[cacheKey] = marker;
        }

        public RTSWorldAnchorFaction Faction => m_Faction;

        public Vector3 WorldPosition
        {
            get
            {
                Transform anchor = m_Anchor != null ? m_Anchor : transform;
                return anchor != null ? anchor.position + m_PositionOffset : Vector3.zero;
            }
        }

        // IRTSFogRevealer ----------------------------------------------------------------------

        public bool RevealFogOfWar => ShouldRevealFog();

        public Vector3 FogRevealPosition => WorldPosition;

        public float FogRevealRadius => ShouldRevealFog() ? m_FogRevealRadius : 0f;

        // IRTSMinimapIcon ----------------------------------------------------------------------

        public bool ShowOnMinimap => ShouldShowOnMinimap();

        public Vector3 MinimapPosition => WorldPosition;

        public Color MinimapColor => m_MinimapColor;

        private bool ShouldRevealFog()
        {
            if (m_FogRevealRadius <= 0f) return false;

            if (_cachedRulesProvider != null)
            {
                return _cachedRulesProvider.ShouldRevealFog(this);
            }

            return m_Faction == RTSWorldAnchorFaction.PlayerControlled;
        }

        private bool ShouldShowOnMinimap()
        {
            if (_cachedRulesProvider != null)
            {
                return _cachedRulesProvider.ShouldShowOnMinimap(this);
            }

            return true;
        }

        private void OnDrawGizmos()
        {
            DrawAnchorGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawAnchorGizmos(true);
        }

        private void DrawAnchorGizmos(bool selected)
        {
            if (!m_ShowGizmos) return;

            Color baseColor = m_MinimapColor;
            if (baseColor.a <= 0f)
            {
                baseColor = new Color(0.2f, 1f, 0.2f, 1f);
            }

            Vector3 pos = WorldPosition;
            float coreAlpha = selected ? 0.6f : 0.35f;
            Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, coreAlpha);
            Gizmos.DrawSphere(pos, 0.4f);

            if (ShouldRevealFog() && m_FogRevealRadius > 0f)
            {
                float radiusAlpha = selected ? 0.4f : 0.2f;
                Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, radiusAlpha);
                Gizmos.DrawWireSphere(pos, m_FogRevealRadius);
            }
        }
    }
}
