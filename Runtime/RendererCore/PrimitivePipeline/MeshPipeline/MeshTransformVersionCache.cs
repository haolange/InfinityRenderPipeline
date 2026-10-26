using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    // GPU-RECORD: FMeshTransformPatch Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute
    internal struct FMeshTransformPatch
    {
        public float4x4 objectToWorld;
        public uint generation, index;
        public uint2 reserved;
    }

    internal sealed class MeshTransformVersion : IRGPassQueueObserver
    {
        internal MeshTransformVersionCache owner;
        internal MeshTransformVersion source;
        internal FBufferRef matrices, generations, patches;
        internal FMeshTransformPatch[] changes;
        internal int changeCount;
        internal int sceneId, count, references = 1;
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
            previous = accepted != null && accepted.sceneId == current.sceneId ? accepted : null;
        }
        internal void Bind(MaterialPropertyBlock properties)
        {
            properties.SetInt(s_Valid, previous != null ? 1 : 0);
            if (current == null) return;
            var source = previous ?? current;
            properties.SetInt(s_Count, previous != null ? previous.count : 0);
            properties.SetBuffer(s_CurrentGeneration, current.generations.buffer);
            properties.SetBuffer(s_PreviousGeneration, source.generations.buffer);
            properties.SetBuffer(s_PreviousMatrices, source.matrices.buffer);
        }
        static readonly int s_Valid = Shader.PropertyToID("meshPreviousTransformValid");
        static readonly int s_Count = Shader.PropertyToID("meshPreviousTransformCount");
        static readonly int s_CurrentGeneration = Shader.PropertyToID("transformGenerationBuffer");
        static readonly int s_PreviousGeneration = Shader.PropertyToID("previousTransformGenerationBuffer");
        static readonly int s_PreviousMatrices = InfinityTech.Rendering.Pipeline.InfinityShaderIDs.PreviousTransformBuffer;
    }

    // The scene owns immutable matrix/generation tables; views retain version references only.
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
        internal long uploadedBytes { get; private set; }
        internal int productionCount { get; private set; }
        static readonly int ID_TransformVersionCount = Shader.PropertyToID("_TransformVersionCount");
        static readonly int ID_TransformSourceCount = Shader.PropertyToID("_TransformSourceCount");
        static readonly int ID_TransformSourceMatrices = Shader.PropertyToID("_TransformSourceMatrices");
        static readonly int ID_TransformSourceGenerations = Shader.PropertyToID("_TransformSourceGenerations");
        static readonly int ID_TransformVersionMatrices = Shader.PropertyToID("_TransformVersionMatrices");
        static readonly int ID_TransformVersionGenerations = Shader.PropertyToID("_TransformVersionGenerations");
        static readonly int ID_TransformPatchCount = Shader.PropertyToID("_TransformPatchCount");
        static readonly int ID_TransformChanges = Shader.PropertyToID("_TransformChanges");
        ComputeShader m_KernelShader;
        int m_InitializeKernel, m_CopyKernel, m_PatchKernel;
        static readonly ProfilingSampler s_Upload = new ProfilingSampler("UploadMeshTransformChanges");
        static readonly ProfilingSampler s_Build = new ProfilingSampler("ComputeMeshTransformVersion");

        internal MeshTransformVersionCache(ResourcePool pool) => m_Pool = pool;

        internal void Prepare(MeshScene scene, NativeList<int2> dirtyRuns)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(MeshTransformVersionCache));
            if (m_Current != null && m_Current.revision == scene.TransformRevision) return;
            var source = m_Current;
            bool mergePending = source != null && !source.queued && source.references == 1;
            var indices = new NativeList<int>(math.max(1, mergePending ? source.changeCount : 0), Allocator.Temp);
            FMeshTransformPatch[] patchArray = null;
            int patchCount;
            try
            {
                // Fold an unsubmitted version with no view references into the next dirty set.
                // Only slot indices need merging: the CPU scene owns their latest values.
                if (mergePending)
                {
                    for (int i = 0; i < source.changeCount; ++i) indices.Add((int)source.changes[i].index);
                    source = source.source;
                }
                if (m_Current == null)
                    for (int i = 0; i < scene.TransformHighWater; ++i) indices.Add(i);
                else
                    foreach (int2 run in dirtyRuns)
                        for (int i = run.x; i < math.min(run.y, scene.TransformHighWater); ++i) indices.Add(i);
                indices.AsArray().Sort();
                int count = 0;
                for (int i = 0; i < indices.Length; ++i)
                    if (i == 0 || indices[i] != indices[i - 1]) count++;
                patchCount = count;
                patchArray = count == 0 ? Array.Empty<FMeshTransformPatch>() : ArrayPool<FMeshTransformPatch>.Shared.Rent(count);
                var matrices = scene.GetTransforms();
                var generations = scene.GetTransformGenerations();
                int destination = 0;
                for (int i = 0; i < indices.Length; ++i)
                {
                    if (i > 0 && indices[i] == indices[i - 1]) continue;
                    int index = indices[i];
                    patchArray[destination++] = new FMeshTransformPatch
                    {
                        index = (uint)index, objectToWorld = matrices[index].current, generation = generations[index]
                    };
                }
            }
            catch
            {
                if (patchArray != null && patchArray.Length > 0) ArrayPool<FMeshTransformPatch>.Shared.Return(patchArray);
                throw;
            }
            finally { indices.Dispose(); }
            if (m_Current != null && patchCount == 0)
                throw new InvalidOperationException("Transform revision advanced without dirty slot data.");
            int capacity = math.max(16, scene.TransformCapacity);
            var next = m_FreeVersions.Count > 0 ? m_FreeVersions.Pop() : new MeshTransformVersion();
            next.owner = this; next.source = source; next.revision = scene.TransformRevision;
            next.sceneId = scene.SceneId; next.count = scene.TransformHighWater;
            next.changes = patchArray; next.changeCount = patchCount;
            next.references = 1; next.queued = false; next.submitted = false;
            try
            {
                next.matrices = m_Pool.GetBuffer(new BufferDescriptor(capacity, 64) { name = "MeshTransformVersion" });
                next.generations = m_Pool.GetBuffer(new BufferDescriptor(capacity, 4) { name = "MeshTransformGenerations" });
                if (patchCount > 0)
                    next.patches = m_Pool.GetBuffer(new BufferDescriptor(patchCount, Marshal.SizeOf<FMeshTransformPatch>()) { name = "MeshTransformChanges" });
            }
            catch
            {
                if (next.matrices.buffer != null) m_Pool.ReleaseBuffer(next.matrices);
                if (next.generations.buffer != null) m_Pool.ReleaseBuffer(next.generations);
                if (next.patches.buffer != null) m_Pool.ReleaseBuffer(next.patches);
                ReturnChanges(next);
                next.matrices = default; next.generations = default; next.patches = default;
                m_FreeVersions.Push(next);
                throw;
            }
            source?.Retain();
            m_Versions.Add(next);
            m_Current?.Release();
            m_Current = next;
        }

        struct UploadData { public MeshTransformVersion version; }
        struct BuildData
        {
            public MeshTransformVersion version;
            public ComputeShader shader;
            public int copyKernel, patchKernel;
            public RGBufferRef matrices, generations, sourceMatrices, sourceGenerations, patches;
        }
        internal void Record(RGBuilder graph, MeshTransformVersion version, ComputeShader shader, HashSet<MeshTransformVersion> scheduled, bool externallyObserved = false)
        {
            if (version == null || version.queued || !scheduled.Add(version)) return;
            if (version.count == 0) { version.queued = true; return; }
            if (shader == null || !shader.HasKernel("InitializeTransformVersion") || !shader.HasKernel("CopyTransformVersion") || !shader.HasKernel("PatchTransformVersion"))
                throw new InvalidOperationException("Mesh transforms require the immutable version kernels.");
            if (m_KernelShader != shader)
            {
                m_KernelShader = shader;
                m_InitializeKernel = shader.FindKernel("InitializeTransformVersion");
                m_CopyKernel = shader.FindKernel("CopyTransformVersion");
                m_PatchKernel = shader.FindKernel("PatchTransformVersion");
            }
            Record(graph, version.source, shader, scheduled);
            RGBufferRef patches = default;
            if (version.changeCount > 0)
            {
                using (var upload = graph.AddTransferPass<UploadData>(s_Upload))
                {
                    if (!externallyObserved) upload.KeepImportedWritesLocal();
                    patches = upload.WriteBuffer(graph.ImportBuffer(version.patches));
                    upload.GetPassData<UploadData>().version = version;
                    upload.SetExecuteFunc((in UploadData data, in RGTransferEncoder commands) =>
                    {
                        var v = data.version;
                        commands.SetBufferData(v.patches.buffer, v.changes, 0, 0, v.changeCount);
                    });
                }
            }
            using (var pass = graph.AddComputePass<BuildData>(s_Build))
            {
                if (!externallyObserved) pass.KeepImportedWritesLocal();
                ref BuildData data = ref pass.GetPassData<BuildData>();
                data = default;
                data.version = version; data.shader = shader;
                data.copyKernel = version.source != null ? m_CopyKernel : m_InitializeKernel;
                data.patchKernel = m_PatchKernel;
                data.matrices = pass.WriteBuffer(graph.ImportBuffer(version.matrices));
                data.generations = pass.WriteBuffer(graph.ImportBuffer(version.generations));
                if (version.source != null)
                {
                    data.sourceMatrices = pass.ReadBuffer(graph.ImportBuffer(version.source.matrices));
                    data.sourceGenerations = pass.ReadBuffer(graph.ImportBuffer(version.source.generations));
                }
                if (patches.IsValid()) data.patches = pass.ReadBuffer(patches);
                pass.SetQueueObserver(version);
                pass.SetExecuteFunc((in BuildData data, in RGComputeEncoder commands) =>
                {
                    var v = data.version;
                    var shader = data.shader;
                    commands.SetComputeIntParam(shader, ID_TransformVersionCount, v.count);
                    commands.SetComputeIntParam(shader, ID_TransformSourceCount, v.source != null ? v.source.count : 0);
                    if (data.sourceMatrices.IsValid())
                    {
                        commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformSourceMatrices, data.sourceMatrices);
                        commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformSourceGenerations, data.sourceGenerations);
                    }
                    commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformVersionMatrices, data.matrices);
                    commands.SetComputeBufferParam(shader, data.copyKernel, ID_TransformVersionGenerations, data.generations);
                    commands.DispatchCompute(shader, data.copyKernel, (v.count + 63) / 64, 1, 1);
                    if (data.patches.IsValid())
                    {
                        commands.SetComputeIntParam(shader, ID_TransformPatchCount, v.changeCount);
                        commands.SetComputeBufferParam(shader, data.patchKernel, ID_TransformChanges, data.patches);
                        commands.SetComputeBufferParam(shader, data.patchKernel, ID_TransformVersionMatrices, data.matrices);
                        commands.SetComputeBufferParam(shader, data.patchKernel, ID_TransformVersionGenerations, data.generations);
                        commands.DispatchCompute(shader, data.patchKernel, (v.changeCount + 63) / 64, 1, 1);
                    }
                });
            }
        }

        internal void Release(MeshTransformVersion version)
        {
            if (m_Disposed) return;
            if (--version.references < 0) throw new InvalidOperationException("Mesh transform version released twice.");
            if (version.references == 0) m_Retired.Add(version);
        }

        internal void RollbackUnsubmitted()
        {
            foreach (var version in m_Versions)
                if (!version.submitted) version.queued = false;
        }

        internal void AfterSubmit(ResourceReuseFence reuseFence)
        {
            foreach (var version in m_Versions)
            {
                if (!version.queued || version.submitted) continue;
                version.submitted = true;
                if (version.count > 0) productionCount++;
                uploadedBytes += (long)version.changeCount * Marshal.SizeOf<FMeshTransformPatch>();
                if (version.patches.buffer != null) m_Pool.ReleaseBuffer(version.patches, reuseFence);
                version.patches = default;
                ReturnChanges(version);
                version.source?.Release(); version.source = null;
            }
            // Releasing a version can retire the source it used to build from.
            for (int i = 0; i < m_Retired.Count; ++i)
            {
                var version = m_Retired[i];
                m_Pool.ReleaseBuffer(version.matrices, reuseFence);
                m_Pool.ReleaseBuffer(version.generations, reuseFence);
                if (version.patches.buffer != null) m_Pool.ReleaseBuffer(version.patches, reuseFence);
                version.source?.Release(); version.source = null;
                m_Versions.Remove(version);
                ReturnChanges(version);
                version.matrices = default; version.generations = default; version.patches = default;
                m_FreeVersions.Push(version);
            }
            m_Retired.Clear();
        }

        static void ReturnChanges(MeshTransformVersion version)
        {
            if (version.changeCount > 0) ArrayPool<FMeshTransformPatch>.Shared.Return(version.changes);
            version.changes = Array.Empty<FMeshTransformPatch>(); version.changeCount = 0;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            foreach (var version in m_Versions)
            {
                m_Pool.ReleaseBuffer(version.matrices);
                m_Pool.ReleaseBuffer(version.generations);
                if (version.patches.buffer != null) m_Pool.ReleaseBuffer(version.patches);
                ReturnChanges(version);
            }
            m_Current = null; m_Versions.Clear(); m_Retired.Clear();
        }
    }
}
