using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Toaster
{
    [ExecuteAlways]
    public class VoxelBaker : MonoBehaviour
    {
        [Header("Preset")]
        [Tooltip("Quick quality preset. Applies voxel size on Bake. Set to Raw for fast iteration, Burnt for final quality.")]
        public Appliance.BrowningLevel browningLevel = Appliance.BrowningLevel.Light;

        [Header("Settings")]
        [Tooltip("Size of each voxel in world units. Smaller = higher quality, more memory. Overridden by Browning Level on bake.")]
        [Range(0.05f, 2.0f)]
        public float voxelSize = 0.25f;

        [Tooltip("World-space size of the voxel grid bounding box.")]
        public Vector3 boundsSize = new Vector3(12, 8, 12);

        [Tooltip("Compute shader for GPU voxelization. Assign Voxelizer.compute.")]
        public ComputeShader voxelizerCompute;

        [Header("Filtering")]
        [Tooltip("Only voxelize objects with the ContributeGI static flag. Skips Toaster visualizers and non-static objects.")]
        public bool requireContributeGI = true;

        [Tooltip("Skip any renderer whose material uses a Toaster/* shader (fog volume, debug quads, etc).")]
        public bool skipToasterShaders = true;

        [Header("Auto-Wire")]
        [Tooltip("After bake, automatically find all Toaster visualizer materials and point cloud renderers in the scene and wire them to the baked grid.")]
        public bool autoWireVisualizers = true;

        [Header("Debug")]
        [Tooltip("Draw yellow wireframe of the bake volume in the Scene view.")]
        public bool drawGizmos = true;

        [Header("Serialization")]
        [Tooltip("Save baked grid as a Texture3D asset. Survives domain reloads and scene saves.")]
        public bool serializeAfterBake = true;

        // The Result — runtime RT (lost on reload)
        [HideInInspector]
        public RenderTexture voxelGrid;

        // Serialized result — persists across reloads
        [Tooltip("Saved voxel grid asset. Auto-populated after bake if serializeAfterBake is enabled.")]
        public Texture3D serializedGrid;

        // Buffers
        private ComputeBuffer vertBuffer;
        private ComputeBuffer normalBuffer;
        private ComputeBuffer uvBuffer;
        private ComputeBuffer indexBuffer;
        private ComputeBuffer accumBuffer;

        private void OnDestroy()
        {
            ReleaseBuffers();
            if (voxelGrid != null)
            {
                voxelGrid.Release();
                voxelGrid = null;
            }
        }

        [ContextMenu("Bake Voxels")]
        public void Bake()
        {
            if (voxelizerCompute == null)
            {
                Appliance.LogWarning("No compute shader assigned!");
                return;
            }

            // Apply browning level preset
            ApplyBrowningPreset();

            ReleaseBuffers();

            // 1. Setup Voxel Grid
            Vector3 worldMin = transform.position - boundsSize / 2;
            int resX = Mathf.CeilToInt(boundsSize.x / voxelSize);
            int resY = Mathf.CeilToInt(boundsSize.y / voxelSize);
            int resZ = Mathf.CeilToInt(boundsSize.z / voxelSize);

            Appliance.Log($"Baking voxel grid: {resX}x{resY}x{resZ} ({resX * resY * resZ} voxels)");

            if (voxelGrid != null) voxelGrid.Release();
            voxelGrid = new RenderTexture(resX, resY, 0, RenderTextureFormat.ARGBHalf);
            voxelGrid.dimension = TextureDimension.Tex3D;
            voxelGrid.volumeDepth = resZ;
            voxelGrid.enableRandomWrite = true;
            voxelGrid.filterMode = FilterMode.Bilinear;
            voxelGrid.wrapMode = TextureWrapMode.Clamp;
            voxelGrid.Create();

            // 2. Clear the grid
            int clearKernel = voxelizerCompute.FindKernel("ClearGrid");
            voxelizerCompute.SetTexture(clearKernel, "VoxelGrid", voxelGrid);
            voxelizerCompute.SetInts("GridResolution", resX, resY, resZ);
            voxelizerCompute.Dispatch(clearKernel,
                Mathf.CeilToInt(resX / 8f),
                Mathf.CeilToInt(resY / 8f),
                Mathf.CeilToInt(resZ / 8f));

            // 2b. Create and clear atomic accumulation buffer (4 uints per voxel: R, G, B, Count)
            int totalVoxels = resX * resY * resZ;
            accumBuffer = new ComputeBuffer(totalVoxels * 4, sizeof(uint));

            int clearAccumKernel = voxelizerCompute.FindKernel("ClearAccum");
            voxelizerCompute.SetBuffer(clearAccumKernel, "AccumBuffer", accumBuffer);
            voxelizerCompute.SetInts("GridResolution", resX, resY, resZ);
            voxelizerCompute.Dispatch(clearAccumKernel,
                Mathf.CeilToInt(totalVoxels * 4 / 64f), 1, 1);

            // 3. Setup Temp Meta Texture
            RenderTexture metaTempRT = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);

            // 4. Find all renderers
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Toaster_MetaBake";

            int kernel = voxelizerCompute.FindKernel("VoxelizeMesh");
            voxelizerCompute.SetTexture(kernel, "VoxelGrid", voxelGrid);
            voxelizerCompute.SetBuffer(kernel, "AccumBuffer", accumBuffer);
            voxelizerCompute.SetVector("WorldBoundsMin", worldMin);
            voxelizerCompute.SetFloat("VoxelSize", voxelSize);
            voxelizerCompute.SetInts("GridResolution", resX, resY, resZ);

            int objectCount = 0;
            int triangleCount = 0;

            foreach (var rend in renderers)
            {
                MeshFilter mf = rend.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                // --- Filtering ---
                // Skip objects using Toaster shaders (fog volumes, debug visualizers)
                if (skipToasterShaders && rend.sharedMaterial != null &&
                    rend.sharedMaterial.shader != null &&
                    rend.sharedMaterial.shader.name.StartsWith("Toaster/"))
                    continue;

#if UNITY_EDITOR
                // Skip objects without ContributeGI static flag
                if (requireContributeGI)
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(rend.gameObject);
                    if ((flags & StaticEditorFlags.ContributeGI) == 0)
                        continue;
                }
#endif

                Mesh mesh = mf.sharedMesh;

                UploadMeshData(mesh);
                voxelizerCompute.SetBuffer(kernel, "Vertices", vertBuffer);
                voxelizerCompute.SetBuffer(kernel, "Normals", normalBuffer);
                voxelizerCompute.SetBuffer(kernel, "UVs", uvBuffer);
                voxelizerCompute.SetBuffer(kernel, "Indices", indexBuffer);
                voxelizerCompute.SetMatrix("LocalToWorld", rend.transform.localToWorldMatrix);

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Material mat = rend.sharedMaterials[Mathf.Min(i, rend.sharedMaterials.Length - 1)];

                    int metaPass = mat.FindPass("Meta");
                    if (metaPass == -1)
                    {
                        Appliance.LogWarning($"Material '{mat.name}' has no Meta pass, skipping.");
                        continue;
                    }

                    // --- STEP A: Bake albedo/emission via Meta Pass ---
                    // Fill with material base color as fallback for safety
                    Color baseColor = mat.HasColor("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;

                    cmd.Clear();
                    cmd.SetRenderTarget(metaTempRT);
                    cmd.ClearRenderTarget(true, true, baseColor);

                    // URP Meta Pass CBUFFER setup — required for UV-space rasterization
                    cmd.SetGlobalVector("unity_MetaVertexControl", new Vector4(1, 0, 0, 0));
                    cmd.SetGlobalVector("unity_MetaFragmentControl", new Vector4(1, 0, 0, 0));
                    cmd.SetGlobalVector("unity_LightmapST", new Vector4(1, 1, 0, 0));

                    // If mesh lacks UV1 (lightmap UVs), the Meta Pass collapses
                    // vertices to (0,0). Temporarily inject UV1 = UV0 if the mesh
                    // is writable; otherwise the _BaseColor fill serves as fallback.
                    bool generatedUV1 = false;
                    if ((mesh.uv2 == null || mesh.uv2.Length == 0) && mesh.isReadable)
                    {
                        if (mesh.uv != null && mesh.uv.Length > 0)
                        {
                            mesh.SetUVs(1, mesh.uv);
                            generatedUV1 = true;
                        }
                    }

                    cmd.DrawRenderer(rend, mat, i, metaPass);
                    Graphics.ExecuteCommandBuffer(cmd);

                    // Restore mesh — remove injected UV1
                    if (generatedUV1)
                        mesh.SetUVs(1, new System.Collections.Generic.List<Vector2>());

                    // --- STEP B: Voxelize on GPU ---
                    voxelizerCompute.SetTexture(kernel, "MetaTexture", metaTempRT);

                    var subMeshDesc = mesh.GetSubMesh(i);
                    voxelizerCompute.SetInt("IndexOffset", subMeshDesc.indexStart);
                    int subMeshTriCount = subMeshDesc.indexCount / 3;
                    voxelizerCompute.SetInt("TriangleCount", subMeshTriCount);

                    int threadGroups = Mathf.CeilToInt(subMeshTriCount / 64f);
                    voxelizerCompute.Dispatch(kernel, Mathf.Max(1, threadGroups), 1, 1);

                    triangleCount += subMeshTriCount;
                }

                ReleaseBuffers();
                objectCount++;
            }

            RenderTexture.ReleaseTemporary(metaTempRT);
            cmd.Release();

            // 5. Finalize — average accumulated colors and write to voxel grid
            int finalizeKernel = voxelizerCompute.FindKernel("FinalizeGrid");
            voxelizerCompute.SetTexture(finalizeKernel, "VoxelGrid", voxelGrid);
            voxelizerCompute.SetBuffer(finalizeKernel, "AccumBuffer", accumBuffer);
            voxelizerCompute.SetInts("GridResolution", resX, resY, resZ);
            voxelizerCompute.Dispatch(finalizeKernel,
                Mathf.CeilToInt(resX / 8f),
                Mathf.CeilToInt(resY / 8f),
                Mathf.CeilToInt(resZ / 8f));

            // Release accumulation buffer
            if (accumBuffer != null) { accumBuffer.Release(); accumBuffer = null; }

            Appliance.Log($"Bake complete! {objectCount} objects, {triangleCount} triangles.");

            // Diagnostic: readback a slice to verify data was written
            VerifyGrid(resX, resY);

            // Serialize to Texture3D asset
#if UNITY_EDITOR
            if (serializeAfterBake)
                SerializeGrid(resX, resY, resZ);
#endif

            // Auto-wire visualizers
            if (autoWireVisualizers)
                WireVisualizers();
        }

        void VerifyGrid(int resX, int resY)
        {
            var readbackRT = RenderTexture.GetTemporary(resX, resY, 0, RenderTextureFormat.ARGBHalf);
            Graphics.Blit(voxelGrid, readbackRT);
            var prevActive = RenderTexture.active;
            RenderTexture.active = readbackRT;
            var tex = new Texture2D(resX, resY, TextureFormat.RGBAHalf, false);
            tex.ReadPixels(new Rect(0, 0, resX, resY), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(readbackRT);

            int nonZero = 0;
            var pixels = tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r > 0.001f || pixels[i].g > 0.001f || pixels[i].b > 0.001f || pixels[i].a > 0.001f)
                    nonZero++;
            }
            Object.DestroyImmediate(tex);

            if (nonZero > 0)
                Appliance.Log($"Diagnostic: {nonZero}/{pixels.Length} non-zero pixels in first slice of voxel grid.");
            else
                Appliance.LogWarning("Diagnostic: Voxel grid first slice is EMPTY — bake may have failed.");
        }

        void UploadMeshData(Mesh mesh)
        {
            ReleaseBuffers();

            var verts = mesh.vertices;
            vertBuffer = new ComputeBuffer(verts.Length, 12);
            vertBuffer.SetData(verts);

            var normals = mesh.normals;
            if (normals.Length == 0) normals = new Vector3[verts.Length];
            normalBuffer = new ComputeBuffer(normals.Length, 12);
            normalBuffer.SetData(normals);

            // Use UV2 (lightmap) if available, else UV0
            var uvs = mesh.uv2.Length > 0 ? mesh.uv2 : mesh.uv;
            if (uvs.Length == 0) uvs = new Vector2[verts.Length];
            uvBuffer = new ComputeBuffer(uvs.Length, 8);
            uvBuffer.SetData(uvs);

            var indices = mesh.triangles;
            indexBuffer = new ComputeBuffer(indices.Length, 4);
            indexBuffer.SetData(indices);
        }

        void ReleaseBuffers()
        {
            if (vertBuffer != null) { vertBuffer.Release(); vertBuffer = null; }
            if (normalBuffer != null) { normalBuffer.Release(); normalBuffer = null; }
            if (uvBuffer != null) { uvBuffer.Release(); uvBuffer = null; }
            if (indexBuffer != null) { indexBuffer.Release(); indexBuffer = null; }
            if (accumBuffer != null) { accumBuffer.Release(); accumBuffer = null; }
        }

