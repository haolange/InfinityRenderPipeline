#ifndef _SSRTRayCast_
#define _SSRTRayCast_

////////////////////////GlobalData////////////////////////
#include "UnityCG.cginc"
#include "../../Common.hlsl"
#include "../../Random.hlsl"
#include "../../Montcalo.hlsl"

//float _Time, _ZBufferParams;

//////Linear 3DTrace
float4 LinearTraceRay3DSpace(Texture2D _DepthTexture, int NumSteps, float Thickness, float2 BlueNoise, float3 rayPos, float3 rayDir) {
    Thickness = clamp(Thickness / 500, 0.001, 0.01);
    float mask = 1;
    float rayDepth = -rayPos.z;

	float2 jitter = BlueNoise + 0.5;
	float StepSize = 1 / (float)NumSteps;
	StepSize = StepSize * (jitter.x + jitter.y) + StepSize;

	for (int i = 0;  i < NumSteps; i++) {
		float endDepth = -Texture2DSampleLevel(_DepthTexture, Global_point_clamp_sampler, rayPos.xy, 0).r;
        
		if (-rayPos.z < endDepth) {
			rayPos += rayDir * StepSize;
		} else if(-rayPos.z > endDepth + Thickness) {
            float delta = ( -LinearEyeDepth(endDepth) ) - ( -LinearEyeDepth(rayPos.z) );
            mask = delta <= Thickness && i > 0;
		}
	}
	return float4(rayPos, mask);
}

//////Hierarchical_Z Trace_1
float GetScreenFadeBord(float2 pos, float value)
{
    float borderDist = min(1 - max(pos.x, pos.y), min(pos.x, pos.y));
    return saturate(borderDist > value ? 1 : borderDist / value);
}

float GetMarchSize(float2 RayCurrrStepPos, float2 RayNextStepPos, float2 RaytraceSize)
{
    float2 RayPos = abs(RayNextStepPos - RayCurrrStepPos);
    return length(float2(min(RayPos.x, RaytraceSize.x), min(RayPos.y, RaytraceSize.y)));
}

float4 HiZ_Trace(int NumSteps, float Thickness, float2 RaytraceSize, float3 rayOrigin, float3 rayDir, Texture2D RT_PyramidDepth)
{
    float SamplerSize = GetMarchSize(rayOrigin.xy, rayOrigin.xy + rayDir.xy, RaytraceSize);
    float3 Curr_RayPos = rayOrigin + rayDir * SamplerSize;

    int level = 0; 
    float RayMask = 0.0;
    
    [loop]
    for (int i = 0; i < NumSteps; ++i) {
        SamplerSize = GetMarchSize( Curr_RayPos.xy, Curr_RayPos.xy + rayDir.xy, RaytraceSize * exp2(level + 1.0) );
        float3 Prev_RayPos = Curr_RayPos + rayDir * SamplerSize;
        float MinZPlane = RT_PyramidDepth.SampleLevel(Global_point_clamp_sampler, Prev_RayPos.xy, level).r;

        [branch]
        if (MinZPlane < Prev_RayPos.z) {
            level = min(level + 1.0, 6.0);
            Curr_RayPos = Prev_RayPos;
        } else {
            level--;
        }

        [branch]
        if (level < 0.0) {
            float IntersectionDiff = -LinearEyeDepth(MinZPlane) + LinearEyeDepth(Curr_RayPos.z);
            RayMask = IntersectionDiff <= Thickness && i > 0.0;
            return float4(Curr_RayPos, RayMask);
        }
    }
    return float4(Curr_RayPos, RayMask);
}

//////Hierarchical_Z Trace_2
float2 cell(float2 ray, float2 cell_count) {
	return floor(ray.xy * cell_count);
}

float2 cell_count(float level, float2 ScreenSize) {
	return ScreenSize / (level == 0 ? 1 : exp2(level));
}

