#ifndef _ETCCompressInclude
#define _ETCCompressInclude

#define BLOCK_MODE_INDIVIDUAL				0
#define BLOCK_MODE_DIFFERENTIAL				1
#define NUM_RGB_TABLES 8
#define NUM_ALPHA_TABLES 16

float4 alpha_distance_tables[16] =
{
    float4(2, 5, 8, 14),
    float4(2, 6, 9, 12),
    float4(1, 4, 7, 12),
    float4(1, 3, 5, 12),
    float4(2, 5, 7, 11),
    float4(2, 6, 8, 10),
    float4(3, 6, 7, 10),
    float4(2, 4, 7, 10),
    float4(1, 5, 7, 9),
    float4(1, 4, 7, 9),
    float4(1, 3, 7, 9),
    float4(1, 4, 6, 9),
    float4(2, 3, 6, 9),
    float4(0, 1, 2, 9),
    float4(3, 5, 7, 8),
    float4(2, 4, 6, 8)
};

float4 rgb_distance_tables[8] =
{
    float4(-8, -2, 2, 8),
    float4(-17, -5, 5, 17),
    float4(-29, -9, 9, 29),
    float4(-42, -13, 13, 42),
    float4(-60, -18, 18, 60),
    float4(-80, -24, 24, 80),
    float4(-106, -33, 33, 106),
    float4(-183, -47, 47, 183)
};

void SelectAlphaMod(in float SourceAlpha, in float EncodedAlpha, int IndexInTable, inout int SelectedIndex, inout float MinDif)
{
    float Dif = abs(EncodedAlpha - SourceAlpha);
    if (Dif < MinDif)
    {
        MinDif = Dif;
        SelectedIndex = IndexInTable;
    }
}


int3 FloatColorToUint555(in float3 FloatColor)
{
    float3 Scale = float3(31.f, 31.f, 31.f);
    float3 ColorScaled = round(saturate(FloatColor) * Scale);
    return int3((int) ColorScaled.r, (int) ColorScaled.g, (int) ColorScaled.b);
}

float3 ExpandColor444(in int3 Color444)
{
    int3 Color888 = (Color444 << 4) + Color444;
    return Color888 / 255.f;
}

float3 ExpandColor555(in int3 Color555)
{
    int3 Color888 = (Color555 << 3) + (Color555 >> 2);
    return Color888 / 255.f;
}

uint SwapEndian32(in uint x)
{
    return
		((x & 0x0000ff) << 24) |
		((x & 0x00ff00) << 8) |
		((x & 0xff0000) >> 8) |
		(x >> 24);
}

float Luminance(float3 LinearColor)
{
    return dot(LinearColor, float3(0.3, 0.59, 0.11));
}

float max3(float a, float b, float c)
{
    return max(a, max(b, c));
}

float min3(float a, float b, float c)
{
    return min(a, min(b, c));
}

uint SelectRGBTableIndex(float LuminanceR)
{
    uint Out = 7;
    float Range = (LuminanceR + LuminanceR * 0.1) * 255.0;
    
    if (Range < 8.0)
    {
        Out = 0;
    } else if (Range < 17.0) {
        Out = 1;
    } else if (Range < 29.0) {
        Out = 2;
    } else if (Range < 42.0) {
        Out = 3;
    } else if (Range < 60.0) {
        Out = 4;
    } else if (Range < 80.0) {
        Out = 5;
    } else if (Range < 106.0) {
        Out = 6;
    }
    return Out;
}


void FindPixelWeights(in float3 Block[16], in float3 BaseColor, in uint TableIdx, in int StartX, in int EndX, in int StartY, in int EndY, out uint SubBlockWeights)
{
	//int PIXEL_INDEX_ENCODE_TABLE[4] = { 3, 2, 0, 1 };
    SubBlockWeights = 0;
	
    float TableRangeMax = rgb_distance_tables[TableIdx].w / 255.f;
    float BaseLum = Luminance(BaseColor);

    for (int Y = StartY; Y < EndY; ++Y)
    {
        for (int X = StartX; X < EndX; ++X)
        {
            float3 OrigColor = Block[4 * Y + X];
            float Diff = Luminance(OrigColor) - BaseLum;
            int EncIndex = 0;
            if (Diff < 0.f)
            {
                EncIndex = (-Diff * 1.58) > TableRangeMax ? 3 : 2;
            }
            else
            {
                EncIndex = (Diff * 1.58) > TableRangeMax ? 1 : 0;
            }
			//int EncIndex = PIXEL_INDEX_ENCODE_TABLE[SelectedIndex];
            int IndexInBlock = X * 4 + Y;
            SubBlockWeights |= ((EncIndex & 1) << IndexInBlock) | ((EncIndex >> 1) << (16 + IndexInBlock));
        }
    }
}

