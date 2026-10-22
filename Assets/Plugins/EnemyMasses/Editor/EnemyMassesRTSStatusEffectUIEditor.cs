using UnityEditor;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(EnemyMassesRTSStatusEffectUI))]
    public class EnemyMassesRTSStatusEffectUIEditor : UnityEditor.Editor
    {
        private const string PlaceholderInfo =
            "Label placeholders:\n" +
            "- {effect} = status effect name\n" +
            "- {category} = Buff/Debuff/DamageOverTime/CrowdControl\n" +
            "- {stacks} = stack count (blank if hidden)\n" +
            "- {remaining} = remaining duration or Permanent\n" +
            "- {agent} = agent index\n" +
            "- {faction} = faction name\n" +
            "- {type} = enemy type name";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Status Effect UI spawns one entry per active effect. " +
                "Display Scope and Faction Filter control which agents are scanned. " +
                "Use Max Items to cap large selections.",
                MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(PlaceholderInfo, MessageType.Info);
        }
    }
}
