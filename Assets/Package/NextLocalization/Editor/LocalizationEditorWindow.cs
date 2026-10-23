#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

// ═══════════════════════════════════════════════════════════
//  WINDOW DATA — runtime editing structures
// ═══════════════════════════════════════════════════════════

/// <summary>Shared section reference + width state</summary>
public class SectionData
{
    public int    Id;
    public string Name;
    public bool   IsExpanded;
    public List<EntryData> Items = new List<EntryData>();

    public SectionData()
    {
        Id   = TableSheetSection.NewId();
        Name = "New Section";
    }
    public SectionData(TableSheetSection src)
    {
        Id   = src.Id;
        Name = src.SectionName;
        foreach (var item in src.SectionSheet)
            Items.Add(new EntryData(item, this));
    }
}

/// <summary>Shared key reference</summary>
public class EntryData
{
    public int        Id;
    public string     Key;
    public SectionData Parent;

    public EntryData()
    {
        Id  = TableSheetSection.NewId();
        Key = "new.key";
    }
    public EntryData(SheetItem src, SectionData parent)
    {
        Id     = src.Id;
        Key    = src.Key;
        Parent = parent;
    }
}

/// <summary>Temporary configuration data for a language</summary>
public class TempLanguageData
{
    public string              LanguageName;
    public LocalizationLanguage Asset;
    public List<TempSection>   TableSheet = new List<TempSection>();
}

/// <summary>Temporary configuration data for a section in a language</summary>
public class TempSection
{
    public SectionData        Reference;
    public List<TempEntry>    Items = new List<TempEntry>();
    public int  Id   => Reference.Id;
    public string Name
    {
        get => Reference.Name;
        set => Reference.Name = value;
    }
    public bool IsExpanded
    {
        get => Reference.IsExpanded;
        set => Reference.IsExpanded = value;
    }
}

/// <summary>Temporary configuration data for a key in a language</summary>
public class TempEntry
{
    public EntryData   Reference;
    public string      Value;
    public bool        IsExpanded;
    public Vector2     Scroll;

    public int    Id     => Reference.Id;
    public string Key
    {
        get => Reference.Key;
        set => Reference.Key = value;
    }
    public SectionData Parent => Reference.Parent;
}

/// <summary>Window working data</summary>
public class LocalizationWindowData
{
    public List<TempLanguageData> Languages  = new List<TempLanguageData>();
    public List<SectionData>      TableSheet = new List<SectionData>();
    public int LanguageCount;
    public int SectionCount;
    public int EntryCount;
}

// ═══════════════════════════════════════════════════════════
//  TREE VIEW — LANGUAGES
// ═══════════════════════════════════════════════════════════

public class LanguagesTreeView : TreeView
{
    private const string k_Delete     = "Delete";
    private const string k_SoftDelete = "SoftDelete";

    public Action<TempLanguageData> OnSelect;
    public Action       OnChanged;

    private readonly LocalizationWindowData _data;

    internal class LangItem : TreeViewItem
    {
        public TempLanguageData Language;
        public LangItem(int id, TempLanguageData lang) : base(id, 0, lang.LanguageName)
            => Language = lang;
    }

    public LanguagesTreeView(TreeViewState state, LocalizationWindowData data) : base(state)
    {
        _data     = data;
        rowHeight = 20f;
        Reload();
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = 0, depth = -1 };
        int id   = 10000;
        foreach (var lang in _data.Languages)
            root.AddChild(new LangItem(id++, lang));
        if (root.children == null)
            root.children = new List<TreeViewItem>();
        SetupDepthsFromParentsAndChildren(root);
        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        var rect = args.rowRect;
        rect.xMin += GetContentIndent(args.item);
        var icon = EditorGUIUtility.IconContent("BuildSettings.Web.Small");
        Rect iconRect = new Rect(rect.x, rect.y + 2f, 16f, 16f);
        GUI.Label(iconRect, icon);
        Rect labelRect = new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height);
        EditorGUI.LabelField(labelRect, args.item.displayName);
    }

    // Header + "+" button rendered externally via OnGUI override
    public override void OnGUI(Rect rect)
    {
        // Header
        Rect headerRect = new Rect(rect.x, rect.y, rect.width, 20f);
        GUI.Box(headerRect, GUIContent.none, EditorStyles.toolbar);
        EditorGUI.LabelField(new Rect(headerRect.x + 4, headerRect.y, headerRect.width - 28, headerRect.height),
            "LANGUAGES", EditorStyles.miniBoldLabel);
        Rect addBtn = new Rect(headerRect.xMax - 22f, headerRect.y + 1f, 18f, 16f);
        if (GUI.Button(addBtn, "+", EditorStyles.miniButton))
        {
            _data.Languages.Add(new TempLanguageData
            {
                LanguageName = "New Language " + (++_data.LanguageCount),
                Asset        = null,
                TableSheet   = BuildLangSections()
            });
            OnChanged?.Invoke();
            Reload();
        }

        rect.yMin += 20f;
        HandleCommandEvent(Event.current);
        base.OnGUI(rect);
    }

    private List<TempSection> BuildLangSections()
    {
        var list = new List<TempSection>();
        foreach (var sec in _data.TableSheet)
        {
            var tempSec = new TempSection { Reference = sec };
            foreach (var item in sec.Items)
                tempSec.Items.Add(new TempEntry { Reference = item, Value = "" });
            list.Add(tempSec);
        }
        return list;
    }

    protected override bool CanRename(TreeViewItem item) => true;

    protected override void RenameEnded(RenameEndedArgs args)
    {
        if (!args.acceptedRename) return;
        var it = FindItem(args.itemID, rootItem) as LangItem;
        if (it == null) return;
        it.displayName             = args.newName;
        it.Language.LanguageName   = args.newName;
        OnChanged?.Invoke();
    }

    protected override void SingleClickedItem(int id)
    {
        var item = FindItem(id, rootItem) as LangItem;
        if (item != null) OnSelect?.Invoke(item.Language);
    }

    protected override void DoubleClickedItem(int id)
    {
        var item = FindItem(id, rootItem);
        if (item != null) BeginRename(item);
    }

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        if (selectedIds.Count != 1)
        {
            OnSelect?.Invoke(null);
            return;
        }
        var item = FindItem(selectedIds[0], rootItem) as LangItem;
        if (item != null) OnSelect?.Invoke(item.Language);
    }

    protected override bool CanMultiSelect(TreeViewItem item) => true;

    protected override void ContextClickedItem(int id)
    {
        var item = FindItem(id, rootItem) as LangItem;
        if (item == null) return;

        SetSelection(new[] { id });

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Delete Language"), false, () => 
        {
            DeleteSelected();
        });
        menu.ShowAsContext();
        Event.current.Use();
    }

    private void HandleCommandEvent(Event e)
    {
        if (e.type == EventType.ValidateCommand)
        {
            if ((e.commandName == k_Delete || e.commandName == k_SoftDelete) && HasSelection())
                e.Use();
        }
        else if (e.type == EventType.ExecuteCommand)
        {
            if (e.commandName == k_Delete || e.commandName == k_SoftDelete)
                DeleteSelected();
        }
    }

    private void DeleteSelected()
    {
        foreach (var id in GetSelection().OrderByDescending(i => i))
        {
            var item = FindItem(id, rootItem) as LangItem;
            if (item != null)
                _data.Languages.Remove(item.Language);
        }
        OnSelect?.Invoke(null);
        SetSelection(new int[0]);
        OnChanged?.Invoke();
        Reload();
    }

    protected override bool CanStartDrag(CanStartDragArgs args)
    {
        return true;
    }

    protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
    {
        DragAndDrop.PrepareStartDrag();
        DragAndDrop.SetGenericData("IDs",  args.draggedItemIDs.ToArray());
        DragAndDrop.SetGenericData("Type", "Languages");
        DragAndDrop.StartDrag("Languages");
    }

    protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
    {
        var ids  = DragAndDrop.GetGenericData("IDs")  as int[];
        var type = DragAndDrop.GetGenericData("Type") as string;
        if (type != "Languages" || ids == null) return DragAndDropVisualMode.Rejected;

        if (args.dragAndDropPosition == DragAndDropPosition.BetweenItems)
        {
            bool ok = false;
            foreach (var dragId in ids)
            {
                var drag = FindItem(dragId, rootItem) as LangItem;
                if (drag != null)
                {
                    if (args.performDrop) MoveLanguage(drag.Language, args.insertAtIndex);
                    ok = true;
                }
            }
            if (args.performDrop && ok) { Reload(); SetSelection(new int[0]); OnChanged?.Invoke(); }
            return ok ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
        }

        return DragAndDropVisualMode.Rejected;
    }

    private void MoveLanguage(TempLanguageData lang, int pos)
    {
        int old = _data.Languages.IndexOf(lang);
        if (old < 0) return;
        int insert = Mathf.Clamp(pos > old ? pos - 1 : pos, 0, _data.Languages.Count - 1);
        _data.Languages.RemoveAt(old);
        _data.Languages.Insert(insert, lang);
    }
}

// ═══════════════════════════════════════════════════════════
//  TREE VIEW — TABLE SHEET
// ═══════════════════════════════════════════════════════════

public class TableSheetTreeView : TreeView
{
    private const string k_Delete     = "Delete";
    private const string k_SoftDelete = "SoftDelete";

    public Action<SectionData, EntryData> OnSelect;
    public Action                         OnChanged;

    private readonly LocalizationWindowData _data;

    internal class SectionItem : TreeViewItem
    {
        public SectionData Section;
        public SectionItem(int id, SectionData sec) : base(id, 0, sec.Name)
            => Section = sec;
    }
    internal class EntryItem : TreeViewItem
    {
        public EntryData Entry;
        public EntryItem(int id, EntryData e) : base(id, 1, e.Key)
            => Entry = e;
    }

    public TableSheetTreeView(TreeViewState state, LocalizationWindowData data) : base(state)
    {
        _data     = data;
        rowHeight = 18f;
        Reload();
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = 0, depth = -1 };
        foreach (var sec in _data.TableSheet)
        {
            var secItem = new SectionItem(sec.Id, sec);
            foreach (var entry in sec.Items)
                secItem.AddChild(new EntryItem(entry.Id, entry));
            root.AddChild(secItem);
        }
        if (root.children == null)
            root.children = new List<TreeViewItem>();
        SetupDepthsFromParentsAndChildren(root);
        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        // Let Unity draw the built-in rename text field when renaming
        if (args.isRenaming) return;

