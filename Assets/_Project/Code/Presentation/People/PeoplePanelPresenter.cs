using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.MapMovement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.People
{
    /// <summary>
    /// 3.9f: a hand test found founders looking like they never did anything, with no way to tell
    /// what each one was up to or why. One row per person: name, role, current activity, current
    /// task or job, target location, distance to target, hunger, rest, and - when they have neither
    /// a job nor a task - a plain reason. Toggled by a HUD button or the P key, matching the T-key
    /// toggle LandPresenter already uses for its overlay. Clicking a row selects that person, moves
    /// the camera to them, and draws a line from them to their current target for as long as they
    /// stay selected. Pure presentation: reads the modules, writes nothing.
    /// </summary>
    public sealed class PeoplePanelPresenter : MonoBehaviour
    {
        private const float SelectionLineHeight = 0.6f;
        private const float PopulateIntervalSeconds = 0.5f;

        private PeopleModule _people;
        private GatheringModule _gathering;
        private JobsModule _jobs;
        private BuildingsModule _buildings;
        private MapMovementModule _mapMovement;
        private StrategyCameraController _cameraController;

        private VisualElement _root;
        private ScrollView _rowsContainer;
        private Button _closeButton;
        private Button _toggleButton;

        private EntityId? _selectedPersonId;
        private LineRenderer _selectionLine;

        private bool _isShowing;
        private float _nextPopulateTime;

        /// <summary>For tests: whether the panel is currently shown.</summary>
        public bool IsShowingForTesting => _isShowing;

        /// <summary>For tests: how many person rows are currently listed.</summary>
        public int RowCountForTesting => _rowsContainer?.childCount ?? 0;

        /// <summary>For tests: the currently selected person, if any.</summary>
        public EntityId? SelectedPersonIdForTesting => _selectedPersonId;

        /// <summary>For tests: whether the selection line is currently drawn.</summary>
        public bool IsSelectionLineVisibleForTesting => _selectionLine != null && _selectionLine.enabled;

        /// <summary>For tests: the panel's own resolved width in pixels (3.10b2: widened so long rows don't get cut off).</summary>
        public float RootWidthForTesting => _root?.resolvedStyle.width ?? 0f;

        /// <summary>For tests: whether a row's detail text wraps instead of clipping, regardless of how wide the panel ends up (3.10b2).</summary>
        public bool RowsWrapForTesting =>
            _rowsContainer != null &&
            _rowsContainer.Query<Label>(className: "people-panel-row-detail").First()?.resolvedStyle.whiteSpace == WhiteSpace.Normal;

        /// <summary>For tests: runs the exact row-click decision a real click on this person's row would (selects them, moves the camera) - a UI Toolkit ClickEvent can't be driven by simulated input (see RealInputClickTests).</summary>
        public void SelectPersonForTesting(EntityId id) => SelectPerson(id);

        /// <summary>3.10b2: clicking a person's capsule in the world opens this panel (if not already open) with them selected and the camera moved to them - exactly like clicking their row.</summary>
        public void ShowWithPersonSelected(EntityId id)
        {
            Show();
            SelectPerson(id);
        }

        public void Initialize(
            PeopleModule people,
            GatheringModule gathering,
            JobsModule jobs,
            BuildingsModule buildings,
            MapMovementModule mapMovement,
            StrategyCameraController cameraController,
            VisualElement uiRoot)
        {
            _people = people;
            _gathering = gathering;
            _jobs = jobs;
            _buildings = buildings;
            _mapMovement = mapMovement;
            _cameraController = cameraController;

            var asset = Resources.Load<VisualTreeAsset>("PeoplePanel");
            if (asset == null)
            {
                Debug.LogError("[TheEmptying] Could not load PeoplePanel from Resources. The people panel will not render.");
                return;
            }

            asset.CloneTree(uiRoot);

            _root = uiRoot.Q<VisualElement>("people-panel-root");
            _rowsContainer = uiRoot.Q<ScrollView>("people-panel-rows");
            _closeButton = uiRoot.Q<Button>("people-panel-close");

            _closeButton.clicked += Hide;
            _root.style.display = DisplayStyle.None;

            _toggleButton = new Button { text = "People" };
            _toggleButton.style.position = Position.Absolute;
            _toggleButton.style.top = 8;
            _toggleButton.style.right = 176;
            _toggleButton.clicked += Toggle;
            uiRoot.Add(_toggleButton);

            var lineObject = new GameObject("PersonSelectionLine");
            lineObject.transform.SetParent(transform, false);
            _selectionLine = lineObject.AddComponent<LineRenderer>();
            _selectionLine.positionCount = 2;
            _selectionLine.startWidth = 0.15f;
            _selectionLine.endWidth = 0.15f;
            _selectionLine.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(1f, 0.85f, 0.2f) };
            _selectionLine.enabled = false;
        }

        /// <summary>After a load, re-point at the freshly loaded modules and drop any selection (the old entity ids may no longer resolve to the same people).</summary>
        public void Rebind(PeopleModule people, GatheringModule gathering, JobsModule jobs, BuildingsModule buildings, MapMovementModule mapMovement)
        {
            _people = people;
            _gathering = gathering;
            _jobs = jobs;
            _buildings = buildings;
            _mapMovement = mapMovement;
            _selectedPersonId = null;
            Hide();
        }

        public void Toggle()
        {
            if (_isShowing)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Show()
        {
            if (_root == null)
            {
                return;
            }

            _isShowing = true;
            _root.style.display = DisplayStyle.Flex;
            Populate();
        }

        public void Hide()
        {
            _isShowing = false;
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
            {
                Toggle();
            }

            // Rebuilding every row is only useful once whatever it shows could plausibly have
            // changed - nothing here updates faster than a tick - and doing it every single
            // rendered frame churned through a full set of Label/VisualElement allocations each
            // frame for no visible benefit, adding real GC pressure over a long play session.
            if (_isShowing && Time.unscaledTime >= _nextPopulateTime)
            {
                Populate();
                _nextPopulateTime = Time.unscaledTime + PopulateIntervalSeconds;
            }

            UpdateSelectionLine();
        }

        private void Populate()
        {
            if (_people == null || _rowsContainer == null)
            {
                return;
            }

            _rowsContainer.Clear();

            foreach (KeyValuePair<EntityId, PersonState> row in _people.People)
            {
                _rowsContainer.Add(BuildRow(row.Key, row.Value));
            }

            if (_rowsContainer.childCount == 0)
            {
                _rowsContainer.Add(new Label("No people.") { style = { color = Color.gray } });
            }
        }

        private VisualElement BuildRow(EntityId id, PersonState person)
        {
            var row = new VisualElement();
            row.AddToClassList("people-panel-row");
            if (_selectedPersonId == id)
            {
                row.AddToClassList("people-panel-row-selected");
            }

            EntityId jobBuilding = EntityId.None;
            JobSlotType jobSlotType = default;
            bool hasJob = _jobs != null && _jobs.TryGetEmployment(id, out jobBuilding, out jobSlotType);
            GatherTaskKind taskKind = _gathering?.GetTaskKind(id) ?? GatherTaskKind.None;
            // 3.9h: "no job and no gather task" alone used to show "No task: ..." under a free-time
            // activity too (Socializing, Strolling, Shopping, Sleeping...) - none of those need a
            // job or a task either. Genuinely idle means the activity itself is Idle.
            bool isIdle = !hasJob && taskKind == GatherTaskKind.None && person.Activity == PersonActivity.Idle;

            string role = DescribeRole(id, person);
            string taskOrJob = hasJob ? DescribeJob(jobBuilding, jobSlotType) : DescribeGatherTask(id);

            GridPoint? target = _mapMovement?.GetDestination(id) ?? _gathering?.GetLocation(id);
            string targetText = DescribeTargetAndDistance(id, target);

            // 3.10b: no banked hours - a production worker whose building currently can't progress
            // (no inputs, or no resource node in reach for a camp) shows why instead of plain "Working".
            string activityText = hasJob && person.Activity == PersonActivity.Working && _gathering?.GetProductionWaitReason(jobBuilding) is string waitReason
                ? waitReason
                : PeopleModule.DescribeActivity(person.Activity);

            var summary = new Label($"{person.FullName} ({role}) - {activityText}");
            summary.AddToClassList("people-panel-row-summary");
            row.Add(summary);

            var detail = new Label($"task: {taskOrJob} | {targetText} | hunger: {DescribeNeed(person, "need.hunger")} | rest: {DescribeNeed(person, "need.rest")} | social: {DescribeNeed(person, "need.social")} | recreation: {DescribeNeed(person, "need.recreation")}");
            detail.AddToClassList("people-panel-row-detail");
            row.Add(detail);

            if (isIdle)
            {
                string reason = _gathering?.DescribeIdleReason(id) ?? "No task.";
                var idleLabel = new Label($"No task: {reason}");
                idleLabel.AddToClassList("people-panel-row-idle-reason");
                row.Add(idleLabel);
            }

            row.RegisterCallback<ClickEvent>(_ => SelectPerson(id));

            return row;
        }

        private string DescribeRole(EntityId id, PersonState person)
        {
            if (person.IsMonarch)
            {
                return "monarch";
            }

            if (person.IsFounder)
            {
                string role = _people.IsAdvisor(id) ? "founder, advisor" : "founder";
                return person.FounderSpecialty.HasValue ? $"{role}, specialty: {PeopleModule.DescribeSkill(person.FounderSpecialty.Value)}" : role;
            }

            return "citizen";
        }

        private string DescribeJob(EntityId building, JobSlotType slotType)
        {
            string buildingName = building.ToString();
            if (_buildings != null && _buildings.TryGetBuilding(building, out BuildingState state))
            {
                BuildingType type = _buildings.GetBuildingType(state.BuildingTypeId);
                buildingName = type?.Name ?? state.BuildingTypeId;
            }

            string waitReason = _gathering?.GetProductionWaitReason(building);
            return string.IsNullOrEmpty(waitReason) ? $"{slotType} at {buildingName}" : $"{slotType} at {buildingName} ({waitReason})";
        }

        private string DescribeGatherTask(EntityId id) =>
            _gathering != null ? _gathering.DescribeTaskDetail(id) : GatheringModule.DescribeTaskKind(GatherTaskKind.None);

        private string DescribeTargetAndDistance(EntityId id, GridPoint? target)
        {
            if (!target.HasValue || _mapMovement == null)
            {
                return "target: none";
            }

            (double X, double Y) targetWorld = _mapMovement.Grid.CellCenterWorldPosition(target.Value);

            if (!_mapMovement.TryGetPosition(id, out double x, out double y))
            {
                return $"target: {target.Value}";
            }

            double dx = targetWorld.X - x;
            double dy = targetWorld.Y - y;
            double distanceMeters = System.Math.Sqrt(dx * dx + dy * dy);
            return $"target: {target.Value}, {distanceMeters:F0}m away";
        }

        private static string DescribeNeed(PersonState person, string needId) =>
            person.Needs.TryGetValue(needId, out PersonNeedState need) ? $"{need.Meter:F0}%" : "n/a";

        private void SelectPerson(EntityId id)
        {
            _selectedPersonId = id;
            Populate(); // so the clicked row immediately shows as selected

            if (_mapMovement != null && _mapMovement.TryGetPosition(id, out double x, out double y) && _cameraController != null)
            {
                var groundPoint = new Vector3((float)x, 0f, (float)y);
                float currentHeight = _cameraController.transform.position.y;
                _cameraController.PlaceAbove(groundPoint, currentHeight);
            }
        }

        private void UpdateSelectionLine()
        {
            if (_selectionLine == null)
            {
                return;
            }

            if (!_selectedPersonId.HasValue || _mapMovement == null || !_mapMovement.TryGetPosition(_selectedPersonId.Value, out double x, out double y))
            {
                _selectionLine.enabled = false;
                return;
            }

            GridPoint? target = _mapMovement.GetDestination(_selectedPersonId.Value) ?? _gathering?.GetLocation(_selectedPersonId.Value);
            if (!target.HasValue)
            {
                _selectionLine.enabled = false;
                return;
            }

            (double X, double Y) targetWorld = _mapMovement.Grid.CellCenterWorldPosition(target.Value);

            _selectionLine.enabled = true;
            _selectionLine.SetPosition(0, new Vector3((float)x, SelectionLineHeight, (float)y));
            _selectionLine.SetPosition(1, new Vector3((float)targetWorld.X, SelectionLineHeight, (float)targetWorld.Y));
        }
    }
}
