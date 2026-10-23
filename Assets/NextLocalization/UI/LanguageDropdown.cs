using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Localization.UI
{
    [RequireComponent(typeof(TMP_Dropdown))]
    public class LanguageDropdown : MonoBehaviour
    {
        private TMP_Dropdown _dropdown;
        private bool _isUpdating;

        private void Awake()
        {
            _dropdown = GetComponent<TMP_Dropdown>();
        }

        private void OnEnable()
        {
            Loc.OnLanguageChanged += HandleLanguageChanged;
            _dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            PopulateDropdown();
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= HandleLanguageChanged;
            _dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
        }

        private void PopulateDropdown()
        {
            if (!Loc.IsInitialized)
            {
                Loc.Init();
            }

            _isUpdating = true;

            _dropdown.ClearOptions();
            
            var languages = Loc.AvailableLanguages;
            List<string> options = new List<string>(languages);
            _dropdown.AddOptions(options);

            UpdateSelectionFromLoc();

            _isUpdating = false;
        }

        private void HandleLanguageChanged()
        {
            if (_isUpdating) return;
            UpdateSelectionFromLoc();
        }

        private void UpdateSelectionFromLoc()
        {
            _isUpdating = true;

            string currentLang = Loc.CurrentLanguage;
            var languages = Loc.AvailableLanguages;

            for (int i = 0; i < languages.Length; i++)
            {
                if (languages[i] == currentLang)
                {
                    _dropdown.value = i;
                    break;
                }
            }

            _dropdown.RefreshShownValue();
            _isUpdating = false;
        }

        private void OnDropdownValueChanged(int index)
        {
            if (_isUpdating) return;

            var languages = Loc.AvailableLanguages;
            if (index >= 0 && index < languages.Length)
            {
                string selectedLang = languages[index];
                Loc.SetLanguage(selectedLang);
            }
        }
    }
}
