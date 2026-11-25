using System;
using System.Collections.Generic;
using TheEmptying.Core.Entities;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Ponds;
using TheEmptying.Presentation.Selection;
using TheEmptying.Presentation.World;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R17 test tool: the cheat menu's "Resources" section - with a pond selected, empties or fills it. R17c adds "Check ponds", a read-only check for the ponds-clear-decorations bug.</summary>
    public sealed class ResourceCheat
    {
        private readonly PondsModule _pondsModule;
        private readonly MapModule _mapModule;
        private readonly WorldMapView _worldMapView;
        private readonly WorldSelectionPresenter _selection;
        private readonly Action<string> _onMessage;

        public ResourceCheat(PondsModule pondsModule, MapModule mapModule, WorldMapView worldMapView, WorldSelectionPresenter selection, Action<string> onMessage)
        {
            _pondsModule = pondsModule;
            _mapModule = mapModule;
            _worldMapView = worldMapView;
            _selection = selection;
            _onMessage = onMessage;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection resources = registry.AddSection("Resources");
            resources.AddButton("Empty pond", () => SetSelectedPondDrinks(0));
            resources.AddButton("Fill pond", () => SetSelectedPondDrinks((int)_pondsModule.MaxDrinksSetting));
            resources.AddButton("Check ponds", CheckPonds);
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

        /// <summary>
        /// R17c: for every pond whose chunk is currently loaded, counts
        /// trees/stones/mounds within its water or clear margin, both in
        /// the map data (MapModule.CountPlacementsWithinRadius) and in
        /// what's actually drawn (TemporaryChunkDecorations.ActivePlacements)
        /// - lets the user confirm by hand that the two always agree (both
        /// zero) after the fix.
        /// </summary>
        private void CheckPonds()
        {
            double marginMeters = _mapModule.CellSizeMeters * _pondsModule.ClearMarginCellsSetting;

            int pondsChecked = 0;
            int dataCount = 0;
            int drawnCount = 0;

            TemporaryChunkDecorations decorations = _worldMapView.Decorations;

            foreach (KeyValuePair<EntityId, PondState> row in _pondsModule.Ponds)
            {
                PondState pond = row.Value;
                ChunkCoord chunk = _mapModule.WorldToChunk(pond.CenterX, pond.CenterZ);
                if (!_worldMapView.IsChunkLoaded(chunk))
                {
                    continue;
                }

                pondsChecked++;
                double radius = pond.RadiusMeters + marginMeters;

                dataCount += _mapModule.CountPlacementsWithinRadius(pond.CenterX, pond.CenterZ, radius);

                foreach (ChunkPlacement placement in decorations.ActivePlacements)
                {
                    if (_mapModule.IsPlacementWithinRadius(placement, pond.CenterX, pond.CenterZ, radius))
                    {
                        drawnCount++;
                    }
                }
            }

            _onMessage?.Invoke(pondsChecked == 0
                ? "No loaded ponds to check."
                : $"{pondsChecked} loaded pond(s): {dataCount} in map data, {drawnCount} drawn on screen.");
        }
    }
}
