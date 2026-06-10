using System;
using System.IO;
using Project.Runtime;
using StateSync.Diagnostics;
using StateSync.Server;
using StateSync.Sync;
using StateSync.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Project.Editor
{
    public static class SceneBuilder
    {
        const string ScenePath          = "Assets/_Project/Scenes/Main.unity";
        const string MaterialsDir       = "Assets/_Project/Art/Materials";
        const string PearlBluePath      = "Assets/_Project/Art/Materials/PearlBlueCarPaint.mat";
        const string PedestalMatPath    = "Assets/_Project/Art/Materials/PedestalMatte.mat";
        const string BackdropMatPath    = "Assets/_Project/Art/Materials/BackdropMatte.mat";
        const string FloorMatPath       = "Assets/_Project/Art/Materials/FloorMatte.mat";
        const string VolumeProfilePath  = "Assets/_Project/Settings/HDRP/SkyandFogSettingsProfile.asset";
        const string CarPaintSampleName = "CoatedCarPaint_Stacklit.mat";

        public static void BuildPanelScene()
        {
            try
            {
                EnsureFolder(MaterialsDir);

                Material panelMat    = CreatePearlBlueCarPaint();
                Material pedestalMat = CreateLitMatte(PedestalMatPath, new Color(0.04f, 0.04f, 0.04f), 0.20f);
                Material backdropMat = CreateLitMatte(BackdropMatPath, new Color(0.18f, 0.18f, 0.18f), 0.10f);
                Material floorMat    = CreateLitMatte(FloorMatPath,    new Color(0.06f, 0.06f, 0.06f), 0.35f);

                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                ClearRootObjects(scene);

                // Panel: 22cm x 30cm x 3mm.
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Panel";
                panel.transform.position = new Vector3(0f, 0.60f, 0f);
                panel.transform.localScale = new Vector3(0.22f, 0.30f, 0.003f);
                panel.GetComponent<MeshRenderer>().sharedMaterial = panelMat;
                panel.AddComponent<PanelRotator>();

                // Pedestal: dark matte disc under the panel.
                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pedestal.name = "Pedestal";
                pedestal.transform.position = new Vector3(0f, 0.435f, 0f);
                pedestal.transform.localScale = new Vector3(0.40f, 0.01f, 0.40f);
                pedestal.GetComponent<MeshRenderer>().sharedMaterial = pedestalMat;

                // Backdrop quad behind the panel, facing the camera.
                var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backdrop.name = "Backdrop";
                backdrop.transform.position = new Vector3(0f, 1.00f, 1.50f);
                backdrop.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                backdrop.transform.localScale = new Vector3(3.0f, 2.5f, 1.0f);
                backdrop.GetComponent<MeshRenderer>().sharedMaterial = backdropMat;

                // Floor quad.
                var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
                floor.name = "Floor";
                floor.transform.position = new Vector3(0f, 0.42f, 0f);
                floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                floor.transform.localScale = new Vector3(6.0f, 6.0f, 1.0f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

                // Camera.
                var cameraGO = new GameObject("Main Camera");
                cameraGO.tag = "MainCamera";
                cameraGO.transform.position = new Vector3(0f, 0.60f, -0.55f);
                cameraGO.transform.LookAt(new Vector3(0f, 0.60f, 0f));
                var cam = cameraGO.AddComponent<Camera>();
                cam.fieldOfView = 38f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 50f;
                cameraGO.AddComponent<AudioListener>();
                cameraGO.AddComponent<HDAdditionalCameraData>();
                var orbit = cameraGO.AddComponent<OrbitCamera>();
                AssignSerializedRef(orbit, "target", panel.transform);

                // Key sun (directional).
                var sunGO = new GameObject("Sun");
                sunGO.transform.position = new Vector3(0f, 3f, 0f);
                sunGO.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
                var sun = sunGO.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.96f, 0.88f);
                sun.shadows = LightShadows.Soft;
                sun.lightUnit = LightUnit.Lux;
                sun.intensity = 12000f;
                sunGO.AddComponent<HDAdditionalLightData>();

                // Studio area lights.
                CreateAreaLight("AreaLight_KeyL",
                    position: new Vector3(-0.8f, 0.9f, -0.3f),
                    aimAt:    new Vector3(0f, 0.60f, 0f),
                    color:    new Color(0.90f, 0.95f, 1.00f),
                    sizeMeters: new Vector2(1.0f, 0.5f),
                    intensityNits: 1500f);

                CreateAreaLight("AreaLight_Rim",
                    position: new Vector3(0.8f, 0.7f, 0.3f),
                    aimAt:    new Vector3(0f, 0.60f, 0f),
                    color:    new Color(1.00f, 0.92f, 0.82f),
                    sizeMeters: new Vector2(0.8f, 0.5f),
                    intensityNits: 800f);

                // Global Volume.
                var volumeGO = new GameObject("Global Volume");
                var volume = volumeGO.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 0;
                volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

                // StateSync — WebSocket bridge for host/follower panel rotation.
                // Both publisher and receiver are attached; each self-disables based on ConnectionConfig.IsHost.
                var netGO = new GameObject("StateSync");
                netGO.AddComponent<MainThreadDispatcher>();
                var server = netGO.AddComponent<WsServer>();
                var publisher = netGO.AddComponent<TransformRotationPublisher>();
                AssignSerializedRef(publisher, "target", panel.transform);
                AssignSerializedRef(publisher, "server", server);
                var receiver = netGO.AddComponent<TransformRotationReceiver>();
                AssignSerializedRef(receiver, "target", panel.transform);
                AssignSerializedRef(receiver, "server", server);
                // Lets external WS clients (the Electron control panel) drive the
                // panel rotation while running as host; the publisher echoes the
                // resulting pose back to every client.
                var applier = netGO.AddComponent<HostRotationApplier>();
                AssignSerializedRef(applier, "target", panel.transform);
                AssignSerializedRef(applier, "server", server);
                var overlay = netGO.AddComponent<NetStatusOverlay>();
                AssignSerializedRef(overlay, "server", server);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[SceneBuilder] Done.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneBuilder] Failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        // ---------------------------------------------------------------------

        static void ClearRootObjects(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            var leaf = Path.GetFileName(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Material CreatePearlBlueCarPaint()
        {
            string samplePath = FindSampleMaterial(CarPaintSampleName);
            if (samplePath == null)
            {
                throw new FileNotFoundException(
                    $"{CarPaintSampleName} not found in Assets/Samples. " +
                    "Run Project.Editor.SampleImporter.ImportMaterialSamples first.");
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(PearlBluePath) != null)
            {
                AssetDatabase.DeleteAsset(PearlBluePath);
            }

            if (!AssetDatabase.CopyAsset(samplePath, PearlBluePath))
            {
                throw new Exception($"CopyAsset {samplePath} -> {PearlBluePath} failed.");
            }
            AssetDatabase.ImportAsset(PearlBluePath);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(PearlBluePath);
            if (mat == null)
            {
                throw new Exception($"Could not load cloned material at {PearlBluePath}.");
            }

            Color pearlBlue = new Color(0.04f, 0.10f, 0.35f, 1f);

            // ShaderGraph-generated StackLit may expose any of these names; try them all.
            TrySetColor(mat, "_BaseColor",      pearlBlue);
            TrySetColor(mat, "_BaseColor0",     pearlBlue);
            TrySetColor(mat, "_Color",          pearlBlue);
            TrySetColor(mat, "Base_Color",      pearlBlue);

            TrySetFloat(mat, "_Smoothness",     0.92f);
            TrySetFloat(mat, "_Smoothness0",    0.92f);
            TrySetFloat(mat, "_Metallic",       0.90f);
            TrySetFloat(mat, "_Metallic0",      0.90f);
            TrySetFloat(mat, "_CoatMask",       1.00f);
            TrySetFloat(mat, "_CoatSmoothness", 0.97f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        static string FindSampleMaterial(string fileName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            return null;
        }

        static Material CreateLitMatte(string path, Color baseColor, float smoothness)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("HDRP/Lit");
                if (shader == null)
                {
                    throw new Exception("Shader 'HDRP/Lit' not found.");
                }
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            TrySetColor(mat, "_BaseColor", baseColor);
            TrySetFloat(mat, "_Smoothness", smoothness);
            TrySetFloat(mat, "_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void TrySetColor(Material m, string name, Color c)
        {
            if (m.HasProperty(name)) m.SetColor(name, c);
        }

        static void TrySetFloat(Material m, string name, float v)
        {
            if (m.HasProperty(name)) m.SetFloat(name, v);
        }

        static void CreateAreaLight(string name, Vector3 position, Vector3 aimAt,
                                    Color color, Vector2 sizeMeters, float intensityNits)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.LookAt(aimAt);

            var light = go.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.color = color;
            light.shadows = LightShadows.Soft;
            light.areaSize = sizeMeters;
            light.lightUnit = LightUnit.Nits;
            light.intensity = intensityNits;
            go.AddComponent<HDAdditionalLightData>();
        }

        static void AssignSerializedRef(UnityEngine.Object component, string propertyName, UnityEngine.Object value)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[SceneBuilder] Property '{propertyName}' not found on {component.GetType().Name}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
