using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Centralized localization system — ScriptableObject based.
///
/// Initialization:
///   Loc.Init();        // Loads saved language (default: first language)
///   Loc.Init("Turkish");
///
/// Get a translation:
///   string text = Loc.Get("menu.play");
///   string text = Loc.Get("game.round", 3);   // → "Round 3"
///
/// Change language:
///   Loc.SetLanguage("Turkish");
/// </summary>
public static class Loc
{
    // ─── Events ─────────────────────────────────────────────────
    public static event Action OnLanguageChanged;

    // ─── State ──────────────────────────────────────────────────
    private static Dictionary<string, string> _texts       = new();
    private static string                     _currentLang = "";
    private static bool                       _initialized;
    private static LocalizationTable          _table;

    public static string CurrentLanguage => _currentLang;
    public static bool   IsInitialized   => _initialized;

    private static string SaveFilePath =>
        Path.Combine(Application.persistentDataPath, "save.loc");

    public static bool HasSavedLanguage
    {
        get
        {
            try { return File.Exists(SaveFilePath); }
            catch { return false; }
        }
    }

    /// <summary>Available language names (LocalizationLanguage.LanguageName).</summary>
    public static string[] AvailableLanguages
    {
        get
        {
            EnsureTable();
            if (_table == null) return Array.Empty<string>();
            return _table.Languages
                .Where(l => l != null)
                .Select(l => l.LanguageName)
                .ToArray();
        }
    }

    // ─── Public API ─────────────────────────────────────────────

    /// <summary>Initializes the system. lang = null → loads saved or first language.</summary>
    public static void Init(string lang = null)
    {
        EnsureTable();
        _currentLang = lang ?? LoadSavedLanguage();
        Load(_currentLang);
        _initialized = true;
    }

    /// <summary>Changes the language and triggers OnLanguageChanged.</summary>
    public static void SetLanguage(string lang)
    {
        if (_currentLang == lang && _initialized) return;
        _currentLang = lang;
        SaveLanguage(lang);
        Load(lang);
        OnLanguageChanged?.Invoke();
    }

    /// <summary>Returns translation by key. If not found or null → "[key]"</summary>
    public static string Get(string key)
    {
        if (!_initialized)
            Init();

        return _texts.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val)
            ? val
            : $"[{key}]";
    }

    /// <summary>Formatted translation: Loc.Get("game.round", 3) → "Round 3"</summary>
    public static string Get(string key, params object[] args)
    {
        var t = Get(key);
        try   { return string.Format(t, args); }
        catch { return t; }
    }

    // ─── Internal ───────────────────────────────────────────────

    private static void EnsureTable()
    {
        if (_table != null) return;
        _table = Resources.Load<LocalizationTable>("Localization/LocalizationTable");
        if (_table == null)
            Debug.LogWarning("[Loc] LocalizationTable not found: Resources/Localization/LocalizationTable");
    }

    private static void Load(string lang)
    {
        _texts.Clear();
        EnsureTable();

        if (_table == null) return;

        var asset = _table.Languages.FirstOrDefault(l =>
            l != null &&
            l.LanguageName.Equals(lang, StringComparison.OrdinalIgnoreCase));

        if (asset == null)
        {
            Debug.LogWarning($"[Loc] '{lang}' language not found.");
            return;
        }

        foreach (var s in asset.Strings)
            if (!string.IsNullOrEmpty(s.Key))
                _texts[s.Key] = s.Value ?? "";

        Debug.Log($"[Loc] '{lang}' loaded — {_texts.Count} keys");
    }

    private static string LoadSavedLanguage()
    {
        try
        {
            if (File.Exists(SaveFilePath))
            {
                var saved = File.ReadAllText(SaveFilePath).Trim();
                if (!string.IsNullOrEmpty(saved)) return saved;
            }
        }
        catch { }

        var available = AvailableLanguages;
        return available.Length > 0 ? available[0] : "English";
    }

    private static void SaveLanguage(string lang)
    {
        try { File.WriteAllText(SaveFilePath, lang); }
        catch (Exception e) { Debug.LogWarning($"[Loc] save.loc couldn't be written: {e.Message}"); }
    }
}
