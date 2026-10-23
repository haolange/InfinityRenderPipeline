using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshPassShaderUtilityTests
    {
        [Test]
        public void FindPassIndex_ShaderReplacement_ResolvesCurrentShaderIncludingMissingPass()
        {
            Shader lit = Shader.Find("InfinityPipeline/InfinityLit");
            Shader fullscreen = Shader.Find("InfinityPipeline/Utility/DrawFullScreen");
            Assert.NotNull(lit);
            Assert.NotNull(fullscreen);
            var material = new Material(lit);
            try
            {
                int expected = material.FindPass("RasterForwardPass");
                Assert.GreaterOrEqual(expected, 0);
                Assert.AreEqual(expected, MeshPassShaderUtility.FindPassIndex(material, "RasterForwardPass"));
                material.shader = fullscreen;
                Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(material, "RasterForwardPass"));
                Assert.AreEqual(material.FindPass("RasterDefaultFullScreen"),
                    MeshPassShaderUtility.FindPassIndex(material, "RasterDefaultFullScreen"));
                material.shader = lit;
                Assert.AreEqual(expected, MeshPassShaderUtility.FindPassIndex(material, "RasterForwardPass"));
                Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(material, "RasterDefaultFullScreen"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void FindPassIndex_NullMaterialOrEmptyLightMode_ReturnsInvalid()
        {
            Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(null, "RasterGBufferPass"));
            Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(null, string.Empty));
        }

        [Test]
        public void ResolvePassIndex_EmptyLightMode_UsesFallback()
        {
            Assert.AreEqual(2, MeshPassShaderUtility.ResolvePassIndex(null, null, 2));
            Assert.AreEqual(2, MeshPassShaderUtility.ResolvePassIndex(null, string.Empty, 2));
        }
    }
}
