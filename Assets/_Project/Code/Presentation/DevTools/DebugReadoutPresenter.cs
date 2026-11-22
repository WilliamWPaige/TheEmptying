using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.People;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>
    /// 3.10b4: a small, always-updating box showing the selected person's task in detail, for
    /// hand-testing hauling and construction without needing the console. Tracks whatever
    /// PeoplePanelPresenter has selected (a People panel row click or a world click on a person both
    /// go through it) rather than taking its own selection input. Toggled with F3; on by default in
    /// the Editor, off in a build.
    /// </summary>
    public sealed class DebugReadoutPresenter : MonoBehaviour
    {
        private const float PopulateIntervalSeconds = 0.2f;

        private PeopleModule _people;
        private GatheringModule _gathering;
        private MapMovementModule _mapMovement;
        private PeoplePanelPresenter _peoplePanel;

        private VisualElement _root;
        private Label _nameLabel;
        private Label _taskLabel;
        private Label _phaseLabel;
        private Label _carryingLabel;
        private Label _targetLabel;
        private Label _shiftLabel;
        private Label _reasonLabel;

        private bool _visible;
        private float _nextPopulateTime;

        /// <summary>For tests: whether the box is currently shown.</summary>
        public bool IsVisibleForTesting => _visible;

        /// <summary>For tests: the box's current lines, in display order.</summary>
        public string[] LineTextsForTesting => _root == null
            ? System.Array.Empty<string>()
            : new[] { _nameLabel.text, _taskLabel.text, _phaseLabel.text, _carryingLabel.text, _targetLabel.text, _shiftLabel.text, _reasonLabel.text };

        public void Initialize(PeopleModule people, GatheringModule gathering, MapMovementModule mapMovement, PeoplePanelPresenter peoplePanel, VisualElement uiRoot)
        {
            _people = people;
            _gathering = gathering;
            _mapMovement = mapMovement;
            _peoplePanel = peoplePanel;

            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.left = 8;
            _root.style.bottom = 8;
            _root.style.paddingLeft = 6;
            _root.style.paddingRight = 6;
            _root.style.paddingTop = 4;
            _root.style.paddingBottom = 4;
            _root.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            _root.style.display = DisplayStyle.None;

            _nameLabel = AddLine();
            _taskLabel = AddLine();
            _phaseLabel = AddLine();
            _carryingLabel = AddLine();
            _targetLabel = AddLine();
            _shiftLabel = AddLine();
            _reasonLabel = AddLine();

            uiRoot.Add(_root);

#if UNITY_EDITOR
            _visible = true;
#else
            _visible = false;
#endif
        }

        public void Rebind(PeopleModule people, GatheringModule gathering, MapMovementModule mapMovement, PeoplePanelPresenter peoplePanel)
        {
            _people = people;
            _gathering = gathering;
            _mapMovement = mapMovement;
            _peoplePanel = peoplePanel;
        }

        private Label AddLine()
        {
            var label = new Label { style = { color = Color.white, fontSize = 12 } };
            _root.Add(label);
            return label;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                _visible = !_visible;
            }

            if (_root == null)
            {
                return;
            }

            _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_visible && Time.unscaledTime >= _nextPopulateTime)
            {
                Populate();
                _nextPopulateTime = Time.unscaledTime + PopulateIntervalSeconds;
            }
        }

        private void Populate()
        {
            EntityId? selected = _peoplePanel?.SelectedPersonId;
            if (!selected.HasValue || _people == null || !_people.TryGetPerson(selected.Value, out PersonState person))
            {
                _nameLabel.text = "No one selected.";
                _taskLabel.text = "";
                _phaseLabel.text = "";
                _carryingLabel.text = "";
                _targetLabel.text = "";
                _shiftLabel.text = "";
                _reasonLabel.text = "";
                return;
            }

            EntityId id = selected.Value;
            GatherTaskKind kind = _gathering?.GetTaskKind(id) ?? GatherTaskKind.None;
            GridPoint? target = _mapMovement?.GetDestination(id) ?? _gathering?.GetLocation(id);

            _nameLabel.text = $"{person.FullName} ({id})";
            _taskLabel.text = $"Task: {GatheringModule.DescribeTaskKind(kind)}";
            _phaseLabel.text = $"Phase: {_gathering?.DescribeTaskPhase(id) ?? "idle"}";
            _carryingLabel.text = $"Carrying: {_gathering?.DescribeCarrying(id) ?? "nothing"}";
            _targetLabel.text = DescribeTargetAndDistance(id, target);
            _shiftLabel.text = $"On shift: {((_gathering?.IsOnShift() ?? false) ? "yes" : "no")}";
            _reasonLabel.text = $"Reason: {_gathering?.GetTaskReason(id) ?? ""}";
        }

        private string DescribeTargetAndDistance(EntityId id, GridPoint? target)
        {
            if (!target.HasValue || _mapMovement == null)
            {
                return "Target: none";
            }

            (double X, double Y) targetWorld = _mapMovement.Grid.CellCenterWorldPosition(target.Value);
            if (!_mapMovement.TryGetPosition(id, out double x, out double y))
            {
                return $"Target: {target.Value}";
            }

            double dx = targetWorld.X - x;
            double dy = targetWorld.Y - y;
            double distanceMeters = System.Math.Sqrt(dx * dx + dy * dy);
            return $"Target: {target.Value}, {distanceMeters:F0}m away";
        }
    }
}
