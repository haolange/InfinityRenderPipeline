Shader "InfinityPipeline/InfinityLit-Instance"
{
	Properties 
	{
        [Header (Color)]
        [Toggle (_UseAlbedoTex)]UseBaseColorTex ("UseBaseColorTex", Range(0, 1)) = 0
        [NoScaleOffset]_MainTex ("BaseColorTexture", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
        _BaseColorTile ("BaseColorTile", Range(0, 1024)) = 1

		[Header (Microface)]
        _Roughness ("Roughness", Range(0, 1)) = 0
        _Reflectance ("Reflectance", Range(0, 1)) = 0
        _SpecularLevel ("SpecularLevel", Range(0, 1)) = 0.5

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

		//Depth Pass
		Pass
		{
			Name "DepthPass"
			Tags { "LightMode" = "DepthPass" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0 

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma enable_d3d11_debug_symbols

			#include "../Include/GPUScene.hlsl"
			#include "../Include/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 vertex_WS : TEXCOORD2;
				float4 vertex_CS : SV_POSITION;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				Out.PrimitiveId  = meshBatchIndexs[In.InstanceId + meshBatchOffset];
				FMeshBatch meshBatch = meshBatchBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				Out.vertex_WS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.vertex_CS = mul(Matrix_ViewJitterProj, Out.vertex_WS);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		//Gbuffer Pass
		Pass
		{
			Name "GBufferPass"
			Tags { "LightMode" = "GBufferPass" }
			ZTest[_ZTest] ZWrite[_ZWrite] Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma enable_d3d11_debug_symbols

			#include "../Include/Common.hlsl"
			#include "../Include/GPUScene.hlsl"
			#include "../Include/Lightmap.hlsl"
			#include "../Include/GBufferPack.hlsl"
			#include "../Include/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _SpecularLevel;
				float _BaseColorTile;
				float4 _BaseColor;
			CBUFFER_END
			
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 vertex_WS : TEXCOORD2;
				float4 vertex_CS : SV_POSITION;
			};
			
			Varyings vert (Attributes In)
			{
				Varyings Out;
				Out.PrimitiveId  = meshBatchIndexs[In.InstanceId + meshBatchOffset];
				FMeshBatch meshBatch = meshBatchBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				//Out.normal = In.normal;
				Out.normal = normalize(mul((float3x3)meshBatch.matrix_LocalToWorld, In.normal));
				//Out.normal = normalize(mul(Out.normal, (float3x3)meshBatch.matrix_LocalToWorld));
				Out.vertex_WS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.vertex_CS = mul(Matrix_ViewJitterProj, Out.vertex_WS);
				return Out;
			}
			
			void frag (Varyings In, out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1)
			{
				float3 Albedo = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile).rgb * _BaseColor.rgb;

				FGBufferData GBufferData;
				GBufferData.Albedo = Albedo;
				GBufferData.Roughness = Albedo.r;
				GBufferData.Specular = _SpecularLevel * Albedo.g;
				GBufferData.Reflactance = Albedo.b;
				GBufferData.Normal = normalize(In.normal);
				EncodeGBuffer(GBufferData, In.vertex_CS.xy, GBufferA, GBufferB);
			}
			ENDHLSL
		}

		//Forward Pass
		Pass
		{
			Name "ForwardPass"
			Tags { "LightMode" = "ForwardPass" }
			ZTest Equal ZWrite Off Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma enable_d3d11_debug_symbols

			#include "../Include/Common.hlsl"
			#include "../Include/GPUScene.hlsl"
			#include "../Include/Lighting.hlsl"
			#include "../Include/GBufferPack.hlsl"
			#include "../Include/ShadingModel.hlsl"
			#include "../Include/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _SpecularLevel;
				float _BaseColorTile;
				float4 _BaseColor;
			CBUFFER_END

			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
			};

			struct Varyings
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float4 vertexWS : TEXCOORD2;
				float4 vertexCS : SV_POSITION;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				Out.InstanceId  = meshBatchIndexs[In.InstanceId + meshBatchOffset];
				FMeshBatch meshBatch = meshBatchBuffer[Out.InstanceId];

				Out.uv0 = In.uv0;
				Out.normalWS = normalize(mul((float3x3)meshBatch.matrix_LocalToWorld, In.normalOS));
				Out.vertexWS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(Matrix_ViewJitterProj, Out.vertexWS);
				return Out;
			}

			void frag(Varyings In, out float4 lightingBuffer : SV_Target0)
			{
				float4 albedoMap = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile);

				float3 surfaceAlbedo = albedoMap.rgb * _BaseColor.rgb;
				float surfaceSpecular = _SpecularLevel;
				float surfaceReflctance = _Reflectance;
				float surfaceRoughness = _Roughness;
				MicrofaceContext microfaceContext = InitMicrofaceContext(surfaceSpecular, surfaceRoughness, surfaceReflctance, surfaceAlbedo);

				float3 normalWS = normalize(In.normalWS.xyz);
				float3 positionWS = In.vertexWS.xyz;
				float3 cameraDirWS = normalize(_WorldSpaceCameraPos - positionWS);

				lightingBuffer = 0;
				for(int i = 0; i < g_DirectionalLightCount; ++i)
				{
					float3 lightColor = g_DirectionalLightBuffer[i].color.rgb;
					float3 lightDirWS = g_DirectionalLightBuffer[i].directional.xyz;
					float3 halfDirWS = normalize(lightDirWS + cameraDirWS);

					BSDFContext bsdfContext = InitBXDFContext(normalWS, cameraDirWS, lightDirWS, halfDirWS);
					lightingBuffer.rgb += DefultLit(bsdfContext, microfaceContext);
					lightingBuffer.rgb *= lightColor * bsdfContext.NoL;
				}

				//lightingBuffer += float4(albedoMap * indirectLight, 1);
			}
			ENDHLSL
		}
	}
}
