namespace InfinityTech.Rendering.Pipeline
{
    // Values are mirrored as DEBUGVIEW_* defines in Shaders/ShaderLibrary/DebugViewModes.hlsl.
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
        LightCookieAtlas
    }
}
