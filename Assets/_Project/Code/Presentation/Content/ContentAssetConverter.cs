using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Copies each ScriptableObject content asset into its plain record, in
    /// ordinal Id order, ready for ContentLoader to add to the database
    /// alongside the JSON sources. The simulation only ever sees the
    /// records. A missing Model can't be checked there (it's a UnityEngine
    /// prefab reference, not something the plain record holds), so it's
    /// reported here instead - into the same problem list ContentLoader's
    /// own validation reports into (SimulationBuilder folds the two
    /// together), so a missing model stops the game exactly like every
    /// other content problem (R5b).
    /// </summary>
    public static class ContentAssetConverter
    {
        private const string SourceId = "content-assets";

        public static List<ContentAssetDefinition> BuildDefinitions(ContentCatalog catalog, List<ContentValidationProblem> problems)
        {
            var definitions = new List<ContentAssetDefinition>();
            if (catalog == null)
            {
                return definitions;
            }

            AddSorted(definitions, problems, catalog.Units, BuiltInContentTypes.UnitTypeName, ToRecord);
            AddSorted(definitions, problems, catalog.ResourceNodes, BuiltInContentTypes.ResourceTypeName, ToRecord);
            AddSorted(definitions, problems, catalog.Buildings, BuiltInContentTypes.BuildingTypeName, ToRecord);
            AddSorted(definitions, problems, catalog.Items, BuiltInContentTypes.ItemTypeName, ToRecord);

            return definitions;
        }

        private static void AddSorted<TAsset>(
            List<ContentAssetDefinition> definitions,
            List<ContentValidationProblem> problems,
            IReadOnlyList<TAsset> assets,
            string typeName,
            Func<TAsset, ContentDefinition> toRecord)
            where TAsset : ContentAssetBase
        {
            foreach (TAsset asset in assets.Where(a => a != null).OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                if (asset.Model == null)
                {
                    problems.Add(new ContentValidationProblem(SourceId, asset.name, asset.Id, "model", "Model is missing."));
                    continue;
                }

                definitions.Add(new ContentAssetDefinition(typeName, toRecord(asset), SourceId, asset.name));
            }
        }

        private static UnitContent ToRecord(UnitAsset asset) => new UnitContent { Id = asset.Id, DisplayName = asset.DisplayName };

        private static ItemContent ToRecord(ItemAsset asset) => new ItemContent
        {
            Id = asset.Id,
            DisplayName = asset.DisplayName,
            FoodValue = asset.FoodValue,
            CarryAmount = asset.CarryAmount,
            BaseValueTenthCopper = asset.BaseValueTenthCopper,
            SpoilTimeMinutes = asset.SpoilTimeMinutes,
            Size = asset.Size
        };

        private static ResourceContent ToRecord(ResourceAsset asset) => new ResourceContent
        {
            Id = asset.Id,
            DisplayName = asset.DisplayName,
            FootprintWidth = asset.FootprintWidth,
            FootprintDepth = asset.FootprintDepth,
            YieldedItem = new ContentRef<ItemContent>(asset.YieldedItemId),
            UnlimitedAmount = asset.UnlimitedAmount,
            AmountHeld = asset.AmountHeld,
            HarvestTimeUnskilledMinutes = asset.HarvestTimeUnskilledMinutes,
            HarvestTimeSkilledMinutes = asset.HarvestTimeSkilledMinutes,
            HarvestTimeMasterMinutes = asset.HarvestTimeMasterMinutes,
            MaxWorkers = asset.MaxWorkers,
            GatherItem = new ContentRef<ItemContent>(asset.GatherItemId),
            GatherRadiusMeters = asset.GatherRadiusMeters,
            GatherTimeUnskilledMinutes = asset.GatherTimeUnskilledMinutes,
            GatherTimeSkilledMinutes = asset.GatherTimeSkilledMinutes,
            GatherTimeMasterMinutes = asset.GatherTimeMasterMinutes,
            GatherMaxConcurrent = asset.GatherMaxConcurrent,
            GatherBonusItem = new ContentRef<ItemContent>(asset.GatherBonusItemId),
            GatherBonusChance = asset.GatherBonusChance
        };

        private static BuildingContent ToRecord(BuildingAsset asset) => new BuildingContent
        {
            Id = asset.Id,
            DisplayName = asset.DisplayName,
            FootprintWidth = asset.FootprintWidth,
            FootprintDepth = asset.FootprintDepth
        };
    }
}
