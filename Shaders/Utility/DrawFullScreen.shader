Shader "InfinityPipeline/Utility/DrawFullScreen"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "InfinityRenderPipeline" }
		
		Pass
		{
			Name"DefaultFullScreen"
			ZTest Always ZWrite Off Blend Off Cull Off

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

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
			ZTest Always ZWrite Off Blend Off Cull Off

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			//#define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod) SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, coord2, 0, lod)
			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
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
				o.uv = ( (v.vertex.xy + 1) * 0.5) * _ScaleBais.xy + _ScaleBais.zw;
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				float2 uv = i.uv.xy;
				return _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0);

				/*FGBufferData GBufferData;
				FReconstructInput ReconstructInput;
				ReconstructInput.PixelCoord = uv * _ScreenParams.xy;
				ReconstructInput.CoCgR = _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0, int2(1, 0)).rg;
				ReconstructInput.CoCgL = _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0, int2(-1, 0)).rg;
				ReconstructInput.CoCgT = _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0, int2(0, 1)).rg;
				ReconstructInput.CoCgB = _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0, int2(0, -1)).rg;
				DecodeGBuffer(ReconstructInput, _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0), 1, GBufferData);
				return float4(GBufferData.BaseColor, 1);*/
			}
			ENDHLSL
		}

		Pass
		{
			Name"CameraMotion"
			ZTest Always ZWrite Off Blend Off Cull Off
			Stencil 
			{
				Ref 5
				comp NotEqual
				pass keep
			}

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			Texture2D _MainTex;

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
				float2 uv = i.uv.xy;
				float sceneDepth = _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0).r;

				float4 worldPos = mul(Matrix_InvViewFlipYProj, float4(uv * 2 - 1, sceneDepth, 1));
				worldPos.xyz /= worldPos.w;

				float4 lastClip = mul(Matrix_LastViewFlipYProj, float4(worldPos.xyz, 1));
				float2 lastUV = (lastClip.xy / lastClip.w) * 0.5 + 0.5;

#if UNITY_UV_STARTS_AT_TOP
				//uv.y = 1.0 - uv.y;
				//lastUV.y = 1.0 - lastUV.y;
#endif
				return float4(uv - lastUV, 0, 1);		
			}
			ENDHLSL
		}
    }
	Fallback Off
}
