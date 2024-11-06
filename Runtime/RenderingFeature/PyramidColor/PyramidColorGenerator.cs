using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GraphicsFeature
{
    internal static class PyramidColorShaderIDs
    {
        public static int PrevLevelColor = Shader.PropertyToID("_Source");
        public static int CurrLevelColor = Shader.PropertyToID("_Result");
        public static int PrevCurr_Size = Shader.PropertyToID("_Size");
        public static int ColorPyramidNumLOD = Shader.PropertyToID("ColorPyramidNumLOD");
    }

    public class PyramidColorGenerator
    {
        private int m_MipCount;
        private int[] m_PyramidMipIDs;
        private ComputeShader m_Shader;

        public PyramidColorGenerator(ComputeShader shader, in int maxMipCount = 12)
        {
            m_Shader = shader;
            m_MipCount = maxMipCount;

            m_PyramidMipIDs = new int[m_MipCount];
            for (int i = 0; i < m_MipCount; ++i) 
            {
                m_PyramidMipIDs[i] = Shader.PropertyToID("_SSSRGaussianMip" + i);
            }
        }

        public void ColorPyramidUpdate(CommandBuffer cmdBuffer, in int2 screenSize, in RenderTargetIdentifier pyramidColorTexture)
        {
            //int ColorPyramidCount = Mathf.FloorToInt(Mathf.Log(ScreenSize.x, 2) - 3);
            //ColorPyramidCount = Mathf.Min(ColorPyramidCount, 12);
            cmdBuffer.SetGlobalFloat(PyramidColorShaderIDs.ColorPyramidNumLOD, (float)m_MipCount);
            RenderTargetIdentifier lastColorPyramid = pyramidColorTexture;
            int2 ColorPyramidSize = screenSize;

            for (int i = 0; i < m_MipCount; ++i) 
            {
                ColorPyramidSize.x >>= 1;
                ColorPyramidSize.y >>= 1;

                cmdBuffer.GetTemporaryRT(m_PyramidMipIDs[i], ColorPyramidSize.x, ColorPyramidSize.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, 1, true);
                cmdBuffer.SetComputeTextureParam(m_Shader, 0, PyramidColorShaderIDs.PrevLevelColor, lastColorPyramid);
                cmdBuffer.SetComputeTextureParam(m_Shader, 0, PyramidColorShaderIDs.CurrLevelColor, m_PyramidMipIDs[i]);
                cmdBuffer.SetComputeVectorParam(m_Shader, PyramidColorShaderIDs.PrevCurr_Size, new float4(ColorPyramidSize.x, ColorPyramidSize.y, 1f / ColorPyramidSize.x, 1f / ColorPyramidSize.y));
                cmdBuffer.DispatchCompute(m_Shader, 0, Mathf.CeilToInt(ColorPyramidSize.x / 8f), Mathf.CeilToInt(ColorPyramidSize.y / 8f), 1);
                cmdBuffer.CopyTexture(m_PyramidMipIDs[i], 0, 0, pyramidColorTexture, 0, i + 1);

                lastColorPyramid = m_PyramidMipIDs[i];
            } 

            for (int i = 0; i < m_MipCount; ++i) 
            {
                cmdBuffer.ReleaseTemporaryRT(m_PyramidMipIDs[i]);
            }
        }
    }
}
