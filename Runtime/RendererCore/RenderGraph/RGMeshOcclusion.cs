using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.RenderGraph
{
    internal readonly struct MeshOcclusionLists
    {
        internal readonly MeshView first, final, recovered;
        internal readonly RGBufferRef deferred;
        internal readonly bool history;
        internal MeshOcclusionLists(MeshView first, MeshView final, MeshView recovered, RGBufferRef deferred, bool history)
        { this.first = first; this.final = final; this.recovered = recovered; this.deferred = deferred; this.history = history; }
    }

    public partial class RGBuilder
    {
        readonly HashSet<MeshTransformVersion> m_ScheduledMeshVersions = new HashSet<MeshTransformVersion>();
        struct MeshOcclusionData
        {
            public MeshGpuCullPassData inputs;
            public ComputeShader shader;
            public int kernel, currentCount, previousCount, mipCount;
            public bool history, recovery;
            public Matrix4x4 projection;
            public int2 extent;
            public RGTextureRef furthest;
            public RGBufferRef deferred, final, recovered, transforms, generations, previousTransforms, previousGenerations, instanceTransforms;
        }
        static class MeshOcclusionBindings
        {
            internal static readonly int Deferred = Shader.PropertyToID("_DeferredVisibility"), Final = Shader.PropertyToID("_FinalVisibility"),
                Recovered = Shader.PropertyToID("_RecoveredVisibility"), Input = Shader.PropertyToID("_OcclusionDeferred"),
                Furthest = Shader.PropertyToID("_MeshCullFurthest"), Projection = Shader.PropertyToID("_MeshCullProjection"),
                Extent = Shader.PropertyToID("_MeshCullFullExtent"), Mips = Shader.PropertyToID("_MeshCullMipCount"),
                CurrentCount = Shader.PropertyToID("_MeshCullCurrentCount"), PreviousCount = Shader.PropertyToID("_MeshCullPreviousCount"),
                CurrentTransforms = Shader.PropertyToID("_MeshCullCurrentTransforms"), PreviousTransforms = Shader.PropertyToID("_MeshCullPreviousTransforms"),
                CurrentGenerations = Shader.PropertyToID("_MeshCullCurrentGenerations"), PreviousGenerations = Shader.PropertyToID("_MeshCullPreviousGenerations"),
                InstanceTransforms = Shader.PropertyToID("_InstanceToTransform");
            static ComputeShader s_Shader;
            static int s_NoHistory, s_History, s_Recovery;
            internal static int Kernel(ComputeShader shader, bool history, bool recovery)
            {
                if (shader == null) throw new InvalidOperationException("Two-stage visibility requires its Mesh compute shader.");
                if (s_Shader != shader)
                {
                    if (shader == null || !shader.HasKernel("CullFirstNoHistory") || !shader.HasKernel("CullFirstWithHistory") || !shader.HasKernel("CullOcclusionRecovery"))
                        throw new InvalidOperationException("Two-stage visibility requires its three Mesh culling kernels.");
                    s_NoHistory = shader.FindKernel("CullFirstNoHistory"); s_History = shader.FindKernel("CullFirstWithHistory");
                    s_Recovery = shader.FindKernel("CullOcclusionRecovery"); s_Shader = shader;
                }
                return recovery ? s_Recovery : history ? s_History : s_NoHistory;
            }
        }
        RGBufferRef OcclusionList(in MeshView view, int count)
            => ImportBuffer(m_MeshWorld.GetGpuVisibilityBufferRef(view, count));
        void RegisterOcclusionList(in MeshView view, RGBufferRef buffer)
            => m_GpuCullVisibility.Add(MeshGpuVisibilityCache.MakeKey(view, true), buffer);
        MeshGpuCullPassData CullInputs(in MeshView view, RGComputePassRef pass)
        {
            var residency = m_MeshWorld.Residency;
            var data = new MeshGpuCullPassData { view = view, instanceCount = residency.Scene.InstanceHighWater, frustumPlanes = m_ObjectPool.GetTempArray<Vector4>(6) };
            view.CopyFrustumPlanes(data.frustumPlanes);
            data.boundsCenter = pass.ReadBuffer(ImportBuffer(residency.BoundsCenterBuffer));
            data.boundsExtent = pass.ReadBuffer(ImportBuffer(residency.BoundsExtentBuffer));
            data.instanceFlags = pass.ReadBuffer(ImportBuffer(residency.InstanceFlagsBuffer));
            data.instanceLayerMask = pass.ReadBuffer(ImportBuffer(residency.InstanceLayerMaskBuffer));
            data.instanceSceneCullingMask = pass.ReadBuffer(ImportBuffer(residency.InstanceSceneCullingMaskBuffer));
            data.instanceRenderingLayer = pass.ReadBuffer(ImportBuffer(residency.InstanceRenderingLayerBuffer));
            return data;
        }
        static readonly RGComputePassExecuteAction<MeshOcclusionData> s_ExecuteMeshOcclusion = ExecuteMeshOcclusion;

        internal MeshOcclusionLists RecordMeshOcclusionFirst(MeshView view, ComputeShader shader, MeshMotionBinding motion,
            RGTextureRef previousFurthest, Matrix4x4 previousProjection, int2 extent, ulong historyVersion)
        {
            bool history = previousFurthest.IsValid() && motion.previous != null;
            view.visibilityHistory = historyVersion;
            var first = view; first.visibilityPhase = EMeshVisibilityPhase.First;
            var deferredView = view; deferredView.visibilityPhase = EMeshVisibilityPhase.Deferred;
            var recovered = view; recovered.visibilityPhase = EMeshVisibilityPhase.Recovered;
            int count = m_MeshWorld.Residency.Scene.InstanceHighWater;
            var visible = OcclusionList(first, count); var final = OcclusionList(view, count); var deferred = OcclusionList(deferredView, count);
            RegisterOcclusionList(first, visible); RegisterOcclusionList(view, final);
            motion.current.owner.Record(this, motion.current, shader, m_ScheduledMeshVersions);
            using (var pass = AddComputePass<MeshOcclusionData>(MeshDrawProfiling.culling))
            {
                pass.KeepImportedWritesLocal();
                ref var data = ref pass.GetPassData<MeshOcclusionData>(); data = default;
                data.inputs = CullInputs(first, pass); data.inputs.visibility = pass.WriteBuffer(visible);
                data.shader = shader; data.kernel = MeshOcclusionBindings.Kernel(shader, history, false); data.history = history;
                data.final = pass.WriteBuffer(final); data.deferred = pass.WriteBuffer(deferred);
                if (history)
                {
                    data.furthest = pass.ReadTexture(previousFurthest); data.projection = previousProjection; data.extent = extent;
                    var descriptor = GetTextureDescriptor(previousFurthest);
                    data.mipCount = PyramidMipBatch.MipCount(descriptor.width, descriptor.height);
                    data.currentCount = motion.current.count; data.previousCount = motion.previous.count;
                    data.transforms = pass.ReadBuffer(ImportBuffer(motion.current.matrices)); data.generations = pass.ReadBuffer(ImportBuffer(motion.current.generations));
                    data.previousTransforms = pass.ReadBuffer(ImportBuffer(motion.previous.matrices)); data.previousGenerations = pass.ReadBuffer(ImportBuffer(motion.previous.generations));
                    data.instanceTransforms = pass.ReadBuffer(ImportBuffer(m_MeshWorld.Residency.InstanceTransformIndexBuffer));
                }
                pass.SetExecuteFunc(s_ExecuteMeshOcclusion);
            }
            return new MeshOcclusionLists(first, view, recovered, deferred, history);
        }
        internal void RecordMeshOcclusionRecovery(in MeshOcclusionLists lists, ComputeShader shader, RGTextureRef furthest,
            Matrix4x4 projection, int2 extent)
        {
            var recovered = OcclusionList(lists.recovered, m_MeshWorld.Residency.Scene.InstanceHighWater);
            RegisterOcclusionList(lists.recovered, recovered);
            using (var pass = AddComputePass<MeshOcclusionData>(MeshDrawProfiling.culling))
            {
                pass.KeepImportedWritesLocal();
                ref var data = ref pass.GetPassData<MeshOcclusionData>(); data = default;
                var residency = m_MeshWorld.Residency;
                data.inputs = new MeshGpuCullPassData { view = lists.final, instanceCount = residency.Scene.InstanceHighWater,
                    boundsCenter = pass.ReadBuffer(ImportBuffer(residency.BoundsCenterBuffer)),
                    boundsExtent = pass.ReadBuffer(ImportBuffer(residency.BoundsExtentBuffer)) };
                data.recovery = true;
                data.shader = shader; data.kernel = MeshOcclusionBindings.Kernel(shader, false, true);
                data.deferred = pass.ReadBuffer(lists.deferred);
                data.final = pass.WriteBuffer(m_GpuCullVisibility[MeshGpuVisibilityCache.MakeKey(lists.final, true)]);
                data.recovered = pass.WriteBuffer(recovered); data.furthest = pass.ReadTexture(furthest);
                data.projection = projection; data.extent = extent;
                var descriptor = GetTextureDescriptor(furthest); data.mipCount = PyramidMipBatch.MipCount(descriptor.width, descriptor.height);
                pass.SetExecuteFunc(s_ExecuteMeshOcclusion);
            }
        }
        static void ExecuteMeshOcclusion(in MeshOcclusionData data, in RGComputeEncoder commands, RGObjectPool objects)
        {
            var input = data.inputs; var shader = data.shader; int kernel = data.kernel;
            if (data.recovery)
            {
                MeshDrawGPUBackend.BindCullBounds(commands, shader, kernel, input.instanceCount, input.boundsCenter, input.boundsExtent);
                commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Input, data.deferred);
                commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Recovered, data.recovered);
            }
            else
            {
                MeshDrawGPUBackend.BindCullInputs(commands, shader, kernel, input.view, input.instanceCount, input.frustumPlanes,
                    input.boundsCenter, input.boundsExtent, input.instanceFlags, input.instanceLayerMask, input.instanceSceneCullingMask, input.instanceRenderingLayer, input.visibility);
                commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.Deferred, data.deferred);
                if (data.history)
                {
                    commands.SetComputeIntParam(shader, MeshOcclusionBindings.CurrentCount, data.currentCount);
                    commands.SetComputeIntParam(shader, MeshOcclusionBindings.PreviousCount, data.previousCount);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.CurrentTransforms, data.transforms);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.CurrentGenerations, data.generations);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.PreviousTransforms, data.previousTransforms);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.PreviousGenerations, data.previousGenerations);
                    commands.SetComputeBufferParam(shader, kernel, MeshOcclusionBindings.InstanceTransforms, data.instanceTransforms);
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
            commands.DispatchCompute(shader, kernel, (input.instanceCount + 63) / 64, 1, 1);
            MeshPipelineDiagnostics.GpuCullDispatchesPerFrame++;
        }
    }
}
