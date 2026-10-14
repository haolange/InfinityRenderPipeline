using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightShapeGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/LightShapeContract.compute";
        static readonly string[] Names = { "punctual", "sphere", "line", "tube", "rect", "disk" };

        static Vector4[] Run(float distance)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                using (var output = new ComputeBuffer(Names.Length, 16))
                {
                    int kernel = shader.FindKernel("ShapeIrradiance");
                    shader.SetVector("ShapeDistance", new Vector4(distance, 0, 0, 0));
                    shader.SetBuffer(kernel, "Results", output);
                    shader.Dispatch(kernel, Names.Length, 1, 1);
                    var results = new Vector4[Names.Length];
                    output.GetData(results);
                    return results;
                }
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [TestCase(5.0f, 0.02f)]
        [TestCase(50.0f, 0.001f)]
        [Category("Unverified-0.5")]
        public void EveryShape_ConvergesToThePunctualIrradiance(float distance, float tolerance)
        {
            Vector4[] results = Run(distance);
            for (int i = 0; i < Names.Length; ++i)
            {
                Assert.IsFalse(float.IsNaN(results[i].x) || float.IsInfinity(results[i].x), Names[i]);
                Assert.AreEqual(1.0f, results[i].x / results[i].y, tolerance, $"{Names[i]}: E / I against 1 / d^2");
                Assert.That(results[i].w, Is.InRange(0.0f, 1.0f), $"{Names[i]}: energy normalization");
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void LargerSources_LowerSpecularPeakNormalization()
        {
            Vector4[] near = Run(2.0f);
            Assert.AreEqual(1.0f, near[0].w, 1e-6f, "Punctual lights are not renormalized.");
            Assert.Less(near[1].w, 1.0f, "Sphere");
            Assert.Less(near[4].w, 1.0f, "Rect");
        }
    }
}
