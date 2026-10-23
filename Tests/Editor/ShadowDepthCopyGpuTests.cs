using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowDepthCopyGpuTests
    {
        static RenderTexture Depth(int width, int height)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.None,
                depthStencilFormat = GraphicsFormat.D32_SFloat,
                msaaSamples = 1
            };
            var texture = new RenderTexture(descriptor);
            try
            {
                Assert.IsTrue(texture.Create());
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        static void Copy(CommandBuffer commands, Material material, Mesh triangle, int pass,
            Texture source, RenderTexture destination, RectInt sourceRect, RectInt destinationRect)
        {
            commands.SetRenderTarget(destination, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            commands.SetViewport(new Rect(destinationRect.x, destinationRect.y, destinationRect.width, destinationRect.height));
            commands.SetGlobalTexture("_InfinitySourceDepth", source);
            commands.SetGlobalVector("_InfinityDepthCopyOffset", LocalShadowPassUtilityData.DepthCopyOffset(
                new Vector4(sourceRect.x, sourceRect.y, sourceRect.width, sourceRect.height),
                new Vector4(destinationRect.x, destinationRect.y, destinationRect.width, destinationRect.height)));
            commands.DrawMesh(triangle, Matrix4x4.identity, material, 0, pass);
        }

        static void ExpectedCopy(float[] source, int sourceWidth, int sourceHeight, float[] destination,
            int destinationWidth, int destinationHeight, RectInt sourceRect, RectInt destinationRect)
        {
            // CPU oracle indexes the raw physical rows, independently of the production offset helper.
            int sourceY = sourceRect.y;
            int destinationY = destinationRect.y;
            for (int y = 0; y < sourceRect.height; ++y)
                for (int x = 0; x < sourceRect.width; ++x)
                    destination[(destinationY + y) * destinationWidth + destinationRect.x + x] =
                        source[(sourceY + y) * sourceWidth + sourceRect.x + x];
        }

        static float[] ReadDepth(RenderTexture depth, ComputeShader readback)
        {
            var output = new RenderTexture(depth.width, depth.height, 0, GraphicsFormat.R32_SFloat)
            {
                enableRandomWrite = true
            };
            try
            {
                Assert.IsTrue(output.Create());
                int kernel = readback.FindKernel("CaptureDepth");
                readback.SetVector("CaptureSize", new Vector4(depth.width, depth.height, 0, 0));
                readback.SetTexture(kernel, "SRV_Depth", depth);
                readback.SetTexture(kernel, "UAV_Depth", output);
                readback.Dispatch(kernel, (depth.width + 7) / 8, (depth.height + 7) / 8, 1);
                var request = AsyncGPUReadback.Request(output);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, "Depth readback");
                return request.GetData<float>().ToArray();
            }
            finally
            {
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void DepthTiles_RoundTripAcrossDifferentAtlasSizes_PreserveEveryNeighbourPixel()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback are required.");
            Material material = null;
            Mesh triangle = null;
            Texture2D source = null, sentinel = null;
            RenderTexture frame = null, cache = null, restored = null, referenceCache = null, referenceRestored = null;
            ComputeShader readback = null;
            CommandBuffer commands = null;
            try
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/com.infinity.render-pipeline/Shaders/Utility/DrawFullScreen.shader"));
                triangle = new Mesh();
                triangle.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 3, 0), new Vector3(3, -1, 0) };
                triangle.triangles = new[] { 0, 1, 2 };
                var sourcePixels = new float[32 * 24];
                for (int y = 0; y < 24; ++y)
                    for (int x = 0; x < 32; ++x) sourcePixels[y * 32 + x] = .1f + x / 256f + y / 128f;
                source = new Texture2D(32, 24, TextureFormat.RFloat, false, true);
                source.SetPixelData(sourcePixels, 0);
                source.Apply(false);
                var sentinelPixels = new float[48 * 40];
                Array.Fill(sentinelPixels, .875f);
                sentinel = new Texture2D(48, 40, TextureFormat.RFloat, false, true);
                sentinel.SetPixelData(sentinelPixels, 0);
                sentinel.Apply(false);
                frame = Depth(32, 24);
                cache = Depth(48, 40);
                restored = Depth(32, 24);
                referenceCache = new RenderTexture(48, 40, 0, GraphicsFormat.R32_SFloat);
                referenceRestored = new RenderTexture(32, 24, 0, GraphicsFormat.R32_SFloat);
                Assert.IsTrue(referenceCache.Create());
                Assert.IsTrue(referenceRestored.Create());
                readback = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Runtime/Resources/Compute_CaptureDepth.compute"));
                commands = new CommandBuffer();
                int pass = material.FindPass("RasterCopyShadowDepthTile");
                Assert.GreaterOrEqual(pass, 0);
                Copy(commands, material, triangle, pass, source, frame, new RectInt(0, 0, 32, 24), new RectInt(0, 0, 32, 24));
                Copy(commands, material, triangle, pass, sentinel, cache, new RectInt(0, 0, 48, 40), new RectInt(0, 0, 48, 40));
                Copy(commands, material, triangle, pass, source, restored, new RectInt(0, 0, 32, 24), new RectInt(0, 0, 32, 24));
                var from = new[] { new RectInt(3, 5, 8, 6), new RectInt(17, 13, 7, 5) };
                var slots = new[] { new RectInt(21, 29, 8, 6), new RectInt(2, 3, 7, 5) };
                var targets = new[] { new RectInt(14, 2, 8, 6), new RectInt(1, 17, 7, 5) };
                var expectedCache = (float[])sentinelPixels.Clone();
                var expectedRestored = (float[])sourcePixels.Clone();
                for (int i = 0; i < 2; ++i)
                {
                    Copy(commands, material, triangle, pass, frame, cache, from[i], slots[i]);
                    ExpectedCopy(sourcePixels, 32, 24, expectedCache, 48, 40, from[i], slots[i]);
                }
                for (int i = 0; i < 2; ++i)
                {
                    Copy(commands, material, triangle, pass, cache, restored, slots[i], targets[i]);
                    ExpectedCopy(expectedCache, 48, 40, expectedRestored, 32, 24, slots[i], targets[i]);
                }
                // Legal color region copies are an independent GPU oracle for the atlas pixel convention.
                commands.CopyTexture(sentinel, referenceCache);
                commands.CopyTexture(source, referenceRestored);
                for (int i = 0; i < 2; ++i)
                    commands.CopyTexture(source, 0, 0, from[i].x, from[i].y, from[i].width, from[i].height,
                        referenceCache, 0, 0, slots[i].x, slots[i].y);
                for (int i = 0; i < 2; ++i)
                    commands.CopyTexture(referenceCache, 0, 0, slots[i].x, slots[i].y, slots[i].width, slots[i].height,
                        referenceRestored, 0, 0, targets[i].x, targets[i].y);
                Graphics.ExecuteCommandBuffer(commands);
                var colorCacheRequest = AsyncGPUReadback.Request(referenceCache);
                var colorRestoredRequest = AsyncGPUReadback.Request(referenceRestored);
                colorCacheRequest.WaitForCompletion();
                colorRestoredRequest.WaitForCompletion();
                Assert.IsFalse(colorCacheRequest.hasError || colorRestoredRequest.hasError);
                var colorCache = colorCacheRequest.GetData<float>().ToArray();
                var colorRestored = colorRestoredRequest.GetData<float>().ToArray();
                var actualFrame = ReadDepth(frame, readback);
                var actualCache = ReadDepth(cache, readback);
                var actualRestored = ReadDepth(restored, readback);
                for (int i = 0; i < actualFrame.Length; ++i) Assert.AreEqual(sourcePixels[i], actualFrame[i], 1e-7f, "seed " + i);
                for (int i = 0; i < actualCache.Length; ++i)
                {
                    Assert.AreEqual(expectedCache[i], colorCache[i], 1e-7f, "color oracle cache " + i);
                    Assert.AreEqual(colorCache[i], actualCache[i], 1e-7f, "depth cache " + i);
                }
                for (int i = 0; i < actualRestored.Length; ++i)
                {
                    Assert.AreEqual(expectedRestored[i], colorRestored[i], 1e-7f, "color oracle restored " + i);
                    Assert.AreEqual(colorRestored[i], actualRestored[i], 1e-7f, "depth restored " + i);
                }
            }
            finally
            {
                commands?.Release();
                foreach (var texture in new[] { frame, cache, restored, referenceCache, referenceRestored })
                {
                    if (texture == null) continue;
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                foreach (var resource in new UnityEngine.Object[] { source, sentinel, material, triangle, readback })
                    if (resource != null) UnityEngine.Object.DestroyImmediate(resource);
            }
        }
    }
}
