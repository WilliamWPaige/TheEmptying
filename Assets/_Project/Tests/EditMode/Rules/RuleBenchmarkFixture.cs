using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TheEmptying.Core;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Editing;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>
    /// Prompt 2.8: shared content for the performance benchmark and the
    /// save/load-under-load test, so both exercise the exact same 500-rule,
    /// 2,000-entity setup through the real content pipeline (SimulationBuilder,
    /// a real content source on disk) rather than two different hand-rolled
    /// ones. 300 rules trigger on one of 300 distinct events (one rule each);
    /// every tick, a sliding window of 50 of those 300 events fires, so the
    /// benchmark generates about 50 events per tick as specified. 200 rules
    /// trigger on a schedule (split across every N hours, at an hour of day,
    /// and on new day). Every core block kind appears somewhere across 10
    /// rotating rule templates, including Wait, flags and counters.
    /// </summary>
    internal static class RuleBenchmarkFixture
    {
        public const int EntityCount = 2000;
        public const int OnEventRuleCount = 300;
        public const int ScheduleRuleCount = 200;
        public const int RuleCount = OnEventRuleCount + ScheduleRuleCount;
        public const int EventsPerTick = 50;
        public const string BenchOutEventId = "bench.out";

        private static string EventId(int i) => $"bench.event.{i}";

        private static RuleNode Node(string id, string blockType, Dictionary<string, RuleFieldValueSource> fields = null) =>
            new RuleNode { Id = id, BlockTypeId = blockType, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort) =>
            new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        /// <summary>Writes the manifest (no modules needed), 301 event definitions, the two rules settings, and all 500 rules into source's Data folder - exactly what SimulationBuilder.Build needs to load and compile them the same way the game does.</summary>
        public static void WriteContent(TempContentSource source)
        {
            source.WriteDataFile("manifest.json", "{\"type\":\"module_manifest\",\"items\":[]}");

            var eventItems = new JArray
            {
                new JObject { ["id"] = BenchOutEventId, ["visibility"] = "Public", ["reportingScore"] = 0, ["notabilityScore"] = 0, ["description"] = "bench" }
            };
            for (int i = 0; i < OnEventRuleCount; i++)
            {
                eventItems.Add(new JObject { ["id"] = EventId(i), ["visibility"] = "Public", ["reportingScore"] = 0, ["notabilityScore"] = 0, ["description"] = "bench" });
            }

            var eventsFile = new JObject { ["type"] = "event_definition", ["items"] = eventItems };
            source.WriteDataFile("events.json", eventsFile.ToString());

            var settingsFile = new JObject
            {
                ["type"] = "setting_definition",
                ["items"] = new JArray
                {
                    new JObject { ["id"] = RuleRunner.MaxFiresPerTickSettingId, ["valueType"] = "Integer", ["default"] = 1000, ["minimum"] = 1, ["maximum"] = 1000000, ["category"] = "Rules", ["description"] = "x" },
                    new JObject { ["id"] = RuleRunner.QuestionsPauseGameSettingId, ["valueType"] = "Boolean", ["default"] = 1, ["minimum"] = 0, ["maximum"] = 1, ["category"] = "Rules", ["description"] = "x" }
                }
            };
            source.WriteDataFile("settings.json", settingsFile.ToString());

            source.WriteDataFile("rules.json", RuleJsonSerializer.SerializeFile(BuildRules()));
        }

        public static List<RuleDefinition> BuildRules()
        {
            var rules = new List<RuleDefinition>(RuleCount);

            for (int i = 0; i < OnEventRuleCount; i++)
            {
                var trigger = Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit(EventId(i)) });
                rules.Add(BuildRule($"rule.bench_event_{i}", trigger, i % 10, hasEventData: true));
            }

            for (int i = 0; i < ScheduleRuleCount; i++)
            {
                RuleNode trigger;
                if (i % 3 == 0)
                {
                    trigger = Node("trigger", "block.trigger.every_n_hours", new Dictionary<string, RuleFieldValueSource> { ["hours"] = Lit(1.0 + (i % 6)) });
                }
                else if (i % 3 == 1)
                {
                    trigger = Node("trigger", "block.trigger.at_hour_of_day", new Dictionary<string, RuleFieldValueSource> { ["hour"] = Lit((double)(i % 24)) });
                }
                else
                {
                    trigger = Node("trigger", "block.trigger.on_new_day");
                }

                int template = i % 10;
                if (template == 7)
                {
                    template = 0; // ReadEventParameter needs a triggering event's data; schedule rules have none.
                }

                rules.Add(BuildRule($"rule.bench_schedule_{i}", trigger, template, hasEventData: false));
            }

            return rules;
        }

        private static RuleDefinition BuildRule(string ruleId, RuleNode trigger, int template, bool hasEventData)
        {
            var nodes = new List<RuleNode> { trigger };
            var connections = new List<RuleConnection>();
            BuildTemplate(template, ruleId, nodes, connections);

            return new RuleDefinition { Id = ruleId, Name = ruleId, Description = string.Empty, Enabled = true, Nodes = nodes, Connections = connections };
        }

        /// <summary>One of 10 rotating shapes, together covering every core block kind (triggers aside) including Wait, flags and counters. Every template starts its chain from the trigger's "then" output into node "a".</summary>
        private static void BuildTemplate(int template, string ruleId, List<RuleNode> nodes, List<RuleConnection> connections)
        {
            switch (template)
            {
                case 0: // counters + post event
                    nodes.Add(Node("a", "block.action.set_counter", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(1.0) }));
                    nodes.Add(Node("b", "block.action.change_counter", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["amount"] = Lit(1.0) }));
                    nodes.Add(Node("c", "block.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit(BenchOutEventId) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("a", "then", "b", "exec"));
                    connections.Add(Conn("b", "then", "c", "exec"));
                    break;

                case 1: // branch + flags
                    nodes.Add(Node("a", "block.flow.branch", new Dictionary<string, RuleFieldValueSource> { ["condition"] = Lit(true) }));
                    nodes.Add(Node("b", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(true) }));
                    nodes.Add(Node("c", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(false) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("a", "true", "b", "exec"));
                    connections.Add(Conn("a", "false", "c", "exec"));
                    break;

                case 2: // local variables + log line
                    nodes.Add(Node("a", "block.action.set_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x"), ["value"] = Lit(5.0) }));
                    nodes.Add(Node("b", "block.value.get_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x") }));
                    nodes.Add(Node("c", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("a", "then", "c", "exec"));
                    connections.Add(Conn("b", "result", "c", "arg0"));
                    break;

                case 3: // random chance + branch
                    nodes.Add(Node("v", "block.value.random_chance", new Dictionary<string, RuleFieldValueSource> { ["probability"] = Lit(0.5) }));
                    nodes.Add(Node("a", "block.flow.branch"));
                    nodes.Add(Node("b", "block.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit(BenchOutEventId) }));
                    nodes.Add(Node("c", "block.flow.stop"));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("v", "result", "a", "condition"));
                    connections.Add(Conn("a", "true", "b", "exec"));
                    connections.Add(Conn("a", "false", "c", "exec"));
                    break;

                case 4: // wait + herald line
                    nodes.Add(Node("a", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(6.0), ["unit"] = Lit("minutes") }));
                    nodes.Add(Node("b", "block.action.write_herald_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("bench") }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("a", "then", "b", "exec"));
                    break;

                case 5: // sequence + counters + flag
                    nodes.Add(Node("a", "block.flow.sequence"));
                    nodes.Add(Node("b", "block.action.set_counter", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(2.0) }));
                    nodes.Add(Node("c", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(true) }));
                    nodes.Add(Node("d", "block.action.change_counter", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["amount"] = Lit(-1.0) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("a", "then1", "b", "exec"));
                    connections.Add(Conn("a", "then2", "c", "exec"));
                    connections.Add(Conn("a", "then3", "d", "exec"));
                    break;

                case 6: // compare text + branch + flags
                    nodes.Add(Node("v", "block.value.compare_text", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(ruleId), ["b"] = Lit(ruleId) }));
                    nodes.Add(Node("a", "block.flow.branch"));
                    nodes.Add(Node("b", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(true) }));
                    nodes.Add(Node("c", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(false) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("v", "result", "a", "condition"));
                    connections.Add(Conn("a", "true", "b", "exec"));
                    connections.Add(Conn("a", "false", "c", "exec"));
                    break;

                case 7: // read event parameter + change counter (on-event rules only)
                    nodes.Add(Node("v", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("value") }));
                    nodes.Add(Node("a", "block.action.change_counter", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("v", "result", "a", "amount"));
                    break;

                case 8: // get counter + compare numbers + branch + flag
                    nodes.Add(Node("g", "block.value.get_counter", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId) }));
                    nodes.Add(Node("v", "block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["b"] = Lit(0.0) }));
                    nodes.Add(Node("a", "block.flow.branch"));
                    nodes.Add(Node("b", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(true) }));
                    nodes.Add(Node("c", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(false) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("g", "result", "v", "a"));
                    connections.Add(Conn("v", "result", "a", "condition"));
                    connections.Add(Conn("a", "true", "b", "exec"));
                    connections.Add(Conn("a", "false", "c", "exec"));
                    break;

                default: // 9: ask the player (times out with a default choice if never answered) + flags
                    nodes.Add(Node("a", "block.flow.ask_player", new Dictionary<string, RuleFieldValueSource>
                    {
                        ["title"] = Lit(ruleId),
                        ["choice1"] = Lit("Yes"),
                        ["choice2"] = Lit("No"),
                        ["defaultChoice"] = Lit(1.0),
                        ["timeoutHours"] = Lit(0.5)
                    }));
                    nodes.Add(Node("b", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(true) }));
                    nodes.Add(Node("c", "block.action.set_flag", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit(ruleId), ["value"] = Lit(false) }));
                    connections.Add(Conn("trigger", "then", "a", "exec"));
                    connections.Add(Conn("a", "choice1", "b", "exec"));
                    connections.Add(Conn("a", "choice2", "c", "exec"));
                    break;
            }
        }

        /// <summary>Posts a sliding window of 50 of the 300 bench events, advancing by 50 each tick (so the full 300 cycle every 6 ticks) - about 50 events per tick, as specified. Each posted event carries a "value" parameter for template 7 (Read Event Parameter) to read.</summary>
        public static void PostTickEvents(Simulation simulation, long tick)
        {
            int start = (int)((tick * EventsPerTick) % OnEventRuleCount);
            for (int i = 0; i < EventsPerTick; i++)
            {
                int eventIndex = (start + i) % OnEventRuleCount;
                var data = new TheEmptying.Core.Events.EventData().SetDouble("value", eventIndex);
                simulation.Events.Post(EventId(eventIndex), tick, data);
            }
        }
    }
}
