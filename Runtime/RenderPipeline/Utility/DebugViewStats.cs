using System;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    public struct DebugViewYCoCg
    {
        public float y;
        public float co;
        public float cg;
    }

    public struct DebugViewStatResult
    {
        public float meanR;
        public float meanG;
        public float meanB;
        public float stdR;
        public float stdG;
        public float stdB;
        public float lumaStddev;
        public float clipPercent;
        public float meanAbsCo;
        public float meanAbsCg;
        public float highPassEnergy;
        public int pixelCount;
        public int width;
        public int height;
        public bool hasLivenessRoi;
        public int roiX;
        public int roiY;
        public int roiW;
        public int roiH;
    }

    public static class DebugViewStats
    {
        public const float FloatClipThreshold = 0.995f;

        // Mirrors RGB2YCoCg in Shaders/ShaderLibrary/Common.hlsl (unnormalized).
        // Y = R+2G+B, Co = 2R-2B, Cg = -R+2G-B. Inverse divides each by 4.
        public static DebugViewYCoCg RGB2YCoCg(float r, float g, float b)
        {
            DebugViewYCoCg ycocg;
            ycocg.y = r + 2.0f * g + b;
            ycocg.co = 2.0f * r - 2.0f * b;
            ycocg.cg = -r + 2.0f * g - b;
            return ycocg;
        }

        public static float Rec709Luma(float r, float g, float b)
        {
            return 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;
        }

        public static DebugViewStatResult Compute(Color[] pixels, int width, int height, RectInt? excludeRoi = null)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "DebugViewStats requires a positive image size.");
            }

            if (pixels.Length < width * height)
            {
                throw new ArgumentException("DebugViewStats pixel buffer is smaller than width*height.");
            }

            DebugViewStatResult result = default;
            result.width = width;
            result.height = height;
            if (excludeRoi.HasValue)
            {
                RectInt roi = excludeRoi.Value;
                result.hasLivenessRoi = true;
                result.roiX = roi.x;
                result.roiY = roi.y;
                result.roiW = roi.width;
                result.roiH = roi.height;
            }

            double sumR = 0.0;
            double sumG = 0.0;
            double sumB = 0.0;
            double sumLuma = 0.0;
            double sumAbsCo = 0.0;
            double sumAbsCg = 0.0;
            int count = 0;
            int clipCount = 0;

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    if (IsExcluded(x, y, excludeRoi))
                    {
                        continue;
                    }

                    Color pixel = pixels[y * width + x];
                    sumR += pixel.r;
                    sumG += pixel.g;
                    sumB += pixel.b;
                    sumLuma += Rec709Luma(pixel.r, pixel.g, pixel.b);
                    DebugViewYCoCg ycocg = RGB2YCoCg(pixel.r, pixel.g, pixel.b);
                    sumAbsCo += mathAbs(ycocg.co);
                    sumAbsCg += mathAbs(ycocg.cg);
                    if (pixel.r >= FloatClipThreshold || pixel.g >= FloatClipThreshold || pixel.b >= FloatClipThreshold)
                    {
                        clipCount++;
                    }

                    count++;
                }
            }

            result.pixelCount = count;
            if (count == 0)
            {
                return result;
            }

            double inv = 1.0 / count;
            double meanR = sumR * inv;
            double meanG = sumG * inv;
            double meanB = sumB * inv;
            double meanLuma = sumLuma * inv;
            result.meanR = (float)meanR;
            result.meanG = (float)meanG;
            result.meanB = (float)meanB;
            result.meanAbsCo = (float)(sumAbsCo * inv);
            result.meanAbsCg = (float)(sumAbsCg * inv);
            result.clipPercent = (float)(clipCount * 100.0 * inv);

            double varR = 0.0;
            double varG = 0.0;
            double varB = 0.0;
            double varLuma = 0.0;
            double highPass = 0.0;
            int highPassCount = 0;

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    if (IsExcluded(x, y, excludeRoi))
                    {
                        continue;
                    }

                    Color pixel = pixels[y * width + x];
                    double dR = pixel.r - meanR;
                    double dG = pixel.g - meanG;
                    double dB = pixel.b - meanB;
                    double luma = Rec709Luma(pixel.r, pixel.g, pixel.b);
                    varR += dR * dR;
                    varG += dG * dG;
                    varB += dB * dB;
                    double dLuma = luma - meanLuma;
                    varLuma += dLuma * dLuma;

                    int xL = x > 0 ? x - 1 : x;
                    int xR = x + 1 < width ? x + 1 : x;
                    int yD = y > 0 ? y - 1 : y;
                    int yU = y + 1 < height ? y + 1 : y;
                    float lumaC = (float)luma;
                    float lumaL = Rec709LumaAt(pixels, width, xL, y);
                    float lumaRight = Rec709LumaAt(pixels, width, xR, y);
                    float lumaDn = Rec709LumaAt(pixels, width, x, yD);
                    float lumaUp = Rec709LumaAt(pixels, width, x, yU);
                    float laplacian = lumaL + lumaRight + lumaDn + lumaUp - 4.0f * lumaC;
                    highPass += mathAbs(laplacian);
                    highPassCount++;
                }
            }

            result.stdR = (float)Math.Sqrt(varR * inv);
            result.stdG = (float)Math.Sqrt(varG * inv);
            result.stdB = (float)Math.Sqrt(varB * inv);
            result.lumaStddev = (float)Math.Sqrt(varLuma * inv);
            result.highPassEnergy = highPassCount > 0 ? (float)(highPass / highPassCount) : 0.0f;
            return result;
        }

        static bool IsExcluded(int x, int y, RectInt? excludeRoi)
        {
            if (!excludeRoi.HasValue)
            {
                return false;
            }

            RectInt roi = excludeRoi.Value;
            return x >= roi.x && y >= roi.y && x < roi.x + roi.width && y < roi.y + roi.height;
        }

        static float Rec709LumaAt(Color[] pixels, int width, int x, int y)
        {
            Color pixel = pixels[y * width + x];
            return Rec709Luma(pixel.r, pixel.g, pixel.b);
        }

        static float mathAbs(float value)
        {
            return value < 0.0f ? -value : value;
        }

        static double mathAbs(double value)
        {
            return value < 0.0 ? -value : value;
        }
    }
}
