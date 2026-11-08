using System;
using System.IO;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// A throwaway copy of the real Assets/StreamingAssets/Content/core source,
    /// so milestone tests exercise the actual shipped data (manifests,
    /// settings, event definitions) rather than a hand-rolled stand-in.
    /// Deletes itself on Dispose.
    /// </summary>
    internal sealed class TempCoreContentCopy : IDisposable
    {
        public string FolderPath { get; }

        public TempCoreContentCopy()
        {
            string realCoreFolder = Path.Combine(Application.streamingAssetsPath, "Content", "core");
            FolderPath = Path.Combine(Path.GetTempPath(), "TheEmptyingMilestone1Tests", Guid.NewGuid().ToString("N"));
            CopyDirectory(realCoreFolder, FolderPath);
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                if (filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(filePath, Path.Combine(destinationDir, Path.GetFileName(filePath)), overwrite: true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(FolderPath, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
