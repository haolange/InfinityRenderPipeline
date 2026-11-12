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
        internal static readonly int SRV_IndirectDiffuseID = Shader.PropertyToID("SRV_IndirectDiffuse");
        internal static readonly int SRV_IndirectSpecularID = Shader.PropertyToID("SRV_IndirectSpecular");
        internal static readonly string TextureName = "ScreenSpaceComposite";
        internal static readonly int Composite_ResolutionID = Shader.PropertyToID("Composite_Resolution");
        internal static readonly int SRV_SSRTextureID = Shader.PropertyToID("SRV_SSRTexture");
        internal static readonly int SRV_SSGITextureID = Shader.PropertyToID("SRV_SSGITexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct ScreenSpaceCompositePassData
        {
            public int2 resolution;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public bool hasSubsurface;
            public RGTextureHandle scatterDiffuse;
            public int hasSSR;
            public int hasSSGI;
            public ComputeShader compositeShader;
            public int kernel;
            public int2 groups;
            public LocalKeyword subsurfaceKeyword, ssrKeyword, ssgiKeyword;
            public RGTextureHandle lightingTexture;
            public RGTextureHandle indirectDiffuse, indirectSpecular;
            public RGTextureHandle gBufferA;
            public RGTextureHandle gBufferB;
            public RGTextureHandle gBufferC;
            public RGTextureHandle depthTexture;
            public RGTextureHandle ssrTexture;
            public RGTextureHandle ssgiTexture;
            public RGTextureHandle sceneColorTexture;
        }

        void ComputeScreenSpaceComposite(in CameraRecordContext context)
        {
            bool hasSSR = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.SSRBuffer, out RGTextureHandle ssrTexture);
            bool hasSSGI = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.SSGIBuffer, out RGTextureHandle ssgiTexture);
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
            compositeDsc.depthBufferBits = EDepthBits.None;
            compositeDsc.enableRandomWrite = true;
            RGTextureHandle compositeTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ScreenSpaceCompositeBuffer, compositeDsc);

            RGTextureHandle lightingTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle gBufferA = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferB = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle gBufferC = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            using (var passRef = m_RGBuilder.AddComputePass<ScreenSpaceCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceComposite)))
            {
                ref ScreenSpaceCompositePassData passData = ref passRef.GetPassData();
                passData.resolution = new int2(width, height);
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = context.camera.transform.position;
                passData.hasSubsurface = context.state.subsurfaceEnabled;
                if (passData.hasSubsurface && hasSSGI)
                {
                    passData.scatterDiffuse = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer);
                    passRef.ReadTexture(passData.scatterDiffuse);
                    passRef.WriteTexture(passData.scatterDiffuse);
                }
                passData.hasSSR = hasSSR ? 1 : 0;
                passData.hasSSGI = hasSSGI ? 1 : 0;
                passData.compositeShader = kernels.screenSpaceComposite.shader;
                passData.kernel = kernels.screenSpaceComposite.index;
                passData.groups = new int2(kernels.screenSpaceComposite.GroupsX(width), kernels.screenSpaceComposite.GroupsY(height));
                passData.subsurfaceKeyword = kernels.compositeSubsurface;
                passData.ssrKeyword = kernels.compositeSSR;
                passData.ssgiKeyword = kernels.compositeSSGI;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.indirectDiffuse = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.IndirectDiffuseBuffer));
                passData.indirectSpecular = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.IndirectSpecularBuffer));
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.gBufferC = passRef.ReadTexture(gBufferC);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
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
                passRef.SetExecuteFunc((in ScreenSpaceCompositePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.compositeShader;
                    cmdEncoder.SetKeyword(shader, passData.subsurfaceKeyword, passData.hasSubsurface);
                    if (passData.hasSubsurface && passData.hasSSGI != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_ScatterDiffuse, passData.scatterDiffuse);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_IndirectDiffuseID, passData.indirectDiffuse);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_IndirectSpecularID, passData.indirectSpecular);
                    cmdEncoder.SetComputeVectorParam(shader, ScreenSpaceCompositePassUtilityData.Composite_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.WorldSpaceCameraPos, passData.worldSpaceCameraPos);
                    cmdEncoder.SetKeyword(shader, passData.ssrKeyword, passData.hasSSR != 0);
                    cmdEncoder.SetKeyword(shader, passData.ssgiKeyword, passData.hasSSGI != 0);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_LightingTexture, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    if (passData.hasSSR != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_SSRTextureID, passData.ssrTexture);
                    }
                    if (passData.hasSSGI != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ScreenSpaceCompositePassUtilityData.SRV_SSGITextureID, passData.ssgiTexture);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_SceneColor, passData.sceneColorTexture);
                    cmdEncoder.DispatchCompute(shader, passData.kernel, passData.groups.x, passData.groups.y, 1);
                });
            }

            m_RGBuilder.scoper.MoveTexture(InfinityShaderIDs.ScreenSpaceCompositeBuffer, InfinityShaderIDs.OpaqueSceneColorBuffer);
        }
    }
}
