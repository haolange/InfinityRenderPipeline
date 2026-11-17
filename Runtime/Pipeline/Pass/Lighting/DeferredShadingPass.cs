using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_DeferredShadingParameters Shaders/Pass/Lighting/Compute_DeferredShading.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeferredShadingParameters
    {
        public Vector4 resolution;
        public float farDepth;
        public float atmosphereIBLMaxMip;
        public int padding0;
        public int padding1;
    }

    internal static class DeferredShadingPassUtilityData
    {
        internal static readonly int CBV_DeferredShadingParametersID = Shader.PropertyToID("CBV_DeferredShadingParameters");
        internal static readonly int SRV_BakedDiffuseID = Shader.PropertyToID("SRV_BakedDiffuse");
        internal static readonly int SRV_BakedOcclusionID = Shader.PropertyToID("SRV_BakedOcclusion");
        internal static readonly int UAV_IndirectDiffuseID = Shader.PropertyToID("UAV_IndirectDiffuse");
        internal static readonly int UAV_IndirectSpecularID = Shader.PropertyToID("UAV_IndirectSpecular");
        internal static readonly string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct DeferredShadingPassData
        {
            public int kernel;
            public int2 groupCount;
            public LightLoopInputs lightLoop;
            public RGConstantsHandle parametersHandle;
            public bool hasSubsurface;
            public RGTextureHandle scatterDiffuseHandle;
            public int hasAO;
            public int hasContactShadow;
            public RGBufferHandle lightRecordBufferHandle;
            public ShadowInputs shadows;
            public ComputeShader deferredShadingShader;
            public LocalKeyword subsurfaceKeyword;
            public LocalKeyword occlusionKeyword;
            public LocalKeyword contactShadowKeyword;
            public RGTextureHandle bakedDiffuseHandle, bakedOcclusionHandle, indirectDiffuseHandle, indirectSpecularHandle;
            public RGTextureHandle gBufferAHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle gBufferCHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle occlusionTextureHandle;
            public RGTextureHandle contactShadowTextureHandle;
            public RGTextureHandle contactShadowMaskHandle;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle atmosphereGGXPrefilterHandle;
            public RGBufferHandle atmosphereSkySHHandle;
        }

        void ComputeDeferredShading(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            ComputeKernel kernel = kernels.deferredShading;
            int2 groupCount = new int2(kernel.GroupsX(width), kernel.GroupsY(height));

            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle gBufferAHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle gBufferCHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            // The feature set decides which optional inputs exist; a recorded producer that did not register its output throws.
            bool hasAO = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
            RGTextureHandle occlusionTextureHandle = hasAO ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer) : default;

            bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);
            RGTextureHandle contactShadowTextureHandle = hasContactShadow ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer) : default;
            RGTextureHandle atmosphereGGXPrefilterHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter);
            RGBufferHandle atmosphereSkySHHandle = m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);
            AtmosphereParameter atmosphereParameter = context.atmosphere;


            var indirectDescriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            { name = "IndirectDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true };
            RGTextureHandle indirectDiffuseHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectDiffuseBuffer, indirectDescriptor);
            indirectDescriptor.name = "IndirectSpecular";
            RGTextureHandle indirectSpecularHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectSpecularBuffer, indirectDescriptor);

            RGTextureHandle scatterDiffuseHandle = default;
            if (context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering))
            {
                indirectDescriptor.name = "ScatterDiffuse";
                scatterDiffuseHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ScatterDiffuseBuffer, indirectDescriptor);
            }

            {
                RGComputePassHandle<DeferredShadingPassData> passHandle = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading));
                ref DeferredShadingPassData passData = ref passHandle.GetPassData();
                passData.bakedDiffuseHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.BakedDiffuseBuffer));
                passData.bakedOcclusionHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.BakedOcclusionBuffer));
                passData.indirectDiffuseHandle = passHandle.WriteTexture(indirectDiffuseHandle);
                passData.indirectSpecularHandle = passHandle.WriteTexture(indirectSpecularHandle);
                passData.hasSubsurface = context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
                if (passData.hasSubsurface)
                {
                    passData.scatterDiffuseHandle = passHandle.WriteTexture(scatterDiffuseHandle);
                }
                passData.kernel = kernel.index;
                passData.groupCount = groupCount;
                passData.parametersHandle = passHandle.WriteConstants(new DeferredShadingParameters
                {
                    resolution = new Vector4(width, height, 1.0f / width, 1.0f / height),
                    farDepth = GraphicsConventions.SampledFarDepth,
                    atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(atmosphereParameter.cubemapSize) - 1
                });
                passData.hasAO = hasAO ? 1 : 0;
                passData.hasContactShadow = hasContactShadow ? 1 : 0;
                LightLoopInputs lightLoop = QueryLightLoop(context);
                lightLoop.Read(passHandle);
                passData.lightLoop = lightLoop;
                passData.lightRecordBufferHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                ShadowInputs shadows = QueryShadowInputs(context);
                shadows.Read(passHandle);
                passData.shadows = shadows;
                passData.deferredShadingShader = kernel.shader;
                passData.subsurfaceKeyword = kernels.deferredShadingSubsurface;
                passData.occlusionKeyword = kernels.deferredShadingOcclusion;
                passData.contactShadowKeyword = kernels.deferredShadingContactShadow;
                passData.gBufferAHandle = passHandle.ReadTexture(gBufferAHandle);
                passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                passData.gBufferCHandle = passHandle.ReadTexture(gBufferCHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                if (hasAO)
                {
                    passData.occlusionTextureHandle = passHandle.ReadTexture(occlusionTextureHandle);
                }
                if (hasContactShadow)
                {
                    passData.contactShadowTextureHandle = passHandle.ReadTexture(contactShadowTextureHandle);
                    passData.contactShadowMaskHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                }
                passData.lightingTextureHandle = passHandle.WriteTexture(lightingTextureHandle);
                passData.atmosphereGGXPrefilterHandle = passHandle.ReadTexture(atmosphereGGXPrefilterHandle);
                passData.atmosphereSkySHHandle = passHandle.ReadBuffer(atmosphereSkySHHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.deferredShadingShader;
                    cmdEncoder.SetKeyword(shader, passData.subsurfaceKeyword, passData.hasSubsurface);
                    if (passData.hasSubsurface)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_ScatterDiffuse, passData.scatterDiffuseHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_BakedDiffuseID, passData.bakedDiffuseHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_BakedOcclusionID, passData.bakedOcclusionHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_IndirectDiffuseID, passData.indirectDiffuseHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_IndirectSpecularID, passData.indirectSpecularHandle);
                    cmdEncoder.SetComputeConstantBufferParam(shader, DeferredShadingPassUtilityData.CBV_DeferredShadingParametersID, passData.parametersHandle);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBufferHandle);
                    passData.shadows.Bind(cmdEncoder, shader, passData.kernel);

                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferAHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferCHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetKeyword(shader, passData.occlusionKeyword, passData.hasAO != 0);
                    if (passData.hasAO != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionTextureHandle);
                    }
                    cmdEncoder.SetKeyword(shader, passData.contactShadowKeyword, passData.hasContactShadow != 0);
                    if (passData.hasContactShadow != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowSunID, passData.contactShadowTextureHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowMaskID, passData.contactShadowMaskHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_LightingTexture, passData.lightingTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, InfinityShaderIDs.AtmosphereGGXPrefilter, passData.atmosphereGGXPrefilterHandle);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, InfinityShaderIDs.AtmosphereSkySH, passData.atmosphereSkySHHandle);

                    cmdEncoder.DispatchCompute(shader, passData.kernel, passData.groupCount.x, passData.groupCount.y, 1);
                });
            }

            context.features.MarkProduced(EFrameFeature.DeferredShading);
        }
    }
}
