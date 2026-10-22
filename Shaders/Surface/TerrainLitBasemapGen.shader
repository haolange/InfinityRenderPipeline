Shader "Hidden/InfinityPipeline/TerrainLitBasemapGen"
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
        _DstBlend("Destination blend", Float) = 0
    }
    SubShader
    {
        HLSLINCLUDE
        #pragma target 4.5
        #define _TERRAIN_BASEMAP_GEN
        #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
        #pragma shader_feature_local _MASKMAP
        #include "TerrainLitInclude.hlsl"
        Varyings BasemapVertex(Attributes v)
        {
            Varyings o = (Varyings)0;
            // Terrain basemap generation uses Unity's native projection, outside the SRP camera pass.
            o.clipPos = mul(unity_MatrixVP, v.positionOS);
            o.uvMainAndLM.xy = v.texcoord;
            o.uvSplat01.xy = TRANSFORM_TEX(v.texcoord, _Splat0);
            o.uvSplat01.zw = TRANSFORM_TEX(v.texcoord, _Splat1);
            o.uvSplat23.xy = TRANSFORM_TEX(v.texcoord, _Splat2);
            o.uvSplat23.zw = TRANSFORM_TEX(v.texcoord, _Splat3);
            return o;
        }
        void BasemapSurface(Varyings i, out half4 diffuse)
        {
            half4 masks[4];
            ComputeMasks(masks, half4(_LayerHasMask0, _LayerHasMask1, _LayerHasMask2, _LayerHasMask3), i);
            float2 uv = (i.uvMainAndLM.xy * (_Control_TexelSize.zw - 1) + .5) * _Control_TexelSize.xy;
            half4 control = SAMPLE_TEXTURE2D(_Control, sampler_Control, uv);
            #if defined(_TERRAIN_BLEND_HEIGHT)
            if (_NumLayersCount <= 4) HeightBasedSplatModify(control, masks);
            #endif
            half weight; half4 smoothness; half3 normal = half3(0, 0, 1);
            SplatmapMix(i.uvMainAndLM, i.uvSplat01, i.uvSplat23, control, weight, diffuse, smoothness, normal);
            diffuse.a = dot(control, smoothness);
        }
        half4 BasemapColor(Varyings i) : SV_Target { half4 diffuse; BasemapSurface(i, diffuse); return diffuse; }
        ENDHLSL
        Pass
        {
            Tags { "Name" = "_MainTex" "Format" = "ARGB32" "Size" = "1" }
            ZTest Always Cull Off ZWrite Off Blend One [_DstBlend]
            HLSLPROGRAM
            #pragma vertex BasemapVertex
            #pragma fragment BasemapColor
            ENDHLSL
        }
    }
}
