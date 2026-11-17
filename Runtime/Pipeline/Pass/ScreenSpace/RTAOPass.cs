using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class RTAOPassUtilityData
    {
        internal static readonly string OcclusionTextureName = "RTAOOcclusionTexture";
        internal static readonly int NumRaysID = Shader.PropertyToID("RTAO_NumRays");
        internal static readonly int RadiusID = Shader.PropertyToID("RTAO_Radius");
        internal static readonly int ResolutionID = Shader.PropertyToID("RTAO_Resolution");
        // The UnifiedRayTracing shader takes its inputs through the backend's own binding calls, not the global ViewUniform buffer.
        internal static readonly int InvViewFlipYJitterProjID = Shader.PropertyToID("Matrix_InvViewFlipYJitterProj");
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
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle normalTextureHandle;
            public RGTextureHandle gBufferCHandle;
            public RGTextureHandle occlusionTextureHandle;
        }

        void TraceOcclusion(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.RTAO))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                InfinityDebugDisplaySettings.current.lighting.rayTracingBackend = InfinityRayTracingEnvironment.lastBackend;
                InfinityDebugDisplaySettings.current.lighting.rtaoBlockReason = InfinityRayTracingEnvironment.lastBlockReason;
#endif
                return;
            }

            RayTracingAmbientOcclusion settings = context.volumes.rayTracingAmbientOcclusion;
            if (!VolumeComponentActive(settings))
            {
                throw new System.InvalidOperationException("InfinityRP: RTAO is marked to produce but the Volume is inactive.");
            }

            InfinityRayTracingEnvironment env = InfinityRayTracingEnvironment.EnsureOwned();
            env.SyncGeometry(renderContext.sceneWorld.meshes);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            InfinityDebugDisplaySettings.current.lighting.rayTracingBackend = InfinityRayTracingEnvironment.lastBackend;
            InfinityDebugDisplaySettings.current.lighting.rtaoBlockReason = InfinityRayTracingEnvironment.lastBlockReason;
#endif
            if (!env.IsReady || env.InstanceCount == 0 || env.Shader == null || env.Accel == null)
            {
                throw new System.InvalidOperationException("InfinityRP: RTAO is active but UnifiedRayTracing accel/shader is not ready: " + InfinityRayTracingEnvironment.lastBlockReason);
            }

            TextureDescriptor occlusionDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            occlusionDsc.name = RTAOPassUtilityData.OcclusionTextureName;
            occlusionDsc.dimension = TextureDimension.Tex2D;
            occlusionDsc.colorFormat = GraphicsFormat.R16_SFloat;
            occlusionDsc.enableRandomWrite = true;
            occlusionDsc.filterMode = FilterMode.Bilinear;
            occlusionDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureHandle occlusionHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionDsc);

            {
                RGRayTracingPassHandle<RTAOPassData> passHandle = m_RGBuilder.AddRayTracingPass<RTAOPassData>(ProfilingSampler.Get(InfinityRayTracingEnvironment.OcclusionSampler(env.Backend)));
                ref RTAOPassData passData = ref passHandle.GetPassData();
                passData.resolution = new int2(context.dimensions.internalSize.x, context.dimensions.internalSize.y);
                RTAOSettings resolved = settings.Resolve();
                passData.numRays = resolved.numRays;
                passData.radius = resolved.radius;
                passData.buildAccel = env.NeedsBuild;
                if (passData.buildAccel)
                {
                    passHandle.SetQueueObserver(env);
                }
                passData.invView = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.shader = env.Shader;
                passData.accel = env.Accel;
                passData.buildScratch = env.PrepareBuildScratch();
                passData.traceScratch = env.PrepareTraceScratch((uint)context.dimensions.internalSize.x, (uint)context.dimensions.internalSize.y);
                passData.depthTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.normalTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferCHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.occlusionTextureHandle = passHandle.WriteTexture(occlusionHandle);
                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in RTAOPassData passData, in RGRaytracingEncoder cmdEncoder) =>
                {
                    if (passData.buildAccel)
                    {
                        cmdEncoder.BuildUnifiedAccel(passData.accel, passData.buildScratch);
                    }

                    cmdEncoder.SetUnifiedAccelerationStructure(passData.shader, "g_SceneAccelStruct", passData.accel);
                    cmdEncoder.SetUnifiedInt(passData.shader, RTAOPassUtilityData.NumRaysID, passData.numRays);
                    cmdEncoder.SetUnifiedFloat(passData.shader, RTAOPassUtilityData.RadiusID, passData.radius);
                    cmdEncoder.SetUnifiedVector(passData.shader, RTAOPassUtilityData.ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetUnifiedMatrix(passData.shader, RTAOPassUtilityData.InvViewFlipYJitterProjID, passData.invView);
                    cmdEncoder.SetUnifiedTexture(passData.shader, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetUnifiedTexture(passData.shader, PassShaderIDs.SRV_NormalTexture, passData.normalTextureHandle);
                    cmdEncoder.SetUnifiedTexture(passData.shader, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferCHandle);
                    cmdEncoder.SetUnifiedTexture(passData.shader, PassShaderIDs.UAV_OcclusionTexture, passData.occlusionTextureHandle);
                    cmdEncoder.DispatchUnified(passData.shader, passData.traceScratch, (uint)passData.resolution.x, (uint)passData.resolution.y, 1u);
                });
            }

            context.features.MarkProduced(EFrameFeature.RTAO);
        }
    }
}
