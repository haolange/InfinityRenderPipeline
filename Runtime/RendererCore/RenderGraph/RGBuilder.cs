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
        }
    }

    internal struct RGResourceCompileInfo
    {
        public int refCount;
        public bool resourceCreated;
        public int lastGraphicsUse, lastComputeUse;
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
            refCount = 0;
        }
    }

    public partial class RGBuilder
    {
        public string name;
        // Label of the next Execute in the RenderGraph Viewer (the pipeline sets the camera name).
        public string debugLabel;
        RGResourceFactory m_Resources;
        Stack<int> m_CullingStack = new Stack<int>();
        internal T[] GetRecordingArray<T>(int count)
        {
            T[] array = m_ObjectPool.GetTempArray<T>(count);
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
        readonly List<IRGPass> m_PreparedPasses = new List<IRGPass>(64);
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

        public void SetMeshWorld(MeshWorld meshWorld)
        {
            m_MeshWorld = meshWorld;
        }

        /// <summary>
        /// Record a DrawList for one (view, pass). Does not Schedule or allocate TempJob memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGDrawListRef CreateDrawList(in MeshView view, MeshPassId passId)
        {
            if (m_MeshWorld == null)
            {
                throw new InvalidOperationException("CreateDrawList requires MeshWorld.");
            }

            MeshPassContext pass = m_MeshWorld.BindPass(view, passId);
            RGDrawListRef draws = m_DrawListRecords.Declare(
                m_MeshWorld.Processor,
                pass,
                MeshVisibilityHandle.Invalid,
                m_MeshWorld.VisibilityShare,
                view,
                passId,
                m_MeshWorld);
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
            {
                throw new InvalidOperationException("EnsureMeshGpuCull requires MeshWorld.");
            }

            EnsureMeshGpuCull(view, RGDrawListRef.Invalid);
        }

        void EnsureMeshGpuCull(in MeshView view, in RGDrawListRef draws)
        {
            if (MeshDrawGPUBackend.SelectPolicy(m_MeshWorld.SelectPolicy()) != EMeshBackendPolicy.GpuIndirect)
            {
                return;
            }

            MeshGpuVisibilityKey key = MeshGpuVisibilityCache.MakeKey(view, true);
            if (m_GpuCullVisibility.TryGetValue(key, out RGBufferRef existing))
            {
                if (m_DrawListRecords.IsLiveRef(draws))
                {
                    m_DrawListRecords.BindGpuVisibility(draws.index, existing);
                }

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
            {
                throw new InvalidOperationException("MeshGpuCull requires uploaded GpuScene instance buffers.");
            }

            if (!MeshDrawGPUBackend.TryGetCullKernel(out _, out _))
            {
                throw new InvalidOperationException("MeshGpuCull requires CullFrustum.");
            }

            int instanceCount = residency.Scene != null ? residency.Scene.InstanceHighWater : 0;
            FBufferRef visibility = m_MeshWorld.GetGpuVisibilityBufferRef(view, math.max(1, instanceCount));
            RGBufferRef visibilityRef = ImportBuffer(visibility);
            RGBufferRef boundsCenter = ImportBuffer(residency.BoundsCenterBuffer);
            RGBufferRef boundsExtent = ImportBuffer(residency.BoundsExtentBuffer);
            RGBufferRef instanceFlags = ImportBuffer(residency.InstanceFlagsBuffer);
            RGBufferRef instanceLayerMask = ImportBuffer(residency.InstanceLayerMaskBuffer);
            RGBufferRef instanceSceneCullingMask = ImportBuffer(residency.InstanceSceneCullingMaskBuffer);
            RGBufferRef instanceRenderingLayer = ImportBuffer(residency.InstanceRenderingLayerBuffer);

            var frustumPlanes = m_ObjectPool.GetTempArray<Vector4>(6);
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
                passRef.EnablePassCulling(true);
                passRef.KeepImportedWritesLocal();
                passRef.SetExecuteFunc((in MeshGpuCullPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    MeshDrawGPUBackend.DispatchCullFrustum(
                        cmdEncoder,
                        passData.view,
                        passData.instanceCount,
                        passData.frustumPlanes,
                        passData.boundsCenter,
                        passData.boundsExtent,
                        passData.instanceFlags,
                        passData.instanceLayerMask,
                        passData.instanceSceneCullingMask,
                        passData.instanceRenderingLayer,
                        passData.visibility);
                    passData.world?.MarkGpuCulled(passData.view);
                });
            }

            m_GpuCullVisibility[key] = visibilityRef;
            if (m_DrawListRecords.IsLiveRef(draws))
            {
                m_DrawListRecords.BindGpuVisibility(draws.index, visibilityRef);
            }
        }

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
        public RGTextureRef ImportTexture(RTHandle texture, in int shaderProperty = 0)
        {
            return m_Resources.ImportTexture(texture, shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ImportTexture(in FTextureRef texture, in int shaderProperty = 0)
        {
            return m_Resources.ImportTexture(texture.texture, texture.descriptor, shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ImportBackbuffer(in RenderTargetIdentifier backBuffer, in TextureDescriptor descriptor, in int shaderProperty = 0)
        {
            return m_Resources.ImportBackbuffer(backBuffer, descriptor, shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTexture(in RGTextureRef textureRef, in int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(m_Resources.GetTextureDescriptor(textureRef.handle), shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTexture(in TextureDescriptor descriptor, in int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(descriptor, shaderProperty);
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
                    snapshot.resources.Add(new RGResourceSnapshot
                    {
                        type = (ERGResourceType)type, index = i, name = resourceName, imported = imported, description = description,
                        firstUse = -1, lastUse = -1, createPass = -1, releasePass = -1
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
                    allowCulling = pass.enablePassCulling, waitsForPass = info.syncToPassIndex, signalsToPass = info.syncFromPassIndex,
                    graphicsFence = info.needGraphicsFence, reads = reads, writes = writes, creates = creates, releases = releases
                });

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
            transferPass.index = m_PassList.Count;
            transferPass.customSampler = profilerSampler;
            m_PassList.Add(transferPass);
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
            computePass.index = m_PassList.Count;
            computePass.customSampler = profilerSampler;
            m_PassList.Add(computePass);
            return new RGComputePassRef(computePass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginProfilingSampler(ProfilingSampler sampler)
        {
            if (sampler == null)
            {
                return;
            }

            using (RGComputePassRef passRef = AddComputePass<RGProfilingScopePassData>("BeginProfilingSampler", null))
            {
                ref RGProfilingScopePassData passData = ref passRef.GetPassData<RGProfilingScopePassData>();
                passData.sampler = sampler;
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in RGProfilingScopePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.BeginSample(passData.sampler);
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndProfilingSampler(ProfilingSampler sampler)
        {
            if (sampler == null)
            {
                return;
            }

            using (RGComputePassRef passRef = AddComputePass<RGProfilingScopePassData>("EndProfilingSampler", null))
            {
                ref RGProfilingScopePassData passData = ref passRef.GetPassData<RGProfilingScopePassData>();
                passData.sampler = sampler;
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in RGProfilingScopePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.EndSample(passData.sampler);
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGRayTracingPassRef AddRayTracingPass<T>(ProfilingSampler profilerSampler) where T : struct
        {
            RGRayTracingPass<T> rayTracingPass = m_ObjectPool.Get<RGRayTracingPass<T>>();
            rayTracingPass.Clear();
            rayTracingPass.name = profilerSampler.name;
            rayTracingPass.index = m_PassList.Count;
            rayTracingPass.customSampler = profilerSampler;
            m_PassList.Add(rayTracingPass);
            return new RGRayTracingPassRef(rayTracingPass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGRasterPassRef AddRasterPass<T>(ProfilingSampler profilerSampler) where T : struct
        {
            RGRasterPass<T> rasterPass = m_ObjectPool.Get<RGRasterPass<T>>();
            rasterPass.Clear();
            rasterPass.name = profilerSampler.name;
            rasterPass.index = m_PassList.Count;
            rasterPass.customSampler = profilerSampler;
            m_PassList.Add(rasterPass);
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
        public void Execute(RenderContext renderContext, ResourcePool resourcePool, CommandBuffer cmdBuffer)
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
                    try { m_ObjectPool.ReleaseAllTempAlloc(); }
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

        void QueueGraphics(RenderContext context, CommandBuffer commands)
        {
            try { context.scriptableRenderContext.ExecuteCommandBuffer(commands); }
            catch { m_QueueSubmissionFailed = true; throw; }
        }

        void QueueAsync(RenderContext context, CommandBuffer commands)
        {
            try { context.scriptableRenderContext.ExecuteCommandBufferAsync(commands, ComputeQueueType.Background); }
            catch { m_QueueSubmissionFailed = true; throw; }
        }

        internal void RecoverUnjoinedAsync(RenderContext context, bool forceDrain = false)
        {
            if (!m_UnrecoverableAsyncSubmission && !forceDrain) return;
            if (!m_AsyncComputeSupported) { m_UnrecoverableAsyncSubmission = false; return; }
            var asyncCommands = CommandBufferPool.Get();
            var graphicsCommands = CommandBufferPool.Get();
            try
            {
                asyncCommands.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                graphicsCommands.SetExecutionFlags(CommandBufferExecutionFlags.None);
                GraphicsFence fence = asyncCommands.CreateAsyncGraphicsFence();
                QueueAsync(context, asyncCommands);
                graphicsCommands.WaitOnAsyncGraphicsFence(fence);
                QueueGraphics(context, graphicsCommands);
                m_UnrecoverableAsyncSubmission = false;
            }
            finally
            {
                asyncCommands.Clear(); graphicsCommands.Clear();
                CommandBufferPool.Release(asyncCommands); CommandBufferPool.Release(graphicsCommands);
            }
        }

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
        void InitResourceInfoData(DynamicArray<RGResourceCompileInfo> resourceInfos, in int count)
        {
            resourceInfos.Resize(count);
            for (int i = 0; i < resourceInfos.size; ++i)
            {
                resourceInfos[i].Reset();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitializeCompileData()
        {
            InitResourceInfoData(m_ResourcesCompileInfos[(int)ERGResourceType.Buffer], m_Resources.GetBufferCount());
            InitResourceInfoData(m_ResourcesCompileInfos[(int)ERGResourceType.Texture], m_Resources.GetTextureCount());
            InitResourceInfoData(m_ResourcesCompileInfos[(int)ERGResourceType.AccelerationStructure], 0);

            m_PassCompileInfos.Resize(m_PassList.Count);
            for (int i = 0; i < m_PassCompileInfos.size; ++i)
            {
                m_PassCompileInfos[i].Reset(m_PassList[i], m_AsyncComputeSupported);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CountPassReference()
        {
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];

                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    var resourceRead = passInfo.pass.resourceReadLists[type];
                    foreach (var resource in resourceRead)
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource];
                        info.consumers.Add(passIndex);
                        info.refCount++;
                    }

                    var resourceWrite = passInfo.pass.resourceWriteLists[type];
                    foreach (var resource in resourceWrite)
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource];
                        info.producers.Add(passIndex);
                        passInfo.refCount++;

                        // Writing to an imported texture is considered as a side effect because we don't know what users will do with it outside of render graph.
                        if (passInfo.pass.importedWritesAreSideEffects && m_Resources.IsResourceImported(resource)) {
                            passInfo.hasSideEffect = true;
                        }
                    }

                    foreach (int resourceIndex in passInfo.pass.temporalResourceList[type])
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resourceIndex];
                        info.refCount++;
                        info.consumers.Add(passIndex);
                        info.producers.Add(passIndex);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CullingUnusedPass()
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                m_CullingStack.Clear();
                DynamicArray<RGResourceCompileInfo> resourceUsageList = m_ResourcesCompileInfos[type];

                // Gather resources that are never read.
                for (int i = 0; i < resourceUsageList.size; ++i)
                {
                    if (resourceUsageList[i].refCount == 0)
                    {
                        m_CullingStack.Push(i);
                    }
                }

                while (m_CullingStack.Count != 0)
                {
                    var unusedResource = resourceUsageList[m_CullingStack.Pop()];
                    foreach (var producerIndex in unusedResource.producers)
                    {
                        ref var producerInfo = ref m_PassCompileInfos[producerIndex];
                        producerInfo.refCount--;
                        if (producerInfo.refCount == 0 && !producerInfo.hasSideEffect && producerInfo.enablePassCulling)
                        {
                            producerInfo.culled = true;
                            foreach (var resourceIndex in producerInfo.pass.resourceReadLists[type])
                            {
                                ref RGResourceCompileInfo resourceInfo = ref resourceUsageList[resourceIndex];
                                resourceInfo.refCount--;
                                // If a resource is not used anymore, add it to the stack to be processed in subsequent iteration.
                                if (resourceInfo.refCount == 0) {
                                    m_CullingStack.Push(resourceIndex);
                                }
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdatePassSynchronization(ref RGPassCompileInfo currentPassInfo, ref RGPassCompileInfo producerPassInfo, in int currentPassIndex, in int lastProducer, ref int intLastSyncIndex)
        {
            // Update latest pass waiting for the other pipe.
            intLastSyncIndex = lastProducer;
            // Current pass needs to wait for pass index lastProducer
            currentPassInfo.syncToPassIndex = lastProducer;

            // Producer will need a graphics fence that this pass will wait on.
            producerPassInfo.needGraphicsFence = true;
            // We update the producer pass with the index of the smallest pass waiting for it.
            // This will be used to "lock" resource from being reused until the pipe has been synchronized.
            if (producerPassInfo.syncFromPassIndex == -1) {
                producerPassInfo.syncFromPassIndex = currentPassIndex;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateResourceSynchronization(ref int lastGraphicsPipeSync, ref int lastComputePipeSync, int currentPassIndex,
            in RGResourceCompileInfo resourceInfo, bool writesResource)
        {
            ref RGPassCompileInfo current = ref m_PassCompileInfos[currentPassIndex];
            int dependency = -1;
            foreach (int writer in resourceInfo.producers)
            {
                if (writer >= currentPassIndex) break;
                if (!m_PassCompileInfos[writer].culled && m_PassCompileInfos[writer].enableAsyncCompute != current.enableAsyncCompute)
                    dependency = writer;
            }
            if (writesResource)
            {
                // A later writer must not overwrite data still sampled on the other queue.
                foreach (int reader in resourceInfo.consumers)
                {
                    if (reader >= currentPassIndex) break;
                    if (!m_PassCompileInfos[reader].culled && m_PassCompileInfos[reader].enableAsyncCompute != current.enableAsyncCompute)
                        dependency = Math.Max(dependency, reader);
                }
            }
            if (dependency < 0) return;
            if (current.enableAsyncCompute)
            {
                if (dependency > lastGraphicsPipeSync)
                    UpdatePassSynchronization(ref current, ref m_PassCompileInfos[dependency], currentPassIndex, dependency, ref lastGraphicsPipeSync);
            }
            else if (dependency > lastComputePipeSync)
                UpdatePassSynchronization(ref current, ref m_PassCompileInfos[dependency], currentPassIndex, dependency, ref lastComputePipeSync);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLatestValidReadIndex(in RGResourceCompileInfo resourceInfo)
        {
            if (resourceInfo.consumers.Count == 0) {
                return -1;
            }

            var consumers = resourceInfo.consumers;
            for (int i = consumers.Count - 1; i >= 0; --i)
            {
                if (!m_PassCompileInfos[consumers[i]].culled) {
                    return consumers[i];
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetFirstValidWriteIndex(in RGResourceCompileInfo resourceInfo)
        {
            if (resourceInfo.producers.Count == 0) {
                return -1;
            }

            var producers = resourceInfo.producers;
            for (int i = 0; i < producers.Count; ++i)
            {
                if (!m_PassCompileInfos[producers[i]].culled) {
                    return producers[i];
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLatestValidWriteIndex(in RGResourceCompileInfo resourceInfo)
        {
            if (resourceInfo.producers.Count == 0) {
                return -1;
            }

            var producers = resourceInfo.producers;
            for (int i = producers.Count - 1; i >= 0; --i)
            {
                if (!m_PassCompileInfos[producers[i]].culled) {
                    return producers[i];
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateResource()
        {
            int lastComputePipeSync = -1;
            int lastGraphicsPipeSync = -1;

            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];

                if (passInfo.culled) { continue; }

                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    var resourcesInfo = m_ResourcesCompileInfos[type];
                    foreach (int resource in passInfo.pass.resourceReadLists[type])
                    {
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resource], false);
                    }

                    foreach (int resource in passInfo.pass.resourceWriteLists[type])
                    {
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resource], true);
                    }
                }
            }

            bool hasAsync = false;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                hasAsync |= !m_PassCompileInfos[passIndex].culled && m_PassCompileInfos[passIndex].enableAsyncCompute;

            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                var resourceInfos = m_ResourcesCompileInfos[type];
                for (int i = 0; i < resourceInfos.size; ++i)
                {
                    ref RGResourceCompileInfo resourceInfo = ref resourceInfos[i];

                    int firstWriteIndex = GetFirstValidWriteIndex(resourceInfo);
                    if (firstWriteIndex != -1) 
                    {
                        m_PassCompileInfos[firstWriteIndex].resourceCreateList[type].Add(i);
                    }

                    int lastReadPassIndex = Math.Max(GetLatestValidReadIndex(resourceInfo), GetLatestValidWriteIndex(resourceInfo));
                    if (lastReadPassIndex != -1)
                    {
                        m_PassCompileInfos[lastReadPassIndex].resourceReleaseList[type].Add(i);
                        if ((hasAsync || m_AsyncComputeSupported && m_Resources.IsPoolTracked(new RGResourceHandle(i, (ERGResourceType)type)))
                            && (!m_Resources.IsResourceImported(new RGResourceHandle(i, (ERGResourceType)type)) || m_Resources.IsPoolTracked(new RGResourceHandle(i, (ERGResourceType)type))))
                        {
                            // Physical pooling can alias otherwise unrelated logical resources.
                            // Keep the last use from each queue with the allocation, so reuse waits
                            // at acquisition instead of extending every lifetime to graph end.
                            foreach (int passIndex in resourceInfo.consumers) RecordLastQueueUse(ref resourceInfo, passIndex);
                            foreach (int passIndex in resourceInfo.producers) RecordLastQueueUse(ref resourceInfo, passIndex);
                            if (resourceInfo.lastGraphicsUse >= 0)
                            {
                                m_PassCompileInfos[resourceInfo.lastGraphicsUse].needGraphicsFence = true;
                                m_PassCompileInfos[resourceInfo.lastGraphicsUse].needResourceFence = true;
                            }
                            if (resourceInfo.lastComputeUse >= 0)
                            {
                                m_PassCompileInfos[resourceInfo.lastComputeUse].needGraphicsFence = true;
                                m_PassCompileInfos[resourceInfo.lastComputeUse].needResourceFence = true;
                            }
                        }
                    }
                }
            }
        }

        void RecordLastQueueUse(ref RGResourceCompileInfo resource, int passIndex)
        {
            ref RGPassCompileInfo pass = ref m_PassCompileInfos[passIndex];
            if (pass.culled) return;
            if (pass.enableAsyncCompute) resource.lastComputeUse = Math.Max(resource.lastComputeUse, passIndex);
            else resource.lastGraphicsUse = Math.Max(resource.lastGraphicsUse, passIndex);
        }

        ResourceReuseFence GetResourceReuseFence(ERGResourceType type, int index)
        {
            ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[(int)type][index];
            return new ResourceReuseFence
            {
                hasGraphics = info.lastGraphicsUse >= 0, hasCompute = info.lastComputeUse >= 0,
                graphics = info.lastGraphicsUse >= 0 ? m_PassCompileInfos[info.lastGraphicsUse].fence : default,
                compute = info.lastComputeUse >= 0 ? m_PassCompileInfos[info.lastComputeUse].fence : default,
                graphicsCompletion = info.lastGraphicsUse >= 0 ? m_PassCompileInfos[info.lastGraphicsUse].completionFence : default,
                computeCompletion = info.lastComputeUse >= 0 ? m_PassCompileInfos[info.lastComputeUse].completionFence : default
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CompileDrawLists()
        {
            // Structural liveness: only DrawLists consumed by non-culled passes are scheduled.
            m_DrawListRecords.ClearConsumers();

            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled)
                {
                    continue;
                }

                List<RGDrawListRef> used = passInfo.pass.usedDrawLists;
                for (int i = 0; i < used.Count; ++i)
                {
                    RGDrawListRef draws = used[i];
                    if (!m_DrawListRecords.IsLiveRef(draws))
                    {
                        continue;
                    }

                    m_DrawListRecords.MarkLiveConsumer(draws.index, passIndex);
                }
            }

            m_DrawListRecords.PrepareLive(m_MeshWorld);
            CompileRendererLists();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CompileRendererLists()
        {
            m_RendererListRecords.ClearConsumers();

            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled)
                {
                    continue;
                }

                List<RGRendererListRef> used = passInfo.pass.usedRendererLists;
                if (used == null)
                {
                    continue;
                }

                for (int i = 0; i < used.Count; ++i)
                {
                    RGRendererListRef list = used[i];
                    if (!m_RendererListRecords.IsLiveRef(list))
                    {
                        continue;
                    }

                    m_RendererListRecords.MarkLiveConsumer(list.index, passIndex);
                }
            }

            m_RendererListRecords.ReleaseUnused();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureDrawListResolved(in RGDrawListRef draws)
        {
            m_DrawListRecords.EnsureResolved(draws);
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

        struct MeshDrawPreparePassData { public RGDrawListContext lists; public int index; }

        static CustomSamplerId MeshDrawGroup(string passName)
        {
            switch (passName)
            {
                case "RasterDepth":
                case "RasterDepthRecovery": return CustomSamplerId.Depth;
                case "RasterGBuffer": return CustomSamplerId.GBuffer;
                case "RasterObjectMotion": return CustomSamplerId.ObjectMotion;
                case "RasterCascadeShadow": return CustomSamplerId.CascadeShadow;
                case "RasterLocalShadow": return CustomSamplerId.LocalShadow;
                default: return CustomSamplerId.Forward;
            }
        }
        RGBufferRef ImportMeshBuffer(ComputeBuffer buffer, string name, ComputeBufferType type = ComputeBufferType.Structured)
            => ImportBuffer(new FBufferRef(-1, new BufferDescriptor(buffer.count, buffer.stride, type) { name = name }, buffer));

        void InsertDrawListPreparation()
        {
            var original = m_ObjectPool.GetTempArray<IRGPass>(m_PassList.Count);
            m_PassList.CopyTo(original);
            var ordered = m_PreparedPasses; ordered.Clear();
            var prepared = m_PreparedDrawLists; prepared.Clear();
            var transformVersions = m_ScheduledMeshVersions;
            for (int passIndex = 0; passIndex < original.Length; ++passIndex)
            {
                IRGPass consumer = original[passIndex];
                if (m_PassCompileInfos[passIndex].culled || consumer.usedDrawLists.Count == 0)
                { ordered.Add(consumer); continue; }
                foreach (var draws in consumer.usedDrawLists)
                {
                    var record = m_DrawListRecords.GetRecordCopy(draws.index);
                    var residency = record.residency;
                    if (!residency.isValid) continue;
                    int versionPassBegin = m_PassList.Count;
                    residency.version.owner.Record(this, residency.version, MeshDrawGPUBackend.shader, transformVersions);
                    for (int i = versionPassBegin; i < m_PassList.Count; ++i) ordered.Add(m_PassList[i]);
                    consumer.AddResourceRead(ImportBuffer(residency.TransformBuffer).handle);
                    consumer.AddResourceRead(ImportBuffer(residency.RenderingLayerBuffer).handle);
                    consumer.AddResourceRead(ImportBuffer(residency.version.generations).handle);
                    var previous = record.pass.previousTransforms.previous;
                    if (previous == null) continue;
                    consumer.AddResourceRead(ImportBuffer(previous.matrices).handle);
                    consumer.AddResourceRead(ImportBuffer(previous.generations).handle);
                }
                var group = ProfilingSampler.Get(MeshDrawGroup(consumer.name));
                BeginProfilingSampler(group);
                ordered.Add(m_PassList[m_PassList.Count - 1]);
                foreach (var draws in consumer.usedDrawLists)
                {
                    if (!prepared.TryGetValue(draws.index, out var outputs))
                    {
                        var record = m_DrawListRecords.PrepareGraphRecord(draws.index);
                        if (record.pipeline == null) continue;
                        bool gpu = record.gpuPayload != null;
                        var metadata = gpu ? ImportMeshBuffer(record.gpuPayload.commandMeta, "MeshDrawMetadata")
                            : record.cpuIndexBuffer.buffer != null ? ImportBuffer(record.cpuIndexBuffer) : default;
                        if (!metadata.IsValid()) { prepared.Add(draws.index, Array.Empty<RGBufferRef>()); continue; }
                        using (var upload = AddTransferPass<MeshDrawPreparePassData>(MeshDrawProfiling.upload))
                        {
                            upload.WriteBuffer(metadata);
                            ref var data = ref upload.GetPassData<MeshDrawPreparePassData>(); data.lists = m_DrawListRecords; data.index = draws.index;
                            upload.SetExecuteFunc((in MeshDrawPreparePassData data, in RGTransferEncoder commands, RGObjectPool pool) => data.lists.Upload(data.index, commands.m_CommandBuffer));
                        }
                        ordered.Add(m_PassList[m_PassList.Count - 1]);
                        if (gpu)
                        {
                            var indices = ImportMeshBuffer(record.gpuPayload.compactedIndices, "MeshDrawCompactedIndices");
                            var counts = ImportMeshBuffer(record.gpuPayload.visibleCounts, "MeshDrawVisibleCounts");
                            var args = ImportMeshBuffer(record.gpuPayload.argsBuffer, "MeshDrawIndirectArgs", ComputeBufferType.IndirectArguments);
                            var candidates = ImportMeshBuffer(record.gpuStaging.candidateTable, "MeshDrawCandidates");
                            var transforms = ImportBuffer(record.residency.InstanceTransformIndexBuffer);
                            using (var compact = AddComputePass<MeshDrawPreparePassData>(MeshDrawProfiling.compaction))
                            {
                                compact.ReadBuffer(metadata); compact.ReadBuffer(candidates); compact.ReadBuffer(transforms);
                                compact.ReadBuffer(record.gpuVisibilityBuffer);
                                compact.WriteBuffer(indices); compact.WriteBuffer(counts); compact.WriteBuffer(args);
                                ref var data = ref compact.GetPassData<MeshDrawPreparePassData>(); data.lists = m_DrawListRecords; data.index = draws.index;
                                compact.SetExecuteFunc((in MeshDrawPreparePassData data, in RGComputeEncoder commands, RGObjectPool pool) => data.lists.Compact(data.index, commands.m_CommandBuffer));
                            }
                            ordered.Add(m_PassList[m_PassList.Count - 1]);
                            outputs = m_ObjectPool.GetTempArray<RGBufferRef>(2);
                            outputs[0] = indices; outputs[1] = args;
                        }
                        else
                        {
                            outputs = m_ObjectPool.GetTempArray<RGBufferRef>(1); outputs[0] = metadata;
                        }
                        prepared.Add(draws.index, outputs);
                    }
                    foreach (var output in outputs) consumer.AddResourceRead(output.handle);
                }
                ordered.Add(consumer);
                EndProfilingSampler(group);
                ordered.Add(m_PassList[m_PassList.Count - 1]);
            }
            m_PassList.Clear(); m_PassList.AddRange(ordered);
            ordered.Clear(); prepared.Clear();
            for (int i = 0; i < m_PassList.Count; ++i) m_PassList[i].index = i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CompilePass()
        {
            InitializeCompileData();
            CountPassReference();
            CullingUnusedPass();
            CompileDrawLists();
            InsertDrawListPreparation();
            InitializeCompileData();
            CountPassReference();
            CullingUnusedPass();
            CompileRendererLists();
            UpdateResource();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ApplyAttachmentClearValues(ref AttachmentDescriptor attachment, Color clearColor)
        {
            attachment.clearColor = clearColor;
            attachment.clearDepth = GraphicsUtility.ClearDepthFar;
            attachment.clearStencil = 0;
        }

        void ClearFallbackColors(CommandBuffer commands, IRGPass pass)
        {
            // Clear each requested color independently before binding the complete MRT.
            // A single MRT clear would overwrite attachments whose load action is Load.
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (!pass.colorBuffers[i].IsValid() || pass.colorBufferActions[i].loadAction != RenderBufferLoadAction.Clear)
                    continue;
                commands.SetRenderTarget(m_Resources.GetTexture(pass.colorBuffers[i]), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                commands.ClearRenderTarget(false, true, GetTextureDescriptor(pass.colorBuffers[i]).clearColor);
            }
        }

        static RenderBufferLoadAction FallbackColorLoad(RenderBufferLoadAction action)
            => action == RenderBufferLoadAction.Clear ? RenderBufferLoadAction.Load : action;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetRenderTarget(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            var pass = passCompileInfo.pass;
            ClearFallbackColors(graphContext.cmdBuffer, pass);
            var depthLoad = pass.depthBufferAction.loadAction == RenderBufferLoadAction.Clear
                ? RenderBufferLoadAction.DontCare : pass.depthBufferAction.loadAction;
            if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
            {
                if (pass.colorBufferMaxIndex > 0)
                {
                    RenderTargetIdentifier[] mrtArray = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(pass.colorBufferMaxIndex + 1);
                    RenderBufferLoadAction[] loadOpArray = graphContext.objectPool.GetTempArray<RenderBufferLoadAction>(pass.colorBufferMaxIndex + 1);
                    RenderBufferStoreAction[] storeOpArray = graphContext.objectPool.GetTempArray<RenderBufferStoreAction>(pass.colorBufferMaxIndex + 1);

                    for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                    {
                        if (!pass.colorBuffers[i].IsValid()) 
                        {
                            throw new InvalidOperationException("MRT setup is invalid. Some indices are not used.");
                        }

                        mrtArray[i] = m_Resources.GetTexture(pass.colorBuffers[i]);
                        loadOpArray[i] = FallbackColorLoad(pass.colorBufferActions[i].loadAction);
                        storeOpArray[i] = pass.colorBufferActions[i].storeAction;
                    }

                    if (pass.depthBuffer.IsValid()) 
                    {
                        RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, m_Resources.GetTexture(pass.depthBuffer), depthLoad, pass.depthBufferAction.storeAction);
                        renderTargetBinding.flags = GetFallbackDepthFlags(pass.depthBufferAccess);
                        graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);
                    } 
                    else 
                    {
                        throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                    }
                } 
                else 
                {
                    if (pass.depthBuffer.IsValid())
                    {
                        RenderTargetIdentifier[] mrtArray = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(1);
                        RenderBufferLoadAction[] loadOpArray = graphContext.objectPool.GetTempArray<RenderBufferLoadAction>(1);
                        RenderBufferStoreAction[] storeOpArray = graphContext.objectPool.GetTempArray<RenderBufferStoreAction>(1);

                        if (pass.colorBufferMaxIndex > -1) 
                        {
                            mrtArray[0] = m_Resources.GetTexture(pass.colorBuffers[0]);
                            loadOpArray[0] = FallbackColorLoad(pass.colorBufferActions[0].loadAction);
                            storeOpArray[0] = pass.colorBufferActions[0].storeAction;

                            RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, m_Resources.GetTexture(pass.depthBuffer), depthLoad, pass.depthBufferAction.storeAction);
                            renderTargetBinding.flags = GetFallbackDepthFlags(pass.depthBufferAccess);
                            graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);
                        } 
                        else
                        {
                            mrtArray[0] = m_Resources.GetTexture(pass.depthBuffer);
                            loadOpArray[0] = depthLoad;
                            storeOpArray[0] = pass.depthBufferAction.storeAction;

                            RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, m_Resources.GetTexture(pass.depthBuffer), depthLoad, pass.depthBufferAction.storeAction);
                            renderTargetBinding.flags = GetFallbackDepthFlags(pass.depthBufferAccess);
                            graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);
                        }
                    } 
                    else 
                    {
                        // Color only. Binding an empty depth identifier through RenderTargetBinding makes Unity
                        // report the target as a missing temporary render texture.
                        graphContext.cmdBuffer.SetRenderTarget(m_Resources.GetTexture(pass.colorBuffers[0]), FallbackColorLoad(pass.colorBufferActions[0].loadAction), pass.colorBufferActions[0].storeAction);
                    }
                }

                if (pass.depthBuffer.IsValid() && pass.depthBufferAction.loadAction == RenderBufferLoadAction.Clear)
                    graphContext.cmdBuffer.ClearRenderTarget(true, false, Color.clear, GraphicsUtility.ClearDepthFar);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static RenderTargetFlags GetFallbackDepthFlags(EDepthAccess access)
        {
            bool readOnly = (access & EDepthAccess.ReadOnly) != 0;
            bool write = (access & EDepthAccess.Write) != 0;
            return readOnly && !write ? RenderTargetFlags.ReadOnlyDepthStencil : RenderTargetFlags.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void BeginRasterPass(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            IRGPass pass = passCompileInfo.pass;
            if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
            {
                if (pass.colorBufferMaxIndex > 0)
                {
                    if (pass.depthBuffer.IsValid())
                    {
                        NativeArray<AttachmentDescriptor> attachmentDescriptors = new NativeArray<AttachmentDescriptor>(pass.colorBufferMaxIndex + 2, Allocator.Temp);
                        for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                        {
                            if (!pass.colorBuffers[i].IsValid())
                            {
                                throw new InvalidOperationException("MRT setup is invalid. Some indices are not used.");
                            }

                            RenderTexture renderBuffer = m_Resources.GetTexture(pass.colorBuffers[i]);

                            AttachmentDescriptor attachmentDescriptor = new AttachmentDescriptor();
                            {
                                attachmentDescriptor.loadAction = pass.colorBufferActions[i].loadAction;
                                attachmentDescriptor.storeAction = pass.colorBufferActions[i].storeAction;
                                attachmentDescriptor.graphicsFormat = renderBuffer.graphicsFormat;
                                attachmentDescriptor.loadStoreTarget = renderBuffer;
                                ApplyAttachmentClearValues(ref attachmentDescriptor, GetTextureDescriptor(pass.colorBuffers[i]).clearColor);
                            }
                            attachmentDescriptors[i] = attachmentDescriptor;
                        }

                        RenderTexture depthBuffer = m_Resources.GetTexture(pass.depthBuffer);
                        AttachmentDescriptor depthAttachmentDescriptor = new AttachmentDescriptor();
                        {
                            depthAttachmentDescriptor.loadAction = pass.depthBufferAction.loadAction;
                            depthAttachmentDescriptor.storeAction = pass.depthBufferAction.storeAction;
                            depthAttachmentDescriptor.graphicsFormat = depthBuffer.depthStencilFormat;
                            depthAttachmentDescriptor.loadStoreTarget = depthBuffer;
                            ApplyAttachmentClearValues(ref depthAttachmentDescriptor, Color.clear);
                        }
                        attachmentDescriptors[pass.colorBufferMaxIndex + 1] = depthAttachmentDescriptor;

                        SubPassDescriptor subPassDescriptor = new SubPassDescriptor();
                        {
                            subPassDescriptor.inputs = AttachmentIndexArray.Emtpy;
                            subPassDescriptor.colorOutputs = new AttachmentIndexArray(pass.colorBufferMaxIndex + 1);

                            for(int i = 0; i < pass.colorBufferMaxIndex + 1; ++i)
                            {
                                subPassDescriptor.colorOutputs[i] = i;
                            }

                            if(pass.depthBufferAccess == EDepthAccess.ReadOnly)
                            {
                                subPassDescriptor.flags = SubPassFlags.ReadOnlyDepthStencil;
                            }
                        }
                        NativeArray<SubPassDescriptor> subPassDescriptors = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
                        {
                            subPassDescriptors[0] = subPassDescriptor;
                        }

                        graphContext.cmdBuffer.BeginRenderPass(depthBuffer.width, depthBuffer.height, 1, attachmentDescriptors, pass.colorBufferMaxIndex + 1, subPassDescriptors);

                        attachmentDescriptors.Dispose();
                        subPassDescriptors.Dispose();
                    }
                    else
                    {
                        throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                    }
                }
                else
                {
                    if (pass.depthBuffer.IsValid())
                    {
                        if (pass.colorBufferMaxIndex > -1)
                        {
                            RenderTexture depthBuffer = m_Resources.GetTexture(pass.depthBuffer);
                            RenderTexture colorBuffer = m_Resources.GetTexture(pass.colorBuffers[0]);

                            if (depthBuffer.width != colorBuffer.width || depthBuffer.height != colorBuffer.height)
                            {
                                Debug.LogError("ColorAttachment size not match DepthAttachment in RenderPass : {pass.name}");
                            }

                            AttachmentDescriptor depthAttachmentDescriptor = new AttachmentDescriptor();
                            {
                                depthAttachmentDescriptor.loadAction = pass.depthBufferAction.loadAction;
                                depthAttachmentDescriptor.storeAction = pass.depthBufferAction.storeAction;
                                depthAttachmentDescriptor.graphicsFormat = depthBuffer.depthStencilFormat;
                                depthAttachmentDescriptor.loadStoreTarget = depthBuffer;
                                ApplyAttachmentClearValues(ref depthAttachmentDescriptor, Color.clear);
                            }
                            AttachmentDescriptor colorAttachmentDescriptor = new AttachmentDescriptor();
                            {
                                colorAttachmentDescriptor.loadAction = pass.colorBufferActions[0].loadAction;
                                colorAttachmentDescriptor.storeAction = pass.colorBufferActions[0].storeAction;
                                colorAttachmentDescriptor.graphicsFormat = colorBuffer.graphicsFormat;
                                colorAttachmentDescriptor.loadStoreTarget = colorBuffer;
                                ApplyAttachmentClearValues(ref colorAttachmentDescriptor, GetTextureDescriptor(pass.colorBuffers[0]).clearColor);
                            }

                            NativeArray<AttachmentDescriptor> attachmentDescriptors = new NativeArray<AttachmentDescriptor>(2, Allocator.Temp);
                            {
                                attachmentDescriptors[0] = colorAttachmentDescriptor;
                                attachmentDescriptors[1] = depthAttachmentDescriptor;
                            }

                            SubPassDescriptor subPassDescriptor = new SubPassDescriptor();
                            {
                                subPassDescriptor.inputs = AttachmentIndexArray.Emtpy;
                                subPassDescriptor.colorOutputs = new AttachmentIndexArray(1);

                                subPassDescriptor.colorOutputs[0] = 0;

                                if (pass.depthBufferAccess == EDepthAccess.ReadOnly)
                                {
                                    subPassDescriptor.flags = SubPassFlags.ReadOnlyDepthStencil;
                                }
                            }
                            NativeArray<SubPassDescriptor> subPassDescriptors = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
                            {
                                subPassDescriptors[0] = subPassDescriptor;
                            }

                            graphContext.cmdBuffer.BeginRenderPass(depthBuffer.width, depthBuffer.height, 1, attachmentDescriptors, 1, subPassDescriptors);

                            attachmentDescriptors.Dispose();
                            subPassDescriptors.Dispose();
                        }
                        else
                        {
                            RenderTexture depthBuffer = m_Resources.GetTexture(pass.depthBuffer);
                            AttachmentDescriptor attachmentDescriptor = new AttachmentDescriptor();
                            {
                                attachmentDescriptor.loadAction = pass.depthBufferAction.loadAction;
                                attachmentDescriptor.storeAction = pass.depthBufferAction.storeAction;
                                attachmentDescriptor.graphicsFormat = depthBuffer.depthStencilFormat;
                                attachmentDescriptor.loadStoreTarget = depthBuffer;
                                ApplyAttachmentClearValues(ref attachmentDescriptor, Color.clear);
                            }
                            NativeArray<AttachmentDescriptor> attachmentDescriptors = new NativeArray<AttachmentDescriptor>(1, Allocator.Temp);
                            {
                                attachmentDescriptors[0] = attachmentDescriptor;
                            }

                            SubPassDescriptor subPassDescriptor = new SubPassDescriptor();
                            {
                                subPassDescriptor.inputs = AttachmentIndexArray.Emtpy;
                                subPassDescriptor.colorOutputs = AttachmentIndexArray.Emtpy;

                                if (pass.depthBufferAccess == EDepthAccess.ReadOnly)
                                {
                                    subPassDescriptor.flags = SubPassFlags.ReadOnlyDepthStencil;
                                }
                            }
                            NativeArray<SubPassDescriptor> subPassDescriptors = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
                            {
                                subPassDescriptors[0] = subPassDescriptor;
                            }

                            graphContext.cmdBuffer.BeginRenderPass(depthBuffer.width, depthBuffer.height, 1, attachmentDescriptors, 0, subPassDescriptors);

                            attachmentDescriptors.Dispose();
                            subPassDescriptors.Dispose();
                        }
                    }
                    else
                    {
                        RenderTexture colorBuffer = m_Resources.GetTexture(pass.colorBuffers[0]);

                        AttachmentDescriptor colorAttachmentDescriptor = new AttachmentDescriptor();
                        {
                            colorAttachmentDescriptor.loadAction = pass.colorBufferActions[0].loadAction;
                            colorAttachmentDescriptor.storeAction = pass.colorBufferActions[0].storeAction;
                            colorAttachmentDescriptor.graphicsFormat = colorBuffer.graphicsFormat;
                            colorAttachmentDescriptor.loadStoreTarget = colorBuffer;
                            ApplyAttachmentClearValues(ref colorAttachmentDescriptor, GetTextureDescriptor(pass.colorBuffers[0]).clearColor);
                        }

                        NativeArray<AttachmentDescriptor> attachmentDescriptors = new NativeArray<AttachmentDescriptor>(1, Allocator.Temp);
                        {
                            attachmentDescriptors[0] = colorAttachmentDescriptor;
                        }

                        SubPassDescriptor subPassDescriptor = new SubPassDescriptor();
                        {
                            subPassDescriptor.inputs = AttachmentIndexArray.Emtpy;
                            subPassDescriptor.colorOutputs = new AttachmentIndexArray(1);

                            subPassDescriptor.colorOutputs[0] = 0;
                        }
                        NativeArray<SubPassDescriptor> subPassDescriptors = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
                        {
                            subPassDescriptors[0] = subPassDescriptor;
                        }

                        graphContext.cmdBuffer.BeginRenderPass(colorBuffer.width, colorBuffer.height, 1, attachmentDescriptors, -1, subPassDescriptors);

                        attachmentDescriptors.Dispose();
                        subPassDescriptors.Dispose();
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EndRasterPass(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            IRGPass pass = passCompileInfo.pass;
            if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
            {
                if (pass.colorBufferMaxIndex > 0)
                {
                    if (pass.depthBuffer.IsValid())
                    {
                        graphContext.cmdBuffer.EndRenderPass();
                    }
                    else
                    {
                        throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                    }
                }
                else
                {
                    if (pass.depthBuffer.IsValid())
                    {
                        if (pass.colorBufferMaxIndex > -1)
                        {
                            graphContext.cmdBuffer.EndRenderPass();
                        }
                        else
                        {
                            graphContext.cmdBuffer.EndRenderPass();
                        }
                    }
                    else
                    {
                        graphContext.cmdBuffer.EndRenderPass();
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PrePassExecute(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            IRGPass pass = passCompileInfo.pass;

            if (passCompileInfo.syncToPassIndex != -1)
            {
                ref RGPassCompileInfo producerInfo = ref m_PassCompileInfos[passCompileInfo.syncToPassIndex];
                if (producerInfo.needGraphicsFence)
                {
                    graphContext.cmdBuffer.WaitOnAsyncGraphicsFence(producerInfo.fence);
                }
            }

            foreach (var bufferHandle in passCompileInfo.resourceCreateList[(int)ERGResourceType.Buffer])
            {
                m_Resources.CreateBufferResource(ref graphContext, bufferHandle, passCompileInfo.enableAsyncCompute);
            }

            foreach (var textureHandle in passCompileInfo.resourceCreateList[(int)ERGResourceType.Texture])
            {
                m_Resources.CreateTextureResource(ref graphContext, textureHandle, passCompileInfo.enableAsyncCompute);
            }

            if (pass.passType == EPassType.Raster)
            {
                if (pass.enableNativeRenderPass)
                {
                    BeginRasterPass(ref graphContext, passCompileInfo);
                }
                else
                {
                    SetRenderTarget(ref graphContext, passCompileInfo);
                }
                graphContext.cmdBuffer.DisableScissorRect();
                RGTextureRef viewportTarget = pass.colorBufferMaxIndex >= 0 ? pass.colorBuffers[0] : pass.depthBuffer;
                if (viewportTarget.IsValid())
                {
                    RenderTexture target = m_Resources.GetTexture(viewportTarget).rt;
                    if (target != null)
                        graphContext.cmdBuffer.SetViewport(new Rect(0, 0, GetTextureDescriptor(viewportTarget).width, GetTextureDescriptor(viewportTarget).height));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PostPassExecute(ref RGContext graphContext, ref RGPassCompileInfo passCompileInfo)
        {
            IRGPass pass = passCompileInfo.pass;

            switch (pass.passType)
            {
                case EPassType.Raster:
                    if (pass.enableNativeRenderPass)
                    {
                        EndRasterPass(ref graphContext, passCompileInfo);
                    }
                    break;
            }

            if (passCompileInfo.needGraphicsFence)
            {
                passCompileInfo.fence = graphContext.cmdBuffer.CreateAsyncGraphicsFence();
            }
            if (passCompileInfo.needResourceFence)
                passCompileInfo.completionFence = graphContext.cmdBuffer.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);

        }

        void ReleasePassResources(ref RGPassCompileInfo passCompileInfo)
        {
            m_ObjectPool.ReleaseAllTempAlloc();

            foreach (var buffer in passCompileInfo.resourceReleaseList[(int)ERGResourceType.Buffer]) 
            {
                m_Resources.ReleaseBufferResource(buffer, GetResourceReuseFence(ERGResourceType.Buffer, buffer));
            }

            foreach (var texture in passCompileInfo.resourceReleaseList[(int)ERGResourceType.Texture]) 
            {
                m_Resources.ReleaseTextureResource(texture, GetResourceReuseFence(ERGResourceType.Texture, texture));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ExecutePass(ref RGContext graphContext)
        {
            CommandBuffer graphicsCmdBuffer = graphContext.cmdBuffer;
            bool hasQueuedAsync = false;
            GraphicsFence lastAsyncFence = default;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            try
            {
                for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                {
                    ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                    if (passInfo.culled) 
                    { 
                        continue; 
                    }

                    if (!passInfo.pass.hasExecuteAction) 
                    {
                        throw new InvalidOperationException(string.Format("RenderPass {0} was not provided with an execute function.", passInfo.pass.name));
                    }

                    CommandBuffer asyncCommands = null;
                    try
                    {
                        IRGPass pass = passInfo.pass;
                        graphContext.cmdBuffer = graphicsCmdBuffer;

                        if (passInfo.enableAsyncCompute)
                        {
                            asyncCommands = CommandBufferPool.Get();
                            asyncCommands.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                            graphContext.cmdBuffer = asyncCommands;
                            // Imported resources can be reused across camera graphs. Establish ordering
                            // against all earlier graphics work before this graph's first async submit.
                            if (!hasQueuedAsync)
                            {
                                GraphicsFence entryFence = graphicsCmdBuffer.CreateAsyncGraphicsFence();
                                asyncCommands.WaitOnAsyncGraphicsFence(entryFence);
                            }
                        }

                        ProfilingScope profilingScope = default;
                        if (pass.customSampler != null)
                        {
                            profilingScope = new ProfilingScope(graphContext.cmdBuffer, pass.customSampler);
                        }
                        try
                        {
                            PrePassExecute(ref graphContext, passInfo);
                            pass.Execute(ref graphContext);
                            PostPassExecute(ref graphContext, ref passInfo);
                        }
                        finally
                        {
                            profilingScope.Dispose();
                        }

                        RenderFaultValidation.AtPass(ERenderFaultPoint.BeforePassQueue, pass.name);
                        var useFence = new ResourceReuseFence
                        {
                            hasGraphics = !passInfo.enableAsyncCompute && passInfo.needGraphicsFence,
                            hasCompute = passInfo.enableAsyncCompute && passInfo.needGraphicsFence,
                            graphics = passInfo.fence, compute = passInfo.fence,
                            graphicsCompletion = passInfo.completionFence, computeCompletion = passInfo.completionFence
                        };
                        m_Resources.QueueAttempt(pass, passInfo.enableAsyncCompute, useFence);
                        if (passInfo.enableAsyncCompute)
                        {
                            GraphicsFence completionFence = passInfo.needGraphicsFence
                                ? passInfo.fence : asyncCommands.CreateAsyncGraphicsFence();
                            if (!hasQueuedAsync)
                            {
                                QueueGraphics(graphContext.renderContext, graphicsCmdBuffer);
                                graphicsCmdBuffer.Clear();
                            }
                            try
                            {
                                QueueAsync(graphContext.renderContext, asyncCommands);
                                RenderFaultValidation.AtPass(ERenderFaultPoint.AfterPassQueue, pass.name);
                                lastAsyncFence = completionFence;
                                hasQueuedAsync = true;
                            }
                            catch
                            {
                                pass.queueObserver?.OnQueueFailed();
                                // A fresh fence on the same async queue covers both accepted and rejected
                                // outcomes without waiting on a fence that may never have been queued.
                                var drain = CommandBufferPool.Get();
                                try
                                {
                                    drain.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                                    GraphicsFence drainFence = drain.CreateAsyncGraphicsFence();
                                    QueueAsync(graphContext.renderContext, drain);
                                    lastAsyncFence = drainFence;
                                    hasQueuedAsync = true;
                                }
                                catch (Exception drainError)
                                {
                                    m_UnrecoverableAsyncSubmission = true;
                                    Debug.LogException(drainError);
                                }
                                finally { drain.Clear(); CommandBufferPool.Release(drain); }
                                throw;
                            }
                        }
                        else
                        {
                            try
                            {
                                QueueGraphics(graphContext.renderContext, graphContext.cmdBuffer);
                                RenderFaultValidation.AtPass(ERenderFaultPoint.AfterPassQueue, pass.name);
                            }
                            catch { m_QueueSubmissionFailed = true; pass.queueObserver?.OnQueueFailed(); throw; }
                            graphContext.cmdBuffer.Clear();
                        }
                        pass.queueObserver?.OnQueued();
                        ReleasePassResources(ref passInfo);
                    }
                    finally
                    {
                        if (asyncCommands != null)
                        {
                            asyncCommands.Clear();
                            CommandBufferPool.Release(asyncCommands);
                        }
                        graphContext.cmdBuffer = graphicsCmdBuffer;
                    }
                }

            }
            catch (Exception error)
            {
                m_Resources.SubmissionUncertain();
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
            }
            finally
            {
                graphicsCmdBuffer.Clear();
                if (hasQueuedAsync)
                {
                    try
                    {
                        // A later pass failure does not cancel earlier async work. Join it before
                        // another camera uploads into shared native buffers or changes cache content.
                        graphicsCmdBuffer.WaitOnAsyncGraphicsFence(lastAsyncFence);
                        QueueGraphics(graphContext.renderContext, graphicsCmdBuffer);
                    }
                    catch (Exception joinError)
                    {
                        m_UnrecoverableAsyncSubmission = true;
                        if (failure == null) failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(joinError);
                        else Debug.LogException(joinError);
                    }
                    finally { graphicsCmdBuffer.Clear(); }
                }
            }
            failure?.Throw();
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
