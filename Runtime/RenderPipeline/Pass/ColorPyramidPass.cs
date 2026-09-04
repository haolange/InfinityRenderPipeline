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
        internal static int SRV_ColorTextureID = Shader.PropertyToID("_Source");
        internal static int UAV_ColorPyramidID = Shader.PropertyToID("_Result");
        internal static int UAV_ColorPyramid1ID = Shader.PropertyToID("_Result1");
        internal static int ColorPyramid_SizeID = Shader.PropertyToID("_Size");
        internal static int ColorPyramid_DstSizeID = Shader.PropertyToID("_DstSize");
        internal static int ColorPyramid_MipWriteCountID = Shader.PropertyToID("_MipWriteCount");
        internal static int ColorPyramid_SrcScaleID = Shader.PropertyToID("_SrcScale");
        internal const int KernelMain = 0;

        internal static void DispatchMipBatches(in RGComputeEncoder cmdEncoder, ComputeShader shader, RGTextureRef sourceTexture, RGTextureRef pyramidTexture, int2 resolution, int mipCount)
        {
            int kernel = KernelMain;
            int batchCount = PyramidMipBatch.ColorPyramidBatchCount(mipCount);

            for (int batch = 0; batch < batchCount; ++batch)
            {
                int startMip = batch * PyramidMipBatch.ColorPyramidMipsPerDispatch;
                int writeCount = math.min(PyramidMipBatch.ColorPyramidMipsPerDispatch, mipCount - startMip);
                bool copySource = startMip == 0;
                int srcMip = copySource ? 0 : startMip - 1;
                int srcWidth = copySource ? resolution.x : PyramidMipBatch.MipSize(resolution.x, srcMip);
                int srcHeight = copySource ? resolution.y : PyramidMipBatch.MipSize(resolution.y, srcMip);
                int outWidth = PyramidMipBatch.MipSize(resolution.x, startMip);
                int outHeight = PyramidMipBatch.MipSize(resolution.y, startMip);
                int groupsX = math.max(1, Mathf.CeilToInt(outWidth / 8.0f));
                int groupsY = math.max(1, Mathf.CeilToInt(outHeight / 8.0f));
                int lastMip = startMip + writeCount - 1;
                string mipMarker = $"ColorPyramid_Mip{startMip}-{lastMip}";

                cmdEncoder.BeginSample(mipMarker);
                cmdEncoder.SetComputeIntParam(shader, ColorPyramid_MipWriteCountID, writeCount);
                cmdEncoder.SetComputeIntParam(shader, ColorPyramid_SrcScaleID, copySource ? 1 : 2);
                cmdEncoder.SetComputeVectorParam(shader, ColorPyramid_SizeID, new Vector4(srcWidth, srcHeight, 1.0f / srcWidth, 1.0f / srcHeight));
                cmdEncoder.SetComputeVectorParam(shader, ColorPyramid_DstSizeID, new Vector4(outWidth, outHeight, 1.0f / outWidth, 1.0f / outHeight));
                if (copySource)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernel, SRV_ColorTextureID, sourceTexture);
                }
                else
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernel, SRV_ColorTextureID, pyramidTexture, srcMip);
                }

                cmdEncoder.SetComputeTextureParam(shader, kernel, UAV_ColorPyramidID, pyramidTexture, startMip);
                int secondMip = writeCount > 1 ? startMip + 1 : startMip;
                cmdEncoder.SetComputeTextureParam(shader, kernel, UAV_ColorPyramid1ID, pyramidTexture, secondMip);
                cmdEncoder.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
                cmdEncoder.EndSample(mipMarker);
            }
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct ColorPyramidPassData
        {
            public int mipCount;
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
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
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
            int mipCount = PyramidMipBatch.MipCount(width, height);

            TextureDescriptor colorPyramidDsc = CreateColorPyramidDescriptor(width, height, ColorPyramidPassUtilityData.TextureName);
            RGTextureRef colorPyramidTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ColorPyramidBuffer, colorPyramidDsc);

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ColorPyramidPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeColorPyramid)))
            {
                ref ColorPyramidPassData passData = ref passRef.GetPassData<ColorPyramidPassData>();
                passData.mipCount = mipCount;
                passData.resolution = new int2(width, height);
                passData.colorPyramidShader = pipelineAsset.colorPyramidShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.colorPyramidTexture = passRef.WriteTexture(colorPyramidTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ColorPyramidPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ColorPyramidPassUtilityData.DispatchMipBatches(cmdEncoder, passData.colorPyramidShader, passData.lightingTexture, passData.colorPyramidTexture, passData.resolution, passData.mipCount);
                });
            }

            MarkFeatureProduced(EFrameFeature.ColorPyramid);
        }
    }
}
