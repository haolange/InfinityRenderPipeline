Shader "InfinityPipeline/InfinityLit"
{
	Properties
	{
        [Header (Color)]
        [Toggle (_UseAlbedoTex)]UseBaseColorTex ("UseBaseColorTex", Range(0, 1)) = 0
        [NoScaleOffset]_MainTex ("BaseColorTexture", 2D) = "white" {}
		_BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
        _BaseColorTile ("BaseColorTile", Range(0, 1024)) = 1
        _EmissionColor ("Emission", Color) = (0, 0, 0, 1)

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

		[Header(Subsurface)]
		[Toggle(_SUBSURFACE)] _Subsurface ("Subsurface", Float) = 0
		_SSSProfileIndex ("SSS Profile Index", Range(0, 15)) = 0
		_SSSThickness ("SSS Thickness", Range(0, 1)) = 0

		[Header(Surface Route)]
		[Enum(Deferred, 0, Forward, 1)] _SurfaceRoute ("Surface Route", Float) = 0
		[Enum(None, 0, T0, 1, T1, 2, T2, 3)] _TranslucentStage ("Translucent Stage", Float) = 0
		_RefractionStrength ("Refraction Strength", Range(0, 0.2)) = 0.04

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
            #include "../ShaderLibrary/ShadowCaster.hlsl"
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
			#pragma enable_debug_symbols

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION;
                float3 normal : NORMAL;
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
				WorldPos.xyz = OffsetShadowCaster(WorldPos.xyz, normalize(mul(In.normal, (float3x3)unity_WorldToObject)));
                Out.vertex = mul(Matrix_ViewFlipYProj, WorldPos);
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

		// Unity CreateShadowRendererList looks up LightMode ShadowCaster.
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
            #include "../ShaderLibrary/ShadowCaster.hlsl"
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION;
                float3 normal : NORMAL;
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
				WorldPos.xyz = OffsetShadowCaster(WorldPos.xyz, normalize(mul(In.normal, (float3x3)unity_WorldToObject)));
                Out.vertex = mul(Matrix_ViewFlipYProj, WorldPos);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
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
            #pragma instancing_options renderinglayer
			#pragma enable_debug_symbols

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

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
            #pragma instancing_options renderinglayer
			#pragma enable_debug_symbols
			#pragma multi_compile _ _DBUFFER
			#pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ SHADOWS_SHADOWMASK

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/Lightmap.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/DBuffer.hlsl"
			#include "../ShaderLibrary/BSDF.hlsl"
			#include "../ShaderLibrary/ImageBasedLighting.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"

			StructuredBuffer<float4> _AtmosphereSkySH;
			Texture2DArray<float4> _AtmosphereGGXPrefilter;
			float _AtmosphereIBLMaxMip;
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			CBUFFER_START(UnityPerMaterial)
                float _SurfaceRoute;
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
				float _Subsurface;
				float _SSSProfileIndex;
				float _SSSThickness;
				float4 _BaseColor;
				float4 _EmissionColor;
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
				float2 uv1 : TEXCOORD1;
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
				Out.uv1 = In.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
				Out.vertexWS = mul(UNITY_MATRIX_M, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(UNITY_MATRIX_VP, Out.vertexWS);
				Out.normalWS = normalize(mul(In.normalOS, (float3x3)unity_WorldToObject));
				Out.tangentWS = normalize(mul(unity_ObjectToWorld, float4(In.tangentOS.xyz, 0)).xyz);
                Out.bitangentWS = normalize(cross(Out.normalWS, Out.tangentWS) * In.tangentOS.w);
				return Out;
			}

			void frag (Varyings In, out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1, out float4 GBufferC : SV_Target2, out float4 LightingBuffer : SV_Target3, out float4 BakedDiffuse : SV_Target4, out float4 BakedOcclusion : SV_Target5)
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

				#if defined(_DBUFFER)
				float2 screenUV = In.vertexCS.xy * rcp(_ScreenParams.xy);
				ApplyDBuffer(screenUV, surfaceAlbedo, pnormalWS, surfaceRoughness, surfaceReflctance);
				#endif

				FGBufferData GBufferData;
				GBufferData.Normal = pnormalWS;
				GBufferData.Albedo = surfaceAlbedo;
				GBufferData.Specular = surfaceSpecular;
				GBufferData.Roughness = surfaceRoughness;
				GBufferData.Reflactance = surfaceReflctance;
				GBufferData.ShadingModel = _Subsurface > 0.5 ? GBUFFER_SHADING_MODEL_SUBSURFACE : GBUFFER_SHADING_MODEL_DEFAULT_LIT;
				GBufferData.Flags = (_Subsurface > 0.5 ? GBUFFER_FLAG_SUBSURFACE : 0) | (_SurfaceRoute == 1 ? GBUFFER_FLAG_FORWARD : 0);
				GBufferData.SSSProfileIndex = (uint)(_SSSProfileIndex + 0.5);
				GBufferData.Thickness = _SSSThickness;
                GBufferData.RenderingLayer = asuint(unity_RenderingLayer.x);
				EncodeGBuffer(GBufferData, In.vertexCS.xy, GBufferA, GBufferB, GBufferC);
				LightingBuffer = float4(_EmissionColor.rgb, 0);
                float4 shadowMask;
                SampleBakedLighting(In.uv1, pnormalWS, BakedDiffuse, shadowMask);
                BakedOcclusion = 1 - shadowMask;
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
            #pragma multi_compile _ INFINITY_FORWARD_AO
#if defined(INFINITY_FORWARD_AO)
            Texture2D<float> SRV_ForwardOcclusion;
#endif
            #include "../ShaderLibrary/SurfaceLighting.hlsl"
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
			#pragma enable_debug_symbols
			#pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ SHADOWS_SHADOWMASK

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/BSDF.hlsl"
			#include "../ShaderLibrary/Lightmap.hlsl"
			#include "../ShaderLibrary/Lighting.hlsl"
			#include "../ShaderLibrary/ShadingModel.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ImageBasedLighting.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"

			StructuredBuffer<float4> _AtmosphereSkySH;
			Texture2DArray<float4> _AtmosphereGGXPrefilter;
			float _AtmosphereIBLMaxMip;
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
				float _Subsurface;
				float _SSSProfileIndex;
				float _SSSThickness;
				float4 _BaseColor;
				float4 _EmissionColor;
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
				float2 uv1 : TEXCOORD1;
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

			void frag(Varyings In, out float4 lightingBuffer : SV_Target0, out float4 indirectDiffuse : SV_Target1, out float4 indirectSpecular : SV_Target2)
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

                float4 baked, mask;
                SampleBakedLighting(In.uv1, pnormalWS, baked, mask);
                float viewDepth = -mul(unity_MatrixV, float4(positionWS, 1)).z;
                float3 diffuse = microfaceContext.AlbedoColor * (baked.a > 0 ? baked.rgb : EvaluateSH2Irradiance(pnormalWS, _AtmosphereSkySH));
                float3 specular = SampleGGXCubemapArray(_AtmosphereGGXPrefilter, reflect(-cameraDirWS, pnormalWS), microfaceContext.RoughnessClamp, _AtmosphereIBLMaxMip)
                    * EnvBRDFApprox(microfaceContext.SpecularColor, microfaceContext.RoughnessClamp, saturate(dot(pnormalWS, cameraDirWS))).rgb;
                #if defined(INFINITY_FORWARD_AO)
                float ao = SRV_ForwardOcclusion.Load(int3(In.vertexCS.xy, 0));
                diffuse *= ao; specular *= ao;
#endif
                indirectDiffuse = float4(diffuse, 1);
                indirectSpecular = float4(specular, 1);
                lightingBuffer = float4(EvaluateSurfaceLights(positionWS, pnormalWS, cameraDirWS, microfaceContext, mask, viewDepth, asuint(unity_RenderingLayer.x))
                    + diffuse + specular + _EmissionColor.rgb, 1);
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
            #pragma instancing_options renderinglayer
			#pragma enable_debug_symbols

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				uint vertexId : SV_VertexID;
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

				Out.clipPos = mul(Matrix_ViewFlipYProj, WorldPos);
				Out.clipPosOld = mul(Matrix_LastViewFlipYProj, NativePreviousPosition(In.vertex, In.vertexId));
				return Out;
			}

            MotionOutput frag(Varyings In)
            {
                MotionOutput output;
                output.velocity = SurfaceVelocity(In.clipPos, In.clipPosOld);
                output.metadata = SurfaceMotionMetadata(In.clipPos, In.clipPosOld, In.vertex.z);
                return output;
            }

			ENDHLSL
		}

		Pass
		{
			Name "TranslucentDepthPass"
			Tags { "LightMode" = "TranslucentDepthPass" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);
				float4 worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(UNITY_MATRIX_VP, worldPos);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		Pass
		{
			Name "TranslucentT0Pass"
			Tags { "LightMode" = "TranslucentT0Pass" }
			ZTest LEqual ZWrite Off Cull Back
			Blend 0 SrcAlpha OneMinusSrcAlpha
			Blend 1 One Zero
			Blend 2 One Zero
            Blend 3 One Zero

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
			#pragma multi_compile _ _VOLUMETRIC_FOG
			#pragma multi_compile _ _AERIAL_PERSPECTIVE

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "../ShaderLibrary/TranslucentCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION;
				uint vertexId : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
				float4 clipPos : TEXCOORD2;
				float4 clipPosOld : TEXCOORD3;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct FragOutput
			{
				float4 color : SV_Target0;
				float reactive : SV_Target1;
				float2 motion : SV_Target2;
                float4 motionMetadata : SV_Target3;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);
				Out.uv = In.uv;
				float4 worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.worldPos = worldPos.xyz;
				Out.vertex = mul(UNITY_MATRIX_VP, worldPos);
				Out.clipPos = mul(Matrix_ViewFlipYProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewFlipYProj, NativePreviousPosition(In.vertex, In.vertexId));
				return Out;
			}

			FragOutput frag(Varyings In)
			{
				float2 screenUV = In.vertex.xy / _ScreenParams.xy;
				float linearDepth = length(In.worldPos - _WorldSpaceCameraPos);
				float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv).rgb * _BaseColor.rgb;
				FragOutput o;
				o.color = ApplyT0Fog(albedo, _BaseColor.a, screenUV, linearDepth);
				o.reactive = TranslucentReactive(_BaseColor.a);
				o.motion = SurfaceVelocity(In.clipPos, In.clipPosOld);
                o.motionMetadata = SurfaceMotionMetadata(In.clipPos, In.clipPosOld, In.vertex.z);
				return o;
			}
			ENDHLSL
		}

		Pass
		{
			Name "TranslucentT1Pass"
			Tags { "LightMode" = "TranslucentT1Pass" }
			ZTest LEqual ZWrite Off Cull Back
			Blend 0 SrcAlpha OneMinusSrcAlpha
			Blend 1 One Zero
			Blend 2 One Zero
            Blend 3 One Zero

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
			#pragma multi_compile _ _REFRACTION_PYRAMID

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "../ShaderLibrary/TranslucentCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
				float _Roughness;
				float _RefractionStrength;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
				uint vertexId : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float4 clipPos : TEXCOORD2;
				float4 clipPosOld : TEXCOORD3;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct FragOutput
			{
				float4 color : SV_Target0;
				float reactive : SV_Target1;
				float2 motion : SV_Target2;
                float4 motionMetadata : SV_Target3;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);
				Out.uv = In.uv;
				float4 worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, In.normal));
				Out.vertex = mul(UNITY_MATRIX_VP, worldPos);
				Out.clipPos = mul(Matrix_ViewFlipYProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewFlipYProj, NativePreviousPosition(In.vertex, In.vertexId));
				return Out;
			}

			FragOutput frag(Varyings In)
			{
				float2 screenUV = In.vertex.xy / _ScreenParams.xy;
				float glassDepth = _TranslucentDepthTexture.SampleLevel(Global_point_clamp_sampler, screenUV, 0).r;
				float thickness = saturate(abs(LinearEyeDepth(In.vertex.z, _ZBufferParams) - LinearEyeDepth(glassDepth, _ZBufferParams)) * 0.05);
				float4 refracted = SampleRefractionPyramid(screenUV, normalize(In.normalWS), _RefractionStrength * (0.25 + thickness), _Roughness);
				float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv).rgb * _BaseColor.rgb;
				float3 color = lerp(albedo, refracted.rgb * albedo, saturate(_BaseColor.a));
				FragOutput o;
				o.color = float4(color, _BaseColor.a);
				o.reactive = TranslucentReactive(_BaseColor.a);
				o.motion = SurfaceVelocity(In.clipPos, In.clipPosOld);
                o.motionMetadata = SurfaceMotionMetadata(In.clipPos, In.clipPosOld, In.vertex.z);
				return o;
			}
			ENDHLSL
		}

		Pass
		{
			Name "TranslucentT2Pass"
			Tags { "LightMode" = "TranslucentT2Pass" }
			ZTest LEqual ZWrite Off Cull Back
			Blend 0 SrcAlpha OneMinusSrcAlpha
			Blend 1 One Zero
			Blend 2 One Zero
            Blend 3 One Zero

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "../ShaderLibrary/TranslucentCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "../ShaderLibrary/NativeMotion.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION;
				uint vertexId : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 clipPos : TEXCOORD1;
				float4 clipPosOld : TEXCOORD2;
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct FragOutput
			{
				float4 color : SV_Target0;
				float reactive : SV_Target1;
				float2 motion : SV_Target2;
                float4 motionMetadata : SV_Target3;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);
				Out.uv = In.uv;
				float4 worldPos = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(UNITY_MATRIX_VP, worldPos);
				Out.clipPos = mul(Matrix_ViewFlipYProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewFlipYProj, NativePreviousPosition(In.vertex, In.vertexId));
				return Out;
			}

			FragOutput frag(Varyings In)
			{
				float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv).rgb * _BaseColor.rgb;
				FragOutput o;
				o.color = float4(albedo, _BaseColor.a);
				o.reactive = TranslucentReactive(_BaseColor.a);
				o.motion = SurfaceVelocity(In.clipPos, In.clipPosOld);
                o.motionMetadata = SurfaceMotionMetadata(In.clipPos, In.clipPosOld, In.vertex.z);
				return o;
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
            #include "../ShaderLibrary/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _Roughness;
				float _Reflectance;
				float _NormalTile;
				float _BaseColorTile;
				float _SpecularLevel;
				float _Subsurface;
				float _SSSProfileIndex;
				float _SSSThickness;
				float4 _BaseColor;
				float4 _EmissionColor;
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
				Out.Emission = _EmissionColor.rgb;
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
	CustomEditor "InfinityTech.Rendering.Editor.InfinityLitGUI"
}
