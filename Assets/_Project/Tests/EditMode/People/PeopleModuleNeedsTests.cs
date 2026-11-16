using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    public class PeopleModuleNeedsTests
    {
        private static (Simulation Simulation, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
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

        /// <summary>Removes every loaf from a person's inventory, so hunger drains freely without the auto-eat system interfering.</summary>
        private static void StripFood(InventoryModule inventory, EntityId id)
        {
            while (inventory.TryRemove(id, "good.loaf", 1))
            {
            }
        }

        private static void StepHours(Simulation sim, int hours)
        {
            for (long i = 0; i < CalendarConstants.TicksPerHour * hours; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void Hunger_DrainsAtTheContentHoursToEmptyRate()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            StripFood(inventory, id);

            StepHours(sim, 12);

            // 24 hours to empty from full, so 12 hours should land exactly on 50.
            Assert.AreEqual(50f, GetPerson(people, id).Needs["need.hunger"].Meter, 0.01f);
        }

        [Test]
        public void OnlyClassAppropriateNeedsApply_EveryoneIsLowerClassForNow()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            PersonState person = GetPerson(people, id);

            Assert.IsTrue(person.Needs.ContainsKey("need.hunger"));
            Assert.IsTrue(person.Needs.ContainsKey("need.rest"));
            Assert.IsTrue(person.Needs.ContainsKey("need.hygiene"));

            // Middle/Upper-only needs must not apply to a lower-class person.
            Assert.IsFalse(person.Needs.ContainsKey("need.comfort"));
            Assert.IsFalse(person.Needs.ContainsKey("need.recreation"));

            if (!person.IsBeliever)
            {
                Assert.IsFalse(person.Needs.ContainsKey("need.faith"));
            }
        }

        [Test]
        public void NeedEvents_FireExactlyOnceAtLowAndCritical_NeverOnOrdinaryDrain()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            StripFood(inventory, id);

            int lowCount = 0, criticalCount = 0, recoveredCount = 0;
            sim.Events.Subscribe(PeopleModule.NeedLowEventId, e => { if (e.Data.GetEntity("person") == id && e.Data.GetString("need") == "need.hunger") lowCount++; });
            sim.Events.Subscribe(PeopleModule.NeedCriticalEventId, e => { if (e.Data.GetEntity("person") == id && e.Data.GetString("need") == "need.hunger") criticalCount++; });
            sim.Events.Subscribe(PeopleModule.NeedRecoveredEventId, e => { if (e.Data.GetEntity("person") == id && e.Data.GetString("need") == "need.hunger") recoveredCount++; });

            // Drain all the way from 100 to 0 (24 hours), well past both thresholds.
            StepHours(sim, 24);

            Assert.AreEqual(1, lowCount, "Should fire low exactly once.");
            Assert.AreEqual(1, criticalCount, "Should fire critical exactly once.");
            Assert.AreEqual(0, recoveredCount, "Should not have recovered - nothing fed it.");
        }

        [Test]
        public void NeedEvents_FireRecoveredOnceWhenANeedComesBackToNormal()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            StripFood(inventory, id);

            // Push hunger into Critical directly, then feed it back above Low.
            GetPerson(people, id).Needs["need.hunger"].Meter = 5f;
            StepHours(sim, 1); // lets the hourly system observe and mark Critical

            int recoveredCount = 0;
            sim.Events.Subscribe(PeopleModule.NeedRecoveredEventId, e => { if (e.Data.GetEntity("person") == id) recoveredCount++; });

            GetPerson(people, id).Needs["need.hunger"].Meter = 100f;
            StepHours(sim, 1);

            Assert.AreEqual(1, recoveredCount);
        }
    }
}
