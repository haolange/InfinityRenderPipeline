using System;
using System.Collections.Generic;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // Per-camera inputs the registry needs to decide whether a requested feature can record this frame.
    internal struct FeatureSupportContext
    {
        // PipelineCapabilities.featureMask: the features the pipeline records, indexed by EFrameFeature.
        public bool[] pipelineFeatures;
        // InfinityRenderPipelineResources.featureResourceReasons: support that depends only on resources.
        public string[] resourceReasons;
        // From CameraRenderPolicy.temporal: jitter, history and temporal resolve are allowed.
        public bool temporal;
        public bool hasDiffusionProfiles;
        // The pipeline writes per-pixel light channels (InfinityRenderPipelineAsset Rendering Layers).
        public bool renderingLayers;
        // RTAO readiness is decided by the UnifiedRayTracing environment before the registry runs.
        public bool rayTracedOcclusionReady;
        public string rayTracedOcclusionReason;
    }

    internal sealed class FeatureDescriptor
    {
        public EFrameFeature id;
        // Features that must record (and be supported) for this one to record. Requesting this feature requests them.
        public EFrameFeature[] dependsOn = Array.Empty<EFrameFeature>();
        // The kernels the feature dispatches; the first missing one is its resource reason.
        public Func<PipelineKernels, ComputeKernel[]> kernels;
        // Other resources the feature reads; returns null when they are present, otherwise the reason.
        public Func<InfinityRenderPipelineRuntimeTextures, string> textures;
        // The feature reads or writes history and needs a temporal camera.
        public bool requiresTemporal;
        // Per-camera support predicate; returns null when supported, otherwise the reason.
        public Func<FeatureSupportContext, string> cameraSupport;
    }

    // One table describing, for every EFrameFeature, which compute kernels it needs, which features it depends on and
    // whether it needs a temporal camera. ResolveResourceSupport evaluates kernels and textures once when resources
    // load; FrameFeatureSet.ResolveFrame and ResolveContent state what the camera wants; Resolve turns that into the recorded set,
    // requests dependencies, and keeps a readable reason for everything that cannot record (shown in the Rendering
    // Debugger). A pass that records trusts the table and does not check its kernels again.
    internal static class FeatureRegistry
    {
        static readonly FeatureDescriptor[] s_Descriptors = Build();

        // The reason of every feature outside PipelineCapabilities.features.
        internal const string NotRecordedReason = "This pipeline does not record it.";

        // Every camera records the pipeline's required producers (PipelineCapabilities). Resources that cannot support one
        // fail when they resolve, not per frame.
        internal static void RequireProducerSupport(string[] resourceReasons, EFrameFeature[] requiredProducers)
        {
            foreach (EFrameFeature feature in requiredProducers)
            {
                string reason = resourceReasons[(int)feature];
                if (reason != null)
                {
                    throw new InvalidOperationException($"InfinityRP cannot render without {feature}: {reason}");
                }
            }
        }

        internal static FeatureDescriptor Get(EFrameFeature feature) => s_Descriptors[(int)feature];

        static FeatureDescriptor[] Build()
        {
            var table = new FeatureDescriptor[Enum.GetValues(typeof(EFrameFeature)).Length];
            void Add(FeatureDescriptor d) => table[(int)d.id] = d;

            Add(new FeatureDescriptor { id = EFrameFeature.Depth });
            Add(new FeatureDescriptor { id = EFrameFeature.GBuffer });
            Add(new FeatureDescriptor { id = EFrameFeature.DBuffer });
            Add(new FeatureDescriptor { id = EFrameFeature.Motion });
            Add(new FeatureDescriptor { id = EFrameFeature.DeferredShading });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.HiZ,
                kernels = k => new[] { k.hiZClosest, k.hiZFurthest }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.HalfResDownsample,
                kernels = k => new[] { k.halfResDownsample }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ColorPyramid,
                kernels = k => new[] { k.colorPyramid }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.TemporalColorPyramid,
                requiresTemporal = true,
                kernels = k => new[] { k.temporalColorMip }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.GTAO,
                requiresTemporal = true,
                dependsOn = new[] { EFrameFeature.HalfResDownsample },
                kernels = k => new[] { k.occlusionTrace, k.occlusionSpatialX, k.occlusionSpatialY, k.occlusionTemporal, k.occlusionUpsample }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.RTAO,
                cameraSupport = c => c.rayTracedOcclusionReady ? null : (string.IsNullOrEmpty(c.rayTracedOcclusionReason) ? "UnifiedRayTracing is not ready." : c.rayTracedOcclusionReason)
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ContactShadow,
                kernels = k => new[] { k.contactShadow }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SubsurfaceScattering,
                cameraSupport = c => c.hasDiffusionProfiles ? null : "The RP Asset has no live diffusion profiles."
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SSR,
                requiresTemporal = true,
                dependsOn = new[] { EFrameFeature.HiZ, EFrameFeature.TemporalColorPyramid },
                kernels = k => new[]
                {
                    k.ssrClearArgs, k.ssrClassify, k.ssrTrace, k.ssrResolve, k.ssrTemporal, k.ssrBilateral,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    k.ssrDebug,
#endif
                },
                textures = BlueNoiseSupport
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SSGI,
                requiresTemporal = true,
                dependsOn = new[] { EFrameFeature.HiZ, EFrameFeature.TemporalColorPyramid },
                kernels = k => new[]
                {
                    k.ssgiTrace, k.ssgiTemporal, k.ssgiSpatial, k.ssgiUpsample,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    k.ssgiTraceDebug,
#endif
                },
                textures = BlueNoiseSupport
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.TAA,
                requiresTemporal = true,
                kernels = k => new[]
                {
                    k.temporalAntiAliasing,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    k.temporalAntiAliasingDebug,
#endif
                }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SuperResolution,
                requiresTemporal = true,
                kernels = k => new[] { k.superResolution }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.VolumetricFog,
                requiresTemporal = true,
                kernels = k => new[] { k.fogScatterDensity, k.fogIntegrate, k.fogTemporal, k.fogComposite }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.VolumetricCloud,
                requiresTemporal = true,
                kernels = k => new[] { k.volumetricCloud, k.fogComposite }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.PostProcess,
                kernels = k => new[] { k.bloomDownsample, k.bloomUpsample, k.exposureClear, k.exposureHistogram, k.exposureReduce }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.LocalLightList,
                kernels = k => new[] { k.lightTileMask }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ShadowMoments,
                kernels = k => new[] { k.momentFromDepth, k.momentBlurH, k.momentBlurV }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.LightCookies,
                kernels = k => new[] { k.cookieCopy2D, k.cookieCubeToOctahedral, k.cookieDownsample }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ReflectionProbes,
                kernels = k => new[] { k.filterCubeToArray, k.lightTileMask }
            });
            Add(new FeatureDescriptor { id = EFrameFeature.Display });

            for (int i = 0; i < table.Length; ++i)
            {
                if (table[i] == null)
                    throw new InvalidOperationException($"FeatureRegistry has no descriptor for {(EFrameFeature)i}.");
            }
            return table;
        }

        // Every reason is a literal or a resource reason resolved when resources load, so a dependency's composed reason is
        // built once per distinct pair and then read from this table (AGENTS R14, R17).
        static readonly Dictionary<string, string>[] s_DependsOnReasons = CreateDependsOnReasonTables();

        static Dictionary<string, string>[] CreateDependsOnReasonTables()
        {
            var tables = new Dictionary<string, string>[Enum.GetValues(typeof(EFrameFeature)).Length];
            for (int i = 0; i < tables.Length; ++i)
            {
                tables[i] = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            return tables;
        }

        static string DependsOnReason(EFrameFeature dependency, string dependencyReason)
        {
            Dictionary<string, string> table = s_DependsOnReasons[(int)dependency];
            if (!table.TryGetValue(dependencyReason, out string reason))
            {
                reason = "Depends on " + dependency + ": " + dependencyReason;
                table.Add(dependencyReason, reason);
            }
            return reason;
        }

        static string BlueNoiseSupport(InfinityRenderPipelineRuntimeTextures textures)
        {
            return textures.HasBlueNoise ? null : "RuntimeTextures is missing the blue-noise Sobol textures.";
        }

        // Resource-only support for every feature, evaluated when resources load or shaders are imported.
        internal static string[] ResolveResourceSupport(PipelineKernels kernels, InfinityRenderPipelineRuntimeTextures textures)
        {
            var reasons = new string[s_Descriptors.Length];
            for (int i = 0; i < s_Descriptors.Length; ++i)
            {
                FeatureDescriptor descriptor = s_Descriptors[i];
                if (descriptor.kernels != null)
                {
                    foreach (ComputeKernel kernel in descriptor.kernels(kernels))
                    {
                        if (!kernel.isValid)
                        {
                            reasons[i] = kernel.missingReason;
                            break;
                        }
                    }
                }
                if (reasons[i] == null && descriptor.textures != null)
                {
                    reasons[i] = descriptor.textures(textures);
                }
            }
            return reasons;
        }

        // Turns the direct requests in features into the recorded set. A supported feature requests its dependencies;
        // an unsupported one does not, so nothing records only to feed a feature that cannot run.
        internal static void Resolve(FrameFeatureSet features, in FeatureSupportContext context)
        {
            int count = s_Descriptors.Length;
            var supportReason = features.supportReasons;
            Array.Clear(supportReason, 0, count);
            var evaluated = features.supportEvaluated;
            Array.Clear(evaluated, 0, count);
            for (int i = 0; i < count; ++i)
            {
                Evaluate((EFrameFeature)i, context, supportReason, evaluated);
            }

            var requested = features.requestedClosure;
            Array.Clear(requested, 0, count);
            for (int i = 0; i < count; ++i)
            {
                if (features.IsRequested((EFrameFeature)i) && supportReason[i] == null)
                {
                    RequestClosure((EFrameFeature)i, requested);
                }
            }

            for (int i = 0; i < count; ++i)
            {
                var feature = (EFrameFeature)i;
                bool direct = features.IsRequested(feature);
                features.SetResolved(feature, requested: requested[i] || direct, supported: supportReason[i] == null, reason: supportReason[i]);
            }
        }

        static void RequestClosure(EFrameFeature feature, bool[] requested)
        {
            if (requested[(int)feature])
            {
                return;
            }
            requested[(int)feature] = true;
            foreach (EFrameFeature dependency in s_Descriptors[(int)feature].dependsOn)
            {
                RequestClosure(dependency, requested);
            }
        }

        static string Evaluate(EFrameFeature feature, in FeatureSupportContext context, string[] reasons, bool[] evaluated)
        {
            int index = (int)feature;
            if (evaluated[index])
            {
                return reasons[index];
            }
            evaluated[index] = true;
            FeatureDescriptor descriptor = s_Descriptors[index];
            string reason = null;
            if (!context.pipelineFeatures[index])
            {
                reason = NotRecordedReason;
            }

            if (reason == null && descriptor.requiresTemporal && !context.temporal)
            {
                reason = "This camera type renders without history (preview or reflection probe).";
            }

            if (reason == null)
            {
                reason = context.resourceReasons[index];
            }

            if (reason == null && descriptor.cameraSupport != null)
            {
                reason = descriptor.cameraSupport(context);
            }

            if (reason == null)
            {
                foreach (EFrameFeature dependency in descriptor.dependsOn)
                {
                    string dependencyReason = Evaluate(dependency, context, reasons, evaluated);
                    if (dependencyReason != null)
                    {
                        reason = DependsOnReason(dependency, dependencyReason);
                        break;
                    }
                }
            }

            reasons[index] = reason;
            return reason;
        }
    }
}
