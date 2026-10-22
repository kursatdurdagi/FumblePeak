using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    [AddComponentMenu("Enemy Masses/RTS/World Marker")]
    public class EnemyMassesRTSWorldMarker : MonoBehaviour
    {
        public enum VisibilityRule
        {
            Always,
            OnlyWhenVisible,
            Never
        }

        [Header("Minimap Visibility Rules")]
        [Tooltip("Minimap visibility rule for player-controlled anchors.")]
        [SerializeField] private VisibilityRule m_FriendlyVisibility = VisibilityRule.Always;

        [Tooltip("Minimap visibility rule for enemy anchors.")]
        [SerializeField] private VisibilityRule m_EnemyVisibility = VisibilityRule.OnlyWhenVisible;

        [Tooltip("Minimap visibility rule for neutral anchors.")]
        [SerializeField] private VisibilityRule m_NeutralVisibility = VisibilityRule.Always;

        [Header("Fog Reveal Rules")]
        [Tooltip("Allow player-controlled anchors to reveal fog.")]
        [SerializeField] private bool m_RevealFogForFriendly = true;

        [Tooltip("Allow enemy anchors to reveal fog.")]
        [SerializeField] private bool m_RevealFogForEnemy = false;

        [Tooltip("Allow neutral anchors to reveal fog.")]
        [SerializeField] private bool m_RevealFogForNeutral = false;

        [Header("Fog Visibility")]
        [Tooltip("Optional provider that implements IFogVisibility (e.g., FogOfWarLite).")]
        [SerializeField] private MonoBehaviour m_FogProviderOverride;

        [Tooltip("Alpha threshold for active visibility; lower hides sooner during refog.")]
        [Range(1, 255)]
        [SerializeField] private byte m_FogVisibilityThreshold = 32;

        private IFogVisibility _fog;

        private void OnEnable()
        {
            CacheFogProvider();
            EnemyMassesRTSWorldAnchor.NotifyRulesProviderAdded(this);
        }

        private void OnValidate()
        {
            CacheFogProvider();
        }

        private void CacheFogProvider()
        {
            if (m_FogProviderOverride != null)
            {
                _fog = m_FogProviderOverride as IFogVisibility;
                if (_fog == null)
                {
                    _fog = m_FogProviderOverride.GetComponent<IFogVisibility>();
                }
            }

            if (_fog == null)
            {
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IFogVisibility fv)
                    {
                        _fog = fv;
                        break;
                    }
                }
            }
        }

        private VisibilityRule GetVisibilityRule(RTSWorldAnchorFaction faction)
        {
            switch (faction)
            {
                case RTSWorldAnchorFaction.PlayerControlled:
                    return m_FriendlyVisibility;
                case RTSWorldAnchorFaction.Enemy:
                    return m_EnemyVisibility;
                case RTSWorldAnchorFaction.Neutral:
                default:
                    return m_NeutralVisibility;
            }
        }

        private bool IsVisibleByFog(Vector3 worldPosition)
        {
            if (_fog == null) CacheFogProvider();
            if (_fog == null) return true;
            return _fog.IsActivelyVisible(worldPosition, m_FogVisibilityThreshold);
        }

        public bool ShouldShowOnMinimap(EnemyMassesRTSWorldAnchor anchor)
        {
            if (anchor == null) return false;

            var rule = GetVisibilityRule(anchor.Faction);
            if (rule == VisibilityRule.Never) return false;
            if (rule == VisibilityRule.Always) return true;

            return IsVisibleByFog(anchor.WorldPosition);
        }

        public bool ShouldRevealFog(EnemyMassesRTSWorldAnchor anchor)
        {
            if (anchor == null) return false;

            switch (anchor.Faction)
            {
                case RTSWorldAnchorFaction.PlayerControlled:
                    return m_RevealFogForFriendly;
                case RTSWorldAnchorFaction.Enemy:
                    return m_RevealFogForEnemy;
                case RTSWorldAnchorFaction.Neutral:
                default:
                    return m_RevealFogForNeutral;
            }
        }
    }
}
