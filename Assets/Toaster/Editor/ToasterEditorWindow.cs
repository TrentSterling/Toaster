using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toaster
{
    public class ToasterEditorWindow : EditorWindow
    {
        private VoxelBaker selectedBaker;
        private Vector2 scrollPos;
        private Editor bakerEditor;

        // Froxel feature reference (cached)
        private ToasterFroxelFeature cachedFroxelFeature;

        // Shader/compute paths for froxel auto-wire
        private const string FroxelComputePath = "Assets/Toaster/Runtime/Shaders/ToasterFroxel.compute";
        private const string FroxelApplyShaderPath = "Assets/Toaster/Runtime/Shaders/ToasterFroxelApply.shader";
        private const string BlueNoisePath = "Assets/Toaster/Runtime/Textures/BlueNoise128.png";

        [MenuItem("Toaster/Baker Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<ToasterEditorWindow>("Toaster Baker");
            window.minSize = new Vector2(320, 400);
        }

        void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            FindBaker();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            if (bakerEditor != null)
                DestroyImmediate(bakerEditor);
        }

        void OnSelectionChanged()
        {
            var go = Selection.activeGameObject;
            if (go != null)
            {
                var baker = go.GetComponent<VoxelBaker>();
                if (baker != null)
                    selectedBaker = baker;
            }
            Repaint();
        }

        void FindBaker()
        {
            if (selectedBaker != null) return;
            selectedBaker = FindFirstObjectByType<VoxelBaker>();
        }

        void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Header
            EditorGUILayout.Space(4);
            GUILayout.Label($"Toaster {Appliance.Version}", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Baker selection
            selectedBaker = (VoxelBaker)EditorGUILayout.ObjectField(
                "Baker", selectedBaker, typeof(VoxelBaker), true);

            if (selectedBaker == null)
            {
                EditorGUILayout.HelpBox("No VoxelBaker found. Create a demo scene or add a VoxelBaker component.", MessageType.Info);

                if (GUILayout.Button("Create Demo Scene", GUILayout.Height(30)))
                    ToasterDemoSetup.CreateDemoScene();

                if (GUILayout.Button("Create Demo Scene && Bake", GUILayout.Height(30)))
                    ToasterDemoSetup.CreateDemoSceneAndBake();

                EditorGUILayout.Space(8);
                DrawFroxelSetup();

                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(8);

            // Draw the baker's inspector inline
            if (bakerEditor == null || bakerEditor.target != selectedBaker)
            {
                if (bakerEditor != null) DestroyImmediate(bakerEditor);
                bakerEditor = Editor.CreateEditor(selectedBaker);
            }
            bakerEditor.OnInspectorGUI();

            EditorGUILayout.Space(8);

            // Bake buttons
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("Bake Voxels", GUILayout.Height(36)))
                selectedBaker.Bake();
            GUI.backgroundColor = new Color(0.8f, 0.5f, 0.2f);
            if (GUILayout.Button("Trace Lighting", GUILayout.Height(36)))
            {
                var tracer = FindFirstObjectByType<ToasterTracer>();
                if (tracer != null)
                    tracer.Trace();
                else
                    Appliance.LogWarning("No ToasterTracer found in scene.");
            }
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Wire Visualizers"))
                selectedBaker.WireVisualizers();
            if (GUILayout.Button("Auto-Fit Bounds"))
                selectedBaker.AutoFitBounds();
            if (GUILayout.Button("Select Baker"))
                Selection.activeGameObject = selectedBaker.gameObject;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Grid info
            DrawGridInfo();

            EditorGUILayout.Space(8);

            // Froxel fog setup
            DrawFroxelSetup();

            EditorGUILayout.Space(8);

            // Preview
            DrawGridPreview();

            EditorGUILayout.EndScrollView();
        }

        void DrawGridInfo()
        {
            EditorGUILayout.LabelField("Grid Info", EditorStyles.boldLabel);

            var grid = selectedBaker.GetActiveGrid();
            if (grid == null)
            {
                EditorGUILayout.HelpBox("No baked grid. Click 'Bake Voxels' to generate.", MessageType.Warning);
                return;
            }

            int resX = Mathf.CeilToInt(selectedBaker.boundsSize.x / selectedBaker.voxelSize);
            int resY = Mathf.CeilToInt(selectedBaker.boundsSize.y / selectedBaker.voxelSize);
            int resZ = Mathf.CeilToInt(selectedBaker.boundsSize.z / selectedBaker.voxelSize);
            int totalVoxels = resX * resY * resZ;

            // Memory estimate: ARGBHalf = 8 bytes per voxel
            float memoryMB = totalVoxels * 8f / (1024f * 1024f);

            EditorGUILayout.LabelField("Resolution", $"{resX} x {resY} x {resZ}");
            EditorGUILayout.LabelField("Total Voxels", totalVoxels.ToString("N0"));
            EditorGUILayout.LabelField("Est. Memory", $"{memoryMB:F1} MB");

            if (selectedBaker.serializedGrid != null)
                EditorGUILayout.LabelField("Serialized", AssetDatabase.GetAssetPath(selectedBaker.serializedGrid));
            else
                EditorGUILayout.LabelField("Serialized", "Not saved");

            string source = selectedBaker.voxelGrid != null ? "RenderTexture (live)" : "Texture3D (serialized)";
            EditorGUILayout.LabelField("Active Source", source);
        }

        void DrawGridPreview()
        {
            EditorGUILayout.LabelField("Preview (first slice)", EditorStyles.boldLabel);

            var grid = selectedBaker.GetActiveGrid();
            if (grid == null) return;

            float previewSize = EditorGUIUtility.currentViewWidth - 40;
            Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize * 0.66f);

            if (grid is RenderTexture rt)
                EditorGUI.DrawPreviewTexture(previewRect, rt, null, ScaleMode.ScaleToFit);
            else if (grid is Texture3D tex3D)
                EditorGUI.DrawPreviewTexture(previewRect, tex3D, null, ScaleMode.ScaleToFit);
        }

        // ============================================================
        // Froxel Fog Setup
        // ============================================================

        void DrawFroxelSetup()
        {
            EditorGUILayout.LabelField("Froxel Fog", EditorStyles.boldLabel);

            // Find active renderer
            var rendererData = FindActiveRenderer();
            if (rendererData == null)
            {
                EditorGUILayout.HelpBox("No active URP renderer found. Check Project Settings > Graphics.", MessageType.Warning);
                return;
            }

            // Check if froxel feature already exists
            var froxelFeature = FindFroxelFeature(rendererData);

            if (froxelFeature != null)
            {
                EditorGUILayout.HelpBox("Froxel fog feature is active on the renderer.", MessageType.Info);

                // Show status of references
                bool hasCompute = froxelFeature.froxelCompute != null;
                bool hasShader = froxelFeature.applyShader != null;
                bool hasNoise = froxelFeature.blueNoise != null;

                if (!hasCompute || !hasShader)
                {
                    EditorGUILayout.HelpBox(
                        (!hasCompute ? "Missing: Froxel compute shader\n" : "") +
                        (!hasShader ? "Missing: Apply shader\n" : "") +
                        (!hasNoise ? "Missing: Blue noise texture (run Toaster > Generate Blue Noise)" : ""),
                        MessageType.Warning);

                    if (GUILayout.Button("Auto-Wire References"))
                    {
                        AutoWireFroxelReferences(froxelFeature);
                    }
                }
                else if (!hasNoise)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.HelpBox("Blue noise texture not assigned. Generate one first.", MessageType.Info);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Generate Blue Noise"))
                        BlueNoiseGenerator.Generate();
                    if (GUILayout.Button("Auto-Wire"))
                        AutoWireFroxelReferences(froxelFeature);
                    EditorGUILayout.EndHorizontal();
                }

                // Quick settings — edit directly without navigating to renderer asset
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Quick Settings", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                var s = froxelFeature.settings;

                s.fogDensity = EditorGUILayout.Slider("Fog Density", s.fogDensity, 0f, 1f);
                s.fogIntensity = EditorGUILayout.Slider("Fog Intensity", s.fogIntensity, 0f, 10f);
                s.ambientColor = EditorGUILayout.ColorField("Ambient Color", s.ambientColor);
                s.scatterAnisotropy = EditorGUILayout.Slider("Scatter Anisotropy", s.scatterAnisotropy, -0.99f, 0.99f);
                s.maxDistance = EditorGUILayout.Slider("Max Distance", s.maxDistance, 10f, 500f);
                s.enableTemporal = EditorGUILayout.Toggle("Temporal Reprojection", s.enableTemporal);
                if (s.enableTemporal)
                    s.temporalBlendAlpha = EditorGUILayout.Slider("  Blend Alpha", s.temporalBlendAlpha, 0.01f, 1f);

                EditorGUILayout.Space(2);
                s.debugMode = (ToasterFroxelFeature.DebugMode)EditorGUILayout.EnumPopup("Debug View", s.debugMode);

                if (EditorGUI.EndChangeCheck())
                    EditorUtility.SetDirty(froxelFeature);

                EditorGUILayout.Space(4);
                if (GUILayout.Button("Select Renderer Asset (all settings)"))
                    Selection.activeObject = rendererData;
            }
            else
            {
                EditorGUILayout.HelpBox("Froxel fog not configured. Add the feature to your URP renderer.", MessageType.Info);

                GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
                if (GUILayout.Button("Add Froxel Fog to Renderer", GUILayout.Height(30)))
                {
                    AddFroxelFeatureToRenderer(rendererData);
                }
                GUI.backgroundColor = Color.white;
            }
        }

        static ScriptableRendererData FindActiveRenderer()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
                return null;

            // Use reflection to get the default renderer data
            // URP stores it in m_RendererDataList[m_DefaultRendererIndex]
            var pipeType = pipeline.GetType();
            var rendererListField = pipeType.GetField("m_RendererDataList",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (rendererListField == null)
                return null;

            var rendererList = rendererListField.GetValue(pipeline) as ScriptableRendererData[];
            if (rendererList == null || rendererList.Length == 0)
                return null;

            return rendererList[0];
        }

        static ToasterFroxelFeature FindFroxelFeature(ScriptableRendererData rendererData)
        {
            if (rendererData == null) return null;

            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature is ToasterFroxelFeature froxel)
                    return froxel;
            }
            return null;
        }

        static void AddFroxelFeatureToRenderer(ScriptableRendererData rendererData)
        {
            // Create the feature as a sub-asset of the renderer
            var feature = ScriptableObject.CreateInstance<ToasterFroxelFeature>();
            feature.name = "ToasterFroxelFeature";

            // Auto-wire references
            AutoWireFroxelReferences(feature);

            // Add to renderer's feature list via serialized property
            Undo.RecordObject(rendererData, "Add Toaster Froxel Feature");

            // Use SerializedObject to properly add the feature
            var so = new SerializedObject(rendererData);
            var featuresProp = so.FindProperty("m_RendererFeatures");
            int newIndex = featuresProp.arraySize;
            featuresProp.InsertArrayElementAtIndex(newIndex);
            featuresProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = feature;

            // Add as sub-asset so it's saved with the renderer
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            so.ApplyModifiedProperties();

            // Force renderer to rebuild
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            Appliance.Log("Added ToasterFroxelFeature to renderer. Check the renderer asset for settings.");
        }

        static void AutoWireFroxelReferences(ToasterFroxelFeature feature)
        {
            bool changed = false;

            if (feature.froxelCompute == null)
            {
                var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(FroxelComputePath);
                if (cs != null) { feature.froxelCompute = cs; changed = true; }
            }

            if (feature.applyShader == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(FroxelApplyShaderPath);
                if (shader != null) { feature.applyShader = shader; changed = true; }
            }

            if (feature.blueNoise == null)
            {
                var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(BlueNoisePath);
                if (noise != null) { feature.blueNoise = noise; changed = true; }
            }

            if (changed)
            {
                EditorUtility.SetDirty(feature);
                Appliance.Log("Auto-wired froxel feature references.");
            }
        }
    }
}
