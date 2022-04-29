using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal struct RDGPassOption
    {
        public bool IsActive;
        public ClearFlag clearFlag;

        public RenderBufferLoadAction colorLoadAction;
        public RenderBufferStoreAction colorStoreAction;

        public RenderBufferLoadAction depthLoadAction;
        public RenderBufferStoreAction depthStoreAction;
    }

    internal abstract class IRDGPass
    {
        public int index;
        public string name;
        public RDGPassOption passOption;
        public ProfilingSampler customSampler;
        public int refCount { get; protected set; }
        internal virtual bool hasExecuteFunc => false;
        public int colorBufferMaxIndex { get; protected set; }
        public bool enablePassCulling { get; protected set; }
        public bool enableAsyncCompute { get; protected set; }
        public RDGTextureRef depthBuffer { get; protected set; }
        public RDGTextureRef[] colorBuffers { get; protected set; }
        public List<RDGResourceHandle>[] resourceReadLists;
        public List<RDGResourceHandle>[] resourceWriteLists;
        public List<RDGResourceHandle>[] temporalResourceList;

        public IRDGPass()
        {
            colorBufferMaxIndex = -1;
            colorBuffers = new RDGTextureRef[8];
            resourceReadLists = new List<RDGResourceHandle>[2];
            resourceWriteLists = new List<RDGResourceHandle>[2];
            temporalResourceList = new List<RDGResourceHandle>[2];

            for (int i = 0; i < 2; ++i)
            {
                resourceReadLists[i] = new List<RDGResourceHandle>();
                resourceWriteLists[i] = new List<RDGResourceHandle>();
                temporalResourceList[i] = new List<RDGResourceHandle>();
            }
        }

        public abstract void Execute(in RDGContext graphContext);
        public abstract void Release(RDGObjectPool objectPool);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResourceRead(in RDGResourceHandle handle)
        {
            resourceReadLists[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResourceWrite(in RDGResourceHandle handle)
        {
            resourceWriteLists[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTemporalResource(in RDGResourceHandle handle)
        {
            temporalResourceList[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColorBuffer(in RDGTextureRef resource, in int index)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            AddResourceWrite(resource.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthBuffer(in RDGTextureRef resource, in EDepthAccess flags)
        {
            depthBuffer = resource;
            if ((flags & EDepthAccess.Read) != 0) {
                AddResourceRead(resource.handle);
            }
                
            if ((flags & EDepthAccess.Write) != 0) {
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref RDGPassOption GetPassOption()
        {
            return ref passOption;
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
            depthBuffer = new RDGTextureRef();
            for (int i = 0; i < 8; ++i)
            {
                colorBuffers[i] = new RDGTextureRef();
            }
        }
    }

    public delegate void FExecuteAction<T>(in T passData, in RDGContext graphContext) where T : struct;

    internal sealed class RDGPass<T> : IRDGPass where T : struct
    {
        internal T passData;
        internal FExecuteAction<T> ExcuteFunc;
        internal override bool hasExecuteFunc { get { return ExcuteFunc != null; } }

        public override void Execute(in RDGContext graphContext)
        {
            ExcuteFunc(in passData, in graphContext);
        }

        public override void Release(RDGObjectPool objectPool)
        {
            Clear();
            ExcuteFunc = null;
            objectPool.Release(this);
        }
    }

    public struct RDGPassRef : IDisposable
    {
        bool m_Disposed;
        IRDGPass m_Pass;
        RDGResourceFactory m_Resources;

        internal RDGPassRef(IRDGPass pass, RDGResourceFactory resources)
        {
            m_Pass = pass;
            m_Disposed = false;
            m_Resources = resources;

            ref RDGPassOption passOption = ref m_Pass.GetPassOption();
            passOption.IsActive = false;
            passOption.clearFlag = ClearFlag.All;
            passOption.colorLoadAction = RenderBufferLoadAction.DontCare;
            passOption.depthLoadAction = RenderBufferLoadAction.DontCare;
            passOption.colorStoreAction = RenderBufferStoreAction.Store;
            passOption.depthStoreAction = RenderBufferStoreAction.Store;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnablePassCulling(in bool value)
        {
            m_Pass.EnablePassCulling(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnableAsyncCompute(in bool value)
        {
            m_Pass.EnableAsyncCompute(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOption(in ClearFlag clearFlag, in RenderBufferLoadAction colorLoadAction = RenderBufferLoadAction.DontCare, in RenderBufferStoreAction colorStoreAction = RenderBufferStoreAction.Store, in RenderBufferLoadAction depthLoadAction = RenderBufferLoadAction.DontCare, in RenderBufferStoreAction depthStoreAction = RenderBufferStoreAction.Store)
        {
            ref RDGPassOption passOption = ref m_Pass.GetPassOption();
            passOption.IsActive = true;
            passOption.clearFlag = clearFlag;
            passOption.colorLoadAction = colorLoadAction;
            passOption.depthLoadAction = depthLoadAction;
            passOption.colorStoreAction = colorStoreAction;
            passOption.depthStoreAction = depthStoreAction;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef ReadTexture(in RDGTextureRef input)
        {
            m_Pass.AddResourceRead(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef WriteTexture(in RDGTextureRef input)
        {
            m_Pass.AddResourceWrite(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef UseColorBuffer(in RDGTextureRef input, int index)
        {
            m_Pass.SetColorBuffer(input, index);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef UseDepthBuffer(in RDGTextureRef input, in EDepthAccess flags)
        {
            m_Pass.SetDepthBuffer(input, flags);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef CreateTemporaryTexture(in TextureDescriptor descriptor)
        {
            var result = m_Resources.CreateTexture(descriptor, 0, m_Pass.index);
            m_Pass.AddTemporalResource(result.handle);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGBufferRef ReadBuffer(in RDGBufferRef bufferRef)
        {
            m_Pass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGBufferRef WriteBuffer(in RDGBufferRef bufferRef)
        {
            m_Pass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGBufferRef CreateTemporaryBuffer(in BufferDescriptor descriptor)
        {
            RDGBufferRef bufferRef = m_Resources.CreateBuffer(descriptor, m_Pass.index);
            m_Pass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetPassData<T>() where T : struct
        {
            return ref ((RDGPass<T>)m_Pass).passData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExecuteFunc<T>(FExecuteAction<T> ExcuteFunc) where T : struct
        {
            ((RDGPass<T>)m_Pass).ExcuteFunc = ExcuteFunc;
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