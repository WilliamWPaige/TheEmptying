using TheEmptying.Core.Rules.Tracing;

namespace TheEmptying.Core.Console
{
    /// <summary>Writes a rule's trace segments to a log sink as short text - what the "trace &lt;ruleId&gt; on" console command attaches, for builds without the editor.</summary>
    internal sealed class RuleConsoleTraceListener : IRuleTraceListener
    {
        private readonly ILogSink _logSink;

        public RuleConsoleTraceListener(ILogSink logSink)
        {
            _logSink = logSink;
        }

        public void OnSegmentCompleted(RuleTraceSegment segment) => _logSink.Log($"[trace] {segment}");
    }
}
