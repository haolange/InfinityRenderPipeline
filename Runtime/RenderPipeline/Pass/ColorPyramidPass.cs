using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ColorPyramidPassUtilityData
    {
        internal static string TextureName = "ColorPyramidTexture";
        internal static string HistoryTextureName = "HistoryColorPyramidTexture";
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

        static TextureDescriptor CreateColorPyramidDescriptor(int width, int height, string name)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = false;
            return descriptor;
        }

        void ImportHistoryColorPyramid(Camera camera, HistoryCache historyCache)
        {
            TextureDescriptor historyDsc = CreateColorPyramidDescriptor(camera.pixelWidth, camera.pixelHeight, ColorPyramidPassUtilityData.HistoryTextureName);
            RGTextureRef historyColorPyramid = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryColorPyramidBuffer, historyDsc));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryColorPyramidBuffer, historyColorPyramid);
        }

        void ComputeColorPyramid(RenderContext renderContext, Camera camera)
        {
            if (!ShouldRecordFeature(EFrameFeature.ColorPyramid))
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.colorPyramidShader, "KMain"))
            {
                return;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int maxMipLevel = (int)math.floor(math.log2(math.max(width, height)));

            TextureDescriptor colorPyramidDsc = CreateColorPyramidDescriptor(width, height, ColorPyramidPassUtilityData.TextureName);
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

            MarkFeatureProduced(EFrameFeature.ColorPyramid);
        }

        struct CopyHistoryColorPyramidPassData
        {
            public int mipCount;
            public RGTextureRef colorPyramidTexture;
            public RGTextureRef historyColorPyramidTexture;
        }

        void CopyHistoryColorPyramid(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.ColorPyramidBuffer, out RGTextureRef colorPyramidTexture))
            {
                return;
            }

            TextureDescriptor historyDsc = CreateColorPyramidDescriptor(camera.pixelWidth, camera.pixelHeight, ColorPyramidPassUtilityData.HistoryTextureName);
            RGTextureRef historyColorPyramidTexture = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryColorPyramidBuffer, historyDsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryColorPyramidBuffer);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryColorPyramidPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryColorPyramid)))
            {
                passRef.ReadTexture(colorPyramidTexture);
                passRef.WriteTexture(historyColorPyramidTexture);

                ref CopyHistoryColorPyramidPassData passData = ref passRef.GetPassData<CopyHistoryColorPyramidPassData>();
                passData.colorPyramidTexture = colorPyramidTexture;
                passData.historyColorPyramidTexture = historyColorPyramidTexture;
                int maxMipLevel = (int)math.floor(math.log2(math.max(camera.pixelWidth, camera.pixelHeight)));
                passData.mipCount = 1 + math.min(maxMipLevel, 8);

                passRef.SetExecuteFunc((in CopyHistoryColorPyramidPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    int mipCount = math.max(1, passData.mipCount);
                    for (int mip = 0; mip < mipCount; ++mip)
                    {
                        cmdEncoder.CopyTexture(passData.colorPyramidTexture, 0, mip, passData.historyColorPyramidTexture, 0, mip);
                    }
                });
            }
        }
    }
}
