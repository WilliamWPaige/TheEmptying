namespace TheEmptying.Core.Events
{
    /// <summary>
    /// Marker for exceptions that already identify their own source (which module,
    /// which call). EventBus rethrows these unwrapped instead of wrapping them
    /// again in a generic subscriber-failure exception.
    /// </summary>
    public interface IAttributedFailure
    {
    }
}