        var rect = args.rowRect;
        rect.xMin += GetContentIndent(args.item);

        if (args.item is SectionItem sec)
        {
            var folderIcon = EditorGUIUtility.IconContent(
                IsExpanded(args.item.id) ? "FolderOpened Icon" : "Folder Icon");
            Rect iconRect  = new Rect(rect.x, rect.y + 1f, 16f, 16f);
            GUI.Label(iconRect, folderIcon);
            Rect labelRect = new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height);
            // Always read from displayName so rename updates immediately
            EditorGUI.LabelField(labelRect, args.item.displayName, EditorStyles.label);
        }
        else if (args.item is EntryItem)
        {
            var keyStyle = new GUIStyle(EditorStyles.label)
                { normal = { textColor = new Color(0.65f, 0.65f, 0.65f) } };
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, rect.height);
            EditorGUI.LabelField(labelRect, args.item.displayName, keyStyle);
        }
    }

    public override void OnGUI(Rect rect)
    {
        // Header
        Rect headerRect = new Rect(rect.x, rect.y, rect.width, 20f);
        GUI.Box(headerRect, GUIContent.none, EditorStyles.toolbar);
        EditorGUI.LabelField(new Rect(headerRect.x + 4, headerRect.y, headerRect.width - 28, headerRect.height),
            "TABLE SHEET", EditorStyles.miniBoldLabel);
        Rect addBtn = new Rect(headerRect.xMax - 22f, headerRect.y + 1f, 18f, 16f);
        if (GUI.Button(addBtn, "+", EditorStyles.miniButton))
            ShowAddMenu();

        rect.yMin += 20f;
        HandleCommandEvent(Event.current);
        base.OnGUI(rect);
    }

    private void ShowAddMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Add Section"), false, () =>
        {
            var sec = new SectionData();
            _data.TableSheet.Add(sec);
            _data.SectionCount++;
            // Add to every language
            foreach (var lang in _data.Languages)
                lang.TableSheet.Add(new TempSection { Reference = sec });
            OnChanged?.Invoke();
            Reload();
        });

        // If a section is selected, offer "Add Key"
        var sel = GetSelection();
        if (sel.Count > 0)
        {
            var item = FindItem(sel[0], rootItem);
            SectionItem secItem = item as SectionItem ?? item?.parent as SectionItem;
            if (secItem != null)
            {
                menu.AddItem(new GUIContent($"Add Key to '{secItem.Section.Name}'"), false, () =>
                {
                    AddEntryToSection(secItem.Section);
                });
            }
        }
        menu.ShowAsContext();
    }

    private void AddEntryToSection(SectionData sec)
    {
        var entry = new EntryData();
        entry.Parent = sec;
        sec.Items.Add(entry);
        _data.EntryCount++;

        foreach (var lang in _data.Languages)
        {
            var tempSec = lang.TableSheet.FirstOrDefault(s => s.Reference == sec);
            tempSec?.Items.Add(new TempEntry { Reference = entry, Value = "" });
        }
        OnChanged?.Invoke();
        Reload();
        // Expand parent section
        if (rootItem?.children != null)
        {
            var secTreeItem = rootItem.children.OfType<SectionItem>().FirstOrDefault(s => s.Section == sec);
            if (secTreeItem != null) SetExpanded(secTreeItem.id, true);
        }
    }

    protected override bool CanRename(TreeViewItem item) => true;

    protected override void RenameEnded(RenameEndedArgs args)
    {
        if (!args.acceptedRename) return;
        var it = FindItem(args.itemID, rootItem);
        if (it is SectionItem si)
        {
            si.displayName    = args.newName;
            si.Section.Name   = args.newName;
        }
        else if (it is EntryItem ei)
        {
            ei.displayName  = args.newName;
            ei.Entry.Key    = args.newName;
        }
        OnChanged?.Invoke();
    }

    protected override void SingleClickedItem(int id)
    {
        FireSelectFromId(id);
    }

    protected override void DoubleClickedItem(int id)
    {
        var item = FindItem(id, rootItem);
        if (item != null) BeginRename(item);
    }

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        if (selectedIds.Count != 1)
        {
            OnSelect?.Invoke(null, null);
            return;
        }
        FireSelectFromId(selectedIds[0]);
    }

    private void FireSelectFromId(int id)
    {
        var item = FindItem(id, rootItem);
        if (item is SectionItem si)
            OnSelect?.Invoke(si.Section, null);
        else if (item is EntryItem ei)
            OnSelect?.Invoke(null, ei.Entry);
    }


    protected override void ContextClickedItem(int id)
    {
        var item = FindItem(id, rootItem);
        var menu = new GenericMenu();

        if (item is SectionItem si)
        {
            menu.AddItem(new GUIContent("Add Key"), false, () => AddEntryToSection(si.Section));
            menu.AddItem(new GUIContent("Rename"),  false, () => BeginRename(item));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Section"), false, () =>
            {
                _data.TableSheet.Remove(si.Section);
                foreach (var lang in _data.Languages)
                {
                    var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == si.Section);
                    if (ts != null) lang.TableSheet.Remove(ts);
                }
                OnChanged?.Invoke();
                Reload();
            });
        }
        else if (item is EntryItem ei)
        {
            menu.AddItem(new GUIContent("Rename"), false, () => BeginRename(item));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Key"), false, () =>
            {
                var parentSec = ei.parent as SectionItem;
                if (parentSec == null) return;
                parentSec.Section.Items.Remove(ei.Entry);
                foreach (var lang in _data.Languages)
                {
                    var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == parentSec.Section);
                    var te = ts?.Items.FirstOrDefault(i => i.Reference == ei.Entry);
                    if (te != null) ts.Items.Remove(te);
                }
                OnChanged?.Invoke();
                Reload();
            });
        }

        menu.ShowAsContext();
        Event.current.Use();
    }

    protected override bool CanMultiSelect(TreeViewItem item) => true;

    /// <summary>Returns the single selected item ID, or -1 if none/multi.</summary>
    public int GetSelectedId()
    {
        var sel = GetSelection();
        return sel.Count == 1 ? sel[0] : -1;
    }

    /// <summary>Forces inspector sync from the current TreeView selection.</summary>
    public void SyncSelection()
    {
        int id = GetSelectedId();
        if (id < 0) { OnSelect?.Invoke(null, null); return; }
        FireSelectFromId(id);
    }

    protected override bool CanStartDrag(CanStartDragArgs args)
    {
        var first = FindItem(args.draggedItemIDs[0], rootItem);
        return args.draggedItemIDs.All(i => FindItem(i, rootItem).depth == first.depth);

    }

    protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
    {
        DragAndDrop.PrepareStartDrag();
        DragAndDrop.SetGenericData("IDs",  args.draggedItemIDs.ToArray());
        DragAndDrop.SetGenericData("Type", "TableSheet");
        DragAndDrop.StartDrag("TableSheet");
    }

    protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
    {
        var ids  = DragAndDrop.GetGenericData("IDs")  as int[];
        var type = DragAndDrop.GetGenericData("Type") as string;
        if (type != "TableSheet" || ids == null) return DragAndDropVisualMode.Rejected;

        if (args.dragAndDropPosition == DragAndDropPosition.BetweenItems)
        {
            bool ok = false;
            foreach (var dragId in ids)
            {
                var drag = FindItem(dragId, rootItem);
                if (drag is SectionItem si)
                {
                    if (args.performDrop) MoveSection(si.Section, args.insertAtIndex);
                    ok = true;
                }
                else if (drag is EntryItem ei)
                {
                    var parentSec = drag.parent as SectionItem;
                    var destSec   = args.parentItem as SectionItem;
                    if (args.performDrop && parentSec != null)
                    {
                        if (destSec == null || destSec == parentSec)
                            MoveEntryWithinSection(parentSec.Section, ei.Entry, args.insertAtIndex);
                        else
                            MoveEntryToSection(parentSec.Section, destSec.Section, ei.Entry);
                    }
                    ok = true;
                }
            }
            if (args.performDrop && ok) { Reload(); SetSelection(new int[0]); OnChanged?.Invoke(); }
            return ok ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
        }

        if (args.dragAndDropPosition == DragAndDropPosition.UponItem &&
            args.parentItem is SectionItem targetSec)
        {
            bool ok = false;
            foreach (var dragId in ids)
            {
                var drag = FindItem(dragId, rootItem);
                if (drag is EntryItem ei && drag.parent is SectionItem fromSec && fromSec != targetSec)
                {
                    if (args.performDrop)
                        MoveEntryToSection(fromSec.Section, targetSec.Section, ei.Entry);
                    ok = true;
                }
            }
            if (args.performDrop && ok) { Reload(); SetSelection(new int[0]); OnChanged?.Invoke(); }
            return ok ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
        }

        return DragAndDropVisualMode.Rejected;
    }

    private void MoveSection(SectionData sec, int pos)
    {
        int old = _data.TableSheet.IndexOf(sec);
        if (old < 0) return;
        int insert = Mathf.Clamp(pos > old ? pos - 1 : pos, 0, _data.TableSheet.Count - 1);
        _data.TableSheet.RemoveAt(old);
        _data.TableSheet.Insert(insert, sec);
        foreach (var lang in _data.Languages)
        {
            var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == sec);
            if (ts == null) continue;
            lang.TableSheet.Remove(ts);
            int li = Mathf.Clamp(insert, 0, lang.TableSheet.Count);
            lang.TableSheet.Insert(li, ts);
        }
    }

    private void MoveEntryWithinSection(SectionData sec, EntryData entry, int pos)
    {
        int old = sec.Items.IndexOf(entry);
        if (old < 0) return;
        int insert = Mathf.Clamp(pos > old ? pos - 1 : pos, 0, sec.Items.Count - 1);
        sec.Items.RemoveAt(old); sec.Items.Insert(insert, entry);
        foreach (var lang in _data.Languages)
        {
            var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == sec);
            if (ts == null) continue;
            var te = ts.Items.FirstOrDefault(i => i.Reference == entry);
            if (te == null) continue;
            ts.Items.Remove(te);
            int li = Mathf.Clamp(insert, 0, ts.Items.Count);
            ts.Items.Insert(li, te);
        }
    }

    private void MoveEntryToSection(SectionData from, SectionData to, EntryData entry)
    {
        from.Items.Remove(entry);
        entry.Parent = to;
        to.Items.Add(entry);
        foreach (var lang in _data.Languages)
        {
            var fromTs = lang.TableSheet.FirstOrDefault(s => s.Reference == from);
            var toTs   = lang.TableSheet.FirstOrDefault(s => s.Reference == to);
            if (fromTs == null || toTs == null) continue;
            var te = fromTs.Items.FirstOrDefault(i => i.Reference == entry);
            if (te == null) continue;
            fromTs.Items.Remove(te);
            toTs.Items.Add(te);
        }
    }

    private void HandleCommandEvent(Event e)
    {
        if (e.type == EventType.ValidateCommand)
        {
            if ((e.commandName == k_Delete || e.commandName == k_SoftDelete) && HasSelection())
                e.Use();
        }
        else if (e.type == EventType.ExecuteCommand)
        {
            if (e.commandName == k_Delete || e.commandName == k_SoftDelete)
                DeleteSelected();
        }
    }

    private void DeleteSelected()
    {
        foreach (var id in GetSelection().OrderByDescending(i => i))
        {
            var item = FindItem(id, rootItem);
            if (item is EntryItem ei)
            {
                if (!HasFocus()) continue;
                var parentSec = ei.parent as SectionItem;
                if (parentSec == null) continue;
                parentSec.Section.Items.Remove(ei.Entry);
                foreach (var lang in _data.Languages)
                {
                    var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == parentSec.Section);
                    var te = ts?.Items.FirstOrDefault(i => i.Reference == ei.Entry);
                    if (te != null) ts.Items.Remove(te);
                }
            }
            else if (item is SectionItem si)
            {
                if (!HasFocus()) continue;
                _data.TableSheet.Remove(si.Section);
                foreach (var lang in _data.Languages)
                {
                    var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == si.Section);
                    if (ts != null) lang.TableSheet.Remove(ts);
                }
            }
        }
        SetSelection(new int[0]);
        OnChanged?.Invoke();
        Reload();
    }
}

