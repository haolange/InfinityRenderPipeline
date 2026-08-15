using System;
using NUnit.Framework;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.RenderGraph.Tests
{
    public class RGScoperTests
    {
        [Test]
        public void QueryTexture_UnregisteredHandle_Throws()
        {
            var scoper = new RGScoper(null);
            try
            {
                Assert.Throws<InvalidOperationException>(() => scoper.QueryTexture(12345));
            }
            finally
            {
                scoper.Dispose();
            }
        }

        [Test]
        public void TryQueryTexture_UnregisteredHandle_ReturnsFalse()
        {
            var scoper = new RGScoper(null);
            try
            {
                Assert.IsFalse(scoper.TryQueryTexture(12345, out RGTextureRef textureRef));
                Assert.IsFalse(textureRef.IsValid());
            }
            finally
            {
                scoper.Dispose();
            }
        }

        [Test]
        public void QueryBuffer_UnregisteredHandle_Throws()
        {
            var scoper = new RGScoper(null);
            try
            {
                Assert.Throws<InvalidOperationException>(() => scoper.QueryBuffer(12345));
            }
            finally
            {
                scoper.Dispose();
            }
        }
    }
}
