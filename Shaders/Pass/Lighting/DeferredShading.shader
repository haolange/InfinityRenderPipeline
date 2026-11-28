Shader "InfinityPipeline/DeferredShading"
{
    SubShader
    {
        Tags { "RenderPipeline" = "InfinityRenderPipeline" }
        Pass
        {
            Name "RasterDeferredShading"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One SrcAlpha
            Stencil
            {
                Ref 2
                ReadMask 2
                WriteMask 0
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ INFINITY_SIDEBAND
            #pragma multi_compile _ DEFERRED_AO
            #pragma multi_compile _ DEFERRED_SSGI
            #pragma multi_compile _ DEFERRED_SSR
            #pragma multi_compile _ DEFERRED_CONTACT_SHADOW
            #pragma multi_compile _ DEFERRED_RASTER_REFLECTION
            #pragma multi_compile _ DEFERRED_ANALYTIC_SHADOW
            #pragma multi_compile _ INFINITY_CASCADE_SHADOW_MOMENTS
            #pragma multi_compile _ INFINITY_LOCAL_SHADOW_MOMENTS
            #pragma multi_compile _ INFINITY_LIGHT_COOKIES
            #pragma multi_compile _ INFINITY_REFLECTION_PROBES

            #include "../../Library/Common.hlsl"
            #include "../../Library/FullScreenTriangle.hlsl"
            #include "../../Library/GBufferPack.hlsl"
            #include "../../Library/Sideband.hlsl"
            #include "../../Library/BSDF.hlsl"
            #include "../../Library/Lighting.hlsl"
            #include "../../Library/ImageBasedLighting.hlsl"
            #include "../../Library/IndirectComposition.hlsl"
            #include "../../Library/SurfaceLighting.hlsl"
            #include "../../Library/ViewUniform.hlsl"

            Texture2DArray<float4> _AtmosphereGGXPrefilter;

            // GPU-RECORD: CBV_DeferredShadingParameters Shaders/Pass/Lighting/DeferredShading.shader
            cbuffer CBV_DeferredShadingParameters
            {
                float4 DeferredShading_Resolution;
                float _AtmosphereIBLMaxMip;
                int DeferredShading_HasRenderingLayers;
                int DeferredShading_Padding0;
                int DeferredShading_Padding1;
            };

            Texture2D SRV_GBufferTextureA;
            Texture2D SRV_GBufferTextureB;
            Texture2D<float> SRV_DepthTexture;
#if defined(INFINITY_SIDEBAND)
            Texture2D<float4> SRV_Sideband;
#endif
#if defined(DEFERRED_AO)
            Texture2D<float> SRV_OcclusionTexture;
#endif
#if defined(DEFERRED_SSGI)
            Texture2D<float4> SRV_SSGITexture;
            Texture2D<float> SRV_SSGIConfidenceTexture;
#endif
#if defined(DEFERRED_SSR)
            #include "../../Library/ScreenSpaceRadiance.hlsl"
            Texture2DArray<float2> SRV_SSRTexture;
#endif
#if defined(DEFERRED_RASTER_REFLECTION)
            Texture2D<float4> SRV_RasterReflection;
#endif

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = FullScreenTrianglePosition(vertexID);
                return output;
            }

            // Blend One SrcAlpha against the geometry indirect diffuse: rgb is direct plus environment plus SSGI,
            // alpha is the factor that keeps the occluded indirect term.
            float4 frag(Varyings input) : SV_Target
            {
                uint2 pixelCoord = (uint2)input.positionCS.xy;
                float2 screenUV = (pixelCoord + 0.5) * DeferredShading_Resolution.zw;
                float rawDepth = SRV_DepthTexture[pixelCoord];
                float3 ndcPos = GetNDCPos(screenUV, rawDepth);
                float3 worldPos = GetWorldSpacePos(ndcPos, Matrix_InvViewFlipYJitterProj);
                float3 viewDir = normalize(ViewPositionWS() - worldPos);
                float viewDepth = -GetViewSpacePos(ndcPos, Matrix_InvFlipYJitterProj).z;

                SurfaceData surface;
                DecodeSurface(pixelCoord, SRV_GBufferTextureA, SRV_GBufferTextureB, surface);
#if defined(INFINITY_SIDEBAND)
                SidebandData sideband = UnpackSideband(SRV_Sideband[pixelCoord]);
#else
                SidebandData sideband = DefaultSideband();
#endif
                uint lightChannels = DeferredShading_HasRenderingLayers != 0 ? sideband.LightChannels : SIDEBAND_LIGHT_CHANNELS_ALL;
                MicrofaceContext microfaceCtx = InitMicrofaceContext(surface.Specular, surface.Roughness, surface.Metallic, surface.Albedo);

#if defined(DEFERRED_AO)
                float occlusion = InfinitySampleLevel(SRV_OcclusionTexture, Global_point_clamp_sampler, screenUV, 0);
#else
                float occlusion = 1.0;
#endif
#if defined(DEFERRED_ANALYTIC_SHADOW)
                occlusion *= EvaluateAnalyticIndirectOcclusion(pixelCoord, viewDepth, worldPos, surface.Normal, lightChannels);
#endif

                float3 directDiffuse;
                float3 direct = EvaluateSurfaceLights(worldPos, surface.Normal, viewDir, microfaceCtx, sideband.BakedVisibility, viewDepth, lightChannels, pixelCoord, directDiffuse);
                float3 reflectionWS = reflect(-viewDir, surface.Normal);
                float3 sky = SampleGGXCubemapArray(_AtmosphereGGXPrefilter, reflectionWS, microfaceCtx.RoughnessClamp, _AtmosphereIBLMaxMip);
#if defined(DEFERRED_RASTER_REFLECTION)
                float4 rasterReflection = SRV_RasterReflection[pixelCoord];
#else
                float4 rasterReflection = 0;
#endif
                float3 environmentBRDF = EnvBRDFApprox(microfaceCtx.SpecularColor, microfaceCtx.RoughnessClamp, saturate(dot(surface.Normal, viewDir))).rgb;
#if defined(DEFERRED_SSGI)
                float4 ssgi = float4(SRV_SSGITexture[pixelCoord].rgb, SRV_SSGIConfidenceTexture[pixelCoord]);
#else
                float4 ssgi = 0;
#endif
                float alpha = occlusion * (1.0 - saturate(ssgi.a));
                float3 environment = ComposeSpecularEnvironment(rasterReflection, worldPos, reflectionWS, microfaceCtx.RoughnessClamp, pixelCoord, viewDepth, sky)
                    * environmentBRDF * occlusion;
#if defined(DEFERRED_SSR)
                environment = ReplaceEnvironmentSpecular(environment, environmentBRDF, LoadScreenSpaceRadiance(SRV_SSRTexture, pixelCoord, 0));
#endif
                float3 rgb = max(0, direct + environment + microfaceCtx.AlbedoColor * ssgi.rgb);
                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
