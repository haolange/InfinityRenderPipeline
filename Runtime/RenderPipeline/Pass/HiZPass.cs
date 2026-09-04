using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class HiZPassUtilityData
    {
        internal static string TextureName = "HiZTexture";
        internal static int SRV_PyramidDepthID = Shader.PropertyToID("_PrevMipDepth");
        internal static int UAV_HierarchicalDepth0ID = Shader.PropertyToID("_HierarchicalDepth0");
        internal static int UAV_HierarchicalDepth1ID = Shader.PropertyToID("_HierarchicalDepth1");
        internal static int UAV_HierarchicalDepth2ID = Shader.PropertyToID("_HierarchicalDepth2");
        internal static int UAV_HierarchicalDepth3ID = Shader.PropertyToID("_HierarchicalDepth3");
        internal static int HiZ_SrcSizeID = Shader.PropertyToID("_SrcSize");
        internal static int HiZ_DstSizeID = Shader.PropertyToID("_DstSize");
        internal static int HiZ_CopySourceID = Shader.PropertyToID("_CopySource");
        internal static int HiZ_MipWriteCountID = Shader.PropertyToID("_MipWriteCount");
        internal const int KernelHiZGeneration = 0;
    }

    public partial class InfinityRenderPipeline
    {
        struct HiZPassData
        {
            public int mipCount;
            public int2 depthSize;
            public ComputeShader hiZShader;
            public RGTextureRef depthTexture;
            public RGTextureRef hiZTexture;
        }

        void ComputeHiZ(RenderContext renderContext, Camera camera)
        {
            if (!ShouldRecordFeature(EFrameFeature.HiZ))
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.hiZShader, "HiZ_Generation"))
            {
                return;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int mipCount = PyramidMipBatch.MipCount(width, height);

            TextureDescriptor hiZTextureDsc = new TextureDescriptor(width, height);
            {
                hiZTextureDsc.name = HiZPassUtilityData.TextureName;
                hiZTextureDsc.dimension = TextureDimension.Tex2D;
                hiZTextureDsc.colorFormat = GraphicsFormat.R32_SFloat;
                hiZTextureDsc.depthBufferBits = EDepthBits.None;
                hiZTextureDsc.enableRandomWrite = true;
                hiZTextureDsc.useMipMap = true;
                hiZTextureDsc.autoGenerateMips = false;
                hiZTextureDsc.filterMode = FilterMode.Point;
                hiZTextureDsc.wrapMode = TextureWrapMode.Clamp;
            }
            RGTextureRef hiZTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.HiZBuffer, hiZTextureDsc);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<HiZPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeHiZ)))
            {
                ref HiZPassData passData = ref passRef.GetPassData<HiZPassData>();
                passData.mipCount = mipCount;
                passData.depthSize = new int2(width, height);
                passData.hiZShader = pipelineAsset.hiZShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.hiZTexture = passRef.WriteTexture(hiZTexture);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in HiZPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.hiZShader;
                    int kernel = HiZPassUtilityData.KernelHiZGeneration;
                    int mipCount = passData.mipCount;
                    int batchCount = PyramidMipBatch.HiZBatchCount(mipCount);

                    for (int batch = 0; batch < batchCount; ++batch)
                    {
                        int startMip = batch * PyramidMipBatch.HiZMipsPerDispatch;
                        int writeCount = math.min(PyramidMipBatch.HiZMipsPerDispatch, mipCount - startMip);
                        int lastMip = startMip + writeCount - 1;
                        bool copySource = startMip == 0;
                        int srcMip = copySource ? 0 : startMip - 1;
                        int srcWidth = copySource ? passData.depthSize.x : PyramidMipBatch.MipSize(passData.depthSize.x, srcMip);
                        int srcHeight = copySource ? passData.depthSize.y : PyramidMipBatch.MipSize(passData.depthSize.y, srcMip);
                        int outWidth = PyramidMipBatch.MipSize(passData.depthSize.x, startMip);
                        int outHeight = PyramidMipBatch.MipSize(passData.depthSize.y, startMip);
                        int groupsX = math.max(1, Mathf.CeilToInt(outWidth / 8.0f));
                        int groupsY = math.max(1, Mathf.CeilToInt(outHeight / 8.0f));

                        cmdEncoder.BeginSample($"HiZ_Mip{startMip}-{lastMip}");
                        cmdEncoder.SetComputeIntParam(shader, HiZPassUtilityData.HiZ_CopySourceID, copySource ? 1 : 0);
                        cmdEncoder.SetComputeIntParam(shader, HiZPassUtilityData.HiZ_MipWriteCountID, writeCount);
                        cmdEncoder.SetComputeVectorParam(shader, HiZPassUtilityData.HiZ_SrcSizeID, new Vector4(srcWidth, srcHeight, 1.0f / srcWidth, 1.0f / srcHeight));
                        cmdEncoder.SetComputeVectorParam(shader, HiZPassUtilityData.HiZ_DstSizeID, new Vector4(outWidth, outHeight, 1.0f / outWidth, 1.0f / outHeight));
                        if (copySource)
                        {
                            cmdEncoder.SetComputeTextureParam(shader, kernel, HiZPassUtilityData.SRV_PyramidDepthID, passData.depthTexture);
                        }
                        else
                        {
                            cmdEncoder.SetComputeTextureParam(shader, kernel, HiZPassUtilityData.SRV_PyramidDepthID, passData.hiZTexture, srcMip);
                        }

                        for (int i = 0; i < PyramidMipBatch.HiZMipsPerDispatch; ++i)
                        {
                            int mip = i < writeCount ? startMip + i : lastMip;
                            int uavId = i == 0 ? HiZPassUtilityData.UAV_HierarchicalDepth0ID
                                : i == 1 ? HiZPassUtilityData.UAV_HierarchicalDepth1ID
                                : i == 2 ? HiZPassUtilityData.UAV_HierarchicalDepth2ID
                                : HiZPassUtilityData.UAV_HierarchicalDepth3ID;
                            cmdEncoder.SetComputeTextureParam(shader, kernel, uavId, passData.hiZTexture, mip);
                        }

                        cmdEncoder.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
                        cmdEncoder.EndSample($"HiZ_Mip{startMip}-{lastMip}");
                    }
                });
            }

            MarkFeatureProduced(EFrameFeature.HiZ);
        }
    }
}
