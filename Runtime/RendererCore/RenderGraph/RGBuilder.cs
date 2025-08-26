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
        public int mergeGroupId; // Pass合并组ID，-1表示不属于任何合并组
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
            mergeGroupId = -1; // 初始化合并组ID
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
        List<List<int>> m_PassMergeGroups = new List<List<int>>(); // Pass合并组列表

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
            m_PassMergeGroups.Clear(); // 清理合并组数据

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
                        
                        // 修复异步计算Pass裁剪Bug：
                        // 如果是异步计算Pass且被强制保留(!enablePassCulling)，则不应该被裁剪
                        // 这样的Pass应该被视为有副作用或者是依赖图的有效终点
                        bool isAsyncComputeAndForced = producerInfo.enableAsyncCompute && !producerInfo.enablePassCulling;
                        
                        if (producerInfo.refCount == 0 && !producerInfo.hasSideEffect && producerInfo.enablePassCulling && !isAsyncComputeAndForced)
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
            InferLoadStoreActions();  // 新增：自动推导Load/Store Action
            OptimizePassMerging();    // 新增：Pass合并优化
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
            HashSet<int> processedMergeGroups = new HashSet<int>();

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
                    // 检查这个Pass是否属于合并组
                    if (passInfo.mergeGroupId != -1 && !processedMergeGroups.Contains(passInfo.mergeGroupId))
                    {
                        // 执行整个合并组
                        ExecuteMergedPassGroup(ref graphContext, graphicsCmdBuffer, passInfo.mergeGroupId);
                        processedMergeGroups.Add(passInfo.mergeGroupId);
                    }
                    else if (passInfo.mergeGroupId == -1)
                    {
                        // 执行单个Pass
                        using (new ProfilingScope(graphContext.cmdBuffer, passInfo.pass.customSampler))
                        {
                            PrePassExecute(ref graphContext, passInfo);
                            passInfo.pass.Execute(ref graphContext);
                            PostPassExecute(graphicsCmdBuffer, ref graphContext, ref passInfo);
                        }
                    }
                    // 如果mergeGroupId != -1 但已经处理过，跳过这个Pass
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

        /// <summary>
        /// 执行一个合并的Pass组
        /// 在同一个RenderPass内依次执行组内所有Pass的逻辑
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ExecuteMergedPassGroup(ref RGContext graphContext, CommandBuffer graphicsCmdBuffer, int mergeGroupId)
        {
            var mergeGroup = m_PassMergeGroups[mergeGroupId];
            
            // 使用第一个Pass的信息来开始RenderPass
            int firstPassIndex = mergeGroup[0];
            ref RGPassCompileInfo firstPassInfo = ref m_PassCompileInfos[firstPassIndex];
            
            string mergedPassName = $"MergedPass_Group{mergeGroupId}";
            ProfilingSampler mergedPassSampler = ProfilingSampler.Get(mergedPassName);
            
            using (new ProfilingScope(graphContext.cmdBuffer, mergedPassSampler))
            {
                // 预执行第一个Pass（创建资源、开始RenderPass）
                PrePassExecute(ref graphContext, firstPassInfo);
                
                // 依次执行所有Pass的内容，但在同一个RenderPass内
                foreach (int passIndex in mergeGroup)
                {
                    ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                    IRGPass pass = passInfo.pass;
                    
                    // 为每个Pass添加调试组标记
                    using (new ProfilingScope(graphContext.cmdBuffer, pass.customSampler))
                    {
                        // 执行Pass的内容
                        pass.Execute(ref graphContext);
                    }
                }
                
                // 后执行最后一个Pass（结束RenderPass、释放资源）
                int lastPassIndex = mergeGroup[mergeGroup.Count - 1];
                ref RGPassCompileInfo lastPassInfo = ref m_PassCompileInfos[lastPassIndex];
                PostPassExecute(graphicsCmdBuffer, ref graphContext, ref lastPassInfo);
            }
        }
        
        /// <summary>
        /// 自动推导所有Pass的Load/Store Action
        /// 基于Pass间的拓扑依赖关系和访问标志来确定最优的加载和存储操作
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InferLoadStoreActions()
        {
            // 遍历所有未被裁剪的Pass，按执行顺序分析每个附件
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled || passInfo.pass.passType != EPassType.Raster) 
                    continue;

                IRGPass pass = passInfo.pass;

                // 推导颜色附件的Load/Store Action
                for (int colorIndex = 0; colorIndex <= pass.colorBufferMaxIndex; ++colorIndex)
                {
                    if (!pass.colorBuffers[colorIndex].IsValid()) 
                        continue;

                    EColorAccessFlag accessFlag = pass.colorBufferAccessFlags[colorIndex];
                    RGResourceHandle colorHandle = pass.colorBuffers[colorIndex].handle;
                    
                    // 推导LoadAction
                    RenderBufferLoadAction loadAction = InferColorLoadAction(passIndex, colorHandle, accessFlag);
                    
                    // 推导StoreAction
                    RenderBufferStoreAction storeAction = InferColorStoreAction(passIndex, colorHandle, accessFlag);
                    
                    // 设置推导出的Load/Store Action
                    pass.colorBufferActions[colorIndex] = new RGAttachmentAction(loadAction, storeAction);
                }

                // 推导深度附件的Load/Store Action
                if (pass.depthBuffer.IsValid())
                {
                    EDepthAccessFlag accessFlag = pass.depthBufferAccessFlag;
                    RGResourceHandle depthHandle = pass.depthBuffer.handle;
                    
                    // 推导LoadAction
                    RenderBufferLoadAction loadAction = InferDepthLoadAction(passIndex, depthHandle, accessFlag);
                    
                    // 推导StoreAction  
                    RenderBufferStoreAction storeAction = InferDepthStoreAction(passIndex, depthHandle, accessFlag);
                    
                    // 设置推导出的Load/Store Action
                    pass.depthBufferAction = new RGAttachmentAction(loadAction, storeAction);
                }
            }
        }

        /// <summary>
        /// 推导颜色附件的LoadAction
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferLoadAction InferColorLoadAction(int passIndex, RGResourceHandle colorHandle, EColorAccessFlag accessFlag)
        {
            // 根据访问标志确定基础LoadAction
            switch (accessFlag)
            {
                case EColorAccessFlag.WriteAll:
                case EColorAccessFlag.Discard:
                    // 不关心之前的内容，优先使用DontCare
                    var textureDesc = m_Resources.GetTextureDescriptor(colorHandle);
                    return textureDesc.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                    
                case EColorAccessFlag.Write:
                    // 需要保留现有内容，检查前序Pass
                    return HasPreviousWriter(passIndex, colorHandle) ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare;
                    
                default:
                    return RenderBufferLoadAction.DontCare;
            }
        }

        /// <summary>
        /// 推导颜色附件的StoreAction
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferStoreAction InferColorStoreAction(int passIndex, RGResourceHandle colorHandle, EColorAccessFlag accessFlag)
        {
            // 检查后续是否有Pass读取此资源
            return HasSubsequentReader(passIndex, colorHandle) ? RenderBufferStoreAction.Store : RenderBufferStoreAction.DontCare;
        }

        /// <summary>
        /// 推导深度附件的LoadAction
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferLoadAction InferDepthLoadAction(int passIndex, RGResourceHandle depthHandle, EDepthAccessFlag accessFlag)
        {
            switch (accessFlag)
            {
                case EDepthAccessFlag.ReadOnly:
                    // 只读深度，必须加载
                    return RenderBufferLoadAction.Load;
                    
                case EDepthAccessFlag.ReadWrite:
                    // 读写深度，检查是否有前序写入
                    if (HasPreviousWriter(passIndex, depthHandle))
                    {
                        return RenderBufferLoadAction.Load;
                    }
                    else
                    {
                        var textureDesc = m_Resources.GetTextureDescriptor(depthHandle);
                        return textureDesc.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                    }
                    
                default:
                    return RenderBufferLoadAction.DontCare;
            }
        }

        /// <summary>
        /// 推导深度附件的StoreAction
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferStoreAction InferDepthStoreAction(int passIndex, RGResourceHandle depthHandle, EDepthAccessFlag accessFlag)
        {
            // 如果是只读，不需要存储
            if (accessFlag == EDepthAccessFlag.ReadOnly)
                return RenderBufferStoreAction.DontCare;
                
            // 检查后续是否有Pass读取此资源
            return HasSubsequentReader(passIndex, depthHandle) ? RenderBufferStoreAction.Store : RenderBufferStoreAction.DontCare;
        }

        /// <summary>
        /// 检查指定Pass之前是否有其他Pass写入了指定资源
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasPreviousWriter(int passIndex, RGResourceHandle handle)
        {
            var resourceInfo = m_ResourcesCompileInfos[handle.iType][handle.index];
            foreach (var producerIndex in resourceInfo.producers)
            {
                if (producerIndex < passIndex && !m_PassCompileInfos[producerIndex].culled)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查指定Pass之后是否有其他Pass读取了指定资源
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasSubsequentReader(int passIndex, RGResourceHandle handle)
        {
            var resourceInfo = m_ResourcesCompileInfos[handle.iType][handle.index];
            
            // 检查消费者（读取者）
            foreach (var consumerIndex in resourceInfo.consumers)
            {
                if (consumerIndex > passIndex && !m_PassCompileInfos[consumerIndex].culled)
                {
                    return true;
                }
            }
            
            // 检查生产者（写入者），因为写入也可能需要读取
            foreach (var producerIndex in resourceInfo.producers)
            {
                if (producerIndex > passIndex && !m_PassCompileInfos[producerIndex].culled)
                {
                    return true;
                }
            }
            
            // 检查是否是导入的资源，导入的资源可能会在RenderGraph外部被使用
            if (m_Resources.IsResourceImported(handle))
            {
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Pass合并优化：识别并合并连续的兼容光栅Pass
        /// 基于渲染目标一致性检查来确定哪些Pass可以合并到单个RenderPass中
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void OptimizePassMerging()
        {
            // 遍历所有Pass，寻找可以合并的连续光栅Pass序列
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled || passInfo.pass.passType != EPassType.Raster) 
                    continue;
                
                // 检查当前Pass是否允许合并
                if (!passInfo.pass.enablePassMerge)
                    continue;
                    
                // 如果这个Pass已经是某个合并组的一部分，跳过
                if (passInfo.mergeGroupId != -1)
                    continue;
                    
                // 开始一个新的合并组
                List<int> mergeGroup = new List<int>();
                mergeGroup.Add(passIndex);
                
                // 寻找可以与当前Pass合并的后续连续Pass
                for (int nextIndex = passIndex + 1; nextIndex < m_PassCompileInfos.size; ++nextIndex)
                {
                    ref RGPassCompileInfo nextPassInfo = ref m_PassCompileInfos[nextIndex];
                    
                    // 必须是未裁剪的光栅Pass
                    if (nextPassInfo.culled || nextPassInfo.pass.passType != EPassType.Raster)
                        break;
                        
                    // 必须允许合并
                    if (!nextPassInfo.pass.enablePassMerge)
                        break;
                        
                    // 必须还没有被分配到其他合并组
                    if (nextPassInfo.mergeGroupId != -1)
                        break;
                        
                    // 检查渲染目标是否兼容
                    if (!ArePassesCompatibleForMerging(passInfo.pass, nextPassInfo.pass))
                        break;
                        
                    // 可以合并，添加到组中
                    mergeGroup.Add(nextIndex);
                }
                
                // 如果找到了可以合并的Pass组（至少2个Pass），标记它们
                if (mergeGroup.Count > 1)
                {
                    int groupId = m_PassMergeGroups.Count;
                    m_PassMergeGroups.Add(mergeGroup);
                    
                    foreach (int groupPassIndex in mergeGroup)
                    {
                        m_PassCompileInfos[groupPassIndex].mergeGroupId = groupId;
                    }
                }
            }
        }

        /// <summary>
        /// 检查两个光栅Pass是否可以合并
        /// 合并的条件是它们必须使用完全相同的渲染目标配置
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool ArePassesCompatibleForMerging(IRGPass pass1, IRGPass pass2)
        {
            // 检查颜色附件数量
            if (pass1.colorBufferMaxIndex != pass2.colorBufferMaxIndex)
                return false;
                
            // 检查深度附件
            if (pass1.depthBuffer.handle.index != pass2.depthBuffer.handle.index)
                return false;
                
            // 检查深度访问模式
            if (pass1.depthBufferAccess != pass2.depthBufferAccess)
                return false;
                
            // 检查每个颜色附件
            for (int i = 0; i <= pass1.colorBufferMaxIndex; ++i)
            {
                if (pass1.colorBuffers[i].handle.index != pass2.colorBuffers[i].handle.index)
                    return false;
            }
            
            // 通过所有检查，可以合并
            return true;
        }
        
        public void Dispose()
        {
            m_Resources.Dispose();
        }
    }
}
