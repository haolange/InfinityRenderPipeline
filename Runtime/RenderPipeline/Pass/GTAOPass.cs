using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class GTAOPassUtilityData
    {
        internal static string OcclusionTextureName = "OcclusionTexture";
        internal static string SpatialTempTextureName = "SpatialTempTexture";
        internal static int OcclusionTraceKernel = 0;
        internal static int OcclusionSpatialXKernel = 1;
        internal static int OcclusionSpatialYKernel = 2;
        internal static int OcclusionTemporalKernel = 3;
        internal static int OcclusionUpsampleKernel = 4;

        // Property IDs matching HLSL cbuffer CBV_OcclusionUnifrom
        internal static int NumRayID = Shader.PropertyToID("NumRay");
        internal static int NumStepID = Shader.PropertyToID("NumStep");
        internal static int PowerID = Shader.PropertyToID("Power");
        internal static int RadiusID = Shader.PropertyToID("Radius");
        internal static int IntensityID = Shader.PropertyToID("Intensity");
        internal static int SharpenessID = Shader.PropertyToID("Sharpeness");
        internal static int HalfProjScaleID = Shader.PropertyToID("HalfProjScale");
        internal static int TemporalOffsetID = Shader.PropertyToID("TemporalOffset");
        internal static int TemporalDirectionID = Shader.PropertyToID("TemporalDirection");
        internal static int TemporalScaleID = Shader.PropertyToID("TemporalScale");
        internal static int TemporalWeightID = Shader.PropertyToID("TemporalWeight");
        internal static int ResolutionID = Shader.PropertyToID("Resolution");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static int Matrix_ViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_NormalTextureID = Shader.PropertyToID("SRV_NormalTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static int UAV_OcclusionTextureID = Shader.PropertyToID("UAV_OcclusionTexture");
        internal static int UAV_SpatialTextureID = Shader.PropertyToID("UAV_SpatialTexture");

        // Temporal jitter patterns (cycled every 4 frames)
        internal static readonly float[] TemporalOffsets = { 0.0f, 0.5f, 0.25f, 0.75f };
        internal static readonly float[] TemporalDirections = { 0.0f, 0.5f, 0.25f, 0.75f };
    }

    public partial class InfinityRenderPipeline
    {
        struct GTAOPassData
        {
            public int numRays;
            public int numSteps;
            public float power;
            public float radius;
            public float intensity;
            public float sharpeness;
            public float halfProjScale;
            public float temporalOffset;
            public float temporalDirection;
            public float temporalScale;
            public float temporalWeight;
            public int2 halfResolution;
            public Matrix4x4 matrix_Proj;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public ComputeShader ssaoShader;
            public RGTextureRef halfResDepthTexture;
            public RGTextureRef halfResNormalTexture;
            public RGTextureRef occlusionTexture;
            public RGTextureRef spatialTempTexture;
        }

        void ComputeGroundTruthOcclusion(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var ssao = stack.GetComponent<ScreenSpaceAmbientOcclusion>();
            if (ssao == null) return;

            int fullWidth = camera.pixelWidth;
            int fullHeight = camera.pixelHeight;
            int halfWidth = Mathf.Max(1, fullWidth >> 1);
            int halfHeight = Mathf.Max(1, fullHeight >> 1);

            // Occlusion at half resolution (deferred shading samples with bilinear)
            TextureDescriptor occlusionTextureDsc = new TextureDescriptor(halfWidth, halfHeight);
            {
                occlusionTextureDsc.name = GTAOPassUtilityData.OcclusionTextureName;
                occlusionTextureDsc.dimension = TextureDimension.Tex2D;
                occlusionTextureDsc.colorFormat = GraphicsFormat.R8_UNorm;
                occlusionTextureDsc.depthBufferBits = EDepthBits.None;
                occlusionTextureDsc.enableRandomWrite = true;
            }
            RGTextureRef occlusionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionTextureDsc);

            // Temporary spatial filter texture for ping-pong
            TextureDescriptor spatialTempDsc = new TextureDescriptor(halfWidth, halfHeight);
            {
                spatialTempDsc.name = GTAOPassUtilityData.SpatialTempTextureName;
                spatialTempDsc.dimension = TextureDimension.Tex2D;
                spatialTempDsc.colorFormat = GraphicsFormat.R8_UNorm;
                spatialTempDsc.depthBufferBits = EDepthBits.None;
                spatialTempDsc.enableRandomWrite = true;
            }
            RGTextureRef spatialTempTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SpatialTempBuffer, spatialTempDsc);

            RGTextureRef halfResDepthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HalfResDepthBuffer);
            RGTextureRef halfResNormalTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HalfResNormalBuffer);

            //Add GTAOPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<GTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeGroundTruthOcclusion)))
            {
                //Setup Phase
                ref GTAOPassData passData = ref passRef.GetPassData<GTAOPassData>();
                passData.numRays = ssao.NumRays.value;
                passData.numSteps = ssao.NumSteps.value;
                passData.power = ssao.Power.value;
                passData.radius = ssao.Radius.value;
                passData.intensity = ssao.Intensity.value;
                passData.sharpeness = ssao.Sharpeness.value;
                passData.temporalScale = ssao.TemporalScale.value;
                passData.temporalWeight = ssao.TemporalWeight.value;
                passData.halfResolution = new int2(halfWidth, halfHeight);

                // Compute HalfProjScale: projMatrix[1,1] * halfHeight * 0.5
                Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.halfProjScale = projMatrix.m11 * halfHeight * 0.5f;

                // Compute temporal jitter from frame index
                int frameIndex = Time.frameCount;
                int temporalIndex = frameIndex & 3;
                passData.temporalOffset = GTAOPassUtilityData.TemporalOffsets[temporalIndex];
                passData.temporalDirection = GTAOPassUtilityData.TemporalDirections[temporalIndex];

                passData.matrix_Proj = projMatrix;
                passData.matrix_InvProj = projMatrix.inverse;
                passData.matrix_ViewProj = projMatrix * camera.worldToCameraMatrix;
                passData.matrix_InvViewProj = passData.matrix_ViewProj.inverse;
                passData.matrix_WorldToView = camera.worldToCameraMatrix;
                passData.ssaoShader = pipelineAsset.ssaoShader;
                passData.halfResDepthTexture = passRef.ReadTexture(halfResDepthTexture);
                passData.halfResNormalTexture = passRef.ReadTexture(halfResNormalTexture);
                passData.occlusionTexture = passRef.WriteTexture(occlusionTexture);
                passData.spatialTempTexture = passRef.WriteTexture(spatialTempTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in GTAOPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.ssaoShader == null) return;

                    int halfWidth = passData.halfResolution.x;
                    int halfHeight = passData.halfResolution.y;

                    // Set common uniforms (matching HLSL cbuffer CBV_OcclusionUnifrom)
                    cmdEncoder.SetComputeIntParam(passData.ssaoShader, GTAOPassUtilityData.NumRayID, passData.numRays);
                    cmdEncoder.SetComputeIntParam(passData.ssaoShader, GTAOPassUtilityData.NumStepID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.PowerID, passData.power);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.RadiusID, passData.radius);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.IntensityID, passData.intensity);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.SharpenessID, passData.sharpeness);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.HalfProjScaleID, passData.halfProjScale);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.TemporalOffsetID, passData.temporalOffset);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.TemporalDirectionID, passData.temporalDirection);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.TemporalScaleID, passData.temporalScale);
                    cmdEncoder.SetComputeFloatParam(passData.ssaoShader, GTAOPassUtilityData.TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeVectorParam(passData.ssaoShader, GTAOPassUtilityData.ResolutionID, new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight));
                    cmdEncoder.SetComputeMatrixParam(passData.ssaoShader, GTAOPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssaoShader, GTAOPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssaoShader, GTAOPassUtilityData.Matrix_ViewProjID, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssaoShader, GTAOPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(passData.ssaoShader, GTAOPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);

                    // Dispatch thread groups: shader uses [numthreads(16, 16, 1)]
                    int groupsX = Mathf.CeilToInt(halfWidth / 16.0f);
                    int groupsY = Mathf.CeilToInt(halfHeight / 16.0f);

                    // Kernel 0: Occlusion Trace (at half-res) → writes UAV_OcclusionTexture
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionTraceKernel, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionTraceKernel, GTAOPassUtilityData.SRV_NormalTextureID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionTraceKernel, GTAOPassUtilityData.UAV_OcclusionTextureID, passData.occlusionTexture);
                    cmdEncoder.DispatchCompute(passData.ssaoShader, GTAOPassUtilityData.OcclusionTraceKernel, groupsX, groupsY, 1);

                    // Kernel 1: Spatial filter X → reads SRV_OcclusionTexture (trace output), writes UAV_SpatialTexture
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialXKernel, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialXKernel, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.occlusionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialXKernel, GTAOPassUtilityData.UAV_SpatialTextureID, passData.spatialTempTexture);
                    cmdEncoder.DispatchCompute(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialXKernel, groupsX, groupsY, 1);

                    // Kernel 2: Spatial filter Y → reads SRV_OcclusionTexture (spatialX output), writes UAV_SpatialTexture (final)
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialYKernel, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialYKernel, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.spatialTempTexture);
                    cmdEncoder.SetComputeTextureParam(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialYKernel, GTAOPassUtilityData.UAV_SpatialTextureID, passData.occlusionTexture);
                    cmdEncoder.DispatchCompute(passData.ssaoShader, GTAOPassUtilityData.OcclusionSpatialYKernel, groupsX, groupsY, 1);
                });
            }
        }
    }
}
