using NUnit.Framework;
using TheEmptying.Core.Console;

namespace TheEmptying.Tests.EditMode.Console
{
    public class ConsoleCommandRegistryTests
    {
        [Test]
        public void Execute_ParsesArgumentsAndRunsTheMatchingHandler()
        {
            var registry = new ConsoleCommandRegistry();
            string[] received = null;
            registry.Register(new ConsoleCommand("echo", "echoes its arguments", args =>
            {
                received = args;
                return "ok";
            }));

            string result = registry.Execute("echo one two three");

            Assert.AreEqual("ok", result);
            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, received);
        }

        [Test]
        public void Execute_UnknownCommand_ReturnsTheHelpList()
        {
            var registry = new ConsoleCommandRegistry();
            registry.Register(new ConsoleCommand("help", "lists commands", args => registry.HelpText()));
            registry.Register(new ConsoleCommand("date", "shows the date", args => "today"));

            string result = registry.Execute("nonsense");

            StringAssert.Contains("help", result);
            StringAssert.Contains("date", result);
        }

        [Test]
        public void Execute_EmptyInput_ReturnsTheHelpList()
        {
            var registry = new ConsoleCommandRegistry();
            registry.Register(new ConsoleCommand("date", "shows the date", args => "today"));

            Assert.AreEqual(registry.HelpText(), registry.Execute("   "));
        }

        [Test]
        public void Register_DuplicateName_Throws()
        {
            var registry = new ConsoleCommandRegistry();
            registry.Register(new ConsoleCommand("date", "shows the date", args => "today"));

            Assert.Throws<System.InvalidOperationException>(() =>
                registry.Register(new ConsoleCommand("date", "shows the date again", args => "today")));
        }
    }
}
