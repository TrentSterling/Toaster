using System.Collections.Generic;
using UnityEngine;

namespace Toaster
{
    /// <summary>
    /// Links a VoxelBaker to a fog volume renderer. Place this on a cube
    /// with the Toaster/Volume shader. Multiple ToasterVolume instances
    /// can overlap — edge falloff ensures smooth blending at boundaries.
    /// Also registers with a static set so the froxel pipeline can find all active volumes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class ToasterVolume : MonoBehaviour
    {
        [Tooltip("The VoxelBaker whose grid this volume renders.")]
        public VoxelBaker baker;

        [Tooltip("Automatically match this volume's transform to the baker bounds.")]
        public bool autoMatchBounds = true;

        [Header("Froxel Fog")]
        [Tooltip("Density multiplier for froxel fog injection.")]
        [Range(0f, 10f)]
        public float densityMultiplier = 1f;

        [Tooltip("Intensity multiplier for froxel fog color.")]
        [Range(0f, 10f)]
        public float intensityMultiplier = 1f;

        [Tooltip("Edge falloff distance (0-1 in volume UVW). Fades density near volume boundaries for smooth overlap.")]
        [Range(0f, 0.5f)]
        public float edgeFalloff = 0.1f;

        MeshRenderer meshRenderer;
        MaterialPropertyBlock propertyBlock;

        // ============================================================
        // Static volume registry — froxel pipeline reads this
        // ============================================================

        static readonly HashSet<ToasterVolume> s_ActiveVolumes = new HashSet<ToasterVolume>();

        public static IReadOnlyCollection<ToasterVolume> ActiveVolumes => s_ActiveVolumes;

        /// <summary>
        /// Returns the best available lighting texture for froxel injection.
        /// Prefers the tracer's lightingGrid (traced), falls back to baker's albedo grid (baked).
        /// </summary>
        public Texture LightingGrid
        {
            get
            {
                if (baker == null)
                    return null;

                // Check if there's a tracer with a lighting grid on the same object
                var tracer = baker.GetComponent<ToasterTracer>();
                if (tracer != null && tracer.lightingGrid != null)
                    return tracer.lightingGrid;

                // Fall back to baker's albedo grid
                return baker.GetActiveGrid();
            }
        }

        void OnEnable()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            propertyBlock = new MaterialPropertyBlock();
            s_ActiveVolumes.Add(this);
        }

        void OnDisable()
        {
            s_ActiveVolumes.Remove(this);
        }

        void Update()
        {
            if (baker == null || meshRenderer == null) return;

            var grid = baker.GetActiveGrid();
            if (grid == null) return;

            // Auto-match transform to baker bounds
            if (autoMatchBounds)
            {
                transform.position = baker.transform.position;
                transform.rotation = Quaternion.identity;
                transform.localScale = baker.boundsSize;
            }

            // Set per-instance properties via MaterialPropertyBlock
            // This allows multiple volumes with different textures
            // to share the same material without creating instances.
            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture("_VolumeTex", grid);
            meshRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// Wire this volume to a specific baker. Called by auto-wire.
        /// </summary>
        public void ConfigureFromBaker(VoxelBaker source)
        {
            baker = source;
        }
    }
}
