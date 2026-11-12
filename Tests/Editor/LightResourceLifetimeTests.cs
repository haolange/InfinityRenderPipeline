using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightResourceLifetimeTests
    {
        [Test]
        public void CapacityGrowth_PreservesAllOldBuffersUntilExplicitRetirement()
        {
            using (var context = new LightContext())
            {
                var records = context.LightRecordBuffer;
                var bounds = context.TileRectBuffer;
                var matrices = context.LocalShadowMatrixBuffer;
                var rects = context.LocalShadowRectBuffer;
                context.EnsureCapacity(4, 3, 6);
                Assert.AreEqual(4, context.RetiredBufferCount);
                foreach (var buffer in new[] { records, bounds, matrices, rects }) Assert.IsTrue(buffer.IsValid());
                context.RequireCapacity(4, 3, 6);
                context.EnsureCapacity(2, 1, 2);
                Assert.AreEqual(4, context.RetiredBufferCount);
                context.FlushRetiredBuffers();
                Assert.AreEqual(0, context.RetiredBufferCount);
                foreach (var buffer in new[] { records, bounds, matrices, rects }) Assert.IsFalse(buffer.IsValid());
                Assert.IsTrue(context.LightRecordBuffer.IsValid());
                Assert.IsTrue(context.LocalShadowRectBuffer.IsValid());
            }
        }

        [Test]
        public void ImportedGraphicsBuffer_UsesSameGraphHandleWithoutChangingOwnership()
        {
            using (var native = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16))
            using (var commands = new CommandBuffer())
            {
                var factory = new RGResourceFactory();
                try
                {
                    RGBufferHandle handle = factory.ImportBuffer(native, "NativeLightRecords");
                    factory.BeginRender();
                    Assert.AreSame(native, factory.ResolveBuffer(handle).graphicsResource);
                    Assert.AreEqual(native.target, factory.ResolveBuffer(handle).graphicsTarget);
                    Assert.AreEqual(3, factory.ResolveBuffer(handle).descriptor.count);
                    Assert.AreEqual(16, factory.ResolveBuffer(handle).descriptor.stride);
                    Assert.IsNull(factory.ResolveBuffer(handle).resource, "A native GraphicsBuffer import has no ComputeBuffer.");
                    // The encoder binds the handle through the graph's own factory, the path a recorded pass takes.
                    var encoder = new RGRasterEncoder(commands, factory);
                    encoder.SetGlobalBuffer(Shader.PropertyToID("TestLightRecords"), handle);
                    Assert.Greater(commands.sizeInBytes, 0);
                    commands.Clear();
                    factory.Clear();
                    Assert.Throws<InvalidOperationException>(() => encoder.SetGlobalBuffer(Shader.PropertyToID("TestLightRecords"), handle),
                        "A handle from a cleared graph must be rejected where it is bound.");
                    Assert.AreEqual(0, commands.sizeInBytes);
                    factory.FlushRetired();
                    Assert.IsTrue(native.IsValid(), "Imported native ownership must remain with LightContext.");
                }
                finally { factory.Dispose(); }
                Assert.IsTrue(native.IsValid());
            }
        }
    }
}
