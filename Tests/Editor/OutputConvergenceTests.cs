using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class OutputConvergenceTests
    {
        const string Root = "Packages/com.infinity.render-pipeline/";

        [TestCase(EOutputEncodePolicy.HardwareSRGB, false)]
        [TestCase(EOutputEncodePolicy.ShaderLinearToSRGB, true)]
        [TestCase(EOutputEncodePolicy.ShaderPQRec2020, true)]
        [TestCase(EOutputEncodePolicy.ShaderHLGRec2020, true)]
        [TestCase(EOutputEncodePolicy.ShaderScRGBLinear, true)]
        [TestCase(EOutputEncodePolicy.ShaderRec2020Linear, true)]
        public void UiOutput_PreservesLinearBlendContract(EOutputEncodePolicy policy, bool intermediate)
        {
            Assert.AreEqual(intermediate, new FOutputPlan(true, policy, false, false, false, false).linearIntermediate);
            Assert.IsFalse(new FOutputPlan(false, policy, false, false, false, false).linearIntermediate);
        }

        [TestCase(true, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, false, false, true)]
        public void LinearConsumers_RequireOwnedIntermediate(bool debug, bool capture, bool wire, bool gizmos)
        {
            var plan = new FOutputPlan(false, EOutputEncodePolicy.HardwareSRGB, debug, capture, wire, gizmos);
            Assert.IsTrue(plan.linearIntermediate);
            Assert.AreEqual(wire || gizmos, plan.editorDepth);
        }

        [Test]
        public void PartialViewport_RequiresPreservedDestination()
        {
            Assert.IsTrue(FOutputPlan.CoversTarget(new Rect(0, 0, 1, 1)));
            Assert.IsFalse(FOutputPlan.CoversTarget(new Rect(.2f, 0, .8f, 1)));
            Assert.IsFalse(FOutputPlan.CoversTarget(new Rect(0, 0, .5f, 1)));
        }

        [TestCase(EOutputEncodePolicy.LinearTarget)]
        [TestCase(EOutputEncodePolicy.ShaderLinearToSRGB)]
        [TestCase(EOutputEncodePolicy.ShaderPQRec2020)]
        [TestCase(EOutputEncodePolicy.ShaderHLGRec2020)]
        [TestCase(EOutputEncodePolicy.ShaderScRGBLinear)]
        [TestCase(EOutputEncodePolicy.ShaderRec2020Linear)]
        public void DirectPostCombine_MatchesLinearIntermediateThenEncode(EOutputEncodePolicy policy)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Utility/DrawFullScreen.shader");
            var material = new Material(shader);
            material.SetVector("_MainTex_InfinityView",Vector4.zero);
            material.SetVector("SRV_SceneColorTexture_InfinityView",Vector4.zero);
            var source = new Texture2D(7, 5, TextureFormat.RGBAFloat, false, true);
            var exposure = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
            var lut = new Texture3D(4, 4, 4, TextureFormat.RGBAFloat, false);
            var intermediate = Target(7, 5, GraphicsFormat.R16G16B16A16_SFloat);
            var direct = Target(7, 5, GraphicsFormat.R32G32B32A32_SFloat);
            var composed = Target(7, 5, GraphicsFormat.R32G32B32A32_SFloat);
            try
            {
                var pixels = new Color[35];
                for (int i = 0; i < pixels.Length; ++i) pixels[i] = new Color(.01f + i * .1f, .02f + i * .03f, .4f, 1);
                source.SetPixels(pixels); source.Apply();
                exposure.SetPixel(0, 0, new Color(.5f, 0, 0, 0)); exposure.Apply();
                var grades = new Color[64];
                for (int b = 0; b < 4; b++) for (int g = 0; g < 4; g++) for (int r = 0; r < 4; r++)
                    grades[r + 4 * g + 16 * b] = new Color(r / 3f, g / 3f, b / 3f, 1);
                lut.SetPixels(grades); lut.Apply();
                material.SetTexture("SRV_SceneColorTexture", source); material.SetVector("SRV_SceneColorTexture_InfinityView",Vector4.zero);
                material.SetTexture("SRV_ExposureEV", exposure);
                material.SetTexture("SRV_CombineLUT", lut);
                material.SetVector("PP_Resolution", new Vector4(7, 5, 1f / 7, .2f));
                material.SetFloat("PP_ExposureMultiplier", 1.25f);
                material.SetFloat("PP_AutoExposure", 1);
                material.SetFloat("PP_VignetteIntensity", .2f);
                material.SetFloat("PP_VignetteSmoothness", .5f);
                material.SetFloat("PP_FilmGrainIntensity", .1f);
                material.SetFloat("PP_FilmGrainResponse", .8f);
                material.SetInt("PP_FrameIndex", 19);
                Draw(material, "RasterPostCombine", direct, policy);
                Draw(material, "RasterPostCombine", intermediate, EOutputEncodePolicy.LinearTarget);
                material.SetTexture("_MainTex", intermediate); material.SetVector("_MainTex_InfinityView",Vector4.zero);
                Draw(material, "RasterFinalOutput", composed, policy);
                float[] a = Read(direct), bValues = Read(composed);
                for (int i = 0; i < a.Length; ++i)
                    Assert.That(a[i], Is.EqualTo(bValues[i]).Within(.0015f), policy + " channel " + i);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(exposure); UnityEngine.Object.DestroyImmediate(lut);
                UnityEngine.Object.DestroyImmediate(intermediate); UnityEngine.Object.DestroyImmediate(direct);
                UnityEngine.Object.DestroyImmediate(composed);
            }
        }

        [Test]
        public void HardwareSrgbUiBlend_MatchesLinearCompositionBeforeOutput()
        {
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Utility/DrawFullScreen.shader"));
            var ui = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "Tests/Editor/Fixtures/OutputUiBlend.shader"));
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var linear = Target(4, 4, GraphicsFormat.R16G16B16A16_SFloat);
            var direct = Target(4, 4, GraphicsFormat.R8G8B8A8_SRGB);
            var composed = Target(4, 4, GraphicsFormat.R8G8B8A8_SRGB);
            try
            {
                source.SetPixel(0, 0, new Color(.18f, .1f, .02f, 1)); source.Apply();
                material.SetTexture("_MainTex", source); material.SetVector("_MainTex_InfinityView",Vector4.zero);
                Draw(material, "RasterFinalOutput", direct, EOutputEncodePolicy.HardwareSRGB);
                Draw(material, "RasterFinalOutput", linear, EOutputEncodePolicy.LinearTarget);
                foreach (var target in new[] { direct, linear })
                {
                    using var commands = new CommandBuffer();
                    commands.SetRenderTarget(target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    commands.SetViewport(new Rect(0, 0, 4, 4));
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, ui, 0, 0);
                    Graphics.ExecuteCommandBuffer(commands);
                }
                material.SetTexture("_MainTex", linear); material.SetVector("_MainTex_InfinityView",Vector4.zero);
                Draw(material, "RasterFinalOutput", composed, EOutputEncodePolicy.HardwareSRGB);
                var a = AsyncGPUReadback.Request(direct); a.WaitForCompletion();
                var b = AsyncGPUReadback.Request(composed); b.WaitForCompletion();
                Assert.IsFalse(a.hasError || b.hasError);
                for (int i = 0; i < a.GetData<byte>().Length; i++)
                    Assert.That((int)a.GetData<byte>()[i], Is.EqualTo((int)b.GetData<byte>()[i]).Within(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(ui);
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(linear);
                UnityEngine.Object.DestroyImmediate(direct); UnityEngine.Object.DestroyImmediate(composed);
            }
        }

        [Test]
        public void PartialViewport_PreservesPixelsOutsideCamera()
        {
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Utility/DrawFullScreen.shader"));
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = Target(8, 6, GraphicsFormat.R32G32B32A32_SFloat);
            try
            {
                source.SetPixel(0, 0, Color.red); source.Apply();
                material.SetTexture("_MainTex", source); material.SetVector("_MainTex_InfinityView",Vector4.zero);
                material.SetVector("_ScaleBais", new Vector4(1, 1, 0, 0));
                material.SetVector("_InfinityOutputTransfer", new Vector4((int)EOutputEncodePolicy.LinearTarget, 0, 100, 0));
                using var commands = new CommandBuffer();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, Color.green);
                commands.SetViewport(new Rect(2, 1, 4, 3));
                commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, material, 0, material.FindPass("RasterFinalOutput"));
                Graphics.ExecuteCommandBuffer(commands);
                var pixels = Read(target);
                int red = 0, green = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] > .99f && pixels[i + 1] < .01f) red++;
                    else if (pixels[i] < .01f && pixels[i + 1] > .99f) green++;
                    else Assert.Fail("Unexpected viewport pixel");
                }
                Assert.AreEqual(12, red);
                Assert.AreEqual(36, green);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static RenderTexture Target(int width, int height, GraphicsFormat format)
        {
            var target = new RenderTexture(new RenderTextureDescriptor(width, height) { graphicsFormat = format, depthBufferBits = 0 });
            Assert.IsTrue(target.Create());
            return target;
        }

        static void Draw(Material material, string passName, RenderTexture target, EOutputEncodePolicy policy)
        {
            material.SetVector("_MainTex_InfinityView",Vector4.zero);
            material.SetVector("SRV_SceneColorTexture_InfinityView",Vector4.zero);
            int pass = material.FindPass(passName);
            Assert.GreaterOrEqual(pass, 0);
            material.SetVector("_ScaleBais", new Vector4(1, 1, 0, 0));
            material.SetVector("_InfinityOutputTransfer", new Vector4((int)policy, 0, 100, 0));
            using var commands = new CommandBuffer();
            commands.DisableShaderKeyword("INFINITY_POST_BLOOM");
            commands.SetRenderTarget(target);
            commands.SetViewport(new Rect(0, 0, target.width, target.height));
            commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, material, 0, pass);
            Graphics.ExecuteCommandBuffer(commands);
        }

        static float[] Read(RenderTexture target)
        {
            var request = AsyncGPUReadback.Request(target); request.WaitForCompletion();
            Assert.IsFalse(request.hasError);
            float[] values = request.GetData<float>().ToArray();
            foreach (float value in values) Assert.IsTrue(!float.IsNaN(value) && !float.IsInfinity(value));
            return values;
        }
    }
}
