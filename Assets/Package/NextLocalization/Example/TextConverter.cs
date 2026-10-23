using TMPro;
using UnityEngine;

public class TextConverter : MonoBehaviour
{
    [SerializeField] private LocalizationKey titleKey;
    private TMP_Text titleText;

    private void Awake()
    {
        titleText = GetComponent<TMP_Text>();
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
        if (titleText != null)
        {
            titleText.text = Loc.Get(titleKey);
        }
    }
}