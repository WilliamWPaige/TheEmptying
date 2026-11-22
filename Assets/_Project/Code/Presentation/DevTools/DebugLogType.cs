namespace TheEmptying.Presentation.DevTools
{
    /// <summary>The three kinds of Unity log message the Errors tab collects. Plain Debug.Log messages are never collected.</summary>
    public enum DebugLogType
    {
        Warning,
        Error,
        Exception
    }
}
