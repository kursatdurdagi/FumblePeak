using UnityEditor;
using UnityEngine;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(Runtime.CrowdDamageDealer))]
    [CanEditMultipleObjects]
    public class CrowdDamageDealerEditor : UnityEditor.Editor
    {
        private Runtime.CrowdDamageDealer _dealer;

        // ── Damage ──
        private SerializedProperty _baseDamage;
        private SerializedProperty _damageVariance;
        private SerializedProperty _isPlayerAttack;
        private SerializedProperty _allowBlock;
        private SerializedProperty _forceKill;

        // ── Critical ──
        private SerializedProperty _criticalChance;
        private SerializedProperty _criticalMultiplier;

        // ── Targeting ──
        private SerializedProperty _targetMode;
        private SerializedProperty _damageRadius;
        private SerializedProperty _damageOrigin;
        private SerializedProperty _maxTargets;

        // ── Faction ──
        private SerializedProperty _factionFilter;
        private SerializedProperty _factionList;

        // ── Cooldown ──
        private SerializedProperty _cooldown;

        // ── Debug ──
        private SerializedProperty _showGizmos;
        private SerializedProperty _gizmoColor;

        // ── Events ──
        private SerializedProperty _onDamageBegin;
        private SerializedProperty _onDamageEnd;
        private SerializedProperty _onAgentDamaged;
        private SerializedProperty _onAgentKilled;
        private SerializedProperty _onCriticalHit;
        private SerializedProperty _onTotalAgentsDamaged;
        private SerializedProperty _onTotalDamageDealt;
        private SerializedProperty _onNoTargetsFound;

        // ── Editor State ──
        private SerializedProperty _editorTab;

        private static readonly string[] TabLabels = { "Damage", "Targeting", "Faction", "Events" };
        private static readonly Color AccentColor = new(0.35f, 0.7f, 0.95f);

        private void OnEnable()
        {
            if (target == null) return;

            _dealer = (Runtime.CrowdDamageDealer)target;

            _baseDamage       = serializedObject.FindProperty("baseDamage");
            _damageVariance   = serializedObject.FindProperty("damageVariance");
            _isPlayerAttack   = serializedObject.FindProperty("isPlayerAttack");
            _allowBlock       = serializedObject.FindProperty("allowBlock");
            _forceKill        = serializedObject.FindProperty("forceKill");

            _criticalChance     = serializedObject.FindProperty("criticalChance");
            _criticalMultiplier = serializedObject.FindProperty("criticalMultiplier");

            _targetMode   = serializedObject.FindProperty("targetMode");
            _damageRadius = serializedObject.FindProperty("damageRadius");
            _damageOrigin = serializedObject.FindProperty("damageOrigin");
            _maxTargets   = serializedObject.FindProperty("maxTargets");

            _factionFilter = serializedObject.FindProperty("factionFilter");
            _factionList   = serializedObject.FindProperty("factionList");

            _cooldown = serializedObject.FindProperty("cooldown");

            _showGizmos = serializedObject.FindProperty("showGizmos");
            _gizmoColor = serializedObject.FindProperty("gizmoColor");

            _onDamageBegin        = serializedObject.FindProperty("OnDamageBegin");
            _onDamageEnd          = serializedObject.FindProperty("OnDamageEnd");
            _onAgentDamaged       = serializedObject.FindProperty("OnAgentDamaged");
            _onAgentKilled        = serializedObject.FindProperty("OnAgentKilled");
            _onCriticalHit        = serializedObject.FindProperty("OnCriticalHit");
            _onTotalAgentsDamaged = serializedObject.FindProperty("OnTotalAgentsDamaged");
            _onTotalDamageDealt   = serializedObject.FindProperty("OnTotalDamageDealt");
            _onNoTargetsFound     = serializedObject.FindProperty("OnNoTargetsFound");

            _editorTab = serializedObject.FindProperty("editorTab");
        }

        public override void OnInspectorGUI()
        {
            if (_dealer == null)
            {
                EditorGUILayout.HelpBox("Target object is missing or destroyed.", MessageType.Warning);
                return;
            }

            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(8);

            _editorTab.intValue = GUILayout.Toolbar(_editorTab.intValue, TabLabels);
            EditorGUILayout.Space(6);

            switch (_editorTab.intValue)
            {
                case 0: DrawDamageTab(); break;
                case 1: DrawTargetingTab(); break;
                case 2: DrawFactionTab(); break;
                case 3: DrawEventsTab(); break;
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(8);
                DrawRuntimeControls();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ════════════════════════════════════════════════════════════════
        // Header
        // ════════════════════════════════════════════════════════════════

        private new void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            GUI.color = AccentColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("Crowd Damage Dealer", titleStyle);

            var descStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                wordWrap = true
            };
            EditorGUILayout.LabelField(
                "Deals damage to EnemyCrowd agents. Call DealDamage() from events, animation events, or code.",
                descStyle);

            EditorGUILayout.EndVertical();
        }

        // ════════════════════════════════════════════════════════════════
        // Tab: Damage
        // ════════════════════════════════════════════════════════════════

        private void DrawDamageTab()
        {
            EditorGUILayout.LabelField("Damage", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_baseDamage);
            EditorGUILayout.PropertyField(_damageVariance);

            // Show calculated range
            if (_damageVariance.floatValue > 0f)
            {
                float min = Mathf.Max(0f, _baseDamage.floatValue - _damageVariance.floatValue);
                float max = _baseDamage.floatValue + _damageVariance.floatValue;
                EditorGUILayout.HelpBox($"Damage range: {min:F1} — {max:F1}", MessageType.None);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_isPlayerAttack);
            EditorGUILayout.PropertyField(_allowBlock);
            EditorGUILayout.PropertyField(_forceKill);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Critical Hits", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_criticalChance);
            if (_criticalChance.floatValue > 0f)
            {
                EditorGUILayout.PropertyField(_criticalMultiplier);
                float critDmg = _baseDamage.floatValue * _criticalMultiplier.floatValue;
                EditorGUILayout.HelpBox(
                    $"Critical: {_criticalChance.floatValue * 100f:F0}% chance for {critDmg:F1} damage ({_criticalMultiplier.floatValue:F1}x)",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Cooldown", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_cooldown);
            if (_cooldown.floatValue > 0f)
            {
                EditorGUILayout.HelpBox(
                    $"Can deal damage once every {_cooldown.floatValue:F2}s.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        // ════════════════════════════════════════════════════════════════
        // Tab: Targeting
        // ════════════════════════════════════════════════════════════════

        private void DrawTargetingTab()
        {
            EditorGUILayout.LabelField("Target Selection", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_targetMode, new GUIContent("Mode"));
            EditorGUILayout.PropertyField(_damageRadius, new GUIContent("Radius"));
            EditorGUILayout.PropertyField(_damageOrigin, new GUIContent("Origin"));

            if (_damageOrigin.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No origin assigned — uses this GameObject's position.", MessageType.Info);
            }

            if (_targetMode.enumValueIndex == (int)Runtime.CrowdDamageTargetMode.AllInRadius)
            {
                EditorGUILayout.PropertyField(_maxTargets, new GUIContent("Max Targets"));
                if (_maxTargets.intValue == 0)
                {
                    EditorGUILayout.HelpBox("0 = unlimited targets.", MessageType.None);
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Visual Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_showGizmos);
            if (_showGizmos.boolValue)
            {
                EditorGUILayout.PropertyField(_gizmoColor);
            }

            EditorGUILayout.EndVertical();
        }

        // ════════════════════════════════════════════════════════════════
        // Tab: Faction
        // ════════════════════════════════════════════════════════════════

        private void DrawFactionTab()
        {
            EditorGUILayout.LabelField("Faction Filtering", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_factionFilter, new GUIContent("Filter Mode"));

            var mode = (Runtime.CrowdDamageFactionFilter)_factionFilter.enumValueIndex;

            switch (mode)
            {
                case Runtime.CrowdDamageFactionFilter.AllFactions:
                    EditorGUILayout.HelpBox("All factions will be damaged regardless of relationship.", MessageType.Info);
                    break;

                case Runtime.CrowdDamageFactionFilter.HostileOnly:
                    EditorGUILayout.HelpBox(
                        "Only agents hostile to this object will be damaged.\n" +
                        "Uses ExternalFactionIdentifier on this or parent object. " +
                        "Falls back to global player relationship if none found.",
                        MessageType.Info);

                    if (_dealer != null && _dealer.GetComponentInParent<Runtime.Factions.ExternalFactionIdentifier>() == null)
                    {
                        EditorGUILayout.HelpBox(
                            "No ExternalFactionIdentifier found on this object or parents.\n" +
                            "At runtime, will fall back to the global player-agent relationship.",
                            MessageType.Warning);
                    }
                    break;

                case Runtime.CrowdDamageFactionFilter.OnlyListedFactions:
                    EditorGUILayout.PropertyField(_factionList, new GUIContent("Target Factions"), true);
                    if (_factionList.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "Only Listed Factions is selected but the list is empty. No agents will be damaged.",
                            MessageType.Warning);
                    }
                    break;

                case Runtime.CrowdDamageFactionFilter.ExcludeListedFactions:
                    EditorGUILayout.PropertyField(_factionList, new GUIContent("Excluded Factions"), true);
                    if (_factionList.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "Exclude list is empty — all factions will be damaged.",
                            MessageType.Info);
                    }
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        // ════════════════════════════════════════════════════════════════
        // Tab: Events
        // ════════════════════════════════════════════════════════════════

        private void DrawEventsTab()
        {
            EditorGUILayout.LabelField("Damage Events", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_onDamageBegin);
            EditorGUILayout.PropertyField(_onDamageEnd);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Per-Agent Events", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_onAgentDamaged, new GUIContent("On Agent Damaged (Int32: agentIndex)"));
            EditorGUILayout.PropertyField(_onAgentKilled, new GUIContent("On Agent Killed (Int32: agentIndex)"));
            EditorGUILayout.PropertyField(_onCriticalHit);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Summary Events", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_onTotalAgentsDamaged, new GUIContent("On Total Agents Damaged (Int32: count)"));
            EditorGUILayout.PropertyField(_onTotalDamageDealt, new GUIContent("On Total Damage Dealt (Float: amount)"));
            EditorGUILayout.PropertyField(_onNoTargetsFound);

            EditorGUILayout.EndVertical();
        }

        // ════════════════════════════════════════════════════════════════
        // Runtime Controls
        // ════════════════════════════════════════════════════════════════

        private void DrawRuntimeControls()
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_dealer.IsOnCooldown)
            {
                EditorGUILayout.LabelField($"Cooldown: {_dealer.CooldownRemaining:F2}s remaining");
                Repaint();
            }
            else
            {
                EditorGUILayout.LabelField("Ready to deal damage.");
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Deal Damage", GUILayout.Height(24)))
            {
                _dealer.DealDamage();
            }

            if (GUILayout.Button("Kill All In Radius", GUILayout.Height(24)))
            {
                _dealer.KillAllInRadius();
            }

            if (GUILayout.Button("Reset Cooldown", GUILayout.Height(24)))
            {
                _dealer.ResetCooldown();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }
}
