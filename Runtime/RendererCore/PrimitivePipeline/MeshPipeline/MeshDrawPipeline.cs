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
        private readonly MeshSceneResidency m_Residency;
        private readonly ResourcePool m_ResourcePool;
        private readonly ProfilingSampler m_DrawProfiler;
        private readonly MaterialPropertyBlock m_PropertyBlock;
        private readonly PassBinStore m_PassBins;
        private readonly uint m_PlatformFeatureKey;
        private readonly List<FBufferRef> m_FrameRentedBuffers = new List<FBufferRef>(8);
        private readonly List<FBufferRef> m_RetiredBuffers = new List<FBufferRef>(8);

        public PassBinStore PassBins => m_PassBins;

        public MeshDrawPipeline(MeshScene scene, MeshSceneResidency residency, ResourcePool resourcePool, PassRegistry registry)
        {
            m_Scene = scene;
            m_Residency = residency;
            m_ResourcePool = resourcePool;
            m_DrawProfiler = new ProfilingSampler("RenderLoop.DrawMeshPipeline");
            m_PropertyBlock = new MaterialPropertyBlock();
            m_PlatformFeatureKey = MeshDrawGPUBackend.SupportsIndirect ? 1u : 0u;
            m_PassBins = new PassBinStore(scene, registry, m_PlatformFeatureKey);
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

        public MeshDrawList Resolve(ref MeshDrawExtract extract)
        {
            if (!extract.isCreated)
            {
                return MeshDrawList.Invalid;
            }

            return new MeshDrawList
            {
                isValid = true,
                commands = extract.drawCommands.AsArray(),
                instanceIndices = extract.instanceIndices.AsArray(),
                instanceSlotIndices = extract.instanceSlotIndices.AsArray(),
                commandCount = extract.drawCommands.Length,
                instanceCount = extract.instanceIndices.Length
            };
        }

        /// <summary>
        /// CPU direct draw path only. GPU indirect submit is owned by RenderGraph via <see cref="SubmitGpu"/>.
        /// Temporary instance-index buffers are retired by <see cref="ReleaseFrameBuffers"/> and physically
        /// returned by <see cref="FlushRetiredBuffers"/> after <c>ScriptableRenderContext.Submit</c>.
        /// </summary>
        public void Submit(CommandBuffer cmdBuffer, in MeshDrawList drawList, int shaderPassIndex, string lightModeTag = null)
        {
            FBufferRef indexBuffer = PrepareCpuDirect(cmdBuffer, drawList);
            SubmitCpuDirect(cmdBuffer, drawList, shaderPassIndex, indexBuffer, lightModeTag);
        }

        internal FBufferRef PrepareCpuDirect(CommandBuffer cmdBuffer, in MeshDrawList drawList)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0
                || m_Residency.TransformBuffer.buffer == null)
            {
                return default;
            }

            int indexCount = math.max(1, drawList.instanceCount);
            FBufferRef indexBufferRef = m_ResourcePool.GetBuffer(new BufferDescriptor(math.max(indexCount, 16), Marshal.SizeOf<int>()));
            m_FrameRentedBuffers.Add(indexBufferRef);
            cmdBuffer.SetBufferData(indexBufferRef.buffer, drawList.instanceIndices, 0, 0, drawList.instanceCount);
            return indexBufferRef;
        }

        internal bool PrepareGpu(
            CommandBuffer cmdBuffer,
            in MeshDrawList drawList,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            MeshWorld world,
            in MeshView view)
        {
            if (!MeshDrawGPUBackend.SupportsIndirect || payload == null || staging == null)
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
                return false;
            }

            return MeshDrawGPUBackend.PrepareIndirect(cmdBuffer, drawList, m_Residency, m_DrawProfiler, payload, staging, world, view);
        }

        internal void SubmitGpu(
            CommandBuffer cmdBuffer,
            in MeshDrawList drawList,
            int shaderPassIndex,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            string lightModeTag = null, ComputeBuffer previousTransforms = null)
        {
            MeshDrawGPUBackend.DrawIndirect(
                cmdBuffer,
                drawList,
                shaderPassIndex,
                m_Residency,
                m_PropertyBlock,
                m_DrawProfiler,
                payload,
                staging,
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
            m_PassBins.Dispose();
        }

        internal void SubmitCpuDirect(CommandBuffer cmdBuffer, in MeshDrawList drawList, int shaderPassIndex, FBufferRef indexBufferRef, string lightModeTag = null, ComputeBuffer previousTransforms = null)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0
                || indexBufferRef.buffer == null
                || m_Residency.TransformBuffer.buffer == null)
            {
                return;
            }

            using (new ProfilingScope(cmdBuffer, m_DrawProfiler))
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
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.TransformBuffer, m_Residency.TransformBuffer.buffer);
                    if (previousTransforms != null) m_PropertyBlock.SetBuffer(InfinityShaderIDs.PreviousTransformBuffer, previousTransforms);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.RenderingLayerBuffer, m_Residency.RenderingLayerBuffer.buffer);
                    MeshBakedLighting.Bind(cmdBuffer, m_PropertyBlock, command.bakedTextureSet, m_Residency.BakedLightingBuffer.buffer);
                    cmdBuffer.DrawMeshInstancedProcedural(mesh, command.sectionIndex, material, passIndex, command.countOffset.x, m_PropertyBlock);
                    MeshBakedLighting.ClearKeywords(cmdBuffer);
                }
            }
        }
    }
}
