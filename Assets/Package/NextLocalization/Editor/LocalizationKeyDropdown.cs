#if UNITY_EDITOR

using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class LocalizationKeyDropdown : AdvancedDropdown
{
    private readonly LocalizationTable table;
    private readonly System.Action<string> onSelect;

    public LocalizationKeyDropdown(
        AdvancedDropdownState state,
        LocalizationTable table,
        System.Action<string> onSelect)
        : base(state)
    {
        this.table = table;
        this.onSelect = onSelect;

        minimumSize = new Vector2(300, 400);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem("Localization Keys");

        if (table == null)
            return root;

        foreach (var section in table.TableSheet)
        {
            var sectionItem = new AdvancedDropdownItem(section.SectionName);

            foreach (var entry in section.SectionSheet)
            {
                string fullKey =
                    section.SectionName.Replace(" ", "") +
                    "." +
                    entry.Key.Replace(" ", "");

                sectionItem.AddChild(new KeyItem(fullKey));
            }

            root.AddChild(sectionItem);
        }

        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is KeyItem keyItem)
        {
            onSelect?.Invoke(keyItem.Key);
        }
    }

    private class KeyItem : AdvancedDropdownItem
    {
        public string Key;

        public KeyItem(string key) : base(key)
        {
            Key = key;
        }
    }
}

#endif