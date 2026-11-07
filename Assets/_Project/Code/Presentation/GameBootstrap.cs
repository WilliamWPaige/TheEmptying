using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core;
using TheEmptying.Core.Console;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// Milestone-1 bootstrap object: builds the Simulation through
    /// SimulationBuilder, drives the clock each frame against real time, and
    /// hosts a minimal HUD and developer console. Everything lives on one
    /// component for now; it splits into separate pieces once there's a
    /// gray-box town to justify it.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour, IPresentationCommands
    {
        private const ulong FixedWorldSeed = 1;
        private const double RealSecondsPerDay = 720; // 12 real minutes per in-game day at 1x.
        private const int MaxTicksPerFrame = 200;

        private Simulation _simulation;
        private double _accumulatedRealSeconds;
        private SimSpeed _speed = SimSpeed.Normal;
        private bool _paused;

        private InputActionAsset _actions;

        private UIDocument _hudDocument;
        private Label _dateLabel;
        private Label _speedLabel;

        private UIDocument _consoleDocument;
        private VisualElement _consoleRoot;
        private ScrollView _consoleLog;
        private TextField _consoleInput;
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex;
        private bool _consoleOpen;

        private string _coreContentFolder;
        private string _modsFolder;
        private string _savesFolder;

        public bool IsPaused => _paused;
        public int Speed => (int)_speed;

        /// <summary>For the PlayMode smoke test: confirms the clock is actually advancing.</summary>
        public long CurrentTick => _simulation.Clock.CurrentTick;

        private void Awake()
        {
            ResolveFolders();
            BuildSimulation();
            BuildHud();
            BuildConsole();
            WireInput();
        }

        private void Update()
        {
            if (_paused)
            {
                return;
            }

            _accumulatedRealSeconds += Time.unscaledDeltaTime;
            double ticksPerRealSecond = TickRateCalculator.TicksPerRealSecond(RealSecondsPerDay, _speed);
            int ticksToRun = TickRateCalculator.TicksToRun(_accumulatedRealSeconds, ticksPerRealSecond, MaxTicksPerFrame, out double leftover);
            _accumulatedRealSeconds = leftover;

            for (int i = 0; i < ticksToRun; i++)
            {
                _simulation.Step();
            }

            if (ticksToRun > 0)
            {
                RefreshHud();
            }
        }

        private void OnDestroy()
        {
            _actions?.Disable();
        }

        #region Startup

        private void ResolveFolders()
        {
            _coreContentFolder = Path.Combine(Application.streamingAssetsPath, "Content", "core");
            _modsFolder = Path.Combine(Application.persistentDataPath, "Mods");
            _savesFolder = Path.Combine(Application.persistentDataPath, "Saves");

            Directory.CreateDirectory(_modsFolder);
            Directory.CreateDirectory(_savesFolder);
        }

        private List<string> ResolveSourceFolders()
        {
            var sources = new List<string> { _coreContentFolder };

            if (Directory.Exists(_modsFolder))
            {
                sources.AddRange(Directory.GetDirectories(_modsFolder).OrderBy(d => d, StringComparer.Ordinal));
            }

            return sources;
        }

        private void BuildSimulation()
        {
            List<string> sources = ResolveSourceFolders();
            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(FixedWorldSeed, sources, Array.Empty<IModule>(), ErrorMode.Development);
            _simulation = simulation;

            BuiltInConsoleCommands.RegisterAll(_simulation, this);

            Debug.Log($"[TheEmptying] Startup report:\n{report}");
        }

        #endregion

        #region HUD

        private void BuildHud()
        {
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(transform);
            _hudDocument = hudGo.AddComponent<UIDocument>();
            _hudDocument.panelSettings = panelSettings;

            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 8,
                    left = 8,
                    flexDirection = FlexDirection.Row,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = new Color(0f, 0f, 0f, 0.5f)
                }
            };

            _dateLabel = new Label { style = { color = Color.white, marginRight = 16 } };
            _speedLabel = new Label { style = { color = Color.white } };

            panel.Add(_dateLabel);
            panel.Add(_speedLabel);
            _hudDocument.rootVisualElement.Add(panel);

            RefreshHud();
        }

        private void RefreshHud()
        {
            if (_dateLabel == null)
            {
                return;
            }

            _dateLabel.text = _simulation.Clock.CurrentDate.ToString();
            _speedLabel.text = _paused ? "Paused" : $"{(int)_speed}x";
        }

        #endregion

        #region Console

        private void BuildConsole()
        {
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);

            var consoleGo = new GameObject("Console");
            consoleGo.transform.SetParent(transform);
            _consoleDocument = consoleGo.AddComponent<UIDocument>();
            _consoleDocument.panelSettings = panelSettings;

            _consoleRoot = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    height = Length.Percent(50),
                    backgroundColor = new Color(0f, 0f, 0f, 0.85f),
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 8,
                    paddingBottom = 8
                }
            };

            _consoleLog = new ScrollView { style = { flexGrow = 1 } };
            _consoleInput = new TextField { style = { marginTop = 4 } };

            _consoleRoot.Add(_consoleLog);
            _consoleRoot.Add(_consoleInput);
            _consoleDocument.rootVisualElement.Add(_consoleRoot);

            _consoleInput.RegisterCallback<KeyDownEvent>(OnConsoleKeyDown, TrickleDown.TrickleDown);

            SetConsoleOpen(false);
        }

        private void OnConsoleKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    RunConsoleInput();
                    evt.StopPropagation();
                    break;
                case KeyCode.UpArrow:
                    RecallHistory(-1);
                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                    RecallHistory(1);
                    evt.StopPropagation();
                    break;
            }
        }

        private void RunConsoleInput()
        {
            string input = _consoleInput.value;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            _commandHistory.Add(input);
            _historyIndex = _commandHistory.Count;

            AppendLog($"> {input}");
            AppendLog(_simulation.Commands.Execute(input));

            _consoleInput.SetValueWithoutNotify(string.Empty);
        }

        private void RecallHistory(int direction)
        {
            if (_commandHistory.Count == 0)
            {
                return;
            }

            _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, _commandHistory.Count - 1);
            _consoleInput.SetValueWithoutNotify(_commandHistory[_historyIndex]);
        }

        private void AppendLog(string text)
        {
            var label = new Label(text) { style = { color = Color.white, whiteSpace = WhiteSpace.Normal } };
            _consoleLog.Add(label);
            _consoleLog.ScrollTo(label);
        }

        private void SetConsoleOpen(bool open)
        {
            _consoleOpen = open;
            _consoleRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (open)
            {
                _consoleInput.Focus();
            }
        }

        #endregion

        #region Input

        private void WireInput()
        {
            var asset = Resources.Load<InputActionAsset>("TheEmptyingControls");
            if (asset == null)
            {
                Debug.LogError("[TheEmptying] Could not load TheEmptyingControls input actions from Resources.");
                return;
            }

            _actions = Instantiate(asset);
            InputActionMap map = _actions.FindActionMap("Gameplay");

            map.FindAction("TogglePause").performed += _ => { if (!_consoleOpen) SetPaused(!_paused); };
            map.FindAction("SetSpeed1").performed += _ => { if (!_consoleOpen) SetSpeed(1); };
            map.FindAction("SetSpeed2").performed += _ => { if (!_consoleOpen) SetSpeed(2); };
            map.FindAction("SetSpeed3").performed += _ => { if (!_consoleOpen) SetSpeed(3); };
            map.FindAction("ToggleConsole").performed += _ => SetConsoleOpen(!_consoleOpen);

            _actions.Enable();
        }

        #endregion

        #region IPresentationCommands

        public void SetPaused(bool paused)
        {
            _paused = paused;
            RefreshHud();
        }

        public void SetSpeed(int speed)
        {
            _speed = (SimSpeed)Mathf.Clamp(speed, 1, 3);
            RefreshHud();
        }

        public string Save(string name)
        {
            try
            {
                string path = Path.Combine(_savesFolder, name + ".sav");
                using var stream = File.Create(path);
                var headerInfo = new SaveHeaderInfo
                {
                    KingdomName = "Kingdom",
                    Population = _simulation.Entities.Count,
                    Sources = SourceManifestReader.ReadAll(ResolveSourceFolders())
                };

                GameSave.Save(_simulation, headerInfo, stream);
                return $"Saved to '{path}'.";
            }
            catch (Exception ex)
            {
                return $"Save failed: {ex.Message}";
            }
        }

        public string Load(string name)
        {
            try
            {
                string path = Path.Combine(_savesFolder, name + ".sav");
                using var stream = File.OpenRead(path);
                (Simulation loaded, LoadReport report) = GameSave.Load(stream, ResolveSourceFolders(), Array.Empty<IModule>(), ErrorMode.Development);

                _simulation = loaded;
                BuiltInConsoleCommands.RegisterAll(_simulation, this);

                string differences = report.SourceDifferences.Count == 0 ? "no source differences." : string.Join("\n", report.SourceDifferences);
                return $"Loaded '{path}'.\n{differences}";
            }
            catch (Exception ex)
            {
                return $"Load failed: {ex.Message}";
            }
        }

        #endregion
    }
}
