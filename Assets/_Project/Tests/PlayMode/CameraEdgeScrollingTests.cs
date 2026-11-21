using System.Collections;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using TheEmptying.Presentation.MapMovement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>3.9d: edge scrolling is off by default, kept behind setting.camera.edge_scrolling for later.</summary>
    public class CameraEdgeScrollingTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator EdgeScrolling_IsOffByDefault_ButPansOnceTheSettingIsTurnedOn()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            var cameraController = Object.FindFirstObjectByType<StrategyCameraController>();
            Assert.IsNotNull(cameraController, "Expected a StrategyCameraController on the main camera.");

            Move(mouse.position, new Vector2(1f, Screen.height * 0.5f)); // left screen edge
            yield return null;

            Vector3 positionBefore = cameraController.transform.position;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(positionBefore, cameraController.transform.position,
                "Edge scrolling should be off by default - hovering the screen edge should not pan the camera.");

            bootstrap.RunConsoleCommandForTesting($"set {StrategyCameraController.EdgeScrollingSettingId} 1");

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreNotEqual(positionBefore, cameraController.transform.position,
                "With the setting turned on, hovering the screen edge should pan the camera.");
        }
    }
}
