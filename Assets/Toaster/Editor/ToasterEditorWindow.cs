using UnityEngine;
using UnityEditor;

namespace Toaster
{
    public class ToasterEditorWindow : EditorWindow
    {
        private VoxelBaker selectedBaker;
        private Vector2 scrollPos;
        private Editor bakerEditor;

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

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("Bake Voxels", GUILayout.Height(36)))
            {
                selectedBaker.Bake();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Wire Visualizers"))
                selectedBaker.WireVisualizers();
            if (GUILayout.Button("Select Baker"))
                Selection.activeGameObject = selectedBaker.gameObject;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Grid info
            DrawGridInfo();

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
    }
}
