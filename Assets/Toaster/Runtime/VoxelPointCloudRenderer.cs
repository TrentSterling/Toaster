using UnityEngine;

namespace Toaster
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class VoxelPointCloudRenderer : MonoBehaviour
    {
        public RenderTexture volumeTexture;
        public Vector3 boundsMin = new Vector3(-6, -4, -6);
        public Vector3 boundsMax = new Vector3(6, 4, 6);
        public Vector3Int gridResolution = new Vector3Int(48, 32, 48);

        private MaterialPropertyBlock propBlock;

        void OnEnable()
        {
            propBlock = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (volumeTexture == null) return;

            var rend = GetComponent<MeshRenderer>();
            if (rend == null || rend.sharedMaterial == null) return;

            rend.GetPropertyBlock(propBlock);
            propBlock.SetTexture("_VolumeTex", volumeTexture);
            propBlock.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0));
            propBlock.SetVector("_BoundsMax", new Vector4(boundsMax.x, boundsMax.y, boundsMax.z, 0));
            propBlock.SetVector("_GridRes", new Vector4(gridResolution.x, gridResolution.y, gridResolution.z, 0));
            rend.SetPropertyBlock(propBlock);
        }

        /// <summary>
        /// Configure from a VoxelBaker's results
        /// </summary>
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
    }
}
