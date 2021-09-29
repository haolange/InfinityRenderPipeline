Shader "InfinityPipeline/Utility/DrawFullScreen"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "InfinityRenderPipeline" }
		
		Pass
		{
			Name"DefaultFullScreen"
			ZTest Always ZWrite Off  Blend Off Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			struct Attributes
			{
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.vertex = float4(v.vertex.x, -v.vertex.y, 0, 1);
				o.uv = (v.vertex.xy + 1) * 0.5;
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				float2 UV = i.uv.xy;
				//return GBufferB / 127.0 - 1;
				return _MainTex.SampleLevel(sampler_MainTex, UV, 0);
				//return SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_MainTex, UV, 0);
			}
			ENDHLSL
		}

		Pass
		{
			Name"SmartFullScreen"
			ZTest Always ZWrite Off  Blend Off Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			//#define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod) SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, coord2, 0, lod)
			#include "../Include/Common.hlsl"
			#include "../Include/GBufferPack.hlsl"
			#include "../Include/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			float4 _ScaleBais;	
			Texture2D _MainTex; SamplerState sampler_MainTex;
			//Texture2D<uint4> _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.vertex = float4(v.vertex.x, -v.vertex.y, 0, 1);
				//o.uv = (v.vertex.xy + 1) * 0.5;
				//o.uv = ((v.vertex.xy + 1) * 0.5) * float2(1, -1) + float2(0, 1);
				o.uv = ( (v.vertex.xy + 1) * 0.5) * _ScaleBais.xy + _ScaleBais.zw;
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				float2 UV = i.uv.xy;
				return _MainTex.SampleLevel(Global_bilinear_clamp_sampler, UV - TAAJitter.zw, 0);

				/*FGBufferData GBufferData;
				DecodeGBuffer(1, _MainTex.SampleLevel(Global_bilinear_clamp_sampler, UV - TAAJitter.zw, 0), 1, GBufferData);
				return float4(GBufferData.WorldNormal, 1);*/
			}
			ENDHLSL
		}
    }
	Fallback Off
}
