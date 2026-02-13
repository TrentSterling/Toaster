using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toaster
{
    public class ToasterFroxelFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("Resolution")]
            [Tooltip("Froxel grid resolution (X=width, Y=height, Z=depth slices).")]
            public Vector3Int froxelResolution = new Vector3Int(160, 90, 128);

            [Header("Depth")]
            [Tooltip("Maximum fog distance from camera.")]
            public float maxDistance = 200f;

            [Tooltip("Near plane for froxel grid (should match camera near).")]
            public float nearPlane = 0.3f;

            [Tooltip("Blend between logarithmic (0) and linear (1) depth slicing.")]
            [Range(0f, 1f)]
            public float depthUniformity = 0.5f;

            [Header("Fog")]
            [Tooltip("Base fog density. Controls how thick the fog medium is.")]
            [Range(0f, 1f)]
            public float fogDensity = 0.03f;

            [Tooltip("How strongly baked light colors the fog.")]
            [Range(0f, 10f)]
            public float fogIntensity = 1f;

            [Tooltip("Ambient fog color — provides base haze even in unlit areas.")]
            public Color ambientColor = new Color(0.02f, 0.02f, 0.04f, 1f);

            [Header("Lighting")]
            [Tooltip("Maximum number of scene lights to evaluate per froxel.")]
            [Range(0, 32)]
            public int maxLights = 16;

            [Tooltip("Scattering anisotropy (Henyey-Greenstein g). 0 = isotropic, >0 = forward scatter, <0 = back scatter.")]
            [Range(-0.99f, 0.99f)]
            public float scatterAnisotropy = 0.3f;

            [Header("Temporal")]
            [Tooltip("Enable temporal reprojection for smoother fog.")]
            public bool enableTemporal = false;

            [Tooltip("Temporal blend weight. Lower = more history (smoother but ghostier).")]
            [Range(0.01f, 1f)]
            public float temporalBlendAlpha = 0.05f;
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
