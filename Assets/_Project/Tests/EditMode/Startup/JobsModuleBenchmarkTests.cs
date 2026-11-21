using System;
using System.Diagnostics;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
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
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// Explicit performance benchmark for prompt 3.8: 500 people with jobs
    /// across about 60 buildings, for one in-game day after a warm-up.
    /// Reports average and worst ms per tick; fails only on a wildly blown
    /// budget.
    /// </summary>
    public class JobsModuleBenchmarkTests
    {
        private const int PersonCount = 500;
        private const int BuildingCount = 60;
        private const double AverageTickBudgetMs = 4.0;

        [Test]
        public void FiveHundredPeopleWithJobs_About60Buildings_OneInGameDay_StaysWithinBudget()
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

            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            EntityId owner = simulation.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(2_000_000));

            bool discovered = false;
            for (int i = 0; i < BuildingCount; i++)
            {
                var at = new GridPoint(20 + (i % 8) * 10, 20 + (i / 8) * 10);
                land.RequestSetZone(new CellRect(at.X, at.Y, 6, 6), "zone.commercial");
                simulation.Step();
                Assert.IsTrue(land.TryBuyPlot(owner, "zone.commercial", at, out _, out string buyError), buyError);

                EntityId bakeryId;
                if (!buildings.TryRequestPrivateBuilding(owner, "building.bakery", out bakeryId, out _))
                {
                    // Undiscovered (first building only): answering "Allow" makes BuildingsModule retry the
                    // original request internally, so we must not call TryRequestPrivateBuilding again - the
                    // plot would already have a building on it.
                    Assert.IsFalse(discovered, "Discovery should only be needed for the first bakery.");
                    long questionId = simulation.RuleRunner.PendingQuestions[0].QuestionId;
                    simulation.AnswerQuestion(questionId, 1); // Allow
                    simulation.Step();
                    discovered = true;

                    bakeryId = EntityId.None;
                    foreach (var row in buildings.Buildings)
                    {
                        if (row.Value.Owner == owner && row.Value.BuildingTypeId == "building.bakery")
                        {
                            bakeryId = row.Key;
                            break;
                        }
                    }

                    Assert.AreNotEqual(EntityId.None, bakeryId, "Answering 'Allow' should have let BuildingsModule's internal retry build the bakery.");
                }

                inventory.Add(owner, "good.plank", 20);
                inventory.Add(owner, "good.stone", 10);
                buildings.TryDeliverGoods(bakeryId, "good.plank", 20, out _);
                buildings.TryDeliverGoods(bakeryId, "good.stone", 10, out _);
                buildings.TryAddLaborHours(bakeryId, 30, out _);
            }

            for (int i = 0; i < PersonCount; i++)
            {
                EntityId person = people.SpawnPerson(18, 45);
                inventory.Add(person, "good.loaf", 10000);
            }

            // Warm-up: untimed days so hiring settles into its steady state before measuring.
            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                simulation.Step();
            }

            long tickCount = CalendarConstants.TicksPerDay;
            var tickElapsedStopwatchTicks = new long[tickCount];

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

            long previousTimestamp = Stopwatch.GetTimestamp();
            for (long i = 0; i < tickCount; i++)
            {
                simulation.Step();
                long now = Stopwatch.GetTimestamp();
                tickElapsedStopwatchTicks[i] = now - previousTimestamp;
                previousTimestamp = now;
            }

            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            double msPerStopwatchTick = 1000.0 / Stopwatch.Frequency;
            double totalMs = 0.0;
            double worstMs = 0.0;
            for (long i = 0; i < tickCount; i++)
            {
                double ms = tickElapsedStopwatchTicks[i] * msPerStopwatchTick;
                totalMs += ms;
                if (ms > worstMs)
                {
                    worstMs = ms;
                }
            }

            double averageMs = totalMs / tickCount;
            long totalAllocatedBytes = allocatedAfter - allocatedBefore;
            double bytesPerTick = (double)totalAllocatedBytes / tickCount;

            int filled = 0;
            foreach (var row in jobs.Jobs)
            {
                if (row.Value.Worker != null)
                {
                    foreach (EntityId? occupant in row.Value.Worker.Occupants)
                    {
                        if (occupant.HasValue)
                        {
                            filled++;
                        }
                    }
                }
            }

            TestContext.WriteLine($"People: {PersonCount}, buildings: {BuildingCount}, filled worker slots: {filled}, ticks measured: {tickCount}");
            TestContext.WriteLine($"Average tick: {averageMs:F4} ms");
            TestContext.WriteLine($"Worst tick: {worstMs:F4} ms");
            TestContext.WriteLine($"Total GC allocations: {totalAllocatedBytes:N0} bytes ({bytesPerTick:F1} bytes/tick)");

            Assert.Less(averageMs, AverageTickBudgetMs, $"Average tick time {averageMs:F4} ms exceeded the {AverageTickBudgetMs} ms budget.");
        }
    }
}
