using UnityEngine;
using Pathfinding;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// Base class for all A* Pathfinding Project adapters (v5.4+).
    /// Provides common implementation for IAstarAI-based agents.
    /// </summary>
    public abstract class AStarAgentAdapterBase : MonoBehaviour, INavigationAgent
    {
        private static readonly NavigationCapabilities _capabilities = NavigationCapabilities.AStar;
        protected IAstarAI _ai;
        protected Vector3 _lastDestination;
        private int _lastIsOnNavMeshFrame = -1;
        private float _lastIsOnNavMeshTime = -1f;
        private bool _lastIsOnNavMesh;
        private const float IS_ON_NAVMESH_REFRESH_INTERVAL = 0.2f;
        
        /// <summary>
        /// The underlying A* AI component.
        /// </summary>
        public IAstarAI AI
        {
            get
            {
                if (_ai == null)
                    _ai = GetComponent<IAstarAI>();
                return _ai;
            }
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // IDENTITY
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// The navigation type for this adapter.
        /// </summary>
        public abstract NavigationType NavigationType { get; }
        
        /// <inheritdoc />
        public NavigationCapabilities Capabilities => _capabilities;
        
        /// <inheritdoc />
        public Transform Transform => transform;
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // POSITION & MOVEMENT
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public Vector3 Position
        {
            get => AI?.position ?? transform.position;
            set
            {
                // In A* 5.4+, position is read-only on IAstarAI.
                // Use Teleport without clearing the path to avoid canceling in-flight requests
                // (Position is synced every frame by the navigation controller).
                if (AI != null)
                {
                    Vector3 current = AI.position;
                    if ((current - value).sqrMagnitude > 0.0001f)
                        AI.Teleport(value, false);
                }
            }
        }
        
        /// <inheritdoc />
        /// <remarks>
        /// For A* Pathfinding: Maps to AIBase.updatePosition which controls
        /// whether the agent syncs transform position with its internal position.
        /// Note: This is separate from simulateMovement which controls whether
        /// movement is simulated at all.
        /// </remarks>
        public bool UpdatePosition
        {
            get
            {
                if (AI is AIBase aiBase)
                    return aiBase.updatePosition;
                return true; // Default to true for other AI types
            }
            set
            {
                if (AI is AIBase aiBase)
                    aiBase.updatePosition = value;
            }
        }
        
        /// <inheritdoc />
        public abstract bool UpdateRotation { get; set; }
        
        /// <inheritdoc />
        public Vector3 Velocity
        {
            get
            {
                if (AI == null)
                    return Vector3.zero;

                // If stopped or not simulating movement, treat velocity as zero
                if (AI.isStopped || !AI.simulateMovement)
                    return Vector3.zero;

                // If no path is available (and none pending), treat as idle
                if (!AI.hasPath && !AI.pathPending)
                    return Vector3.zero;

                return AI.velocity;
            }
            set
            {
                if (AI == null)
                    return;

                // Best-effort: try to zero desired velocity when stopping
                if (value.sqrMagnitude <= 0.0001f)
                {
                    try
                    {
                        AI.desiredVelocityWithoutLocalAvoidance = Vector3.zero;
                    }
                    catch
                    {
                        // AILerp may throw if setting desiredVelocityWithoutLocalAvoidance
                    }
                }
            }
        }
        
        /// <inheritdoc />
        public Vector3 DesiredVelocity
        {
            get
            {
                if (AI == null)
                    return Vector3.zero;

                if (AI.isStopped || !AI.simulateMovement)
                    return Vector3.zero;

                if (AI.reachedDestination || (!AI.hasPath && !AI.pathPending))
                    return Vector3.zero;

                return AI.desiredVelocity;
            }
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // SPEED & MOVEMENT PARAMETERS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public float Speed
        {
            get => AI?.maxSpeed ?? 3.5f;
            set
            {
                if (AI != null)
                    AI.maxSpeed = value;
            }
        }
        
        /// <inheritdoc />
        public abstract float Acceleration { get; set; }
        
        /// <inheritdoc />
        public abstract float AngularSpeed { get; set; }
        
        /// <inheritdoc />
        public abstract float StoppingDistance { get; set; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // AGENT DIMENSIONS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public abstract float Height { get; set; }
        
        /// <inheritdoc />
        public abstract float Radius { get; set; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // PATH STATUS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public float RemainingDistance => AI?.remainingDistance ?? 0f;
        
        /// <inheritdoc />
        public bool HasPath => AI?.hasPath ?? false;
        
        /// <inheritdoc />
        public bool PathPending => AI?.pathPending ?? false;
        
        /// <inheritdoc />
        public bool IsOnNavMesh
        {
            get
            {
                if (AI == null || AstarPath.active == null)
                    return false;

                // If we already have a path (or are computing one), assume we're on the graph.
                // This avoids expensive nearest-node queries every frame.
                if (AI.hasPath || AI.pathPending || AI.reachedDestination)
                {
                    _lastIsOnNavMesh = true;
                    _lastIsOnNavMeshFrame = Time.frameCount;
                    _lastIsOnNavMeshTime = Time.time;
                    return true;
                }

                // Cache result per-frame (and with a small time window) to reduce GetNearest calls.
                if (Time.frameCount == _lastIsOnNavMeshFrame)
                    return _lastIsOnNavMesh;

                if (Time.time - _lastIsOnNavMeshTime < IS_ON_NAVMESH_REFRESH_INTERVAL)
                    return _lastIsOnNavMesh;

                var nearest = AstarPath.active.GetNearest(transform.position);
                _lastIsOnNavMesh = nearest.node != null && nearest.node.Walkable;
                _lastIsOnNavMeshFrame = Time.frameCount;
                _lastIsOnNavMeshTime = Time.time;
                return _lastIsOnNavMesh;
            }
        }
        
        /// <inheritdoc />
        public Vector3 Destination
        {
            get => AI?.destination ?? _lastDestination;
            set
            {
                _lastDestination = value;
                if (AI != null)
                    AI.destination = value;
            }
        }
        
        /// <inheritdoc />
        public Vector3 SteeringTarget => AI?.steeringTarget ?? transform.position;
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // CONTROL FLAGS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public bool IsStopped
        {
            get => AI?.isStopped ?? true;
            set
            {
                if (AI != null)
                    AI.isStopped = value;
            }
        }
        
        /// <inheritdoc />
        public bool Enabled
        {
            get => AI?.simulateMovement ?? false;
            set
            {
                if (AI != null)
                    AI.simulateMovement = value;
            }
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // OFF-MESH LINKS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public bool IsOnOffMeshLink => false;
        
        /// <inheritdoc />
        public void CompleteOffMeshLink()
        {
            // A* handles off-mesh links automatically
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // COMMANDS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public bool SetDestination(Vector3 target)
        {
            if (AI == null)
                return false;

            bool sameTarget = (_lastDestination - target).sqrMagnitude <= 0.0004f;
            _lastDestination = target;

            // Avoid spamming new path requests when the target hasn't meaningfully changed.
            // This prevents canceling in-flight path calculations every frame.
            if (sameTarget && (AI.pathPending || AI.hasPath))
            {
                AI.destination = target;
                return true;
            }

            // If a path request is already pending, just update the destination.
            // Let the automatic repath handle the new target instead of canceling in-flight requests.
            if (AI.pathPending)
            {
                AI.destination = target;
                return true;
            }
            
            // CRITICAL: Ensure the agent is ready to move
            // 1. Enable movement simulation (required for AIPath to actually move)
            if (!AI.simulateMovement)
                AI.simulateMovement = true;
            
            // 2. Ensure agent is not stopped
            if (AI.isStopped)
                AI.isStopped = false;
            
            // 3. Set destination and trigger path search
            AI.destination = target;
            AI.SearchPath();
            
            return true;
        }
        
        /// <inheritdoc />
        public bool Warp(Vector3 position)
        {
            if (AI == null) return false;
            AI.Teleport(position);
            return true;
        }
        
        /// <inheritdoc />
        public void Stop()
        {
            if (AI != null)
            {
                AI.isStopped = true;
                AI.SetPath(null);
            }
        }
        
        /// <inheritdoc />
        public void Resume()
        {
            if (AI != null)
            {
                AI.isStopped = false;
                AI.SearchPath();
            }
        }
        
        /// <inheritdoc />
        public void ResetPath()
        {
            if (AI == null)
                return;

            // Avoid canceling in-flight path calculations (prevents "Canceled by script" spam).
            if (AI.pathPending)
            {
                AI.isStopped = true;
                return;
            }

            AI.SetPath(null);
        }
        
        /// <inheritdoc />
        public void Move(Vector3 offset)
        {
            if (AI != null)
                AI.Move(offset);
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // QUERIES
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public bool SamplePosition(Vector3 position, out Vector3 result, float maxDistance)
        {
            result = position;
            
            if (AstarPath.active == null)
                return false;
            
            var nearest = AstarPath.active.GetNearest(position);
            if (nearest.node != null && nearest.node.Walkable)
            {
                float distance = Vector3.Distance(position, (Vector3)nearest.position);
                if (distance <= maxDistance)
                {
                    result = (Vector3)nearest.position;
                    return true;
                }
            }
            
            return false;
        }
        
        /// <inheritdoc />
        public bool Raycast(Vector3 targetPosition, out Vector3 hitPosition)
        {
            hitPosition = targetPosition;
            // Basic implementation - return false (no obstacle)
            return false;
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // UNDERLYING AGENT ACCESS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <inheritdoc />
        public T GetUnderlyingAgent<T>() where T : class
        {
            return GetComponent<T>();
        }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // UNITY LIFECYCLE
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        protected virtual void Awake()
        {
            _ai = GetComponent<IAstarAI>();
        }
        
        protected virtual void OnEnable()
        {
            _ai = GetComponent<IAstarAI>();
        }
    }
}