// ═══════════════════════════════════════════════════════════
//  MAIN EDITOR WINDOW
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Localization Editor — 3-panel, TreeView-based editor window.
///
/// Left   : LANGUAGES  (language list, add/delete, rename)
/// Middle : TABLE SHEET (section + key tree, drag-drop, rename)
/// Right  : INSPECTOR  (translations of selected language or section/key info)
///
/// Toolbar: Import OLD | Export CSV | Import CSV | Save Asset
/// </summary>
public class LocalizationEditorWindow : EditorWindow
{
    // ─── Constants ──────────────────────────────────────────────
    private const float k_LangWidth    = 180f;
    private const float k_SheetWidth   = 180f;
    private const string TABLE_PATH    = "Assets/Resources/Localization/LocalizationTable.asset";

    // ─── Data ───────────────────────────────────────────────────
    private LocalizationTable      _table;
    private LocalizationWindowData _windowData;

    // ─── Tree Views ─────────────────────────────────────────────
    [SerializeField] private TreeViewState _langState;
    [SerializeField] private TreeViewState _sheetState;
    private LanguagesTreeView   _langTree;
    private TableSheetTreeView  _sheetTree;

    // ─── Inspector State ────────────────────────────────────────
    // Selection types
    private int          _selectedLangIndex = -1;   // kept for legacy, unused
    private TempLanguageData _selectedLang;           // active language selection
    private SectionData  _selectedSection;            // != null → section selected
    private EntryData    _selectedEntry;              // != null → key selected

    private SearchField  _searchField;
    private string       _searchString = "";
    private Vector2      _scrollPos;
    private bool         _globalExpanded;

    // Inspector: mini bold foldout style
    private GUIStyle _miniFoldout;
    private GUIStyle _miniBtn;
    private bool     _stylesReady;
    private bool     _needsSheetReload;       // deferred tree reload flag
    private bool     _needsLangReload;        // deferred lang tree reload flag
    private int      _lastSheetSelId = -1;    // polling: last selected tree ID

    private bool   _isDirty;   // true after any change, reset after CommitSave

    // ─── Preserved state for Cancel-close ───────────────────────
    // When the user clicks X while dirty and then hits "Cancel" in the
    // dialog, OnDestroy has already run.  We stash the live data here
    // so the freshly-opened window can restore it instead of reloading
    // from disk (which would discard all unsaved work).
    private static LocalizationWindowData _preservedData;

    // ─── Menu ───────────────────────────────────────────────
    [MenuItem("NextHaven Studio/Localization Editor")]
    public static void Open()
    {
        var win = GetWindow<LocalizationEditorWindow>("Localization Editor");
        win.minSize = new Vector2(720, 450);
        win.Show();
    }

    // ─── Lifecycle ──────────────────────────────────────────────
    private void OnEnable()
    {
        _searchField = new SearchField();

        // If the user hit "Cancel" on a dirty-close dialog, restore the
        // preserved in-memory data instead of reloading from disk.
        if (_preservedData != null)
        {
            _table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(TABLE_PATH);
            if (_table == null)
            {
                var guids = AssetDatabase.FindAssets("t:LocalizationTable");
                if (guids.Length > 0)
                    _table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            _windowData    = _preservedData;
            _preservedData = null;
            _isDirty       = true;
            RebuildTreeViewsFromData();
        }
        else
        {
            LoadOrCreateTable();
        }
    }

    private void OnDestroy()
    {

        if (_isDirty)
        {
            // Stash live data BEFORE showing the dialog — if the user picks
            // "Cancel" OnEnable on the new window will restore it.
            _preservedData = _windowData;

            bool closeAnyway = EditorUtility.DisplayDialog(
                "Unsaved Changes",
                "Your changes will not be saved.\n\nClose anyway and discard all changes?",
                "Close Without Saving",
                "Cancel");

            if (!closeAnyway)
            {
                // User wants to stay → schedule reopen with preserved data
                EditorApplication.delayCall += () =>
                {
                    var win = GetWindow<LocalizationEditorWindow>("Localization Editor");
                    win.minSize = new Vector2(720, 450);
                    win.Show();
                };
            }
            else
            {
                // Truly discarding — don't restore
                _preservedData = null;
            }
        }
    }

    // ─── Styles ─────────────────────────────────────────────────
    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _miniFoldout = new GUIStyle(EditorStyles.foldout)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 11
        };

        _miniBtn = new GUIStyle(EditorStyles.miniButton)
        {
            font      = EditorStyles.miniBoldLabel.font,
            fontSize  = EditorStyles.miniBoldLabel.fontSize
        };

