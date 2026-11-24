using System;
using TheEmptying.Core.Console;
using TheEmptying.Presentation.DevTools;
using TheEmptying.Presentation.Discovery;
using TheEmptying.Presentation.Selection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Menus
{
    /// <summary>
    /// The in-game Escape menu: Resume, Main Menu, Options, Exit Game, plus
    /// the Yes/No confirmations for the two choices that lose the game.
    /// While open the game is paused and the camera is frozen; closing it
    /// restores whatever pause state was in effect before it opened. Escape
    /// itself is handled by GameBootstrap, which calls HandleEscapePressed
    /// only after ruling out a focused text field and an open debug window
    /// (see its own ordering comment) - this class only ever has to decide
    /// between "go back one step" and "open or close".
    /// </summary>
    public sealed class EscapeMenuPresenter : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";

        private IPresentationCommands _presentation;
        private DebugWindowPresenter _debugWindow;
        private WorldSelectionPresenter _worldSelection;
        private DiscoveriesWindowPresenter _discoveriesWindow;

        private VisualElement _root;
        private VisualElement _mainPanel;
        private VisualElement _confirmPanel;
        private Label _confirmLabel;
        private Action _confirmYesAction;
        private OptionsMenuPresenter _options;

        private bool _pausedBeforeOpen;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        public void Initialize(
            VisualElement uiRoot,
            IPresentationCommands presentation,
            DebugWindowPresenter debugWindow,
            WorldSelectionPresenter worldSelection,
            DiscoveriesWindowPresenter discoveriesWindow = null)
        {
            _presentation = presentation;
            _debugWindow = debugWindow;
            _worldSelection = worldSelection;
            _discoveriesWindow = discoveriesWindow;

            _root = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    display = DisplayStyle.None
                }
            };

            _mainPanel = BuildMainPanel();
            _root.Add(_mainPanel);

            _options = new OptionsMenuPresenter();
            _options.Initialize(onBack: () =>
            {
                _options.Close();
                _mainPanel.style.display = DisplayStyle.Flex;
            });
            _root.Add(_options.Panel);

            _confirmPanel = BuildConfirmPanel();
            _root.Add(_confirmPanel);

            uiRoot.Add(_root);
        }

        private void OnDestroy()
        {
            _options?.Dispose();
        }

        public void Open()
        {
            _pausedBeforeOpen = _presentation.IsPaused;
            _presentation.SetPaused(true);

            _debugWindow?.ForceClose();
            _worldSelection?.ForceCloseForModal();
            _discoveriesWindow?.ForceCloseForModal();

            _mainPanel.style.display = DisplayStyle.Flex;
            _options.Close();
            _confirmPanel.style.display = DisplayStyle.None;
            _root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            _root.style.display = DisplayStyle.None;

            _presentation.SetPaused(_pausedBeforeOpen);

            _debugWindow?.ForceReopen();
            _worldSelection?.ForceReopenAfterModal();
            _discoveriesWindow?.ForceReopenAfterModal();
        }

        /// <summary>Go back one step if Options or a Yes/No question is open; otherwise open or close this menu.</summary>
        public void HandleEscapePressed()
        {
            if (_confirmPanel.style.display == DisplayStyle.Flex)
            {
                CloseConfirm();
                return;
            }

            if (_options.IsOpen)
            {
                _options.Close();
                _mainPanel.style.display = DisplayStyle.Flex;
                return;
            }

            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
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

            panel.Add(BuildButton("Resume", Close));
            panel.Add(BuildButton("Main Menu", () => ShowConfirm("Return to the main menu? This game will be lost.", ReturnToMainMenu)));
            panel.Add(BuildButton("Options", OpenOptions));
            panel.Add(BuildButton("Exit Game", () => ShowConfirm("Exit the game? This game will be lost.", GameExit.Quit)));

            return panel;
        }

        private void OpenOptions()
        {
            _mainPanel.style.display = DisplayStyle.None;
            _options.Open();
        }

        private VisualElement BuildConfirmPanel()
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
                    backgroundColor = new Color(0f, 0f, 0f, 0.5f),
                    display = DisplayStyle.None
                }
            };

            _confirmLabel = new Label
            {
                style =
                {
                    color = Color.white,
                    marginBottom = 16,
                    whiteSpace = WhiteSpace.Normal,
                    maxWidth = 320
                }
            };
            panel.Add(_confirmLabel);

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttonRow.Add(BuildButton("Yes", () =>
            {
                CloseConfirm();
                _confirmYesAction?.Invoke();
            }, marginRight: 8));
            buttonRow.Add(BuildButton("No", CloseConfirm));
            panel.Add(buttonRow);

            return panel;
        }

        private void ShowConfirm(string message, Action onYes)
        {
            _confirmLabel.text = message;
            _confirmYesAction = onYes;
            _mainPanel.style.display = DisplayStyle.None;
            _confirmPanel.style.display = DisplayStyle.Flex;
        }

        private void CloseConfirm()
        {
            _confirmPanel.style.display = DisplayStyle.None;
            _mainPanel.style.display = DisplayStyle.Flex;
        }

        private static void ReturnToMainMenu()
        {
            SceneManager.LoadScene(MainMenuSceneName);
        }

        private static Button BuildButton(string text, Action onClick, float marginRight = 0f)
        {
            return new Button(onClick) { text = text, style = { width = 200, height = 36, marginBottom = 8, marginRight = marginRight } };
        }
    }
}
