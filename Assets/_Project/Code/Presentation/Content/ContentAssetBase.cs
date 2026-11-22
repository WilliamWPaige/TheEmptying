using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Base for every player-facing content ScriptableObject: a stable text
    /// Id, a display name and a required Model - the three fields every
    /// type shares (Phase 3, "Asset tool, first version"). Self-contained
    /// validation that doesn't need the project's asset database (that
    /// part - duplicate Ids across assets - is checked separately, in the
    /// Editor assembly).
    /// </summary>
    public abstract class ContentAssetBase : ScriptableObject
    {
        public abstract string Id { get; }
        public abstract string IdPrefix { get; }
        public abstract string ContentTypeName { get; }
        public abstract string DisplayName { get; }
        public abstract GameObject Model { get; }

        /// <summary>Problems this asset can detect on its own. Subclasses add their own checks (such as a footprint) on top of the ones here.</summary>
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

            if (string.IsNullOrEmpty(DisplayName))
            {
                yield return "Display name is missing.";
            }

            if (Model == null)
            {
                yield return "Model is missing.";
            }
        }
    }
}
