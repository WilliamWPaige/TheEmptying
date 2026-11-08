using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Editor.Rules.Canvas;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.5: the rule canvas view-model (positions, zoom, pan, selection, hit tests, coordinate conversion).</summary>
    public class RuleCanvasViewModelTests
    {
        private static RuleNode Node(string id, string blockType, double x = 0, double y = 0, Dictionary<string, RuleFieldValueSource> fields = null)
        {
            return new RuleNode { Id = id, BlockTypeId = blockType, X = x, Y = y, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };
        }

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort)
        {
            return new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };
        }

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        private static RuleDefinition Rule(string id, List<RuleNode> nodes, List<RuleConnection> connections = null)
        {
            return new RuleDefinition { Id = id, Name = id, Description = string.Empty, Enabled = true, Nodes = nodes, Connections = connections ?? new List<RuleConnection>() };
        }

        private static BlockRegistry Blocks()
        {
            var registry = new BlockRegistry();
            CoreBlocks.RegisterAll(registry);
            return registry;
        }

        // --- Zoom ---

        [Test]
        public void ZoomAt_ClampsToMinAndMax()
        {
            var vm = new RuleCanvasViewModel(Rule("r", new List<RuleNode>()), Blocks());

            vm.ZoomAt(Vector2.zero, 10f);
            Assert.AreEqual(RuleCanvasViewModel.MaxZoom, vm.Zoom, 0.0001f);

            vm.ZoomAt(Vector2.zero, 0.01f);
            Assert.AreEqual(RuleCanvasViewModel.MinZoom, vm.Zoom, 0.0001f);
        }

        [Test]
        public void ZoomAt_KeepsTheCanvasPointUnderTheCursorFixed()
        {
            var vm = new RuleCanvasViewModel(Rule("r", new List<RuleNode>()), Blocks());
            var cursor = new Vector2(300f, 200f);

            vm.PanBy(new Vector2(50f, -20f));
            Vector2 canvasPointBefore = vm.ScreenToCanvas(cursor);

            vm.ZoomAt(cursor, 1.75f);

            Vector2 screenPointAfter = vm.CanvasToScreen(canvasPointBefore);
            Assert.That(screenPointAfter.x, Is.EqualTo(cursor.x).Within(0.01f));
            Assert.That(screenPointAfter.y, Is.EqualTo(cursor.y).Within(0.01f));
        }

        // --- Coordinate round trip ---

        [TestCase(1f)]
        [TestCase(2f)]
        [TestCase(0.5f)]
        [TestCase(1.75f)]
        [TestCase(0.25f)]
        public void ScreenToCanvas_RoundTrips_AtVariousZoomAndPan(float zoom)
        {
            var vm = new RuleCanvasViewModel(Rule("r", new List<RuleNode>()), Blocks());
            vm.ZoomAt(new Vector2(150f, 150f), zoom);
            vm.PanBy(new Vector2(73f, -41f));

            foreach (Vector2 screenPoint in new[] { Vector2.zero, new Vector2(400f, 300f), new Vector2(-50f, 900f) })
            {
                Vector2 canvasPoint = vm.ScreenToCanvas(screenPoint);
                Vector2 roundTripped = vm.CanvasToScreen(canvasPoint);
                Assert.That(roundTripped.x, Is.EqualTo(screenPoint.x).Within(0.01f));
                Assert.That(roundTripped.y, Is.EqualTo(screenPoint.y).Within(0.01f));
            }
        }

        // --- Framing ---

        [Test]
        public void FrameAll_FitsEveryNodeInsideTheViewport()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("a", "block.flow.stop", 0, 0),
                Node("b", "block.flow.stop", 500, 300)
            });
            var vm = new RuleCanvasViewModel(rule, Blocks());
            var viewport = new Vector2(800f, 600f);

            vm.FrameAll(viewport);

            foreach (NodeViewModel node in vm.Nodes)
            {
                Vector2 screenMin = vm.CanvasToScreen(node.Bounds.min);
                Vector2 screenMax = vm.CanvasToScreen(node.Bounds.max);
                Assert.GreaterOrEqual(screenMin.x, -0.5f);
                Assert.GreaterOrEqual(screenMin.y, -0.5f);
                Assert.LessOrEqual(screenMax.x, viewport.x + 0.5f);
                Assert.LessOrEqual(screenMax.y, viewport.y + 0.5f);
            }
        }

        [Test]
        public void FrameSelected_OnlyFitsSelectedNodes_AndIsNoOpWithNoSelection()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("a", "block.flow.stop", 0, 0),
                Node("b", "block.flow.stop", 5000, 5000)
            });
            var vm = new RuleCanvasViewModel(rule, Blocks());
            var viewport = new Vector2(800f, 600f);

            float zoomBefore = vm.Zoom;
            Vector2 panBefore = vm.Pan;
            vm.FrameSelected(viewport);
            Assert.AreEqual(zoomBefore, vm.Zoom, "FrameSelected with nothing selected should be a no-op.");
            Assert.AreEqual(panBefore, vm.Pan);

            NodeViewModel a = vm.Nodes.First(n => n.Node.Id == "a");
            vm.SelectAt(a.Bounds.center, additive: false);
            vm.FrameSelected(viewport);

            Vector2 screenMin = vm.CanvasToScreen(a.Bounds.min);
            Vector2 screenMax = vm.CanvasToScreen(a.Bounds.max);
            Assert.GreaterOrEqual(screenMin.x, -0.5f);
            Assert.LessOrEqual(screenMax.x, viewport.x + 0.5f);

            // A single small node framed in an 800x600 viewport should hit the zoom ceiling.
            Assert.AreEqual(RuleCanvasViewModel.MaxZoom, vm.Zoom, 0.0001f);
        }

        // --- Selection ---

        [Test]
        public void SelectAt_PicksTheTopmostNodeUnderThePoint()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("a", "block.flow.stop", 0, 0),
                Node("b", "block.flow.stop", 300, 0)
            });
            var vm = new RuleCanvasViewModel(rule, Blocks());

            NodeViewModel b = vm.Nodes.First(n => n.Node.Id == "b");
            vm.SelectAt(b.Bounds.center, additive: false);

            CollectionAssert.AreEquivalent(new[] { "b" }, vm.SelectedNodeIds);
        }

        [Test]
        public void SelectAt_Additive_TogglesMembership()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("a", "block.flow.stop", 0, 0),
                Node("b", "block.flow.stop", 300, 0)
            });
            var vm = new RuleCanvasViewModel(rule, Blocks());
            NodeViewModel a = vm.Nodes.First(n => n.Node.Id == "a");
            NodeViewModel b = vm.Nodes.First(n => n.Node.Id == "b");

            vm.SelectAt(a.Bounds.center, additive: false);
            vm.SelectAt(b.Bounds.center, additive: true);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, vm.SelectedNodeIds);

            vm.SelectAt(a.Bounds.center, additive: true);
            CollectionAssert.AreEquivalent(new[] { "b" }, vm.SelectedNodeIds, "additive click on an already-selected node should remove it.");
        }

        [Test]
        public void SelectAt_EmptySpace_ClearsSelection_UnlessAdditive()
        {
            var rule = Rule("r", new List<RuleNode> { Node("a", "block.flow.stop", 0, 0) });
            var vm = new RuleCanvasViewModel(rule, Blocks());
            NodeViewModel a = vm.Nodes.First(n => n.Node.Id == "a");

            vm.SelectAt(a.Bounds.center, additive: false);
            vm.SelectAt(new Vector2(99999f, 99999f), additive: true);
            CollectionAssert.AreEquivalent(new[] { "a" }, vm.SelectedNodeIds, "additive click on empty space should leave the selection alone.");

            vm.SelectAt(new Vector2(99999f, 99999f), additive: false);
            CollectionAssert.IsEmpty(vm.SelectedNodeIds, "a plain click on empty space should clear the selection.");
        }

        [Test]
        public void SelectInBox_PicksEveryOverlappingNode()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("a", "block.flow.stop", 0, 0),
                Node("b", "block.flow.stop", 300, 0),
                Node("c", "block.flow.stop", 2000, 2000)
            });
            var vm = new RuleCanvasViewModel(rule, Blocks());

            vm.SelectInBox(new Rect(-10, -10, 450, 100), additive: false);

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, vm.SelectedNodeIds);
        }

        [Test]
        public void ClearSelection_EmptiesIt()
        {
            var rule = Rule("r", new List<RuleNode> { Node("a", "block.flow.stop", 0, 0) });
            var vm = new RuleCanvasViewModel(rule, Blocks());
            vm.SelectAt(vm.Nodes[0].Bounds.center, additive: false);

            vm.ClearSelection();

            CollectionAssert.IsEmpty(vm.SelectedNodeIds);
        }

        // --- Dragging ---

        [Test]
        public void DragSelected_MovesAllSelectedNodes_AndUpdatesXAndY_LeavingOthersAlone()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("a", "block.flow.stop", 0, 0),
                Node("b", "block.flow.stop", 300, 0),
                Node("c", "block.flow.stop", 1000, 1000)
            });
            var vm = new RuleCanvasViewModel(rule, Blocks());

            vm.SelectAt(vm.Nodes.First(n => n.Node.Id == "a").Bounds.center, additive: false);
            vm.SelectAt(vm.Nodes.First(n => n.Node.Id == "b").Bounds.center, additive: true);

            vm.DragSelected(new Vector2(50f, 20f));

            RuleNode a = rule.Nodes.First(n => n.Id == "a");
            RuleNode b = rule.Nodes.First(n => n.Id == "b");
            RuleNode c = rule.Nodes.First(n => n.Id == "c");

            Assert.AreEqual(50.0, a.X, 0.001);
            Assert.AreEqual(20.0, a.Y, 0.001);
            Assert.AreEqual(350.0, b.X, 0.001);
            Assert.AreEqual(20.0, b.Y, 0.001);
            Assert.AreEqual(1000.0, c.X, 0.001, "an unselected node must not move.");
            Assert.AreEqual(1000.0, c.Y, 0.001);
        }

        // --- Port and wire hit tests ---

        [Test]
        public void HitTestPort_FindsThePortClosestToThePoint_WithinRadius()
        {
            var rule = Rule("r", new List<RuleNode> { Node("log", "block.action.write_log_line", 0, 0) });
            var vm = new RuleCanvasViewModel(rule, Blocks());
            NodeViewModel log = vm.Nodes[0];
            PortViewModel execPort = log.InputPorts.First(p => p.Name == "exec");
            Vector2 execCanvasPoint = log.Position + execPort.LocalOffset;

            var hit = vm.HitTestPort(execCanvasPoint, 8f);
            Assert.IsNotNull(hit);
            Assert.AreEqual("exec", hit.Value.Port.Name);

            var miss = vm.HitTestPort(execCanvasPoint + new Vector2(500, 500), 8f);
            Assert.IsNull(miss);
        }

        [Test]
        public void HitTestWire_FindsAWireNearItsStraightLine()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", 0, 0, new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("log", "block.action.write_log_line", 300, 0)
            }, new List<RuleConnection> { Conn("trigger", "then", "log", "exec") });
            var vm = new RuleCanvasViewModel(rule, Blocks());

            WireViewModel wire = vm.Wires[0];
            Vector2 midpoint = Vector2.Lerp(wire.FromAnchor, wire.ToAnchor, 0.5f);

            Assert.AreSame(wire, vm.HitTestWire(midpoint, 5f));
            Assert.IsNull(vm.HitTestWire(midpoint + new Vector2(0, 500), 5f));
        }

        // --- Missing blocks ---

        [Test]
        public void MissingBlockNode_KeepsItsData_AndInfersPortsFromConnections_SoWiresStillDraw()
        {
            var fields = new Dictionary<string, RuleFieldValueSource> { ["foo"] = Lit("bar") };
            var rule = Rule("r", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", 0, 0, new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("missing", "block.unknown.whatever", 200, 0, fields),
                Node("log", "block.action.write_log_line", 400, 0, new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("x") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "missing", "exec_in"),
                Conn("missing", "exec_out", "log", "exec")
            });

            var vm = new RuleCanvasViewModel(rule, Blocks());

            NodeViewModel missing = vm.Nodes.First(n => n.Node.Id == "missing");
            Assert.IsTrue(missing.IsMissing);
            Assert.AreSame(rule.Nodes.First(n => n.Id == "missing"), missing.Node, "the underlying rule node must be kept as-is.");
            Assert.AreEqual("bar", missing.Node.Fields["foo"].Literal.Value<string>(), "its field data must survive.");

            CollectionAssert.Contains(missing.InputPorts.Select(p => p.Name), "exec_in");
            CollectionAssert.Contains(missing.OutputPorts.Select(p => p.Name), "exec_out");
            Assert.IsTrue(missing.Ports.All(p => p.IsInferred));

            PortViewModel execIn = missing.InputPorts.First(p => p.Name == "exec_in");
            PortViewModel execOut = missing.OutputPorts.First(p => p.Name == "exec_out");
            Assert.AreEqual(PortKind.Execution, execIn.Kind, "its kind should come from the known neighbor (trigger's 'then' output) at the other end of the wire.");
            Assert.AreEqual(PortKind.Execution, execOut.Kind, "its kind should come from the known neighbor (log's 'exec' input) at the other end of the wire.");

            Assert.AreEqual(2, vm.Wires.Count, "both connections touching the missing node should still resolve to a drawable wire.");
        }

        [Test]
        public void MissingBlockNode_BetweenTwoMissingNodes_PortKindIsUnknown()
        {
            var rule = Rule("r", new List<RuleNode>
            {
                Node("missingA", "block.unknown.a", 0, 0),
                Node("missingB", "block.unknown.b", 200, 0)
            }, new List<RuleConnection>
            {
                Conn("missingA", "out", "missingB", "in")
            });

            var vm = new RuleCanvasViewModel(rule, Blocks());

            NodeViewModel missingA = vm.Nodes.First(n => n.Node.Id == "missingA");
            PortViewModel outPort = missingA.OutputPorts.First(p => p.Name == "out");

            Assert.AreEqual(PortKind.Data, outPort.Kind, "with no known neighbor, the kind falls back to the unknown default.");
            Assert.IsNull(outPort.DataType, "with no known neighbor, the data type is unknown (wildcard).");
        }

        // --- Smoke: builds without errors ---

        [Test]
        public void Canvas_BuildsViews_ForAGenerated200NodeRule_WithoutErrors()
        {
            var nodes = new List<RuleNode>();
            for (int i = 0; i < 200; i++)
            {
                nodes.Add(Node($"n{i}", "block.value.math", i * 50, (i % 10) * 60));
            }

            var connections = new List<RuleConnection>();
            for (int i = 0; i < 200; i++)
            {
                connections.Add(Conn($"n{i}", "result", $"n{(i + 1) % 200}", "a"));
            }

            for (int i = 0; i < 100; i++)
            {
                connections.Add(Conn($"n{i}", "result", $"n{(i + 2) % 200}", "b"));
            }

            Assert.AreEqual(300, connections.Count);

            var rule = Rule("r.generated_200", nodes, connections);

            Assert.DoesNotThrow(() =>
            {
                var vm = new RuleCanvasViewModel(rule, Blocks());
                Assert.AreEqual(200, vm.Nodes.Count);
                Assert.AreEqual(300, vm.Wires.Count);
            });
        }

        [Test]
        public void Canvas_BuildsViews_ForTheSampleRuleFile_WithoutErrors()
        {
            string coreContentFolder = System.IO.Path.Combine(Application.streamingAssetsPath, "Content", "core");
            var loader = new ContentLoader(BuiltInTypes(), ErrorMode.Development);
            (ContentDatabase database, _) = loader.Load(new[] { coreContentFolder });

            RuleDefinition sample = database.ListByType(BuiltInContentTypes.RuleTypeName)
                .Select(e => (RuleDefinition)e.Definition)
                .FirstOrDefault(r => r.Id == "rule.sample_canvas_preview");

            Assert.IsNotNull(sample, "Expected a sample rule 'rule.sample_canvas_preview' in the core content folder.");

            Assert.DoesNotThrow(() => new RuleCanvasViewModel(sample, Blocks()));
        }

        private static ContentTypeRegistry BuiltInTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }
    }
}
