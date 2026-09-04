using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ScreenSpaceCompositePassUtilityData
    {
        internal static string TextureName = "ScreenSpaceComposite";
        internal static int Composite_ResolutionID = Shader.PropertyToID("Composite_Resolution");
        internal static int SRV_LightingTextureID = Shader.PropertyToID("SRV_LightingTexture");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static int SRV_SSRTextureID = Shader.PropertyToID("SRV_SSRTexture");
        internal static int SRV_SSGITextureID = Shader.PropertyToID("SRV_SSGITexture");
        internal static int UAV_SceneColorID = Shader.PropertyToID("UAV_SceneColor");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int WorldSpaceCameraPosID = Shader.PropertyToID("_WorldSpaceCameraPos");
        internal const string SSRKeyword = "COMPOSITE_SSR";
        internal const string SSGIKeyword = "COMPOSITE_SSGI";
        internal const string AOKeyword = "COMPOSITE_AO";
    }

    public partial class InfinityRenderPipeline
    {
        struct ScreenSpaceCompositePassData
        {
            public int2 resolution;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public int hasSSR;
            public int hasSSGI;
            public int hasAO;
            public ComputeShader compositeShader;
            public RGTextureRef lightingTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef depthTexture;
            public RGTextureRef occlusionTexture;
            public RGTextureRef ssrTexture;
            public RGTextureRef ssgiTexture;
            public RGTextureRef sceneColorTexture;
        }

        void ComputeScreenSpaceComposite(RenderContext renderContext, Camera camera)
        {
            bool hasSSR = m_RGScoper.TryQueryTexture(InfinityShaderIDs.SSRBuffer, out RGTextureRef ssrTexture);
            bool hasSSGI = m_RGScoper.TryQueryTexture(InfinityShaderIDs.SSGIBuffer, out RGTextureRef ssgiTexture);
            if (!hasSSR && !hasSSGI)
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.screenSpaceCompositeShader, "ScreenSpaceComposite"))
            {
                throw new System.InvalidOperationException("InfinityRP: SSR/SSGI produced this frame but screenSpaceCompositeShader is missing kernel ScreenSpaceComposite.");
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor compositeDsc = new TextureDescriptor(width, height);
            compositeDsc.name = ScreenSpaceCompositePassUtilityData.TextureName;
            compositeDsc.dimension = TextureDimension.Tex2D;
            compositeDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            compositeDsc.depthBufferBits = EDepthBits.None;
            compositeDsc.enableRandomWrite = true;
            RGTextureRef compositeTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ScreenSpaceCompositeBuffer, compositeDsc);

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef gBufferC = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool hasAO = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureRef occlusionTexture);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ScreenSpaceCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceComposite)))
            {
                ref ScreenSpaceCompositePassData passData = ref passRef.GetPassData<ScreenSpaceCompositePassData>();
                passData.resolution = new int2(width, height);
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.hasSSR = hasSSR ? 1 : 0;
                passData.hasSSGI = hasSSGI ? 1 : 0;
                passData.hasAO = hasAO ? 1 : 0;
                passData.compositeShader = pipelineAsset.screenSpaceCompositeShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.gBufferC = passRef.ReadTexture(gBufferC);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                if (hasAO)
                {
                    passData.occlusionTexture = passRef.ReadTexture(occlusionTexture);
                }
                if (hasSSR)
                {
                    passData.ssrTexture = passRef.ReadTexture(ssrTexture);
                }
                if (hasSSGI)
                {
                    passData.ssgiTexture = passRef.ReadTexture(ssgiTexture);
                }
                passData.sceneColorTexture = passRef.WriteTexture(compositeTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ScreenSpaceCompositePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.compositeShader;
                    cmdEncoder.SetComputeVectorParam(shader, ScreenSpaceCompositePassUtilityData.Composite_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeMatrixParam(shader, ScreenSpaceCompositePassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, ScreenSpaceCompositePassUtilityData.WorldSpaceCameraPosID, passData.worldSpaceCameraPos);
                    shader.SetKeyword(new LocalKeyword(shader, ScreenSpaceCompositePassUtilityData.SSRKeyword), passData.hasSSR != 0);
                    shader.SetKeyword(new LocalKeyword(shader, ScreenSpaceCompositePassUtilityData.SSGIKeyword), passData.hasSSGI != 0);
                    shader.SetKeyword(new LocalKeyword(shader, ScreenSpaceCompositePassUtilityData.AOKeyword), passData.hasAO != 0);
                    cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_LightingTextureID, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    if (passData.hasAO != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_OcclusionTextureID, passData.occlusionTexture);
                    }
                    if (passData.hasSSR != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_SSRTextureID, passData.ssrTexture);
                    }
                    if (passData.hasSSGI != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.SRV_SSGITextureID, passData.ssgiTexture);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, 0, ScreenSpaceCompositePassUtilityData.UAV_SceneColorID, passData.sceneColorTexture);
                    cmdEncoder.DispatchCompute(shader, 0, Mathf.CeilToInt(passData.resolution.x / 16.0f), Mathf.CeilToInt(passData.resolution.y / 16.0f), 1);
                });
            }

            m_RGScoper.MoveTexture(InfinityShaderIDs.ScreenSpaceCompositeBuffer, InfinityShaderIDs.OpaqueSceneColorBuffer);
        }
    }
}