uint2 CompressBlock_ETC2_RGB(in float3 Block[16])
{
	// Always use side-by-side mode (flip bit set to 0).
    uint FlipBit = 0;
	
    float3 BaseColor1_Float = (Block[0] + Block[1] + Block[4] + Block[5] + Block[8] + Block[9] + Block[12] + Block[13]) * 0.125;
    float3 BaseColor2_Float = (Block[2] + Block[3] + Block[6] + Block[7] + Block[10] + Block[11] + Block[14] + Block[15]) * 0.125;
		
    int3 BaseColor1 = FloatColorToUint555(BaseColor1_Float);
    int3 BaseColor2 = FloatColorToUint555(BaseColor2_Float);
    int3 Diff = BaseColor2 - BaseColor1;

    uint ColorBits;
    float3 BaseColor1_Quant, BaseColor2_Quant;

    uint BlockMode;
    int3 MinDiff = { -4, -4, -4 }, MaxDiff = { 3, 3, 3 };
    if (all(Diff >= MinDiff) && all(Diff <= MaxDiff))
    {
		// We can use differential mode.
        BlockMode = BLOCK_MODE_DIFFERENTIAL;
        ColorBits = ((Diff.b & 7) << 16) | (BaseColor1.b << 19) | ((Diff.g & 7) << 8) | (BaseColor1.g << 11) | (Diff.r & 7) | (BaseColor1.r << 3);
        BaseColor1_Quant = ExpandColor555(BaseColor1);
        BaseColor2_Quant = ExpandColor555(BaseColor2);
    }
    else
    {
		// We must use the lower precision individual mode.
        BlockMode = BLOCK_MODE_INDIVIDUAL;
        BaseColor1 >>= 1;
        BaseColor2 >>= 1;
        ColorBits = (BaseColor1.b << 20) | (BaseColor2.b << 16) | (BaseColor1.g << 12) | (BaseColor2.g << 8) | (BaseColor1.r << 4) | BaseColor2.r;
        BaseColor1_Quant = ExpandColor444(BaseColor1);
        BaseColor2_Quant = ExpandColor444(BaseColor2);
    }

    float l00 = Luminance(Block[0]);
    float l08 = Luminance(Block[8]);
    float l13 = Luminance(Block[13]);
    float LuminanceR1 = (max3(l00, l08, l13) - min3(l00, l08, l13)) * 0.5;
    uint SubBlock1TableIdx = SelectRGBTableIndex(LuminanceR1);
    uint SubBlock1Weights = 0;
    FindPixelWeights(Block, BaseColor1_Quant, SubBlock1TableIdx, 0, 2, 0, 4, SubBlock1Weights);

    float l02 = Luminance(Block[2]);
    float l10 = Luminance(Block[10]);
    float l15 = Luminance(Block[15]);
    float LuminanceR2 = (max3(l02, l10, l15) - min3(l02, l10, l15)) * 0.5;
    uint SubBlock2TableIdx = SelectRGBTableIndex(LuminanceR2);
    uint SubBlock2Weights = 0;
    FindPixelWeights(Block, BaseColor2_Quant, SubBlock2TableIdx, 2, 4, 0, 4, SubBlock2Weights);
	
	// Both these values need to be big-endian. We can build ModeBits directly in big-endian layout, but for IndexBits
	// it's too hard, so we'll just swap here.
    uint ModeBits = (SubBlock1TableIdx << 29) | (SubBlock2TableIdx << 26) | (BlockMode << 25) | (FlipBit << 24) | ColorBits;
    uint IndexBits = SwapEndian32(SubBlock1Weights | SubBlock2Weights);

    return uint2(ModeBits, IndexBits);
}

