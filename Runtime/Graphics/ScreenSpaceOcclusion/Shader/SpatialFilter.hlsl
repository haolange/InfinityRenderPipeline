#ifndef _SpatialFilter_
#define _SpatialFilter_

#define BLUR_RADIUS 8

#include "Common.hlsl"
#include "UnityCG.cginc"

void GetAO_Depth(float2 sampleUV, inout float occlusion, inout float depth)
{
	depth = LinearEyeDepth(SRV_DepthTexture.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0).r);
	occlusion = SRV_OcclusionTexture.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0).r;
}

float CrossBilateralWeight(float radius, float sampleDepth, float sceneDepth) 
{
	float blurSigma = (float)BLUR_RADIUS * 0.5;
	float blurFalloff = 1 / (2 * blurSigma * blurSigma);
    float edgeStop = (sceneDepth - sampleDepth) * _ProjectionParams.z * Sharpeness;
	return exp2(-radius * radius * blurFalloff - edgeStop * edgeStop);
}

void ProcessSample(float occlusion, float sampleDepth, float radius, float sceneDepth, inout float totalOcclusion, inout float totalWeight)
{
	float weight = CrossBilateralWeight(radius, sceneDepth, sampleDepth);
	totalWeight += weight;
	totalOcclusion += weight * occlusion;
}

void ProcessRadius(float2 screenUV, float2 deltaUV, float sceneDepth, inout float totalOcclusion, inout float totalWeight)
{
	float radius = 1;
	float sampleDepth = 0;
	float occlusion = 0;
	float2 sampleUV = 0;

	[unroll]
	for (; radius <= BLUR_RADIUS / 2; radius += 1) 
	{
		sampleUV = screenUV + radius * deltaUV;
		GetAO_Depth(sampleUV, occlusion, sampleDepth);
		ProcessSample(occlusion, sampleDepth, radius, sceneDepth, totalOcclusion, totalWeight);
	}

	[unroll]
	for (; radius <= BLUR_RADIUS; radius += 2) 
	{
		sampleUV = screenUV + (radius + 0.5f) * deltaUV;
		GetAO_Depth(sampleUV, occlusion, sampleDepth);
		ProcessSample(occlusion, sampleDepth, radius, sceneDepth, totalOcclusion, totalWeight);
	}
}

float BilateralBlur(float2 screenUV, float2 deltaUV)
{
	float sceneDepth;
	float totalWeight = 1;
	float totalOcclusion;
	GetAO_Depth(screenUV, totalOcclusion, sceneDepth);
		
	ProcessRadius(screenUV, -deltaUV, sceneDepth, totalOcclusion, totalWeight);
	ProcessRadius(screenUV, deltaUV, sceneDepth, totalOcclusion, totalWeight);

	return totalOcclusion /= totalWeight;
}
#endif
