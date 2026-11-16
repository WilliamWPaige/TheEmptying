using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    public class PeopleModuleDeathTests
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
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
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

        private static bool Exists(PeopleModule module, EntityId id)
        {
            foreach (KeyValuePair<EntityId, PersonState> row in module.People)
            {
                if (row.Key == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static void StepHours(Simulation sim, int hours)
        {
            for (long i = 0; i < CalendarConstants.TicksPerHour * hours; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void HomelessNights_DamageHealthOncePerNight()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            double startingHealth = GetPerson(people, id).Health;
            double damagePerNight = sim.Settings.Get(PeopleModule.HomelessHealthDamagePerNightSettingId);

            // A full in-game day crosses the night-start hour exactly once.
            StepHours(sim, (int)CalendarConstants.HoursPerDay);

            Assert.AreEqual(startingHealth - damagePerNight, GetPerson(people, id).Health, 0.01);
            Assert.AreEqual(1, GetPerson(people, id).HomelessNightsThisMonth);
        }

        [Test]
        public void Starvation_KillsAfterTheConfiguredConsecutiveEmptyHours()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            sim.Settings.Set(PeopleModule.StarvationHoursBeforeDeathSettingId, 4);
            // Keep health damage from the (also homeless) nights from being the actual cause of death.
            sim.Settings.Set(PeopleModule.HomelessHealthDamagePerNightSettingId, 0);

            EntityId id = people.SpawnPerson(30, 30);
            while (inventory.TryRemove(id, "good.loaf", 1))
            {
            }

            string cause = null;
            sim.Events.Subscribe(PeopleModule.PersonDiedEventId, e =>
            {
                if (e.Data.GetEntity("person") == id)
                {
                    cause = e.Data.GetString("cause");
                }
            });

            // 24 hours to empty + 4 consecutive empty hours before death, with margin.
            StepHours(sim, 30);

            Assert.AreEqual("starvation", cause);
            Assert.IsFalse(Exists(people, id), "The person should have been removed from the table.");
        }
    }
}
