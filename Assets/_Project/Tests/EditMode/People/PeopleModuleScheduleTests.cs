using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    public class PeopleModuleScheduleTests
    {
        private static (Simulation Simulation, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, people, inventory);
        }

        private static PersonState GetPerson(PeopleModule module, EntityId id)
        {
            foreach (KeyValuePair<EntityId, PersonState> row in module.People)
            {
                if (row.Key == id)
                {
                    return row.Value;
                }
            }

            throw new KeyNotFoundException();
        }

        private static void StepHours(Simulation sim, int hours)
        {
            for (long i = 0; i < CalendarConstants.TicksPerHour * hours; i++)
            {
                sim.Step();
            }
        }

        /// <summary>3.9h: a new game starts at noon, not midnight, so a test that needs a specific hour of day steps to it directly rather than assuming tick 0 is hour 0.</summary>
        private static void StepUntilHour(Simulation sim, int hour)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                if (GameDate.FromTick(sim.Clock.CurrentTick).Hour == hour)
                {
                    return;
                }

                sim.Step();
            }
        }

        [Test]
        public void Person_IsSleepingDuringTheConfiguredNightHours()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            // Strip food so the eat-before-sleep priority in ChooseActivity can't short-circuit this check.
            while (inventory.TryRemove(id, "good.loaf", 1))
            {
            }

            // Default night: 22:00-06:00.
            StepUntilHour(sim, 22);

            Assert.AreEqual(PersonActivity.Sleeping, GetPerson(people, id).Activity);
        }

        [Test]
        public void Person_IsNotSleepingDuringWorkHours_WithNoWanderingAndNoHunger()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            sim.Settings.Set(PeopleModule.IdleMillChancePerCheckSettingId, 0.0); // isolate the idle/sleep decision

            EntityId id = people.SpawnPerson(30, 30);

            // A daytime hour clear of the night window (22:00-06:00) and of the day shift (07:00-15:00)
            // - a jobless adult can pick up a hand-gathering task from the world's own starting
            // buildings, and being on shift would otherwise take priority over this check. Hunger
            // (drains over 24h from 100, eats at 50%) is still comfortably above the eat threshold, so
            // nothing but the idle/sleep decision is live.
            StepUntilHour(sim, 16);

            Assert.AreEqual(PersonActivity.Idle, GetPerson(people, id).Activity);
        }
    }
}
