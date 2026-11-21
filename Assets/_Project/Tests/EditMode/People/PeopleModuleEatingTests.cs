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
        public void WithBreadAvailable_EatsNormally_NeverForages()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            int loavesBefore = inventory.GetCount(id, "good.loaf");
            GetPerson(people, id).Needs["need.hunger"].Meter = 40f; // at/below the eat-at-50% threshold

            StepHours(sim, 1);

            Assert.AreEqual(loavesBefore - 1, inventory.GetCount(id, "good.loaf"), "Bread should be eaten while it's available.");
            Assert.AreEqual(PersonActivity.Eating, GetPerson(people, id).Activity);
            Assert.AreEqual(0.0, GetPerson(people, id).GrubDiggingHoursAccumulated, "No bread shortage, so no self-foraging.");
            Assert.AreEqual(0, GetPerson(people, id).GrubMoodPenaltyHoursRemaining);
        }

        [Test]
        public void WithNoBreadLeft_ForagesForTwoHours_ThenRestoresHungerWithAMoodPenalty()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            while (inventory.TryRemove(id, "good.loaf", 1))
            {
            }

            GetPerson(people, id).Needs["need.hunger"].Meter = 40f; // at/below the eat-at-50% threshold

            StepHours(sim, 1);

            Assert.AreEqual(PersonActivity.ForagingFood, GetPerson(people, id).Activity,
                "With no bread left, a hungry person should stop and self-forage instead of whatever else they'd do.");
            Assert.AreEqual(1.0, GetPerson(people, id).GrubDiggingHoursAccumulated, 0.001, "One hour toward the 2-hour placeholder dig time.");
            Assert.AreEqual(0, GetPerson(people, id).GrubMoodPenaltyHoursRemaining, "Still mid-dig; no grub dug up yet.");

            double hungerMidDig = GetPerson(people, id).Needs["need.hunger"].Meter;

            StepHours(sim, 1);

            Assert.AreEqual(0.0, GetPerson(people, id).GrubDiggingHoursAccumulated, "The accumulator resets once a grub is dug up.");
            Assert.Greater(GetPerson(people, id).Needs["need.hunger"].Meter, hungerMidDig, "Digging up the grub should restore hunger.");
            Assert.Greater(GetPerson(people, id).GrubMoodPenaltyHoursRemaining, 0, "Eating a self-foraged grub should start its mood penalty.");
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
