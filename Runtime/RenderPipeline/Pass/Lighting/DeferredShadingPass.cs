using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DeferredShadingPassUtilityData
    {
        internal static string TextureName = "LightingTexture";
        internal static int DeferredShading_ResolutionID = Shader.PropertyToID("DeferredShading_Resolution");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal const string AOKeyword = "DEFERRED_AO";
        internal const string ContactShadowKeyword = "DEFERRED_CONTACT_SHADOW";
        internal static int UAV_LightingTextureID = Shader.PropertyToID("UAV_LightingTexture");
        internal static int DeferredShading_FarDepthID = Shader.PropertyToID("DeferredShading_FarDepth");
    }

    public partial class InfinityRenderPipeline
    {
        struct DeferredShadingPassData
        {
            public int kernel;
            public int2 groupCount;
            public FLightLoopInputs lightLoop;
            public int2 resolution;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public int hasAO;
            public int hasContactShadow;
            public RGBufferRef lightRecordBuffer;
            public FShadowInputs shadows;
            public ComputeShader deferredShadingShader;
            public RGTextureRef bakedDiffuse, bakedOcclusion, indirectDiffuse, indirectSpecular;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef depthTexture;
            public RGTextureRef occlusionTexture;
            public RGTextureRef contactShadowTexture;
            public RGTextureRef contactShadowMask;
            public RGTextureRef lightingTexture;
            public RGTextureRef atmosphereGGXPrefilter;
            public RGBufferRef atmosphereSkySH;
            public float atmosphereIBLMaxMip;
        }

        void ComputeDeferredShading(RenderContext renderContext, Camera camera)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            if (!GraphicsUtility.HasRequiredKernels(shaders.deferredShadingShader, "DeferredShadingCS"))
            {
                throw new System.InvalidOperationException("InfinityRP: Deferred shading is the LightingBuffer producer but deferredShadingShader is missing or kernel DeferredShadingCS is invalid.");
            }

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            int kernel = shaders.deferredShadingShader.FindKernel("DeferredShadingCS");
            shaders.deferredShadingShader.GetKernelThreadGroupSizes(kernel, out uint threadsX, out uint threadsY, out uint threadsZ);
            if (threadsX == 0 || threadsY == 0 || threadsZ != 1)
                throw new System.InvalidOperationException("InfinityRP: DeferredShadingCS requires a nonempty 2D thread group.");
            int2 groupCount = new int2(Mathf.CeilToInt(width / (float)threadsX), Mathf.CeilToInt(height / (float)threadsY));

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef gBufferC = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool hasAO = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureRef occlusionTexture);
            if ((ShouldRecordFeature(EFrameFeature.GTAO) || ShouldRecordFeature(EFrameFeature.RTAO)) && !hasAO)
            {
                throw new System.InvalidOperationException("InfinityRP: AO is recorded this frame but OcclusionBuffer is not registered.");
            }

            bool hasContactShadow = m_RGScoper.TryQueryTexture(InfinityShaderIDs.ContactShadowBuffer, out RGTextureRef contactShadowTexture);
            if (ShouldRecordFeature(EFrameFeature.ContactShadow) && !hasContactShadow)
            {
                throw new System.InvalidOperationException("InfinityRP: ContactShadow is recorded this frame but ContactShadowBuffer is not registered.");
            }
            RGTextureRef atmosphereGGXPrefilter = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter);
            RGBufferRef atmosphereSkySH = m_RGScoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);
            AtmosphereParameter atmosphereParameter = AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile);
            atmosphereParameter.ThrowIfInvalid();


            var indirectDescriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            { name = "IndirectDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true };
            RGTextureRef indirectDiffuse = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectDiffuseBuffer, indirectDescriptor);
            indirectDescriptor.name = "IndirectSpecular";
            RGTextureRef indirectSpecular = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectSpecularBuffer, indirectDescriptor);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading)))
            {
                ref DeferredShadingPassData passData = ref passRef.GetPassData<DeferredShadingPassData>();
                passData.bakedDiffuse = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.BakedDiffuseBuffer));
                passData.bakedOcclusion = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.BakedOcclusionBuffer));
                passData.indirectDiffuse = passRef.WriteTexture(indirectDiffuse);
                passData.indirectSpecular = passRef.WriteTexture(indirectSpecular);
                passData.kernel = kernel;
                passData.groupCount = groupCount;
                passData.resolution = new int2(width, height);
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.hasAO = hasAO ? 1 : 0;
                passData.hasContactShadow = hasContactShadow ? 1 : 0;
                FLightLoopInputs lightLoop = QueryLightLoop(renderContext);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                FShadowInputs shadows = QueryShadowInputs(renderContext);
                shadows.Read(passRef);
                passData.shadows = shadows;
                passData.deferredShadingShader = shaders.deferredShadingShader;
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
                passRef.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.deferredShadingShader;
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, Shader.PropertyToID("SRV_BakedDiffuse"), passData.bakedDiffuse);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, Shader.PropertyToID("SRV_BakedOcclusion"), passData.bakedOcclusion);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, Shader.PropertyToID("UAV_IndirectDiffuse"), passData.indirectDiffuse);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, Shader.PropertyToID("UAV_IndirectSpecular"), passData.indirectSpecular);
                    cmdEncoder.SetComputeVectorParam(shader, DeferredShadingPassUtilityData.DeferredShading_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(shader, DeferredShadingPassUtilityData.DeferredShading_FarDepthID, GraphicsUtility.SampledFarDepth);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvProj"), passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.shadows.Bind(cmdEncoder, shader, passData.kernel);

                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    shader.SetKeyword(new LocalKeyword(shader, DeferredShadingPassUtilityData.AOKeyword), passData.hasAO != 0);
                    if (passData.hasAO != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_OcclusionTextureID, passData.occlusionTexture);
                    }
                    shader.SetKeyword(new LocalKeyword(shader, DeferredShadingPassUtilityData.ContactShadowKeyword), passData.hasContactShadow != 0);
                    if (passData.hasContactShadow != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowSunID, passData.contactShadowTexture);
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowMaskID, passData.contactShadowMask);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_LightingTextureID, passData.lightingTexture);
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
