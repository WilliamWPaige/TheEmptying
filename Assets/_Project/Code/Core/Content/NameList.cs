using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A list of first or family names people are drawn from. Immutable once loaded.</summary>
    public sealed class NameList : ContentDefinition
    {
        [JsonProperty("kind")]
        public NameListKind Kind { get; }

        [JsonProperty("names")]
        public IReadOnlyList<string> Names { get; }

        [JsonConstructor]
        public NameList(string id, NameListKind kind, IReadOnlyList<string> names)
        {
            Id = id;
            Kind = kind;
            Names = names ?? Array.Empty<string>();
        }
    }
}
