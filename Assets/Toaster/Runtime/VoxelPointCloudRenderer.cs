using UnityEngine;

namespace Toaster
{
    [ExecuteAlways]
    public class VoxelPointCloudRenderer : MonoBehaviour
    {
        [Header("Data")]
        public RenderTexture volumeTexture;
        public Vector3 boundsMin = new Vector3(-6, -4, -6);
        public Vector3 boundsMax = new Vector3(6, 4, 6);
        public Vector3Int gridResolution = new Vector3Int(48, 32, 48);

        [Header("Appearance")]
        public Material pointCloudMaterial;
        [Range(0.01f, 0.5f)] public float pointSize = 0.1f;
        [Range(0.001f, 0.5f)] public float threshold = 0.01f;
        [Range(1f, 10f)] public float colorBoost = 3f;

        public void ConfigureFromBaker(VoxelBaker baker)
        {
            if (baker == null || baker.voxelGrid == null) return;

            volumeTexture = baker.voxelGrid;
            boundsMin = baker.transform.position - baker.boundsSize / 2;
            boundsMax = baker.transform.position + baker.boundsSize / 2;
            gridResolution = new Vector3Int(
                Mathf.CeilToInt(baker.boundsSize.x / baker.voxelSize),
                Mathf.CeilToInt(baker.boundsSize.y / baker.voxelSize),
                Mathf.CeilToInt(baker.boundsSize.z / baker.voxelSize)
            );
        }

        void Update()
        {
            if (volumeTexture == null || pointCloudMaterial == null) return;

            int totalVoxels = gridResolution.x * gridResolution.y * gridResolution.z;
            if (totalVoxels <= 0) return;

            pointCloudMaterial.SetTexture("_VolumeTex", volumeTexture);
            pointCloudMaterial.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0));
            pointCloudMaterial.SetVector("_BoundsMax", new Vector4(boundsMax.x, boundsMax.y, boundsMax.z, 0));
            pointCloudMaterial.SetVector("_GridRes", new Vector4(gridResolution.x, gridResolution.y, gridResolution.z, 0));
            pointCloudMaterial.SetFloat("_PointSize", pointSize);
            pointCloudMaterial.SetFloat("_Threshold", threshold);
            pointCloudMaterial.SetFloat("_ColorBoost", colorBoost);

            // Draw procedurally: 6 verts per quad, one quad per voxel
            var bounds = new Bounds(
                (boundsMin + boundsMax) * 0.5f,
                boundsMax - boundsMin
            );

            Graphics.DrawProcedural(
                pointCloudMaterial,
                bounds,
                MeshTopology.Triangles,
                6,              // vertices per instance (2 triangles = 1 quad)
                totalVoxels     // one instance per voxel
            );
        }
    }
}