#if UNITY_EDITOR
        void SerializeGrid(int resX, int resY, int resZ)
        {
            if (voxelGrid == null) return;

            // Create Texture3D with matching format
            var tex3D = new Texture3D(resX, resY, resZ, TextureFormat.RGBAHalf, false);
            tex3D.filterMode = FilterMode.Bilinear;
            tex3D.wrapMode = TextureWrapMode.Clamp;

            // Read back slice by slice
            var sliceRT = RenderTexture.GetTemporary(resX, resY, 0, RenderTextureFormat.ARGBHalf);
            var prevActive = RenderTexture.active;
            var readTex = new Texture2D(resX, resY, TextureFormat.RGBAHalf, false);

            Color[] allPixels = new Color[resX * resY * resZ];

            for (int z = 0; z < resZ; z++)
            {
                // Blit specific depth slice to 2D RT
                // _VolumeTex_TexelSize.z gives us the depth, but we use a simple material-less blit
                // with sourceDepthSlice parameter
                Graphics.CopyTexture(voxelGrid, z, 0, sliceRT, 0, 0);
                RenderTexture.active = sliceRT;
                readTex.ReadPixels(new Rect(0, 0, resX, resY), 0, 0);
                readTex.Apply();

                var slicePixels = readTex.GetPixels();
                System.Array.Copy(slicePixels, 0, allPixels, z * resX * resY, resX * resY);
            }

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(sliceRT);
            Object.DestroyImmediate(readTex);

            tex3D.SetPixels(allPixels);
            tex3D.Apply();

            // Save as asset
            string sceneName = gameObject.scene.name;
            if (string.IsNullOrEmpty(sceneName)) sceneName = "Untitled";
            string assetPath = $"Assets/Toaster/BakedGrids/{sceneName}_{gameObject.name}_VoxelGrid.asset";

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(assetPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            // Replace existing or create new
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(tex3D, existing);
                Object.DestroyImmediate(tex3D);
                serializedGrid = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(tex3D, assetPath);
                serializedGrid = tex3D;
            }

            AssetDatabase.SaveAssets();
            Appliance.Log($"Voxel grid serialized to: {assetPath}");
        }
