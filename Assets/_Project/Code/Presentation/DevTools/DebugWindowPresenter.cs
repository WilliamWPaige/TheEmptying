using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>
    /// The debug window: an Errors tab (every collected Unity warning, error
    /// and exception) and a Cheats tab (built from CheatMenuRegistry's
    /// sections). F1 toggles it; it starts hidden in the Editor and in
    /// builds alike. Never blocks gameplay keys except while one of its own
    /// text fields is actively focused for typing (see UiInputUtility).
    /// F1 (ToggleWindow) bypasses that check entirely, so a stuck focus
    /// check can never trap the player. Escape is handled by
    /// GameBootstrap, which closes this window (via ToggleWindow) before
    /// anything else gets a turn - see its HandleEscapeKeyPressed.
    /// </summary>
    public sealed class DebugWindowPresenter : MonoBehaviour
    {
        private enum ErrorFilter
        {
            All,
            ErrorsOnly,
            WarningsOnly
        }

        /// <summary>One live row's control plus the UI element(s) its value is pushed into each frame - built once, never recreated just to show a new value.</summary>
        private sealed class CheatRowBinding
        {
            public CheatControl Control;
            public Toggle ToggleElement;
            public FloatField NumberField;
        }

        /// <summary>R9: one section's title label plus its rows, kept so the search box can show/hide them without rebuilding the tree.</summary>
        private sealed class CheatSectionBinding
        {
            public string Title;
            public Label TitleLabel;
            public readonly List<(CheatControl Control, VisualElement Row)> Rows = new List<(CheatControl Control, VisualElement Row)>();
        }

        private VisualElement _uiRoot;
        private CheatMenuRegistry _cheatMenu;

        private VisualElement _windowRoot;
        private Button _errorsTabButton;
        private Button _cheatsTabButton;
        private VisualElement _errorsPanel;
        private VisualElement _cheatsPanel;
        private Button _filterAllButton;
        private Button _filterErrorsButton;
        private Button _filterWarningsButton;
        private Button _clearButton;
        private Button _saveButton;
        private Label _saveResultLabel;
        private ScrollView _errorsList;
        private ScrollView _cheatsList;
        private TextField _cheatsSearchField;

        private ErrorFilter _filter = ErrorFilter.All;
        private readonly HashSet<DebugLogEntry> _expandedEntries = new HashSet<DebugLogEntry>();
        private readonly List<CheatRowBinding> _cheatBindings = new List<CheatRowBinding>();
        private readonly List<CheatSectionBinding> _cheatSectionBindings = new List<CheatSectionBinding>();

        /// <summary>R9: whether this window was open, and on which tab, right before the Escape menu force-closed it - so ForceReopen can restore exactly that (or do nothing, if it wasn't open).</summary>
        private bool _wasOpenBeforeModalClose;
        private bool _wasShowingErrorsTabBeforeModalClose;

        public bool IsOpen => _windowRoot != null && _windowRoot.style.display == DisplayStyle.Flex;

        public void Initialize(VisualElement uiRoot, CheatMenuRegistry cheatMenu)
        {
            _uiRoot = uiRoot;
            _cheatMenu = cheatMenu;
            BuildUi();
            RebuildCheatsStructure();

            ErrorCatcher.Changed += OnErrorsChanged;
        }

        private void OnDestroy()
        {
            ErrorCatcher.Changed -= OnErrorsChanged;
        }

        private void Update()
        {
            if (IsOpen && _cheatsPanel.style.display == DisplayStyle.Flex)
            {
                RefreshCheatValuesInPlace();
            }
        }

        /// <summary>F1's entry point. Never gated by the focus check - see class remarks.</summary>
        public void ToggleWindow()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open(showErrorsTab: true);
            }
        }

        /// <summary>The HUD badge opens the window on the Errors tab; new errors never do this on their own.</summary>
        public void OpenOnErrorsTab()
        {
            Open(showErrorsTab: true);
        }

        /// <summary>R9: the Escape menu is modal - it force-closes this window while open, remembering whether it was open (and which tab) so Close() can restore it.</summary>
        public void ForceClose()
        {
            _wasOpenBeforeModalClose = IsOpen;
            if (!_wasOpenBeforeModalClose)
            {
                return;
            }

            _wasShowingErrorsTabBeforeModalClose = _errorsPanel.style.display == DisplayStyle.Flex;
            Close();
        }

        /// <summary>R9: undoes ForceClose once the Escape menu closes; a no-op if the window wasn't open when it was force-closed.</summary>
        public void ForceReopen()
        {
            if (!_wasOpenBeforeModalClose)
            {
                return;
            }

            _wasOpenBeforeModalClose = false;
            Open(_wasShowingErrorsTabBeforeModalClose);
        }

        private void Open(bool showErrorsTab)
        {
            _windowRoot.style.display = DisplayStyle.Flex;
            SwitchTab(showErrorsTab);
        }

        private void Close()
        {
            _windowRoot.style.display = DisplayStyle.None;
            ClearFocusIfInsideWindow();
        }

        /// <summary>Closing the window always clears focus from anything inside it - never from a HUD control outside it.</summary>
        private void ClearFocusIfInsideWindow()
        {
            if (!(_uiRoot?.panel?.focusController?.focusedElement is VisualElement focused))
            {
                return;
            }

            for (VisualElement current = focused; current != null; current = current.parent)
            {
                if (current == _windowRoot)
                {
                    focused.Blur();
                    return;
                }
            }
        }

        private void OnErrorsChanged()
        {
            if (IsOpen && _errorsPanel.style.display == DisplayStyle.Flex)
            {
                RefreshErrorsList();
            }
        }

        #region Building the shell

        private void BuildUi()
        {
            var asset = Resources.Load<VisualTreeAsset>("DebugWindow");
            if (asset == null)
            {
                UnityEngine.Debug.LogError("[TheEmptying] Could not load DebugWindow from Resources. The debug window will not render.");
                return;
            }

            asset.CloneTree(_uiRoot);

            _windowRoot = _uiRoot.Q<VisualElement>("debug-window-root");
            _errorsTabButton = _uiRoot.Q<Button>("debug-tab-errors-button");
            _cheatsTabButton = _uiRoot.Q<Button>("debug-tab-cheats-button");
            _errorsPanel = _uiRoot.Q<VisualElement>("debug-errors-panel");
            _cheatsPanel = _uiRoot.Q<VisualElement>("debug-cheats-panel");
            _filterAllButton = _uiRoot.Q<Button>("debug-errors-filter-all");
            _filterErrorsButton = _uiRoot.Q<Button>("debug-errors-filter-errors");
            _filterWarningsButton = _uiRoot.Q<Button>("debug-errors-filter-warnings");
            _clearButton = _uiRoot.Q<Button>("debug-errors-clear");
            _saveButton = _uiRoot.Q<Button>("debug-errors-save");
            _saveResultLabel = _uiRoot.Q<Label>("debug-errors-save-result");
            _errorsList = _uiRoot.Q<ScrollView>("debug-errors-list");
            _cheatsList = _uiRoot.Q<ScrollView>("debug-cheats-list");
            _cheatsSearchField = _uiRoot.Q<TextField>("debug-cheats-search");
            _cheatsSearchField.RegisterValueChangedCallback(_ => ApplyCheatsSearchFilter());

            _errorsTabButton.clicked += () => SwitchTab(true);
            _cheatsTabButton.clicked += () => SwitchTab(false);

            _filterAllButton.clicked += () => SetFilter(ErrorFilter.All);
            _filterErrorsButton.clicked += () => SetFilter(ErrorFilter.ErrorsOnly);
            _filterWarningsButton.clicked += () => SetFilter(ErrorFilter.WarningsOnly);
            _clearButton.clicked += () =>
            {
                ErrorCatcher.Clear();
                RefreshErrorsList();
            };
            _saveButton.clicked += SaveToFile;

            _windowRoot.style.display = DisplayStyle.None;
            SwitchTab(true);
        }

        private void SwitchTab(bool showErrors)
        {
            _errorsPanel.style.display = showErrors ? DisplayStyle.Flex : DisplayStyle.None;
            _cheatsPanel.style.display = showErrors ? DisplayStyle.None : DisplayStyle.Flex;
            _errorsTabButton.EnableInClassList("debug-tab-button-active", showErrors);
            _cheatsTabButton.EnableInClassList("debug-tab-button-active", !showErrors);

            if (showErrors)
            {
                RefreshErrorsList();
            }
            else
            {
                RefreshCheatValuesInPlace();
            }
        }

        private void SetFilter(ErrorFilter filter)
        {
            _filter = filter;
            _filterAllButton.EnableInClassList("debug-tab-button-active", filter == ErrorFilter.All);
            _filterErrorsButton.EnableInClassList("debug-tab-button-active", filter == ErrorFilter.ErrorsOnly);
            _filterWarningsButton.EnableInClassList("debug-tab-button-active", filter == ErrorFilter.WarningsOnly);
            RefreshErrorsList();
        }

        #endregion

        #region Errors tab

        private void RefreshErrorsList()
        {
            if (_errorsList == null)
            {
                return;
            }

            _errorsList.Clear();

            IEnumerable<DebugLogEntry> entries = ErrorCatcher.Entries;
            entries = _filter switch
            {
                ErrorFilter.ErrorsOnly => entries.Where(e => e.Type != DebugLogType.Warning),
                ErrorFilter.WarningsOnly => entries.Where(e => e.Type == DebugLogType.Warning),
                _ => entries
            };

            foreach (DebugLogEntry entry in entries)
            {
                _errorsList.Add(BuildErrorRow(entry));
            }
        }

        private VisualElement BuildErrorRow(DebugLogEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("debug-log-entry");

            var header = new VisualElement();
            header.AddToClassList("debug-log-entry-header");
            if (entry.Type != DebugLogType.Warning)
            {
                header.AddToClassList("debug-log-entry-error");
            }
            else
            {
                header.AddToClassList("debug-log-entry-warning");
            }

            string countSuffix = entry.Count > 1 ? $" (x{entry.Count})" : string.Empty;
            var headerLabel = new Label($"{entry.RealTimestamp:HH:mm:ss} | {entry.InGameTimestamp} | {entry.Type.ToString().ToUpperInvariant()} | {entry.Message}{countSuffix}");
            headerLabel.style.whiteSpace = WhiteSpace.Normal;
            header.Add(headerLabel);

            var stackLabel = new Label(entry.FullStackTrace);
            stackLabel.AddToClassList("debug-log-entry-stack");

            header.RegisterCallback<ClickEvent>(_ =>
            {
                bool expanded = _expandedEntries.Contains(entry);
                if (expanded)
                {
                    _expandedEntries.Remove(entry);
                }
                else
                {
                    _expandedEntries.Add(entry);
                }

                stackLabel.EnableInClassList("debug-log-entry-stack-visible", !expanded);
            });

            stackLabel.EnableInClassList("debug-log-entry-stack-visible", _expandedEntries.Contains(entry));

            row.Add(header);
            row.Add(stackLabel);
            return row;
        }

        private void SaveToFile()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (DebugLogEntry entry in ErrorCatcher.Entries)
                {
                    string countSuffix = entry.Count > 1 ? $" (x{entry.Count})" : string.Empty;
                    sb.AppendLine($"{entry.RealTimestamp:yyyy-MM-dd HH:mm:ss} | {entry.InGameTimestamp} | {entry.Type}{countSuffix}");
                    sb.AppendLine(entry.Message);
                    if (!string.IsNullOrEmpty(entry.FullStackTrace))
                    {
                        sb.AppendLine(entry.FullStackTrace);
                    }

                    sb.AppendLine();
                }

                string fileName = $"errors_{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.txt";
                string logsFolder = Path.Combine(Application.dataPath, "..", "Logs");
                Directory.CreateDirectory(logsFolder);
                File.WriteAllText(Path.Combine(logsFolder, fileName), sb.ToString());

                _saveResultLabel.text = $"Saved to {fileName}";
            }
            catch (Exception ex)
            {
                _saveResultLabel.text = $"Save failed: {ex.Message}";
            }
        }

        #endregion

        #region Cheats tab

        /// <summary>
        /// Builds the Cheats tab's rows once. Call again only when
        /// CheatMenuRegistry's sections themselves change (the simulation
        /// was rebuilt by a new game or a load) - never on a timer or just
        /// to reflect a changed value, which RefreshCheatValuesInPlace does
        /// without touching the tree.
        /// </summary>
        public void RebuildCheatsStructure()
        {
            if (_cheatsList == null || _cheatMenu == null)
            {
                return;
            }

            _cheatsList.Clear();
            _cheatBindings.Clear();
            _cheatSectionBindings.Clear();

            foreach (CheatSection section in _cheatMenu.Sections)
            {
                var titleLabel = new Label(section.Title);
                titleLabel.AddToClassList("debug-cheat-section-title");
                _cheatsList.Add(titleLabel);

                var sectionBinding = new CheatSectionBinding { Title = section.Title, TitleLabel = titleLabel };

                foreach (CheatControl control in section.Controls)
                {
                    VisualElement row = BuildCheatRow(control);
                    _cheatsList.Add(row);
                    sectionBinding.Rows.Add((control, row));
                }

                _cheatSectionBindings.Add(sectionBinding);
            }

            RefreshCheatValuesInPlace();
            ApplyCheatsSearchFilter();
        }

        /// <summary>
        /// R9: hides every row (and, if none of its rows match, the whole
        /// section) whose label, Id and section title all fail to contain
        /// the search text. An empty search shows everything.
        /// </summary>
        private void ApplyCheatsSearchFilter()
        {
            string query = _cheatsSearchField?.value?.Trim() ?? string.Empty;

            foreach (CheatSectionBinding section in _cheatSectionBindings)
            {
                bool sectionTitleMatches = query.Length == 0 || Contains(section.Title, query);
                bool anyRowVisible = false;

                foreach ((CheatControl control, VisualElement row) in section.Rows)
                {
                    bool rowMatches = sectionTitleMatches || Contains(control.Label, query) || Contains(control.Id, query);
                    row.style.display = rowMatches ? DisplayStyle.Flex : DisplayStyle.None;
                    anyRowVisible |= rowMatches;
                }

                section.TitleLabel.style.display = anyRowVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Pushes each control's current value into its already-built UI
        /// element - never clears or recreates anything. Skips exactly the
        /// one number field the player is currently typing in; everything
        /// else (including every other number field) still updates.
        /// </summary>
        private void RefreshCheatValuesInPlace()
        {
            Focusable focused = _uiRoot?.panel?.focusController?.focusedElement;

            foreach (CheatRowBinding binding in _cheatBindings)
            {
                if (binding.ToggleElement != null)
                {
                    binding.ToggleElement.SetValueWithoutNotify(binding.Control.GetToggleValue());
                }
                else if (binding.NumberField != null && !IsFocusedWithin(binding.NumberField, focused))
                {
                    binding.NumberField.SetValueWithoutNotify((float)binding.Control.GetNumberValue());
                }
            }
        }

        private static bool IsFocusedWithin(VisualElement container, Focusable focused)
        {
            if (!(focused is VisualElement element))
            {
                return false;
            }

            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current == container)
                {
                    return true;
                }
            }

            return false;
        }

        private VisualElement BuildCheatRow(CheatControl control)
        {
            var row = new VisualElement();
            row.AddToClassList("debug-cheat-row");

            if (!string.IsNullOrEmpty(control.Tooltip))
            {
                row.tooltip = control.Tooltip;
            }

            switch (control.Kind)
            {
                case CheatControlKind.Button:
                    row.Add(new Button(control.OnClick) { text = control.Label });
                    break;

                case CheatControlKind.Toggle:
                {
                    var toggle = new Toggle(control.Label) { value = control.GetToggleValue() };
                    toggle.RegisterValueChangedCallback(evt => control.SetToggleValue(evt.newValue));
                    row.Add(toggle);
                    _cheatBindings.Add(new CheatRowBinding { Control = control, ToggleElement = toggle });
                    AddResetButtonIfAny(row, control);
                    break;
                }

                case CheatControlKind.Number:
                {
                    var label = new Label(control.Label);
                    label.AddToClassList("debug-cheat-label");
                    row.Add(label);

                    var field = new FloatField { value = (float)control.GetNumberValue() };
                    field.RegisterCallback<FocusOutEvent>(_ => CommitNumber(control, field));
                    field.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        {
                            CommitNumber(control, field);
                            field.Blur();
                        }
                    });
                    row.Add(field);
                    _cheatBindings.Add(new CheatRowBinding { Control = control, NumberField = field });

                    if (control.Minimum.HasValue || control.Maximum.HasValue)
                    {
                        var caption = new Label($"({control.Minimum ?? double.NegativeInfinity}..{control.Maximum ?? double.PositiveInfinity})");
                        caption.AddToClassList("debug-cheat-caption");
                        row.Add(caption);
                    }

                    AddResetButtonIfAny(row, control);
                    break;
                }
            }

            return row;
        }

        private static void CommitNumber(CheatControl control, FloatField field)
        {
            double value = field.value;
            if (control.Minimum.HasValue)
            {
                value = Math.Max(value, control.Minimum.Value);
            }

            if (control.Maximum.HasValue)
            {
                value = Math.Min(value, control.Maximum.Value);
            }

            field.SetValueWithoutNotify((float)value);
            control.SetNumberValue(value);
        }

        private static void AddResetButtonIfAny(VisualElement row, CheatControl control)
        {
            if (control.OnReset == null)
            {
                return;
            }

            var resetButton = new Button(control.OnReset) { text = "Reset" };
            resetButton.AddToClassList("debug-cheat-reset-button");
            row.Add(resetButton);
        }

        #endregion
    }
}
