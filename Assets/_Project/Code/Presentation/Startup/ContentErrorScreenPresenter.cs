using System;
using System.Collections.Generic;
using System.IO;
using TheEmptying.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Startup
{
    /// <summary>
    /// R5b: shown instead of the game when content fails to load at
    /// startup - every problem as one line, "Save list to file" and
    /// "Exit". Plain UI Toolkit code, same style as the HUD and menus.
    /// Works the same whether the Game scene was reached from the main
    /// menu or opened directly, since both paths run through
    /// GameBootstrap.Awake().
    /// </summary>
    public static class ContentErrorScreenPresenter
    {
        public static void Build(VisualElement root, IReadOnlyList<ContentValidationProblem> problems)
        {
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            root.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 1f);

            var panel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    paddingLeft = 32,
                    paddingRight = 32,
                    paddingTop = 24,
                    paddingBottom = 24,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f)
                }
            };

            var title = new Label("The game can't start: content problems")
            {
                style =
                {
                    color = Color.white,
                    fontSize = 24,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 16
                }
            };
            panel.Add(title);

            var scrollView = new ScrollView
            {
                style =
                {
                    width = 700,
                    height = 320,
                    marginBottom = 16,
                    backgroundColor = new Color(0f, 0f, 0f, 0.3f)
                }
            };

            foreach (ContentValidationProblem problem in problems)
            {
                var line = new Label(FormatProblem(problem))
                {
                    style = { color = Color.white, whiteSpace = WhiteSpace.Normal, marginBottom = 4, marginLeft = 8 }
                };
                scrollView.Add(line);
            }

            panel.Add(scrollView);

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };
            var resultLabel = new Label { style = { color = Color.white } };

            var saveButton = new Button(() => SaveToFile(problems, resultLabel)) { text = "Save list to file", style = { width = 180, height = 32, marginRight = 8 } };
            var exitButton = new Button(GameExit.Quit) { text = "Exit", style = { width = 120, height = 32 } };
            buttonRow.Add(saveButton);
            buttonRow.Add(exitButton);

            panel.Add(buttonRow);
            panel.Add(resultLabel);

            root.Add(panel);
        }

        private static string FormatProblem(ContentValidationProblem problem)
        {
            string asset = !string.IsNullOrEmpty(problem.File) ? problem.File : (problem.ItemId ?? "?");
            string field = problem.Field ?? "?";
            return $"{asset} ({field}): {problem.Message}";
        }

        private static void SaveToFile(IReadOnlyList<ContentValidationProblem> problems, Label resultLabel)
        {
            try
            {
                string fileName = $"content_errors_{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.txt";
                string logsFolder = Path.Combine(Application.dataPath, "..", "Logs");
                Directory.CreateDirectory(logsFolder);

                var lines = new List<string>();
                foreach (ContentValidationProblem problem in problems)
                {
                    lines.Add(FormatProblem(problem));
                }

                File.WriteAllLines(Path.Combine(logsFolder, fileName), lines);
                resultLabel.text = $"Saved to {fileName}";
            }
            catch (Exception ex)
            {
                resultLabel.text = $"Save failed: {ex.Message}";
            }
        }
    }
}
