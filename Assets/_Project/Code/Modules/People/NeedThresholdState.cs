namespace TheEmptying.Modules.People
{
    /// <summary>Where a need's meter sits relative to the low and critical settings, so events fire only on a crossing, never on ordinary drain.</summary>
    public enum NeedThresholdState
    {
        Normal,
        Low,
        Critical
    }
}
