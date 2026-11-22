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

        /// <summary>Placeable anywhere, regardless of zones (the crown's own buildings). A non-government building type must allow at least one zone.</summary>
        [JsonProperty("isGovernment")]
        public bool IsGovernment { get; }

        [JsonProperty("allowedZones")]
        public IReadOnlyList<ContentRef<Zone>> AllowedZones { get; }

        /// <summary>How many households this building houses; 0 if it isn't a home.</summary>
        [JsonProperty("householdsProvided")]
        public int HouseholdsProvided { get; }

        /// <summary>The class of household it houses; null if it isn't a home.</summary>
        [JsonProperty("homeClass")]
        public SocialClass? HomeClass { get; }

        /// <summary>3.10b: a government building the founders build themselves when the town needs it (a saw pit, a camp, a cabin...), never offered in the player's build menu.</summary>
        [JsonProperty("isFounderBuilt")]
        public bool IsFounderBuilt { get; }

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
            bool isGovernment,
            IReadOnlyList<ContentRef<Zone>> allowedZones,
            int householdsProvided,
            SocialClass? homeClass,
            bool isFounderBuilt = false)
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
            IsGovernment = isGovernment;
            AllowedZones = allowedZones ?? Array.Empty<ContentRef<Zone>>();
            HouseholdsProvided = householdsProvided;
            HomeClass = homeClass;
            IsFounderBuilt = isFounderBuilt;
        }
    }
}
