using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Renderer lists of a camera's culling result. One place builds drawing and filtering settings, including the GPU
    // instancing the pipeline asset chooses for draws the SRP Batcher does not take.
    internal static class CameraRendererLists
    {
        internal static readonly ShaderTagId UnlitPass = new ShaderTagId("SRPDefaultUnlit");

        internal static RendererListParams Create(in CullingResults cullingResults, Camera camera, ShaderTagId passTag,
            RenderQueueRange queues, SortingCriteria sorting, PerObjectData perObjectData, bool enableInstancing)
        {
            var drawing = new DrawingSettings(passTag, new SortingSettings(camera) { criteria = sorting });
            return Finish(cullingResults, camera, drawing, queues, perObjectData, enableInstancing);
        }

        // A list that also draws Unity's unlit pass of each material.
        internal static RendererListParams CreateWithUnlit(in CullingResults cullingResults, Camera camera, ShaderTagId passTag,
            RenderQueueRange queues, SortingCriteria sorting, PerObjectData perObjectData, bool enableInstancing)
        {
            var drawing = new DrawingSettings(passTag, new SortingSettings(camera) { criteria = sorting });
            drawing.SetShaderPassName(1, UnlitPass);
            return Finish(cullingResults, camera, drawing, queues, perObjectData, enableInstancing);
        }

        static RendererListParams Finish(in CullingResults cullingResults, Camera camera, DrawingSettings drawing,
            RenderQueueRange queues, PerObjectData perObjectData, bool enableInstancing)
        {
            drawing.perObjectData = perObjectData;
            drawing.enableInstancing = enableInstancing;
            var filtering = new FilteringSettings(queues, camera.cullingMask, uint.MaxValue);
            return new RendererListParams(cullingResults, drawing, filtering);
        }
    }
}
