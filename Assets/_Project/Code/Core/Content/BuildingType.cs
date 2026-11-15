using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A kind of building: footprint, construction cost, job slots, which zones allow it, and the home it provides (if any). Immutable once loaded.</summary>
    public sealed class BuildingType : ContentDefinition
    {
        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("footprintWidth")]
        public int FootprintWidth { get; }

        [JsonProperty("footprintDepth")]
        public int FootprintDepth { get; }

        [JsonProperty("constructionCost")]
        public IReadOnlyList<GoodAmount> ConstructionCost { get; }

        [JsonProperty("workerSlots")]
        public int WorkerSlots { get; }

        [JsonProperty("managerSlots")]
        public int ManagerSlots { get; }

        [JsonProperty("trainerSlots")]
        public int TrainerSlots { get; }

        /// <summary>Allowed in its zones by default (true), or must be discovered by someone wanting one first (false).</summary>
        [JsonProperty("isBasic")]
        public bool IsBasic { get; }

        [JsonProperty("allowedZones")]
        public IReadOnlyList<ContentRef<Zone>> AllowedZones { get; }

        /// <summary>How many households this building houses; 0 if it isn't a home.</summary>
        [JsonProperty("householdsProvided")]
        public int HouseholdsProvided { get; }

        /// <summary>The class of household it houses; null if it isn't a home.</summary>
        [JsonProperty("homeClass")]
        public SocialClass? HomeClass { get; }

        [JsonConstructor]
        public BuildingType(
            string id,
            string name,
            int footprintWidth,
            int footprintDepth,
            IReadOnlyList<GoodAmount> constructionCost,
            int workerSlots,
            int managerSlots,
            int trainerSlots,
            bool isBasic,
            IReadOnlyList<ContentRef<Zone>> allowedZones,
            int householdsProvided,
            SocialClass? homeClass)
        {
            Id = id;
            Name = name;
            FootprintWidth = footprintWidth;
            FootprintDepth = footprintDepth;
            ConstructionCost = constructionCost ?? Array.Empty<GoodAmount>();
            WorkerSlots = workerSlots;
            ManagerSlots = managerSlots;
            TrainerSlots = trainerSlots;
            IsBasic = isBasic;
            AllowedZones = allowedZones ?? Array.Empty<ContentRef<Zone>>();
            HouseholdsProvided = householdsProvided;
            HomeClass = homeClass;
        }
    }
}
