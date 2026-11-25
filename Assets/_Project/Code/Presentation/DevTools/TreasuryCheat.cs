using System;
using TheEmptying.Modules.People;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R15b test tool: the cheat menu's "Treasury" section - set the crown treasury directly, or nudge it by 100 copper. Applies immediately to the current game.</summary>
    public sealed class TreasuryCheat
    {
        private const int NudgeTenthCopper = 1000;

        private readonly PeopleModule _peopleModule;

        public TreasuryCheat(PeopleModule peopleModule)
        {
            _peopleModule = peopleModule;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection treasury = registry.AddSection("Treasury");

            treasury.AddNumber(
                "Set treasury (copper)",
                () => _peopleModule.TreasuryTenthCopper / 10.0,
                value => _peopleModule.SetTreasuryTenthCopper((int)Math.Round(value * 10.0)),
                minimum: 0);

            treasury.AddButton("Add 100 copper", () => _peopleModule.SetTreasuryTenthCopper(_peopleModule.TreasuryTenthCopper + NudgeTenthCopper));
            treasury.AddButton("Remove 100 copper", () => _peopleModule.SetTreasuryTenthCopper(_peopleModule.TreasuryTenthCopper - NudgeTenthCopper));
        }
    }
}
