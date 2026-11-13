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
    internal static class DeferredShadingPassUtilityData
    {
        internal static readonly int SRV_BakedDiffuseID = Shader.PropertyToID("SRV_BakedDiffuse");
        internal static readonly int SRV_BakedOcclusionID = Shader.PropertyToID("SRV_BakedOcclusion");
        internal static readonly int UAV_IndirectDiffuseID = Shader.PropertyToID("UAV_IndirectDiffuse");
        internal static readonly int UAV_IndirectSpecularID = Shader.PropertyToID("UAV_IndirectSpecular");
        internal static readonly string TextureName = "LightingTexture";
        internal static readonly int DeferredShading_ResolutionID = Shader.PropertyToID("DeferredShading_Resolution");
        internal static readonly int DeferredShading_FarDepthID = Shader.PropertyToID("DeferredShading_FarDepth");
    }

    public partial class InfinityRenderPipeline
    {
        struct DeferredShadingPassData
        {
            public int kernel;
            public int2 groupCount;
            public LightLoopInputs lightLoop;
            public int2 resolution;
            public Vector4 worldSpaceCameraPos;
            public bool hasSubsurface;
            public RGTextureHandle scatterDiffuse;
            public int hasAO;
            public int hasContactShadow;
            public RGBufferHandle lightRecordBuffer;
            public ShadowInputs shadows;
            public ComputeShader deferredShadingShader;
            public LocalKeyword subsurfaceKeyword;
            public LocalKeyword occlusionKeyword;
            public LocalKeyword contactShadowKeyword;
            public RGTextureHandle bakedDiffuse, bakedOcclusion, indirectDiffuse, indirectSpecular;
            public RGTextureHandle gBufferA;
            public RGTextureHandle gBufferB;
            public RGTextureHandle gBufferC;
            public RGTextureHandle depthTexture;
            public RGTextureHandle occlusionTexture;
            public RGTextureHandle contactShadowTexture;
            public RGTextureHandle contactShadowMask;
            public RGTextureHandle lightingTexture;
            public RGTextureHandle atmosphereGGXPrefilter;
            public RGBufferHandle atmosphereSkySH;
            public float atmosphereIBLMaxMip;
        }

        void ComputeDeferredShading(in CameraRecordContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            ComputeKernel kernel = kernels.deferredShading;
            int2 groupCount = new int2(kernel.GroupsX(width), kernel.GroupsY(height));

            RGTextureHandle lightingTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle gBufferA = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferB = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle gBufferC = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            // The feature set decides which optional inputs exist; a recorded producer that did not register its output throws.
            bool hasAO = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
            RGTextureHandle occlusionTexture = hasAO ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer) : default;

            bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);
            RGTextureHandle contactShadowTexture = hasContactShadow ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer) : default;
            RGTextureHandle atmosphereGGXPrefilter = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter);
            RGBufferHandle atmosphereSkySH = m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);
            AtmosphereParameter atmosphereParameter = context.atmosphere;


            var indirectDescriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            { name = "IndirectDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true };
            RGTextureHandle indirectDiffuse = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectDiffuseBuffer, indirectDescriptor);
            indirectDescriptor.name = "IndirectSpecular";
            RGTextureHandle indirectSpecular = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectSpecularBuffer, indirectDescriptor);

            RGTextureHandle scatterDiffuse = default;
            if (context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering))
            {
                indirectDescriptor.name = "ScatterDiffuse";
                scatterDiffuse = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ScatterDiffuseBuffer, indirectDescriptor);
            }

            using (var passRef = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading)))
            {
                ref DeferredShadingPassData passData = ref passRef.GetPassData();
                passData.bakedDiffuse = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.BakedDiffuseBuffer));
                passData.bakedOcclusion = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.BakedOcclusionBuffer));
                passData.indirectDiffuse = passRef.WriteTexture(indirectDiffuse);
                passData.indirectSpecular = passRef.WriteTexture(indirectSpecular);
                passData.hasSubsurface = context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
                if (passData.hasSubsurface)
                {
                    passData.scatterDiffuse = passRef.WriteTexture(scatterDiffuse);
                }
                passData.kernel = kernel.index;
                passData.groupCount = groupCount;
                passData.resolution = new int2(width, height);
                passData.worldSpaceCameraPos = context.camera.transform.position;
                passData.hasAO = hasAO ? 1 : 0;
                passData.hasContactShadow = hasContactShadow ? 1 : 0;
                LightLoopInputs lightLoop = QueryLightLoop(context);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                ShadowInputs shadows = QueryShadowInputs(context);
                shadows.Read(passRef);
                passData.shadows = shadows;
                passData.deferredShadingShader = kernel.shader;
                passData.subsurfaceKeyword = kernels.deferredShadingSubsurface;
                passData.occlusionKeyword = kernels.deferredShadingOcclusion;
                passData.contactShadowKeyword = kernels.deferredShadingContactShadow;
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.gBufferC = passRef.ReadTexture(gBufferC);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                if (hasAO)
                {
                    passData.occlusionTexture = passRef.ReadTexture(occlusionTexture);
                }
                if (hasContactShadow)
                {
                    passData.contactShadowTexture = passRef.ReadTexture(contactShadowTexture);
                    passData.contactShadowMask = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                }
                passData.lightingTexture = passRef.WriteTexture(lightingTexture);
                passData.atmosphereGGXPrefilter = passRef.ReadTexture(atmosphereGGXPrefilter);
                passData.atmosphereSkySH = passRef.ReadBuffer(atmosphereSkySH);
                passData.atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(atmosphereParameter.cubemapSize) - 1;

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.deferredShadingShader;
                    cmdEncoder.SetKeyword(shader, passData.subsurfaceKeyword, passData.hasSubsurface);
                    if (passData.hasSubsurface)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_ScatterDiffuse, passData.scatterDiffuse);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_BakedDiffuseID, passData.bakedDiffuse);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_BakedOcclusionID, passData.bakedOcclusion);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_IndirectDiffuseID, passData.indirectDiffuse);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_IndirectSpecularID, passData.indirectSpecular);
                    cmdEncoder.SetComputeVectorParam(shader, DeferredShadingPassUtilityData.DeferredShading_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(shader, DeferredShadingPassUtilityData.DeferredShading_FarDepthID, GraphicsConventions.SampledFarDepth);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.WorldSpaceCameraPos, passData.worldSpaceCameraPos);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.shadows.Bind(cmdEncoder, shader, passData.kernel);

                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetKeyword(shader, passData.occlusionKeyword, passData.hasAO != 0);
                    if (passData.hasAO != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionTexture);
                    }
                    cmdEncoder.SetKeyword(shader, passData.contactShadowKeyword, passData.hasContactShadow != 0);
                    if (passData.hasContactShadow != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowSunID, passData.contactShadowTexture);
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowMaskID, passData.contactShadowMask);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_LightingTexture, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, InfinityShaderIDs.AtmosphereGGXPrefilter, passData.atmosphereGGXPrefilter);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, InfinityShaderIDs.AtmosphereSkySH, passData.atmosphereSkySH);
                    cmdEncoder.SetComputeFloatParam(shader, InfinityShaderIDs.AtmosphereIBLMaxMip, passData.atmosphereIBLMaxMip);

                    cmdEncoder.DispatchCompute(shader, passData.kernel, passData.groupCount.x, passData.groupCount.y, 1);
                });
            }

            context.features.MarkProduced(EFrameFeature.DeferredShading);
        }
    }
}