        _stylesReady = true;
    }

    // ─── OnGUI ──────────────────────────────────────────────────
    private void OnGUI()
    {
        EnsureStyles();

        if (_table == null)    { LoadOrCreateTable(); return; }
        if (_langTree == null) { RebuildTreeViews();  return; }

        // ── Toolbar ──────────────────────────────────────────
        Rect toolbarRect = new Rect(0, 0, position.width, 20f);
        GUI.Box(toolbarRect, GUIContent.none, EditorStyles.toolbar);

        // Toolbar buttons (right → left)
        float btnW       = 100f;
        float tplW       = 115f;
        float reviewW    =  80f;
        float btnGap     =   5f;
        float x          = toolbarRect.xMax;

        x -= btnW + btnGap;
        Rect saveBtn     = new Rect(x, 0, btnW, 20f);
        x -= btnW + btnGap;
        Rect importBtn   = new Rect(x, 0, btnW, 20f);
        x -= btnW + btnGap;
        Rect exportBtn   = new Rect(x, 0, btnW, 20f);
        x -= tplW + btnGap;
        Rect templateBtn = new Rect(x, 0, tplW, 20f);

        // Left side: Review
        float lx = 4f;
        Rect reviewBtn = new Rect(lx, 1f, reviewW, 18f);

        if (GUI.Button(reviewBtn, new GUIContent("Review", "Review pending changes without saving"), EditorStyles.miniButton))
            SaveAsset(true);

        if (GUI.Button(templateBtn, "Export Template", EditorStyles.toolbarButton)) ExportTemplate();
        if (GUI.Button(exportBtn,   "Export CSV",      EditorStyles.toolbarButton)) ExportCSV();
        if (GUI.Button(importBtn,   "Import CSV",      EditorStyles.toolbarButton)) ImportCSV();
        
        // Toolbar buttons from mods
        Rect modToolbarRect = new Rect(x, 0, 0, 20f);
        LocalizationEntryInspectorMods.DrawAllToolbarButtons(ref modToolbarRect);
        x = modToolbarRect.x;

        // Save Asset button: active if localization OR mod has changes
        bool anyDirty = _isDirty || LocalizationEntryInspectorMods.HasPendingChanges;
        using (new EditorGUI.DisabledGroupScope(!anyDirty))
        {
            var saveBg = GUI.backgroundColor;
            GUI.backgroundColor = anyDirty ? new Color(0.3f, 0.75f, 0.35f) : Color.white;
            if (GUI.Button(saveBtn, "Save Asset", EditorStyles.toolbarButton)) SaveAsset(false);
            GUI.backgroundColor = saveBg;
        }

        // ── Panels ───────────────────────────────────────────
        const float k_FooterH = 16f;
        float y = 25f;
        float h = position.height - y - k_FooterH - 5f;

        Rect langRect  = new Rect(5f,                         y, k_LangWidth,  h);
        Rect sheetRect = new Rect(langRect.xMax + 5f,         y, k_SheetWidth, h);
        Rect inspRect  = new Rect(sheetRect.xMax + 5f,        y,
                                  position.width - sheetRect.xMax - 10f, h);

        // ── Deferred tree reload ──────────────────────────────
        if (Event.current.type == EventType.Layout)
        {
            if (_needsSheetReload) { _needsSheetReload = false; _sheetTree?.Reload(); }
            if (_needsLangReload)  { _needsLangReload = false;  _langTree?.Reload(); }
        }

        // ── Draw bordered tree panels ─────────────────────────
        // Border is 1px larger on each side so the tree sits inside
        DrawBorder(new Rect(langRect.x  - 1, langRect.y  - 1, langRect.width  + 2, langRect.height + 2));
        DrawBorder(new Rect(sheetRect.x - 1, sheetRect.y - 1, sheetRect.width + 2, sheetRect.height + 2));

        _langTree.OnGUI(langRect);
        _sheetTree.OnGUI(sheetRect);

        // ── Polling-based inspector sync ──────────────────────
        // Ensures inspector always reflects the actual tree selection,
        // even when SelectionChanged callbacks miss (e.g., same item re-clicked).
        if (_sheetTree != null && Event.current.type == EventType.Repaint)
        {
            int curId = _sheetTree.GetSelectedId();
            if (curId != _lastSheetSelId)
            {
                _lastSheetSelId = curId;
                _sheetTree.SyncSelection();
            }
        }
        DrawInspector(inspRect);

        // ── Copyright Footer ──────────────────────────────────
        float footerY = position.height - k_FooterH;
        Rect separatorRect = new Rect(0, footerY, position.width, 1f);
        EditorGUI.DrawRect(separatorRect, new Color(0.15f, 0.15f, 0.15f, 1f));

        Rect footerRect = new Rect(0, footerY + 1f, position.width, k_FooterH - 1f);
        EditorGUI.DrawRect(footerRect, new Color(0.18f, 0.18f, 0.18f, 1f));

        var footerStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.45f, 0.45f, 0.45f) }
        };
        GUI.Label(footerRect, "© NextHaven Studio — All Rights Reserved", footerStyle);
    }


    // ═══════════════════════════════════════════════════════════
    //  INSPECTOR
    // ═══════════════════════════════════════════════════════════

    private void DrawInspector(Rect rect)
    {
        // Nothing selected
        if (_selectedLang == null && _selectedSection == null && _selectedEntry == null)
        {
            DrawBorderBox(rect);
            return;
        }

        if (_selectedLang != null)
        {
            DrawLanguageInspector(rect, _selectedLang);
        }
        else if (_selectedSection != null)
        {
            DrawSectionInspector(rect, _selectedSection);
        }
        else if (_selectedEntry != null)
        {
            DrawEntryInspector(rect, _selectedEntry);
        }
    }



    // ── Language Inspector ─────────────────────────────────────

    private void DrawLanguageInspector(Rect rect, TempLanguageData lang)
    {
        // Header with border
        Rect headerRect = DrawHeaderWithBorder(ref rect,
            EditorGUIUtility.TrTextContentWithIcon($" INSPECTOR ({lang.LanguageName})", "PrefabVariant On Icon"), 20f);

        GUILayout.BeginArea(rect);
        {
            // Name + rename button
            Rect nameCtrl = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            {
                Rect nameField = nameCtrl;
                nameField.xMax -= EditorGUIUtility.singleLineHeight + 2f;
                nameField = EditorGUI.PrefixLabel(nameField, new GUIContent("Name"));
                lang.LanguageName = EditorGUI.TextField(nameField, lang.LanguageName);
            }
            if (EditorGUI.EndChangeCheck())
            { _needsLangReload = true; _isDirty = true; Repaint(); }

            // Edit icon → rename asset
            Rect editBtnRect = new Rect(nameCtrl.xMax - EditorGUIUtility.singleLineHeight,
                                         nameCtrl.y,
                                         EditorGUIUtility.singleLineHeight,
                                         nameCtrl.height);
            using (new EditorGUI.DisabledGroupScope(lang.Asset == null))
            {
                if (GUI.Button(editBtnRect, EditorGUIUtility.IconContent("editicon.sml", "Rename Asset"), EditorStyles.iconButton))
                {
                    string path = AssetDatabase.GetAssetPath(lang.Asset);
                    if (!string.IsNullOrEmpty(path))
                        AssetDatabase.RenameAsset(path, "(Language) " + lang.LanguageName);
                }
            }

            // Asset field
            EditorGUI.BeginChangeCheck();
            lang.Asset = (LocalizationLanguage)EditorGUILayout.ObjectField(
                "Asset", lang.Asset, typeof(LocalizationLanguage), false);
            if (EditorGUI.EndChangeCheck() && lang.Asset != null)
                AssignLanguageAsset(lang);

            // Create asset button
            if (lang.Asset == null)
            {
                if (GUILayout.Button("Create Asset"))
                    CreateLanguageAsset(lang);
            }

            // ── Search + Expand ──
            EditorGUILayout.Space(3f);
            using (new EditorGUI.DisabledGroupScope(lang.Asset == null))
            {
                GUIContent expandText = new GUIContent("Expand");
                float expandW = _miniBtn.CalcSize(expandText).x;

                Rect searchRect = EditorGUILayout.GetControlRect();
                searchRect.xMax -= expandW + 2f;
                _searchString = _searchField.OnGUI(searchRect, _searchString);

                Rect expandRect = new Rect(searchRect.xMax + 2f, searchRect.y - 1f, expandW, searchRect.height);
                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.97f, 0.91f, 0.53f);
                if (GUI.Button(expandRect, expandText, _miniBtn))
                {
                    _globalExpanded = !_globalExpanded;
                    foreach (var sec in lang.TableSheet)
                        sec.IsExpanded = _globalExpanded;
                }
                GUI.backgroundColor = prevColor;

                // Localization scroll list
                if (lang.Asset != null)
                {
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
                    foreach (var sec in GetSearchResult(lang))
                        DrawLocalizationSection(sec);
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "To begin editing, assign a localization asset.", MessageType.Warning);
                }
            }
        }
        GUILayout.EndArea();
    }

    private void DrawLocalizationSection(TempSection sec)
    {
        if (sec.Items == null || sec.Items.Count == 0) return;

        // Border box
        var boxRect = EditorGUILayout.BeginVertical(GUI.skin.box);

        string secName = sec.Name.Replace(" ", "");
        sec.IsExpanded = EditorGUILayout.Foldout(sec.IsExpanded,
            new GUIContent(secName), true, _miniFoldout);

        if (sec.IsExpanded)
        {
            foreach (var item in sec.Items)
            {
                string keyName = item.Key.Replace(" ", "");
                string fullKey = secName + "." + keyName;
                bool multiline = item.Value != null && (item.Value.Contains("\n") || item.Value.Contains("\r"));
                if (multiline) fullKey += " (Multiline)";

                EditorGUILayout.BeginVertical(GUI.skin.box);
                {
                    item.IsExpanded = EditorGUILayout.Foldout(item.IsExpanded,
                        new GUIContent(fullKey), true, _miniFoldout);

                    if (item.IsExpanded)
                    {
                        float lineH = (EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight) * 10
                                    + EditorGUIUtility.standardVerticalSpacing;
                        item.Scroll = EditorGUILayout.BeginScrollView(item.Scroll, GUILayout.Height(lineH));
                        EditorGUI.BeginChangeCheck();
                        string newVal = EditorGUILayout.TextArea(item.Value ?? "", GUILayout.ExpandHeight(true));
                        if (EditorGUI.EndChangeCheck())
                        {
                            item.Value = newVal;
                            _isDirty = true; Repaint();
                        }
                        EditorGUILayout.EndScrollView();
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        string newVal = EditorGUILayout.TextField(item.Value ?? "");
                        if (EditorGUI.EndChangeCheck())
                        { item.Value = newVal; _isDirty = true; Repaint(); }
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(1f);
    }

    // ── Section Inspector ──────────────────────────────────────

    private void DrawSectionInspector(Rect rect, SectionData sec)
    {
        Rect hdr = DrawHeaderWithBorder(ref rect,
            EditorGUIUtility.TrTextContentWithIcon($" INSPECTOR ({sec.Name})", "PrefabVariant On Icon"), 20f);

        GUILayout.BeginArea(rect);
        {
            EditorGUI.BeginChangeCheck();
            sec.Name = EditorGUILayout.TextField("Name", sec.Name);
            if (EditorGUI.EndChangeCheck()) { _needsSheetReload = true; _isDirty = true; Repaint(); }

            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.IntField("Keys", sec.Items?.Count ?? 0);

            EditorGUILayout.Space(2f);
            DrawSeparator();
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.LabelField("Id: " + sec.Id, EditorStyles.miniBoldLabel);

            // ── Mod Hook (Section Düzeyi) ────────────────────────────────────
            LocalizationEntryInspectorMods.DrawAllSection(sec.Name);
            // ─────────────────────────────────────────────────────────────────
        }
        GUILayout.EndArea();
    }

    // ── Entry Inspector ───────────────────────────────────────

    private void DrawEntryInspector(Rect rect, EntryData entry)
    {
        Rect hdr = DrawHeaderWithBorder(ref rect,
            EditorGUIUtility.TrTextContentWithIcon($" INSPECTOR ({entry.Key})", "PrefabVariant On Icon"), 20f);

        GUILayout.BeginArea(rect);
        {
            EditorGUI.BeginChangeCheck();
            entry.Key = EditorGUILayout.TextField("Key", entry.Key);
            if (EditorGUI.EndChangeCheck()) { _needsSheetReload = true; _isDirty = true; Repaint(); }

            // ── Mod Hook ─────────────────────────────────────────────────────
            // Draws extra fields from all registered LocalizationEntryInspectorMods.
            // To add new fields, do not edit this file — write a separate mod script.
            LocalizationEntryInspectorMods.DrawAll(entry);
            // ─────────────────────────────────────────────────────────────────

            EditorGUILayout.Space(2f);
            DrawSeparator();
            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.LabelField("Parent: " + entry.Parent?.Name, EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Id: "     + entry.Id,           EditorStyles.miniBoldLabel);
            }
        }
        GUILayout.EndArea();
    }

    // ─── Search ─────────────────────────────────────────────────

    private IEnumerable<TempSection> GetSearchResult(TempLanguageData lang)
    {
        if (string.IsNullOrEmpty(_searchString))
            return lang.TableSheet;

        var result = new List<TempSection>();
        foreach (var sec in lang.TableSheet)
        {
            string secName = sec.Name.Replace(" ", "");
            var items = sec.Items
                .Where(i =>
                {
                    string k = secName + "." + i.Key.Replace(" ", "");
                    return k.IndexOf(_searchString, StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            result.Add(new TempSection
            {
                Reference = sec.Reference,
                Items     = items
            });
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  DATA BUILD / SAVE / LOAD
    // ═══════════════════════════════════════════════════════════

    private void RebuildTreeViews()
    {
        // Build window data from table
        _windowData = BuildWindowData(_table);

        _langState  ??= new TreeViewState();
        _sheetState ??= new TreeViewState();

        _langTree = new LanguagesTreeView(_langState, _windowData)
        {
            OnSelect  = lang =>
            {
                _selectedLang    = lang;
                _selectedSection = null;
                _selectedEntry   = null;
                _searchString    = "";
                Repaint();
            },
            OnChanged = () => { _isDirty = true; Repaint(); }
        };

        _sheetTree = new TableSheetTreeView(_sheetState, _windowData)
        {
            OnSelect  = (sec, entry) =>
            {
                // null,null means multi-select or deselect — clear inspector
                if (sec == null && entry == null)
                {
                    _selectedSection = null;
                    _selectedEntry   = null;
                }
                else
                {
                    _selectedSection = sec;
                    _selectedEntry   = entry;
                    _selectedLang    = null;
                }
                Repaint();
            },
            OnChanged = () => { _isDirty = true; Repaint(); }
        };
    }

    private static LocalizationWindowData BuildWindowData(LocalizationTable table)
    {
        var data = new LocalizationWindowData();

        // 1. Build shared section/entry structure
        foreach (var tbl in table.TableSheet)
        {
            var sec = new SectionData(tbl);
            data.TableSheet.Add(sec);
            data.SectionCount++;
            data.EntryCount += sec.Items.Count;
        }

        // 2. Build per-language data
        foreach (var langAsset in table.Languages)
        {
            var langData = new TempLanguageData
            {
                LanguageName = langAsset != null ? langAsset.LanguageName : "Unknown",
                Asset        = langAsset
            };

            foreach (var sec in data.TableSheet)
            {
                var tempSec = new TempSection { Reference = sec };

                foreach (var entry in sec.Items)
                {
                    string val = "";
                    if (langAsset != null)
                    {
                        var match = langAsset.Strings.FirstOrDefault(
                            s => s.SectionId == sec.Id && s.EntryId == entry.Id);
                        if (match != null) val = match.Value;
                    }
                    tempSec.Items.Add(new TempEntry { Reference = entry, Value = val });
                }

                langData.TableSheet.Add(tempSec);
            }

            data.Languages.Add(langData);
            data.LanguageCount++;
        }

        return data;
    }

    private void SaveAsset(bool reviewOnly = false)
    {
        if (_table == null || _windowData == null) return;

        var lines = BuildDiffLines(_table, _windowData);

        bool hasChanges = lines.Any(l => 
            l.LineKind == LocalizationSaveReviewWindow.DiffLine.Kind.Added ||
            l.LineKind == LocalizationSaveReviewWindow.DiffLine.Kind.Removed ||
            l.LineKind == LocalizationSaveReviewWindow.DiffLine.Kind.Modified);

        if (!hasChanges)
        {
            reviewOnly = true;
        }

        LocalizationSaveReviewWindow reviewWindow = null;
        System.Action refreshAction = () => 
        {
            GUIUtility.keyboardControl = 0; // Clear text field focus so names update immediately
            _needsSheetReload = true;
            _needsLangReload = true;
            
            var newLines = BuildDiffLines(_table, _windowData);
            bool stillHasChanges = newLines.Any(l => 
                l.LineKind == LocalizationSaveReviewWindow.DiffLine.Kind.Added ||
                l.LineKind == LocalizationSaveReviewWindow.DiffLine.Kind.Removed ||
                l.LineKind == LocalizationSaveReviewWindow.DiffLine.Kind.Modified);
            
            if (!stillHasChanges)
            {
                _isDirty = false;
            }

            if (reviewWindow != null) reviewWindow.RefreshData(newLines);
            Repaint();
        };

        // Open the review window — Cancel just closes it, Save commits
        reviewWindow = LocalizationSaveReviewWindow.Show(lines, () => CommitSave(), reviewOnly, refreshAction, () => DiscardAllChanges());
    }

    private void DiscardAllChanges()
    {
        _isDirty = false;
        RebuildTreeViews(); // Core changes reverted
        LocalizationEntryInspectorMods.DiscardAllChanges(); // Mod changes reverted
        Repaint();
        Debug.Log("[LocalizationEditor] Bütün değişiklikler geri alındı (Discard All).");
    }

    // Called by the review window when the user confirms
    private void CommitSave()
    {
        if (_table == null || _windowData == null) return;

        // 0. Save before detecting renames
        var renames = ExtractRenames(_table, _windowData);

        // 1. Save TableSheet structure
        _table.TableSheet.Clear();
        foreach (var sec in _windowData.TableSheet)
        {
            var tbl = new TableSheetSection { Id = sec.Id, SectionName = sec.Name };
            foreach (var entry in sec.Items)
                tbl.SectionSheet.Add(new SheetItem { Id = entry.Id, Key = entry.Key });
            _table.TableSheet.Add(tbl);
        }

        // 2. Save each language asset
        var oldLangs = new List<LocalizationLanguage>(_table.Languages);
        _table.Languages.Clear();
        foreach (var lang in _windowData.Languages)
        {
            if (lang.Asset == null) continue;

            lang.Asset.LanguageName = lang.LanguageName;
            lang.Asset.Strings.Clear();

            foreach (var sec in lang.TableSheet)
            {
                foreach (var item in sec.Items)
                {
                    lang.Asset.Strings.Add(new LocalizationLanguage.LocalizationString
                    {
                        SectionId = sec.Id,
                        EntryId   = item.Id,
                        Key       = sec.Name.Replace(" ","") + "." + item.Key.Replace(" ",""),
                        Value     = item.Value ?? ""
                    });
                }
            }

            EditorUtility.SetDirty(lang.Asset);
            _table.Languages.Add(lang.Asset);
        }

        // 2b. Delete orphaned languages
        foreach (var oldLang in oldLangs)
        {
            if (!_table.Languages.Contains(oldLang))
            {
                string path = AssetDatabase.GetAssetPath(oldLang);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        EditorUtility.SetDirty(_table);
        AssetDatabase.SaveAssets();

        _isDirty = false;

        Debug.Log("[Localization Editor] Asset saved.");

        // 3. If rename exists, update all project references
        if (renames.Count > 0)
        {
            LocalizationRefactorHelper.RunRefactor(renames);
            // 3b. Rename hook — mods subscribe to OnCommitRename event
            //     to update their own data in their projects.
            //     No code needs to be written in the package.
            LocalizationEntryInspectorMods.NotifyRename(renames);
        }

        // 4. Trigger save hooks of registered mods (QuestionMetadataMod etc.)
        LocalizationEntryInspectorMods.NotifySave();
        LocalizationEntryInspectorMods.ClearModDirty();
    }

    /// <summary>
    /// Compares the current asset with the unsaved window data;
    /// returns pairs of changed keys (oldFullKey, newFullKey).
    /// If a section is renamed, all keys in that section are added to the list.
    /// </summary>
    private static List<(string oldKey, string newKey)> ExtractRenames(
        LocalizationTable table, LocalizationWindowData data)
    {
        var result = new List<(string, string)>();

        foreach (var newSec in data.TableSheet)
        {
            // Find old section by ID
            var oldSec = table.TableSheet.FirstOrDefault(s => s.Id == newSec.Id);
            if (oldSec == null) continue; // new section — not a rename

            string oldSecName = oldSec.SectionName.Replace(" ", "");
            string newSecName = newSec.Name.Replace(" ", "");
            bool sectionRenamed = oldSecName != newSecName;

            foreach (var newEntry in newSec.Items)
            {
                var oldEntry = oldSec.SectionSheet.FirstOrDefault(e => e.Id == newEntry.Id);
                if (oldEntry == null) continue; // new key — not a rename

                string oldEntryKey = oldEntry.Key.Replace(" ", "");
                string newEntryKey = newEntry.Key.Replace(" ", "");

                string oldFull = oldSecName + "." + oldEntryKey;
                string newFull = newSecName + "." + newEntryKey;

                if (oldFull != newFull)
                    result.Add((oldFull, newFull));
            }
        }

        return result;
    }

    /// <summary>
    /// Rebuilds only the TreeView controllers from the current _windowData without
    /// reloading from disk.  Used after RestoreSnapshot and after a Cancel-reopen.
    /// </summary>
    private void RebuildTreeViewsFromData()
    {
        _langState  ??= new TreeViewState();
        _sheetState ??= new TreeViewState();

        _langTree = new LanguagesTreeView(_langState, _windowData)
        {
            OnSelect  = lang =>
            {
                _selectedLang    = lang;
                _selectedSection = null;
                _selectedEntry   = null;
                _searchString    = "";
                Repaint();
            },
            OnChanged = () => { _isDirty = true; Repaint(); }
        };

        _sheetTree = new TableSheetTreeView(_sheetState, _windowData)
        {
            OnSelect  = (sec, entry) =>
            {
                if (sec == null && entry == null)
                {
                    _selectedSection = null;
                    _selectedEntry   = null;
                }
                else
                {
                    _selectedSection = sec;
                    _selectedEntry   = entry;
                    _selectedLang    = null;
                }
                Repaint();
            },
            OnChanged = () => { _isDirty = true; Repaint(); }
        };
    }

    // ── Diff builder ─────────────────────────────────────────────
    public static List<LocalizationSaveReviewWindow.DiffLine> BuildDiffLines(
        LocalizationTable table, LocalizationWindowData data)
    {
        var lines = new List<LocalizationSaveReviewWindow.DiffLine>();

        var oldSecs = table.TableSheet;
        var newSecs = data.TableSheet;

        // ── Sections & Keys ──────────────────────────────────────
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, "STRUCTURE  (Sections & Keys)"));

        bool structChange = false;

        var commonOldSecs = oldSecs.Where(os => newSecs.Any(ns => ns.Id == os.Id)).ToList();
        var commonNewSecs = newSecs.Where(ns => oldSecs.Any(os => os.Id == ns.Id)).ToList();

        foreach (var ns in newSecs)
        {
            var capturedNs = ns;
            var os = oldSecs.FirstOrDefault(s => s.Id == ns.Id);
            if (os == null)
            {
                System.Action discardAction = () => 
                {
                    newSecs.Remove(capturedNs);
                    foreach (var lang in data.Languages)
                        lang.TableSheet.RemoveAll(x => x.Id == capturedNs.Id);
                };

                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Added, $"+ [Section]  {ns.Name}", discardAction));
                foreach (var ni in ns.Items)
                    lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                        LocalizationSaveReviewWindow.DiffLine.Kind.Added, $"    + {ns.Name}.{ni.Key}", discardAction));
                structChange = true;
                continue;
            }
            if (os.SectionName != ns.Name)
            {
                var capturedOs = os;
                System.Action discardAction = () => 
                {
                    capturedNs.Name = capturedOs.SectionName;
                    foreach (var lang in data.Languages)
                    {
                        var ts = lang.TableSheet.FirstOrDefault(x => x.Id == capturedNs.Id);
                        if (ts != null) ts.Name = capturedOs.SectionName;
                    }
                };

                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                    $"~ [Section]  {os.SectionName}  →  {ns.Name}", discardAction));
                structChange = true;
            }

            int oldSecIdx = commonOldSecs.FindIndex(x => x.Id == ns.Id);
            int newSecIdx = commonNewSecs.IndexOf(ns);
            if (oldSecIdx != newSecIdx)
            {
                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                    $"~ [Section Reordered]  {ns.Name}"));
                structChange = true;
            }

            var commonOldItems = os.SectionSheet.Where(oi => ns.Items.Any(ni => ni.Id == oi.Id)).ToList();
            var commonNewItems = ns.Items.Where(ni => os.SectionSheet.Any(oi => oi.Id == ni.Id)).ToList();

            foreach (var ni in ns.Items)
            {
                var capturedNi = ni;
                var oi = os.SectionSheet.FirstOrDefault(i => i.Id == ni.Id);
                if (oi == null)
                {
                    System.Action discardAction = () => 
                    {
                        capturedNs.Items.Remove(capturedNi);
                        foreach (var lang in data.Languages)
                        {
                            var ts = lang.TableSheet.FirstOrDefault(x => x.Id == capturedNs.Id);
                            if (ts != null) ts.Items.RemoveAll(x => x.Id == capturedNi.Id);
                        }
                    };

                    lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                        LocalizationSaveReviewWindow.DiffLine.Kind.Added,
                        $"    + {ns.Name}.{ni.Key}", discardAction));
                    structChange = true;
                }
                else if (oi.Key != ni.Key)
                {
                    var capturedOi = oi;
                    System.Action discardAction = () => 
                    {
                        capturedNi.Key = capturedOi.Key;
                        foreach (var lang in data.Languages)
                        {
                            var ts = lang.TableSheet.FirstOrDefault(x => x.Id == capturedNs.Id);
                            if (ts != null)
                            {
                                var ti = ts.Items.FirstOrDefault(x => x.Id == capturedNi.Id);
                                if (ti != null) ti.Key = capturedOi.Key;
                            }
                        }
                    };

                    lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                        LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                        $"    ~ {ns.Name}.{oi.Key}  →  {ni.Key}", discardAction));
                    structChange = true;
                }

                if (oi != null)
                {
                    int oldItemIdx = commonOldItems.FindIndex(x => x.Id == ni.Id);
                    int newItemIdx = commonNewItems.IndexOf(ni);
                    if (oldItemIdx != newItemIdx)
                    {
                        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                            LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                            $"    ~ [Reordered]  {ns.Name}.{ni.Key}"));
                        structChange = true;
                    }
                }
            }

            foreach (var oi in os.SectionSheet)
            {
                var capturedOi = oi;
                if (ns.Items.All(i => i.Id != oi.Id))
                {
                    System.Action discardAction = () => 
                    {
                        var newEd = new EntryData(capturedOi, capturedNs);
                        capturedNs.Items.Add(newEd);
                        foreach (var lang in data.Languages)
                        {
                            var ts = lang.TableSheet.FirstOrDefault(x => x.Id == capturedNs.Id);
                            if (ts != null)
                            {
                                var found = lang.Asset?.Strings.FirstOrDefault(s => s.SectionId == capturedNs.Id && s.EntryId == capturedOi.Id);
                                ts.Items.Add(new TempEntry { Reference = newEd, Value = found?.Value ?? "" });
                            }
                        }
                    };

                    lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                        LocalizationSaveReviewWindow.DiffLine.Kind.Removed,
                        $"    - {ns.Name}.{oi.Key}", discardAction));
                    structChange = true;
                }
            }
        }

        foreach (var os in oldSecs)
        {
            var capturedOs = os;
            if (newSecs.All(ns => ns.Id != os.Id))
            {
                System.Action discardAction = () => 
                {
                    var restoredSec = new SectionData(capturedOs);
                    newSecs.Add(restoredSec);
                    foreach (var lang in data.Languages)
                    {
                        var ts = new TempSection { Reference = restoredSec };
                        foreach (var item in restoredSec.Items)
                        {
                            var found = lang.Asset?.Strings.FirstOrDefault(s => s.SectionId == restoredSec.Id && s.EntryId == item.Id);
                            ts.Items.Add(new TempEntry { Reference = item, Value = found?.Value ?? "" });
                        }
                        lang.TableSheet.Add(ts);
                    }
                };

                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Removed,
                    $"- [Section]  {os.SectionName}", discardAction));
                structChange = true;
            }
        }

        if (!structChange)
            lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                LocalizationSaveReviewWindow.DiffLine.Kind.Unchanged, "  (no structural changes)"));

        // ── Languages ─────────────────────────────────────────
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, ""));
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, "LANGUAGES"));
        
        bool langStructChange = false;
        var oldLangs = table.Languages;
        var newLangs = data.Languages;

        var commonOldLangs = oldLangs.Where(ol => newLangs.Any(nl => nl.Asset == ol)).ToList();
        var commonNewLangs = newLangs.Where(nl => oldLangs.Any(ol => ol == nl.Asset)).ToList();

        foreach (var nl in newLangs)
        {
            var capturedNl = nl;
            var ol = (nl.Asset != null) ? oldLangs.FirstOrDefault(l => l == nl.Asset) : null;
            
            if (ol == null)
            {
                System.Action discardAction = () => 
                {
                    newLangs.Remove(capturedNl);
                    data.LanguageCount--;
                };
                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Added,
                    $"+ [Language]  {nl.LanguageName}", discardAction));
                langStructChange = true;
            }
            else if (ol.LanguageName != nl.LanguageName)
            {
                var capturedOl = ol;
                System.Action discardAction = () => { capturedNl.LanguageName = capturedOl.LanguageName; };
                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                    $"~ [Language]  {ol.LanguageName}  →  {nl.LanguageName}", discardAction));
                langStructChange = true;
            }

            if (ol != null)
            {
                int oldLangIdx = commonOldLangs.IndexOf(ol);
                int newLangIdx = commonNewLangs.IndexOf(nl);
                if (oldLangIdx != newLangIdx)
                {
                    lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                        LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                        $"~ [Language Reordered]  {nl.LanguageName}"));
                    langStructChange = true;
                }
            }
        }

        foreach (var ol in oldLangs)
        {
            var capturedOl = ol;
            if (newLangs.All(nl => nl.Asset != ol))
            {
                System.Action discardAction = () => 
                {
                    var tl = new TempLanguageData { LanguageName = capturedOl.LanguageName, Asset = capturedOl };
                    foreach (var sec in data.TableSheet)
                    {
                        var ts = new TempSection { Reference = sec };
                        foreach (var entry in sec.Items)
                        {
                            var found = capturedOl.Strings.FirstOrDefault(s => s.SectionId == sec.Id && s.EntryId == entry.Id);
                            ts.Items.Add(new TempEntry { Reference = entry, Value = found?.Value ?? "" });
                        }
                        tl.TableSheet.Add(ts);
                    }
                    newLangs.Add(tl);
                    data.LanguageCount++;
                };
                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Removed,
                    $"- [Language]  {ol.LanguageName}", discardAction));
                langStructChange = true;
            }
        }

        if (!langStructChange)
        {
            lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                LocalizationSaveReviewWindow.DiffLine.Kind.Unchanged, "  (no structural changes in languages)"));
        }

        // ── Translations ─────────────────────────────────────────
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, ""));
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, "TRANSLATIONS"));

        foreach (var lang in data.Languages)
        {
            if (lang.Asset == null) continue;

            bool langHdr = false;
            foreach (var sec in lang.TableSheet)
            {
                foreach (var item in sec.Items)
                {
                    var saved = lang.Asset.Strings.FirstOrDefault(
                        s => s.SectionId == sec.Id && s.EntryId == item.Id);
                    string oldVal = saved?.Value ?? "";
                    string newVal = item.Value  ?? "";
                    if (oldVal == newVal) continue;

                    if (!langHdr)
                    {
                        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                            LocalizationSaveReviewWindow.DiffLine.Kind.Header,
                            $"  [{lang.LanguageName}]"));
                        langHdr = true;
                    }

                    string fullKey = sec.Name.Replace(" ","") + "." + item.Key.Replace(" ","");
                    System.Action discardAction = () => { item.Value = oldVal; };

                    if (string.IsNullOrEmpty(oldVal))
                    {
                        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                            LocalizationSaveReviewWindow.DiffLine.Kind.Added,
                            $"    + {fullKey}  =  \"{Trunc(newVal)}\"", discardAction));
                    }
                    else
                    {
                        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                            LocalizationSaveReviewWindow.DiffLine.Kind.Modified,
                            $"    ~ {fullKey}", discardAction));
                        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                            LocalizationSaveReviewWindow.DiffLine.Kind.Removed,
                            $"         - \"{Trunc(oldVal)}\""));
                        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                            LocalizationSaveReviewWindow.DiffLine.Kind.Added,
                            $"         + \"{Trunc(newVal)}\""));
                    }
                }
            }

            if (!langHdr)
            {
                lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                    LocalizationSaveReviewWindow.DiffLine.Kind.Unchanged,
                    $"  [{lang.LanguageName}]  (no changes)"));
            }
        }

        // ── Mod Pending Changes ───────────────────────────────────
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, ""));
        lines.Add(new LocalizationSaveReviewWindow.DiffLine(
            LocalizationSaveReviewWindow.DiffLine.Kind.Header, "MOD CHANGES"));

        var modLines = LocalizationEntryInspectorMods.BuildDiffLines();
        if (modLines.Count == 0)
        {
            lines.Add(new LocalizationSaveReviewWindow.DiffLine(
                LocalizationSaveReviewWindow.DiffLine.Kind.Unchanged, "  (no mod changes)"));
        }
        else
        {
            lines.AddRange(modLines);
        }

        return lines;
    }

    private static string Trunc(string s, int max = 60)
        => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");


    private void LoadOrCreateTable()
    {
        _table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(TABLE_PATH);
        if (_table == null)
        {
            var guids = AssetDatabase.FindAssets("t:LocalizationTable");
            if (guids.Length > 0)
                _table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        if (_table == null)
        {
            EnsureFolder("Assets/Resources/Localization");
            _table = CreateInstance<LocalizationTable>();
            AssetDatabase.CreateAsset(_table, TABLE_PATH);
            AssetDatabase.SaveAssets();
        }
        RebuildTreeViews();
    }

    // ─── Language Asset Ops ─────────────────────────────────────

    private void CreateLanguageAsset(TempLanguageData lang)
    {
        string assetPath = AssetDatabase.GetAssetPath(_table);
        if (string.IsNullOrEmpty(assetPath)) { Debug.LogError("Table path not found."); return; }

        var asset = CreateInstance<LocalizationLanguage>();
        asset.LanguageName = lang.LanguageName;

        string dir    = Path.GetDirectoryName(assetPath);
        string unique = AssetDatabase.GenerateUniqueAssetPath(
            Path.Combine(dir, "(Language) " + lang.LanguageName + ".asset").Replace("\\","/"));

        AssetDatabase.CreateAsset(asset, unique);
        AssetDatabase.SaveAssets();
        lang.Asset = asset;
    }

    private void AssignLanguageAsset(TempLanguageData lang)
    {
        lang.LanguageName = lang.Asset.LanguageName;

        // Match existing strings by SectionId + EntryId
        foreach (var sec in lang.TableSheet)
        {
            foreach (var item in sec.Items)
            {
                var match = lang.Asset.Strings.FirstOrDefault(
                    s => s.SectionId == sec.Id && s.EntryId == item.Id);
                item.Value = match?.Value ?? "";
            }
        }

        _langTree?.Reload();
    }

    private void ExportCSV()
    {
        if (_windowData == null) return;
        string path = EditorUtility.SaveFilePanel("Export CSV", "", "Localization", "csv");
        if (string.IsNullOrEmpty(path)) return;

        var sb = new StringBuilder();

        // header
        sb.Append("Key");
        foreach (var lang in _windowData.Languages)
            sb.Append("," + CsvEscape(lang.LanguageName));
        sb.AppendLine();

        // rows
        foreach (var sec in _windowData.TableSheet)
        {
            string secName = sec.Name.Replace(" ","");
            foreach (var entry in sec.Items)
            {
                string fullKey = secName + "." + entry.Key.Replace(" ","");
                sb.Append(CsvEscape(fullKey));

                foreach (var lang in _windowData.Languages)
                {
                    var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == sec);
                    var te = ts?.Items.FirstOrDefault(i => i.Reference == entry);
                    sb.Append("," + CsvEscape(te?.Value ?? ""));
                }
                sb.AppendLine();
            }
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[Localization Editor] CSV exported → {path}");
        EditorUtility.RevealInFinder(path);
    }

    private void ExportTemplate()
    {
        if (_windowData == null) return;
        string path = EditorUtility.SaveFilePanel("Export Template CSV", "", "Localization_Template", "csv");
        if (string.IsNullOrEmpty(path)) return;

        var sb = new StringBuilder();

        // ── Header: Key + dil isimleri ──────────────────────────
        sb.Append("Key");
        foreach (var lang in _windowData.Languages)
            sb.Append("," + CsvEscape(lang.LanguageName));
        sb.AppendLine();

        // ── Tek örnek dummy satır ────────────────────────────────
        sb.Append("Section.key_name");
        int langCount = _windowData.Languages.Count;
        for (int i = 0; i < langCount; i++)
            sb.Append(",TRANSLATE_ME");
        sb.AppendLine();

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[Localization Editor] Template exported → {path}");
        EditorUtility.RevealInFinder(path);
    }

    private void ImportCSV()
    {
        string path = EditorUtility.OpenFilePanel("Import CSV", "", "csv");
        if (string.IsNullOrEmpty(path)) return;

        string text = File.ReadAllText(path, Encoding.UTF8);
        var rows = CsvParse(text);

        if (rows.Count < 2) { EditorUtility.DisplayDialog("Error","The CSV file is very short.","OK"); return; }

        var header = rows[0];
        if (header.Count < 2 || header[0].ToLower() != "key")
        {
            EditorUtility.DisplayDialog("Error","CSV header is invalid.","OK");
            return;
        }

        // Find/add languages
        var langCols = new List<TempLanguageData>();
        for (int c = 1; c < header.Count; c++)
        {
            string name = header[c].Trim();
            var existing = _windowData.Languages.FirstOrDefault(l =>
                l.LanguageName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new TempLanguageData { LanguageName = name };
                _windowData.Languages.Add(existing);
            }
            langCols.Add(existing);
        }

        for (int r = 1; r < rows.Count; r++)
        {
            var cols = rows[r];
            if (cols.Count < 1) continue;
            string key = cols[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var parts = key.Split(new[]{'.'},2);
            string secName  = parts.Length > 1 ? parts[0] : "__root__";
            string entryKey = parts.Length > 1 ? parts[1] : parts[0];

            var sec = _windowData.TableSheet.FirstOrDefault(s => s.Name == secName);
            if (sec == null)
            {
                sec = new SectionData { Name = secName };
                _windowData.TableSheet.Add(sec);
                foreach (var lang in _windowData.Languages)
                    lang.TableSheet.Add(new TempSection { Reference = sec });
            }

            var entry = sec.Items.FirstOrDefault(e => e.Key == entryKey);
            if (entry == null)
            {
                entry = new EntryData { Key = entryKey, Parent = sec };
                sec.Items.Add(entry);
                foreach (var lang in _windowData.Languages)
                {
                    var ts = lang.TableSheet.FirstOrDefault(s => s.Reference == sec);
                    ts?.Items.Add(new TempEntry { Reference = entry, Value = "" });
                }
            }

            for (int c = 0; c < langCols.Count && c + 1 < cols.Count; c++)
            {
                var ts = langCols[c].TableSheet.FirstOrDefault(s => s.Reference == sec);
                var te = ts?.Items.FirstOrDefault(i => i.Reference == entry);
                // Only write to the cell if the CSV cell is NOT empty.
                // This ensures the import works additively: old translations are preserved.
                if (te != null && !string.IsNullOrEmpty(cols[c + 1]))
                    te.Value = cols[c + 1];
            }
        }

        // RebuildTreeViews() should NOT be called here — because that method reconstructs
        // _windowData from scratch from the disk (asset) and deletes all imported values.
        // Just reload the tree views and refresh the screen.
        _langTree?.Reload();
        _sheetTree?.Reload();
        _isDirty = true;
        Repaint();
        Debug.Log($"[Localization Editor] CSV imported ← {path}");
        EditorUtility.DisplayDialog("Import CSV","Successfully imported. Press 'Save Asset' button to persist changes.","OK");
    }

    // ═══════════════════════════════════════════════════════════
    //  DRAWING HELPERS
    // ═══════════════════════════════════════════════════════════

    private static Rect DrawHeaderWithBorder(ref Rect rect, GUIContent title, float headerHeight)
    {
        // outer border
        Rect border = rect;
        GUI.Box(border, GUIContent.none);

        // header
        Rect header = new Rect(border.x + 1, border.y + 1, border.width - 2, headerHeight);
        EditorGUI.DrawRect(header, new Color(0.18f, 0.18f, 0.18f));
        EditorGUI.LabelField(header, title, EditorStyles.miniBoldLabel);

        rect = new Rect(border.x + 1, header.yMax + 1, border.width - 2, border.height - headerHeight - 2);
        return header;
    }



    private static void DrawBorderBox(Rect rect)
    {
        GUI.Box(rect, GUIContent.none);
    }

    private static void DrawBorder(Rect rect)
    {
        var color = new Color(0.13f, 0.13f, 0.13f, 1f);
        // top
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        // bottom
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        // left
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        // right
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }


    private static void DrawSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));
    }

    // ═══════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════

    private static void EnsureFolder(string path)
    {
        var parts = path.Replace("\\","/").Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }


    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            return "\"" + s.Replace("\"","\"\"") + "\"";
        return s;
    }

    private static List<List<string>> CsvParse(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        bool inQ = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            
            if (inQ)
            {
                if (c == '"' && i + 1 < text.Length && text[i+1] == '"') 
                { 
                    currentCell.Append('"'); 
                    i++; 
                }
                else if (c == '"') 
                {
                    inQ = false;
                }
                else 
                {
                    currentCell.Append(c);
                }
            }
            else
            {
                if (c == '"') 
                {
                    inQ = true;
                }
                else if (c == ',') 
                { 
                    currentRow.Add(currentCell.ToString()); 
                    currentCell.Clear(); 
                }
                else if (c == '\r')
                {
                    // Ignore \r outside quotes if followed by \n
                    if (i + 1 < text.Length && text[i+1] == '\n')
                        continue;
                    else
                    {
                        // standalone \r is treated as a row break
                        currentRow.Add(currentCell.ToString());
                        currentCell.Clear();
                        rows.Add(currentRow);
                        currentRow = new List<string>();
                    }
                }
                else if (c == '\n') 
                { 
                    currentRow.Add(currentCell.ToString()); 
                    currentCell.Clear(); 
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else 
                {
                    currentCell.Append(c);
                }
            }
        }
        
        // Add the last cell and row if anything remains or if it doesn't end in newline
        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}

