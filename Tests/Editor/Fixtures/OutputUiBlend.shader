Shader "Hidden/InfinityTests/OutputUiBlend"
{
    SubShader
    {
        Tags { "RenderPipeline" = "InfinityRenderPipeline" }
        Pass
        {
            Name "RasterUIBlend"
            ZTest Always ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.x, -vertex.y, 0, 1); }
            float4 frag() : SV_Target { return float4(.8, .3, .1, .5); }
            ENDHLSL
        }
    }
}
