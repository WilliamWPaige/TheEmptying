using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
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

namespace TheEmptying.Tests.EditMode.Gathering
{
    /// <summary>3.9b: hand gathering - founders build crown sites and gather by hand, anyone jobless may gather and sell to the crown.</summary>
    public class GatheringModuleTests
    {
        private static (Simulation Simulation, GatheringModule Gathering, BuildingsModule Buildings, InventoryModule Inventory, WealthModule Wealth, MarketModule Market, PeopleModule People, JobsModule Jobs) Build(TempCoreContentCopy core)
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
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, gathering, buildings, inventory, wealth, market, people, jobs);
        }

        /// <summary>
        /// 3.9i: a hand test found founders doing about an hour of work, then idle, then wandering,
        /// then work again - caused by OnMoverArrived unconditionally stamping Idle on arrival at
        /// work (instead of promoting straight to Working), leaving a transient Idle window that
        /// idle milling could grab and wander away with. Traces every founder's activity, tick by
        /// tick, through a full subsequent workday (after letting the first day settle the initial
        /// task assignment and commute) and logs it hour by hour.
        /// </summary>
        [Test]
        public void Founders_WorkSteadilyThroughTheWholeShift_NoIdleOrWanderingGaps()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.loaf", 10000); // food isn't short: every founder gathers material, none farm

            int dayStart = (int)sim.Settings.Get(JobsModule.DayShiftStartHourSettingId);
            int dayEnd = (int)sim.Settings.Get(JobsModule.DayShiftEndHourSettingId);

            EntityId[] founderIds = people.People.Where(row => people.IsFounder(row.Key)).Select(row => row.Key).ToArray();

            // Let the first day settle (initial task assignment, the first commute to a gather spot)
            // before tracing a clean, already-established workday - this test is about steadiness
            // during an ongoing shift, not the one-time startup lag before the first hourly
            // ChooseActivity call ever runs.
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            var gaps = new List<string>();
            int lastLoggedHour = -1;

            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
                GameDate date = GameDate.FromTick(sim.Clock.CurrentTick);
                bool withinShift = dayStart <= dayEnd
                    ? date.Hour >= dayStart && date.Hour < dayEnd
                    : date.Hour >= dayStart || date.Hour < dayEnd;

                if (date.Hour != lastLoggedHour)
                {
                    lastLoggedHour = date.Hour;
                    foreach (EntityId id in founderIds)
                    {
                        people.TryGetPerson(id, out PersonState person);
                        TestContext.WriteLine($"hour {date.Hour:D2}: {id} activity={person.Activity}, task={gathering.GetTaskKind(id)}");
                    }
                }

                if (!withinShift)
                {
                    continue;
                }

                foreach (EntityId id in founderIds)
                {
                    if (gathering.GetTaskKind(id) == GatherTaskKind.None)
                    {
                        continue;
                    }

                    people.TryGetPerson(id, out PersonState person);
                    bool okay = person.Activity == PersonActivity.Working
                        || person.Activity == PersonActivity.Commuting
                        || person.Activity == PersonActivity.Eating
                        || person.Activity == PersonActivity.ForagingFood
                        || person.Activity == PersonActivity.Shopping;

                    if (!okay)
                    {
                        gaps.Add($"tick {sim.Clock.CurrentTick} (hour {date.Hour}): {id} was {person.Activity} while on shift with a {gathering.GetTaskKind(id)} task.");
                    }
                }
            }

            Assert.IsEmpty(gaps, $"Expected no idle/wandering gaps during work hours:\n{string.Join("\n", gaps)}");
        }

        [Test]
        public void Founders_GatherWood_IntoTheStockpile_WithNoOrdersOrHelp()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // Plenty of bread already, so food isn't short and the priority order moves straight to
            // whichever raw material (logs or stone) is scarcer - here, logs (the stockpile starts with
            // some stone).
            inventory.Add(storehouseId, "good.loaf", 10000);

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.log"), 0,
                "Founders with no job and no orders should have hand-gathered some logs into the stockpile within 10 days.");
        }

        [Test]
        public void Founders_GatherMaterial_WhenFoodIsShortButOutOfSeason()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // Keep everyone well fed while skipping forward to Frost (month 10) - out of season for
            // wheat (Waking, Bloom and Reaping are the only seasons wheat farming allows) - so even
            // once bread runs short below, nothing can be farmed, and material gathering is all that's
            // left (grubs are self-foraged now, never a task or a stockpiled good).
            const int wellFed = 10000;
            for (long i = 0; i < CalendarConstants.TicksPerMonth * 9; i++)
            {
                sim.Step();
                if (i % CalendarConstants.TicksPerDay == 0)
                {
                    int currentLoaves = inventory.GetCount(storehouseId, "good.loaf");
                    if (currentLoaves < wellFed)
                    {
                        inventory.Add(storehouseId, "good.loaf", wellFed - currentLoaves);
                    }
                }
            }

            // Drain the bread below the "short" threshold (9 people * 56) without emptying the stockpile.
            int loavesNow = inventory.GetCount(storehouseId, "good.loaf");
            Assert.IsTrue(inventory.TryRemove(storehouseId, "good.loaf", loavesNow - 400));

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.log"), 0,
                "With wheat out of season, founders should gather material even while bread is short.");
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.ground_grub"), "Grubs are never stockpiled.");
        }

        [Test]
        public void Founders_FarmWheat_IntoTheStockpile_WhenFoodIsShortAndInSeason()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // Keep bread topped up to a fixed level under the "short" threshold (9 people * 56) every day,
            // so founders stay assigned to farming (never fed enough to be considered "not short", never
            // starved) for the whole ~30 days wheat needs to grow and be harvested. This isolates the
            // farming mechanic from the separate, pre-existing gap between the starting-bread placeholder
            // and wheat's growth time.
            const int keepFedAt = 450;

            long totalTicks = CalendarConstants.TicksPerMonth + CalendarConstants.TicksPerDay * 5;
            for (long i = 0; i < totalTicks; i++)
            {
                sim.Step();

                if (i % CalendarConstants.TicksPerDay == 0)
                {
                    int currentLoaves = inventory.GetCount(storehouseId, "good.loaf");
                    if (currentLoaves < keepFedAt)
                    {
                        inventory.Add(storehouseId, "good.loaf", keepFedAt - currentLoaves);
                    }
                }
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.wheat"), 0,
                "Founders should have planted, grown and harvested wheat into the stockpile within about a month.");
        }

        [Test]
        public void Founders_FinishAWaitingCrownSite_WithUnpaidOrHiredLabor_WithinAReasonableTime()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            for (long i = 0; i < CalendarConstants.TicksPerDay * 15; i++)
            {
                sim.Step();
            }

            Assert.IsTrue(buildings.TryGetBuilding(siteId, out BuildingState site));
            Assert.IsNull(site.Construction, "The founders (building for free, or hired as paid builders) should finish the crown lodging within 15 days.");
        }

        [Test]
        public void WithASiteWaiting_AndFoodShortInSeason_UpToTwoFoundersFarm_AndTheRestBuild()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);

            // Below the "short" threshold (9 people * 56), and the game starts in Waking (in season for wheat).
            int loavesNow = inventory.GetCount(storehouseId, "good.loaf");
            Assert.IsTrue(inventory.TryRemove(storehouseId, "good.loaf", loavesNow));

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            sim.Step(); // deliver the construction-site-started event so AssignTasks runs immediately

            EntityId[] founderIds = people.People.Where(row => people.IsFounder(row.Key)).Select(row => row.Key).ToArray();
            int farmCount = founderIds.Count(id => gathering.GetTaskKind(id) == GatherTaskKind.Farm);
            int buildCount = founderIds.Count(id => gathering.GetTaskKind(id) == GatherTaskKind.Build);

            Assert.AreEqual(2, farmCount, "Exactly 2 founders should be farming wheat while a crown site waits for labor and food is short.");
            Assert.AreEqual(founderIds.Length - 2, buildCount, "Every other founder should be building the waiting site.");
        }

        [Test]
        public void WithASiteWaiting_AndFoodNotShort_AllFoundersBuild()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);
            inventory.Add(storehouseId, "good.loaf", 10000); // plenty of bread: food isn't short

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            sim.Step();

            EntityId[] founderIds = people.People.Where(row => people.IsFounder(row.Key)).Select(row => row.Key).ToArray();
            int buildCount = founderIds.Count(id => gathering.GetTaskKind(id) == GatherTaskKind.Build);

            Assert.AreEqual(founderIds.Length, buildCount, "With food not short, there's no farming carve-out: every founder should build.");
        }

        /// <summary>3.9h: builders on the same site used to all head for the identical doorstep cell and stand stacked on top of each other.</summary>
        [Test]
        public void SeveralFoundersBuildingOneSite_OccupyDifferentCells()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);
            inventory.Add(storehouseId, "good.loaf", 10000); // plenty of bread: food isn't short, so every founder builds

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            sim.Step();

            EntityId[] builderIds = people.People
                .Where(row => people.IsFounder(row.Key) && gathering.GetTaskKind(row.Key) == GatherTaskKind.Build)
                .Select(row => row.Key)
                .ToArray();
            Assert.Greater(builderIds.Length, 1, "Setup: expected more than one founder building this site.");

            var spots = new HashSet<GridPoint>(builderIds.Select(id => gathering.GetLocation(id).Value));
            Assert.AreEqual(builderIds.Length, spots.Count, "Expected every builder on the same site to occupy a different cell.");
        }

        [Test]
        public void PlacingABuilding_SwitchesFoundersToBuilding_TheSameDay()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);
            inventory.Add(storehouseId, "good.loaf", 10000); // food isn't short, so founders start out on material gathering

            sim.Step();

            EntityId someFounder = people.People.First(row => people.IsFounder(row.Key)).Key;
            Assert.AreNotEqual(GatherTaskKind.Build, gathering.GetTaskKind(someFounder), "Before any site exists, no founder should be building.");

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            // Well under a day: the reassignment should come from the construction-site-started event
            // firing immediately, not from waiting for the next morning's NewDayEvent.
            for (long i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            int buildingFounders = people.People.Count(row => people.IsFounder(row.Key) && gathering.GetTaskKind(row.Key) == GatherTaskKind.Build);
            Assert.Greater(buildingFounders, 0, "At least one founder should already be building the new site within the same day it was placed.");
        }

        /// <summary>
        /// 3.9f: a hand test found founders stopped doing anything once a building finished. The
        /// cause was GatheringModule never reassigning tasks when a site completes - ProcessBuild
        /// clears a finished builder's task to None, but nothing re-ran AssignTasks until the next
        /// morning's NewDayEvent. Fixed by subscribing to BuildingCompletedEventId too.
        /// </summary>
        [Test]
        public void FinishingASite_ReassignsBuildersImmediately_NotJustNextMorning()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, JobsModule jobs) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);

            // The cheapest government building, so every founder building it finishes well within a
            // single day's shift and there's plenty of real/simulated time left before the next
            // morning to prove the reassignment didn't wait for it.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            bool completed = false;
            for (long i = 0; i < CalendarConstants.TicksPerDay && !completed; i++)
            {
                sim.Step();

                if (buildings.TryGetBuilding(siteId, out BuildingState site) && site.Construction == null)
                {
                    completed = true;
                }
            }

            Assert.IsTrue(completed, "Expected the storehouse to finish within a single day.");

            // A handful of ticks, not a whole day: the BuildingCompletedEventId subscription should
            // re-run AssignTasks within the same tick the completion is noticed.
            for (long i = 0; i < 10; i++)
            {
                sim.Step();
            }

            // A founder hired into the storehouse's own new job slot is correctly untasked here (they
            // have a real job instead - see GatheringModule.DescribeIdleReason), not idle.
            int idleFounders = people.People.Count(row =>
                people.IsFounder(row.Key) && !jobs.IsEmployed(row.Key) && gathering.GetTaskKind(row.Key) == GatherTaskKind.None);
            Assert.AreEqual(0, idleFounders, "Expected every founder with no real job to already have a new task (farming or gathering material) right after the site finished, not stay idle until the next morning.");
        }

        [Test]
        public void NewGame_SurvivesThreeMonthsWithNoConsoleHelp_NobodyStarves_AndAPlacedBuildingCompletes()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            int startingPopulation = people.People.Count();
            Assert.AreEqual(9, startingPopulation, "8 founders plus the monarch.");

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // Materials for the one building below, since nothing in this fresh colony can mill planks
            // from hand-cut logs yet - this is still the "no console help" scenario the test asks for,
            // just like the construction test above seeds materials directly.
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            for (long i = 0; i < CalendarConstants.TicksPerMonth * 3; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(startingPopulation, people.People.Count(), "Nobody should have starved to death over 3 months with no console help.");
            Assert.IsTrue(buildings.TryGetBuilding(siteId, out BuildingState site));
            Assert.IsNull(site.Construction, "The placed crown lodging should have completed within 3 months.");
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.ground_grub"), "Grubs are self-foraged, never stockpiled.");
        }

        /// <summary>3.9f: the People panel shows a plain reason whenever someone has no task - DescribeIdleReason must give one for everyone truly idle, and "" for anyone who actually has a task.</summary>
        [Test]
        public void DescribeIdleReason_GivesAReasonForTheMonarch_AndNothingForATaskedFounder()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.loaf", 10000);

            sim.Step();

            Assert.IsTrue(people.MonarchId.HasValue, "Setup: expected a monarch.");
            string monarchReason = gathering.DescribeIdleReason(people.MonarchId.Value);
            Assert.IsNotEmpty(monarchReason, "Expected a plain reason for the monarch, who never has a gathering task.");

            EntityId someFounder = people.People.First(row => people.IsFounder(row.Key)).Key;
            Assert.AreNotEqual(GatherTaskKind.None, gathering.GetTaskKind(someFounder), "Setup: expected this founder to already have a task.");
            Assert.AreEqual("", gathering.DescribeIdleReason(someFounder), "Expected no idle reason for a founder who already has a task.");
        }

        [Test]
        public void JoblessNonFounder_SellsGatheredGoods_ToTheCrownAtMarketPrice()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, WealthModule wealth, MarketModule market, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            EntityId gatherer = people.SpawnPerson(30, 30);
            Assert.IsFalse(people.IsFounder(gatherer));

            // Plenty of bread for everyone (including the new gatherer), so food isn't short and
            // material gathering is the priority for all of them.
            inventory.Add(storehouseId, "good.loaf", 10000);

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.log"), 0,
                "The crown should have bought at least one log from the jobless gatherer, landing it in the storehouse.");
            Assert.Greater(wealth.GetBalance(AccountId.Person(gatherer)).HundredthsOfShil, 0,
                "The gatherer should have been paid at the market price for what the crown bought.");
        }

        [Test]
        public void SaveAndLoad_MidGathering_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 555;
            long n = CalendarConstants.TicksPerDay * 4 + 123; // a few days into gathering
            long m = CalendarConstants.TicksPerDay * 3; // continue a few more days after loading

            (Simulation uninterrupted, _, BuildingsModule uninterruptedBuildings, _, _, _, _) = BuildWithSeed(core, seed);
            Assert.IsTrue(uninterruptedBuildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error0), error0);
            for (long i = 0; i < n + m; i++)
            {
                uninterrupted.Step();
            }

            ulong expectedHash = uninterrupted.ComputeStateHash();

            (Simulation firstRun, _, BuildingsModule firstBuildings, _, _, _, _) = BuildWithSeed(core, seed);
            Assert.IsTrue(firstBuildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error1), error1);
            for (long i = 0; i < n; i++)
            {
                firstRun.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstRun, new SaveHeaderInfo { KingdomName = "K", Population = 9 }, stream);
            stream.Position = 0;

            IModule[] secondModules = ModulesWithSeed(out _, out _, out _, out _, out _, out _, out _);
            (Simulation secondRun, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            for (long i = 0; i < m; i++)
            {
                secondRun.Step();
            }

            Assert.AreEqual(expectedHash, secondRun.ComputeStateHash());
        }

        private static IModule[] ModulesWithSeed(out MapMovementModule mapMovement, out InventoryModule inventory, out PeopleModule people, out WealthModule wealth, out BuildingsModule buildings, out MarketModule market, out GatheringModule gathering)
        {
            mapMovement = new MapMovementModule();
            inventory = new InventoryModule();
            people = new PeopleModule(mapMovement, inventory);
            wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering };
        }

        private static (Simulation Simulation, GatheringModule Gathering, BuildingsModule Buildings, InventoryModule Inventory, WealthModule Wealth, MarketModule Market, PeopleModule People) BuildWithSeed(TempCoreContentCopy core, ulong seed)
        {
            IModule[] modules = ModulesWithSeed(out MapMovementModule _, out InventoryModule inventory, out PeopleModule people, out WealthModule wealth, out BuildingsModule buildings, out MarketModule market, out GatheringModule gathering);
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, gathering, buildings, inventory, wealth, market, people);
        }
    }
}
