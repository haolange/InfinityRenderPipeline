using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ColorPyramidPassUtilityData
    {
        internal static string TextureName = "ColorPyramidTexture";
        internal static int SRV_ColorTextureID = Shader.PropertyToID("_Source");
        internal static int UAV_ColorPyramidID = Shader.PropertyToID("_Result");
        internal static int ColorPyramid_SizeID = Shader.PropertyToID("_Size");
    }

    public partial class InfinityRenderPipeline
    {
        struct ColorPyramidPassData
        {
            public int maxMipLevel;
            public int2 resolution;
            public ComputeShader colorPyramidShader;
            public RGTextureRef lightingTexture;
            public RGTextureRef colorPyramidTexture;
        }

        void ComputeColorPyramid(RenderContext renderContext, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int maxMipLevel = (int)math.floor(math.log2(math.max(width, height)));

            TextureDescriptor colorPyramidDsc = new TextureDescriptor(width, height);
            {
                colorPyramidDsc.name = ColorPyramidPassUtilityData.TextureName;
                colorPyramidDsc.dimension = TextureDimension.Tex2D;
                colorPyramidDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                colorPyramidDsc.depthBufferBits = EDepthBits.None;
                colorPyramidDsc.enableRandomWrite = true;
                colorPyramidDsc.useMipMap = true;
                colorPyramidDsc.autoGenerateMips = false;
            }
            RGTextureRef colorPyramidTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ColorPyramidBuffer, colorPyramidDsc);

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);

            //Add ColorPyramidPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ColorPyramidPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeColorPyramid)))
            {
                //Setup Phase
                ref ColorPyramidPassData passData = ref passRef.GetPassData<ColorPyramidPassData>();
                passData.maxMipLevel = maxMipLevel;
                passData.resolution = new int2(width, height);
                passData.colorPyramidShader = pipelineAsset.colorPyramidShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.colorPyramidTexture = passRef.WriteTexture(colorPyramidTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ColorPyramidPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.colorPyramidShader == null) return;

                    int prevWidth = passData.resolution.x;
                    int prevHeight = passData.resolution.y;

                    // Mip 0: Copy scene color
                    cmdEncoder.SetComputeTextureParam(passData.colorPyramidShader, 0, ColorPyramidPassUtilityData.SRV_ColorTextureID, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(passData.colorPyramidShader, 0, ColorPyramidPassUtilityData.UAV_ColorPyramidID, passData.colorPyramidTexture, 0);
                    cmdEncoder.SetComputeVectorParam(passData.colorPyramidShader, ColorPyramidPassUtilityData.ColorPyramid_SizeID, new Vector4(prevWidth, prevHeight, 1.0f / prevWidth, 1.0f / prevHeight));
                    cmdEncoder.DispatchCompute(passData.colorPyramidShader, 0, Mathf.CeilToInt(prevWidth / 8.0f), Mathf.CeilToInt(prevHeight / 8.0f), 1);

                    // Subsequent mips: gaussian downsample
                    for (int mip = 1; mip <= Mathf.Min(passData.maxMipLevel, 8); ++mip)
                    {
                        int currWidth = Mathf.Max(1, prevWidth >> 1);
                        int currHeight = Mathf.Max(1, prevHeight >> 1);

                        cmdEncoder.SetComputeTextureParam(passData.colorPyramidShader, 0, ColorPyramidPassUtilityData.SRV_ColorTextureID, passData.colorPyramidTexture, mip - 1);
                        cmdEncoder.SetComputeTextureParam(passData.colorPyramidShader, 0, ColorPyramidPassUtilityData.UAV_ColorPyramidID, passData.colorPyramidTexture, mip);
                        cmdEncoder.SetComputeVectorParam(passData.colorPyramidShader, ColorPyramidPassUtilityData.ColorPyramid_SizeID, new Vector4(prevWidth, prevHeight, 1.0f / prevWidth, 1.0f / prevHeight));
                        cmdEncoder.DispatchCompute(passData.colorPyramidShader, 0, Mathf.CeilToInt(currWidth / 8.0f), Mathf.CeilToInt(currHeight / 8.0f), 1);

                        prevWidth = currWidth;
                        prevHeight = currHeight;
                    }
                });
            }
        }
    }
}
