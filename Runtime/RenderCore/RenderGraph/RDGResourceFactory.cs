using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal enum ERDGProfileId
    {
        ViewContext,
        ComputeLOD,
        CulllingScene,
        BeginFrameRendering,
        EndFrameRendering,
        SceneRendering,
        CameraRendering
    }

    internal class FRDGResourceFactory
    {
        static FRDGResourceFactory m_CurrentRegistry;
        internal static FRDGResourceFactory current
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

        public FRDGResourceFactory()
        {
            for (int i = 0; i < 2; ++i)
            {
                m_Resources[i] = new DynamicArray<IRDGResource>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComputeBuffer GetBuffer(in FRDGBufferRef bufferRef)
        {
            return GetBufferResource(bufferRef.handle).resource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RenderTexture GetTexture(in FRDGTextureRef textureRef)
        {
            return GetTextureResource(textureRef.handle).resource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BeginRender()
        {
            current = this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EndRender()
        {
            current = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string GetResourceName(in FRDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].GetName();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsResourceImported(in FRDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].imported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetResourceTemporalIndex(in FRDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].temporalPassIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int AddNewResource<ResType>(DynamicArray<IRDGResource> resourceArray, out ResType outRes) where ResType : IRDGResource, new()
        {
            int result = resourceArray.size;
            resourceArray.Resize(resourceArray.size + 1, true);
            if (resourceArray[result] == null)
            {
                resourceArray[result] = new ResType();
            }

            outRes = resourceArray[result] as ResType;
            outRes.Reset();
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGBufferRef ImportBuffer(ComputeBuffer computeBuffer)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out FRDGBuffer rdgBuffer);
            rdgBuffer.resource = computeBuffer;
            rdgBuffer.imported = true;

            return new FRDGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGBufferRef CreateBuffer(in FBufferDescription description, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out FRDGBuffer rdgBuffer);
            rdgBuffer.description = description;
            rdgBuffer.temporalPassIndex = temporalPassIndex;

            return new FRDGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetBufferCount()
        {
            return m_Resources[(int)ERDGResourceType.Buffer].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        FRDGBuffer GetBufferResource(in FRDGResourceHandle handle)
        {
            return m_Resources[(int)ERDGResourceType.Buffer][handle] as FRDGBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FBufferDescription GetBufferDescription(in FRDGResourceHandle handle)
        {
            return (m_Resources[(int)ERDGResourceType.Buffer][handle] as FRDGBuffer).description;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGTextureRef ImportTexture(RTHandle rt, in int shaderProperty = 0)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out FRDGTexture rdgTexture);
            rdgTexture.resource = rt;
            rdgTexture.imported = true;
            rdgTexture.shaderProperty = shaderProperty;

            return new FRDGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGTextureRef CreateTexture(in FTextureDescription description, in int shaderProperty = 0, in int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out FRDGTexture rdgTexture);
            rdgTexture.description = description;
            rdgTexture.shaderProperty = shaderProperty;
            rdgTexture.temporalPassIndex = temporalPassIndex;
            return new FRDGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetTextureCount()
        {
            return m_Resources[(int)ERDGResourceType.Texture].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        FRDGTexture GetTextureResource(in FRDGResourceHandle handle)
        {
            return m_Resources[(int)ERDGResourceType.Texture][handle] as FRDGTexture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FTextureDescription GetTextureDescription(in FRDGResourceHandle handle)
        {
            return (m_Resources[(int)ERDGResourceType.Texture][handle] as FRDGTexture).description;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateRealBuffer(int index)
        {
            FRDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as FRDGBuffer;

            if (!resource.imported)
            {
                var desc = resource.description;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created Compute Buffer ({0}). Buffer was probably declared for writing more than once in the same pass.", resource.description.name));

                resource.resource = null;
                if (!m_BufferPool.Pull(hashCode, out resource.resource))
                {
                    resource.resource = new ComputeBuffer(resource.description.count, resource.description.stride, resource.description.type);
                }
                resource.cachedHash = hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseRealBuffer(int index)
        {
            FRDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as FRDGBuffer;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a compute buffer ({resource.description.name}) that was never created. Check that there is at least one pass writing to it first.");

                m_BufferPool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateRealTexture(ref FRDGContext graphContext, int index)
        {
            FRDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as FRDGTexture;

            if (!resource.imported)
            {
                var desc = resource.description;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created texture ({0}). Texture was probably declared for writing more than once in the same pass.", resource.description.name));

                resource.resource = null;
                if (!m_TexturePool.Pull(hashCode, out resource.resource)) 
                {
                    resource.resource = RTHandles.Alloc(desc.width, desc.height, desc.slices, (DepthBits)desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                    desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, (MSAASamples)desc.msaaSamples, desc.bindTextureMS, false, RenderTextureMemoryless.None, desc.name);
                }
                resource.cachedHash = hashCode;

                if (resource.description.clearBuffer)
                {
                    bool debugClear = !resource.description.clearBuffer;
                    var clearFlag = resource.description.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    var clearColor = debugClear ? Color.magenta : resource.description.clearColor;
                    CoreUtils.SetRenderTarget(graphContext.cmdBuffer, resource.resource, clearFlag, clearColor);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseRealTexture(int index)
        {
            FRDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as FRDGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.description.name}) that was never created. Check that there is at least one pass writing to it first.");

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetGlobalTextures(ref FRDGContext graphContext, List<FRDGResourceHandle> textures)
        {
            foreach (var resource in textures)
            {
                FRDGTexture Texture = GetTextureResource(resource);
                if (Texture.shaderProperty != 0)
                {
                    if (Texture.resource != null)
                    {
                        graphContext.cmdBuffer.SetGlobalTexture(Texture.shaderProperty, Texture.resource);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    }
}
