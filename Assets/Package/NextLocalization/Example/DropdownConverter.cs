using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Dropdown))]
public class DropdownConverter : MonoBehaviour
{
    [Tooltip("Dropdown içindeki her bir seçeneğin çevirisi için eklenecek lokalizasyon anahtarları (yukarıdan aşağı sırayla)")]
    [SerializeField] private List<LocalizationKey> optionKeys = new List<LocalizationKey>();
    
    private TMP_Dropdown dropdown;

    private void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += UpdateText;
        UpdateText();
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= UpdateText;
    }

    private void UpdateText()
    {
        if (dropdown == null) return;

        // optionKeys sayısına göre dropdown option listesini boyutlandır
        while (dropdown.options.Count < optionKeys.Count)
            dropdown.options.Add(new TMP_Dropdown.OptionData());

        while (dropdown.options.Count > optionKeys.Count)
            dropdown.options.RemoveAt(dropdown.options.Count - 1);

        // Her key için ilgili option'ı güncelle
        for (int i = 0; i < optionKeys.Count; i++)
        {
            if (optionKeys[i] != null && !string.IsNullOrEmpty(optionKeys[i].Key))
            {
                dropdown.options[i].text = Loc.Get(optionKeys[i]);
            }
        }

        // UI üzerinde görünen seçili metni de anında yenile
        dropdown.RefreshShownValue();
    }
}
