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
			//#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
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
			//#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/Lightmap.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;
			Texture2D _NomralTexture; SamplerState sampler_NomralTexture;

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				float4 tangentOS : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				uint PrimitiveId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
				float4 vertexWS : TEXCOORD5;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			Varyings vert (Attributes In)
			{
				Varyings Out = (Varyings)0;
				Out.PrimitiveId  = meshBatchIndexs[In.InstanceId + meshBatchOffset];
				FMeshBatch meshBatch = meshBatchBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				Out.vertexWS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(Matrix_ViewJitterProj, Out.vertexWS);
				//Out.normal = normalize(mul(Out.normal, (float3x3)meshBatch.matrix_LocalToWorld));
				Out.normalWS = normalize(mul((float3x3)meshBatch.matrix_LocalToWorld, In.normalOS));
				Out.tangentWS = normalize(mul(meshBatch.matrix_LocalToWorld, float4(In.tangentOS.xyz, 0)).xyz);
				Out.bitangentWS = normalize(cross(Out.normalWS, Out.tangentWS) * In.tangentOS.w);
				return Out;
			}
			
			void frag (Varyings In, out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1)
			{
				float4 albedoMap = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile);
				float3 normalMap = UnpackNormal(_NomralTexture.Sample(sampler_NomralTexture, In.uv0 * _NormalTile));

				float3 vnormalWS = normalize(In.normalWS.xyz);
				float3 positionWS = In.vertexWS.xyz;
				float3 cameraDirWS = normalize(_WorldSpaceCameraPos - positionWS);
				float3x3 tangentMatrix = float3x3(In.tangentWS, In.bitangentWS, vnormalWS);
				float3 pnormalWS = normalize(mul(normalMap, tangentMatrix)); 

				float3 surfaceAlbedo = albedoMap.rgb * _BaseColor.rgb;
				float surfaceSpecular = _SpecularLevel;
				float surfaceReflctance = _Reflectance;
				float surfaceRoughness = _Roughness;

				FGBufferData GBufferData;
				GBufferData.Normal = pnormalWS;
				GBufferData.Albedo = surfaceAlbedo;
				GBufferData.Specular = surfaceSpecular;
				GBufferData.Roughness = surfaceRoughness;
				GBufferData.Reflactance = surfaceReflctance;
				EncodeGBuffer(GBufferData, In.vertexCS.xy, GBufferA, GBufferB);
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
			//#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/Lighting.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShadingModel.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END

			Texture2D _MainTex; SamplerState sampler_MainTex;
			Texture2D _NomralTexture; SamplerState sampler_NomralTexture;

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				float4 tangentOS : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				uint PrimitiveId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
				float4 vertexWS : TEXCOORD5;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			Varyings vert (Attributes In)
			{
				Varyings Out = (Varyings)0;
				Out.PrimitiveId  = meshBatchIndexs[In.InstanceId + meshBatchOffset];
				FMeshBatch meshBatch = meshBatchBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				Out.vertexWS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(Matrix_ViewJitterProj, Out.vertexWS);
				//Out.normal = normalize(mul(Out.normal, (float3x3)meshBatch.matrix_LocalToWorld));
				Out.normalWS = normalize(mul((float3x3)meshBatch.matrix_LocalToWorld, In.normalOS));
				Out.tangentWS = normalize(mul(meshBatch.matrix_LocalToWorld, float4(In.tangentOS.xyz, 0)).xyz);
				Out.bitangentWS = normalize(cross(Out.normalWS, Out.tangentWS) * In.tangentOS.w);
				return Out;
			}

			void frag(Varyings In, out float4 lightingBuffer : SV_Target0)
			{
				float4 albedoMap = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile);
				float3 normalMap = UnpackNormal(_NomralTexture.Sample(sampler_NomralTexture, In.uv0 * _NormalTile));

				float3 vnormalWS = normalize(In.normalWS.xyz);
				float3 positionWS = In.vertexWS.xyz;
				float3 cameraDirWS = normalize(_WorldSpaceCameraPos - positionWS);
				float3x3 tangentMatrix = float3x3(In.tangentWS, In.bitangentWS, vnormalWS);
				float3 pnormalWS = normalize(mul(normalMap, tangentMatrix)); 

				float3 surfaceAlbedo = albedoMap.rgb * _BaseColor.rgb;
				float surfaceSpecular = _SpecularLevel;
				float surfaceReflctance = _Reflectance;
				float surfaceRoughness = _Roughness;
				MicrofaceContext microfaceContext = InitMicrofaceContext(surfaceSpecular, surfaceRoughness, surfaceReflctance, surfaceAlbedo);

				lightingBuffer = 0;
				for(int i = 0; i < g_DirectionalLightCount; ++i)
				{
					float3 lightColor = g_DirectionalLightBuffer[i].color.rgb;
					float3 lightDirWS = g_DirectionalLightBuffer[i].directional.xyz;
					float3 halfDirWS = normalize(lightDirWS + cameraDirWS);

					BSDFContext bsdfContext = InitBXDFContext(pnormalWS, cameraDirWS, lightDirWS, halfDirWS);
					lightingBuffer.rgb += DefultLit(bsdfContext, microfaceContext);
					lightingBuffer.rgb *= lightColor * bsdfContext.NoL;
				}

				//lightingBuffer += float4(albedoMap * indirectLight, 1);
			}
			ENDHLSL
		}
	}
}
