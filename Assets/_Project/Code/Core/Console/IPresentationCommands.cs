namespace TheEmptying.Core.Console
{
    /// <summary>
    /// Implemented by the presentation layer for console commands that reach
    /// outside the simulation core (pause, speed, save, load), so Core stays
    /// free of Unity.
    /// </summary>
    public interface IPresentationCommands
    {
        bool IsPaused { get; }
        void SetPaused(bool paused);

        int Speed { get; }
        void SetSpeed(int speed);

        string Save(string name);
        string Load(string name);
        string ListSaves();

        string SetLogFilter(string mode);
    }
}
