Shader "InfinityPipeline/InfinityLit-Instanced"
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

		//Shadow Pass
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
				uint PrimitiveId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 vertex_CS : SV_POSITION;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				Out.PrimitiveId = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				float4 vertex_WS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.vertex_CS = mul(Matrix_ViewProj, vertex_WS);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

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
				Out.PrimitiveId  = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[Out.PrimitiveId];

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

			#pragma multi_compile _ _DBUFFER

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/Lightmap.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/DBuffer.hlsl"
			#include "../ShaderLibrary/BSDF.hlsl"
			#include "../ShaderLibrary/ImageBasedLighting.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"

			StructuredBuffer<float4> _AtmosphereSkySH;
			Texture2DArray<float4> _AtmosphereGGXPrefilter;
			float _AtmosphereIBLMaxMip;
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

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
				Out.PrimitiveId  = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				Out.vertexWS = mul(meshBatch.matrix_LocalToWorld, float4(In.vertexOS.xyz, 1.0));
				Out.vertexCS = mul(Matrix_ViewJitterProj, Out.vertexWS);
				//Out.normal = normalize(mul(Out.normal, (float3x3)meshBatch.matrix_LocalToWorld));
				Out.normalWS = normalize(mul((float3x3)meshBatch.matrix_LocalToWorld, In.normalOS));
				Out.tangentWS = normalize(mul(meshBatch.matrix_LocalToWorld, float4(In.tangentOS.xyz, 0)).xyz);
				Out.bitangentWS = normalize(cross(Out.normalWS, Out.tangentWS) * In.tangentOS.w);
				return Out;
			}
			
			void frag (Varyings In, out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1, out float4 GBufferC : SV_Target2, out float4 LightingBuffer : SV_Target3)
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
				GBufferData.Flags = _Subsurface > 0.5 ? GBUFFER_FLAG_SUBSURFACE : 0;
				GBufferData.SSSProfileIndex = (uint)(_SSSProfileIndex + 0.5);
				GBufferData.Thickness = _SSSThickness;
				EncodeGBuffer(GBufferData, In.vertexCS.xy, GBufferA, GBufferB, GBufferC);
				LightingBuffer = float4(_EmissionColor.rgb, 0);
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
			#include "../ShaderLibrary/ImageBasedLighting.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"

			StructuredBuffer<float4> _AtmosphereSkySH;
			Texture2DArray<float4> _AtmosphereGGXPrefilter;
			float _AtmosphereIBLMaxMip;
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


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
				Out.PrimitiveId  = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[Out.PrimitiveId];

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
					FLightRecord dirLight = g_LightRecordBuffer[i];
					float3 lightColor = LightRadiance(dirLight);
					float3 lightDirWS = dirLight.directionSpot.xyz;
					float3 halfDirWS = normalize(lightDirWS + cameraDirWS);

					BSDFContext bsdfContext = InitBXDFContext(pnormalWS, cameraDirWS, lightDirWS, halfDirWS);
					lightingBuffer.rgb += DefultLit(bsdfContext, microfaceContext) * lightColor * saturate(bsdfContext.NoL);
				}

				if (g_HasTileLightList != 0 && g_LocalLightCount > 0)
				{
					uint2 tile = (uint2)(In.vertexCS.xy / 16.0);
					uint tileIndex = tile.y * (uint)ceil(_ScreenParams.x / 16.0) + tile.x;
					uint2 range = SRV_TileLightRange[tileIndex];
					[loop]
					for (uint li = 0; li < range.y; ++li)
					{
						FLightRecord light = g_LightRecordBuffer[SRV_TileLightList[range.x + li]];
						float3 toLight = light.positionRange.xyz - positionWS;
						float dist = length(toLight);
						float3 lightDirWS = toLight / max(dist, 1e-4);
						float att = DistanceAttenuation(dist, light.positionRange.w);
						if (light.lightType == LIGHT_TYPE_SPOT)
						{
							att *= SpotAttenuation(lightDirWS, light.directionSpot.xyz, light.shape.x, light.directionSpot.w);
						}
						float3 halfDirWS = normalize(lightDirWS + cameraDirWS);
						BSDFContext bsdfContext = InitBXDFContext(pnormalWS, cameraDirWS, lightDirWS, halfDirWS);
						lightingBuffer.rgb += DefultLit(bsdfContext, microfaceContext) * LightRadiance(light) * saturate(bsdfContext.NoL) * att;
					}
				}

				lightingBuffer.rgb += EvaluateAtmosphereIBL(microfaceContext, pnormalWS, cameraDirWS, _AtmosphereSkySH, _AtmosphereGGXPrefilter, _AtmosphereIBLMaxMip);
				lightingBuffer.rgb += _EmissionColor.rgb;
			}
			ENDHLSL
		}

		//Motion Pass
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
			//#pragma enable_d3d11_debug_symbols

			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId : SV_InstanceID;
				float4 clipPos : TEXCOORD0;
				float4 clipPosOld : TEXCOORD1;
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				Out.PrimitiveId = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData currBatch = transformBuffer[Out.PrimitiveId];
				FTransformData prevBatch = previousTransformBuffer[Out.PrimitiveId];

				float4 worldPos = mul(currBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				float4 worldPosOld = mul(prevBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));

				Out.vertex = mul(Matrix_ViewJitterProj, worldPos);
				Out.clipPos = mul(Matrix_ViewProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewProj, worldPosOld);
				return Out;
			}

			float2 frag(Varyings In) : SV_Target
			{
				float2 hPos = (In.clipPos.xy / In.clipPos.w);
				float2 hPosOld = (In.clipPosOld.xy / In.clipPosOld.w);

				float2 ndcPos = (hPos.xy + 1.0f) / 2.0f;
				float2 ndcPosOld = (hPosOld.xy + 1.0f) / 2.0f;
				return ndcPos - ndcPosOld;
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

			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				uint primitiveId = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[primitiveId];
				float4 worldPos = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, worldPos);
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

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ _VOLUMETRIC_FOG
			#pragma multi_compile _ _AERIAL_PERSPECTIVE

			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "../ShaderLibrary/TranslucentCommon.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
				float4 clipPos : TEXCOORD2;
				float4 clipPosOld : TEXCOORD3;
				float4 vertex : SV_POSITION;
			};

			struct FragOutput
			{
				float4 color : SV_Target0;
				float reactive : SV_Target1;
				float2 motion : SV_Target2;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				uint primitiveId = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[primitiveId];
				Out.uv0 = In.uv0;
				float4 worldPos = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.worldPos = worldPos.xyz;
				Out.vertex = mul(Matrix_ViewJitterProj, worldPos);
				Out.clipPos = mul(Matrix_ViewProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewProj, mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0)));
				return Out;
			}

			FragOutput frag(Varyings In)
			{
				float2 screenUV = In.vertex.xy / _ScreenParams.xy;
				float linearDepth = length(In.worldPos - _WorldSpaceCameraPos);
				float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv0).rgb * _BaseColor.rgb;
				FragOutput o;
				o.color = ApplyT0Fog(albedo, _BaseColor.a, screenUV, linearDepth);
				o.reactive = TranslucentReactive(_BaseColor.a);
				o.motion = TranslucentMotion(In.clipPos, In.clipPosOld);
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

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ _REFRACTION_PYRAMID

			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "../ShaderLibrary/TranslucentCommon.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
				float _Roughness;
				float _RefractionStrength;
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
				float2 uv0 : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float4 clipPos : TEXCOORD2;
				float4 clipPosOld : TEXCOORD3;
				float4 vertex : SV_POSITION;
			};

			struct FragOutput
			{
				float4 color : SV_Target0;
				float reactive : SV_Target1;
				float2 motion : SV_Target2;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				uint primitiveId = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[primitiveId];
				Out.uv0 = In.uv0;
				float4 worldPos = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.normalWS = normalize(mul((float3x3)meshBatch.matrix_LocalToWorld, In.normal));
				Out.vertex = mul(Matrix_ViewJitterProj, worldPos);
				Out.clipPos = mul(Matrix_ViewProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewProj, worldPos);
				return Out;
			}

			FragOutput frag(Varyings In)
			{
				float2 screenUV = In.vertex.xy / _ScreenParams.xy;
				float glassDepth = _TranslucentDepthTexture.SampleLevel(Global_point_clamp_sampler, screenUV, 0).r;
				float thickness = saturate(abs(LinearEyeDepth(In.vertex.z, _ZBufferParams) - LinearEyeDepth(glassDepth, _ZBufferParams)) * 0.05);
				float4 refracted = SampleRefractionPyramid(screenUV, normalize(In.normalWS), _RefractionStrength * (0.25 + thickness), _Roughness);
				float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv0).rgb * _BaseColor.rgb;
				float3 color = lerp(albedo, refracted.rgb * albedo, saturate(_BaseColor.a));
				FragOutput o;
				o.color = float4(color, _BaseColor.a);
				o.reactive = TranslucentReactive(_BaseColor.a);
				o.motion = TranslucentMotion(In.clipPos, In.clipPosOld);
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

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../ShaderLibrary/GPUScene.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
			#include "../ShaderLibrary/TranslucentCommon.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
			CBUFFER_END
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float4 clipPos : TEXCOORD1;
				float4 clipPosOld : TEXCOORD2;
				float4 vertex : SV_POSITION;
			};

			struct FragOutput
			{
				float4 color : SV_Target0;
				float reactive : SV_Target1;
				float2 motion : SV_Target2;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				uint primitiveId = instanceIndexBuffer[In.InstanceId + instanceIndexOffset];
				FTransformData meshBatch = transformBuffer[primitiveId];
				Out.uv0 = In.uv0;
				float4 worldPos = mul(meshBatch.matrix_LocalToWorld, float4(In.vertex.xyz, 1.0));
				Out.vertex = mul(Matrix_ViewJitterProj, worldPos);
				Out.clipPos = mul(Matrix_ViewProj, worldPos);
				Out.clipPosOld = mul(Matrix_LastViewProj, worldPos);
				return Out;
			}

			FragOutput frag(Varyings In)
			{
				float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv0).rgb * _BaseColor.rgb;
				FragOutput o;
				o.color = float4(albedo, _BaseColor.a);
				o.reactive = TranslucentReactive(_BaseColor.a);
				o.motion = TranslucentMotion(In.clipPos, In.clipPosOld);
				return o;
			}
			ENDHLSL
		}
	}
	CustomEditor "InfinityTech.Rendering.Editor.InfinityLitGUI"
}
