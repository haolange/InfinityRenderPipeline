using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace InfinityTech.Runtime.Rendering.RDG
{
    internal enum ERGProfileId
    {
        GraphBuilderClear,
        GraphBuilderBind,
        HDRenderPipeline
    }

    class RDGResourceFactory
    {
        static readonly ShaderTagId s_EmptyName = new ShaderTagId("");

        static RDGResourceFactory m_CurrentRegistry;
        internal static RDGResourceFactory current
        {
            get {
                return m_CurrentRegistry;
            } set {
                m_CurrentRegistry = value; 
            }
        }

        int m_CurrentFrameIndex;
        FRGTexturePool m_TexturePool = new FRGTexturePool();
        FRGBufferPool m_BufferPool = new FRGBufferPool();
        DynamicArray<IRDGResource>[] m_Resources = new DynamicArray<IRDGResource>[2];


        internal ComputeBuffer GetBuffer(in RDGBufferRef handle)
        {
            return GetBufferResource(handle.handle).resource;
        }

        internal RenderTexture GetTexture(in RDGTextureRef handle)
        {
            return GetTextureResource(handle.handle).resource;
        }

        #region Internal Interface
        internal RDGResourceFactory()
        {
            for (int i = 0; i < 2; ++i)
                m_Resources[i] = new DynamicArray<IRDGResource>();
        }

        ResType GetResource<DescType, ResType>(DynamicArray<IRDGResource> resourceArray, int index) where DescType : struct where ResType : class
        {
            var res = resourceArray[index] as RDGResource<DescType, ResType>;
            return res.resource;
        }

        internal void BeginRender(int InFrameIndex)
        {
            current = this;
            m_CurrentFrameIndex = InFrameIndex;
        }

        internal void EndRender()
        {
            current = null;
        }

        internal string GetResourceName(in RDGResourceRef res)
        {
            return m_Resources[res.iType][res.index].GetName();
        }

        internal bool IsResourceImported(in RDGResourceRef res)
        {
            return m_Resources[res.iType][res.index].imported;
        }

        internal int GetResourceTemporalIndex(in RDGResourceRef res)
        {
            return m_Resources[res.iType][res.index].temporalPassIndex;
        }

        int AddNewResource<ResType>(DynamicArray<IRDGResource> resourceArray, out ResType outRes) where ResType : IRDGResource, new()
        {
            int result = resourceArray.size;
            resourceArray.Resize(resourceArray.size + 1, true);
            if (resourceArray[result] == null)
                resourceArray[result] = new ResType();

            outRes = resourceArray[result] as ResType;
            outRes.Reset();
            return result;
        }

        internal RDGTextureRef ImportTexture(RenderTexture rt, int shaderProperty = 0)
        {
            int newHandle = AddNewResource(m_Resources[(int)RDGResourceType.Texture], out RDGTexture texResource);
            texResource.resource = rt;
            texResource.imported = true;
            texResource.shaderProperty = shaderProperty;

            return new RDGTextureRef(newHandle);
        }

        internal RDGTextureRef CreateTexture(in RDGTextureDesc desc, int shaderProperty = 0, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)RDGResourceType.Texture], out RDGTexture texResource);
            texResource.desc = desc;
            texResource.shaderProperty = shaderProperty;
            texResource.temporalPassIndex = temporalPassIndex;
            return new RDGTextureRef(newHandle);
        }

        internal int GetTextureResourceCount()
        {
            return m_Resources[(int)RDGResourceType.Texture].size;
        }

        RDGTexture GetTextureResource(in RDGResourceRef handle)
        {
            return m_Resources[(int)RDGResourceType.Texture][handle] as RDGTexture;
        }

        internal RDGTextureDesc GetTextureResourceDesc(in RDGResourceRef handle)
        {
            return (m_Resources[(int)RDGResourceType.Texture][handle] as RDGTexture).desc;
        }

        internal RDGBufferRef ImportBuffer(ComputeBuffer computeBuffer)
        {
            int newHandle = AddNewResource(m_Resources[(int)RDGResourceType.Buffer], out RDGBuffer bufferResource);
            bufferResource.resource = computeBuffer;
            bufferResource.imported = true;

            return new RDGBufferRef(newHandle);
        }

        internal RDGBufferRef CreateBuffer(in RDGBufferDesc desc, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)RDGResourceType.Buffer], out RDGBuffer bufferResource);
            bufferResource.desc = desc;
            bufferResource.temporalPassIndex = temporalPassIndex;

            return new RDGBufferRef(newHandle);
        }

        internal int GetBufferResourceCount()
        {
            return m_Resources[(int)RDGResourceType.Buffer].size;
        }

        RDGBuffer GetBufferResource(in RDGResourceRef handle)
        {
            return m_Resources[(int)RDGResourceType.Buffer][handle] as RDGBuffer;
        }

        internal RDGBufferDesc GetBufferResourceDesc(in RDGResourceRef handle)
        {
            return (m_Resources[(int)RDGResourceType.Buffer][handle] as RDGBuffer).desc;
        }


        internal void CreateRealBuffer(int index)
        {
            var resource = m_Resources[(int)RDGResourceType.Buffer][index] as RDGBuffer;
            if (!resource.imported)
            {
                var desc = resource.desc;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created Compute Buffer ({0}). Buffer was probably declared for writing more than once in the same pass.", resource.desc.name));

                resource.resource = null;
                if (!m_BufferPool.Request(hashCode, out resource.resource))
                {
                    resource.resource = new ComputeBuffer(resource.desc.count, resource.desc.stride, resource.desc.type);
                }
                resource.cachedHash = hashCode;
            }
        }

        internal void ReleaseRealBuffer(int index)
        {
            var resource = m_Resources[(int)RDGResourceType.Buffer][index] as RDGBuffer;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a compute buffer ({resource.desc.name}) that was never created. Check that there is at least one pass writing to it first.");

                m_BufferPool.Release(resource.cachedHash, resource.resource, m_CurrentFrameIndex);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        internal void CreateRealTexture(RDGContext rgContext, int index)
        {
            var resource = m_Resources[(int)RDGResourceType.Texture][index] as RDGTexture;

            if (!resource.imported)
            {
                var desc = resource.desc;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created texture ({0}). Texture was probably declared for writing more than once in the same pass.", resource.desc.name));

                resource.resource = null;

                bool IsCache = m_TexturePool.Request(hashCode, out resource.resource);
                if (IsCache == false) {
                    resource.resource = RTHandles.Alloc(desc.width, desc.height, desc.slices, (DepthBits)desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                    desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, (MSAASamples)desc.msaaSamples, desc.bindTextureMS, false, RenderTextureMemoryless.None, desc.name);
                }

                resource.cachedHash = hashCode;

                if (resource.desc.clearBuffer)
                {
                    bool debugClear = !resource.desc.clearBuffer;
                    using (new ProfilingScope(rgContext.CmdBuffer, ProfilingSampler.Get(ERGProfileId.GraphBuilderClear)))
                    {
                        var clearFlag = resource.desc.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                        var clearColor = debugClear ? Color.magenta : resource.desc.clearColor;
                        CoreUtils.SetRenderTarget(rgContext.CmdBuffer, resource.resource, clearFlag, clearColor);
                    }
                }
            }
        }

        internal void ReleaseRealTexture(int index)
        {
            var resource = m_Resources[(int)RDGResourceType.Texture][index] as RDGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.desc.name}) that was never created. Check that there is at least one pass writing to it first.");

                /*using (new ProfilingScope(rgContext.CmdBuffer, ProfilingSampler.Get(ERGProfileId.RenderGraphClearDebug)))
                {
                    var clearFlag = resource.desc.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    CoreUtils.SetRenderTarget(rgContext.CmdBuffer, GetTexture(new RDGTextureHandle(index)), clearFlag, Color.magenta);
                }*/

                m_TexturePool.Release(resource.cachedHash, resource.resource, m_CurrentFrameIndex);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        internal void SetGlobalTextures(RDGContext rgContext, List<RDGResourceRef> textures)
        {
            foreach (var resource in textures)
            {
                RDGTexture Texture = GetTextureResource(resource);
                if (Texture.shaderProperty != 0)
                {
                    if (Texture.resource != null)
                    {
                        rgContext.CmdBuffer.SetGlobalTexture(Texture.shaderProperty, Texture.resource);
                    }
                }
            }
        }

        internal void Clear()
        {
            for (int i = 0; i < 2; ++i) { 
                m_Resources[i].Clear();
            }
        }

        internal void CullingUnusedResources()
        {
            m_BufferPool.CullingUnusedResources(m_CurrentFrameIndex);
            m_TexturePool.CullingUnusedResources(m_CurrentFrameIndex);
        }

        internal void Cleanup()
        {
            m_BufferPool.Cleanup();
            m_TexturePool.Cleanup();
        }

        #endregion
    }
}
