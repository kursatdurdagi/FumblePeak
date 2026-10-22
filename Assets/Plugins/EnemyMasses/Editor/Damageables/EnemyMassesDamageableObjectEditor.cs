using UnityEditor;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Damageables;

namespace Arawn.EnemyMasses.Editor.Damageables
{
    [CustomEditor(typeof(EnemyMassesDamageableObject))]
    public class EnemyMassesDamageableObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw the default inspector (Health, Events fields)
            serializedObject.Update();

            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip the debug fields — we'll draw them manually
                if (iterator.name.StartsWith("_dbg") || iterator.name == "_debugFoldout")
                    continue;

                // Skip the script reference field as is convention
                if (iterator.name == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iterator, true);
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            serializedObject.ApplyModifiedProperties();

            // ── Runtime Debug section (Play mode only) ──
            if (!Application.isPlaying) return;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("═══ RUNTIME DEBUG ═══", EditorStyles.boldLabel);

            var obj = (EnemyMassesDamageableObject)target;

            using (new EditorGUI.DisabledScope(true))
            {
                float maxHp = serializedObject.FindProperty("maxHealth").floatValue;
                float pct = maxHp > 0f ? obj.CurrentHealth / maxHp * 100f : 0f;

                EditorGUILayout.FloatField("Current Health", obj.CurrentHealth);
                EditorGUILayout.FloatField("Max Health", maxHp);

                var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                EditorGUI.ProgressBar(rect, Mathf.Clamp01(obj.CurrentHealth / Mathf.Max(1f, maxHp)),
                    $"{pct:F1}%");

                EditorGUILayout.Toggle("Is Alive", obj.IsAlive);
            }

            // Force repaint while in Play mode so the health bar updates live
            if (Application.isPlaying)
                Repaint();
        }
    }
}
