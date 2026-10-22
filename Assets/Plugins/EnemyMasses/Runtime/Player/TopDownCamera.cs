using UnityEngine;
using UnityEngine.InputSystem;

namespace Arawn.EnemyMasses.Runtime.Player
{
    /// <summary>
    /// Top-down camera for Vampire Survivors style gameplay.
    /// Follows the player from above with configurable angle, distance, and smoothing.
    /// </summary>
    public class TopDownCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Target to follow (usually the player). Auto-finds ThirdPersonPlayerController if not set.")]
        [SerializeField] private Transform target;
        
        [Tooltip("Offset from target position")]
        [SerializeField] private Vector3 targetOffset = new Vector3(0, 0, 0);
        
        [Header("Camera Position")]
        [Tooltip("Distance from target")]
        [SerializeField] private float distance = 15f;
        
        [Tooltip("Minimum zoom distance")]
        [SerializeField] private float minDistance = 8f;
        
        [Tooltip("Maximum zoom distance")]
        [SerializeField] private float maxDistance = 25f;
        
        [Tooltip("Camera angle (0 = straight down, 90 = horizontal)")]
        [SerializeField] [Range(0f, 89f)] private float angle = 45f;
        
        [Tooltip("Camera rotation around Y axis")]
        [SerializeField] private float rotation = 0f;
        
        [Header("Following")]
        [Tooltip("How smoothly the camera follows the target")]
        [SerializeField] private float followSmoothTime = 0.15f;
        
        [Tooltip("How smoothly the camera zooms")]
        [SerializeField] private float zoomSmoothTime = 0.2f;
        
        [Tooltip("Dead zone - camera won't move if target is within this radius of screen center")]
        [SerializeField] private float deadZone = 0f;
        
        [Header("Look Ahead")]
        [Tooltip("Camera looks ahead in the direction of movement")]
        [SerializeField] private bool enableLookAhead = false;
        
        [Tooltip("How far ahead to look based on velocity")]
        [SerializeField] private float lookAheadDistance = 3f;
        
        [Tooltip("How smoothly look-ahead adjusts")]
        [SerializeField] private float lookAheadSmoothTime = 0.3f;
        
        [Header("Zoom Input")]
        [Tooltip("Enable scroll wheel zoom")]
        [SerializeField] private bool enableZoom = true;
        
        [Tooltip("Zoom speed")]
        [SerializeField] private float zoomSpeed = 2f;
        
        [Tooltip("Input action for zoom (scroll wheel)")]
        [SerializeField] 
        public InputAction zoomInput = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
        
        [Header("Screen Shake")]
        [Tooltip("Enable screen shake effects")]
        [SerializeField] private bool enableScreenShake = true;
        
        [Tooltip("Screen shake decay speed")]
        [SerializeField]
        #pragma warning disable CS0414 // Exposed for future shake decay customization
        private float shakeDecay = 5f;
        #pragma warning restore CS0414
        
        [Header("Bounds (Optional)")]
        [Tooltip("Constrain camera to bounds")]
        [SerializeField] private bool useBounds = false;
        
        [Tooltip("Minimum world position")]
        [SerializeField] private Vector3 boundsMin = new Vector3(-100, 0, -100);
        
        [Tooltip("Maximum world position")]
        [SerializeField] private Vector3 boundsMax = new Vector3(100, 100, 100);
        
        // Runtime state
        private Vector3 _currentVelocity;
        private Vector3 _lookAheadVelocity;
        private Vector3 _lookAheadOffset;
        private float _currentDistance;
        private float _targetDistance;
        private float _zoomVelocity;
        private Vector3 _lastTargetPosition;
        
        // Screen shake
        private float _shakeIntensity;
        private float _shakeDuration;
        private float _shakeTimeRemaining;
        
        // Cached
        private Camera _camera;
        
        /// <summary>Current camera distance from target</summary>
        public float Distance => _currentDistance;
        
