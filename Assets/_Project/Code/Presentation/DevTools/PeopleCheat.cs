using TheEmptying.Modules.People;
using TheEmptying.Presentation.Selection;
using TheEmptying.Presentation.World;
using UnityEngine;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R7 test tool: the cheat menu's "People" section - spawn a person at the camera center, and remove the selected person.</summary>
    public sealed class PeopleCheat
    {
        private readonly PeopleModule _peopleModule;
        private readonly StrategyCameraController _cameraController;
        private readonly WorldSelectionPresenter _selection;

        public PeopleCheat(PeopleModule peopleModule, StrategyCameraController cameraController, WorldSelectionPresenter selection)
        {
            _peopleModule = peopleModule;
            _cameraController = cameraController;
            _selection = selection;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection people = registry.AddSection("People");
            people.AddButton("Spawn person at camera", SpawnAtCamera);
            people.AddButton("Remove selected person", RemoveSelected);
        }

        private void SpawnAtCamera()
        {
            Vector3 focus = _cameraController.FocusPoint;
            _peopleModule.TrySpawnPersonNear(focus.x, focus.z);
        }

        private void RemoveSelected()
        {
            if (!_selection.SelectedPersonId.HasValue)
            {
                return;
            }

            _peopleModule.RemovePerson(_selection.SelectedPersonId.Value);
            _selection.ClearSelection();
        }
    }
}
