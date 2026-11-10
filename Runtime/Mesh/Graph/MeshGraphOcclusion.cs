using System;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal readonly struct MeshOcclusionLists
    {
        internal readonly MeshView first, final, recovered;
        internal readonly RGBufferHandle deferred;
        internal readonly bool history;
        internal MeshOcclusionLists(MeshView first, MeshView final, MeshView recovered, RGBufferHandle deferred, bool history)
        { this.first = first; this.final = final; this.recovered = recovered; this.deferred = deferred; this.history = history; }
    }

    internal sealed partial class MeshGraphExtension
    {
        struct MeshOcclusionData
        {
            public MeshWorld world;
            public MeshView view;
            public Vector4[] frustumPlanes;
            public MeshCullInputs inputs;
            public int kernel, currentCount, previousCount, mipCount;
            public bool history, recovery;
            public Matrix4x4 projection;
            public int2 extent;
            public RGTextureHandle furthest;
            public RGBufferHandle visibility, deferred, final, recovered, transforms, generations, previousTransforms, previousGenerations;
        }

        static class MeshOcclusionBindings
        {
            internal static readonly int Deferred = Shader.PropertyToID("_DeferredVisibility"), Final = Shader.PropertyToID("_FinalVisibility"),
                Recovered = Shader.PropertyToID("_RecoveredVisibility"), Input = Shader.PropertyToID("_OcclusionDeferred"),
                Furthest = Shader.PropertyToID("_MeshCullFurthest"), Projection = Shader.PropertyToID("_MeshCullProjection"),
                Extent = Shader.PropertyToID("_MeshCullFullExtent"), Mips = Shader.PropertyToID("_MeshCullMipCount"),
                CurrentCount = Shader.PropertyToID("_MeshCullCurrentCount"), PreviousCount = Shader.PropertyToID("_MeshCullPreviousCount"),
                CurrentTransforms = Shader.PropertyToID("_MeshCullCurrentTransforms"), PreviousTransforms = Shader.PropertyToID("_MeshCullPreviousTransforms"),
                CurrentGenerations = Shader.PropertyToID("_MeshCullCurrentGenerations"), PreviousGenerations = Shader.PropertyToID("_MeshCullPreviousGenerations");
        }

        RGBufferHandle OcclusionList(RGBuilder builder, in MeshView view) => builder.ImportBuffer(world.GetGPUVisibilityBuffer(view));

        void RegisterOcclusionList(in MeshView view, RGBufferHandle buffer)
        {
            m_GPUCullVisibility.Add(new MeshVisibilityKey(view, true), buffer);
            if (view.visibilityPhase != EMeshVisibilityPhase.Final)
            {
                return;
            }
            m_OcclusionFinal[CoverageKey(view)] = new GpuVisibilityBinding { handle = buffer, buffer = world.GetGPUVisibilityBuffer(view) };
        }

        static readonly RGComputePassExecuteAction<MeshOcclusionData> s_ExecuteMeshOcclusion = ExecuteMeshOcclusion;

        internal MeshOcclusionLists RecordMeshOcclusionPrediction(RGBuilder builder, MeshView view, MeshMotionBinding motion,
            RGTextureHandle previousFurthest, Matrix4x4 previousProjection, int2 extent, ulong historyVersion)
        {
            view.lodProjection.ThrowIfInvalid();
            MeshDrawGPUBackend backend = world.gpuBackend;
            bool history = previousFurthest.IsValid() && motion.previous != null;
            view.visibilityHistory = historyVersion;
            var first = view;
            first.visibilityPhase = EMeshVisibilityPhase.Prediction;
            var deferredView = view;
            deferredView.visibilityPhase = EMeshVisibilityPhase.Deferred;
            var recovered = view;
            recovered.visibilityPhase = EMeshVisibilityPhase.Recovered;
            RGBufferHandle visible = OcclusionList(builder, first), final = OcclusionList(builder, view), deferred = OcclusionList(builder, deferredView);
            RegisterOcclusionList(first, visible);
            RegisterOcclusionList(view, final);
            motion.current.owner.Record(builder, motion.current, world.meshDrawShader, m_ScheduledVersions);
            using (var pass = builder.AddComputePass<MeshOcclusionData>(MeshDrawProfiling.culling))
            {
                pass.KeepImportedWritesLocal();
                ref var data = ref pass.GetPassData<MeshOcclusionData>();
                data = default;
                data.world = world;
                data.view = first;
                data.frustumPlanes = builder.GetRecordingArray<Vector4>(6);
                first.CopyFrustumPlanes(data.frustumPlanes);
                data.inputs = CullInputs(builder, pass);
                data.visibility = pass.WriteBuffer(visible);
                data.kernel = backend.FindKernel(history ? "CullFirstWithHistory" : "CullFirstNoHistory");
                data.history = history;
                data.final = pass.WriteBuffer(final);
                data.deferred = pass.WriteBuffer(deferred);
                if (history)
                {
                    data.furthest = pass.ReadTexture(previousFurthest);
                    data.projection = previousProjection;
                    data.extent = extent;
                    var descriptor = builder.GetTextureDescriptor(previousFurthest);
                    data.mipCount = PyramidMipBatch.MipCount(descriptor.width, descriptor.height);
                    data.currentCount = motion.current.count;
                    data.previousCount = motion.previous.count;
                    data.transforms = pass.ReadBuffer(builder.ImportBuffer(motion.current.matrices));
                    data.generations = pass.ReadBuffer(builder.ImportBuffer(motion.current.generations));
                    data.previousTransforms = pass.ReadBuffer(builder.ImportBuffer(motion.previous.matrices));
                    data.previousGenerations = pass.ReadBuffer(builder.ImportBuffer(motion.previous.generations));
                }
                pass.SetExecuteFunc(s_ExecuteMeshOcclusion);
            }
            return new MeshOcclusionLists(first, view, recovered, deferred, history);
        }

        internal void RecordMeshOcclusionRecovery(RGBuilder builder, in MeshOcclusionLists lists, RGTextureHandle furthest, Matrix4x4 projection, int2 extent)
        {
            RGBufferHandle recovered = OcclusionList(builder, lists.recovered);
            RegisterOcclusionList(lists.recovered, recovered);
            using (var pass = builder.AddComputePass<MeshOcclusionData>(MeshDrawProfiling.culling))
            {
                pass.KeepImportedWritesLocal();
                ref var data = ref pass.GetPassData<MeshOcclusionData>();
                data = default;
                data.world = world;
                data.view = lists.final;
                data.inputs = CullInputs(builder, pass, boundsOnly: true);
                data.recovery = true;
                data.kernel = world.gpuBackend.FindKernel("CullOcclusionRecovery");
                data.deferred = pass.ReadBuffer(lists.deferred);
                data.final = pass.WriteBuffer(m_GPUCullVisibility[new MeshVisibilityKey(lists.final, true)]);
                data.recovered = pass.WriteBuffer(recovered);
                data.furthest = pass.ReadTexture(furthest);
                data.projection = projection;
                data.extent = extent;
                var descriptor = builder.GetTextureDescriptor(furthest);
                data.mipCount = PyramidMipBatch.MipCount(descriptor.width, descriptor.height);
                pass.SetExecuteFunc(s_ExecuteMeshOcclusion);
            }
        }

        static void ExecuteMeshOcclusion(in MeshOcclusionData data, in RGComputeEncoder commands)
        {
            MeshDrawGPUBackend backend = data.world.gpuBackend;
            ComputeShader shader = backend.shader;
            int kernel = data.kernel;
            if (data.recovery)
            {
                backend.BindCullBounds(commands, kernel, data.view, data.inputs);
                commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Input, data.deferred);
                commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Recovered, data.recovered);
            }
            else
            {
                backend.BindCullInputs(commands, kernel, data.view, data.frustumPlanes, data.inputs, data.visibility);
                commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Deferred, data.deferred);
                if (data.history)
                {
                    commands.SetComputeIntParam(shader, MeshOcclusionBindings.CurrentCount, data.currentCount);
                    commands.SetComputeIntParam(shader, MeshOcclusionBindings.PreviousCount, data.previousCount);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.CurrentTransforms, data.transforms);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.CurrentGenerations, data.generations);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.PreviousTransforms, data.previousTransforms);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.PreviousGenerations, data.previousGenerations);
                }
            }
            commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Final, data.final);
            if (data.history || data.recovery)
            {
                commands.SetComputeTextureParam(shader, kernel, MeshOcclusionBindings.Furthest, data.furthest);
                commands.SetComputeMatrixParam(shader, MeshOcclusionBindings.Projection, data.projection);
                commands.SetComputeVectorParam(shader, MeshOcclusionBindings.Extent, new Vector4(data.extent.x, data.extent.y, 0, 0));
                commands.SetComputeIntParam(shader, MeshOcclusionBindings.Mips, data.mipCount);
            }
            string sample = data.recovery ? "DepthRecovery" : "DepthPrediction";
            commands.BeginSample(sample);
            try
            {
                commands.DispatchCompute(shader, kernel, math.max(1, (data.inputs.entityCount + 63) / 64), 1, 1);
            }
            finally
            {
                commands.EndSample(sample);
            }
            data.world.stats.gpuCullDispatches++;
        }
    }
}
