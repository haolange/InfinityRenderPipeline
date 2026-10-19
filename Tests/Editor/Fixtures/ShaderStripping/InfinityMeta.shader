Shader "Hidden/InfinityTests/ShaderStripping/InfinityMeta"
{
    SubShader
    {
        Tags { "RenderPipeline" = "InfinityRenderPipeline" }
        Pass
        {
            Name "Fixture"
            Tags { "LightMode" = "Meta" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(float4 position : POSITION) : SV_POSITION { return position; }
            float4 Frag() : SV_Target { return float4(1, 1, 1, 1); }
            ENDHLSL
        }
    }
}
