using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Scheduling;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Rules
{
    public class RuleCompilerTests
    {
        private sealed class ModuleCustomTriggerBlockType : BlockType
        {
            public override string Id => "module.test.custom_trigger";
            public override string DisplayName => "Custom Trigger (Test Module)";
            public override string Category => "Test";
            public override BlockKind Kind => BlockKind.Trigger;

            public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
            {
                new PortDefinition("then", PortDirection.Output, PortKind.Execution)
            };

            public override IReadOnlyList<FieldDefinition> Fields { get; } = Array.Empty<FieldDefinition>();
        }

        private sealed class BlockDeclaringModule : IModule, IModuleDeclaration
        {
            public string Id => "module.test_block_declarer";
            public int Version => 1;
            public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();

            public void Declare(ModuleDeclarationContext context)
            {
                context.Blocks.Register(new ModuleCustomTriggerBlockType());
            }

            public void Initialize(ModuleContext context)
            {
            }

            public void Shutdown()
            {
            }
        }

        private static ContentTypeRegistry CreateBuiltInTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }

        private static RuleNode Node(string id, string blockType, Dictionary<string, RuleFieldValueSource> fields = null, double x = 0, double y = 0)
        {
            return new RuleNode { Id = id, BlockTypeId = blockType, X = x, Y = y, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };
        }

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort)
        {
            return new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };
        }

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        private static RuleFieldValueSource SettingRef(string settingId) => RuleFieldValueSource.FromSetting(settingId);

        private static RuleDefinition Rule(string id, List<RuleNode> nodes, List<RuleConnection> connections, bool enabled = true)
        {
            return new RuleDefinition { Id = id, Name = id, Description = string.Empty, Enabled = enabled, Nodes = nodes, Connections = connections };
        }

        private static ContentDatabase DatabaseWithRules(params RuleDefinition[] rules)
        {
            var database = new ContentDatabase();
            foreach (RuleDefinition rule in rules)
            {
                database.Set(BuiltInContentTypes.RuleTypeName, rule, "core", "rules.json");
            }

            return database;
        }

        private static EventDefinitionRegistry CreateEvents(params string[] ids)
        {
            var registry = new EventDefinitionRegistry();
            foreach (string id in ids)
            {
                registry.Register(new EventDefinition(id, EventVisibility.Public, 0f, 0f, "test"));
            }

            return registry;
        }

        private static (RuleRegistry Registry, RuleCompileReport Report) Compile(RuleDefinition rule, SettingsRegistry settings = null, ErrorMode errorMode = ErrorMode.Release, EventDefinitionRegistry events = null)
        {
            ContentDatabase database = DatabaseWithRules(rule);
            BlockRegistry blocks = TestBlocks.CreateRegistry();
            return RuleCompiler.Compile(database, blocks, settings ?? new SettingsRegistry(), events ?? CreateEvents("e"), errorMode);
        }

        [Test]
        public void ValidRule_LoadsFromTempSource_CompilesAndResolvesEveryNodeAndConnection()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("rules.json", @"{
                ""type"": ""rule"",
                ""items"": [
                    {
                        ""id"": ""rule.test_valid"",
                        ""name"": ""Test Valid Rule"",
                        ""description"": ""A trigger that logs."",
                        ""enabled"": true,
                        ""nodes"": [
                            { ""id"": ""n1"", ""blockType"": ""test.trigger.on_event"", ""x"": 0, ""y"": 0, ""fields"": { ""event"": ""test.event.fired"" } },
                            { ""id"": ""n2"", ""blockType"": ""test.action.log"", ""x"": 100, ""y"": 0, ""fields"": { ""message"": ""hi"" } }
                        ],
                        ""connections"": [
                            { ""fromNode"": ""n1"", ""fromPort"": ""then"", ""toNode"": ""n2"", ""toPort"": ""exec"" }
                        ]
                    }
                ]
            }");

            ContentTypeRegistry types = CreateBuiltInTypes();
            var loader = new ContentLoader(types, ErrorMode.Development);
            (ContentDatabase database, ContentLoadReport contentReport) = loader.Load(new[] { source.FolderPath });
            Assert.IsFalse(contentReport.HasErrors);

            BlockRegistry blocks = TestBlocks.CreateRegistry();
            var settings = new SettingsRegistry();
            EventDefinitionRegistry events = CreateEvents("test.event.fired");

            (RuleRegistry registry, RuleCompileReport report) = RuleCompiler.Compile(database, blocks, settings, events, ErrorMode.Development);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.test_valid", out CompiledRule compiled));
            Assert.AreEqual(2, compiled.Nodes.Count);
            Assert.AreEqual("n1", compiled.TriggerNodeId);
            Assert.AreEqual(1, compiled.Connections.Count);

            CompiledRuleConnection connection = compiled.Connections[0];
            Assert.AreEqual("n1", connection.FromNode);
            Assert.AreEqual("n2", connection.ToNode);
            Assert.AreEqual(0, connection.FromPortIndex);
            Assert.AreEqual(0, connection.ToPortIndex);

            CompiledRuleNode actionNode = compiled.Nodes["n2"];
            Assert.AreEqual("hi", actionNode.Fields["message"].LiteralValue);
        }

        [Test]
        public void UnknownBlockType_IsReportedWithRuleAndNodeId()
        {
            var rule = Rule("rule.bad_block", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.block.does_not_exist")
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.NodeId == "n2");
            Assert.AreEqual("rule.bad_block", error.RuleId);
            StringAssert.Contains("Unknown block type", error.Message);
        }

        [Test]
        public void UnknownPortName_IsReportedWithRuleAndNodeId()
        {
            var rule = Rule("rule.bad_port", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.action.log")
            }, new List<RuleConnection>
            {
                Conn("n1", "not_a_port", "n2", "exec")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("Unknown output port"));
            Assert.AreEqual("rule.bad_port", error.RuleId);
            Assert.AreEqual("n1", error.NodeId);
        }

        [Test]
        public void ExecutionConnectedToData_IsReported()
        {
            var rule = Rule("rule.exec_to_data", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.value.add_numbers")
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "a")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("Cannot connect"));
            Assert.AreEqual("n1", error.NodeId);
        }

        [Test]
        public void MismatchedDataTypes_IsReported()
        {
            var rule = Rule("rule.mismatch", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(1.0), ["b"] = Lit(2.0) }),
                Node("n3", "test.flow.branch")
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n3", "exec"),
                Conn("n2", "sum", "n3", "condition")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("Data type mismatch"));
            Assert.AreEqual("rule.mismatch", error.RuleId);
            Assert.AreEqual("n2", error.NodeId);
        }

        [Test]
        public void ExecutionOutputConnectedTwice_IsReported()
        {
            var rule = Rule("rule.exec_fanout", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.action.log"),
                Node("n3", "test.action.log")
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "exec"),
                Conn("n1", "then", "n3", "exec")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("may only connect to one"));
            Assert.AreEqual("rule.exec_fanout", error.RuleId);
            Assert.AreEqual("n1", error.NodeId);
        }

        [Test]
        public void DataInputConnectedTwice_IsReported()
        {
            var rule = Rule("rule.data_fanin", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("src1", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(1.0), ["b"] = Lit(2.0) }),
                Node("src2", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(1.0), ["b"] = Lit(2.0) }),
                Node("dst", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource> { ["b"] = Lit(0.0) })
            }, new List<RuleConnection>
            {
                Conn("src1", "sum", "dst", "a"),
                Conn("src2", "sum", "dst", "a")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("already has an incoming wire"));
            Assert.AreEqual("dst", error.NodeId);
        }

        [Test]
        public void RequiredDataInputMissing_IsReported()
        {
            var rule = Rule("rule.missing_required", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.flow.branch")
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "exec")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single();
            Assert.AreEqual("n2", error.NodeId);
            StringAssert.Contains("Required input 'condition'", error.Message);
        }

        [Test]
        public void SettingReference_MissingSetting_IsReported()
        {
            var rule = Rule("rule.bad_setting", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource>
                {
                    ["a"] = SettingRef("setting.does_not_exist"),
                    ["b"] = Lit(1.0)
                })
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("does not exist"));
            Assert.AreEqual("n2", error.NodeId);
        }

        [Test]
        public void SettingReference_WrongType_IsReported()
        {
            var settings = new SettingsRegistry();
            settings.Declare(new SettingDefinition { Id = "setting.a_flag", ValueType = SettingValueType.Boolean, Default = 1, Minimum = 0, Maximum = 1, Category = "Test", Description = "x" });

            var rule = Rule("rule.wrong_type_setting", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource>
                {
                    ["a"] = SettingRef("setting.a_flag"),
                    ["b"] = Lit(1.0)
                })
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule, settings);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("not a number or true-or-false"));
            Assert.AreEqual("n2", error.NodeId);
        }

        [Test]
        public void SettingReference_CorrectType_Compiles()
        {
            var settings = new SettingsRegistry();
            settings.Declare(new SettingDefinition { Id = "setting.a_rate", ValueType = SettingValueType.Number, Default = 2.5, Minimum = 0, Maximum = 10, Category = "Test", Description = "x" });

            var rule = Rule("rule.setting_ok", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource>
                {
                    ["a"] = SettingRef("setting.a_rate"),
                    ["b"] = Lit(1.0)
                })
            }, new List<RuleConnection>());

            (RuleRegistry registry, RuleCompileReport report) = Compile(rule, settings);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.setting_ok", out CompiledRule compiled));
            CompiledFieldValue fieldValue = compiled.Nodes["n2"].Fields["a"];
            Assert.IsTrue(fieldValue.IsSettingReference);
            Assert.AreEqual("setting.a_rate", fieldValue.SettingId);
        }

        [Test]
        public void NoTrigger_IsReported()
        {
            var rule = Rule("rule.no_trigger", new List<RuleNode>
            {
                Node("n1", "test.action.log")
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single();
            Assert.AreEqual("rule.no_trigger", error.RuleId);
            Assert.IsNull(error.NodeId);
            StringAssert.Contains("no trigger", error.Message);
        }

        [Test]
        public void MultipleTriggers_IsReported()
        {
            var rule = Rule("rule.two_triggers", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e1") }),
                Node("n2", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e2") })
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule, events: CreateEvents("e1", "e2"));

            RuleCompileProblem error = report.Errors.Single();
            Assert.AreEqual("rule.two_triggers", error.RuleId);
            Assert.IsNull(error.NodeId);
            StringAssert.Contains("more than one trigger", error.Message);
        }

        [Test]
        public void DuplicateNodeId_IsReported()
        {
            var rule = Rule("rule.dup_id", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n1", "test.action.log")
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("used more than once"));
            Assert.AreEqual("rule.dup_id", error.RuleId);
            Assert.AreEqual("n1", error.NodeId);
        }

        [Test]
        public void TriggerListeningForUnregisteredEvent_IsReported()
        {
            var rule = Rule("rule.unregistered_trigger_event", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.does_not_exist") })
            }, new List<RuleConnection>());

            (_, RuleCompileReport report) = Compile(rule, events: new EventDefinitionRegistry());

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("Listens for event"));
            Assert.AreEqual("rule.unregistered_trigger_event", error.RuleId);
            Assert.AreEqual("n1", error.NodeId);
            StringAssert.Contains("event.does_not_exist", error.Message);
        }

        [Test]
        public void ActionPostingUnregisteredEvent_IsReported()
        {
            var rule = Rule("rule.unregistered_action_event", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.does_not_exist") })
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "exec")
            });

            (_, RuleCompileReport report) = Compile(rule);

            RuleCompileProblem error = report.Errors.Single(e => e.Message.Contains("Posts event"));
            Assert.AreEqual("rule.unregistered_action_event", error.RuleId);
            Assert.AreEqual("n2", error.NodeId);
            StringAssert.Contains("event.does_not_exist", error.Message);
        }

        [Test]
        public void UnreachableNode_WarningReported_DoesNotBlockCompiling()
        {
            var rule = Rule("rule.unreachable", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.action.log"),
                Node("n3", "test.action.log")
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "exec")
            });

            (RuleRegistry registry, RuleCompileReport report) = Compile(rule);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.unreachable", out _));
            RuleCompileProblem warning = report.Warnings.Single(w => w.NodeId == "n3");
            StringAssert.Contains("not reachable", warning.Message);
        }

        [Test]
        public void PureValueNodeReachableThroughDataWire_NoWarning()
        {
            var rule = Rule("rule.value_reachable", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") }),
                Node("n2", "test.action.record_number"),
                Node("n3", "test.value.add_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(1.0), ["b"] = Lit(2.0) })
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "exec"),
                Conn("n3", "sum", "n2", "value")
            });

            (RuleRegistry registry, RuleCompileReport report) = Compile(rule);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.value_reachable", out _));
            Assert.IsFalse(report.Warnings.Any(w => w.NodeId == "n3"));
        }

        [Test]
        public void ActionPostingSameEventAsTrigger_WarningReported_DoesNotBlockCompiling()
        {
            var rule = Rule("rule.possible_loop", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("test.event.loop") }),
                Node("n2", "test.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("test.event.loop") })
            }, new List<RuleConnection>
            {
                Conn("n1", "then", "n2", "exec")
            });

            (RuleRegistry registry, RuleCompileReport report) = Compile(rule, events: CreateEvents("test.event.loop"));

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.possible_loop", out _));
            RuleCompileProblem warning = report.Warnings.Single(w => w.NodeId == "n2");
            StringAssert.Contains("possible loop", warning.Message);
        }

        [Test]
        public void ModOverridesCoreRuleById()
        {
            using var core = new TempContentSource("core");
            core.WriteDataFile("rules.json", @"{
                ""type"": ""rule"",
                ""items"": [
                    {
                        ""id"": ""rule.overridable"",
                        ""name"": ""Core Version"",
                        ""enabled"": true,
                        ""nodes"": [
                            { ""id"": ""n1"", ""blockType"": ""test.trigger.on_event"", ""fields"": { ""event"": ""test.event.a"" } },
                            { ""id"": ""n2"", ""blockType"": ""test.action.log"", ""fields"": { ""message"": ""core"" } }
                        ],
                        ""connections"": [
                            { ""fromNode"": ""n1"", ""fromPort"": ""then"", ""toNode"": ""n2"", ""toPort"": ""exec"" }
                        ]
                    }
                ]
            }");

            using var mod = new TempContentSource("modA");
            mod.WriteDataFile("rules.json", @"{
                ""type"": ""rule"",
                ""items"": [
                    {
                        ""id"": ""rule.overridable"",
                        ""name"": ""Mod Version"",
                        ""enabled"": true,
                        ""nodes"": [
                            { ""id"": ""n1"", ""blockType"": ""test.trigger.on_event"", ""fields"": { ""event"": ""test.event.a"" } },
                            { ""id"": ""n2"", ""blockType"": ""test.action.log"", ""fields"": { ""message"": ""mod"" } }
                        ],
                        ""connections"": [
                            { ""fromNode"": ""n1"", ""fromPort"": ""then"", ""toNode"": ""n2"", ""toPort"": ""exec"" }
                        ]
                    }
                ]
            }");

            ContentTypeRegistry types = CreateBuiltInTypes();
            var loader = new ContentLoader(types, ErrorMode.Development);
            (ContentDatabase database, ContentLoadReport contentReport) = loader.Load(new[] { core.FolderPath, mod.FolderPath });

            Assert.IsFalse(contentReport.HasErrors);
            Assert.IsTrue(contentReport.Overrides.Any(o => o.Id == "rule.overridable" && o.NewSourceId == "modA"));

            BlockRegistry blocks = TestBlocks.CreateRegistry();
            var settings = new SettingsRegistry();
            EventDefinitionRegistry events = CreateEvents("test.event.a");
            (RuleRegistry registry, RuleCompileReport report) = RuleCompiler.Compile(database, blocks, settings, events, ErrorMode.Development);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.overridable", out CompiledRule compiled));
            Assert.AreEqual("mod", compiled.Nodes["n2"].Fields["message"].LiteralValue);
        }

        [Test]
        public void ModuleRegistersBlockTypeDuringDeclaration_RuleUsingItCompiles()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("rules.json", @"{
                ""type"": ""rule"",
                ""items"": [
                    {
                        ""id"": ""rule.module_block"",
                        ""enabled"": true,
                        ""nodes"": [
                            { ""id"": ""n1"", ""blockType"": ""module.test.custom_trigger"" }
                        ],
                        ""connections"": []
                    }
                ]
            }");

            var definitions = new EventDefinitionRegistry();
            var events = new EventBus(definitions);
            var scheduler = new Scheduler();
            var entities = new EntityRegistry();
            var random = new RandomService(1);
            long tick = 0;
            var host = new ModuleHost(events, scheduler, entities, definitions, random, () => tick);

            var module = new BlockDeclaringModule();
            host.ValidateAndOrder(new[] { new ModuleRegistration(module, new ModuleManifest(module.Id, 1, Array.Empty<string>(), true)) });

            ContentTypeRegistry contentTypes = CreateBuiltInTypes();
            var settings = new SettingsRegistry();
            var blocks = new BlockRegistry();
            host.RunDeclarationPhase(contentTypes, settings, blocks);

            var loader = new ContentLoader(contentTypes, ErrorMode.Development);
            (ContentDatabase database, ContentLoadReport contentReport) = loader.Load(new[] { source.FolderPath });
            Assert.IsFalse(contentReport.HasErrors);

            (RuleRegistry registry, RuleCompileReport report) = RuleCompiler.Compile(database, blocks, settings, definitions, ErrorMode.Development);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(registry.TryGet("rule.module_block", out _));

            host.InitializeModules();
        }

        [Test]
        public void DevelopmentMode_RuleError_Throws()
        {
            var badRule = Rule("rule.bad", new List<RuleNode> { Node("n1", "test.action.log") }, new List<RuleConnection>());
            ContentDatabase database = DatabaseWithRules(badRule);
            BlockRegistry blocks = TestBlocks.CreateRegistry();
            var settings = new SettingsRegistry();
            EventDefinitionRegistry events = CreateEvents("e");

            RuleCompileException ex = Assert.Throws<RuleCompileException>(() => RuleCompiler.Compile(database, blocks, settings, events, ErrorMode.Development));
            Assert.IsTrue(ex.Report.Errors.Count > 0);
        }

        [Test]
        public void ReleaseMode_SkipsBadRule_AndKeepsReport()
        {
            var badRule = Rule("rule.bad", new List<RuleNode> { Node("n1", "test.action.log") }, new List<RuleConnection>());
            var goodRule = Rule("rule.good", new List<RuleNode>
            {
                Node("n1", "test.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("e") })
            }, new List<RuleConnection>());

            ContentDatabase database = DatabaseWithRules(badRule, goodRule);
            BlockRegistry blocks = TestBlocks.CreateRegistry();
            var settings = new SettingsRegistry();
            EventDefinitionRegistry events = CreateEvents("e");

            (RuleRegistry registry, RuleCompileReport report) = RuleCompiler.Compile(database, blocks, settings, events, ErrorMode.Release);

            Assert.IsTrue(report.HasErrors);
            Assert.IsFalse(registry.TryGet("rule.bad", out _));
            Assert.IsTrue(registry.TryGet("rule.good", out _));
        }
    }
}
