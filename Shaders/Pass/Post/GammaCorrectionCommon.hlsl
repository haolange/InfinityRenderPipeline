// Copyright Epic Games, Inc. All Rights Reserved.
//
// Generic log lin transforms
//
float3 LogToLin( float3 LogColor )
{
	const float LinearRange = 14;
	const float LinearGrey = 0.18;
	const float ExposureGrey = 444;

	// Using stripped down, 'pure log', formula. Parameterized by grey points and dynamic range covered.
	float3 LinearColor = exp2( ( LogColor - ExposureGrey / 1023.0 ) * LinearRange ) * LinearGrey;
	//float3 LinearColor = 2 * ( pow(10.0, ((LogColor - 0.616596 - 0.03) / 0.432699)) - 0.037584 );	// SLog
	//float3 LinearColor = ( pow( 10, ( 1023 * LogColor - 685 ) / 300) - .0108 ) / (1 - .0108);	// Cineon
	//LinearColor = max( 0, LinearColor );

	return LinearColor;
}

float3 LinToLog( float3 LinearColor )
{
	const float LinearRange = 14;
	const float LinearGrey = 0.18;
	const float ExposureGrey = 444;

	// Using stripped down, 'pure log', formula. Parameterized by grey points and dynamic range covered.
	float3 LogColor = log2(LinearColor) / LinearRange - log2(LinearGrey) / LinearRange + ExposureGrey / 1023.0;	// scalar: 3log2 3mad
	//float3 LogColor = (log2(LinearColor) - log2(LinearGrey)) / LinearRange + ExposureGrey / 1023.0;
	//float3 LogColor = log2( LinearColor / LinearGrey ) / LinearRange + ExposureGrey / 1023.0;
	//float3 LogColor = (0.432699 * log10(0.5 * LinearColor + 0.037584) + 0.616596) + 0.03;	// SLog
	//float3 LogColor = ( 300 * log10( LinearColor * (1 - .0108) + .0108 ) + 685 ) / 1023;	// Cineon
	LogColor = saturate( LogColor );

	return LogColor;
}

//
// Pseudo-ACES 100 nit transforms
//
float
aces100nitFitInverseFloat(float x) 
{
	x = max(0.f, min(0.99f, x));
	//float c = ((-6.32456e-8*pow((-21510484096000.0*x*x + 26714646293200.0*x + 27735750507.0), 0.5) -0.146704*x + 0.0083284)) * rcp( x - 1.01654);
	float c = ( -0.632456 * sqrt( -0.21510484096 *x*x + 0.267146462932 * x + 0.00027735750507 ) - 0.146704 * x + 0.0083284 ) / ( x - 1.01654 );

	// Clamp to half float range
	return max(0.f, min(65504.f, c)); 	
}

float3
aces100nitFitInverse(float3 FilmColor) 
{
	float3 inverse;
	inverse.r = aces100nitFitInverseFloat(FilmColor.r);
	inverse.g = aces100nitFitInverseFloat(FilmColor.g);
	inverse.b = aces100nitFitInverseFloat(FilmColor.b);
	return inverse;
}

//
// Dolby PQ transforms
//
float3
ST2084ToLinear(float3 pq)
{
	const float m1 = 0.1593017578125;
	// = 2610. / 4096. * .25;
	const float m2 = 78.84375;
	// = 2523. / 4096. *  128;
	const float c1 = 0.8359375;
	// = 2392. / 4096. * 32 - 2413./4096.*32 + 1;
	const float c2 = 18.8515625;
	// = 2413. / 4096. * 32;
	const float c3 = 18.6875;
	// = 2392. / 4096. * 32;
	const float C = 10000.;

	float3 Np = pow( pq, 1./m2 );
	float3 L = Np - c1;
	L = max(0., L);
	L = L / (c2 - c3 * Np);
	L = pow( abs(L), 1./m1 );
	float3 P = L * C;

	return P;
}

float3
LinearToST2084(float3 lin)
{
	const float m1 = 0.1593017578125;
	// = 2610. / 4096. * .25;
	const float m2 = 78.84375;
	// = 2523. / 4096. *  128;
	const float c1 = 0.8359375;
	// = 2392. / 4096. * 32 - 2413./4096.*32 + 1;
	const float c2 = 18.8515625;
	// = 2413. / 4096. * 32;
	const float c3 = 18.6875;
	// = 2392. / 4096. * 32;
	const float C = 10000.;

	float3 L = lin/C;
	float3 Lm = pow(abs(L), m1);
	float3 N1 = ( c1 + c2 * Lm );
	float3 N2 = ( 1.0 + c3 * Lm );
	float3 N = N1 * rcp(N2);
	float3 P = pow( N, m2 );
	
	return P;
}