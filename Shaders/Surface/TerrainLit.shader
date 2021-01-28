Shader "InfinityPipeline/TerrainLit"
{
    Properties
    {
        [HideInInspector] [ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0.0
        _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0
        // Layer count is passed down to guide height-blend enable/disable, due
        // to the fact that heigh-based blend will be broken with multipass.
        [HideInInspector] [PerRendererData] _NumLayersCount ("Total Layer Count", Float) = 1.0
    
        // set by terrain engine
        [HideInInspector] _Control("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat3("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Splat2("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Splat1("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Splat0("Layer 0 (R)", 2D) = "grey" {}
        [HideInInspector] _Normal3("Normal 3 (A)", 2D) = "bump" {}
        [HideInInspector] _Normal2("Normal 2 (B)", 2D) = "bump" {}
        [HideInInspector] _Normal1("Normal 1 (G)", 2D) = "bump" {}
        [HideInInspector] _Normal0("Normal 0 (R)", 2D) = "bump" {}
        [HideInInspector] _Mask3("Mask 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Mask2("Mask 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Mask1("Mask 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Mask0("Mask 0 (R)", 2D) = "grey" {}
        [HideInInspector] _Metallic0("Metallic 0", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Metallic1("Metallic 1", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Metallic2("Metallic 2", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Metallic3("Metallic 3", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Smoothness0("Smoothness 0", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness1("Smoothness 1", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness2("Smoothness 2", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness3("Smoothness 3", Range(0.0, 1.0)) = 0.5

        // used in fallback on old cards & base map
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)
		
		[HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {} 

        [ToggleUI] _EnableInstancedPerPixelNormal("Enable Instanced per-pixel normal", Float) = 1.0
    }

	HLSLINCLUDE
	    #pragma multi_compile __ _ALPHATEST_ON
	ENDHLSL 

    SubShader
    {
        Tags{ "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "InfinityPipeline" "IgnoreProjector" = "False" }

        Pass
        {
			Name "ForwardPass"
			Tags { "LightMode" = "ForwardPlus" }
			ZTest LEqual ZWrite On Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma exclude_renderers d3d11_9x
			#pragma enable_d3d11_debug_symbols
            #pragma shader_feature_local _MASKMAP
            #pragma instancing_options renderinglayer
			#pragma shader_feature_local _ _SHOWRVTMIPMAP
            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "../Private/ShaderVariable.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                half _Cutoff;
            CBUFFER_END

            CBUFFER_START(_Terrain)
                half _NormalScale0, _NormalScale1, _NormalScale2, _NormalScale3;
                half _Metallic0, _Metallic1, _Metallic2, _Metallic3;
                half _Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3;
                half4 _DiffuseRemapScale0, _DiffuseRemapScale1, _DiffuseRemapScale2, _DiffuseRemapScale3;
                half4 _MaskMapRemapOffset0, _MaskMapRemapOffset1, _MaskMapRemapOffset2, _MaskMapRemapOffset3;
                half4 _MaskMapRemapScale0, _MaskMapRemapScale1, _MaskMapRemapScale2, _MaskMapRemapScale3;

                float4 _Control_ST;
                float4 _Control_TexelSize;
                half _DiffuseHasAlpha0, _DiffuseHasAlpha1, _DiffuseHasAlpha2, _DiffuseHasAlpha3;
                half _LayerHasMask0, _LayerHasMask1, _LayerHasMask2, _LayerHasMask3;
                half4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
                half _HeightTransition;
                half _NumLayersCount;

                #ifdef UNITY_INSTANCING_ENABLED
                    float4 _TerrainHeightmapRecipSize;   // float4(1.0f/width, 1.0f/height, 1.0f/(width-1), 1.0f/(height-1))
                    float4 _TerrainHeightmapScale;       // float4(hmScale.x, hmScale.y / (float)(kMaxHeight), hmScale.z, 0.0f)
                #endif

                #ifdef SCENESELECTIONPASS
                    int _ObjectId;
                    int _PassValue;
                #endif
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Terrain)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // float4(xBase, yBase, skipScale, ~)
            UNITY_INSTANCING_BUFFER_END(Terrain)

            TEXTURE2D(_MainTex);            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MetallicTex);        SAMPLER(sampler_MetallicTex);
            TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);

            TEXTURE2D(_Control);    SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);     SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_Splat2);
            TEXTURE2D(_Splat3);

            #ifdef _NORMALMAP
                TEXTURE2D(_Normal0);     SAMPLER(sampler_Normal0);
                TEXTURE2D(_Normal1);
                TEXTURE2D(_Normal2);
                TEXTURE2D(_Normal3);
            #endif

            #ifdef _MASKMAP
                TEXTURE2D(_Mask0);      SAMPLER(sampler_Mask0);
                TEXTURE2D(_Mask1);
                TEXTURE2D(_Mask2);
                TEXTURE2D(_Mask3);
            #endif

            #if defined(UNITY_INSTANCING_ENABLED) && defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL)
                #define ENABLE_TERRAIN_PERPIXEL_NORMAL
            #endif

            #ifdef UNITY_INSTANCING_ENABLED
                TEXTURE2D(_TerrainHeightmapTexture);
                TEXTURE2D(_TerrainNormalmapTexture);
                SAMPLER(sampler_TerrainNormalmapTexture);
            #endif

            #ifdef _ALPHATEST_ON
                TEXTURE2D(_TerrainHolesTexture);
                SAMPLER(sampler_TerrainHolesTexture);

                void ClipHoles(float2 uv)
                {
                    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r;
                    clip(hole == 0.0f ? -1 : 1);
                }
            #endif

            void TerrainInstancing(inout float4 positionOS, inout float2 uv)
            {
            #ifdef UNITY_INSTANCING_ENABLED
                float2 patchVertex = positionOS.xy;
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

                float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z; // (xy + float2(xBase,yBase)) * skipScale
                float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

                positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
                positionOS.y = height * _TerrainHeightmapScale.y;
                uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
            #endif
            }

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

            Varyings vert (Attributes In)
            {
                Varyings Out;
                UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

                TerrainInstancing(In.vertex, In.uv);

                Out.uv = In.uv;
				float4 WorldPos = mul(UNITY_MATRIX_M, In.vertex);
				Out.vertex = mul(Matrix_ViewJitterProj, WorldPos);
                return Out;
            }

            float4 frag (Varyings In) : SV_Target
            {
                return 0.5;
            }
            ENDHLSL
        }
    }
}
