using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Project.Editor
{
    public static class HdrpQualityTuner
    {
        const string AssetPath = "Assets/_Project/Settings/HDRP/HDRP Balanced.asset";

        public static void EnableSsr()
        {
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetPath);
                if (asset == null)
                {
                    Debug.LogError($"[HdrpQualityTuner] Asset not found: {AssetPath}");
                    EditorApplication.Exit(1);
                    return;
                }

                var so = new SerializedObject(asset);
                var prop = FindByName(so, "supportSSR");
                if (prop == null)
                {
                    Debug.LogError("[HdrpQualityTuner] Could not find 'supportSSR' property. HDRP serialized schema may have changed.");
                    EditorApplication.Exit(1);
                    return;
                }

                bool previous = prop.boolValue;
                prop.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);

                Debug.Log($"[HdrpQualityTuner] supportSSR: {previous} -> true on {AssetPath} (propertyPath={prop.propertyPath})");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HdrpQualityTuner] Failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        static SerializedProperty FindByName(SerializedObject so, string targetName)
        {
            // First try the canonical nested path.
            var direct = so.FindProperty("m_RenderPipelineSettings.supportSSR");
            if (direct != null) return direct;

            // Fall back to deep scan.
            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    if (it.name == targetName) return it.Copy();
                } while (it.NextVisible(true));
            }
            return null;
        }
    }
}
