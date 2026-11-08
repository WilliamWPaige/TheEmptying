using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Canvas;
using TheEmptying.Editor.Rules.Canvas.UI;
using TheEmptying.Editor.Rules.Editing;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules
{
    /// <summary>
    /// The rule editor window: browse content sources and their rule files,
    /// manage rules (new, duplicate, delete, rename, properties), edit on the
    /// canvas, see validation problems, and save with a stable byte layout.
    /// Replaces the milestone-2.5 preview window.
    /// </summary>
    public sealed class RuleEditorWindow : EditorWindow
    {
        [MenuItem("The Emptying/Rule Editor")]
        private static void Open()
        {
            var window = GetWindow<RuleEditorWindow>();
            window.titleContent = new GUIContent("Rule Editor");
            window.minSize = new Vector2(900f, 560f);
        }

        private EditorContext _context;
        private List<RuleContentSource> _sources = new List<RuleContentSource>();
        private RuleContentSource _selectedSource;

        private readonly Dictionary<string, List<RuleDefinition>> _rulesByFilePath = new Dictionary<string, List<RuleDefinition>>();
        private readonly HashSet<string> _structurallyDirtyFiles = new HashSet<string>();
        private readonly Dictionary<RuleDefinition, RuleEditorModel> _openModels = new Dictionary<RuleDefinition, RuleEditorModel>();

        private RuleFileHandle _selectedFile;
        private RuleDefinition _selectedRule;
        private RuleEditorModel _activeModel;

        private VisualElement _filesListHost;
        private VisualElement _rulesListHost;
        private VisualElement _canvasHost;
        private RuleCanvasView _canvasView;
        private VisualElement _problemsListHost;
        private Label _statusLabel;

        private TextField _idField;
        private TextField _nameField;
        private TextField _descriptionField;
        private Toggle _enabledToggle;
        private bool _suppressPropertyEvents;

        public override void SaveChanges()
        {
            SaveAllDirtyFiles();
            base.SaveChanges();
        }

        private void CreateGUI()
        {
            rootVisualElement.style.flexDirection = FlexDirection.Row;

            VisualElement leftPanel = BuildLeftPanel();
            VisualElement centerPanel = BuildCenterPanel();
            VisualElement rightPanel = BuildRightPanel();

            rootVisualElement.Add(leftPanel);
            rootVisualElement.Add(centerPanel);
            rootVisualElement.Add(rightPanel);

            rootVisualElement.RegisterCallback<KeyDownEvent>(OnWindowKeyDown, TrickleDown.TrickleDown);

            ReloadContext();
        }

        #region Layout

        private VisualElement BuildLeftPanel()
        {
            var panel = new VisualElement { style = { width = 260, borderRightWidth = 1, borderRightColor = new Color(0, 0, 0, 0.4f) } };

            var sourceRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingTop = 4, paddingBottom = 4 } };
            var refreshButton = new Button(ReloadContext) { text = "Refresh" };
            sourceRow.Add(new Label("Sources") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            sourceRow.Add(refreshButton);
            panel.Add(sourceRow);

            var sourceTabs = new VisualElement { name = "source-tabs", style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 4 } };
            panel.Add(sourceTabs);

            panel.Add(new Label("Rule files") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 4, paddingTop = 6 } });
            _filesListHost = new ScrollView { style = { height = 160, borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.4f) } };
            panel.Add(_filesListHost);

            var fileButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 2, paddingTop = 2 } };
            fileButtons.Add(new Button(OnNewFileClicked) { text = "New File" });
            fileButtons.Add(new Button(OnDeleteFileClicked) { text = "Delete" });
            fileButtons.Add(new Button(OnRenameFileClicked) { text = "Rename" });
            panel.Add(fileButtons);

            panel.Add(new Label("Rules in file") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 4, paddingTop = 6 } });
            _rulesListHost = new ScrollView { style = { flexGrow = 1 } };
            panel.Add(_rulesListHost);

            var ruleButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 2, paddingTop = 2, flexWrap = Wrap.Wrap } };
            ruleButtons.Add(new Button(OnNewRuleClicked) { text = "New Rule" });
            ruleButtons.Add(new Button(OnDuplicateRuleClicked) { text = "Duplicate" });
            ruleButtons.Add(new Button(OnDeleteRuleClicked) { text = "Delete" });
            panel.Add(ruleButtons);

            return panel;
        }

        private VisualElement BuildCenterPanel()
        {
            var panel = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };

            var propsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingTop = 4, paddingBottom = 4, alignItems = Align.Center } };
            _idField = new TextField("Id") { style = { width = 240 } };
            _nameField = new TextField("Name") { style = { width = 220 } };
            _enabledToggle = new Toggle("Enabled");
            _descriptionField = new TextField("Description") { style = { flexGrow = 1 } };

            WireRuleProperty(_idField);
            WireRuleProperty(_nameField);
            WireRuleProperty(_descriptionField);
            _enabledToggle.RegisterValueChangedCallback(_ => CommitRuleProperties());

            propsRow.Add(_idField);
            propsRow.Add(_nameField);
            propsRow.Add(_enabledToggle);
            propsRow.Add(_descriptionField);
            panel.Add(propsRow);

            _canvasHost = new VisualElement { style = { flexGrow = 1 } };
            panel.Add(_canvasHost);

            _statusLabel = new Label(string.Empty) { style = { paddingLeft = 6, paddingTop = 2, paddingBottom = 2, color = new Color(0.8f, 0.8f, 0.8f) } };
            panel.Add(_statusLabel);

            return panel;
        }

        private VisualElement BuildRightPanel()
        {
            var panel = new VisualElement { style = { width = 280, borderLeftWidth = 1, borderLeftColor = new Color(0, 0, 0, 0.4f) } };
            panel.Add(new Label("Problems") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 4, paddingTop = 4 } });
            _problemsListHost = new ScrollView { style = { flexGrow = 1 } };
            panel.Add(_problemsListHost);
            return panel;
        }

        private void WireRuleProperty(TextField field)
        {
            field.RegisterCallback<FocusInEvent>(_ => _activeModel?.BeginCombinedEdit());
            field.RegisterValueChangedCallback(_ => CommitRuleProperties());
            field.RegisterCallback<FocusOutEvent>(_ => _activeModel?.EndCombinedEdit());
        }

        private void CommitRuleProperties()
        {
            if (_activeModel == null || _suppressPropertyEvents)
            {
                return;
            }

            _activeModel.SetRuleProperties(_idField.value, _nameField.value, _descriptionField.value, _enabledToggle.value);
            RefreshFilesList();
            RefreshRulesList();
        }

        #endregion

        #region Context and sources

        private void ReloadContext()
        {
            string coreFolder = Path.Combine(Application.streamingAssetsPath, "Content", "core");
            string modsFolder = Path.Combine(Application.persistentDataPath, "Mods");

            _sources = RuleFileRepository.DiscoverSources(coreFolder, modsFolder);
            _context = EditorContext.Build(_sources.Select(s => s.FolderPath).ToList());
            _selectedSource = _sources.FirstOrDefault(s => _selectedSource != null && s.FolderPath == _selectedSource.FolderPath) ?? _sources.FirstOrDefault();

            // Load every rule file up front (not just the selected one) so Id
            // uniqueness is checked against everything loaded, not just what's
            // been browsed to so far.
            foreach (RuleContentSource source in _sources)
            {
                foreach (RuleFileHandle handle in RuleFileRepository.ListRuleFiles(source))
                {
                    EnsureFileLoaded(handle);
                }
            }

            foreach (RuleEditorModel model in _openModels.Values)
            {
                model.RebindContext(_context);
            }

            RefreshSourceTabs();
            RefreshFilesList();
            RefreshRulesList();
            RefreshCanvas();
            SetStatus("Context refreshed.");
        }

        private void RefreshSourceTabs()
        {
            VisualElement tabs = rootVisualElement.Q<VisualElement>("source-tabs");
            tabs.Clear();

            foreach (RuleContentSource source in _sources)
            {
                var button = new Button(() => SelectSource(source)) { text = source.Name };
                if (source == _selectedSource)
                {
                    button.style.unityFontStyleAndWeight = FontStyle.Bold;
                }

                tabs.Add(button);
            }
        }

        private void SelectSource(RuleContentSource source)
        {
            _selectedSource = source;
            _selectedFile = null;
            RefreshSourceTabs();
            RefreshFilesList();
            RefreshRulesList();
            RefreshCanvas();
        }

        #endregion

        #region Files

        private void RefreshFilesList()
        {
            _filesListHost.Clear();
            if (_selectedSource == null)
            {
                return;
            }

            foreach (RuleFileHandle handle in RuleFileRepository.ListRuleFiles(_selectedSource))
            {
                EnsureFileLoaded(handle);

                bool dirty = IsFileDirty(handle);
                var button = new Button(() => SelectFile(handle)) { text = (dirty ? "* " : string.Empty) + handle.FileName };
                if (_selectedFile != null && _selectedFile.FilePath == handle.FilePath)
                {
                    button.style.backgroundColor = new Color(0.25f, 0.4f, 0.6f);
                }

                _filesListHost.Add(button);
            }
        }

        private void EnsureFileLoaded(RuleFileHandle handle)
        {
            if (!_rulesByFilePath.ContainsKey(handle.FilePath))
            {
                _rulesByFilePath[handle.FilePath] = RuleFileRepository.LoadRules(handle);
            }
        }

        private bool IsFileDirty(RuleFileHandle handle)
        {
            if (_structurallyDirtyFiles.Contains(handle.FilePath))
            {
                return true;
            }

            return _rulesByFilePath.TryGetValue(handle.FilePath, out List<RuleDefinition> rules) &&
                   rules.Any(r => _openModels.TryGetValue(r, out RuleEditorModel model) && model.IsDirty);
        }

        private void SelectFile(RuleFileHandle handle)
        {
            if (_selectedFile != null && _selectedFile.FilePath != handle.FilePath && IsFileDirty(_selectedFile))
            {
                if (!EditorUtility.DisplayDialog("Unsaved changes", $"'{_selectedFile.FileName}' has unsaved changes. Switch files anyway and keep them in memory (not on disk)?", "Switch", "Cancel"))
                {
                    return;
                }
            }

            _selectedFile = handle;
            _selectedRule = null;
            EnsureFileLoaded(handle);
            RefreshFilesList();
            RefreshRulesList();
            RefreshCanvas();
        }

        private void OnNewFileClicked()
        {
            if (_selectedSource == null)
            {
                return;
            }

            TextPromptWindow.Show("New rule file", "rules.json", fileName =>
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return;
                }

                RuleFileHandle handle = RuleFileRepository.CreateFile(_selectedSource, fileName);
                _rulesByFilePath[handle.FilePath] = new List<RuleDefinition>();
                SelectFile(handle);
            });
        }

        private void OnDeleteFileClicked()
        {
            if (_selectedFile == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete file", $"Delete '{_selectedFile.FileName}' and every rule in it? This cannot be undone.", "Delete", "Cancel"))
            {
                return;
            }

            RuleFileRepository.DeleteFile(_selectedFile);
            _rulesByFilePath.Remove(_selectedFile.FilePath);
            _structurallyDirtyFiles.Remove(_selectedFile.FilePath);
            _selectedFile = null;
            _selectedRule = null;
            RefreshFilesList();
            RefreshRulesList();
            RefreshCanvas();
        }

        private void OnRenameFileClicked()
        {
            if (_selectedFile == null)
            {
                return;
            }

            TextPromptWindow.Show("Rename file", _selectedFile.FileName, newName =>
            {
                if (string.IsNullOrWhiteSpace(newName))
                {
                    return;
                }

                List<RuleDefinition> rules = _rulesByFilePath[_selectedFile.FilePath];
                _rulesByFilePath.Remove(_selectedFile.FilePath);
                RuleFileHandle renamed = RuleFileRepository.RenameFile(_selectedFile, newName);
                _rulesByFilePath[renamed.FilePath] = rules;
                _selectedFile = renamed;
                RefreshFilesList();
            });
        }

        #endregion

        #region Rules

        private IEnumerable<string> AllLoadedRuleIds(RuleDefinition excluding = null)
        {
            return _rulesByFilePath.Values.SelectMany(list => list).Where(r => r != excluding).Select(r => r.Id);
        }

        private void RefreshRulesList()
        {
            _rulesListHost.Clear();
            if (_selectedFile == null || !_rulesByFilePath.TryGetValue(_selectedFile.FilePath, out List<RuleDefinition> rules))
            {
                return;
            }

            foreach (RuleDefinition rule in rules)
            {
                bool dirty = _openModels.TryGetValue(rule, out RuleEditorModel model) && model.IsDirty;
                string label = (dirty ? "* " : string.Empty) + rule.Id + (rule.Enabled ? string.Empty : " (disabled)");
                var button = new Button(() => SelectRule(rule)) { text = label };
                if (rule == _selectedRule)
                {
                    button.style.backgroundColor = new Color(0.25f, 0.4f, 0.6f);
                }

                _rulesListHost.Add(button);
            }
        }

        private void SelectRule(RuleDefinition rule)
        {
            _selectedRule = rule;
            RefreshRulesList();
            RefreshCanvas();
        }

        private void OnNewRuleClicked()
        {
            if (_selectedFile == null)
            {
                return;
            }

            TextPromptWindow.Show("New rule", "My New Rule", name =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                string id = RuleIdValidator.GenerateUniqueId(name, AllLoadedRuleIds());
                var rule = new RuleDefinition { Id = id, Name = name, Description = string.Empty, Enabled = true, Nodes = new List<RuleNode>(), Connections = new List<RuleConnection>() };
                _rulesByFilePath[_selectedFile.FilePath].Add(rule);
                _structurallyDirtyFiles.Add(_selectedFile.FilePath);
                SelectRule(rule);
                RefreshFilesList();
            });
        }

        private void OnDuplicateRuleClicked()
        {
            if (_selectedFile == null || _selectedRule == null)
            {
                return;
            }

            string json = RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { _selectedRule });
            List<RuleDefinition> parsed = RuleJsonSerializer.DeserializeFile(json);
            RuleDefinition copy = parsed[0];
            copy.Id = RuleIdValidator.GenerateUniqueId(_selectedRule.Name + "_copy", AllLoadedRuleIds());
            copy.Name = _selectedRule.Name + " (copy)";

            _rulesByFilePath[_selectedFile.FilePath].Add(copy);
            _structurallyDirtyFiles.Add(_selectedFile.FilePath);
            SelectRule(copy);
            RefreshFilesList();
        }

        private void OnDeleteRuleClicked()
        {
            if (_selectedFile == null || _selectedRule == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete rule", $"Delete rule '{_selectedRule.Id}'? This cannot be undone.", "Delete", "Cancel"))
            {
                return;
            }

            _rulesByFilePath[_selectedFile.FilePath].Remove(_selectedRule);
            _openModels.Remove(_selectedRule);
            _structurallyDirtyFiles.Add(_selectedFile.FilePath);
            _selectedRule = null;
            RefreshRulesList();
            RefreshFilesList();
            RefreshCanvas();
        }

        #endregion

        #region Canvas and properties

        private void RefreshCanvas()
        {
            _canvasHost.Clear();
            _canvasView = null;
            _activeModel = null;

            if (_selectedRule == null || _context == null)
            {
                _idField?.SetEnabled(false);
                _nameField?.SetEnabled(false);
                _descriptionField?.SetEnabled(false);
                _enabledToggle?.SetEnabled(false);
                RefreshProblemsList();
                return;
            }

            _idField.SetEnabled(true);
            _nameField.SetEnabled(true);
            _descriptionField.SetEnabled(true);
            _enabledToggle.SetEnabled(true);

            if (!_openModels.TryGetValue(_selectedRule, out RuleEditorModel model))
            {
                model = new RuleEditorModel(_selectedRule, _context);
                _openModels[_selectedRule] = model;
            }

            _activeModel = model;
            model.Changed -= OnActiveModelChanged;
            model.Changed += OnActiveModelChanged;

            _canvasView = new RuleCanvasView(model);
            _canvasView.AddNodeMenuRequested += OnAddNodeMenuRequested;
            _canvasHost.Add(_canvasView);

            SyncPropertiesFromModel();
            RefreshProblemsList();
        }

        private void OnActiveModelChanged()
        {
            SyncPropertiesFromModel();
            RefreshProblemsList();
            RefreshRulesList();
            RefreshFilesList();
            _canvasView?.Refresh();
            hasUnsavedChanges = _rulesByFilePath.Keys.Any(path => IsFileDirty(new RuleFileHandle(_selectedSource, path)));
            SetStatus(_activeModel != null && _activeModel.LastValidation.HasErrors
                ? "This rule has compile errors - it will be saved, but won't run in game until they're fixed."
                : "No problems found.");
        }

        private void SyncPropertiesFromModel()
        {
            if (_activeModel == null)
            {
                return;
            }

            _suppressPropertyEvents = true;
            _idField.SetValueWithoutNotify(_activeModel.Rule.Id);
            _nameField.SetValueWithoutNotify(_activeModel.Rule.Name);
            _descriptionField.SetValueWithoutNotify(_activeModel.Rule.Description);
            _enabledToggle.SetValueWithoutNotify(_activeModel.Rule.Enabled);
            _suppressPropertyEvents = false;
        }

        private void RefreshProblemsList()
        {
            _problemsListHost.Clear();
            if (_activeModel?.LastValidation == null)
            {
                return;
            }

            foreach (RuleCompileProblem problem in _activeModel.LastValidation.Errors)
            {
                _problemsListHost.Add(BuildProblemRow(problem, new Color(0.9f, 0.4f, 0.4f)));
            }

            foreach (RuleCompileProblem problem in _activeModel.LastValidation.Warnings)
            {
                _problemsListHost.Add(BuildProblemRow(problem, new Color(0.9f, 0.8f, 0.3f)));
            }
        }

        private VisualElement BuildProblemRow(RuleCompileProblem problem, Color color)
        {
            var label = new Label(problem.ToString())
            {
                style = { color = color, whiteSpace = WhiteSpace.Normal, paddingLeft = 4, paddingTop = 2, paddingBottom = 2, borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.3f) }
            };

            if (problem.NodeId != null)
            {
                label.RegisterCallback<ClickEvent>(_ => _canvasView?.SelectAndFrameNode(problem.NodeId));
            }

            return label;
        }

        private void OnAddNodeMenuRequested(AddNodeMenuRequest request)
        {
            if (_activeModel == null)
            {
                return;
            }

            var families = BlockFamilyCatalog.DetectFamilies(_context.Blocks);

            Func<BlockType, bool> filter = null;
            if (request.DragSourcePort != null)
            {
                NodeViewModel sourceNode = request.DragSourceNode;
                PortViewModel sourcePort = request.DragSourcePort;
                filter = candidate => candidate.Ports.Any(p =>
                    p.Direction != sourcePort.Direction &&
                    p.Kind == sourcePort.Kind &&
                    (sourcePort.DataType == null || p.DataType == null || p.DataType == sourcePort.DataType));
            }

            var menu = new AddNodeMenu(_context.Blocks, families, filter);
            menu.BlockTypeChosen += blockType =>
            {
                string newNodeId = _activeModel.AddNode(blockType.Id, request.CanvasPosition);

                if (request.DragSourcePort != null)
                {
                    PortDefinition compatiblePort = blockType.Ports.FirstOrDefault(p =>
                        p.Direction != request.DragSourcePort.Direction &&
                        p.Kind == request.DragSourcePort.Kind &&
                        (request.DragSourcePort.DataType == null || p.DataType == null || p.DataType == request.DragSourcePort.DataType));

                    if (compatiblePort != null)
                    {
                        if (request.DragSourcePort.Direction == PortDirection.Output)
                        {
                            _activeModel.Connect(request.DragSourceNode.Node.Id, request.DragSourcePort.Name, newNodeId, compatiblePort.Name);
                        }
                        else
                        {
                            _activeModel.Connect(newNodeId, compatiblePort.Name, request.DragSourceNode.Node.Id, request.DragSourcePort.Name);
                        }
                    }
                }

                _canvasView?.Refresh();
            };

            // AdvancedDropdown.Show wants a rect in the window's own GUI space; the
            // request's position is local to the canvas element, so convert it.
            Vector2 windowPosition = _canvasView != null ? _canvasView.LocalToWorld(request.ScreenPosition) : request.ScreenPosition;
            menu.Show(new Rect(windowPosition, Vector2.zero));
        }

        #endregion

        #region Save and close

        private void OnWindowKeyDown(KeyDownEvent evt)
        {
            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.S)
            {
                if (_selectedFile != null)
                {
                    SaveFile(_selectedFile);
                }

                evt.StopPropagation();
            }
        }

        private void SaveFile(RuleFileHandle handle)
        {
            if (!_rulesByFilePath.TryGetValue(handle.FilePath, out List<RuleDefinition> rules))
            {
                return;
            }

            RuleFileRepository.SaveRules(handle, rules);
            _structurallyDirtyFiles.Remove(handle.FilePath);

            foreach (RuleDefinition rule in rules)
            {
                if (_openModels.TryGetValue(rule, out RuleEditorModel model))
                {
                    model.MarkSaved();
                }
            }

            RefreshFilesList();
            RefreshRulesList();
            hasUnsavedChanges = _rulesByFilePath.Keys.Any(path => IsFileDirty(new RuleFileHandle(_selectedSource, path)));
            SetStatus($"Saved '{handle.FileName}'.");
        }

        private void SaveAllDirtyFiles()
        {
            foreach (string path in _rulesByFilePath.Keys.ToList())
            {
                RuleContentSource source = _sources.FirstOrDefault(s => path.StartsWith(s.DataFolderPath, StringComparison.Ordinal)) ?? _selectedSource;
                var handle = new RuleFileHandle(source, path);
                if (IsFileDirty(handle))
                {
                    SaveFile(handle);
                }
            }
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
        }

        #endregion
    }
}
