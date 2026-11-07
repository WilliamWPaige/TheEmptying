using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Tests.EditMode.Modules
{
    public class ModuleHostTests
    {
        private readonly struct TestEvent : ISimEvent
        {
            public const string DefinitionIdValue = "test.module_event";

            public string DefinitionId => DefinitionIdValue;
        }

        private sealed class RecordingModule : IModule
        {
            private readonly List<string> _log;

            public string Id { get; }
            public int Version => 1;
            public IReadOnlyList<string> Dependencies { get; }
            public Action<ModuleContext> OnInitialize;

            public RecordingModule(string id, List<string> log, params string[] dependencies)
            {
                Id = id;
                _log = log;
                Dependencies = dependencies;
            }

            public void Initialize(ModuleContext context)
            {
                _log.Add($"init:{Id}");
                OnInitialize?.Invoke(context);
            }

            public void Shutdown()
            {
                _log.Add($"shutdown:{Id}");
            }
        }

        private sealed class ThrowingSystem : ISimSystem
        {
            public string Id { get; }

            public ThrowingSystem(string id)
            {
                Id = id;
            }

            public void Update(long tick) => throw new InvalidOperationException("system boom");
        }

        private sealed class CountingSystem : ISimSystem
        {
            public string Id { get; }
            public int RunCount { get; private set; }

            public CountingSystem(string id)
            {
                Id = id;
            }

            public void Update(long tick) => RunCount++;
        }

        private static (ModuleHost host, Scheduler scheduler, EventBus events, EventDefinitionRegistry definitions, long[] tick) CreateHost(ErrorMode mode = ErrorMode.Development)
        {
            var definitions = new EventDefinitionRegistry();
            var events = new EventBus(definitions);
            var scheduler = new Scheduler();
            var entities = new EntityRegistry();
            var random = new RandomService(1);
            var tick = new long[1];
            var host = new ModuleHost(events, scheduler, entities, definitions, random, () => tick[0], mode);
            return (host, scheduler, events, definitions, tick);
        }

        [Test]
        public void Dependencies_InitializeBeforeDependents_TiesOrderById()
        {
            var log = new List<string>();
            (ModuleHost host, _, _, _, _) = CreateHost();

            var moduleC = new RecordingModule("c", log, "a", "b");
            var moduleB = new RecordingModule("b", log);
            var moduleA = new RecordingModule("a", log);

            host.Setup(new[]
            {
                new ModuleRegistration(moduleC, new ModuleManifest("c", 1, new[] { "a", "b" }, true)),
                new ModuleRegistration(moduleB, new ModuleManifest("b", 1, Array.Empty<string>(), true)),
                new ModuleRegistration(moduleA, new ModuleManifest("a", 1, Array.Empty<string>(), true)),
            });

            CollectionAssert.AreEqual(new[] { "init:a", "init:b", "init:c" }, log);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, host.InitializationOrder);
        }

        [Test]
        public void DisablingAModule_DisablesDirectAndIndirectDependents_WithReasonsListed()
        {
            var log = new List<string>();
            (ModuleHost host, _, _, _, _) = CreateHost();

            var moduleA = new RecordingModule("a", log);
            var moduleB = new RecordingModule("b", log, "a");
            var moduleC = new RecordingModule("c", log, "b");

            host.Setup(new[]
            {
                new ModuleRegistration(moduleA, new ModuleManifest("a", 1, Array.Empty<string>(), false)),
                new ModuleRegistration(moduleB, new ModuleManifest("b", 1, new[] { "a" }, true)),
                new ModuleRegistration(moduleC, new ModuleManifest("c", 1, new[] { "b" }, true)),
            });

            CollectionAssert.IsEmpty(log);
            CollectionAssert.IsEmpty(host.InitializationOrder);

            Assert.AreEqual(3, host.DisabledReasons.Count);
            StringAssert.Contains("'a' is disabled", host.DisabledReasons[0]);
            Assert.IsTrue(Contains(host.DisabledReasons, "'b'", "'a'"));
            Assert.IsTrue(Contains(host.DisabledReasons, "'c'", "'b'"));
        }

        private static bool Contains(IReadOnlyList<string> reasons, string a, string b)
        {
            foreach (string reason in reasons)
            {
                if (reason.Contains(a) && reason.Contains(b))
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void MissingDependency_ProducesError()
        {
            (ModuleHost host, _, _, _, _) = CreateHost();
            var module = new RecordingModule("a", new List<string>(), "missing");

            ModuleValidationException ex = Assert.Throws<ModuleValidationException>(() => host.Setup(new[]
            {
                new ModuleRegistration(module, new ModuleManifest("a", 1, new[] { "missing" }, true))
            }));

            StringAssert.Contains("'a'", ex.Message);
            StringAssert.Contains("'missing'", ex.Message);
        }

        [Test]
        public void CircularDependency_ProducesError()
        {
            (ModuleHost host, _, _, _, _) = CreateHost();
            var log = new List<string>();
            var moduleA = new RecordingModule("a", log, "b");
            var moduleB = new RecordingModule("b", log, "a");

            ModuleValidationException ex = Assert.Throws<ModuleValidationException>(() => host.Setup(new[]
            {
                new ModuleRegistration(moduleA, new ModuleManifest("a", 1, new[] { "b" }, true)),
                new ModuleRegistration(moduleB, new ModuleManifest("b", 1, new[] { "a" }, true)),
            }));

            StringAssert.Contains("a", ex.Message);
            StringAssert.Contains("b", ex.Message);
        }

        [Test]
        public void DuplicateModuleId_ProducesError()
        {
            (ModuleHost host, _, _, _, _) = CreateHost();
            var log = new List<string>();
            var moduleA1 = new RecordingModule("a", log);
            var moduleA2 = new RecordingModule("a", log);

            ModuleValidationException ex = Assert.Throws<ModuleValidationException>(() => host.Setup(new[]
            {
                new ModuleRegistration(moduleA1, new ModuleManifest("a", 1, Array.Empty<string>(), true)),
                new ModuleRegistration(moduleA2, new ModuleManifest("a", 1, Array.Empty<string>(), true)),
            }));

            StringAssert.Contains("'a'", ex.Message);
        }

        [Test]
        public void Shutdown_RunsInReverseInitializationOrder()
        {
            var log = new List<string>();
            (ModuleHost host, _, _, _, _) = CreateHost();

            var moduleA = new RecordingModule("a", log);
            var moduleB = new RecordingModule("b", log, "a");

            host.Setup(new[]
            {
                new ModuleRegistration(moduleA, new ModuleManifest("a", 1, Array.Empty<string>(), true)),
                new ModuleRegistration(moduleB, new ModuleManifest("b", 1, new[] { "a" }, true)),
            });

            log.Clear();
            host.Shutdown();

            CollectionAssert.AreEqual(new[] { "shutdown:b", "shutdown:a" }, log);
        }

        [Test]
        public void DevelopmentMode_ThrowingSystemUpdate_ProducesErrorNamingModule()
        {
            (ModuleHost host, Scheduler scheduler, _, _, long[] tick) = CreateHost(ErrorMode.Development);
            var log = new List<string>();
            var module = new RecordingModule("a", log)
            {
                OnInitialize = ctx => ctx.RegisterSystem(new ThrowingSystem("a.system"), 1, 0, 0)
            };

            host.Setup(new[] { new ModuleRegistration(module, new ModuleManifest("a", 1, Array.Empty<string>(), true)) });

            tick[0] = 1;
            ModuleException ex = Assert.Throws<ModuleException>(() => scheduler.RunTick(tick[0]));

            Assert.AreEqual("a", ex.ModuleId);
            StringAssert.Contains("'a'", ex.Message);
        }

        [Test]
        public void ReleaseMode_ThrowingSystem_SwitchesOffModuleAndDependents_OthersKeepRunning_FailureReportRecorded()
        {
            (ModuleHost host, Scheduler scheduler, EventBus events, EventDefinitionRegistry definitions, long[] tick) = CreateHost(ErrorMode.Release);
            definitions.Register(new EventDefinition(TestEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "test"));

            var log = new List<string>();
            var dependentEventCount = new int[1];
            var okSystem = new CountingSystem("ok.system");

            var failingModule = new RecordingModule("failing", log)
            {
                OnInitialize = ctx => ctx.RegisterSystem(new ThrowingSystem("failing.system"), 1, 0, 0)
            };
            var dependentModule = new RecordingModule("dependent", log, "failing")
            {
                OnInitialize = ctx => ctx.Subscribe<TestEvent>(e => dependentEventCount[0]++)
            };
            var okModule = new RecordingModule("ok", log)
            {
                OnInitialize = ctx => ctx.RegisterSystem(okSystem, 1, 0, 0)
            };

            host.Setup(new[]
            {
                new ModuleRegistration(failingModule, new ModuleManifest("failing", 1, Array.Empty<string>(), true)),
                new ModuleRegistration(dependentModule, new ModuleManifest("dependent", 1, new[] { "failing" }, true)),
                new ModuleRegistration(okModule, new ModuleManifest("ok", 1, Array.Empty<string>(), true)),
            });

            tick[0] = 1;
            Assert.DoesNotThrow(() => scheduler.RunTick(tick[0]));

            Assert.AreEqual(1, okSystem.RunCount);
            Assert.IsFalse(host.IsActive("failing"));
            Assert.IsFalse(host.IsActive("dependent"));
            Assert.IsTrue(host.IsActive("ok"));

            Assert.AreEqual(1, host.FailureReports.Count);
            ModuleFailureReport report = host.FailureReports[0];
            Assert.AreEqual("failing", report.ModuleId);
            Assert.AreEqual("system update", report.Call);
            Assert.AreEqual(1, report.Tick);

            events.Post(new TestEvent(), tick[0]);
            events.DeliverPending();
            Assert.AreEqual(0, dependentEventCount[0]);

            tick[0] = 2;
            scheduler.RunTick(tick[0]);
            Assert.AreEqual(2, okSystem.RunCount);
            Assert.AreEqual(1, host.FailureReports.Count);
        }
    }
}
