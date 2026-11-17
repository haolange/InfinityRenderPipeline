using System;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // The desktop deferred pipeline: every frame feature, deferred shading, async compute where the device has it.
    public class InfinityRenderPipeline : InfinityRenderPipelineBase
    {
        // Every EFrameFeature; each camera produces depth, the G-buffer, deferred shading and the display.
        internal static readonly PipelineCapabilities s_Capabilities = new PipelineCapabilities(
            (EFrameFeature[])Enum.GetValues(typeof(EFrameFeature)),
            new[] { EFrameFeature.Depth, EFrameFeature.GBuffer, EFrameFeature.DeferredShading, EFrameFeature.Display },
            asyncCompute: true);

        // One instance per pass for the pipeline's lifetime, built from the render context's services at construction (R3).
        readonly LightUploadPass m_LightUploadPass;
        readonly LightCookiePass m_LightCookiePass;
        readonly LightTileMaskPass m_LightTileMaskPass;
        readonly ReflectionProbePass m_ReflectionProbePass;
        readonly CombineLutPass m_CombineLutPass;
        readonly AtmosphericLUTPass m_AtmosphericLUTPass;
        readonly TemporalColorSourcePass m_TemporalColorSourcePass;
        readonly HiZPass m_HiZPass;
        readonly MeshOcclusionPass m_MeshOcclusionPass;
        readonly DepthPass m_DepthPass;
        readonly DBufferPass m_DBufferPass;
        readonly GBufferPass m_GBufferPass;
        readonly MotionPass m_MotionPass;
        readonly HalfResDownsamplePass m_HalfResDownsamplePass;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly RenderCapturePass m_RenderCapturePass;
        readonly DebugViewPass m_DebugViewPass;
#endif
        readonly RTAOPass m_RTAOPass;
        readonly GTAOPass m_GTAOPass;
        readonly ContactShadowPass m_ContactShadowPass;
        readonly SSRPass m_SSRPass;
        readonly SSGIPass m_SSGIPass;
        readonly ShadowCasterCulling m_ShadowCasterCulling;
        readonly CascadeShadowPass m_CascadeShadowPass;
        readonly LocalShadowPass m_LocalShadowPass;
        readonly ShadowMomentPass m_ShadowMomentPass;
        readonly DeferredShadingPass m_DeferredShadingPass;
        readonly ForwardPass m_ForwardPass;
        readonly ScreenSpaceCompositePass m_ScreenSpaceCompositePass;
        readonly ScreenSpaceHistoryPass m_ScreenSpaceHistoryPass;
        readonly SubsurfacePass m_SubsurfacePass;
        readonly AtmosphericSkyFogPass m_AtmosphericSkyFogPass;
        readonly TranslucentPass m_TranslucentPass;
        readonly VolumetricCloudPass m_VolumetricCloudPass;
        readonly VolumetricFogPass m_VolumetricFogPass;
        readonly FogCompositePass m_FogCompositePass;
        readonly ColorPyramidPass m_ColorPyramidPass;
        readonly SuperResolutionPass m_SuperResolutionPass;
        readonly AntiAliasingPass m_AntiAliasingPass;
        readonly FinalOutputPass m_FinalOutputPass;
        readonly PostProcessingPass m_PostProcessingPass;
        readonly UIOverlayPass m_UIOverlayPass;
        readonly UtilityPass m_UtilityPass;

        public InfinityRenderPipeline(InfinityRenderPipelineAsset asset) : base(asset, s_Capabilities)
        {
            try
            {
                // Passes in dependency order: a pass another one calls is built first.
                RGBuilder graph = renderContext.graph;
                m_LightUploadPass = new LightUploadPass(graph);
                m_LightCookiePass = new LightCookiePass(graph, resources, renderContext.sceneWorld.lights.cookieAtlas);
                m_LightTileMaskPass = new LightTileMaskPass(graph, resources);
                m_ReflectionProbePass = new ReflectionProbePass(graph, resources, renderContext.sceneWorld.probes.atlas, m_LightTileMaskPass);
                m_CombineLutPass = new CombineLutPass(graph, resources);
                m_AtmosphericLUTPass = new AtmosphericLUTPass(graph, resources, renderContext.atmosphereCache);
                m_TemporalColorSourcePass = new TemporalColorSourcePass(graph, resources);
                m_HiZPass = new HiZPass(graph, resources);
                m_MeshOcclusionPass = new MeshOcclusionPass(graph, m_HiZPass);
                m_DepthPass = new DepthPass(graph, settings);
                m_DBufferPass = new DBufferPass(graph, settings);
                m_GBufferPass = new GBufferPass(graph, settings);
                m_MotionPass = new MotionPass(graph, resources, settings);
                m_HalfResDownsamplePass = new HalfResDownsamplePass(graph, resources);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                m_RenderCapturePass = new RenderCapturePass(graph, resources);
                m_DebugViewPass = new DebugViewPass(graph, resources);
#endif
                m_RTAOPass = new RTAOPass(graph);
                m_GTAOPass = new GTAOPass(graph, resources
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , m_RenderCapturePass
#endif
                    );
                m_ContactShadowPass = new ContactShadowPass(graph, resources);
                m_SSRPass = new SSRPass(graph, resources);
                m_SSGIPass = new SSGIPass(graph, resources
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , m_RenderCapturePass
#endif
                    );
                m_ShadowCasterCulling = new ShadowCasterCulling();
                m_CascadeShadowPass = new CascadeShadowPass(graph, settings);
                m_LocalShadowPass = new LocalShadowPass(graph, resources, settings, renderContext.sceneWorld.lights.shadowCache);
                m_ShadowMomentPass = new ShadowMomentPass(graph, resources, settings);
                m_DeferredShadingPass = new DeferredShadingPass(graph, resources);
                m_ForwardPass = new ForwardPass(graph, settings);
                m_ScreenSpaceCompositePass = new ScreenSpaceCompositePass(graph, resources);
                m_ScreenSpaceHistoryPass = new ScreenSpaceHistoryPass(graph);
                m_SubsurfacePass = new SubsurfacePass(graph, resources, settings, renderContext.resourcePool);
                m_AtmosphericSkyFogPass = new AtmosphericSkyFogPass(graph, resources);
                m_TranslucentPass = new TranslucentPass(graph, settings);
                m_VolumetricCloudPass = new VolumetricCloudPass(graph, resources);
                m_VolumetricFogPass = new VolumetricFogPass(graph, resources);
                m_FogCompositePass = new FogCompositePass(graph, resources);
                m_ColorPyramidPass = new ColorPyramidPass(graph, resources);
                m_SuperResolutionPass = new SuperResolutionPass(graph, resources);
                m_AntiAliasingPass = new AntiAliasingPass(graph, resources
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , m_RenderCapturePass
#endif
                    );
                m_FinalOutputPass = new FinalOutputPass(graph, resources);
                m_PostProcessingPass = new PostProcessingPass(graph, resources, m_FinalOutputPass);
                m_UIOverlayPass = new UIOverlayPass(graph);
                m_UtilityPass = new UtilityPass(graph, resources, m_PostProcessingPass, m_UIOverlayPass, m_FinalOutputPass
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , m_DebugViewPass, m_RenderCapturePass
#endif
                    );
            }
            catch (Exception constructionFailure)
            {
                // The pass-owned diffusion buffer returns to the pool before the base releases the render context.
                try
                {
                    try
                    {
                        if (m_SubsurfacePass != null) m_SubsurfacePass.Dispose();
                    }
                    finally
                    {
                        ReleaseOwnedResources();
                    }
                }
                catch (Exception cleanupFailure) { throw new AggregateException(constructionFailure, cleanupFailure); }
                throw;
            }
        }

        // The camera's pass list in submission order. Each record call has its own CPU marker.
        private protected override void RecordCamera(in ViewContext context, CommandBuffer cmdBuffer)
        {
            // Shadow and light-loop constants are final once CullCamera built the lights; every pass of the
            // camera reads them from these two uniforms, uploaded ahead of the graph.
            context.state.shadowUniform.Upload(cmdBuffer, ShadowInputs.ResolveUniform(context, settings));
            context.state.lightLoopUniform.Upload(cmdBuffer, LightTileMaskPass.ResolveLightLoopUniform(context));

            // PHASE 0: frame-constant async. Zero RG-resource inputs; submit first
            // so the whole geometry raster window can overlap them.
            using (PipelineCpuMarkers.s_RecordUploadLightData.Auto())
            {
                m_LightUploadPass.UploadLightData(context.lights);
            }
            using (PipelineCpuMarkers.s_RecordComputeLightCookies.Auto())
            {
                m_LightCookiePass.ComputeLightCookies(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeReflectionProbes.Auto())
            {
                m_ReflectionProbePass.ComputeReflectionProbes(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeCombineLuts.Auto())
            {
                m_CombineLutPass.ComputeCombineLuts(context, CombineLutParameterUtility.FromVolumeStack(context.volumes.filmTonemap, context.volumes.colorGrading));
            }
            using (PipelineCpuMarkers.s_RecordComputeAtmosphericLUT.Auto())
            {
                m_AtmosphericLUTPass.ComputeAtmosphericLUT(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeTemporalColorPyramid.Auto())
            {
                m_TemporalColorSourcePass.ComputeTemporalColorPyramid(context);
            }

            // PHASE 1: geometry raster (shared depth attachment chain). Each pass creates its own draw list. Compile
            // places the producer before that pass's native group, so the producer does not split the group.
            MeshMotionBinding meshMotion;
            using (PipelineCpuMarkers.s_RecordPrepareMeshMotion.Auto())
            {
                meshMotion = context.state.meshMotionHistory.Prepare(context.meshes.transformVersions.current);
            }
            using (PipelineCpuMarkers.s_RecordSetMeshMotion.Auto())
            {
                renderContext.graph.SetMeshMotion(meshMotion);
            }
            bool twoStageMesh;
            using (PipelineCpuMarkers.s_RecordRegisterMeshHistoryOccluder.Auto())
            {
                twoStageMesh = m_MeshOcclusionPass.RegisterMeshHistoryOccluder(context, meshMotion);
            }
            if (twoStageMesh)
            {
                using (PipelineCpuMarkers.s_RecordRasterDepth.Auto())
                {
                    m_DepthPass.RasterDepth(context);
                }
                using (PipelineCpuMarkers.s_RecordRegisterMeshEarlyOccluder.Auto())
                {
                    m_MeshOcclusionPass.RegisterMeshEarlyOccluder(context);
                }
                using (PipelineCpuMarkers.s_RecordRasterDepthRecovery.Auto())
                {
                    m_MeshOcclusionPass.RasterDepthRecovery(context);
                }
            }
            else
            {
                using (PipelineCpuMarkers.s_RecordRasterDepth.Auto())
                {
                    m_DepthPass.RasterDepth(context);
                }
            }
            using (PipelineCpuMarkers.s_RecordRasterDBuffer.Auto())
            {
                m_DBufferPass.RasterDBuffer(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterGBuffer.Auto())
            {
                m_GBufferPass.RasterGBuffer(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterMotion.Auto())
            {
                m_MotionPass.RasterMotion(context);
            }

            // PHASE 2: depth-derived async. ZBin stays here so fog/deferred can consume it.
            using (PipelineCpuMarkers.s_RecordComputeHiZ.Auto())
            {
                m_HiZPass.ComputeHiZ(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeHalfResDownsample.Auto())
            {
                m_HalfResDownsamplePass.ComputeHalfResDownsample(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeLightTileMask.Auto())
            {
                m_LightTileMaskPass.ComputeLightTileMask(context);
            }

            // Dispatch near-deadline AO/contact before the longer previous-color effects.
            // Their graphics-independent results can overlap the shadow raster window.
            using (PipelineCpuMarkers.s_RecordTraceOcclusion.Auto())
            {
                m_RTAOPass.TraceOcclusion(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeGroundTruthOcclusion.Auto())
            {
                m_GTAOPass.ComputeGroundTruthOcclusion(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeContactShadow.Auto())
            {
                m_ContactShadowPass.ComputeContactShadow(context);
            }
            // Previous-color effects. They read depth, Hi-Z, the G-buffer and the previous frame's color only, so one
            // position serves both queues: async compute overlaps them with shadows, and a device without it runs them
            // here on the graphics queue.
            using (PipelineCpuMarkers.s_RecordComputeScreenSpaceReflection.Auto())
            {
                m_SSRPass.ComputeScreenSpaceReflection(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeScreenSpaceIndirect.Auto())
            {
                m_SSGIPass.ComputeScreenSpaceIndirect(context);
            }

            // PHASE 3: shadow raster (longest ROP window).
            using (PipelineCpuMarkers.s_RecordCullShadowCasterSplits.Auto())
            {
                m_ShadowCasterCulling.CullShadowCasterSplits(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterCascadeShadow.Auto())
            {
                m_CascadeShadowPass.RasterCascadeShadow(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterLocalShadow.Auto())
            {
                m_LocalShadowPass.RasterLocalShadow(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeShadowMoments.Auto())
            {
                m_ShadowMomentPass.ComputeShadowMoments(context);
            }

            // PHASE 6: base lighting, forward, indirect replacement and diffuse-only SSS.
            using (PipelineCpuMarkers.s_RecordComputeDeferredShading.Auto())
            {
                m_DeferredShadingPass.ComputeDeferredShading(context);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (PipelineCpuMarkers.s_RecordRecordDeferredLightingCapture.Auto())
            {
                m_RenderCapturePass.RecordDeferredLightingCapture(context);
            }
#endif
            using (PipelineCpuMarkers.s_RecordRasterForward.Auto())
            {
                m_ForwardPass.RasterForward(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeScreenSpaceComposite.Auto())
            {
                m_ScreenSpaceCompositePass.ComputeScreenSpaceComposite(context);
            }
            // History copies stay after the shadow window; an early graphics copy
            // would join the async effects before independent shadow rasterization.
            using (PipelineCpuMarkers.s_RecordCopyHistorySSR.Auto())
            {
                m_ScreenSpaceHistoryPass.CopyHistorySSR(context);
            }
            using (PipelineCpuMarkers.s_RecordCopyHistorySSGI.Auto())
            {
                m_ScreenSpaceHistoryPass.CopyHistorySSGI(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeBurleySubsurface.Auto())
            {
                m_SubsurfacePass.ComputeBurleySubsurface(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeAtmosphericSkyAndFog.Auto())
            {
                m_AtmosphericSkyFogPass.ComputeAtmosphericSkyAndFog(context);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (PipelineCpuMarkers.s_RecordRecordLightingCapture.Auto())
            {
                m_RenderCapturePass.RecordLightingCapture(context);
            }
#endif

            // PHASE 7: T0 depth → Cloud/Fog → FogComposite → FoggedSceneColor → T0 → RefractionPyramid → T1 → T2
            using (PipelineCpuMarkers.s_RecordRasterTranslucentDepth.Auto())
            {
                m_TranslucentPass.RasterTranslucentDepth(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeVolumetricCloud.Auto())
            {
                m_VolumetricCloudPass.ComputeVolumetricCloud(context);
            }
            using (PipelineCpuMarkers.s_RecordCopyHistoryVolumetricCloud.Auto())
            {
                m_VolumetricCloudPass.CopyHistoryVolumetricCloud(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeVolumetricFog.Auto())
            {
                m_VolumetricFogPass.ComputeVolumetricFog(context);
            }
            using (PipelineCpuMarkers.s_RecordCopyHistoryVolumetricFog.Auto())
            {
                m_VolumetricFogPass.CopyHistoryVolumetricFog(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeFogComposite.Auto())
            {
                m_FogCompositePass.ComputeFogComposite(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterTranslucentT0.Auto())
            {
                m_TranslucentPass.RasterTranslucentT0(context);
            }
            using (PipelineCpuMarkers.s_RecordComputeColorPyramid.Auto())
            {
                m_ColorPyramidPass.ComputeColorPyramid(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterTranslucentT1.Auto())
            {
                m_TranslucentPass.RasterTranslucentT1(context);
            }
            using (PipelineCpuMarkers.s_RecordRasterTranslucentT2.Auto())
            {
                m_TranslucentPass.RasterTranslucentT2(context);
            }

            // PHASE 8: temporal resolve + post (TAA after fog/translucents)
            // Previews and probe faces have no temporal resolve; post reads FoggedSceneColor.
            if (context.temporalResolve == ETemporalResolve.SuperResolution)
            {
                using (PipelineCpuMarkers.s_RecordComputeSuperResolution.Auto())
                {
                    m_SuperResolutionPass.ComputeSuperResolution(context);
                }
            }
            else if (context.temporalResolve == ETemporalResolve.TAA)
            {
                using (PipelineCpuMarkers.s_RecordComputeAntiAliasing.Auto())
                {
                    m_AntiAliasingPass.ComputeAntiAliasing(context);
                }
            }
            using (PipelineCpuMarkers.s_RecordRecordCameraOutput.Auto())
            {
                m_UtilityPass.RecordCameraOutput(context);
            }
        }

        // The pass-owned diffusion buffer returns to the pool before the base disposes the render context.
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing) m_SubsurfacePass.Dispose();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}
