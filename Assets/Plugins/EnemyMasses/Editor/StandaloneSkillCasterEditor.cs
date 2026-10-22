using UnityEngine;
using UnityEditor;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.StandaloneSkillCaster))]
    public class StandaloneSkillCasterEditor : UnityEditor.Editor
    {
        // ── Portrait / Identity ──
        private SerializedProperty _factionName;
        private SerializedProperty _enemyTypeName;
        private SerializedProperty _portraitMode;
        private SerializedProperty _portrait;
        private SerializedProperty _factionColor;
        private SerializedProperty _showInPortraitBar;

        // ── Fog of War ──
        private SerializedProperty _revealFogOfWar;
        private SerializedProperty _fogRevealRadius;

        // ── Selection / RTS ──
        private SerializedProperty _isPlayerControlled;
        private SerializedProperty _selectionIndicatorOffset;
        private SerializedProperty _selectionIndicatorOverride;
        private SerializedProperty _skipFactionFilter;
        private SerializedProperty _selectionIndicator;
        private SerializedProperty _autoCreateSelectionRing;
        private SerializedProperty _selectionRingRadius;
        private SerializedProperty _selectionRingColor;
        private SerializedProperty _selectionRingYOffset;
        private SerializedProperty _selectionAudioClip;

        // ── Skills ──
        private SerializedProperty _skillSystemConfig;

        // ── Auto-Cast AI ──
        private SerializedProperty _autocastScanRange;
        private SerializedProperty _autocastDecisionInterval;
        private SerializedProperty _autocastTimingVariation;

        // ── Health ──
        private SerializedProperty _health;
        private SerializedProperty _maxHealth;
        private SerializedProperty _underAttackWindow;

        // ── Debug ──
        private SerializedProperty _debugMode;

        private static readonly Color HeaderAccentColor = new Color(0.35f, 0.7f, 0.95f);

        private void OnEnable()
        {
            if (target == null) return;

            _factionName       = serializedObject.FindProperty("factionName");
            _enemyTypeName     = serializedObject.FindProperty("enemyTypeName");
            _portraitMode      = serializedObject.FindProperty("portraitMode");
            _portrait          = serializedObject.FindProperty("portrait");
            _factionColor      = serializedObject.FindProperty("factionColor");
            _showInPortraitBar = serializedObject.FindProperty("showInPortraitBar");

            _revealFogOfWar = serializedObject.FindProperty("revealFogOfWar");
            _fogRevealRadius = serializedObject.FindProperty("fogRevealRadius");

            _isPlayerControlled        = serializedObject.FindProperty("isPlayerControlled");
            _selectionIndicatorOffset  = serializedObject.FindProperty("selectionIndicatorOffset");
            _selectionIndicatorOverride = serializedObject.FindProperty("selectionIndicatorOverride");
            _skipFactionFilter         = serializedObject.FindProperty("skipFactionFilter");
            _selectionIndicator        = serializedObject.FindProperty("selectionIndicator");
            _autoCreateSelectionRing   = serializedObject.FindProperty("autoCreateSelectionRing");
            _selectionRingRadius       = serializedObject.FindProperty("selectionRingRadius");
            _selectionRingColor        = serializedObject.FindProperty("selectionRingColor");
            _selectionRingYOffset      = serializedObject.FindProperty("selectionRingYOffset");
            _selectionAudioClip        = serializedObject.FindProperty("selectionAudioClip");

            _skillSystemConfig = serializedObject.FindProperty("skillSystemConfig");

            _autocastScanRange        = serializedObject.FindProperty("autocastScanRange");
            _autocastDecisionInterval = serializedObject.FindProperty("autocastDecisionInterval");
            _autocastTimingVariation  = serializedObject.FindProperty("autocastTimingVariation");

            _health            = serializedObject.FindProperty("health");
            _maxHealth         = serializedObject.FindProperty("maxHealth");
            _underAttackWindow = serializedObject.FindProperty("underAttackWindow");

            _debugMode = serializedObject.FindProperty("debugMode");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawPortraitIdentity();
            EditorGUILayout.Space(5);

            DrawFogOfWar();
            EditorGUILayout.Space(5);

            DrawSelectionRTS();
            EditorGUILayout.Space(5);

            DrawSkills();
            EditorGUILayout.Space(5);

            DrawAutoCastAI();
            EditorGUILayout.Space(5);

            DrawHealth();
            EditorGUILayout.Space(5);

            DrawDebug();
            EditorGUILayout.Space(5);

            serializedObject.ApplyModifiedProperties();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Header
        // ═══════════════════════════════════════════════════════════════

        private new void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            Color prevColor = GUI.color;
            GUI.color = _factionColor != null
                ? _factionColor.colorValue
                : HeaderAccentColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = prevColor;

            EditorGUILayout.LabelField("Standalone Skill Caster", titleStyle);

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Portrait / Identity
        // ═══════════════════════════════════════════════════════════════

        private void DrawPortraitIdentity()
        {
            EditorGUILayout.LabelField("Portrait / Identity", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_factionName);
            EditorGUILayout.PropertyField(_enemyTypeName);
            EditorGUILayout.PropertyField(_factionColor);
            EditorGUILayout.PropertyField(_showInPortraitBar);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_portraitMode);

            bool isAutoMode = _portraitMode.enumValueIndex == (int)Runtime.PortraitMode.AutoGenerate;

            if (!isAutoMode)
            {
                // Manual mode — just show the portrait field
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_portrait);
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_portrait);
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(4);

                // Portrait preview
                var currentPortrait = _portrait.objectReferenceValue as Texture2D;
                if (currentPortrait != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    var previewRect = GUILayoutUtility.GetRect(128, 128, GUILayout.Width(128));
                    EditorGUI.DrawPreviewTexture(previewRect, currentPortrait);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(4);
                }

                if (GUILayout.Button("Generate Portrait from GameObject", GUILayout.Height(28)))
                {
                    GeneratePortraitForCaster();
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Fog of War
        // ═══════════════════════════════════════════════════════════════

        private void DrawFogOfWar()
        {
            EditorGUILayout.LabelField("Fog of War", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_revealFogOfWar);
            if (_revealFogOfWar.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_fogRevealRadius);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Selection / RTS
        // ═══════════════════════════════════════════════════════════════

        private void DrawSelectionRTS()
        {
            EditorGUILayout.LabelField("Selection / RTS", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_isPlayerControlled);
            EditorGUILayout.PropertyField(_skipFactionFilter);

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(_selectionIndicator);
            EditorGUILayout.PropertyField(_selectionIndicatorOverride);
            EditorGUILayout.PropertyField(_selectionIndicatorOffset);

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(_autoCreateSelectionRing);
            if (_autoCreateSelectionRing.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_selectionRingRadius);
                EditorGUILayout.PropertyField(_selectionRingColor);
                EditorGUILayout.PropertyField(_selectionRingYOffset);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(_selectionAudioClip);

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Skills
        // ═══════════════════════════════════════════════════════════════

        private void DrawSkills()
        {
            EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_skillSystemConfig, true);

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Auto-Cast AI
        // ═══════════════════════════════════════════════════════════════

        private void DrawAutoCastAI()
        {
            EditorGUILayout.LabelField("Auto-Cast AI", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_autocastScanRange,        new GUIContent("Scan Range"));
            EditorGUILayout.PropertyField(_autocastDecisionInterval, new GUIContent("Decision Interval"));
            EditorGUILayout.PropertyField(_autocastTimingVariation,  new GUIContent("Timing Variation"));

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Health
        // ═══════════════════════════════════════════════════════════════

        private void DrawHealth()
        {
            EditorGUILayout.LabelField("Health", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_health);
            EditorGUILayout.PropertyField(_maxHealth);
            EditorGUILayout.PropertyField(_underAttackWindow);

            // Live health bar preview (play mode)
            if (Application.isPlaying)
            {
                var caster = (Runtime.StandaloneSkillCaster)target;
                float pct = caster.HealthPercent;
                EditorGUILayout.Space(2);
                Rect barRect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(barRect, pct, $"HP  {pct * 100f:F0}%");
            }

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Debug
        // ═══════════════════════════════════════════════════════════════

        private void DrawDebug()
        {
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_debugMode);

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Portrait Generation Helpers
        // ═══════════════════════════════════════════════════════════════

        private void GeneratePortraitForCaster()
        {
            var caster = (Runtime.StandaloneSkillCaster)target;

            // Determine the prefab source to render
            GameObject prefabToRender = ResolvePrefab(caster);
            if (prefabToRender == null)
            {
                EditorUtility.DisplayDialog("Portrait Generator",
                    "Cannot generate a portrait:\n\n" +
                    "The StandaloneSkillCaster must be on a prefab (or a prefab instance in the scene) " +
                    "so the generator knows which model to photograph.",
                    "OK");
                return;
            }

            // Determine save directory and name
            if (!ResolveSavePath(caster, out string directory, out string baseName))
            {
                EditorUtility.DisplayDialog("Portrait Generator",
                    "Cannot determine where to save the portrait.\n\n" +
                    "Make sure the StandaloneSkillCaster is on a prefab asset or a prefab instance in a saved scene.",
                    "OK");
                return;
            }

            Texture2D generated = EnemyCrowdPortraitGenerator.GeneratePortrait(
                prefabToRender, directory, baseName);

            if (generated != null)
            {
                serializedObject.Update();
                _portrait.objectReferenceValue = generated;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);

                EditorUtility.DisplayDialog("Portrait Generator",
                    "Portrait generated successfully!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Portrait Generator",
                    "Failed to generate portrait. Check the console for details.", "OK");
            }
        }

        /// <summary>
        /// Finds the prefab asset to instantiate and render.
        /// Works whether the user selects a prefab in the Project window or a prefab instance in the scene.
        /// </summary>
        private static GameObject ResolvePrefab(Component component)
        {
            // If editing a prefab asset directly
            string assetPath = AssetDatabase.GetAssetPath(component.gameObject);
            if (!string.IsNullOrEmpty(assetPath))
                return component.gameObject;

            // If editing a scene instance of a prefab
            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(component.gameObject);
            if (prefabSource != null)
                return prefabSource;

            // Scene-only object with no prefab — use the game object itself
            // (the generator will instantiate a copy)
            return component.gameObject;
        }

        /// <summary>
        /// Determines the asset directory and base name for saving the portrait PNG.
        /// Prefers the prefab's location; falls back to the scene file's location.
        /// </summary>
        private static bool ResolveSavePath(Component component, out string directory, out string baseName)
        {
            directory = null;
            baseName = null;

            // 1. Try prefab asset path
            string assetPath = AssetDatabase.GetAssetPath(component.gameObject);
            if (!string.IsNullOrEmpty(assetPath))
            {
                directory = System.IO.Path.GetDirectoryName(assetPath);
                baseName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                return true;
            }

            // 2. Try the source prefab of a scene instance
            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(component.gameObject);
            if (prefabSource != null)
            {
                string prefabPath = AssetDatabase.GetAssetPath(prefabSource);
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    directory = System.IO.Path.GetDirectoryName(prefabPath);
                    baseName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                    return true;
                }
            }

            // 3. Fallback: save next to the scene file, using the GameObject name
            string scenePath = component.gameObject.scene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                directory = System.IO.Path.GetDirectoryName(scenePath);
                baseName = component.gameObject.name;
                return true;
            }

            return false;
        }
    }
}
