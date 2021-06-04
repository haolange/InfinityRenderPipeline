Shader "InfinityPipeline/Utility/DrawSystemLUT" 
{
	Properties
	{
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

		static float mPi = 3.14159265;
		#define INTEGRAL_DOMAIN_SPHERE_3D 0
		#define INTEGRAL_DOMAIN_SPHERE_2D 1
		#define INTEGRAL_DOMAIN_SPHERE_1D 2
		#define INTEGRAL_DOMAIN_FUNC INTEGRAL_DOMAIN_SPHERE_2D

		float Distance(float3 v0, float3 v1)
		{
			float3 v = v0 - v1;
			return sqrt(dot(v, v));
		}

		// in BurleyNormalizedSSSCommon.ush
		float3 mGetSearchLightDiffuseScalingFactor3D(float3 Albedo)
		{
			return 3.5 + 100.0 * pow(max(Albedo - 0.33,0.0), 4.0);
		}

		// DiffuseMeanFreePath, Albedo, RadiusInMM
		float3 EvaluateBurleyDiffusionProfile(float3 L, float3 Albedo, float Radius)
		{
			float3 S3D = mGetSearchLightDiffuseScalingFactor3D(Albedo);

			//rR(r)
			float3 D = 1 / S3D;
			float3 R = Radius / L;
			const float Inv8Pi = 1.0 / (8 * mPi);
			float3 NegRbyD = -R / D;
			return max((exp(NegRbyD) + exp(NegRbyD / 3.0)) / (D*L)*Inv8Pi, 0);
		}

		float3 CaculateBurleyV2(float offset, float2 UV, float3 albedo, float3 meanFreePathColor, float meanFreePathScale, float maxRadiusInMM, float minRadiusInMM)
		{
			float x = UV.x;
			float y = UV.y;

			// cm to mm
			meanFreePathColor *= (10.0 * meanFreePathScale);

			float CurvatureMin = 1.0 / maxRadiusInMM;
			float CurvatureMax = 1.0 / minRadiusInMM;
			float CurrentCurvature = lerp(CurvatureMin, CurvatureMax, y);
			float Radius = 1.0f / CurrentCurvature;
			
			float NdotLMin = -1.0;
			float NdotLMax = 1.0;
			float NdotL = lerp(NdotLMin, NdotLMax, x);
			float thetaNoL = acos(NdotL);

			float3 Integral = float3(0,0,0);
			float3 rgb = float3(0,0,0);

			#if INTEGRAL_DOMAIN_FUNC == INTEGRAL_DOMAIN_SPHERE_3D
				static int NumSamplesTheta = 360;
				static int NumSamplesFai = 90;
				static int NumSamplesTotal = NumSamplesTheta * NumSamplesFai;

				float ThetaScale = (2 * mPi) / NumSamplesTheta;
				float ThetaBias = 0 + 0.5 * ThetaScale;

				float FaiScale = (0.5 * mPi) / NumSamplesFai;
				float FaiBias = 0 + 0.5 * FaiScale;

				[loop]
				for(int iFai = 0;iFai < NumSamplesFai;++iFai)
				{
					float fai = iFai * FaiScale + FaiBias;
					float r = Radius * sin(fai);

					for(int iTheta = 0;iTheta < NumSamplesTheta;++iTheta)
					{
						float theta = iTheta * ThetaScale + ThetaBias;

						// ∫∫ Irradiance * D(r') * r2 * sinφ dφdθ

						// r' = dis( (0,0,R), (rcosθ, rsinθ，sqrt(R*R - r*r) )
						float3 p0 = float3(0,0,Radius);
						float3 p1 = float3(r * cos(theta), r * sin(theta), sqrt(Radius * Radius - r * r));
						float rStar = Distance(p0, p1);

						// D(r')
						//EvaluateDiffusionProfile(rStar, Dr);
						float3 Dr = EvaluateBurleyDiffusionProfile(meanFreePathColor, albedo, rStar);

						// Irradiance = N' dot L
						float3 N = float3(0,0,1);
						float3 L = float3(sin(thetaNoL), 0, cos(thetaNoL));
						float3 NStar = normalize(p1);
						float NStaroL = max(0.0f, dot(NStar, L));

						// r2 * sinφ dφdθ
						float dS = Radius * Radius * sin(fai) * ThetaScale * FaiScale;

						// ∫∫ D(r') dS
						Integral += Dr * dS;        

						// ∫∫ Irradiance * D(r') * rdrdθ
						rgb += NStaroL * Dr * dS;
					}
				}
			#endif

			#if INTEGRAL_DOMAIN_FUNC == INTEGRAL_DOMAIN_SPHERE_2D
				static int NumSamplesTheta = 720;
				static int NumSamplesRadius = 200;
				static int NumSamplesTotal = NumSamplesTheta * NumSamplesRadius;

				float RadiusScale = (Radius - 0) / NumSamplesRadius;
				float RadiusBias = 0 + 0.5 * RadiusScale;

				float ThetaScale = (2 * mPi) / NumSamplesTheta;
				float ThetaBias = 0 + 0.5 * ThetaScale;

				[loop]
				for(int iR = 0; iR < NumSamplesRadius; ++iR)
				{
					float r = iR * RadiusScale + RadiusBias;

					for(int iTheta = 0; iTheta < NumSamplesTheta; ++iTheta)
					{
						float theta = iTheta * ThetaScale + ThetaBias;

						// ∫∫ Irradiance * D(r') * rdrdθ

						// r' = dis( (0,0,R), (rcosθ, rsinθ，sqrt(R*R - r*r) )
						float3 p0 = float3(0, 0, Radius);
						float3 p1 = float3(r * cos(theta), r * sin(theta), sqrt(Radius * Radius - r * r));
						float rStar = Distance(p0, p1);

						// D(r')
						//EvaluateDiffusionProfile(rStar, Dr);
						float3 Dr = EvaluateBurleyDiffusionProfile(meanFreePathColor, albedo, rStar);

						// Irradiance = N' dot L
						float3 N = float3(0, 0, 1);
						float3 L = float3(sin(thetaNoL), 0, cos(thetaNoL));
						float3 NStar = normalize(p1);
						float NStaroL = max(0, dot(NStar, L));

						// rdrdθ
						float dS = r * ThetaScale * RadiusScale;

						// ∫∫ D(r') dS
						Integral += Dr * dS;        

						// ∫∫ Irradiance * D(r') * rdrdθ
						rgb += NStaroL * Dr * dS;
					}
				}
			#endif

			rgb /= Integral;
			/*rgb -= max(0.0, NdotL);
			rgb *= 2.0;
			rgb += 0.5;*/
			return saturate(rgb);
		}


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
			return CaculateBurleyV2(0, uv, 1, float3(1, 0.15, 0.01), 0.05, 0.15, 5);
		}

		float3 frag_Integrated_SkinShadow(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;
			uv.y = 1 - uv.y;
			return CaculateBurleyV2(0.15, uv, 1, float3(1, 0.15, 0.01), 0.05, 0.15, 5);
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

		float frag_Integrated_ProxyShadow(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;
			float coneAngle = 0.5;
			float cosPhiStep = 1 / (127);
			float sinThetaStep = 1 / (255);
			uint coneZStepCount = 64;
			uint conePhiStepCount = 64;

			float cosPhi = uv.x;
			float sinTheta = uv.y;

			float shinPhi = sqrt(1 - cosPhi * cosPhi);
			float cosTheta = sqrt(1 - sinTheta * sinTheta);

			float3 sphereDir = float3(shinPhi, cosTheta, 0);
			float cosConeAngle = cos(coneAngle);
			float zStep = (1 - cosConeAngle) / (cosConeAngle - 1);
			float conePhiStep = 6.28 / conePhiStepCount;

			int numHit = 0;
			for(uint i = 0; i < coneZStepCount; ++i)
			{
				float z = cosConeAngle + (i * zStep);
				float xy = sqrt(1 - z);
				//float xy = sqrt(1 - z * z);

				for(uint j = 0; j < conePhiStepCount; ++j)
				{
					float conePhi = j * conePhiStep;
					float3 rayDir = float3(cos(conePhi) * xy, z, sin(conePhi) * xy);
					if(dot(rayDir, sphereDir) < cosTheta)
					{
						++numHit;
					}
				}
			}

			return float(numHit) / float(coneZStepCount * conePhiStepCount);
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

		Pass 
		{
			Name "Proxy_Shadow"
			CGPROGRAM
				#pragma vertex CustomRenderTextureVertexShader
				#pragma fragment frag_Integrated_ProxyShadow
			ENDCG
		}
	}
}
