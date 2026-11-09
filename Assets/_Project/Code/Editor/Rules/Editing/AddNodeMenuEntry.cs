using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// One entry in the add-node menu: a plain block, or (for a family like
    /// Get Local) the family and the default variant to add before the user
    /// picks a different type from the node's own dropdown.
    /// </summary>
    public sealed class AddNodeMenuEntry
    {
        public string Category { get; }
        public string DisplayName { get; }
        public BlockType BlockType { get; }
        public BlockFamily Family { get; }

        public AddNodeMenuEntry(string category, string displayName, BlockType blockType, BlockFamily family)
        {
            Category = category;
            DisplayName = displayName;
            BlockType = blockType;
            Family = family;
        }
    }
}
