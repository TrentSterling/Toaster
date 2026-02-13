using UnityEngine;
using UnityEngine.Rendering;

namespace Toaster
{
    [ExecuteAlways]
    public class VoxelBaker : MonoBehaviour
    {
        [Header("Settings")]
        public float voxelSize = 0.25f;
        public Vector3 boundsSize = new Vector3(12, 8, 12);
        public ComputeShader voxelizerCompute;

        [Header("Debug")]
        public bool drawGizmos = true;

        // The Result
        public RenderTexture voxelGrid;

        // Buffers
        private ComputeBuffer vertBuffer;
        private ComputeBuffer normalBuffer;
        private ComputeBuffer uvBuffer;
        private ComputeBuffer indexBuffer;

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

            // 3. Setup Temp Meta Texture
            RenderTexture metaTempRT = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);

            // 4. Find all renderers
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Toaster_MetaBake";

            int kernel = voxelizerCompute.FindKernel("VoxelizeMesh");
            voxelizerCompute.SetTexture(kernel, "VoxelGrid", voxelGrid);
            voxelizerCompute.SetVector("WorldBoundsMin", worldMin);
            voxelizerCompute.SetFloat("VoxelSize", voxelSize);
            voxelizerCompute.SetInts("GridResolution", resX, resY, resZ);

            int objectCount = 0;
            int triangleCount = 0;

            foreach (var rend in renderers)
            {
                MeshFilter mf = rend.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

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
                    // Fill with material base color as fallback — Unity primitives lack UV1
                    // (lightmap UVs) so the Meta pass collapses vertices to (0,0).
                    // For solid-color materials this gives the exact right result.
                    Color baseColor = mat.HasColor("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;

                    cmd.Clear();
                    cmd.SetRenderTarget(metaTempRT);
                    cmd.ClearRenderTarget(true, true, baseColor);

                    // URP Meta Pass CBUFFER setup — required for UV-space rasterization
                    cmd.SetGlobalVector("unity_MetaVertexControl", new Vector4(1, 0, 0, 0));
                    cmd.SetGlobalVector("unity_MetaFragmentControl", new Vector4(1, 0, 0, 0));
                    cmd.SetGlobalVector("unity_LightmapST", new Vector4(1, 1, 0, 0));

                    cmd.DrawRenderer(rend, mat, i, metaPass);
                    Graphics.ExecuteCommandBuffer(cmd);

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

            Appliance.Log($"Bake complete! {objectCount} objects, {triangleCount} triangles.");

            // Diagnostic: readback a slice to verify data was written
            VerifyGrid(resX, resY);
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
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, boundsSize);
        }
    }
}
