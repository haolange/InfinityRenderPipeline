Shader "InfinityPipeline/InfinityDecal"
{
	Properties
	{
		[NoScaleOffset]_MainTex ("Albedo", 2D) = "white" {}
		_BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
		_Roughness ("Roughness", Range(0, 1)) = 0.5
		_Metallic ("Metallic", Range(0, 1)) = 0
		_Occlusion ("Occlusion", Range(0, 1)) = 1
	}

	SubShader
	{
		Tags { "RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque" "Queue" = "Geometry+100" }

		Pass
		{
			Name "DBufferPass"
			Tags { "LightMode" = "DBufferPass" }
			ZTest Always ZWrite Off Cull Front
			Blend 0 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			Blend 1 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			Blend 2 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Metallic;
				float _Occlusion;
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;
			Texture2D _DepthTexture;

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

			void frag(Varyings In, out float4 DBufferA : SV_Target0, out float4 DBufferB : SV_Target1, out float4 DBufferC : SV_Target2)
			{
				float2 screenUV = In.positionCS.xy * rcp(_ScreenParams.xy);
				float sceneDepth = _DepthTexture.Load(int3(In.positionCS.xy, 0)).r;
				float3 ndcPos = GetNDCPos(screenUV, sceneDepth);
				float3 worldPos = GetWorldSpacePos(ndcPos, Matrix_InvViewJitterProj);
				float3 objectPos = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
				clip(0.5 - abs(objectPos));

				float2 uv = objectPos.xz + 0.5;
				float4 albedo = _MainTex.Sample(sampler_MainTex, uv) * _BaseColor;
				clip(albedo.a - 1e-4);

				float3 decalNormalWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));

				DBufferA = float4(albedo.rgb, albedo.a);
				DBufferB = float4(decalNormalWS * 0.5 + 0.5, albedo.a);
				DBufferC = float4(_Roughness, _Metallic, _Occlusion, albedo.a);
			}
			ENDHLSL
		}
	}
}
