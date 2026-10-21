using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DeferredLightLoopGpuTests
    {
        struct ShadowSlice
        {
            public Vector4 rect, depth, moments;
        }
        struct ProbeRecord
        {
            public Vector4 row0, row1, row2, extents, capture, projection, parameters;
        }
        // The production kernel checks aggregate contribution across words; the exact-index probe and normal multi-tile frames complement it.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        public void ActualDeferredKernel_AccumulatesLocalLightsAcrossMaskWords(int localCount)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A compute device with GPU readback is required.");
            const int size = 16, directionalCount = 3;
            var owned = new List<UnityEngine.Object>();
            var buffers = new List<ComputeBuffer>();
            RenderTexture previous = RenderTexture.active;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/DeferredShading/Compute_DeferredShading.compute");
                Assert.IsNotNull(source);
                var shader = UnityEngine.Object.Instantiate(source);
                owned.Add(shader);
                shader.shaderKeywords = Array.Empty<string>();
                int kernel = shader.FindKernel("DeferredShadingCS");
                Texture2D Texture(Color color)
                {
                    var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                    owned.Add(texture);
                    var pixels = new Color[size * size];
                    Array.Fill(pixels, color);
                    texture.SetPixels(pixels); texture.Apply();
                    return texture;
                }
                RenderTexture Target()
                {
                    var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
                    owned.Add(target); target.Create();
                    RenderTexture.active = target; GL.Clear(false, true, Color.clear);
                    RenderTexture.active = previous;
                    return target;
                }
                ComputeBuffer Buffer<T>(T[] data, int stride) where T : struct
                {
                    var buffer = new ComputeBuffer(data.Length, stride); buffers.Add(buffer); buffer.SetData(data); return buffer;
                }
                var black = Texture(Color.clear);
                var layer = Texture(new Color(64f / 255, 0, 0, 1f / 255));
                var baked = Texture(new Color(0, 0, 0, 1));
                var normal = Texture(new Color(.5f, .5f, 0, 0));
                var albedo = Texture(new Color(.5f, 128f / 255, .65f, 0));
                var environment = new Texture2DArray(1, 1, 6, TextureFormat.RGBAFloat, false, true);
                owned.Add(environment);
                for (int i = 0; i < 6; i++) environment.SetPixels(new[] { Color.clear }, i);
                environment.Apply();
                var output = Target(); var diffuse = Target(); var specular = Target();
                var records = new FLightRecord[directionalCount + Math.Max(1, localCount)];
                for (int i = 0; i < records.Length; i++)
                {
                    records[i].positionRange = new Vector4(0, 0, -1, 20);
                    records[i].forward = new Vector4(0, 0, 1, 0);
                    records[i].right = new Vector4(1, 0, 0, 0);
                    records[i].weights = Vector4.one;
                    records[i].lightLayer = 1;
                    records[i].shadowMatrixIndex = -1;
                    records[i].bakedOcclusionChannel = -1;
                    records[i].cookieIndex = -1;
                    // Normalized total power makes every nonempty count share one analytic expectation.
                    records[i].radiance = i < directionalCount ? Vector4.zero : Vector4.one * (30f / Math.Max(1, localCount));
                }
                int words = Math.Max(1, (localCount + 31) / 32);
                var masks = new uint[words];
                for (int i = 0; i < localCount; i++) masks[i / 32] |= 1u << (i & 31);
                uint packed = localCount == 0 ? 0x0000ffffu : (uint)(localCount - 1) << 16;
                shader.SetBuffer(kernel, "g_LightRecordBuffer", Buffer(records, Marshal.SizeOf<FLightRecord>()));
                shader.SetBuffer(kernel, "SRV_LightTileMasks", Buffer(masks, 4));
                shader.SetBuffer(kernel, "SRV_LightZBins", Buffer(new[] { packed }, 4));
                shader.SetBuffer(kernel, "SRV_LocalShadowMatrices", Buffer(new[] { Matrix4x4.identity }, 64));
                // Replay fixtures bind the actual stage's inactive-resource layout, never a later shader payload.
                bool shadowSlices = System.IO.File.ReadAllText("Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/ShadowSampling.hlsl").Contains("struct FLocalShadowSlice");
                if (shadowSlices)
                {
                    shader.SetBuffer(kernel, "SRV_LocalShadowRects", Buffer(new[] { new ShadowSlice { rect = Vector4.zero, depth = Vector4.zero, moments = Vector4.zero } }, 48));
                    shader.SetTexture(kernel, "SRV_CascadeShadowMoments", black);
                    shader.SetTexture(kernel, "SRV_LocalShadowMoments", black);
                }
                else shader.SetBuffer(kernel, "SRV_LocalShadowRects", Buffer(new[] { Vector4.zero }, 16));
                if (System.IO.File.Exists("Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/Lighting/LightCookie.hlsl"))
                {
                    shader.SetBuffer(kernel, "SRV_LightCookies", Buffer(new[] { Matrix4x4.zero }, 64));
                    shader.SetTexture(kernel, "SRV_LightCookieAtlas", black);
                }
                if (System.IO.File.Exists("Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/Lighting/ReflectionProbes.hlsl"))
                {
                    var probe = new ProbeRecord { row0 = Vector4.zero, row1 = Vector4.zero, row2 = Vector4.zero,
                        extents = Vector4.zero, capture = Vector4.zero, projection = Vector4.zero, parameters = Vector4.zero };
                    shader.SetBuffer(kernel, "SRV_ReflectionProbes", Buffer(new[] { probe }, 112));
                    shader.SetBuffer(kernel, "SRV_ProbeZBins", Buffer(new[] { 0x0000ffffu }, 4));
                    shader.SetBuffer(kernel, "SRV_ProbeTileMasks", Buffer(new[] { 0u }, 4));
                    shader.SetTexture(kernel, "SRV_ReflectionProbeAtlas", environment);
                    shader.SetInt("g_ReflectionProbeCount", 0);
                }
                shader.SetBuffer(kernel, "_AtmosphereSkySH", Buffer(new Vector4[9], 16));
                shader.SetInt("g_LocalLightCount", localCount); shader.SetInt("g_DirectionalLightCount", directionalCount);
                shader.SetVector("g_LightTileParams", new Vector4(size, size, 1, words));
                shader.SetVector("g_LightZBinParams", new Vector4(1, 0, 0, 0));
                shader.SetVector("DeferredShading_Resolution", new Vector4(size, size, 1f / size, 1f / size));
                shader.SetFloat("DeferredShading_FarDepth", 1);
                shader.SetMatrix("Matrix_InvProj", Matrix4x4.identity); shader.SetMatrix("Matrix_InvViewProj", Matrix4x4.identity);
                shader.SetVector("_WorldSpaceCameraPos", new Vector4(0, 0, -2, 1));
                shader.SetInt("_CascadeCount", 0); shader.SetFloat("_InfinityShadowDistance", 1000);
                shader.SetFloat("_AtmosphereIBLMaxMip", 0);
                shader.SetTexture(kernel, "SRV_GBufferTextureA", albedo); shader.SetTexture(kernel, "SRV_GBufferTextureB", normal);
                shader.SetTexture(kernel, "SRV_GBufferTextureC", layer); shader.SetTexture(kernel, "SRV_DepthTexture", black);
                shader.SetTexture(kernel, "SRV_BakedDiffuse", baked); shader.SetTexture(kernel, "SRV_BakedOcclusion", black);
                shader.SetTexture(kernel, "SRV_CascadeShadowMap", black); shader.SetTexture(kernel, "SRV_LocalShadowMap", black);
                shader.SetTexture(kernel, "_AtmosphereGGXPrefilter", environment);
                shader.SetTexture(kernel, "UAV_LightingTexture", output); shader.SetTexture(kernel, "UAV_IndirectDiffuse", diffuse);
                shader.SetTexture(kernel, "UAV_IndirectSpecular", specular);
                shader.Dispatch(kernel, 1, 1, 1);
                var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                var actual = readback.GetData<Color>()[8 * size + 8];
                float distanceSquared = 1 + 2 * .0625f * .0625f;
                float window = 1 - Mathf.Pow(Mathf.Sqrt(distanceSquared) / 20, 4);
                float expected = localCount == 0 ? 0 : .5f / Mathf.PI * 30 * window * window / distanceSquared / Mathf.Sqrt(distanceSquared);
                Assert.That(actual.r, Is.EqualTo(expected).Within(.015f), "Actual production Deferred kernel, local=" + localCount);
                Assert.That(actual.g, Is.EqualTo(expected).Within(.015f));
                Assert.That(actual.b, Is.EqualTo(expected).Within(.015f));
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var buffer in buffers) buffer.Dispose();
                foreach (var item in owned) UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
