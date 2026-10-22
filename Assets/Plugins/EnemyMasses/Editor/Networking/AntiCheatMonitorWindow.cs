using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Arawn.EnemyMasses.Runtime.Networking;

namespace Arawn.EnemyMasses.Editor.Networking
{
    /// <summary>
    /// Editor window for debugging and monitoring anti-cheat during development.
    /// This is a development tool only - it runs in the Unity Editor and is not
    /// included in builds. For in-game admin panels, create your own UI that
    /// queries AntiCheatComponent.GetStatistics() and GetAllSuspicionLevels().
    /// 
    /// Accessible from Tools > Enemy Masses > Network > Anti-Cheat Monitor
    /// </summary>
    public class AntiCheatMonitorWindow : EditorWindow
    {
        // Tab state
        private int _selectedTab;
        private readonly string[] _tabNames = { "Overview", "Players", "Detection Log", "Settings" };
        
        // Scroll positions
        private Vector2 _overviewScroll;
        private Vector2 _playersScroll;
        private Vector2 _logScroll;
        
        // Detection log
        private static List<CheatDetectionEntry> _detectionLog = new List<CheatDetectionEntry>();
        private const int MaxLogEntries = 200;
        
        // Filters
        private string _playerFilter = "";
        private CheatType? _typeFilter = null;
        private float _minSeverityFilter = 0f;
        
        // Auto-refresh
        private bool _autoRefresh = true;
        private float _lastRefreshTime;
        private const float RefreshInterval = 0.5f;
        
        // Styles
        private static GUIStyle _headerStyle;
        private static GUIStyle _boxStyle;
        private static GUIStyle _statCardStyle;
        private static GUIStyle _warningStyle;
        private static GUIStyle _errorStyle;
        private static GUIStyle _successStyle;
        private static bool _stylesInitialized;
        
        // Colors
        private static readonly Color TabActiveColor = new Color(0.3f, 0.5f, 0.8f, 1f);
        private static readonly Color SuspicionLow = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        private static readonly Color SuspicionMedium = new Color(0.9f, 0.7f, 0.1f, 0.8f);
        private static readonly Color SuspicionHigh = new Color(0.9f, 0.4f, 0.1f, 0.8f);
        private static readonly Color SuspicionCritical = new Color(0.9f, 0.1f, 0.1f, 0.8f);
        
        private struct CheatDetectionEntry
        {
            public float GameTime;
            public System.DateTime RealTime;
            public int PlayerId;
            public CheatType Type;
            public string Details;
            public float Severity;
        }
        
