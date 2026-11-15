using System.Collections.Generic;
using System.Linq;
using TheEmptying.Presentation.Content;
using UnityEditor;
using UnityEngine;

namespace TheEmptying.Editor.Content
{
    /// <summary>Shows every content asset's own validation problems, and any duplicate-Id problem, as help boxes above its normal Inspector.</summary>
    [CustomEditor(typeof(ContentAssetBase), true)]
    public sealed class ContentAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (ContentAssetBase)target;

            List<string> problems = asset.ValidateSelf().ToList();
            problems.AddRange(ContentAssetDuplicateCheck.FindDuplicateIdProblems(asset));

            foreach (string problem in problems)
            {
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }

            DrawDefaultInspector();
        }
    }
}
