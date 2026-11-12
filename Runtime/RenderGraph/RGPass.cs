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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Graph-wide diagnostics hook: told when each pass has recorded its commands and when its segment was queued.
    internal interface IRGExecutionObserver
    {
        void PassRecorded(string passName);
        void PassQueued(string passName);
    }
#endif

    internal abstract class IRGPass
    {
        public int index;
        public string name;
        public ProfilingSampler customSampler;
        internal IRGPassQueueObserver queueObserver;
        internal bool importedWritesAreSideEffects = true;

        public int refCount
        {
            get;
            protected set;
        }
        public EPassType passType
        {
            get;
            protected set;
        }
        public int colorBufferMaxIndex
        {
            get;
            protected set;
        }
        public bool enablePassCulling
        {
            get;
            internal set;
        }
        public bool enableAsyncCompute
        {
            get;
            protected set;
        }
        public bool enableNativeRenderPass
        {
            get;
            protected set;
        }
        public RGTextureHandle depthBuffer
        {
            get;
            protected set;
        }
        public EDepthAccess depthBufferAccess
        {
            get;
            protected set;
        }
        public RGAttachmentAction depthBufferAction
        {
            get;
            protected set;
        }
        public RGTextureHandle[] colorBuffers
        {
            get;
            protected set;
        }
        public RGAttachmentAction[] colorBufferActions
        {
            get;
            protected set;
        }


        public List<RGResourceHandle>[] resourceReadLists;
        public List<RGResourceHandle>[] resourceWriteLists;
        public List<RGResourceHandle>[] temporalResourceList;
        public List<RGResourceHandle> sampledResources;
        // Extension-defined ids a pass consumes (mesh draw-list indices). The core only stores and clears them.
        internal readonly List<int> extensionUses = new List<int>(4);
        public List<RGRendererListHandle> usedRendererLists;

        public IRGPass()
        {
            int resourceTypeCount = (int)ERGResourceType.Max;
            colorBufferMaxIndex = -1;
            enableNativeRenderPass = true;
            colorBuffers = new RGTextureHandle[8];
            colorBufferActions = new RGAttachmentAction[8];
            resourceReadLists = new List<RGResourceHandle>[resourceTypeCount];
            resourceWriteLists = new List<RGResourceHandle>[resourceTypeCount];
            temporalResourceList = new List<RGResourceHandle>[resourceTypeCount];
            sampledResources = new List<RGResourceHandle>(4);
            usedRendererLists = new List<RGRendererListHandle>(4);

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
        public void SetColorAttachment(in RGTextureHandle resource, in int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            colorBufferActions[index] = new RGAttachmentAction(loadAction, storeAction);
            if (loadAction == RenderBufferLoadAction.Load)
            {
                AddResourceRead(resource.handle);
            }
            AddResourceWrite(resource.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthStencilAttachment(in RGTextureHandle resource, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
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
            for (int i = 0; i < (int)ERGResourceType.Max; ++i)
            {
                resourceReadLists[i].Clear();
                resourceWriteLists[i].Clear();
                temporalResourceList[i].Clear();
            }
            sampledResources.Clear();
            extensionUses.Clear();
            if (usedRendererLists == null)
                usedRendererLists = new List<RGRendererListHandle>(4);
            else
                usedRendererLists.Clear();

            refCount = 0;
            enablePassCulling = true;
            enableAsyncCompute = false;
            enableNativeRenderPass = true;

            colorBufferMaxIndex = -1;
            depthBuffer = new RGTextureHandle();
            depthBufferAccess = EDepthAccess.Write;
            depthBufferAction = new RGAttachmentAction();
            for (int i = 0; i < 8; ++i)
            {
                colorBuffers[i] = new RGTextureHandle();
                colorBufferActions[i] = new RGAttachmentAction();
            }
        }
    }

    internal sealed class RGTransferPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGTransferPassExecuteAction<T> ExecuteAction;
        public RGTransferPass() : base()
        {
            passType = EPassType.Transfer;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGTransferEncoder(graphContext.cmdBuffer, graphContext.resources));
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
        public RGComputePass() : base()
        {
            passType = EPassType.Compute;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGComputeEncoder(graphContext.cmdBuffer, graphContext.resources));
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
        public RGRayTracingPass() : base()
        {
            passType = EPassType.RayTracing;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGRaytracingEncoder(graphContext.cmdBuffer, graphContext.resources));
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
        public RGRasterPass() : base()
        {
            passType = EPassType.Raster;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExecuteAction(in passData, new RGRasterEncoder(graphContext.cmdBuffer, graphContext.resources, graphContext.rendererLists, graphContext.extension));
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
        internal const string ReadTexture = "RGPass.ReadTexture";
        internal const string WriteTexture = "RGPass.WriteTexture";
        internal const string ReadBuffer = "RGPass.ReadBuffer";
        internal const string WriteBuffer = "RGPass.WriteBuffer";

        public static void RequireTexture(RGResourceFactory factory, in RGTextureHandle textureHandle, string api)
        {
            if (!textureHandle.IsValid())
                throw new InvalidOperationException(api + " received an invalid RGTextureHandle.");
            factory.RequireLive(textureHandle.handle, api);
        }

        public static void RequireBuffer(RGResourceFactory factory, in RGBufferHandle bufferHandle, string api)
        {
            if (!bufferHandle.IsValid())
                throw new InvalidOperationException(api + " received an invalid RGBufferHandle.");
            factory.RequireLive(bufferHandle.handle, api);
        }
    }

    // One declaration implementation for every pass kind. The public handles only keep the execute-delegate type.
    static class RGPassDeclaration
    {

        public static RGTextureHandle ReadTexture(IRGPass pass, RGResourceFactory factory, in RGTextureHandle input)
        {
            RGPassGuard.RequireTexture(factory, input, RGPassGuard.ReadTexture);
            NoteSampled(pass, input.handle);
            pass.AddResourceRead(input.handle);
            return input;
        }

        public static RGTextureHandle WriteTexture(IRGPass pass, RGResourceFactory factory, in RGTextureHandle input)
        {
            RGPassGuard.RequireTexture(factory, input, RGPassGuard.WriteTexture);
            NoteSampled(pass, input.handle);
            pass.AddResourceWrite(input.handle);
            return input;
        }

        public static RGBufferHandle ReadBuffer(IRGPass pass, RGResourceFactory factory, in RGBufferHandle bufferHandle)
        {
            RGPassGuard.RequireBuffer(factory, bufferHandle, RGPassGuard.ReadBuffer);
            pass.AddResourceRead(bufferHandle.handle);
            return bufferHandle;
        }

        public static RGBufferHandle WriteBuffer(IRGPass pass, RGResourceFactory factory, in RGBufferHandle bufferHandle)
        {
            RGPassGuard.RequireBuffer(factory, bufferHandle, RGPassGuard.WriteBuffer);
            pass.AddResourceWrite(bufferHandle.handle);
            return bufferHandle;
        }

        static void NoteSampled(IRGPass pass, in RGResourceHandle handle)
        {
            for (int i = 0; i < pass.sampledResources.Count; ++i)
            {
                RGResourceHandle existing = pass.sampledResources[i];
                if (existing.Equals(handle))
                    return;
            }
            pass.sampledResources.Add(handle);
        }

        public static RGTextureHandle CreatePassLocalTexture(IRGPass pass, RGResourceFactory factory, in TextureDescriptor descriptor)
        {
            RGTextureHandle result = factory.CreateTexture(descriptor);
            pass.AddTemporalResource(result.handle);
            return result;
        }

        public static RGBufferHandle CreatePassLocalBuffer(IRGPass pass, RGResourceFactory factory, in BufferDescriptor descriptor)
        {
            RGBufferHandle result = factory.CreateBuffer(descriptor);
            pass.AddTemporalResource(result.handle);
            return result;
        }
    }

    // One recorded transfer pass; the pass-data type is fixed when the pass is added.
    public struct RGTransferPassHandle<TData> : IDisposable where TData : struct
    {
        readonly RGTransferPass<TData> m_Pass;
        RGResourceFactory m_Resources;

        internal RGTransferPassHandle(RGTransferPass<TData> pass, RGResourceFactory resources)
        {
            m_Pass = pass;
            m_Resources = resources;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        internal void KeepImportedWritesLocal() => m_Pass.importedWritesAreSideEffects = false;
        public void EnablePassCulling(bool value) => m_Pass.enablePassCulling = value;
        public RGTextureHandle ReadTexture(in RGTextureHandle input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureHandle WriteTexture(in RGTextureHandle input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferHandle ReadBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferHandle);
        public RGBufferHandle WriteBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferHandle);
        public RGTextureHandle CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferHandle CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGTransferPassExecuteAction<TData> execute) => m_Pass.ExecuteAction = execute;

        // Recording a pass ends here; a pass without an execute function is a recording defect.
        public void Dispose()
        {
            if (m_Pass.ExecuteAction == null)
                throw new InvalidOperationException($"Render graph pass '{m_Pass.name}' was recorded without an execute function.");
        }
    }

    // One recorded compute pass; the pass-data type is fixed when the pass is added.
    public struct RGComputePassHandle<TData> : IDisposable where TData : struct
    {
        readonly RGComputePass<TData> m_Pass;
        RGResourceFactory m_Resources;

        internal RGComputePassHandle(RGComputePass<TData> pass, RGResourceFactory resources)
        {
            m_Pass = pass;
            m_Resources = resources;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        internal void KeepImportedWritesLocal() => m_Pass.importedWritesAreSideEffects = false;
        public void EnablePassCulling(bool value) => m_Pass.enablePassCulling = value;
        public void EnableAsyncCompute(in bool value) => m_Pass.EnableAsyncCompute(value);
        public RGTextureHandle ReadTexture(in RGTextureHandle input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureHandle WriteTexture(in RGTextureHandle input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferHandle ReadBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferHandle);
        public RGBufferHandle WriteBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferHandle);
        public RGTextureHandle CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferHandle CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGComputePassExecuteAction<TData> execute) => m_Pass.ExecuteAction = execute;

        // Recording a pass ends here; a pass without an execute function is a recording defect.
        public void Dispose()
        {
            if (m_Pass.ExecuteAction == null)
                throw new InvalidOperationException($"Render graph pass '{m_Pass.name}' was recorded without an execute function.");
        }
    }

    // One recorded raytracing pass; the pass-data type is fixed when the pass is added.
    public struct RGRayTracingPassHandle<TData> : IDisposable where TData : struct
    {
        readonly RGRayTracingPass<TData> m_Pass;
        RGResourceFactory m_Resources;

        internal RGRayTracingPassHandle(RGRayTracingPass<TData> pass, RGResourceFactory resources)
        {
            m_Pass = pass;
            m_Resources = resources;
        }

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        public void EnablePassCulling(bool value) => m_Pass.enablePassCulling = value;
        public RGTextureHandle ReadTexture(in RGTextureHandle input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureHandle WriteTexture(in RGTextureHandle input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferHandle ReadBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferHandle);
        public RGBufferHandle WriteBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferHandle);
        public RGTextureHandle CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferHandle CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGRayTracingPassExecuteAction<TData> execute) => m_Pass.ExecuteAction = execute;

        // Recording a pass ends here; a pass without an execute function is a recording defect.
        public void Dispose()
        {
            if (m_Pass.ExecuteAction == null)
                throw new InvalidOperationException($"Render graph pass '{m_Pass.name}' was recorded without an execute function.");
        }
    }

    // One recorded raster pass; the pass-data type is fixed when the pass is added.
    public struct RGRasterPassHandle<TData> : IDisposable where TData : struct
    {
        readonly RGRasterPass<TData> m_Pass;
        RGResourceFactory m_Resources;
        RGRendererListContext m_RendererLists;
        IRGGraphExtension m_Extension;

        internal RGRasterPassHandle(RGRasterPass<TData> pass, RGResourceFactory resources, RGRendererListContext rendererLists, IRGGraphExtension extension)
        {
            m_Pass = pass;
            m_Resources = resources;
            m_RendererLists = rendererLists;
            m_Extension = extension;
        }

        // Graph extensions record their consumer edges on the pass they extend and accept only their own builder's handles.
        internal IRGPass pass => m_Pass;
        internal IRGGraphExtension extension => m_Extension;

        internal void SetQueueObserver(IRGPassQueueObserver observer) => m_Pass.queueObserver = observer;
        public void EnablePassCulling(bool value) => m_Pass.enablePassCulling = value;
        public void EnableNativeRenderPass(in bool value) => m_Pass.EnableNativeRenderPass(value);
        public RGTextureHandle ReadTexture(in RGTextureHandle input) => RGPassDeclaration.ReadTexture(m_Pass, m_Resources, input);
        public RGTextureHandle WriteTexture(in RGTextureHandle input) => RGPassDeclaration.WriteTexture(m_Pass, m_Resources, input);
        public RGBufferHandle ReadBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.ReadBuffer(m_Pass, m_Resources, bufferHandle);
        public RGBufferHandle WriteBuffer(in RGBufferHandle bufferHandle) => RGPassDeclaration.WriteBuffer(m_Pass, m_Resources, bufferHandle);
        public RGTextureHandle CreatePassLocalTexture(in TextureDescriptor descriptor) => RGPassDeclaration.CreatePassLocalTexture(m_Pass, m_Resources, descriptor);
        public RGBufferHandle CreatePassLocalBuffer(in BufferDescriptor descriptor) => RGPassDeclaration.CreatePassLocalBuffer(m_Pass, m_Resources, descriptor);

        public RGTextureHandle SetColorAttachment(in RGTextureHandle renderTarget, int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            RGPassGuard.RequireTexture(m_Resources, renderTarget, nameof(SetColorAttachment));
            m_Pass.SetColorAttachment(renderTarget, index, loadAction, storeAction);
            return renderTarget;
        }

        public RGTextureHandle SetDepthStencilAttachment(in RGTextureHandle input, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            RGPassGuard.RequireTexture(m_Resources, input, nameof(SetDepthStencilAttachment));
            m_Pass.SetDepthStencilAttachment(input, loadAction, storeAction, flags);
            return input;
        }

        public RGRendererListHandle UseRendererList(in RGRendererListHandle list)
        {
            if (!m_RendererLists.IsLive(list))
                throw new InvalidOperationException($"'{m_Pass.name}' uses a renderer list from another graph or an earlier frame.");
            m_Pass.usedRendererLists.Add(list);
            return list;
        }

        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGRasterPassExecuteAction<TData> execute) => m_Pass.ExecuteAction = execute;

        // Recording a pass ends here; a pass without an execute function is a recording defect.
        public void Dispose()
        {
            if (m_Pass.ExecuteAction == null)
                throw new InvalidOperationException($"Render graph pass '{m_Pass.name}' was recorded without an execute function.");
        }
    }
}
