using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Ponds;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>
    /// R7/R11: founder placement (now inside the keep, one at a time out
    /// the door, instead of spread on the map at new-game time). R8:
    /// identity and stats - deterministic by world seed, in range per
    /// role, exactly one Monarch and one Advisor, and the save/load round
    /// trip. R10: the action/movement settings' defaults, that the same
    /// seed gives the same action choices, and that action state
    /// (including history) survives a save and load round trip - all
    /// against the real shipped content.
    /// </summary>
    public class PeopleModuleTests
    {
        private static readonly string[] CoreTradeSkillIds =
        {
            "skill.logging", "skill.quarrying", "skill.farming", "skill.foraging", "skill.hauling", "skill.construction"
        };

        private static (Simulation Simulation, MapModule Map, BuildingsModule Buildings, PeopleModule People) BuildNewGame(TempCoreContentCopy core, ulong seed = 1)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (
                simulation,
                (MapModule)modules.First(m => m is MapModule),
                (BuildingsModule)modules.First(m => m is BuildingsModule),
                (PeopleModule)modules.First(m => m is PeopleModule));
        }

        /// <summary>R18b: a simple rectangular blob of cells, for tests that just need a working zone of roughly this size - not testing zone shape itself.</summary>
        private static HashSet<CellCoord> RectCells(MapModule map, double centerX, double centerZ, double sideA, double sideB)
        {
            double cellSize = map.CellSizeMeters;
            CellCoord center = map.WorldToCell(centerX, centerZ);
            int halfW = Math.Max(1, (int)Math.Round(sideA / cellSize / 2.0));
            int halfD = Math.Max(1, (int)Math.Round(sideB / cellSize / 2.0));

            var cells = new HashSet<CellCoord>();
            for (int dx = -halfW; dx < halfW; dx++)
            {
                for (int dz = -halfD; dz < halfD; dz++)
                {
                    cells.Add(new CellCoord(center.X + dx, center.Z + dz));
                }
            }

            return cells;
        }

        private static EntityId PlaceZone(ZonesModule zones, MapModule map, string zoneTypeId, double centerX, double centerZ, double sideA, double sideB)
        {
            return zones.CreateZone(zoneTypeId, zoneTypeId + " Zone", "#808080", RectCells(map, centerX, centerZ, sideA, sideB));
        }

        /// <summary>Places the keep at the map center and spawns the starting group (Monarch and founders waiting inside, Commoners appearing outside) - the same sequence the founding popup's Yes triggers.</summary>
        private static EntityId FoundKingdom(MapModule map, BuildingsModule buildings, PeopleModule people)
        {
            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            EntityId keepId = buildings.PlaceBuilding(BuildingsModule.KeepContentId, centerX, centerZ, 12, 12);
            people.SpawnFoundersInside(keepId);
            return keepId;
        }

        /// <summary>Steps the simulation until nobody is left waiting inside a building, or maxTicks is reached.</summary>
        private static void RunUntilEveryoneHasLeftTheKeep(Simulation simulation, PeopleModule people, int maxTicks = 5000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (people.People.All(r => r.Value.CurrentAction.Kind != ActionKind.InsideBuilding))
                {
                    return;
                }

                simulation.Step();
            }

            Assert.Fail("Not everyone left the keep within the tick budget.");
        }

        /// <summary>R12: while at least one Living Area exists, idle people pick their wander spots inside it instead of anywhere nearby.</summary>
        [Test]
        public void WithALivingArea_IdlePeopleWanderInsideIt()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            FoundKingdom(map, buildings, people);

            // Well clear of the 12x12 keep at the map center, and large enough that pathfinding and wander-target picking both have plenty of room.
            double livingAreaCenterX = map.WorldWidthMeters * 0.5 + 40;
            double livingAreaCenterZ = map.WorldDepthMeters * 0.5;
            EntityId livingAreaId = PlaceZone(zones, map, ZonesModule.LivingAreaTypeId, livingAreaCenterX, livingAreaCenterZ, 20, 20);

            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            for (int i = 0; i < 4000; i++)
            {
                simulation.Step();
            }

            Assert.IsTrue(zones.TryGetZone(livingAreaId, out ZoneState livingArea));

            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                Assert.IsTrue(livingArea.Cells.Contains(person.ReservedCell), $"{person.Name}'s reserved cell {person.ReservedCell} is outside the Living Area.");
            }
        }

        [Test]
        public void NewGame_StartsWithNoPeople()
        {
            using var core = new TempCoreContentCopy();
            (_, _, _, PeopleModule people) = BuildNewGame(core);

            Assert.AreEqual(0, people.People.Count());
        }

        /// <summary>R16: the Monarch and founders (Advisor included) wait inside the keep; the Commoners appear outside, already Idle, at the same moment.</summary>
        [Test]
        public void Founding_SpawnsTheConfiguredGroup_FoundersWaitInside_CommonersAppearOutside()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            EntityId keepId = FoundKingdom(map, buildings, people);

            Assert.AreEqual(9, people.People.Count());

            List<PersonState> all = people.People.Select(r => r.Value).ToList();
            List<PersonState> insideGroup = all.Where(p => p.Role != PersonRole.Commoner).ToList();
            List<PersonState> commoners = all.Where(p => p.Role == PersonRole.Commoner).ToList();

            Assert.AreEqual(5, insideGroup.Count, "Monarch + 4 founders (one the Advisor) = 5.");
            Assert.AreEqual(4, commoners.Count);

            foreach (PersonState person in insideGroup)
            {
                Assert.AreEqual(ActionKind.InsideBuilding, person.CurrentAction.Kind);
                Assert.AreEqual(keepId, person.CurrentAction.BuildingId);
                Assert.AreEqual("Waiting to head out.", person.CurrentAction.Thought);
            }

            Assert.AreEqual(5, people.GetPeopleInside(keepId).Count());

            foreach (PersonState commoner in commoners)
            {
                Assert.AreNotEqual(ActionKind.InsideBuilding, commoner.CurrentAction.Kind, "Commoners appear already outside, not waiting in the keep.");
                Assert.IsFalse(commoner.HomeBuildingId.HasValue, "Commoners have no home.");
            }
        }

        [Test]
        public void AfterEveryoneLeavesTheKeep_EveryFounder_IsOnAFreeCell()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);
            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            foreach (KeyValuePair<EntityId, PersonState> row in people.People)
            {
                CellCoord cell = map.WorldToCell(row.Value.WorldX, row.Value.WorldZ);
                Assert.IsTrue(map.IsCellFree(cell), $"Founder at {cell} is not on a free cell (a tree, stone or mound covers it).");
            }
        }

        /// <summary>
        /// Checked against ReservedCell, not instantaneous WorldX/WorldZ -
        /// "walking people may pass through each other" (R10) means two
        /// people's exact positions can coincide for a moment while one is
        /// mid-step and the other has just arrived or just set off; the
        /// real "no stacking" guarantee is that no two people ever hold
        /// the same reservation at once.
        /// </summary>
        [Test]
        public void AfterEveryoneLeavesTheKeep_NoTwoPeopleEverHoldTheSameReservedCell()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);
            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            for (int i = 0; i < 500; i++)
            {
                simulation.Step();

                List<CellCoord> reserved = people.People.Select(row => row.Value.ReservedCell).ToList();
                for (int a = 0; a < reserved.Count; a++)
                {
                    for (int b = a + 1; b < reserved.Count; b++)
                    {
                        Assert.AreNotEqual(reserved[a], reserved[b], $"tick {i}: two people both reserve {reserved[a]}.");
                    }
                }
            }
        }

        /// <summary>R16: founding makes exactly 1 Monarch, 1 Advisor, 3 other Founders and 4 Commoners.</summary>
        [Test]
        public void Founding_HasExactlyOneMonarchOneAdvisorThreeFoundersAndFourCommoners()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);

            List<PersonState> all = people.People.Select(r => r.Value).ToList();

            Assert.AreEqual(1, all.Count(p => p.Role == PersonRole.Monarch));
            Assert.AreEqual(1, all.Count(p => p.Role == PersonRole.Advisor));
            Assert.AreEqual(3, all.Count(p => p.Role == PersonRole.Founder));
            Assert.AreEqual(4, all.Count(p => p.Role == PersonRole.Commoner));
        }

        [Test]
        public void Founding_EveryRoll_IsInsideItsRoleRange()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);

            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                bool isMonarch = person.Role == PersonRole.Monarch;
                bool isCommoner = person.Role == PersonRole.Commoner;

                if (isCommoner)
                {
                    Assert.That(person.AgeYears, Is.InRange(18, 45), "Commoner age out of range.");
                }
                else
                {
                    Assert.That(person.AgeYears, isMonarch ? Is.InRange(25, 50) : Is.InRange(20, 40), $"{person.Role} age out of range.");
                }

                foreach (KeyValuePair<string, int> kvp in person.Attributes)
                {
                    (int Lower, int Upper) expected = isMonarch || isCommoner ? (6, 14) : (10, 16);
                    Assert.That(kvp.Value, Is.InRange(expected.Lower, expected.Upper), $"{person.Role} attribute {kvp.Key} out of range.");
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
                else if (isCommoner)
                {
                    Assert.IsNotEmpty(person.SpecialtySkillId, "A Commoner's trade skill must be set.");

                    foreach (KeyValuePair<string, int> kvp in person.Skills)
                    {
                        Assert.That(kvp.Value, kvp.Key == person.SpecialtySkillId ? Is.InRange(11, 15) : Is.InRange(3, 10), $"Commoner skill {kvp.Key} out of range.");
                    }

                    Assert.That(person.WalletTenthCopper, Is.InRange(200, 1000), "Commoner wallet (in tenth-copper) out of the 20-100 copper range.");
                    Assert.IsFalse(person.HomeBuildingId.HasValue, "Commoners have no home.");
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
        public void Founding_SameSeed_GivesTheSamePeopleAndStats()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (_, MapModule mapA, BuildingsModule buildingsA, PeopleModule peopleA) = BuildNewGame(coreA, 42);
            (_, MapModule mapB, BuildingsModule buildingsB, PeopleModule peopleB) = BuildNewGame(coreB, 42);
            FoundKingdom(mapA, buildingsA, peopleA);
            FoundKingdom(mapB, buildingsB, peopleB);

            List<PersonState> listA = peopleA.People.Select(r => r.Value).ToList();
            List<PersonState> listB = peopleB.People.Select(r => r.Value).ToList();

            Assert.AreEqual(listA.Count, listB.Count);
            for (int i = 0; i < listA.Count; i++)
            {
                AssertSameState(listA[i], listB[i]);
            }
        }

        [Test]
        public void Founding_SameSeed_GivesTheSameFirstActionChoices()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (Simulation simA, MapModule mapA, BuildingsModule buildingsA, PeopleModule peopleA) = BuildNewGame(coreA, 7);
            (Simulation simB, MapModule mapB, BuildingsModule buildingsB, PeopleModule peopleB) = BuildNewGame(coreB, 7);
            FoundKingdom(mapA, buildingsA, peopleA);
            FoundKingdom(mapB, buildingsB, peopleB);

            for (int i = 0; i < 2000; i++)
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

        /// <summary>R13: nobody likes and dislikes the same skill, and placeholder counts (2 liked, 1 disliked) are rolled for everyone.</summary>
        [Test]
        public void Founding_LikesAndDislikes_NeverOverlap_AndMatchThePlaceholderCounts()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);

            int likedCount = (int)simulation.Settings.Get(PeopleModule.LikedWorkCountSettingId);
            int dislikedCount = (int)simulation.Settings.Get(PeopleModule.DislikedWorkCountSettingId);

            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                Assert.AreEqual(likedCount, person.LikedSkillIds.Count);
                Assert.AreEqual(dislikedCount, person.DislikedSkillIds.Count);
                CollectionAssert.IsNotSubsetOf(person.LikedSkillIds, person.DislikedSkillIds);
                Assert.IsEmpty(person.LikedSkillIds.Intersect(person.DislikedSkillIds));
            }
        }

        /// <summary>R13: likes and dislikes are rolled from the world seed, so founding the same seed twice gives everyone the same ones.</summary>
        [Test]
        public void Founding_SameSeed_GivesTheSameLikesAndDislikes()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (Simulation _, MapModule mapA, BuildingsModule buildingsA, PeopleModule peopleA) = BuildNewGame(coreA, 11);
            (Simulation _, MapModule mapB, BuildingsModule buildingsB, PeopleModule peopleB) = BuildNewGame(coreB, 11);
            FoundKingdom(mapA, buildingsA, peopleA);
            FoundKingdom(mapB, buildingsB, peopleB);

            List<PersonState> listA = peopleA.People.Select(r => r.Value).ToList();
            List<PersonState> listB = peopleB.People.Select(r => r.Value).ToList();

            Assert.AreEqual(listA.Count, listB.Count);
            Assert.Greater(listA.Count, 0);

            for (int i = 0; i < listA.Count; i++)
            {
                CollectionAssert.AreEqual(listA[i].LikedSkillIds, listB[i].LikedSkillIds);
                CollectionAssert.AreEqual(listA[i].DislikedSkillIds, listB[i].DislikedSkillIds);
            }
        }

        [TestCase(PeopleModule.WalkSpeedMpsSettingId, SettingValueType.Number, 1.4, 0.1, 10.0, "People")]
        [TestCase(PeopleModule.PathfindingMaxDistanceMetersSettingId, SettingValueType.Number, 500.0, 1.0, 5000.0, "People")]
        [TestCase(PeopleModule.IdleWaitMinSecondsSettingId, SettingValueType.Number, 5.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.IdleWaitMaxSecondsSettingId, SettingValueType.Number, 20.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.IdleWanderRadiusMetersSettingId, SettingValueType.Number, 8.0, 1.0, 100.0, "People")]
        [TestCase(PeopleModule.ActionHistoryCountSettingId, SettingValueType.Integer, 10.0, 1.0, 100.0, "People")]
        [TestCase(PeopleModule.KeepExitIntervalMinSecondsSettingId, SettingValueType.Number, 3.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.KeepExitIntervalMaxSecondsSettingId, SettingValueType.Number, 5.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.HungerDrainDaysSettingId, SettingValueType.Number, 1.0, 0.1, 30.0, "People")]
        [TestCase(PeopleModule.HungerEatThresholdSettingId, SettingValueType.Number, 50.0, 0.0, 99.0, "People")]
        [TestCase(PeopleModule.EatingDurationMinutesSettingId, SettingValueType.Number, 5.0, 0.1, 120.0, "People")]
        [TestCase(PeopleModule.HomeStashMinimumGrubsSettingId, SettingValueType.Integer, 5.0, 0.0, 1000.0, "People")]
        [TestCase(PeopleModule.SkillYearsToMasterSettingId, SettingValueType.Number, 10.0, 0.1, 200.0, "People")]
        [TestCase(PeopleModule.SkillWorkHoursPerDaySettingId, SettingValueType.Number, 8.0, 1.0, 24.0, "People")]
        [TestCase(PeopleModule.SkillLevelGrowthFactorSettingId, SettingValueType.Number, 1.25, 1.01, 5.0, "People")]
        [TestCase(PeopleModule.LikedWorkCountSettingId, SettingValueType.Integer, 2.0, 0.0, 10.0, "People")]
        [TestCase(PeopleModule.DislikedWorkCountSettingId, SettingValueType.Integer, 1.0, 0.0, 10.0, "People")]
        [TestCase(PeopleModule.LikedWorkChoiceWeightSettingId, SettingValueType.Number, 2.0, 0.1, 10.0, "People")]
        [TestCase(PeopleModule.DislikedWorkChoiceWeightSettingId, SettingValueType.Number, 0.5, 0.01, 10.0, "People")]
        [TestCase(PeopleModule.LikedWorkExperienceBonusSettingId, SettingValueType.Number, 0.25, 0.0, 5.0, "People")]
        [TestCase(PeopleModule.SpotWaitMinSecondsSettingId, SettingValueType.Number, 10.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.SpotWaitMaxSecondsSettingId, SettingValueType.Number, 30.0, 0.1, 300.0, "People")]
        [TestCase(PeopleModule.LogDecisionsSettingId, SettingValueType.Boolean, 0.0, 0.0, 1.0, "People")]
        [TestCase(PeopleModule.CrownStartingTreasuryCopperSettingId, SettingValueType.Integer, 5000.0, 0.0, 1000000.0, "People")]
        [TestCase(PeopleModule.ThirstDrainDaysSettingId, SettingValueType.Number, 2.0, 0.1, 30.0, "People")]
        [TestCase(PeopleModule.ThirstDrinkThresholdSettingId, SettingValueType.Number, 50.0, 0.0, 99.0, "People")]
        [TestCase(PeopleModule.DrinkingDurationMinutesSettingId, SettingValueType.Number, 5.0, 0.1, 120.0, "People")]
        [TestCase(PeopleModule.MakeEveryoneThirstyMeterSettingId, SettingValueType.Number, 40.0, 0.0, 100.0, "People")]
        public void Setting_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory(
            string settingId, SettingValueType expectedType, double expectedDefault, double expectedMinimum, double expectedMaximum, string expectedCategory)
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _, _) = BuildNewGame(core);

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
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule peopleBefore) = BuildNewGame(core);
            FoundKingdom(map, buildings, peopleBefore);

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

        /// <summary>R13: hunger, carried goods, a home stash, the current goal/target, likes/dislikes and skill experience all survive a save and load round trip - with every one of those fields set to a non-default value first, so the round trip is actually exercised.</summary>
        [Test]
        public void SaveAndLoad_HungerCarryingStashExperienceLikesAndDislikes_SurviveTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule peopleBefore) = BuildNewGame(core);
            FoundKingdom(map, buildings, peopleBefore);

            EntityId firstId = peopleBefore.People.First().Key;
            peopleBefore.TryGetPerson(firstId, out PersonState firstPerson);

            firstPerson.Hunger = 37.5;
            firstPerson.Thirst = 62.5;
            firstPerson.AddCarried("item.grub", 4);
            firstPerson.ForagingItemId = "item.grub";
            firstPerson.EatingFromCarried = false;
            firstPerson.WalletTenthCopper = 13;
            Assert.IsTrue(firstPerson.HomeBuildingId.HasValue);
            peopleBefore.DepositIntoHomeStash(firstPerson.HomeBuildingId.Value, "item.grub", 6);
            firstPerson.Goal = PersonGoal.ForagingForStockpile;
            firstPerson.TargetIsHome = true;
            firstPerson.TargetIsMound = false;
            firstPerson.LastAteFromMound = true;
            firstPerson.SkillExperienceHours["skill.foraging"] = 12.5;
            firstPerson.SetSkillLevel("skill.foraging", 9);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var peopleAfter = (PeopleModule)loadedModules.First(m => m is PeopleModule);

            Assert.IsTrue(peopleAfter.TryGetPerson(firstId, out PersonState loadedPerson));
            AssertSameState(firstPerson, loadedPerson);

            Assert.IsTrue(loadedPerson.HomeBuildingId.HasValue);
            CollectionAssert.AreEquivalent(peopleBefore.GetHomeStash(firstPerson.HomeBuildingId.Value), peopleAfter.GetHomeStash(loadedPerson.HomeBuildingId.Value));
        }

        /// <summary>R10/R11: a founded game, with people still waiting inside the keep, survives a save and load round trip exactly (action state, history and the keep-exit timer alike), and continuing the loaded game afterward matches continuing the original unbroken.</summary>
        [Test]
        public void SaveAndLoad_AFoundedGameWithPeopleStillInside_SurvivesTheRoundTripAndKeepsMatchingAfterward()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule peopleBefore) = BuildNewGame(core);
            FoundKingdom(map, buildings, peopleBefore);

            for (int i = 0; i < 10; i++)
            {
                simulation.Step();
            }

            List<(EntityId Id, PersonState State)> before = peopleBefore.People.Select(r => (r.Key, r.Value)).ToList();
            Assert.Greater(before.Count, 0);
            Assert.IsTrue(before.Any(p => p.State.CurrentAction.Kind == ActionKind.InsideBuilding), "Sanity check: someone should still be inside this early.");

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

            // Continuing the loaded game (through the rest of everyone leaving the keep) must match continuing the original, unbroken.
            for (int i = 0; i < 2000; i++)
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
            Assert.AreEqual(x.BuildingId, y.BuildingId);
            Assert.AreEqual(x.SkillId, y.SkillId);
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

            Assert.AreEqual(a.Hunger, b.Hunger);
            Assert.AreEqual(a.Thirst, b.Thirst);
            CollectionAssert.AreEquivalent(a.CarriedItems, b.CarriedItems);
            Assert.AreEqual(a.ForagingItemId, b.ForagingItemId);
            Assert.AreEqual(a.EatingFromCarried, b.EatingFromCarried);
            Assert.AreEqual(a.WalletTenthCopper, b.WalletTenthCopper);
            Assert.AreEqual(a.HomeBuildingId, b.HomeBuildingId);
            Assert.AreEqual(a.Goal, b.Goal);
            Assert.AreEqual(a.TargetZoneId, b.TargetZoneId);
            Assert.AreEqual(a.TargetPondId, b.TargetPondId);
            Assert.AreEqual(a.ForagingDeliveryZoneId, b.ForagingDeliveryZoneId);
            Assert.AreEqual(a.TargetIsHome, b.TargetIsHome);
            Assert.AreEqual(a.TargetIsMound, b.TargetIsMound);
            Assert.AreEqual(a.LastAteFromMound, b.LastAteFromMound);
            CollectionAssert.AreEquivalent(a.LikedSkillIds, b.LikedSkillIds);
            CollectionAssert.AreEquivalent(a.DislikedSkillIds, b.DislikedSkillIds);
            CollectionAssert.AreEquivalent(a.SkillExperienceHours, b.SkillExperienceHours);
        }

        /// <summary>R13b: a mound never has more than its own max-workers limit of people reserving one of its neighbor cells at once, and no two people ever hold the same reserved cell - the fix for the "one worker only" / endless "someone is already there" bugs.</summary>
        [Test]
        public void ForagingNearMounds_NeverExceedsAMoundsMaxWorkers_AndNeverDoubleReservesACell()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            FoundKingdom(map, buildings, people);

            // So foraging-for-the-stockpile is actually chosen as work, not just eating at a mound when hungry.
            PlaceZone(zones, map, ZonesModule.StockpileTypeId, centerX + 20, centerZ, 10, 10);

            Assert.IsTrue(simulation.Content.TryGet(BuiltInContentTypes.ResourceTypeName, "resource.grub_mound", out ContentEntry entry));
            var moundResource = (ResourceContent)entry.Definition;
            int maxWorkers = moundResource.MaxWorkers;
            Assert.Greater(maxWorkers, 0);

            var moundCells = new HashSet<CellCoord>();
            ChunkCoord centerChunk = map.WorldToChunk(centerX, centerZ);
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    ChunkData chunk = map.GetOrCreateChunk(new ChunkCoord(centerChunk.X + dx, centerChunk.Z + dz));
                    foreach (ChunkPlacement placement in chunk.Placements)
                    {
                        if (placement.Kind == PlacementKind.GrubMound)
                        {
                            moundCells.Add(placement.Cell);
                        }
                    }
                }
            }

            Assert.Greater(moundCells.Count, 0, "Sanity check: expected grub mounds near the keep.");

            for (int tick = 0; tick < 20000; tick++)
            {
                simulation.Step();

                List<CellCoord> reservedCells = people.People
                    .Select(r => r.Value)
                    .Where(p => p.CurrentAction.Kind != ActionKind.InsideBuilding)
                    .Select(p => p.ReservedCell)
                    .ToList();

                CollectionAssert.AllItemsAreUnique(reservedCells, $"Two people held the same reserved cell at tick {tick}.");

                foreach (CellCoord moundCell in moundCells)
                {
                    int workers = reservedCells.Count(c => IsAdjacentOrSame(c, moundCell));
                    Assert.LessOrEqual(workers, maxWorkers, $"Mound at {moundCell} had {workers} workers at tick {tick}, more than its limit of {maxWorkers}.");
                }
            }
        }

        private static bool IsAdjacentOrSame(CellCoord a, CellCoord b)
        {
            return Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Z - b.Z) <= 1;
        }

        /// <summary>R13b/R16: stockpiles are drop-off and pick-up only - nobody's idle target (their standing cell while Idle) is ever inside one, over a long run with several stockpiles (the single central check in ProcessPersonAction, not a per-path patch).</summary>
        [Test]
        public void IdlePeople_AreNeverStandingInsideAStockpile()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            FoundKingdom(map, buildings, people);

            // Several small, close and tightly capped stockpiles, so people deliver to and eat from all of them constantly - clear of the 12x12 keep at the map center.
            var stockpiles = new List<ZoneState>();
            foreach ((double offsetX, double offsetZ) in new[] { (15.0, 0.0), (-15.0, 0.0), (0.0, 15.0) })
            {
                EntityId stockpileId = PlaceZone(zones, map, ZonesModule.StockpileTypeId, centerX + offsetX, centerZ + offsetZ, 8, 8);
                zones.SetKeepOnHandLimit(stockpileId, "item.grub", 3);
                Assert.IsTrue(zones.TryGetZone(stockpileId, out ZoneState stockpile));
                stockpiles.Add(stockpile);
            }

            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            for (int tick = 0; tick < 15000; tick++)
            {
                simulation.Step();

                foreach (PersonState person in people.People.Select(r => r.Value))
                {
                    if (person.CurrentAction.Kind != ActionKind.Idle)
                    {
                        continue;
                    }

                    foreach (ZoneState stockpile in stockpiles)
                    {
                        Assert.IsFalse(stockpile.Cells.Contains(person.ReservedCell), $"{person.Name} was idling inside a stockpile at tick {tick} (thought: '{person.CurrentAction.Thought}').");
                    }
                }
            }
        }

        /// <summary>R16: a person loaded from a save while already idling inside a stockpile (the exact reported bug, forced here) steps out on the very first tick after load - caught by the same central check as every other path, not a load-time special case.</summary>
        [Test]
        public void APersonIdlingInsideAStockpile_StepsOutAfterBeingLoadedFromASave()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            FoundKingdom(map, buildings, people);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            EntityId stockpileId = PlaceZone(zones, map, ZonesModule.StockpileTypeId, centerX + 15, centerZ, 8, 8);
            Assert.IsTrue(zones.TryGetZone(stockpileId, out ZoneState stockpile));

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);

            // Force the exact reported bug directly: an Idle action, standing inside the stockpile, with plenty of duration left.
            person.WorldX = stockpile.CenterX;
            person.WorldZ = stockpile.CenterZ;
            long tick = simulation.Clock.CurrentTick;
            person.CurrentAction = new PersonAction { Kind = ActionKind.Idle, Thought = "Stuck inside the stockpile.", StartTick = tick, DurationTicks = 1000, CurrentTickForProgress = tick };

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 9 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            (Simulation loaded, LoadReport _) = GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var peopleAfter = (PeopleModule)loadedModules.First(m => m is PeopleModule);

            Assert.IsTrue(peopleAfter.TryGetPerson(firstId, out PersonState loadedPerson));
            Assert.AreEqual(ActionKind.Idle, loadedPerson.CurrentAction.Kind, "Sanity check: the save really did capture them idling.");

            loaded.Step();

            Assert.AreEqual(ActionKind.Walk, loadedPerson.CurrentAction.Kind, "A single tick after load should already have started stepping them out.");

            Assert.IsFalse(stockpile.Cells.Contains(loadedPerson.ReservedCell), "The cell they're now walking toward must not be inside the stockpile.");
        }

        /// <summary>R13b: goods never vanish - when a stockpile can't take everything being delivered, only what fit is removed from what's carried; the rest stays carried for the next trip.</summary>
        [Test]
        public void DeliveringMoreThanAStockpileCanHold_KeepsTheLeftoverCarried()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            FoundKingdom(map, buildings, people);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            EntityId stockpileId = PlaceZone(zones, map, ZonesModule.StockpileTypeId, centerX + 20, centerZ, 8, 8);
            zones.SetKeepOnHandLimit(stockpileId, "item.grub", 2);

            Assert.IsTrue(zones.TryGetZone(stockpileId, out ZoneState stockpile));

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);

            person.WorldX = stockpile.CenterX;
            person.WorldZ = stockpile.CenterZ;
            person.AddCarried("item.grub", 10);
            person.ForagingItemId = "item.grub";
            person.Goal = PersonGoal.ForagingForStockpile;
            person.ForagingDeliveryZoneId = stockpileId;

            // A Walk that's already "arrived" (TotalDistanceMeters 0), not an Idle one - R16's central stockpile-exit check would otherwise immediately walk them back out before this decision ever runs, since nobody's Idle action is ever allowed to stand inside a stockpile.
            long tick = simulation.Clock.CurrentTick;
            person.CurrentAction = new PersonAction
            {
                Kind = ActionKind.Walk,
                StartTick = tick,
                TotalDistanceMeters = 0,
                CurrentTickForProgress = tick
            };

            simulation.Step();

            (int amountInStockpile, _, _) = zones.GetStockpileItemInfo(stockpileId, "item.grub");
            Assert.AreEqual(2, amountInStockpile, "Only the room actually available should have been added.");
            Assert.AreEqual(8, person.CarriedAmountOf("item.grub"), "The leftover that didn't fit must stay carried, not vanish.");
        }

        /// <summary>R15/R16: a new game's crown treasury starts at exactly 5,000 copper (the placeholder setting's default); every founder's wallet starts empty, and every Commoner's starts somewhere in their 20-100 copper placeholder range.</summary>
        [Test]
        public void NewGame_TreasuryStartsAt5000Copper_AndFoundersStartWithEmptyWallets()
        {
            using var core = new TempCoreContentCopy();
            (Simulation _, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);

            Assert.AreEqual(50000, people.TreasuryTenthCopper);
            Assert.AreEqual("5000 copper", PeopleModule.FormatTenthCopper(people.TreasuryTenthCopper));

            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                if (person.Role == PersonRole.Commoner)
                {
                    Assert.That(person.WalletTenthCopper, Is.InRange(200, 1000));
                }
                else
                {
                    Assert.AreEqual(0, person.WalletTenthCopper);
                }
            }
        }

        /// <summary>R15: cash on delivery - dropping 5 sticks (0.2 copper each) in a crown stockpile pays out exactly 1 copper, taken from the treasury and credited to the person's wallet, with no fraction lost.</summary>
        [Test]
        public void DeliveringFiveSticks_PaysExactlyOneCopper_FromTreasuryToWallet()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            FoundKingdom(map, buildings, people);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            EntityId stockpileId = PlaceZone(zones, map, ZonesModule.StockpileTypeId, centerX + 20, centerZ, 8, 8);
            Assert.IsTrue(zones.TryGetZone(stockpileId, out ZoneState stockpile));

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);

            int treasuryBefore = people.TreasuryTenthCopper;

            person.WorldX = stockpile.CenterX;
            person.WorldZ = stockpile.CenterZ;
            person.AddCarried("item.stick", 5);
            person.ForagingItemId = "item.stick";
            person.Goal = PersonGoal.ForagingForStockpile;
            person.ForagingDeliveryZoneId = stockpileId;

            // A Walk that's already "arrived" (TotalDistanceMeters 0), not an Idle one - see the comment in DeliveringMoreThanAStockpileCanHold_KeepsTheLeftoverCarried.
            long tick = simulation.Clock.CurrentTick;
            person.CurrentAction = new PersonAction { Kind = ActionKind.Walk, StartTick = tick, TotalDistanceMeters = 0, CurrentTickForProgress = tick };

            simulation.Step();

            (int amountInStockpile, _, _) = zones.GetStockpileItemInfo(stockpileId, "item.stick");
            Assert.AreEqual(5, amountInStockpile, "All 5 sticks should have fit and been accepted.");
            Assert.AreEqual(0, person.CarriedAmountOf("item.stick"), "Everything delivered should no longer be carried.");
            Assert.AreEqual(10, treasuryBefore - people.TreasuryTenthCopper, "5 sticks at 0.2 copper each must cost exactly 1 whole copper (10 tenth-copper), no more and no less.");
            Assert.AreEqual(10, person.WalletTenthCopper, "The paid amount must be credited to the person's own wallet.");
        }

        /// <summary>R15: if the treasury can't pay for an item, it isn't accepted at all - a person already carrying unpaid goods keeps them and takes them home instead, rather than losing them or getting paid for nothing.</summary>
        [Test]
        public void WhenTheTreasuryCannotPay_GoodsAreNotAcceptedAndStayWithThePerson()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var zones = (ZonesModule)modules.First(m => m is ZonesModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            EntityId keepId = FoundKingdom(map, buildings, people);
            Assert.IsTrue(buildings.TryGetBuilding(keepId, out BuildingState keep));

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            EntityId stockpileId = PlaceZone(zones, map, ZonesModule.StockpileTypeId, centerX + 20, centerZ, 8, 8);

            people.SetTreasuryTenthCopper(1);

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);
            Assert.IsTrue(person.HomeBuildingId.HasValue);

            double cellSize = map.CellSizeMeters;
            person.WorldX = (keep.DoorCell.X + 0.5) * cellSize;
            person.WorldZ = (keep.DoorCell.Z + 0.5) * cellSize;
            person.AddCarried("item.stick", 5);
            person.ForagingItemId = "item.stick";
            person.Goal = PersonGoal.ForagingForStockpile;
            person.ForagingDeliveryZoneId = stockpileId;

            long tick = simulation.Clock.CurrentTick;
            person.CurrentAction = new PersonAction { Kind = ActionKind.Idle, StartTick = tick, DurationTicks = 1, CurrentTickForProgress = tick };

            simulation.Step();

            (int amountInStockpile, _, _) = zones.GetStockpileItemInfo(stockpileId, "item.stick");
            Assert.AreEqual(0, amountInStockpile, "Nothing should have been accepted into the stockpile while the treasury can't pay for it.");
            Assert.AreEqual(0, person.CarriedAmountOf("item.stick"), "Goods that couldn't be sold were taken home, not left carried in limbo.");
            CollectionAssert.Contains(people.GetHomeStash(keepId).Keys, "item.stick", "Unpaid goods should be deposited at home instead.");
            Assert.AreEqual(5, people.GetHomeStash(keepId)["item.stick"]);
            Assert.AreEqual(1, people.TreasuryTenthCopper, "The treasury must not be touched for goods it couldn't afford.");
            Assert.AreEqual(0, person.WalletTenthCopper, "Nobody is paid for goods the crown never accepted.");
        }

        /// <summary>R15: the crown treasury and every person's wallet survive a save and load round trip exactly.</summary>
        [Test]
        public void TreasuryAndWallets_SurviveASaveAndLoadRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule peopleBefore) = BuildNewGame(core);
            FoundKingdom(map, buildings, peopleBefore);

            peopleBefore.SetTreasuryTenthCopper(4321);

            EntityId firstId = peopleBefore.People.First().Key;
            peopleBefore.TryGetPerson(firstId, out PersonState firstPerson);
            firstPerson.WalletTenthCopper = 99;

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = peopleBefore.People.Count() }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var peopleAfter = (PeopleModule)loadedModules.First(m => m is PeopleModule);

            Assert.AreEqual(4321, peopleAfter.TreasuryTenthCopper);
            Assert.IsTrue(peopleAfter.TryGetPerson(firstId, out PersonState loadedPerson));
            Assert.AreEqual(99, loadedPerson.WalletTenthCopper);
        }

        /// <summary>R13b: a home building's shared stash (not any one person's) survives a save and load round trip.</summary>
        [Test]
        public void SharedHomeStash_SurvivesASaveAndLoadRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule peopleBefore) = BuildNewGame(core);
            EntityId keepId = FoundKingdom(map, buildings, peopleBefore);

            peopleBefore.DepositIntoHomeStash(keepId, "item.grub", 11);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 9 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var peopleAfter = (PeopleModule)loadedModules.First(m => m is PeopleModule);

            CollectionAssert.AreEquivalent(peopleBefore.GetHomeStash(keepId), peopleAfter.GetHomeStash(keepId));
            Assert.AreEqual(11, peopleAfter.GetHomeStash(keepId)["item.grub"]);
        }

        /// <summary>R17: thirst drains by exactly FullThirst / (drainDays * ticksPerDay) each tick a person isn't inside a building - the same math as hunger, on its own setting.</summary>
        [Test]
        public void ThirstDrain_ReducesAtTheRateImpliedByTheSetting()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);
            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);
            person.Thirst = 100.0;

            double drainDays = simulation.Settings.Get(PeopleModule.ThirstDrainDaysSettingId);
            double expectedDrainPerTick = 100.0 / (drainDays * CalendarConstants.TicksPerDay);

            simulation.Step();

            Assert.AreEqual(100.0 - expectedDrainPerTick, person.Thirst, 1e-6);
        }

        /// <summary>R17: whichever meter is lower is served first, whether that's hunger or thirst.</summary>
        [Test]
        public void WhenBothHungerAndThirstAreBelowThreshold_TheLowerMeterIsServedFirst()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);
            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);

            long tick = simulation.Clock.CurrentTick;
            person.Hunger = 30;
            person.Thirst = 20;
            person.Goal = PersonGoal.None;
            person.CurrentAction = new PersonAction { Kind = ActionKind.Idle, StartTick = tick - 1000, DurationTicks = 1, CurrentTickForProgress = tick };
            simulation.Step();
            Assert.AreEqual(PersonGoal.SeekingWater, person.Goal, "Thirst (20) is lower than Hunger (30), so water should be served first.");

            tick = simulation.Clock.CurrentTick;
            person.Hunger = 20;
            person.Thirst = 30;
            person.Goal = PersonGoal.None;
            person.CurrentAction = new PersonAction { Kind = ActionKind.Idle, StartTick = tick - 1000, DurationTicks = 1, CurrentTickForProgress = tick };
            simulation.Step();
            Assert.AreEqual(PersonGoal.SeekingFood, person.Goal, "Hunger (20) is lower than Thirst (30), so food should be served first.");
        }

        /// <summary>R17: walking to a pond's edge and drinking fills thirst to exactly full and takes exactly 1 drink from the pond drunk from.</summary>
        [Test]
        public void WhenThirsty_PersonWalksToAPondAndDrinksToFull_TakingExactlyOneDrinkFromIt()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var ponds = (PondsModule)modules.First(m => m is PondsModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            FoundKingdom(map, buildings, people);
            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);
            person.Thirst = 5;

            EntityId? targetPondId = null;
            int drinksWhenStarted = 0;

            for (int tick = 0; tick < 50000; tick++)
            {
                simulation.Step();

                if (targetPondId == null && person.CurrentAction.Kind == ActionKind.Drinking)
                {
                    targetPondId = person.TargetPondId;
                    Assert.IsTrue(targetPondId.HasValue);
                    Assert.IsTrue(ponds.TryGetPond(targetPondId.Value, out PondState pondAtStart));
                    drinksWhenStarted = pondAtStart.DrinksCurrent;
                }

                if (targetPondId.HasValue && person.Thirst >= 100.0)
                {
                    break;
                }
            }

            Assert.IsTrue(targetPondId.HasValue, "The thirsty person should have started drinking within the tick budget.");
            Assert.AreEqual(100.0, person.Thirst);
            Assert.IsTrue(ponds.TryGetPond(targetPondId.Value, out PondState pondAfter));
            Assert.AreEqual(drinksWhenStarted - 1, pondAfter.DrinksCurrent);
        }

        /// <summary>R17: a dry pond (0 drinks) is never targeted for drinking - a thirsty person finds a different, non-dry pond instead.</summary>
        [Test]
        public void WhenTheNearestPondIsDry_PersonNeverDrinksFromIt()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var map = (MapModule)modules.First(m => m is MapModule);
            var buildings = (BuildingsModule)modules.First(m => m is BuildingsModule);
            var ponds = (PondsModule)modules.First(m => m is PondsModule);
            var people = (PeopleModule)modules.First(m => m is PeopleModule);

            FoundKingdom(map, buildings, people);
            RunUntilEveryoneHasLeftTheKeep(simulation, people);

            EntityId dryPondId = ponds.Ponds.First().Key;
            ponds.SetDrinks(dryPondId, 0);
            Assert.IsTrue(ponds.TryGetPond(dryPondId, out PondState dryPond));
            Assert.IsTrue(dryPond.IsDry);

            EntityId firstId = people.People.First().Key;
            people.TryGetPerson(firstId, out PersonState person);
            person.Thirst = 5;

            bool drankFromTheDryPond = false;
            bool drankAtAll = false;

            for (int tick = 0; tick < 50000; tick++)
            {
                simulation.Step();

                if (person.CurrentAction.Kind == ActionKind.Drinking)
                {
                    drankAtAll = true;
                    if (person.TargetPondId == dryPondId)
                    {
                        drankFromTheDryPond = true;
                    }
                }

                if (drankAtAll && person.Thirst >= 100.0)
                {
                    break;
                }
            }

            Assert.IsFalse(drankFromTheDryPond, "A dry pond must never be targeted for drinking.");
            Assert.IsTrue(drankAtAll, "A reachable, non-dry pond should still have been found and used.");
        }

        /// <summary>R17: the "Make everyone thirsty" cheat sets every current person's thirst to the placeholder meter value, immediately.</summary>
        [Test]
        public void MakeEveryoneThirsty_SetsEveryonesThirstToThePlaceholderMeter()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings, PeopleModule people) = BuildNewGame(core);
            FoundKingdom(map, buildings, people);

            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                person.Thirst = 100.0;
            }

            people.MakeEveryoneThirsty();

            double expectedMeter = simulation.Settings.Get(PeopleModule.MakeEveryoneThirstyMeterSettingId);
            foreach (PersonState person in people.People.Select(r => r.Value))
            {
                Assert.AreEqual(expectedMeter, person.Thirst);
            }
        }
    }
}
