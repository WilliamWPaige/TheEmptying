namespace TheEmptying.Core.Rules
{
    public sealed class EventTriggerDescriptor : RuleTriggerDescriptor
    {
        public string EventDefinitionId { get; }

        public EventTriggerDescriptor(string eventDefinitionId)
        {
            EventDefinitionId = eventDefinitionId;
        }
    }
}
