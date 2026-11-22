using System.Collections.Generic;
using TheEmptying.Core.Content;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Copies each ScriptableObject content asset into its plain record, in
    /// ordinal Id order, ready for ContentLoader to add to the database
    /// alongside the JSON sources. The simulation only ever sees the records.
    ///
    /// Empty since R0 removed the Milestone 3 content types; conversions come
    /// back here as each type is rebuilt.
    /// </summary>
    public static class ContentAssetConverter
    {
        public static List<ContentAssetDefinition> BuildDefinitions(ContentCatalog catalog)
        {
            return new List<ContentAssetDefinition>();
        }
    }
}
