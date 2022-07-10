#ifndef _Occlusion_
#define _Occlusion_

#include "Common.hlsl"

float GTAO_Noise(uint2 pixelPosition)
{
	return frac(52.9829189 * frac(dot(pixelPosition, float2( 0.06711056, 0.00583715))));
}

float GTAO_Offsets(uint2 pixelPosition)
{
	return 0.25 * (float)((pixelPosition.y - pixelPosition.x) & 3);
}

float ComputeDistanceFade(float distance, float2 fadeness)
{
	return saturate(max(0, distance - fadeness.x) * fadeness.y);
}

float IntegrateArc_UniformWeight(float2 h)
{
	float2 Arc = 1 - cos(h);
	return Arc.x + Arc.y;
}

float IntegrateArc_CosWeight(float2 h, float n)
{
    float2 Arc = -cos(2 * h - n) + cos(n) + 2 * h * sin(n);
    return 0.25 * (Arc.x + Arc.y + 0.05);
}

float GTAO(int numRay, int numStep, float radius, float power, float halfProjScale, float temporalOffset, float temporalDirection, float sceneDepth, float2 screenUV, uint2 pixelPosition, float3 viewPos, float3 viewDir, float3 viewNormal, float4 resolution, float4x4 matrix_InvProj, Texture2D DepthTexture, SamplerState DepthSampler)
{
	float2 radius_thickness = lerp(float2(radius, 1), 0, ComputeDistanceFade(viewPos.z, 0.005));
	float stepRadius = (max(min((radius_thickness.x * halfProjScale) / viewPos.z, 512), (float)numStep)) / ((float)numStep + 1);
	float noiseOffset = frac(GTAO_Offsets(pixelPosition) + temporalOffset);
	float noiseDirection = GTAO_Noise(pixelPosition) + temporalDirection;

	float occlusion = 0;
	float vdirXYDot = dot(viewDir.xy, viewDir.xy);

	[loop]
	for (int i = 0; i < numRay; ++i)
	{
		float angle = (i + noiseDirection) * (PI / (float)numRay);
		float3 sliceDir = float3(float2(cos(angle), sin(angle)), 0);

		half wallDarkeningCorrection = dot(viewNormal, cross(viewDir, sliceDir)) * vdirXYDot;
		wallDarkeningCorrection = wallDarkeningCorrection * wallDarkeningCorrection;

		float2 h = -1;

		[loop]
		for (int j = 0; j < numStep; ++j)
		{
			float2 uvOffset = (sliceDir.xy * resolution.zw) * max(stepRadius * (j + noiseOffset), 1 + j);
			float4 uvSlice = screenUV.xyxy + float4(uvOffset.xy, -uvOffset);

			//float3 h1 = GetPosition(uvSlice.xy) - viewPos;
			//float3 h2 = GetPosition(uvSlice.zw) - viewPos;

			float3 h1 = GetViewSpacePosInvZ(GetScreenSpacePos(uvSlice.xy, DepthTexture.SampleLevel(DepthSampler, uvSlice.xy, 0).r), matrix_InvProj) - viewPos;
			float3 h2 = GetViewSpacePosInvZ(GetScreenSpacePos(uvSlice.zw, DepthTexture.SampleLevel(DepthSampler, uvSlice.zw, 0).r), matrix_InvProj) - viewPos;

			float2 h1h2 = float2(dot(h1, h1), dot(h2, h2));
			float2 h1h2Length = rsqrt(h1h2 + 0.001);

			float2 falloff = saturate(h1h2 * (2 / pow2(radius_thickness.x)));
			float2 H = float2(dot(h1, viewDir), dot(h2, viewDir)) * h1h2Length;
			
			h.xy = (H.xy > h.xy) ? lerp(H, h, falloff) : lerp(H.xy, h.xy, radius_thickness.y);
		}

		float3 planeNormal = normalize(cross(sliceDir, viewDir));
		float3 planeTangent = cross(viewDir, planeNormal);
		float3 sliceNormal = viewNormal - planeNormal * dot(viewNormal, planeNormal);
		float sliceLength = length(sliceNormal) + 0.0001;
		float cos_n = clamp(dot(sliceNormal, viewDir) / sliceLength, -1, 1);
		float n = -sign(dot(sliceNormal, planeTangent)) * acos(cos_n);
		h = acos(clamp(h, -1, 1));
		h.x = n + max(-h.x -n, -Half_PI);
		h.y = n + min(h.y -n, Half_PI);

		//bentAngle = (h.x + h.y) * 0.5;
		//bentNormal += viewDir * cos(bentAngle) - planeTangent * sin(bentAngle);
		occlusion += (sliceLength + wallDarkeningCorrection) * IntegrateArc_CosWeight(h, n);	
	}

	//bentNormal = normalize(normalize(bentNormal) - viewDir * 0.5);
	occlusion = saturate(pow(occlusion / (float)numRay, power));
    return occlusion;
}

#endif
