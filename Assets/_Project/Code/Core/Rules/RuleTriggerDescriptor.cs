namespace TheEmptying.Core.Rules
{
    /// <summary>How a compiled rule's trigger activates: by event definition Id, or by a schedule. Resolved once, when the RuleRunner activates the rule.</summary>
    public abstract class RuleTriggerDescriptor
    {
    }

    public sealed class EventTriggerDescriptor : RuleTriggerDescriptor
    {
        public string EventDefinitionId { get; }

        public EventTriggerDescriptor(string eventDefinitionId)
        {
            EventDefinitionId = eventDefinitionId;
        }
    }

    public enum RuleScheduleKind
    {
        EveryNHours,
        AtHourOfDay,
        OnNewDay
    }

    public sealed class ScheduleTriggerDescriptor : RuleTriggerDescriptor
    {
        public RuleScheduleKind ScheduleKind { get; }

        /// <summary>In-game hours between fires. Only meaningful for EveryNHours.</summary>
        public int Hours { get; }

        /// <summary>The hour of day (0 to 23) to fire at. Only meaningful for AtHourOfDay.</summary>
        public int HourOfDay { get; }

        private ScheduleTriggerDescriptor(RuleScheduleKind kind, int hours, int hourOfDay)
        {
            ScheduleKind = kind;
            Hours = hours;
            HourOfDay = hourOfDay;
        }

        public static ScheduleTriggerDescriptor EveryNHours(int hours) => new ScheduleTriggerDescriptor(RuleScheduleKind.EveryNHours, hours, 0);
        public static ScheduleTriggerDescriptor AtHourOfDay(int hourOfDay) => new ScheduleTriggerDescriptor(RuleScheduleKind.AtHourOfDay, 0, hourOfDay);
        public static ScheduleTriggerDescriptor OnNewDay() => new ScheduleTriggerDescriptor(RuleScheduleKind.OnNewDay, 0, 0);
    }
}
