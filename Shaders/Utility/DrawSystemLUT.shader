Shader "InfinityPipeline/Utility/DrawSystemLUT" 
{
	Properties
	{
		[Header(Cubemap)]
		_Cubemap("Cubemap", Cube) = "black" {}
		
		[Header(Subsurface)]
        _Albedo("ScatterColor", Color) = (1, 1, 1, 1)
		_ScatterColor("FalloffColor", Color) = (1, 0.15, 0.01, 1)
        _ScatterScale("ScatterScale", Range(0, 1)) = 0.05
		_ScatterRadiuMin("ScatterRadiuMin", Float) = 0.15
		_ScatterRadiuMax("ScatterRadiuMax", Float) = 5
		_DistributeCorrect("DistributeCorrect", Vector) = (0, 1, 0, 1)
	}

	CGINCLUDE
		#include "../ShaderLibrary/Random.hlsl"
		#include "UnityCustomRenderTexture.cginc"
		#include "../ShaderLibrary/SphericalHarmonic.hlsl"
		#include "../ShaderLibrary/ImageBasedLighting.hlsl"

        float _ScatterScale, _ScatterRadiuMin, _ScatterRadiuMax;
        float4 _Albedo, _ScatterColor, _DistributeCorrect;

		TextureCube _Cubemap;
		SamplerState sampler_Cubemap;

		static float mPi = 3.14159265;
		#define INTEGRAL_DOMAIN_SPHERE_3D 0
		#define INTEGRAL_DOMAIN_SPHERE_2D 1
		#define INTEGRAL_DOMAIN_SPHERE_1D 2
		#define INTEGRAL_DOMAIN_FUNC INTEGRAL_DOMAIN_SPHERE_3D

		#define PREINTEGRATEDSSS_SHADOWLUT_RANGEMUL 0.2

		float GetShadowRangeInCMFromYCoord(float yCoord)
		{
			return (1.0 / yCoord - 1) / PREINTEGRATEDSSS_SHADOWLUT_RANGEMUL;
		}

		float GetYCoordFromShadowRangeInCM(float ShadowRangeInCM)
		{
			return 1.0 / (ShadowRangeInCM * PREINTEGRATEDSSS_SHADOWLUT_RANGEMUL + 1.0);
		}

		float Distance(float3 v0, float3 v1)
		{
			float3 v = v0 - v1;
			return sqrt(dot(v, v));
		}

		float3 ToneMapping(float3 color)
		{
			float3 x = max(0, color - 0.004);
			return  (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
		}

		float3 ACESToneMapping(float3 color)
		{
			const float A = 2.51f;
			const float B = 0.03f;
			const float C = 2.43f;
			const float D = 0.59f;
			const float E = 0.14f;
			return (color * (A * color + B)) / (color * (C * color + D) + E);
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

		float3 IntegralBurley2DTo1D(float2 UV, float3 albedo, float3 meanFreePathColor, float meanFreePathScale, uint width, float halfRangeInMM)
		{
			// cm to mm
			meanFreePathColor *= (10.0 * meanFreePathScale);

			float DistMin = -halfRangeInMM;
			float DistMax = halfRangeInMM;
			float DistX = lerp(DistMin, DistMax, UV.x);

			float3 rgb = 0;
			float x2 = DistX * DistX;
			
			for(uint iY = 0;iY < width;++iY)
			{
				float Y = (iY + 0.5) / (float)width;
				float DistY = lerp(DistMin, DistMax, Y);
				float y2 = DistY * DistY;
				float Dist = sqrt(x2 + y2);
				float3 BurleyValue = EvaluateBurleyDiffusionProfile(meanFreePathColor, albedo, Dist);

				rgb += BurleyValue;
			}

			return rgb;
		}

		float3 BakeBurleySSSLUT(float2 UV, float3 albedo, float3 meanFreePathColor, float meanFreePathScale, float maxRadiusInMM, float minRadiusInMM)
		{
			float x = UV.x;
			float y = UV.y;

			// cm to mm
			meanFreePathColor *= (10 * meanFreePathScale);

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

		float3 BakeBurleyShadowSSSLUT_NoL_ShadowValue(float2 UV, float3 albedo, float3 meanFreePathColor, float meanFreePathScale, float halfRangeInMM, float maxScatterDistInMM)
		{
			// cm to mm
			meanFreePathColor *= (10.0 * meanFreePathScale);

			// Position is the x of projection space
			float Position = lerp(-halfRangeInMM, halfRangeInMM, UV.x);
			float Shadow = (Position>0)?1:0;
			float PosMin = Position - halfRangeInMM;
			float PosMax = Position + halfRangeInMM;

			float NdotLMin = 0.0;
			float NdotLMax = 1.0;
			float NdotL = lerp(NdotLMin, NdotLMax, UV.y);

			float3 Integral = float3(0,0,0);
			float3 rgb = float3(0,0,0);

			// 1D integral
			static int NumSamplesDist = 1024;

			float PosScale = (PosMax - PosMin) / NumSamplesDist;
			float PosBias = PosMin + 0.5 * PosScale;
			float dx = PosScale;

			[loop]
			for(int iX = 0;iX < NumSamplesDist;++iX)
			{
				float PositionNew = iX * PosScale + PosBias;
				// PositionOffset = offset / costheta
				float PositionOffset = (PositionNew - Position) / max(NdotL, 0.0001);
				PositionOffset = abs(PositionOffset);

				// ∫∫ Irradiance(Position + x) * D(x) * dx

				// Irradiance = NdotL * shadow(Position + x)
				// shadow function is hard 01 function when pre integrate
				float ShadowNew = (PositionNew > 0)?1:0;
				// just as shadow, don't mul NdotL
				// float Irradiance = NdotL * ShadowNew;
				float Irradiance = 1 * ShadowNew;

				// todo, lut sample
				float3 Dr = EvaluateBurleyDiffusionProfile(meanFreePathColor, albedo, PositionOffset);

				// ∫∫ D(x) * dx
				Integral += Dr * dx;

				rgb += Irradiance * Dr * dx;
			}

			rgb /= Integral;
			// rgb -= max(0.0, NdotL * Shadow);
			// rgb *= 2.0;
			// rgb += 0.5;
			rgb = saturate(rgb);
			return max(rgb, 0.0);
		}

		float3 BakeBurleyShadowSSSLUT_ShadowRange_ShadowValue(float2 UV, float3 albedo, float3 meanFreePathColor, float meanFreePathScale, float halfRangeInMM, float maxScatterDistInMM, float shadowSSSDistributeCorrectDark, float shadowSSSDistributeCorrectBright, float shadowSSSFinalCorrectDark, float shadowSSSFinalCorrectBright)
		{
			//  _____________  x(shadow value)
			// |
			// |
			// |
			// |
			// |
			// |
			//  1 / (shadow range + 1)

			// cm to mm
			meanFreePathColor *= (10.0 * meanFreePathScale);

			float xCoord = UV.x;
			float yCoord = UV.y;

			// yCoord = 1 / (ShadowRangeInCM * RangeMul + 1)
			// ShadowRangeInCM = (1 / yCoord - 1) / RangeMul
			
			// yCoord = ShadowOffsetInMM / (ShadowRangeInMM + ShadowOffsetInMM)
			// ShadowRangeInMM = ShadowOffsetInMM / yCoord - ShadowOffsetInMM;

			// RangeMul = 0.1:      [1cm, 30cm] -> [0.333, 0.909]
			// RangeMul = 0.2:      [1cm, 30cm] -> [0.143, 0.833]
			// RangeMul = 0.3:      [1cm, 30cm] -> [0.1, 0.769]
			const float RangeMul = PREINTEGRATEDSSS_SHADOWLUT_RANGEMUL;
			const float ShadowOffsetInMM = 20;
			float DistributeCorrectDark = shadowSSSDistributeCorrectDark;
			float DistributeCorrectBright = shadowSSSDistributeCorrectBright;
			float FinalCorrectDark = shadowSSSFinalCorrectDark;
			float FinalCorrectBright = shadowSSSFinalCorrectBright;
			
			float ShadowRangeInCM = GetShadowRangeInCMFromYCoord(yCoord);
			float ShadowRangeInMM = ShadowRangeInCM * 10;

			// float ShadowRangeInMM = (ShadowOffsetInMM / yCoord) - ShadowOffsetInMM;
			//               shadowrange
			//           __________________
			//          |__________________|
			//          |    |             |
			//       shadow=0|          shadow = 1
			//   <---------- x ---------->
			//  x-L                     x+L
			// integral [x-L, x+L] scatter energy to x
			// yCoord is shadow range, xCoord is shadow value
			// x should do remap, since shadow after sss will be wider than origin shadow
			// origin x is ranged in [shadow=0, shadow=1], new x is ranged from [shadow=0 - ShadowOffsetInMM, shadow=1 + ShadowOffsetInMM]
			float Shadow0Position = -0.5 * ShadowRangeInMM;
			float xInMM = (xCoord - 0.5) * (ShadowRangeInMM + 2 * ShadowOffsetInMM);
			float LInMM = maxScatterDistInMM;
			float LeftBound = xInMM - LInMM;
			float RightBound = xInMM + LInMM;

			float3 Integral = float3(0,0,0);
			float3 rgb = float3(0,0,0);
			static int NumSamplesDist = 1024;

			float PosScale = (RightBound - LeftBound) / NumSamplesDist;
			float PosBias = LeftBound + 0.5 * PosScale;
			float dx = PosScale;

			[loop]
			for(int iX = 0;iX < NumSamplesDist;++iX)
			{
				float PositionNew = iX * PosScale + PosBias;
				float PositionOffset = abs(xInMM - PositionNew);

				// ∫∫ Shadow'(s + x) * D(x) * dx

				float ShadowNew = saturate((PositionNew - Shadow0Position) / ShadowRangeInMM);
				
				float3 Dr = EvaluateBurleyDiffusionProfile(meanFreePathColor, albedo, PositionOffset);

				// ∫∫ D(x) * dx
				Integral += Dr * dx;

				rgb += ShadowNew * Dr * dx;
			}

			rgb /= Integral;
			rgb = saturate(rgb);

			// finally lerp sss color to shadow color, trick
			float LerpFactor = abs(xCoord - 0.5) * 2;
			LerpFactor = pow(LerpFactor, (xCoord < 0.5)?DistributeCorrectDark:DistributeCorrectBright);
			LerpFactor = lerp(LerpFactor, 1, (xCoord < 0.5)?FinalCorrectDark:FinalCorrectBright);
			//rgb = lerp(rgb, xCoord, LerpFactor);

			// // 1\ luminance lerp to original
			// float3 Origin = float3(xCoord, xCoord, xCoord);
			// float LumNew = Luminance(rgb);
			// float LumOri = Luminance(Origin);
			// float LumMul = LumOri / max(0.001, LumNew);
			// rgb *= (lerp(1, LumMul, LuminanceCorrect));

			// // 2\ final lerp to original
			// rgb = lerp(rgb, Origin, FinalCorrect);

			return saturate(rgb);
		}

		static const float diffusionSigmas[] = { 0.080f, 0.220f, 0.432f, 0.753f, 1.411f, 2.722f };
		static const float diffusionWeightsR[] = { 0.233f, 0.100f, 0.118f, 0.113f, 0.358f, 0.078f };
		static const float diffusionWeightsG[] = { 0.455f, 0.336f, 0.198f, 0.007f, 0.004f, 0.000f };
		static const float diffusionWeightsB[] = { 0.649f, 0.344f, 0.000f, 0.007f, 0.000f, 0.000f };

		void EvaluateDiffusionProfile(float x, inout float3 rgb)	// x in millimeters
		{
			for (int i = 0; i < 6; ++i)
			{
				static const float rsqrtTwoPi = 0.39894228f;
				float sigma = diffusionSigmas[i];
				float gaussian = (rsqrtTwoPi / sigma) * exp(-0.5f * (x*x) / (sigma*sigma));

				rgb[0] += diffusionWeightsR[i] * gaussian;
				rgb[1] += diffusionWeightsG[i] * gaussian;
				rgb[2] += diffusionWeightsB[i] * gaussian;
			}
		}

		float3 CaculateShadowScatter(float2 samplePos, float shadowBias, float shadowScale, float shadowSharp, float resolution)
		{
			// Calculate input position relative to the shadow edge, by approximately
			// inverting the transfer function of a disc or Gaussian filter.
			float u = (samplePos.x) / resolution;
			float inputPos = (sqrt(u) - sqrt(1.0f - u)) * 0.5f + 0.5f;

			float rcpWidth = float(samplePos.y) * shadowScale + shadowBias;

			// Sample points along a line perpendicular to the shadow edge, and
			// Monte-Carlo-integrate the scattered lighting using the diffusion profile

			static const int cIter = 128;
			float3 rgb = 0;

			float iterScale = 20.0f / float(cIter);
			float iterBias = -10.0f + 0.5f * iterScale;

			for (int iIter = 0; iIter < cIter; ++iIter)
			{
				float delta = float(iIter) * iterScale + iterBias;
				float3 rgbDiffusion = 0;
				EvaluateDiffusionProfile(delta, rgbDiffusion);

				// Use smoothstep as an approximation of the transfer function of a
				// disc or Gaussian filter.
				float newPos = (inputPos + delta * rcpWidth) * shadowSharp + (-0.5f * shadowSharp + 0.5f);
				float newPosClamped = min(max(newPos, 0.0f), 1.0f);
				float newShadow = (3.0f - 2.0f * newPosClamped) * newPosClamped * newPosClamped;

				rgb[0] += newShadow * rgbDiffusion.x;
				rgb[1] += newShadow * rgbDiffusion.y;
				rgb[2] += newShadow * rgbDiffusion.z;
			}

			// Scale sum of samples to get value of integral.  Also hack in a
			// fade to ensure the left edge of the image goes strictly to zero.
			float scale = 20.0f / float(cIter);
			if (samplePos.x * 25 < resolution)
			{
				scale *= min(25.0f * float(samplePos.x) / resolution, 1.0f);
			}
			rgb[0] *= scale;
			rgb[1] *= scale;
			rgb[2] *= scale;

			// Clamp to [0, 1]
			rgb[0] = min(max(rgb[0], 0.0f), 1.0f);
			rgb[1] = min(max(rgb[1], 0.0f), 1.0f);
			rgb[2] = min(max(rgb[2], 0.0f), 1.0f);

			// Convert linear to sRGB
			rgb[0] = (rgb[0] < 0.0031308f) ? (12.92f * rgb[0]) : (1.055f * pow(rgb[0], 1.0f / 2.4f) - 0.055f);
			rgb[1] = (rgb[1] < 0.0031308f) ? (12.92f * rgb[1]) : (1.055f * pow(rgb[1], 1.0f / 2.4f) - 0.055f);
			rgb[2] = (rgb[2] < 0.0031308f) ? (12.92f * rgb[2]) : (1.055f * pow(rgb[2], 1.0f / 2.4f) - 0.055f);

			return rgb;
		}

		float3 CaculateDirectScatter(float2 samplePos, float curvatureBias, float curvatureScale, float NdotLBias, float NdotLScale)
		{
			float NdotL = samplePos.x * NdotLScale + NdotLBias;
			float theta = acos(NdotL);

			float curvature = float(samplePos.y) * curvatureScale + curvatureBias;
			float radius = 1.0f / curvature;

			// Sample points around a ring, and Monte-Carlo-integrate the
			// scattered lighting using the diffusion profile

			static const int cIter = 128;
			float3 rgb = 0;

			// Set integration bounds in arc-length in mm on the sphere
			float lowerBound = max(-3.14f * radius, -10.0f);
			float upperBound = min(3.14f * radius, 10.0f);

			float iterScale = (upperBound - lowerBound) / float(cIter);
			float iterBias = lowerBound + 0.5f * iterScale;

			for (int iIter = 0; iIter < cIter; ++iIter)
			{
				float delta = float(iIter) * iterScale + iterBias;
				float3 rgbDiffusion = 0;
				EvaluateDiffusionProfile(delta, rgbDiffusion);

				float NdotLDelta = max(0.0f, cos(theta - delta * curvature));
				rgb[0] += NdotLDelta * rgbDiffusion.x;
				rgb[1] += NdotLDelta * rgbDiffusion.y;
				rgb[2] += NdotLDelta * rgbDiffusion.z;
			}

			// Scale sum of samples to get value of integral
			float scale = (upperBound - lowerBound) / float(cIter);
			rgb[0] *= scale;
			rgb[1] *= scale;
			rgb[2] *= scale;

			// Calculate delta from standard diffuse lighting (saturate(N.L)) to
			// scattered result, remapped from [-.25, .25] to [0, 1].
			float rgbAdjust = -max(0.0f, NdotL) * 2.0f + 0.5f;
			rgb[0] = rgb[0] * 2.0f + rgbAdjust;
			rgb[1] = rgb[1] * 2.0f + rgbAdjust;
			rgb[2] = rgb[2] * 2.0f + rgbAdjust;

			// Clamp to [0, 1]
			rgb[0] = min(max(rgb[0], 0.0f), 1.0f);
			rgb[1] = min(max(rgb[1], 0.0f), 1.0f);
			rgb[2] = min(max(rgb[2], 0.0f), 1.0f);

			return radius;
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
			float curvatiRcpWidthMin = 10.0f / _ScatterRadiuMax;
			float curvatiRcpWidthMax = 10.0f / _ScatterRadiuMin;
			float curvatiScale = (curvatiRcpWidthMax - curvatiRcpWidthMin) / 128.0f;
			float curvatiBias = curvatiRcpWidthMin + 0.5f * curvatiScale;
			float NdotLBias = -1.0f + 0.5f * 64.0f;
			
			//float3 LUTColor = CaculateDirectScatter(uv * 128.0, curvatiBias, curvatiScale, NdotLBias, 64.0);
			//float3 LUTColor = BakeBurleyShadowSSSLUT_NoL_ShadowValue(uv, _Albedo.rgb, max(0.01, _ScatterColor.rgb), _ScatterScale, _ScatterRadiuMax, _ScatterRadiuMin);
			float3 LUTColor = BakeBurleySSSLUT(uv, _Albedo.rgb, max(0.01, _ScatterColor.rgb), _ScatterScale, _ScatterRadiuMax, _ScatterRadiuMin);

			// Convert linear to sRGB
			/*LUTColor[0] = (LUTColor[0] < 0.0031308f) ? (12.92f * LUTColor[0]) : (1.055f * pow(LUTColor[0], 1.0f / 2.4f) - 0.055f);
			LUTColor[1] = (LUTColor[1] < 0.0031308f) ? (12.92f * LUTColor[1]) : (1.055f * pow(LUTColor[1], 1.0f / 2.4f) - 0.055f);
			LUTColor[2] = (LUTColor[2] < 0.0031308f) ? (12.92f * LUTColor[2]) : (1.055f * pow(LUTColor[2], 1.0f / 2.4f) - 0.055f);*/

			return LUTColor;
		}

		float3 frag_Integrated_SkinShadow(v2f_customrendertexture i) : SV_Target
		{
			float2 uv = i.localTexcoord.xy;
			float shadowRcpWidthMin = 1.0f / _ScatterRadiuMax;
			float shadowRcpWidthMax = 1.0f / _ScatterRadiuMin;
			float shadowScale = (shadowRcpWidthMax - shadowRcpWidthMin) / 128.0f;
			float shadowBias = shadowRcpWidthMin + 0.5f * shadowScale;
			return CaculateShadowScatter(uv * 128.0, shadowBias, shadowScale, 10.0f, 128.0f);
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
			for(int j = 0; j < 9; ++j)
			{
				Irradiance += SHTable.Coefficients[j] * SHBasis.Basis[j];
			}
			return Irradiance;*/
		}
		Texture2D _BestFitLUT;
		SamplerState sampler_BestFitLUT;

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
