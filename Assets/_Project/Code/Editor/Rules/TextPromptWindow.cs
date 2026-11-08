using System;
using UnityEditor;
using UnityEngine;

namespace TheEmptying.Editor.Rules
{
    /// <summary>A minimal modal text prompt (Unity has no built-in one) for naming a new file, a new rule, a duplicate, or a rename.</summary>
    internal sealed class TextPromptWindow : EditorWindow
    {
        private string _value;
        private Action<string> _onConfirm;

        public static void Show(string title, string initialValue, Action<string> onConfirm)
        {
            var window = CreateInstance<TextPromptWindow>();
            window.titleContent = new GUIContent(title);
            window._value = initialValue;
            window._onConfirm = onConfirm;
            window.minSize = new Vector2(360f, 80f);
            window.maxSize = new Vector2(360f, 80f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUI.SetNextControlName("TextPromptField");
            _value = EditorGUILayout.TextField(_value);
            EditorGUI.FocusTextInControl("TextPromptField");

            bool pressedReturn = Event.current.type == EventType.KeyDown &&
                                  (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                Close();
            }

            if (GUILayout.Button("OK", GUILayout.Width(80)) || pressedReturn)
            {
                Action<string> confirm = _onConfirm;
                string value = _value;
                Close();
                confirm?.Invoke(value);
            }

            GUILayout.EndHorizontal();
        }
    }
}
