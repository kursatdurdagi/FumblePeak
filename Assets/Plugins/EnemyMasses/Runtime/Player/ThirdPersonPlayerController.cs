using UnityEngine;
using UnityEngine.InputSystem;

namespace Arawn.EnemyMasses.Runtime.Player
{
    /// <summary>
    /// Simple third-person character controller for Vampire Survivors-style gameplay.
    /// Uses the new Input System and controls a character with Speed parameter for locomotion blendtree.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ThirdPersonPlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Movement speed in units per second")]
        [SerializeField] private float moveSpeed = 5f;
        
        [Tooltip("Sprint speed multiplier")]
        [SerializeField] private float sprintMultiplier = 1.5f;
        
        [Tooltip("Rotation smoothing speed")]
        [SerializeField] private float rotationSmoothTime = 0.1f;
        
        [Tooltip("Acceleration for smooth speed changes")]
        [SerializeField] private float acceleration = 10f;
        
        [Header("Input Actions")]
        [Tooltip("Input action for movement (Vector2)")]
        [SerializeField]
        public InputAction moveInput = new InputAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
        
        [Tooltip("Input action for sprint")]
        [SerializeField]
        public InputAction sprintInput = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
        
        [Header("Animation")]
        [Tooltip("Animator component (auto-found if not assigned)")]
        [SerializeField] private Animator animator;
        
        [Tooltip("Animator parameter name for speed")]
        [SerializeField] private string speedParameterName = "Speed";
        
        [Tooltip("Speed value when walking (normalized 0-1)")]
        [SerializeField] private float walkAnimSpeed = 0.5f;
        
        [Tooltip("Speed value when sprinting (normalized 0-1)")]
        [SerializeField] private float sprintAnimSpeed = 1f;
        
        [Header("Camera")]
        [Tooltip("Reference to the main camera (auto-found if not assigned)")]
        [SerializeField] private Transform cameraTransform;
        
        [Tooltip("Use camera-relative movement (WASD moves relative to camera facing)")]
        [SerializeField] private bool cameraRelativeMovement = true;
        
        [Header("Gravity")]
        [Tooltip("Gravity force applied to character")]
        [SerializeField] private float gravity = -9.81f;
        
        [Tooltip("Ground check distance")]
        [SerializeField]
        #pragma warning disable CS0414 // Exposed for future ground check implementation
        private float groundCheckDistance = 0.1f;
        #pragma warning restore CS0414
        
        // Components
        private CharacterController _characterController;
        private int _speedParameterHash;
        
        // State
        private Vector2 _moveInputValue;
        private bool _isSprinting;
        private float _currentSpeed;
        private float _rotationVelocity;
        private Vector3 _velocity;
        private bool _isGrounded;
        
        /// <summary>Current movement speed (read-only)</summary>
        public float CurrentSpeed => _currentSpeed;
        
        /// <summary>Is the player currently moving</summary>
        public bool IsMoving => _moveInputValue.sqrMagnitude > 0.01f;
        
        /// <summary>Is the player sprinting</summary>
        public bool IsSprinting => _isSprinting && IsMoving;
        
        /// <summary>World position of the player</summary>
        public Vector3 Position => transform.position;
        
        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
            
            if (animator != null)
            {
                _speedParameterHash = Animator.StringToHash(speedParameterName);
            }
            
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
            
            // Add default bindings if none exist
            if (moveInput.bindings.Count == 0)
            {
                moveInput.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
                moveInput.AddBinding("<Gamepad>/leftStick");
            }
            
            if (sprintInput.bindings.Count == 0)
            {
                sprintInput.AddBinding("<Keyboard>/leftShift");
                sprintInput.AddBinding("<Gamepad>/leftTrigger");
            }
        }
        
        private void OnEnable()
        {
            moveInput?.Enable();
            sprintInput?.Enable();
        }
        
        private void OnDisable()
        {
            moveInput?.Disable();
            sprintInput?.Disable();
        }
        
        private void Update()
        {
            ReadInput();
            UpdateGroundCheck();
            UpdateMovement();
            UpdateAnimation();
        }
        
        private void ReadInput()
        {
            _moveInputValue = moveInput != null ? moveInput.ReadValue<Vector2>() : Vector2.zero;
            _isSprinting = sprintInput != null && sprintInput.IsPressed();
        }
        
        private void UpdateGroundCheck()
        {
            _isGrounded = _characterController.isGrounded;
            
            if (_isGrounded && _velocity.y < 0)
            {
                _velocity.y = -2f; // Small downward force to keep grounded
            }
        }
        
        private void UpdateMovement()
        {
            // Calculate target speed
            float targetSpeed = 0f;
            
            if (_moveInputValue.sqrMagnitude > 0.01f)
            {
                targetSpeed = _isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
            }
            
            // Smooth speed transition
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * Time.deltaTime);
            
            // Calculate movement direction
            Vector3 moveDirection = Vector3.zero;
            
            if (_moveInputValue.sqrMagnitude > 0.01f)
            {
                // Get input direction
                Vector3 inputDirection = new Vector3(_moveInputValue.x, 0f, _moveInputValue.y).normalized;
                
                if (cameraRelativeMovement && cameraTransform != null)
                {
                    // Transform input to camera space
                    Vector3 cameraForward = cameraTransform.forward;
                    Vector3 cameraRight = cameraTransform.right;
                    
                    // Flatten to horizontal plane
                    cameraForward.y = 0f;
                    cameraRight.y = 0f;
                    cameraForward.Normalize();
                    cameraRight.Normalize();
                    
                    moveDirection = cameraForward * inputDirection.z + cameraRight * inputDirection.x;
                }
                else
                {
                    moveDirection = inputDirection;
                }
                
                // Rotate character to face movement direction
                if (moveDirection.sqrMagnitude > 0.01f)
                {
                    float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
                    float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _rotationVelocity, rotationSmoothTime);
                    transform.rotation = Quaternion.Euler(0f, angle, 0f);
                }
            }
            
            // Apply gravity
            _velocity.y += gravity * Time.deltaTime;
            
            // Move character
            Vector3 motion = moveDirection.normalized * _currentSpeed * Time.deltaTime;
            motion.y = _velocity.y * Time.deltaTime;
            
            _characterController.Move(motion);
        }
        
        private void UpdateAnimation()
        {
            if (animator == null) return;
            
            // Calculate normalized speed for animator (0 = idle, walkAnimSpeed = walk, sprintAnimSpeed = sprint)
            float normalizedSpeed = 0f;
            
            if (_currentSpeed > 0.01f)
            {
                float maxSpeed = moveSpeed * sprintMultiplier;
                float speedRatio = _currentSpeed / maxSpeed;
                
                if (_isSprinting)
                {
                    normalizedSpeed = Mathf.Lerp(walkAnimSpeed, sprintAnimSpeed, speedRatio);
                }
                else
                {
                    normalizedSpeed = speedRatio * walkAnimSpeed;
                }
            }
            
            animator.SetFloat(_speedParameterHash, normalizedSpeed);
        }
        
        /// <summary>
        /// Teleport the player to a position
        /// </summary>
        public void Teleport(Vector3 position)
        {
            _characterController.enabled = false;
            transform.position = position;
            _characterController.enabled = true;
        }
        
        /// <summary>
        /// Apply an external force to the player (knockback, etc.)
        /// </summary>
        public void ApplyForce(Vector3 force)
        {
            _velocity += force;
        }
    }
}
