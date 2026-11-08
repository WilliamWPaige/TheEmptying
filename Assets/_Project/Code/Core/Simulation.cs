using System;
using System.Collections.Generic;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
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
        public ConsoleCommandRegistry Commands { get; }

        /// <summary>Every registered block type. Modules register their blocks during the declaration phase; the core registers its own too.</summary>
        public BlockRegistry Blocks { get; }

        /// <summary>The loaded content database. Empty until SimulationBuilder loads content; a Simulation built from code-supplied manifests alone has no content.</summary>
        public ContentDatabase Content { get; internal set; }

        /// <summary>Every rule that compiled without errors. Empty until SimulationBuilder compiles rules.</summary>
        public RuleRegistry Rules { get; internal set; }

        /// <summary>Activates and runs compiled rules. Null until SimulationBuilder builds it, after modules initialize.</summary>
        public RuleRunner RuleRunner { get; internal set; }

        /// <summary>
        /// Sections from a loaded save whose module is currently missing or
        /// switched off. Kept untouched and written back out on the next save,
        /// so re-enabling the module later restores its data.
        /// </summary>
        public Dictionary<string, SaveSectionData> OrphanedModuleSections { get; } = new Dictionary<string, SaveSectionData>();

        /// <summary>
        /// Builds every core service but runs none of the module startup
        /// phases. SimulationBuilder drives ValidateAndOrder, the declaration
        /// phase, content loading and InitializeModules itself, in that order,
        /// with content loaded in between.
        /// </summary>
        internal Simulation(ulong worldSeed, ErrorMode errorMode)
        {
            Random = new RandomService(worldSeed);
            Clock = new SimClock();
            Scheduler = new Scheduler();
            Entities = new EntityRegistry();
            EventDefinitions = new EventDefinitionRegistry();
            Events = new EventBus(EventDefinitions);
            Commands = new ConsoleCommandRegistry();
            ContentTypes = new ContentTypeRegistry();
            Settings = new SettingsRegistry();
            Blocks = new BlockRegistry();
            Modules = new ModuleHost(Events, Scheduler, Entities, EventDefinitions, Random, () => Clock.CurrentTick, errorMode, Commands, Settings);
            EventSerializers = new TypedEventSerializerRegistry();
            Content = new ContentDatabase();
            Rules = new RuleRegistry();

            RegisterCalendarDefinitions();
            RegisterRuleDefinitions();
            CalendarEventSerializers.RegisterAll(EventSerializers);
        }

        /// <summary>The way tests build a Simulation from code-supplied manifests: validates, declares and initializes in one call, with no content loaded.</summary>
        public Simulation(ulong worldSeed, IEnumerable<ModuleRegistration> modules = null, ErrorMode errorMode = ErrorMode.Development, bool isLoading = false)
            : this(worldSeed, errorMode)
        {
            Modules.ValidateAndOrder(modules ?? Array.Empty<ModuleRegistration>());
            Modules.RunDeclarationPhase(ContentTypes, Settings, Blocks);
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

        private void RegisterRuleDefinitions()
        {
            EventDefinitions.Register(new EventDefinition(WriteHeraldLineActionBlockType.EventDefinitionId, EventVisibility.Public, 0f, 5f, "A herald line was posted by a rule."));
        }
    }
}
