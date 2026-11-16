using TheEmptying.Core.Modules;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// Every module the game knows how to run, regardless of whether its
    /// manifest enables it. The single shared list the game scene and editor
    /// tools (the block catalog) both build from, so neither can drift out of
    /// sync with the other. A new instance each call, since modules hold
    /// per-game state.
    /// </summary>
    public static class ModuleCatalog
    {
        public static IModule[] CreateModules()
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs };
        }
    }
}
