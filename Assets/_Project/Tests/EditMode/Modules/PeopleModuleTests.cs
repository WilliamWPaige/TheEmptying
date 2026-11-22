using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>
    /// R7: new-game founder placement (free cells, spaced apart). R8:
    /// identity and stats - deterministic by world seed, in range per
    /// role, exactly one Monarch and one Advisor, and the save/load round
    /// trip - all against the real shipped content.
    /// </summary>
    public class PeopleModuleTests
    {
        private static readonly string[] CoreTradeSkillIds =
        {
            "skill.logging", "skill.quarrying", "skill.farming", "skill.foraging", "skill.hauling", "skill.construction"
        };

        private static (Simulation Simulation, MapModule Map, PeopleModule People) BuildNewGame(TempCoreContentCopy core, ulong seed = 1)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, (MapModule)modules.First(m => m is MapModule), (PeopleModule)modules.First(m => m is PeopleModule));
        }

        [Test]
        public void NewGame_SpawnsTheConfiguredFounderCount()
        {
            using var core = new TempCoreContentCopy();
            (_, _, PeopleModule people) = BuildNewGame(core);

            Assert.AreEqual(9, people.People.Count());
        }

        [Test]
        public void NewGame_EveryFounder_IsOnAFreeCell()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, PeopleModule people) = BuildNewGame(core);

            foreach (KeyValuePair<EntityId, PersonState> row in people.People)
            {
                CellCoord cell = map.WorldToCell(row.Value.WorldX, row.Value.WorldZ);
                Assert.IsTrue(map.IsCellFree(cell), $"Founder at {cell} is not on a free cell (a tree, stone or mound covers it).");
            }
        }

        [Test]
        public void NewGame_NoTwoFoundersShareACell()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, PeopleModule people) = BuildNewGame(core);

            List<CellCoord> cells = people.People.Select(row => map.WorldToCell(row.Value.WorldX, row.Value.WorldZ)).ToList();

            for (int i = 0; i < cells.Count; i++)
            {
                for (int j = i + 1; j < cells.Count; j++)
                {
                    Assert.AreNotEqual(cells[i], cells[j], "Two founders share the same cell.");
                }
            }
        }

        [Test]
        public void NewGame_HasExactlyOneMonarchAndOneAdvisor()
        {
            using var core = new TempCoreContentCopy();
            (_, _, PeopleModule people) = BuildNewGame(core);

            List<PersonState> all = people.People.Select(r => r.Value).ToList();

            Assert.AreEqual(1, all.Count(p => p.Role == PersonRole.Monarch));
            Assert.AreEqual(1, all.Count(p => p.Role == PersonRole.Advisor));
            Assert.AreEqual(all.Count - 2, all.Count(p => p.Role == PersonRole.Founder));
        }

        [Test]
        public void NewGame_EveryRoll_IsInsideItsRoleRange()
        {
            using var core = new TempCoreContentCopy();
            (_, _, PeopleModule people) = BuildNewGame(core);

            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                bool isMonarch = person.Role == PersonRole.Monarch;

                Assert.That(person.AgeYears, isMonarch ? Is.InRange(25, 50) : Is.InRange(20, 40), $"{person.Role} age out of range.");

                foreach (KeyValuePair<string, int> kvp in person.Attributes)
                {
                    Assert.That(kvp.Value, isMonarch ? Is.InRange(6, 14) : Is.InRange(10, 16), $"{person.Role} attribute {kvp.Key} out of range.");
                }

                if (isMonarch)
                {
                    foreach (KeyValuePair<string, int> kvp in person.Skills)
                    {
                        bool isLeadershipOrSpeech = kvp.Key == "skill.leadership" || kvp.Key == "skill.speech";
                        Assert.That(kvp.Value, isLeadershipOrSpeech ? Is.InRange(8, 14) : Is.InRange(1, 6), $"Monarch skill {kvp.Key} out of range.");
                    }

                    Assert.IsEmpty(person.SpecialtySkillId);
                }
                else
                {
                    Assert.IsNotEmpty(person.SpecialtySkillId);
                    Assert.IsFalse(CoreTradeSkillIds.Contains(person.SpecialtySkillId), "The specialty must not be a core trade.");

                    foreach (KeyValuePair<string, int> kvp in person.Skills)
                    {
                        if (CoreTradeSkillIds.Contains(kvp.Key))
                        {
                            Assert.That(kvp.Value, Is.InRange(14, 18), $"{person.Role} core trade {kvp.Key} out of range.");
                        }
                        else if (kvp.Key == person.SpecialtySkillId)
                        {
                            Assert.That(kvp.Value, Is.InRange(19, 20), $"{person.Role} specialty {kvp.Key} out of range.");
                        }
                        else
                        {
                            Assert.That(kvp.Value, Is.InRange(5, 12), $"{person.Role} other skill {kvp.Key} out of range.");
                        }
                    }
                }
            }
        }

        [Test]
        public void NewGame_SameSeed_GivesTheSamePeopleAndStats()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (_, _, PeopleModule peopleA) = BuildNewGame(coreA, 42);
            (_, _, PeopleModule peopleB) = BuildNewGame(coreB, 42);

            List<PersonState> listA = peopleA.People.Select(r => r.Value).ToList();
            List<PersonState> listB = peopleB.People.Select(r => r.Value).ToList();

            Assert.AreEqual(listA.Count, listB.Count);
            for (int i = 0; i < listA.Count; i++)
            {
                AssertSameState(listA[i], listB[i]);
            }
        }

        [Test]
        public void SaveAndLoad_StatsSurviveTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, PeopleModule peopleBefore) = BuildNewGame(core);

            List<(EntityId Id, PersonState State)> before = peopleBefore.People.Select(r => (r.Key, r.Value)).ToList();
            Assert.Greater(before.Count, 0);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = before.Count }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            (Simulation loaded, LoadReport _) = GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var peopleAfter = (PeopleModule)loadedModules.First(m => m is PeopleModule);

            List<(EntityId Id, PersonState State)> after = peopleAfter.People.Select(r => (r.Key, r.Value)).ToList();

            Assert.AreEqual(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].Id, after[i].Id);
                AssertSameState(before[i].State, after[i].State);
            }
        }

        private static void AssertSameState(PersonState a, PersonState b)
        {
            Assert.AreEqual(a.UnitContentId, b.UnitContentId);
            Assert.AreEqual(a.WorldX, b.WorldX);
            Assert.AreEqual(a.WorldZ, b.WorldZ);
            Assert.AreEqual(a.Name, b.Name);
            Assert.AreEqual(a.Sex, b.Sex);
            Assert.AreEqual(a.AgeYears, b.AgeYears);
            Assert.AreEqual(a.Role, b.Role);
            Assert.AreEqual(a.SpecialtySkillId, b.SpecialtySkillId);
            CollectionAssert.AreEquivalent(a.Skills, b.Skills);
            CollectionAssert.AreEquivalent(a.Attributes, b.Attributes);
        }
    }
}
