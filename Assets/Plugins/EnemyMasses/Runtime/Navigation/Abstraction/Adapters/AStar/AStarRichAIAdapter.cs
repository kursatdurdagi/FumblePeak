using UnityEngine;
using Pathfinding;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// Adapter for A* Pathfinding Project's RichAI component (v5.4+).
    /// RichAI is specifically designed for navmesh/recast graphs and provides
    /// advanced features like off-mesh link traversal and funnel simplification.
    /// </summary>
    [RequireComponent(typeof(RichAI))]
    public class AStarRichAIAdapter : AStarAgentAdapterBase
    {
        private RichAI _richAI;
        
        /// <summary>
        /// The underlying RichAI component.
        /// </summary>
        public RichAI RichAI
        {
            get
            {
                if (_richAI == null)
                    _richAI = GetComponent<RichAI>();
                return _richAI;
            }
        }
        
        /// <inheritdoc />
        public override NavigationType NavigationType => NavigationType.AStarRichAI;
        
        /// <inheritdoc />
        public override float Acceleration
        {
            get => RichAI?.acceleration ?? 10f;
            set
            {
                if (RichAI != null)
                    RichAI.acceleration = value;
            }
        }
        
        /// <inheritdoc />
        public override float AngularSpeed
        {
            get => RichAI?.rotationSpeed ?? 360f;
            set
            {
                if (RichAI != null)
                    RichAI.rotationSpeed = value;
            }
        }
        
        /// <inheritdoc />
        public override bool UpdateRotation
        {
            get => RichAI?.enableRotation ?? true;
            set
            {
                if (RichAI != null)
                    RichAI.enableRotation = value;
            }
        }
        
        /// <inheritdoc />
        public override float StoppingDistance
        {
            get => RichAI?.endReachedDistance ?? 0.2f;
            set
            {
                if (RichAI != null)
                    RichAI.endReachedDistance = value;
            }
        }
        
        /// <inheritdoc />
        public override float Height
        {
            get => RichAI?.height ?? 2f;
            set
            {
                if (RichAI != null)
                    RichAI.height = value;
            }
        }
        
        /// <inheritdoc />
        public override float Radius
        {
            get => RichAI?.radius ?? 0.5f;
            set
            {
                if (RichAI != null)
                    RichAI.radius = value;
            }
        }
        
        #region Unity Lifecycle
        
        protected override void Awake()
        {
            base.Awake();
            _richAI = GetComponent<RichAI>();
        }
        
        protected override void OnEnable()
        {
            base.OnEnable();
            _richAI = GetComponent<RichAI>();
        }
        
        #endregion
    }
}
