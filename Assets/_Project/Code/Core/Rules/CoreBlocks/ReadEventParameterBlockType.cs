using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>
    /// Reads a named parameter from the triggering event's data. One output
    /// port type per instance (ports are statically typed, so "type" picks
    /// which registered block to use rather than a field on one block).
    /// A missing parameter is a runtime error naming the rule and node.
    /// </summary>
    public sealed class ReadEventParameterBlockType : BlockType
    {
        private readonly RuleDataType _type;

        public override string Id { get; }
        public override string DisplayName { get; }
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;
        public override IReadOnlyList<PortDefinition> Ports { get; }
        public override IReadOnlyList<FieldDefinition> Fields { get; }

        public ReadEventParameterBlockType(RuleDataType type)
        {
            _type = type;
            string suffix = type.ToString().ToLowerInvariant();
            Id = $"block.value.read_event_parameter.{suffix}";
            DisplayName = $"Read Event Parameter ({type})";
            Ports = new[] { new PortDefinition("result", PortDirection.Output, PortKind.Data, type) };
            Fields = new[] { new FieldDefinition("parameter", RuleDataType.Text, string.Empty) };
        }

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            string name = context.GetInput("parameter").AsText;
            if (!context.TryGetEventParameter(name, _type, out RuleValue value))
            {
                throw new InvalidOperationException($"Missing event parameter '{name}'.");
            }

            return value;
        }
    }
}
