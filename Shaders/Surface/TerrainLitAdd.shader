Shader "Hidden/InfinityPipeline/TerrainLitAdd"
{
    Properties
    {
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
		
		[HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}
    }

	HLSLINCLUDE
	    #pragma multi_compile __ _ALPHATEST_ON
	ENDHLSL 
	
    SubShader
    {
        Tags{ "Queue" = "Geometry-99" "RenderType" = "Opaque" "RenderPipeline" = "InfinityPipeline" "IgnoreProjector" = "false" }

        Pass
        {
			Name "GBufferPass"
			Tags { "LightMode" = "GBufferPass" }

            Blend One One
			ZTest Equal ZWrite Off Cull Back

            HLSLPROGRAM
            #pragma vertex SplatmapVert
            #pragma fragment DeferredFragment
            
            #pragma multi_compile_instancing
            //#pragma enable_d3d11_debug_symbols
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL

            #define TERRAIN_SPLAT_ADDPASS
    
            #include "TerrainLitInclude.hlsl"
            ENDHLSL
        }

        Pass
        {
			Name "TerrainAdd"
			Tags { "LightMode" = "ForwardPass" }

            Blend One One
			ZTest Equal ZWrite Off Cull Back

            HLSLPROGRAM
            #pragma vertex SplatmapVert
            #pragma fragment ForwardFragment
            
            #pragma multi_compile_instancing
            //#pragma enable_d3d11_debug_symbols
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL

            #define TERRAIN_SPLAT_ADDPASS
    
            #include "TerrainLitInclude.hlsl"
            ENDHLSL
        }
    }
}
