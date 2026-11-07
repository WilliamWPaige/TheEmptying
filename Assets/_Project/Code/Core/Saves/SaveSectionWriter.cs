using System.IO;

namespace TheEmptying.Core.Saves
{
    public static class SaveSectionWriter
    {
        public static byte[] WriteModule(ISaveableModule module)
        {
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            module.Write(writer);
            return stream.ToArray();
        }
    }
}
