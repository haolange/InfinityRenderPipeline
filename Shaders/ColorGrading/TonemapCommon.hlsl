// Copyright Epic Games, Inc. All Rights Reserved.

/*=============================================================================
	TonemapCommon.usf: PostProcessing tone mapping common
=============================================================================*/

#include "ACES.hlsl"
#include "GammaCorrectionCommon.hlsl"

// Scale factor for converting pixel values to nits. 
// This value is required for PQ (ST2084) conversions, because PQ linear values are in nits. 
// The purpose is to make good use of PQ lut entries. A scale factor of 100 conveniently places 
// about half of the PQ lut indexing below 1.0, with the other half for input values over 1.0.
// Also, 100nits is the expected monitor brightness for a 1.0 pixel value without a tone curve.
static const float LinearToNitsScale = 100.0;
static const float LinearToNitsScaleInverse = 1.0 / 100.0;

/*
============================================
// Uncharted settings
Slope = 0.63;
Toe = 0.55;
Shoulder = 0.47;
BlackClip= 0;
WhiteClip = 0.01;

// HP settings
Slope = 0.65;
Toe = 0.63;
Shoulder = 0.45;
BlackClip = 0;
WhiteClip = 0;

// Legacy settings
Slope = 0.98;
Toe = 0.3;
Shoulder = 0.22;
BlackClip = 0;
WhiteClip = 0.025;

// ACES settings
Slope = 0.91;
Toe = 0.53;
Shoulder = 0.23;
BlackClip = 0;
WhiteClip = 0.035;
===========================================
*/
float FilmSlope = 0.91;
float FilmToe = 0.53;
float FilmShoulder = 0.23;
float FilmBlackClip = 0;
float FilmWhiteClip = 0.035;

