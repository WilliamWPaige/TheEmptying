using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TheEmptying.Presentation.MainMenu
{
    /// <summary>
    /// Builds the main menu entirely in code, the same way GameBootstrap
    /// builds the HUD - a plain centered UI Toolkit panel, no scene-authored
    /// UI. Load and Options are shown but disabled; they get built later.
    /// </summary>
    public sealed class MainMenuBootstrap : MonoBehaviour
    {
        private const string GameSceneName = "Game";

        private void Awake()
        {
            var panelSettings = Resources.Load<PanelSettings>("GamePanelSettings");
            if (panelSettings == null)
            {
                Debug.LogError("[TheEmptying] Could not load GamePanelSettings from Resources. The main menu will not render.");
                return;
            }

            var uiDocument = gameObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = panelSettings;

            VisualElement root = uiDocument.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;

            var panel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    paddingLeft = 32,
                    paddingRight = 32,
                    paddingTop = 24,
                    paddingBottom = 24,
                    backgroundColor = new Color(0f, 0f, 0f, 0.5f)
                }
            };

            var title = new Label("The Emptying")
            {
                style =
                {
                    color = Color.white,
                    fontSize = 32,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 24
                }
            };
            panel.Add(title);

            panel.Add(BuildMenuButton("Start", StartGame, enabled: true));
            panel.Add(BuildMenuButton("Load", null, enabled: false));
            panel.Add(BuildMenuButton("Options", null, enabled: false));
            panel.Add(BuildMenuButton("Exit", ExitGame, enabled: true));

            root.Add(panel);
        }

        private static Button BuildMenuButton(string text, Action onClick, bool enabled)
        {
            var button = new Button(onClick)
            {
                text = text,
                style =
                {
                    width = 200,
                    height = 36,
                    marginBottom = 8
                }
            };
            button.SetEnabled(enabled);
            return button;
        }

        private static void StartGame()
        {
            SceneManager.LoadScene(GameSceneName);
        }

        private static void ExitGame()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
