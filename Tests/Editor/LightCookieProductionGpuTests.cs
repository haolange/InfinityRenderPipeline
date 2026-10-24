using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightCookieProductionGpuTests
    {
        [TestCase(false, 0)] [TestCase(false, 1)] [TestCase(false, 2)]
        [TestCase(true, 0)] [TestCase(true, 1)] [TestCase(true, 2)]
        public void ProductionCopy_PreservesSelectedChannels(bool cube, int channels)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/LightCookie/Compute_LightCookie.compute"));
            var color = new Color(.125f, .25f, .5f, .75f);
            var pixels = new Color[16]; System.Array.Fill(pixels, color);
            var source = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            source.SetPixels(pixels); source.Apply();
            var cubemap = new Cubemap(4, TextureFormat.RGBAFloat, false);
            for (int face = 0; face < 6; ++face) cubemap.SetPixels(pixels, (CubemapFace)face);
            cubemap.Apply();
            var target = new RenderTexture(7, 7, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
            target.Create();
            try
            {
                int kernel = shader.FindKernel(cube ? "CookieCubeToOctahedral" : "CookieCopy2D");
                shader.SetInt("Cookie_Channels", channels);
                shader.SetVector("Cookie_Block", new Vector4(0, 0, 7, 0));
                shader.SetTexture(kernel, cube ? "SRV_CookieCube" : "SRV_CookieSource", cube ? (Texture)cubemap : source);
                shader.SetTexture(kernel, "UAV_CookieAtlas", target);
                shader.Dispatch(kernel, 1, 1, 1);
                var readback = AsyncGPUReadback.Request(target); readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                Color expected = channels == 0 ? new Color(color.r, color.g, color.b, 1)
                    : channels == 1 ? new Color(color.a, color.a, color.a, 1) : new Color(color.r, color.r, color.r, 1);
                foreach (Color actual in readback.GetData<Color>())
                    for (int channel = 0; channel < 4; ++channel)
                        Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(1e-5));
            }
            finally
            {
                Object.DestroyImmediate(target); Object.DestroyImmediate(cubemap);
                Object.DestroyImmediate(source); Object.DestroyImmediate(shader);
            }
        }
    }
}
