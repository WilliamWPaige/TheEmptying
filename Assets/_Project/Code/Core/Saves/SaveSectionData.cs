namespace TheEmptying.Core.Saves
{
    /// <summary>One section's decompressed payload. The section Id is the module Id for module sections.</summary>
    public sealed class SaveSectionData
    {
        public string Id { get; }
        public int Version { get; }
        public byte[] Data { get; }

        public SaveSectionData(string id, int version, byte[] data)
        {
            Id = id;
            Version = version;
            Data = data;
        }
    }
}
