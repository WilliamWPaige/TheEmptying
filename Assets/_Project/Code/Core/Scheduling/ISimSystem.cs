namespace TheEmptying.Core.Scheduling
{
    public interface ISimSystem
    {
        string Id { get; }

        void Update(long tick);
    }
}
