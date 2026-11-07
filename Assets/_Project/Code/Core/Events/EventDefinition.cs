using System;

namespace TheEmptying.Core.Events
{
    /// <summary>Plain data. For now registered from code; prompt 1.6 loads these from data files.</summary>
    public sealed class EventDefinition
    {
        public string Id { get; }
        public EventVisibility Visibility { get; }
        public float ReportingScore { get; }
        public float NotabilityScore { get; }
        public string Description { get; }

        public EventDefinition(string id, EventVisibility visibility, float reportingScore, float notabilityScore, string description)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id must not be empty.", nameof(id));
            }

            if (reportingScore < 0f || reportingScore > 10f)
            {
                throw new ArgumentOutOfRangeException(nameof(reportingScore), "ReportingScore must be between 0 and 10.");
            }

            if (notabilityScore < 0f || notabilityScore > 10f)
            {
                throw new ArgumentOutOfRangeException(nameof(notabilityScore), "NotabilityScore must be between 0 and 10.");
            }

            Id = id;
            Visibility = visibility;
            ReportingScore = reportingScore;
            NotabilityScore = notabilityScore;
            Description = description ?? string.Empty;
        }
    }
}
