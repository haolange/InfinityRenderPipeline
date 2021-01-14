using System;
using System.Collections.Generic;

namespace InfinityTech.Rendering.RDG
{
    abstract class IRDGRenderPass
    {
        public abstract void Step(ref RDGPassBuilder PassBuilder);
        public abstract void Execute(RDGContext GraphContext);
        public abstract void Release(RDGObjectPool ObjectPool);
        public abstract bool HasRenderFunc();

        public string name;
        public int index;
        public UnityEngine.Rendering.ProfilingSampler customSampler;
        public bool             enableAsyncCompute { get; protected set; }
        public bool             allowPassCulling { get; protected set; }

        public RDGTextureRef depthBuffer { get; protected set; }
        public RDGTextureRef[]  colorBuffers { get; protected set; } = new RDGTextureRef[8];
        public int              colorBufferMaxIndex { get; protected set; } = -1;
        public int              refCount { get; protected set; }

        public List<RDGResourceRef>[] resourceReadLists = new List<RDGResourceRef>[2];
        public List<RDGResourceRef>[] resourceWriteLists = new List<RDGResourceRef>[2];
        public List<RDGResourceRef>[] temporalResourceList = new List<RDGResourceRef>[2];


        public IRDGRenderPass()
        {
            for (int i = 0; i < 2; ++i)
            {
                resourceReadLists[i] = new List<RDGResourceRef>();
                resourceWriteLists[i] = new List<RDGResourceRef>();
                temporalResourceList[i] = new List<RDGResourceRef>();
            }
        }

        public void AddResourceWrite(in RDGResourceRef res)
        {
            resourceWriteLists[res.iType].Add(res);
        }

        public void AddResourceRead(in RDGResourceRef res)
        {
            resourceReadLists[res.iType].Add(res);
        }

        public void AddTemporalResource(in RDGResourceRef res)
        {
            temporalResourceList[res.iType].Add(res);
        }

        public void SetColorBuffer(RDGTextureRef resource, int index)
        {
            colorBufferMaxIndex = Math.Max(colorBufferMaxIndex, index);
            colorBuffers[index] = resource;
            AddResourceWrite(resource.handle);
        }

        public void SetDepthBuffer(RDGTextureRef resource, EDepthAccess flags)
        {
            depthBuffer = resource;
            if ((flags & EDepthAccess.Read) != 0)
                AddResourceRead(resource.handle);
            if ((flags & EDepthAccess.Write) != 0)
                AddResourceWrite(resource.handle);
        }

        public void EnableAsyncCompute(bool value)
        {
            enableAsyncCompute = value;
        }

        public void AllowPassCulling(bool value)
        {
            allowPassCulling = value;
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
            allowPassCulling = true;
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


    public delegate void StepAction<T>(ref T PassData, ref RDGPassBuilder PassBuilder) where T : struct;
    public delegate void ExecuteAction<T>(ref T PassData, RDGContext GraphContext) where T : struct;

    internal sealed class RDGRenderPass<T> : IRDGRenderPass where T : struct
    {
        internal T PassData;
        internal StepAction<T> StepFunc;
        internal ExecuteAction<T> ExecuteFunc;

        public override void Step(ref RDGPassBuilder PassBuilder)
        {
            StepFunc(ref PassData, ref PassBuilder);
        }

        public override void Execute(RDGContext GraphContext)
        {
            ExecuteFunc(ref PassData, GraphContext);
        }

        public override void Release(RDGObjectPool ObjectPool)
        {
            Clear();
            ExecuteFunc = null;
            ObjectPool.Release(this);
        }

        public override bool HasRenderFunc()
        {
            return ExecuteFunc != null;
        }
    }
}