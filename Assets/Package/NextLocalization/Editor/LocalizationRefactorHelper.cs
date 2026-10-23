#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LocalizationKey rename refactor tool.
///
/// When the key or section name changes during the asset process,
/// updates all LocalizationKey references in the entire project.
///
/// Scanned types:
///   • ScriptableObject (.asset)
///   • Prefab           (.prefab)
///   • Scene            (.unity) — including closed scenes
/// </summary>
public static class LocalizationRefactorHelper
{
    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the given rename list throughout the project.
    /// Each item: (oldFullKey, newFullKey)  e.g. ("Menu.play", "Menu.start")
    /// </summary>
    public static void RunRefactor(List<(string oldKey, string newKey)> renames)
    {
        if (renames == null || renames.Count == 0) return;

        // 1 ── Collect all reference candidates
        var assetPaths  = CollectAssetPaths();
        var prefabPaths = CollectPrefabPaths();
        var scenePaths  = CollectScenePaths();

        int total = assetPaths.Count + prefabPaths.Count + scenePaths.Count;
        if (total == 0) return;

        // 2 ── Preview the number of references to update
        int preview = PreviewCount(assetPaths, prefabPaths, scenePaths, renames);

        if (preview == 0)
        {
            Debug.Log("[Localization Refactor] No LocalizationKey references found to update.");
            return;
        }

        // 3 ── User confirmation
        string renameList = string.Join("\n", renames.Select(r => $"  • {r.oldKey}  →  {r.newKey}"));
        bool ok = EditorUtility.DisplayDialog(
            "Localization Refactor",
            $"The following key renames were detected:\n{renameList}\n\n" +
            $"{preview} LocalizationKey references will be updated in the project.\n\n" +
            "Continue?",
            "Update", "Skip");

        if (!ok) return;

        // 4 ── Update
        int updated = 0;
        int processed = 0;

        try
        {
            // Assets & Prefabs — SerializedObject API
            foreach (var path in assetPaths.Concat(prefabPaths))
            {
                EditorUtility.DisplayProgressBar(
                    "Localization Refactor",
                    System.IO.Path.GetFileName(path),
                    (float)processed / total);

                updated += ProcessAssetAtPath(path, renames);
                processed++;
            }

            // Scenes
            foreach (var path in scenePaths)
            {
                EditorUtility.DisplayProgressBar(
                    "Localization Refactor",
                    System.IO.Path.GetFileName(path),
                    (float)processed / total);

                updated += ProcessScene(path, renames);
                processed++;
            }

            if (updated > 0)
                AssetDatabase.SaveAssets();

            Debug.Log($"[Localization Refactor] Completed — {updated} references updated.");
            EditorUtility.DisplayDialog(
                "Localization Refactor",
                $"{updated} LocalizationKey references successfully updated.",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ─── Collection ─────────────────────────────────────────────────────────

    private static List<string> CollectAssetPaths()
    {
        return AssetDatabase.FindAssets("t:ScriptableObject")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> CollectPrefabPaths()
    {
        return AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToList();
    }

    private static List<string> CollectScenePaths()
    {
        return AssetDatabase.FindAssets("t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ─── Preview (for confirmation dialog) ─────────────────────────────────

    private static int PreviewCount(
        List<string> assets,
        List<string> prefabs,
        List<string> scenes,
        List<(string oldKey, string newKey)> renames)
    {
        int count = 0;
        foreach (var path in assets.Concat(prefabs))
            count += CountInAsset(path, renames);
        foreach (var path in scenes)
            count += CountInScene(path, renames);
        return count;
    }

    private static int CountInAsset(string path, List<(string oldKey, string newKey)> renames)
    {
        var obj = AssetDatabase.LoadMainAssetAtPath(path);
        if (obj == null) return 0;

        var so = new SerializedObject(obj);
        return CountInSerializedObject(so, renames);
    }

    private static int CountInScene(string path, List<(string oldKey, string newKey)> renames)
    {
        // Is the scene already open?
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.path == path && open.isLoaded)
                return CountInLoadedScene(open, renames);
        }

        // Closed scene — load additively, count, close
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try   { return CountInLoadedScene(scene, renames); }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static int CountInLoadedScene(Scene scene, List<(string oldKey, string newKey)> renames)
    {
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
            count += CountInGameObject(root, renames);
        return count;
    }

    private static int CountInGameObject(GameObject go, List<(string oldKey, string newKey)> renames)
    {
        int count = 0;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            count += CountInSerializedObject(so, renames);
        }
        return count;
    }

    private static int CountInSerializedObject(SerializedObject so, List<(string oldKey, string newKey)> renames)
    {
        int count = 0;
        var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = it.propertyType != SerializedPropertyType.String;
            if (it.propertyType == SerializedPropertyType.String &&
                IsLocalizationKeyField(it) &&
                renames.Any(r => r.oldKey == it.stringValue))
            {
                count++;
            }
        }
        return count;
    }

    // ─── Processing ─────────────────────────────────────────────────────────

    private static int ProcessAssetAtPath(string path, List<(string oldKey, string newKey)> renames)
    {
        // Load all assets for ScriptableObject (including sub-assets)
        var objs = AssetDatabase.LoadAllAssetsAtPath(path);
        int updated = 0;

        foreach (var obj in objs)
        {
            if (obj == null) continue;
            var so = new SerializedObject(obj);
            if (ApplyRenames(so, renames) > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(obj);
                updated++;
            }
        }
        return updated;
    }

    private static int ProcessScene(string path, List<(string oldKey, string newKey)> renames)
    {
        // Is the scene already open?
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.path == path && open.isLoaded)
            {
                int c = ProcessLoadedScene(open, renames);
                if (c > 0) EditorSceneManager.MarkSceneDirty(open);
                return c;
            }
        }

        // Closed scene — load additively, update, save, close
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            int c = ProcessLoadedScene(scene, renames);
            if (c > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            return c;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static int ProcessLoadedScene(Scene scene, List<(string oldKey, string newKey)> renames)
    {
        int updated = 0;
        foreach (var root in scene.GetRootGameObjects())
            updated += ProcessGameObject(root, renames);
        return updated;
    }

    private static int ProcessGameObject(GameObject go, List<(string oldKey, string newKey)> renames)
    {
        int updated = 0;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            if (ApplyRenames(so, renames) > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mb);
                updated++;
            }
        }
        return updated;
    }

    // ─── Core: SerializedProperty traversal ─────────────────────────────────

    /// <summary>
    /// Scans all LocalizationKey.key fields within a SerializedObject and applies rename.
    /// Returns the number of fields changed.
    /// </summary>
    private static int ApplyRenames(SerializedObject so, List<(string oldKey, string newKey)> renames)
    {
        int changed = 0;
        var it = so.GetIterator();
        bool enter = true;

        while (it.Next(enter))
        {
            enter = it.propertyType != SerializedPropertyType.String;

            if (it.propertyType == SerializedPropertyType.String && IsLocalizationKeyField(it))
            {
                string cur = it.stringValue;
                foreach (var (oldKey, newKey) in renames)
                {
                    if (cur == oldKey)
                    {
                        it.stringValue = newKey;
                        changed++;
                        break;
                    }
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// Checks if a SerializedProperty is a LocalizationKey.key field.
    /// The LocalizationKey class holds a private string field named "key".
    /// The parent property type must be "LocalizationKey".
    /// </summary>
    private static bool IsLocalizationKeyField(SerializedProperty prop)
    {
        // Field name must be "key"
        if (prop.name != "key") return false;

        // Parent type name must be "LocalizationKey"
        var parent = prop.Copy();
        // Go back to serializedObject and find the parent
        // propertyPath: "someField.key" or "list.Array.data[0].key" etc.
        string path = prop.propertyPath;
        if (!path.EndsWith(".key")) return false;

        // Extract parent path
        string parentPath = path.Substring(0, path.Length - ".key".Length);
        // If it's an array element, clean the end
        // e.g. "optionKeys.Array.data[0]" → parentPath
        var parentProp = prop.serializedObject.FindProperty(parentPath);
        if (parentProp == null) return false;

        return parentProp.type == "LocalizationKey";
    }
}
#endif
