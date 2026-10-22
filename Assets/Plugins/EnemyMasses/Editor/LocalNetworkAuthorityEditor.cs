using UnityEngine;
using UnityEditor;
using Arawn.EnemyMasses.Runtime.Networking;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom editor for LocalNetworkAuthority providing a clean, modern inspector layout.
    /// Shows network status, configuration, and runtime statistics.
    /// </summary>
    [CustomEditor(typeof(LocalNetworkAuthority))]
    public class LocalNetworkAuthorityEditor : UnityEditor.Editor
    {
        // Serialized properties
        private SerializedProperty _simulateNetworkDelay;
        private SerializedProperty _simulatedDelayMs;
        private SerializedProperty _enableDebugLogging;
        private SerializedProperty _localPlayerId;

        // Foldout states
        private static bool _showConfiguration = true;
        private static bool _showRuntimeStats = true;
        private static bool _showSubAuthorities = false;
        private static bool _showIntegrationGuide = false;

        // Cached styles
        private GUIStyle _headerStyle;
        private GUIStyle _statusBoxStyle;

        private void OnEnable()
        {
            _simulateNetworkDelay = serializedObject.FindProperty("simulateNetworkDelay");
            _simulatedDelayMs = serializedObject.FindProperty("simulatedDelayMs");
            _enableDebugLogging = serializedObject.FindProperty("enableDebugLogging");
            _localPlayerId = serializedObject.FindProperty("localPlayerId");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InitStyles();

            var authority = (LocalNetworkAuthority)target;

            // Header
            DrawInspectorHeader();

            EditorGUILayout.Space(10);

            // Network Status
            DrawNetworkStatus(authority);

            EditorGUILayout.Space(5);

            // Configuration Section
            _showConfiguration = EditorGUILayout.BeginFoldoutHeaderGroup(_showConfiguration, "Configuration");
            if (_showConfiguration)
            {
                EditorGUI.indentLevel++;
                DrawConfiguration();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Runtime Statistics (Play Mode only)
            if (Application.isPlaying)
            {
                _showRuntimeStats = EditorGUILayout.BeginFoldoutHeaderGroup(_showRuntimeStats, "Runtime Statistics");
                if (_showRuntimeStats)
                {
                    EditorGUI.indentLevel++;
                    DrawRuntimeStats(authority);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();

                EditorGUILayout.Space(3);
            }

            // Sub-Authorities Status
            _showSubAuthorities = EditorGUILayout.BeginFoldoutHeaderGroup(_showSubAuthorities, "Sub-Authority Status");
            if (_showSubAuthorities)
            {
                EditorGUI.indentLevel++;
                DrawSubAuthoritiesStatus(authority);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(3);

            // Integration Guide
            _showIntegrationGuide = EditorGUILayout.BeginFoldoutHeaderGroup(_showIntegrationGuide, "Integration Guide");
            if (_showIntegrationGuide)
            {
                EditorGUI.indentLevel++;
                DrawIntegrationGuide();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (_statusBoxStyle == null)
            {
                _statusBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }
        }

        private void DrawInspectorHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("Local Network Authority", _headerStyle);
            EditorGUILayout.LabelField("Single-Player / Testing Implementation", EditorStyles.centeredGreyMiniLabel);
            
            EditorGUILayout.EndVertical();
        }

        private void DrawNetworkStatus(LocalNetworkAuthority authority)
        {
            bool isRegistered = false;
            try
            {
                isRegistered = NetworkAuthorityProvider.IsNetworkMode && 
                               ReferenceEquals(NetworkAuthorityProvider.SpawnAuthority, authority);
            }
            catch { }

            Color statusColor = isRegistered ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.8f, 0.2f);
            string statusText = isRegistered 
                ? "✓ REGISTERED - Active as network authority" 
                : "○ NOT REGISTERED - Will register on Start()";

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = statusColor;
            EditorGUILayout.BeginVertical(_statusBoxStyle);
            GUI.backgroundColor = prevColor;
            
            EditorGUILayout.LabelField(statusText, EditorStyles.wordWrappedLabel);
            
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField($"Network Time: {authority.NetworkTime:F2}s", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Is Server: {authority.IsServer} | Is Host: {authority.IsHost}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndVertical();
        }

        private void DrawConfiguration()
        {
            EditorGUILayout.PropertyField(_localPlayerId, new GUIContent("Local Player ID", 
                "Player ID for this client. In single-player, typically 0."));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Debug Settings", EditorStyles.miniBoldLabel);

            EditorGUILayout.PropertyField(_enableDebugLogging, new GUIContent("Enable Debug Logging", 
                "Log all network operations to Console for debugging."));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Network Simulation", EditorStyles.miniBoldLabel);

            EditorGUILayout.PropertyField(_simulateNetworkDelay, new GUIContent("Simulate Network Delay", 
                "Add artificial latency to simulate network conditions."));

            EditorGUI.BeginDisabledGroup(!_simulateNetworkDelay.boolValue);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_simulatedDelayMs, new GUIContent("Delay (ms)", 
                "Simulated round-trip time in milliseconds."));
            
            // Visual delay indicator
            int delay = _simulatedDelayMs.intValue;
            string qualityLabel;
            Color qualityColor;
            
            if (delay <= 50)
            {
                qualityLabel = "Excellent (LAN)";
                qualityColor = new Color(0.2f, 0.8f, 0.2f);
            }
            else if (delay <= 100)
            {
                qualityLabel = "Good (Nearby Server)";
                qualityColor = new Color(0.6f, 0.8f, 0.2f);
            }
            else if (delay <= 200)
            {
                qualityLabel = "Fair (Distant Server)";
                qualityColor = new Color(0.8f, 0.8f, 0.2f);
            }
            else
            {
                qualityLabel = "Poor (High Latency)";
                qualityColor = new Color(0.8f, 0.4f, 0.2f);
            }
            
            var prevColor = GUI.color;
            GUI.color = qualityColor;
            EditorGUILayout.LabelField($"  → {qualityLabel}", EditorStyles.miniLabel);
            GUI.color = prevColor;
            
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();
        }

        private void DrawRuntimeStats(LocalNetworkAuthority authority)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("Network IDs Generated:", EditorStyles.boldLabel);
            
            // We can't directly access private fields, but we can show what's available
            EditorGUILayout.LabelField($"Network Time: {authority.NetworkTime:F3}s", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"RTT: {authority.RoundTripTime * 1000:F0}ms", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Is Network Active: {authority.IsNetworkActive}", EditorStyles.miniLabel);
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Refresh button
            if (GUILayout.Button("Refresh Statistics"))
            {
                Repaint();
            }
        }

        private void DrawSubAuthoritiesStatus(LocalNetworkAuthority authority)
        {
            EditorGUILayout.HelpBox(
                "LocalNetworkAuthority provides implementations for all sub-authorities. " +
                "In a real multiplayer implementation, these would connect to your network solution.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            DrawSubAuthorityItem("Transform Authority", "LocalTransformAuthority", 
                "Handles position interpolation (no-op in single-player)");
            DrawSubAuthorityItem("State Authority", "LocalStateAuthority", 
                "Handles state batch sync (no-op in single-player)");
            DrawSubAuthorityItem("Fog Authority", "LocalFogAuthority", 
                "Handles fog visibility (all visible in single-player)");
            DrawSubAuthorityItem("Projectile Authority", "LocalProjectileAuthority", 
                "Handles projectile hit validation (always approved)");
        }

        private void DrawSubAuthorityItem(string name, string implName, string description)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField("✓", GUILayout.Width(20));
            EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField($"Implementation: {implName}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawIntegrationGuide()
        {
            EditorGUILayout.HelpBox(
                "LocalNetworkAuthority is a reference implementation for testing.\n\n" +
                "For real multiplayer, create your own class implementing INetworkAuthorityExtended " +
                "and register it with NetworkAuthorityProvider.SetAuthority().",
                MessageType.Info);

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Quick Start", EditorStyles.miniBoldLabel);
            
            EditorGUILayout.TextArea(
                "1. This component auto-registers on Start()\n" +
                "2. Enable 'Debug Logging' to see events in Console\n" +
                "3. All network operations are immediately approved\n" +
                "4. Use as a template for real network implementations",
                EditorStyles.helpBox);

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Implemented Interfaces", EditorStyles.miniBoldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("• INetworkSpawnAuthority", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("• INetworkDamageAuthority", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("• INetworkCommandAuthority", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("• INetworkAuthorityExtended (parent)", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Open Online Documentation"))
            {
                Application.OpenURL("https://arawn-software-publishing.gitbook.io/enemy-masses/");
            }
        }
    }
}
