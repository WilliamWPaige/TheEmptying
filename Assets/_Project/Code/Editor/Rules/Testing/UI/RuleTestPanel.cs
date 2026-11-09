using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Editor.Rules.Editing;
using TheEmptying.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Testing.UI
{
    /// <summary>
    /// The rule editor's Test panel: builds and drives a RuleTestSession for
    /// the rule currently open, and shows the resulting run (trace steps,
    /// events, log lines, locals, waiting/asking state). Never touches files
    /// or a running game - everything happens in the session's own
    /// throwaway simulation.
    /// </summary>
    internal sealed class RuleTestPanel : VisualElement
    {
        private sealed class ParamRow
        {
            public VisualElement Root;
            public TextField NameField;
            public DropdownField TypeField;
            public TextField TextValueField;
            public Toggle BoolValueField;

            public (string Name, RuleDataType Type, object Value) ToParameter()
            {
                RuleDataType type = TypeField.value switch
                {
                    "Bool" => RuleDataType.Bool,
                    "Text" => RuleDataType.Text,
                    _ => RuleDataType.Number
                };

                object value = type switch
                {
                    RuleDataType.Bool => (object)BoolValueField.value,
                    RuleDataType.Text => TextValueField.value,
                    _ => double.TryParse(TextValueField.value, out double n) ? n : 0.0
                };

                return (NameField.value, type, value);
            }
        }

        private readonly RuleEditorModel _model;
        private readonly Action<RuleTraceSegment> _onSegmentSelected;
        private readonly RuleTestSession _session = new RuleTestSession();

        private IntegerField _seedField;
        private Toggle _isolatedToggle;
        private Label _statusLabel;

        private VisualElement _fireHost;
        private readonly List<ParamRow> _paramRows = new List<ParamRow>();
        private TextField _eventIdField;

        private ScrollView _runViewHost;

        public RuleTestPanel(RuleEditorModel model, Action<RuleTraceSegment> onSegmentSelected = null)
        {
            _model = model;
            _onSegmentSelected = onSegmentSelected;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;
            BuildUi();
        }

        private void BuildUi()
        {
            var optionsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingTop = 4, alignItems = Align.Center } };
            _seedField = new IntegerField("Seed") { value = 1, style = { width = 120 } };
            _isolatedToggle = new Toggle("Isolated") { value = true };
            optionsRow.Add(_seedField);
            optionsRow.Add(_isolatedToggle);
            Add(optionsRow);

            var buttonsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingTop = 2, paddingBottom = 4 } };
            buttonsRow.Add(new Button(OnBuildClicked) { text = "Build" });
            buttonsRow.Add(new Button(OnClearClicked) { text = "Clear" });
            Add(buttonsRow);

            _statusLabel = new Label("Not built.") { style = { paddingLeft = 4, paddingBottom = 4, color = new Color(0.75f, 0.75f, 0.75f), whiteSpace = WhiteSpace.Normal } };
            Add(_statusLabel);

            _fireHost = new VisualElement { style = { paddingLeft = 4, borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.3f), paddingBottom = 4 } };
            Add(_fireHost);

            Add(new Label("Run") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 4, paddingTop = 4 } });
            _runViewHost = new ScrollView { style = { flexGrow = 1 } };
            Add(_runViewHost);

            RefreshFireControls();
        }

        private void OnBuildClicked()
        {
            IReadOnlyList<string> sources = _model.Context.SourceFolders;
            _session.Build(_model.Rule, sources, ModuleCatalog.CreateModules(), _isolatedToggle.value, (ulong)Math.Max(1, _seedField.value));
            RefreshFireControls();
            RefreshStatus();
            RefreshRunView();
        }

        private void OnClearClicked()
        {
            if (!_session.IsBuilt)
            {
                return;
            }

            _session.Clear();
            RefreshStatus();
            RefreshRunView();
        }

        private void RefreshStatus()
        {
            if (!_session.IsBuilt)
            {
                _statusLabel.text = "Not built.";
                return;
            }

            if (_session.LastStartupReport.RuleErrors.Count > 0)
            {
                _statusLabel.text = "Built, but this rule has compile errors - it will not run:\n" + string.Join("\n", _session.LastStartupReport.RuleErrors);
                return;
            }

            _statusLabel.text = $"Built (seed {_session.Seed}, {(_session.Isolated ? "isolated" : "all enabled rules")}). Tick {_session.Simulation.Clock.CurrentTick}.";
        }

        private BlockType FindTriggerBlockType()
        {
            foreach (RuleNode node in _model.Rule.Nodes)
            {
                if (_model.Context.Blocks.TryGet(node.BlockTypeId, out BlockType blockType) && blockType.Kind == BlockKind.Trigger)
                {
                    return blockType;
                }
            }

            return null;
        }

        private void RefreshFireControls()
        {
            _fireHost.Clear();
            _paramRows.Clear();

            BlockType trigger = FindTriggerBlockType();
            if (trigger == null)
            {
                _fireHost.Add(new Label("This rule has no trigger yet."));
                return;
            }

            if (!_session.IsBuilt)
            {
                _fireHost.Add(new Label("Build first."));
                return;
            }

            if (trigger.Id == "block.trigger.on_event")
            {
                BuildEventFireControls();
            }
            else
            {
                var fireNowRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                fireNowRow.Add(new Label("Time trigger - "));
                fireNowRow.Add(new Button(() =>
                {
                    _session.FireNow();
                    RefreshStatus();
                    RefreshRunView();
                }) { text = "Fire now" });
                _fireHost.Add(fireNowRow);
            }

            BuildWaitingAndAskingControls();
        }

        private void BuildEventFireControls()
        {
            RuleNode triggerNode = _model.Rule.Nodes.First(n => _model.Context.Blocks.TryGet(n.BlockTypeId, out BlockType bt) && bt.Kind == BlockKind.Trigger);
            string defaultEventId = triggerNode.Fields != null && triggerNode.Fields.TryGetValue("event", out RuleFieldValueSource source) && source != null && !source.IsSettingReference
                ? source.Literal?.ToString() ?? string.Empty
                : string.Empty;

            _eventIdField = new TextField("Event") { value = defaultEventId };
            _fireHost.Add(_eventIdField);

            var paramsHost = new VisualElement();
            _fireHost.Add(paramsHost);

            var addRowButton = new Button(() => AddParamRow(paramsHost)) { text = "+ parameter" };
            _fireHost.Add(addRowButton);

            var fireRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 4 } };
            fireRow.Add(new Button(() =>
            {
                var parameters = _paramRows.Where(r => !string.IsNullOrEmpty(r.NameField.value)).Select(r => r.ToParameter()).ToList();
                _session.FireEvent(_eventIdField.value, parameters);
                RefreshStatus();
                RefreshRunView();
            }) { text = "Fire" });
            _fireHost.Add(fireRow);
        }

        private void AddParamRow(VisualElement host)
        {
            var row = new ParamRow { Root = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } } };

            row.NameField = new TextField("Name") { style = { width = 160 } };

            row.TypeField = new DropdownField(new List<string> { "Number", "Bool", "Text" }, 0) { style = { width = 70 } };

            row.TextValueField = new TextField { style = { width = 100 } };
            row.BoolValueField = new Toggle { style = { display = DisplayStyle.None } };

            row.TypeField.RegisterValueChangedCallback(evt =>
            {
                bool isBool = evt.newValue == "Bool";
                row.TextValueField.style.display = isBool ? DisplayStyle.None : DisplayStyle.Flex;
                row.BoolValueField.style.display = isBool ? DisplayStyle.Flex : DisplayStyle.None;
            });

            var removeButton = new Button(() =>
            {
                host.Remove(row.Root);
                _paramRows.Remove(row);
            })
            { text = "x", style = { width = 20 } };

            row.Root.Add(row.NameField);
            row.Root.Add(row.TypeField);
            row.Root.Add(row.TextValueField);
            row.Root.Add(row.BoolValueField);
            row.Root.Add(removeButton);

            host.Add(row.Root);
            _paramRows.Add(row);
        }

        private void BuildWaitingAndAskingControls()
        {
            if (!_session.IsBuilt)
            {
                return;
            }

            if (_session.TryGetResumeTick(out long resumeTick))
            {
                var waitRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingTop = 4 } };
                waitRow.Add(new Label($"Waiting until tick {resumeTick}. "));
                waitRow.Add(new Button(() =>
                {
                    _session.SkipToResume();
                    RefreshStatus();
                    RefreshFireControls();
                    RefreshRunView();
                }) { text = "Skip ahead" });
                _fireHost.Add(waitRow);
            }

            foreach (QuestionInfo question in _session.PendingQuestionsForRule())
            {
                var questionBox = new VisualElement { style = { paddingTop = 4, borderTopWidth = 1, borderTopColor = new Color(0, 0, 0, 0.3f) } };
                questionBox.Add(new Label(question.Title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                questionBox.Add(new Label(question.Body) { style = { whiteSpace = WhiteSpace.Normal } });

                var choicesRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
                foreach ((int choiceNumber, string text) in question.Choices)
                {
                    long questionId = question.QuestionId;
                    choicesRow.Add(new Button(() =>
                    {
                        _session.AnswerQuestion(questionId, choiceNumber);
                        RefreshStatus();
                        RefreshFireControls();
                        RefreshRunView();
                    })
                    { text = text });
                }

                questionBox.Add(choicesRow);
                _fireHost.Add(questionBox);
            }
        }

        private void RefreshRunView()
        {
            _runViewHost.Clear();

            if (!_session.IsBuilt)
            {
                return;
            }

            foreach (RuleTraceSegment segment in _session.Segments)
            {
                _runViewHost.Add(BuildSegmentView(segment, _onSegmentSelected));
            }

            _onSegmentSelected?.Invoke(_session.Segments.Count > 0 ? _session.Segments[^1] : null);
        }

        private static VisualElement BuildSegmentView(RuleTraceSegment segment, Action<RuleTraceSegment> onSelected)
        {
            var box = new VisualElement
            {
                style = { paddingLeft = 4, paddingTop = 4, paddingBottom = 4, borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.3f) }
            };

            if (onSelected != null)
            {
                box.RegisterCallback<ClickEvent>(_ => onSelected(segment));
            }

            box.Add(new Label($"Tick {segment.Tick} - {DescribeStart(segment)} - ended: {DescribeEnd(segment)}")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal }
            });

            foreach (RuleTraceEntry entry in segment.Entries)
            {
                box.Add(new Label(entry.ToString()) { style = { fontSize = 10, color = new Color(0.8f, 0.8f, 0.8f), whiteSpace = WhiteSpace.Normal } });
            }

            return box;
        }

        private static string DescribeStart(RuleTraceSegment segment)
        {
            return segment.StartKind switch
            {
                RuleTraceStartKind.EventTrigger => $"event '{segment.StartEventDefinitionId}'",
                RuleTraceStartKind.ScheduleTrigger => "schedule",
                RuleTraceStartKind.Resume => $"resume (due at {segment.StartResumeTick})",
                RuleTraceStartKind.Answer => $"answer to question {segment.StartQuestionId} (choice {segment.StartChoiceNumber})",
                _ => segment.StartKind.ToString()
            };
        }

        private static string DescribeEnd(RuleTraceSegment segment)
        {
            return segment.EndKind switch
            {
                RuleTraceEndKind.Waiting => $"waiting until tick {segment.EndResumeTick}",
                RuleTraceEndKind.Asking => $"asking question {segment.EndQuestionId}",
                RuleTraceEndKind.Error => $"error: {segment.EndErrorMessage}",
                _ => segment.EndKind.ToString()
            };
        }
    }
}
