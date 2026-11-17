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
                Assert.IsFalse(scoper.TryQueryTexture(12345, out RGTextureHandle textureHandle));
                Assert.IsFalse(textureHandle.IsValid());
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

        [Test]
        public void RegisterTexture_DifferentHandles_Throws()
        {
            var scoper = new RGScoper(null);
            try
            {
                scoper.RegisterTexture(7, new RGTextureHandle(1, 1));
                Assert.Throws<InvalidOperationException>(() => scoper.RegisterTexture(7, new RGTextureHandle(2, 1)));
            }
            finally
            {
                scoper.Dispose();
            }
        }

        [Test]
        public void RegisterTexture_SameHandle_IsAllowed()
        {
            var scoper = new RGScoper(null);
            try
            {
                var textureHandle = new RGTextureHandle(3, 1);
                scoper.RegisterTexture(9, textureHandle);
                Assert.DoesNotThrow(() => scoper.RegisterTexture(9, textureHandle));
            }
            finally
            {
                scoper.Dispose();
            }
        }

    }
}
