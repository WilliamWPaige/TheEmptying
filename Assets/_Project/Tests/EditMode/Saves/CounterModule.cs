using System;
using System.Collections.Generic;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Tests.EditMode.Saves
{
    /// <summary>A small test module with its own saved data and random rolls, for save/load round-trip tests.</summary>
    internal sealed class CounterModule : IModule, ISaveableModule
    {
        private sealed class TickSystem : ISimSystem
        {
            private readonly CounterModule _owner;
            private readonly SimRandom _random;

            public string Id => "test.counter.system";

            public TickSystem(CounterModule owner, SimRandom random)
            {
                _owner = owner;
                _random = random;
            }

            public void Update(long tick)
            {
                _owner.Counter += 1;
                _owner.RandomRolls.Add(_random.NextInt(0, 1000));
            }
        }

        public string Id => "test.counter";
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();
        public int SectionVersion => 1;

        public int SetupCount { get; private set; }
        public long Counter { get; private set; }
        public List<long> RandomRolls { get; private set; } = new List<long>();

        public void Initialize(ModuleContext context)
        {
            if (!context.IsLoading)
            {
                SetupCount++;
            }

            context.RegisterSystem(new TickSystem(this, context.Random), 1, 0, 0);
        }

        public void Shutdown()
        {
        }

        public void Write(SaveWriter writer)
        {
            writer.WriteLong(Counter);
            writer.WriteLongList(RandomRolls);
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            Counter = reader.ReadLong();
            RandomRolls = reader.ReadLongList();
        }
    }
}
