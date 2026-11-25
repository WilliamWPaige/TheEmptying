using System;
using System.Collections.Generic;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18: the zoning menu's default-name rule - "Stockpile Zone",
    /// "Stockpile Zone 2", "Stockpile Zone 3", and so on, the first of
    /// those not already in use. Pure and testable on its own; ordinal
    /// comparison only, per the project's determinism rule for string
    /// comparisons.
    /// </summary>
    public static class ZoneNaming
    {
        public static string NextDefaultName(string baseName, IEnumerable<string> existingNames)
        {
            var taken = new HashSet<string>(existingNames, StringComparer.Ordinal);
            if (!taken.Contains(baseName))
            {
                return baseName;
            }

            int suffix = 2;
            while (taken.Contains($"{baseName} {suffix}"))
            {
                suffix++;
            }

            return $"{baseName} {suffix}";
        }
    }
}
