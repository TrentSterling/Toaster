using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Toaster
{
    public class ToasterFroxelPass : ScriptableRenderPass, System.IDisposable
    {
        // GPU data struct — must match ToasterFroxel.compute VolumeGPUData
        struct VolumeGPUData
        {
            public Vector4 boundsMin;  // xyz = world min, w = unused
            public Vector4 boundsMax;  // xyz = world max, w = unused
            public Vector4 settings;   // x = density, y = intensity, z = gridIndex, w = edgeFalloff
        }

        const int MAX_VOLUME_GRIDS = 8;
        const int VOLUME_GPU_STRIDE = 48; // 3 x float4 = 48 bytes

        // Cached references (set per-frame via Setup)
        ToasterFroxelFeature.Settings m_Settings;
        ComputeShader m_Compute;
        Material m_ApplyMaterial;
        Texture2D m_BlueNoise;

        // Kernel indices
        int m_ClearKernel;
        int m_InjectKernel;
        int m_IntegrateKernel;

        // Persistent render targets
        RTHandle m_FroxelScattering;
        RTHandle m_FroxelIntegrated;
        RTHandle m_HistoryBuffer;

        // Volume data buffer
        GraphicsBuffer m_VolumeDataBuffer;
        List<VolumeGPUData> m_VolumeDataList = new List<VolumeGPUData>(8);
        List<Texture> m_VolumeGrids = new List<Texture>(8);

        // Temporal state
        int m_FrameIndex;
        Matrix4x4 m_PrevViewProj = Matrix4x4.identity;
        Vector3Int m_CurrentResolution;

        // Keyword for temporal
        LocalKeyword m_TemporalKeyword;
        bool m_TemporalKeywordInitialized;

        public void Setup(ToasterFroxelFeature.Settings settings, ComputeShader compute, Material applyMaterial, Texture2D blueNoise)
        {
            m_Settings = settings;
            m_Compute = compute;
            m_ApplyMaterial = applyMaterial;
            m_BlueNoise = blueNoise;

            // Cache kernel indices
            m_ClearKernel = compute.FindKernel("ClearFroxels");
            m_InjectKernel = compute.FindKernel("InjectMedia");
            m_IntegrateKernel = compute.FindKernel("IntegrateFroxels");

            EnsureRenderTargets(settings.froxelResolution);
        }

        void EnsureRenderTargets(Vector3Int resolution)
        {
            if (m_CurrentResolution == resolution && m_FroxelScattering != null)
                return;

            m_CurrentResolution = resolution;

            // Release old targets
            ReleaseRenderTargets();

            var desc = new RenderTextureDescriptor(resolution.x, resolution.y, RenderTextureFormat.ARGBHalf, 0);
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = resolution.z;
            desc.enableRandomWrite = true;
            desc.msaaSamples = 1;

            m_FroxelScattering = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "FroxelScattering");
            m_FroxelIntegrated = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "FroxelIntegrated");
            m_HistoryBuffer = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "FroxelHistory");
        }

        void ReleaseRenderTargets()
        {
            m_FroxelScattering?.Release();
            m_FroxelIntegrated?.Release();
            m_HistoryBuffer?.Release();
            m_FroxelScattering = null;
            m_FroxelIntegrated = null;
            m_HistoryBuffer = null;
        }

        void CollectVolumeData()
        {
            m_VolumeDataList.Clear();
            m_VolumeGrids.Clear();

            if (ToasterVolume.ActiveVolumes == null)
                return;

            int gridIndex = 0;
            foreach (var volume in ToasterVolume.ActiveVolumes)
            {
                if (gridIndex >= MAX_VOLUME_GRIDS)
                    break;

                if (volume == null || volume.baker == null)
                    continue;

                var grid = volume.LightingGrid;
                if (grid == null)
                    continue;

                // Compute world bounds from baker
                Vector3 center = volume.baker.transform.position;
                Vector3 size = volume.baker.boundsSize;
                Vector3 worldMin = center - size * 0.5f;
                Vector3 worldMax = center + size * 0.5f;

                var data = new VolumeGPUData
                {
                    boundsMin = new Vector4(worldMin.x, worldMin.y, worldMin.z, 0),
                    boundsMax = new Vector4(worldMax.x, worldMax.y, worldMax.z, 0),
                    settings = new Vector4(
                        volume.densityMultiplier,
                        volume.intensityMultiplier,
                        gridIndex,
                        volume.edgeFalloff
                    )
                };

                m_VolumeDataList.Add(data);
                m_VolumeGrids.Add(grid);
                gridIndex++;
            }
        }

        void EnsureVolumeBuffer(int count)
        {
            int needed = Mathf.Max(1, count);
            if (m_VolumeDataBuffer != null && m_VolumeDataBuffer.count >= needed)
                return;

            m_VolumeDataBuffer?.Release();
            m_VolumeDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, needed, VOLUME_GPU_STRIDE);
        }

        // ============================================================
        // RenderGraph
        // ============================================================

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            if (m_FroxelScattering == null || m_FroxelIntegrated == null)
                return;

            // Collect active volumes
            CollectVolumeData();
            if (m_VolumeDataList.Count == 0)
                return;

            // Upload volume buffer
            EnsureVolumeBuffer(m_VolumeDataList.Count);
            m_VolumeDataBuffer.SetData(m_VolumeDataList);

            var res = m_Settings.froxelResolution;

            // Camera matrices
            Matrix4x4 view = cameraData.GetViewMatrix();
            Matrix4x4 proj = cameraData.GetProjectionMatrix();
            Matrix4x4 viewProj = proj * view;
            Matrix4x4 invViewProj = viewProj.inverse;

            // Import persistent textures into render graph
            TextureHandle scatteringHandle = renderGraph.ImportTexture(m_FroxelScattering);
            TextureHandle integratedHandle = renderGraph.ImportTexture(m_FroxelIntegrated);
            TextureHandle historyHandle = renderGraph.ImportTexture(m_HistoryBuffer);

            // --------------------------------------------------------
            // Compute pass (unsafe — manual CommandBuffer for 3 dispatches)
            // --------------------------------------------------------
            using (var builder = renderGraph.AddUnsafePass<ComputePassData>("Toaster Froxel Compute", out var passData))
            {
                passData.compute = m_Compute;
                passData.settings = m_Settings;
                passData.resolution = res;
                passData.clearKernel = m_ClearKernel;
                passData.injectKernel = m_InjectKernel;
                passData.integrateKernel = m_IntegrateKernel;
                passData.volumeBuffer = m_VolumeDataBuffer;
                passData.volumeCount = m_VolumeDataList.Count;
                passData.volumeGrids = m_VolumeGrids.ToArray();
                passData.invViewProj = invViewProj;
                passData.prevViewProj = m_PrevViewProj;
                passData.frameIndex = m_FrameIndex;
                passData.blueNoise = m_BlueNoise;

                passData.scatteringHandle = scatteringHandle;
                passData.integratedHandle = integratedHandle;
                passData.historyHandle = historyHandle;

                builder.UseTexture(scatteringHandle, AccessFlags.ReadWrite);
                builder.UseTexture(integratedHandle, AccessFlags.ReadWrite);
                builder.UseTexture(historyHandle, AccessFlags.Read);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ComputePassData data, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    ExecuteCompute(cmd, data);
                });
            }

            // --------------------------------------------------------
            // Apply pass (fullscreen composite)
            // --------------------------------------------------------
            using (var builder = renderGraph.AddRasterRenderPass<ApplyPassData>("Toaster Froxel Apply", out var passData))
            {
                passData.applyMaterial = m_ApplyMaterial;
                passData.settings = m_Settings;
                passData.resolution = res;
                passData.integratedHandle = integratedHandle;

                builder.UseTexture(integratedHandle, AccessFlags.Read);

                // Write to active color target
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                // Read depth for depth-aware sampling
                builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ApplyPassData data, RasterGraphContext ctx) =>
                {
                    ExecuteApply(ctx.cmd, data);
                });
            }

            // Swap history for temporal reprojection
            if (m_Settings.enableTemporal)
            {
                // Copy current scattering to history for next frame
                // (Swap handles — next frame reads what we wrote this frame)
                var temp = m_HistoryBuffer;
                m_HistoryBuffer = m_FroxelScattering;
                m_FroxelScattering = temp;
            }

            // Update temporal state
            m_PrevViewProj = viewProj;
            m_FrameIndex++;
        }

        // ============================================================
        // Compute execution
        // ============================================================

        class ComputePassData
        {
            public ComputeShader compute;
            public ToasterFroxelFeature.Settings settings;
            public Vector3Int resolution;
            public int clearKernel;
            public int injectKernel;
            public int integrateKernel;
            public GraphicsBuffer volumeBuffer;
            public int volumeCount;
            public Texture[] volumeGrids;
            public Matrix4x4 invViewProj;
            public Matrix4x4 prevViewProj;
            public int frameIndex;
            public Texture2D blueNoise;

            public TextureHandle scatteringHandle;
            public TextureHandle integratedHandle;
            public TextureHandle historyHandle;
        }

        static readonly string[] s_VolumeGridNames = new string[]
        {
            "VolumeGrid0", "VolumeGrid1", "VolumeGrid2", "VolumeGrid3",
            "VolumeGrid4", "VolumeGrid5", "VolumeGrid6", "VolumeGrid7"
        };

        static void ExecuteCompute(CommandBuffer cmd, ComputePassData data)
        {
            var cs = data.compute;
            var res = data.resolution;

            // -- Shared uniforms --
            cmd.SetComputeIntParam(cs, "_FroxelResX", res.x);
            cmd.SetComputeIntParam(cs, "_FroxelResY", res.y);
            cmd.SetComputeIntParam(cs, "_FroxelResZ", res.z);
            cmd.SetComputeFloatParam(cs, "_FroxelNear", data.settings.nearPlane);
            cmd.SetComputeFloatParam(cs, "_FroxelFar", data.settings.maxDistance);
            cmd.SetComputeFloatParam(cs, "_DepthUniformity", data.settings.depthUniformity);
            cmd.SetComputeMatrixParam(cs, "_InvViewProj", data.invViewProj);
            cmd.SetComputeMatrixParam(cs, "_PrevViewProj", data.prevViewProj);
            cmd.SetComputeIntParam(cs, "_FrameIndex", data.frameIndex);
            cmd.SetComputeFloatParam(cs, "_FogDensity", data.settings.fogDensity);
            cmd.SetComputeFloatParam(cs, "_FogIntensity", data.settings.fogIntensity);

            if (data.blueNoise != null)
                cmd.SetComputeTextureParam(cs, data.injectKernel, "_BlueNoise", data.blueNoise);

            // -- 1. Clear froxels --
            cmd.SetComputeTextureParam(cs, data.clearKernel, "FroxelScattering", data.scatteringHandle);
            cmd.DispatchCompute(cs, data.clearKernel,
                Mathf.CeilToInt(res.x / 4f),
                Mathf.CeilToInt(res.y / 4f),
                Mathf.CeilToInt(res.z / 4f));

            // -- 2. Inject media --
            cmd.SetComputeTextureParam(cs, data.injectKernel, "FroxelScattering", data.scatteringHandle);

            // Temporal reprojection
            if (data.settings.enableTemporal)
            {
                cmd.SetComputeTextureParam(cs, data.injectKernel, "FroxelHistory", data.historyHandle);
                cmd.SetComputeFloatParam(cs, "_TemporalBlendAlpha", data.settings.temporalBlendAlpha);
            }

            // Volume data
            cmd.SetComputeBufferParam(cs, data.injectKernel, "_VolumeDataBuffer", data.volumeBuffer);
            cmd.SetComputeIntParam(cs, "_VolumeCount", data.volumeCount);

            // Bind volume grids (up to 8 named slots)
            for (int i = 0; i < data.volumeGrids.Length && i < MAX_VOLUME_GRIDS; i++)
            {
                cmd.SetComputeTextureParam(cs, data.injectKernel, s_VolumeGridNames[i], data.volumeGrids[i]);
            }

            cmd.DispatchCompute(cs, data.injectKernel,
                Mathf.CeilToInt(res.x / 4f),
                Mathf.CeilToInt(res.y / 4f),
                Mathf.CeilToInt(res.z / 4f));

            // -- 3. Integrate froxels --
            cmd.SetComputeTextureParam(cs, data.integrateKernel, "FroxelScattering", data.scatteringHandle);
            cmd.SetComputeTextureParam(cs, data.integrateKernel, "FroxelIntegrated", data.integratedHandle);
            cmd.DispatchCompute(cs, data.integrateKernel,
                Mathf.CeilToInt(res.x / 8f),
                Mathf.CeilToInt(res.y / 8f),
                1);
        }

        // ============================================================
        // Apply execution
        // ============================================================

        class ApplyPassData
        {
            public Material applyMaterial;
            public ToasterFroxelFeature.Settings settings;
            public Vector3Int resolution;
            public TextureHandle integratedHandle;
        }

        static void ExecuteApply(RasterCommandBuffer cmd, ApplyPassData data)
        {
            var mat = data.applyMaterial;

            mat.SetTexture("_FroxelTex", data.integratedHandle);
            mat.SetFloat("_FroxelNear", data.settings.nearPlane);
            mat.SetFloat("_FroxelFar", data.settings.maxDistance);
            mat.SetFloat("_DepthUniformity", data.settings.depthUniformity);
            mat.SetInt("_FroxelResZ", data.settings.froxelResolution.z);

            cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
        }

        // ============================================================
        // Cleanup
        // ============================================================

        public void Dispose()
        {
            ReleaseRenderTargets();
            m_VolumeDataBuffer?.Release();
            m_VolumeDataBuffer = null;
        }
    }
}
