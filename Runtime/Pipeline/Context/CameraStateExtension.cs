using System;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// Typed camera-lifetime state extension participating in camera frame transactions and retirement (PRD §10.1).
    /// Implementations live with their owning rendering feature rather than on <see cref="CameraFrameState"/>.
    /// </summary>
    internal interface ICameraStateExtension : IDisposable
    {
        void BeginFrame();
        void CommitFrame(FrameFeatureSet features, int frameCount);
        void RollbackFrame();
        void CameraFailed(bool queueUncertain);
        void FlushRetired(ResourceReuseFence fence);
        void Dispose(ResourceReuseFence fence);
    }

    internal static class CameraExtensionRegistry
    {
        static int s_NextSlot;

        internal static int AllocateSlot()
        {
            return System.Threading.Interlocked.Increment(ref s_NextSlot) - 1;
        }
    }

    /// <summary>
    /// O(1) typed slot handle for a feature's camera-lifetime extension, allocated once per extension type.
    /// Avoids per-frame reflection or string lookups (R14).
    /// </summary>
    internal readonly struct CameraExtensionKey<T> where T : class, ICameraStateExtension
    {
        internal static readonly CameraExtensionKey<T> Default = new CameraExtensionKey<T>(CameraExtensionRegistry.AllocateSlot());

        internal readonly int slotIndex;

        CameraExtensionKey(int slotIndex)
        {
            this.slotIndex = slotIndex;
        }
    }
}
