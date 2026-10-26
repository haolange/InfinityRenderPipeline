using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RenderGraph
{
    internal enum EPassType : byte
    {
        Transfer = 0,
        Compute = 1,
        RayTracing = 2,
        Raster = 3
    }

    internal struct RGAttachmentAction
    {
        public RenderBufferLoadAction loadAction;
        public RenderBufferStoreAction storeAction;

        public RGAttachmentAction(in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            this.loadAction = loadAction;
            this.storeAction = storeAction;
        }
    }

    public delegate void RGTransferPassExecuteAction<T>(in T passData, in RGTransferEncoder cmdEncoder) where T : struct;
    public delegate void RGComputePassExecuteAction<T>(in T passData, in RGComputeEncoder cmdEncoder) where T : struct;
    public delegate void RGRayTracingPassExecuteAction<T>(in T passData, in RGRaytracingEncoder cmdEncoder) where T : struct;
    public delegate void RGRasterPassExecuteAction<T>(in T passData, in RGRasterEncoder cmdEncoder) where T : struct;

    internal interface IRGPassQueueObserver
    {
        void OnQueued();
        void OnQueueFailed();
    }

    internal abstract class IRGPass
    {
        public int index;
        public string name;
        public ProfilingSampler customSampler;
        internal IRGPassQueueObserver queueObserver;
        internal bool importedWritesAreSideEffects = true;
        internal bool sideEffect;
        internal bool insertedByCompiler;

        public int refCount { get; protected set; }
        public EPassType passType { get; protected set; }
        public int colorBufferMaxIndex { get; protected set; }
        public bool enablePassCulling { get; protected set; }
        public bool enableAsyncCompute { get; protected set; }
        public bool enableNativeRenderPass { get; protected set; }
        public RGTextureRef depthBuffer { get; protected set; }
        public EDepthAccess depthBufferAccess { get; protected set; }
        public RGAttachmentAction depthBufferAction { get; protected set; }
        public RGTextureRef[] colorBuffers { get; protected set; }
        public RGAttachmentAction[] colorBufferActions { get; protected set; }

        internal virtual bool hasExecuteAction => false;

        public List<RGResourceHandle>[] resourceReadLists;
        public List<RGResourceHandle>[] resourceWriteLists;
        public List<RGResourceHandle>[] temporalResourceList;
        public List<RGResourceHandle> sampledResources;
        public List<RGDrawListRef> usedDrawLists;
        public List<RGRendererListRef> usedRendererLists;

        public IRGPass()
        {
            int resourceTypeCount = (int)ERGResourceType.Max;
            colorBufferMaxIndex = -1;
            enableNativeRenderPass = true;
            colorBuffers = new RGTextureRef[8];
            colorBufferActions = new RGAttachmentAction[8];
            resourceReadLists = new List<RGResourceHandle>[resourceTypeCount];
            resourceWriteLists = new List<RGResourceHandle>[resourceTypeCount];
            temporalResourceList = new List<RGResourceHandle>[resourceTypeCount];
            sampledResources = new List<RGResourceHandle>(4);
            usedDrawLists = new List<RGDrawListRef>(4);
            usedRendererLists = new List<RGRendererListRef>(4);

            for (int i = 0; i < resourceTypeCount; ++i)
            {
                resourceReadLists[i] = new List<RGResourceHandle>();
                resourceWriteLists[i] = new List<RGResourceHandle>();
                temporalResourceList[i] = new List<RGResourceHandle>();
            }
        }

        public abstract void Execute(ref RGContext graphContext);
        public abstract void Release(RGObjectPool objectPool);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResourceRead(in RGResourceHandle handle)
        {
            resourceReadLists[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResourceWrite(in RGResourceHandle handle)
        {
            resourceWriteLists[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTemporalResource(in RGResourceHandle handle)
        {
            temporalResourceList[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColorAttachment(in RGTextureRef resource, in int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            colorBufferActions[index] = new RGAttachmentAction(loadAction, storeAction);
            if (loadAction == RenderBufferLoadAction.Load) AddResourceRead(resource.handle);
            AddResourceWrite(resource.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthStencilAttachment(in RGTextureRef resource, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            depthBuffer = resource;
            depthBufferAccess = flags;
            depthBufferAction = new RGAttachmentAction(loadAction, storeAction);
            if ((flags & EDepthAccess.ReadOnly) != 0 || loadAction == RenderBufferLoadAction.Load)
                AddResourceRead(resource.handle);
            if ((flags & EDepthAccess.Write) != 0)
                AddResourceWrite(resource.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnableAsyncCompute(in bool value)
        {
            enableAsyncCompute = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnableNativeRenderPass(in bool value)
        {
            enableNativeRenderPass = value;
        }

        public void Clear()
        {
            name = "";
            index = -1;
            customSampler = null;
            queueObserver = null;
            importedWritesAreSideEffects = true;
            sideEffect = false;
            insertedByCompiler = false;
            for (int i = 0; i < (int)ERGResourceType.Max; ++i)
            {
                resourceReadLists[i].Clear();
                resourceWriteLists[i].Clear();
                temporalResourceList[i].Clear();
            }
            sampledResources.Clear();
            usedDrawLists.Clear();
            if (usedRendererLists == null)
                usedRendererLists = new List<RGRendererListRef>(4);
            else
                usedRendererLists.Clear();

            refCount = 0;
            enablePassCulling = true;
            enableAsyncCompute = false;
            enableNativeRenderPass = true;

            colorBufferMaxIndex = -1;
            depthBuffer = new RGTextureRef();
            depthBufferAccess = EDepthAccess.Write;
            depthBufferAction = new RGAttachmentAction();
            for (int i = 0; i < 8; ++i)
            {
                colorBuffers[i] = new RGTextureRef();
                colorBufferActions[i] = new RGAttachmentAction();
            }
        }
    }

    internal sealed class RGTransferPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGTransferPassExecuteAction<T> ExecuteAction;
        internal override bool hasExecuteAction => ExecuteAction != null;

        public RGTransferPass() : base()
        {
            passType = EPassType.Transfer;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGTransferEncoder(graphContext.cmdBuffer));
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExecuteAction = null;
            objectPool.Release(this);
        }
    }

    internal sealed class RGComputePass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGComputePassExecuteAction<T> ExecuteAction;
        internal override bool hasExecuteAction => ExecuteAction != null;

        public RGComputePass() : base()
        {
            passType = EPassType.Compute;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGComputeEncoder(graphContext.cmdBuffer));
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExecuteAction = null;
            objectPool.Release(this);
        }
    }

    internal sealed class RGRayTracingPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGRayTracingPassExecuteAction<T> ExecuteAction;
        internal override bool hasExecuteAction => ExecuteAction != null;

        public RGRayTracingPass() : base()
        {
            passType = EPassType.RayTracing;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGRaytracingEncoder(graphContext.cmdBuffer));
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExecuteAction = null;
            objectPool.Release(this);
        }
    }

    internal sealed class RGRasterPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGRasterPassExecuteAction<T> ExecuteAction;
        internal override bool hasExecuteAction => ExecuteAction != null;

        public RGRasterPass() : base()
        {
            passType = EPassType.Raster;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGRasterEncoder(graphContext.cmdBuffer, graphContext.drawLists, graphContext.rendererLists));
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExecuteAction = null;
            objectPool.Release(this);
        }
    }

    static class RGPassGuard
    {
        public static void RequireTexture(RGResourceFactory factory, in RGTextureRef textureRef, string api)
        {
            if (!textureRef.IsValid())
                throw new InvalidOperationException($"RGPass.{api} received an invalid RGTextureRef.");
            factory.RequireLive(textureRef.handle, "RGPass." + api);
        }

        public static void RequireBuffer(RGResourceFactory factory, in RGBufferRef bufferRef, string api)
        {
            if (!bufferRef.IsValid())
                throw new InvalidOperationException($"RGPass.{api} received an invalid RGBufferRef.");
            factory.RequireLive(bufferRef.handle, "RGPass." + api);
        }
    }

    // One declaration implementation for every pass kind. The public refs only keep the execute-delegate type.
    static class RGPassDeclaration
    {
        public static void MarkSideEffect(IRGPass pass) => pass.sideEffect = true;

        public static RGTextureRef ReadTexture(IRGPass pass, RGResourceFactory factory, in RGTextureRef input)
        {
            RGPassGuard.RequireTexture(factory, input, nameof(ReadTexture));
            NoteSampled(pass, input.handle);
            pass.AddResourceRead(input.handle);
            return input;
        }

        public static RGTextureRef WriteTexture(IRGPass pass, RGResourceFactory factory, in RGTextureRef input)
        {
            RGPassGuard.RequireTexture(factory, input, nameof(WriteTexture));
            NoteSampled(pass, input.handle);
            pass.AddResourceWrite(input.handle);
            return input;
        }

        public static RGBufferRef ReadBuffer(IRGPass pass, RGResourceFactory factory, in RGBufferRef bufferRef)
        {
            RGPassGuard.RequireBuffer(factory, bufferRef, nameof(ReadBuffer));
            pass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        public static RGBufferRef WriteBuffer(IRGPass pass, RGResourceFactory factory, in RGBufferRef bufferRef)
        {
            RGPassGuard.RequireBuffer(factory, bufferRef, nameof(WriteBuffer));
            pass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        static void NoteSampled(IRGPass pass, in RGResourceHandle handle)
        {
            for (int i = 0; i < pass.sampledResources.Count; ++i)
            {
                RGResourceHandle existing = pass.sampledResources[i];
                if (existing.index == handle.index && existing.type == handle.type && existing.generation == handle.generation)
                    return;
            }
            pass.sampledResources.Add(handle);
        }

        public static RGTextureRef CreatePassLocalTexture(IRGPass pass, RGResourceFactory factory, in TextureDescriptor descriptor)
        {
            RGTextureRef result = factory.CreateTexture(descriptor);
            pass.AddTemporalResource(result.handle);
            return result;
        }

        public static RGBufferRef CreatePassLocalBuffer(IRGPass pass, RGResourceFactory factory, in BufferDescriptor descriptor)
        {
            RGBufferRef result = factory.CreateBuffer(descriptor);
            pass.AddTemporalResource(result.handle);
            return result;
        }
    }

    public struct RGTransferPassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_Pass;
        RGResourceFactory m_Resources;

        internal RGTransferPassRef(IRGPass pass, RGResourceFactory resources)
        {
            m_Disposed = false;
            m_Pass = pass;
            m_Resources = resources;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        internal void KeepImportedWritesLocal() => m_Pass.importedWritesAreSideEffects = false;
        public void MarkSideEffect() => RGPassDeclaration.MarkSideEffect(m_Pass);
        public RGTextureRef ReadTexture(in RGTextureRef input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureRef WriteTexture(in RGTextureRef input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferRef);
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferRef);
        public RGTextureRef CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferRef CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public ref T GetPassData<T>() where T : struct => ref ((RGTransferPass<T>)m_Pass).passData;
        public void SetExecuteFunc<T>(RGTransferPassExecuteAction<T> execute) where T : struct => ((RGTransferPass<T>)m_Pass).ExecuteAction = execute;
        public void Dispose() { m_Disposed = true; }
    }

    public struct RGComputePassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_Pass;
        RGResourceFactory m_Resources;

        internal RGComputePassRef(IRGPass pass, RGResourceFactory resources)
        {
            m_Disposed = false;
            m_Pass = pass;
            m_Resources = resources;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        internal void KeepImportedWritesLocal() => m_Pass.importedWritesAreSideEffects = false;
        public void MarkSideEffect() => RGPassDeclaration.MarkSideEffect(m_Pass);
        public void EnableAsyncCompute(in bool value) => m_Pass.EnableAsyncCompute(value);
        public RGTextureRef ReadTexture(in RGTextureRef input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureRef WriteTexture(in RGTextureRef input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferRef);
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferRef);
        public RGTextureRef CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferRef CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public ref T GetPassData<T>() where T : struct => ref ((RGComputePass<T>)m_Pass).passData;
        public void SetExecuteFunc<T>(RGComputePassExecuteAction<T> execute) where T : struct => ((RGComputePass<T>)m_Pass).ExecuteAction = execute;
        public void Dispose() { m_Disposed = true; }
    }

    public struct RGRayTracingPassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_Pass;
        RGResourceFactory m_Resources;

        internal RGRayTracingPassRef(IRGPass pass, RGResourceFactory resources)
        {
            m_Disposed = false;
            m_Pass = pass;
            m_Resources = resources;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        public void MarkSideEffect() => RGPassDeclaration.MarkSideEffect(m_Pass);
        public RGTextureRef ReadTexture(in RGTextureRef input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureRef WriteTexture(in RGTextureRef input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferRef);
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferRef);
        public RGTextureRef CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferRef CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public ref T GetPassData<T>() where T : struct => ref ((RGRayTracingPass<T>)m_Pass).passData;
        public void SetExecuteFunc<T>(RGRayTracingPassExecuteAction<T> execute) where T : struct => ((RGRayTracingPass<T>)m_Pass).ExecuteAction = execute;
        public void Dispose() { m_Disposed = true; }
    }

    public struct RGRasterPassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_Pass;
        RGResourceFactory m_Resources;
        RGDrawListContext m_DrawLists;
        RGRendererListContext m_RendererLists;

        internal RGRasterPassRef(IRGPass pass, RGResourceFactory resources, RGDrawListContext drawLists, RGRendererListContext rendererLists)
        {
            m_Disposed = false;
            m_Pass = pass;
            m_Resources = resources;
            m_DrawLists = drawLists;
            m_RendererLists = rendererLists;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        public void MarkSideEffect() => RGPassDeclaration.MarkSideEffect(m_Pass);
        public void EnableNativeRenderPass(in bool value) => m_Pass.EnableNativeRenderPass(value);
        public RGTextureRef ReadTexture(in RGTextureRef input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureRef WriteTexture(in RGTextureRef input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferRef);
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferRef);
        public RGTextureRef CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferRef CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public RGTextureRef SetColorAttachment(in RGTextureRef renderTarget, int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            RGPassGuard.RequireTexture(m_Resources, renderTarget, nameof(SetColorAttachment));
            m_Pass.SetColorAttachment(renderTarget, index, loadAction, storeAction);
            return renderTarget;
        }

        public RGTextureRef SetDepthStencilAttachment(in RGTextureRef input, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            RGPassGuard.RequireTexture(m_Resources, input, nameof(SetDepthStencilAttachment));
            m_Pass.SetDepthStencilAttachment(input, loadAction, storeAction, flags);
            return input;
        }

        public RGDrawListRef UseDrawList(in RGDrawListRef draws)
        {
            if (m_DrawLists != null && m_DrawLists.IsLiveRef(draws))
            {
                m_Pass.usedDrawLists.Add(draws);
                RGDrawListRecord record = m_DrawLists.GetRecordCopy(draws.index);
                if (record.gpuVisibilityBuffer.IsValid())
                    m_Pass.AddResourceRead(record.gpuVisibilityBuffer.handle);
                return draws;
            }
            return RGDrawListRef.Invalid;
        }

        public RGRendererListRef UseRendererList(in RGRendererListRef list)
        {
            if (m_RendererLists != null && m_RendererLists.IsLiveRef(list))
            {
                m_Pass.usedRendererLists.Add(list);
                return list;
            }
            return RGRendererListRef.Invalid;
        }

        public ref T GetPassData<T>() where T : struct => ref ((RGRasterPass<T>)m_Pass).passData;
        public void SetExecuteFunc<T>(RGRasterPassExecuteAction<T> execute) where T : struct => ((RGRasterPass<T>)m_Pass).ExecuteAction = execute;
        public void Dispose() { m_Disposed = true; }
    }
}
