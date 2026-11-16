using System.Collections.Generic;
using TheEmptying.Presentation.Content;
using UnityEditor;
using UnityEngine;

namespace TheEmptying.Editor.Content
{
    /// <summary>"The Emptying/Validate All Content": checks every content asset and lists problems, each as a console entry that selects its asset.</summary>
    public static class ValidateAllContentMenu
    {
        [MenuItem("The Emptying/Validate All Content")]
        public static void ValidateAllContent()
        {
            int problemCount = 0;

            problemCount += ValidateType<GoodAsset>();
            problemCount += ValidateType<NeedAsset>();
            problemCount += ValidateType<ZoneAsset>();
            problemCount += ValidateType<BuildingTypeAsset>();
            problemCount += ValidateType<RecipeAsset>();
            problemCount += ValidateType<NameListAsset>();

            if (problemCount == 0)
            {
                Debug.Log("[The Emptying] Validate All Content: no problems found.");
            }
            else
            {
                Debug.LogWarning($"[The Emptying] Validate All Content: {problemCount} problem(s) found (see above).");
            }
        }

        private static int ValidateType<T>() where T : ContentAssetBase
        {
            List<T> assets = ContentAssetDuplicateCheck.FindAllOfSameType<T>();
            int count = 0;

            foreach (T asset in assets)
            {
                foreach (string problem in asset.ValidateSelf())
                {
                    Debug.LogWarning($"[The Emptying] {asset.name}: {problem}", asset);
                    count++;
                }

                foreach (string problem in ContentAssetDuplicateCheck.FindDuplicateIdProblems(asset))
                {
                    Debug.LogWarning($"[The Emptying] {asset.name}: {problem}", asset);
                    count++;
                }
            }

            return count;
        }
    }
}
