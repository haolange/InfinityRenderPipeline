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
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/FullScreenTriangle.hlsl"
            float4 vert(uint vertexID : SV_VertexID) : SV_POSITION { return FullScreenTrianglePosition(vertexID); }
            float4 frag() : SV_Target { return float4(.8, .3, .1, .5); }
            ENDHLSL
        }
    }
}
