using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Arawn.EnemyMasses.Runtime.Networking;

namespace Arawn.EnemyMasses.Editor.Networking
{
    /// <summary>
    /// Modern editor for the AntiCheatComponent with live statistics,
    /// player suspicion visualization, and configuration management.
    /// </summary>
    [CustomEditor(typeof(AntiCheatComponent))]
    public class AntiCheatComponentEditor : UnityEditor.Editor
    {
        // Foldout states
        private bool _showConfiguration = true;
        private bool _showStatistics = true;
        private bool _showPlayerSuspicion = true;
        private bool _showRecentDetections = true;
        private bool _showSubsystems = false;
        
        // Scroll positions
        private Vector2 _playerScrollPos;
        private Vector2 _detectionsScrollPos;
        
        // Detection log for display
        private static List<CheatDetectionEntry> _recentDetections = new List<CheatDetectionEntry>();
        private const int MaxDetectionLogEntries = 50;
        
        // Styles (lazy initialized)
        private static GUIStyle _headerStyle;
        private static GUIStyle _boxStyle;
        private static GUIStyle _statsValueStyle;
        private static GUIStyle _warningStyle;
        private static GUIStyle _errorStyle;
        private static GUIStyle _successStyle;
        private static GUIStyle _miniButtonStyle;
        private static bool _stylesInitialized;
        
        // Colors
        private static readonly Color HeaderColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color SuspicionLow = new Color(0.2f, 0.8f, 0.2f, 1f);
        private static readonly Color SuspicionMedium = new Color(0.9f, 0.7f, 0.1f, 1f);
        private static readonly Color SuspicionHigh = new Color(0.9f, 0.4f, 0.1f, 1f);
        private static readonly Color SuspicionCritical = new Color(0.9f, 0.1f, 0.1f, 1f);
        
        // Search/filter
        private string _playerSearchFilter = "";
        private CheatType? _cheatTypeFilter = null;
        
        private struct CheatDetectionEntry
        {
            public float Time;
            public int PlayerId;
            public CheatType Type;
            public string Details;
            public float Severity;
        }
        
        private void OnEnable()
        {
            // Subscribe to detection events if playing
            if (Application.isPlaying && target is AntiCheatComponent component)
            {
                var manager = component.GetRuntimeManager();
                if (manager != null)
                {
                    manager.OnCheatDetected -= OnCheatDetected;
                    manager.OnCheatDetected += OnCheatDetected;
                }
            }
            
            EditorApplication.update += OnEditorUpdate;
        }
        
        private void OnDisable()
        {
            if (target is AntiCheatComponent component && Application.isPlaying)
            {
                var manager = component.GetRuntimeManager();
                if (manager != null)
                {
                    manager.OnCheatDetected -= OnCheatDetected;
                }
            }
            
            EditorApplication.update -= OnEditorUpdate;
        }
        
        private void OnEditorUpdate()
        {
            // Repaint during play mode for live updates
            if (Application.isPlaying)
            {
                Repaint();
            }
        }
        
        private void OnCheatDetected(int playerId, CheatType type, string details, float severity)
        {
            _recentDetections.Insert(0, new CheatDetectionEntry
            {
                Time = Time.time,
                PlayerId = playerId,
                Type = type,
                Details = details,
                Severity = severity
            });
            
            // Trim old entries
            if (_recentDetections.Count > MaxDetectionLogEntries)
            {
                _recentDetections.RemoveAt(_recentDetections.Count - 1);
            }
        }
        
        private static void InitializeStyles()
        {
            if (_stylesInitialized) return;
            
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 4, 4)
            };
            
            _boxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
            
