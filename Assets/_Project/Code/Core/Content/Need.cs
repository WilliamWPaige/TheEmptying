using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A need a person can have: a meter that drains over time, or a state that doesn't. Immutable once loaded.</summary>
    public sealed class Need : ContentDefinition
    {
        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("kind")]
        public NeedKind Kind { get; }

        /// <summary>Hours until a meter need runs out, starting full. Not meaningful for a state need.</summary>
        [JsonProperty("hoursToEmpty")]
        public float HoursToEmpty { get; }

        /// <summary>The lowest class that has this need; every class at or above it has it too.</summary>
        [JsonProperty("minimumClass")]
        public SocialClass MinimumClass { get; }

        [JsonProperty("believersOnly")]
        public bool BelieversOnly { get; }

        [JsonConstructor]
        public Need(string id, string name, NeedKind kind, float hoursToEmpty, SocialClass minimumClass, bool believersOnly)
        {
            Id = id;
            Name = name;
            Kind = kind;
            HoursToEmpty = hoursToEmpty;
            MinimumClass = minimumClass;
            BelieversOnly = believersOnly;
        }
    }
}
