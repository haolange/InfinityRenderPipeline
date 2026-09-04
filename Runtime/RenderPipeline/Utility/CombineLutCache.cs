using System;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class CombineLutCache : IDisposable
    {
        readonly HistoryCache m_History = new HistoryCache();
        CombineLutParameterDescriptor m_Key;
        CombineLutParameterDescriptor m_PendingKey;
        bool m_HasKey;
        bool m_ProducedThisFrame;

        public int RetiredQueuedCount => m_History.RetiredQueuedCount;

        public void BeginFrame()
        {
            m_History.BeginFrame();
            m_ProducedThisFrame = false;
        }

        public void Resolve(in CombineLutParameterDescriptor key, in TextureDescriptor descriptor, out FTextureRef texture, out bool hit)
        {
            bool committedHit = m_HasKey && m_Key.Equals(key);
            bool pendingHit = m_ProducedThisFrame && m_PendingKey.Equals(key);
            hit = committedHit || pendingHit;

            if (committedHit && !pendingHit)
            {
                texture = m_History.GetTexture(InfinityShaderIDs.CombineLookupTexture, descriptor);
                return;
            }

            texture = m_History.GetWriteTexture(InfinityShaderIDs.CombineLookupTexture, descriptor);
            if (!pendingHit)
            {
                m_PendingKey = key;
            }
        }

        public void MarkProduced()
        {
            m_History.MarkProduced(InfinityShaderIDs.CombineLookupTexture);
            m_ProducedThisFrame = true;
        }

        public void CommitFrame()
        {
            m_History.CommitFrame();
            if (m_ProducedThisFrame)
            {
                m_Key = m_PendingKey;
                m_HasKey = true;
            }

            m_ProducedThisFrame = false;
        }

        public void RollbackPending()
        {
            m_History.RollbackPending();
            m_ProducedThisFrame = false;
        }

        public void FlushRetired()
        {
            m_History.FlushRetired();
        }

        public void Dispose()
        {
            m_History.Release();
            m_History.ForceFlushForTeardown();
        }
    }
}
