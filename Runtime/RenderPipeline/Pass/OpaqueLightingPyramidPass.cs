using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class OpaqueLightingPyramidPassUtilityData
    {
        internal static string TextureName = "OpaqueLightingPyramid";
    }

    public partial class InfinityRenderPipeline
    {
        struct OpaqueLightingPyramidPassData
        {
            public int mipCount;
            public int2 resolution;
            public ComputeShader colorPyramidShader;
            public RGTextureRef lightingTexture;
            public RGTextureRef pyramidTexture;
        }

        void ComputeOpaqueLightingPyramid(RenderContext renderContext, Camera camera)
        {
            if (!ShouldRecordFeature(EFrameFeature.OpaqueLightingPyramid))
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

            TextureDescriptor pyramidDsc = CreateColorPyramidDescriptor(width, height, OpaqueLightingPyramidPassUtilityData.TextureName);
            RGTextureRef pyramidTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OpaqueLightingPyramidBuffer, pyramidDsc);
            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<OpaqueLightingPyramidPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeOpaqueLightingPyramid)))
            {
                ref OpaqueLightingPyramidPassData passData = ref passRef.GetPassData<OpaqueLightingPyramidPassData>();
                passData.mipCount = mipCount;
                passData.resolution = new int2(width, height);
                passData.colorPyramidShader = pipelineAsset.colorPyramidShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.pyramidTexture = passRef.WriteTexture(pyramidTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in OpaqueLightingPyramidPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ColorPyramidPassUtilityData.DispatchMipBatches(cmdEncoder, passData.colorPyramidShader, passData.lightingTexture, passData.pyramidTexture, passData.resolution, passData.mipCount);
                });
            }

            MarkFeatureProduced(EFrameFeature.OpaqueLightingPyramid);
        }
    }
}
