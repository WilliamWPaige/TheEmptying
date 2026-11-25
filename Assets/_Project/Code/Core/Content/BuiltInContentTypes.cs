using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Events;
using TheEmptying.Core.Rules;

namespace TheEmptying.Core.Content
{
    /// <summary>The core's own content types: event definitions, module manifests, setting definitions, rules, the asset tool's unit, resource, building and item types (R5), and skills, attributes and name lists (R8).</summary>
    public static class BuiltInContentTypes
    {
        public const string EventDefinitionTypeName = "event_definition";
        public const string ModuleManifestTypeName = "module_manifest";
        public const string SettingDefinitionTypeName = "setting_definition";
        public const string RuleTypeName = "rule";
        public const string UnitTypeName = "unit";
        public const string ResourceTypeName = "resource";
        public const string BuildingTypeName = "building";
        public const string ItemTypeName = "item";
        public const string SkillDefinitionTypeName = "skill_definition";
        public const string AttributeDefinitionTypeName = "attribute_definition";
        public const string NameListTypeName = "name_list";
        public const string StartingSiteTypeName = "starting_site";
        public const string ZoneTypeTypeName = "zone_type";
        public const string ActionDefinitionTypeName = "action_definition";

        public static void RegisterAll(ContentTypeRegistry registry)
        {
            registry.Register(new ContentTypeDefinition(EventDefinitionTypeName, typeof(EventDefinitionContent), ValidateEventDefinition));
            registry.Register(new ContentTypeDefinition(ModuleManifestTypeName, typeof(ModuleManifestContent), ValidateModuleManifest));
            registry.Register(new ContentTypeDefinition(SettingDefinitionTypeName, typeof(SettingDefinition), ValidateSettingDefinition));
            registry.Register(new ContentTypeDefinition(RuleTypeName, typeof(RuleDefinition)));
            registry.Register(new ContentTypeDefinition(UnitTypeName, typeof(UnitContent), ValidateUnit));
            registry.Register(new ContentTypeDefinition(ResourceTypeName, typeof(ResourceContent), ValidateResource));
            registry.Register(new ContentTypeDefinition(BuildingTypeName, typeof(BuildingContent), ValidateBuilding));
            registry.Register(new ContentTypeDefinition(ItemTypeName, typeof(ItemContent), ValidateItem));
            registry.Register(new ContentTypeDefinition(SkillDefinitionTypeName, typeof(SkillDefinition), ValidateSkillDefinition));
            registry.Register(new ContentTypeDefinition(AttributeDefinitionTypeName, typeof(AttributeDefinition), ValidateAttributeDefinition));
            registry.Register(new ContentTypeDefinition(NameListTypeName, typeof(NameListDefinition), ValidateNameList));
            registry.Register(new ContentTypeDefinition(StartingSiteTypeName, typeof(StartingSiteDefinition), ValidateStartingSite));
            registry.Register(new ContentTypeDefinition(ZoneTypeTypeName, typeof(ZoneTypeDefinition), ValidateZoneType));
            registry.Register(new ContentTypeDefinition(ActionDefinitionTypeName, typeof(ActionDefinitionContent), ValidateActionDefinition));
        }

        /// <summary>Every loaded zone type, in Id order.</summary>
        public static List<ZoneTypeDefinition> ZoneTypes(ContentDatabase database)
        {
            return database.ListByType(ZoneTypeTypeName).Select(e => (ZoneTypeDefinition)e.Definition).ToList();
        }

        /// <summary>Every loaded skill, in the order the data lists them (ListByType sorts by Id, so this re-sorts by the Order field instead).</summary>
        public static List<SkillDefinition> OrderedSkills(ContentDatabase database)
        {
            return database.ListByType(SkillDefinitionTypeName)
                .Select(e => (SkillDefinition)e.Definition)
                .OrderBy(s => s.Order)
                .ToList();
        }

        /// <summary>Every loaded attribute, in the order the data lists them.</summary>
        public static List<AttributeDefinition> OrderedAttributes(ContentDatabase database)
        {
            return database.ListByType(AttributeDefinitionTypeName)
                .Select(e => (AttributeDefinition)e.Definition)
                .OrderBy(a => a.Order)
                .ToList();
        }

        /// <summary>The names list for one kind (male first names, female first names, or family names); empty if none is loaded.</summary>
        public static List<string> NamesOfKind(ContentDatabase database, NameListKind kind)
        {
            foreach (ContentEntry entry in database.ListByType(NameListTypeName))
            {
                var content = (NameListDefinition)entry.Definition;
                if (System.Enum.TryParse(content.Kind, out NameListKind entryKind) && entryKind == kind)
                {
                    return content.Names;
                }
            }

            return new List<string>();
        }

        /// <summary>Every loaded starting site. For now there is exactly one; later there are 3 or 4.</summary>
        public static List<StartingSiteDefinition> StartingSites(ContentDatabase database)
        {
            return database.ListByType(StartingSiteTypeName)
                .Select(e => (StartingSiteDefinition)e.Definition)
                .ToList();
        }

