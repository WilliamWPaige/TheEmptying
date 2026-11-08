using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private const int MaxLogLines = 500;
        private const float ConsoleGapBelowHud = 8f;
        private static readonly Color ConsoleBackgroundColorValue = new Color(0.05f, 0.05f, 0.06f, 1f);

        private enum LogFilterMode
        {
            Off,
            ErrorsAndWarnings,
            All
        }

        private Simulation _simulation;
        private double _accumulatedRealSeconds;
        private SimSpeed _speed = SimSpeed.Normal;
        private bool _paused;

        private InputActionAsset _actions;

        private UIDocument _uiDocument;
        private VisualElement _hudPanel;
        private Label _dateLabel;
        private Label _speedLabel;

        private VisualElement _consoleRoot;
        private ScrollView _consoleLog;
        private TextField _consoleInput;
        private VisualElement _consoleInputTextBox;
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex;
        private bool _consoleOpen;
        private bool _scrollLogToBottomPending;

        private LogFilterMode _logFilterMode = Application.isEditor ? LogFilterMode.All : LogFilterMode.Off;

        private string _coreContentFolder;
        private string _modsFolder;
        private string _savesFolder;

        public bool IsPaused => _paused;
        public int Speed => (int)_speed;

        /// <summary>For the PlayMode smoke test: confirms the clock is actually advancing.</summary>
        public long CurrentTick => _simulation.Clock.CurrentTick;

        /// <summary>For tests: whether the console is currently open.</summary>
        public bool IsConsoleOpen => _consoleOpen;

        /// <summary>For tests: the resolved layout (post-layout-pass) of the HUD, the console root, the output log, and the input line.</summary>
        public Rect HudLayout => _hudPanel.layout;
        public Rect ConsoleRootLayout => _consoleRoot.layout;
        public Rect ConsoleLogLayout => _consoleLog.layout;
        public Rect ConsoleInputLayout => _consoleInput.layout;

        /// <summary>For tests: the console root's resolved background color.</summary>
        public Color ConsoleBackgroundColor => _consoleRoot.resolvedStyle.backgroundColor;

        /// <summary>For tests: the log's full scrollable content height versus its visible viewport height.</summary>
        public float ConsoleLogContentHeight => _consoleLog.contentContainer.layout.height;
        public float ConsoleLogViewportHeight => _consoleLog.contentViewport.layout.height;

        /// <summary>For tests: the input line's current text.</summary>
        public string ConsoleInputValue => _consoleInput.value;

        /// <summary>For tests: the resolved colors of the input's inner text box, to check they contrast.</summary>
        public Color ConsoleInputTextColor => _consoleInputTextBox.resolvedStyle.color;
        public Color ConsoleInputBackgroundColor => _consoleInputTextBox.resolvedStyle.backgroundColor;

        /// <summary>For tests: every line currently in the console log, with its resolved color.</summary>
        public IEnumerable<(string Text, Color Color)> ConsoleLogEntries =>
            _consoleLog.contentContainer.Children().OfType<Label>().Select(l => (l.text, l.resolvedStyle.color));

        /// <summary>For tests: the log's current and maximum vertical scroll offset.</summary>
        public float ConsoleLogScrollOffsetY => _consoleLog.scrollOffset.y;
        public float ConsoleLogMaxScrollOffsetY => Mathf.Max(0f, _consoleLog.contentContainer.layout.height - _consoleLog.contentViewport.layout.height);

        /// <summary>For tests: whether the console input (or its inner text box) currently has focus.</summary>
        public bool IsConsoleInputFocused =>
            _consoleInput.panel != null &&
            _consoleInput.panel.focusController.focusedElement != null &&
            (_consoleInput.panel.focusController.focusedElement == _consoleInput ||
             _consoleInput.panel.focusController.focusedElement == _consoleInputTextBox);

        /// <summary>For tests: runs a command exactly as typing it and pressing Enter would, returning its result text.</summary>
        public string RunConsoleCommandForTesting(string input)
        {
            _consoleInput.SetValueWithoutNotify(input);
            return RunConsoleInput();
        }

        /// <summary>For tests: the simulation's current living entity count.</summary>
        public int EntityCountForTesting => _simulation.Entities.Count;

        /// <summary>For tests: the simulation's current state hash.</summary>
        public ulong StateHashForTesting => _simulation.ComputeStateHash();

        /// <summary>For tests: appends throwaway lines to the log without going through a real command.</summary>
        public void AppendLogLinesForTesting(int count)
        {
            for (int i = 0; i < count; i++)
            {
                AppendLog($"test line {i}");
            }
        }

        private void Awake()
        {
            ResolveFolders();
            StartupReport report = BuildSimulation();
            BuildUi();
            WireInput();

            Application.logMessageReceived += OnUnityLogMessage;
            AppendLog($"Startup report:\n{report}");
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
            Application.logMessageReceived -= OnUnityLogMessage;
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

        private StartupReport BuildSimulation()
        {
            List<string> sources = ResolveSourceFolders();
            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(FixedWorldSeed, sources, Array.Empty<IModule>(), ErrorMode.Development);
            _simulation = simulation;

            BuiltInConsoleCommands.RegisterAll(_simulation, this);

            Debug.Log($"[TheEmptying] Startup report:\n{report}");
            return report;
        }

        #endregion

        #region UI

        /// <summary>
        /// One UIDocument hosts both the HUD and the console, so draw order
        /// between them is just child order (the console is added after the
        /// HUD, so it draws above it) instead of relying on separate panels'
        /// sort order.
        /// </summary>
        private void BuildUi()
        {
            var panelSettings = Resources.Load<PanelSettings>("GamePanelSettings");
            if (panelSettings == null)
            {
                Debug.LogError("[TheEmptying] Could not load GamePanelSettings from Resources. UI will not render.");
                return;
            }

            _uiDocument = gameObject.AddComponent<UIDocument>();
            _uiDocument.panelSettings = panelSettings;

            // The root element's own Yoga style defaults to width/height: auto, which
            // shrinks to fit its (non-absolute) children rather than filling the
            // panel. Every percentage size below is resolved against this element,
            // so it must be stretched to the panel's actual size first.
            VisualElement root = _uiDocument.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            // Column flow, rather than absolute positioning for every child, means
            // the console is placed after wherever the HUD actually ends up, so
            // the two can never overlap regardless of the HUD's real height.
            root.style.flexDirection = FlexDirection.Column;

            BuildHud(root);
            BuildConsole(root);
        }

        private void BuildHud(VisualElement root)
        {
            _hudPanel = new VisualElement
            {
                style =
                {
                    // The column root stretches children to its full width by
                    // default; opt this one back out so it just hugs its labels.
                    alignSelf = Align.FlexStart,
                    marginTop = 8,
                    marginLeft = 8,
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

            _hudPanel.Add(_dateLabel);
            _hudPanel.Add(_speedLabel);
            root.Add(_hudPanel);

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

        private void BuildConsole(VisualElement root)
        {
            _consoleRoot = new VisualElement
            {
                style =
                {
                    // Normal flow, placed right after the HUD in the column root,
                    // plus a small gap, so the two never overlap.
                    marginTop = ConsoleGapBelowHud,
                    width = Length.Percent(100),
                    height = Length.Percent(40),
                    overflow = Overflow.Hidden,
                    backgroundColor = ConsoleBackgroundColorValue,
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 8,
                    paddingBottom = 8
                }
            };

            _consoleLog = new ScrollView
            {
                style =
                {
                    flexGrow = 1,
                    minHeight = 80,
                    overflow = Overflow.Hidden,
                    color = Color.white,
                    backgroundColor = new Color(1f, 1f, 1f, 0.05f)
                }
            };

            // Without a theme style sheet, the viewport's own default clipping rule
            // (normally supplied by the theme) doesn't apply; set it explicitly so
            // log content can never draw outside the scroll view, theme or no theme.
            _consoleLog.contentViewport.style.overflow = Overflow.Hidden;

            // The content container's size only becomes real once a layout pass
            // has actually measured the newly added line(s); retry the pending
            // scroll-to-bottom request right when that happens.
            _consoleLog.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => TryScrollLogToBottomNow());

            _consoleInput = new TextField
            {
                style =
                {
                    flexShrink = 0,
                    height = 32,
                    marginTop = 4
                }
            };

            // The default theme styles the TextField's inner text box (the
            // "unity-text-input" element), overriding colors set on the outer
            // TextField itself; style that inner element directly so typed
            // text is actually readable.
            _consoleInputTextBox = _consoleInput.Q("unity-text-input");
            if (_consoleInputTextBox != null)
            {
                _consoleInputTextBox.style.color = Color.white;
                _consoleInputTextBox.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
                var borderColor = new Color(1f, 1f, 1f, 0.35f);
                _consoleInputTextBox.style.borderTopWidth = 1;
                _consoleInputTextBox.style.borderBottomWidth = 1;
                _consoleInputTextBox.style.borderLeftWidth = 1;
                _consoleInputTextBox.style.borderRightWidth = 1;
                _consoleInputTextBox.style.borderTopColor = borderColor;
                _consoleInputTextBox.style.borderBottomColor = borderColor;
                _consoleInputTextBox.style.borderLeftColor = borderColor;
                _consoleInputTextBox.style.borderRightColor = borderColor;
            }
            else
            {
                Debug.LogError("[TheEmptying] Could not find the console input's inner text box ('unity-text-input'); typed text may be unreadable.");
            }

            _consoleRoot.Add(_consoleLog);
            _consoleRoot.Add(_consoleInput);
            root.Add(_consoleRoot);

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
                    evt.PreventDefault();
                    break;
                case KeyCode.UpArrow:
                    RecallHistory(-1);
                    evt.StopPropagation();
                    evt.PreventDefault();
                    break;
                case KeyCode.DownArrow:
                    RecallHistory(1);
                    evt.StopPropagation();
                    evt.PreventDefault();
                    break;
                case KeyCode.BackQuote:
                case KeyCode.F1:
                    // These toggle the console itself (via the Input System action,
                    // handled separately); never let them type into the field.
                    evt.StopPropagation();
                    evt.PreventDefault();
                    break;
            }
        }

        /// <summary>Runs whatever's currently typed, as if Enter had been pressed. Returns the command's result text (null if the input was blank).</summary>
        private string RunConsoleInput()
        {
            string input = _consoleInput.value;
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            _commandHistory.Add(input);
            _historyIndex = _commandHistory.Count;

            // Running a command always jumps the log to the bottom, even if the
            // player had scrolled up to read older lines.
            AppendLog($"> {input}", Color.white, forceScrollToBottom: true);
            string result = _simulation.Commands.Execute(input);
            AppendLog(result, Color.white, forceScrollToBottom: true);

            _consoleInput.SetValueWithoutNotify(string.Empty);
            RefocusConsoleInput();
            return result;
        }

        private void RecallHistory(int direction)
        {
            if (_commandHistory.Count == 0)
            {
                return;
            }

            _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, _commandHistory.Count - 1);
            _consoleInput.SetValueWithoutNotify(_commandHistory[_historyIndex]);
            RefocusConsoleInput();
        }

        /// <summary>Keeps the input ready to type into without clicking: deferred one step, since focusing the same frame the value changed isn't reliable.</summary>
        private void RefocusConsoleInput()
        {
            _consoleInput.schedule.Execute(() => _consoleInput.Focus());
        }

        private void AppendLog(string text) => AppendLog(text, Color.white, forceScrollToBottom: false);

        private void AppendLog(string text, Color color) => AppendLog(text, color, forceScrollToBottom: false);

        private void AppendLog(string text, Color color, bool forceScrollToBottom)
        {
            bool wasAtBottom = IsConsoleLogScrolledToBottom();

            var label = new Label(text)
            {
                style =
                {
                    position = Position.Relative,
                    color = color,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            _consoleLog.Add(label);

            // Cap the log so it can't grow without limit; drop the oldest lines first.
            while (_consoleLog.contentContainer.childCount > MaxLogLines)
            {
                _consoleLog.contentContainer.RemoveAt(0);
            }

            // Running a command always follows to the bottom; a log line arriving
            // on its own only follows if the player was already at the bottom, so
            // it doesn't yank them away from older lines they scrolled up to read.
            if (forceScrollToBottom || wasAtBottom)
            {
                RequestScrollLogToBottom();
            }
        }

        /// <summary>
        /// Flags that the log should end up at the true bottom, then tries right
        /// away (in case the content's size already reflects it). The content
        /// container's own GeometryChangedEvent handler (wired once in
        /// BuildConsole) retries whenever a layout pass actually changes its
        /// size, which is when a just-added line's height first becomes real -
        /// including when several lines were added in the same frame.
        /// </summary>
        private void RequestScrollLogToBottom()
        {
            _scrollLogToBottomPending = true;
            TryScrollLogToBottomNow();
        }

        private void TryScrollLogToBottomNow()
        {
            if (!_scrollLogToBottomPending)
            {
                return;
            }

            // The scroller's own highValue isn't reliably in sync with the content
            // container's real size at this point; computing the maximum directly
            // from the resolved layout (the same formula IsConsoleLogScrolledToBottom
            // uses) is what actually reflects the true bottom.
            float maxScroll = Mathf.Max(0f, _consoleLog.contentContainer.layout.height - _consoleLog.contentViewport.layout.height);
            _consoleLog.scrollOffset = new Vector2(_consoleLog.scrollOffset.x, maxScroll);
            _scrollLogToBottomPending = false;
        }

        private bool IsConsoleLogScrolledToBottom()
        {
            float maxScroll = Mathf.Max(0f, _consoleLog.contentContainer.layout.height - _consoleLog.contentViewport.layout.height);
            return _consoleLog.scrollOffset.y >= maxScroll - 1f;
        }

        /// <summary>Mirrors Unity's own log messages into the console, color-coded and prefixed with the in-game time, subject to the current log filter.</summary>
        private void OnUnityLogMessage(string message, string stackTrace, LogType type)
        {
            if (_logFilterMode == LogFilterMode.Off)
            {
                return;
            }

            bool isProblem = type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (_logFilterMode == LogFilterMode.ErrorsAndWarnings && !isProblem)
            {
                return;
            }

            Color color = Color.white;
            string text = message;

            if (type == LogType.Warning)
            {
                color = Color.yellow;
            }
            else if (isProblem)
            {
                color = new Color(1f, 0.35f, 0.35f);
                string firstStackLine = string.IsNullOrEmpty(stackTrace) ? null : stackTrace.Split('\n')[0].TrimEnd('\r');
                if (!string.IsNullOrEmpty(firstStackLine))
                {
                    text = $"{text}\n{firstStackLine}";
                }
            }

            AppendLog($"[{_simulation.Clock.CurrentDate}] {text}", color);
        }

        /// <summary>The single entry point for opening/closing the console, so the input action and the PlayMode test both go through the same code.</summary>
        public void ToggleConsole()
        {
            SetConsoleOpen(!_consoleOpen);
        }

        private void SetConsoleOpen(bool open)
        {
            _consoleOpen = open;
            _consoleRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (open)
            {
                // Safety net: if the key that opened the console (or anything else)
                // left a stray character in the field, start clean every time.
                _consoleInput.SetValueWithoutNotify(string.Empty);

                // Focusing the same frame display flips to Flex can run before the
                // panel has a laid-out element to focus; deferring one step is reliable.
                _consoleInput.schedule.Execute(() => _consoleInput.Focus());
            }
            else
            {
                // Release focus so the hidden field can't go on receiving and
                // inserting keystrokes meant for toggling the console back open.
                _consoleInput.Blur();
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
            map.FindAction("ToggleConsole").performed += _ => ToggleConsole();

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
                    Sources = SourceManifestReader.ReadAll(ResolveSourceFolders()),
                    SavedAtUtc = DateTime.UtcNow
                };

                GameSave.Save(_simulation, headerInfo, stream);
                return $"Saved to '{path}' ({_simulation.Clock.CurrentDate}).";
            }
            catch (Exception ex)
            {
                return $"Save failed: {ex.Message}";
            }
        }

        public string Load(string name)
        {
            string path = Path.Combine(_savesFolder, name + ".sav");
            if (!File.Exists(path))
            {
                return $"No save named '{name}'.\nAvailable saves:\n{ListSaves()}";
            }

            try
            {
                SaveHeader header;
                using (var headerStream = File.OpenRead(path))
                {
                    header = SaveFile.ReadHeader(headerStream);
                }

                using var stream = File.OpenRead(path);
                (Simulation loaded, LoadReport report) = GameSave.Load(stream, ResolveSourceFolders(), Array.Empty<IModule>(), ErrorMode.Development);

                _simulation = loaded;
                BuiltInConsoleCommands.RegisterAll(_simulation, this);

                var sb = new StringBuilder();
                sb.Append($"Loaded '{name}': {header.KingdomName}, {header.InGameDate}, tick {header.CurrentTick}, population {header.Population}.");

                var warnings = new List<string>(report.SourceDifferences);
                warnings.AddRange(report.NewerSectionVersions);
                if (report.ModulesStartingFresh.Count > 0)
                {
                    warnings.Add($"Modules starting fresh: {string.Join(", ", report.ModulesStartingFresh)}");
                }

                if (warnings.Count > 0)
                {
                    sb.Append("\nWarnings:");
                    foreach (string warning in warnings)
                    {
                        sb.Append("\n  ").Append(warning);
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Load failed: {ex.Message}";
            }
        }

        public string ListSaves()
        {
            if (!Directory.Exists(_savesFolder))
            {
                return "(no saves)";
            }

            string[] files = Directory.GetFiles(_savesFolder, "*.sav").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0)
            {
                return "(no saves)";
            }

            var lines = new List<string>();
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                try
                {
                    using var stream = File.OpenRead(file);
                    SaveHeader header = SaveFile.ReadHeader(stream);
                    lines.Add($"{name} - saved {header.SavedAtUtc:yyyy-MM-dd HH:mm:ss} UTC, in-game date {header.InGameDate}");
                }
                catch (Exception ex)
                {
                    lines.Add($"{name} - unreadable ({ex.Message})");
                }
            }

            return string.Join("\n", lines);
        }

        public string SetLogFilter(string mode)
        {
            switch (mode)
            {
                case "on":
                    _logFilterMode = LogFilterMode.All;
                    return "Logs on.";
                case "off":
                    _logFilterMode = LogFilterMode.Off;
                    return "Logs off.";
                case "errors":
                    _logFilterMode = LogFilterMode.ErrorsAndWarnings;
                    return "Logs limited to errors and warnings.";
                default:
                    return "Usage: logs <on|off|errors>";
            }
        }

        #endregion
    }
}
