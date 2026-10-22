using UnityEngine;
using UnityEngine.AI;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// Adapter that wraps Unity's NavMeshAgent to implement INavigationAgent.
    /// This allows the fake physics system to work with Unity's built-in navigation.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NavMeshAgentAdapter : MonoBehaviour, INavigationAgent
    {
        private static readonly NavigationCapabilities _capabilities = NavigationCapabilities.UnityNavMesh;
        private NavMeshAgent _agent;
        
        /// <summary>
        /// The underlying NavMeshAgent component.
        /// </summary>
        public NavMeshAgent Agent
        {
            get
            {
                if (_agent == null)
                    _agent = GetComponent<NavMeshAgent>();
                return _agent;
            }
        }
        
        #region INavigationAgent Implementation
        
        /// <inheritdoc/>
        public NavigationType NavigationType => NavigationType.UnityNavMesh;
        
        /// <inheritdoc/>
        public NavigationCapabilities Capabilities => _capabilities;
        
        /// <inheritdoc/>
        public Transform Transform => Agent.transform;
        
        /// <inheritdoc/>
        public Vector3 Position
        {
            get => Agent.nextPosition;
            set => Agent.nextPosition = value;
        }
        
        /// <inheritdoc/>
        public bool UpdatePosition
        {
            get => Agent.updatePosition;
            set => Agent.updatePosition = value;
        }
        
        /// <inheritdoc/>
        public bool UpdateRotation
        {
            get => Agent.updateRotation;
            set => Agent.updateRotation = value;
        }
        
        /// <inheritdoc/>
        public Vector3 Velocity
        {
            get => Agent.velocity;
            set => Agent.velocity = value;
        }
        
        /// <inheritdoc/>
        public Vector3 DesiredVelocity => Agent.desiredVelocity;
        
        /// <inheritdoc/>
        public float Speed
        {
            get => Agent.speed;
            set => Agent.speed = value;
        }
        
        /// <inheritdoc/>
        public float AngularSpeed
        {
            get => Agent.angularSpeed;
            set => Agent.angularSpeed = value;
        }
        
        /// <inheritdoc/>
        public float Acceleration
        {
            get => Agent.acceleration;
            set => Agent.acceleration = value;
        }
        
        /// <inheritdoc/>
        public float StoppingDistance
        {
            get => Agent.stoppingDistance;
            set => Agent.stoppingDistance = value;
        }
        
        /// <inheritdoc/>
        public float RemainingDistance => Agent.remainingDistance;
        
        /// <inheritdoc/>
        public bool HasPath => Agent.hasPath;
        
        /// <inheritdoc/>
        public bool PathPending => Agent.pathPending;
        
        /// <inheritdoc/>
        public bool IsOnNavMesh => Agent.isOnNavMesh;
        
        /// <inheritdoc/>
        public bool IsStopped
        {
            get => Agent.isStopped;
            set => Agent.isStopped = value;
        }
        
        /// <inheritdoc/>
        public Vector3 SteeringTarget => Agent.steeringTarget;
        
        /// <inheritdoc/>
        public Vector3 Destination
        {
            get => Agent.destination;
            set => Agent.SetDestination(value);
        }
        
        /// <inheritdoc/>
        public float Height
        {
            get => Agent.height;
            set => Agent.height = value;
        }
        
        /// <inheritdoc/>
        public float Radius
        {
            get => Agent.radius;
            set => Agent.radius = value;
        }
        
        /// <inheritdoc/>
        public bool Enabled
        {
            get => Agent.enabled;
            set => Agent.enabled = value;
        }
        
        /// <inheritdoc/>
        public bool SetDestination(Vector3 target)
        {
            return Agent.SetDestination(target);
        }
        
        /// <inheritdoc/>
        public bool Warp(Vector3 position)
        {
            return Agent.Warp(position);
        }
        
        /// <inheritdoc/>
        public void Stop()
        {
            Agent.isStopped = true;
            Agent.ResetPath();
        }
        
        /// <inheritdoc/>
        public void Resume()
        {
            Agent.isStopped = false;
        }
        
        /// <inheritdoc/>
        public void ResetPath()
        {
            Agent.ResetPath();
        }
        
        /// <inheritdoc/>
        public void Move(Vector3 offset)
        {
            Agent.Move(offset);
        }
        
        /// <inheritdoc/>
        public bool SamplePosition(Vector3 position, out Vector3 result, float maxDistance)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
            result = position;
            return false;
        }
        
        /// <inheritdoc/>
        public bool Raycast(Vector3 targetPosition, out Vector3 hitPosition)
        {
            if (Agent.Raycast(targetPosition, out NavMeshHit hit))
            {
                hitPosition = hit.position;
                return true;
            }
            hitPosition = targetPosition;
            return false;
        }
        
        /// <inheritdoc/>
        public void CompleteOffMeshLink()
        {
            Agent.CompleteOffMeshLink();
        }
        
        /// <inheritdoc/>
        public bool IsOnOffMeshLink => Agent.isOnOffMeshLink;
        
        /// <inheritdoc/>
        public T GetUnderlyingAgent<T>() where T : class
        {
            return Agent as T;
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }
        
        private void OnValidate()
        {
            _agent = GetComponent<NavMeshAgent>();
        }
        
        #endregion
    }
}
