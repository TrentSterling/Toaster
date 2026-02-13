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

        // GPU data struct — must match ToasterFroxel.compute LightGPUData
        struct LightGPUData
        {
            public Vector4 positionAndRange;    // xyz = position, w = range
            public Vector4 colorAndIntensity;   // rgb = color, w = intensity
            public Vector4 directionAndAngle;   // xyz = forward dir, w = cos(spotAngle * 0.5)
            public Vector4 typeAndFlags;        // x = type (0=point, 1=spot, 2=directional), y = cos(innerAngle)
        }

        const int MAX_VOLUME_GRIDS = 8;
        const int VOLUME_GPU_STRIDE = 48; // 3 x float4 = 48 bytes
        const int LIGHT_GPU_STRIDE = 64;  // 4 x float4 = 64 bytes

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

        // Light data buffer
        GraphicsBuffer m_LightDataBuffer;
        List<LightGPUData> m_LightDataList = new List<LightGPUData>(16);

        // Temporal state
        int m_FrameIndex;
        Matrix4x4 m_PrevViewProj = Matrix4x4.identity;
        Vector3Int m_CurrentResolution;

        // Shader property IDs (cached)
        static readonly int s_FroxelResX = Shader.PropertyToID("_FroxelResX");
        static readonly int s_FroxelResY = Shader.PropertyToID("_FroxelResY");
        static readonly int s_FroxelResZ = Shader.PropertyToID("_FroxelResZ");
        static readonly int s_FroxelNear = Shader.PropertyToID("_FroxelNear");
        static readonly int s_FroxelFar = Shader.PropertyToID("_FroxelFar");
        static readonly int s_DepthUniformity = Shader.PropertyToID("_DepthUniformity");
        static readonly int s_InvViewProj = Shader.PropertyToID("_InvViewProj");
        static readonly int s_PrevViewProj = Shader.PropertyToID("_PrevViewProj");
        static readonly int s_FrameIndex = Shader.PropertyToID("_FrameIndex");
        static readonly int s_FogDensity = Shader.PropertyToID("_FogDensity");
        static readonly int s_FogIntensity = Shader.PropertyToID("_FogIntensity");
        static readonly int s_AmbientFogColor = Shader.PropertyToID("_AmbientFogColor");
        static readonly int s_TemporalBlendAlpha = Shader.PropertyToID("_TemporalBlendAlpha");
        static readonly int s_VolumeCount = Shader.PropertyToID("_VolumeCount");
        static readonly int s_LightCount = Shader.PropertyToID("_LightCount");
        static readonly int s_ScatterAnisotropy = Shader.PropertyToID("_ScatterAnisotropy");
        static readonly int s_CameraPos = Shader.PropertyToID("_CameraPos");
        static readonly int s_CamForward = Shader.PropertyToID("_CamForward");
        static readonly int s_FroxelTex = Shader.PropertyToID("_FroxelTex");
        static readonly int s_FroxelScatterTex = Shader.PropertyToID("_FroxelScatterTex");
        static readonly int s_FroxelDebugMode = Shader.PropertyToID("_FroxelDebugMode");
        static readonly int s_LightDensityBoost = Shader.PropertyToID("_LightDensityBoost");
        static readonly int s_EnableHeightFog = Shader.PropertyToID("_EnableHeightFog");
        static readonly int s_HeightFogBase = Shader.PropertyToID("_HeightFogBase");
        static readonly int s_HeightFogTop = Shader.PropertyToID("_HeightFogTop");

        static readonly string[] s_VolumeGridNames = new string[]
        {
            "VolumeGrid0", "VolumeGrid1", "VolumeGrid2", "VolumeGrid3",
            "VolumeGrid4", "VolumeGrid5", "VolumeGrid6", "VolumeGrid7"
        };

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

        int m_LastLoggedVolumeCount = -1;

        void CollectVolumeData()
        {
            m_VolumeDataList.Clear();
            m_VolumeGrids.Clear();

            if (ToasterVolume.ActiveVolumes == null)
                return;

            int gridIndex = 0;
            int skippedNoBaker = 0, skippedNoGrid = 0;
            foreach (var volume in ToasterVolume.ActiveVolumes)
            {
                if (gridIndex >= MAX_VOLUME_GRIDS)
                    break;

                if (volume == null || volume.baker == null)
                {
                    skippedNoBaker++;
                    continue;
                }

                var grid = volume.LightingGrid;
                if (grid == null)
                {
                    skippedNoGrid++;
                    continue;
                }

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

            // Log when volume count changes
            if (m_VolumeDataList.Count != m_LastLoggedVolumeCount)
            {
                m_LastLoggedVolumeCount = m_VolumeDataList.Count;
                int total = ToasterVolume.ActiveVolumes.Count;
                if (m_VolumeDataList.Count == 0)
                {
                    Appliance.LogWarning($"Froxel: {total} volume(s) found but 0 have valid grids. " +
                        $"({skippedNoBaker} missing baker, {skippedNoGrid} missing grid texture). " +
                        "Bake voxels first, or check serializedGrid on baker.");
                }
                else
                {
                    Appliance.Log($"Froxel: injecting {m_VolumeDataList.Count} volume(s) into froxel grid.");
                    for (int i = 0; i < m_VolumeDataList.Count; i++)
                    {
                        var d = m_VolumeDataList[i];
                        var g = m_VolumeGrids[i];
                        Appliance.Log($"  Volume[{i}]: grid={g.name} ({g.GetType().Name} {g.width}x{g.height}), " +
                            $"bounds=({d.boundsMin.x:F1},{d.boundsMin.y:F1},{d.boundsMin.z:F1})-" +
                            $"({d.boundsMax.x:F1},{d.boundsMax.y:F1},{d.boundsMax.z:F1}), " +
                            $"density={d.settings.x:F2}, intensity={d.settings.y:F2}, gridIdx={d.settings.z:F0}");
                    }
                }

                if (skippedNoBaker > 0 || skippedNoGrid > 0)
                    Appliance.LogWarning($"Froxel: skipped {skippedNoBaker} (no baker) + {skippedNoGrid} (no grid).");
            }
        }

        void CollectLightData(int maxLights)
        {
            m_LightDataList.Clear();

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var light in lights)
            {
                if (m_LightDataList.Count >= maxLights)
                    break;

                if (!light.isActiveAndEnabled || light.intensity <= 0f)
                    continue;

                // Skip area lights (not supported in real-time fog)
                if (light.type == LightType.Rectangle || light.type == LightType.Disc)
                    continue;

                var data = new LightGPUData();
                Color linearColor = light.color.linear;

                data.colorAndIntensity = new Vector4(linearColor.r, linearColor.g, linearColor.b, light.intensity);

                switch (light.type)
                {
                    case LightType.Point:
                        data.positionAndRange = new Vector4(
                            light.transform.position.x, light.transform.position.y,
                            light.transform.position.z, light.range);
                        data.directionAndAngle = Vector4.zero;
                        data.typeAndFlags = new Vector4(0, 0, 0, 0);
                        break;

                    case LightType.Spot:
                        data.positionAndRange = new Vector4(
                            light.transform.position.x, light.transform.position.y,
                            light.transform.position.z, light.range);
                        Vector3 fwd = light.transform.forward;
                        float cosOuter = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                        float cosInner = Mathf.Cos(light.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                        data.directionAndAngle = new Vector4(fwd.x, fwd.y, fwd.z, cosOuter);
                        data.typeAndFlags = new Vector4(1, cosInner, 0, 0);
                        break;

                    case LightType.Directional:
                        data.positionAndRange = Vector4.zero;
                        Vector3 dir = light.transform.forward;
                        data.directionAndAngle = new Vector4(dir.x, dir.y, dir.z, -1);
                        data.typeAndFlags = new Vector4(2, 0, 0, 0);
                        break;

                    default:
                        continue;
                }

                m_LightDataList.Add(data);
            }
        }

        void EnsureLightBuffer(int count)
        {
            int needed = Mathf.Max(1, count);
            if (m_LightDataBuffer != null && m_LightDataBuffer.count >= needed)
                return;

            m_LightDataBuffer?.Release();
            m_LightDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, needed, LIGHT_GPU_STRIDE);
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
        // Shared compute dispatch logic (used by both paths)
        // ============================================================

        void DispatchCompute(CommandBuffer cmd, RenderTexture scatteringRT, RenderTexture integratedRT,
            RenderTexture historyRT, Matrix4x4 invViewProj, Vector3 cameraPos, Vector3 camForward)
        {
            var cs = m_Compute;
            var res = m_Settings.froxelResolution;

            // Shared uniforms
            cmd.SetComputeIntParam(cs, s_FroxelResX, res.x);
            cmd.SetComputeIntParam(cs, s_FroxelResY, res.y);
            cmd.SetComputeIntParam(cs, s_FroxelResZ, res.z);
            cmd.SetComputeFloatParam(cs, s_FroxelNear, m_Settings.nearPlane);
            cmd.SetComputeFloatParam(cs, s_FroxelFar, m_Settings.maxDistance);
            cmd.SetComputeFloatParam(cs, s_DepthUniformity, m_Settings.depthUniformity);
            cmd.SetComputeMatrixParam(cs, s_InvViewProj, invViewProj);
            cmd.SetComputeMatrixParam(cs, s_PrevViewProj, m_PrevViewProj);
            cmd.SetComputeIntParam(cs, s_FrameIndex, m_FrameIndex);
            cmd.SetComputeFloatParam(cs, s_FogDensity, m_Settings.fogDensity);
            cmd.SetComputeFloatParam(cs, s_FogIntensity, m_Settings.fogIntensity);
            cmd.SetComputeVectorParam(cs, s_AmbientFogColor, m_Settings.ambientColor);
            cmd.SetComputeVectorParam(cs, s_CameraPos, cameraPos);
            cmd.SetComputeVectorParam(cs, s_CamForward, camForward);
            cmd.SetComputeFloatParam(cs, s_ScatterAnisotropy, m_Settings.scatterAnisotropy);
            cmd.SetComputeFloatParam(cs, s_LightDensityBoost, m_Settings.lightDensityBoost);
            cmd.SetComputeIntParam(cs, s_EnableHeightFog, m_Settings.enableHeightFog ? 1 : 0);
            cmd.SetComputeFloatParam(cs, s_HeightFogBase, m_Settings.heightFogBase);
            cmd.SetComputeFloatParam(cs, s_HeightFogTop, m_Settings.heightFogTop);

            if (m_BlueNoise != null)
                cmd.SetComputeTextureParam(cs, m_InjectKernel, "_BlueNoise", m_BlueNoise);

            // Toggle temporal reprojection keyword on compute shader
            if (m_Settings.enableTemporal)
                cs.EnableKeyword("TEMPORAL_REPROJECTION");
            else
                cs.DisableKeyword("TEMPORAL_REPROJECTION");

            // 1. Clear
            cmd.SetComputeTextureParam(cs, m_ClearKernel, "FroxelScattering", scatteringRT);
            cmd.DispatchCompute(cs, m_ClearKernel,
                Mathf.CeilToInt(res.x / 4f),
                Mathf.CeilToInt(res.y / 4f),
                Mathf.CeilToInt(res.z / 4f));

            // 2. Inject
            cmd.SetComputeTextureParam(cs, m_InjectKernel, "FroxelScattering", scatteringRT);

            if (m_Settings.enableTemporal && historyRT != null)
            {
                cmd.SetComputeTextureParam(cs, m_InjectKernel, "FroxelHistory", historyRT);
                cmd.SetComputeFloatParam(cs, s_TemporalBlendAlpha, m_Settings.temporalBlendAlpha);
            }

            cmd.SetComputeBufferParam(cs, m_InjectKernel, "_VolumeDataBuffer", m_VolumeDataBuffer);
            cmd.SetComputeIntParam(cs, s_VolumeCount, m_VolumeDataList.Count);

            // Bind light data
            cmd.SetComputeBufferParam(cs, m_InjectKernel, "_LightDataBuffer", m_LightDataBuffer);
            cmd.SetComputeIntParam(cs, s_LightCount, m_LightDataList.Count);

            // Bind all 8 grid slots — unused slots get first grid to silence DX11
            Texture fallbackGrid = m_VolumeGrids.Count > 0 ? m_VolumeGrids[0] : scatteringRT;
            for (int i = 0; i < MAX_VOLUME_GRIDS; i++)
            {
                var tex = i < m_VolumeGrids.Count ? m_VolumeGrids[i] : fallbackGrid;
                cmd.SetComputeTextureParam(cs, m_InjectKernel, s_VolumeGridNames[i], tex);
            }

            cmd.DispatchCompute(cs, m_InjectKernel,
                Mathf.CeilToInt(res.x / 4f),
                Mathf.CeilToInt(res.y / 4f),
                Mathf.CeilToInt(res.z / 4f));

            // 3. Integrate
            cmd.SetComputeTextureParam(cs, m_IntegrateKernel, "FroxelScattering", scatteringRT);
            cmd.SetComputeTextureParam(cs, m_IntegrateKernel, "FroxelIntegrated", integratedRT);
            cmd.DispatchCompute(cs, m_IntegrateKernel,
                Mathf.CeilToInt(res.x / 8f),
                Mathf.CeilToInt(res.y / 8f),
                1);
        }

        void SetApplyMaterialProperties(RenderTexture integratedRT, RenderTexture scatteringRT)
        {
            m_ApplyMaterial.SetTexture(s_FroxelTex, integratedRT);
            m_ApplyMaterial.SetFloat(s_FroxelNear, m_Settings.nearPlane);
            m_ApplyMaterial.SetFloat(s_FroxelFar, m_Settings.maxDistance);
            m_ApplyMaterial.SetFloat(s_DepthUniformity, m_Settings.depthUniformity);
            m_ApplyMaterial.SetInt(s_FroxelResZ, m_Settings.froxelResolution.z);
            m_ApplyMaterial.SetInt(s_FroxelDebugMode, (int)m_Settings.debugMode);
            if (scatteringRT != null)
                m_ApplyMaterial.SetTexture(s_FroxelScatterTex, scatteringRT);
        }

        // ============================================================
        // Legacy Execute path (compatibility / Scene view fallback)
        // ============================================================

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_FroxelScattering == null || m_FroxelIntegrated == null)
                return;

            CollectVolumeData();
            if (m_VolumeDataList.Count == 0)
                return;

            EnsureVolumeBuffer(m_VolumeDataList.Count);
            m_VolumeDataBuffer.SetData(m_VolumeDataList);

            // Collect scene lights
            CollectLightData(m_Settings.maxLights);
            EnsureLightBuffer(m_LightDataList.Count);
            if (m_LightDataList.Count > 0)
                m_LightDataBuffer.SetData(m_LightDataList);

            // Camera matrices
            var cam = renderingData.cameraData.camera;
            Matrix4x4 view = cam.worldToCameraMatrix;
            Matrix4x4 proj = cam.projectionMatrix;
            // GPU projection: false = Z-range correction only, no Y-flip (we handle Y in shader)
            proj = GL.GetGPUProjectionMatrix(proj, false);
            Matrix4x4 viewProj = proj * view;
            Matrix4x4 invViewProj = viewProj.inverse;

            var cmd = CommandBufferPool.Get("Toaster Froxel");

            // Get underlying RenderTextures from RTHandles
            RenderTexture scatteringRT = m_FroxelScattering.rt;
            RenderTexture integratedRT = m_FroxelIntegrated.rt;
            RenderTexture historyRT = m_HistoryBuffer?.rt;

            if (scatteringRT == null || integratedRT == null)
            {
                CommandBufferPool.Release(cmd);
                return;
            }

            // Dispatch compute
            DispatchCompute(cmd, scatteringRT, integratedRT, historyRT, invViewProj, cam.transform.position, cam.transform.forward);

            // Apply fullscreen
            SetApplyMaterialProperties(integratedRT, scatteringRT);
            cmd.DrawProcedural(Matrix4x4.identity, m_ApplyMaterial, 0, MeshTopology.Triangles, 3);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // Temporal swap
            if (m_Settings.enableTemporal)
            {
                var temp = m_HistoryBuffer;
                m_HistoryBuffer = m_FroxelScattering;
                m_FroxelScattering = temp;
            }

            m_PrevViewProj = viewProj;
            m_FrameIndex++;
        }

        // ============================================================
        // RenderGraph path (URP 17 / Unity 6 default)
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
            public GraphicsBuffer lightBuffer;
            public int lightCount;
            public Matrix4x4 invViewProj;
            public Matrix4x4 prevViewProj;
            public Vector3 cameraPos;
            public Vector3 camForward;
            public int frameIndex;
            public Texture2D blueNoise;

            // Use RTHandles directly for texture access
            public RTHandle scatteringRT;
            public RTHandle integratedRT;
            public RTHandle historyRT;
            public bool enableTemporal;

            // TextureHandles for RenderGraph resource tracking
            public TextureHandle scatteringHandle;
            public TextureHandle integratedHandle;
            public TextureHandle historyHandle;
        }

        class ApplyPassData
        {
            public Material applyMaterial;
            public ToasterFroxelFeature.Settings settings;
            public RTHandle integratedRT;
            public RTHandle scatteringRT;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            if (m_FroxelScattering?.rt == null || m_FroxelIntegrated?.rt == null)
                return;

            CollectVolumeData();
            if (m_VolumeDataList.Count == 0)
                return;

            EnsureVolumeBuffer(m_VolumeDataList.Count);
            m_VolumeDataBuffer.SetData(m_VolumeDataList);

            // Collect scene lights
            CollectLightData(m_Settings.maxLights);
            EnsureLightBuffer(m_LightDataList.Count);
            if (m_LightDataList.Count > 0)
                m_LightDataBuffer.SetData(m_LightDataList);

            var res = m_Settings.froxelResolution;

            // Camera matrices — Z-range correction only, no Y-flip (shader handles UV→NDC Y)
            Matrix4x4 view = cameraData.GetViewMatrix();
            Matrix4x4 proj = cameraData.GetProjectionMatrix();
            proj = GL.GetGPUProjectionMatrix(proj, false);
            Matrix4x4 viewProj = proj * view;
            Matrix4x4 invViewProj = viewProj.inverse;

            // Import persistent textures into render graph
            TextureHandle scatteringHandle = renderGraph.ImportTexture(m_FroxelScattering);
            TextureHandle integratedHandle = renderGraph.ImportTexture(m_FroxelIntegrated);
            TextureHandle historyHandle = renderGraph.ImportTexture(m_HistoryBuffer);

            // --------------------------------------------------------
            // Compute pass (unsafe — manual CommandBuffer)
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
                passData.lightBuffer = m_LightDataBuffer;
                passData.lightCount = m_LightDataList.Count;
                passData.invViewProj = invViewProj;
                passData.prevViewProj = m_PrevViewProj;
                passData.cameraPos = cameraData.worldSpaceCameraPos;
                passData.camForward = cameraData.camera.transform.forward;
                passData.frameIndex = m_FrameIndex;
                passData.blueNoise = m_BlueNoise;
                passData.enableTemporal = m_Settings.enableTemporal;

                // Pass RTHandles directly for texture access in render func
                passData.scatteringRT = m_FroxelScattering;
                passData.integratedRT = m_FroxelIntegrated;
                passData.historyRT = m_HistoryBuffer;

                // Declare resource usage for RenderGraph
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
                    ExecuteComputeRG(cmd, data);
                });
            }

            // --------------------------------------------------------
            // Apply pass (raster — draws fullscreen triangle into camera color)
            // --------------------------------------------------------
            using (var builder = renderGraph.AddRasterRenderPass<ApplyPassData>("Toaster Froxel Apply", out var passData))
            {
                passData.applyMaterial = m_ApplyMaterial;
                passData.settings = m_Settings;
                passData.integratedRT = m_FroxelIntegrated;
                passData.scatteringRT = m_FroxelScattering;

                // Read the integrated froxel texture + scattering (for debug modes)
                builder.UseTexture(integratedHandle);
                builder.UseTexture(scatteringHandle);

                // Bind camera color as render target, depth for reading
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                if (resourceData.activeDepthTexture.IsValid())
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ApplyPassData data, RasterGraphContext ctx) =>
                {
                    var mat = data.applyMaterial;
                    RenderTexture intRT = data.integratedRT.rt;
                    if (intRT == null) return;

                    mat.SetTexture(s_FroxelTex, intRT);
                    mat.SetFloat(s_FroxelNear, data.settings.nearPlane);
                    mat.SetFloat(s_FroxelFar, data.settings.maxDistance);
                    mat.SetFloat(s_DepthUniformity, data.settings.depthUniformity);
                    mat.SetInt(s_FroxelResZ, data.settings.froxelResolution.z);
                    mat.SetInt(s_FroxelDebugMode, (int)data.settings.debugMode);
                    if (data.scatteringRT?.rt != null)
                        mat.SetTexture(s_FroxelScatterTex, data.scatteringRT.rt);

                    ctx.cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
                });
            }

            // Temporal swap
            if (m_Settings.enableTemporal)
            {
                var temp = m_HistoryBuffer;
                m_HistoryBuffer = m_FroxelScattering;
                m_FroxelScattering = temp;
            }

            m_PrevViewProj = viewProj;
            m_FrameIndex++;
        }

        static void ExecuteComputeRG(CommandBuffer cmd, ComputePassData data)
        {
            var cs = data.compute;
            var res = data.resolution;

            // Shared uniforms
            cmd.SetComputeIntParam(cs, s_FroxelResX, res.x);
            cmd.SetComputeIntParam(cs, s_FroxelResY, res.y);
            cmd.SetComputeIntParam(cs, s_FroxelResZ, res.z);
            cmd.SetComputeFloatParam(cs, s_FroxelNear, data.settings.nearPlane);
            cmd.SetComputeFloatParam(cs, s_FroxelFar, data.settings.maxDistance);
            cmd.SetComputeFloatParam(cs, s_DepthUniformity, data.settings.depthUniformity);
            cmd.SetComputeMatrixParam(cs, s_InvViewProj, data.invViewProj);
            cmd.SetComputeMatrixParam(cs, s_PrevViewProj, data.prevViewProj);
            cmd.SetComputeIntParam(cs, s_FrameIndex, data.frameIndex);
            cmd.SetComputeFloatParam(cs, s_FogDensity, data.settings.fogDensity);
            cmd.SetComputeFloatParam(cs, s_FogIntensity, data.settings.fogIntensity);
            cmd.SetComputeVectorParam(cs, s_AmbientFogColor, data.settings.ambientColor);
            cmd.SetComputeVectorParam(cs, s_CameraPos, data.cameraPos);
            cmd.SetComputeVectorParam(cs, s_CamForward, data.camForward);
            cmd.SetComputeFloatParam(cs, s_ScatterAnisotropy, data.settings.scatterAnisotropy);
            cmd.SetComputeFloatParam(cs, s_LightDensityBoost, data.settings.lightDensityBoost);
            cmd.SetComputeIntParam(cs, s_EnableHeightFog, data.settings.enableHeightFog ? 1 : 0);
            cmd.SetComputeFloatParam(cs, s_HeightFogBase, data.settings.heightFogBase);
            cmd.SetComputeFloatParam(cs, s_HeightFogTop, data.settings.heightFogTop);

            if (data.blueNoise != null)
                cmd.SetComputeTextureParam(cs, data.injectKernel, "_BlueNoise", data.blueNoise);

            // Toggle temporal reprojection keyword on compute shader
            if (data.enableTemporal)
                cs.EnableKeyword("TEMPORAL_REPROJECTION");
            else
                cs.DisableKeyword("TEMPORAL_REPROJECTION");

            // Use RTHandle.rt for texture binding (not TextureHandle)
            RenderTexture scatRT = data.scatteringRT.rt;
            RenderTexture intRT = data.integratedRT.rt;

            // 1. Clear
            cmd.SetComputeTextureParam(cs, data.clearKernel, "FroxelScattering", scatRT);
            cmd.DispatchCompute(cs, data.clearKernel,
                Mathf.CeilToInt(res.x / 4f),
                Mathf.CeilToInt(res.y / 4f),
                Mathf.CeilToInt(res.z / 4f));

            // 2. Inject
            cmd.SetComputeTextureParam(cs, data.injectKernel, "FroxelScattering", scatRT);

            if (data.enableTemporal && data.historyRT?.rt != null)
            {
                cmd.SetComputeTextureParam(cs, data.injectKernel, "FroxelHistory", data.historyRT.rt);
                cmd.SetComputeFloatParam(cs, s_TemporalBlendAlpha, data.settings.temporalBlendAlpha);
            }

            cmd.SetComputeBufferParam(cs, data.injectKernel, "_VolumeDataBuffer", data.volumeBuffer);
            cmd.SetComputeIntParam(cs, s_VolumeCount, data.volumeCount);

            // Bind light data
            cmd.SetComputeBufferParam(cs, data.injectKernel, "_LightDataBuffer", data.lightBuffer);
            cmd.SetComputeIntParam(cs, s_LightCount, data.lightCount);

            // Bind all 8 grid slots — unused slots get first grid to silence DX11
            Texture fallbackGrid = data.volumeGrids.Length > 0 ? data.volumeGrids[0] : scatRT;
            for (int i = 0; i < MAX_VOLUME_GRIDS; i++)
            {
                var tex = i < data.volumeGrids.Length ? data.volumeGrids[i] : fallbackGrid;
                cmd.SetComputeTextureParam(cs, data.injectKernel, s_VolumeGridNames[i], tex);
            }

            cmd.DispatchCompute(cs, data.injectKernel,
                Mathf.CeilToInt(res.x / 4f),
                Mathf.CeilToInt(res.y / 4f),
                Mathf.CeilToInt(res.z / 4f));

            // 3. Integrate
            cmd.SetComputeTextureParam(cs, data.integrateKernel, "FroxelScattering", scatRT);
            cmd.SetComputeTextureParam(cs, data.integrateKernel, "FroxelIntegrated", intRT);
            cmd.DispatchCompute(cs, data.integrateKernel,
                Mathf.CeilToInt(res.x / 8f),
                Mathf.CeilToInt(res.y / 8f),
                1);
        }

        // ============================================================
        // Cleanup
        // ============================================================

        public void Dispose()
        {
            ReleaseRenderTargets();
            m_VolumeDataBuffer?.Release();
            m_VolumeDataBuffer = null;
            m_LightDataBuffer?.Release();
            m_LightDataBuffer = null;
        }
    }
}
