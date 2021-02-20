using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Rendering.TerrainPipeline;

namespace InfinityTech.Component
{
    [AddComponentMenu("InfinityRenderer/Terrain Component")]
    public class TerrainComponent : EntityComponent
    {
        [Header("Terrain Setting")]
        public float LOD0ScreenSize = 0.5f;
        public float LOD0Distribution = 1.25f;
        public float LODXDistribution = 2.8f;

        [HideInInspector]
        public int NumSection;
        [HideInInspector]
        public int SectionSize;
        public int SectorSize
        {
            get
            {
                return UnityTerrainData.heightmapResolution - 1;
            }
        }
        public float TerrainScaleY
        {
            get
            {
                return UnityTerrainData.size.y;
            }
        }

        [HideInInspector]
        public Terrain UnityTerrain;
        [HideInInspector]
        public TerrainData UnityTerrainData;
        [HideInInspector]
        public FTerrainSector TerrainSector;


        public TerrainComponent() : base()
        {

        }

        protected override void OnRegister()
        {
            GetWorld().AddWorldTerrain(this);

            TerrainSector.BuildLODData(LOD0ScreenSize, LOD0Distribution, LODXDistribution);
            TerrainSector.BuildNativeCollection();
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

            TerrainSector.ReleaseNativeCollection();
        }

        public void UpdateLODData(in float3 ViewOringin, in float4x4 Matrix_Proj)
        {
            TerrainSector.UpdateLODData(SectionSize, ViewOringin, Matrix_Proj);
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            UnityTerrain = GetComponent<UnityEngine.Terrain>();
            UnityTerrainData = GetComponent<TerrainCollider>().terrainData;

            NumSection = TerrainUtility.GetSectionNumFromTerrainSize(SectorSize);
            SectionSize = (SectorSize) / NumSection;

            TerrainTexture HeightTexture = new TerrainTexture(SectorSize);
            HeightTexture.TerrainDataToHeightmap(UnityTerrainData);

            TerrainSector.ReleaseNativeCollection();

            TerrainSector = new FTerrainSector(SectorSize, NumSection, SectionSize, transform.position, UnityTerrainData.bounds);
            TerrainSector.BuildBounds(SectorSize, SectionSize, TerrainScaleY, transform.position, HeightTexture.HeightMap);
            TerrainSector.BuildLODData(LOD0ScreenSize, LOD0Distribution, LODXDistribution);
            TerrainSector.BuildNativeCollection();

            HeightTexture.Release();
        }

        public void DrawBounds(in bool LODColor = false)
        {
            TerrainSector.DrawBound(LODColor);
        }

        void OnDrawGizmosSelected()
        {

        }
#endif
    }
}
