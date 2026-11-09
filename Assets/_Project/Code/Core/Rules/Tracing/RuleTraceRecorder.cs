using System.Collections.Generic;

namespace TheEmptying.Core.Rules.Tracing
{
    /// <summary>
    /// Builds one run segment's trace as RuleRunner executes it, then yields
    /// an immutable RuleTraceSegment. Only ever constructed when a listener
    /// is attached to the rule being run - its existence (null or not) is
    /// the whole cost-control mechanism for "tracing costs nothing with no
    /// listener": every record call on the hot path is a null-conditional
    /// call on a possibly-null recorder.
    /// </summary>
    internal sealed class RuleTraceRecorder
    {
        public string RuleId { get; }
        public long InstanceId { get; }
        public long Tick { get; }

        private readonly List<RuleTraceEntry> _entries = new List<RuleTraceEntry>();

        private RuleTraceStartKind _startKind;
        private string _startEventDefinitionId;
        private List<(string Name, string ValueText)> _startEventParameters;
        private long _startResumeTick;
        private long _startQuestionId;
        private int _startChoiceNumber;

        public RuleTraceRecorder(string ruleId, long instanceId, long tick)
        {
            RuleId = ruleId;
            InstanceId = instanceId;
            Tick = tick;
        }

        public void StartFromEventTrigger(string eventDefinitionId, List<(string Name, string ValueText)> parameters)
        {
            _startKind = RuleTraceStartKind.EventTrigger;
            _startEventDefinitionId = eventDefinitionId;
            _startEventParameters = parameters;
        }

        public void StartFromScheduleTrigger()
        {
            _startKind = RuleTraceStartKind.ScheduleTrigger;
        }

        public void StartFromResume(long resumeTick)
        {
            _startKind = RuleTraceStartKind.Resume;
            _startResumeTick = resumeTick;
        }

        public void StartFromAnswer(long questionId, int choiceNumber)
        {
            _startKind = RuleTraceStartKind.Answer;
            _startQuestionId = questionId;
            _startChoiceNumber = choiceNumber;
        }

        public void RecordNodeExecuted(string nodeId, string followedPort) => _entries.Add(RuleTraceEntry.NodeExecuted(nodeId, followedPort));
        public void RecordValueComputed(string nodeId, RuleValue value) => _entries.Add(RuleTraceEntry.ValueComputed(nodeId, value));
        public void RecordInputRead(string nodeId, string portOrFieldName, RuleValue value) => _entries.Add(RuleTraceEntry.InputRead(nodeId, portOrFieldName, value));
        public void RecordLocalSet(string nodeId, string localName, RuleValue value) => _entries.Add(RuleTraceEntry.LocalSet(nodeId, localName, value));
        public void RecordEventPosted(string nodeId, string eventDefinitionId, IReadOnlyList<(string Name, RuleValue Value)> parameters) => _entries.Add(RuleTraceEntry.EventPosted(nodeId, eventDefinitionId, parameters));
        public void RecordLineLogged(string nodeId, string message) => _entries.Add(RuleTraceEntry.LineLogged(nodeId, message));

        public RuleTraceSegment Finish(RuleTraceEndKind endKind, long endResumeTick = 0, long endQuestionId = 0, string endErrorMessage = null, string endNodeId = null)
        {
            return new RuleTraceSegment(
                RuleId, InstanceId, Tick,
                _startKind, _startEventDefinitionId, _startEventParameters, _startResumeTick, _startQuestionId, _startChoiceNumber,
                _entries,
                endKind, endResumeTick, endQuestionId, endErrorMessage, endNodeId);
        }
    }
}
