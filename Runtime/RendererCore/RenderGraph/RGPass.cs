using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;
using System.Net.Mime;

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

    public delegate void RGTransferPassExecuteAction<T>(in T passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) where T : struct;
    public delegate void RGComputePassExecuteAction<T>(in T passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) where T : struct;
    public delegate void RGRayTracingPassExecuteAction<T>(in T passData, in RGRaytracingEncoder cmdEncoder, RGObjectPool objectPool) where T : struct;
    public delegate void RGRasterPassExecuteAction<T>(in T passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) where T : struct;

    internal abstract class IRGPass
    {
        public int index;
        public string name;
        public ProfilingSampler customSampler;

        public int refCount { get; protected set; }
        public EPassType passType { get; protected set; }
        public int colorBufferMaxIndex { get; protected set; }
        public bool enablePassCulling { get; protected set; }
        public bool enableAsyncCompute { get; protected set; }
        public RGTextureRef depthBuffer { get; protected set; }
        public EDepthAccess depthBufferAccess { get; protected set; }
        public RGAttachmentAction depthBufferAction { get; protected set; }
        public RGTextureRef[] colorBuffers { get; protected set; }
        public RGAttachmentAction[] colorBufferActions { get; protected set; }

        internal virtual bool hasExecuteAction => false;

        public List<RGResourceHandle>[] resourceReadLists;
        public List<RGResourceHandle>[] resourceWriteLists;
        public List<RGResourceHandle>[] temporalResourceList;

        public IRGPass()
        {
            colorBufferMaxIndex = -1;
            colorBuffers = new RGTextureRef[8];
            colorBufferActions = new RGAttachmentAction[8];
            resourceReadLists = new List<RGResourceHandle>[2];
            resourceWriteLists = new List<RGResourceHandle>[2];
            temporalResourceList = new List<RGResourceHandle>[2];

            for (int i = 0; i < 2; ++i)
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
            AddResourceWrite(resource.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthStencilAttachment(in RGTextureRef resource, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            depthBuffer = resource;
            depthBufferAccess = flags;
            depthBufferAction = new RGAttachmentAction(loadAction, storeAction);
            if ((flags & EDepthAccess.ReadOnly) != 0) 
            {
                AddResourceRead(resource.handle);
            }
                
            if ((flags & EDepthAccess.Write) != 0) 
            {
                AddResourceWrite(resource.handle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnablePassCulling(in bool value)
        {
            enablePassCulling = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnableAsyncCompute(in bool value)
        {
            enableAsyncCompute = value;
        }

        public void Clear()
        {
            name = "";
            index = -1;
            customSampler = null;
            for (int i = 0; i < 2; ++i)
            {
                resourceReadLists[i].Clear();
                resourceWriteLists[i].Clear();
                temporalResourceList[i].Clear();
            }

            refCount = 0;
            enablePassCulling = true;
            enableAsyncCompute = false;

            // Invalidate everything
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
        internal RGTransferPassExecuteAction<T> ExcuteAction;
        internal override bool hasExecuteAction { get { return ExcuteAction != null; } }

        public RGTransferPass() : base()
        {
            passType = EPassType.Transfer;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExcuteAction(in passData, new RGTransferEncoder(graphContext.cmdBuffer), graphContext.objectPool);
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExcuteAction = null;
            objectPool.Release(this);
        }
    }

    internal sealed class RGComputePass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGComputePassExecuteAction<T> ExcuteAction;
        internal override bool hasExecuteAction { get { return ExcuteAction != null; } }

        public RGComputePass() : base()
        {
            passType = EPassType.Compute;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExcuteAction(in passData, new RGComputeEncoder(graphContext.cmdBuffer), graphContext.objectPool);
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExcuteAction = null;
            objectPool.Release(this);
        }
    }

    internal sealed class RGRayTracingPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGRayTracingPassExecuteAction<T> ExcuteAction;
        internal override bool hasExecuteAction { get { return ExcuteAction != null; } }

        public RGRayTracingPass() : base()
        {
            passType = EPassType.RayTracing;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExcuteAction(in passData, new RGRaytracingEncoder(graphContext.cmdBuffer), graphContext.objectPool);
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExcuteAction = null;
            objectPool.Release(this);
        }
    }

    internal sealed class RGRasterPass<T> : IRGPass where T : struct
    {
        internal T passData;
        internal RGRasterPassExecuteAction<T> ExcuteAction;
        internal override bool hasExecuteAction { get { return ExcuteAction != null; } }

        public RGRasterPass() : base()
        {
            passType = EPassType.Raster;
        }

        public override void Execute(ref RGContext graphContext)
        {
            ExcuteAction(in passData, new RGRasterEncoder(graphContext.cmdBuffer), graphContext.objectPool);
        }

        public override void Release(RGObjectPool objectPool)
        {
            Clear();
            ExcuteAction = null;
            objectPool.Release(this);
        }
    }

    public struct RGTransferPassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_TransferPass;
        RGResourceFactory m_ResourceFactory;

        internal RGTransferPassRef(IRGPass transferPass, RGResourceFactory resourceFactory)
        {
            m_Disposed = false;
            m_TransferPass = transferPass;
            m_ResourceFactory = resourceFactory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnablePassCulling(in bool value)
        {
            m_TransferPass.EnablePassCulling(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ReadTexture(in RGTextureRef input)
        {
            m_TransferPass.AddResourceRead(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef WriteTexture(in RGTextureRef input)
        {
            m_TransferPass.AddResourceWrite(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTemporaryTexture(in TextureDescriptor descriptor)
        {
            var result = m_ResourceFactory.CreateTexture(descriptor, 0, m_TransferPass.index);
            m_TransferPass.AddTemporalResource(result.handle);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef)
        {
            m_TransferPass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef)
        {
            m_TransferPass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateTemporaryBuffer(in BufferDescriptor descriptor)
        {
            RGBufferRef bufferRef = m_ResourceFactory.CreateBuffer(descriptor, m_TransferPass.index);
            m_TransferPass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetPassData<T>() where T : struct
        {
            return ref ((RGTransferPass<T>)m_TransferPass).passData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExecuteFunc<T>(RGTransferPassExecuteAction<T> ExcuteAction) where T : struct
        {
            ((RGTransferPass<T>)m_TransferPass).ExcuteAction = ExcuteAction;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed) { return; }
            m_Disposed = true;
        }
    }

    public struct RGComputePassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_ComputePass;
        RGResourceFactory m_ResourceFactory;

        internal RGComputePassRef(IRGPass computePass, RGResourceFactory resourceFactory)
        {
            m_Disposed = false;
            m_ComputePass = computePass;
            m_ResourceFactory = resourceFactory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnablePassCulling(in bool value)
        {
            m_ComputePass.EnablePassCulling(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnableAsyncCompute(in bool value)
        {
            m_ComputePass.EnableAsyncCompute(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ReadTexture(in RGTextureRef input)
        {
            m_ComputePass.AddResourceRead(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef WriteTexture(in RGTextureRef input)
        {
            m_ComputePass.AddResourceWrite(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTemporaryTexture(in TextureDescriptor descriptor)
        {
            var result = m_ResourceFactory.CreateTexture(descriptor, 0, m_ComputePass.index);
            m_ComputePass.AddTemporalResource(result.handle);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef)
        {
            m_ComputePass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef)
        {
            m_ComputePass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateTemporaryBuffer(in BufferDescriptor descriptor)
        {
            RGBufferRef bufferRef = m_ResourceFactory.CreateBuffer(descriptor, m_ComputePass.index);
            m_ComputePass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetPassData<T>() where T : struct
        {
            return ref ((RGComputePass<T>)m_ComputePass).passData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExecuteFunc<T>(RGComputePassExecuteAction<T> ExcuteAction) where T : struct
        {
            ((RGComputePass<T>)m_ComputePass).ExcuteAction = ExcuteAction;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed) { return; }
            m_Disposed = true;
        }
    }

    public struct RGRayTracingPassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_RayTracingPass;
        RGResourceFactory m_ResourceFactory;

        internal RGRayTracingPassRef(IRGPass rayTracingPass, RGResourceFactory resourceFactory)
        {
            m_Disposed = false;
            m_RayTracingPass = rayTracingPass;
            m_ResourceFactory = resourceFactory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnablePassCulling(in bool value)
        {
            m_RayTracingPass.EnablePassCulling(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ReadTexture(in RGTextureRef input)
        {
            m_RayTracingPass.AddResourceRead(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef WriteTexture(in RGTextureRef input)
        {
            m_RayTracingPass.AddResourceWrite(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTemporaryTexture(in TextureDescriptor descriptor)
        {
            var result = m_ResourceFactory.CreateTexture(descriptor, 0, m_RayTracingPass.index);
            m_RayTracingPass.AddTemporalResource(result.handle);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef)
        {
            m_RayTracingPass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef)
        {
            m_RayTracingPass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateTemporaryBuffer(in BufferDescriptor descriptor)
        {
            RGBufferRef bufferRef = m_ResourceFactory.CreateBuffer(descriptor, m_RayTracingPass.index);
            m_RayTracingPass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetPassData<T>() where T : struct
        {
            return ref ((RGRayTracingPass<T>)m_RayTracingPass).passData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExecuteFunc<T>(RGRayTracingPassExecuteAction<T> ExcuteAction) where T : struct
        {
            ((RGRayTracingPass<T>)m_RayTracingPass).ExcuteAction = ExcuteAction;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed) { return; }
            m_Disposed = true;
        }
    }

    public struct RGRasterPassRef : IDisposable
    {
        bool m_Disposed;
        IRGPass m_RasterPass;
        RGResourceFactory m_ResourceFactory;

        internal RGRasterPassRef(IRGPass rasterPass, RGResourceFactory resourceFactory)
        {
            m_Disposed = false;
            m_RasterPass = rasterPass;
            m_ResourceFactory = resourceFactory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnablePassCulling(in bool value)
        {
            m_RasterPass.EnablePassCulling(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef ReadTexture(in RGTextureRef input)
        {
            m_RasterPass.AddResourceRead(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef WriteTexture(in RGTextureRef input)
        {
            m_RasterPass.AddResourceWrite(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef SetColorAttachment(in RGTextureRef renderTarget, int index, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction)
        {
            m_RasterPass.SetColorAttachment(renderTarget, index, loadAction, storeAction);
            return renderTarget;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef SetDepthStencilAttachment(in RGTextureRef input, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            m_RasterPass.SetDepthStencilAttachment(input, loadAction, storeAction, flags);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateTemporaryTexture(in TextureDescriptor descriptor)
        {
            var result = m_ResourceFactory.CreateTexture(descriptor, 0, m_RasterPass.index);
            m_RasterPass.AddTemporalResource(result.handle);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef ReadBuffer(in RGBufferRef bufferRef)
        {
            m_RasterPass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef WriteBuffer(in RGBufferRef bufferRef)
        {
            m_RasterPass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateTemporaryBuffer(in BufferDescriptor descriptor)
        {
            RGBufferRef bufferRef = m_ResourceFactory.CreateBuffer(descriptor, m_RasterPass.index);
            m_RasterPass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetPassData<T>() where T : struct
        {
            return ref ((RGRasterPass<T>)m_RasterPass).passData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExecuteFunc<T>(RGRasterPassExecuteAction<T> ExcuteAction) where T : struct
        {
            ((RGRasterPass<T>)m_RasterPass).ExcuteAction = ExcuteAction;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed) { return; }
            m_Disposed = true;
        }
    }
}