using System;
using System.Collections.Generic;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Pauses the instance until the current tick plus a duration (minutes, hours or days), at least 1 tick.</summary>
    public sealed class WaitFlowBlockType : BlockType
    {
        public override string Id => "block.flow.wait";
        public override string DisplayName => "Wait";
        public override string Category => "Flow";
        public override BlockKind Kind => BlockKind.Flow;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("amount", RuleDataType.Number, 1.0, allowsSettingReference: true),
            new FieldDefinition("unit", RuleDataType.Text, "hours")
        };

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            double amount = context.GetInput("amount").AsNumber;
            string unit = context.GetInput("unit").AsText;

            long ticksPerUnit = unit switch
            {
                "minutes" => CalendarConstants.TicksPerMinute,
                "days" => CalendarConstants.TicksPerDay,
                _ => CalendarConstants.TicksPerHour
            };

            long duration = (long)Math.Round(amount * ticksPerUnit, MidpointRounding.AwayFromZero);
            if (duration < 1)
            {
                duration = 1;
            }

            return RuleExecutionResult.Wait(context.CurrentTick + duration, "then");
        }
    }
}
