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
        [HideInInspector][Gamma] _Metallic0("Metallic 0", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic1("Metallic 1", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic2("Metallic 2", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic3("Metallic 3", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Smoothness0("Smoothness 0", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness1("Smoothness 1", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness2("Smoothness 2", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness3("Smoothness 3", Range(0.0, 1.0)) = 0.5

        // used in fallback on old cards & base map
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)
        [HideInInspector] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
		
		[HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {} 

        [Toggle (_TERRAIN_INSTANCED_PERPIXEL_NORMAL)] EnableInstancedPerPixelNormal ("Enable Instanced per-pixel normal", Range(0, 1)) = 0
    }

	HLSLINCLUDE
	    #pragma multi_compile __ _ALPHATEST_ON
	ENDHLSL 
	
    SubShader
    {
        Tags{ "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "InfinityRenderPipeline" "IgnoreProjector" = "false" "TerrainCompatible" = "True"}

        Pass
        {
			Name "GBufferPass"
			Tags { "LightMode" = "GBufferPass" }
            
			ZTest LEqual ZWrite On Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SplatmapVert
            #pragma fragment DeferredFragment
            
            #pragma multi_compile_instancing
            #pragma multi_compile _ _DBUFFER
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            //#pragma enable_d3d11_debug_symbols
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            
            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            #include "TerrainLitInclude.hlsl"
            ENDHLSL
        }

        Pass
        {
			Name "ForwardPass"
			Tags { "LightMode" = "ForwardPass" }
            
			ZTest Equal ZWrite On Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SplatmapVert
            #pragma fragment ForwardFragment
            
            #pragma multi_compile_instancing
            //#pragma enable_d3d11_debug_symbols
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
    
            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            #include "TerrainLitInclude.hlsl"
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
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "TerrainLitInclude.hlsl"
            ENDHLSL
        }

        // The lightmapper renders terrain albedo and emission through Meta.
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainMetaVertex
            #pragma fragment TerrainMetaFragment

            #include "TerrainLitInclude.hlsl"
            ENDHLSL
        }
    }

    Dependency "BaseMapShader" = "Hidden/InfinityPipeline/TerrainLitBase"
    Dependency "BaseMapGenShader" = "Hidden/InfinityPipeline/TerrainLitBasemapGen"
    Dependency "AddPassShader" = "Hidden/InfinityPipeline/TerrainLitAdd"
}
