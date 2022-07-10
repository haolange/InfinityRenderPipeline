using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GraphicsFeature
{
    public static class PyramidDepthShaderIDs
    {
        public static int PrevMipDepth = Shader.PropertyToID("_PrevMipDepth");
        public static int HierarchicalDepth = Shader.PropertyToID("_HierarchicalDepth");
        public static int PrevCurr_InvSize = Shader.PropertyToID("_PrevCurr_Inverse_Size");
    }

    public class PyramidDepthGenerator
    {
        private int m_MipCount;
        private int[] m_PyramidMipIDs;
        private ComputeShader m_Shader;

        public PyramidDepthGenerator(ComputeShader shader, in int maxMipCount = 10)
        {
            m_Shader = shader;
            m_MipCount = maxMipCount;

            m_PyramidMipIDs = new int[m_MipCount];
            for (int i = 0; i < m_MipCount; ++i) 
            {
                m_PyramidMipIDs[i] = Shader.PropertyToID("_SSSRDepthMip" + i);
            }
        }

        public void DepthPyramidUpdate(CommandBuffer cmdBuffer, in int2 screenSize, in RenderTargetIdentifier pyramidDepthTexture) 
        {
            int2 pyramidSize = screenSize;
            int2 lastPyramidSize = screenSize;
            RenderTargetIdentifier lastPyramidDepthTexture = pyramidDepthTexture;

            for (int i = 0; i < m_MipCount; ++i) 
            {
                pyramidSize.x /= 2;
                pyramidSize.y /= 2;
                int dispatchSizeX = Mathf.CeilToInt(pyramidSize.x / 8);
                int dispatchSizeY = Mathf.CeilToInt(pyramidSize.y / 8);

                if (dispatchSizeX < 1 || dispatchSizeY < 1) break;

                cmdBuffer.GetTemporaryRT(m_PyramidMipIDs[i], pyramidSize.x, pyramidSize.y, 0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default, 1, true);
                cmdBuffer.SetComputeVectorParam(m_Shader, PyramidDepthShaderIDs.PrevCurr_InvSize, new float4(1.0f / pyramidSize.x, 1.0f / pyramidSize.y, 1.0f / lastPyramidSize.x, 1.0f / lastPyramidSize.y));
                cmdBuffer.SetComputeTextureParam(m_Shader, 0, PyramidDepthShaderIDs.PrevMipDepth, lastPyramidDepthTexture);
                cmdBuffer.SetComputeTextureParam(m_Shader, 0, PyramidDepthShaderIDs.HierarchicalDepth, m_PyramidMipIDs[i]);
                cmdBuffer.DispatchCompute(m_Shader, 0, Mathf.CeilToInt(pyramidSize.x / 8), Mathf.CeilToInt(pyramidSize.y / 8), 1);
                cmdBuffer.CopyTexture(m_PyramidMipIDs[i], 0, 0, pyramidDepthTexture, 0, i + 1);

                lastPyramidSize = pyramidSize;
                lastPyramidDepthTexture = m_PyramidMipIDs[i];
		    } 
            
            for (int j = 0; j < m_MipCount; ++j) 
            {
                cmdBuffer.ReleaseTemporaryRT(m_PyramidMipIDs[j]);
            }
        }
    }
}
