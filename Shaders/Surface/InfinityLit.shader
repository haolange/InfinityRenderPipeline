Shader "InfinityPipeline/InfinityLit"
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

		//ShadowBuffer
		Pass
		{
			Name "ShadowBufferPass"
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
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols
			//#pragma multi_compile _ LIGHTMAP_ON


			#include "../Private/Common.hlsl"
			#include "../Private/Lightmap.hlsl"
			#include "../Private/GBufferPack.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				int _BaseColorTile;
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END
			
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 worldPos : TEXCOORD2;
				float4 vertex : SV_POSITION;

				/*#if defined(LIGHTMAP_ON)
				float2 uv1 : TEXCOORD3;
				#endif*/
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			Varyings vert (Attributes In)
			{
				Varyings Out = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(In);
                UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv0 = In.uv0;
				Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, Out.worldPos);

				/*#if defined(LIGHTMAP_ON)
					Out.uv1 = In.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif*/

				return Out;
			}
			
			void frag (Varyings In, out float4 GBufferA : SV_Target0, out uint4 GBufferB : SV_Target1)
			{
				UNITY_SETUP_INSTANCE_ID(In);
				
				float3 WS_PixelPos = In.worldPos.xyz;
				float3 BaseColor = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile).rgb * _BaseColor.rgb;
				
				/*float3 IndirectLight = 1;
				#if defined(LIGHTMAP_ON)
					IndirectLight = SampleLightmap(In.uv1, In.normal);
				#endif*/

				//GBufferA = float4(BaseColor, 1);
				//GBufferB = uint4((In.normal * 127 + 127), 1);

				FGBufferData GBufferData;
				GBufferData.WorldNormal = normalize(In.normal);
				GBufferData.BaseColor = BaseColor;
				GBufferData.Roughness = BaseColor.r;
				GBufferData.Specular = _SpecularLevel;
				GBufferData.Reflactance = BaseColor.b;
				EncodeGBuffer(GBufferData, GBufferA, GBufferB);
			}
			ENDHLSL
		}

		//MotionBuffer
		Pass
		{
			Name "OpaqueMotionPass"
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
			Name "ForwardPass"
			Tags { "LightMode" = "ForwardPlus" }
			ZTest Equal ZWrite Off Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols
			#pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON


			#include "../Private/Common.hlsl"
			#include "../Private/Lightmap.hlsl"
			#include "../Private/GBufferPack.hlsl"
			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


			CBUFFER_START(UnityPerMaterial)
				int _BaseColorTile;
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END

			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 worldPos : TEXCOORD2;
				float4 vertex : SV_POSITION;

				#if defined(LIGHTMAP_ON)
				float2 uv1 : TEXCOORD3;
				#endif

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv0 = In.uv0;
				Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, Out.worldPos);

				#if defined(LIGHTMAP_ON)
					Out.uv1 = In.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif

				return Out;
			}

			void frag(Varyings In, out float3 DiffuseBuffer : SV_Target0, out float3 SpecularBuffer : SV_Target1)
			{
				UNITY_SETUP_INSTANCE_ID(In);

				float3 WS_PixelPos = In.worldPos.xyz;
				float3 BaseColor = _MainTex.Sample(sampler_MainTex, In.uv0 * _BaseColorTile).rgb * _BaseColor.rgb;

				float3 IndirectLight = 1;
				#if defined(LIGHTMAP_ON)
					IndirectLight = SampleLightmap(In.uv1, In.normal);
				#endif

				DiffuseBuffer = BaseColor * IndirectLight;
				SpecularBuffer = 0.5f;
			}
			ENDHLSL
		}

		//BakeLighting
		Pass
		{
			Name "Meta"
			Tags { "LightMode" = "Meta" }

			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			CBUFFER_START(UnityPerMaterial)
				int _BaseColorTile;
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END

			Texture2D _MainTex; SamplerState sampler_MainTex;

			CBUFFER_START(UnityMetaPass)
				bool4 unity_MetaVertexControl;
				bool4 unity_MetaFragmentControl;
			CBUFFER_END

			float unity_OneOverOutputBoost;
			float unity_MaxOutputValue;
			float unity_UseLinearSpace;

			struct MetaInput
			{
				float3 Albedo;
				float3 Emission;
				float3 SpecularColor;
			};

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float3 normalOS     : NORMAL;
				float2 uv0          : TEXCOORD0;
				float2 uv1          : TEXCOORD1;
				float2 uv2          : TEXCOORD2;
			};

			struct Varyings
			{
				float4 pos:SV_POSITION;
				float2 uv:TEXCOORD1;
			};

			float4 MetaVertexPosition(float4 positionOS, float2 uv1, float2 uv2, float4 uv1ST, float4 uv2ST)
			{
				if (unity_MetaVertexControl.x)
				{
					positionOS.xy = uv1 * uv1ST.xy + uv1ST.zw;
					// OpenGL right now needs to actually use incoming vertex position,
					// so use it in a very dummy way
					positionOS.z = positionOS.z > 0 ? REAL_MIN : 0.0f;
				}
				if (unity_MetaVertexControl.y)
				{
					positionOS.xy = uv2 * uv2ST.xy + uv2ST.zw;
					// OpenGL right now needs to actually use incoming vertex position,
					// so use it in a very dummy way
					positionOS.z = positionOS.z > 0 ? REAL_MIN : 0.0f;
				}
				return mul(unity_MatrixVP, float4(positionOS.xyz, 1.0));
			}
				
			float4 MetaFragment(MetaInput input)
			{
				float4 res = 0;
				if (unity_MetaFragmentControl.x)
				{
					res = float4(input.Albedo, 1.0);

					// d3d9 shader compiler doesn't like NaNs and infinity.
					unity_OneOverOutputBoost = saturate(unity_OneOverOutputBoost);

					// Apply Albedo Boost from LightmapSettings.
					res.rgb = clamp(PositivePow(res.rgb, unity_OneOverOutputBoost), 0, unity_MaxOutputValue);
				}
				if (unity_MetaFragmentControl.y)
				{
					float3 emission;
					if (unity_UseLinearSpace)
						emission = input.Emission;
					else
						emission = LinearToSRGB(input.Emission);

					res = float4(emission, 1.0);
				}
				return res;
			}

			Varyings vert(Attributes In)
			{
				Varyings Out;

				Out.uv = In.uv0;
				//Out.pos = mul(unity_MatrixVP, float4(In.positionOS.xyz, 1.0));
				Out.pos = MetaVertexPosition(In.positionOS, In.uv1, In.uv2, unity_LightmapST, unity_DynamicLightmapST);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				MetaInput Out;
				Out.Albedo = _MainTex.Sample(sampler_MainTex, In.uv).rgb * _BaseColor.rgb;
				Out.Emission = 0;
				Out.SpecularColor = 0.04;
				return MetaFragment(Out);
			}
			ENDHLSL
		}

		//RayTrace AO
		/*Pass
		{
			Name "RTAOPass"
			Tags { "LightMode" = "RayTraceAmbientOcclusion" }

			HLSLPROGRAM
			#pragma raytracing test

			#include "../Private/RayTracing/Common/RayTracingCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _SpecularLevel;
				int _BaseColorTile;
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
		}*/
	}
}
