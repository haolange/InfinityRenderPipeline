using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public enum EDepthBits
    {
        None = 0,
        Depth8 = 8,
        Depth16 = 16,
        Depth24 = 24,
        Depth32 = 32
    }

    public enum EMSAASamples
    {
        None = 1,
        MSAA2x = 2,
        MSAA4x = 4,
        MSAA8x = 8
    }

    public struct BufferDescriptor : IEquatable<BufferDescriptor>
    {
        public string name;

        public int count;
        public int stride;
        public ComputeBufferType type;

        public BufferDescriptor(int count, int stride) : this()
        {
            this.count = count;
            this.stride = stride;
            type = ComputeBufferType.Default;
        }

        public BufferDescriptor(int count, int stride, ComputeBufferType type) : this()
        {
            this.type = type;
            this.count = count;
            this.stride = stride;
        }

        public bool Equals(BufferDescriptor target)
        {
            return count == target.count
                && stride == target.stride
                && type == target.type;
        }

        public override bool Equals(object target)
        {
            return target is BufferDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hashCode = 17;
            hashCode = hashCode * 23 + count;
            hashCode = hashCode * 23 + stride;
            hashCode = hashCode * 23 + (int)type;
            return hashCode;
        }
    }

    [Serializable]
    public struct TextureDescriptor : IEquatable<TextureDescriptor>
    {
        public string name;

        public int width;
        public int height;
        public int allocationWidth, allocationHeight;
        public ETextureSizingDomain sizingDomain;
        public int viewDivisor;
        public int physicalWidth => allocationWidth > 0 ? allocationWidth : width;
        public int physicalHeight => allocationHeight > 0 ? allocationHeight : height;
        public FTextureView view => new FTextureView(new Unity.Mathematics.int2(width, height), new Unity.Mathematics.int2(physicalWidth, physicalHeight));
        public TextureDescriptor PhysicalDescriptor()
        {
            var physical = this;
            physical.width = physicalWidth; physical.height = physicalHeight;
            physical.allocationWidth = physical.allocationHeight = 0;
            physical.sizingDomain = ETextureSizingDomain.Exact; physical.viewDivisor = 0;
            return physical;
        }
        public int slices;
        public EDepthBits depthBufferBits;
        public GraphicsFormat colorFormat;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public TextureDimension dimension;
        public bool enableRandomWrite;
        public bool useMipMap;
        public bool autoGenerateMips;
        public bool isShadowMap;
        public int anisoLevel;
        public float mipMapBias;
        public bool enableMSAA;
        public bool bindTextureMS;
        public EMSAASamples msaaSamples;
        public bool clearBuffer;
        public Color clearColor;

        public TextureDescriptor(int width, int height, ETextureSizingDomain domain, int divisor = 1) : this(width, height)
        { sizingDomain = domain; viewDivisor = divisor; }

        public TextureDescriptor(int Width, int Height) : this()
        {
            width = Width;
            height = Height;
            slices = 1;

            clearColor = Color.black;
            enableMSAA = false;
            bindTextureMS = false;
            clearBuffer = false;
            isShadowMap = false;
            enableRandomWrite = false;

            msaaSamples = EMSAASamples.None;
            depthBufferBits = EDepthBits.None;
            wrapMode = TextureWrapMode.Repeat;
        }

        public TextureDescriptor(int Width, int Height, int SliceOrDepth) : this()
        {
            width = Width;
            height = Height;
            slices = SliceOrDepth;

            clearColor = Color.black;
            enableMSAA = false;
            bindTextureMS = false;
            clearBuffer = false;
            isShadowMap = false;
            enableRandomWrite = false;

            msaaSamples = EMSAASamples.None;
            depthBufferBits = EDepthBits.None;
            wrapMode = TextureWrapMode.Repeat;
        }

        public bool Equals(TextureDescriptor target)
        {
            return allocationWidth == target.allocationWidth && allocationHeight == target.allocationHeight
                && sizingDomain == target.sizingDomain && viewDivisor == target.viewDivisor
                && width == target.width
                && height == target.height
                && slices == target.slices
                && mipMapBias.Equals(target.mipMapBias)
                && depthBufferBits == target.depthBufferBits
                && colorFormat == target.colorFormat
                && filterMode == target.filterMode
                && wrapMode == target.wrapMode
                && dimension == target.dimension
                && anisoLevel == target.anisoLevel
                && enableRandomWrite == target.enableRandomWrite
                && useMipMap == target.useMipMap
                && autoGenerateMips == target.autoGenerateMips
                && isShadowMap == target.isShadowMap
                && msaaSamples == target.msaaSamples
                && bindTextureMS == target.bindTextureMS;
        }

        public override bool Equals(object target)
        {
            return target is TextureDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hashCode = 17;
            hashCode = hashCode * 23 + allocationWidth;
            hashCode = hashCode * 23 + allocationHeight;
            hashCode = hashCode * 23 + (int)sizingDomain;
            hashCode = hashCode * 23 + viewDivisor;
            hashCode = hashCode * 23 + width;
            hashCode = hashCode * 23 + height;
            hashCode = hashCode * 23 + slices;
            hashCode = hashCode * 23 + mipMapBias.GetHashCode();
            hashCode = hashCode * 23 + (int)depthBufferBits;
            hashCode = hashCode * 23 + (int)colorFormat;
            hashCode = hashCode * 23 + (int)filterMode;
            hashCode = hashCode * 23 + (int)wrapMode;
            hashCode = hashCode * 23 + (int)dimension;
            hashCode = hashCode * 23 + anisoLevel;
            hashCode = hashCode * 23 + (enableRandomWrite ? 1 : 0);
            hashCode = hashCode * 23 + (useMipMap ? 1 : 0);
            hashCode = hashCode * 23 + (autoGenerateMips ? 1 : 0);
            hashCode = hashCode * 23 + (isShadowMap ? 1 : 0);
            hashCode = hashCode * 23 + (int)msaaSamples;
            hashCode = hashCode * 23 + (bindTextureMS ? 1 : 0);
            return hashCode;
        }
    }

    public struct FBufferRef
    {
        internal int handle;
        internal BufferDescriptor descriptor;
        public ComputeBuffer buffer;

        public FBufferRef(in int handle, in BufferDescriptor descriptor, ComputeBuffer buffer)
        {
            this.handle = handle;
            this.descriptor = descriptor;
            this.buffer = buffer;
        }

        public static implicit operator ComputeBuffer(in FBufferRef bufferRef) => bufferRef.buffer;
    }

    public struct FTextureRef
    {
        internal int handle;
        internal TextureDescriptor descriptor;
        public RTHandle texture;

        internal FTextureRef(in int handle, in TextureDescriptor descriptor, RTHandle texture)
        {
            this.handle = handle;
            this.descriptor = descriptor;
            this.texture = texture;
        }

        public static implicit operator RTHandle(in FTextureRef textureRef) => textureRef.texture;
    }

    public abstract class FGPUResourceCache<TResource, TDescriptor>
        where TResource : class
        where TDescriptor : struct, IEquatable<TDescriptor>
    {
        protected struct Entry
        {
            public TDescriptor descriptor;
            public TResource resource;
            public long lastUse, bytes;
        }
        struct RetiredEntry { public Entry entry; public GraphicsFence fence; }
        protected readonly Dictionary<int, List<Entry>> m_ResourcePool = new Dictionary<int, List<Entry>>(64);
        readonly List<RetiredEntry> m_Retired = new List<RetiredEntry>();
        long m_Epoch;
        public long estimatedIdleBytes { get; private set; }
        public long estimatedRetiredBytes { get; private set; }
        public long allocationCount { get; private set; }
        public long reuseCount { get; private set; }
        public long evictionCount { get; private set; }
        public long estimatedAllocatedBytes { get; private set; }
        public long estimatedPeakBytes { get; private set; }
        public long estimatedActiveBytes => estimatedAllocatedBytes - estimatedIdleBytes - estimatedRetiredBytes;
        protected abstract void ReleaseInternalResource(TResource resource);
        protected abstract long EstimateBytes(in TDescriptor descriptor);
        public void RecordAllocation(in TDescriptor descriptor)
        {
            allocationCount++;
            estimatedAllocatedBytes += EstimateBytes(descriptor);
            estimatedPeakBytes = Math.Max(estimatedPeakBytes, estimatedAllocatedBytes);
        }
        public bool Pull(in int hashCode, in TDescriptor descriptor, out TResource resource)
        {
            if (m_ResourcePool.TryGetValue(hashCode, out var list))
                for (int i = list.Count - 1; i >= 0; --i)
                {
                    Entry entry = list[i];
                    if (!entry.descriptor.Equals(descriptor)) continue;
                    resource = entry.resource; list.RemoveAt(i);
                    if (list.Count == 0) m_ResourcePool.Remove(hashCode);
                    estimatedIdleBytes -= entry.bytes; reuseCount++;
                    return true;
                }
            resource = null;
            return false;
        }
        public void Push(in int hash, in TDescriptor descriptor, TResource resource)
        {
            if (!m_ResourcePool.TryGetValue(hash, out var list)) m_ResourcePool.Add(hash, list = new List<Entry>());
            long bytes = EstimateBytes(descriptor);
            list.Add(new Entry { descriptor = descriptor, resource = resource, bytes = bytes, lastUse = m_Epoch });
            estimatedIdleBytes += bytes;
        }
        public void AfterSubmit(long watermark, GraphicsFence fence)
        {
            m_Epoch++;
            for (int i = m_Retired.Count - 1; i >= 0; --i)
            {
                if (!m_Retired[i].fence.passed) continue;
                Entry entry = m_Retired[i].entry;
                ReleaseInternalResource(entry.resource);
                estimatedRetiredBytes -= entry.bytes; estimatedAllocatedBytes -= entry.bytes;
                m_Retired.RemoveAt(i);
            }
            while (estimatedAllocatedBytes - estimatedRetiredBytes > watermark)
            {
                int bucket = 0, index = -1; long oldest = long.MaxValue;
                foreach (var pair in m_ResourcePool)
                    for (int i = 0; i < pair.Value.Count; ++i)
                    {
                        Entry candidate = pair.Value[i];
                        if (m_Epoch - candidate.lastUse <= 2 || candidate.lastUse >= oldest) continue;
                        bucket = pair.Key; index = i; oldest = candidate.lastUse;
                    }
                if (index < 0) break;
                var entries = m_ResourcePool[bucket]; Entry entry = entries[index]; entries.RemoveAt(index);
                if (entries.Count == 0) m_ResourcePool.Remove(bucket);
                estimatedIdleBytes -= entry.bytes; estimatedRetiredBytes += entry.bytes; evictionCount++;
                m_Retired.Add(new RetiredEntry { entry = entry, fence = fence });
            }
        }
        public void Dispose()
        {
            foreach (var pair in m_ResourcePool) foreach (var entry in pair.Value) ReleaseInternalResource(entry.resource);
            foreach (var entry in m_Retired) ReleaseInternalResource(entry.entry.resource);
            m_ResourcePool.Clear(); m_Retired.Clear();
            estimatedAllocatedBytes = estimatedIdleBytes = estimatedRetiredBytes = 0;
        }
    }
    public sealed class BufferCache : FGPUResourceCache<ComputeBuffer, BufferDescriptor>
    {
        protected override void ReleaseInternalResource(ComputeBuffer resource) => resource.Release();
        protected override long EstimateBytes(in BufferDescriptor descriptor) => (long)descriptor.count * descriptor.stride;
    }
    public sealed class TextureCache : FGPUResourceCache<RTHandle, TextureDescriptor>
    {
        protected override void ReleaseInternalResource(RTHandle resource) => resource.Release();
        protected override long EstimateBytes(in TextureDescriptor descriptor)
        {
            long bytes = 0;
            var format = descriptor.colorFormat != GraphicsFormat.None ? descriptor.colorFormat : GraphicsFormat.D32_SFloat;
            int width = descriptor.physicalWidth, height = descriptor.physicalHeight;
            do
            {
                bytes += (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, format) * Math.Max(1, descriptor.slices) * Math.Max(1, (int)descriptor.msaaSamples);
                if (!descriptor.useMipMap || (width == 1 && height == 1)) break;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            } while (true);
            return bytes;
        }
    }
}
