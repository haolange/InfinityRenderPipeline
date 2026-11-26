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
            #include "../../../Shaders/Library/GBufferPack.hlsl"
            #include "../../../Shaders/Library/Sideband.hlsl"
            #include "../../../Shaders/Library/FullScreenTriangle.hlsl"

            int _FixtureId;
            float4 _Resolution;
            float4 _FixtureNormal;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 FixtureAlbedo(int fixture, uint2 pixel, uint2 resolution)
            {
                if (fixture == 6)
                {
                    return float3(0.4, 0.1, 0.03);
                }
                if (fixture == 4)
                {
                    return float3(0.03, 0.02, 0.01);
                }
                if (fixture == 5)
                {
                    return float3(0.8, 0.05, 0.02);
                }
                if (fixture == 0)
                {
                    return 0.18;
                }
                if (fixture == 1)
                {
                    return float3(1.0, 0.0, 0.0);
                }
                return pixel.x < (resolution.x / 2) ? float3(1.0, 0.0, 0.0) : float3(1.0, 0.0, 1.0);
            }

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = FullScreenTrianglePosition(vertexID);
                return output;
            }

            // Mirrors GBufferContractTests.ExpectedSideband: every light channel mask 0..15 and profile 0..15 along x and y.
            SidebandData FixtureSideband(uint2 pixel)
            {
                SidebandData sideband;
                sideband.DiffuseRatio = (pixel.x + 0.5) / 32.0;
                sideband.BakedVisibility = float2((pixel.y + 0.5) / 32.0, 1.0 - (pixel.x + 0.5) / 32.0);
                sideband.LightChannels = pixel.x & 15u;
                sideband.Profile = pixel.y & 15u;
                return sideband;
            }

            void Frag(Varyings input, out float4 gBufferA : SV_Target0, out float4 gBufferB : SV_Target1, out float4 sideband : SV_Target2)
            {
                uint2 resolution = (uint2)_Resolution.xy;
                uint2 pixel = (uint2)input.positionCS.xy;
                if (_FixtureId == 4 && pixel.x > pixel.y)
                {
                    discard;
                }
                if (_FixtureId == 5 && pixel.x != resolution.x / 2)
                {
                    discard;
                }
                SurfaceData data;
                data.Albedo = FixtureAlbedo(_FixtureId, pixel, resolution);
                data.Normal = normalize(_FixtureNormal.xyz);
                data.Specular = 0.5;
                data.Roughness = 0.5;
                data.Metallic = 0.25;
                EncodeSurfaceGBuffer(data, gBufferA, gBufferB);
                sideband = PackSideband(FixtureSideband(pixel));
            }
            ENDHLSL
        }
    }
}
