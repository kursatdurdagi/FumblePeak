#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  LOCALIZATION ENTRY INSPECTOR MOD SYSTEM
//
//  Usage:
//    1. To add a mod to this system, inherit from LocalizationEntryInspectorMod.
//    2. Call Register() with [InitializeOnLoad] and a static constructor.
//    3. Override the DrawFields() method to add UI to the Entry Inspector.
//
//  You don't need to touch LocalizationEditorWindow.cs.
//  Each project writes its own mod script — the core editor remains unchanged.
//
//  Example:
//    [InitializeOnLoad]
//    public class MyCustomMod : LocalizationEntryInspectorMod
//    {
//        static MyCustomMod() => LocalizationEntryInspectorMods.Register(new MyCustomMod());
//        public override string ModId => "my.custom.mod";
//        public override bool ShouldDraw(string sectionName, string fullKey)
//            => sectionName == "items";  // only visible in "items" section
//        public override void DrawFields(string sectionName, string fullKey) { /* GUI */ }
//    }
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for adding extra UI fields to the Localization Entry Inspector.
/// Subclasses inherit this class to write their own mods.
/// </summary>
public abstract class LocalizationEntryInspectorMod
{
    /// <summary>Unique id identifying the mod (e.g., "questions.metadata")</summary>
    public abstract string ModId { get; }

    /// <summary>
    /// Determines whether this mod should be drawn for the given section and key.
    /// E.g., if you want it to work only for the "questions" section:
    ///   return sectionName == "questions";
    /// </summary>
    /// <param name="sectionName">Name of the section (non-empty, e.g., "questions")</param>
    /// <param name="fullKey">Full key (e.g., "questions.question.1")</param>
    public abstract bool ShouldDraw(string sectionName, string fullKey);

    /// <summary>
    /// Draws extra fields using GUILayout.
    /// This method is called inside GUILayout.BeginArea(); the area size is automatic.
    /// </summary>
    /// <param name="sectionName">Name of the section</param>
    /// <param name="fullKey">Full key (e.g., "questions.question.1")</param>
    public abstract void DrawFields(string sectionName, string fullKey);

    /// <summary>
    /// Returns the diff lines of the changes made by this mod
    /// to be displayed in the review window.
    /// </summary>
    public virtual System.Collections.Generic.List<LocalizationSaveReviewWindow.DiffLine> GetDiffLines()
    {
        return null;
    }

    /// <summary>
    /// Discards all pending changes for this mod.
    /// </summary>
    public virtual void DiscardAllChanges()
    {
    }

    /// <summary>
    /// Draws section-level settings (folder-level).
    /// </summary>
    public virtual void DrawSectionFields(string sectionName)
    {
    }

    /// <summary>
    /// Draws buttons for this mod in the main toolbar of the Localization Editor.
    /// Buttons are drawn from right to left (rect.x must be modified).
    /// </summary>
    public virtual void DrawToolbarButtons(ref UnityEngine.Rect rect)
    {
    }
}


/// <summary>
/// Keeps track of all registered mods and is called by the Localization Editor Window.
/// </summary>
public static class LocalizationEntryInspectorMods
{
    private static readonly List<LocalizationEntryInspectorMod> _mods
        = new List<LocalizationEntryInspectorMod>();

    /// <summary>Registers a mod instance with the system.</summary>
    public static void Register(LocalizationEntryInspectorMod mod)
    {
        if (mod == null) return;
        // If a mod with the same ID already exists, update it (domain reload protection)
        int idx = _mods.FindIndex(m => m.ModId == mod.ModId);
        if (idx >= 0)
            _mods[idx] = mod;
        else
            _mods.Add(mod);
    }

    /// <summary>Unregisters a mod from the system.</summary>
    public static void Unregister(string modId)
        => _mods.RemoveAll(m => m.ModId == modId);

    // ─── Save Hook ───────────────────────────────────────────────────────────

    /// <summary>
    /// Invoked when the Save button in the Localization Editor is clicked.
    /// Mods can subscribe to this event to save their own data along with the main localization data.
    /// </summary>
    public static event System.Action OnCommitSave;

    /// <summary>
    /// Called by LocalizationEditorWindow.CommitSave().
    /// Triggers save callbacks for all registered mods.
    /// </summary>
    public static void NotifySave() => OnCommitSave?.Invoke();

    // ─── Rename Hook ─────────────────────────────────────────────────────────

    /// <summary>
    /// Invoked when one or more localization keys are renamed.
    /// Mods can subscribe to this event from a script in their own project
    /// to update their own data structures — no need to modify the package code.
    /// <para>Example (mod author writes this in their own project):</para>
    /// <code>
    /// [InitializeOnLoad]
    /// public static class MyRenameHook
    /// {
    ///     static MyRenameHook()
    ///         => LocalizationEntryInspectorMods.OnCommitRename += OnRename;
    ///
    ///     static void OnRename(List&lt;(string oldKey, string newKey)&gt; renames)
    ///     {
    ///         // Update your own data here
    ///     }
    /// }
    /// </code>
    /// </summary>
    public static event System.Action<System.Collections.Generic.List<(string oldKey, string newKey)>> OnCommitRename;

