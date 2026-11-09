using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Editor.Rules.Testing;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.7b: the Test/Live tabs' plain-language run view. A designer, not a programmer, must be able to read it - numbered steps, the trigger as step 1, day/time instead of ticks, one plain sentence for how it ended.</summary>
    public class RuleTraceTextFormatterTests
    {
        private static Dictionary<string, RuleTraceNodeInfo> Nodes(params (string Id, string DisplayName, string BlockTypeId)[] entries)
        {
            var map = new Dictionary<string, RuleTraceNodeInfo>();
            foreach ((string id, string displayName, string blockTypeId) in entries)
            {
                map[id] = new RuleTraceNodeInfo(displayName, blockTypeId);
            }

            return map;
        }

        private static RuleTraceSegment Segment(
            RuleTraceStartKind startKind, string startEventId, IReadOnlyList<RuleTraceEntry> entries, RuleTraceEndKind endKind,
            long endResumeTick = 0, long endQuestionId = 0, string endErrorMessage = null, string endNodeId = null,
            long startResumeTick = 0, long startQuestionId = 0, int startChoiceNumber = 0)
        {
            return new RuleTraceSegment(
                "rule.test", 1, 0,
                startKind, startEventId, Array.Empty<(string, string)>(), startResumeTick, startQuestionId, startChoiceNumber,
                entries, endKind, endResumeTick, endQuestionId, endErrorMessage, endNodeId);
        }

        [Test]
        public void TriggerIsStepOne_AndWriteLogLine_ShowsWhatItWrote()
        {
            var entries = new List<RuleTraceEntry>
            {
                RuleTraceEntry.NodeExecuted("trigger", "then"),
                RuleTraceEntry.InputRead("log", "text", RuleValue.Text("hi")),
                RuleTraceEntry.LineLogged("log", "hi"),
                RuleTraceEntry.NodeExecuted("log", "then")
            };
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", entries, RuleTraceEndKind.Ended);
            var nodes = Nodes(("trigger", "On Event", "block.trigger.on_event"), ("log", "Write Log Line", "block.action.write_log_line"));

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual(2, steps.Count);
            Assert.AreEqual(1, steps[0].Number);
            Assert.AreEqual("trigger", steps[0].NodeId);
            Assert.AreEqual("On Event fired: go", steps[0].Summary);

            Assert.AreEqual(2, steps[1].Number);
            Assert.AreEqual("log", steps[1].NodeId);
            Assert.AreEqual("Write Log Line wrote: hi", steps[1].Summary);
            Assert.IsTrue(steps[1].Details.Count > 0, "field reads and raw values must still be available, just behind details.");

            Assert.AreEqual("Finished.", RuleTraceTextFormatter.DescribeEnd(segment, nodes));
        }

        [Test]
        public void Branch_ShowsWhichOutputItWent()
        {
            var entries = new List<RuleTraceEntry> { RuleTraceEntry.NodeExecuted("trigger", "then"), RuleTraceEntry.NodeExecuted("branch", "true") };
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", entries, RuleTraceEndKind.Ended);
            var nodes = Nodes(("trigger", "On Event", "block.trigger.on_event"), ("branch", "Branch", "block.flow.branch"));

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual("Branch went: true", steps[1].Summary);
        }

        [Test]
        public void PostEvent_ShowsWhichEventItPosted()
        {
            var entries = new List<RuleTraceEntry>
            {
                RuleTraceEntry.NodeExecuted("trigger", "then"),
                RuleTraceEntry.EventPosted("post", "event.herald_line", Array.Empty<(string, RuleValue)>()),
                RuleTraceEntry.NodeExecuted("post", "then")
            };
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", entries, RuleTraceEndKind.Ended);
            var nodes = Nodes(("trigger", "On Event", "block.trigger.on_event"), ("post", "Post Event", "block.action.post_event"));

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual("Post Event posted event.herald_line", steps[1].Summary);
        }

        [Test]
        public void SetLocal_ShowsNameAndValue()
        {
            var entries = new List<RuleTraceEntry>
            {
                RuleTraceEntry.NodeExecuted("trigger", "then"),
                RuleTraceEntry.LocalSet("set", "gold", RuleValue.Number(5)),
                RuleTraceEntry.NodeExecuted("set", "then")
            };
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", entries, RuleTraceEndKind.Ended);
            var nodes = Nodes(("trigger", "On Event", "block.trigger.on_event"), ("set", "Set Local (Number)", "block.action.set_local.number"));

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual("Set Local set gold = 5", steps[1].Summary);
        }

        [Test]
        public void Wait_ShowsTheDayAndTimeItResumesAt_NotTicks()
        {
            var entries = new List<RuleTraceEntry> { RuleTraceEntry.NodeExecuted("trigger", "then"), RuleTraceEntry.NodeExecuted("wait", "then") };
            long resumeTick = CalendarConstants.TicksPerDay + CalendarConstants.TicksPerHour * 6;
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", entries, RuleTraceEndKind.Waiting, endResumeTick: resumeTick);
            var nodes = Nodes(("trigger", "On Event", "block.trigger.on_event"), ("wait", "Wait", "block.flow.wait"));

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual("Wait paused until Day 2, 06:00", steps[1].Summary);
            Assert.AreEqual("Waiting until Day 2, 06:00.", RuleTraceTextFormatter.DescribeEnd(segment, nodes));
        }

        [Test]
        public void AskPlayer_ShowsTheQuestionTitle_AndEndsAsWaitingForAnAnswer()
        {
            var entries = new List<RuleTraceEntry>
            {
                RuleTraceEntry.NodeExecuted("trigger", "then"),
                RuleTraceEntry.InputRead("ask", "title", RuleValue.Text("Test 1")),
                RuleTraceEntry.NodeExecuted("ask", null)
            };
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", entries, RuleTraceEndKind.Asking, endQuestionId: 1);
            var nodes = Nodes(("trigger", "On Event", "block.trigger.on_event"), ("ask", "Ask Player", "block.flow.ask_player"));

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual("Ask Player asked 'Test 1' and is waiting for an answer", steps[1].Summary);
            Assert.AreEqual("Waiting for an answer.", RuleTraceTextFormatter.DescribeEnd(segment, nodes));
        }

        [Test]
        public void EveryOtherEndReason_HasAPlainSentence()
        {
            var noNodes = new Dictionary<string, RuleTraceNodeInfo>();

            Assert.AreEqual("Stopped: ran too many steps without finishing.",
                RuleTraceTextFormatter.DescribeEnd(Segment(RuleTraceStartKind.EventTrigger, "go", Array.Empty<RuleTraceEntry>(), RuleTraceEndKind.StepCap), noNodes));

            Assert.AreEqual("Stopped: too many rules chained together.",
                RuleTraceTextFormatter.DescribeEnd(Segment(RuleTraceStartKind.EventTrigger, "go", Array.Empty<RuleTraceEntry>(), RuleTraceEndKind.ChainDepthCutOff), noNodes));

            Assert.AreEqual("Stopped: this rule already fired its limit for now.",
                RuleTraceTextFormatter.DescribeEnd(Segment(RuleTraceStartKind.EventTrigger, "go", Array.Empty<RuleTraceEntry>(), RuleTraceEndKind.FireCap), noNodes));

            var nodes = Nodes(("bad", "Math", "block.value.math"));
            RuleTraceSegment errorSegment = Segment(RuleTraceStartKind.EventTrigger, "go", Array.Empty<RuleTraceEntry>(), RuleTraceEndKind.Error, endErrorMessage: "divide by zero", endNodeId: "bad");
            Assert.AreEqual("Stopped: error in Math: divide by zero", RuleTraceTextFormatter.DescribeEnd(errorSegment, nodes));
        }

        [Test]
        public void ZeroStepSegment_StillShowsTheTriggerAsStepOne()
        {
            // Chain-depth cutoff and fire-cap segments never create an instance, so there's no trigger NodeExecuted entry - step 1 must still come from the start info alone.
            RuleTraceSegment segment = Segment(RuleTraceStartKind.EventTrigger, "go", Array.Empty<RuleTraceEntry>(), RuleTraceEndKind.FireCap);
            var nodes = new Dictionary<string, RuleTraceNodeInfo>();

            IReadOnlyList<RuleRunStep> steps = RuleTraceTextFormatter.BuildSteps(segment, nodes);

            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual("On Event fired: go", steps[0].Summary);
        }
    }
}
