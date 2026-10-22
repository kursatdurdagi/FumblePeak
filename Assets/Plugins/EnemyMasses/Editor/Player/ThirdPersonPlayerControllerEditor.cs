using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.Player;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for ThirdPersonPlayerController with Input Action visualization.
    /// </summary>
    [CustomEditor(typeof(ThirdPersonPlayerController))]
    public class ThirdPersonPlayerControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty _moveSpeed;
        private SerializedProperty _sprintMultiplier;
        private SerializedProperty _rotationSmoothTime;
        private SerializedProperty _acceleration;
        
        private SerializedProperty _moveInput;
        private SerializedProperty _sprintInput;
        
        private SerializedProperty _animator;
        private SerializedProperty _speedParameterName;
        private SerializedProperty _walkAnimSpeed;
        private SerializedProperty _sprintAnimSpeed;
        
        private SerializedProperty _cameraTransform;
        private SerializedProperty _cameraRelativeMovement;
        
        private SerializedProperty _gravity;
        private SerializedProperty _groundCheckDistance;
        
        private bool _showMovement = true;
        private bool _showInput = true;
        private bool _showAnimation = true;
        private bool _showCamera = true;
        private bool _showGravity = true;
        
        private void OnEnable()
        {
            _moveSpeed = serializedObject.FindProperty("moveSpeed");
            _sprintMultiplier = serializedObject.FindProperty("sprintMultiplier");
            _rotationSmoothTime = serializedObject.FindProperty("rotationSmoothTime");
            _acceleration = serializedObject.FindProperty("acceleration");
            
            _moveInput = serializedObject.FindProperty("moveInput");
            _sprintInput = serializedObject.FindProperty("sprintInput");
            
            _animator = serializedObject.FindProperty("animator");
            _speedParameterName = serializedObject.FindProperty("speedParameterName");
            _walkAnimSpeed = serializedObject.FindProperty("walkAnimSpeed");
            _sprintAnimSpeed = serializedObject.FindProperty("sprintAnimSpeed");
            
            _cameraTransform = serializedObject.FindProperty("cameraTransform");
            _cameraRelativeMovement = serializedObject.FindProperty("cameraRelativeMovement");
            
            _gravity = serializedObject.FindProperty("gravity");
            _groundCheckDistance = serializedObject.FindProperty("groundCheckDistance");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var controller = (ThirdPersonPlayerController)target;
            
            // Header
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Third Person Player Controller", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Vampire Survivors Style Movement", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            // Movement Settings
            _showMovement = EditorGUILayout.BeginFoldoutHeaderGroup(_showMovement, "Movement Settings");
            if (_showMovement)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_moveSpeed);
                EditorGUILayout.PropertyField(_sprintMultiplier);
                EditorGUILayout.PropertyField(_rotationSmoothTime);
                EditorGUILayout.PropertyField(_acceleration);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Input Actions
            _showInput = EditorGUILayout.BeginFoldoutHeaderGroup(_showInput, "Input Actions");
            if (_showInput)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_moveInput);
                EditorGUILayout.PropertyField(_sprintInput);
                
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Configure input bindings in the Input Action properties above.\n" +
                    "Default: WASD/Left Stick for movement, Left Shift/Left Trigger for sprint.",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Animation Settings
            _showAnimation = EditorGUILayout.BeginFoldoutHeaderGroup(_showAnimation, "Animation Settings");
            if (_showAnimation)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_animator);
                EditorGUILayout.PropertyField(_speedParameterName);
                EditorGUILayout.PropertyField(_walkAnimSpeed);
                EditorGUILayout.PropertyField(_sprintAnimSpeed);
                
                if (string.IsNullOrEmpty(_speedParameterName.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        "Set the Speed parameter name to match your Animator's float parameter.",
                        MessageType.Warning);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Camera Settings
            _showCamera = EditorGUILayout.BeginFoldoutHeaderGroup(_showCamera, "Camera Settings");
            if (_showCamera)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_cameraTransform);
                EditorGUILayout.PropertyField(_cameraRelativeMovement);
                
                if (_cameraRelativeMovement.boolValue && _cameraTransform.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Camera will be auto-detected from Camera.main at runtime.",
                        MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Gravity Settings
            _showGravity = EditorGUILayout.BeginFoldoutHeaderGroup(_showGravity, "Physics");
            if (_showGravity)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_gravity);
                EditorGUILayout.PropertyField(_groundCheckDistance);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Runtime Info
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);
                
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Toggle("Is Moving", controller.IsMoving);
                EditorGUILayout.Toggle("Is Sprinting", controller.IsSprinting);
                EditorGUILayout.FloatField("Current Speed", controller.CurrentSpeed);
                EditorGUI.EndDisabledGroup();
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}
