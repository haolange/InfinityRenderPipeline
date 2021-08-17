using UnityEngine;
using System.Collections.Generic;

namespace InfinityTech.Rendering.RDG
{
    abstract class RDGResourcePool<Type> where Type : class
    {
        protected Dictionary<int, List<Type>> m_ResourcePool = new Dictionary<int, List<Type>>(64);
        abstract protected void ReleaseInternalResource(Type res);
        abstract protected string GetResourceName(Type res);
        abstract protected string GetResourceTypeName();

        public bool Request(in int hashCode, out Type resource)
        {
            if (m_ResourcePool.TryGetValue(hashCode, out var list) && list.Count > 0)
            {
                /*resource = list[0];
                list.RemoveAt(0);*/
                resource = list[list.Count - 1];
                list.RemoveAt(list.Count - 1); 
                return true;
            }

            resource = null;
            return false;
        }

        public void Release(in int hash, Type resource)
        {
            if (!m_ResourcePool.TryGetValue(hash, out var list))
            {
                list = new List<Type>();
                m_ResourcePool.Add(hash, list);
            }

            list.Add(resource);
        }

        public void Cleanup()
        {
            foreach (var kvp in m_ResourcePool)
            {
                foreach (var resource in kvp.Value)
                {
                    ReleaseInternalResource(resource);
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

        protected override string GetResourceTypeName()
        {
            return "Texture";
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

        protected override string GetResourceTypeName()
        {
            return "Buffer";
        }
    }
}
