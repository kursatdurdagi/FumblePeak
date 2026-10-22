using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.FlowField;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters
{
    /// <summary>
    /// INavigationAgent adapter that drives movement using a flow-field service when available,
    /// with direct steering fallback when no field is present.
    /// </summary>
    public class FlowFieldAgentAdapter : MonoBehaviour, INavigationAgent
    {
        private static readonly NavigationCapabilities _capabilities = NavigationCapabilities.FlowField;

        [SerializeField] private FlowFieldRuntimeHost flowFieldRuntimeHost;
        [SerializeField] private bool autoFindRuntimeHost = true;
        [SerializeField] private bool buildFieldOnSetDestination = false;
        [SerializeField] private float runtimeHostSearchInterval = 1f;
        [SerializeField] private float flowAlignmentThreshold = 0.15f;
        [SerializeField] [Range(0f, 1f)] private float flowDirectionWeight = 0.7f;

        [SerializeField] private float speed = 3.5f;
        [SerializeField] private float angularSpeed = 120f;
        [SerializeField] private float acceleration = 8f;
        [SerializeField] private float stoppingDistance = 0.5f;
        [SerializeField] private float height = 2f;
        [SerializeField] private float radius = 0.5f;
        [SerializeField] private bool updatePosition = true;
        [SerializeField] private bool updateRotation = true;
        [SerializeField] private bool enableSeparation = true;
        [SerializeField] [Range(0f, 4f)] private float separationWeight = 1.25f;
        [SerializeField] [Min(0.1f)] private float separationRadius = 1f;
        [SerializeField] [Range(1, 24)] private int maxSeparationNeighbors = 8;

        private const float SeparationCellSize = 1.5f;
        private const float SeparationInverseCellSize = 1f / SeparationCellSize;

        private static readonly List<FlowFieldAgentAdapter> _registeredAgents = new List<FlowFieldAgentAdapter>(256);
        private static readonly Dictionary<Vector2Int, List<FlowFieldAgentAdapter>> _separationBuckets = new Dictionary<Vector2Int, List<FlowFieldAgentAdapter>>(128);
        private static int _separationBucketsFrame = -1;

        private bool _enabled = true;
        private bool _isStopped;
        private bool _hasPath;
        private bool _pathPending;

        private Vector3 _position;
        private Vector3 _velocity;
        private Vector3 _desiredVelocity;
        private Vector3 _destination;
        private Vector3 _steeringTarget;
        private float _nextRuntimeHostSearchTime;

        public NavigationType NavigationType => NavigationType.FlowField;
        public NavigationCapabilities Capabilities => _capabilities;
        public Transform Transform => transform;

        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                if (updatePosition)
                    transform.position = value;
            }
        }

        public bool UpdatePosition
        {
            get => updatePosition;
            set
            {
                updatePosition = value;
                if (updatePosition)
                    transform.position = _position;
            }
        }

        public bool UpdateRotation
        {
            get => updateRotation;
            set => updateRotation = value;
        }

        public Vector3 Velocity
        {
            get => _velocity;
            set => _velocity = value;
        }

        public Vector3 DesiredVelocity => _desiredVelocity;

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        public float AngularSpeed
        {
            get => angularSpeed;
            set => angularSpeed = Mathf.Max(0f, value);
        }

        public float Acceleration
        {
            get => acceleration;
            set => acceleration = Mathf.Max(0f, value);
        }

        public float StoppingDistance
        {
            get => stoppingDistance;
            set => stoppingDistance = Mathf.Max(0f, value);
        }

        public float Height
        {
            get => height;
            set => height = Mathf.Max(0f, value);
        }

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0f, value);
        }

        public float RemainingDistance
        {
            get
            {
                if (!_hasPath)
                    return 0f;

                Vector3 delta = _destination - _position;
                delta.y = 0f;
                return delta.magnitude;
            }
        }

        public bool HasPath => _hasPath;
        public bool PathPending => _pathPending;
        public bool IsOnNavMesh
        {
            get
            {
                var field = flowFieldRuntimeHost?.CurrentField;
                if (field == null)
                    return true;

                if (!field.TryWorldToCell(_position, out Vector2Int cell))
                    return false;

                return field.GetWalkable(cell.x, cell.y);
            }
        }

        public Vector3 Destination
        {
            get => _destination;
            set => SetDestination(value);
        }

        public Vector3 SteeringTarget => _steeringTarget;

        public bool IsStopped
        {
            get => _isStopped;
            set
            {
                _isStopped = value;
                if (_isStopped)
                {
                    _velocity = Vector3.zero;
                    _desiredVelocity = Vector3.zero;
                }
            }
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!_enabled)
                {
                    _velocity = Vector3.zero;
                    _desiredVelocity = Vector3.zero;
                    _pathPending = false;
                }
            }
        }

        public bool IsOnOffMeshLink => false;

        public bool SetDestination(Vector3 target)
        {
            _destination = target;
            _steeringTarget = target;
            _hasPath = true;
            _isStopped = false;
            _pathPending = false;

            EnsureRuntimeHost();
            if (buildFieldOnSetDestination && flowFieldRuntimeHost != null)
            {
                bool shouldBuild = ShouldBuildFieldForTarget(target);
                if (shouldBuild)
                {
                    _pathPending = true;
                    flowFieldRuntimeHost.TryBuildField(target);
                    _pathPending = false;
                }
            }

            return true;
        }

        public bool Warp(Vector3 position)
        {
            _position = position;
            if (updatePosition)
                transform.position = position;
            return true;
        }

        public void Stop()
        {
            _isStopped = true;
            ResetPath();
        }

        public void Resume()
        {
            _isStopped = false;
        }

        public void ResetPath()
        {
            _hasPath = false;
            _pathPending = false;
            _velocity = Vector3.zero;
            _desiredVelocity = Vector3.zero;
            _steeringTarget = _position;
        }

        public void Move(Vector3 offset)
        {
            _position += offset;
            if (updatePosition)
                transform.position = _position;
        }

        public bool SamplePosition(Vector3 position, out Vector3 result, float maxDistance)
        {
            result = position;

            var field = flowFieldRuntimeHost?.CurrentField;
            if (field != null && field.TryWorldToCell(position, out Vector2Int cell))
            {
                // Keep caller-provided world-space spread when the sampled cell is walkable.
                // Snapping every agent to the same cell center causes clumping.
                if (field.GetWalkable(cell.x, cell.y))
                {
                    result = position;
                    return true;
                }

                // Fallback for blocked cells: move to the cell center so callers can recover.
                result = field.GetCellCenterWorld(cell.x, cell.y);
                return true;
            }

            return true;
        }

        public bool Raycast(Vector3 targetPosition, out Vector3 hitPosition)
        {
            hitPosition = targetPosition;
            return false;
        }

        public void CompleteOffMeshLink()
        {
            // No-op for flow field backend.
        }

        public T GetUnderlyingAgent<T>() where T : class
        {
            return this as T;
        }

        private void Awake()
        {
            _position = transform.position;
            _destination = _position;
            _steeringTarget = _position;
            EnsureRuntimeHost();
        }

        private void OnEnable()
        {
            if (updatePosition)
                _position = transform.position;
            EnsureRuntimeHost();

            if (!_registeredAgents.Contains(this))
                _registeredAgents.Add(this);
            _separationBucketsFrame = -1;
        }

        private void Update()
        {
            if (!_enabled)
                return;

            if (updatePosition)
                _position = transform.position;

            if (_isStopped || !_hasPath)
            {
                _desiredVelocity = Vector3.zero;
                _velocity = Vector3.zero;
                return;
            }

            Vector3 toDestination = _destination - _position;
            toDestination.y = 0f;
            float distanceToDestination = toDestination.magnitude;

            if (distanceToDestination <= stoppingDistance)
            {
                ResetPath();
                _isStopped = true;
                return;
            }

            Vector3 directDirection = distanceToDestination > 0.0001f
                ? toDestination / distanceToDestination
                : Vector3.zero;

            Vector3 desiredDirection = directDirection;
            _steeringTarget = _destination;

            if (TryGetFlowDirection(out Vector3 flowDirection))
            {
                float alignmentThreshold = Mathf.Clamp(flowAlignmentThreshold, -1f, 1f);
                bool useFlowDirection = directDirection.sqrMagnitude <= 0.0001f ||
                                        Vector3.Dot(flowDirection, directDirection) >= alignmentThreshold;
                if (useFlowDirection)
                {
                    float blendWeight = Mathf.Clamp01(flowDirectionWeight);
                    if (directDirection.sqrMagnitude > 0.0001f && blendWeight < 1f)
                        desiredDirection = Vector3.Slerp(directDirection, flowDirection, blendWeight).normalized;
                    else
                        desiredDirection = flowDirection;

                    _steeringTarget = _position + flowDirection * Mathf.Max(0.1f, radius);
                }
            }

            if (enableSeparation && separationWeight > 0f && separationRadius > 0f)
            {
                Vector3 separation = ComputeSeparationDirection();
                if (separation.sqrMagnitude > 0.0001f)
                {
                    Vector3 combined = desiredDirection + separation * separationWeight;
                    if (combined.sqrMagnitude > 0.0001f)
                        desiredDirection = combined.normalized;
                }
            }

            if (desiredDirection.sqrMagnitude <= 0.0001f)
            {
                ResetPath();
                return;
            }

            _desiredVelocity = desiredDirection * speed;
            _velocity = Vector3.MoveTowards(_velocity, _desiredVelocity, acceleration * Time.deltaTime);

            Vector3 delta = _velocity * Time.deltaTime;
            delta.y = 0f;

            if (delta.sqrMagnitude > toDestination.sqrMagnitude)
                delta = toDestination;

            _position += delta;
            if (updatePosition)
                transform.position = _position;

            if (updateRotation && desiredDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, angularSpeed * Time.deltaTime);
            }

            Vector3 remaining = _destination - _position;
            remaining.y = 0f;
            if (remaining.sqrMagnitude <= stoppingDistance * stoppingDistance)
            {
                ResetPath();
                _isStopped = true;
            }
        }

        private bool TryGetFlowDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            EnsureRuntimeHost();
            if (flowFieldRuntimeHost == null)
                return false;

            if (!flowFieldRuntimeHost.TrySampleDirection(_position, out direction))
                return false;

            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f;
        }

        private Vector3 ComputeSeparationDirection()
        {
            int neighborLimit = Mathf.Max(1, maxSeparationNeighbors);
            float radiusValue = Mathf.Max(0.1f, separationRadius);
            float radiusSq = radiusValue * radiusValue;
            int cellSearchRadius = Mathf.Max(1, Mathf.CeilToInt(radiusValue * SeparationInverseCellSize));

            EnsureSeparationBucketsBuilt();

            Vector3 myPos = _position;
            Vector2Int centerCell = WorldToSeparationCell(myPos);
            Vector3 separation = Vector3.zero;
            int neighborCount = 0;

            for (int z = -cellSearchRadius; z <= cellSearchRadius && neighborCount < neighborLimit; z++)
            {
                for (int x = -cellSearchRadius; x <= cellSearchRadius && neighborCount < neighborLimit; x++)
                {
                    Vector2Int cell = new Vector2Int(centerCell.x + x, centerCell.y + z);
                    if (!_separationBuckets.TryGetValue(cell, out List<FlowFieldAgentAdapter> bucket))
                        continue;

                    for (int i = 0; i < bucket.Count && neighborCount < neighborLimit; i++)
                    {
                        FlowFieldAgentAdapter other = bucket[i];
                        if (other == null || other == this || !other._enabled)
                            continue;

                        Vector3 delta = myPos - other._position;
                        delta.y = 0f;

                        float distanceSq = delta.sqrMagnitude;
                        if (distanceSq > radiusSq)
                            continue;

                        if (distanceSq < 0.0001f)
                        {
                            float jitter = ((GetInstanceID() ^ other.GetInstanceID()) & 1023) * 0.0122718f;
                            delta = new Vector3(Mathf.Cos(jitter), 0f, Mathf.Sin(jitter)) * 0.01f;
                            distanceSq = delta.sqrMagnitude;
                        }

                        float distance = Mathf.Sqrt(distanceSq);
                        float falloff = 1f - Mathf.Clamp01(distance / radiusValue);
                        separation += (delta / distance) * falloff;
                        neighborCount++;
                    }
                }
            }

            if (neighborCount == 0 || separation.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            return separation.normalized;
        }

        private static Vector2Int WorldToSeparationCell(Vector3 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x * SeparationInverseCellSize),
                Mathf.FloorToInt(position.z * SeparationInverseCellSize));
        }

        private static void EnsureSeparationBucketsBuilt()
        {
            int frame = Time.frameCount;
            if (_separationBucketsFrame == frame)
                return;

            _separationBucketsFrame = frame;
            _separationBuckets.Clear();

            for (int i = _registeredAgents.Count - 1; i >= 0; i--)
            {
                FlowFieldAgentAdapter agent = _registeredAgents[i];
                if (agent == null)
                {
                    _registeredAgents.RemoveAt(i);
                    continue;
                }

                if (!agent._enabled)
                    continue;

                Vector2Int cell = WorldToSeparationCell(agent._position);
                if (!_separationBuckets.TryGetValue(cell, out List<FlowFieldAgentAdapter> bucket))
                {
                    bucket = new List<FlowFieldAgentAdapter>(8);
                    _separationBuckets[cell] = bucket;
                }

                bucket.Add(agent);
            }
        }

        private void EnsureRuntimeHost()
        {
            if (flowFieldRuntimeHost != null || !autoFindRuntimeHost)
                return;

            if (Time.time < _nextRuntimeHostSearchTime)
                return;

            float searchInterval = Mathf.Max(0.05f, runtimeHostSearchInterval);
            _nextRuntimeHostSearchTime = Time.time + searchInterval;

#if UNITY_2023_1_OR_NEWER
            flowFieldRuntimeHost = FindFirstObjectByType<FlowFieldRuntimeHost>();
#else
            flowFieldRuntimeHost = FindObjectOfType<FlowFieldRuntimeHost>();
#endif
        }

        private void OnDisable()
        {
            _registeredAgents.Remove(this);
            _separationBucketsFrame = -1;
        }

        private void OnDestroy()
        {
            _registeredAgents.Remove(this);
            _separationBucketsFrame = -1;
        }

        private bool ShouldBuildFieldForTarget(Vector3 target)
        {
            var currentField = flowFieldRuntimeHost?.CurrentField;
            if (currentField == null || !currentField.HasValidTarget)
                return true;

            if (!currentField.TryWorldToCell(target, out Vector2Int targetCell))
                return true;

            return targetCell != currentField.TargetCell;
        }

    }
}
