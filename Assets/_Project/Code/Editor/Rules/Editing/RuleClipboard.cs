using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// Plain JSON serialization for a set of nodes and the wires between
    /// them, with a recognizable header so paste can tell a real payload from
    /// unrelated clipboard text. Pure text in, text out - the actual system
    /// clipboard (GUIUtility.systemCopyBuffer) is read and written by the UI,
    /// never here, so this stays testable without one.
    /// </summary>
    internal static class RuleClipboard
    {
        private const string Header = "TheEmptyingRuleClipboard:v1:";

        private sealed class Payload
        {
            [JsonProperty("nodes")]
            public List<RuleNode> Nodes { get; set; } = new List<RuleNode>();

            [JsonProperty("connections")]
            public List<RuleConnection> Connections { get; set; } = new List<RuleConnection>();
        }

        public static string Serialize(IReadOnlyList<RuleNode> nodes, IReadOnlyList<RuleConnection> connections)
        {
            var payload = new Payload { Nodes = new List<RuleNode>(nodes), Connections = new List<RuleConnection>(connections) };
            return Header + JsonConvert.SerializeObject(payload);
        }

        public static bool TryDeserialize(string text, out List<RuleNode> nodes, out List<RuleConnection> connections)
        {
            nodes = null;
            connections = null;

            if (string.IsNullOrEmpty(text) || !text.StartsWith(Header, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                Payload payload = JsonConvert.DeserializeObject<Payload>(text.Substring(Header.Length));
                nodes = payload?.Nodes ?? new List<RuleNode>();
                connections = payload?.Connections ?? new List<RuleConnection>();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
