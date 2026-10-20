using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderGraphViewerTests
    {
        struct ViewerPassData { public RGTextureRef texture; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void StopBeforeSubmit() => throw new InvalidOperationException("Viewer contract stops before GPU submission.");

        static TextureDescriptor Descriptor(string name, int size)
        {
            return new TextureDescriptor(size, size)
            {
                name = name, dimension = TextureDimension.Tex2D, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true
            };
        }

        [Test]
        [Category("Unverified_0_5")]
        public void Snapshot_ListsPassesLifetimesAndCulling()
        {
            var graph = new RGBuilder("ViewerContract") { debugLabel = "ViewerCamera" };
            var resources = new ResourcePool();
            var frameCommands = new CommandBuffer();
            RGGraphSnapshot captured = null;
            Action onChanged = () => RGDebugSession.latest.TryGetValue("ViewerCamera", out captured);
            RGDebugSession.captureEnabled = true;
            RGDebugSession.changed += onChanged;
            try
            {
                RGTextureRef texture = graph.CreateTexture(Descriptor("ViewerTexture", 8));
                RGTextureRef unused = graph.CreateTexture(Descriptor("ViewerUnused", 4));
                using (RGComputePassRef pass = graph.AddComputePass<ViewerPassData>(new ProfilingSampler("ViewerProducer")))
                {
                    pass.EnablePassCulling(false);
                    pass.GetPassData<ViewerPassData>().texture = pass.WriteTexture(texture);
                    pass.SetExecuteFunc((in ViewerPassData data, in RGComputeEncoder commands, RGObjectPool pool) => { });
                }
                using (RGComputePassRef pass = graph.AddComputePass<ViewerPassData>(new ProfilingSampler("ViewerCulled")))
                {
                    pass.EnablePassCulling(true);
                    pass.GetPassData<ViewerPassData>().texture = pass.WriteTexture(unused);
                    pass.SetExecuteFunc((in ViewerPassData data, in RGComputeEncoder commands, RGObjectPool pool) => { });
                }
                using (RGTransferPassRef pass = graph.AddTransferPass<ViewerPassData>(new ProfilingSampler("ViewerConsumer")))
                {
                    pass.EnablePassCulling(false);
                    pass.GetPassData<ViewerPassData>().texture = pass.ReadTexture(texture);
                    pass.SetExecuteFunc((in ViewerPassData data, in RGTransferEncoder commands, RGObjectPool pool) => StopBeforeSubmit());
                }

                Assert.Throws<InvalidOperationException>(() => graph.Execute(default, resources, frameCommands));
                Assert.IsNotNull(captured, "The snapshot is published after compilation, before any pass executes.");
                Assert.AreEqual(3, captured.passes.Count);
                Assert.AreEqual("ViewerProducer", captured.passes[0].name);
                Assert.IsFalse(captured.passes[0].culled);
                Assert.IsTrue(captured.passes[1].culled, "A cullable pass whose output nobody reads is culled.");
                Assert.IsFalse(captured.passes[2].culled);

                int written = captured.resources.FindIndex(r => r.name == "ViewerTexture");
                int dropped = captured.resources.FindIndex(r => r.name == "ViewerUnused");
                Assert.GreaterOrEqual(written, 0);
                Assert.GreaterOrEqual(dropped, 0);
                RGResourceSnapshot resource = captured.resources[written];
                CollectionAssert.Contains(captured.passes[0].writes, written);
                CollectionAssert.Contains(captured.passes[2].reads, written);
                Assert.AreEqual(0, resource.firstUse);
                Assert.AreEqual(2, resource.lastUse);
                Assert.AreEqual(0, resource.createPass);
                Assert.AreEqual(2, resource.releasePass);
                StringAssert.Contains("8x8", resource.description);
                Assert.AreEqual(-1, captured.resources[dropped].firstUse, "Resources of culled passes are never live.");
                StringAssert.Contains("ViewerConsumer", captured.ToText());
            }
            finally
            {
                RGDebugSession.changed -= onChanged;
                RGDebugSession.captureEnabled = false;
                RGDebugSession.Clear();
                graph.FlushRetiredResources();
                graph.Dispose(); resources.Dispose(); frameCommands.Dispose();
            }
        }
    }
}
