#ifndef INFINITY_OUTPUT_TRANSFORM_INCLUDED
#define INFINITY_OUTPUT_TRANSFORM_INCLUDED

float3 InfinityLinearToSRGB(float3 color)
{
    float3 lo = color * 12.92;
    float3 hi = pow(max(abs(color), 1e-6), 1.0 / 2.4) * 1.055 - 0.055;
    return color <= 0.0031308 ? lo : hi;
}

float3 InfinityLinearToST2084(float3 lin)
{
    const float m1 = 0.1593017578125;
    const float m2 = 78.84375;
    const float c1 = 0.8359375;
    const float c2 = 18.8515625;
    const float c3 = 18.6875;
    const float C = 10000.0;

    float3 L = max(lin, 0.0) / C;
    float3 Lm = pow(abs(L), m1);
    float3 N = (c1 + c2 * Lm) * rcp(1.0 + c3 * Lm);
    return pow(N, m2);
}

float InfinityLinearToHLGChannel(float E)
{
    const float a = 0.17883277;
    const float b = 0.28466892;
    const float c = 0.55991073;
    E = max(E, 0.0);
    if (E <= 1.0 / 12.0)
    {
        return sqrt(3.0 * E);
    }

    return a * log(12.0 * E - b) + c;
}

float3 InfinityLinearToHLG(float3 lin)
{
    return float3(InfinityLinearToHLGChannel(lin.r), InfinityLinearToHLGChannel(lin.g), InfinityLinearToHLGChannel(lin.b));
}

float3 InfinityRec709ToRec2020(float3 rec709)
{
    const float3x3 Rec709_2_Rec2020 =
    {
        0.6274040, 0.3292820, 0.0433136,
        0.0690970, 0.9195400, 0.0113612,
        0.0163914, 0.0880133, 0.8955950
    };
    return mul(Rec709_2_Rec2020, rec709);
}

float3 InfinityEncodeOutput(float3 color, int policy, int rec2020, float paperWhiteNits)
{
    if (rec2020 != 0) color = InfinityRec709ToRec2020(color);
    if (policy == 0) return InfinityLinearToSRGB(color);
    if (policy == 2) return InfinityLinearToST2084(color * paperWhiteNits);
    if (policy == 3) return InfinityLinearToHLG(color);
    return color;
}
#endif
