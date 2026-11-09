using System;
using System.Collections.Generic;
using TheEmptying.Core.Rules.Tracing;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Testing.UI
{
    /// <summary>
    /// Builds one segment's plain-language run view - shared by the Test and
    /// Live tabs so a run reads the same way in both: numbered steps (the
    /// trigger is step 1), each with its own "details" toggle for the field
    /// reads, computed values and node Ids a designer doesn't need by
    /// default, and a plain sentence for how it ended. Clicking a step (but
    /// not its details toggle) selects and frames that step's node.
    /// </summary>
    internal static class RuleRunView
    {
        public static VisualElement Build(RuleTraceSegment segment, IReadOnlyDictionary<string, RuleTraceNodeInfo> nodes, Action<string> onStepNodeSelected)
        {
            var box = new VisualElement
            {
                style = { paddingLeft = 4, paddingTop = 4, paddingBottom = 6, borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.3f) }
            };

            foreach (RuleRunStep step in RuleTraceTextFormatter.BuildSteps(segment, nodes))
            {
                box.Add(BuildStepRow(step, onStepNodeSelected));
            }

            box.Add(new Label(RuleTraceTextFormatter.DescribeEnd(segment, nodes))
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal, paddingTop = 2 }
            });

            return box;
        }

        private static VisualElement BuildStepRow(RuleRunStep step, Action<string> onStepNodeSelected)
        {
            var row = new VisualElement { style = { paddingTop = 1, paddingBottom = 1 } };

            var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var summaryLabel = new Label($"{step.Number}. {step.Summary}") { style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal } };
            if (step.NodeId != null && onStepNodeSelected != null)
            {
                summaryLabel.RegisterCallback<ClickEvent>(_ => onStepNodeSelected(step.NodeId));
            }

            headerRow.Add(summaryLabel);

            VisualElement detailsHost = null;
            if (step.Details.Count > 0)
            {
                var detailsToggle = new Button { text = "details", style = { fontSize = 9, height = 16, marginLeft = 4 } };
                detailsHost = new VisualElement { style = { display = DisplayStyle.None, paddingLeft = 14 } };

                foreach (string line in step.Details)
                {
                    detailsHost.Add(new Label(line) { style = { fontSize = 10, color = new Color(0.65f, 0.65f, 0.65f), whiteSpace = WhiteSpace.Normal } });
                }

                detailsToggle.clicked += () => detailsHost.style.display = detailsHost.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                headerRow.Add(detailsToggle);
            }

            row.Add(headerRow);
            if (detailsHost != null)
            {
                row.Add(detailsHost);
            }

            return row;
        }
    }
}
