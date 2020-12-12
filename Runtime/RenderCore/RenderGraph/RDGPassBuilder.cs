using System;

namespace InfinityTech.Runtime.Rendering.RDG
{
    public struct RDGPassBuilder : IDisposable
    {
        bool m_Disposed;
        IRDGRenderPass m_RenderPass;
        RDGResourceFactory m_Resources;


        #region Public Interface
        public ref T GetPassData<T>() where T : struct => ref ((RDGRenderPass<T>)m_RenderPass).PassData;

        public void EnableAsyncCompute(bool value)
        {
            m_RenderPass.EnableAsyncCompute(value);
        }

        public void AllowPassCulling(bool value)
        {
            m_RenderPass.AllowPassCulling(value);
        }

        public RDGTextureRef ReadTexture(in RDGTextureRef input)
        {
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        public RDGTextureRef WriteTexture(in RDGTextureRef input)
        {
            m_RenderPass.AddResourceWrite(input.handle);
            return input;
        }

        public RDGTextureRef CreateTemporalTexture(in RDGTextureDesc desc)
        {
            var result = m_Resources.CreateTexture(desc, 0, m_RenderPass.index);
            m_RenderPass.AddTemporalResource(result.handle);
            return result;
        }

        public RDGBufferRef ReadBuffer(in RDGBufferRef input)
        {
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        public RDGBufferRef WriteBuffer(in RDGBufferRef input)
        {
            m_RenderPass.AddResourceWrite(input.handle);
            return input;
        }

        public RDGBufferRef CreateTemporalBuffer(in RDGBufferDesc desc)
        {
            var result = m_Resources.CreateBuffer(desc, m_RenderPass.index);
            m_RenderPass.AddTemporalResource(result.handle);
            return result;
        }

        public RDGTextureRef UseDepthBuffer(in RDGTextureRef input, EDepthAccess flags)
        {
            m_RenderPass.SetDepthBuffer(input, flags);
            return input;
        }

        public RDGTextureRef UseColorBuffer(in RDGTextureRef input, int index)
        {
            m_RenderPass.SetColorBuffer(input, index);
            return input;
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        #region Internal Interface
        internal RDGPassBuilder(IRDGRenderPass renderPass, RDGResourceFactory resources)
        {
            m_RenderPass = renderPass;
            m_Resources = resources;
            m_Disposed = false;
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
        }
        #endregion
    }
}
