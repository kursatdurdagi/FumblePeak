using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.EnemyMassesRTSAI))]
    public class EnemyMassesRTSAIEditor : UnityEditor.Editor
    {
        private Runtime.EnemyMassesRTSAI _ai;

        // References
        private SerializedProperty _crowdController;
        private SerializedProperty _playerRTSController;

        // Faction Control
        private SerializedProperty _controlledFactions;
        private SerializedProperty _enemyFactions;
        private SerializedProperty _autoDetectEnemyFactions;

        // Difficulty
        private SerializedProperty _difficulty;
        private SerializedProperty _decisionInterval;
        private SerializedProperty _decisionVariance;

        // Exploration
        private SerializedProperty _enableExploration;
        private SerializedProperty _explorationRadius;
        private SerializedProperty _explorationDuration;
        private SerializedProperty _minExplorationDistance;

        // Combat Tactics
        private SerializedProperty _enableAdvancedTactics;
        private SerializedProperty _minAgentsForBaiting;
        private SerializedProperty _baitPercentage;
        private SerializedProperty _baitRetreatDistance;
        private SerializedProperty _engagementRange;
        private SerializedProperty _retreatRatio;
        private SerializedProperty _regroupDistance;

        // Group Splitting
        private SerializedProperty _enableGroupSplitting;
        private SerializedProperty _maxGroupSize;
        private SerializedProperty _minGroupSize;
        private SerializedProperty _maxGroups;
        private SerializedProperty _flankingDistance;
        private SerializedProperty _flankingAngle;
        private SerializedProperty _flankingDelay;
        private SerializedProperty _attackDistribution;

        // Formation Spacing (AoE Avoidance)
        private SerializedProperty _enableAoEAvoidance;
        private SerializedProperty _normalSpacing;
        private SerializedProperty _spreadSpacing;
        private SerializedProperty _maxSpreadSpacing;
        private SerializedProperty _spacingAdjustmentSpeed;
        private SerializedProperty _autoDetectAoEThreats;
        private SerializedProperty _manualAoEThreatLevel;
        private SerializedProperty _aoEThresholdRadius;

        // Spawning
        private SerializedProperty _enableSpawning;
        private SerializedProperty _maxAgentCount;
        private SerializedProperty _spawnWaveSize;
        private SerializedProperty _spawnInterval;
        private SerializedProperty _spawnFormation;
        private SerializedProperty _spawnLocation;

        // Visual
        private SerializedProperty _showGizmos;
        private SerializedProperty _aiColor;

        // Debug
        private SerializedProperty _debugLogs;

        private static readonly string[] DifficultyNames = new string[]
        {
            "Beginner",
            "Easy",
            "Normal",
            "Hard",
            "Expert",
            "Nightmare"
        };

        private static readonly Color[] DifficultyColors = new Color[]
        {
            new Color(0.3f, 0.8f, 0.3f),    // Beginner - Green
            new Color(0.5f, 0.9f, 0.5f),    // Easy - Light Green
            new Color(0.9f, 0.9f, 0.3f),    // Normal - Yellow
            new Color(1f, 0.6f, 0.2f),      // Hard - Orange
            new Color(1f, 0.3f, 0.3f),      // Expert - Red
            new Color(0.6f, 0.1f, 0.6f)     // Nightmare - Purple
        };

        private void OnEnable()
        {
            if (target == null) return;

            _ai = target as Runtime.EnemyMassesRTSAI;
            if (_ai == null) return;

            // References
            _crowdController = serializedObject.FindProperty("crowdController");
            _playerRTSController = serializedObject.FindProperty("playerRTSController");

            // Faction Control
            _controlledFactions = serializedObject.FindProperty("controlledFactions");
            _enemyFactions = serializedObject.FindProperty("enemyFactions");
            _autoDetectEnemyFactions = serializedObject.FindProperty("autoDetectEnemyFactions");

            // Difficulty
            _difficulty = serializedObject.FindProperty("difficulty");
            _decisionInterval = serializedObject.FindProperty("decisionInterval");
            _decisionVariance = serializedObject.FindProperty("decisionVariance");

            // Exploration
            _enableExploration = serializedObject.FindProperty("enableExploration");
            _explorationRadius = serializedObject.FindProperty("explorationRadius");
            _explorationDuration = serializedObject.FindProperty("explorationDuration");
            _minExplorationDistance = serializedObject.FindProperty("minExplorationDistance");

            // Combat Tactics
            _enableAdvancedTactics = serializedObject.FindProperty("enableAdvancedTactics");
            _minAgentsForBaiting = serializedObject.FindProperty("minAgentsForBaiting");
            _baitPercentage = serializedObject.FindProperty("baitPercentage");
            _baitRetreatDistance = serializedObject.FindProperty("baitRetreatDistance");
            _engagementRange = serializedObject.FindProperty("engagementRange");
            _retreatRatio = serializedObject.FindProperty("retreatRatio");
            _regroupDistance = serializedObject.FindProperty("regroupDistance");

            // Group Splitting
            _enableGroupSplitting = serializedObject.FindProperty("enableGroupSplitting");
            _maxGroupSize = serializedObject.FindProperty("maxGroupSize");
            _minGroupSize = serializedObject.FindProperty("minGroupSize");
            _maxGroups = serializedObject.FindProperty("maxGroups");
            _flankingDistance = serializedObject.FindProperty("flankingDistance");
            _flankingAngle = serializedObject.FindProperty("flankingAngle");
            _flankingDelay = serializedObject.FindProperty("flankingDelay");
            _attackDistribution = serializedObject.FindProperty("attackDistribution");

            // Formation Spacing (AoE Avoidance)
            _enableAoEAvoidance = serializedObject.FindProperty("enableAoEAvoidance");
            _normalSpacing = serializedObject.FindProperty("normalSpacing");
            _spreadSpacing = serializedObject.FindProperty("spreadSpacing");
            _maxSpreadSpacing = serializedObject.FindProperty("maxSpreadSpacing");
            _spacingAdjustmentSpeed = serializedObject.FindProperty("spacingAdjustmentSpeed");
            _autoDetectAoEThreats = serializedObject.FindProperty("autoDetectAoEThreats");
            _manualAoEThreatLevel = serializedObject.FindProperty("manualAoEThreatLevel");
            _aoEThresholdRadius = serializedObject.FindProperty("aoEThresholdRadius");

            // Spawning
            _enableSpawning = serializedObject.FindProperty("enableSpawning");
            _maxAgentCount = serializedObject.FindProperty("maxAgentCount");
            _spawnWaveSize = serializedObject.FindProperty("spawnWaveSize");
            _spawnInterval = serializedObject.FindProperty("spawnInterval");
            _spawnFormation = serializedObject.FindProperty("spawnFormation");
            _spawnLocation = serializedObject.FindProperty("spawnLocation");

            // Visual
            _showGizmos = serializedObject.FindProperty("showGizmos");
            _aiColor = serializedObject.FindProperty("aiColor");

            // Debug
            _debugLogs = serializedObject.FindProperty("debugLogs");
        }

        public override void OnInspectorGUI()
        {
            if (_ai == null)
            {
                EditorGUILayout.HelpBox("Target object is null or has been destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawReferences();
            EditorGUILayout.Space(5);

            DrawFactionControl();
            EditorGUILayout.Space(5);

            DrawDifficultySettings();
            EditorGUILayout.Space(5);

            DrawExplorationSettings();
            EditorGUILayout.Space(5);

            DrawCombatTactics();
            EditorGUILayout.Space(5);

            DrawGroupSplitting();
            EditorGUILayout.Space(5);

            DrawFormationSpacing();
            EditorGUILayout.Space(5);

            DrawSpawningSettings();
            EditorGUILayout.Space(5);

            DrawVisualSettings();
            EditorGUILayout.Space(5);

            DrawDebugSettings();
            EditorGUILayout.Space(10);

            DrawRuntimeControls();

            serializedObject.ApplyModifiedProperties();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            // Difficulty-based color
            int diffIdx = Mathf.Clamp(_difficulty.intValue, 0, DifficultyColors.Length - 1);
            GUI.color = DifficultyColors[diffIdx];
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Enemy Masses RTS AI", titleStyle);

            // Show current difficulty
            GUIStyle diffStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                normal = { textColor = DifficultyColors[diffIdx] }
            };
            EditorGUILayout.LabelField($"Difficulty: {DifficultyNames[diffIdx]}", diffStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawReferences()
        {
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_crowdController);
            if (_crowdController.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Will auto-find EnemyMassesCrowdController in scene.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(_playerRTSController, new GUIContent("Player RTS Controller"));
            if (_playerRTSController.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Will auto-find player's RTS controller to detect enemy factions.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFactionControl()
        {
            EditorGUILayout.LabelField("Faction Control", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_controlledFactions, new GUIContent("AI Controlled Factions"));
            
            bool hasControlled = _controlledFactions.FindPropertyRelative("selectedFactions") != null &&
                                _controlledFactions.FindPropertyRelative("selectedFactions").arraySize > 0;
            
            if (!hasControlled)
            {
                EditorGUILayout.HelpBox("Select which factions this AI will control.", MessageType.Warning);
            }

            EditorGUILayout.Space(3);

            EditorGUILayout.PropertyField(_autoDetectEnemyFactions);
            
            if (!_autoDetectEnemyFactions.boolValue)
            {
                EditorGUILayout.PropertyField(_enemyFactions, new GUIContent("Enemy Factions"));
            }
            else
            {
                EditorGUILayout.HelpBox("Enemy factions will be auto-detected from player's RTS Controller.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDifficultySettings()
        {
            EditorGUILayout.LabelField("Difficulty", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Custom difficulty slider with labels
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("AI Difficulty", GUILayout.Width(100));
            
            int diffIdx = Mathf.Clamp(_difficulty.intValue, 0, 5);
            GUI.color = DifficultyColors[diffIdx];
            _difficulty.intValue = EditorGUILayout.IntSlider(_difficulty.intValue, 0, 5);
            GUI.color = Color.white;
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = DifficultyColors[_difficulty.intValue] }
            };
            EditorGUILayout.LabelField(DifficultyNames[_difficulty.intValue], labelStyle, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            // Difficulty description
            string diffDesc = _difficulty.intValue switch
            {
                0 => "Slow reactions, basic tactics, makes mistakes.",
                1 => "Slower reactions, rarely uses advanced tactics.",
                2 => "Balanced AI with moderate decision making.",
                3 => "Fast reactions, uses flanking and baiting.",
                4 => "Very aggressive, uses advanced trapping tactics.",
                5 => "Brutal AI, perfect timing, ruthless tactics.",
                _ => ""
            };
            EditorGUILayout.HelpBox(diffDesc, MessageType.None);

            EditorGUILayout.Space(3);

            EditorGUILayout.PropertyField(_decisionInterval, new GUIContent("Decision Interval (s)"));
            EditorGUILayout.PropertyField(_decisionVariance, new GUIContent("Timing Variance"));

            EditorGUILayout.EndVertical();
        }

        private void DrawExplorationSettings()
        {
            EditorGUILayout.LabelField("Exploration", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableExploration);

            if (_enableExploration.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_explorationRadius, new GUIContent("Radius"));
                EditorGUILayout.PropertyField(_explorationDuration, new GUIContent("Duration (s)"));
                EditorGUILayout.PropertyField(_minExplorationDistance, new GUIContent("Min Distance"));
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("AI will wait idle until enemies are in range.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCombatTactics()
        {
            EditorGUILayout.LabelField("Combat Tactics", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_engagementRange, new GUIContent("Engagement Range"));
            EditorGUILayout.PropertyField(_retreatRatio, new GUIContent("Retreat When Outnumbered"));
            EditorGUILayout.PropertyField(_regroupDistance, new GUIContent("Regroup Distance"));

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(_enableAdvancedTactics, new GUIContent("Enable Advanced Tactics"));

            if (_enableAdvancedTactics.boolValue)
            {
                EditorGUILayout.HelpBox("Advanced tactics include baiting enemies into traps and encirclement.", MessageType.Info);
                
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_minAgentsForBaiting, new GUIContent("Min Agents for Baiting"));
                
                // Bait percentage as percentage display
                float baitPct = _baitPercentage.floatValue * 100f;
                baitPct = EditorGUILayout.Slider("Bait Force %", baitPct, 1f, 20f);
                _baitPercentage.floatValue = baitPct / 100f;
                
                EditorGUILayout.PropertyField(_baitRetreatDistance, new GUIContent("Bait Retreat Distance"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGroupSplitting()
        {
            EditorGUILayout.LabelField("Group Splitting (Multi-Directional Attacks)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableGroupSplitting, new GUIContent("Enable Group Splitting"));

            if (_enableGroupSplitting.boolValue)
            {
                EditorGUILayout.HelpBox("AI will split large forces into tactical groups for flanking and multi-directional attacks.", MessageType.Info);

                EditorGUI.indentLevel++;

                // Group size settings
                EditorGUILayout.LabelField("Group Sizes", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_maxGroupSize, new GUIContent("Max Group Size", "Forces split when larger than this"));
                EditorGUILayout.PropertyField(_minGroupSize, new GUIContent("Min Group Size", "Minimum agents to form a group"));
                EditorGUILayout.PropertyField(_maxGroups, new GUIContent("Max Groups", "Maximum tactical groups"));

                EditorGUILayout.Space(5);

                // Flanking settings
                EditorGUILayout.LabelField("Flanking Maneuvers", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_flankingDistance, new GUIContent("Flanking Distance", "How far flanking groups position from target"));
                EditorGUILayout.PropertyField(_flankingAngle, new GUIContent("Flanking Angle (°)", "90 = perpendicular, 135 = rear flank"));
                EditorGUILayout.PropertyField(_flankingDelay, new GUIContent("Flanking Delay (s)", "Delay before flanks engage after main force"));

                EditorGUILayout.Space(5);

                // Attack distribution
                EditorGUILayout.LabelField("Force Distribution", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_attackDistribution, new GUIContent("Attack Distribution"));
                
                // Show distribution breakdown
                Vector3 dist = _attackDistribution.vector3Value;
                float total = dist.x + dist.y + dist.z;
                if (total > 0)
                {
                    int frontPct = Mathf.RoundToInt((dist.x / total) * 100f);
                    int flankPct = Mathf.RoundToInt((dist.y / total) * 100f);
                    int rearPct = Mathf.RoundToInt((dist.z / total) * 100f);
                    EditorGUILayout.HelpBox($"Front: {frontPct}% | Flanks: {flankPct}% | Rear: {rearPct}%", MessageType.None);
                }

                EditorGUI.indentLevel--;

                // Show runtime status
                if (Application.isPlaying && _ai != null)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Runtime Status", EditorStyles.miniBoldLabel);
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.IntField("Active Groups", _ai.TacticalGroupCount);
                    EditorGUI.EndDisabledGroup();

                    // Show each group's info
                    if (_ai.TacticalGroupCount > 0)
                    {
                        foreach (var group in _ai.TacticalGroups)
                        {
                            Color groupColor = GetGroupColor(group.role);
                            GUIStyle groupStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = groupColor } };
                            EditorGUILayout.LabelField($"  {group.role}: {group.Count} agents", groupStyle);
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enable to allow the AI to split forces for flanking attacks.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private Color GetGroupColor(Runtime.EnemyMassesRTSAI.TacticalRole role)
        {
            return role switch
            {
                Runtime.EnemyMassesRTSAI.TacticalRole.MainForce => new Color(1f, 0.5f, 0.2f),
                Runtime.EnemyMassesRTSAI.TacticalRole.LeftFlank => new Color(0.3f, 0.7f, 1f),
                Runtime.EnemyMassesRTSAI.TacticalRole.RightFlank => new Color(0.3f, 1f, 0.7f),
                Runtime.EnemyMassesRTSAI.TacticalRole.RearGuard => new Color(0.8f, 0.3f, 0.8f),
                Runtime.EnemyMassesRTSAI.TacticalRole.Reserve => Color.gray,
                Runtime.EnemyMassesRTSAI.TacticalRole.Bait => Color.yellow,
                _ => Color.white
            };
        }

        private void DrawFormationSpacing()
        {
            EditorGUILayout.LabelField("Formation Spacing (AoE Avoidance)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableAoEAvoidance, new GUIContent("Enable AoE Avoidance"));

            if (_enableAoEAvoidance.boolValue)
            {
                EditorGUILayout.HelpBox("AI will dynamically adjust formation spacing when facing enemies with AoE/splash damage attacks.", MessageType.Info);
                
                EditorGUI.indentLevel++;
                
                // Spacing settings
                EditorGUILayout.LabelField("Spacing Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_normalSpacing, new GUIContent("Normal Spacing", "Formation spacing when no AoE threat"));
                EditorGUILayout.PropertyField(_spreadSpacing, new GUIContent("Spread Spacing", "Formation spacing when AoE threat detected"));
                EditorGUILayout.PropertyField(_maxSpreadSpacing, new GUIContent("Max Spread Spacing", "Maximum spacing against heavy AoE"));
                EditorGUILayout.PropertyField(_spacingAdjustmentSpeed, new GUIContent("Adjustment Speed", "Time to fully adjust spacing"));
                
                EditorGUILayout.Space(5);
                
                // Detection settings
                EditorGUILayout.LabelField("Threat Detection", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_autoDetectAoEThreats, new GUIContent("Auto-Detect AoE", "Detect AoE from enemy EnemyCrowd settings"));
                
                if (!_autoDetectAoEThreats.boolValue)
                {
                    EditorGUILayout.PropertyField(_manualAoEThreatLevel, new GUIContent("Manual Threat Level", "0 = none, 1 = maximum"));
                }
                
                EditorGUILayout.PropertyField(_aoEThresholdRadius, new GUIContent("AoE Threshold Radius", "Minimum damage radius to consider as AoE"));
                
                EditorGUI.indentLevel--;
                
                // Show current values at runtime
                if (Application.isPlaying && _ai != null)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Runtime Status", EditorStyles.miniBoldLabel);
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Slider("Current Threat Level", _ai.CurrentAoEThreatLevel, 0f, 1f);
                    EditorGUILayout.FloatField("Current Spacing", _ai.CurrentFormationSpacing);
                    EditorGUI.EndDisabledGroup();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSpawningSettings()
        {
            EditorGUILayout.LabelField("Spawning (Optional)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_enableSpawning, new GUIContent("Enable AI Spawning"));

            if (_enableSpawning.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_maxAgentCount, new GUIContent("Max Agents"));
                EditorGUILayout.PropertyField(_spawnWaveSize, new GUIContent("Wave Size"));
                EditorGUILayout.PropertyField(_spawnInterval, new GUIContent("Spawn Interval (s)"));
                EditorGUILayout.PropertyField(_spawnFormation, new GUIContent("Formation"));
                EditorGUILayout.PropertyField(_spawnLocation, new GUIContent("Spawn Location"));

                if (_spawnLocation.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("No spawn location set. Will spawn near existing forces.", MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("AI will only control existing agents, not spawn new ones.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVisualSettings()
        {
            EditorGUILayout.LabelField("Visual Feedback", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showGizmos);
            if (_showGizmos.boolValue)
            {
                EditorGUILayout.PropertyField(_aiColor, new GUIContent("AI Color"));
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDebugSettings()
        {
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_debugLogs, new GUIContent("Enable Debug Logs"));

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeControls()
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (Application.isPlaying)
            {
                // Status display
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Current State:", GUILayout.Width(100));
                
                Color stateColor = GetStateColor(_ai.CurrentState);
                GUIStyle stateStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = stateColor } };
                EditorGUILayout.LabelField(_ai.CurrentState.ToString(), stateStyle);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField($"Controlled Agents: {_ai.ControlledAgentCount}");
                EditorGUILayout.LabelField($"Enemy Agents: {_ai.EnemyAgentCount}");

                EditorGUILayout.Space(5);

                // Manual controls
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Force Attack", GUILayout.Height(24)))
                {
                    _ai.ForceAttack();
                }
                if (GUILayout.Button("Force Retreat", GUILayout.Height(24)))
                {
                    _ai.ForceRetreat();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Regroup", GUILayout.Height(24)))
                {
                    _ai.ForceRegroup();
                }
                if (GUILayout.Button("Flanking Attack", GUILayout.Height(24)))
                {
                    _ai.ForceFlankingAttack();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Split Groups", GUILayout.Height(22)))
                {
                    _ai.ForceSplitGroups();
                }
                if (GUILayout.Button("Consolidate", GUILayout.Height(22)))
                {
                    _ai.ForceConsolidateGroups();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                EditorGUILayout.LabelField("Set Stance:", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Offensive", GUILayout.Height(22)))
                {
                    _ai.SetStance(Runtime.CombatStance.Offensive);
                }
                if (GUILayout.Button("Defensive", GUILayout.Height(22)))
                {
                    _ai.SetStance(Runtime.CombatStance.Defensive);
                }
                if (GUILayout.Button("Hold", GUILayout.Height(22)))
                {
                    _ai.SetStance(Runtime.CombatStance.HoldPosition);
                }
                if (GUILayout.Button("Passive", GUILayout.Height(22)))
                {
                    _ai.SetStance(Runtime.CombatStance.Passive);
                }
                EditorGUILayout.EndHorizontal();

                // Repaint for live updates
                Repaint();
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use runtime controls.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private Color GetStateColor(Runtime.RTSAIState state)
        {
            return state switch
            {
                Runtime.RTSAIState.Idle => Color.gray,
                Runtime.RTSAIState.Exploring => Color.green,
                Runtime.RTSAIState.Engaging => Color.red,
                Runtime.RTSAIState.Retreating => Color.yellow,
                Runtime.RTSAIState.Regrouping => Color.cyan,
                Runtime.RTSAIState.SettingTrap => new Color(1f, 0.5f, 0f),
                Runtime.RTSAIState.Baiting => new Color(1f, 0f, 0.5f),
                Runtime.RTSAIState.Flanking => new Color(0.5f, 0f, 1f),
                _ => Color.white
            };
        }
    }
}
