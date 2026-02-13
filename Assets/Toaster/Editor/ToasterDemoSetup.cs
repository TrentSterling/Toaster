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
        private const string TracerComputePath = "Assets/Toaster/Runtime/Shaders/ToasterTracer.compute";

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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Appliance.LogWarning("Could not find URP Lit shader. Using default.");
                urpLit = Shader.Find("Standard");
            }

            // ============================================================
            // Camera — looking down a corridor
            // ============================================================
            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 1f);
            cameraGO.transform.position = new Vector3(0, 3, -14);
            cameraGO.transform.rotation = Quaternion.Euler(10, 0, 0);
            cameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            // ============================================================
            // Lights — dramatic colored setup
            // ============================================================
            // Dim directional (moonlight)
            var dirLightGO = new GameObject("Directional Light");
            var dirLight = dirLightGO.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(0.3f, 0.4f, 0.7f);
            dirLight.intensity = 0.4f;
            dirLightGO.transform.rotation = Quaternion.Euler(60, -30, 0);

            // Warm spotlight through archway (key light for fog)
            var spotGO = new GameObject("Spot Key");
            var spotLight = spotGO.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.color = new Color(1f, 0.7f, 0.3f);
            spotLight.intensity = 8f;
            spotLight.range = 25f;
            spotLight.spotAngle = 60f;
            spotGO.transform.position = new Vector3(0, 8, 8);
            spotGO.transform.rotation = Quaternion.Euler(50, 180, 0);

            // Colored point lights for volumetric color bleed
            CreatePointLight("Red Neon", new Vector3(-5, 2.5f, 2), new Color(1f, 0.1f, 0.05f), 10f, 4f);
            CreatePointLight("Cyan Neon", new Vector3(5, 2.5f, 2), new Color(0.05f, 0.8f, 1f), 10f, 4f);
            CreatePointLight("Purple Under", new Vector3(0, 0.3f, 0), new Color(0.6f, 0.1f, 1f), 8f, 3f);
            CreatePointLight("Warm Interior", new Vector3(0, 3.5f, 6), new Color(1f, 0.9f, 0.6f), 12f, 2.5f);
            CreatePointLight("Green Accent", new Vector3(-3, 1f, -5), new Color(0.2f, 1f, 0.3f), 6f, 2f);

            // ============================================================
            // Materials
            // ============================================================
            var darkConcrete = CreateMaterial(urpLit, "DarkConcrete_Mat", new Color(0.15f, 0.14f, 0.13f));
            var lightConcrete = CreateMaterial(urpLit, "LightConcrete_Mat", new Color(0.35f, 0.33f, 0.3f));
            var metalDark = CreateMaterial(urpLit, "MetalDark_Mat", new Color(0.08f, 0.08f, 0.1f));
            metalDark.SetFloat("_Smoothness", 0.8f);
            metalDark.SetFloat("_Metallic", 0.9f);
            var warmWood = CreateMaterial(urpLit, "WarmWood_Mat", new Color(0.4f, 0.25f, 0.12f));

            // Emissive materials — these glow and bleed into the fog
            var redEmissive = CreateEmissiveMaterial(urpLit, "RedEmissive_Mat", Color.black, new Color(4f, 0.2f, 0.1f));
            var cyanEmissive = CreateEmissiveMaterial(urpLit, "CyanEmissive_Mat", Color.black, new Color(0.1f, 2f, 3f));
            var purpleEmissive = CreateEmissiveMaterial(urpLit, "PurpleEmissive_Mat", Color.black, new Color(1.5f, 0.2f, 3f));

            // ============================================================
            // Floor — large reflective ground plane
            // ============================================================
            CreatePrimitive("Floor", PrimitiveType.Plane, new Vector3(0, 0, 0),
                new Vector3(3, 1, 3), metalDark);

            // ============================================================
            // Corridor walls — two long walls creating a passage
            // ============================================================
            // Left wall
            CreatePrimitive("Wall Left", PrimitiveType.Cube, new Vector3(-6, 3, 2),
                new Vector3(0.5f, 6, 20), darkConcrete);
            // Right wall
            CreatePrimitive("Wall Right", PrimitiveType.Cube, new Vector3(6, 3, 2),
                new Vector3(0.5f, 6, 20), darkConcrete);

            // Back wall with gap (doorway)
            CreatePrimitive("Back Wall Left", PrimitiveType.Cube, new Vector3(-3.5f, 3, 10),
                new Vector3(5f, 6, 0.5f), darkConcrete);
            CreatePrimitive("Back Wall Right", PrimitiveType.Cube, new Vector3(3.5f, 3, 10),
                new Vector3(5f, 6, 0.5f), darkConcrete);
            // Lintel above doorway
            CreatePrimitive("Lintel", PrimitiveType.Cube, new Vector3(0, 5, 10),
                new Vector3(2f, 1, 0.5f), darkConcrete);

            // ============================================================
            // Columns — archway pillars
            // ============================================================
            for (int i = 0; i < 4; i++)
            {
                float z = -4 + i * 4f;
                CreatePrimitive($"Pillar L{i}", PrimitiveType.Cylinder, new Vector3(-4, 2.5f, z),
                    new Vector3(0.6f, 2.5f, 0.6f), lightConcrete);
                CreatePrimitive($"Pillar R{i}", PrimitiveType.Cylinder, new Vector3(4, 2.5f, z),
                    new Vector3(0.6f, 2.5f, 0.6f), lightConcrete);
            }

            // ============================================================
            // Arch crossbeams between pillars
            // ============================================================
            for (int i = 0; i < 4; i++)
            {
                float z = -4 + i * 4f;
                CreatePrimitive($"Beam {i}", PrimitiveType.Cube, new Vector3(0, 5, z),
                    new Vector3(8.5f, 0.4f, 0.6f), lightConcrete);
            }

            // Ceiling slab
            CreatePrimitive("Ceiling", PrimitiveType.Cube, new Vector3(0, 5.5f, 2),
                new Vector3(12, 0.3f, 18), darkConcrete);

            // ============================================================
            // Interior objects — furniture-like geometry for occlusion
            // ============================================================
            // Central altar/pedestal
            CreatePrimitive("Pedestal Base", PrimitiveType.Cube, new Vector3(0, 0.4f, 3),
                new Vector3(2, 0.8f, 2), lightConcrete);
            CreatePrimitive("Pedestal Top", PrimitiveType.Cube, new Vector3(0, 1.0f, 3),
                new Vector3(1.5f, 0.4f, 1.5f), warmWood);

            // Sphere on pedestal (reflective focal point)
            var focalSphere = CreatePrimitive("Focal Sphere", PrimitiveType.Sphere, new Vector3(0, 1.8f, 3),
                Vector3.one * 1f, metalDark);

            // Scattered boxes (debris / crates)
            CreatePrimitive("Crate 1", PrimitiveType.Cube, new Vector3(-3, 0.5f, -2),
                new Vector3(1, 1, 1), warmWood);
            CreatePrimitive("Crate 2", PrimitiveType.Cube, new Vector3(-2.5f, 0.35f, -3.5f),
                new Vector3(0.7f, 0.7f, 0.7f), warmWood);
            CreatePrimitive("Crate 3", PrimitiveType.Cube, new Vector3(2, 0.5f, -1),
                new Vector3(1, 1, 1.2f), warmWood);
            // Rotated crate
            var tiltedCrate = CreatePrimitive("Crate Tilted", PrimitiveType.Cube, new Vector3(3.5f, 0.6f, -4),
                new Vector3(0.8f, 0.8f, 0.8f), warmWood);
            tiltedCrate.transform.rotation = Quaternion.Euler(0, 35, 12);

            // ============================================================
            // Emissive panels — neon strips on walls (bleed color into fog)
            // ============================================================
            // Red neon strip (left wall)
            CreatePrimitive("Neon Red", PrimitiveType.Cube, new Vector3(-5.7f, 2.5f, 2),
                new Vector3(0.05f, 0.3f, 8f), redEmissive);

            // Cyan neon strip (right wall)
            CreatePrimitive("Neon Cyan", PrimitiveType.Cube, new Vector3(5.7f, 2.5f, 2),
                new Vector3(0.05f, 0.3f, 8f), cyanEmissive);

            // Purple floor strip (center)
            CreatePrimitive("Neon Purple", PrimitiveType.Cube, new Vector3(0, 0.02f, 2),
                new Vector3(0.3f, 0.02f, 12f), purpleEmissive);

            // ============================================================
            // Steps — leading up to back doorway
            // ============================================================
            for (int i = 0; i < 3; i++)
            {
                float z = 7.5f + i * 0.8f;
                float y = 0.15f + i * 0.3f;
                CreatePrimitive($"Step {i}", PrimitiveType.Cube, new Vector3(0, y, z),
                    new Vector3(3, 0.3f, 0.8f), lightConcrete);
            }

            // ============================================================
            // Toaster Baker — bounds cover the whole corridor
            // ============================================================
            var bakerGO = new GameObject("Toaster Baker");
            var baker = bakerGO.AddComponent<VoxelBaker>();
            baker.boundsSize = new Vector3(16, 8, 24);
            baker.voxelSize = 0.25f;
            bakerGO.transform.position = new Vector3(0, 3, 1);

            var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            if (computeShader != null)
                baker.voxelizerCompute = computeShader;
            else
                Appliance.LogWarning($"Could not find compute shader at {ComputeShaderPath}. Assign manually.");

            // --- Toaster Tracer ---
            var tracer = bakerGO.AddComponent<ToasterTracer>();
            tracer.baker = baker;
            tracer.raysPerVoxel = 64;
            tracer.maxBounces = 3;
            var tracerCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(TracerComputePath);
            if (tracerCompute != null)
                tracer.tracerCompute = tracerCompute;
            else
                Appliance.LogWarning($"Could not find tracer compute at {TracerComputePath}. Assign manually.");

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
            fogVolumeGO.transform.position = bakerGO.transform.position;
            fogVolumeGO.transform.localScale = baker.boundsSize;
            Object.DestroyImmediate(fogVolumeGO.GetComponent<BoxCollider>());
            if (volumeMat != null)
                fogVolumeGO.GetComponent<MeshRenderer>().sharedMaterial = volumeMat;
            var toasterVol = fogVolumeGO.AddComponent<ToasterVolume>();
            toasterVol.baker = baker;
            GameObjectUtility.SetStaticEditorFlags(fogVolumeGO, 0);

            // ============================================================
            // Debug visualizers (offset to the side)
            // ============================================================
            var debugParent = new GameObject("Debug Visualizers");
            debugParent.transform.position = new Vector3(14, 0, 0);

            // Slice
            Material debugMat = null;
            var debugShader = AssetDatabase.LoadAssetAtPath<Shader>(DebugSliceShaderPath);
            if (debugShader != null)
            {
                debugMat = new Material(debugShader);
                debugMat.name = "ToasterDebugSlice_Mat";
            }
            var debugQuadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            debugQuadGO.name = "Debug Slice";
            debugQuadGO.transform.SetParent(debugParent.transform, false);
            debugQuadGO.transform.localPosition = new Vector3(0, 4, 0);
            debugQuadGO.transform.localScale = new Vector3(6, 4, 1);
            Object.DestroyImmediate(debugQuadGO.GetComponent<MeshCollider>());
            if (debugMat != null) debugQuadGO.GetComponent<MeshRenderer>().sharedMaterial = debugMat;
            GameObjectUtility.SetStaticEditorFlags(debugQuadGO, 0);

            // Heatmap
            Material heatmapMat = null;
            var heatmapShader = AssetDatabase.LoadAssetAtPath<Shader>(HeatmapShaderPath);
            if (heatmapShader != null)
            {
                heatmapMat = new Material(heatmapShader);
                heatmapMat.name = "ToasterHeatmap_Mat";
            }
            var heatmapQuadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            heatmapQuadGO.name = "Debug Heatmap";
            heatmapQuadGO.transform.SetParent(debugParent.transform, false);
            heatmapQuadGO.transform.localPosition = new Vector3(0, 4, 7);
            heatmapQuadGO.transform.localScale = new Vector3(6, 4, 1);
            Object.DestroyImmediate(heatmapQuadGO.GetComponent<MeshCollider>());
            if (heatmapMat != null) heatmapQuadGO.GetComponent<MeshRenderer>().sharedMaterial = heatmapMat;
            GameObjectUtility.SetStaticEditorFlags(heatmapQuadGO, 0);

            // Isosurface
            Material isoMat = null;
            var isoShader = AssetDatabase.LoadAssetAtPath<Shader>(IsosurfaceShaderPath);
            if (isoShader != null)
            {
                isoMat = new Material(isoShader);
                isoMat.name = "ToasterIsosurface_Mat";
            }
            var isoVolumeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            isoVolumeGO.name = "Debug Isosurface";
            isoVolumeGO.transform.SetParent(debugParent.transform, false);
            isoVolumeGO.transform.localPosition = new Vector3(0, 4, 14);
            isoVolumeGO.transform.localScale = new Vector3(12, 8, 12);
            Object.DestroyImmediate(isoVolumeGO.GetComponent<BoxCollider>());
            if (isoMat != null) isoVolumeGO.GetComponent<MeshRenderer>().sharedMaterial = isoMat;
            GameObjectUtility.SetStaticEditorFlags(isoVolumeGO, 0);

            // Multi-Slice
            Material multiSliceMat = null;
            var multiSliceShader = AssetDatabase.LoadAssetAtPath<Shader>(MultiSliceShaderPath);
            if (multiSliceShader != null)
            {
                multiSliceMat = new Material(multiSliceShader);
                multiSliceMat.name = "ToasterMultiSlice_Mat";
            }
            var multiSliceGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            multiSliceGO.name = "Debug MultiSlice";
            multiSliceGO.transform.SetParent(debugParent.transform, false);
            multiSliceGO.transform.localPosition = new Vector3(0, 4, -14);
            multiSliceGO.transform.localScale = new Vector3(12, 8, 12);
            Object.DestroyImmediate(multiSliceGO.GetComponent<BoxCollider>());
            if (multiSliceMat != null) multiSliceGO.GetComponent<MeshRenderer>().sharedMaterial = multiSliceMat;
            GameObjectUtility.SetStaticEditorFlags(multiSliceGO, 0);

            // Point Cloud
            Material pointCloudMat = null;
            var pointCloudShader = AssetDatabase.LoadAssetAtPath<Shader>(PointCloudShaderPath);
            if (pointCloudShader != null)
            {
                pointCloudMat = new Material(pointCloudShader);
                pointCloudMat.name = "ToasterPointCloud_Mat";
            }
            var pointCloudGO = new GameObject("Debug Point Cloud");
            pointCloudGO.transform.SetParent(debugParent.transform, false);
            var pcRenderer = pointCloudGO.AddComponent<VoxelPointCloudRenderer>();
            if (pointCloudMat != null) pcRenderer.pointCloudMaterial = pointCloudMat;
            GameObjectUtility.SetStaticEditorFlags(pointCloudGO, 0);

            // ============================================================
            // Bake
            // ============================================================
            if (bakeImmediately)
            {
                baker.Bake();

                if (baker.voxelGrid != null)
                {
                    Material[] vizMats = { volumeMat, debugMat, heatmapMat, isoMat, multiSliceMat };
                    foreach (var m in vizMats)
                    {
                        if (m != null)
                            m.SetTexture("_VolumeTex", baker.voxelGrid);
                    }

                    pcRenderer.ConfigureFromBaker(baker);

                    if (tracer.tracerCompute != null)
                    {
                        tracer.Trace();
                        if (tracer.lightingGrid != null && volumeMat != null)
                            volumeMat.SetTexture("_VolumeTex", tracer.lightingGrid);
                    }

                    Appliance.Log("Demo scene created, baked, and traced! Visualizers wired.");
                }
            }
            else
            {
                Appliance.Log("Demo scene created. Select 'Toaster Baker' and use context menu > Bake Voxels.");
            }

            // Focus scene view on the corridor
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 3, 1), Quaternion.Euler(15, 0, 0), 20f);
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

            // Remove colliders on non-floor objects to reduce clutter
            var col = go.GetComponent<Collider>();
            if (col != null && type != PrimitiveType.Plane)
                Object.DestroyImmediate(col);

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

        private static Material CreateEmissiveMaterial(Shader shader, string name, Color baseColor, Color emissionColor)
        {
            var mat = new Material(shader);
            mat.name = name;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetColor("_EmissionColor", emissionColor);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            return mat;
        }
    }
}
