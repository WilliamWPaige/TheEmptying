using System;

namespace TheEmptying.Presentation.DevTools
{
    public enum CheatControlKind
    {
        Button,
        Toggle,
        Number
    }

    /// <summary>One control in a cheat section: a button, an on/off toggle, or a number field with optional limits. Every action it triggers goes through the command system.</summary>
    public sealed class CheatControl
    {
        public CheatControlKind Kind { get; }
        public string Label { get; }

        /// <summary>The setting's own content Id (R9), for the search box and the hover tooltip; null for a control that isn't backed by a setting.</summary>
        public string Id { get; }

        /// <summary>Shown on hover (R9) - the Id and description for a setting; null otherwise.</summary>
        public string Tooltip { get; }

        public Action OnClick { get; }

        public Func<bool> GetToggleValue { get; }
        public Action<bool> SetToggleValue { get; }

        public Func<double> GetNumberValue { get; }
        public Action<double> SetNumberValue { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }

        /// <summary>Optional: shown as a small "Reset" button beside the control.</summary>
        public Action OnReset { get; }

        private CheatControl(
            CheatControlKind kind,
            string label,
            string id = null,
            string tooltip = null,
            Action onClick = null,
            Func<bool> getToggleValue = null,
            Action<bool> setToggleValue = null,
            Func<double> getNumberValue = null,
            Action<double> setNumberValue = null,
            double? minimum = null,
            double? maximum = null,
            Action onReset = null)
        {
            Kind = kind;
            Label = label;
            Id = id;
            Tooltip = tooltip;
            OnClick = onClick;
            GetToggleValue = getToggleValue;
            SetToggleValue = setToggleValue;
            GetNumberValue = getNumberValue;
            SetNumberValue = setNumberValue;
            Minimum = minimum;
            Maximum = maximum;
            OnReset = onReset;
        }

        public static CheatControl Button(string label, Action onClick) => new CheatControl(CheatControlKind.Button, label, onClick: onClick);

        public static CheatControl Toggle(string label, Func<bool> getValue, Action<bool> setValue, Action onReset = null, string id = null, string tooltip = null) =>
            new CheatControl(CheatControlKind.Toggle, label, id: id, tooltip: tooltip, getToggleValue: getValue, setToggleValue: setValue, onReset: onReset);

        public static CheatControl Number(string label, Func<double> getValue, Action<double> setValue, double? minimum = null, double? maximum = null, Action onReset = null, string id = null, string tooltip = null) =>
            new CheatControl(CheatControlKind.Number, label, id: id, tooltip: tooltip, getNumberValue: getValue, setNumberValue: setValue, minimum: minimum, maximum: maximum, onReset: onReset);
    }
}
