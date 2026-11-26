using System;
using TheEmptying.Presentation.Menus;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.MainMenu
{
    /// <summary>
    /// Builds the main menu entirely in code, the same way GameBootstrap
    /// builds the HUD - a plain centered UI Toolkit panel, no scene-authored
    /// UI. Load stays disabled; it gets built later. Options opens the
    /// shared OptionsMenuPresenter, the same panel the in-game Escape menu
    /// uses.
    /// </summary>
    public sealed class MainMenuBootstrap : MonoBehaviour
    {
        private const string GameSceneName = "Game";

        private VisualElement _mainPanel;
        private OptionsMenuPresenter _options;

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
            UiPreferences.RegisterPanel(panelSettings);

            VisualElement root = uiDocument.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;

            _mainPanel = BuildMainPanel();
            root.Add(_mainPanel);

            _options = new OptionsMenuPresenter();
            _options.Initialize(onBack: () =>
            {
                _options.Close();
                _mainPanel.style.display = DisplayStyle.Flex;
            });
            root.Add(_options.Panel);

            VersionLabelPresenter.Build(root);
        }

        private void OnDestroy()
        {
            _options?.Dispose();
        }

        private VisualElement BuildMainPanel()
        {
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
            panel.Add(BuildMenuButton("Options", OpenOptions, enabled: true));
            panel.Add(BuildMenuButton("Exit", GameExit.Quit, enabled: true));

            return panel;
        }

        private void OpenOptions()
        {
            _mainPanel.style.display = DisplayStyle.None;
            _options.Open();
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
    }
}
