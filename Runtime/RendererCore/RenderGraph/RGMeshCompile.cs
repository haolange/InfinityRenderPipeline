using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.RenderGraph
{
    internal struct MeshGpuCullPassData
    {
        public MeshWorld world;
        public MeshView view;
        public int instanceCount;
        public Vector4[] frustumPlanes;
        public RGDrawListContext drawLists;
        public RGBufferRef visibility;
        public RGBufferRef boundsCenter;
        public RGBufferRef boundsExtent;
        public RGBufferRef instanceFlags;
        public RGBufferRef instanceLayerMask;
        public RGBufferRef instanceSceneCullingMask;
        public RGBufferRef instanceRenderingLayer;
    }

    sealed class MeshGraphCompileExtension : IRGCompileExtension
    {
        public void Contribute(RGBuilder builder) => builder.ContributeMeshPreparation();
    }

    public partial class RGBuilder
    {
        static readonly IRGCompileExtension s_MeshExtension = new MeshGraphCompileExtension();
        struct MeshDrawPreparePassData { public RGDrawListContext lists; public int index; }

        public void SetMeshWorld(MeshWorld meshWorld)
        {
            m_MeshWorld = meshWorld;
            m_CompileExtension = meshWorld != null ? s_MeshExtension : null;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public RGDrawListRef CreateDrawList(in MeshView view, MeshPassId passId)
        {
            if (m_MeshWorld == null)
                throw new InvalidOperationException("CreateDrawList requires MeshWorld.");

            MeshPassContext pass = m_MeshWorld.BindPass(view, passId);
            RGDrawListRef draws = m_DrawListRecords.Declare(
                m_MeshWorld.Processor, pass, MeshVisibilityHandle.Invalid, m_MeshWorld.VisibilityShare,
                view, passId, m_MeshWorld);
            RetainDrawListInputs(draws);
            EnsureMeshGpuCull(view, draws);
            return draws;
        }

        void RetainDrawListInputs(RGDrawListRef draws)
        {
            var record = m_DrawListRecords.GetRecordCopy(draws.index);
            var residency = record.residency;
            if (residency.TransformBuffer.buffer != null) ImportBuffer(residency.TransformBuffer);
            if (residency.RenderingLayerBuffer.buffer != null) ImportBuffer(residency.RenderingLayerBuffer);
            if (residency.InstanceTransformIndexBuffer.buffer != null) ImportBuffer(residency.InstanceTransformIndexBuffer);
            if (residency.version != null) ImportBuffer(residency.version.generations);
            var previous = record.pass.previousTransforms.previous;
            if (previous != null) { ImportBuffer(previous.matrices); ImportBuffer(previous.generations); }
        }

        public void EnsureMeshGpuCull(in MeshView view)
        {
            if (m_MeshWorld == null)
                throw new InvalidOperationException("EnsureMeshGpuCull requires MeshWorld.");
            EnsureMeshGpuCull(view, RGDrawListRef.Invalid);
        }

        void EnsureMeshGpuCull(in MeshView view, in RGDrawListRef draws)
        {
            if (MeshDrawGPUBackend.SelectPolicy(m_MeshWorld.SelectPolicy()) != EMeshBackendPolicy.GpuIndirect)
                return;

            MeshGpuVisibilityKey key = MeshGpuVisibilityCache.MakeKey(view, true);
            if (m_GpuCullVisibility.TryGetValue(key, out RGBufferRef existing))
            {
                if (m_DrawListRecords.IsLiveRef(draws))
                    m_DrawListRecords.BindGpuVisibility(draws.index, existing);
                return;
            }

            GpuScene residency = m_MeshWorld.Residency;
            if (residency == null
                || residency.BoundsCenterBuffer.buffer == null
                || residency.BoundsExtentBuffer.buffer == null
                || residency.InstanceFlagsBuffer.buffer == null
                || residency.InstanceLayerMaskBuffer.buffer == null
                || residency.InstanceSceneCullingMaskBuffer.buffer == null
                || residency.InstanceRenderingLayerBuffer.buffer == null)
                throw new InvalidOperationException("MeshGpuCull requires uploaded GpuScene instance buffers.");
            if (!MeshDrawGPUBackend.TryGetCullKernel(out _, out _))
                throw new InvalidOperationException("MeshGpuCull requires CullFrustum.");

            int instanceCount = residency.Scene != null ? residency.Scene.InstanceHighWater : 0;
            FBufferRef visibility = m_MeshWorld.GetGpuVisibilityBufferRef(view, math.max(1, instanceCount));
            RGBufferRef visibilityRef = ImportBuffer(visibility);
            RGBufferRef boundsCenter = ImportBuffer(residency.BoundsCenterBuffer);
            RGBufferRef boundsExtent = ImportBuffer(residency.BoundsExtentBuffer);
            RGBufferRef instanceFlags = ImportBuffer(residency.InstanceFlagsBuffer);
            RGBufferRef instanceLayerMask = ImportBuffer(residency.InstanceLayerMaskBuffer);
            RGBufferRef instanceSceneCullingMask = ImportBuffer(residency.InstanceSceneCullingMaskBuffer);
            RGBufferRef instanceRenderingLayer = ImportBuffer(residency.InstanceRenderingLayerBuffer);
            Vector4[] frustumPlanes = GetRecordingArray<Vector4>(6);
            view.CopyFrustumPlanes(frustumPlanes);

            using (RGComputePassRef passRef = AddComputePass<MeshGpuCullPassData>(MeshDrawProfiling.culling))
            {
                ref MeshGpuCullPassData passData = ref passRef.GetPassData<MeshGpuCullPassData>();
                passData.world = m_MeshWorld;
                passData.view = view;
                passData.instanceCount = instanceCount;
                passData.frustumPlanes = frustumPlanes;
                passData.drawLists = m_DrawListRecords;
                passData.visibility = passRef.WriteBuffer(visibilityRef);
                passData.boundsCenter = passRef.ReadBuffer(boundsCenter);
                passData.boundsExtent = passRef.ReadBuffer(boundsExtent);
                passData.instanceFlags = passRef.ReadBuffer(instanceFlags);
                passData.instanceLayerMask = passRef.ReadBuffer(instanceLayerMask);
                passData.instanceSceneCullingMask = passRef.ReadBuffer(instanceSceneCullingMask);
                passData.instanceRenderingLayer = passRef.ReadBuffer(instanceRenderingLayer);
                passRef.KeepImportedWritesLocal();
                passRef.SetExecuteFunc((in MeshGpuCullPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    MeshDrawGPUBackend.DispatchCullFrustum(
                        cmdEncoder, passData.view, passData.instanceCount, passData.frustumPlanes,
                        passData.boundsCenter, passData.boundsExtent, passData.instanceFlags, passData.instanceLayerMask,
                        passData.instanceSceneCullingMask, passData.instanceRenderingLayer, passData.visibility);
                    passData.world?.MarkGpuCulled(passData.view);
                });
            }

            m_GpuCullVisibility[key] = visibilityRef;
            if (m_DrawListRecords.IsLiveRef(draws))
                m_DrawListRecords.BindGpuVisibility(draws.index, visibilityRef);
        }

        internal void ContributeMeshPreparation()
        {
            CompileMeshDrawLists();
            InsertDrawListPreparation();
        }

        void CompileMeshDrawLists()
        {
            m_DrawListRecords.ClearConsumers();
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled) continue;
                List<RGDrawListRef> used = passInfo.pass.usedDrawLists;
                for (int i = 0; i < used.Count; ++i)
                {
                    RGDrawListRef draws = used[i];
                    if (m_DrawListRecords.IsLiveRef(draws))
                        m_DrawListRecords.MarkLiveConsumer(draws.index, passIndex);
                }
            }
            m_DrawListRecords.PrepareLive(m_MeshWorld);
        }

        RGBufferRef ImportMeshBuffer(ComputeBuffer buffer, string name, ComputeBufferType type = ComputeBufferType.Structured)
            => ImportBuffer(new FBufferRef(-1, new BufferDescriptor(buffer.count, buffer.stride, type) { name = name }, buffer));

        // Upload and compaction are appended here and then moved to the earliest pass whose inputs exist,
        // so they do not sit between adjacent raster passes.
        void InsertDrawListPreparation()
        {
            int originalCount = m_PassList.Count;
            var prepared = m_PreparedDrawLists;
            prepared.Clear();
            var transformVersions = m_ScheduledMeshVersions;
            for (int passIndex = 0; passIndex < originalCount; ++passIndex)
            {
                IRGPass consumer = m_PassList[passIndex];
                if (m_PassCompileInfos[passIndex].culled || consumer.usedDrawLists.Count == 0) continue;
                ProfilingSampler group = consumer.customSampler;
                if (group != null) BeginProfileRange(group);
                foreach (RGDrawListRef draws in consumer.usedDrawLists)
                {
                    var record = m_DrawListRecords.GetRecordCopy(draws.index);
                    var residency = record.residency;
                    if (!residency.isValid) continue;
                    residency.version.owner.Record(this, residency.version, MeshDrawGPUBackend.shader, transformVersions);
                    consumer.AddResourceRead(ImportBuffer(residency.TransformBuffer).handle);
                    consumer.AddResourceRead(ImportBuffer(residency.RenderingLayerBuffer).handle);
                    consumer.AddResourceRead(ImportBuffer(residency.version.generations).handle);
                    var previous = record.pass.previousTransforms.previous;
                    if (previous == null) continue;
                    consumer.AddResourceRead(ImportBuffer(previous.matrices).handle);
                    consumer.AddResourceRead(ImportBuffer(previous.generations).handle);
                }
                foreach (RGDrawListRef draws in consumer.usedDrawLists)
                {
                    if (!prepared.TryGetValue(draws.index, out RGBufferRef[] outputs))
                    {
                        var record = m_DrawListRecords.PrepareGraphRecord(draws.index);
                        if (record.pipeline == null) continue;
                        bool gpu = record.gpuPayload != null;
                        RGBufferRef metadata = gpu ? ImportMeshBuffer(record.gpuPayload.commandMeta, "MeshDrawMetadata")
                            : record.cpuIndexBuffer.buffer != null ? ImportBuffer(record.cpuIndexBuffer) : default;
                        if (!metadata.IsValid())
                        {
                            prepared.Add(draws.index, Array.Empty<RGBufferRef>());
                            continue;
                        }
                        using (RGTransferPassRef upload = AddTransferPass<MeshDrawPreparePassData>(MeshDrawProfiling.upload))
                        {
                            upload.WriteBuffer(metadata);
                            ref MeshDrawPreparePassData data = ref upload.GetPassData<MeshDrawPreparePassData>();
                            data.lists = m_DrawListRecords;
                            data.index = draws.index;
                            upload.SetExecuteFunc((in MeshDrawPreparePassData data, in RGTransferEncoder commands) => data.lists.Upload(data.index, commands.m_CommandBuffer));
                        }
                        if (gpu)
                        {
                            RGBufferRef indices = ImportMeshBuffer(record.gpuPayload.compactedIndices, "MeshDrawCompactedIndices");
                            RGBufferRef counts = ImportMeshBuffer(record.gpuPayload.visibleCounts, "MeshDrawVisibleCounts");
                            RGBufferRef args = ImportMeshBuffer(record.gpuPayload.argsBuffer, "MeshDrawIndirectArgs", ComputeBufferType.IndirectArguments);
                            RGBufferRef candidates = ImportMeshBuffer(record.gpuStaging.candidateTable, "MeshDrawCandidates");
                            RGBufferRef transforms = ImportBuffer(record.residency.InstanceTransformIndexBuffer);
                            using (RGComputePassRef compact = AddComputePass<MeshDrawPreparePassData>(MeshDrawProfiling.compaction))
                            {
                                compact.ReadBuffer(metadata);
                                compact.ReadBuffer(candidates);
                                compact.ReadBuffer(transforms);
                                compact.ReadBuffer(record.gpuVisibilityBuffer);
                                compact.WriteBuffer(indices);
                                compact.WriteBuffer(counts);
                                compact.WriteBuffer(args);
                                ref MeshDrawPreparePassData data = ref compact.GetPassData<MeshDrawPreparePassData>();
                                data.lists = m_DrawListRecords;
                                data.index = draws.index;
                                compact.SetExecuteFunc((in MeshDrawPreparePassData data, in RGComputeEncoder commands) => data.lists.Compact(data.index, commands.m_CommandBuffer));
                            }
                            outputs = GetRecordingArray<RGBufferRef>(2);
                            outputs[0] = indices;
                            outputs[1] = args;
                        }
                        else
                        {
                            outputs = GetRecordingArray<RGBufferRef>(1);
                            outputs[0] = metadata;
                        }
                        prepared.Add(draws.index, outputs);
                    }
                    foreach (RGBufferRef output in outputs) consumer.AddResourceRead(output.handle);
                }
                if (group != null) EndProfileRange(group);
            }
            prepared.Clear();
        }
    }
}