uint2 CompressBlock_ETC2_Alpha(in float BlockA[16])
{
    float MinAlpha = 1.f;
    float MaxAlpha = 0.f;
    for (int k = 0; k < 16; ++k)
    {
        float A = BlockA[k];
        MinAlpha = min(A, MinAlpha);
        MaxAlpha = max(A, MaxAlpha);
    }
	
    float AlphaRange = MaxAlpha - MinAlpha;
    const float MidRange = 20.f; // an average range in ALPHA_DISTANCE_TABLES
    float Multiplier = clamp(round(255.f * AlphaRange / MidRange), 1.f, 15.f);
	
    int TableIdx = 0;
    float4 TableValueNeg = float4(0, 0, 0, 0);
    float4 TableValuePos = float4(0, 0, 0, 0);
	
	// iterating through all tables to find a best fit is quite slow
	// instead guess the best table based on alpha range
    for (int i = 0; i < NUM_ALPHA_TABLES; ++i)
    {
        TableIdx = NUM_ALPHA_TABLES - 1 - i;
        TableValuePos = alpha_distance_tables[TableIdx];
				
        float TableRange = ((TableValuePos.w * 2 + 1) / 255.f) * Multiplier;
        float Dif = TableRange - AlphaRange;
        if (Dif >= 0.f)
        {
            break;
        }
    }
    TableValueNeg = -(TableValuePos + float4(1, 1, 1, 1));

	// make sure an exact value of MinAlpha can always be decoded from a BaseValue
    float BaseValue = MinAlpha - TableValueNeg.w;
	
    TableValueNeg = saturate(TableValueNeg + BaseValue.xxxx);
    TableValuePos = saturate(TableValuePos + BaseValue.xxxx);
    uint2 BlockWeights = 0;
	
    for (int PixelIndex = 0; PixelIndex < 16; ++PixelIndex)
    {
        float Alpha = BlockA[PixelIndex];
        int SelectedIndex = 0;
        float MinDif = 100000.f;
		
        if ((Alpha - TableValuePos.x) < 0.f)
        {
            SelectAlphaMod(Alpha, TableValueNeg.x, 0, SelectedIndex, MinDif);
            SelectAlphaMod(Alpha, TableValueNeg.y, 1, SelectedIndex, MinDif);
            SelectAlphaMod(Alpha, TableValueNeg.z, 2, SelectedIndex, MinDif);
            SelectAlphaMod(Alpha, TableValueNeg.w, 3, SelectedIndex, MinDif);
        }
        else
        {
            SelectAlphaMod(Alpha, TableValuePos.x, 4, SelectedIndex, MinDif);
            SelectAlphaMod(Alpha, TableValuePos.y, 5, SelectedIndex, MinDif);
            SelectAlphaMod(Alpha, TableValuePos.z, 6, SelectedIndex, MinDif);
            SelectAlphaMod(Alpha, TableValuePos.w, 7, SelectedIndex, MinDif);
        }

		// ETC uses column-major indexing for the pixels in a block...
        int TransposedIndex = (PixelIndex >> 2) | ((PixelIndex & 3) << 2);
        int StartBit = (15 - TransposedIndex) * 3;
        BlockWeights.x |= (StartBit < 32) ? SelectedIndex << StartBit : 0;
        int ShiftRight = (StartBit == 30) ? 2 : 0;
        int ShiftLeft = (StartBit >= 32) ? StartBit - 32 : 0;
        BlockWeights.y |= (StartBit >= 30) ? (SelectedIndex >> ShiftRight) << ShiftLeft : 0;
    }

    int MultiplierInt = round(Multiplier);
    int BaseValueInt = round(BaseValue * 255.f);
	
    uint2 AlphaBits;
    AlphaBits.x = SwapEndian32(BlockWeights.y | (TableIdx << 16) | (MultiplierInt << 20) | (BaseValueInt << 24));
    AlphaBits.y = SwapEndian32(BlockWeights.x);

    return AlphaBits;
}

uint4 CompressBlock_ETC2_RGBA(in float3 BlockRGB[16], in float BlockA[16])
{
    uint2 CompressedRGB = CompressBlock_ETC2_RGB(BlockRGB);
    uint2 CompressedAlpha = CompressBlock_ETC2_Alpha(BlockA);
    return uint4(CompressedAlpha, CompressedRGB);
}

#endif
