using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>File-system operations for rule content files: discovering sources, listing their rule files, and loading/saving/creating/deleting/renaming them. Saving always goes through RuleJsonSerializer for a stable byte layout.</summary>
    public static class RuleFileRepository
    {
        public static List<RuleContentSource> DiscoverSources(string coreFolder, string modsFolder)
        {
            var sources = new List<RuleContentSource> { new RuleContentSource("core", coreFolder) };

            if (Directory.Exists(modsFolder))
            {
                foreach (string dir in Directory.GetDirectories(modsFolder).OrderBy(d => d, StringComparer.Ordinal))
                {
                    sources.Add(new RuleContentSource(Path.GetFileName(dir), dir));
                }
            }

            return sources;
        }

        public static List<RuleFileHandle> ListRuleFiles(RuleContentSource source)
        {
            var result = new List<RuleFileHandle>();
            if (!Directory.Exists(source.DataFolderPath))
            {
                return result;
            }

            foreach (string file in Directory.GetFiles(source.DataFolderPath, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                if (IsRuleFile(file))
                {
                    result.Add(new RuleFileHandle(source, file));
                }
            }

            return result;
        }

        private static bool IsRuleFile(string path)
        {
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                return (string)obj["type"] == BuiltInContentTypes.RuleTypeName;
            }
            catch
            {
                return false;
            }
        }

        public static List<RuleDefinition> LoadRules(RuleFileHandle handle)
        {
            return RuleJsonSerializer.DeserializeFile(File.ReadAllText(handle.FilePath));
        }

        public static void SaveRules(RuleFileHandle handle, IReadOnlyList<RuleDefinition> rules)
        {
            File.WriteAllText(handle.FilePath, RuleJsonSerializer.SerializeFile(rules));
        }

        public static RuleFileHandle CreateFile(RuleContentSource source, string fileName)
        {
            Directory.CreateDirectory(source.DataFolderPath);
            string path = Path.Combine(source.DataFolderPath, EnsureJsonExtension(fileName));
            File.WriteAllText(path, RuleJsonSerializer.SerializeFile(new List<RuleDefinition>()));
            return new RuleFileHandle(source, path);
        }

        public static void DeleteFile(RuleFileHandle handle)
        {
            File.Delete(handle.FilePath);
        }

        public static RuleFileHandle RenameFile(RuleFileHandle handle, string newFileName)
        {
            string newPath = Path.Combine(handle.Source.DataFolderPath, EnsureJsonExtension(newFileName));
            File.Move(handle.FilePath, newPath);
            return new RuleFileHandle(handle.Source, newPath);
        }

        private static string EnsureJsonExtension(string fileName)
        {
            return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".json";
        }
    }
}
