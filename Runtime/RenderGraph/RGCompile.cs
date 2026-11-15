using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Core;

namespace InfinityTech.Rendering.RenderGraph
{
    // One instance per builder. Contribute runs once per compile after liveness and does not reorder passes.
    // A declared pass is inserted by PlaceDeclaredPasses before liveness. Clear runs whenever the recorded graph is dropped.
    internal interface IRGGraphExtension
    {
        void Contribute(RGBuilder builder);
        void Clear();
    }

    sealed class RGProfileScope
    {
        public ProfilingSampler sampler;
        public readonly List<IRGPass> passes = new List<IRGPass>(8);
    }

    internal struct RGGroupAttachment
    {
        public RGTextureHandle textureHandle;
        public RenderBufferLoadAction load;
        public RenderBufferStoreAction store;
        public bool depth;
    }

    internal struct RGGroupSubpass
    {
        public int passIndex;
        public bool readOnlyDepth;
        public byte colorCount;
        public byte c0, c1, c2, c3, c4, c5, c6, c7;

        public int Color(int index)
        {
            switch (index)
            {
                case 0:
                    return c0;
                case 1:
                    return c1;
                case 2:
                    return c2;
                case 3:
                    return c3;
                case 4:
                    return c4;
                case 5:
                    return c5;
                case 6:
                    return c6;
                default:
                    return c7;
            }
        }

        public void SetColor(int index, int attachment)
        {
            byte value = (byte)attachment;
            switch (index)
            {
                case 0:
                    c0 = value;
                    break;
                case 1:
                    c1 = value;
                    break;
                case 2:
                    c2 = value;
                    break;
                case 3:
                    c3 = value;
                    break;
                case 4:
                    c4 = value;
                    break;
                case 5:
                    c5 = value;
                    break;
                case 6:
                    c6 = value;
                    break;
                default:
                    c7 = value;
                    break;
            }
        }
    }

    internal sealed class RGNativePassGroup
    {
        public int firstPass = -1;
        public int lastPass = -1;
        public int width, height, samples = 1;
        public int depthAttachment = -1;
        public readonly List<RGGroupAttachment> attachments = new List<RGGroupAttachment>(4);
        public readonly List<RGGroupSubpass> subpasses = new List<RGGroupSubpass>(4);

        public void Clear()
        {
            firstPass = lastPass = -1;
            width = height = 0;
            samples = 1;
            depthAttachment = -1;
            attachments.Clear();
            subpasses.Clear();
        }
    }

    public partial class RGBuilder
    {
        struct RGCullItem
        {
            public int type;
            public int index;
        }

        readonly List<RGCullItem> m_CullStack = new List<RGCullItem>(32);
        readonly List<(int first, int last)> m_SegmentRanges = new List<(int first, int last)>(8);
        readonly List<int> m_ProfileIndices = new List<int>(8);
        readonly List<int> m_SegmentPasses = new List<int>(16);
        // Physical keys released so far in the open segment.
        readonly HashSet<long> m_SegmentReleases = new HashSet<long>();
        // (effective create pass, physical key) of every created resource, ordered by pass.
        readonly List<(int pass, long key)> m_SegmentCreates = new List<(int pass, long key)>(32);
        static readonly Comparison<(int pass, long key)> s_ByCreatePass = (a, b) => a.pass.CompareTo(b.pass);
        readonly int[][] m_PassLocalOwners = { Array.Empty<int>(), Array.Empty<int>() };
        readonly Stack<RGProfileScope> m_ScopePool = new Stack<RGProfileScope>(8);

        static bool IsAttachmentRaster(IRGPass pass)
        {
            if (pass.passType != EPassType.Raster || !pass.enableNativeRenderPass)
            {
                return false;
            }
            return pass.depthBufferHandle.IsValid() || pass.colorBufferMaxIndex >= 0;
        }

        static void Ensure(ref int[] values, int count)
        {
            if (values.Length < count)
            {
                values = new int[Mathf.NextPowerOfTwo(Mathf.Max(count, 1))];
            }
        }

        static bool PassTouches(List<RGResourceHandle>[] lists, in RGResourceHandle handle)
        {
            List<RGResourceHandle> list = lists[(int)handle.type];
            for (int i = 0; i < list.Count; ++i)
            {
                if (list[i].Equals(handle))
                {
                    return true;
                }
            }
            return false;
        }

        // Declared passes stay out of the recorded sequence. Each one is inserted after every pass that writes an
        // input, at the start of the native group that contains its first consumer, so it does not split that group.
        // A pass with no consumer is inserted after its inputs and then follows ordinary culling.
        void PlaceDeclaredPasses()
        {
            int declared = m_DeclaredPasses.Count;
            if (declared == 0)
            {
                return;
            }
            int sequenced = m_PassList.Count;
            Ensure(ref m_ProvisionalGroupStart, sequenced);
            Ensure(ref m_DeclaredInsertAt, declared);
            Ensure(ref m_DeclaredGroupLimit, declared);
            Ensure(ref m_DeclaredOrder, declared);
            BuildProvisionalGroupStarts(sequenced);

            for (int i = 0; i < declared; ++i)
            {
                m_DeclaredOrder[i] = i;
                IRGPass pass = m_DeclaredPasses[i];
                int ready = -1;
                RGResourceHandle blockedHandle = default;
                int blockedWriter = -1;
                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    List<RGResourceHandle> reads = pass.resourceReadLists[type];
                    for (int r = 0; r < reads.Count; ++r)
                    {
                        RGResourceHandle handle = reads[r];
                        for (int p = sequenced - 1; p >= 0; --p)
                        {
                            if (!PassTouches(m_PassList[p].resourceWriteLists, handle))
                            {
                                continue;
                            }
                            if (p > ready)
                            {
                                ready = p;
                                blockedHandle = handle;
                                blockedWriter = p;
                            }
                            break;
                        }
                    }
                }
                int consumer = -1;
                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    List<RGResourceHandle> writes = pass.resourceWriteLists[type];
                    for (int w = 0; w < writes.Count; ++w)
                    {
                        RGResourceHandle handle = writes[w];
                        for (int p = 0; p < sequenced; ++p)
                        {
                            if (!PassTouches(m_PassList[p].resourceReadLists, handle))
                            {
                                continue;
                            }
                            if (consumer < 0 || p < consumer)
                            {
                                consumer = p;
                            }
                            break;
                        }
                    }
                }
                if (consumer < 0)
                {
                    m_DeclaredGroupLimit[i] = -1;
                    m_DeclaredInsertAt[i] = ready + 1;
                    continue;
                }
                int limit = m_ProvisionalGroupStart[consumer];
                if (limit < 0)
                {
                    limit = consumer;
                }
                m_DeclaredGroupLimit[i] = limit;
                if (ready >= limit)
                {
                    throw new InvalidOperationException(
                        $"'{PassLabel(pass)}' reads '{m_Resources.GetResourceName(blockedHandle)}' written by '{PassLabel(m_PassList[blockedWriter])}', which is inside or after the native group of '{PassLabel(m_PassList[consumer])}'.");
                }
                m_DeclaredInsertAt[i] = limit;
            }

