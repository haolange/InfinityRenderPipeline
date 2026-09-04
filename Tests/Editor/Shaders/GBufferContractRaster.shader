Shader "Hidden/InfinityPipeline/GBufferContractRaster"
{
    Properties
    {
        _FixtureId ("FixtureId", Int) = 0
        _Resolution ("Resolution", Vector) = (32, 32, 0, 0)
        _FixtureNormal ("FixtureNormal", Vector) = (0, 0, 1, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "InfinityRenderPipeline" }
        Pass
        {
            Name "GBufferContractEncode"
            ZTest Always ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "../../../Shaders/ShaderLibrary/GBufferPack.hlsl"

            int _FixtureId;
            float4 _Resolution;
            float4 _FixtureNormal;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 FixtureAlbedo(int fixture, uint2 pixel, uint2 resolution)
            {
                if (fixture == 0)
                {
                    return 0.18;
                }
                if (fixture == 1)
                {
                    return float3(1.0, 0.0, 0.0);
                }
                if (fixture == 2)
                {
                    return pixel.x < (resolution.x / 2) ? float3(1.0, 0.0, 0.0) : float3(1.0, 0.0, 1.0);
                }
                return float3(1.0, 0.0, 0.0);
            }

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }

            void Frag(Varyings input, out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1, out float4 GBufferC : SV_Target2)
            {
                uint2 resolution = (uint2)_Resolution.xy;
                uint2 pixel = GBufferPixelCoord(input.positionCS.xy);

                FGBufferData data;
                data.Albedo = FixtureAlbedo(_FixtureId, pixel, resolution);
                data.Normal = normalize(_FixtureNormal.xyz);
                data.Specular = 0.5;
                data.Roughness = 0.5;
                data.Reflactance = 0.0;
                data.ShadingModel = GBUFFER_SHADING_MODEL_DEFAULT_LIT;
                data.Flags = 0;
                data.SSSProfileIndex = 0;
                data.Thickness = 0.0;
                EncodeGBuffer(data, input.positionCS.xy, GBufferA, GBufferB, GBufferC);
            }
            ENDHLSL
        }
    }
}
