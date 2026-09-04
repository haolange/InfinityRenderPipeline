using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SSGIPassUtilityData
    {
        internal static string RadianceName = "SSGIRadiance";
        internal static string SpatialName = "SSGISpatial";
        internal static string TemporalName = "SSGITemporal";
        internal static string MomentsName = "SSGIMoments";
        internal static string DepthNormalName = "SSGIDepthNormal";
        internal static string TextureName = "SSGITexture";
        internal static string HistoryRadianceName = "HistorySSGIRadiance";
        internal static string HistoryMomentsName = "HistorySSGIMoments";
        internal static string HistoryDepthNormalName = "HistorySSGIDepthNormal";

        internal static int SSGi_TraceResolutionID = Shader.PropertyToID("SSGi_TraceResolution");
        internal static int SSGi_FilterResolutionID = Shader.PropertyToID("SSGi_FilterResolution");
        internal static int SSGi_NumRaysID = Shader.PropertyToID("SSGi_NumRays");
        internal static int SSGi_NumStepsID = Shader.PropertyToID("SSGi_NumSteps");
        internal static int SSGi_IntensityID = Shader.PropertyToID("SSGi_Intensity");
        internal static int SSGi_FrameIndexID = Shader.PropertyToID("SSGi_FrameIndex");
        internal static int SSGi_SpatialRadiusID = Shader.PropertyToID("SSGi_SpatialRadius");
        internal static int SSGi_TemporalScaleID = Shader.PropertyToID("SSGi_TemporalScale");
        internal static int SSGi_TemporalWeightID = Shader.PropertyToID("SSGi_TemporalWeight");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static int Matrix_ViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int Matrix_LastViewProjID = Shader.PropertyToID("Matrix_LastViewProj");
        internal static int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static int SRV_PyramidDepthID = Shader.PropertyToID("SRV_PyramidDepth");
        internal static int SRV_PyramidColorID = Shader.PropertyToID("SRV_PyramidColor");
        internal static int SRV_SceneDepthID = Shader.PropertyToID("SRV_SceneDepth");
        internal static int SRV_GBufferNormalID = Shader.PropertyToID("SRV_GBufferNormal");
        internal static int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static int SRV_ColorMaskTextureID = Shader.PropertyToID("SRV_ColorMaskTexture");
        internal static int SRV_AliasingTextureID = Shader.PropertyToID("SRV_AliasingTexture");
        internal static int SRV_HistoryTextureID = Shader.PropertyToID("SRV_HistoryTexture");
        internal static int SRV_HistoryMomentsID = Shader.PropertyToID("SRV_HistoryMoments");
        internal static int SRV_HistoryDepthNormalID = Shader.PropertyToID("SRV_HistoryDepthNormal");
        internal static int UAV_ScreenIrradianceID = Shader.PropertyToID("UAV_ScreenIrradiance");
        internal static int UAV_SpatialTextureID = Shader.PropertyToID("UAV_SpatialTexture");
        internal static int UAV_AccmulateTextureID = Shader.PropertyToID("UAV_AccmulateTexture");
        internal static int UAV_MomentsTextureID = Shader.PropertyToID("UAV_MomentsTexture");
        internal static int UAV_DepthNormalTextureID = Shader.PropertyToID("UAV_DepthNormalTexture");
        internal static int UAV_BilateralColorID = Shader.PropertyToID("UAV_BilateralColor");
        internal static int SVGF_BilateralRadiusID = Shader.PropertyToID("SVGF_BilateralRadius");
        internal static int SVGF_ColorWeightID = Shader.PropertyToID("SVGF_ColorWeight");
        internal static int SVGF_NormalWeightID = Shader.PropertyToID("SVGF_NormalWeight");
        internal static int SVGF_DepthWeightID = Shader.PropertyToID("SVGF_DepthWeight");
        internal static int SVGF_BilateralSizeID = Shader.PropertyToID("SVGF_BilateralSize");

        internal static int RaytracingKernel = 0;
        internal static int SpatialKernel = 1;
        internal static int TemporalKernel = 2;
        internal static int BilateralKernel = 3;
    }

    public partial class InfinityRenderPipeline
    {
        struct SSGIPassData
        {
            public int numRays;
            public int numSteps;
            public int numSpatial;
            public int runBilateral;
            public float intensity;
            public float spatialRadius;
            public float temporalScale;
            public float temporalWeight;
            public float bilateralRadius;
            public float bilateralColorWeight;
            public float bilateralDepthWeight;
            public float bilateralNormalWeight;
            public int frameIndex;
            public int2 resolution;
            public Matrix4x4 matrix_Proj;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_LastViewProj;
            public Matrix4x4 matrix_WorldToView;
            public ComputeShader ssgiShader;
            public RGTextureRef hiZTexture;
            public RGTextureRef colorPyramidTexture;
            public RGTextureRef gBufferB;
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
            public RGTextureRef historyRadiance;
            public RGTextureRef historyMoments;
            public RGTextureRef historyDepthNormal;
            public RGTextureRef radianceTexture;
            public RGTextureRef spatialTexture;
            public RGTextureRef temporalTexture;
            public RGTextureRef momentsTexture;
            public RGTextureRef depthNormalTexture;
            public RGTextureRef ssgiTexture;
        }

        void ComputeScreenSpaceIndirect(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.SSGI))
            {
                return;
            }

            var ssgi = ActiveVolumeStack.GetComponent<ScreenSpaceIndirectDiffuse>();
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.ssgiShader, "Raytracing", "SpatialFilter", "TemporalFilter", "BilateralFilter"))
            {
                return;
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.OpaqueLightingPyramidBuffer, out RGTextureRef colorPyramidTexture))
            {
                return;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat;

            RGTextureRef radianceTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIRadianceBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSGIPassUtilityData.RadianceName));
            RGTextureRef spatialTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGISpatialBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSGIPassUtilityData.SpatialName));
            RGTextureRef temporalTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITemporalBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSGIPassUtilityData.TemporalName));
            RGTextureRef momentsTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIMomentsBuffer, ScreenSpaceHistoryUtility.CreateMomentsDescriptor(width, height, SSGIPassUtilityData.MomentsName));
            RGTextureRef depthNormalTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIDepthNormalBuffer, ScreenSpaceHistoryUtility.CreateDepthNormalDescriptor(width, height, SSGIPassUtilityData.DepthNormalName));

            bool runBilateral = ssgi.BilateralSample.value > 0;
            RGTextureRef ssgiTexture;
            if (runBilateral)
            {
                ssgiTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSGIPassUtilityData.TextureName));
            }
            else
            {
                m_RGScoper.RegisterTexture(InfinityShaderIDs.SSGIBuffer, temporalTexture);
                ssgiTexture = temporalTexture;
            }

            TextureDescriptor historyRadianceDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSGIPassUtilityData.HistoryRadianceName, format);
            TextureDescriptor historyMomentsDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSGIPassUtilityData.HistoryMomentsName, format);
            TextureDescriptor historyDepthNormalDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSGIPassUtilityData.HistoryDepthNormalName, format);
            RGTextureRef historyRadiance = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadianceDsc, out bool historyRadianceCreated));
            RGTextureRef historyMoments = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSGIMomentsBuffer, historyMomentsDsc, out bool historyMomentsCreated));
            RGTextureRef historyDepthNormal = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSGIDepthNormalBuffer, historyDepthNormalDsc, out bool historyDepthNormalCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadiance);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSGIMomentsBuffer, historyMoments);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSGIDepthNormalBuffer, historyDepthNormal);

            RGTextureRef hiZTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HiZBuffer);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SSGIPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceIndirect)))
            {
                ref SSGIPassData passData = ref passRef.GetPassData<SSGIPassData>();
                passData.numRays = ssgi.NumRays.value;
                passData.numSteps = ssgi.NumSteps.value;
                passData.numSpatial = math.max(1, ssgi.SpatialSample.value);
                passData.runBilateral = runBilateral ? 1 : 0;
                passData.intensity = ssgi.IntensityScale.value;
                passData.spatialRadius = math.max(1, ssgi.SpatialRadius.value);
                bool resetHistory = m_CameraUniform.historyReset || historyRadianceCreated || historyMomentsCreated || historyDepthNormalCreated;
                passData.temporalScale = ssgi.TemporalScale.value;
                passData.temporalWeight = ScreenSpaceHistoryUtility.RampTemporalWeight(ssgi.TemporalWeight.value, ref m_ActiveFrameState.ssgiValidFrames, resetHistory);
                passData.bilateralRadius = math.max(1, ssgi.BilateralSample.value);
                passData.bilateralColorWeight = ssgi.BilateralColorWeight.value;
                passData.bilateralDepthWeight = ssgi.BilateralDepthWeight.value;
                passData.bilateralNormalWeight = ssgi.BilateralNormalWeight.value;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(width, height);
                passData.matrix_Proj = m_CameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_ViewProj = m_CameraUniform.matrix_ViewFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_LastViewProj = m_CameraUniform.matrix_LastViewFlipYJitterProj;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.ssgiShader = pipelineAsset.ssgiShader;
                passData.hiZTexture = passRef.ReadTexture(hiZTexture);
                passData.colorPyramidTexture = passRef.ReadTexture(colorPyramidTexture);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyRadiance = passRef.ReadTexture(historyRadiance);
                passData.historyMoments = passRef.ReadTexture(historyMoments);
                passData.historyDepthNormal = passRef.ReadTexture(historyDepthNormal);
                passData.radianceTexture = passRef.WriteTexture(radianceTexture);
                passData.spatialTexture = passRef.WriteTexture(spatialTexture);
                passData.temporalTexture = passRef.WriteTexture(temporalTexture);
                passData.momentsTexture = passRef.WriteTexture(momentsTexture);
                passData.depthNormalTexture = passRef.WriteTexture(depthNormalTexture);
                if (runBilateral)
                {
                    passData.ssgiTexture = passRef.WriteTexture(ssgiTexture);
                }
                else
                {
                    passData.ssgiTexture = passData.temporalTexture;
                }

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SSGIPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.ssgiShader;
                    int width = passData.resolution.x;
                    int height = passData.resolution.y;
                    Vector4 resolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
                    int groupsX = math.max(1, Mathf.CeilToInt(width / 16.0f));
                    int groupsY = math.max(1, Mathf.CeilToInt(height / 16.0f));

                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGi_TraceResolutionID, resolution);
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGi_FilterResolutionID, resolution);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGi_NumRaysID, passData.numRays);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGi_NumStepsID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGi_IntensityID, passData.intensity);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGi_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGi_SpatialRadiusID, passData.spatialRadius);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGi_TemporalScaleID, passData.temporalScale);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGi_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeMatrixParam(shader, SSGIPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSGIPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSGIPassUtilityData.Matrix_ViewProjID, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSGIPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSGIPassUtilityData.Matrix_LastViewProjID, passData.matrix_LastViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSGIPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);

                    int ray = SSGIPassUtilityData.RaytracingKernel;
                    cmdEncoder.BeginSample("SSGI_RayMarch");
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSGIPassUtilityData.SRV_PyramidDepthID, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSGIPassUtilityData.SRV_PyramidColorID, passData.colorPyramidTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSGIPassUtilityData.SRV_SceneDepthID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSGIPassUtilityData.SRV_GBufferNormalID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSGIPassUtilityData.UAV_ScreenIrradianceID, passData.radianceTexture);
                    cmdEncoder.DispatchCompute(shader, ray, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSGI_RayMarch");

                    RGTextureRef spatialRead = passData.radianceTexture;
                    RGTextureRef spatialWrite = passData.spatialTexture;
                    int spatial = SSGIPassUtilityData.SpatialKernel;
                    cmdEncoder.BeginSample("SSGI_Spatial");
                    for (int i = 0; i < passData.numSpatial; ++i)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSGIPassUtilityData.SRV_SceneDepthID, passData.depthTexture);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSGIPassUtilityData.SRV_GBufferNormalID, passData.gBufferB);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSGIPassUtilityData.SRV_ColorMaskTextureID, spatialRead);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSGIPassUtilityData.UAV_SpatialTextureID, spatialWrite);
                        cmdEncoder.DispatchCompute(shader, spatial, groupsX, groupsY, 1);

                        RGTextureRef swap = spatialRead;
                        spatialRead = spatialWrite;
                        spatialWrite = swap;
                    }
                    cmdEncoder.EndSample("SSGI_Spatial");

                    int temporal = SSGIPassUtilityData.TemporalKernel;
                    cmdEncoder.BeginSample("SSGI_Temporal");
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_HistoryTextureID, passData.historyRadiance);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_HistoryMomentsID, passData.historyMoments);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_HistoryDepthNormalID, passData.historyDepthNormal);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_AliasingTextureID, spatialRead);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_SceneDepthID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.SRV_GBufferNormalID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.UAV_AccmulateTextureID, passData.temporalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.UAV_MomentsTextureID, passData.momentsTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSGIPassUtilityData.UAV_DepthNormalTextureID, passData.depthNormalTexture);
                    cmdEncoder.DispatchCompute(shader, temporal, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSGI_Temporal");

                    if (passData.runBilateral != 0)
                    {
                        int bilateral = SSGIPassUtilityData.BilateralKernel;
                        cmdEncoder.BeginSample("SSGI_Bilateral");
                        cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SVGF_BilateralRadiusID, passData.bilateralRadius);
                        cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SVGF_ColorWeightID, passData.bilateralColorWeight);
                        cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SVGF_NormalWeightID, passData.bilateralNormalWeight);
                        cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SVGF_DepthWeightID, passData.bilateralDepthWeight);
                        cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SVGF_BilateralSizeID, resolution);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSGIPassUtilityData.SRV_AliasingTextureID, passData.temporalTexture);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSGIPassUtilityData.SRV_GBufferNormalID, passData.gBufferB);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSGIPassUtilityData.SRV_SceneDepthID, passData.depthTexture);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSGIPassUtilityData.UAV_BilateralColorID, passData.ssgiTexture);
                        cmdEncoder.DispatchCompute(shader, bilateral, groupsX, groupsY, 1);
                        cmdEncoder.EndSample("SSGI_Bilateral");
                    }
                });
            }

            MarkFeatureProduced(EFrameFeature.SSGI);
        }
    }
}
