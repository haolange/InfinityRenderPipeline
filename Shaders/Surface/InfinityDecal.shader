Shader "InfinityPipeline/InfinityDecal"
{
	Properties
	{
		[NoScaleOffset]_MainTex ("Albedo", 2D) = "white" {}
		_BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
		_Roughness ("Roughness", Range(0, 1)) = 0.5
		_Metallic ("Metallic", Range(0, 1)) = 0
		_Specular ("Specular", Range(0, 1)) = 0.5
		_Occlusion ("Occlusion", Range(0, 1)) = 1
	}

	SubShader
	{
		Tags { "RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque" "Queue" = "Geometry+100" }

		Pass
		{
			Name "RasterDBufferPass"
			Tags { "LightMode" = "RasterDBufferPass" }
			ZTest Always ZWrite Off Cull Front
			Blend 0 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			Blend 1 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			Blend 2 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

			HLSLPROGRAM
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../Library/Common.hlsl"
			#include "../Library/GBufferPack.hlsl"
			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "../Library/DecalProjection.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Metallic;
				float _Specular;
				float _Occlusion;
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex;
			SamplerState sampler_MainTex;
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
				float3 worldPos = GetWorldSpacePos(ndcPos, Matrix_InvViewFlipYJitterProj);
				float3 objectPos = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
				clip(0.5 - abs(objectPos));

				float2 uv = DecalProjectedUV(objectPos);
				float3 decalNormalWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
				float4 albedo = _MainTex.Sample(sampler_MainTex, uv) * _BaseColor;
				albedo.rgb *= _Occlusion;
				albedo.a *= DecalAngleFade(worldPos, decalNormalWS, _WorldSpaceCameraPos);
				clip(albedo.a - 1e-4);

				DBufferA = float4(albedo.rgb, albedo.a);
				DBufferB = float4(decalNormalWS * 0.5 + 0.5, albedo.a);
				DBufferC = float4(_Roughness, _Metallic, _Specular, albedo.a);
			}
			ENDHLSL
		}

		// Direct GBuffer Decal path: hardware-blends Albedo RGB directly on GBufferA without touching packed Roughness
		// (A channel) or Octahedral Normal (GBufferB), avoiding a DBuffer MRT allocation when only color changes.
		Pass
		{
			Name "RasterDirectDecalPass"
			Tags { "LightMode" = "RasterDirectDecalPass" }
			ZTest Always ZWrite Off Cull Front
			ColorMask RGB
			Blend 0 SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../Library/Common.hlsl"
			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "../Library/DecalProjection.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Metallic;
				float _Specular;
				float _Occlusion;
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex;
			SamplerState sampler_MainTex;
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

			float4 frag(Varyings In) : SV_Target
			{
				float2 screenUV = In.positionCS.xy * rcp(_ScreenParams.xy);
				float sceneDepth = _DepthTexture.Load(int3(In.positionCS.xy, 0)).r;
				float3 ndcPos = GetNDCPos(screenUV, sceneDepth);
				float3 worldPos = GetWorldSpacePos(ndcPos, Matrix_InvViewFlipYJitterProj);
				float3 objectPos = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
				clip(0.5 - abs(objectPos));

				float2 uv = DecalProjectedUV(objectPos);
				float3 decalNormalWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
				float4 albedo = _MainTex.Sample(sampler_MainTex, uv) * _BaseColor;
				albedo.rgb *= _Occlusion;
				albedo.a *= DecalAngleFade(worldPos, decalNormalWS, _WorldSpaceCameraPos);
				clip(albedo.a - 1e-4);

				return float4(albedo.rgb, saturate(albedo.a));
			}
			ENDHLSL
		}
	}
}
