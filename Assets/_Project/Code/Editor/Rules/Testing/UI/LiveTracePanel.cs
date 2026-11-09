using System;
using System.Collections.Generic;
using System.Linq;
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
    /// pending questions in the running game. Leaving play mode clears the
    /// view and detaches the listener.
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
        private readonly Action<RuleTraceSegment> _onSegmentSelected;

        private Toggle _traceToggle;
        private Label _statusLabel;
        private ScrollView _listHost;
        private CollectingListener _listener;
        private GameBootstrap _bootstrap;

        public LiveTracePanel(string ruleId, Action<RuleTraceSegment> onSegmentSelected)
        {
            _ruleId = ruleId;
            _onSegmentSelected = onSegmentSelected;
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
            _listHost = new ScrollView { style = { flexGrow = 1 } };
            Add(_listHost);
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
                    _statusLabel.text = "Left play mode - live view cleared.";
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
                var row = new Label($"Tick {segment.Tick} - ended: {segment.EndKind}") { style = { fontSize = 10, whiteSpace = WhiteSpace.Normal } };
                row.RegisterCallback<ClickEvent>(_ => _onSegmentSelected?.Invoke(segment));
                _listHost.Add(row);
            }
        }
    }
}
