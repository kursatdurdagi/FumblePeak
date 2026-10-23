using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FumblePeak
{
    /// <summary>
    /// NextLocalization (Loc) için kısa yol. Tüm oyun metinleri buradan geçer.
    /// İlk açılışta sistem diline göre Türkçe / İngilizce / Almanca seçilir.
    /// </summary>
    public static class L
    {
        public static readonly Dictionary<string, string> NativeNames = new Dictionary<string, string>
        {
            { "Turkish", "Türkçe" }, { "English", "English" }, { "German", "Deutsch" },
        };

        public static void EnsureInit()
        {
            if (Loc.IsInitialized) return;
            if (Loc.HasSavedLanguage) { Loc.Init(); return; }
            string lang = Application.systemLanguage switch
            {
                SystemLanguage.Turkish => "Turkish",
                SystemLanguage.German => "German",
                _ => "English",
            };
            if (Array.IndexOf(Loc.AvailableLanguages, lang) < 0) lang = null;
            Loc.Init(lang);
        }

        public static string T(string key) { EnsureInit(); return Loc.Get(key); }
        public static string T(string key, params object[] args) { EnsureInit(); return Loc.Get(key, args); }

        public static string Native(string lang) => NativeNames.TryGetValue(lang, out var n) ? n : lang;
    }

    /// <summary>
    /// UXML içinde metni "#anahtar" olarak yazılan tüm Label / Button / alan etiketlerini çevirir
    /// ve dil değişince otomatik günceller.
    /// </summary>
    public class UILocalizer : IDisposable
    {
        readonly Dictionary<TextElement, string> keys = new Dictionary<TextElement, string>();
        public event Action Changed;

        public UILocalizer(VisualElement root)
        {
            L.EnsureInit();
            Scan(root);
            Apply();
            Loc.OnLanguageChanged += OnLang;
        }

        void OnLang()
        {
            Apply();
            Changed?.Invoke();
        }

        public void Scan(VisualElement root)
        {
            root.Query<TextElement>().ForEach(t =>
            {
                if (!string.IsNullOrEmpty(t.text) && t.text.StartsWith("#")) keys[t] = t.text.Substring(1);
            });
        }

        /// <summary>Koddan oluşturulan bir elemana anahtar bağla.</summary>
        public void Bind(TextElement t, string key)
        {
            keys[t] = key;
            t.text = L.T(key);
        }

        public void Apply()
        {
            foreach (var kv in keys) kv.Key.text = L.T(kv.Value);
        }

        public void Dispose() => Loc.OnLanguageChanged -= OnLang;
    }
}
