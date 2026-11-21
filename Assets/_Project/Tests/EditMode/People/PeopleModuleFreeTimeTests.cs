using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    /// <summary>3.9g: off-shift free time - social, recreation and shopping.</summary>
    public class PeopleModuleFreeTimeTests
    {
        private static (Simulation Simulation, PeopleModule People, MapMovementModule MapMovement, InventoryModule Inventory) Build(TempCoreContentCopy core)
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
            return (simulation, people, mapMovement, inventory);
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

        /// <summary>3.9h: a new game starts at noon, not midnight, so a test that needs a specific hour of day steps to it directly rather than assuming a round number of hours lands outside work hours.</summary>
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
        public void TwoFreePeople_WithSocialAsTheirLowestNeed_BothGainSocialWhileTogether()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, MapMovementModule mapMovement, _) = Build(core);

            // No night, ever, and no idle milling - isolate the free-time decision.
            sim.Settings.Set(PeopleModule.NightStartHourSettingId, 0);
            sim.Settings.Set(PeopleModule.NightEndHourSettingId, 0);
            sim.Settings.Set(PeopleModule.IdleMillChancePerCheckSettingId, 0.0);
            // The setting's own maximum (50) clamps anything higher - still far more than enough to
            // cross the whole map well within the handful of ticks given below.
            sim.Settings.Set(MapMovementModule.WalkingSpeedMetersPerTickSettingId, 1000.0);

            EntityId a = people.SpawnPerson(30, 30);
            EntityId b = people.SpawnPerson(30, 30);

            // An hour clear of the day shift (07:00-15:00) first - the world's own starting buildings
            // can give a jobless adult a hand-gathering task before this point, and being on shift
            // would otherwise take priority over the free-time decision this test is isolating. Doing
            // this before positioning them means any such task (and whatever walking it causes) is
            // long since moot by the time we check "are they on the same cell".
            StepUntilHour(sim, 16);

            // Put them on the same cell, regardless of where each one spawned or wandered since.
            mapMovement.TryMoveTo(b, mapMovement.GetCell(a));
            for (int i = 0; i < 50 && mapMovement.GetCell(b) != mapMovement.GetCell(a); i++)
            {
                sim.Step();
            }

            Assert.AreEqual(mapMovement.GetCell(a), mapMovement.GetCell(b), "Setup: expected both on the same cell.");

            GetPerson(people, a).Needs["need.social"].Meter = 10f;
            GetPerson(people, b).Needs["need.social"].Meter = 10f;

            StepHours(sim, 1);

            Assert.AreEqual(PersonActivity.Socializing, GetPerson(people, a).Activity);
            Assert.AreEqual(PersonActivity.Socializing, GetPerson(people, b).Activity);
            Assert.Greater(GetPerson(people, a).Needs["need.social"].Meter, 10f, "Expected A's social to be restored by being together, not just decayed further.");
            Assert.Greater(GetPerson(people, b).Needs["need.social"].Meter, 10f, "Expected B's social to be restored too - both gain it, independently.");
        }

        [Test]
        public void OffShiftPerson_WithEveryNeedFine_MillsAboutInstead()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _, _) = Build(core);

            sim.Settings.Set(PeopleModule.NightStartHourSettingId, 0);
            sim.Settings.Set(PeopleModule.NightEndHourSettingId, 0);
            sim.Settings.Set(PeopleModule.IdleMillChancePerCheckSettingId, 1.0); // always mill once checked

            EntityId id = people.SpawnPerson(30, 30);

            // Fresh and alone: social is nowhere near the low threshold yet, so the free-time
            // dispatcher should decline every hour, leaving idle milling as the only thing that can
            // ever move them. Idle milling only checks a slice of idle people each tick (see
            // PeopleModule.UpdateIdleMilling), so watch across the whole hour rather than sampling
            // the activity at one single tick.
            bool everWandered = false;
            for (long i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
                if (GetPerson(people, id).Activity == PersonActivity.Wandering)
                {
                    everWandered = true;
                }
            }

            Assert.IsTrue(everWandered, "Expected the idle person to have milled about (Wandering) at least once within the hour.");
        }
    }
}
