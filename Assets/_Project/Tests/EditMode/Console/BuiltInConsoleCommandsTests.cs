using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Console
{
    public class BuiltInConsoleCommandsTests
    {
        private sealed class DeclaringModule : IModule, IModuleDeclaration
        {
            public string Id => "test.declaring";
            public int Version => 1;
            public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();

            public void Declare(ModuleDeclarationContext context)
            {
                context.Settings.Declare(new SettingDefinition
                {
                    Id = "test.rate",
                    ValueType = SettingValueType.Number,
                    Default = 5,
                    Minimum = 0,
                    Maximum = 10,
                    Category = "Test",
                    Description = "A test rate."
                });
            }

            public void Initialize(ModuleContext context)
            {
            }

            public void Shutdown()
            {
            }
        }

        private sealed class InspectableTestModule : IModule, IInspectableModule
        {
            public string Id => "test.inspectable";
            public int Version => 1;
            public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();

            public void Initialize(ModuleContext context)
            {
            }

            public void Shutdown()
            {
            }

            public IEnumerable<string> Inspect(EntityId entity)
            {
                yield return "likes turnips: true";
            }
        }

        private sealed class CommandModule : IModule
        {
            public bool ThrowAfterRegistering;

            public string Id => "test.command";
            public int Version => 1;
            public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();

            public void Initialize(ModuleContext context)
            {
                context.RegisterCommand(new ConsoleCommand("ping", "replies pong", args => "pong"));

                if (ThrowAfterRegistering)
                {
                    throw new InvalidOperationException("boom");
                }
            }

            public void Shutdown()
            {
            }
        }

        private static ModuleRegistration Registration(IModule module, string id)
        {
            return new ModuleRegistration(module, new ModuleManifest(id, 1, Array.Empty<string>(), true));
        }

        [Test]
        public void Skip_AdvancesTheClockByTheRequestedAmount()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string result = simulation.Commands.Execute("skip 2 h");

            Assert.AreEqual(2 * CalendarConstants.TicksPerHour, simulation.Clock.CurrentTick);
            StringAssert.Contains("Skipped", result);
        }

        [Test]
        public void Set_ClampsOutOfRangeValues_AndReportsIt()
        {
            var module = new DeclaringModule();
            var simulation = new Simulation(1, new[] { Registration(module, module.Id) });
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string result = simulation.Commands.Execute("set test.rate 999");

            Assert.AreEqual(10, simulation.Settings.Get("test.rate"));
            StringAssert.Contains("clamped", result);
        }

        [Test]
        public void Inspect_ListsEveryActiveModulesLinesForTheEntity()
        {
            var module = new InspectableTestModule();
            var simulation = new Simulation(1, new[] { Registration(module, module.Id) });
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            EntityId id = simulation.Entities.Create(EntityKind.Person);

            string result = simulation.Commands.Execute($"inspect {id}");

            StringAssert.Contains("[test.inspectable]", result);
            StringAssert.Contains("likes turnips: true", result);
        }

        [Test]
        public void Inspect_AlsoListsTheEntitysFlagsAndCounters()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            EntityId id = simulation.Entities.Create(EntityKind.Person);
            simulation.Memory.SetFlag(id, "known", true);
            simulation.Memory.SetCounter(id, "visits", 3);

            string result = simulation.Commands.Execute($"inspect {id}");

            StringAssert.Contains("[memory] flag.known = true", result);
            StringAssert.Contains("[memory] counter.visits = 3", result);
        }

        [Test]
        public void Memory_ListsAnEntitysFlagsAndCounters()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            EntityId id = simulation.Entities.Create(EntityKind.Person);
            simulation.Memory.SetFlag(id, "known", true);

            string result = simulation.Commands.Execute($"memory {id}");

            StringAssert.Contains("flag.known = true", result);
        }

        [Test]
        public void Memory_Kingdom_ListsKingdomOwnedFlagsAndCounters()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            simulation.Memory.SetCounter(EntityId.None, "gold", 42);

            string result = simulation.Commands.Execute("memory kingdom");

            StringAssert.Contains("counter.gold = 42", result);
        }

        [Test]
        public void Speed_WithWrongArguments_PrintsItsOwnUsageLine_NotTheFullHelpList()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string result = simulation.Commands.Execute("speed");

            Assert.AreEqual("Usage: speed <1|2|3>", result);
        }

        [Test]
        public void UnknownCommand_PrintsTheUnknownCommandMessage_NotTheFullHelpList()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string result = simulation.Commands.Execute("zzz");

            Assert.AreEqual("Unknown command: zzz. Type help for the list.", result);
        }

        [Test]
        public void Logs_WithWrongArguments_PrintsItsOwnUsageLine()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string result = simulation.Commands.Execute("logs bogus");

            Assert.AreEqual("Usage: logs <on|off|errors>", result);
        }

        [Test]
        public void Logs_WithAValidMode_PassesTheLowercasedModeToPresentation()
        {
            var simulation = new Simulation(1);
            var presentation = new FakePresentationCommands();
            BuiltInConsoleCommands.RegisterAll(simulation, presentation);

            simulation.Commands.Execute("logs ERRORS");

            Assert.AreEqual("errors", presentation.LastLogFilterMode);
        }

        [Test]
        public void Saves_DelegatesToPresentation()
        {
            var simulation = new Simulation(1);
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string result = simulation.Commands.Execute("saves");

            Assert.AreEqual("(fake saves list)", result);
        }

        [Test]
        public void DisabledModule_CommandsAreRemoved()
        {
            var module = new CommandModule { ThrowAfterRegistering = true };
            var simulation = new Simulation(1, new[] { Registration(module, module.Id) }, ErrorMode.Release);

            Assert.IsFalse(simulation.Modules.IsActive("test.command"));
            Assert.IsFalse(simulation.Commands.IsRegistered("ping"));
        }
    }
}
