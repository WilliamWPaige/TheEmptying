using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Copies each ScriptableObject content asset into its plain record, in
    /// ordinal Id order, ready for ContentLoader to add to the database
    /// alongside the JSON sources. The simulation only ever sees the records.
    /// </summary>
    public static class ContentAssetConverter
    {
        private const string SourceId = "content-assets";

        public static List<ContentAssetDefinition> BuildDefinitions(ContentCatalog catalog)
        {
            var definitions = new List<ContentAssetDefinition>();
            if (catalog == null)
            {
                return definitions;
            }

            AddSorted(definitions, catalog.Goods, ToRecord, BuiltInContentTypes.GoodTypeName);
            AddSorted(definitions, catalog.Needs, ToRecord, BuiltInContentTypes.NeedTypeName);
            AddSorted(definitions, catalog.Zones, ToRecord, BuiltInContentTypes.ZoneTypeName);
            AddSorted(definitions, catalog.BuildingTypes, ToRecord, BuiltInContentTypes.BuildingTypeTypeName);
            AddSorted(definitions, catalog.Recipes, ToRecord, BuiltInContentTypes.RecipeTypeName);
            AddSorted(definitions, catalog.NameLists, ToRecord, BuiltInContentTypes.NameListTypeName);

            return definitions;
        }

        private static void AddSorted<TAsset>(
            List<ContentAssetDefinition> definitions,
            IReadOnlyList<TAsset> assets,
            Func<TAsset, ContentDefinition> toRecord,
            string typeName)
            where TAsset : ContentAssetBase
        {
            foreach (TAsset asset in assets.Where(a => a != null).OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                definitions.Add(new ContentAssetDefinition(typeName, toRecord(asset), SourceId, asset.name));
            }
        }

        public static Good ToRecord(GoodAsset asset) =>
            new Good(asset.Id, asset.DisplayName, asset.Category, Money.FromCopper((double)asset.BasePriceCopper));

        public static Need ToRecord(NeedAsset asset) =>
            new Need(asset.Id, asset.DisplayName, asset.Kind, asset.HoursToEmpty, asset.MinimumClass, asset.BelieversOnly);

        public static Zone ToRecord(ZoneAsset asset) =>
            new Zone(asset.Id, asset.DisplayName, asset.ClassTier);

        public static BuildingType ToRecord(BuildingTypeAsset asset) =>
            new BuildingType(
                asset.Id,
                asset.DisplayName,
                asset.FootprintWidth,
                asset.FootprintDepth,
                ToGoodAmounts(asset.ConstructionCost),
                asset.WorkerSlots,
                asset.ManagerSlots,
                asset.TrainerSlots,
                asset.IsBasic,
                asset.IsGovernment,
                asset.AllowedZones.Where(z => z != null).Select(z => new ContentRef<Zone>(z.Id)).ToList(),
                asset.HouseholdsProvided,
                asset.HomeClass);

        public static Recipe ToRecord(RecipeAsset asset) =>
            new Recipe(
                asset.Id,
                asset.Workplace != null ? new ContentRef<BuildingType>(asset.Workplace.Id) : default,
                ToGoodAmounts(asset.Inputs),
                ToGoodAmounts(asset.Outputs),
                asset.DurationHours,
                asset.WorkerTier,
                asset.Skill.ToString(),
                asset.IsHardLabor,
                asset.SeasonsAllowed.ToList(),
                asset.GrowthTimeMonths);

        public static NameList ToRecord(NameListAsset asset) =>
            new NameList(asset.Id, asset.Kind, asset.Names.ToList());

        private static List<GoodAmount> ToGoodAmounts(IReadOnlyList<GoodAmountAsset> amounts)
        {
            return amounts
                .Where(a => a.Good != null)
                .Select(a => new GoodAmount(new ContentRef<Good>(a.Good.Id), a.Amount))
                .ToList();
        }
    }
}
