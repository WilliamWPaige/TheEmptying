using System;
using TheEmptying.Core.Events;

namespace TheEmptying.Core.Content
{
    /// <summary>The core's own content types: event definitions, module manifests and setting definitions.</summary>
    public static class BuiltInContentTypes
    {
        public const string EventDefinitionTypeName = "event_definition";
        public const string ModuleManifestTypeName = "module_manifest";
        public const string SettingDefinitionTypeName = "setting_definition";

        public static void RegisterAll(ContentTypeRegistry registry)
        {
            registry.Register(new ContentTypeDefinition(EventDefinitionTypeName, typeof(EventDefinitionContent), ValidateEventDefinition));
            registry.Register(new ContentTypeDefinition(ModuleManifestTypeName, typeof(ModuleManifestContent), ValidateModuleManifest));
            registry.Register(new ContentTypeDefinition(SettingDefinitionTypeName, typeof(SettingDefinition), ValidateSettingDefinition));
        }

        /// <summary>Registers every loaded event_definition content item into the EventDefinitionRegistry. The four calendar definitions stay registered in code.</summary>
        public static void RegisterEventDefinitionsFrom(ContentDatabase database, EventDefinitionRegistry registry)
        {
            foreach (ContentEntry entry in database.ListByType(EventDefinitionTypeName))
            {
                var content = (EventDefinitionContent)entry.Definition;
                var visibility = (EventVisibility)Enum.Parse(typeof(EventVisibility), content.Visibility);
                registry.Register(new EventDefinition(content.Id, visibility, content.ReportingScore, content.NotabilityScore, content.Description));
            }
        }

        /// <summary>Declares every loaded setting_definition content item into the SettingsRegistry, skipping any a module already declared itself.</summary>
        public static void DeclareSettingsFrom(ContentDatabase database, SettingsRegistry settings)
        {
            foreach (ContentEntry entry in database.ListByType(SettingDefinitionTypeName))
            {
                var definition = (SettingDefinition)entry.Definition;
                if (!settings.IsDeclared(definition.Id))
                {
                    settings.Declare(definition);
                }
            }
        }

        private static void ValidateEventDefinition(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (EventDefinitionContent)definition;

            if (!System.Enum.TryParse(content.Visibility, out EventVisibility _))
            {
                context.ReportProblem(content.Id, "visibility", $"'{content.Visibility}' is not a valid visibility (Public, Witnessed, Hidden).");
            }

            if (content.ReportingScore < 0f || content.ReportingScore > 10f)
            {
                context.ReportProblem(content.Id, "reportingScore", "must be between 0 and 10.");
            }

            if (content.NotabilityScore < 0f || content.NotabilityScore > 10f)
            {
                context.ReportProblem(content.Id, "notabilityScore", "must be between 0 and 10.");
            }
        }

        private static void ValidateModuleManifest(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (ModuleManifestContent)definition;

            if (content.Version < 0)
            {
                context.ReportProblem(content.Id, "version", "must not be negative.");
            }
        }

        private static void ValidateSettingDefinition(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (SettingDefinition)definition;

            if (content.Minimum > content.Maximum)
            {
                context.ReportProblem(content.Id, "minimum", "must not be greater than maximum.");
            }
            else if (content.Default < content.Minimum || content.Default > content.Maximum)
            {
                context.ReportProblem(content.Id, "default", "must be between minimum and maximum.");
            }
        }
    }
}
