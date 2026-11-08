using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Canvas;
using TheEmptying.Editor.Rules.Canvas.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules
{
    /// <summary>
    /// Temporary preview window for the rule canvas (replaced by the real
    /// editor window in prompt 2.6). Lets you pick a rule file from the core
    /// content folder and shows it on the canvas; nothing is editable or
    /// saved here.
    /// </summary>
    public sealed class RuleCanvasPreviewWindow : EditorWindow
    {
        private List<RuleDefinition> _rules = new List<RuleDefinition>();
        private BlockRegistry _blocks;
        private RuleCanvasViewModel _viewModel;
        private RuleCanvasView _canvasView;
        private DropdownField _ruleDropdown;
        private Label _statusLabel;
        private VisualElement _canvasHost;

        [MenuItem("The Emptying/Rule Canvas (Preview)")]
        private static void Open()
        {
            var window = GetWindow<RuleCanvasPreviewWindow>();
            window.titleContent = new GUIContent("Rule Canvas (Preview)");
            window.minSize = new Vector2(600f, 400f);
        }

        private void CreateGUI()
        {
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1;

            var toolbar = new Toolbar();
            _ruleDropdown = new DropdownField("Rule") { style = { minWidth = 320 } };
            _ruleDropdown.RegisterValueChangedCallback(evt => ShowRule(evt.newValue));
            toolbar.Add(_ruleDropdown);

            var reloadButton = new ToolbarButton(ReloadAndRefreshList) { text = "Reload" };
            toolbar.Add(reloadButton);
            rootVisualElement.Add(toolbar);

            _statusLabel = new Label { style = { paddingLeft = 4, paddingTop = 2, color = new Color(0.9f, 0.6f, 0.3f) } };
            rootVisualElement.Add(_statusLabel);

            _canvasHost = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(_canvasHost);

            ReloadAndRefreshList();
        }

        private void ReloadAndRefreshList()
        {
            LoadContent();

            List<string> ids = _rules.Select(r => r.Id).OrderBy(i => i, System.StringComparer.Ordinal).ToList();
            _ruleDropdown.choices = ids;

            if (ids.Count == 0)
            {
                _statusLabel.text = "No rule content found in the core content folder.";
                return;
            }

            _statusLabel.text = string.Empty;
            string current = ids.Contains(_ruleDropdown.value) ? _ruleDropdown.value : ids[0];
            _ruleDropdown.SetValueWithoutNotify(current);
            ShowRule(current);
        }

        private void LoadContent()
        {
            string coreFolder = Path.Combine(Application.streamingAssetsPath, "Content", "core");

            var contentTypes = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(contentTypes);
            var loader = new ContentLoader(contentTypes, ErrorMode.Development);
            (ContentDatabase database, _) = loader.Load(new[] { coreFolder });

            _rules = database.ListByType(BuiltInContentTypes.RuleTypeName).Select(e => (RuleDefinition)e.Definition).ToList();
            _blocks = EditorBlockCatalog.Build(new[] { coreFolder });
        }

        private void ShowRule(string ruleId)
        {
            RuleDefinition rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null)
            {
                return;
            }

            if (_viewModel == null)
            {
                _viewModel = new RuleCanvasViewModel(rule, _blocks);
                _canvasView = new RuleCanvasView(_viewModel);
                _canvasHost.Add(_canvasView);
            }
            else
            {
                _viewModel.Load(rule, _blocks);
                _canvasView.Refresh();
            }
        }
    }
}
