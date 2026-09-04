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
        OpaqueLightingPyramid
    }

    public class FrameFeatureSet
    {
        struct FeatureState
        {
            public bool requested;
            public bool supported;
            public bool produced;
        }

        readonly FeatureState[] m_States = new FeatureState[FeatureCount];

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

        public void EnsureRequiredProducers(bool superResolutionEnabled)
        {
            EnsureProduced(EFrameFeature.Depth);
            EnsureProduced(EFrameFeature.GBuffer);
            EnsureProduced(EFrameFeature.DeferredShading);
            EnsureProduced(EFrameFeature.Display);
            if (!superResolutionEnabled)
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