float3 intersect_cell_boundary(float3 rayOrigin, float3 rayDir, float2 cellIndex, float2 cellCount, float2 crossStep, float2 crossOffset)
{
    float2 cell_size = rcp(cellCount);
    float2 planes = cellIndex / cellCount + cell_size * crossStep;

    float2 solutions = (planes - rayOrigin.xy) / rayDir.xy;
    float3 intersection_pos = rayOrigin + rayDir * min(solutions.x, solutions.y);

    intersection_pos.xy += (solutions.x < solutions.y) ? float2(crossOffset.x, 0) : float2(0, crossOffset.y);

    return intersection_pos;
}

bool crossed_cell_boundary(float2 cell_id_one, float2 cell_id_two) {
	return (int)cell_id_one.x != (int)cell_id_two.x || (int)cell_id_one.y != (int)cell_id_two.y;
}

float minimum_depth_plane(float2 ray, float level, float2 cell_count, Texture2D SceneDepth) {
	return -SceneDepth.Load( int3( (ray * cell_count), level ) ).r;
}

float4 HiZ_Trace(int HiZ_Max_Level, int HiZ_Start_Level, int HiZ_Stop_Level, int NumSteps, float Thickness, float2 screenSize, float3 rayOrigin, float3 rayDir, Texture2D SceneDepth)
{
    rayDir = float3(rayDir.x, rayDir.y, -rayDir.z);
    rayOrigin = float3(rayOrigin.x, rayOrigin.y, -rayOrigin.z);

    float level = HiZ_Start_Level;
    float2 hi_z_size = cell_count(level, screenSize);
    float3 ray = rayOrigin;

    float2 cross_step = float2(rayDir.x >= 0 ? 1 : -1, rayDir.y >= 0 ? 1 : -1);
    float2 cross_offset = cross_step * 0.00001;
    cross_step = saturate(cross_step);

    float2 ray_cell = cell(ray.xy, hi_z_size.xy);
    ray = intersect_cell_boundary(ray, rayDir, ray_cell, hi_z_size, cross_step, cross_offset);

    int iterations = 0;
    float mask = 1;

    while(level >= HiZ_Stop_Level && iterations < NumSteps) {
        float3 tmp_ray = ray;
        float2 current_cell_count = cell_count(level, screenSize);
        float2 old_cell_id = cell(ray.xy, current_cell_count);
        float min_z = minimum_depth_plane(ray.xy, level, current_cell_count, SceneDepth);

        float delta = LinearEyeDepth(min_z) - LinearEyeDepth(ray.z);
        mask = delta <= Thickness && iterations > 0;

        [branch]
        if(rayDir.z > 0) {
            float min_minus_ray = min_z - ray.z;
            tmp_ray = min_minus_ray > 0 ? ray + (rayDir / rayDir.z) * min_minus_ray : tmp_ray;
            float2 new_cell_id = cell(tmp_ray.xy, current_cell_count);

            [branch]
            if(crossed_cell_boundary(old_cell_id, new_cell_id)) {
                tmp_ray = intersect_cell_boundary(ray, rayDir, old_cell_id, current_cell_count, cross_step, cross_offset);
                level = min(HiZ_Max_Level, level + 2);
            } else { //Trace Behind Surfaces
                [branch]
                if(level == 1 && abs(min_minus_ray) > 0.0001) {
                    tmp_ray = intersect_cell_boundary(ray, rayDir, old_cell_id, current_cell_count, cross_step, cross_offset);
                    level = 2;
                }
            }
        } else if(ray.z < min_z) { //ToWard Ray
            tmp_ray = intersect_cell_boundary(ray, rayDir, old_cell_id, current_cell_count, cross_step, cross_offset);
            level = min(HiZ_Max_Level, level + 2);
        }

        ray.xyz = tmp_ray.xyz;
        level--;
        iterations++;
    }

    return half4( ray.xy, -ray.z, saturate(mask) );
}

