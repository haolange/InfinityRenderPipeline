using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Drives RasterSubsurfaceExtract and RasterSubsurfaceScatter of DrawFullScreen.shader through a stencil-tested target:
    // Out = t·blur(SceneColor·R) + SceneColor·(1 − t·R) within one profile.
    public class SubsurfaceGpuTests
    {
        const int k_Width = 17;
        const int k_Height = 19;

        public enum EScatterCase
        {
            // Uniform field, diffuse share 0.5: the blur of a constant is the constant, so the color is preserved.
            ConstantField,
            // A bright pixel in a dim field, all diffuse: energy spreads to neighbors with its chroma intact.
            Impulse,
            // Left half profile 1, right half profile 2, different colors: no tap crosses the profile boundary.
            ProfileBoundary,
            // Profile 0 marks a pixel that is not subsurface even under the stencil bit.
            NoProfile,
            // The subsurface stencil bit is clear: neither draw touches the pixel.
            NoStencilBit,
            // Thickness 0 scatters nothing.
            ZeroThickness,
            // A scatter distance far below a pixel has no footprint.
            SubPixelFootprint,
            // Profile 3 with two profiles in the list is outside the asset's list.
            ProfileOutsideList
        }

        [TestCase(EScatterCase.ConstantField)]
        [TestCase(EScatterCase.Impulse)]
        [TestCase(EScatterCase.ProfileBoundary)]
        [TestCase(EScatterCase.NoProfile)]
        [TestCase(EScatterCase.NoStencilBit)]
        [TestCase(EScatterCase.ZeroThickness)]
        [TestCase(EScatterCase.SubPixelFootprint)]
        [TestCase(EScatterCase.ProfileOutsideList)]
        public void RasterScatter_ConservesUnscatteredColorAndKeepsProfilesApart(EScatterCase scatterCase)
        {
            if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("A GPU with readback is required.");
            }
            Shader blit = Shader.Find("InfinityPipeline/Utility/DrawFullScreen");
            Assert.IsNotNull(blit);
            var owned = new List<Object>();
            GraphicsBuffer profiles = null;
            try
            {
                T Own<T>(T value) where T : Object
                {
                    owned.Add(value);
                    return value;
                }
                Texture2D Texture(System.Func<int, int, Color> value)
                {
                    var texture = Own(new Texture2D(k_Width, k_Height, TextureFormat.RGBAFloat, false, true));
                    var pixels = new Color[k_Width * k_Height];
                    for (int y = 0; y < k_Height; ++y)
                    {
                        for (int x = 0; x < k_Width; ++x)
                        {
                            pixels[y * k_Width + x] = value(x, y);
                        }
                    }
                    texture.SetPixels(pixels);
                    texture.Apply(false, false);
                    return texture;
                }
                RenderTexture ColorTarget()
                {
                    var target = Own(new RenderTexture(k_Width, k_Height, 0, GraphicsFormat.R32G32B32A32_SFloat));
                    Assert.IsTrue(target.Create());
                    return target;
                }

                var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, (float)k_Width / k_Height, .1f, 100), false);
                var clip = projection * new Vector4(0, 0, -5, 1);
                float depth = clip.z / clip.w;
                bool impulse = scatterCase == EScatterCase.Impulse;
                bool boundary = scatterCase == EScatterCase.ProfileBoundary;
                float ratio = scatterCase == EScatterCase.ConstantField ? 0.5f : 1.0f;
                Color Initial(int x, int y)
                {
                    if (impulse)
                    {
                        return x == 8 && y == 8 ? new Color(1, .5f, .25f, 1) : new Color(.1f, .05f, .025f, 1);
                    }
                    if (boundary)
                    {
                        return x < k_Width / 2 ? new Color(.2f, .1f, .05f, 1) : new Color(.05f, .4f, .2f, 1);
                    }
                    return new Color(.25f, .125f, .0625f, 1);
                }
                uint Profile(int x)
                {
                    switch (scatterCase)
                    {
                        case EScatterCase.NoProfile:
                            return 0;
                        case EScatterCase.ProfileOutsideList:
                            return 3;
                        case EScatterCase.ProfileBoundary:
                            return x < k_Width / 2 ? 1u : 2u;
                        default:
                            return 1;
                    }
                }
                // Light channel 0 in the high nibble, the profile in the low nibble.
                var sideband = Texture((x, y) => new Color(ratio, 1, 1, (16 + Profile(x)) / 255.0f));
                var depthTexture = Texture((x, y) => new Color(depth, 0, 0, 0));
                // Oct (0, 0): every normal faces the camera.
                var gBufferB = Texture((x, y) => new Color(.5f, .5f, 0f, .5f));
                var initial = Texture(Initial);
                var sceneColor = ColorTarget();
                var irradiance = ColorTarget();
                Graphics.CopyTexture(initial, sceneColor);
                var stencilTarget = Own(new RenderTexture(new RenderTextureDescriptor(k_Width, k_Height)
                {
                    graphicsFormat = GraphicsFormat.None,
                    depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt
                }));
                Assert.IsTrue(stencilTarget.Create());

                float distance = scatterCase == EScatterCase.SubPixelFootprint ? .00001f : 1;
                float thickness = scatterCase == EScatterCase.ZeroThickness ? 0 : 1;
                profiles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 32);
                profiles.SetData(new[]
                {
                    new DiffusionProfileRecord { scatterAlbedoDistance = new Vector4(1, 1, 1, distance), parameters = new Vector4(3, thickness, 0, 0) },
                    new DiffusionProfileRecord { scatterAlbedoDistance = new Vector4(1, 1, 1, distance), parameters = new Vector4(3, thickness, 0, 0) }
                });

                var material = Own(new Material(blit) { hideFlags = HideFlags.HideAndDontSave });
                int extractPass = material.FindPass("RasterSubsurfaceExtract");
                int scatterPass = material.FindPass("RasterSubsurfaceScatter");
                Assert.GreaterOrEqual(extractPass, 0);
                Assert.GreaterOrEqual(scatterPass, 0);
                material.SetBuffer("SRV_DiffusionProfiles", profiles);
                material.SetTexture("SRV_DepthTexture", depthTexture);
                material.SetTexture("SRV_GBufferTextureB", gBufferB);
                material.SetTexture("SRV_Sideband", sideband);
                material.SetTexture("SRV_LightingTexture", initial);
                material.SetTexture("SRV_SubsurfaceIrradiance", irradiance);
                material.SetVector("_SubsurfaceResolution", new Vector4(k_Width, k_Height, 1f / k_Width, 1f / k_Height));
                material.SetVector("_SubsurfaceSampling", new Vector4(32, 2, 0, 0));
                using var view = new UniformBinding<ViewUniform>(new ViewUniform { matrix_FlipYJitterProj = projection, matrix_InvFlipYJitterProj = projection.inverse });

                uint stencil = scatterCase == EScatterCase.NoStencilBit ? 0u : 4u;
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(irradiance, stencilTarget);
                    cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1.0f, stencil);
                    cmd.DrawProcedural(Matrix4x4.identity, material, extractPass, MeshTopology.Triangles, 3);
                    cmd.SetRenderTarget(sceneColor, stencilTarget);
                    cmd.DrawProcedural(Matrix4x4.identity, material, scatterPass, MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                var readback = AsyncGPUReadback.Request(sceneColor);
                readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                var result = readback.GetData<Color>();

                for (int y = 0; y < k_Height; ++y)
                {
                    for (int x = 0; x < k_Width; ++x)
                    {
                        Color expected = Initial(x, y);
                        Color actual = result[y * k_Width + x];
                        for (int channel = 0; channel < 3; ++channel)
                        {
                            Assert.IsFalse(float.IsNaN(actual[channel]) || float.IsInfinity(actual[channel]));
                        }
                        if (impulse)
                        {
                            Assert.That(actual.r, Is.EqualTo(2 * actual.g).Within(.001f), "The scattered impulse keeps its chroma.");
                            Assert.That(actual.r, Is.EqualTo(4 * actual.b).Within(.001f), "The scattered impulse keeps its chroma.");
                            Assert.That(actual.r, Is.InRange(.099f, 1.001f));
                            if (x == 8 && y == 8)
                            {
                                Assert.Less(actual.r, .8f, "The diffuse impulse must actually scatter; a no-op cannot pass.");
                            }
                            if (x == 7 && y == 8)
                            {
                                Assert.Greater(actual.r, .1001f, "A neighboring receiver must gain diffuse energy.");
                            }
                            continue;
                        }
                        for (int channel = 0; channel < 3; ++channel)
                        {
                            Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(.0005f), $"{scatterCase}, {x},{y}, channel {channel}");
                        }
                    }
                }
            }
            finally
            {
                profiles?.Release();
                foreach (var value in owned)
                {
                    if (value is RenderTexture target)
                    {
                        target.Release();
                    }
                    Object.DestroyImmediate(value);
                }
            }
        }
    }
}