        [MenuItem("Tools/Enemy Masses/Network/Anti-Cheat Monitor")]
        public static void ShowWindow()
        {
            var window = GetWindow<AntiCheatMonitorWindow>();
            window.titleContent = new GUIContent("Anti-Cheat Monitor", EditorGUIUtility.IconContent("d_console.erroricon.inactive.sml").image);
            window.minSize = new Vector2(500, 400);
            window.Show();
        }
        
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SubscribeToEvents();
        }
        
        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnsubscribeFromEvents();
        }
        
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SubscribeToEvents();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                UnsubscribeFromEvents();
            }
        }
        
        private void SubscribeToEvents()
        {
            if (!Application.isPlaying) return;
            
            var component = AntiCheatComponent.Instance;
            if (component != null)
            {
                var manager = component.GetRuntimeManager();
                if (manager != null)
                {
                    manager.OnCheatDetected -= OnCheatDetected;
                    manager.OnCheatDetected += OnCheatDetected;
                }
            }
        }
        
        private void UnsubscribeFromEvents()
        {
            var component = AntiCheatComponent.Instance;
            if (component != null)
            {
                var manager = component.GetRuntimeManager();
                if (manager != null)
                {
                    manager.OnCheatDetected -= OnCheatDetected;
                }
            }
        }
        
        private void OnCheatDetected(int playerId, CheatType type, string details, float severity)
        {
            _detectionLog.Insert(0, new CheatDetectionEntry
            {
                GameTime = Time.time,
                RealTime = System.DateTime.Now,
                PlayerId = playerId,
                Type = type,
                Details = details,
                Severity = severity
            });
            
            if (_detectionLog.Count > MaxLogEntries)
            {
                _detectionLog.RemoveAt(_detectionLog.Count - 1);
            }
            
            Repaint();
        }
        
        private void Update()
        {
            if (_autoRefresh && Application.isPlaying && Time.realtimeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                _lastRefreshTime = Time.realtimeSinceStartup;
                Repaint();
            }
        }
        
        private static void InitializeStyles()
        {
            if (_stylesInitialized) return;
            
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            
            _boxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(4, 4, 4, 4)
            };
            
            _statCardStyle = new GUIStyle("box")
            {
                padding = new RectOffset(15, 15, 10, 10),
                margin = new RectOffset(4, 4, 4, 4)
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
            
            _stylesInitialized = true;
        }
        
        private void OnGUI()
        {
            InitializeStyles();
            
            // Header
            DrawWindowHeader();
            
            // Tabs
            EditorGUILayout.Space(4);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(28));
            EditorGUILayout.Space(4);
            
            // Content based on tab
            switch (_selectedTab)
            {
                case 0: DrawOverviewTab(); break;
                case 1: DrawPlayersTab(); break;
                case 2: DrawLogTab(); break;
                case 3: DrawSettingsTab(); break;
            }
        }
        
        private void DrawWindowHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Status
            var component = AntiCheatComponent.Instance;
            bool isActive = Application.isPlaying && component != null && component.IsInitialized;
            
            var statusContent = isActive 
                ? new GUIContent(" MONITORING", EditorGUIUtility.IconContent("d_greenLight").image)
                : new GUIContent(" INACTIVE", EditorGUIUtility.IconContent("d_orangeLight").image);
            
            GUILayout.Label(statusContent, EditorStyles.toolbarButton, GUILayout.Width(100));
            
            GUILayout.FlexibleSpace();
            
            // Auto-refresh toggle
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto Refresh", EditorStyles.toolbarButton);
            
            // Manual refresh
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                Repaint();
            }
            
            // Clear log
            if (GUILayout.Button("Clear Log", EditorStyles.toolbarButton))
            {
                _detectionLog.Clear();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawOverviewTab()
        {
            var component = AntiCheatComponent.Instance;
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to begin monitoring.", MessageType.Info);
                return;
            }
            
            if (component == null)
            {
                EditorGUILayout.HelpBox("No AntiCheatComponent found in scene. Add one to enable monitoring.", MessageType.Warning);
                if (GUILayout.Button("Create Anti-Cheat Manager"))
                {
                    var go = new GameObject("AntiCheatManager");
                    go.AddComponent<AntiCheatComponent>();
                    Selection.activeGameObject = go;
                }
                return;
            }
            
            _overviewScroll = EditorGUILayout.BeginScrollView(_overviewScroll);
            
            var stats = component.GetStatistics();
            
            // Summary cards
            EditorGUILayout.BeginHorizontal();
            DrawStatCard("Total\nDetections", stats.totalDetections.ToString(), 
                stats.totalDetections > 0 ? _warningStyle : _successStyle);
            DrawStatCard("Players\nFlagged", stats.playersFlagged.ToString(),
                stats.playersFlagged > 0 ? _errorStyle : _successStyle);
            DrawStatCard("Players\nTracked", stats.playersTracked.ToString(), EditorStyles.boldLabel);
            DrawStatCard("Active\nSubsystems", "4", _successStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // Subsystem breakdown
            EditorGUILayout.LabelField("Subsystem Statistics", _headerStyle);
            EditorGUILayout.BeginVertical(_boxStyle);
            
            DrawSubsystemRow("Damage Validation", stats.damageChecks, stats.damageRejections, stats.damageRejectRate);
            DrawSubsystemRow("Command Validation", stats.commandChecks, stats.commandRejections, stats.commandRejectRate);
            DrawSubsystemRow("Speed Detection", stats.movementChecks, stats.speedViolations, 
                stats.movementChecks > 0 ? (float)stats.speedViolations / stats.movementChecks : 0);
            DrawSubsystemRow("Reconciliation", stats.snapshotsSent, stats.urgentReconciliations,
                stats.snapshotsSent > 0 ? (float)stats.urgentReconciliations / stats.snapshotsSent : 0);
            
            EditorGUILayout.EndVertical();
            
            // Recent activity
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Recent Activity (Last 5)", _headerStyle);
            EditorGUILayout.BeginVertical(_boxStyle);
            
            var recent = _detectionLog.Take(5).ToList();
            if (recent.Count == 0)
            {
                EditorGUILayout.LabelField("No recent detections", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var entry in recent)
                {
                    DrawCompactLogEntry(entry);
                }
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawStatCard(string label, string value, GUIStyle valueStyle)
        {
            EditorGUILayout.BeginVertical(_statCardStyle, GUILayout.MinWidth(100), GUILayout.Height(70));
            
            var centeredValueStyle = new GUIStyle(valueStyle)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField(value, centeredValueStyle, GUILayout.Height(30));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawSubsystemRow(string name, int checks, int issues, float rate)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(150));
            EditorGUILayout.LabelField($"{checks} checks", GUILayout.Width(100));
            
            var issueStyle = issues > 0 ? _warningStyle : EditorStyles.label;
            EditorGUILayout.LabelField($"{issues} issues", issueStyle, GUILayout.Width(80));
            
            // Progress bar for rate
            var barRect = EditorGUILayout.GetControlRect(GUILayout.Width(100));
            EditorGUI.ProgressBar(barRect, rate, $"{rate:P1}");
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawPlayersTab()
        {
            var component = AntiCheatComponent.Instance;
            
            if (!Application.isPlaying || component == null)
            {
                EditorGUILayout.HelpBox("Player monitoring available during Play Mode with AntiCheatComponent.", MessageType.Info);
                return;
            }
            
            // Filter
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            _playerFilter = EditorGUILayout.TextField(_playerFilter, EditorStyles.toolbarSearchField);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(4);
            
            var suspicionLevels = component.GetAllSuspicionLevels();
            
            if (suspicionLevels == null || suspicionLevels.Count == 0)
            {
                EditorGUILayout.HelpBox("No players are currently being tracked.", MessageType.Info);
                return;
            }
            
            // Sort and filter
            var sorted = suspicionLevels
                .Where(kvp => string.IsNullOrEmpty(_playerFilter) || kvp.Key.ToString().Contains(_playerFilter))
                .OrderByDescending(kvp => kvp.Value)
                .ToList();
            
            _playersScroll = EditorGUILayout.BeginScrollView(_playersScroll);
            
            float threshold = component.SuspicionThreshold;
            
            foreach (var kvp in sorted)
            {
                DrawPlayerRow(component, kvp.Key, kvp.Value, threshold);
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawPlayerRow(AntiCheatComponent component, int playerId, float suspicion, float threshold)
        {
            float ratio = Mathf.Clamp01(suspicion / threshold);
            
            Color barColor = ratio < 0.25f ? SuspicionLow :
                            ratio < 0.5f ? SuspicionMedium :
                            ratio < 0.75f ? SuspicionHigh : SuspicionCritical;
            
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.BeginHorizontal();
            
            // Player info
            EditorGUILayout.LabelField($"Player {playerId}", EditorStyles.boldLabel, GUILayout.Width(100));
            
            // Suspicion bar
            var barRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(20));
            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f, 1f));
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height), barColor);
            
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) 
            { 
                normal = { textColor = Color.white }, 
                alignment = TextAnchor.MiddleCenter 
            };
            GUI.Label(barRect, $"{suspicion:F1} / {threshold:F0}", labelStyle);
            
            // Actions
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
            {
                component.ResetPlayerSuspicion(playerId);
            }
            if (GUILayout.Button("Flag", GUILayout.Width(40)))
            {
                component.FlagPlayer(playerId);
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        
        private void DrawLogTab()
        {
            // Filters
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField("Type:", GUILayout.Width(35));
            var typeOptions = new List<string> { "All" };
            typeOptions.AddRange(System.Enum.GetNames(typeof(CheatType)));
            int typeIndex = _typeFilter.HasValue ? typeOptions.IndexOf(_typeFilter.Value.ToString()) : 0;
            int newTypeIndex = EditorGUILayout.Popup(typeIndex, typeOptions.ToArray(), GUILayout.Width(120));
            _typeFilter = newTypeIndex == 0 ? null : (CheatType?)System.Enum.Parse(typeof(CheatType), typeOptions[newTypeIndex]);
            
            EditorGUILayout.LabelField("Min Severity:", GUILayout.Width(80));
            _minSeverityFilter = EditorGUILayout.Slider(_minSeverityFilter, 0f, 1f, GUILayout.Width(100));
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.LabelField($"{_detectionLog.Count} entries", EditorStyles.miniLabel);
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(4);
            
            // Log entries
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
            
            var filtered = _detectionLog
                .Where(e => (!_typeFilter.HasValue || e.Type == _typeFilter.Value) && e.Severity >= _minSeverityFilter)
                .ToList();
            
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("No detections matching filters.", MessageType.Info);
            }
            else
            {
                foreach (var entry in filtered)
                {
                    DrawLogEntry(entry);
                }
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawLogEntry(CheatDetectionEntry entry)
        {
            Color bgColor = entry.Severity > 0.7f ? new Color(0.5f, 0.15f, 0.15f, 0.3f) :
                           entry.Severity > 0.4f ? new Color(0.5f, 0.35f, 0.1f, 0.3f) :
                           new Color(0.2f, 0.2f, 0.2f, 0.2f);
            
            var rect = EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUI.DrawRect(rect, bgColor);
            
            EditorGUILayout.BeginHorizontal();
            
            // Timestamp
            EditorGUILayout.LabelField(entry.RealTime.ToString("HH:mm:ss"), EditorStyles.miniLabel, GUILayout.Width(60));
            
            // Game time
            EditorGUILayout.LabelField($"[{entry.GameTime:F1}s]", EditorStyles.miniLabel, GUILayout.Width(50));
            
            // Player
            EditorGUILayout.LabelField($"Player {entry.PlayerId}", GUILayout.Width(70));
            
            // Type
            var typeStyle = entry.Severity > 0.7f ? _errorStyle : entry.Severity > 0.4f ? _warningStyle : EditorStyles.label;
            EditorGUILayout.LabelField(entry.Type.ToString(), typeStyle, GUILayout.Width(100));
            
            // Severity
            EditorGUILayout.LabelField($"Sev: {entry.Severity:F2}", EditorStyles.miniLabel, GUILayout.Width(60));
            
            EditorGUILayout.EndHorizontal();
            
            // Details
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(entry.Details, EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawCompactLogEntry(CheatDetectionEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{entry.GameTime:F1}s]", EditorStyles.miniLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField($"P{entry.PlayerId}", EditorStyles.miniLabel, GUILayout.Width(25));
            
            var typeStyle = entry.Severity > 0.7f ? _errorStyle : entry.Severity > 0.4f ? _warningStyle : EditorStyles.miniLabel;
            EditorGUILayout.LabelField(entry.Type.ToString(), typeStyle, GUILayout.Width(90));
            EditorGUILayout.LabelField(entry.Details, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawSettingsTab()
        {
            var component = AntiCheatComponent.Instance;
            
            if (component == null)
            {
                EditorGUILayout.HelpBox("No AntiCheatComponent found. Create one to configure settings.", MessageType.Info);
                
                if (GUILayout.Button("Create Anti-Cheat Manager"))
                {
                    var go = new GameObject("AntiCheatManager");
                    go.AddComponent<AntiCheatComponent>();
                    Selection.activeGameObject = go;
                }
                return;
            }
            
            EditorGUILayout.HelpBox("Configure settings on the AntiCheatComponent in the scene.", MessageType.Info);
            
            if (GUILayout.Button("Select Anti-Cheat Component"))
            {
                Selection.activeGameObject = component.gameObject;
            }
            
            EditorGUILayout.Space(10);
            
            // Quick presets
            EditorGUILayout.LabelField("Quick Apply Preset", _headerStyle);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Default"))
            {
                Undo.RecordObject(component, "Apply Default Preset");
                component.ApplyPreset(AntiCheatComponent.ConfigPreset.Default);
                EditorUtility.SetDirty(component);
            }
            if (GUILayout.Button("Strict"))
            {
                Undo.RecordObject(component, "Apply Strict Preset");
                component.ApplyPreset(AntiCheatComponent.ConfigPreset.Strict);
                EditorUtility.SetDirty(component);
            }
            if (GUILayout.Button("Lenient"))
            {
                Undo.RecordObject(component, "Apply Lenient Preset");
                component.ApplyPreset(AntiCheatComponent.ConfigPreset.Lenient);
                EditorUtility.SetDirty(component);
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
