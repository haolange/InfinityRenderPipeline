using NUnit.Framework;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DepthStencilFormatTests
    {
        [TestCase("InfinityPipeline/TerrainLit")]
        [TestCase("Hidden/InfinityPipeline/TerrainLitBase")]
        public void TerrainGBufferDefaultsToLessEqualAndWritesDepth(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader, shaderName);
            var material = new Material(shader);
            try
            {
                int passIndex = material.FindPass("RasterGBufferPass");
                Assert.GreaterOrEqual(passIndex, 0);
                Assert.AreEqual(new ShaderTagId("RasterGBufferPass"), shader.FindPassTagValue(passIndex, new ShaderTagId("LightMode")));
                Assert.AreEqual(-1, material.FindPass("RasterDepthPass"));
                Assert.AreEqual((float)CompareFunction.LessEqual, material.GetFloat("_ZTest"));
                Assert.AreEqual(1f, material.GetFloat("_ZWrite"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CreateDepthStencil_SetsExplicitFormatAndHashDistinction()
        {
            var d32s8 = TextureDescriptor.CreateDepthStencil("Depth", 1280, 720, GraphicsFormat.D32_SFloat_S8_UInt, ETextureSizingDomain.Internal);
            var d24s8 = TextureDescriptor.CreateDepthStencil("Depth", 1280, 720, GraphicsFormat.D24_UNorm_S8_UInt, ETextureSizingDomain.Internal);
            var d16 = TextureDescriptor.CreateDepthStencil("Depth", 1280, 720, GraphicsFormat.D16_UNorm, ETextureSizingDomain.Internal);

            Assert.AreEqual(GraphicsFormat.None, d32s8.colorFormat);
            Assert.AreEqual(GraphicsFormat.D32_SFloat_S8_UInt, d32s8.depthStencilFormat);
            Assert.AreNotEqual(d32s8, d24s8);
            Assert.AreNotEqual(d24s8, d16);
            Assert.Greater(d32s8.EstimateBytes(), d24s8.EstimateBytes());
            Assert.Greater(d24s8.EstimateBytes(), d16.EstimateBytes());
        }

        [TestCase(EDepthAccess.ReadOnly, SubPassFlags.ReadOnlyDepthStencil, RenderTargetFlags.ReadOnlyDepthStencil)]
        [TestCase(EDepthAccess.ReadOnlyDepth | EDepthAccess.WriteStencil, SubPassFlags.ReadOnlyDepth, RenderTargetFlags.ReadOnlyDepth)]
        [TestCase(EDepthAccess.WriteDepth | EDepthAccess.ReadOnlyStencil, SubPassFlags.ReadOnlyStencil, RenderTargetFlags.ReadOnlyStencil)]
        [TestCase(EDepthAccess.Write, SubPassFlags.None, RenderTargetFlags.None)]
        public void DepthAccess_MapsToExpectedSubPassAndTargetFlags(EDepthAccess access, SubPassFlags expectedSubPass, RenderTargetFlags expectedTarget)
        {
            Assert.AreEqual(expectedSubPass, RGBuilder.SubPassDepthFlags(access));
            Assert.AreEqual(expectedTarget, RGBuilder.TargetDepthFlags(access));
        }
    }
}
