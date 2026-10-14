#ifndef INFINITY_SG_CORE
#define INFINITY_SG_CORE
// First pre-graph include of every Infinity Shader Graph pass (Editor/ShaderGraph/InfinityPasses.cs): pass ids,
// pipeline globals, and the CoreRP / Shader Graph library functions the generated code calls.

// SHADERPASS values (the generator defines SHADERPASS to the pass reference name).
#define SHADERPASS_DEPTH 0
#define SHADERPASS_SHADOWS 1
#define SHADERPASS_GBUFFER 2
#define SHADERPASS_FORWARD 3
#define SHADERPASS_MOTION 4
#define SHADERPASS_TRANSLUCENT_DEPTH 5
#define SHADERPASS_TRANSLUCENT_T0 6
#define SHADERPASS_TRANSLUCENT_T1 7
#define SHADERPASS_TRANSLUCENT_T2 8
#define SHADERPASS_META 9
#define SHADERPASS_DBUFFER 10

#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/Common.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// Set by ScriptableRenderContext.SetupCameraProperties.
float4 unity_OrthoParams;

#include "Packages/com.unity.shadergraph/ShaderGraphLibrary/ShaderVariablesFunctions.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/MotionVectors.hlsl"

// Infinity renders without dynamic resolution scaling of the screen parameters.
float4 GetScaledScreenParams()
{
    return _ScreenParams;
}
#endif