// ═══════════════════════════════════════════════════════════
//  EDITOR SNAPSHOT  (for undo/redo)
// ═══════════════════════════════════════════════════════════

public class EditorSnapshot
{
    public List<SecSnap>  Sections  = new List<SecSnap>();
    public List<LangSnap> Languages = new List<LangSnap>();

    public class SecSnap
    {
        public int             Id;
        public string          Name;
        public List<EntrySnap> Keys = new List<EntrySnap>();
    }
    public class EntrySnap
    {
        public int    Id;
        public string Key;
    }
    public class LangSnap
    {
        public string              LanguageName;
        public LocalizationLanguage Asset;
        public List<ValSnap>       Values = new List<ValSnap>();
    }
    public class ValSnap
    {
        public int    SectionId;
        public int    EntryId;
        public string Value;
    }
}

// ═══════════════════════════════════════════════════════════
//  SAVE REVIEW WINDOW
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Modal-style EditorWindow that shows a diff-style review of pending
/// localization changes before the user confirms or cancels the save.
/// </summary>
public class LocalizationSaveReviewWindow : EditorWindow
{
    public struct DiffLine
    {
        public enum Kind { Header, Added, Removed, Modified, Unchanged }
        public Kind   LineKind;
        public string Text;
        public System.Action DiscardAction;
        public DiffLine(Kind k, string t, System.Action discardAction = null) { LineKind = k; Text = t; DiscardAction = discardAction; }
    }

