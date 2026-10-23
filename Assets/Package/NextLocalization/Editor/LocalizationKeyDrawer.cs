#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomPropertyDrawer(typeof(LocalizationKey))]
public class LocalizationKeyDrawer : PropertyDrawer
{
    private AdvancedDropdownState state;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var keyProp = property.FindPropertyRelative("key");

        Rect fieldRect = position;
        fieldRect.width -= 25;

        Rect buttonRect = position;
        buttonRect.x = fieldRect.xMax + 2;
        buttonRect.width = 23;

        EditorGUI.BeginProperty(position, label, property);

        EditorGUI.TextField(fieldRect, label, keyProp.stringValue);

        if (GUI.Button(buttonRect, EditorGUIUtility.IconContent("Search Icon"), EditorStyles.iconButton))
        {
            ShowDropdown(buttonRect, keyProp);
        }

        EditorGUI.EndProperty();
    }

    private void ShowDropdown(Rect rect, SerializedProperty keyProp)
    {
        var table = Resources.Load<LocalizationTable>("Localization/LocalizationTable");

        if (table == null)
        {
            Debug.LogWarning("LocalizationTable bulunamadı.");
            return;
        }

        state ??= new AdvancedDropdownState();

        var dropdown = new LocalizationKeyDropdown(
            state,
            table,
            selectedKey =>
            {
                keyProp.stringValue = selectedKey;
                keyProp.serializedObject.ApplyModifiedProperties();
            });

        dropdown.Show(rect);
    }
}

#endif