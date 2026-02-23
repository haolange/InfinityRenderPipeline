using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SSRPassUtilityData
    {
        internal static string TextureName = "SSRTexture";
        internal static string HitPDFTextureName = "SSRHitPDFTexture";

        // Property IDs matching HLSL declarations in Compute_ScreenSpaceReflection.compute
        internal static int SSR_ResolutionID = Shader.PropertyToID("SSR_Resolution");
        internal static int SSR_NumRaysID = Shader.PropertyToID("SSR_NumRays");
        internal static int SSR_NumStepsID = Shader.PropertyToID("SSR_NumSteps");
        internal static int SSR_BRDFBiasID = Shader.PropertyToID("SSR_BRDFBias");
        internal static int SSR_FadenessID = Shader.PropertyToID("SSR_Fadeness");
        internal static int SSR_RoughnessID = Shader.PropertyToID("SSR_Roughness");
        internal static int SSR_FrameIndexID = Shader.PropertyToID("SSR_FrameIndex");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static int Matrix_ViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static int SRV_HiZTextureID = Shader.PropertyToID("SRV_HiZTexture");
        internal static int SRV_HiCTextureID = Shader.PropertyToID("SRV_HiCTexture");
        internal static int SRV_NormalTextureID = Shader.PropertyToID("SRV_NormalTexture");
        internal static int SRV_RoughnessTextureID = Shader.PropertyToID("SRV_RoughnessTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int UAV_HitPDFTextureID = Shader.PropertyToID("UAV_HitPDFTexture");
        internal static int UAV_ColorMaskTextureID = Shader.PropertyToID("UAV_ColorMaskTexture");

        internal static int RaytracingKernel = 0;
    }

    public partial class InfinityRenderPipeline
    {
        struct SSRPassData
        {
            public int numRays;
            public int numSteps;
            public float brdfBias;
            public float fadeness;
            public float maxRoughness;
            public int frameIndex;
            public int2 resolution;
            public Matrix4x4 matrix_Proj;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public ComputeShader ssrShader;
            public RGTextureRef hiZTexture;
            public RGTextureRef colorPyramidTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef depthTexture;
            public RGTextureRef ssrTexture;
            public RGTextureRef hitPdfTexture;
        }

        void ComputeScreenSpaceReflection(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var ssr = stack.GetComponent<ScreenSpaceReflection>();
            if (ssr == null) return;

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            // Main SSR output (color mask from raytracing)
            TextureDescriptor ssrTextureDsc = new TextureDescriptor(width, height);
            {
                ssrTextureDsc.name = SSRPassUtilityData.TextureName;
                ssrTextureDsc.dimension = TextureDimension.Tex2D;
                ssrTextureDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                ssrTextureDsc.depthBufferBits = EDepthBits.None;
                ssrTextureDsc.enableRandomWrite = true;
            }
            RGTextureRef ssrTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRBuffer, ssrTextureDsc);

            // Intermediate HitPDF texture (used by raytracing kernel)
            TextureDescriptor hitPdfTextureDsc = new TextureDescriptor(width, height);
            {
                hitPdfTextureDsc.name = SSRPassUtilityData.HitPDFTextureName;
                hitPdfTextureDsc.dimension = TextureDimension.Tex2D;
                hitPdfTextureDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                hitPdfTextureDsc.depthBufferBits = EDepthBits.None;
                hitPdfTextureDsc.enableRandomWrite = true;
            }
            RGTextureRef hitPdfTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitPDFBuffer, hitPdfTextureDsc);

            RGTextureRef hiZTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HiZBuffer);
            RGTextureRef colorPyramidTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.ColorPyramidBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add SSRPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SSRPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceReflection)))
            {
                //Setup Phase
                ref SSRPassData passData = ref passRef.GetPassData<SSRPassData>();
                passData.numRays = ssr.NumRays.value;
                passData.numSteps = ssr.NumSteps.value;
                passData.brdfBias = ssr.BrdfBias.value;
                passData.fadeness = ssr.Fadeness.value;
                passData.maxRoughness = ssr.MaxRoughness.value;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(width, height);
                passData.matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.matrix_InvProj = passData.matrix_Proj.inverse;
                passData.matrix_ViewProj = passData.matrix_Proj * camera.worldToCameraMatrix;
                passData.matrix_InvViewProj = passData.matrix_ViewProj.inverse;
                passData.matrix_WorldToView = camera.worldToCameraMatrix;
                passData.ssrShader = pipelineAsset.ssrShader;
                passData.hiZTexture = passRef.ReadTexture(hiZTexture);
                passData.colorPyramidTexture = passRef.ReadTexture(colorPyramidTexture);
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.ssrTexture = passRef.WriteTexture(ssrTexture);
                passData.hitPdfTexture = passRef.WriteTexture(hitPdfTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SSRPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.ssrShader == null) return;

                    // Set uniforms matching HLSL declarations
                    cmdEncoder.SetComputeVectorParam(passData.ssrShader, SSRPassUtilityData.SSR_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(passData.ssrShader, SSRPassUtilityData.SSR_NumRaysID, passData.numRays);
                    cmdEncoder.SetComputeIntParam(passData.ssrShader, SSRPassUtilityData.SSR_NumStepsID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(passData.ssrShader, SSRPassUtilityData.SSR_BRDFBiasID, passData.brdfBias);
                    cmdEncoder.SetComputeFloatParam(passData.ssrShader, SSRPassUtilityData.SSR_FadenessID, passData.fadeness);
                    cmdEncoder.SetComputeFloatParam(passData.ssrShader, SSRPassUtilityData.SSR_RoughnessID, passData.maxRoughness);
                    cmdEncoder.SetComputeIntParam(passData.ssrShader, SSRPassUtilityData.SSR_FrameIndexID, passData.frameIndex);

                    cmdEncoder.SetComputeMatrixParam(passData.ssrShader, SSRPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssrShader, SSRPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssrShader, SSRPassUtilityData.Matrix_ViewProjID, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssrShader, SSRPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssrShader, SSRPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);

                    // Kernel 0: Raytracing - outputs UAV_HitPDFTexture + UAV_ColorMaskTexture
                    int kernel = SSRPassUtilityData.RaytracingKernel;
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.SRV_HiZTextureID, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.SRV_HiCTextureID, passData.colorPyramidTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.SRV_NormalTextureID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.SRV_RoughnessTextureID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.UAV_HitPDFTextureID, passData.hitPdfTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssrShader, kernel, SSRPassUtilityData.UAV_ColorMaskTextureID, passData.ssrTexture);

                    // Shader uses [numthreads(16, 16, 1)]
                    cmdEncoder.DispatchCompute(passData.ssrShader, kernel, Mathf.CeilToInt(passData.resolution.x / 16.0f), Mathf.CeilToInt(passData.resolution.y / 16.0f), 1);
                });
            }
        }
    }
}
