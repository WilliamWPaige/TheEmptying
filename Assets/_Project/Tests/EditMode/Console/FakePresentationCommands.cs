using TheEmptying.Core.Console;

namespace TheEmptying.Tests.EditMode.Console
{
    /// <summary>A test double for IPresentationCommands; records what was asked of it.</summary>
    internal sealed class FakePresentationCommands : IPresentationCommands
    {
        public bool IsPaused { get; private set; }
        public int Speed { get; private set; } = 1;
        public string LastSavedName { get; private set; }
        public string LastLoadedName { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;
        public void SetSpeed(int speed) => Speed = speed;

        public string Save(string name)
        {
            LastSavedName = name;
            return $"Saved '{name}'.";
        }

        public string Load(string name)
        {
            LastLoadedName = name;
            return $"Loaded '{name}'.";
        }
    }
}
