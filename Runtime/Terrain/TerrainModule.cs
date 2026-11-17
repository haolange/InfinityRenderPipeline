#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Component;

namespace InfinityTech.Rendering.TerrainPipeline
{
    /// <summary>
    /// The terrain module of the scene world, an in-development feature (AGENTS R15). Persistent layer: the terrain table
    /// (component entity id to component, derived from the component registry). View layer: the section LOD of each
    /// terrain for the camera being culled. No pass draws terrain sections yet; their draw lists will read this view layer.
    /// </summary>
    internal sealed class TerrainModule
    {
        public readonly SceneTable<TerrainComponent> table = new SceneTable<TerrainComponent>();

        public void Prepare() => table.Prepare(TerrainComponent.s_Registry);

        public void Commit() => table.Commit();

        public void Rollback() => table.Rollback();

        public void ProcessView(Camera camera, int2 internalSize)
        {
            if (table.count == 0)
            {
                return;
            }
            float4x4 projection = TerrainUtility.GetProjectionMatrix(camera.fieldOfView + 30, internalSize.x, internalSize.y, camera.nearClipPlane, camera.farClipPlane);
            float3 viewOrigin = camera.transform.position;
            foreach (TerrainComponent terrain in table.components)
            {
                terrain.ProcessLOD(viewOrigin, projection);
#if UNITY_EDITOR
                if (Handles.ShouldRenderGizmos())
                {
                    terrain.DrawBounds(true);
                }
#endif
            }
        }
    }
}
