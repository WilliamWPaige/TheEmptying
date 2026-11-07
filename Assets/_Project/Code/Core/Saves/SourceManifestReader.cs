using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Saves
{
    /// <summary>Reads each source folder's own Id and version from its mod.json, for save headers and load comparisons.</summary>
    public static class SourceManifestReader
    {
        public static List<SaveSourceInfo> ReadAll(IEnumerable<string> sourceFolders)
        {
            return (sourceFolders ?? Enumerable.Empty<string>()).Select(ReadOne).ToList();
        }

        private static SaveSourceInfo ReadOne(string folder)
        {
            string trimmedFolder = folder.TrimEnd('/', '\\');
            string sourceId = Path.GetFileName(trimmedFolder);
            string version = "0.0.0";

            string manifestPath = Path.Combine(folder, "mod.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    SourceManifest manifest = JsonConvert.DeserializeObject<SourceManifest>(File.ReadAllText(manifestPath));
                    if (!string.IsNullOrEmpty(manifest?.Id))
                    {
                        sourceId = manifest.Id;
                    }

                    if (!string.IsNullOrEmpty(manifest?.Version))
                    {
                        version = manifest.Version;
                    }
                }
                catch
                {
                    // A malformed mod.json is reported by ContentLoader during the actual content load.
                }
            }

            return new SaveSourceInfo { Id = sourceId, Version = version };
        }
    }
}
