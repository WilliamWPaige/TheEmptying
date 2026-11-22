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
    /// <summary>R7: new-game founder placement (free cells, spaced apart) and the people save/load round trip, against the real shipped content.</summary>
    public class PeopleModuleTests
    {
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
        public void SaveAndLoad_PeopleSurviveTheRoundTrip()
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
                Assert.AreEqual(before[i].State.UnitContentId, after[i].State.UnitContentId);
                Assert.AreEqual(before[i].State.WorldX, after[i].State.WorldX);
                Assert.AreEqual(before[i].State.WorldZ, after[i].State.WorldZ);
            }
        }
    }
}
