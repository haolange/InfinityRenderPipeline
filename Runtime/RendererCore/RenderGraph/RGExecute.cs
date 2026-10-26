using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.RenderGraph
{
    public partial class RGBuilder
    {
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

        static RenderBufferLoadAction FallbackColorLoad(RenderBufferLoadAction action)
            => action == RenderBufferLoadAction.Clear ? RenderBufferLoadAction.Load : action;

        RenderBufferStoreAction FallbackColorStore(in RGPassCompileInfo info, int colorIndex)
        {
            if (info.pass != null && info.pass.index >= 0 && info.pass.index < m_PassCompileInfos.size
                && m_PassCompileInfos[info.pass.index].pass == info.pass)
                return CompiledColorStore(info.pass.index, colorIndex);
            return info.pass.colorBufferActions[colorIndex].storeAction;
        }

        RenderBufferStoreAction FallbackDepthStore(in RGPassCompileInfo info)
        {
            if (info.pass != null && info.pass.index >= 0 && info.pass.index < m_PassCompileInfos.size
                && m_PassCompileInfos[info.pass.index].pass == info.pass)
                return CompiledDepthStore(info.pass.index);
            return info.pass.depthBufferAction.storeAction;
        }

        void ClearFallbackColors(CommandBuffer commands, IRGPass pass)
        {
            for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
            {
                if (!pass.colorBuffers[i].IsValid() || pass.colorBufferActions[i].loadAction != RenderBufferLoadAction.Clear)
                    continue;
                commands.SetRenderTarget(m_Resources.GetTexture(pass.colorBuffers[i]), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                commands.ClearRenderTarget(false, true, GetTextureDescriptor(pass.colorBuffers[i]).clearColor);
            }
        }

        void SetRenderTarget(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            IRGPass pass = passCompileInfo.pass;
            ClearFallbackColors(graphContext.cmdBuffer, pass);
            var depthLoad = pass.depthBufferAction.loadAction == RenderBufferLoadAction.Clear
                ? RenderBufferLoadAction.DontCare : pass.depthBufferAction.loadAction;
            RenderBufferStoreAction depthStore = FallbackDepthStore(passCompileInfo);
            if (!pass.depthBuffer.IsValid() && pass.colorBufferMaxIndex == -1) return;

            if (pass.colorBufferMaxIndex > 0)
            {
                if (!pass.depthBuffer.IsValid())
                    throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                RenderTargetIdentifier[] colors = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(pass.colorBufferMaxIndex + 1);
                RenderBufferLoadAction[] loads = graphContext.objectPool.GetTempArray<RenderBufferLoadAction>(pass.colorBufferMaxIndex + 1);
                RenderBufferStoreAction[] stores = graphContext.objectPool.GetTempArray<RenderBufferStoreAction>(pass.colorBufferMaxIndex + 1);
                for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                {
                    if (!pass.colorBuffers[i].IsValid())
                        throw new InvalidOperationException("MRT setup is invalid. Some indices are not used.");
                    colors[i] = m_Resources.GetTexture(pass.colorBuffers[i]);
                    loads[i] = FallbackColorLoad(pass.colorBufferActions[i].loadAction);
                    stores[i] = FallbackColorStore(passCompileInfo, i);
                }
                var binding = new RenderTargetBinding(colors, loads, stores, m_Resources.GetTexture(pass.depthBuffer), depthLoad, depthStore)
                {
                    flags = GetFallbackDepthFlags(pass.depthBufferAccess)
                };
                graphContext.cmdBuffer.SetRenderTarget(binding);
            }
            else if (pass.depthBuffer.IsValid())
            {
                var colors = graphContext.objectPool.GetTempArray<RenderTargetIdentifier>(1);
                var loads = graphContext.objectPool.GetTempArray<RenderBufferLoadAction>(1);
                var stores = graphContext.objectPool.GetTempArray<RenderBufferStoreAction>(1);
                if (pass.colorBufferMaxIndex > -1)
                {
                    colors[0] = m_Resources.GetTexture(pass.colorBuffers[0]);
                    loads[0] = FallbackColorLoad(pass.colorBufferActions[0].loadAction);
                    stores[0] = FallbackColorStore(passCompileInfo, 0);
                    var binding = new RenderTargetBinding(colors, loads, stores, m_Resources.GetTexture(pass.depthBuffer), depthLoad, depthStore)
                    {
                        flags = GetFallbackDepthFlags(pass.depthBufferAccess)
                    };
                    graphContext.cmdBuffer.SetRenderTarget(binding);
                }
                else
                {
                    colors[0] = m_Resources.GetTexture(pass.depthBuffer);
                    loads[0] = depthLoad;
                    stores[0] = depthStore;
                    var binding = new RenderTargetBinding(colors, loads, stores, m_Resources.GetTexture(pass.depthBuffer), depthLoad, depthStore)
                    {
                        flags = GetFallbackDepthFlags(pass.depthBufferAccess)
                    };
                    graphContext.cmdBuffer.SetRenderTarget(binding);
                }
            }
            else
            {
                graphContext.cmdBuffer.SetRenderTarget(
                    m_Resources.GetTexture(pass.colorBuffers[0]),
                    FallbackColorLoad(pass.colorBufferActions[0].loadAction),
                    FallbackColorStore(passCompileInfo, 0));
            }

            if (pass.depthBuffer.IsValid() && pass.depthBufferAction.loadAction == RenderBufferLoadAction.Clear)
                graphContext.cmdBuffer.ClearRenderTarget(true, false, Color.clear, GraphicsUtility.ClearDepthFar);
        }

        static RenderTargetFlags GetFallbackDepthFlags(EDepthAccess access)
        {
            bool readOnly = (access & EDepthAccess.ReadOnly) != 0;
            bool write = (access & EDepthAccess.Write) != 0;
            return readOnly && !write ? RenderTargetFlags.ReadOnlyDepthStencil : RenderTargetFlags.None;
        }

        void BeginNativeGroup(ref RGContext graphContext, RGNativePassGroup group)
        {
            var attachments = new NativeArray<AttachmentDescriptor>(group.attachments.Count, Allocator.Temp);
            var subpasses = new NativeArray<SubPassDescriptor>(group.subpasses.Count, Allocator.Temp);
            try
            {
                RenderTexture sizeSource = null;
                for (int i = 0; i < group.attachments.Count; ++i)
                {
                    RGGroupAttachment attachment = group.attachments[i];
                    RenderTexture texture = m_Resources.GetTexture(attachment.texture);
                    if (sizeSource == null) sizeSource = texture;
                    var descriptor = new AttachmentDescriptor(attachment.depth ? texture.depthStencilFormat : texture.graphicsFormat)
                    {
                        loadAction = attachment.load,
                        storeAction = StoreOfGroupAttachment(group, i),
                        loadStoreTarget = texture
                    };
                    ApplyAttachmentClearValues(ref descriptor, attachment.depth ? Color.clear : GetTextureDescriptor(attachment.texture).clearColor);
                    attachments[i] = descriptor;
                }
                for (int s = 0; s < group.subpasses.Count; ++s)
                {
                    RGGroupSubpass subpass = group.subpasses[s];
                    var descriptor = new SubPassDescriptor
                    {
                        inputs = AttachmentIndexArray.Emtpy,
                        colorOutputs = subpass.colorCount == 0 ? AttachmentIndexArray.Emtpy : new AttachmentIndexArray(subpass.colorCount)
                    };
                    for (int c = 0; c < subpass.colorCount; ++c) descriptor.colorOutputs[c] = subpass.Color(c);
                    if (subpass.readOnlyDepth) descriptor.flags = SubPassFlags.ReadOnlyDepthStencil;
                    subpasses[s] = descriptor;
                }
                graphContext.cmdBuffer.BeginRenderPass(sizeSource.width, sizeSource.height, group.samples, attachments, group.depthAttachment, subpasses);
            }
            finally
            {
                attachments.Dispose();
                subpasses.Dispose();
            }
        }

        RenderBufferStoreAction StoreOfGroupAttachment(RGNativePassGroup group, int attachmentIndex)
        {
            int lastPass = -1;
            int colorSlot = 0;
            bool depth = group.attachments[attachmentIndex].depth;
            for (int s = 0; s < group.subpasses.Count; ++s)
            {
                RGGroupSubpass subpass = group.subpasses[s];
                IRGPass pass = m_PassCompileInfos[subpass.passIndex].pass;
                if (depth && pass.depthBuffer.IsValid() && SameResource(pass.depthBuffer, group.attachments[attachmentIndex].texture))
                    lastPass = subpass.passIndex;
                for (int c = 0; c < subpass.colorCount; ++c)
                {
                    if (subpass.Color(c) != attachmentIndex) continue;
                    lastPass = subpass.passIndex;
                    colorSlot = c;
                }
            }
            if (lastPass < 0) return group.attachments[attachmentIndex].store;
            return depth ? CompiledDepthStore(lastPass) : CompiledColorStore(lastPass, colorSlot);
        }

        static void ApplyAttachmentClearValues(ref AttachmentDescriptor attachment, Color clearColor)
        {
            attachment.clearColor = clearColor;
            attachment.clearDepth = GraphicsUtility.ClearDepthFar;
            attachment.clearStencil = 0;
        }

        void BindRasterTarget(ref RGContext graphContext, in RGPassCompileInfo info)
        {
            IRGPass pass = info.pass;
            if (pass.enableNativeRenderPass && info.mergeGroup >= 0)
            {
                if (info.mergeFirst) BeginNativeGroup(ref graphContext, m_NativeGroups[info.mergeGroup]);
                else graphContext.cmdBuffer.NextSubPass();
            }
            else if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
                SetRenderTarget(ref graphContext, info);

            graphContext.cmdBuffer.DisableScissorRect();
            RGTextureRef viewportTarget = pass.colorBufferMaxIndex >= 0 ? pass.colorBuffers[0] : pass.depthBuffer;
            if (!viewportTarget.IsValid()) return;
            var descriptor = GetTextureDescriptor(viewportTarget);
            graphContext.cmdBuffer.SetViewport(new Rect(0, 0, descriptor.width, descriptor.height));
        }

        void PrePassExecute(ref RGContext graphContext, in RGPassCompileInfo passCompileInfo)
        {
            if (passCompileInfo.syncToPassIndex != -1)
            {
                ref RGPassCompileInfo producer = ref m_PassCompileInfos[passCompileInfo.syncToPassIndex];
                if (producer.needGraphicsFence)
                    graphContext.cmdBuffer.WaitOnAsyncGraphicsFence(producer.fence);
            }

            foreach (int buffer in passCompileInfo.resourceCreateList[(int)ERGResourceType.Buffer])
                m_Resources.CreateBufferResource(ref graphContext, buffer, passCompileInfo.enableAsyncCompute);
            foreach (int texture in passCompileInfo.resourceCreateList[(int)ERGResourceType.Texture])
                m_Resources.CreateTextureResource(ref graphContext, texture, passCompileInfo.enableAsyncCompute);

            if (passCompileInfo.pass.passType == EPassType.Raster)
                BindRasterTarget(ref graphContext, passCompileInfo);
        }

        void PostPassExecute(ref RGContext graphContext, ref RGPassCompileInfo passCompileInfo)
        {
            if (passCompileInfo.pass.passType == EPassType.Raster && passCompileInfo.pass.enableNativeRenderPass && passCompileInfo.mergeLast)
                graphContext.cmdBuffer.EndRenderPass();
            if (passCompileInfo.needGraphicsFence)
                passCompileInfo.fence = graphContext.cmdBuffer.CreateAsyncGraphicsFence();
            if (passCompileInfo.needResourceFence)
                passCompileInfo.completionFence = graphContext.cmdBuffer.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
        }

        void ReleasePassResources(ref RGPassCompileInfo passCompileInfo)
        {
            m_ObjectPool.ReleaseAllTempAlloc();
            foreach (int buffer in passCompileInfo.resourceReleaseList[(int)ERGResourceType.Buffer])
                m_Resources.ReleaseBufferResource(buffer, GetResourceReuseFence(ERGResourceType.Buffer, buffer));
            foreach (int texture in passCompileInfo.resourceReleaseList[(int)ERGResourceType.Texture])
                m_Resources.ReleaseTextureResource(texture, GetResourceReuseFence(ERGResourceType.Texture, texture));
        }

        static void BeginSamples(List<ProfilingSampler> samplers, CommandBuffer commands)
        {
            if (samplers == null) return;
            for (int i = 0; i < samplers.Count; ++i) samplers[i].Begin(commands);
        }

        static void EndSamples(List<ProfilingSampler> samplers, CommandBuffer commands)
        {
            if (samplers == null) return;
            for (int i = samplers.Count - 1; i >= 0; --i) samplers[i].End(commands);
        }

        void ExecutePass(ref RGContext graphContext)
        {
            CommandBuffer graphicsCommands = graphContext.cmdBuffer;
            bool hasQueuedAsync = false;
            GraphicsFence lastAsyncFence = default;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            try
            {
                int passIndex = 0;
                while (passIndex < m_PassCompileInfos.size)
                {
                    while (passIndex < m_PassCompileInfos.size && m_PassCompileInfos[passIndex].culled) passIndex++;
                    if (passIndex >= m_PassCompileInfos.size) break;
                    int segment = m_PassCompileInfos[passIndex].segment;
                    int segmentEnd = passIndex;
                    while (segmentEnd < m_PassCompileInfos.size)
                    {
                        if (!m_PassCompileInfos[segmentEnd].culled && m_PassCompileInfos[segmentEnd].segment != segment) break;
                        segmentEnd++;
                    }
                    ExecuteSegment(ref graphContext, graphicsCommands, passIndex, segmentEnd, ref hasQueuedAsync, ref lastAsyncFence);
                    passIndex = segmentEnd;
                }
            }
            catch (Exception error)
            {
                m_Resources.SubmissionUncertain();
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
            }
            finally
            {
                graphicsCommands.Clear();
                if (hasQueuedAsync)
                {
                    try
                    {
                        graphicsCommands.WaitOnAsyncGraphicsFence(lastAsyncFence);
                        QueueGraphics(graphContext.renderContext, graphicsCommands);
                    }
                    catch (Exception joinError)
                    {
                        m_UnrecoverableAsyncSubmission = true;
                        if (failure == null) failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(joinError);
                        else Debug.LogException(joinError);
                    }
                    finally { graphicsCommands.Clear(); }
                }
            }
            failure?.Throw();
        }

        void ExecuteSegment(ref RGContext graphContext, CommandBuffer graphicsCommands, int first, int end, ref bool hasQueuedAsync, ref GraphicsFence lastAsyncFence)
        {
            ref RGPassCompileInfo firstInfo = ref m_PassCompileInfos[first];
            bool async = firstInfo.enableAsyncCompute;
            CommandBuffer asyncCommands = null;
            CommandBuffer commands = graphicsCommands;
            var passes = new List<int>(end - first);
            try
            {
                if (async)
                {
                    asyncCommands = CommandBufferPool.Get();
                    asyncCommands.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                    commands = asyncCommands;
                    graphContext.cmdBuffer = asyncCommands;
                    if (!hasQueuedAsync)
                    {
                        GraphicsFence entryFence = graphicsCommands.CreateAsyncGraphicsFence();
                        asyncCommands.WaitOnAsyncGraphicsFence(entryFence);
                    }
                }
                else graphContext.cmdBuffer = graphicsCommands;

                for (int passIndex = first; passIndex < end; ++passIndex)
                {
                    ref RGPassCompileInfo info = ref m_PassCompileInfos[passIndex];
                    if (info.culled) continue;
                    if (!info.pass.hasExecuteAction)
                        throw new InvalidOperationException($"RenderPass {info.pass.name} was not provided with an execute function.");
                    passes.Add(passIndex);
                    BeginSamples(info.beginSamples, graphContext.cmdBuffer);
                    bool passScoped = info.pass.customSampler != null;
                    ProfilingScope passScope = passScoped ? new ProfilingScope(graphContext.cmdBuffer, info.pass.customSampler) : default;
                    try
                    {
                        PrePassExecute(ref graphContext, info);
                        info.pass.Execute(ref graphContext);
                        PostPassExecute(ref graphContext, ref info);
                        RenderFaultValidation.AtPass(ERenderFaultPoint.BeforePassQueue, info.pass.name);
                    }
                    finally
                    {
                        if (passScoped) passScope.Dispose();
                        EndSamples(info.endSamples, graphContext.cmdBuffer);
                    }
                    var useFence = new InfinityTech.Rendering.GPUResource.ResourceReuseFence
                    {
                        hasGraphics = !info.enableAsyncCompute && info.needGraphicsFence,
                        hasCompute = info.enableAsyncCompute && info.needGraphicsFence,
                        graphics = info.fence, compute = info.fence,
                        graphicsCompletion = info.completionFence, computeCompletion = info.completionFence
                    };
                    m_Resources.QueueAttempt(info.pass, info.enableAsyncCompute, useFence);
                }

                if (async)
                {
                    GraphicsFence completion = default;
                    bool haveCompletion = false;
                    for (int i = 0; i < passes.Count; ++i)
                    {
                        if (!m_PassCompileInfos[passes[i]].needGraphicsFence) continue;
                        completion = m_PassCompileInfos[passes[i]].fence;
                        haveCompletion = true;
                    }
                    if (!haveCompletion) completion = asyncCommands.CreateAsyncGraphicsFence();
                    if (!hasQueuedAsync)
                    {
                        QueueGraphics(graphContext.renderContext, graphicsCommands);
                        graphicsCommands.Clear();
                    }
                    try
                    {
                        QueueAsync(graphContext.renderContext, asyncCommands);
                        lastAsyncFence = completion;
                        hasQueuedAsync = true;
                    }
                    catch
                    {
                        FailSegment(passes);
                        DrainAsync(graphContext.renderContext, ref lastAsyncFence, ref hasQueuedAsync);
                        throw;
                    }
                }
                else
                {
                    try { QueueGraphics(graphContext.renderContext, graphicsCommands); }
                    catch
                    {
                        m_QueueSubmissionFailed = true;
                        FailSegment(passes);
                        throw;
                    }
                    graphicsCommands.Clear();
                }

                for (int i = 0; i < passes.Count; ++i)
                    RenderFaultValidation.AtPass(ERenderFaultPoint.AfterPassQueue, m_PassCompileInfos[passes[i]].pass.name);
                for (int i = 0; i < passes.Count; ++i)
                {
                    m_PassCompileInfos[passes[i]].pass.queueObserver?.OnQueued();
                    ReleasePassResources(ref m_PassCompileInfos[passes[i]]);
                }
            }
            finally
            {
                if (asyncCommands != null)
                {
                    asyncCommands.Clear();
                    CommandBufferPool.Release(asyncCommands);
                }
                graphContext.cmdBuffer = graphicsCommands;
            }
        }

        void FailSegment(List<int> passes)
        {
            for (int i = 0; i < passes.Count; ++i)
                m_PassCompileInfos[passes[i]].pass.queueObserver?.OnQueueFailed();
        }

        void DrainAsync(RenderContext context, ref GraphicsFence lastAsyncFence, ref bool hasQueuedAsync)
        {
            var drain = CommandBufferPool.Get();
            try
            {
                drain.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                GraphicsFence drainFence = drain.CreateAsyncGraphicsFence();
                QueueAsync(context, drain);
                lastAsyncFence = drainFence;
                hasQueuedAsync = true;
            }
            catch (Exception drainError)
            {
                m_UnrecoverableAsyncSubmission = true;
                Debug.LogException(drainError);
            }
            finally { drain.Clear(); CommandBufferPool.Release(drain); }
        }
    }
}
