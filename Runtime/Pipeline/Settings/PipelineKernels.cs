using System;
using InfinityTech.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Every compute kernel the pipeline dispatches, resolved once from RuntimeShaders when resources load or a compute
    // shader is imported. A kernel a feature needs reports its missing reason through FeatureRegistry; any other
    // kernel fails resolution in RequireUngatedKernels.
    internal sealed class PipelineKernels
    {
        internal readonly ComputeKernel hiZClosest;
        internal readonly ComputeKernel hiZFurthest;
        internal readonly ComputeKernel halfResDownsample;

        internal readonly ComputeKernel deferredShading;
        internal readonly ComputeKernel lightTileMask;
        internal readonly ComputeKernel burleySubsurface;
        internal readonly ComputeKernel composeSubsurface;
        internal readonly ComputeKernel cookieCopy2D;
        internal readonly ComputeKernel cookieCubeToOctahedral;
        internal readonly ComputeKernel cookieDownsample;
        internal readonly ComputeKernel filterCubeToArray;
        internal readonly ComputeKernel filterArrayToArray;

        internal readonly ComputeKernel contactShadow;
        internal readonly ComputeKernel momentFromDepth;
        internal readonly ComputeKernel momentBlurH;
        internal readonly ComputeKernel momentBlurV;

        internal readonly ComputeKernel occlusionTrace;
        internal readonly ComputeKernel occlusionSpatialX;
        internal readonly ComputeKernel occlusionSpatialY;
        internal readonly ComputeKernel occlusionTemporal;
        internal readonly ComputeKernel occlusionUpsample;

        internal readonly ComputeKernel ssrClearArgs;
        internal readonly ComputeKernel ssrClassify;
        internal readonly ComputeKernel ssrTrace;
        internal readonly ComputeKernel ssrResolve;
        internal readonly ComputeKernel ssrTemporal;
        internal readonly ComputeKernel ssrBilateral;
        internal readonly ComputeKernel ssrDebug;

        internal readonly ComputeKernel ssgiTrace;
        internal readonly ComputeKernel ssgiTemporal;
        internal readonly ComputeKernel ssgiSpatial;
        internal readonly ComputeKernel ssgiUpsample;
        internal readonly ComputeKernel ssgiTraceDebug;

        internal readonly ComputeKernel screenSpaceComposite;
        internal readonly ComputeKernel colorPyramid;
        internal readonly ComputeKernel temporalColorMip;

        internal readonly ComputeKernel temporalAntiAliasing;
        internal readonly ComputeKernel temporalAntiAliasingDebug;
        internal readonly ComputeKernel superResolution;

        internal readonly ComputeKernel transmittanceLUT;
        internal readonly ComputeKernel multiScatteringLUT;
        internal readonly ComputeKernel skyViewLUT;
        internal readonly ComputeKernel aerialPerspectiveLUT;
        internal readonly ComputeKernel atmosphereCubemap;
        internal readonly ComputeKernel sunBuffer;
        internal readonly ComputeKernel atmosphereComposite;
        internal readonly ComputeKernel atmosphereSHProject;
        internal readonly ComputeKernel atmosphereSHReduce;

        internal readonly ComputeKernel fogScatterDensity;
        internal readonly ComputeKernel fogIntegrate;
        internal readonly ComputeKernel fogTemporal;
        internal readonly ComputeKernel volumetricCloud;
        internal readonly ComputeKernel fogComposite;

        internal readonly ComputeKernel bloomDownsample;
        internal readonly ComputeKernel bloomUpsample;
        internal readonly ComputeKernel exposureClear;
        internal readonly ComputeKernel exposureHistogram;
        internal readonly ComputeKernel exposureReduce;
        internal readonly ComputeKernel combineLut;

        internal readonly ComputeKernel debugViewGBuffer;
        internal readonly ComputeKernel debugViewMotion;
        internal readonly ComputeKernel debugViewSceneColor;
        internal readonly ComputeKernel debugViewOptional;
        internal readonly ComputeKernel debugViewMissing;
        internal readonly ComputeKernel debugViewShadow;
        internal readonly ComputeKernel debugViewLightLoop;
        internal readonly ComputeKernel debugViewProbeAtlas;

        // Local keywords select kernel variants; a shader that is not assigned leaves its keywords default.
        internal readonly LocalKeyword deferredShadingSubsurface;
        internal readonly LocalKeyword deferredShadingOcclusion;
        internal readonly LocalKeyword deferredShadingContactShadow;
        internal readonly LocalKeyword compositeSubsurface;
        internal readonly LocalKeyword compositeSSR;
        internal readonly LocalKeyword compositeSSGI;
        internal readonly LocalKeyword fogCompositeFog;
        internal readonly LocalKeyword fogCompositeCloud;

        const string Source = "GlobalSettings RuntimeShaders.";

        internal PipelineKernels(InfinityRenderPipelineRuntimeShaders shaders)
        {
            const string hiZ = Source + nameof(InfinityRenderPipelineRuntimeShaders.hiZShader);
            hiZClosest = ComputeKernel.Resolve(shaders.hiZShader, hiZ, "HiZ_Generation");
            hiZFurthest = ComputeKernel.Resolve(shaders.hiZShader, hiZ, "HiZ_FurthestGeneration");
            halfResDownsample = ComputeKernel.Resolve(shaders.halfResDownsampleShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.halfResDownsampleShader), "HalfResDownsample");

            deferredShading = ComputeKernel.Resolve(shaders.deferredShadingShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.deferredShadingShader), "DeferredShadingCS");
            lightTileMask = ComputeKernel.Resolve(shaders.lightTileMaskShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.lightTileMaskShader), "BuildTileMask");
            const string subsurface = Source + nameof(InfinityRenderPipelineRuntimeShaders.subsurfaceShader);
            burleySubsurface = ComputeKernel.Resolve(shaders.subsurfaceShader, subsurface, "BurleySubsurfaceCS");
            composeSubsurface = ComputeKernel.Resolve(shaders.subsurfaceShader, subsurface, "ComposeSubsurfaceCS");
            const string cookie = Source + nameof(InfinityRenderPipelineRuntimeShaders.lightCookieShader);
            cookieCopy2D = ComputeKernel.Resolve(shaders.lightCookieShader, cookie, "CookieCopy2D");
            cookieCubeToOctahedral = ComputeKernel.Resolve(shaders.lightCookieShader, cookie, "CookieCubeToOctahedral");
            cookieDownsample = ComputeKernel.Resolve(shaders.lightCookieShader, cookie, "CookieDownsample");
            const string cubeFilter = Source + nameof(InfinityRenderPipelineRuntimeShaders.cubeFilterShader);
            filterCubeToArray = ComputeKernel.Resolve(shaders.cubeFilterShader, cubeFilter, "FilterCubeToArray");
            filterArrayToArray = ComputeKernel.Resolve(shaders.cubeFilterShader, cubeFilter, "FilterArrayToArray");

            contactShadow = ComputeKernel.Resolve(shaders.contactShadowShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.contactShadowShader), "ContactShadowCS");
            const string moment = Source + nameof(InfinityRenderPipelineRuntimeShaders.shadowMomentShader);
            momentFromDepth = ComputeKernel.Resolve(shaders.shadowMomentShader, moment, "MomentFromDepth");
            momentBlurH = ComputeKernel.Resolve(shaders.shadowMomentShader, moment, "MomentBlurH");
            momentBlurV = ComputeKernel.Resolve(shaders.shadowMomentShader, moment, "MomentBlurV");

            const string occlusion = Source + nameof(InfinityRenderPipelineRuntimeShaders.ssaoShader);
            occlusionTrace = ComputeKernel.Resolve(shaders.ssaoShader, occlusion, "OcclusionTrace");
            occlusionSpatialX = ComputeKernel.Resolve(shaders.ssaoShader, occlusion, "OcclusionSpatialX");
            occlusionSpatialY = ComputeKernel.Resolve(shaders.ssaoShader, occlusion, "OcclusionSpatialY");
            occlusionTemporal = ComputeKernel.Resolve(shaders.ssaoShader, occlusion, "OcclusionTemporal");
            occlusionUpsample = ComputeKernel.Resolve(shaders.ssaoShader, occlusion, "OcclusionUpsample");

            const string ssr = Source + nameof(InfinityRenderPipelineRuntimeShaders.ssrShader);
            ssrClearArgs = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRClearArgs");
            ssrClassify = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRClassify");
            ssrTrace = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRTrace");
            ssrResolve = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRResolve");
            ssrTemporal = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRTemporal");
            ssrBilateral = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRBilateral");
            ssrDebug = ComputeKernel.Resolve(shaders.ssrShader, ssr, "SSRDebug");

            const string ssgi = Source + nameof(InfinityRenderPipelineRuntimeShaders.ssgiShader);
            ssgiTrace = ComputeKernel.Resolve(shaders.ssgiShader, ssgi, "SSGITrace");
            ssgiTemporal = ComputeKernel.Resolve(shaders.ssgiShader, ssgi, "SSGITemporal");
            ssgiSpatial = ComputeKernel.Resolve(shaders.ssgiShader, ssgi, "SSGISpatial");
            ssgiUpsample = ComputeKernel.Resolve(shaders.ssgiShader, ssgi, "SSGIUpsample");
            ssgiTraceDebug = ComputeKernel.Resolve(shaders.ssgiShader, ssgi, "SSGITraceDebug");

            screenSpaceComposite = ComputeKernel.Resolve(shaders.screenSpaceCompositeShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.screenSpaceCompositeShader), "ScreenSpaceComposite");
            const string colorPyramidField = Source + nameof(InfinityRenderPipelineRuntimeShaders.colorPyramidShader);
            colorPyramid = ComputeKernel.Resolve(shaders.colorPyramidShader, colorPyramidField, "KMain");
            temporalColorMip = ComputeKernel.Resolve(shaders.colorPyramidShader, colorPyramidField, "TemporalColorMip");

            const string taa = Source + nameof(InfinityRenderPipelineRuntimeShaders.taaShader);
            temporalAntiAliasing = ComputeKernel.Resolve(shaders.taaShader, taa, "Main");
            temporalAntiAliasingDebug = ComputeKernel.Resolve(shaders.taaShader, taa, "MainDebug");
            superResolution = ComputeKernel.Resolve(shaders.superResolutionShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.superResolutionShader), "SuperResolutionCS");

            const string atmosphere = Source + nameof(InfinityRenderPipelineRuntimeShaders.atmosphericLUTShader);
            transmittanceLUT = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "TransmittanceLUT");
            multiScatteringLUT = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "MultiScatteringLUT");
            skyViewLUT = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "SkyViewLUT");
            aerialPerspectiveLUT = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "AerialPerspectiveLUT");
            atmosphereCubemap = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "AtmosphereCubemap");
            sunBuffer = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "SunBuffer");
            atmosphereComposite = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "AtmosphereComposite");
            atmosphereSHProject = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "AtmosphereSHProject");
            atmosphereSHReduce = ComputeKernel.Resolve(shaders.atmosphericLUTShader, atmosphere, "AtmosphereSHReduce");

            const string fog = Source + nameof(InfinityRenderPipelineRuntimeShaders.volumetricFogShader);
            fogScatterDensity = ComputeKernel.Resolve(shaders.volumetricFogShader, fog, "ScatterDensity");
            fogIntegrate = ComputeKernel.Resolve(shaders.volumetricFogShader, fog, "Integrate");
            fogTemporal = ComputeKernel.Resolve(shaders.volumetricFogShader, fog, "Temporal");
            volumetricCloud = ComputeKernel.Resolve(shaders.volumetricCloudShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.volumetricCloudShader), "VolumetricCloudCS");
            fogComposite = ComputeKernel.Resolve(shaders.fogCompositeShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.fogCompositeShader), "FogComposite");

            const string post = Source + nameof(InfinityRenderPipelineRuntimeShaders.postProcessingShader);
            bloomDownsample = ComputeKernel.Resolve(shaders.postProcessingShader, post, "BloomDownsample");
            bloomUpsample = ComputeKernel.Resolve(shaders.postProcessingShader, post, "BloomUpsample");
            exposureClear = ComputeKernel.Resolve(shaders.postProcessingShader, post, "ExposureClear");
            exposureHistogram = ComputeKernel.Resolve(shaders.postProcessingShader, post, "ExposureHistogram");
            exposureReduce = ComputeKernel.Resolve(shaders.postProcessingShader, post, "ExposureReduce");
            combineLut = ComputeKernel.Resolve(shaders.combineLUTShader, Source + nameof(InfinityRenderPipelineRuntimeShaders.combineLUTShader), "MainCS");

            const string debugView = Source + nameof(InfinityRenderPipelineRuntimeShaders.debugViewShader);
            debugViewGBuffer = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewGBuffer");
            debugViewMotion = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewMotion");
            debugViewSceneColor = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewSceneColor");
            debugViewOptional = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewOptional");
            debugViewMissing = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewMissing");
            debugViewShadow = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewShadow");
            debugViewLightLoop = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewLightLoop");
            debugViewProbeAtlas = ComputeKernel.Resolve(shaders.debugViewShader, debugView, "DebugViewProbeAtlas");

            deferredShadingSubsurface = Keyword(shaders.deferredShadingShader, "INFINITY_SSS_OUTPUT");
            deferredShadingOcclusion = Keyword(shaders.deferredShadingShader, "DEFERRED_AO");
            deferredShadingContactShadow = Keyword(shaders.deferredShadingShader, "DEFERRED_CONTACT_SHADOW");
            compositeSubsurface = Keyword(shaders.screenSpaceCompositeShader, "INFINITY_SSS_OUTPUT");
            compositeSSR = Keyword(shaders.screenSpaceCompositeShader, "COMPOSITE_SSR");
            compositeSSGI = Keyword(shaders.screenSpaceCompositeShader, "COMPOSITE_SSGI");
            fogCompositeFog = Keyword(shaders.fogCompositeShader, "FOG_COMPOSITE_FOG");
            fogCompositeCloud = Keyword(shaders.fogCompositeShader, "FOG_COMPOSITE_CLOUD");
        }

        static LocalKeyword Keyword(ComputeShader shader, string name) => shader != null ? new LocalKeyword(shader, name) : default;

        // Kernels no feature gates record whenever their pass runs, so a missing one is a broken install.
        internal void RequireUngatedKernels()
        {
            Require(filterArrayToArray,
                transmittanceLUT, multiScatteringLUT, skyViewLUT, aerialPerspectiveLUT, atmosphereCubemap, sunBuffer,
                atmosphereComposite, atmosphereSHProject, atmosphereSHReduce, combineLut,
                debugViewGBuffer, debugViewMotion, debugViewSceneColor, debugViewOptional, debugViewMissing,
                debugViewShadow, debugViewLightLoop, debugViewProbeAtlas);
        }

        static void Require(params ComputeKernel[] kernels)
        {
            foreach (ComputeKernel kernel in kernels)
            {
                if (!kernel.isValid)
                {
                    throw new InvalidOperationException("InfinityRP: " + kernel.missingReason);
                }
            }
        }
    }

    // Pass indices of the GlobalSettings blit material, resolved with the kernels. Every pass is required.
    internal readonly struct BlitShaderPasses
    {
        internal readonly int cameraMotion;
        internal readonly int finalOutput;
        internal readonly int postCombine;
        internal readonly int upscaleDepth;
        internal readonly int copyShadowDepthTile;

        internal BlitShaderPasses(Material material)
        {
            cameraMotion = Find(material, "RasterCameraMotion");
            finalOutput = Find(material, "RasterFinalOutput");
            postCombine = Find(material, "RasterPostCombine");
            upscaleDepth = Find(material, "RasterUpscaleDepth");
            copyShadowDepthTile = Find(material, "RasterCopyShadowDepthTile");
        }

        static int Find(Material material, string name)
        {
            int pass = material.FindPass(name);
            if (pass < 0)
            {
                throw new InvalidOperationException("InfinityRP: GlobalSettings RuntimeMaterials.blitMaterial has no pass " + name + ".");
            }
            return pass;
        }
    }
}
