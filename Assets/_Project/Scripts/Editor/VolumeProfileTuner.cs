using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Project.Editor
{
    public static class VolumeProfileTuner
    {
        const string ProfilePath = "Assets/_Project/Settings/HDRP/SkyandFogSettingsProfile.asset";

        // SkyType is an int parameter inside VisualEnvironment (custom skies can register).
        // Values come from the HDRP SkyType enum: HDRI=1, PhysicallyBased=4.
        const int SkyTypeHdri = 1;

        public static void AddHdriSky()
        {
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
                if (profile == null)
                {
                    Debug.LogError($"[VolumeProfileTuner] Volume Profile not found: {ProfilePath}");
                    EditorApplication.Exit(1);
                    return;
                }

                Cubemap cubemap = TryFindBuiltInHdri();
                bool hasHdri = cubemap != null;

                if (!profile.TryGet<VisualEnvironment>(out var visEnv))
                    visEnv = profile.Add<VisualEnvironment>(true);

                if (!profile.TryGet<HDRISky>(out var hdri))
                    hdri = profile.Add<HDRISky>(true);

                if (hasHdri)
                {
                    hdri.hdriSky.overrideState = true;
                    hdri.hdriSky.value = cubemap;
                    hdri.exposure.overrideState = true;
                    hdri.exposure.value = 0f;
                    hdri.multiplier.overrideState = true;
                    hdri.multiplier.value = 1f;

                    visEnv.skyType.overrideState = true;
                    visEnv.skyType.value = SkyTypeHdri;
                    visEnv.skyAmbientMode.overrideState = true;
                    visEnv.skyAmbientMode.value = SkyAmbientMode.Dynamic;

                    Debug.Log($"[VolumeProfileTuner] HDRI sky set to: {AssetDatabase.GetAssetPath(cubemap)}");
                }
                else
                {
                    Debug.LogWarning("[VolumeProfileTuner] No built-in HDRI cubemap found in project or HDRP package cache. " +
                                     "Keeping VisualEnvironment on PhysicallyBasedSky. " +
                                     "Drop a .exr/.hdr cubemap into Assets/_Project/Art/Textures/ and rerun this method to switch.");
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                Debug.Log("[VolumeProfileTuner] Done.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VolumeProfileTuner] Failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        static Cubemap TryFindBuiltInHdri()
        {
            string[] guids = AssetDatabase.FindAssets("t:Cubemap");
            Cubemap firstAny = null;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var cm = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (cm == null) continue;
                if (firstAny == null) firstAny = cm;
                // Prefer assets that look like HDRI skies.
                if (path.IndexOf("Skybox", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("HDRI", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return cm;
                }
            }
            return firstAny;
        }
    }
}
