using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Mesh draw facade: Extract / Resolve / Submit against PassBin + CommandTable.
    /// </summary>
    public class MeshDrawPipeline
    {
        private readonly MeshScene m_Scene;
        private readonly GpuScene m_Residency;
        private readonly ResourcePool m_ResourcePool;
        private readonly MaterialPropertyBlock m_PropertyBlock;
        private readonly PassBinStore m_PassBins;
        private readonly MeshCandidateTableStore m_CandidateTables;
        private readonly uint m_PlatformFeatureKey;
        private readonly List<FBufferRef> m_FrameRentedBuffers = new List<FBufferRef>(8);
        private readonly List<FBufferRef> m_RetiredBuffers = new List<FBufferRef>(8);

        internal MeshGpuSceneSnapshot CaptureScene() => new MeshGpuSceneSnapshot(m_Residency);

        public PassBinStore PassBins => m_PassBins;
        internal MeshCandidateTableStore CandidateTables => m_CandidateTables;

        public MeshDrawPipeline(MeshScene scene, GpuScene residency, ResourcePool resourcePool, PassRegistry registry)
        {
            m_Scene = scene;
            m_Residency = residency;
            m_ResourcePool = resourcePool;
            m_PropertyBlock = new MaterialPropertyBlock();
            m_PlatformFeatureKey = MeshDrawGPUBackend.SupportsIndirect ? 1u : 0u;
            m_PassBins = new PassBinStore(scene, registry, m_PlatformFeatureKey);
            m_CandidateTables = new MeshCandidateTableStore(scene, m_PassBins, resourcePool);
        }

        public MeshDrawExtract Extract(MeshPassId passId, in MeshViewCullingResult culling)
        {
            if (m_Scene == null || !culling.isValid || !culling.instanceVisibility.IsCreated)
            {
                MeshPipelineDiagnostics.CulledPassSkippedBuilds++;
                return default;
            }

            m_PassBins.EnsureRebuilt(passId);
            NativeArray<MeshPassCommand> commands = m_PassBins.GetCommands(passId);
            NativeArray<int> members = m_PassBins.GetMemberDrawIndices(passId);
            int visibleCapacity = math.max(1, members.Length);
            var extract = new MeshDrawExtract
            {
                isCreated = true,
                drawCommands = new NativeList<MeshDrawCommand>(math.max(1, commands.Length), Allocator.TempJob),
                instanceIndices = new NativeList<int>(visibleCapacity, Allocator.TempJob),
                instanceSlotIndices = new NativeList<int>(visibleCapacity, Allocator.TempJob)
            };
            MeshPipelineDiagnostics.TempAllocCount += 3;

            new MeshPassExtractJob
            {
                commands = commands,
                memberDrawIndices = members,
                draws = m_Scene.GetDraws(),
                instances = m_Scene.GetInstances(),
                instanceGenerations = m_Scene.GetInstanceGenerations(),
                transformGenerations = m_Scene.GetTransformGenerations(),
                instanceVisibility = culling.instanceVisibility,
                drawCommands = extract.drawCommands,
                instanceIndices = extract.instanceIndices,
                instanceSlotIndices = extract.instanceSlotIndices
            }.Run();

            return extract;
        }

        public PassView Resolve(ref MeshDrawExtract extract)
        {
            if (!extract.isCreated)
            {
                return PassView.Invalid;
            }

            return new PassView
            {
                isValid = true,
                commands = extract.drawCommands.AsArray(),
                instanceIndices = extract.instanceIndices.AsArray(),
                instanceSlotIndices = extract.instanceSlotIndices.AsArray(),
                commandCount = extract.drawCommands.Length,
                instanceCount = extract.instanceIndices.Length
            };
        }

        internal ResourcePool resourcePool => m_ResourcePool;

        // Allocation happens while the graph compiles; the explicit Upload producer fills the buffer.
        internal FBufferRef AllocateCpuDirect(in PassView drawList)
        {
            if (!drawList.isValid || drawList.commandCount == 0) return default;
            int indexCount = math.max(1, drawList.instanceCount);
            var buffer = m_ResourcePool.GetBuffer(new BufferDescriptor(math.max(indexCount, 16), Marshal.SizeOf<int>()));
            m_FrameRentedBuffers.Add(buffer); return buffer;
        }
        internal void UploadCpuDirect(CommandBuffer commands, in PassView drawList, in FBufferRef buffer)
        {
            if (buffer.buffer != null && drawList.instanceCount > 0)
                commands.SetBufferData(buffer.buffer, drawList.instanceIndices, 0, 0, drawList.instanceCount);
        }

        internal bool PrepareGpu(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            MeshWorld world,
            in MeshGpuSceneSnapshot residency,
            in MeshView view,
            MeshPassId passId)
        {
            if (!MeshDrawGPUBackend.SupportsIndirect || payload == null || staging == null)
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
                return false;
            }

            return MeshDrawGPUBackend.PrepareIndirect(cmdBuffer, drawList, residency, payload, staging, world, view, passId);
        }

        internal void SubmitGpu(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            int shaderPassIndex,
            in MeshGpuSceneSnapshot residency,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            EMeshViewKind viewKind,
            string lightModeTag = null, MeshMotionBinding previousTransforms = default)
        {
            MeshDrawGPUBackend.DrawIndirect(
                cmdBuffer,
                drawList,
                shaderPassIndex,
                residency,
                m_PropertyBlock,
                payload,
                staging,
                viewKind,
                lightModeTag, previousTransforms);
        }

        internal int GetBoundsCullCount()
        {
            return math.max(1, m_Scene != null ? m_Scene.InstanceHighWater : 1);
        }

        public void Release(ref MeshDrawExtract extract)
        {
            extract.Dispose();
            extract = default;
        }

        /// <summary>
        /// Logical cleanup: move this frame's rented CPU buffers into the retirement queue.
        /// Does not return buffers to the resource pool (GPU may still reference them until Submit).
        /// Idempotent; safe to call per DrawList record.
        /// </summary>
        public void ReleaseFrameBuffers()
        {
            for (int i = 0; i < m_FrameRentedBuffers.Count; ++i)
            {
                m_RetiredBuffers.Add(m_FrameRentedBuffers[i]);
            }

            m_FrameRentedBuffers.Clear();
        }

        /// <summary>
        /// Physical drain: return retired CPU buffers to the resource pool after frame Submit.
        /// </summary>
        public void FlushRetiredBuffers()
        {
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                m_ResourcePool.ReleaseBuffer(m_RetiredBuffers[i]);
            }

            m_RetiredBuffers.Clear();
        }

        public void Dispose()
        {
            ReleaseFrameBuffers();
            FlushRetiredBuffers();
            m_CandidateTables.Dispose();
            m_PassBins.Dispose();
        }

        internal void SubmitCpuDirect(CommandBuffer cmdBuffer, in PassView drawList, int shaderPassIndex, in MeshGpuSceneSnapshot residency, FBufferRef indexBufferRef, EMeshViewKind viewKind, string lightModeTag = null, MeshMotionBinding previousTransforms = default)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0
                || indexBufferRef.buffer == null
                || residency.TransformBuffer.buffer == null)
            {
                return;
            }

            string marker = MeshDrawProfiling.Draw(viewKind, false);
            cmdBuffer.BeginSample(marker);
            try
            {
                for (int i = 0; i < drawList.commandCount; ++i)
                {
                    MeshDrawCommand command = drawList.commands[i];
                    Mesh mesh = UnityEntityId.ToObject<Mesh>(command.meshUnityId);
                    Material material = UnityEntityId.ToObject<Material>(command.materialUnityId);
                    if (mesh == null || material == null || command.countOffset.x <= 0)
                    {
                        continue;
                    }

                    int passIndex = MeshPassShaderUtility.ResolvePassIndex(material, lightModeTag, shaderPassIndex);
                    if (passIndex < 0)
                    {
                        continue;
                    }

                    m_PropertyBlock.Clear();
                    // Current poses come from the scene; previous poses come from this view's accepted history.
                    m_PropertyBlock.SetInt(InfinityShaderIDs.InstanceIndexOffset, command.countOffset.y);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.InstanceIndexBuffer, indexBufferRef.buffer);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.TransformBuffer, residency.TransformBuffer.buffer);
                    (previousTransforms.isValid ? previousTransforms : new MeshMotionBinding(residency.version, null)).Bind(m_PropertyBlock);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.RenderingLayerBuffer, residency.RenderingLayerBuffer.buffer);
                    cmdBuffer.DrawMeshInstancedProcedural(mesh, command.sectionIndex, material, passIndex, command.countOffset.x, m_PropertyBlock);
                }
            }
            finally
            {
                cmdBuffer.EndSample(marker);
            }
        }
    }
}
