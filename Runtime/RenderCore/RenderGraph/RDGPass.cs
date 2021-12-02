using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal struct FRDGPassOption
    {
        public bool IsActive;
        public bool IsClearDepth;
        public bool IsClearColor;
        public Color clearColor;

        public RenderBufferLoadAction colorLoadAction;
        public RenderBufferStoreAction colorStoreAction;

        public RenderBufferLoadAction depthLoadAction;
        public RenderBufferStoreAction depthStoreAction;
    }

    internal abstract class IRDGPass
    {
        public int index;
        public string name;
        public FRDGPassOption passOption;
        public ProfilingSampler customSampler;
        public int refCount { get; protected set; }
        internal virtual bool hasExecuteFunc => false;
        public int colorBufferMaxIndex { get; protected set; }
        public bool enablePassCulling { get; protected set; }
        public bool enableAsyncCompute { get; protected set; }
        public FRDGTextureRef depthBuffer { get; protected set; }
        public FRDGTextureRef[] colorBuffers { get; protected set; }
        public List<FRDGResourceHandle>[] resourceReadLists;
        public List<FRDGResourceHandle>[] resourceWriteLists;
        public List<FRDGResourceHandle>[] temporalResourceList;

        public IRDGPass()
        {
            colorBufferMaxIndex = -1;
            colorBuffers = new FRDGTextureRef[8];
            resourceReadLists = new List<FRDGResourceHandle>[2];
            resourceWriteLists = new List<FRDGResourceHandle>[2];
            temporalResourceList = new List<FRDGResourceHandle>[2];

            for (int i = 0; i < 2; ++i)
            {
                resourceReadLists[i] = new List<FRDGResourceHandle>();
                resourceWriteLists[i] = new List<FRDGResourceHandle>();
                temporalResourceList[i] = new List<FRDGResourceHandle>();
            }
        }

        public abstract void Execute(in FRDGContext graphContext);
        public abstract void Release(FRDGObjectPool objectPool);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResourceRead(in FRDGResourceHandle handle)
        {
            resourceReadLists[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResourceWrite(in FRDGResourceHandle handle)
        {
            resourceWriteLists[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTemporalResource(in FRDGResourceHandle handle)
        {
            temporalResourceList[handle.iType].Add(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColorBuffer(in FRDGTextureRef resource, in int index)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            AddResourceWrite(resource.handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDepthBuffer(in FRDGTextureRef resource, in EDepthAccess flags)
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
        public ref FRDGPassOption GetPassOption()
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
            depthBuffer = new FRDGTextureRef();
            for (int i = 0; i < 8; ++i)
            {
                colorBuffers[i] = new FRDGTextureRef();
            }
        }
    }

    public delegate void FExecuteAction<T>(in T passData, in FRDGContext graphContext) where T : struct;

    internal sealed class FRDGPass<T> : IRDGPass where T : struct
    {
        internal T passData;
        internal FExecuteAction<T> ExcuteFunc;
        internal override bool hasExecuteFunc { get { return ExcuteFunc != null; } }

        public override void Execute(in FRDGContext graphContext)
        {
            ExcuteFunc(in passData, in graphContext);
        }

        public override void Release(FRDGObjectPool objectPool)
        {
            Clear();
            ExcuteFunc = null;
            objectPool.Release(this);
        }
    }

    public struct FRDGPassRef : IDisposable
    {
        bool m_Disposed;
        IRDGPass m_Pass;
        FRDGResourceFactory m_Resources;

        internal FRDGPassRef(IRDGPass pass, FRDGResourceFactory resources)
        {
            m_Pass = pass;
            m_Disposed = false;
            m_Resources = resources;

            ref FRDGPassOption passOption = ref m_Pass.GetPassOption();
            passOption.IsActive = false;
            passOption.clearColor = Color.black;
            passOption.IsClearDepth = false;
            passOption.IsClearColor = false;
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
        public void SetOption(in bool clearDetph, in bool clearColor, in Color color, in RenderBufferLoadAction colorLoadAction = RenderBufferLoadAction.DontCare, in RenderBufferStoreAction colorStoreAction = RenderBufferStoreAction.Store, in RenderBufferLoadAction depthLoadAction = RenderBufferLoadAction.DontCare, in RenderBufferStoreAction depthStoreAction = RenderBufferStoreAction.Store)
        {
            ref FRDGPassOption passOption = ref m_Pass.GetPassOption();
            passOption.IsActive = true;
            passOption.clearColor = color;
            passOption.IsClearDepth = clearDetph;
            passOption.IsClearColor = clearDetph;
            passOption.colorLoadAction = colorLoadAction;
            passOption.depthLoadAction = depthLoadAction;
            passOption.colorStoreAction = colorStoreAction;
            passOption.depthStoreAction = depthStoreAction;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef ReadTexture(in FRDGTextureRef input)
        {
            m_Pass.AddResourceRead(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef WriteTexture(in FRDGTextureRef input)
        {
            m_Pass.AddResourceWrite(input.handle);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef UseColorBuffer(in FRDGTextureRef input, int index)
        {
            m_Pass.SetColorBuffer(input, index);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef UseDepthBuffer(in FRDGTextureRef input, in EDepthAccess flags)
        {
            m_Pass.SetDepthBuffer(input, flags);
            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef CreateTemporaryTexture(in FTextureDescriptor descriptor)
        {
            var result = m_Resources.CreateTexture(descriptor, 0, m_Pass.index);
            m_Pass.AddTemporalResource(result.handle);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef ReadBuffer(in FRDGBufferRef bufferRef)
        {
            m_Pass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef WriteBuffer(in FRDGBufferRef bufferRef)
        {
            m_Pass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef CreateTemporaryBuffer(in FBufferDescriptor descriptor)
        {
            FRDGBufferRef bufferRef = m_Resources.CreateBuffer(descriptor, m_Pass.index);
            m_Pass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetPassData<T>() where T : struct
        {
            return ref ((FRDGPass<T>)m_Pass).passData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExecuteFunc<T>(FExecuteAction<T> ExcuteFunc) where T : struct
        {
            ((FRDGPass<T>)m_Pass).ExcuteFunc = ExcuteFunc;
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