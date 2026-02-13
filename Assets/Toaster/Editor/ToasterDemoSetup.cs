using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Toaster
{
    public static class ToasterDemoSetup
    {
        private const string ComputeShaderPath = "Assets/Toaster/Runtime/Shaders/Voxelizer.compute";
        private const string VolumeShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterVolume.shader";
        private const string DebugSliceShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterDebugSlice.shader";
        private const string HeatmapShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterDebugHeatmap.shader";
        private const string IsosurfaceShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterDebugIsosurface.shader";
        private const string MultiSliceShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterDebugMultiSlice.shader";
        private const string PointCloudShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterDebugPointCloud.shader";

        [MenuItem("Toaster/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            SetupScene(bakeImmediately: false);
        }

        [MenuItem("Toaster/Create Demo Scene && Bake")]
        public static void CreateDemoSceneAndBake()
        {
            SetupScene(bakeImmediately: true);
        }

        private static void SetupScene(bool bakeImmediately)
        {
            // Create a new empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Camera ---
            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            cameraGO.transform.position = new Vector3(0, 5, -12);
            cameraGO.transform.rotation = Quaternion.Euler(20, 0, 0);
            cameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            // --- Lights ---
            var dirLightGO = new GameObject("Directional Light");
            var dirLight = dirLightGO.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1f, 0.95f, 0.85f);
            dirLight.intensity = 1.5f;
            dirLightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

            CreatePointLight("Red Point Light", new Vector3(-3, 3, 0), Color.red, 8f, 2f);
            CreatePointLight("Blue Point Light", new Vector3(3, 3, 0), Color.blue, 8f, 2f);
            CreatePointLight("Green Point Light", new Vector3(0, 3, 3), Color.green, 8f, 2f);

            // --- URP Lit Material helper ---
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Appliance.LogWarning("Could not find URP Lit shader. Using default.");
                urpLit = Shader.Find("Standard");
            }

            // --- Test Geometry ---
            // Floor
            var floor = CreatePrimitive("Floor", PrimitiveType.Plane, Vector3.zero,
                new Vector3(2, 1, 2), CreateMaterial(urpLit, "Floor_Mat", new Color(0.5f, 0.5f, 0.5f)));

            // Colored cubes
            CreatePrimitive("Red Cube", PrimitiveType.Cube, new Vector3(-3, 1, 0),
                Vector3.one * 1.5f, CreateMaterial(urpLit, "Red_Mat", Color.red));

            CreatePrimitive("Green Cube", PrimitiveType.Cube, new Vector3(3, 1, 0),
                Vector3.one * 1.5f, CreateMaterial(urpLit, "Green_Mat", Color.green));

            CreatePrimitive("Blue Cube", PrimitiveType.Cube, new Vector3(0, 1, 3),
                Vector3.one * 1.5f, CreateMaterial(urpLit, "Blue_Mat", Color.blue));

            // Sphere
            CreatePrimitive("Yellow Sphere", PrimitiveType.Sphere, new Vector3(0, 1.5f, 0),
                Vector3.one * 2f, CreateMaterial(urpLit, "Yellow_Mat", Color.yellow));

            // Extra cubes
            CreatePrimitive("Magenta Cube", PrimitiveType.Cube, new Vector3(-2, 1, -3),
                Vector3.one, CreateMaterial(urpLit, "Magenta_Mat", Color.magenta));

            CreatePrimitive("Cyan Cube", PrimitiveType.Cube, new Vector3(2, 1, -3),
                Vector3.one, CreateMaterial(urpLit, "Cyan_Mat", Color.cyan));

            // --- Toaster Baker ---
            var bakerGO = new GameObject("Toaster Baker");
            var baker = bakerGO.AddComponent<VoxelBaker>();
            baker.boundsSize = new Vector3(12, 8, 12);
            baker.voxelSize = 0.25f;

            var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            if (computeShader != null)
            {
                baker.voxelizerCompute = computeShader;
            }
            else
            {
                Appliance.LogWarning($"Could not find compute shader at {ComputeShaderPath}. Assign manually.");
            }

            // --- Fog Volume ---
            Material volumeMat = null;
            var volumeShader = AssetDatabase.LoadAssetAtPath<Shader>(VolumeShaderPath);
            if (volumeShader != null)
            {
                volumeMat = new Material(volumeShader);
                volumeMat.name = "ToasterVolume_Mat";
            }

            var fogVolumeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fogVolumeGO.name = "Fog Volume";
            fogVolumeGO.transform.position = Vector3.zero;
            fogVolumeGO.transform.localScale = new Vector3(12, 8, 12);
            Object.DestroyImmediate(fogVolumeGO.GetComponent<BoxCollider>());
            if (volumeMat != null)
                fogVolumeGO.GetComponent<MeshRenderer>().sharedMaterial = volumeMat;
            // Don't mark fog volume as static — it's not geometry to voxelize
            GameObjectUtility.SetStaticEditorFlags(fogVolumeGO, 0);

            // --- Debug Slice Quad ---
            Material debugMat = null;
            var debugShader = AssetDatabase.LoadAssetAtPath<Shader>(DebugSliceShaderPath);
            if (debugShader != null)
            {
                debugMat = new Material(debugShader);
                debugMat.name = "ToasterDebugSlice_Mat";
            }

            var debugQuadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            debugQuadGO.name = "Debug Slice";
            debugQuadGO.transform.position = new Vector3(8, 4, 0);
            debugQuadGO.transform.localScale = new Vector3(6, 4, 1);
            Object.DestroyImmediate(debugQuadGO.GetComponent<MeshCollider>());
            if (debugMat != null)
                debugQuadGO.GetComponent<MeshRenderer>().sharedMaterial = debugMat;
            GameObjectUtility.SetStaticEditorFlags(debugQuadGO, 0);

            // --- Debug Heatmap Quad ---
            Material heatmapMat = null;
            var heatmapShader = AssetDatabase.LoadAssetAtPath<Shader>(HeatmapShaderPath);
            if (heatmapShader != null)
            {
                heatmapMat = new Material(heatmapShader);
                heatmapMat.name = "ToasterHeatmap_Mat";
            }

            var heatmapQuadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            heatmapQuadGO.name = "Debug Heatmap";
            heatmapQuadGO.transform.position = new Vector3(8, 4, 7);
            heatmapQuadGO.transform.localScale = new Vector3(6, 4, 1);
            Object.DestroyImmediate(heatmapQuadGO.GetComponent<MeshCollider>());
            if (heatmapMat != null)
                heatmapQuadGO.GetComponent<MeshRenderer>().sharedMaterial = heatmapMat;
            GameObjectUtility.SetStaticEditorFlags(heatmapQuadGO, 0);

            // --- Isosurface Volume ---
            Material isoMat = null;
            var isoShader = AssetDatabase.LoadAssetAtPath<Shader>(IsosurfaceShaderPath);
            if (isoShader != null)
            {
                isoMat = new Material(isoShader);
                isoMat.name = "ToasterIsosurface_Mat";
            }

            var isoVolumeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            isoVolumeGO.name = "Debug Isosurface";
            isoVolumeGO.transform.position = new Vector3(0, 0, 16);
            isoVolumeGO.transform.localScale = new Vector3(12, 8, 12);
            Object.DestroyImmediate(isoVolumeGO.GetComponent<BoxCollider>());
            if (isoMat != null)
                isoVolumeGO.GetComponent<MeshRenderer>().sharedMaterial = isoMat;
            GameObjectUtility.SetStaticEditorFlags(isoVolumeGO, 0);

            // --- Multi-Slice Volume ---
            Material multiSliceMat = null;
            var multiSliceShader = AssetDatabase.LoadAssetAtPath<Shader>(MultiSliceShaderPath);
            if (multiSliceShader != null)
            {
                multiSliceMat = new Material(multiSliceShader);
                multiSliceMat.name = "ToasterMultiSlice_Mat";
            }

            var multiSliceGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            multiSliceGO.name = "Debug MultiSlice";
            multiSliceGO.transform.position = new Vector3(0, 0, -16);
            multiSliceGO.transform.localScale = new Vector3(12, 8, 12);
            Object.DestroyImmediate(multiSliceGO.GetComponent<BoxCollider>());
            if (multiSliceMat != null)
                multiSliceGO.GetComponent<MeshRenderer>().sharedMaterial = multiSliceMat;
            GameObjectUtility.SetStaticEditorFlags(multiSliceGO, 0);

            // --- Point Cloud Renderer (procedural — no mesh needed) ---
            Material pointCloudMat = null;
            var pointCloudShader = AssetDatabase.LoadAssetAtPath<Shader>(PointCloudShaderPath);
            if (pointCloudShader != null)
            {
                pointCloudMat = new Material(pointCloudShader);
                pointCloudMat.name = "ToasterPointCloud_Mat";
            }

            var pointCloudGO = new GameObject("Debug Point Cloud");
            var pcRenderer = pointCloudGO.AddComponent<VoxelPointCloudRenderer>();
            if (pointCloudMat != null)
                pcRenderer.pointCloudMaterial = pointCloudMat;
            GameObjectUtility.SetStaticEditorFlags(pointCloudGO, 0);

            // --- Bake if requested ---
            if (bakeImmediately)
            {
                baker.Bake();

                // Wire the baked texture to all visualizer materials
                if (baker.voxelGrid != null)
                {
                    Material[] vizMats = { volumeMat, debugMat, heatmapMat, isoMat, multiSliceMat };
                    foreach (var m in vizMats)
                    {
                        if (m != null)
                            m.SetTexture("_VolumeTex", baker.voxelGrid);
                    }

                    // Wire point cloud renderer
                    pcRenderer.ConfigureFromBaker(baker);

                    Appliance.Log("Demo scene created and baked! Visualizers wired.");
                }
            }
            else
            {
                Appliance.Log("Demo scene created. Select 'Toaster Baker' and use context menu > Bake Voxels.");
            }

            // Focus scene view on the setup
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(Vector3.zero, Quaternion.Euler(30, -20, 0), 18f);
            }
        }

        private static GameObject CreatePrimitive(string name, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;

            if (material != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Mark as ContributeGI static
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);

            return go;
        }

        private static GameObject CreatePointLight(string name, Vector3 position, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            go.transform.position = position;
            return go;
        }

        private static Material CreateMaterial(Shader shader, string name, Color color)
        {
            var mat = new Material(shader);
            mat.name = name;
            mat.SetColor("_BaseColor", color);
            return mat;
        }
    }
}
