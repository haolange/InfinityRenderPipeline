using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class SubsurfaceGpuTests
    {
        [TestCase(17, 19, 0)]
        [TestCase(17, 19, 1)]
        [TestCase(17, 19, 2)]
        [TestCase(17, 19, 3)]
        [TestCase(17, 19, 4)]
        [TestCase(17, 19, 5)]
        [TestCase(17, 19, 6)]
        [TestCase(1, 1, 0)]
        [TestCase(1, 19, 0)]
        public void DiffuseRecomposition_PreservesConstantsBoundariesAndUnscatteredLight(int width, int height, int mode)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and readback are required.");
            var owned = new List<Object>();
            ComputeBuffer profiles = null;
            try
            {
                T Own<T>(T value) where T : Object { owned.Add(value); return value; }
                Texture2D Texture(System.Func<int, int, Color> value)
                {
                    var texture = Own(new Texture2D(width + 7, height + 5, TextureFormat.RGBAFloat, false, true));
                    var pixels = new Color[texture.width * texture.height];
                    for (int y = 0; y < texture.height; ++y)
                        for (int x = 0; x < texture.width; ++x)
                            pixels[y * texture.width + x] = x < width && y < height ? value(x, y) : new Color(900, 900, 900, 900);
                    texture.SetPixels(pixels); texture.Apply(false, false);
                    return texture;
                }
                RenderTexture Target(int w, int h, GraphicsFormat format)
                {
                    var texture = Own(new RenderTexture(w, h, 0, format) { enableRandomWrite = true });
                    Assert.IsTrue(texture.Create());
                    var previous = RenderTexture.active;
                    RenderTexture.active = texture; GL.Clear(false, true, new Color(-77, -77, -77, -77)); RenderTexture.active = previous;
                    return texture;
                }
                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/BurleySubsurface/Compute_BurleySubsurface.compute");
                var shader = Own(Object.Instantiate(asset));
                var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, (float)width / height, .1f, 100), false);
                var clip = projection * new Vector4(0, 0, -5, 1);
                float depth = clip.z / clip.w;
                Color Diffuse(int x, int y) => (mode == 5 || mode == 6) ? (x == 8 && y == 8 ? new Color(1,.5f,.25f,1) : new Color(.1f,.05f,.025f,1)) : (mode == 1 || mode == 4) ? (x < width / 2 ? new Color(.2f, .1f, .05f, 1) : new Color(.8f, .4f, .2f, 1)) : new Color(.25f, .125f, .0625f, 1);
                var diffuse = Texture(Diffuse);
                var rawDepth = Texture((x, y) => new Color(depth, 0, 0, 0));
                var b = Texture((x, y) => mode == 4 && x >= width / 2 ? new Color(1, .5f, .5f, .5f) : new Color(.5f, .5f, 1, .5f));
                var c = Texture((x, y) => new Color(mode == 2 ? 64f / 255 : 81f / 255, (mode == 1 && x >= width / 2 || mode == 6) ? 1f / 255 : 0, 1, 1));
                var filtered = Target((width + 1) / 2 + 3, (height + 1) / 2 + 3, GraphicsFormat.R16G16B16A16_SFloat);
                var scene = Target(width + 7, height + 5, GraphicsFormat.R32G32B32A32_SFloat);
                // Upload a nonuniform specular/emissive residual through a CPU-created texture.
                var original = Texture((x, y) => Diffuse(x, y) + new Color(x % 2 == 0 ? 3 : 0, y % 2 == 0 ? 2 : 0, 1, 0));
                Graphics.CopyTexture(original, scene);
                profiles = new ComputeBuffer(2, 32);
                profiles.SetData(new[] { new FDiffusionProfileRecord { scatterAlbedoDistance = new Vector4(1,1,1,mode == 3 ? .00001f : 1), parameters = new Vector4(3,0,0,0) }, new FDiffusionProfileRecord { scatterAlbedoDistance = new Vector4(1,1,1,mode == 6 ? .00001f : 1), parameters = new Vector4(3,0,0,0) } });
                shader.SetVector("SSS_Resolution", new Vector4(width, height, 1f / width, 1f / height));
                shader.SetMatrix("Matrix_Proj", projection); shader.SetMatrix("Matrix_InvProj", projection.inverse);
                shader.SetInt("SSS_NumSamples", 32); shader.SetInt("SSS_ProfileCount", 2);
                foreach (string name in new[] { "SRV_LightingTexture", "SRV_DepthTexture", "SRV_GBufferTextureB", "SRV_GBufferTextureC", "SRV_FilteredSubsurface" })
                {
                    bool half = name == "SRV_FilteredSubsurface";
                    shader.SetVector(name + "_InfinityView", half ? new Vector4((width + 1) / 2, (height + 1) / 2, filtered.width, filtered.height) : new Vector4(width, height, original.width, original.height));
                }
                int blur = shader.FindKernel("BurleySubsurfaceCS"), compose = shader.FindKernel("ComposeSubsurfaceCS");
                foreach (int kernel in new[] { blur, compose })
                {
                    shader.SetBuffer(kernel, "SRV_DiffusionProfiles", profiles);
                    shader.SetTexture(kernel, "SRV_LightingTexture", diffuse); shader.SetTexture(kernel, "SRV_DepthTexture", rawDepth);
                    shader.SetTexture(kernel, "SRV_GBufferTextureB", b); shader.SetTexture(kernel, "SRV_GBufferTextureC", c);
                }
                shader.SetTexture(blur, "UAV_SubsurfaceTexture", filtered);
                shader.Dispatch(blur, (((width + 1) / 2) + 7) / 8, (((height + 1) / 2) + 7) / 8, 1);
                shader.SetTexture(compose, "SRV_FilteredSubsurface", filtered); shader.SetTexture(compose, "UAV_SceneColor", scene);
                shader.Dispatch(compose, (width + 7) / 8, (height + 7) / 8, 1);
                var readback = AsyncGPUReadback.Request(scene); readback.WaitForCompletion(); Assert.IsFalse(readback.hasError);
                var result = readback.GetData<Color>();
                for (int y = 0; y < scene.height; ++y)
                    for (int x = 0; x < scene.width; ++x)
                    {
                        Color expected = x < width && y < height ? Diffuse(x, y) + new Color(x % 2 == 0 ? 3 : 0, y % 2 == 0 ? 2 : 0, 1, 0) : new Color(900, 900, 900, 900);
                        Color actual = result[y * scene.width + x];
                        if (mode == 5 && x < width && y < height)
                        {
                            float red = actual.r - (x % 2 == 0 ? 3 : 0);
                            float green = actual.g - (y % 2 == 0 ? 2 : 0);
                            float blue = actual.b - 1;
                            Assert.That(red,Is.EqualTo(2*green).Within(.001f));
                            Assert.That(red,Is.EqualTo(4*blue).Within(.001f));
                            Assert.That(red,Is.InRange(.099f,1.001f));
                            if (x==8 && y==8) Assert.Less(red,.8f,"The diffuse impulse must actually scatter; a no-op cannot pass.");
                            if (x==7 && y==8) Assert.Greater(red,.1001f,"A neighboring receiver must gain diffuse energy.");
                            continue;
                        }
                        for (int channel = 0; channel < 4; ++channel)
                        {
                            Assert.IsFalse(float.IsNaN(actual[channel]) || float.IsInfinity(actual[channel]));
                            Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(.001f), $"mode {mode}, {x},{y}, channel {channel}");
                        }
                    }
            }
            finally
            {
                profiles?.Release();
                foreach (var value in owned) { if (value is RenderTexture target) target.Release(); Object.DestroyImmediate(value); }
            }
        }
    }
}
