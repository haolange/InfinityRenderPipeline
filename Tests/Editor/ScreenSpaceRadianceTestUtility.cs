using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    internal static class ScreenSpaceRadianceTestUtility
    {
        internal static RenderTexture Target(int width, int height, bool mips = false, bool halfPrecision = false)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = halfPrecision ? GraphicsFormat.R16G16_SFloat : GraphicsFormat.R32G32_SFloat,
                depthBufferBits = 0,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = mips,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Assert.IsTrue(texture.Create());
            Assert.AreEqual(descriptor.graphicsFormat, texture.graphicsFormat);
            return texture;
        }

        internal static Texture2DArray Source(int width, int height, bool mips = false, bool halfPrecision = false)
        {
            return new Texture2DArray(width, height, 2, halfPrecision ? TextureFormat.RGHalf : TextureFormat.RGFloat, mips, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        internal static Texture2DArray Source(int width, int height, Func<int, int, Color> color)
        {
            Texture2DArray texture = Source(width, height);
            var values = new Color[width * height];
            for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
                values[y * width + x] = color(x, y);
            Write(texture, values, 0);
            texture.Apply(false, false);
            return texture;
        }

        internal static void Write(Texture2DArray texture, Color[] values, int mip)
        {
            for (int layer = 0; layer < 2; ++layer)
            {
                var plane = new Vector2[values.Length];
                for (int i = 0; i < values.Length; ++i)
                    plane[i] = layer == 0 ? new Vector2(values[i].r, values[i].g) : new Vector2(values[i].b, values[i].a);
                if (texture.graphicsFormat == GraphicsFormat.R16G16_SFloat)
                    texture.SetPixelData(Array.ConvertAll(plane, c => new half2((half)c.x, (half)c.y)), mip, layer);
                else
                    texture.SetPixelData(plane, mip, layer);
            }
        }

        internal static float[] Read(RenderTexture texture, int mip = 0)
        {
            var request = AsyncGPUReadback.Request(texture, mip);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError);
            return Unpack(request, texture.graphicsFormat == GraphicsFormat.R16G16_SFloat);
        }

        internal static float[] Unpack(AsyncGPUReadbackRequest request, bool halfPrecision = false)
        {
            var values = new float[request.width * request.height * 4];
            for (int layer = 0; layer < 2; ++layer)
            {
                int channelOffset = layer * 2;
                if (halfPrecision)
                {
                    var plane = request.GetData<half2>(layer);
                    for (int i = 0; i < plane.Length; ++i)
                    {
                        values[i * 4 + channelOffset] = (float)plane[i].x;
                        values[i * 4 + channelOffset + 1] = (float)plane[i].y;
                    }
                }
                else
                {
                    var plane = request.GetData<Vector2>(layer);
                    for (int i = 0; i < plane.Length; ++i)
                    {
                        values[i * 4 + channelOffset] = plane[i].x;
                        values[i * 4 + channelOffset + 1] = plane[i].y;
                    }
                }
            }
            return values;
        }
    }
}
