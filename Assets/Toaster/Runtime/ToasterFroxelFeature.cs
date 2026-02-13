using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toaster
{
    public class ToasterFroxelFeature : ScriptableRendererFeature
    {
        public enum DebugMode
        {
            Off = 0,
            Scattering = 1,
            Extinction = 2,
            Transmittance = 3,
            DepthSlices = 4
        }

        [System.Serializable]
        public class Settings
        {
            [Header("Resolution")]
            [Tooltip("Froxel grid resolution (X=width, Y=height, Z=depth slices).")]
            public Vector3Int froxelResolution = new Vector3Int(160, 90, 128);

            [Header("Depth")]
            [Tooltip("How far the froxel grid extends from the camera (meters). Larger = covers more scene but less depth precision per slice.")]
            public float maxDistance = 200f;

            [Tooltip("Near plane for froxel grid (should match camera near).")]
            public float nearPlane = 0.3f;

            [Tooltip("Blend between logarithmic (0) and linear (1) depth slicing.")]
            [Range(0f, 1f)]
            public float depthUniformity = 0.5f;

            [Header("Fog")]
            [Tooltip("Extinction coefficient — fog absorption per meter. Controls opacity. 0.005 = light haze, 0.02 = medium, 0.05 = thick.")]
            [Range(0f, 0.2f)]
            public float fogDensity = 0.015f;

            [Tooltip("Brightness multiplier for baked light color in fog. 0 = lights-only mode (no baked tint). Higher = more colorful fog from baked data.")]
            [Range(0f, 10f)]
            public float fogIntensity = 1f;

            [Tooltip("Scattering albedo — multiplies fog brightness without changing opacity. >1 = brighter light halos at same density. 3 is a good starting point.")]
            [Range(0f, 10f)]
            public float scatteringAlbedo = 3f;

            [Tooltip("Base haze color added everywhere in fog. Subtle values (0.05-0.1) give atmospheric depth. Too high = washed out.")]
            public Color ambientColor = new Color(0.08f, 0.08f, 0.12f, 1f);

            [Header("Lighting")]
            [Tooltip("Maximum number of scene lights to evaluate per froxel.")]
            [Range(0, 32)]
            public int maxLights = 16;

            [Tooltip("Scattering anisotropy (Henyey-Greenstein g). 0 = isotropic, >0 = forward scatter, <0 = back scatter.")]
            [Range(-0.99f, 0.99f)]
            public float scatterAnisotropy = 0.3f;

            [Header("Light Fog")]
            [Tooltip("Extra fog density near point/spot lights, creating visible glow halos. Weighted by light intensity and distance attenuation. 1-2 = visible halos.")]
            [Range(0f, 5f)]
            public float lightDensityBoost = 1.5f;

            [Header("Height Fog")]
            [Tooltip("Enable Y-based density falloff (thicker at ground level).")]
            public bool enableHeightFog = false;

            [Tooltip("World Y below which fog is at full density.")]
            public float heightFogBase = 0f;

            [Tooltip("World Y above which fog is zero density.")]
            public float heightFogTop = 20f;

            [Header("Temporal")]
            [Tooltip("Enable temporal reprojection for smoother fog. Strongly recommended.")]
            public bool enableTemporal = true;

            [Tooltip("Temporal blend weight — how much of the current frame to use. Low (0.05) = smooth but ghosty when camera moves. High (0.3-0.5) = responsive, less ghosting. 0.2 = balanced default.")]
            [Range(0.01f, 1f)]
            public float temporalBlendAlpha = 0.2f;

            [Header("Debug")]
            [Tooltip("Debug visualization mode. Off = normal fog compositing.")]
            public DebugMode debugMode = DebugMode.Off;
        }

        [Header("Settings")]
        public Settings settings = new Settings();

        [Header("References")]
        [Tooltip("Assign ToasterFroxel.compute")]
        public ComputeShader froxelCompute;

        [Tooltip("Assign ToasterFroxelApply.shader")]
        public Shader applyShader;

        [Tooltip("128x128 R8 blue noise texture for jitter.")]
        public Texture2D blueNoise;

        private ToasterFroxelPass m_FroxelPass;
        private Material m_ApplyMaterial;

        public override void Create()
        {
            if (applyShader != null)
                m_ApplyMaterial = CoreUtils.CreateEngineMaterial(applyShader);

            m_FroxelPass = new ToasterFroxelPass();
            m_FroxelPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

            // Request depth texture so _CameraDepthTexture is available
            m_FroxelPass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Skip preview and reflection cameras
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            // Need all references
            if (froxelCompute == null || m_ApplyMaterial == null)
                return;

            // Skip if no active ToasterVolume instances
            if (ToasterVolume.ActiveVolumes == null || ToasterVolume.ActiveVolumes.Count == 0)
                return;

            m_FroxelPass.Setup(settings, froxelCompute, m_ApplyMaterial, blueNoise);
            renderer.EnqueuePass(m_FroxelPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_FroxelPass?.Dispose();
            if (m_ApplyMaterial != null)
                CoreUtils.Destroy(m_ApplyMaterial);
        }
    }
}
