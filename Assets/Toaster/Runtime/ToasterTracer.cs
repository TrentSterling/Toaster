using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Toaster
{
    [ExecuteAlways]
    public class ToasterTracer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The VoxelBaker whose albedo grid we'll trace.")]
        public VoxelBaker baker;

        [Tooltip("Path tracer compute shader. Assign ToasterTracer.compute.")]
        public ComputeShader tracerCompute;

        [Header("Quality")]
        [Tooltip("Number of rays per occupied voxel.")]
        [Range(4, 256)]
        public int raysPerVoxel = 64;

        [Tooltip("Maximum number of light bounces for color bleeding.")]
        [Range(1, 5)]
        public int maxBounces = 3;

        [Tooltip("Light falloff multiplier for inverse square attenuation.")]
        [Range(0.01f, 2f)]
        public float lightFalloff = 0.5f;

        [Header("Output")]
        [HideInInspector]
        public RenderTexture lightingGrid;

        // Light data buffers
        private ComputeBuffer lightPosBuffer;
        private ComputeBuffer lightColorBuffer;

        private void OnDestroy()
        {
            ReleaseBuffers();
            if (lightingGrid != null)
            {
                lightingGrid.Release();
                lightingGrid = null;
            }
        }

        [ContextMenu("Trace Lighting")]
        public void Trace()
        {
            if (tracerCompute == null)
            {
                Appliance.LogWarning("No tracer compute shader assigned!");
                return;
            }

            if (baker == null)
            {
                Appliance.LogWarning("No VoxelBaker assigned to tracer!");
                return;
            }

            var albedoGrid = baker.GetActiveGrid();
            if (albedoGrid == null)
            {
                Appliance.LogWarning("VoxelBaker has no baked grid. Bake first!");
                return;
            }

            int resX = Mathf.CeilToInt(baker.boundsSize.x / baker.voxelSize);
            int resY = Mathf.CeilToInt(baker.boundsSize.y / baker.voxelSize);
            int resZ = Mathf.CeilToInt(baker.boundsSize.z / baker.voxelSize);
            Vector3 worldMin = baker.transform.position - baker.boundsSize / 2;
            Vector3 worldMax = baker.transform.position + baker.boundsSize / 2;

            Appliance.Log($"Tracing lighting: {resX}x{resY}x{resZ}, {raysPerVoxel} rays/voxel, {maxBounces} bounces");

#if UNITY_EDITOR
            try {
            EditorUtility.DisplayProgressBar("Toaster Trace", "Setting up lighting grid...", 0f);
#endif

            // Create lighting grid RT
            if (lightingGrid != null) lightingGrid.Release();
            lightingGrid = new RenderTexture(resX, resY, 0, RenderTextureFormat.ARGBHalf);
            lightingGrid.dimension = TextureDimension.Tex3D;
            lightingGrid.volumeDepth = resZ;
            lightingGrid.enableRandomWrite = true;
            lightingGrid.filterMode = FilterMode.Bilinear;
            lightingGrid.wrapMode = TextureWrapMode.Clamp;
            lightingGrid.Create();

            // Clear lighting grid
            int clearKernel = tracerCompute.FindKernel("ClearLighting");
            tracerCompute.SetTexture(clearKernel, "LightingGrid", lightingGrid);
            tracerCompute.SetInt("GridResX", resX);
            tracerCompute.SetInt("GridResY", resY);
            tracerCompute.SetInt("GridResZ", resZ);
            tracerCompute.Dispatch(clearKernel,
                Mathf.CeilToInt(resX / 8f),
                Mathf.CeilToInt(resY / 8f),
                Mathf.CeilToInt(resZ / 8f));

            // Gather scene lights
            UploadLightData();

            // Setup trace kernel
            int traceKernel = tracerCompute.FindKernel("TraceLight");
            tracerCompute.SetTexture(traceKernel, "AlbedoGrid", albedoGrid);
            tracerCompute.SetTexture(traceKernel, "LightingGrid", lightingGrid);
            tracerCompute.SetVector("WorldBoundsMin", worldMin);
            tracerCompute.SetVector("WorldBoundsMax", worldMax);
            tracerCompute.SetFloat("VoxelSize", baker.voxelSize);
            tracerCompute.SetInt("GridResX", resX);
            tracerCompute.SetInt("GridResY", resY);
            tracerCompute.SetInt("GridResZ", resZ);
            tracerCompute.SetInt("RaysPerVoxel", raysPerVoxel);
            tracerCompute.SetInt("MaxBounces", maxBounces);
            tracerCompute.SetFloat("LightFalloff", lightFalloff);
            tracerCompute.SetInt("FrameSeed", (int)(Time.realtimeSinceStartup * 1000));
            tracerCompute.SetBuffer(traceKernel, "LightPositions", lightPosBuffer);
            tracerCompute.SetBuffer(traceKernel, "LightColors", lightColorBuffer);

#if UNITY_EDITOR
            EditorUtility.DisplayProgressBar("Toaster Trace", "Dispatching path tracer...", 0.3f);
#endif
            // Dispatch surface trace — 4x4x4 threads per group
            tracerCompute.Dispatch(traceKernel,
                Mathf.CeilToInt(resX / 4f),
                Mathf.CeilToInt(resY / 4f),
                Mathf.CeilToInt(resZ / 4f));

#if UNITY_EDITOR
            EditorUtility.DisplayProgressBar("Toaster Trace", "Dispatching volumetric trace (air voxels)...", 0.6f);
#endif
            // Dispatch volumetric trace for fog — overwrites LightingGrid with
            // air-voxel lighting (direct light + DDA shadows).
            // Solid voxels become (0,0,0,1), air voxels get traced light color.
            int volKernel = tracerCompute.FindKernel("TraceVolumetric");
            tracerCompute.SetTexture(volKernel, "AlbedoGrid", albedoGrid);
            tracerCompute.SetTexture(volKernel, "LightingGrid", lightingGrid);
            tracerCompute.SetVector("WorldBoundsMin", worldMin);
            tracerCompute.SetVector("WorldBoundsMax", worldMax);
            tracerCompute.SetFloat("VoxelSize", baker.voxelSize);
            tracerCompute.SetInt("GridResX", resX);
            tracerCompute.SetInt("GridResY", resY);
            tracerCompute.SetInt("GridResZ", resZ);
            tracerCompute.SetBuffer(volKernel, "LightPositions", lightPosBuffer);
            tracerCompute.SetBuffer(volKernel, "LightColors", lightColorBuffer);
            tracerCompute.Dispatch(volKernel,
                Mathf.CeilToInt(resX / 4f),
                Mathf.CeilToInt(resY / 4f),
                Mathf.CeilToInt(resZ / 4f));

            ReleaseBuffers();

#if UNITY_EDITOR
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
#endif

            Appliance.Log("Lighting trace complete!");
        }

        void UploadLightData()
        {
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);

            // Count point and spot lights
            int count = 0;
            foreach (var l in lights)
            {
                if (l.type == LightType.Point || l.type == LightType.Spot)
                    count++;
            }

            // Also add directional lights as very far point lights
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                    count++;
            }

            if (count == 0)
            {
                Appliance.LogWarning("No lights found in scene for tracing!");
                count = 1; // Dummy
            }

            var positions = new Vector4[count];
            var colors = new Vector4[count];
            int idx = 0;

            foreach (var l in lights)
            {
                if (l.type == LightType.Point || l.type == LightType.Spot)
                {
                    positions[idx] = new Vector4(l.transform.position.x, l.transform.position.y,
                        l.transform.position.z, l.range);
                    colors[idx] = new Vector4(l.color.r, l.color.g, l.color.b, l.intensity);
                    idx++;
                }
                else if (l.type == LightType.Directional)
                {
                    // Simulate directional light as a very far point light
                    Vector3 farPos = -l.transform.forward * 1000f;
                    positions[idx] = new Vector4(farPos.x, farPos.y, farPos.z, 2000f);
                    colors[idx] = new Vector4(l.color.r, l.color.g, l.color.b, l.intensity * 0.5f);
                    idx++;
                }
            }

            tracerCompute.SetInt("LightCount", idx);

            ReleaseBuffers();
            lightPosBuffer = new ComputeBuffer(Mathf.Max(1, idx), 16);
            lightColorBuffer = new ComputeBuffer(Mathf.Max(1, idx), 16);
            lightPosBuffer.SetData(positions);
            lightColorBuffer.SetData(colors);
        }

        void ReleaseBuffers()
        {
            if (lightPosBuffer != null) { lightPosBuffer.Release(); lightPosBuffer = null; }
            if (lightColorBuffer != null) { lightColorBuffer.Release(); lightColorBuffer = null; }
        }
    }
}
