using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SSRPassUtilityData
    {
        internal static string RadianceName = "SSRRadiance";
        internal static string HitPDFTextureName = "SSRHitPDFTexture";
        internal static string SpatialName = "SSRSpatial";
        internal static string TemporalName = "SSRTemporal";
        internal static string MomentsName = "SSRMoments";
        internal static string DepthNormalName = "SSRDepthNormal";
        internal static string TextureName = "SSRTexture";
        internal static string HistoryRadianceName = "HistorySSRRadiance";
        internal static string HistoryMomentsName = "HistorySSRMoments";
        internal static string HistoryDepthNormalName = "HistorySSRDepthNormal";

        internal static int SSR_ResolutionID = Shader.PropertyToID("SSR_Resolution");
        internal static int SSR_FilterResolutionID = Shader.PropertyToID("SSR_FilterResolution");
        internal static int SSR_NumRaysID = Shader.PropertyToID("SSR_NumRays");
        internal static int SSR_NumStepsID = Shader.PropertyToID("SSR_NumSteps");
        internal static int SSR_BRDFBiasID = Shader.PropertyToID("SSR_BRDFBias");
        internal static int SSR_FadenessID = Shader.PropertyToID("SSR_Fadeness");
        internal static int SSR_RoughnessID = Shader.PropertyToID("SSR_Roughness");
        internal static int SSR_FrameIndexID = Shader.PropertyToID("SSR_FrameIndex");
        internal static int SSR_SpatialRadiusID = Shader.PropertyToID("SSR_SpatialRadius");
        internal static int SSR_TemporalScaleID = Shader.PropertyToID("SSR_TemporalScale");
        internal static int SSR_TemporalWeightID = Shader.PropertyToID("SSR_TemporalWeight");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static int Matrix_ViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int Matrix_LastViewProjID = Shader.PropertyToID("Matrix_LastViewProj");
        internal static int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static int SRV_HiZTextureID = Shader.PropertyToID("SRV_HiZTexture");
        internal static int SRV_HiCTextureID = Shader.PropertyToID("SRV_HiCTexture");
        internal static int SRV_NormalTextureID = Shader.PropertyToID("SRV_NormalTexture");
        internal static int SRV_RoughnessTextureID = Shader.PropertyToID("SRV_RoughnessTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static int SRV_HitPDFTextureID = Shader.PropertyToID("SRV_HitPDFTexture");
        internal static int SRV_ColorMaskTextureID = Shader.PropertyToID("SRV_ColorMaskTexture");
        internal static int SRV_AliasingTextureID = Shader.PropertyToID("SRV_AliasingTexture");
        internal static int SRV_HistoryTextureID = Shader.PropertyToID("SRV_HistoryTexture");
        internal static int SRV_HistoryMomentsID = Shader.PropertyToID("SRV_HistoryMoments");
        internal static int SRV_HistoryDepthNormalID = Shader.PropertyToID("SRV_HistoryDepthNormal");
        internal static int UAV_HitPDFTextureID = Shader.PropertyToID("UAV_HitPDFTexture");
        internal static int UAV_ColorMaskTextureID = Shader.PropertyToID("UAV_ColorMaskTexture");
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
        struct SSRPassData
        {
            public int numRays;
            public int numSteps;
            public int numSpatial;
            public int runBilateral;
            public float brdfBias;
            public float fadeness;
            public float maxRoughness;
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
            public ComputeShader ssrShader;
            public RGTextureRef hiZTexture;
            public RGTextureRef colorPyramidTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
            public RGTextureRef historyRadiance;
            public RGTextureRef historyMoments;
            public RGTextureRef historyDepthNormal;
            public RGTextureRef hitPdfTexture;
            public RGTextureRef radianceTexture;
            public RGTextureRef spatialTexture;
            public RGTextureRef temporalTexture;
            public RGTextureRef momentsTexture;
            public RGTextureRef depthNormalTexture;
            public RGTextureRef ssrTexture;
        }

        void ComputeScreenSpaceReflection(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.SSR))
            {
                return;
            }

            var ssr = ActiveVolumeStack.GetComponent<ScreenSpaceReflection>();
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.ssrShader, "Raytracing", "SpatialFilter", "TemporalFilter", "BilateralFilter"))
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

            RGTextureRef radianceTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRRadianceBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSRPassUtilityData.RadianceName));
            RGTextureRef hitPdfTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitPDFBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSRPassUtilityData.HitPDFTextureName));
            RGTextureRef spatialTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRSpatialBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSRPassUtilityData.SpatialName));
            RGTextureRef temporalTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRTemporalBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSRPassUtilityData.TemporalName));
            RGTextureRef momentsTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRMomentsBuffer, ScreenSpaceHistoryUtility.CreateMomentsDescriptor(width, height, SSRPassUtilityData.MomentsName));
            RGTextureRef depthNormalTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRDepthNormalBuffer, ScreenSpaceHistoryUtility.CreateDepthNormalDescriptor(width, height, SSRPassUtilityData.DepthNormalName));

            bool runBilateral = ssr.BilateralSample.value > 0;
            RGTextureRef ssrTexture;
            if (runBilateral)
            {
                ssrTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRBuffer, ScreenSpaceHistoryUtility.CreateRadianceDescriptor(width, height, SSRPassUtilityData.TextureName));
            }
            else
            {
                m_RGScoper.RegisterTexture(InfinityShaderIDs.SSRBuffer, temporalTexture);
                ssrTexture = temporalTexture;
            }

            TextureDescriptor historyRadianceDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSRPassUtilityData.HistoryRadianceName, format);
            TextureDescriptor historyMomentsDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSRPassUtilityData.HistoryMomentsName, format);
            TextureDescriptor historyDepthNormalDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSRPassUtilityData.HistoryDepthNormalName, format);
            RGTextureRef historyRadiance = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadianceDsc, out bool historyRadianceCreated));
            RGTextureRef historyMoments = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSRMomentsBuffer, historyMomentsDsc, out bool historyMomentsCreated));
            RGTextureRef historyDepthNormal = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSRDepthNormalBuffer, historyDepthNormalDsc, out bool historyDepthNormalCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadiance);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSRMomentsBuffer, historyMoments);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSRDepthNormalBuffer, historyDepthNormal);

            RGTextureRef hiZTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HiZBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SSRPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceReflection)))
            {
                ref SSRPassData passData = ref passRef.GetPassData<SSRPassData>();
                passData.numRays = ssr.NumRays.value;
                passData.numSteps = ssr.NumSteps.value;
                passData.numSpatial = math.max(1, ssr.SpatialSample.value);
                passData.runBilateral = runBilateral ? 1 : 0;
                passData.brdfBias = ssr.BrdfBias.value;
                passData.fadeness = ssr.Fadeness.value;
                passData.maxRoughness = ssr.MaxRoughness.value;
                passData.spatialRadius = math.max(1, ssr.SpatialRadius.value);
                bool resetHistory = m_CameraUniform.historyReset || historyRadianceCreated || historyMomentsCreated || historyDepthNormalCreated;
                passData.temporalScale = ssr.TemporalScale.value;
                passData.temporalWeight = ScreenSpaceHistoryUtility.RampTemporalWeight(ssr.TemporalWeight.value, ref m_ActiveFrameState.ssrValidFrames, resetHistory);
                passData.bilateralRadius = math.max(1, ssr.BilateralSample.value);
                passData.bilateralColorWeight = ssr.BilateralColorWeight.value;
                passData.bilateralDepthWeight = ssr.BilateralDepthWeight.value;
                passData.bilateralNormalWeight = ssr.BilateralNormalWeight.value;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(width, height);
                passData.matrix_Proj = m_CameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_ViewProj = m_CameraUniform.matrix_ViewFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_LastViewProj = m_CameraUniform.matrix_LastViewFlipYJitterProj;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.ssrShader = pipelineAsset.ssrShader;
                passData.hiZTexture = passRef.ReadTexture(hiZTexture);
                passData.colorPyramidTexture = passRef.ReadTexture(colorPyramidTexture);
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyRadiance = passRef.ReadTexture(historyRadiance);
                passData.historyMoments = passRef.ReadTexture(historyMoments);
                passData.historyDepthNormal = passRef.ReadTexture(historyDepthNormal);
                passData.hitPdfTexture = passRef.WriteTexture(hitPdfTexture);
                passData.radianceTexture = passRef.WriteTexture(radianceTexture);
                passData.spatialTexture = passRef.WriteTexture(spatialTexture);
                passData.temporalTexture = passRef.WriteTexture(temporalTexture);
                passData.momentsTexture = passRef.WriteTexture(momentsTexture);
                passData.depthNormalTexture = passRef.WriteTexture(depthNormalTexture);
                if (runBilateral)
                {
                    passData.ssrTexture = passRef.WriteTexture(ssrTexture);
                }
                else
                {
                    passData.ssrTexture = passData.temporalTexture;
                }

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SSRPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.ssrShader;
                    int width = passData.resolution.x;
                    int height = passData.resolution.y;
                    Vector4 resolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
                    int groupsX = math.max(1, Mathf.CeilToInt(width / 16.0f));
                    int groupsY = math.max(1, Mathf.CeilToInt(height / 16.0f));

                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SSR_ResolutionID, resolution);
                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SSR_FilterResolutionID, resolution);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_NumRaysID, passData.numRays);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_NumStepsID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_BRDFBiasID, passData.brdfBias);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_FadenessID, passData.fadeness);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_RoughnessID, passData.maxRoughness);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_SpatialRadiusID, passData.spatialRadius);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_TemporalScaleID, passData.temporalScale);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_ViewProjID, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_LastViewProjID, passData.matrix_LastViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);

                    int ray = SSRPassUtilityData.RaytracingKernel;
                    cmdEncoder.BeginSample("SSR_RayMarch");
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.SRV_HiZTextureID, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.SRV_HiCTextureID, passData.colorPyramidTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.SRV_NormalTextureID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.SRV_RoughnessTextureID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.UAV_HitPDFTextureID, passData.hitPdfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, ray, SSRPassUtilityData.UAV_ColorMaskTextureID, passData.radianceTexture);
                    cmdEncoder.DispatchCompute(shader, ray, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSR_RayMarch");

                    RGTextureRef spatialRead = passData.radianceTexture;
                    RGTextureRef spatialWrite = passData.spatialTexture;
                    int spatial = SSRPassUtilityData.SpatialKernel;
                    cmdEncoder.BeginSample("SSR_Spatial");
                    for (int i = 0; i < passData.numSpatial; ++i)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.SRV_NormalTextureID, passData.gBufferB);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.SRV_RoughnessTextureID, passData.gBufferA);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.SRV_HitPDFTextureID, passData.hitPdfTexture);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.SRV_ColorMaskTextureID, spatialRead);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.UAV_SpatialTextureID, spatialWrite);
                        cmdEncoder.DispatchCompute(shader, spatial, groupsX, groupsY, 1);

                        RGTextureRef swap = spatialRead;
                        spatialRead = spatialWrite;
                        spatialWrite = swap;
                    }
                    cmdEncoder.EndSample("SSR_Spatial");

                    int temporal = SSRPassUtilityData.TemporalKernel;
                    cmdEncoder.BeginSample("SSR_Temporal");
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_HitPDFTextureID, passData.hitPdfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_HistoryTextureID, passData.historyRadiance);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_HistoryMomentsID, passData.historyMoments);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_HistoryDepthNormalID, passData.historyDepthNormal);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_AliasingTextureID, spatialRead);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.SRV_NormalTextureID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.UAV_AccmulateTextureID, passData.temporalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.UAV_MomentsTextureID, passData.momentsTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, SSRPassUtilityData.UAV_DepthNormalTextureID, passData.depthNormalTexture);
                    cmdEncoder.DispatchCompute(shader, temporal, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSR_Temporal");

                    if (passData.runBilateral != 0)
                    {
                        int bilateral = SSRPassUtilityData.BilateralKernel;
                        cmdEncoder.BeginSample("SSR_Bilateral");
                        cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SVGF_BilateralRadiusID, passData.bilateralRadius);
                        cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SVGF_ColorWeightID, passData.bilateralColorWeight);
                        cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SVGF_NormalWeightID, passData.bilateralNormalWeight);
                        cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SVGF_DepthWeightID, passData.bilateralDepthWeight);
                        cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SVGF_BilateralSizeID, resolution);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSRPassUtilityData.SRV_AliasingTextureID, passData.temporalTexture);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSRPassUtilityData.SRV_NormalTextureID, passData.gBufferB);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSRPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSRPassUtilityData.UAV_BilateralColorID, passData.ssrTexture);
                        cmdEncoder.DispatchCompute(shader, bilateral, groupsX, groupsY, 1);
                        cmdEncoder.EndSample("SSR_Bilateral");
                    }
                });
            }

            MarkFeatureProduced(EFrameFeature.SSR);
        }
    }
}
