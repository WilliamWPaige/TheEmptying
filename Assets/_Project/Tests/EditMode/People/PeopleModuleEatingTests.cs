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
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    public class PeopleModuleEatingTests
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
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
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
        public void EatingFromInventory_RestoresHungerAndRemovesOneLoaf()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            while (inventory.TryRemove(id, "good.loaf", 1))
            {
            }

            inventory.Add(id, "good.loaf", 2);
            GetPerson(people, id).Needs["need.hunger"].Meter = 40f; // at/below the eat-at-50% threshold

            StepHours(sim, 1);

            Assert.AreEqual(1, inventory.GetCount(id, "good.loaf"));
            Assert.Greater(GetPerson(people, id).Needs["need.hunger"].Meter, 40f);
            Assert.AreEqual(PersonActivity.Eating, GetPerson(people, id).Activity);
        }

        [Test]
        public void PrefersBread_OverGrubs_WhenBothAreAvailable()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            inventory.Add(id, "good.ground_grub", 5);
            int loavesBefore = inventory.GetCount(id, "good.loaf");
            GetPerson(people, id).Needs["need.hunger"].Meter = 40f; // at/below the eat-at-50% threshold

            StepHours(sim, 1);

            Assert.AreEqual(loavesBefore - 1, inventory.GetCount(id, "good.loaf"), "Bread should be eaten before any grub, while both are available.");
            Assert.AreEqual(5, inventory.GetCount(id, "good.ground_grub"));
            Assert.AreEqual(0, GetPerson(people, id).GrubMoodPenaltyHoursRemaining, "Eating bread should not trigger the grub mood penalty.");
        }

        [Test]
        public void EatingAGrub_OnlyWithNoBreadLeft_RestoresHungerWithAMoodPenalty()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            while (inventory.TryRemove(id, "good.loaf", 1))
            {
            }

            inventory.Add(id, "good.ground_grub", 3);
            GetPerson(people, id).Needs["need.hunger"].Meter = 40f; // at/below the eat-at-50% threshold

            StepHours(sim, 1);

            Assert.AreEqual(2, inventory.GetCount(id, "good.ground_grub"), "With no bread left, hunger should be satisfied by a grub instead.");
            Assert.Greater(GetPerson(people, id).Needs["need.hunger"].Meter, 40f, "Eating a grub should restore hunger.");
            Assert.AreEqual(PersonActivity.Eating, GetPerson(people, id).Activity);
            Assert.Greater(GetPerson(people, id).GrubMoodPenaltyHoursRemaining, 0, "Eating a grub should start its temporary mood penalty.");
        }

        [Test]
        public void StartingProvisions_LastAboutAMonthForOnePerson()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            int startingLoaves = inventory.GetCount(id, "good.loaf");
            Assert.AreEqual(56, startingLoaves, "Prompt-specified starting provisions.");

            StepHours(sim, (int)CalendarConstants.DaysPerMonth * (int)CalendarConstants.HoursPerDay);

            int remaining = inventory.GetCount(id, "good.loaf");
            Assert.LessOrEqual(remaining, 2, $"56 loaves at ~2/day should be essentially gone after a month; {remaining} remained.");
            Assert.GreaterOrEqual(remaining, 0);
        }
    }
}
