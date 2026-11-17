using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_CompositeParameters Shaders/Pass/ScreenSpace/Compute_ScreenSpaceComposite.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct CompositeParameters
    {
        public Vector4 resolution;
    }

    internal static class ScreenSpaceCompositePassUtilityData
    {
        internal static readonly int CBV_CompositeParametersID = Shader.PropertyToID("CBV_CompositeParameters");
        internal static readonly int SRV_IndirectDiffuseID = Shader.PropertyToID("SRV_IndirectDiffuse");
        internal static readonly int SRV_IndirectSpecularID = Shader.PropertyToID("SRV_IndirectSpecular");
        internal static readonly string TextureName = "ScreenSpaceComposite";
        internal static readonly int SRV_SSRTextureID = Shader.PropertyToID("SRV_SSRTexture");
        internal static readonly int SRV_SSGITextureID = Shader.PropertyToID("SRV_SSGITexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct ScreenSpaceCompositePassData
        {
            public RGConstantsHandle parametersHandle;
            public bool hasSubsurface;
            public RGTextureHandle scatterDiffuseHandle;
            public int hasSSR;
            public int hasSSGI;
            public ComputeShader compositeShader;
            public int kernel;
            public int2 groups;
            public LocalKeyword subsurfaceKeyword, ssrKeyword, ssgiKeyword;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle indirectDiffuseHandle, indirectSpecularHandle;
            public RGTextureHandle gBufferAHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle gBufferCHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle ssrTextureHandle;
            public RGTextureHandle ssgiTextureHandle;
            public RGTextureHandle sceneColorTextureHandle;
        }

        void ComputeScreenSpaceComposite(in ViewContext context)
        {
            bool hasSSR = context.features.ShouldRecord(EFrameFeature.SSR);
            bool hasSSGI = context.features.ShouldRecord(EFrameFeature.SSGI);
            RGTextureHandle ssrTextureHandle = hasSSR ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSRBuffer) : default;
            RGTextureHandle ssgiTextureHandle = hasSSGI ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSGIBuffer) : default;
            // Without SSR or SSGI the deferred lighting is the opaque scene color (SceneColorKeys.opaque).
            if (!hasSSR && !hasSSGI)
            {
                return;
            }

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;

            TextureDescriptor compositeDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            compositeDsc.name = ScreenSpaceCompositePassUtilityData.TextureName;
            compositeDsc.dimension = TextureDimension.Tex2D;
            compositeDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            compositeDsc.enableRandomWrite = true;
            RGTextureHandle compositeTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ScreenSpaceCompositeBuffer, compositeDsc);

            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle gBufferAHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle gBufferCHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            {
                RGComputePassHandle<ScreenSpaceCompositePassData> passHandle = m_RGBuilder.AddComputePass<ScreenSpaceCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceComposite));
                ref ScreenSpaceCompositePassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(new CompositeParameters
                {
                    resolution = new Vector4(width, height, 1.0f / width, 1.0f / height)
                });
                passData.hasSubsurface = context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
                if (passData.hasSubsurface && hasSSGI)
                {
                    passData.scatterDiffuseHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer);
                    passHandle.ReadTexture(passData.scatterDiffuseHandle);
                    passHandle.WriteTexture(passData.scatterDiffuseHandle);
                }
                passData.hasSSR = hasSSR ? 1 : 0;
                passData.hasSSGI = hasSSGI ? 1 : 0;
                passData.compositeShader = kernels.screenSpaceComposite.shader;
                passData.kernel = kernels.screenSpaceComposite.index;
                passData.groups = new int2(kernels.screenSpaceComposite.GroupsX(width), kernels.screenSpaceComposite.GroupsY(height));
                passData.subsurfaceKeyword = kernels.compositeSubsurface;
                passData.ssrKeyword = kernels.compositeSSR;
                passData.ssgiKeyword = kernels.compositeSSGI;
                passData.lightingTextureHandle = passHandle.ReadTexture(lightingTextureHandle);
                passData.indirectDiffuseHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.IndirectDiffuseBuffer));
                passData.indirectSpecularHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.IndirectSpecularBuffer));
                passData.gBufferAHandle = passHandle.ReadTexture(gBufferAHandle);
                passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                passData.gBufferCHandle = passHandle.ReadTexture(gBufferCHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                if (hasSSR)
                {
                    passData.ssrTextureHandle = passHandle.ReadTexture(ssrTextureHandle);
                }
                if (hasSSGI)
                {
                    passData.ssgiTextureHandle = passHandle.ReadTexture(ssgiTextureHandle);
                }
                passData.sceneColorTextureHandle = passHandle.WriteTexture(compositeTextureHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in ScreenSpaceCompositePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.compositeShader;
                    cmdEncoder.SetKeyword(shader, passData.subsurfaceKeyword, passData.hasSubsurface);
                    if (passData.hasSubsurface && passData.hasSSGI != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_ScatterDiffuse, passData.scatterDiffuseHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_IndirectDiffuseID, passData.indirectDiffuseHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_IndirectSpecularID, passData.indirectSpecularHandle);
                    cmdEncoder.SetComputeConstantBufferParam(shader, ScreenSpaceCompositePassUtilityData.CBV_CompositeParametersID, passData.parametersHandle);
                    cmdEncoder.SetKeyword(shader, passData.ssrKeyword, passData.hasSSR != 0);
                    cmdEncoder.SetKeyword(shader, passData.ssgiKeyword, passData.hasSSGI != 0);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_LightingTexture, passData.lightingTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferAHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferCHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    if (passData.hasSSR != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_SSRTextureID, passData.ssrTextureHandle);
                    }
                    if (passData.hasSSGI != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_SSGITextureID, passData.ssgiTextureHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_SceneColor, passData.sceneColorTextureHandle);
                    cmdEncoder.DispatchCompute(shader, passData.kernel, passData.groups.x, passData.groups.y, 1);
                });
            }

        }
    }
}