#endif

        void ApplyBrowningPreset()
        {
            voxelSize = browningLevel switch
            {
                Appliance.BrowningLevel.Raw => 1.0f,
                Appliance.BrowningLevel.Light => 0.5f,
                Appliance.BrowningLevel.Burnt => 0.25f,
                _ => voxelSize
            };
            Appliance.Log($"Browning level: {browningLevel} → voxelSize = {voxelSize}");
        }

        /// <summary>
        /// Finds all Toaster visualizer materials and point cloud renderers in the scene
        /// and wires them to this baker's voxel grid.
        /// </summary>
        /// <summary>
        /// Returns the best available texture: runtime RT if it exists, otherwise serialized Texture3D.
        /// </summary>
        public Texture GetActiveGrid()
        {
            if (voxelGrid != null) return voxelGrid;
            if (serializedGrid != null) return serializedGrid;
            return null;
        }

        [ContextMenu("Wire Visualizers")]
        public void WireVisualizers()
        {
            var grid = GetActiveGrid();
            if (grid == null)
            {
                Appliance.LogWarning("No voxel grid to wire — bake first.");
                return;
            }

            int wiredCount = 0;

            // Wire all MeshRenderers using Toaster shaders
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            foreach (var rend in renderers)
            {
                var mat = rend.sharedMaterial;
                if (mat == null || mat.shader == null) continue;

                if (mat.shader.name.StartsWith("Toaster/"))
                {
                    if (mat.HasTexture("_VolumeTex"))
                    {
                        mat.SetTexture("_VolumeTex", grid);
                        wiredCount++;
                    }
                }
            }

            // Wire all VoxelPointCloudRenderers
            var pointClouds = FindObjectsByType<VoxelPointCloudRenderer>(FindObjectsSortMode.None);
            foreach (var pc in pointClouds)
            {
                pc.ConfigureFromBaker(this);
                wiredCount++;
            }

            Appliance.Log($"Auto-wired {wiredCount} visualizer(s) to {(voxelGrid != null ? "RenderTexture" : "serialized Texture3D")}.");
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, boundsSize);
        }
    }
}
