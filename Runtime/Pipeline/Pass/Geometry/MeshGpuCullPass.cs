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
        MeshView CascadeShadowView(in CameraRecordContext context, Light light, in CascadeShadowSlice slice, int cascade)
        {
            var planes = m_RGBuilder.GetRecordingArray<Plane>(slice.splitData.cullingPlaneCount);
            for (int plane = 0; plane < planes.Length; plane++)
            {
                planes[plane] = slice.splitData.GetCullingPlane(plane);
            }
            return MeshView.FromShadow(EMeshViewKind.CascadeShadow, UnityEntityId.ToUInt64(light), planes, context.camera.transform.position, light.cullingMask,
                RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask)), cascade, MeshView.SceneCullingMaskFor(context.camera), MeshLODProjection.FromCamera(context.camera));
        }

        MeshView LocalShadowView(in CameraRecordContext context, in VisibleLight visibleLight, in LocalShadowSlice local)
        {
            Light light = visibleLight.light;
            var planes = m_RGBuilder.GetRecordingArray<Plane>(local.splitData.cullingPlaneCount);
            for (int plane = 0; plane < planes.Length; plane++)
            {
                planes[plane] = local.splitData.GetCullingPlane(plane);
            }
            Vector3 position = visibleLight.localToWorldMatrix.GetColumn(3);
            return MeshView.FromShadow(EMeshViewKind.LocalShadow, UnityEntityId.ToUInt64(light), planes, position, light.cullingMask,
                RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask)), local.face, MeshView.SceneCullingMaskFor(context.camera), MeshLODProjection.FromCamera(context.camera));
        }

        void RecordMeshGpuCulls(in CameraRecordContext context, in MeshView mainView)
        {
            m_RGBuilder.RequestMeshGPUCulling(mainView);
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            int cascadeLight = allocator.CascadeVisibleLightIndex;
            if (cascadeLight >= 0)
            {
                Light shadowLight = context.cullingResults.visibleLights[cascadeLight].light;
                for (int cascade = 0; cascade < ShadowAllocator.CascadeCount; ++cascade)
                {
                    CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (slice.valid)
                    {
                        m_RGBuilder.RequestMeshGPUCulling(CascadeShadowView(context, shadowLight, slice, cascade));
                    }
                }
            }

            for (int sliceIndex = 0; sliceIndex < allocator.LocalSliceCount; ++sliceIndex)
            {
                LocalShadowSlice local = allocator.LocalSlices[sliceIndex];
                if (local.cachedSampling || !local.valid)
                    continue;
                m_RGBuilder.RequestMeshGPUCulling(LocalShadowView(context, context.cullingResults.visibleLights[local.visibleLightIndex], local));
            }
        }

        RGMeshDrawListHandle MakeMeshList(in MeshView view, ShaderTagId pass)
            => m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(view, pass));

        void PrepareMeshList(in RGMeshDrawListHandle handle)
        {
            if (handle.IsValid)
                m_RGBuilder.PrepareMeshDrawList(handle);
        }

        void PrepareMeshLists(RGMeshDrawListHandle[] handles)
        {
            for (int i = 0; i < handles.Length; ++i)
                PrepareMeshList(handles[i]);
        }

        void PrepareMeshLists(
            in RGMeshDrawListHandle first, in RGMeshDrawListHandle second, in RGMeshDrawListHandle third, in RGMeshDrawListHandle fourth,
            in RGMeshDrawListHandle fifth, in RGMeshDrawListHandle sixth, in RGMeshDrawListHandle seventh, in RGMeshDrawListHandle eighth)
        {
            PrepareMeshList(first);
            PrepareMeshList(second);
            PrepareMeshList(third);
            PrepareMeshList(fourth);
            PrepareMeshList(fifth);
            PrepareMeshList(sixth);
            PrepareMeshList(seventh);
            PrepareMeshList(eighth);
        }

        RGMeshDrawListHandle[] CreateCascadeMeshLists(in CameraRecordContext context)
        {
            int count = ShadowAllocator.CascadeCount;
            var lists = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(count);
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            if (allocator == null)
                return lists;
            int lightIndex = allocator.CascadeVisibleLightIndex;
            if (lightIndex < 0 || lightIndex >= context.cullingResults.visibleLights.Length)
                return lists;
            Light shadowLight = context.cullingResults.visibleLights[lightIndex].light;
            for (int cascade = 0; cascade < count; ++cascade)
            {
                CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                if (!slice.valid)
                    continue;
                lists[cascade] = MakeMeshList(CascadeShadowView(context, shadowLight, slice, cascade), InfinityPassIDs.RasterShadowPass);
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
                lists[slice] = MakeMeshList(LocalShadowView(context, context.cullingResults.visibleLights[local.visibleLightIndex], local), InfinityPassIDs.RasterShadowPass);
            }
            return lists;
        }
    }
}
