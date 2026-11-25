using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Discovery;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>
    /// R14: each real trigger event records its category once (never
    /// repeatedly for the same content Id), action descriptions load from
    /// the shipped content, and discoveries survive a save and load round
    /// trip.
    /// </summary>
    public class DiscoveryModuleTests
    {
        /// <summary>A bare Simulation with only a DiscoveryModule - no content loaded, so the trigger event definitions it listens for are registered by hand, the way content normally would.</summary>
        private static (Simulation Simulation, DiscoveryModule Module) BuildBareSimulation()
        {
            var discovery = new DiscoveryModule();
            var manifest = new ModuleManifest(DiscoveryModule.ModuleId, 1, Array.Empty<string>(), true);
            var simulation = new Simulation(1, new[] { new ModuleRegistration(discovery, manifest) });

            foreach (string eventId in new[]
                     {
                         "event.people.item_held",
                         "event.zones.item_stored",
                         "event.people.resource_harvested",
                         "event.people.action_performed",
                         "event.buildings.placed",
                         "event.zones.placed"
                     })
            {
                simulation.EventDefinitions.Register(new EventDefinition(eventId, EventVisibility.Hidden, 0f, 0f, "test"));
            }

            return (simulation, discovery);
        }

        private static void PostAndDeliver(Simulation simulation, string eventId, string contentId, long tick)
        {
            simulation.Events.Post(eventId, tick, new EventData().SetString("contentId", contentId));
            simulation.Events.DeliverPending();
        }

        [TestCase("event.people.item_held", DiscoveryModule.ItemCategory)]
        [TestCase("event.zones.item_stored", DiscoveryModule.ItemCategory)]
        [TestCase("event.people.resource_harvested", DiscoveryModule.ResourceCategory)]
        [TestCase("event.people.action_performed", DiscoveryModule.ActionCategory)]
        [TestCase("event.buildings.placed", DiscoveryModule.BuildingCategory)]
        [TestCase("event.zones.placed", DiscoveryModule.ZoneCategory)]
        public void EachTrigger_RecordsItsCategoryOnce_EvenWhenPostedRepeatedly(string eventId, string expectedCategory)
        {
            (Simulation simulation, DiscoveryModule discovery) = BuildBareSimulation();

            PostAndDeliver(simulation, eventId, "thing.test", 10);
            PostAndDeliver(simulation, eventId, "thing.test", 20);
            PostAndDeliver(simulation, eventId, "thing.test", 30);

            Assert.IsTrue(discovery.IsDiscovered(expectedCategory, "thing.test"));
            Assert.AreEqual(1, discovery.DiscoveredIn(expectedCategory).Count, "The same content Id must be recorded exactly once, no matter how many times its trigger fires.");
            Assert.AreEqual(10, discovery.DiscoveredIn(expectedCategory).Single().Tick, "The tick of the FIRST trigger must be kept, not overwritten by later ones.");

            foreach (string otherCategory in new[] { DiscoveryModule.ItemCategory, DiscoveryModule.ResourceCategory, DiscoveryModule.BuildingCategory, DiscoveryModule.ActionCategory, DiscoveryModule.ZoneCategory })
            {
                if (otherCategory != expectedCategory)
                {
                    Assert.IsFalse(discovery.IsDiscovered(otherCategory, "thing.test"), $"Must not also land in '{otherCategory}'.");
                }
            }
        }

        [Test]
        public void DiscoverEverything_FillsEveryCategory_WithoutRaisingDiscoveredEvents()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var discovery = (DiscoveryModule)modules.First(m => m is DiscoveryModule);

            bool eventRaised = false;
            discovery.Discovered += (category, contentId, tick) => eventRaised = true;

            discovery.DiscoverEverything();

            Assert.IsFalse(eventRaised, "DiscoverEverything is a test cheat; it must never fire the real 'Discovered:' notification.");
            Assert.Greater(discovery.DiscoveredIn(DiscoveryModule.ItemCategory).Count, 0, "Sanity check: should have found real items to discover.");
            Assert.Greater(discovery.DiscoveredIn(DiscoveryModule.ResourceCategory).Count, 0, "Sanity check: should have found real resources to discover.");
            Assert.Greater(discovery.DiscoveredIn(DiscoveryModule.BuildingCategory).Count, 0, "Sanity check: should have found real buildings to discover.");
            Assert.Greater(discovery.DiscoveredIn(DiscoveryModule.ActionCategory).Count, 0, "Sanity check: should have found real action kinds to discover.");
            Assert.Greater(discovery.DiscoveredIn(DiscoveryModule.ZoneCategory).Count, 0, "Sanity check: should have found real zone types to discover.");
        }

        [Test]
        public void ResetDiscoveries_ForgetsEverything()
        {
            (Simulation simulation, DiscoveryModule discovery) = BuildBareSimulation();
            PostAndDeliver(simulation, "event.people.item_held", "item.grub", 5);
            Assert.IsTrue(discovery.IsDiscovered(DiscoveryModule.ItemCategory, "item.grub"));

            discovery.ResetDiscoveries();

            Assert.IsFalse(discovery.IsDiscovered(DiscoveryModule.ItemCategory, "item.grub"));
            Assert.AreEqual(0, discovery.DiscoveredIn(DiscoveryModule.ItemCategory).Count);
        }

        /// <summary>R14/R15: the 6 action kinds (Idle, Walk, Inside a building, Eat, Dig, Gather) each load a non-empty display name and description from the shipped content.</summary>
        [TestCase("action.idle")]
        [TestCase("action.walk")]
        [TestCase("action.inside_building")]
        [TestCase("action.eating")]
        [TestCase("action.digging")]
        [TestCase("action.gathering")]
        public void ActionDescriptions_LoadFromRealContent(string actionId)
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsTrue(simulation.Content.TryGet(BuiltInContentTypes.ActionDefinitionTypeName, actionId, out ContentEntry entry), $"Expected '{actionId}' to be declared.");
            var action = (ActionDefinitionContent)entry.Definition;

            Assert.IsFalse(string.IsNullOrEmpty(action.DisplayName));
            Assert.IsFalse(string.IsNullOrEmpty(action.Description));
        }

        [Test]
        public void RealContent_HasExactlyTheSixActionKinds()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            CollectionAssert.AreEquivalent(
                new[] { "action.idle", "action.walk", "action.inside_building", "action.eating", "action.digging", "action.gathering" },
                simulation.Content.ListByType(BuiltInContentTypes.ActionDefinitionTypeName).Select(e => e.Definition.Id));
        }

        [Test]
        public void SaveAndLoad_DiscoveriesSurviveTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var discoveryBefore = (DiscoveryModule)modules.First(m => m is DiscoveryModule);

            discoveryBefore.DiscoverEverything();
            Assert.Greater(discoveryBefore.DiscoveredIn(DiscoveryModule.ItemCategory).Count, 0, "Sanity check: DiscoverEverything should have found real items to discover.");

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var discoveryAfter = (DiscoveryModule)loadedModules.First(m => m is DiscoveryModule);

            foreach (string category in new[] { DiscoveryModule.ItemCategory, DiscoveryModule.ResourceCategory, DiscoveryModule.BuildingCategory, DiscoveryModule.ActionCategory, DiscoveryModule.ZoneCategory })
            {
                CollectionAssert.AreEquivalent(discoveryBefore.DiscoveredIn(category), discoveryAfter.DiscoveredIn(category), $"Category '{category}' did not round-trip exactly.");
            }
        }

        /// <summary>R15b: placing a zone for the first time records it as a Zones discovery; placing another of the same type does not record it again.</summary>
        [Test]
        public void PlacingAZone_RecordsAZonesDiscoveryOnce()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var discovery = (DiscoveryModule)modules.First(m => m is DiscoveryModule);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;

            Assert.IsFalse(discovery.IsDiscovered(DiscoveryModule.ZoneCategory, ZonesModule.StockpileTypeId));

            EntityId? first = zones.TryPlaceZone(ZonesModule.StockpileTypeId, new OrientedRect(centerX - 20, centerZ, 8, 8, 0), out string reason1);
            Assert.IsNotNull(first, reason1);
            simulation.Events.DeliverPending();

            Assert.IsTrue(discovery.IsDiscovered(DiscoveryModule.ZoneCategory, ZonesModule.StockpileTypeId));
            Assert.AreEqual(1, discovery.DiscoveredIn(DiscoveryModule.ZoneCategory).Count);

            EntityId? second = zones.TryPlaceZone(ZonesModule.StockpileTypeId, new OrientedRect(centerX + 20, centerZ, 8, 8, 0), out string reason2);
            Assert.IsNotNull(second, reason2);
            simulation.Events.DeliverPending();

            Assert.AreEqual(1, discovery.DiscoveredIn(DiscoveryModule.ZoneCategory).Count, "A second stockpile of the same type must not be recorded again.");
        }
    }
}
