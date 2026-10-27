using System;
using System.Collections.Generic;
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
        struct ViewerPassData { public RGTextureHandle texture; }

        [Test]
        public void CaptureProvenance_DistinguishesPersistentImportFromMissingProducer()
        {
            var graph = new RGBuilder("CaptureProvenance");
            var texture = new RenderTexture(4, 4, 0) { name = "PersistentCaptureSource" };
            RTHandle handle = RTHandles.Alloc(texture);
            var buffer = new ComputeBuffer(1, sizeof(uint));
            try
            {
                graph.ImportBuffer(new BufferHandle(-1, new BufferDescriptor { count = 1, stride = sizeof(uint) }, buffer));
                Assert.Throws<InvalidOperationException>(() => graph.GetTextureProducer(default, out _));
                Assert.Throws<InvalidOperationException>(() => graph.GetBufferProducer(default, out _));
                TextureDescriptor descriptor = Descriptor("PersistentCaptureSource", 4);
                RGTextureHandle imported = graph.ImportTexture(new TextureHandle(-1, descriptor, handle));
                Assert.AreEqual(descriptor, graph.GetTextureDescriptor(imported));
                StringAssert.StartsWith("Imported: ", graph.GetTextureProducer(imported, out string queue));
                Assert.AreEqual("External", queue);
                RGTextureHandle unproduced = graph.CreateTexture(Descriptor("Unproduced", 4));
                Assert.Throws<InvalidOperationException>(() => graph.GetTextureProducer(unproduced, out _));
                using (RGComputePassHandle pass = graph.AddComputePass<ViewerPassData>(new ProfilingSampler("CurrentProducer")))
                {
                    pass.GetPassData<ViewerPassData>().texture = pass.WriteTexture(imported);
                }
                Assert.AreEqual("CurrentProducer", graph.GetTextureProducer(imported, out queue));
                Assert.AreEqual("Graphics", queue);
            }
            finally
            {
                graph.Dispose();
                buffer.Release();
                handle.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        const string StopMessage = "Viewer contract stops before GPU submission.";

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void StopBeforeSubmit() => throw new InvalidOperationException(StopMessage);

        static TextureDescriptor Descriptor(string name, int size)
        {
            return new TextureDescriptor(size, size)
            {
                name = name, dimension = TextureDimension.Tex2D, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true
            };
        }

        [Test]
        public void Snapshot_ListsPassesLifetimesAndCulling()
        {
            string label = "ViewerContract-" + Guid.NewGuid().ToString("N");
            var graph = new RGBuilder("ViewerContract") { debugLabel = label };
            var resources = new ResourcePool();
            var frameCommands = new CommandBuffer();
            RGGraphSnapshot captured = null;
            bool executed = false;
            bool previousCapture = RGDebugSession.captureEnabled;
            bool previousPause = RGDebugSession.paused;
            var previousSnapshots = new List<RGGraphSnapshot>(RGDebugSession.latest.Values);
            Action onChanged = () =>
            {
                if (RGDebugSession.latest.TryGetValue(label, out captured))
                    StopBeforeSubmit(); // The snapshot is published before any pass can queue GPU commands.
            };
            RGDebugSession.captureEnabled = true;
            RGDebugSession.paused = false;
            RGDebugSession.changed += onChanged;
            try
            {
                RGTextureHandle texture = graph.CreateTexture(Descriptor("ViewerTexture", 8));
                RGTextureHandle unused = graph.CreateTexture(Descriptor("ViewerUnused", 4));
                using (RGComputePassHandle pass = graph.AddComputePass<ViewerPassData>(new ProfilingSampler("ViewerProducer")))
                {
                    pass.EnablePassCulling(false);
                    pass.GetPassData<ViewerPassData>().texture = pass.WriteTexture(texture);
                    pass.SetExecuteFunc((in ViewerPassData data, in RGComputeEncoder commands) => { executed = true; });
                }
                using (RGComputePassHandle pass = graph.AddComputePass<ViewerPassData>(new ProfilingSampler("ViewerCulled")))
                {
                    pass.GetPassData<ViewerPassData>().texture = pass.WriteTexture(unused);
                    pass.SetExecuteFunc((in ViewerPassData data, in RGComputeEncoder commands) => { executed = true; });
                }
                using (RGTransferPassHandle pass = graph.AddTransferPass<ViewerPassData>(new ProfilingSampler("ViewerConsumer")))
                {
                    pass.EnablePassCulling(false);
                    pass.GetPassData<ViewerPassData>().texture = pass.ReadTexture(texture);
                    pass.SetExecuteFunc((in ViewerPassData data, in RGTransferEncoder commands) => { executed = true; StopBeforeSubmit(); });
                }

                var stopped = Assert.Throws<InvalidOperationException>(() => graph.Execute(default, frameCommands));
                Assert.AreEqual(StopMessage, stopped.Message);
                Assert.IsFalse(executed, "The compiled-snapshot contract must stop before every pass executes.");
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
                try
                {
                    graph.FlushRetiredResources();
                    graph.Dispose(); resources.Dispose(); frameCommands.Dispose();
                }
                finally
                {
                    RGDebugSession.paused = false;
                    try
                    {
                        RGDebugSession.Clear();
                        foreach (RGGraphSnapshot snapshot in previousSnapshots) RGDebugSession.Publish(snapshot);
                    }
                    finally
                    {
                        RGDebugSession.captureEnabled = previousCapture;
                        RGDebugSession.paused = previousPause;
                    }
                }
            }
        }
    }
}
