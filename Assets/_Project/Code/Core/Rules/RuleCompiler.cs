using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Checks each enabled "rule" content item against the block registry, the
    /// settings registry and the event definition registry, and compiles it
    /// into a fast internal form. Does not run rules; prompt 2.2 adds execution.
    /// </summary>
    public static class RuleCompiler
    {
        private static readonly Dictionary<string, RuleFieldValueSource> EmptyFields = new Dictionary<string, RuleFieldValueSource>();

        public static (RuleRegistry Registry, RuleCompileReport Report) Compile(
            ContentDatabase database,
            BlockRegistry blocks,
            SettingsRegistry settings,
            EventDefinitionRegistry events,
            ErrorMode errorMode)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (blocks == null)
            {
                throw new ArgumentNullException(nameof(blocks));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var registry = new RuleRegistry();
            var report = new RuleCompileReport();

            foreach (ContentEntry entry in database.ListByType(BuiltInContentTypes.RuleTypeName))
            {
                var rule = (RuleDefinition)entry.Definition;
                if (!rule.Enabled)
                {
                    continue;
                }

                var errors = new List<RuleCompileProblem>();
                var warnings = new List<RuleCompileProblem>();

                CompiledRule compiled = CompileRule(rule, blocks, settings, events, errors, warnings);

                report.Errors.AddRange(errors);
                report.Warnings.AddRange(warnings);

                if (errors.Count == 0)
                {
                    registry.Add(compiled);
                }
            }

            if (errorMode == ErrorMode.Development && report.HasErrors)
            {
                string message = string.Join("\n", report.Errors.Select(e => e.ToString()));
                throw new RuleCompileException($"Rule compilation failed:\n{message}", report);
            }

            return (registry, report);
        }

        private static CompiledRule CompileRule(
            RuleDefinition rule,
            BlockRegistry blocks,
            SettingsRegistry settings,
            EventDefinitionRegistry events,
            List<RuleCompileProblem> errors,
            List<RuleCompileProblem> warnings)
        {
            string ruleId = rule.Id;

            // --- Nodes: duplicate Id check + block type resolution. ---
            var nodesById = new Dictionary<string, RuleNode>();
            var blockTypesById = new Dictionary<string, BlockType>();

            foreach (RuleNode node in rule.Nodes ?? Enumerable.Empty<RuleNode>())
            {
                if (nodesById.ContainsKey(node.Id))
                {
                    errors.Add(new RuleCompileProblem(ruleId, node.Id, $"Node Id '{node.Id}' is used more than once."));
                    continue;
                }

                nodesById[node.Id] = node;

                if (blocks.TryGet(node.BlockTypeId, out BlockType blockType))
                {
                    blockTypesById[node.Id] = blockType;
                }
                else
                {
                    errors.Add(new RuleCompileProblem(ruleId, node.Id, $"Unknown block type '{node.BlockTypeId}'."));
                }
            }

            // --- Trigger count. ---
            List<string> triggerNodeIds = blockTypesById
                .Where(kvp => kvp.Value.Kind == BlockKind.Trigger)
                .Select(kvp => kvp.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            if (triggerNodeIds.Count == 0)
            {
                errors.Add(new RuleCompileProblem(ruleId, null, "The rule has no trigger."));
            }
            else if (triggerNodeIds.Count > 1)
            {
                errors.Add(new RuleCompileProblem(ruleId, null, $"The rule has more than one trigger ({string.Join(", ", triggerNodeIds)})."));
            }

            // --- Connections: resolve ports, check kind/type/arity. ---
            var compiledConnections = new List<CompiledRuleConnection>();
            var executionEdges = new List<(string From, string To)>();
            var dataEdges = new List<(string From, string To)>();
            var execOutgoingCount = new Dictionary<(string Node, string Port), int>();
            var dataIncomingCount = new Dictionary<(string Node, string Port), int>();
            var connectedDataInputs = new HashSet<(string Node, string Port)>();

            foreach (RuleConnection connection in rule.Connections ?? Enumerable.Empty<RuleConnection>())
            {
                if (!blockTypesById.TryGetValue(connection.FromNode, out BlockType fromBlockType))
                {
                    errors.Add(new RuleCompileProblem(ruleId, connection.FromNode, $"Connection references unknown node '{connection.FromNode}'."));
                    continue;
                }

                if (!blockTypesById.TryGetValue(connection.ToNode, out BlockType toBlockType))
                {
                    errors.Add(new RuleCompileProblem(ruleId, connection.ToNode, $"Connection references unknown node '{connection.ToNode}'."));
                    continue;
                }

                PortDefinition fromPort = fromBlockType.FindPort(connection.FromPort, PortDirection.Output);
                if (fromPort == null)
                {
                    errors.Add(new RuleCompileProblem(ruleId, connection.FromNode, $"Unknown output port '{connection.FromPort}' on block '{fromBlockType.Id}'."));
                    continue;
                }

                PortDefinition toPort = toBlockType.FindPort(connection.ToPort, PortDirection.Input);
                if (toPort == null)
                {
                    errors.Add(new RuleCompileProblem(ruleId, connection.ToNode, $"Unknown input port '{connection.ToPort}' on block '{toBlockType.Id}'."));
                    continue;
                }

                if (fromPort.Kind != toPort.Kind)
                {
                    errors.Add(new RuleCompileProblem(ruleId, connection.FromNode, $"Cannot connect {fromPort.Kind} output '{connection.FromPort}' to {toPort.Kind} input '{connection.ToPort}'."));
                    continue;
                }

                if (fromPort.Kind == PortKind.Data && fromPort.DataType != toPort.DataType)
                {
                    errors.Add(new RuleCompileProblem(ruleId, connection.FromNode, $"Data type mismatch: '{connection.FromPort}' ({fromPort.DataType}) to '{connection.ToPort}' ({toPort.DataType})."));
                    continue;
                }

                var fromKey = (connection.FromNode, connection.FromPort);
                var toKey = (connection.ToNode, connection.ToPort);

                if (fromPort.Kind == PortKind.Execution)
                {
                    execOutgoingCount.TryGetValue(fromKey, out int count);
                    count++;
                    execOutgoingCount[fromKey] = count;

                    if (count > 1)
                    {
                        errors.Add(new RuleCompileProblem(ruleId, connection.FromNode, $"Execution output '{connection.FromPort}' already connects to another input; it may only connect to one."));
                        continue;
                    }
                }

                if (toPort.Kind == PortKind.Data)
                {
                    dataIncomingCount.TryGetValue(toKey, out int count);
                    count++;
                    dataIncomingCount[toKey] = count;

                    if (count > 1)
                    {
                        errors.Add(new RuleCompileProblem(ruleId, connection.ToNode, $"Data input '{connection.ToPort}' already has an incoming wire; it may only have one."));
                        continue;
                    }

                    connectedDataInputs.Add(toKey);
                }

                int fromIndex = IndexOfPort(fromBlockType.Ports, fromPort);
                int toIndex = IndexOfPort(toBlockType.Ports, toPort);
                compiledConnections.Add(new CompiledRuleConnection(connection.FromNode, fromIndex, connection.ToNode, toIndex));

                if (fromPort.Kind == PortKind.Execution)
                {
                    executionEdges.Add((connection.FromNode, connection.ToNode));
                }
                else
                {
                    dataEdges.Add((connection.FromNode, connection.ToNode));
                }
            }

            // --- Per-node: required data inputs + field resolution. ---
            var compiledNodes = new Dictionary<string, CompiledRuleNode>();

            foreach (KeyValuePair<string, BlockType> kvp in blockTypesById)
            {
                string nodeId = kvp.Key;
                BlockType blockType = kvp.Value;
                RuleNode node = nodesById[nodeId];

                foreach (PortDefinition port in blockType.Ports)
                {
                    if (port.Kind != PortKind.Data || port.Direction != PortDirection.Input || !port.Required)
                    {
                        continue;
                    }

                    if (connectedDataInputs.Contains((nodeId, port.Name)))
                    {
                        continue;
                    }

                    if (blockType.FindField(port.Name) == null)
                    {
                        errors.Add(new RuleCompileProblem(ruleId, nodeId, $"Required input '{port.Name}' has neither a wire nor a field value."));
                    }
                }

                var compiledFields = new Dictionary<string, CompiledFieldValue>();

                foreach (FieldDefinition field in blockType.Fields)
                {
                    RuleFieldValueSource source = null;
                    node.Fields?.TryGetValue(field.Name, out source);

                    if (source == null)
                    {
                        compiledFields[field.Name] = CompiledFieldValue.FromLiteral(field.DataType, field.DefaultValue);
                        continue;
                    }

                    if (source.IsSettingReference)
                    {
                        if (!settings.TryGetDefinition(source.SettingId, out SettingDefinition settingDefinition))
                        {
                            errors.Add(new RuleCompileProblem(ruleId, nodeId, $"Field '{field.Name}' references setting '{source.SettingId}', which does not exist."));
                            continue;
                        }

                        bool compatible = field.DataType switch
                        {
                            RuleDataType.Number => settingDefinition.ValueType == SettingValueType.Number || settingDefinition.ValueType == SettingValueType.Integer,
                            RuleDataType.Bool => settingDefinition.ValueType == SettingValueType.Boolean,
                            _ => false
                        };

                        if (!compatible)
                        {
                            errors.Add(new RuleCompileProblem(ruleId, nodeId, $"Field '{field.Name}' references setting '{source.SettingId}', which is not a number or true-or-false."));
                            continue;
                        }

                        compiledFields[field.Name] = CompiledFieldValue.FromSetting(field.DataType, source.SettingId);
                    }
                    else
                    {
                        object literalValue = ConvertLiteral(field.DataType, source.Literal, field.DefaultValue);
                        compiledFields[field.Name] = CompiledFieldValue.FromLiteral(field.DataType, literalValue);
                    }
                }

                compiledNodes[nodeId] = new CompiledRuleNode(nodeId, blockType, node.X, node.Y, compiledFields);
            }

            string triggerNodeId = triggerNodeIds.Count == 1 ? triggerNodeIds[0] : null;

            // --- Warnings: unreachable nodes. ---
            if (triggerNodeId != null)
            {
                var reachable = new HashSet<string> { triggerNodeId };

                Dictionary<string, List<string>> execAdjacency = executionEdges
                    .GroupBy(e => e.From)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());

                var queue = new Queue<string>();
                queue.Enqueue(triggerNodeId);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    if (execAdjacency.TryGetValue(current, out List<string> next))
                    {
                        foreach (string to in next)
                        {
                            if (reachable.Add(to))
                            {
                                queue.Enqueue(to);
                            }
                        }
                    }
                }

                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach ((string from, string to) in dataEdges)
                    {
                        if (reachable.Contains(to) && reachable.Add(from))
                        {
                            changed = true;
                        }
                    }
                }

                foreach (string nodeId in blockTypesById.Keys.OrderBy(id => id, StringComparer.Ordinal))
                {
                    if (!reachable.Contains(nodeId))
                    {
                        warnings.Add(new RuleCompileProblem(ruleId, nodeId, "This node is not reachable from the trigger."));
                    }
                }

                // --- Warnings: a possible loop (an action posts the event the trigger listens to). ---
                BlockType triggerBlockType = blockTypesById[triggerNodeId];
                RuleNode triggerNode = nodesById[triggerNodeId];
                string listenedEventId = triggerBlockType.GetTriggerEventDefinitionId(triggerNode.Fields ?? EmptyFields);

                if (!string.IsNullOrEmpty(listenedEventId))
                {
                    foreach (string nodeId in blockTypesById.Keys.OrderBy(id => id, StringComparer.Ordinal))
                    {
                        BlockType blockType = blockTypesById[nodeId];
                        if (blockType.Kind != BlockKind.Action)
                        {
                            continue;
                        }

                        RuleNode node = nodesById[nodeId];
                        IEnumerable<string> postedIds = blockType.GetPostedEventDefinitionIds(node.Fields ?? EmptyFields) ?? Enumerable.Empty<string>();

                        if (postedIds.Contains(listenedEventId, StringComparer.Ordinal))
                        {
                            warnings.Add(new RuleCompileProblem(ruleId, nodeId, $"This action posts event '{listenedEventId}', the same event the rule's trigger listens to (possible loop)."));
                        }
                    }
                }
            }

            return new CompiledRule(ruleId, rule.Name, triggerNodeId, compiledNodes, compiledConnections);
        }

        private static int IndexOfPort(IReadOnlyList<PortDefinition> ports, PortDefinition target)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ReferenceEquals(ports[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        private static object ConvertLiteral(RuleDataType dataType, JToken token, object fallback)
        {
            if (token == null)
            {
                return fallback;
            }

            try
            {
                return dataType switch
                {
                    RuleDataType.Number => token.Value<double>(),
                    RuleDataType.Bool => token.Value<bool>(),
                    RuleDataType.Text => token.Value<string>(),
                    RuleDataType.ContentId => token.Value<string>(),
                    RuleDataType.Entity => token.Value<string>(),
                    _ => fallback
                };
            }
            catch
            {
                return fallback;
            }
        }
    }
}
