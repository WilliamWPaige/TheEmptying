using UnityEditor;
using UnityEngine;

namespace TheEmptying.Editor.Content
{
    /// <summary>
    /// "The Emptying/Validate All Content": checks every content asset and
    /// lists problems, each as a console entry that selects its asset.
    ///
    /// Empty since R0 removed the Milestone 3 content types; checks come
    /// back here as each type is rebuilt.
    /// </summary>
    public static class ValidateAllContentMenu
    {
        [MenuItem("The Emptying/Validate All Content")]
        public static void ValidateAllContent()
        {
            Debug.Log("[The Emptying] Validate All Content: no content types registered yet.");
        }
    }
}
