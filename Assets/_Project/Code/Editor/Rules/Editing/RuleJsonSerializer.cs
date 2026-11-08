using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// Reads and writes a rule content file ({"type": "rule", "items": [...]})
    /// with a stable, deterministic byte layout: fixed key order, fixed
    /// indentation, nodes/connections/rules kept in their existing list order
    /// (never resorted), and field values written in ascending key order. An
    /// untouched field's literal JToken is re-emitted exactly as parsed, so an
    /// unedited value's original number/string formatting survives a
    /// load-then-save round trip byte for byte.
    /// </summary>
    public static class RuleJsonSerializer
    {
        public static List<RuleDefinition> DeserializeFile(string json)
        {
            JObject root = JObject.Parse(json);
            var items = (JArray)root["items"];
            var result = new List<RuleDefinition>();

            if (items == null)
            {
                return result;
            }

            foreach (JToken item in items)
            {
                result.Add(item.ToObject<RuleDefinition>());
            }

            return result;
        }

        public static string SerializeFile(IReadOnlyList<RuleDefinition> rules)
        {
            var root = new JObject
            {
                ["type"] = "rule",
                ["items"] = new JArray(rules.Select(SerializeRule))
            };

            return root.ToString(Formatting.Indented);
        }

        private static JObject SerializeRule(RuleDefinition rule)
        {
            return new JObject
            {
                ["id"] = rule.Id,
                ["name"] = rule.Name ?? string.Empty,
                ["description"] = rule.Description ?? string.Empty,
                ["enabled"] = rule.Enabled,
                ["nodes"] = new JArray((rule.Nodes ?? new List<RuleNode>()).Select(SerializeNode)),
                ["connections"] = new JArray((rule.Connections ?? new List<RuleConnection>()).Select(SerializeConnection))
            };
        }

        private static JObject SerializeNode(RuleNode node)
        {
            return new JObject
            {
                ["id"] = node.Id,
                ["blockType"] = node.BlockTypeId,
                ["x"] = node.X,
                ["y"] = node.Y,
                ["fields"] = SerializeFields(node.Fields)
            };
        }

        private static JObject SerializeFields(Dictionary<string, RuleFieldValueSource> fields)
        {
            var obj = new JObject();
            if (fields == null)
            {
                return obj;
            }

            foreach (string key in fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                obj[key] = SerializeFieldValue(fields[key]);
            }

            return obj;
        }

        private static JToken SerializeFieldValue(RuleFieldValueSource source)
        {
            if (source == null)
            {
                return JValue.CreateNull();
            }

            if (source.IsSettingReference)
            {
                return new JObject { ["setting"] = source.SettingId };
            }

            return source.Literal ?? JValue.CreateNull();
        }

        private static JObject SerializeConnection(RuleConnection connection)
        {
            return new JObject
            {
                ["fromNode"] = connection.FromNode,
                ["fromPort"] = connection.FromPort,
                ["toNode"] = connection.ToNode,
                ["toPort"] = connection.ToPort
            };
        }
    }
}
