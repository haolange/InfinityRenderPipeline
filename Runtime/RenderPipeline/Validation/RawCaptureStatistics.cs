using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    internal sealed class RawCaptureStatistics
    {
        public long samples, nonFinite, roiSamples;
        public double minimum, maximum, mean;
        public bool hasFiniteRoiSamples;

        internal static RawCaptureStatistics Compute(byte[] bytes, GraphicsFormat format, int width, int height, RectInt roi)
        {
            int channels, stride, encoding;
            switch (format)
            {
                case GraphicsFormat.R16_SFloat: channels = 1; stride = 2; encoding = 16; break;
                case GraphicsFormat.R16G16_SFloat: channels = 2; stride = 4; encoding = 16; break;
                case GraphicsFormat.R16G16B16A16_SFloat: channels = 4; stride = 8; encoding = 16; break;
                case GraphicsFormat.R32_SFloat:
                case GraphicsFormat.D32_SFloat: channels = 1; stride = 4; encoding = 32; break;
                case GraphicsFormat.R32G32_SFloat: channels = 2; stride = 8; encoding = 32; break;
                case GraphicsFormat.R32G32B32A32_SFloat: channels = 4; stride = 16; encoding = 32; break;
                case GraphicsFormat.B10G11R11_UFloatPack32: channels = 3; stride = 4; encoding = 11; break;
                case GraphicsFormat.R8_UNorm: channels = 1; stride = 1; encoding = 8; break;
                case GraphicsFormat.R8G8B8A8_UNorm:
                case GraphicsFormat.R8G8B8A8_SRGB:
                case GraphicsFormat.B8G8R8A8_UNorm:
                case GraphicsFormat.B8G8R8A8_SRGB: channels = 4; stride = 4; encoding = 8; break;
                default: throw new NotSupportedException("Raw capture decoder does not support " + format);
            }
            if (bytes.LongLength != (long)width * height * stride)
                throw new InvalidOperationException("Raw GPU byte count does not match the recorded descriptor.");
            var result = new RawCaptureStatistics();
            double sum = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int pixel = (y * width + x) * stride;
                for (int channel = 0; channel < channels; channel++)
                {
                    double value;
                    if (encoding == 32) value = BitConverter.ToSingle(bytes, pixel + channel * 4);
                    else if (encoding == 16) value = DecodeFloat(BitConverter.ToUInt16(bytes, pixel + channel * 2), 10, true);
                    else if (encoding == 11)
                    {
                        uint packed = BitConverter.ToUInt32(bytes, pixel);
                        value = channel == 0 ? DecodeFloat(packed & 2047, 6, false) :
                            channel == 1 ? DecodeFloat((packed >> 11) & 2047, 6, false) : DecodeFloat(packed >> 22, 5, false);
                    }
                    else value = bytes[pixel + channel] / 255.0;
                    result.samples++;
                    if (double.IsNaN(value) || double.IsInfinity(value)) { result.nonFinite++; continue; }
                    if (!roi.Contains(new Vector2Int(x, y))) continue;
                    if (!result.hasFiniteRoiSamples) result.minimum = result.maximum = value;
                    result.hasFiniteRoiSamples = true;
                    result.minimum = Math.Min(result.minimum, value); result.maximum = Math.Max(result.maximum, value);
                    result.roiSamples++; sum += value;
                }
            }
            if (result.roiSamples > 0) result.mean = sum / result.roiSamples;
            return result;
        }
        static double DecodeFloat(uint value, int mantissaBits, bool signed)
        {
            uint mantissa = value & ((1u << mantissaBits) - 1);
            uint exponent = (value >> mantissaBits) & 31;
            double sign = signed && (value & (1u << (mantissaBits + 5))) != 0 ? -1 : 1;
            if (exponent == 31) return mantissa == 0 ? sign * double.PositiveInfinity : double.NaN;
            if (exponent == 0) return sign * mantissa * Math.Pow(2, -14 - mantissaBits);
            return sign * (1 + mantissa / (double)(1u << mantissaBits)) * Math.Pow(2, (int)exponent - 15);
        }
    }
}
