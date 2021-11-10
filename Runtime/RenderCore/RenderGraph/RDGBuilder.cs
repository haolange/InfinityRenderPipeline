using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Rendering.Core;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    public struct FRDGContext
    {
        public FRenderWorld world;
        public CommandBuffer cmdBuffer;
        public FRDGObjectPool objectPool;
        public FResourcePool resourcePool;
        public ScriptableRenderContext renderContext;
    }

    internal struct FRDGPassCompileInfo
    {
        public IRDGPass pass;
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


        public void Reset(IRDGPass pass)
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

    internal struct FResourceCompileInfo
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

    public class FRDGBuilder 
    {
        public string name;
        bool m_ExecuteExceptionIsRaised;
        FRDGResourceFactory m_Resources;
        FRDGResourceScope<FRDGBufferRef> m_BufferScope;
        FRDGResourceScope<FRDGTextureRef> m_TextureScope;

        Stack<int> m_CullingStack = new Stack<int>();
        List<IRDGPass> m_PassList = new List<IRDGPass>(64);
        FRDGObjectPool m_ObjectPool = new FRDGObjectPool();
        DynamicArray<FRDGPassCompileInfo> m_PassCompileInfos;
        DynamicArray<FResourceCompileInfo>[] m_ResourcesCompileInfos;

        public FRDGBuilder(string name)
        {
            this.name = name;
            this.m_Resources = new FRDGResourceFactory();
            this.m_BufferScope = new FRDGResourceScope<FRDGBufferRef>();
            this.m_TextureScope = new FRDGResourceScope<FRDGTextureRef>();
            this.m_PassCompileInfos = new DynamicArray<FRDGPassCompileInfo>();
            this.m_ResourcesCompileInfos = new DynamicArray<FResourceCompileInfo>[2];

            for (int i = 0; i < 2; ++i)
            {
                this.m_ResourcesCompileInfos[i] = new DynamicArray<FResourceCompileInfo>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef ImportBuffer(ComputeBuffer buffer)
        {
            return m_Resources.ImportBuffer(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef CreateBuffer(in FBufferDescription description)
        {
            return m_Resources.CreateBuffer(description);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef CreateBuffer(in FRDGBufferRef bufferRef)
        {
            return m_Resources.CreateBuffer(m_Resources.GetBufferDescription(bufferRef.handle));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef ScopeBuffer(in int handle)
        {
            return m_BufferScope.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ScopeBuffer(int handle, in FRDGBufferRef bufferRef)
        {
            m_BufferScope.Set(handle, bufferRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef ScopeBuffer(in int handle, in FBufferDescription description)
        {
            FRDGBufferRef bufferRef = CreateBuffer(description);
            m_BufferScope.Set(handle, bufferRef);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FBufferDescription GetBufferDescription(in FRDGBufferRef bufferRef)
        {
            return m_Resources.GetBufferDescription(bufferRef.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef ImportTexture(RTHandle texture, in int shaderProperty = 0)
        {
            return m_Resources.ImportTexture(texture, shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef CreateTexture(in FRDGTextureRef textureRef, int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(m_Resources.GetTextureDescription(textureRef.handle), shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef CreateTexture(in FTextureDescription description, int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(description, shaderProperty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef ScopeTexture(in int handle)
        {
            return m_TextureScope.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ScopeTexture(int handle, in FRDGTextureRef textureRef)
        {
            m_TextureScope.Set(handle, textureRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef ScopeTexture(in int handle, in FTextureDescription description)
        {
            FRDGTextureRef textureRef = CreateTexture(description, handle);
            m_TextureScope.Set(handle, textureRef);
            return textureRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FTextureDescription GetTextureDescription(in FRDGTextureRef textureRef)
        {
            return m_Resources.GetTextureDescription(textureRef.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGPassRef AddPass<T>(string passName, ProfilingSampler profilerSampler) where T : struct
        {
            var renderPass = m_ObjectPool.Get<FRDGPass<T>>();
            renderPass.Clear();
            renderPass.name = passName;
            renderPass.index = m_PassList.Count;
            renderPass.customSampler = profilerSampler;
            m_PassList.Add(renderPass);
            return new FRDGPassRef(renderPass, m_Resources);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(in ScriptableRenderContext renderContext, FRenderWorld world, CommandBuffer cmdBuffer, FResourcePool resourcePool)
        {
            FRDGContext graphContext;
            graphContext.world = world;
            graphContext.cmdBuffer = cmdBuffer;
            graphContext.objectPool = m_ObjectPool;
            graphContext.resourcePool = resourcePool;
            graphContext.renderContext = renderContext;
            m_ExecuteExceptionIsRaised = false;

            #region ExecuteRenderPass
            try
            {
                m_Resources.BeginRender();
                CompilePass();
                ExecutePass(ref graphContext);
            } catch (Exception exception) {
                Debug.LogError("RenderGraph Execute error");
                if (!m_ExecuteExceptionIsRaised) { Debug.LogException(exception); }
                m_ExecuteExceptionIsRaised = true;
            } finally {
                ClearPass();
                m_Resources.EndRender();
            }
            #endregion
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
            m_BufferScope.Clear();
            m_TextureScope.Clear();

            for (int i = 0; i < 2; ++i)
            {
                m_ResourcesCompileInfos[i].Clear();
            }

            m_PassCompileInfos.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitResourceInfoData(DynamicArray<FResourceCompileInfo> resourceInfos, int count)
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
            InitResourceInfoData(m_ResourcesCompileInfos[(int)ERDGResourceType.Buffer], m_Resources.GetBufferCount());
            InitResourceInfoData(m_ResourcesCompileInfos[(int)ERDGResourceType.Texture], m_Resources.GetTextureCount());

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
                ref FRDGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];

                for (int type = 0; type < 2; ++type)
                {
                    var resourceRead = passInfo.pass.resourceReadLists[type];
                    foreach (var resource in resourceRead)
                    {
                        ref FResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource];
                        info.consumers.Add(passIndex);
                        info.refCount++;
                    }

                    var resourceWrite = passInfo.pass.resourceWriteLists[type];
                    foreach (var resource in resourceWrite)
                    {
                        ref FResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resource];
                        info.producers.Add(passIndex);
                        passInfo.refCount++;

                        // Writing to an imported texture is considered as a side effect because we don't know what users will do with it outside of render graph.
                        if (m_Resources.IsResourceImported(resource))
                            passInfo.hasSideEffect = true;
                    }

                    foreach (int resourceIndex in passInfo.pass.temporalResourceList[type])
                    {
                        ref FResourceCompileInfo info = ref m_ResourcesCompileInfos[type][resourceIndex];
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
                DynamicArray<FResourceCompileInfo> resourceUsageList = m_ResourcesCompileInfos[type];

                // Gather resources that are never read.
                m_CullingStack.Clear();
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
                                ref FResourceCompileInfo resourceInfo = ref resourceUsageList[resourceIndex];
                                resourceInfo.refCount--;
                                // If a resource is not used anymore, add it to the stack to be processed in subsequent iteration.
                                if (resourceInfo.refCount == 0)
                                    m_CullingStack.Push(resourceIndex);
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdatePassSynchronization(ref FRDGPassCompileInfo currentPassInfo, ref FRDGPassCompileInfo producerPassInfo, int currentPassIndex, int lastProducer, ref int intLastSyncIndex)
        {
            // Current pass needs to wait for pass index lastProducer
            currentPassInfo.syncToPassIndex = lastProducer;
            // Update latest pass waiting for the other pipe.
            intLastSyncIndex = lastProducer;

            // Producer will need a graphics fence that this pass will wait on.
            producerPassInfo.needGraphicsFence = true;
            // We update the producer pass with the index of the smallest pass waiting for it.
            // This will be used to "lock" resource from being reused until the pipe has been synchronized.
            if (producerPassInfo.syncFromPassIndex == -1)
                producerPassInfo.syncFromPassIndex = currentPassIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateResourceSynchronization(ref int lastGraphicsPipeSync, ref int lastComputePipeSync, int currentPassIndex, in FResourceCompileInfo resourceInfo)
        {
            int lastProducer = GetLatestProducerIndex(currentPassIndex, resourceInfo);
            if (lastProducer != -1)
            {
                ref FRDGPassCompileInfo currentPassInfo = ref m_PassCompileInfos[currentPassIndex];

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
                    } else {
                        if (lastProducer > lastComputePipeSync)
                        {
                            UpdatePassSynchronization(ref currentPassInfo, ref m_PassCompileInfos[lastProducer], currentPassIndex, lastProducer, ref lastComputePipeSync);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLatestProducerIndex(int passIndex, in FResourceCompileInfo resourceInfo)
        {
            // We want to know the highest pass index below the current pass that writes to the resource.
            int result = -1;
            foreach (var producer in resourceInfo.producers)
            {
                // producers are by construction in increasing order.
                if (producer < passIndex)
                    result = producer;
                else
                    return result;
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLatestValidReadIndex(in FResourceCompileInfo resourceInfo)
        {
            if (resourceInfo.consumers.Count == 0)
                return -1;

            var consumers = resourceInfo.consumers;
            for (int i = consumers.Count - 1; i >= 0; --i)
            {
                if (!m_PassCompileInfos[consumers[i]].culled)
                    return consumers[i];
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetFirstValidWriteIndex(in FResourceCompileInfo resourceInfo)
        {
            if (resourceInfo.producers.Count == 0)
                return -1;

            var producers = resourceInfo.producers;
            for (int i = 0; i < producers.Count; ++i)
            {
                if (!m_PassCompileInfos[producers[i]].culled)
                    return producers[i];
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetLatestValidWriteIndex(in FResourceCompileInfo resourceInfo)
        {
            if (resourceInfo.producers.Count == 0)
                return -1;

            var producers = resourceInfo.producers;
            for (int i = producers.Count - 1; i >= 0; --i)
            {
                if (!m_PassCompileInfos[producers[i]].culled)
                    return producers[i];
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
                ref FRDGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];

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
                // Now push resources to the release list of the pass that reads it last.
                for (int i = 0; i < resourceInfos.size; ++i)
                {
                    FResourceCompileInfo resourceInfo = resourceInfos[i];

                    // Resource creation
                    int firstWriteIndex = GetFirstValidWriteIndex(resourceInfo);
                    // Index -1 can happen for imported resources (for example an imported dummy black texture will never be written to but does not need creation anyway)
                    if (firstWriteIndex != -1)
                        m_PassCompileInfos[firstWriteIndex].resourceCreateList[type].Add(i);

                    // Texture release
                    // Sometimes, a texture can be written by a pass after the last pass that reads it.
                    // In this case, we need to extend its lifetime to this pass otherwise the pass would get an invalid texture.
                    int lastReadPassIndex = Math.Max(GetLatestValidReadIndex(resourceInfo), GetLatestValidWriteIndex(resourceInfo));

                    if (lastReadPassIndex != -1)
                    {
                        // In case of async passes, we need to extend lifetime of resource to the first pass on the graphics pipeline that wait for async passes to be over.
                        // Otherwise, if we freed the resource right away during an async pass, another non async pass could reuse the resource even though the async pipe is not done.
                        if (m_PassCompileInfos[lastReadPassIndex].enableAsyncCompute)
                        {
                            int currentPassIndex = lastReadPassIndex;
                            int firstWaitingPassIndex = m_PassCompileInfos[currentPassIndex].syncFromPassIndex;
                            // Find the first async pass that is synchronized by the graphics pipeline (ie: passInfo.syncFromPassIndex != -1)
                            while (firstWaitingPassIndex == -1 && currentPassIndex < m_PassCompileInfos.size)
                            {
                                currentPassIndex++;
                                if (m_PassCompileInfos[currentPassIndex].enableAsyncCompute) {
                                    firstWaitingPassIndex = m_PassCompileInfos[currentPassIndex].syncFromPassIndex;
                                }
                            }

                            // Finally add the release command to the pass before the first pass that waits for the compute pipe.
                            ref FRDGPassCompileInfo passInfo = ref m_PassCompileInfos[Math.Max(0, firstWaitingPassIndex - 1)];
                            passInfo.resourceReleaseList[type].Add(i);

                            // Fail safe in case render graph is badly formed.
                            if (currentPassIndex == m_PassCompileInfos.size) {
                                IRDGPass invalidPass = m_PassList[lastReadPassIndex];
                                throw new InvalidOperationException($"Asynchronous pass {invalidPass.name} was never synchronized on the graphics pipeline.");
                            }
                        } else {
                            ref FRDGPassCompileInfo passInfo = ref m_PassCompileInfos[lastReadPassIndex];
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
            UpdateResource();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetRenderTarget(ref FRDGContext graphContext, in FRDGPassCompileInfo passCompileInfo)
        {
            var pass = passCompileInfo.pass;
            if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
            {
                if (pass.colorBufferMaxIndex > 0)
                {
                    var mrtArray = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(pass.colorBufferMaxIndex + 1);

                    for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                    {
                        if (!pass.colorBuffers[i].IsValid())
                            throw new InvalidOperationException("MRT setup is invalid. Some indices are not used.");

                        mrtArray[i] = m_Resources.GetTexture(pass.colorBuffers[i]);
                    }

                    if (pass.depthBuffer.IsValid()) {
                        CoreUtils.SetRenderTarget(graphContext.cmdBuffer, mrtArray, m_Resources.GetTexture(pass.depthBuffer));
                    } else {
                        throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                    }
                } else {
                    if (pass.depthBuffer.IsValid())
                    {
                        if (pass.colorBufferMaxIndex > -1) {
                            CoreUtils.SetRenderTarget(graphContext.cmdBuffer, m_Resources.GetTexture(pass.colorBuffers[0]), m_Resources.GetTexture(pass.depthBuffer));
                        } else {
                            CoreUtils.SetRenderTarget(graphContext.cmdBuffer, m_Resources.GetTexture(pass.depthBuffer));
                        }
                    } else {
                        CoreUtils.SetRenderTarget(graphContext.cmdBuffer, m_Resources.GetTexture(pass.colorBuffers[0]));
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PrePassExecute(ref FRDGContext graphContext, in FRDGPassCompileInfo passCompileInfo)
        {
            // TODO RENDERGRAPH merge clear and setup here if possible
            IRDGPass pass = passCompileInfo.pass;

            // TODO RENDERGRAPH remove this when we do away with auto global texture setup
            // (can't put it in the profiling scope otherwise it might be executed on compute queue which is not possible for global sets)
            m_Resources.SetGlobalTextures(ref graphContext, pass.resourceReadLists[(int)ERDGResourceType.Texture]);

            foreach (var bufferHandle in passCompileInfo.resourceCreateList[(int)ERDGResourceType.Buffer])
            {
                m_Resources.CreateRealBuffer(bufferHandle);
            }

            foreach (var textureHandle in passCompileInfo.resourceCreateList[(int)ERDGResourceType.Texture])
            {
                m_Resources.CreateRealTexture(ref graphContext, textureHandle);
            }

            SetRenderTarget(ref graphContext, passCompileInfo);

            // Flush first the current command buffer on the render context.
            graphContext.renderContext.ExecuteCommandBuffer(graphContext.cmdBuffer);
            graphContext.cmdBuffer.Clear();

            if (pass.enableAsyncCompute)
            {
                CommandBuffer asyncCmdBuffer = CommandBufferPool.Get(pass.name);
                asyncCmdBuffer.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                graphContext.cmdBuffer = asyncCmdBuffer;
            }

            // Synchronize with graphics or compute pipe if needed.
            if (passCompileInfo.syncToPassIndex != -1)
            {
                graphContext.cmdBuffer.WaitOnAsyncGraphicsFence(m_PassCompileInfos[passCompileInfo.syncToPassIndex].fence);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PostPassExecute(CommandBuffer cmdBuffer, ref FRDGContext graphContext, ref FRDGPassCompileInfo passCompileInfo)
        {
            IRDGPass pass = passCompileInfo.pass;

            if (passCompileInfo.needGraphicsFence)
                passCompileInfo.fence = graphContext.cmdBuffer.CreateAsyncGraphicsFence();

            if (pass.enableAsyncCompute)
            {
                // The command buffer has been filled. We can kick the async task.
                graphContext.renderContext.ExecuteCommandBufferAsync(graphContext.cmdBuffer, ComputeQueueType.Background);
                CommandBufferPool.Release(graphContext.cmdBuffer);
                graphContext.cmdBuffer = cmdBuffer; // Restore the main command buffer.
            }

            m_ObjectPool.ReleaseAllTempAlloc();

            foreach (var buffer in passCompileInfo.resourceReleaseList[(int)ERDGResourceType.Buffer])
                m_Resources.ReleaseRealBuffer(buffer);

            foreach (var texture in passCompileInfo.resourceReleaseList[(int)ERDGResourceType.Texture])
                m_Resources.ReleaseRealTexture(texture);

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ExecutePass(ref FRDGContext graphContext)
        {
            CommandBuffer graphicsCmdBuffer = graphContext.cmdBuffer;

            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref var passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled) { continue; }

                if (!passInfo.pass.hasExecuteFunc)
                {
                    throw new InvalidOperationException(string.Format("RenderPass {0} was not provided with an execute function.", passInfo.pass.name));
                }

                try
                {
                    using (new ProfilingScope(graphContext.cmdBuffer, passInfo.pass.customSampler))
                    {
                        PrePassExecute(ref graphContext, passInfo);
                        passInfo.pass.Execute(graphContext);
                        PostPassExecute(graphicsCmdBuffer, ref graphContext, ref passInfo);
                    }
                } catch (Exception e) {
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
            m_BufferScope.Dispose();
            m_TextureScope.Dispose();
        }
    }
}
