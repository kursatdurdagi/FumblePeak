using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Navigation
{
    /// <summary>
    /// 3D volumetric navigation agent for flying vehicles, spaceships, drones, and persistent flyers.
    /// Implements <see cref="INavigationAgent"/> so it slots into the existing EnemyMasses navigation pipeline
    /// without any changes to the controller.
    /// 
    /// Supports three steering modes:
    /// <list type="bullet">
    /// <item><see cref="NavigationType3D.Boids"/>: Separation + alignment + cohesion + obstacle avoidance.</item>
    /// <item><see cref="NavigationType3D.Waypoint3D"/>: Follow a sequence of 3D waypoints with smooth interpolation.</item>
    /// <item><see cref="NavigationType3D.DirectSteering"/>: Fly straight toward destination with altitude control.</item>
    /// </list>
    /// 
    /// Network-ready: Position/Velocity/Destination are plain Vector3 properties the existing
    /// <see cref="Networking.INetworkTransformAuthority"/> and <see cref="Networking.INetworkCommandAuthority"/>
    /// already sync as full 3D vectors.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Navigation/Flight 3D Navigation Agent")]
    public class Flight3DNavigationAgent : MonoBehaviour, INavigationAgent
    {
        // ═══════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════

        [Header("Steering")]
        [Tooltip("3D steering mode.")]
        [SerializeField] private NavigationType3D steeringMode = NavigationType3D.Boids;

        [Tooltip("Maximum movement speed (units/sec).")]
        [SerializeField] private float maxSpeed = 8f;

        [Tooltip("Maximum angular speed (degrees/sec).")]
        [SerializeField] private float maxAngularSpeed = 180f;

        [Tooltip("Acceleration applied when changing velocity (units/sec²).")]
        [SerializeField] private float maxAcceleration = 12f;

        [Tooltip("Distance from destination at which the agent considers itself arrived.")]
        [SerializeField] private float arrivalDistance = 1.5f;

        [Tooltip("Agent bounding radius for separation and avoidance.")]
        [SerializeField] private float agentRadius = 1f;

        [Tooltip("Agent bounding height for avoidance.")]
        [SerializeField] private float agentHeight = 2f;

        [Header("Altitude")]
        [Tooltip("Target cruising altitude above ground (world units). Set by EnemyCrowd.cruisingAltitude on spawn.")]
        [SerializeField] private float targetAltitude = 10f;

        [Tooltip("Random variance applied per-agent to targetAltitude.")]
        [SerializeField] private float altitudeVariance = 3f;

        [Tooltip("Max vertical speed (units/sec) for altitude changes.")]
        [SerializeField] private float verticalSpeed = 5f;

        [Header("Hover Bob")]
        [Tooltip("Amplitude of the idle hover bob (world units). 0 = disabled.")]
        [SerializeField] private float hoverBobAmplitude = 0.15f;

        [Tooltip("Frequency of the idle hover bob (cycles per second).")]
        [SerializeField] private float hoverBobFrequency = 0.6f;

        [Header("Altitude Behaviour")]
        [Tooltip("When true, the agent maintains cruising altitude and navigates XZ only.")]
        [SerializeField] private bool _maintainAltitude = true;

        [Header("Boids")]
        [Tooltip("Weight for separation steering (avoid nearby agents).")]
        [SerializeField] private float separationWeight = 1.5f;

        [Tooltip("Weight for alignment steering (match heading of nearby agents).")]
        [SerializeField] private float alignmentWeight = 0.5f;

        [Tooltip("Weight for cohesion steering (move toward group center).")]
        [SerializeField] private float cohesionWeight = 0.3f;

        [Tooltip("Weight for destination-seeking steering.")]
        [SerializeField] private float seekWeight = 1.0f;

        [Tooltip("Radius for neighbor detection in boids calculations.")]
        [SerializeField] private float neighborRadius = 6f;

        [Tooltip("Maximum number of neighbors to consider (performance cap).")]
        [SerializeField] private int maxNeighbors = 8;

        [Header("Obstacle Avoidance")]
        [Tooltip("Layers treated as obstacles for raycasting.")]
        [SerializeField] private LayerMask obstacleLayers = ~0;

        [Tooltip("Raycast distance for forward obstacle detection.")]
        [SerializeField] private float obstacleDetectionRange = 10f;

        [Tooltip("Weight for obstacle avoidance steering.")]
        [SerializeField] private float obstacleAvoidanceWeight = 2f;

        [Header("Flight Feel (set via EnemyCrowd)")]
        [Tooltip("How quickly the visual heading adapts to velocity changes.")]
        [SerializeField] private float headingSmoothRate = 5f;

        [Tooltip("Maximum nose-up/down pitch angle in degrees.")]
        [SerializeField] private float maxPitchAngle = 30f;

        [Tooltip("Maximum bank/roll angle in degrees when turning.")]
        [SerializeField] private float maxBankAngle = 25f;

        [Tooltip("How quickly the bank angle reaches its target (degrees/sec).")]
        [SerializeField] private float bankSpeed = 90f;

        [Tooltip("Which rotation axes are locked (set via EnemyCrowd).")]
        [SerializeField] private RotationAxisLock rotationAxisLock = RotationAxisLock.None;

        // ═══════════════════════════════════════════════════════════════════
        // RUNTIME STATE
        // ═══════════════════════════════════════════════════════════════════

        private Vector3 _destination;
        private Vector3 _velocity;
        private Vector3 _desiredVelocity;
        private bool _hasDestination;
        private bool _isStopped;
        private bool _enabled = true;
        private bool _updatePosition = true;
        private bool _updateRotation = true;
        private float _personalAltitude; // Per-agent altitude with variance
        private float _bobPhase;              // Per-agent random phase for bob
        private Vector3 _smoothedForward;     // Filtered heading for rotation (prevents fish-like oscillation)
        private float _currentBankDeg;           // Smoothed bank angle for gradual roll in/out
        private Vector3 _velocitySmoothRef;      // SmoothDamp helper for velocity
        private float _smoothedPitchDeg;         // Low-pass filtered pitch for stable nose angle
        private float _smoothedAltitudeVel;      // Smoothed vertical velocity for altitude corrections

        // Spatial bucket for boids neighbor queries
        private static readonly List<Flight3DNavigationAgent> _allAgents = new List<Flight3DNavigationAgent>(512);
        private static readonly Dictionary<long, List<int>> _spatialBuckets = new Dictionary<long, List<int>>(128);
        private static int _spatialBucketsFrame = -1;
        private const float SpatialCellSize = 6f;

        // ═══════════════════════════════════════════════════════════════════
        // INavigationAgent IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════════════

        public NavigationType NavigationType => NavigationType.Flight3D;

        public NavigationCapabilities Capabilities => _capabilities;

        private static readonly NavigationCapabilities _capabilities = new NavigationCapabilities(
            requiresSurfaceValidationForControl: false,
            supportsPathPending: false,
            supportsOffMeshLinks: false,
            supportsNavmeshOffsetImpulse: false,
            supportsMoveBasedKnockback: true,
            shouldThrottleFrequentDestinationUpdates: false,
            supportsPerAgentAvoidanceTuning: false);

        public Transform Transform => transform;

        public Vector3 Position
        {
            get => transform.position;
            set => transform.position = value;
        }

        public bool UpdatePosition
        {
            get => _updatePosition;
            set => _updatePosition = value;
        }

        public bool UpdateRotation
        {
            get => _updateRotation;
            set => _updateRotation = value;
        }

        public Vector3 Velocity
        {
            get => _velocity;
            set => _velocity = value;
        }

        public Vector3 DesiredVelocity => _desiredVelocity;

        public float Speed
        {
            get => maxSpeed;
            set => maxSpeed = Mathf.Max(0.01f, value);
        }

        public float AngularSpeed
        {
            get => maxAngularSpeed;
            set => maxAngularSpeed = value;
        }

        public float Acceleration
        {
            get => maxAcceleration;
            set => maxAcceleration = value;
        }

        public float StoppingDistance
        {
            get => arrivalDistance;
            set => arrivalDistance = Mathf.Max(0.01f, value);
        }

        public float Height
        {
            get => agentHeight;
            set => agentHeight = value;
        }

        public float Radius
        {
            get => agentRadius;
            set => agentRadius = value;
        }

        public float RemainingDistance
        {
            get
            {
                if (!_hasDestination) return 0f;
                // When maintainAltitude is ON, report XZ-only distance so callers
                // (combat range checks, formation arrival, etc.) aren't thrown off
                // by the altitude gap between the airborne agent and its ground-level destination.
                if (_maintainAltitude)
                {
                    float dx = transform.position.x - _destination.x;
                    float dz = transform.position.z - _destination.z;
                    return Mathf.Sqrt(dx * dx + dz * dz);
                }
                return Vector3.Distance(transform.position, _destination);
            }
        }

        public bool HasPath => _hasDestination;

        public bool PathPending => false;

        public bool IsOnNavMesh => true; // Always "on mesh" — there is no mesh to fall off

        public Vector3 Destination
        {
            get => _destination;
            set
            {
                _destination = value;
                _hasDestination = true;
                _isStopped = false;
            }
        }

        public Vector3 SteeringTarget => _hasDestination ? _destination : transform.position;

        public bool IsStopped
        {
            get => _isStopped;
            set
            {
                _isStopped = value;
                if (value) _velocity = Vector3.zero;
            }
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                enabled = value;
            }
        }

        public bool IsOnOffMeshLink => false;
        public void CompleteOffMeshLink() { }

        public bool SetDestination(Vector3 target)
        {
            Destination = target;
            return true;
        }

        public bool Warp(Vector3 position)
        {
            transform.position = position;
            _velocity = Vector3.zero;
            return true;
        }

        public void Stop()
        {
            IsStopped = true;
            _hasDestination = false;
        }

        public void Resume()
        {
            _isStopped = false;
        }

        public void ResetPath()
        {
            _hasDestination = false;
            _velocity = Vector3.zero;
        }

        public void Move(Vector3 offset)
        {
            transform.position += offset;
        }

        public bool SamplePosition(Vector3 position, out Vector3 result, float maxDistance)
        {
            // No navmesh to sample — any position is valid in 3D space
            result = position;
            return true;
        }

        public bool Raycast(Vector3 targetPosition, out Vector3 hitPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            if (Physics.Raycast(transform.position, direction.normalized, out RaycastHit hit, direction.magnitude, obstacleLayers))
            {
                hitPosition = hit.point;
                return true;
            }
            hitPosition = targetPosition;
            return false;
        }

        public T GetUnderlyingAgent<T>() where T : class
        {
            return this as T;
        }

        // ═══════════════════════════════════════════════════════════════════
        // PUBLIC CONFIGURATION API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Configure from an EnemyCrowd ScriptableObject. Called by the spawn system.
        /// </summary>
        public void ConfigureFromCrowd(EnemyCrowd crowd)
        {
            steeringMode = crowd.flight3DNavigationType;
            targetAltitude = crowd.cruisingAltitude;
            altitudeVariance = crowd.altitudeVariance;
            verticalSpeed = crowd.verticalSpeed;
            hoverBobAmplitude = crowd.hoverBobAmplitude;
            hoverBobFrequency = crowd.hoverBobFrequency;
            _maintainAltitude = crowd.maintainAltitude;
            maxSpeed = crowd.agentBaseSpeed * crowd.speedMultiplier;
            agentRadius = crowd.IsNonHumanoid ? crowd.vehicleBoundingRadius : 0.5f;
            agentHeight = crowd.IsNonHumanoid ? crowd.vehicleBoundingHeight : 2f;

            _personalAltitude = targetAltitude + Random.Range(-altitudeVariance, altitudeVariance);
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);

            // Wire flight-feel from crowd
            maxBankAngle = crowd.bankAngle;
            bankSpeed = crowd.bankSpeed;
            headingSmoothRate = crowd.headingSmoothRate;
            maxPitchAngle = crowd.maxPitchAngle;
            rotationAxisLock = crowd.rotationAxisLock;
        }

        /// <summary>
        /// Per-agent target altitude including variance.
        /// </summary>
        public float PersonalAltitude => _personalAltitude;

        /// <summary>
        /// Current steering mode.
        /// </summary>
        public NavigationType3D SteeringMode
        {
            get => steeringMode;
            set => steeringMode = value;
        }

        // ═══════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _personalAltitude = targetAltitude + Random.Range(-altitudeVariance, altitudeVariance);
            _smoothedForward = transform.forward;
        }

        private void OnEnable()
        {
            _allAgents.Add(this);
        }

        private void OnDisable()
        {
            _allAgents.Remove(this);
        }

        private int _bobDebugLogCount = 0;

        private void Update()
        {
            // Even when IsStopped is true, keep altitude + hover bob active so
            // idle flying agents don't "freeze" at spawn until first move order.
            if (!_enabled || !_updatePosition)
            {
                // Log once per agent when update is fully blocked.
                if (_bobDebugLogCount < 3 && hoverBobAmplitude > 0f)
                {
                    Debug.Log($"[HoverBobDebug] Flight3D agent '{gameObject.name}' Update SKIPPED: _enabled={_enabled} _updatePosition={_updatePosition} hoverBobAmplitude={hoverBobAmplitude:F3} hasDestination={_hasDestination}");
                    _bobDebugLogCount++;
                }
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 steering = Vector3.zero;

            // Preserve idle hover/altitude behavior while stopped, but disable
            // horizontal steering/pathing until movement resumes.
            if (!_isStopped)
            {
                switch (steeringMode)
                {
                    case NavigationType3D.Boids:
                        steering = CalculateBoidsSteering();
                        break;
                    case NavigationType3D.Waypoint3D:
                    case NavigationType3D.DirectSteering:
                        steering = CalculateDirectSteering();
                        break;
                }

                // Apply obstacle avoidance only while actively steering
                steering += CalculateObstacleAvoidance() * obstacleAvoidanceWeight;
            }

            // Apply altitude control
            steering += CalculateAltitudeSteering();

            // Clamp and apply via SmoothDamp — eliminates frame-to-frame jitter
            _desiredVelocity = Vector3.ClampMagnitude(steering, maxSpeed);
            float smoothTime = Mathf.Max(0.05f, 1f / maxAcceleration); // acceleration-derived damping
            _velocity = Vector3.SmoothDamp(_velocity, _desiredVelocity, ref _velocitySmoothRef, smoothTime, maxSpeed, dt);

            // Arrival slowdown — use XZ distance when maintainAltitude is ON so the
            // altitude gap between agent and ground-level destination doesn't prevent
            // the agent from properly arriving and slowing at its formation slot.
            if (!_isStopped && _hasDestination)
            {
                float dist;
                if (_maintainAltitude)
                {
                    Vector3 xzDiff = new Vector3(
                        transform.position.x - _destination.x, 0f,
                        transform.position.z - _destination.z);
                    dist = xzDiff.magnitude;
                }
                else
                {
                    dist = Vector3.Distance(transform.position, _destination);
                }

                if (dist < arrivalDistance * 3f)
                {
                    float slowdown = Mathf.Clamp01(dist / (arrivalDistance * 3f));
                    _velocity *= slowdown;
                }
            }

            // Add hover bob as a smooth sine offset in velocity (not position)
            // so it integrates naturally without position-level judder
            if (hoverBobAmplitude > 0f && _velocity.sqrMagnitude < maxSpeed * maxSpeed * 0.25f)
            {
                float bobVelocity = Mathf.Cos(Time.time * hoverBobFrequency * 6.2831853f + _bobPhase)
                                    * hoverBobAmplitude * hoverBobFrequency * 6.2831853f;
                _velocity.y += bobVelocity * dt; // gentle, derivative-based bob
            }

            // Move
            if (_updatePosition)
            {
                transform.position += _velocity * dt;
            }

            // Rotate toward smoothed heading with pitch and bank
            if (_updateRotation && _velocity.sqrMagnitude > 0.01f)
            {
                bool lockPitch = (rotationAxisLock & RotationAxisLock.LockPitch) != 0;
                bool lockYaw   = (rotationAxisLock & RotationAxisLock.LockYaw)   != 0;
                bool lockRoll  = (rotationAxisLock & RotationAxisLock.LockRoll)  != 0;

                Vector3 flatVel = new Vector3(_velocity.x, 0f, _velocity.z);

                // Smoothly track horizontal heading — prevents fish-like oscillation
                if (!lockYaw && flatVel.sqrMagnitude > 0.1f)
                {
                    _smoothedForward = Vector3.Slerp(
                        _smoothedForward, flatVel.normalized,
                        dt * headingSmoothRate).normalized;
                }

                if (_smoothedForward.sqrMagnitude < 0.001f)
                    _smoothedForward = transform.forward;

                // Gentle pitch based on climb / descent rate — smoothed to prevent nose wobble
                float speed = flatVel.magnitude;
                float targetPitchDeg = 0f;
                if (!lockPitch && speed > 0.5f && maxPitchAngle > 0f)
                {
                    targetPitchDeg = Mathf.Clamp(
                        Mathf.Atan2(_velocity.y, speed) * Mathf.Rad2Deg,
                        -maxPitchAngle, maxPitchAngle);
                }
                float pitchDeg = lockPitch ? 0f : Mathf.Lerp(_smoothedPitchDeg, targetPitchDeg, dt * headingSmoothRate);
                _smoothedPitchDeg = pitchDeg;

                // Bank into turns for an airplane-like feel
                float targetBankDeg = 0f;
                if (!lockRoll && maxBankAngle > 0f && flatVel.sqrMagnitude > 0.1f)
                {
                    float turn = Vector3.Cross(_smoothedForward, flatVel.normalized).y;
                    targetBankDeg = Mathf.Clamp(-turn * maxBankAngle * 3f, -maxBankAngle, maxBankAngle);
                }

                // Smoothly roll into/out of bank using bankSpeed (degrees/sec)
                _currentBankDeg = lockRoll ? 0f : Mathf.MoveTowards(_currentBankDeg, targetBankDeg, bankSpeed * dt);

                // Compose: yaw from smoothed heading, then local pitch & bank
                Quaternion yawRot  = Quaternion.LookRotation(_smoothedForward, Vector3.up);
                Quaternion finalRot = yawRot * Quaternion.Euler(-pitchDeg, 0f, _currentBankDeg);

                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, finalRot, maxAngularSpeed * dt);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEERING CALCULATIONS
        // ═══════════════════════════════════════════════════════════════════

        private Vector3 CalculateDirectSteering()
        {
            if (!_hasDestination) return Vector3.zero;

            Vector3 toTarget = _destination - transform.position;

            // When maintainAltitude is ON, flatten seek to XZ so altitude steering
            // has exclusive control over Y. Without this, the seek vector aims at
            // the ground-level destination and pulls the agent downward, causing
            // flying vehicles to "land" when given formation or move commands.
            if (_maintainAltitude)
                toTarget.y = 0f;

            float dist = toTarget.magnitude;

            if (dist < 0.01f) return Vector3.zero;

            return toTarget.normalized * maxSpeed;
        }

        private Vector3 CalculateBoidsSteering()
        {
            RebuildSpatialBuckets();

            Vector3 separation = Vector3.zero;
            Vector3 alignment = Vector3.zero;
            Vector3 cohesion = Vector3.zero;
            int neighborCount = 0;

            // Query neighbors via spatial hash
            Vector3 pos = transform.position;
            int cx = Mathf.FloorToInt(pos.x / SpatialCellSize);
            int cy = Mathf.FloorToInt(pos.y / SpatialCellSize);
            int cz = Mathf.FloorToInt(pos.z / SpatialCellSize);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long key = PackCellKey(cx + dx, cy + dy, cz + dz);
                if (!_spatialBuckets.TryGetValue(key, out var bucket)) continue;

                for (int i = 0; i < bucket.Count && neighborCount < maxNeighbors; i++)
                {
                    int agentIdx = bucket[i];
                    if (agentIdx < 0 || agentIdx >= _allAgents.Count) continue;
                    var other = _allAgents[agentIdx];
                    if (other == this || other == null) continue;

                    Vector3 diff = pos - other.transform.position;
                    float distSq = diff.sqrMagnitude;
                    float radiusSq = neighborRadius * neighborRadius;

                    if (distSq > radiusSq || distSq < 0.001f) continue;

                    float dist = Mathf.Sqrt(distSq);

                    // Separation: inversely proportional to distance (clamped to avoid spikes at very close range)
                    float invDist = 1f / Mathf.Max(dist, agentRadius * 0.5f);
                    separation += diff.normalized * invDist;

                    // Alignment: match neighbor's velocity
                    alignment += other._velocity;

                    // Cohesion: move toward neighbor
                    cohesion += other.transform.position;

                    neighborCount++;
                }
            }

            Vector3 steer = Vector3.zero;

            if (neighborCount > 0)
            {
                separation /= neighborCount;
                steer += separation.normalized * maxSpeed * separationWeight;

                alignment /= neighborCount;
                steer += (alignment.normalized * maxSpeed - _velocity).normalized * maxSpeed * alignmentWeight;

                cohesion = (cohesion / neighborCount - pos);
                steer += cohesion.normalized * maxSpeed * cohesionWeight;
            }

            // Seek toward destination
            if (_hasDestination)
            {
                Vector3 seekVec = _destination - pos;

                // When maintainAltitude is ON, flatten seek to XZ so altitude steering
                // has exclusive control over Y (prevents flying agents from landing
                // when given formation or move commands at ground level).
                if (_maintainAltitude)
                    seekVec.y = 0f;

                if (seekVec.sqrMagnitude > 0.0001f)
                    steer += seekVec.normalized * maxSpeed * seekWeight;
            }

            return steer;
        }

        private Vector3 CalculateAltitudeSteering()
        {
            float currentY = transform.position.y;
            float targetY = _personalAltitude;

            // If we have a destination and maintainAltitude is off, fly to destination's Y.
            // Otherwise always hover at personal cruising altitude (RTS-style).
            if (_hasDestination && !_maintainAltitude)
            {
                targetY = _destination.y;
            }

            float diff = targetY - currentY;

            // Dead zone — no correction needed when very close to target altitude
            if (Mathf.Abs(diff) < 0.1f)
            {
                _smoothedAltitudeVel = Mathf.Lerp(_smoothedAltitudeVel, 0f, Time.deltaTime * 4f);
                return new Vector3(0f, _smoothedAltitudeVel, 0f);
            }

            // Proportional target, clamped to verticalSpeed, then smoothed
            float targetVel = Mathf.Clamp(diff * 2f, -verticalSpeed, verticalSpeed);
            _smoothedAltitudeVel = Mathf.Lerp(_smoothedAltitudeVel, targetVel, Time.deltaTime * 3f);
            return new Vector3(0f, _smoothedAltitudeVel, 0f);
        }

        private Vector3 CalculateObstacleAvoidance()
        {
            if (_velocity.sqrMagnitude < 0.01f) return Vector3.zero;

            Vector3 forward = _velocity.normalized;
            if (Physics.SphereCast(transform.position, agentRadius * 0.5f, forward, out RaycastHit hit,
                    obstacleDetectionRange, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                // Steer away from the obstacle normal
                Vector3 avoidDir = Vector3.Reflect(forward, hit.normal);
                float urgency = 1f - (hit.distance / obstacleDetectionRange);
                return avoidDir * maxSpeed * urgency;
            }

            return Vector3.zero;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPATIAL HASHING
        // ═══════════════════════════════════════════════════════════════════

        private static void RebuildSpatialBuckets()
        {
            int frame = Time.frameCount;
            if (frame == _spatialBucketsFrame) return;
            _spatialBucketsFrame = frame;

            // Clear buckets (reuse lists)
            foreach (var kv in _spatialBuckets)
                kv.Value.Clear();

            for (int i = 0; i < _allAgents.Count; i++)
            {
                var agent = _allAgents[i];
                if (agent == null || !agent.isActiveAndEnabled) continue;

                Vector3 p = agent.transform.position;
                long key = PackCellKey(
                    Mathf.FloorToInt(p.x / SpatialCellSize),
                    Mathf.FloorToInt(p.y / SpatialCellSize),
                    Mathf.FloorToInt(p.z / SpatialCellSize));

                if (!_spatialBuckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(16);
                    _spatialBuckets[key] = list;
                }
                list.Add(i);
            }
        }

        private static long PackCellKey(int x, int y, int z)
        {
            // Pack three 21-bit signed values into a 64-bit key
            long lx = (long)(x & 0x1FFFFF);
            long ly = (long)(y & 0x1FFFFF);
            long lz = (long)(z & 0x1FFFFF);
            return (lx << 42) | (ly << 21) | lz;
        }

        // ═══════════════════════════════════════════════════════════════════
        // GIZMOS
        // ═══════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, agentRadius);

            if (_hasDestination)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, _destination);
                Gizmos.DrawWireSphere(_destination, 0.3f);
            }

            if (_velocity.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, _velocity);
            }

            // Altitude reference
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Vector3 groundPoint = new Vector3(transform.position.x, 0f, transform.position.z);
            Gizmos.DrawLine(transform.position, groundPoint);
        }
    }
}
