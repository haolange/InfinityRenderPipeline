Shader "InfinityPipeline/Utility/DrawFullScreen"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "InfinityRenderPipeline" }
		
		Pass
		{
			Name"DefaultFullScreen"
			ZTest Always ZWrite Off Blend Off Cull Off

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			Texture2D _MainTex;
			SamplerState sampler_MainTex;

			struct Attributes
			{
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.vertex = float4(v.vertex.x, -v.vertex.y, 0, 1);
				o.uv = (v.vertex.xy + 1) * 0.5;
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				float2 UV = i.uv.xy;
				//return GBufferB / 127.0 - 1;
				return _MainTex.SampleLevel(sampler_MainTex, UV, 0);
				//return SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_MainTex, UV, 0);
			}
			ENDHLSL
		}

		Pass
		{
			Name"SmartFullScreen"
			ZTest Always ZWrite Off Blend Off Cull Off

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			//#define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod) SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, coord2, 0, lod)
			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
            #include "../ShaderLibrary/ScreenSpaceDepth.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			
			float4 _ScaleBais;
			Texture2D _MainTex; SamplerState sampler_MainTex;

			struct Attributes
			{
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.vertex = float4(v.vertex.x, -v.vertex.y, 0, 1);
				o.uv = ((v.vertex.xy + 1) * 0.5) * _ScaleBais.xy + _ScaleBais.zw;
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				float2 uv = i.uv.xy;
				return _MainTex.SampleLevel(Global_bilinear_clamp_sampler, uv, 0);


			}
			ENDHLSL
		}

		Pass
		{
			Name"CameraMotion"
			ZTest Always ZWrite Off Blend Off Cull Off
			Stencil 
			{
				Ref 5
				comp NotEqual
				pass keep
			}

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "../ShaderLibrary/Common.hlsl"
			#include "../ShaderLibrary/GBufferPack.hlsl"
			#include "../ShaderLibrary/ShaderVariables.hlsl"
            #include "../ShaderLibrary/MotionVectors.hlsl"
            #include "../ShaderLibrary/ScreenSpaceDepth.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

			Texture2D _MainTex;

			struct Attributes
			{
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.vertex = float4(v.vertex.x, -v.vertex.y, 0, 1);
				o.uv = (v.vertex.xy + 1) * 0.5;
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
                else world /= world.w;
                float4 current = mul(Matrix_ViewFlipYProj, world);
                float4 previous = mul(Matrix_LastViewFlipYProj, world);
                MotionOutput output;
                if (sky && Matrix_FlipYProj[3][3] != 0)
                {
                    // Parallel sky rays have no screen-space parallax. Translation and
                    // roll preserve the ray; a changed viewing direction has no matching
                    // ray anywhere in the previous orthographic framebuffer.
                    float3 currentAxis = normalize(Matrix_ViewFlipYProj[2].xyz);
                    float3 previousAxis = normalize(Matrix_LastViewFlipYProj[2].xyz);
                    float3 directionDelta = currentAxis - previousAxis;
                    output.velocity = 0;
                    output.metadata = float4(depth, depth,
                        dot(directionDelta, directionDelta) <= 1e-10 ? 1 : 0, 0);
                    return output;
                }
                output.velocity = SurfaceVelocity(current, previous);
                output.metadata = SurfaceMotionMetadata(current, previous, depth);
                if (sky) output.metadata = float4(depth, depth, previous.w > 0 ? 1 : 0, 0);
                return output;
            }

			ENDHLSL
		}
    }
	Fallback Off
}
