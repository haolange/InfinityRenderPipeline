using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.RenderGraph
{
    public struct RGContext
    {
        public RGObjectPool objectPool;
        internal CommandBuffer cmdBuffer;
        public RenderContext renderContext;
        internal RGDrawListContext drawLists;
        internal RGRendererListContext rendererLists;
    }

    internal struct RGPassCompileInfo
    {
        public IRGPass pass;
        public int refCount;
        public int syncToPassIndex; // Index of the pass that needs to be waited for.
        public int syncFromPassIndex; // Smaller pass index that waits for this pass.
        public bool culled;
        public bool hasSideEffect;
        public bool needGraphicsFence;
        public GraphicsFence fence, completionFence;
        public bool needResourceFence;
        public List<int>[] resourceCreateList;
        public List<int>[] resourceReleaseList;
        public bool enablePassCulling { get { return pass.enablePassCulling; } }
        public bool enableAsyncCompute;
        public int segment;
        public int mergeGroup;
        public int subpass;
        public bool mergeFirst, mergeLast;
        public string cullReason;
        public List<ProfilingSampler> beginSamples;
        public List<ProfilingSampler> endSamples;

        public void Reset(IRGPass pass, bool asyncComputeSupported)
        {
            this.pass = pass;
            enableAsyncCompute = pass.enableAsyncCompute && asyncComputeSupported;

            int resourceTypeCount = (int)ERGResourceType.Max;
            if (resourceCreateList == null || resourceCreateList.Length != resourceTypeCount)
            {
                resourceCreateList = new List<int>[resourceTypeCount];
                resourceReleaseList = new List<int>[resourceTypeCount];
                for (int i = 0; i < resourceTypeCount; ++i)
                {
                    resourceCreateList[i] = new List<int>();
                    resourceReleaseList[i] = new List<int>();
                }
            }

            for (int i = 0; i < resourceTypeCount; ++i)
            {
                resourceCreateList[i].Clear();
                resourceReleaseList[i].Clear();
            }

            refCount = 0;
            culled = false;
            hasSideEffect = false;
            syncToPassIndex = -1;
            syncFromPassIndex = -1;
            needGraphicsFence = false;
            fence = completionFence = default;
            needResourceFence = false;
            segment = -1;
            mergeGroup = -1;
            subpass = 0;
            mergeFirst = mergeLast = false;
            cullReason = null;
            if (beginSamples == null) beginSamples = new List<ProfilingSampler>(2);
            if (endSamples == null) endSamples = new List<ProfilingSampler>(2);
            beginSamples.Clear();
            endSamples.Clear();
        }
    }

    internal struct RGResourceCompileInfo
    {
        public int refCount;
        public bool resourceCreated;
        public int lastGraphicsUse, lastComputeUse;
        public int aliasSlot;
        public int aliasGroup;
        public long estimatedBytes;
        public bool cullClosed;
        public List<int> consumers;
        public List<int> producers;

        public void Reset()
        {
            if (producers == null)
                producers = new List<int>();
            if (consumers == null)
                consumers = new List<int>();

            producers.Clear();
            consumers.Clear();
            resourceCreated = false;
            lastGraphicsUse = lastComputeUse = -1;
            aliasSlot = -1;
            aliasGroup = -1;
            estimatedBytes = 0;
            cullClosed = false;
            refCount = 0;
        }
    }

    public partial class RGBuilder
    {
        public string name;
        // Label of the next Execute in the RenderGraph Viewer (the pipeline sets the camera name).
        public string debugLabel;
        RGResourceFactory m_Resources;
        readonly List<RGProfileScope> m_ProfileScopes = new List<RGProfileScope>(8);
        readonly List<RGProfileScope> m_OpenScopes = new List<RGProfileScope>(4);
        readonly List<RGNativePassGroup> m_NativeGroups = new List<RGNativePassGroup>(8);
        IRGCompileExtension m_CompileExtension;
        bool m_RecordingCompilerPasses;
        bool m_ExtensionContributed;
        internal bool RecordingCompilerPasses => m_RecordingCompilerPasses;
        internal T[] GetRecordingArray<T>(int count)
        {
            T[] array = m_ObjectPool.GetRecordingArray<T>(count);
            Array.Clear(array, 0, count);
            return array;
        }

        internal T[] SnapshotRecordingArray<T>(T[] source)
        {
            var destination = GetRecordingArray<T>(source.Length);
            Array.Copy(source, destination, source.Length);
            return destination;
        }

        List<IRGPass> m_PassList = new List<IRGPass>(64);
        readonly Dictionary<int, RGBufferRef[]> m_PreparedDrawLists = new Dictionary<int, RGBufferRef[]>();
        RGObjectPool m_ObjectPool = new RGObjectPool();
        DynamicArray<RGPassCompileInfo> m_PassCompileInfos;
        DynamicArray<RGResourceCompileInfo>[] m_ResourcesCompileInfos;
        RGDrawListContext m_DrawListRecords = new RGDrawListContext();
        RGRendererListContext m_RendererListRecords = new RGRendererListContext();
        MeshWorld m_MeshWorld;
        readonly Dictionary<MeshGpuVisibilityKey, RGBufferRef> m_GpuCullVisibility = new Dictionary<MeshGpuVisibilityKey, RGBufferRef>(8);

        readonly bool m_AsyncComputeSupported;

        public RGBuilder(string name, ResourcePool resourcePool = null)
            : this(name, resourcePool, SystemInfo.supportsAsyncCompute && SystemInfo.supportsGraphicsFence) { }

        // The capability is frozen for this graph. Tests can compile both topologies
        // without submitting unsupported queue work on the host device.
        internal RGBuilder(string name, ResourcePool resourcePool, bool asyncComputeSupported)
        {
            m_AsyncComputeSupported = asyncComputeSupported;
            this.name = name;
            this.m_Resources = new RGResourceFactory(resourcePool);
            this.m_PassCompileInfos = new DynamicArray<RGPassCompileInfo>();
            this.m_ResourcesCompileInfos = new DynamicArray<RGResourceCompileInfo>[(int)ERGResourceType.Max];

            for (int i = 0; i < (int)ERGResourceType.Max; ++i)
            {
                this.m_ResourcesCompileInfos[i] = new DynamicArray<RGResourceCompileInfo>();
            }
        }

        public void SetTextureCapacity(FTextureCapacityContext capacity) => m_Resources.textureCapacity = capacity;

        /// <summary>
        /// Record a Unity RendererList descriptor. Does not call ScriptableRenderContext.CreateRendererList.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGRendererListRef CreateRendererList(in RendererListDesc desc)
        {
            return m_RendererListRecords.Declare(desc);
        }

        /// <summary>
        /// Record shadow drawing settings. CreateShadowRendererList runs after pass culling.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGRendererListRef CreateShadowRendererList(in ShadowDrawingSettings settings)
        {
            return m_RendererListRecords.DeclareShadow(settings);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef ImportBuffer(in FBufferRef buffer)
        {
            return m_Resources.ImportBuffer(buffer);
        }

        public RGBufferRef ImportBuffer(GraphicsBuffer buffer, string name)
        {
            return m_Resources.ImportBuffer(buffer, name);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateBuffer(in BufferDescriptor descriptor)
        {
            return m_Resources.CreateBuffer(descriptor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateBuffer(in RGBufferRef bufferRef)
        {
            return m_Resources.CreateBuffer(m_Resources.GetBufferDescriptor(bufferRef.handle));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferDescriptor GetBufferDescriptor(in RGBufferRef bufferRef)
        {
            return m_Resources.GetBufferDescriptor(bufferRef.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ImportTexture(RTHandle texture)
        {
            return m_Resources.ImportTexture(texture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ImportTexture(in FTextureRef texture)
        {
            return m_Resources.ImportTexture(texture.texture, texture.descriptor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ImportBackbuffer(in RenderTargetIdentifier backBuffer, in TextureDescriptor descriptor)
        {
            return m_Resources.ImportBackbuffer(backBuffer, descriptor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTexture(in RGTextureRef textureRef)
        {
            return m_Resources.CreateTexture(m_Resources.GetTextureDescriptor(textureRef.handle));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTexture(in TextureDescriptor descriptor)
        {
            return m_Resources.CreateTexture(descriptor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureDescriptor GetTextureDescriptor(in RGTextureRef textureRef)
        {
            return m_Resources.GetTextureDescriptor(textureRef.handle);
        }

        internal string GetTextureProducer(in RGTextureRef textureRef, out string queue) => GetProducer(textureRef.handle, out queue);
        internal string GetBufferProducer(in RGBufferRef bufferRef, out string queue) => GetProducer(bufferRef.handle, out queue);

        string GetProducer(in RGResourceHandle handle, out string queue)
        {
            if (!handle.IsValid()) throw new InvalidOperationException("Capture source handle is invalid.");
            for (int i = m_PassList.Count - 1; i >= 0; --i)
            {
                IRGPass pass = m_PassList[i];
                if (!pass.resourceWriteLists[handle.iType].Contains(handle)) continue;
                queue = m_AsyncComputeSupported && pass.enableAsyncCompute ? "AsyncCompute" : "Graphics";
                return pass.name;
            }
            // A persistent import can be read without being produced again in this graph (for example,
            // an OnDemand shadow cache hit). Keep that external provenance distinct from a frame pass.
            m_Resources.DescribeResource(handle.iType, handle.index, out string name, out bool imported, out _);
            if (imported)
            {
                queue = "External";
                return "Imported: " + name;
            }
            throw new InvalidOperationException("Capture source has no recorded producer.");
        }

        internal string DescribeCompiledGraph()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("index|name|type|queue|culled|waitForPass|signalsToPass|reads|writes|creates|releases|fence");
            for (int i = 0; i < m_PassCompileInfos.size; ++i)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[i];
                IRGPass pass = info.pass;
                text.Append(i).Append('|').Append(pass.name).Append('|').Append(pass.passType).Append('|')
                    .Append(info.enableAsyncCompute ? "AsyncCompute" : "Graphics").Append('|').Append(info.culled)
                    .Append('|').Append(info.syncToPassIndex).Append('|').Append(info.syncFromPassIndex).Append('|');
                foreach (var list in pass.resourceReadLists)
                    foreach (RGResourceHandle resource in list) text.Append(resource.type).Append(':').Append(resource.index).Append(',');
                text.Append('|');
                foreach (var list in pass.resourceWriteLists)
                    foreach (RGResourceHandle resource in list) text.Append(resource.type).Append(':').Append(resource.index).Append(',');
                text.Append('|');
                for (int type = 0; type < (int)ERGResourceType.Max; type++)
                    foreach (int resource in info.resourceCreateList[type]) text.Append((ERGResourceType)type).Append(':').Append(resource).Append(',');
                text.Append('|');
                for (int type = 0; type < (int)ERGResourceType.Max; type++)
                    foreach (int resource in info.resourceReleaseList[type]) text.Append((ERGResourceType)type).Append(':').Append(resource).Append(',');
                text.Append('|').Append(info.needGraphicsFence);
                text.Append('|').Append(info.segment).Append('|').Append(info.mergeGroup);
                text.AppendLine();
            }
            return text.ToString();
        }

        // The compiled graph for the RenderGraph Viewer; valid between CompilePass and ClearPass.
        internal RGGraphSnapshot CaptureDebugSnapshot()
        {
            var snapshot = new RGGraphSnapshot { label = string.IsNullOrEmpty(debugLabel) ? name : debugLabel, frame = Time.frameCount };
            int[] offsets = new int[(int)ERGResourceType.Max];
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                offsets[type] = snapshot.resources.Count;
                int count = m_Resources.GetResourceCount(type);
                for (int i = 0; i < count; ++i)
                {
                    m_Resources.DescribeResource(type, i, out string resourceName, out bool imported, out string description);
                    ref RGResourceCompileInfo compiled = ref m_ResourcesCompileInfos[type][i];
                    snapshot.resources.Add(new RGResourceSnapshot
                    {
                        type = (ERGResourceType)type, index = i, name = resourceName, imported = imported, description = description,
                        firstUse = -1, lastUse = -1, createPass = -1, releasePass = -1,
                        aliasSlot = compiled.aliasSlot, aliasGroup = compiled.aliasGroup, estimatedBytes = compiled.estimatedBytes,
                        memoryless = type == (int)ERGResourceType.Texture && m_Resources.GetTextureDescriptor(new RGResourceHandle(i, ERGResourceType.Texture)).memoryless != RenderTextureMemoryless.None
                    });
                }
            }

            var scratch = new List<int>(16);
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                IRGPass pass = info.pass;
                int[] reads = FlattenHandles(pass.resourceReadLists, offsets, scratch);
                int[] writes = FlattenHandles(pass.resourceWriteLists, offsets, scratch);
                int[] creates = FlattenIndices(info.resourceCreateList, offsets, scratch);
                int[] releases = FlattenIndices(info.resourceReleaseList, offsets, scratch);
                snapshot.passes.Add(new RGPassSnapshot
                {
                    name = pass.name, type = pass.passType, asyncCompute = info.enableAsyncCompute, culled = info.culled,
                    allowCulling = !info.hasSideEffect, waitsForPass = info.syncToPassIndex, signalsToPass = info.syncFromPassIndex,
                    graphicsFence = info.needGraphicsFence, segment = info.segment, mergeGroup = info.mergeGroup, subpass = info.subpass,
                    cullReason = info.cullReason, reads = reads, writes = writes, creates = creates, releases = releases
                });
                if (info.segment >= snapshot.segmentCount) snapshot.segmentCount = info.segment + 1;

                if (!info.culled)
                {
                    MarkUse(snapshot, reads, passIndex);
                    MarkUse(snapshot, writes, passIndex);
                }
                foreach (int resource in creates)
                {
                    RGResourceSnapshot entry = snapshot.resources[resource];
                    if (!entry.imported) { entry.createPass = passIndex; snapshot.resources[resource] = entry; }
                }
                foreach (int resource in releases)
                {
                    RGResourceSnapshot entry = snapshot.resources[resource];
                    if (!entry.imported) { entry.releasePass = passIndex; snapshot.resources[resource] = entry; }
                }
            }
            long peak = 0;
            var physical = new HashSet<long>();
            for (int i = 0; i < snapshot.resources.Count; ++i)
            {
                RGResourceSnapshot resource = snapshot.resources[i];
                if (resource.imported || resource.firstUse < 0) continue;
                if (resource.aliasSlot < 0) { peak += resource.estimatedBytes; continue; }
                long key = ((long)resource.aliasGroup << 32) | (uint)resource.aliasSlot;
                if (physical.Add(key)) peak += resource.estimatedBytes;
            }
            snapshot.peakAliasBytes = peak;
            return snapshot;
        }

        static int[] FlattenHandles(List<RGResourceHandle>[] lists, int[] offsets, List<int> scratch)
        {
            scratch.Clear();
            for (int type = 0; type < lists.Length && type < offsets.Length; ++type)
                foreach (RGResourceHandle handle in lists[type]) scratch.Add(offsets[type] + handle.index);
            return scratch.ToArray();
        }

        static int[] FlattenIndices(List<int>[] lists, int[] offsets, List<int> scratch)
        {
            scratch.Clear();
            for (int type = 0; type < lists.Length && type < offsets.Length; ++type)
                foreach (int index in lists[type]) scratch.Add(offsets[type] + index);
            return scratch.ToArray();
        }

        static void MarkUse(RGGraphSnapshot snapshot, int[] resources, int passIndex)
        {
            foreach (int resource in resources)
            {
                RGResourceSnapshot entry = snapshot.resources[resource];
                if (entry.firstUse < 0) entry.firstUse = passIndex;
                entry.lastUse = passIndex;
                snapshot.resources[resource] = entry;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTransferPassRef AddTransferPass<T>(ProfilingSampler profilerSampler) where T : struct
        {
            RGTransferPass<T> transferPass = m_ObjectPool.Get<RGTransferPass<T>>();
            transferPass.Clear();
            transferPass.name = profilerSampler.name;
            transferPass.customSampler = profilerSampler;
            RegisterPass(transferPass);
            return new RGTransferPassRef(transferPass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGComputePassRef AddComputePass<T>(ProfilingSampler profilerSampler) where T : struct
        {
            return AddComputePass<T>(profilerSampler.name, profilerSampler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGComputePassRef AddComputePass<T>(string passName, ProfilingSampler profilerSampler) where T : struct
        {
            RGComputePass<T> computePass = m_ObjectPool.Get<RGComputePass<T>>();
            computePass.Clear();
            computePass.name = passName;
            computePass.customSampler = profilerSampler;
            RegisterPass(computePass);
            return new RGComputePassRef(computePass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginProfilingSampler(ProfilingSampler sampler) => BeginProfileRange(sampler);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndProfilingSampler(ProfilingSampler sampler) => EndProfileRange(sampler);

        void RegisterPass(IRGPass pass)
        {
            if (m_RecordingCompilerPasses) pass.insertedByCompiler = true;
            pass.index = m_PassList.Count;
            m_PassList.Add(pass);
            NoteProfiledPass(pass);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGRayTracingPassRef AddRayTracingPass<T>(ProfilingSampler profilerSampler) where T : struct
        {
            RGRayTracingPass<T> rayTracingPass = m_ObjectPool.Get<RGRayTracingPass<T>>();
            rayTracingPass.Clear();
            rayTracingPass.name = profilerSampler.name;
            rayTracingPass.customSampler = profilerSampler;
            RegisterPass(rayTracingPass);
            return new RGRayTracingPassRef(rayTracingPass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGRasterPassRef AddRasterPass<T>(ProfilingSampler profilerSampler) where T : struct
        {
            RGRasterPass<T> rasterPass = m_ObjectPool.Get<RGRasterPass<T>>();
            rasterPass.Clear();
            rasterPass.name = profilerSampler.name;
            rasterPass.customSampler = profilerSampler;
            RegisterPass(rasterPass);
            return new RGRasterPassRef(rasterPass, m_Resources, m_DrawListRecords, m_RendererListRecords);
        }

        /// <summary>
        /// Drop recorded passes / DrawLists without executing. Safe to call after Execute (no-op on empty).
        /// Ensures visibility refs and GPU payloads are released if recording aborts early.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearRecordedGraph()
        {
            ClearPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(RenderContext renderContext, CommandBuffer cmdBuffer)
        {
            m_QueueSubmissionFailed = false;
            // Keep frame/camera scopes separate from commands that may be abandoned by a failed pass.
            CommandBuffer graphCommands = CommandBufferPool.Get();
            graphCommands.SetExecutionFlags(CommandBufferExecutionFlags.None);
            RGContext graphContext = new RGContext
            {
                cmdBuffer = graphCommands, objectPool = m_ObjectPool,
                renderContext = renderContext, drawLists = m_DrawListRecords,
                rendererLists = m_RendererListRecords
            };
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            try
            {
                if (cmdBuffer.sizeInBytes != 0)
                {
                    QueueGraphics(renderContext, cmdBuffer);
                    cmdBuffer.Clear();
                }
                m_Resources.BeginRender();
                CompilePass();
                if (RGDebugSession.captureEnabled)
                {
                    RGDebugSession.Publish(CaptureDebugSnapshot());
                }
                if (m_RendererListRecords.LiveCount > 0)
                {
                    if (renderContext == null)
                    {
                        throw new InvalidOperationException("CreateRendererList requires ScriptableRenderContext after pass culling.");
                    }

                    m_RendererListRecords.CreateLive(renderContext.scriptableRenderContext);
                }
                ExecutePass(ref graphContext);
            }
            catch (Exception exception)
            {
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                graphCommands.Clear();
                CommandBufferPool.Release(graphCommands);
                try { ClearPass(); }
                catch (Exception cleanupError)
                {
                    if (failure == null) failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError);
                    else Debug.LogException(cleanupError);
                }
                finally
                {
                    m_Resources.EndRender();
                    try { m_ObjectPool.ReleaseAllTempAlloc(); m_ObjectPool.ReleaseRecordingAlloc(); }
                    catch (Exception cleanupError)
                    {
                        if (failure == null) failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError);
                        else Debug.LogException(cleanupError);
                    }
                }
            }
            failure?.Throw();
        }

        bool m_QueueSubmissionFailed;
        bool m_UnrecoverableAsyncSubmission;
        internal bool HasQueueSubmissionFailure => m_QueueSubmissionFailed;
        internal bool CanRetireSubmittedResources => !m_UnrecoverableAsyncSubmission;

        internal int RetiredResourceCount => m_Resources.RetiredResourceCount;
        internal void FlushRetiredResources(ResourceReuseFence reuseFence = default) => m_Resources.FlushRetired(reuseFence);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ClearPass()
        {
            m_ScheduledMeshVersions.Clear();
            Exception failure = null;
            try { ReleaseAllDrawLists(); }
            catch (Exception error) { failure = error; }
            try { ReleaseAllRendererLists(); }
            catch (Exception error)
            {
                if (failure == null) failure = error;
                else Debug.LogException(error);
            }
            foreach (var pass in m_PassList)
            {
                try { pass.Release(m_ObjectPool); }
                catch (Exception error)
                {
                    if (failure == null) failure = error;
                    else Debug.LogException(error);
                }
            }
            m_PassList.Clear();
            m_GpuCullVisibility.Clear();
            m_ObjectPool.ReleaseRecordingAlloc();
            m_ProfileScopes.Clear();
            m_OpenScopes.Clear();
            for (int i = 0; i < m_NativeGroups.Count; ++i) m_NativeGroups[i].Clear();
            m_ExtensionContributed = false;
            m_RecordingCompilerPasses = false;
            try { m_Resources.Clear(); }
            catch (Exception error)
            {
                if (failure == null) failure = error;
                else Debug.LogException(error);
            }
            for (int i = 0; i < (int)ERGResourceType.Max; ++i) m_ResourcesCompileInfos[i].Clear();
            m_PassCompileInfos.Clear();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ReleaseAllDrawLists()
        {
            m_DrawListRecords.ReleaseAll();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ReleaseAllRendererLists()
        {
            m_RendererListRecords.ReleaseAll();
        }

        public void Dispose()
        {
            m_Resources.Dispose();
        }
    }

    struct RGProfilingScopePassData
    {
        public ProfilingSampler sampler;
    }

    public struct RGProfilingScope : IDisposable
    {
        RGBuilder m_Builder;
        ProfilingSampler m_Sampler;
        bool m_Disposed;

        public RGProfilingScope(RGBuilder builder, ProfilingSampler sampler)
        {
            m_Builder = builder;
            m_Sampler = sampler;
            m_Disposed = false;
            m_Builder.BeginProfilingSampler(sampler);
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            m_Builder.EndProfilingSampler(m_Sampler);
        }
    }
}
