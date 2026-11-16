using System;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Jobs;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Buildings
{
    /// <summary>
    /// The panel that opens when a building with job slots is clicked: every
    /// slot group's pay, coverage, minimum on duty, minimum skill, allowed
    /// classes and who fills each slot. Government buildings get editable
    /// fields (applied next tick through JobsModule) and a Fire button per
    /// worker; private buildings are read-only. Pure presentation: reads the
    /// modules, writes only through their public APIs.
    /// </summary>
    public sealed class BuildingPanelPresenter : MonoBehaviour
    {
        private BuildingsModule _buildings;
        private JobsModule _jobs;
        private Action<string> _executeCommand;

        private VisualElement _root;
        private Label _title;
        private Button _closeButton;
        private ScrollView _slotsContainer;

        private EntityId _shownBuildingId;
        private bool _isShowing;

        public void Initialize(BuildingsModule buildings, JobsModule jobs, VisualElement uiRoot, Action<string> executeCommand)
        {
            _buildings = buildings;
            _jobs = jobs;
            _executeCommand = executeCommand;

            var asset = Resources.Load<VisualTreeAsset>("BuildingPanel");
            if (asset == null)
            {
                Debug.LogError("[TheEmptying] Could not load BuildingPanel from Resources. The building panel will not render.");
                return;
            }

            asset.CloneTree(uiRoot);

            _root = uiRoot.Q<VisualElement>("building-panel-root");
            _title = uiRoot.Q<Label>("building-panel-title");
            _closeButton = uiRoot.Q<Button>("building-panel-close");
            _slotsContainer = uiRoot.Q<ScrollView>("building-panel-slots");

            _closeButton.clicked += Hide;
            _root.style.display = DisplayStyle.None;
        }

        public void Rebind(BuildingsModule buildings, JobsModule jobs)
        {
            _buildings = buildings;
            _jobs = jobs;
            Hide();
        }

        public void Hide()
        {
            _isShowing = false;
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        /// <summary>Opens on a building with job slots; does nothing (and hides) for anything else, since the console inspector already covers the rest.</summary>
        public void Show(EntityId buildingId)
        {
            if (_root == null || _jobs == null || !_jobs.TryGetJobs(buildingId, out JobsState jobs) || !_buildings.TryGetBuilding(buildingId, out BuildingState building))
            {
                Hide();
                return;
            }

            _shownBuildingId = buildingId;
            _isShowing = true;
            Populate(buildingId, building, jobs);
            _root.style.display = DisplayStyle.Flex;
        }

        private void Update()
        {
            if (_isShowing && _jobs != null && _jobs.TryGetJobs(_shownBuildingId, out JobsState jobs) && _buildings.TryGetBuilding(_shownBuildingId, out BuildingState building))
            {
                Populate(_shownBuildingId, building, jobs);
            }
            else if (_isShowing)
            {
                Hide();
            }
        }

        private void Populate(EntityId buildingId, BuildingState building, JobsState jobs)
        {
            BuildingType type = _buildings.GetBuildingType(building.BuildingTypeId);
            bool isGovernment = !building.Owner.HasValue;
            _title.text = $"{type?.Name ?? building.BuildingTypeId} {buildingId}" + (isGovernment ? " (crown)" : " (private)");

            _slotsContainer.Clear();

            AddGroup(buildingId, JobSlotType.Builder, jobs.Builder, isGovernment);
            AddGroup(buildingId, JobSlotType.Worker, jobs.Worker, isGovernment);
            AddGroup(buildingId, JobSlotType.Manager, jobs.Manager, isGovernment);
            AddGroup(buildingId, JobSlotType.Trainer, jobs.Trainer, isGovernment);

            if (_slotsContainer.childCount == 0)
            {
                _slotsContainer.Add(new Label("No job slots.") { style = { color = Color.gray } });
            }
        }

        private void AddGroup(EntityId buildingId, JobSlotType type, JobSlotGroup group, bool isGovernment)
        {
            if (group == null)
            {
                return;
            }

            var box = new VisualElement();
            box.AddToClassList("building-panel-group");

            int vacancies = 0;
            foreach (EntityId? occupant in group.Occupants)
            {
                if (!occupant.HasValue)
                {
                    vacancies++;
                }
            }

            var title = new Label($"{type}: {group.Tier} {group.Skill}, {group.PayPerDay}/day, {group.Coverage}, min on duty {group.MinimumOnDuty}, {vacancies} vacant");
            title.AddToClassList("building-panel-group-title");
            box.Add(title);

            for (int i = 0; i < group.Occupants.Count; i++)
            {
                EntityId? occupant = group.Occupants[i];
                var row = new VisualElement();
                row.AddToClassList("building-panel-occupant-row");

                var label = new Label(occupant.HasValue ? $"#{i}: {occupant.Value}" : $"#{i}: vacant");
                label.AddToClassList("building-panel-occupant-label");
                row.Add(label);

                if (occupant.HasValue && isGovernment)
                {
                    var fireButton = new Button { text = "Fire" };
                    fireButton.AddToClassList("building-panel-fire-button");
                    EntityId personId = occupant.Value;
                    fireButton.clicked += () => _executeCommand($"fire {personId}");
                    row.Add(fireButton);
                }

                box.Add(row);
            }

            if (isGovernment)
            {
                AddEditControls(box, buildingId, type, group);
            }
            else
            {
                var note = new Label("Read-only: a private owner sets their own terms.");
                note.AddToClassList("building-panel-readonly-note");
                box.Add(note);
            }

            _slotsContainer.Add(box);
        }

        private void AddEditControls(VisualElement box, EntityId buildingId, JobSlotType type, JobSlotGroup group)
        {
            var payField = new IntegerField { label = "Pay/day (shil)", value = (int)(group.PayPerDay.HundredthsOfShil / 100) };
            payField.AddToClassList("building-panel-edit-row");
            box.Add(payField);

            var coverageField = new EnumField("Coverage", group.Coverage);
            coverageField.AddToClassList("building-panel-edit-row");
            box.Add(coverageField);

            var minOnDutyField = new IntegerField { label = "Min on duty", value = group.MinimumOnDuty };
            minOnDutyField.AddToClassList("building-panel-edit-row");
            box.Add(minOnDutyField);

            var minSkillField = new IntegerField { label = "Min skill", value = group.MinimumSkill };
            minSkillField.AddToClassList("building-panel-edit-row");
            box.Add(minSkillField);

            var lowerToggle = new Toggle("Lower") { value = (group.AllowedClassesMask & 1) != 0 };
            var middleToggle = new Toggle("Middle") { value = (group.AllowedClassesMask & 2) != 0 };
            var upperToggle = new Toggle("Upper") { value = (group.AllowedClassesMask & 4) != 0 };
            var classesRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            classesRow.Add(lowerToggle);
            classesRow.Add(middleToggle);
            classesRow.Add(upperToggle);
            box.Add(classesRow);

            var applyButton = new Button { text = "Apply" };
            applyButton.AddToClassList("building-panel-apply-button");
            applyButton.clicked += () =>
            {
                int mask = (lowerToggle.value ? 1 : 0) | (middleToggle.value ? 2 : 0) | (upperToggle.value ? 4 : 0);
                _jobs.TrySetGovernmentSlotConfig(
                    buildingId,
                    type,
                    Core.Economy.Money.FromShil(payField.value),
                    (JobCoverage)coverageField.value,
                    minOnDutyField.value,
                    minSkillField.value,
                    mask,
                    out _);
            };
            box.Add(applyButton);
        }
    }
}
