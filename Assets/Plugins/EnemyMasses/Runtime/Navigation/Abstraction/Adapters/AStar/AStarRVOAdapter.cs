using UnityEngine;
using Pathfinding;
using Pathfinding.RVO;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// Adapter for A* Pathfinding Project movement with RVO local avoidance.
    /// Works with AIPath or RichAI (and adds an RVOController for local avoidance).
    /// </summary>
    [RequireComponent(typeof(RVOController))]
    public class AStarRVOAdapter : AStarAgentAdapterBase
    {
        private AIPath _aiPath;
        private RichAI _richAI;
        private AILerp _aiLerp;

        /// <inheritdoc />
        public override NavigationType NavigationType => NavigationType.AStarRVO;

        private void CacheComponents()
        {
            if (_aiPath == null)
                _aiPath = GetComponent<AIPath>();
            if (_richAI == null)
                _richAI = GetComponent<RichAI>();
            if (_aiLerp == null)
                _aiLerp = GetComponent<AILerp>();
        }

        /// <inheritdoc />
        public override float Acceleration
        {
            get
            {
                if (_richAI != null)
                    return _richAI.acceleration;
                if (_aiPath != null)
                    return Speed * 4f;
                if (_aiLerp != null)
                    return Speed * 2f;
                return Speed * 4f;
            }
            set
            {
                if (_richAI != null)
                    _richAI.acceleration = value;
            }
        }

        /// <inheritdoc />
        public override float AngularSpeed
        {
            get
            {
                if (_richAI != null)
                    return _richAI.rotationSpeed;
                if (_aiPath != null)
                    return _aiPath.rotationSpeed;
                if (_aiLerp != null)
                    return _aiLerp.rotationSpeed;
                return 360f;
            }
            set
            {
                if (_richAI != null)
                    _richAI.rotationSpeed = value;
                else if (_aiPath != null)
                    _aiPath.rotationSpeed = value;
                else if (_aiLerp != null)
                    _aiLerp.rotationSpeed = value;
            }
        }

        /// <inheritdoc />
        public override bool UpdateRotation
        {
            get
            {
                if (_richAI != null)
                    return _richAI.enableRotation;
                if (_aiPath != null)
                    return _aiPath.enableRotation;
                if (_aiLerp != null)
                    return _aiLerp.enableRotation;
                return true;
            }
            set
            {
                if (_richAI != null)
                    _richAI.enableRotation = value;
                else if (_aiPath != null)
                    _aiPath.enableRotation = value;
                else if (_aiLerp != null)
                    _aiLerp.enableRotation = value;
            }
        }

        /// <inheritdoc />
        public override float StoppingDistance
        {
            get
            {
                if (_richAI != null)
                    return _richAI.endReachedDistance;
                if (_aiPath != null)
                    return _aiPath.endReachedDistance;
                return 0.1f;
            }
            set
            {
                if (_richAI != null)
                    _richAI.endReachedDistance = value;
                else if (_aiPath != null)
                    _aiPath.endReachedDistance = value;
            }
        }

        /// <inheritdoc />
        public override float Height
        {
            get
            {
                if (_richAI != null)
                    return _richAI.height;
                if (_aiPath != null)
                    return _aiPath.height;
                return 2f;
            }
            set
            {
                if (_richAI != null)
                    _richAI.height = value;
                else if (_aiPath != null)
                    _aiPath.height = value;
            }
        }

        /// <inheritdoc />
        public override float Radius
        {
            get
            {
                if (_richAI != null)
                    return _richAI.radius;
                if (_aiPath != null)
                    return _aiPath.radius;
                return 0.5f;
            }
            set
            {
                if (_richAI != null)
                    _richAI.radius = value;
                else if (_aiPath != null)
                    _aiPath.radius = value;
            }
        }

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            CacheComponents();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            CacheComponents();
        }

        #endregion
    }
}
