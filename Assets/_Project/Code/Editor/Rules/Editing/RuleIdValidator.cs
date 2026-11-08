using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>A rule's Id must start with "rule." and be unique across every loaded rule (all files, all sources).</summary>
    public static class RuleIdValidator
    {
        public static bool IsWellFormed(string ruleId)
        {
            return !string.IsNullOrEmpty(ruleId) && ruleId.StartsWith("rule.", StringComparison.Ordinal) && ruleId.Length > "rule.".Length;
        }

        /// <summary>Checks a candidate Id against every other already-loaded rule Id (excluding the rule's own current Id, if renaming). Returns null when valid, or a message naming the problem.</summary>
        public static string Validate(string candidateId, IEnumerable<string> otherLoadedRuleIds)
        {
            if (!IsWellFormed(candidateId))
            {
                return "A rule Id must start with 'rule.' and have more after it.";
            }

            if (otherLoadedRuleIds.Any(id => string.Equals(id, candidateId, StringComparison.Ordinal)))
            {
                return $"Rule Id '{candidateId}' is already used by another loaded rule.";
            }

            return null;
        }

        /// <summary>A short, free, "rule.&lt;base&gt;" (or "rule.&lt;base&gt;_2", "_3", ...) Id not already used by any loaded rule.</summary>
        public static string GenerateUniqueId(string baseName, IEnumerable<string> loadedRuleIds)
        {
            var used = new HashSet<string>(loadedRuleIds, StringComparer.Ordinal);
            string slug = Slugify(baseName);
            string candidate = $"rule.{slug}";

            int suffix = 2;
            while (used.Contains(candidate))
            {
                candidate = $"rule.{slug}_{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "new_rule";
            }

            var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            string slug = new string(chars);

            while (slug.Contains("__"))
            {
                slug = slug.Replace("__", "_");
            }

            slug = slug.Trim('_');
            return string.IsNullOrEmpty(slug) ? "new_rule" : slug;
        }
    }
}
