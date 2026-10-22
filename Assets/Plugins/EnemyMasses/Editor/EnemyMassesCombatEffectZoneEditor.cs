using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesCombatEffectZone))]
    public class EnemyMassesCombatEffectZoneEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesCombatEffectZone _zone;

        private SerializedProperty _crowdController;
        private SerializedProperty _radius;
        private SerializedProperty _startOnStart;
        private SerializedProperty _startDelay;
        private SerializedProperty _activeDuration;
        private SerializedProperty _inactiveDuration;
        private SerializedProperty _applyInterval;
        private SerializedProperty _effectMode;
        private SerializedProperty _effectType;
        private SerializedProperty _effectDuration;
        private SerializedProperty _effectValue;
        private SerializedProperty _effectTickInterval;
        private SerializedProperty _instantHealAmount;
        private SerializedProperty _factionFilterMode;
        private SerializedProperty _factionFilter;
        private SerializedProperty _startVFX;
        private SerializedProperty _endVFX;
        private SerializedProperty _activeVFX;
        private SerializedProperty _activeVfxInterval;
        private SerializedProperty _presenceVFX;
        private SerializedProperty _presenceVfxInterval;
        private SerializedProperty _vfxScale;
        private SerializedProperty _vfxOffset;
        private SerializedProperty _startSound;
        private SerializedProperty _endSound;
        private SerializedProperty _activeLoopSound;
        private SerializedProperty _presenceLoopSound;
        private SerializedProperty _showGizmos;
        private SerializedProperty _gizmoColor;
        private SerializedProperty _radiusLineRenderer;
        private SerializedProperty _radiusLineSegments;
        private SerializedProperty _radiusLineHeight;
        private SerializedProperty _updateLineRendererInEditor;
        private SerializedProperty _updateLineRendererEveryFrame;
        private SerializedProperty _animateRadiusLine;
        private SerializedProperty _animateRadiusLineInEditor;
        private SerializedProperty _radiusLinePulseSpeed;
        private SerializedProperty _radiusLineAlphaPulse;
        private SerializedProperty _radiusLineWidthPulse;
        private SerializedProperty _radiusLineFadeDuration;

        private void OnEnable()
        {
            _zone = (Runtime.EnemyMassesCombatEffectZone)target;

            _crowdController = serializedObject.FindProperty("crowdController");
            _radius = serializedObject.FindProperty("radius");
            _startOnStart = serializedObject.FindProperty("startOnStart");
            _startDelay = serializedObject.FindProperty("startDelay");
            _activeDuration = serializedObject.FindProperty("activeDuration");
            _inactiveDuration = serializedObject.FindProperty("inactiveDuration");
            _applyInterval = serializedObject.FindProperty("applyInterval");
            _effectMode = serializedObject.FindProperty("effectMode");
            _effectType = serializedObject.FindProperty("effectType");
            _effectDuration = serializedObject.FindProperty("effectDuration");
            _effectValue = serializedObject.FindProperty("effectValue");
            _effectTickInterval = serializedObject.FindProperty("effectTickInterval");
            _instantHealAmount = serializedObject.FindProperty("instantHealAmount");
            _factionFilterMode = serializedObject.FindProperty("factionFilterMode");
            _factionFilter = serializedObject.FindProperty("factionFilter");
            _startVFX = serializedObject.FindProperty("startVFX");
            _endVFX = serializedObject.FindProperty("endVFX");
            _activeVFX = serializedObject.FindProperty("activeVFX");
            _activeVfxInterval = serializedObject.FindProperty("activeVFXInterval");
            _presenceVFX = serializedObject.FindProperty("presenceVFX");
            _presenceVfxInterval = serializedObject.FindProperty("presenceVFXInterval");
            _vfxScale = serializedObject.FindProperty("vfxScale");
            _vfxOffset = serializedObject.FindProperty("vfxOffset");
            _startSound = serializedObject.FindProperty("startSound");
            _endSound = serializedObject.FindProperty("endSound");
            _activeLoopSound = serializedObject.FindProperty("activeLoopSound");
            _presenceLoopSound = serializedObject.FindProperty("presenceLoopSound");
            _showGizmos = serializedObject.FindProperty("showGizmos");
            _gizmoColor = serializedObject.FindProperty("gizmoColor");
            _radiusLineRenderer = serializedObject.FindProperty("radiusLineRenderer");
            _radiusLineSegments = serializedObject.FindProperty("radiusLineSegments");
            _radiusLineHeight = serializedObject.FindProperty("radiusLineHeight");
            _updateLineRendererInEditor = serializedObject.FindProperty("updateLineRendererInEditor");
            _updateLineRendererEveryFrame = serializedObject.FindProperty("updateLineRendererEveryFrame");
            _animateRadiusLine = serializedObject.FindProperty("animateRadiusLine");
            _animateRadiusLineInEditor = serializedObject.FindProperty("animateRadiusLineInEditor");
            _radiusLinePulseSpeed = serializedObject.FindProperty("radiusLinePulseSpeed");
            _radiusLineAlphaPulse = serializedObject.FindProperty("radiusLineAlphaPulse");
            _radiusLineWidthPulse = serializedObject.FindProperty("radiusLineWidthPulse");
            _radiusLineFadeDuration = serializedObject.FindProperty("radiusLineFadeDuration");
        }

        public override void OnInspectorGUI()
        {
            if (_zone == null)
            {
                EditorGUILayout.HelpBox("Target object is missing or destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeaderSection();
            EditorGUILayout.Space(8);

            DrawControllerSection();
            EditorGUILayout.Space(6);

            DrawZoneSection();
            EditorGUILayout.Space(6);

            DrawTimingSection();
            EditorGUILayout.Space(6);

            DrawEffectSection();
            EditorGUILayout.Space(6);

            DrawFilteringSection();
            EditorGUILayout.Space(6);

            DrawVfxSection();
            EditorGUILayout.Space(6);

            DrawAudioSection();
            EditorGUILayout.Space(6);

            DrawVisualsSection();
            EditorGUILayout.Space(8);

            DrawRadiusLineSection();
            EditorGUILayout.Space(8);

            DrawRuntimeControls();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeaderSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            Color gizmoColor = _zone.GetGizmoColor();
            GUI.color = gizmoColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Enemy Masses Combat Effect Zone", titleStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawControllerSection()
        {
            EditorGUILayout.LabelField("Controller", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_crowdController);
            if (_crowdController.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Uses EnemyMassesCrowdController.Instance at runtime.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawZoneSection()
        {
            EditorGUILayout.LabelField("Zone", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_radius);

            EditorGUILayout.EndVertical();
        }

        private void DrawTimingSection()
        {
            EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_startOnStart, new GUIContent("Auto Start"));
            EditorGUILayout.PropertyField(_startDelay);
            EditorGUILayout.PropertyField(_activeDuration);
            EditorGUILayout.PropertyField(_inactiveDuration);
            EditorGUILayout.PropertyField(_applyInterval);

            bool usesCycle = _activeDuration.floatValue > 0f && _inactiveDuration.floatValue > 0f;
            if (!usesCycle)
            {
                EditorGUILayout.HelpBox("Active Duration or Inactive Duration is 0, so the zone stays active continuously.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"Zone cycles: Active {_activeDuration.floatValue:0.##}s, Inactive {_inactiveDuration.floatValue:0.##}s.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEffectSection()
        {
            EditorGUILayout.LabelField("Effect", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_effectMode);

            var mode = (Runtime.EnemyMassesCombatEffectZone.CombatZoneEffectMode)_effectMode.enumValueIndex;
            if (mode == Runtime.EnemyMassesCombatEffectZone.CombatZoneEffectMode.InstantHeal)
            {
                EditorGUILayout.PropertyField(_instantHealAmount, new GUIContent("Heal Amount"));
                EditorGUILayout.HelpBox("Instant Heal applies an immediate heal. For heal-over-time use Regeneration in Status Effect mode.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(_effectType);
                EditorGUILayout.PropertyField(_effectDuration);
                EditorGUILayout.PropertyField(_effectValue);
                EditorGUILayout.PropertyField(_effectTickInterval);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFilteringSection()
        {
            EditorGUILayout.LabelField("Filtering", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_factionFilterMode);
            EditorGUILayout.PropertyField(_factionFilter, true);

            var mode = (Runtime.EnemyMassesCombatEffectZone.CombatZoneFilterMode)_factionFilterMode.enumValueIndex;
            if (mode == Runtime.EnemyMassesCombatEffectZone.CombatZoneFilterMode.OnlyListedFactions &&
                _factionFilter.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Only Listed Factions is selected but the list is empty. No agents will be affected.", MessageType.Warning);
            }
            else if (mode == Runtime.EnemyMassesCombatEffectZone.CombatZoneFilterMode.ExcludeListedFactions &&
                     _factionFilter.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Exclude Listed Factions is selected but the list is empty. All factions will be affected.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVfxSection()
        {
            EditorGUILayout.LabelField("VFX", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_startVFX, true);
            EditorGUILayout.PropertyField(_endVFX, true);
            EditorGUILayout.PropertyField(_activeVFX, true);
            EditorGUILayout.PropertyField(_activeVfxInterval);
            EditorGUILayout.PropertyField(_presenceVFX, true);
            EditorGUILayout.PropertyField(_presenceVfxInterval);
            EditorGUILayout.PropertyField(_vfxScale);
            EditorGUILayout.PropertyField(_vfxOffset);

            EditorGUILayout.EndVertical();
        }

        private void DrawAudioSection()
        {
            EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_startSound, true);
            EditorGUILayout.PropertyField(_endSound, true);
            EditorGUILayout.PropertyField(_activeLoopSound, true);
            EditorGUILayout.PropertyField(_presenceLoopSound, true);

            EditorGUILayout.EndVertical();
        }

        private void DrawVisualsSection()
        {
            EditorGUILayout.LabelField("Visual Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showGizmos);
            EditorGUILayout.PropertyField(_gizmoColor, new GUIContent("Custom Gizmo Color"));
            if (_gizmoColor.colorValue.a <= 0f)
            {
                EditorGUILayout.HelpBox("Gizmo color is auto-derived from the effect type.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRadiusLineSection()
        {
            EditorGUILayout.LabelField("Radius Line", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_radiusLineRenderer, new GUIContent("Line Renderer"));
            if (_radiusLineRenderer.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(_radiusLineSegments);
                EditorGUILayout.PropertyField(_radiusLineHeight);
                EditorGUILayout.PropertyField(_updateLineRendererInEditor);
                EditorGUILayout.PropertyField(_updateLineRendererEveryFrame);
                EditorGUILayout.PropertyField(_animateRadiusLine);
                EditorGUILayout.PropertyField(_animateRadiusLineInEditor);
                EditorGUILayout.PropertyField(_radiusLinePulseSpeed);
                EditorGUILayout.PropertyField(_radiusLineAlphaPulse);
                EditorGUILayout.PropertyField(_radiusLineWidthPulse);
                EditorGUILayout.PropertyField(_radiusLineFadeDuration);
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a LineRenderer to draw the radius circle.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeControls()
        {
            if (!Application.isPlaying)
                return;

            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"Running: {_zone.IsRunning}  |  Active: {_zone.IsActive}");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Zone", GUILayout.Height(22)))
            {
                _zone.StartZone();
            }
            if (GUILayout.Button("Stop Zone", GUILayout.Height(22)))
            {
                _zone.StopZone();
            }
            if (GUILayout.Button("Restart", GUILayout.Height(22)))
            {
                _zone.RestartZone();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
    }
}
