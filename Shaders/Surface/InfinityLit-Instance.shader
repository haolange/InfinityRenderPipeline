Shader "InfinityPipeline/InfinityLit-Instance"
{
	Properties 
	{
        [Header (Microface)]
        [Toggle (_UseAlbedoTex)]UseBaseColorTex ("UseBaseColorTex", Range(0, 1)) = 0
        [NoScaleOffset]_MainTex ("BaseColorTexture", 2D) = "white" {}
        _BaseColorTile ("BaseColorTile", Range(0, 1024)) = 1
        _BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
        _SpecularLevel ("SpecularLevel", Range(0, 1)) = 0.5
        _Reflectance ("Reflectance", Range(0, 1)) = 0
        _Roughness ("Roughness", Range(0, 1)) = 0


        [Header (Normal)]
        [NoScaleOffset]_NomralTexture ("NomralTexture", 2D) = "bump" {}
        _NormalTile ("NormalTile", Range(0, 100)) = 1


        [Header (Iridescence)]
        [Toggle (_Iridescence)] Iridescence ("Iridescence", Range(0, 1)) = 0
        _Iridescence_Distance ("Iridescence_Distance", Range(0, 1)) = 1

		[Header(PixelDepthOffset)]
        _PixelDepthOffsetVaule ("PixelDepthOffsetVaule", Range(-1, 1)) = 0

		[Header(RenderState)]
		//[HideInInspector] 
		_ZTest("ZTest", Int) = 4
		_ZWrite("ZWrite", Int) = 1
	}
	
	SubShader
	{
		Tags{"RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque"}

		//DepthBuffer
		Pass
		{
			Name "OpaqueDepthPass"
			Tags { "LightMode" = "OpaqueDepth" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0 

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../Private/GPUScene.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 vertex_WS : TEXCOORD2;
				float4 vertex_CS : SV_POSITION;
			};

			Varyings vert(Attributes In, uint InstanceId : SV_InstanceID)
			{
				Varyings Out;
				Out.InstanceId = _Indexs[InstanceId + _Offset];
				FMeshbatch Meshbatch = _GPUScene[Out.InstanceId];

				Out.uv0 = In.uv0;
				Out.vertex_WS = mul(Meshbatch.Matrix_Model, float4(In.vertex.xyz, 1.0));
				Out.vertex_CS = mul(Matrix_ViewJitterProj, Out.vertex_WS);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		//Gbuffer
		Pass
		{
			Name "OpaqueGBufferPass"
			Tags { "LightMode" = "OpaqueGBuffer" }
			ZTest[_ZTest] ZWrite[_ZWrite] Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma enable_d3d11_debug_symbols


			#include "../Private/Common.hlsl"
			#include "../Private/GPUScene.hlsl"
			#include "../Private/PackData.hlsl"
			#include "../Private/Lightmap.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				float _SpecularLevel;
				float _BaseColorTile;
				float4 _BaseColor;
			CBUFFER_END
			
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 vertex_WS : TEXCOORD2;
				float4 vertex_CS : SV_POSITION;
			};
			
			Varyings vert (Attributes In, uint InstanceId : SV_InstanceID)
			{
				Varyings Out;
				Out.InstanceId = _Indexs[InstanceId + _Offset];
				FMeshbatch Meshbatch = _GPUScene[Out.InstanceId];

				Out.uv0 = In.uv0;
				Out.normal = In.normal;
				Out.vertex_WS = mul(Meshbatch.Matrix_Model, float4(In.vertex.xyz, 1.0));
				Out.vertex_CS = mul(Matrix_ViewJitterProj, Out.vertex_WS);

				return Out;
			}
			
			void frag (Varyings In, out float4 GBufferA : SV_Target0, out uint4 GBufferB : SV_Target1)
			{
				float3 BaseColor = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile).rgb * _BaseColor.rgb;

				ThinGBufferData GBufferData;
				GBufferData.WorldNormal = normalize(In.normal);
				GBufferData.BaseColor = BaseColor;
				GBufferData.Roughness = BaseColor.r;
				GBufferData.Specular = _SpecularLevel;
				GBufferData.Reflactance = BaseColor.b;
				EncodeGBuffer(GBufferData, GBufferA, GBufferB);
			}
			ENDHLSL
		}

		//ForwardPlus
		Pass
		{
			Name "ForwardPass"
			Tags { "LightMode" = "ForwardPlus" }
			ZTest Equal ZWrite Off Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma enable_d3d11_debug_symbols


			#include "../Private/Common.hlsl"
			#include "../Private/GPUScene.hlsl"
			#include "../Private/PackData.hlsl"
			#include "../Private/Lightmap.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				float _SpecularLevel;
				float _BaseColorTile;
				float4 _BaseColor;
			CBUFFER_END

			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 vertex_WS : TEXCOORD2;
				float4 vertex_CS : SV_POSITION;
			};

			Varyings vert(Attributes In, uint InstanceId : SV_InstanceID)
			{
				Varyings Out;
				Out.InstanceId = _Indexs[InstanceId + _Offset];
				FMeshbatch Meshbatch = _GPUScene[Out.InstanceId];

				Out.uv0 = In.uv0;
				Out.normal = In.normal;
				Out.vertex_WS = mul(Meshbatch.Matrix_Model, float4(In.vertex.xyz, 1.0));
				Out.vertex_CS = mul(Matrix_ViewJitterProj, Out.vertex_WS);

				return Out;
			}

			void frag(Varyings In, out float3 DiffuseBuffer : SV_Target0, out float3 SpecularBuffer : SV_Target1)
			{
				float3 BaseColor = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile).rgb * _BaseColor.rgb;

				DiffuseBuffer = BaseColor;
				SpecularBuffer = 0.5f;
			}
			ENDHLSL
		}
	}
}
