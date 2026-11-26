Shader "InfinityPipeline/InfinityRasterReflection"
{
    Properties
    {
        _ReflectionCube ("Reflection Cubemap", Cube) = "" {}
        _Intensity ("Intensity", Range(0, 8)) = 1.0
        _ReflectionBlendDistance ("Edge Blend Distance", Range(0.01, 0.5)) = 0.25
    }

    SubShader
    {
        Tags { "RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque" "Queue" = "Geometry+150" }

        Pass
        {
            Name "RasterReflectionPass"
            Tags { "LightMode" = "RasterReflectionPass" }
            ZTest Always
            ZWrite Off
            Cull Front
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "../Library/Common.hlsl"
            #include "../Library/GBufferPack.hlsl"
            #include "../Library/ShaderVariables.hlsl"
            #include "../Library/Spatial/SpatialGeometry.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _ReflectionBlendDistance;
            CBUFFER_END

            TEXTURECUBE(_ReflectionCube);
            SAMPLER(sampler_ReflectionCube);
            Texture2D<float> SRV_DepthTexture;
            Texture2D SRV_GBufferTextureB;
            float4 _RasterReflectionResolution;

            struct Attributes
            {
                float3 vertex : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes In)
            {
                Varyings Out;
                float4 worldPos = mul(UNITY_MATRIX_M, float4(In.vertex, 1.0));
                Out.positionCS = mul(UNITY_MATRIX_VP, worldPos);
                return Out;
            }

            float4 frag(Varyings In) : SV_Target
            {
                uint2 pixelCoord = (uint2)In.positionCS.xy;
                float2 screenUV = In.positionCS.xy * _RasterReflectionResolution.zw;
                float rawDepth = SRV_DepthTexture.Load(int3(pixelCoord, 0));
                float3 worldPos = GetWorldSpacePos(GetNDCPos(screenUV, rawDepth), Matrix_InvViewFlipYJitterProj);
                float3 localPos = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
                clip(0.5 - abs(localPos));

                float coverage = SpatialBoxInfluenceWeight(localPos, float3(0.5, 0.5, 0.5), _ReflectionBlendDistance);
                clip(coverage - 1e-4);

                float3 viewDir = normalize(ViewPositionWS() - worldPos);
                float3 normalWS = DecodeGBufferNormal(SRV_GBufferTextureB.Load(int3(pixelCoord, 0)));
                float3 reflectionWS = reflect(-viewDir, normalWS);
                float3 sampleRadiance = SAMPLE_TEXTURECUBE(_ReflectionCube, sampler_ReflectionCube, reflectionWS).rgb * _Intensity;
                return float4(sampleRadiance * coverage, coverage);
            }
            ENDHLSL
        }
    }
}
