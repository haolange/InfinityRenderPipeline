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
        struct PassData { public RGTextureHandle texture; public RGBufferHandle buffer; }

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
                RGBufferHandle buffer = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "KeptUntilReaderDies" });
                RGTextureHandle texture = graph.CreateTexture(Color("UnusedTexture"));
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("BufferWriter")))
                {
                    pass.WriteBuffer(buffer);
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("Middle")))
                {
                    pass.ReadBuffer(buffer);
                    pass.WriteTexture(texture);
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                string[] rows = Compile(graph);
                Assert.AreEqual("True", Row(rows, "BufferWriter")[4]);
                Assert.AreEqual("True", Row(rows, "Middle")[4]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void StaleHandle_IsRejectedOnTheNextGraph()
        {
            var graph = Graph();
            try
            {
                RGTextureHandle stale = graph.CreateTexture(Color("Stale"));
                graph.ClearRecordedGraph();
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("Next")))
                {
                    Assert.Throws<InvalidOperationException>(() => pass.ReadTexture(stale));
                    RGTextureHandle fresh = graph.CreateTexture(Color("Fresh"));
                    pass.ReadTexture(fresh);
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
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
                RGTextureHandle depth = graph.CreateTexture(new TextureDescriptor(4, 4)
                {
                    name = "UnwrittenDepth", dimension = TextureDimension.Tex2D, depthBufferBits = EDepthBits.Depth32
                });
                using (var pass = graph.AddRasterPass<PassData>(new ProfilingSampler("LoadDepth")))
                {
                    pass.SetDepthStencilAttachment(depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                    pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                }
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Compile(graph));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
                StringAssert.Contains("loads", error.InnerException.Message);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void AliasPlan_ReusesOneSlotForNonOverlappingTextures()
        {
            var graph = Graph();
            try
            {
                RGTextureHandle first = graph.CreateTexture(Color("AliasA"));
                RGTextureHandle second = graph.CreateTexture(Color("AliasB"));
                WriteThenRead(graph, "WriteA", "ReadA", first);
                WriteThenRead(graph, "WriteB", "ReadB", second);
                Compile(graph);
                RGGraphSnapshot snapshot = graph.CaptureDebugSnapshot();
                RGResourceSnapshot a = snapshot.resources.Find(resource => resource.name == "AliasA");
                RGResourceSnapshot b = snapshot.resources.Find(resource => resource.name == "AliasB");
                Assert.GreaterOrEqual(a.aliasSlot, 0);
                Assert.AreEqual(a.aliasSlot, b.aliasSlot);
                Assert.AreEqual(a.aliasGroup, b.aliasGroup);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void NativePass_MergesAdjacentRasterAndSplitsOnASample()
        {
            var graph = new RGBuilder("Merge", null, false);
            try
            {
                RGTextureHandle color = graph.CreateTexture(Color("MergedColor"));
                Raster(graph, "DepthLike", color, false);
                Raster(graph, "Shade", color, false);
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
                RGTextureHandle color = graph.CreateTexture(Color("SampledColor"));
                Raster(graph, "Write", color, false);
                using (var pass = graph.AddRasterPass<PassData>(new ProfilingSampler("Sample")))
                {
                    pass.EnablePassCulling(false);
                    pass.ReadTexture(color);
                    pass.SetColorAttachment(color, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
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
                RGTextureHandle depth = graph.CreateTexture(new TextureDescriptor(4, 4)
                {
                    name = "Depth", dimension = TextureDimension.Tex2D, depthBufferBits = EDepthBits.Depth32
                });
                var colors = new RGTextureHandle[8];
                for (int i = 0; i < colors.Length; ++i)
                    colors[i] = graph.CreateTexture(Color("Color" + i));
                using (var pass = graph.AddRasterPass<PassData>(new ProfilingSampler("Depth")))
                {
                    pass.EnablePassCulling(false);
                    pass.SetDepthStencilAttachment(depth, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);
                    pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                }
                for (int i = 0; i < colors.Length; ++i)
                {
                    RGTextureHandle color = colors[i];
                    using (var pass = graph.AddRasterPass<PassData>(new ProfilingSampler("Color" + i)))
                    {
                        pass.EnablePassCulling(false);
                        pass.SetDepthStencilAttachment(depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                        pass.SetColorAttachment(color, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                        pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
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
                RGBufferHandle buffer = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "QueueBuffer" });
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("GraphicsWrite")))
                {
                    pass.EnablePassCulling(false);
                    pass.WriteBuffer(buffer);
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("AsyncRead")))
                {
                    pass.EnableAsyncCompute(true);
                    pass.EnablePassCulling(false);
                    pass.ReadBuffer(buffer);
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
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
                RGBufferHandle local = default;
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("Owner")))
                {
                    local = pass.CreatePassLocalBuffer(new BufferDescriptor(1, sizeof(uint)) { name = "PassLocal" });
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("Thief")))
                {
                    pass.ReadBuffer(local);
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
                }
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Compile(graph));
                StringAssert.Contains("Pass-local", error.InnerException.Message);
            }
            finally { graph.Dispose(); }
        }

        static void WriteThenRead(RGBuilder graph, string writer, string reader, RGTextureHandle texture)
        {
            using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler(writer)))
            {
                pass.WriteTexture(texture);
                pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
            }
            using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler(reader)))
            {
                pass.ReadTexture(texture);
                pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands) => { });
            }
        }

        static void Raster(RGBuilder graph, string name, RGTextureHandle color, bool sample)
        {
            using (var pass = graph.AddRasterPass<PassData>(new ProfilingSampler(name)))
            {
                pass.EnablePassCulling(false);
                if (sample) pass.ReadTexture(color);
                // The first producer cannot Load a target nobody has written. Later passes Load that store.
                RenderBufferLoadAction load = name == "DepthLike" ? RenderBufferLoadAction.Clear
                    : name == "Write" ? RenderBufferLoadAction.DontCare
                    : RenderBufferLoadAction.Load;
                pass.SetColorAttachment(color, 0, load, RenderBufferStoreAction.Store);
                pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
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
                RGTextureHandle color = graph.CreateTexture(Color("ScopeColor"));
                using (new RGProfilingScope(graph, parent))
                {
                    using (var pass = graph.AddRasterPass<PassData>(first))
                    {
                        pass.EnablePassCulling(false);
                        pass.SetColorAttachment(color, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                        pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
                    }
                    using (var pass = graph.AddRasterPass<PassData>(second))
                    {
                        pass.EnablePassCulling(false);
                        pass.SetColorAttachment(color, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                        pass.SetExecuteFunc((in PassData data, in RGRasterEncoder commands) => { });
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
                    using (var pass = graph.AddComputePass<PassData>(graphics))
                        pass.EnablePassCulling(false);
                    using (var pass = graph.AddComputePass<PassData>(asyncFirst))
                    {
                        pass.EnableAsyncCompute(true);
                        pass.EnablePassCulling(false);
                    }
                    using (var pass = graph.AddComputePass<PassData>(asyncLast))
                    {
                        pass.EnableAsyncCompute(true);
                        pass.EnablePassCulling(false);
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
