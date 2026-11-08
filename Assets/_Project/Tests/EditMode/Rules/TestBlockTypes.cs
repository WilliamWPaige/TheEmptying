using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Rules;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>A trigger listening to a configurable event, named by its "event" field.</summary>
    internal sealed class TestTriggerBlockType : BlockType
    {
        public override string Id => "test.trigger.on_event";
        public override string DisplayName => "On Event (Test)";
        public override string Category => "Test";
        public override BlockKind Kind => BlockKind.Trigger;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("event", RuleDataType.Text, string.Empty)
        };

        public override string GetTriggerEventDefinitionId(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            if (fieldValues != null && fieldValues.TryGetValue("event", out RuleFieldValueSource source) && !source.IsSettingReference)
            {
                return source.Literal?.Value<string>();
            }

            return null;
        }
    }

    /// <summary>A value block with two required number inputs and a number output: a + b.</summary>
    internal sealed class TestAddNumbersBlockType : BlockType
    {
        public override string Id => "test.value.add_numbers";
        public override string DisplayName => "Add Numbers (Test)";
        public override string Category => "Test";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("a", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("b", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("sum", PortDirection.Output, PortKind.Data, RuleDataType.Number)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("a", RuleDataType.Number, 0.0, allowsSettingReference: true),
            new FieldDefinition("b", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };
    }

    /// <summary>A flow block with a required bool data input and two execution outputs. Has no fields, so "condition" can only be satisfied by a wire.</summary>
    internal sealed class TestBranchBlockType : BlockType
    {
        public override string Id => "test.flow.branch";
        public override string DisplayName => "Branch (Test)";
        public override string Category => "Test";
        public override BlockKind Kind => BlockKind.Flow;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("condition", PortDirection.Input, PortKind.Data, RuleDataType.Bool, required: true),
            new PortDefinition("true", PortDirection.Output, PortKind.Execution),
            new PortDefinition("false", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = System.Array.Empty<FieldDefinition>();
    }

    /// <summary>An action with execution ports and a text field.</summary>
    internal sealed class TestLogActionBlockType : BlockType
    {
        public override string Id => "test.action.log";
        public override string DisplayName => "Log (Test)";
        public override string Category => "Test";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("message", RuleDataType.Text, string.Empty)
        };
    }

    /// <summary>A post-event action, naming the event it posts in its "event" field.</summary>
    internal sealed class TestPostEventActionBlockType : BlockType
    {
        public override string Id => "test.action.post_event";
        public override string DisplayName => "Post Event (Test)";
        public override string Category => "Test";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("event", RuleDataType.Text, string.Empty)
        };

        public override IEnumerable<string> GetPostedEventDefinitionIds(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            if (fieldValues != null && fieldValues.TryGetValue("event", out RuleFieldValueSource source) && !source.IsSettingReference)
            {
                string id = source.Literal?.Value<string>();
                if (!string.IsNullOrEmpty(id))
                {
                    return new[] { id };
                }
            }

            return Enumerable.Empty<string>();
        }
    }

    /// <summary>An action with an optional number data input, used to prove a pure value node counts as reachable when something reachable consumes its output.</summary>
    internal sealed class TestRecordNumberActionBlockType : BlockType
    {
        public override string Id => "test.action.record_number";
        public override string DisplayName => "Record Number (Test)";
        public override string Category => "Test";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("value", PortDirection.Input, PortKind.Data, RuleDataType.Number),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("value", RuleDataType.Number, 0.0)
        };
    }

    internal static class TestBlocks
    {
        public static BlockRegistry CreateRegistry()
        {
            var registry = new BlockRegistry();
            registry.Register(new TestTriggerBlockType());
            registry.Register(new TestAddNumbersBlockType());
            registry.Register(new TestBranchBlockType());
            registry.Register(new TestLogActionBlockType());
            registry.Register(new TestPostEventActionBlockType());
            registry.Register(new TestRecordNumberActionBlockType());
            return registry;
        }
    }
}
