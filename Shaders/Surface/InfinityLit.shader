Shader "InfinityPipeline/InfinityLit"
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
		//[NoScaleOffset]g_NormalScaleTable ("BestFitTexture", 2D) = "white" {}
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

		//ShadowPass
		Pass
		{
			Name "ShadowPass"
			Tags { "LightMode" = "ShadowPass" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0 

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/ShaderVariables.hlsl"
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

		//DepthPass
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
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/ShaderVariables.hlsl"
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

		//GBufferPass
		Pass
		{
			Name "GBufferPass"
			Tags { "LightMode" = "GBufferPass" }
			ZTest[_ZTest] ZWrite[_ZWrite] Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols
			//#pragma multi_compile _ LIGHTMAP_ON

			#include "../ShaderLibrary/Common.hlsl"
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
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				float4 tangentOS : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				/*#if defined(LIGHTMAP_ON)
				float2 uv1 : TEXCOORD1;
				#endif*/
				float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
				float4 vertexWS : TEXCOORD5;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv0 = In.uv0;
				/*#if defined(LIGHTMAP_ON)
				Out.uv1 = In.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif*/
				Out.vertexWS = mul(UNITY_MATRIX_M, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(UNITY_MATRIX_VP, Out.vertexWS);
				Out.normalWS = normalize(mul(In.normalOS, (float3x3)unity_WorldToObject));
				Out.tangentWS = normalize(mul(unity_ObjectToWorld, float4(In.tangentOS.xyz, 0)).xyz);
                Out.bitangentWS = normalize(cross(Out.normalWS, Out.tangentWS) * In.tangentOS.w);
				return Out;
			}
			
			void frag (Varyings In, out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1)
			{
				UNITY_SETUP_INSTANCE_ID(In);
				
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

				/*float3 IndirectLight = 1;
				#if defined(LIGHTMAP_ON)
					IndirectLight = SampleLightmap(In.uv1, In.normal);
				#endif*/

				//GBufferA = float4(Albedo, 1);
				//GBufferB = uint4((In.normal * 127 + 127), 1);

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

		//ForwardPlus
		Pass
		{
			Name "ForwardPass"
			Tags { "LightMode" = "ForwardPass" }
			ZTest Equal ZWrite Off Cull Back
		
			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols
			#pragma multi_compile _ LIGHTMAP_ON

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/BSDF.hlsl"
			#include "../ShaderLibrary/Lightmap.hlsl"
			#include "../ShaderLibrary/Lighting.hlsl"
			#include "../ShaderLibrary/ShadingModel.hlsl"
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
			Texture2D unity_ShadowMask; SamplerState samplerunity_ShadowMask;

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				float4 tangentOS : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				#if defined(LIGHTMAP_ON)
				float2 uv1 : TEXCOORD1;
				#endif
				float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
				float4 vertexWS : TEXCOORD5;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv0 = In.uv0;
				#if defined(LIGHTMAP_ON)
				Out.uv1 = In.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				Out.vertexWS = mul(UNITY_MATRIX_M, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(UNITY_MATRIX_VP, Out.vertexWS);
				Out.normalWS = normalize(mul(In.normalOS, (float3x3)unity_WorldToObject));
				Out.tangentWS = normalize(mul(unity_ObjectToWorld, float4(In.tangentOS.xyz, 0)).xyz);
                Out.bitangentWS = normalize(cross(Out.normalWS, Out.tangentWS) * In.tangentOS.w);
				return Out;
			}

			float SampleBakedShadows(float2 lightMapUV) 
			{
				#if defined(LIGHTMAP_ON)
					return unity_ShadowMask.Sample(samplerunity_ShadowMask, lightMapUV).r;
				#else
					return 1.0;
				#endif
			}

			void frag(Varyings In, out float4 lightingBuffer : SV_Target0)
			{
				UNITY_SETUP_INSTANCE_ID(In);

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

				float staticShadow = 1;
				float3 indirectLight = 1;

				#if defined(LIGHTMAP_ON)
					staticShadow = SampleBakedShadows(In.uv1);
					indirectLight = SampleLightmap(In.uv1, pnormalWS);
				#else
					indirectLight = 0;
				#endif

				lightingBuffer = 0;
				for(int i = 0; i < g_DirectionalLightCount; ++i)
				{
					float3 lightColor = g_DirectionalLightBuffer[i].color.rgb;
					float3 lightDirWS = g_DirectionalLightBuffer[i].directional.xyz;
					float3 halfDirWS = normalize(lightDirWS + cameraDirWS);

					BSDFContext bsdfContext = InitBXDFContext(pnormalWS, cameraDirWS, lightDirWS, halfDirWS);
					lightingBuffer.rgb += DefultLit(bsdfContext, microfaceContext);
					lightingBuffer.rgb *= lightColor * saturate(bsdfContext.NoL) * staticShadow;
				}
				
				lightingBuffer += float4(surfaceAlbedo * indirectLight, 1);
			}
			ENDHLSL
		}

		//MotionBuffer
		Pass
		{
			Name "MotionPass"
			Tags { "LightMode" = "MotionPass" }
			ZTest Equal ZWrite Off Cull Back
            Stencil
			{
                Ref 5
                comp always
                pass replace
            }
			
			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				float3 vertexOld : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
				float4 clipPos : TEXCOORD0;
				float4 clipPosOld : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				float4 WorldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1));
				Out.vertex = mul(UNITY_MATRIX_VP, WorldPos);
				
				Out.clipPos = mul(Matrix_ViewProj, WorldPos);
				Out.clipPosOld = mul(Matrix_LastViewProj, mul(unity_MatrixPreviousM, unity_MotionVectorsParams.x > 0 ? float4(In.vertexOld, 1) : In.vertex));
				return Out;
			}

			float2 frag(Varyings In) : SV_Target
			{
				float2 hPos = (In.clipPos.xy / In.clipPos.w);
				float2 hPosOld = (In.clipPosOld.xy / In.clipPosOld.w);

				// V is the viewport position at this pixel in the range 0 to 1.
				float2 ndcPos = (hPos.xy + 1.0f) / 2.0f;
				float2 ndcPosOld = (hPosOld.xy + 1.0f) / 2.0f;

				#if UNITY_UV_STARTS_AT_TOP
					ndcPos.y = 1 - ndcPos.y;
					ndcPosOld.y = 1 - ndcPosOld.y;
				#endif
				
				float2 objectMotion = ndcPos - ndcPosOld;
				//float2 objectMotion = (ndcPos - ndcPosOld) * 0.5;
				return lerp(objectMotion, 0, unity_MotionVectorsParams.y == 0);
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

			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
				float4 _BaseColor;
			CBUFFER_END

			CBUFFER_START(UnityMetaPass)
				bool4 unity_MetaVertexControl;
				bool4 unity_MetaFragmentControl;
			CBUFFER_END

			float unity_OneOverOutputBoost;
			float unity_MaxOutputValue;
			float unity_UseLinearSpace;
			Texture2D _MainTex; SamplerState sampler_MainTex;

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
			Name "RTAO"
			Tags { "LightMode" = "RayTraceAmbientOcclusion" }

			HLSLPROGRAM
			#pragma raytracing test

			#include "../ShaderLibrary/RayTracing/Common/RayTracingCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
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
