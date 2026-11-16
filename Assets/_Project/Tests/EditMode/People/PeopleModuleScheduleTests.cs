using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.MapMovement;
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
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
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

            // Default night: 22:00-06:00. Step to tick 22*TicksPerHour, which lands on hour 22.
            StepHours(sim, 22);

            Assert.AreEqual(PersonActivity.Sleeping, GetPerson(people, id).Activity);
        }

        [Test]
        public void Person_IsNotSleepingDuringWorkHours_WithNoWanderingAndNoHunger()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            sim.Settings.Set(PeopleModule.WanderChancePerHourSettingId, 0.0); // isolate the idle/sleep decision

            EntityId id = people.SpawnPerson(30, 30);

            // Hour 6: clear of the default 22:00-06:00 night window, and hunger
            // (drains over 24h from 100, eats at 50%) is still comfortably above
            // the eat threshold, so nothing but the idle/sleep decision is live.
            StepHours(sim, 6);

            Assert.AreEqual(PersonActivity.Idle, GetPerson(people, id).Activity);
        }
    }
}