            for (int guard = 0; guard <= declared; ++guard)
            {
                bool moved = false;
                for (int i = 0; i < declared; ++i)
                {
                    IRGPass pass = m_DeclaredPasses[i];
                    for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                    {
                        List<RGResourceHandle> reads = pass.resourceReadLists[type];
                        for (int r = 0; r < reads.Count; ++r)
                        {
                            RGResourceHandle handle = reads[r];
                            for (int j = 0; j < declared; ++j)
                            {
                                if (j == i || !PassTouches(m_DeclaredPasses[j].resourceWriteLists, handle))
                                {
                                    continue;
                                }
                                int limit = m_DeclaredGroupLimit[i];
                                if (limit >= 0)
                                {
                                    if (m_DeclaredInsertAt[j] > limit || (m_DeclaredInsertAt[j] == limit && j > i))
                                    {
                                        throw new InvalidOperationException(
                                            $"'{PassLabel(pass)}' reads '{m_Resources.GetResourceName(handle)}' written by '{PassLabel(m_DeclaredPasses[j])}', which cannot be placed before the native group of '{PassLabel(pass)}'s first consumer.");
                                    }
                                }
                                else if (m_DeclaredInsertAt[i] <= m_DeclaredInsertAt[j])
                                {
                                    m_DeclaredInsertAt[i] = m_DeclaredInsertAt[j] + 1;
                                    moved = true;
                                }
                            }
                        }
                    }
                }
                if (!moved)
                {
                    break;
                }
                if (guard == declared)
                {
                    throw new InvalidOperationException("Declared passes read each other's outputs in a cycle.");
                }
            }

            for (int i = 1; i < declared; ++i)
            {
                int key = m_DeclaredOrder[i];
                int at = m_DeclaredInsertAt[key];
                int j = i - 1;
                while (j >= 0)
                {
                    int other = m_DeclaredOrder[j];
                    if (m_DeclaredInsertAt[other] < at || (m_DeclaredInsertAt[other] == at && other < key))
                    {
                        break;
                    }
                    m_DeclaredOrder[j + 1] = other;
                    j--;
                }
                m_DeclaredOrder[j + 1] = key;
            }