    // ── Colours ──────────────────────────────────────────────────
    private static readonly Color k_ColHeader    = new Color(0.55f, 0.75f, 1.00f);  // blue
    private static readonly Color k_ColAdded     = new Color(0.40f, 0.85f, 0.50f);  // green
    private static readonly Color k_ColRemoved   = new Color(0.90f, 0.35f, 0.35f);  // red
    private static readonly Color k_ColModified  = new Color(0.99f, 0.78f, 0.20f);  // yellow
    private static readonly Color k_ColUnchanged = new Color(0.60f, 0.60f, 0.60f);  // grey
    private static readonly Color k_BgEven       = new Color(0.18f, 0.18f, 0.18f);
    private static readonly Color k_BgOdd        = new Color(0.21f, 0.21f, 0.21f);
    private static readonly Color k_BgHeader     = new Color(0.14f, 0.14f, 0.14f);

    // ── State ─────────────────────────────────────────────────────
    private List<DiffLine>  _lines;
    private System.Action   _onConfirm;
    private System.Action   _onRefresh;
    private System.Action   _onDiscardAll;
    private bool            _reviewOnly;
    private Vector2         _scroll;
    private GUIStyle        _lineStyle;
    private bool            _stylesReady;

    // ── Open ──────────────────────────────────────────────────────
    public static LocalizationSaveReviewWindow Show(List<DiffLine> lines, System.Action onConfirm, bool reviewOnly = false, System.Action onRefresh = null, System.Action onDiscardAll = null)
    {
        var win = CreateInstance<LocalizationSaveReviewWindow>();
        win.titleContent = new GUIContent(reviewOnly ? "Change Review" : "Save Review",
            EditorGUIUtility.IconContent("d_FilterByType").image);
        win._lines        = lines;
        win._onConfirm    = onConfirm;
        win._onRefresh    = onRefresh;
        win._onDiscardAll = onDiscardAll;
        win._reviewOnly   = reviewOnly;
        win.minSize     = new Vector2(560, 400);
        win.ShowUtility();   // always-on-top, modal-like
        return win;
    }

