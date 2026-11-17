using System;

namespace InfinityTech.Rendering.Pipeline
{
    public enum EFrameFeature
    {
        Depth,
        GBuffer,
        Motion,
        HiZ,
        GTAO,
        RTAO,
        ContactShadow,
        SSR,
        SSGI,
        DeferredShading,
        TAA,
        SuperResolution,
        ColorPyramid,
        VolumetricFog,
        VolumetricCloud,
        PostProcess,
        Display,
        DBuffer,
        TemporalColorPyramid,
        HalfResDownsample,
        // Content-phase features: requested after LightContext.Build from what is visible.
        // Local lights are visible.
        LocalLightList,
        // A shadowed light filters with VSM / EVSM.
        ShadowMoments,
        // A visible light has a cookie or IES profile in the atlas.
        LightCookies,
        // Reflection probes reach the screen.
        ReflectionProbes,
        SubsurfaceScattering
    }

    // Every input of a camera's frame-phase request (R2): ResolveFrame is a function of these alone.
    internal struct FrameFeatureInputs
    {
        // PipelineCapabilities.requiredProducers: every camera requests these.
        public EFrameFeature[] requiredProducers;
        public ResolvedFrameSettings frameSettings;
        public ETemporalResolve temporalResolve;
        public VolumeSnapshot volumes;
        // Published decal count of this frame's scene transaction.
        public int decalCount;
        public FeatureSupportContext support;
    }

    // Every input of the content phase: what the camera's visible lights need, after LightContext.Build.
    internal struct ContentFeatureInputs
    {
        public bool localLightList;
        // Contact shadows need the frame setting, an active volume and a light that casts them.
        public bool contactShadowsEnabled, contactShadowVolume, contactShadowCasters;
        public bool lightCookies;
        public bool reflectionProbes;
        public bool shadowMoments;
    }

    public class FrameFeatureSet
    {
        struct FeatureState
        {
            public bool requested;
            public bool supported;
            public bool produced;
            // Why a requested feature cannot record (null when supported). Filled by FeatureRegistry.Resolve.
            public string unsupportedReason;
        }

        internal readonly string[] supportReasons = new string[FeatureCount];
        internal readonly bool[] supportEvaluated = new bool[FeatureCount], requestedClosure = new bool[FeatureCount];

        readonly FeatureState[] m_States = new FeatureState[FeatureCount];

        public static int Count => FeatureCount;

        static readonly int FeatureCount = Enum.GetNames(typeof(EFrameFeature)).Length;

        public void Reset()
        {
            for (int i = 0; i < m_States.Length; ++i)
            {
                m_States[i] = default;
            }
        }

        public void Request(EFrameFeature feature)
        {
            m_States[Index(feature)].requested = true;
        }

        public void MarkSupported(EFrameFeature feature)
        {
            m_States[Index(feature)].supported = true;
        }

        public void MarkProduced(EFrameFeature feature)
        {
            m_States[Index(feature)].produced = true;
        }

        public bool IsRequested(EFrameFeature feature)
        {
            return m_States[Index(feature)].requested;
        }

        public bool IsSupported(EFrameFeature feature)
        {
            return m_States[Index(feature)].supported;
        }

        public string GetUnsupportedReason(EFrameFeature feature)
        {
            return m_States[Index(feature)].unsupportedReason;
        }

        internal void SetResolved(EFrameFeature feature, bool requested, bool supported, string reason)
        {
            ref FeatureState state = ref m_States[Index(feature)];
            state.requested = requested;
            state.supported = supported;
            state.unsupportedReason = supported ? null : reason;
        }

        public bool IsProduced(EFrameFeature feature)
        {
            return m_States[Index(feature)].produced;
        }

        public bool ShouldRecord(EFrameFeature feature)
        {
            FeatureState state = m_States[Index(feature)];
            return state.requested && state.supported;
        }

        public void ThrowIfCannotProduce(EFrameFeature feature)
        {
            if (!ShouldRecord(feature))
            {
                throw new InvalidOperationException($"InfinityRP: required producer {feature} is not going to produce this frame.");
            }
        }

        // The pipeline's required producers, plus the camera's temporal resolve when it has one.
        internal void EnsureRequiredProducers(EFrameFeature[] requiredProducers, ETemporalResolve temporalResolve)
        {
            foreach (EFrameFeature feature in requiredProducers)
            {
                EnsureProduced(feature);
            }
            if (temporalResolve == ETemporalResolve.TAA)
            {
                EnsureProduced(EFrameFeature.TAA);
            }
            else if (temporalResolve == ETemporalResolve.SuperResolution)
            {
                EnsureProduced(EFrameFeature.SuperResolution);
            }
        }

