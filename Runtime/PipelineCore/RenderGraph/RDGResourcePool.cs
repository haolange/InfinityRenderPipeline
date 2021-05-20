using UnityEngine;
using System.Collections.Generic;

namespace InfinityTech.Rendering.RDG
{
    abstract class RDGResourcePool<Type> where Type : class
    {
        protected static int s_CurrentFrameIndex;

        protected Dictionary<int, List<(Type resource, int frameIndex)>> m_ResourcePool = new Dictionary<int, List<(Type resource, int frameIndex)>>();

        abstract protected void ReleaseInternalResource(Type res);
        abstract protected string GetResourceName(Type res);
        abstract protected string GetResourceTypeName();

        public bool Request(int hashCode, out Type resource)
        {
            if (m_ResourcePool.TryGetValue(hashCode, out var list) && list.Count > 0)
            {
                resource = list[list.Count - 1].resource;
                list.RemoveAt(list.Count - 1); 
                return true;
            }

            resource = null;
            return false;
        }

        public void Release(int hash, Type resource, int currentFrameIndex)
        {
            if (!m_ResourcePool.TryGetValue(hash, out var list))
            {
                list = new List<(Type resource, int frameIndex)>();
                m_ResourcePool.Add(hash, list);
            }

            list.Add((resource, currentFrameIndex));
        }

        abstract public void CullingUnusedResources(int currentFrameIndex);

        public void Cleanup()
        {
            foreach (var kvp in m_ResourcePool)
            {
                foreach (var res in kvp.Value)
                {
                    ReleaseInternalResource(res.resource);
                }
            }
        }
    }

    class FRGTexturePool : RDGResourcePool<RenderTexture>
    {
        protected override void ReleaseInternalResource(RenderTexture res)
        {
            res.Release();
        }

        protected override string GetResourceName(RenderTexture res)
        {
            return res.name;
        }

        override protected string GetResourceTypeName()
        {
            return "Texture";
        }

        override public void CullingUnusedResources(int currentFrameIndex)
        {
            s_CurrentFrameIndex = currentFrameIndex;

            foreach (var kvp in m_ResourcePool)
            {
                var list = kvp.Value;
                list.RemoveAll(obj =>
                {
                    if (obj.frameIndex < s_CurrentFrameIndex)
                    {
                        obj.resource.Release();
                        return true;
                    }
                    return false;
                });
            }
        }
    }

    class FRGBufferPool : RDGResourcePool<ComputeBuffer>
    {
        protected override void ReleaseInternalResource(ComputeBuffer res)
        {
            res.Release();
        }

        protected override string GetResourceName(ComputeBuffer res)
        {
            return "BufferNameNotAvailable"; 
        }

        override protected string GetResourceTypeName()
        {
            return "Buffer";
        }

        override public void CullingUnusedResources(int currentFrameIndex)
        {
            s_CurrentFrameIndex = currentFrameIndex;

            foreach (var kvp in m_ResourcePool)
            {
                var list = kvp.Value;
                list.RemoveAll(obj =>
                {
                    if (obj.frameIndex < s_CurrentFrameIndex)
                    {
                        obj.resource.Release();
                        return true;
                    }
                    return false;
                });
            }
        }

    }
}
