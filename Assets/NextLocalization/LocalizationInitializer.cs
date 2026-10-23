using UnityEngine;

/// <summary>
/// Initializes the localization system.
/// Add this component to the root of each scene (or a Bootstrap scene).
///
/// Keeps running across scene transitions with DontDestroyOnLoad,
/// so the system is always ready.
/// </summary>
public class LocalizationInitializer : MonoBehaviour
{
    [Tooltip("Default language code (e.g., en, tr, de). If empty, uses saved language.")]
    [SerializeField] private string defaultLanguage = "English";

    private static bool _created;

    void Awake()
    {
        // Singleton — only one instance even if added to multiple scenes
        if (_created)
        {
            Destroy(gameObject);
            return;
        }

        _created = true;
        DontDestroyOnLoad(gameObject);

        // Initialize the system
        if (!Loc.HasSavedLanguage && !string.IsNullOrEmpty(defaultLanguage))
            Loc.Init(defaultLanguage);
        else
            Loc.Init();
    }
}
