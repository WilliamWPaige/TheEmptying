using System;
using System.Linq;
using TheEmptying.Presentation.Content;
using UnityEditor;

namespace TheEmptying.Editor.Content
{
    /// <summary>Keeps the shipped ContentCatalog asset's lists in sync with the project's content assets whenever one is added, removed or moved.</summary>
    internal sealed class ContentCatalogUpdater : AssetPostprocessor
    {
        private const string CatalogPath = "Assets/_Project/Data/Resources/ContentCatalog.asset";
        private static readonly string[] WatchedFolders = { "Assets/_Project/Data/", "Assets/_Project/Content/" };

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool touchedContent = importedAssets
                .Concat(deletedAssets)
                .Concat(movedAssets)
                .Concat(movedFromAssetPaths)
                .Any(IsRelevantPath);

            if (touchedContent)
            {
                Refresh();
            }
        }

        private static bool IsRelevantPath(string path)
        {
            return path != CatalogPath
                   && path.EndsWith(".asset", StringComparison.Ordinal)
                   && WatchedFolders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal));
        }

        public static void Refresh()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath);
            if (catalog == null)
            {
                return;
            }

            catalog.SetContentsForEditor(
                ContentAssetDuplicateCheck.FindAllOfSameType<UnitAsset>(),
                ContentAssetDuplicateCheck.FindAllOfSameType<ResourceAsset>(),
                ContentAssetDuplicateCheck.FindAllOfSameType<BuildingAsset>(),
                ContentAssetDuplicateCheck.FindAllOfSameType<ItemAsset>());

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
