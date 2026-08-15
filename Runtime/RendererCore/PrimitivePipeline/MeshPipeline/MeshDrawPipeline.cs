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
    /// Mesh draw facade: Schedule / Resolve / Submit.
    /// Template cache is warmed on the managed side before Burst filter jobs.
    /// Transform/camera motion does not invalidate MeshPassDrawCache entries.
    /// Scene authority lives on the pipeline (<see cref="m_Scene"/>), not on the request.
    /// </summary>
    public class MeshDrawPipeline
    {
        private readonly MeshScene m_Scene;
        private readonly MeshSceneResidency m_Residency;
        private readonly ResourcePool m_ResourcePool;
        private readonly ProfilingSampler m_DrawProfiler;
        private readonly MaterialPropertyBlock m_PropertyBlock;
        private readonly MeshPassDrawCache m_PassDrawCache;
        private readonly uint m_PlatformFeatureKey;
        private readonly List<FBufferRef> m_FrameRentedBuffers = new List<FBufferRef>(8);
        private readonly List<FBufferRef> m_RetiredBuffers = new List<FBufferRef>(8);

        public MeshPassDrawCache PassDrawCache => m_PassDrawCache;

        public MeshDrawPipeline(MeshScene scene, MeshSceneResidency residency, ResourcePool resourcePool)
        {
            m_Scene = scene;
            m_Residency = residency;
            m_ResourcePool = resourcePool;
            m_DrawProfiler = new ProfilingSampler("RenderLoop.DrawMeshPipeline");
            m_PropertyBlock = new MaterialPropertyBlock();
            m_PassDrawCache = new MeshPassDrawCache();
            m_PlatformFeatureKey = MeshDrawGPUBackend.SupportsIndirect ? 1u : 0u;
        }

        public MeshDrawBuild Schedule(in MeshDrawRequest request, in MeshViewCullingResult culling, JobHandle dependency = default)
        {
            if (m_Scene == null || !culling.isValid || !culling.instanceVisibility.IsCreated)
            {
                MeshPipelineDiagnostics.CulledPassSkippedBuilds++;
                return default;
            }

            int drawHighWater = math.max(1, m_Scene.DrawHighWater);
            int visibleCapacity = drawHighWater;

            // Warm MeshPassDraw templates on managed side (Burst-safe ids for filter).
            NativeArray<MeshPassDrawId> passDrawIds = new NativeArray<MeshPassDrawId>(drawHighWater, Allocator.TempJob);
            MeshPipelineDiagnostics.TempAllocCount++;
            WarmPassDrawCache(request, passDrawIds);

            MeshFilterProgram filter = request.filter;
            if (request.renderingLayerMask != 0)
            {
                filter.renderingLayerMask = request.renderingLayerMask;
            }

            var build = new MeshDrawBuild
            {
                isCreated = true,
                visibleDraws = new NativeList<VisibleMeshDraw>(visibleCapacity, Allocator.TempJob),
                drawCommands = new NativeList<MeshDrawCommand>(visibleCapacity, Allocator.TempJob),
                instanceIndices = new NativeArray<int>(visibleCapacity, Allocator.TempJob),
                instanceSlotIndices = new NativeArray<int>(visibleCapacity, Allocator.TempJob),
                passDrawIds = passDrawIds
            };
            MeshPipelineDiagnostics.TempAllocCount += 4;

            var filterJob = new MeshPassFilterJob
            {
                instanceVisibility = culling.instanceVisibility,
                instances = m_Scene.GetInstances(),
                instanceGenerations = m_Scene.GetInstanceGenerations(),
                transformGenerations = m_Scene.GetTransformGenerations(),
                draws = m_Scene.GetDraws(),
                passDrawIds = passDrawIds,
                filter = filter,
                sort = request.sort,
                viewPosition = request.viewPosition,
                shaderPassIndex = request.shaderPassIndex,
                drawHighWater = m_Scene.DrawHighWater,
                visibleDraws = build.visibleDraws
            };
            JobHandle filterHandle = filterJob.Schedule(dependency);

            var sortJob = new MeshPassSortJob
            {
                visibleDraws = build.visibleDraws
            };
            JobHandle sortHandle = sortJob.Schedule(filterHandle);

            var buildJob = new MeshPassBuildJob
            {
                visibleDraws = build.visibleDraws,
                draws = m_Scene.GetDraws(),
                drawCommands = build.drawCommands,
                instanceIndices = build.instanceIndices,
                instanceSlotIndices = build.instanceSlotIndices
            };
            build.dependency = buildJob.Schedule(sortHandle);
            return build;
        }

        public MeshDrawList Resolve(ref MeshDrawBuild build)
        {
            if (!build.isCreated)
            {
                return MeshDrawList.Invalid;
            }

            build.dependency.Complete();

            return new MeshDrawList
            {
                isValid = true,
                commands = build.drawCommands.AsArray(),
                instanceIndices = build.instanceIndices,
                instanceSlotIndices = build.instanceSlotIndices,
                commandCount = build.drawCommands.Length,
                instanceCount = build.visibleDraws.Length
            };
        }

        /// <summary>
        /// CPU direct draw path only. GPU indirect submit is owned by RenderGraph via <see cref="SubmitGpu"/>.
        /// Temporary instance-index buffers are retired by <see cref="ReleaseFrameBuffers"/> and physically
        /// returned by <see cref="FlushRetiredBuffers"/> after <c>ScriptableRenderContext.Submit</c>.
        /// </summary>
        public void Submit(CommandBuffer cmdBuffer, in MeshDrawList drawList, int shaderPassIndex)
        {
            FBufferRef indexBuffer = PrepareCpuDirect(cmdBuffer, drawList);
            SubmitCpuDirect(cmdBuffer, drawList, shaderPassIndex, indexBuffer);
        }

        internal FBufferRef PrepareCpuDirect(CommandBuffer cmdBuffer, in MeshDrawList drawList)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0
                || m_Residency.TransformBuffer.buffer == null
                || m_Residency.PreviousTransformBuffer.buffer == null)
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
            MeshDrawGpuStaging staging)
        {
            if (!MeshDrawGPUBackend.SupportsIndirect || payload == null || staging == null)
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
                return false;
            }

            return MeshDrawGPUBackend.PrepareIndirect(cmdBuffer, drawList, m_Residency, m_DrawProfiler, payload, staging);
        }

        internal void SubmitGpu(
            CommandBuffer cmdBuffer,
            in MeshDrawList drawList,
            int shaderPassIndex,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging)
        {
            MeshDrawGPUBackend.DrawIndirect(
                cmdBuffer,
                drawList,
                shaderPassIndex,
                m_Residency,
                m_PropertyBlock,
                m_DrawProfiler,
                payload,
                staging);
        }

        internal int GetBoundsCullCount()
        {
            return math.max(1, m_Scene != null ? m_Scene.InstanceHighWater : 1);
        }

        public void Release(ref MeshDrawBuild build)
        {
            build.Dispose();
            build = default;
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
            m_PassDrawCache.Dispose();
        }

        internal void SubmitCpuDirect(CommandBuffer cmdBuffer, in MeshDrawList drawList, int shaderPassIndex, FBufferRef indexBufferRef)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0
                || indexBufferRef.buffer == null
                || m_Residency.TransformBuffer.buffer == null
                || m_Residency.PreviousTransformBuffer.buffer == null)
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

                    m_PropertyBlock.Clear();
                    // Shader bindings: transformBuffer / previousTransformBuffer are MeshScene TransformTable matrices.
                    m_PropertyBlock.SetInt(InfinityShaderIDs.InstanceIndexOffset, command.countOffset.y);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.InstanceIndexBuffer, indexBufferRef.buffer);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.TransformBuffer, m_Residency.TransformBuffer.buffer);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.PreviousTransformBuffer, m_Residency.PreviousTransformBuffer.buffer);
                    cmdBuffer.DrawMeshInstancedProcedural(mesh, command.sectionIndex, material, shaderPassIndex, command.countOffset.x, m_PropertyBlock);
                }
            }
        }

        private void WarmPassDrawCache(in MeshDrawRequest request, NativeArray<MeshPassDrawId> passDrawIds)
        {
            NativeArray<MeshDrawRecord> draws = m_Scene.GetDraws();
            NativeArray<MaterialDataRecord> materials = m_Scene.GetMaterials();
            NativeArray<MeshSectionRecord> sections = m_Scene.GetSections();
            int highWater = m_Scene.DrawHighWater;

            for (int drawIndex = 0; drawIndex < highWater; ++drawIndex)
            {
                if (!m_Scene.IsDrawSlotLive(drawIndex))
                {
                    passDrawIds[drawIndex] = MeshPassDrawId.Invalid;
                    continue;
                }

                MeshDrawRecord draw = draws[drawIndex];
                uint materialRevision = 0;
                if (draw.material.IsValid && draw.material.Index < (uint)materials.Length)
                {
                    MaterialDataRecord material = materials[(int)draw.material.Index];
                    if (material.materialUnityId >= 0)
                    {
                        materialRevision = material.revision;
                    }
                }

                uint sectionRevision = 0;
                if (draw.section.IsValid && draw.section.Index < (uint)sections.Length)
                {
                    MeshSectionRecord section = sections[(int)draw.section.Index];
                    if (section.meshUnityId >= 0)
                    {
                        // section.revision already reflects geometryRevision / geometrySource changes.
                        sectionRevision = section.revision;
                    }
                }

                passDrawIds[drawIndex] = m_PassDrawCache.GetOrCreate(
                    request.shaderPassIndex,
                    draw.meshUnityId,
                    draw.sectionIndex,
                    draw.materialUnityId,
                    materialRevision,
                    sectionRevision: sectionRevision,
                    platformFeatureKey: m_PlatformFeatureKey,
                    staticFlags: draw.staticFlags);
            }
        }
    }
}
