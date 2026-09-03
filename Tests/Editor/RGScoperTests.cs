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

        [Test]
        public void RegisterTexture_DifferentRefs_Throws()
        {
            var scoper = new RGScoper(null);
            try
            {
                scoper.RegisterTexture(7, new RGTextureRef(1));
                Assert.Throws<InvalidOperationException>(() => scoper.RegisterTexture(7, new RGTextureRef(2)));
            }
            finally
            {
                scoper.Dispose();
            }
        }

        [Test]
        public void RegisterTexture_SameRef_IsAllowed()
        {
            var scoper = new RGScoper(null);
            try
            {
                var textureRef = new RGTextureRef(3);
                scoper.RegisterTexture(9, textureRef);
                Assert.DoesNotThrow(() => scoper.RegisterTexture(9, textureRef));
            }
            finally
            {
                scoper.Dispose();
            }
        }

        [Test]
        public void MoveTexture_UnregistersSource()
        {
            var scoper = new RGScoper(null);
            try
            {
                scoper.RegisterTexture(1, new RGTextureRef(10));
                scoper.MoveTexture(1, 2);
                Assert.IsFalse(scoper.TryQueryTexture(1, out _));
                Assert.IsTrue(scoper.TryQueryTexture(2, out RGTextureRef moved));
                Assert.IsTrue(moved.IsValid());
                Assert.AreEqual(10, moved.handle.index);
            }
            finally
            {
                scoper.Dispose();
            }
        }

        [Test]
        public void MoveTexture_TargetAlreadyOwned_Throws()
        {
            var scoper = new RGScoper(null);
            try
            {
                scoper.RegisterTexture(1, new RGTextureRef(10));
                scoper.RegisterTexture(2, new RGTextureRef(20));
                Assert.Throws<InvalidOperationException>(() => scoper.MoveTexture(1, 2));
            }
            finally
            {
                scoper.Dispose();
            }
        }
    }
}
