using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Randomness;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// What a block's ComputeValue or Execute can do: read its own inputs
    /// (wire if connected, else field, settings read live), use the "rules"
    /// random stream, log, post events, and read or write this instance's
    /// local variables.
    /// </summary>
    public interface IRuleNodeContext
    {
        /// <summary>The value of a named input port or field on the currently executing node. A wire takes priority over a same-named field.</summary>
        RuleValue GetInput(string name);

        /// <summary>Whether a named data input port has an incoming wire.</summary>
        bool IsInputConnected(string portName);

        SimRandom Random { get; }
        SettingsRegistry Settings { get; }

        void Log(string message);

        void PostEvent(string eventDefinitionId, IReadOnlyList<(string Name, RuleValue Value)> parameters);

        RuleValue GetLocal(string name, RuleDataType type);
        void SetLocal(string name, RuleDataType type, RuleValue value);

        /// <summary>Reads a named parameter from the instance's triggering event's data, if any.</summary>
        bool TryGetEventParameter(string name, RuleDataType type, out RuleValue value);

        /// <summary>Runs the chain wired to a named execution output to completion, sharing this instance's step budget. Used by blocks like Sequence that run more than one branch.</summary>
        void RunExecutionOutput(string portName);
    }
}
