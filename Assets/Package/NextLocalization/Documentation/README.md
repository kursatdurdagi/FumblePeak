# NextLocalization
### ScriptableObject-Based Localization for Unity
**Publisher:** NextHaven Studio  
**Version:** 1.0.0  
**Unity:** 2021.3 LTS or higher  
**Rendering Pipelines:** Compatible with Built-In, URP, and HDRP

---

> [!IMPORTANT]
> **Before you start:** Delete the `Assets/NextLocalization/Example/` folder and all its contents once you've reviewed the examples. It is for demonstration only and should not ship with your game.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Installation](#2-installation)
3. [Quick Start (5 Minutes)](#3-quick-start-5-minutes)
4. [Localization Editor Window](#4-localization-editor-window)
5. [Runtime API — Loc](#5-runtime-api--loc)
6. [Scene Setup — LocalizationInitializer](#6-scene-setup--localizationinitializer)
7. [UI Components](#7-ui-components)
8. [LocalizationKey Field](#8-localizationkey-field)
9. [CSV Import & Export](#9-csv-import--export)
10. [Rename Refactoring](#10-rename-refactoring)
11. [Mod System](#11-mod-system)
12. [File & Folder Structure](#12-file--folder-structure)
13. [FAQ](#13-faq)
14. [Support](#14-support)

---

## 1. Overview

**NextLocalization** is a lightweight, code-friendly localization system built entirely on ScriptableObjects. It ships with a full-featured custom editor window, CSV import/export, automatic key-rename refactoring across your entire project, and an extensible mod system so you can add custom inspector fields without ever touching package code.

### Key Features

| Feature | Details |
|---|---|
| **ScriptableObject architecture** | All data lives in `.asset` files — VCS-friendly, no JSON parsing at runtime |
| **3-panel editor window** | Languages · Table Sheet · Inspector, all in one view |
| **Persistent language save** | Saves player's language choice to disk automatically |
| **`LocalizationKey` field** | Serializable field with an in-editor searchable key picker |
| **CSV round-trip** | Export all translations to CSV, edit in Excel/Sheets, import back |
| **Auto rename refactor** | Rename a key → all `LocalizationKey` references across every prefab, asset, and scene update automatically |
| **Mod system** | Add custom inspector UI panels via `LocalizationEntryInspectorMod` — no core file edits needed |
| **Zero-dependency** | No external packages required. TextMeshPro needed only for the optional UI helper components |

---

## 2. Installation

1. Import the package via the **Unity Package Manager** or drag the `NextLocalization` folder into your `Assets/` directory.
2. TextMeshPro is required for `TextConverter`, `DropdownConverter`, and `LanguageDropdown`. Install it via **Window → Package Manager → TextMeshPro** if not already present.
3. Open the Localization Editor from the Unity menu bar:
   ```
   NextHaven Studio → Localization Editor
   ```
4. The editor will automatically create the `LocalizationTable` asset at:
   ```
   Assets/Resources/Localization/LocalizationTable.asset
   ```

> **Note:** The `Resources/Localization/` path is required for runtime loading via `Resources.Load`.  
> Do not move `LocalizationTable.asset` out of a `Resources` folder.

---

## 3. Quick Start (5 Minutes)

### Step 1 — Add Languages

1. Open **NextHaven Studio → Localization Editor**.
2. In the **Languages** panel (left), click **+** to add a new entry (e.g. `English`).
3. Click **Create Asset** in the Inspector panel to generate the language's `.asset` file.
4. Repeat for each language (e.g. `Turkish`, `German`).

### Step 2 — Add Keys

1. In the **Table Sheet** panel (center), click **+** → **Add Section** (e.g. `menu`).
2. Right-click the section → **Add Key** (e.g. `play`, `quit`).
3. Keys are addressed as `section.key` at runtime — e.g. `"menu.play"`.

### Step 3 — Enter Translations

1. Select a **Language** in the Languages panel.
2. The **Inspector** (right) shows all sections and keys with editable text fields.
3. Type the translation for each key.

### Step 4 — Save

Click **Save Asset** in the toolbar.

### Step 5 — Initialize at Runtime

Add `LocalizationInitializer` to a root GameObject in your first scene:
```
Add Component → LocalizationInitializer
```
Set **Default Language** to `English`.

### Step 6 — Use in Code

```csharp
// Simple key lookup
string label = Loc.Get("menu.play");          // → "Play"

// Formatted lookup ({0} placeholder)
string round = Loc.Get("game.round", 3);      // → "Round 3"

// Change language
Loc.SetLanguage("Turkish");
```

---

## 4. Localization Editor Window

Open via **NextHaven Studio → Localization Editor**.

```
┌──────────────────┬──────────────────┬──────────────────────────────┐
│   LANGUAGES      │   TABLE SHEET    │   INSPECTOR                  │
│                  │                  │                              │
│  • English  ←sel │  ▼ menu          │  menu.play                   │
│  • Turkish       │    • play   ←sel │  ┌──────────────────────┐   │
│  • German        │    • quit        │  │ Play                 │   │
│  [+]  [-]        │  ▼ game          │  └──────────────────────┘   │
│                  │    • round       │                              │
└──────────────────┴──────────────────┴──────────────────────────────┘
                  © NextHaven Studio — All Rights Reserved
```

### Languages Panel

| Action | How |
|---|---|
| Add language | Click **+** |
| Delete language | Select → click **−** |
| Rename | Double-click the name |
| Assign existing asset | Drag a `LocalizationLanguage` asset into the **Asset** field |

### Table Sheet Panel

The center panel shows the **shared structure** — sections (folders) and keys — across all languages.

| Action | How |
|---|---|
| Add section | **+** → **Add Section** |
| Add key | Right-click section → **Add Key** |
| Rename | Double-click the item |
| Delete | Select → **Delete** key, or right-click → **Delete** |
| Reorder | Drag and drop |
| Move key to another section | Drag key onto the target section |

### Inspector Panel

| Selection | What is shown |
|---|---|
| Language | All sections and keys with editable text fields + search bar |
| Section | Section name + any registered mod fields |
| Key | Full key path + any registered mod fields |

### Toolbar

| Button | Description |
|---|---|
| **Review** | Diff window showing all unsaved changes |
| **Export Template** | CSV with keys only — for translators |
| **Export CSV** | Full CSV with all translations |
| **Import CSV** | Additive import from CSV |
| **Save Asset** | Commits all changes to disk. Turns green when dirty |

---

## 5. Runtime API — `Loc`

`Loc` is a static class — no instance or singleton reference needed.

### Initialization

```csharp
Loc.Init();              // Load saved language or first available
Loc.Init("Turkish");     // Load a specific language
```

> `LocalizationInitializer` calls this automatically. Manual calls are rarely needed.

### Getting Translations

```csharp
// Simple
string text = Loc.Get("menu.play");
// → "Play"            (key found)
// → "[menu.play]"     (key missing — never throws)

// Formatted  (localization value: "Round {0} of {1}")
string text = Loc.Get("game.round_of", 3, 10);
// → "Round 3 of 10"
```

### Changing Language

```csharp
Loc.SetLanguage("German");
// Loads the German asset, fires OnLanguageChanged, saves preference to disk
```

### Properties

```csharp
string   lang    = Loc.CurrentLanguage;     // "English" / "Turkish" / ...
string[] langs   = Loc.AvailableLanguages;  // all languages in the table
bool     ready   = Loc.IsInitialized;
bool     saved   = Loc.HasSavedLanguage;
```

### Language Changed Event

```csharp
// Subscribe in OnEnable
Loc.OnLanguageChanged += RefreshMyUI;

// Always unsubscribe in OnDisable — prevents NullReferenceException
Loc.OnLanguageChanged -= RefreshMyUI;
```

> **Warning:** Always pair `+=` in `OnEnable` with `-=` in `OnDisable`.

---

## 6. Scene Setup — `LocalizationInitializer`

| Property | Description |
|---|---|
| **Default Language** | Language loaded on first run (if no saved preference exists) |

- Acts as a **singleton** — duplicates across multiple scenes self-destruct.
- Calls `DontDestroyOnLoad` — persists across scene transitions.
- If the player already has a saved language, that takes priority over **Default Language**.

---

## 7. UI Components

### 7.1 TextConverter

Keeps a `TMP_Text` in sync with the current language automatically.

**Setup:** Add `TextConverter` to a GameObject with a `TMP_Text` component. Set **Title Key** in the Inspector.

### 7.2 DropdownConverter

Localizes the option labels of a `TMP_Dropdown`.

**Setup:** Add `DropdownConverter` to a GameObject with a `TMP_Dropdown`. Add one **Option Key** per dropdown option (top-to-bottom order).

### 7.3 LanguageDropdown

A self-configuring `TMP_Dropdown` that lists all available languages and switches the active language on selection.

**Setup:** Add `LanguageDropdown` to a GameObject with a `TMP_Dropdown`. No additional configuration needed.

---

## 8. LocalizationKey Field

A serializable field type that stores a key string and provides a searchable picker in the Inspector.

```csharp
public class MyUI : MonoBehaviour
{
    [SerializeField] private LocalizationKey myKey;

    private void Start()
    {
        // LocalizationKey implicitly converts to string
        string value = Loc.Get(myKey);
    }
}
```

In the Inspector:
- **Text field** — shows the current key value.
- **🔍 button** — opens a searchable dropdown listing all sections and keys.

> **Tip:** When you rename a key in the editor and save, all `LocalizationKey` references across prefabs, ScriptableObjects, and scenes are updated automatically.

---

## 9. CSV Import & Export

### Exported Format

```
Key,English,Turkish,German
menu.play,Play,Oyna,Spielen
menu.quit,Quit,Çık,Beenden
game.round,Round {0},Tur {0},Runde {0}
```

### Import Rules

- Import is **additive** — existing values are preserved unless the CSV cell contains a new value.
- Empty CSV cells are skipped; they do not overwrite existing translations.
- After importing, click **Save Asset** to commit.

> **Tip:** Use **Export Template** to give translators a file with only the keys and empty columns — they fill in their language column and send it back.

---

## 10. Rename Refactoring

When you rename a key or section in the editor and click **Save Asset**:

1. Changes are detected by comparing internal IDs (not names), so renames are never missed.
2. A confirmation dialog lists every detected rename.
3. All `.asset`, `.prefab`, and `.unity` files are scanned.
4. Every `LocalizationKey` referencing the old name is updated in place.
5. Modified assets are saved automatically.

> **Important:** The refactor opens closed scenes additively to update them. Make sure you have no unsaved scene work before clicking Save Asset when keys have been renamed.

---

## 11. Mod System

Extend the Localization Editor without modifying any package files.

### Creating a Mod

```csharp
using UnityEditor;

[InitializeOnLoad]
public class MyMod : LocalizationEntryInspectorMod
{
    public override string ModId => "my.mod";

    static MyMod() => Register(new MyMod());

    // Return true to draw this panel for the given key
    public override bool ShouldDraw(string sectionName, string fullKey)
        => sectionName == "dialogue";

    // Draw your IMGUI UI here
    public override void DrawFields(string sectionName, string fullKey)
    {
        EditorGUILayout.LabelField("Voice Clip", EditorStyles.boldLabel);
        // ...
    }
}
```

### Save Hook

```csharp
[InitializeOnLoad]
public static class MySaveHook
{
    static MySaveHook()
    {
        LocalizationEntryInspectorMods.OnCommitSave += () =>
        {
            // Save your own data alongside the localization asset
        };
    }
}
```

### Rename Hook

```csharp
LocalizationEntryInspectorMods.OnCommitRename += renames =>
{
    foreach (var (oldKey, newKey) in renames)
    {
        // Update your own data structures
    }
};
```

### Marking the Save Button Dirty

```csharp
// Call this whenever your mod has unsaved changes
// The Save Asset button will turn green to signal pending changes
LocalizationEntryInspectorMods.MarkModDirty();
```

---

## 12. File & Folder Structure

```
Assets/
└── NextLocalization/
    ├── Documentation/
    │   └── README.md                         ← This file
    │
    ├── Loc.cs                                ← Static runtime API
    ├── LocalizationInitializer.cs            ← Scene bootstrap component
    ├── LocalizationKey.cs                    ← Serializable key field
    ├── LocalizationLanguage.cs               ← Per-language ScriptableObject
    ├── LocalizationTable.cs                  ← Master table ScriptableObject
    │
    ├── UI/
    │   └── LanguageDropdown.cs               ← Language-switching dropdown
    │
    ├── Example/                              ← DELETE BEFORE SHIPPING
    │   ├── ExampleLocalizationMod.cs
    │   ├── TextConverter.cs
    │   └── DropdownConverter.cs
    │
    └── Editor/
        ├── LocalizationEditorWindow.cs       ← Main editor window
        ├── LocalizationEntryInspectorMod.cs  ← Mod system base + registry
        ├── LocalizationKeyDrawer.cs          ← Key field property drawer
        ├── LocalizationKeyDropdown.cs        ← Key picker dropdown widget
        ├── LocalizationLanguageEditor.cs     ← Language asset inspector
        └── LocalizationRefactorHelper.cs     ← Auto-rename refactoring

Assets/
└── Resources/
    └── Localization/
        ├── LocalizationTable.asset           ← Auto-created by the editor
        ├── (Language) English.asset
        ├── (Language) Turkish.asset
        └── (Language) German.asset
```

---

## 13. FAQ

**Q: Can I use this without TextMeshPro?**  
Yes. The `Loc` API, `LocalizationKey`, and the editor window have no TMP dependency. Only `TextConverter`, `DropdownConverter`, and `LanguageDropdown` require TMP.

---

**Q: What happens if a key is missing at runtime?**  
`Loc.Get("missing.key")` returns `"[missing.key]"`. It never throws an exception.

---

**Q: Can I add new keys without breaking existing language assets?**  
Yes. New keys are added to all language assets with empty values. Existing values are untouched.

---

**Q: Does the Editor folder get included in builds?**  
No. Unity automatically strips the `Editor/` folder from builds. Only the runtime scripts and your `Resources/Localization/` assets are included.

---

**Q: How is the player's language saved?**  
It is saved as plain text (`save.loc`) in `Application.persistentDataPath`. It contains only the language name string (e.g. `English`).

---

**Q: Can I call `Loc.Get()` before `Loc.Init()`?**  
Yes. `Loc.Get()` auto-initializes using the saved or first available language if called before initialization.

---

**Q: Does rename refactoring work on scenes that are not open?**  
Yes. The refactor helper opens closed scenes additively, applies the updates, saves, and closes them — no manual steps required.

---

**Q: Can multiple mods be registered simultaneously?**  
Yes. All registered mods are drawn in registration order in the Inspector panel. Each mod controls its own visibility via `ShouldDraw()`.

---

## 14. Support

- **Publisher:** NextHaven Studio  
- **Email:** nexthavenstudio@gmail.com 
- **Documentation :** https://docs.nexthavenstudio.com/docs/nextlocalization/

Please include your Unity version and a description of the issue when contacting support.

---

*© NextHaven Studio — All Rights Reserved*
