using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        // The one place shadow mesh views are built; GPU culling and the shadow passes request the same views. Casters use the
        // camera's LOD projection, so a shadow shows the level the camera draws.
        MeshView CascadeShadowView(Light light, in FCascadeShadowSlice slice, int cascade, Camera camera)
        {
            var planes = m_RGBuilder.GetRecordingArray<Plane>(slice.splitData.cullingPlaneCount);
            for (int plane = 0; plane < planes.Length; plane++) planes[plane] = slice.splitData.GetCullingPlane(plane);
            return MeshView.FromShadow(EMeshViewKind.CascadeShadow, UnityEntityId.ToUInt64(light), planes, camera.transform.position, light.cullingMask,
                RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask)), cascade, MeshView.SceneCullingMaskFor(camera), MeshLODProjection.FromCamera(camera));
        }

        MeshView LocalShadowView(in VisibleLight visibleLight, in FLocalShadowSlice local, Camera camera)
        {
            Light light = visibleLight.light;
            var planes = m_RGBuilder.GetRecordingArray<Plane>(local.splitData.cullingPlaneCount);
            for (int plane = 0; plane < planes.Length; plane++) planes[plane] = local.splitData.GetCullingPlane(plane);
            Vector3 position = visibleLight.localToWorldMatrix.GetColumn(3);
            return MeshView.FromShadow(EMeshViewKind.LocalShadow, UnityEntityId.ToUInt64(light), planes, position, light.cullingMask,
                RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask)), local.face, MeshView.SceneCullingMaskFor(camera), MeshLODProjection.FromCamera(camera));
        }

        void RecordMeshGpuCulls(RenderContext renderContext, Camera camera, in MeshView mainView, in CullingResults cullingResults)
        {
            m_RGBuilder.RequestMeshGPUCulling(mainView);
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            if (allocator == null) return;

            int cascadeLight = allocator.CascadeVisibleLightIndex;
            if (cascadeLight >= 0 && cascadeLight < cullingResults.visibleLights.Length)
            {
                Light shadowLight = cullingResults.visibleLights[cascadeLight].light;
                for (int cascade = 0; cascade < ShadowAllocator.CascadeCount; ++cascade)
                {
                    FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (slice.valid) m_RGBuilder.RequestMeshGPUCulling(CascadeShadowView(shadowLight, slice, cascade, camera));
                }
            }

            for (int sliceIndex = 0; sliceIndex < allocator.LocalSliceCount; ++sliceIndex)
            {
                FLocalShadowSlice local = allocator.LocalSlices[sliceIndex];
                if (local.cachedSampling || !local.valid || local.visibleLightIndex < 0 || local.visibleLightIndex >= cullingResults.visibleLights.Length)
                    continue;
                m_RGBuilder.RequestMeshGPUCulling(LocalShadowView(cullingResults.visibleLights[local.visibleLightIndex], local, camera));
            }
        }
    }
}
