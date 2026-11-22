#if UNITY_EDITOR
using UnityEditor;
#else
using UnityEngine;
#endif

namespace TheEmptying.Presentation
{
    /// <summary>Shared by every "Exit" button: stops Play mode in the Editor, quits in a real build.</summary>
    public static class GameExit
    {
        public static void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
