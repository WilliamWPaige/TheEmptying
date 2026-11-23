using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
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
    /// trip. R10: the new action/movement settings' defaults, that the
    /// same seed gives the same action choices, and that action state
    /// (including history) survives a save and load round trip - all
    /// against the real shipped content.
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
        public void NewGame_SameSeed_GivesTheSameFirstActionChoices()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (Simulation simA, _, PeopleModule peopleA) = BuildNewGame(coreA, 7);
            (Simulation simB, _, PeopleModule peopleB) = BuildNewGame(coreB, 7);

            for (int i = 0; i < 100; i++)
            {
                simA.Step();
                simB.Step();
            }

            List<PersonState> listA = peopleA.People.Select(r => r.Value).ToList();
            List<PersonState> listB = peopleB.People.Select(r => r.Value).ToList();

            Assert.AreEqual(listA.Count, listB.Count);
            Assert.Greater(listA.Count, 0);

            for (int i = 0; i < listA.Count; i++)
            {
                AssertSameActionState(listA[i], listB[i]);
            }
        }

        [TestCase(PeopleModule.WalkSpeedMpsSettingId, SettingValueType.Number, 1.4, 0.1, 10.0, "People")]
        [TestCase(PeopleModule.PathfindingMaxDistanceMetersSettingId, SettingValueType.Number, 500.0, 1.0, 5000.0, "People")]
        [TestCase(PeopleModule.IdleWaitMinSecondsSettingId, SettingValueType.Number, 5.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.IdleWaitMaxSecondsSettingId, SettingValueType.Number, 20.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.IdleWanderRadiusMetersSettingId, SettingValueType.Number, 8.0, 1.0, 100.0, "People")]
        [TestCase(PeopleModule.ActionHistoryCountSettingId, SettingValueType.Integer, 10.0, 1.0, 100.0, "People")]
        public void Setting_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory(
            string settingId, SettingValueType expectedType, double expectedDefault, double expectedMinimum, double expectedMaximum, string expectedCategory)
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _) = BuildNewGame(core);

            Assert.IsTrue(simulation.Settings.TryGetDefinition(settingId, out SettingDefinition definition), $"Expected '{settingId}' to be declared.");
            Assert.AreEqual(expectedType, definition.ValueType);
            Assert.AreEqual(expectedDefault, definition.Default);
            Assert.AreEqual(expectedMinimum, definition.Minimum);
            Assert.AreEqual(expectedMaximum, definition.Maximum);
            Assert.AreEqual(expectedCategory, definition.Category);
            Assert.AreEqual(expectedDefault, simulation.Settings.Get(settingId), "The live value should start at the default.");
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

        /// <summary>R10: a person's action state (kind, thought, progress-relevant fields, path, reserved cell, history) survives a save and load round trip exactly, and continuing the loaded game afterward matches continuing the original unbroken.</summary>
        [Test]
        public void SaveAndLoad_ActionStateSurvivesTheRoundTripAndKeepsMatchingAfterward()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, PeopleModule peopleBefore) = BuildNewGame(core);

            for (int i = 0; i < 200; i++)
            {
                simulation.Step();
            }

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
                AssertSameActionState(before[i].State, after[i].State);
            }

            // Continuing the loaded game must match continuing the original, unbroken - including the world itself (a chunk already generated before the save must never regenerate differently after a load).
            for (int i = 0; i < 200; i++)
            {
                simulation.Step();
                loaded.Step();
            }

            Assert.AreEqual(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        }

        private static void AssertSameActionState(PersonState a, PersonState b)
        {
            Assert.AreEqual(a.WorldX, b.WorldX);
            Assert.AreEqual(a.WorldZ, b.WorldZ);
            Assert.AreEqual(a.ReservedCell, b.ReservedCell);

            PersonAction x = a.CurrentAction;
            PersonAction y = b.CurrentAction;
            Assert.AreEqual(x.Kind, y.Kind);
            Assert.AreEqual(x.Thought, y.Thought);
            Assert.AreEqual(x.StartTick, y.StartTick);
            Assert.AreEqual(x.DurationTicks, y.DurationTicks);
            Assert.AreEqual(x.TargetCell, y.TargetCell);
            CollectionAssert.AreEqual(x.Path, y.Path);
            Assert.AreEqual(x.NextPathIndex, y.NextPathIndex);
            Assert.AreEqual(x.DistanceTraveledMeters, y.DistanceTraveledMeters);
            Assert.AreEqual(x.TotalDistanceMeters, y.TotalDistanceMeters);

            Assert.AreEqual(a.History.Count, b.History.Count);
            for (int h = 0; h < a.History.Count; h++)
            {
                Assert.AreEqual(a.History[h].Kind, b.History[h].Kind);
                Assert.AreEqual(a.History[h].Thought, b.History[h].Thought);
                Assert.AreEqual(a.History[h].StartTick, b.History[h].StartTick);
                Assert.AreEqual(a.History[h].DurationTicks, b.History[h].DurationTicks);
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
