using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Randomness;
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

        public Simulation(ulong worldSeed)
        {
            Random = new RandomService(worldSeed);
            Clock = new SimClock();
            Scheduler = new Scheduler();
            Entities = new EntityRegistry();
            EventDefinitions = new EventDefinitionRegistry();
            Events = new EventBus(EventDefinitions);

            RegisterCalendarDefinitions();
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

        private void RegisterCalendarDefinitions()
        {
            EventDefinitions.Register(new EventDefinition(NewHourEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new in-game hour has begun."));
            EventDefinitions.Register(new EventDefinition(NewDayEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new in-game day has begun."));
            EventDefinitions.Register(new EventDefinition(NewSeasonEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new season has begun."));
            EventDefinitions.Register(new EventDefinition(NewYearEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "A new year has begun."));
        }
    }
}
