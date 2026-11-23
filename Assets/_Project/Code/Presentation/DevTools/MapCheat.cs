using TheEmptying.Modules.Map;
using TheEmptying.Presentation.World;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R6 test tool: the cheat menu's "Map" section - "go to" X/Y, and jump-to-center/corner.</summary>
    public sealed class MapCheat
    {
        private readonly MapModule _mapModule;
        private readonly StrategyCameraController _cameraController;

        public MapCheat(MapModule mapModule, StrategyCameraController cameraController)
        {
            _mapModule = mapModule;
            _cameraController = cameraController;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection map = registry.AddSection("Map");

            map.AddNumber(
                "Go to X",
                () => _cameraController.FocusPoint.x,
                value => _cameraController.SetFocusPoint((float)value, _cameraController.FocusPoint.z),
                0,
                _mapModule.WorldWidthMeters);

            map.AddNumber(
                "Go to Y",
                () => _cameraController.FocusPoint.z,
                value => _cameraController.SetFocusPoint(_cameraController.FocusPoint.x, (float)value),
                0,
                _mapModule.WorldDepthMeters);

            map.AddButton("Center", () => _cameraController.SetFocusPoint((float)(_mapModule.WorldWidthMeters * 0.5), (float)(_mapModule.WorldDepthMeters * 0.5)));
            map.AddButton("Corner: SW", () => _cameraController.SetFocusPoint(0f, 0f));
            map.AddButton("Corner: SE", () => _cameraController.SetFocusPoint((float)_mapModule.WorldWidthMeters, 0f));
            map.AddButton("Corner: NW", () => _cameraController.SetFocusPoint(0f, (float)_mapModule.WorldDepthMeters));
            map.AddButton("Corner: NE", () => _cameraController.SetFocusPoint((float)_mapModule.WorldWidthMeters, (float)_mapModule.WorldDepthMeters));
        }
    }
}
