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
                // Passes resolve by LightMode tag only: the final-output pass is named RasterFinalOutput but carries
                // no such LightMode, so a name match (Material.FindPass) is not a mesh pass.
                Assert.GreaterOrEqual(material.FindPass("RasterFinalOutput"), 0);
                Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(material, "RasterFinalOutput"));
                material.shader = lit;
                Assert.AreEqual(expected, MeshPassShaderUtility.FindPassIndex(material, "RasterForwardPass"));
                Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(material, "RasterFinalOutput"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void FindPassIndex_RejectsMissingMaterialOrLightMode()
        {
            var material = new Material(Shader.Find("InfinityPipeline/InfinityLit"));
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
        public void MeshInstancing_IsDeclaredByInfinityLit()
        {
            Shader lit = Shader.Find("InfinityPipeline/InfinityLit");
            Shader fullscreen = Shader.Find("InfinityPipeline/Utility/DrawFullScreen");
            Assert.NotNull(lit);
            Assert.NotNull(fullscreen);
            Assert.IsTrue(MeshPassShaderUtility.SupportsMeshInstancing(lit));
            Assert.IsFalse(MeshPassShaderUtility.SupportsMeshInstancing(fullscreen));
            Assert.IsFalse(MeshPassShaderUtility.SupportsMeshInstancing(null));
        }
    }
}
