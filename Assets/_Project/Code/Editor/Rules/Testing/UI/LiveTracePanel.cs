using System;
using System.Collections.Generic;
using TheEmptying.Core;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Testing.UI
{
    /// <summary>
    /// While the game runs in the editor, shows live traces for the rule
    /// currently open: a toggle to switch tracing on/off for it (off by
    /// default), the last 50 segments, and this rule's paused instances and
    /// pending questions in the running game. Selecting a segment shows the
    /// same plain-language run view and canvas highlighting as the Test tab.
    /// Leaving play mode clears the view and detaches the listener.
    /// </summary>
    internal sealed class LiveTracePanel : VisualElement
    {
        private sealed class CollectingListener : IRuleTraceListener
        {
            private const int MaxSegments = 50;
            public List<RuleTraceSegment> Segments { get; } = new List<RuleTraceSegment>();

            public void OnSegmentCompleted(RuleTraceSegment segment)
            {
                Segments.Add(segment);
                while (Segments.Count > MaxSegments)
                {
                    Segments.RemoveAt(0);
                }
            }
        }

        private readonly string _ruleId;
        private readonly IReadOnlyDictionary<string, RuleTraceNodeInfo> _nodeInfo;
        private readonly Action<RuleTraceSegment> _onSegmentSelected;
        private readonly Action<string> _onStepNodeSelected;

        private Toggle _traceToggle;
        private Label _statusLabel;
        private ScrollView _listHost;
        private VisualElement _selectedDetailHost;
        private CollectingListener _listener;
        private GameBootstrap _bootstrap;
        private RuleTraceSegment _selectedSegment;

        public LiveTracePanel(string ruleId, RuleDefinition rule, BlockRegistry blocks, Action<RuleTraceSegment> onSegmentSelected, Action<string> onStepNodeSelected = null)
        {
            _ruleId = ruleId;
            _nodeInfo = RuleTraceNodeInfo.BuildMap(rule, blocks);
            _onSegmentSelected = onSegmentSelected;
            _onStepNodeSelected = onStepNodeSelected;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;
            BuildUi();

            RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.update += OnEditorUpdate);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.update -= OnEditorUpdate;
                DetachListener();
            });
        }

        private void BuildUi()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4, paddingTop = 4 } };
            _traceToggle = new Toggle("Live trace this rule");
            _traceToggle.RegisterValueChangedCallback(OnToggleChanged);
            row.Add(_traceToggle);
            Add(row);

            _statusLabel = new Label(string.Empty) { style = { paddingLeft = 4, paddingBottom = 4, color = new Color(0.75f, 0.75f, 0.75f), whiteSpace = WhiteSpace.Normal } };
            Add(_statusLabel);

            Add(new Label("Segments (most recent 50)") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 4 } });
            _listHost = new ScrollView { style = { flexGrow = 1, maxHeight = 160 } };
            Add(_listHost);

            _selectedDetailHost = new ScrollView { style = { flexGrow = 1 } };
            Add(_selectedDetailHost);
        }

        private void OnToggleChanged(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
            {
                AttachListener();
            }
            else
            {
                DetachListener();
            }
        }

        private void AttachListener()
        {
            _bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (_bootstrap == null)
            {
                _statusLabel.text = "Enter play mode (with the Game scene running) to trace live.";
                _traceToggle.SetValueWithoutNotify(false);
                return;
            }

            Simulation simulation = _bootstrap.SimulationForEditorTools;
            if (simulation?.RuleRunner == null)
            {
                _statusLabel.text = "No rule runner in the running game.";
                _traceToggle.SetValueWithoutNotify(false);
                return;
            }

            _listener = new CollectingListener();
            simulation.RuleRunner.SetTraceListener(_ruleId, _listener);
        }

        private void DetachListener()
        {
            if (_bootstrap != null)
            {
                Simulation simulation = _bootstrap.SimulationForEditorTools;
                simulation?.RuleRunner?.ClearTraceListener(_ruleId);
            }

            _listener = null;
            _bootstrap = null;
        }

        private void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying)
            {
                if (_listener != null)
                {
                    DetachListener();
                    _traceToggle.SetValueWithoutNotify(false);
                    _listHost.Clear();
                    _selectedDetailHost.Clear();
                    _selectedSegment = null;
                    _statusLabel.text = "Left play mode - live view cleared.";
                    _onSegmentSelected?.Invoke(null);
                }

                return;
            }

            if (_listener == null)
            {
                return;
            }

            RuleRunner runner = _bootstrap?.SimulationForEditorTools?.RuleRunner;
            if (runner == null)
            {
                return;
            }

            _statusLabel.text = $"Tracing. {runner.PausedInstanceCount(_ruleId)} paused instance(s), {runner.PendingQuestionCount(_ruleId)} pending question(s).";

            _listHost.Clear();
            foreach (RuleTraceSegment segment in _listener.Segments)
            {
                string label = $"{RuleTraceTextFormatter.FormatDayTime(segment.Tick)} - {RuleTraceTextFormatter.DescribeEnd(segment, _nodeInfo)}";
                var row = new Label(label) { style = { fontSize = 10, whiteSpace = WhiteSpace.Normal } };
                row.RegisterCallback<ClickEvent>(_ => SelectSegment(segment));
                _listHost.Add(row);
            }
        }

        private void SelectSegment(RuleTraceSegment segment)
        {
            _selectedSegment = segment;
            _selectedDetailHost.Clear();
            _selectedDetailHost.Add(RuleRunView.Build(segment, _nodeInfo, _onStepNodeSelected));
            _onSegmentSelected?.Invoke(segment);
        }
    }
}
