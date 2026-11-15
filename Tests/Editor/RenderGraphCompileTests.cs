using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderGraphCompileTests
    {
        struct PassData { public RGTextureHandle textureHandle; public RGBufferHandle bufferHandle; }

        static RGBuilder Graph() => new RGBuilder("CompileContract", null, true);

        static TextureDescriptor Color(string name, int size = 4)
        {
            return new TextureDescriptor(size, size)
            {
                name = name, dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite = true
            };
        }

        static string[] Compile(RGBuilder graph)
        {
            typeof(RGBuilder).GetMethod("CompilePass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(graph, null);
            return graph.DescribeCompiledGraph().Split('\n');
        }

        [Test]
        public void Culling_PropagatesAcrossResourceTypes()
        {
            var graph = Graph();
            try
            {
                RGBufferHandle bufferHandle = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "KeptUntilReaderDies" });
                RGTextureHandle textureHandle = graph.CreateTexture(Color("UnusedTexture"));
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("BufferWriter"));
                    passHandle.WriteBuffer(bufferHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("Middle"));
                    passHandle.ReadBuffer(bufferHandle);
                    passHandle.WriteTexture(textureHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                string[] rows = Compile(graph);
                Assert.AreEqual("True", Row(rows, "BufferWriter")[4]);
                Assert.AreEqual("True", Row(rows, "Middle")[4]);
            }
            finally { graph.Dispose(); }
        }

        // ProfilingSampler.Get returns null in non-development players, which have no profiler.
        [Test]
        public void NullSampler_RecordsAnUnnamedPassAndOpensNoScope()
        {
            var graph = Graph();
            try
            {
                var outer = new ProfilingSampler("Outer");
                RGBufferHandle bufferHandle = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "WrittenByUnnamedPass" });
                graph.BeginProfilingSampler(outer);
                graph.BeginProfilingSampler(null);
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(null);
                    passHandle.WriteBuffer(bufferHandle);
                    passHandle.EnablePassCulling(false);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                graph.EndProfilingSampler(null);
                Assert.Throws<InvalidOperationException>(() => graph.EndProfilingSampler(new ProfilingSampler("Other")),
                    "The outer scope is still open after a null scope ends.");
                graph.EndProfilingSampler(outer);
                string[] rows = Compile(graph);
                Assert.AreEqual("False", Row(rows, "")[4], "The unnamed pass is recorded and kept.");
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void StaleHandle_IsRejectedOnTheNextGraph()
        {
            var graph = Graph();
            try
            {
                RGTextureHandle staleHandle = graph.CreateTexture(Color("Stale"));
                graph.ClearRecordedGraph();
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("Next"));
                    Assert.Throws<InvalidOperationException>(() => passHandle.ReadTexture(staleHandle));
                    RGTextureHandle freshHandle = graph.CreateTexture(Color("Fresh"));
                    passHandle.ReadTexture(freshHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void LoadWithoutProducer_Throws()
        {
            var graph = Graph();
            try
            {
                RGTextureHandle depthHandle = graph.CreateTexture(new TextureDescriptor(4, 4)
                {
                    name = "UnwrittenDepth", dimension = TextureDimension.Tex2D, depthBufferBits = EDepthBits.Depth32
                });
                {
                    RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler("LoadDepth"));
                    passHandle.SetDepthStencilAttachment(depthHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                    passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                }
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Compile(graph));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
                StringAssert.Contains("loads", error.InnerException.Message);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void Segment_SplitsWhereACreateWouldTakeAnAllocationReleasedInTheSameSegment()
        {
            var graph = Graph();
            try
            {
                RGTextureHandle firstHandle = graph.CreateTexture(Color("ReuseA"));
                RGTextureHandle secondHandle = graph.CreateTexture(Color("ReuseB"));
                WriteThenRead(graph, "WriteA", "ReadA", firstHandle);
                WriteThenRead(graph, "WriteB", "ReadB", secondHandle);
                Compile(graph);
                RGGraphSnapshot snapshot = graph.CaptureDebugSnapshot();
                int readA = snapshot.passes.FindIndex(pass => pass.name == "ReadA");
                int writeB = snapshot.passes.FindIndex(pass => pass.name == "WriteB");
                Assert.Greater(snapshot.passes[writeB].segment, snapshot.passes[readA].segment,
                    "ReuseB has ReuseA's physical descriptor; the pool would hand it the allocation ReadA released.");
                RGResourceSnapshot a = snapshot.resources.Find(resource => resource.name == "ReuseA");
                Assert.Greater(a.estimatedBytes, 0);
                Assert.AreEqual(a.estimatedBytes, snapshot.peakLiveBytes, "The two textures are never live at the same pass.");
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void DeclaredPasses_SitBeforeTheConsumerGroup_AndDoNotSplitIt()
        {
            var graph = new RGBuilder("DeclaredPlace", null, true);
            try
            {
                RGTextureHandle colorHandle = graph.CreateTexture(Color("Shared"));
                RGBufferHandle firstHandle = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "ProducerAOut" });
                RGBufferHandle secondHandle = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "ProducerBOut" });
                {
                    RGComputePassHandle<PassData> passHandle = graph.DeclareComputePass<PassData>(new ProfilingSampler("ProducerA"));
                    passHandle.EnablePassCulling(false);
                    passHandle.WriteBuffer(firstHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                {
                    RGComputePassHandle<PassData> passHandle = graph.DeclareComputePass<PassData>(new ProfilingSampler("ProducerB"));
                    passHandle.EnablePassCulling(false);
                    passHandle.WriteBuffer(secondHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                RasterReading(graph, "DepthLike", colorHandle, firstHandle);
                RasterReading(graph, "Shade", colorHandle, secondHandle);
                Compile(graph);
                RGGraphSnapshot snapshot = graph.CaptureDebugSnapshot();
                Assert.AreEqual("ProducerA", snapshot.passes[0].name);
                Assert.AreEqual("ProducerB", snapshot.passes[1].name);
                Assert.AreEqual("DepthLike", snapshot.passes[2].name);
                Assert.AreEqual("Shade", snapshot.passes[3].name);
                Assert.AreEqual(snapshot.passes[2].mergeGroup, snapshot.passes[3].mergeGroup);
                Assert.GreaterOrEqual(snapshot.passes[2].mergeGroup, 0);
                Assert.Less(snapshot.passes[0].mergeGroup, 0);
                Assert.IsFalse(graph.CompiledPass(0).enableAsyncCompute);
                Assert.IsFalse(graph.CompiledPass(1).enableAsyncCompute);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void DeclaredPass_WhoseInputIsWrittenInsideTheConsumerGroup_Fails()
        {
            var graph = new RGBuilder("DeclaredConflict", null, false);
            try
            {
                RGTextureHandle colorHandle = graph.CreateTexture(Color("Shared"));
                RGBufferHandle producedHandle = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "DeclaredOut" });
                Raster(graph, "DepthLike", colorHandle, false);
                {
                    RGComputePassHandle<PassData> passHandle = graph.DeclareComputePass<PassData>(new ProfilingSampler("LateProducer"));
                    passHandle.EnablePassCulling(false);
                    passHandle.ReadTexture(colorHandle);
                    passHandle.WriteBuffer(producedHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                {
                    RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler("Shade"));
                    passHandle.EnablePassCulling(false);
                    passHandle.ReadBuffer(producedHandle);
                    passHandle.SetColorAttachment(colorHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                }
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Compile(graph));
                StringAssert.Contains("LateProducer", error.InnerException.Message);
                StringAssert.Contains("Shared", error.InnerException.Message);
            }
            finally { graph.Dispose(); }
        }

        static void RasterReading(RGBuilder graph, string name, RGTextureHandle colorHandle, RGBufferHandle bufferHandle)
        {
            {
                RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler(name));
                passHandle.EnablePassCulling(false);
                RenderBufferLoadAction load = name == "DepthLike" ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.Load;
                passHandle.SetColorAttachment(colorHandle, 0, load, RenderBufferStoreAction.Store);
                passHandle.ReadBuffer(bufferHandle);
                passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
            }
        }

        [Test]
        public void NativePass_MergesAdjacentRasterAndSplitsOnASample()
        {
            var graph = new RGBuilder("Merge", null, false);
            try
            {
                RGTextureHandle colorHandle = graph.CreateTexture(Color("MergedColor"));
                Raster(graph, "DepthLike", colorHandle, false);
                Raster(graph, "Shade", colorHandle, false);
                Compile(graph);
                RGGraphSnapshot merged = graph.CaptureDebugSnapshot();
                Assert.AreEqual(merged.passes[0].mergeGroup, merged.passes[1].mergeGroup);
                Assert.GreaterOrEqual(merged.passes[0].mergeGroup, 0);
                Assert.AreEqual(0, merged.passes[0].subpass);
                Assert.AreEqual(1, merged.passes[1].subpass);
            }
            finally { graph.Dispose(); }

            graph = new RGBuilder("Split", null, false);
            try
            {
                RGTextureHandle colorHandle = graph.CreateTexture(Color("SampledColor"));
                Raster(graph, "Write", colorHandle, false);
                {
                    RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler("Sample"));
                    passHandle.EnablePassCulling(false);
                    passHandle.ReadTexture(colorHandle);
                    passHandle.SetColorAttachment(colorHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                }
                Compile(graph);
                RGGraphSnapshot split = graph.CaptureDebugSnapshot();
                Assert.AreNotEqual(split.passes[0].mergeGroup, split.passes[1].mergeGroup);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void NativePass_SplitsBeforeExceedingEightAttachments()
        {
            var graph = new RGBuilder("AttachmentCap", null, false);
            try
            {
                RGTextureHandle depthHandle = graph.CreateTexture(new TextureDescriptor(4, 4)
                {
                    name = "Depth", dimension = TextureDimension.Tex2D, depthBufferBits = EDepthBits.Depth32
                });
                var colors = new RGTextureHandle[8];
                for (int i = 0; i < colors.Length; ++i)
                    colors[i] = graph.CreateTexture(Color("Color" + i));
                {
                    RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler("Depth"));
                    passHandle.EnablePassCulling(false);
                    passHandle.SetDepthStencilAttachment(depthHandle, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);
                    passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                }
                for (int i = 0; i < colors.Length; ++i)
                {
                    RGTextureHandle colorHandle = colors[i];
                    {
                        RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler("Color" + i));
                        passHandle.EnablePassCulling(false);
                        passHandle.SetDepthStencilAttachment(depthHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                        passHandle.SetColorAttachment(colorHandle, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                        passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                    }
                }
                Compile(graph);
                RGGraphSnapshot snapshot = graph.CaptureDebugSnapshot();
                int first = snapshot.passes[0].mergeGroup;
                Assert.GreaterOrEqual(first, 0);
                for (int i = 1; i < 8; ++i)
                    Assert.AreEqual(first, snapshot.passes[i].mergeGroup);
                Assert.AreNotEqual(first, snapshot.passes[8].mergeGroup);
                Assert.GreaterOrEqual(snapshot.passes[8].mergeGroup, 0);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void Segments_SplitOnQueueChanges()
        {
            var graph = Graph();
            try
            {
                RGBufferHandle bufferHandle = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "QueueBuffer" });
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("GraphicsWrite"));
                    passHandle.EnablePassCulling(false);
                    passHandle.WriteBuffer(bufferHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("AsyncRead"));
                    passHandle.EnableAsyncCompute(true);
                    passHandle.EnablePassCulling(false);
                    passHandle.ReadBuffer(bufferHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                string[] rows = Compile(graph);
                Assert.AreEqual("0", Row(rows, "AsyncRead")[5]);
                Assert.AreNotEqual(Row(rows, "GraphicsWrite")[12], Row(rows, "AsyncRead")[12]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void RecordingArrays_SurviveExecutionTemps()
        {
            var pool = new RGObjectPool();
            int[] recording = pool.GetRecordingArray<int>(2);
            recording[0] = 42;
            int[] temp = pool.GetTempArray<int>(2);
            temp[0] = 1;
            pool.ReleaseAllTempAlloc();
            int[] reused = pool.GetTempArray<int>(2);
            Assert.AreEqual(42, recording[0]);
            reused[0] = 7;
            Assert.AreEqual(42, recording[0]);
            pool.ReleaseRecordingAlloc();
        }

        [Test]
        public void PassLocalResource_CannotBeUsedByAnotherPass()
        {
            var graph = Graph();
            try
            {
                RGBufferHandle localHandle = default;
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("Owner"));
                    localHandle = passHandle.CreatePassLocalBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "PassLocal" });
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                {
                    RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler("Thief"));
                    passHandle.ReadBuffer(localHandle);
                    passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Compile(graph));
                StringAssert.Contains("Pass-local", error.InnerException.Message);
            }
            finally { graph.Dispose(); }
        }

        static void WriteThenRead(RGBuilder graph, string writer, string reader, RGTextureHandle textureHandle)
        {
            {
                RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler(writer));
                passHandle.WriteTexture(textureHandle);
                passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
            }
            {
                RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(new ProfilingSampler(reader));
                passHandle.ReadTexture(textureHandle);
                passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
            }
        }

        static void Raster(RGBuilder graph, string name, RGTextureHandle colorHandle, bool sample)
        {
            {
                RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(new ProfilingSampler(name));
                passHandle.EnablePassCulling(false);
                if (sample) passHandle.ReadTexture(colorHandle);
                // The first producer cannot Load a target nobody has written. Later passes Load that store.
                RenderBufferLoadAction load = name == "DepthLike" ? RenderBufferLoadAction.Clear
                    : name == "Write" ? RenderBufferLoadAction.DontCare
                    : RenderBufferLoadAction.Load;
                passHandle.SetColorAttachment(colorHandle, 0, load, RenderBufferStoreAction.Store);
                passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
            }
        }

        [Test]
        public void ProfilingScope_BeginsOnFirstRasterPassAndEndsOnLastInOneSegment()
        {
            var graph = Graph();
            var parent = new ProfilingSampler("ParentGroup");
            var first = new ProfilingSampler("RasterA");
            var second = new ProfilingSampler("RasterB");
            try
            {
                RGTextureHandle colorHandle = graph.CreateTexture(Color("ScopeColor"));
                using (new RGProfilingScope(graph, parent))
                {
                    {
                        RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(first);
                        passHandle.EnablePassCulling(false);
                        passHandle.SetColorAttachment(colorHandle, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                        passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                    }
                    {
                        RGRasterPassHandle<PassData> passHandle = graph.AddRasterPass<PassData>(second);
                        passHandle.EnablePassCulling(false);
                        passHandle.SetColorAttachment(colorHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                        passHandle.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                    }
                }
                Compile(graph);
                RGPassCompileInfo a = graph.CompiledPass(0);
                RGPassCompileInfo b = graph.CompiledPass(1);
                Assert.AreEqual(a.segment, b.segment);
                Assert.IsTrue(HasSampler(a.beginSamples, parent));
                Assert.IsFalse(HasSampler(a.endSamples, parent));
                Assert.IsFalse(HasSampler(b.beginSamples, parent));
                Assert.IsTrue(HasSampler(b.endSamples, parent));
                Assert.AreSame(first, a.pass.customSampler);
                Assert.AreSame(second, b.pass.customSampler);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void ProfilingScope_ClosesAndReopensWhenTheQueueSplitsTheSegment()
        {
            var graph = Graph();
            var parent = new ProfilingSampler("SplitGroup");
            var graphics = new ProfilingSampler("GraphicsPass");
            var asyncFirst = new ProfilingSampler("AsyncFirst");
            var asyncLast = new ProfilingSampler("AsyncLast");
            try
            {
                using (new RGProfilingScope(graph, parent))
                {
                    {
                        RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(graphics);
                        passHandle.EnablePassCulling(false);
                        passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                    }
                    {
                        RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(asyncFirst);
                        passHandle.EnableAsyncCompute(true);
                        passHandle.EnablePassCulling(false);
                        passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                    }
                    {
                        RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(asyncLast);
                        passHandle.EnableAsyncCompute(true);
                        passHandle.EnablePassCulling(false);
                        passHandle.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                    }
                }
                Compile(graph);
                RGPassCompileInfo a = graph.CompiledPass(0);
                RGPassCompileInfo b = graph.CompiledPass(1);
                RGPassCompileInfo c = graph.CompiledPass(2);
                Assert.AreNotEqual(a.segment, b.segment);
                Assert.AreEqual(b.segment, c.segment);
                Assert.IsTrue(HasSampler(a.beginSamples, parent));
                Assert.IsTrue(HasSampler(a.endSamples, parent));
                Assert.IsTrue(HasSampler(b.beginSamples, parent));
                Assert.IsFalse(HasSampler(b.endSamples, parent));
                Assert.IsFalse(HasSampler(c.beginSamples, parent));
                Assert.IsTrue(HasSampler(c.endSamples, parent));
                Assert.AreSame(graphics, a.pass.customSampler);
                Assert.AreSame(asyncFirst, b.pass.customSampler);
                Assert.AreSame(asyncLast, c.pass.customSampler);
            }
            finally { graph.Dispose(); }
        }

        static bool HasSampler(System.Collections.Generic.List<ProfilingSampler> samples, ProfilingSampler sampler)
        {
            if (samples == null) return false;
            for (int i = 0; i < samples.Count; ++i)
            {
                if (ReferenceEquals(samples[i], sampler)) return true;
            }
            return false;
        }

        static string[] Row(string[] rows, string name)
        {
            foreach (string row in rows)
            {
                if (row.StartsWith("index", StringComparison.Ordinal) || row.Length == 0) continue;
                string[] cells = row.Split('|');
                if (cells.Length > 1 && cells[1] == name) return cells;
            }
            Assert.Fail("Missing pass " + name);
            return null;
        }
    }
}
