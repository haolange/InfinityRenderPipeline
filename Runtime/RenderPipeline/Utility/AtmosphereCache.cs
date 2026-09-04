using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class AtmosphereSharedCache : IDisposable
    {
        readonly HistoryCache m_History = new HistoryCache();
        AtmosphereParameter m_SharedKey;
        AtmosphereParameter m_PendingSharedKey;
        AtmosphereIBLKey m_IBLKey;
        AtmosphereIBLKey m_PendingIBLKey;
        bool m_HasShared;
        bool m_HasIBL;
        bool m_SharedProducedThisFrame;
        bool m_IBLProducedThisFrame;

        public int RetiredQueuedCount => m_History.RetiredQueuedCount;

        public void BeginFrame()
        {
            m_History.BeginFrame();
            m_SharedProducedThisFrame = false;
            m_IBLProducedThisFrame = false;
        }

        public void ResolveShared(
            in AtmosphereParameter key,
            in TextureDescriptor transmittanceDescriptor,
            in TextureDescriptor multiScatterDescriptor,
            out FTextureRef transmittance,
            out FTextureRef multiScatter,
            out bool hit)
        {
            bool committedHit = m_HasShared && m_SharedKey.Equals(key);
            bool pendingHit = m_SharedProducedThisFrame && m_PendingSharedKey.Equals(key);
            hit = committedHit || pendingHit;

            if (committedHit && !pendingHit)
            {
                transmittance = m_History.GetTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceDescriptor);
                multiScatter = m_History.GetTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatterDescriptor);
                return;
            }

            transmittance = m_History.GetWriteTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceDescriptor);
            multiScatter = m_History.GetWriteTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatterDescriptor);
            if (!pendingHit)
            {
                m_PendingSharedKey = key;
            }
        }

        public void MarkSharedProduced()
        {
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereTransmittanceLUT);
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereMultiScatteringLUT);
            m_SharedProducedThisFrame = true;
        }

        public void ResolveIBL(
            in AtmosphereIBLKey key,
            in TextureDescriptor cubemapDescriptor,
            in TextureDescriptor prefilterDescriptor,
            in BufferDescriptor shDescriptor,
            out FTextureRef cubemap,
            out FTextureRef prefilter,
            out FBufferRef shCoefficients,
            out bool hit)
        {
            bool committedHit = m_HasIBL && m_IBLKey.Equals(key);
            bool pendingHit = m_IBLProducedThisFrame && m_PendingIBLKey.Equals(key);
            hit = committedHit || pendingHit;

            if (committedHit && !pendingHit)
            {
                cubemap = m_History.GetTexture(InfinityShaderIDs.AtmosphereCubemap, cubemapDescriptor);
                prefilter = m_History.GetTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, prefilterDescriptor);
                shCoefficients = m_History.GetBuffer(InfinityShaderIDs.AtmosphereSkySH, shDescriptor);
                return;
            }

            cubemap = m_History.GetWriteTexture(InfinityShaderIDs.AtmosphereCubemap, cubemapDescriptor);
            prefilter = m_History.GetWriteTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, prefilterDescriptor);
            shCoefficients = m_History.GetWriteBuffer(InfinityShaderIDs.AtmosphereSkySH, shDescriptor);
            if (!pendingHit)
            {
                m_PendingIBLKey = key;
            }
        }

        public void MarkIBLProduced()
        {
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereCubemap);
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereGGXPrefilter);
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereSkySH);
            m_IBLProducedThisFrame = true;
        }

        public void CommitFrame()
        {
            m_History.CommitFrame();
            if (m_SharedProducedThisFrame)
            {
                m_SharedKey = m_PendingSharedKey;
                m_HasShared = true;
            }

            if (m_IBLProducedThisFrame)
            {
                m_IBLKey = m_PendingIBLKey;
                m_HasIBL = true;
            }

            m_SharedProducedThisFrame = false;
            m_IBLProducedThisFrame = false;
        }

        public void RollbackPending()
        {
            m_History.RollbackPending();
            m_SharedProducedThisFrame = false;
            m_IBLProducedThisFrame = false;
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

    internal sealed class AtmosphereViewCache : IDisposable
    {
        readonly HistoryCache m_History = new HistoryCache();
        AtmosphereViewKey m_Key;
        AtmosphereViewKey m_PendingKey;
        bool m_HasKey;
        bool m_ProducedThisFrame;

        public int RetiredQueuedCount => m_History.RetiredQueuedCount;

        public void BeginFrame()
        {
            m_History.BeginFrame();
            m_ProducedThisFrame = false;
        }

        public void Resolve(
            in AtmosphereViewKey key,
            in TextureDescriptor skyViewDescriptor,
            in TextureDescriptor aerialDescriptor,
            in BufferDescriptor sunDescriptor,
            out FTextureRef skyView,
            out FTextureRef aerial,
            out FBufferRef sunBuffer,
            out bool hit)
        {
            bool committedHit = m_HasKey && m_Key.Equals(key);
            bool pendingHit = m_ProducedThisFrame && m_PendingKey.Equals(key);
            hit = committedHit || pendingHit;

            if (committedHit && !pendingHit)
            {
                skyView = m_History.GetTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, skyViewDescriptor);
                aerial = m_History.GetTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialDescriptor);
                sunBuffer = m_History.GetBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunDescriptor);
                return;
            }

            skyView = m_History.GetWriteTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, skyViewDescriptor);
            aerial = m_History.GetWriteTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialDescriptor);
            sunBuffer = m_History.GetWriteBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunDescriptor);
            if (!pendingHit)
            {
                m_PendingKey = key;
            }
        }

        public void MarkProduced()
        {
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereSkyViewLUT);
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT);
            m_History.MarkProduced(InfinityShaderIDs.AtmosphereSunBuffer);
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
