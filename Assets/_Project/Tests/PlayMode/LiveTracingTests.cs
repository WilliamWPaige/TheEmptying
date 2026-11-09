using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>Prompt 2.7: with the game running, tracing switched on for a rule records its segments and switching it off stops recording.</summary>
    public class LiveTracingTests
    {
        private sealed class RecordingTraceListener : IRuleTraceListener
        {
            public List<RuleTraceSegment> Segments { get; } = new List<RuleTraceSegment>();
            public void OnSegmentCompleted(RuleTraceSegment segment) => Segments.Add(segment);
        }

        private const string RuleId = "rule.live_tracing_playmode_test";
        private string _modFolder;

        [SetUp]
        public void WriteTestModRule()
        {
            _modFolder = Path.Combine(Application.persistentDataPath, "Mods", "test_live_tracing_mod");
            string dataFolder = Path.Combine(_modFolder, "Data");
            Directory.CreateDirectory(dataFolder);

            string ruleJson = $@"{{
  ""type"": ""rule"",
  ""items"": [
    {{
      ""id"": ""{RuleId}"",
      ""name"": ""Live tracing PlayMode test"",
      ""description"": """",
      ""enabled"": true,
      ""nodes"": [
        {{ ""id"": ""trigger"", ""blockType"": ""block.trigger.on_new_day"", ""x"": 0, ""y"": 0, ""fields"": {{}} }},
        {{ ""id"": ""log"", ""blockType"": ""block.action.write_log_line"", ""x"": 200, ""y"": 0, ""fields"": {{ ""text"": ""live traced"" }} }}
      ],
      ""connections"": [
        {{ ""fromNode"": ""trigger"", ""fromPort"": ""then"", ""toNode"": ""log"", ""toPort"": ""exec"" }}
      ]
    }}
  ]
}}";
            File.WriteAllText(Path.Combine(dataFolder, "live_tracing_test_rule.json"), ruleJson);
        }

        [TearDown]
        public void RemoveTestModRule()
        {
            if (_modFolder != null && Directory.Exists(_modFolder))
            {
                Directory.Delete(_modFolder, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator TracingSwitchedOn_RecordsSegments_AndSwitchingOffStopsRecording()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let startup settle

            Simulation simulation = bootstrap.SimulationForEditorTools;
            Assert.IsNotNull(simulation, "The bootstrap must expose its current simulation for editor tools.");

            RuleRunner runner = simulation.RuleRunner;
            Assert.IsNotNull(runner);
            Assert.IsTrue(runner.IsRuleActive(RuleId), "The test mod's rule should have loaded and activated.");

            var listener = new RecordingTraceListener();
            Assert.IsFalse(runner.IsTracingEnabled(RuleId));

            runner.SetTraceListener(RuleId, listener);
            Assert.IsTrue(runner.IsTracingEnabled(RuleId));

            runner.FireNow(RuleId);
            yield return null;

            Assert.AreEqual(1, listener.Segments.Count);
            Assert.AreEqual(RuleTraceEndKind.Ended, listener.Segments[0].EndKind);

            runner.ClearTraceListener(RuleId);
            Assert.IsFalse(runner.IsTracingEnabled(RuleId));

            runner.FireNow(RuleId);
            yield return null;

            Assert.AreEqual(1, listener.Segments.Count, "no new segment should be recorded after tracing was switched off.");
        }
    }
}
