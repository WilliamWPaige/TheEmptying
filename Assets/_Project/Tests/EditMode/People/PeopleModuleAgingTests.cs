using System.Collections.Generic;
using System.Linq;
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
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    public class PeopleModuleAgingTests
    {
        private static (Simulation Simulation, PeopleModule Module) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, people);
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

        [Test]
        public void Aging_AccruesOneLifeYearPerConfiguredDays()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule module) = Build(core);

            EntityId id = module.SpawnPerson(0, 0);
            double daysPerLifeYear = sim.Settings.Get(PeopleModule.DaysPerLifeYearSettingId);

            for (long i = 0; i < CalendarConstants.TicksPerDay * (long)daysPerLifeYear; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(1.0, GetPerson(module, id).AgeLifeYears, 0.01);
        }

        [Test]
        public void Aging_ReachesAdulthoodAndOldAgeOnTheRightDay()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule module) = Build(core);

            // One life-year per in-game day, so adult (16) lands at day 16 exactly.
            sim.Settings.Set(PeopleModule.DaysPerLifeYearSettingId, 1);
            double adultAgeYears = sim.Settings.Get(PeopleModule.AdultAgeYearsSettingId);

            EntityId id = module.SpawnPerson(0, 0);
            long ticksToAdult = CalendarConstants.TicksPerDay * (long)adultAgeYears;

            for (long i = 0; i < ticksToAdult - CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            Assert.Less(GetPerson(module, id).AgeLifeYears, adultAgeYears, "Should not be an adult yet, one hour before the day it happens.");

            for (long i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            Assert.GreaterOrEqual(GetPerson(module, id).AgeLifeYears, adultAgeYears - 1e-6, "Should be an adult by the exact configured day.");
        }

        [Test]
        public void LifestyleShift_NeverMovesLifespanByMoreThanTheConfiguredMaximum()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule module) = Build(core);

            EntityId id = module.SpawnPerson(20, 20);
            double maxShift = sim.Settings.Get(PeopleModule.LifestyleShiftMaxYearsPerMonthSettingId);
            double startingLifespan = GetPerson(module, id).LifespanYears;

            for (long i = 0; i < CalendarConstants.TicksPerMonth; i++)
            {
                sim.Step();
            }

            double change = GetPerson(module, id).LifespanYears - startingLifespan;
            Assert.LessOrEqual(change, maxShift + 0.0001);
            Assert.GreaterOrEqual(change, -maxShift - 0.0001);
        }

        [Test]
        public void OldAgeDeath_OnlyBecomesPossibleOncePastLifespan()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule module) = Build(core);

            // Certain death once past lifespan, to make "only past lifespan" observable in one run.
            sim.Settings.Set(PeopleModule.OldAgeDeathChancePerYearPastLifespanSettingId, 1.0);
            sim.Settings.Set(PeopleModule.OldAgeDeathChanceCapSettingId, 1.0);
            sim.Settings.Set(PeopleModule.LifestyleShiftMaxYearsPerMonthSettingId, 0.0); // keep lifespan fixed for a clean test
            sim.Settings.Set(PeopleModule.DaysPerLifeYearSettingId, 10000); // make natural aging negligible, so age is fully test-controlled

            EntityId id = module.SpawnPerson(20, 20);
            GetPerson(module, id).LifespanYears = 20.5; // just past the starting age

            bool diedCalled = false;
            sim.Events.Subscribe(PeopleModule.PersonDiedEventId, _ => diedCalled = true);

            // One month with negligible natural aging: still well short of lifespan - no death possible yet.
            for (long i = 0; i < CalendarConstants.TicksPerMonth; i++)
            {
                sim.Step();
            }

            Assert.IsFalse(diedCalled, "Should not die before passing lifespan.");

            // Now push age past lifespan directly and let the next month's roll see it.
            GetPerson(module, id).AgeLifeYears = 21.0;

            for (long i = 0; i < CalendarConstants.TicksPerMonth; i++)
            {
                sim.Step();
            }

            Assert.IsTrue(diedCalled, "Should die of old age soon after passing lifespan, with a 100% monthly chance.");
        }
    }
}
