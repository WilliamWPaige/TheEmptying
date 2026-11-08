using System.Collections.Generic;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// One run of a rule: its Id, the triggering event (if any), local
    /// variables, the chain depth to stamp on any event this run posts, and
    /// its whole execution position. A fresh instance runs synchronously from
    /// trigger to completion unless it waits, in which case its Position (and
    /// everything else here) is exactly what gets saved and resumed.
    /// </summary>
    public sealed class RuleInstance
    {
        public long InstanceId { get; }
        public string RuleId { get; }

        /// <summary>The event that triggered this run, or null for a schedule-triggered run.</summary>
        public PostedEvent TriggeringEvent { get; }

        /// <summary>The chain depth to stamp on any event this instance posts: the triggering event's depth plus 1, or 0 for a schedule-triggered run.</summary>
        public int PostedEventChainDepth { get; }

        public RuleExecutionPosition Position { get; } = new RuleExecutionPosition();

        /// <summary>Internal: the save code (same assembly) restores these directly when loading a paused instance.</summary>
        internal readonly Dictionary<string, RuleValue> Locals = new Dictionary<string, RuleValue>();

        public RuleInstance(long instanceId, string ruleId, PostedEvent triggeringEvent, int postedEventChainDepth)
        {
            InstanceId = instanceId;
            RuleId = ruleId;
            TriggeringEvent = triggeringEvent;
            PostedEventChainDepth = postedEventChainDepth;
        }

        public RuleValue GetLocal(string name, RuleDataType type)
        {
            return Locals.TryGetValue(name, out RuleValue value) ? value : RuleValue.Default(type);
        }

        public void SetLocal(string name, RuleDataType type, RuleValue value)
        {
            Locals[name] = value;
        }

        public bool TryGetEventParameter(string name, RuleDataType type, out RuleValue value)
        {
            value = default;
            EventData data = TriggeringEvent?.Data;
            if (data == null)
            {
                return false;
            }

            switch (type)
            {
                case RuleDataType.Number:
                    if (data.TryGetDouble(name, out double number))
                    {
                        value = RuleValue.Number(number);
                        return true;
                    }

                    break;

                case RuleDataType.Bool:
                    if (data.TryGetBool(name, out bool boolean))
                    {
                        value = RuleValue.Bool(boolean);
                        return true;
                    }

                    break;

                case RuleDataType.Text:
                    if (data.TryGetString(name, out string text))
                    {
                        value = RuleValue.Text(text);
                        return true;
                    }

                    break;

                case RuleDataType.ContentId:
                    if (data.TryGetString(name, out string contentId))
                    {
                        value = RuleValue.ContentId(contentId);
                        return true;
                    }

                    break;

                case RuleDataType.Entity:
                    if (data.TryGetEntity(name, out EntityId entity))
                    {
                        value = RuleValue.Entity(entity);
                        return true;
                    }

                    break;
            }

            return false;
        }
    }
}
