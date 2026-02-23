using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class HiZPassUtilityData
    {
        internal static string TextureName = "HiZTexture";
        internal static int SRV_PyramidDepthID = Shader.PropertyToID("_PrevMipDepth");
        internal static int UAV_PyramidDepthID = Shader.PropertyToID("_HierarchicalDepth");
        internal static int HiZ_PrevCurr_SizeID = Shader.PropertyToID("_PrevCurr_Inverse_Size");
    }

    public partial class InfinityRenderPipeline
    {
        struct HiZPassData
        {
            public int maxMipLevel;
            public int2 depthSize;
            public ComputeShader hiZShader;
            public RGTextureRef depthTexture;
            public RGTextureRef hiZTexture;
        }

        void ComputeHiZ(RenderContext renderContext, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int mipWidth = Mathf.Max(1, width >> 1);
            int mipHeight = Mathf.Max(1, height >> 1);
            int maxMipLevel = (int)math.floor(math.log2(math.max(width, height)));

            TextureDescriptor hiZTextureDsc = new TextureDescriptor(width, height);
            {
                hiZTextureDsc.name = HiZPassUtilityData.TextureName;
                hiZTextureDsc.dimension = TextureDimension.Tex2D;
                hiZTextureDsc.colorFormat = GraphicsFormat.R32_SFloat;
                hiZTextureDsc.depthBufferBits = EDepthBits.None;
                hiZTextureDsc.enableRandomWrite = true;
                hiZTextureDsc.useMipMap = true;
                hiZTextureDsc.autoGenerateMips = false;
            }
            RGTextureRef hiZTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.HiZBuffer, hiZTextureDsc);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add HiZPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<HiZPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeHiZ)))
            {
                //Setup Phase
                ref HiZPassData passData = ref passRef.GetPassData<HiZPassData>();
                passData.maxMipLevel = maxMipLevel;
                passData.depthSize = new int2(width, height);
                passData.hiZShader = pipelineAsset.hiZShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.hiZTexture = passRef.WriteTexture(hiZTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in HiZPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.hiZShader == null) return;

                    int prevWidth = passData.depthSize.x;
                    int prevHeight = passData.depthSize.y;

                    // Mip 0: Copy depth to HiZ mip0
                    cmdEncoder.SetComputeTextureParam(passData.hiZShader, 0, HiZPassUtilityData.SRV_PyramidDepthID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.hiZShader, 0, HiZPassUtilityData.UAV_PyramidDepthID, passData.hiZTexture, 0);
                    cmdEncoder.SetComputeVectorParam(passData.hiZShader, HiZPassUtilityData.HiZ_PrevCurr_SizeID, new Vector4(1.0f / prevWidth, 1.0f / prevHeight, 1.0f / prevWidth, 1.0f / prevHeight));
                    cmdEncoder.DispatchCompute(passData.hiZShader, 0, Mathf.CeilToInt(prevWidth / 8.0f), Mathf.CeilToInt(prevHeight / 8.0f), 1);

                    // Subsequent mips: downsample from previous mip
                    for (int mip = 1; mip <= passData.maxMipLevel; ++mip)
                    {
                        int currWidth = Mathf.Max(1, prevWidth >> 1);
                        int currHeight = Mathf.Max(1, prevHeight >> 1);

                        cmdEncoder.SetComputeTextureParam(passData.hiZShader, 0, HiZPassUtilityData.SRV_PyramidDepthID, passData.hiZTexture, mip - 1);
                        cmdEncoder.SetComputeTextureParam(passData.hiZShader, 0, HiZPassUtilityData.UAV_PyramidDepthID, passData.hiZTexture, mip);
                        cmdEncoder.SetComputeVectorParam(passData.hiZShader, HiZPassUtilityData.HiZ_PrevCurr_SizeID, new Vector4(1.0f / prevWidth, 1.0f / prevHeight, 1.0f / currWidth, 1.0f / currHeight));
                        cmdEncoder.DispatchCompute(passData.hiZShader, 0, Mathf.CeilToInt(currWidth / 8.0f), Mathf.CeilToInt(currHeight / 8.0f), 1);

                        prevWidth = currWidth;
                        prevHeight = currHeight;
                    }
                });
            }
        }
    }
}
