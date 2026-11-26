Shader "InfinityPipeline/InfinitySkin"
{
	Properties
	{
		[Header(Color)]
		[NoScaleOffset] _MainTex ("BaseColorTexture", 2D) = "white" {}
		_BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
		_BaseColorTile ("BaseColorTile", Range(0, 1024)) = 1

		[Header(Normal)]
		[NoScaleOffset] _NormalTexture ("NormalTexture", 2D) = "bump" {}
		_NormalTile ("NormalTile", Range(0, 100)) = 1

		[Header(Occlusion)]
		[NoScaleOffset] _OcclusionTexture ("OcclusionTexture", 2D) = "white" {}
		_OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1

		[Header(Specular)]
		_Roughness ("Roughness", Range(0, 1)) = 0.45
		_SpecularLevel ("SpecularLevel", Range(0, 1)) = 0.35
		_SecondLobeMix ("SecondLobeMix", Range(0, 1)) = 0.85
		_SecondLobeRoughnessScale ("SecondLobeRoughnessScale", Range(0.1, 1)) = 0.5

		[Header(Subsurface)]
		[IntRange] _DiffusionProfileIndex ("DiffusionProfileIndex", Range(0, 14)) = 0

		// Skin is always Forward opaque (MaterialRouteUtility); the routing contract still reads both values.
		[HideInInspector] _SurfaceRoute ("Surface Route", Float) = 1
		[HideInInspector] _TranslucentStage ("Translucent Stage", Float) = 0
	}

	SubShader
	{
		Tags{"RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque"}

		//ShadowPass
		Pass
		{
			Name "RasterShadowPass"
			Tags { "LightMode" = "RasterShadowPass" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0

			HLSLPROGRAM
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#include "../Library/ShadowCaster.hlsl"
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer

			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "../Library/InfinitySkinMaterial.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
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
				float4 positionWS = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = ShadowCasterClipPosition(positionWS.xyz, normalize(mul(In.normal, (float3x3)unity_WorldToObject)));
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
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
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#include "../Library/ShadowCaster.hlsl"
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer

			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "../Library/InfinitySkinMaterial.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
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
				float4 positionWS = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0));
				Out.vertex = ShadowCasterClipPosition(positionWS.xyz, normalize(mul(In.normal, (float3x3)unity_WorldToObject)));
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
			Name "RasterDepthPass"
			Tags { "LightMode" = "RasterDepthPass" }
			ZTest LEqual ZWrite On Cull Back
			ColorMask 0

			HLSLPROGRAM
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer

			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "../Library/InfinitySkinMaterial.hlsl"

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
				Out.vertex = mul(UNITY_MATRIX_VP, mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1.0)));
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		//GBufferPass: the surface proxy, emission-free geometry color and the subsurface admission bit with its profile.
		Pass
		{
			Name "RasterGBufferPass"
			Tags { "LightMode" = "RasterGBufferPass" }
			ZTest Equal ZWrite Off Cull Back
			Stencil
			{
				Ref 4
				ReadMask 6
				WriteMask 6
				Comp Always
				Pass Replace
			}

			HLSLPROGRAM
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
			#pragma multi_compile _ INFINITY_SIDEBAND

			#include "../Library/Common.hlsl"
			#include "../Library/Lightmap.hlsl"
			#include "../Library/GBufferPack.hlsl"
			#include "../Library/GeometryColor.hlsl"
			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "../Library/InfinitySkinSurface.hlsl"

			StructuredBuffer<float4> _AtmosphereSkySH;

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

			GBufferFragmentOutput frag(Varyings In)
			{
				UNITY_SETUP_INSTANCE_ID(In);
				SkinSurface skin = EvaluateSkinSurface(In.uv0, In.normalWS, In.tangentWS, In.bitangentWS);
				float3 positionWS = In.vertexWS.xyz;
				float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
				uint lightChannels = asuint(unity_RenderingLayer.x);

				float4 bakedDiffuse = 0;
				float4 shadowMask = 1;
				SampleBakedLighting(In.uv1, positionWS, skin.normalWS, viewDir, In.vertexCS.xy, lightChannels, bakedDiffuse, shadowMask);

				SurfaceData surface;
				surface.Albedo = skin.albedo;
				surface.Roughness = _Roughness;
				surface.Normal = skin.normalWS;
				surface.Metallic = 0;
				surface.Specular = _SpecularLevel;

				GBufferFragmentOutput output = (GBufferFragmentOutput)0;
				EncodeSurfaceGBuffer(surface, output.gBufferA, output.gBufferB);
				WriteGeometryColor(skin.albedo, skin.occlusion, bakedDiffuse, shadowMask, skin.normalWS, _AtmosphereSkySH,
					lightChannels, SkinSidebandProfile(), output);
				return output;
			}
			ENDHLSL
		}

		//ForwardPass: the complete skin shading in one execution, dual-lobe Beckmann specular included.
		Pass
		{
			Name "RasterForwardPass"
			Tags { "LightMode" = "RasterForwardPass" }
			ZTest Equal ZWrite Off Cull Back
			ColorMask R 1

			HLSLPROGRAM
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ INFINITY_SIDEBAND
			#pragma multi_compile _ INFINITY_FORWARD_AO
			#pragma multi_compile _ INFINITY_FORWARD_SSGI
			#pragma multi_compile _ INFINITY_FORWARD_SSR
			#pragma multi_compile _ INFINITY_FORWARD_CONTACT_SHADOW
			#pragma multi_compile _ INFINITY_FORWARD_ANALYTIC_SHADOW
			#pragma multi_compile _ INFINITY_CASCADE_SHADOW_MOMENTS
			#pragma multi_compile _ INFINITY_LOCAL_SHADOW_MOMENTS
			#pragma multi_compile _ INFINITY_LIGHT_COOKIES
			#pragma multi_compile _ INFINITY_REFLECTION_PROBES
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#define INFINITY_SKIN_SPECULAR 1

			#include "../Library/SurfaceLighting.hlsl"
			#include "../Library/Common.hlsl"
			#include "../Library/BSDF.hlsl"
			#include "../Library/Lightmap.hlsl"
			#include "../Library/Lighting.hlsl"
			#include "../Library/ForwardOutput.hlsl"
			#include "../Library/ImageBasedLighting.hlsl"
			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "../Library/InfinitySkinSurface.hlsl"

			StructuredBuffer<float4> _AtmosphereSkySH;
			Texture2DArray<float4> _AtmosphereGGXPrefilter;
			float _AtmosphereIBLMaxMip;

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

			ForwardFragmentOutput frag(Varyings In)
			{
				UNITY_SETUP_INSTANCE_ID(In);
				SkinSurface skin = EvaluateSkinSurface(In.uv0, In.normalWS, In.tangentWS, In.bitangentWS);
				MicrofaceContext microfaceContext = InitSkinMicrofaceContext(skin);
				float3 positionWS = In.vertexWS.xyz;
				float3 normalWS = skin.normalWS;
				float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
				uint2 positionSS = (uint2)In.vertexCS.xy;
				uint lightChannels = asuint(unity_RenderingLayer.x);

				float4 baked, mask;
				SampleBakedLighting(In.uv1, positionWS, normalWS, viewDir, In.vertexCS.xy, lightChannels, baked, mask);
				float viewDepth = -mul(unity_MatrixV, float4(positionWS, 1)).z;
				float occlusion = ForwardIndirectOcclusion(positionSS, viewDepth, positionWS, normalWS, lightChannels);
				float3 indirectDiffuse = ForwardIndirectDiffuse(positionSS, microfaceContext.AlbedoColor, IndirectIrradiance(baked, normalWS, _AtmosphereSkySH),
					occlusion * skin.occlusion);
				float3 reflectionWS = reflect(-viewDir, normalWS);
				float3 sky = SampleGGXCubemapArray(_AtmosphereGGXPrefilter, reflectionWS, microfaceContext.RoughnessClamp, _AtmosphereIBLMaxMip);
				float3 environmentBRDF = EnvBRDFApprox(microfaceContext.SpecularColor, microfaceContext.RoughnessClamp, saturate(dot(normalWS, viewDir))).rgb;
				float3 environmentSpecular = ForwardEnvironmentSpecular(positionSS,
					BlendReflectionProbesWithSky(positionWS, reflectionWS, microfaceContext.RoughnessClamp, positionSS, viewDepth, sky), environmentBRDF, occlusion);
				float3 directDiffuse;
				float3 direct = EvaluateSurfaceLights(positionWS, normalWS, viewDir, microfaceContext, SelectMixedLightVisibility(mask), viewDepth, lightChannels, positionSS, directDiffuse);
				return WriteForwardColor(indirectDiffuse + direct + environmentSpecular, indirectDiffuse + directDiffuse);
			}
			ENDHLSL
		}

		//MotionBuffer
		Pass
		{
			Name "RasterMotionPass"
			Tags { "LightMode" = "RasterMotionPass" }
			ZTest Equal ZWrite Off Cull Back
			Stencil
			{
				Ref 1
				ReadMask 1
				WriteMask 1
				Comp Always
				Pass Replace
			}

			HLSLPROGRAM
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer

			#include "../Library/ShaderVariables.hlsl"
			#include "../Library/MotionVectors.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "../Library/NativeMotion.hlsl"
			#include "../Library/InfinitySkinMaterial.hlsl"

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
				float4 positionWS = mul(UNITY_MATRIX_M, float4(In.vertex.xyz, 1));
				Out.vertex = mul(UNITY_MATRIX_VP, positionWS);
				Out.clipPos = mul(Matrix_ViewFlipYProj, positionWS);
				Out.clipPosOld = mul(Matrix_LastViewFlipYProj, NativePreviousPosition(In.vertex, In.vertexId));
				return Out;
			}

			MotionOutput frag(Varyings In)
			{
				MotionOutput output;
				output.velocity = SurfaceVelocity(In.clipPos, In.clipPosOld);
				return output;
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
			#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma vertex vert
			#pragma fragment frag

			#include "../Library/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "../Library/InfinitySkinMaterial.hlsl"

			CBUFFER_START(UnityMetaPass)
				bool4 unity_MetaVertexControl;
				bool4 unity_MetaFragmentControl;
			CBUFFER_END

			float unity_OneOverOutputBoost;
			float unity_MaxOutputValue;
			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float2 uv2 : TEXCOORD2;
			};

			struct Varyings
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD1;
			};

			Varyings vert(Attributes In)
			{
				Varyings Out;
				float4 positionOS = In.positionOS;
				if (unity_MetaVertexControl.x)
				{
					positionOS.xy = In.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
					// OpenGL needs the incoming vertex position to be used.
					positionOS.z = positionOS.z > 0 ? REAL_MIN : 0.0f;
				}
				if (unity_MetaVertexControl.y)
				{
					positionOS.xy = In.uv2 * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
					positionOS.z = positionOS.z > 0 ? REAL_MIN : 0.0f;
				}
				Out.pos = mul(unity_MatrixVP, float4(positionOS.xyz, 1.0));
				Out.uv = In.uv0;
				return Out;
			}

			// Skin has no emission: the lightmapper reads its albedo only.
			float4 frag(Varyings In) : SV_Target
			{
				float4 result = 0;
				if (unity_MetaFragmentControl.x)
				{
					float3 albedo = _MainTex.Sample(sampler_MainTex, In.uv * _BaseColorTile).rgb * _BaseColor.rgb;
					result = float4(clamp(PositivePow(albedo, saturate(unity_OneOverOutputBoost)), 0, unity_MaxOutputValue), 1.0);
				}
				return result;
			}
			ENDHLSL
		}
	}
	CustomEditor "InfinityTech.Rendering.Editor.InfinitySkinGUI"
}
