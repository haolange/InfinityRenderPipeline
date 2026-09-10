using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class VisualContractGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/VisualContract.compute";
        static Vector4[] Run(ComputeShader shader, string kernelName, int count)
        {
            using (var output = new ComputeBuffer(count, 16))
            {
                int kernel = shader.FindKernel(kernelName);
                shader.SetBuffer(kernel, "Results", output);
                shader.Dispatch(kernel, count, 1, 1);
                var request = AsyncGPUReadback.Request(output);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);
                return request.GetData<Vector4>().ToArray();
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void HierarchicalRay_HitIsOneActualSurfaceTexel(bool wall)
        {
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            var depth = new Texture2D(64, 64, TextureFormat.RFloat, true, true);
            Vector4 oldZ = Shader.GetGlobalVector("_ZBufferParams"), oldProjection = Shader.GetGlobalVector("_ProjectionParams");
            try
            {
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false);
                float far = SystemInfo.usesReversedZBuffer ? 0 : 1;
                Vector4 surfaceClip = projection * new Vector4(0, 0, -5, 1);
                float surfaceDepth = surfaceClip.z / surfaceClip.w;
                var values = new float[4096];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) values[y * 64 + x] = wall && x >= 32 ? surfaceDepth : far;
                int size = 64;
                for (int mip = 0; mip < 7; mip++)
                {
                    depth.SetPixelData(values, mip);
                    int nextSize = Math.Max(1, size / 2);
                    var next = new float[nextSize * nextSize];
                    for (int y = 0; y < nextSize; y++) for (int x = 0; x < nextSize; x++)
                    {
                        float d = far;
                        for (int oy = 0; oy < Math.Min(2, size); oy++) for (int ox = 0; ox < Math.Min(2, size); ox++)
                            d = SystemInfo.usesReversedZBuffer ? Math.Max(d, values[(y * 2 + oy) * size + x * 2 + ox]) : Math.Min(d, values[(y * 2 + oy) * size + x * 2 + ox]);
                        next[y * nextSize + x] = d;
                    }
                    values = next; size = nextSize;
                }
                depth.Apply(false, false);
                float tangent = Mathf.Tan(30 * Mathf.Deg2Rad);
                Vector3 origin = new Vector3(-0.5f * tangent * 2, 0, -2);
                Vector3 target = new Vector3(0.5f * tangent * 5, 0, -5);
                shader.SetMatrix("Projection", projection);
                shader.SetVector("ScreenSpaceDepthParams", new Vector4(0.1f, 100, 0, SystemInfo.usesReversedZBuffer ? 1 : 0));
                shader.SetVector("Origin", origin); shader.SetVector("Direction", (target - origin).normalized);
                shader.SetFloat("RayDistance", (target - origin).magnitude + 1);
                shader.SetTexture(shader.FindKernel("TraceContract"), "DepthPyramid", depth);
                Shader.SetGlobalVector("_ZBufferParams", SystemInfo.usesReversedZBuffer ? new Vector4(999, 1, 9.99f, 0.01f) : new Vector4(-999, 1000, -9.99f, 10));
                Shader.SetGlobalVector("_ProjectionParams", new Vector4(1, 0.1f, 100, 0.01f));
                Vector4 hit = Run(shader, "TraceContract", 1)[0];
                Assert.AreEqual(wall ? 1 : 0, hit.w);
                if (wall)
                {
                    Assert.That(hit.x, Is.EqualTo(0.75f).Within(1f / 128 + 1e-5f));
                    Assert.That(hit.y, Is.EqualTo(32.5f / 64).Within(1e-5f));
                    Assert.That(hit.z, Is.EqualTo(surfaceDepth).Within(1e-6f));
                }
                else Assert.AreEqual(Vector4.zero, hit);
            }
            finally { Shader.SetGlobalVector("_ZBufferParams", oldZ); Shader.SetGlobalVector("_ProjectionParams", oldProjection); UnityEngine.Object.DestroyImmediate(shader); UnityEngine.Object.DestroyImmediate(depth); }
        }
        [TestCase(0, 0f)]
        [TestCase(0, 0.5f)]
        [TestCase(0, 1f)]
        [TestCase(1, 0f)]
        [TestCase(1, 0.5f)]
        [TestCase(1, 1f)]
        public void MixedShadow_UsesEachChannelAndDistanceMode(int mode, float strength)
        {
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                shader.SetFloat("ShadowStrength", strength);
                shader.SetInt("_InfinityShadowmaskMode", mode); shader.SetFloat("_InfinityShadowDistance", 100);
                Vector4[] values = Run(shader, "MixedShadowContract", 12);
                for (int i = 0; i < values.Length; i++)
                {
                    float baked = 0.1f * (1 + i % 4), fade = i < 4 ? 0 : i < 8 ? 0.5f : 1;
                    float expected = mode == 0 ? baked : Mathf.Lerp(0.6f, baked, fade);
                    Assert.That(values[i].x, Is.EqualTo(Mathf.Lerp(1, expected, strength)).Within(1e-5f));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }
        [TestCase(1f)]
        [TestCase(-1f)]
        public void MeshNormal_NonuniformAndMirroredTransformMatchesInverseTranspose(float handedness)
        {
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                Matrix4x4 transform = Matrix4x4.TRS(new Vector3(4, 7, -3), Quaternion.Euler(23, 46, 17), new Vector3(0.2f * handedness, 3, 1.7f));
                shader.SetMatrix("ObjectToWorld", transform);
                Vector4 value = Run(shader, "MeshNormalContract", 1)[0];
                Vector3 expected = transform.inverse.transpose.MultiplyVector(new Vector3(1, 2, 3).normalized).normalized;
                Assert.That(Vector3.Distance(value, expected), Is.LessThan(1e-5f));
                Assert.AreEqual(handedness, value.w);
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }

        [Test]
        public void ShadowTaps_CompareAtTheReceiverPlaneDepth()
        {
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            var depth = new Texture2D(64, 64, TextureFormat.RFloat, false, true);
            try
            {
                var values = new float[64 * 64];
                float away = SystemInfo.usesReversedZBuffer ? -1e-6f : 1e-6f;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                    values[y * 64 + x] = 0.4f + 0.2f * (x + 0.5f) / 64 + 0.1f * (y + 0.5f) / 64 + away;
                depth.SetPixelData(values, 0); depth.Apply();
                shader.SetTexture(shader.FindKernel("ReceiverPlaneContract"), "ShadowRamp", depth);
                foreach (var result in Run(shader, "ReceiverPlaneContract", 2)) Assert.That(result.x, Is.EqualTo(1).Within(1e-6f));
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); UnityEngine.Object.DestroyImmediate(depth); }
        }

        [Test]
        public void IndirectReplacement_PreservesEnergyAtZeroHalfAndFullConfidence()
        {
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try { foreach (var value in Run(shader, "EnergyContract", 3)) Assert.That(Vector3.Distance(value, new Vector3(1.2f, 2.4f, 3.6f)), Is.LessThan(1e-5f)); }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }
    }
}