            int end = sequenced;
            for (int i = 0; i < declared; ++i)
            {
                if (m_DeclaredInsertAt[i] > end)
                {
                    end = m_DeclaredInsertAt[i];
                }
            }
            int needed = sequenced + declared;
            if (m_PlacedPasses.Capacity < needed)
            {
                m_PlacedPasses.Capacity = needed;
            }
            if (m_PassList.Capacity < needed)
            {
                m_PassList.Capacity = needed;
            }
            m_PlacedPasses.Clear();
            int cursor = 0;
            for (int i = 0; i <= end; ++i)
            {
                while (cursor < declared && m_DeclaredInsertAt[m_DeclaredOrder[cursor]] == i)
                {
                    m_PlacedPasses.Add(m_DeclaredPasses[m_DeclaredOrder[cursor]]);
                    cursor++;
                }
                if (i < sequenced)
                {
                    m_PlacedPasses.Add(m_PassList[i]);
                }
            }
            m_PassList.Clear();
            for (int i = 0; i < m_PlacedPasses.Count; ++i)
            {
                IRGPass pass = m_PlacedPasses[i];
                pass.index = i;
                m_PassList.Add(pass);
            }
            m_DeclaredPasses.Clear();
        }

        RGNativePassGroup RentProvisionalGroup()
        {
            for (int i = 0; i < m_ProvisionalGroups.Count; ++i)
            {
                RGNativePassGroup group = m_ProvisionalGroups[i];
                if (group.firstPass < 0 && group.attachments.Count == 0)
                {
                    return group;
                }
            }
            var created = new RGNativePassGroup();
            m_ProvisionalGroups.Add(created);
            return created;
        }

        static string PassLabel(IRGPass pass) => string.IsNullOrEmpty(pass.name) ? "declared pass" : pass.name;

        void BuildProvisionalGroupStarts(int count)
        {
            for (int i = 0; i < m_ProvisionalGroups.Count; ++i)
            {
                m_ProvisionalGroups[i].Clear();
            }
            RGNativePassGroup current = null;
            for (int passIndex = 0; passIndex < count; ++passIndex)
            {
                m_ProvisionalGroupStart[passIndex] = -1;
                IRGPass pass = m_PassList[passIndex];
                if (!IsAttachmentRaster(pass))
                {
                    current = null;
                    continue;
                }
                if (current == null || !TryJoinPass(current, pass))
                {
                    current = RentProvisionalGroup();
                    current.Clear();
                    if (!TryJoinPass(current, pass))
                    {
                        current = null;
                        continue;
                    }
                }
                if (current.firstPass < 0)
                {
                    current.firstPass = passIndex;
                }
                m_ProvisionalGroupStart[passIndex] = current.firstPass;
            }
        }

        void CompilePass()
        {
            PlaceDeclaredPasses();
            RunLiveness();
            if (!m_ExtensionContributed)
            {
                extension?.Contribute(this);
                m_ExtensionContributed = true;
            }
            CompileRendererLists();
            UpdateResource();
            BuildNativeGroups();
            BuildSegments();
            ConformGroupsToSegments();
            HoistGroupCreates();
            ApplyMemoryless();
            BakeProfileMarkers();
            ValidateGraph();
        }

        void RunLiveness()
        {
            InitializeCompileData();
            CountPassReference();
            CullingUnusedPass();
        }

        void InitializeCompileData()
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                InitResourceInfoData(m_ResourcesCompileInfos[type], m_Resources.GetResourceCount(type));

            m_PassCompileInfos.Resize(m_PassList.Count);
            for (int i = 0; i < m_PassCompileInfos.size; ++i)
                m_PassCompileInfos[i].Reset(m_PassList[i], m_AsyncComputeSupported);
        }

        void InitResourceInfoData(DynamicArray<RGResourceCompileInfo> resourceInfos, int count)
        {
            resourceInfos.Resize(count);
            for (int i = 0; i < resourceInfos.size; ++i)
                resourceInfos[i].Reset();
        }

        void CountPassReference()
        {
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (!passInfo.pass.enablePassCulling)
                {
                    passInfo.hasSideEffect = true;
                }

                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    foreach (var resource in passInfo.pass.resourceReadLists[type])
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource.index];
                        info.consumers.Add(passIndex);
                        info.refCount++;
                    }

                    foreach (var resource in passInfo.pass.resourceWriteLists[type])
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource.index];
                        info.producers.Add(passIndex);
                        passInfo.refCount++;
                        if (passInfo.pass.importedWritesAreSideEffects && m_Resources.IsResourceImported(resource))
                            passInfo.hasSideEffect = true;
                    }

                    foreach (RGResourceHandle temporalHandle in passInfo.pass.temporalResourceList[type])
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][temporalHandle.index];
                        info.refCount++;
                        info.consumers.Add(passIndex);
                        info.producers.Add(passIndex);
                    }
                }
            }
        }

        void CullingUnusedPass()
        {
            m_CullStack.Clear();
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                DynamicArray<RGResourceCompileInfo> resources = m_ResourcesCompileInfos[type];
                for (int i = 0; i < resources.size; ++i)
                {
                    if (resources[i].refCount == 0)
                        m_CullStack.Add(new RGCullItem { type = type, index = i });
                }
            }

            for (int cursor = 0; cursor < m_CullStack.Count; ++cursor)
            {
                RGCullItem item = m_CullStack[cursor];
                ref RGResourceCompileInfo unused = ref m_ResourcesCompileInfos[item.type][item.index];
                if (unused.cullClosed)
                {
                    continue;
                }
                unused.cullClosed = true;
                foreach (int producerIndex in unused.producers)
                {
                    ref RGPassCompileInfo producer = ref m_PassCompileInfos[producerIndex];
                    producer.refCount--;
                    if (producer.culled || producer.refCount != 0 || producer.hasSideEffect || !producer.enablePassCulling)
                        continue;
                    producer.culled = true;
                    producer.cullReason = "unused output";
                    IRGPass pass = producer.pass;
                    for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                    {
                        foreach (RGResourceHandle resourceHandle in pass.resourceReadLists[type])
                            CloseRead(type, resourceHandle.index);
                        foreach (RGResourceHandle resourceHandle in pass.temporalResourceList[type])
                            CloseRead(type, resourceHandle.index);
                    }
                }
            }
        }

        void CloseRead(int type, int index)
        {
            ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][index];
            info.refCount--;
            if (info.refCount == 0 && !info.cullClosed)
                m_CullStack.Add(new RGCullItem { type = type, index = index });
        }

        void UpdatePassSynchronization(ref RGPassCompileInfo currentPassInfo, ref RGPassCompileInfo producerPassInfo, int currentPassIndex, int lastProducer, ref int lastSyncIndex)
        {
            lastSyncIndex = lastProducer;
            currentPassInfo.syncToPassIndex = lastProducer;
            producerPassInfo.needGraphicsFence = true;
            if (producerPassInfo.syncFromPassIndex == -1)
                producerPassInfo.syncFromPassIndex = currentPassIndex;
        }

        void UpdateResourceSynchronization(ref int lastGraphicsPipeSync, ref int lastComputePipeSync, int currentPassIndex, in RGResourceCompileInfo resourceInfo, bool writesResource)
        {
            ref RGPassCompileInfo current = ref m_PassCompileInfos[currentPassIndex];
            int dependency = -1;
            foreach (int writer in resourceInfo.producers)
            {
                if (writer >= currentPassIndex)
                {
                    break;
                }
                if (!m_PassCompileInfos[writer].culled && m_PassCompileInfos[writer].enableAsyncCompute != current.enableAsyncCompute)
                    dependency = writer;
            }
            if (writesResource)
            {
                foreach (int reader in resourceInfo.consumers)
                {
                    if (reader >= currentPassIndex)
                    {
                        break;
                    }
                    if (!m_PassCompileInfos[reader].culled && m_PassCompileInfos[reader].enableAsyncCompute != current.enableAsyncCompute)
                        dependency = Math.Max(dependency, reader);
                }
            }
            if (dependency < 0)
            {
                return;
            }
            if (current.enableAsyncCompute)
            {
                if (dependency > lastGraphicsPipeSync)
                    UpdatePassSynchronization(ref current, ref m_PassCompileInfos[dependency], currentPassIndex, dependency, ref lastGraphicsPipeSync);
            }
            else if (dependency > lastComputePipeSync)
                UpdatePassSynchronization(ref current, ref m_PassCompileInfos[dependency], currentPassIndex, dependency, ref lastComputePipeSync);
        }

        int GetLatestValidReadIndex(in RGResourceCompileInfo resourceInfo)
        {
            for (int i = resourceInfo.consumers.Count - 1; i >= 0; --i)
            {
                if (!m_PassCompileInfos[resourceInfo.consumers[i]].culled)
                    return resourceInfo.consumers[i];
            }
            return -1;
        }

        int GetFirstValidWriteIndex(in RGResourceCompileInfo resourceInfo)
        {
            for (int i = 0; i < resourceInfo.producers.Count; ++i)
            {
                if (!m_PassCompileInfos[resourceInfo.producers[i]].culled)
                    return resourceInfo.producers[i];
            }
            return -1;
        }

        int GetLatestValidWriteIndex(in RGResourceCompileInfo resourceInfo)
        {
            for (int i = resourceInfo.producers.Count - 1; i >= 0; --i)
            {
                if (!m_PassCompileInfos[resourceInfo.producers[i]].culled)
                    return resourceInfo.producers[i];
            }
            return -1;
        }

        int LastUse(in RGResourceCompileInfo resourceInfo)
        {
            return Math.Max(GetLatestValidReadIndex(resourceInfo), GetLatestValidWriteIndex(resourceInfo));
        }

        void UpdateResource()
        {
            int lastComputePipeSync = -1;
            int lastGraphicsPipeSync = -1;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled)
                {
                    continue;
                }
                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    var resourcesInfo = m_ResourcesCompileInfos[type];
                    foreach (RGResourceHandle resourceHandle in passInfo.pass.resourceReadLists[type])
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resourceHandle.index], false);
                    foreach (RGResourceHandle resourceHandle in passInfo.pass.resourceWriteLists[type])
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resourceHandle.index], true);
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
                        m_PassCompileInfos[firstWriteIndex].resourceCreateList[type].Add(i);

                    int lastUse = LastUse(resourceInfo);
                    if (lastUse == -1)
                    {
                        continue;
                    }
                    m_PassCompileInfos[lastUse].resourceReleaseList[type].Add(i);
                    var handle = m_Resources.LiveHandle((ERGResourceType)type, i);
                    if ((hasAsync || m_AsyncComputeSupported && m_Resources.IsPoolTracked(handle))
                        && (!m_Resources.IsResourceImported(handle) || m_Resources.IsPoolTracked(handle)))
                    {
                        foreach (int passIndex in resourceInfo.consumers)
                        {
                            RecordLastQueueUse(ref resourceInfo, passIndex);
                        }
                        foreach (int passIndex in resourceInfo.producers)
                        {
                            RecordLastQueueUse(ref resourceInfo, passIndex);
                        }
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

        void RecordLastQueueUse(ref RGResourceCompileInfo resource, int passIndex)
        {
            ref RGPassCompileInfo pass = ref m_PassCompileInfos[passIndex];
            if (pass.culled)
            {
                return;
            }
            if (pass.enableAsyncCompute)
            {
                resource.lastComputeUse = Math.Max(resource.lastComputeUse, passIndex);
            }
            else
            {
                resource.lastGraphicsUse = Math.Max(resource.lastGraphicsUse, passIndex);
            }
        }

        internal ResourceReuseFence GetResourceReuseFence(ERGResourceType type, int index)
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
                List<RGRendererListHandle> used = passInfo.pass.usedRendererLists;
                if (used == null)
                {
                    continue;
                }
                for (int i = 0; i < used.Count; ++i)
                {
                    RGRendererListHandle listHandle = used[i];
                    if (m_RendererListRecords.IsLive(listHandle))
                        m_RendererListRecords.MarkLiveConsumer(listHandle.index, passIndex);
                }
            }
            m_RendererListRecords.ReleaseUnused();
        }

        bool TryAttachmentSize(in RGTextureHandle textureHandle, out int width, out int height, out int samples)
        {
            TextureDescriptor descriptor = m_Resources.GetTextureDescriptor(textureHandle.handle);
            width = descriptor.physicalWidth;
            height = descriptor.physicalHeight;
            samples = Math.Max(1, (int)descriptor.msaaSamples);
            return width > 0 && height > 0;
        }

        static bool SameResource(in RGTextureHandle aHandle, in RGTextureHandle bHandle)
        {
            return aHandle.IsValid() && aHandle.handle.Equals(bHandle.handle);
        }

        bool Samples(IRGPass pass, in RGTextureHandle textureHandle)
        {
            foreach (RGResourceHandle handle in pass.sampledResources)
            {
                if (handle.Equals(textureHandle.handle))
                    return true;
            }
            return false;
        }

        bool IsNativeRaster(int passIndex)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            if (info.culled || info.pass.passType != EPassType.Raster || !info.pass.enableNativeRenderPass)
            {
                return false;
            }
            return info.pass.depthBufferHandle.IsValid() || info.pass.colorBufferMaxIndex >= 0;
        }

        int FindAttachment(RGNativePassGroup group, in RGTextureHandle textureHandle)
        {
            for (int i = 0; i < group.attachments.Count; ++i)
                if (SameResource(group.attachments[i].textureHandle, textureHandle))
                {
                    return i;
                }
            return -1;
        }

        // BeginRenderPass asserts numAttachments <= kMaxSupportedRenderTargets. Depth counts.
        const int kMaxNativePassAttachments = 8;

        bool TryAddAttachment(RGNativePassGroup group, in RGTextureHandle textureHandle, RenderBufferLoadAction load, RenderBufferStoreAction store, bool depth, out int attachmentIndex)
        {
            attachmentIndex = FindAttachment(group, textureHandle);
            if (attachmentIndex >= 0)
            {
                if (depth && group.depthAttachment != attachmentIndex)
                {
                    return false;
                }
                return true;
            }
            if (group.attachments.Count >= kMaxNativePassAttachments)
            {
                return false;
            }
            if (!TryAttachmentSize(textureHandle, out int width, out int height, out int samples))
            {
                return false;
            }
            if (group.attachments.Count == 0)
            {
                group.width = width;
                group.height = height;
                group.samples = samples;
            }
            else if (width != group.width || height != group.height || samples != group.samples)
                return false;
            if (depth && group.depthAttachment >= 0)
            {
                return false;
            }
            attachmentIndex = group.attachments.Count;
            group.attachments.Add(new RGGroupAttachment { textureHandle = textureHandle, load = load, store = store, depth = depth });
            if (depth)
            {
                group.depthAttachment = attachmentIndex;
            }
            return true;
        }

        bool TryJoinGroup(RGNativePassGroup group, int passIndex)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            if (info.segment >= 0 && group.subpasses.Count > 0)
            {
                int firstSegment = m_PassCompileInfos[group.subpasses[0].passIndex].segment;
                if (info.segment != firstSegment)
                {
                    return false;
                }
            }
            return TryJoinPass(group, info.pass);
        }

        // Attachment rules shared by the compiled groups and the provisional groups that place declared passes.
        // A failure returns before any attachment is added.
        bool TryJoinPass(RGNativePassGroup group, IRGPass pass)
        {
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (!pass.colorBuffersHandle[i].IsValid())
                {
                    return false;
                }
            int additional = 0;
            if (pass.depthBufferHandle.IsValid() && FindAttachment(group, pass.depthBufferHandle) < 0)
            {
                additional++;
            }
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (FindAttachment(group, pass.colorBuffersHandle[i]) < 0)
                {
                    additional++;
                }
            if (group.attachments.Count + additional > kMaxNativePassAttachments)
            {
                return false;
            }
            foreach (RGGroupAttachment attachment in group.attachments)
                if (Samples(pass, attachment.textureHandle))
                {
                    return false;
                }
            if (!AttachmentFits(group, pass.depthBufferHandle, true))
            {
                return false;
            }
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (!AttachmentFits(group, pass.colorBuffersHandle[i], false))
                {
                    return false;
                }
            if (pass.depthBufferHandle.IsValid())
                TryAddAttachment(group, pass.depthBufferHandle, pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction, true, out _);
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                TryAddAttachment(group, pass.colorBuffersHandle[i], pass.colorBufferActions[i].loadAction, pass.colorBufferActions[i].storeAction, false, out _);
            return true;
        }

        bool AttachmentFits(RGNativePassGroup group, in RGTextureHandle textureHandle, bool depth)
        {
            if (!textureHandle.IsValid())
            {
                return true;
            }
            if (FindAttachment(group, textureHandle) >= 0)
                return !depth || group.depthAttachment == FindAttachment(group, textureHandle);
            if (!TryAttachmentSize(textureHandle, out int width, out int height, out int samples))
            {
                return false;
            }
            if (group.attachments.Count >= kMaxNativePassAttachments)
            {
                return false;
            }
            if (group.attachments.Count > 0 && (width != group.width || height != group.height || samples != group.samples))
                return false;
            if (depth && group.depthAttachment >= 0)
            {
                return false;
            }
            return true;
        }

        void PushSubpass(RGNativePassGroup group, int passIndex)
        {
            IRGPass pass = m_PassCompileInfos[passIndex].pass;
            var subpass = new RGGroupSubpass { passIndex = passIndex, readOnlyDepth = (pass.depthBufferAccess & EDepthAccess.ReadOnly) != 0 && (pass.depthBufferAccess & EDepthAccess.Write) == 0 };
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                int attachment = FindAttachment(group, pass.colorBuffersHandle[i]);
                subpass.SetColor(subpass.colorCount, attachment);
                subpass.colorCount++;
            }
            if (group.firstPass < 0)
            {
                group.firstPass = passIndex;
            }
            group.lastPass = passIndex;
            group.subpasses.Add(subpass);
        }

        RGNativePassGroup RentGroup()
        {
            for (int i = 0; i < m_NativeGroups.Count; ++i)
            {
                if (m_NativeGroups[i].firstPass < 0 && m_NativeGroups[i].subpasses.Count == 0)
                    return m_NativeGroups[i];
            }
            var group = new RGNativePassGroup();
            m_NativeGroups.Add(group);
            return group;
        }

        void BuildNativeGroups()
        {
            for (int i = 0; i < m_NativeGroups.Count; ++i)
            {
                m_NativeGroups[i].Clear();
            }
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                m_PassCompileInfos[passIndex].mergeGroup = -1;
            RGNativePassGroup current = null;
            int active = 0;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                if (m_PassCompileInfos[passIndex].culled)
                {
                    continue;
                }
                if (!IsNativeRaster(passIndex))
                {
                    current = null;
                    continue;
                }
                bool joined = current != null && TryJoinGroup(current, passIndex);
                if (!joined)
                {
                    current = active < m_NativeGroups.Count ? m_NativeGroups[active] : RentGroup();
                    current.Clear();
                    if (!TryJoinGroup(current, passIndex))
                    {
                        current.Clear();
                        current = null;
                        continue;
                    }
                    active++;
                }
                PushSubpass(current, passIndex);
            }
            if (active < m_NativeGroups.Count)
            {
                for (int i = m_NativeGroups.Count - 1; i >= active; --i)
                {
                    m_NativeGroups.RemoveAt(i);
                }
            }
            AssignGroupIndices();
        }

        void AssignGroupIndices()
        {
            for (int groupIndex = 0; groupIndex < m_NativeGroups.Count; ++groupIndex)
            {
                RGNativePassGroup group = m_NativeGroups[groupIndex];
                for (int subpass = 0; subpass < group.subpasses.Count; ++subpass)
                {
                    ref RGPassCompileInfo info = ref m_PassCompileInfos[group.subpasses[subpass].passIndex];
                    info.mergeGroup = groupIndex;
                    info.subpass = subpass;
                    info.mergeFirst = subpass == 0;
                    info.mergeLast = subpass == group.subpasses.Count - 1;
                }
            }
        }

        static bool PassUsesTexture(IRGPass pass, int textureIndex)
        {
            if (pass.depthBufferHandle.IsValid() && pass.depthBufferHandle.handle.index == textureIndex)
            {
                return true;
            }
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (pass.colorBuffersHandle[i].IsValid() && pass.colorBuffersHandle[i].handle.index == textureIndex)
                {
                    return true;
                }
            return false;
        }

        void BuildSegments()
        {
            int segment = -1;
            bool open = false;
            bool openAsync = false;
            m_SegmentReleases.Clear();
            CollectSegmentCreates();
            int createCursor = 0;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled)
                {
                    info.segment = -1;
                    continue;
                }
                bool split = !open || info.enableAsyncCompute != openAsync || info.syncToPassIndex >= 0 || CreatesReleasedPhysical(passIndex, ref createCursor);
                if (split)
                {
                    segment++;
                    open = true;
                    openAsync = info.enableAsyncCompute;
                    m_SegmentReleases.Clear();
                }
                info.segment = segment;
                NoteReleases(passIndex);
                if (info.needGraphicsFence && info.syncFromPassIndex >= 0)
                    open = false;
            }
        }

        // The pool hands a released allocation to the next create of the same physical descriptor. A segment is queued as a
        // unit, so a create that would take an allocation released earlier in the open segment starts a new segment.
        long PhysicalKey(int type, int index)
        {
            if (type == (int)ERGResourceType.Buffer)
            {
                return ((long)type << 32) | (uint)m_Resources.GetBufferDescriptor(m_Resources.LiveHandle(ERGResourceType.Buffer, index)).GetHashCode();
            }
            return ((long)type << 32) | (uint)m_Resources.GetTextureDescriptor(m_Resources.LiveHandle(ERGResourceType.Texture, index)).PhysicalDescriptor().GetHashCode();
        }

        // A texture attached by a native render-pass group is created at the group's first pass (HoistGroupCreates);
        // segment planning uses that position so a hoisted create that overlaps an earlier release does not split.
        int EffectiveCreatePass(int createPass, int type, int index)
        {
            if (type != (int)ERGResourceType.Texture)
            {
                return createPass;
            }
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled || info.mergeGroup < 0 || !PassUsesTexture(info.pass, index))
                {
                    continue;
                }
                int first = m_NativeGroups[info.mergeGroup].firstPass;
                return first >= 0 && first < createPass ? first : createPass;
            }
            return createPass;
        }

        void CollectSegmentCreates()
        {
            m_SegmentCreates.Clear();
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    foreach (int index in info.resourceCreateList[type])
                    {
                        m_SegmentCreates.Add((EffectiveCreatePass(passIndex, type, index), PhysicalKey(type, index)));
                    }
                }
            }
            m_SegmentCreates.Sort(s_ByCreatePass);
        }

        bool CreatesReleasedPhysical(int passIndex, ref int cursor)
        {
            bool conflict = false;
            for (; cursor < m_SegmentCreates.Count && m_SegmentCreates[cursor].pass <= passIndex; ++cursor)
            {
                conflict |= m_SegmentCreates[cursor].pass == passIndex && m_SegmentReleases.Contains(m_SegmentCreates[cursor].key);
            }
            return conflict;
        }

        void NoteReleases(int passIndex)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (int index in info.resourceReleaseList[type])
                {
                    m_SegmentReleases.Add(PhysicalKey(type, index));
                }
            }
        }

        void ConformGroupsToSegments()
        {
            bool spans = false;
            for (int i = 0; i < m_NativeGroups.Count; ++i)
            {
                RGNativePassGroup group = m_NativeGroups[i];
                int segment = -1;
                for (int s = 0; s < group.subpasses.Count; ++s)
                {
                    int passSegment = m_PassCompileInfos[group.subpasses[s].passIndex].segment;
                    if (segment < 0)
                    {
                        segment = passSegment;
                    }
                    else if (segment != passSegment)
                    {
                        spans = true;
                    }
                }
            }
            if (!spans)
            {
                return;
            }
            m_SegmentRanges.Clear();
            for (int i = 0; i < m_NativeGroups.Count; ++i)
            {
                RGNativePassGroup group = m_NativeGroups[i];
                int runFirst = -1;
                int runSegment = -1;
                int previous = -1;
                for (int s = 0; s < group.subpasses.Count; ++s)
                {
                    int passIndex = group.subpasses[s].passIndex;
                    int passSegment = m_PassCompileInfos[passIndex].segment;
                    if (runFirst < 0)
                    {
                        runFirst = passIndex;
                        runSegment = passSegment;
                        previous = passIndex;
                        continue;
                    }
                    if (passSegment != runSegment)
                    {
                        m_SegmentRanges.Add((runFirst, previous));
                        runFirst = passIndex;
                        runSegment = passSegment;
                    }
                    previous = passIndex;
                }
                if (runFirst >= 0)
                {
                    m_SegmentRanges.Add((runFirst, previous));
                }
            }
            for (int i = 0; i < m_NativeGroups.Count; ++i)
            {
                m_NativeGroups[i].Clear();
            }
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                m_PassCompileInfos[passIndex].mergeGroup = -1;
            int active = 0;
            foreach ((int first, int last) range in m_SegmentRanges)
            {
                RGNativePassGroup group = active < m_NativeGroups.Count ? m_NativeGroups[active] : RentGroup();
                group.Clear();
                active++;
                for (int passIndex = range.first; passIndex <= range.last; ++passIndex)
                {
                    if (!IsNativeRaster(passIndex))
                    {
                        continue;
                    }
                    if (group.subpasses.Count > 0 && !TryJoinGroup(group, passIndex))
                    {
                        group = active < m_NativeGroups.Count ? m_NativeGroups[active] : RentGroup();
                        group.Clear();
                        active++;
                        TryJoinGroup(group, passIndex);
                    }
                    else if (group.subpasses.Count == 0)
                        TryJoinGroup(group, passIndex);
                    PushSubpass(group, passIndex);
                }
            }
            if (active < m_NativeGroups.Count)
            {
                for (int i = m_NativeGroups.Count - 1; i >= active; --i)
                {
                    m_NativeGroups.RemoveAt(i);
                }
            }
            AssignGroupIndices();
        }

        void HoistGroupCreates()
        {
            for (int groupIndex = 0; groupIndex < m_NativeGroups.Count; ++groupIndex)
            {
                RGNativePassGroup group = m_NativeGroups[groupIndex];
                foreach (RGGroupAttachment attachment in group.attachments)
                {
                    if (!attachment.textureHandle.IsValid() || m_Resources.IsResourceImported(attachment.textureHandle.handle))
                    {
                        continue;
                    }
                    int index = attachment.textureHandle.handle.index;
                    int type = (int)ERGResourceType.Texture;
                    for (int passIndex = group.firstPass + 1; passIndex <= group.lastPass; ++passIndex)
                    {
                        List<int> creates = m_PassCompileInfos[passIndex].resourceCreateList[type];
                        if (!creates.Remove(index))
                        {
                            continue;
                        }
                        List<int> destination = m_PassCompileInfos[group.firstPass].resourceCreateList[type];
                        if (!destination.Contains(index))
                        {
                            destination.Add(index);
                        }
                    }
                }
            }
        }

        RenderBufferStoreAction EffectiveStore(in RGTextureHandle textureHandle, RenderBufferStoreAction declared, int passIndex)
        {
            if (declared != RenderBufferStoreAction.Store || !textureHandle.IsValid() || m_Resources.IsResourceImported(textureHandle.handle))
                return declared;
            ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[(int)ERGResourceType.Texture][textureHandle.handle.index];
            return LastUse(info) == passIndex ? RenderBufferStoreAction.DontCare : declared;
        }

        internal RenderBufferStoreAction CompiledColorStore(int passIndex, int colorIndex)
        {
            IRGPass pass = m_PassCompileInfos[passIndex].pass;
            return EffectiveStore(pass.colorBuffersHandle[colorIndex], pass.colorBufferActions[colorIndex].storeAction, passIndex);
        }

        internal RenderBufferStoreAction CompiledDepthStore(int passIndex)
        {
            IRGPass pass = m_PassCompileInfos[passIndex].pass;
            return EffectiveStore(pass.depthBufferHandle, pass.depthBufferAction.storeAction, passIndex);
        }

        void ApplyMemoryless()
        {
            if (!GraphicsConventions.SupportsMemorylessRenderTargets)
            {
                return;
            }
            int type = (int)ERGResourceType.Texture;
            for (int index = 0; index < m_ResourcesCompileInfos[type].size; ++index)
            {
                if (m_Resources.IsResourceImported(m_Resources.LiveHandle(ERGResourceType.Texture, index)))
                {
                    continue;
                }
                int group = -1;
                bool depth = false;
                bool attachmentOnly = true;
                int lastPass = -1;
                for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                {
                    ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                    if (info.culled || !PassUsesTexture(info.pass, index))
                    {
                        continue;
                    }
                    if (Samples(info.pass, m_Resources.TextureHandle(index)))
                    {
                        attachmentOnly = false;
                        break;
                    }
                    if (info.mergeGroup < 0)
                    {
                        attachmentOnly = false;
                        break;
                    }
                    if (group < 0)
                    {
                        group = info.mergeGroup;
                    }
                    else if (group != info.mergeGroup)
                    {
                        attachmentOnly = false;
                        break;
                    }
                    if (info.pass.depthBufferHandle.IsValid() && info.pass.depthBufferHandle.handle.index == index)
                    {
                        depth = true;
                    }
                    lastPass = passIndex;
                }
                if (!attachmentOnly || group < 0 || lastPass < 0)
                {
                    continue;
                }
                RGTextureHandle textureHandle = m_Resources.TextureHandle(index);
                RenderBufferStoreAction declared = depth
                    ? m_PassCompileInfos[lastPass].pass.depthBufferAction.storeAction
                    : StoreOfColor(m_PassCompileInfos[lastPass].pass, index);
                if (EffectiveStore(textureHandle, declared, lastPass) != RenderBufferStoreAction.DontCare)
                {
                    continue;
                }
                TextureDescriptor descriptor = m_Resources.GetTextureDescriptor(m_Resources.LiveHandle(ERGResourceType.Texture, index));
                // Unity drops memoryless color when the texture is also a UAV.
                if (!depth && descriptor.enableRandomWrite)
                {
                    continue;
                }
                RenderTextureMemoryless memoryless = depth ? RenderTextureMemoryless.Depth : RenderTextureMemoryless.Color;
                if ((int)descriptor.msaaSamples > 1)
                {
                    memoryless |= RenderTextureMemoryless.MSAA;
                }
                descriptor.memoryless = memoryless;
                SetTextureDescriptor(index, descriptor);
            }
        }

        static RenderBufferStoreAction StoreOfColor(IRGPass pass, int textureIndex)
        {
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (pass.colorBuffersHandle[i].IsValid() && pass.colorBuffersHandle[i].handle.index == textureIndex)
                    return pass.colorBufferActions[i].storeAction;
            }
            return RenderBufferStoreAction.Store;
        }

        void SetTextureDescriptor(int index, in TextureDescriptor descriptor)
        {
            m_Resources.SetTextureDescriptor(index, descriptor);
        }

        void BakeProfileMarkers()
        {
            for (int scopeIndex = 0; scopeIndex < m_ProfileScopes.Count; ++scopeIndex)
            {
                RGProfileScope scope = m_ProfileScopes[scopeIndex];
                m_ProfileIndices.Clear();
                foreach (IRGPass pass in scope.passes)
                {
                    if (pass.index < 0 || pass.index >= m_PassCompileInfos.size || m_PassCompileInfos[pass.index].culled)
                    {
                        continue;
                    }
                    if (!m_ProfileIndices.Contains(pass.index))
                    {
                        m_ProfileIndices.Add(pass.index);
                    }
                }
                if (m_ProfileIndices.Count == 0)
                {
                    continue;
                }
                m_ProfileIndices.Sort();
                int runStart = m_ProfileIndices[0];
                int previous = m_ProfileIndices[0];
                for (int i = 1; i <= m_ProfileIndices.Count; ++i)
                {
                    bool boundary = i == m_ProfileIndices.Count
                        || m_ProfileIndices[i] != previous + 1
                        || m_PassCompileInfos[m_ProfileIndices[i]].segment != m_PassCompileInfos[previous].segment;
                    if (boundary)
                    {
                        m_PassCompileInfos[runStart].beginSamples.Add(scope.sampler);
                        m_PassCompileInfos[previous].endSamples.Add(scope.sampler);
                        if (i < m_ProfileIndices.Count)
                        {
                            runStart = m_ProfileIndices[i];
                        }
                    }
                    if (i < m_ProfileIndices.Count)
                    {
                        previous = m_ProfileIndices[i];
                    }
                }
            }
        }

        void ValidateGraph()
        {
            RecordPassLocalOwners();
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled)
                {
                    continue;
                }
                if (!info.pass.hasExecuteAction)
                    throw new InvalidOperationException($"Render graph pass '{info.pass.name}' was recorded without an execute function.");
                ValidatePassLocal(info.pass, passIndex);
                ValidateReads(info.pass, passIndex);
                if (info.pass.passType == EPassType.Raster)
                    ValidateAttachments(info.pass, passIndex);
            }
        }

        // Each pass-local (temporal) resource has one live owner, recorded once; any other live pass using it is an error.
        void RecordPassLocalOwners()
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                int count = m_ResourcesCompileInfos[type].size;
                if (m_PassLocalOwners[type].Length < count)
                {
                    m_PassLocalOwners[type] = new int[Math.Max(count, Math.Max(16, m_PassLocalOwners[type].Length * 2))];
                }
                Array.Fill(m_PassLocalOwners[type], -1, 0, count);
            }
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled)
                {
                    continue;
                }
                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    foreach (RGResourceHandle temporalHandle in info.pass.temporalResourceList[type])
                    {
                        int owner = m_PassLocalOwners[type][temporalHandle.index];
                        if (owner >= 0 && owner != passIndex)
                            throw new InvalidOperationException($"Pass-local resource {m_Resources.GetResourceName(temporalHandle)} is used by '{info.pass.name}' but belongs to '{m_PassCompileInfos[owner].pass.name}'.");
                        m_PassLocalOwners[type][temporalHandle.index] = passIndex;
                    }
                }
            }
        }

        void ValidatePassLocal(IRGPass pass, int passIndex)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (RGResourceHandle handle in pass.resourceReadLists[type])
                    RequirePassLocalOwner(handle, pass, passIndex);
                foreach (RGResourceHandle handle in pass.resourceWriteLists[type])
                    RequirePassLocalOwner(handle, pass, passIndex);
            }
        }

        void RequirePassLocalOwner(in RGResourceHandle handle, IRGPass pass, int passIndex)
        {
            int owner = m_PassLocalOwners[handle.iType][handle.index];
            if (owner >= 0 && owner != passIndex)
                throw new InvalidOperationException($"Pass-local resource {m_Resources.GetResourceName(handle)} is used by '{pass.name}' but belongs to '{m_PassCompileInfos[owner].pass.name}'.");
        }

        void ValidateReads(IRGPass pass, int passIndex)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (RGResourceHandle handle in pass.resourceReadLists[type])
                {
                    if (m_Resources.IsResourceImported(handle))
                    {
                        continue;
                    }
                    int firstWrite = GetFirstValidWriteIndex(m_ResourcesCompileInfos[type][handle.index]);
                    if (firstWrite < 0 || passIndex < firstWrite)
                        throw new InvalidOperationException($"'{pass.name}' reads {m_Resources.GetResourceName(handle)} before any pass writes it.");
                }
            }
        }

        void ValidateAttachments(IRGPass pass, int passIndex)
        {
            if (pass.colorBufferMaxIndex > 0 && !pass.depthBufferHandle.IsValid())
                throw new InvalidOperationException($"'{pass.name}' sets multiple color attachments without a depth buffer.");
            int width = 0, height = 0, samples = 0;
            bool sized = false;
            if (pass.depthBufferHandle.IsValid())
            {
                if (!TryAttachmentSize(pass.depthBufferHandle, out width, out height, out samples))
                    throw new InvalidOperationException($"'{pass.name}' has a depth attachment with no size.");
                sized = true;
                if (pass.depthBufferAction.loadAction == RenderBufferLoadAction.Load)
                    RequireEarlierWrite(pass, passIndex, pass.depthBufferHandle.handle);
            }
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (!pass.colorBuffersHandle[i].IsValid())
                    throw new InvalidOperationException($"'{pass.name}' has a hole in its color attachments at index {i}.");
                if (!TryAttachmentSize(pass.colorBuffersHandle[i], out int colorWidth, out int colorHeight, out int colorSamples))
                    throw new InvalidOperationException($"'{pass.name}' color attachment {i} has no size.");
                if (sized && (colorWidth != width || colorHeight != height || colorSamples != samples))
                    throw new InvalidOperationException($"'{pass.name}' color attachment {i} is {colorWidth}x{colorHeight} x{colorSamples}, which does not match {width}x{height} x{samples}.");
                width = colorWidth;
                height = colorHeight;
                samples = colorSamples;
                sized = true;
                if (pass.colorBufferActions[i].loadAction == RenderBufferLoadAction.Load)
                    RequireEarlierWrite(pass, passIndex, pass.colorBuffersHandle[i].handle);
            }
        }

        void RequireEarlierWrite(IRGPass pass, int passIndex, in RGResourceHandle handle)
        {
            if (m_Resources.IsResourceImported(handle))
            {
                return;
            }
            int firstWrite = GetFirstValidWriteIndex(m_ResourcesCompileInfos[handle.iType][handle.index]);
            if (firstWrite < 0 || firstWrite >= passIndex)
                throw new InvalidOperationException($"'{pass.name}' loads {m_Resources.GetResourceName(handle)} but no earlier pass writes it.");
        }

        internal void NoteProfiledPass(IRGPass pass)
        {
            for (int i = 0; i < m_OpenScopes.Count; ++i)
                m_OpenScopes[i].passes.Add(pass);
        }

        internal void BeginProfileRange(ProfilingSampler sampler)
        {
            if (sampler == null)
            {
                return;
            }
            RGProfileScope scope = m_ScopePool.Count > 0 ? m_ScopePool.Pop() : new RGProfileScope();
            scope.sampler = sampler;
            scope.passes.Clear();
            m_ProfileScopes.Add(scope);
            m_OpenScopes.Add(scope);
        }

        // A null sampler (non-development players have none) opened no scope, so it closes none.
        internal void EndProfileRange(ProfilingSampler sampler)
        {
            if (sampler == null || m_OpenScopes.Count == 0)
            {
                return;
            }
            if (m_OpenScopes[m_OpenScopes.Count - 1].sampler != sampler)
                throw new InvalidOperationException("A profiling scope ended out of order.");
            m_OpenScopes.RemoveAt(m_OpenScopes.Count - 1);
        }
    }
}