float GetStepScreenFactorToClipAtScreenEdge(float2 RayStartScreen, float2 RayStepScreen)
{
	const float RayStepScreenInvFactor = 0.5 * length(RayStepScreen);
	const float2 S = 1 - max(abs(RayStepScreen + RayStartScreen * RayStepScreenInvFactor) - RayStepScreenInvFactor, 0.0f) / abs(RayStepScreen);
	const float RayStepFactor = min(S.x, S.y) / RayStepScreenInvFactor;
	return RayStepFactor;
}

bool RayCast_Diffuse(uint NumSteps, float Roughness, float CompareFactory, float StepOffset, float3 RayStartScreen, float3 RayStepScreen, Texture2D Texture, out float3 OutHitUVz, out float Level)
{
	float Step = 1.0 / NumSteps;
    float CompareTolerance = CompareFactory * Step;

	float3 RayStartUVz = float3((RayStartScreen.xy * float2(0.5, 0.5) + 0.5), RayStartScreen.z);
	float3 RayStepUVz  = float3(RayStepScreen.xy  * float2(0.5, 0.5), RayStepScreen.z);
	RayStepUVz *= Step;
	float3 RayUVz = RayStartUVz + RayStepUVz * StepOffset;
    
	Level = 1;
	bool bHit = false;
	OutHitUVz = float3(0, 0, 0);

	[loop]
	for(uint i = 0; i < NumSteps; i += 4)
	{
		float4 SampleUV0 = RayUVz.xyxy + RayStepUVz.xyxy * float4( 1, 1, 2, 2 );
		float4 SampleUV1 = RayUVz.xyxy + RayStepUVz.xyxy * float4( 3, 3, 4, 4 );
		float4 SampleZ   = RayUVz.zzzz + RayStepUVz.zzzz * float4( 1, 2, 3, 4 );

		float4 SampleDepth;
		SampleDepth.x = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV0.xy), Level).r;
		SampleDepth.y = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV0.zw), Level).r;
		Level += (8.0 / NumSteps);
		
		SampleDepth.z = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV1.xy), Level).r;
		SampleDepth.w = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV1.zw), Level).r;
		Level += (8.0 / NumSteps);

		float4 DepthDiff = SampleZ - SampleDepth;
		bool4 Hit = abs(DepthDiff + CompareTolerance) < CompareTolerance;

		[branch] 
        if( any(Hit) ) {
			float4 HitTime = Hit ? float4( 1, 2, 3, 4 ) : 5;
			float Time1 = min( min3( HitTime.x, HitTime.y, HitTime.z ), HitTime.w );
			float Time0 = Time1 - 1;

		#if 0 // Binary search
            for( uint j = 0; j < 4; j++ ) {
                CompareTolerance *= 0.5;

                float  MidTime = 0.5 * ( Time0 + Time1 );
                float3 MidUVz = RayUVz + RayStepUVz * MidTime;
                float  MidDepth = Texture.SampleLevel( Global_point_clamp_sampler, MidUVz.xy, Level ).r;
                float  MidDepthDiff = MidUVz.z - MidDepth;

                if( abs( MidDepthDiff + CompareTolerance ) < CompareTolerance ) {
                    Time1 = MidTime;
                } else {
                    Time0 = MidTime;
                }
            }
		#endif
			OutHitUVz = RayUVz + RayStepUVz * Time1;
			bHit = true;
			break;
		}
		RayUVz += 4 * RayStepUVz;
	}
	return bHit;
}

