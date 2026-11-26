#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace InfinityTech.Rendering.Pipeline
{
    // Values are mirrored as DEBUGVIEW_* defines in Shaders/Library/DebugViewModes.hlsl.
    // DebugViewModeTests keeps the two in sync; append new views at the end.
    public enum EDebugView
    {
        None = 0,
        Albedo,
        Normal,
        Roughness,
        AO,
        SSR,
        SSGI,
        MotionMagnitude,
        TAAConfidence,
        PreTonemapLuma,
        // Stochastic SSR diagnostics (written by the SSR pass into SSRDebugBuffer).
        SSRTileClass,
        SSRRayHit,
        SSRConfidence,
        SSRHistoryAge,
        // Shadow atlases, letterboxed to a square (near = bright).
        CascadeShadowAtlas,
        LocalShadowAtlas,
        CachedShadowAtlas,
        // First moment of the cascade moment atlas, or of the local one when the sun has none.
        ShadowMoments,
        // Light cookie / IES atlas (mip 0, stretched).
        LightCookieAtlas,
        // Material: metallic and specular level (GBuffer), the stencil admission bits, the sideband's light channels.
        Metallic,
        Specular,
        SurfaceStencil,
        RenderingLayers,
        // Lighting: clustered-list heat maps (local lights / reflection probes a pixel's loop visits).
        LightComplexity,
        ReflectionProbeComplexity,
        // Sideband: diffuse share of Scene Color (Lighting), baked visibility of the two mixed lights (Lighting), subsurface
        // profile (Material).
        DiffuseRatio,
        MixedLightVisibility,
        SubsurfaceProfile,
        // Lighting: the six faces of one reflection probe in the probe atlas (InfinityDebugDisplaySettingsLighting).
        ReflectionProbeAtlas,
        // Lighting: screen space trace shadow of the sun.
        ContactShadows
    }
}
#endif
