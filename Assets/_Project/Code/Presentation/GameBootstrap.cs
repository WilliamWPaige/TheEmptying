using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TheEmptying.Core;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.DevTools;
using TheEmptying.Presentation.Menus;
using TheEmptying.Presentation.People;
using TheEmptying.Presentation.Selection;
using TheEmptying.Presentation.Startup;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// Milestone-1 bootstrap object: builds the Simulation through
    /// SimulationBuilder, drives the clock each frame against real time, and
    /// hosts a minimal HUD and the rule system's question popup. Everything
    /// lives on one component for now; it splits into separate pieces once
    /// there's a gray-box town to justify it.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour, IPresentationCommands, ILogSink
    {
        private const ulong FixedWorldSeed = 1;
        private const string RealSecondsPerDaySettingId = "setting.time.real_seconds_per_day";
        private const int MaxTicksPerFrame = 200;

        private Simulation _simulation;
        private double _accumulatedRealSeconds;
        private SimSpeed _speed = SimSpeed.Normal;
        private bool _paused;

        /// <summary>
        /// For tests only: multiplies the real time fed into the tick accumulator, so a long
        /// real-time PlayMode test can cover the same in-game days in far less wall-clock time while
        /// still playing the real Game scene at a real game speed. 1 (no change) outside tests.
        /// </summary>
        private double _testTimeMultiplier = 1.0;

        private UIDocument _uiDocument;
        private VisualElement _hudPanel;
        private Label _dateLabel;
        private Label _speedLabel;

        private VisualElement _questionPopupRoot;
        private Label _questionPopupCounter;
        private Label _questionPopupTitle;
        private Label _questionPopupBody;
        private VisualElement _questionPopupChoices;
        private readonly List<Action> _questionPopupChoiceActions = new List<Action>();
        private long _questionPopupShownQuestionId = -1;

        /// <summary>
        /// Questions the player has already clicked a choice for, but whose
        /// answer hasn't been applied yet (that happens at the start of the
        /// next real tick). The popup treats these as already resolved; an id
        /// is dropped once it leaves RuleRunner.PendingQuestions.
        /// </summary>
        private readonly HashSet<long> _answeredQuestionIds = new HashSet<long>();

        private string _coreContentFolder;
        private string _modsFolder;
        private string _savesFolder;

        private InputActionAsset _actions;
        private StrategyCameraController _cameraController;

        private readonly FloatingOrigin _floatingOrigin = new FloatingOrigin();
        private MapModule _mapModule;
        private WorldMapView _worldMapView;
        private MapCursorPresenter _mapCursor;
        private PeopleModule _peopleModule;
        private PersonPresenter _personPresenter;
        private WorldSelectionPresenter _worldSelection;

        private readonly CheatMenuRegistry _cheatMenu = new CheatMenuRegistry();
        private DebugWindowPresenter _debugWindow;
        private EscapeMenuPresenter _escapeMenu;
        private SpawnCheat _spawnCheat;
        private VisualElement _errorBadge;
        private Label _errorBadgeErrorsLabel;
        private Label _errorBadgeWarningsLabel;

        /// <summary>R9: the short on-screen message for Quick save/load ("Saved", "Loaded" or an error) - shown for ToastSeconds, then hidden.</summary>
        private const float ToastSeconds = 2.5f;
        private const string QuickSaveName = "quicksave";
        private VisualElement _toastWrapper;
        private Label _toastLabel;
        private float _toastHideAtUnscaledTime = -1f;

        public bool IsPaused => _paused;
        public int Speed => (int)_speed;

        /// <summary>For the PlayMode smoke test: confirms the clock is actually advancing.</summary>
        public long CurrentTick => _simulation.Clock.CurrentTick;

#if UNITY_EDITOR
        /// <summary>
        /// The game's currently-running simulation, for editor tools only (the
        /// rule editor's live trace panel). Never referenced by anything that
        /// ships in a build.
        /// </summary>
        public Simulation SimulationForEditorTools => _simulation;
#endif

        /// <summary>For tests: the simulation's current living entity count.</summary>
        public int EntityCountForTesting => _simulation.Entities.Count;

        /// <summary>For tests: the simulation's current state hash.</summary>
        public ulong StateHashForTesting => _simulation.ComputeStateHash();

        /// <summary>For tests: whether the question popup is currently shown.</summary>
        public bool IsQuestionPopupOpen => _questionPopupRoot.style.display == DisplayStyle.Flex;

        /// <summary>For tests: the currently shown question's title and body.</summary>
        public string QuestionPopupTitle => _questionPopupTitle.text;
        public string QuestionPopupBody => _questionPopupBody.text;

        /// <summary>For tests: how many choice buttons the popup currently shows.</summary>
        public int QuestionPopupChoiceCount => _questionPopupChoices.childCount;

        /// <summary>For tests: runs the same action a real click on one of the popup's choice buttons would.</summary>
        public void ClickQuestionPopupChoiceForTesting(int choiceNumber)
        {
            if (choiceNumber < 1 || choiceNumber > _questionPopupChoiceActions.Count)
            {
                return;
            }

            _questionPopupChoiceActions[choiceNumber - 1]();
        }

        /// <summary>For tests: asks a question not tied to any rule, to exercise the popup without authoring rule content at runtime.</summary>
        public long AskTestQuestionForTesting(string title, string body, string[] choiceTexts, int defaultChoiceNumber)
        {
            return _simulation.RuleRunner.AskQuestion("question.test", title, body, choiceTexts, defaultChoiceNumber);
        }

        private void Awake()
        {
            ResolveFolders();

            StartupReport report;
            try
            {
                report = BuildSimulation();
            }
            catch (ContentValidationException ex)
            {
                ShowContentErrorScreen(ex.Report.Problems);
                return;
            }

            BuildUi();
            WireInput();
            BuildWorld();
            BuildDebugWindow();
            BuildEscapeMenu();
            BuildCheatMenu();
            LogAndClearAnyStartupFocus();
            Debug.Log($"[TheEmptying] Startup report:\n{report}");
        }

        /// <summary>
        /// R5b: content problems stop the game before anything that would
        /// need a simulation gets built - no world, no camera, no cheat
        /// menu. Logs every problem individually (so the debug window's
        /// Errors tab shows them too, the same as any other startup
        /// error) and shows the full-screen panel in its place. Works
        /// whether the Game scene was reached from the main menu or opened
        /// directly, since both run through this same Awake().
        /// </summary>
        private void ShowContentErrorScreen(IReadOnlyList<ContentValidationProblem> problems)
        {
            foreach (ContentValidationProblem problem in problems)
            {
                Debug.LogError($"[TheEmptying] Content: {problem}");
            }

            var panelSettings = Resources.Load<PanelSettings>("GamePanelSettings");
            if (panelSettings == null)
            {
                Debug.LogError("[TheEmptying] Could not load GamePanelSettings from Resources. The content error screen will not render.");
                return;
            }

            _uiDocument = gameObject.AddComponent<UIDocument>();
            _uiDocument.panelSettings = panelSettings;
            UiPreferences.RegisterPanel(panelSettings);

            VisualElement root = _uiDocument.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;

            ContentErrorScreenPresenter.Build(root, problems);
            BuildDebugWindow();

            var asset = Resources.Load<InputActionAsset>("TheEmptyingControls");
            if (asset == null)
            {
                Debug.LogError("[TheEmptying] Could not load TheEmptyingControls input actions from Resources.");
                return;
            }

            _actions = Instantiate(asset);
            InputActionMap gameplayMap = _actions.FindActionMap("Gameplay");
            gameplayMap.FindAction("ToggleDebugWindow").performed += _ => _debugWindow.ToggleWindow();
            _actions.Enable();
        }

        /// <summary>
        /// R2c: logs what (if anything) holds UI focus right after every
        /// panel is built, so a stuck gameplay-key check can never fail
        /// silently again, then clears it - nothing the player has not
        /// clicked into should ever start focused.
        /// </summary>
        private void LogAndClearAnyStartupFocus()
        {
            Focusable focused = _uiDocument?.rootVisualElement?.panel?.focusController?.focusedElement;
            Debug.Log($"[TheEmptying] Input: focused element at startup = {(focused == null ? "(none)" : focused.ToString())}");
            (focused as VisualElement)?.Blur();
        }

        private void OnDestroy()
        {
            _actions?.Disable();
        }

        private void Update()
        {
            ClearUiFocusOnEmptyGameViewClick();
            RefreshQuestionPopup();
            RefreshErrorBadge();
            RefreshCameraInputSuspension();
            RefreshToast();

            if (_paused || IsBlockedByPendingQuestion())
            {
                return;
            }

            _accumulatedRealSeconds += Time.unscaledDeltaTime * _testTimeMultiplier;
            double realSecondsPerDay = _simulation.Settings.Get(RealSecondsPerDaySettingId);
            double ticksPerRealSecond = TickRateCalculator.TicksPerRealSecond(realSecondsPerDay, _speed);
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

        /// <summary>
        /// True while any question is pending AND NOT YET ANSWERED and the
        /// pause setting is on - the clock driver does not advance, and the
        /// player can't unpause, until every pending question has an answer.
        /// Once an answer is queued (AnswerQuestion), the question no longer
        /// counts as blocking even though it isn't applied until the next
        /// real tick runs ProcessQuestionAnswers.
        /// </summary>
        private bool IsBlockedByPendingQuestion()
        {
            if (_simulation.RuleRunner == null || _simulation.Settings.Get(RuleRunner.QuestionsPauseGameSettingId) == 0)
            {
                return false;
            }

            return _simulation.RuleRunner.PendingQuestions.Any(q => !_answeredQuestionIds.Contains(q.QuestionId));
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
            var contentAssetProblems = new List<ContentValidationProblem>();
            List<ContentAssetDefinition> contentAssets = LoadContentAssetDefinitions(contentAssetProblems);
            IModule[] modules = ModuleCatalog.CreateModules();

            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(FixedWorldSeed, sources, modules, ErrorMode.Development, logSink: this, contentAssets: contentAssets, contentAssetProblems: contentAssetProblems);
            _simulation = simulation;
            _mapModule = modules.OfType<MapModule>().First();
            _peopleModule = modules.OfType<PeopleModule>().First();

            // The closure reads _simulation live, so it keeps working after a load swaps the field.
            ErrorCatcher.InGameTimestampProvider = () =>
            {
                GameDate date = _simulation.Clock.CurrentDate;
                return $"{date}, {date.ToClockString(false)}";
            };

            BuiltInConsoleCommands.RegisterAll(_simulation, this, this);

            Debug.Log($"[TheEmptying] Startup report:\n{report}");
            return report;
        }

        /// <summary>Converts every ScriptableObject content asset in the shipped catalog into the plain records the simulation reads.</summary>
        private static List<ContentAssetDefinition> LoadContentAssetDefinitions(List<ContentValidationProblem> problems)
        {
            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            if (catalog == null)
            {
                Debug.LogError("[TheEmptying] Could not load ContentCatalog from Resources.");
                return new List<ContentAssetDefinition>();
            }

            return ContentAssetConverter.BuildDefinitions(catalog, problems);
        }

        /// <summary>R6/R7: the full-size chunked world map, its camera, the mouse-cell test helper, and the people presenter. Replaces the R1 TemporaryGround.</summary>
        private void BuildWorld()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[TheEmptying] No main camera found; the strategy camera cannot be attached.");
                return;
            }

            InputActionMap gameplayMap = _actions?.FindActionMap("Gameplay");
            if (gameplayMap == null)
            {
                Debug.LogError("[TheEmptying] No Gameplay action map found; the strategy camera will not respond to input.");
                return;
            }

            _cameraController = camera.gameObject.AddComponent<StrategyCameraController>();
            _cameraController.Initialize(gameplayMap, _simulation.Settings, _uiDocument.rootVisualElement, _mapModule, _floatingOrigin);

            _worldMapView = gameObject.AddComponent<WorldMapView>();
            _worldMapView.Initialize(_mapModule, _simulation.Settings, _floatingOrigin, _cameraController);

            _mapCursor = gameObject.AddComponent<MapCursorPresenter>();
            _mapCursor.Initialize(_mapModule, _floatingOrigin, camera, _uiDocument.rootVisualElement);

            _personPresenter = gameObject.AddComponent<PersonPresenter>();
            _personPresenter.Initialize(_peopleModule, _mapModule, _floatingOrigin, _worldMapView);

            _worldSelection = gameObject.AddComponent<WorldSelectionPresenter>();
            _worldSelection.Initialize(
                _peopleModule,
                _mapModule,
                _simulation.Content,
                _floatingOrigin,
                _cameraController,
                _personPresenter,
                _worldMapView.Decorations,
                camera,
                _uiDocument.rootVisualElement);
        }

        /// <summary>
        /// The Cheats tab's sections. Rebuilt whenever the simulation is
        /// rebuilt (here, and again after Load) since every control closes
        /// over _simulation - rebuilding keeps the section list itself (not
        /// just each control's live value) in sync with the current content.
        /// </summary>
        private void BuildCheatMenu()
        {
            _cheatMenu.Clear();

            CheatSection time = _cheatMenu.AddSection("Time");
            time.AddToggle("Pause", () => _paused, _ => _simulation.Commands.Execute("pause"));
            time.AddButton("1x", () => _simulation.Commands.Execute("speed 1"));
            time.AddButton("2x", () => _simulation.Commands.Execute("speed 2"));
            time.AddButton("3x", () => _simulation.Commands.Execute("speed 3"));
            time.AddButton("Skip 1 hour", () => _simulation.Commands.Execute("skip 1 h"));
            time.AddButton("Skip 1 day", () => _simulation.Commands.Execute("skip 1 d"));

            CheatSection save = _cheatMenu.AddSection("Save");
            save.AddButton("Quick save", QuickSave);
            save.AddButton("Quick load", QuickLoad);

            foreach (IGrouping<string, SettingDefinition> group in _simulation.Settings.ExportAll().GroupBy(d => d.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                CheatSection section = _cheatMenu.AddSection($"Settings: {group.Key}");
                foreach (SettingDefinition definition in group.OrderBy(d => d.Id, StringComparer.Ordinal))
                {
                    string id = definition.Id;
                    double defaultValue = definition.Default;
                    string tooltip = $"{id}\n{definition.Description}";

                    if (definition.ValueType == SettingValueType.Boolean)
                    {
                        section.AddToggle(
                            definition.Label,
                            () => _simulation.Settings.Get(id) != 0,
                            value => _simulation.Commands.Execute($"set {id} {(value ? 1 : 0)}"),
                            onReset: () => _simulation.Commands.Execute($"set {id} {defaultValue}"),
                            id: id,
                            tooltip: tooltip);
                    }
                    else
                    {
                        section.AddNumber(
                            definition.Label,
                            () => _simulation.Settings.Get(id),
                            value => _simulation.Commands.Execute($"set {id} {value}"),
                            definition.Minimum,
                            definition.Maximum,
                            onReset: () => _simulation.Commands.Execute($"set {id} {defaultValue}"),
                            id: id,
                            tooltip: tooltip);
                    }
                }
            }

            if (_spawnCheat == null)
            {
                _spawnCheat = new SpawnCheat(_cameraController, _floatingOrigin);
            }

            _spawnCheat.BuildSections(_cheatMenu);

            // Rebuilt fresh every time (unlike SpawnCheat): it must always hold the current MapModule, which a Load replaces.
            new MapCheat(_mapModule, _worldMapView, _cameraController).BuildSections(_cheatMenu);
            new PeopleCheat(_peopleModule, _cameraController, _worldSelection).BuildSections(_cheatMenu);
        }

        private void BuildDebugWindow()
        {
            _debugWindow = gameObject.AddComponent<DebugWindowPresenter>();
            _debugWindow.Initialize(_uiDocument.rootVisualElement, _cheatMenu);
        }

        /// <summary>R4: Resume/Main Menu/Options/Exit Game, with the shared Options panel and the two Yes/No confirmations.</summary>
        private void BuildEscapeMenu()
        {
            _escapeMenu = gameObject.AddComponent<EscapeMenuPresenter>();
            _escapeMenu.Initialize(_uiDocument.rootVisualElement, this, _debugWindow, _worldSelection);
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
            InputActionMap gameplayMap = _actions.FindActionMap("Gameplay");

            gameplayMap.FindAction("TogglePause").performed += _ => HandleTogglePause();
            gameplayMap.FindAction("SetSpeed1").performed += _ => HandleSetSpeed(1);
            gameplayMap.FindAction("SetSpeed2").performed += _ => HandleSetSpeed(2);
            gameplayMap.FindAction("SetSpeed3").performed += _ => HandleSetSpeed(3);
            gameplayMap.FindAction("ToggleDebugWindow").performed += _ => HandleToggleDebugWindow();
            gameplayMap.FindAction("Escape").performed += _ => HandleEscapeKeyPressed();

            // Always enabled: the keyboard rule is that gameplay keys work unless a text
            // field is actively focused for typing, never that the whole map gets disabled
            // (that is exactly what broke every gameplay key in the old debug console).
            _actions.Enable();

            // R2c: so this can never fail silently again.
            Debug.Log($"[TheEmptying] Input: controls loaded={_actions != null}, Gameplay map enabled={gameplayMap?.enabled ?? false}");
        }

        /// <summary>Space, and the matching HUD button, both funnel through here - and, like every cheat action, through the command system.</summary>
        private void HandleTogglePause()
        {
            if (UiInputUtility.IsTextFieldFocused(_uiDocument.rootVisualElement) || (_escapeMenu != null && _escapeMenu.IsOpen))
            {
                return;
            }

            _simulation.Commands.Execute("pause");
        }

        /// <summary>1/2/3, and the matching HUD buttons, both funnel through here. The speed command itself also unpauses.</summary>
        private void HandleSetSpeed(int speed)
        {
            if (UiInputUtility.IsTextFieldFocused(_uiDocument.rootVisualElement) || (_escapeMenu != null && _escapeMenu.IsOpen))
            {
                return;
            }

            _simulation.Commands.Execute($"speed {speed}");
        }

        /// <summary>
        /// R4/R8: Escape's single entry point, in strict priority order -
        /// stop typing, then close the debug window, then close the info
        /// window, then hand off to the Escape menu for going back one
        /// step or opening/closing itself. Never gated by the text-field
        /// focus check itself (step 1 IS that check, for every panel at
        /// once), so a stuck focus can never trap the player, same
        /// reasoning as F1.
        /// </summary>
        private void HandleEscapeKeyPressed()
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (UiInputUtility.IsTextFieldFocused(root))
            {
                (root.panel.focusController.focusedElement as VisualElement)?.Blur();
                return;
            }

            if (_debugWindow.IsOpen)
            {
                _debugWindow.ToggleWindow();
                return;
            }

            if (_worldSelection != null && _worldSelection.IsOpen)
            {
                _worldSelection.HandleEscapePressed();
                return;
            }

            _escapeMenu.HandleEscapePressed();
        }

        /// <summary>
        /// R9: the camera is frozen while the debug window or the Escape
        /// menu is open - recomputed every frame (instead of toggled at
        /// each one's own Open/Close) so the two sources can never fight
        /// over the flag.
        /// </summary>
        private void RefreshCameraInputSuspension()
        {
            if (_cameraController == null)
            {
                return;
            }

            bool shouldSuspend = (_debugWindow != null && _debugWindow.IsOpen) || (_escapeMenu != null && _escapeMenu.IsOpen);
            _cameraController.InputSuspended = shouldSuspend;
        }

        /// <summary>F1's entry point - does nothing while the Escape menu is open, since it is modal.</summary>
        private void HandleToggleDebugWindow()
        {
            if (_escapeMenu != null && _escapeMenu.IsOpen)
            {
                return;
            }

            _debugWindow.ToggleWindow();
        }

        /// <summary>A left click that lands on nothing in the UI always clears whatever UI control currently has focus.</summary>
        private void ClearUiFocusOnEmptyGameViewClick()
        {
            if (_uiDocument == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            VisualElement root = _uiDocument.rootVisualElement;
            if (root?.panel == null || UiInputUtility.IsPointerOverUi(root, Mouse.current.position.ReadValue()))
            {
                return;
            }

            root.panel.focusController.focusedElement?.Blur();
        }

        #endregion

        #region UI

        /// <summary>One UIDocument hosts both the HUD and the question popup.</summary>
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
            UiPreferences.RegisterPanel(panelSettings);

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
            root.style.flexDirection = FlexDirection.Column;

            BuildHud(root);
            BuildQuestionPopup(root);
            BuildToast(root);
        }

        /// <summary>R9: a small, self-hiding message for Quick save/load results, centered near the top of the screen.</summary>
        private void BuildToast(VisualElement root)
        {
            var wrapper = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 56,
                    left = 0,
                    right = 0,
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.Center,
                    display = DisplayStyle.None
                }
            };

            _toastLabel = new Label
            {
                style =
                {
                    color = Color.white,
                    backgroundColor = new Color(0f, 0f, 0f, 0.65f),
                    paddingLeft = 16,
                    paddingRight = 16,
                    paddingTop = 6,
                    paddingBottom = 6,
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    whiteSpace = WhiteSpace.Normal,
                    maxWidth = 400
                }
            };

            wrapper.Add(_toastLabel);
            root.Add(wrapper);
            _toastWrapper = wrapper;
        }

        /// <summary>Shows a short message, then hides it again after ToastSeconds.</summary>
        private void ShowToast(string message)
        {
            if (_toastWrapper == null)
            {
                return;
            }

            _toastLabel.text = message;
            _toastWrapper.style.display = DisplayStyle.Flex;
            _toastHideAtUnscaledTime = Time.unscaledTime + ToastSeconds;
        }

        private void RefreshToast()
        {
            if (_toastWrapper == null || _toastHideAtUnscaledTime < 0f)
            {
                return;
            }

            if (Time.unscaledTime >= _toastHideAtUnscaledTime)
            {
                _toastWrapper.style.display = DisplayStyle.None;
                _toastHideAtUnscaledTime = -1f;
            }
        }

        /// <summary>The Save cheat section's "Quick save" button - one fixed slot, reusing the existing Save path.</summary>
        private void QuickSave()
        {
            string result = Save(QuickSaveName);
            bool failed = result.StartsWith("Save failed", StringComparison.Ordinal);
            ShowToast(failed ? result : "Saved");
            if (failed)
            {
                Debug.LogError($"[TheEmptying] {result}");
            }
        }

        /// <summary>The Save cheat section's "Quick load" button - restores full state through the existing Load path.</summary>
        private void QuickLoad()
        {
            string result = Load(QuickSaveName);
            bool failed = result.StartsWith("No save named", StringComparison.Ordinal) || result.StartsWith("Load failed", StringComparison.Ordinal);
            ShowToast(failed ? result : "Loaded");
            if (failed)
            {
                Debug.LogError($"[TheEmptying] {result}");
            }
        }

        private void BuildQuestionPopup(VisualElement root)
        {
            var asset = Resources.Load<VisualTreeAsset>("QuestionPopup");
            if (asset == null)
            {
                Debug.LogError("[TheEmptying] Could not load QuestionPopup from Resources. The question popup will not render.");
                return;
            }

            asset.CloneTree(root);

            _questionPopupRoot = root.Q<VisualElement>("question-popup-root");
            _questionPopupCounter = root.Q<Label>("question-popup-counter");
            _questionPopupTitle = root.Q<Label>("question-popup-title");
            _questionPopupBody = root.Q<Label>("question-popup-body");
            _questionPopupChoices = root.Q<VisualElement>("question-popup-choices");

            _questionPopupRoot.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Shows the oldest pending-and-unanswered question, if any; otherwise
        /// hides the popup. Rebuilds the choice buttons only when the question
        /// being shown actually changes. Also drops any answered-question id
        /// that has finally left the pending list (its answer was applied).
        /// </summary>
        private void RefreshQuestionPopup()
        {
            if (_questionPopupRoot == null)
            {
                return;
            }

            IReadOnlyList<QuestionInfo> allPending = _simulation.RuleRunner?.PendingQuestions ?? Array.Empty<QuestionInfo>();
            _answeredQuestionIds.RemoveWhere(id => !allPending.Any(q => q.QuestionId == id));

            List<QuestionInfo> unanswered = allPending.Where(q => !_answeredQuestionIds.Contains(q.QuestionId)).ToList();
            if (unanswered.Count == 0)
            {
                _questionPopupRoot.style.display = DisplayStyle.None;
                _questionPopupShownQuestionId = -1;
                return;
            }

            QuestionInfo question = unanswered[0];
            if (question.QuestionId == _questionPopupShownQuestionId)
            {
                return;
            }

            _questionPopupShownQuestionId = question.QuestionId;

            _questionPopupCounter.text = unanswered.Count > 1 ? $"1 of {unanswered.Count}" : string.Empty;
            _questionPopupTitle.text = question.Title;
            _questionPopupBody.text = question.Body;

            _questionPopupChoices.Clear();
            _questionPopupChoiceActions.Clear();
            foreach ((int choiceNumber, string text) in question.Choices)
            {
                var button = new Button { text = text };
                button.AddToClassList("question-popup-choice-button");
                void OnClick() => OnQuestionPopupChoiceClicked(question.QuestionId, choiceNumber);
                button.clicked += OnClick;
                _questionPopupChoiceActions.Add(OnClick);
                _questionPopupChoices.Add(button);
            }

            _questionPopupRoot.style.display = DisplayStyle.Flex;
        }

        private void OnQuestionPopupChoiceClicked(long questionId, int choiceNumber)
        {
            string rejection = _simulation.AnswerQuestion(questionId, choiceNumber);
            if (rejection != null)
            {
                Debug.LogWarning($"[TheEmptying] {rejection}");
                return;
            }

            // The popup stops showing this question immediately; the answer
            // itself is applied at the start of the next real tick.
            _answeredQuestionIds.Add(questionId);
            _questionPopupShownQuestionId = -1;
        }

        #endregion

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
            _speedLabel = new Label { style = { color = Color.white, marginRight = 16 } };

            _hudPanel.Add(_dateLabel);
            _hudPanel.Add(_speedLabel);
            _hudPanel.Add(BuildHudButton("Pause", () => HandleTogglePause()));
            _hudPanel.Add(BuildHudButton("1x", () => HandleSetSpeed(1)));
            _hudPanel.Add(BuildHudButton("2x", () => HandleSetSpeed(2)));
            _hudPanel.Add(BuildHudButton("3x", () => HandleSetSpeed(3)));
            _hudPanel.Add(BuildErrorBadge());
            root.Add(_hudPanel);

            RefreshHud();
        }

        /// <summary>Hidden when there are no errors or warnings. Clicking it opens the debug window on the Errors tab; new errors never do that on their own.</summary>
        private VisualElement BuildErrorBadge()
        {
            _errorBadge = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 8, display = DisplayStyle.None } };
            _errorBadge.RegisterCallback<ClickEvent>(_ => _debugWindow.OpenOnErrorsTab());

            _errorBadgeErrorsLabel = new Label { style = { color = new Color(1f, 0.35f, 0.35f), marginRight = 8, display = DisplayStyle.None } };
            _errorBadgeWarningsLabel = new Label { style = { color = Color.yellow, display = DisplayStyle.None } };

            _errorBadge.Add(_errorBadgeErrorsLabel);
            _errorBadge.Add(_errorBadgeWarningsLabel);
            return _errorBadge;
        }

        private void RefreshErrorBadge()
        {
            if (_errorBadge == null)
            {
                return;
            }

            int errorCount = ErrorCatcher.ErrorCount;
            int warningCount = ErrorCatcher.WarningCount;

            _errorBadge.style.display = errorCount == 0 && warningCount == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            _errorBadgeErrorsLabel.style.display = errorCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _errorBadgeErrorsLabel.text = $"Errors: {errorCount}";

            _errorBadgeWarningsLabel.style.display = warningCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _errorBadgeWarningsLabel.text = $"Warnings: {warningCount}";
        }

        private static Button BuildHudButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text, style = { marginRight = 4 } };
            return button;
        }

        private void RefreshHud()
        {
            if (_dateLabel == null)
            {
                return;
            }

            GameDate date = _simulation.Clock.CurrentDate;
            _dateLabel.text = $"{date}, {date.ToClockString(false)}";
            _speedLabel.text = _paused ? "Paused" : $"{(int)_speed}x";
        }

        #region IPresentationCommands

        public void SetPaused(bool paused)
        {
            if (!paused && IsBlockedByPendingQuestion())
            {
                // Can't unpause while a question is pending and the pause setting is on.
                return;
            }

            _paused = paused;
            RefreshHud();
        }

        public void SetSpeed(int speed)
        {
            _speed = (SimSpeed)Mathf.Clamp(speed, 1, 3);
            RefreshHud();
        }

        /// <summary>For tests: fast-forwards the tick accumulator so a long real-time test covers the same in-game time in far less wall-clock time (see _testTimeMultiplier). 1 means no change.</summary>
        public void SetTestTimeMultiplierForTesting(double multiplier)
        {
            _testTimeMultiplier = multiplier;
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

                IModule[] modules = ModuleCatalog.CreateModules();
                var contentAssetProblems = new List<ContentValidationProblem>();

                using var stream = File.OpenRead(path);
                (Simulation loaded, LoadReport report) = GameSave.Load(stream, ResolveSourceFolders(), modules, ErrorMode.Development, logSink: this, contentAssets: LoadContentAssetDefinitions(contentAssetProblems), contentAssetProblems: contentAssetProblems);

                _simulation = loaded;
                _mapModule = modules.OfType<MapModule>().First();
                _peopleModule = modules.OfType<PeopleModule>().First();
                BuiltInConsoleCommands.RegisterAll(_simulation, this, this);
                _cameraController?.Rebind(_simulation.Settings, _mapModule);
                _worldMapView?.Rebind(_mapModule, _simulation.Settings);
                _personPresenter?.Rebind(_peopleModule, _mapModule);
                _worldSelection?.Rebind(_peopleModule, _mapModule, _simulation.Content);
                BuildCheatMenu();
                _debugWindow?.RebuildCheatsStructure();

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

        /// <summary>No console to filter yet (see R0); kept so the "logs" built-in command has somewhere to go.</summary>
        public string SetLogFilter(string mode)
        {
            switch (mode)
            {
                case "on":
                    return "Logs on.";
                case "off":
                    return "Logs off.";
                case "errors":
                    return "Logs limited to errors and warnings.";
                default:
                    return "Usage: logs <on|off|errors>";
            }
        }

        #endregion

        #region ILogSink

        public void Log(string message) => Debug.Log(message);

        #endregion
    }
}
