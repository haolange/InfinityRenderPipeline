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

		float3 CaculateSkinScatter(float2 UV) {
			const float KernelWeight[6] = { 0.0064, 0.0484, 0.187, 0.567, 1.99, 7.41 };
			const float3 KernelColor[6] = { float3(0.233,0.455,0.649), float3(0.1,0.366,0.344), float3(0.118,0.198,0), float3(0.113,0.007,0.007), float3(0.358,0.004,0), float3(0.078,0,0) };

			float Beta = -3.14 / 2;
			float Theta = (1 - UV.x) * 3.14;

			float3 OutColor = 0;
			float3 TotalWeight = 0;

			[loop]
			for (int i = 0; i < 512; i++) {
				float ScatterDistance = abs(2 * (1 / (1 - UV.y)) * sin(Beta * 0.5));
				//float ScatterDistance = Beta / (1 - UV.y) / 0.75;
				float3 GaussA = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[0])) * KernelColor[0];
				float3 GaussB = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[1])) * KernelColor[1];
				float3 GaussC = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[2])) * KernelColor[2];
				float3 GaussD = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[3])) * KernelColor[3];
				float3 GaussE = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[4])) * KernelColor[4];
				float3 GaussF = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[5])) * KernelColor[5];
				float3 D = GaussA + GaussB + GaussC + GaussD + GaussE + GaussF;

				OutColor += saturate(cos(Beta + Theta)) * D;
				TotalWeight += D;
				Beta += 0.01;

				[branch]
				if (Beta == (3.14 / 2)) {
					break;
				}
			}
			return OutColor / TotalWeight;
		}

		float3 CaculateSkinShadow(float Radius, float Offset, float2 UV) {
			const float KernelWeight[6] = { 0.0064, 0.0484, 0.187, 0.567, 1.99, 7.41 };
			const float3 KernelColor[6] = { float3(0.233,0.455,0.649), float3(0.1,0.366,0.344), float3(0.118,0.198,0), float3(0.113,0.007,0.007), float3(0.358,0.004,0), float3(0.078,0,0) };

			float Beta = -3.14 / 2;
			float Theta = (Offset - UV.x) * 3.14;

			float3 OutColor = 0;
			float3 TotalWeight = 0;

			[loop]
			for (int i = 0; i < 512; i++) {
				float ScatterDistance = Beta / (1 - UV.y) / Radius;
				float3 GaussA = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[0])) * KernelColor[0];
				float3 GaussB = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[1])) * KernelColor[1];
				float3 GaussC = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[2])) * KernelColor[2];
				float3 GaussD = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[3])) * KernelColor[3];
				float3 GaussE = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[4])) * KernelColor[4];
				float3 GaussF = exp(-ScatterDistance * ScatterDistance / (2 * KernelWeight[5])) * KernelColor[5];
				float3 D = GaussA + GaussB + GaussC + GaussD + GaussE + GaussF;

				OutColor += saturate(UV.x + Beta + Offset) * D;
				TotalWeight += D;
				Beta += 0.01;

				[branch]
				if (Beta == (3.14 / 2)) {
					break;
				}
			}
			return OutColor / TotalWeight;
		}

		float frag_Integrated_DiffuseGF(v2f_customrendertexture i) : SV_Target{
			float2 uv = i.localTexcoord.xy;

			float DiffuseD = IBL_Defualt_DiffuseIntegrated(uv.x, uv.y);
			return DiffuseD;
		}

		float2 frag_Integrated_SpecularGF(v2f_customrendertexture i) : SV_Target{
			float2 uv = i.localTexcoord.xy;

			float2 ReflectionGF = IBL_Defualt_SpecularIntegrated(uv.x, uv.y);
			return ReflectionGF;
		}

		float2 frag_Integrated_ClothGF(v2f_customrendertexture i) : SV_Target{
			float2 uv = i.localTexcoord.xy;

			float2 ReflectionGF = IBL_Ashikhmin_SpecularIntegrated(uv.x, uv.y);
			return ReflectionGF;
		}

		float3 frag_Integrated_SkinScatter(v2f_customrendertexture i) : SV_Target{
			float2 uv = i.localTexcoord.xy;
			uv.y = 1 - uv.y;

			float3 SkinScatter = CaculateSkinScatter(uv);
			SkinScatter = ACESToneMapping(SkinScatter * 1);
			//SkinScatter = max(0, SkinScatter - 0.004);
			//SkinScatter = (SkinScatter * (6.2 * SkinScatter + 0.5)) / (SkinScatter * (6.2 * SkinScatter + 1.7) + 0.06);
			return saturate(SkinScatter);
		}

		float3 frag_Integrated_SkinShadow(v2f_customrendertexture i) : SV_Target{
			float2 uv = i.localTexcoord.xy;
			uv.y = 1 - uv.y;

			float3 SkinShadow = CaculateSkinShadow(0.2, -0.65, uv);
			SkinShadow = ACESToneMapping(SkinShadow * 2.5);
			//SkinShadow = max(0, SkinShadow - 0.004);
			//SkinShadow = (SkinShadow * (6.2 * SkinShadow + 0.5)) / (SkinShadow * (6.2 * SkinShadow + 1.7) + 0.06);
			return saturate(SkinShadow);
		}

		float3 frag_Prefilter_Diffuse(v2f_customrendertexture i) : SV_Target{
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
		SubShader {
		Pass {
			Name "PBR_DiffuseGF"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_DiffuseGF
			ENDCG
		}

		Pass {
			Name "PBR_SpecularGF"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_SpecularGF
			ENDCG
		}

		Pass {
			Name "PBR_ClothGF"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_ClothGF
			ENDCG
		}

		Pass {
			Name "Skin_Scatter"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_SkinScatter
			ENDCG
		}

		Pass {
			Name "Skin_Shadow"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_SkinShadow
			ENDCG
		}

		Pass {
			Name "Prefilter_Diffuse"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Prefilter_Diffuse
			ENDCG
		}
	}
}
