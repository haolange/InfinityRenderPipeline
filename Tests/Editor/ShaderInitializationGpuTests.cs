using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShaderInitializationGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/ShaderInitializationContract.compute";

        static Vector4[] Run(string kernelName, Vector4[] inputs, Action<ComputeShader, int> bind)
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A compute-capable graphics device is required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                using var source = new ComputeBuffer(inputs.Length, 16);
                using var output = new ComputeBuffer(inputs.Length, 16);
                source.SetData(inputs);
                output.SetData(new Vector4[inputs.Length]);
                int kernel = shader.FindKernel(kernelName);
                if (kernelName != "SurfaceContract") shader.SetBuffer(kernel, "ContractInputs", source);
                shader.SetBuffer(kernel, "ContractResults", output);
                bind(shader, kernel);
                shader.Dispatch(kernel, inputs.Length, 1, 1);
                var results = new Vector4[inputs.Length];
                output.GetData(results);
                foreach (Vector4 result in results)
                    for (int c = 0; c < 4; c++) Assert.IsTrue(float.IsFinite(result[c]));
                return results;
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DepthConversion_PreservesPerspectiveAndOrthographicBranches(bool orthographic, bool reversed)
        {
            var inputs = new[] { Vector4.zero, new Vector4(.25f, 0, 0, 0), new Vector4(.5f, 0, 0, 0), Vector4.one };
            Vector4[] results = Run("DepthContract", inputs, (shader, _) =>
                shader.SetVector("ScreenSpaceDepthParams", new Vector4(.1f, 100, orthographic ? 1 : 0, reversed ? 1 : 0)));
            for (int i = 0; i < inputs.Length; i++)
            {
                double raw = inputs[i].x;
                double expected = orthographic ? .1 + 99.9 * (reversed ? 1 - raw : raw)
                    : 10 / (reversed ? .1 + raw * 99.9 : 100 - raw * 99.9);
                Assert.That(results[i].x, Is.EqualTo(expected).Within(Math.Max(1e-5, expected * 3e-5)));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Reprojection_RejectsEachBehindCameraProjectionAndInitializesOutputs(int rejectedMatrix)
        {
            Matrix4x4 previous = Matrix4x4.identity, currentMotion = Matrix4x4.identity, previousMotion = Matrix4x4.identity;
            if (rejectedMatrix == 1) previous.m33 = -1;
            if (rejectedMatrix == 2) currentMotion.m33 = -1;
            if (rejectedMatrix == 3) previousMotion.m33 = -1;
            Vector4 result = Run("ReprojectionContract", new[] { new Vector4(.2f, .7f, .4f, 0) }, (shader, _) =>
            {
                shader.SetMatrix("Matrix_InvViewProj", Matrix4x4.identity);
                shader.SetMatrix("ContractPreviousVP", previous);
                shader.SetMatrix("ContractCurrentMotionVP", currentMotion);
                shader.SetMatrix("ContractPreviousMotionVP", previousMotion);
            })[0];
            Assert.That(result.x, Is.EqualTo(rejectedMatrix == 0 ? .15f : -1f).Within(1e-6));
            Assert.That(result.y, Is.EqualTo(rejectedMatrix == 0 ? .73f : -1f).Within(1e-6));
            Assert.That(result.z, Is.EqualTo(.4f).Within(1e-6));
            Assert.That(result.w, Is.EqualTo(rejectedMatrix == 1 ? -1f : .6f).Within(1e-6));
        }

        [Test]
        public void SphereAndDiskIlluminance_MatchesIndependentDoublePrecisionReference()
        {
            var inputs = new[]
            {
                new Vector4(-.95f, .04f, 0, 0), new Vector4(-.1f, .25f, 0, 0),
                new Vector4(0, .5f, 0, 0), new Vector4(.1f, .25f, 0, 0), new Vector4(.95f, .04f, 0, 0)
            };
            Vector4[] results = Run("IlluminanceContract", inputs, (_, _) => { });
            for (int i = 0; i < inputs.Length; i++)
            {
                double cosine = inputs[i].x, sigma = inputs[i].y;
                double expected = Math.PI * sigma * Math.Max(cosine, 0);
                if (cosine * cosine <= sigma)
                {
                    double sine = Math.Sqrt(Math.Max(0, 1 - cosine * cosine));
                    double x = Math.Sqrt(Math.Max(1 / sigma - 1, 1e-6));
                    double y = -x * cosine / Math.Max(sine, 1e-6);
                    double term = sine * Math.Sqrt(Math.Clamp(1 - y * y, 0, 1));
                    expected = Math.Max(0, (cosine * Math.Acos(Math.Clamp(y, -1, 1)) - x * term) * sigma + Math.Atan(term / x));
                }
                // Metal atan/acos approximations need not match CPU libm at single-ULP precision.
                Assert.That(results[i].x, Is.EqualTo(expected).Within(Math.Max(2e-6, expected * 2e-4)));
            }
        }

        [Test]
        public void ContactMask_PreservesUnallocatedUnselectedAndOccludedLightBranches()
        {
            var inputs = new[]
            {
                new Vector4(-1, 2, 255, 0), new Vector4(1, 0, 255, 0),
                new Vector4(1, 2, 128, 0), new Vector4(23, 1 << 23, 255, 0), new Vector4(1, 2, 0, 0)
            };
            Vector4[] results = Run("ContactShadowContract", inputs, (_, _) => { });
            float[] expected = { 1, 1, 127f / 255, 0, 1 };
            for (int i = 0; i < results.Length; i++)
                Assert.That(results[i].x, Is.EqualTo(expected[i]).Within(1e-6));
        }

        [Test]
        public void SurfaceRejection_PreservesRawDepthAndDoesNotPopulateGeometry()
        {
            var depth = new Texture2D(3, 1, TextureFormat.RGBAFloat, false, true);
            var flags = new Texture2D(3, 1, TextureFormat.RGBAFloat, false, true);
            var normals = new Texture2D(3, 1, TextureFormat.RGBAFloat, false, true);
            float far = SystemInfo.usesReversedZBuffer ? 0 : 1;
            try
            {
                depth.SetPixels(new[] { new Color(far, 0, 0, 0), new Color(.4f, 0, 0, 0), new Color(.6f, 0, 0, 0) });
                flags.SetPixels(new[] { Color.clear, new Color((6 << 4) / 255f, 0, 0, 0), new Color((4 << 4) / 255f, 0, 0, 0) });
                normals.SetPixels(new[] { new Color(.5f, .5f, 1, 0), new Color(.5f, .5f, 1, 0), new Color(.5f, .5f, 1, 0) });
                depth.Apply(); flags.Apply(); normals.Apply();
                Vector4[] results = Run("SurfaceContract", new Vector4[3], (shader, kernel) =>
                {
                    shader.SetTexture(kernel, "SRV_DepthTexture", depth);
                    shader.SetTexture(kernel, "SRV_GBufferC", flags);
                    shader.SetTexture(kernel, "SRV_GBufferB", normals);
                    shader.SetVector("SSGI_Resolution", new Vector4(3, 1, 1f / 3, 1));
                    shader.SetMatrix("Matrix_InvProj", Matrix4x4.identity);
                    shader.SetMatrix("Matrix_InvViewProj", Matrix4x4.identity);
                });
                Assert.AreEqual(new Vector4(far, 0, 0, 0), results[0]);
                Assert.AreEqual(new Vector4(.4f, 0, 0, 0), results[1]);
                Assert.That(results[2].x, Is.EqualTo(.6f).Within(1e-6));
                Assert.AreEqual(1, results[2].y);
                Assert.That(results[2].z, Is.EqualTo(1).Within(1e-6));
                Assert.Greater(results[2].w, 0);
            }
            finally { Object.DestroyImmediate(depth); Object.DestroyImmediate(flags); Object.DestroyImmediate(normals); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TraceExit_PreservesPlaneHitsAndSkyMisses(bool sky)
        {
            var surface = new Texture2D(16, 8, TextureFormat.RGBAFloat, false, true);
            var pyramid = new Texture2D(8, 4, TextureFormat.RGBAFloat, true, true);
            bool reversed = SystemInfo.usesReversedZBuffer;
            float rawDepth = sky ? (reversed ? 0 : 1) : .5f;
            try
            {
                for (int mip = 0; mip < pyramid.mipmapCount; mip++)
                {
                    var pixels = new Color[Math.Max(1, 8 >> mip) * Math.Max(1, 4 >> mip)];
                    Array.Fill(pixels, new Color(rawDepth, 0, 0, 0));
                    pyramid.SetPixels(pixels, mip);
                }
                pyramid.Apply(false, false);
                var surfacePixels = new Color[16 * 8]; Array.Fill(surfacePixels, new Color(rawDepth, 0, 0, 0));
                surface.SetPixels(surfacePixels); surface.Apply(false, false);
                Vector4[] results = Run("TraceContract", new[] { Vector4.zero, Vector4.one }, (shader, kernel) =>
                {
                    Matrix4x4 projection = Matrix4x4.identity;
                    projection.m22 = -1;
                    shader.SetMatrix("ContractProjection", projection);
                    shader.SetTexture(kernel, "ContractDepthPyramid", pyramid);
                    shader.SetTexture(kernel, "ContractSurfaceDepth", surface);
                    shader.SetVector("ContractOrigin", new Vector4(-.6f, 0, reversed ? -.8f : -.2f, 0));
                    shader.SetVector("ContractDirection", new Vector4(.8f, 0, reversed ? .6f : -.6f, 0));
                    shader.SetVector("ScreenSpaceDepthParams", new Vector4(.1f, 100, 1, reversed ? 1 : 0));
                });
                foreach (Vector4 result in results)
                {
                    if (sky) Assert.AreEqual(Vector4.zero, result);
                    else
                    {
                        Assert.AreEqual(1, result.w, result.ToString());
                        Assert.That(result.z, Is.EqualTo(.5f).Within(1e-6));
                        Assert.That(result.x * 16 - .5f, Is.EqualTo(Mathf.Round(result.x * 16 - .5f)).Within(1e-5));
                        Assert.That(result.y * 8 - .5f, Is.EqualTo(Mathf.Round(result.y * 8 - .5f)).Within(1e-5));
                    }
                }
            }
            finally { Object.DestroyImmediate(pyramid); Object.DestroyImmediate(surface); }
        }
    }
}