        /// <summary>Registers every loaded event_definition content item into the EventDefinitionRegistry. The four calendar definitions stay registered in code.</summary>
        public static void RegisterEventDefinitionsFrom(ContentDatabase database, EventDefinitionRegistry registry)
        {
            foreach (ContentEntry entry in database.ListByType(EventDefinitionTypeName))
            {
                var content = (EventDefinitionContent)entry.Definition;
                var visibility = (EventVisibility)System.Enum.Parse(typeof(EventVisibility), content.Visibility);
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

            if (string.IsNullOrEmpty(content.Label))
            {
                context.ReportProblem(content.Id, "label", "must not be empty.");
            }
        }

        /// <summary>
        /// A missing Model can't be checked here (it's a UnityEngine prefab
        /// reference, not something the plain record holds) - that's caught
        /// by ContentAssetConverter instead. Everything else these four
        /// types validate (a wrong Id prefix, an empty display name, a
        /// footprint below 1) goes through here, same as every other
        /// content type; a missing or duplicate Id is already caught
        /// generically by ContentLoader itself.
        /// </summary>
        private static void CheckIdPrefix(string id, string prefix, ContentValidationContext context)
        {
            if (!string.IsNullOrEmpty(id) && !id.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                context.ReportProblem(id, "id", $"must start with '{prefix}'.");
            }
        }

        private static void ValidateUnit(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (UnitContent)definition;
            CheckIdPrefix(content.Id, "unit.", context);

            if (string.IsNullOrEmpty(content.DisplayName))
            {
                context.ReportProblem(content.Id, "displayName", "must not be empty.");
            }
        }

        private static void ValidateItem(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (ItemContent)definition;
            CheckIdPrefix(content.Id, "item.", context);

            if (string.IsNullOrEmpty(content.DisplayName))
            {
                context.ReportProblem(content.Id, "displayName", "must not be empty.");
            }

            if (content.Size < 1 || content.Size > 10)
            {
                context.ReportProblem(content.Id, "size", "must be between 1 and 10.");
            }

            if (content.SpoilTimeMinutes < 0)
            {
                context.ReportProblem(content.Id, "spoilTimeMinutes", "must not be negative.");
            }
        }

        private static void ValidateResource(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (ResourceContent)definition;
            CheckIdPrefix(content.Id, "resource.", context);
            ValidateFootprint(content.Id, content.DisplayName, content.FootprintWidth, content.FootprintDepth, context);

            if (content.HarvestTimeUnskilledMinutes < 0 || content.HarvestTimeSkilledMinutes < 0 || content.HarvestTimeMasterMinutes < 0)
            {
                context.ReportProblem(content.Id, "harvestTime", "must not be negative.");
            }

            if (content.MaxWorkers < 1)
            {
                context.ReportProblem(content.Id, "maxWorkers", "must be at least 1.");
            }
        }

        private static void ValidateBuilding(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (BuildingContent)definition;
            CheckIdPrefix(content.Id, "building.", context);
            ValidateFootprint(content.Id, content.DisplayName, content.FootprintWidth, content.FootprintDepth, context);
        }

        /// <summary>R14: one kind of action a person can perform, for the Discoveries window's Actions tab.</summary>
        private static void ValidateActionDefinition(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (ActionDefinitionContent)definition;
            CheckIdPrefix(content.Id, "action.", context);

            if (string.IsNullOrEmpty(content.DisplayName))
            {
                context.ReportProblem(content.Id, "displayName", "must not be empty.");
            }

            if (string.IsNullOrEmpty(content.Description))
            {
                context.ReportProblem(content.Id, "description", "must not be empty.");
            }
        }

        private static void ValidateFootprint(string id, string displayName, int footprintWidth, int footprintDepth, ContentValidationContext context)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                context.ReportProblem(id, "displayName", "must not be empty.");
            }

            if (footprintWidth < 1)
            {
                context.ReportProblem(id, "footprintWidth", "must be at least 1.");
            }

            if (footprintDepth < 1)
            {
                context.ReportProblem(id, "footprintDepth", "must be at least 1.");
            }
        }

        private static void ValidateSkillDefinition(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (SkillDefinition)definition;
            CheckIdPrefix(content.Id, "skill.", context);

            if (string.IsNullOrEmpty(content.DisplayName))
            {
                context.ReportProblem(content.Id, "displayName", "must not be empty.");
            }

            if (string.IsNullOrEmpty(content.Group))
            {
                context.ReportProblem(content.Id, "group", "must not be empty.");
            }
        }

        private static void ValidateAttributeDefinition(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (AttributeDefinition)definition;
            CheckIdPrefix(content.Id, "attribute.", context);

            if (string.IsNullOrEmpty(content.DisplayName))
            {
                context.ReportProblem(content.Id, "displayName", "must not be empty.");
            }
        }

        private static void ValidateStartingSite(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (StartingSiteDefinition)definition;
            CheckIdPrefix(content.Id, "site.", context);

            if (content.FractionX < 0.0 || content.FractionX > 1.0)
            {
                context.ReportProblem(content.Id, "fractionX", "must be between 0 and 1.");
            }

            if (content.FractionZ < 0.0 || content.FractionZ > 1.0)
            {
                context.ReportProblem(content.Id, "fractionZ", "must be between 0 and 1.");
            }
        }

        private static void ValidateZoneType(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (ZoneTypeDefinition)definition;
            CheckIdPrefix(content.Id, "zone.", context);

            if (string.IsNullOrEmpty(content.DisplayName))
            {
                context.ReportProblem(content.Id, "displayName", "must not be empty.");
            }

            if (string.IsNullOrEmpty(content.ColorHex))
            {
                context.ReportProblem(content.Id, "colorHex", "must not be empty.");
            }

            if (string.IsNullOrEmpty(content.Description))
            {
                context.ReportProblem(content.Id, "description", "must not be empty.");
            }
        }

        private static void ValidateNameList(ContentDefinition definition, ContentValidationContext context)
        {
            var content = (NameListDefinition)definition;
            CheckIdPrefix(content.Id, "name.", context);

            if (!System.Enum.TryParse(content.Kind, out NameListKind _))
            {
                context.ReportProblem(content.Id, "kind", $"'{content.Kind}' is not a valid kind (MaleFirstName, FemaleFirstName, FamilyName).");
            }

            if (content.Names == null || content.Names.Count == 0)
            {
                context.ReportProblem(content.Id, "names", "must not be empty.");
            }
        }
    }
}
