Shader "InfinityPipeline/Utility/DrawSystemLUT" {
	Properties{
		[Header(Cubemap)]
		_Cubemap("Cubemap", Cube) = "black" {}
	}
		CGINCLUDE
		#include "UnityCustomRenderTexture.cginc"
		#include "../Private/Random.hlsl"
		#include "../Private/SphericalHarmonic.hlsl"
		#include "../Private/ImageBasedLighting.hlsl"

		TextureCube _Cubemap;
		SamplerState sampler_Cubemap;

		float3 ACESToneMapping(float3 color)
		{
			const float A = 2.51f;
			const float B = 0.03f;
			const float C = 2.43f;
			const float D = 0.59f;
			const float E = 0.14f;
			return (color * (A * color + B)) / (color * (C * color + D) + E);
		}

		float3 CaculateSkinScatter(float2 uv, float offset, float radius) 
		{
			const float KernelWeight[6] = { 0.0064, 0.0484, 0.187, 0.567, 1.99, 7.41 };
			const float3 KernelColor[6] = { float3(0.233,0.455,0.649), float3(0.1,0.366,0.344), float3(0.118,0.198,0), float3(0.113,0.007,0.007), float3(0.358,0.004,0), float3(0.078,0,0) };

			uv.x = 1 - uv.x;
			float Theta = (offset - uv.x) * 3.14;

			float3 A = 0;
			float3 B = 0;
			float x = -3.14 / 2;

			for (int i = 0; i < 1024; ++i)
			{
				float dis = abs(2 * (1 / (1 - uv.y) * radius) * sin(x * 0.5));
				float3 Guss0 = exp(-dis * dis / (2 * KernelWeight[0])) * KernelColor[0];
				float3 Guss1 = exp(-dis * dis / (2 * KernelWeight[1])) * KernelColor[1];
				float3 Guss2 = exp(-dis * dis / (2 * KernelWeight[2])) * KernelColor[2];
				float3 Guss3 = exp(-dis * dis / (2 * KernelWeight[3])) * KernelColor[3];
				float3 Guss4 = exp(-dis * dis / (2 * KernelWeight[4])) * KernelColor[4];
				float3 Guss5 = exp(-dis * dis / (2 * KernelWeight[5])) * KernelColor[5];
				float3 D = Guss0 + Guss1 + Guss2 + Guss3 + Guss4 + Guss5;

				A += saturate(cos(x + Theta)) * D;
				B += D;
				x += 0.01;

				if (x == (3.14 / 2))
				{
					break;
				}
			}
			float3 result = A / B;
			return result;
		}

		float3 SeparableSSS_Gaussian(float variance, float r, float3 FalloffColor)
		{
			float3 Ret;

			/**
			* We use a falloff to modulate the shape of the profile. Big falloffs
			* spreads the shape making it wider, while small falloffs make it
			* narrower.
			*/
			for (int i = 0; i < 3; i++)
			{
				float rr = r / (0.001 + FalloffColor[i]);
				Ret[i] = exp((-(rr * rr)) / (2.0 * variance)) / (2.0 * 3.14 * variance);
			}

			return Ret;
		}

		float3 SeparableSSS_Profile(float r, float3 FalloffColor)
		{
			/**
			* We used the red channel of the original skin profile defined in
			* [d'Eon07] for all three channels. We noticed it can be used for green
			* and blue channels (scaled using the falloff parameter) without
			* introducing noticeable differences and allowing for total control over
			* the profile. For example, it allows to create blue SSS gradients, which
			* could be useful in case of rendering blue creatures.
			*/
			// first parameter is variance in mm^2
			return  // 0.233f * SeparableSSS_Gaussian(0.0064f, r, FalloffColor) + /* We consider this one to be directly bounced light, accounted by the strength parameter (see @STRENGTH) */
				0.100 * SeparableSSS_Gaussian(0.0484, r, FalloffColor) +
				0.118 * SeparableSSS_Gaussian(0.187, r, FalloffColor) +
				0.113 * SeparableSSS_Gaussian(0.567, r, FalloffColor) +
				0.358 * SeparableSSS_Gaussian(1.99, r, FalloffColor) +
				0.078 * SeparableSSS_Gaussian(7.41, r, FalloffColor);
		}

		float3 CaculateSeperable(float2 uv, float3 scatterColor, float scatterRadius) 
		{
			uv.x = 1 - uv.x;
			float Theta = (uv.x) * 3.14;

			float3 A = 0;
			float3 B = 0;
			float x = -3.14 / 2;
			for (int i = 0; i < 1000; i++)
			{
				float step = 0.001;

				float dis = abs(2 * (1 / (1 - uv.y) * scatterRadius) * sin(x * 0.5));
				float3 D = SeparableSSS_Profile(dis, scatterColor);

				A += saturate(cos(x + Theta)) * D;
				B += D;
				x += 0.01;

				if (x == (3.14 / 2))
				{
					break;
				}
			}
			float3 result = A / B;
			return result;
		}

		float Burley_ScatteringProfile(float r, float A, float S, float L)
		{   
			float D = 1 / S;
			float R = r / L;
			const float Inv8Pi = 1.0 / (8 * 3.14);
			float NegRbyD = -R / D;
			float RrDotR = A*max((exp(NegRbyD) + exp(NegRbyD / 3.0)) / (D*L)*Inv8Pi, 0.0);
			return RrDotR;
		}

		float3 Burley_ScatteringProfile(float r, float3 SurfaceAlbedo, float3 ScalingFactor, float3 DiffuseMeanFreePath)
		{  
			return float3(Burley_ScatteringProfile(r, SurfaceAlbedo.r, ScalingFactor.x, DiffuseMeanFreePath.r), Burley_ScatteringProfile(r, SurfaceAlbedo.g, ScalingFactor.y, DiffuseMeanFreePath.g), Burley_ScatteringProfile(r, SurfaceAlbedo.b, ScalingFactor.z, DiffuseMeanFreePath.b));
		}

		float GetPerpendicularScalingFactor(float SurfaceAlbedo)
		{
			return 1.85 - SurfaceAlbedo + 7 * pow(SurfaceAlbedo - 0.8, 3);
		}

		float3 GetPerpendicularScalingFactor(float3 SurfaceAlbedo)
		{
			return float3(GetPerpendicularScalingFactor(SurfaceAlbedo.r), GetPerpendicularScalingFactor(SurfaceAlbedo.g), GetPerpendicularScalingFactor(SurfaceAlbedo.b));
		}

		float GetDiffuseSurfaceScalingFactor(float SurfaceAlbedo)
		{
			return 1.9 - SurfaceAlbedo + 3.5 * pow(SurfaceAlbedo - 0.8, 2);
		}

		float3 GetDiffuseSurfaceScalingFactor(float3 SurfaceAlbedo)
		{
			return float3(GetDiffuseSurfaceScalingFactor(SurfaceAlbedo.r), GetDiffuseSurfaceScalingFactor(SurfaceAlbedo.g), GetDiffuseSurfaceScalingFactor(SurfaceAlbedo.b));
		}

		float GetSearchLightDiffuseScalingFactor(float SurfaceAlbedo)
		{
			return 3.5 + 100 * pow(SurfaceAlbedo - 0.33, 4);
		}

		float3 GetSearchLightDiffuseScalingFactor(float3 SurfaceAlbedo)
		{
			return float3(GetSearchLightDiffuseScalingFactor(SurfaceAlbedo.r), GetSearchLightDiffuseScalingFactor(SurfaceAlbedo.g), GetSearchLightDiffuseScalingFactor(SurfaceAlbedo.b));
		}

		float3 CaculateBurley(float2 uv, float3 SurfaceAlbedo, float3 DiffuseMeanFreePath, float ScatterRadius)
		{
			uv.x = 1 - uv.x;
			float Theta = (uv.x) * 3.14;
			float3 ScalingFactor = GetSearchLightDiffuseScalingFactor(SurfaceAlbedo);

			float3 A = 0;
			float3 B = 0;
			float x = -3.14 / 2;
			for (int i = 0; i < 1024; ++i)
			{
				float step = 0.001;

				float dis = abs(2 * (1 / (1 - uv.y) * ScatterRadius) * sin(x * 0.5));
				float3 D = Burley_ScatteringProfile(dis, SurfaceAlbedo, ScalingFactor, DiffuseMeanFreePath);

				A += saturate(cos(x + Theta)) * D;
				B += D;
				x += 0.01;

				if (x == (3.14 / 2))
				{
					break;
				}
			}
			float3 result = A / B;
			return result;
		}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		float frag_Integrated_DiffuseGF(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;

			float DiffuseD = IBL_Defualt_DiffuseIntegrated(uv.x, uv.y);
			return DiffuseD;
		}

		float2 frag_Integrated_SpecularGF(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;

			float2 ReflectionGF = IBL_Defualt_SpecularIntegrated(uv.x, uv.y);
			return ReflectionGF;
		}

		float2 frag_Integrated_ClothGF(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;

			float2 ReflectionGF = IBL_Ashikhmin_SpecularIntegrated(uv.x, uv.y);
			return ReflectionGF;
		}

		float3 frag_Integrated_SkinScatter(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;
			uv.y = 1 - uv.y;

			float3 SkinScatter = CaculateSkinScatter(uv, 0, 2.5);
			//float3 SkinScatter = CaculateSeperable(uv, float3(0.85, 0.28, 0.1), 1);
			//float3 SkinScatter = CaculateBurley(uv, 1, float3(1, 0.15, 0.01), 0.2);
			//SkinScatter = ACESToneMapping(SkinScatter);
			return saturate(SkinScatter);
		}

		float3 frag_Integrated_SkinShadow(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;
			uv.y = 1 - uv.y;

			float3 SkinShadow = CaculateSkinScatter(uv, -0.125, 2.5);
			SkinShadow = ACESToneMapping(SkinShadow);
			//SkinShadow = max(0, SkinShadow - 0.004);
			//SkinShadow = (SkinShadow * (6.2 * SkinShadow + 0.5)) / (SkinShadow * (6.2 * SkinShadow + 1.7) + 0.06);
			return saturate(SkinShadow);
		}

		float3 frag_Prefilter_Diffuse(v2f_customrendertexture i) : SV_Target
		{
			float2 UV = i.localTexcoord.xy;

			const uint NumSample = 256;
			uint2 Random = Rand3DPCG16(uint3(UV * float2(512, 256), 1)).xy;

			float3 SphereCoord = UniformSampleSphere(UV).xyz;
			float3x3 LocalToWorld = GetTangentBasis(SphereCoord);

			float3 Radiance = 0;

			[loop]
			for (uint i = 0; i < NumSample; ++i) 
			{
				float2 E = Hammersley16(i, (uint)NumSample, Random);
				float3 Dir_TS = CosineSampleHemisphere(E).xyz;
				float3 Dir_WS = mul(Dir_TS, LocalToWorld);
				Radiance += TextureCubeSampleLevel(_Cubemap, sampler_Cubemap, float3(Dir_WS.x, -Dir_WS.z, Dir_WS.y), 5).xyz * saturate(Dir_TS.b);
			}
			Radiance /= (float)NumSample;
			
			return Radiance;

			/*SH2Table SHTable = InitSH2Table(SphereCoord);
			SH2Basis SHBasis = InitSH2Basis(SHTable, Radiance);

			float3 Irradiance = 0;
			for(int j = 0; j < 9; j++)
			{
				Irradiance += SHTable.Coefficients[j] * SHBasis.Basis[j];
			}
			return Irradiance;*/
		}

		ENDCG
		SubShader 
		{
		Pass 
		{
			Name "PBR_DiffuseGF"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_DiffuseGF
			ENDCG
		}

		Pass 
		{
			Name "PBR_SpecularGF"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_SpecularGF
			ENDCG
		}

		Pass 
		{
			Name "PBR_ClothGF"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_ClothGF
			ENDCG
		}

		Pass 
		{
			Name "Skin_Scatter"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_SkinScatter
			ENDCG
		}

		Pass 
		{
			Name "Skin_Shadow"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_SkinShadow
			ENDCG
		}

		Pass 
		{
			Name "Prefilter_Diffuse"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Prefilter_Diffuse
			ENDCG
		}
	}
}