half3 FilmToneMap( half3 LinearColor ) 
{
	const float3x3 sRGB_2_AP0 = mul( XYZ_2_AP0_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );
	const float3x3 sRGB_2_AP1 = mul( XYZ_2_AP1_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );

	const float3x3 AP0_2_sRGB = mul( XYZ_2_sRGB_MAT, mul( D60_2_D65_CAT, AP0_2_XYZ_MAT ) );
	const float3x3 AP1_2_sRGB = mul( XYZ_2_sRGB_MAT, mul( D60_2_D65_CAT, AP1_2_XYZ_MAT ) );
	
	const float3x3 AP0_2_AP1 = mul( XYZ_2_AP1_MAT, AP0_2_XYZ_MAT );
	const float3x3 AP1_2_AP0 = mul( XYZ_2_AP0_MAT, AP1_2_XYZ_MAT );
	
	float3 ColorAP1 = LinearColor;
	//float3 ColorAP1 = mul( sRGB_2_AP1, float3(LinearColor) );

#if 0
	{
		float3 oces = Inverse_ODT_sRGB_D65( LinearColor );
		float3 aces = Inverse_RRT( oces );
		ColorAP1 = mul( AP0_2_AP1, aces );
	}
#endif
	
#if 0
	float3 ColorSRGB = mul( AP1_2_sRGB, ColorAP1 );
	ColorSRGB = max( 0, ColorSRGB );
	ColorAP1 = mul( sRGB_2_AP1, ColorSRGB );
#endif

	float3 ColorAP0 = mul( AP1_2_AP0, ColorAP1 );

#if 0
	{
		float3 aces = ColorAP0;
		float3 oces = RRT( aces );
		LinearColor = ODT_sRGB_D65( oces );
	}
	return mul( sRGB_2_AP1, LinearColor );
#endif

#if 1
	// "Glow" module constants
	const float RRT_GLOW_GAIN = 0.05;
	const float RRT_GLOW_MID = 0.08;

	float saturation = rgb_2_saturation( ColorAP0 );
	float ycIn = rgb_2_yc( ColorAP0 );
	float s = sigmoid_shaper( (saturation - 0.4) / 0.2);
	float addedGlow = 1 + glow_fwd( ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
	ColorAP0 *= addedGlow;
#endif

#if 1
	// --- Red modifier --- //
	const float RRT_RED_SCALE = 0.82;
	const float RRT_RED_PIVOT = 0.03;
	const float RRT_RED_HUE = 0;
	const float RRT_RED_WIDTH = 135;
	float hue = rgb_2_hue( ColorAP0 );
	float centeredHue = center_hue( hue, RRT_RED_HUE );
	float hueWeight = Square( smoothstep( 0, 1, 1 - abs( 2 * centeredHue / RRT_RED_WIDTH ) ) );
		
	ColorAP0.r += hueWeight * saturation * (RRT_RED_PIVOT - ColorAP0.r) * (1. - RRT_RED_SCALE);
#endif
	
	// Use ACEScg primaries as working space
	float3 WorkingColor = mul( AP0_2_AP1_MAT, ColorAP0 );

	WorkingColor = max( 0, WorkingColor );

	// Pre desaturate
	WorkingColor = lerp( dot( WorkingColor, AP1_RGB2Y ), WorkingColor, 0.96 );
	
	const half ToeScale			= 1 + FilmBlackClip - FilmToe;
	const half ShoulderScale	= 1 + FilmWhiteClip - FilmShoulder;
	
	const float InMatch = 0.18;
	const float OutMatch = 0.18;

	float ToeMatch;
	if( FilmToe > 0.8 )
	{
		// 0.18 will be on straight segment
		ToeMatch = ( 1 - FilmToe  - OutMatch ) / FilmSlope + log10( InMatch );
	}
	else
	{
		// 0.18 will be on toe segment

		// Solve for ToeMatch such that input of InMatch gives output of OutMatch.
		const float bt = ( OutMatch + FilmBlackClip ) / ToeScale - 1;
		ToeMatch = log10( InMatch ) - 0.5 * log( (1+bt)/(1-bt) ) * (ToeScale / FilmSlope);
	}

	float StraightMatch = ( 1 - FilmToe ) / FilmSlope - ToeMatch;
	float ShoulderMatch = FilmShoulder / FilmSlope - StraightMatch;
	
	half3 LogColor = log10( WorkingColor );
	half3 StraightColor = FilmSlope * ( LogColor + StraightMatch );
	
	half3 ToeColor		= (    -FilmBlackClip ) + (2 *      ToeScale) / ( 1 + exp( (-2 * FilmSlope /      ToeScale) * ( LogColor -      ToeMatch ) ) );
	half3 ShoulderColor	= ( 1 + FilmWhiteClip ) - (2 * ShoulderScale) / ( 1 + exp( ( 2 * FilmSlope / ShoulderScale) * ( LogColor - ShoulderMatch ) ) );

	ToeColor		= LogColor <      ToeMatch ?      ToeColor : StraightColor;
	ShoulderColor	= LogColor > ShoulderMatch ? ShoulderColor : StraightColor;

	half3 t = saturate( ( LogColor - ToeMatch ) / ( ShoulderMatch - ToeMatch ) );
	t = ShoulderMatch < ToeMatch ? 1 - t : t;
	t = (3-2*t)*t*t;
	half3 ToneColor = lerp( ToeColor, ShoulderColor, t );

	// Post desaturate
	ToneColor = lerp( dot( float3(ToneColor), AP1_RGB2Y ), ToneColor, 0.93 );

	// Returning positive AP1 values
	return max( 0, ToneColor );
}

half3 FilmToneMapInverse( half3 ToneColor ) 
{
	const float3x3 sRGB_2_AP1 = mul( XYZ_2_AP1_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );
	const float3x3 AP1_2_sRGB = mul( XYZ_2_sRGB_MAT, mul( D60_2_D65_CAT, AP1_2_XYZ_MAT ) );
	
	// Use ACEScg primaries as working space
	half3 WorkingColor = mul( sRGB_2_AP1, saturate( ToneColor ) );

	WorkingColor = max( 0, WorkingColor );
	
	// Post desaturate
	WorkingColor = lerp( dot( WorkingColor, AP1_RGB2Y ), WorkingColor, 1.0 / 0.93 );

	half3 ToeColor		= 0.374816 * pow( 0.9 / min( WorkingColor, 0.8 ) - 1, -0.588729 );
	half3 ShoulderColor	= 0.227986 * pow( 1.56 / ( 1.04 - WorkingColor ) - 1, 1.02046 );

	half3 t = saturate( ( WorkingColor - 0.35 ) / ( 0.45 - 0.35 ) );
	t = (3-2*t)*t*t;
	half3 LinearColor = lerp( ToeColor, ShoulderColor, t );

	// Pre desaturate
	LinearColor = lerp( dot( LinearColor, AP1_RGB2Y ), LinearColor, 1.0 / 0.96 );

	LinearColor = mul( AP1_2_sRGB, LinearColor );

	// Returning positive sRGB values
	return max( 0, LinearColor );
}

//
// ACES sRGB D65 Output Transform - Forward and Inverse
//  Input is scene-referred linear values in the sRGB gamut
//  Output is output-referred linear values in the sRGB gamut
//
float3 ACESOutputTransformsRGBD65( float3 SceneReferredLinearsRGBColor )
{
	const float3x3 sRGB_2_AP0 = mul( XYZ_2_AP0_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );

	float3 aces = mul( sRGB_2_AP0, SceneReferredLinearsRGBColor * 1.5 );
	float3 oces = RRT( aces );
	float3 OutputReferredLinearsRGBColor =  ODT_sRGB_D65( oces );
	return OutputReferredLinearsRGBColor;
}

float3 InverseACESOutputTransformsRGBD65( float3 OutputReferredLinearsRGBColor )
{
	const float3x3 AP0_2_sRGB = mul( XYZ_2_sRGB_MAT, mul( D60_2_D65_CAT, AP0_2_XYZ_MAT ) );

	float3 oces = Inverse_ODT_sRGB_D65( OutputReferredLinearsRGBColor );
	float3 aces = Inverse_RRT( oces );
	float3 SceneReferredLinearsRGBColor = mul( AP0_2_sRGB, aces ) * 0.6666;

	return SceneReferredLinearsRGBColor;
}

//
// ACES D65 1000 nit Output Transform - Forward
//  Input is scene-referred linear values in the sRGB gamut
//  Output is output-referred linear values in the AP1 gamut
//
float3 ACESOutputTransforms1000( float3 SceneReferredLinearsRGBColor )
{
	const float3x3 sRGB_2_AP0 = mul( XYZ_2_AP0_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );

	float3 aces = mul( sRGB_2_AP0, SceneReferredLinearsRGBColor * 1.5 );
	float3 oces = RRT( aces );
	float3 OutputReferredLinearAP1Color = ODT_1000nits( oces );
	return OutputReferredLinearAP1Color;
}

//
// ACES D65 2000 nit Output Transform - Forward
//  Input is scene-referred linear values in the sRGB gamut
//  Output is output-referred linear values in the AP1 gamut
//
float3 ACESOutputTransforms2000( float3 SceneReferredLinearsRGBColor )
{
	const float3x3 sRGB_2_AP0 = mul( XYZ_2_AP0_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );

	float3 aces = mul( sRGB_2_AP0, SceneReferredLinearsRGBColor * 1.5 );
	float3 oces = RRT( aces );
	float3 OutputReferredLinearAP1Color = ODT_2000nits( oces );
	return OutputReferredLinearAP1Color;
}

static const float3x3 GamutMappingIdentityMatrix = { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 };

//
// Gamut conversion matrices
//
float3x3 OuputGamutMappingMatrix( uint OutputGamut )
{
	// Gamut mapping matrices used later
	const float3x3 AP1_2_sRGB    = mul( XYZ_2_sRGB_MAT, mul( D60_2_D65_CAT, AP1_2_XYZ_MAT ) );
	const float3x3 AP1_2_DCI_D65 = mul( XYZ_2_P3D65_MAT, mul( D60_2_D65_CAT, AP1_2_XYZ_MAT ) );
	const float3x3 AP1_2_Rec2020 = mul( XYZ_2_Rec2020_MAT, mul( D60_2_D65_CAT, AP1_2_XYZ_MAT ) );

	// Set gamut mapping matrix 
	// 0 = sRGB - D65
	// 1 = P3 - D65
	// 2 = Rec.2020 - D65
	// 3 = ACES AP0 - D60
	// 4 = ACES AP1 - D60

	if( OutputGamut == 1 )
		return AP1_2_DCI_D65;
	else if( OutputGamut == 2 )
		return AP1_2_Rec2020;
	else if( OutputGamut == 3 )
		return AP1_2_AP0_MAT;
	else if( OutputGamut == 4 )
		return GamutMappingIdentityMatrix;
	else
		return AP1_2_sRGB;
}

float3x3 OuputInverseGamutMappingMatrix( uint OutputGamut )
{
	// Gamut mapping matrices used later
	const float3x3 sRGB_2_AP1    = mul( XYZ_2_AP1_MAT, mul( D65_2_D60_CAT, sRGB_2_XYZ_MAT ) );
	const float3x3 DCI_D65_2_AP1 = mul( XYZ_2_AP1_MAT, mul( D65_2_D60_CAT, P3D65_2_XYZ_MAT ) );
	const float3x3 Rec2020_2_AP1 = mul( XYZ_2_AP1_MAT, mul( D65_2_D60_CAT, Rec2020_2_XYZ_MAT ) );

	// Set gamut mapping matrix 
	// 0 = sRGB - D65
	// 1 = P3 (DCI) - D65
	// 2 = Rec.2020 - D65
	// 3 = ACES AP0 - D60
	float3x3 GamutMappingMatrix = sRGB_2_AP1;
	if( OutputGamut == 1 )
		GamutMappingMatrix = DCI_D65_2_AP1;
	else if( OutputGamut == 2 )
		GamutMappingMatrix = Rec2020_2_AP1;
	else if( OutputGamut == 3 )
		GamutMappingMatrix = AP0_2_AP1_MAT;

	return GamutMappingMatrix;
}

float3 ST2084ToScRGB(float3 Color, uint OutputDevice)
{
	// Nvidia HDR encoding - Remove PQ, convert to linear scRGB
	const float3x3 AP1_2_sRGB = mul(XYZ_2_sRGB_MAT, AP1_2_XYZ_MAT);
	const float WhitePoint = 80.f;

	// 1000.f nit display
	float MaxODTNits = 1000.0f;
	float MinODTNits = 0.0001f;

	if (OutputDevice == 4 || OutputDevice == 6)
	{
		// 2000 nit display
		MaxODTNits = 2000.0f;
		MinODTNits = 0.005f;
	}

	float3 OutColor = ST2084ToLinear(Color);
		
	OutColor = clamp(OutColor, MinODTNits, MaxODTNits);
	OutColor.x = Y_2_linCV(OutColor.x, MaxODTNits, MinODTNits);
	OutColor.y = Y_2_linCV(OutColor.y, MaxODTNits, MinODTNits);
	OutColor.z = Y_2_linCV(OutColor.z, MaxODTNits, MinODTNits);

	float scRGBScale = MaxODTNits / WhitePoint;
	OutColor = mul(AP1_2_sRGB, OutColor) * scRGBScale;

	return OutColor;
}