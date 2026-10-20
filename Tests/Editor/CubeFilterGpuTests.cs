using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Runs Compute_CubeFilter (FilterCubeToArray) on small cubemaps: a constant cube stays constant at any roughness
    // (the GGX weights are normalized), and at roughness 0 each array slice is the cube face of the same index
    // (CubemapFaceDirection layout = Unity face order +X, -X, +Y, -Y, +Z, -Z).
    public class CubeFilterGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/CubeFilter/Compute_CubeFilter.compute";
        const int Size = 16;

        static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Compute shaders and async readback are required.");
        }

        static Cubemap MakeCube(System.Func<int, Color> faceColor)
        {
            var cube = new Cubemap(Size, TextureFormat.RGBAFloat, true);
            for (int mip = 0; mip < cube.mipmapCount; mip++)
            {
                int size = Mathf.Max(1, Size >> mip);
                for (int face = 0; face < 6; face++)
                {
                    var pixels = new Color[size * size];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = faceColor(face);
                    cube.SetPixels(pixels, (CubemapFace)face, mip);
                }
            }
            cube.Apply(false);
            return cube;
        }

        static Vector4[][] Filter(Cubemap source, float roughness, float intensity)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            var target = new RenderTexture(new RenderTextureDescriptor(Size, Size, GraphicsFormat.R32G32B32A32_SFloat, 0)
            {
                dimension = TextureDimension.Tex2DArray, volumeDepth = 6, enableRandomWrite = true, mipCount = 1
            });
            target.Create();
            try
            {
                int kernel = shader.FindKernel("FilterCubeToArray");
                shader.SetVector("CubeFilter_Params", new Vector4(Size, 0, roughness, 0));
                shader.SetVector("CubeFilter_Source", new Vector4(Size, source.mipmapCount, intensity, 64));
                shader.SetTexture(kernel, "SRV_CubeSource", source);
                shader.SetTexture(kernel, "UAV_CubeArray", target);
                shader.Dispatch(kernel, (Size + 7) / 8, (Size + 7) / 8, 6);
                var request = AsyncGPUReadback.Request(target, 0, 0, Size, 0, Size, 0, 6, GraphicsFormat.R32G32B32A32_SFloat);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, "Readback failed.");
                var slices = new Vector4[6][];
                for (int s = 0; s < 6; s++)
                {
                    NativeArray<Vector4> data = request.GetData<Vector4>(s);
                    slices[s] = data.ToArray();
                }
                return slices;
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        [TestCase(0.0f)]
        [TestCase(0.5f)]
        [TestCase(1.0f)]
        [Category("Unverified_0_5")]
        public void ConstantCube_StaysConstant_TimesIntensity(float roughness)
        {
            RequireGpu();
            Cubemap cube = MakeCube(face => new Color(0.25f, 0.5f, 1.0f, 1));
            try
            {
                Vector4[][] slices = Filter(cube, roughness, 2.0f);
                foreach (Vector4[] slice in slices)
                {
                    foreach (Vector4 texel in slice)
                    {
                        Assert.AreEqual(0.5f, texel.x, 2e-3f);
                        Assert.AreEqual(1.0f, texel.y, 2e-3f);
                        Assert.AreEqual(2.0f, texel.z, 4e-3f);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void MirrorFilter_KeepsFaceOrder()
        {
            RequireGpu();
            Cubemap cube = MakeCube(face => new Color(face + 1, 0, 0, 1));
            try
            {
                Vector4[][] slices = Filter(cube, 0.0f, 1.0f);
                int center = (Size / 2) * Size + Size / 2;
                for (int face = 0; face < 6; face++)
                {
                    Assert.AreEqual(face + 1, slices[face][center].x, 1e-3f, $"Slice {face} center reads cube face {face}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }
    }
}
