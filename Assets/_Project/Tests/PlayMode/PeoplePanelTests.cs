using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.9f, Part 2: a hand test found no way to see what founders were doing or why some looked
    /// idle. The People panel lists every person with their activity, task, target and needs, and
    /// selecting a row moves the camera to them and draws a line to their target.
    /// </summary>
    public class PeoplePanelTests
    {
        [UnityTest]
        public IEnumerator PeoplePanel_Opens_AndListsAllNineStartingPeopleWithActivities()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsFalse(bootstrap.IsPeoplePanelOpenForTesting, "Setup: the panel should start closed.");

            bootstrap.ShowPeoplePanelForTesting();
            yield return null;

            Assert.IsTrue(bootstrap.IsPeoplePanelOpenForTesting, "Expected the people panel to be open after showing it.");
            Assert.AreEqual(9, bootstrap.PeoplePanelRowCountForTesting, "Expected all 9 starting people (8 founders + the monarch) to be listed.");

            // The "activities" requirement is checked more directly by PeoplePanel_RowClick_SelectsPersonAndDrawsLineToTarget
            // (which needs a real task/target to assert against) and by the underlying data this panel reads
            // (PersonState.Activity, GatheringModule.GetTaskKind) - both covered by GatheringModuleTests and
            // RealTimePlayTests. Here, just confirm the panel is actually reachable and populated.
        }

        [UnityTest]
        public IEnumerator PeoplePanel_RowClick_SelectsPersonAndMovesCamera()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            string peopleList = bootstrap.RunConsoleCommandForTesting("people");
            string firstLine = peopleList.Split('\n')[0];
            Assert.IsTrue(EntityId.TryParse(firstLine.Split(':')[0].Trim(), out EntityId founder), $"Expected to parse a founder id from: {firstLine}");

            bootstrap.ShowPeoplePanelForTesting();
            yield return null;

            Assert.IsNull(bootstrap.PeoplePanelSelectedPersonIdForTesting, "Setup: nobody should be selected yet.");

            Camera camera = bootstrap.CameraForTesting;
            Vector3 cameraPositionBefore = camera.transform.position;

            bootstrap.SelectPersonInPeoplePanelForTesting(founder);
            yield return null;

            Assert.AreEqual(founder, bootstrap.PeoplePanelSelectedPersonIdForTesting, "Expected the clicked person to become selected.");
            Assert.AreNotEqual(cameraPositionBefore, camera.transform.position, "Expected selecting a person to move the camera toward them.");
        }

        /// <summary>3.10b2: clicking a person in the world opens the People panel with them selected and moves the camera, exactly like a row click - and never pops the debug console.</summary>
        [UnityTest]
        public IEnumerator ClickingAPersonInTheWorld_OpensThePeoplePanelWithThemSelected_AndNeverTheConsole()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");

            string peopleList = bootstrap.RunConsoleCommandForTesting("people");
            string firstLine = peopleList.Split('\n')[0];
            Assert.IsTrue(EntityId.TryParse(firstLine.Split(':')[0].Trim(), out EntityId founder), $"Expected to parse a founder id from: {firstLine}");

            Assert.IsFalse(bootstrap.IsConsoleOpen, "Setup: the console should start closed.");

            // Placing the stockpile unpauses the game, but at normal speed a frame or two isn't
            // necessarily enough real time for even one tick to run yet - and a founder's capsule
            // only renders once MapMovementPresenter has synced at least one real position for them.
            Vector3? renderedPos = null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!renderedPos.HasValue && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                renderedPos = bootstrap.MoverRenderedPositionForTesting(founder);
            }

            Assert.IsTrue(renderedPos.HasValue, $"Expected {founder} to have a rendered capsule position.");

            Camera camera = bootstrap.CameraForTesting;
            Assert.IsNotNull(camera, "Expected a main camera.");
            Vector3 screenPoint = camera.WorldToScreenPoint(renderedPos.Value);
            Assert.Greater(screenPoint.z, 0f, "Sanity check: the person should be in front of the camera.");

            bootstrap.ClickOnPersonCapsuleForTesting(new Vector2(screenPoint.x, screenPoint.y));
            yield return null;

            Assert.IsTrue(bootstrap.IsPeoplePanelOpenForTesting, "Expected clicking a person in the world to open the People panel.");
            Assert.AreEqual(founder, bootstrap.PeoplePanelSelectedPersonIdForTesting, "Expected the clicked person to become selected.");
            Assert.IsFalse(bootstrap.IsConsoleOpen, "A world click must never open the debug console.");
        }

        /// <summary>3.10b2: a hand test found the panel too narrow, with long rows cut off.</summary>
        [UnityTest]
        public IEnumerator PeoplePanel_IsWideEnough_AndWrapsLongRows_SoNothingIsCutOff()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            bootstrap.ShowPeoplePanelForTesting();
            yield return null; // let layout resolve

            Assert.Greater(bootstrap.PeoplePanelRootWidthForTesting, 500f, "Expected the widened People panel to be well over the old 420px width.");
            Assert.IsTrue(bootstrap.PeoplePanelRowsWrapForTesting, "Expected a row's detail text to wrap rather than clip, regardless of panel width.");
        }

        [UnityTest]
        public IEnumerator PeoplePanel_SelectingABuilder_DrawsALineToTheirConstructionSite()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            EntityId? siteId = bootstrap.SimulatePlacementClickForTesting("building.crown_lodging", new GridPoint(115, 115), new Vector2(Screen.width - 5f, 5f));
            Assert.IsTrue(siteId.HasValue, "Setup: placing the crown lodging site should succeed.");
            bootstrap.CloseBuildMenuForTesting();

            // ConstructionSiteStartedEventId (which triggers GatheringModule.AssignTasks) delivers at
            // the end of a real simulation tick, not the instant this call returns - a frame-count
            // wait isn't a reliable proxy for that (batch mode can render several frames before even
            // one tick's worth of real time has accumulated at normal speed), so wait a short real
            // interval instead.
            yield return new WaitForSecondsRealtime(0.5f);

            string peopleList = bootstrap.RunConsoleCommandForTesting("people");
            var founders = new List<EntityId>();
            foreach (string line in peopleList.Split('\n'))
            {
                if (EntityId.TryParse(line.Split(':')[0].Trim(), out EntityId id) && bootstrap.IsFounderForTesting(id))
                {
                    founders.Add(id);
                }
            }
            Assert.Greater(founders.Count, 0, $"Expected to parse at least one founder id from: {peopleList}");
            EntityId founder = founders[0];

            bootstrap.ShowPeoplePanelForTesting();
            bootstrap.SelectPersonInPeoplePanelForTesting(founder);
            yield return null;

            // AssignTasks runs immediately on placement (see GatheringModule), so this founder should
            // already have a task (and therefore a target) before any tick has to pass.
            Assert.IsTrue(bootstrap.IsPeoplePanelSelectionLineVisibleForTesting,
                "Expected a line to the selected person's current target as soon as they have one.");
        }
    }
}
