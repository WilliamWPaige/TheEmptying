namespace TheEmptying.Modules.Map
{
    /// <summary>
    /// TEMPORARY (R6b): what a chunk's random placement is. Presentation
    /// decides what each kind actually looks like (which content asset's
    /// Model to draw) - Core only knows there's a tree-kind or stone-kind
    /// placement at a cell. Replace once the real map design exists.
    /// </summary>
    public enum PlacementKind
    {
        Tree,
        Stone,
        GrubMound
    }
}
