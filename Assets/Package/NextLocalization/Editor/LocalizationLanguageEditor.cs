#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LocalizationLanguage))]
public class LocalizationLanguageEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var lang = (LocalizationLanguage)target;

        // ── Başlık Kutusu ──────────────────────────────────────
        var headerRect = EditorGUILayout.GetControlRect(false, 36f);
        EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.36f, 0.56f));

        var iconContent = EditorGUIUtility.IconContent("PrefabVariant On Icon");
        Rect iconRect = new Rect(headerRect.x + 6, headerRect.y + 10, 16, 16);
        if (iconContent != null) GUI.Label(iconRect, iconContent);

        var headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 12,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = Color.white }
        };
        Rect titleRect = new Rect(iconRect.xMax + 4, headerRect.y + 4, headerRect.width - 36, 16);
        GUI.Label(titleRect, "LOCALIZATION LANGUAGE", headerStyle);

        var subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            normal   = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };
        Rect subRect = new Rect(titleRect.x, titleRect.yMax + 2, titleRect.width, 12);
        GUI.Label(subRect, "Edit this language in the Localization Editor window.", subStyle);

        EditorGUILayout.Space(4);

        // ── Info Box ───────────────────────────────────────────
        EditorGUILayout.HelpBox(
            "This asset stores localization strings for a specific language.\n" +
            "Open the Localization Editor to add, edit or remove entries.",
            MessageType.Info);

        EditorGUILayout.Space(4);

        // ── Summary ────────────────────────────────────────────
        using (new EditorGUI.DisabledGroupScope(true))
        {
            EditorGUILayout.TextField("Language Name", lang.LanguageName);
            EditorGUILayout.IntField("String Count", lang.Strings?.Count ?? 0);
        }

        EditorGUILayout.Space(6);

        // ── Open Editor Button ─────────────────────────────────
        if (GUILayout.Button("Open Localization Editor", GUILayout.Height(28)))
            LocalizationEditorWindow.Open();
    }
}
#endif
