Shader "InfinityPipeline/Utility/DrawFullScreen"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "InfinityRenderPipeline" }
		
		Pass
		{
			Name"RasterCameraMotion"
			ZTest Always ZWrite Off Blend Off Cull Off
			Stencil
			{
				Ref 1
				ReadMask 1
				WriteMask 0
				Comp NotEqual
				Pass Keep
			}

			HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../Library/Common.hlsl"
			#include "../Library/ShaderVariables.hlsl"
            #include "../Library/MotionVectors.hlsl"
            #include "../Library/ScreenSpaceDepth.hlsl"
            #include "../Library/FullScreenTriangle.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			Texture2D _MainTex;

			struct Varyings
			{
				float4 vertex : SV_POSITION;
			};

			Varyings vert(uint vertexID : SV_VertexID)
			{
				Varyings o;
				o.vertex = FullScreenTrianglePosition(vertexID);
				return o;
			}

            MotionOutput frag(Varyings i)
            {
                float2 uv = i.vertex.xy / _ScreenParams.xy;
                float depth = _MainTex.Load(int3(int2(i.vertex.xy), 0)).r;
                float4 world = mul(Matrix_InvViewFlipYJitterProj, float4(uv * 2 - 1, depth, 1));
                bool sky = ScreenSpaceIsFarDepth(depth);
                if (sky)
                {
                    float4 view = mul(Matrix_InvFlipYJitterProj, float4(uv * 2 - 1, depth, 1));
                    float3 direction = Matrix_FlipYProj[3][3] != 0 ? float3(0, 0, -1) : normalize(view.xyz);
                    world = mul(Matrix_ViewToWorld, float4(direction, 0));
                }
                else
                {
                    world /= world.w;
                }
                float4 current = mul(Matrix_ViewFlipYProj, world);
                float4 previous = mul(Matrix_LastViewFlipYProj, world);
                MotionOutput output;
                if (sky && Matrix_FlipYProj[3][3] != 0)
                {
                    // Parallel sky rays have no screen-space parallax: translation and roll keep the ray in place.
                    output.velocity = 0;
                    return output;
                }
                output.velocity = SurfaceVelocity(current, previous);
                return output;
            }

			ENDHLSL
		}
        Pass
        {
            Name "RasterFinalOutput"
            ZTest Always ZWrite Off Blend Off Cull Off
            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "../Library/Common.hlsl"
            #include "../Library/OutputTransform.hlsl"
            #include "../Library/FullScreenTriangle.hlsl"
            float4 _ScaleBais;
            float4 _InfinityOutputTransfer;
            Texture2D<float4> _MainTex;
            struct Varyings { float2 uv : TEXCOORD0; float4 position : SV_POSITION; };
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.position = FullScreenTrianglePosition(vertexID);
                o.uv = FullScreenTriangleUV(vertexID) * _ScaleBais.xy + _ScaleBais.zw;
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                float4 color = InfinitySampleLevel(_MainTex,Global_bilinear_clamp_sampler,i.uv,0);
                return float4(InfinityEncodeOutput(color.rgb, (int)_InfinityOutputTransfer.x,
                    (int)_InfinityOutputTransfer.y, _InfinityOutputTransfer.z), color.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "RasterPostCombine"
            ZTest Always ZWrite Off Blend Off Cull Off
            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ INFINITY_POST_BLOOM
            #include "../Library/Common.hlsl"
            #include "../Library/PostCombine.hlsl"
            #include "../Library/OutputTransform.hlsl"
            #include "../Library/FullScreenTriangle.hlsl"
            float4 _ScaleBais;
            float4 _InfinityOutputTransfer;
            struct Varyings { float2 uv : TEXCOORD0; float4 position : SV_POSITION; };
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.position = FullScreenTrianglePosition(vertexID);
                o.uv = FullScreenTriangleUV(vertexID) * _ScaleBais.xy + _ScaleBais.zw;
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                return float4(InfinityEncodeOutput(InfinityPostCombine(i.uv), (int)_InfinityOutputTransfer.x,
                    (int)_InfinityOutputTransfer.y, _InfinityOutputTransfer.z), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "RasterUpscaleDepth"
            ZTest Always ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "../Library/Common.hlsl"
            #include "../Library/FullScreenTriangle.hlsl"
            Texture2D<float> _InfinitySourceDepth;
            struct Varyings { float2 uv : TEXCOORD0; float4 position : SV_POSITION; };
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.position = FullScreenTrianglePosition(vertexID);
                o.uv = FullScreenTriangleUV(vertexID);
                return o;
            }
            float frag(Varyings i) : SV_Depth
            {
                return InfinitySampleLevel(_InfinitySourceDepth,Global_point_clamp_sampler,i.uv,0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "RasterCopyShadowDepthTile"
            ZTest Always ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "../Library/FullScreenTriangle.hlsl"
            Texture2D<float> _InfinitySourceDepth;
            float4 _InfinityDepthCopyOffset;
            struct Varyings { float4 position : SV_POSITION; };
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.position = FullScreenTrianglePosition(vertexID);
                return o;
            }
            float frag(Varyings i) : SV_Depth
            {
                // Copy the stored depth exactly, without filtering or reversed-Z conversion.
                return _InfinitySourceDepth.Load(int3(int2(i.position.xy) + int2(_InfinityDepthCopyOffset.xy), 0));
            }
            ENDHLSL
        }
        // Skin scattering, step 1: the diffuse of each subsurface pixel into the irradiance target.
        Pass
        {
            Name "RasterSubsurfaceExtract"
            ZTest Always ZWrite Off Blend Off Cull Off
            Stencil
            {
                Ref 4
                ReadMask 4
                WriteMask 0
                Comp Equal
                Pass Keep
            }
            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "../Library/Subsurface.hlsl"
            #include "../Library/FullScreenTriangle.hlsl"
            struct Varyings { float4 position : SV_POSITION; };
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.position = FullScreenTrianglePosition(vertexID);
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                return float4(ExtractSubsurfaceIrradiance((uint2)i.position.xy), 1);
            }
            ENDHLSL
        }
        // Skin scattering, step 2: Out = t·blur(D) + Dst·(1 − t·R) over Scene Color.
        Pass
        {
            Name "RasterSubsurfaceScatter"
            ZTest Always ZWrite Off Blend One SrcAlpha Cull Off
            Stencil
            {
                Ref 4
                ReadMask 4
                WriteMask 0
                Comp Equal
                Pass Keep
            }
            HLSLPROGRAM
            #include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "../Library/Subsurface.hlsl"
            #include "../Library/FullScreenTriangle.hlsl"
            struct Varyings { float4 position : SV_POSITION; };
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.position = FullScreenTrianglePosition(vertexID);
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                return ScatterSubsurface((uint2)i.position.xy);
            }
            ENDHLSL
        }
    }
	Fallback Off
}
