using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.Player;

namespace Arawn.EnemyMasses.Editor.Player
{
    /// <summary>
    /// Custom editor for TopDownCamera with preview and quick setup options.
    /// </summary>
    [CustomEditor(typeof(TopDownCamera))]
    public class TopDownCameraEditor : UnityEditor.Editor
    {
        private SerializedProperty _target;
        private SerializedProperty _targetOffset;
        private SerializedProperty _distance;
        private SerializedProperty _minDistance;
        private SerializedProperty _maxDistance;
        private SerializedProperty _angle;
        private SerializedProperty _rotation;
        private SerializedProperty _followSmoothTime;
        private SerializedProperty _zoomSmoothTime;
        private SerializedProperty _deadZone;
        private SerializedProperty _enableLookAhead;
        private SerializedProperty _lookAheadDistance;
        private SerializedProperty _lookAheadSmoothTime;
        private SerializedProperty _enableZoom;
        private SerializedProperty _zoomSpeed;
        private SerializedProperty _zoomInput;
        private SerializedProperty _enableScreenShake;
        private SerializedProperty _shakeDecay;
        private SerializedProperty _useBounds;
        private SerializedProperty _boundsMin;
        private SerializedProperty _boundsMax;
        
        private bool _showTarget = true;
        private bool _showPosition = true;
        private bool _showFollowing = true;
        private bool _showLookAhead = false;
        private bool _showZoom = true;
        private bool _showShake = false;
        private bool _showBounds = false;
        
        private void OnEnable()
        {
            _target = serializedObject.FindProperty("target");
            _targetOffset = serializedObject.FindProperty("targetOffset");
            _distance = serializedObject.FindProperty("distance");
            _minDistance = serializedObject.FindProperty("minDistance");
            _maxDistance = serializedObject.FindProperty("maxDistance");
            _angle = serializedObject.FindProperty("angle");
            _rotation = serializedObject.FindProperty("rotation");
            _followSmoothTime = serializedObject.FindProperty("followSmoothTime");
            _zoomSmoothTime = serializedObject.FindProperty("zoomSmoothTime");
            _deadZone = serializedObject.FindProperty("deadZone");
            _enableLookAhead = serializedObject.FindProperty("enableLookAhead");
            _lookAheadDistance = serializedObject.FindProperty("lookAheadDistance");
            _lookAheadSmoothTime = serializedObject.FindProperty("lookAheadSmoothTime");
            _enableZoom = serializedObject.FindProperty("enableZoom");
            _zoomSpeed = serializedObject.FindProperty("zoomSpeed");
            _zoomInput = serializedObject.FindProperty("zoomInput");
            _enableScreenShake = serializedObject.FindProperty("enableScreenShake");
            _shakeDecay = serializedObject.FindProperty("shakeDecay");
            _useBounds = serializedObject.FindProperty("useBounds");
            _boundsMin = serializedObject.FindProperty("boundsMin");
            _boundsMax = serializedObject.FindProperty("boundsMax");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var camera = (TopDownCamera)target;
            
            // Header
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Top-Down Camera", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Vampire Survivors Style Camera", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            // Quick Presets
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Presets:", GUILayout.Width(50));
            
            if (GUILayout.Button("Top-Down", GUILayout.Width(70)))
            {
                _angle.floatValue = 80f;
                _distance.floatValue = 20f;
            }
            if (GUILayout.Button("Isometric", GUILayout.Width(70)))
            {
                _angle.floatValue = 45f;
                _distance.floatValue = 15f;
                _rotation.floatValue = 45f;
            }
            if (GUILayout.Button("3/4 View", GUILayout.Width(70)))
            {
                _angle.floatValue = 55f;
                _distance.floatValue = 12f;
                _rotation.floatValue = 0f;
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Target
            _showTarget = EditorGUILayout.BeginFoldoutHeaderGroup(_showTarget, "Target");
            if (_showTarget)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_target);
                EditorGUILayout.PropertyField(_targetOffset);
                
                if (_target.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No target set. Will auto-find ThirdPersonPlayerController at runtime.",
                        MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Camera Position
            _showPosition = EditorGUILayout.BeginFoldoutHeaderGroup(_showPosition, "Camera Position");
            if (_showPosition)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_distance);
                EditorGUILayout.PropertyField(_minDistance);
                EditorGUILayout.PropertyField(_maxDistance);
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(_angle);
                EditorGUILayout.PropertyField(_rotation);
                
                // Visual angle helper
                string angleDesc = _angle.floatValue switch
                {
                    >= 80 => "(Nearly top-down)",
                    >= 60 => "(High angle)",
                    >= 40 => "(Isometric)",
                    >= 20 => "(Low angle)",
                    _ => "(Near horizontal)"
                };
                EditorGUILayout.LabelField($"  {angleDesc}", EditorStyles.miniLabel);
                
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Following
            _showFollowing = EditorGUILayout.BeginFoldoutHeaderGroup(_showFollowing, "Following");
            if (_showFollowing)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_followSmoothTime);
                EditorGUILayout.PropertyField(_deadZone);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Look Ahead
            _showLookAhead = EditorGUILayout.BeginFoldoutHeaderGroup(_showLookAhead, "Look Ahead");
            if (_showLookAhead)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_enableLookAhead);
                
                EditorGUI.BeginDisabledGroup(!_enableLookAhead.boolValue);
                EditorGUILayout.PropertyField(_lookAheadDistance);
                EditorGUILayout.PropertyField(_lookAheadSmoothTime);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Zoom
            _showZoom = EditorGUILayout.BeginFoldoutHeaderGroup(_showZoom, "Zoom");
            if (_showZoom)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_enableZoom);
                
                EditorGUI.BeginDisabledGroup(!_enableZoom.boolValue);
                EditorGUILayout.PropertyField(_zoomSpeed);
                EditorGUILayout.PropertyField(_zoomSmoothTime);
                EditorGUILayout.PropertyField(_zoomInput);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Screen Shake
            _showShake = EditorGUILayout.BeginFoldoutHeaderGroup(_showShake, "Screen Shake");
            if (_showShake)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_enableScreenShake);
                
                EditorGUI.BeginDisabledGroup(!_enableScreenShake.boolValue);
                EditorGUILayout.PropertyField(_shakeDecay);
                EditorGUI.EndDisabledGroup();
                
                if (Application.isPlaying && _enableScreenShake.boolValue)
                {
                    EditorGUILayout.Space(3);
                    if (GUILayout.Button("Test Shake"))
                    {
                        camera.Shake(0.3f, 0.3f);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Bounds
            _showBounds = EditorGUILayout.BeginFoldoutHeaderGroup(_showBounds, "Bounds");
            if (_showBounds)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_useBounds);
                
                EditorGUI.BeginDisabledGroup(!_useBounds.boolValue);
                EditorGUILayout.PropertyField(_boundsMin);
                EditorGUILayout.PropertyField(_boundsMax);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Runtime info
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
                
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField("Current Distance", camera.Distance);
                EditorGUI.EndDisabledGroup();
                
                if (GUILayout.Button("Snap to Target"))
                {
                    camera.SnapToTarget();
                }
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}