    /// <summary>
    /// Called by LocalizationEditorWindow.CommitSave() — rename hook.
    /// Triggers all registered subscribers (mods).
    /// </summary>
    public static void NotifyRename(System.Collections.Generic.List<(string oldKey, string newKey)> renames)
        => OnCommitRename?.Invoke(renames);

    // ─── Dirty ─────────────────────────────────────────────────────────

    private static bool _hasPendingChanges;

    /// <summary>
    /// Any mod has pending changes?
    /// LocalizationEditorWindow reads this property to activate the Save button.
    /// </summary>
    public static bool HasPendingChanges => _hasPendingChanges;

    /// <summary>
    /// A mod should fire this event when it makes changes.
    /// LocalizationEditorWindow subscribes to this to set its own _isDirty.
    /// </summary>
    public static event System.Action OnModDirtyChanged;

    /// <summary>
    /// Called by mods: indicates that there are pending changes.
    /// </summary>
    public static void MarkModDirty()
    {
        _hasPendingChanges = true;
        OnModDirtyChanged?.Invoke();
    }

    /// <summary>
    /// Resets mod dirty state after NotifySave().
    /// </summary>
    public static void ClearModDirty() => _hasPendingChanges = false;

    /// <summary>
    /// Called by LocalizationEditorWindow.DrawEntryInspector().
    /// Calls DrawFields() for all registered mods in order.
    /// </summary>
    /// <param name="entry">Selected localization key data</param>
    public static void DrawAll(EntryData entry)
    {
        if (_mods.Count == 0) return;

        string secName = entry.Parent?.Name?.Replace(" ", "") ?? "";
        string fullKey = secName + "." + entry.Key.Replace(" ", "");

        bool anyDrawn = false;
        foreach (var mod in _mods)
        {
            try
            {
                if (!mod.ShouldDraw(secName, fullKey)) continue;
                if (!anyDrawn)
                {
                    // Place a separator before drawing the first mod
                    EditorGUILayout.Space(4f);
                    DrawModSeparator();
                    anyDrawn = true;
                }
                mod.DrawFields(secName, fullKey);
            }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox(
                    $"[Mod Hatası] {mod.ModId}: {ex.Message}",
                    MessageType.Error);
            }
        }
    }

    /// <summary>
    /// Calls DrawSectionFields() for all registered mods.
    /// Used by LocalizationEditorWindow.DrawSectionInspector().
    /// </summary>
    public static void DrawAllSection(string sectionName)
    {
        if (_mods.Count == 0 || string.IsNullOrEmpty(sectionName)) return;

        string cleanName = sectionName.Replace(" ", "");
        bool anyDrawn = false;
        
        foreach (var mod in _mods)
        {
            try
            {
                // ShouldDraw() can be called with empty string as fullKey or just to check if the mod is active
                if (!anyDrawn)
                {
                    EditorGUILayout.Space(4f);
                    DrawModSeparator();
                    anyDrawn = true;
                }
                mod.DrawSectionFields(cleanName);
            }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox(
                    $"[Mod Hatası] {mod.ModId} DrawSectionFields: {ex.Message}",
                    MessageType.Error);
            }
        }
    }

    /// <summary>
    /// Calls DrawToolbarButtons() for all registered mods.
    /// Used by LocalizationEditorWindow when drawing the toolbar.
    /// </summary>
    public static void DrawAllToolbarButtons(ref UnityEngine.Rect rect)
    {
        if (_mods.Count == 0) return;
        foreach (var mod in _mods)
        {
            try
            {
                mod.DrawToolbarButtons(ref rect);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Mod Hatası] {mod.ModId} DrawToolbarButtons: {ex.Message}");
            }
        }
    }

    // ─── Utilities ───────────────────────────────────────────────────────────

    private static void DrawModSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(rect, new Color(0.35f, 0.55f, 0.75f, 0.6f));   // blue separator
    }

    /// <summary>
    /// Combines diff lines from all registered mods for the review window.
    /// </summary>
    public static List<LocalizationSaveReviewWindow.DiffLine> BuildDiffLines()
    {
        var result = new List<LocalizationSaveReviewWindow.DiffLine>();
        foreach (var mod in _mods)
        {
            try
            {
                var lines = mod.GetDiffLines();
                if (lines != null && lines.Count > 0)
                {
                    result.AddRange(lines);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Mod Hatası] {mod.ModId} GetDiffLines: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// Reverts pending changes in all registered mods.
    /// </summary>
    public static void DiscardAllChanges()
    {
        foreach (var mod in _mods)
        {
            try
            {
                mod.DiscardAllChanges();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Mod Hatası] {mod.ModId} DiscardAllChanges: {ex.Message}");
            }
        }
        ClearModDirty();
    }
}

#endif
