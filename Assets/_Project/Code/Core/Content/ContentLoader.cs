using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Modules;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// Loads content from an ordered list of source folders (core first, then
    /// mods in the player's order), validates it, and returns the database plus
    /// a report. Each source folder has an optional mod.json manifest and a Data
    /// folder of JSON files, each holding a type name and a list of items.
    /// </summary>
    public sealed class ContentLoader
    {
        private sealed class ContentFile
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("items")]
            public List<JObject> Items { get; set; }
        }

        private readonly ContentTypeRegistry _types;
        private readonly ErrorMode _errorMode;

        public ContentLoader(ContentTypeRegistry types, ErrorMode errorMode)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _errorMode = errorMode;
        }

        public (ContentDatabase Database, ContentLoadReport Report) Load(IEnumerable<string> sourceFolders, IEnumerable<string> requiredModuleIds = null)
        {
            var database = new ContentDatabase();
            var report = new ContentLoadReport();

            foreach (string folder in sourceFolders ?? Enumerable.Empty<string>())
            {
                LoadSource(folder, database, report);
            }

            ResolveReferences(database, report);
            CheckRequiredModuleManifests(database, report, requiredModuleIds);

            report.Overrides.AddRange(database.Overrides);

            if (_errorMode == ErrorMode.Development && report.HasErrors)
            {
                string message = string.Join("\n", report.Problems.Select(p => p.ToString()));
                throw new ContentValidationException($"Content validation failed:\n{message}", report);
            }

            return (database, report);
        }

        private void LoadSource(string folder, ContentDatabase database, ContentLoadReport report)
        {
            string manifestPath = Path.Combine(folder, "mod.json");
            string trimmedFolder = folder.TrimEnd('/', '\\');
            string sourceId = Path.GetFileName(trimmedFolder);

            if (File.Exists(manifestPath))
            {
                try
                {
                    SourceManifest manifest = JsonConvert.DeserializeObject<SourceManifest>(File.ReadAllText(manifestPath));
                    if (!string.IsNullOrEmpty(manifest?.Id))
                    {
                        sourceId = manifest.Id;
                    }
                }
                catch (Exception ex)
                {
                    report.Problems.Add(new ContentValidationProblem(sourceId, "mod.json", null, null, $"Malformed source manifest: {ex.Message}"));
                }
            }

            string dataFolder = Path.Combine(folder, "Data");
            if (!Directory.Exists(dataFolder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(dataFolder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                LoadFile(file, sourceId, database, report);
            }
        }

        private void LoadFile(string filePath, string sourceId, ContentDatabase database, ContentLoadReport report)
        {
            string fileName = Path.GetFileName(filePath);

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                report.Problems.Add(new ContentValidationProblem(sourceId, fileName, null, null, $"Could not read file: {ex.Message}"));
                return;
            }

            ContentFile contentFile;
            try
            {
                contentFile = JsonConvert.DeserializeObject<ContentFile>(json);
            }
            catch (Exception ex)
            {
                report.Problems.Add(new ContentValidationProblem(sourceId, fileName, null, null, $"Malformed file: {ex.Message}"));
                return;
            }

            if (contentFile == null || string.IsNullOrEmpty(contentFile.Type))
            {
                report.Problems.Add(new ContentValidationProblem(sourceId, fileName, null, "type", "Missing or empty 'type'."));
                return;
            }

            if (!_types.TryGet(contentFile.Type, out ContentTypeDefinition typeDef))
            {
                report.Problems.Add(new ContentValidationProblem(sourceId, fileName, null, "type", $"Unknown content type '{contentFile.Type}'."));
                return;
            }

            if (contentFile.Items == null)
            {
                return;
            }

            foreach (JObject itemJson in contentFile.Items)
            {
                LoadItem(itemJson, typeDef, sourceId, fileName, database, report);
            }
        }

        private void LoadItem(JObject itemJson, ContentTypeDefinition typeDef, string sourceId, string fileName, ContentDatabase database, ContentLoadReport report)
        {
            ContentDefinition definition;
            try
            {
                definition = (ContentDefinition)itemJson.ToObject(typeDef.ClrType);
            }
            catch (Exception ex)
            {
                report.Problems.Add(new ContentValidationProblem(sourceId, fileName, null, null, $"Could not parse item: {ex.Message}"));
                return;
            }

            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                report.Problems.Add(new ContentValidationProblem(sourceId, fileName, null, "id", "Item is missing an Id."));
                return;
            }

            int problemCountBefore = report.Problems.Count;
            var context = new ContentValidationContext(database, sourceId, fileName, report.Problems);
            typeDef.Validator?.Invoke(definition, context);
            bool hadProblem = report.Problems.Count > problemCountBefore;

            if (hadProblem && _errorMode == ErrorMode.Release)
            {
                return;
            }

            database.Set(typeDef.TypeName, definition, sourceId, fileName);
        }

        private void ResolveReferences(ContentDatabase database, ContentLoadReport report)
        {
            foreach (string typeName in _types.TypeNames)
            {
                foreach (ContentEntry entry in database.ListByType(typeName))
                {
                    ContentDefinition definition = entry.Definition;

                    foreach (PropertyInfo prop in definition.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!prop.PropertyType.IsGenericType || prop.PropertyType.GetGenericTypeDefinition() != typeof(ContentRef<>))
                        {
                            continue;
                        }

                        object refValue = prop.GetValue(definition);
                        var refId = (string)prop.PropertyType.GetProperty("Id").GetValue(refValue);
                        if (string.IsNullOrEmpty(refId))
                        {
                            continue;
                        }

                        Type targetClrType = prop.PropertyType.GetGenericArguments()[0];
                        bool resolved = _types.TryGetTypeNameForClrType(targetClrType, out string targetTypeName)
                                         && database.TryGet(targetTypeName, refId, out _);

                        if (!resolved)
                        {
                            report.Problems.Add(new ContentValidationProblem(entry.SourceId, entry.SourceFile, definition.Id, prop.Name, $"References '{refId}', which does not exist."));
                        }
                    }
                }
            }
        }

        private void CheckRequiredModuleManifests(ContentDatabase database, ContentLoadReport report, IEnumerable<string> requiredModuleIds)
        {
            if (requiredModuleIds == null)
            {
                return;
            }

            foreach (string moduleId in requiredModuleIds)
            {
                if (!database.TryGet(BuiltInContentTypes.ModuleManifestTypeName, moduleId, out _))
                {
                    report.Problems.Add(new ContentValidationProblem(null, null, moduleId, "manifest", $"Module '{moduleId}' has no manifest in the data."));
                }
            }
        }
    }
}
