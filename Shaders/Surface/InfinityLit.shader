Shader "InfinityPipeline/InfinityLit"
{
	Properties {
        [Header (Microface)]
        [Toggle (_UseAlbedoTex)]UseBaseColorTex ("UseBaseColorTex", Range(0, 1)) = 0
        [NoScaleOffset]_MainTex ("BaseColorTexture", 2D) = "white" {}
        _BaseColorTile ("BaseColorTile", Range(0, 100)) = 1
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
		Tags{"RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "True" "RenderType" = "InfinityLit"}

		//ShadowBuffer
		Pass
		{
			Name "Pass_ShadowBuffer"
			Tags { "LightMode" = "ShadowBuffer" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0 

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv = In.uv;
				float4 WorldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(UNITY_MATRIX_VP, WorldPos);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				/*UNITY_SETUP_INSTANCE_ID(In);
				if (In.uv.x < 0.5) {
					discard;
				}*/
				return 0;
			}
			ENDHLSL
		}

		//DepthBuffer
		Pass
		{
			Name "Pass_OpaqueDepth"
			Tags { "LightMode" = "OpaqueDepth" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0 

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv = In.uv;
				float4 WorldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, WorldPos);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				/*UNITY_SETUP_INSTANCE_ID(In);
				if (In.uv.x < 0.5) {
					discard;
				}*/
				return 0;
			}
			ENDHLSL
		}

		//ThinGbuffer
		Pass
		{
			Name "Pass_OpaqueGBuffer"
			Tags { "LightMode" = "OpaqueGBuffer" }
			ZTest [_ZTest] 
			ZWrite [_ZWrite] 
			Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols


			#include "../Private/Common.hlsl"
			#include "../Private/PackData.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END
			
			Texture2D _MainTex; 
			SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 worldPos : TEXCOORD2;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			Varyings vert (Attributes In)
			{
				Varyings Out = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(In);
                UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv = In.uv;
				Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, Out.worldPos);
				return Out;
			}
			
			void frag (Varyings In, out float4 ThinGBufferA : SV_Target0, out uint4 ThinGBufferB : SV_Target1)
			{
				UNITY_SETUP_INSTANCE_ID(In);
				
				float3 WS_PixelPos = In.worldPos.xyz;
				float3 BaseColor = _MainTex.Sample(sampler_MainTex, In.uv).rgb;
				
				//ThinGBufferA = float4(BaseColor, 1);
				//ThinGBufferB = uint4((In.normal * 127 + 127), 1);
				ThinGBufferData GBufferData;
				GBufferData.WorldNormal = normalize(In.normal);
				GBufferData.BaseColor = BaseColor;
				GBufferData.Roughness = BaseColor.r;
				GBufferData.Specular = _SpecularLevel;
				GBufferData.Reflactance = BaseColor.b;
				EncodeGBuffer(GBufferData, ThinGBufferA, ThinGBufferB);
			}
			ENDHLSL
		}

		//MotionBuffer
		Pass
		{
			Name "Pass_OpaqueMotion"
			Tags { "LightMode" = "OpaqueMotion" }
			ZTest Equal ZWrite Off Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				float3 vertex_Old : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
				float4 clipPos : TEXCOORD0;
				float4 clipPos_Old : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.clipPos = mul(Matrix_ViewProj, mul(unity_ObjectToWorld, In.vertex));
				Out.clipPos_Old = mul(Matrix_PrevViewProj, mul(unity_MatrixPreviousM, unity_MotionVectorsParams.x > 0 ? float4(In.vertex_Old, 1) : In.vertex));

				float4 WorldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1));
				Out.vertex = mul(Matrix_ViewJitterProj, WorldPos);//UNITY_MATRIX_VP
				return Out;
			}

			float2 frag(Varyings In) : SV_Target
			{
				float2 NDC_PixelPos = (In.clipPos.xy / In.clipPos.w);
				float2 NDC_PixelPos_Old = (In.clipPos_Old.xy / In.clipPos_Old.w);
				float2 ObjectMotion = (NDC_PixelPos - NDC_PixelPos_Old) * 0.5;
				return lerp(ObjectMotion, 0, unity_MotionVectorsParams.y == 0);
			}
			ENDHLSL
		}

		//ForwardPlus
		Pass
		{
			Name "Pass_ForwardPlus"
			Tags { "LightMode" = "ForwardPlus" }
			ZTest Equal ZWrite Off Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols


			#include "../Private/Common.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
			CBUFFER_END

			Texture2D _MainTex; SamplerState sampler_MainTex;


			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 worldPos : TEXCOORD2;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			Varyings vert (Attributes In)
			{
				Varyings Out = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(In);
                UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv = In.uv;
				Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, Out.worldPos);
				return Out;
			}
			
			float4 frag (Varyings In) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(In);
				
				float3 WS_PixelPos = In.worldPos.xyz;
				float3 Normal = In.normal * 0.5 + 0.5;
				float3 Albedo = _MainTex.Sample(sampler_MainTex, In.uv).rgb;
				float Roughness = _BaseColor.r;
				float Reflactance = Albedo.g;

				return float4(Albedo, 1);
			}
			ENDHLSL
		}

		//RayTrace AO
		Pass
		{
			Name "Pass_RTAO"
			Tags { "LightMode" = "RayTraceAmbientOcclusion" }

			HLSLPROGRAM
			#pragma raytracing test

			#include "../Private/RayTracing/Common/RayTracingCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
			CBUFFER_END

			[shader("closesthit")]
			void ClosestHit(inout AORayPayload RayIntersectionAO : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
			{
				RayIntersectionAO.HitDistance = RayTCurrent();
				//Calculate_VertexData(FragInput);
			}

			[shader("anyhit")]
			void Anyhit(inout AORayPayload RayIntersectionAO : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
			{
				IgnoreHit();
			}
			ENDHLSL
		}
	}
}
