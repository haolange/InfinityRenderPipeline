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
                var bounds = context.LightBoundsBuffer;
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
                    RGBufferRef handle = factory.ImportBuffer(native, "NativeLightRecords");
                    factory.BeginRender();
                    Assert.AreSame(native, handle.Resolve().graphicsResource);
                    Assert.AreEqual(native.target, handle.Resolve().graphicsTarget);
                    Assert.AreEqual(3, handle.Resolve().descriptor.count);
                    Assert.AreEqual(16, handle.Resolve().descriptor.stride);
                    Assert.Throws<InvalidOperationException>(() => { ComputeBuffer invalid = handle; });
                    new CommandBufferCommands(commands).SetGlobalBuffer(Shader.PropertyToID("TestLightRecords"), handle);
                    Assert.Greater(commands.sizeInBytes, 0);
                    commands.Clear();
                    factory.EndRender();
                    factory.Clear();
                    factory.FlushRetired();
                    Assert.IsTrue(native.IsValid(), "Imported native ownership must remain with LightContext.");
                }
                finally { factory.EndRender(); factory.Dispose(); }
                Assert.IsTrue(native.IsValid());
            }
        }
    }
}
