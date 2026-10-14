using System;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Inputs the registry needs to decide whether a requested feature can record this frame.
    internal struct FeatureSupportContext
    {
        public InfinityRenderPipelineRuntimeShaders shaders;
        public InfinityRenderPipelineRuntimeTextures textures;
        // From CameraRenderPolicy.temporal: jitter, history and temporal resolve are allowed.
        public bool temporal;
        // RTAO readiness is decided by the UnifiedRayTracing environment before the registry runs.
        public bool rayTracedOcclusionReady;
        public string rayTracedOcclusionReason;
        // The blit material exposes the Present pass.
        public bool presentAvailable;
    }

    internal readonly struct FeatureShaderRequirement
    {
        public readonly string shaderField;
        public readonly Func<InfinityRenderPipelineRuntimeShaders, ComputeShader> shader;
        public readonly string[] kernels;

        public FeatureShaderRequirement(string shaderField, Func<InfinityRenderPipelineRuntimeShaders, ComputeShader> shader, params string[] kernels)
        {
            this.shaderField = shaderField;
            this.shader = shader;
            this.kernels = kernels;
        }
    }

    internal sealed class FeatureDescriptor
    {
        public EFrameFeature id;
        // Features that must record (and be supported) for this one to record. Requesting this feature requests them.
        public EFrameFeature[] dependsOn = Array.Empty<EFrameFeature>();
        public FeatureShaderRequirement[] shaders = Array.Empty<FeatureShaderRequirement>();
        // The feature reads or writes history and needs a temporal camera.
        public bool requiresTemporal;
        // Extra support predicate; returns null when supported, otherwise the reason.
        public Func<FeatureSupportContext, string> customSupport;
    }

    // One table describing, for every EFrameFeature, which compute kernels it needs, which features it depends on and
    // whether it needs a temporal camera. ConfigureFrameFeatures only states what the camera wants; Resolve turns
    // that into the recorded set, requests dependencies, and keeps a readable reason for everything that cannot
    // record (shown in the Rendering Debugger). This replaces per-pass kernel checks that ran mid-recording.
    internal static class FeatureRegistry
    {
        static readonly FeatureDescriptor[] s_Descriptors = Build();

        internal static FeatureDescriptor Get(EFrameFeature feature) => s_Descriptors[(int)feature];

        static FeatureDescriptor[] Build()
        {
            var table = new FeatureDescriptor[Enum.GetValues(typeof(EFrameFeature)).Length];
            void Add(FeatureDescriptor d) => table[(int)d.id] = d;

            Add(new FeatureDescriptor { id = EFrameFeature.Depth });
            Add(new FeatureDescriptor { id = EFrameFeature.GBuffer });
            Add(new FeatureDescriptor { id = EFrameFeature.DBuffer });
            Add(new FeatureDescriptor { id = EFrameFeature.Motion });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.DeferredShading,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.deferredShadingShader), s => s.deferredShadingShader, "DeferredShadingCS") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.HiZ,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.hiZShader), s => s.hiZShader, "HiZ_Generation") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.HalfResDownsample,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.halfResDownsampleShader), s => s.halfResDownsampleShader, "HalfResDownsample") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ColorPyramid,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.colorPyramidShader), s => s.colorPyramidShader, "KMain") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.OpaqueLightingPyramid,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.colorPyramidShader), s => s.colorPyramidShader, "KMain") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.GTAO,
                requiresTemporal = true,
                dependsOn = new[] { EFrameFeature.HalfResDownsample },
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.ssaoShader), s => s.ssaoShader,
                    "OcclusionTrace", "OcclusionSpatialX", "OcclusionSpatialY", "OcclusionTemporal", "OcclusionUpsample") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.RTAO,
                customSupport = c => c.rayTracedOcclusionReady ? null : (string.IsNullOrEmpty(c.rayTracedOcclusionReason) ? "UnifiedRayTracing is not ready." : c.rayTracedOcclusionReason)
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ContactShadow,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.contactShadowShader), s => s.contactShadowShader, "ContactShadowCS") }
            });
            var composite = new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.screenSpaceCompositeShader), s => s.screenSpaceCompositeShader, "ScreenSpaceComposite");
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SSR,
                requiresTemporal = true,
                dependsOn = new[] { EFrameFeature.HiZ, EFrameFeature.OpaqueLightingPyramid },
                shaders = new[]
                {
                    new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.ssrShader), s => s.ssrShader, SSRPassUtilityData.RequiredKernels),
                    new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.utilityShader), s => s.utilityShader, ComputeUtility.ClearIndirectArgsKernelName),
                    composite
                },
                customSupport = c => c.textures != null && c.textures.HasBlueNoise ? null : "RuntimeTextures is missing the blue-noise Sobol textures."
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SSGI,
                requiresTemporal = true,
                dependsOn = new[] { EFrameFeature.HiZ, EFrameFeature.OpaqueLightingPyramid },
                shaders = new[]
                {
                    new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.ssgiShader), s => s.ssgiShader, SSGIPassUtilityData.RequiredKernels),
                    new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.utilityShader), s => s.utilityShader, ComputeUtility.ClearIndirectArgsKernelName),
                    composite
                },
                customSupport = c => c.textures != null && c.textures.HasBlueNoise ? null : "RuntimeTextures is missing the blue-noise Sobol textures."
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.TAA,
                requiresTemporal = true,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.taaShader), s => s.taaShader, "Main", "MainDebug", "Sharpen") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.SuperResolution,
                requiresTemporal = true,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.superResolutionShader), s => s.superResolutionShader, "SuperResolutionCS") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.VolumetricFog,
                requiresTemporal = true,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.volumetricFogShader), s => s.volumetricFogShader, "ScatterDensity", "Integrate", "Temporal") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.VolumetricCloud,
                requiresTemporal = true,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.volumetricCloudShader), s => s.volumetricCloudShader, "VolumetricCloudCS") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.PostProcess,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.postProcessingShader), s => s.postProcessingShader,
                    "BloomDownsample", "BloomUpsample", "FinalCombine", "ExposureClear", "ExposureHistogram", "ExposureReduce", "FinalCombineNoBloom") }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.LocalLightList,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.lightTileMaskShader), s => s.lightTileMaskShader, LightTileMaskPassUtilityData.KernelName) }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ShadowMoments,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.shadowMomentShader), s => s.shadowMomentShader,
                    ShadowMomentPassUtilityData.KernelFromDepth, ShadowMomentPassUtilityData.KernelBlurH, ShadowMomentPassUtilityData.KernelBlurV) }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.LightCookies,
                shaders = new[] { new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.lightCookieShader), s => s.lightCookieShader,
                    LightCookiePassUtilityData.KernelCopy2D, LightCookiePassUtilityData.KernelCubeToOctahedral, LightCookiePassUtilityData.KernelDownsample) }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.ReflectionProbes,
                shaders = new[]
                {
                    new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.cubeFilterShader), s => s.cubeFilterShader, CubeFilterUtilityData.KernelCubeToArray),
                    new FeatureShaderRequirement(nameof(InfinityRenderPipelineRuntimeShaders.lightTileMaskShader), s => s.lightTileMaskShader, LightTileMaskPassUtilityData.KernelName),
                }
            });
            Add(new FeatureDescriptor
            {
                id = EFrameFeature.Display,
                customSupport = c => c.presentAvailable ? null : "The GlobalSettings blit material has no Present pass."
            });

            for (int i = 0; i < table.Length; ++i)
            {
                if (table[i] == null)
                    throw new InvalidOperationException($"FeatureRegistry has no descriptor for {(EFrameFeature)i}.");
            }
            return table;
        }

        // Turns the direct requests in features into the recorded set. A supported feature requests its dependencies;
        // an unsupported one does not, so nothing records only to feed a feature that cannot run.
        internal static void Resolve(FrameFeatureSet features, in FeatureSupportContext context)
        {
            int count = s_Descriptors.Length;
            var supportReason = new string[count];
            var evaluated = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                Evaluate((EFrameFeature)i, context, supportReason, evaluated);
            }

            var requested = new bool[count];
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
            if (requested[(int)feature]) return;
            requested[(int)feature] = true;
            foreach (EFrameFeature dependency in s_Descriptors[(int)feature].dependsOn)
            {
                RequestClosure(dependency, requested);
            }
        }

        static string Evaluate(EFrameFeature feature, in FeatureSupportContext context, string[] reasons, bool[] evaluated)
        {
            int index = (int)feature;
            if (evaluated[index]) return reasons[index];
            evaluated[index] = true;
            FeatureDescriptor descriptor = s_Descriptors[index];
            string reason = null;
            if (descriptor.requiresTemporal && !context.temporal)
            {
                reason = "This camera type renders without history (preview or reflection probe).";
            }

            if (reason == null)
            {
                foreach (FeatureShaderRequirement requirement in descriptor.shaders)
                {
                    ComputeShader shader = context.shaders != null ? requirement.shader(context.shaders) : null;
                    if (shader == null)
                    {
                        reason = $"GlobalSettings RuntimeShaders.{requirement.shaderField} is not assigned.";
                        break;
                    }
                    foreach (string kernel in requirement.kernels)
                    {
                        if (!shader.HasKernel(kernel))
                        {
                            reason = $"{shader.name} has no kernel {kernel}.";
                            break;
                        }
                    }
                    if (reason != null) break;
                }
            }

            if (reason == null && descriptor.customSupport != null)
            {
                reason = descriptor.customSupport(context);
            }

            if (reason == null)
            {
                foreach (EFrameFeature dependency in descriptor.dependsOn)
                {
                    string dependencyReason = Evaluate(dependency, context, reasons, evaluated);
                    if (dependencyReason != null)
                    {
                        reason = $"Depends on {dependency}: {dependencyReason}";
                        break;
                    }
                }
            }

            reasons[index] = reason;
            return reason;
        }
    }
}
