using System;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Registers every core block type into a BlockRegistry.</summary>
    public static class CoreBlocks
    {
        private static readonly RuleDataType[] AllDataTypes = (RuleDataType[])Enum.GetValues(typeof(RuleDataType));

        public static void RegisterAll(BlockRegistry registry)
        {
            registry.Register(new OnEventTriggerBlockType());
            registry.Register(new EveryNHoursTriggerBlockType());
            registry.Register(new AtHourOfDayTriggerBlockType());
            registry.Register(new OnNewDayTriggerBlockType());

            registry.Register(new CompareNumbersBlockType());
            registry.Register(new CompareTextBlockType());
            registry.Register(new AndBlockType());
            registry.Register(new OrBlockType());
            registry.Register(new NotBlockType());
            registry.Register(new MathBlockType());
            registry.Register(new RandomNumberBlockType());
            registry.Register(new RandomChanceBlockType());
            registry.Register(new ReadSettingBlockType());

            foreach (RuleDataType type in AllDataTypes)
            {
                registry.Register(new ReadEventParameterBlockType(type));
                registry.Register(new GetLocalBlockType(type));
                registry.Register(new SetLocalBlockType(type));
            }

            registry.Register(new BranchBlockType());
            registry.Register(new SequenceBlockType());
            registry.Register(new StopBlockType());

            registry.Register(new PostEventActionBlockType());
            registry.Register(new WriteLogLineActionBlockType());
            registry.Register(new WriteHeraldLineActionBlockType());
        }
    }
}
