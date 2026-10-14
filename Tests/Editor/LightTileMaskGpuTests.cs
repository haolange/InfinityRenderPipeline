using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Runs Compute_LightTileMask (BuildTileMask) for 45 lights (two mask words) on a 7 x 5 tile grid and compares every
    // word with the CPU reference: bit b of word w is set when light w * 32 + b's rectangle covers the tile.
    public class LightTileMaskGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/LightTileMask/Compute_LightTileMask.compute";

        [Test]
        [Category("Unverified-0.5")]
        public void GpuMasks_MatchCpuReference()
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Compute shaders are required.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);

            const int lightCount = 45;
            var tileCount = new int2(7, 5);
            int wordCount = (lightCount + 31) / 32;
            var random = new Unity.Mathematics.Random(1234);
            var rects = new int4[lightCount];
            for (int i = 0; i < lightCount; i++)
            {
                int2 a = random.NextInt2(int2.zero, tileCount);
                int2 b = random.NextInt2(int2.zero, tileCount);
                rects[i] = new int4(math.min(a, b), math.max(a, b));
            }
            rects[3] = new int4(0, 0, tileCount.x - 1, tileCount.y - 1); // a light containing the camera covers every tile
            rects[40] = new int4(6, 4, 6, 4); // the last tile only

            int total = tileCount.x * tileCount.y * wordCount;
            using (var rectBuffer = new ComputeBuffer(lightCount, 16))
            using (var maskBuffer = new ComputeBuffer(total, 4))
            {
                rectBuffer.SetData(rects);
                maskBuffer.SetData(new uint[total]);
                int kernel = shader.FindKernel("BuildTileMask");
                shader.SetInt("LightTile_LocalCount", lightCount);
                shader.SetInt("LightTile_WordCount", wordCount);
                shader.SetVector("LightTile_TileCount", new Vector4(tileCount.x, tileCount.y, 0, 0));
                shader.SetBuffer(kernel, "SRV_LightTileRects", rectBuffer);
                shader.SetBuffer(kernel, "UAV_LightTileMasks", maskBuffer);
                shader.Dispatch(kernel, (total + 63) / 64, 1, 1);
                var masks = new uint[total];
                maskBuffer.GetData(masks);

                for (int ty = 0; ty < tileCount.y; ty++)
                {
                    for (int tx = 0; tx < tileCount.x; tx++)
                    {
                        for (int w = 0; w < wordCount; w++)
                        {
                            uint expected = 0;
                            for (int bit = 0; bit < 32 && w * 32 + bit < lightCount; bit++)
                            {
                                int4 r = rects[w * 32 + bit];
                                if (tx >= r.x && ty >= r.y && tx <= r.z && ty <= r.w) expected |= 1u << bit;
                            }
                            int index = (ty * tileCount.x + tx) * wordCount + w;
                            Assert.AreEqual(expected, masks[index], $"tile ({tx}, {ty}) word {w}");
                        }
                    }
                }
                Assert.AreNotEqual(0u, masks[0] & (1u << 3), "The full-screen light covers the first tile.");
            }
        }
    }
}
