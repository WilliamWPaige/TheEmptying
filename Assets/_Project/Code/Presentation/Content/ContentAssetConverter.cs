using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Copies each ScriptableObject content asset into its plain record, in
    /// ordinal Id order, ready for ContentLoader to add to the database
    /// alongside the JSON sources. The simulation only ever sees the
    /// records. A missing Model can't be checked there (it's a UnityEngine
    /// prefab reference, not something the plain record holds), so it's
    /// caught and logged here instead, and that one asset is skipped;
    /// every other problem (a missing or duplicate Id, a wrong prefix, an
    /// empty display name, a footprint below 1) goes through ContentLoader's
    /// own validation, same as every other content type.
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

            AddSorted(definitions, catalog.Units, BuiltInContentTypes.UnitTypeName, ToRecord);
            AddSorted(definitions, catalog.ResourceNodes, BuiltInContentTypes.ResourceTypeName, ToRecord);
            AddSorted(definitions, catalog.Buildings, BuiltInContentTypes.BuildingTypeName, ToRecord);
            AddSorted(definitions, catalog.Items, BuiltInContentTypes.ItemTypeName, ToRecord);

            return definitions;
        }

        private static void AddSorted<TAsset>(
            List<ContentAssetDefinition> definitions,
            IReadOnlyList<TAsset> assets,
            string typeName,
            Func<TAsset, ContentDefinition> toRecord)
            where TAsset : ContentAssetBase
        {
            foreach (TAsset asset in assets.Where(a => a != null).OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                if (asset.Model == null)
                {
                    Debug.LogError($"[TheEmptying] Content: {asset.ContentTypeName} '{asset.name}' has no Model; it will not be loaded.", asset);
                    continue;
                }

                definitions.Add(new ContentAssetDefinition(typeName, toRecord(asset), SourceId, asset.name));
            }
        }

        private static UnitContent ToRecord(UnitAsset asset) => new UnitContent { Id = asset.Id, DisplayName = asset.DisplayName };

        private static ItemContent ToRecord(ItemAsset asset) => new ItemContent { Id = asset.Id, DisplayName = asset.DisplayName };

        private static ResourceContent ToRecord(ResourceAsset asset) => new ResourceContent
        {
            Id = asset.Id,
            DisplayName = asset.DisplayName,
            FootprintWidth = asset.FootprintWidth,
            FootprintDepth = asset.FootprintDepth
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
