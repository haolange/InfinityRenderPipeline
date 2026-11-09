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

        // TAA is a required producer only for temporal cameras that do not use super resolution.
        public void EnsureRequiredProducers(bool requiresTemporalAntiAliasing)
        {
            EnsureProduced(EFrameFeature.Depth);
            EnsureProduced(EFrameFeature.GBuffer);
            EnsureProduced(EFrameFeature.DeferredShading);
            EnsureProduced(EFrameFeature.Display);
            if (requiresTemporalAntiAliasing)
            {
                EnsureProduced(EFrameFeature.TAA);
            }
        }

        void EnsureProduced(EFrameFeature feature)
        {
            if (!IsProduced(feature))
            {
                throw new InvalidOperationException($"InfinityRP: required producer {feature} did not record this frame.");
            }
        }

        static int Index(EFrameFeature feature)
        {
            return (int)feature;
        }
    }
}
