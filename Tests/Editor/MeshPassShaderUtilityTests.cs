using NUnit.Framework;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshPassShaderUtilityTests
    {
        [Test]
        public void FindPassIndex_NullMaterialOrEmptyLightMode_ReturnsInvalid()
        {
            Assert.AreEqual(-1, MeshPassShaderUtility.FindPassIndex(null, "GBufferPass"));
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
