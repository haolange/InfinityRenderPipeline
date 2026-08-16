#ifndef _ColorUtilityInclude
#define _ColorUtilityInclude

// Named Rec709* rather than Luminance because UnityCG.cginc also declares Luminance(half3).
// A guarded redeclaration cannot work here: SSRTRayCast.hlsl pulls UnityCG.cginc transitively,
// so whichever file is included first wins and the other one is a redefinition error.
float Rec709Luminance(float3 linearRgb)
{
    return dot(linearRgb, float3(0.2126729, 0.7151522, 0.0721750));
}

float Rec709Luminance(float4 linearRgba)
{
    return Rec709Luminance(linearRgba.rgb);
}

#endif
