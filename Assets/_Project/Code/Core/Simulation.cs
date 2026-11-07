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

namespace TheEmptying.Core
{
    /// <summary>
    /// Owns every core system and drives one tick at a time, created from a world seed.
    /// </summary>
    public sealed class Simulation
    {
        public RandomService Random { get; }
        public SimClock Clock { get; }
        public Scheduler Scheduler { get; }
        public EntityRegistry Entities { get; }
        public EventDefinitionRegistry EventDefinitions { get; }
        public EventBus Events { get; }
        public ModuleHost Modules { get; }
        public ContentTypeRegistry ContentTypes { get; }
        public SettingsRegistry Settings { get; }
        public TypedEventSerializerRegistry EventSerializers { get; }

        /// <summary>
        /// Sections from a loaded save whose module is currently missing or
        /// switched off. Kept untouched and written back out on the next save,
        /// so re-enabling the module later restores its data.
        /// </summary>
        public Dictionary<string, SaveSectionData> OrphanedModuleSections { get; } = new Dictionary<string, SaveSectionData>();

        public Simulation(ulong worldSeed, IEnumerable<ModuleRegistration> modules = null, ErrorMode errorMode = ErrorMode.Development, bool isLoading = false)
        {
            Random = new RandomService(worldSeed);
            Clock = new SimClock();
            Scheduler = new Scheduler();
            Entities = new EntityRegistry();
            EventDefinitions = new EventDefinitionRegistry();
            Events = new EventBus(EventDefinitions);
            Modules = new ModuleHost(Events, Scheduler, Entities, EventDefinitions, Random, () => Clock.CurrentTick, errorMode);
            ContentTypes = new ContentTypeRegistry();
            Settings = new SettingsRegistry();
            EventSerializers = new TypedEventSerializerRegistry();

            RegisterCalendarDefinitions();
            CalendarEventSerializers.RegisterAll(EventSerializers);

            Modules.ValidateAndOrder(modules ?? Array.Empty<ModuleRegistration>());
            Modules.RunDeclarationPhase(ContentTypes, Settings);
            Modules.InitializeModules(isLoading);
        }

        /// <summary>Runs exactly one tick: advance the clock, post calendar events, run the scheduler, deliver events.</summary>
        public void Step()
        {
            TickBoundaries boundaries = Clock.Tick();
            long tick = Clock.CurrentTick;

            if (boundaries.NewHour)
            {
                Events.Post(new NewHourEvent(tick), tick);
            }

            if (boundaries.NewDay)
            {
                Events.Post(new NewDayEvent(tick), tick);
            }

            if (boundaries.NewSeason)
            {
                Events.Post(new NewSeasonEvent(tick), tick);
            }

            if (boundaries.NewYear)
            {
                Events.Post(new NewYearEvent(tick), tick);
            }

            Scheduler.RunTick(tick);
            Events.DeliverPending();
        }

        /// <summary>A stable FNV-1a hash over the serialized bytes of every core and module section, in a fixed (ordinal Id) order.</summary>
        public ulong ComputeStateHash()
        {
            List<SaveSectionData> sections = CoreSections.WriteAll(this, Settings, EventSerializers);

            foreach ((string moduleId, ISaveableModule module) in Modules.GetActiveSaveableModules())
            {
                sections.Add(new SaveSectionData(moduleId, module.SectionVersion, SaveSectionWriter.WriteModule(module)));
            }

            sections.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            ulong hash = Fnv1a.InitialHash;
            foreach (SaveSectionData section in sections)
            {
                hash = Fnv1a.Continue(hash, System.Text.Encoding.UTF8.GetBytes(section.Id));
                hash = Fnv1a.Continue(hash, section.Data);
            }

            return hash;
        }

        private void RegisterCalendarDefinitions()
        {
            EventDefinitions.Register(new EventDefinition(NewHourEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new in-game hour has begun."));
            EventDefinitions.Register(new EventDefinition(NewDayEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new in-game day has begun."));
            EventDefinitions.Register(new EventDefinition(NewSeasonEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new season has begun."));
            EventDefinitions.Register(new EventDefinition(NewYearEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new year has begun."));
        }
    }
}
