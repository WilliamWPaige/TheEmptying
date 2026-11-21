using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
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

namespace TheEmptying.Tests.EditMode.People
{
    /// <summary>3.9h: idle people mill about instead of standing frozen, and a Wandering label always matches real movement.</summary>
    public class PeopleModuleIdleTests
    {
        private static (Simulation Simulation, PeopleModule People, MapMovementModule MapMovement) Build(TempCoreContentCopy core)
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
            return (simulation, people, mapMovement);
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
        public void IdlePeople_MillAbout_OnDifferentTicks_NotAllAtOnce()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            sim.Settings.Set(PeopleModule.NightStartHourSettingId, 0);
            sim.Settings.Set(PeopleModule.NightEndHourSettingId, 0);
            sim.Settings.Set(PeopleModule.IdleMillChancePerCheckSettingId, 1.0); // deterministic: move the instant their bucket comes up

            // 3.9i: the keep's completion event is posted during module Initialize, before any tick
            // has run, so it stays pending until the first Step() call - flush it now (it resolves
            // against the founders it's meant for) so it doesn't also sweep the people spawned below
            // into a gathering task the instant they're stepped.
            sim.Step();

            var ids = new List<EntityId>();
            for (int i = 0; i < 5; i++)
            {
                ids.Add(people.SpawnPerson(30, 30));
            }

            var firstWanderTick = new Dictionary<EntityId, long>();
            for (long tick = 1; tick <= 600 && firstWanderTick.Count < ids.Count; tick++)
            {
                sim.Step();
                foreach (EntityId id in ids)
                {
                    if (!firstWanderTick.ContainsKey(id) && GetPerson(people, id).Activity == PersonActivity.Wandering)
                    {
                        firstWanderTick[id] = tick;
                    }
                }
            }

            Assert.AreEqual(ids.Count, firstWanderTick.Count, "Expected every idle person to have milled about at least once within the first hour.");
            Assert.Greater(firstWanderTick.Values.Distinct().Count(), 1, "Expected idle people to start milling on different ticks, not all on the same one.");
        }

        /// <summary>
        /// 3.9h fix: a person whose chosen wander/mill target has no path (e.g. it's unreachable)
        /// used to stay labeled Wandering forever, since MapMovement only posts MoverArrivedEventId
        /// on success - never on failure - and PeopleModule never listened for the failure event.
        /// </summary>
        [Test]
        public void WanderingActivity_WhenNoPathExists_ResetsToIdle_NotStuckMislabeled()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, MapMovementModule mapMovement) = Build(core);

            EntityId id = people.SpawnPerson(30, 30);
            GridPoint start = mapMovement.GetCell(id);
            var unreachable = new GridPoint(start.X + 5, start.Y);
            mapMovement.Grid.SetWalkable(unreachable, false);

            // Exactly what the wander/mill decision does: issue the move, then label it - but this
            // target can never be reached.
            mapMovement.TryMoveTo(id, unreachable);
            GetPerson(people, id).Activity = PersonActivity.Wandering;

            sim.Step(); // resolves the failing path request and delivers MoverNoPathEventId

            Assert.AreEqual(PersonActivity.Idle, GetPerson(people, id).Activity,
                "A Wandering activity aimed at an unreachable cell should reset to Idle, not stay mislabeled while standing still.");
        }
    }
}
