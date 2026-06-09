using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StateSync.Protocol;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Project.Editor
{
    public static class BuildScript
    {
        const string DefaultOutputDir = "Builds/Windows";
        const string ExecutableName = "PanelScene.exe";
        const string MetadataFileName = "unity-build.json";
        const string BuildOutputArg = "-buildOutput";

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

            string outputDir = ResolveOutputDir();
            Directory.CreateDirectory(outputDir);
            string exePath = Path.Combine(outputDir, ExecutableName);

            var opts = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = exePath,
                target           = BuildTarget.StandaloneWindows64,
                options          = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var ok = report.summary.result == BuildResult.Succeeded;

            Debug.Log($"[BuildScript] Result={report.summary.result} " +
                      $"Size={report.summary.totalSize}B Time={report.summary.totalTime}");

            if (ok)
            {
                WriteMetadata(outputDir);
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        static string ResolveOutputDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], BuildOutputArg, StringComparison.Ordinal))
                {
                    string v = args[i + 1];
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return DefaultOutputDir;
        }

        static void WriteMetadata(string outputDir)
        {
            string metadataPath = Path.Combine(outputDir, MetadataFileName);
            string commit = TryReadGitCommit();
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string unityVersion = UnityEngine.Application.unityVersion;
            int protocolVersion = JsonCodec.Version;

            var sb = new StringBuilder(256);
            sb.Append("{\n");
            sb.Append("  \"executable\": \"").Append(ExecutableName).Append("\",\n");
            sb.Append("  \"buildTimestamp\": \"").Append(timestamp).Append("\",\n");
            sb.Append("  \"gitCommit\": \"").Append(EscapeJson(commit ?? "unknown")).Append("\",\n");
            sb.Append("  \"unityVersion\": \"").Append(EscapeJson(unityVersion)).Append("\",\n");
            sb.Append("  \"protocolVersion\": ").Append(protocolVersion.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"buildTarget\": \"StandaloneWindows64\"\n");
            sb.Append("}\n");

            File.WriteAllText(metadataPath, sb.ToString());
            Debug.Log($"[BuildScript] Wrote metadata → {metadataPath} (commit={commit ?? "unknown"}, protocol=v{protocolVersion})");
        }

        static string TryReadGitCommit()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(2000)) { try { p.Kill(); } catch { } return null; }
                if (p.ExitCode != 0) return null;
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }

        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
