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
        internal abstract bool hasExecuteAction { get; }
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
            internal set;
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
        public RGTextureHandle depthBufferHandle
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
        public RGTextureHandle[] colorBuffersHandle
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
        // Constants the graph uploads into this pass's own constant buffers when the pass begins.
        internal readonly List<RGConstantsHandle> constants = new List<RGConstantsHandle>(2);
        public List<RGRendererListHandle> usedRendererLists;

        public IRGPass()
        {
            int resourceTypeCount = (int)ERGResourceType.Max;
            colorBufferMaxIndex = -1;
            enableNativeRenderPass = true;
            colorBuffersHandle = new RGTextureHandle[8];
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
        public void SetColorAttachment(in RGTextureHandle resourceHandle, in int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffersHandle[index] = resourceHandle;
            colorBufferActions[index] = new RGAttachmentAction(loadAction, storeAction);
            if (loadAction == RenderBufferLoadAction.Load)
            {
                AddResourceRead(resourceHandle.handle);
            }
            AddResourceWrite(resourceHandle.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthStencilAttachment(in RGTextureHandle resourceHandle, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            depthBufferHandle = resourceHandle;
            depthBufferAccess = flags;
            depthBufferAction = new RGAttachmentAction(loadAction, storeAction);
            if ((flags & EDepthAccess.ReadOnly) != 0 || loadAction == RenderBufferLoadAction.Load)
                AddResourceRead(resourceHandle.handle);
            if ((flags & EDepthAccess.Write) != 0)
                AddResourceWrite(resourceHandle.handle);
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
            constants.Clear();
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
            depthBufferHandle = new RGTextureHandle();
            depthBufferAccess = EDepthAccess.Write;
            depthBufferAction = new RGAttachmentAction();
            for (int i = 0; i < 8; ++i)
            {
                colorBuffersHandle[i] = new RGTextureHandle();
                colorBufferActions[i] = new RGAttachmentAction();
            }
        }
    }

    // One pass object for every kind. The kind is data, set when the pass is added. Execute builds that kind's encoder once.
    internal sealed class RGPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal object execute;
        internal override bool hasExecuteAction => execute != null;

        public RGPass() : base()
        {
        }

        internal void Prepare(EPassType type)
        {
            execute = null;
            passType = type;
        }

        public override void Execute(ref RGContext graphContext)
        {
            switch (passType)
            {
                case EPassType.Transfer:
                    ((RGTransferPassExecuteAction<T>)execute)(in passData, new RGTransferEncoder(graphContext.cmdBuffer, graphContext.resources));
                    break;
                case EPassType.Compute:
                    ((RGComputePassExecuteAction<T>)execute)(in passData, new RGComputeEncoder(graphContext.cmdBuffer, graphContext.resources));
                    break;
                case EPassType.RayTracing:
                    ((RGRayTracingPassExecuteAction<T>)execute)(in passData, new RGRaytracingEncoder(graphContext.cmdBuffer, graphContext.resources));
                    break;
                default:
                    ((RGRasterPassExecuteAction<T>)execute)(in passData, new RGRasterEncoder(graphContext.cmdBuffer, graphContext.resources, graphContext.rendererLists, graphContext.extension));
                    break;
            }
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            execute = null;
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

    // One declaration implementation for every pass kind; the typed handles forward to it.
    static class RGPassDeclaration
    {
        public static RGTextureHandle ReadTexture(IRGPass pass, RGResourceFactory factory, in RGTextureHandle inputHandle)
        {
            RGPassGuard.RequireTexture(factory, inputHandle, RGPassGuard.ReadTexture);
            NoteSampled(pass, inputHandle.handle);
            pass.AddResourceRead(inputHandle.handle);
            return inputHandle;
        }

        public static RGTextureHandle WriteTexture(IRGPass pass, RGResourceFactory factory, in RGTextureHandle inputHandle)
        {
            RGPassGuard.RequireTexture(factory, inputHandle, RGPassGuard.WriteTexture);
            NoteSampled(pass, inputHandle.handle);
            pass.AddResourceWrite(inputHandle.handle);
            return inputHandle;
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
                RGResourceHandle existingHandle = pass.sampledResources[i];
                if (existingHandle.Equals(handle))
                    return;
            }
            pass.sampledResources.Add(handle);
        }

        public static RGTextureHandle CreatePassLocalTexture(IRGPass pass, RGResourceFactory factory, in TextureDescriptor descriptor)
        {
            RGTextureHandle resultHandle = factory.CreateTexture(descriptor);
            pass.AddTemporalResource(resultHandle.handle);
            return resultHandle;
        }

        public static RGBufferHandle CreatePassLocalBuffer(IRGPass pass, RGResourceFactory factory, in BufferDescriptor descriptor)
        {
            RGBufferHandle resultHandle = factory.CreateBuffer(descriptor);
            pass.AddTemporalResource(resultHandle.handle);
            return resultHandle;
        }

        public static RGConstantsHandle WriteConstants<T>(IRGPass pass, RGResourceFactory factory, in T constants) where T : unmanaged
        {
            int offset = factory.constants.Write(constants, out int units);
            var descriptor = new BufferDescriptor(units, RGConstantStaging.Unit, GraphicsBuffer.Target.Constant) { name = RGConstantsName<T>.value };
            var handle = new RGConstantsHandle(CreatePassLocalBuffer(pass, factory, descriptor), offset, units);
            pass.constants.Add(handle);
            return handle;
        }
    }

    // Shared recording operations. The kind stays on the view, so a compute handle cannot declare a color attachment.
    public interface IPassHandle
    {
        internal IRGPass Pass { get; }
        internal RGResourceFactory Resources { get; }
    }

    public static class RGPassHandleExtensions
    {
        public static void EnablePassCulling<THandle>(this THandle handle, bool value) where THandle : struct, IPassHandle
            => handle.Pass.enablePassCulling = value;

        internal static void SetQueueObserver<THandle>(this THandle handle, IRGPassQueueObserver observer) where THandle : struct, IPassHandle
            => handle.Pass.queueObserver = observer;

        public static RGTextureHandle ReadTexture<THandle>(this THandle handle, in RGTextureHandle inputHandle) where THandle : struct, IPassHandle
            => RGPassDeclaration.ReadTexture(handle.Pass, handle.Resources, inputHandle);

        public static RGTextureHandle WriteTexture<THandle>(this THandle handle, in RGTextureHandle inputHandle) where THandle : struct, IPassHandle
            => RGPassDeclaration.WriteTexture(handle.Pass, handle.Resources, inputHandle);

        public static RGBufferHandle ReadBuffer<THandle>(this THandle handle, in RGBufferHandle bufferHandle) where THandle : struct, IPassHandle
            => RGPassDeclaration.ReadBuffer(handle.Pass, handle.Resources, bufferHandle);

        public static RGBufferHandle WriteBuffer<THandle>(this THandle handle, in RGBufferHandle bufferHandle) where THandle : struct, IPassHandle
            => RGPassDeclaration.WriteBuffer(handle.Pass, handle.Resources, bufferHandle);

        public static RGTextureHandle CreatePassLocalTexture<THandle>(this THandle handle, in TextureDescriptor descriptor) where THandle : struct, IPassHandle
            => RGPassDeclaration.CreatePassLocalTexture(handle.Pass, handle.Resources, descriptor);

        public static RGBufferHandle CreatePassLocalBuffer<THandle>(this THandle handle, in BufferDescriptor descriptor) where THandle : struct, IPassHandle
            => RGPassDeclaration.CreatePassLocalBuffer(handle.Pass, handle.Resources, descriptor);

        internal static void KeepImportedWritesLocal(IRGPass pass) => pass.importedWritesAreSideEffects = false;
    }

    public struct RGTransferPassHandle<TData> : IPassHandle where TData : struct
    {
        readonly RGPass<TData> m_Pass;
        readonly RGResourceFactory m_Resources;

        internal RGTransferPassHandle(RGPass<TData> pass, RGResourceFactory resources)
        {
            m_Pass = pass;
            m_Resources = resources;
        }

        IRGPass IPassHandle.Pass => m_Pass;
        RGResourceFactory IPassHandle.Resources => m_Resources;

        internal void KeepImportedWritesLocal() => RGPassHandleExtensions.KeepImportedWritesLocal(m_Pass);
        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGTransferPassExecuteAction<TData> execute) => m_Pass.execute = execute;
    }

    public struct RGComputePassHandle<TData> : IPassHandle where TData : struct
    {
        readonly RGPass<TData> m_Pass;
        readonly RGResourceFactory m_Resources;

        internal RGComputePassHandle(RGPass<TData> pass, RGResourceFactory resources)
        {
            m_Pass = pass;
            m_Resources = resources;
        }

        IRGPass IPassHandle.Pass => m_Pass;
        RGResourceFactory IPassHandle.Resources => m_Resources;

        internal void KeepImportedWritesLocal() => RGPassHandleExtensions.KeepImportedWritesLocal(m_Pass);
        public void EnableAsyncCompute(in bool value) => m_Pass.EnableAsyncCompute(value);
        /// <summary>Records constants this pass binds as a constant buffer; the graph uploads them when the pass begins.</summary>
        public RGConstantsHandle WriteConstants<T>(in T constants) where T : unmanaged => RGPassDeclaration.WriteConstants(m_Pass, m_Resources, constants);
        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGComputePassExecuteAction<TData> execute) => m_Pass.execute = execute;
    }

    public struct RGRayTracingPassHandle<TData> : IPassHandle where TData : struct
    {
        readonly RGPass<TData> m_Pass;
        readonly RGResourceFactory m_Resources;

        internal RGRayTracingPassHandle(RGPass<TData> pass, RGResourceFactory resources)
        {
            m_Pass = pass;
            m_Resources = resources;
        }

        IRGPass IPassHandle.Pass => m_Pass;
        RGResourceFactory IPassHandle.Resources => m_Resources;

        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGRayTracingPassExecuteAction<TData> execute) => m_Pass.execute = execute;
    }

    public struct RGRasterPassHandle<TData> : IPassHandle where TData : struct
    {
        readonly RGPass<TData> m_Pass;
        readonly RGResourceFactory m_Resources;
        readonly RGRendererListContext m_RendererLists;
        readonly IRGGraphExtension m_Extension;

        internal RGRasterPassHandle(RGPass<TData> pass, RGResourceFactory resources, RGRendererListContext rendererLists, IRGGraphExtension extension)
        {
            m_Pass = pass;
            m_Resources = resources;
            m_RendererLists = rendererLists;
            m_Extension = extension;
        }

        IRGPass IPassHandle.Pass => m_Pass;
        RGResourceFactory IPassHandle.Resources => m_Resources;

        // Graph extensions record their consumer edges on the pass they extend and accept only their own builder's handles.
        internal IRGPass pass => m_Pass;
        internal IRGGraphExtension extension => m_Extension;

        public void EnableNativeRenderPass(in bool value) => m_Pass.EnableNativeRenderPass(value);
        public RGConstantsHandle WriteConstants<T>(in T constants) where T : unmanaged => RGPassDeclaration.WriteConstants(m_Pass, m_Resources, constants);

        public RGTextureHandle SetColorAttachment(in RGTextureHandle renderTargetHandle, int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            RGPassGuard.RequireTexture(m_Resources, renderTargetHandle, nameof(SetColorAttachment));
            m_Pass.SetColorAttachment(renderTargetHandle, index, loadAction, storeAction);
            return renderTargetHandle;
        }

        public RGTextureHandle SetDepthStencilAttachment(in RGTextureHandle inputHandle, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            RGPassGuard.RequireTexture(m_Resources, inputHandle, nameof(SetDepthStencilAttachment));
            m_Pass.SetDepthStencilAttachment(inputHandle, loadAction, storeAction, flags);
            return inputHandle;
        }

        public RGRendererListHandle UseRendererList(in RGRendererListHandle listHandle)
        {
            if (!m_RendererLists.IsLive(listHandle))
                throw new InvalidOperationException($"'{m_Pass.name}' uses a renderer list from another graph or an earlier frame.");
            m_Pass.usedRendererLists.Add(listHandle);
            return listHandle;
        }

        public ref TData GetPassData() => ref m_Pass.passData;
        public void SetExecuteFunc(RGRasterPassExecuteAction<TData> execute) => m_Pass.execute = execute;
    }
}
