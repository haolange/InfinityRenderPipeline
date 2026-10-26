using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.RenderGraph
{
    internal interface IRGCompileExtension
    {
        void Contribute(RGBuilder builder);
    }

    sealed class RGProfileScope
    {
        public ProfilingSampler sampler;
        public readonly List<IRGPass> passes = new List<IRGPass>(8);
    }

    internal struct RGGroupAttachment
    {
        public RGTextureRef texture;
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
                case 0: return c0;
                case 1: return c1;
                case 2: return c2;
                case 3: return c3;
                case 4: return c4;
                case 5: return c5;
                case 6: return c6;
                default: return c7;
            }
        }

        public void SetColor(int index, int attachment)
        {
            byte value = (byte)attachment;
            switch (index)
            {
                case 0: c0 = value; break;
                case 1: c1 = value; break;
                case 2: c2 = value; break;
                case 3: c3 = value; break;
                case 4: c4 = value; break;
                case 5: c5 = value; break;
                case 6: c6 = value; break;
                default: c7 = value; break;
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

        void CompilePass()
        {
            RunLiveness();
            if (!m_ExtensionContributed)
            {
                m_RecordingCompilerPasses = true;
                m_CompileExtension?.Contribute(this);
                m_RecordingCompilerPasses = false;
                PlaceInsertedPasses();
                m_ExtensionContributed = true;
                RunLiveness();
            }
            CompileRendererLists();
            UpdateResource();
            BuildNativeGroups();
            PlanAliases(true);
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
                if (passInfo.pass.sideEffect) passInfo.hasSideEffect = true;

                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    foreach (var resource in passInfo.pass.resourceReadLists[type])
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource];
                        info.consumers.Add(passIndex);
                        info.refCount++;
                    }

                    foreach (var resource in passInfo.pass.resourceWriteLists[type])
                    {
                        ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource];
                        info.producers.Add(passIndex);
                        passInfo.refCount++;
                        if (passInfo.pass.importedWritesAreSideEffects && m_Resources.IsResourceImported(resource))
                            passInfo.hasSideEffect = true;
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

        void CullingUnusedPass()
        {
            var stack = new List<RGCullItem>(32);
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                DynamicArray<RGResourceCompileInfo> resources = m_ResourcesCompileInfos[type];
                for (int i = 0; i < resources.size; ++i)
                {
                    if (resources[i].refCount == 0)
                        stack.Add(new RGCullItem { type = type, index = i });
                }
            }

            for (int cursor = 0; cursor < stack.Count; ++cursor)
            {
                RGCullItem item = stack[cursor];
                ref RGResourceCompileInfo unused = ref m_ResourcesCompileInfos[item.type][item.index];
                if (unused.cullClosed) continue;
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
                        foreach (RGResourceHandle resource in pass.resourceReadLists[type])
                            CloseRead(type, resource.index, stack);
                        foreach (int resource in pass.temporalResourceList[type])
                            CloseRead(type, resource, stack);
                    }
                }
            }
        }

        void CloseRead(int type, int index, List<RGCullItem> stack)
        {
            ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][index];
            info.refCount--;
            if (info.refCount == 0 && !info.cullClosed)
                stack.Add(new RGCullItem { type = type, index = index });
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
                if (writer >= currentPassIndex) break;
                if (!m_PassCompileInfos[writer].culled && m_PassCompileInfos[writer].enableAsyncCompute != current.enableAsyncCompute)
                    dependency = writer;
            }
            if (writesResource)
            {
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
                if (passInfo.culled) continue;
                for (int type = 0; type < (int)ERGResourceType.Max; ++type)
                {
                    var resourcesInfo = m_ResourcesCompileInfos[type];
                    foreach (int resource in passInfo.pass.resourceReadLists[type])
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resource], false);
                    foreach (int resource in passInfo.pass.resourceWriteLists[type])
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resource], true);
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
                    if (lastUse == -1) continue;
                    m_PassCompileInfos[lastUse].resourceReleaseList[type].Add(i);
                    var handle = new RGResourceHandle(i, (ERGResourceType)type);
                    if ((hasAsync || m_AsyncComputeSupported && m_Resources.IsPoolTracked(handle))
                        && (!m_Resources.IsResourceImported(handle) || m_Resources.IsPoolTracked(handle)))
                    {
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

        void RecordLastQueueUse(ref RGResourceCompileInfo resource, int passIndex)
        {
            ref RGPassCompileInfo pass = ref m_PassCompileInfos[passIndex];
            if (pass.culled) return;
            if (pass.enableAsyncCompute) resource.lastComputeUse = Math.Max(resource.lastComputeUse, passIndex);
            else resource.lastGraphicsUse = Math.Max(resource.lastGraphicsUse, passIndex);
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
                if (passInfo.culled) continue;
                List<RGRendererListRef> used = passInfo.pass.usedRendererLists;
                if (used == null) continue;
                for (int i = 0; i < used.Count; ++i)
                {
                    RGRendererListRef list = used[i];
                    if (m_RendererListRecords.IsLiveRef(list))
                        m_RendererListRecords.MarkLiveConsumer(list.index, passIndex);
                }
            }
            m_RendererListRecords.ReleaseUnused();
        }

        bool IsCulledPass(IRGPass pass)
        {
            if (pass.insertedByCompiler) return false;
            if (pass.index < 0 || pass.index >= m_PassCompileInfos.size) return false;
            return m_PassCompileInfos[pass.index].culled;
        }

        static bool Produces(IRGPass pass, in RGResourceHandle handle)
        {
            foreach (RGResourceHandle written in pass.resourceWriteLists[handle.iType])
                if (written.index == handle.index && written.generation == handle.generation) return true;
            foreach (RGResourceHandle temporal in pass.temporalResourceList[handle.iType])
                if (temporal.index == handle.index && temporal.generation == handle.generation) return true;
            return false;
        }

        bool DependenciesReady(IRGPass pass, List<IRGPass> placed)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (RGResourceHandle handle in pass.resourceReadLists[type])
                {
                    for (int i = 0; i < m_PassList.Count; ++i)
                    {
                        IRGPass writer = m_PassList[i];
                        if (writer == pass || IsCulledPass(writer) || !Produces(writer, handle)) continue;
                        if (!placed.Contains(writer)) return false;
                    }
                }
            }
            return true;
        }

        void PlaceInsertedPasses()
        {
            int inserted = 0;
            for (int i = 0; i < m_PassList.Count; ++i)
                if (m_PassList[i].insertedByCompiler) inserted++;
            if (inserted == 0) return;

            var anchored = new List<IRGPass>(m_PassList.Count - inserted);
            var pending = new List<IRGPass>(inserted);
            for (int i = 0; i < m_PassList.Count; ++i)
            {
                if (m_PassList[i].insertedByCompiler) pending.Add(m_PassList[i]);
                else anchored.Add(m_PassList[i]);
            }

            var placed = new List<IRGPass>(m_PassList.Count);
            int anchor = 0;
            while (anchor < anchored.Count || pending.Count > 0)
            {
                bool moved = false;
                for (int i = 0; i < pending.Count;)
                {
                    if (!DependenciesReady(pending[i], placed)) { ++i; continue; }
                    placed.Add(pending[i]);
                    pending.RemoveAt(i);
                    moved = true;
                }
                if (anchor < anchored.Count)
                {
                    placed.Add(anchored[anchor++]);
                    moved = true;
                }
                else if (!moved)
                    throw new InvalidOperationException("A compiler-inserted pass depends on itself.");
            }

            m_PassList.Clear();
            m_PassList.AddRange(placed);
            for (int i = 0; i < m_PassList.Count; ++i) m_PassList[i].index = i;
        }

        bool TryAttachmentSize(in RGTextureRef texture, out int width, out int height, out int samples)
        {
            TextureDescriptor descriptor = m_Resources.GetTextureDescriptor(texture.handle);
            width = descriptor.physicalWidth;
            height = descriptor.physicalHeight;
            samples = Math.Max(1, (int)descriptor.msaaSamples);
            return width > 0 && height > 0;
        }

        static bool SameResource(in RGTextureRef a, in RGTextureRef b)
        {
            return a.IsValid() && b.IsValid() && a.handle.index == b.handle.index && a.handle.generation == b.handle.generation;
        }

        bool Samples(IRGPass pass, in RGTextureRef texture)
        {
            foreach (RGResourceHandle handle in pass.sampledResources)
            {
                if (handle.type == ERGResourceType.Texture && handle.index == texture.handle.index && handle.generation == texture.handle.generation)
                    return true;
            }
            return false;
        }

        bool IsNativeRaster(int passIndex)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            if (info.culled || info.pass.passType != EPassType.Raster || !info.pass.enableNativeRenderPass) return false;
            return info.pass.depthBuffer.IsValid() || info.pass.colorBufferMaxIndex >= 0;
        }

        int FindAttachment(RGNativePassGroup group, in RGTextureRef texture)
        {
            for (int i = 0; i < group.attachments.Count; ++i)
                if (SameResource(group.attachments[i].texture, texture)) return i;
            return -1;
        }

        bool TryAddAttachment(RGNativePassGroup group, in RGTextureRef texture, RenderBufferLoadAction load, RenderBufferStoreAction store, bool depth, out int attachmentIndex)
        {
            attachmentIndex = FindAttachment(group, texture);
            if (attachmentIndex >= 0)
            {
                if (depth && group.depthAttachment != attachmentIndex) return false;
                return true;
            }
            if (!TryAttachmentSize(texture, out int width, out int height, out int samples)) return false;
            if (group.attachments.Count == 0)
            {
                group.width = width;
                group.height = height;
                group.samples = samples;
            }
            else if (width != group.width || height != group.height || samples != group.samples)
                return false;
            int colors = group.depthAttachment >= 0 ? group.attachments.Count - 1 : group.attachments.Count;
            if (!depth && colors >= 8) return false;
            if (depth && group.depthAttachment >= 0) return false;
            attachmentIndex = group.attachments.Count;
            group.attachments.Add(new RGGroupAttachment { texture = texture, load = load, store = store, depth = depth });
            if (depth) group.depthAttachment = attachmentIndex;
            return true;
        }

        bool TryJoinGroup(RGNativePassGroup group, int passIndex)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            IRGPass pass = info.pass;
            if (info.segment >= 0 && group.subpasses.Count > 0)
            {
                int firstSegment = m_PassCompileInfos[group.subpasses[0].passIndex].segment;
                if (info.segment != firstSegment) return false;
            }
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (!pass.colorBuffers[i].IsValid()) return false;
            foreach (RGGroupAttachment attachment in group.attachments)
                if (Samples(pass, attachment.texture)) return false;
            if (!AttachmentFits(group, pass.depthBuffer, true)) return false;
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (!AttachmentFits(group, pass.colorBuffers[i], false)) return false;
            if (pass.depthBuffer.IsValid())
                TryAddAttachment(group, pass.depthBuffer, pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction, true, out _);
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                TryAddAttachment(group, pass.colorBuffers[i], pass.colorBufferActions[i].loadAction, pass.colorBufferActions[i].storeAction, false, out _);
            return true;
        }

        bool AttachmentFits(RGNativePassGroup group, in RGTextureRef texture, bool depth)
        {
            if (!texture.IsValid()) return true;
            if (FindAttachment(group, texture) >= 0)
                return !depth || group.depthAttachment == FindAttachment(group, texture);
            if (!TryAttachmentSize(texture, out int width, out int height, out int samples)) return false;
            if (group.attachments.Count > 0 && (width != group.width || height != group.height || samples != group.samples))
                return false;
            int colors = group.depthAttachment >= 0 ? group.attachments.Count - 1 : group.attachments.Count;
            if (!depth && colors >= 8) return false;
            if (depth && group.depthAttachment >= 0) return false;
            return true;
        }

        void PushSubpass(RGNativePassGroup group, int passIndex)
        {
            IRGPass pass = m_PassCompileInfos[passIndex].pass;
            var subpass = new RGGroupSubpass { passIndex = passIndex, readOnlyDepth = (pass.depthBufferAccess & EDepthAccess.ReadOnly) != 0 && (pass.depthBufferAccess & EDepthAccess.Write) == 0 };
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                int attachment = FindAttachment(group, pass.colorBuffers[i]);
                subpass.SetColor(subpass.colorCount, attachment);
                subpass.colorCount++;
            }
            if (group.firstPass < 0) group.firstPass = passIndex;
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
            for (int i = 0; i < m_NativeGroups.Count; ++i) m_NativeGroups[i].Clear();
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                m_PassCompileInfos[passIndex].mergeGroup = -1;
            RGNativePassGroup current = null;
            int active = 0;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                if (m_PassCompileInfos[passIndex].culled) continue;
                if (!IsNativeRaster(passIndex)) { current = null; continue; }
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
                for (int i = m_NativeGroups.Count - 1; i >= active; --i) m_NativeGroups.RemoveAt(i);
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

        int SimulatedCreate(int type, int index)
        {
            int create = -1;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                if (m_PassCompileInfos[passIndex].resourceCreateList[type].Contains(index))
                {
                    create = passIndex;
                    break;
                }
            }
            if (create < 0 || type != (int)ERGResourceType.Texture) return create;
            int groupIndex = -1;
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled || info.mergeGroup < 0) continue;
                if (!PassUsesTexture(info.pass, index)) continue;
                groupIndex = info.mergeGroup;
                break;
            }
            if (groupIndex < 0) return create;
            int first = m_NativeGroups[groupIndex].firstPass;
            return first >= 0 && first < create ? first : create;
        }

        static bool PassUsesTexture(IRGPass pass, int textureIndex)
        {
            if (pass.depthBuffer.IsValid() && pass.depthBuffer.handle.index == textureIndex) return true;
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                if (pass.colorBuffers[i].IsValid() && pass.colorBuffers[i].handle.index == textureIndex) return true;
            return false;
        }

        struct RGAliasItem
        {
            public int type, index, start, end;
        }

        void PlanAliases(bool simulateHoist)
        {
            var items = new List<RGAliasItem>(32);
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                for (int index = 0; index < m_ResourcesCompileInfos[type].size; ++index)
                {
                    var handle = new RGResourceHandle(index, (ERGResourceType)type);
                    if (m_Resources.IsResourceImported(handle)) continue;
                    int end = LastUse(m_ResourcesCompileInfos[type][index]);
                    if (end < 0) continue;
                    int start = simulateHoist ? SimulatedCreate(type, index) : CreatePass(type, index);
                    if (start < 0) start = GetFirstValidWriteIndex(m_ResourcesCompileInfos[type][index]);
                    if (start < 0) continue;
                    items.Add(new RGAliasItem { type = type, index = index, start = start, end = end });
                    m_ResourcesCompileInfos[type][index].estimatedBytes = EstimateResourceBytes(type, index);
                }
            }

            int groupId = 0;
            var used = new bool[items.Count];
            for (int i = 0; i < items.Count; ++i)
            {
                if (used[i]) continue;
                var members = new List<int>(4);
                for (int j = i; j < items.Count; ++j)
                {
                    if (used[j] || !SamePhysical(items[i], items[j])) continue;
                    used[j] = true;
                    members.Add(j);
                }
                members.Sort((a, b) =>
                {
                    int start = items[a].start.CompareTo(items[b].start);
                    return start != 0 ? start : items[a].end.CompareTo(items[b].end);
                });
                var slotFree = new List<int>(4);
                foreach (int member in members)
                {
                    RGAliasItem item = items[member];
                    int slot = -1;
                    for (int s = 0; s < slotFree.Count; ++s)
                    {
                        if (slotFree[s] <= item.start) { slot = s; break; }
                    }
                    if (slot < 0)
                    {
                        slot = slotFree.Count;
                        slotFree.Add(item.end + 1);
                    }
                    else slotFree[slot] = item.end + 1;
                    ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[item.type][item.index];
                    info.aliasSlot = slot;
                    info.aliasGroup = groupId;
                }
                groupId++;
            }
        }

        int CreatePass(int type, int index)
        {
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                if (m_PassCompileInfos[passIndex].resourceCreateList[type].Contains(index))
                    return passIndex;
            }
            return -1;
        }

        bool SamePhysical(RGAliasItem a, RGAliasItem b)
        {
            if (a.type != b.type) return false;
            if (a.type == (int)ERGResourceType.Buffer)
            {
                BufferDescriptor left = m_Resources.GetBufferDescriptor(new RGResourceHandle(a.index, ERGResourceType.Buffer));
                BufferDescriptor right = m_Resources.GetBufferDescriptor(new RGResourceHandle(b.index, ERGResourceType.Buffer));
                return left.Equals(right);
            }
            TextureDescriptor leftTexture = m_Resources.GetTextureDescriptor(new RGResourceHandle(a.index, ERGResourceType.Texture)).PhysicalDescriptor();
            TextureDescriptor rightTexture = m_Resources.GetTextureDescriptor(new RGResourceHandle(b.index, ERGResourceType.Texture)).PhysicalDescriptor();
            return leftTexture.Equals(rightTexture);
        }

        long EstimateResourceBytes(int type, int index)
        {
            if (type == (int)ERGResourceType.Buffer)
            {
                BufferDescriptor descriptor = m_Resources.GetBufferDescriptor(new RGResourceHandle(index, ERGResourceType.Buffer));
                return (long)descriptor.count * descriptor.stride;
            }
            TextureDescriptor descriptor = m_Resources.GetTextureDescriptor(new RGResourceHandle(index, ERGResourceType.Texture));
            int texelBytes = 4;
            if (descriptor.colorFormat != GraphicsFormat.None)
                texelBytes = (int)Math.Max(1u, GraphicsFormatUtility.GetBlockSize(descriptor.colorFormat));
            else if (descriptor.depthBufferBits != EDepthBits.None)
                texelBytes = Math.Max(1, (int)descriptor.depthBufferBits / 8);
            int width = Math.Max(1, descriptor.physicalWidth);
            int height = Math.Max(1, descriptor.physicalHeight);
            int slices = Math.Max(1, descriptor.slices);
            long bytes = 0;
            int guard = 0;
            do
            {
                bytes += (long)width * height * slices * texelBytes;
                if (!descriptor.useMipMap || (width == 1 && height == 1) || ++guard == 16) break;
                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
            }
            while (true);
            return bytes * Math.Max(1, (int)descriptor.msaaSamples);
        }

        void BuildSegments()
        {
            int segment = -1;
            bool open = false;
            bool openAsync = false;
            var slotSegment = new Dictionary<long, int>();
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled) { info.segment = -1; continue; }
                bool split = !open || info.enableAsyncCompute != openAsync || info.syncToPassIndex >= 0 || AliasConflict(passIndex, segment, slotSegment);
                if (split)
                {
                    segment++;
                    open = true;
                    openAsync = info.enableAsyncCompute;
                }
                info.segment = segment;
                NoteSlotReleases(passIndex, segment, slotSegment);
                if (info.needGraphicsFence && info.syncFromPassIndex >= 0)
                    open = false;
            }
        }

        bool AliasConflict(int passIndex, int segment, Dictionary<long, int> slotSegment)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (int index in info.resourceCreateList[type])
                {
                    ref RGResourceCompileInfo resource = ref m_ResourcesCompileInfos[type][index];
                    if (resource.aliasSlot < 0) continue;
                    long key = ((long)resource.aliasGroup << 32) | (uint)resource.aliasSlot;
                    if (slotSegment.TryGetValue(key, out int released) && released == segment) return true;
                }
            }
            return false;
        }

        void NoteSlotReleases(int passIndex, int segment, Dictionary<long, int> slotSegment)
        {
            ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (int index in info.resourceReleaseList[type])
                {
                    ref RGResourceCompileInfo resource = ref m_ResourcesCompileInfos[type][index];
                    if (resource.aliasSlot < 0) continue;
                    long key = ((long)resource.aliasGroup << 32) | (uint)resource.aliasSlot;
                    slotSegment[key] = segment;
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
                    if (segment < 0) segment = passSegment;
                    else if (segment != passSegment) spans = true;
                }
            }
            if (!spans) return;
            var ranges = new List<(int first, int last)>();
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
                    if (runFirst < 0) { runFirst = passIndex; runSegment = passSegment; previous = passIndex; continue; }
                    if (passSegment != runSegment)
                    {
                        ranges.Add((runFirst, previous));
                        runFirst = passIndex;
                        runSegment = passSegment;
                    }
                    previous = passIndex;
                }
                if (runFirst >= 0) ranges.Add((runFirst, previous));
            }
            for (int i = 0; i < m_NativeGroups.Count; ++i) m_NativeGroups[i].Clear();
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                m_PassCompileInfos[passIndex].mergeGroup = -1;
            int active = 0;
            foreach ((int first, int last) range in ranges)
            {
                RGNativePassGroup group = active < m_NativeGroups.Count ? m_NativeGroups[active] : RentGroup();
                group.Clear();
                active++;
                for (int passIndex = range.first; passIndex <= range.last; ++passIndex)
                {
                    if (!IsNativeRaster(passIndex)) continue;
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
                for (int i = m_NativeGroups.Count - 1; i >= active; --i) m_NativeGroups.RemoveAt(i);
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
                    if (!attachment.texture.IsValid() || m_Resources.IsResourceImported(attachment.texture.handle)) continue;
                    int index = attachment.texture.handle.index;
                    int type = (int)ERGResourceType.Texture;
                    for (int passIndex = group.firstPass + 1; passIndex <= group.lastPass; ++passIndex)
                    {
                        List<int> creates = m_PassCompileInfos[passIndex].resourceCreateList[type];
                        if (!creates.Remove(index)) continue;
                        List<int> destination = m_PassCompileInfos[group.firstPass].resourceCreateList[type];
                        if (!destination.Contains(index)) destination.Add(index);
                    }
                }
            }
        }

        RenderBufferStoreAction EffectiveStore(in RGTextureRef texture, RenderBufferStoreAction declared, int passIndex)
        {
            if (declared != RenderBufferStoreAction.Store || !texture.IsValid() || m_Resources.IsResourceImported(texture.handle))
                return declared;
            ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[(int)ERGResourceType.Texture][texture.handle.index];
            return LastUse(info) == passIndex ? RenderBufferStoreAction.DontCare : declared;
        }

        internal RenderBufferStoreAction CompiledColorStore(int passIndex, int colorIndex)
        {
            IRGPass pass = m_PassCompileInfos[passIndex].pass;
            return EffectiveStore(pass.colorBuffers[colorIndex], pass.colorBufferActions[colorIndex].storeAction, passIndex);
        }

        internal RenderBufferStoreAction CompiledDepthStore(int passIndex)
        {
            IRGPass pass = m_PassCompileInfos[passIndex].pass;
            return EffectiveStore(pass.depthBuffer, pass.depthBufferAction.storeAction, passIndex);
        }

        void ApplyMemoryless()
        {
            if (!GraphicsUtility.SupportsMemorylessRenderTargets) return;
            int type = (int)ERGResourceType.Texture;
            for (int index = 0; index < m_ResourcesCompileInfos[type].size; ++index)
            {
                if (m_Resources.IsResourceImported(new RGResourceHandle(index, ERGResourceType.Texture))) continue;
                int group = -1;
                bool depth = false;
                bool attachmentOnly = true;
                int lastPass = -1;
                for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
                {
                    ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                    if (info.culled || !PassUsesTexture(info.pass, index)) continue;
                    if (Samples(info.pass, new RGTextureRef(index, GenerationOfTexture(index)))) { attachmentOnly = false; break; }
                    if (info.mergeGroup < 0) { attachmentOnly = false; break; }
                    if (group < 0) group = info.mergeGroup;
                    else if (group != info.mergeGroup) { attachmentOnly = false; break; }
                    if (info.pass.depthBuffer.IsValid() && info.pass.depthBuffer.handle.index == index) depth = true;
                    lastPass = passIndex;
                }
                if (!attachmentOnly || group < 0 || lastPass < 0) continue;
                RGTextureRef texture = new RGTextureRef(index, GenerationOfTexture(index));
                RenderBufferStoreAction declared = depth
                    ? m_PassCompileInfos[lastPass].pass.depthBufferAction.storeAction
                    : StoreOfColor(m_PassCompileInfos[lastPass].pass, index);
                if (EffectiveStore(texture, declared, lastPass) != RenderBufferStoreAction.DontCare) continue;
                TextureDescriptor descriptor = m_Resources.GetTextureDescriptor(new RGResourceHandle(index, ERGResourceType.Texture));
                RenderTextureMemoryless memoryless = depth ? RenderTextureMemoryless.Depth : RenderTextureMemoryless.Color;
                if ((int)descriptor.msaaSamples > 1) memoryless |= RenderTextureMemoryless.MSAA;
                descriptor.memoryless = memoryless;
                SetTextureDescriptor(index, descriptor);
            }
        }

        ushort GenerationOfTexture(int index)
        {
            for (int passIndex = 0; passIndex < m_PassList.Count; ++passIndex)
            {
                IRGPass pass = m_PassList[passIndex];
                if (pass.depthBuffer.IsValid() && pass.depthBuffer.handle.index == index) return pass.depthBuffer.handle.generation;
                for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                    if (pass.colorBuffers[i].IsValid() && pass.colorBuffers[i].handle.index == index) return pass.colorBuffers[i].handle.generation;
                foreach (RGResourceHandle handle in pass.sampledResources)
                    if (handle.type == ERGResourceType.Texture && handle.index == index) return handle.generation;
                foreach (RGResourceHandle handle in pass.resourceReadLists[(int)ERGResourceType.Texture])
                    if (handle.index == index) return handle.generation;
                foreach (RGResourceHandle handle in pass.resourceWriteLists[(int)ERGResourceType.Texture])
                    if (handle.index == index) return handle.generation;
            }
            return 0;
        }

        static RenderBufferStoreAction StoreOfColor(IRGPass pass, int textureIndex)
        {
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (pass.colorBuffers[i].IsValid() && pass.colorBuffers[i].handle.index == textureIndex)
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
                var indices = new List<int>(scope.passes.Count);
                foreach (IRGPass pass in scope.passes)
                {
                    if (pass.index < 0 || pass.index >= m_PassCompileInfos.size || m_PassCompileInfos[pass.index].culled) continue;
                    if (!indices.Contains(pass.index)) indices.Add(pass.index);
                }
                if (indices.Count == 0) continue;
                indices.Sort();
                int runStart = indices[0];
                int previous = indices[0];
                for (int i = 1; i <= indices.Count; ++i)
                {
                    bool boundary = i == indices.Count
                        || indices[i] != previous + 1
                        || m_PassCompileInfos[indices[i]].segment != m_PassCompileInfos[previous].segment;
                    if (boundary)
                    {
                        m_PassCompileInfos[runStart].beginSamples.Add(scope.sampler);
                        m_PassCompileInfos[previous].endSamples.Add(scope.sampler);
                        if (i < indices.Count) runStart = indices[i];
                    }
                    if (i < indices.Count) previous = indices[i];
                }
            }
        }

        void ValidateGraph()
        {
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                if (info.culled) continue;
                ValidatePassLocal(info.pass, passIndex);
                ValidateReads(info.pass, passIndex);
                if (info.pass.passType == EPassType.Raster)
                    ValidateAttachments(info.pass, passIndex);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ValidateDuplicates(info.pass);
#endif
            }
            ValidateCycles();
        }

        void ValidatePassLocal(IRGPass pass, int passIndex)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (RGResourceHandle handle in pass.resourceReadLists[type])
                    RequirePassLocalOwner(handle, passIndex);
                foreach (RGResourceHandle handle in pass.resourceWriteLists[type])
                    RequirePassLocalOwner(handle, passIndex);
            }
        }

        void RequirePassLocalOwner(in RGResourceHandle handle, int passIndex)
        {
            for (int other = 0; other < m_PassCompileInfos.size; ++other)
            {
                if (other == passIndex || m_PassCompileInfos[other].culled) continue;
                foreach (RGResourceHandle temporal in m_PassCompileInfos[other].pass.temporalResourceList[handle.iType])
                {
                    if (temporal.index == handle.index && temporal.generation == handle.generation)
                        throw new InvalidOperationException($"Pass-local resource {m_Resources.GetResourceName(handle)} is used by '{m_PassCompileInfos[passIndex].pass.name}' but belongs to '{m_PassCompileInfos[other].pass.name}'.");
                }
            }
        }

        void ValidateReads(IRGPass pass, int passIndex)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (RGResourceHandle handle in pass.resourceReadLists[type])
                {
                    if (m_Resources.IsResourceImported(handle)) continue;
                    int firstWrite = GetFirstValidWriteIndex(m_ResourcesCompileInfos[type][handle.index]);
                    if (firstWrite < 0 || passIndex < firstWrite)
                        throw new InvalidOperationException($"'{pass.name}' reads {m_Resources.GetResourceName(handle)} before any pass writes it.");
                }
            }
        }

        void ValidateAttachments(IRGPass pass, int passIndex)
        {
            if (pass.colorBufferMaxIndex > 0 && !pass.depthBuffer.IsValid())
                throw new InvalidOperationException($"'{pass.name}' sets multiple color attachments without a depth buffer.");
            int width = 0, height = 0, samples = 0;
            bool sized = false;
            if (pass.depthBuffer.IsValid())
            {
                if (!TryAttachmentSize(pass.depthBuffer, out width, out height, out samples))
                    throw new InvalidOperationException($"'{pass.name}' has a depth attachment with no size.");
                sized = true;
                if (pass.depthBufferAction.loadAction == RenderBufferLoadAction.Load)
                    RequireEarlierWrite(pass, passIndex, pass.depthBuffer.handle);
            }
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (!pass.colorBuffers[i].IsValid())
                    throw new InvalidOperationException($"'{pass.name}' has a hole in its color attachments at index {i}.");
                if (!TryAttachmentSize(pass.colorBuffers[i], out int colorWidth, out int colorHeight, out int colorSamples))
                    throw new InvalidOperationException($"'{pass.name}' color attachment {i} has no size.");
                if (sized && (colorWidth != width || colorHeight != height || colorSamples != samples))
                    throw new InvalidOperationException($"'{pass.name}' color attachment {i} is {colorWidth}x{colorHeight} x{colorSamples}, which does not match {width}x{height} x{samples}.");
                width = colorWidth; height = colorHeight; samples = colorSamples; sized = true;
                if (pass.colorBufferActions[i].loadAction == RenderBufferLoadAction.Load)
                    RequireEarlierWrite(pass, passIndex, pass.colorBuffers[i].handle);
            }
        }

        void RequireEarlierWrite(IRGPass pass, int passIndex, in RGResourceHandle handle)
        {
            if (m_Resources.IsResourceImported(handle)) return;
            int firstWrite = GetFirstValidWriteIndex(m_ResourcesCompileInfos[handle.iType][handle.index]);
            if (firstWrite < 0 || firstWrite >= passIndex)
                throw new InvalidOperationException($"'{pass.name}' loads {m_Resources.GetResourceName(handle)} but no earlier pass writes it.");
        }

        void ValidateDuplicates(IRGPass pass)
        {
            var seen = new HashSet<int>();
            foreach (RGResourceHandle handle in pass.sampledResources)
            {
                int key = (handle.iType << 24) ^ (handle.index << 8) ^ handle.generation;
                if (!seen.Add(key))
                    throw new InvalidOperationException($"'{pass.name}' declares {m_Resources.GetResourceName(handle)} more than once.");
            }
        }

        void ValidateCycles()
        {
            var edges = new List<int>[m_PassCompileInfos.size];
            for (int i = 0; i < edges.Length; ++i) edges[i] = new List<int>(4);
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                for (int index = 0; index < m_ResourcesCompileInfos[type].size; ++index)
                {
                    ref RGResourceCompileInfo info = ref m_ResourcesCompileInfos[type][index];
                    foreach (int producer in info.producers)
                    {
                        if (m_PassCompileInfos[producer].culled) continue;
                        foreach (int consumer in info.consumers)
                        {
                            if (consumer == producer || m_PassCompileInfos[consumer].culled) continue;
                            if (consumer > producer) edges[producer].Add(consumer);
                            else edges[consumer].Add(producer);
                        }
                    }
                }
            }
            var color = new byte[edges.Length];
            for (int i = 0; i < edges.Length; ++i)
            {
                if (color[i] == 0) WalkCycle(i, edges, color);
            }
        }

        void WalkCycle(int passIndex, List<int>[] edges, byte[] color)
        {
            color[passIndex] = 1;
            foreach (int next in edges[passIndex])
            {
                if (color[next] == 1)
                    throw new InvalidOperationException($"Render graph has a cross-pass resource cycle involving '{m_PassCompileInfos[passIndex].pass.name}' and '{m_PassCompileInfos[next].pass.name}'.");
                if (color[next] == 0) WalkCycle(next, edges, color);
            }
            color[passIndex] = 2;
        }

        internal void NoteProfiledPass(IRGPass pass)
        {
            for (int i = 0; i < m_OpenScopes.Count; ++i)
                m_OpenScopes[i].passes.Add(pass);
        }

        internal void BeginProfileRange(ProfilingSampler sampler)
        {
            if (sampler == null) return;
            var scope = new RGProfileScope { sampler = sampler };
            m_ProfileScopes.Add(scope);
            m_OpenScopes.Add(scope);
        }

        internal void EndProfileRange(ProfilingSampler sampler)
        {
            if (m_OpenScopes.Count == 0) return;
            if (sampler != null && m_OpenScopes[m_OpenScopes.Count - 1].sampler != sampler)
                throw new InvalidOperationException("A profiling scope ended out of order.");
            m_OpenScopes.RemoveAt(m_OpenScopes.Count - 1);
        }
    }
}
