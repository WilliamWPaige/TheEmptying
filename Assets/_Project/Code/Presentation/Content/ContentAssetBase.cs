using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Base for every player-facing content ScriptableObject: a stable text
    /// Id, and self-contained validation that doesn't need the project's
    /// asset database (that part - duplicate Ids across assets - is checked
    /// separately, in the Editor assembly).
    /// </summary>
    public abstract class ContentAssetBase : ScriptableObject
    {
        public abstract string Id { get; }
        public abstract string IdPrefix { get; }
        public abstract string ContentTypeName { get; }

        /// <summary>Problems this asset can detect on its own. Subclasses add their own checks on top of the Id check here.</summary>
        public virtual IEnumerable<string> ValidateSelf()
        {
            if (string.IsNullOrEmpty(Id))
            {
                yield return "Id is missing.";
            }
            else if (!Id.StartsWith(IdPrefix, StringComparison.Ordinal))
            {
                yield return $"Id '{Id}' must start with '{IdPrefix}'.";
            }
        }
    }
}
