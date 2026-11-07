using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Saves
{
    public class GameSaveTests
    {
        private readonly struct UnsavableTestEvent : ISimEvent
        {
            public const string DefinitionIdValue = "test.unsavable_event";
            public string DefinitionId => DefinitionIdValue;
        }

        private static ModuleRegistration CounterRegistration(CounterModule module)
        {
            return new ModuleRegistration(module, new ModuleManifest("test.counter", 1, Array.Empty<string>(), true));
        }

        [Test]
        public void RoundTrip_StateHashMatches()
        {
            var counter = new CounterModule();
            var sim = new Simulation(999, new[] { CounterRegistration(counter) });

            for (int i = 0; i < 50; i++)
            {
                sim.Step();
            }

            ulong originalHash = sim.ComputeStateHash();

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var loadedCounter = new CounterModule();
            (Simulation loaded, _) = GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), new[] { CounterRegistration(loadedCounter) });

            Assert.AreEqual(originalHash, loaded.ComputeStateHash());
        }

        [Test]
        public void InterruptedRun_WithSaveAndLoad_MatchesUninterruptedRun()
        {
            const ulong seed = 4242;
            const int n = 370; // mid-hour (TicksPerHour = 600)
            const int m = 850; // n + m = 1220, also mid-hour

            var uninterruptedCounter = new CounterModule();
            var uninterruptedSim = new Simulation(seed, new[] { CounterRegistration(uninterruptedCounter) });
            for (int i = 0; i < n + m; i++)
            {
                uninterruptedSim.Step();
            }

            ulong uninterruptedHash = uninterruptedSim.ComputeStateHash();

            var firstCounter = new CounterModule();
            var firstSim = new Simulation(seed, new[] { CounterRegistration(firstCounter) });
            for (int i = 0; i < n; i++)
            {
                firstSim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var secondCounter = new CounterModule();
            (Simulation secondSim, _) = GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), new[] { CounterRegistration(secondCounter) });

            for (int i = 0; i < m; i++)
            {
                secondSim.Step();
            }

            Assert.AreEqual(uninterruptedHash, secondSim.ComputeStateHash());
        }

        [Test]
        public void ReadHeader_DoesNotRequireReadingTheBody()
        {
            var sim = new Simulation(123);
            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "Testholm", Population = 42 }, stream);

            stream.Position = 0;
            SaveHeader header = SaveFile.ReadHeader(stream);

            Assert.AreEqual("Testholm", header.KingdomName);
            Assert.AreEqual(42, header.Population);
            Assert.AreEqual(123UL, header.WorldSeed);
            Assert.Less(stream.Position, stream.Length);
        }

        [Test]
        public void SaveWithModule_LoadedWithoutIt_KeepsSection_AndWritesItBackOnNextSave()
        {
            var counter = new CounterModule();
            var sim = new Simulation(321, new[] { CounterRegistration(counter) });
            for (int i = 0; i < 5; i++)
            {
                sim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            (Simulation loadedWithout, _) = GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), null);

            Assert.IsTrue(loadedWithout.OrphanedModuleSections.ContainsKey("test.counter"));

            using var stream2 = new MemoryStream();
            GameSave.Save(loadedWithout, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream2);
            stream2.Position = 0;

            (_, Dictionary<string, SaveSectionData> sections) = SaveFile.ReadAll(stream2);
            Assert.IsTrue(sections.ContainsKey("test.counter"));
        }

        [Test]
        public void NewerSectionVersion_Development_Throws()
        {
            var saveModule = new VersionedModule(2);
            var saveRegs = new[] { new ModuleRegistration(saveModule, new ModuleManifest("test.versioned", 1, Array.Empty<string>(), true)) };
            var sim = new Simulation(1, saveRegs);

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var loadModule = new VersionedModule(1);
            var loadRegs = new[] { new ModuleRegistration(loadModule, new ModuleManifest("test.versioned", 1, Array.Empty<string>(), true)) };

            Assert.Throws<ModuleException>(() => GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), loadRegs, ErrorMode.Development));
        }

        [Test]
        public void NewerSectionVersion_Release_SwitchesOffModule_AndReports()
        {
            var saveModule = new VersionedModule(2);
            var saveRegs = new[] { new ModuleRegistration(saveModule, new ModuleManifest("test.versioned", 1, Array.Empty<string>(), true)) };
            var sim = new Simulation(1, saveRegs);

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var loadModule = new VersionedModule(1);
            var loadRegs = new[] { new ModuleRegistration(loadModule, new ModuleManifest("test.versioned", 1, Array.Empty<string>(), true)) };

            (Simulation loaded, LoadReport report) = GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), loadRegs, ErrorMode.Release);

            Assert.IsFalse(loaded.Modules.IsActive("test.versioned"));
            Assert.AreEqual(1, report.NewerSectionVersions.Count);
        }

        [Test]
        public void PendingEvents_SurviveSaveAndLoad_AndAreDeliveredOnNextTick()
        {
            var sim = new Simulation(55);
            long tick = sim.Clock.CurrentTick;
            sim.Events.Post(new NewHourEvent(tick), tick);

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            (Simulation loaded, _) = GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), null);

            int hourEventsReceived = 0;
            loaded.Events.Subscribe<NewHourEvent>(e => hourEventsReceived++);

            loaded.Step();

            Assert.AreEqual(1, hourEventsReceived);
        }

        [Test]
        public void SavingPendingTypedEventWithNoSerializer_GivesClearError()
        {
            var sim = new Simulation(1);
            sim.EventDefinitions.Register(new EventDefinition(UnsavableTestEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "test"));
            sim.Events.Post(new UnsavableTestEvent(), sim.Clock.CurrentTick);

            using var stream = new MemoryStream();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream));

            StringAssert.Contains(UnsavableTestEvent.DefinitionIdValue, ex.Message);
        }

        [Test]
        public void Loading_NeverRunsNewGameSetup()
        {
            var counterForSave = new CounterModule();
            var sim = new Simulation(7, new[] { CounterRegistration(counterForSave) });
            Assert.AreEqual(1, counterForSave.SetupCount);

            for (int i = 0; i < 10; i++)
            {
                sim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var counterForLoad = new CounterModule();
            GameSave.Load(stream, Array.Empty<SaveSourceInfo>(), new[] { CounterRegistration(counterForLoad) });

            Assert.AreEqual(0, counterForLoad.SetupCount);
        }

        [Test]
        public void SourceDifferences_AreReported()
        {
            var sim = new Simulation(1);
            var headerInfo = new SaveHeaderInfo
            {
                KingdomName = "K",
                Population = 1,
                Sources = new List<SaveSourceInfo>
                {
                    new SaveSourceInfo { Id = "core", Version = "1.0.0" },
                    new SaveSourceInfo { Id = "modOld", Version = "1.0.0" }
                }
            };

            using var stream = new MemoryStream();
            GameSave.Save(sim, headerInfo, stream);
            stream.Position = 0;

            var currentSources = new List<SaveSourceInfo>
            {
                new SaveSourceInfo { Id = "core", Version = "2.0.0" },
                new SaveSourceInfo { Id = "modNew", Version = "1.0.0" }
            };

            (_, LoadReport report) = GameSave.Load(stream, currentSources, null);

            Assert.GreaterOrEqual(report.SourceDifferences.Count, 3);
        }
    }
}