bool RayCast_Specular(uint NumSteps, float Roughness, float CompareFactory, float StepOffset, float3 RayStartScreen, float3 RayStepScreen, Texture2D Texture, out float3 OutHitUVz, out float Level)
{
	float Step = 1.0 / NumSteps;
    float CompareTolerance = CompareFactory * Step;

	float3 RayStartUVz = float3((RayStartScreen.xy * float2(0.5, 0.5) + 0.5), RayStartScreen.z);
	float3 RayStepUVz  = float3(RayStepScreen.xy  * float2(0.5, 0.5), RayStepScreen.z);
	RayStepUVz *= Step;
	float3 RayUVz = RayStartUVz + RayStepUVz * StepOffset;
    
	Level = 1;
	bool bHit = false;
	float LastDiff = 0;
	OutHitUVz = float3(0, 0, 0);

	[unroll(12)]
	for( uint i = 0; i < NumSteps; i += 4 )
	{
		// Vectorized to group fetches
		float4 SampleUV0 = RayUVz.xyxy + RayStepUVz.xyxy * float4( 1, 1, 2, 2 );
		float4 SampleUV1 = RayUVz.xyxy + RayStepUVz.xyxy * float4( 3, 3, 4, 4 );
		float4 SampleZ   = RayUVz.zzzz + RayStepUVz.zzzz * float4( 1, 2, 3, 4 );
		
		// Use lower res for farther samples
		float4 SampleDepth;
		SampleDepth.x = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV0.xy), 0).r;
		SampleDepth.y = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV0.zw), 0).r;
		Level += (8.0 / NumSteps) * Roughness;
		
		SampleDepth.z = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV1.xy), 0).r;
		SampleDepth.w = Texture.SampleLevel(Global_point_clamp_sampler, (SampleUV1.zw), 0).r;
		Level += (8.0 / NumSteps) * Roughness;

		float4 DepthDiff = SampleZ - SampleDepth;
		bool4 Hit = abs(DepthDiff + CompareTolerance) < CompareTolerance;

		[branch] 
        if(any(Hit))
		{
			float DepthDiff0 = DepthDiff[2];
			float DepthDiff1 = DepthDiff[3];
			float Time0 = 3;

			[flatten]  
            if( Hit[2] ) {
				DepthDiff0 = DepthDiff[1];
				DepthDiff1 = DepthDiff[2];
				Time0 = 2;
			}
			[flatten] 
            if( Hit[1] ) {
				DepthDiff0 = DepthDiff[0];
				DepthDiff1 = DepthDiff[1];
				Time0 = 1;
			}
			[flatten] 
            if( Hit[0] ) {
				DepthDiff0 = LastDiff;
				DepthDiff1 = DepthDiff[0];
				Time0 = 0;
			}

			float Time1 = Time0 + 1;
		#if 0
			// Binary search
			for( uint j = 0; j < 4; j++ )
			{
				CompareTolerance *= 0.5;

				float  MidTime = 0.5 * ( Time0 + Time1 );
				float3 MidUVz = RayUVz + RayStepUVz * MidTime;
				float  MidDepth = Texture.SampleLevel( Global_point_clamp_sampler, MidUVz.xy, Level ).r;
				float  MidDepthDiff = MidUVz.z - MidDepth;

				if( abs( MidDepthDiff + CompareTolerance ) < CompareTolerance ) {
					DepthDiff1	= MidDepthDiff;
					Time1		= MidTime;
				} else {
					DepthDiff0	= MidDepthDiff;
					Time0		= MidTime;
				}
			}
		#endif
			float TimeLerp = saturate( DepthDiff0 / (DepthDiff0 - DepthDiff1) );
			float IntersectTime = Time0 + TimeLerp;
			OutHitUVz = RayUVz + RayStepUVz * IntersectTime;

			bHit = true;
			break;
		}
		LastDiff = DepthDiff.w;
		RayUVz += 4 * RayStepUVz;
	}

	return bHit;
}

#ifndef IS_SSGI_SHADER
#define IS_SSGI_SHADER 0
#endif

#ifndef SSGI_TRACE_CONE
#define SSGI_TRACE_CONE 0
#endif