        /// <summary>Target being followed</summary>
        public Transform Target
        {
            get => target;
            set => target = value;
        }
        
        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                _camera = gameObject.AddComponent<Camera>();
            }
            
            _currentDistance = distance;
            _targetDistance = distance;
            
            // Auto-find target
            if (target == null)
            {
                var playerController = FindFirstObjectByType<ThirdPersonPlayerController>();
                if (playerController != null)
                {
                    target = playerController.transform;
                }
            }
            
            if (target != null)
            {
                _lastTargetPosition = target.position;
            }
            
            // Setup zoom input
            if (zoomInput.bindings.Count == 0)
            {
                zoomInput.AddBinding("<Mouse>/scroll/y");
            }
        }
        
        private void OnEnable()
        {
            zoomInput?.Enable();
        }
        
        private void OnDisable()
        {
            zoomInput?.Disable();
        }
        
        private void LateUpdate()
        {
            if (target == null) return;
            
            HandleZoomInput();
            UpdateLookAhead();
            UpdateCameraPosition();
            UpdateScreenShake();
        }
        
        private void HandleZoomInput()
        {
            if (!enableZoom || zoomInput == null) return;
            
            float scrollValue = zoomInput.ReadValue<float>();
            if (Mathf.Abs(scrollValue) > 0.01f)
            {
                // Normalize scroll value (can vary by platform)
                float normalizedScroll = Mathf.Sign(scrollValue);
                _targetDistance -= normalizedScroll * zoomSpeed;
                _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
            }
            
            // Smooth zoom
            _currentDistance = Mathf.SmoothDamp(_currentDistance, _targetDistance, ref _zoomVelocity, zoomSmoothTime);
        }
        
        private void UpdateLookAhead()
        {
            if (!enableLookAhead || target == null) return;
            
            // Calculate velocity
            Vector3 velocity = (target.position - _lastTargetPosition) / Time.deltaTime;
            _lastTargetPosition = target.position;
            
            // Flatten velocity to horizontal plane
            velocity.y = 0;
            
            // Calculate look-ahead target
            Vector3 targetLookAhead = Vector3.zero;
            if (velocity.magnitude > 0.1f)
            {
                targetLookAhead = velocity.normalized * lookAheadDistance;
            }
            
            // Smooth look-ahead
            _lookAheadOffset = Vector3.SmoothDamp(_lookAheadOffset, targetLookAhead, ref _lookAheadVelocity, lookAheadSmoothTime);
        }
        
        private void UpdateCameraPosition()
        {
            // Calculate target position with offset and look-ahead
            Vector3 targetPos = target.position + targetOffset + _lookAheadOffset;
            
            // Check dead zone
            Vector3 currentFocusPoint = GetFocusPoint();
            float distanceToTarget = Vector3.Distance(
                new Vector3(currentFocusPoint.x, 0, currentFocusPoint.z),
                new Vector3(targetPos.x, 0, targetPos.z));
            
            if (distanceToTarget < deadZone)
            {
                targetPos = currentFocusPoint;
            }
            
            // Calculate camera position based on angle and distance
            float angleRad = angle * Mathf.Deg2Rad;
            float rotationRad = rotation * Mathf.Deg2Rad;
            
            // Spherical to cartesian
            float horizontalDist = _currentDistance * Mathf.Cos(angleRad);
            float verticalDist = _currentDistance * Mathf.Sin(angleRad);
            
            Vector3 offset = new Vector3(
                horizontalDist * Mathf.Sin(rotationRad),
                verticalDist,
                horizontalDist * Mathf.Cos(rotationRad)
            );
            
            // For top-down (angle near 90), we want camera above looking down
            // Recalculate for intuitive top-down behavior
            float pitch = (90f - angle) * Mathf.Deg2Rad;
            offset = new Vector3(
                -_currentDistance * Mathf.Sin(pitch) * Mathf.Sin(rotationRad),
                _currentDistance * Mathf.Cos(pitch),
                -_currentDistance * Mathf.Sin(pitch) * Mathf.Cos(rotationRad)
            );
            
            Vector3 desiredPosition = targetPos + offset;
            
            // Apply bounds
            if (useBounds)
            {
                desiredPosition = ClampToBounds(desiredPosition);
            }
            
            // Smooth follow
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _currentVelocity, followSmoothTime);
            
            // Look at target
            transform.LookAt(targetPos);
        }
        
        private Vector3 GetFocusPoint()
        {
            // Get the point the camera is currently looking at
            Ray ray = new Ray(transform.position, transform.forward);
            Plane groundPlane = new Plane(Vector3.up, target.position);
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                return ray.GetPoint(distance);
            }
            
            return target.position;
        }
        
        private Vector3 ClampToBounds(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, boundsMin.x, boundsMax.x),
                Mathf.Clamp(position.y, boundsMin.y, boundsMax.y),
                Mathf.Clamp(position.z, boundsMin.z, boundsMax.z)
            );
        }
        
        private void UpdateScreenShake()
        {
            if (!enableScreenShake || _shakeTimeRemaining <= 0) return;
            
            _shakeTimeRemaining -= Time.deltaTime;
            
            float currentIntensity = _shakeIntensity * (_shakeTimeRemaining / _shakeDuration);
            
            Vector3 shakeOffset = new Vector3(
                Random.Range(-1f, 1f) * currentIntensity,
                Random.Range(-1f, 1f) * currentIntensity,
                0
            );
            
            transform.position += transform.TransformDirection(shakeOffset);
        }
        
        /// <summary>
        /// Trigger screen shake effect
        /// </summary>
        /// <param name="intensity">Shake intensity in units</param>
        /// <param name="duration">Shake duration in seconds</param>
        public void Shake(float intensity, float duration)
        {
            if (!enableScreenShake) return;
            
            _shakeIntensity = intensity;
            _shakeDuration = duration;
            _shakeTimeRemaining = duration;
        }
        
        /// <summary>
        /// Set camera zoom level
        /// </summary>
        /// <param name="newDistance">Target distance</param>
        /// <param name="instant">If true, snaps immediately instead of smoothing</param>
        public void SetZoom(float newDistance, bool instant = false)
        {
            _targetDistance = Mathf.Clamp(newDistance, minDistance, maxDistance);
            
            if (instant)
            {
                _currentDistance = _targetDistance;
            }
        }
        
        /// <summary>
        /// Set camera angle
        /// </summary>
        /// <param name="newAngle">Angle in degrees (0 = top-down, 90 = horizontal)</param>
        public void SetAngle(float newAngle)
        {
            angle = Mathf.Clamp(newAngle, 0f, 89f);
        }
        
        /// <summary>
        /// Set camera rotation around target
        /// </summary>
        /// <param name="newRotation">Rotation in degrees</param>
        public void SetRotation(float newRotation)
        {
            rotation = newRotation;
        }
        
        /// <summary>
        /// Snap camera to target immediately (no smoothing)
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            
            _currentVelocity = Vector3.zero;
            _lookAheadOffset = Vector3.zero;
            _lookAheadVelocity = Vector3.zero;
            
            // Force immediate position update
            float pitch = (90f - angle) * Mathf.Deg2Rad;
            float rotationRad = rotation * Mathf.Deg2Rad;
            
            Vector3 offset = new Vector3(
                -_currentDistance * Mathf.Sin(pitch) * Mathf.Sin(rotationRad),
                _currentDistance * Mathf.Cos(pitch),
                -_currentDistance * Mathf.Sin(pitch) * Mathf.Cos(rotationRad)
            );
            
            transform.position = target.position + targetOffset + offset;
            transform.LookAt(target.position + targetOffset);
        }
        
        /// <summary>
        /// Get the world position under the mouse cursor
        /// </summary>
        public bool GetMouseWorldPosition(out Vector3 worldPosition, float groundHeight = 0f)
        {
            worldPosition = Vector3.zero;
            
            if (_camera == null) return false;
            
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, groundHeight, 0));
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                worldPosition = ray.GetPoint(distance);
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Convert screen position to world position on ground plane
        /// </summary>
        public bool ScreenToWorldPosition(Vector2 screenPos, out Vector3 worldPosition, float groundHeight = 0f)
        {
            worldPosition = Vector3.zero;
            
            if (_camera == null) return false;
            
            Ray ray = _camera.ScreenPointToRay(screenPos);
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, groundHeight, 0));
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                worldPosition = ray.GetPoint(distance);
                return true;
            }
            
            return false;
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (target == null) return;
            
            // Draw line to target
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, target.position + targetOffset);
            
            // Draw target point
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target.position + targetOffset, 0.5f);
            
            // Draw bounds
            if (useBounds)
            {
                Gizmos.color = Color.cyan;
                Vector3 center = (boundsMin + boundsMax) / 2f;
                Vector3 size = boundsMax - boundsMin;
                Gizmos.DrawWireCube(center, size);
            }
            
            // Draw dead zone
            if (deadZone > 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(target.position + targetOffset, deadZone);
            }
        }
#endif
    }
}
