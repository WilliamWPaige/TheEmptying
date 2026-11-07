using System.Globalization;

namespace TheEmptying.Core.Console
{
    /// <summary>Console command argument parsing always uses the invariant culture.</summary>
    public static class ConsoleArgs
    {
        public static bool TryParseInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
