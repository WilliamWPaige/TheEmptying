using System;

namespace TheEmptying.Core.Rules
{
    /// <summary>Thrown in Development ErrorMode when rule compilation finds any error.</summary>
    public sealed class RuleCompileException : Exception
    {
        public RuleCompileReport Report { get; }

        public RuleCompileException(string message, RuleCompileReport report) : base(message)
        {
            Report = report;
        }
    }
}
