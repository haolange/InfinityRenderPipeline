using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class RTAOPassUtilityData
    {
        internal static string OcclusionTextureName = "RTAOOcclusionTexture";
        internal static int NumRaysID = Shader.PropertyToID("RTAO_NumRays");
        internal static int RadiusID = Shader.PropertyToID("RTAO_Radius");
        internal static int ResolutionID = Shader.PropertyToID("RTAO_Resolution");
        internal static int InvViewID = Shader.PropertyToID("Matrix_InvViewFlipYJitterProj");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_NormalTextureID = Shader.PropertyToID("SRV_NormalTexture");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int UAV_OcclusionTextureID = Shader.PropertyToID("UAV_OcclusionTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct RTAOPassData
        {
            public int2 resolution;
            public int numRays;
            public float radius;
            public bool buildAccel;
            public Matrix4x4 invView;
            public IRayTracingShader shader;
            public IRayTracingAccelStruct accel;
            public GraphicsBuffer buildScratch;
            public GraphicsBuffer traceScratch;
            public RGTextureRef depthTexture;
            public RGTextureRef normalTexture;
            public RGTextureRef gBufferC;
            public RGTextureRef occlusionTexture;
        }

        void ComputeRayTracedOcclusion(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.RTAO))
            {
                InfinityDebugDisplaySettings.current.lighting.rayTracingBackend = InfinityRayTracingEnvironment.lastBackend;
                InfinityDebugDisplaySettings.current.lighting.rtaoBlockReason = InfinityRayTracingEnvironment.lastBlockReason;
                return;
            }

            RayTracingAmbientOcclusion settings = ActiveVolumeStack.GetComponent<RayTracingAmbientOcclusion>();
            if (!VolumeComponentActive(settings))
            {
                throw new System.InvalidOperationException("InfinityRP: RTAO is marked to produce but the Volume is inactive.");
            }

            InfinityRayTracingEnvironment env = InfinityRayTracingEnvironment.EnsureOwned();
            env.SyncGeometry(renderContext.GetMeshScene());
            InfinityDebugDisplaySettings.current.lighting.rayTracingBackend = InfinityRayTracingEnvironment.lastBackend;
            InfinityDebugDisplaySettings.current.lighting.rtaoBlockReason = InfinityRayTracingEnvironment.lastBlockReason;
            if (!env.IsReady || env.InstanceCount == 0 || env.Shader == null || env.Accel == null)
            {
                throw new System.InvalidOperationException("InfinityRP: RTAO is active but UnifiedRayTracing accel/shader is not ready: " + InfinityRayTracingEnvironment.lastBlockReason);
            }

            TextureDescriptor occlusionDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y);
            occlusionDsc.name = RTAOPassUtilityData.OcclusionTextureName;
            occlusionDsc.dimension = TextureDimension.Tex2D;
            occlusionDsc.colorFormat = GraphicsFormat.R16_SFloat;
            occlusionDsc.enableRandomWrite = true;
            occlusionDsc.filterMode = FilterMode.Bilinear;
            occlusionDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef occlusion = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionDsc);

            using (RGRayTracingPassRef passRef = m_RGBuilder.AddRayTracingPass<RTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeRayTracedOcclusion)))
            {
                ref RTAOPassData passData = ref passRef.GetPassData<RTAOPassData>();
                passData.resolution = new int2(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y);
                passData.numRays = settings.NumRays.value;
                passData.radius = settings.Radius.value;
                passData.buildAccel = env.ConsumeBuild();
                passData.invView = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.shader = env.Shader;
                passData.accel = env.Accel;
                passData.buildScratch = env.PrepareBuildScratch();
                passData.traceScratch = env.PrepareTraceScratch((uint)m_ActiveFrameState.dimensions.internalSize.x, (uint)m_ActiveFrameState.dimensions.internalSize.y);
                passData.depthTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.normalTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferC = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.occlusionTexture = passRef.WriteTexture(occlusion);
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in RTAOPassData passData, in RGRaytracingEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.buildAccel)
                    {
                        cmdEncoder.BuildUnifiedAccel(passData.accel, passData.buildScratch);
                    }

                    cmdEncoder.SetUnifiedAccelerationStructure(passData.shader, "g_SceneAccelStruct", passData.accel);
                    cmdEncoder.SetUnifiedInt(passData.shader, RTAOPassUtilityData.NumRaysID, passData.numRays);
                    cmdEncoder.SetUnifiedFloat(passData.shader, RTAOPassUtilityData.RadiusID, passData.radius);
                    cmdEncoder.SetUnifiedVector(passData.shader, RTAOPassUtilityData.ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetUnifiedMatrix(passData.shader, RTAOPassUtilityData.InvViewID, passData.invView);
                    cmdEncoder.SetUnifiedTexture(passData.shader, RTAOPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetUnifiedTexture(passData.shader, RTAOPassUtilityData.SRV_NormalTextureID, passData.normalTexture);
                    cmdEncoder.SetUnifiedTexture(passData.shader, RTAOPassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    cmdEncoder.SetUnifiedTexture(passData.shader, RTAOPassUtilityData.UAV_OcclusionTextureID, passData.occlusionTexture);
                    cmdEncoder.DispatchUnified(passData.shader, passData.traceScratch, (uint)passData.resolution.x, (uint)passData.resolution.y, 1u);
                });
            }

            MarkFeatureProduced(EFrameFeature.RTAO);
        }
    }
}
