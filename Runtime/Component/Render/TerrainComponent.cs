using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Rendering.Pipeline;
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
                return sectorSize / 64;
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
        public TerrainSector terrainSector;

        protected override void OnRegister()
        {
            terrainSector?.Initializ();
            terrainSector?.BuildLODData(lod0ScreenSize, lod0Distribution, lodXDistribution);
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.AddWorldTerrain(this);
            });
            //RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        /*void OnBeginCameraRendering(ScriptableRenderContext renderContext, Camera camera)
        {
            List<TerrainComponent> terrains = renderWorld.GetWorldTerrains();
            float4x4 matrix_Proj = TerrainUtility.GetProjectionMatrix(camera.fieldOfView + 30, camera.pixelWidth, camera.pixelHeight, camera.nearClipPlane, camera.farClipPlane);

            for(int i = 0; i < terrains.Count; ++i)
            {
                TerrainComponent terrain = terrains[i];
                terrain.UpdateLODData(camera.transform.position, matrix_Proj);

                #if UNITY_EDITOR
                    if (Handles.ShouldRenderGizmos())
                    {
                        terrain.DrawBounds(true);
                    }
                #endif
            }
        }*/

        protected override void UnRegister()
        {
            terrainSector?.Dispose();
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.RemoveWorldTerrain(this);
            });
            //RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        public void ProcessLOD(in float3 viewOringin, in float4x4 matrix_Proj)
        {
            terrainSector.ProcessLOD(sectionSize, viewOringin, matrix_Proj);
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

            terrainSector = new TerrainSector(sectorSize, numSection, sectionSize, transform.position, terrainData.bounds);
            terrainSector.BuildBounds(sectorSize, sectionSize, terrainScaleY, transform.position, HeightTexture.heightMap);
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
