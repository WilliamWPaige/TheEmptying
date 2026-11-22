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
        private const string DataFolder = "Assets/_Project/Data/";

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
                   && path.StartsWith(DataFolder, StringComparison.Ordinal)
                   && path.EndsWith(".asset", StringComparison.Ordinal);
        }

        /// <summary>Empty since R0 removed the Milestone 3 content types; syncs come back here as each type is rebuilt.</summary>
        public static void Refresh()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath);
            if (catalog == null)
            {
                return;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
