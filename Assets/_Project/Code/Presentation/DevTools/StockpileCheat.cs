using System;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.Selection;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R12 test tool: the cheat menu's "Stockpile" section - one button per item, each adding 10 of it to the selected stockpile, respecting slots, sizes, blocks and limits.</summary>
    public sealed class StockpileCheat
    {
        private const int AddAmount = 10;

        private readonly ZonesModule _zonesModule;
        private readonly WorldSelectionPresenter _selection;
        private readonly ContentDatabase _content;
        private readonly Action<string> _onMessage;

        public StockpileCheat(ZonesModule zonesModule, WorldSelectionPresenter selection, ContentDatabase content, Action<string> onMessage)
        {
            _zonesModule = zonesModule;
            _selection = selection;
            _content = content;
            _onMessage = onMessage;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection stockpile = registry.AddSection("Stockpile");

            foreach (ItemContent item in _content.ListByType(BuiltInContentTypes.ItemTypeName).Select(e => (ItemContent)e.Definition))
            {
                string itemId = item.Id;
                string displayName = item.DisplayName;
                stockpile.AddButton($"Add {AddAmount} {displayName} to selected stockpile", () => AddToSelectedStockpile(itemId, displayName));
            }
        }

        private void AddToSelectedStockpile(string itemId, string displayName)
        {
            if (!_selection.SelectedZoneId.HasValue)
            {
                _onMessage?.Invoke("Select a stockpile first.");
                return;
            }

            (int added, int didNotFit, string reason) = _zonesModule.TryAddItemToStockpile(_selection.SelectedZoneId.Value, itemId, AddAmount);

            _onMessage?.Invoke(didNotFit == 0
                ? $"Added {added} {displayName}."
                : $"Added {added} {displayName}; {didNotFit} didn't fit ({reason}).");
        }
    }
}
