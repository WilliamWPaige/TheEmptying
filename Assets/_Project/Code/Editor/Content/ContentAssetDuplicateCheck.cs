using System.Collections.Generic;
using System.Linq;
using TheEmptying.Presentation.Content;
using UnityEditor;

namespace TheEmptying.Editor.Content
{
    /// <summary>Finds other project assets sharing an Id. Editor-only: needs the project's asset database.</summary>
    internal static class ContentAssetDuplicateCheck
    {
        public static IEnumerable<string> FindDuplicateIdProblems(ContentAssetBase asset)
        {
            if (string.IsNullOrEmpty(asset.Id))
            {
                yield break;
            }

            foreach (ContentAssetBase other in FindAllOfType(asset.GetType()))
            {
                if (other != asset && other.Id == asset.Id)
                {
                    yield return $"Id '{asset.Id}' is also used by '{other.name}'.";
                }
            }
        }

        public static List<T> FindAllOfSameType<T>() where T : ContentAssetBase
        {
            return FindAllOfType(typeof(T)).Cast<T>().ToList();
        }

        private static List<ContentAssetBase> FindAllOfType(System.Type type)
        {
            return AssetDatabase.FindAssets($"t:{type.Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ContentAssetBase>)
                .Where(a => a != null)
                .ToList();
        }
    }
}
