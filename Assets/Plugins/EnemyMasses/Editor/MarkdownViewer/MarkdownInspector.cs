using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Custom inspector for viewing Markdown (.md) files in Unity's Inspector panel.
    /// Renders markdown with proper formatting including headers, bold, italic, code blocks, lists, links, and tables.
    /// </summary>
    [CustomEditor(typeof(TextAsset))]
    public class MarkdownInspector : UnityEditor.Editor
    {
        private const string StyleSheetPath = "Assets/Plugins/EnemyMasses/Editor/MarkdownViewer/MarkdownViewerStyle.uss";

        // Dark mode colors (Pro skin) - High contrast
        private static readonly Color DarkHeaderColor = new Color(0.4f, 0.75f, 1f);          // Bright blue
        private static readonly Color DarkCodeBackgroundColor = new Color(0.1f, 0.1f, 0.1f);
        private static readonly Color DarkCodeTextColor = new Color(1f, 0.95f, 0.7f);        // Warm yellow
        private static readonly Color DarkLinkColor = new Color(0.4f, 0.7f, 1f);             // Bright blue
        private static readonly Color DarkBlockquoteColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color DarkHorizontalRuleColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color DarkTableBorderColor = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color DarkTableHeaderBgColor = new Color(0.18f, 0.18f, 0.22f);
        private static readonly Color DarkTextColor = new Color(0.95f, 0.95f, 0.95f);        // Near white
        private static readonly Color DarkCodeBorderColor = new Color(0.3f, 0.55f, 0.85f);

        // Light mode colors (Personal skin) - High contrast
        private static readonly Color LightHeaderColor = new Color(0.0f, 0.3f, 0.65f);       // Deep blue
        private static readonly Color LightCodeBackgroundColor = new Color(0.94f, 0.94f, 0.94f);
        private static readonly Color LightCodeTextColor = new Color(0.6f, 0.1f, 0.1f);      // Dark red
        private static readonly Color LightLinkColor = new Color(0.0f, 0.35f, 0.7f);         // Strong blue
        private static readonly Color LightBlockquoteColor = new Color(0.35f, 0.35f, 0.35f);
        private static readonly Color LightHorizontalRuleColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color LightTableBorderColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color LightTableHeaderBgColor = new Color(0.88f, 0.88f, 0.92f);
        private static readonly Color LightTextColor = new Color(0.1f, 0.1f, 0.1f);          // Near black
        private static readonly Color LightCodeBorderColor = new Color(0.2f, 0.45f, 0.7f);

        // Current theme colors (set based on skin)
        private Color _headerColor;
        private Color _codeBackgroundColor;
        private Color _codeTextColor;
        private Color _linkColor;
        private Color _blockquoteColor;
        private Color _horizontalRuleColor;
        private Color _tableBorderColor;
        private Color _tableHeaderBgColor;
        private Color _textColor;
        private Color _codeBorderColor;

        private bool _isMarkdownFile;
        private string _markdownContent;
        private Vector2 _scrollPosition;
        private GUIStyle _h1Style;
        private GUIStyle _h2Style;
        private GUIStyle _h3Style;
        private GUIStyle _h4Style;
        private GUIStyle _normalStyle;
        private GUIStyle _boldStyle;
        private GUIStyle _italicStyle;
        private GUIStyle _codeStyle;
        private GUIStyle _codeBlockStyle;
        private GUIStyle _blockquoteStyle;
        private GUIStyle _listItemStyle;
        private GUIStyle _linkStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _tableCellStyle;
        private bool _stylesInitialized;
        private bool _showRawText;

        private void OnEnable()
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            _isMarkdownFile = !string.IsNullOrEmpty(assetPath) && 
                              assetPath.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase);

            if (_isMarkdownFile)
            {
                TextAsset textAsset = target as TextAsset;
                _markdownContent = textAsset?.text ?? string.Empty;
            }
        }

        private void UpdateThemeColors()
        {
            bool isDarkMode = EditorGUIUtility.isProSkin;

            if (isDarkMode)
            {
                _headerColor = DarkHeaderColor;
                _codeBackgroundColor = DarkCodeBackgroundColor;
                _codeTextColor = DarkCodeTextColor;
                _linkColor = DarkLinkColor;
                _blockquoteColor = DarkBlockquoteColor;
                _horizontalRuleColor = DarkHorizontalRuleColor;
                _tableBorderColor = DarkTableBorderColor;
                _tableHeaderBgColor = DarkTableHeaderBgColor;
                _textColor = DarkTextColor;
                _codeBorderColor = DarkCodeBorderColor;
            }
            else
            {
                _headerColor = LightHeaderColor;
                _codeBackgroundColor = LightCodeBackgroundColor;
                _codeTextColor = LightCodeTextColor;
                _linkColor = LightLinkColor;
                _blockquoteColor = LightBlockquoteColor;
                _horizontalRuleColor = LightHorizontalRuleColor;
                _tableBorderColor = LightTableBorderColor;
                _tableHeaderBgColor = LightTableHeaderBgColor;
                _textColor = LightTextColor;
                _codeBorderColor = LightCodeBorderColor;
            }
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            // Set colors based on current Unity skin (dark/light mode)
            UpdateThemeColors();

            // H1 Style
            _h1Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(0, 0, 10, 5)
            };
            _h1Style.normal.textColor = _headerColor;

            // H2 Style
            _h2Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(0, 0, 8, 4)
            };
            _h2Style.normal.textColor = _headerColor;

            // H3 Style
            _h3Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(0, 0, 6, 3)
            };
            _h3Style.normal.textColor = _headerColor;

            // H4 Style
            _h4Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(0, 0, 4, 2)
            };
            _h4Style.normal.textColor = _headerColor;

            // Normal Style
            _normalStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(0, 0, 2, 2)
            };
            _normalStyle.normal.textColor = _textColor;

            // Bold Style
            _boldStyle = new GUIStyle(_normalStyle)
            {
                fontStyle = FontStyle.Bold
            };

            // Italic Style
            _italicStyle = new GUIStyle(_normalStyle)
            {
                fontStyle = FontStyle.Italic
            };

            // Inline Code Style
            _codeStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = false,
                richText = true,
                padding = new RectOffset(4, 4, 2, 2)
            };
            _codeStyle.normal.textColor = _codeTextColor;

            // Code Block Style
            _codeBlockStyle = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(8, 8, 8, 8)
            };
            _codeBlockStyle.normal.textColor = _codeTextColor;

            // Blockquote Style
            _blockquoteStyle = new GUIStyle(_normalStyle)
            {
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(15, 0, 2, 2)
            };
            _blockquoteStyle.normal.textColor = _blockquoteColor;

            // List Item Style
            _listItemStyle = new GUIStyle(_normalStyle)
            {
                padding = new RectOffset(20, 0, 1, 1)
            };
            _listItemStyle.normal.textColor = _textColor;

            // Link Style
            _linkStyle = new GUIStyle(_normalStyle);
            _linkStyle.normal.textColor = _linkColor;

            // Table Header Style
            _tableHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(5, 5, 3, 3),
                richText = true
            };

            // Table Cell Style
            _tableCellStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(5, 5, 3, 3),
                richText = true,
                wordWrap = true
            };

            _stylesInitialized = true;
        }

        public override VisualElement CreateInspectorGUI()
        {
            if (!_isMarkdownFile)
            {
                return null;
            }

            var root = new VisualElement();
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            var toolbar = new VisualElement();
            toolbar.AddToClassList("markdown-toolbar");

            var titleLabel = new Label("Markdown Viewer");
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolbar.Add(titleLabel);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            var toggleButton = new Button();
            toggleButton.AddToClassList("markdown-toolbar-button");

            var openButton = new Button(() =>
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(path))
                {
                    System.Diagnostics.Process.Start(Path.GetFullPath(path));
                }
            })
            {
                text = "Open in Editor"
            };
            openButton.AddToClassList("markdown-toolbar-button");

            toolbar.Add(toggleButton);
            toolbar.Add(openButton);
            root.Add(toolbar);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;

            var contentContainer = new VisualElement();
            contentContainer.AddToClassList("markdown-container");
            scrollView.Add(contentContainer);

            void RefreshContent()
            {
                contentContainer.Clear();
                if (_showRawText)
                {
                    var rawField = new TextField
                    {
                        value = _markdownContent ?? string.Empty,
                        multiline = true,
                        isReadOnly = true
                    };
                    rawField.AddToClassList("markdown-paragraph");
#if UNITY_6000_0_OR_NEWER
                    rawField.style.whiteSpace = WhiteSpace.PreWrap;
#else
                    rawField.style.whiteSpace = WhiteSpace.Normal;
#endif
                    rawField.style.flexGrow = 1;
                    contentContainer.Add(rawField);
                }
                else
                {
                    BuildMarkdownElements(contentContainer, _markdownContent);
                }
            }

            void UpdateToggleButton()
            {
                toggleButton.text = _showRawText ? "Show Formatted" : "Show Raw";
            }

            toggleButton.clicked += () =>
            {
                _showRawText = !_showRawText;
                UpdateToggleButton();
                RefreshContent();
            };

            UpdateToggleButton();
            RefreshContent();

            root.Add(scrollView);
            return root;
        }

        public override void OnInspectorGUI()
        {
            if (!_isMarkdownFile)
            {
                // Not a markdown file, use default inspector
                DrawDefaultInspector();
                return;
            }

            InitializeStyles();

            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            GUILayout.Label("Markdown Viewer", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button(_showRawText ? "Show Formatted" : "Show Raw", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                _showRawText = !_showRawText;
            }
            
            if (GUILayout.Button("Open in Editor", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(path))
                {
                    System.Diagnostics.Process.Start(Path.GetFullPath(path));
                }
            }
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (_showRawText)
            {
                // Show raw text
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                EditorGUILayout.TextArea(_markdownContent, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
            else
            {
                // Render markdown
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                RenderMarkdown(_markdownContent);
                EditorGUILayout.EndScrollView();
            }
        }

        private void BuildMarkdownElements(VisualElement container, string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return;
            }

            string[] lines = markdown.Split('\n');
            bool inCodeBlock = false;
            string codeBlockContent = "";
            string codeBlockLanguage = "";
            List<string> tableRows = new List<string>();
            bool inTable = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                if (line.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeBlockLanguage = line.Length > 3 ? line.Substring(3).Trim() : "";
                        codeBlockContent = "";
                    }
                    else
                    {
                        inCodeBlock = false;
                        AddCodeBlockElement(container, codeBlockContent, codeBlockLanguage);
                        codeBlockContent = "";
                        codeBlockLanguage = "";
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBlockContent += (codeBlockContent.Length > 0 ? "\n" : "") + line;
                    continue;
                }

                if (IsTableRow(line))
                {
                    if (!inTable)
                    {
                        inTable = true;
                        tableRows.Clear();
                    }
                    tableRows.Add(line);
                    continue;
                }
                else if (inTable)
                {
                    RenderTableElement(container, tableRows);
                    tableRows.Clear();
                    inTable = false;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    container.Add(CreateSpacer(8));
                    continue;
                }

                if (Regex.IsMatch(line.Trim(), @"^(-{3,}|\*{3,}|_{3,})$"))
                {
                    container.Add(CreateHorizontalRuleElement());
                    continue;
                }

                if (line.StartsWith("####"))
                {
                    string headerText = line.Substring(4).Trim();
                    container.Add(CreateMarkdownLabel(ProcessInlineFormatting(headerText), "markdown-header-4"));
                    continue;
                }
                if (line.StartsWith("###"))
                {
                    string headerText = line.Substring(3).Trim();
                    container.Add(CreateMarkdownLabel(ProcessInlineFormatting(headerText), "markdown-header-3"));
                    continue;
                }
                if (line.StartsWith("##"))
                {
                    string headerText = line.Substring(2).Trim();
                    container.Add(CreateMarkdownLabel(ProcessInlineFormatting(headerText), "markdown-header-2"));
                    continue;
                }
                if (line.StartsWith("#"))
                {
                    string headerText = line.Substring(1).Trim();
                    container.Add(CreateMarkdownLabel(ProcessInlineFormatting(headerText), "markdown-header-1"));
                    continue;
                }

                if (line.StartsWith(">"))
                {
                    string quoteText = line.Substring(1).Trim();
                    container.Add(CreateMarkdownLabel(ProcessInlineFormatting(quoteText), "markdown-blockquote"));
                    continue;
                }

                if (Regex.IsMatch(line, @"^\s*[-*+]\s"))
                {
                    int indentLevel = GetIndentLevel(line);
                    string listText = Regex.Replace(line, @"^\s*[-*+]\s", "").Trim();
                    string bullet = indentLevel == 0 ? "•" : (indentLevel == 1 ? "◦" : "▪");

                    var itemLabel = CreateMarkdownLabel($"{bullet} {ProcessInlineFormatting(listText)}", "markdown-list-item");
                    itemLabel.style.paddingLeft = new Length(20 + (indentLevel * 15), LengthUnit.Pixel);
                    container.Add(itemLabel);
                    continue;
                }

                Match orderedMatch = Regex.Match(line, @"^\s*(\d+)\.\s");
                if (orderedMatch.Success)
                {
                    int indentLevel = GetIndentLevel(line);
                    string listText = line.Substring(orderedMatch.Length).Trim();
                    string number = orderedMatch.Groups[1].Value;

                    var itemLabel = CreateMarkdownLabel($"{number}. {ProcessInlineFormatting(listText)}", "markdown-list-item");
                    itemLabel.style.paddingLeft = new Length(20 + (indentLevel * 15), LengthUnit.Pixel);
                    container.Add(itemLabel);
                    continue;
                }

                Match checkboxMatch = Regex.Match(line, @"^\s*[-*+]\s*\[([ xX])\]\s*(.*)$");
                if (checkboxMatch.Success)
                {
                    bool isChecked = checkboxMatch.Groups[1].Value.ToLower() == "x";
                    string checkboxText = checkboxMatch.Groups[2].Value;
                    string checkbox = isChecked ? "☑" : "☐";

                    container.Add(CreateMarkdownLabel($"{checkbox} {ProcessInlineFormatting(checkboxText)}", "markdown-list-item"));
                    continue;
                }

                container.Add(CreateMarkdownLabel(ProcessInlineFormatting(line), "markdown-paragraph"));
            }

            if (inTable && tableRows.Count > 0)
            {
                RenderTableElement(container, tableRows);
            }
        }

        private void AddCodeBlockElement(VisualElement container, string code, string language)
        {
            if (!string.IsNullOrEmpty(language))
            {
                var langLabel = new Label(language.ToUpper());
                langLabel.AddToClassList("markdown-code-language");
                container.Add(langLabel);
            }

            var codeLabel = new Label(code ?? string.Empty);
            codeLabel.enableRichText = false;
            codeLabel.AddToClassList("markdown-code-block");
#if UNITY_6000_0_OR_NEWER
            codeLabel.style.whiteSpace = WhiteSpace.PreWrap;
#else
            codeLabel.style.whiteSpace = WhiteSpace.Normal;
#endif
            container.Add(codeLabel);
        }

        private void RenderTableElement(VisualElement container, List<string> rows)
        {
            if (rows.Count < 2)
            {
                return;
            }

            var table = new VisualElement();
            table.AddToClassList("markdown-table");
            container.Add(table);

            string[] headers = ParseTableRow(rows[0]);

            int dataStartIndex = 1;
            if (rows.Count > 1 && Regex.IsMatch(rows[1].Trim(), @"^[\|\-\:\s]+$"))
            {
                dataStartIndex = 2;
            }

            var headerRow = new VisualElement();
            headerRow.AddToClassList("markdown-table-row");
            headerRow.AddToClassList("markdown-table-header-row");
            table.Add(headerRow);

            for (int col = 0; col < headers.Length; col++)
            {
                var headerLabel = CreateMarkdownLabel(ProcessInlineFormatting(headers[col].Trim()), "markdown-table-header");
                headerLabel.style.flexGrow = 1;
                headerLabel.style.flexBasis = 0;
                headerRow.Add(headerLabel);
            }

            for (int row = dataStartIndex; row < rows.Count; row++)
            {
                string[] cells = ParseTableRow(rows[row]);

                var rowElement = new VisualElement();
                rowElement.AddToClassList("markdown-table-row");
                if ((row - dataStartIndex) % 2 == 1)
                {
                    rowElement.AddToClassList("markdown-table-row-alt");
                }
                table.Add(rowElement);

                for (int col = 0; col < Mathf.Min(cells.Length, headers.Length); col++)
                {
                    var cellLabel = CreateMarkdownLabel(ProcessInlineFormatting(cells[col].Trim()), "markdown-table-cell");
                    cellLabel.style.flexGrow = 1;
                    cellLabel.style.flexBasis = 0;
                    rowElement.Add(cellLabel);
                }
            }
        }

        private static VisualElement CreateSpacer(float height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            return spacer;
        }

        private static VisualElement CreateHorizontalRuleElement()
        {
            var rule = new VisualElement();
            rule.AddToClassList("markdown-horizontal-rule");
            return rule;
        }

        private static Label CreateMarkdownLabel(string text, string className)
        {
            var label = new Label(text ?? string.Empty)
            {
                enableRichText = true
            };
            label.AddToClassList(className);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void RenderMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return;

            string[] lines = markdown.Split('\n');
            bool inCodeBlock = false;
            string codeBlockContent = "";
            string codeBlockLanguage = "";
            List<string> tableRows = new List<string>();
            bool inTable = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                // Code block handling
                if (line.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeBlockLanguage = line.Length > 3 ? line.Substring(3).Trim() : "";
                        codeBlockContent = "";
                    }
                    else
                    {
                        // End of code block
                        inCodeBlock = false;
                        RenderCodeBlock(codeBlockContent, codeBlockLanguage);
                        codeBlockContent = "";
                        codeBlockLanguage = "";
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBlockContent += (codeBlockContent.Length > 0 ? "\n" : "") + line;
                    continue;
                }

                // Table handling
                if (IsTableRow(line))
                {
                    if (!inTable)
                    {
                        inTable = true;
                        tableRows.Clear();
                    }
                    tableRows.Add(line);
                    continue;
                }
                else if (inTable)
                {
                    // End of table
                    RenderTable(tableRows);
                    tableRows.Clear();
                    inTable = false;
                }

                // Empty line
                if (string.IsNullOrWhiteSpace(line))
                {
                    EditorGUILayout.Space(8);
                    continue;
                }

                // Horizontal rule
                if (Regex.IsMatch(line.Trim(), @"^(-{3,}|\*{3,}|_{3,})$"))
                {
                    DrawHorizontalRule();
                    continue;
                }

                // Headers
                if (line.StartsWith("####"))
                {
                    string headerText = line.Substring(4).Trim();
                    EditorGUILayout.LabelField(ProcessInlineFormatting(headerText), _h4Style);
                    continue;
                }
                if (line.StartsWith("###"))
                {
                    string headerText = line.Substring(3).Trim();
                    EditorGUILayout.LabelField(ProcessInlineFormatting(headerText), _h3Style);
                    continue;
                }
                if (line.StartsWith("##"))
                {
                    string headerText = line.Substring(2).Trim();
                    EditorGUILayout.LabelField(ProcessInlineFormatting(headerText), _h2Style);
                    continue;
                }
                if (line.StartsWith("#"))
                {
                    string headerText = line.Substring(1).Trim();
                    EditorGUILayout.LabelField(ProcessInlineFormatting(headerText), _h1Style);
                    continue;
                }

                // Blockquote
                if (line.StartsWith(">"))
                {
                    string quoteText = line.Substring(1).Trim();
                    EditorGUILayout.BeginHorizontal();
                    DrawBlockquoteBorder();
                    EditorGUILayout.LabelField(ProcessInlineFormatting(quoteText), _blockquoteStyle);
                    EditorGUILayout.EndHorizontal();
                    continue;
                }

                // Unordered list
                if (Regex.IsMatch(line, @"^\s*[-*+]\s"))
                {
                    int indentLevel = GetIndentLevel(line);
                    string listText = Regex.Replace(line, @"^\s*[-*+]\s", "").Trim();
                    string bullet = indentLevel == 0 ? "•" : (indentLevel == 1 ? "◦" : "▪");
                    
                    GUIStyle indentedStyle = new GUIStyle(_listItemStyle);
                    indentedStyle.padding.left = 20 + (indentLevel * 15);
                    
                    EditorGUILayout.LabelField($"{bullet} {ProcessInlineFormatting(listText)}", indentedStyle);
                    continue;
                }

                // Ordered list
                Match orderedMatch = Regex.Match(line, @"^\s*(\d+)\.\s");
                if (orderedMatch.Success)
                {
                    int indentLevel = GetIndentLevel(line);
                    string listText = line.Substring(orderedMatch.Length).Trim();
                    string number = orderedMatch.Groups[1].Value;
                    
                    GUIStyle indentedStyle = new GUIStyle(_listItemStyle);
                    indentedStyle.padding.left = 20 + (indentLevel * 15);
                    
                    EditorGUILayout.LabelField($"{number}. {ProcessInlineFormatting(listText)}", indentedStyle);
                    continue;
                }

                // Checkbox list (GitHub style)
                Match checkboxMatch = Regex.Match(line, @"^\s*[-*+]\s*\[([ xX])\]\s*(.*)$");
                if (checkboxMatch.Success)
                {
                    bool isChecked = checkboxMatch.Groups[1].Value.ToLower() == "x";
                    string checkboxText = checkboxMatch.Groups[2].Value;
                    string checkbox = isChecked ? "☑" : "☐";
                    
                    EditorGUILayout.LabelField($"{checkbox} {ProcessInlineFormatting(checkboxText)}", _listItemStyle);
                    continue;
                }

                // Normal paragraph
                EditorGUILayout.LabelField(ProcessInlineFormatting(line), _normalStyle);
            }

            // Handle table at end of file
            if (inTable && tableRows.Count > 0)
            {
                RenderTable(tableRows);
            }
        }

        private bool IsTableRow(string line)
        {
            return line.Trim().StartsWith("|") || Regex.IsMatch(line.Trim(), @"^[\|\-\:\s]+$");
        }

        private void RenderTable(List<string> rows)
        {
            if (rows.Count < 2) return;

            EditorGUILayout.Space(5);

            // Parse header row
            string[] headers = ParseTableRow(rows[0]);
            
            // Skip separator row (row[1])
            int dataStartIndex = 1;
            if (rows.Count > 1 && Regex.IsMatch(rows[1].Trim(), @"^[\|\-\:\s]+$"))
            {
                dataStartIndex = 2;
            }

            // Calculate column widths
            float totalWidth = EditorGUIUtility.currentViewWidth - 40;
            float columnWidth = totalWidth / headers.Length;

            // Draw header row
            EditorGUILayout.BeginHorizontal();
            Rect headerRect = GUILayoutUtility.GetRect(totalWidth, 22);
            EditorGUI.DrawRect(headerRect, _tableHeaderBgColor);
            
            for (int col = 0; col < headers.Length; col++)
            {
                Rect cellRect = new Rect(headerRect.x + col * columnWidth, headerRect.y, columnWidth, headerRect.height);
                EditorGUI.LabelField(cellRect, ProcessInlineFormatting(headers[col].Trim()), _tableHeaderStyle);
            }
            EditorGUILayout.EndHorizontal();

            // Draw data rows
            for (int row = dataStartIndex; row < rows.Count; row++)
            {
                string[] cells = ParseTableRow(rows[row]);
                
                EditorGUILayout.BeginHorizontal();
                Rect rowRect = GUILayoutUtility.GetRect(totalWidth, 20);
                
                // Alternate row background
                if ((row - dataStartIndex) % 2 == 1)
                {
                    Color altRowColor = EditorGUIUtility.isProSkin 
                        ? new Color(0.2f, 0.2f, 0.2f) 
                        : new Color(0.92f, 0.92f, 0.92f);
                    EditorGUI.DrawRect(rowRect, altRowColor);
                }

                for (int col = 0; col < Mathf.Min(cells.Length, headers.Length); col++)
                {
                    Rect cellRect = new Rect(rowRect.x + col * columnWidth, rowRect.y, columnWidth, rowRect.height);
                    EditorGUI.LabelField(cellRect, ProcessInlineFormatting(cells[col].Trim()), _tableCellStyle);
                }
                EditorGUILayout.EndHorizontal();
            }

            // Draw border
            Rect tableRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y + tableRect.height, totalWidth, 1), _tableBorderColor);

            EditorGUILayout.Space(5);
        }

        private string[] ParseTableRow(string row)
        {
            // Remove leading/trailing pipes and split
            string trimmed = row.Trim();
            if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|")) trimmed = trimmed.Substring(0, trimmed.Length - 1);
            return trimmed.Split('|');
        }

        private string ProcessInlineFormatting(string text)
        {
            // Process inline code first (to avoid conflicts with other formatting)
            // Use different colors for dark/light mode - high contrast
            string codeColor = EditorGUIUtility.isProSkin ? "#FFE566" : "#991B1B";
            text = Regex.Replace(text, @"`([^`]+)`", $"<color={codeColor}><b>$1</b></color>");

            // Bold and Italic combined
            text = Regex.Replace(text, @"\*\*\*([^*]+)\*\*\*", "<b><i>$1</i></b>");
            text = Regex.Replace(text, @"___([^_]+)___", "<b><i>$1</i></b>");

            // Bold
            text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "<b>$1</b>");
            text = Regex.Replace(text, @"__([^_]+)__", "<b>$1</b>");

            // Italic
            text = Regex.Replace(text, @"\*([^*]+)\*", "<i>$1</i>");
            text = Regex.Replace(text, @"_([^_]+)_", "<i>$1</i>");

            // Strikethrough - high contrast
            string strikeColor = EditorGUIUtility.isProSkin ? "#CCCCCC" : "#555555";
            text = Regex.Replace(text, @"~~([^~]+)~~", $"<color={strikeColor}><s>$1</s></color>");

            // Links [text](url) - display as colored text - high contrast
            string linkColor = EditorGUIUtility.isProSkin ? "#66BBFF" : "#0055AA";
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", $"<color={linkColor}>$1</color>");

            // Images ![alt](url) - just show alt text
            text = Regex.Replace(text, @"!\[([^\]]*)\]\([^)]+\)", "[Image: $1]");

            return text;
        }

        private void RenderCodeBlock(string code, string language)
        {
            EditorGUILayout.Space(5);
            
            // Language label if provided
            if (!string.IsNullOrEmpty(language))
            {
                GUIStyle langStyle = new GUIStyle(EditorStyles.miniLabel);
                langStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                EditorGUILayout.LabelField(language.ToUpper(), langStyle);
            }

            // Calculate height based on line count
            string[] codeLines = code.Split('\n');
            int lineCount = codeLines.Length;
            float lineHeight = 14f;
            float height = Mathf.Max(30, lineCount * lineHeight + 16);

            // Draw code block background
            Rect bgRect = EditorGUILayout.GetControlRect(GUILayout.Height(height));
            EditorGUI.DrawRect(bgRect, _codeBackgroundColor);
            
            // Draw border
            EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, 3, bgRect.height), _codeBorderColor);
            
            // Draw code text with selectable label
            Rect textRect = new Rect(bgRect.x + 10, bgRect.y + 4, bgRect.width - 20, height - 8);
            
            GUIStyle codeDisplayStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            codeDisplayStyle.normal.textColor = _codeTextColor;
            
            EditorGUI.SelectableLabel(textRect, code, codeDisplayStyle);
            
            EditorGUILayout.Space(5);
        }

        private void DrawHorizontalRule()
        {
            EditorGUILayout.Space(10);
            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(1));
            EditorGUI.DrawRect(rect, _horizontalRuleColor);
            EditorGUILayout.Space(10);
        }

        private void DrawBlockquoteBorder()
        {
            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Width(3), GUILayout.ExpandHeight(true));
            rect.height = 18;
            EditorGUI.DrawRect(rect, _blockquoteColor);
        }

        private int GetIndentLevel(string line)
        {
            int spaces = 0;
            foreach (char c in line)
            {
                if (c == ' ') spaces++;
                else if (c == '\t') spaces += 4;
                else break;
            }
            return spaces / 2;
        }
    }
}
