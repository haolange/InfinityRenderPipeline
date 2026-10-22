using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowMomentGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowMomentContract.compute";
        const int CaseCount = 5;

        static Vector4[] Run()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                using (var output = new ComputeBuffer(CaseCount, 16))
                {
                    int kernel = shader.FindKernel("MomentVisibility");
                    shader.SetBuffer(kernel, "Results", output);
                    shader.Dispatch(kernel, CaseCount, 1, 1);
                    var results = new Vector4[CaseCount];
                    output.GetData(results);
                    return results;
                }
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void Moments_LitInFront_ShadowedBehind()
        {
            Vector4[] r = Run();
            for (int i = 0; i < CaseCount; ++i)
            {
                Assert.IsFalse(float.IsNaN(r[i].x) || float.IsNaN(r[i].y), $"case {i}");
            }
            Assert.AreEqual(1.0f, r[0].x, 1e-4f, "VSM in front");
            Assert.AreEqual(1.0f, r[0].y, 1e-4f, "EVSM in front");
            Assert.AreEqual(1.0f, r[1].x, 1e-4f, "VSM at the occluder");
            Assert.AreEqual(1.0f, r[1].y, 1e-4f, "EVSM at the occluder");
            Assert.Less(r[2].x, 0.01f, "VSM behind");
            Assert.Less(r[2].y, 0.01f, "EVSM behind");
            Assert.Less(r[3].y, 0.01f, "EVSM behind both depths of a mixed texel");
            Assert.Less(r[3].x, 0.5f, "VSM bleeding stays below one half after reduction");
            Assert.That(r[4].y, Is.InRange(0.01f, 0.99f), "EVSM penumbra between two depths");
        }
    }
}
