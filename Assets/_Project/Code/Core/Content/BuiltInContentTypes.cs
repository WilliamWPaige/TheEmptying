using System;
using TheEmptying.Core.Events;
using TheEmptying.Core.Rules;

namespace TheEmptying.Core.Content
{
    /// <summary>The core's own content types: event definitions, module manifests, setting definitions, rules, and the gray-box town's goods, needs, zones, building types and recipes.</summary>
    public static class BuiltInContentTypes
    {
        public const string EventDefinitionTypeName = "event_definition";
        public const string ModuleManifestTypeName = "module_manifest";
        public const string SettingDefinitionTypeName = "setting_definition";
        public const string RuleTypeName = "rule";
        public const string GoodTypeName = "good";
        public const string NeedTypeName = "need";
        public const string ZoneTypeName = "zone";
        public const string BuildingTypeTypeName = "building_type";
        public const string RecipeTypeName = "recipe";
        public const string NameListTypeName = "name_list";

        public static void RegisterAll(ContentTypeRegistry registry)
        {
            registry.Register(new ContentTypeDefinition(EventDefinitionTypeName, typeof(EventDefinitionContent), ValidateEventDefinition));
            registry.Register(new ContentTypeDefinition(ModuleManifestTypeName, typeof(ModuleManifestContent), ValidateModuleManifest));
            registry.Register(new ContentTypeDefinition(SettingDefinitionTypeName, typeof(SettingDefinition), ValidateSettingDefinition));
            registry.Register(new ContentTypeDefinition(RuleTypeName, typeof(RuleDefinition)));
            registry.Register(new ContentTypeDefinition(GoodTypeName, typeof(Good), ValidateGood));
            registry.Register(new ContentTypeDefinition(NeedTypeName, typeof(Need), ValidateNeed));
            registry.Register(new ContentTypeDefinition(ZoneTypeName, typeof(Zone), ValidateZone));
            registry.Register(new ContentTypeDefinition(BuildingTypeTypeName, typeof(BuildingType), ValidateBuildingType));
            registry.Register(new ContentTypeDefinition(RecipeTypeName, typeof(Recipe), ValidateRecipe));
            registry.Register(new ContentTypeDefinition(NameListTypeName, typeof(NameList), ValidateNameList));
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

        private static void CheckIdPrefix(string id, string prefix, ContentValidationContext context)
        {
            if (!string.IsNullOrEmpty(id) && !id.StartsWith(prefix, StringComparison.Ordinal))
            {
                context.ReportProblem(id, "id", $"must start with '{prefix}'.");
            }
        }

        private static void ValidateGood(ContentDefinition definition, ContentValidationContext context)
        {
            var good = (Good)definition;
            CheckIdPrefix(good.Id, "good.", context);

            if (good.BasePrice.HundredthsOfShil < 0)
            {
                context.ReportProblem(good.Id, "basePrice", "must not be negative.");
            }
        }

        private static void ValidateNeed(ContentDefinition definition, ContentValidationContext context)
        {
            var need = (Need)definition;
            CheckIdPrefix(need.Id, "need.", context);

            if (need.Kind == NeedKind.Meter && need.HoursToEmpty <= 0f)
            {
                context.ReportProblem(need.Id, "hoursToEmpty", "must be greater than 0 for a meter need.");
            }
        }

        private static void ValidateZone(ContentDefinition definition, ContentValidationContext context)
        {
            var zone = (Zone)definition;
            CheckIdPrefix(zone.Id, "zone.", context);
        }

        private static void ValidateBuildingType(ContentDefinition definition, ContentValidationContext context)
        {
            var buildingType = (BuildingType)definition;
            CheckIdPrefix(buildingType.Id, "building.", context);

            if (!buildingType.IsGovernment && buildingType.AllowedZones.Count == 0)
            {
                context.ReportProblem(buildingType.Id, "allowedZones", "must allow at least one zone, unless the building type is marked government.");
            }

            foreach (GoodAmount cost in buildingType.ConstructionCost)
            {
                if (cost.Amount < 0)
                {
                    context.ReportProblem(buildingType.Id, "constructionCost", $"Amount for '{cost.Good.Id}' must not be negative.");
                }

                if (!cost.Good.IsEmpty && !context.Database.TryGet(GoodTypeName, cost.Good.Id, out _))
                {
                    context.ReportProblem(buildingType.Id, "constructionCost", $"References '{cost.Good.Id}', which does not exist.");
                }
            }

            foreach (ContentRef<Zone> zoneRef in buildingType.AllowedZones)
            {
                if (!zoneRef.IsEmpty && !context.Database.TryGet(ZoneTypeName, zoneRef.Id, out _))
                {
                    context.ReportProblem(buildingType.Id, "allowedZones", $"References '{zoneRef.Id}', which does not exist.");
                }
            }
        }

        private static void ValidateRecipe(ContentDefinition definition, ContentValidationContext context)
        {
            var recipe = (Recipe)definition;
            CheckIdPrefix(recipe.Id, "recipe.", context);

            if (recipe.DurationHours <= 0f)
            {
                context.ReportProblem(recipe.Id, "durationHours", "must be greater than 0.");
            }

            if (string.IsNullOrEmpty(recipe.Skill))
            {
                context.ReportProblem(recipe.Id, "skill", "must not be empty.");
            }

            foreach (GoodAmount input in recipe.Inputs)
            {
                if (input.Amount < 0)
                {
                    context.ReportProblem(recipe.Id, "inputs", $"Amount for '{input.Good.Id}' must not be negative.");
                }

                if (!input.Good.IsEmpty && !context.Database.TryGet(GoodTypeName, input.Good.Id, out _))
                {
                    context.ReportProblem(recipe.Id, "inputs", $"References '{input.Good.Id}', which does not exist.");
                }
            }

            foreach (GoodAmount output in recipe.Outputs)
            {
                if (output.Amount < 0)
                {
                    context.ReportProblem(recipe.Id, "outputs", $"Amount for '{output.Good.Id}' must not be negative.");
                }

                if (!output.Good.IsEmpty && !context.Database.TryGet(GoodTypeName, output.Good.Id, out _))
                {
                    context.ReportProblem(recipe.Id, "outputs", $"References '{output.Good.Id}', which does not exist.");
                }
            }
        }

        private static void ValidateNameList(ContentDefinition definition, ContentValidationContext context)
        {
            var nameList = (NameList)definition;
            CheckIdPrefix(nameList.Id, "name.", context);

            if (nameList.Names.Count == 0)
            {
                context.ReportProblem(nameList.Id, "names", "must not be empty.");
            }
        }
    }
}
