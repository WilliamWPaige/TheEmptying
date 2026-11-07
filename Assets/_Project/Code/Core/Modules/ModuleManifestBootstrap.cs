using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Modules
{
    /// <summary>
    /// Reads only module_manifest items from every source (core first, then mods
    /// in order; a later Id replaces an earlier one), before modules are
    /// ordered. This is the step that makes manifests truly data-driven: the
    /// real content load (declaration types, validation, everything else)
    /// happens later, after modules have declared.
    /// </summary>
    public static class ModuleManifestBootstrap
    {
        public static Dictionary<string, ModuleManifest> LoadManifests(IEnumerable<string> sourceFolders)
        {
            var manifests = new Dictionary<string, ModuleManifest>();

            foreach (string folder in sourceFolders ?? Enumerable.Empty<string>())
            {
                string dataFolder = Path.Combine(folder, "Data");
                if (!Directory.Exists(dataFolder))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(dataFolder, "*.json").OrderBy(f => f, System.StringComparer.Ordinal))
                {
                    ReadManifestsFromFile(file, manifests);
                }
            }

            return manifests;
        }

        private static void ReadManifestsFromFile(string filePath, Dictionary<string, ModuleManifest> manifests)
        {
            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch
            {
                return;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return;
            }

            if ((string)root["type"] != BuiltInContentTypes.ModuleManifestTypeName)
            {
                return;
            }

            if (!(root["items"] is JArray items))
            {
                return;
            }

            foreach (JToken itemToken in items)
            {
                if (!(itemToken is JObject itemJson))
                {
                    continue;
                }

                ModuleManifestContent content;
                try
                {
                    content = itemJson.ToObject<ModuleManifestContent>();
                }
                catch
                {
                    continue;
                }

                if (content == null || string.IsNullOrEmpty(content.Id))
                {
                    continue;
                }

                manifests[content.Id] = new ModuleManifest(content.Id, content.Version, content.Dependencies, content.Enabled);
            }
        }
    }
}