        void EnsureProduced(EFrameFeature feature)
        {
            if (!IsProduced(feature))
            {
                throw new InvalidOperationException($"InfinityRP: required producer {feature} did not record this frame.");
            }
        }

        // The frame phase, before culling: what the camera asks for from its settings and volumes, then what can record
        // (FeatureRegistry: kernels, dependencies, camera policy, with the reason for anything that cannot).
        internal void ResolveFrame(in FrameFeatureInputs inputs)
        {
            Reset();
            foreach (EFrameFeature feature in inputs.requiredProducers)
            {
                Request(feature);
            }
            Request(EFrameFeature.Motion);
            Request(EFrameFeature.HiZ);
            Request(EFrameFeature.ColorPyramid);
            Request(EFrameFeature.PostProcess);
            ResolvedFrameSettings frameSettings = inputs.frameSettings;
            VolumeSnapshot volumes = inputs.volumes;
            if (inputs.decalCount > 0 && frameSettings.IsEnabled(EFrameSetting.Decals))
            {
                Request(EFrameFeature.DBuffer);
            }
            // Without a temporal resolve post processing reads the fogged scene color directly.
            if (inputs.temporalResolve == ETemporalResolve.SuperResolution)
            {
                Request(EFrameFeature.SuperResolution);
            }
            else if (inputs.temporalResolve == ETemporalResolve.TAA)
            {
                Request(EFrameFeature.TAA);
            }
            if (GraphicsUtility.VolumeComponentActive(volumes.subsurfaceScattering))
            {
                Request(EFrameFeature.SubsurfaceScattering);
            }
            // RTAO owns ambient occlusion when it can record; otherwise GTAO does, when requested.
            if (inputs.support.rayTracedOcclusionReady)
            {
                Request(EFrameFeature.RTAO);
            }
            else if (frameSettings.IsEnabled(EFrameSetting.AmbientOcclusion) && ScreenSpaceModeUtility.ShouldRequestGTAO(volumes.screenSpaceAmbientOcclusion))
            {
                Request(EFrameFeature.GTAO);
            }
            EScreenSpaceMode screenSpaceMode = ScreenSpaceModeUtility.Resolve(volumes.screenSpaceReflection, volumes.screenSpaceIndirectDiffuse);
            if (ScreenSpaceModeUtility.IncludesSSR(screenSpaceMode) && frameSettings.IsEnabled(EFrameSetting.ScreenSpaceReflection))
            {
                Request(EFrameFeature.SSR);
            }
            if (ScreenSpaceModeUtility.IncludesSSGI(screenSpaceMode) && frameSettings.IsEnabled(EFrameSetting.ScreenSpaceGlobalIllumination))
            {
                Request(EFrameFeature.SSGI);
            }
            if (frameSettings.IsEnabled(EFrameSetting.VolumetricFog) && GraphicsUtility.VolumeComponentActive(volumes.volumetricFog))
            {
                Request(EFrameFeature.VolumetricFog);
            }
            if (frameSettings.IsEnabled(EFrameSetting.VolumetricClouds) && GraphicsUtility.VolumeComponentActive(volumes.volumetricCloud))
            {
                Request(EFrameFeature.VolumetricCloud);
            }
            FeatureRegistry.Resolve(this, inputs.support);
        }

        // The content phase: what the visible lights add, resolved again with the frame phase's support.
        internal void ResolveContent(in ContentFeatureInputs inputs, in FeatureSupportContext support)
        {
            if (inputs.localLightList)
            {
                Request(EFrameFeature.LocalLightList);
            }
            if (inputs.contactShadowsEnabled && inputs.contactShadowVolume && inputs.contactShadowCasters)
            {
                Request(EFrameFeature.ContactShadow);
            }
            if (inputs.lightCookies)
            {
                Request(EFrameFeature.LightCookies);
            }
            if (inputs.reflectionProbes)
            {
                Request(EFrameFeature.ReflectionProbes);
            }
            if (inputs.shadowMoments)
            {
                Request(EFrameFeature.ShadowMoments);
            }
            FeatureRegistry.Resolve(this, support);
        }

        static int Index(EFrameFeature feature)
        {
            return (int)feature;
        }
    }
}
