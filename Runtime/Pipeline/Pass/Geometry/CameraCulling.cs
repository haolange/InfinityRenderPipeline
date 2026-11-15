using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // The pipeline's views (camera, shadow cascade, local-light face) as mesh culling contexts. Shadow contexts copy their
    // camera's LOD projection, so a shadow shows the level the camera draws.
    internal static class CameraCulling
    {
        internal static MeshLODProjection LODProjection(Camera camera)
        {
            bool orthographic = camera.orthographic;
            return new MeshLODProjection
            {
                origin = camera.transform.position,
                scale = orthographic ? 1.0f / camera.orthographicSize : 1.0f / math.tan(math.radians(camera.fieldOfView) * 0.5f),
                bias = QualitySettings.lodBias,
                orthographic = orthographic
            };
        }

        // A camera context does not filter rendering layers.
        internal static MeshCullingContext Context(Camera camera, ref ScriptableCullingParameters cullingParameters)
        {
            Span<Plane> planes = stackalloc Plane[MeshCullingContext.MaxPlanes];
            int count = math.min(MeshCullingContext.MaxPlanes, cullingParameters.cullingPlaneCount);
            for (int i = 0; i < count; ++i)
            {
                planes[i] = cullingParameters.GetCullingPlane(i);
            }
            return MeshCullingContext.Create(planes.Slice(0, count), camera.cullingMask, CameraSceneCulling.Mask(camera),
                (uint)ERenderingLayer.Everything, false, LODProjection(camera));
        }

        // A light context filters by the light's rendering layers. Split planes beyond the context's six are dropped, which
        // only keeps more casters.
        internal static MeshCullingContext Context(Camera camera, Light light, in ShadowSplitData split)
        {
            Span<Plane> planes = stackalloc Plane[MeshCullingContext.MaxPlanes];
            int count = math.min(MeshCullingContext.MaxPlanes, split.cullingPlaneCount);
            for (int i = 0; i < count; ++i)
            {
                planes[i] = split.GetCullingPlane(i);
            }
            return MeshCullingContext.Create(planes.Slice(0, count), light.cullingMask, CameraSceneCulling.Mask(camera),
                RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask)), true, LODProjection(camera));
        }
    }

    public partial class InfinityRenderPipeline
    {
        RGMeshDrawListHandle CreateMeshList(in MeshCullingContext cullingContext, ShaderTagId pass)
            => m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(cullingContext, pass));

        RGMeshDrawListHandle[] CreateCascadeMeshLists(in CameraRecordContext context)
        {
            int count = ShadowAllocator.CascadeCount;
            var lists = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(count);
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            int lightIndex = allocator.CascadeVisibleLightIndex;
            if (lightIndex < 0)
                return lists;
            Light shadowLight = context.cullingResults.visibleLights[lightIndex].light;
            for (int cascade = 0; cascade < count; ++cascade)
            {
                CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                if (!slice.valid)
                    continue;
                lists[cascade] = CreateMeshList(CameraCulling.Context(context.camera, shadowLight, slice.splitData), InfinityPassIDs.RasterShadowPass);
            }
            return lists;
        }

        RGMeshDrawListHandle[] CreateLocalMeshLists(in CameraRecordContext context)
        {
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            int count = allocator.LocalSliceCount;
            var lists = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(count < 1 ? 1 : count);
            for (int slice = 0; slice < count; ++slice)
            {
                LocalShadowSlice local = allocator.LocalSlices[slice];
                if (local.cachedSampling || !local.valid)
                    continue;
                Light light = context.cullingResults.visibleLights[local.visibleLightIndex].light;
                lists[slice] = CreateMeshList(CameraCulling.Context(context.camera, light, local.splitData), InfinityPassIDs.RasterShadowPass);
            }
            return lists;
        }
    }
}
