using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
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
        public int NumQuad;
        [HideInInspector]
        public int NumSection;
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
            print("OnRigister");
            TerrainSector.Initializ();
            TerrainSector.FlushLODData(LOD0ScreenSize, LOD0Distribution, LODXDistribution);
            TerrainSector.FlushNative();
        }

        protected override void OnTransformChange()
        {

        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {
            //print("XX");
        }

        protected override void UnRegister()
        {
            print("UnRigister");
            TerrainSector.Release();
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            print("Serialize");
            UnityTerrain = GetComponent<UnityEngine.Terrain>();
            UnityTerrainData = GetComponent<TerrainCollider>().terrainData;

            NumSection = TerrainUtility.GetSectionNumFromTerrainSize(SectorSize);
            NumQuad = (SectorSize) / TerrainUtility.GetSectionNumFromTerrainSize(SectorSize);

            TerrainTexture HeightTexture = new TerrainTexture(SectorSize);
            HeightTexture.TerrainDataToHeightmap(UnityTerrainData);

            TerrainSector = new FTerrainSector(SectorSize, NumSection, NumQuad, transform.position, UnityTerrainData.bounds);
            TerrainSector.FlushBounds(NumQuad, SectorSize, TerrainScaleY, transform.position, HeightTexture.HeightMap);

            HeightTexture.Release();
        }

        private void DrawBound()
        {
            TerrainSector.DrawBound();
        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
        }
#endif
    }
}
