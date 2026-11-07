namespace TheEmptying.Core.Saves
{
    /// <summary>Optional. A module implements this to save and load its own section. The section Id is the module Id.</summary>
    public interface ISaveableModule
    {
        int SectionVersion { get; }

        void Write(SaveWriter writer);

        void Read(SaveReader reader, int savedVersion);
    }
}
