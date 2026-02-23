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
        public bool enablePassMerge { get { return pass.enablePassMerge; } }
        
        // Pass合并相关字段
        public int mergeGroupIndex; // 所属的合并组索引，-1表示不参与合并
        public bool isGroupLeader; // 是否是合并组的主导Pass

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
            mergeGroupIndex = -1;
            isGroupLeader = false;
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

    /// <summary>
    /// Pass合并组的数据结构，包含一个连续的光栅Pass序列
    /// </summary>
    internal struct RGPassMergeGroup
    {
        public int startPassIndex;  // 组内第一个Pass的索引
        public int endPassIndex;    // 组内最后一个Pass的索引
        public int passCount;       // 组内Pass的数量
        
        public RGPassMergeGroup(int startIndex, int endIndex)
        {
            startPassIndex = startIndex;
            endPassIndex = endIndex;
            passCount = endIndex - startIndex + 1;
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
        List<RGPassMergeGroup> m_PassMergeGroups = new List<RGPassMergeGroup>();

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
            m_PassMergeGroups.Clear();

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
                        
                        // 修复异步计算Pass裁剪Bug：强制保留的Pass不应该被裁剪，即使它们的输出没有被后续Pass读取
                        // 这些Pass被视为依赖图的有效终点（根节点）
                        bool shouldCull = producerInfo.refCount == 0 && 
                                         !producerInfo.hasSideEffect && 
                                         producerInfo.enablePassCulling;
                        
                        // 对于异步计算Pass，如果它被强制保留（!enablePassCulling），则不应该被裁剪
                        // 即使它的输出资源没有被任何后续Pass读取
                        if (shouldCull && producerInfo.enableAsyncCompute && !producerInfo.enablePassCulling)
                        {
                            shouldCull = false;
                        }
                        
                        if (shouldCull)
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
            InferLoadStoreActions();
            AnalyzePassMerging();
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
                    // 检查是否是合并组的成员
                    if (passInfo.mergeGroupIndex != -1)
                    {
                        // 只有组长负责执行整个合并组
                        if (passInfo.isGroupLeader)
                        {
                            ExecuteMergedPassGroup(ref graphContext, graphicsCmdBuffer, passInfo.mergeGroupIndex);
                        }
                        // 非组长的Pass跳过执行（已在组长执行时处理）
                    }
                    else
                    {
                        // 普通单独执行的Pass
                        using (new ProfilingScope(graphContext.cmdBuffer, passInfo.pass.customSampler))
                        {
                            PrePassExecute(ref graphContext, passInfo);
                            passInfo.pass.Execute(ref graphContext);
                            PostPassExecute(graphicsCmdBuffer, ref graphContext, ref passInfo);
                        }
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ExecuteMergedPassGroup(ref RGContext graphContext, CommandBuffer graphicsCmdBuffer, int groupIndex)
        {
            // 执行一个合并的Pass组
            var group = m_PassMergeGroups[groupIndex];
            
            // 使用组长Pass的profiler sampler作为整个组的名称
            var leaderPass = m_PassCompileInfos[group.startPassIndex].pass;
            using (new ProfilingScope(graphContext.cmdBuffer, leaderPass.customSampler))
            {
                // 执行组长的PrePassExecute（这会开始RenderPass）
                ref var leaderPassInfo = ref m_PassCompileInfos[group.startPassIndex];
                PrePassExecute(ref graphContext, leaderPassInfo);
                
                // 执行组内所有Pass的Execute回调
                for (int passIndex = group.startPassIndex; passIndex <= group.endPassIndex; ++passIndex)
                {
                    ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                    
                    if (passInfo.culled || passInfo.pass.passType != EPassType.Raster)
                        continue;
                        
                    // 为每个Pass创建独立的Profiler调试组
                    using (new ProfilingScope(graphContext.cmdBuffer, passInfo.pass.customSampler))
                    {
                        passInfo.pass.Execute(ref graphContext);
                    }
                }
                
                // 执行组长的PostPassExecute（这会结束RenderPass）
                PostPassExecute(graphicsCmdBuffer, ref graphContext, ref leaderPassInfo);
            }
        }
        
        public void Dispose()
        {
            m_Resources.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InferLoadStoreActions()
        {
            // 自动推导Load/Store Action的逻辑
            // 遍历所有未被裁剪的Pass，分析其颜色和深度附件的访问模式
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

                    RGTextureRef colorBuffer = pass.colorBuffers[colorIndex];
                    EColorAccessFlag accessFlag = pass.colorBufferAccessFlags[colorIndex];

                    // 推导LoadAction
                    RenderBufferLoadAction loadAction = InferColorLoadAction(colorBuffer, accessFlag, passIndex);
                    // 推导StoreAction
                    RenderBufferStoreAction storeAction = InferColorStoreAction(colorBuffer, passIndex);

                    pass.colorBufferActions[colorIndex] = new RGAttachmentAction(loadAction, storeAction);
                }

                // 推导深度附件的Load/Store Action
                if (pass.depthBuffer.IsValid())
                {
                    RGTextureRef depthBuffer = pass.depthBuffer;
                    EDepthAccessFlag accessFlag = pass.depthBufferAccessFlag;

                    // 推导LoadAction
                    RenderBufferLoadAction loadAction = InferDepthLoadAction(depthBuffer, accessFlag, passIndex);
                    // 推导StoreAction  
                    RenderBufferStoreAction storeAction = InferDepthStoreAction(depthBuffer, passIndex);

                    pass.depthBufferAction = new RGAttachmentAction(loadAction, storeAction);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferLoadAction InferColorLoadAction(RGTextureRef colorBuffer, EColorAccessFlag accessFlag, int currentPassIndex)
        {
            // 根据颜色附件的访问标志和前序Pass的写入情况推导LoadAction
            switch (accessFlag)
            {
                case EColorAccessFlag.WriteAll:
                case EColorAccessFlag.Discard:
                    // WriteAll和Discard都表示不需要加载之前的数据
                    var descriptor = m_Resources.GetTextureDescriptor(colorBuffer.handle);
                    return descriptor.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;

                case EColorAccessFlag.Write:
                    // Write表示需要在现有像素基础上写入，需要判断是否有前序写入
                    int lastWritePass = GetLastWritePassForTexture(colorBuffer, currentPassIndex);
                    if (lastWritePass == -1)
                    {
                        // 首次使用或者前序没有写入，检查是否有ClearValue
                        var descriptor = m_Resources.GetTextureDescriptor(colorBuffer.handle);
                        return descriptor.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                    }
                    else
                    {
                        // 前序有写入，需要加载之前的数据
                        IRGPass lastWritePassRef = m_PassCompileInfos[lastWritePass].pass;
                        // 检查前序Pass的写入方式
                        bool previousPassDiscarded = WasColorBufferDiscardedInPass(lastWritePassRef, colorBuffer);
                        if (previousPassDiscarded)
                        {
                            var descriptor = m_Resources.GetTextureDescriptor(colorBuffer.handle);
                            return descriptor.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                        }
                        return RenderBufferLoadAction.Load;
                    }

                default:
                    return RenderBufferLoadAction.Load;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferStoreAction InferColorStoreAction(RGTextureRef colorBuffer, int currentPassIndex)
        {
            // 检查后续Pass是否会读取此颜色附件
            bool hasSubsequentRead = HasSubsequentReadForTexture(colorBuffer, currentPassIndex);
            bool isImported = m_Resources.IsResourceImported(colorBuffer.handle);

            // 如果后续有读取或者是导入的资源（可能在RG外被使用），则需要Store
            if (hasSubsequentRead || isImported)
            {
                return RenderBufferStoreAction.Store;
            }
            
            // 否则可以DontCare以节省带宽
            return RenderBufferStoreAction.DontCare;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferLoadAction InferDepthLoadAction(RGTextureRef depthBuffer, EDepthAccessFlag accessFlag, int currentPassIndex)
        {
            // 根据深度附件的访问标志和前序Pass的写入情况推导LoadAction
            switch (accessFlag)
            {
                case EDepthAccessFlag.Discard:
                    // Discard表示不需要加载之前的数据
                    var descriptor = m_Resources.GetTextureDescriptor(depthBuffer.handle);
                    return descriptor.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;

                case EDepthAccessFlag.ReadOnly:
                case EDepthAccessFlag.ReadWrite:
                    // ReadOnly和ReadWrite都可能需要加载之前的数据
                    int lastWritePass = GetLastWritePassForTexture(depthBuffer, currentPassIndex);
                    if (lastWritePass == -1)
                    {
                        // 首次使用或者前序没有写入
                        var descriptor = m_Resources.GetTextureDescriptor(depthBuffer.handle);
                        return descriptor.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                    }
                    else
                    {
                        // 前序有写入，需要加载之前的数据
                        IRGPass lastWritePassRef = m_PassCompileInfos[lastWritePass].pass;
                        bool previousPassDiscarded = WasDepthBufferDiscardedInPass(lastWritePassRef, depthBuffer);
                        if (previousPassDiscarded)
                        {
                            var descriptor = m_Resources.GetTextureDescriptor(depthBuffer.handle);
                            return descriptor.clearBuffer ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
                        }
                        return RenderBufferLoadAction.Load;
                    }

                default:
                    return RenderBufferLoadAction.Load;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderBufferStoreAction InferDepthStoreAction(RGTextureRef depthBuffer, int currentPassIndex)
        {
            // 检查后续Pass是否会读取此深度附件
            bool hasSubsequentRead = HasSubsequentReadForTexture(depthBuffer, currentPassIndex);
            bool isImported = m_Resources.IsResourceImported(depthBuffer.handle);

            // 如果后续有读取或者是导入的资源，则需要Store
            if (hasSubsequentRead || isImported)
            {
                return RenderBufferStoreAction.Store;
            }
            
            // 否则可以DontCare以节省带宽
            return RenderBufferStoreAction.DontCare;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLastWritePassForTexture(RGTextureRef texture, int beforePassIndex)
        {
            // 查找在给定Pass之前最后一个写入指定纹理的Pass
            var resourceInfo = m_ResourcesCompileInfos[(int)ERGResourceType.Texture][texture.handle.index];
            
            int lastWriter = -1;
            foreach (int writerIndex in resourceInfo.producers)
            {
                if (writerIndex < beforePassIndex && !m_PassCompileInfos[writerIndex].culled)
                {
                    lastWriter = writerIndex;
                }
                else if (writerIndex >= beforePassIndex)
                {
                    break; // 生产者列表是按索引排序的
                }
            }
            
            return lastWriter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasSubsequentReadForTexture(RGTextureRef texture, int afterPassIndex)
        {
            // 检查在给定Pass之后是否有其他Pass读取指定纹理
            var resourceInfo = m_ResourcesCompileInfos[(int)ERGResourceType.Texture][texture.handle.index];
            
            foreach (int readerIndex in resourceInfo.consumers)
            {
                if (readerIndex > afterPassIndex && !m_PassCompileInfos[readerIndex].culled)
                {
                    return true;
                }
            }
            
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool WasColorBufferDiscardedInPass(IRGPass pass, RGTextureRef colorBuffer)
        {
            // 检查指定Pass中颜色附件是否使用了Discard或WriteAll访问标志
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (pass.colorBuffers[i].handle.index == colorBuffer.handle.index)
                {
                    EColorAccessFlag flag = pass.colorBufferAccessFlags[i];
                    return flag == EColorAccessFlag.Discard || flag == EColorAccessFlag.WriteAll;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool WasDepthBufferDiscardedInPass(IRGPass pass, RGTextureRef depthBuffer)
        {
            // 检查指定Pass中深度附件是否使用了Discard访问标志
            if (pass.depthBuffer.handle.index == depthBuffer.handle.index)
            {
                return pass.depthBufferAccessFlag == EDepthAccessFlag.Discard;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AnalyzePassMerging()
        {
            // 分析并识别可以合并的连续光栅Pass序列
            m_PassMergeGroups.Clear();
            
            int currentGroupStart = -1;
            IRGPass previousPass = null;
            
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref RGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];
                
                // 跳过被裁剪的Pass
                if (passInfo.culled)
                    continue;
                    
                IRGPass currentPass = passInfo.pass;
                
                // 只处理光栅Pass
                if (currentPass.passType != EPassType.Raster)
                {
                    // 非光栅Pass打断序列，结束当前组
                    if (currentGroupStart != -1)
                    {
                        FinalizeMergeGroup(currentGroupStart, passIndex - 1);
                        currentGroupStart = -1;
                    }
                    previousPass = null;
                    continue;
                }
                
                // 检查是否可以与前一个Pass合并
                bool canMergeWithPrevious = false;
                if (previousPass != null && currentGroupStart != -1)
                {
                    canMergeWithPrevious = CanMergePasses(previousPass, currentPass);
                }
                
                if (canMergeWithPrevious)
                {
                    // 继续当前组
                    // 无需特殊处理，组的起始点保持不变
                }
                else
                {
                    // 不能合并，结束当前组(如果存在)
                    if (currentGroupStart != -1)
                    {
                        FinalizeMergeGroup(currentGroupStart, passIndex - 1);
                    }
                    
                    // 开始新组，但前提是当前Pass允许合并
                    if (currentPass.enablePassMerge)
                    {
                        currentGroupStart = passIndex;
                    }
                    else
                    {
                        currentGroupStart = -1;
                    }
                }
                
                previousPass = currentPass;
            }
            
            // 处理最后一个可能的组
            if (currentGroupStart != -1)
            {
                FinalizeMergeGroup(currentGroupStart, m_PassCompileInfos.size - 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool CanMergePasses(IRGPass pass1, IRGPass pass2)
        {
            // 检查两个光栅Pass是否可以合并
            
            // 1. 两个Pass都必须允许合并
            if (!pass1.enablePassMerge || !pass2.enablePassMerge)
                return false;
            
            // 2. 渲染目标必须完全一致 (颜色附件和深度附件)
            
            // 检查颜色附件数量
            if (pass1.colorBufferMaxIndex != pass2.colorBufferMaxIndex)
                return false;
                
            // 检查每个颜色附件
            for (int i = 0; i <= pass1.colorBufferMaxIndex; ++i)
            {
                // 句柄必须相同
                if (pass1.colorBuffers[i].handle.index != pass2.colorBuffers[i].handle.index)
                    return false;
                    
                // 验证纹理描述符是否匹配
                if (pass1.colorBuffers[i].IsValid() && pass2.colorBuffers[i].IsValid())
                {
                    var desc1 = m_Resources.GetTextureDescriptor(pass1.colorBuffers[i].handle);
                    var desc2 = m_Resources.GetTextureDescriptor(pass2.colorBuffers[i].handle);
                    
                    if (desc1.width != desc2.width || desc1.height != desc2.height || 
                        desc1.colorFormat != desc2.colorFormat)
                        return false;
                }
            }
            
            // 检查深度附件
            bool pass1HasDepth = pass1.depthBuffer.IsValid();
            bool pass2HasDepth = pass2.depthBuffer.IsValid();
            
            if (pass1HasDepth != pass2HasDepth)
                return false;
                
            if (pass1HasDepth && pass2HasDepth)
            {
                // 句柄必须相同
                if (pass1.depthBuffer.handle.index != pass2.depthBuffer.handle.index)
                    return false;
                    
                // 验证纹理描述符是否匹配
                var desc1 = m_Resources.GetTextureDescriptor(pass1.depthBuffer.handle);
                var desc2 = m_Resources.GetTextureDescriptor(pass2.depthBuffer.handle);
                
                if (desc1.width != desc2.width || desc1.height != desc2.height || 
                    desc1.depthBufferBits != desc2.depthBufferBits)
                    return false;
            }
            
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void FinalizeMergeGroup(int startIndex, int endIndex)
        {
            // 只有包含2个或更多Pass的组才需要合并
            if (endIndex > startIndex)
            {
                // 跳过被裁剪的Pass来找到实际的开始和结束索引
                int actualStart = startIndex;
                int actualEnd = endIndex;
                
                // 找到第一个未被裁剪的Pass
                while (actualStart <= endIndex && m_PassCompileInfos[actualStart].culled)
                    actualStart++;
                    
                // 找到最后一个未被裁剪的Pass
                while (actualEnd >= actualStart && m_PassCompileInfos[actualEnd].culled)
                    actualEnd--;
                
                // 如果仍然有多个有效Pass，则创建合并组
                if (actualEnd > actualStart)
                {
                    int groupIndex = m_PassMergeGroups.Count;
                    m_PassMergeGroups.Add(new RGPassMergeGroup(actualStart, actualEnd));
                    
                    // 标记组内的Pass
                    for (int i = actualStart; i <= actualEnd; ++i)
                    {
                        if (!m_PassCompileInfos[i].culled && m_PassCompileInfos[i].pass.passType == EPassType.Raster)
                        {
                            ref var passInfo = ref m_PassCompileInfos[i];
                            passInfo.mergeGroupIndex = groupIndex;
                            passInfo.isGroupLeader = (i == actualStart); // 第一个Pass是组长
                        }
                    }
                }
            }
        }
    }
}
