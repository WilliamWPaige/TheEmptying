namespace TheEmptying.Modules.People
{
    /// <summary>One need's state for one person: 100 is full/satisfied, 0 is empty.</summary>
    public sealed class PersonNeedState
    {
        public float Meter = 100f;
        public NeedThresholdState Threshold = NeedThresholdState.Normal;
    }
}
