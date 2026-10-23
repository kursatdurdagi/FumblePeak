// ╔══════════════════════════════════════════════════════════════════════════╗
// ║                     ⚠  EXAMPLE CODE — DELETE ME!  ⚠                   ║
// ║                                                                          ║
// ║  This file explains only how to use the NextLocalization system.         ║
// ║  It was created to show you how it works.                                ║
// ║                                                                          ║
// ║  Before integrating into your project:                                   ║
// ║    1. Delete this file (ExampleLocalizationMod.cs).                      ║
// ║    2. Delete the Assets/NextLocalization/Example/ folder.                ║
// ║    3. Use only the following patterns in your own scripts.               ║
// ║                                                                          ║
// ║  HOW TO USE (quick summary):                                             ║
// ║    • Get text     → Loc.Get("section.key")                               ║
// ║    • Change lang  → Loc.SetLanguage("Turkish")                           ║
// ║    • Listen event → Loc.OnLanguageChanged += YourMethod;                 ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NextLocalization A comprehensive use case for the system.
///
/// Attach this component to a GameObject in the Inspector and
/// link the relevant UI references.
///
/// ⚠ THIS SCRIPT IS AN EXAMPLE — DELETE IT IN YOUR REAL PROJECT.
/// </summary>
public class ExampleLocalizationMod : MonoBehaviour
{
    // ─── Inspector References ──────────────────────────────────────────────

    [Header("─── Text Fields ───")]
    [Tooltip("TMP_Text component for the main heading")]
    [SerializeField] private TMP_Text titleText;

    [Tooltip("TMP_Text component for the sub description")]
    [SerializeField] private TMP_Text descriptionText;

    [Tooltip("TMP_Text component for formatted text like round / counter")]
    [SerializeField] private TMP_Text roundText;

    [Header("─── Buttons ─────────────────────────────────────────")]
    [Tooltip("'Play' button — text is localized")]
    [SerializeField] private Button playButton;

    [Tooltip("'Settings' button — text is localized")]
    [SerializeField] private Button settingsButton;

    [Tooltip("'Quit' button — text is localized")]
    [SerializeField] private Button quitButton;

    [Header("─── Language Switch ─────────────────")]
    [Tooltip("Switch to English")]
    [SerializeField] private Button englishButton;

    [Tooltip("Switch to German")]
    [SerializeField] private Button germanButton;

    // ─── Example Counter (for formatted text) ────────────────────────────────
    // In your real project, this value comes from game logic.
    private int _currentRound = 1;

    // ────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // ── Button click events ──────────────────────────────────────────
        // Ensure your buttons are assigned in the Inspector.
        // Null check: prevents errors if a reference is missing.

        if (playButton != null)
            playButton.onClick.AddListener(OnPlayClicked);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

        // Dil değiştirme butonları
        if (germanButton != null)
            germanButton.onClick.AddListener(() => Loc.SetLanguage("German"));

        if (englishButton != null)
            englishButton.onClick.AddListener(() => Loc.SetLanguage("English"));
    }

    private void OnEnable()
    {
        // ── Language changed event ────────────────────────────────────────
        // OnEnable / OnDisable pair: event is not listened when the object
        // is disabled, preventing memory leaks.
        Loc.OnLanguageChanged += RefreshAllTexts;

        // Refresh texts immediately when the screen opens
        RefreshAllTexts();
    }

    private void OnDisable()
    {
        // ── Remove event subscription ───────────────────────────────────────
        // DO NOT FORGET THIS LINE — otherwise the event will continue to
        // trigger even after the object is destroyed, causing
        // NullReferenceException.
        Loc.OnLanguageChanged -= RefreshAllTexts;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Text Update
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes all localized texts on the screen.
    /// Automatically called when the Loc.OnLanguageChanged event is triggered.
    /// </summary>
    private void RefreshAllTexts()
    {
        // ── Simple text retrieval ──────────────────────────────────────────
        // Loc.Get("key") → Returns the translation defined in LocalizationTable.
        // If the key is not found, it returns "[key]" (does not throw an exception).

        if (titleText != null)
            titleText.text = Loc.Get("menu.title");
            // Example: "menu.title" → TR: "Ana Menü"  |  EN: "Main Menu"

        if (descriptionText != null)
            descriptionText.text = Loc.Get("menu.description");
            // Example: "menu.description" → TR: "Bir seçenek belirleyin."  |  EN: "Select an option."

        // ── Formatted text retrieval ───────────────────────────────────────
        // Loc.Get("key", arg0, arg1, ...) → Places {0}, {1}, etc. using string.Format.
        // Value in Localization table: "Round {0}"  →  Loc.Get("game.round", 3) = "Round 3"
        if (roundText != null)
            roundText.text = Loc.Get("game.round", _currentRound);

        // ── Update button texts ──────────────────────────────────────────
        // TMP_Dropdown or the TMP_Text inside a Button is accessed using GetComponentInChildren.
        UpdateButtonLabel(playButton,     "menu.button.play");
        UpdateButtonLabel(settingsButton, "menu.button.settings");
        UpdateButtonLabel(quitButton,     "menu.button.quit");
    }

    /// <summary>
    /// Updates the TMP_Text label of a button with the localized key.
    /// </summary>
    /// <param name="button">The button to update</param>
    /// <param name="key">The key in the LocalizationTable</param>
    private void UpdateButtonLabel(Button button, string key)
    {
        if (button == null) return;

        // Find the first TMP_Text child of the button
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = Loc.Get(key);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Button Events
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the game. Add your own scene/game logic in your real project.
    /// </summary>
    private void OnPlayClicked()
    {
        // Example: log localized message
        Debug.Log($"[Example] Play clicked — Current language: {Loc.CurrentLanguage}");

        // TODO: Add your scene loading or game logic here
        // SceneManager.LoadScene("GameScene");
    }

    /// <summary>
    /// Opens the settings menu.
    /// </summary>
    private void OnSettingsClicked()
    {
        Debug.Log("[Example] Settings clicked");

        // Example of changing language programmatically:
        // Loc.SetLanguage("Turkish");
        // → This line automatically triggers Loc.OnLanguageChanged inside SetLanguage.
        // → RefreshAllTexts() is also called automatically (because it's subscribed to the event).
    }

    /// <summary>
    /// Exits the game.
    /// </summary>
    private void OnQuitClicked()
    {
        Debug.Log("[Example] Quit clicked");

        // Example of showing localized quit message:
        string quitMsg = Loc.Get("menu.quit_confirm");
        Debug.Log($"[Example] Quit confirmation text: {quitMsg}");

        // Application.Quit();   // ← Uncomment for actual quit
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Advanced Usage Examples (Commented Out)
    // ────────────────────────────────────────────────────────────────────────

    // ── Getting current language ───────────────────────────────────────────
    // string lang = Loc.CurrentLanguage;   // "Turkish" / "English" etc.

    // ── Listing all available languages ──────────────────────────────────────
    // string[] langs = Loc.AvailableLanguages;
    // foreach (var l in langs) Debug.Log(l);

    // ── Initializing system manually (usually done by LocalizationInitializer) ─────
    // Loc.Init();              // Loads saved language, otherwise uses the first language
    // Loc.Init("Turkish");     // Starts with a specific language

    // ── Checking if saved language exists ───────────────────────────────────
    // if (Loc.HasSavedLanguage) { ... }

    // ── What happens if key is not found? ───────────────────────────────────
    // Loc.Get("nonexistent.key")   →  "[nonexistent.key]"   (no exception, safe)

    #endregion
}
