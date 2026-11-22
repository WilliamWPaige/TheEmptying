using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R8: the shipped skills (skills.json), attributes (attributes.json) and names (names.json) load from data in the doc's order and groups.</summary>
    public class SkillsAndAttributesTests
    {
        private static ContentDatabase LoadRealContent()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            return simulation.Content;
        }

        private static readonly string[] ExpectedSkillIdsInOrder =
        {
            "skill.logging", "skill.quarrying", "skill.mining", "skill.farming", "skill.foraging", "skill.hunting", "skill.fishing", "skill.animal_handling",
            "skill.construction", "skill.masonry", "skill.carpentry", "skill.smithing", "skill.tailoring", "skill.crafting", "skill.cooking", "skill.brewing",
            "skill.hauling",
            "skill.medicine",
            "skill.leadership", "skill.administration", "skill.speech", "skill.trading", "skill.teaching", "skill.law",
            "skill.melee", "skill.ranged", "skill.tactics",
            "skill.scholarship", "skill.faith"
        };

        private static readonly string[] ExpectedAttributeIdsInOrder =
        {
            "attribute.strength", "attribute.endurance", "attribute.agility", "attribute.constitution",
            "attribute.intelligence", "attribute.willpower", "attribute.charisma", "attribute.perception"
        };

        [Test]
        public void Skills_LoadInTheDocsExactOrder()
        {
            ContentDatabase content = LoadRealContent();
            List<SkillDefinition> skills = BuiltInContentTypes.OrderedSkills(content);

            CollectionAssert.AreEqual(ExpectedSkillIdsInOrder, skills.Select(s => s.Id).ToList());
        }

        [Test]
        public void Skills_AreGroupedAsTheDocLists()
        {
            ContentDatabase content = LoadRealContent();
            List<SkillDefinition> skills = BuiltInContentTypes.OrderedSkills(content);

            var groupsInOrder = skills.Select(s => s.Group).Distinct().ToList();
            CollectionAssert.AreEqual(
                new[] { "Gathering", "Building and crafts", "Labor", "Care", "Government and social", "Military", "Knowledge and faith" },
                groupsInOrder);

            Assert.AreEqual(8, skills.Count(s => s.Group == "Gathering"));
            Assert.AreEqual(8, skills.Count(s => s.Group == "Building and crafts"));
            Assert.AreEqual(1, skills.Count(s => s.Group == "Labor"));
            Assert.AreEqual(1, skills.Count(s => s.Group == "Care"));
            Assert.AreEqual(6, skills.Count(s => s.Group == "Government and social"));
            Assert.AreEqual(3, skills.Count(s => s.Group == "Military"));
            Assert.AreEqual(2, skills.Count(s => s.Group == "Knowledge and faith"));
        }

        [Test]
        public void Attributes_LoadInTheDocsExactOrder()
        {
            ContentDatabase content = LoadRealContent();
            List<AttributeDefinition> attributes = BuiltInContentTypes.OrderedAttributes(content);

            CollectionAssert.AreEqual(ExpectedAttributeIdsInOrder, attributes.Select(a => a.Id).ToList());
        }

        [Test]
        public void NameLists_LoadAllThreeKinds_NonEmpty()
        {
            ContentDatabase content = LoadRealContent();

            Assert.IsNotEmpty(BuiltInContentTypes.NamesOfKind(content, NameListKind.MaleFirstName));
            Assert.IsNotEmpty(BuiltInContentTypes.NamesOfKind(content, NameListKind.FemaleFirstName));
            Assert.IsNotEmpty(BuiltInContentTypes.NamesOfKind(content, NameListKind.FamilyName));
        }
    }
}
