using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Core.Console
{
    /// <summary>Registers every milestone-1 built-in command directly on the Simulation's own registry (not through a ModuleContext, so they're never removed).</summary>
    public static class BuiltInConsoleCommands
    {
        public static void RegisterAll(Simulation simulation, IPresentationCommands presentation, ILogSink logSink = null)
        {
            ConsoleCommandRegistry commands = simulation.Commands;
            ILogSink traceLogSink = logSink ?? presentation as ILogSink;

            commands.Register(new ConsoleCommand("help", "Lists every command.", args => commands.HelpText()));

            commands.Register(new ConsoleCommand("pause", "Toggles pause.", args =>
            {
                presentation.SetPaused(!presentation.IsPaused);
                return presentation.IsPaused ? "Paused." : "Resumed.";
            }));

            commands.Register(new ConsoleCommand("speed", "speed <1|2|3> - sets the game speed.", args =>
            {
                if (args.Length != 1 || !ConsoleArgs.TryParseInt(args[0], out int speed) || speed < 1 || speed > 3)
                {
                    return "Usage: speed <1|2|3>";
                }

                presentation.SetSpeed(speed);
                return $"Speed set to {speed}x.";
            }));

            commands.Register(new ConsoleCommand("skip", "skip <number> <h|d> - runs ticks immediately.", args =>
            {
                if (args.Length != 2 || !ConsoleArgs.TryParseInt(args[0], out int amount) || amount <= 0)
                {
                    return "Usage: skip <number> <h|d>";
                }

                long ticks;
                switch (args[1].ToLowerInvariant())
                {
                    case "h":
                        ticks = amount * CalendarConstants.TicksPerHour;
                        break;
                    case "d":
                        ticks = amount * CalendarConstants.TicksPerDay;
                        break;
                    default:
                        return "Usage: skip <number> <h|d>";
                }

                for (long i = 0; i < ticks; i++)
                {
                    simulation.Step();
                }

                return $"Skipped to {simulation.Clock.CurrentDate}.";
            }));

            commands.Register(new ConsoleCommand("date", "Shows the current in-game date.", args => simulation.Clock.CurrentDate.ToString()));

            commands.Register(new ConsoleCommand("seed", "Shows the world seed.", args => simulation.Random.WorldSeed.ToString(CultureInfo.InvariantCulture)));

            commands.Register(new ConsoleCommand("hash", "Shows the current state hash.", args => simulation.ComputeStateHash().ToString(CultureInfo.InvariantCulture)));

            commands.Register(new ConsoleCommand("modules", "Lists enabled and disabled modules, with reasons.", args => FormatModules(simulation)));

            commands.Register(new ConsoleCommand("events", "Lists registered event definitions.", args => FormatEvents(simulation)));

            commands.Register(new ConsoleCommand("settings", "Lists every setting and its value.", args => FormatSettings(simulation)));

            commands.Register(new ConsoleCommand("set", "set <settingId> <value> - sets a setting, clamped to its range.", args =>
            {
                if (args.Length != 2 || !ConsoleArgs.TryParseDouble(args[1], out double value))
                {
                    return "Usage: set <settingId> <value>";
                }

                SettingsRegistry.SetResult result = simulation.Settings.Set(args[0], value);
                return result.WasClamped
                    ? $"{args[0]} set to {result.Value} (clamped)."
                    : $"{args[0]} set to {result.Value}.";
            }));

            commands.Register(new ConsoleCommand("spawn", "spawn <kind> - creates a test entity (Person, Building, Item, Group, Site).", args =>
            {
                if (args.Length != 1 || !Enum.TryParse(args[0], true, out EntityKind kind))
                {
                    return "Usage: spawn <kind> (Person, Building, Item, Group, Site)";
                }

                EntityId id = simulation.Entities.Create(kind);
                return $"Created {id} ({kind}).";
            }));

            commands.Register(new ConsoleCommand("entities", "Lists every living entity.", args => FormatEntities(simulation)));

            commands.Register(new ConsoleCommand("inspect", "inspect <id> - shows every module's data for an entity (e.g. e12.3).", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !simulation.Entities.IsAlive(id))
                {
                    return "Usage: inspect <id> (e.g. e12.3)";
                }

                List<string> lines = simulation.Modules.Inspect(id).ToList();
                lines.AddRange(simulation.Memory.Describe(id).Select(line => $"[memory] {line}"));
                return lines.Count == 0 ? $"{id}: no module has data for this entity." : string.Join("\n", lines);
            }));

            commands.Register(new ConsoleCommand("memory", "memory <entityId|kingdom> - lists an entity's (or the kingdom's) flags and counters.", args =>
            {
                if (args.Length != 1)
                {
                    return "Usage: memory <entityId|kingdom>";
                }

                EntityId owner;
                if (string.Equals(args[0], "kingdom", StringComparison.OrdinalIgnoreCase))
                {
                    owner = EntityId.None;
                }
                else if (!EntityId.TryParse(args[0], out owner))
                {
                    return "Usage: memory <entityId|kingdom> (e.g. e12.3)";
                }

                List<string> lines = simulation.Memory.Describe(owner).ToList();
                string label = owner.IsNone ? "kingdom" : owner.ToString();
                return lines.Count == 0 ? $"{label}: no flags or counters." : string.Join("\n", lines);
            }));

            commands.Register(new ConsoleCommand("questions", "Lists pending questions, oldest first.", args => FormatQuestions(simulation)));

            commands.Register(new ConsoleCommand("answer", "answer <questionId> <choiceNumber> - answers a pending question.", args =>
            {
                if (args.Length != 2 || !ConsoleArgs.TryParseInt(args[0], out int questionIdInt) || !ConsoleArgs.TryParseInt(args[1], out int choiceNumber))
                {
                    return "Usage: answer <questionId> <choiceNumber>";
                }

                string rejection = simulation.AnswerQuestion(questionIdInt, choiceNumber);
                return rejection ?? $"Answer to question {questionIdInt} queued (choice {choiceNumber}).";
            }));

            commands.Register(new ConsoleCommand("save", "save <name> - saves the game.", args =>
                args.Length == 1 ? presentation.Save(args[0]) : "Usage: save <name>"));

            commands.Register(new ConsoleCommand("load", "load <name> - loads a save.", args =>
                args.Length == 1 ? presentation.Load(args[0]) : "Usage: load <name>"));

            commands.Register(new ConsoleCommand("saves", "Lists available saves with their saved-at and in-game dates.", args => presentation.ListSaves()));

            commands.Register(new ConsoleCommand("logs", "logs <on|off|errors> - controls whether Unity log messages appear in the console.", args =>
            {
                string mode = args.Length == 1 ? args[0].ToLowerInvariant() : null;
                if (mode != "on" && mode != "off" && mode != "errors")
                {
                    return "Usage: logs <on|off|errors>";
                }

                return presentation.SetLogFilter(mode);
            }));

            commands.Register(new ConsoleCommand("rules", "Lists every rule: active, fires this game, paused instances, pending questions.", args => FormatRules(simulation)));

            commands.Register(new ConsoleCommand("trace", "trace <ruleId> <on|off> - writes a rule's run segments to the log as short text.", args =>
            {
                if (args.Length != 2 || (args[1] != "on" && args[1] != "off"))
                {
                    return "Usage: trace <ruleId> <on|off>";
                }

                if (simulation.RuleRunner == null)
                {
                    return "No rule runner.";
                }

                string ruleId = args[0];

                if (args[1] == "off")
                {
                    simulation.RuleRunner.ClearTraceListener(ruleId);
                    return $"Tracing off for '{ruleId}'.";
                }

                if (traceLogSink == null)
                {
                    return "No log sink available to trace to.";
                }

                simulation.RuleRunner.SetTraceListener(ruleId, new RuleConsoleTraceListener(traceLogSink));
                return $"Tracing on for '{ruleId}'.";
            }));
        }

        private static string FormatRules(Simulation simulation)
        {
            if (simulation.RuleRunner == null)
            {
                return "(no rule runner)";
            }

            List<string> ruleIds = simulation.RuleRunner.ActivatedRuleIds.ToList();
            if (ruleIds.Count == 0)
            {
                return "(no activated rules)";
            }

            return string.Join("\n", ruleIds.Select(id =>
                $"{id}: {(simulation.RuleRunner.IsRuleActive(id) ? "active" : "inactive")}, fires {simulation.RuleRunner.TotalFires(id)}, paused {simulation.RuleRunner.PausedInstanceCount(id)}, pending questions {simulation.RuleRunner.PendingQuestionCount(id)}"));
        }

        private static string FormatModules(Simulation simulation)
        {
            List<string> enabled = simulation.Modules.RegisteredModuleIds.Where(simulation.Modules.IsActive).OrderBy(i => i, StringComparer.Ordinal).ToList();
            List<string> disabled = simulation.Modules.RegisteredModuleIds.Where(id => !simulation.Modules.IsActive(id)).OrderBy(i => i, StringComparer.Ordinal).ToList();

            var sb = new StringBuilder();
            sb.Append("Enabled: ").Append(enabled.Count == 0 ? "(none)" : string.Join(", ", enabled));
            sb.Append("\nDisabled: ").Append(disabled.Count == 0 ? "(none)" : string.Join(", ", disabled));

            foreach (string reason in simulation.Modules.DisabledReasons)
            {
                sb.Append("\n  ").Append(reason);
            }

            return sb.ToString();
        }

        private static string FormatEvents(Simulation simulation)
        {
            List<EventDefinition> definitions = simulation.EventDefinitions.All.ToList();
            return definitions.Count == 0
                ? "(no event definitions registered)"
                : string.Join("\n", definitions.Select(d => $"{d.Id} ({d.Visibility}, reporting {d.ReportingScore}, notability {d.NotabilityScore})"));
        }

        private static string FormatSettings(Simulation simulation)
        {
            List<SettingDefinition> definitions = simulation.Settings.ExportAll().ToList();
            return definitions.Count == 0
                ? "(no settings declared)"
                : string.Join("\n", definitions.Select(d => $"{d.Id} = {simulation.Settings.Get(d.Id)} [{d.Minimum}..{d.Maximum}] ({d.Category})"));
        }

        private static string FormatQuestions(Simulation simulation)
        {
            if (simulation.RuleRunner == null || simulation.RuleRunner.PendingQuestions.Count == 0)
            {
                return "(no pending questions)";
            }

            return string.Join("\n", simulation.RuleRunner.PendingQuestions.Select(q =>
                $"#{q.QuestionId} [{q.Category}] {q.Title}: {string.Join(", ", q.Choices.Select(c => $"{c.ChoiceNumber}) {c.Text}"))} (default {q.DefaultChoiceNumber})"));
        }

        private static string FormatEntities(Simulation simulation)
        {
            List<EntityId> living = simulation.Entities.LivingEntities().ToList();
            return living.Count == 0
                ? "(no entities)"
                : string.Join("\n", living.Select(id => $"{id} ({simulation.Entities.GetKind(id)})"));
        }
    }
}
