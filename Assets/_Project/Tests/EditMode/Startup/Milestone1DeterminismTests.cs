using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.DemoHunger;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// The milestone 1 check (Phase 5 Technical Framework, prompt 1.9): the
    /// demo module exercises the real core content source end to end, and
    /// determinism holds across an uninterrupted run and a save/load
    /// interruption alike.
    /// </summary>
    public class Milestone1DeterminismTests
    {
        private static void RunTicks(Simulation simulation, long count)
        {
            for (long i = 0; i < count; i++)
            {
                simulation.Step();
            }
        }

        private static List<(EntityId Entity, long Tick)> SubscribeToStarvingEvents(Simulation simulation, List<(EntityId Entity, long Tick)> into)
        {
            simulation.Events.Subscribe(DemoHungerModule.StarvingEventId, posted => into.Add((posted.Data.GetEntity("entity"), posted.Tick)));
            return into;
        }

        [Test]
        public void SameSeedAndSources_TwoDayRuns_ProduceIdenticalStateHashes()
        {
            using var core = new TempCoreContentCopy();
            string[] sources = { core.FolderPath };

            (Simulation simA, _) = SimulationBuilder.Build(1, sources, new IModule[] { new DemoHungerModule() });
            (Simulation simB, _) = SimulationBuilder.Build(1, sources, new IModule[] { new DemoHungerModule() });

            RunTicks(simA, CalendarConstants.TicksPerDay * 2);
            RunTicks(simB, CalendarConstants.TicksPerDay * 2);

            Assert.AreEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
        }

        [Test]
        public void InterruptedTwoDayRun_WithSaveAndLoad_MatchesAnUnbrokenRun_StateHashAndStarvingEvents()
        {
            using var core = new TempCoreContentCopy();
            string[] sources = { core.FolderPath };
            const ulong seed = 777;

            (Simulation unbroken, _) = SimulationBuilder.Build(seed, sources, new IModule[] { new DemoHungerModule() });
            var unbrokenStarving = SubscribeToStarvingEvents(unbroken, new List<(EntityId, long)>());
            RunTicks(unbroken, CalendarConstants.TicksPerDay * 2);
            ulong unbrokenHash = unbroken.ComputeStateHash();

            (Simulation day1, _) = SimulationBuilder.Build(seed, sources, new IModule[] { new DemoHungerModule() });
            var interruptedStarving = SubscribeToStarvingEvents(day1, new List<(EntityId, long)>());
            RunTicks(day1, CalendarConstants.TicksPerDay);

            using var stream = new MemoryStream();
            GameSave.Save(day1, new SaveHeaderInfo { KingdomName = "K", Population = day1.Entities.Count }, stream);
            stream.Position = 0;

            (Simulation day2, _) = GameSave.Load(stream, sources, new IModule[] { new DemoHungerModule() });
            SubscribeToStarvingEvents(day2, interruptedStarving);
            RunTicks(day2, CalendarConstants.TicksPerDay);

            ulong interruptedHash = day2.ComputeStateHash();

            Assert.AreEqual(unbrokenHash, interruptedHash, "State hash after an interrupted run should match an unbroken run from the same seed.");
            Assert.Greater(unbrokenStarving.Count, 0, "Sanity check: the default decay setting should have produced at least one starving event over 2 days.");
            CollectionAssert.AreEqual(unbrokenStarving, interruptedStarving, "The full list of starving events (entity, tick) should match exactly.");
        }

        [Test]
        public void DifferentSeed_OneDayRun_GivesADifferentStateHash()
        {
            using var core = new TempCoreContentCopy();
            string[] sources = { core.FolderPath };

            (Simulation simA, _) = SimulationBuilder.Build(1, sources, new IModule[] { new DemoHungerModule() });
            (Simulation simB, _) = SimulationBuilder.Build(2, sources, new IModule[] { new DemoHungerModule() });

            RunTicks(simA, CalendarConstants.TicksPerDay);
            RunTicks(simB, CalendarConstants.TicksPerDay);

            Assert.AreNotEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
        }

        [Test]
        public void SwitchingTheDemoModuleOff_ViaModManifest_BuildSucceeds_SystemsNeverRun_AndLoadingAnOldSaveKeepsItsSectionOrphaned()
        {
            using var core = new TempCoreContentCopy();
            using var modOff = new TempContentSource("demo_hunger_off");
            modOff.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""module.demo_hunger"", ""version"": 1, ""dependencies"": [], ""enabled"": false }
                ]
            }");

            string[] onSources = { core.FolderPath };
            string[] offSources = { core.FolderPath, modOff.FolderPath };

            (Simulation on, _) = SimulationBuilder.Build(1, onSources, new IModule[] { new DemoHungerModule() });
            on.Step();

            using var stream = new MemoryStream();
            GameSave.Save(on, new SaveHeaderInfo { KingdomName = "K", Population = on.Entities.Count }, stream);
            stream.Position = 0;

            (Simulation off, StartupReport report) = SimulationBuilder.Build(1, offSources, new IModule[] { new DemoHungerModule() });

            Assert.IsFalse(off.Modules.IsActive(DemoHungerModule.ModuleId), "The module should be disabled by the mod's manifest.");
            Assert.Greater(report.DisabledModuleReasons.Count, 0);
            Assert.AreEqual(0, off.Entities.Count, "Initialize (where the demo module creates its people) should never have run.");

            (Simulation loaded, _) = GameSave.Load(stream, offSources, new IModule[] { new DemoHungerModule() });

            Assert.IsFalse(loaded.Modules.IsActive(DemoHungerModule.ModuleId));
            Assert.IsTrue(loaded.OrphanedModuleSections.ContainsKey(DemoHungerModule.ModuleId), "The save's demo-hunger section should be kept untouched as an orphan.");
        }
    }
}
