using System.Collections.Generic;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// One run of a rule: the triggering event (if any), local variables, and
    /// the chain depth to stamp on any event this run posts. Lives only for
    /// the duration of one synchronous run from trigger to completion.
    /// </summary>
    public sealed class RuleInstance
    {
        public string RuleId { get; }

        /// <summary>The event that triggered this run, or null for a schedule-triggered run.</summary>
        public PostedEvent TriggeringEvent { get; }

        /// <summary>The chain depth to stamp on any event this instance posts: the triggering event's depth plus 1, or 0 for a schedule-triggered run.</summary>
        public int PostedEventChainDepth { get; }

        private readonly Dictionary<string, RuleValue> _locals = new Dictionary<string, RuleValue>();

        public RuleInstance(string ruleId, PostedEvent triggeringEvent, int postedEventChainDepth)
        {
            RuleId = ruleId;
            TriggeringEvent = triggeringEvent;
            PostedEventChainDepth = postedEventChainDepth;
        }

        public RuleValue GetLocal(string name, RuleDataType type)
        {
            return _locals.TryGetValue(name, out RuleValue value) ? value : RuleValue.Default(type);
        }

        public void SetLocal(string name, RuleDataType type, RuleValue value)
        {
            _locals[name] = value;
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
