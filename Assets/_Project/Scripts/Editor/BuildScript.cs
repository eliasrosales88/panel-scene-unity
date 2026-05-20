using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Project.Editor
{
    public static class BuildScript
    {
        public static void BuildWindowsStandalone()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildScript] EditorBuildSettings.scenes is empty. Add scenes via File > Build Settings before building.");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory("Builds/Windows");

            var opts = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = "Builds/Windows/PanelScene.exe",
                target           = BuildTarget.StandaloneWindows64,
                options          = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var ok = report.summary.result == BuildResult.Succeeded;

            Debug.Log($"[BuildScript] Result={report.summary.result} " +
                      $"Size={report.summary.totalSize}B Time={report.summary.totalTime}");

            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
