using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Describes one kind of node a rule can use: its Id, category, kind, ports
    /// and fields, plus how it runs.
    /// </summary>
    public abstract class BlockType
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Category { get; }
        public abstract BlockKind Kind { get; }
        public abstract IReadOnlyList<PortDefinition> Ports { get; }
        public abstract IReadOnlyList<FieldDefinition> Fields { get; }

        public PortDefinition FindPort(string name, PortDirection direction)
        {
            return Ports.FirstOrDefault(p => p.Direction == direction && p.Name == name);
        }

        public FieldDefinition FindField(string name)
        {
            return Fields.FirstOrDefault(f => f.Name == name);
        }

        /// <summary>
        /// For a Trigger block: which event definition Id this node's fields
        /// make it listen to, or null if it isn't event-triggered. Used only by
        /// the compiler's loop warning.
        /// </summary>
        public virtual string GetTriggerEventDefinitionId(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            return null;
        }

        /// <summary>
        /// For an Action block: which event definition Ids this node's fields
        /// make it post. Used only by the compiler's loop warning.
        /// </summary>
        public virtual IEnumerable<string> GetPostedEventDefinitionIds(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Extra compile-time checks beyond the generic port/wire/type checks
        /// (e.g. Ask Player's choice-count and default-choice checks). Called
        /// once per node with its raw field values and the names of its
        /// execution OUTPUT ports that have an outgoing wire. Return one
        /// message per error; empty default means "nothing extra to check".
        /// </summary>
        public virtual IEnumerable<string> ValidateNode(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues, ISet<string> wiredExecutionOutputPorts)
        {
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Only meaningful for Kind == Trigger: how this rule activates. Called
        /// once, when the RuleRunner activates the rule, with the trigger
        /// node's compiled fields.
        /// </summary>
        public virtual RuleTriggerDescriptor DescribeTrigger(IReadOnlyDictionary<string, CompiledFieldValue> fields, SettingsRegistry settings)
        {
            return null;
        }

        /// <summary>
        /// Only meaningful for Kind == Value: computes this node's single
        /// output value from its (already-resolved) inputs. The caller caches
        /// the result for the rest of the current execution step.
        /// </summary>
        public virtual RuleValue ComputeValue(IRuleNodeContext context)
        {
            return default;
        }

        /// <summary>
        /// Only meaningful for Kind == Action or Flow: runs one step. Returns
        /// what the runner should do next: follow one output, follow several
        /// in order (Sequence), wait, or end this path.
        /// </summary>
        public virtual RuleExecutionResult Execute(IRuleNodeContext context)
        {
            return RuleExecutionResult.End;
        }
    }
}
