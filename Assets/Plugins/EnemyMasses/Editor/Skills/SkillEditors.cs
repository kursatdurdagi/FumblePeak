using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Editor.Skills
{
    /// <summary>
    /// Custom editor for BaseSkill and derived classes with preview and level testing.
    /// </summary>
    [CustomEditor(typeof(BaseSkill), true)]
    public class BaseSkillEditor : UnityEditor.Editor
    {
        private int _previewLevel = 1;
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var skill = (BaseSkill)target;
            
            // Header with skill info
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(skill.SkillName, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(skill.Description))
            {
                EditorGUILayout.HelpBox(skill.Description, MessageType.None);
            }
            EditorGUILayout.Space(5);
            
            // Draw default inspector
            DrawDefaultInspector();
            
            EditorGUILayout.Space(10);
            
            // Stats preview section
            EditorGUILayout.LabelField("Stats Preview", EditorStyles.boldLabel);
            
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(skill.GetStatsDescription(), GUILayout.Height(60));
            EditorGUI.EndDisabledGroup();
            
            // Level preview (runtime only)
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Level: {skill.CurrentLevel} / {skill.MaxLevel}");
                
                EditorGUI.BeginDisabledGroup(skill.IsMaxLevel);
                if (GUILayout.Button("Level Up", GUILayout.Width(80)))
                {
                    skill.LevelUp();
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.LabelField("Cooldown Progress", EditorStyles.miniLabel);
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(18)),
                    skill.CooldownProgress,
                    $"{skill.CooldownRemaining:F1}s remaining");
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Next Level Preview:", EditorStyles.miniLabel);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextArea(skill.GetNextLevelDescription(), GUILayout.Height(50));
                EditorGUI.EndDisabledGroup();
                
                // Force repaint for real-time updates
                Repaint();
            }
            else
            {
                // Editor mode level preview
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Level Preview (Editor)", EditorStyles.miniLabel);
                
                int maxLevel = serializedObject.FindProperty("maxLevel").intValue;
                _previewLevel = EditorGUILayout.IntSlider("Preview Level", _previewLevel, 1, maxLevel);
                
                if (GUILayout.Button("Apply Preview Level"))
                {
                    serializedObject.FindProperty("currentLevel").intValue = _previewLevel;
                    serializedObject.ApplyModifiedProperties();
                    skill.RecalculateStats();
                }
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
    
    /// <summary>
    /// Custom editor for PlayerSkillManager with skill overview.
    /// </summary>
    [CustomEditor(typeof(PlayerSkillManager))]
    public class PlayerSkillManagerEditor : UnityEditor.Editor
    {
        private bool _showSkills = true;
        private bool _showSettings = true;
        private bool _showEvents = false;
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var manager = (PlayerSkillManager)target;
            
            // Header
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Player Skill Manager", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Vampire Survivors Style Combat", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            // References
            EditorGUILayout.PropertyField(serializedObject.FindProperty("crowdControllers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skillOrigin"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoFindNewControllers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controllerScanInterval"));
            
            EditorGUILayout.Space(5);
            
            // Settings
            _showSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showSettings, "Settings");
            if (_showSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enemyCacheUpdateRate"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxTrackedAgents"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onlyCacheAliveAgents"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("damageMultiplier"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("invincibilityDuration"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Effect Limits", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("globalMaxVFXPerActivation"), 
                    new GUIContent("Max VFX Per Activation", "Maximum VFX spawned per skill activation (0 = unlimited)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("globalMaxHitSoundsPerActivation"),
                    new GUIContent("Max Hit Sounds Per Activation", "Maximum hit sounds per skill activation (0 = unlimited)"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Debug Settings
            EditorGUILayout.Space(5);
            var showDebug = EditorGUILayout.BeginFoldoutHeaderGroup(false, "Debug Settings");
            if (showDebug)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableDebugLogs"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("logDamageEvents"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("logCacheUpdates"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("periodicStatusInterval"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Active Skills (Runtime)
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                _showSkills = EditorGUILayout.BeginFoldoutHeaderGroup(_showSkills, $"Active Skills ({manager.ActiveSkills.Count})");
                if (_showSkills)
                {
                    EditorGUI.indentLevel++;
                    
                    if (manager.ActiveSkills.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No active skills. Add skill components to this GameObject or children.", MessageType.Info);
                    }
                    else
                    {
                        foreach (var skill in manager.ActiveSkills)
                        {
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(skill.SkillName, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField($"Lv.{skill.CurrentLevel}", GUILayout.Width(40));
                            EditorGUILayout.EndHorizontal();
                            
                            EditorGUI.ProgressBar(
                                EditorGUILayout.GetControlRect(GUILayout.Height(12)),
                                skill.CooldownProgress,
                                "");
                            EditorGUILayout.EndVertical();
                        }
                    }
                    
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
                
                // Force repaint for real-time updates
                Repaint();
            }
            
            // Events
            _showEvents = EditorGUILayout.BeginFoldoutHeaderGroup(_showEvents, "Events (Code Only)");
            if (_showEvents)
            {
                EditorGUILayout.HelpBox(
                    "Events available in code:\n" +
                    "• OnEnemyDamaged(int agentIndex, float damage, BaseSkill skill)\n" +
                    "• OnEnemyKilled(int agentIndex, BaseSkill skill)\n" +
                    "• OnSkillLevelUp(BaseSkill skill)",
                    MessageType.Info);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}
