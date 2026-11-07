using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Console
{
    /// <summary>
    /// Holds every registered console command and runs input against them.
    /// Unknown or empty input returns the help list.
    /// </summary>
    public sealed class ConsoleCommandRegistry
    {
        private readonly Dictionary<string, ConsoleCommand> _commands = new Dictionary<string, ConsoleCommand>(StringComparer.Ordinal);

        /// <summary>Every registered command, in ascending name order.</summary>
        public IEnumerable<ConsoleCommand> All => _commands.Values.OrderBy(c => c.Name, StringComparer.Ordinal);

        public void Register(ConsoleCommand command)
        {
            if (_commands.ContainsKey(command.Name))
            {
                throw new InvalidOperationException($"A console command named '{command.Name}' is already registered.");
            }

            _commands[command.Name] = command;
        }

        /// <summary>No-op if the name isn't registered.</summary>
        public void Unregister(string name)
        {
            _commands.Remove(name);
        }

        public bool IsRegistered(string name) => _commands.ContainsKey(name);

        public string HelpText()
        {
            return string.Join("\n", All.Select(c => $"{c.Name} - {c.HelpLine}"));
        }

        /// <summary>Tokenizes and runs a line of input. A handler that throws returns an error line instead of propagating.</summary>
        public string Execute(string input)
        {
            string[] tokens = Tokenize(input);
            if (tokens.Length == 0 || !_commands.TryGetValue(tokens[0], out ConsoleCommand command))
            {
                return HelpText();
            }

            var args = new string[tokens.Length - 1];
            Array.Copy(tokens, 1, args, 0, args.Length);

            try
            {
                return command.Handler(args);
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static string[] Tokenize(string input)
        {
            return string.IsNullOrWhiteSpace(input)
                ? Array.Empty<string>()
                : input.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
