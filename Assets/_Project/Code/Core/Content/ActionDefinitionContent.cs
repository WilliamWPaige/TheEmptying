using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>R14: one kind of action a person can perform (Walk, Idle, Eat, Dig, Inside a building) - just enough to show in the Discoveries window once someone has performed it.</summary>
    public sealed class ActionDefinitionContent : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
