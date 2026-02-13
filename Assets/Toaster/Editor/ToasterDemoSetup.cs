using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
            // Courtyard area — beyond the back doorway (cool blue/green)
            // Overlaps with corridor by ~3m for volume blending test
            // ============================================================
            // Courtyard ground
            CreatePrimitive("Courtyard Floor", PrimitiveType.Plane, new Vector3(0, 0, 20),
                new Vector3(2.5f, 1, 2), lightConcrete);

            // Low walls / planters
            CreatePrimitive("Planter Left", PrimitiveType.Cube, new Vector3(-8, 1, 20),
                new Vector3(0.8f, 2, 12), lightConcrete);
            CreatePrimitive("Planter Right", PrimitiveType.Cube, new Vector3(8, 1, 20),
                new Vector3(0.8f, 2, 12), lightConcrete);

            // Central fountain pedestal
            CreatePrimitive("Fountain Base", PrimitiveType.Cylinder, new Vector3(0, 0.6f, 20),
                new Vector3(2.5f, 0.6f, 2.5f), lightConcrete);
            CreatePrimitive("Fountain Pillar", PrimitiveType.Cylinder, new Vector3(0, 2.0f, 20),
                new Vector3(0.4f, 1.4f, 0.4f), lightConcrete);
            CreatePrimitive("Fountain Top", PrimitiveType.Sphere, new Vector3(0, 3.2f, 20),
                new Vector3(1.2f, 0.6f, 1.2f), lightConcrete);

            // Tall lamp posts with colored lights
            CreatePrimitive("Lamp Post L", PrimitiveType.Cylinder, new Vector3(-4, 2.5f, 17),
                new Vector3(0.15f, 2.5f, 0.15f), metalDark);
            CreatePrimitive("Lamp Post R", PrimitiveType.Cylinder, new Vector3(4, 2.5f, 23),
                new Vector3(0.15f, 2.5f, 0.15f), metalDark);

            // Courtyard emissive — blue glow on fountain
            var blueEmissive = CreateEmissiveMaterial(urpLit, "BlueEmissive_Mat", Color.black, new Color(0.2f, 0.5f, 4f));
            CreatePrimitive("Fountain Glow", PrimitiveType.Cylinder, new Vector3(0, 0.05f, 20),
                new Vector3(3f, 0.05f, 3f), blueEmissive);

            // Courtyard lights — cool tones contrast with warm corridor
            CreatePointLight("Blue Lamp L", new Vector3(-4, 5.2f, 17), new Color(0.2f, 0.4f, 1f), 14f, 5f);
            CreatePointLight("Teal Lamp R", new Vector3(4, 5.2f, 23), new Color(0.1f, 0.9f, 0.7f), 14f, 5f);
            CreatePointLight("Fountain Blue", new Vector3(0, 1.5f, 20), new Color(0.1f, 0.3f, 1f), 8f, 3f);

            // Archway columns at the transition zone
            CreatePrimitive("Arch Pillar OL", PrimitiveType.Cylinder, new Vector3(-3, 3, 12),
                new Vector3(0.5f, 3f, 0.5f), darkConcrete);
            CreatePrimitive("Arch Pillar OR", PrimitiveType.Cylinder, new Vector3(3, 3, 12),
                new Vector3(0.5f, 3f, 0.5f), darkConcrete);
            CreatePrimitive("Arch Beam Outer", PrimitiveType.Cube, new Vector3(0, 5.8f, 12),
                new Vector3(7f, 0.4f, 0.6f), darkConcrete);

            // ============================================================
            // Shared resources
            // ============================================================
            var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            var tracerCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(TracerComputePath);
            Material volumeMat = null;
            var volumeShader = AssetDatabase.LoadAssetAtPath<Shader>(VolumeShaderPath);
            if (volumeShader != null)
            {
                volumeMat = new Material(volumeShader);
                volumeMat.name = "ToasterVolume_Mat";
            }

            // ============================================================
            // Baker 1 — Corridor interior (warm)
            // Covers z=-11 to z=13
            // ============================================================
            var bakerGO = new GameObject("Toaster Baker (Corridor)");
            var baker = bakerGO.AddComponent<VoxelBaker>();
            baker.boundsSize = new Vector3(16, 8, 24);
            baker.voxelSize = 0.25f;
            bakerGO.transform.position = new Vector3(0, 3, 1);

            if (computeShader != null)
                baker.voxelizerCompute = computeShader;
            else
                Appliance.LogWarning($"Could not find compute shader at {ComputeShaderPath}. Assign manually.");

            var tracer = bakerGO.AddComponent<ToasterTracer>();
            tracer.baker = baker;
            tracer.raysPerVoxel = 64;
            tracer.maxBounces = 3;
            if (tracerCompute != null)
                tracer.tracerCompute = tracerCompute;

            var fogVolumeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fogVolumeGO.name = "Fog Volume (Corridor)";
            fogVolumeGO.transform.position = bakerGO.transform.position;
            fogVolumeGO.transform.localScale = baker.boundsSize;
            Object.DestroyImmediate(fogVolumeGO.GetComponent<BoxCollider>());
            if (volumeMat != null)
                fogVolumeGO.GetComponent<MeshRenderer>().sharedMaterial = volumeMat;
            var toasterVol = fogVolumeGO.AddComponent<ToasterVolume>();
            toasterVol.baker = baker;
            toasterVol.densityMultiplier = 3f;
            toasterVol.intensityMultiplier = 2f;
            toasterVol.edgeFalloff = 0.15f;
            GameObjectUtility.SetStaticEditorFlags(fogVolumeGO, 0);

            // ============================================================
            // Baker 2 — Courtyard exterior (cool)
            // Covers z=10 to z=26 (overlaps corridor z=10..13)
            // ============================================================
            var baker2GO = new GameObject("Toaster Baker (Courtyard)");
            var baker2 = baker2GO.AddComponent<VoxelBaker>();
            baker2.boundsSize = new Vector3(20, 10, 16);
            baker2.voxelSize = 0.25f;
            baker2GO.transform.position = new Vector3(0, 4, 18);

            if (computeShader != null)
                baker2.voxelizerCompute = computeShader;

            var tracer2 = baker2GO.AddComponent<ToasterTracer>();
            tracer2.baker = baker2;
            tracer2.raysPerVoxel = 64;
            tracer2.maxBounces = 3;
            if (tracerCompute != null)
                tracer2.tracerCompute = tracerCompute;

            Material volumeMat2 = null;
            if (volumeShader != null)
            {
                volumeMat2 = new Material(volumeShader);
                volumeMat2.name = "ToasterVolume2_Mat";
            }

            var fogVolume2GO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fogVolume2GO.name = "Fog Volume (Courtyard)";
            fogVolume2GO.transform.position = baker2GO.transform.position;
            fogVolume2GO.transform.localScale = baker2.boundsSize;
            Object.DestroyImmediate(fogVolume2GO.GetComponent<BoxCollider>());
            if (volumeMat2 != null)
                fogVolume2GO.GetComponent<MeshRenderer>().sharedMaterial = volumeMat2;
            var toasterVol2 = fogVolume2GO.AddComponent<ToasterVolume>();
            toasterVol2.baker = baker2;
            toasterVol2.densityMultiplier = 2f;
            toasterVol2.intensityMultiplier = 2f;
            toasterVol2.edgeFalloff = 0.15f;
            GameObjectUtility.SetStaticEditorFlags(fogVolume2GO, 0);

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
            // Bake both volumes
            // ============================================================
            if (bakeImmediately)
            {
                // Bake corridor
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
                }

                // Bake courtyard
                baker2.Bake();
                if (baker2.voxelGrid != null)
                {
                    if (tracer2.tracerCompute != null)
                    {
                        tracer2.Trace();
                        if (tracer2.lightingGrid != null && volumeMat2 != null)
                            volumeMat2.SetTexture("_VolumeTex", tracer2.lightingGrid);
                    }
                }

                ConfigureFroxelFeature();
                Appliance.Log("Demo scene created with 2 volumes, baked, and traced! Visualizers wired.");
            }
            else
            {
                Appliance.Log("Demo scene created with 2 volumes. Select bakers and use Bake Voxels.");
            }

            // Focus scene view on the transition zone between volumes
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 4, 10), Quaternion.Euler(20, 0, 0), 25f);
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

        private static void ConfigureFroxelFeature()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
                return;

            var pipeType = pipeline.GetType();
            var rendererListField = pipeType.GetField("m_RendererDataList",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (rendererListField == null)
                return;

            var rendererList = rendererListField.GetValue(pipeline) as ScriptableRendererData[];
            if (rendererList == null || rendererList.Length == 0)
                return;

            var rendererData = rendererList[0];
            ToasterFroxelFeature froxelFeature = null;
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature is ToasterFroxelFeature f)
                {
                    froxelFeature = f;
                    break;
                }
            }

            if (froxelFeature == null)
            {
                Appliance.Log("No ToasterFroxelFeature on renderer — skipping froxel config. Add it via Toaster > Baker Window.");
                return;
            }

            froxelFeature.settings.fogDensity = 0.03f;
            froxelFeature.settings.fogIntensity = 1.5f;
            froxelFeature.settings.lightDensityBoost = 0.8f;
            froxelFeature.settings.maxDistance = 200f;
            froxelFeature.settings.scatterAnisotropy = 0.3f;

            EditorUtility.SetDirty(froxelFeature);
            AssetDatabase.SaveAssets();
            Appliance.Log("Configured froxel feature for demo scene (density=0.03, intensity=1.5, lightBoost=0.8).");
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
