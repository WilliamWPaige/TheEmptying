using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Rules
{
    /// <summary>Every rule that compiled without errors, by Id.</summary>
    public sealed class RuleRegistry
    {
        private readonly Dictionary<string, CompiledRule> _rules = new Dictionary<string, CompiledRule>();

        public void Add(CompiledRule rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            _rules[rule.Id] = rule;
        }

        public bool TryGet(string id, out CompiledRule rule)
        {
            return _rules.TryGetValue(id, out rule);
        }

        /// <summary>Every compiled rule, in ascending Id order (ordinal).</summary>
        public IEnumerable<CompiledRule> All => _rules.Values.OrderBy(r => r.Id, StringComparer.Ordinal);
    }
}
