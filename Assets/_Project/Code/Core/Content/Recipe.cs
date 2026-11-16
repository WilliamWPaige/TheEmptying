using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Core.Content
{
    /// <summary>A production recipe: a workplace, its inputs and outputs, and how long it takes. Immutable once loaded.</summary>
    public sealed class Recipe : ContentDefinition
    {
        [JsonProperty("workplace")]
        public ContentRef<BuildingType> Workplace { get; }

        [JsonProperty("inputs")]
        public IReadOnlyList<GoodAmount> Inputs { get; }

        [JsonProperty("outputs")]
        public IReadOnlyList<GoodAmount> Outputs { get; }

        [JsonProperty("durationHours")]
        public float DurationHours { get; }

        [JsonProperty("workerTier")]
        public WorkerTier WorkerTier { get; }

        /// <summary>The name of the skill (from People's Skill enum) this recipe's worker uses and trains. Kept as a string so Core content never references a module's types.</summary>
        [JsonProperty("skill")]
        public string Skill { get; }

        [JsonProperty("isHardLabor")]
        public bool IsHardLabor { get; }

        /// <summary>Seasons this recipe can be started in; empty means any season. Only meaningful for farming recipes.</summary>
        [JsonProperty("seasonsAllowed")]
        public IReadOnlyList<Season> SeasonsAllowed { get; }

        /// <summary>Months until a crop recipe's output is ready; 0 for a recipe that isn't a crop.</summary>
        [JsonProperty("growthTimeMonths")]
        public int GrowthTimeMonths { get; }

        [JsonConstructor]
        public Recipe(
            string id,
            ContentRef<BuildingType> workplace,
            IReadOnlyList<GoodAmount> inputs,
            IReadOnlyList<GoodAmount> outputs,
            float durationHours,
            WorkerTier workerTier,
            string skill,
            bool isHardLabor,
            IReadOnlyList<Season> seasonsAllowed,
            int growthTimeMonths)
        {
            Id = id;
            Workplace = workplace;
            Inputs = inputs ?? Array.Empty<GoodAmount>();
            Outputs = outputs ?? Array.Empty<GoodAmount>();
            DurationHours = durationHours;
            WorkerTier = workerTier;
            Skill = skill ?? string.Empty;
            IsHardLabor = isHardLabor;
            SeasonsAllowed = seasonsAllowed ?? Array.Empty<Season>();
            GrowthTimeMonths = growthTimeMonths;
        }
    }
}
