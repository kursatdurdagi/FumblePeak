using UnityEngine;
using Pathfinding;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// Adapter for A* Pathfinding Project's AILerp component (v5.4+).
    /// AILerp moves the agent by linearly interpolating along the path.
    /// This is useful for grid-based games or when exact path following is needed.
    /// Note: AILerp does NOT use physics or avoid other agents.
    /// </summary>
    [RequireComponent(typeof(AILerp))]
    public class AStarAILerpAdapter : AStarAgentAdapterBase
    {
        private AILerp _aiLerp;
        
        /// <summary>
        /// The underlying AILerp component.
        /// </summary>
        public AILerp AILerp
        {
            get
            {
                if (_aiLerp == null)
                    _aiLerp = GetComponent<AILerp>();
                return _aiLerp;
            }
        }
        
        /// <inheritdoc />
        public override NavigationType NavigationType => NavigationType.AStarAILerp;
        
        /// <inheritdoc />
        public override float Acceleration
        {
            // AILerp doesn't have acceleration - it interpolates at a fixed speed
            get => Speed * 2f;
            set { /* AILerp doesn't support acceleration */ }
        }
        
        /// <inheritdoc />
        public override float AngularSpeed
        {
            get => AILerp?.rotationSpeed ?? 360f;
            set
            {
                if (AILerp != null)
                    AILerp.rotationSpeed = value;
            }
        }
        
        /// <inheritdoc />
        public override bool UpdateRotation
        {
            get => AILerp?.enableRotation ?? true;
            set
            {
                if (AILerp != null)
                    AILerp.enableRotation = value;
            }
        }
        
        /// <inheritdoc />
        public override float StoppingDistance
        {
            // AILerp doesn't have endReachedDistance, return default
            get => 0.1f;
            set { /* AILerp doesn't support stopping distance */ }
        }
        
        /// <inheritdoc />
        public override float Height
        {
            // AILerp doesn't have height property
            get => 2f;
            set { /* AILerp doesn't support height */ }
        }
        
        /// <inheritdoc />
        public override float Radius
        {
            // AILerp doesn't have radius property
            get => 0.5f;
            set { /* AILerp doesn't support radius */ }
        }
        
        #region Unity Lifecycle
        
        protected override void Awake()
        {
            base.Awake();
            _aiLerp = GetComponent<AILerp>();
        }
        
        protected override void OnEnable()
        {
            base.OnEnable();
            _aiLerp = GetComponent<AILerp>();
        }
        
        #endregion
    }
}
