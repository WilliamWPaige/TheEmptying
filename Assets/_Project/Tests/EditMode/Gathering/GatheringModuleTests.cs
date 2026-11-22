using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
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
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Gathering
{
    /// <summary>3.9b: hand gathering - founders build crown sites and gather by hand, anyone jobless may gather and sell to the crown.</summary>
    public class GatheringModuleTests
    {
        /// <summary>
        /// 3.10b3: fully delivers a site's whole construction cost straight from BuildingsModule's own
        /// test-only API (TryDeliverGoods), so a test about labor/reassignment timing isn't also, now
        /// that materials have to be hauled over by hand, implicitly a test of hauling - these tests
        /// seed abundant storehouse stock specifically so materials are never the point.
        /// </summary>
        private static void FullyDeliverConstructionCost(BuildingsModule buildings, EntityId siteId)
        {
            buildings.TryGetBuilding(siteId, out BuildingState site);
            BuildingType type = buildings.GetBuildingType(site.BuildingTypeId);
            foreach (GoodAmount cost in type.ConstructionCost)
            {
                Assert.IsTrue(buildings.TryDeliverGoods(siteId, cost.Good.Id, cost.Amount, out string error), error);
            }
        }

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
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, gathering, buildings, inventory, wealth, market, people, jobs);
        }

        /// <summary>
        /// 3.9i/3.9j: a hand test on 3.9i found founders doing about an hour of work, then idle, then
        /// wandering, then work again - fixed in 3.9i for the OnMoverArrived/idle-milling cause, but
        /// that fix's own test seeded 10,000 loaves, masking a second, independent cause a 3.9j hand
        /// test on a REAL new game (504 starting loaves, counted food-short almost immediately) then
        /// found: a founder who becomes ineligible for a hand-gathering task outside of AssignTasks'
        /// own trigger events (a real job firing them mid-day, rather than at the next day boundary)
        /// keeps GatherTaskKind.None - and nothing ever looks at them again - until the next trigger,
        /// which can be most of a day away. Traces every founder, tick by tick, through two full
        /// days from a true default start (no seeded stock at all), treating "no task at all while
        /// eligible and on shift" as a gap in its own right, not just "doing the wrong activity".
        /// </summary>
        [Test]
        public void Founders_WorkSteadilyThroughTheWholeShift_TrueDefaultStart_NoBuildingPlaced()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, _, _, _, PeopleModule people, JobsModule jobs) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error), error);

            AssertNoIdleOrUntaskedGapsOverTwoDays(sim, gathering, people, jobs);
        }

        /// <summary>
        /// 3.9j: a gather/build spot, once picked, is never revisited unless the task's kind changes -
        /// a chosen cell that's passable but unreachable (a disconnected patch, say) traps a founder
        /// idle forever once their commute fails. Forces exactly that by walling off a founder's
        /// already-assigned spot on all eight sides (passable itself, but now cut off), then confirms
        /// a failed commute re-picks the spot instead of leaving them stuck on the same dead end.
        /// </summary>
        [Test]
        public void GatherSpot_UnreachableByPath_IsRepickedAfterTheFailedCommute_NotStuckForever()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error), error);

            EntityId someFounder = people.People.First(row => people.IsFounder(row.Key)).Key;
            sim.Step(); // let the initial task assignment happen

            GridPoint spot = gathering.GetLocation(someFounder).Value;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx != 0 || dy != 0)
                    {
                        mapMovement.Grid.SetWalkable(new GridPoint(spot.X + dx, spot.Y + dy), false);
                    }
                }
            }

            mapMovement.TryMoveTo(someFounder, spot);

            for (long i = 0; i < 5 && gathering.GetLocation(someFounder) == spot; i++)
            {
                sim.Step();
            }

            Assert.AreNotEqual(spot, gathering.GetLocation(someFounder), "Expected a fresh spot to be picked after the commute to the walled-off one failed.");
        }

        /// <summary>3.10a: the user's hand test - the bootstrap chain from raw logs alone, no console help, no seeded stock.</summary>
        [Test]
        public void TrueDefaultNewGame_SawPitAndWoodcuttersCamp_MakeTheCrownLodgingComplete_FromHandCutLogs()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // The true default stockpile already holds more planks than the lodging alone needs -
            // spend them down first, so the saw pit is genuinely load-bearing here, matching the
            // user's actual hand test ("nothing in the game can make planks yet").
            Assert.IsTrue(inventory.TryRemove(storehouseId, "good.plank", inventory.GetCount(storehouseId, "good.plank")));

            int initialTreeCount = mapResources.Nodes.Count(n => n.Value.Kind == ResourceNodeKind.Tree);
            Assert.Greater(initialTreeCount, 0, "Setup: expected trees to exist.");

            // No console help at all: every good here comes only from hand-cutting trees and the
            // two bootstrap buildings processing them - never seeded directly.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.woodcutters_camp", new GridPoint(20, 10), out _, out string campError), campError);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.saw_pit", new GridPoint(30, 10), out _, out string pitError), pitError);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId lodgingId, out string lodgingError), lodgingError);

            int planksNeeded = buildings.GetBuildingType("building.crown_lodging").ConstructionCost.First(c => c.Good.Id == "good.plank").Amount;
            Assert.Greater(planksNeeded, 0, "Setup: expected the crown lodging to need planks.");
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.plank"), "Setup: expected zero planks in stock at this point.");

            const long maxTicks = CalendarConstants.TicksPerDay * 90;
            long completedAtTick = -1;

            for (long i = 0; i < maxTicks; i++)
            {
                sim.Step();

                if (buildings.TryGetBuilding(lodgingId, out BuildingState lodging) && lodging.Construction == null)
                {
                    completedAtTick = i;
                    break;
                }
            }

            Assert.IsTrue(completedAtTick >= 0, $"Expected the crown lodging to complete within {maxTicks / CalendarConstants.TicksPerDay} days, from hand-cut logs alone.");

            int remainingTrees = mapResources.Nodes.Count(n => n.Value.Kind == ResourceNodeKind.Tree);
            Assert.Less(remainingTrees, initialTreeCount, "Expected at least one tree to have visibly depleted (disappeared) along the way.");

            TestContext.WriteLine($"Bootstrap completed in {completedAtTick / (double)CalendarConstants.TicksPerDay:F1} in-game days ({completedAtTick} ticks).");
        }

        /// <summary>3.10a: a depleted tree is never revisited - workers move to the next one, and report a specific idle reason once none are left.</summary>
        [Test]
        public void TreesRunOut_WorkersMoveToTheNextOne_AndReportASpecificIdleReason_WhenNoneAreLeft()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            // Plenty of bread, so food never goes short over this test's span - isolating the
            // resource-depletion behavior from the separate "up to 2 founders farm wheat" mechanic.
            inventory.Add(storehouseId, "good.loaf", 10000);
            sim.Step(); // let the first task assignment happen

            EntityId someFounder = people.People.First(row => people.IsFounder(row.Key) && gathering.GetTaskKind(row.Key) == GatherTaskKind.Woodcut).Key;
            GridPoint assignedSpot = gathering.GetLocation(someFounder).Value;
            EntityId assignedTreeId = mapResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Tree && n.Value.Cell == assignedSpot).Key;

            int yieldPerTree = (int)sim.Settings.Get(MapResourcesModule.LogsPerTreeSettingId);
            mapResources.Harvest(assignedTreeId, yieldPerTree); // fully deplete it directly, bypassing real time

            // ProcessHandGather only notices the tree is gone once this founder has actually arrived
            // there - give them a full day to walk the distance and get at least one hourly check in.
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            Assert.AreNotEqual(GatherTaskKind.None, gathering.GetTaskKind(someFounder), "Expected them to have moved to a different tree, not gone idle - others remain standing.");
            Assert.AreNotEqual(assignedSpot, gathering.GetLocation(someFounder), "Expected a different tree after the first one ran out.");

            // Now drain every remaining tree too, so none are left anywhere.
            foreach (KeyValuePair<EntityId, ResourceNodeState> row in mapResources.Nodes.Where(n => n.Value.Kind == ResourceNodeKind.Tree).ToList())
            {
                mapResources.Harvest(row.Key, row.Value.RemainingStock);
            }

            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(GatherTaskKind.None, gathering.GetTaskKind(someFounder), "Expected no task at all once no trees are left anywhere.");
            Assert.AreEqual("No trees left in reach.", gathering.DescribeIdleReason(someFounder));
        }

        /// <summary>3.10a: a builder whose site is missing something that can't be hand-gathered (planks) switches to gathering raw material instead of idling, and returns once it arrives.</summary>
        [Test]
        public void BuilderAtASiteMissingPlanks_SwitchesToGathering_AndReturnsOncePlanksArrive()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            // This test is about the pre-existing (3.10a) "switch to gathering when stalled"
            // behavior specifically, on this one site - not about the founders' own (3.10b) decision
            // to go build a saw pit in response to the very same stall, which would otherwise pull
            // builders onto a second, usually-closer site instead of the lodging this test cares about.
            sim.Settings.Set(GatheringModule.StarterBuildingsEnabledSettingId, 0);
            // Spend the default starting planks down to zero (the true default stockpile already
            // holds more planks than the lodging alone needs, so this test forces the "missing"
            // condition directly instead of relying on exact numbers happening to fall short).
            // Plenty of stone (not the point here) but zero planks - the one thing the lodging is
            // missing, and planks can't be gathered by hand, so the fallback is "whichever raw
            // material is scarcer" (logs, since there's no stock of either and ties favor woodcutting).
            Assert.IsTrue(inventory.TryRemove(storehouseId, "good.plank", inventory.GetCount(storehouseId, "good.plank")));
            inventory.Add(storehouseId, "good.stone", 1000);
            // Plenty of bread too, so food never goes short over this test's two-day span - isolating
            // the stalled-site behavior from the separate "up to 2 founders farm wheat" mechanic.
            inventory.Add(storehouseId, "good.loaf", 10000);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);
            sim.Step(); // deliver ConstructionSiteStartedEventId and assign builders - this stall also gives the founders a reason to start their own saw pit (3.10b), so pick a builder on THIS site specifically, not whichever site a founder happens to be building.

            EntityId someBuilder = people.People.First(row => people.IsFounder(row.Key) && gathering.GetTargetSite(row.Key) == siteId).Key;

            // Give them time to actually reach the site, not just be assigned to it - ProcessBuild
            // (where the stall check lives) only runs once a builder has physically arrived.
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(GatherTaskKind.Woodcut, gathering.GetTaskKind(someBuilder),
                "Expected the builder to switch to gathering logs once the site's own panel-equivalent check found planks neither in stock nor on order.");

            int planksNeeded = buildings.GetBuildingType("building.crown_lodging").ConstructionCost.First(c => c.Good.Id == "good.plank").Amount;
            inventory.Add(storehouseId, "good.plank", planksNeeded);

            // AssignTasks re-evaluates a stalled site at the next trigger (a new day, here) - step a
            // full day to cross that boundary.
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(GatherTaskKind.Build, gathering.GetTaskKind(someBuilder), "Expected the builder to return to building once planks were available again.");
        }

        /// <summary>3.10b: "the people decide the work, not the player" - the founder with the highest Logging skill leads a woodcutter's camp the moment the town needs one, reported in the task-change log by name.</summary>
        [Test]
        public void FoundersDecideToBuildAWoodcuttersCamp_LedByTheFounderWithTheHighestLoggingSkill()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // The ambient log reserve defaults to 0 (purely reactive) - so give the town a genuine,
            // immediate reason to need logs: a saw pit with none in stock and none on order yet,
            // stalled on its own 20-log construction cost the moment it's placed.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.saw_pit", new GridPoint(30, 10), out _, out string pitError), pitError);
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.log"), "Setup: expected zero logs in stock, so the saw pit is genuinely stalled.");

            // Founders start with varied (not identical) skills - give one a lead unmistakably
            // beyond whatever the current highest actually is.
            int currentMaxLogging = people.People.Where(row => people.IsFounder(row.Key)).Max(row => row.Value.Skills[(int)Skill.Logging]);
            EntityId bestWoodcutter = people.People.First(row => people.IsFounder(row.Key)).Key;
            Assert.IsTrue(people.TryGetPerson(bestWoodcutter, out PersonState bestWoodcutterState));
            bestWoodcutterState.Skills[(int)Skill.Logging] = currentMaxLogging + 10;
            string expectedName = bestWoodcutterState.FullName;

            var capturedReasons = new List<string>();
            sim.Events.Subscribe(GatheringModule.TaskChangedEventId, e => capturedReasons.Add(e.Data.GetString("reason")));

            sim.Step(); // delivers ConstructionSiteStartedEventId -> AssignTasks -> TryStartNeededStarterBuildings - this queues the task-changed events it posts
            sim.Step(); // events post during a tick and deliver at the end of the NEXT one - this flushes them to the subscriber above

            Assert.IsTrue(buildings.Buildings.Any(row => row.Value.BuildingTypeId == "building.woodcutters_camp"),
                "Expected the founders to have started a woodcutter's camp on their own.");
            Assert.AreEqual(GatherTaskKind.Build, gathering.GetTaskKind(bestWoodcutter), "Expected the best-logging founder to be the one assigned to build it.");

            string expectedReason = $"{expectedName} (best woodcutter) is building a Woodcutter's Camp";
            Assert.IsTrue(capturedReasons.Contains(expectedReason), $"Expected a task-change log line \"{expectedReason}\" - got: {string.Join(" | ", capturedReasons)}");
        }

        /// <summary>3.10b: only one of each starter building ever exists - a second is never started while the first stands, even across many days with the same shortage persisting.</summary>
        [Test]
        public void StarterBuildings_OnlyOneOfEachEverExists_EvenWhenTheShortageNeverLetsUp()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // The ambient reserves default to 0 (purely reactive) - so give the town a genuine,
            // persistent reason to need logs: a saw pit, kept permanently stalled on its own 20-log
            // construction cost by draining any logs that arrive before they can be delivered to it.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.saw_pit", new GridPoint(30, 10), out _, out string pitError), pitError);

            for (int day = 0; day < 20; day++)
            {
                int logsInStock = inventory.GetCount(storehouseId, "good.log");
                if (logsInStock > 0)
                {
                    inventory.TryRemove(storehouseId, "good.log", logsInStock); // TryRemove throws for a zero amount
                }

                for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
                {
                    sim.Step();
                }

                Assert.LessOrEqual(buildings.Buildings.Count(row => row.Value.BuildingTypeId == "building.woodcutters_camp"), 1, $"Day {day}: expected at most one woodcutter's camp ever.");
            }

            Assert.IsTrue(buildings.Buildings.Any(row => row.Value.BuildingTypeId == "building.woodcutters_camp"),
                "Setup: expected the persistent shortage to actually have triggered one at some point.");
        }

        /// <summary>
        /// 3.10b: the user's actual ask - from a true default new game with no console help and
        /// nothing placed but the stockpile, the founders decide for themselves to build a
        /// woodcutter's camp and a saw pit (3.10a's version of this test placed them directly - this
        /// is the same bootstrap chain, but with the founders choosing to build both on their own).
        /// Once that's working, a crown lodging needing more planks than the stock completes with no
        /// further help at all.
        /// </summary>
        [Test]
        public void TrueDefaultNewGame_FoundersBuildTheirOwnWoodcuttersCampAndSawPit_AndTheCrownLodgingCompletes()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // The true default stockpile already holds more planks than the lodging alone needs -
            // spend them down first, so the saw pit the founders build themselves is genuinely
            // load-bearing here, matching the user's actual hand test.
            Assert.IsTrue(inventory.TryRemove(storehouseId, "good.plank", inventory.GetCount(storehouseId, "good.plank")));

            int initialTreeCount = mapResources.Nodes.Count(n => n.Value.Kind == ResourceNodeKind.Tree);
            Assert.Greater(initialTreeCount, 0, "Setup: expected trees to exist.");

            // No console help at all, and nothing placed but the stockpile and this one lodging -
            // the founders decide for themselves to build the woodcutter's camp and the saw pit.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId lodgingId, out string lodgingError), lodgingError);

            int planksNeeded = buildings.GetBuildingType("building.crown_lodging").ConstructionCost.First(c => c.Good.Id == "good.plank").Amount;
            Assert.Greater(planksNeeded, 0, "Setup: expected the crown lodging to need planks.");
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.plank"), "Setup: expected zero planks in stock at this point.");

            const long maxTicks = CalendarConstants.TicksPerDay * 90;
            long campAtTick = -1;
            long pitAtTick = -1;
            long completedAtTick = -1;

            for (long i = 0; i < maxTicks; i++)
            {
                sim.Step();

                if (campAtTick < 0 && buildings.Buildings.Any(row => row.Value.BuildingTypeId == "building.woodcutters_camp"))
                {
                    campAtTick = i;
                }

                if (pitAtTick < 0 && buildings.Buildings.Any(row => row.Value.BuildingTypeId == "building.saw_pit"))
                {
                    pitAtTick = i;
                }

                if (buildings.TryGetBuilding(lodgingId, out BuildingState lodging) && lodging.Construction == null)
                {
                    completedAtTick = i;
                    break;
                }
            }

            Assert.IsTrue(campAtTick >= 0, "Expected the founders to have started a woodcutter's camp on their own.");
            Assert.IsTrue(pitAtTick >= 0, "Expected the founders to have started a saw pit on their own.");
            Assert.IsTrue(completedAtTick >= 0, $"Expected the crown lodging to complete within {maxTicks / CalendarConstants.TicksPerDay} days, with the founders bootstrapping everything themselves.");

            int remainingTrees = mapResources.Nodes.Count(n => n.Value.Kind == ResourceNodeKind.Tree);
            Assert.Less(remainingTrees, initialTreeCount, "Expected at least one tree to have visibly depleted (disappeared) along the way.");

            TestContext.WriteLine($"Founders started their own woodcutter's camp by day {campAtTick / (double)CalendarConstants.TicksPerDay:F1} " +
                $"and saw pit by day {pitAtTick / (double)CalendarConstants.TicksPerDay:F1}; " +
                $"the crown lodging completed in {completedAtTick / (double)CalendarConstants.TicksPerDay:F1} in-game days ({completedAtTick} ticks).");
        }

        /// <summary>3.10b: no banked hours - a saw pit with no logs makes no progress at all while waiting, and resumes at the recipe's normal rate (no burst) once logs arrive.</summary>
        [Test]
        public void SawPit_WithNoLogs_AccumulatesNoHours_AndProducesNormallyOnceLogsArrive()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            // No founders at all (destroyed immediately, before anything can reference them - the
            // same safe pattern the Jobs test files use) and a dedicated worker spawned instead, so
            // nobody else can hand-cut a stray log into the stockpile in the background - this test
            // is about the saw pit's own production hours specifically, not a race against the rest
            // of the town's woodcutters.
            foreach (KeyValuePair<EntityId, PersonState> row in people.People.ToList())
            {
                sim.Entities.Destroy(row.Key);
            }

            // No founders left to lead one anyway, but keeps the world down to exactly the one
            // worker this test cares about - no stray log cabin from the spawned worker's own homelessness.
            sim.Settings.Set(GatheringModule.StarterBuildingsEnabledSettingId, 0);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            // The default stockpile starts with its own planks already - drain them, so any planks
            // seen later are unambiguously the saw pit's own output.
            Assert.IsTrue(inventory.TryRemove(storehouseId, "good.plank", inventory.GetCount(storehouseId, "good.plank")));

            // Built already complete, via the same test-only shortcut other test files use
            // (TryDeliverGoods/TryAddLaborHours) - no logs or builders needed to get there.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.saw_pit", new GridPoint(30, 10), out EntityId sawPitId, out string pitError), pitError);
            inventory.Add(storehouseId, "good.log", 20);
            Assert.IsTrue(buildings.TryDeliverGoods(sawPitId, "good.log", 20, out string deliverError), deliverError);
            Assert.IsTrue(buildings.TryAddLaborHours(sawPitId, buildings.GetLaborHoursNeeded(sawPitId), out string laborError), laborError);
            Assert.IsTrue(buildings.TryGetBuilding(sawPitId, out BuildingState sawPitSetup) && sawPitSetup.Construction == null, "Setup: expected the saw pit to be finished.");
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.log"), "Setup: expected delivery to have spent all 20 logs on construction.");

            EntityId sawPitWorker = people.SpawnPerson(30, 30);
            inventory.Add(sawPitWorker, "good.loaf", 10000);

            long hiredAtTick = -1;
            for (long i = 0; i < CalendarConstants.TicksPerDay * 5; i++)
            {
                sim.Step();
                if (jobs.TryGetEmployment(sawPitWorker, out EntityId employedAt, out _) && employedAt == sawPitId)
                {
                    hiredAtTick = i;
                    break;
                }
            }

            Assert.IsTrue(hiredAtTick >= 0, "Setup: expected the spawned worker to be hired at the saw pit.");
            Assert.IsTrue(jobs.TryGetJobs(sawPitId, out JobsState jobsState) && jobsState.Worker != null);

            // No logs at all - the worker is there, on shift, but has nothing to saw.
            for (long i = 0; i < CalendarConstants.TicksPerDay * 3; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(0.0, jobsState.Worker.WorkHoursAccumulated, "Expected no banked hours at all while waiting for logs - the whole point of 'no banked hours'.");
            Assert.AreEqual(0, inventory.GetCount(storehouseId, "good.plank"), "Expected no planks produced while waiting for logs.");

            // Logs arrive - production should resume at the recipe's normal rate, not a sudden
            // burst reflecting all the hours that would otherwise have banked up while blocked.
            inventory.Add(storehouseId, "good.log", 100);

            long planksArrivedAtTick = -1;
            for (long i = 0; i < CalendarConstants.TicksPerDay * 5; i++)
            {
                sim.Step();
                if (inventory.GetCount(storehouseId, "good.plank") > 0)
                {
                    planksArrivedAtTick = i;
                    break;
                }
            }

            Assert.IsTrue(planksArrivedAtTick >= 0, "Expected planks to start appearing again once logs arrived.");
        }

        /// <summary>
        /// 3.10b2/3.10b3: the gap the other TrueDefaultNewGame_* tests above left open - each of them
        /// drains the stockpile's planks first "since the true default stockpile already holds more
        /// planks than the lodging alone needs", so none of them actually proves the literal default
        /// case (a true new game, nothing placed but the stockpile and one crown lodging, left running
        /// with no console help and no draining at all) ever completes. This is the user's exact hand
        /// test. 3.10b3: materials now travel by hauler, not instantly, so this no longer expects them
        /// within the first day - just that they do arrive, and the lodging does finish.
        /// </summary>
        [Test]
        public void TrueDefaultNewGame_CrownLodging_DrawsFromTheStockpileAndCompletes_WithNoHelp()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, WealthModule wealth, MarketModule market, PeopleModule people, JobsModule jobs) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId lodgingId, out string placeError), placeError);

            int planksNeeded = buildings.GetBuildingType("building.crown_lodging").ConstructionCost.First(c => c.Good.Id == "good.plank").Amount;
            int stoneNeeded = buildings.GetBuildingType("building.crown_lodging").ConstructionCost.First(c => c.Good.Id == "good.stone").Amount;
            Assert.LessOrEqual(planksNeeded, inventory.GetCount(storehouseId, "good.plank"), "Setup: expected the true default stockpile to already hold enough planks on its own.");
            Assert.LessOrEqual(stoneNeeded, inventory.GetCount(storehouseId, "good.stone"), "Setup: expected the true default stockpile to already hold enough stone on its own.");

            const long maxTicks = CalendarConstants.TicksPerDay * 10;
            long deliveredByTick = -1;
            long completedAtTick = -1;
            double lastProgress = 0.0;

            for (long i = 0; i < maxTicks; i++)
            {
                sim.Step();

                buildings.TryGetBuilding(lodgingId, out BuildingState site);
                if (site.Construction == null)
                {
                    completedAtTick = i;
                    break;
                }

                int plankDelivered = site.Construction.DeliveredGoods.TryGetValue("good.plank", out int pd) ? pd : 0;
                int stoneDelivered = site.Construction.DeliveredGoods.TryGetValue("good.stone", out int sd) ? sd : 0;
                if (deliveredByTick < 0 && plankDelivered >= planksNeeded && stoneDelivered >= stoneNeeded)
                {
                    deliveredByTick = i;
                }

                double progress = buildings.GetConstructionProgressPercent(lodgingId);
                Assert.GreaterOrEqual(progress, lastProgress - 0.001, $"Expected progress to never go backwards (tick {i}).");
                lastProgress = progress;
            }

            Assert.IsTrue(deliveredByTick >= 0 && deliveredByTick < maxTicks,
                "Expected both planks and stone to eventually be fully hauled over from the stockpile (3.10b3: a founder carries them by hand now, not an instant draw).");
            Assert.IsTrue(completedAtTick >= 0,
                $"Expected the crown lodging to complete within {maxTicks / CalendarConstants.TicksPerDay} days, from the true default stockpile alone, with no console help.");
        }

        /// <summary>The user's actual case: the stockpile plus one building placed, from a true default start.</summary>
        [Test]
        public void Founders_WorkSteadilyThroughTheWholeShift_TrueDefaultStart_OneBuildingPlaced()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, _, _, _, PeopleModule people, JobsModule jobs) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out _, out string placeError), placeError);

            AssertNoIdleOrUntaskedGapsOverTwoDays(sim, gathering, people, jobs);
        }

        /// <summary>
        /// Traces every founder hour by hour over two full days and fails on any hour, during that
        /// founder's work shift, where they are both non-employed and either (a) have no
        /// hand-gathering task at all, or (b) have one but aren't actually acting on it.
        /// </summary>
        private static void AssertNoIdleOrUntaskedGapsOverTwoDays(Simulation sim, GatheringModule gathering, PeopleModule people, JobsModule jobs)
        {
            int dayStart = (int)sim.Settings.Get(JobsModule.DayShiftStartHourSettingId);
            int dayEnd = (int)sim.Settings.Get(JobsModule.DayShiftEndHourSettingId);

            EntityId[] founderIds = people.People.Where(row => people.IsFounder(row.Key)).Select(row => row.Key).ToArray();

            // Let the first (shorter, noon-start) day settle - the very first task assignment lands
            // mid-tick, one calendar hour before ChooseActivity's first-ever call starts the commute -
            // before tracing two full, already-established workdays. That one-hour startup lag is an
            // accepted artifact of the hourly ChooseActivity cadence, not the gap this test is after.
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            var gaps = new List<string>();
            int lastLoggedHour = -1;

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
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
                        TestContext.WriteLine($"day {date.DayOfMonth} hour {date.Hour:D2}: {id} activity={person.Activity}, task={gathering.GetTaskKind(id)}, employed={jobs.IsEmployed(id)}");
                    }
                }

                if (!withinShift)
                {
                    continue;
                }

                foreach (EntityId id in founderIds)
                {
                    if (jobs.IsEmployed(id))
                    {
                        continue; // a real job's own shift governs them instead - not this check's concern
                    }

                    GatherTaskKind task = gathering.GetTaskKind(id);
                    if (task == GatherTaskKind.None)
                    {
                        gaps.Add($"tick {sim.Clock.CurrentTick} (day {date.DayOfMonth} hour {date.Hour}): {id} has no gathering task at all while on shift (idle reason: \"{gathering.DescribeIdleReason(id)}\").");
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
                        gaps.Add($"tick {sim.Clock.CurrentTick} (day {date.DayOfMonth} hour {date.Hour}): {id} was {person.Activity} while on shift with a {task} task.");
                    }
                }
            }

            Assert.IsEmpty(gaps, $"Expected no idle/untasked gaps during work hours:\n{string.Join("\n", gaps)}");
        }

        /// <summary>
        /// 3.9j: the real cause the hand test's panel showed ("task: none", nothing farming or
        /// waiting for a harvest) - a founder fired from a real job outside of AssignTasks' own
        /// trigger events (here, the player's "fire" console command, mid-shift) used to keep
        /// GatherTaskKind.None for the rest of the day, since nothing re-ran AssignTasks until the
        /// next morning. Fixed by having GatheringModule's own hourly system notice and self-heal
        /// within the hour.
        /// </summary>
        [Test]
        public void FounderFiredFromARealJob_MidDay_GetsAHandGatheringTaskWithinTheHour_NotNextMorning()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, JobsModule jobs) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(50, 50), out _, out string placeError), placeError);

            EntityId? firedFounder = null;
            for (long i = 0; i < CalendarConstants.TicksPerDay * 5 && firedFounder == null; i++)
            {
                sim.Step();
                foreach (KeyValuePair<EntityId, PersonState> row in people.People)
                {
                    if (people.IsFounder(row.Key) && jobs.IsEmployed(row.Key))
                    {
                        firedFounder = row.Key;
                        break;
                    }
                }
            }

            Assert.IsNotNull(firedFounder, "Setup: expected at least one founder to have taken a real job within 5 days.");

            ConsoleCommand fireCommand = sim.Commands.All.First(c => c.Name == "fire");
            string result = fireCommand.Handler(new[] { firedFounder.Value.ToString() });
            Assert.IsFalse(result.Contains("doesn't hold a job"), $"Setup: expected the fire command to succeed: {result}");
            Assert.IsFalse(jobs.IsEmployed(firedFounder.Value), "Setup: expected the founder to no longer be employed right after firing.");

            // Well under a day - the hourly self-heal, not the next morning's NewDayEvent.
            for (long i = 0; i < CalendarConstants.TicksPerHour * 2; i++)
            {
                sim.Step();
            }

            Assert.AreNotEqual(GatherTaskKind.None, gathering.GetTaskKind(firedFounder.Value),
                "Expected the just-fired founder to already have a hand-gathering task again, not stay at None until the next morning.");
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
            FullyDeliverConstructionCost(buildings, siteId); // this test is about the farm/build split, not hauling - materials are already fully on site

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
            FullyDeliverConstructionCost(buildings, siteId); // this test is about the farm/build split, not hauling - materials are already fully on site

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
            FullyDeliverConstructionCost(buildings, siteId); // this test is about spreading around the site's edge, not hauling - materials are already fully on site

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
            FullyDeliverConstructionCost(buildings, siteId); // this test is about reassignment timing, not hauling - materials are already fully on site

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
            FullyDeliverConstructionCost(buildings, siteId); // this test is about reassignment timing, not hauling - materials are already fully on site

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
            Assert.Greater(wealth.GetBalance(AccountId.Person(gatherer)).HundredthsOfCopper, 0,
                "The gatherer should have been paid at the market price for what the crown bought.");
        }

        #region Hauling (3.10b3)

        /// <summary>The new rule: goods never move on their own - with nobody at all to haul them, a well-stocked stockpile's goods must never reach a crown site by themselves.</summary>
        [Test]
        public void NoGoodsReachACrownSite_WithoutAHaulerWalkingThemThere()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            // Nobody exists to haul anything - the stockpile's own planks and stone should just sit there.
            foreach (KeyValuePair<EntityId, PersonState> row in people.People.ToList())
            {
                sim.Entities.Destroy(row.Key);
            }

            for (long i = 0; i < CalendarConstants.TicksPerDay * 5; i++)
            {
                sim.Step();
            }

            Assert.IsTrue(buildings.TryGetBuilding(siteId, out BuildingState site));
            Assert.IsNotNull(site.Construction, "Expected the site to still be waiting - nobody exists to haul its materials.");
            int totalDelivered = site.Construction.DeliveredGoods.Values.Sum();
            Assert.AreEqual(0, totalDelivered, "Expected zero goods delivered - the stockpile has plenty, but nothing moves without a hauler.");
        }

        /// <summary>No hauler ever carries more than the carry limit, and the site never ends up with more delivered-plus-carried than it actually needs - no two haulers fetch what the site doesn't need.</summary>
        [Test]
        public void Hauling_RespectsTheCarryLimit_AndNeverOverFetchesBeyondWhatTheSiteStillNeeds()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            int carryLimit = (int)sim.Settings.Get(GatheringModule.HaulCarryLimitSettingId);
            BuildingType type = buildings.GetBuildingType("building.crown_lodging");

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();

                foreach (KeyValuePair<EntityId, PersonState> row in people.People)
                {
                    if (!people.IsFounder(row.Key))
                    {
                        continue;
                    }

                    Assert.LessOrEqual(gathering.GetHaulAmount(row.Key), carryLimit,
                        $"Expected nobody to ever carry more than the {carryLimit}-unit limit (tick {i}).");
                }

                if (!buildings.TryGetBuilding(siteId, out BuildingState site) || site.Construction == null)
                {
                    break;
                }

                foreach (GoodAmount cost in type.ConstructionCost)
                {
                    int delivered = site.Construction.DeliveredGoods.TryGetValue(cost.Good.Id, out int d) ? d : 0;
                    int carried = gathering.GetCarriedTowardSite(siteId, cost.Good.Id);
                    Assert.LessOrEqual(delivered + carried, cost.Amount,
                        $"Expected delivered+carried for {cost.Good.Id} to never exceed what the site needs - no two haulers should ever fetch more than it needs (tick {i}).");
                }
            }
        }

        /// <summary>An interrupted hauler (end of shift) finishes the trip and drops the load at the site first, rather than freezing mid-walk with the goods stuck in their hands.</summary>
        [Test]
        public void InterruptedHauler_FinishesTheTrip_EvenPastShiftEnd()
        {
            using var core = new TempCoreContentCopy();
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
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(220, 220), out EntityId siteId, out string placeError), placeError);

            // Find the first founder to actually pick up a load, however long that takes.
            EntityId? hauler = null;
            for (long i = 0; i < CalendarConstants.TicksPerDay * 10 && hauler == null; i++)
            {
                sim.Step();
                foreach (KeyValuePair<EntityId, PersonState> row in people.People)
                {
                    if (people.IsFounder(row.Key) && gathering.GetHaulAmount(row.Key) > 0)
                    {
                        hauler = row.Key;
                        break;
                    }
                }
            }

            Assert.IsNotNull(hauler, "Setup: expected a founder to pick up a load at some point.");
            int carriedAmount = gathering.GetHaulAmount(hauler.Value);
            Assert.Greater(carriedAmount, 0);

            // Jump straight to the next shift-end boundary, computed from settings rather than hoped
            // for, so this test doesn't depend on however fast movement happens to be.
            int dayEnd = (int)sim.Settings.Get(JobsModule.DayShiftEndHourSettingId);
            long currentTick = sim.Clock.CurrentTick;
            GameDate now = GameDate.FromTick(currentTick);
            long ticksIntoToday = currentTick % CalendarConstants.TicksPerDay;
            long shiftEndTickToday = (long)dayEnd * CalendarConstants.TicksPerHour;
            long ticksUntilShiftEnd = shiftEndTickToday > ticksIntoToday
                ? shiftEndTickToday - ticksIntoToday
                : CalendarConstants.TicksPerDay - ticksIntoToday + shiftEndTickToday;

            for (long i = 0; i < ticksUntilShiftEnd + CalendarConstants.TicksPerHour * 3; i++)
            {
                sim.Step();
            }

            // Confirm this tick really is now off-shift, then keep stepping through it - a founder
            // who's still carrying must still be getting processed (moving, or already delivered),
            // never frozen just because the clock crossed into off-shift hours.
            int dayStart = (int)sim.Settings.Get(JobsModule.DayShiftStartHourSettingId);
            int hourNow = GameDate.FromTick(sim.Clock.CurrentTick).Hour;
            bool onShiftNow = dayStart > dayEnd ? hourNow >= dayStart || hourNow < dayEnd : hourNow >= dayStart && hourNow < dayEnd;
            Assert.IsFalse(onShiftNow, $"Setup: expected to have landed on an off-shift hour by now (hour {hourNow}).");

            bool stillCarrying = gathering.GetHaulAmount(hauler.Value) > 0;
            bool delivered = !stillCarrying;

            if (stillCarrying)
            {
                (double X, double Y) positionBefore = mapMovement.TryGetPosition(hauler.Value, out double x0, out double y0) ? (x0, y0) : default;

                for (long i = 0; i < CalendarConstants.TicksPerHour * 6 && stillCarrying; i++)
                {
                    sim.Step();
                    stillCarrying = gathering.GetHaulAmount(hauler.Value) > 0;
                }

                delivered = !stillCarrying;
                bool stillMoved = mapMovement.TryGetPosition(hauler.Value, out double x1, out double y1) &&
                    (System.Math.Abs(x1 - positionBefore.X) > 0.01 || System.Math.Abs(y1 - positionBefore.Y) > 0.01);

                Assert.IsTrue(delivered || stillMoved,
                    "Expected the hauler to still be getting processed (moving toward the site, or already delivered) well past shift end, not frozen in place with the load stuck in their hands.");
            }

            Assert.IsTrue(delivered, "Expected the load to eventually be delivered despite the shift ending mid-trip.");
        }

        /// <summary>A founder labeled "Building" must actually be adding labor every on-shift hour they stay labeled that way, not stand there with nothing to work with.</summary>
        [Test]
        public void FounderLabeledBuilder_AddsLabor_EveryOnShiftHourTheyStayBuildLabeled()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, _, _, PeopleModule people, JobsModule jobs) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            int dayStart = (int)sim.Settings.Get(JobsModule.DayShiftStartHourSettingId);
            int dayEnd = (int)sim.Settings.Get(JobsModule.DayShiftEndHourSettingId);
            double lastLabor = 0;
            bool checkedAtLeastOnce = false;
            var consecutiveOnShiftBuildHours = new Dictionary<EntityId, int>();

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();

                if (i % CalendarConstants.TicksPerHour != 0)
                {
                    continue;
                }

                if (!buildings.TryGetBuilding(siteId, out BuildingState site) || site.Construction == null)
                {
                    break;
                }

                long currentTick = sim.Clock.CurrentTick;
                int hourOfDay = GameDate.FromTick(currentTick).Hour;
                bool onShift = dayStart > dayEnd ? hourOfDay >= dayStart || hourOfDay < dayEnd : hourOfDay >= dayStart && hourOfDay < dayEnd;

                double laborNow = site.Construction.LaborHoursDone;
                bool laborGrewThisHour = laborNow > lastLabor + 1e-9;
                // The one other exception: the exact hour labor reaches its own cap, with goods
                // still short - the redirect away from Build (see ProcessBuild) only takes effect
                // the *next* time this founder is checked, not retroactively within the capping hour.
                bool laborAlreadyFullyDone = laborNow >= buildings.GetLaborHoursNeeded(siteId) - 1e-6;

                foreach (KeyValuePair<EntityId, PersonState> row in people.People)
                {
                    EntityId id = row.Key;
                    bool isBuilderOnShiftHere = onShift && people.IsFounder(id) && gathering.GetTaskKind(id) == GatherTaskKind.Build && gathering.GetTargetSite(id) == siteId;

                    if (!isBuilderOnShiftHere)
                    {
                        consecutiveOnShiftBuildHours[id] = 0;
                        continue;
                    }

                    // The first on-shift hour might still be the commute there - from the second
                    // straight such hour on, they must actually be contributing.
                    int streak = consecutiveOnShiftBuildHours.TryGetValue(id, out int s) ? s + 1 : 1;
                    consecutiveOnShiftBuildHours[id] = streak;

                    if (streak >= 2 && !laborAlreadyFullyDone)
                    {
                        checkedAtLeastOnce = true;
                        Assert.IsTrue(laborGrewThisHour,
                            $"Expected labor to grow this hour (tick {currentTick}) since {id} has been labeled Building, on shift, for {streak} straight hours - never labeled builder while adding no labor.");
                    }
                }

                lastLabor = laborNow;
            }

            Assert.IsTrue(checkedAtLeastOnce, "Setup: expected at least one founder to stay Build-labeled through 2+ consecutive on-shift hours during this run.");
        }

        /// <summary>Carried goods and an in-progress trip survive a save/load round trip, and the loaded run still delivers the load.</summary>
        [Test]
        public void SaveAndLoad_MidHaul_PreservesTheCarriedLoad_AndStillDelivers()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 777;

            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, InventoryModule inventory, WealthModule wealth, MarketModule market, PeopleModule people) = BuildWithSeed(core, seed);
            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(150, 150), out EntityId siteId, out string placeError), placeError);

            EntityId? hauler = null;
            for (long i = 0; i < CalendarConstants.TicksPerDay * 10 && hauler == null; i++)
            {
                sim.Step();

                foreach (KeyValuePair<EntityId, PersonState> row in people.People)
                {
                    if (people.IsFounder(row.Key) && gathering.GetHaulAmount(row.Key) > 0)
                    {
                        hauler = row.Key;
                        break;
                    }
                }
            }

            Assert.IsNotNull(hauler, "Setup: expected a founder to be mid-haul at some point.");
            int carriedBeforeSave = gathering.GetHaulAmount(hauler.Value);
            Assert.Greater(carriedBeforeSave, 0);

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 9 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModulesWithSeed(out _, out _, out _, out _, out BuildingsModule loadedBuildings, out _, out GatheringModule loadedGathering);
            (Simulation loaded, _) = GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.AreEqual(carriedBeforeSave, loadedGathering.GetHaulAmount(hauler.Value), "Expected the carried load to survive the save/load round trip.");

            loadedBuildings.TryGetBuilding(siteId, out BuildingState siteBefore);
            int deliveredBefore = siteBefore.Construction?.DeliveredGoods.Values.Sum() ?? 0;

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10 && loadedGathering.GetHaulAmount(hauler.Value) > 0; i++)
            {
                loaded.Step();
            }

            Assert.AreEqual(0, loadedGathering.GetHaulAmount(hauler.Value), "Expected the loaded hauler to eventually deliver their load.");
            loadedBuildings.TryGetBuilding(siteId, out BuildingState siteAfter);
            int deliveredAfter = siteAfter.Construction?.DeliveredGoods.Values.Sum() ?? 0;
            Assert.Greater(deliveredAfter, deliveredBefore, "Expected the loaded run's delivered total to have grown once the carried load landed.");
        }

        #endregion

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
            var mapResources = new MapResourcesModule(mapMovement, land);
            gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering };
        }

        private static (Simulation Simulation, GatheringModule Gathering, BuildingsModule Buildings, InventoryModule Inventory, WealthModule Wealth, MarketModule Market, PeopleModule People) BuildWithSeed(TempCoreContentCopy core, ulong seed)
        {
            IModule[] modules = ModulesWithSeed(out MapMovementModule _, out InventoryModule inventory, out PeopleModule people, out WealthModule wealth, out BuildingsModule buildings, out MarketModule market, out GatheringModule gathering);
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, gathering, buildings, inventory, wealth, market, people);
        }
    }
}
