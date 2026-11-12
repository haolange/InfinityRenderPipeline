using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ColorPyramidPassUtilityData
    {
        internal static readonly string TextureName = "ColorPyramidTexture";
        internal static readonly int SRV_ColorTextureID = Shader.PropertyToID("_Source");
        internal static readonly int UAV_ColorPyramidID = Shader.PropertyToID("_Result");
        internal static readonly int UAV_ColorPyramid1ID = Shader.PropertyToID("_Result1");
        internal static readonly int ColorPyramid_SizeID = Shader.PropertyToID("_Size");
        internal static readonly int ColorPyramid_DstSizeID = Shader.PropertyToID("_DstSize");
        internal static readonly int ColorPyramid_MipWriteCountID = Shader.PropertyToID("_MipWriteCount");
        internal static readonly int ColorPyramid_SrcScaleID = Shader.PropertyToID("_SrcScale");

        // One sampler per (batch, mips written), named by the first and last mip; 8 batches cover 16 mips.
        const int MaxBatchCount = 8;
        static readonly ProfilingSampler[] s_BatchSamplers = CreateBatchSamplers();

        static ProfilingSampler[] CreateBatchSamplers()
        {
            var samplers = new ProfilingSampler[MaxBatchCount * PyramidMipBatch.ColorPyramidMipsPerDispatch];
            for (int batch = 0; batch < MaxBatchCount; ++batch)
            {
                for (int written = 1; written <= PyramidMipBatch.ColorPyramidMipsPerDispatch; ++written)
                {
                    int first = batch * PyramidMipBatch.ColorPyramidMipsPerDispatch;
                    samplers[batch * PyramidMipBatch.ColorPyramidMipsPerDispatch + written - 1] = new ProfilingSampler($"ColorPyramid_Mip{first}-{first + written - 1}");
                }
            }
            return samplers;
        }

        internal static void DispatchMipBatches(in RGComputeEncoder cmdEncoder, in ComputeKernel kernel, RGTextureHandle sourceTexture, RGTextureHandle pyramidTexture, int2 resolution, int mipCount)
        {
            ComputeShader shader = kernel.shader;
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
                int groupsX = kernel.GroupsX(outWidth);
                int groupsY = kernel.GroupsY(outHeight);
                ProfilingSampler mipSample = s_BatchSamplers[batch * PyramidMipBatch.ColorPyramidMipsPerDispatch + writeCount - 1];
                cmdEncoder.BeginSample(mipSample);
                cmdEncoder.SetComputeIntParam(shader, ColorPyramid_MipWriteCountID, writeCount);
                cmdEncoder.SetComputeIntParam(shader, ColorPyramid_SrcScaleID, copySource ? 1 : 2);
                cmdEncoder.SetComputeVectorParam(shader, ColorPyramid_SizeID, new Vector4(srcWidth, srcHeight, 1.0f / srcWidth, 1.0f / srcHeight));
                cmdEncoder.SetComputeVectorParam(shader, ColorPyramid_DstSizeID, new Vector4(outWidth, outHeight, 1.0f / outWidth, 1.0f / outHeight));
                if (copySource)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernel.index, SRV_ColorTextureID, sourceTexture);
                }
                else
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernel.index, SRV_ColorTextureID, pyramidTexture, srcMip);
                }

                cmdEncoder.SetComputeTextureParam(shader, kernel.index, UAV_ColorPyramidID, pyramidTexture, startMip);
                int secondMip = writeCount > 1 ? startMip + 1 : startMip;
                cmdEncoder.SetComputeTextureParam(shader, kernel.index, UAV_ColorPyramid1ID, pyramidTexture, secondMip);
                cmdEncoder.DispatchCompute(shader, kernel.index, groupsX, groupsY, 1);
                cmdEncoder.EndSample(mipSample);
            }
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct ColorPyramidPassData
        {
            public int mipCount;
            public int2 resolution;
            public ComputeKernel kernel;
            public RGTextureHandle lightingTexture;
            public RGTextureHandle colorPyramidTexture;
        }

        static TextureDescriptor CreateColorPyramidDescriptor(int width, int height, string name)
        {
            TextureDescriptor descriptor = TextureDescriptor.Create2D(name, width, height, GraphicsFormat.B10G11R11_UFloatPack32, ETextureSizingDomain.Internal);
            descriptor.useMipMap = true;
            return descriptor;
        }

        void ComputeColorPyramid(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.ColorPyramid))
            {
                return;
            }

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int mipCount = PyramidMipBatch.MipCount(width, height);

            TextureDescriptor colorPyramidDsc = CreateColorPyramidDescriptor(width, height, ColorPyramidPassUtilityData.TextureName);
            RGTextureHandle colorPyramidTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ColorPyramidBuffer, colorPyramidDsc);

            RGTextureHandle lightingTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer);

            using (var passRef = m_RGBuilder.AddComputePass<ColorPyramidPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeColorPyramid)))
            {
                ref ColorPyramidPassData passData = ref passRef.GetPassData();
                passData.mipCount = mipCount;
                passData.resolution = new int2(width, height);
                passData.kernel = kernels.colorPyramid;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.colorPyramidTexture = passRef.WriteTexture(colorPyramidTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ColorPyramidPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ColorPyramidPassUtilityData.DispatchMipBatches(cmdEncoder, passData.kernel, passData.lightingTexture, passData.colorPyramidTexture, passData.resolution, passData.mipCount);
                });
            }

            context.features.MarkProduced(EFrameFeature.ColorPyramid);
        }
    }
}
