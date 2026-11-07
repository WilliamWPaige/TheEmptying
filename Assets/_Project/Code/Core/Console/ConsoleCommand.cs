using System;

namespace TheEmptying.Core.Console
{
    /// <summary>A console command: a name, a short help line, and a handler that takes arguments and returns text output.</summary>
    public readonly struct ConsoleCommand
    {
        public string Name { get; }
        public string HelpLine { get; }
        public Func<string[], string> Handler { get; }

        public ConsoleCommand(string name, string helpLine, Func<string[], string> handler)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }

            Name = name;
            HelpLine = helpLine ?? string.Empty;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }
    }
}
