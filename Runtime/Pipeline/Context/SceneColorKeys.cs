using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    // The graph key of each scene-color stage, derived once per camera from its resolved features (R18). A writer
    // registers only its own fixed output; a reader asks for a stage, never for whichever texture was registered last.
    // Each condition is the one its producer records under.
    internal readonly struct SceneColorKeys
    {
        internal readonly RGResourceKey opaque;
        internal readonly RGResourceKey fogged;
        internal readonly RGResourceKey resolved;
        internal readonly RGResourceKey display;

        internal SceneColorKeys(FrameFeatureSet features, bool temporalActive
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , bool debugView
#endif
            )
        {
            opaque = InfinityShaderIDs.LightingBuffer;
            fogged = features.ShouldRecord(EFrameFeature.VolumetricFog) || features.ShouldRecord(EFrameFeature.VolumetricCloud)
                ? InfinityShaderIDs.FoggedSceneColorBuffer : opaque;
            resolved = temporalActive ? InfinityShaderIDs.TemporalOutputBuffer : fogged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            display = debugView ? InfinityShaderIDs.DebugViewBuffer : InfinityShaderIDs.PostProcessBuffer;
#else
            display = InfinityShaderIDs.PostProcessBuffer;
#endif
        }
    }
}
