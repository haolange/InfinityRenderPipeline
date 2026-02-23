using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DeferredShadingPassUtilityData
    {
        internal static string TextureName = "LightingTexture";
        internal static int DeferredShading_ResolutionID = Shader.PropertyToID("DeferredShading_Resolution");
        internal static int DeferredShading_TileSizeID = Shader.PropertyToID("DeferredShading_TileSize");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static int SRV_ContactShadowTextureID = Shader.PropertyToID("SRV_ContactShadowTexture");
        internal static int SRV_SSRTextureID = Shader.PropertyToID("SRV_SSRTexture");
        internal static int SRV_SSGITextureID = Shader.PropertyToID("SRV_SSGITexture");
        internal static int SRV_CascadeShadowMapID = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static int UAV_LightingTextureID = Shader.PropertyToID("UAV_LightingTexture");
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
            public ComputeShader deferredShadingShader;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
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
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int tileSize = 16;

            TextureDescriptor lightingTextureDsc = new TextureDescriptor(width, height);
            {
                lightingTextureDsc.name = DeferredShadingPassUtilityData.TextureName;
                lightingTextureDsc.dimension = TextureDimension.Tex2D;
                lightingTextureDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                lightingTextureDsc.depthBufferBits = EDepthBits.None;
                lightingTextureDsc.enableRandomWrite = true;
            }
            RGTextureRef lightingTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, lightingTextureDsc);

            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
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
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.matrix_InvProj = gpuProj.inverse;
                passData.matrix_InvViewProj = (gpuProj * camera.worldToCameraMatrix).inverse;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.deferredShadingShader = pipelineAsset.deferredShadingShader;
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
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
                    if (passData.deferredShadingShader == null) return;

                    cmdEncoder.SetComputeVectorParam(passData.deferredShadingShader, DeferredShadingPassUtilityData.DeferredShading_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(passData.deferredShadingShader, DeferredShadingPassUtilityData.DeferredShading_TileSizeID, passData.tileSize);
                    cmdEncoder.SetComputeMatrixParam(passData.deferredShadingShader, Shader.PropertyToID("Matrix_InvProj"), passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(passData.deferredShadingShader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(passData.deferredShadingShader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);

                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(passData.deferredShadingShader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
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
        }
    }
}
