using UnityEngine;
using Pathfinding;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// Adapter for A* Pathfinding Project's AIPath component (v5.4+).
    /// AIPath is the most commonly used movement script, suitable for most games.
    /// Works with all graph types.
    /// </summary>
    [RequireComponent(typeof(AIPath))]
    public class AStarAIPathAdapter : AStarAgentAdapterBase
    {
        private AIPath _aiPath;
        
        /// <summary>
        /// The underlying AIPath component.
        /// </summary>
        public AIPath AIPath
        {
            get
            {
                if (_aiPath == null)
                    _aiPath = GetComponent<AIPath>();
                return _aiPath;
            }
        }
        
        /// <inheritdoc />
        public override NavigationType NavigationType => NavigationType.AStarAIPath;
        
        /// <inheritdoc />
        public override float Acceleration
        {
            // AIPath in 5.4+ uses MovementSettings
            get => Speed * 4f; // Reasonable default
            set { /* Not directly settable in 5.4+ */ }
        }
        
        /// <inheritdoc />
        public override float AngularSpeed
        {
            get => AIPath?.rotationSpeed ?? 360f;
            set
            {
                if (AIPath != null)
                    AIPath.rotationSpeed = value;
            }
        }
        
        /// <inheritdoc />
        public override bool UpdateRotation
        {
            get => AIPath?.enableRotation ?? true;
            set
            {
                if (AIPath != null)
                    AIPath.enableRotation = value;
            }
        }
        
        /// <inheritdoc />
        public override float StoppingDistance
        {
            get => AIPath?.endReachedDistance ?? 0.2f;
            set
            {
                if (AIPath != null)
                    AIPath.endReachedDistance = value;
            }
        }
        
        /// <inheritdoc />
        public override float Height
        {
            get => AIPath?.height ?? 2f;
            set
            {
                if (AIPath != null)
                    AIPath.height = value;
            }
        }
        
        /// <inheritdoc />
        public override float Radius
        {
            get => AIPath?.radius ?? 0.5f;
            set
            {
                if (AIPath != null)
                    AIPath.radius = value;
            }
        }
        
        #region Unity Lifecycle
        
        protected override void Awake()
        {
            base.Awake();
            _aiPath = GetComponent<AIPath>();
        }
        
        protected override void OnEnable()
        {
            base.OnEnable();
            _aiPath = GetComponent<AIPath>();
        }
        
        #endregion
    }
}
