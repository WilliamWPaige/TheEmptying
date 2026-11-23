using System;
using System.Collections.Generic;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>A titled group of cheat controls, shown together in the Cheats tab.</summary>
    public sealed class CheatSection
    {
        public string Title { get; }
        public List<CheatControl> Controls { get; } = new List<CheatControl>();

        public CheatSection(string title)
        {
            Title = title;
        }

        public CheatSection AddButton(string label, Action onClick)
        {
            Controls.Add(CheatControl.Button(label, onClick));
            return this;
        }

        public CheatSection AddToggle(string label, Func<bool> getValue, Action<bool> setValue, Action onReset = null, string id = null, string tooltip = null)
        {
            Controls.Add(CheatControl.Toggle(label, getValue, setValue, onReset, id, tooltip));
            return this;
        }

        public CheatSection AddNumber(string label, Func<double> getValue, Action<double> setValue, double? minimum = null, double? maximum = null, Action onReset = null, string id = null, string tooltip = null)
        {
            Controls.Add(CheatControl.Number(label, getValue, setValue, minimum, maximum, onReset, id, tooltip));
            return this;
        }
    }
}
