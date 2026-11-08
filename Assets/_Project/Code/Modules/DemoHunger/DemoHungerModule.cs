using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Core.Console;

namespace TheEmptying.Modules.DemoHunger
{
    /// <summary>
    /// TEMPORARY: proves the milestone-1 core framework works end to end
    /// (modules, scheduling with importance tiers, entity module data,
    /// events, data-driven settings, saves, console, inspector). Deleted in
    /// milestone 3 once a real people and needs system exists. See README.md.
    /// </summary>
    public sealed class DemoHungerModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.demo_hunger";
        public const string DecayPerHourSettingId = "setting.demo_hunger.decay_per_hour";
        public const string StarvingEventId = "event.demo_hunger.starving";

        private const int DefaultPersonCount = 50;
        private const int BucketCount = 10;
        private const double StartingHunger = 100.0;
        private const double StarvingThreshold = 20.0;
        private const double JitterRange = 0.5;

        private sealed class HungerSystem : ISimSystem
        {
            private readonly DemoHungerModule _owner;

            public HungerSystem(DemoHungerModule owner)
            {
                _owner = owner;
            }

            public string Id => ModuleId + ".hunger_system";

            public void Update(long tick)
            {
                _owner.UpdateHunger(tick);
            }
        }

        private readonly int _personCount;
        private readonly Dictionary<int, EntityId> _idsByIndex = new Dictionary<int, EntityId>();

        private EntityTable<double> _hunger;
        private BucketedWork _work;
        private SettingsRegistry _settings;
        private EventBus _events;
        private SimRandom _random;

        /// <summary>personCount is overridable only for the milestone performance check; real play always uses the default.</summary>
        public DemoHungerModule(int personCount = DefaultPersonCount)
        {
            _personCount = personCount;
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();
        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _random = context.Random;

            _hunger = new EntityTable<double>(context.Entities);
            _work = new BucketedWork(BucketCount);

            if (!context.IsLoading)
            {
                for (int i = 0; i < _personCount; i++)
                {
                    EntityId id = context.Entities.Create(EntityKind.Person);
                    _hunger.Add(id, StartingHunger);
                    _idsByIndex[id.Index] = id;
                }
            }

            context.RegisterSystem(new HungerSystem(this), 1, 0, 0);

            context.RegisterCommand(new ConsoleCommand("feed", "feed <entityId> - sets hunger back to 100 (e.g. e12.3).", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_hunger.Has(id))
                {
                    return "Usage: feed <entityId> (e.g. e12.3)";
                }

                _hunger.Set(id, StartingHunger);
                return $"{id} fed; hunger reset to 100.";
            }));
        }

        public void Shutdown()
        {
        }

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_hunger.Count);
            foreach (KeyValuePair<EntityId, double> row in _hunger.Rows())
            {
                writer.WriteEntityId(row.Key);
                writer.WriteDouble(row.Value);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            _idsByIndex.Clear();

            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                double hunger = reader.ReadDouble();
                _hunger.Add(id, hunger);
                _idsByIndex[id.Index] = id;
            }
        }

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (_hunger.TryGet(entity, out double hunger))
            {
                yield return $"hunger: {hunger:F1} ({GetTier(entity.Index)})";
            }
        }

        /// <summary>Starting values from the milestone spec: entity index modulo 3 picks the tier, since a real importance score arrives with the people modules.</summary>
        private static ImportanceTier GetTier(long entityIndex)
        {
            long mod = entityIndex % 3;
            if (mod == 0)
            {
                return ImportanceTier.High;
            }

            if (mod == 1)
            {
                return ImportanceTier.Normal;
            }

            return ImportanceTier.Low;
        }

        /// <summary>
        /// The content container's rows are the only ordered, allocation-free-at-
        /// storage-level view of "every entity this module owns"; wrapping it to
        /// hand BucketedWork plain indices is itself an allocating iterator on top
        /// of Rows()'s own allocating iterator - see the milestone performance
        /// report for what this costs.
        /// </summary>
        private IEnumerable<long> EntityIndices()
        {
            foreach (KeyValuePair<EntityId, double> row in _hunger.Rows())
            {
                yield return row.Key.Index;
            }
        }

        private void UpdateHunger(long tick)
        {
            double decayPerHour = _settings.Get(DecayPerHourSettingId);
            double decayPerTick = decayPerHour / CalendarConstants.TicksPerHour;

            _work.Run(EntityIndices(), tick, GetTier, index =>
            {
                int entityIndex = (int)index;
                long periodTicks = BucketCount * ImportanceTierConstants.GetMultiplier(GetTier(entityIndex));

                EntityId id = _idsByIndex[entityIndex];
                double oldHunger = _hunger.Get(id);

                double jitter = (_random.NextFloat() - 0.5) * JitterRange;
                double newHunger = oldHunger - (decayPerTick * periodTicks) + jitter;
                if (newHunger < 0.0)
                {
                    newHunger = 0.0;
                }

                _hunger.Set(id, newHunger);

                if (oldHunger >= StarvingThreshold && newHunger < StarvingThreshold)
                {
                    _events.Post(StarvingEventId, tick, new EventData().SetEntity("entity", id));
                }
            });
        }
    }
}
