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
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_InvViewProj;
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

        void ComputeDeferredShading(RenderContext renderContext, Camera camera)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            ComputeKernel kernel = kernels.deferredShading;
            int2 groupCount = new int2(kernel.GroupsX(width), kernel.GroupsY(height));

            RGTextureHandle lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle gBufferC = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool hasAO = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureHandle occlusionTexture);
            if ((ShouldRecordFeature(EFrameFeature.GTAO) || ShouldRecordFeature(EFrameFeature.RTAO)) && !hasAO)
            {
                throw new System.InvalidOperationException("InfinityRP: AO is recorded this frame but OcclusionBuffer is not registered.");
            }

            bool hasContactShadow = m_RGScoper.TryQueryTexture(InfinityShaderIDs.ContactShadowBuffer, out RGTextureHandle contactShadowTexture);
            if (ShouldRecordFeature(EFrameFeature.ContactShadow) && !hasContactShadow)
            {
                throw new System.InvalidOperationException("InfinityRP: ContactShadow is recorded this frame but ContactShadowBuffer is not registered.");
            }
            RGTextureHandle atmosphereGGXPrefilter = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter);
            RGBufferHandle atmosphereSkySH = m_RGScoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);
            AtmosphereParameter atmosphereParameter = AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile);
            atmosphereParameter.ThrowIfInvalid();


            var indirectDescriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            { name = "IndirectDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true };
            RGTextureHandle indirectDiffuse = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectDiffuseBuffer, indirectDescriptor);
            indirectDescriptor.name = "IndirectSpecular";
            RGTextureHandle indirectSpecular = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectSpecularBuffer, indirectDescriptor);

            m_ActiveFrameState.subsurfaceEnabled = ShouldRecordFeature(EFrameFeature.SubsurfaceScattering);
            RGTextureHandle scatterDiffuse = default;
            if (m_ActiveFrameState.subsurfaceEnabled)
            {
                indirectDescriptor.name = "ScatterDiffuse";
                scatterDiffuse = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ScatterDiffuseBuffer, indirectDescriptor);
            }

            using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading)))
            {
                ref DeferredShadingPassData passData = ref passRef.GetPassData<DeferredShadingPassData>();
                passData.bakedDiffuse = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.BakedDiffuseBuffer));
                passData.bakedOcclusion = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.BakedOcclusionBuffer));
                passData.indirectDiffuse = passRef.WriteTexture(indirectDiffuse);
                passData.indirectSpecular = passRef.WriteTexture(indirectSpecular);
                passData.hasSubsurface = m_ActiveFrameState.subsurfaceEnabled;
                if (passData.hasSubsurface)
                {
                    passData.scatterDiffuse = passRef.WriteTexture(scatterDiffuse);
                }
                passData.kernel = kernel.index;
                passData.groupCount = groupCount;
                passData.resolution = new int2(width, height);
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.hasAO = hasAO ? 1 : 0;
                passData.hasContactShadow = hasContactShadow ? 1 : 0;
                LightLoopInputs lightLoop = QueryLightLoop(renderContext);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                ShadowInputs shadows = QueryShadowInputs(renderContext);
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
                    passData.contactShadowMask = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                }
                passData.lightingTexture = passRef.WriteTexture(lightingTexture);
                passData.atmosphereGGXPrefilter = passRef.ReadTexture(atmosphereGGXPrefilter);
                passData.atmosphereSkySH = passRef.ReadBuffer(atmosphereSkySH);
                passData.atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(atmosphereParameter.cubemapSize) - 1;

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.deferredShadingShader;
                    shader.SetKeyword(passData.subsurfaceKeyword, passData.hasSubsurface);
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
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvProj, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.WorldSpaceCameraPos, passData.worldSpaceCameraPos);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.shadows.Bind(cmdEncoder, shader, passData.kernel);

                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    shader.SetKeyword(passData.occlusionKeyword, passData.hasAO != 0);
                    if (passData.hasAO != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionTexture);
                    }
                    shader.SetKeyword(passData.contactShadowKeyword, passData.hasContactShadow != 0);
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

            MarkFeatureProduced(EFrameFeature.DeferredShading);
        }
    }
}
