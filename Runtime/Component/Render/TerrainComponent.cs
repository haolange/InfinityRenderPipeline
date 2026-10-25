#if UNITY_EDITOR
using UnityEditor;
#endif
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
        }

        protected override void UnRegister()
        {
            terrainSector?.Dispose();

            TerrainComponent self = this;
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.RemoveWorldTerrain(self);
            });
        }

        public void ProcessLOD(in float3 viewOringin, in float4x4 matrix_Proj)
        {
            terrainSector.ProcessLOD(sectionSize, viewOringin, matrix_Proj);
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            Terrain source = GetComponent<Terrain>();
            TerrainData data = source != null ? source.terrainData : null;
            if (data == null) throw new System.InvalidOperationException("Sector authoring requires a Unity Terrain with TerrainData.");
            int quads = data.heightmapResolution - 1;
            if (quads < 64) throw new System.InvalidOperationException("Infinity terrain sectors require a heightmap resolution of at least 65.");
            var height = new TerrainTexture(quads);
            try
            {
                height.TerrainDataToHeightmap(data);
                int count = quads / 64;
                var replacement = new TerrainSector(quads, count, quads / count, transform.position, data.bounds);
                replacement.BuildBounds(quads, quads / count, data.size.y, transform.position, height.heightMap);
                terrainSector?.Dispose();
                terrain = source;
                terrainData = data;
                terrainSector = replacement;
            }
            finally { height.Release(); }
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
