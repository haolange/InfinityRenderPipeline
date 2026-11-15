using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Texture binding through the graph encoders. The factory imports an external render target identifier, so no
    // GPU resource is created and nothing is executed: the checks cover what the core resolves and what it rejects.
    public class RenderGraphBindingTests
    {
        static readonly int s_ViewProperty = Shader.PropertyToID("SRV_DepthTexture");
        static readonly int s_ViewCompanion = Shader.PropertyToID("SRV_DepthTexture_InfinityView");
        static readonly int s_PlainProperty = Shader.PropertyToID("RenderGraphBindingTests_PlainTexture");
        static readonly RenderTargetIdentifier s_External = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

        [Test]
        public void GraphTexture_BindsItsLogicalAndCapacityViewToAViewProperty()
        {
            var factory = new RGResourceFactory();
            using (var commands = new CommandBuffer())
            {
                try
                {
                    var descriptor = new TextureDescriptor(100, 60) { name = "PaddedDepth", allocationWidth = 128, allocationHeight = 64 };
                    RGTextureHandle texture = factory.ImportBackbuffer(s_External, descriptor);
                    var core = new RGCommandCore(commands, factory);

                    Assert.IsTrue(core.ResolveTexture(s_ViewProperty, texture, 0, out _, out int viewID, out Vector4 view));
                    Assert.AreEqual(s_ViewCompanion, viewID);
                    Assert.AreEqual(new Vector4(100, 60, 128, 64), view);

                    Assert.IsTrue(core.ResolveTexture(s_ViewProperty, texture, 1, out _, out _, out Vector4 mipView));
                    Assert.AreEqual(new Vector4(50, 30, 64, 32), mipView, "A mip halves logical size and capacity together.");

                    Assert.IsFalse(core.ResolveTexture(s_PlainProperty, texture, 0, out _, out _, out _), "A property without a view companion publishes no view.");

                    var encoder = new RGRasterEncoder(commands, factory);
                    encoder.SetGlobalTexture(s_ViewProperty, texture);
                    Assert.Greater(commands.sizeInBytes, 0);
                }
                finally { factory.Dispose(); }
            }
        }

        [Test]
        public void GraphTexture_WithoutLogicalSize_CannotBindToAViewProperty()
        {
            var factory = new RGResourceFactory();
            using (var commands = new CommandBuffer())
            {
                try
                {
                    RGTextureHandle texture = factory.ImportBackbuffer(s_External, new TextureDescriptor { name = "Unsized" });
                    var encoder = new RGRasterEncoder(commands, factory);
                    var error = Assert.Throws<InvalidOperationException>(() => encoder.SetGlobalTexture(s_ViewProperty, texture));
                    StringAssert.Contains("Unsized", error.Message);
                    Assert.AreEqual(0, commands.sizeInBytes, "A rejected binding records nothing.");
                    encoder.SetGlobalTexture(s_PlainProperty, texture);
                }
                finally { factory.Dispose(); }
            }
        }

        [Test]
        public void NonGraphTexture_CannotBindToAViewProperty()
        {
            var factory = new RGResourceFactory();
            using (var commands = new CommandBuffer())
            {
                try
                {
                    var raster = new RGRasterEncoder(commands, factory);
                    var compute = new RGComputeEncoder(commands, factory);
                    var error = Assert.Throws<InvalidOperationException>(() => raster.SetGlobalTexture(s_ViewProperty, s_External));
                    StringAssert.Contains("texture-view property", error.Message);
                    Assert.Throws<InvalidOperationException>(() => compute.SetComputeTextureParam(null, 0, s_ViewProperty, s_External));
                    Assert.Throws<InvalidOperationException>(() => compute.SetComputeTextureParam(null, 0, s_ViewProperty, s_External, 1));
                    Assert.AreEqual(0, commands.sizeInBytes, "A rejected binding records nothing.");

                    raster.SetGlobalTexture(s_PlainProperty, s_External);
                    raster.SetGlobalTexture(s_ViewProperty, s_External, new TextureView(new int2(4, 4), new int2(4, 4)));
                    Assert.Greater(commands.sizeInBytes, 0, "A caller-stated view is the only way to bind a non-graph texture to a view property.");
                }
                finally { factory.Dispose(); }
            }
        }

        [Test]
        public void GraphTexture_FromAClearedGraph_IsRejectedWhereItIsBound()
        {
            var factory = new RGResourceFactory();
            using (var commands = new CommandBuffer())
            {
                try
                {
                    RGTextureHandle texture = factory.ImportBackbuffer(s_External, new TextureDescriptor(4, 4) { name = "Stale" });
                    var encoder = new RGRasterEncoder(commands, factory);
                    factory.Clear();
                    Assert.Throws<InvalidOperationException>(() => encoder.SetGlobalTexture(s_ViewProperty, texture));
                    Assert.Throws<InvalidOperationException>(() => encoder.SetGlobalTexture(s_PlainProperty, texture));
                    Assert.AreEqual(0, commands.sizeInBytes);
                }
                finally { factory.Dispose(); }
            }
        }
    }
}
