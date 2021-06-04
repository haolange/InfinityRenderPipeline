using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Rendering.TerrainPipeline;

namespace InfinityTech.Component
{
    //[ExecuteInEditMode]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    [AddComponentMenu("InfinityRenderer/Terrain Component")]
    public class TerrainComponent : EntityComponent
    {
        public float lod0ScreenSize = 0.5f;
        public float lod0Distribution = 1.25f;
        public float lodXDistribution = 2.8f;

        public int numSection
        {
            get
            {
                return sectorSize / 32;
            }
        }
        public int sectorSize
        {
            get
            {
                return terrainData.heightmapResolution - 1;
            }
        }
        public int sectionSize
        {
            get
            {
                return (sectorSize) / numSection;
            }
        }
        public float terrainScaleY
        {
            get
            {
                return terrainData.size.y;
            }
        }

        [HideInInspector]
        public Terrain terrain;
        [HideInInspector]
        public TerrainData terrainData;
        [HideInInspector]
        public FTerrainSector terrainSector;

        public TerrainComponent() : base()
        {

        }

        protected override void OnRegister()
        {
            GetWorld().AddWorldTerrain(this);
            terrainSector?.Initializ();
            terrainSector?.BuildLODData(lod0ScreenSize, lod0Distribution, lodXDistribution);
        }

        protected override void OnTransformChange()
        {

        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {

        }

        protected override void UnRegister()
        {
            GetWorld().RemoveWorldTerrain(this);
            terrainSector?.Dispose();
        }

        public void UpdateLODData(in float3 viewOringin, in float4x4 matrix_Proj)
        {
            terrainSector.UpdateLODData(sectionSize, viewOringin, matrix_Proj);
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            terrain = GetComponent<UnityEngine.Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;

            TerrainTexture HeightTexture = new TerrainTexture(sectorSize);
            HeightTexture.TerrainDataToHeightmap(terrainData);

            /*if (terrainSector != null)
            {
                if (terrainSector.nativeCreated == true)
                {
                    terrainSector.ReleaseNativeCollection();
                }
            }*/

            terrainSector = new FTerrainSector(sectorSize, numSection, sectionSize, transform.position, terrainData.bounds);
            terrainSector.BuildBounds(sectorSize, sectionSize, terrainScaleY, transform.position, HeightTexture.HeightMap);
            //terrainSector.BuildLODData(lod0ScreenSize, lod0Distribution, lodXDistribution);
            //terrainSector.BuildNativeCollection();

            HeightTexture.Release();
        }

        public void DrawBounds(in bool useLODColor = false)
        {
            terrainSector.DrawBound(useLODColor);
        }

        void OnDrawGizmosSelected()
        {

        }
#endif
    }
}
