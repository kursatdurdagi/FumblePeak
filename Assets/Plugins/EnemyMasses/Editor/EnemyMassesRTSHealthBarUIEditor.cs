using UnityEditor;
using UnityEngine;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(EnemyMassesRTSHealthBarUI))]
    public class EnemyMassesRTSHealthBarUIEditor : UnityEditor.Editor
    {
        private const string PlaceholderInfo =
            "Label placeholders:\n" +
            "- Enemy Crowd Label Format: {faction} = faction name, {type} = enemy type name, {index} = crowd index\n" +
            "- Spawn Group Label Format: {group} = spawn group id, {faction}/{type} = crowd names, {index} = crowd index (fallback: group id)\n" +
            "- Agent Label Format: {index} = agent index, {faction}/{type} = crowd names\n" +
            "- Combined/Selected labels are plain text (no placeholders).";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(BuildLabelUsageInfo(), MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(PlaceholderInfo, MessageType.Info);
        }

        private string BuildLabelUsageInfo()
        {
            var ui = target as EnemyMassesRTSHealthBarUI;
            if (ui == null) return "Label usage: Unable to resolve target.";

            string modeLabel;
            if (ui.displayMode == EnemyMassesRTSHealthBarUI.DisplayMode.IndividualAgents)
            {
                modeLabel = "IndividualAgents -> Agent Label Format";
            }
            else
            {
                switch (ui.averageGrouping)
                {
                    case EnemyMassesRTSHealthBarUI.AverageGrouping.Combined:
                        modeLabel = ui.displayScope == EnemyMassesRTSHealthBarUI.DisplayScope.SelectedAgentsOnly
                            ? "Average + Combined -> Selected Label"
                            : "Average + Combined -> Combined Label";
                        break;
                    case EnemyMassesRTSHealthBarUI.AverageGrouping.SpawnGroup:
                        modeLabel = "Average + SpawnGroup -> Spawn Group Label Format";
                        break;
                    case EnemyMassesRTSHealthBarUI.AverageGrouping.EnemyCrowd:
                    default:
                        modeLabel = "Average + EnemyCrowd -> Enemy Crowd Label Format";
                        break;
                }
            }

            return "Label usage (based on current settings):\n" +
                   "- " + modeLabel + "\n" +
                   "- Display Scope and Faction Filter change which agents are included, not which label format is used.";
        }
    }
}
