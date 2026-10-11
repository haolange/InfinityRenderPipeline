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
        PreTonemapLuma
    }
}
