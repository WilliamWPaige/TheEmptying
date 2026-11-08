namespace TheEmptying.Core
{
    /// <summary>
    /// A sink for simulation log lines, implemented by the presentation layer
    /// so they reach the Unity console. Lets the core log without referencing
    /// UnityEngine.
    /// </summary>
    public interface ILogSink
    {
        void Log(string message);
    }
}
