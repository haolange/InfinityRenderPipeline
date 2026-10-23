using System;
using UnityEngine;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Result in the buffer's recorded encoding. DisplayColor is display-linear and can contain HDR values above 1.
    [Serializable]
    internal struct FImageComparison
    {
        public bool comparable;
        public string reason;
        public float rmse;
        public float maxError;
        // Pixels whose largest channel difference exceeds the per-pixel threshold, over all pixels.
        public float outlierFraction;
        public int pixels;
        public int nonFinitePixels;

        public bool Passes(float maxRmse, float maxOutlierFraction) => comparable && rmse <= maxRmse && outlierFraction <= maxOutlierFraction;
    }

    /// <summary>
    /// Pixel comparison of raw RenderCaptureService readbacks (.bin) in their GraphicsFormat: 8-bit UNorm / sRGB (as
    /// stored), 16-bit half and 32-bit float RGBA. Other formats compare only when byte-identical.
    /// </summary>
    internal static class ImageComparison
    {
        public static int BytesPerPixel(string format)
        {
            switch (format)
            {
                case "R8G8B8A8_UNorm":
                case "R8G8B8A8_SRGB":
                case "B8G8R8A8_UNorm":
                case "B8G8R8A8_SRGB":
                    return 4;
                case "R16G16B16A16_SFloat":
                    return 8;
                case "R32G32B32A32_SFloat":
                    return 16;
                default:
                    return 0;
            }
        }

        public static Vector4 Decode(byte[] data, int pixel, string format)
        {
            switch (BytesPerPixel(format))
            {
                case 4:
                {
                    int o = pixel * 4;
                    return new Vector4(data[o], data[o + 1], data[o + 2], data[o + 3]) / 255.0f;
                }
                case 8:
                {
                    int o = pixel * 8;
                    return new Vector4(Mathf.HalfToFloat(BitConverter.ToUInt16(data, o)), Mathf.HalfToFloat(BitConverter.ToUInt16(data, o + 2)),
                        Mathf.HalfToFloat(BitConverter.ToUInt16(data, o + 4)), Mathf.HalfToFloat(BitConverter.ToUInt16(data, o + 6)));
                }
                case 16:
                {
                    int o = pixel * 16;
                    return new Vector4(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8), BitConverter.ToSingle(data, o + 12));
                }
                default:
                    throw new NotSupportedException(format);
            }
        }

        public static FImageComparison Compare(byte[] baseline, byte[] candidate, string format, int width, int height, float pixelThreshold)
        {
            var result = new FImageComparison();
            long pixelCount = (long)width * height;
            if (width <= 0 || height <= 0 || pixelCount > int.MaxValue || !Finite(pixelThreshold) || pixelThreshold < 0)
            {
                result.reason = "invalid dimensions or pixel threshold";
                return result;
            }
            result.pixels = (int)pixelCount;
            if (baseline == null || candidate == null)
            {
                result.reason = "missing data";
                return result;
            }
            if (baseline.Length == 0 || candidate.Length == 0)
            {
                result.reason = "empty data";
                return result;
            }
            int bpp = BytesPerPixel(format);
            if (bpp == 0)
            {
                result.comparable = baseline.Length == candidate.Length;
                result.reason = result.comparable ? "byte compare" : "size mismatch";
                bool identical = result.comparable && baseline.AsSpan().SequenceEqual(candidate);
                result.outlierFraction = identical ? 0 : 1;
                result.maxError = identical ? 0 : 1;
                result.rmse = identical ? 0 : 1;
                return result;
            }
            long expected = (long)width * height * bpp;
            if (baseline.LongLength != expected || candidate.LongLength != expected)
            {
                result.reason = $"expected {expected} bytes, baseline {baseline.LongLength}, candidate {candidate.LongLength}";
                return result;
            }

            double sumSquares = 0;
            int outliers = 0;
            float maxError = 0;
            int count = width * height;
            for (int p = 0; p < count; p++)
            {
                Vector4 a = Decode(baseline, p, format);
                Vector4 b = Decode(candidate, p, format);
                if (!Finite(a.x) || !Finite(a.y) || !Finite(a.z) || !Finite(a.w) ||
                    !Finite(b.x) || !Finite(b.y) || !Finite(b.z) || !Finite(b.w)) result.nonFinitePixels++;
                // Alpha is ignored: display alpha carries no image content.
                float dr = Difference(a.x, b.x), dg = Difference(a.y, b.y), db = Difference(a.z, b.z);
                float pixelError = Mathf.Max(dr, Mathf.Max(dg, db));
                sumSquares += (dr * dr + dg * dg + db * db) / 3.0;
                maxError = Mathf.Max(maxError, pixelError);
                if (pixelError > pixelThreshold) outliers++;
            }
            result.comparable = result.nonFinitePixels == 0;
            if (!result.comparable) result.reason = $"non-finite data in {result.nonFinitePixels} pixel(s)";
            result.rmse = (float)Math.Sqrt(sumSquares / Math.Max(1, count));
            result.maxError = maxError;
            result.outlierFraction = count > 0 ? (float)outliers / count : 0;
            return result;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Retain diagnostic statistics for invalid images, but non-finite data can never pass the comparison.
        static float Difference(float a, float b)
        {
            if (float.IsNaN(a) || float.IsNaN(b) || float.IsInfinity(a) || float.IsInfinity(b)) return 1.0f;
            return Mathf.Abs(a - b);
        }
    }
}
