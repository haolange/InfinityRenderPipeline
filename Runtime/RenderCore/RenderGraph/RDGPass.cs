using System;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal abstract class IRDGPass
    {
        public int index;
        public string name;
        public ProfilingSampler customSampler;
        public int refCount { get; protected set; }
        internal virtual bool hasExecuteFunc => false;
        public int colorBufferMaxIndex { get; protected set; }
        public bool enablePassCulling { get; protected set; }
        public bool enableAsyncCompute { get; protected set; }
        public FRDGTextureRef depthBuffer { get; protected set; }
        public FRDGTextureRef[]  colorBuffers { get; protected set; } = new FRDGTextureRef[8];
        public List<FRDGResourceHandle>[] resourceReadLists = new List<FRDGResourceHandle>[2];
        public List<FRDGResourceHandle>[] resourceWriteLists = new List<FRDGResourceHandle>[2];
        public List<FRDGResourceHandle>[] temporalResourceList = new List<FRDGResourceHandle>[2];

        public IRDGPass()
        {
            colorBufferMaxIndex = -1;

            for (int i = 0; i < 2; ++i)
            {
                resourceReadLists[i] = new List<FRDGResourceHandle>();
                resourceWriteLists[i] = new List<FRDGResourceHandle>();
                temporalResourceList[i] = new List<FRDGResourceHandle>();
            }
        }

        public abstract void Execute(in FRDGContext graphContext);
        public abstract void Release(FRDGObjectPool objectPool);

        public void AddResourceRead(in FRDGResourceHandle handle)
        {
            resourceReadLists[handle.iType].Add(handle);
        }

        public void AddResourceWrite(in FRDGResourceHandle handle)
        {
            resourceWriteLists[handle.iType].Add(handle);
        }

        public void AddTemporalResource(in FRDGResourceHandle handle)
        {
            temporalResourceList[handle.iType].Add(handle);
        }

        public void SetColorBuffer(in FRDGTextureRef resource, in int index)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            AddResourceWrite(resource.handle);
        }

        public void SetDepthBuffer(in FRDGTextureRef resource, in EDepthAccess flags)
        {
            depthBuffer = resource;
            if ((flags & EDepthAccess.Read) != 0)
                AddResourceRead(resource.handle);
            if ((flags & EDepthAccess.Write) != 0)
                AddResourceWrite(resource.handle);
        }

        public void EnablePassCulling(in bool value)
        {
            enablePassCulling = value;
        }

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
        IRDGPass m_RenderPass;
        FRDGResourceFactory m_Resources;

        internal FRDGPassRef(IRDGPass renderPass, FRDGResourceFactory resources)
        {
            m_Disposed = false;
            m_Resources = resources;
            m_RenderPass = renderPass;
        }

        public void EnablePassCulling(in bool value)
        {
            m_RenderPass.EnablePassCulling(value);
        }

        public void EnableAsyncCompute(in bool value)
        {
            m_RenderPass.EnableAsyncCompute(value);
        }

        public FRDGTextureRef ReadTexture(in FRDGTextureRef input)
        {
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        public FRDGTextureRef WriteTexture(in FRDGTextureRef input)
        {
            m_RenderPass.AddResourceWrite(input.handle);
            return input;
        }

        public FRDGTextureRef UseColorBuffer(in FRDGTextureRef input, int index)
        {
            m_RenderPass.SetColorBuffer(input, index);
            return input;
        }

        public FRDGTextureRef UseDepthBuffer(in FRDGTextureRef input, in EDepthAccess flags)
        {
            m_RenderPass.SetDepthBuffer(input, flags);
            return input;
        }

        public FRDGTextureRef CreateTemporaryTexture(in FTextureDescription description)
        {
            var result = m_Resources.CreateTexture(description, 0, m_RenderPass.index);
            m_RenderPass.AddTemporalResource(result.handle);
            return result;
        }

        public FRDGBufferRef ReadBuffer(in FRDGBufferRef bufferRef)
        {
            m_RenderPass.AddResourceRead(bufferRef.handle);
            return bufferRef;
        }

        public FRDGBufferRef WriteBuffer(in FRDGBufferRef bufferRef)
        {
            m_RenderPass.AddResourceWrite(bufferRef.handle);
            return bufferRef;
        }

        public FRDGBufferRef CreateTemporaryBuffer(in FBufferDescription description)
        {
            FRDGBufferRef bufferRef = m_Resources.CreateBuffer(description, m_RenderPass.index);
            m_RenderPass.AddTemporalResource(bufferRef.handle);
            return bufferRef;
        }

        public ref T GetPassData<T>() where T : struct
        {
            return ref ((FRDGPass<T>)m_RenderPass).passData;
        }

        public void SetExecuteFunc<T>(FExecuteAction<T> ExcuteFunc) where T : struct
        {
            ((FRDGPass<T>)m_RenderPass).ExcuteFunc = ExcuteFunc;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
        }
    }
}