bool RayCast_All(uint NumSteps, float Roughness, float CompareFactory, float StepOffset, float3 RayStartScreen, float3 RayStepScreen, Texture2D Texture, out float3 OutHitUVz, out float Level)
{
	float3 RayStartUVz = float3((RayStartScreen.xy * float2(0.5, 0.5) + 0.5), RayStartScreen.z);
	float3 RayStepUVz  = float3(RayStepScreen.xy  * float2(0.5, 0.5), RayStepScreen.z);
	
	const float Step = 1.0 / NumSteps;
	float CompareTolerance = CompareFactory * Step;
	
	float LastDiff = 0;
	Level = 1;

	RayStepUVz *= Step;
	float3 RayUVz = RayStartUVz + RayStepUVz * StepOffset;

	#if IS_SSGI_SHADER && SSGI_TRACE_CONE
		RayUVz = RayStartUVz;
	#endif
	
	float4 MultipleSampleDepthDiff;
	bool4 bMultipleSampleHit; // TODO: Might consumes VGPRS if bug in compiler.
	bool bFoundAnyHit = false;
	
	#if IS_SSGI_SHADER && SSGI_TRACE_CONE
		const float ConeAngle = PI / 4;
		const float d = 1;
		const float r = d * sin(0.5 * ConeAngle);
		const float Exp = 1.6; //(d + r) / (d - r);
		const float ExpLog2 = log2(Exp);
		const float MaxPower = exp2(log2(Exp) * (NumSteps + 1.0)) - 0.9;
	#endif

	uint i;
	[loop]
	for (i = 0; i < NumSteps; i += 4)
	{
		float2 SamplesUV[4];
		float4 SamplesZ;
		float4 SamplesMip;

		// Compute the sample coordinates.
		#if IS_SSGI_SHADER && SSGI_TRACE_CONE
		{
			[unroll]
			for (uint j = 0; j < 4; j++) {
				float S = float(i + j) + StepOffset;

				float NormalizedPower = (exp2(ExpLog2 * S) - 0.9) / MaxPower;

				float Offset = NormalizedPower * NumSteps;

				SamplesUV[j] = RayUVz.xy + Offset * RayStepUVz.xy;
				SamplesZ[j] = RayUVz.z + Offset * RayStepUVz.z;
			}
		
			SamplesMip.xy = Level;
			Level += (8.0 / NumSteps) * Roughness;
		
			SamplesMip.zw = Level;
			Level += (8.0 / NumSteps) * Roughness;
		} 
		#else 
		{
			[unroll]
			for (uint j = 0; j < 4; j++) {
				SamplesUV[j] = RayUVz.xy + (float(i) + float(j + 1)) * RayStepUVz.xy;
				SamplesZ[j] = RayUVz.z + (float(i) + float(j + 1)) * RayStepUVz.z;
			}
		
			SamplesMip.xy = Level;
			Level += (8.0 / NumSteps) * Roughness;
		
			SamplesMip.zw = Level;
			Level += (8.0 / NumSteps) * Roughness;
		}
		#endif

		// Sample the scene depth.
		float4 SampleDepth;
		{
			[unroll]
			for (uint j = 0; j < 4; j++) {
				SampleDepth[j] = Texture.SampleLevel(Global_point_clamp_sampler, SamplesUV[j], SamplesMip[j]).r;
			}
		}

		// Evaluates the intersections.
		MultipleSampleDepthDiff = SamplesZ - SampleDepth;
		bMultipleSampleHit = abs(MultipleSampleDepthDiff + CompareTolerance) < CompareTolerance;
		bFoundAnyHit = any(bMultipleSampleHit);

		[branch]
		if (bFoundAnyHit) {
			break;
		}
		LastDiff = MultipleSampleDepthDiff.w;
	} 
	
	// Compute the output coordinates.
	[branch]
	if (bFoundAnyHit)
    {
		#if IS_SSGI_SHADER && SSGI_TRACE_CONE
		{
			// If hit set to intersect time. If missed set to beyond end of ray
            float4 HitTime = bMultipleSampleHit ? float4(0, 1, 2, 3) : 4;
			// Take closest hit
            float Time1 = min(min3(HitTime.x, HitTime.y, HitTime.z), HitTime.w);
			float S = float(i + Time1) + StepOffset;
			float NormalizedPower = (exp2(log2(Exp) * S) - 0.9) / MaxPower;
			float Offset = NormalizedPower * NumSteps;
            OutHitUVz = RayUVz + RayStepUVz * Offset;
		}
		#elif IS_SSGI_SHADER
		{
			// If hit set to intersect time. If missed set to beyond end of ray
            float4 HitTime = bMultipleSampleHit ? float4(1, 2, 3, 4) : 5;
			// Take closest hit
            float Time1 = float(i) + min(min3(HitTime.x, HitTime.y, HitTime.z), HitTime.w);
            OutHitUVz = RayUVz + RayStepUVz * Time1;
		}
		#elif 0 // binary search refinement that has been attempted for SSR.
        {
			// If hit set to intersect time. If missed set to beyond end of ray
            float4 HitTime = bMultipleSampleHit ? float4(1, 2, 3, 4) : 5;

			// Take closest hit
            float Time1 = float(i) + min(min3(HitTime.x, HitTime.y, HitTime.z), HitTime.w);
            float Time0 = Time1 - 1;

            const uint NumBinarySteps = Roughness < 0.2 ? 4 : 0;

			// Binary search
            for (uint j = 0; j < NumBinarySteps; j++)
            {
                CompareTolerance *= 0.5;

                float MidTime = 0.5 * (Time0 + Time1);
                float3 MidUVz = RayUVz + RayStepUVz * MidTime;
                float MidDepth = Texture.SampleLevel(Global_point_clamp_sampler, MidUVz.xy, Level).r;
                float MidDepthDiff = MidUVz.z - MidDepth;

                if (abs(MidDepthDiff + CompareTolerance) < CompareTolerance) {
                    Time1 = MidTime;
                } else {
                    Time0 = MidTime;
                }
            }
            OutHitUVz = RayUVz + RayStepUVz * Time1;
        }
		#else // SSR
        {
            float DepthDiff0 = MultipleSampleDepthDiff[2];
            float DepthDiff1 = MultipleSampleDepthDiff[3];
            float Time0 = 3;

            [flatten]
            if (bMultipleSampleHit[2]) {
                DepthDiff0 = MultipleSampleDepthDiff[1];
                DepthDiff1 = MultipleSampleDepthDiff[2];
                Time0 = 2;
            }
            [flatten]
            if (bMultipleSampleHit[1]) {
                DepthDiff0 = MultipleSampleDepthDiff[0];
                DepthDiff1 = MultipleSampleDepthDiff[1];
                Time0 = 1;
            }
            [flatten]
            if (bMultipleSampleHit[0]) {
                DepthDiff0 = LastDiff;
                DepthDiff1 = MultipleSampleDepthDiff[0];
                Time0 = 0;
            }

			Time0 += float(i);

            float Time1 = Time0 + 1;
			#if 0
			{
				// Binary search
				for( uint j = 0; j < 4; j++ )
				{
					CompareTolerance *= 0.5;

					float  MidTime = 0.5 * ( Time0 + Time1 );
					float3 MidUVz = RayUVz + RayStepUVz * MidTime;
					float  MidDepth = Texture.SampleLevel( Global_point_clamp_sampler, MidUVz.xy, Level ).r;
					float  MidDepthDiff = MidUVz.z - MidDepth;

					if( abs( MidDepthDiff + CompareTolerance ) < CompareTolerance )
					{
						DepthDiff1	= MidDepthDiff;
						Time1		= MidTime;
					}
					else
					{
						DepthDiff0	= MidDepthDiff;
						Time0		= MidTime;
					}
				}
			}
			#endif

			// Find more accurate hit using line segment intersection
            float TimeLerp = saturate(DepthDiff0 / (DepthDiff0 - DepthDiff1));
            float IntersectTime = Time0 + TimeLerp;
				
            OutHitUVz = RayUVz + RayStepUVz * IntersectTime;
        }
		#endif
    } else {
		OutHitUVz = float3(0, 0, 0);
    }
	
	return bFoundAnyHit;
} 

#endif