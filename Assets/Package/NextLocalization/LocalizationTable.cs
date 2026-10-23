using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Localization table's single section (folder) data.
/// </summary>
[Serializable]
public class TableSheetSection
{
    public int Id;
    public string SectionName;
    public List<SheetItem> SectionSheet = new List<SheetItem>();

    // Parameterless constructor — Called by Unity serialisation.
    // Random.Range is FORBIDDEN — doesn't work during serialization.
    public TableSheetSection() { }

    // Used when creating a new section from the editor.
    public TableSheetSection(string name)
    {
        Id          = NewId();
        SectionName = name;
    }

    /// <summary>Generates a unique ID without using Unity.Random.</summary>
    public static int NewId()
    {
        // Guid hash can be negative, use absolute value + offset for a safe ID
        int hash = Math.Abs(Guid.NewGuid().GetHashCode());
        return (hash % 89999999) + 10000000; // 8-digit, 10000000-99999999 range
    }
}

/// <summary>
/// A single key entry inside a section.
/// </summary>
[Serializable]
public class SheetItem
{
    public int Id;
    public string Key;

    // Parameterless constructor — for Unity serialization.
    public SheetItem() { }

    public SheetItem(string key)
    {
        Id  = TableSheetSection.NewId();
        Key = key;
    }
}

/// <summary>
/// Central ScriptableObject holding the entire localization structure.
/// TableSheet: section + key tree
/// Languages: list of language assets
/// </summary>
[CreateAssetMenu(fileName = "LocalizationTable", menuName = "Localization/Table")]
public class LocalizationTable : ScriptableObject
{
    [HideInInspector] public List<TableSheetSection> TableSheet = new List<TableSheetSection>();
    [HideInInspector] public List<LocalizationLanguage> Languages = new List<LocalizationLanguage>();
}
