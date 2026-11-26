using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    // The graph key of each scene-color stage, derived once per camera from its resolved features (R18). A writer
    // registers only its own fixed output; a reader asks for a stage, never for whichever texture was registered last.
    // Each condition is the one its producer records under.
    internal readonly struct SceneColorKeys
    {
        // The geometry draw's Scene Color. Deferred shading, forward shading, subsurface scattering and the sky write
        // into it in place.
        internal readonly RGResourceKey opaque;
        // The fog composite when volumetric fog or clouds record, else the opaque color. Translucent passes draw into it.
        internal readonly RGResourceKey fogged;
        // The temporal resolve's output, else the fogged color. Post processing reads it.
        internal readonly RGResourceKey resolved;
        // What the camera presents: post processing's output, or the debug view's when one is active (Editor and
        // Development builds only).
        internal readonly RGResourceKey display;

        internal SceneColorKeys(FrameFeatureSet features, ETemporalResolve temporalResolve
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , bool debugView
#endif
            )
        {
            opaque = InfinityShaderIDs.LightingBuffer;
            fogged = features.ShouldRecord(EFrameFeature.VolumetricFog) || features.ShouldRecord(EFrameFeature.VolumetricCloud)
                ? InfinityShaderIDs.FoggedSceneColorBuffer : opaque;
            resolved = temporalResolve == ETemporalResolve.SuperResolution ? InfinityShaderIDs.SuperResolutionBuffer
                : temporalResolve == ETemporalResolve.TAA ? InfinityShaderIDs.AntiAliasingBuffer : fogged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            display = debugView ? InfinityShaderIDs.DebugViewBuffer : InfinityShaderIDs.PostProcessBuffer;
#else
            display = InfinityShaderIDs.PostProcessBuffer;
#endif
        }
    }
}