    public void RefreshData(List<DiffLine> lines)
    {
        _lines = lines;
        
        bool hasChanges = _lines.Any(l => 
            l.LineKind == DiffLine.Kind.Added ||
            l.LineKind == DiffLine.Kind.Removed ||
            l.LineKind == DiffLine.Kind.Modified);

        if (!hasChanges)
        {
            _reviewOnly = true;
            titleContent = new GUIContent("Change Review", EditorGUIUtility.IconContent("d_FilterByType").image);
        }

        Repaint();
    }

    // ── Styles ────────────────────────────────────────────────────
    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _lineStyle = new GUIStyle(EditorStyles.label)
        {
            font      = EditorStyles.miniFont,
            fontSize  = 11,
            richText  = false,
            padding   = new RectOffset(6, 4, 1, 1),
            wordWrap  = false
        };
        _stylesReady = true;
    }

    // ── OnGUI ─────────────────────────────────────────────────────
    private void OnGUI()
    {
        EnsureStyles();

        // ── Title bar ──────────────────────────────────────────
        Rect titleBar = new Rect(0, 0, position.width, 28f);
        EditorGUI.DrawRect(titleBar, new Color(0.12f, 0.12f, 0.12f));
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 12, normal = { textColor = Color.white } };
        string titleText = _reviewOnly ? "  Reviewing Pending Changes" : "  Review Changes Before Saving";
        GUI.Label(new Rect(8f, 4f, position.width - 16f, 22f), titleText, titleStyle);

        // ── Legend ─────────────────────────────────────────────
        float legY = 32f;
        DrawLegend(ref legY);

        // ── Diff scroll area ───────────────────────────────────
        float footH   = 36f;
        float scrollY = legY + 4f;
        Rect scrollRect = new Rect(0, scrollY, position.width,
            position.height - scrollY - footH);
        EditorGUI.DrawRect(scrollRect, new Color(0.16f, 0.16f, 0.16f));

        float contentH = 0f;
        List<float> rowHeights = new List<float>();
        
        if (_lines != null)
        {
            var testStyle = new GUIStyle(_lineStyle) { wordWrap = true };
            for (int i = 0; i < _lines.Count; i++)
            {
                testStyle.fontStyle = _lines[i].LineKind == DiffLine.Kind.Header ? FontStyle.Bold : FontStyle.Normal;
                float h = Mathf.Max(18f, testStyle.CalcHeight(new GUIContent(_lines[i].Text), scrollRect.width - 24f));
                rowHeights.Add(h);
                contentH += h;
            }
        }

        Rect viewRect = new Rect(0, 0, scrollRect.width - 16f, contentH);

        _scroll = GUI.BeginScrollView(scrollRect, _scroll, viewRect);
        {
            if (_lines != null)
            {
                float currentY = 0f;
                for (int i = 0; i < _lines.Count; i++)
                {
                    var dl   = _lines[i];
                    float rowH = rowHeights[i];
                    Rect row = new Rect(0, currentY, viewRect.width, rowH);
                    currentY += rowH;

                    // background
                    Color bg = dl.LineKind == DiffLine.Kind.Header ? k_BgHeader
                             : i % 2 == 0 ? k_BgEven : k_BgOdd;
                    EditorGUI.DrawRect(row, bg);

                    // text colour
                    Color tc = dl.LineKind switch
                    {
                        DiffLine.Kind.Header    => k_ColHeader,
                        DiffLine.Kind.Added     => k_ColAdded,
                        DiffLine.Kind.Removed   => k_ColRemoved,
                        DiffLine.Kind.Modified  => k_ColModified,
                        _                       => k_ColUnchanged
                    };

                    var s = new GUIStyle(_lineStyle)
                        { normal = { textColor = tc },
                          fontStyle = dl.LineKind == DiffLine.Kind.Header
                                      ? FontStyle.Bold : FontStyle.Normal,
                          wordWrap = true };

                    GUI.Label(row, dl.Text, s);

                    if (dl.DiscardAction != null && Event.current.type == EventType.ContextClick && row.Contains(Event.current.mousePosition))
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Discard Change"), false, () => 
                        {
                            dl.DiscardAction.Invoke();
                            _onRefresh?.Invoke();
                        });
                        menu.ShowAsContext();
                        Event.current.Use();
                    }
                }
            }
        }
        GUI.EndScrollView();

        // ── Footer buttons ─────────────────────────────────────
        Rect footRect = new Rect(0, position.height - footH,
            position.width, footH);
        EditorGUI.DrawRect(footRect, new Color(0.13f, 0.13f, 0.13f));

        float bW = 110f, bH = 24f;
        float bY = footRect.y + (footH - bH) * 0.5f;

        if (GUI.Button(new Rect(8f, bY, bW, bH), "Discard All"))
        {
            if (EditorUtility.DisplayDialog("Discard All Changes", "Are you sure you want to discard ALL pending changes? This cannot be undone.", "Yes, Discard", "Cancel"))
            {
                _onDiscardAll?.Invoke();
                Close();
            }
        }

        if (_reviewOnly)
        {
            // Review-only: just Close
            if (GUI.Button(new Rect(footRect.xMax - bW - 8f, bY, bW, bH), "Close"))
                Close();
        }
        else
        {
            // Save-confirm mode: Cancel (close only) + Save (commit & close)
            if (GUI.Button(new Rect(footRect.xMax - bW * 2f - 16f, bY, bW, bH), "Cancel"))
                Close();   // close without saving

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f, 0.60f, 0.30f);
            if (GUI.Button(new Rect(footRect.xMax - bW - 8f, bY, bW, bH), "Save"))
            {
                _onConfirm?.Invoke();
                Close();
            }
            GUI.backgroundColor = prevBg;
        }
    }

    private void DrawLegend(ref float y)
    {
        float h = 20f;
        EditorGUI.DrawRect(new Rect(0, y, position.width, h),
            new Color(0.13f, 0.13f, 0.13f));

        float x = 8f;
        x = LegendItem(x, y, h, k_ColAdded,    "+ Added");
        x = LegendItem(x, y, h, k_ColRemoved,  "- Removed");
        x = LegendItem(x, y, h, k_ColModified, "~ Modified");
        LegendItem(x, y, h, k_ColUnchanged, "  Unchanged");

        y += h;
    }

    private float LegendItem(float x, float y, float h, Color c, string label)
    {
        var s = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = c }, fontStyle = FontStyle.Bold };
        float w = s.CalcSize(new GUIContent(label)).x + 20f;
        GUI.Label(new Rect(x, y, w, h), label, s);
        return x + w;
    }
}
#endif
