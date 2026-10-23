using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores all localization strings for a single language.
/// Each LocalizationLanguage asset represents one language.
/// </summary>
[CreateAssetMenu(fileName = "Language", menuName = "Localization/Language Asset")]
public class LocalizationLanguage : ScriptableObject
{
    [Serializable]
    public sealed class LocalizationString
    {
        public int SectionId;
        public int EntryId;
        public string Key;
        public string Value;
    }

    public string LanguageName;
    public List<LocalizationString> Strings = new List<LocalizationString>();
}
