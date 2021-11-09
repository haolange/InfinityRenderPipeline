using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Rendering.Core;
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
        FRDGResourceFactory m_Resources;
        FRDGResourceScope<FRDGBufferRef> m_BufferScope;
        FRDGResourceScope<FRDGTextureRef> m_TextureScope;
        List<IRDGPass> m_RenderPasses = new List<IRDGPass>(64);

        bool m_ExecutionExceptionWasRaised;
        FRDGContext m_GraphContext = new FRDGContext();
        FRDGObjectPool m_ObjectPool = new FRDGObjectPool();

        // Compiled Render Graph info.
        Stack<int> m_CullingStack = new Stack<int>();
        DynamicArray<FRDGPassCompileInfo> m_PassCompileInfos = new DynamicArray<FRDGPassCompileInfo>();
        DynamicArray<FResourceCompileInfo>[] m_ResourcesCompileInfos = new DynamicArray<FResourceCompileInfo>[2];

        public FRDGBuilder(string name)
        {
            this.name = name;
            this.m_Resources = new FRDGResourceFactory();
            this.m_BufferScope = new FRDGResourceScope<FRDGBufferRef>();
            this.m_TextureScope = new FRDGResourceScope<FRDGTextureRef>();

            for (int i = 0; i < 2; ++i)
            {
                this.m_ResourcesCompileInfos[i] = new DynamicArray<FResourceCompileInfo>();
            }
        }

        public void Dispose()
        {
            m_Resources.Dispose();
            m_BufferScope.Dispose();
            m_TextureScope.Dispose();
        }

        public FRDGBufferRef ImportBuffer(ComputeBuffer buffer)
        {
            return m_Resources.ImportBuffer(buffer);
        }

        public FRDGBufferRef CreateBuffer(in FBufferDescription bufferDesc)
        {
            return m_Resources.CreateBuffer(bufferDesc);
        }

        public FRDGBufferRef CreateBuffer(in FRDGBufferRef bufferRef)
        {
            return m_Resources.CreateBuffer(m_Resources.GetBufferResourceDesc(bufferRef.handle));
        }

        public FRDGBufferRef ScopeBuffer(in int handle)
        {
            return m_BufferScope.Get(handle);
        }

        public void ScopeBuffer(int handle, in FRDGBufferRef bufferRef)
        {
            m_BufferScope.Set(handle, bufferRef);
        }

        public FRDGBufferRef ScopeBuffer(in int handle, in FBufferDescription bufferDesc)
        {
            FRDGBufferRef bufferRef = CreateBuffer(bufferDesc);
            m_BufferScope.Set(handle, bufferRef);
            return bufferRef;
        }

        public FBufferDescription GetBufferDesc(in FRDGBufferRef bufferRef)
        {
            return m_Resources.GetBufferResourceDesc(bufferRef.handle);
        }

        public FRDGTextureRef ImportTexture(RTHandle texture, in int shaderProperty = 0)
        {
            return m_Resources.ImportTexture(texture, shaderProperty);
        }

        public FRDGTextureRef CreateTexture(in FRDGTextureRef textureRef, int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(m_Resources.GetTextureResourceDesc(textureRef.handle), shaderProperty);
        }

        public FRDGTextureRef CreateTexture(in FTextureDescription textureDesc, int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(textureDesc, shaderProperty);
        }

        public FRDGTextureRef ScopeTexture(in int handle)
        {
            return m_TextureScope.Get(handle);
        }

        public void ScopeTexture(int handle, in FRDGTextureRef textureRef)
        {
            m_TextureScope.Set(handle, textureRef);
        }

        public FRDGTextureRef ScopeTexture(in int handle, in FTextureDescription textureDesc)
        {
            FRDGTextureRef textureRef = CreateTexture(textureDesc, handle);
            m_TextureScope.Set(handle, textureRef);
            return textureRef;
        }

        public FTextureDescription GetTextureDesc(in FRDGTextureRef textureRef)
        {
            return m_Resources.GetTextureResourceDesc(textureRef.handle);
        }

        public FRDGPassRef AddPass<T>(string passName, ProfilingSampler profilerSampler) where T : struct
        {
            var renderPass = m_ObjectPool.Get<FRDGPass<T>>();
            renderPass.Clear();
            renderPass.name = passName;
            renderPass.index = m_RenderPasses.Count;
            renderPass.customSampler = profilerSampler;
            m_RenderPasses.Add(renderPass);
            return new FRDGPassRef(renderPass, m_Resources);
        }

        public void Execute(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, FRenderWorld world, FResourcePool resourcePool)
        {
            m_ExecutionExceptionWasRaised = false;

            #region ExecuteRenderPass
            try
            {
                m_Resources.BeginRender();
                CompileRenderPass();
                ExecuteRenderPass(renderContext, world, resourcePool, cmdBuffer);
            } catch (Exception exception) {
                Debug.LogError("RenderGraph Execute error");
                if (!m_ExecutionExceptionWasRaised)
                    Debug.LogException(exception);
                m_ExecutionExceptionWasRaised = true;
            } finally {
                ClearCompiledPass();
                m_Resources.EndRender();
            }
            #endregion
        }

        internal DynamicArray<FRDGPassCompileInfo> GetCompiledPassInfos() 
        { 
            return m_PassCompileInfos; 
        }

        internal void ClearCompiledPass()
        {
            ClearRenderPasses();
            m_Resources.Clear();

            for (int i = 0; i < 2; ++i)
                m_ResourcesCompileInfos[i].Clear();

            m_PassCompileInfos.Clear();
        }

        void InitResourceInfosData(DynamicArray<FResourceCompileInfo> resourceInfos, int count)
        {
            resourceInfos.Resize(count);
            for (int i = 0; i < resourceInfos.size; ++i)
                resourceInfos[i].Reset();
        }

        void InitializeCompileData()
        {
            InitResourceInfosData(m_ResourcesCompileInfos[(int)ERDGResourceType.Buffer], m_Resources.GetBufferResourceCount());
            InitResourceInfosData(m_ResourcesCompileInfos[(int)ERDGResourceType.Texture], m_Resources.GetTextureResourceCount());

            m_PassCompileInfos.Resize(m_RenderPasses.Count);
            for (int i = 0; i < m_PassCompileInfos.size; ++i)
                m_PassCompileInfos[i].Reset(m_RenderPasses[i]);
        }

        void CountPassReferences()
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

        void UpdateResource()
        {
            int lastComputePipeSync = -1;
            int lastGraphicsPipeSync = -1;

            // First go through all passes.
            // - Update the last pass read index for each resource.
            // - Add texture to creation list for passes that first write to a texture.
            // - Update synchronization points for all resources between compute and graphics pipes.
            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref FRDGPassCompileInfo passInfo = ref m_PassCompileInfos[passIndex];

                if (passInfo.culled)
                    continue;

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
                                if (m_PassCompileInfos[currentPassIndex].enableAsyncCompute)
                                    firstWaitingPassIndex = m_PassCompileInfos[currentPassIndex].syncFromPassIndex;
                            }

                            // Finally add the release command to the pass before the first pass that waits for the compute pipe.
                            ref FRDGPassCompileInfo passInfo = ref m_PassCompileInfos[Math.Max(0, firstWaitingPassIndex - 1)];
                            passInfo.resourceReleaseList[type].Add(i);

                            // Fail safe in case render graph is badly formed.
                            if (currentPassIndex == m_PassCompileInfos.size)
                            {
                                IRDGPass invalidPass = m_RenderPasses[lastReadPassIndex];
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

        internal void CompileRenderPass()
        {
            InitializeCompileData();
            CountPassReferences();
            CullingUnusedPass();
            UpdateResource();
        }

        void ExecuteRenderPass(ScriptableRenderContext renderContext, FRenderWorld renderWorld, FResourcePool resourcePool, CommandBuffer cmdBuffer)
        {
            m_GraphContext.world = renderWorld;
            m_GraphContext.cmdBuffer = cmdBuffer;
            m_GraphContext.objectPool = m_ObjectPool;
            m_GraphContext.resourcePool = resourcePool;
            m_GraphContext.renderContext = renderContext;

            for (int passIndex = 0; passIndex < m_PassCompileInfos.size; ++passIndex)
            {
                ref var passInfo = ref m_PassCompileInfos[passIndex];
                if (passInfo.culled)
                    continue;

                if (!passInfo.pass.hasExecuteFunc)
                {
                    throw new InvalidOperationException(string.Format("RenderPass {0} was not provided with an execute function.", passInfo.pass.name));
                }

                try
                {
                    using (new ProfilingScope(m_GraphContext.cmdBuffer, passInfo.pass.customSampler))
                    {
                        PreRenderPassExecute(ref m_GraphContext, passInfo);
                        passInfo.pass.Execute(m_GraphContext);
                        PostRenderPassExecute(cmdBuffer, ref m_GraphContext, ref passInfo);
                    }
                } catch (Exception e) {
                    m_ExecutionExceptionWasRaised = true;
                    Debug.LogError($"RenderGraph Execute error at pass {passInfo.pass.name} ({passIndex})");
                    Debug.LogException(e);
                    throw;
                }
            }
        }

        void PreRenderPassSetRenderTargets(ref FRDGContext graphContext, in FRDGPassCompileInfo passCompileInfo)
        {
            var pass = passCompileInfo.pass;
            if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
            {
                var mrtArray = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(pass.colorBufferMaxIndex + 1);
                var colorBuffers = pass.colorBuffers;

                if (pass.colorBufferMaxIndex > 0)
                {
                    for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                    {
                        if (!colorBuffers[i].IsValid())
                            throw new InvalidOperationException("MRT setup is invalid. Some indices are not used.");

                        mrtArray[i] = m_Resources.GetTexture(colorBuffers[i]);
                    }

                    if (pass.depthBuffer.IsValid())
                    {
                        CoreUtils.SetRenderTarget(graphContext.cmdBuffer, mrtArray, m_Resources.GetTexture(pass.depthBuffer));
                    } else {
                        throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                    }
                } else {
                    if (pass.depthBuffer.IsValid())
                    {
                        if (pass.colorBufferMaxIndex > -1) 
                        {
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

        void PreRenderPassExecute(ref FRDGContext graphContext, in FRDGPassCompileInfo passCompileInfo)
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

            PreRenderPassSetRenderTargets(ref graphContext, passCompileInfo);

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

        void PostRenderPassExecute(CommandBuffer cmdBuffer, ref FRDGContext graphContext, ref FRDGPassCompileInfo passCompileInfo)
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

        void ClearRenderPasses()
        {
            foreach (var pass in m_RenderPasses)
            {
                pass.Release(m_ObjectPool);
            }

            m_BufferScope.Clear();
            m_TextureScope.Clear();
            m_RenderPasses.Clear();
        }
    }
}
