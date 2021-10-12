#ifndef _Occlusion_
#define _Occlusion_

#include "../../../../Shader/Include/Common.hlsl"

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
    return 0.25 * (Arc.x + Arc.y);
}

float GTAO(int numRay, int numStep, float radius, float power, float halfProjScale, float temporalOffset, float temporalDirection, float sceneDepth, float2 screenUV, uint2 pixelPosition, float3 viewPos, float3 viewDir, float3 viewNormal, float4 resolution, float4x4 matrix_InvProj, Texture2D DepthTexture, SamplerState DepthSampler)
{
	float2 radius_thickness = lerp(float2(radius, 1), 0, ComputeDistanceFade(viewPos.b, 0).xx);
	float stepRadius = (max(min((radius_thickness.x * halfProjScale) / viewPos.b, 512), (float)numStep)) / ((float)numStep + 1);
	float noiseOffset = frac(GTAO_Offsets(pixelPosition) + temporalOffset);
	float noiseDirection = GTAO_Noise(pixelPosition) + temporalDirection;

	float Occlusion, angle, wallDarkeningCorrection, sliceLength, n, cos_n;
	float2 slideDir_TexelSize, h, H, falloff, uvOffset, h1h2, h1h2Length;
	float3 sliceDir, h1, h2, planeNormal, planeTangent, sliceNormal;
	float4 uvSlice;

	[loop]
	for (int i = 0; i < numRay; i++)
	{
		angle = (i + noiseDirection) * (Pi / (float)numRay);
		sliceDir = float3(float2(cos(angle), sin(angle)), 0);

		planeNormal = normalize(cross(sliceDir, viewDir));
		planeTangent = cross(viewDir, planeNormal);
		sliceNormal = viewNormal - planeNormal * dot(viewNormal, planeNormal);
		sliceLength = length(sliceNormal);

		cos_n = clamp(dot(normalize(sliceNormal), viewDir), -1, 1);
		n = -sign(dot(sliceNormal, planeTangent)) * acos(cos_n);
		h = -1;

		[loop]
		for (int j = 0; j < numStep; j++)
		{
			uvOffset = (sliceDir.xy * resolution.zw) * max(stepRadius * (j + noiseOffset), 1 + j);
			uvSlice = screenUV.xyxy + float4(uvOffset.xy, -uvOffset);

			//h1 = GetPosition(uvSlice.xy) - viewPos;
			//h2 = GetPosition(uvSlice.zw) - viewPos;

			h1 = GetViewSpacePosInvZ(GetNDCPos(uvSlice.xy, DepthTexture.SampleLevel(DepthSampler, uvSlice.xy, 0).r), matrix_InvProj) - viewPos;
			h2 = GetViewSpacePosInvZ(GetNDCPos(uvSlice.zw, DepthTexture.SampleLevel(DepthSampler, uvSlice.zw, 0).r), matrix_InvProj) - viewPos;

			h1h2 = float2(dot(h1, h1), dot(h2, h2));
			h1h2Length = rsqrt(h1h2);

			falloff = saturate(h1h2 * (2 / pow2(radius_thickness.x)));

			H = float2(dot(h1, viewDir), dot(h2, viewDir)) * h1h2Length;
			h.xy = (H.xy > h.xy) ? lerp(H, h, falloff) : lerp(H.xy, h.xy, radius_thickness.y);
		}

		h = acos(clamp(h, -1, 1));
		h.x = n + max(-h.x - n, -Half_Pi);
		h.y = n + min(h.y - n, Half_Pi);

		//BentAngle = (h.x + h.y) * 0.5;
		//BentNormal += viewDir * cos(BentAngle) - planeTangent * sin(BentAngle);

		Occlusion += sliceLength * IntegrateArc_CosWeight(h, n); 			
		//Occlusion += sliceLength * IntegrateArc_UniformWeight(h);			
	}

	//BentNormal = normalize(normalize(BentNormal) - viewDir * 0.5);
	Occlusion = saturate(pow(Occlusion / (float)numRay, power));
    return Occlusion;
}

#endif
