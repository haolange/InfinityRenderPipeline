using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
using Unity.Collections;

namespace InfinityTech.Rendering.RenderGraph
{
    public struct RGContext
    {
        public RGObjectPool objectPool;
        public CommandBuffer cmdBuffer;
        public RenderContext renderContext;
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
        public GraphicsFence fence;
        public List<int>[] resourceCreateList;
        public List<int>[] resourceReleaseList;
        public bool enablePassCulling { get { return pass.enablePassCulling; } }
        public bool enableAsyncCompute { get { return pass.enableAsyncCompute; } }

        public void Reset(IRGPass pass)
        {
            this.pass = pass;

            if (resourceCreateList == null)
            {
                resourceCreateList = new List<int>[2];
                resourceReleaseList = new List<int>[2];
                for (int i = 0; i < 2; ++i)
                {
                    resourceCreateList[i] = new List<int>();
                    resourceReleaseList[i] = new List<int>();
                }
            }

            for (int i = 0; i < 2; ++i)
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
        }
    }

    internal struct RGResourceCompileInfo
    {
        public int refCount;
        public bool resourceCreated;
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
            refCount = 0;
        }
    }

    public class RGBuilder 
    {
        public string name;
        bool m_ExecuteExceptionIsRaised;
        RGResourceFactory m_Resources;
        Stack<int> m_CullingStack = new Stack<int>();
        List<IRGPass> m_PassList = new List<IRGPass>(64);
        RGObjectPool m_ObjectPool = new RGObjectPool();
        DynamicArray<RGPassCompileInfo> m_PassCompileInfos;
        DynamicArray<RGResourceCompileInfo>[] m_ResourcesCompileInfos;

        public RGBuilder(string name)
        {
            this.name = name;
            this.m_Resources = new RGResourceFactory();
            this.m_PassCompileInfos = new DynamicArray<RGPassCompileInfo>();
            this.m_ResourcesCompileInfos = new DynamicArray<RGResourceCompileInfo>[2];

            for (int i = 0; i < 2; ++i)
            {
                this.m_ResourcesCompileInfos[i] = new DynamicArray<RGResourceCompileInfo>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef ImportBuffer(ComputeBuffer buffer)
        {
            return m_Resources.ImportBuffer(buffer);
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

        /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ImportBackbuffer(in RenderTargetIdentifier backBuffer, in int shaderProperty = 0)
        {
            return m_Resources.ImportBackbuffer(backBuffer, shaderProperty);
        }*/

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
            RGComputePass<T> computePass = m_ObjectPool.Get<RGComputePass<T>>();
            computePass.Clear();
            computePass.name = profilerSampler.name;
            computePass.index = m_PassList.Count;
            computePass.customSampler = profilerSampler;
            m_PassList.Add(computePass);
            return new RGComputePassRef(computePass, m_Resources);
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
            return new RGRasterPassRef(rasterPass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(RenderContext renderContext, ResourcePool resourcePool, CommandBuffer cmdBuffer)
        {
            RGContext graphContext;
            {
                graphContext.cmdBuffer = cmdBuffer;
                graphContext.objectPool = m_ObjectPool;
                graphContext.renderContext = renderContext;
            }
            m_ExecuteExceptionIsRaised = false;

            try
            {
                m_Resources.BeginRender();
                CompilePass();
                ExecutePass(ref graphContext);
            } 
            catch (Exception exception) 
            {
                if (!m_ExecuteExceptionIsRaised) 
                { 
                    Debug.LogException(exception); 
                }
                m_ExecuteExceptionIsRaised = true;

                Debug.LogError("RenderGraph Execute error");
            } 
            finally 
            {
                ClearPass();
                m_Resources.EndRender();
            }
    }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ClearPass()
        {
            foreach (var pass in m_PassList)
            {
                pass.Release(m_ObjectPool);
            }

            m_PassList.Clear();
            m_Resources.Clear();

            for (int i = 0; i < 2; ++i)
            {
                m_ResourcesCompileInfos[i].Clear();
            }

            m_PassCompileInfos.Clear();
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

            m_PassCompileInfos.Resize(m_PassList.Count);
            for (int i = 0; i < m_PassCompileInfos.size; ++i)
            {
                m_PassCompileInfos[i].Reset(m_PassList[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CountPassReference()
        {
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];

                for (int type = 0; type < 2; ++type)
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
                        if (m_Resources.IsResourceImported(resource)) {
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
            for (int type = 0; type < 2; ++type)
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
                        
                        // 修复异步计算Pass裁剪Bug：对于强制保留的Pass（enablePassCulling=false），
                        // 即使其输出资源未被后续Pass消费，也不应被裁剪。
                        // 这样的Pass会被视为依赖图中的"根节点"或有效终点。
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
        void UpdateResourceSynchronization(ref int lastGraphicsPipeSync, ref int lastComputePipeSync, in int currentPassIndex, in RGResourceCompileInfo resourceInfo)
        {
            int lastProducer = GetLatestProducerIndex(currentPassIndex, resourceInfo);
            if (lastProducer != -1)
            {
                ref RGPassCompileInfo currentPassInfo = ref m_PassCompileInfos[currentPassIndex];

                //If the passes are on different pipes, we need synchronization.
                if (m_PassCompileInfos[lastProducer].enableAsyncCompute != currentPassInfo.enableAsyncCompute)
                {
                    // Pass is on compute pipe, need sync with graphics pipe.
                    if (currentPassInfo.enableAsyncCompute)
                    {
                        if (lastProducer > lastGraphicsPipeSync)
                        {
                            UpdatePassSynchronization(ref currentPassInfo, ref m_PassCompileInfos[lastProducer], currentPassIndex, lastProducer, ref lastGraphicsPipeSync);
                        }
                    } 
                    else 
                    {
                        if (lastProducer > lastComputePipeSync)
                        {
                            UpdatePassSynchronization(ref currentPassInfo, ref m_PassCompileInfos[lastProducer], currentPassIndex, lastProducer, ref lastComputePipeSync);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLatestProducerIndex(in int passIndex, in RGResourceCompileInfo resourceInfo)
        {
            // We want to know the highest pass index below the current pass that writes to the resource.
            int result = -1;
            foreach (var producer in resourceInfo.producers)
            {
                // producers are by construction in increasing order.
                if (producer < passIndex)
                {
                    result = producer;
                } else {
                    return result;
                }
            }

            return result;
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

        /// <summary>
        /// 自动推导附件的Load/Store Action。
        /// 根据Pass间的拓扑依赖关系和访问标志，自动计算最优的LoadAction和StoreAction。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DeduceAttachmentLoadStoreActions()
        {
            // 遍历所有未被裁剪的光栅Pass
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled || passInfo.pass.passType != EPassType.Raster)
                    continue;

                IRGPass pass = passInfo.pass;

                // 推导颜色附件的Load/Store Action
                for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                {
                    if (!pass.colorBuffers[i].IsValid())
                        continue;

                    var colorAccessFlag = pass.colorBufferAccessFlags[i];
                    var textureHandle = pass.colorBuffers[i].handle;
                    
                    // 推导LoadAction
                    RenderBufferLoadAction loadAction = DeduceColorLoadAction(passIndex, textureHandle, colorAccessFlag);
                    
                    // 推导StoreAction
                    RenderBufferStoreAction storeAction = DeduceColorStoreAction(passIndex, textureHandle, colorAccessFlag);
                    
                    // 设置推导出的Load/Store Action
                    pass.colorBufferActions[i] = new RGAttachmentAction(loadAction, storeAction);
                }

                // 推导深度附件的Load/Store Action
                if (pass.depthBuffer.IsValid())
                {
                    var depthAccessFlag = pass.depthBufferAccessFlag;
                    var depthHandle = pass.depthBuffer.handle;
                    
                    // 推导LoadAction
                    RenderBufferLoadAction loadAction = DeduceDepthLoadAction(passIndex, depthHandle, depthAccessFlag);
                    
                    // 推导StoreAction  
                    RenderBufferStoreAction storeAction = DeduceDepthStoreAction(passIndex, depthHandle, depthAccessFlag);
                    
                    // 设置推导出的Load/Store Action
                    pass.depthBufferAction = new RGAttachmentAction(loadAction, storeAction);
                }
            }
        }

        /// <summary>
        /// 推导颜色附件的LoadAction。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferLoadAction DeduceColorLoadAction(int passIndex, RGResourceHandle textureHandle, EColorAccessFlag accessFlag)
        {
            // 根据访问标志决定LoadAction
            switch (accessFlag)
            {
                case EColorAccessFlag.WriteAll:
                case EColorAccessFlag.Discard:
                    // 全屏写入或显式丢弃，无需加载之前的内容
                    var textureDesc = m_Resources.GetTextureDescriptor(textureHandle);
                    return textureDesc.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;

                case EColorAccessFlag.Write:
                    // 需要保留现有像素值的写入操作
                    return IsFirstWriteToTexture(passIndex, textureHandle) ? 
                        RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load;

                case EColorAccessFlag.Read:
                    // 读取操作必须加载之前的内容
                    return RenderBufferLoadAction.Load;

                default:
                    return RenderBufferLoadAction.Load;
            }
        }

        /// <summary>
        /// 推导颜色附件的StoreAction。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferStoreAction DeduceColorStoreAction(int passIndex, RGResourceHandle textureHandle, EColorAccessFlag accessFlag)
        {
            // 检查是否有后续Pass读取此资源
            bool hasSubsequentRead = HasSubsequentRead(passIndex, textureHandle);
            
            // 检查是否为导入的纹理（可能需要在RG外部使用）
            bool isImported = m_Resources.IsResourceImported(textureHandle);
            
            // 如果后续有读取或者是导入的纹理，必须存储
            if (hasSubsequentRead || isImported)
            {
                return RenderBufferStoreAction.Store;
            }
            
            // 否则可以丢弃以节省带宽
            return RenderBufferStoreAction.DontCare;
        }

        /// <summary>
        /// 推导深度附件的LoadAction。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferLoadAction DeduceDepthLoadAction(int passIndex, RGResourceHandle depthHandle, EDepthAccessFlag accessFlag)
        {
            if (accessFlag == EDepthAccessFlag.ReadOnly)
            {
                // 只读深度测试，必须加载之前的深度值
                return RenderBufferLoadAction.Load;
            }
            else // EDepthAccessFlag.ReadWrite
            {
                // 检查是否为首次写入
                bool isFirstWrite = IsFirstWriteToTexture(passIndex, depthHandle);
                if (isFirstWrite)
                {
                    var depthDesc = m_Resources.GetTextureDescriptor(depthHandle);
                    return depthDesc.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                }
                else
                {
                    return RenderBufferLoadAction.Load;
                }
            }
        }

        /// <summary>
        /// 推导深度附件的StoreAction。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferStoreAction DeduceDepthStoreAction(int passIndex, RGResourceHandle depthHandle, EDepthAccessFlag accessFlag)
        {
            // 检查是否有后续Pass读取此深度缓冲
            bool hasSubsequentRead = HasSubsequentRead(passIndex, depthHandle);
            
            // 检查是否为导入的纹理
            bool isImported = m_Resources.IsResourceImported(depthHandle);
            
            // 如果后续有读取或者是导入的纹理，必须存储
            if (hasSubsequentRead || isImported)
            {
                return RenderBufferStoreAction.Store;
            }
            
            // 否则可以丢弃
            return RenderBufferStoreAction.DontCare;
        }

        /// <summary>
        /// 检查指定Pass是否为纹理的首次写入。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsFirstWriteToTexture(int passIndex, RGResourceHandle textureHandle)
        {
            var resourceInfo = m_ResourcesCompileInfos[textureHandle.iType][textureHandle.index];
            
            foreach (var producerIndex in resourceInfo.producers)
            {
                if (!m_PassCompileInfos[producerIndex].culled && producerIndex < passIndex)
                {
                    return false; // 找到了之前的写入者
                }
            }
            
            return true; // 没有找到之前的写入者，这是首次写入
        }

        /// <summary>
        /// 检查是否有后续Pass读取指定资源。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasSubsequentRead(int passIndex, RGResourceHandle resourceHandle)
        {
            var resourceInfo = m_ResourcesCompileInfos[resourceHandle.iType][resourceHandle.index];
            
            foreach (var consumerIndex in resourceInfo.consumers)
            {
                if (!m_PassCompileInfos[consumerIndex].culled && consumerIndex > passIndex)
                {
                    return true; // 找到了后续的读取者
                }
            }
            
            return false; // 没有找到后续的读取者
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

                for (int type = 0; type < 2; ++type)
                {
                    var resourcesInfo = m_ResourcesCompileInfos[type];
                    foreach (int resource in passInfo.pass.resourceReadLists[type])
                    {
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resource]);
                    }

                    foreach (int resource in passInfo.pass.resourceWriteLists[type])
                    {
                        UpdateResourceSynchronization(ref lastGraphicsPipeSync, ref lastComputePipeSync, passIndex, resourcesInfo[resource]);
                    }
                }
            }

            for (int type = 0; type < 2; ++type)
            {
                var resourceInfos = m_ResourcesCompileInfos[type];
                for (int i = 0; i < resourceInfos.size; ++i)
                {
                    RGResourceCompileInfo resourceInfo = resourceInfos[i];

                    int firstWriteIndex = GetFirstValidWriteIndex(resourceInfo);
                    if (firstWriteIndex != -1) 
                    {
                        m_PassCompileInfos[firstWriteIndex].resourceCreateList[type].Add(i);
                    }

                    int lastReadPassIndex = Math.Max(GetLatestValidReadIndex(resourceInfo), GetLatestValidWriteIndex(resourceInfo));
                    if (lastReadPassIndex != -1)
                    {
                        if (m_PassCompileInfos[lastReadPassIndex].enableAsyncCompute)
                        {
                            int currentPassIndex = lastReadPassIndex;
                            int firstWaitingPassIndex = m_PassCompileInfos[currentPassIndex].syncFromPassIndex;
                            while (firstWaitingPassIndex == -1 && currentPassIndex < m_PassCompileInfos.size)
                            {
                                currentPassIndex++;
                                if (m_PassCompileInfos[currentPassIndex].enableAsyncCompute) 
                                {
                                    firstWaitingPassIndex = m_PassCompileInfos[currentPassIndex].syncFromPassIndex;
                                }
                            }

                            ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[Math.Max(0, firstWaitingPassIndex - 1)];
                            passInfo.resourceReleaseList[type].Add(i);

                            if (currentPassIndex == m_PassCompileInfos.size) 
                            {
                                IRGPass invalidPass = m_PassList[lastReadPassIndex];
                                throw new InvalidOperationException($"Async pass {invalidPass.name} was never synchronized on the graphics pipeline.");
                            }
                        } 
                        else 
                        {
                            ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[lastReadPassIndex];
                            passInfo.resourceReleaseList[type].Add(i);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CompilePass()
        {
            InitializeCompileData();
            CountPassReference();
            CullingUnusedPass();
            DeduceAttachmentLoadStoreActions(); // 新增：推导附件的Load/Store Action
            UpdateResource();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetRenderTarget(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            var pass = passCompileInfo.pass;
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
                        loadOpArray[i] = pass.colorBufferActions[i].loadAction;
                        storeOpArray[i] = pass.colorBufferActions[i].storeAction;
                    }

                    if (pass.depthBuffer.IsValid()) 
                    {
                        RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, m_Resources.GetTexture(pass.depthBuffer), pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction);
                        renderTargetBinding.flags = RenderTargetFlags.ReadOnlyDepthStencil;
                        graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);

                        //CoreUtils.SetRenderTarget(graphContext.cmdBuffer, mrtArray, m_Resources.GetTexture(pass.depthBuffer));
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
                            loadOpArray[0] = pass.colorBufferActions[0].loadAction;
                            storeOpArray[0] = pass.colorBufferActions[0].storeAction;

                            RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, m_Resources.GetTexture(pass.depthBuffer), pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction);
                            renderTargetBinding.flags = RenderTargetFlags.ReadOnlyDepthStencil;
                            graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);

                            //CoreUtils.SetRenderTarget(graphContext.cmdBuffer, m_Resources.GetTexture(pass.colorBuffers[0]), pass.colorBufferActions[0].loadAction, pass.colorBufferActions[0].storeAction, m_Resources.GetTexture(pass.depthBuffer), pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction);
                        } 
                        else
                        {
                            mrtArray[0] = m_Resources.GetTexture(pass.depthBuffer);
                            loadOpArray[0] = pass.depthBufferAction.loadAction;
                            storeOpArray[0] = pass.depthBufferAction.storeAction;

                            RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, m_Resources.GetTexture(pass.depthBuffer), pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction);
                            renderTargetBinding.flags = RenderTargetFlags.ReadOnlyDepthStencil;
                            graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);

                            //CoreUtils.SetRenderTarget(graphContext.cmdBuffer, m_Resources.GetTexture(pass.depthBuffer), pass.depthBufferAction.loadAction, pass.depthBufferAction.storeAction);
                        }
                    } 
                    else 
                    {
                        RenderTargetIdentifier[] mrtArray = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(1);
                        RenderBufferLoadAction[] loadOpArray = graphContext.objectPool.GetTempArray<RenderBufferLoadAction>(1);
                        RenderBufferStoreAction[] storeOpArray = graphContext.objectPool.GetTempArray<RenderBufferStoreAction>(1);

                        mrtArray[0] = m_Resources.GetTexture(pass.colorBuffers[0]);
                        loadOpArray[0] = pass.colorBufferActions[0].loadAction;
                        storeOpArray[0] = pass.colorBufferActions[0].storeAction;

                        RenderTargetBinding renderTargetBinding = new RenderTargetBinding(mrtArray, loadOpArray, storeOpArray, new RenderTargetIdentifier(), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
                        renderTargetBinding.flags = RenderTargetFlags.ReadOnlyDepthStencil;
                        graphContext.cmdBuffer.SetRenderTarget(renderTargetBinding);

                        //CoreUtils.SetRenderTarget(graphContext.cmdBuffer, m_Resources.GetTexture(pass.colorBuffers[0]), pass.colorBufferActions[0].loadAction, pass.colorBufferActions[0].storeAction);
                    }
                }
            }
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
                                attachmentDescriptor.clearColor = Color.black;
                                attachmentDescriptor.clearDepth = 1;
                                attachmentDescriptor.clearStencil = 0;
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
                            depthAttachmentDescriptor.clearColor = Color.black;
                            depthAttachmentDescriptor.clearDepth = 1;
                            depthAttachmentDescriptor.clearStencil = 0;
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
                                depthAttachmentDescriptor.clearColor = Color.black;
                                depthAttachmentDescriptor.clearDepth = 1;
                                depthAttachmentDescriptor.clearStencil = 0;
                            }
                            AttachmentDescriptor colorAttachmentDescriptor = new AttachmentDescriptor();
                            {
                                colorAttachmentDescriptor.loadAction = pass.colorBufferActions[0].loadAction;
                                colorAttachmentDescriptor.storeAction = pass.colorBufferActions[0].storeAction;
                                colorAttachmentDescriptor.graphicsFormat = colorBuffer.graphicsFormat;
                                colorAttachmentDescriptor.loadStoreTarget = colorBuffer;
                                colorAttachmentDescriptor.clearColor = Color.black;
                                colorAttachmentDescriptor.clearDepth = 1;
                                colorAttachmentDescriptor.clearStencil = 0;
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
                                attachmentDescriptor.clearColor = Color.black;
                                attachmentDescriptor.clearDepth = 1;
                                attachmentDescriptor.clearStencil = 0;
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
                            colorAttachmentDescriptor.clearColor = Color.black;
                            colorAttachmentDescriptor.clearDepth = 1;
                            colorAttachmentDescriptor.clearStencil = 0;
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

            foreach (var bufferHandle in passCompileInfo.resourceCreateList[(int)ERGResourceType.Buffer]) 
            {
                m_Resources.CreateBufferResource(bufferHandle);
            }

            foreach (var textureHandle in passCompileInfo.resourceCreateList[(int)ERGResourceType.Texture]) 
            {
                m_Resources.CreateTextureResource(ref graphContext, textureHandle);
            }

            graphContext.renderContext.scriptableRenderContext.ExecuteCommandBuffer(graphContext.cmdBuffer);
            graphContext.cmdBuffer.Clear();

            switch (pass.passType)
            {
                case EPassType.Compute:
                case EPassType.RayTracing:
                    if (pass.enableAsyncCompute)
                    {
                        CommandBuffer asyncCmdBuffer = CommandBufferPool.Get(pass.name);
                        asyncCmdBuffer.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                        graphContext.cmdBuffer = asyncCmdBuffer;
                    }
                    break;

                case EPassType.Raster:
                    if (passCompileInfo.syncToPassIndex != -1)
                    {
                        graphContext.cmdBuffer.WaitOnAsyncGraphicsFence(m_PassCompileInfos[passCompileInfo.syncToPassIndex].fence);
                    }

                    //SetRenderTarget(ref graphContext, passCompileInfo);
                    BeginRasterPass(ref graphContext, passCompileInfo);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PostPassExecute(CommandBuffer graphicsCmdBuffer, ref RGContext graphContext, ref RGPassCompileInfo passCompileInfo)
        {
            IRGPass pass = passCompileInfo.pass;

            switch (pass.passType)
            {
                case EPassType.Compute:
                case EPassType.RayTracing:
                    if (passCompileInfo.needGraphicsFence)
                    {
                        passCompileInfo.fence = graphContext.cmdBuffer.CreateAsyncGraphicsFence();
                    }

                    if (pass.enableAsyncCompute)
                    {
                        graphContext.renderContext.scriptableRenderContext.ExecuteCommandBufferAsync(graphContext.cmdBuffer, ComputeQueueType.Background);
                        CommandBufferPool.Release(graphContext.cmdBuffer);
                        graphContext.cmdBuffer = graphicsCmdBuffer;
                    }
                    break;

                case EPassType.Raster:
                    EndRasterPass(ref graphContext, passCompileInfo);
                    break;
            }

            m_ObjectPool.ReleaseAllTempAlloc();

            foreach (var buffer in passCompileInfo.resourceReleaseList[(int)ERGResourceType.Buffer]) 
            {
                m_Resources.ReleaseBufferResource(buffer);
            }

            foreach (var texture in passCompileInfo.resourceReleaseList[(int)ERGResourceType.Texture]) 
            {
                m_Resources.ReleaseTextureResource(texture);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ExecutePass(ref RGContext graphContext)
        {
            CommandBuffer graphicsCmdBuffer = graphContext.cmdBuffer;

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

                try
                {
                    using (new ProfilingScope(graphContext.cmdBuffer, passInfo.pass.customSampler))
                    {
                        PrePassExecute(ref graphContext, passInfo);
                        passInfo.pass.Execute(ref graphContext);
                        PostPassExecute(graphicsCmdBuffer, ref graphContext, ref passInfo);
                    }
                } 
                catch (Exception e) 
                {
                    m_ExecuteExceptionIsRaised = true;
                    Debug.LogError($"RenderGraph Execute error at pass {passInfo.pass.name} ({passIndex})");
                    Debug.LogException(e);
                    throw;
                }
            }
        }
        
        public void Dispose()
        {
            m_Resources.Dispose();
        }
    }
}
