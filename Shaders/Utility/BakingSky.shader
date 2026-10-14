// Skybox the lightmapper reads as environment lighting: the pipeline's atmosphere cubemap saved as a 6-slice
// Texture2DArray (InfinityBakingSky editor tool). Faces use the pipeline's own layout (Compute_AtmosphericLUT
// CubemapFaceDirection / AtmosphereCommon CubemapDirectionToFaceUv), so no cubemap re-orientation is involved.
// Untagged SubShader: the lightmapper renders it outside the pipeline.
Shader "Hidden/InfinityPipeline/BakingSky"
{
    Properties
    {
        [NoScaleOffset] _BakingSky ("Atmosphere Faces", 2DArray) = "" {}
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_BakingSky);
            float _Exposure;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // Same as AtmosphereCommon.hlsl CubemapDirectionToFaceUv.
            void DirectionToFaceUv(float3 dir, out float face, out float2 uv)
            {
                float3 a = abs(dir);
                float2 st;
                if (a.x >= a.y && a.x >= a.z)
                {
                    face = dir.x > 0.0 ? 0 : 1;
                    st = (dir.x > 0.0 ? float2(-dir.z, -dir.y) : float2(dir.z, -dir.y)) / max(a.x, 1e-6);
                }
                else if (a.y >= a.z)
                {
                    face = dir.y > 0.0 ? 2 : 3;
                    st = (dir.y > 0.0 ? float2(dir.x, dir.z) : float2(dir.x, -dir.z)) / max(a.y, 1e-6);
                }
                else
                {
                    face = dir.z > 0.0 ? 4 : 5;
                    st = (dir.z > 0.0 ? float2(dir.x, -dir.y) : float2(-dir.x, -dir.y)) / max(a.z, 1e-6);
                }
                uv = st * 0.5 + 0.5;
            }

            float4 frag(v2f i) : SV_Target
            {
                float face;
                float2 uv;
                DirectionToFaceUv(normalize(i.dir), face, uv);
                return float4(UNITY_SAMPLE_TEX2DARRAY_LOD(_BakingSky, float3(uv, face), 0).rgb * _Exposure, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
