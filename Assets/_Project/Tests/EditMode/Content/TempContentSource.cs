using System;
using System.IO;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>A throwaway source folder (mod.json + Data/) for content-loading tests. Deletes itself on Dispose.</summary>
    internal sealed class TempContentSource : IDisposable
    {
        public string FolderPath { get; }

        public TempContentSource(string sourceId, string version = "1.0.0")
        {
            FolderPath = Path.Combine(Path.GetTempPath(), "TheEmptyingContentTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(FolderPath, "Data"));
            File.WriteAllText(Path.Combine(FolderPath, "mod.json"), "{\"id\": \"" + sourceId + "\", \"name\": \"" + sourceId + "\", \"version\": \"" + version + "\", \"dependencies\": []}");
        }

        public void WriteDataFile(string fileName, string json)
        {
            File.WriteAllText(Path.Combine(FolderPath, "Data", fileName), json);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(FolderPath, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
