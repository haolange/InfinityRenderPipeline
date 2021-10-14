using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal enum ERGProfileId
    {
        ViewContext,
        ComputeLOD,
        CulllingScene,
        BeginFrameRendering,
        EndFrameRendering,
        SceneRendering,
        CameraRendering
    }

    class RDGResourceFactory
    {
        static RDGResourceFactory m_CurrentRegistry;
        internal static RDGResourceFactory current
        {
            get {
                return m_CurrentRegistry;
            } set {
                m_CurrentRegistry = value; 
            }
        }

        FBufferCache m_BufferPool = new FBufferCache();
        FTextureCache m_TexturePool = new FTextureCache();
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

        internal void BeginRender()
        {
            current = this;
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

        internal RDGBufferRef ImportBuffer(ComputeBuffer computeBuffer)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out RDGBuffer bufferResource);
            bufferResource.resource = computeBuffer;
            bufferResource.imported = true;

            return new RDGBufferRef(newHandle);
        }

        internal RDGBufferRef CreateBuffer(in BufferDescription desc, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out RDGBuffer bufferResource);
            bufferResource.desc = desc;
            bufferResource.temporalPassIndex = temporalPassIndex;

            return new RDGBufferRef(newHandle);
        }

        internal int GetBufferResourceCount()
        {
            return m_Resources[(int)ERDGResourceType.Buffer].size;
        }

        RDGBuffer GetBufferResource(in RDGResourceRef handle)
        {
            return m_Resources[(int)ERDGResourceType.Buffer][handle] as RDGBuffer;
        }

        internal BufferDescription GetBufferResourceDesc(in RDGResourceRef handle)
        {
            return (m_Resources[(int)ERDGResourceType.Buffer][handle] as RDGBuffer).desc;
        }

        internal RDGTextureRef ImportTexture(RTHandle rt, int shaderProperty = 0)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out RDGTexture texResource);
            texResource.resource = rt;
            texResource.imported = true;
            texResource.shaderProperty = shaderProperty;

            return new RDGTextureRef(newHandle);
        }

        internal RDGTextureRef CreateTexture(in TextureDescription desc, int shaderProperty = 0, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out RDGTexture texResource);
            texResource.desc = desc;
            texResource.shaderProperty = shaderProperty;
            texResource.temporalPassIndex = temporalPassIndex;
            return new RDGTextureRef(newHandle);
        }

        internal int GetTextureResourceCount()
        {
            return m_Resources[(int)ERDGResourceType.Texture].size;
        }

        RDGTexture GetTextureResource(in RDGResourceRef handle)
        {
            return m_Resources[(int)ERDGResourceType.Texture][handle] as RDGTexture;
        }

        internal TextureDescription GetTextureResourceDesc(in RDGResourceRef handle)
        {
            return (m_Resources[(int)ERDGResourceType.Texture][handle] as RDGTexture).desc;
        }

        internal void CreateRealBuffer(int index)
        {
            RDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as RDGBuffer;
            if (!resource.imported)
            {
                var desc = resource.desc;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created Compute Buffer ({0}). Buffer was probably declared for writing more than once in the same pass.", resource.desc.name));

                resource.resource = null;
                if (!m_BufferPool.Pull(hashCode, out resource.resource))
                {
                    resource.resource = new ComputeBuffer(resource.desc.count, resource.desc.stride, resource.desc.type);
                }
                resource.cachedHash = hashCode;
            }
        }

        internal void ReleaseRealBuffer(int index)
        {
            RDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as RDGBuffer;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a compute buffer ({resource.desc.name}) that was never created. Check that there is at least one pass writing to it first.");

                m_BufferPool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        internal void CreateRealTexture(ref RDGContext graphContext, int index)
        {
            RDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as RDGTexture;

            if (!resource.imported)
            {
                var desc = resource.desc;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created texture ({0}). Texture was probably declared for writing more than once in the same pass.", resource.desc.name));

                resource.resource = null;
                if (!m_TexturePool.Pull(hashCode, out resource.resource)) 
                {
                    resource.resource = RTHandles.Alloc(desc.width, desc.height, desc.slices, (DepthBits)desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                    desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, (MSAASamples)desc.msaaSamples, desc.bindTextureMS, false, RenderTextureMemoryless.None, desc.name);
                }

                resource.cachedHash = hashCode;

                if (resource.desc.clearBuffer)
                {
                    bool debugClear = !resource.desc.clearBuffer;
                    var clearFlag = resource.desc.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    var clearColor = debugClear ? Color.magenta : resource.desc.clearColor;
                    CoreUtils.SetRenderTarget(graphContext.cmdBuffer, resource.resource, clearFlag, clearColor);
                }
            }
        }

        internal void ReleaseRealTexture(int index)
        {
            RDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as RDGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.desc.name}) that was never created. Check that there is at least one pass writing to it first.");

                /*using (new ProfilingScope(rgContext.CmdBuffer, ProfilingSampler.Get(ERGProfileId.RenderGraphClearDebug)))
                {
                    var clearFlag = resource.desc.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    CoreUtils.SetRenderTarget(rgContext.CmdBuffer, GetTexture(new RDGTextureHandle(index)), clearFlag, Color.magenta);
                }*/

                m_TexturePool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        internal void SetGlobalTextures(ref RDGContext graphContext, List<RDGResourceRef> textures)
        {
            foreach (var resource in textures)
            {
                RDGTexture Texture = GetTextureResource(resource);
                if (Texture.shaderProperty != 0)
                {
                    if (Texture.resource != null)
                    {
                        graphContext.cmdBuffer.SetGlobalTexture(Texture.shaderProperty, Texture.resource);
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

        internal void Dispose()
        {
            m_BufferPool.Dispose();
            m_TexturePool.Dispose();
        }

        #endregion
    }
}
