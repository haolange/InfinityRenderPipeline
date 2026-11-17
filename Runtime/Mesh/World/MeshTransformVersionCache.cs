using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Core;

namespace InfinityTech.Rendering.MeshPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    // GPU-RECORD: MeshTransformPatch Shaders/Mesh/Compute_MeshDrawPipeline.compute
    internal struct MeshTransformPatch
    {
        public float4x4 objectToWorld;
        public uint generation, index;
        public uint2 reserved;
    }

    internal sealed class MeshTransformVersion : IRGPassQueueObserver
    {
        internal MeshTransformVersionCache owner;
        internal MeshTransformVersion source;
        internal BufferHandle matricesHandle, generationsHandle, patchesHandle;
        internal MeshTransformPatch[] changes;
        internal int changeCount;
        internal int worldId, count, references = 1;
        internal ulong revision;
        internal bool queued, submitted;
        public void OnQueued() => queued = true;
        public void OnQueueFailed() { }
        internal void Retain() => references++;
        internal void Release() => owner.Release(this);
    }

    internal readonly struct MeshMotionBinding
    {
        internal readonly MeshTransformVersion current, previous;
        internal bool isValid => current != null;
        internal MeshMotionBinding(MeshTransformVersion current, MeshTransformVersion accepted)
        {
            this.current = current;
            previous = accepted != null && accepted.worldId == current.worldId ? accepted : null;
        }
        internal void Bind(MaterialPropertyBlock properties)
        {
            properties.SetInt(s_Valid, previous != null ? 1 : 0);
            if (current == null)
            {
                return;
            }
            var source = previous ?? current;
            properties.SetInt(s_Count, previous != null ? previous.count : 0);
            properties.SetBuffer(s_CurrentGeneration, current.generationsHandle.buffer);
            properties.SetBuffer(s_PreviousGeneration, source.generationsHandle.buffer);
            properties.SetBuffer(s_PreviousMatrices, source.matricesHandle.buffer);
        }
        static readonly int s_Valid = MeshShaderIDs.PreviousEntityValid;
        static readonly int s_Count = MeshShaderIDs.PreviousEntityCount;
        static readonly int s_CurrentGeneration = MeshShaderIDs.EntityGenerations;
        static readonly int s_PreviousGeneration = MeshShaderIDs.PreviousEntityGenerations;
        static readonly int s_PreviousMatrices = MeshShaderIDs.PreviousEntityTransforms;
    }

    // The world owns immutable matrix/generation tables per entity slot; views retain version references only.
    internal sealed class MeshTransformVersionCache : IDisposable
    {
        readonly ResourcePool m_Pool;
        readonly Stack<MeshTransformVersion> m_FreeVersions = new Stack<MeshTransformVersion>();
        readonly HashSet<MeshTransformVersion> m_Versions = new HashSet<MeshTransformVersion>();
        readonly List<MeshTransformVersion> m_Retired = new List<MeshTransformVersion>();
        MeshTransformVersion m_Current;
        bool m_Disposed;
        internal MeshTransformVersion current => m_Current;
        internal int versionCount => m_Versions.Count;
        internal long uploadedBytes
        {
            get;
            private set;
        }
        internal int productionCount
        {
            get;
            private set;
        }
        static readonly int ID_TransformVersionCount = Shader.PropertyToID("_TransformVersionCount");
        static readonly int ID_TransformSourceCount = Shader.PropertyToID("_TransformSourceCount");
        static readonly int ID_TransformSourceMatrices = Shader.PropertyToID("_TransformSourceMatrices");
        static readonly int ID_TransformSourceGenerations = Shader.PropertyToID("_TransformSourceGenerations");
        static readonly int ID_TransformVersionMatrices = Shader.PropertyToID("_TransformVersionMatrices");
        static readonly int ID_TransformVersionGenerations = Shader.PropertyToID("_TransformVersionGenerations");
        static readonly int ID_TransformPatchCount = Shader.PropertyToID("_TransformPatchCount");
        static readonly int ID_TransformChanges = Shader.PropertyToID("_TransformChanges");
        readonly ComputeKernel m_InitializeKernel, m_CopyKernel, m_PatchKernel;
        static readonly ProfilingSampler s_Upload = new ProfilingSampler("UploadMeshTransformChanges");
        static readonly ProfilingSampler s_Build = new ProfilingSampler("ComputeMeshTransformVersion");

        internal MeshTransformVersionCache(ResourcePool pool, ComputeShader shader)
        {
            m_Pool = pool;
            const string field = "GlobalSettings RuntimeShaders.meshDrawPipelineCS";
            m_InitializeKernel = ComputeKernel.Resolve(shader, field, "InitializeTransformVersion");
            m_CopyKernel = ComputeKernel.Resolve(shader, field, "CopyTransformVersion");
            m_PatchKernel = ComputeKernel.Resolve(shader, field, "PatchTransformVersion");
            foreach (ComputeKernel kernel in new[] { m_InitializeKernel, m_CopyKernel, m_PatchKernel })
            {
                if (!kernel.isValid)
                {
                    throw new InvalidOperationException("Mesh transforms require the immutable version kernels: " + kernel.missingReason);
                }
            }
        }

        internal void Prepare(MeshWorld world, NativeList<int2> dirtyRuns)
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(MeshTransformVersionCache));
            }
            if (m_Current != null && m_Current.revision == world.transformRevision)
            {
                return;
            }
            var source = m_Current;
            bool mergePending = source != null && !source.queued && source.references == 1;
            // Patch indices live until this call returns (AGENTS R17: Temp).
            using var patchIndices = new NativeList<int>(16, Allocator.Temp);
            // Fold an unsubmitted version with no view references into the next dirty set.
            // Only slot indices need merging: the CPU world owns their latest values.
            if (mergePending)
            {
                for (int i = 0; i < source.changeCount; ++i)
                {
                    patchIndices.Add((int)source.changes[i].index);
                }
                source = source.source;
            }
            if (m_Current == null)
                for (int i = 0; i < world.entityHighWater; ++i)
                {
                    patchIndices.Add(i);
                }
            else
                foreach (int2 run in dirtyRuns)
                    for (int i = run.x; i < math.min(run.y, world.entityHighWater); ++i)
                    {
                        patchIndices.Add(i);
                    }
            patchIndices.AsArray().Sort();
            int patchCount = 0;
            for (int i = 0; i < patchIndices.Length; ++i)
                if (i == 0 || patchIndices[i] != patchIndices[i - 1])
                {
                    patchCount++;
                }
            if (m_Current != null && patchCount == 0)
                throw new InvalidOperationException("Transform revision advanced without dirty slot data.");
            int capacity = math.max(16, math.ceilpow2(math.max(1, world.entityHighWater)));
            bool recycle = mergePending;
            MeshTransformVersion next = recycle ? m_Current
                : m_FreeVersions.Count > 0 ? m_FreeVersions.Pop() : new MeshTransformVersion();
            EnsureChanges(next, patchCount);
            var matrices = world.entityTransforms;
            var generations = world.entityGenerations;
            int destination = 0;
            for (int i = 0; i < patchIndices.Length; ++i)
            {
                if (i > 0 && patchIndices[i] == patchIndices[i - 1])
                {
                    continue;
                }
                int index = patchIndices[i];
                next.changes[destination++] = new MeshTransformPatch
                {
                    index = (uint)index, objectToWorld = matrices[index], generation = generations[index]
                };
            }
            next.owner = this;
            next.source = source;
            next.revision = world.transformRevision;
            next.worldId = world.worldId;
            next.count = world.entityHighWater;
            next.changeCount = patchCount;
            next.queued = false;
            next.submitted = false;
            if (recycle)
            {
                EnsureVersionBuffers(next, capacity, patchCount);
                return;
            }
            next.references = 1;
            try
            {
                next.matricesHandle = m_Pool.GetBuffer(new BufferDescriptor(capacity, 64) { name = "MeshTransformVersion" });
                next.generationsHandle = m_Pool.GetBuffer(new BufferDescriptor(capacity, 4) { name = "MeshTransformGenerations" });
                if (patchCount > 0)
                    next.patchesHandle = m_Pool.GetBuffer(new BufferDescriptor(patchCount, Marshal.SizeOf<MeshTransformPatch>()) { name = "MeshTransformChanges" });
            }
            catch
            {
                if (next.matricesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(next.matricesHandle);
                }
                if (next.generationsHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(next.generationsHandle);
                }
                if (next.patchesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(next.patchesHandle);
                }
                ReturnChanges(next);
                next.matricesHandle = default;
                next.generationsHandle = default;
                next.patchesHandle = default;
                m_FreeVersions.Push(next);
                throw;
            }
            source?.Retain();
            m_Versions.Add(next);
            m_Current?.Release();
            m_Current = next;
        }

        struct UploadData
        {
            public MeshTransformVersion version;
        }
        struct BuildData
        {
            public MeshTransformVersion version;
            public ComputeShader shader;
            public int copyKernel, patchKernel;
            public RGBufferHandle matricesHandle, generationsHandle, sourceMatricesHandle, sourceGenerationsHandle, patchesHandle;
        }
        internal void Record(RGBuilder graph, MeshTransformVersion version, HashSet<MeshTransformVersion> scheduled, bool externallyObserved = false)
        {
            if (version == null || version.queued || !scheduled.Add(version))
            {
                return;
            }
            if (version.count == 0)
            {
                version.queued = true;
                return;
            }
            Record(graph, version.source, scheduled);
            RGBufferHandle patchesHandle = default;
            if (version.changeCount > 0)
            {
                {
                    RGTransferPassHandle<UploadData> passHandle = graph.AddTransferPass<UploadData>(s_Upload);
                    if (!externallyObserved)
                    {
                        passHandle.KeepImportedWritesLocal();
                    }
                    patchesHandle = passHandle.WriteBuffer(graph.ImportBuffer(version.patchesHandle));
                    passHandle.GetPassData().version = version;
                    passHandle.SetExecuteFunc(s_WritePatches);
                }
            }
            {
                RGComputePassHandle<BuildData> passHandle = graph.AddComputePass<BuildData>(s_Build);
                if (!externallyObserved)
                {
                    passHandle.KeepImportedWritesLocal();
                }
                ref BuildData data = ref passHandle.GetPassData();
                data = default;
                data.version = version;
                data.shader = m_CopyKernel.shader;
                data.copyKernel = version.source != null ? m_CopyKernel.index : m_InitializeKernel.index;
                data.patchKernel = m_PatchKernel.index;
                data.matricesHandle = passHandle.WriteBuffer(graph.ImportBuffer(version.matricesHandle));
                data.generationsHandle = passHandle.WriteBuffer(graph.ImportBuffer(version.generationsHandle));
                if (version.source != null)
                {
                    data.sourceMatricesHandle = passHandle.ReadBuffer(graph.ImportBuffer(version.source.matricesHandle));
                    data.sourceGenerationsHandle = passHandle.ReadBuffer(graph.ImportBuffer(version.source.generationsHandle));
                }
                if (patchesHandle.IsValid())
                {
                    data.patchesHandle = passHandle.ReadBuffer(patchesHandle);
                }
                passHandle.SetQueueObserver(version);
                passHandle.SetExecuteFunc(s_BuildVersion);
            }
        }

        internal void Release(MeshTransformVersion version)
        {
            if (m_Disposed)
            {
                return;
            }
            if (--version.references < 0)
            {
                throw new InvalidOperationException("Mesh transform version released twice.");
            }
            if (version.references == 0)
            {
                m_Retired.Add(version);
            }
        }

        internal void RollbackUnsubmitted()
        {
            foreach (var version in m_Versions)
                if (!version.submitted)
                {
                    version.queued = false;
                }
        }

        internal void AfterSubmit(ResourceReuseFence reuseFence)
        {
            foreach (var version in m_Versions)
            {
                if (!version.queued || version.submitted)
                {
                    continue;
                }
                version.submitted = true;
                if (version.count > 0)
                {
                    productionCount++;
                }
                uploadedBytes += (long)version.changeCount * Marshal.SizeOf<MeshTransformPatch>();
                if (version.patchesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(version.patchesHandle, reuseFence);
                }
                version.patchesHandle = default;
                ReturnChanges(version);
                version.source?.Release();
                version.source = null;
            }
            // Releasing a version can retire the source it used to build from.
            for (int i = 0; i < m_Retired.Count; ++i)
            {
                var version = m_Retired[i];
                m_Pool.ReleaseBuffer(version.matricesHandle, reuseFence);
                m_Pool.ReleaseBuffer(version.generationsHandle, reuseFence);
                if (version.patchesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(version.patchesHandle, reuseFence);
                }
                version.source?.Release();
                version.source = null;
                m_Versions.Remove(version);
                ReturnChanges(version);
                version.matricesHandle = default;
                version.generationsHandle = default;
                version.patchesHandle = default;
                m_FreeVersions.Push(version);
            }
            m_Retired.Clear();
        }

        static readonly RGTransferPassExecuteAction<UploadData> s_WritePatches = WritePatches;
        static readonly RGComputePassExecuteAction<BuildData> s_BuildVersion = BuildVersion;

        static void WritePatches(in UploadData data, in RGTransferEncoder commands)
        {
            MeshTransformVersion version = data.version;
            commands.SetBufferData(version.patchesHandle.buffer, version.changes, 0, 0, version.changeCount);
        }

        static void BuildVersion(in BuildData data, in RGComputeEncoder commands)
        {
            MeshTransformVersion version = data.version;
            ComputeShader shader = data.shader;
            commands.SetComputeIntParam(shader, ID_TransformVersionCount, version.count);
            commands.SetComputeIntParam(shader, ID_TransformSourceCount, version.source != null ? version.source.count : 0);
            if (data.sourceMatricesHandle.IsValid())
            {
                commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformSourceMatrices, data.sourceMatricesHandle);
                commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformSourceGenerations, data.sourceGenerationsHandle);
            }
            commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformVersionMatrices, data.matricesHandle);
            commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformVersionGenerations, data.generationsHandle);
            commands.DispatchCompute(shader, data.copyKernel, (version.count + 63) / 64, 1, 1);
            if (data.patchesHandle.IsValid())
            {
                commands.SetComputeIntParam(shader, ID_TransformPatchCount, version.changeCount);
                commands.SetComputeBufferParam(shader, data.patchKernel, ID_TransformChanges, data.patchesHandle);
                commands.SetComputeBufferParam(shader, data.patchKernel, ID_TransformVersionMatrices, data.matricesHandle);
                commands.SetComputeBufferParam(shader, data.patchKernel, ID_TransformVersionGenerations, data.generationsHandle);
                commands.DispatchCompute(shader, data.patchKernel, (version.changeCount + 63) / 64, 1, 1);
            }
        }

        static void EnsureChanges(MeshTransformVersion version, int count)
        {
            if (count <= 0)
            {
                return;
            }
            if (version.changes != null && version.changes.Length >= count)
            {
                return;
            }
            int capacity = version.changes == null ? count : math.max(count, version.changes.Length * 2);
            version.changes = new MeshTransformPatch[capacity];
        }

        void EnsureVersionBuffers(MeshTransformVersion version, int capacity, int patchCount)
        {
            if (version.matricesHandle.buffer == null || version.matricesHandle.descriptor.count < capacity)
            {
                if (version.matricesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(version.matricesHandle);
                }
                if (version.generationsHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(version.generationsHandle);
                }
                version.matricesHandle = m_Pool.GetBuffer(new BufferDescriptor(capacity, 64) { name = "MeshTransformVersion" });
                version.generationsHandle = m_Pool.GetBuffer(new BufferDescriptor(capacity, 4) { name = "MeshTransformGenerations" });
            }
            if (patchCount > 0 && (version.patchesHandle.buffer == null || version.patchesHandle.descriptor.count < patchCount))
            {
                if (version.patchesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(version.patchesHandle);
                }
                version.patchesHandle = m_Pool.GetBuffer(new BufferDescriptor(patchCount, Marshal.SizeOf<MeshTransformPatch>()) { name = "MeshTransformChanges" });
            }
        }

        static void ReturnChanges(MeshTransformVersion version)
        {
            version.changeCount = 0;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            m_Disposed = true;
            foreach (var version in m_Versions)
            {
                m_Pool.ReleaseBuffer(version.matricesHandle);
                m_Pool.ReleaseBuffer(version.generationsHandle);
                if (version.patchesHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(version.patchesHandle);
                }
                ReturnChanges(version);
            }
            m_Current = null;
            m_Versions.Clear();
            m_Retired.Clear();
        }
    }
}
