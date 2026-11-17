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

}
