using System;
using TheEmptying.Modules.Ponds;
using TheEmptying.Presentation.Selection;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R17 test tool: the cheat menu's "Resources" section - with a pond selected, empties or fills it.</summary>
    public sealed class ResourceCheat
    {
        private readonly PondsModule _pondsModule;
        private readonly WorldSelectionPresenter _selection;
        private readonly Action<string> _onMessage;

        public ResourceCheat(PondsModule pondsModule, WorldSelectionPresenter selection, Action<string> onMessage)
        {
            _pondsModule = pondsModule;
            _selection = selection;
            _onMessage = onMessage;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection resources = registry.AddSection("Resources");
            resources.AddButton("Empty pond", () => SetSelectedPondDrinks(0));
            resources.AddButton("Fill pond", () => SetSelectedPondDrinks((int)_pondsModule.MaxDrinksSetting));
        }

        private void SetSelectedPondDrinks(int drinks)
        {
            if (!_selection.SelectedPondId.HasValue)
            {
                _onMessage?.Invoke("Select a pond first.");
                return;
            }

            _pondsModule.SetDrinks(_selection.SelectedPondId.Value, drinks);
        }
    }
}
