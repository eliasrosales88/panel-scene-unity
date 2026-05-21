using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace Project.Editor
{
    public static class SampleImporter
    {
        const string HdrpPackage = "com.unity.render-pipelines.high-definition";
        const string TargetSampleName = "Material Samples";

        public static void ImportMaterialSamples()
        {
            try
            {
                var samples = Sample.FindByPackage(HdrpPackage, null).ToList();
                if (samples.Count == 0)
                {
                    Debug.LogError($"[SampleImporter] No samples advertised by {HdrpPackage}.");
                    EditorApplication.Exit(1);
                    return;
                }

                Sample? hit = null;
                foreach (var s in samples)
                {
                    if (string.Equals(s.displayName, TargetSampleName, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = s;
                        break;
                    }
                }

                if (hit == null)
                {
                    Debug.LogError(
                        $"[SampleImporter] Sample '{TargetSampleName}' not found. Available: " +
                        string.Join(", ", samples.Select(s => s.displayName)));
                    EditorApplication.Exit(1);
                    return;
                }

                var sample = hit.Value;
                Debug.Log($"[SampleImporter] Importing '{sample.displayName}' -> {sample.importPath} (alreadyImported={sample.isImported})");
                bool ok = sample.Import(Sample.ImportOptions.OverridePreviousImports | Sample.ImportOptions.HideImportWindow);
                if (!ok)
                {
                    Debug.LogError("[SampleImporter] Sample.Import returned false.");
                    EditorApplication.Exit(1);
                    return;
                }

                AssetDatabase.Refresh();
                Debug.Log("[SampleImporter] Done.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SampleImporter] Failed: {ex}");
                EditorApplication.Exit(1);
            }
        }
    }
}
