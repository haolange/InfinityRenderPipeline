using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SSGIPassUtilityData
    {
        internal static string TextureName = "SSGITexture";

        // Property IDs matching HLSL declarations in Compute_ScreenSpaceIndirectDiffuse.compute
        // Note: shader uses SSGi_ prefix (not SSGI_)
        internal static int SSGi_TraceResolutionID = Shader.PropertyToID("SSGi_TraceResolution");
        internal static int SSGi_NumRaysID = Shader.PropertyToID("SSGi_NumRays");
        internal static int SSGi_NumStepsID = Shader.PropertyToID("SSGi_NumSteps");
        internal static int SSGi_IntensityID = Shader.PropertyToID("SSGi_Intensity");
        internal static int SSGi_FrameIndexID = Shader.PropertyToID("SSGi_FrameIndex");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static int Matrix_ViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static int SRV_PyramidDepthID = Shader.PropertyToID("SRV_PyramidDepth");
        internal static int SRV_PyramidColorID = Shader.PropertyToID("SRV_PyramidColor");
        internal static int SRV_SceneDepthID = Shader.PropertyToID("SRV_SceneDepth");
        internal static int SRV_GBufferNormalID = Shader.PropertyToID("SRV_GBufferNormal");
        internal static int UAV_ScreenIrradianceID = Shader.PropertyToID("UAV_ScreenIrradiance");

        internal static int RaytracingKernel = 0;
    }

    public partial class InfinityRenderPipeline
    {
        struct SSGIPassData
        {
            public int numRays;
            public int numSteps;
            public float intensity;
            public int frameIndex;
            public int2 resolution;
            public Matrix4x4 matrix_Proj;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public ComputeShader ssgiShader;
            public RGTextureRef hiZTexture;
            public RGTextureRef colorPyramidTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef depthTexture;
            public RGTextureRef ssgiTexture;
        }

        void ComputeScreenSpaceIndirect(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var ssgi = stack.GetComponent<ScreenSpaceIndirectDiffuse>();
            if (ssgi == null) return;

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor ssgiTextureDsc = new TextureDescriptor(width, height);
            {
                ssgiTextureDsc.name = SSGIPassUtilityData.TextureName;
                ssgiTextureDsc.dimension = TextureDimension.Tex2D;
                ssgiTextureDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                ssgiTextureDsc.depthBufferBits = EDepthBits.None;
                ssgiTextureDsc.enableRandomWrite = true;
            }
            RGTextureRef ssgiTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIBuffer, ssgiTextureDsc);

            RGTextureRef hiZTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HiZBuffer);
            RGTextureRef colorPyramidTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.ColorPyramidBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add SSGIPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SSGIPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceIndirect)))
            {
                //Setup Phase
                ref SSGIPassData passData = ref passRef.GetPassData<SSGIPassData>();
                passData.numRays = ssgi.NumRays.value;
                passData.numSteps = ssgi.NumSteps.value;
                passData.intensity = ssgi.IntensityScale.value;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(width, height);
                passData.matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.matrix_InvProj = passData.matrix_Proj.inverse;
                passData.matrix_ViewProj = passData.matrix_Proj * camera.worldToCameraMatrix;
                passData.matrix_InvViewProj = passData.matrix_ViewProj.inverse;
                passData.matrix_WorldToView = camera.worldToCameraMatrix;
                passData.ssgiShader = pipelineAsset.ssgiShader;
                passData.hiZTexture = passRef.ReadTexture(hiZTexture);
                passData.colorPyramidTexture = passRef.ReadTexture(colorPyramidTexture);
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.ssgiTexture = passRef.WriteTexture(ssgiTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SSGIPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.ssgiShader == null) return;

                    // Set uniforms matching HLSL declarations (SSGi_ prefix)
                    cmdEncoder.SetComputeVectorParam(passData.ssgiShader, SSGIPassUtilityData.SSGi_TraceResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(passData.ssgiShader, SSGIPassUtilityData.SSGi_NumRaysID, passData.numRays);
                    cmdEncoder.SetComputeIntParam(passData.ssgiShader, SSGIPassUtilityData.SSGi_NumStepsID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(passData.ssgiShader, SSGIPassUtilityData.SSGi_IntensityID, passData.intensity);
                    cmdEncoder.SetComputeIntParam(passData.ssgiShader, SSGIPassUtilityData.SSGi_FrameIndexID, passData.frameIndex);

                    cmdEncoder.SetComputeMatrixParam(passData.ssgiShader, SSGIPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssgiShader, SSGIPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssgiShader, SSGIPassUtilityData.Matrix_ViewProjID, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssgiShader, SSGIPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssgiShader, SSGIPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);

                    // Kernel 0: Raytracing
                    int kernel = SSGIPassUtilityData.RaytracingKernel;
                    cmdEncoder.SetComputeTextureParam(passData.ssgiShader, kernel, SSGIPassUtilityData.SRV_PyramidDepthID, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssgiShader, kernel, SSGIPassUtilityData.SRV_PyramidColorID, passData.colorPyramidTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssgiShader, kernel, SSGIPassUtilityData.SRV_SceneDepthID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssgiShader, kernel, SSGIPassUtilityData.SRV_GBufferNormalID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(passData.ssgiShader, kernel, SSGIPassUtilityData.UAV_ScreenIrradianceID, passData.ssgiTexture);

                    // Shader uses [numthreads(16, 16, 1)]
                    cmdEncoder.DispatchCompute(passData.ssgiShader, kernel, Mathf.CeilToInt(passData.resolution.x / 16.0f), Mathf.CeilToInt(passData.resolution.y / 16.0f), 1);
                });
            }
        }
    }
}
