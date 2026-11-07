using System;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.MeshPipeline;
using Object = UnityEngine.Object;

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
        public void FindPassIndex_RejectsMissingMaterialOrLightMode()
        {
            var material = new Material(Shader.Find("InfinityPipeline/InfinityLit-Instanced"));
            try
            {
                Assert.Throws<ArgumentNullException>(() => MeshPassShaderUtility.FindPassIndex(null, "RasterGBufferPass"));
                Assert.Throws<ArgumentException>(() => MeshPassShaderUtility.FindPassIndex(material, string.Empty));
                Assert.Throws<ArgumentException>(() => MeshPassShaderUtility.FindPassIndex(material, null));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MeshInstancing_IsDeclaredByTheInstancedShaderOnly()
        {
            Shader instanced = Shader.Find("InfinityPipeline/InfinityLit-Instanced");
            Shader lit = Shader.Find("InfinityPipeline/InfinityLit");
            Assert.NotNull(instanced);
            Assert.NotNull(lit);
            Assert.IsTrue(MeshPassShaderUtility.SupportsMeshInstancing(instanced));
            Assert.IsFalse(MeshPassShaderUtility.SupportsMeshInstancing(lit), "Renderer-list shaders do not read mesh entity data.");
            Assert.IsFalse(MeshPassShaderUtility.SupportsMeshInstancing(null));
        }
    }
}
