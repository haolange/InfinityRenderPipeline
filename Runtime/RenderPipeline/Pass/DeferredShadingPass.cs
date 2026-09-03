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
        internal static int DeferredShading_TileSizeID = Shader.PropertyToID("DeferredShading_TileSize");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static int SRV_ContactShadowTextureID = Shader.PropertyToID("SRV_ContactShadowTexture");
        internal static int SRV_SSRTextureID = Shader.PropertyToID("SRV_SSRTexture");
        internal static int SRV_SSGITextureID = Shader.PropertyToID("SRV_SSGITexture");
        internal static int SRV_CascadeShadowMapID = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static int UAV_LightingTextureID = Shader.PropertyToID("UAV_LightingTexture");
        internal static int DeferredShading_FarDepthID = Shader.PropertyToID("DeferredShading_FarDepth");
    }

    public partial class InfinityRenderPipeline
    {
        struct DeferredShadingPassData
        {
            public int tileSize;
            public int2 resolution;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public int directionalLightCount;
            public GraphicsBuffer directionalLightBuffer;
            public int cascadeCount;
            public Matrix4x4[] cascadeMatrices;
            public Vector4 cascadeSplitDistances;
            public ComputeShader deferredShadingShader;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef depthTexture;
            public RGTextureRef occlusionTexture;
            public RGTextureRef contactShadowTexture;
            public RGTextureRef ssrTexture;
            public RGTextureRef ssgiTexture;
            public RGTextureRef cascadeShadowMap;
            public RGTextureRef lightingTexture;
        }

        void ComputeDeferredShading(RenderContext renderContext, Camera camera)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.deferredShadingShader, "DeferredShadingCS"))
            {
                throw new System.InvalidOperationException("InfinityRP: Deferred shading is the LightingBuffer producer but deferredShadingShader is missing or kernel DeferredShadingCS is invalid.");
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int tileSize = 16;

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef gBufferC = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            // TODO: TryQuery optional AO/SSR/SSGI when FeatureSet says they were not produced.
            // Lighting still samples these SRVs unconditionally, so keep Query while those paths always record.
            RGTextureRef occlusionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer);
            RGTextureRef contactShadowTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer);
            RGTextureRef ssrTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.SSRBuffer);
            RGTextureRef ssgiTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.SSGIBuffer);
            RGTextureRef cascadeShadowMap = m_RGScoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap);

            //Add DeferredShadingPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading)))
            {
                //Setup Phase
                ref DeferredShadingPassData passData = ref passRef.GetPassData<DeferredShadingPassData>();
                passData.tileSize = tileSize;
                passData.resolution = new int2(width, height);
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.directionalLightCount = renderContext.lightContext.DirectionalLightCount;
                passData.directionalLightBuffer = renderContext.lightContext.DirectionalLightBuffer;
                passData.cascadeCount = m_ActiveCascadeCount;
                passData.cascadeMatrices = m_ActiveCascadeMatrices;
                passData.cascadeSplitDistances = m_ActiveCascadeSplitDistances;
                passData.deferredShadingShader = pipelineAsset.deferredShadingShader;
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.gBufferC = passRef.ReadTexture(gBufferC);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.occlusionTexture = passRef.ReadTexture(occlusionTexture);
                passData.contactShadowTexture = passRef.ReadTexture(contactShadowTexture);
                passData.ssrTexture = passRef.ReadTexture(ssrTexture);
                passData.ssgiTexture = passRef.ReadTexture(ssgiTexture);
                passData.cascadeShadowMap = passRef.ReadTexture(cascadeShadowMap);
                passData.lightingTexture = passRef.WriteTexture(lightingTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeVectorParam(passData.deferredShadingShader, DeferredShadingPassUtilityData.DeferredShading_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(passData.deferredShadingShader, DeferredShadingPassUtilityData.DeferredShading_TileSizeID, passData.tileSize);
                    cmdEncoder.SetComputeFloatParam(passData.deferredShadingShader, DeferredShadingPassUtilityData.DeferredShading_FarDepthID, GraphicsUtility.SampledFarDepth);
                    cmdEncoder.SetComputeMatrixParam(passData.deferredShadingShader, Shader.PropertyToID("Matrix_InvProj"), passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(passData.deferredShadingShader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(passData.deferredShadingShader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeIntParam(passData.deferredShadingShader, LightShaderIDs.DirectionalLightCount, passData.directionalLightCount);
                    cmdEncoder.SetComputeBufferParam(passData.deferredShadingShader, 0, LightShaderIDs.DirectionalLightBuffer, passData.directionalLightBuffer);
                    cmdEncoder.SetComputeIntParam(passData.deferredShadingShader, CascadeShadowPassUtilityData.CascadeCountID, passData.cascadeCount);
                    cmdEncoder.SetComputeMatrixArrayParam(passData.deferredShadingShader, CascadeShadowPassUtilityData.CascadeMatricesID, passData.cascadeMatrices);
                    cmdEncoder.SetComputeVectorParam(passData.deferredShadingShader, CascadeShadowPassUtilityData.CascadeSplitDistancesID, passData.cascadeSplitDistances);

                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_OcclusionTextureID, passData.occlusionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_ContactShadowTextureID, passData.contactShadowTexture);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_SSRTextureID, passData.ssrTexture);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_SSGITextureID, passData.ssgiTexture);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_CascadeShadowMapID, passData.cascadeShadowMap);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.UAV_LightingTextureID, passData.lightingTexture);
                    cmdEncoder.DispatchCompute(passData.deferredShadingShader, 0, Mathf.CeilToInt(passData.resolution.x / (float)passData.tileSize), Mathf.CeilToInt(passData.resolution.y / (float)passData.tileSize), 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.DeferredShading);
        }
    }
}
