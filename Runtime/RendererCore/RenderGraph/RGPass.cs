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

    /// <summary>
    /// 定义了对某个Render Attachment的访问意图，用于Render Graph编译器自动推导Load/Store Action及优化（如Pass Merge/Subpass Merge）。
    /// </summary>
    public enum EAccessFlag : byte
    {
        /// <summary>
        /// 读取操作。
        /// 对于Color Attachment，意味着作为Texture进行采样（会中断Subpass）。
        /// 对于Depth Attachment，意味着仅用于深度测试，不写入深度。
        /// 对于Input Attachment，意味着在同一像素位置读取（触发Subpass合并）。
        /// </summary>
        Read = 0,

        /// <summary>
        /// 写入操作，需要保留Attachment的现有内容。
        /// 通常用于半透明混合、或在已有图像上进行局部绘制。
        /// RG将推导出 LoadAction = Load。
        /// </summary>
        Write = 1,

        /// <summary>
        /// 全屏写入，不关心Attachment的现有内容，但会确保写入所有像素（例如天空盒、全屏后处理）。
        /// 这是最强的优化提示，RG将推导出 LoadAction = DontCare 或 Clear。
        /// </summary>
        WriteAll = 2,

        /// <summary>
        /// 写入操作，但不关心Attachment的现有内容，且不保证全屏写入（例如不透明物体的GBuffer Pass，被剔除的区域不会被写入）。
        /// 强提示RG"不要Load"，RG将推导出 LoadAction = DontCare。开发者需自行确保不会读取到未定义区域。
        /// </summary>
        Discard = 3,

        /// <summary>
        /// 读写操作，目前主要用于深度附件，表示需要进行深度测试并同时写入深度。
        /// </summary>
        ReadWrite = 4,
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
        public bool allowPassMerge { get; protected set; }
        public RGTextureRef depthBuffer { get; protected set; }
        public EDepthAccess depthBufferAccess { get; protected set; }
        public RGAttachmentAction depthBufferAction { get; protected set; }
        public RGTextureRef[] colorBuffers { get; protected set; }
        public RGAttachmentAction[] colorBufferActions { get; protected set; }
        
        // 新增：输入附件支持（用于Subpass合并）
        public List<RGTextureRef> inputAttachments { get; protected set; }
        public List<EAccessFlag> inputAttachmentAccess { get; protected set; }
        
        // 新增：访问标志支持（用于自动推导Load/Store Action）
        public EAccessFlag[] colorBufferAccessFlags { get; protected set; }
        public EAccessFlag depthBufferAccessFlag { get; protected set; }

        internal virtual bool hasExecuteAction => false;

        public List<RGResourceHandle>[] resourceReadLists;
        public List<RGResourceHandle>[] resourceWriteLists;
        public List<RGResourceHandle>[] temporalResourceList;

        public IRGPass()
        {
            colorBufferMaxIndex = -1;
            colorBuffers = new RGTextureRef[8];
            colorBufferActions = new RGAttachmentAction[8];
            colorBufferAccessFlags = new EAccessFlag[8];
            inputAttachments = new List<RGTextureRef>();
            inputAttachmentAccess = new List<EAccessFlag>();
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

        /// <summary>
        /// 新的基于访问意图的颜色附件设置函数。
        /// 根据访问标志自动推导Load/Store Action，实现更好的性能优化。
        /// </summary>
        /// <param name="resource">要设置的颜色附件纹理</param>
        /// <param name="index">MRT索引</param>
        /// <param name="access">访问意图标志</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColorAttachment(in RGTextureRef resource, in int index, in EAccessFlag access)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            colorBufferAccessFlags[index] = access;
            
            // 根据访问标志添加相应的资源依赖
            switch (access)
            {
                case EAccessFlag.Read:
                    AddResourceRead(resource.handle);
                    break;
                case EAccessFlag.Write:
                case EAccessFlag.WriteAll:
                case EAccessFlag.Discard:
                    AddResourceWrite(resource.handle);
                    break;
                case EAccessFlag.ReadWrite:
                    AddResourceRead(resource.handle);
                    AddResourceWrite(resource.handle);
                    break;
            }
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

        /// <summary>
        /// 新的基于访问意图的深度附件设置函数。
        /// 使用统一的EAccessFlag替代EDepthAccess，保持API一致性。
        /// </summary>
        /// <param name="resource">要设置的深度附件纹理</param>
        /// <param name="access">访问意图标志</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthAttachment(in RGTextureRef resource, in EAccessFlag access)
        {
            depthBuffer = resource;
            depthBufferAccessFlag = access;
            
            // 根据访问标志转换为EDepthAccess并添加资源依赖
            switch (access)
            {
                case EAccessFlag.Read:
                    depthBufferAccess = EDepthAccess.ReadOnly;
                    AddResourceRead(resource.handle);
                    break;
                case EAccessFlag.Write:
                case EAccessFlag.WriteAll:
                case EAccessFlag.Discard:
                    depthBufferAccess = EDepthAccess.Write;
                    AddResourceWrite(resource.handle);
                    break;
                case EAccessFlag.ReadWrite:
                    depthBufferAccess = EDepthAccess.ReadOnly | EDepthAccess.Write;
                    AddResourceRead(resource.handle);
                    AddResourceWrite(resource.handle);
                    break;
            }
        }

        /// <summary>
        /// 新增：设置输入附件，用于支持Subpass合并优化。
        /// 输入附件允许在同一个RenderPass内的不同Subpass之间直接传递数据，避免带宽消耗。
        /// </summary>
        /// <param name="input">输入附件纹理</param>
        /// <param name="access">访问标志，通常为Read</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInputAttachment(in RGTextureRef input, in EAccessFlag access = EAccessFlag.Read)
        {
            inputAttachments.Add(input);
            inputAttachmentAccess.Add(access);
            AddResourceRead(input.handle);
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

        /// <summary>
        /// 设置是否允许当前Pass与其他Pass进行合并优化。
        /// 当启用时，RG编译器会尝试将具有兼容Render Target配置的连续Pass合并，以减少带宽消耗。
        /// </summary>
        /// <param name="allow">是否允许Pass合并</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AllowPassMerge(in bool allow)
        {
            allowPassMerge = allow;
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
            allowPassMerge = true;  // 默认允许Pass合并

            // Invalidate everything
            colorBufferMaxIndex = -1;
            depthBuffer = new RGTextureRef();
            depthBufferAccess = EDepthAccess.Write;
            depthBufferAction = new RGAttachmentAction();
            depthBufferAccessFlag = EAccessFlag.Write;
            
            // Clear input attachments
            inputAttachments.Clear();
            inputAttachmentAccess.Clear();
            
            for (int i = 0; i < 8; ++i)
            {
                colorBuffers[i] = new RGTextureRef();
                colorBufferActions[i] = new RGAttachmentAction();
                colorBufferAccessFlags[i] = EAccessFlag.Write;
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

        /// <summary>
        /// 设置是否允许当前Pass与其他Pass进行合并优化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AllowPassMerge(in bool allow)
        {
            m_RasterPass.AllowPassMerge(allow);
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

        /// <summary>
        /// 新的基于访问意图的颜色附件设置 - 推荐使用
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef SetColorAttachment(in RGTextureRef renderTarget, int index, in EAccessFlag access)
        {
            m_RasterPass.SetColorAttachment(renderTarget, index, access);
            return renderTarget;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef SetDepthStencilAttachment(in RGTextureRef input, in RenderBufferLoadAction loadAction, in RenderBufferStoreAction storeAction, in EDepthAccess flags)
        {
            m_RasterPass.SetDepthStencilAttachment(input, loadAction, storeAction, flags);
            return input;
        }

        /// <summary>
        /// 新的基于访问意图的深度附件设置 - 推荐使用
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef SetDepthAttachment(in RGTextureRef depthTarget, in EAccessFlag access)
        {
            m_RasterPass.SetDepthAttachment(depthTarget, access);
            return depthTarget;
        }

        /// <summary>
        /// 设置输入附件，用于Subpass合并优化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef SetInputAttachment(in RGTextureRef input, in EAccessFlag access = EAccessFlag.Read)
        {
            m_RasterPass.SetInputAttachment(input, access);
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