            _statsValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleRight
            };
            
            _warningStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.9f, 0.7f, 0.1f) },
                fontStyle = FontStyle.Bold
            };
            
            _errorStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.9f, 0.2f, 0.2f) },
                fontStyle = FontStyle.Bold
            };
            
            _successStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.2f, 0.8f, 0.2f) },
                fontStyle = FontStyle.Bold
            };
            
            _miniButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedWidth = 60
            };
            
            _stylesInitialized = true;
        }
        
        public override void OnInspectorGUI()
        {
            InitializeStyles();
            
            var component = (AntiCheatComponent)target;
            
            DrawHeader(component);
            
            EditorGUILayout.Space(4);
            
            // Runtime warning
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Anti-cheat monitoring is only active during Play Mode. " +
                    "Configuration changes made here will be applied when entering Play Mode.",
                    MessageType.Info);
                EditorGUILayout.Space(4);
            }
            
            DrawConfigurationSection(component);
            EditorGUILayout.Space(2);
            
            if (Application.isPlaying && component.IsInitialized)
            {
                DrawStatisticsSection(component);
                EditorGUILayout.Space(2);
                
                DrawPlayerSuspicionSection(component);
                EditorGUILayout.Space(2);
                
                DrawRecentDetectionsSection();
                EditorGUILayout.Space(2);
                
                DrawSubsystemsSection(component);
            }
            
            // Apply changes
            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
        
        private void DrawHeader(AntiCheatComponent component)
        {
            var headerRect = EditorGUILayout.GetControlRect(false, 36);
            
            // Background
            EditorGUI.DrawRect(headerRect, HeaderColor);
            
            // Icon and title
            var iconRect = new Rect(headerRect.x + 8, headerRect.y + 6, 24, 24);
            GUI.Label(iconRect, EditorGUIUtility.IconContent("d_console.erroricon.inactive.sml"));
            
            var titleRect = new Rect(headerRect.x + 38, headerRect.y, headerRect.width - 120, headerRect.height);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
            GUI.Label(titleRect, "Anti-Cheat Manager", titleStyle);
            
            // Status indicator
            var statusRect = new Rect(headerRect.xMax - 80, headerRect.y + 8, 70, 20);
            var statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };
            
            if (Application.isPlaying && component.IsInitialized)
            {
                statusStyle.normal.textColor = new Color(0.5f, 1f, 0.5f);
                GUI.Label(statusRect, "● ACTIVE", statusStyle);
            }
            else
            {
                statusStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
                GUI.Label(statusRect, "○ IDLE", statusStyle);
            }
        }
        
        private void DrawConfigurationSection(AntiCheatComponent component)
        {
            _showConfiguration = EditorGUILayout.BeginFoldoutHeaderGroup(_showConfiguration, "Configuration");
            
            if (_showConfiguration)
            {
                EditorGUILayout.BeginVertical(_boxStyle);
                
                // Preset buttons
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Quick Presets:", GUILayout.Width(90));
                
                if (GUILayout.Button("Default", EditorStyles.miniButtonLeft))
                {
                    Undo.RecordObject(component, "Apply Default Preset");
                    component.ApplyPreset(AntiCheatComponent.ConfigPreset.Default);
                    EditorUtility.SetDirty(component);
                }
                if (GUILayout.Button("Strict", EditorStyles.miniButtonMid))
                {
                    Undo.RecordObject(component, "Apply Strict Preset");
                    component.ApplyPreset(AntiCheatComponent.ConfigPreset.Strict);
                    EditorUtility.SetDirty(component);
                }
                if (GUILayout.Button("Lenient", EditorStyles.miniButtonRight))
                {
                    Undo.RecordObject(component, "Apply Lenient Preset");
                    component.ApplyPreset(AntiCheatComponent.ConfigPreset.Lenient);
                    EditorUtility.SetDirty(component);
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(8);
                
                // Draw default inspector properties
                serializedObject.Update();
                
                var iterator = serializedObject.GetIterator();
                iterator.NextVisible(true); // Skip script
                
                while (iterator.NextVisible(false))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
                
                serializedObject.ApplyModifiedProperties();
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawStatisticsSection(AntiCheatComponent component)
        {
            _showStatistics = EditorGUILayout.BeginFoldoutHeaderGroup(_showStatistics, "Live Statistics");
            
            if (_showStatistics)
            {
                EditorGUILayout.BeginVertical(_boxStyle);
                
                var stats = component.GetStatistics();
                
                // Stats grid
                EditorGUILayout.BeginHorizontal();
                DrawStatCard("Detections", stats.totalDetections.ToString(), 
                    stats.totalDetections > 0 ? _warningStyle : _successStyle);
                DrawStatCard("Flagged", stats.playersFlagged.ToString(), 
                    stats.playersFlagged > 0 ? _errorStyle : _successStyle);
                DrawStatCard("Tracked", stats.playersTracked.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(4);
                
                // Detailed stats
                EditorGUILayout.LabelField("Damage Validation", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Checks: {stats.damageChecks}, Rejections: {stats.damageRejections} ({stats.damageRejectRate:P1})");
                EditorGUI.indentLevel--;
                
                EditorGUILayout.LabelField("Command Validation", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Checks: {stats.commandChecks}, Rejections: {stats.commandRejections} ({stats.commandRejectRate:P1})");
                EditorGUI.indentLevel--;
                
                EditorGUILayout.LabelField("Speed Detection", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Checks: {stats.movementChecks}, Violations: {stats.speedViolations}, Teleports: {stats.teleportDetections}");
                EditorGUI.indentLevel--;
                
                // Most common cheat type
                if (stats.totalDetections > 0 && stats.detectionsByType != null && stats.detectionsByType.Count > 0)
                {
                    var mostCommon = stats.detectionsByType
                        .OrderByDescending(kvp => kvp.Value)
                        .FirstOrDefault();
                    
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"Most Common: {mostCommon.Key} ({mostCommon.Value} detections)", 
                        EditorStyles.miniLabel);
                }
                
                // Reset button
                EditorGUILayout.Space(4);
                if (GUILayout.Button("Reset Statistics", EditorStyles.miniButton))
                {
                    component.ResetStatistics();
                    _recentDetections.Clear();
                }
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawStatCard(string label, string value, GUIStyle valueStyle)
        {
            EditorGUILayout.BeginVertical(_boxStyle, GUILayout.MinWidth(80));
            EditorGUILayout.LabelField(value, valueStyle, GUILayout.Height(24));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
        }
        
        private void DrawPlayerSuspicionSection(AntiCheatComponent component)
        {
            _showPlayerSuspicion = EditorGUILayout.BeginFoldoutHeaderGroup(_showPlayerSuspicion, "Player Suspicion Levels");
            
            if (_showPlayerSuspicion)
            {
                EditorGUILayout.BeginVertical(_boxStyle);
                
                // Search filter
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
                _playerSearchFilter = EditorGUILayout.TextField(_playerSearchFilter, EditorStyles.toolbarSearchField);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(4);
                
                var suspicionLevels = component.GetAllSuspicionLevels();
                
                if (suspicionLevels == null || suspicionLevels.Count == 0)
                {
                    EditorGUILayout.LabelField("No players tracked yet", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    // Sort by suspicion (highest first)
                    var sorted = suspicionLevels
                        .Where(kvp => string.IsNullOrEmpty(_playerSearchFilter) || 
                                      kvp.Key.ToString().Contains(_playerSearchFilter))
                        .OrderByDescending(kvp => kvp.Value)
                        .ToList();
                    
                    _playerScrollPos = EditorGUILayout.BeginScrollView(_playerScrollPos, GUILayout.MaxHeight(200));
                    
                    foreach (var kvp in sorted)
                    {
                        DrawPlayerSuspicionRow(component, kvp.Key, kvp.Value);
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawPlayerSuspicionRow(AntiCheatComponent component, int playerId, float suspicion)
        {
            float threshold = component.SuspicionThreshold;
            float ratio = Mathf.Clamp01(suspicion / threshold);
            
            // Determine color
            Color barColor;
            if (ratio < 0.25f) barColor = SuspicionLow;
            else if (ratio < 0.5f) barColor = SuspicionMedium;
            else if (ratio < 0.75f) barColor = SuspicionHigh;
            else barColor = SuspicionCritical;
            
            EditorGUILayout.BeginHorizontal();
            
            // Player ID
            EditorGUILayout.LabelField($"Player {playerId}", GUILayout.Width(80));
            
            // Suspicion bar
            var barRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(18));
            EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f, 1f));
            
            var filledRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
            EditorGUI.DrawRect(filledRect, barColor);
            
            // Value label
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(barRect, $"{suspicion:F1} / {threshold:F0}", labelStyle);
            
            // Actions
            if (GUILayout.Button("Reset", _miniButtonStyle))
            {
                component.ResetPlayerSuspicion(playerId);
            }
            
            if (GUILayout.Button("Flag", _miniButtonStyle))
            {
                component.FlagPlayer(playerId);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawRecentDetectionsSection()
        {
            _showRecentDetections = EditorGUILayout.BeginFoldoutHeaderGroup(_showRecentDetections, 
                $"Recent Detections ({_recentDetections.Count})");
            
            if (_showRecentDetections)
            {
                EditorGUILayout.BeginVertical(_boxStyle);
                
                if (_recentDetections.Count == 0)
                {
                    EditorGUILayout.LabelField("No detections recorded", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    // Filter
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Filter Type:", GUILayout.Width(70));
                    
                    var options = new List<string> { "All" };
                    options.AddRange(System.Enum.GetNames(typeof(CheatType)));
                    
                    int selectedIndex = _cheatTypeFilter.HasValue 
                        ? options.IndexOf(_cheatTypeFilter.Value.ToString()) 
                        : 0;
                    
                    int newIndex = EditorGUILayout.Popup(selectedIndex, options.ToArray());
                    _cheatTypeFilter = newIndex == 0 ? null : (CheatType?)System.Enum.Parse(typeof(CheatType), options[newIndex]);
                    
                    if (GUILayout.Button("Clear Log", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        _recentDetections.Clear();
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space(4);
                    
                    _detectionsScrollPos = EditorGUILayout.BeginScrollView(_detectionsScrollPos, GUILayout.MaxHeight(200));
                    
                    var filtered = _recentDetections
                        .Where(d => !_cheatTypeFilter.HasValue || d.Type == _cheatTypeFilter.Value)
                        .ToList();
                    
                    foreach (var detection in filtered)
                    {
                        DrawDetectionEntry(detection);
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawDetectionEntry(CheatDetectionEntry entry)
        {
            // Color based on severity
            Color bgColor = entry.Severity > 0.7f ? new Color(0.4f, 0.15f, 0.15f, 0.5f) :
                           entry.Severity > 0.4f ? new Color(0.4f, 0.3f, 0.1f, 0.5f) :
                           new Color(0.2f, 0.2f, 0.2f, 0.3f);
            
            var rect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(rect, bgColor);
            
            // Time
            EditorGUILayout.LabelField($"[{entry.Time:F1}s]", EditorStyles.miniLabel, GUILayout.Width(50));
            
            // Player
            EditorGUILayout.LabelField($"P{entry.PlayerId}", EditorStyles.miniLabel, GUILayout.Width(30));
            
            // Type with icon
            var typeIcon = GetCheatTypeIcon(entry.Type);
            EditorGUILayout.LabelField(new GUIContent($"{entry.Type}", typeIcon), 
                EditorStyles.miniLabel, GUILayout.Width(100));
            
            // Details
            EditorGUILayout.LabelField(entry.Details, EditorStyles.miniLabel);
            
            EditorGUILayout.EndHorizontal();
        }
        
        private Texture GetCheatTypeIcon(CheatType type)
        {
            return type switch
            {
                CheatType.SpeedHack => EditorGUIUtility.IconContent("d_Animation.Play").image,
                CheatType.Teleport => EditorGUIUtility.IconContent("d_Transform Icon").image,
                CheatType.DamageHack => EditorGUIUtility.IconContent("d_console.erroricon.sml").image,
                CheatType.GodMode => EditorGUIUtility.IconContent("d_Favorite Icon").image,
                CheatType.CommandFlooding => EditorGUIUtility.IconContent("d_console.warnicon.sml").image,
                CheatType.OwnershipViolation => EditorGUIUtility.IconContent("d_UnityEditor.HierarchyWindow").image,
                CheatType.Suspicious => EditorGUIUtility.IconContent("d_Settings Icon").image,
                _ => null
            };
        }
        
        private void DrawSubsystemsSection(AntiCheatComponent component)
        {
            _showSubsystems = EditorGUILayout.BeginFoldoutHeaderGroup(_showSubsystems, "Subsystem Status");
            
            if (_showSubsystems)
            {
                EditorGUILayout.BeginVertical(_boxStyle);
                
                DrawSubsystemStatus("Damage Validator", component.DamageValidator != null);
                DrawSubsystemStatus("Command Validator", component.CommandValidator != null);
                DrawSubsystemStatus("Speed Detector", component.SpeedDetector != null);
                DrawSubsystemStatus("Reconciliation System", component.ReconciliationSystem != null);
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        private void DrawSubsystemStatus(string name, bool initialized)
        {
            EditorGUILayout.BeginHorizontal();
            
            var icon = initialized 
                ? EditorGUIUtility.IconContent("d_greenLight") 
                : EditorGUIUtility.IconContent("d_orangeLight");
            
            EditorGUILayout.LabelField(new GUIContent(name, icon.image), GUILayout.Height(18));
            
            var statusStyle = initialized ? _successStyle : _warningStyle;
            EditorGUILayout.LabelField(initialized ? "Ready" : "Not Initialized", statusStyle, GUILayout.Width(100));
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
