using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public class ResourcePool
    {
        public BufferCache buffers { get; }
        public TextureCache textures { get; }
        public void AfterSubmit(GraphicsFence fence)
        {
            textures.AfterSubmit(400L * 1024 * 1024, fence);
            buffers.AfterSubmit(64L * 1024 * 1024, fence);
        }

        public ResourcePool()
        {
            buffers = new BufferCache();
            textures = new TextureCache();
        }

        public FBufferRef GetBuffer(in BufferDescriptor descriptor)
        {
            ComputeBuffer buffer;
            int handle = descriptor.GetHashCode();

            if (!buffers.Pull(handle, descriptor, out buffer))
            {
                buffer = new ComputeBuffer(descriptor.count, descriptor.stride, descriptor.type);
                buffer.name = descriptor.name;
                buffers.RecordAllocation(descriptor);
            }

            return new FBufferRef(handle, descriptor, buffer);
        }

        public void ReleaseBuffer(in FBufferRef bufferRef)
        {
            buffers.Push(bufferRef.handle, bufferRef.descriptor, bufferRef.buffer);
        }

        public FTextureRef GetTexture(in TextureDescriptor descriptor)
        {
            RTHandle texture;
            var physical = descriptor.PhysicalDescriptor();
            int handle = physical.GetHashCode();

            if (!textures.Pull(handle, physical, out texture))
            {
                texture = RTHandles.Alloc(descriptor.physicalWidth, descriptor.physicalHeight, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                          descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, false, RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
                textures.RecordAllocation(physical);
            }

            return new FTextureRef(handle, descriptor, texture);
        }

        public void ReleaseTexture(in FTextureRef textureRef)
        {
            var physical = textureRef.descriptor.PhysicalDescriptor();
            textures.Push(physical.GetHashCode(), physical, textureRef.texture);
        }

        public void Dispose()
        {
            buffers.Dispose();
            textures.Dispose();
        }
    }
}
