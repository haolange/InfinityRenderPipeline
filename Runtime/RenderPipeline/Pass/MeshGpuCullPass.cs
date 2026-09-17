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
        void RecordMeshGpuCulls(RenderContext renderContext, Camera camera, in MeshView mainView, in CullingResults cullingResults)
        {
            m_RGBuilder.EnsureMeshGpuCull(mainView);
            if (renderContext?.lightContext?.ShadowAllocator == null)
            {
                return;
            }

            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            int cascadeLight = allocator.CascadeVisibleLightIndex;
            if (cascadeLight >= 0 && cascadeLight < cullingResults.visibleLights.Length)
            {
                Light shadowLight = cullingResults.visibleLights[cascadeLight].light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                uint shadowRenderingLayerMask = RenderingLayerUtility.Validate(unchecked((uint)shadowLight.renderingLayerMask));
                for (int cascade = 0; cascade < ShadowAllocator.CascadeCount; ++cascade)
                {
                    FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }

                    var cascadePlanes = new Plane[slice.splitData.cullingPlaneCount];
                    for (int plane = 0; plane < cascadePlanes.Length; plane++)
                    {
                        cascadePlanes[plane] = slice.splitData.GetCullingPlane(plane);
                    }

                    m_RGBuilder.EnsureMeshGpuCull(MeshView.FromCascadeShadow(
                        lightInstanceId,
                        cascadePlanes,
                        camera.transform.position,
                        shadowLight.cullingMask,
                        shadowRenderingLayerMask,
                        cascade));
                }
            }

            for (int sliceIndex = 0; sliceIndex < allocator.LocalSliceCount; ++sliceIndex)
            {
                FLocalShadowSlice local = allocator.LocalSlices[sliceIndex];
                if (!local.valid || local.visibleLightIndex < 0 || local.visibleLightIndex >= cullingResults.visibleLights.Length)
                {
                    continue;
                }

                VisibleLight visibleLight = cullingResults.visibleLights[local.visibleLightIndex];
                Light shadowLight = visibleLight.light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                Vector3 lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
                uint shadowRenderingLayerMask = RenderingLayerUtility.Validate(unchecked((uint)shadowLight.renderingLayerMask));
                var shadowPlanes = new Plane[local.splitData.cullingPlaneCount];
                for (int plane = 0; plane < shadowPlanes.Length; plane++)
                {
                    shadowPlanes[plane] = local.splitData.GetCullingPlane(plane);
                }

                m_RGBuilder.EnsureMeshGpuCull(MeshView.FromLocalShadow(
                    lightInstanceId,
                    shadowPlanes,
                    lightPosition,
                    shadowLight.cullingMask,
                    shadowRenderingLayerMask,
                    local.face));
            }
        }
    }
}
